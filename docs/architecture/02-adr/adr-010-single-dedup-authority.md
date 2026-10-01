# ADR-010 — One dedup authority; retire `FileDownloadIndex`

- **Status:** Proposed
- **Date:** 2026-10-01
- **Review:** Composer 2.5 re-run — read-only verification on `enterprise-readiness` @ `1c3753e`; no build
- **Deciders:** Architecture
- **Related:** ADR-002, ADR-005, ADR-007

## Context

Deduplication is the feature that makes this product usable — without it, every restart or rescan re-downloads terabytes. There are currently **two independent authorities**, and they diverge permanently.

**Authority A — `FileDownloadIndex`.** A `static` class with a `static Timer` (`TelegramClient/FileDownloadIndex.cs:21-35`) holding a `HashSet<long>` of Telegram `document_id`s, flushed to `%APPDATA%/TelegramAutoDownload/downloaded_ids.json` every 10 seconds (`:33-35`) by rewriting the **entire** set (`:109-121`). It lives in `TelegramClient` and is consulted by the download pipeline.

**Authority B — SQLite.** `MediaRepository.IsKnownDocumentAsync` unions `Media WHERE document_id = ? AND status = 'done'` with `LegacyDedup` (`Data/MediaRepository.cs:28-37`). It is consulted by `ExistingDownloadValidator` on the orchestrator's preflight path (`Scanning/ExistingDownloadValidator.cs:37-38`, called from `DownloadOrchestrator.cs:113`).

**They are synchronised exactly once, ever.** `LegacyDedupMigration.RunAsync` imports the JSON into `LegacyDedup` but early-returns whenever the table is already non-empty:

```csharp
var existing = await repo.CountLegacyDedupAsync();
if (existing > 0) { Log.Debug(...); return; }      // LegacyDedupMigration.cs:17-22
```

Meanwhile `FileDownloadIndex` keeps appending to that same JSON file for the life of the deployment. **From the second startup onward the two stores diverge monotonically.** Symptoms: files known to A but not B are re-downloaded by the orchestrator; files known to B but not A are re-downloaded by the live path. Both are silent.

Three further problems:

1. **O(n) rewrite every 10 seconds.** At the p99 assumption of 2M downloaded documents, serialising a `List<long>` of 2M entries is roughly a 20 MB write every 10 s — about 170 GB/day of pointless disk writes. *(Estimate: ~10 bytes per decimal ID plus separators.)*
2. **Corruption silently empties the index.** `Load()` is wrapped in `catch { /* start with empty index on corruption */ }` (`FileDownloadIndex.cs:105`). A truncated file means the user's entire archive is re-downloaded with no error and no log.
3. **A static with a static timer is untestable and unconfigurable.** The path is a hardcoded static readonly field (`:23-25`); there is no way to inject a temp path, no way to disable the timer in a test, and no way to honour `DATA_DIR`.

There is also a *third*, weaker dedup signal: `ExistingDownloadValidator.FindOnDisk` runs `Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories)` per queued item (`ExistingDownloadValidator.cs:96`) — a full recursive directory walk on the hot path, which at p99 folder sizes is the dominant preflight cost.

## Decision

**SQLite is the single dedup authority. `FileDownloadIndex` is retired.**

### 1. One lookup path

All dedup checks — live path and orchestrator path — go through `IMediaStore.IsKnownDocumentAsync`, which keeps the existing union of `Media(status='done')` and `LegacyDedup` (`MediaRepository.cs:28-37`) and is served by the read connection pool from ADR-002. The index `IX_Media_DocumentId` already exists (`001_initial.sql:37`).

### 2. Continuous reconciliation during the transition

Replace the one-shot import (`LegacyDedupMigration.cs:17-22`) with a reconciler that runs on every startup **for one release**:

- Compare the JSON file's checksum against the last-imported checksum stored in `MigrationLog` (ADR-007).
- On difference, import the delta with `INSERT OR IGNORE` — which `BulkInsertLegacyDedupAsync` already does correctly inside one transaction (`MediaRepository.cs:332-341`).
- Log the delta count. A non-zero delta after the write path has been switched is a bug signal and should be alerted on.

This closes the divergence window without requiring a flag day.

### 3. Retirement sequence

1. **Release N:** SQLite becomes the authority for *reads*; `FileDownloadIndex` keeps writing (dual-write) so a rollback is safe. The reconciler runs each startup. Emit a metric for the delta.
2. **Release N+1:** if the delta has been zero across the fleet, stop writing to `FileDownloadIndex` and mark it `[Obsolete]`.
3. **Release N+2:** delete the class and the timer. Keep `downloaded_ids.json` on disk, untouched, as a permanent safety copy — deleting a user's dedup index is unrecoverable and the file costs nothing.

### 4. Content hash as a secondary key

Add `content_hash` (ADR-005, migration `002`) recorded on completion. `document_id` is the primary dedup key and is correct for native Telegram media, but plugin downloads (yt-dlp, torrent) have **no** `document_id` — the schema falls back to `url_hash` (`001_initial.sql:15`). A content hash catches the same file arriving via different URLs, which `url_hash` cannot.

### 5. Replace the recursive disk walk

`ExistingDownloadValidator.FindOnDisk`'s `AllDirectories` enumeration (`ExistingDownloadValidator.cs:96`) is replaced by an indexed `downloaded_path` lookup, falling back to a **single** `File.Exists` at the expected path (the direct check already present at `:92-94`). The recursive walk is retained only behind an explicit "repair index" admin action.

## Consequences

**Positive**

- Silent duplicate downloads and silent false skips both stop. At p99 scale a single false-negative rescan is terabytes of wasted transfer and a large FLOOD_WAIT exposure, so this is a bandwidth *and* a ban-risk fix.
- Removes ~170 GB/day of unnecessary disk writes at p99. *(Estimate.)*
- Dedup state joins the backup and integrity-check regime from ADR-002. Today the JSON index has neither, and it silently empties on corruption.
- Deleting a `static` class with a `static Timer` removes one of the two worst testability blockers in `TelegramClient`.
- Replacing the per-item recursive directory walk removes the dominant preflight cost at scale.
- `content_hash` extends dedup to plugin downloads, which `document_id` structurally cannot cover.

**Negative**

- A DB round-trip per dedup check replaces an in-memory `HashSet` lookup. At 0.33 items/s against an indexed integer lookup this is unmeasurable, but it is a real change from O(1) in-memory to O(log n) on disk.
- Dual-write across release N adds temporary complexity. Justified: it makes the rollback safe, and rollback safety is what allows shipping this at all.
- The reconciler must be correct or it defeats the purpose. It is ~50 lines over an existing, already-transactional bulk insert (`MediaRepository.cs:332`), and it is directly unit-testable.
- `content_hash` requires hashing each completed file — an extra full read unless it is computed during the streaming write. It must be computed inline.

**Neutral**

- `LegacyDedup` remains as a distinct table. Merging it into `Media` would require synthesising fake `chat_id`/`message_id` values, which is worse.

## Alternatives considered

### A. Make `FileDownloadIndex` the single authority; drop the SQLite path

Fewer moving parts and O(1) lookups. Rejected: it is a full-file-rewrite JSON store with no backup, no transactions, no integrity check, and a documented silent-empty-on-corruption path (`FileDownloadIndex.cs:105`). It also cannot express per-chat or per-status dedup, and it cannot dedup plugin downloads, which have no `document_id`.

### B. Keep both, synchronise on a timer

Cheapest change. Rejected: two authorities with eventual synchronisation means a permanent window where they disagree, and the failure is silent in both directions. Two sources of truth is the defect, not the sync interval.

### C. Bloom filter in front of SQLite

Rejected as premature. The lookup is an indexed integer probe at 0.33 items/s. Adding a probabilistic structure with a false-positive rate — where a false positive means *skipping a file the user wanted* — trades correctness for performance we do not need.

### D. Big-bang cutover, no dual-write

One release, delete `FileDownloadIndex`, rely on the one-shot import. Rejected: if the import is wrong the user re-downloads their entire archive, and there is no rollback because the JSON file has by then stopped being maintained. The dual-write release costs little and makes the failure recoverable.

### E. Dedup purely by file presence on disk

The `FindOnDisk` path (`ExistingDownloadValidator.cs:82-108`) already does something close. Rejected as the primary mechanism: it is O(directory size) per item, it breaks when the user reorganises or renames folders, and it cannot distinguish two different files that happen to share a name — the current guard is a size match (`:114-115`), which is a weak hash.

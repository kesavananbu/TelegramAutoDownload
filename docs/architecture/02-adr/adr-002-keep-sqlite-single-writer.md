# ADR-002 — Keep SQLite with WAL and an enforced single-writer boundary

- **Status:** Proposed
- **Date:** 2026-09-30
- **Deciders:** Architecture
- **Related:** ADR-001, ADR-005, ADR-007

## Context

State lives in one SQLite file, `/data/tad.db` (`TelegramAutoDownload.Headless/HeadlessPaths.cs:23`), accessed through Dapper. `Database.Open()` returns a **new** `SqliteConnection` per call and applies four PRAGMAs each time — `journal_mode=WAL`, `synchronous=NORMAL`, `foreign_keys=ON`, `busy_timeout=5000` (`Data/Database.cs:35-46`). All 25 `MediaRepository` methods call `_db.Open()` independently, so every write is its own connection and its own implicit transaction.

The question is whether to keep this or move to Postgres. The brief requires the answer be justified with concurrency numbers derived from the code.

### Required write rate, derived

**Bootstrap — the heaviest writer.** Default scan rate is 1 page/s (`TelegramClient/Models/ConfigParams.cs:24`); a page is 100 messages (`Scanning/BootstrapScanner.cs:32`); each media-bearing message is one `INSERT OR IGNORE` (`Data/MediaRepository.cs:47`, called at `BootstrapScanner.cs:108`) plus one `UpsertScanStateAsync` per page (`BootstrapScanner.cs:112`).

> **≈ 101 writes/s peak.** `MaxParallelBootstraps` (default 3, cap 10 — `ConfigParams.cs:34`, `Program.cs:239`) does not change this: all jobs share one token bucket (`Scanning/ScanRateLimits.cs:13`), so the bucket sets the aggregate rate.

**Downloads.** Four writes per item — claim (`MediaRepository.cs:206`), preflight status (`DownloadOrchestrator.cs:118`), terminal status (`:148`), path (`:150`). At the maximum 10 concurrent (`Program.cs:195`) and an assumed 30 s average per file: 0.33 items/s.

> **≈ 1.3 writes/s.**

**Live path.** ~3 writes per message (`Scanning/MediaTracker.cs:69,77,93`). At the p99 assumption of 50k messages/day:

> **≈ 1.7 writes/s average.**

**Sustained total < 10 writes/s. Peak ≈ 101 writes/s.**

The UI permits 50 pages/s (`Program.cs:233`) → 5,000 writes/s, but that exceeds the safe Telegram budget by roughly 50× and is not a reachable steady state; ADR-003's budget removes it as an operating point entirely.

### Available write rate

SQLite in WAL with `synchronous=NORMAL` sustains roughly **1,000–10,000 single-statement commits/s** on SSD. This codebase pays a fresh connection plus four PRAGMAs per operation (`Database.cs:35-46`), including `journal_mode=WAL` which writes the database header. **Estimated effective ceiling: ~1,000–2,000 commits/s.** *(Estimate — not measured. Benchmarking it is task A4.1.)*

### Required writer processes

**One.** ADR-001 fixes the deployment at one process per Telegram account, and `session.dat` cannot be shared (`TelegramClient/SessionLockHelper.cs:12-23`). There is no second process that could write.

### Three real defects in the current usage

1. **`Cache=Shared` in the connection string** (`Database.cs:28`) combined with WAL and many concurrent connections. Shared-cache mode introduces table-level locks that surface as `SQLITE_LOCKED` — which, unlike `SQLITE_BUSY`, is **not** retried by `busy_timeout` (`Database.cs:43`). A latent hard failure under load.
2. **No enforced single-writer boundary.** The invariant that makes SQLite safe is currently only a comment (`MediaRepository.cs:5-9`), not a structure.
3. **No durability.** No backup, no `VACUUM`, no `integrity_check`, anywhere.

## Decision

**Keep SQLite with WAL. Do not adopt Postgres.**

> ### Deciding number
> **Peak demand ≈ 101 writes/s against an effective ceiling of ~1,000–2,000 writes/s — 10–20× headroom at peak, ~100× at steady state — with a hard requirement of exactly one writer process.**
>
> Postgres's decisive advantage is concurrent multi-process writes. That advantage is **structurally unusable here**, because the Telegram session cannot be shared and therefore a second writer process cannot exist.

Alongside keeping SQLite, fix the three defects:

1. **Remove `Cache=Shared`** from the connection string (`Database.cs:28`).
2. **Introduce `SqliteWriteQueue`** — a `System.Threading.Channels` serialiser owning **one** long-lived write connection, with reads served by a separate pooled read-only connection set. This converts the single-writer invariant from a hope into a compile-time property, allows batching bootstrap inserts (100 rows → 1 transaction, ~100× fewer commits), and removes the per-call PRAGMA cost.
3. **Add durability:** continuous WAL replication (Litestream sidecar or equivalent) to object storage, nightly `VACUUM INTO` snapshot, `PRAGMA integrity_check` at startup, and a documented, *tested* restore procedure.

Define a **reversal trigger**: if sustained write demand exceeds 500 writes/s, or a second writer process becomes necessary (which requires ADR-001 to be reversed first), re-evaluate. Both conditions will be visible as Prometheus metrics from Milestone E.

## Consequences

**Positive**

- No new operational dependency for 10–50 independent deployments. No HA, no pooler, no major-version upgrade path, no second container per tenant.
- Backup becomes a file copy; restore becomes a file move. Both scriptable and easy to test, which is how backups actually stay working.
- The single-writer channel removes an entire class of lock-contention bugs and makes batching possible, which is the one change that meaningfully raises the ceiling.
- Removing `Cache=Shared` eliminates a latent `SQLITE_LOCKED` hard failure for the cost of deleting nine characters.
- The existing schema, migration runner (`Database.cs:63-109`) and all 25 repository methods survive unchanged. Zero data migration.

**Negative**

- No concurrent writers, ever. Any future feature wanting a second writing process is blocked until ADR-001 and this ADR are both reopened. This is intentional: it forces the conversation rather than allowing drift.
- Weaker types than Postgres (no native `timestamptz`, no `jsonb`, no partial-index planner sophistication). Timestamps are already stored as ISO-8601 text (`001_initial.sql:19,27`); acceptable at this scale.
- Single-file blast radius: corruption loses everything. Mitigated by continuous replication and `integrity_check`; the *current* state — no backup at all — is strictly worse.
- Analytical queries over millions of rows will be slower. `GetPerChatStatusCountsAsync` (`MediaRepository.cs:258`) is a full `GROUP BY` scan today; at p99 volumes it needs a summary table regardless of engine.

**Neutral**

- The write-queue refactor is incremental. The 25 methods are independent, so each can be moved behind the channel and shipped separately.

## Alternatives considered

### A. Postgres

Rejected. It solves multi-writer concurrency, which we cannot use (ADR-001). Costs: a second container per deployment × 10–50 deployments, connection pooling, HA or accepted downtime, major-version upgrades, a full data migration of `Media`/`ChatScanState`/`LegacyDedup`, and a rewrite of the `RETURNING`-based atomic claim (`MediaRepository.cs:206-230`) and `datetime('now')` usage throughout. Estimated **6–8 ew** plus permanent operational load, for capacity we are two orders of magnitude away from needing.

### B. SQLite now, Postgres-ready abstraction

Write everything against a generic `IMediaStore` with no SQLite-specific SQL. Rejected as stated, accepted in weakened form: we *do* introduce `IMediaStore` (ADR-009) for testability, but we deliberately keep SQLite-specific SQL such as `UPDATE … RETURNING` and `INSERT OR IGNORE` behind it. Portability-by-lowest-common-denominator would cost us the atomic claim — the single best property of the current design — to hedge against a migration this ADR argues will never happen.

### C. LiteDB / embedded document store

Rejected. Loses SQL, loses the atomic `UPDATE … RETURNING` claim, loses a mature migration story, and has a far smaller operational track record. No upside.

### D. Keep SQLite exactly as-is

Rejected. `Cache=Shared` with WAL (`Database.cs:28`) is a live hazard, there is no backup of any kind, and the single-writer property is unenforced. "Keep SQLite" is the right call; "change nothing" is not.

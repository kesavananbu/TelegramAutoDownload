# ADR-007 — Harden the schema migration runner; add a state-migration pipeline

- **Status:** Proposed
- **Date:** 2026-10-01
- **Review:** Composer 2.5 re-run — read-only verification on `enterprise-readiness` @ `1c3753e`; no build
- **Deciders:** Architecture
- **Related:** ADR-002, ADR-005, ADR-008, ADR-010

## Context

The brief states there is "no migration framework beyond one .sql file". **That is not accurate, and the correction matters**, because it changes the decision from "build one" to "harden one".

`Database.ApplyMigrationsAsync` (`Data/Database.cs:63-109`) is a working runner. It creates a `SchemaMigration` ledger (`:65-71`), reads applied versions (`:73-74`), discovers embedded `*.sql` resources under `Data/Schema/` (`:76-80`, embedded via `TelegramAutoDownload.Headless.csproj:24`), parses a numeric prefix from the filename (`:85-91`), and applies each unapplied script **inside a transaction together with its ledger insert** (`:99-105`). Transactional apply-and-record is the hard part, and it is already correct.

Six real gaps:

| # | Gap | Evidence | Impact |
|---|---|---|---|
| 1 | No checksum — only `version` is stored | `Database.cs:66-70` | An edited already-applied script is silently ignored; dev and prod schemas diverge with no signal |
| 2 | Lexical ordering by resource name | `.OrderBy(n => n)` at `Database.cs:79`, while the version is parsed later at `:86` | `010_x.sql` sorts before `2_x.sql`; breaks at the 10th migration |
| 3 | Unparseable filenames warn and skip | `Log.Warning` + `continue` at `Database.cs:88-91` | A typo silently omits a migration and the app starts "successfully" |
| 4 | No concurrency guard | none | Two simultaneously starting processes can both apply; realistic during a container-restart overlap |
| 5 | No baseline / stamp | none | An existing database cannot be adopted without re-running `001` |
| 6 | No dry-run, no down-migrations | none | No way to review a pending change or roll back a bad one |

**The larger gap is non-SQL state.** The brief's constraint is real — the desktop app has installed users — and there is no versioned pipeline for any of it:

| Artifact | Owner | Current migration story |
|---|---|---|
| `config.txt` (WPF) | `TelegramAutoDownload/AppPaths.cs:17` | none — headless uses a different file, `config.json` (`HeadlessPaths.cs:21`) |
| `config.json` (headless) | `ConfigStore.cs:17` | no `schemaVersion`; `Read()` deserializes and patches two fields (`:30-34`) and accepts anything else |
| `downloaded_ids.json` | `FileDownloadIndex.cs:23` | **one-shot** import guarded by `CountLegacyDedupAsync() > 0` (`LegacyDedupMigration.cs:17-22`); see ADR-010 |
| `stats.json` | `AppPaths.cs:22` | none |
| `session.dat` | hardcoded in `TelegramApp.cs:101-104` | none; `HeadlessPaths.SessionFile` (`HeadlessPaths.cs:22`) is declared but referenced nowhere |
| `tad.db` | `Database.cs` | the runner above |

Failure behaviour is also wrong: a corrupt `downloaded_ids.json` is handled by `catch { }` and continuing with an empty set (`FileDownloadIndex.cs:105`), which silently re-downloads the user's entire archive.

## Decision

**Keep and harden the existing runner. Add a parallel, versioned pipeline for application state. Make every migration back up first, log to a queryable ledger, and fail loudly.**

### 1. Close the six schema gaps

| Gap | Fix |
|---|---|
| 1 | Add `checksum TEXT` to `SchemaMigration`; compute SHA-256 of the script; **fail startup** on drift with the version, expected and actual hash |
| 2 | Parse the version first (the parse already exists at `Database.cs:86`) and order numerically |
| 3 | Replace `Log.Warning` + `continue` (`:88-91`) with a thrown exception |
| 4 | Wrap the whole run in `BEGIN IMMEDIATE` as an advisory lock |
| 5 | `migrate --baseline <version>` CLI to stamp an existing database |
| 6 | `migrate --plan` / `--verify`; paired `NNN_name.down.sql`, with a CI job that applies every up then every down against a fresh database |

### 2. Application-state pipeline

An `IStateMigration` chain with the same discipline as the schema runner, keyed by a `schemaVersion` field added to `config.json`:

```csharp
public interface IStateMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    Task<MigrationResult> ApplyAsync(MigrationContext ctx, CancellationToken ct);
}
```

Rules, applied uniformly:

1. **Back up before writing.** `{file}.v{from}.bak`, retained. `ConfigStore.Save` currently does a bare `File.WriteAllText` (`ConfigStore.cs:41`) with no temp-and-rename, so a crash mid-write corrupts the config; adopt the atomic pattern already used correctly in `FileDownloadIndex.cs:115-118`.
2. **Record `{from, to, timestamp, checksum, outcome}`** in a `MigrationLog` table; expose `GET /api/system/migrations`.
3. **Fail loudly.** If a migration cannot complete, refuse to start with an actionable message. Never silently continue with empty state.
4. **Forward-only** for state. Restore from the backup rather than writing inverse transforms for JSON.

Concrete migrations:

| From → To | Work |
|---|---|
| `config.txt` v1 → `config.json` v2 | field mapping, `schemaVersion: 2`, secrets extracted to `ISecretStore` (ADR-008), original kept as `config.txt.pre-v2.bak` |
| `downloaded_ids.json` → `LegacyDedup` | **continuous reconciliation**, not one-shot — ADR-010 |
| `stats.json` → `Stats` table | one-shot, guarded by file checksum rather than a row count |
| `session.dat` | move to the `HeadlessPaths.SessionFile` path, wrap in AES-GCM (ADR-008), keep the original for one release |

### 3. Desktop compatibility gate

Desktop builds ≥ the version that writes `schemaVersion` round-trip cleanly. Older builds are read-only-import: the app imports, writes the new format, and preserves the original as `.pre-v2.bak`. A desktop version below the gate that encounters a v2 config must refuse to write and prompt the user to upgrade — never silently downgrade and drop fields.

## Consequences

**Positive**

- Schema drift between environments becomes impossible to miss instead of impossible to detect.
- The ordering bug at migration 10 is fixed before it can ship.
- Every state transition is backed up, logged and queryable — so "why did this install lose its dedup index?" becomes answerable.
- Down-migrations tested in CI make a bad release recoverable.
- Existing production databases can be adopted via `--baseline` rather than needing special-case code.
- Installed desktop users get a defined, reversible upgrade rather than an undefined one.

**Negative**

- Checksum enforcement will break developer habits: editing an applied migration now fails startup. This is the point, but it needs documenting.
- Down-scripts roughly double the authoring effort per migration. Accepted for schema; explicitly **not** adopted for state (forward-only plus backups).
- Failing loudly converts some silent degradations into startup failures. Net positive — a downloader that silently re-downloads 5 TB is worse than one that refuses to start — but it must be communicated.
- Backup files accumulate; the Janitor needs a retention policy.

**Neutral**

- The runner keeps its existing shape, so the change is incremental and low-risk.

## Alternatives considered

### A. Adopt FluentMigrator or Evolve

Mature, batteries-included, both support checksums and baselines. Rejected: a dependency plus a migration-of-the-migrations for the existing `SchemaMigration` ledger, to replace roughly 45 lines that are already structurally correct. The six gaps are perhaps 2 ew to close in place. Reconsider if we ever adopt a second database engine.

### B. EF Core Migrations

Rejected. The data access is Dapper with hand-written, deliberately SQLite-specific SQL — `UPDATE … RETURNING` (`MediaRepository.cs:206-230`), `INSERT OR IGNORE` (`:51`), `datetime('now')` throughout. Introducing EF means either a full data-access rewrite or maintaining two models. It also fights ADR-002's decision to embrace SQLite-specific features.

### C. Leave the runner alone; only add state migrations

Rejected. Gap 2 (lexical ordering) is a defect that activates silently at the tenth migration, and gap 1 (no checksum) is how environments diverge undetected. Both are cheap to fix now and expensive to diagnose later.

### D. Version the config by filename (`config.v2.json`) instead of a field

Rejected. It multiplies files, complicates the "which is current?" question, and breaks the existing `HeadlessPaths.ConfigFile` contract (`HeadlessPaths.cs:21`). A `schemaVersion` field is standard and lets the loader migrate transparently.

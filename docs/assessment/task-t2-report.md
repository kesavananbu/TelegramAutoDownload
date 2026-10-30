# Task T2 — Dedup authority, session paths, SQLite hardening

**Branch (intended):** `er/t2-dedup-sqlite` from `enterprise-readiness`  
**Controller note:** Work committed by path; this report was not git-committed per override.

## Summary

- **Dedup:** `LegacyDedupMigration` reconciles `downloaded_ids.json` on every startup via `INSERT OR IGNORE`; orchestrator preflight already uses `MediaRepository.IsKnownDocumentAsync` (Media `done` ∪ `LegacyDedup`). `Program.cs` registers `FileDownloadIndex.Flush()` on `ApplicationStopping`.
- **Paths:** `HeadlessPaths.SessionFile` and `LegacyDedupIndexFile` now use `{ApplicationData}/TelegramAutoDownload/…`, matching WTelegram / `FileDownloadIndex`.
- **SQLite:** Removed `Cache=Shared`; migrations ordered by numeric version, `BEGIN IMMEDIATE` batch, refuse DB newer than embedded migrations; `MediaRepository` serializes writes with `SemaphoreSlim(1)`.
- **Packages:** `Microsoft.Data.Sqlite` 8.0.17, `Dapper` 2.1.89, `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5 (clears GHSA-2m69-gcr7-jv3q).

## Build / test

```powershell
dotnet build TelegramAutoDownload.Headless\TelegramAutoDownload.Headless.csproj -c Release --artifacts-path D:\tad-artifacts\t2
dotnet test TelegramAutoDownload.Headless.Tests\TelegramAutoDownload.Headless.Tests.csproj -c Release --artifacts-path D:\tad-artifacts\t2
```

- Headless project: **build succeeded** (0 errors).
- T2-owned tests (LegacyDedup, HeadlessPaths, DatabaseMigration, MediaRepository*, concurrency): **13 passed**.
- Full Headless suite: **1 failure** in `TelegramApiBudgetTests` (timeout; owned by parallel track T3 — not modified in T2).

```text
dotnet list TelegramAutoDownload.Headless package --vulnerable --include-transitive --source https://api.nuget.org/v3/index.json
→ The given project has no vulnerable packages given the current sources.
```

## Remaining (Headless half done)

Live download factories in `TelegramClient` still consult `FileDownloadIndex` only (`Files.cs`, `Videos.cs`, etc.). Full ADR-010 unification requires a later track to route those checks through SQLite or dual-write from Headless — **out of scope** (`TelegramApp.cs` frozen).

## Open risks

- Dual dedup on live path vs SQLite orchestrator until TelegramClient read path is unified.
- Migration checksum / baseline CLI (ADR-007 gaps 1, 5, 6) not implemented — only cheap guards added.
- `SQLitePCLRaw` 3.x is a major bump; monitor runtime on all target platforms.

## Files touched (exact paths)

| Path | Change |
|------|--------|
| `TelegramAutoDownload.Headless/HeadlessPaths.cs` | `WTelegramConfigDir`; session + legacy JSON paths |
| `TelegramAutoDownload.Headless/Data/LegacyDedupMigration.cs` | Idempotent every-startup reconcile |
| `TelegramAutoDownload.Headless/Data/Database.cs` | No shared cache; migration hardening |
| `TelegramAutoDownload.Headless/Data/MediaRepository.cs` | Write lock |
| `TelegramAutoDownload.Headless/Program.cs` | Shutdown flush (see hunks below) |
| `TelegramAutoDownload.Headless/TelegramAutoDownload.Headless.csproj` | Package bumps |
| `TelegramAutoDownload.Headless.Tests/LegacyDedupMigrationTests.cs` | **New** |
| `TelegramAutoDownload.Headless.Tests/HeadlessPathsTests.cs` | **New** |
| `TelegramAutoDownload.Headless.Tests/MediaRepositoryConcurrencyTests.cs` | **New** |
| `TelegramAutoDownload.Headless.Tests/DatabaseMigrationTests.cs` | Newer-DB refusal test |
| `docs/assessment/task-t2-report.md` | **New** (this file) |

### Shared file hunks

**`Program.cs`:** Added `using TelegramClient;` after DB init block, registered `app.Lifetime.ApplicationStopping` to call `FileDownloadIndex.Flush()` with warning log on failure.

**`TelegramAutoDownload.Headless.csproj`:** `Dapper` 2.1.35 → 2.1.89; `Microsoft.Data.Sqlite` 8.0.10 → 8.0.17; added `SQLitePCLRaw.bundle_e_sqlite3` 3.0.5.

## Edits outside ownership

None intentional. Did **not** edit `TelegramApp.cs`, `FileDownloadIndex.cs`, `HeadlessHost.cs`, or `Scanning/*`.

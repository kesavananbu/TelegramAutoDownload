# Task T3 — Download data integrity

**Branch (intended):** `er/t3-data-integrity` from `enterprise-readiness` (a458fd3)  
**Controller commit:** pending — this report is not committed per override.

## Evidence verified

| ID | Claim | Fix |
|---|---|---|
| F-13 | Regex filters case-sensitive, no timeout, invalid pattern throws | `RegexFilterHelper` |
| F-15 | No max size / free-space gate | `DownloadLimits` + policy checks |
| 03-feature #2 | `.part` promoted without size check | `DownloadPartFiles.TryPromotePartToFinal` |
| 03-feature #45 | Chunk retry appends without seek | `PrepareStreamForChunkWrite` |
| 03-feature #50 | Corrupt config throws; non-atomic save | `ConfigJsonFileHelper` + `ConfigFile` / `ConfigStore` |

**Note (item 6):** Minimum-size filter still bypasses native photos (`CheckDownloadPolicy` non-document path) — unchanged.

## Validator API (for Headless PATCH / UI)

- `TelegramClient.RegexFilterHelper.TryValidatePattern(string pattern, out string? errorMessage)`
- Matching: `RegexFilterHelper.IsMatch`, `FindFirstMatchingIgnorePattern`
- Default match timeout: `RegexFilterHelper.DefaultMatchTimeout` (250 ms)

## Tests

```powershell
dotnet build TelegramAutoDownload.Tests --artifacts-path D:\tad-artifacts\t3 -c Release
dotnet test TelegramAutoDownload.Tests --artifacts-path D:\tad-artifacts\t3 -c Release --no-build
dotnet test TelegramAutoDownload.Headless.Tests --artifacts-path D:\tad-artifacts\t3 -c Release
```

| Run | Passed | Failed |
|---|---|---|
| WPF (`TelegramAutoDownload.Tests`) | 423 | 0 |
| Headless (`TelegramAutoDownload.Headless.Tests`) | — | — (build OK; full test run exceeded 10 min in isolated checkout — re-run locally) |

New/updated: `DataIntegrityT3Tests`, `DownloadLimitsTestCollection`, `ConfigFilePersistenceTests.Read_CorruptJson_*` (behaviour intentionally changed from throw → defaults + backup).

## Files created

- `BasePlugin/DownloadLimits.cs`
- `TelegramClient/RegexFilterHelper.cs`
- `TelegramClient/DownloadPartFiles.cs`
- `TelegramClient/ConfigJsonFileHelper.cs`
- `TelegramAutoDownload.Tests/DataIntegrityT3Tests.cs`
- `TelegramAutoDownload.Tests/DownloadLimitsTestCollection.cs`
- `docs/assessment/task-t3-report.md`

## Files modified

- `TelegramClient/Factory/Base/BaseMessage.cs` — disk gate; size-checked `.part` promote; regex via helper
- `TelegramClient/Models/ConfigParams.cs` — `DownloadFreeSpaceSafetyMarginBytes`, `MaxDownloadFileSizeBytes`, `ApplyDownloadLimits()`
- `DownloadService/DownladerPlugin.cs` — disk gate; `PrepareStreamForChunkWrite` on chunk retry
- `TelegramAutoDownload/Models/ConfigFile.cs` — atomic save; corrupt backup + defaults
- `TelegramAutoDownload.Headless/ConfigStore.cs` — same persistence semantics
- `TelegramAutoDownload.Tests/ConfigFilePersistenceTests.cs` — corrupt JSON test updated
- `TelegramAutoDownload.Tests/DownloadPolicyTests.cs` — `DownloadLimits` isolation for parallel runs

## Shared-file hunk summary

**ConfigParams.cs:** Added two `long` settings (default margin 1 GiB, max size 0=unlimited) and `ApplyDownloadLimits()` syncing into `DownloadLimits` static.

**BasePlugin/DownloadLimits.cs:** New static gate: max file size + free space (file bytes + margin).

## Edits outside ownership

None intentionally. Concurrent agents may have touched other paths in the shared checkout.

## Open risks

- `DownloadLimits` is process-global; hosts must call `ApplyDownloadLimits()` after every config read (WPF `ConfigFile`, Headless `ConfigStore` do; runtime edits without re-read need host to re-apply).
- `FactoryMessagesService` / `TelegramApp` text-capture regex sites still use legacy `Regex.IsMatch` (F-13 partial outside this track’s file list).
- Free-space check skipped when `expectedSizeBytes <= 0` (unknown Telegram/HTTP size).
- Full `RunnerApp.sln` build not verified here when unrelated agent WIP breaks non-owned projects.

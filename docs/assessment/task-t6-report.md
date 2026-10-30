# Task T6 — Desktop supply-chain, secret hygiene, flaky tests

**Branch (intended):** `er/t6-desktop-supply-chain` from `enterprise-readiness` @ a458fd3  
**Agent:** T6 (parallel track). Controller override: no git writes from agent; commits pending controller path-staging.

## Findings addressed

| ID | Summary | Verification |
|---|---|---|
| F-09 | Auto-updater integrity, asset allowlist, batch/env hardening, numeric version compare | `04-findings.md` F-09 evidence lines; `05-operations.md` §4.3 U2–U4, U8 |
| F-10 | yt-dlp/ffmpeg SHA2-256SUMS before install; env opt-out | F-10 evidence; `YtdlpService.cs` bootstrap path |
| F-22 | No shell-execute on risky download extensions | `MainWindow.xaml.cs` open handler |
| Export | README promises credentials excluded | `SettingsExportHelper` + optional checkbox |

## Flaky suite (AppLogAlert singleton)

- Added `[Collection("AppLogAlertSingleton")]` with `DisableParallelization = true` on `AppLogAlertServiceTests` and `LogAlertSinkLevelFilterTests`.
- **5× filtered runs** (22 tests): all **PASS** (exit 0 each).
- **5× full suite** (`423` tests): **422 pass, 1 fail** every run — failures are **other-track**, not singleton race:
  - Runs 1–2: `DataIntegrityT3Tests.Policy_MaxFileSize_BlocksOversizedDocument` (T3)
  - Runs 3–5: `DownloadPolicyTests.Policy_FileLargerThanThreshold_Passes` (DownloadService/T3 overlap)
- No `AppLogAlertServiceTests` / `LogAlertSinkLevelFilterTests` / `Emit_Error_*` variance observed post-fix.

## Build / test

```
dotnet build TelegramAutoDownload.Tests --artifacts-path D:\tad-artifacts\t6  → succeeded (after T3 compile settled)
dotnet test TelegramAutoDownload.Tests --artifacts-path D:\tad-artifacts\t6 --no-build
```

- **New tests:** 23 (`AutoUpdateIntegrityTests`, `ConfigExportHelperTests`, `DownloadLaunchHelperTests`, `AppVersionCompareTests`).
- **Total suite:** 423 tests when T3 tests compile.

## Implementation notes

- **Auto-update:** GitHub asset `digest` (`sha256:`) or release `SHA256SUMS.txt`; mismatch aborts; missing digest → `UpdateIntegrityException` + release page prompt. Asset allowlist `^TelegramAutoDownload_v\d+\.\d+\.\d+_(Setup\.exe|Portable\.zip)$`. Random temp dir; batch uses `%TAD_*%` env vars only. `AppVersion.GitHubOwner` unchanged (`il90il90`).
- **yt-dlp/ffmpeg:** `SHA2-256SUMS` verification; `TAD_DISABLE_TOOL_AUTO_DOWNLOAD=1|true` skips auto-download.
- **Release process rule:** step to generate/upload `SHA256SUMS.txt` with both mandatory assets.

## Files touched (T6 ownership)

**Created**

- `TelegramAutoDownload/Services/FileIntegrity.cs`
- `TelegramAutoDownload/Services/AppVersionCompare.cs`
- `TelegramAutoDownload/Services/UpdateIntegrityException.cs`
- `TelegramAutoDownload/Services/ConfigExportHelper.cs`
- `TelegramAutoDownload/Services/DownloadLaunchHelper.cs`
- `TelegramAutoDownload.Tests/AppLogAlertSingletonCollection.cs`
- `TelegramAutoDownload.Tests/AutoUpdateIntegrityTests.cs`
- `TelegramAutoDownload.Tests/ConfigExportHelperTests.cs`
- `TelegramAutoDownload.Tests/DownloadLaunchHelperTests.cs`
- `TelegramAutoDownload.Tests/AppVersionCompareTests.cs`

**Modified**

- `TelegramAutoDownload/Services/AutoUpdateService.cs`
- `TelegramAutoDownload/Services/YtdlpService.cs`
- `TelegramAutoDownload/MainWindow.xaml.cs`
- `TelegramAutoDownload/SettingsWindow.xaml` / `SettingsWindow.xaml.cs`
- `TelegramAutoDownload/UpdateDialog.xaml.cs`
- `TelegramAutoDownload.Tests/SelectedChannelFilterTests.cs`
- `TelegramAutoDownload.Tests/SettingsExportImportTests.cs`
- `.cursor/rules/release-process.mdc`

**Not modified (other tracks):** `ConfigFile.cs`, Headless, TelegramClient, installer, `.github/workflows`.

## Follow-ups

1. Replace batch/cmd updater with **Velopack** or signed helper exe (`05-operations.md` §4.3).
2. Authenticode / publisher pin for Setup.exe (F-09 step 1 optional hardening).
3. CI: attach `SHA256SUMS.txt` on `release-desktop.yml` (out of T6 scope).
4. Reconcile `GitHubOwner` feed vs `origin` repo for enterprise customers (documented U1; owner left deliberate).

## Suggested commits (controller)

1. `test: serialize AppLogAlert singleton tests`
2. `fix(wpf): harden auto-update integrity and asset allowlist`
3. `fix(wpf): verify yt-dlp/ffmpeg checksums; env opt-out`
4. `fix(wpf): reveal risky downloads in Explorer; strip export secrets`
5. `docs(rules): require SHA256SUMS.txt on desktop releases`

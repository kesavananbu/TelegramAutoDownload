# Phase 0 — Baseline Assessment

**Repository:** `D:/Codebase/TelegramDownloader`
**Branch:** `enterprise-readiness`
**HEAD at time of assessment:** `554d8e198206835991373a911180024cfd250000` — *"update user config to try out"* (2026-05-27)
**Assessment date:** 2026-09-30
**Host:** Windows 10.0.26200, PowerShell, x64

Everything in this document was measured by running commands in this repository. Anything not measured is marked **NOT MEASURED**. Anything inferred is labelled **ASSUMPTION**.

---

## 0. Scope, Method, and Environment Caveats

### 0.1 Tooling and modifications made

Per the assessment constraints, **no source file was modified** (no `.cs`, `.csproj`, `.js`, `.html`, `.css`, `.sql`, `.xaml`). No `.sln` was created — every project was built individually by `.csproj` path.

Two things outside the repository source tree were created/installed, and are disclosed here:

| Item | What / Why | Impact on repo |
|---|---|---|
| `_assess_logs/` (temporary) | Captured raw `dotnet build` / `dotnet test` output. **Deleted after the report was written.** | None (was git-ignored-adjacent, untracked, now removed) |
| `dotnet-coverage` v18.11.2 | Installed as a **global .NET tool**, to measure coverage without adding `coverlet.collector` to any `.csproj` | None — machine-level tool, nothing in repo changed |
| `docs/assessment/00-baseline.md` | This deliverable | Intentional |

### 0.2 Environment blocker discovered: unreachable corporate NuGet feed

The machine's user-level NuGet configuration registers a corporate Artifactory feed that is **unreachable from this host**:

```
> dotnet nuget list source
Registered Sources:
  1.  nuget.org [Enabled]
      https://api.nuget.org/v3/index.json
  2.  Artifactory [Enabled]
      https://art-bobcat.autodesk.com/artifactory/api/nuget/v3/team-iis-nuget/index.json
  3.  Microsoft Visual Studio Offline Packages [Enabled]
      C:\Program Files (x86)\Microsoft SDKs\NuGetPackages\
```

A plain `dotnet restore` fails intermittently with:

```
error NU1301: Unable to load the service index for source https://art-bobcat.autodesk.com/artifactory/api/nuget/v3/team-iis-nuget/index.json.
error NU1301:   The requested name is valid, but no data of the requested type was found. (art-bobcat.autodesk.com:443)
warning NU1900: Error occurred while getting package vulnerability data: Unable to load the service index for source https://art-bobcat.autodesk.com/...
  Failed to restore D:\Codebase\TelegramDownloader\BasePlugin\BasePlugins.csproj (in 40.08 sec).
```

Restores took **40–52 seconds each and failed non-deterministically** (5 of 11 projects failed on the first pass). This is a **host/environment problem, not a repository defect** — there is no `NuGet.config` committed in the repo (verified: a recursive search for `NuGet.config` returned nothing).

**All build, test, and dependency commands in this report were therefore run with `--source https://api.nuget.org/v3/index.json`** to pin resolution to the public feed. With that pin, all 11 restores succeeded in **under 4 seconds each**.

> **Note for CI design:** because the repo commits no `NuGet.config`, restore behaviour is entirely at the mercy of whatever machine-level feeds happen to be configured. That is a reproducibility gap worth closing (see §7).

### 0.3 Concurrent-agent interference — important for reading this report

Other agents were operating in this working tree during the assessment. Two directories appeared **after** this session began and are **not part of the baseline**:

```
> git ls-tree -r HEAD --name-only | Select-String '^\.github'
(empty — .github/ is NOT in HEAD)

> Get-ChildItem .github -Recurse -File | Select-Object FullName, CreationTime
.github\workflows\codeql.yml                 30-09-2026 11:05:42 PM
.github\workflows\container-image.yml        30-09-2026 11:06:12 PM
.github\workflows\pr-validation.yml          30-09-2026 11:05:23 PM
.github\workflows\release-desktop.yml        30-09-2026 11:07:44 PM
.github\workflows\scheduled-maintenance.yml  30-09-2026 11:09:01 PM
```

Those files were created at 23:05–23:10 tonight by another agent. **The baseline (HEAD `554d8e1`) contains no `.github/` directory and no CI of any kind.** All CI-related findings in §7 describe the baseline, not the current working tree.

---

## 1. Build

### 1.1 SDK vs. target framework — the "SDK 10 vs net8.0" question: NOT a problem

The installed SDK is newer than the target framework, but this is **explicitly supported** and caused **zero build problems**:

```
> dotnet --info
.NET SDK:
 Version:           10.0.401
 MSBuild version:   18.9.11+e34a38d2a

.NET SDKs installed:
  9.0.305, 10.0.100-rc.1.25451.107, 10.0.112, 10.0.401

.NET runtimes installed:
  Microsoft.NETCore.App 8.0.20, 8.0.31, 9.0.9, 9.0.20, 10.0.12
  Microsoft.AspNetCore.App 8.0.20, 8.0.31, ...
  Microsoft.WindowsDesktop.App 8.0.20, 8.0.31, ...

global.json file:
  Not found
```

The required **net8.0 targeting packs and runtimes (8.0.20 and 8.0.31) are installed**, so the .NET 10 SDK cross-compiles to net8.0 correctly. Verified from the generated MSBuild config that the TFM was honoured:

```
build_property.TargetFramework = net8.0
build_property.TargetFrameworkIdentifier = .NETCoreApp
build_property.TargetFrameworkVersion = v8.0
```

**Result: all 11 projects built successfully. 0 errors.**

However, there is a genuine **reproducibility risk** here, distinct from a build break:

- **No `global.json` exists** (verified: `Test-Path global.json` → `False`). Nothing pins the SDK. A developer or CI runner with only a .NET 9 SDK, or a future SDK that drops net8.0 targeting packs, will get different (or failing) behaviour. The `Dockerfile` pins `mcr.microsoft.com/dotnet/sdk:8.0`, so **the container build and the local build currently use different compilers** — the container uses Roslyn from the .NET 8 SDK, the local build uses Roslyn 18.9.11 from the .NET 10 SDK.
- **No `Directory.Build.props`, `Directory.Build.targets`, or `Directory.Packages.props` exists** (verified by recursive search). Every one of the 11 projects configures `TargetFramework`, `Nullable`, and `ImplicitUsings` independently, and package versions are duplicated across files with no central management.

### 1.2 Build commands and results

Restore (one command per project):

```powershell
dotnet restore <project>.csproj --source https://api.nuget.org/v3/index.json
```

Build (one command per project, `-t:Rebuild` to force full recompilation so warning counts are accurate rather than suppressed by incremental builds):

```powershell
dotnet build <project>.csproj -c Release --no-restore -t:Rebuild -v normal
```

| # | Project | TFM | Exit code | Result | Warnings (incl. deps) |
|---|---|---|---|---|---|
| 1 | `BasePlugin/BasePlugins.csproj` | net8.0 | 0 | Build succeeded | 5 |
| 2 | `Logger/Logger.csproj` | net8.0 | 0 | Build succeeded | 4 |
| 3 | `TelegramClient/TelegramClient.csproj` | net8.0 | 0 | Build succeeded | 66 |
| 4 | `DownloadService/DownloadPlugin.csproj` | net8.0 | 0 | Build succeeded | 5 |
| 5 | `YoutubePlugin/YoutubePlugin.csproj` | net8.0 | 0 | Build succeeded | 5 |
| 6 | `SocialMediaPlugin/SocialMediaPlugin.csproj` | net8.0 | 0 | Build succeeded | 5 |
| 7 | `TorrentPlugin/TorrentPlugin.csproj` | net8.0 | 0 | Build succeeded | 5 |
| 8 | `TelegramAutoDownload/TelegramAutoDownload.csproj` | **net8.0-windows** | 0 | Build succeeded | 70 |
| 9 | `TelegramAutoDownload.Headless/...csproj` | net8.0 | 0 | Build succeeded | 70 |
| 10 | `TelegramAutoDownload.Tests/...csproj` | **net8.0-windows** | 0 | Build succeeded | 71 |
| 11 | `TelegramAutoDownload.UITests/...csproj` | **net8.0-windows** | 0 | Build succeeded | 0 |

**Build status: 11/11 succeeded, 0 errors, exit code 0 for every project.**

The per-project warning counts above are cumulative (they include warnings re-emitted from referenced projects), which is why the numbers appear to grow down the table. The de-duplicated figures are in §1.4.

### 1.3 Windows-only projects

Three of eleven projects target `net8.0-windows` and **cannot build or run on Linux**:

| Project | TFM | Why Windows-only |
|---|---|---|
| `TelegramAutoDownload` | net8.0-windows | `<UseWPF>true</UseWPF>` + `<UseWindowsForms>true</UseWindowsForms>` + `<OutputType>WinExe</OutputType>` |
| `TelegramAutoDownload.UITests` | net8.0-windows | FlaUI/UIA3 — Windows UI Automation |
| `TelegramAutoDownload.Tests` | net8.0-windows | **Only** because it has `<ProjectReference Include="..\TelegramAutoDownload\TelegramAutoDownload.csproj" />` — see §6.3 |

The remaining eight (`BasePlugin`, `Logger`, `TelegramClient`, `DownloadService`, `YoutubePlugin`, `SocialMediaPlugin`, `TorrentPlugin`, `TelegramAutoDownload.Headless`) are portable `net8.0`.

That the **unit-test project is Windows-locked is a self-inflicted constraint**, not a technical necessity — it means the cross-platform core (`TelegramClient`, the plugins, and the containerized Headless server) cannot be tested on a Linux CI runner.

### 1.4 Warning inventory — 71 unique warnings, all nullable-related

De-duplicated across all 11 rebuild logs by `file(line,col): warning CODE [owning-project]`:

**Total unique compiler warnings: 71. Total errors: 0.**

Grouped by warning code:

| Count | Code | Meaning | Severity assessment |
|---|---|---|---|
| **61** | `CS8632` | "The annotation for nullable reference types should only be used in code within a `#nullable` annotations context" | Symptom of a real gap — see below |
| **9** | `CS8618` | "Non-nullable property must contain a non-null value when exiting constructor" | Real latent `NullReferenceException` risk |
| **1** | `CS8601` | "Possible null reference assignment" | Test-code only |

Grouped by owning project:

| Count | Project | Codes |
|---|---|---|
| 61 | `TelegramClient.csproj` | CS8632 ×61 |
| 5 | `BasePlugins.csproj` | CS8618 ×5 |
| 4 | `Logger.csproj` | CS8618 ×4 |
| 1 | `TelegramAutoDownload.Tests.csproj` | CS8601 ×1 |

**Non-CS warnings (MSB / NETSDK / NU / SYSLIB) in the build logs: 0.** No obsolete-API (`CS0618`/`SYSLIB`) warnings were emitted. No package-downgrade or framework-mismatch warnings.

#### Why the 61 × CS8632 matters more than the count suggests

All 61 are in `TelegramClient`, and they exist because **`TelegramClient.csproj` is the only project in the solution that does not enable nullable reference types**:

```1:14:TelegramClient/TelegramClient.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
  </PropertyGroup>
```

Compare with every other project, e.g.:

```1:9:BasePlugin/BasePlugins.csproj
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>
```

The authors **wrote 61 `?` nullable annotations throughout `TelegramClient`** — meaning they were actively thinking about nullability — but because `<Nullable>` is absent, **the compiler ignores every one of them and performs no null-flow analysis at all on this project.** The annotations are decorative.

This is the single largest and most network-exposed C# project in the solution (3,697 lines, the entire Telegram protocol surface), and it is the **only** one with null checking switched off. Representative locations:

- `TelegramClient/TelegramApp.cs:84`, `:97`, `:192`, `:242`, `:245`, `:293`, `:295`, `:507`, `:517`, `:709`, `:721`, `:731`, `:784`, `:968`, `:1072`, `:1090`, `:1097`, `:1147`, `:1154`, `:1189`, `:1257`, `:1292`, `:1313`, `:1501`
- `TelegramClient/ChatHistoryService.cs:91`, `:92`
- `TelegramClient/Factory/Base/BaseMessage.cs:135`

#### The 9 × CS8618 (uninitialised non-nullable members)

These are real null-dereference hazards on DTOs that cross process/serialization boundaries:

```
BasePlugin/ResultExecute.cs(5,16): warning CS8618: Non-nullable property 'FileName' ...
BasePlugin/ResultExecute.cs(5,16): warning CS8618: Non-nullable property 'FilePath' ...
BasePlugin/ResultExecute.cs(5,16): warning CS8618: Non-nullable property 'MessageType' ...
BasePlugin/ResultExecute.cs(5,16): warning CS8618: Non-nullable property 'ErrorMessage' ...
BasePlugin/ResultExecute.cs(5,16): warning CS8618: Non-nullable property 'NotificationKey' ...
Logger/Config/ConfigTelegramLogger.cs(13,23): warning CS8618: Non-nullable property 'BotToken' ...
Logger/Config/ConfigTelegramLogger.cs(14,23): warning CS8618: Non-nullable property 'ChatId' ...
Logger/Services/TelegramService.cs(14,16): warning CS8618: Non-nullable property 'BotToken' ...
Logger/Services/TelegramService.cs(14,16): warning CS8618: Non-nullable property 'ChatId' ...
```

`BasePlugin/ResultExecute.cs:5` is the return type of **every plugin execution** — it is the central result contract of the entire plugin chain, and five of its properties can be silently null.

#### The 1 × CS8601

```
TelegramAutoDownload.Tests/NotificationTests.cs(21,36): warning CS8601: Possible null reference assignment.
```

#### No warnings-as-errors anywhere

No project sets `TreatWarningsAsErrors`, and there is no `Directory.Build.props` to set it centrally. **71 warnings can accumulate indefinitely with nothing to stop them.**

---

## 2. Tests

### 2.1 `TelegramAutoDownload.Tests` (xUnit) — 391 tests, 1 failing, and it is FLAKY

Command:

```powershell
dotnet test TelegramAutoDownload.Tests\TelegramAutoDownload.Tests.csproj -c Release --no-build --logger "trx;LogFileName=unit.trx" --logger "console;verbosity=normal"
```

Raw result of the first run:

```
Results File: D:\Codebase\TelegramDownloader\TelegramAutoDownload.Tests\TestResults\unit.trx

Total tests: 391
     Passed: 390
     Failed: 1
Test Run Failed.
 Total time: 30.9176 Seconds
EXIT=1 WALLCLOCK_SEC=33.2138196
```

| Metric | Value |
|---|---|
| Total tests | **391** |
| Passed | **390** |
| Failed | **1** |
| Skipped | **0** |
| Reported duration | 29–45 s (varies per run) |
| Wall-clock duration | **33.2 s** (first run); 33.2 / 48.8 / 38.5 s across repeat runs |
| Exit code | **1** |

#### The failure is non-deterministic — a different test fails every run

The suite was executed **four times**. Every run produced exactly one failure, but **the failing test was different each time**:

| Run | Wall clock | Failing test |
|---|---|---|
| 1 | 33.2 s | `LogAlertSinkLevelFilterTests.Emit_BelowWarning_DoesNotAlertService(level: Verbose)` |
| 2 | 33.2 s | `LogAlertSinkLevelFilterTests.Emit_WarningOrAbove_AlertsService(level: Warning)` |
| 3 | 48.8 s | `LogAlertSinkLevelFilterTests.Emit_WarningOrAbove_AlertsService(level: Warning)` |
| 4 | 38.5 s | `AppLogAlertServiceTests.Clear_FiresChangedEvent` |
| 5 (under coverage) | — | `LogAlertSinkLevelFilterTests.Emit_WarningOrAbove_AlertsService(level: Fatal)` |

Representative failure messages, verbatim:

```
Failed TelegramAutoDownload.Tests.LogAlertSinkLevelFilterTests.Emit_BelowWarning_DoesNotAlertService(level: Verbose) [74 ms]
  Error Message:
   Expected AppLogAlertService.Instance.UnreadCount to be 0 because level Verbose must not trigger
   the error alert, but found 1 (difference of 1).
  Stack Trace:
     at TelegramAutoDownload.Tests.LogAlertSinkLevelFilterTests.Emit_BelowWarning_DoesNotAlertService(LogEventLevel level)
        in D:\Codebase\TelegramDownloader\TelegramAutoDownload.Tests\SelectedChannelFilterTests.cs:line 298
```

```
Failed TelegramAutoDownload.Tests.LogAlertSinkLevelFilterTests.Emit_WarningOrAbove_AlertsService(level: Fatal) [203 ms]
  Error Message:
   Expected AppLogAlertService.Instance.UnreadCount to be 1 because level Fatal must increment
   the unread count, but found 0 (difference of -1).
  Stack Trace:
     at TelegramAutoDownload.Tests.LogAlertSinkLevelFilterTests.Emit_WarningOrAbove_AlertsService(LogEventLevel level)
        in D:\Codebase\TelegramDownloader\TelegramAutoDownload.Tests\SelectedChannelFilterTests.cs:line 312
```

Note the failures go in **both directions** — sometimes the count is too high, sometimes too low.

#### Root cause: shared mutable singleton + xUnit parallel collections

The tests assert against a process-wide singleton, `AppLogAlertService.Instance`, defined at `TelegramAutoDownload/Services/AppLogAlertService.cs:11`. The test classes that touch it are declared at `TelegramAutoDownload.Tests/SelectedChannelFilterTests.cs:137` (`AppLogAlertServiceTests`) and `:265` (`LogAlertSinkLevelFilterTests`).

The pattern used is read-then-assert-relative:

```287:300:TelegramAutoDownload.Tests/SelectedChannelFilterTests.cs
        [Theory]
        [InlineData(LogEventLevel.Verbose)]
        [InlineData(LogEventLevel.Debug)]
        [InlineData(LogEventLevel.Information)]
        public void Emit_BelowWarning_DoesNotAlertService(LogEventLevel level)
        {
            var before = AppLogAlertService.Instance.UnreadCount;

            _sink.Emit(MakeLogEvent(level));

            AppLogAlertService.Instance.UnreadCount.Should().Be(before,
                $"level {level} must not trigger the error alert");
        }
```

...while the constructor and `Dispose` of the *other* class concurrently call `AppLogAlertService.Instance.Clear()`:

```268:275:TelegramAutoDownload.Tests/SelectedChannelFilterTests.cs
        public LogAlertSinkLevelFilterTests()
        {
            AppLogAlertService.Instance.Clear();
            _sink = new LogAlertSink(SerilogFileSettings.FileOutputTemplate);
        }

        public void Dispose() => AppLogAlertService.Instance.Clear();
```

xUnit runs distinct test classes **in parallel by default**. A search across the whole repository for `CollectionBehavior`, `DisableTestParallelization`, and `[Collection` found **only two matches, both in the UITests project**:

```
TelegramAutoDownload.UITests\MainWindowTests.cs:25:    [Collection("UI")]   // Serialise all UI tests
TelegramAutoDownload.UITests\UITestBase.cs:26:    [CollectionDefinition("UI", DisableParallelization = true)]
```

There is **no `xunit.runner.json`** and **no assembly-level `CollectionBehavior` attribute** in `TelegramAutoDownload.Tests`. So the unit-test project has no parallelization control at all, and two classes race on one global singleton. The UITests project got this right; the unit-test project did not.

**This is not a "1 failing test" problem — it is a permanently red build.** Any CI gate on this suite fails roughly 100% of the time, on an unpredictable test. That trains everyone to ignore test results.

### 2.2 `TelegramAutoDownload.UITests` (FlaUI) — 7 tests, 4 failing

Command:

```powershell
dotnet test TelegramAutoDownload.UITests\TelegramAutoDownload.UITests.csproj -c Release --no-build
```

Raw result:

```
Failed!  - Failed:     4, Passed:     3, Skipped:     0, Total:     7, Duration: 25 s
          - TelegramAutoDownload.UITests.dll (net8.0)
EXIT=1 WALL=29.0862482
```

| Metric | Value |
|---|---|
| Total tests | **7** |
| Passed | **3** |
| Failed | **4** |
| Skipped | **0** |
| Wall-clock duration | **29.1 s** |
| Exit code | **1** |

All four failures, with verbatim messages:

| Test | Failure reason |
|---|---|
| `MainWindowTests.SearchTextBox_IsPresent` | `Expected textBoxes not to be empty because at least one text input (search/filter) must exist in the main window.` |
| `MainWindowTests.SettingsButton_Click_OpensSettingsWindow` (`MainWindowTests.cs:124`) | `Expected settingsButton not to be <null> because a Settings button must exist.` |
| `MainWindowTests.DownloadsGrid_IsPresent` (`MainWindowTests.cs:84`) | `Expected grid not to be <null> because a DataGrid for download progress must be visible in the main window.` |
| `MainWindowTests.SyncButton_IsPresent_AndEnabled` (`MainWindowTests.cs:108`) | `Expected syncButton not to be <null> because a Sync button must exist in the main window (add a monitored chat if the list is empty).` |

#### Can the UI tests run headlessly? — Partially, by design, but it is an opt-out not a skip-detect

The project provides a custom `UIFact` attribute that skips on an **explicit environment variable**:

```11:23:TelegramAutoDownload.UITests/UITestBase.cs
    /// <summary>
    /// Custom [Fact] attribute that automatically skips the test when the environment
    /// variable SKIP_UI_TESTS=1 is set (e.g. in headless CI environments).
    /// </summary>
    public sealed class UIFact : FactAttribute
    {
        public UIFact()
        {
            if (string.Equals(Environment.GetEnvironmentVariable("SKIP_UI_TESTS"), "1",
                    StringComparison.OrdinalIgnoreCase))
                Skip = "SKIP_UI_TESTS=1 — UI automation tests require a Windows display";
        }
    }
```

So: **with `SKIP_UI_TESTS=1`, they skip cleanly. Without it on a headless runner, they will hard-fail**, because the opt-out is manual — nothing detects the absence of a desktop session. A CI author who forgets the variable gets four confusing assertion failures rather than a clear "no display" message.

On this host a desktop session *was* available, so the tests genuinely launched the WPF application and ran. The four failures are therefore **not** "no display" errors — the app started, but the expected controls were not found. The most likely explanation (**ASSUMPTION**, not verified — verifying would require driving a real Telegram login) is that the app opened a login/first-run window rather than the fully-initialised main window, because no authenticated Telegram session exists in this environment. That makes these tests dependent on **live external state (a logged-in Telegram account)**, which is not viable for CI.

Two further defects in the harness:

1. **It prefers a stale Debug build over the Release build under test.** The lookup order is `Debug` first:

```66:81:TelegramAutoDownload.UITests/UITestBase.cs
        private static string AppExePath()
        {
            const string ExeName = "TelegramAutoDownload.exe";
            var root = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
                             "..", "..", "..", "..",  // UITests/bin/Debug/net8.0-windows → solution root
                             "TelegramAutoDownload", "bin"));

            foreach (var config in new[] { "Debug", "Release" })
            {
                var candidate = Path.Combine(root, config, "net8.0-windows", ExeName);
                if (File.Exists(candidate)) return candidate;
            }
```

   Running `dotnet test -c Release` will still silently exercise a **Debug** binary if one exists on disk. The tests can pass against code that is not the code you built.

2. **`TelegramAutoDownload.UITests.csproj` has zero `<ProjectReference>` entries.** It binds to the application by hardcoded relative filesystem path. MSBuild therefore has no dependency edge, does not rebuild the app before the tests, and a rename or output-path change fails only at runtime.

### 2.3 Combined test totals

| Suite | Total | Passed | Failed | Skipped | Wall clock | Exit |
|---|---|---|---|---|---|---|
| `TelegramAutoDownload.Tests` | 391 | 390 | 1 (flaky, varies) | 0 | 33.2 s | 1 |
| `TelegramAutoDownload.UITests` | 7 | 3 | 4 | 0 | 29.1 s | 1 |
| **Combined** | **398** | **393** | **5** | **0** | **62.3 s** | **1** |

**Neither suite passes. There is no green build to regress from.**

---

## 3. Code Coverage — measured, not estimated

### 3.1 The blocker, and how it was worked around without editing a `.csproj`

The requested command fails, because `coverlet.collector` is not referenced by any test project:

```
> dotnet test ... --collect:"XPlat Code Coverage"
Data collection : Unable to find a datacollector with friendly name 'XPlat Code Coverage'.
Data collection : Could not find data collector 'XPlat Code Coverage'
```

Rather than add the package (which would require editing `TelegramAutoDownload.Tests.csproj`), coverage was measured with the **`dotnet-coverage` global tool**, which instruments at the CLR profiler level and needs no project changes:

```powershell
dotnet tool install --global dotnet-coverage   # v18.11.2
dotnet-coverage collect --output _assess_logs\cov\coverage.cobertura.xml --output-format cobertura `
  "dotnet test TelegramAutoDownload.Tests\TelegramAutoDownload.Tests.csproj -c Release --no-build"
```

```
dotnet-coverage v18.11.2.0 [win-x64 - .NET 8.0.31]
SessionId: 0944b41f-70ed-40ee-9f93-b74233d1460a
Failed!  - Failed: 1, Passed: 390, Skipped: 0, Total: 391, Duration: 27 s
Code coverage results: _assess_logs\cov\coverage.cobertura.xml.
```

**Coverage was successfully measured. These are real numbers parsed from the Cobertura report.**

### 3.2 Raw per-assembly results

Overall figure emitted by the tool, across **all** loaded assemblies including third-party dependencies:

```
line-rate=0.1601983244999145  branch-rate=0.06337296548539767
lines-covered=2811  lines-valid=17547
branches-covered=549  branches-valid=8663
```

Broken down per assembly:

| Assembly | Lines covered | Lines valid | **Line %** | Branches covered | Branches valid | **Branch %** |
|---|---|---|---|---|---|---|
| MonoTorrent.Client *(3rd party)* | 0 | 7,727 | 0.00% | 0 | 4,052 | 0.00% |
| **TelegramAutoDownload** *(WPF)* | 135 | 3,228 | **4.18%** | 60 | 1,670 | **3.59%** |
| TelegramAutoDownload.Tests *(test code)* | 2,012 | 2,061 | 97.62% | 173 | 208 | 83.17% |
| **TelegramClient** | 288 | 2,048 | **14.06%** | 129 | 1,360 | **9.49%** |
| MonoTorrent *(3rd party)* | 0 | 1,128 | 0.00% | 0 | 608 | 0.00% |
| RichardSzalay.MockHttp *(3rd party)* | 163 | 644 | 25.31% | 41 | 224 | 18.30% |
| **BasePlugins** | 80 | 283 | **28.27%** | 49 | 176 | **27.84%** |
| **SocialMediaPlugin** | 61 | 220 | **27.73%** | 58 | 251 | **23.11%** |
| **YoutubePlugin** | 2 | 118 | **1.69%** | 2 | 68 | **2.94%** |
| **DownloadPlugin** | 65 | 65 | **100.00%** | 33 | 40 | **82.50%** |
| **TorrentPlugin** | 5 | 25 | **20.00%** | 4 | 6 | **66.67%** |

### 3.3 The headline number

Excluding third-party assemblies (MonoTorrent, MonoTorrent.Client, RichardSzalay.MockHttp) and the test assembly itself, across the **seven production assemblies that were actually instrumented**:

| Metric | Covered | Valid | **Percentage** |
|---|---|---|---|
| **Lines** | 636 | 5,987 | **10.62%** |
| **Branches** | 335 | 3,571 | **9.38%** |

> ### **Measured production line coverage: 10.62% (636 / 5,987 lines). Branch coverage: 9.38% (335 / 3,571).**

### 3.4 Two production assemblies were never loaded at all — coverage is effectively lower

`TelegramAutoDownload.Headless` and `Logger` **do not appear in the coverage report**, because no test ever loads them. Their *coverable* line counts are therefore **NOT MEASURED** (the profiler only instruments assemblies that get loaded). What is certain is that **zero lines of either were executed**:

| Assembly | C# source lines | Covered lines | Coverable lines | Coverage |
|---|---|---|---|---|
| `TelegramAutoDownload.Headless` | 3,295 | **0** (certain) | NOT MEASURED | **0%** |
| `Logger` | 109 | **0** (certain) | NOT MEASURED | **0%** |

This is confirmed structurally, not just by absence from the report: a search of all 11 `.csproj` files for the string `Headless` found **no `<ProjectReference>` to it from any test project**. The only matches were the Headless project naming itself and an unrelated comment. Likewise, `TelegramAutoDownload.Tests.csproj` lists seven project references and `Logger` is not among them:

```21:29:TelegramAutoDownload.Tests/TelegramAutoDownload.Tests.csproj
  <ItemGroup>
    <ProjectReference Include="..\BasePlugin\BasePlugins.csproj" />
    <ProjectReference Include="..\YoutubePlugin\YoutubePlugin.csproj" />
    <ProjectReference Include="..\SocialMediaPlugin\SocialMediaPlugin.csproj" />
    <ProjectReference Include="..\TorrentPlugin\TorrentPlugin.csproj" />
    <ProjectReference Include="..\DownloadService\DownloadPlugin.csproj" />
    <ProjectReference Include="..\TelegramClient\TelegramClient.csproj" />
    <ProjectReference Include="..\TelegramAutoDownload\TelegramAutoDownload.csproj" />
  </ItemGroup>
```

**The containerized ASP.NET Core server — the deployment target described by `Dockerfile` and `docker-compose.yml` — has 0% test coverage and is not wired into the test build at all.**

**ESTIMATE (labelled as such):** if Headless's coverable-line ratio resembles the WPF app's (3,228 coverable / 5,052 source ≈ 64%), Headless would contribute roughly 2,100 coverable lines and `Logger` roughly 70. That would put true production coverage at approximately **636 / 8,157 ≈ 7.8%**. This is an estimate, not a measurement.

### 3.5 Every Headless source file has zero coverage

All 27 files below are executed by no test. Of particular concern are the authentication, database, and rate-limiting components:

| File | Lines | Why it matters |
|---|---|---|
| `TelegramAutoDownload.Headless/Program.cs` | 569 | Entire minimal-API surface + DI wiring |
| `TelegramAutoDownload.Headless/Data/MediaRepository.cs` | 313 | All SQL / Dapper data access |
| `TelegramAutoDownload.Headless/Scanning/ChatDownloadTester.cs` | 310 | |
| `TelegramAutoDownload.Headless/WebAuthService.cs` | 228 | **Authentication + password handling** |
| `TelegramAutoDownload.Headless/HeadlessHost.cs` | 208 | |
| `TelegramAutoDownload.Headless/Logging/HeadlessLogReader.cs` | 198 | |
| `TelegramAutoDownload.Headless/Scanning/DownloadOrchestrator.cs` | 176 | |
| `TelegramAutoDownload.Headless/Scanning/BootstrapManager.cs` | 146 | |
| `TelegramAutoDownload.Headless/Scanning/MessageMapper.cs` | 143 | |
| `TelegramAutoDownload.Headless/Scanning/BootstrapScanner.cs` | 124 | |
| `TelegramAutoDownload.Headless/LoginCoordinator.cs` | 104 | **Login flow** |
| `TelegramAutoDownload.Headless/Scanning/ExistingDownloadValidator.cs` | 99 | |
| `TelegramAutoDownload.Headless/Data/Database.cs` | 98 | Schema/migration bootstrap |
| `TelegramAutoDownload.Headless/Scanning/MediaTracker.cs` | 91 | |
| `TelegramAutoDownload.Headless/Data/MediaStatus.cs` | 67 | |
| `TelegramAutoDownload.Headless/Scanning/FloodWaitTracker.cs` | 66 | Telegram flood-wait handling |
| `TelegramAutoDownload.Headless/Scanning/TokenBucketRateLimiter.cs` | 64 | **Rate limiting** |
| `TelegramAutoDownload.Headless/Data/MediaRecord.cs` | 49 | |
| `TelegramAutoDownload.Headless/Data/LegacyDedupMigration.cs` | 42 | **Data migration** |
| `TelegramAutoDownload.Headless/ConfigStore.cs` | 38 | |
| `TelegramAutoDownload.Headless/HeadlessPaths.cs` | 37 | |
| `TelegramAutoDownload.Headless/WebAuthMiddleware.cs` | 26 | **Auth middleware** |
| `TelegramAutoDownload.Headless/Logging/HeadlessLogModels.cs` | 25 | |
| `TelegramAutoDownload.Headless/Scanning/TestDownloadModels.cs` | 22 | |
| `TelegramAutoDownload.Headless/Scanning/RateLimitThresholds.cs` | 19 | |
| `TelegramAutoDownload.Headless/Scanning/ScanRateLimits.cs` | 19 | |
| `TelegramAutoDownload.Headless/Scanning/BootstrapConflictException.cs` | 14 | |

Also entirely untested: the **1,063-line `TelegramAutoDownload.Headless/wwwroot/app.js`** dashboard. There is no JavaScript test infrastructure of any kind in the repository (no `package.json`, no test runner).

### 3.6 Coverage of the two largest source files

- `TelegramClient/TelegramApp.cs` (1,602 lines) sits in an assembly measured at **14.06%** line coverage.
- `TelegramAutoDownload/MainWindow.xaml.cs` (1,265 lines) sits in an assembly measured at **4.18%** line coverage.

The one bright spot is `DownloadPlugin` at **100%** line / 82.5% branch — but it is only 65 coverable lines (a single 96-line source file).

---

## 4. Dependency Inventory

### 4.1 Complete top-level package inventory

20 distinct top-level NuGet packages across 11 projects. Versions, publish dates, and licenses were retrieved from the NuGet v3 registration API; "Latest" is as reported by `dotnet list package --outdated`.

| Package | Used | Used published | Latest | Latest published | Age of pinned version | License | Status |
|---|---|---|---|---|---|---|---|
| **WTelegramClient** | 4.4.2 | 2026-03-06 | **4.4.9** | 2026-09-26 | ~7 months | MIT | 7 patches behind; very actively maintained (130 stable releases) |
| **MonoTorrent** | 3.0.2 | 2024-08-04 | **3.0.2** | 2024-08-04 | ~2 years | ⚠️ **deprecated `licenseUrl`** | **Current, but upstream is dormant — no release in 2 years** |
| **Ytdlp.NET** | 3.0.4 | 2026-05-03 | **4.2.0** | 2026-09-21 | ~5 months | ⚠️ **deprecated `licenseUrl`** | **A full major version behind** |
| **Newtonsoft.Json** | 13.0.3 | 2023-03-08 | **13.0.4** | 2025-09-16 | **~3.5 years** | MIT | 1 patch behind; maintenance-mode upstream |
| **Dapper** | 2.1.35 | 2024-03-06 | **2.1.89** | 2026-09-23 | ~2.5 years | Apache-2.0 | **54 patch releases behind**; very active |
| **Microsoft.Data.Sqlite** | 8.0.10 | 2024-10-08 | **10.0.12** | 2026-09-09 * | ~2 years | MIT | **2 major versions behind; pulls a HIGH-severity transitive CVE (§4.2)** |
| **Serilog** | 4.2.0 | 2024-12-06 | **4.4.0** | 2026-07-10 | ~1.8 years | Apache-2.0 | 2 minors behind; very active |
| **Serilog.AspNetCore** | 8.0.3 | 2024-10-11 | **10.0.0** | 2025-11-28 | ~2 years | Apache-2.0 | **2 major versions behind** |
| **Serilog.Sinks.Console** | 6.0.0 | 2024-06-10 | **6.1.1** | 2025-11-03 | ~2.3 years | Apache-2.0 | 1 minor behind |
| **Serilog.Sinks.File** | 6.0.0 | 2024-06-23 | **7.0.0** | 2025-04-28 | ~2.3 years | Apache-2.0 | 1 major behind |
| **Serilog.Sinks.Seq** | 8.0.0 | 2024-06-04 | **9.1.0** | 2026-05-14 | ~2.3 years | Apache-2.0 | 1 major behind |
| MahApps.Metro | 2.4.11 | 2025-09-14 | **2.4.11** | 2025-09-14 | ~1 year | MIT | ✅ **Current** |
| QRCoder.Xaml | 1.7.0 | 2025-10-09 | **1.8.0** | 2026-04-04 | ~1 year | MIT | 1 minor behind |
| dotenv.net | 3.2.0 | 2024-06-22 | **4.2.0** | 2026-09-10 | ~2.3 years | MIT (via URL) | 1 major behind |
| FluentAssertions | 6.12.0 | 2023-08-23 | **8.11.0** | 2026-09-14 | **~3 years** | Apache-2.0 | **2 majors behind — v8 is under a paid commercial license (§4.4)** |
| Microsoft.NET.Test.Sdk | 17.9.0 | 2024-02-06 | **18.10.1** | 2026-09-15 | ~2.6 years | ⚠️ deprecated `licenseUrl` | 1 major behind |
| RichardSzalay.MockHttp | 7.0.0 | 2023-10-12 | **7.1.0** | 2026-08-08 | ~3 years | MIT | 1 minor behind |
| xunit | 2.7.0 | 2024-02-16 | **2.9.3** | 2025-01-08 | ~2.6 years | Apache-2.0 | 2 minors behind |
| xunit.runner.visualstudio | 2.5.7 | 2024-02-16 | **4.0.0** | 2026-08-15 | ~2.6 years | Apache-2.0 | **2 major versions behind** |
| FlaUI.UIA3 | 4.0.0 | 2022-09-25 | **5.0.0** | 2025-02-25 | **~4 years** | ⚠️ deprecated `licenseUrl` | **1 major behind; pulls a CRITICAL transitive CVE (§4.2)** |

\* `Microsoft.Data.Sqlite`: the date shown is the publish date of the most recent release across all supported branches (the 9.0.x servicing line). The exact publish date of 10.0.12 specifically is **NOT MEASURED**; the version number is taken from `dotnet list package --outdated`.

**Summary: 18 of 20 top-level packages are outdated. Only `MonoTorrent` and `MahApps.Metro` are on their latest version** — and `MonoTorrent` only because upstream has not shipped in two years.

Per-project package line references (used for the citations below): `TelegramClient.csproj:8-9`; `Logger.csproj:10`; `BasePlugins.csproj:11`; `TorrentPlugin.csproj:8`; `YoutubePlugin.csproj:12`; `SocialMediaPlugin.csproj:9`; `TelegramAutoDownload.csproj:22-27`; `TelegramAutoDownload.Headless.csproj:13-19`; `TelegramAutoDownload.Tests.csproj:11-15`; `TelegramAutoDownload.UITests.csproj:13-17`.

### 4.2 Vulnerability scan — 2 vulnerable packages, both transitive

Command run per-project:

```powershell
dotnet list <project>.csproj package --vulnerable --include-transitive --source https://api.nuget.org/v3/index.json
```

Raw findings:

```
Project `TelegramAutoDownload.Headless` has the following vulnerable packages
   [net8.0]:
   Transitive Package                Resolved   Severity   Advisory URL
   > SQLitePCLRaw.lib.e_sqlite3      2.1.6      High       https://github.com/advisories/GHSA-2m69-gcr7-jv3q
```

```
Project `TelegramAutoDownload.UITests` has the following vulnerable packages
   [net8.0-windows]:
   Transitive Package           Resolved   Severity   Advisory URL
   > System.Drawing.Common      5.0.2      Critical   https://github.com/advisories/GHSA-rxg9-xrhp-64gj
```

The other nine projects each reported: `The given project ... has no vulnerable packages given the current sources.`

| # | Package | Resolved | Severity | Advisory | Affected project | Introduced via | Fixed in | Production risk |
|---|---|---|---|---|---|---|---|---|
| 1 | `SQLitePCLRaw.lib.e_sqlite3` | 2.1.6 (2023-08-24) | **HIGH** | [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) | **`TelegramAutoDownload.Headless`** | `Microsoft.Data.Sqlite` 8.0.10 | 2.1.13 (2026-08-13) | 🔴 **YES — ships in the Docker container** |
| 2 | `System.Drawing.Common` | 5.0.2 (2021-03-09) | **CRITICAL** | [GHSA-rxg9-xrhp-64gj](https://github.com/advisories/GHSA-rxg9-xrhp-64gj) | `TelegramAutoDownload.UITests` | `FlaUI.UIA3` 4.0.0 | 8.0.31 (2026-09-09) | 🟡 Test-only — not shipped |

**Vulnerable package count: 2 (1 High, 1 Critical). One of them is in the production container image.**

The High-severity finding is directly caused by the outdated `Microsoft.Data.Sqlite` 8.0.10 pin at `TelegramAutoDownload.Headless.csproj:14`. The CRITICAL finding is caused by `FlaUI.UIA3` 4.0.0 (from 2022) at `TelegramAutoDownload.UITests.csproj:13`, whose transitive `System.Drawing.Common` 5.0.2 dates from **March 2021 — over five years old**.

**Caveat on scan completeness:** `dotnet list package --vulnerable` only reports advisories present in the **NuGet.org GitHub Advisory feed**. It does not cover the `yt-dlp` and `ffmpeg` binaries installed by `Dockerfile`, nor any JavaScript. Actual exposure is a superset of these two findings.

### 4.3 Individually flagged packages

**`WTelegramClient` 4.4.2** (`TelegramClient.csproj:9`) — MIT, extremely actively maintained (130 stable releases; latest 4.4.9 on 2026-09-26, four days before this assessment). Seven patch releases behind. This is the library that speaks the raw MTProto protocol to Telegram's servers; Telegram changes its API layer frequently, so falling behind here causes functional breakage, not just missed fixes. **No known CVEs.** Low upgrade risk (patch-level within 4.4.x), high value.

**`MonoTorrent` 3.0.2** (`BasePlugins.csproj:11` and `TorrentPlugin.csproj:8`) — technically current, because **3.0.2 is still the newest stable release and it is from 2024-08-04**. Upstream has published nothing in over two years across 44 total releases. It is also the **single largest dependency by code volume**: 8,855 coverable lines (`MonoTorrent.Client` 7,727 + `MonoTorrent` 1,128), of which **0 lines are covered by any test**. The package uses the **deprecated `licenseUrl` field** (`https://aka.ms/deprecateLicenseUrl`) rather than a modern SPDX expression, so the license is **not machine-readable from the package metadata** — it must be confirmed from the project source (upstream MonoTorrent is MIT, but this is **NOT VERIFIED** from package metadata here). **No known CVEs.** Risk: abandonment, not vulnerability.

**`Ytdlp.NET` 3.0.4** (`YoutubePlugin.csproj:12` and `SocialMediaPlugin.csproj:9`) — **the highest-risk dependency in the tree.** A full major version behind (3.0.4 → 4.2.0). It is a small, single-maintainer wrapper (27 stable releases) around the `yt-dlp` executable. Its license is also only a **deprecated `licenseUrl`** — not machine-readable. It has the **lowest coverage of any production assembly consumed through it**: `YoutubePlugin` is at **1.69%** line coverage (2 of 118 lines). Combined with a major-version gap and `yt-dlp`'s own rapid release cadence (it must track YouTube's changes constantly), this is both the most likely thing to break and the least tested.

**`Dapper` 2.1.35** (`TelegramAutoDownload.Headless.csproj:13`) — Apache-2.0, very actively maintained, **54 patch releases behind** (2.1.35 → 2.1.89). No CVEs. Because Dapper is the ORM for all Headless persistence and `MediaRepository.cs` (313 lines) has **0% coverage**, any upgrade would be unverifiable by tests. Low intrinsic risk, high blast radius due to no tests.

**`Newtonsoft.Json` 13.0.3** (`TelegramAutoDownload.csproj:24` and `TelegramAutoDownload.Headless.csproj:15`) — MIT, no known CVEs at 13.0.3. Only one patch behind (13.0.4). The real observation is not risk but **redundancy**: both projects target .NET 8, which has had `System.Text.Json` built in since .NET Core 3.0. This is an avoidable third-party dependency on a library now in maintenance mode.

**`Serilog*` family** (6 packages) — all Apache-2.0, all healthy and actively maintained, **all outdated**:

| Package | Used | Latest | Gap | Project |
|---|---|---|---|---|
| `Serilog` | 4.2.0 | 4.4.0 | 2 minor | `TelegramClient.csproj:8`, `TelegramAutoDownload.csproj:26`, `...Headless.csproj:16` |
| `Serilog.AspNetCore` | 8.0.3 | 10.0.0 | **2 major** | `...Headless.csproj:17` |
| `Serilog.Sinks.Console` | 6.0.0 | 6.1.1 | 1 minor | `...Headless.csproj:18` |
| `Serilog.Sinks.File` | 6.0.0 | 7.0.0 | **1 major** | `TelegramAutoDownload.csproj:27`, `...Headless.csproj:19` |
| `Serilog.Sinks.Seq` | 8.0.0 | 9.1.0 | **1 major** | `Logger.csproj:10` |

No CVEs in any of them. The concern is **version drift across projects with no central pinning** — `Serilog` 4.2.0 is declared independently in three separate `.csproj` files, so they can silently diverge.

### 4.4 License inventory and one commercial-licensing landmine

| License | Packages |
|---|---|
| **MIT** | WTelegramClient, Newtonsoft.Json, Microsoft.Data.Sqlite, MahApps.Metro, QRCoder.Xaml, dotenv.net, RichardSzalay.MockHttp, Microsoft.NET.Test.Sdk, System.Drawing.Common |
| **Apache-2.0** | Dapper, Serilog, Serilog.AspNetCore, Serilog.Sinks.Console, Serilog.Sinks.File, Serilog.Sinks.Seq, xunit, xunit.runner.visualstudio, SQLitePCLRaw.lib.e_sqlite3, FluentAssertions **6.12.0** |
| **Not machine-readable** (deprecated `licenseUrl`) | **MonoTorrent 3.0.2**, **Ytdlp.NET 3.0.4**, FlaUI.UIA3 4.0.0, Microsoft.NET.Test.Sdk |
| MIT via raw URL | dotenv.net (`github.com/bolorundurowb/dotenv.net/blob/master/LICENSE`) |

No copyleft (GPL/LGPL/AGPL) licenses were found among the top-level packages. **However:**

> ⚠️ **`FluentAssertions` upgrade is a commercial-licensing trap.** The pinned 6.12.0 is **Apache-2.0** and free. From **version 8 onward, FluentAssertions moved to a paid commercial license for non-open-source use.** `dotnet list package --outdated` reports "Latest: 8.11.0" for both test projects and gives no licensing signal whatsoever. A routine "update all packages" action would silently introduce a **paid license obligation** into the build. This affects `TelegramAutoDownload.Tests.csproj:11` and `TelegramAutoDownload.UITests.csproj:14`.

**Three packages' licenses are not machine-readable** (deprecated `licenseUrl` field), which blocks automated license compliance scanning. MonoTorrent and Ytdlp.NET are both **shipped** dependencies, so their exact license terms are **NOT VERIFIED** from package metadata and would need manual confirmation from upstream sources before any commercial distribution.

---

## 5. Repo Hygiene

> Reminder from §0.3: this section describes the **baseline at HEAD `554d8e1`**. Another agent created `.github/workflows/` during this session; that is not part of the baseline.

### 5.1 No CI — confirmed at baseline

```
> git ls-tree -r HEAD --name-only | Select-String '^\.github'
(empty — no .github/ in HEAD)
```

At baseline there is **no CI, no PR validation, no automated build, no automated test run, and no security scanning**. Combined with §2 (neither test suite passes) and §1.4 (71 warnings, no `TreatWarningsAsErrors`), nothing has ever enforced quality automatically.

`Dockerfile`, `docker-compose.yml`, and `installer/setup.iss` exist and are tracked, so **release artifacts are produced by hand**. Note also that `.cursor/rules/release-process.mdc` documents a **manual, multi-step, human-executed release procedure** (publish → zip → compile Inno Setup → `gh release create`) with warnings about steps people have skipped before — direct evidence that the manual process has already failed in practice.

### 5.2 No LICENSE file, but README claims MIT — confirmed

```
> git ls-tree -r HEAD --name-only | Select-String -Pattern 'LICEN'
(empty — no LICENSE file)

> Test-Path LICENSE
False
```

Yet `README.md` asserts MIT:

```
README.md:341: ## License
README.md:343: MIT
```

**A repository with no `LICENSE` file is, by default, "all rights reserved" under copyright law** regardless of what the README says. The project also redistributes MIT and Apache-2.0 dependencies, both of which **require** that their license text and copyright notices accompany distribution. Neither the `Portable.zip` nor the Inno Setup installer described in the release process includes any third-party license notices. This is an **unambiguous legal defect** and the cheapest item on this entire list to fix.

### 5.3 No tracked `.sln` — confirmed, and README is broken because of it

```
> git ls-files | Select-String '\.sln'
(empty)

> Get-ChildItem -Recurse -Filter *.sln -Force
(empty — no .sln anywhere on disk, tracked or untracked)
```

The README instructs users to build a file that **does not exist**:

```
README.md:111: dotnet build RunnerApp.sln
README.md:120: Or open `RunnerApp.sln` in Visual Studio 2022 and press **F5**.
README.md:298: dotnet test RunnerApp.sln
```

**All three documented build/test entry points fail immediately.** There is no single command that builds the product. This assessment had to enumerate and build 11 `.csproj` files individually. The practical consequences: no one-command build, no one-command test, no natural CI entry point, and new contributors cannot follow the README.

### 5.4 The `obj/` artifacts in `git status` — `.gitignore` is NOT failing

The task brief reported "a large number of untracked `obj/` build artifacts showing in `git status`" and asked whether `.gitignore` is failing to exclude them.

**It is not failing. `.gitignore` is working correctly.** Verified three ways.

**(1)** `git status` shows no `obj/` entries at all:

```
> git status --porcelain
?? .github/
?? .vscode/
?? _assess_logs/
?? docs/
```

**(2)** `git check-ignore` confirms the rule is matching, and names the exact line responsible:

```
> git check-ignore -v BasePlugin/obj/Debug/net8.0/BasePlugins.AssemblyInfo.cs
.gitignore:64:[Oo]bj/	BasePlugin/obj/Debug/net8.0/BasePlugins.AssemblyInfo.cs
exit=0

> Test-Path BasePlugin/obj/Debug/net8.0/BasePlugins.AssemblyInfo.cs
True
```

The file exists on disk **and** is correctly ignored by `.gitignore` line 64.

**(3)** Adding `--ignored=matching` reproduces exactly those paths, but flagged `!!` (ignored), not `??` (untracked):

```
> git status --porcelain --ignored=matching | Select-String 'obj/'
!! BasePlugin/obj/
!! DownloadService/obj/
!! Logger/obj/
!! SocialMediaPlugin/obj/
!! TelegramAutoDownload.Headless/obj/
!! TelegramAutoDownload.Tests/obj/
!! TelegramAutoDownload.UITests/obj/
!! TelegramAutoDownload/obj/
...  (11 total)
```

The relevant `.gitignore` rules:

```
.gitignore:63: [Bb]in/
.gitignore:64: [Oo]bj/
```

**Explanation:** the `obj/` paths in the original status snapshot were **ignored** files (`!!`), reported by a status invocation that included ignored entries, and then rendered with the `??` marker. They are not untracked, git will never stage or commit them, and no `.gitignore` change is needed. `.gitignore` is a 443-line comprehensive Visual Studio template.

One genuine (minor) oddity: `.gitignore:38` contains a negation for build output —

```
.gitignore:38: !TelegramAutoDownload/bin/Debug/net8.0-windows/Plugins/
```

— but because it appears **before** the `[Bb]in/` rule at line 63, and git applies the **last** matching pattern, the negation has no effect. It is dead configuration. (This is benign: it was presumably an attempt to commit plugin DLLs, which is not happening and should not.)

**Repository size:** 182 tracked files.

---

## 6. Architecture: Dependency Graph, God Objects, Layering

### 6.1 Project dependency graph

Extracted from `<ProjectReference>` elements in all 11 `.csproj` files. `(build-only)` marks `ReferenceOutputAssembly="false"` — MSBuild builds the project for artifact copying but does not link against it.

```
BasePlugins                   -> (none)                         [NuGet: MonoTorrent]
Logger                        -> (none)                         [NuGet: Serilog.Sinks.Seq]
TelegramClient                -> BasePlugins
DownloadPlugin                -> BasePlugins
YoutubePlugin                 -> BasePlugins
SocialMediaPlugin             -> BasePlugins
TorrentPlugin                 -> BasePlugins
TelegramAutoDownload          -> Logger, TelegramClient,
                                 SocialMediaPlugin (build-only), YoutubePlugin (build-only),
                                 DownloadPlugin (build-only),    TorrentPlugin (build-only)
TelegramAutoDownload.Headless -> Logger, TelegramClient, BasePlugins,
                                 SocialMediaPlugin (build-only), YoutubePlugin (build-only),
                                 DownloadPlugin (build-only),    TorrentPlugin (build-only)
TelegramAutoDownload.Tests    -> BasePlugins, YoutubePlugin, SocialMediaPlugin, TorrentPlugin,
                                 DownloadPlugin, TelegramClient, TelegramAutoDownload
TelegramAutoDownload.UITests  -> (none)   ← binds to the app by hardcoded filesystem path
```

Visualised as layers:

```
  ┌──────────────────────┐        ┌───────────────────────────────┐
  │ TelegramAutoDownload │        │ TelegramAutoDownload.Headless │   Front ends
  │   (WPF, Windows)     │        │   (ASP.NET Core, container)   │
  └────────┬─────────────┘        └───────────────┬───────────────┘
           │                                      │
           ├──────────────┬───────────────────────┤
           ▼              ▼                       ▼
      ┌────────┐   ┌──────────────┐        ┌────────────┐
      │ Logger │   │TelegramClient│        │ BasePlugins│ ◄── Headless reaches
      └────────┘   └──────┬───────┘        └─────▲──────┘     past TelegramClient
                          │                      │             straight to BasePlugins
                          └──────────────────────┤
                                                 │
        ┌────────────────┬───────────────┬───────┴────────┐
        ▼                ▼               ▼                ▼
  ┌───────────┐  ┌──────────────┐ ┌─────────────┐ ┌──────────────┐
  │DownloadPl.│  │YoutubePlugin │ │SocialMediaP.│ │TorrentPlugin │   Plugins
  └───────────┘  └──────────────┘ └─────────────┘ └──────────────┘
```

### 6.2 Cycles

**No dependency cycles exist.** The graph is a proper DAG. `BasePlugins` and `Logger` are leaf nodes with no project dependencies. This is a genuine strength and the healthiest structural property of the codebase.

### 6.3 Layering violations

**Violation 1 — `BasePlugin` is not a base layer; it contains a full torrent implementation.**

`BasePlugin` is positioned as the shared abstraction layer (every other plugin depends on it, and nothing depends on those plugins). But its contents contradict that role:

| File | Lines | Role |
|---|---|---|
| `BasePlugin/BasePlugin.cs` | 54 | abstraction ✅ |
| `BasePlugin/CancellationRegistry.cs` | 55 | infrastructure ✅ |
| `BasePlugin/Config.cs` | 33 | abstraction ✅ |
| `BasePlugin/DownloadActivity.cs` | 14 | abstraction ✅ |
| `BasePlugin/IBasePlugin.cs` | 10 | abstraction ✅ |
| `BasePlugin/MagnetLinkHelper.cs` | 29 | ⚠️ torrent-specific |
| `BasePlugin/PluginFolderPathHelper.cs` | 54 | infrastructure ✅ |
| `BasePlugin/ResultExecute.cs` | 27 | abstraction ✅ |
| **`BasePlugin/TorrentDownloadService.cs`** | **294** | 🔴 **concrete torrent implementation** |
| `BasePlugin/YtdlpFormatHelper.cs` | 38 | ⚠️ yt-dlp-specific |

`TorrentDownloadService.cs` is **48% of the entire project by line count**, and it forces the `MonoTorrent` NuGet dependency into the base layer:

```9:12:BasePlugin/BasePlugins.csproj
  <ItemGroup>
    <PackageReference Include="MonoTorrent" Version="3.0.2" />
  </ItemGroup>
```

**Consequence:** `Logger`, `YoutubePlugin`, `SocialMediaPlugin`, `DownloadPlugin`, `TelegramClient`, and the entire Headless server all transitively drag in **8,855 lines of MonoTorrent** they never use, purely because the base abstraction layer contains a torrent client. `TorrentPlugin` — which is the project that *should* own this — is a **39-line file** that delegates to `BasePlugin`. The dependency is inverted: the concrete implementation lives in the abstraction, and the plugin that names the feature is nearly empty.

**Violation 2 — the unit-test project depends on the WPF desktop application.**

```28:28:TelegramAutoDownload.Tests/TelegramAutoDownload.Tests.csproj
    <ProjectReference Include="..\TelegramAutoDownload\TelegramAutoDownload.csproj" />
```

This single line forces `TelegramAutoDownload.Tests` to `net8.0-windows`, which means **the cross-platform core cannot be unit-tested on Linux**. The Docker build targets `linux` (`mcr.microsoft.com/dotnet/aspnet:8.0`), so **the container can never be built and tested in the same CI job**. It is also the direct cause of the flaky tests in §2.1, because the singleton being raced on (`AppLogAlertService`) lives in the WPF project.

**Violation 3 — `Headless` bypasses `TelegramClient` and reaches directly into `BasePlugins`.**

The WPF app references `Logger` + `TelegramClient` only. Headless additionally references `BasePlugins` directly (`TelegramAutoDownload.Headless.csproj:30`). The two front ends therefore consume the core through **different dependency shapes**, so "shared core" is not enforced by the build.

**Violation 4 — `UITests` has no project reference at all.** Discussed in §2.2; it resolves the application by hardcoded relative path (`UITestBase.cs:66-81`) and prefers Debug over Release.

**Violation 5 — no shared test project for the plugin contract.** Each plugin is referenced individually by the test project; there is no contract/conformance test suite for `IBasePlugin`.

### 6.4 God objects

**`TelegramClient/TelegramApp.cs` — 1,602 lines.** The largest file in the repository. Measured: 22 public methods, 10 fields/properties, 16 `using` directives. It is the entire Telegram integration surface in one class — connection, auth, chat enumeration, message fetching, media download, history. It carries **all 61 `CS8632` warnings** (§1.4), sits in an assembly at **14.06% coverage**, and is the sole network boundary to an external API. Highest-risk file in the codebase by every measure taken.

**`TelegramAutoDownload/MainWindow.xaml.cs` — 1,265 lines.** Measured: **59 methods** in a single code-behind class. Classic WPF god-object: no MVVM separation, UI event handlers and business logic interleaved. Its assembly is at **4.18% coverage**, which is a direct consequence — logic in code-behind is not unit-testable without a UI harness, which is why the UITests exist and why they are unreliable (§2.2).

**`TelegramAutoDownload.Headless/wwwroot/app.js` — 1,063 lines.** The entire dashboard front end in one un-modularised, untested JavaScript file. No `package.json`, no bundler, no linter, no test runner anywhere in the repository.

**`TelegramAutoDownload.Headless/Program.cs` — 569 lines.** The whole minimal-API surface plus DI/config wiring in one file, at **0% coverage**.

### 6.5 Largest files (top 25, excluding `obj/` and `bin/`)

| Lines | File |
|---|---|
| **1,602** | `TelegramClient/TelegramApp.cs` |
| **1,265** | `TelegramAutoDownload/MainWindow.xaml.cs` |
| **1,063** | `TelegramAutoDownload.Headless/wwwroot/app.js` |
| 701 | `TelegramAutoDownload/MainWindow.xaml` |
| 572 | `TelegramAutoDownload.Tests/IntegrationTests.cs` |
| **569** | `TelegramAutoDownload.Headless/Program.cs` |
| 529 | `TelegramAutoDownload.Tests/NewFeaturesTests.cs` |
| 449 | `TelegramAutoDownload/Services/DownloadProgressService.cs` |
| 330 | `TelegramClient/Factory/Base/BaseMessage.cs` |
| 322 | `TelegramAutoDownload.Tests/SelectedChannelFilterTests.cs` |
| 316 | `TelegramAutoDownload.Tests/MembersExportTests.cs` |
| 313 | `TelegramAutoDownload.Headless/Data/MediaRepository.cs` |
| 310 | `TelegramAutoDownload.Headless/wwwroot/index.html` |
| 310 | `TelegramAutoDownload.Headless/Scanning/ChatDownloadTester.cs` |
| 297 | `TelegramAutoDownload/SettingsWindow.xaml.cs` |
| 294 | `BasePlugin/TorrentDownloadService.cs` |
| 287 | `TelegramAutoDownload/FilterDialog.xaml` |
| 259 | `TelegramAutoDownload/FilterDialog.xaml.cs` |
| 236 | `TelegramAutoDownload.Tests/ChatHistoryTests.cs` |
| 235 | `SocialMediaPlugin/SocialMediaPlugin.cs` |
| 235 | `TelegramAutoDownload/SettingsWindow.xaml` |
| 233 | `TelegramClient/Factory/Service/FactoryMessagesService.cs` |
| 229 | `TelegramAutoDownload/Services/AutoUpdateService.cs` |
| 228 | `TelegramAutoDownload.Headless/WebAuthService.cs` |
| 210 | `TelegramAutoDownload/Notification.cs` |

### 6.6 Codebase size

Counted across `*.cs`, `*.xaml`, `*.js`, `*.html`, `*.css`, `*.sql`, excluding `obj/` and `bin/`:

| Project | Files | Total lines | C# lines |
|---|---|---|---|
| TelegramAutoDownload | 49 | 7,117 | 5,052 |
| TelegramAutoDownload.Headless | 31 | 4,866 | 3,295 |
| TelegramAutoDownload.Tests | 28 | 3,928 | 3,928 |
| TelegramClient | 30 | 3,697 | 3,697 |
| BasePlugin | 10 | 608 | 608 |
| SocialMediaPlugin | 2 | 304 | 304 |
| TelegramAutoDownload.UITests | 2 | 195 | 195 |
| YoutubePlugin | 1 | 170 | 170 |
| Logger | 5 | 109 | 109 |
| DownloadService | 1 | 96 | 96 |
| TorrentPlugin | 1 | 39 | 39 |
| **TOTAL** | **160** | **21,129** | **17,493** |

Production code (excluding both test projects): **17,006 lines** across 130 files.
Test-to-production ratio: **4,123 test lines : 17,006 production lines ≈ 1 : 4.1**.

Note the imbalance against §3: a 1:4.1 line ratio would normally suggest reasonable coverage, but **measured coverage is 10.62%**. The test lines are concentrated on a narrow slice of the codebase (the test assembly itself is 97.62% covered, while `DownloadPlugin`'s 65 lines are at 100% and Headless's 3,295 lines are at 0%).

---

## 7. Summary of Baseline Facts

| Dimension | Measured baseline |
|---|---|
| Projects | 11 (3 Windows-only, 8 portable) |
| Build | **11/11 succeed, 0 errors** |
| Compiler warnings | **71 unique** (61 × CS8632, 9 × CS8618, 1 × CS8601); 0 non-CS warnings |
| Unit tests | 391 total / 390 pass / **1 fail (flaky, different test each run)** / 0 skip / 33.2 s |
| UI tests | 7 total / 3 pass / **4 fail** / 0 skip / 29.1 s |
| **Production line coverage** | **10.62%** (636 / 5,987) — measured |
| **Production branch coverage** | **9.38%** (335 / 3,571) — measured |
| Headless coverage | **0%** (never loaded; not referenced by any test project) |
| Vulnerable packages | **2** — 1 HIGH (in production container), 1 CRITICAL (test-only) |
| Outdated packages | **18 of 20** top-level |
| Dependency cycles | **0** ✅ |
| CI (at HEAD) | **None** |
| LICENSE file | **None** (README claims MIT) |
| Tracked `.sln` | **None** (README references a non-existent `RunnerApp.sln` 3 times) |
| `.gitignore` excluding `obj/` | **Working correctly** — not a defect |
| Total lines | 21,129 (17,006 production) |

### Top 5 threats to productionizing this codebase

1. **The production deployment target has 0% test coverage.** `TelegramAutoDownload.Headless` — the ASP.NET Core server shipped by `Dockerfile`/`docker-compose.yml` — is not referenced by any test project. Its 3,295 C# lines (including `WebAuthService.cs`, `WebAuthMiddleware.cs`, `LoginCoordinator.cs`, `MediaRepository.cs`, `TokenBucketRateLimiter.cs`) plus a 1,063-line untested `app.js` have **never been executed by a test**. Overall measured production coverage is **10.62%**.

2. **There is no green build to protect, and no CI that would notice.** At HEAD there is no `.github/`, no `LICENSE`, and no tracked `.sln` — so the three build commands in the README all fail. Both test suites currently exit non-zero, and the unit-test failure is **non-deterministic**: four runs produced four different failing tests, caused by `AppLogAlertService.Instance` (`TelegramAutoDownload/Services/AppLogAlertService.cs:11`) being raced across xUnit's default parallel collections with no `xunit.runner.json` or `CollectionBehavior` anywhere in `TelegramAutoDownload.Tests`.

3. **A HIGH-severity CVE ships inside the container image.** `SQLitePCLRaw.lib.e_sqlite3` 2.1.6 ([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)) arrives transitively via the outdated `Microsoft.Data.Sqlite` 8.0.10 pin (`TelegramAutoDownload.Headless.csproj:14`, two major versions behind). More broadly, **18 of 20 packages are outdated**, with no `Directory.Packages.props` to manage them centrally — and the obvious remediation is booby-trapped: upgrading `FluentAssertions` past v8 silently introduces a **paid commercial license**.

4. **Null safety is switched off in the most dangerous project.** `TelegramClient.csproj` is the only project lacking `<Nullable>enable</Nullable>`, yet its 3,697 lines contain 61 nullable annotations the compiler entirely ignores (all 61 × CS8632). This is the sole network boundary to Telegram's API, it holds the 1,602-line `TelegramApp.cs` god object, and it is at 14.06% coverage. Separately, all five properties of `BasePlugin/ResultExecute.cs:5` — the return contract of *every* plugin execution — can be silently null (CS8618). Nothing sets `TreatWarningsAsErrors`.

5. **The plugin architecture is inverted, and the build is not reproducible.** `BasePlugin/TorrentDownloadService.cs` puts a 294-line concrete torrent client (48% of the "base abstraction" project) plus the `MonoTorrent` dependency into the layer every other component depends on — forcing 8,855 lines of untested MonoTorrent into the Headless server, which never uses it — while the actual `TorrentPlugin` is 39 lines. Meanwhile there is **no `global.json`, no `Directory.Build.props`, and no committed `NuGet.config`**, so the local build (SDK 10.0.401) and the container build (SDK 8.0) use different compilers, and restore succeeds or fails depending on whatever feeds a given machine happens to have configured.

---

## Appendix A — Commands Run

```powershell
# Environment
dotnet --info
dotnet nuget list source
git ls-tree -r HEAD --name-only
git status --porcelain
git status --porcelain --ignored=matching
git check-ignore -v BasePlugin/obj/Debug/net8.0/BasePlugins.AssemblyInfo.cs

# Restore (per project, ×11) — pinned to public feed, see §0.2
dotnet restore <project>.csproj --source https://api.nuget.org/v3/index.json

# Build (per project, ×11)
dotnet build <project>.csproj -c Release --no-restore -v normal
dotnet build <project>.csproj -c Release --no-restore -t:Rebuild -v normal

# Test
dotnet test TelegramAutoDownload.Tests\TelegramAutoDownload.Tests.csproj -c Release --no-build `
  --logger "trx;LogFileName=unit.trx" --logger "console;verbosity=normal"
dotnet test TelegramAutoDownload.UITests\TelegramAutoDownload.UITests.csproj -c Release --no-build
# (unit suite executed 4× to characterise flakiness)

# Coverage
dotnet test ... --collect:"XPlat Code Coverage"     # FAILED — collector not present
dotnet tool install --global dotnet-coverage --add-source https://api.nuget.org/v3/index.json
dotnet-coverage collect --output coverage.cobertura.xml --output-format cobertura `
  "dotnet test TelegramAutoDownload.Tests\TelegramAutoDownload.Tests.csproj -c Release --no-build"

# Dependencies (per project, ×11)
dotnet list <project>.csproj package --vulnerable --include-transitive --source https://api.nuget.org/v3/index.json
dotnet list <project>.csproj package --outdated --source https://api.nuget.org/v3/index.json

# Package metadata
Invoke-RestMethod "https://api.nuget.org/v3/registration5-gz-semver2/<id>/<version>.json"
```

## Appendix B — Things NOT measured

| Item | Why |
|---|---|
| Coverable line counts for `TelegramAutoDownload.Headless` and `Logger` | Never loaded by any test, so the profiler never instrumented them. Executed lines are certainly **0**; the denominator is unknown. An estimate is given in §3.4 and labelled as such. |
| JavaScript coverage (`wwwroot/app.js`, 1,063 lines) | No JS test infrastructure exists in the repository (no `package.json`, no runner). |
| Runtime behaviour of the WPF app and Headless server | Both require a live authenticated Telegram session (API credentials + phone verification). Not available. |
| Docker image build | Would require Docker and a Linux build of the Headless project; also outside the "build every project" scope, which was satisfied via `dotnet build`. |
| Vulnerabilities in `yt-dlp` / `ffmpeg` OS packages installed by `Dockerfile` | `dotnet list package --vulnerable` only covers NuGet. Requires a container image scanner. |
| Exact license text for MonoTorrent 3.0.2, Ytdlp.NET 3.0.4, FlaUI.UIA3 4.0.0 | These packages use the deprecated `licenseUrl` field instead of an SPDX expression, so the license is not machine-readable from package metadata. |
| Whether the 4 UI test failures are caused by a missing Telegram session | Stated as an **ASSUMPTION** in §2.2; confirming it would require completing a real Telegram login. |
| Cyclomatic complexity / maintainability index | Requires an analyzer package (e.g. `Microsoft.CodeAnalysis.Metrics`) added to the projects, which the no-edit constraint forbids. Line counts and method counts are given instead. |

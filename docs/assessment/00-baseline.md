# Phase 0 — Baseline Assessment (re-run)

**Repository:** `D:/Codebase/TelegramDownloader`  
**Branch:** `enterprise-readiness`  
**HEAD:** `1c3753e0491fb9ebba8d9da8d3aa413b8c3f4499` — *docs: Wave 1 assessment, hardening roadmap, and draft CI workflows* (2026-10-01)  
**Assessment date:** 2026-10-01  
**Agent / model:** Composer 2.5 re-run  
**Host:** Windows 10.0.26200, PowerShell, x64  

Everything below was measured by commands run in this repository unless labelled **NOT MEASURED** or **ASSUMPTION**.

---

## Executive summary

| Dimension | Result |
|---|---|
| **Build** | **12/12** projects + `RunnerApp.sln` — **0 errors** |
| **Unit tests** (`TelegramAutoDownload.Tests`) | **391** total — **390 pass / 1 fail (flaky)** — exit **1** |
| **Headless tests** (`TelegramAutoDownload.Headless.Tests`) | **22** total — **22 pass** — exit **0** (~3.9 s wall) |
| **UI tests** (`TelegramAutoDownload.UITests`) | **7** total — **3 pass / 4 fail** — exit **1** (~20.8 s wall) |
| **Production line coverage (unit-test run)** | **10.63%** (636 / 5,987 lines) — `dotnet-coverage` on unit suite |
| **Headless assembly line coverage (Headless.Tests run)** | **31.59%** — `dotnet-coverage` on Headless suite |
| **Compiler warnings (unique CS)** | **66** (61× CS8632, 4× CS8618, 1× CS8601) |
| **NuGet CVEs (dotnet list --vulnerable)** | **2** — 1 **High** (production Headless), 1 **Critical** (UITests only) |

### Top 5 production risks

1. **Main unit suite is not trustworthy.** Five consecutive runs of `TelegramAutoDownload.Tests` all exited **1** with **1–2** failures; failing tests varied (`LogAlertSinkLevelFilterTests`, `AppLogAlertServiceTests`, `Emit_Error_SetsLevelToERR`). Root cause: shared singleton `AppLogAlertService.Instance` (`TelegramAutoDownload/Services/AppLogAlertService.cs:11`) with **no** xUnit collection / parallelization control in the unit project (contrast `TelegramAutoDownload.UITests` `[Collection("UI")]`).

2. **Shipped container defaults and weak web auth.** `docker-compose.yml:16-17` sets `WEB_USERNAME: admin` / `WEB_PASSWORD: changeme`. Passwords are hashed with unsalted `SHA256.HashData` (`WebAuthService.cs:248-249`). Sessions are stateless HMAC cookies (`WebAuthService.cs:251-254`); no CSRF, rate limit, or revocation (`WebAuthMiddleware.cs:5-24`).

3. **HIGH-severity SQLite native CVE in Headless.** Transitive `SQLitePCLRaw.lib.e_sqlite3` **2.1.6** ([GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)) via pinned `Microsoft.Data.Sqlite` **8.0.10** (`TelegramAutoDownload.Headless.csproj:14`).

4. **Telegram core has null-checking disabled and low coverage.** `TelegramClient.csproj` has **no** `<Nullable>enable</Nullable>` (lines 3-5) while containing **61** decorative `CS8632` annotations. `TelegramApp.cs` is **1,799** lines; assembly line coverage **14.06%** under the unit-test run.

5. **Arbitrary download path from authenticated API.** `POST /api/settings/download-folder` writes client-supplied `req.Folder` to config with no path validation (`Program.cs:179-182`), then downloads use that path — traversal / symlink risk depends on host OS and process privileges (**exploit path not demonstrated** in this run).

---

## 0. Method and constraints

- **No source edits** (no `.cs`, `.csproj`, etc.). Coverage via global tool **`dotnet-coverage` 18.11.2** (no `coverlet.collector` added to `TelegramAutoDownload.Tests`).
- **Build/test owner:** this re-run only; commands recorded in Appendix A.
- **Artifacts on disk (untracked):** `_baseline_cov/` (Cobertura XML from this run). Safe to delete.
- **Solution:** `RunnerApp.sln` is **tracked at HEAD** and lists **12** projects including `TelegramAutoDownload.Headless.Tests`. README still references the same name (`README.md:111,120,298`) — **verified:** `dotnet build RunnerApp.sln -c Release` succeeded (exit 0, ~15 s).

### 0.1 Tooling

```
.NET SDK: 10.0.401 (MSBuild 18.9.11)
global.json: NOT present
```

All projects target **net8.0** or **net8.0-windows**. Installed **Microsoft.NETCore.App 8.0.31** — cross-compile from SDK 10 succeeded for every project.

### 0.2 NuGet restore

Repo **`NuGet.config`** clears machine feeds and pins **nuget.org only** (`NuGet.config:3-5`). Restores were run with `--source https://api.nuget.org/v3/index.json` for consistency with prior baselines; with the committed config, plain `dotnet restore` should also ignore the unreachable corporate Artifactory feed on this host.

Machine still registers Artifactory (`dotnet nuget list source`); the committed config overrides it for this repo.

### 0.3 CI at HEAD

```
.github/workflows/codeql.yml
.github/workflows/container-image.yml
.github/workflows/pr-validation.yml
.github/workflows/release-desktop.yml
.github/workflows/scheduled-maintenance.yml
```

Workflow files exist but are marked **UNVALIDATED** in YAML comments (e.g. `pr-validation.yml:5-6`). **NOT MEASURED:** whether any workflow has produced a green run on GitHub.

**LICENSE:** still **absent** at repo root (`Test-Path LICENSE` → False) while `README.md:341-343` claims MIT.

---

## 1. Build

### 1.1 Projects (12)

| # | Project | TFM | Release rebuild exit | Notes |
|---|---|---|---|---|
| 1 | `BasePlugin/BasePlugins.csproj` | net8.0 | 0 | |
| 2 | `Logger/Logger.csproj` | net8.0 | 0 | |
| 3 | `TelegramClient/TelegramClient.csproj` | net8.0 | 0 | No nullable project setting |
| 4 | `DownloadService/DownloadPlugin.csproj` | net8.0 | 0 | |
| 5 | `YoutubePlugin/YoutubePlugin.csproj` | net8.0 | 0 | |
| 6 | `SocialMediaPlugin/SocialMediaPlugin.csproj` | net8.0 | 0 | |
| 7 | `TorrentPlugin/TorrentPlugin.csproj` | net8.0 | 0 | |
| 8 | `TelegramAutoDownload/TelegramAutoDownload.csproj` | net8.0-windows | 0 | WPF + WinForms |
| 9 | `TelegramAutoDownload.Headless/...csproj` | net8.0 | 0 | Docker / ASP.NET Core |
| 10 | `TelegramAutoDownload.Tests/...csproj` | net8.0-windows | 0 | References WPF app |
| 11 | `TelegramAutoDownload.UITests/...csproj` | net8.0-windows | 0 | FlaUI |
| 12 | `TelegramAutoDownload.Headless.Tests/...csproj` | net8.0 | 0 | **New since earlier baseline** |

**Build status: 12/12 succeeded, 0 errors.**

**Windows-only (3):** WPF app, unit tests (via WPF reference), UITests. **Portable (9):** all plugin/core/Headless/Headless.Tests projects.

### 1.2 Compiler warnings (deduplicated)

Parsed from per-project `Rebuild` logs; unique key `file(line,col)|CSxxxx|owning-project`:

| Code | Count | Assessment |
|---|---|---|
| CS8632 | 61 | Nullable annotations ignored in `TelegramClient` (no `<Nullable>enable</Nullable>`) |
| CS8618 | 4 | Uninitialized non-nullable members on `BasePlugin/ResultExecute.cs:5` (five properties, one line) |
| CS8601 | 1 | `TelegramAutoDownload.Tests/NotificationTests.cs:21` |

**Total unique CS warnings: 66.** No `TreatWarningsAsErrors` in any `.csproj`. **NOT MEASURED:** full MSBuild warning count including duplicate emission across reference graphs.

---

## 2. Tests

### 2.1 `TelegramAutoDownload.Tests` (xUnit)

```powershell
dotnet test TelegramAutoDownload.Tests\TelegramAutoDownload.Tests.csproj -c Release --no-build
```

| Run | Passed | Failed | Wall clock | Notes |
|---|---|---|---|---|
| 1 (normal logger) | 390 | 1 | ~50 s | `LogAlertSinkLevelFilterTests.Emit_WarningOrAbove_AlertsService(Warning)` |
| 2 | 389 | 2 | 33.6 s | |
| 3 | 390 | 1 | 29.2 s | |
| 4 | 390 | 1 | 34.6 s | |
| 5 (under coverage) | 390 | 1 | 47.0 s | `LogAlertSinkLevelFilterTests.Emit_Error_SetsLevelToERR` |

| Metric | Value |
|---|---|
| Total | **391** |
| Skipped | **0** |
| Exit code | **1** (every run) |

**28** test source files under `TelegramAutoDownload.Tests/` (glob count).

Flaky pattern matches prior baseline: tests in `SelectedChannelFilterTests.cs` (`AppLogAlertServiceTests`, `LogAlertSinkLevelFilterTests`) mutate/read `AppLogAlertService.Instance` while xUnit parallelizes classes by default.

### 2.2 `TelegramAutoDownload.Headless.Tests` (xUnit + `WebApplicationFactory`)

```powershell
dotnet test TelegramAutoDownload.Headless.Tests\TelegramAutoDownload.Headless.Tests.csproj -c Release --no-build
```

| Metric | Value |
|---|---|
| Total | **22** |
| Passed | **22** |
| Failed | **0** |
| Wall clock | **3.9 s** |
| Exit code | **0** |

Coverage includes: `WebAuthServiceTests`, `WebAuthApiSmokeTests`, `MediaRepositoryTests`, `DatabaseMigrationTests`, `HeadlessLogReaderTests` (7 files). Project references Headless only (`TelegramAutoDownload.Headless.Tests.csproj:30`); uses `Microsoft.AspNetCore.Mvc.Testing` 8.0.11 and optional `coverlet.collector` 6.0.2 (not used in this baseline run).

### 2.3 `TelegramAutoDownload.UITests` (FlaUI)

| Metric | Value |
|---|---|
| Total | **7** |
| Passed | **3** |
| Failed | **4** |
| Wall clock | **20.8 s** |
| Exit code | **1** |

Failures (controls not found — app launched but main-window chrome missing):

- `SearchTextBox_IsPresent` (`MainWindowTests.cs:70`)
- `SettingsButton_Click_OpensSettingsWindow` (`:124`)
- `DownloadsGrid_IsPresent` (`:84`)
- `SyncButton_IsPresent_AndEnabled` (`:108`)

Skip hook: `SKIP_UI_TESTS=1` via `UIFact` (`UITestBase.cs:11-14`). **ASSUMPTION:** failures reflect login/first-run UI rather than missing display (session not verified).

### 2.4 Combined

| Suite | Total | Pass | Fail | Exit |
|---|---|---|---|---|
| Unit (WPF-linked) | 391 | 390 | 1 (flaky) | 1 |
| Headless | 22 | 22 | 0 | 0 |
| UI | 7 | 3 | 4 | 1 |
| **All** | **420** | **415** | **5** | **mixed** |

**There is still no single command that exits 0 for the full test surface** unless UI failures are skipped and unit flakiness is ignored.

---

## 3. Code coverage (measured)

### 3.1 Unit suite (`TelegramAutoDownload.Tests`) — `dotnet-coverage`

```powershell
dotnet-coverage collect --output _baseline_cov\coverage.cobertura.xml --output-format cobertura `
  "dotnet test TelegramAutoDownload.Tests\TelegramAutoDownload.Tests.csproj -c Release --no-build"
```

Overall instrumented (includes third-party assemblies loaded during test):

- **line-rate:** 0.1601 (2,810 / 17,547 lines)
- **branch-rate:** 0.0634 (549 / 8,663)

**Production assemblies** (excludes `TelegramAutoDownload.Tests`, `MonoTorrent*`, `RichardSzalay.MockHttp`):

| Assembly | Line % | Lines covered / valid |
|---|---|---|
| DownloadPlugin | 100.00% | 65 / 65 |
| BasePlugins | 28.27% | 80 / 283 |
| SocialMediaPlugin | 27.73% | 61 / 220 |
| TorrentPlugin | 20.00% | 5 / 25 |
| TelegramClient | 14.06% | 288 / 2,048 |
| TelegramAutoDownload (WPF) | 4.18% | 135 / 3,228 |
| YoutubePlugin | 1.69% | 2 / 118 |
| **TelegramAutoDownload.Headless** | **0%** | **not loaded** |
| **Logger** | **0%** | **not loaded** |

> ### **Headline (unit-test run): production line coverage 10.63% (636 / 5,987). Branch coverage 9.38% (335 / 3,571).**

`TelegramAutoDownload.Tests` does not reference Headless or Logger (`TelegramAutoDownload.Tests.csproj:21-28`).

### 3.2 Headless suite — `dotnet-coverage`

```powershell
dotnet-coverage collect --output _baseline_cov\headless.cobertura.xml --output-format cobertura `
  "dotnet test TelegramAutoDownload.Headless.Tests\TelegramAutoDownload.Headless.Tests.csproj -c Release --no-build"
```

| Assembly | Line % | Branch % |
|---|---|---|
| **TelegramAutoDownload.Headless** | **31.59%** | **23.85%** |
| TelegramAutoDownload.Headless.Tests | 100% | 87.5% |

Auth, DB migrations, `MediaRepository`, and log reader paths are partially exercised; **`Program.cs` (643 lines)**, **`DownloadOrchestrator`**, **`BootstrapScanner`**, and most scanning/download pipeline remain largely untested (**NOT MEASURED** per-file breakdown in this re-run).

### 3.3 JavaScript

`TelegramAutoDownload.Headless/wwwroot/app.js` — **1,158** lines. **No** JS test infrastructure (**NOT MEASURED** coverage).

---

## 4. Dependency inventory and CVEs

### 4.1 Top-level NuGet packages (21 distinct names)

Aggregated from all `.csproj` files via `dotnet list package`:

| Package | Version | Projects |
|---|---|---|
| WTelegramClient | 4.4.2 | TelegramClient |
| MonoTorrent | 3.0.2 | BasePlugin, TorrentPlugin |
| Ytdlp.NET | 3.0.4 | YoutubePlugin, SocialMediaPlugin |
| Dapper | 2.1.35 | Headless |
| Microsoft.Data.Sqlite | 8.0.10 | Headless |
| Newtonsoft.Json | 13.0.3 | WPF, Headless |
| Serilog | 4.2.0 | TelegramClient, WPF, Headless |
| Serilog.AspNetCore | 8.0.3 | Headless |
| Serilog.Sinks.* | various | Logger, WPF, Headless |
| MahApps.Metro | 2.4.11 | WPF |
| dotenv.net | 3.2.0 | WPF |
| QRCoder.Xaml | 1.7.0 | WPF |
| FluentAssertions | 6.12.0 | test projects |
| xunit / runner / SDK | 2.7.0 / 2.5.7 / 17.9.0 | tests |
| FlaUI.UIA3 | 4.0.0 | UITests |
| RichardSzalay.MockHttp | 7.0.0 | unit tests |
| Microsoft.AspNetCore.Mvc.Testing | 8.0.11 | Headless.Tests |
| coverlet.collector | 6.0.2 | Headless.Tests |

**Outdated (sample):** Headless reports updates for Dapper, Microsoft.Data.Sqlite (→ 10.0.12), Serilog.AspNetCore (→ 10.0.0), etc. **18+ packages behind latest** — full matrix **NOT MEASURED** in this re-run (see prior baseline methodology).

**Flagged for commercial / ops review:**

- **WTelegramClient 4.4.2** — MIT, active; 7 patches behind 4.4.9.
- **MonoTorrent 3.0.2** — latest on NuGet but **no release since 2024-08**; large transitive footprint; **0%** test coverage of MonoTorrent assemblies in unit run.
- **Ytdlp.NET 3.0.4** — **major** behind 4.x; wraps auto-updated `yt-dlp` binary (supply chain **NOT MEASURED** here).
- **FluentAssertions** — pinned 6.12.0 (Apache-2.0); v8+ is **commercial** for non-OSS — upgrade trap.

### 4.2 Vulnerabilities (`dotnet list package --vulnerable --include-transitive`)

| Package | Severity | Introduced via | Production? |
|---|---|---|---|
| SQLitePCLRaw.lib.e_sqlite3 2.1.6 | **High** [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) | Microsoft.Data.Sqlite 8.0.10 | **Yes — Headless / Docker** |
| System.Drawing.Common 5.0.2 | **Critical** [GHSA-rxg9-xrhp-64gj](https://github.com/advisories/GHSA-rxg9-xrhp-64gj) | FlaUI.UIA3 4.0.0 | **No — UITests only** |

**yt-dlp / ffmpeg** OS packages in `Dockerfile`: **NOT MEASURED** (requires image scanner).

---

## 5. HTTP API surface (Headless)

All routes defined in `TelegramAutoDownload.Headless/Program.cs`. Auth: `WebAuthMiddleware` (`WebAuthMiddleware.cs:9-13`) when `WebAuthService.Enabled`; bypass for `WebAuthService.IsPublicPath` (`WebAuthService.cs:173-182`):

| Public (no session) | Path |
|---|---|
| Static | `/`, `/index.html`, `/styles.css`, `/app.js` |
| Auth | `GET /api/web-auth/status`, `POST /api/web-auth/login` |

All other listed endpoints require a valid session cookie when web auth is enabled (including Telegram login and settings). **No CSRF tokens** on cookie-authenticated `POST`/`PATCH`/`DELETE`.

| Method | Path | Side effects / blast radius (summary) |
|---|---|---|
| POST | `/api/web-auth/logout`, `/api/web-auth/change-password` | Session / credential file |
| GET | `/api/login/status` | Read login state |
| POST | `/api/login/phone`, `/code`, `/password`, `/logout` | **Telegram session** (`session.dat`) |
| GET | `/api/settings` | Read config |
| POST | `/api/settings/credentials` | **API ID/hash** in config |
| POST | `/api/settings/download-folder` | **Arbitrary folder** → config (`Program.cs:179-182`) |
| POST | `/api/settings/folder-layout`, `/threads`, `/limits` | Config / rate limits |
| GET/POST/PATCH | `/api/chats*` | Monitored chats, sync, bootstrap |
| GET | `/api/flood-wait`, `/api/status` | Operational read |
| GET | `/api/logs/*` | **Read log files** (incl. SSE stream) |
| GET/POST | `/api/queue/*`, `/api/downloads` | SQLite queue, download control |
| POST | `/api/chats/{id}/test-download` | Trigger test download |

Input validation is **per-handler**; no global model validation layer observed in `Program.cs`.

---

## 6. Persisted state

| Asset | Location | Format | Growth / concurrency |
|---|---|---|---|
| SQLite DB | `HeadlessPaths.DatabaseFile` → `{DATA_DIR}/tad.db` (`HeadlessPaths.cs:21-23`) | SQLite WAL, single-writer (`Database.cs:39-44`) | Grows with media queue/history |
| Schema migrations | Embedded `Data/Schema/*.sql` (`Database.cs:63-108`) | Version table `SchemaMigration` | Applied once per version |
| Telegram session | `{DATA_DIR}/session.dat` (`HeadlessPaths.cs:22`) | WTelegramClient session | **Crown jewel** — full account |
| App config | `{DATA_DIR}/config.json` (`HeadlessPaths.cs:21`) | JSON | Download folder, credentials, filters |
| Web credentials | `{DATA_DIR}/web-auth.json` (`WebAuthService.cs:18`) | JSON + base64 SHA-256 hash | Overwritable from UI |
| Signing secret | `{DATA_DIR}/.web-auth-secret` (`WebAuthService.cs:237`) | Random key file | Session forgery if leaked |
| Legacy dedup | `LegacyDedupIndexFile` (`HeadlessPaths.cs:32-34`) | JSON | Import path for WPF migration |
| Logs | `{DATA_DIR}/logs` | Serilog files | Unbounded without rotation policy review |
| WPF legacy | `%AppData%/TelegramAutoDownload/*` | config.txt, stats, etc. | Desktop path (**NOT MEASURED** in this run) |

**Backup / encryption at rest:** **NOT MEASURED** — no built-in backup; files are plaintext on volume mounts (`docker-compose.yml:25-27`).

---

## 7. Architecture (dependency graph)

```
BasePlugins, Logger          (leaves)
TelegramClient → BasePlugins
*Plugin → BasePlugins
TelegramAutoDownload → Logger, TelegramClient + build-only plugins
TelegramAutoDownload.Headless → Logger, TelegramClient, BasePlugins + build-only plugins
TelegramAutoDownload.Tests → plugins, TelegramClient, WPF app
TelegramAutoDownload.Headless.Tests → Headless
TelegramAutoDownload.UITests → (no ProjectReference; exe path hack)
```

- **Cycles:** **none** (DAG).
- **Layering issues (unchanged):** `TorrentDownloadService` in BasePlugin; unit tests forced to `net8.0-windows` via WPF reference; UITests bind by filesystem path.
- **God objects (line counts this run):** `TelegramApp.cs` **1,799**; `MainWindow.xaml.cs` **1,478**; `app.js` **1,158**; `Program.cs` **643**.

**Tracked files:** 232 (`git ls-files` count). **Source files** (*.cs/xaml/js/html/css, excl bin/obj): **166**.

---

## 8. Corrections vs earlier prompt / baseline

| Claim | Measured truth |
|---|---|
| No `.sln` | **`RunnerApp.sln` tracked** at HEAD with 12 projects |
| No CI | **`.github/workflows/` present** at HEAD (5 files); execution **NOT MEASURED** |
| Headless 0% tests | **`TelegramAutoDownload.Headless.Tests`** — 22 tests, **31.59%** line coverage on Headless assembly |
| No NuGet.config | **`NuGet.config` commits nuget.org-only** |
| No LICENSE | **Still true** |

---

## Appendix A — Commands run

```powershell
dotnet --info
dotnet nuget list source
git branch --show-current; git rev-parse HEAD

# Per-project restore/build (×12) + RunnerApp.sln build
dotnet restore <csproj> --source https://api.nuget.org/v3/index.json
dotnet build <csproj> -c Release --no-restore -t:Rebuild

dotnet test TelegramAutoDownload.Tests\... -c Release --no-build
dotnet test TelegramAutoDownload.Headless.Tests\... -c Release --no-build
dotnet test TelegramAutoDownload.UITests\... -c Release --no-build

dotnet-coverage collect ... TelegramAutoDownload.Tests
dotnet-coverage collect ... TelegramAutoDownload.Headless.Tests

dotnet list <csproj> package --vulnerable --include-transitive --source https://api.nuget.org/v3/index.json
dotnet sln RunnerApp.sln list
```

## Appendix B — NOT measured this run

- GitHub Actions workflow green/red status  
- Docker image build / container scan  
- Combined merged coverage across both test suites in one Cobertura file  
- Runtime soak / load  
- Full outdated-package matrix and NuGet license API pass  
- Exploit demonstration for path traversal, ReDoS, or SSRF  

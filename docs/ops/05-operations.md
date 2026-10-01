# 05 — Operations: SLOs, Observability, Backup/DR, Upgrade & Rollback, Runbook

**Scope:** reliability engineering for TelegramAutoDownload across both deployment
surfaces — the container (`TelegramAutoDownload.Headless/`, packaged by `Dockerfile`)
and the desktop WPF app (`TelegramAutoDownload/`).

**Authoring constraints in force for this document**

- Existing source was treated as **read-only**. Nothing under `*.cs`, `*.csproj`,
  `*.js`, `*.html`, `*.css`, `Dockerfile`, or `docker-compose.yml` was modified.
- `dotnet build` / `dotnet test` were **not run** (another agent owns the build
  machine; concurrent builds cause file-lock conflicts). Every design item below
  is therefore a **design**, not a verified implementation.
- All new pipeline files under `.github/workflows/` are marked **UNVALIDATED** —
  see [`05-cicd.md`](./05-cicd.md).

**Legend for implementation status**

| Mark | Meaning |
|---|---|
| **EXISTS** | verified present in the code today, with a `path:line` citation |
| **GAP** | verified absent today |
| **DESIGN** | proposed here; requires a code change by the team that owns the source |

---

## 0. Verified current state

Every claim in the brief was checked against the tree.

| Claim | Verdict | Evidence |
|---|---|---|
| No `.github/`, no CI/CD | **Confirmed** (before this work) | `.github/` did not exist; the five workflows in this change are the first |
| No tracked `.sln` despite README referencing `RunnerApp.sln` | **Confirmed** | `Get-ChildItem -Recurse -Filter *.sln` returns nothing; `README.md:111`, `README.md:120`, `README.md:298` all reference `RunnerApp.sln` |
| Serilog → console + daily rolling file, 7-day retention | **Confirmed** | `TelegramAutoDownload.Headless/Program.cs:15-23` (`retainedFileCountLimit: 7` at `:20`) |
| Seq sink available in `Logger/` | **Confirmed, unwired** | `Logger/Services/SeqService.cs:11-13`; nothing in `Program.cs` references it |
| No metrics / tracing / correlation IDs | **Confirmed** | no `Meter`, `ActivitySource`, `OpenTelemetry`, or `Prometheus` reference anywhere in the tree |
| No health or readiness endpoints | **Confirmed** | no `MapHealthChecks`, `/health`, or `/ready` route in `Program.cs:44-534` |
| Log reading exposed via API + SSE | **Confirmed** | `Logging/HeadlessLogReader.cs:10-52`; routes at `Program.cs:291`, `:293`, `:306`, `:319` (SSE at `:337-357`) |
| Single SQLite file + JSON config + `session.dat` on a mounted volume | **Confirmed** | `HeadlessPaths.cs:21-25`; volumes at `Dockerfile:50`, `docker-compose.yml:26-28` |
| No backup, no restore procedure, no encryption at rest | **Confirmed** | no backup code, no scripts, no docs; SQLite opened with a plain `Data Source=` connection string (`Data/Database.cs:28`) |
| Release process manual, both artifacts mandatory | **Confirmed** | `.cursor/rules/release-process.mdc:10-15` and `:33-36` |
| Local SDK 10.0.401 vs `net8.0` targets | **Confirmed** | `dotnet --version` → `10.0.401`; `TelegramAutoDownload.Headless.csproj:5` (`net8.0`), `TelegramAutoDownload.csproj:5` (`net8.0-windows`) |

### 0.1 Three findings the brief did not mention — all operationally significant

**(a) `session.dat` is not where `HeadlessPaths` says it is.**
`HeadlessPaths.SessionFile` resolves to `/data/session.dat`
(`HeadlessPaths.cs:22`), but the Telegram client ignores that property and builds
its own path from `SpecialFolder.ApplicationData`
(`TelegramClient/TelegramApp.cs:102-106`, and again at `:161-164`). Inside the
container `XDG_CONFIG_HOME=/data` (`Dockerfile:46`), so the real session file is
**`/data/TelegramAutoDownload/session.dat`** — the `Dockerfile:44-45` comment
confirms this is deliberate. **Consequence: any backup script written against
`HeadlessPaths.SessionFile` silently backs up nothing.** The backup design in
§3 targets the real path and globs both.

**(b) The desktop auto-updater points at a different GitHub repository than this one.**
`AppVersion.GitHubOwner = "il90il90"` (`TelegramAutoDownload/AppVersion.cs:6`), and
that is what builds the update URL (`Services/AutoUpdateService.cs:24-25`). The
`origin` remote is `kesavananbu/TelegramAutoDownload`. **Every installed user of a
build produced from this repo auto-updates from a third party's release feed.**
Whoever controls that upstream repository controls code execution on this
product's installed base. This is the single highest-severity item in this
document. See §4.3.

**(c) The container's `yt-dlp` is frozen at image-build time.**
It comes from Debian apt (`Dockerfile:34-37`) and never updates in a running
container. The desktop app, by contrast, self-updates `yt-dlp` on every launch
(`TelegramAutoDownload/Services/YtdlpService.cs:41-108`). Since `yt-dlp` breakage
after a site change is the most frequent functional failure mode for the
SocialMedia and YouTube plugins, **container users will experience breakages that
desktop users never see, with no in-product remedy.** Runbook R6 and the
`ytdlp-canary` job in `scheduled-maintenance.yml` address this.

### 0.2 Restart / resume behaviour of the long-running workers

Understanding this is a prerequisite for the SLOs and the upgrade design.

`DownloadOrchestrator` is an `IHostedService`/`BackgroundService` registered at
`Program.cs:40`. Its loop (`Scanning/DownloadOrchestrator.cs:39-91`):

1. Idles at 3 s intervals while `_host.Telegram` is null or disconnected
   (`:47-51`) or while `DownloadsPaused` is set (`:55-59`).
2. Computes spare capacity against `DownloadThreads`, read fresh from config each
   iteration so the setting hot-reloads (`:61-62`).
3. Atomically claims work: `MediaRepository.PickQueuedAsync` runs a single
   `UPDATE ... SET status='in_progress' ... RETURNING *`
   (`Data/MediaRepository.cs:199-217`) — the claim and the state transition are
   one statement, so two workers cannot claim the same row.
4. Dispatches each row on a fire-and-forget `Task.Run` (`:79-82`).

**Crash/restart resume is real and correct at the row level.** `Database.InitializeAsync`
reverts every stranded `in_progress` row back to `queued` on startup
(`Data/Database.cs:52-61`), and that runs before the host serves traffic
(`Program.cs:105-108`). Partial bytes on disk are then handled by
`ExistingDownloadValidator.CheckAsync` (`DownloadOrchestrator.cs:113-127`), which
can mark an item `done` or `skipped` without re-downloading.

**Two resume gaps that shape the SLOs:**

- **Restart during a bootstrap sweep loses in-flight queue promotion.**
  `BootstrapScanner` writes discovered rows as `pending` and only promotes them
  to `queued` on *successful completion* (`Scanning/BootstrapScanner.cs:129-134`).
  Jobs are tracked in an in-memory `ConcurrentDictionary` in `BootstrapManager`
  (`Scanning/BootstrapManager.cs:20`) started by fire-and-forget `Task.Run`
  (`:48`). A restart therefore leaves rows stuck in `pending` forever and no job
  resumes them — the operator must re-trigger bootstrap. The scan *watermark* is
  persisted per page (`BootstrapScanner.cs:112-113`), so the re-run is cheap, but
  it is not automatic. **DESIGN:** a startup reconciliation pass that promotes
  orphaned `pending` rows for monitored chats whose `bootstrap_complete = 1`, and
  a restart-safe job registry.
- **`FloodWaitTracker` state is in-memory only** (`Scanning/FloodWaitTracker.cs:12-15`).
  A restart forgets an active Telegram flood-wait window and immediately resumes
  API calls, which typically earns a longer penalty. **DESIGN:** persist
  `paused_until` to the DB or a file under `/data`.

---

## 1. Service Level Objectives

### 1.1 Design premise: this system cannot be made highly available as built

Three independent single-writer constraints make a second active replica
incorrect, not merely inefficient:

1. **One SQLite file**, opened `Cache=Shared` with WAL (`Data/Database.cs:28`,
   `:38-45`). Safe for one process, not for two across a bind mount.
2. **One Telegram session.** Two processes sharing `session.dat` fight over the
   file lock — the codebase already has a helper dedicated to that failure
   (`TelegramClient/SessionLockHelper.cs:8-23`) and the desktop app takes a global
   single-instance mutex specifically to prevent it
   (`TelegramAutoDownload/App.xaml.cs:29-33`). Worse, concurrent use of the same
   auth key causes Telegram to invalidate it server-side, which costs a manual
   phone-code re-login.
3. **In-memory job and rate-limit state**: `BootstrapManager._running`
   (`BootstrapManager.cs:20`), `FloodWaitTracker` (`FloodWaitTracker.cs:12-15`),
   and the live-download snapshot in `HeadlessHost._downloads`
   (`HeadlessHost.cs:33`) are per-process. Two replicas would each rate-limit
   independently and jointly exceed Telegram's budget.

Committing to 99.9% would therefore be dishonest. The numbers below are what a
paying customer should accept for a **single-instance, self-hosted, restart-safe**
service, and they are deliberately achievable.

### 1.2 The SLO set

| # | SLO | Target | Window | SLI definition | Measured by |
|---|---|---|---|---|---|
| **S1** | **Service availability** | **99.5%** of probes succeed (≈3 h 39 m error budget/month) | 30 d rolling | fraction of 30 s blackbox probes where `GET /healthz/ready` returns `200` within 2 s | Blackbox exporter → Prometheus. `ready` deliberately excludes Telegram session validity (§2.3) |
| **S2** | **Download success rate** | **≥99.0%** | 28 d rolling | `done / (done + failed)` over media rows whose `discovered_at` falls in the window. `skipped` is excluded — it is user intent (filters/dedup), not failure (`DownloadOrchestrator.cs:142-161`) | `tad_downloads_completed_total{outcome}`; auditable from the `Media` table (`Data/Schema/001_initial.sql:22`) |
| **S3** | **Download completeness** | **≥99.5%** of eligible media reach `done` within **24 h** of discovery | 28 d rolling | fraction of eligible rows with `completed_at - discovered_at ≤ 24 h`. Catches the failure S2 cannot see: items stuck in `queued` forever still count as "not failed" | `histogram_quantile` over `tad_media_time_to_complete_seconds` |
| **S4** | **Time to detect a new message** | **p95 ≤ 60 s**, **p99 ≤ 300 s** | 7 d rolling | message timestamp → `Media` row insert. Both values already exist: `date_utc` and `discovered_at` (`001_initial.sql:19`, `:27`). Live path only (`MediaTracker.OnMessageDiscovered`, `MediaTracker.cs:60-71`); bootstrap sweeps are excluded | `tad_message_detect_latency_seconds` histogram |
| **S5** | **Time to start downloading** | **p95 ≤ 5 min** | 7 d rolling | `queued_at` → `started_at` (`001_initial.sql:28-29`). Excluded when `tad_flood_wait_active == 1` or `queue_depth{status="queued"} > 1000` — both are capacity/upstream states, not service faults | `tad_queue_wait_seconds` histogram |
| **S6** | **Data durability — application state** | **zero loss** of `done` rows and config beyond **RPO 1 h** | continuous | (a) ≥1 backup younger than RPO exists and passes `PRAGMA integrity_check`; (b) the monthly restore drill (§3.5) succeeds | `tad_backup_age_seconds`, `tad_backup_verify_status`; drill outcome recorded in the DR log |
| **S7** | **Data durability — Telegram session** | **RPO 24 h**, and **RTO ≤ 15 min without human interaction** | continuous | a restorable encrypted `session.dat` + its matching `config.json` exist as one consistent pair (§3.3). "Without human interaction" matters because the fallback costs a phone code | `tad_session_backup_age_seconds`; quarterly drill |
| **S8** | **Recovery time** | **RTO ≤ 2 h** for total host loss with a valid session backup; **≤ 4 h** if re-authentication is required | per incident | wall-clock from declaration to S1 green | Incident record; validated by drill |
| **S9** | **Dedup correctness** | **≤0.1%** duplicate downloads | 28 d rolling | files written whose `document_id` already had a `done` row, or that existed in `LegacyDedup` (`001_initial.sql:55-58`) | `tad_duplicate_downloads_total / tad_downloads_completed_total{outcome="done"}` |
| **S10** | **Desktop update health** | **≥99%** of update attempts end on the new version; **0** unsigned releases | per release | successful-update telemetry, or ticket-based proxy until telemetry exists | Release audit job + support ticket tags |

### 1.3 Explicit SLI exclusions (error-budget carve-outs)

These must be written into the customer-facing SLA or the numbers are unachievable:

1. **Telegram-imposed FLOOD_WAIT.** When `tad_flood_wait_active == 1` the upstream
   API is refusing us by design (`FloodWaitTracker.cs:26-33`); S4 and S5 are
   suspended. S1 is **not** suspended — the service must still answer probes.
2. **Operator-initiated pause.** `DownloadsPaused` (`Program.cs:494-501`,
   `DownloadOrchestrator.cs:55-59`) suspends S3 and S5.
3. **Not logged in.** If there is no valid Telegram session, S2–S5 are suspended;
   S1 continues (the login UI must stay reachable — that is the whole point of
   §2.3).
4. **Source-side deletion.** A message deleted or a file expired on Telegram
   produces a `failed` row that is not our fault. Requires a distinct
   `last_error` classification — **DESIGN**, since today every non-success collapses
   into `failed` with a free-text message (`DownloadOrchestrator.cs:136-160`).
5. **Disk full.** Counts against S2/S3 as a **customer-responsibility** breach
   only if the `tad_disk_free_ratio` alert fired ≥24 h before the failures.
6. **Site-side `yt-dlp` breakage.** Excluded from S2 for **72 h** after an upstream
   `yt-dlp` release that fixes the site, then it counts. This is the honest
   position: we cannot fix a site change faster than yt-dlp can, but we *can* ship
   the fix promptly.

### 1.4 Error-budget policy

| Budget consumed (30 d) | Response |
|---|---|
| <50% | Normal feature work |
| 50–75% | Reliability items get priority in the next sprint; no risky config changes (rate limits, thread counts) |
| 75–100% | Feature freeze on the affected surface. Only reliability, backup, and observability work ships |
| >100% | Incident review with written action items; releases require a named approver |

---

## 2. Observability design

Today's observability surface is: one text log, a 7-day retention window, and a
log-reading API. Everything in this section is **DESIGN**.

### 2.1 Structured logging with correlation IDs

#### The correlation key already exists — use it

The pipeline is `scan → queue → download`, and it is **persisted**, not in-memory.
`Media` is keyed `PRIMARY KEY (chat_id, message_id)` (`001_initial.sql:32`). That
composite key is the natural correlation ID because it **survives process
restarts**, which no `Activity.TraceId` does. A download that is discovered on
Monday, flood-waited on Tuesday, and completed on Wednesday spans three trace
trees but one `media_key`.

| Field | Value | Scope | Purpose |
|---|---|---|---|
| `media_key` | `"{chat_id}:{message_id}"` | Whole item lifetime, across restarts | **Primary** correlation key |
| `chat_id` | `long` | Per chat | Per-tenant slicing |
| `scan_id` | GUID per `BootstrapScanner.RunAsync` / per live-update batch | One sweep | Ties a page of discoveries together |
| `trace_id` / `span_id` | W3C, from `Activity.Current` | One in-process operation | OTel correlation |
| `correlation_id` | inbound `X-Correlation-ID`, else generated | One HTTP request | Ties an operator action to its effects |
| `attempt` | existing `attempts` column (`001_initial.sql:23`) | Per try | Distinguishes retry storms from many distinct failures |

**DESIGN — implementation shape:**

- Add `Serilog.Enrichers.Span` + `.Enrich.FromLogContext()` to the logger
  configuration at `Program.cs:15-23`.
- Push scope at the three pipeline boundaries: `BootstrapScanner.RunAsync`
  (`BootstrapScanner.cs:41`), `DownloadOrchestrator.DispatchAsync`
  (`DownloadOrchestrator.cs:94`), and `MediaTracker.OnMessageDiscovered`
  (`MediaTracker.cs:60`) — e.g.
  `using var _ = LogContext.PushProperty("media_key", $"{row.chat_id}:{row.message_id}")`.
- Add a correlation-ID middleware next to `app.UseWebAuth()` (`Program.cs:49`).
- Schema migration `002_add_trace_columns.sql` adding `trace_id TEXT` and
  `scan_id TEXT` to `Media`. The migration runner picks up any new embedded
  `Data/Schema/*.sql` automatically (`Data/Database.cs:63-109`,
  `TelegramAutoDownload.Headless.csproj:24`), so this is additive and safe.

#### Constraint: do not replace the text sink with JSON

`HeadlessLogReader` parses log levels with the regex `\[(DBG|INF|WRN|ERR|FTL|VRB)\]`
(`Logging/HeadlessLogReader.cs:15-16`), tails files matching `headless*.log`
(`:23`), and validates filenames by prefix (`:43-45`). The SSE endpoint streams
those parsed lines (`Program.cs:337-357`). **Switching the file sink to compact
JSON would break the in-product log viewer, the SSE stream, and the level filter.**

**DESIGN — dual-sink layout:**

| Sink | Format | Retention | Consumer |
|---|---|---|---|
| Console | text | — | `docker logs`, existing smoke tests |
| `/data/logs/headless-*.log` | **unchanged** text template (`Program.cs:22`) | raise 7 d → **30 d** | `HeadlessLogReader`, SSE, support bundle |
| `/data/logs/structured-*.jsonl` | `CompactJsonFormatter` | 14 d | Promtail/Vector → Loki/Elastic |
| Seq (opt-in) | Seq sink | external | central aggregation — wire up the existing `Logger/Services/SeqService.cs:11-13`, gated on a `SEQ_URL` env var |

Retention at 7 days (`Program.cs:20`) is below what enterprise incident review
needs; a Friday-evening failure reported the following Monday-plus-a-week is
already unanalysable. 30 days local + off-host shipping.

#### Log hygiene rules

- Never log `ApiHash`, phone numbers, verification codes, the web password hash,
  or the session signing key. Current risk: `LoginCoordinator` stores raw
  exception text in `LastError` (`LoginCoordinator.cs:90-91`) and serves it over
  the API (`Program.cs:126`) — Telegram exceptions can echo phone numbers.
  **DESIGN:** a redacting `ILogEventEnricher` plus a scrub on `LastError`.
- File paths are business data here, not secrets, but `downloaded_path`
  (`001_initial.sql:25`) should be hashed in exported diagnostics (§2.6).

### 2.2 Metrics — OpenTelemetry → Prometheus

**DESIGN.** Add `OpenTelemetry.Extensions.Hosting`,
`OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Runtime`,
and `OpenTelemetry.Exporter.Prometheus.AspNetCore`; expose `/metrics` on a
**separate port** (`METRICS_URL`, default `http://0.0.0.0:9090`) so scraping does
not have to traverse `WebAuthMiddleware` (`Program.cs:49`,
`WebAuthMiddleware.cs`). Meter name: `TelegramAutoDownload.Headless`.

Cardinality rule: `chat_id` is bounded by the user's dialog list (tens to low
hundreds) and is acceptable as a label. `message_id`, `file_name`, and
`last_error` are **never** labels — they belong in logs and exemplars.

#### Counters (monotonic)

| Metric | Labels | Source to instrument |
|---|---|---|
| `tad_messages_scanned_total` | `chat_id`, `source` (`live`\|`bootstrap`) | `BootstrapScanner.cs:99` loop; `MediaTracker.cs:60` |
| `tad_media_discovered_total` | `chat_id`, `kind` | after `InsertPendingAsync` returns true (`BootstrapScanner.cs:108`) |
| `tad_downloads_started_total` | `chat_id`, `kind`, `plugin` | `DownloadOrchestrator.cs:132`; `MediaTracker.OnStartedAsync` (`:74`) |
| `tad_downloads_completed_total` | `chat_id`, `kind`, `plugin`, `outcome` (`done`\|`failed`\|`skipped`) | the three-way branch at `DownloadOrchestrator.cs:146-161` — it already maps 1:1 onto `outcome` |
| `tad_download_bytes_total` | `chat_id`, `kind`, `plugin` | `HeadlessHost.OnProgress` (`HeadlessHost.cs:95-97`) |
| `tad_download_failures_total` | `chat_id`, `reason` (**bounded enum**: `flood_wait`, `network`, `auth`, `not_found`, `disk_full`, `plugin_error`, `ytdlp_error`, `unknown`) | `DownloadOrchestrator.cs:176-185`. Requires the error classifier from §1.3(4) |
| `tad_preflight_skips_total` | `chat_id`, `result` (`done`\|`skipped`) | `DownloadOrchestrator.cs:113-127` |
| `tad_duplicate_downloads_total` | `chat_id` | dedup path — feeds **S9** |
| `tad_flood_wait_events_total` | `source` | `FloodWaitTracker.Report` (`:17-34`) — `source` is already a low-cardinality string like `download:chat…`; **normalise to `download`\|`bootstrap`\|`scan`** before using it as a label |
| `tad_flood_wait_seconds_total` | `source` | sum of penalty seconds |
| `tad_telegram_api_calls_total` | `method`, `outcome` | wrap `Messages_GetHistory` (`BootstrapScanner.cs:73`) and peers |
| `tad_ytdlp_invocations_total` | `outcome` (`ok`\|`extractor_error`\|`http_error`\|`timeout`) | plugin boundary — **the yt-dlp breakage signal** |
| `tad_session_reauth_required_total` | — | increment when `LoginStage` leaves `LoggedIn` (`LoginCoordinator.cs:76-82`) |
| `tad_bootstrap_runs_total` | `chat_id`, `outcome` (`complete`\|`cancelled`\|`error`) | `BootstrapManager.cs:53-63` |
| `tad_schema_migrations_applied_total` | `version` | `Database.cs:107` |
| `tad_backup_runs_total` | `target` (`db`\|`config`\|`session`), `outcome` | backup job (§3) |
| `tad_http_requests_total` | `route`, `status` | ASP.NET Core instrumentation (free) |

#### Gauges / observable gauges

| Metric | Labels | Source |
|---|---|---|
| `tad_queue_depth` | `status` | `MediaRepository.GetGlobalStatusCountsAsync` (`MediaRepository.cs:246-256`) — already exists, exposed at `Program.cs:360` |
| `tad_queue_depth_by_chat` | `chat_id`, `status` | `GetPerChatStatusCountsAsync` (`MediaRepository.cs:258`) |
| `tad_queue_oldest_queued_age_seconds` | — | `MIN(queued_at)` where `status='queued'` — **the single best stalled-queue signal** |
| `tad_downloads_in_flight` | — | `DownloadOrchestrator._inFlight` (`:30`) |
| `tad_download_concurrency_limit` | — | `DownloadThreads` (`DownloadOrchestrator.cs:61`) |
| `tad_flood_wait_active` | — | `0`/`1` from `FloodWaitTracker.Snapshot()` (`:36-51`) |
| `tad_flood_wait_remaining_seconds` | `source` | `FloodWaitSnapshot.RemainingSeconds` (`:47`) |
| `tad_telegram_session_valid` | — | `LoginCoordinator.IsLoggedIn` (`:33`) |
| `tad_telegram_connected` | — | `TelegramApp.IsConnected` (`TelegramApp.cs:74`) — distinct from "session valid": a valid session with a dead TCP connection is a different failure |
| `tad_monitored_chats` / `tad_total_chats` | — | `Program.cs:527-528` |
| `tad_active_bootstrap_jobs` | — | `BootstrapManager.ActiveCount` (`:87`) |
| `tad_bootstrap_progress` | `chat_id`, `field` (`discovered`\|`inserted`) | `BootstrapJobView` (`BootstrapManager.cs:160-169`) |
| `tad_last_scan_age_seconds` | `chat_id` | `ChatScanState.last_scanned_date` (`001_initial.sql:45`) via `GetAllScanStatesAsync` (`MediaRepository.cs:314`) |
| `tad_disk_free_bytes` / `tad_disk_total_bytes` | `mount` (`/data`\|`/downloads`) | `DriveInfo`; desktop equivalent already exists (`Services/DiskSpaceService.cs:27`) |
| `tad_disk_free_ratio` | `mount` | derived |
| `tad_db_size_bytes` / `tad_db_wal_size_bytes` | — | `FileInfo` on `tad.db` / `tad.db-wal`. WAL growth is the early warning for a wedged writer |
| `tad_config_writable` | — | probe `/data` writability |
| `tad_backup_age_seconds` | `target` | newest verified backup |
| `tad_downloads_paused` | — | `DownloadsPaused` (`Program.cs:490-491`) — prevents paging on an intentional pause |
| `tad_build_info` | `version`, `commit`, `schema_version` | constant `1`; `schema_version` from `MAX(version)` in `SchemaMigration` (`Database.cs:73`) |

#### Histograms

| Metric | Labels | Buckets (seconds unless noted) | Serves |
|---|---|---|---|
| `tad_message_detect_latency_seconds` | `chat_id` | .5, 1, 2, 5, 10, 30, 60, 120, 300, 900 | **S4** |
| `tad_queue_wait_seconds` | `chat_id` | 1, 5, 15, 60, 300, 900, 3600, 21600 | **S5** |
| `tad_download_duration_seconds` | `kind`, `plugin` | 1, 5, 15, 60, 300, 900, 3600, 10800 | throughput |
| `tad_media_time_to_complete_seconds` | `chat_id` | 60, 300, 3600, 21600, 86400, 259200 | **S3** |
| `tad_download_size_bytes` | `kind` | 1e5, 1e6, 1e7, 1e8, 1e9, 5e9 (bytes) | capacity planning |
| `tad_telegram_api_call_duration_seconds` | `method` | .05, .1, .25, .5, 1, 2.5, 5, 10, 30 | upstream health |
| `tad_scan_page_duration_seconds` | `chat_id` | .1, .5, 1, 2, 5, 15, 60 | rate-limit tuning (`BootstrapScanner.cs:64-75`) |
| `tad_ytdlp_duration_seconds` | `outcome` | 1, 5, 15, 60, 300, 900 | plugin health |
| `tad_backup_duration_seconds` | `target` | 1, 5, 30, 120, 600 | backup-window fit |

#### Tracing

One `ActivitySource` (`TelegramAutoDownload.Headless`) with spans
`scan.bootstrap.page`, `queue.claim`, `download.dispatch`, `download.transfer`,
`plugin.ytdlp`, `telegram.api`. Attributes carry `media_key`, `chat_id`, `kind`,
`plugin`, `attempt`. OTLP exporter behind `OTEL_EXPORTER_OTLP_ENDPOINT`; **sample
at 100% of errors and 1–5% of successes** — a 3-hour download span is cheap to keep
when it fails and pointless to keep when it does not.

### 2.3 Health and readiness endpoints

**DESIGN.** Three endpoints, mounted before `app.UseWebAuth()` (`Program.cs:49`)
and allow-listed so probes need no credentials.

#### `GET /healthz/live` — liveness

Checks **only**: the process is up and the HTTP pipeline responds. No dependency
checks.

**This is a deliberate, load-bearing decision.** Liveness failure means "kill and
restart". With `restart: unless-stopped` (`docker-compose.yml:10`), any dependency
in liveness turns a recoverable condition into a restart loop. In particular a
restart loop would repeatedly abort long-running downloads (hours per file) and
repeatedly re-run the crash-recovery sweep (`Database.cs:56-60`), making forward
progress impossible.

#### `GET /healthz/ready` — readiness

| Check | Implementation | Fails readiness? |
|---|---|---|
| SQLite openable | `Database.Open()` + `SELECT 1` (`Database.cs:35-46`) | **Yes** |
| Schema at head | `MAX(version)` in `SchemaMigration` == highest embedded script (`Database.cs:63-109`) | **Yes** |
| `/data` writable | temp-file write/delete | **Yes** |
| Download root writable | `cfg.PathSaveFile` or `HeadlessPaths.DownloadsDir` (`DownloadOrchestrator.cs:109-111`) | **Yes** |
| Disk free above floor | `tad_disk_free_ratio` > 2% | **Yes** |
| Orchestrator loop alive | heartbeat timestamp updated each iteration, < 60 s old (`DownloadOrchestrator.cs:43`) | **Yes** |
| Telegram session valid | `LoginCoordinator.IsLoggedIn` (`:33`) | **No — reported, not enforced.** See below |

#### `GET /healthz/telegram` — the session signal

Returns `200` when `IsLoggedIn && IsConnected`, `503` otherwise, with a body
carrying `stage`, `userId`, `lastError`, and flood-wait state.

**Why session validity is not part of `/healthz/ready`, despite being the most
meaningful readiness signal here.** Session validity is exactly the signal an
operator most needs — it predicts total functional outage and it is the one failure
that requires a human with the account's phone. But routing readiness to it is
self-defeating:

- Under Kubernetes or any reverse proxy that honours readiness, a failing `ready`
  removes the pod from the Service. **The web UI is the only way to enter the
  phone code** (`Program.cs:129-148`). Failing readiness on an expired session
  locks the operator out of the one interface that can fix it — a deadlock.
- `LoginCoordinator.ProbeAsync` runs in a fire-and-forget task with an 800 ms
  sleep (`LoginCoordinator.cs:39-49`, invoked at `Program.cs:112-117`). A
  cold-start readiness check against `IsLoggedIn` races that probe and reports a
  false negative on every restart.

So: session validity is a **first-class, separately-surfaced, alerting** signal
(`/healthz/telegram`, `tad_telegram_session_valid`, alert A4 at 10 minutes), not a
traffic-routing one. For platforms that genuinely want it in readiness, gate it
behind `TAD_READINESS_REQUIRES_SESSION=true` (default `false`) and document the
lockout risk.

#### `GET /healthz/startup` — startup probe

Succeeds once migrations have completed and the login probe has run once
(`Program.cs:105-117`). `failureThreshold: 30`, `periodSeconds: 5` — buys 150 s
for a large migration without tripping liveness.

Also **DESIGN**, in the Dockerfile (owned by another agent — recommended, not applied):

```dockerfile
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
  CMD ["/bin/sh","-c","curl -fsS http://127.0.0.1:8080/healthz/live || exit 1"]
```

`curl` is not currently installed in the runtime stage (`Dockerfile:34-37`); add
it or use a tiny `dotnet`-based probe.

### 2.4 Alert conditions

Severity: **P1** = page immediately, **P2** = page during business hours, **P3** = ticket.

| ID | Condition | Sev | PromQL sketch | For | Why this threshold |
|---|---|---|---|---|---|
| **A1** | Service down | P1 | `up{job="tad"} == 0 or probe_success == 0` | 5 m | 5 m absorbs a rolling restart |
| **A2** | FLOOD_WAIT active and long | P2 | `tad_flood_wait_active == 1` | **15 m** | Short flood-waits are normal and self-healing (`FloodWaitTracker.WaitIfPausedAsync`, `:54-65`). A 15-minute window means Telegram has applied a real penalty |
| **A2b** | FLOOD_WAIT churn | P2 | `increase(tad_flood_wait_events_total[1h]) > 5` | 0 m | Repeated short penalties ⇒ rate limits are misconfigured. Remediation is the `/api/settings/limits` knobs (`Program.cs:228-242`) |
| **A3** | Disk filling | P1 | `tad_disk_free_ratio < 0.10 or tad_disk_free_bytes < 20e9` | 10 m | Two conditions because a ratio is meaningless on a 100 TB NAS and bytes are meaningless on a 200 GB VM |
| **A3b** | Disk **will** fill | P2 | `predict_linear(tad_disk_free_bytes{mount="/downloads"}[6h], 24*3600) < 0` | 30 m | Predictive beats reactive: a full disk mid-download corrupts partial files and mass-fails the queue |
| **A4** | Session expired / logged out | **P1** | `tad_telegram_session_valid == 0` | **10 m** | Total functional outage, and the **only** failure requiring a human with the phone. 10 m tolerates restart + probe race (§2.3) |
| **A4b** | Session valid but disconnected | P2 | `tad_telegram_session_valid == 1 and tad_telegram_connected == 0` | 15 m | `MaxAutoReconnects = 5` (`TelegramApp.cs:107`); past that, reconnection has given up |
| **A5** | Download failure-rate spike | P2 | `sum(rate(tad_downloads_completed_total{outcome="failed"}[30m])) / sum(rate(tad_downloads_completed_total[30m])) > 0.20` | 30 m | Burn rate against **S2** (99%). 20× the budget over 30 m |
| **A5b** | Fast burn on S2 | P1 | same ratio `> 0.50` | 5 m | Multi-window/multi-burn-rate: catches total-failure conditions in minutes |
| **A6** | **Queue stalled** | P1 | `tad_queue_depth{status="queued"} > 0 and rate(tad_downloads_started_total[15m]) == 0 and tad_flood_wait_active == 0 and tad_downloads_paused == 0` | 15 m | The exclusions are what make this actionable — otherwise it fires on every flood-wait and every intentional pause |
| **A6b** | Oldest queued item is ageing | P2 | `tad_queue_oldest_queued_age_seconds > 86400` | 1 h | Direct **S3** guard. Catches the silent case: a row that is neither failing nor progressing |
| **A6c** | `in_progress` stuck | P2 | `tad_queue_depth{status="in_progress"} > 0 and rate(tad_downloads_completed_total[6h]) == 0` | 30 m | `PickQueuedAsync` claims without a lease (`MediaRepository.cs:199-217`); only a restart reclaims (`Database.cs:56-60`) |
| **A6d** | Orphaned `pending` rows | P2 | `tad_queue_depth{status="pending"} > 0 and tad_active_bootstrap_jobs == 0` | 2 h | The §0.2 restart gap, made visible |
| **A7** | `yt-dlp` failing | P2 | `sum(rate(tad_ytdlp_invocations_total{outcome!="ok"}[1h])) / sum(rate(tad_ytdlp_invocations_total[1h])) > 0.5` | 1 h | Near-certain site-change breakage → runbook R6 |
| **A8** | Backup stale | P2 | `tad_backup_age_seconds{target="db"} > 7200` or `{target="session"} > 172800` | 0 m | Direct **S6**/**S7** guard |
| **A8b** | Backup verification failed | P1 | `tad_backup_verify_status != 1` | 0 m | An unverified backup is not a backup |
| **A9** | SQLite WAL bloat | P2 | `tad_db_wal_size_bytes > 500e6` | 15 m | WAL that never checkpoints ⇒ a long-lived reader or a wedged writer |
| **A10** | Crash-loop | P1 | `changes(process_start_time_seconds[30m]) > 3` | 0 m | `restart: unless-stopped` (`docker-compose.yml:10`) hides crashes; without this alert a loop is invisible |
| **A11** | Scan gone quiet | P2 | `min(tad_last_scan_age_seconds) > 3600 and tad_monitored_chats > 0` | 0 m | **S4** guard for the silent case: updates stopped arriving but nothing errored |
| **A12** | Default web password in use | P2 | log-derived: `UsingDefaultPassword == true` | 1 h | `changeme` default (`WebAuthService.cs:16`, `docker-compose.yml:18`); already warned at startup (`WebAuthService.cs:69-71`) but nobody reads logs |
| **A13** | Schema drift after rollback | P1 | `tad_build_info` schema version > the version the running binary knows | 0 m | The §4.2 rollback hazard |

### 2.5 Dashboards

1. **Executive / SLO** — S1–S5 attainment, error-budget burn-down, current state
   (logged in, paused, flood-wait).
2. **Pipeline** — Sankey-style `discovered → pending → queued → in_progress →
   done/failed/skipped`, queue depth by status, oldest-queued age, in-flight vs limit.
3. **Per-chat** — table of `chat_id`, monitored, queue depth, success rate,
   `last_scan_age`, bootstrap progress. Answers "chat X isn't downloading" in one look.
4. **Upstream/Telegram** — API call rate and latency, flood-wait timeline, session
   state, reconnect count.
5. **Capacity** — disk free + `predict_linear` projection per mount, DB and WAL
   size, download throughput, bytes/day by kind.
6. **Plugins** — yt-dlp version, invocation outcome split, duration p95. First
   place to look for "social links stopped working".

### 2.6 Diagnostics bundle for support

**DESIGN.** `POST /api/support/bundle` — authenticated (behind `UseWebAuth`,
`Program.cs:49`), rate-limited to 1/5 min, streams a ZIP.

Most of the content already exists behind individual endpoints; the bundle exists
so a support interaction is *one* artifact instead of twelve copy-pastes.

| Entry | Source | Redaction |
|---|---|---|
| `manifest.json` | bundle version, UTC timestamp, app version, commit, schema version (`Database.cs:73`) | — |
| `status.json` | `/api/status` (`Program.cs:519`) | — |
| `login.json` | `/api/login/status` (`Program.cs:120`) | **`error` scrubbed** — may contain a phone number (`LoginCoordinator.cs:90`) |
| `settings.json` | `/api/settings` + `/api/settings/limits` (`Program.cs:157`, `:199`) | — |
| `config.redacted.json` | `/data/config.json` (`ConfigStore.cs:25-36`) | **`ApiHash` → `"***"`, `AppId` → last 2 digits, folder templates kept, any `phone` removed** |
| `queue-stats.json` | `/api/queue/stats` + by-chat (`Program.cs:360`, `:375`) | — |
| `queue-failures.json` | `/api/queue/items?status=failed&limit=200` (`Program.cs:397`) | `file_name` → basename + SHA-256; `last_error` kept |
| `flood-wait.json` | `/api/flood-wait` (`Program.cs:288`) | — |
| `bootstrap-jobs.json` | `/api/bootstrap/jobs` (`Program.cs:453`) | — |
| `schema.json` | `SchemaMigration` rows + `PRAGMA integrity_check` + `PRAGMA quick_check` | — |
| `db-aggregates.json` | status/kind/chat counts, oldest queued, attempt histogram | **aggregates only — no filenames, no paths** |
| `logs/headless-*.log` | last 3 rolling files, capped 50 MB total (`HeadlessLogReader.ListFiles`, `:23-31`) | regex scrub for 32-hex API hashes, `+<digits>` phone patterns, `Bearer` tokens, cookie values |
| `disk.json` | free/total per mount, download-root size | — |
| `tools.json` | `yt-dlp --version`, `ffmpeg -version` (`YtdlpService.cs:190-206` equivalent) | — |
| `env.json` | **allow-list only**: `DATA_DIR`, `DOWNLOADS_DIR`, `LISTEN_URL`, `TZ`, `XDG_CONFIG_HOME`, `ASPNETCORE_URLS`, `WEB_AUTH_ENABLED`, `WEB_SESSION_DAYS` | everything else dropped, including `APP_ID`/`API_HASH`/`WEB_PASSWORD` (`docker-compose.yml:15-21`) |
| `metrics.txt` | `/metrics` snapshot | — |

**Never included, under any flag:** `session.dat` (§3.3), `web-auth.json`
(`WebAuthService.cs:18`), the cookie signing key (`WebAuthService.cs:63`), raw
`ApiHash`, verification codes, 2FA passwords, downloaded file content, full
filesystem paths of downloads.

Implementation notes: build to a temp file and stream (bundles can exceed memory);
write the redaction rules as a **default-deny allow-list** so a future field added
to `config.json` is excluded until explicitly permitted; emit a
`redaction-report.txt` listing what was removed so users can consent to what they
are sending.

---

## 3. Backup, restore, and disaster recovery

Three assets, three completely different failure and recovery profiles. Treating
them uniformly is the mistake to avoid.

| Asset | Real path (container) | Size | Recreatable? | Secret? | RPO | RTO |
|---|---|---|---|---|---|---|
| SQLite DB | `/data/tad.db` (+ `-wal`, `-shm`) — `HeadlessPaths.cs:23` | MB–GB | Yes, expensively: a full re-bootstrap of every chat, rate-limited (`BootstrapScanner.cs:64-75`) — hours to days, and it burns flood-wait budget | Contains filenames & chat IDs → **confidential** | **1 h** | 30 m |
| JSON config | `/data/config.json` — `HeadlessPaths.cs:21` | KB | Partially: chat selections, filters, folder templates are hand-curated and effectively unrecoverable | **Yes — holds `ApiHash`** (`HeadlessHost.cs:48-54`) | **1 h** | 5 m |
| Telegram session | **`/data/TelegramAutoDownload/session.dat`** — see §0.1(a) | KB | **No.** Requires phone + SMS/app code + possibly 2FA (`Program.cs:129-148`) | **Crown jewel** | **24 h** | 15 m with backup; hours without |
| `web-auth.json` | `/data/web-auth.json` — `WebAuthService.cs:18` | KB | Yes (reset via env) | Yes | 24 h | 5 m |
| Downloads | `/downloads` — `docker-compose.yml:27` | TB | Yes, from Telegram | User data | user choice | — |

### 3.1 SQLite — never copy the file while the app runs

`cp tad.db` on a live WAL database produces a **corrupt or stale** backup: recent
commits live in `tad.db-wal`, and the three files can be captured at inconsistent
moments. WAL is on for every connection (`Database.cs:38-45`).

Use SQLite's own online backup. `VACUUM INTO` produces a single consistent,
defragmented file with no writer lock held for the duration:

```bash
sqlite3 /data/tad.db "PRAGMA busy_timeout=30000; VACUUM INTO '/backup/staging/tad-$(date -u +%Y%m%dT%H%M%SZ).db';"
```

Then verify before the backup counts as existing:

```bash
sqlite3 "$OUT" "PRAGMA integrity_check;"   # must print exactly: ok
sqlite3 "$OUT" "SELECT COUNT(*) FROM Media; SELECT MAX(version) FROM SchemaMigration;"
```

Recording the schema version alongside every backup is what makes the
point-in-time restore in §4.2 decidable.

### 3.2 config.json — fix the torn-write risk first

`ConfigStore.Save` is a bare `File.WriteAllText` under a lock
(`ConfigStore.cs:39-42`). The lock serialises *writers in this process*; it does
nothing about power loss mid-write. Because config writes happen on ordinary user
actions — toggling a chat (`Program.cs:261-283`), changing limits (`:228`) — a host
crash at the wrong instant yields a truncated `config.json`, and `Read()`
(`ConfigStore.cs:25-36`) will throw on next start.

**DESIGN (source change, not made here):** write to `config.json.tmp`, `fsync`,
then `File.Move(..., overwrite: true)`; keep `config.json.bak` as the previous
good copy. `YtdlpService.DownloadFileAsync` already uses exactly this
temp-then-move idiom (`Services/YtdlpService.cs:185-187`) — the pattern is
established in the codebase.

**Operationally, until that lands:** back up `config.json` on every change
(inotify) rather than on a timer, and keep the last 20 versions. It is kilobytes.

### 3.3 The Telegram session — the hard one

`session.dat` is simultaneously the most valuable and least reproducible asset.

**What it is.** A WTelegramClient session store holding the DC auth key, created
at `TelegramApp.cs:106` and deleted on logout (`TelegramApp.cs:149`). Possessing it
is equivalent to possessing the Telegram account for API purposes — no phone, no
password, no 2FA prompt.

**Five properties that dictate the backup design:**

1. **It is a bearer credential.** An unencrypted copy in a backup bucket is an
   account takeover waiting to happen. Backups **must** be encrypted at rest with
   a key stored separately from the backup medium.
2. **It is useless without its matching `config.json`.** The auth key is bound to
   the `(AppId, ApiHash)` pair that created it (`TelegramApp.cs:106`, fed from
   `HeadlessHost.cs:48-54`). Restoring a session next to a different `ApiHash`
   fails to authenticate. **Back the two up as one atomic unit** — same snapshot,
   same manifest, same encryption envelope. This is the most common way session
   restores fail.
3. **It is held open by a running process.** The codebase has a dedicated helper
   for lock contention on this file (`SessionLockHelper.cs:8-23`) and the desktop
   app takes a global mutex to avoid it (`App.xaml.cs:29-33`). A hot copy can be
   torn.
4. **Two concurrent users of the same auth key get it revoked.** Restoring a
   session onto a second host while the first is still running does not give you
   two workers — it gives you **zero**, plus a mandatory phone-code re-login.
   This is the single most dangerous DR mistake available here.
5. **Telegram can invalidate it server-side** at any time: "terminate other
   sessions", password change, or anti-abuse action. A backup does not protect
   against that; only a human with the phone does. This is precisely why S7's
   RTO has two values.

**Backup procedure (consistency-first):**

| Tier | Method | Consistency | Use |
|---|---|---|---|
| **A — preferred** | Stop container → copy `/data/TelegramAutoDownload/session.dat` + `config.json` → start | Guaranteed | Nightly, during an idle window |
| **B — hot** | Copy in place while running | Best-effort; **must** be validated by a restore test | Hourly, between A runs |
| **C — filesystem snapshot** | ZFS/Btrfs/LVM snapshot of `/data`, then copy out of the snapshot | Crash-consistent for all three assets **atomically** — the only method that captures DB + config + session at one instant | Best option where the host supports it |

Tier A costs a short outage; downloads resume from `queued` automatically
(`Database.cs:56-60`), so the cost is bounded. Prefer C where available.

**Single-writer guard (DESIGN).** Write `/data/instance.lock` containing hostname
+ PID + a heartbeat timestamp; refuse to start if a lock is fresher than 60 s from
a different host, unless `TAD_FORCE_TAKEOVER=true`. Without this, the split-brain
in (4) is one `docker compose up` away on a restored host.

### 3.4 Backup topology, encryption, retention

**3-2-1:** 3 copies, 2 media, 1 off-site.

```
/data (live)
  └─ hourly  → /backup/local/   (same host, different filesystem)  — fast restore
       └─ daily → off-site object store (S3/B2/rsync.net)          — host loss
            └─ monthly → offline/immutable copy (object lock)      — ransomware
```

Use **restic** (or **Borg**): content-addressed dedup, AES-256 at rest,
`restic check --read-data` for verification, and append-only repositories that a
compromised host cannot retroactively delete.

```bash
# Environment (never in the compose file — use a secrets manager / docker secret)
export RESTIC_REPOSITORY="s3:s3.amazonaws.com/acme-tad-backups"
export RESTIC_PASSWORD_FILE="/run/secrets/restic_password"

restic backup /backup/staging /data/config.json /data/TelegramAutoDownload/session.dat \
  --tag tad --tag "schema=$SCHEMA_VERSION" --host "$(hostname)"

restic forget --tag tad --keep-hourly 24 --keep-daily 14 --keep-weekly 8 \
  --keep-monthly 12 --keep-yearly 3 --prune
```

Retention (GFS): 24 hourly, 14 daily, 8 weekly, 12 monthly, 3 yearly.

**Encryption at rest, today: GAP.** `Data Source={path};Mode=ReadWriteCreate;Cache=Shared`
(`Database.cs:28`) has no key; `config.json` and `session.dat` are plaintext
(`ConfigStore.cs:41`, `TelegramApp.cs:106`). Mitigations, in order of effort:

1. **Now, zero code:** encrypt the **backups** (restic does this) and put `/data`
   on an encrypted volume (LUKS / BitLocker / NAS encrypted shared folder).
   Restrict permissions — the compose file already runs as `user: "1029:100"`
   (`docker-compose.yml:11`); `chmod 700 /data` and `600` on the three files.
2. **Low effort, code:** pass WTelegramClient's session encryption key from an env
   var or mounted secret so `session.dat` is encrypted at rest.
3. **Medium:** SQLCipher, or move `ApiHash` out of `config.json` into a secret
   reference so the config file stops being a secret at all.

### 3.5 TESTED restore procedure

> **Run this quarterly as a drill, and record the outcome.** An unexercised restore
> procedure is a hypothesis. S6 and S7 are only *met* if the most recent drill passed.

Drill against a **scratch host or a second directory** — never the live `/data`.

```bash
#!/usr/bin/env bash
# restore-drill.sh — verify a TelegramAutoDownload backup is genuinely restorable.
# Usage: ./restore-drill.sh <restic-snapshot-id|latest>
set -euo pipefail

SNAPSHOT="${1:-latest}"
DRILL="/tmp/tad-drill-$(date -u +%Y%m%dT%H%M%SZ)"
IMAGE="${TAD_IMAGE:?set TAD_IMAGE to the digest-pinned GHCR image under test}"
PORT=18099

echo "== 1. Restore into an isolated directory =="
mkdir -p "$DRILL/data" "$DRILL/downloads"
restic restore "$SNAPSHOT" --target "$DRILL/restored"

echo "== 2. Place assets at the paths the app actually reads =="
# NOTE: session.dat lives under /data/TelegramAutoDownload/, NOT /data/ —
# TelegramApp.cs:102-106 builds the path from SpecialFolder.ApplicationData and
# Dockerfile:46 sets XDG_CONFIG_HOME=/data. Restoring to /data/session.dat is a
# silent no-op: the app starts unauthenticated and asks for a phone number.
mkdir -p "$DRILL/data/TelegramAutoDownload"
cp "$(find "$DRILL/restored" -name 'tad-*.db' | sort | tail -1)" "$DRILL/data/tad.db"
cp "$(find "$DRILL/restored" -name 'config.json'  | head -1)" "$DRILL/data/config.json"
cp "$(find "$DRILL/restored" -name 'session.dat'  | head -1)" "$DRILL/data/TelegramAutoDownload/session.dat"
chmod 600 "$DRILL/data/config.json" "$DRILL/data/TelegramAutoDownload/session.dat"

echo "== 3. Verify the database BEFORE booting anything =="
test "$(sqlite3 "$DRILL/data/tad.db" 'PRAGMA integrity_check;')" = "ok" \
  || { echo "FAIL: integrity_check"; exit 1; }
sqlite3 "$DRILL/data/tad.db" <<'SQL'
.headers on
SELECT MAX(version) AS schema_version FROM SchemaMigration;
SELECT status, COUNT(*) FROM Media GROUP BY status;
SELECT COUNT(*) AS chats_with_scan_state FROM ChatScanState;
SELECT COUNT(*) AS legacy_dedup FROM LegacyDedup;
SQL

echo "== 4. Verify config integrity and that it still carries credentials =="
python3 -c "import json,sys; c=json.load(open('$DRILL/data/config.json')); \
  assert c.get('AppId'), 'AppId missing'; assert c.get('ApiHash'), 'ApiHash missing'; \
  print('chats:', len(c.get('Chats', [])), 'monitored:', sum(1 for x in c.get('Chats',[]) if x.get('Selected')))"

echo "== 5. Boot the app against the restored volume =="
# CRITICAL: the live instance must be STOPPED, or Telegram sees the same auth key
# from two clients and revokes it — costing a manual phone-code re-login.
docker run -d --name tad-drill -p ${PORT}:8080 \
  -e DATA_DIR=/data -e DOWNLOADS_DIR=/downloads -e XDG_CONFIG_HOME=/data \
  -e WEB_AUTH_ENABLED=false \
  -v "$DRILL/data":/data -v "$DRILL/downloads":/downloads \
  "$IMAGE"

echo "== 6. Wait for readiness =="
for i in $(seq 1 60); do
  curl -fsS "http://localhost:${PORT}/healthz/ready" >/dev/null 2>&1 && break || true
  # Fallback while /healthz is still DESIGN (§2.3):
  curl -fsS "http://localhost:${PORT}/api/web-auth/status" >/dev/null 2>&1 && break || true
  sleep 2
done

echo "== 7. THE test that matters: did the session restore? =="
# ProbeAsync is fire-and-forget with an 800 ms delay (LoginCoordinator.cs:39-49,
# called from Program.cs:112-117), so poll rather than checking once.
ok=0
for i in $(seq 1 30); do
  if curl -fsS "http://localhost:${PORT}/api/login/status" | grep -q '"loggedIn":true'; then ok=1; break; fi
  sleep 2
done
[ "$ok" -eq 1 ] || { echo "FAIL: session did NOT restore — re-auth would be required"; \
                     docker logs tad-drill | tail -50; exit 1; }

echo "== 8. Queue state survived =="
curl -fsS "http://localhost:${PORT}/api/queue/stats" | tee /dev/stderr | grep -q '"byStatus"'

echo "== 9. No fatals =="
docker logs tad-drill 2>&1 | grep -E '\[FTL\]|Unhandled' && { echo "FAIL: fatal in log"; exit 1; } || true

echo "== 10. Tear down =="
docker rm -f tad-drill
echo "DRILL PASSED — snapshot $SNAPSHOT is restorable. Record this in the DR log."
```

PowerShell equivalent for the desktop surface (state lives in
`%APPDATA%\TelegramAutoDownload`, per `TelegramApp.cs:102-104` and
`App.xaml.cs:123-125`):

```powershell
# restore-drill-desktop.ps1 — verify desktop state backup
$ErrorActionPreference = 'Stop'
$src   = "$env:APPDATA\TelegramAutoDownload"
$drill = Join-Path $env:TEMP "tad-drill-$(Get-Date -Format yyyyMMddHHmmss)"

# 1. App MUST be closed — it holds a global mutex (App.xaml.cs:29-33) and an
#    exclusive handle on session.dat (SessionLockHelper.cs:8-23).
Get-Process TelegramAutoDownload -ErrorAction SilentlyContinue |
  ForEach-Object { throw "Close TelegramAutoDownload before running the drill (PID $($_.Id))." }

New-Item -ItemType Directory -Path $drill -Force | Out-Null
Copy-Item "$src\*" $drill -Recurse -Force

foreach ($f in 'session.dat','config.json','downloaded_ids.json') {
  if (Test-Path (Join-Path $drill $f)) { Write-Host "OK   $f" } else { Write-Warning "MISSING $f" }
}
# downloaded_ids.json is the legacy dedup index imported on first headless launch
# (HeadlessPaths.cs:32-34, Data/LegacyDedupMigration.cs). Losing it causes
# re-downloads, not data loss.
Write-Host "Desktop state snapshot verified at $drill"
```

**DR log entry template** (keep in the ops repo, one row per drill):

| Date | Snapshot | Schema ver | `integrity_check` | Session restored? | Wall clock | Operator | Notes |
|---|---|---|---|---|---|---|---|

### 3.6 Disaster scenarios

| Scenario | Detection | Recovery | Target |
|---|---|---|---|
| Container/host lost | A1, A10 | Restore §3.5 steps 1–5 onto new host; confirm the old host is off (§3.3(4)) | **RTO 2 h** |
| `tad.db` corrupt | A8b, startup failure | Runbook R4 | **RTO 30 m** |
| `config.json` truncated | startup exception from `ConfigStore.Read` (`:29-30`) | Restore latest `config.json`; else `config.json.bak`; else reseed (`ConfigStore.cs:18-22`) and re-select chats by hand | **RTO 15 m** |
| `session.dat` lost, backup valid | A4 | Stop, restore session **+ matching config**, start, poll `/api/login/status` | **RTO 15 m** |
| `session.dat` lost, no backup | A4 | Human with the phone: `POST /api/login/phone` → `/code` → `/password` (`Program.cs:129-148`) | **RTO hours** (human-gated) |
| Session revoked by Telegram | A4 | Same as above; no backup helps | **RTO hours** |
| Split-brain (two instances, one session) | A4 flapping, `AUTH_KEY_*` errors | Stop **both**, pick one host, delete the other's copy, re-login once | **RTO 1 h** |
| Ransomware on the host | A8b, integrity failures | Restore from the immutable/object-locked monthly tier; **rotate `ApiHash` and re-login** — assume the session was exfiltrated | **RTO 4 h** |
| Downloads volume lost | A3, mass `failed` | Re-point `PathSaveFile`; `POST /api/queue/retry-failed` (`Program.cs:463`). Note `ExistingDownloadValidator` (`DownloadOrchestrator.cs:113`) re-verifies rather than blindly re-downloading | **RTO days** (bandwidth-bound) |

---

## 4. Upgrade and rollback

### 4.1 Container upgrades

Current posture is not deployable as-is: `docker-compose.yml:6-7` uses
`build: .` with `image: telegram-auto-download:latest`. That means the running
version is whatever the host last compiled, there is no way to name the version in
an incident, and rollback is "hope the old layers are still cached."

**Target posture**

```yaml
services:
  telegram-auto-download:
    # Digest-pinned. Immutable, auditable, rollback = change this one line.
    image: ghcr.io/<owner>/telegramautodownload/telegram-auto-download@sha256:<digest>
    restart: unless-stopped
```

The container pipeline emits the exact line to paste
(`container-image.yml` → "Emit deployment summary").

**Upgrade runbook (container)**

```bash
set -euo pipefail
NEW_DIGEST="sha256:..."          # from the Container Image workflow summary
OLD_DIGEST=$(docker inspect --format='{{index .RepoDigests 0}}' telegram_downloader)
echo "$OLD_DIGEST" > /backup/last-known-good-image.txt   # step 0: record rollback target

# 1. Pre-flight
curl -fsS localhost:8080/healthz/ready
curl -fsS localhost:8080/api/queue/stats | jq '.byStatus'

# 2. Quiesce — stop claiming new work, and push in_progress rows back to queued
#    (Program.cs:494-501 calls RequeueInProgressAsync, MediaRepository.cs:138-157)
curl -fsS -X POST localhost:8080/api/queue/downloads \
  -H 'Content-Type: application/json' -d '{"paused":true}'

# 3. Drain — wait for in-flight transfers, then verify
sleep 60
curl -fsS localhost:8080/api/status | jq '.activeDownloads'   # expect 0

# 4. BACKUP — non-negotiable before any migration (see §4.2)
sqlite3 /data/tad.db "VACUUM INTO '/backup/pre-upgrade-$(date -u +%Y%m%dT%H%M%SZ).db';"
cp -a /data/config.json /data/TelegramAutoDownload/session.dat /backup/pre-upgrade/

# 5. Deploy
docker compose pull && docker compose up -d

# 6. Verify: migrations applied, session restored, queue moving
docker logs --since 5m telegram_downloader | grep -E 'Applied schema migration|Database ready'
curl -fsS localhost:8080/healthz/ready
curl -fsS localhost:8080/api/login/status | jq '.loggedIn'    # expect true

# 7. Resume
curl -fsS -X POST localhost:8080/api/queue/downloads \
  -H 'Content-Type: application/json' -d '{"paused":false}'

# 8. Watch for 30 minutes: A5, A6, A10.
```

Step 2 is the useful subtlety: the pause endpoint already re-queues `in_progress`
rows (`Program.cs:497-499`), so a clean pause leaves no stranded claims even if
the subsequent restart is abrupt.

### 4.2 Database schema migration safety

**What exists.** A forward-only embedded-SQL migrator: scripts are embedded
resources under `Data/Schema/` (`TelegramAutoDownload.Headless.csproj:24`),
discovered and sorted lexically (`Database.cs:76-80`), version-parsed from the
numeric filename prefix (`Database.cs:85-91`), each applied inside its own
transaction and recorded in `SchemaMigration` (`Database.cs:99-105`). There is one
script today (`Data/Schema/001_initial.sql`). This is a genuinely sound design —
better than most projects at this stage.

**Four real gaps.**

1. **No down-migrations, by design.** Rollback of the *image* after a migration has
   run leaves new schema under old code.
2. **`SchemaMigration` does not record which app version applied a row**
   (`Database.cs:65-71`), so "is this DB safe for binary X?" is not answerable.
   **DESIGN:** add `applied_by_version TEXT`, and have the binary assert
   `MAX(version) <= HighestKnownMigration` at startup, refusing to boot otherwise
   (alert A13).
3. **Migrations run before the host serves traffic** (`Program.cs:105-108`) but with
   **no pre-migration backup**. A failing migration is transactional per script
   (`Database.cs:99-105`) but a *partially successful sequence* (003 applied, 004
   failed) leaves an intermediate state. **DESIGN:** `VACUUM INTO` a
   `pre-migration-<ts>.db` snapshot before the first pending script, and keep the
   last 5.
4. **No migration timeout or lock handling.** `busy_timeout` is 5 s
   (`Database.cs:43`); a long `ALTER`/backfill on a multi-GB DB could exceed an
   orchestrator's startup probe budget. Hence the startup probe in §2.3.

**Migration authoring rules (expand → migrate → contract)**

| Rule | Reason |
|---|---|
| **Additive only** in any release that might be rolled back. `ADD COLUMN` with a default, `CREATE TABLE IF NOT EXISTS`, `CREATE INDEX IF NOT EXISTS` — all three idioms are already used (`001_initial.sql:6,35-39,42`) | Old code ignores unknown columns; it cannot invent removed ones |
| **Never drop or rename in the release that stops using a column.** Three releases: N adds + dual-writes, N+1 reads new only, N+2 drops old | Guarantees N−1 rollback works |
| **Never rewrite `Media` in place.** It is the queue **and** the dedup index and the audit log (`001_initial.sql:6-33`) | Losing it means a full re-bootstrap of every chat |
| **Backfills must be chunked and idempotent** | A single `UPDATE` over millions of rows blocks the writer and busts `busy_timeout` |
| **Every script must be re-runnable.** The `applied` set protects it (`Database.cs:73-74`), but a crash between `ExecuteAsync` and the `INSERT` at `:101-104`… | …is actually covered: both are in one transaction committed at `:105`. Preserve that property |
| **New status values are a breaking change.** `MediaStatusExtensions.ParseStatus` **throws** on unknown values (`Data/MediaStatus.cs:39-48`) | A new status written by vN and read by vN−1 crashes the older binary. Adding a status requires a tolerant-read release first |

That last row is the sharpest rollback hazard in the codebase and is easy to miss.

**Rollback decision table**

| Situation | Action |
|---|---|
| New image, **no** new migration applied | Re-point digest to last-known-good, `up -d`. Clean |
| New image, migration applied, **additive only** | Re-point digest. Old code ignores new columns. **Verify no new status values were written**: `SELECT DISTINCT status FROM Media;` must be a subset of the six known values (`MediaStatus.cs:39-47`) |
| New image, migration applied, **not** additive | **Restore the pre-migration snapshot**, then re-point digest. Media discovered in the interim is lost from the DB but re-discoverable by bootstrap |
| Migration failed mid-sequence | Restore pre-migration snapshot; do not attempt a partial fix-forward under incident pressure |

### 4.3 Desktop auto-updater — review and verdict

#### Verdict: **not safe to ship to enterprise customers as written.** Replace it.

The intent is right and the install-type detection is genuinely thoughtful —
`IsPortableInstall()` (`AutoUpdateService.cs:33-42`) correctly distinguishes
Program Files / LocalAppData installs from portable ones, and the asset choice
follows from it (`:69-99`). The *delivery mechanism* is the problem.

**Findings, highest severity first.**

| # | Severity | Finding | Evidence |
|---|---|---|---|
| **U1** | **Critical** | **Update feed points at a different owner's repository.** `GitHubOwner = "il90il90"` (`AppVersion.cs:6`) builds the URL at `AutoUpdateService.cs:24-25`, while `origin` is `kesavananbu/TelegramAutoDownload`. Builds from this repo take code from a third party's releases. That party — or anyone who compromises that account — achieves remote code execution on the installed base, elevated, via the installer path | `AppVersion.cs:6`, `AutoUpdateService.cs:24-25` |
| **U2** | **Critical** | **No integrity or authenticity verification of the downloaded payload.** The stream is written straight to disk and executed; there is no hash check, no Authenticode verification, no signed manifest | `AutoUpdateService.cs:140-158` then `:178-182` |
| **U3** | **High** | **Command injection / path traversal via a server-controlled filename.** `release.AssetName` comes from the GitHub API JSON (`:79`) and is interpolated into a filesystem path (`:130`) and then into a **batch script** (`:176`) with no validation or escaping. A crafted asset name containing `"`, `&`, or `..\` yields arbitrary batch execution or writes outside `%TEMP%` | `AutoUpdateService.cs:79`, `:130`, `:176` |
| **U4** | **High** | **TOCTOU on a world-writable, fixed-name script.** `tad_setup_updater.bat` (`:168`) and `tad_updater.bat` (`:208`) are written to predictable paths in `%TEMP%`, then executed via `cmd.exe` (`:178-182`, `:222-226`). Any process running as the user can swap the file in the window between write and execute. The fixed name also means two concurrent update attempts clobber each other | `AutoUpdateService.cs:167-182`, `:208-226` |
| **U5** | High | **Force-kill mid-work.** `taskkill /f /im TelegramAutoDownload.exe` (`:173`), plus `setup.iss:39-40` `CloseApplications=force` and `setup.iss:91` `Exec('taskkill…')`. No drain: in-flight downloads are truncated and the dedup index / config may not be flushed | `AutoUpdateService.cs:173`, `setup.iss:39-40,83,91` |
| **U6** | High | **Non-atomic in-place file replacement with no rollback.** `robocopy` over the live install directory, falling back to `xcopy` on `errorlevel 8`, then `start` the exe **unconditionally** (`:217-219`). A failure halfway leaves a mixed-version install and launches it anyway | `AutoUpdateService.cs:216-219` |
| **U7** | Medium | **PID-based wait is unsound.** The loop matches `tasklist /fi "pid eq …"` output with `findstr` (`:171-172`, `:214-215`). Windows reuses PIDs, so this can wait on an unrelated process or proceed while the app still holds file locks. `findstr`'s exit code is also being read via `if not errorlevel 1`, which is inverted-logic-prone | `AutoUpdateService.cs:170-172` |
| **U8** | Medium | **Version comparison falls back to lexicographic.** `IsNewer` uses `Version.TryParse`, but on any non-parsable tag silently compares strings (`:248-252`), where `"2.10.0" < "2.9.3"`. There is also no downgrade protection and no channel pinning | `AutoUpdateService.cs:247-252` |
| **U9** | Medium | **No staged rollout and no kill switch.** Every client polls `/releases/latest` (`:24-25`); a bad release reaches 100% of users at once with no way to stop it except deleting the release — which `release-process.mdc:35` explicitly forbids | `AutoUpdateService.cs:24-25` |
| **U10** | Low | **`del "%~f0"` self-deletion races the interpreter** (`:177`, `:220`), leaving orphaned scripts in `%TEMP%`; and the ZIP branch deletes the downloaded payload in `finally` (`:241-243`), destroying the evidence needed to debug a failed update |
| **U11** | Low | **Silent failure.** `CheckAsync` swallows every exception and returns `null` (`:112-116`), so a permanently broken update channel is indistinguishable from "up to date" |

#### Recommended design

**Adopt a maintained updater framework rather than rebuilding this.**
[**Velopack**](https://velopack.io) is the strongest fit for a .NET 8 WPF app: it
does atomic versioned-directory installs with automatic rollback, delta packages,
signature verification, staged rollout, and it replaces the entire batch-script
approach with a signed helper binary. ClickOnce and MSIX are alternatives; MSIX
additionally gets you OS-managed differential updates and a real app identity, at
the cost of packaging work and losing the portable mode.

If a hand-rolled updater must be kept, the minimum bar is:

1. **Fix U1 immediately** — point `GitHubOwner` at the repository that actually
   publishes these releases. Treat this as a security incident, not a config typo.
2. **Signed manifest + verified payload.** Publish `latest.json` (version, asset
   names, SHA-256 per asset, minimum-upgradable-from version), signed. Client:
   fetch manifest → verify signature → download → verify SHA-256 → verify
   Authenticode with a **pinned publisher name and certificate thumbprint**
   (`WinVerifyTrust`) → only then execute. The release pipeline already emits
   `SHA256SUMS.txt` and has both signing hooks wired.
3. **No generated scripts, no shell.** Ship a small signed `Updater.exe`. Launch it
   with an argument array via `Process.Start(ProcessStartInfo)` with
   `UseShellExecute = false` — no string interpolation anywhere in the path.
4. **Per-run randomised staging directory** under `%LOCALAPPDATA%` with a
   user-only ACL. Never a fixed name, never `%TEMP%`.
5. **Validate `AssetName` against `^TelegramAutoDownload_v\d+\.\d+\.\d+_(Setup\.exe|Portable\.zip)$`**
   before it touches a path or a command line. This one regex closes U3.
6. **Graceful drain handshake.** Signal the app to pause the queue, finish or
   cleanly abort the current transfer, flush config and the dedup index, then
   exit. Force-kill only as a timeout fallback, and log it.
7. **Atomic swap with rollback.** Install to `app\<version>\`, flip a junction or
   shortcut, keep N−1 on disk. Rollback is a pointer flip. A watchdog that sees two
   consecutive failed starts of the new version reverts automatically.
8. **Strict semver comparison** — no lexicographic fallback; refuse downgrades
   unless explicitly requested; honour `minimumUpgradableFrom` from the manifest.
9. **Staged rollout + kill switch.** A `rolloutPercentage` field in the manifest
   and a `blockedVersions` list lets a bad release be stopped **without deleting
   the release assets** — which is what keeps the fix compatible with
   `release-process.mdc:35`.
10. **Report outcomes.** Distinguish up-to-date / check-failed / download-failed /
    verify-failed / install-failed, surface it in the UI, and count it (S10).

**Interim mitigations available with no code change:** configure the signing
secrets so both artifacts are Authenticode-signed (the release pipeline hooks are
in place), publish `SHA256SUMS.txt` (the pipeline already does), and document the
manual upgrade path so security-sensitive customers can disable auto-update and
verify checksums by hand.

### 4.4 Desktop rollback

| Install type | Rollback |
|---|---|
| Installed | Uninstall current, install the previous `_Setup.exe`. `%APPDATA%\TelegramAutoDownload` is untouched by uninstall, so session, config, and dedup index survive (`TelegramApp.cs:102-104`) |
| Portable | Extract the previous `_Portable.zip` over the folder. Same state directory, so no data loss |
| Both | If the downgrade predates a `config.json` schema change, restore `config.json` from backup first (§3.2) |

This is the operational reason `release-process.mdc` insists on keeping both
artifacts on every release: **the rollback path for each install type requires that
type's artifact to still exist on the previous release.**

---

## 5. Operational runbook

Each entry: detection → triage → remediation → prevention.

### R1 — FLOOD_WAIT lockout

**Detect:** A2 / A2b. `GET /api/flood-wait` (`Program.cs:288`) returns
`{active, remainingSeconds, source, message}` (`Program.cs:577-584`).

**Understand:** `FloodWaitTracker` is a **process-wide** pause gate. `Report`
extends `_pausedUntil` to the furthest future value seen
(`FloodWaitTracker.cs:24-29`) and every scanner and download waits on it
(`:54-65`). So one aggressive chat pauses *everything*. The orchestrator re-queues
the affected item rather than failing it (`DownloadOrchestrator.cs:164-175`), which
is correct: no data is lost.

**Triage**
```bash
curl -s localhost:8080/api/flood-wait | jq            # remaining + which subsystem triggered it
curl -s localhost:8080/api/settings/limits | jq '{scannerApiCapacity,scannerApiRefillPerSecond,downloadThreads,activeBootstrapJobs}'
curl -s localhost:8080/api/bootstrap/jobs | jq 'length'
```

**Remediate**
1. **Wait.** It is self-clearing and the code already handles it. Do **not**
   restart to "clear" it — `_pausedUntil` is in-memory (`FloodWaitTracker.cs:13`),
   so a restart *forgets* the penalty, immediately re-offends, and earns a longer
   one. This is the single most common well-intentioned mistake here.
2. Cancel concurrent bootstraps: `DELETE /api/chats/{id}/bootstrap`
   (`Program.cs:444`).
3. Reduce the pressure that caused it (`POST /api/settings/limits`,
   `Program.cs:228`): halve `scannerApiRefillPerSecond`, drop
   `scannerApiCapacity`, reduce `downloadThreads`. Bounds are enforced at
   `:232-239`; guidance thresholds are exposed at `:219-223`.
4. For repeat offences, set `AllowParallelBootstrap=false` so the guard at
   `BootstrapManager.cs:101-108` serialises history sweeps.

**Prevent:** keep defaults (`RateLimitThresholds`, surfaced at `Program.cs:211-217`);
treat the "danger" thresholds as hard limits; run bootstraps one chat at a time;
**DESIGN:** persist `paused_until` so restarts respect it.

### R2 — Session expiry / logged out

**Detect:** A4. `GET /api/login/status` → `loggedIn: false` (`Program.cs:120-127`).
Downloads stop entirely: the orchestrator idles while `Telegram` is null or
disconnected (`DownloadOrchestrator.cs:47-51`).

**Distinguish three causes before acting** — they have different fixes:

| Cause | Symptom | Fix |
|---|---|---|
| Transient disconnect | `loggedIn: true`, `IsConnected` false | Wait; `MaxAutoReconnects = 5` (`TelegramApp.cs:107`). If exhausted, restart the container |
| Missing/unreadable `session.dat` | `stage: AwaitingPhone` right after a deploy | **Check the path.** It must be `/data/TelegramAutoDownload/session.dat`, not `/data/session.dat` (§0.1a). A volume remap or a changed `XDG_CONFIG_HOME` (`Dockerfile:46`) is the usual cause — restore from backup (§3.5) rather than re-authenticating |
| Revoked by Telegram | `stage: Failed`, `AUTH_KEY_*` / `SESSION_REVOKED` in `lastError` | Re-authenticate; a backup will not help |

**Remediate (re-authentication — needs a human with the phone)**
```bash
curl -X POST localhost:8080/api/login/phone    -H 'Content-Type: application/json' -d '{"phone":"+1555…"}'
curl -X POST localhost:8080/api/login/code     -H 'Content-Type: application/json' -d '{"code":"12345"}'
curl -X POST localhost:8080/api/login/password -H 'Content-Type: application/json' -d '{"password":"…"}'   # 2FA only
curl -s localhost:8080/api/login/status | jq
```
`LoginCoordinator` serialises these through a semaphore (`:26`, `:68`) — do not
fire them concurrently. Stages map from WTelegram's next-field response
(`:77-82`). After success, back up the new `session.dat` **immediately** (§3.3),
paired with its `config.json`.

**Also check:** is a second instance running against the same session? Two clients
on one auth key gets it revoked (§3.3(4)). Verify before re-authenticating, or you
will do it twice.

**Prevent:** A4 at 10 minutes; nightly session backup; the single-writer lock file;
never copy `/data` to a second live host.

### R3 — Disk full

**Detect:** A3 / A3b. Mass `failed` rows with I/O errors in `last_error`
(`/api/queue/items?status=failed`, `Program.cs:397`).

**Triage**
```bash
df -h /data /downloads
du -sh /downloads/* | sort -h | tail -20
ls -la /data/tad.db*                     # WAL can be large; see R5
curl -s localhost:8080/api/queue/stats | jq '.byStatus'
```

**Remediate**
1. **Pause immediately** so failures stop accruing against S2:
   `POST /api/queue/downloads {"paused":true}` — it also re-queues `in_progress`
   rows (`Program.cs:497-499`).
2. Free space, or re-point the download root:
   `POST /api/settings/download-folder` (`Program.cs:179`).
3. Delete truncated partials from the failed window (identify via
   `downloaded_path`, `001_initial.sql:25`).
4. Resume, then `POST /api/queue/retry-failed` (`Program.cs:463`).
   `ExistingDownloadValidator` re-checks what is already on disk
   (`DownloadOrchestrator.cs:113-127`), so this is not a blind re-download.

**Prevent:** predictive alert A3b; per-chat `DownloadFromSize` minimums
(`Program.cs:268`); quota on `/downloads`; **DESIGN:** a pre-download free-space
check that `skipped`s with a clear reason instead of failing — the desktop app
already has the measurement primitive (`Services/DiskSpaceService.cs:27`).

### R4 — Corrupt SQLite database

**Detect:** startup failure from `InitializeAsync` (`Program.cs:107`),
`SQLITE_CORRUPT` in logs, or A8b.

**Triage — read-only, do not write to the suspect file**
```bash
docker compose stop telegram-auto-download        # a live writer makes everything worse
sqlite3 /data/tad.db "PRAGMA integrity_check;"
sqlite3 /data/tad.db "PRAGMA quick_check;"
ls -la /data/tad.db /data/tad.db-wal /data/tad.db-shm
cp -a /data/tad.db /data/tad.db-wal /data/tad.db-shm /backup/forensics/   # preserve evidence
```

**Remediate, in order of data preservation**

1. **WAL not yet checkpointed** (DB fine, `-wal` present):
   ```bash
   sqlite3 /data/tad.db "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA integrity_check;"
   ```
2. **Salvage via dump** (recovers most rows):
   ```bash
   sqlite3 /data/tad.db ".recover" > /tmp/recovered.sql     # .recover beats .dump on corruption
   sqlite3 /data/tad-new.db < /tmp/recovered.sql
   sqlite3 /data/tad-new.db "PRAGMA integrity_check; SELECT COUNT(*) FROM Media;"
   mv /data/tad.db /backup/forensics/tad.corrupt.db && mv /data/tad-new.db /data/tad.db
   ```
3. **Restore from backup** (§3.5 steps 1–3), then start. Crash recovery resets
   stranded claims (`Database.cs:56-60`) and bootstrap re-discovers anything
   missed since the snapshot.
4. **Last resort — rebuild empty.** Delete `tad.db*`; migrations recreate the
   schema (`Database.cs:63-109`) and `LegacyDedupMigration` re-imports the legacy
   dedup index if present (`Program.cs:108`, `HeadlessPaths.cs:32-34`). **Cost:**
   the dedup index is gone, so already-downloaded files may re-download until
   `ExistingDownloadValidator` (`DownloadOrchestrator.cs:113`) catches them on
   disk. Then re-bootstrap each monitored chat (`Program.cs:415`), rate-limited.

**Prevent:** hourly `VACUUM INTO` + `integrity_check` (§3.1); `synchronous=NORMAL`
is already set (`Database.cs:41`) — consider `FULL` on hosts with unreliable power;
never snapshot the live file with `cp`; keep `/data` off network filesystems (NFS
locking breaks SQLite).

### R5 — Stuck queue

**Detect:** A6 / A6b / A6c / A6d.

**Decision tree — identify which of five distinct causes you have:**

```bash
curl -s localhost:8080/api/queue/stats | jq '.byStatus'
curl -s localhost:8080/api/status      | jq '{loggedIn,downloadsPaused,activeDownloads,monitoredChats}'
curl -s localhost:8080/api/flood-wait  | jq '.active'
```

| Symptom | Cause | Fix |
|---|---|---|
| `downloadsPaused: true` | Intentional pause (`DownloadOrchestrator.cs:55-59`) | Unpause (`Program.cs:494`) |
| `flood-wait active` | R1 | Wait |
| `loggedIn: false` | R2 | Re-auth |
| Many `queued`, **no chats monitored** | `PickQueuedAsync` filters by monitored chat IDs (`DownloadOrchestrator.cs:69-71`, `MediaRepository.cs:202`). **An empty monitored list returns zero rows, so the queue looks stuck while working exactly as written** | `PATCH /api/chats/{id}` with `{"selected":true}` (`Program.cs:261`) |
| Many `pending`, `activeBootstrapJobs: 0` | The §0.2 restart gap: promotion `pending → queued` only happens on successful bootstrap completion (`BootstrapScanner.cs:129-134`) | Re-run bootstrap: `POST /api/chats/{id}/bootstrap` (`Program.cs:415`). The persisted watermark makes it cheap (`BootstrapScanner.cs:112`) |
| Rows stuck `in_progress`, nothing completing | Claim without a lease (`MediaRepository.cs:199-217`); the worker task died | Pause + unpause — the pause path calls `RequeueInProgressAsync` (`Program.cs:497-499`, `MediaRepository.cs:138-157`). Or restart: startup reverts them (`Database.cs:56-60`) |
| Rows `queued`, chat deleted from config | Dispatch fails fast with "Chat removed from config" (`DownloadOrchestrator.cs:100-107`) | `POST /api/queue/clear?chatId=…` (`Program.cs:481`) |

**Bulk tools:** `retry-failed`, `retry-skipped`, `delete-failed`
(`Program.cs:463`, `:469`, `:475`), and single-item retry (`:456`).

**Prevent:** A6 with its pause/flood-wait exclusions so it stays actionable;
**DESIGN:** a claim lease (`claimed_until`) with a reaper, plus the startup
`pending` reconciliation.

### R6 — yt-dlp breakage after a site change

This is the **highest-frequency functional failure** in this product and deserves
to be treated as routine maintenance rather than an incident.

**Why it happens:** `yt-dlp` extractors break whenever a site changes its player.
Upstream fixes within days; the fix only reaches you if your `yt-dlp` updates.

**Why the two surfaces behave differently:**

| Surface | yt-dlp source | Updates? |
|---|---|---|
| Desktop | Downloaded to `%APPDATA%\…\tools\yt-dlp.exe`, version-checked against GitHub on **every launch** (`Services/YtdlpService.cs:41-108`, compare at `:96-104`) | **Yes, automatically** |
| Container | Debian apt at image build (`Dockerfile:34-37`) | **No. Frozen until the image is rebuilt** |

**Detect:** A7; the `ytdlp-canary` job in `scheduled-maintenance.yml`; tickets
saying "Instagram/YouTube links stopped downloading" while Telegram-native media
still works.

**Triage**
```bash
docker exec telegram_downloader yt-dlp --version
curl -s https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest | jq -r .tag_name
curl -s 'localhost:8080/api/queue/items?status=failed&limit=50' | jq -r '.[].lastError' | sort | uniq -c | sort -rn
docker exec telegram_downloader yt-dlp -v --simulate '<one failing URL>'    # confirm it is extractor-side
```

**Remediate — container**
1. **Immediate, in-place (survives until the container is recreated):**
   ```bash
   docker exec -u 0 telegram_downloader sh -c \
     'curl -fsSL https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /usr/local/bin/yt-dlp && chmod 755 /usr/local/bin/yt-dlp'
   docker exec telegram_downloader yt-dlp --version
   ```
   Note `docker-compose.yml:11` runs as `1029:100`, hence `-u 0`. This is a
   deliberate, logged, temporary intervention.
2. **Durable:** re-run the `Container Image` workflow and roll to the new digest
   (§4.1).
3. Then `POST /api/queue/retry-failed` (`Program.cs:463`).

**Remediate — desktop:** relaunch the app; `YtdlpService.EnsureAsync` self-updates
(`:41-45`). If GitHub is unreachable the check is swallowed and status shows
"Update check failed" (`:110-114`) — check egress.

**Prevent (DESIGN, highest-value reliability change available for the container):**
install `yt-dlp` from its upstream release in the Dockerfile instead of apt, and
add a startup/daily self-update path mirroring `YtdlpService`; pin and record the
version in `tad_build_info`; if a site is persistently broken, disable that
plugin per chat (`EnabledPlugins`, `Program.cs:277-278`) so items are `skipped`
with a clear reason rather than accumulating as `failed` and burning S2 budget.

### R7 — Container restart loop

**Detect:** A10. `restart: unless-stopped` (`docker-compose.yml:10`) makes this
otherwise invisible.

**Triage**
```bash
docker inspect telegram_downloader --format '{{.RestartCount}} {{.State.ExitCode}} {{.State.Error}}'
docker logs --tail 200 telegram_downloader
```
Likely causes, in order: `config.json` unparsable (`ConfigStore.cs:29-30`),
migration failure (`Program.cs:107`), `/data` not writable — the static
constructor calls `Directory.CreateDirectory` for four paths and will throw
(`HeadlessPaths.cs:37-42`), or a port conflict on `LISTEN_URL`
(`Program.cs:42`).

**Remediate:** stop the loop first (`docker compose stop`) so logs stop churning;
fix the root cause (R4 for migrations, §3.6 for config); check ownership matches
`user: "1029:100"` (`docker-compose.yml:11`); then start and watch.

### R8 — Locked out of the web UI

**Detect:** login rejected at `POST /api/web-auth/login` (`Program.cs:68`).

**Remediate:** credentials live in `/data/web-auth.json` after the first change
(`WebAuthService.cs:18`), otherwise they come from `WEB_USERNAME`/`WEB_PASSWORD`
(`docker-compose.yml:16-18`). Stop the container, move `web-auth.json` aside, set
the env vars, start — `WebAuthService.Load()` falls back to env
(`:63-73`). Emergency-only: `WEB_AUTH_ENABLED=false` (`:50-57`) **and never on an
exposed network** — it disables the gate entirely.

**Prevent:** A12 catches the `changeme` default still being active
(`WebAuthService.cs:16`, warned at `:69-71`); document that rotating
`web-auth.json` invalidates all cookies because the signing key is regenerated
(`:63`).

### R9 — Backup or restore drill failed

**Detect:** A8 / A8b, or a failed §3.5 drill.

**Triage:** if `integrity_check` failed → the snapshot was taken with `cp` on a
live WAL DB (§3.1), or the source was already corrupt (R4). If the session did not
restore → almost always the path issue in §0.1(a), or a config/session mismatch
(§3.3(2)).

**Remediate:** treat a failed drill as a **P1**. Until a drill passes, S6 and S7
are unmet regardless of what the backup job reports. Re-run against the previous
snapshot to find the last good one, and fix the job before the next window.

---

## 6. Build-environment risk: SDK 10.0.401 vs net8.0 targets

**Verified:** `dotnet --version` → `10.0.401`. Every project targets `net8.0`
(`TelegramAutoDownload.Headless.csproj:5`) or `net8.0-windows`
(`TelegramAutoDownload.csproj:5`, `TelegramAutoDownload.Tests.csproj:4`,
`TelegramAutoDownload.UITests.csproj:4`). There is no `global.json` anywhere in
the tree.

**Recommendation: yes, pin — but the team that owns the build must add the file, not this change.**

Adding `global.json` changes build behaviour for everyone immediately, and on any
machine without a .NET 8 SDK installed it makes the build fail outright. That is
exactly the kind of surprise that must not arrive from a docs/CI-only change while
another agent is mid-build. Recommended content, to be added deliberately:

```json
{
  "sdk": {
    "version": "8.0.400",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

`latestFeature` accepts any `8.0.4xx` SDK, so patch-level differences between
developers and CI do not break anything, while a major-version jump does — which
is the point.

**Why pin at all, given that SDK 10 can build `net8.0`?** It can, and the container
build is already immune because it uses `mcr.microsoft.com/dotnet/sdk:8.0`
(`Dockerfile:5`). The risks are the desktop path and developer/CI divergence:

1. **The desktop release is a self-contained single-file publish**
   (`release-process.mdc:20`), so the SDK's bundled runtime and host are baked into
   the shipped executable. Publishing with an SDK 10 host produces a different
   artifact than CI would with SDK 8 — and `setup.iss:50-54` packages that exact
   output, including the native WPF DLLs that cannot be embedded.
2. **Analyser and default-behaviour drift.** New SDKs enable new analysers and
   change MSBuild defaults; without a pin, "works on my machine" and "works in CI"
   diverge silently.
3. **Reproducibility.** Debugging a customer's binary requires knowing which SDK
   produced it.
4. **The Dockerfile is already pinned to 8** (`Dockerfile:5`, `:32`) — the local
   environment is the odd one out, not CI.

**What CI installs (already encoded in the workflows):**

| Workflow / job | Runner | Installs | Why |
|---|---|---|---|
| `pr-validation.yml` → `build-test` | `windows-latest` | .NET SDK **8.0.x** | `net8.0-windows` tests project-reference WPF (`TelegramAutoDownload.Tests.csproj:4,28`) |
| `pr-validation.yml` → `dependency-scan`, `static-analysis` | `windows-latest` | .NET SDK 8.0.x | needs a restore that resolves the Windows TFMs |
| `pr-validation.yml` → `secret-scan` | `ubuntu-latest` | none | TruffleHog / grep only |
| `pr-validation.yml` → `container-smoke` | `ubuntu-latest` | none | the SDK lives inside the Docker build stage (`Dockerfile:5`) |
| `codeql.yml` → csharp | `windows-latest` | .NET SDK 8.0.x | CodeQL needs a real build |
| `container-image.yml` | `ubuntu-latest` | none | buildx only |
| `release-desktop.yml` → `test`, `build` | `windows-latest` | .NET SDK 8.0.x **+ Inno Setup** (choco) | Inno Setup is Windows-only; SDK 8 must match the pin |
| `scheduled-maintenance.yml` → `dependency-cve` | `windows-latest` | .NET SDK 8.0.x | restore must resolve Windows TFMs |

Every workflow uses `dotnet-version: "8.0.x"` today and includes an explicit guard
that fails if the resolved SDK is not 8.x. **Once `global.json` exists, switch each
`setup-dotnet` step to `global-json-file: global.json`** so the pin has exactly one
source of truth. This is item 1 on the validation checklist in
[`05-cicd.md`](./05-cicd.md).

---

## 7. Prioritised gap list

| # | Gap | Impact | Effort | Owner |
|---|---|---|---|---|
| 1 | **Updater points at a third-party repo** (`AppVersion.cs:6`) | **RCE on installed base** | Trivial | Source owner — **do this first** |
| 2 | **No backup of anything** | Session loss ⇒ human-gated recovery; DB loss ⇒ days of re-bootstrap | Low (§3, no code needed) | Ops |
| 3 | No artifact signing | SmartScreen blocks installs; U2 unfixable without it | Low (hooks exist in the pipeline) | Ops + release owner |
| 4 | No health endpoints | No automated failure detection at all | Low | Source owner |
| 5 | No metrics | Every SLO here is unmeasurable | Medium | Source owner |
| 6 | Updater U2–U4 (no verification, TOCTOU, injection) | Code execution during update | Medium (adopt Velopack) | Source owner |
| 7 | Container `yt-dlp` frozen | Recurring functional breakage with no in-product remedy | Low (Dockerfile change) | Source owner |
| 8 | `config.json` torn-write | Startup failure after power loss | Low (temp+rename; pattern exists at `YtdlpService.cs:185-187`) | Source owner |
| 9 | `pending` rows orphaned by restart | Silent partial stall | Low | Source owner |
| 10 | FLOOD_WAIT state not persisted | Restart re-offends and earns a longer penalty | Low | Source owner |
| 11 | No encryption at rest | Session + `ApiHash` in plaintext on disk | Medium | Ops (volume) + source (session key) |
| 12 | 7-day log retention (`Program.cs:20`) | Incidents older than a week are unanalysable | Trivial | Source owner |
| 13 | No `.sln` (`README.md:111`) | CI must synthesise one; onboarding friction | Trivial | Source owner |
| 14 | No `global.json` | Build drift; non-reproducible desktop releases | Trivial | Build owner (§6) |

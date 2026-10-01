# 03 — Test Strategy

**Companion to:** `docs/quality/03-feature-verification.md` (feature verdicts and evidence).
**Inputs:** `TelegramAutoDownload.Tests` (25 files, ~230 test methods, xUnit + FluentAssertions) and `TelegramAutoDownload.UITests` (2 files, 7 FlaUI smoke tests).
**Method:** static reading. No test run was performed, so this document reports *what exists and what it covers*, not pass/fail.

---

## 1. Coverage mapping — feature → test file

Legend: **Direct** = a test exercises the production code path. **Indirect** = a test covers a helper the feature depends on, but not the feature's own control flow. **UNTESTED** = no test touches it.

### 1.1 Download core

| Feature | Verdict (from 03-feature-verification) | Test coverage | Files |
|---|---|---|---|
| Automatic media download (live update → handler) | WORKS | **UNTESTED** | `Client_OnUpdates` (`TelegramApp.cs:257-501`) has no test of any kind. Routing *inputs* are tested (`MessageTypeRoutingTests.cs`, 15 tests) but the orchestration — semaphore, reaction ordering, retry wiring, history append, error classification — is not. |
| Message-type routing (photo/video/music/file/text) | WORKS | **Direct** | `MessageTypeRoutingTests.cs:31-172` (15 tests), `DocumentMediaKindHelperTests.cs:12-45` (4) |
| Resume via `.part` | PARTIAL | **Indirect only** | `IntegrationTests.cs:178, 212, 236` assert the *stream contract* (seek-to-end, create-new, rename-on-complete). They never invoke `DownloadDocumentAsync`, so neither named gap — missing size validation before `File.Move` (`BaseMessage.cs:145`) nor the dead `ct.IsCancellationRequested` guard (`:238`) — is covered. |
| Stale `.part` cleanup | WORKS | **Direct** | `IntegrationTests.cs:296, 326, 349, 358` (4 tests, incl. recursion and missing-directory) |
| Dedup by document ID | PARTIAL | **Direct (in-memory only)** | `FileDownloadIndexConcurrencyTests.cs:28-200` (9 tests incl. parallel writes, concurrent remove/mark), `IntegrationTests.cs:386`. **Not covered:** the 10 s flush window, `Flush()` on exit, JSON round-trip through `downloaded_ids.json`, recovery from a corrupt index file. |
| Min file size per chat | PARTIAL | **Direct (happy path)** | `DownloadPolicyTests.cs:32, 45, 56` (3 tests). **Not covered:** the integer-truncation boundary (2.9 MB vs threshold 2) or the photo bypass. |
| Parallel downloads / `DownloadThreads` | PARTIAL | **Indirect** | Persistence only: `ConfigFilePersistenceTests.cs:90`, `ConfigFileTests.cs`, `SettingsExportImportTests.cs:38`. The semaphore itself — including the replace-on-`UpdateConfig` over-subscription bug — is **UNTESTED**. |
| Retry button / `CanRetry` | PARTIAL | **Direct (model only)** | `NewFeaturesTests.cs:51-136` (6 tests on property-change notifications and state transitions). **Not covered:** that the row survives long enough to click (the 4 s auto-removal), or that plugin failures produce a retry action at all. |
| Cancel / Cancel-all | WORKS | **Direct, strong** | `CancellationRegistryTests.cs:21-181` (13 tests), `CancelAllTests.cs:31-262` (12 tests incl. concurrent register/cancel and the "token registered before key assigned" regression) |
| Stuck-download watchdog | WORKS | **UNTESTED** | `DownloadProgressService.CheckForStuckDownloads` (`:56-88`) and the 3-minute inactivity CTS (`BaseMessage.cs:68`) have no tests. |
| Auto clean completed rows | PARTIAL | **UNTESTED** | `ShouldAutoRemoveCompletedItem` (`DownloadProgressService.cs:311`) untested — which is exactly why the error-row/success-row asymmetry went unnoticed. |
| Open file / folder | WORKS | **Direct (predicate only)** | `NewFeaturesTests.cs:22, 38` (`CanOpenFile`) |
| Progress / speed / ETA | PARTIAL | **UNTESTED** | No test of `UpdateProgress` (`:179-257`), `FormatSpeed`, `FormatEta`, or `GetTotalCurrentSpeed`. Preview-name matching is covered (`DownloadProgressPreviewTests.cs:15, 23`). |

### 1.2 Per-chat settings

| Feature | Verdict | Test coverage | Files |
|---|---|---|---|
| Provider toggles / plugin priority chain | WORKS / PARTIAL | **Direct** | `IntegrationTests.cs:93-177` (5 tests: ascending priority, SocialMedia-beats-YouTube, magnet→Torrent, generic http→Other), `PluginCanHandleTests.cs:26-138` (11 tests) |
| Per-chat yt-dlp quality | **BROKEN** | **Direct — the test asserts the broken behaviour** | `ConfigFilePersistenceTests.cs:136` (`SaveAndRead_YtdlpQuality_IsAlwaysHighestAfterRead`) and `IntegrationTests.cs:269, 285` lock in the override. The format-string mapping is tested in isolation (`IntegrationTests.cs:32-92`, 6 tests) but is unreachable in production. |
| Regex filter — parsing | PARTIAL | **Direct** | `NewFeaturesTests.cs:249-305` (7 tests), `FilterDialogTests.cs:27-139` (10 tests incl. validity, live-test, escape) |
| Regex filter — enforcement | PARTIAL | **Thin** | `DownloadPolicyTests.cs` covers size only. **Not covered:** case-sensitivity mismatch, null `Document.Filename`, invalid pattern throwing out of `CheckDownloadPolicy`. |
| Filter text-capture (`.txt` writing) | undocumented behaviour | **UNTESTED** | `SaveCapturedTextAsync` (`TelegramApp.cs:1321`) has no test despite writing files and firing reactions. |
| Folder template | PARTIAL | **Direct, strong** | `NewFeaturesTests.cs:141-231` (10 tests), `:440-511` absolute-path suite (8), `:535-611` dialog preview (8), `PluginFolderPathHelperTests.cs:10-41` (4). **Not covered:** that `{Year}/{Month}/{Day}` resolve to the *download* date rather than the message date — no test passes an `at` override against a message timestamp. |
| Emoji reactions (start / end / history) | PARTIAL | **UNTESTED** | `ReactToMessage` (`TelegramApp.cs:1435`), including the `updates.Chats.First()` peer selection, has no test. |
| Mute toggle | PARTIAL | **UNTESTED** | `MuteChatAsync` (`:1594`) untested; the missing `InputPeerUser` branch is unguarded. |
| Download types (V/P/M/F) | WORKS | **Direct** | `MessageTypeRoutingTests.cs` (routing), `IntegrationTests.cs:408-516` (sticker/voice/audio) |
| Monitored-chat selection | WORKS | **Direct** | `SelectedChannelFilterTests.cs:24-135` (9 tests incl. the negative-ID variant) |

### 1.3 Sync, history, catch-up

| Feature | Verdict | Test coverage | Files |
|---|---|---|---|
| History sync — pagination correctness | WORKS | **Direct** | `IntegrationTests.cs:553, 582, 600` — the three regressions that matter (service-message-only page does not stop early; `offset_id` uses raw min not filtered min; empty page stops) |
| History sync — end-to-end execution | WORKS | **UNTESTED** | `SyncHistoryAsync` (`:507-701`) is never invoked by a test; only its pagination arithmetic is re-implemented in the test. |
| Missed-update catch-up | PARTIAL | **Partial** | `IntegrationTests.cs:609, 620` cover watermark arithmetic. **Not covered:** that URL/magnet messages are dropped, or that download-type toggles are ignored. |
| Sticker / voice filtering | WORKS | **Direct** | `IntegrationTests.cs:408, 434, 457, 483`, `MessageTypeRoutingTests.cs:83, 118` |
| JSONL history append + export | WORKS | **Direct, strong** | `ChatHistoryTests.cs:35-269` (21 tests incl. sanitisation, unicode, corrupt-line skip, 100-entry round-trip, same-name collision) |
| URL / magnet message detection | WORKS | **Direct** | `IntegrationTests.cs:517, 526, 538`, `MagnetLinkHelperTests.cs`, `ManualBrowseSelectTests.cs:20-47` |
| Members export | undocumented | **Direct** | `MembersExportTests.cs` (316 lines) — the most thoroughly tested undocumented feature in the codebase |

### 1.4 Statistics, monitoring, notifications, updates

| Feature | Verdict | Test coverage | Files |
|---|---|---|---|
| Session stats | WORKS | **UNTESTED** | Counters in `DownloadProgressService.cs:277` untested. |
| All-time stats (`stats.json`) | PARTIAL | **Direct (data layer)** | `NewFeaturesTests.cs:320, 338, 350` (round-trip, atomic increment, file write/read). The `StatisticsService` singleton itself and the `Reset()` race are untested. |
| Disk space | WORKS | **Direct (pure functions)** | `DiskSpaceServiceTests.cs:16, 22, 42` (3 tests). The 60 s timer, generation guard and cancellation path are untested. |
| Telegram bot notifications | WORKS | **Direct (message bodies only)** | `NotificationTests.cs:28-75` (6 tests on text content). **Not covered:** the progress-message sentinel race (`Notification.cs:44-58`), the 25 % throttle, edit-vs-send fallback, or any HTTP interaction. |
| Total speed badge | PARTIAL | **UNTESTED** | |
| Tray icon / close dialog | WORKS | **Enum only** | `NewFeaturesTests.cs:376, 384, 392` test the `CloseAction` enum values. Actual tray minimise/restore is untested; the FlaUI suite does not cover it. |
| Auto-update (check, version compare, asset selection, install) | WORKS / PARTIAL | **UNTESTED** | `AutoUpdateService.cs` (229 lines) has **zero** tests — including `IsNewer` version comparison (`:248`), portable detection (`:33`) and asset selection (`:74-99`). |
| Logging / alert plumbing | — | **Direct** | `AppLogServiceTests.cs` (3), `SelectedChannelFilterTests.cs:153-398` — `AppLogAlertService` (11) and `LogAlertSink` (11) |
| Session lock detection | — | **Direct** | `SessionLockHelperTests.cs:11, 19` |

### 1.5 Config and settings

| Feature | Verdict | Test coverage | Files |
|---|---|---|---|
| `ConfigFile` save/load round-trip | WORKS | **Direct, strong** | `ConfigFilePersistenceTests.cs:35-206` (12 tests), `ConfigFileTests.cs:11-45` (3) |
| Corrupt-config resilience | **BROKEN** | **Direct — the test asserts the broken behaviour** | `ConfigFilePersistenceTests.cs:193` is literally named `Read_CorruptJson_ThrowsJsonReaderException`. The CHANGELOG claims the opposite. Note the *apparent* contradiction with `ConfigFileTests.cs:37` (`ConfigParams_CorruptJson_ReturnsNewInstance`): that test exercises raw `JsonConvert` with error-tolerant settings, not `ConfigFile.Read()`. Two tests, two different behaviours, one of which ships. |
| Settings export / import | **BROKEN (README claim)** | **Direct — the test asserts the leak** | `SettingsExportImportTests.cs:29` asserts `json.Should().Contain("api_hash_secret")`. |
| Atomic config write | defect | **UNTESTED** | No test kills a process mid-`Save`. |

### 1.6 Plugins

| Plugin | Test coverage | Files |
|---|---|---|
| `DownladerPlugin` ("Other") | **Direct, strongest plugin coverage** — 6 tests with a mocked `HttpMessageHandler`: success + file creation, zero content-length, server error, pre-cancelled, no `Content-Disposition` (`DownloadPluginTests.cs:51-215`) | Still misses the two real bugs: chunk-retry file corruption (`DownladerPlugin.cs:82-91`) and unverified `206` responses. |
| `TorrentPlugin` / `TorrentDownloadService` | **Partial** — `TorrentDownloadServiceTests.cs:20-88` (4 tests: filename detection, name resolution, MonoTorrent parse, progress start) | No swarm completion, no cancellation, no timeout. |
| `SocialMediaPlugin` | **`CanHandle` only** — `PluginCanHandleTests.cs:48-76`, `PlatformNameHelperTests.cs:17, 21` | `ExecuteAsync` (175 lines, process invocation, output parsing, error mapping) is **UNTESTED**. |
| `YouTubePlugin` | **`CanHandle` only** — `PluginCanHandleTests.cs:26-47` | `ExecuteAsync` **UNTESTED**. |
| Plugin loading by reflection | **UNTESTED** | `MessageTextFactoryService` constructor (`:21-87`) — duplicate-assembly handling, `ReflectionTypeLoadException`, read-only install dir — has no test. |

### 1.7 The headless application

| Component | Lines | Test coverage |
|---|---|---|
| `Program.cs` — 40+ HTTP endpoints | 569 | **UNTESTED** |
| `Data/MediaRepository.cs` | 313 | **UNTESTED** |
| `Data/Database.cs` (migrations) | 98 | **UNTESTED** |
| `Data/LegacyDedupMigration.cs` | 42 | **UNTESTED** |
| `Scanning/ChatDownloadTester.cs` | 310 | **UNTESTED** |
| `WebAuthService.cs` | 228 | **UNTESTED** |
| `Logging/HeadlessLogReader.cs` | 198 | **UNTESTED** |
| `Scanning/DownloadOrchestrator.cs` | 176 | **UNTESTED** |
| `Scanning/BootstrapManager.cs` / `BootstrapScanner.cs` | 270 | **UNTESTED** |
| `HeadlessHost.cs` | 208 | **UNTESTED** |
| `Scanning/MessageMapper.cs`, `MediaTracker.cs`, `ExistingDownloadValidator.cs`, `TokenBucketRateLimiter.cs`, `FloodWaitTracker.cs` | ~440 | **UNTESTED** |
| `wwwroot/app.js` | 1063 | **UNTESTED** |

> **The single most important finding in this document: the headless application — roughly 3,300 lines of C# plus 1,063 lines of JavaScript, and the intended commercial surface — has zero automated tests.** The `TelegramAutoDownload.Tests` project does not reference the headless project at all. Every existing test targets the WPF app or the shared `TelegramClient`/`BasePlugin` core.

### 1.8 Coverage summary

| Area | Production lines (approx.) | Assessment |
|---|---|---|
| Pure helpers (`FolderTemplateHelper`, `YtdlpFormatHelper`, `MagnetLinkHelper`, `DocumentMediaKindHelper`, `PartFileCleanup`, `CancellationRegistry`, `ChatHistoryService`, `SessionLockHelper`, `PlatformNameHelper`, `PluginFolderPathHelper`) | ~600 | **Good.** This is where the ~230 tests concentrate. |
| `TelegramApp` orchestration | 1,602 | **Near zero.** Pagination arithmetic is re-implemented in tests; the class itself is never instantiated. |
| WPF UI + services | ~3,200 | **Thin.** Models and pure functions only; 7 FlaUI smoke tests. `AutoUpdateService` (229 lines) has none. |
| Plugins | ~800 | **`CanHandle` well covered; `ExecuteAsync` almost entirely uncovered** except `DownladerPlugin`. |
| Headless | ~3,300 + 1,063 JS | **Zero.** |

The suite's shape is a classic proof-of-concept artefact: excellent coverage of the parts that were easy to test in isolation, none of the parts that carry the product's risk. There is no integration layer, no fake Telegram transport, no HTTP-level test, and no failure-injection anywhere.

---

## 2. Prioritised test-gap list

Ranked by **likelihood of breaking × user impact**. "Likelihood" weighs how often the path runs, how much concurrency and I/O it touches, and whether a defect is already identified in `03-feature-verification.md`.

### Tier 1 — Critical (build first)

| # | Untested behaviour | Likelihood | Impact | Why it ranks here |
|---|---|---|---|---|
| 1 | **Headless HTTP API surface** — all 40+ endpoints in `Program.cs` | High | Critical | The commercial product's entire control plane. No test asserts that `PATCH /api/chats/{id}` persists, that `POST /api/queue/delete-failed` deletes only what it should, or that a malformed body returns 400 rather than 500. `Program.cs:276-277` accepts arbitrary regex strings straight into the download path. |
| 2 | **SQLite queue state machine** — `MediaRepository` (313 lines) | High | Critical | `PickQueuedAsync` is an atomic claim-and-flip; if it is not truly atomic, two orchestrator threads download the same message. Nothing verifies the `pending → queued → in_progress → done/failed/skipped` transitions, the unique constraint on `(chat_id, message_id)`, or migration idempotency. Silent data loss or duplicate downloads. |
| 3 | **`.part` resume correctness end to end** | High | Critical | Two identified defects: no size validation before `File.Move` (`BaseMessage.cs:145`) means a truncated file is promoted and marked complete; the dead cancel guard (`:238`) turns a watchdog abort into three more retry attempts. Failure mode is **silent corruption** — the user gets a file that looks fine and is not. |
| 4 | **Live-update orchestration** — `Client_OnUpdates` (`TelegramApp.cs:257-501`) | High | Critical | 240 lines running on every message: semaphore acquire/release across a config swap, reaction ordering, dedup-vs-failure classification (`:433-441`), retry wiring, history append. Entirely untested. A `Release()` on a replaced semaphore throws `SemaphoreFullException` and the comment at `:343-346` shows the team already hit this once. |
| 5 | **Corrupt `config.txt` → unrecoverable app** | Medium | Critical | `ConfigFile.Read()` throws on malformed JSON (`:30`) and `Save()` is non-atomic (`:24`). A crash during any settings write bricks the installation. The existing test asserts the throw rather than fixing it. |
| 6 | **Concurrent semaphore replacement in `UpdateConfig`** | High | High | Fires on every UI checkbox click and every headless `PATCH`. Over-subscribes the download limit and can throw on release. |
| 7 | **`DownladerPlugin` chunk retry corrupts the file** | Medium | Critical | `CopyToAsync` appends at the current position on retry (`:89`) with no seek reset, and no `206` verification (`:87`). Produces corrupt output with a success result. The plugin has 6 tests and none of them retries. |
| 8 | **Web authentication** — `WebAuthService` (228 lines) | Medium | Critical | Cookie signing, expiry, constant-time comparison, default-password detection, the `SaveCredentials` path. Also needs a test pinning the middleware ordering defect (`Program.cs:48-49`) once fixed. An auth bypass exposes the user's entire Telegram account. |

### Tier 2 — High

| # | Untested behaviour | Likelihood | Impact |
|---|---|---|---|
| 9 | **Dedup index durability** — 10 s flush window, no `Flush()` in the headless host, corrupt-file recovery (`FileDownloadIndex.cs:95-121`) | High | High — mass re-download after an unclean shutdown |
| 10 | **Retry affordance lifetime** — error rows removed after 4 s (`DownloadProgressService.cs:311`); plugin failures never get a retry action (`TelegramApp.cs:446`) | High | High — an advertised recovery path the user cannot use |
| 11 | **Regex filter enforcement edge cases** — null `Document.Filename` (`BaseMessage.cs:294`), invalid pattern thrown outside the try (`FactoryMessagesService.cs:120`), case-sensitivity | Medium | High — one bad pattern can break a chat's whole download path |
| 12 | **`BootstrapScanner` resume and promotion** — `PromotePendingToQueuedAsync` promotes live-path rows too (`BootstrapScanner.cs:132`) | Medium | High — duplicate downloads, wasted API budget, FLOOD_WAIT |
| 13 | **Rate limiter + FLOOD_WAIT** — `TokenBucketRateLimiter`, `FloodWaitTracker`, `FloodWaitHelper.TryParseSeconds` | Medium | High — a parsing miss means hammering Telegram into an account ban |
| 14 | **`AutoUpdateService`** — `IsNewer` (`:248`), portable detection (`:33`), asset selection (`:74-99`) | Medium | High — a bad comparison either blocks all updates or downgrades users |
| 15 | **yt-dlp plugin `ExecuteAsync`** — output parsing (`Destination:` scraping), warning-vs-error classification, cancellation | High | High — the primary path for social/YouTube; "success with no file" is reachable at `SocialMediaPlugin.cs:204-211` |
| 16 | **Min-size boundary + photo bypass** (`BaseMessage.cs:276-289`) | Medium | Medium |
| 17 | **Filter text-capture writes files** (`TelegramApp.cs:1321`) | Medium | Medium — surprising disk writes + reactions |

### Tier 3 — Medium

| # | Untested behaviour | Likelihood | Impact |
|---|---|---|---|
| 18 | Missed-update catch-up drops URL/magnet messages and ignores type toggles (`TelegramApp.cs:1395`) | Medium | Medium |
| 19 | `ReactToMessage` wrong-peer selection in batched updates (`:1443`) | Low | Medium — a reaction lands in the wrong chat, visible to third parties |
| 20 | `MuteChatAsync` missing `InputPeerUser` branch (`:1599`) | Medium | Medium |
| 21 | Stuck-download watchdog + 3-minute inactivity CTS | Medium | Medium |
| 22 | Progress/speed/ETA arithmetic; yt-dlp rows report zero bytes | High | Low–Medium — cosmetic but constant |
| 23 | `MediaTracker` name-based correlation (`MediaTracker.cs:88`) and `HeadlessHost.UpsertByName` suffix match (`HeadlessHost.cs:123`) | Medium | Medium — orphaned rows, mis-attributed progress |
| 24 | `HeadlessLogReader` path traversal + tail/search correctness | Low | High if wrong |
| 25 | Plugin assembly loading (duplicate names, `ReflectionTypeLoadException`, read-only dir) | Low | High if wrong — all URL downloads silently stop |
| 26 | `ClearStorageDialog` deletion scope | Low | Critical if wrong — irreversible user data loss |
| 27 | `DiskSpaceService` timer, generation guard, cancellation | Low | Low |
| 28 | Settings export/import round-trip through the real dialogs | Low | Medium (credential handling) |

### Tier 4 — Documentation-first, then tests

Items #12 (yt-dlp quality), #25 (Quiet Hours), #48 (Dark Mode) and #47 (export credentials) from the verification document should be resolved as **product decisions** before tests are written. Writing a test for Quiet Hours means implementing Quiet Hours; deleting the claim from the README is the cheaper correct answer unless the feature is wanted. Do not add tests that lock in a documented-but-absent feature, and do not leave `SettingsExportImportTests.cs:29` asserting the credential leak once the leak is fixed.

---

## 3. Proposed test strategy

### 3.1 Target pyramid

| Layer | Target | Count (approx.) | Runtime budget | Rationale |
|---|---|---|---|---|
| **L1 — Unit (pure logic)** | 85 % line coverage of helpers and models | ~350 | < 10 s | Already ~65 % there. Cheap, fast, and the right home for boundary conditions: size truncation, regex edge cases, version comparison, speed/ETA formatting, token resolution with an injected clock. |
| **L2 — Integration (real SQLite + real filesystem)** | 75 % of `Headless/Data/*` and the download-to-disk path | ~120 | < 60 s | The queue state machine and `.part` lifecycle are I/O-shaped; in-memory fakes would not catch the bugs that matter. Use a real temp-file SQLite database per test class, not `:memory:`, so `PRAGMA`/WAL/locking behaviour is exercised. |
| **L3 — Faked Telegram transport** | 70 % of `TelegramApp` + `BootstrapScanner` + `DownloadOrchestrator` | ~90 | < 120 s | The single highest-leverage investment. Everything downstream of `Client_OnUpdates` is currently untestable purely because `WTelegram.Client` is a concrete dependency. |
| **L4 — Plugin contract tests** | 100 % of `IBasePlugin` implementations against a shared conformance suite | ~60 | < 60 s | Guarantees every plugin — including ones added later — honours the same cancellation, progress, error and result-shape contract. |
| **L5 — HTTP API tests (`WebApplicationFactory`)** | 90 % of `Program.cs` endpoints | ~110 | < 120 s | Highest coverage target of any layer, because the API *is* the commercial product and each endpoint is small and independently testable. |
| **L6 — End-to-end (mocked Telegram, real server, real DB, real disk)** | ~15 golden scenarios | 15–20 | < 10 min | Nightly. Proves the layers compose. |
| **L7 — UI smoke (FlaUI)** | Keep at smoke level | ~15 | < 5 min | Do not grow this. WPF UI tests are slow and brittle; push logic into testable services instead. |

**Overall target: 70 % line / 60 % branch on `TelegramClient`, `BasePlugin`, `*Plugin`, and `TelegramAutoDownload.Headless`.** Explicitly exclude XAML code-behind and `wwwroot/app.js` from the line-coverage gate — cover those at L5/L7 and via a small Playwright suite instead.

### 3.2 The enabling refactor (prerequisite for L3 and most of L2)

Three seams unlock roughly 60 % of the untested surface. Note these are *recommendations for the implementation phase*; no source was modified during this audit.

1. **Extract an `ITelegramTransport` interface** over the ~12 `WTelegram.Client` calls `TelegramApp` actually makes (`Messages_GetHistory`, `Messages_GetDialogs`, `Channels_GetMessages`, `DownloadFileAsync`, `Messages_SendReaction`, `Account_UpdateNotifySettings`, `Channels_GetParticipants`, `Messages_GetFullChat`, `Channels_GetFullChannel`, `Messages_GetAvailableReactions`, `ConnectAsync`, `LoginUserIfNeeded`). `TelegramApp` already takes `(appId, apiHash)` and constructs the client at `:106`; accept an injected transport instead. This single change makes sync pagination, catch-up, reactions, mute, and the whole live-update pipeline testable.
2. **Inject a clock.** `DateTime.Now` in `FolderTemplateHelper.cs:42` and `DateTime.UtcNow` throughout `DownloadProgressService` make date-token and watchdog behaviour untestable. `Resolve` already accepts an `at` override — thread it through `ResolveDownloadDirectory` and pass the message timestamp (which also fixes verification gap #14).
3. **Make `FileDownloadIndex` and `StatisticsService` injectable.** Both are `static`/`Lazy` singletons with process-wide state rooted at a fixed `%APPDATA%` path (`FileDownloadIndex.cs:23`). Tests currently cannot isolate them, which is why `FileDownloadIndexConcurrencyTests` can only assert in-memory behaviour and never touches the JSON file.

### 3.3 Layer detail

**L1 — Unit.** Extend the existing suite rather than replacing it. Highest-value additions: size-threshold boundaries (0/1/1.99/2.0/2.01 MB against threshold 2); regex enforcement with `null` filename, invalid pattern, and mixed-case input; `AutoUpdateService.IsNewer` across `2.9.3` vs `2.10.0` vs `v2.9.3` vs `2.9.3-beta` (string fallback at `:251` is wrong for any non-`Version`-parsable tag); `FormatSpeed`/`FormatEta` boundaries incl. infinity and NaN; `FolderTemplateHelper.Resolve` with an injected `at`.

**L2 — Integration, real SQLite.** One temp database per test class, migrations applied through `Database.InitializeAsync`. Cover: every state transition including illegal ones; `PickQueuedAsync` atomicity under 8 concurrent callers (assert no row is claimed twice); `InsertPendingAsync` idempotency on the `(chat_id, message_id)` key; `PromotePendingToQueuedAsync` scoping; `RequeueInProgressAsync` after a simulated crash; `LegacyDedupMigration` run twice; migration forward-compatibility from an older schema file. Also cover the real-filesystem half: `.part` create/resume/promote, `PartFileCleanup` against a tree with locked files, `ExistingDownloadValidator` across all three `FolderLayoutMode` values.

**L3 — Faked Telegram transport.** A `FakeTelegramTransport` that serves scripted `Messages_MessagesBase` pages and records outbound calls. Scenarios: sync paginates a 2,500-message history with service-message-heavy pages and downloads exactly the matching set; `UpdateChannelTooLong` catch-up stops at the watermark and does not re-download; concurrent live updates never exceed `DownloadThreads`; `UpdateConfig` mid-download does not throw `SemaphoreFullException`; `FILE_REFERENCE_EXPIRED` triggers exactly one refresh-and-retry; reactions target the correct peer when an update batch contains three chats; `BootstrapScanner` resumes from a partial run without duplicating rows.

**L4 — Plugin contract tests.** A single abstract `PluginContractTests<TPlugin>` suite, run against all four plugins, asserting: `CanHandle` is pure and never throws on adversarial input (empty, whitespace, 10 KB string, `null`-ish, non-UTF8); `ExecuteAsync` honours a pre-cancelled token within 5 s and returns `ErrorMessage` matching the cancel convention; `OnProgress` is monotonic and terminal percent ≤ 100; a failure result always carries a non-empty `ErrorMessage` (currently violated by `SocialMediaPlugin.cs:204-211`, which returns `IsSuccess = true` with no file); the cancellation key is always removed on every exit path; output stays within the configured download root (path-traversal guard). Plus per-plugin specifics: `DownladerPlugin` retry-after-partial-chunk must produce a byte-correct file, and a `200`-instead-of-`206` response must fail rather than corrupt.

**L5 — HTTP API tests.** `WebApplicationFactory<Program>` with a temp `DATA_DIR`, a fake transport, and `WEB_AUTH_ENABLED=true`. Per endpoint: happy path, validation failure → 400 with a useful body, unauthenticated → 401, not-found → 404. Cross-cutting: every non-public route rejects a missing/expired/tampered cookie; `IsPublicPath` matches the actual static-file pipeline; concurrent `PATCH` to the same chat does not lose writes (`HeadlessHost.cs:209` takes a lock — prove it); `/api/logs/tail?file=../../secrets` is rejected; `/api/settings/threads` clamps out-of-range input; oversized and malformed JSON bodies return 400. Add a contract snapshot test over the JSON shapes so the web UI cannot be silently broken.

**L6 — End-to-end.** Real Kestrel host, real SQLite, real temp download directory, fake Telegram. Fifteen scenarios: cold start → login → refresh chats → monitor → live message → file on disk → DB `done`; bootstrap 1,000 messages → queue drains → dedup on re-run; pause mid-queue → restart process → resume from persisted state; FLOOD_WAIT mid-bootstrap → pause → resume; filter/min-size/type toggles each produce `skipped` with the right reason; retry-all-failed re-queues and succeeds on the second attempt.

**L7 — UI smoke.** Keep the 7 FlaUI tests; add only close-dialog branches and tray restore. Add a small Playwright suite (~10 tests) for the web UI: login gate, forced password change, chat toggle persists, queue tab renders, log stream connects.

### 3.4 Deterministic failure-injection scenarios

These are the scenarios that separate a proof of concept from a product. All must be deterministic — no `Thread.Sleep`-and-hope.

| # | Scenario | Injection mechanism | Assertion |
|---|---|---|---|
| F1 | **Network loss mid-download** | Fake transport throws `IOException` after N bytes; a second variant hangs (never calls the progress callback) to exercise the 3-minute inactivity CTS with an injected clock | `.part` is **kept**; retry resumes from the existing offset; final file hash matches a single-shot download; DB row ends `failed` (not `done`); no duplicate row created |
| F2 | **Disk full** | Write into a size-capped volume (Windows VHD / Linux loopback), or a `Stream` wrapper throwing `IOException(0x70 ERROR_DISK_FULL)` after a quota | No partial file is promoted to the final name; dedup index is **not** marked; error surfaces to the UI/API with a disk-full-specific message; the app does not crash. **This scenario currently has no code path at all** — nothing checks free space before starting a download despite `DiskSpaceService` existing. |
| F3 | **Permission denied** | Read-only download directory; ACL-denied single file; locked destination held by another handle | `Directory.CreateDirectory` failure (`BaseMessage.cs:270`) is reported, not swallowed; `PartFileCleanup` skips and continues (`PartFileCleanup.cs:46` already does — prove it); config save failure is surfaced rather than silently lost |
| F4 | **Corrupt database** | Truncate the SQLite file mid-header; flip bytes in a page; leave a stale `-wal`; open with a mismatched schema version | Startup reports a clear error instead of throwing on the first request; migration detects and refuses rather than half-applying; a recovery path exists (quarantine + rebuild from `downloaded_ids.json`) |
| F5 | **Corrupt `config.txt`** | Truncated JSON, trailing garbage, empty file, valid JSON with wrong types | App starts with defaults and warns — this is the fix for verification defect #50 and the test must be written against the *fixed* behaviour, replacing `ConfigFilePersistenceTests.cs:193` |
| F6 | **Corrupt `downloaded_ids.json` / `stats.json`** | Truncated array, non-numeric elements | Index resets empty and rebuilds (`FileDownloadIndex.cs:105` claims this — prove it); stats reset to zero with a warning (`StatisticsService.cs:75`) |
| F7 | **Process killed mid-write** | Child process writing the target file, killed by the parent at a deterministic offset | `config.txt`, `stats.json` and `downloaded_ids.json` are each either fully old or fully new, never partial. `FileDownloadIndex` and `StatisticsService` already do temp-then-rename; `ConfigFile.Save` (`:24`) does **not** — this test should fail today |
| F8 | **Telegram FLOOD_WAIT** | Fake transport returns `FLOOD_WAIT_X` at a scripted call index | Parsed by `FloodWaitHelper.TryParseSeconds`; global pause engages; the row is re-queued not failed (`DownloadOrchestrator.cs:170`); bootstrap resumes at the same offset |
| F9 | **`FILE_REFERENCE_EXPIRED`** | Fake transport fails once, then succeeds on the refetched message | Exactly one refresh (`FactoryMessagesService.cs:95`); no infinite loop; success on the second attempt |
| F10 | **Session file locked** | Hold an exclusive handle on `session.dat` | Startup retry logic engages (CHANGELOG 2.8.4 claims 40 s); clear user-facing message; single-instance mutex is released correctly |
| F11 | **Telegram reconnect during transfer** | Force `Disconnected` while `DownloadActivity.ActiveCount > 0` | Reconnect is deferred (`TelegramApp.cs:1759`); the transfer is not aborted — this is the CHANGELOG 2.8.6 regression and it has no test |
| F12 | **Clock skew / DST** | Injected clock crossing a DST boundary and a year boundary | Folder date tokens and any future scheduling logic behave; `.part` age comparison (`PartFileCleanup.cs:40`, UTC) stays correct |
| F13 | **Malformed plugin DLL** | Drop a text file named `.dll`, a native DLL, and a duplicate assembly into `Plugins/` | `MessageTextFactoryService:50-85` skips them; the remaining plugins still load; no startup crash |
| F14 | **Hostile chat/file names** | Names containing `../`, `~`, reserved Windows names (`CON`, `PRN`), >255 chars, RTL override, emoji, null bytes | Output stays inside the download root; no `PathTooLongException`; sanitisation in `FolderTemplateHelper.SanitizeChatName:109` and `BaseMessage.PathLocationFolder:253` holds |
| F15 | **Duplicate message delivery** | Fake transport delivers the same `UpdateNewMessage` twice concurrently | Exactly one file written; exactly one DB row; dedup index consistent |

### 3.5 Load and soak targets

| Dimension | Target | Measured how | Why |
|---|---|---|---|
| Chat list scale | 5,000 dialogs across both folders; refresh < 30 s; search keystroke → repaint < 250 ms | Fake transport returning 50 pages of 100; measure `GetAllChats` (`TelegramApp.cs:1567`) and `ApplySearch` (`MainWindow.xaml.cs:581`) | `FetchDialogFolder` caps at 50 pages per folder (`:1626`) — verify the cap is hit gracefully, not silently truncating |
| Bootstrap scale | 100,000-message history; complete without unbounded memory growth; resumable from any interruption | Scripted transport + real SQLite | `BootstrapScanner` streams pages, but `ExportChatHistoryAsync` accumulates **all** entries in a `List<HistoryEntry>` before writing (`TelegramApp.cs:1130, 1169`) — that one is O(n) memory and will OOM on a large channel. Load-test it specifically. |
| Queue scale | 250,000 rows in `media`; `PickQueuedAsync` p99 < 50 ms; `GetPerChatStatusCountsAsync` p99 < 500 ms | Seeded DB + BenchmarkDotNet or a timing harness | `/api/queue/stats/by-chat` (`Program.cs:375`) does a full group-by on every dashboard poll |
| Download concurrency | 10 parallel downloads sustained 1 h; no semaphore leak; `CancellationRegistry.Count` returns to 0 | Fake transport with throttled streams | Registry leaks would be invisible today |
| Soak | 72 h continuous, 1 message/10 s, periodic config changes and bootstraps | Container + memory/handle sampling | Targets: RSS growth < 5 % after hour 1; file handles stable; no unbounded growth in `HeadlessHost._downloads` (trimmed only inside `UpsertByName` at `:140` — if progress events stop, completed rows are never trimmed), `MediaTracker._chatIdByName`/`_lastMsgIdByChatFile` (never bounded), or `Notification._progressMessages`/`_lastReportedPct` (removed only on the completion path) |
| Web dashboard | 10 concurrent sessions polling every 2 s + 3 SSE log streams for 1 h | k6 or Playwright | SSE holds a request thread per client with a 1 s poll loop (`Program.cs:341-354`) |
| Startup | Cold start to serving < 10 s with a 250 k-row DB | Timed container start | Migrations + `LegacyDedupMigration` run synchronously before the first request (`Program.cs:107-108`) |

### 3.6 Suggested sequencing

| Phase | Work | Outcome |
|---|---|---|
| **1** | The three seams in §3.2 (transport interface, clock, injectable singletons) | Unlocks everything else. No new tests yet. |
| **2** | L5 HTTP API tests + L2 SQLite integration (gaps #1, #2) | The commercial surface stops being untested. Fastest risk reduction per hour spent. |
| **3** | L3 fake-transport tests for `Client_OnUpdates`, sync, catch-up (gaps #4, #6) | Covers the 1,602-line orchestrator. |
| **4** | Failure injection F1–F7 (gaps #3, #5, #7) | Covers the silent-corruption class of bugs. |
| **5** | L4 plugin contract suite (gap #15) | Makes future plugins safe by construction. |
| **6** | Load and soak baselines; wire coverage gates into CI | Establishes regression detection. |
| **7** | Reconcile documentation with reality; delete or re-point the tests that currently assert broken behaviour (`ConfigFilePersistenceTests.cs:193`, `SettingsExportImportTests.cs:29`, `ConfigFilePersistenceTests.cs:136`) | Tests stop protecting the bugs. |

### 3.7 CI gates to adopt

- Coverage floor of 70 % line / 60 % branch on `TelegramClient`, `BasePlugin`, the four plugin projects, and `TelegramAutoDownload.Headless`; ratchet upward, never down. Exclude XAML code-behind.
- L1+L2+L4 run on every commit (target < 3 min). L3+L5 on every PR (< 5 min). L6 + soak nightly.
- Fail the build on any new `catch { }` that swallows without logging — the codebase has many (`TelegramApp.cs:147, 376, 379, 1532, 1556`; `FileDownloadIndex.cs:105, 120`; `Photos.cs` and others), and each one is a place a failure-injection test cannot observe anything.
- Mutation testing (Stryker.NET) on `TelegramClient` and `Headless/Data` once coverage passes 60 %, to confirm the new tests actually assert rather than merely execute.

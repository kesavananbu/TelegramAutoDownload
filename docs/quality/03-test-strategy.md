# 03 — Test Strategy

**Companion to:** `docs/quality/03-feature-verification.md`
**Inputs:** `TelegramAutoDownload.Tests` (**28** files, **~296** `[Fact]`/`[Theory]` methods per static count), `TelegramAutoDownload.UITests` (**7** `[UIFact]` smoke tests in `MainWindowTests.cs`).
**Method:** static reading only (Composer 2.5 re-run, 2026-10-01). No test execution — coverage is *existence and intent*, not pass/fail.

---

## 1. Coverage mapping — feature → tests

Legend: **Direct** = production path exercised. **Indirect** = helper only. **UNTESTED** = no meaningful test.

### 1.1 Download core

| Feature | Verdict | Coverage | Notes |
|---|---|---|---|
| Live update → download orchestration | WORKS | **UNTESTED** | `Client_OnUpdates` `TelegramApp.cs:257-501` never instantiated in tests |
| Message-type routing | WORKS | **Direct** | `MessageTypeRoutingTests.cs`, `DocumentMediaKindHelperTests.cs` |
| `.part` resume | PARTIAL | **Indirect** | Stream contract in `IntegrationTests.cs:178+`; not `DownloadDocumentAsync` end-to-end |
| Dedup index | PARTIAL | **Direct (memory)** | `FileDownloadIndexConcurrencyTests.cs`; not flush window / headless shutdown |
| Parallel `DownloadThreads` | PARTIAL | **UNTESTED** | Semaphore replace on `UpdateConfig` `:176` |
| Retry UI / plugin retry | PARTIAL | **Model only** | `NewFeaturesTests.cs`; not 4 s row removal or missing `FileName` |
| Cancel / cancel-all | WORKS | **Direct** | `CancellationRegistryTests.cs`, `CancelAllTests.cs` |
| Watchdog / timeout | WORKS | **UNTESTED** | `DownloadProgressService.cs:56-88`, inactivity CTS |
| Auto-clean semantics | PARTIAL | **UNTESTED** | `ShouldAutoRemoveCompletedItem` `:311` |
| Progress / speed / ETA | PARTIAL | **UNTESTED** | yt-dlp zero-byte progress |

### 1.2 Per-chat, sync, stats

| Feature | Verdict | Coverage |
|---|---|---|
| Provider chain | WORKS/PARTIAL | **Direct** — `IntegrationTests.cs`, `PluginCanHandleTests.cs` |
| yt-dlp quality | BROKEN | Test **locks broken behaviour** — `ConfigFilePersistenceTests.cs:136` |
| Regex filter | PARTIAL | Parsing/dialog tests; enforcement edges thin |
| Folder template | PARTIAL | **Strong** unit tests; message-date vs download-date untested |
| Reactions / mute | PARTIAL | **UNTESTED** |
| History sync pagination | WORKS | **Direct** arithmetic — not full `SyncHistoryAsync` |
| Catch-up | PARTIAL | Watermark tests only |
| JSONL history | WORKS | **Direct** — `ChatHistoryTests.cs` |
| Session/all-time stats | WORKS/PARTIAL | JSON layer tested; service singleton untested |
| Auto-update | PARTIAL | **UNTESTED** — `AutoUpdateService.cs` |
| Config corrupt read | BROKEN | Test asserts throw — `ConfigFilePersistenceTests.cs:193` |
| Export credentials | BROKEN | Test asserts leak — `SettingsExportImportTests.cs:29` |

### 1.3 Plugins

| Plugin | Coverage |
|---|---|
| `DownladerPlugin` | **Direct** mocked HTTP (6 tests); misses chunk-retry corruption |
| Torrent | Partial unit tests; no swarm E2E |
| Social / YouTube | **`CanHandle` only** — `ExecuteAsync` untested |
| Reflection load | **UNTESTED** |

### 1.4 Headless (commercial surface)

| Component | Lines (approx.) | Tests |
|---|---|---|
| `Program.cs` (42 routes) | 641 | **0** |
| `MediaRepository`, `Database`, migrations | ~400 | **0** |
| `WebAuthService`, middleware ordering | ~230 | **0** |
| `DownloadOrchestrator`, `BootstrapScanner`, scanners | ~650 | **0** |
| `HeadlessHost`, `ChatDownloadTester` | ~520 | **0** |
| `wwwroot/app.js` | 1158 | **0** |

> **Headless has zero automated tests.** `TelegramAutoDownload.Tests` does not reference the headless project.

### 1.5 Shape of the suite

Concentration on pure helpers (~600 LOC well covered). Near-zero on `TelegramApp` (1799 LOC), thin on WPF services, `ExecuteAsync` for yt-dlp plugins, and entire headless stack. No fake Telegram transport, no `WebApplicationFactory`, no failure-injection.

---

## 2. Top 10 untested risks

Ranked by **likelihood × impact**, aligned with defects in `03-feature-verification.md`.

| # | Untested behaviour | Why it ranks |
|---|---|---|
| 1 | **Headless HTTP API** — all 42 routes in `Program.cs` | Control plane for commercial deployment; auth, validation, and side effects unasserted |
| 2 | **SQLite queue state machine** — `MediaRepository.PickQueuedAsync`, status transitions, unique `(chat_id, message_id)` | Duplicate downloads or stuck rows if claim is not atomic |
| 3 | **`.part` promote without size validation** — `BaseMessage.cs:145` | Silent truncated files marked complete and deduped |
| 4 | **`Client_OnUpdates` orchestration** — semaphore capture across `UpdateConfig`, reactions, retry wiring | 240+ lines on every message; known `SemaphoreFullException` class |
| 5 | **Concurrent `UpdateConfig` / semaphore replacement** — `TelegramApp.cs:176` | Fires on every checkbox and headless PATCH; over-subscription |
| 6 | **Corrupt / partial `config.txt`** — `ConfigFile.Read` throws; `Save` non-atomic | Bricks app on bad byte; test currently asserts throw |
| 7 | **`DownladerPlugin` chunk retry** — append without seek reset | Corrupt HTTP downloads with success result |
| 8 | **`WebAuthService`** — cookie signing, default password, login brute force | Auth bypass exposes full Telegram session |
| 9 | **Dedup durability** — 10 s debounce, no headless `Flush`, crash window | Mass re-download after kill |
| 10 | **Bootstrap `PromotePendingToQueuedAsync`** — promotes all pending including live-path rows (`BootstrapScanner.cs:132`) | Duplicate work, FLOOD_WAIT, API budget |

**Runners-up:** yt-dlp `ExecuteAsync` parsing; FLOOD_WAIT/`TokenBucketRateLimiter`; `AutoUpdateService.IsNewer`; regex null-filename / ReDoS; `MediaTracker` name correlation; plugin DLL load failures.

---

## 3. Proposed test strategy

### 3.1 Target pyramid

| Layer | Target | Rationale |
|---|---|---|
| **L1 Unit** | 85% line on helpers/models | Extend existing ~296 tests with boundaries (size MB, regex, version compare) |
| **L2 Integration** | Real SQLite + filesystem temp dirs | Queue transitions, migrations, `.part` lifecycle |
| **L3 Faked Telegram** | `ITelegramTransport` over WTelegram calls | Unlocks `TelegramApp`, sync, catch-up, reactions |
| **L4 Plugin contract** | Shared suite per `IBasePlugin` | Cancellation, progress monotonicity, path confinement |
| **L5 HTTP API** | 90% of `Program.cs` via `WebApplicationFactory` | Highest ROI for commercial surface |
| **L6 E2E** | ~15 golden paths nightly | Compose server + DB + fake transport |
| **L7 UI smoke** | 7 FlaUI + ~10 Playwright for web | Do not expand FlaUI; cover login, PATCH, SSE |

**Overall gate:** 70% line / 60% branch on `TelegramClient`, `BasePlugin`, plugins, `TelegramAutoDownload.Headless` (exclude XAML; cover `app.js` via Playwright).

### 3.2 Prerequisites (seams)

1. **`ITelegramTransport`** — inject into `TelegramApp` instead of concrete `Client`.
2. **Clock injection** — folder tokens, watchdogs, quiet-hours (if built).
3. **Injectable `FileDownloadIndex` / paths** — tests must not use `%APPDATA%` singleton.

### 3.3 Failure-injection (deterministic)

| Scenario | Assertion |
|---|---|
| Network loss mid-download | `.part` kept; retry resumes; hash matches |
| Disk full | No promote; no dedup mark; surfaced error |
| Permission denied | Reported, not swallowed |
| Corrupt SQLite / config | Clear error or safe defaults (fix #50 before locking test) |
| Process killed mid-write | Atomic writes for config (fix `ConfigFile.Save`) |
| FLOOD_WAIT | Parsed, global pause, row re-queued |
| Duplicate `UpdateNewMessage` | One file, one DB row |
| Hostile filenames | Stay under download root |

### 3.4 Load and soak (define then measure)

| Target | Intent |
|---|---|
| 5k dialogs, refresh &lt; 30 s | Dialog pagination caps |
| 100k-message bootstrap, bounded RSS | OOM risk in full-history export list |
| 250k queue rows, `PickQueuedAsync` p99 &lt; 50 ms | Dashboard poll cost |
| 10 parallel downloads × 1 h | No semaphore/registry leak |
| 72 h soak, 1 msg/10 s | No unbounded in-memory maps (`HeadlessHost._downloads`, notification caches) |
| 10 web clients + SSE × 1 h | SSE thread/poll loop `Program.cs:341+` |

*No load/soak measurements were run in this re-run (read-only mandate).*

### 3.5 Sequencing

1. Seams (§3.2)
2. L5 API + L2 SQLite (risks #1, #2)
3. L3 transport tests (#4, #5)
4. Failure injection F1–F7 (#3, #6, #7)
5. L4 plugin contracts
6. CI coverage gates + nightly L6/soak
7. **Reconcile docs/tests** — remove tests that assert BROKEN behaviour (`ConfigFilePersistenceTests.cs:193`, `SettingsExportImportTests.cs:29`, quality override test) after fixes

### 3.6 Documentation-first items

Do **not** add Quiet Hours or Dark Mode tests until product decides build vs delete README claims (#25, #48). Do not expand yt-dlp quality tests until setting is restored (#12).

### 3.7 CI gates (target)

- Per-push: L1+L2+L4 (&lt; 3 min)
- Per-PR: + L3+L5 (&lt; 5 min)
- Nightly: L6 + soak baselines
- Fail on new empty `catch` swallowing errors in download/auth paths
- Mutation testing (Stryker.NET) once headless line coverage ≥ 60%

---

## 4. Top 10 web gaps (test + product)

For operator parity with WPF, the web UI/API lacks (see §2.1 in feature verification):

1. Telegram bot notifications configuration and runtime wiring  
2. Cancel in-flight download (expose `CancellationRegistry`)  
3. Disk space / volume warning API  
4. Session + all-time download statistics (`stats.json` semantics)  
5. Emoji reaction icon editing (data preserved server-side only)  
6. Mute/unmute chat  
7. History export (JSONL)  
8. Members export (high PII — gate deliberately)  
9. Manual browse / message picker download  
10. Per-plugin folder templates (Social/YouTube/Other/Torrent)

Closing gaps #1–#4 and #10 should precede marketplace positioning; #8 requires compliance review before exposure.

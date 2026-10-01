# 03 — Feature Verification (Function-Vetting)

**Scope:** `TelegramAutoDownload` (WPF) + `TelegramAutoDownload.Headless` (ASP.NET Core) + shared core, branch `enterprise-readiness`.
**Method:** static code reading only (Composer 2.5 re-run, 2026-10-01). No `dotnet build`, no test run, no source modification.
**Baseline claims:** `README.md` (v2.9.3) and `CHANGELOG.md`.

> **Evidence rule:** every verdict cites `path:line` from files actually read. Where behaviour cannot be settled by reading alone, verdict is **UNVERIFIABLE-BY-READING** with a named experiment.

---

## 0. Verdict summary

| Verdict | Count |
|---|---|
| **WORKS** | 19 |
| **PARTIAL** | 18 |
| **BROKEN** | 8 |
| **UNVERIFIABLE-BY-READING** | 5 |
| **Total claims audited** | 50 |

### The eight BROKEN claims at a glance

| # | README/CHANGELOG claim | Reality |
|---|---|---|
| 1 | Quiet Hours (scheduling window) | **No implementation in any `.cs`, `.xaml`, `.js`, or `.html`.** |
| 2 | Per-chat yt-dlp quality (Best/4K/1080p/720p/480p/Audio) | Overwritten to highest video on every config read/import. |
| 3 | Dark Mode setting | Light theme hard-coded; no persisted theme. |
| 4 | `QuietHoursHelper` in README architecture diagram | Class does not exist. |
| 5 | README: test suite covers Quiet Hours | No test references `quiet` in `TelegramAutoDownload.Tests` (repo-wide `.cs` grep). |
| 6 | Settings export excludes credentials | Export includes `ApiHash` and `BotToken`; test asserts hash presence. |
| 7 | "Active downloads are pinned to the top" | Sort is `EnqueuedAt` descending only; `SortOrder` unused. |
| 8 | CHANGELOG: `ConfigFile.Read()` returns defaults on corrupt JSON | Malformed JSON throws; test asserts throw. |

---

## 1. Feature verification table

### 1.1 Download

| # | Claim (README) | Verdict | Evidence + reasoning |
|---|---|---|---|
| 1 | Automatic media download from monitored chats | **WORKS** | `TelegramApp.Client_OnUpdates` `:257-501`; `UpdateNewMessage` at `:289`; factory dispatch `FactoryMessagesService.cs` handlers for video/photo/music/file. |
| 2 | Resume via `.part` files | **PARTIAL** | `.part` + seek + promote `BaseMessage.cs:118-215`, `:145`. **Gaps:** no size check before `File.Move` (`:145`); `WithRetryAsync` invoked at `:127` without cancel token so watchdog stream dispose may retry instead of abort (`:238-246`). |
| 3 | Deduplication by Telegram document ID | **PARTIAL** | `FileDownloadIndex.cs` mark/check; debounced save (~10 s). **Gap:** headless host never calls `FileDownloadIndex.Flush()` on shutdown (`TelegramAutoDownload.Headless/Program.cs` — only HTTP `FlushAsync` at `:351`). |
| 4 | Minimum file size per chat | **PARTIAL** | `BaseMessage.cs:276-289` uses integer MB floor; native `MessageMediaPhoto` bypasses document size gate (`:309` path). |
| 5 | Parallel downloads 1–10, default 3 | **PARTIAL** | `TelegramApp.UpdateConfig` `:176` replaces `SemaphoreSlim`; in-flight work holds old instance (`:343+`). Triggered on every WPF edit and headless `PATCH /api/chats/{id}`. |
| 6 | Retry button (↺) for failed/timeout | **PARTIAL** | Wired `TelegramApp.cs:451` → `DownloadProgressService.SetRetryAction` `:334`. **Gaps:** error rows auto-remove in 4 s (`ShouldAutoRemoveCompletedItem` `:311`); plugin failures often lack `FileName` so no retry action (`TelegramApp.cs:446`). |
| 7 | Auto clean — keep history when unchecked | **PARTIAL** | Toggle gates `"✔ Done"` only (`DownloadProgressService.cs:311`); failures always removed. |
| 8 | Open from UI (📂) | **WORKS** | `MainWindow.xaml.cs:761-782`, `DownloadItem.CanOpenFile`. |
| 9 | Cancel while queued/downloading | **WORKS** | `CancellationRegistry`, `DownloadProgressService.cs:349-378`, cancel-all `:407`. |
| 10 | Stuck-download watchdog / Timeout status | **WORKS** | 3-minute inactivity CTS `BaseMessage.cs:68+`; 30 s sweep `DownloadProgressService.cs:56-88`. |

### 1.2 Per-chat settings

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| 11 | Per-chat provider toggles | **PARTIAL** | `MessageTextFactoryService.cs:145`; fourth key is `"Other"` (HTTP), not native Telegram attachments (`DownladerPlugin.cs:22`). |
| 12 | Per-chat yt-dlp quality | **BROKEN** | `ConfigParams.NormalizeYtDlpQualityForAllChats` `TelegramClient/Models/ConfigParams.cs:62-66` on every `ConfigFile.Read` `:31`; WPF hard-codes on load `MainWindow.xaml.cs:245,302,697`; test `ConfigFilePersistenceTests.cs:136`. |
| 13 | Regex filters on file name | **PARTIAL** | `BaseMessage.cs:291-303` — not case-insensitive vs README; null `Document.Filename` risk; invalid regex throws outside handler try. Headless accepts raw filter on `PATCH` `Program.cs:261-277`. |
| 14 | Folder template tokens | **PARTIAL** | `FolderTemplateHelper.cs:39-64` — `{Year}/{Month}/{Day}` default to download time (`DateTime.Now` at `:42`), not message date per README. |
| 15 | Emoji reaction on download start | **PARTIAL** | `TelegramApp.cs:394-398`; peer from `updates.Chats.First()` wrong-peer risk `:1443+`. |
| 16 | Emoji reaction on complete | **PARTIAL** | `:420`; Sync path skips reactions `:639-658`. |
| 17 | History Icon on JSONL record | **WORKS** | `:365-378`. |
| 18 | Mute toggle via API | **PARTIAL** | `MuteChatAsync` `:1594-1612` — missing `InputPeerUser` vs `ResolveInputPeerAsync` `:731-740`. |
| 19 | Download Types V/P/M/F | **WORKS** | `IsMessageTypeEnabled` `:1223-1251`. |

### 1.3 Sync & history

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| 20 | History sync (full chat scan) | **WORKS** | `SyncHistoryAsync` `:507-701`; pagination tests `IntegrationTests.cs:553,582,600`. |
| 21 | `UpdateChannelTooLong` catch-up | **PARTIAL** | `:278-285`, sweep `:1366-1433` — URL/magnet/text gaps; type toggles not applied at queue time `:1395-1397`. |
| 22 | Sticker / voice skipped | **WORKS** | `TelegramApp.cs:1231-1233`; tests `IntegrationTests.cs:408+`. |
| 23 | History JSONL + export | **WORKS** | Append `:365-371`; export `:1089-1182`; path matches README (not erroneous status string at `:1175`). |
| 24 | Members column | **WORKS** | Dialog enumeration `TelegramApp.cs:1684-1685`. |

### 1.4 Scheduling

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| 25 | Quiet Hours | **BROKEN** | Zero matches for `quiet` in production UI/code (`.cs`/`.xaml`/`.js`/`.html`). No fields on `ConfigParams` `TelegramClient/Models/ConfigParams.cs:9-67`. README `:33,262,313,330`. |

### 1.5 Statistics & monitoring

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| 26 | Session stats | **WORKS** | `DownloadProgressService.cs:277-279`. |
| 27 | All-time `stats.json` | **PARTIAL** | `StatisticsService.cs` debounced save; `Reset()` race on `_data` swap. |
| 28 | Disk space bar | **WORKS** | `DiskSpaceService.cs`, UI thresholds `MainWindow.xaml.cs:543-555`. |
| 29 | Progress, speed, ETA | **PARTIAL** | Cumulative average speed `:237`; yt-dlp plugins report zero bytes (`SocialMediaPlugin.cs:137`, `YouTubePlugin.cs:95`). |
| 30 | Total speed badge | **PARTIAL** | Same averaging; yt-dlp contributes 0. |
| 31 | Active downloads pinned to top | **BROKEN** | Sort `EnqueuedAt` only `MainWindow.xaml.cs:125`; `DownloadItem.SortOrder` `:144-149` unused. CHANGELOG 2.9.3 contradicts README. |
| 32 | Completed/error rows auto-remove 4 s | **PARTIAL** | `:293` + `:311` asymmetry vs README Auto-clean wording. |
| 33 | Connection dot | **WORKS** | Reconnect deferral during transfers `:1759-1764`. |

### 1.6 Notifications, tray, updates

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| 34 | Telegram bot notifications | **WORKS** | `Notification.cs`; HTML escape `:241`. |
| 35 | Tray icon / close to tray | **WORKS** | `App.xaml.cs:99-118`, `MainWindow.xaml.cs:1449-1476`. |
| 36 | Auto-update on startup | **WORKS** | `MainWindow.xaml.cs:211-216`, `:1418-1446`. |
| 37 | Manual version check | **WORKS** | `:1336-1416`. |
| 38 | Smart update (portable vs installer) | **PARTIAL** | `AutoUpdateService.cs`; `taskkill /im TelegramAutoDownload.exe` `:173` kills all instances by image name. |
| 39 | Single-instance guard | **WORKS** | Mutex `App.xaml.cs:30-41`. |

### 1.7 Plugin system

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| 40 | Priority chain, first success wins | **WORKS** | `MessageTextFactoryService.cs:112-158`; test `IntegrationTests.cs:93`. |
| 41 | README plugin order Social→YouTube→Torrent→Direct | **PARTIAL** | Actual priorities: Social 2, Torrent 3, YouTube 10, Other 100 — Torrent before YouTube. |
| 42 | SocialMedia "any http(s) URL" | **PARTIAL** | Domain allowlist `SocialMediaPlugin.cs:16-59`. |
| 43 | "Direct" = native Telegram plugin | **PARTIAL** | Native path is factory, not plugin; web labels "Direct" but key is `"Other"`. |
| 44 | Torrent (magnets + `.torrent`) | **PARTIAL** | `Files.cs:49` removes dedup entry on re-post; >5 MB heuristic for "already downloaded". |
| 45 | Generic HTTP ("Other") | **PARTIAL** | Chunk retry without seek reset `DownladerPlugin.cs:82-91`; no `.part` resume. |
| 46 | yt-dlp auto download/update | **UNVERIFIABLE-BY-READING** | `YtdlpService.EnsureAsync` — needs runtime hash/version experiment. |

### 1.8 Settings, config, data files

| # | Claim | Verdict | Evidence |
|---|---|---|---|
| 47 | Export excludes credentials | **BROKEN** | `SettingsWindow.xaml.cs:176-186`; test `SettingsExportImportTests.cs:29`. |
| 48 | Dark Mode | **BROKEN** | `App.xaml.cs:89-92` light-only. |
| 49 | Data files layout (`config.txt`, etc.) | **WORKS** | `AppPaths.cs`, `FileDownloadIndex`, session path `TelegramApp.cs:164`. |
| 50 | CHANGELOG: corrupt `ConfigFile.Read()` → defaults | **BROKEN** | `ConfigFile.cs:27-32`; test `ConfigFilePersistenceTests.cs:193`; non-atomic `Save` `:24`. |

### 1.9 Runtime-only claims (the five **UNVERIFIABLE-BY-READING** verdicts)

These are not separate numbered README claims; they explain why five rows above cannot be closed by static review alone (including row **#46**).

| ID | Topic | Deciding experiment |
|---|---|---|
| U1 | WTelegram `.part` resume offset | Fake transport; compare hash after kill/restart. |
| U2 | Reaction emoji accepted by Telegram | Test account with restricted reaction set. |
| U3 | Torrent swarm completes on real network | Local seeded torrent. |
| U4 | yt-dlp output resolution vs selector | Golden-file with pinned yt-dlp (quality UI BROKEN today). |
| U5 | WPF tray / retry button lifetime | FlaUI after induced failure. |

---

## 2. Desktop (WPF) vs Headless (Web) — parity

**Headless surface:** `Program.cs` (641 lines, **42** HTTP routes), `wwwroot/app.js` (1158 lines). `/api/status` (`Program.cs:519-533`) exposes login/queue metrics but **not** application version.

### 2.1 Top desktop features missing from web (product gaps)

| Rank | Feature | WPF / engine | Headless gap |
|---|---|---|---|
| 1 | Telegram bot notifications | `Notification.cs`, `SettingsWindow.xaml.cs` | Not wired; no bot fields on `/api/settings` |
| 2 | Cancel in-flight download | `CancellationRegistry`, WPF cancel | Queue retry only; no cancel API |
| 3 | Disk space monitoring | `DiskSpaceService` | No endpoint; container fill is silent |
| 4 | Session + all-time stats (`stats.json`) | `StatisticsService` | `/api/queue/stats` is DB counts, not bytes/files lifetime |
| 5 | Emoji reactions (start/end/history) | `TelegramApp.cs`, preserved on refresh `HeadlessHost.cs:170-171` | `ChatPatch` / `ChatDtoView` omit icons `Program.cs:607-640` |
| 6 | Mute / unmute chat | `MuteChatAsync` | `Muted` read-only in view; no PATCH |
| 7 | Chat history export JSONL | `ExportChatHistoryAsync` | No route |
| 8 | Members export CSV/vCard | `GetChannelMembersAsync` | No route (PII-sensitive) |
| 9 | Manual browse + selective download | `ChatBrowseWindow`, `ManualDownloadMessagesAsync` | Only `test-download` newest-10 probe |
| 10 | Per-plugin folder templates | Four templates on `ChatDto` | `ChatPatch` only `FolderTemplate`; extras preserved on refresh `HeadlessHost.cs:183-186` but not editable |

Additional gaps: settings export/import, clear-storage tool, open-file in browser (by design), app version display, per-chat yt-dlp quality (broken on desktop too).

### 2.2 Headless-only (do not regress)

SQLite media queue + migrations, bootstrap scanner, token-bucket + FLOOD_WAIT UI, pause/resume downloads, bulk queue ops, log tail/search/SSE, web auth gate, test-download probe, legacy dedup migration — see prior headless-only table in architecture docs; evidence in `MediaRepository.cs`, `BootstrapScanner.cs`, `WebAuthService.cs`, `Program.cs`.

### 2.3 Headless architecture risks

| Risk | Evidence |
|---|---|
| Every chat PATCH → full `UpdateConfig` + `.part` cleanup sweep | `HeadlessHost.cs:217`, `TelegramApp.UpdateConfig` `:166-185` |
| Bootstrap promotes all `pending` for chat (incl. live-path rows) | `BootstrapScanner.cs:132` `PromotePendingToQueuedAsync` |
| Progress correlated by chat display name / suffix match | `MediaTracker.cs:88-98`, `HeadlessHost.UpsertByName` `:122-123` |
| Static files before auth middleware | `Program.cs:47-49` vs `WebAuthService.IsPublicPath` |

---

## 3. Undocumented behaviour (selected)

| Topic | Evidence |
|---|---|
| Filter regex also **captures** matching text-only messages to `.txt` | `TelegramApp.cs:321-340`, `SaveCapturedTextAsync` `:1321-1364` |
| Member export includes phone numbers | `MembersExportDialog`, `GetChannelMembersAsync` |
| Settings export leaks secrets | BROKEN #47 |
| Filter field on headless PATCH without dialog validation | `Program.cs:276-277` |
| Default compose credentials | `docker-compose.yml`, `WebAuthService.cs` |

---

## 4. How to read these verdicts

- **WORKS:** traced in code; not equivalent to tested (see `03-test-strategy.md`).
- **PARTIAL:** named gap; several are user-visible correctness bugs.
- **BROKEN:** missing feature or contradicts README/CHANGELOG.
- **UNVERIFIABLE-BY-READING:** third-party/network contract; experiment listed.

# 03 — Feature Verification (Function-Vetting)

**Scope:** `TelegramAutoDownload` (WPF) + `TelegramAutoDownload.Headless` (ASP.NET Core) + shared core, branch `enterprise-readiness`.
**Method:** static code reading only. No build, no test run, no source modification.
**Baseline claims:** `README.md` (v2.9.3) and `CHANGELOG.md`.

> **Evidence rule applied throughout:** a feature is a hypothesis until the implementing code is found. Every verdict below cites `path:line`. Where behaviour cannot be settled by reading, the verdict is `UNVERIFIABLE-BY-READING` and the deciding experiment is named.

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
| 1 | Quiet Hours (scheduling window) | **No implementation exists anywhere in the repo.** |
| 2 | Per-chat yt-dlp quality (Best/4K/1080p/720p/480p/Audio) | Forcibly overwritten to `VIDEO` on every config read. |
| 3 | Dark Mode setting | Removed from the product; light theme hard-coded. |
| 4 | `QuietHoursHelper` in the architecture diagram | Class does not exist. |
| 5 | Test suite covers Quiet Hours | No such test. |
| 6 | Settings export excludes credentials | Export includes `ApiHash` **and** `BotToken`. |
| 7 | "Active downloads are pinned to the top" | Sort is newest-first only; `SortOrder` is dead code. |
| 8 | CHANGELOG: `ConfigFile.Read()` returns defaults on corrupt JSON | It throws; a test asserts that it throws. |

---

## 1. Feature verification table

### 1.1 Download

| # | Claim (README) | Verdict | Evidence + reasoning |
|---|---|---|---|
| 1 | Automatic media download (video/photo/music/files) from monitored chats | **WORKS** | Update handler `TelegramClient/TelegramApp.cs:257` dispatches `UpdateNewMessage` at `:289`, gated on the chat being resolved as monitored by `FactoryUserService` (`:273`); execution at `:405` → `TelegramClient/Factory/Service/FactoryMessagesService.cs:74`, type routing at `:242`, handler invocation at `:141`. Handlers exist for all four kinds: `Videos.cs:23`, `Photos.cs:25`, `Music.cs`, `Files.cs:23`. |
| 2 | Resume interrupted downloads via `.part` files | **PARTIAL** | Implemented: `.part` path `TelegramClient/Factory/Base/BaseMessage.cs:199`, resume-seek `:206-215`, atomic promote `File.Move(partPath, finalPath, overwrite:true)` `:145`, partial kept on non-user failure `:170-172`. **Gap 1 — no integrity gate before promote:** `:145` renames the `.part` unconditionally once `WithRetryAsync` returns, with no check that the file length equals `document.size`; a short-but-non-throwing transfer is promoted and then indexed as complete at `:147`. **Gap 2 — dead cancellation guard:** `WithRetryAsync` is called at `:127` without a token, so `ct` defaults to `CancellationToken.None` and the `catch when (ct.IsCancellationRequested)` arm at `:238` can never fire; the `ObjectDisposedException`/`IOException` produced by the watchdog disposing the stream (`:134`) is therefore swallowed by the generic retry arm at `:243` and retried up to 3× instead of aborting. |
| 3 | Deduplication by Telegram document ID | **PARTIAL** | Index at `TelegramClient/FileDownloadIndex.cs:43` (`IsAlreadyDownloaded`) / `:53` (`MarkDownloaded`); call sites `Photos.cs:41`, `Videos.cs:42`, `Files.cs:53`, write-back `BaseMessage.cs:147`. Stale-entry self-healing (index hit but file gone → `Remove` + re-download) is sound: `Photos.cs:44-49`. **Gap — durability window:** writes are debounced to a 10-second timer (`FileDownloadIndex.cs:33-35`) and only made durable by `Flush()` on clean exit (`TelegramAutoDownload/App.xaml.cs:150`). A crash or `kill -9` loses up to 10 s of completions, which re-downloads those files on next start. The headless host never calls `Flush()` at all — no shutdown hook exists in `TelegramAutoDownload.Headless/Program.cs`. |
| 4 | Configurable minimum file size per chat (skip files below N MB) | **PARTIAL** | `TelegramClient/Factory/Base/BaseMessage.cs:278-289`. **Gap 1 — truncating integer division:** `document.size / 1024 / 1024` at `:279` floors the size, so with `DownloadFromSize = 2` a 2.9 MB file reports `2` and is *not* skipped while the user expects a 2 MB floor to behave consistently; the effective rule is `floor(sizeMB) < N`. **Gap 2 — native photos bypass the check entirely:** the guard only runs inside `if (message.media is MessageMediaDocument …)` at `:276`; a `MessageMediaPhoto` falls to the `else` at `:309` which returns `IsSuccess = true` unconditionally. README ("Files below this size are ignored") does not carve out photos. |
| 5 | Parallel downloads, configurable 1–10, default 3 | **PARTIAL** | `SemaphoreSlim` created at `TelegramClient/TelegramApp.cs:70` (default 3) and resized in `UpdateConfig` at `:176`; clamped to 1–10 in the WPF slider (`SettingsWindow.xaml.cs:123`) and in the headless API (`Headless/Program.cs:195`). **Gap — the limit is not enforced across a config change:** `UpdateConfig` *replaces* the semaphore instance rather than adjusting it (`:176`). In-flight tasks captured the old instance (`:346`, `:609`) and release into it, so immediately after any settings change the effective concurrency is `oldInFlight + newLimit`. Because `UpdateConfig` is invoked on *every* per-chat UI edit (`MainWindow.xaml.cs:718, 1017, 1041, 1074, 1116, 1148, 1175`) and on every headless `PATCH /api/chats/{id}` (`HeadlessHost.cs:217`), this is routine, not exotic. |
| 6 | Retry button (↺) appears next to failed or timed-out downloads | **PARTIAL** | Wiring exists end-to-end: `TelegramApp.cs:451` (`OnRetryReady`) → `MainWindow.xaml.cs:107` → `DownloadProgressService.cs:334` (`SetRetryAction`) → `DownloadItem.cs:169` (`CanRetry`). **Gap 1 — the row self-destructs in 4 s:** `DownloadProgressService.cs:311` returns `true` for any status other than `"✔ Done"`, so the removal timer at `:293` deletes every `"✖ Error"`/`"✖ Timeout"` row 4 seconds after failure — the retry affordance has a 4-second lifetime regardless of the Auto-clean setting. **Gap 2 — plugin failures never get a retry action:** `TelegramApp.cs:446` requires `!string.IsNullOrEmpty(resultExecute.FileName)`, but yt-dlp/torrent failure paths return a `ResultExecute` with no `FileName` (`SocialMediaPlugin.cs:201`, `YouTubePlugin.cs:154`), so `OnRetryReady` is never invoked for them. |
| 7 | Auto clean — remove completed rows after a few seconds; unchecked = keep history + Open button | **PARTIAL** | Toggle persisted at `MainWindow.xaml.cs:751-759`, honoured at `DownloadProgressService.cs:311`, 4 s delay at `:293`. **Gap:** the toggle only gates the `"✔ Done"` status. Unchecking it does *not* keep error, timeout or cancelled rows — those are always removed (see #6). README's "unchecked = keep history" is true only for successes. |
| 8 | Open from UI (📂) opens the finished file or torrent folder | **WORKS** | `MainWindow.xaml.cs:761-782` handles both file and directory via `UseShellExecute`; visibility driven by `DownloadItem.CanOpenFile` at `DownloadItem.cs:42`; path populated from `ResultExecute.FilePath` at `MainWindow.xaml.cs:790-797` and `DownloadProgressService.cs:300`. |
| 9 | Cancel (✕) available while queued or downloading | **WORKS** | `MainWindow.xaml.cs:735`; queued rows removed outright and active rows cancelled via the registry at `DownloadProgressService.cs:349-378`; token plumbing `BasePlugin/CancellationRegistry.cs`, consumed at `BaseMessage.cs:65`. Cancel-all at `DownloadProgressService.cs:407`. |
| 10 | Stuck-download watchdog / `✖ Timeout` status | **WORKS** | Two layers: per-download 3-minute inactivity CTS reset on every progress callback (`BaseMessage.cs:68, 86`) and a 30-second sweeping watchdog (`DownloadProgressService.cs:43, 56-88`). |

### 1.2 Per-chat settings

| # | Claim | Verdict | Evidence + reasoning |
|---|---|---|---|
| 11 | Per-chat provider toggles (YouTube, SocialMedia, Torrent, Direct) | **PARTIAL** | Enforced at `TelegramClient/Factory/Service/MessageTextFactoryService.cs:145` — missing key means disabled, which matches the documented "explicitly enable" model. Persisted at `MainWindow.xaml.cs:1098-1118`; headless at `Program.cs:277-278`. **Gap — the fourth provider is not "Direct":** the plugin key is `"Other"` (`DownloadService/DownladerPlugin.cs:22`), a generic HTTP downloader, not the native Telegram path. Native Telegram attachments are handled by the factory (`FactoryMessagesService.cs:45-52`), which has no toggle at all and cannot be disabled per chat. |
| 12 | Per-chat yt-dlp quality: Best / 4K / 1080p / 720p / 480p / Audio-only | **BROKEN** | The setting is unreachable. `ConfigParams.NormalizeYtDlpQualityForAllChats()` (`TelegramAutoDownload/Models/ConfigParams.cs:62-66`) overwrites `chat.YtdlpQuality` with `HighestVideoQuality` for every chat, and it is called on **every** config read (`Models/ConfigFile.cs:31`) and on every import (`SettingsWindow.xaml.cs:218`). The WPF layer additionally hard-codes the value in all three chat-load paths (`MainWindow.xaml.cs:245, 302, 697`). The code comment states the intent plainly: *"Per-chat yt-dlp quality UI was removed; every chat always uses best video+audio"* (`ConfigParams.cs:59`). `YtdlpFormatHelper.GetFormatString` (`BasePlugin/YtdlpFormatHelper.cs:28-36`) still maps the six labels, but nothing can ever set them. A test locks in the removal: `TelegramAutoDownload.Tests/ConfigFilePersistenceTests.cs:136`. README `:21` and the Quality column at `:149` are fiction. |
| 13 | Per-chat regex filters skip files whose **name** matches any pattern | **PARTIAL** | Implemented at `BaseMessage.cs:291-303`. **Gap 1 — not case-insensitive.** `new Regex(regexPattern)` at `:293` uses default options; README `:197` explicitly promises "(case-insensitive)". The text-capture path *does* pass `RegexOptions.IgnoreCase` (`TelegramApp.cs:330`), so the two halves of the same feature disagree. **Gap 2 — `NullReferenceException` risk:** `regex.IsMatch(document.Filename)` at `:294` passes `Document.Filename`, which is null for documents lacking a `DocumentAttributeFilename` (exactly the case `Videos.cs:38` and `Files.cs:29` defend against). **Gap 3 — invalid patterns throw:** `new Regex(...)` at `:293` throws `ArgumentException` on a malformed pattern, and `CheckDownloadPolicy` is invoked at `FactoryMessagesService.cs:120`, *outside* the `try` that begins at `:139`. The UI validates patterns in the Filter dialog (`FilterDialog.xaml.cs`) but the headless `PATCH /api/chats/{id}` accepts any string (`Program.cs:276-277`). |
| 14 | Per-chat folder template with `{Type} {ChatName} {Year} {Month} {Day}` | **PARTIAL** | Resolution at `TelegramClient/FolderTemplateHelper.cs:39-64`; absolute-path escape hatch at `:57` and `:80`; layout fallback at `:93-101`; used by `BaseMessage.PathLocationFolder` at `:264`. **Gap — date tokens are the *download* date, not the message date.** `Resolve` defaults `at` to `DateTime.Now` (`:42`), and neither `ResolveDownloadDirectory` (`:78`) nor `SaveCapturedTextAsync` (`TelegramApp.cs:1329`) nor `ExistingDownloadValidator.ResolveChatDirectory` (`Headless/Scanning/ExistingDownloadValidator.cs:55`) ever supplies the message timestamp. README `:231-233` states these tokens are "of the message". Consequence beyond cosmetics: a Sync of old history files everything under today's date, and the headless pre-flight duplicate check computes the same (today's) directory, so it stays self-consistent but historical archives are mis-foldered. |
| 15 | Emoji reaction on download **start** | **PARTIAL** | Fired at `TelegramApp.cs:394-398`, gated on there actually being queued work (`:384-387`). **Gap — wrong-peer risk:** `ReactToMessage` derives the peer from `updates.Chats.First()` (`:1443`) / `updates.Users.First()` (`:1451`) instead of resolving the message's own `peer_id` or the already-cached `_accessHashes[chat.Id]`. When Telegram batches an update containing more than one chat, the reaction is sent to whichever chat happens to be first in the dictionary. |
| 16 | Emoji reaction on download **complete** | **PARTIAL** | Fired at `TelegramApp.cs:420`. Same wrong-peer risk as #15. Additional documented-behaviour gap: reactions are deliberately **not** sent during Sync (comment and code at `:639-658` call `ExecuteDirectAsync` with no reaction step), while README `:152` presents Sync as equivalent to live monitoring. |
| 17 | History Icon — reaction sent only when a message is recorded in the history log | **WORKS** | `TelegramApp.cs:365-378`: gated on `chat.SaveHistory` **and** a non-empty `HistoryIcon`, exactly as documented at README `:154`. |
| 18 | Mute toggle via the Telegram API | **PARTIAL** | `TelegramApp.MuteChatAsync` at `:1594-1612` correctly uses `Account_UpdateNotifySettings` with `mute_until` far-future/epoch; UI at `MainWindow.xaml.cs:379-408` persists the flag. **Gap — DMs and cache misses target the wrong peer type:** `:1599-1601` builds only `InputPeerChannel` or `InputPeerChat`; there is no `InputPeerUser` branch, unlike the correct `ResolveInputPeerAsync` at `:731-740`. Muting a `User` chat, or any chat whose access hash is not yet cached, silently addresses `InputPeerChat(id)`. |
| 19 | Per-chat Download Types (Videos / Photos / Music / Files) | **WORKS** | Central gate `IsMessageTypeEnabled` at `TelegramApp.cs:1223-1251` (MIME-first, then `DocumentMediaKindHelper`), plus per-handler re-checks at `Photos.cs:27`, `Videos.cs:25`, `Files.cs:33`. |

### 1.3 Sync & history

| # | Claim | Verdict | Evidence + reasoning |
|---|---|---|---|
| 20 | History sync — scan full chat history and download what matches and is not yet downloaded | **WORKS** | `TelegramApp.SyncHistoryAsync` at `:507-701`. Peer resolution with dialog fallback `:517-542`; pagination `:554-691`. The two classic pagination bugs are explicitly avoided: the loop terminates on `history.Messages.Length` (all message types) rather than on the filtered media count (`:563`, `:690`), and the next `offset_id` uses the raw minimum ID across service/empty messages (`:691`). Three work streams — native media, URL/magnet plugin messages, filter text captures — are processed concurrently under the semaphore (`:612-683`). Regression-tested at `Tests/IntegrationTests.cs:553, 582, 600`. |
| 21 | Missed-update catch-up on `UpdateChannelTooLong` | **PARTIAL** | Handled at `TelegramApp.cs:278-285`; sweep at `:1366-1433` with a per-chat watermark (`:1376`) advanced atomically (`:305`, `:1406`). **Gap 1 — catch-up only recovers native media:** the filter at `:1395-1397` is `m.media != null`, so URL, magnet-link and filter-text messages missed during the gap are lost permanently (Sync at `:569-589` handles all three). **Gap 2 — download-type toggles ignored:** unlike Sync (`:569`), the catch-up path does not call `IsMessageTypeEnabled`, so it queues media the user has switched off; the per-handler guards then reject it, producing silent no-ops rather than respecting intent at queue time. **Gap 3 — silent no-op on cache miss:** `:1370` returns immediately when the channel's access hash is not cached, with no dialog-list fallback and no log line. |
| 22 | Sticker / voice message filtering (silently skipped) | **WORKS** | `TelegramApp.cs:1231` (sticker) and `:1232-1233` (`DocumentAttributeAudio.Flags.voice`); second line of defence in `Photos.cs:34`. Tested at `Tests/IntegrationTests.cs:408, 434, 457` and `Tests/MessageTypeRoutingTests.cs:83, 118`. |
| 23 | History checkbox → JSONL log at `History/{ChatName}.jsonl`; 📤 exports full history | **WORKS** | Live append `TelegramApp.cs:365-371` → `MainWindow.xaml.cs:111-117` → `ChatHistoryService.AppendEntryAsync` (`:43-49`). Full export `TelegramApp.ExportChatHistoryAsync` at `:1089-1182`, sorted oldest-first at `:1169`, UI at `MainWindow.xaml.cs:1253`. Note the status string at `TelegramApp.cs:1175` advertises `History/{Type}/{Name}.jsonl`, but the real path omits the type segment (`ChatHistoryService.cs:33-36`) — README `:153` is the correct one. See §3 for the resulting filename-collision behaviour. |
| 24 | Chat list Members column (participant count from Telegram) | **WORKS** | Populated during dialog enumeration at `TelegramApp.cs:1684-1685`; deliberately not restored from config on refresh (`MainWindow.xaml.cs:311`). |

### 1.4 Scheduling

| # | Claim | Verdict | Evidence + reasoning |
|---|---|---|---|
| 25 | **Quiet Hours** — daily window (incl. overnight) during which no new downloads start | **BROKEN — feature does not exist** | A case-insensitive search for `quiet` across every `.cs`, `.xaml`, `.js`, `.html` and `.csproj` in the repository returns **zero** matches. There is no `QuietHoursHelper`, no `QuietHoursEnabled`/`QuietHoursFrom`/`QuietHoursTo` on `ConfigParams` (`TelegramAutoDownload/Models/ConfigParams.cs:9-67` — the full property list contains nothing time-related), no UI control in `SettingsWindow.xaml.cs:35-59` (`LoadValues` enumerates every persisted setting) or in `BtnSave_Click` (`:116-130`), and no time check anywhere on the download path (`TelegramApp.cs:289-491`, `BaseMessage.cs:112-185`). The claim appears four times: README `:33` (Scheduling section), `:262` (Settings table), `:313` (test-coverage list), `:330` (architecture diagram). All four are false. |

### 1.5 Statistics & monitoring

| # | Claim | Verdict | Evidence + reasoning |
|---|---|---|---|
| 26 | Session stats — files and bytes since launch | **WORKS** | Counters incremented under `Interlocked` at `DownloadProgressService.cs:277-279`; rendered at `MainWindow.xaml.cs:493-505`. |
| 27 | All-time persistent stats in `stats.json` | **PARTIAL** | `Services/StatisticsService.cs:38-42` subscribes to `DownloadCompleted`; debounced 5 s save at `:45-52`; atomic temp-then-rename write at `:57-60`; corruption-tolerant load at `:69-79`. **Gap — `Interlocked` on a replaceable target:** `:39-40` does `Interlocked.Increment(ref _data.TotalFilesAllTime)` while `Reset()` at `:84` swaps the whole `_data` instance; a completion racing a reset increments the discarded object. Low severity, but it is the one documented durability guarantee in the feature. |
| 28 | Disk space bar — free space, folder size, file count, low-space colours | **WORKS** | `Services/DiskSpaceService.cs:68-160` (60 s timer at `:49`, generation-guarded background scan at `:106-128`); colour thresholds at 1 GB/5 GB in `MainWindow.xaml.cs:543-555`. |
| 29 | Real-time progress, speed and ETA for every active download | **PARTIAL** | `DownloadProgressService.cs:230-256`. **Gap 1 — "Speed" is a cumulative average, not a current rate:** `:237` computes `item.BytesDownloaded / elapsedSinceStart`, so a stalled download's displayed speed decays slowly instead of dropping to zero, and a download that speeds up under-reports. README `:167` says "Current transfer speed". **Gap 2 — yt-dlp downloads have no size, speed or ETA at all:** both yt-dlp plugins report byte counts as literal zeros (`SocialMediaPlugin.cs:137`, `YoutubePlugin/YouTubePlugin.cs:95` pass `0, 0`), so `:235` takes the percent-only branch at `:251` — the Size and Speed columns stay blank for every Instagram/TikTok/YouTube item. |
| 30 | Total speed badge — combined speed of all active downloads | **PARTIAL** | `DownloadProgressService.cs:456-466`, surfaced at `MainWindow.xaml.cs:476-485`. Same cumulative-average basis as #29, and yt-dlp rows contribute nothing because `BytesDownloaded` stays 0 (`:462`). |
| 31 | "Active downloads are pinned to the top" (README `:172`) | **BROKEN** | `MainWindow.xaml.cs:124-126` installs exactly one sort: `EnqueuedAt` descending. `DownloadItem.SortOrder` (`Models/DownloadItem.cs:144-149`), which exists precisely to pin Downloading→Queued→finished, is never referenced by a `SortDescription` anywhere. It is dead code. CHANGELOG `2.9.3` ("Downloads list sorted **newest first**") describes the real behaviour and silently contradicts the README. |
| 32 | Completed/error rows auto-remove after 4 seconds (README `:172`) | **PARTIAL** | The 4 s delay is real (`DownloadProgressService.cs:293`) but is unconditional only for failures; successes are gated by Auto-clean (`:311`). The README sentence and the Auto-clean bullet at README `:16` describe mutually exclusive behaviours; the code implements a third thing. |
| 33 | Connection dot — 🟢 connected / 🔴 disconnected | **WORKS** | `TelegramApp.IsConnected` at `:75`; 15 s monitor at `:1743-1797` that defers reconnection while transfers are active (`:1759-1764`, the fix from CHANGELOG 2.8.6); UI binding `MainWindow.xaml.cs:150-158`. |

### 1.6 Notifications, tray, updates

| # | Claim | Verdict | Evidence + reasoning |
|---|---|---|---|
| 34 | Telegram bot notifications on start/progress/complete/error, HTML-formatted | **WORKS** | `TelegramAutoDownload/Notification.cs`: startup `:126`, progress `:35`, complete `:69` and `:178`, error `:152`; each gated on its own `NotifyOn*` flag (`:38, 72, 129, 155`) persisted at `SettingsWindow.xaml.cs:126-129`. HTML escaping at `Notification.cs:241`; `parse_mode=HTML` in `Logger/Services/TelegramService.cs`. Progress messages are edited in place with a sentinel-based race guard (`:44-53`). Minor internal inconsistency: the field comment at `:19` says 10 % throttle, the code at `:62` uses 25 %. |
| 35 | Windows tray icon; app keeps running when the window is closed | **WORKS** | Icon + context menu `App.xaml.cs:99-118`; close interception and Minimize/Exit/Cancel handling `MainWindow.xaml.cs:1449-1476`; `CloseDialog.xaml.cs`. |
| 36 | Auto-update check on startup | **WORKS** | `MainWindow.xaml.cs:211-216` (2 s delayed) → `:1418-1446`; honours `skipped_version.txt` at `:1425-1428`. |
| 37 | Manual check via the version button, "✓ Up to date" feedback | **WORKS** | `MainWindow.xaml.cs:1336-1416`; concurrency guard `:1351`; green "✓ Up to date" for 2.5 s at `:1369-1378`. |
| 38 | Smart update type detection (portable → ZIP + robocopy, installed → silent installer) | **PARTIAL** | Install-type detection `Services/AutoUpdateService.cs:33-42`; asset preference `:88-99`; installer batch `:168-178`; robocopy batch with xcopy fallback `:212-221`. **Gap — collateral process termination:** the installer script unconditionally runs `taskkill /f /im TelegramAutoDownload.exe` (`:173`) after waiting on its own PID, which force-kills *any* other process with that image name (e.g. a second portable copy the user is running from a different folder) without warning. |
| 39 | Single-instance guard | **WORKS** | Global mutex `App.xaml.cs:30-41`, released only after session handles close `:157`. |

### 1.7 Plugin system

| # | Claim | Verdict | Evidence + reasoning |
|---|---|---|---|
| 40 | Plugins resolved in priority order; first success stops the chain | **WORKS** | Ordering and short-circuit at `MessageTextFactoryService.cs:112-158` (`OrderBy(p => p.Priority)` at `:116`, `break` on success at `:154, :157`). Assemblies discovered by reflection from `Plugins/` with duplicate-simple-name protection at `:41-85`. Tested at `Tests/IntegrationTests.cs:93`. |
| 41 | README plugin table ordering: SocialMedia → YouTube → Torrent → Direct | **PARTIAL** | Actual priorities are SocialMedia `2` (`SocialMediaPlugin/SocialMediaPlugin.cs:12`), Torrent `3` (`TorrentPlugin/TorrentPlugin.cs:10`), YouTube `10` (`YoutubePlugin/YouTubePlugin.cs:18`), Other `100` (`DownloadService/DownladerPlugin.cs:23`). Torrent runs **before** YouTube, contradicting the README table at `:55-59`. Functionally harmless today because their `CanHandle` predicates are disjoint, but the documented contract is wrong. |
| 42 | SocialMedia handles "any `http(s)://` URL" | **PARTIAL / overstated** | `SocialMediaPlugin.cs:44-59` matches against a hard-coded 25-entry domain allowlist (`:16-41`) with exact-host or suffix matching at `:59`. Anything outside that list — Vimeo subdomains aside, e.g. `kick.com`, `bilibili.com`, most of yt-dlp's 1000+ extractors — falls through to the generic HTTP downloader, which will fetch the HTML page rather than the media. The README's parenthetical list is accurate; the "any URL" framing is not. |
| 43 | "Direct — Telegram file attachments — native Telegram API" is the fourth plugin | **PARTIAL / mislabelled** | There is no `Direct` plugin. The fourth plugin is `DownladerPlugin` with `PluginName = "Other"` (`DownladerPlugin.cs:22`), a chunked HTTP range downloader (`:75-97`) for arbitrary URLs. Native Telegram attachment download is not a plugin at all — it is the factory path (`FactoryMessagesService.cs:45-52`). The web UI perpetuates the confusion by labelling the toggle "D / Direct" (`Headless/wwwroot/index.html:130`) while the persisted key is `"Other"`. |
| 44 | Torrent plugin downloads real content via MonoTorrent (magnets + `.torrent` attachments) | **PARTIAL** | Magnet path `TorrentPlugin.cs:21-43` → `BasePlugin/TorrentDownloadService.cs`; `.torrent` attachment path `Factory/FactoriesMessages/Files.cs:36-51, 99-153` (downloads the metadata file to temp, runs the swarm, cleans up at `:139-152`). **Gap — dedup index is mutated unconditionally:** `Files.cs:49` calls `FileDownloadIndex.Remove(document.ID)` on *every* torrent attachment whose content is not yet present, so re-posting the same `.torrent` always restarts a full swarm download. The presence heuristic itself is a >5 MB file scan (`:89`), which misclassifies legitimately small torrent payloads as "not downloaded". |
| 45 | Generic HTTP downloader ("Other") | **PARTIAL** | `DownladerPlugin.cs:30-116`. **Gap 1 — retry corrupts the output file:** `WithRetryAsync` at `:82-91` re-issues the range request but `contentStream.CopyToAsync(fileStream)` at `:89` appends at the *current* stream position; a chunk that fails mid-copy leaves its partial bytes in the file and the retry appends the full chunk again. There is no `fileStream.Seek` reset. **Gap 2 — no 206 verification:** `:87` only calls `EnsureSuccessStatusCode()`; a server that ignores `Range` and returns `200` with the whole body causes every chunk iteration to append the entire file. **Gap 3 — no `.part`/resume:** it writes straight to `filePath` with `FileMode.Create` (`:72`) and deletes on cancel (`:106`), so the README's resume claim does not apply to this plugin. |
| 46 | yt-dlp is downloaded and kept up to date automatically | **UNVERIFIABLE-BY-READING** | `Services/YtdlpService.EnsureAsync()` is invoked fire-and-forget at `MainWindow.xaml.cs:190` and the resolver checks AppData then install dir (`SocialMediaPlugin.cs:243-265`, `YouTubePlugin.cs:172-195`). Whether the *update* half actually refreshes a stale binary depends on GitHub asset naming and the on-disk version comparison inside `YtdlpService.cs`. **Experiment:** place a deliberately old `yt-dlp.exe` in `%APPDATA%\TelegramAutoDownload\tools\`, launch, and assert the file hash changes and the new version is what the plugins invoke. |

### 1.8 Settings, config, data files

| # | Claim | Verdict | Evidence + reasoning |
|---|---|---|---|
| 47 | Export settings to JSON **(credentials excluded)** | **BROKEN** | `SettingsWindow.xaml.cs:178-196` (`BuildConfigSnapshotFromUi`) explicitly copies `ApiHash = pbApiHash.Password` (`:184`) and `BotToken = pbBotToken.Text` (`:185`), plus the full chat list, and the XML doc comment above it says so out loud: *"Full in-memory snapshot for export (includes Telegram API hash, bot token, chats)"* (`:177`). The result is serialised and shown for copy-out at `:153-155`. The behaviour is locked in by `Tests/SettingsExportImportTests.cs:29`, which asserts `json.Should().Contain("api_hash_secret")`. README `:263` is false, and this is a credential-disclosure hazard whenever a user shares an exported config for support. |
| 48 | **Dark Mode** switches the app theme | **BROKEN** | `App.xaml.cs:89-92` hard-codes `ThemeManager.Current.ChangeTheme(this, "Light.Blue")` under the comment *"Light theme only (dark mode removed)"*. `SettingsWindow.LoadValues` (`:35-59`) and `BtnSave_Click` (`:83-142`) contain no theme control or persisted theme field, and `ConfigParams` has no theme property. README `:259` is fiction. |
| 49 | Data files: `config.txt`, `downloaded_ids.json`, `stats.json`, `skipped_version.txt`, `session.dat`, `logs/`, `tools/yt-dlp.exe` | **WORKS** | All seven paths exist and are used: `AppPaths.cs` (config/stats/skipped-version/logs/env), `FileDownloadIndex.cs:23-25`, `TelegramApp.cs:101-105` (session), resolver paths in `SocialMediaPlugin.cs:247-252`. |
| 50 | CHANGELOG 2.0.0: "`ConfigFile.Read()` now returns `new ConfigParams()` on null/corrupt JSON" | **BROKEN** | `Models/ConfigFile.cs:30` is `JsonConvert.DeserializeObject<ConfigParams>(data) ?? new ConfigParams()`. The `??` only covers the literal JSON token `null`; malformed JSON throws `JsonReaderException` out of `DeserializeObject` before the coalesce is reached. This is not speculation — `Tests/ConfigFilePersistenceTests.cs:193` is named `Read_CorruptJson_ThrowsJsonReaderException` and asserts exactly that. Because `Read()` is called on nearly every UI interaction (`MainWindow.xaml.cs:679, 1005, 1030, 1050, 1110, 1128, 1169, 1184, 1243`), a single corrupt byte in `config.txt` makes the application unusable rather than resetting to defaults. Compounding it, `ConfigFile.Save` at `:24` is a bare `File.WriteAllText` with no temp-and-rename — unlike `FileDownloadIndex.SaveInternal` (`:113-118`) and `StatisticsService.Save` (`:57-60`), which both do it correctly. A crash during any settings write can therefore produce exactly the corrupt file that `Read()` cannot survive. |

### 1.9 Claims that cannot be settled by reading

| # | Claim | Verdict | Deciding experiment |
|---|---|---|---|
| U1 | `.part` resume actually continues from the existing byte offset | **UNVERIFIABLE-BY-READING** | The code seeks to end-of-stream (`BaseMessage.cs:206-215`) on the documented assumption that WTelegramClient's `DownloadFileAsync` reads `stream.Position` as the start offset. That is a third-party contract, not visible in this repo. **Experiment:** against a fake/recording Telegram transport, start a 100 MB download, kill at ~30 MB, restart, and assert (a) the server-side `offset` of the first `upload.getFile` equals the `.part` length, and (b) the final file's SHA-256 matches a single-shot download. `Tests/IntegrationTests.cs:178-268` only asserts the *stream contract* (position at end, rename on completion) — it never exercises WTelegram. |
| U2 | Emoji reactions are actually accepted by Telegram for the emoji the user picked | **UNVERIFIABLE-BY-READING** | `Messages_SendReaction` at `TelegramApp.cs:1460` can fail server-side with `REACTION_INVALID` for chat-restricted emoji; the available-reactions fetch at `:1489-1535` falls back to a hard-coded set (`:1491`) that a given chat may reject. **Experiment:** integration test against a real test account with a channel that restricts reactions to a subset; assert the failure surfaces to the user rather than only into `resultExecute.ErrorMessage` (`:425`). |
| U3 | Torrent swarm download completes for real magnets (DHT/UPnP path) | **UNVERIFIABLE-BY-READING** | `BasePlugin/TorrentDownloadService.cs` configures DHT/PEX/port-forwarding and two distinct timeouts (30 min metadata, 10 min stall) per CHANGELOG 2.8.8/2.8.9. Success depends entirely on network reachability. **Experiment:** seed a local torrent from a second process on the same host, download it through the plugin, assert byte-identical output and that progress callbacks include the hash-check phase. |
| U4 | yt-dlp format selectors actually yield the requested resolution | **UNVERIFIABLE-BY-READING** | `YtdlpFormatHelper.cs:28-36` builds selector strings that only yt-dlp can interpret. Moot in practice today — see BROKEN #12, the labels are unreachable. **Experiment:** golden-file test invoking a pinned yt-dlp against a fixed local media file, asserting output resolution per label. |
| U5 | WPF UI behaviour end-to-end (tray restore, close dialog, retry button visibility) | **UNVERIFIABLE-BY-READING** | `TelegramAutoDownload.UITests` (FlaUI) has 7 smoke tests (`MainWindowTests.cs:33-120`) covering window title, chat list presence, search box, downloads grid, sync button, settings dialog. None covers tray minimise/restore, the close dialog's three branches, or the retry button's (4-second) lifetime. **Experiment:** extend the FlaUI suite with a driven failure so the `↺` button appears, and assert it is clickable ≥N seconds later. |

---

## 2. Desktop (WPF) vs Headless (Web) — feature-parity gap table

The headless server is the intended commercial surface. Every row marked **MISSING** is a product gap, not merely a porting task. Several are marked *engine-ready*: the capability already exists in shared `TelegramClient` code and only lacks an HTTP route and a UI control.

### 2.1 Desktop features missing from the web UI

| # | Feature | WPF evidence | Headless status | Notes |
|---|---|---|---|---|
| 1 | **Emoji reactions** (start / complete / history icons) | `MainWindow.xaml.cs:891-943`, engine `TelegramApp.cs:394, 420, 1435` | **MISSING** | `ChatPatch` (`Headless/Program.cs:607-614`) has no reaction fields and `ChatDtoView` (`:616-640`) does not even return the saved values, so a chat configured in the desktop app shows no reactions in the web UI. `HeadlessHost.RefreshChatsAsync:170-171` *preserves* them on refresh — the data survives but is unreachable. **Engine-ready.** |
| 2 | **Mute / unmute a chat** | `MainWindow.xaml.cs:379-408`, engine `TelegramApp.cs:1594` | **MISSING** | `ChatDtoView` exposes `Muted` read-only (`Program.cs:629`); there is no `PATCH` field and no `/api/chats/{id}/mute` route. **Engine-ready.** |
| 3 | **Telegram bot notifications** (start/progress/complete/error, token, chat id, test button) | `Notification.cs` (whole file), config UI `SettingsWindow.xaml.cs:41-57, 227-268` | **MISSING** | `Notification.cs` lives in the WPF project and is never referenced by the headless host. `/api/settings` (`Program.cs:157-168`) exposes no `BotToken`, `ChatId` or `NotifyOn*`. For an unattended server this is the *primary* way an operator would learn that downloads are failing — its absence is the single most commercially significant gap. |
| 4 | **Session + all-time statistics** (`stats.json`) | `Services/StatisticsService.cs`, `MainWindow.xaml.cs:493-505` | **MISSING** | No `/api/stats`. `/api/queue/stats` (`Program.cs:360-373`) returns DB row counts by status, which is queue bookkeeping, not "files and bytes downloaded". |
| 5 | **Disk space / free space monitoring** | `Services/DiskSpaceService.cs`, `MainWindow.xaml.cs:509-556` | **MISSING** | No endpoint. A headless container filling its volume has no in-product warning at all — the most likely silent production failure. |
| 6 | **Cancel / cancel-all a live download** | `MainWindow.xaml.cs:735, 741`, `DownloadProgressService.cs:349, 407` | **MISSING** | `/api/queue/{chatId}/{messageId}/retry` (`Program.cs:456`) only flips a DB status. `CancellationRegistry` is never exposed, so an operator cannot stop a runaway 40 GB transfer without pausing the whole orchestrator (`:494`) or restarting the container. |
| 7 | **Chat history export to JSONL** | `MainWindow.xaml.cs:1253-1293`, engine `TelegramApp.cs:1089` | **MISSING** | Route does not exist. **Engine-ready.** |
| 8 | **Members export (CSV / vCard)** | `MembersExportDialog.xaml.cs:83-147`, engine `TelegramApp.cs:966` | **MISSING** | Route does not exist. **Engine-ready.** (See §3 — this is also a privacy-sensitive capability that should be gated deliberately rather than ported by default.) |
| 9 | **Manual browse + selective download** | `ChatBrowseWindow.xaml.cs`, `MainWindow.xaml.cs:629-634`, engine `TelegramApp.cs:824, 855` | **MISSING** | `FetchBrowseHistoryPageAsync` and `ManualDownloadMessagesAsync` are public and unrouted. The nearest web equivalent, `POST /api/chats/{id}/test-download` (`Program.cs:418`), is a fixed newest-10 probe with no message picker. **Engine-ready.** |
| 10 | **Per-plugin folder templates** (Social / YouTube / Other / Torrent) | `FolderTemplateDialog.xaml.cs`, `MainWindow.xaml.cs:1180-1211` | **MISSING** | `ChatDto` carries all four (`ChatDto.cs:66-75`) and `HeadlessHost.RefreshChatsAsync:183-186` preserves them, but `ChatPatch` (`Program.cs:607-614`) only accepts the single `FolderTemplate`. |
| 11 | Auto-update (startup check, manual check, in-place install) | `Services/AutoUpdateService.cs`, `MainWindow.xaml.cs:1336, 1418` | **MISSING** | Reasonable for a container (image-tag upgrades), but there is no version endpoint either — the web UI never displays the running version. |
| 12 | Settings export / import | `SettingsWindow.xaml.cs:150-219` | **MISSING** | No backup/restore path for a server deployment. |
| 13 | "Clear storage" disk-reclaim tool | `ClearStorageDialog.xaml.cs` | **MISSING** | Compounds gap #5 — no way to see *or* reclaim space from the web UI. |
| 14 | Open downloaded file / folder | `MainWindow.xaml.cs:761` | **MISSING (by nature)** | Server-side; would need a download/stream endpoint instead. Today files are only reachable through the mounted volume. |
| 15 | Per-chat yt-dlp quality | — | **MISSING in both** | Broken in WPF (#12 above) and absent from `ChatPatch`. |
| 16 | System tray | `App.xaml.cs:99` | **N/A** | Not applicable to a server. |
| 17 | Quiet Hours | — | **MISSING in both** | Does not exist anywhere (#25). Worth noting that for an unattended server, a bandwidth-scheduling window is a *more* valuable feature than it was on the desktop. |

### 2.2 Headless-only capabilities (no WPF equivalent)

Recorded for completeness — these are the headless app's genuine advantages and should not be regressed when closing the gaps above.

| Capability | Evidence |
|---|---|
| SQLite-backed durable media queue with a real status machine (`pending → queued → in_progress → done/failed/skipped`) | `Headless/Data/MediaRepository.cs`, `Data/MediaStatus.cs`, `Data/Schema/001_initial.sql` |
| Legacy `downloaded_ids.json` → SQLite migration | `Headless/Data/LegacyDedupMigration.cs:1-42`, invoked `Program.cs:108` |
| Resumable, rate-limited history bootstrap with per-chat scan state | `Headless/Scanning/BootstrapScanner.cs:41-141`, `BootstrapManager.cs` |
| Token-bucket API rate limiter, user-tunable at runtime | `Scanning/TokenBucketRateLimiter.cs`, `RateLimitThresholds.cs`, `Program.cs:228-242` |
| FLOOD_WAIT detection, global pause and UI banner | `Scanning/FloodWaitTracker.cs`, `TelegramClient/FloodWaitHelper.cs`, `Program.cs:288` |
| Pause/resume downloads, persisted across restarts | `ConfigParams.cs:40`, `Program.cs:494-501`, honoured `DownloadOrchestrator.cs:55` |
| Pre-flight "already on disk" validation before spending an API call | `Scanning/ExistingDownloadValidator.cs:15-44` |
| Bulk queue operations (retry-all-failed / retry-all-skipped / delete-failed / clear-chat) | `Program.cs:463-486` |
| Web log viewer with tail, search and SSE live-follow | `Logging/HeadlessLogReader.cs`, `Program.cs:291-357` |
| Username/password web gate with signed cookies and forced default-password change | `WebAuthService.cs`, `WebAuthMiddleware.cs`, `wwwroot/index.html:310-331` |
| Test-download probe (newest 10, no DB writes) | `Scanning/ChatDownloadTester.cs`, `Program.cs:418` |

### 2.3 Parity risks introduced by the headless architecture itself

| Risk | Evidence |
|---|---|
| Every chat `PATCH` calls `Telegram.UpdateConfig` (`HeadlessHost.cs:217`), which rebuilds `FactoryMessagesService` — re-scanning and re-reflecting the `Plugins/` directory (`MessageTextFactoryService.cs:35-86`) — **and** replaces the download semaphore (`TelegramApp.cs:176`) **and** kicks off another background `.part` cleanup sweep over the entire download tree (`:180-186`). A user toggling ten checkboxes triggers ten full-tree scans. |
| The live path and the orchestrator path can both act on the same message. `MediaTracker` inserts live-discovered rows as `pending` (`MediaTracker.cs:69`) specifically so the orchestrator skips them, but `BootstrapScanner.PromotePendingToQueuedAsync` (`BootstrapScanner.cs:132`) promotes **all** pending rows for the chat — including live ones still in flight. The `.part`/dedup layers should absorb the duplicate, but the two owners are not cleanly separated. |
| `MediaTracker` correlates completion events by `(chatName, fileName)` (`MediaTracker.cs:88-98`). Chat display names are neither unique nor stable, so a rename mid-run silently orphans DB rows in `in_progress`. |
| `HeadlessHost.UpsertByName` matches progress events with `d.FileName.EndsWith(file)` (`HeadlessHost.cs:123`) — a suffix match that can attach one download's progress to a different row (`video.mp4` vs `my_video.mp4`). |

---

## 3. Undocumented behaviour

Things the code does that the README never mentions. Ordered by how surprising, destructive or privacy-relevant they are.

### 3.1 Surprising — the Filter field creates files instead of skipping them

The README describes **Filter / IgnoreFileByRegex** as purely subtractive: *"files whose name matches any pattern are skipped"* (`:150`). The code gives the same field a second, opposite meaning: when a **text-only** message (no media, no URL, no magnet) matches any Filter pattern, the message is **captured and written to disk** as a `.txt` file.

- Live path: `TelegramApp.cs:321-340` detects the match and enqueues a `📝` row; `:402-403` routes it to `SaveCapturedTextAsync`.
- Sync path: `TelegramApp.cs:578-589` builds a `textCaptureMessages` set the same way; processed at `:662-681`.
- Manual/browse path: `TelegramApp.cs:877-885`.
- Writer: `TelegramApp.cs:1321-1364` → `{root}/{chatFolder}/Messages/{yyyyMMdd_HHmmss}_{id}.txt`, and it fires the **End Icon reaction** and the success notification because it returns `IsSuccess = true` (`:1351`).

A user who enters `\.jpg$` to *stop* downloading JPEGs will also begin archiving every text message containing the literal string `.jpg` as a new file on disk, and reacting to it in Telegram. There is no separate setting and no UI hint.

### 3.2 Privacy-relevant

| Behaviour | Evidence | Why it matters |
|---|---|---|
| **Member harvesting including phone numbers.** Exports every participant's user id, username, first/last name, **phone number**, admin and bot flags — to CSV and to a vCard file (`TEL;TYPE=CELL:`) for direct import into a phone's address book. | `TelegramApp.GetChannelMembersAsync:966-1070`, `MembersExportDialog.xaml.cs:97-109` (CSV header at `:97`, phone at `:105`) and `:121-147` (vCard, phone at `:142-143`); UI entry `MainWindow.xaml.cs:1215-1227` | Not mentioned anywhere in the README. This is bulk PII extraction from third-party groups; under GDPR it is the highest-risk capability in the product and it is currently a one-click, unlogged, unconfirmed action. |
| **Full conversation archiving.** With History enabled, *every* incoming message — text included, not just downloadable media — is appended to a JSONL file, with sender id, resolved sender name, forward provenance and timestamps. | `TelegramApp.cs:362-371` (fires for every message type, per the code's own comment), `ChatHistoryService.CreateEntry:89-116`, full export `TelegramApp.cs:1089-1182` | README `:153` describes History as a log but does not state that message *bodies* of all participants are persisted in plain text indefinitely. |
| **History files collide across chats.** The type segment is accepted as a parameter and then ignored, so two chats whose sanitised names match write into one file. | `ChatHistoryService.GetHistoryFilePath:31-37` — `chatType` is unused; test `Tests/ChatHistoryTests.cs:55` (`GetHistoryFilePath_SameName_SameFile_RegardlessOfType`) asserts this | Two different groups named "News" interleave into one archive with no marker distinguishing them. Tested, therefore intentional, therefore should be documented. |
| **Settings export leaks credentials.** See BROKEN #47 — `ApiHash` and `BotToken` are in the exported JSON. | `SettingsWindow.xaml.cs:177-186` | README `:263` actively tells the user the opposite, which makes the user *more* likely to share the file. |
| **Web dashboard exposes full application logs.** Tail, search and live SSE stream over HTTP, including chat names, file names and absolute paths. | `Program.cs:291-357`, `Logging/HeadlessLogReader.cs` | Anyone who reaches the dashboard reads the user's entire Telegram activity trail. Combined with §3.4, the default posture is weak. |
| **Session and logs always land in `%APPDATA%`, even for portable installs.** | `TelegramApp.cs:101-105`, `AppPaths.cs` | A "portable" user who deletes the extracted folder leaves behind a live Telegram session and full logs on the machine. |

### 3.3 Destructive

| Behaviour | Evidence |
|---|---|
| **Clear Storage deletes arbitrary user files.** Recursively enumerates *all* files under the download root (not only `.part`), lets the user multi-select, and hard-deletes with no recycle bin. `Select All` (`:187`) plus the default date range makes mass deletion two clicks. There is a confirmation dialog (`:148-154`), but the feature is entirely absent from the README. | `ClearStorageDialog.xaml.cs:80-107` (scan), `:141-183` (delete), entry point `SettingsWindow.xaml.cs:271-282` |
| **Startup deletes `.part` files older than 7 days** across the whole download tree, silently and in the background. | `PartFileCleanup.cs:27-52`, triggered `TelegramApp.cs:180-186`. Worse, it re-triggers on *every* `UpdateConfig` call — i.e. on every settings change. |
| **Auto-update force-kills every `TelegramAutoDownload.exe`.** `taskkill /f /im` matches by image name, not PID. | `Services/AutoUpdateService.cs:173` |
| **Logout deletes the session file** and the WPF path then relaunches the process. | `TelegramApp.LogoutAsync:145-150`, `SettingsWindow.xaml.cs:290-314` |
| **Torrent re-post always restarts a full swarm download** because the dedup entry is unconditionally removed. | `Factory/FactoriesMessages/Files.cs:49` |
| **Config is written non-atomically** and cannot be read back if the write is interrupted — see BROKEN #50. | `Models/ConfigFile.cs:24` + `:30` |

### 3.4 Security posture of the headless server (undocumented)

| Finding | Evidence |
|---|---|
| Default credentials `admin` / `changeme`, shipped in the compose file. | `WebAuthService.cs:16, 209, 214`; `docker-compose.yml` sets `WEB_PASSWORD: "changeme"` |
| Passwords hashed with a single unsalted `SHA256` — no PBKDF2/bcrypt/Argon2, no work factor. | `WebAuthService.cs:248-249` |
| Authentication can be turned off entirely with one env var. | `WebAuthService.cs:50-57` (`WEB_AUTH_ENABLED=false`) |
| Binds `0.0.0.0:8080` by default. | `Program.cs:42` |
| Session cookie is `Secure` only if `WEB_AUTH_SECURE_COOKIES=true`; default is insecure transport. | `WebAuthService.cs:141` |
| `UseStaticFiles()` is registered **before** `UseWebAuth()`, so the entire `wwwroot` is served unauthenticated regardless of the `IsPublicPath` allowlist. | `Program.cs:48-49` vs `WebAuthService.cs:173-183` |
| No CSRF protection on state-changing endpoints, which are cookie-authenticated with `SameSite=Lax` — `POST /api/queue/delete-failed`, `/api/login/logout`, `/api/settings/*` are reachable via cross-site form posts. | `WebAuthService.cs:140`; endpoints `Program.cs:171-501` |
| No rate limiting on `/api/web-auth/login`. | `Program.cs:68-81` |

To the team's credit, the mitigations that *are* present are real and worth keeping: the forced default-password-change modal (`wwwroot/index.html:310-331`), constant-time comparison (`WebAuthService.cs:83, 116`), HMAC-signed cookies with a persisted random key (`:231-246`), and log-path traversal protection via `ResolveSafeFileName` (`Program.cs:297`).

### 3.5 Other undocumented behaviour

| Behaviour | Evidence |
|---|---|
| **Manual browse ignores the user's own per-chat settings.** Downloads initiated from the browse window run against a clone with all four download types forced on and all four plugins force-enabled when the key was absent. | `TelegramApp.CloneChatDtoForBrowseManualDownload:919-958` (types at `:943`, plugins at `:924-928`) |
| **A `.torrent` attachment downloads even when the Files toggle is off**, provided the Torrent plugin is enabled for the chat. | `Factory/FactoriesMessages/Files.cs:33` |
| **The chat list includes archived chats** (folder 1), not just the main dialog list. | `TelegramApp.GetAllChats:1567-1570` |
| **Migrated/duplicate groups are silently de-duplicated by name + type**, dropping zero-member entries. | `TelegramApp.cs:1578-1587` |
| **A startup notification is sent to the Telegram bot automatically** on every launch when a bot is configured. | `MainWindow.xaml.cs:205-209`, `Notification.cs:126` |
| **`FILE_REFERENCE_EXPIRED` is transparently recovered** by re-fetching the message once. Good behaviour, undocumented. | `FactoryMessagesService.cs:95-103`, `TelegramApp.RefreshMessageAsync:192-239` |
| **Reconnection is deliberately deferred while transfers are active**, so a connection can stay down for the length of a large download. | `TelegramApp.cs:1759-1764` |
| **The headless container runs `user: "1029:100"`** — a host-specific uid/gid baked into the shipped compose file. | `docker-compose.yml` |
| **`Messages` is a magic folder name** under each chat directory for filter-captured text. | `TelegramApp.cs:1336` |
| **A `.part` file in the temp torrent directory is cleaned up on every torrent attachment**, including on the success path. | `Files.cs:139-152` |

---

## 4. How to read these verdicts

- **WORKS** means the implementation was located, the control flow was traced end to end, and no defect was identified by reading. It does **not** mean the feature is tested — see `03-test-strategy.md` for that mapping. Several WORKS features are entirely untested.
- **PARTIAL** always names the specific gap. Most are one-line fixes; a few (#6 retry lifetime, #45 chunk-retry corruption) are correctness bugs that will produce user-visible damage.
- **BROKEN** means either no implementation exists or the implementation provably contradicts the documented claim. Five of the eight are documentation defects that can be fixed by editing the README; three (#47 credential leak, #7/#31 sort, #50 corrupt-config crash) require code changes.
- **UNVERIFIABLE-BY-READING** is used only where a third-party or network contract determines the outcome. Each names the experiment that would settle it.

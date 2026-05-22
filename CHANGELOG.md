# Changelog

## [2.8.2] - 2026-05-22

### Fixed

- **Log viewer delete** no longer fails with "file is in use" when deleting today's active log — Serilog is closed and reopened automatically
- Serilog file sink uses `shared: true` so log files can be read while the app is running

## [2.8.1] - 2026-05-22

### Fixed

- **WTelegram KeepAlive network drops** no longer log as `[ERR]` — transient TCP timeouts are downgraded to `[WRN]` and the app auto-reconnects
- **Single-instance guard** prevents two app copies from locking `session.dat` on startup
- **Connection status dot** in the footer now reflects live Telegram connection state (green/red)
- **Background login failures** are logged clearly instead of being swallowed silently
- **`Client_OnUpdates` handler** wrapped in try/catch so update-processing errors cannot crash the session
- **`CHAT_ADMIN_REQUIRED`** on member export is logged at Debug level (expected when user is not admin)

## [2.7.14] - 2026-05-15

### Note

- **Stable rollback release** — same codebase as v2.7.4 (recommended if v2.7.5–v2.7.13 caused login, chat list, or download issues). Version **2.7.14** so the in-app updater can install it over newer builds.

## [2.0.0] - 2026-05-05

### Added

- **SocialMediaPlugin**: download any yt-dlp-supported site (Instagram, TikTok, Twitter, Reddit, …) — Priority 2
- **TorrentPlugin**: download magnet links via MonoTorrent — Priority 3
- `DownloadProgressService`: thread-safe singleton with `ObservableCollection<DownloadItem>` for real-time progress tracking
- `DownloadItem` model with `INotifyPropertyChanged` for data-binding
- **Priority system** on `IBasePlugin`/`BasePlugin` — plugins run in ascending priority order; first `IsSuccess = true` wins
- **Per-chat provider toggles** (`EnabledPlugins` dict on `ChatDto` and `Config`) — UI checkboxes in new Providers column
- `DownloadThreads` field on `ConfigParams` (default 3) with live slider in the status bar
- `SemaphoreSlim` in `TelegramApp` to honor `DownloadThreads` limit
- `QRCoder.Xaml` QR code login tab in `LoginWindow`
- `MahApps.Metro 2.4.11` modern UI framework with Telegram blue (#2AABEE) accent
- Active Downloads panel in `MainWindow` (DataGrid with progress bars)
- Type badges (Channel=blue, Group=green, User=purple) in chat list
- Alternating row colors + hover highlight
- HTML-formatted Telegram bot notifications (`parse_mode=HTML`)
- Unit test project `TelegramAutoDownload.Tests` (xUnit + FluentAssertions)
- yt-dlp.exe auto-downloaded to `tools\` folder

### Fixed

- **Group.cs**: used the already-matched `group` object for `chatParams` lookup instead of re-querying; added `null` guard on `chatParams`
- **BaseMessage.cs**: corrected inverted size-threshold condition (`documentSizeInMb < DownloadFromSize` → skip)
- **User.cs**: replaced unsafe `((Updates)updates)` cast with safe `updates as Updates` pattern; added `null` guard on `chatParams`
- **TelegramApp.cs**:
  - `ReactToMessage` wrapped in its own try/catch so reaction failure does not suppress `OnSaved`
  - Fixed typo `isCahnnel` → `isChannel`
  - Added `updates.Chats.Count > 0` guards before `.First()` calls
  - `OnSaved` and `OnWarnningMessage` delegates are now properly awaited
  - `configParams.Chats` null-checked in `UpdateConfig`
- **MainWindow.xaml.cs**:
  - `SelectChatId_Checked`: merges by ID instead of discarding all unselected chats
  - `HlOpenFolder_Click`: safe `as Run` cast + null check
  - `Download_Checked` / `DownloadSize_TextChanged`: use injected `ConfigFile` instead of `new ConfigFile()`
  - `DownloadSize_TextChanged`: fixed `textBox = "0"` → `textbox.Text = "0"`
  - `LoadDataAsync`: null-checked `fromConfigFile.Download` before copying flags
- **ConfigFile.cs**: `Read()` now returns `new ConfigParams()` on null/corrupt JSON
- **LoginWindow.xaml.cs**: `BtnLogin_Click` wrapped in try/catch
- **FactoryMessagesService.cs**: null-safe `mime_type?.Contains(…) == true`
- **MessageTextFactoryService.cs**: null-safe `(message.message ?? string.Empty).Split('\n')`
- **Photos.cs**: filename assigned before duplicate check; `Split('/').Last()` replaces fragile `IndexOf` substring
- **Videos.cs / Files.cs / Music.cs**: `File.OpenWrite` → `File.Create` (truncates on re-download); removed redundant `client` field
- **YouTubePlugin.cs**: returns `IsSuccess = false` with message "No mp4 stream available" when `streamInfo == null`
- **App.xaml.cs**: `DotEnv.Load()` moved to `OnStartup`; removed unused `System.Configuration` / `System.Data`
- **ConfigParams.cs**: hardcoded `AppId`/`ApiHash` replaced with `Environment.GetEnvironmentVariable("APP_ID/API_HASH")`
- **TelegramService.cs**: removed redundant `await Task.CompletedTask`; added `parse_mode=HTML`
- **FactoryUserService.cs**: removed unused `System.Threading.Channels` import
- Minor XAML cleanup: fixed `microsofft.com` typo, removed dead `SearchText` binding and broken Space key binding

### Changed

- **YoutubeExplode** upgraded from `6.3.16` → `6.5.7`
- **WTelegramClient** stays at `4.1.1` (QR login implemented without version bump)
- `IBasePlugin` now requires `int Priority { get; }`
- Plugin execution loop: sorted by Priority, respects `EnabledPlugins`, breaks on first success
- `Notification.cs`: rich HTML messages instead of raw JSON dumps
- `MainWindow` and `LoginWindow` converted to `MahApps.Metro.Controls.MetroWindow`
- `LoginWindow` redesigned with TabControl (Phone + QR tabs), step-by-step flow, inline error display

### Security

- Credentials (`APP_ID`, `API_HASH`) no longer hardcoded in source; must be set via `.env`
- `config.txt` and `.env` added to `.gitignore`
- `session.dat` already covered by `*.dat` rule

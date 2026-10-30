# Telegram Auto Download

> **Unofficial** — not affiliated with, endorsed by, or supported by Telegram Messenger Inc.

> **v2.9.3** — A WPF desktop application for Windows that monitors Telegram channels, groups, and DMs and automatically downloads media, YouTube/social-media links, and torrents.

---

## Features

### Download
- **Automatic media download** — videos, photos, music, and files from any monitored Telegram chat
- **Resume interrupted downloads** — `.part` files track progress; if the app closes mid-download the next attempt resumes from where it left off
- **Deduplication** — each file is tracked by its Telegram document ID; already-downloaded files are skipped automatically
- **Configurable minimum file size** per chat (skip files below N MB)
- **Parallel downloads** — configurable thread count (1–10, default 3)
- **Retry on failure** — a Retry button (↺) appears next to failed or timed-out downloads
- **Auto clean** — optional: remove completed rows from the Downloads panel after a few seconds (unchecked = keep history + Open button)
- **Open from UI** — 📂 opens the finished file or torrent folder directly from the Downloads list

### Per-chat settings
- **Per-chat provider toggles** — enable/disable YouTube, SocialMedia, Torrent, or **Other** (generic HTTP; shown as “Direct” in the UI) per chat. Native Telegram attachments are handled outside the plugin chain.
- **yt-dlp URL downloads** — always use the best available quality (per-chat quality presets are not applied)
- **Per-chat regex filters** — skip files whose name matches any semicolon-separated pattern (e.g. `\.jpg$; thumb_`)
- **Per-chat folder template** — customise the download path with tokens: `{Type}`, `{ChatName}`, `{Year}`, `{Month}`, `{Day}`
- **Emoji reactions** — send a reaction when a download starts and/or completes (configurable per chat)
- **Mute toggle** — mute/unmute Telegram notifications for a chat directly from the app

### Sync & history
- **History sync** — scan the full history of a chat and download everything that matches the current settings and hasn't been downloaded yet
- **Missed-update catch-up** — when `UpdateChannelTooLong` is received, the app fetches and processes any missed messages automatically
- **Sticker / voice message filtering** — stickers and voice messages are silently skipped

### Statistics & monitoring
- **Session stats** in the header strip — files downloaded and bytes transferred since last launch
- **All-time persistent stats** — total file count and bytes saved across all sessions (stored in `stats.json`)
- **Disk space bar** — free space on the download drive, total folder size and file count; turns orange/red when space is low
- **Real-time progress** — ETA and percentage for every active download; **Speed** is a cumulative average since the transfer started (not an instantaneous rate)
- **Total speed badge** — combined download speed of all active downloads

### Notifications
- **Telegram bot** — optional notification on start/progress/complete/error (HTML-formatted)
- **Windows tray icon** — app continues running in the system tray when the window is closed

### Update & version
- **Auto-update check on startup** — compares current version to the latest GitHub release
- **Manual check** — click the version button (`vX.X.X`) to check for updates at any time; if already up to date the button briefly shows "✓ Up to date"
- **Smart update type detection** — portable users get a ZIP + robocopy update; installed users get a silent installer

### Plugin system
Plugins handle **URL and torrent text** in messages. They run in **priority order** (lower number first); the first successful plugin stops the chain. Telegram-native media (video, photo, file attachments) is downloaded via the Telegram API, not these plugins.

| Plugin (priority) | Handles | Notes |
|---|---|---|
| **SocialMedia** (2) | `http(s)://` URLs on a **domain allowlist** (Instagram, TikTok, X/Twitter, Reddit, Facebook, YouTube hosts, …) | yt-dlp, best available quality |
| **Torrent** (3) | `.torrent` attachments and `magnet:` links in text | MonoTorrent |
| **YouTube** (10) | YouTube URLs not already handled by SocialMedia | yt-dlp fallback |
| **Other** (100) | Remaining `http(s)://` links | Generic HTTP download (UI label: “Direct”) |

---

## Prerequisites

| Requirement | Version |
|---|---|
| Windows 10 / 11 | any |
| .NET 8 runtime | 8.0+ |
| .NET 8 SDK (to build) | 8.0+ |

**yt-dlp** is downloaded and kept up to date automatically. You can also place `yt-dlp.exe` manually in `%APPDATA%\TelegramAutoDownload\tools\`.

---

## Installation

### Option A — Installer (recommended)
Download `TelegramAutoDownload_vX.X.X_Setup.exe` from the [latest release](https://github.com/il90il90/TelegramAutoDownload/releases/latest) and run it.

### Option B — Portable ZIP
Download `TelegramAutoDownload_vX.X.X_Portable.zip`, extract anywhere, and run `TelegramAutoDownload.exe`.

---

## Building from source

### 1. Clone

```bash
git clone https://github.com/il90il90/TelegramAutoDownload.git
cd TelegramAutoDownload
```

### 2. Create `.env` (first-time only)

```env
# Telegram API credentials — obtain at https://my.telegram.org
APP_ID=12345678
API_HASH=abcdef1234567890abcdef1234567890

# Optional: Telegram bot for download notifications
BOT_TOKEN=123456789:AABBCCDDEEFFaabbccddeeff
CHAT_ID=-1001234567890
```

> `.env` is in `.gitignore` — it is never committed.

### 3. Build

```bash
dotnet build RunnerApp.sln
```

### 4. Run

```bash
dotnet run --project TelegramAutoDownload
```

Or open `RunnerApp.sln` in Visual Studio 2022 and press **F5**.

---

## First run

1. On first launch the **Login** window appears — enter your Telegram phone number and the confirmation code (and 2FA password if enabled).
2. Open **Settings** (gear icon) and set your **download folder**, **App ID**, and **API Hash**.
3. The chat list is populated automatically after login. Check the chats you want to monitor.

---

## UI Reference

### Chat list columns

| Column | Description |
|---|---|
| ☑ | Enables **live monitoring** — every new message in this chat is evaluated for download. |
| ID | Telegram internal chat ID. |
| Name | Display name of the chat / group / channel. |
| Username | Public Telegram `@username`. Empty for private groups. |
| Type | `Channel`, `Group`, or `User`. |
| Members | Participant count (fetched from Telegram on refresh). |
| Start Icon | Emoji reaction sent when a download **starts**. |
| End Icon | Emoji reaction sent when a download **completes**. |
| Download Types | **Videos / Photos / Music / Files** — which media categories are active. |
| Min Size (MB) | Files below this size are ignored. `0` = no minimum. |
| Providers | Which plugins are active: YouTube, Social, Torrent, Other (“Direct” in the UI). |
| Quality | Legacy column; URL downloads always use best available quality via yt-dlp. |
| Filter | Semicolon-separated **regex** patterns — files whose name matches any pattern are **skipped**. Example: `\.jpg$; thumb_`. See [Filter guide](#filter--ignorefilerbyregex) below. |
| Folder | Custom download-path template with date/chat tokens. Empty = default layout. See [Folder guide](#folder-template) below. |
| Sync | One-time retroactive sweep — downloads everything in the chat history that matches current settings and hasn't been downloaded yet. |
| History | ☑ checkbox — when enabled, every incoming message is appended to a JSONL log file (`History/{ChatName}.jsonl`). 📤 exports the full chat history now. |
| History Icon | Emoji reaction sent to Telegram **only** when a message is recorded in the history log (requires History to be enabled). |
| Mute | 🔔 / 🔕 — toggles Telegram's own notification setting for this chat via the API. |

### Downloads panel

| Column | Description |
|---|---|
| File | File name. |
| Chat | Source chat. |
| Plugin | Plugin handling the download. |
| Size | Bytes downloaded / total size. |
| Progress | Progress bar + percentage. |
| Speed | Cumulative average transfer speed since the download started. |
| ETA | Estimated time remaining. |
| Status | `⏳ Queued` → `⬇ Downloading` → `✔ Done` / `✖ Error` / `✖ Timeout` / `✖ Cancelled` |
| ↺ | **Retry** — appears when a download fails; click to re-queue it. |
| ✕ | **Cancel** — available while queued or downloading. |

Rows are sorted by enqueue time (newest first). Completed/error rows auto-remove after 4 seconds unless **Auto clean** is off.

### Header strip

| Indicator | Description |
|---|---|
| 📊 This session | Files downloaded and total bytes transferred since launch. |
| 🗄 All-time | Cumulative totals across all sessions (persisted in `stats.json`). |
| ⚡ Total speed | Combined speed of all active downloads. |
| Active count | Number of items currently in the queue. |

### Footer

| Control | Description |
|---|---|
| Version button | Shows current version. Click to check for updates. If already up to date shows "✓ Up to date". If an update is available, shows the update dialog. |
| Connection dot | 🟢 connected / 🔴 disconnected. |
| Open folder | Opens the current download folder in Explorer. |

---

## Feature guides

### Filter — IgnoreFileByRegex

The **Filter** column accepts one or more **regular expressions** separated by semicolons (`;`). A file is skipped if its name matches **any** of the patterns (case-insensitive).

**Syntax**

```
pattern1; pattern2; pattern3
```

**Examples**

| Pattern | What it skips |
|---|---|
| `\.jpg$` | Any file ending with `.jpg` |
| `\.jpg$; \.png$` | JPEG and PNG files |
| `thumb_` | Any filename containing `thumb_` (thumbnails) |
| `^small` | Filenames that begin with `small` |
| `s\d+e\d+` | Episode files like `S01E05`, `s2e12`, … |
| `720p` | Files that contain `720p` in the name |
| `\.(jpg\|png\|gif)$` | Multiple extensions in one pattern |

> **Tip:** Leave the field blank to download everything that matches the Download Types and Min Size settings.

---

### Folder Template

The **Folder** column lets you set a custom subdirectory path inside your download folder. When left empty, files are saved to `{Type}/{ChatName}/` (e.g. `Channel/MyChannel/`).

**Available tokens**

| Token | Replaced with |
|---|---|
| `{Type}` | Chat type: `Channel`, `Group`, or `User` |
| `{ChatName}` | Display name of the chat (special characters are sanitised) |
| `{Year}` | 4-digit year when the file is saved (`2026`) |
| `{Month}` | 2-digit month when the file is saved (`01`–`12`) |
| `{Day}` | 2-digit day when the file is saved (`01`–`31`) |

**Examples**

| Template | Resulting path (example) |
|---|---|
| *(empty)* | `Channel/MyChannel/` ← default |
| `{ChatName}` | `MyChannel/` |
| `{ChatName}/{Year}-{Month}` | `MyChannel/2026-05/` |
| `{Year}/{Month}/{ChatName}` | `2026/05/MyChannel/` |
| `{Type}/{ChatName}/{Year}` | `Channel/MyChannel/2026/` |
| `Archive/{ChatName}/{Year}/{Month}/{Day}` | `Archive/MyChannel/2026/05/07/` |
| `{ChatName}/{Year}-{Month}-{Day}` | `MyChannel/2026-05-07/` |

> **Tip:** You can combine a static prefix with tokens, e.g. `Archive/{ChatName}` saves all chats under a common `Archive/` root.

---

## Settings

Open Settings from the gear icon in the top-right corner.

| Setting | Description |
|---|---|
| **App ID / API Hash** | Telegram API credentials from [my.telegram.org](https://my.telegram.org). Changing these requires re-login. |
| **Notification Bot** | Optional Telegram bot for download notifications. Toggle on/off; configure token, chat ID, and which events trigger a notification. |
| **Download Folder** | Where downloaded files are saved (browse to choose). |
| **Parallel Threads** | How many files download simultaneously (1–10). |
| **Export / Import** | Export settings to JSON (**API hash and bot token are excluded**) or import from a previously exported file. |

---

## Closing the app

Clicking the **×** button shows a dialog:

| Choice | Effect |
|---|---|
| **Minimize to Tray** | Hides the window; downloads continue in the background. Double-click the tray icon to restore. |
| **Exit** | Quits the application. In-progress downloads are cancelled. |
| **Cancel** | Dismisses the dialog; the window stays open. |

---

## Data files

All user data is stored in `%APPDATA%\TelegramAutoDownload\`:

| File | Description |
|---|---|
| `config.txt` | Chat list and all settings (JSON). |
| `downloaded_ids.json` | Set of Telegram document IDs already downloaded (deduplication index). |
| `stats.json` | All-time download statistics. |
| `skipped_version.txt` | Version tag the user chose to skip in the update dialog. |
| `session.dat` | WTelegramClient session (login state). |
| `logs/` | Rolling daily log files (kept for 7 days). |
| `tools/yt-dlp.exe` | Auto-managed yt-dlp binary. |

---

## Running tests

Build and run all test projects:

```bash
dotnet build RunnerApp.sln
dotnet test RunnerApp.sln
```

On Linux/macOS, only **`TelegramAutoDownload.Headless.Tests`** runs (the WPF test project requires Windows). On Windows, both **`TelegramAutoDownload.Tests`** and **`TelegramAutoDownload.Headless.Tests`** run.

The test suite covers:
- Quality format string mapping (yt-dlp)
- Plugin routing (`CanHandle` / priority)
- Resume download (`.part` file stream contract)
- Stale `.part` file cleanup
- Deduplication index concurrency
- History sync pagination correctness
- Sticker / voice message filtering
- URL message detection
- `ConfigFile` save/load round-trip
- `CancellationRegistry` operations
- HTTP downloader plugin (with mocked HTTP)
- Headless API, SQLite migrations, and config paths (`TelegramAutoDownload.Headless.Tests`)
- **DownloadItem.CanRetry** — property change notifications, state transitions
- **FolderTemplate** — token resolution, sanitization, padding
- **IgnoreFileByRegex** — filter string parsing
- **Persistent statistics** — JSON round-trip, atomic increment
- **CloseDialog** — enum values and default

---

## Architecture overview

```
TelegramAutoDownload (WPF, entry point)
├── TelegramClient          — Telegram API layer (WTelegramClient)
│   ├── TelegramApp         — update loop, plugin dispatch
│   ├── FileDownloadIndex   — deduplication index (debounced save)
│   ├── PartFileCleanup     — removes stale .part files on startup
│   └── FolderTemplateHelper— folder template token resolution (testable)
├── BasePlugin              — shared interfaces, Config, YtdlpFormatHelper
├── YoutubePlugin           — yt-dlp YouTube handler
├── SocialMediaPlugin       — yt-dlp generic URL handler
├── TorrentPlugin           — MonoTorrent magnet-link handler
└── DownloadService         — direct Telegram file download plugin
```

---

## Security notes

- **Web dashboard (headless):** set a strong `WEB_PASSWORD` before use. Do not expose the dashboard on the public internet without HTTPS and additional access controls (reverse proxy, firewall).

#### Dashboard login settings

| Variable | Effect |
|---|---|
| `WEB_USERNAME` / `WEB_PASSWORD` | Dashboard credentials. Docker Compose refuses to start without `WEB_PASSWORD`; `changeme` is rejected. |
| *(neither set, no `web-auth.json`)* | A one-time random password is printed to container stdout (`docker logs`), and you must change it at first sign-in. |
| `WEB_PASSWORD_RESET=true` | Replace an existing stored password with `WEB_PASSWORD`. Without it, a `WEB_PASSWORD` that disagrees with `web-auth.json` stops startup with an explanatory error. |
| `WEB_AUTH_TRUSTED_PROXIES` | Comma-separated proxy IPs whose `X-Forwarded-For`/`X-Forwarded-Proto` are trusted, so rate limiting sees real client IPs and cookies are marked `Secure` behind HTTPS. |
| `WEB_AUTH_SECURE_COOKIES` | Force the `Secure` cookie flag on or off. |
| `WEB_AUTH_ENABLED=false` | Disables login. Only allowed on a loopback bind (`LISTEN_URL=http://127.0.0.1:…`) unless `WEB_AUTH_DISABLE_I_UNDERSTAND=true` is also set. |
| `LISTEN_URL` | Bind address (default `http://0.0.0.0:8080`). |

Changing the password or signing out invalidates every existing session. Sign-in attempts are rate-limited, and successful and failed logins are recorded in `DATA_DIR/audit-<date>.log` (never the password).

#### Dashboard on desktop and phone

The dashboard adapts to the screen: on phones every table turns into a list of labelled cards, and the browser's **Add to Home Screen** / **Install app** opens it full-screen like a native app. Under **Settings → Appearance** you can pick a light, dark, or system theme, an accent colour, compact or comfortable tables, and which Chats-table columns to show. These choices apply instantly and are stored per browser.
- **Telegram session:** `session.dat` grants full API access to the linked account. In Docker it is stored at **`/data/TelegramAutoDownload/session.dat`** (under the mounted data volume). Back it up and restrict file permissions; treat backups like password backups.
- **Account risk:** aggressive automated scanning or high download volume can trigger Telegram rate limits or account action. Prefer a dedicated account where practical.

---

## Docker (Headless / NAS)

The **headless** deployment runs the web dashboard and download engine in a Linux container. It is the recommended path for **Synology Container Manager**, other NAS devices, and headless Linux servers. The WPF desktop app is not included in the container image.

Published images:

```bash
docker pull ghcr.io/OWNER/telegramautodownload:latest
```

Replace `OWNER` with your GitHub user or org (lowercase). For the upstream repository, use `ghcr.io/il90il90/telegramautodownload:latest`.

### Supported platforms

Images are built for:

| Platform | Typical hardware |
|---|---|
| `linux/amd64` | Intel/AMD Synology, most Linux PCs and VPS |
| `linux/arm64` | ARM64 Synology (e.g. Plus series), Apple Silicon Linux VMs, Raspberry Pi 64-bit OS |

Docker automatically pulls the correct architecture from the manifest — you do not pick a platform tag manually.

**ARMv7 (`linux/arm/v7`):** Not published from CI. The .NET runtime image supports armhf, but installing `ffmpeg`/`yt-dlp` under QEMU on GitHub runners often fails (process killed with exit code 9). Use **amd64** or **arm64** Synology models, or build locally on real 32-bit ARM hardware: `docker buildx build --platform linux/arm/v7 -t telegram-auto-download:armv7 .`

### Run with Docker

```bash
docker run -d --name telegram-auto-download \
  -p 8080:8080 \
  -e WEB_USERNAME=admin \
  -e WEB_PASSWORD='your-secure-password' \
  -e APP_ID=12345678 \
  -e API_HASH=your_api_hash \
  -v "$(pwd)/data:/data" \
  -v "$(pwd)/downloads:/downloads" \
  ghcr.io/il90il90/telegramautodownload:latest
```

Open `http://localhost:8080`, sign in with the web credentials, then complete Telegram login and chat setup in the UI.

### Docker Compose

Copy `.env.example` to `.env`, set secrets and paths, then:

```bash
docker compose up -d
```

The compose file pulls `GHCR_IMAGE` (see `.env.example`) and does **not** use `build:` — Synology and other hosts should consume the prebuilt GHCR image.

### Synology Container Manager

1. Open **Container Manager** → **Project** → **Create** (or import `docker-compose.yml`).
2. Set **Volumes** to persistent folders on the NAS, for example:
   - `/volume1/docker/telegram-auto-download/data` → `/data`
   - `/volume1/homes/work/telegram-downloads` → `/downloads`
3. Set **Environment** variables (`WEB_USERNAME`, `WEB_PASSWORD`, optional `APP_ID` / `API_HASH`, optional `TZ` such as `Asia/Kolkata`).
4. Map host port **8080** (or another port via `HOST_PORT` in `.env`).
5. Start the project and open the web UI on the mapped port.
6. Confirm status: Container Manager health (if shown), or `curl http://NAS_IP:8080/api/web-auth/status`.

Intel/AMD Synology units use `linux/amd64`; ARM64 Synology units use `linux/arm64` — both are selected automatically from GHCR.

### Private vs public GHCR packages

If the package on GHCR is **public**, Synology and Docker can pull without extra registry login.

If the package is **private**, configure registry authentication in Container Manager (or `docker login ghcr.io` on the host) using a GitHub account or PAT with `read:packages`. Do not put tokens or passwords in `docker-compose.yml`.

### Volume permissions (Synology / NAS)

The container runs as the official ASP.NET **`app`** user (**UID/GID 1654** in `mcr.microsoft.com/dotnet/aspnet:8.0`). Bind-mounted host directories must be writable by that user, or you can override the runtime user in Compose:

```yaml
user: "1029:100"   # example DSM user UID:GID
```

Persistent paths inside the container:

| Mount | Purpose |
|---|---|
| `/data` | SQLite DB (`tad.db`), config, logs, web auth, Telegram session under `/data/TelegramAutoDownload/` |
| `/downloads` | Downloaded media (default download root unless changed in settings) |

On the NAS, create the host folders and assign ownership to match the container user, for example (SSH, adjust UID/GID):

```bash
sudo chown -R 1654:1654 /volume1/docker/telegram-auto-download/data
```

Avoid `chmod 777`; use ownership aligned to the `user:` you run as.

### Local image build

```bash
docker build -t telegram-auto-download:local .
docker run --rm -p 8080:8080 \
  -e WEB_PASSWORD='choose-a-strong-password' \
  -v "$(pwd)/data:/data" \
  -v "$(pwd)/downloads:/downloads" \
  telegram-auto-download:local
```

Multi-platform build with Buildx (requires QEMU for non-native arches):

```bash
docker buildx create --use --name tad-builder 2>/dev/null || docker buildx use tad-builder
docker buildx build --platform linux/amd64,linux/arm64 -t telegram-auto-download:local --load .
```

`--load` only works for a single platform; omit `--load` and use `--push` to publish to a registry, or build one platform at a time for local testing.

### CI

`.github/workflows/docker-publish.yml` builds on pull requests (no push to GHCR) and pushes to `ghcr.io/<owner>/<repo>` on merges to the main branches and on version tags `v*.*.*` (`1.2.3`, `1.2`, `1`, and `latest` on the default branch).

---

## License

MIT — see [LICENSE](LICENSE). Third-party components are listed in [NOTICE](NOTICE).

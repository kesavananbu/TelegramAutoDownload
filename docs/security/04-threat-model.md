# 04 — STRIDE Threat Model: TelegramAutoDownload

**Scope:** commit `554d8e1`, branch `enterprise-readiness`.
**Method:** read-only source review. Every claim below is anchored to `path:line`. Claims I could
not verify from source are explicitly labelled **[INFERRED]**.
**Companion document:** [`04-findings.md`](./04-findings.md).

---

## 1. What is actually deployed

Two products share the `TelegramClient` + plugin core, and they have **different** threat profiles.
Conflating them produces wrong priorities, so they are modelled separately.

| | **A. Headless server** | **B. WPF desktop** |
|---|---|---|
| Entry point | `TelegramAutoDownload.Headless/Program.cs` | `TelegramAutoDownload/App.xaml.cs` |
| Exposure | HTTP listener on `0.0.0.0:8080` (`Program.cs:42`, `Dockerfile:46-48`) | Local interactive user only |
| Auth | Single-operator cookie gate (`WebAuthService.cs`) | OS session |
| Deployment | Docker (`Dockerfile`, `docker-compose.yml`) | Installer / portable ZIP |
| Primary risk | Remote network attacker | Malicious update / malicious downloaded file |

### 1.1 Correcting the "default deployment" assumption

The task brief assumes a user "runs `docker compose up` following the README". **That path does not
exist.** Verified:

- `README.md` (all 343 lines) documents only the WPF Windows app. It never mentions Docker,
  `docker-compose`, the headless server, `WEB_USERNAME`, or `WEB_PASSWORD`.
- `docker-compose.yml:12-13` has the port publish **commented out**, and `docker-compose.yml:1-3`
  requires a pre-existing external network named `internal`. As shipped, `docker compose up` fails
  unless that network already exists, and even then publishes nothing to the host.

So the realistic "default" headless deployment — and the one the shipped compose file is clearly
built for, given the commit message *"NPM-style web login"* — is:

> **Baseline Deployment (BD):** container attached to a shared Docker network, fronted by a reverse
> proxy (Nginx Proxy Manager or similar) that terminates TLS and forwards to `:8080`, with
> `WEB_USERNAME=admin` / `WEB_PASSWORD=changeme` exactly as shipped (`docker-compose.yml:16-17`).

Everything marked "reachable by default" in `04-findings.md` means **reachable in BD**. Two
consequences of BD that matter throughout:

1. The proxy terminates TLS; the app-to-proxy hop is plaintext HTTP, and the app never sets
   `Secure` on its cookie (`WebAuthService.cs:141`) — so the cookie is also accepted over plaintext.
2. Anything else on that Docker network can reach `:8080` directly, unauthenticated by the proxy.

A second, worse variant is common because the compose file invites it with a commented-out
`ports:` block: **Deployment Variant P (published port)** — operator uncomments `- "8080:8080"`,
putting an unauthenticated-by-default dashboard on the LAN, and on the internet behind any
port-forward.

---

## 2. Assets, ranked by loss impact

| ID | Asset | Location | Why it matters |
|---|---|---|---|
| **A1** | **Telegram session** `session.dat` | `TelegramApp.cs:101-106` → `$XDG_CONFIG_HOME/TelegramAutoDownload/session.dat`, i.e. `/data/TelegramAutoDownload/session.dat` in the container (`Dockerfile:44-46`) | **Crown jewel.** A full user-account MTProto session, not a scoped token. Exfiltration = total Telegram account takeover: read all history, impersonate, enumerate contacts, join/leave, and pivot to any 2FA-less service using Telegram as a login factor. Not revocable per-file — only by terminating the session from another Telegram client. |
| **A2** | Dashboard credential material | `web-auth.json` + `.web-auth-secret` in `HeadlessPaths.DataDir` (`WebAuthService.cs:18, 220-229, 237-245`) | `.web-auth-secret` is the HMAC key for **all** sessions. Reading it = mint a valid cookie for any username and expiry, forever (`WebAuthService.cs:131-146`). |
| **A3** | Telegram API credentials + bot token | `config.json` via `ConfigStore` (`ConfigStore.cs:16-22, 41`), fields at `ConfigParams.cs:11-17` | Plaintext `ApiHash`, `BotToken`. Also **[INFERRED]** the key WTelegramClient uses to encrypt A1 is derived from `api_hash`, which is stored in the same directory — see F-07. |
| **A4** | Downloaded media + the download tree | `ConfigParams.PathSaveFile`, `/downloads` volume | Confidentiality of the operator's private Telegram content; integrity/availability of a host-mounted directory. |
| **A5** | Operational logs | `HeadlessPaths.LogsDir` (`HeadlessPaths.cs:24`), exposed via `/api/logs/*` | Chat names, file names, download paths, error detail. A complete map of what the operator monitors and downloads. |
| **A6** | Queue database | `tad.db` (`HeadlessPaths.cs:23`, `Database.cs:22-29`) | Per-message metadata for every chat ever scanned. |
| **A7** | Host filesystem and the two bind mounts | `docker-compose.yml:25-28` | Integrity. Reachable as a write primitive — see F-02. |
| **A8** | Telegram account standing | — | Aggressive scanning triggers `FLOOD_WAIT` and account restriction. The app itself treats this as a first-class risk (`RateLimitThresholds`, `FloodWaitTracker`) — an attacker can weaponise it (F-15 note). |
| **A9** | Host CPU / disk / bandwidth | — | Unbounded downloads and torrent swarm participation. |

---

## 3. Actors

| ID | Actor | Capability | Notes |
|---|---|---|---|
| **AC1** | Unauthenticated network attacker | HTTP to `:8080` | In BD: anything on the Docker network, plus anyone the proxy lets through. In Variant P: the LAN/internet. |
| **AC2** | Authenticated dashboard operator | Every non-public endpoint | **There is exactly one privilege level.** Any credential compromise yields full authority (F-21). |
| **AC3** | Telegram-side content attacker | Can post a message/file into a monitored chat | *Requires no application credential at all.* For any monitored public channel or group, this is the general public. Controls: message text (→ plugin dispatch, `MessageTextFactoryService.cs:100-155`), file names, file sizes, magnet links. |
| **AC4** | Local user on the container host | Read the bind mounts | `docker-compose.yml:25-28` exposes `/data` at a host path; see F-07 on file modes. |
| **AC5** | Co-tenant process in the container | Read `/data` | Container runs as uid 1029 only because compose says so (`docker-compose.yml:11`); the image has no `USER` (F-16). |
| **AC6** | Release-channel attacker | Control GitHub release metadata/assets, or MITM the update fetch | Targets the WPF updater and the yt-dlp bootstrapper (F-09, F-10). |
| **AC7** | BitTorrent swarm peer | Inbound connections, DHT queries | Enabled by default when the Torrent plugin is turned on (F-11). |
| **AC8** | Same-site web attacker | Host content on another port/subdomain of the dashboard's host | Relevant because `SameSite` is site-scoped, not origin-scoped (F-14). |

---

## 4. Entry points

| ID | Entry point | Evidence | Pre-auth? |
|---|---|---|---|
| **E1** | `GET /api/web-auth/status` | `Program.cs:52-66`; allowlisted at `WebAuthService.cs:178-179` | **Yes** |
| **E2** | `POST /api/web-auth/login` | `Program.cs:68-81`; allowlisted at `WebAuthService.cs:180-181` | **Yes** |
| **E3** | Static dashboard (`/`, `/index.html`, `/app.js`, `/styles.css`) | `Program.cs:47-48`; `WebAuthService.cs:175-177` | **Yes** (and served *before* the auth middleware — F-17) |
| **E4** | 40+ authenticated JSON endpoints | `Program.cs:83-533` | No |
| **E5** | `GET /api/logs/stream` (SSE), `/api/logs/tail`, `/api/logs/search`, `/api/logs/files` | `Program.cs:291-357` | No |
| **E6** | Telegram update stream | `TelegramApp.cs:126` (`Client.OnUpdates`) | n/a — **AC3 reaches this with no app credential** |
| **E7** | `config.json` / `web-auth.json` / `.web-auth-secret` on disk | `ConfigStore.cs:29`, `WebAuthService.cs:187-199, 237-245` | n/a |
| **E8** | GitHub release API + asset download | `AutoUpdateService.cs:24, 59, 140`; `YtdlpService.cs:19-24, 69, 184` | n/a |
| **E9** | BitTorrent swarm (DHT, LPD, UPnP-mapped inbound port) | `TorrentDownloadService.cs:105-110, 273-282` | n/a |
| **E10** | `yt-dlp` subprocess, URL argument from Telegram text | `MessageTextFactoryService.cs:123`; `SocialMediaPlugin.cs:168`; `YouTubePlugin.cs:122` | n/a |
| **E11** | Environment variables | `WebAuthService.cs:50-51, 60, 141, 207, 211, 233`; `HeadlessPaths.cs:13, 18` | n/a |

---

## 5. Trust boundaries

```
                          ┌──────────────── TB1 ────────────────┐
  AC1 unauthenticated     │  reverse proxy (TLS)                │
  internet / LAN  ────────┤  → plaintext HTTP to :8080          │
                          └──────────────┬──────────────────────┘
                                         │ cookie: user|exp|HMAC, no Secure flag
                          ┌──────────────▼──── TB2 ─────────────┐
                          │  WebAuthMiddleware                  │  ← bypassable wholesale via
                          │  WebAuthMiddleware.cs:9-24          │    WEB_AUTH_ENABLED=false (F-08)
                          └──────────────┬──────────────────────┘
                                         │ single role, no CSRF token, no audit
                          ┌──────────────▼──── TB3 ─────────────┐
                          │  Minimal-API surface, Program.cs    │
                          └───┬──────────┬────────────┬─────────┘
                              │          │            │
         ┌────── TB4 ─────────▼──┐  ┌────▼──── TB5 ───▼───┐  ┌──── TB6 ─────────┐
         │ Telegram session A1   │  │ filesystem: /data,  │  │ subprocess:      │
         │ (full account authority)│  │ /downloads, bind   │  │ yt-dlp, ffmpeg,  │
         │ TelegramApp.cs:101-106│  │ mounts to the host  │  │ cmd.exe (WPF)    │
         └───────────┬───────────┘  └─────────┬───────────┘  └────────┬─────────┘
                     │                        │                       │
         ┌────── TB7 ▼───────────┐            │            ┌───── TB8 ▼─────────┐
         │ AC3: the public, via  │            │            │ AC7: torrent swarm │
         │ any monitored chat    │            │            │ inbound + DHT + UPnP│
         └───────────────────────┘            │            └────────────────────┘
                                   ┌──── TB9 ─▼──────────┐
                                   │ host OS / other     │
                                   │ containers (AC4/AC5)│
                                   └─────────────────────┘
```

### Boundary-by-boundary verdict

- **TB1 (network → app).** Weak. `Secure` is opt-in and defaults off (`WebAuthService.cs:141`); no
  HTTPS redirection, no HSTS, no security headers, no CORS policy anywhere in `Program.cs`. The
  app cannot tell whether it is behind TLS.
- **TB2 (anonymous → authenticated).** The weakest boundary in the system, and the one that
  matters most. Shipped default credentials (`docker-compose.yml:16-17`), a pre-auth oracle that
  *announces* those defaults are active (`Program.cs:63-64`), unsalted SHA-256 password storage
  (`WebAuthService.cs:248-249`), no rate limiting, no lockout, no failed-login log line
  (`Program.cs:76-78`), and a stateless non-revocable cookie (`WebAuthService.cs:151-171`).
- **TB3 (authorisation).** **Does not exist.** One implicit role. `WebAuthMiddleware` is binary:
  session or no session (`WebAuthMiddleware.cs:15-19`). No action is attributed to anyone.
- **TB4 (app → Telegram session).** No boundary. Any authenticated request runs with the full
  authority of A1. There is no confirmation step, re-auth, or scoping for session-affecting
  operations such as `POST /api/login/logout` (`Program.cs:150-154`).
- **TB5 (app → filesystem).** **Broken, by design.** Both the download root
  (`Program.cs:179-183`) and the per-chat folder template (`Program.cs:274`) are unvalidated
  client strings that reach `Directory.CreateDirectory` / `File.Move`
  (`FolderTemplateHelper.cs:40-87, 103-107` → `BaseMessage.cs:264-271, 145`). `FolderTemplateHelper`
  *documents* absolute paths as a supported feature (`FolderTemplateHelper.cs:29-32`). This
  converts TB2 compromise into arbitrary file write — including over A2, which closes a
  persistence loop back through TB2.
- **TB6 (app → subprocess).** Attacker-influenced. The URL argument is a raw line of Telegram
  message text (`MessageTextFactoryService.cs:100-123`). The host-side allowlist is correct in
  `SocialMediaPlugin.cs:44-59` and **incorrect** in `YouTubePlugin.cs:22` (raw `StartsWith`).
- **TB7 (Telegram content → app).** Systematically under-defended, and the boundary most often
  forgotten because it needs no credential. AC3 controls regex subject strings
  (`BaseMessage.cs:291-294`), file names (`BaseMessage.cs:252-262`), file sizes with no maximum
  (`BaseMessage.cs:280-289` gates only a *minimum*), magnet links, and plugin-dispatch URLs.
- **TB8 (swarm → host).** Widened deliberately: `AllowPortForwarding`, `AllowLocalPeerDiscovery`
  (`TorrentDownloadService.cs:275-276`), `AllowDht`, `AllowPeerExchange`
  (`TorrentDownloadService.cs:108-109`).
- **TB9 (container → host).** Thin. No `USER` in the `Dockerfile`; uid 1029 comes only from
  compose (`docker-compose.yml:11`). No `read_only`, `cap_drop`, `no-new-privileges`, or resource
  limits. Base images and apt packages unpinned (`Dockerfile:4, 32, 34-37`). No explicit file mode
  is ever set on secret files — `SetUnixFileMode` appears nowhere in the repository.

---

## 6. STRIDE per boundary

### TB1/TB2 — network → authenticated app

| STRIDE | Threat | Verdict | Finding |
|---|---|---|---|
| **S**poof | Log in with shipped `admin`/`changeme`, confirmed pre-auth by `/api/web-auth/status` | **Confirmed, default-reachable** | F-01 |
| **S**poof | Forge a cookie after reading `.web-auth-secret` | Confirmed once A2 is readable | F-02, F-07 |
| **S**poof | Offline crack of unsalted SHA-256 hash | Confirmed | F-03 |
| **S**poof | Online brute force / credential stuffing | Confirmed — no limiter, no lockout | F-05 |
| **T**amper | CSRF on cookie-authenticated state-changing endpoints | **Partly wrong lead** — no defense exists, but `SameSite=Lax` + JSON-only binding blocks the classic cross-site case; exploitable for AC8 | F-14 |
| **R**epudiate | No login success/failure log, no action audit trail | Confirmed | F-05, F-21 |
| **I**nfo | `defaultPassword` / `mustChangePassword` leaked pre-auth | Confirmed | F-01 |
| **I**nfo | Cookie in cleartext (`Secure` off by default) | Confirmed | F-06 |
| **D**oS | Unbounded login attempts; unbounded SSE connections; whole-file log scans | Confirmed (authenticated for the latter two) | F-05, F-19 |
| **E**oP | `WEB_AUTH_ENABLED=false` removes the boundary entirely | Confirmed (misconfiguration only) | F-08 |

### TB3/TB4/TB5 — authenticated app → session, filesystem

| STRIDE | Threat | Verdict | Finding |
|---|---|---|---|
| **T**amper | Arbitrary file write via `folderTemplate` (`..` and absolute paths) or `download-folder` | **Confirmed by code path, end to end** | F-02 |
| **T**amper | Overwrite `web-auth.json` / `.web-auth-secret` → permanent auth control | Confirmed as a consequence of F-02 | F-02 |
| **I**nfo | Exfiltrate A1 by pointing the download root at a location the attacker can read | Confirmed | F-02 |
| **I**nfo | Log endpoints stream chat names, file names, paths | Confirmed (authenticated) | F-19 |
| **R**epudiate | Password change, folder change, logout — none logged with an actor | Confirmed | F-21 |
| **E**oP | Session survives password change and logout | Confirmed | F-04 |
| **D**oS | `POST /api/login/logout` destroys `session.dat` (`TelegramApp.cs:149`) — one request, re-verification by SMS required | Confirmed | F-21 |

### TB6/TB7 — Telegram content and subprocesses (AC3: no credential required)

| STRIDE | Threat | Verdict | Finding |
|---|---|---|---|
| **T**amper | SSRF: `https://youtu<anything>` passes `CanHandle` and is fetched by yt-dlp | Confirmed | F-12 |
| **T**amper | Argument injection into the yt-dlp command line | **Unverified** — `ManuHub.Ytdlp.NET` internals not readable in this environment | F-26 |
| **T**amper | Path traversal via a Telegram-supplied file name | **Lead-adjacent and FALSE** — `Path.GetInvalidFileNameChars()` includes `/` on Linux and `/` + `\` on Windows (`BaseMessage.cs:252-262`) | §7 |
| **D**oS | ReDoS on a catastrophic regex with no `MatchTimeout` | Confirmed (no timeout anywhere in the engine) | F-13 |
| **D**oS | Unhandled `ArgumentException` from an invalid regex pattern | Confirmed — `new Regex(pattern)` with no try/catch | F-13 |
| **D**oS | Disk exhaustion: no maximum file size, no free-space check | Confirmed | F-15 |
| **D**oS | Disk amplification via a large magnet link | Confirmed | F-11, F-15 |
| **I**nfo | DHT/LPD announce the host IP and what it is downloading | Confirmed | F-11 |

### TB8/TB9 — swarm and container

| STRIDE | Threat | Verdict | Finding |
|---|---|---|---|
| **T**amper | Unpinned base image / apt packages → unreproducible, silent dependency change | Confirmed | F-16 |
| **E**oP | Image defaults to root; only compose downgrades the uid | Confirmed | F-16 |
| **I**nfo | Secrets at default umask, shared into a host bind mount | Confirmed (no `SetUnixFileMode` in repo) | F-07 |
| **D**oS | UPnP maps an inbound port on the router from inside the LAN | Confirmed | F-11 |
| **D**oS | No `mem_limit` / `cpus` / `pids_limit` | Confirmed | F-16 |

### Update / supply chain (AC6, WPF only)

| STRIDE | Threat | Verdict | Finding |
|---|---|---|---|
| **T**amper | Installer executed with no signature or hash verification | Confirmed | F-09 |
| **T**amper | GitHub-supplied `AssetName` → temp path → interpolated into a `.bat` run by `cmd.exe` | Confirmed (injection + traversal primitive) | F-09 |
| **T**amper | `yt-dlp.exe` / `ffmpeg.exe` downloaded and executed with no verification | Confirmed | F-10 |
| **E**oP | Predictable `.bat` path in `%TEMP%`, write-then-execute window | Confirmed mechanism, **no privilege boundary crossed** on a default Windows profile | F-09 |

---

## 7. Leads that turned out to be wrong or overstated

Recording these so the remediation phase does not spend effort on them.

1. **`HeadlessLogReader.ResolveSafeFileName` traversal — the defense HOLDS.**
   `HeadlessLogReader.cs:42-49` applies `Path.GetFileName`, then requires the `headless` prefix and
   `.log` suffix, then requires `File.Exists` under `LogsDir`. On Linux, `../../etc/passwd` reduces
   to `passwd` (rejected by the prefix check) and `..\..\x` stays intact as a single segment (also
   rejected). No bypass found. Not a finding.

2. **CSRF — no defense exists, but the classic attack does not work.** There is no antiforgery
   token, no `Origin`/`Referer` check, and no custom-header requirement anywhere. However
   `SameSite=Lax` (`WebAuthService.cs:140`) suppresses the cookie on cross-site `POST`/`PATCH`/
   `DELETE`, and every state-changing handler binds a JSON body, which forces a CORS preflight that
   is never answered. No state-changing `GET` exists (verified across `Program.cs:120-533`).
   Downgraded to Medium and scoped to AC8 — see F-14. Do not treat this as "trivially exploitable".

3. **Log hygiene — no evidence of secret leakage.** I searched for logging of phone numbers, 2FA
   passwords, `ApiHash`, and `BotToken`. The only credential-adjacent log lines are
   `WebAuthService.cs:66` and `:127`, which log a username only. `LoginCoordinator.cs:93` logs the
   exception, not the submitted value. **The lead is not confirmed.** Residual **[INFERRED]** risk:
   WTelegramClient's own internal logger is never wired to Serilog (no match for
   `WTelegram.Helpers.Log` anywhere), so its default sink is unmanaged — worth a follow-up, but it
   is not a finding I can evidence.

4. **`.env` gitignore claim in the README is TRUE.** `git check-ignore -v` resolves `.env` to
   `.gitignore:8` and `session.dat` to `.gitignore:30` (`*.dat`). No secret is currently committed.
   Minor gap only: `web-auth.json`, `.web-auth-secret`, and `config.json` are not ignored (F-25).

5. **Telegram-supplied file names cannot traverse.** `BaseMessage.cs:252-262` strips every
   `Path.GetInvalidFileNameChars()`, which includes the separators on both platforms. The traversal
   primitive is the **folder template** (F-02), not the file name.

6. **SSRF is not wide open.** `SocialMediaPlugin.cs:44-59` does host parsing and allowlist matching
   correctly, including the `www.` strip and the `EndsWith("." + d)` suffix check. Only
   `YouTubePlugin.cs:22` is broken. Scope F-12 to the YouTube plugin.

7. **Stored XSS in the dashboard is mostly handled.** `app.js:603` escapes `& < > "` and is applied
   to every Telegram-derived string I traced (chat name, file name, error text, log lines). Two
   cosmetic gaps remain with no attacker-controlled source today (F-24). The real gap is the absence
   of a CSP to contain any future regression (F-18).

8. **The session-cookie HMAC itself is sound.** `WebAuthService.cs:151-171` re-canonicalises the
   payload from the parsed `user` and `long expires` before verifying, so there is no delimiter or
   truncation ambiguity; HMAC-SHA256 is not length-extendable and the comparison is constant-time
   (`WebAuthService.cs:257-262`). The defect is **what the payload omits** — any credential
   generation counter — not how it is signed. See F-04.

---

## 8. Attack-path summary (highest-value chains)

**Chain 1 — remote to total Telegram account takeover. Default-reachable.**
`GET /api/web-auth/status` → `defaultPassword:true` (F-01) → `POST /api/web-auth/login`
`admin`/`changeme` → `POST /api/settings/download-folder` with a path the attacker can read, or a
`folderTemplate` of `../../TelegramAutoDownload` (F-02) → any monitored chat's next download lands
beside or on top of `session.dat` / `web-auth.json` / `.web-auth-secret` → forge cookies forever
(F-02, F-07) and exfiltrate A1 → **full Telegram account compromise**.

**Chain 2 — no credential required. Default-reachable.**
AC3 posts a very large file (or, if the Torrent plugin is on, a magnet link) into any monitored
public channel (F-15, F-11) → unbounded write to the `/downloads` bind mount → host disk
exhaustion → the SQLite queue and the log volume fail with it.

**Chain 3 — cookie theft with no revocation. Default-reachable.**
Plaintext hop or a same-site attacker captures the cookie (F-06, F-14) → 7 days of full access
(F-04). Changing the password does not help; neither does clicking Sign out (`WebAuthService.cs:148`
deletes the client cookie only). The only remedy is deleting `.web-auth-secret` and restarting,
which is documented nowhere.

**Chain 4 — supply chain into the WPF desktop. Default-reachable for product B.**
AC6 controls a GitHub release → the startup update check downloads the asset with no signature or
hash check and runs it silently (F-09); the asset name also flows unsanitised into a generated
`.bat` executed by `cmd.exe` (F-09) → **SYSTEM-adjacent code execution on every desktop install**.

---

## 9. ASVS L2 gate summary

| ASVS L2 chapter | Verdict | Evidence / finding |
|---|---|---|
| V1 Architecture | **Fail** | No documented trust boundaries; one implicit role (F-21) |
| V2 Authentication | **Fail** | 2.1.7 breached (default password, F-01); 2.4.1 breached (no approved KDF, F-03); 2.2.1 breached (no anti-automation, F-05) |
| V3 Session Management | **Fail** | 3.3.1/3.3.2 breached (no server-side invalidation, F-04); 3.4.1 breached (`Secure` off, F-06) |
| V4 Access Control | **Fail** | 4.1.3 — no least privilege; a single role holds A1 (F-21) |
| V5 Validation / Injection | **Fail** | 5.3.9 breached (arbitrary file write, F-02); 5.2.8 breached (untimed user regex, F-13); 5.2.6 breached (SSRF, F-12) |
| V7 Logging | **Fail** | 7.1.3/7.2.2 breached (no security event log, no audit trail, F-05, F-21) |
| V8 Data Protection | **Fail** | 8.3.4 breached (secrets at rest unprotected, F-07) |
| V9 Communications | **Fail** | 9.1.1 breached (no TLS enforcement, F-06) |
| V10 Malicious Code | **Fail** | 10.3.2 breached (unverified auto-update, F-09, F-10) |
| V12 Files | **Fail** | 12.3.1/12.3.4 breached (F-02); no size quota (F-15) |
| V13 API | **Partial** | JSON binding is sound and SQL is fully parameterised (`MediaRepository.cs`, Dapper throughout — **no SQL injection found**), but 13.2.3 (CSRF) has no defense (F-14) |
| V14 Configuration | **Fail** | 14.2.1 unpinned dependencies; 14.4.x no security headers; 14.1.x container hardening (F-16, F-18) |

**Overall: not ASVS L2 conformant.** The blocking cluster is V2/V3 (authentication and session) plus
V5/V12 (the file-write primitive). Those two clusters are what turn a remote request into loss of
asset A1, and they are the correct remediation entry point.

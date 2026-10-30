# Security Policy

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues, discussions, or
pull requests.**

Report vulnerabilities using **GitHub Private Vulnerability Reporting**: open the **Security**
tab of this repository → **Report a vulnerability**. This keeps the report private until a fix
is published.

### What to include

- The type of issue (credential exposure, authentication bypass, remote code execution, path
  traversal, SSRF, etc.).
- Affected version(s) and deployment surface (desktop, Docker image, or both).
- Step-by-step reproduction instructions and any special configuration required.
- Proof-of-concept or exploit code, if you have it.
- The impact, including how an attacker might exploit it.

### What to expect from us

| Stage | Target |
|---|---|
| Acknowledgement of your report | **3 business days** |
| Initial assessment and severity triage | **10 business days** |
| Status updates while we work | At least every **14 days** |
| Fix released for critical severity | **30 days** from triage, where technically feasible |
| Fix released for high severity | **90 days** from triage |
| Public disclosure | Coordinated with you, normally after a fix ships |

We do not currently operate a paid bug bounty programme.

## Scope

### In scope

- Application source code in this repository.
- The published Docker image and Windows installer/portable distribution.
- The update mechanism and web dashboard shipped with the headless service.

### Out of scope

- **Telegram's platform and API** — report to Telegram via <https://core.telegram.org/>.
- **Third-party components** (WTelegramClient, MonoTorrent, yt-dlp, ffmpeg, .NET runtime).
  Tell us if we ship a vulnerable version so we can update.
- Findings that require an attacker to already have **Administrator/root on the host** running
  the app.
- Missing hardening with no demonstrated impact, scanner-only reports, and social engineering.

### Known design limitations

These are intentional properties of the software, not undisclosed flaws:

- The app stores a **Telegram session credential** on the host; protect the data directory and
  backups accordingly.
- The **web dashboard is for trusted networks** — use a strong `WEB_PASSWORD` and preferably
  HTTPS via a reverse proxy if exposed beyond localhost/LAN.
- **Automating a Telegram account** may lead to rate limits or account action by Telegram.

## Safe harbour

Good-faith research that follows this policy will not be met with legal action. Do not access
other people's data, do not disrupt services, and give us reasonable time to fix before public
disclosure.

## Supported versions

| Version | Supported |
|---|---|
| Latest release | Yes |
| Previous minor release | Security fixes only |
| Older | No |

Security fixes ship in patch releases. Advisories are published via
[GitHub Security Advisories](https://github.com/kesavananbu/TelegramAutoDownload/security/advisories).

## Security-relevant configuration

1. Set a **strong `WEB_PASSWORD`** before exposing the headless dashboard on any network.
2. Do not expose the dashboard to the public internet without TLS and additional access controls.
3. Protect **`session.dat`** and backups — possession equals account access for API purposes.
4. Keep the application and container image updated.
5. Prefer a **dedicated Telegram account** for automation where practical.

*Last updated: 2026-10-30*

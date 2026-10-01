# Security Policy

<!--
DRAFT — proposed for adoption at the repository root as /SECURITY.md.
Replace all [PLACEHOLDERS] before publishing. See ./README.md.
-->

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues, discussions, or
pull requests.**

Report vulnerabilities in one of these ways:

1. **Preferred — GitHub Private Vulnerability Reporting.** Use the **Security** tab of this
   repository → **Report a vulnerability**. This keeps the report private until a fix is
   published and gives you credit automatically.
2. **Email** — [SECURITY EMAIL]. If you wish to encrypt your report, our PGP key is available
   at [WEBSITE]/pgp.txt (fingerprint: `[PGP FINGERPRINT]`).

### What to include

The more of this you can provide, the faster we can act:

- The type of issue (for example: credential exposure, authentication bypass, remote code
  execution, path traversal, SSRF).
- Affected version(s) and deployment surface — desktop application, Docker image, or both.
- Full paths of the source files related to the issue, and the affected code location
  (tag, branch, or commit).
- Step-by-step reproduction instructions, and any special configuration required.
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

If we disagree with your severity assessment, we will explain why rather than simply closing
the report.

## Scope

### In scope

- The application source code in this repository.
- The published Docker image.
- The Windows installer and portable distribution.
- The update mechanism.
- The web dashboard shipped with the headless service.

### Out of scope

- **Telegram's own platform and API.** Report those to Telegram via
  <https://core.telegram.org/> or their bug bounty.
- **Third-party components** — report to the upstream project. This includes
  WTelegramClient, MonoTorrent, yt-dlp, ffmpeg, and the .NET runtime. Do tell us if we are
  shipping a vulnerable version so we can update it.
- **Findings that require an attacker to already have administrative access** to the host
  running the application. This software stores credentials for the operator's own account on
  the operator's own machine; an attacker with root or Administrator on that host has already
  won, and we do not treat that as a vulnerability in this software.
- Missing security headers or configuration hardening with no demonstrated impact.
- Reports generated solely by automated scanners with no analysis or proof of exploitability.
- Denial of service caused by deliberately misconfiguring your own instance.
- Social engineering of the maintainers or users.

### Known design limitations — please do not report these as vulnerabilities

These are documented, intentional properties of the software. We welcome discussion about
improving them, but they are not undisclosed flaws:

- **The application stores a Telegram session credential on the host.** This credential grants
  full access to the linked Telegram account. Protecting the host, the user account running
  the application, and any backups of its data directory is the operator's responsibility.
- **The web dashboard is designed for trusted local networks.** Exposing it directly to the
  public internet is not a supported configuration. If you choose to, place it behind a
  reverse proxy that enforces TLS and authentication.
- **Automating a Telegram account may result in that account being rate-limited, restricted,
  or permanently banned by Telegram.** This is a platform-policy consequence, not a software
  vulnerability. See our Terms of Service.

## Safe harbour

If you make a good-faith effort to comply with this policy during your research, we will:

- Consider your research authorised, and not pursue or support legal action against you.
- Work with you to understand and resolve the issue promptly.
- Credit you in the release notes and advisory, unless you prefer to remain anonymous.

Good faith requires that you:

- Test only against instances you own or have explicit written permission to test.
- **Do not access, download, modify, or retain any other person's data**, including Telegram
  messages, media, session files, or member lists.
- Do not degrade, disrupt, or damage any service or any other user's data.
- Do not use social engineering, physical attacks, or attacks against our own infrastructure
  or third-party services.
- Give us reasonable time to remediate before any public disclosure.
- Do not exploit the issue beyond the minimum necessary to demonstrate it.

We do not currently operate a paid bug bounty programme.

## Supported versions

| Version | Supported |
|---|---|
| Latest release | Yes |
| Previous minor release | Security fixes only |
| Older | No |

Security fixes are shipped in the next patch release. We publish advisories via
[GitHub Security Advisories](https://github.com/[ORG]/[REPO]/security/advisories) and in the
release notes.

## Security-relevant configuration guidance

Operators should:

1. **Change all default credentials before first use.** Never expose an instance with default
   credentials to any network.
2. **Do not expose the web dashboard to the public internet.** Keep it on a trusted LAN, or
   place it behind an authenticating reverse proxy with TLS.
3. **Protect the data directory and your backups.** It contains the Telegram session
   credential. Treat a backup of it as equivalent to a backup of your password.
4. **Run the container as a non-root user** and grant it only the volumes it needs.
5. **Keep the application updated.** Updates include third-party component fixes.
6. **Use a dedicated Telegram account** where practical, rather than your primary personal
   account.

---

*Last updated: [DATE] · Contact: [SECURITY EMAIL]*

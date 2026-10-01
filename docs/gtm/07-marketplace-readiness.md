# Distribution & Community Readiness (not commercial marketplace)

**Branch:** `enterprise-readiness`  
**Date:** 2026-10-01 (Composer 2.5 re-run)  
**Product direction:** **Free personal tool** — GitHub + Docker for homelab users. **Commercial marketplace, billing, and licence keys are out of scope.**

> Depends on `docs/compliance/06-compliance.md` and `docs/product/01-product-definition.md`.

**Evidence convention:** **[FACT]** / **[ASSESSMENT]**

---

## 1. Scope change

Prior drafts of this file assumed **self-hosted commercial software** (Paddle, licence files, NAS catalogues as sales channels). The product owner **dropped that goal**.

**This document now covers:**

- How free users **discover, install, and upgrade** the tool
- **Minimum packaging hygiene** (security, licences, Docker quality)
- What we **decline to pursue** (Store, SaaS, paid tiers)

**Removed from scope:** entitlement enforcement, merchant-of-record, pricing, Team tier gates, “blocking before taking money.”

---

## 2. Primary distribution channels

### 2.1 GitHub Releases (desktop) — primary for Windows

**[FACT]** Release process requires **both** `TelegramAutoDownload_vX.X.X_Setup.exe` and `_Portable.zip` (`.cursor/rules/release-process.mdc`).

| Requirement | Current | Recommendation |
|---|---|---|
| Licence grant | README says MIT; **no `/LICENSE`** | Add MIT + `NOTICE` in zip/installer |
| Installer EULA slot | `LicenseFile=` empty (`installer/setup.iss:22`) | Point at MIT or combined NOTICE |
| Code signing | Not configured in `setup.iss` | **Recommended** for SmartScreen — UX, not monetisation |
| Auto-update | GitHub Releases (`README.md`) | Keep; document verify checksums |

### 2.2 Container registry (headless) — primary for NAS/homelab

**[FACT]** `Dockerfile` builds single-stage runtime with `yt-dlp` + `ffmpeg` (`Dockerfile:35-36`); compose uses `:latest` (`docker-compose.yml:8`).

| Requirement | Current | Recommendation |
|---|---|---|
| **GPL ffmpeg** in published image | Present via `apt` | Remove or ship GPL compliance bundle (`06-compliance.md` §1.3) |
| Multi-arch (`amd64` + `arm64`) | Not evidenced in Dockerfile | **High value** for Synology/QNAP/RPi |
| Non-root default | `user:` only in compose (`docker-compose.yml:11`) | Add `USER` in Dockerfile |
| Semver tags | `:latest` only in example compose | Publish `vX.Y.Z` tags; document pin-by-digest |
| HEALTHCHECK | Absent | Add against `:8080` |
| Default credentials | `admin` / `changeme` in compose example | **Remove** — env required |
| SBOM / scan | CI may exist (see `.github/workflows/`) | Publish SBOM + scan results on release — trust signal |

**[ASSESSMENT]** Docker Hub or GHCR is sufficient; no paid marketplace listing required.

### 2.3 Community templates (optional, unpaid)

Unraid Community Applications, Synology compose guides, TrueNAS compose — **nice-to-have** community contributions, not a gating commercial channel.

| Channel | Effort | Value for free project |
|---|---|---|
| **Documented `docker-compose` + env reference** | Low | **Do first** |
| Unraid CA template (community PR) | Low–medium | Visibility |
| SynoCommunity SPK | High | Defer unless maintainer capacity |

**[ASSESSMENT]** Do not block releases on catalogue approval.

---

## 3. Explicitly declined channels

| Channel | Why declined |
|---|---|
| **Vendor-hosted SaaS** | Telegram non-sublicensable licence + session custody (`06-compliance.md` §3) |
| **Microsoft Store / MSIX** | yt-dlp/exec pattern, self-update conflict, Telegram §4.2 store escalation risk |
| **Paid NAS “official” partnerships** | No commercial product to sell |
| **Licence keys / Paddle / Lemon Squeezy** | Product is free |

---

## 4. Onboarding (free user)

**Goal:** Zero to first successful download with **secure defaults**.

| Step | Today | Target |
|---|---|---|
| Telegram `api_id` / hash | User obtains from my.telegram.org | Unchanged; document clearly |
| Login / session | `session.dat` on volume | Document backup |
| Web dashboard auth | Default creds in compose example | **Force** user-defined password before listen |
| Ban risk | README only | First-run acknowledgement modal |
| Telegram API relationship | Partial | In-app + README disclosure (API ToS §2.2) |

**[ASSESSMENT]** Time-to-first-download is a **quality metric**, not a funnel KPI.

---

## 5. Telemetry & privacy

**[ASSESSMENT]** For a free self-hosted tool, default posture:

- **No telemetry** unless explicitly added later
- If added: **opt-in**, no chat/content paths, no licence/customer linkage, inspectable payload, fail-open

Crash reports: separate opt-in; scrub paths from stacks.

---

## 6. Support & security disclosure

| Surface | Free-project expectation |
|---|---|
| **Issues / discussions** | GitHub |
| **`SECURITY.md`** | Publish at root (draft in `docs/compliance/drafts/SECURITY.md`) |
| **Diagnostics** | Optional export bundle (logs redacted) — future enhancement |
| **Version policy** | Semver tags; breaking changes called out in release notes |

No paid support SLA.

---

## 7. Readiness checklist (free release)

**Legal / trust**

- [ ] `/LICENSE` (MIT) matches README
- [ ] `/NOTICE` in repo, installer, and/or image
- [ ] `/SECURITY.md` published
- [ ] Product rename / Telegram disclosure plan executed or tracked
- [ ] Docker GPL ffmpeg: remove or comply

**Security (see security findings doc)**

- [ ] No default web credentials in shipped examples
- [ ] Web auth hardening shipped or roadmap-visible

**Distribution quality**

- [ ] Semver container tags
- [ ] Documented backup/restore for `/data` (session + SQLite)
- [ ] Optional: arm64 image, HEALTHCHECK, `USER`

**Not required for free v1**

- [ ] ~~Licence-key gate~~
- [ ] ~~Paddle webhook~~
- [ ] ~~Microsoft Store package~~
- [ ] ~~Multi-user Team tier~~

---

## 8. Sources

`Dockerfile`, `docker-compose.yml`, `installer/setup.iss`, `README.md`, `.cursor/rules/release-process.mdc`, `docs/compliance/06-compliance.md`, `docs/product/01-product-definition.md`.

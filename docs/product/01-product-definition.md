# Product Definition — Free Personal Tool

**Branch:** `enterprise-readiness`  
**Date:** 2026-10-01 (Composer 2.5 re-run)  
**Decision:** **No commercial product.** Ship an **excellent, secure, free** personal media archiver for self-hosted use (NAS / home server / desktop).

> Legal constraints that bound features and naming are in `docs/compliance/06-compliance.md`. Marketplace, pricing, and licence-key tiers are **explicitly out of scope**.

**Evidence convention:** **[FACT]** — in-repo citation. **[ASSESSMENT]** — judgement.

---

## 1. What the product is

**[FACT]** One codebase, two surfaces:

| Surface | Role | Distribution |
|---|---|---|
| **WPF desktop** (`TelegramAutoDownload/`) | Interactive Windows client | GitHub Releases — Setup.exe + Portable ZIP (`.cursor/rules/release-process.mdc`) |
| **Headless + web UI** (`TelegramAutoDownload.Headless/`) | Unattended daemon | Docker / `docker-compose.yml` |

**[FACT]** Compose volume paths use Synology-style `/volume1/...` and UID `1029:100` (`docker-compose.yml:11,26-27`) — strong signal the **primary happy path** is Docker on a NAS, not a sleeping desktop PC.

**[ASSESSMENT]** Product promise in one sentence:

> **Keep a deduplicated, organised local copy of media from Telegram chats you already belong to — on your own hardware, without sending your account to anyone else.**

Capabilities (from `README.md`): per-chat monitoring, filters, folder templates, dedup, resume, history sync, quiet hours, plugins (Telegram direct, yt-dlp, torrent), JSONL history, member export, auto-update (desktop).

---

## 2. Who it is for (single primary segment)

### Primary — Individual power user / homelab operator

**Who:** Technically comfortable people who treat Telegram channels/groups as media feeds and already run a NAS, Plex/Jellyfin, or \*arr stack.

**Jobs to be done:**

- “Mirror these channels to my library 24/7.”
- “Backfill what I missed before I joined.”
- “Don’t duplicate files or blow my disk budget.”
- “Run in Docker on the box that’s always on.”

**Deployment expectation:** Self-hosted only. Docker on NAS or small server; optional Windows desktop for setup-heavy users.

**Willingness to pay:** **N/A** — product is free. Success is measured by **adoption, reliability, and trust**, not ARR.

**[ASSESSMENT]** Segments previously labelled “Team” or “Enterprise archival” are **not targets**. Organisations may still run the software, but they inherit **controller obligations** under GDPR and Telegram terms themselves (`06-compliance.md` §3.2). Do not promise compliance certifications, SSO, audit, or indemnity.

---

## 3. Positioning & naming

**[ASSESSMENT]** Recommended public framing:

- **“Personal media archiver”** — not “enterprise-ready,” not “marketplace.”
- **Avoid “Telegram” in the product title** — API ToS §2.3 (`README.md:1`, `installer/setup.iss:1` still violate this today).
- Lead with **privacy/self-hosting**, not feature breadth (torrent, URL ripping, member export carry legal/security weight — see compliance doc).

**Non-goals (product owner decision, 2026-10-01):**

- Paid tiers, licence keys, Paddle/Stripe
- Vendor-hosted SaaS or demo instances on real accounts
- Microsoft Store / paid app catalogue as a launch channel
- “Enterprise-ready” marketing or SOC2 programme

---

## 4. Quality bar — “excellent and secure”

Success criteria replace commercial gates:

| Dimension | Target |
|---|---|
| **Security** | No default credentials; strong web auth; path safety; CSRF/rate limits per `docs/security/04-findings.md` |
| **Reliability** | Resume/crash-safe downloads; documented backup of `session.dat` + DB |
| **Operability** | Clear Docker docs, health signal, semver tags, optional ARM64 |
| **Legal hygiene** | Root MIT + `NOTICE`; GPL ffmpeg handled if image published |
| **Honesty** | README matches behaviour; Telegram/ban risks disclosed in-product |

**[ASSESSMENT]** Prioritised gaps (value for free users):

| Priority | Gap | Rationale |
|---|---|---|
| P0 | **`LICENSE` + `NOTICE` + `SECURITY.md`** | Trust and redistribution (`06-compliance.md` §1.1) |
| P0 | **Rename / Telegram disclosure / ban acknowledgement** | Platform terms + user safety |
| P0 | **Remove compose default passwords; harden web auth** | Security goal |
| P1 | **Rate-limit ceilings (not just warnings)** | Protects users’ Telegram accounts |
| P1 | **Member export hardening or phone removal** | Privacy/terms (`06-compliance.md` §3.3) |
| P1 | **Torrent / URL plugins off-by-default + docs** | User legal risk |
| P2 | **Docker: non-root `USER`, HEALTHCHECK, arm64** | Homelab quality |
| P2 | **Retention/TTL optional** | Power users; GDPR-aware operators |
| P3 | **Code signing for Windows** | SmartScreen UX, not monetisation |

Deliberately **deprioritised** vs prior commercial plan: multi-user RBAC, audit log, licence enforcement, NAS catalogue partnerships as **revenue channels**, Paddle integration.

---

## 5. Deployment model

**[ASSESSMENT]** All users are self-hosted:

| Concern | Expectation |
|---|---|
| Data location | User disk only |
| Credentials | User’s Telegram session + their `api_id` |
| Updates | GitHub Releases / pinned container tags |
| Support | Community (issues, docs) — no SLA |
| Telemetry | **Off by default** if ever added; no phone-home requirement |

---

## 6. Feature boundaries (aligned with compliance)

| Feature | Stance |
|---|---|
| Core Telegram media download | **Core product** |
| JSONL history | **Keep** — document user duties |
| Member export | **Restrict** — see compliance |
| yt-dlp / social URLs | **Opt-in** — user installs tools |
| Torrent | **Off by default or remove** |
| Web dashboard | **Keep** — must be secure by default |

---

## 7. Relation to other docs

- **`docs/compliance/06-compliance.md`** — legal facts and surviving risks  
- **`docs/gtm/07-marketplace-readiness.md`** — renamed scope: **distribution & community**, not commercial marketplace  
- **`docs/security/04-findings.md`** — security work tracks “excellent and secure”  
- **`docs/roadmap/08-scorecard-and-roadmap.md`** — should be read with **free-tool** lens (may still mention legacy commercial items until refreshed)

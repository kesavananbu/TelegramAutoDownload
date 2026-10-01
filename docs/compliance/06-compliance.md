# Licensing, Platform Terms, Data Protection & Content Liability

**Branch:** `enterprise-readiness`  
**Date:** 2026-10-01 (Composer 2.5 re-run)  
**Product direction:** **Free personal tool** — excellent, secure, self-hosted. No commercial sale, no marketplace, no licence-key business model.

> **This document is not legal advice.** Items marked **[LEGAL QUESTION]** need qualified counsel if you expand scope (paid distribution, entity formation, app-store submission).

**Evidence convention:** **[FACT]** — verified at cited `path:line` or cited primary source. **[ASSESSMENT]** — engineering judgement. **[LEGAL QUESTION]** — for counsel.

---

## 0. Executive summary — what still matters for a free tool

Dropping the commercial goal **removes** most marketplace, billing, EULA, and licence-enforcement analysis. It does **not** remove:

| Area | Still applies? | Why |
|---|---|---|
| **MIT / missing `LICENSE`** | **Yes — fix first** | README promises MIT; no grant exists; dependency redistribution obligations are unmet |
| **Telegram API & content terms** | **Yes** | Naming, disclosure, automation risk, account bans — user bears enforcement, but the project should not mislead |
| **GDPR (household vs business use)** | **Partially** | Self-hosted: user is controller; household exemption may apply for genuine personal use |
| **Member export & third-party data** | **Yes** | Highest privacy/terms exposure in the codebase regardless of price |
| **Torrent / generic URL plugins** | **Yes (risk to users)** | Free status helps on *Grokster*/*inducement* margin; §1201 and P2P distribution facts remain |
| **Docker GPL ffmpeg** | **Yes if you publish the image** | Copyleft follows **distribution**, not whether you charge money |
| **Web auth defaults & hardening** | **Yes** | Security quality goal, not “enterprise questionnaire” |

**Top operational legal notes (action-oriented):**

1. **Add a root `MIT` (or chosen) `LICENSE` file** and ship **`NOTICE`** with releases/installer/image — closes the grant gap and satisfies upstream MIT/Apache attribution (`README.md:341-343`; no root `LICENSE` — verified absent).
2. **Document Telegram relationship** — rename or prefix per API ToS §2.3; in-app/API disclosure per §2.2; ban-risk acknowledgement for users (`installer/setup.iss:1`, `README.md:1`).
3. **Treat member export as opt-in with warnings** — or remove phones/vCard (`TelegramClient/TelegramApp.cs` member export path; see §3.4).
4. **GPL compliance for published Docker images** — remove `ffmpeg` from the image or ship licence text + source offer (`Dockerfile:35-36`).
5. **No default web credentials** in examples — force first-run setup (`docker-compose.yml:16-17`).

---

## 1. Licensing audit

### 1.1 The MIT / LICENSE gap

| Claim | Verification | Status |
|---|---|---|
| README states MIT | `README.md:341-343` — `## License` + `MIT` | **[FACT]** |
| Root `LICENSE` exists | No `LICENSE`, `LICENSE.txt`, or `COPYING` at repository root | **[FACT] — absent** |
| `SECURITY.md` at root | Absent (draft in `docs/compliance/drafts/SECURITY.md`) | **[FACT]** |
| Third-party notices in releases | Not bundled in installer (`installer/setup.iss:22` empty `LicenseFile=`) | **[FACT]** |
| Draft MIT text | `docs/compliance/drafts/LICENSE` — Option C (MIT) for adoption | **Draft** |

**[ASSESSMENT]** For a **free** project, the gap is not “MIT vs proprietary” — it is **promise vs grant**. Without a `LICENSE` file, conservative interpretation is **all rights reserved** while README implies MIT. That confuses contributors, packagers (Unraid/Synology templates), and anyone redistributing GitHub Releases.

**[ASSESSMENT]** Adopting MIT deliberately (full text at `/LICENSE`, copyright line, optional `NOTICE`) aligns with the stated product goal. Prior releases that said “MIT” without text are best treated as **intent to license under MIT**; counsel can confirm if any release needs a one-time clarification note.

**[LEGAL QUESTION 1]** Do any third-party contributions require explicit permission before publishing MIT for the aggregate work? Run contributor inventory if unsure (`06-compliance.md` historical LQ 2).

### 1.2 Managed dependencies (NuGet)

**[FACT]** All shipped NuGet dependencies in the solution are **MIT** or **Apache-2.0** (see prior inventory in assessment baseline / `docs/compliance/drafts/NOTICE.md`). No copyleft in managed code.

**Operational notes for a free MIT project:**

- **Apache-2.0 (Serilog, Dapper):** preserve copyright/NOTICE in `NOTICE` when distributing binaries.
- **FluentAssertions 6.x:** stay pinned below v8 (commercial licence) in test projects — dev-only, not shipped.
- **Ytdlp.NET 3.0.4:** deprecated on NuGet — supply-chain hygiene, not licence block.

### 1.3 Bundled / runtime binaries — copyleft still matters

#### yt-dlp

| Surface | Mechanism | Effective licence | Who distributes |
|---|---|---|---|
| Windows | Runtime download (`TelegramAutoDownload/Services/YtdlpService.cs`) | PyInstaller builds **GPLv3+** | **End user** (today) |
| Docker | `apt-get install yt-dlp` (`Dockerfile:35-36`) | Debian Python package **Unlicense** | **Publisher of image** |

**[FACT]** Bundled `tools/yt-dlp.exe` path exists in code if files are added to the installer — would make the **vendor/publisher** a GPLv3+ distributor. Policy: **do not bundle** PyInstaller yt-dlp in installer/releases.

#### ffmpeg

**[FACT]** Docker image installs Debian `ffmpeg` (`Dockerfile:35-36`) → **GPL-2+** binaries in any **published** image.

**[ASSESSMENT]** Free vs paid does not change GPL §3 duties on **distribution**. Options unchanged:

- Best: operator-provided ffmpeg (document install), or separate optional layer.
- Acceptable: ship GPL text + written offer for corresponding Debian source (see `docs/compliance/drafts/NOTICE.md`).

Windows runtime download of GPL ffmpeg builds remains on the **user’s machine** today; prefer LGPL build if remux-only (`YtdlpService` gpl zip URL — see baseline).

### 1.4 Required artifacts (free project)

| Artifact | Purpose | Location |
|---|---|---|
| `LICENSE` (MIT) | Grant + contributor clarity | Root — **adopt from draft Option C** |
| `NOTICE` | Apache + GPL ffmpeg + yt-dlp + trademark disclaimer | Root + installer + image |
| `SECURITY.md` | Coordinated disclosure | Root — from draft |
| Privacy / ToS | Light touch for website/GitHub | Optional outlines in `docs/compliance/drafts/` |

---

## 2. Platform terms — Telegram

### 2.1 Applicable rules (summary)

Primary sources: [Telegram API ToS](https://core.telegram.org/api/terms), [Content Licensing ToS](https://telegram.org/tos/content-licensing).

**[FACT] High-signal clauses for this repo:**

- **§2.3** — App title must not include “Telegram” unless prefixed with “Unofficial”.
- **§2.2** — Users must know the app uses the Telegram API; prominent in description **and in-app intro**.
- **§3.3** — Channel clients must support official sponsored messages (not implemented).
- **§1.3** — Expectation that basic Telegram client features work — **structural mismatch** (automation archiver, not a client).
- **Content licensing** — Non-sublicensable, retractable licence; prohibits scraping/harvesting/aggregation beyond legitimate client operation; AI-training use prohibited.

### 2.2 Current compliance snapshot

| Clause | Current state | Verdict |
|---|---|---|
| §2.3 naming | **“Telegram Auto Download”** — `README.md:1`, `installer/setup.iss:1` | **[FACT] Likely violation** |
| §2.2 disclosure | README only; no dedicated in-app intro | **[ASSESSMENT] Partial** |
| §3.3 sponsored messages | Not supported | **[FACT] Violation if interpreted as client** |
| §1.3 full client | Not a messaging client | **[ASSESSMENT] Structural gap** |
| BYO `api_id` | User supplies credentials (`README.md`, `docker-compose.yml` comments) | **[ASSESSMENT] Standard pattern; user is API “developer”** |
| History JSONL / member export | Local export features | **[ASSESSMENT] “Harvesting” risk under content terms — user responsibility; project should disclose** |

### 2.3 Account ban risk (user operational legal note)

**[ASSESSMENT]** Automation can trigger `FLOOD_WAIT`, restrictions, or bans. Mitigations already in code (rate limiters, flood-wait handling — see `RateLimitThresholds.cs`, orchestrator). For a **free personal tool**, recommend:

- First-run **ban-risk acknowledgement** (not buried in README).
- **Sensible default caps** on aggressive rate settings (warnings alone are insufficient for “excellent and secure”).
- Document that **member enumeration** is high-risk behaviour.

**[LEGAL QUESTION 2]** Is JSONL export on the user’s own machine still “aggregation” under Telegram content licensing?

**[LEGAL QUESTION 3]** Is rename to neutral branding (no “Telegram” in product title) required vs “Unofficial …” prefix?

---

## 3. Data protection (self-hosted, personal tool)

### 3.1 Data inventory (abbreviated)

Crown jewels and sensitive categories unchanged from prior analysis — evidence paths:

- **`session.dat`** — full account access (`TelegramClient/TelegramApp.cs` session path logic; `Headless/HeadlessPaths.cs`).
- **Chat content & metadata** — JSONL (`ChatHistoryService.cs`, `HistoryEntry.cs`).
- **Member exports** — IDs, names, usernames, **phone** (`MemberEntry.cs`, `TelegramApp.cs` export, `MembersExportDialog.xaml.cs`).
- **Web credentials** — env/config (`WebAuthService.cs`; defaults in `docker-compose.yml:16-17`).
- **Logs** — optional Seq sink (`Logger/Logger.csproj`).

### 3.2 Controller / processor roles

**[ASSESSMENT]** For **personal self-hosted** use:

| Role | Who |
|---|---|
| Controller | **The individual user** (or their household) |
| Processor | **None** — no vendor-hosted processing |
| DPA (Art. 28) | **Not required** for the maintainer as software author |

**[ASSESSMENT]** **Household exemption (Art. 2(2)(c))** may cover purely personal archiving. **Business, newsroom, or compliance use** is not exempt — document in README/AUP that organisational use imposes GDPR duties on the operator, not on the project.

**Vendor-hosted SaaS** remains **out of scope** and **strongly discouraged** (non-sublicensable Telegram licence + session custody). Not part of product direction.

### 3.3 Member-list export — surviving risk

**[FACT]** Bulk export including phone numbers to CSV/vCard remains the **sharpest privacy/terms feature** in the tree (see §3.4 in prior doc — code paths unchanged).

**[ASSESSMENT]** For a free tool, **vendor** liability is lower than for a paid commercial product, but:

- Users can still violate GDPR as controllers.
- Regulators and Telegram enforcement still attach to **behaviour**, not price.

**Recommendations (engineering + docs, not legal guarantees):**

1. **Remove phone from export** or drop vCard; admin-only + explicit acknowledgement if feature stays.
2. **Never market** export as lead-gen; remove “with phone” prominence if it reads as a feature ad.
3. Document **purpose limitation** in user-facing security/privacy notes.

**[LEGAL QUESTION 4]** Does shipping the feature create maintainer liability beyond the user who clicks export?

### 3.4 Other gaps (security-aligned)

- **Default `admin` / `changeme`** in compose example — replace with “must set before expose” pattern (`docker-compose.yml:16-17`).
- **Seq to vendor infrastructure** — if ever used, maintainer could become processor; default off, customer-controlled only.

---

## 4. Content liability (free tool framing)

### 4.1 Baseline

**[ASSESSMENT]** *Sony* substantial-non-infringing-use framing fits **personal archiving of Telegram media the user already receives**. Charging money was an aggravating factor under *Grokster*; **free, non-inducement positioning** is materially better but not a shield for deliberate circumvention trafficking.

### 4.2 Generic URL / yt-dlp

**[FACT]** `SocialMediaPlugin` passes user-supplied URLs to yt-dlp (see `SocialMediaPlugin.cs`, README).

**[ASSESSMENT]** DMCA **§1201** anti-circumvention disputes (e.g. stream ripping) attach to **trafficking in tools**; risk is **lower** for a free OSS archiver that does not market ripping, but **not zero**. Operational notes:

- Document that users must comply with platform ToS and copyright.
- Avoid auto-fetching yt-dlp/ffmpeg where it looks like **affirmative distribution** of circumvention tools; prefer documented user install (aligns with security F-10 narrative).
- MIT source publication does not eliminate §1201 theories but changes enforcement calculus.

**[LEGAL QUESTION 5]** Maintainer exposure for orchestrating user-installed yt-dlp on a free project.

### 4.3 Torrent plugin

**[ASSESSMENT]** Automatic swarm participation from chat links is **distribution**, not mere download — high user legal and ISP notice risk. For “excellent and secure”:

- **Disable by default** or remove; require explicit per-item action.
- Document P2P upload behaviour clearly in README.

### 4.4 Feature posture (free personal tool)

| Feature | Recommended posture |
|---|---|
| Telegram media download (core) | Keep; rate limits + user disclosure |
| History JSONL | Keep; document retention as user responsibility |
| Member export | Restrict or harden (§3.3) |
| YouTube / social URL | Opt-in; user-supplied yt-dlp |
| Torrent | Off by default or remove |
| Web dashboard | No default passwords; auth hardening |

### 4.5 DMCA / notices

**[ASSESSMENT]** Self-hosted: maintainer does not host user content. Still useful to publish a **contact + triage** process (`docs/compliance/drafts/dmca-notice-and-takedown.md`) for GitHub/repo issues, not §512(c) safe harbour.

---

## 5. Document checklist (free project)

| Document | Status | Action |
|---|---|---|
| `/LICENSE` (MIT) | Missing | Copy from `docs/compliance/drafts/LICENSE` Option C after review |
| `/NOTICE` | Draft | Adopt + embed in release artifacts |
| `/SECURITY.md` | Draft | Publish at root |
| AUP / privacy / ToS | Outlines | Optional for GitHub; needed if public website collects data |
| In-app Telegram disclosure + ban modal | Spec in drafts | Implement when polishing UX |

---

## 6. Legal questions worth counsel (if scope grows)

1. Contributor rights and MIT adoption for the whole tree.
2. Telegram trademark/naming for a **free** GitHub project vs commercial store listing.
3. Member export and JSONL under Telegram content licensing (user-local only).
4. GPL-2 §3 compliance for Docker images (Debian source offer sufficiency).
5. §1201 exposure for yt-dlp orchestration in MIT OSS.

---

## 7. Key repository evidence

`README.md`, `Dockerfile`, `docker-compose.yml`, `installer/setup.iss`, `TelegramClient/TelegramApp.cs`, `TelegramClient/Models/MemberEntry.cs`, `TelegramClient/ChatHistoryService.cs`, `TelegramAutoDownload/Services/YtdlpService.cs`, `TelegramAutoDownload.Headless/WebAuthService.cs`, `TelegramAutoDownload.Headless/Scanning/RateLimitThresholds.cs`, all `.csproj` PackageReferences, `docs/compliance/drafts/NOTICE.md`.

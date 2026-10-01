# Phase 6 — Licensing, Platform Terms, Data Protection & Content Liability

**Author:** Wave 1, Agent F (read-only analysis)
**Branch:** `enterprise-readiness`
**Date:** 2026-09-30
**Scope:** Commercial and legal readiness of TelegramAutoDownload v2.9.3

> **This document is not legal advice.** The author is not a lawyer. Everything below is a
> commercial-risk assessment written to brief counsel efficiently. Every item marked
> **[LEGAL QUESTION]** must be answered by a qualified attorney before the product is sold.
> Section 6 collects those questions in one place.

**Evidence convention used throughout:**

- **[FACT]** — verified in this repository at the cited `path:line`, or quoted from a cited primary source.
- **[ASSESSMENT]** — the author's professional judgement, which is contestable.
- **[LEGAL QUESTION]** — genuinely unresolved; requires counsel.

---

## 0. Executive summary

TelegramAutoDownload is technically competent and commercially interesting, but in its
current shape it cannot be sold as-is. Four issues are structural rather than cosmetic:

1. **The product name itself violates Telegram's API Terms of Service** ([FACT], §2 below), and
   the licence Telegram grants for platform data is explicitly *non-sublicensable* — which is
   the sentence that kills any vendor-hosted SaaS model.
2. **There is no `LICENSE` file**, so `README.md` promises MIT while granting nothing
   ([FACT], §1.1). Separately, MIT is the *wrong* licence for the owner's stated commercial goal.
3. **The channel member-list export harvests third parties' phone numbers** with no consent,
   no notice, and no lawful basis ([FACT], §3.4).
4. **The torrent and arbitrary-URL features** put the vendor in the path of DMCA §1201
   anti-circumvention liability and BitTorrent distribution liability, neither of which has a
   safe harbour ([ASSESSMENT], §4).

Recommended commercial model: **self-hosted, licence-key gated, with the torrent feature
removed and the generic-URL feature restricted.** Rationale in `docs/gtm/07-marketplace-readiness.md`.

---

## 1. Licensing audit

### 1.1 The stated licence does not exist

| Claim | Verification | Status |
|---|---|---|
| `README.md` states the licence is MIT | `README.md:341-343` — a `## License` heading followed by the single word `MIT` | **[FACT]** |
| A `LICENSE` file exists | No `LICENSE`, `LICENSE.txt`, `LICENSE.md`, `COPYING`, or `NOTICE` file exists anywhere in the repository | **[FACT] — absent** |
| A `SECURITY.md` exists | Absent | **[FACT] — absent** |
| Privacy policy / ToS / AUP / third-party notices | Absent | **[FACT] — absent** |
| The installer presents a licence agreement | `installer/setup.iss:22` — `LicenseFile=` is present but **empty**, so no EULA is displayed during installation | **[FACT]** |

**[ASSESSMENT]** A bare "MIT" in a README with no licence text is legally ambiguous. The
conservative reading is that no licence has been granted at all: copyright defaults to
"all rights reserved," and every existing user of the GitHub releases is technically
unlicensed. The generous reading is that the README is an offer of MIT terms. Either way it
is unfit for a commercial product, and the ambiguity should be resolved deliberately rather
than by accident.

### 1.2 MIT is actively incompatible with the commercial goal

**[ASSESSMENT]** This is the point most likely to be missed. The MIT licence permits anyone
to "use, copy, modify, merge, publish, distribute, sublicense, and/or **sell** copies of the
Software." If the codebase is genuinely MIT, then:

- Any customer may lawfully resell the product, strip the licence-key check, and undercut the vendor.
- Any competitor may fork it, rebrand it, and sell it on the same NAS app catalogue.
- A licence-key entitlement system is contractually unenforceable against someone who
  simply exercises their MIT rights and deletes it.

MIT and "sell this as a commercial, enterprise-ready product" are mutually exclusive unless
the vendor intends open-core, where only a clearly separated proprietary layer is withheld.
The owner must pick one. A proposed proprietary EULA and an open-core alternative are drafted
in `docs/compliance/drafts/LICENSE`.

**[LEGAL QUESTION 1]** Given that GitHub releases have already been distributed under a
README claiming MIT, can the owner relicense future versions proprietarily, and what is the
status of versions already shipped? (Relicensing forward is normally possible if the owner
holds all copyright — but see Legal Question 2.)

**[LEGAL QUESTION 2]** Has any outside contributor's code been merged? Without a CLA,
relicensing another person's Apache/MIT-assumed contribution proprietarily requires their
permission. `git log --format='%aN' | sort -u` should be run to enumerate contributors.

### 1.3 Full NuGet dependency inventory

Every `PackageReference` in the repository, enumerated from all 11 `.csproj` files.
"Ships?" indicates whether the component is distributed to customers (test-only packages are not).

| Package | Ver | Declared in | License | Type | Ships? |
|---|---|---|---|---|---|
| WTelegramClient | 4.4.2 | `TelegramClient/TelegramClient.csproj:9` | **MIT** | Permissive | Yes |
| MonoTorrent | 3.0.2 | `TorrentPlugin/TorrentPlugin.csproj:10`, `BasePlugin/BasePlugins.csproj:11` | **MIT** | Permissive | Yes |
| Ytdlp.NET | 3.0.4 | `YoutubePlugin/YoutubePlugin.csproj:15`, `SocialMediaPlugin/SocialMediaPlugin.csproj:10` | **MIT** | Permissive | Yes |
| Newtonsoft.Json | 13.0.3 | `TelegramAutoDownload.csproj:24`, `...Headless.csproj:15` | **MIT** | Permissive | Yes |
| MahApps.Metro | 2.4.11 | `TelegramAutoDownload.csproj:23` | **MIT** | Permissive | Yes |
| QRCoder.Xaml | 1.7.0 | `TelegramAutoDownload.csproj:25` | **MIT** | Permissive | Yes |
| dotenv.net | 3.2.0 | `TelegramAutoDownload.csproj:22` | **MIT** | Permissive | Yes |
| Microsoft.Data.Sqlite | 8.0.10 | `...Headless.csproj:14` | **MIT** | Permissive | Yes |
| Serilog | 4.2.0 | 3 projects | **Apache-2.0** | Permissive + patent grant | Yes |
| Serilog.Sinks.File | 6.0.0 | `TelegramAutoDownload.csproj:27`, `...Headless.csproj:19` | **Apache-2.0** | Permissive | Yes |
| Serilog.Sinks.Console | 6.0.0 | `...Headless.csproj:18` | **Apache-2.0** | Permissive | Yes |
| Serilog.Sinks.Seq | 8.0.0 | `Logger/Logger.csproj:10` | **Apache-2.0** | Permissive | Yes |
| Serilog.AspNetCore | 8.0.3 | `...Headless.csproj:17` | **Apache-2.0** | Permissive | Yes |
| Dapper | 2.1.35 | `...Headless.csproj:13` | **Apache-2.0** | Permissive | Yes |
| FluentAssertions | 6.12.0 | `...Tests.csproj:11`, `...UITests.csproj:14` | **Apache-2.0** (v6) | Permissive | No (test) |
| xunit | 2.7.0 | both test projects | **Apache-2.0** | Permissive | No (test) |
| xunit.runner.visualstudio | 2.5.7 | both test projects | **Apache-2.0** | Permissive | No (test) |
| Microsoft.NET.Test.Sdk | 17.9.0 | both test projects | **MIT** | Permissive | No (test) |
| RichardSzalay.MockHttp | 7.0.0 | `...Tests.csproj:13` | **MIT** | Permissive | No (test) |
| FlaUI.UIA3 | 4.0.0 | `...UITests.csproj:13` | **MIT** | Permissive | No (test) |

**[FACT]** No NuGet package in the dependency graph is copyleft. Every shipped managed
dependency is MIT or Apache-2.0. Sources: each project's GitHub `LICENSE` file and NuGet
package metadata (see §7 for URLs).

**[ASSESSMENT] — Good news, with two caveats.** The managed dependency tree is clean for
commercial use. Apache-2.0 (Serilog, Dapper) adds an attribution and NOTICE-preservation
requirement but no source-disclosure obligation. Both caveats are below.

#### Caveat A — FluentAssertions is a latent licensing trap

**[FACT]** FluentAssertions v6.12.0 is Apache-2.0. FluentAssertions **v8 and later are
distributed under a paid Xceed commercial licence**; v7 and earlier remain free indefinitely
(Xceed/maintainer statement in the relicensing PR; single-seat pricing was USD 129.95/yr at
announcement).

**[ASSESSMENT]** The pinned 6.12.0 is safe today. A routine `dotnet outdated` or Dependabot
major bump to v8 would silently create a per-developer licence liability in a commercial
product. Mitigation: pin the version range to `[6.12.0,8.0.0)` in both test projects, and add
FluentAssertions to a licence-policy deny-list for major upgrades. Because it is test-only it
never reaches customers, so this is a vendor cost risk, not a distribution risk.

#### Caveat B — Ytdlp.NET 3.0.4 is deprecated upstream

**[FACT]** NuGet marks Ytdlp.NET 3.0.4 as *"deprecated because it is no longer maintained."*
The package has moved to 4.x. Download counts for the whole package are in the low hundreds
per version, and the upstream repository has ~17 GitHub stars.

**[ASSESSMENT]** This is a supply-chain and support risk rather than a licensing one — but an
enterprise buyer's procurement review will flag a deprecated, single-maintainer, ~17-star
dependency sitting on the critical path of two shipped plugins. It is a thin wrapper around
the `yt-dlp` command line; the cheapest durable fix is to vendor the ~200 lines of process
invocation the app actually needs and drop the dependency entirely.

### 1.4 Bundled and downloaded binaries — the real licensing exposure

This is where the copyleft problem lives. The managed code is clean; the native binaries are not.

#### yt-dlp

**[FACT]** yt-dlp's own source is released under **the Unlicense** (public domain dedication),
which permits commercial use and redistribution without conditions. **However**, yt-dlp's own
README states: *"While yt-dlp is licensed under the Unlicense, many of the release files
contain code from other projects with different licenses. Most notably, the PyInstaller-bundled
executables include GPLv3+ licensed code, and as such the combined work is licensed under GPLv3+."*

This creates a **split outcome by platform**, which matters enormously:

| Surface | What is obtained | Effective licence | Who distributes it |
|---|---|---|---|
| **Windows desktop** | `yt-dlp.exe` downloaded at runtime from `github.com/yt-dlp/yt-dlp/releases/latest` (`TelegramAutoDownload/Services/YtdlpService.cs:19-20, 86`) | **GPLv3+** (PyInstaller bundle) | **The end user** — not the vendor |
| **Docker container** | `apt-get install -y yt-dlp` (`Dockerfile:36`) | **Unlicense** (Debian ships the Python package, not the PyInstaller bundle) | **The vendor** |

**[ASSESSMENT]** The Windows path is, somewhat by luck, the safer one: because the user's own
machine fetches the GPLv3+ executable at first run, the vendor is not a distributor of it and
incurs no GPL obligation. The Docker path distributes only the Unlicense-licensed Python
package, which is also fine.

**[ASSESSMENT] — But there is a live trap in the code.** `YtdlpService.cs:51-57` and
`:126-133` already contain logic to copy a **bundled** `tools/yt-dlp.exe` and `tools/ffmpeg.exe`
from the installation directory if present. `installer/setup.iss` does not currently ship those
files, so the trap is unarmed. The moment anyone adds `tools\*` to the installer's `[Files]`
section — a one-line change that looks like a pure convenience improvement — the vendor
becomes a distributor of GPLv3+ and GPL-2+ binaries inside a proprietary installer. This
should be documented as a hard build-policy rule, not left to memory.

#### ffmpeg — the most serious copyleft finding

**[FACT] Docker.** `Dockerfile:36` runs `apt-get install -y --no-install-recommends yt-dlp ffmpeg`
on the `mcr.microsoft.com/dotnet/aspnet:8.0` base image, which is Debian-based. Debian's
`ffmpeg` package is built with `--enable-gpl` and links `libx265` (Debian `rules` file), and
Debian's own copyright file states: *"For building the default Debian packages some of the GPL
licensed files are used, so the resulting binaries are licensed under GPL v2+."*

→ **The published Docker image contains GPL-2+ licensed binaries.**

**[FACT] Windows.** `TelegramAutoDownload/Services/YtdlpService.cs:23-24` downloads
`ffmpeg-master-latest-win64-gpl.zip` from `yt-dlp/FFmpeg-Builds`. The `gpl` variant is,
per that project's build matrix, the GPL build including x264/x265 — the `lgpl` variant, which
omits those codecs, exists and is **not** the one chosen. As with yt-dlp, this download happens
on the end user's machine at runtime (`YtdlpService.cs:137-166`), so the vendor does not
currently distribute it.

**[ASSESSMENT] — What the GPL-2+ ffmpeg in the Docker image actually obliges.**
Two questions must be separated, because conflating them causes most FOSS-compliance panic:

1. *Does bundling GPL ffmpeg make TelegramAutoDownload itself GPL?* **Almost certainly not.**
   The application never links ffmpeg. It invokes `yt-dlp` as a separate OS process
   (`SocialMediaPlugin/SocialMediaPlugin.cs:117-168`), which in turn invokes `ffmpeg` as a
   further separate process discovered on `PATH` or in the tools directory. Arm's-length
   invocation of an independent executable via a process boundary is the classic
   "mere aggregation" pattern and does not create a derivative work. The vendor's own source
   code can remain proprietary.
2. *Does distributing the image create GPL obligations at all?* **Yes, and these are real.**
   By publishing an image containing GPL-2+ binaries, the vendor is a distributor of those
   binaries and must, under GPL-2 §3, accompany the distribution with the corresponding
   source or a valid written offer to supply it, and must convey the licence text. Debian
   satisfies this upstream; the vendor does not inherit that compliance automatically simply
   because `apt` did the installing.

**[ASSESSMENT] — Recommended remediation, in preference order:**

- **Best: stop shipping ffmpeg in the image.** Make the container's ffmpeg an
  operator-provided mount or a separate opt-in image layer the customer builds. Removes the
  vendor from the GPL distribution chain entirely.
- **Acceptable: comply.** Keep `apt`-installed ffmpeg, ship `NOTICE`/`THIRD-PARTY-LICENSES`
  with the full GPL-2 text inside the image, and publish a written offer for corresponding
  source that points at Debian's source packages pinned to the exact versions shipped.
  This is genuinely achievable — it is what most commercial NAS packages do — but it must be
  a maintained artifact, not a one-off.
- **Also worth doing regardless: switch the Windows download to the `lgpl` ffmpeg variant**
  unless x264/x265 encoding is actually required. The app only remuxes/merges streams, which
  the LGPL build handles. This removes the copyleft question from the Windows path
  permanently and cheaply.

**[LEGAL QUESTION 3]** Confirm that process-boundary invocation of GPL ffmpeg and GPLv3+
yt-dlp does not create a derivative work of the proprietary application, on the specific facts
here (no linking, no shared address space, separate executables, tool discovered at runtime).
This is settled in practice but the vendor should have a written opinion before selling.

**[LEGAL QUESTION 4]** For the Docker image specifically: is a written offer pointing to
Debian's published source archives sufficient to discharge GPL-2 §3(b), or must the vendor
mirror the corresponding source itself for the exact package versions shipped?

#### Summary of copyleft exposure

| Binary | Licence | Vendor distributes? | Obligation created |
|---|---|---|---|
| Debian `ffmpeg` in Docker image | **GPL-2+** | **Yes** — `Dockerfile:36` | **Licence text + corresponding-source offer** |
| Debian `yt-dlp` in Docker image | Unlicense | Yes | None (attribution courtesy only) |
| `yt-dlp.exe` on Windows | **GPLv3+** | No — runtime download by user | None today; **armed trap** if bundled |
| `ffmpeg.exe` (gpl build) on Windows | **GPL** | No — runtime download by user | None today; **armed trap** if bundled |
| .NET 8 runtime (self-contained exe) | MIT | Yes | Attribution |

### 1.5 Required third-party notices

Drafted in full at **`docs/compliance/drafts/NOTICE.md`**, covering: per-package attribution
for all 20 NuGet dependencies, the Apache-2.0 NOTICE-preservation requirement for Serilog and
Dapper, the GPL-2+ disclosure and written source offer for the Docker image's ffmpeg, the
Unlicense acknowledgement for yt-dlp, and a Telegram trademark disclaimer.

---

## 2. Platform terms risk — Telegram

### 2.1 What the terms actually say

Three documents govern. All were retrieved 2026-09-30.

**[FACT] Telegram API Terms of Service** (<https://core.telegram.org/api/terms>) — this is the
document that applies to a `my.telegram.org` `api_id`, which is exactly what this app uses
(`README.md:96-98`):

- **§1.4** — *"It is forbidden to interfere with the basic functionality of Telegram. This
  includes but is not limited to: making actions on behalf of the user without the user's
  knowledge and consent..."*
- **§1.3** — *"you must make sure that all the basic features of the main Telegram apps
  function correctly and in an expected way both in your app..."*
- **§2.1** — *"You must obtain your own api_id for your application."*
- **§2.2** — *"your users must be aware of the fact that your app uses the Telegram API and is
  part of the Telegram ecosystem. This fact must be featured prominently in the app's
  description in the app stores and in the in-app intro."*
- **§2.3** — *"the title of your app must not include the word 'Telegram'. An exception can be
  made if the word 'Telegram' is preceded with the word 'Unofficial' in the title."*
- **§2.4** — *"You must not use the official Telegram logo for your app. Both the Telegram
  brand and its logo are registered trademarks protected by law in almost every country."*
- **§3.3** — *"If your app allows accessing content from Telegram channels, you must include
  support for official sponsored messages in Telegram channels and may not interfere with
  this functionality."*
- **§4.1–4.2** — breach triggers notification to the responsible account, then after 10 days
  without remediation, *"we will have to discontinue your access to Telegram API and contact
  the app stores about the removal of your apps."*

**[FACT] Telegram Terms of Service for Content Licensing** (<https://telegram.org/tos/content-licensing>):

- *"Access to user-generated content for any purpose other than ordinary, legitimate, and
  intended use of the Telegram platform as its user is prohibited."*
- *"As a limited exception, Telegram permits access to data required to launch and operate a
  legitimate third-party Telegram Client, Telegram Bot, or Telegram Mini App... Any such data
  is licensed on a **retractable, limited, non-exclusive, non-transferable and
  non-sublicensable** basis solely to the extent strictly required to operate the relevant
  service."*
- *"Telegram firmly prohibits the scraping, indexing, harvesting, aggregation or use of data
  obtained from its platform to train, fine-tune, validate or otherwise engage in the
  development, enhancement, benchmarking or deployment of artificial intelligence, machine
  learning models and similar technologies."*

**[FACT] Telegram Terms of Service** (<https://telegram.org/tos/>): *"Failure to comply with
the Telegram Terms of Service may result in a temporary or a permanent ban from Telegram or
some of its services... we will not compensate you for this loss."*

### 2.2 Compliance assessment against each clause

| Clause | Current state | Verdict |
|---|---|---|
| **§2.3** name must not contain "Telegram" unless prefixed "Unofficial" | Product is named **"Telegram Auto Download"** (`README.md:1`, `installer/setup.iss:1`, assembly/product names throughout) | **[FACT] VIOLATION — unambiguous and verifiable** |
| **§2.4** must not use official Telegram logo | App ships its own `.ico` (`TelegramAutoDownload.csproj:10`). Not inspected pixel-by-pixel, but no evidence of logo misuse | **[ASSESSMENT] Likely compliant — verify the icon** |
| **§3.3** must support official sponsored messages | No sponsored-message support anywhere in the codebase; the app reads channel content and ignores sponsored messages entirely | **[FACT] VIOLATION** |
| **§2.2** prominent disclosure of Telegram API use | README describes it; no in-app intro or disclosure screen; no app-store listing exists yet | **[ASSESSMENT] Partial — fails the "in-app intro" limb** |
| **§1.3** all basic features of main Telegram apps must work | This is a download automation tool, **not a messaging client** — no send/receive UI, no chat view. It does mute chats and send emoji reactions (`README.md:24-25, 155`) but cannot function as a Telegram client | **[ASSESSMENT] Structurally non-compliant** |
| **§2.1** own `api_id` | Each **end user** supplies their own `APP_ID`/`API_HASH` (`README.md:96-98`, `docker-compose.yml:20-21`) | **[ASSESSMENT] Compliant in letter; see 2.3** |
| **§1.4** no actions without user knowledge/consent | Actions are user-configured per chat. Emoji reactions and mute changes are opt-in per chat | **[ASSESSMENT] Defensible** |
| **Content Licensing** — no harvesting/aggregation | History export to JSONL (`TelegramClient/ChatHistoryService.cs:14-15`) and member-list export (`TelegramClient/TelegramApp.cs:1072-1082`) are precisely "harvesting" and "aggregation" of user-generated content | **[ASSESSMENT] High risk of violation** |
| **Content Licensing** — no AI/ML training use | The product does not do this itself, but produces exactly the corpus (JSONL chat logs) someone would use for it | **[ASSESSMENT] Must be prohibited in the AUP** |

### 2.3 The "bring your own api_id" pattern shifts risk onto customers

**[ASSESSMENT]** Requiring each user to register their own `api_id` at `my.telegram.org` looks
like a compliance win, and for §2.1 it is. But it has a consequence the owner should
understand clearly: it makes **every paying customer** the "developer" of record for their own
API access, and therefore the party Telegram notifies and bans under §4.1. The vendor sells a
product; the customer absorbs the enforcement.

For an individual hobbyist that is an acceptable, understood trade. For a **compliance-driven
enterprise archival** buyer it is close to unsellable: the vendor is asking a corporate
customer to accept an unbounded, uninsurable risk of losing a business communications account,
in writing, with no vendor indemnity. Expect enterprise procurement to stop here.

### 2.4 Realistic likelihood of account bans

**[ASSESSMENT]** Drawing on how Telegram's MTProto rate limiting behaves in practice:

**Behaviours that actually trigger enforcement**, in rough descending order of risk:

1. **Bulk member enumeration** (`Messages_GetFullChat` / `Channels_GetParticipants` loops) —
   the single most ban-associated API pattern on the platform, because it is the signature of
   contact-harvesting spam operations.
2. **Aggressive full-history sweeps** across many chats at once, especially right after login
   on a fresh session. The app's `Sync` feature (`README.md:27`) and parallel bootstrap
   scanning do exactly this.
3. **Sustained high-volume media downloads** far beyond human-plausible rates.
4. **New account + immediate heavy automation** — the highest-risk combination by a wide
   margin. A brand-new number that logs in and immediately enumerates members and sweeps
   history is the classic spam-bot fingerprint.
5. **Datacentre IP addresses** — a VPS/cloud-hosted session is materially more suspicious than
   a residential one. Note the Docker/NAS deployment is typically residential, which helps.

**Realistic outcomes**, not binary: the common failure mode is not a permanent ban but a
graduated ladder — `FLOOD_WAIT_x` throttling (seconds to hours), then temporary restrictions,
then `PEER_FLOOD` (cannot message non-contacts), then account limitation, then permanent ban.
Most users who "get banned" are actually stuck at the throttling or limitation rungs.

**[FACT] — The app already handles this better than most.** This is a genuine strength and
should be foregrounded in any counsel discussion:

- `FloodWaitTracker` and `FloodWaitHelper.TryParseSeconds` parse and respect server-issued
  `FLOOD_WAIT` values (`TelegramAutoDownload.Headless/Scanning/DownloadOrchestrator.cs:164-172`,
  `BootstrapScanner.cs:78-82`).
- `TokenBucketRateLimiter` throttles scanner API calls (`BootstrapScanner.cs:23`).
- `RateLimitThresholds` defines conservative defaults (scanner capacity 5.0, refill 1.0/s,
  3 download threads) with explicit `Warn` and `Danger` tiers surfaced in the UI
  (`RateLimitThresholds.cs:6-23`).
- Member fetching paces itself with an explicit `await Task.Delay(500)` between batches and a
  comment reading *"Respect Telegram rate limits"* (`TelegramClient/TelegramApp.cs:1040-1041`).
- Quiet Hours suppresses activity during a configurable window (`README.md:33`).

**[ASSESSMENT] What is still missing for defensibility:**

1. **Hard caps, not just warnings.** `RateLimitThresholds.cs:10-12` lets a user push scanner
   capacity to 100.0 and refill to 50/s — ten to fifty times the default. Labelling that
   "Danger" is not the same as preventing it. A commercial product should cap the maximum at
   a defensible ceiling and refuse to exceed it, so the vendor can state that the software
   *cannot* be configured into abuse.
2. **A ban-risk disclosure at login**, not buried in a README — a one-time modal that the user
   must acknowledge, stating that automating a personal account may result in restriction or
   permanent loss of the account, and that the vendor cannot recover it.
3. **First-run ramp-up.** Detect a fresh session and deliberately throttle the first
   24–48 hours of activity, which is when ban risk peaks.
4. **Kill-switch on repeated FLOOD_WAIT.** Escalating `FLOOD_WAIT` responses should pause the
   whole pipeline and tell the user, not silently re-queue forever.
5. **No member enumeration by default** — see §3.4.

### 2.5 The blunt version

**[ASSESSMENT]** The owner asked for bluntness, so:

**Selling a paid product whose core function is automating a personal Telegram user account is
a fundamental business risk, not a compliance detail to be papered over.** Concretely:

- Telegram's data licence is **retractable and non-sublicensable**. The vendor's entire
  product depends on a permission that the platform can withdraw unilaterally, at any time,
  for any reason, with no notice period and no appeal. There is no contract, no SLA, and no
  commercial relationship to fall back on.
- Two clauses are being **verifiably violated right now** (§2.3 naming, §3.3 sponsored
  messages), and a third (§1.3 full-client requirement) is violated structurally by what the
  product *is*.
- §4.2's remedy is explicit: Telegram will *"contact the app stores about the removal of your
  apps."* That is a direct threat to the Microsoft Store and NAS catalogue channels that
  §5 of the marketplace document depends on.
- The product cannot be made fully compliant by tuning, because §1.3 asks it to be a
  general-purpose Telegram client, which it is not and should not become.

**The honest framing for the owner:** this can be a real business, but only as a
**self-hosted tool sold to users who operate their own accounts at their own risk**, with the
naming and disclosure defects fixed and the risk allocated explicitly in the EULA. It cannot
be a hosted service, and it cannot be sold to risk-averse enterprises with a straight face
unless Telegram grants written permission.

**Immediate, cheap, high-value fixes:** rename the product to remove "Telegram" from the title
(or prefix "Unofficial"); add the in-app Telegram API disclosure required by §2.2; add the
ban-risk acknowledgement modal; cap the rate limits. The rename is the single highest
value-per-hour legal fix available in this entire assessment.

**[LEGAL QUESTION 5]** Is a rename to e.g. "Unofficial Telegram Auto Download" sufficient for
§2.3, or does any use of "Telegram" in a *commercial* product name create trademark exposure
independent of the API terms? Telegram's §2.4 asserts registered trademark protection
"in almost every country." A neutral brand name avoiding the mark entirely is likely safer.

**[LEGAL QUESTION 6]** Can the vendor obtain written confirmation from Telegram that a
non-client automation tool is permitted? If not, is it defensible to sell a product that
cannot satisfy API ToS §1.3 and §3.3 by design?

**[LEGAL QUESTION 7]** Does the JSONL history export and member export constitute prohibited
"harvesting" or "aggregation" under the Content Licensing terms, given that the data stays on
the user's own machine and the user already has lawful access to it as a chat participant?

---

## 3. Data protection

### 3.1 Data inventory

Every category of personal data the system touches, with code evidence.

| # | Category | GDPR classification | Where it lives | Evidence |
|---|---|---|---|---|
| 1 | **Data subject's phone number** | Personal data (identifier) | Entered at login; held in the Telegram session | `TelegramAutoDownload.Headless/LoginCoordinator.cs:58-60`; `wwwroot/index.html:59-61` |
| 2 | **2FA password / login code** | Authentication credential | Transient, entered at login | `README.md:126`; `LoginCoordinator.cs` |
| 3 | **Telegram session token** | **Credential equivalent to full account access** | `session.dat` in `%APPDATA%` / `/data` | `TelegramClient/TelegramApp.cs:101-107`; `Headless/HeadlessPaths.cs:22` |
| 4 | **API credentials** (`APP_ID`, `API_HASH`) | Secret | `.env`, `config.txt`, env vars | `README.md:96-98`; `docker-compose.yml:20-21` |
| 5 | **Chat message content** | Personal data; **often special-category** in practice | `History/{ChatName}.jsonl` | `TelegramClient/ChatHistoryService.cs:14-15, 36` |
| 6 | **Message metadata** — sender ID, sender name, timestamps, forward provenance | Personal data | Same JSONL | `TelegramClient/Models/HistoryEntry.cs:11-30` |
| 7 | **Downloaded media** | Personal data + third-party copyright | Download folder | `README.md:10, 260` |
| 8 | **Channel member lists** — user ID, first/last name, username, **phone number**, bot/admin flags | **Third-party personal data, no consent** | User-chosen CSV / vCard file | `TelegramClient/Models/MemberEntry.cs:9-21`; `TelegramApp.cs:1072-1082`; `MembersExportDialog.xaml.cs:87-140` |
| 9 | **Chat/channel inventory** — IDs, names, usernames, member counts | Personal data (when 1:1 DMs) | `config.txt` | `README.md:285`; `TelegramClient/Models/ChatDto.cs:25-26` |
| 10 | **Application logs** | Personal data — contains chat names, file names, errors | `logs/`, 7-day rolling; optional **Seq** sink | `README.md:290`; `Logger/Logger.csproj:10` |
| 11 | **Web dashboard credentials** | Authentication credential | Env vars / stored config | `Headless/WebAuthService.cs:207-211` |
| 12 | **Deduplication + statistics indexes** | Pseudonymous activity data | `downloaded_ids.json`, `stats.json` | `README.md:286-287` |

**[ASSESSMENT] — Category 5 deserves emphasis.** Chat message content from arbitrary groups
routinely contains health information, political and religious opinion, sexual orientation,
and trade-union membership — GDPR Article 9 special categories, where the lawful bases are
much narrower than for ordinary personal data. A bulk archiver cannot know in advance which
messages are special-category, so it must be assumed that some are.

**[ASSESSMENT] — Category 3 is the crown jewel.** `session.dat` is not "a login cookie." It is
a bearer credential granting full, ongoing, programmatic control of a human being's messaging
account: read all history, send messages as them, enumerate contacts. Whoever holds that file
*is* that user, as far as Telegram is concerned. The security consequences are Agent B's
scope, but the **data-protection** consequence is decisive for the SaaS question in §3.3.

**[FACT]** The session is created via `new Client(appId, apiHash, sessionPath)`
(`TelegramClient/TelegramApp.cs:107`) with no application-supplied session encryption key.
**[LEGAL/TECHNICAL QUESTION]** WTelegramClient's default at-rest protection for `session.dat`
should be confirmed against its documentation and, if it relies on a library-default key
rather than a user secret or OS keystore (DPAPI on Windows), treated as effectively plaintext
for threat-modelling purposes. Flagged to Agent B; noted here because it drives the
Article 32 "appropriate technical measures" analysis.

### 3.2 Self-hosted model — GDPR mapping

**[ASSESSMENT]** In a self-hosted deployment (desktop app, or Docker on the customer's own
NAS), the allocation is clean and strongly favourable to the vendor:

| Question | Answer |
|---|---|
| **Who is the controller?** | **The customer.** They choose the purposes, the chats, the retention, and the storage location. |
| **Who is the processor?** | **Nobody.** The vendor never receives personal data. A software supplier that does not process data on the controller's behalf is neither controller nor processor — it is just a vendor. |
| **Is a DPA (Art. 28) required?** | **No**, because there is no processing on the vendor's behalf. This is a significant commercial simplification. |
| **Is a Record of Processing (Art. 30) required?** | Yes — by the **customer**, not the vendor. |
| **Household exemption (Art. 2(2)(c))?** | Applies to a genuine individual doing purely personal archiving. **Does not apply** to any business, team, or "media archive operator" use — and the CJEU reads this exemption narrowly. Do not let the product marketing imply it covers commercial use. |

**Lawful basis (the customer's problem, but the product must enable it):**

- For the **customer's own** data: consent / contract — trivial.
- For **other participants' messages** in chats the customer belongs to: realistically
  **legitimate interests (Art. 6(1)(f))**, requiring a documented balancing test. Viable for a
  compliance-archival use case with a retention policy; weak for indiscriminate hoarding.
- For **special-category content** (Art. 9): **there is often no available basis at all.**
  Art. 9(2)(e) (data manifestly made public by the data subject) may cover large public
  channels. It does not cover private groups.

**Product requirements that flow from this** — the vendor's job is to make the customer's
compliance *possible*:

1. **Configurable retention with automatic deletion.** Currently absent: logs roll at 7 days
   (`README.md:290`) but JSONL histories, media, and member exports grow forever. Data
   minimisation (Art. 5(1)(c)) and storage limitation (Art. 5(1)(e)) require a TTL. This is a
   concrete, shippable feature and the highest-value compliance investment in the product.
2. **Subject-access and erasure tooling.** If a group member exercises Art. 15/17 against the
   customer, the customer needs to find and delete everything relating to one `SenderId`
   across all JSONL files, media, and the dedup index. Today that is a manual `grep`. A
   `search-by-subject` and `erase-subject` command would be a genuinely differentiating
   enterprise feature.
3. **Export/delete-all.** One action that produces a portable archive, and one that
   destroys all data including `session.dat`.
4. **Documentation the customer's DPO can actually use** — a data-inventory page mapping
   exactly what is stored where, which is largely §3.1 of this document.

### 3.3 Vendor-hosted SaaS model — GDPR mapping

**[ASSESSMENT] The answers differ so fundamentally that this is effectively a different
product with a different legal risk class. My assessment is that it should not be built.**

| Question | Answer |
|---|---|
| **Who is the controller?** | **The customer** nominally — but see below, the vendor's control over the data is so complete that a regulator could find joint controllership. |
| **Who is the processor?** | **The vendor**, unavoidably. |
| **DPA (Art. 28) required?** | **Yes**, with every customer, including sub-processor disclosure for every hosting provider. |
| **Art. 32 security obligations** | Vendor must protect, at scale, in one place, a database of **credentials granting full control of thousands of humans' personal messaging accounts.** |
| **Art. 33 breach notification** | Vendor must notify within 72 hours. A breach here means total account compromise for every customer simultaneously. |
| **Art. 35 DPIA** | **Mandatory.** Large-scale processing of communications content, including special categories, using systematic automated monitoring. This hits multiple Art. 35(3) triggers at once. |
| **Chapter V transfers** | Any non-EEA hosting requires transfer mechanisms and a TIA. |

**Why I recommend against it — four independent reasons, each close to sufficient on its own:**

1. **Telegram's licence is `non-sublicensable`** ([FACT], §2.1). A hosted service accesses
   Telegram on behalf of a customer using the customer's credentials. That is, on its face,
   the vendor operating under a licence it is not permitted to sub-license. This is not a
   gap to be papered over with contract language; it is the platform explicitly withholding
   the right the business model requires.
2. **Aggregated session-token custody is an extreme and largely uninsurable risk.** The vendor
   would hold the equivalent of every customer's phone. One breach is not "a data breach" —
   it is mass account takeover, with the vendor as the proximate cause. Pricing cyber cover
   for that, with honest disclosure, will be punitive if it is available at all.
3. **No lawful basis for the third-party content.** The vendor would process the messages of
   millions of people who have never heard of the vendor, are in no relationship with it, and
   cannot be informed. The customer cannot validly authorise that on their behalf.
4. **It converts the content-liability analysis (§4) from "tool vendor" to "service
   operator."** The vendor's own infrastructure would be performing the downloads, which is
   the difference between contributory-liability arguments and direct involvement. See §4.

**[ASSESSMENT] Conclusion: vendor-hosted SaaS is ruled out.** Not "hard," not "expensive" —
ruled out, on the platform-licence point alone. If the owner wants recurring revenue, it must
come from subscription licensing of self-hosted software, not from hosting.

### 3.4 Member-list export — addressed directly

The task asked for this to be addressed head-on, and it is the sharpest data-protection
finding in the repository.

**[FACT] What the code does.** `TelegramApp.GetChannelMembersAsync` pages through
`Channels_GetParticipants` (or `Messages_GetFullChat` for small groups) and builds a
`MemberEntry` per member (`TelegramApp.cs:1010-1070`). `BuildEntry` at
`TelegramApp.cs:1072-1082` copies, unconditionally and with no filtering:

```
UserId    = userId,
FirstName = user?.first_name,
LastName  = user?.last_name,
Username  = user?.MainUsername,
Phone     = user?.phone       ← phone number
IsBot, IsAdmin
```

The result is exported to **CSV** or **vCard** via a file-save dialog
(`MembersExportDialog.xaml.cs:87-140`). The UI explicitly counts and displays how many members
have a phone number attached: *"{withPhone:N0} with phone"* (`MembersExportDialog.xaml.cs:58-61`).
`MemberEntry.cs:14-18` acknowledges in a comment that phone is *"only populated when the user
has shared it with you"* and that most members will be empty due to privacy settings.

**[ASSESSMENT] Why this is the most legally exposed feature in the product.**

This is processing the personal data of potentially tens of thousands of identifiable people
who are not the user, have no relationship with the user or the vendor, have not consented,
and will never be told it happened. Walking through GDPR:

- **Art. 6 — lawful basis.** There is no plausible one. Not consent (never asked). Not
  contract (no relationship). Legitimate interests fails the balancing test: bulk extraction
  of a membership roster into a portable contact file, with phone numbers where available,
  has no purpose that outweighs the members' rights, and members would plainly not expect it.
- **Art. 14 — information obligation for indirectly collected data.** The controller must
  inform each data subject, normally within one month. With 50,000 members this is
  impossible in practice, and "impossible" is not a defence — it is evidence the processing
  should not occur.
- **Art. 5(1)(b) purpose limitation / 5(1)(c) minimisation.** Exporting *every* field for
  *every* member has no defined purpose to be limited to. The `Phone` field in particular
  is gratuitous.
- **vCard format is an aggravating factor, not a neutral one.** CSV is data; a `.vcf` is a
  file whose *purpose* is importing people into a contact book or CRM. Shipping a
  one-click "export all channel members to vCard **with phone numbers**" is, functionally, a
  lead-generation and contact-harvesting tool. A regulator or opposing counsel will
  characterise it exactly that way, and the `withPhone` counter in the UI reads as a feature
  advertising precisely that capability.
- **Telegram's own terms** independently prohibit "harvesting" and "aggregation"
  ([FACT], §2.1), and member enumeration is the API pattern most associated with account bans
  ([ASSESSMENT], §2.4).

**[ASSESSMENT] Recommendation — in order of preference:**

1. **Remove the feature from the commercial product.** It is not core to "automatically
   download media from my chats." It carries the highest legal risk of any feature, for
   among the lowest product value. Cut it.
2. If it must survive for an existing-user commitment: **drop the `Phone` field entirely**
   (a one-line change in `BuildEntry`), remove the vCard export, remove the `withPhone`
   counter, restrict it to channels where the user is an **admin**, require an explicit
   purpose statement and typed acknowledgement of controller obligations before each export,
   and write an audit record of who exported what and when.
3. **Never** enable it in a hosted deployment, and never in the default configuration.

**[LEGAL QUESTION 8]** Is there *any* lawful basis under GDPR Art. 6 for a commercial product
feature that bulk-exports channel member rosters, including phone numbers where available, to
CSV/vCard — and does shipping it create direct or accessory liability for the **vendor**, as
distinct from the user who runs it?

**[LEGAL QUESTION 9]** Does the vendor's liability differ between shipping this feature
enabled-by-default versus behind an acknowledged warning? (This determines whether option 2
above is actually worth the engineering effort or is merely comfort.)

### 3.5 Other data-protection gaps

**[FACT]** `docker-compose.yml:16-17` ships default dashboard credentials
`WEB_USERNAME: "admin"` / `WEB_PASSWORD: "changeme"`. The application does warn at runtime
when the default password is still active (`Headless/WebAuthService.cs:70`).
**[ASSESSMENT]** A warning is insufficient for a commercial product. Ship with no default
credential and force first-run setup; a shipped default password is an Art. 32 finding and
an instant fail in most enterprise security questionnaires.

**[FACT]** The optional Seq sink (`Logger/Logger.csproj:10`) can ship logs — which contain
chat names and file names — to a remote log aggregator.
**[ASSESSMENT]** If the vendor ever operates that Seq endpoint, the vendor silently becomes a
processor of the customer's personal data, and the clean "no DPA required" position in §3.2
collapses. Seq must point only at customer-controlled infrastructure, be off by default, and
say so in the privacy policy.

---

## 4. Content liability

### 4.1 The baseline risk

**[ASSESSMENT]** A tool whose stated purpose is bulk media acquisition sits in a
well-litigated area. The two poles of US doctrine are *Sony Corp. of America v. Universal City
Studios* (1984) — no contributory liability where a technology has substantial non-infringing
uses — and *MGM Studios v. Grokster* (2005) — liability attaches where a distributor takes
affirmative steps to induce infringement, with marketing and business-model evidence weighing
heavily.

For the **Telegram media download** core feature, the position is reasonably comfortable: the
user already has lawful access to the content as a chat participant, saving a file you were
sent is an ordinary and substantially non-infringing act, and the feature is not marketed
around piracy. The risk rises with the *other* two features.

### 4.2 Arbitrary-URL and yt-dlp downloading — DMCA §1201 is the problem

**[FACT] What the code does.** `SocialMediaPlugin.CanHandle` accepts **any** `http(s)://` URL
(`README.md:57`, `SocialMediaPlugin/SocialMediaPlugin.cs:14-15`) and passes it to `yt-dlp`,
with a per-chat quality selector including 4K and audio-only extraction
(`README.md:21`, `SocialMediaPlugin.cs:113-168`). The README advertises Instagram, TikTok,
Twitter/X, Reddit, and Facebook by name (`README.md:57`).

**[FACT] The governing case law.** In *Yout LLC v. RIAA* (D. Conn. 2022), Yout — operator of a
stream-ripping service — sought a declaratory judgment that its service did **not** violate
17 U.S.C. §1201. The court **granted the RIAA's motion to dismiss**, and the Second Circuit
**affirmed**, accepting that circumventing YouTube's "rolling cipher" signature mechanism
engages §1201's anti-trafficking provisions. Separately, the RIAA's October 2020 notice to
GitHub demanded removal of **youtube-dl itself** — yt-dlp's direct ancestor — expressly under
§§1201(a)(2) and 1201(b)(1), citing a Hamburg Regional Court ruling that YouTube's rolling
cipher is an effective technical measure under EU and German law. GitHub reinstated the
repository after EFF argued the signature code is not a protected technical measure.

**[ASSESSMENT] Why this is materially worse than ordinary copyright risk:**

- **§1201 is an anti-circumvention claim, not an infringement claim.** Fair use is not a
  defence to it. The DMCA §512 safe harbours do not apply to it. A notice-and-takedown process,
  however well-run, does not protect against it.
- **It attaches to *trafficking* in the tool**, not to the end user's copying. Selling the tool
  is the act complained of. This is the crucial difference from the Telegram core feature.
- **The dispute is genuinely unresolved on the merits.** EFF's technical argument (the
  signature is not a lock; yt-dlp runs it exactly as intended) is strong and GitHub accepted
  it. But *Yout* went the other way on a motion to dismiss and was affirmed. A defendant would
  be litigating an open question, expensively.
- **Charging money is an aggravating factor under *Grokster*.** A free hobbyist tool and a
  paid commercial product with a rights-holder-adverse feature are treated differently. The
  business model becomes evidence.

**[ASSESSMENT] Verdict on the generic-URL feature: it cannot ship enabled-by-default in a
paid commercial product.** It is the feature most likely to attract a rights-holder letter,
and the vendor has no safe harbour when it arrives. Options, from safest:

- **(a)** Remove `SocialMediaPlugin` from the commercial build. Keep the Telegram-native
  download core, which is the actual product.
- **(b)** Ship it **self-hosted only, disabled by default, with no bundled yt-dlp**, requiring
  the user to install yt-dlp themselves. This keeps the vendor out of the trafficking chain:
  the vendor ships an orchestrator that can invoke a user-supplied tool, which is a materially
  better position than shipping the circumvention capability. The current runtime-download
  behaviour (`YtdlpService.cs:82-89`) is worse than this, because the vendor's code fetches the
  tool automatically — an affirmative step a *Grokster* plaintiff would highlight.
- **(c)** Restrict the URL allow-list to platforms with sanctioned APIs or explicit download
  permission. Higher engineering cost, much lower risk, and far less useful.

**Recommendation: (b) for self-hosted, with (a) for anything the vendor hosts or lists in a
consumer app store.** And regardless of choice: **stop auto-downloading yt-dlp and ffmpeg.**
That single change meaningfully improves the vendor's position in both the §1201 and the GPL
analyses at negligible product cost.

### 4.3 Torrents — the clearest cut

**[FACT] What the code does.** `TorrentPlugin` uses MonoTorrent 3.0.2
(`TorrentPlugin/TorrentPlugin.csproj:10`) to handle `.torrent` attachments and `magnet:` links
found in chat text (`README.md:58`). The README describes it as participating in the
"BitTorrent swarm."

**[ASSESSMENT] Why this is categorically different from downloading:**

BitTorrent is not a download protocol; it is a **distribution** protocol. A participating peer
uploads pieces to other peers as an inherent condition of using it. So the product does not
merely help a user *acquire* a file — it makes the user's machine **a distributor** of that
file to strangers.

That changes the legal character entirely:

- **Distribution is the right rights-holders enforce most aggressively**, because it is
  directly observable from outside. Anti-piracy monitors join swarms, log peer IP addresses,
  and send infringement notices to ISPs at industrial scale. This is the most heavily
  automated enforcement pipeline in copyright.
- **In the EU**, *Land Nordrhein-Westfalen v. Renckhoff* and the *Ziggo* line of cases treat
  making works available via P2P as communication to the public; the CJEU in *Mircom v.
  Telenet* (C-597/19) confirmed that even uploading fragments of a file in a swarm constitutes
  making available.
- **Automatic ingestion makes it far worse.** The plugin acts on magnet links *found in chat
  messages* — meaning the user's machine can join a swarm for content they never chose,
  because somebody posted a link in a group. The user becomes a distributor of unknown
  material without an affirmative act. That is indefensible in a commercial product.
- **Under *Grokster*, this is the inducement fact pattern.** Bundling automatic magnet-link
  ingestion into a paid media-acquisition product is close to the conduct the Supreme Court
  found culpable.
- **Secondary consequences:** many NAS and app-store catalogues restrict or reject BitTorrent
  clients; some hosting providers and payment processors treat them as prohibited business
  categories. This can cost the vendor its billing rails, not just a lawsuit.

**[ASSESSMENT] Verdict on the torrent feature: remove it from the commercial product.**
This is my clearest recommendation in the entire assessment. The reasoning is not primarily
"it is illegal" — a torrent client is lawful software and many are sold commercially. It is
that **this specific design** (automatic, unattended swarm participation triggered by links in
third-party chat messages, inside a paid bulk-acquisition product) combines the worst
available facts on inducement, gives the vendor no defensible story, and delivers modest
product value.

If the owner refuses to cut it, the *minimum* viable position is: self-hosted only; disabled by
default; **never** auto-ingesting magnet links from messages — explicit per-item user action
only; upload ratio capped at the protocol minimum; separately licensed with its own
acceptance; and excluded from every app-store and NAS-catalogue submission. I would still
advise cutting it.

### 4.4 Feature-by-feature shipping verdict

| Feature | Commercial? | Conditions |
|---|---|---|
| Telegram media download (core) | **Yes** | Rate caps, ban-risk disclosure, AUP |
| Telegram history JSONL logging | **Yes** | Retention/TTL controls, AUP, privacy documentation |
| Channel member export | **No — cut** | If retained: no phone, no vCard, admin-only, audited (§3.4) |
| YouTube plugin | **Restricted** | Self-hosted only, off by default, user-supplied yt-dlp |
| Generic URL / SocialMedia plugin | **Restricted** | Same, plus never in an app-store build |
| Torrent plugin | **No — cut** | If retained: self-hosted, off, manual-only, no auto-ingest |
| Web dashboard | **Yes** | No default credentials, authentication hardening |

### 4.5 Notice-and-takedown process

**[ASSESSMENT]** For **self-hosted**, the vendor hosts no customer content, so DMCA §512(c)
does not apply — there is nothing to take down. The vendor still needs a published process,
because rights-holders will send notices anyway and a professional response path is worth
having. Drafted at `docs/compliance/drafts/dmca-notice-and-takedown.md`, covering: a
designated agent and contact address; a documented triage path (the vendor cannot remove
content it never possessed, and should say so clearly while acting on the parts it controls —
product features, documentation, customer accounts); an account-termination policy for
customers who repeatedly misuse the product; and counter-notice handling.

**[ASSESSMENT]** If the vendor *ever* hosts, §512(c) safe harbour becomes essential and
requires a **designated agent registered with the US Copyright Office** (registration and a
periodic fee), a repeat-infringer termination policy that is actually enforced, and no actual
knowledge or red-flag awareness. In the EU, the DSA adds notice-and-action, statement-of-reasons,
and transparency-reporting obligations. This is a further reason SaaS is the wrong model.

An **acceptable use policy** outline is drafted at
`docs/compliance/drafts/acceptable-use-policy.md`.

---

## 5. Required documents

| Document | Status | Location |
|---|---|---|
| `LICENSE` (EULA + open-core alternative) | **Drafted** | `docs/compliance/drafts/LICENSE` |
| `SECURITY.md` | **Drafted** | `docs/compliance/drafts/SECURITY.md` |
| `NOTICE` / third-party attributions | **Drafted** | `docs/compliance/drafts/NOTICE.md` |
| Acceptable Use Policy | **Drafted (outline + substantive clauses)** | `docs/compliance/drafts/acceptable-use-policy.md` |
| DMCA / notice-and-takedown process | **Drafted** | `docs/compliance/drafts/dmca-notice-and-takedown.md` |
| Privacy Policy | **Outline only — needs counsel** | `docs/compliance/drafts/privacy-policy-outline.md` |
| Terms of Service | **Outline only — needs counsel** | `docs/compliance/drafts/terms-of-service-outline.md` |
| Ban-risk disclosure (in-app) | Specified in the ToS outline | — |
| Telegram API disclosure (API ToS §2.2) | Specified in the ToS outline | — |
| Data Processing Agreement | **Not drafted — only needed if SaaS**, which §3.3 rules out | — |
| Export-control / sanctions statement | **Not drafted** | Counsel; see Legal Question 12 |

> All drafts live in `docs/compliance/drafts/` deliberately. They are **proposals**, not
> adopted policy. Moving any of them to the repository root is a business decision for the
> owner after counsel review, and doing it prematurely would misrepresent the project's legal
> posture to users.

---

## 6. Questions that genuinely require a lawyer

Consolidated, in the order I would put them to counsel. The first four are gating — the
product cannot be priced or marketed without answers.

1. **[LQ 1]** Can the owner relicense proprietarily given that releases have already shipped
   under a README claiming MIT, and what is the status of already-distributed versions?
2. **[LQ 2]** Do any third-party contributions exist that would block relicensing without a CLA?
3. **[LQ 5]** Is "Unofficial Telegram Auto Download" sufficient for API ToS §2.3, or does any
   use of the Telegram mark in a commercial product name create independent trademark exposure?
4. **[LQ 6]** Is it defensible to sell a product that cannot satisfy Telegram API ToS §1.3
   (full-client requirement) and §3.3 (sponsored messages) by design? Can written permission
   be obtained?
5. **[LQ 8]** Is there any GDPR Art. 6 lawful basis for a commercial feature that bulk-exports
   channel member rosters including phone numbers — and does shipping it create vendor
   liability distinct from the user's?
6. **[LQ 9]** Does vendor liability for that feature differ materially between
   enabled-by-default and behind-an-acknowledged-warning?
7. **[LQ 7]** Does JSONL history export constitute prohibited "harvesting"/"aggregation" under
   Telegram's Content Licensing terms when the data never leaves the user's machine?
8. **[LQ 3]** Confirm that process-boundary invocation of GPL ffmpeg and GPLv3+ yt-dlp creates
   no derivative work of the proprietary application on these specific facts.
9. **[LQ 4]** Does a written offer pointing at Debian's source archives discharge GPL-2 §3(b)
   for the Docker image, or must the vendor mirror corresponding source for the exact versions?
10. **[LQ 10]** What is the vendor's realistic DMCA §1201 anti-circumvention exposure for
    shipping a paid product that orchestrates yt-dlp, in light of *Yout v. RIAA* and the
    youtube-dl notice — and does the position change if the user supplies yt-dlp themselves?
11. **[LQ 11]** Does shipping the torrent plugin create contributory or inducement liability
    under *Grokster*, particularly given automatic magnet-link ingestion from third-party
    chat messages?
12. **[LQ 12]** What limitation-of-liability, indemnity, and disclaimer language is actually
    enforceable in the target jurisdictions, given that the product's foreseeable harms
    include loss of the customer's messaging account and third-party infringement claims?
    Which jurisdictions should be excluded from sale entirely (export controls, sanctions,
    and jurisdictions where Telegram itself is restricted)?
13. **[LQ 13]** Is per-seat or per-instance licence-key enforcement contractually enforceable
    against a customer who modifies a locally installed binary, and what anti-circumvention
    protection does the vendor have?

---

## 7. Sources

**Repository evidence** — cited inline as `path:line` throughout. Key files: `README.md`,
`Dockerfile`, `docker-compose.yml`, `installer/setup.iss`, all 11 `.csproj` files,
`TelegramClient/TelegramApp.cs`, `TelegramClient/Models/MemberEntry.cs`,
`TelegramClient/Models/HistoryEntry.cs`, `TelegramClient/ChatHistoryService.cs`,
`TelegramAutoDownload/MembersExportDialog.xaml.cs`,
`TelegramAutoDownload/Services/YtdlpService.cs`,
`TelegramAutoDownload.Headless/Scanning/RateLimitThresholds.cs`,
`TelegramAutoDownload.Headless/WebAuthService.cs`.

**Platform terms** (retrieved 2026-09-30)
- Telegram API Terms of Service — <https://core.telegram.org/api/terms>
- Telegram Terms of Service for Content Licensing — <https://telegram.org/tos/content-licensing>
- Telegram Terms of Service — <https://telegram.org/tos/>
- Telegram Bot Platform Developer Terms of Service — <https://telegram.org/tos/bot-developers>

**Licences**
- WTelegramClient (MIT) — <https://github.com/wiz0u/WTelegramClient/blob/master/LICENSE.txt>
- MonoTorrent (MIT) — <https://github.com/alanmcgovern/monotorrent/>
- Ytdlp.NET (MIT; 3.0.4 deprecated) — <https://github.com/manusoft/Ytdlp.NET>, <https://www.nuget.org/packages/Ytdlp.NET>
- yt-dlp (Unlicense; PyInstaller builds GPLv3+) — <https://github.com/yt-dlp/yt-dlp/blob/master/LICENSE>, <https://github.com/yt-dlp/yt-dlp?tab=readme-ov-file>
- yt-dlp THIRD_PARTY_LICENSES — <https://github.com/yt-dlp/yt-dlp/blob/2026.03.13/THIRD_PARTY_LICENSES.txt>
- FFmpeg licensing — <https://raw.githubusercontent.com/FFmpeg/FFmpeg/master/LICENSE.md>
- Debian ffmpeg copyright (GPL-2+ for default build) — <https://tracker.debian.org/media/packages/f/ffmpeg/copyright-74.3.7-0deb11u1>
- Debian ffmpeg build rules (`--enable-gpl`) — <https://tracker.debian.org/media/packages/f/ffmpeg/rules-78.1-3>
- Debian `libavcodec-extra59` (GPL-3+) — <https://packages.debian.org/bookworm/amd64/libavcodec-extra59>
- yt-dlp/FFmpeg-Builds variants — <https://github.com/yt-dlp/FFmpeg-Builds/tree/master/variants>
- FluentAssertions v8 relicensing — <https://github.com/Fluentassertions/Fluentassertions/pull/2943>, <https://www.infoq.com/news/2025/01/fluent-assertions-v8-license/>

**Content liability**
- *Yout LLC v. RIAA*, D. Conn. 2022, ruling on motion to dismiss — <https://ecf.ctd.uscourts.gov/cgi-bin/show_public_doc?2020cv1602-61=>
- Second Circuit briefing, *Yout LLC v. RIAA*, No. 22-2760 — <https://storage.courtlistener.com/recap/gov.uscourts.ca2.59200/gov.uscourts.ca2.59200.76.0.pdf>
- RIAA §1201 notice re youtube-dl, 23 Oct 2020 — <https://github.com/github/dmca/blob/master/2020/10/2020-10-23-RIAA.md>
- EFF, "GitHub Reinstates youtube-dl After RIAA's Abuse of the DMCA" — <https://www.eff.org/deeplinks/2020/11/github-reinstates-youtube-dl-after-riaas-abuse-dmca>

**Marketplace**
- Microsoft Store Policies v7.20 — <https://learn.microsoft.com/en-us/windows/apps/publish/store-policies>

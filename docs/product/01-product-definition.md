# Phase 1 — Product Definition

**Author:** Wave 1, Agent F (read-only analysis)
**Branch:** `enterprise-readiness`
**Date:** 2026-09-30

> This document depends on the legal findings in `docs/compliance/06-compliance.md`. Where a
> segment or feature is constrained by those findings, it is cross-referenced. Read that
> document first — several otherwise-attractive segments are ruled out by it.

**Evidence convention:** **[FACT]** verified in-repo or from a cited source;
**[ASSESSMENT]** the author's judgement.

---

## 1. What the product actually is today

**[FACT]** Two deployable surfaces exist from one codebase:

| Surface | Entry point | Distribution | Evidence |
|---|---|---|---|
| **WPF desktop app** (Windows 10/11) | `TelegramAutoDownload` | Inno Setup installer + portable ZIP via GitHub Releases | `installer/setup.iss`, `.cursor/rules/release-process.mdc`, `README.md:76-81` |
| **Headless web service** | `TelegramAutoDownload.Headless` (ASP.NET Core, port 8080) | Docker image | `Dockerfile:32-53`, `docker-compose.yml` |

**[FACT]** The `docker-compose.yml` volume paths are `/volume1/docker/telegram_downloader`
and `/volume1/homes/work/telegram_downloader` (`docker-compose.yml:26-27`). `/volume1` is the
Synology DSM default volume path, and `user: "1029:100"` (`docker-compose.yml:11`) matches
Synology's UID/GID convention for a non-admin user in the `users` group.

**[ASSESSMENT]** The author is already running this on a Synology NAS. That is not a footnote —
it is the single strongest product signal in the repository. A 24/7, always-on, low-power box
that already stores the user's media library is the natural home for an unattended
media-archiving daemon, and it is where the product should be aimed. The Windows desktop app,
despite being the more polished surface today, is the worse fit for the actual job: a desktop
PC is off or asleep exactly when an archiver needs to be running.

**[FACT]** Current capability set, from `README.md`: per-chat monitoring with media-type and
minimum-size filters, regex filename exclusion, folder templating with date/chat tokens,
deduplication by Telegram document ID, resumable `.part` downloads, retroactive history sync,
configurable parallelism (1–10), quiet hours, session and all-time statistics, disk-space
monitoring, emoji reactions on download start/complete, optional bot notifications, auto-update
from GitHub Releases, a four-plugin download chain, JSONL chat-history logging, and channel
member export.

---

## 2. Target customer segments

### Segment A — Individual power user ("the hoarder")

**Who:** A technically confident individual who belongs to many Telegram channels and groups
that post media, and wants a local copy. Typically already runs a NAS, Plex or Jellyfin, and
an \*arr stack. Overlaps heavily with the self-hosted/homelab community.

**Jobs to be done:**
- "Everything posted in these 30 channels should land in my media library without me touching it."
- "I joined this channel late — go back and get the 4,000 files I missed."
- "Don't download the same file twice, and don't fill my disk with thumbnails."
- "Put things in folders my Plex/Jellyfin library already understands."

**[ASSESSMENT]** This segment is *already served* by the product as built. The feature set —
dedup, regex filters, folder templates, resume, quiet hours — reads like a list of things a
media hoarder asked for, which is a good sign of authentic product-market fit.

**Deployment expectation:** Self-hosted, Docker on NAS or a home server. Strong preference for
a container and a web UI over a Windows desktop app. Will absolutely not send Telegram
credentials to a third party — this segment self-hosts *because* of that instinct.

**Willingness to pay: [ASSESSMENT]** USD 0–40 **one-time**, or 3–5/month with visible
resistance. This segment is price-sensitive, sophisticated about alternatives, and has a
strong cultural expectation that self-hosted tools are free. They pay for polish, reliability,
and not having to maintain it themselves — but only modestly. Critically, they *do* pay for
one-time "pro" unlocks (the Plex Pass and Unraid licence models both work on this audience).

**Risk:** This is the segment most likely to fork an MIT-licensed codebase rather than pay.
See `06-compliance.md` §1.2.

### Segment B — Team / media-archive operator

**Who:** A small organisation that treats Telegram as a content source: a newsroom monitoring
channels for source material, an OSINT or research team, a marketing agency archiving
competitor channels, a community manager preserving a large group's media history. 2–20 people,
one or a few shared Telegram accounts.

**Jobs to be done:**
- "Continuously archive these sources so nothing is lost when a channel is deleted."
- "Multiple analysts need to see the same archive and know it is complete."
- "Show me what arrived, from where, and when — with an audit trail."
- "Prove the archive is complete for the period we say it covers."

**[ASSESSMENT]** This is the **best commercial segment** — meaningfully higher willingness to
pay than Segment A, a real budget line, and genuinely unmet needs (multi-user access, audit,
completeness reporting, alerting) that the product does not yet have. It is also legally
tractable: the customer is a controller with a documented purpose, which is exactly the
posture GDPR legitimate-interests analysis needs (`06-compliance.md` §3.2).

**Deployment expectation:** Self-hosted, Docker on a server or NAS, behind their own reverse
proxy and SSO if possible. Will ask about backup, upgrade path, and support response time.

**Willingness to pay: [ASSESSMENT]** USD 150–600/year per deployment. They are buying
reliability and support, not features.

**Gap to close:** Multi-user access with roles, audit logging, retention policy enforcement,
completeness/gap reporting, and alerting on failure. None of these exist today.

### Segment C — Compliance-driven enterprise archival

**Who:** A regulated organisation (financial services, healthcare, legal) that must retain
business communications, where staff use Telegram and the regulator does not care that
Telegram is inconvenient. The buyer is Compliance or Legal, not IT.

**Jobs to be done:**
- "Retain all business communications on this channel for N years, immutably."
- "Produce everything from one person or one date range, on demand, for an audit or legal hold."
- "Prove the archive has not been tampered with."
- "Satisfy our DPO that this is lawful, and our CISO that it is safe."

**[ASSESSMENT] — This segment should not be pursued yet, and the reason is legal, not
technical.** It is the highest willingness to pay and the highest apparent prize, which makes
it tempting; it is also the segment the current legal posture most clearly cannot serve:

1. **The vendor must ask the customer to accept the risk of losing a business Telegram
   account** with no indemnity, because Telegram bans the account that runs the automation
   (`06-compliance.md` §2.3–2.4). Enterprise procurement will refuse this in writing.
2. **The product violates Telegram's API Terms today** (naming, sponsored messages) and cannot
   satisfy the full-client requirement by design (`06-compliance.md` §2.2). A compliance buyer
   cannot adopt a tool that breaches a third party's terms — that is the *specific* thing their
   job exists to prevent.
3. **Genuine compliance archival needs WORM storage, tamper-evident sealing, legal hold, and
   chain of custody.** The product writes plain appendable JSONL files
   (`TelegramClient/ChatHistoryService.cs:40-52`) with no integrity guarantee. That is a large
   build, not a feature flag.
4. Regulated buyers demand SOC 2 or ISO 27001, penetration test reports, vendor security
   review, and contractual indemnities. That is a multi-year, six-figure programme.

**[ASSESSMENT] Recommendation:** treat Segment C as a **v3+ ambition contingent on Telegram
granting written permission**, and stop describing the product as "enterprise-ready" until
then. Pursuing it now would mean selling assurances the product cannot honour. Segment B is
the adjacent, reachable version of the same revenue.

**Willingness to pay if ever reachable: [ASSESSMENT]** USD 5,000–50,000/year. Which is exactly
why it needs to be resisted until the legal foundation exists.

---

## 3. Positioning

**[ASSESSMENT]** Recommended positioning:

> **An unattended media archiver for Telegram, for your own NAS.**
> Point it at the channels and groups you care about, and it keeps a complete, deduplicated,
> organised local copy — without sending your account or your files to anyone else.

Deliberate choices in that sentence:

- **"for your own NAS"** — leads with self-hosting as the *feature*, which converts the legal
  constraint (§3.3 of the compliance doc rules out SaaS) into the privacy benefit the target
  audience actually wants.
- **"archiver"** not "downloader" — "downloader" is the word that attracts rights-holder
  attention and app-store rejection. "Archiver" describes the same function with the durable,
  legitimate framing.
- **No "Telegram" in the product name itself** — required by Telegram API ToS §2.3
  (`06-compliance.md` §2.2). "for Telegram" as a descriptor in marketing copy is a different
  question for counsel (Legal Question 5) but is a much weaker claim than a product title.
- **Media-first, not history-first** — leads with the defensible core feature, not the JSONL
  logging or member export that carry the legal risk.

---

## 4. Proposed pricing tiers

**[ASSESSMENT]** Structure assumes the recommended self-hosted licence-key model
(`docs/gtm/07-marketplace-readiness.md` §2). Prices are USD and are a starting hypothesis to
be tested, not a finding.

| | **Free** | **Personal** | **Team** |
|---|---|---|---|
| **Price** | 0 | **39 one-time** (or 15/yr for updates) | **299/yr per deployment** |
| **Target** | Segment A trial / evaluation | Segment A | Segment B |
| Monitored chats | **3** | Unlimited | Unlimited |
| Parallel downloads | 1 | Up to 10 | Up to 10 |
| Retroactive history sync | Last 100 messages | Unlimited | Unlimited |
| Folder templates, regex filters | — | Yes | Yes |
| Quiet hours, scheduling | Yes | Yes | Yes |
| JSONL history logging | — | Yes | Yes |
| **Retention / TTL policies** | — | Basic | **Per-chat, enforced** |
| **Multi-user + roles** | — | — | **Yes** |
| **Audit log** | — | — | **Yes** |
| **Completeness / gap reports** | — | — | **Yes** |
| **Alerting on failure** (webhook, email) | — | Basic | **Yes** |
| **Subject search + erase** (GDPR tooling) | — | — | **Yes** |
| Generic-URL / YouTube plugin | — | **Opt-in, self-hosted only** | **Opt-in, self-hosted only** |
| Torrent plugin | **Removed** | **Removed** | **Removed** |
| Member export | **Removed** | **Removed** | **Removed** |
| Support | Community | Email, best effort | Email, 2 business days |

### What gates each tier, and why

**[ASSESSMENT]** The gate has to be something a customer's usage naturally grows past, must be
cheap to enforce locally, and must not feel punitive.

- **Free → Personal: the 3-chat limit.** The best gate available. Anyone who finds the product
  useful monitors more than three chats almost immediately, so the limit converts quickly
  without crippling evaluation. It is a single integer check against the enabled-chat count,
  trivially enforceable offline, and it does not degrade the experience of what the user *can*
  do — which matters, because an archiver that throttles speed in the free tier feels broken
  rather than limited.
- **Personal → Team: multi-user, audit, and retention enforcement.** These are the features an
  organisation needs and an individual genuinely does not, so the boundary is self-selecting
  rather than artificial. Crucially, all three are also the features that make the customer's
  own GDPR compliance possible (`06-compliance.md` §3.2), which lets the Team tier be sold as
  "the tier your DPO needs" rather than "the tier with the higher number."
- **Deliberately not gated: quiet hours and rate limiting.** These reduce Telegram ban risk.
  Putting safety controls behind a paywall would push free users into the behaviour most
  likely to get their accounts banned and the product blamed. Ship them to everyone.
- **Deliberately not gated: the removals.** Torrent and member export are absent from *every*
  tier, including paid. They are not premium features being withheld; they are risks being
  eliminated (`06-compliance.md` §4.4).

**[ASSESSMENT] On one-time versus subscription for Personal:** the homelab audience strongly
prefers one-time purchase, and fighting that preference costs conversions. The Unraid and Plex
Pass pattern — perpetual licence for the current major version, optional paid upgrade to the
next — matches this audience's expectations and still produces recurring revenue. Team should
be annual subscription, because organisations expect and budget for it.

---

## 5. Deployment expectations by segment

| | Segment A | Segment B | Segment C (aspirational) |
|---|---|---|---|
| Form factor | Docker on NAS | Docker on NAS or server | Hardened VM / K8s |
| Install route | NAS app catalogue, or `docker compose` | Compose or Helm | Vendor-assisted |
| Network | LAN only | LAN + reverse proxy | Segmented, egress-controlled |
| Auth | Single password | SSO desirable | SSO mandatory |
| Backup | "Don't lose my config" | Documented backup/restore | Tested DR |
| Upgrade | In-place, automatic | Pinned tags, changelog | Change-controlled |
| Data location | Their disk, non-negotiable | Their disk | Their disk, encrypted |

**[ASSESSMENT]** Every segment wants self-hosted. This is a fortunate alignment: the legal
analysis rules out vendor hosting (`06-compliance.md` §3.3), and no segment wants it. There is
no revenue being left on the table by refusing to build SaaS — it would have been a harder
product to sell *and* an illegal one.

---

## 6. Product gaps blocking the recommended plan

**[ASSESSMENT]** Ordered by commercial value per unit of effort. Items marked **(legal)** are
required by the compliance findings rather than by customer demand, and should not be
deprioritised on the grounds that no customer asked for them.

| # | Gap | Why it matters | Effort |
|---|---|---|---|
| 1 | **Rename to drop "Telegram" from the title** | Fixes a verified ToS violation; highest value per hour in the whole assessment | S **(legal)** |
| 2 | **Retention / TTL for histories, media, exports** | GDPR storage limitation; also a genuine Segment B feature | M **(legal)** |
| 3 | **Remove torrent plugin and member export** | Removes the two worst liabilities | S **(legal)** |
| 4 | **Ban-risk disclosure + hard rate caps** | Makes the Telegram position defensible; protects users | S **(legal)** |
| 5 | **Licence-key entitlement + 3-chat gate** | Without this there is no business | M |
| 6 | **Remove default `admin`/`changeme` credentials** | Fails any enterprise security review as-is | S **(legal)** |
| 7 | **Stop auto-downloading yt-dlp/ffmpeg** | Improves §1201 and GPL posture at near-zero product cost | S **(legal)** |
| 8 | **Ship `LICENSE`, `NOTICE`, `SECURITY.md`** | Cannot sell without them | S **(legal)** |
| 9 | Multi-user + roles | Gates the Team tier | L |
| 10 | Audit log | Gates the Team tier; supports Segment B's "prove it" job | M |
| 11 | Subject search + erase | GDPR Art. 15/17 tooling; real differentiator | M |
| 12 | Completeness / gap reporting | Segment B's core unmet need | M |
| 13 | NAS catalogue packaging (Synology/Unraid/TrueNAS) | The primary distribution channel | M |
| 14 | Code signing for Windows | SmartScreen; prerequisite for MSIX/Store | S (+ cert cost) |

**[ASSESSMENT]** Items 1, 3, 4, 6, 7, and 8 are together perhaps two weeks of work and
resolve most of the acute legal exposure. They should precede any feature development. Item 5
is the minimum viable business. Items 9–12 are the Team tier and the actual revenue.

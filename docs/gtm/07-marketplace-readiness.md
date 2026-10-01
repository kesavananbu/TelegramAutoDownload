# Phase 7 — Commercial Model & Marketplace Readiness

**Author:** Wave 1, Agent F (read-only analysis)
**Branch:** `enterprise-readiness`
**Date:** 2026-09-30

> Depends on `docs/compliance/06-compliance.md` (legal findings) and
> `docs/product/01-product-definition.md` (segments and pricing). This document answers the
> **open question #1** in `docs/assessment/progress-ledger.md`: which commercial model, and
> what it takes to ship it.

**Evidence convention:** **[FACT]** verified in-repo or from a cited source;
**[ASSESSMENT]** the author's judgement.

---

## 1. Evaluating the three commercial models

Each model is scored against the legal findings, not against its general commercial appeal.

### Model 1 — Self-hosted with a licence key

| Dimension | Assessment |
|---|---|
| **Telegram terms** | Best available position. The **user** holds their own `api_id` and their own session; the vendor never touches the platform. No sub-licensing question arises (`06-compliance.md` §2.1). |
| **GDPR** | Vendor is **neither controller nor processor** — just a software supplier. No DPA, no Art. 30 record, no Art. 33 exposure, no DPIA (`06-compliance.md` §3.2). |
| **Content liability** | Vendor is a tool supplier, not an operator. *Sony*-style substantial-non-infringing-use argument is available for the core feature (`06-compliance.md` §4.1). |
| **Copyleft** | Only the Docker image's GPL-2+ ffmpeg needs handling, and it can be removed or complied with (`06-compliance.md` §1.4). |
| **Revenue quality** | One-time or annual per-deployment. No hosting cost, no infrastructure margin risk. |
| **Enforcement** | Weakest link: a local binary can be patched. Mitigations in §3. |
| **Fit with segments** | Every segment wants this (`01-product-definition.md` §5). |

### Model 2 — Vendor-hosted SaaS

| Dimension | Assessment |
|---|---|
| **Telegram terms** | **Fatal.** The platform data licence is expressly *"non-transferable and non-sublicensable"* (`06-compliance.md` §2.1). A hosted service accessing Telegram for customers is doing the thing the licence withholds. |
| **GDPR** | Vendor becomes processor for every customer and holds an aggregated store of **session tokens that confer full control of customers' personal messaging accounts.** Mandatory DPIA, 72-hour breach duty, and no lawful basis for the third-party message content (`06-compliance.md` §3.3). |
| **Content liability** | Vendor's own infrastructure performs the downloads — moves from "tool vendor" to "operator," and makes §512 safe-harbour registration and DSA notice-and-action obligations live (`06-compliance.md` §4.5). |
| **Insurance** | A breach is mass account takeover. Honest disclosure makes cyber cover punitive or unavailable. |
| **Verdict** | **Ruled out.** |

**[ASSESSMENT] Stated plainly, as requested:** the legal analysis **does rule out vendor-hosted
SaaS.** Not "makes it difficult" — the non-sublicensable platform licence means the model's
central activity is one Telegram has specifically declined to permit, and the session-token
custody risk is severe enough to be a company-ending event on its own. This should be taken off
the roadmap rather than deferred, and the architecture work that multi-tenancy would require
should not be started.

### Model 3 — Open-core with paid tiers

| Dimension | Assessment |
|---|---|
| **Telegram terms** | Same as Model 1 — fine. |
| **GDPR** | Same as Model 1 — fine. |
| **Content liability** | **Worse than Model 1.** Publishing the source of the yt-dlp orchestration and (if retained) torrent plugin keeps the vendor publicly associated with the §1201-exposed capability even in the free tier, and a permissively licensed core invites forks that strip the safeguards while retaining the vendor's name in their lineage (`06-compliance.md` §4.2). |
| **Revenue** | Requires a clean, defensible boundary between open core and paid layer. |
| **Feasibility here** | **Poor, on the current architecture.** The plugin system is a *runtime DLL loader* — plugins are discovered from `Plugins/<Name>/` directories (`TelegramAutoDownload.csproj:43-65`). That is an excellent extensibility design and an almost indefensible licence boundary: if the core is open and loads arbitrary plugin DLLs, the community will simply publish a free replacement for whatever the paid plugin does. |
| **Relicensing risk** | Depends on Legal Questions 1 and 2 (`06-compliance.md` §6). |

**[ASSESSMENT]** Open-core is the model most people reach for and the wrong one here. The
paid layer would have to be the *entitlement check itself*, which is trivially removable in an
open codebase, or the Team features — which would then have to be architected as a separate
closed service rather than as plugins. That is real work for no legal benefit over Model 1.

### Recommendation

**Model 1 — self-hosted, licence-key gated, source-available at most (not open source).**

**[ASSESSMENT] The single deciding reason:** it is the only model in which **the vendor never
holds the customer's Telegram session tokens, message content, or third parties' personal
data** — which simultaneously resolves the GDPR controller/processor problem, sidesteps
Telegram's non-sublicensable data licence, and keeps the vendor out of the DMCA §1201 and
distribution-liability chain. Every other legal finding in this assessment becomes manageable
once the vendor is a software supplier rather than a data custodian. Models 2 and 3 each give
that position away — Model 2 by taking custody of the data, Model 3 by publishing the
liability-bearing code.

---

## 2. Consequences of the recommendation

**[ASSESSMENT]** Adopt these as product constraints, not preferences:

1. **No vendor-operated instance. Ever.** Not even a "demo" that logs into a real Telegram
   account, and not a "managed hosting" upsell. The moment one exists, the §3.3 analysis
   applies and the clean position is gone.
2. **Source-available, proprietary licence** — not MIT (`06-compliance.md` §1.2). Source
   visibility is a trust feature this audience values; source *freedom* destroys the business.
3. **No vendor-operated telemetry endpoint by default** (§5 below).
4. **Torrent plugin and member export removed from all builds** (`06-compliance.md` §4.4).
5. **Generic-URL/YouTube plugins: self-hosted only, opt-in, no vendor-supplied yt-dlp**, and
   excluded entirely from any app-store submission.

---

## 3. Licensing, entitlement, and billing

### 3.1 Entitlement enforcement

**[ASSESSMENT]** The threat model must be honest: a determined user with a local binary will
always be able to defeat a local check. The goal is not to stop that person. It is to make the
licensed path obviously easier than the unlicensed one for the 95%+ who would rather pay than
patch, and to make circumvention a deliberate act the vendor can point to in a dispute.

**Recommended mechanism — offline-verifiable signed licence file:**

- Vendor holds an Ed25519 (or RSA-2048) signing key. Never ships the private key.
- On purchase, a licence payload is signed and delivered: `{ licenceId, customer, tier,
  issuedAt, expiresAt, maxChats, features[] }`.
- The app embeds only the **public** key and verifies the signature locally.
- **No network call is required to validate.** This is essential: the target deployment is a
  NAS that may be on an isolated VLAN, and a product that stops working when it cannot reach a
  licence server will be rejected by exactly this audience.
- A grace period and clear, non-punitive degradation on expiry: revert to Free-tier limits,
  never delete data, never stop in-flight downloads.

**Optional online activation** for the Team tier only, to bind a licence to a deployment ID
and detect gross over-sharing. Must fail *open* — a licence server outage must not break a
customer's archiver.

**[ASSESSMENT] What to gate, mechanically:** the 3-chat Free limit is a single integer
comparison against the count of chats with monitoring enabled. Feature flags for Team
capabilities read from the verified payload. Both are simple; the discipline required is
resisting the temptation to add online checks.

**[LEGAL QUESTION — carried as LQ 13 in the compliance doc]** Is the licence contractually
enforceable against a customer who patches the binary, and what anti-circumvention protection
does the vendor have? (Ironically, the vendor's own position here is the mirror image of the
§1201 argument in `06-compliance.md` §4.2.)

### 3.2 Billing integration options

**[ASSESSMENT]** Requirements: sells to individuals and businesses internationally, handles
VAT/sales-tax remittance, issues invoices a business can expense, and can generate the signed
licence file on payment via webhook.

| Option | Merchant of record | Tax handled | Fees (approx.) | Assessment |
|---|---|---|---|---|
| **Paddle** | **Yes** | **Yes — fully** | ~5% + 50c | **Recommended.** MoR means Paddle owns global VAT/sales-tax registration and remittance, which for a solo vendor selling worldwide is the difference between viable and not. Strong fit for desktop/self-hosted software licensing. |
| **Lemon Squeezy** | **Yes** | **Yes** | ~5% + 50c | Close second; good licence-key API built in. Now Stripe-owned, so watch for platform changes. |
| **Stripe + Stripe Tax** | No — vendor is MoR | Calculates; **vendor must register and remit** | ~2.9% + 30c + Tax fee | Cheapest on paper, most expensive in practice for a small vendor: the vendor personally carries global tax-registration obligations. Choose only with an accountant engaged. |
| **Gumroad** | Yes | Yes | ~10% | Simplest, highest fee, weakest B2B invoicing. Fine for a first 90-day validation. |

**Recommendation: Paddle**, with a webhook that signs and emails the licence file on payment,
and a customer portal for licence retrieval and seat management. **[ASSESSMENT]** The
merchant-of-record point dominates the fee difference; a 2% saving is irrelevant next to an
unmanaged EU VAT registration exposure.

**[ASSESSMENT] One caution:** payment processors maintain prohibited-business lists that
commonly include piracy-adjacent tooling. Removing the torrent plugin
(`06-compliance.md` §4.3) is not only a legal decision — it materially reduces the chance of
losing the billing rail, which for a one-person business is an existential risk that is easy
to overlook.

---

## 4. Distribution channel requirements

### 4.1 Docker Hub / GitHub Container Registry — primary channel

**[ASSESSMENT]** Lowest friction, no gatekeeper, and where the target audience already looks.
This should be the reference distribution.

**[FACT] Current state and gaps:**

| Requirement | Current | Gap |
|---|---|---|
| Multi-arch (`linux/amd64`, `linux/arm64`) | Single-arch build (`Dockerfile:4, 32`) | **ARM64 is essential** — most Synology and all Raspberry Pi/ARM NAS units need it. Requires `docker buildx` in CI. |
| Non-root container user | Set in compose (`docker-compose.yml:11`) but **not in the image** — no `USER` directive in `Dockerfile` | Add `USER` to the Dockerfile so the image is safe by default, not only when the operator remembers. |
| Immutable version tags | Compose uses `:latest` (`docker-compose.yml:8`) | Publish `vX.Y.Z`, `vX.Y`, `vX` alongside `latest`. `:latest` is unacceptable for the Team tier. |
| Image signing / provenance | None | Cosign signature + SLSA provenance attestation. Increasingly expected, and cheap in GitHub Actions. |
| SBOM | None | Publish CycloneDX or SPDX per image. Enterprise buyers ask; also discharges part of the NOTICE obligation. |
| Vulnerability scanning | None visible | Trivy or Grype gate in CI. |
| Healthcheck | None in `Dockerfile` | Add `HEALTHCHECK` against the port-8080 endpoint; NAS UIs surface it. |
| **GPL-2+ ffmpeg compliance** | **Missing** — `Dockerfile:36` | **Blocking.** Either remove ffmpeg from the image or ship licence texts plus a written source offer (`06-compliance.md` §1.4). |
| Published image labels | None | OCI labels: `org.opencontainers.image.source`, `.licenses`, `.version`, `.description`. |
| Secrets handling | Default `admin`/`changeme` in compose (`docker-compose.yml:16-17`) | **Blocking.** Remove defaults; force first-run credential setup. |

### 4.2 Microsoft Store / signed MSIX

**[FACT]** Microsoft Store Policies (v7.20) §11.2 requires that all content and metadata be
*"originally created by the application provider, appropriately licensed from the third-party
rights holder, used as permitted by the rights holder, or used as otherwise permitted by law."*
There is **no** policy provision specifically banning media downloaders — the frequently cited
"10.8.7" governs pricing, not downloading — but §11.2 is the hook a rights-holder complaint
would use, and Microsoft operates an IP-infringement reporting form for exactly this.

**[FACT]** Telegram API ToS §4.2 states that on unremedied breach Telegram will *"contact the
app stores about the removal of your apps."*

**[ASSESSMENT] Assessment: the Microsoft Store is a poor fit and should not be a launch
channel.** Three reasons:

1. The generic-URL/yt-dlp plugin is the exact capability that draws §11.2 complaints. It must
   be excluded from any Store build — at which point the Store version is a reduced product.
2. Telegram has a stated, specific escalation path to app stores, and the product currently
   breaches two of its clauses. Submitting while in breach invites the takedown.
3. MSIX packaging conflicts with how the app works today: it writes to
   `%APPDATA%\TelegramAutoDownload\` (`TelegramClient/TelegramApp.cs:101-104`), downloads and
   executes external binaries into that folder (`Services/YtdlpService.cs:82-89, 137-166`), and
   self-updates from GitHub Releases (`README.md:47-49`). MSIX runs in a container with
   virtualised writes and **forbids self-update outside the Store channel** — the auto-updater
   would have to be removed, and the yt-dlp fetch-and-execute pattern is both awkward under
   MSIX and an antivirus-heuristic magnet.

**[ASSESSMENT] Recommendation:** skip the Store. **Do still adopt code signing**, which is
valuable independently:

| Requirement | Current | Gap |
|---|---|---|
| Code-signing certificate (OV or EV) | **None** — `installer/setup.iss` has no `SignTool` directive | Buy an OV/EV cert. EV gets immediate SmartScreen reputation; OV must build reputation over time. |
| Signed `.exe` and installer | Unsigned | Sign both the app and the setup binary. Unsigned installers trigger SmartScreen "unrecognised app" warnings that cost a large share of installs. |
| EULA in installer | `LicenseFile=` is **empty** (`installer/setup.iss:22`) | Point it at the `LICENSE` file — required if the licence is to be accepted at install. |
| `PrivilegesRequired=admin` | Set (`setup.iss:31`) | Reasonable for Program Files, but a per-user install option would reduce friction. |
| Reproducible/attested build | No | Nice-to-have. |

### 4.3 NAS app catalogues — the strongest fit

**[ASSESSMENT]** This is where the product belongs, and the `/volume1` paths in
`docker-compose.yml:26-27` confirm it is already how the author uses it. Priority order:

**Unraid Community Applications — do this first.**
**[ASSESSMENT]** Lowest barrier by a wide margin: the catalogue is a community-maintained
index of Docker templates, submission is a PR of an XML template to the templates repository,
and the Unraid audience is precisely Segment A. Requirements: a multi-arch image on a public
registry, an XML template declaring ports/volumes/env vars, an icon, a support thread URL, and
a WebUI declaration. Realistically days of work, not months.

**TrueNAS SCALE — second.**
**[ASSESSMENT]** Recent SCALE versions run Docker Compose directly, so a published, documented
compose file is most of the work. A formal catalogue app needs a Helm-style chart with
`questions.yaml` to render the config form. Requirements: multi-arch image, non-root,
explicit volume/permission handling, and a health endpoint.

**Synology Package Center — highest value, highest barrier.**
**[ASSESSMENT]** The natural home given the compose file, but the official Package Center is
curated and Synology-approved; third-party packages typically reach users through
**SynoCommunity** instead. Path: publish excellent Docker/Container Manager documentation
first (this alone serves most Synology users), then pursue SynoCommunity packaging (SPK,
build-framework integration), and treat official Package Center as a later goal requiring a
partner relationship. Requirements include an SPK manifest, DSM UI integration, ARM and x86
builds, and DSM version compatibility testing.

**Cross-cutting NAS requirements:**

| Requirement | Current | Gap |
|---|---|---|
| ARM64 image | No | **Blocking for most Synology/QNAP units** |
| Runs as arbitrary UID/GID | Compose sets `1029:100`; image has no `USER` | Must tolerate any UID and handle volume ownership gracefully |
| Data under declared volumes only | Yes — `VOLUME ["/data","/downloads"]` (`Dockerfile:50`) | Good |
| Documented backup/restore | No | Needed — `session.dat` loss forces full re-login |
| Reverse-proxy friendly | Listens on 8080 (`Dockerfile:47-48`) | Document subpath/subdomain behaviour and trusted-proxy headers |
| No default credentials | **Fails** (`docker-compose.yml:16-17`) | **Blocking** — catalogues increasingly reject shipped default passwords |
| Resource limits documented | No | Memory/CPU guidance for low-power NAS hardware |
| Graceful shutdown | Partial | Must checkpoint `.part` files on SIGTERM |

---

## 5. Telemetry design

**[ASSESSMENT]** An enterprise buyer will accept telemetry only if the defaults are
conservative, the payload is inspectable, and disabling it is trivial and complete. For this
audience — self-hosters who chose self-hosting specifically to avoid phoning home — the
threshold is higher still.

**Design principles:**

1. **Off by default. Opt-in, never opt-out.** For this audience, opt-out telemetry is a
   reputational event; it is what gets a project publicly criticised on r/selfhosted. The data
   is not worth it.
2. **No personal data, ever.** Explicitly excluded: phone numbers, chat names, chat IDs,
   usernames, file names, file paths, message content, IP addresses, and the member/history
   export payloads.
3. **Anonymous install ID only** — a random UUID generated locally, resettable by the user,
   with no linkage to the licence key or customer identity. Sending the licence key with
   telemetry would turn anonymous metrics into per-customer surveillance.
4. **Inspectable.** A UI screen showing the exact JSON that would be sent, plus a local
   log of every transmission. "You can read exactly what we send" is the only argument that
   actually persuades this audience.
5. **Aggregate and coarse.** Bucket everything: report "11–50 monitored chats," not 37.
6. **Fail silently and never block.** No feature may depend on telemetry reachability.
7. **Documented retention** — state the period (e.g. 12 months) and honour it.

**Acceptable payload:**

```json
{
  "installId": "<random uuid, user-resettable>",
  "appVersion": "2.9.3",
  "edition": "personal",
  "platform": "docker/linux-arm64",
  "osVersion": "6.2",
  "chatCountBucket": "11-50",
  "featuresEnabled": ["telegram-media", "history-jsonl"],
  "downloadCountBucket": "1000-5000",
  "errorCounts": { "flood_wait": 3, "download_timeout": 12 },
  "uptimeDaysBucket": "8-30"
}
```

**[ASSESSMENT]** `errorCounts` is the genuinely valuable field — aggregate `flood_wait`
frequency across the installed base is how the vendor learns whether the default rate limits
are actually safe (`06-compliance.md` §2.4), which is a safety feature for users rather than a
growth metric for the vendor. That is the honest justification for asking at all.

**Crash reporting** should be a **separate, separately-consented** channel. Stack traces can
contain file paths and chat names; scrub them, and ask before sending each report rather than
enrolling users once and forever.

---

## 6. Launch readiness checklist

**[ASSESSMENT]** Blocking items must be complete before taking money.

**Legal — blocking**
- [ ] `LICENSE` adopted at repo root and referenced from `installer/setup.iss:22`
- [ ] Product renamed to drop "Telegram" from the title (API ToS §2.3)
- [ ] In-app Telegram API disclosure added (API ToS §2.2)
- [ ] `NOTICE` / third-party attributions shipped in installer and image
- [ ] GPL-2+ ffmpeg either removed from the image or compliance artifacts shipped
- [ ] Torrent plugin removed; member export removed
- [ ] Generic-URL/YouTube plugins opt-in and off by default; auto-download of yt-dlp/ffmpeg removed
- [ ] Privacy policy, ToS, AUP finalised with counsel
- [ ] `SECURITY.md` published
- [ ] Ban-risk acknowledgement shown at first login
- [ ] Legal Questions 1–13 answered (`06-compliance.md` §6)

**Product — blocking**
- [ ] Licence-key verification + Free-tier 3-chat gate
- [ ] Default `admin`/`changeme` credentials removed; first-run setup forced
- [ ] Hard caps on rate-limit settings
- [ ] Retention/TTL controls for histories, media, and logs

**Distribution — blocking**
- [ ] Multi-arch (`amd64` + `arm64`) images
- [ ] Immutable semver tags
- [ ] `USER` directive in `Dockerfile`
- [ ] Code-signing certificate; signed `.exe` and installer

**Distribution — strongly recommended**
- [ ] SBOM per release; Cosign signatures; Trivy gate in CI
- [ ] `HEALTHCHECK` and OCI image labels
- [ ] Documented backup/restore
- [ ] Unraid Community Applications template submitted
- [ ] Paddle integration with signed-licence webhook

**Explicitly out of scope**
- [x] Vendor-hosted SaaS — **ruled out** (`06-compliance.md` §3.3)
- [x] Microsoft Store submission — not a launch channel (§4.2)
- [x] Compliance-archival enterprise segment — deferred (`01-product-definition.md` §2, Segment C)

---

## 7. Sources

**Repository:** `Dockerfile`, `docker-compose.yml`, `installer/setup.iss`,
`TelegramAutoDownload.csproj`, `TelegramClient/TelegramApp.cs`,
`TelegramAutoDownload/Services/YtdlpService.cs`, `README.md`,
`.cursor/rules/release-process.mdc`.

**External** (retrieved 2026-09-30)
- Microsoft Store Policies v7.20 — <https://learn.microsoft.com/en-us/windows/apps/publish/store-policies>
- Microsoft Store Policies change history — <https://learn.microsoft.com/en-us/windows/apps/publish/store-policies-change-history>
- Telegram API Terms of Service — <https://core.telegram.org/api/terms>
- Telegram Terms of Service for Content Licensing — <https://telegram.org/tos/content-licensing>
- Debian ffmpeg copyright — <https://tracker.debian.org/media/packages/f/ffmpeg/copyright-74.3.7-0deb11u1>

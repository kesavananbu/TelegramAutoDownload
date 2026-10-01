# Terms of Service — OUTLINE ONLY

<!--
This is an OUTLINE, not a draft. See ./README.md.

Terms of Service must be drafted by counsel: enforceability of liability caps, disclaimers,
and consumer-law carve-outs is jurisdiction-specific, and this product's foreseeable harms
(loss of the customer's messaging account; third-party infringement claims) make the liability
provisions the most important clauses in the document.
-->

**Status:** Outline for counsel · **Not publishable as-is**

---

## A. Document architecture

Four documents, with a clear hierarchy. Counsel should decide whether to merge the ToS and
EULA; keeping them separate is cleaner because they govern different relationships.

| Document | Governs | Status |
|---|---|---|
| **Terms of Service** | The commercial relationship: purchase, payment, updates, support, term, liability | This outline |
| **End-User Licence Agreement** | The software licence grant and restrictions | Drafted — `./LICENSE` Option A |
| **Acceptable Use Policy** | What the customer may and may not do | Drafted — `./acceptable-use-policy.md` |
| **Privacy Policy** | Data handling | Outline — `./privacy-policy-outline.md` |

All three subordinate documents should be incorporated into the ToS by reference, with a
stated order of precedence for conflicts.

---

## B. Decisions the owner must make first

1. **Legal entity and jurisdiction** — governing law and venue.
2. **Consumer or B2B, or both?** Selling to consumers in the EU/UK triggers mandatory
   protections that override contract terms: a 14-day withdrawal right (with a specific
   waiver mechanism for immediately supplied digital content), statutory conformity rights,
   and limits on liability exclusions. Selling B2B-only is materially simpler but cuts off
   Segment A (`../../product/01-product-definition.md` §2).
3. **Refund policy** — see §C.4. Must be reconciled with the EU withdrawal right.
4. **Support commitments** — response targets by tier, and whether they are obligations or
   targets. Do not promise what one person cannot deliver.
5. **Perpetual licence or subscription** for the Personal tier
   (`../../product/01-product-definition.md` §4 recommends perpetual with paid major upgrades).
6. **Excluded jurisdictions** — sanctions, export controls, and jurisdictions where Telegram
   itself is restricted or where the product's function is unlawful.

---

## C. Required sections

### 1. Agreement and acceptance
Who the parties are; how acceptance occurs (purchase, installation, or use); capacity and
minimum age; authority to bind an organisation.

### 2. The service and the software
What is supplied: a licence to self-hosted software, plus updates and support per the tier
purchased. **State explicitly that no hosted service is provided**, that the customer is
responsible for providing and operating the hardware, and that the vendor has no access to
the customer's installation or data.

### 3. Editions, licence keys, and entitlements
What each tier includes; how keys are delivered; permitted deployments per key; that keys must
not be shared or published; that entitlement checks may not be circumvented (cross-reference
EULA §2(b)); graceful degradation on expiry — revert to Free limits, never delete data, never
interrupt in-flight work.

### 4. Fees, payment, taxes, and refunds
Pricing and currency; merchant of record; taxes; renewal and cancellation terms for
subscriptions; price-change notice.

**Refunds — reconcile carefully:**
- EU/UK consumers have a **14-day right of withdrawal** for digital content, which is lost
  only if the consumer gives prior express consent to immediate supply *and* acknowledges
  losing the right. If the licence key is delivered instantly, that consent flow must exist
  in the checkout.
- **[ASSESSMENT]** For this audience a plain 30-day no-questions refund is commercially
  better than the legal minimum, and removes the need to adjudicate withdrawal-right edge
  cases. It also offsets the trust deficit of an unknown vendor selling security-sensitive
  software.

### 5. Updates, versions, and end of life
What updates are included; whether major versions require a paid upgrade; the auto-update
mechanism and how to disable it; support windows per version; **the notice period before a
feature is removed.**

**[ASSESSMENT]** This last point matters more than it looks. The compliance findings
recommend removing the torrent plugin and member export
(`../06-compliance.md` §4.4). Existing users rely on them. The ToS should reserve the right
to remove features for legal or safety reasons — with notice, and with a pro-rata refund
where a paid feature is withdrawn mid-term.

### 6. Customer responsibilities
Provide and secure the host; supply their own third-party API credentials; comply with the
AUP; comply with third-party platform terms; establish a lawful basis for any personal data
they process; maintain their own backups.

### 7. Third-party platforms — risk allocation
**The most important commercial section in the document.** Must state, prominently and in a
form that is genuinely brought to the customer's attention rather than buried:

- The vendor is independent of and unaffiliated with Telegram Messenger Inc.
- The product depends on Telegram's API, which Telegram may change, restrict, or withdraw at
  any time, and the data licence Telegram grants is **revocable**.
- **Automating a personal account may breach Telegram's terms and may result in the account
  being rate-limited, restricted, or permanently banned, with permanent loss of access to the
  account and its history.**
- The vendor cannot prevent, appeal, or reverse such action and accepts no liability for it.
- If the Telegram API changes such that the product ceases to function, the vendor's
  obligation is limited to [reasonable efforts to restore / pro-rata refund] — **decide which**.

**[ASSESSMENT] A contractual clause alone is not sufficient here.** The risk is severe,
specific, and unlikely to be anticipated by a customer reading marketing copy. It should also
be surfaced as a **one-time in-app acknowledgement at first login** that the user must
actively accept. That is both better ethics and better evidence of informed consent if the
clause is ever tested.

### 8. Telegram API disclosure
**Required by Telegram API Terms of Service §2.2**, which obliges the developer to make users
aware that the app uses the Telegram API and is part of the Telegram ecosystem, *"featured
prominently in the app's description in the app stores and in the in-app intro."*
`../06-compliance.md` §2.2 records this as currently only partially satisfied. Implement as:
a line in every store/catalogue listing, a line in the README, and a statement in the
first-run screen.

### 9. Intellectual property
Vendor retains all rights in the Software; customer retains all rights in their own data and
content; trademark restrictions; third-party components governed by `./NOTICE.md`.

### 10. Warranties and disclaimers
Limited warranty (if any) that the Software will perform substantially as documented;
otherwise "as is". **Must include a consumer-rights carve-out** for jurisdictions where
statutory rights cannot be excluded.

Do not warrant: uninterrupted operation, continued interoperability with Telegram,
completeness of any archive, or preservation of any data.

### 11. Limitation of liability
Exclusion of indirect and consequential loss; exclusion of loss of data, profits, and
goodwill; **specific exclusion of liability for loss, suspension, or ban of a third-party
account**; aggregate cap at fees paid in the preceding 12 months, or a stated floor.
Mandatory carve-outs for death/personal injury, fraud, and anything not excludable by law.

**[LEGAL QUESTION — LQ 12 in `../06-compliance.md` §6]** Counsel must advise what is actually
enforceable in each target jurisdiction, and whether a cap set at the fee level is
enforceable at all given that the foreseeable harm (loss of a messaging account, or an
infringement claim) may greatly exceed a USD 39 purchase price. A cap that a court would
strike as unconscionable provides no protection.

### 12. Indemnity
Customer indemnifies the vendor for: breach of the AUP; content the customer acquires, stores,
or distributes; and the customer's processing of third parties' personal data. Consider
whether a reciprocal IP indemnity from the vendor is commercially necessary for Segment B —
**[ASSESSMENT]** probably not at these price points, and offering one would be imprudent given
the unresolved §1201 question.

### 13. Term, suspension, and termination
Term; termination for convenience and for cause; the effect of termination (cease use;
entitlements lapse; data is not deleted by the vendor and cannot be); survival; refund
treatment on termination for cause versus convenience.

### 14. Export control and sanctions
Compliance representation; restricted-party and embargoed-jurisdiction exclusions; list of
jurisdictions from which purchase is not permitted.

### 15. Changes to the terms
Notice mechanism; notice period for material changes (suggest 30 days); the customer's right
to terminate and receive a pro-rata refund rather than accept a material adverse change.

### 16. Dispute resolution and governing law
Governing law; venue; whether arbitration is used; whether class-action waiver is included
(note it is unenforceable in several jurisdictions); consumer carve-out preserving the right
to bring proceedings in the consumer's country of residence.

### 17. General
Entire agreement; order of precedence; severability; no waiver; assignment; force majeure;
notices.

---

## D. In-app disclosures the ToS depends on

Contractual clauses are only as good as the disclosure supporting them. Three in-product
elements should be treated as deliverables of this document, not optional extras:

| # | Disclosure | When shown | Supports |
|---|---|---|---|
| 1 | **Ban-risk acknowledgement** — automating your account may result in permanent loss of it; we cannot recover it | Once, before first Telegram login; must be actively accepted | ToS §7, EULA §3.2, AUP §7 |
| 2 | **Telegram API disclosure** — this app uses the Telegram API and is part of the Telegram ecosystem; it is unofficial and unaffiliated | First-run screen and About dialog | **Telegram API ToS §2.2 and §2.3** |
| 3 | **Controller notice** — if you archive other people's messages you are the data controller and have legal obligations | When history logging is first enabled for a chat | Privacy Policy §8, AUP §4 |

**[ASSESSMENT]** Disclosure 2 is also the cheapest step toward remedying the verified Telegram
ToS breaches recorded in `../06-compliance.md` §2.2, and should ship alongside the product
rename regardless of the timeline for the rest of these documents.

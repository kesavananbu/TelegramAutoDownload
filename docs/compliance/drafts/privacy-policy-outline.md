# Privacy Policy — OUTLINE ONLY

<!--
This is an OUTLINE, not a draft policy. See ./README.md.

A privacy policy makes binding representations to users and to regulators. It must be written
by counsel against the product as actually shipped. What follows is the structure, the factual
content counsel needs, and the decisions the owner must make first.
-->

**Status:** Outline for counsel · **Not publishable as-is**

---

## A. Decisions the owner must make before counsel can draft

| # | Decision | Why it changes the policy |
|---|---|---|
| 1 | Is there a legal entity, and where is it established? | Determines the controller identity, lead supervisory authority, and whether an EU/UK representative (GDPR Art. 27) is needed |
| 2 | Is telemetry shipped, and is it opt-in? | If opt-in and off by default, the policy is short and the vendor processes almost nothing |
| 3 | Is crash reporting shipped, and separately consented? | Stack traces can contain file paths and chat names |
| 4 | Does licence activation make a network call? | Any call reveals an IP address and creates a processing activity |
| 5 | Who is the payment provider, and are they merchant of record? | With a merchant-of-record provider such as Paddle, they are the controller for payment data and the vendor never sees card data |
| 6 | Does the vendor operate a Seq endpoint or any log sink? | **If yes, the vendor becomes a processor of customer personal data and needs a DPA with every customer.** `../06-compliance.md` §3.5 recommends never doing this |
| 7 | Retention period for support correspondence and billing records? | Must be stated, and honoured |
| 8 | Will there ever be a vendor-hosted offering? | Would require a complete rewrite; `../06-compliance.md` §3.3 recommends never |

**[ASSESSMENT]** If decisions 2–6 follow the recommendations in
`../../gtm/07-marketplace-readiness.md` §5, this becomes one of the simplest privacy policies
a software company can have, because the vendor genuinely processes almost nothing. That is a
marketing asset for a self-hosting audience and should be protected, not traded away for
analytics.

---

## B. The central message the policy must convey

The policy must make one distinction unmistakably clear, because it is the single most
important fact about this product and users will otherwise assume the worst:

> **Two entirely separate categories of data exist.**
>
> **1. Data the Software processes on your own systems** — your Telegram phone number, session
> credential, the messages and media it downloads, and its logs. **This never reaches us.** We
> cannot see it, cannot access it, and could not produce it if compelled. For this data you
> are the controller and we are not a processor.
>
> **2. Data we process as a company** — your name and email when you buy a licence, support
> correspondence, and (if you opt in) anonymous product telemetry. This is a short list, and
> this policy explains all of it.

Structurally, the policy should treat category 2 as its subject and category 1 as a clearly
labelled disclosure, so it is never ambiguous which one a given statement applies to.

---

## C. Required sections

### 1. Who we are
Controller identity, registered address, contact, EU/UK representative if required, DPO if
appointed (likely not required under Art. 37).

### 2. Scope
Covers the website, purchase process, support, and optional telemetry. Does **not** cover the
customer's own use of the self-hosted Software — link to the data-inventory disclosure (§6).

### 3. Data we collect as a company

| Data | Purpose | Lawful basis | Retention |
|---|---|---|---|
| Name, email, billing country | Fulfil the purchase, deliver the licence key, tax compliance | Contract (Art. 6(1)(b)); legal obligation for tax records | Duration of relationship + statutory tax period |
| Licence key, edition, purchase date | Licence administration, support entitlement | Contract | As above |
| Support correspondence | Provide support | Contract / legitimate interests | [DECIDE — suggest 24 months] |
| Website analytics | Improve the site | Consent, if cookies used | [DECIDE] |
| Optional telemetry (anonymous install ID, version, platform, bucketed counters, error counts) | Improve reliability and the safety of default rate limits | **Consent** (Art. 6(1)(a)) — opt-in | [DECIDE — suggest 12 months] |
| Optional crash reports | Diagnose defects | **Consent**, separately obtained per report | [DECIDE] |

**Explicitly state what we do NOT collect**, itemised — this list is more persuasive than any
assurance: Telegram phone numbers; Telegram session credentials or API credentials; message
content; media files; chat, group, or channel names or identifiers; member lists; download
history; file names or paths; the contents of the data directory.

### 4. Payment data
State that payments are processed by [PROVIDER], that they act as merchant of record and as
controller for payment data, that we never receive or store card details, and link to their
policy.

### 5. Telemetry — the section that has to be genuinely convincing

This audience will read this section specifically, and vague assurances will be read as
evasion. It must state:

- It is **off by default** and requires an affirmative opt-in.
- The **exact JSON payload**, reproduced verbatim (see
  `../../gtm/07-marketplace-readiness.md` §5).
- That the install ID is a locally generated random UUID, **not linked to the licence key or
  customer identity**, and resettable by the user at any time.
- That counters are **bucketed**, not exact.
- That it can be disabled permanently in one place, and how.
- That no feature depends on it and disabling it degrades nothing.
- Where it is sent and how long it is kept.

### 6. Data the Software processes on your own systems — disclosure

Not "our" processing, but users need it and a regulator will expect the vendor to be
transparent about what its software does. Reproduce the inventory from
`../06-compliance.md` §3.1: what is stored, where (`%APPDATA%\[PRODUCT]\` or `/data`), and
what it contains. Include:

- **Plain warning about the session credential:** the file grants full access to the linked
  Telegram account; protect it and any backup of it as you would a password.
- The customer's role as **controller** for any third-party personal data the Software
  processes, and a pointer to the guidance in §8.
- Log retention defaults and how to change them.
- How to delete everything, including the session credential.

### 7. Your rights
Art. 15–22 rights over category 2 data: access, rectification, erasure, restriction,
portability, objection, withdrawal of consent, and the right to complain to a supervisory
authority (name the lead authority). State the response target (one month) and how to make a
request.

**Must also state the limitation honestly:** we cannot action a request concerning data held
in a customer's self-hosted installation, because we have no access to it. Data subjects
whose messages appear in a customer's archive must address the customer, who is the controller.

### 8. Guidance for customers who are controllers
Short, practical, and not legal advice: establish a lawful basis before archiving other
people's messages; configure retention; be able to find and erase records for one individual;
restrict access; note that special-category content (GDPR Art. 9) needs a stronger basis and
often has none available; note that the household exemption does **not** cover business use.

### 9. International transfers
Where category 2 data is processed and stored; transfer mechanism (SCCs / adequacy) for any
non-EEA processor; list sub-processors (payment provider, email provider, telemetry host, CDN).

### 10. Security
Measures protecting category 2 data. Reference `SECURITY.md` for vulnerability reporting. Do
not over-claim: no certifications should be implied that do not exist.

### 11. Children
State the minimum age (align with the ToS) and that the product is not directed at children.

### 12. Changes
How changes are notified, and the notice period for material changes.

### 13. Contact
[PRIVACY EMAIL], postal address, supervisory authority details.

---

## D. Drafting cautions

1. **Do not claim GDPR, SOC 2, or ISO 27001 compliance.** There is no basis for any of these
   today, and claiming them is a misrepresentation that creates liability far exceeding the
   marketing benefit.
2. **Do not use "we take your privacy seriously" boilerplate** with this audience. Specificity
   is the only thing that reads as credible.
3. **Do not obscure the member-export and history-logging capabilities** if they remain in the
   product. A policy that omits capabilities a user can discover by clicking around is worse
   than one that discloses them plainly.
4. **Keep the controller/processor line bright.** The moment the vendor operates *anything* that
   receives customer data — a log sink, a hosted demo, a "managed" tier — the policy needs a
   DPA and a rewrite. Protect this boundary as a product decision, not just a legal one.
5. **The "what we do not collect" list is the most valuable paragraph in the document.** Keep
   it itemised, keep it accurate, and make sure engineering knows that changing it requires a
   policy update.

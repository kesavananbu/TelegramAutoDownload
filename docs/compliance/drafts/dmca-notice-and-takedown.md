# Copyright Notice-and-Takedown Process — DRAFT

<!--
DRAFT — proposed. Needs counsel review before publication. See ./README.md.
Intended for publication at [WEBSITE]/dmca.
-->

**Applies to:** [PRODUCT NAME] · **Version:** Draft 0.1 · **Effective:** [DATE]

---

## 1. Important context — what we do and do not host

[LEGAL ENTITY NAME] publishes **self-hosted software**. We do not operate a hosting service,
a content platform, a search index, or a repository of user content.

This means:

- **We do not host, store, transmit, cache, or index any content that our customers acquire
  using the Software.** Content downloaded by a customer resides exclusively on hardware that
  the customer owns and controls.
- **We cannot remove such content**, because we never possess it and have no access to any
  customer's installation.
- We have no ability to identify what any particular customer has downloaded.

We publish this process anyway, because rights holders are entitled to a clear route to reach
us, and because there **are** actions we can and will take. Those are set out in Section 4.

> **Drafting note (remove before publication):** this framing is only accurate while the
> product remains self-hosted. If a vendor-hosted offering is ever introduced, this document
> must be rewritten around DMCA §512(c), which requires a **designated agent registered with
> the US Copyright Office** and an enforced repeat-infringer termination policy, plus EU DSA
> notice-and-action obligations. `../06-compliance.md` §3.3 recommends against ever hosting.

---

## 2. Designated contact

Send copyright notices to:

```
Copyright Agent
[LEGAL ENTITY NAME]
[REGISTERED ADDRESS]

Email: [LEGAL EMAIL]   (preferred — please use subject line "Copyright Notice")
```

We acknowledge receipt within **3 business days** and provide a substantive response within
**10 business days**.

---

## 3. How to submit a notice

To allow us to act, a notice should include all of the following. These elements mirror the
requirements of 17 U.S.C. §512(c)(3); we ask for the same information regardless of your
jurisdiction because it is what we need to assess a claim.

1. **Identification of the copyrighted work** you claim has been infringed. If your notice
   covers multiple works, a representative list is sufficient.
2. **Identification of the material or activity** you claim is infringing, and what you are
   asking us to do about it. **Please be specific** — see Section 4 for what we can act on.
3. **Your contact details**: name, postal address, telephone number, and email address.
4. **A statement** that you have a good-faith belief that the use is not authorised by the
   copyright owner, its agent, or the law.
5. **A statement** that the information in the notice is accurate, and — under penalty of
   perjury — that you are the copyright owner or authorised to act on the owner's behalf.
6. **Your physical or electronic signature.**

**Anti-circumvention claims.** If your notice concerns circumvention of a technological
protection measure (for example, under 17 U.S.C. §1201 rather than §512), please say so
explicitly and identify the measure. These claims are legally distinct from infringement
claims, they are not resolved by a takedown, and we will route them to counsel rather than
through this process.

**Notices we cannot act on.** We will respond, but cannot take substantive action, where a
notice:
- asks us to remove content we have never hosted or possessed;
- identifies a customer's private installation as the infringing location;
- consists only of automated swarm-monitoring output or IP address logs, with no identified
  work or specific request;
- asks us to disclose customer identities without valid legal process (see Section 5).

---

## 4. What we will actually do

We review every notice. Depending on what it identifies, we may take any of the following
actions.

### 4.1 Actions relating to the product itself

If a notice identifies a **feature or capability** of the Software as the cause of
infringement, we will assess it on the merits and, where appropriate:

- disable the capability by default, or place it behind an explicit user acknowledgement;
- restrict it to specific deployment models;
- add technical controls to limit the conduct complained of;
- **remove the capability entirely** in a future release.

We have done this before and will do it again where the assessment supports it: the
peer-to-peer/BitTorrent capability and the channel member export were removed from the product
for precisely these reasons.

### 4.2 Actions relating to our own materials

If a notice identifies material **we** publish — on our website, in documentation,
screenshots, marketing material, sample configurations, or in the repository — we will remove
or replace the infringing material promptly.

### 4.3 Actions relating to a customer

If a notice, or other credible information, indicates that an identifiable customer is using
the Software in breach of our Acceptable Use Policy:

| Step | Trigger |
|---|---|
| Written warning, with a remediation deadline | First credible report |
| Suspension of licence entitlements | Failure to remediate |
| **Termination of the licence, without refund** | Material or repeated breach |

We maintain and enforce a **repeat-infringer termination policy**: a customer who is the
subject of multiple substantiated infringement notices will have their licence terminated.

Terminating a licence stops the customer's entitlement to use and update the Software. It does
**not** delete data already on their systems, and we cannot make it do so.

### 4.4 What we will not do

- We will not remove content we do not host, because we cannot.
- We will not disclose customer identities or data without valid legal process (Section 5).
- We will not act on notices that are facially defective or abusive.

---

## 5. Requests for customer information

We will disclose customer information only where compelled by valid legal process — a
subpoena, court order, or equivalent instrument valid in [JURISDICTION] — or where we have a
legal obligation to do so.

Send legal process to [LEGAL EMAIL]. Where we are legally permitted to do so, we will notify
the affected customer before disclosing, so they can seek to object.

Please note what we hold and what we do not. We hold **billing and licence records** —
name, email, purchase history, licence key, and any support correspondence. We do **not**
hold any record of what any customer has downloaded, which chats they monitor, their Telegram
account details, their IP address at the time of any download, or the contents of their
archive. Legal process seeking those records will return nothing, because they do not exist.

---

## 6. Counter-notice

If we have removed or disabled material of yours in response to a notice, and you believe it
was removed in error or through misidentification, you may submit a counter-notice to
[LEGAL EMAIL] containing:

1. Your physical or electronic signature.
2. Identification of the material removed and its prior location.
3. A statement, under penalty of perjury, that you have a good-faith belief the material was
   removed as a result of mistake or misidentification.
4. Your name, address, and telephone number, and a statement consenting to the jurisdiction of
   the courts of [JURISDICTION] (or, for US-based complainants, the federal district court for
   the district of your address).

We will forward your counter-notice to the original complainant. If they do not notify us
within **10 business days** that they have filed an action seeking a court order, we may
restore the material.

---

## 7. Misrepresentation

Knowingly making a material misrepresentation in a notice or counter-notice may expose you to
liability for damages, including costs and legal fees, under 17 U.S.C. §512(f) or equivalent
law. We reserve the right to seek such remedies and to publish details of abusive notices.

---

## 8. Transparency

We intend to publish an annual transparency report covering the number of copyright notices
received, the number resulting in product changes, the number resulting in customer licence
action, and the number rejected as defective or misdirected.

---

## 9. Internal handling procedure

*(Operational — publish or keep internal at the owner's discretion.)*

| Day | Action | Owner |
|---|---|---|
| 0 | Log notice in the register: date, complainant, works, request | Copyright Agent |
| 0–3 | Acknowledge receipt | Copyright Agent |
| 0–3 | Triage: does it identify (a) our material, (b) a product capability, (c) a customer, (d) nothing we control? | Copyright Agent |
| 3–5 | If (a): remove our material | Maintainer |
| 3–10 | If (b): technical assessment; decide default-off, restrict, or remove; open a tracking issue | Maintainer + counsel |
| 3–10 | If (c): check repeat-infringer register; issue warning or terminate | Copyright Agent |
| 3–10 | If (d): respond explaining what we do and do not host | Copyright Agent |
| 10 | Substantive response issued | Copyright Agent |
| Ongoing | Update the repeat-infringer register; retain the notice for [RETENTION PERIOD] | Copyright Agent |

Escalate to counsel immediately for: any §1201 anti-circumvention claim; any notice from a
trade body (RIAA, MPA, IFPI, BREIN) rather than an individual rights holder; any notice
accompanied by a litigation threat; any notice alleging the product as a whole is unlawful.

---

*Last updated: [DATE] · [LEGAL EMAIL]*

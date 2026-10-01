# Acceptable Use Policy — DRAFT

<!--
DRAFT — proposed. Needs counsel review before publication. See ./README.md.
Intended to be published at [WEBSITE]/aup and incorporated by reference into the EULA
(Option A, §2(e)) and the Terms of Service.
-->

**Applies to:** [PRODUCT NAME] · **Version:** Draft 0.1 · **Effective:** [DATE]

## 1. Purpose and scope

This Acceptable Use Policy ("AUP") sets out what you may and may not do with
[PRODUCT NAME] (the "Software"). It is incorporated by reference into the End-User Licence
Agreement. Breach of this AUP is a material breach of that Agreement and may result in
termination of your licence without refund.

The Software is a tool for archiving media and messages from accounts and conversations that
**you already lawfully have access to**. Everything in this policy follows from that sentence.

**[Drafting note — remove before publication]** This AUP is written on the assumption that
the recommendations in `../06-compliance.md` §4.4 are adopted: the torrent plugin and channel
member export are **removed** from the product, and the generic-URL/YouTube plugins are opt-in
and self-hosted only. Sections 4 and 5 below are drafted to cover those features anyway,
because an AUP must address what the software can do, and because some users will run older
versions. If those features are *not* removed, this AUP alone is not an adequate mitigation —
see `../06-compliance.md` §4.2–4.3.

## 2. Your core responsibilities

You must:

1. Use the Software only with accounts you own or are expressly authorised to operate.
2. Use the Software only to access content you already have a lawful right to access.
3. Comply with all applicable laws, including copyright, data-protection, computer-misuse,
   interception, and export-control law in every jurisdiction that applies to you.
4. Comply with the terms of service of every third-party platform the Software interacts with,
   including the **Telegram Terms of Service**, the **Telegram API Terms of Service**, and the
   **Telegram Terms of Service for Content Licensing**.
5. Obtain and use your own third-party API credentials.
6. Where you process other people's personal data, comply with your obligations as a data
   controller under applicable data-protection law.

## 3. Prohibited uses — general

You must not use the Software to:

**(a) Unauthorised access**
- access any account you do not own or are not authorised to operate;
- use credentials obtained without the account holder's informed consent;
- circumvent any access control, authentication mechanism, paywall, geographic restriction, or
  digital rights management system;
- access private groups or channels you have been removed from or banned from.

**(b) Infringe intellectual property**
- acquire, reproduce, store, or distribute any content in violation of copyright, database
  rights, trademark, or any other intellectual property right;
- acquire content for commercial redistribution, resale, or public performance without the
  rights holder's permission;
- build or operate any service that redistributes acquired content to third parties.

**(c) Misuse personal data**
- collect, aggregate, compile, or export the personal data of third parties without a lawful
  basis and any required notice, including names, usernames, phone numbers, and contact details;
- build marketing lists, contact databases, lead lists, or CRM records from data obtained
  through the Software;
- conduct surveillance, monitoring, profiling, or tracking of any individual without lawful
  authority;
- use the Software to facilitate stalking, harassment, doxxing, or intimidation;
- re-identify, combine, or enrich datasets to identify individuals who have not consented.

**(d) Train AI or machine-learning systems**
- use the Software, or any data obtained through it, to train, fine-tune, validate, benchmark,
  or develop any artificial intelligence or machine-learning model, or to create datasets for
  that purpose. This is independently prohibited by Telegram's Terms of Service for Content
  Licensing and applies regardless of the scale involved.

**(e) Abuse platforms or infrastructure**
- circumvent, defeat, or undermine any rate limit, quota, throttle, or anti-abuse mechanism,
  whether imposed by a third-party platform or built into the Software;
- modify, patch, or reconfigure the Software to exceed the maximum request rates it enforces;
- operate multiple accounts or instances to evade a platform restriction, suspension, or ban;
- distribute load across proxies, VPNs, or IP pools in order to evade detection or limits.

**(f) Acquire unlawful content**
- acquire, store, or distribute child sexual abuse material. **Zero tolerance: we will
  terminate your licence immediately without notice or refund and report to law enforcement
  where legally required or permitted;**
- acquire, store, or distribute non-consensual intimate imagery;
- acquire, store, or distribute content that is unlawful in your jurisdiction, including
  terrorist content and unlawful hate speech;
- acquire, store, or distribute malware, or use the Software to deliver it.

**(g) Evade lawful process**
- destroy, conceal, or alter data subject to a legal hold, preservation order, or litigation
  duty;
- use the Software to create records you represent as complete or authentic when they are not.

## 4. Message and history archiving

The Software can record the content of messages sent by other people to a local log file.

You must not use this capability to:
- record conversations where doing so would constitute unlawful interception or breach a
  recording-consent law applicable to you;
- retain message content longer than you have a lawful basis to retain it;
- record conversations you have left, been removed from, or no longer participate in;
- record special-category personal data (health, political opinion, religious belief, sexual
  orientation, trade-union membership, biometric or genetic data) without a lawful basis
  under GDPR Article 9 or its equivalent.

You must:
- configure a retention period appropriate to your purpose and honour it;
- be able to locate and erase all records relating to an individual if they exercise a data
  subject right against you;
- restrict access to archived message content to people with a legitimate need.

## 5. Third-party URL downloading

Where your edition of the Software includes the ability to download content from arbitrary
URLs or video-sharing platforms, you must not use it to:

- download content in breach of the terms of service of the site hosting it;
- circumvent, or attempt to circumvent, any technological measure that controls access to a
  copyrighted work. **Anti-circumvention laws, including 17 U.S.C. §1201 and the equivalent
  provisions of the EU InfoSoc Directive, may apply independently of whether your copying
  would otherwise be lawful, and fair use or fair dealing is generally not a defence to them;**
- download content for commercial use, redistribution, or public performance without the
  rights holder's permission;
- operate a service that downloads content on behalf of third parties.

**This capability is disabled by default and requires you to install third-party download
tools yourself. Enabling it is your decision and your responsibility.**

## 6. Peer-to-peer and BitTorrent

**[If the torrent plugin is removed as recommended, replace this whole section with:]**

> The Software does not include any peer-to-peer or BitTorrent functionality. Earlier
> versions did; it has been removed and will not be reinstated.

**[If retained, the following applies:]**

BitTorrent is a **distribution** protocol. Participating in a swarm means your system uploads
content to other people. You are therefore not only acquiring content — you are distributing
it, and you may be directly liable for doing so.

You must not use any peer-to-peer functionality to:
- download or upload any content you do not have the right to distribute;
- participate in any swarm for infringing content;
- distribute content to third parties in any manner.

You acknowledge that your IP address is visible to every other peer in a swarm and to
anti-piracy monitoring services, and that your internet service provider may act against you.

## 7. Platform account risk — acknowledgement

You acknowledge and accept that:

- automating a personal messaging account may breach the platform's terms of service;
- the platform may rate-limit, restrict, suspend, or **permanently ban** your account, at its
  sole discretion and without notice or appeal;
- a ban may result in permanent, irrecoverable loss of access to your account, your message
  history, and any services associated with it;
- we have no ability to prevent, appeal, or reverse such action, and no liability for it;
- the risk is highest on new accounts, when scanning large volumes of history, when
  enumerating group members, and when running from datacentre IP addresses.

## 8. Enforcement

We may investigate suspected breaches of this AUP and take any of the following steps:

| Step | When |
|---|---|
| Request information or remediation | Suspected or first-time breach |
| Suspend licence entitlements | Unremediated breach |
| Terminate the licence without refund | Material or repeated breach |
| Immediate termination, no notice, report to authorities | Section 3(f) content, or unlawful access |

Because the Software runs on your own systems, **we cannot monitor your use of it and we do
not attempt to.** Enforcement is triggered by reports, legal process, or information you give
us. This is a deliberate design property: we do not have access to your data.

Termination of your licence does not require you to delete data you lawfully acquired, and
does not affect your obligations to any third party.

## 9. Reporting a violation

To report misuse of the Software, or content acquired using it:

- **Abuse:** [LEGAL EMAIL]
- **Copyright:** see the notice-and-takedown process at [WEBSITE]/dmca
- **Data protection:** [PRIVACY EMAIL]
- **Child safety:** [LEGAL EMAIL], marked urgent. We escalate these immediately.

Please include the nature of the violation, the identity of the user or deployment if known,
and any supporting evidence. Note that because the Software is self-hosted, we generally have
no access to a user's installation or data, and our practical remedies are limited to the
licence relationship and to changing the product.

## 10. Changes

We may update this AUP. Material changes will be notified at least 30 days before they take
effect, via [WEBSITE] and the product's release notes. Continued use after the effective date
constitutes acceptance.

---

*Questions: [LEGAL EMAIL]*

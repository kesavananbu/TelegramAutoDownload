# Draft legal & policy documents — PROPOSED, NOT ADOPTED

These files are **drafts prepared for review**. They live under `docs/compliance/drafts/`
until explicitly promoted to the repository root.

**Product direction (2026-10-01):** **Free personal tool** — not a commercial product.
Marketplace, billing, and licence-key enforcement are out of scope. See
`docs/product/01-product-definition.md` and `docs/gtm/07-marketplace-readiness.md`
(distribution & community).

Nothing here is legal advice.

## Status of each draft

| File | Intended final location | Status | Notes |
|---|---|---|---|
| `LICENSE` | `/LICENSE` | **Adopted (MIT Option C)** | Root `/LICENSE` — Copyright (c) 2026 TelegramAutoDownload contributors |
| `SECURITY.md` | `/SECURITY.md` | **Adopted** | GitHub private vulnerability reporting |
| `NOTICE.md` | `/NOTICE` + release artifacts | **Adopted** | Root `/NOTICE`; includes GPL-2+ Debian ffmpeg in container |
| `acceptable-use-policy.md` | Website / README link | Outline + clauses | Optional for GitHub-only; useful if site collects data |
| `dmca-notice-and-takedown.md` | Website / SECURITY.md link | Process draft | Repo contact path, not §512 hosting |
| `privacy-policy-outline.md` | Website | Outline only | Counsel if operating a website with analytics |
| `terms-of-service-outline.md` | Website | Outline only | Less critical for pure MIT OSS on GitHub |

## Placeholders

Replace before publication:

- `[COPYRIGHT HOLDER]` / `[LEGAL ENTITY NAME]` — individual maintainer or org
- `[PRODUCT NAME]` — **avoid "Telegram" in the product title** (`../06-compliance.md` §2)
- `[YEAR]`, emails, jurisdiction — as needed for SECURITY/policies

## Entity formation

**[ASSESSMENT]** For a **free** project, a formal entity is **optional**. It becomes relevant
if you accept donations, sell support, publish signed binaries as a business, or face
content-liability exposure. Not a prerequisite for MIT + GitHub Releases.

## Related reading

- `../06-compliance.md` — surviving legal risks for a free tool
- `../../security/04-findings.md` — security hardening (product quality goal)

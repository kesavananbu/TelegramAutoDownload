# Draft legal & policy documents — PROPOSED, NOT ADOPTED

These files are **drafts prepared for review**. They are deliberately kept in
`docs/compliance/drafts/` and **not** at the repository root, because placing them at the root
would imply the project has adopted them.

Nothing here is legal advice. The author is not a lawyer. Each draft is written to give
counsel a concrete starting point rather than a blank page, and to make the open questions
explicit.

## Status of each draft

| File | Intended final location | Status | Needs counsel? |
|---|---|---|---|
| `LICENSE` | `/LICENSE` | Draft — two options presented, owner must choose | **Yes** — before adoption |
| `SECURITY.md` | `/SECURITY.md` | Draft — adoptable with minor edits | Light review |
| `NOTICE.md` | `/NOTICE` + shipped in installer & image | Draft — factually complete | Light review |
| `acceptable-use-policy.md` | Website + in-product link | Draft — substantive clauses written | **Yes** |
| `dmca-notice-and-takedown.md` | Website | Draft — process written | **Yes** |
| `privacy-policy-outline.md` | Website | **Outline only** | **Yes — must be drafted by counsel** |
| `terms-of-service-outline.md` | Website + EULA | **Outline only** | **Yes — must be drafted by counsel** |

## Placeholders

Every draft uses these placeholders. All must be replaced before publication:

- `[LEGAL ENTITY NAME]` — the contracting entity (see open question below)
- `[PRODUCT NAME]` — **must not contain "Telegram"** in the title; see
  `../06-compliance.md` §2.2 (Telegram API ToS §2.3)
- `[JURISDICTION]` — governing law and venue
- `[REGISTERED ADDRESS]`
- `[SECURITY EMAIL]`, `[LEGAL EMAIL]`, `[PRIVACY EMAIL]`, `[SUPPORT EMAIL]`
- `[WEBSITE]`
- `[YEAR]`

## Open question the owner must answer before any of this is finalised

**Is there a legal entity?** Every draft assumes one. Selling software personally, with no
limited-liability entity, means personal exposure to the content-liability and data-protection
risks identified in `../06-compliance.md` §4. Forming an entity is a prerequisite to
commercialisation, not a later administrative step.

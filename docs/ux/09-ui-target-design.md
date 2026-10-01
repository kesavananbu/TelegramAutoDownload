# 09 — Target UI Design

**Branch:** `enterprise-readiness` · **Date:** 2026-10-01 · **Re-run:** Composer 2.5  
**Companion:** `09-ui-audit.md` (current state — read first)  
**Product goal:** *fully usable and completely customizable UI* for users to view and manage TelegramAutoDownload.  
**Status:** design only — no source modified.

---

## Quick reference (for planners)

### Technology recommendation

**Adopt Preact + TypeScript + Vite**, with `preact/compat`, **TanStack Table + TanStack Query**, **dnd-kit** (widget grid + column reorder), and a **CSS custom-property token layer** (Tailwind optional as utility layer). Build a thin in-house component set on headless primitives (Ark UI or Radix via compat) — not a opinionated kit — to support white-label accent, density, and layout presets (audit: hardcoded hex in `styles.css`).

**Why not stay vanilla:** the customization goal (virtualized configurable tables, saved views, drag-resize dashboard, accessible overlays) is months of bespoke framework work in `app.js`; the React ecosystem ships it accessible and battle-tested at ~12 KB gzip (Preact compat).

**Why not Blazor WASM:** 1.5–2.5 MB cold start hurts phone/LAN dashboard use; browser APIs (SSE, `localStorage`, Web Notifications, clipboard) become interop; customization libraries are thinner than TanStack/dnd-kit.

**Release impact:** add `npm ci && npm run build` before `dotnet publish`; hashed assets under `wwwroot/assets/`; both Setup.exe and Portable ZIP still required per release rules.

**Incremental migration:** mount Preact shell around existing vanilla tab DOM per phase; tab-level feature flags until Phase 5 deletes `app.js` — see §7.3 in prior structure below.

### Top 15 backlog items (RICE-ordered for usability + customization)

| Rank | Item | Effort | Why |
|------|------|--------|-----|
| 1 | Replace 50× `alert`/`confirm` with toast + confirmation sheets + error boundary | 3 d | Unblocks all feedback (`app.js` alert count) |
| 2 | Surface loader failures as inline banners + stale-data badge + retry | 2 d | Fixes silent failure (`app.js:468,736,1002…`) |
| 3 | **`GET/PUT /api/ui/preferences`** + PreferenceStore (server + localStorage) | 3 d | **Foundation for all customization** |
| 4 | Phase 0: Vite + Preact + design tokens extracted from `styles.css` | 5 d | Enables theme/density without rewrite day |
| 5 | Phase 1: App shell — sidebar, system-state strip, router, command palette (⌘K) | 6 d | Overview-first IA; deep links |
| 6 | DataTable component (virtualized, columns, density, multi-select) | 6 d | Reused by Chats + Downloads |
| 7 | **`PATCH /api/chats/bulk` + `/api/chat-templates` CRUD** + bulk-apply UI | 5 d | **Highest customization value** for many chats |
| 8 | Extend `ChatPatch` + drawer UI for reactions + 4 provider folder templates + reactions API | 2 d | Category B free wins (`Program.cs:607-614`) |
| 9 | Wire orphaned APIs: `/sync`, `GET .../bootstrap`, `GET /api/queue/downloads` global pause | 1.5 d | Zero backend |
| 10 | `GET /api/events` SSE hub; replace 9-endpoint poll | 4 d | Fixes focus destruction + load (`app.js:1033-1047`) |
| 11 | **`GET /api/queue/reasons`** + machine-readable skip/fail codes in DB | 3 d | Fixes “state only in logs” |
| 12 | Phase 2: Downloads queue manager (paged `/api/queue/items`, facets, bulk) | 7 d | Usability at scale |
| 13 | Phase 4: Overview widget grid (add/remove/resize layouts persisted in prefs) | 8 d | Customizable home |
| 14 | Theming: dark/system, accent with AA guard, font scale, reduced motion | 4 d | §5.4 customization |
| 15 | Saved views + column presets persisted (`savedViews`, `tables.*` in prefs) | 4 d | §5.2–5.3 customization |

**Parallel track (Tier 1 vanilla, pre-migration):** items 1–2, 8–9, rollback optimistic PATCH, debounce search, empty states, a11y focus/contrast, responsive tables — ~15 d, ships on current stack.

---

## 1. Design principles

1. **Answer “what is the system doing?” before “what can I change?”** — Overview widget dashboard replaces 14-column landing (`app.js:75`).
2. **No state knowable only from logs** — faceted reasons, stall badges, pause visible globally (audit §4.12).
3. **Push, don’t poll** — `/api/events` SSE; precedent `/api/logs/stream` (`Program.cs:319`).
4. **Mutations confirm visibly** — optimistic UI with rollback (audit §4.3).
5. **Bulk by default** — templates + multi-select for N chats.
6. **Customizable but safe defaults** — server `defaults` for limits (`Program.cs:211-217`); guardrails on accent contrast and non-dismissible system strip.
7. **Accessible + responsive are acceptance criteria** — WCAG 2.1 AA, 375 px layouts.

---

## 2. Target information architecture

```
Unauthenticated:  A1 sign-in · A2 forced password · A3 first-run wizard (5 steps)
Authenticated shell:
  Global: sidebar · command palette · notification centre · system-state strip · theme · user menu
  1 Overview      — customizable widget dashboard (default landing)
  2 Chats         — 2a list (bulk) · 2b detail drawer · 2c discover · 2d message browser
  3 Downloads     — 3a queue manager · 3b live activity · 3c bootstrap jobs
  4 Insights      — statistics · reports/export
  5 Logs          — virtualized explorer
  6 Settings      — general · storage · limits · notifications · appearance · templates · integrations
  7 Access        — users · sessions · audit (when backend supports)
```

- **Routing:** hash or History API — `#/downloads?status=failed&chat=123`.
- **System-state strip (non-removable):** connection (text+icon), Telegram user, pause/running, active count, flood-wait, disk free, data age — sourced from `/api/events` with `/api/status` snapshot fallback.

Mobile: sidebar → bottom tab bar (Overview, Chats, Downloads, Logs) + “More” sheet below 900 px.

---

## 3. Screen specifications (summary)

Detailed specs unchanged in intent from prior design; each screen lists endpoints with **NEW** items in §6 register.

| Screen | Purpose | Key endpoints |
|--------|---------|---------------|
| A1–A4 | Auth + Telegram wizard | Existing `Program.cs` login/web-auth |
| A3 First-run | README-less path to first download | + verify credentials/folder, `GET /api/setup/state` |
| **1 Overview** | Customizable widgets; “needs attention” inbox | `/api/status`, queue stats, downloads, bootstrap, logs ERR + **§6.1 events, §6.3 stats, §6.8 storage, §6.9 reasons** |
| **2a Chats list** | Virtualized table, bulk bar, saved views | `/api/chats`, bulk patch, sync, refresh |
| **2b Drawer** | Rule editor: filter builder, folder tokens, reactions | Extended `ChatPatch`, validate regex/template, export history |
| **3a Queue** | Single virtualized table, facets, paging | Extended `/api/queue/items`, bulk, export |
| **3b Live** | Speed, ETA, stall | **§6.2** on `/api/downloads` |
| **6e Appearance** | Theme, accent, density, landing view | **§6.30** `/api/ui/preferences` |
| **6f Templates** | Named rule templates, apply to N chats | **§6.31** + bulk |

Full field-level specs: see companion sections in git history or expand during implementation stories.

---

## 4. Component inventory (build once)

**Primitives:** Button, Input, Switch, Select, Field (label/hint/error/`aria-describedby`).

**Data:** DataTable (virtualized, sort, multi-select, column manager, density, server page), FilterBar, FacetList, MetricCard, StatusPill (icon+text), WidgetGrid (dnd-kit).

**Feedback:** ToastRegion, ConfirmationSheet, InlineBanner, BulkProgress, StaleDataBadge.

**Layout:** AppShell, Sidebar, SystemStateStrip, Drawer (focus trap), Tabs (WAI-ARIA), CommandPalette.

**Domain:** ChatRuleEditor, RegexListEditor, TokenPaletteInput, FolderTemplateEditor, DownloadProgressRow, BootstrapJobCard, LogViewer (virtualized lines).

**Services:** typed API client, SSE client with resume, PreferenceStore, ThemeProvider, shortcut registry.

**Acceptance per component:** `:focus-visible`, keyboard complete, AA both themes, loading/empty/error, 375 px.

---

## 5. Customization layer (first-class subsystem)

**Persistence:**

| Tier | Store | Contents |
|------|-------|----------|
| Server SoT | `GET/PUT /api/ui/preferences` → `DATA_DIR/ui-preferences/{username}.json` | layouts, savedViews, tables, appearance, shortcuts, locale |
| Device cache | `localStorage` `tad.ui.v1.*` | instant paint, pre-auth theme, sidebar collapsed |

**Conflict:** last-write-wins per key; server wins with “keep this device” toast.

| ID | Feature | User controls | Backend |
|----|---------|---------------|---------|
| 5.1 | Dashboard widgets | Add/remove/resize/reorder; named layouts; per-breakpoint | §6.30 + widget data APIs |
| 5.2 | Saved views | Filter+sort+columns on Chats/Downloads/Logs; URL share | §6.30 + §6.19 |
| 5.3 | Table columns | Show/hide/reorder/resize, density, page size | §6.30 (+ server sort for Downloads) |
| 5.4 | Theming | Light/dark/system, accent (AA-checked), font scale, reduced motion, optional logo/title | §6.30 + CSS tokens |
| 5.5 | Rule templates | Create from chat; bulk apply; “update 23 chats using template” | **§6.31 + §6.10** (highest ROI) |
| 5.6 | Notifications | In-app + browser + Telegram bot routing | §6.28–29 (wire `ConfigParams`) |
| 5.7 | Shortcuts | Remappable; cheat sheet | §6.30 |
| 5.8 | i18n | Catalogues + `Intl`; API `{ code, message, params }` | §6.36 |

**Guardrails:** system-state strip and active flood banner cannot be removed; “Needs attention” cannot be deleted while non-empty; reject failing accent contrast pairs.

---

## 6. NEW BACKEND WORK register (abbreviated)

| § | Endpoint / change | Purpose |
|---|-------------------|---------|
| 6.1 | `GET /api/events` SSE | Replace poll |
| 6.2 | Speed/ETA/stall on downloads | Live activity parity with WPF |
| 6.3 | `/api/stats/timeseries` | Insights + widgets |
| 6.8 | `/api/system/storage` | Disk widget (share `DiskSpaceService`) |
| 6.9 | `/api/queue/reasons` + reason codes in DB | Faceted skip/fail |
| 6.10 | `PATCH /api/chats/bulk` | Bulk customize |
| 6.11–6.16 | mute, extended ChatPatch, reactions, export, validators | Drawer parity |
| 6.17–6.18 | message browse + download | ChatBrowse parity |
| 6.19–6.21 | paged queue items, bulk, export | Queue manager |
| 6.22 | cancel download | Orchestrator work |
| 6.28–6.29 | notifications GET/POST + test | Headless honors `ConfigParams` |
| 6.30 | `/api/ui/preferences` | **Customization SoT** |
| 6.31 | `/api/chat-templates` | **Template customization** |
| 6.33–6.35 | users, sessions, audit | Enterprise access |
| 6.36 | structured errors | i18n + taxonomy |

Full contracts: retain detail from 2026-09-30 design revision; implement alongside UI phases.

---

## 7. Frontend technology & migration

### 7.1 Recommendation (full)

**Preact + TypeScript + Vite + TanStack + dnd-kit + tokenized CSS** — see Quick reference above.

### 7.2 Incremental phases

| Phase | Deliverable | Exit criterion |
|-------|-------------|----------------|
| 0 | Toolchain + tokens + API/SSE clients | Pixel-identical to today |
| 1 | Shell, router, toasts, state strip | Old tabs inside shell |
| 2 | Downloads rewrite | Toggle per tab |
| 3 | Chats + drawer + bulk/templates | Category B in UI |
| 4 | Overview widgets + prefs API | Custom landing |
| 5 | Logs, Insights, Settings, Access | Delete `app.js` |
| 6 | i18n + WCAG audit | AA sign-off |

**Rollback:** per-tab flag serves vanilla or Preact until Phase 5 complete.

---

## 8. Full prioritized backlog (Tiers 1–2)

### Tier 1 — Quick wins + worst defects (~24 d)

1. Toasts + confirmation sheets (3 d)  
2. Inline error banners (2 d)  
3. Extended ChatPatch + reactions API (2 d)  
4. Orphan endpoints wired (1.5 d)  
5. Mute toggle (0.5 d)  
6. PATCH rollback + per-field save (1.5 d)  
7. Debounce search; pause poll when hidden (1 d)  
8. Skeletons + empty states (2 d)  
9. Reset limits to `defaults`; fix parallel confirm flow (1 d)  
10. A11y pass on current DOM (4 d)  
11. Responsive tables + mobile nav (3 d)  
12. Client + server validation helpers (2.5 d)  

### Tier 2 — Structural (~130 d)

13–40: Phase 0–6 items, SSE, reasons, paged queue, bulk/templates, widgets, stats, notifications shared lib, multi-user, error envelope, i18n — as ranked in §Top 15 and prior roadmap.

**Critical path:** `13 → 17 → 18 → 19 → 22 → 25` (scaffold → shell → table → downloads → chats → overview).

**Grand total:** ~154 engineer-days (one engineer ~31 weeks; three engineers ~11 weeks with parallel backend).

---

## 9. WPF desktop note (same product, different surface)

Desktop already supports richer per-chat customization (emoji combos, filter dialog, folder dialog, stats/disk strips) but **no user theming beyond MahApps Light.Blue** (`App.xaml:11-15`). Enterprise “customizable UI” for installed users implies:

- Shared **rule templates** and **export/import** synced with headless config format.
- Optional **accent/branding** in WPF resource dictionaries mirroring web tokens.

Web headless remains the primary target for hosted/self-hosted commercial UX; desktop should consume the same `ConfigParams` template model once §6.31 exists.

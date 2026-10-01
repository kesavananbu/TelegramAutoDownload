# 09 — UI Audit (current state)

**Branch:** `enterprise-readiness` · **Date:** 2026-10-01 · **Re-run:** Composer 2.5  
**Scope:** Headless web dashboard (`TelegramAutoDownload.Headless/wwwroot/`) as primary product surface; WPF desktop (`TelegramAutoDownload/`) as capability reference for gaps.  
**Assets:** `index.html` (335 lines), `app.js` (1158 lines), `styles.css` (164 lines)  
**Backend cross-check:** `TelegramAutoDownload.Headless/Program.cs` (641 lines, 42 mapped endpoints)  
**Method:** Full read of web assets and endpoint inventory in `Program.cs`; every `fetch`/`api()` call site in `app.js` cross-referenced. **No source modified.** **No `dotnet build` run.**

**Product priority for this audit:** *fully usable, completely customizable UI* for viewing and managing downloads — today the web UI is neither fully usable (state, errors, mobile, a11y) nor customizable (no themes, layouts, saved views, bulk templates, or column control).

---

## Executive summary

### Ten worst UI problems (ranked)

| # | Problem | Severity | Evidence |
|---|---------|----------|----------|
| 1 | **Zero customization layer** — no theme/density, no saved views, no column layout, no dashboard widgets, no rule templates, no per-user UI prefs; every user gets the same 14-column grid | Blocks product goal | `styles.css` — 60+ hardcoded hex, no tokens (`styles.css:1-164`); no `localStorage`/API preference keys in `app.js`; landing is fixed Chats table (`app.js:75`) |
| 2 | **Operators cannot understand system state without reading logs** — skip/failure causes not faceted, stall vs progress indistinguishable, `pending` vs `queued` undefined, pause hidden off Queue tab | S1 commercial | `refreshStatus()` drops `at` (`app.js:992-999`); `/api/status` fields `downloadsPaused`/`floodWait` unused (`app.js:985-987` vs `Program.cs:529-531`); skip UI admits missing reasons (`app.js:858`) |
| 3 | **Landing IA is a configuration matrix, not a management home** — no overview, stats, or “needs attention” inbox | S1 | Default tab Chats with 14 editable columns (`index.html:121-140`, `app.js:75`); desktop has session/all-time + disk strips (`MainWindow.xaml:160-184`) |
| 4 | **API failures are silent** — tables freeze with no banner, retry, or “last updated” | S1 | `loadChats`/`refreshQueue`/`refreshStatus`/`loadSettings`/`openLogsTab` → `console.warn` only (`app.js:468, 736, 1002, 1014, 1146`); empty `catch` on chat search stats (`app.js:485`) |
| 5 | **All feedback is `window.alert` / `window.confirm` (50 call sites)** — blocks poller, no copy, no a11y, suppressible on mobile | S1 | Verified count in `app.js` |
| 6 | **Optimistic chat edits lie on PATCH failure; patch object accumulates across fields** | S1 data integrity | `chatRow()` (`app.js:525-538`) — checkbox stays changed after failed save; closure `patch` never cleared |
| 7 | **2.5 s polling rebuilds DOM and overwrites in-progress edits** — up to ~7 parallel queue requests per tick on Queue tab | S1 | `poll()` (`app.js:1033-1047`); `refreshQueue()` full tbody rebuild (`app.js:727-729` overwrites rate inputs) |
| 8 | **Large desktop↔web capability gap on the same engine** — reactions, per-provider folders, mute, filter dialog, browse/export, stats, speed/ETA not in web | S1 parity | `ChatPatch` only 9 fields (`Program.cs:607-614`); `ChatDtoView` omits 8+ persisted fields; WPF exposes them (`MainWindow.xaml:293-435`) |
| 9 | **Not usable on phone / keyboard / screen reader** — no `@media`, colour-only status, almost no ARIA/focus | S1 a11y/mobile | Zero `@media` and zero `:focus` in `styles.css`; 5 `aria-*` in `index.html`; headers `V P M F` / `Y S D T` (`index.html:129-130`) |
| 10 | **No bulk workflow for many chats** — no multi-select, templates, or apply-to-selection; 400 chats = 400 row edits | S1 scale | Single-row PATCH only (`app.js:535`); desktop supports extended selection (`MainWindow.xaml:199`) but web has no bulk bar |

### Free wins (mostly UI; backend already exists)

| Win | Effort | Evidence |
|-----|--------|----------|
| Wire `POST /api/chats/{id}/sync` instead of always `/bootstrap` — monitored-only error text | ~0.25 d | Orphan: no `/sync` in `app.js`; endpoint `Program.cs:285`; logic `Program.cs:543-544` |
| Wire `GET /api/chats/{id}/bootstrap` for per-chat progress | ~0.5 d | Orphan: only POST/DELETE in `app.js:253,785`; GET `Program.cs:447` |
| Wire `GET /api/queue/downloads` + global pause strip; render `/api/status.downloadsPaused` on Status | ~0.5 d | GET orphaned — only POST `app.js:199`; pause UI Queue-only `app.js:179-195` |
| Extend `ChatPatch` + UI for 3 reaction icons + 4 per-provider folder templates | ~2 d | Fields on `ChatDto` (`ChatDto.cs:21,66+`); engine sends reactions (`TelegramApp.cs:373-375,414-420`) |
| `GET /api/chats/{id}/reactions` + emoji pickers | ~0.5 d | `GetChatAvailableReactionsAsync` exists (`TelegramApp.cs:1489`) |
| Mute toggle + show `muted` already in `ChatDtoView` | ~0.5 d | `Program.cs:629`; not in `chatRow()` (`app.js:499-523`) |
| “Reset limits to defaults” from server `defaults` block | ~0.25 d | Emitted `Program.cs:211-217`; never read in `app.js` |
| Use server `thresholds` only — delete duplicated `LIMIT_THRESHOLDS` after first load | ~0.25 d | Client hardcodes `app.js:207-211`; server sends `Program.cs:218-223` |
| Render download `at` + stall hint on Status tab | ~0.5 d | API `Program.cs:516`; UI omits `app.js:992-999` |
| Replace parallel-bootstrap save-then-confirm-then-undo with confirm-first sheet | ~0.5 d | Harmful pattern `app.js:926-939` |

**Category B subtotal:** ~8 engineer-days for 14+ user-visible capabilities, mostly exposure of existing engine/config.

---

## 1. Information architecture as it exists

Single HTML document; screens toggled via `hidden`; one modal.

| Screen | Element | Gate |
|--------|---------|------|
| Dashboard login | `#web-login-screen` (`index.html:12`) | `/api/web-auth/status` (`app.js:85-96`) |
| Forced password change | `#password-change-modal` (`index.html:310`) | `defaultPassword` (`app.js:99-101`) |
| Telegram login | `#telegram-auth-screen` (`index.html:36`) | Web OK, Telegram absent (`app.js:57-60`) |
| Main app | `#app-screen` (`index.html:82`) | `loggedIn` (`app.js:60`) |

Telegram login: linear wizard `#step-creds` → phone → code → password (`index.html:52-74`), driven by `showStep()` from `/api/login/status`.

Main app: **five tabs** (`index.html:85-91`), `switchTab()` (`app.js:118-126`):

```
Chats     → loadChats()
Queue     → refreshQueue()
Status    → refreshStatus()
Settings  → loadSettings()
Logs      → openLogsTab()
```

**Queue tab delta since prior audit:** parallel bootstrap controls added (`index.html:169-176`, `app.js:267-279, 922-923`) including `maxParallelBootstraps` and 409 override flow (`app.js:251-264`).

**Structural gaps vs “usable + customizable”:**

- No Overview / home / widget dashboard.
- No Statistics (desktop has session + all-time — `MainWindow.xaml:160-173`).
- No URL routing — reload always lands on Chats (`app.js:75`).
- No Settings → Appearance, Templates, or Notifications (desktop has bot notify — `SettingsWindow.xaml`).
- No bulk or saved-filter UX anywhere.

---

## 2. User actions inventory

**37 distinct actions** (unchanged count): auth (7), chats (10), queue (11), settings (3), logs (6). All immediate-effect; no undo; no multi-select; no export.

---

## 3. API cross-reference — wired vs orphaned

42 endpoints in `Program.cs`. **39 reachable from UI; 3 fully orphaned** (unchanged):

| Endpoint | Program.cs | app.js | Verdict |
|----------|------------|--------|---------|
| `POST /api/chats/{id}/sync` | 285 | — | **ORPHANED** |
| `GET /api/chats/{id}/bootstrap` | 447 | — | **ORPHANED** |
| `GET /api/queue/downloads` | 488 | — | **ORPHANED** (POST used at `app.js:199`) |

**Partial orphans:**

- `GET /api/queue/items` — only `failed`/`skipped` with limits 50/100 (`app.js:707-708`); server cap 200 (`Program.cs:397-400`); no truncation warning.
- `GET /api/settings` / `/api/status` / `/api/downloads` — fields returned but not rendered (`downloadsPaused`, `floodWait`, `at`, `defaults`, `muted`).

**ChatPatch vs engine:** UI drives all 9 patch fields; **8+ `ChatDto` fields** (reactions, per-provider folders, etc.) persisted by headless but not on API — see §5 Category B.

---

## 4. Usability defects (detail)

Severity: **S1** blocks intent · **S2** degrades daily use · **S3** polish.

### 4.1 Invisible failures — S1

See executive #4. Session expiry throws in `api()` (`app.js:139-143`) but often lands in swallowed loaders.

### 4.2 Alert/confirm-only feedback — S1

50 sites. Blocks 2.5 s poller; no live regions.

### 4.3 Optimistic edits — S1

See executive #6.

### 4.4 Polling — S1

See executive #7. SSE exists for logs (`Program.cs:319`, `app.js:1088`) but not for queue/status.

### 4.5 Chat search refetches aggregates — S2

`$('#chat-search').oninput = () => renderChats()` (`app.js:479`) → `/api/queue/stats/by-chat` every keystroke (`app.js:484`).

### 4.6 Missing loading/empty/error states — S2

Chats table: no zero-row branch in `renderChats()` (`app.js:481-493`). Live downloads: idle ≡ broken (`app.js:988-1001`).

### 4.7 No form validation — S2

App ID `Number('')` → 0 (`app.js:396+`); regex/folder free text with no compile/preview; server splits filter on `;` (`Program.cs:275-276`).

### 4.8 Destructive confirmation inconsistent — S2

Per-row retry unconfirmed; Monitor toggle instant; parallel bootstrap save-then-ask-then-undo (`app.js:926-939`).

### 4.9 Keyboard / SR — S1 for assistive users

No tablist/tabpanel roles (`index.html:85-100`); unlabelled checkboxes (`app.js:506-515`); `#conn` colour-only (`index.html:93`, `styles.css:80-82`); password modal no focus trap (`app.js:17-25`); `aria-live` on 2000-line log re-render (`index.html:303`, `app.js:1060+`).

### 4.10 Contrast / colour-only status — S2

`.auth-footer` ~2.4:1 (`styles.css:42-43`); disabled buttons ~2.6:1 (`styles.css:137`); pills/sync colours without text backup (`styles.css:157-162, 97-98`).

### 4.11 No responsive layout — S1 mobile

Zero `@media` rules in `styles.css`. 14- and 11-column tables without horizontal scroll containers.

### 4.12 State only in logs — S1

See executive #2. Desktop shows speed/ETA (`MainWindow.xaml:557-561`); web does not.

### 4.13 Additional — S2/S3

No deep links; no sort/page on chats; failed list silently capped; log full-buffer re-render; `escape()` omits `'` (`app.js:603`); nine `innerHTML` sites with chat names; no favicon/`noscript`; first-run is one static card (`index.html:101-109`).

---

## 5. Web vs desktop capability gap

### Category A — in web today

Per-chat monitor, media types, providers, min size, regex text field, folder text field, history toggle, global folder/layout/threads, queue management, logs, Telegram 2FA — see prior mapping (`index.html`, `app.js` vs `MainWindow.xaml`).

### Category B — engine/config ready; web missing ⚑

| Capability | Where implemented | Missing |
|------------|---------------------|---------|
| Reaction icons (3) | `ChatDto.cs`, `TelegramApp.cs:373-420`, `HeadlessHost` persist | `ChatPatch` + UI |
| Per-provider folder templates (4) | `ChatDto.cs:66-75` | API + UI |
| Available reactions picker | `TelegramApp.cs:1489` | GET endpoint + UI |
| Mute | `TelegramApp.cs:1594`; `muted` in API | Toggle |
| History export JSONL | `TelegramApp.cs:1089` | POST + button |
| Members browser | `TelegramApp.cs:966` | GET + drawer |
| Message browse / manual download | `TelegramApp.cs:824,855` | Endpoints |
| Monitored-only sync | `POST .../sync` mapped | Call site |
| Per-chat bootstrap GET | mapped | Inline progress |
| Authoritative pause GET | mapped | Global banner |

### Category C — new backend / shared library

Speed/ETA (`DownloadProgressService` in WPF project), statistics (`StatisticsService.cs`), disk space (`DiskSpaceService.cs`), cancel download, headless notification wiring (`Notification.cs` WPF-only), settings import/export, multi-user/RBAC.

---

## 6. Corrections to the record

1. **Quiet hours** — documented in README only; no implementation in repo (search `quiethours` → README).
2. **Per-chat yt-dlp quality** — normalized away in `ConfigParams.NormalizeYtDlpQualityForAllChats()`; not a port gap.

---

## 7. Customization readiness (explicit gap analysis)

| Desired capability | Current state | Blocker |
|--------------------|---------------|---------|
| Theme (light/dark/accent) | Single light palette | Hardcoded CSS |
| Density / font scale | Fixed 14px body | No tokens |
| Dashboard layout | Fixed five tabs | No widget model |
| Saved views / filters | Client filter only, not persisted | No store |
| Table columns | Fixed 14 / 11 cols | No column manager |
| Rule templates | None | No `/api/chat-templates` |
| Cross-device prefs | None | No `/api/ui/preferences` |
| Keyboard shortcuts | None | — |
| i18n | Inline English in HTML/JS/API errors | No catalogues |

**Verdict:** The headless UI is a **fixed-layout operator console**, not a customizable product surface. Closing the product goal requires the customization subsystem in `09-ui-target-design.md` §5 plus structural IA change (Overview-first, drawer-based chat rules, virtualized tables).

---

## 8. Severity rollup

| Severity | Count | Themes |
|----------|-------|--------|
| S1 | 8 | Customization absent, state opacity, silent failure, alerts-only, edit integrity, polling, parity, mobile/a11y, no bulk |
| S2 | 5 | Search fetch, empty states, validation, confirmations, contrast |
| S3 | — | Routing, truncation, log perf, escaping |

Target design, technology recommendation, and prioritized backlog: **`09-ui-target-design.md`**.

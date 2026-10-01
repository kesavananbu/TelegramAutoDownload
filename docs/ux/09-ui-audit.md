# 09 — Web UI Audit (current state)

**Branch:** `enterprise-readiness` · **Date:** 2026-09-30
**Subject:** `TelegramAutoDownload.Headless/wwwroot/` (`index.html` 334 lines, `app.js` 1158 lines, `styles.css` 165 lines)
**Backend under audit:** `TelegramAutoDownload.Headless/Program.cs` (576 lines, 42 mapped endpoints)
**Reference surface:** `TelegramAutoDownload/` WPF desktop app
**Method:** full read of all three web assets and `Program.cs`; every endpoint cross-referenced against every `fetch`/`api()` call site in `app.js`. No source file was modified.

---

## 1. Information architecture as it exists

The dashboard is a single HTML document with three mutually exclusive top-level screens toggled by a `hidden` class, plus one modal.

| Screen | Element | Gate |
|---|---|---|
| Dashboard login | `#web-login-screen` (`index.html:12`) | shown when `/api/web-auth/status` reports `authenticated: false` (`app.js:94-96`) |
| Forced password change | `#password-change-modal` (`index.html:310`) | shown when `defaultPassword: true` (`app.js:99-101`) — blocks both other screens (`app.js:21-22`) |
| Telegram login | `#telegram-auth-screen` (`index.html:36`) | shown when web-authenticated but Telegram session absent (`app.js:59`) |
| Main app | `#app-screen` (`index.html:82`) | shown only when `loggedIn` (`app.js:60`) |

The Telegram login screen is a four-step linear wizard inside one card: `#step-creds` → `#step-phone` → `#step-code` → `#step-password` (`index.html:52-74`), driven by `showStep()` (`app.js:308`) off the `stage` string from `/api/login/status`.

The main app is five tabs (`index.html:85-91`), switched by `switchTab()` (`app.js:118`), each tab re-fetching its own data on activation:

```
Chats     #tab-chats     → loadChats()    (app.js:121)
Queue     #tab-queue     → refreshQueue() (app.js:122)
Status    #tab-status    → refreshStatus()(app.js:123)
Settings  #tab-settings  → loadSettings() (app.js:124)
Logs      #tab-logs      → openLogsTab()  (app.js:125)
```

Panel inventory per tab:

- **Chats** — static explainer card (`index.html:101-109`), dismissible test-download report panel (`index.html:110-115`), toolbar with refresh + client-side text filter + count (`index.html:116-120`), and one 14-column editable table (`index.html:121-140`).
- **Queue** — 8 metric cards (`#queue-cards`, rendered `app.js:746`), Downloads pause card (`index.html:147-152`), Rate limits card with 3 numeric inputs + parallel-bootstrap toggle (`index.html:154-178`), and four tables: Bootstrap jobs (`index.html:181`), Per-chat queue 11 columns (`index.html:189`), Skipped items (`index.html:201`), Failed items (`index.html:213`).
- **Status** — 3 metrics (monitored / active / folder, `index.html:224-226`) and a live-downloads table (`index.html:229-234`).
- **Settings** — three cards: app settings (folder, folder layout, threads), dashboard-account card (password change, sign out), and a danger card for Telegram sign-out (`index.html:238-277`).
- **Logs** — one card: file select, min-level select, filter box, refresh/search buttons, live-follow checkbox, clear button, meta line, and a `<pre>` viewer (`index.html:281-305`).

Global chrome: fixed header with title, tab nav, user label, and a connection dot `#conn` (`index.html:83-94`); a flood-wait banner `#flood-banner` (`index.html:96`).

**Structural observations.** There is no dashboard/overview/home screen — the landing tab is the 14-column Chats table (`app.js:75`). There is no statistics or reporting screen anywhere. There is no URL routing: tab state lives only in DOM classes, so no view is linkable, bookmarkable, or restorable after reload (reload always lands on Chats). There is no user or security management screen beyond a single password change.

---

## 2. Complete inventory of user actions currently possible

**Authentication (7):** sign in to dashboard (`app.js:329`); change dashboard password from modal (`app.js:360`) or Settings (`app.js:373`); sign out of dashboard (`app.js:388`); save Telegram API credentials (`app.js:396`); submit phone (`app.js:407`), code (`app.js:418`), cloud password (`app.js:428`); sign out of Telegram with confirm (`app.js:437`).

**Chats (10):** refresh chat list from Telegram (`app.js:471`); free-text filter by name/username (`app.js:479`); per row — toggle monitor, edit min-MB, toggle Videos/Photos/Music/Files, toggle YouTube/SocialMedia/Other/Torrent providers, edit regex filter, edit folder template, toggle history (`app.js:500-519`, persisted via `PATCH /api/chats/{id}` at `app.js:535`); start bootstrap with confirm (`app.js:541`); run "Test 10" probe and read report (`app.js:552`); dismiss report (`app.js:599`).

**Queue (11):** pause/resume downloads (`app.js:968`); edit and save the three rate limits plus parallel-bootstrap flag (`app.js:907`); cancel a running bootstrap job (`app.js:783`); per-chat bootstrap / clear queue / retry failed / retry skipped (`app.js:821-844`); retry one skipped item (`app.js:867`) or one failed item (`app.js:896`); retry-all-skipped (`app.js:947`), retry-all-failed (`app.js:954`), delete-all-failed (`app.js:961`); manual refresh of the failed/skipped tables (`app.js:945-946`).

**Settings (3):** save download folder (`app.js:1017`), save folder layout (`app.js:1021`), save thread count (`app.js:1027`).

**Logs (6):** pick file (`app.js:1151`), pick min level (`app.js:1152`), refresh tail (`app.js:1149`), server-side full-file search (`app.js:1150`), toggle live follow (`app.js:1154`), clear the client view (`app.js:1158`).

**Total: 37 distinct actions.** Everything is immediate-effect; nothing is undoable; there are no bulk operations across chats, no multi-select anywhere, and no export of any kind.

---

## 3. API surface cross-reference — wired vs orphaned

42 endpoints are mapped in `Program.cs`. 39 are reachable from the UI; **3 are completely orphaned** and several more are only partially driven.

| Method + path | `Program.cs` | Driven from `app.js` | Verdict |
|---|---|---|---|
| GET `/api/web-auth/status` | 52 | 86, 1008 | wired |
| POST `/api/web-auth/login` | 68 | 333 | wired |
| POST `/api/web-auth/logout` | 83 | 389 | wired |
| POST `/api/web-auth/change-password` | 89 | 32 | wired |
| GET `/api/login/status` | 120 | 314, 1036 | wired |
| POST `/api/login/phone` | 129 | 410 | wired |
| POST `/api/login/code` | 136 | 421 | wired |
| POST `/api/login/password` | 143 | 431 | wired |
| POST `/api/login/logout` | 150 | 442 | wired |
| GET `/api/settings` | 157 | 314, 1008 | wired (fields unused — §3.2) |
| POST `/api/settings/credentials` | 171 | 399 | wired |
| POST `/api/settings/download-folder` | 179 | 1018 | wired |
| POST `/api/settings/folder-layout` | 185 | 1023 | wired |
| POST `/api/settings/threads` | 193 | 1028 | wired |
| GET `/api/settings/limits` | 199 | 706 | wired (`defaults` block unused) |
| POST `/api/settings/limits` | 228 | 916, 927 | wired |
| GET `/api/chats` | 245 | 467, 700 | wired (fields unused — §3.2) |
| POST `/api/chats/refresh` | 251 | 474 | wired |
| PATCH `/api/chats/{id}` | 261 | 535 | wired (fields unused — §3.2) |
| **POST `/api/chats/{id}/sync`** | **285** | **— none —** | **ORPHANED** |
| GET `/api/flood-wait` | 288 | 709, 1036 | wired |
| GET `/api/logs/files` | 291 | 1099 | wired |
| GET `/api/logs/tail` | 293 | 1119 | wired |
| GET `/api/logs/search` | 306 | 1136 | wired |
| GET `/api/logs/stream` | 319 | 1087 | wired (`fromOffset` param unused) |
| GET `/api/queue/stats` | 360 | 616, 638, 660, 703 | wired |
| GET `/api/queue/stats/by-chat` | 375 | 484, 619, 641, 663, 683, 704 | wired |
| GET `/api/queue/items` | 397 | 707, 708 | **partially orphaned** — only `failed`/`skipped` requested |
| POST `/api/chats/{id}/bootstrap` | 415 | 253 | wired |
| POST `/api/chats/{id}/test-download` | 418 | 557 | wired (`limit` hardcoded to 10) |
| DELETE `/api/chats/{id}/bootstrap` | 444 | 785 | wired |
| **GET `/api/chats/{id}/bootstrap`** | **447** | **— none —** | **ORPHANED** |
| GET `/api/bootstrap/jobs` | 453 | 705 | wired |
| POST `/api/queue/{chatId}/{messageId}/retry` | 456 | 870, 899 | wired |
| POST `/api/queue/retry-failed` | 463 | 632 | wired |
| POST `/api/queue/retry-skipped` | 469 | 654 | wired |
| POST `/api/queue/delete-failed` | 475 | 676 | wired |
| POST `/api/queue/clear` | 481 | 691 | wired |
| **GET `/api/queue/downloads`** | **488** | **— none —** | **ORPHANED** |
| POST `/api/queue/downloads` | 494 | 199 | wired |
| GET `/api/downloads` | 504 | 984 | wired (fields unused — §3.2) |
| GET `/api/status` | 519 | 984 | wired (fields unused — §3.2) |

### 3.1 Fully orphaned endpoints — free features, UI-only work

1. **`POST /api/chats/{id}/sync` (`Program.cs:285`)** — identical to bootstrap but enforces `requireMonitored: true` (`Program.cs:543`), producing a targeted, actionable error when a user tries to scan history for a chat they never enabled. The UI calls the permissive `/bootstrap` route everywhere (`app.js:253`), so users never get that guidance. Cost to surface: one call site.
2. **`GET /api/chats/{id}/bootstrap` (`Program.cs:447`)** — single-job progress snapshot. The UI only polls the whole-fleet `/api/bootstrap/jobs` every 2.5 s. This endpoint is exactly what a per-chat inline progress bar needs.
3. **`GET /api/queue/downloads` (`Program.cs:488`)** — authoritative paused state. The UI instead derives it from a side-channel field on `/api/settings/limits` (`app.js:735`), which means the Chats, Status, and Logs tabs never learn that downloads are paused. This is the single most confusing state in the product: downloads are paused, and four of five tabs look completely normal.

### 3.2 Orphaned response fields — data already on the wire, never rendered

| Field | Emitted at | Why it matters |
|---|---|---|
| `muted` | `Program.cs:629, 640` | Every chat row already carries its Telegram mute state. Nothing in `chatRow()` (`app.js:495-523`) renders it. |
| `at` (per-download timestamp) | `Program.cs:516` | The only available staleness signal for a stuck download; dropped by `refreshStatus()` (`app.js:992-999`). |
| `floodWait`, `downloadsPaused` | `Program.cs:529, 531` | Returned by `/api/status` but the Status tab renders only 3 of the 7 fields (`app.js:985-987`). |
| `defaults` block | `Program.cs:211-217` | Server-authored safe defaults for every rate limit; the UI hardcodes its own fallbacks instead (`app.js:727-729`) and offers no "reset to defaults" affordance. |
| `legacyDedupCount` | `Program.cs:370` | Rendered (`app.js:762`) but unexplained — a bare number with no tooltip or definition. |
| `bootstrapComplete` | `Program.cs:392` | Collapsed into a tooltip word ("complete"/"partial", `app.js:459`) rather than a visible state. |
| `hasMore` on log search | `app.js:1138` | Shown as text only; no pagination control exists despite `skip`/`limit` being supported (`Program.cs:306`). |

### 3.3 Accepted request fields no UI can set

`ChatPatch` (`Program.cs:607-614`) accepts nine fields and the UI drives all nine — but `ChatDto` (`TelegramClient/Models/ChatDto.cs`) persists **eight more** that the engine already honours at runtime and that no API endpoint exposes. See §5.

---

## 4. Usability defects

Severity: **S1** blocks or silently corrupts user intent · **S2** materially degrades daily use · **S3** polish.

### 4.1 Failures are invisible — S1

Five of the six data-loading functions swallow every error into the console:

```1029:1031:TelegramAutoDownload.Headless/wwwroot/app.js
    applySettingsThreadWarning(Number($('#s-threads').value));
  } catch (e) { console.warn(e); }
}
```

Same pattern at `app.js:468` (`loadChats`), `app.js:736` (`refreshQueue`), `app.js:1002` (`refreshStatus`), `app.js:1146` (`openLogsTab`), plus a fully empty `catch { }` at `app.js:485` and another at `app.js:700`. If the backend returns 500, the database is locked, or the disk is full, **the user sees a table that simply does not change.** There is no toast, no inline banner, no retry affordance, no last-updated timestamp. The `#conn` dot is the only failure signal and it only tracks `/api/login/status` (`app.js:1043`).

### 4.2 All feedback is `window.alert` / `window.confirm` — S1

50 call sites (`app.js`, verified by count). Consequences: every confirmation blocks the JS thread and the 2.5 s poller; messages cannot be copied or inspected; multi-line technical detail is crammed into an unstyled OS dialog (e.g. the 4-line bootstrap confirm at `app.js:542`); success and catastrophic failure look identical; screen readers receive no live-region announcement because the dialog is not part of the document; and mobile Safari/Chrome allow users to permanently suppress dialogs for the origin — after which **the app gives no feedback at all**.

### 4.3 Optimistic edits are never rolled back — S1 (data integrity)

```525:539:TelegramAutoDownload.Headless/wwwroot/app.js
  const patch = {};
  tr.addEventListener('change', async (e) => {
    const t = e.target;
    if (t.dataset.k) {
      patch[t.dataset.k] = t.type === 'checkbox' ? t.checked
                          : t.type === 'number' ? Number(t.value)
                          : t.value;
    } else if (t.dataset.p) {
      patch.plugins = { ...(patch.plugins || c.plugins || {}), [t.dataset.p]: t.checked };
    }
    try { await api(`/api/chats/${c.id}`, { method: 'PATCH', body: JSON.stringify(patch) }); }
    catch (err) { alert(err.message); return; }
    Object.assign(c, patch);
    if ('selected' in patch) tr.classList.toggle('selected', patch.selected);
  });
```

Two defects in fifteen lines. First, on PATCH failure the code alerts and returns — but the checkbox or text input **remains visually changed**, so the UI now shows a setting that was never saved, and it persists that lie until the next full re-render. Second, `patch` is a closure accumulator: it is never cleared, so once a user edits three fields, every subsequent change re-sends all four values. A later failed edit therefore re-submits earlier values, and there is no per-field save indicator to tell the user which of their edits actually landed.

### 4.4 No live updates — fixed 2.5 s polling of 9 endpoints — S1

```1033:1047:TelegramAutoDownload.Headless/wwwroot/app.js
async function poll() {
  if (!webAuthenticated || mustChangeWebPassword) return;
  try {
    const [ls, flood] = await Promise.all([api('/api/login/status'), api('/api/flood-wait')]);
    renderFloodBanner(flood);
    $('#conn').className = ls.loggedIn ? 'ok' : 'bad';
    if (ls.loggedIn && !loggedIn) await refreshLoginStatus();
    else if (!ls.loggedIn && loggedIn) await refreshLoginStatus();
    if (loggedIn && $('#tab-status').classList.contains('active')) refreshStatus();
    if (loggedIn && $('#tab-queue').classList.contains('active')) refreshQueue();
  } catch { if ($('#conn')) $('#conn').className = 'bad'; }
}
```

With the Queue tab open this issues 2 + 7 requests every 2.5 s (`refreshQueue` fires seven parallel calls at `app.js:702-710`), i.e. ~3.6 req/s forever, each running `GROUP BY` aggregates over the whole `Media` table (`MediaRepository.cs:246-267`) — unbounded cost as the table grows. There is no backoff when the tab is hidden (`document.visibilityState` is never consulted), no backoff on error, no `ETag`/`If-None-Match`, and no jitter. Meanwhile the infrastructure for push already exists and is proven: `/api/logs/stream` is a working SSE endpoint (`Program.cs:319-357`) consumed by `EventSource` (`app.js:1088`). Every other live surface ignores it.

Each `refreshQueue()` also blows away and rebuilds four `<tbody>` elements (`app.js:769, 794, 851, 880`), so **a rate-limit value you are typing, a row you have scrolled to, and any focused control are destroyed every 2.5 s.** The rate-limit inputs are explicitly overwritten mid-edit at `app.js:727-729`.

### 4.5 Search triggers a network request per keystroke — S2

`$('#chat-search').oninput = () => renderChats();` (`app.js:479`) and `renderChats()` unconditionally re-fetches `/api/queue/stats/by-chat` (`app.js:484`) before filtering an array that is already in memory. No debounce. Typing "news" issues four aggregate queries and four full table rebuilds.

### 4.6 No loading, empty, or error states — S2

There is no spinner, skeleton, or progress affordance anywhere except two inline button states (`app.js:473`, `app.js:543`). On first paint every table is an empty `<table>` with headers and nothing else, which is indistinguishable from "you have no chats" and from "the request failed." The Queue tables do have zero-row messages (`app.js:771, 797, 853, 882`), but the **Chats table has none** — `renderChats()` (`app.js:481-493`) has no zero-row branch, so a user who has not yet refreshed from Telegram sees a bare 14-column header row and no instruction to click Refresh. Same for the live-downloads table (`app.js:988-1001`): idle and broken look identical.

### 4.7 No form validation — S2

No input is validated client-side. Concretely: App ID is coerced with `Number($('#appId').value)` (`app.js:401`) so an empty field posts `0` and returns a server 400 rendered as a raw string; the phone field (`index.html:60`) accepts anything; the per-chat regex field (`index.html:131`, `app.js:517`) accepts syntactically invalid regex, which is split on `;` and persisted server-side (`Program.cs:275-276`) with no compile check on either side — the failure then surfaces only as skipped or failed downloads later; the folder-template field (`app.js:518`) accepts arbitrary text with no token validation or preview, despite the supported token set being documented in code (`ChatDto.cs:60`); min-MB has `min="0"` but no `max` and no rejection of negative or absurd values before the PATCH. Password fields carry `minlength="8"` (`index.html:265-266`) but the JS checks only equality (`app.js:30`), so the 8-char rule is enforced solely by the server (`WebAuthService.cs:102`).

### 4.8 Destructive-action confirmation is inconsistent — S2

Bootstrap, test-download, Telegram sign-out, pause, retry-all, delete-all-failed, and clear-queue all confirm (`app.js:439, 542, 553, 627, 649, 671, 689, 971`). But: **per-row Retry has no confirmation** (`app.js:867, 896`) while Retry-all does; **every per-chat setting change is unconfirmed and instant**, including unchecking Monitor, which silently stops queue processing for that chat; and the rate-limit save implements an actively harmful pattern —

```926:939:TelegramAutoDownload.Headless/wwwroot/app.js
    if ($('#q-parallel').checked && !confirm('Parallel bootstrap is now enabled. Multiple history scans can run at once (shared rate limit). Keep this on?')) {
      await api('/api/settings/limits', {
        method: 'POST',
        body: JSON.stringify({
          scannerApiCapacity: capacity,
          scannerApiRefillPerSecond: refill,
          downloadThreads: threads,
          allowParallelBootstrap: false,
```

— it saves the dangerous setting first, then asks whether you meant it, then silently issues a second POST to undo it. There is a window where the risky configuration is live, and the user gets no confirmation that the undo succeeded.

### 4.9 Keyboard and screen-reader semantics are largely absent — S1 for assistive users

The entire codebase contains **six ARIA attributes** (`index.html:15, 39, 96, 303, 310, 313`) and **zero `:focus` or `:focus-visible` rules** in `styles.css` — keyboard users navigating a 14-column grid of checkboxes get only the browser default outline, which `styles.css:135` partially defeats by restyling `button` without a focus treatment. Specifics:

- Tab nav is plain buttons with no `role="tablist"`/`role="tab"`/`aria-selected` (`index.html:85-91`); the panels are `<section class="tab">` with no `role="tabpanel"` or `aria-labelledby` (`index.html:100, 144, 222, 238, 281`). Active state is conveyed only by a background colour (`styles.css:77`).
- Column headers for the two densest columns are the literal strings `V P M F` and `Y S D T` (`index.html:129-130`) with meaning hidden in `title` attributes, which most screen readers do not announce by default. The row checkboxes they label have `title` but no `aria-label` and no `<label>` (`app.js:507-515`), so each is announced as an unlabelled checkbox. A non-sighted user cannot tell Videos from Torrent.
- `#conn` (`index.html:93`) communicates connection state purely by dot colour (`styles.css:80-82`) with a static `title="Connection"` that never updates — invisible to screen readers and to colour-blind users.
- Error containers `#web-login-error`, `#login-error`, `#pwd-change-error`, `#settings-pwd-error`, `#limits-warnings`, `#parallel-warn` have no `role="alert"` or `aria-live`, so `showError()` (`app.js:153`) writes text nobody is told about. Only `#flood-banner` is correct.
- The forced password modal (`index.html:310`) has correct `role="dialog"`/`aria-modal` but **no focus trap and no Escape handling** — `showPasswordChangeModal` only focuses the first field (`app.js:24`); Tab walks straight out into the hidden app behind it.
- `aria-live="polite"` on a 2000-line log region (`index.html:303`, re-rendered wholesale at `app.js:1060-1069`) is worse than nothing: every SSE line instructs the screen reader to re-announce the entire buffer.

### 4.10 Colour contrast failures — S2

Against WCAG 2.1 AA (4.5:1 body text, 3:1 large text and UI boundaries):

| Rule | Colours | Ratio | Verdict |
|---|---|---|---|
| `.auth-footer` (`styles.css:42-43`) | `#94a3b8` on `#f8fafc` | ≈2.4:1 | fails |
| `button:disabled` (`styles.css:137`) | `#fff` on `#95a3b8` | ≈2.6:1 | fails |
| `.auth-brand p` (`styles.css:33`) | `#64748b` on `#fff`, 13px | ≈4.6:1 | marginal |
| `#conn.ok` / `#conn.bad` (`styles.css:81-82`) | colour-only status | n/a | fails 1.4.1 (use of colour) |
| `.pill.*` (`styles.css:157-162`) | colour-only status at 11px | n/a | fails 1.4.1 |
| `.sync-stale` / `.sync-ok` (`styles.css:97-98`) | red/green only, 12px | n/a | fails 1.4.1 |

Status is encoded as colour in five different places with no icon, text, or pattern backup. There is no dark mode, no `prefers-color-scheme` handling, and no `prefers-reduced-motion` guard on the animated progress bar (`styles.css:164`).

### 4.11 No responsive behaviour whatsoever — S1 for mobile

`styles.css` contains **zero `@media` rules**. The Chats table is 14 columns of which six contain editable inputs stretched to `width:100%` (`styles.css:148`); the Per-chat queue table is 11 columns. Neither table has an `overflow-x` scroll container — `main` is `max-width:1400px` (`styles.css:84`) and the tables are `width:100%` (`styles.css:144`), so on a phone the layout either overflows the viewport with no scroll affordance or crushes every input to a few pixels. `.cards { display:flex }` with `min-width:140px` metrics (`styles.css:151-152`) overflows at eight cards. `header nav` is flex with no wrap (`styles.css:75`). The `.log-toolbar` is the only container that wraps (`styles.css:117`). For a self-hosted appliance that owners will absolutely check from a phone, the dashboard is unusable below roughly 1100 px.

### 4.12 A user cannot understand system state without reading logs — S1 (product-defining)

This is the defect that matters most commercially.

- **Why did a download fail?** The Failed table shows `lastError` (`app.js:894`), a raw exception string clipped to 280 px (`styles.css:96`) with no categorisation (network vs. auth vs. disk vs. rate limit), no remediation hint, and no link to the corresponding log lines.
- **Why was an item skipped?** The UI's own fallback text admits the data may not exist: `'(no reason recorded — re-bootstrap after update to capture reasons)'` (`app.js:858`). The tab-level explanation is a static paragraph listing six possible reasons (`index.html:200`) and asks the user to hover each row. Skip reasons are not machine-readable, not filterable, and not countable by cause.
- **Is a download stalled?** `/api/downloads` returns no speed and no ETA (`Program.cs:504-517`) — the desktop app shows both (`MainWindow.xaml:558, 561`). The one staleness proxy that *is* returned, `at`, is discarded (`app.js:992-999`). A download frozen at 34% for an hour looks exactly like one that is progressing.
- **What is `pending` vs `queued`?** Both are rendered as metric cards and table columns (`app.js:750-751`, `index.html:192`) with no definition anywhere in the UI. The distinction is meaningful — `pending` rows are promoted to `queued` only after a bootstrap completes (`MediaRepository.cs:236-243`) — but a user cannot learn that from the dashboard.
- **Are downloads paused?** Only visible on the Queue tab (`app.js:180-195`). The Status tab shows "Active: 0" with no explanation, even though `/api/status` returns `downloadsPaused` (`Program.cs:529`).
- **Is a flood-wait in progress?** Correctly bannered (`app.js:166-177`) — the one genuinely good state affordance in the product.
- **How much has the system ever downloaded?** Unanswerable. There is no statistics view, no time series, no per-day or per-chat totals beyond a raw byte count, and no disk-space indicator — the desktop app has all of these (`MainWindow.xaml:163-182`).

### 4.13 Additional defects — S2/S3

- **No routing or deep links** (§1). Reload always returns to Chats; no view can be shared with a collaborator or bookmarked.
- **Tables cannot be sorted, paged, or configured.** Chats sort is hardcoded to monitored-first-then-name (`app.js:488`); Per-chat queue to total descending (`app.js:795`). No column headers are clickable. Failed items are capped at 50 and skipped at 100 (`app.js:707-708`) against a server cap of 200 (`Program.cs:400`) with **no pagination and no indication that the list is truncated** — a user with 5,000 failures sees 50 and is told nothing.
- **Log viewer re-renders the entire buffer on every received line** (`app.js:1077` → `1060`), rebuilding up to 2,000 DOM nodes per SSE message; `/api/logs/stream` supports `fromOffset` resumption (`Program.cs:319`) which the client never uses, so a reconnect silently drops or duplicates lines. There is no log download/export despite the files being on disk.
- **`escape()` omits the apostrophe** (`app.js:603`). All current interpolations are into double-quoted attributes, so this is not presently exploitable, but Telegram-supplied chat names are injected into `innerHTML` at nine sites (`app.js:499, 777, 804, 859, 888, 760`) and the first single-quoted attribute anyone adds becomes an injection point.
- **`renderQueueCards` and `renderChats` use `innerHTML +=`** (`app.js:763`) and full-table rebuilds rather than keyed updates — O(n) DOM churn on every poll.
- **No first-run guidance beyond a paragraph.** A new user landing on an empty Chats table must infer the whole workflow (refresh → select → configure → bootstrap) from the static explainer card (`index.html:101-109`). There is no checklist, no progress, no "you have no chats yet — start here."
- **Credential setup offers no validation or help beyond a link** (`index.html:52-57`); a wrong App ID surfaces only as a login failure later.
- **No `<noscript>`, no favicon, no per-view `<title>`** (`index.html:6`), so browser tabs and history are indistinguishable.
- **Session expiry is handled but not explained**: `api()` detects `webAuthRequired` and throws "Session expired — sign in again." (`app.js:143`), but the thrown message lands in whichever `console.warn` swallowed it, so in practice the user watches a dead dashboard until they reload.

---

## 5. Web vs desktop capability gap

Three categories. **Category B is the commercially important one**: the capability is already implemented and honoured by code the headless service references — only an endpoint field and a control are missing.

### Category A — in the web UI today

| Capability | Web | Desktop |
|---|---|---|
| Per-chat monitor toggle | `index.html:124`, `app.js:500` | `MainWindow.xaml:233` |
| Per-chat media types (V/P/M/F) | `app.js:507-510` | `MainWindow.xaml:317-320` |
| Per-chat provider toggles (YouTube/Social/Direct/Torrent) | `app.js:512-515` | `MainWindow.xaml:344-352` |
| Per-chat min file size | `app.js:504` | `MainWindow.xaml:329` |
| Per-chat regex filter | `app.js:517` (plain text field) | `FilterDialog.xaml` (full dialog, add/remove, file-name vs message-text modes at `FilterDialog.xaml:222-224`) |
| Per-chat folder template | `app.js:518` (single field) | `MainWindow.xaml:378` (dialog) |
| Per-chat history toggle | `app.js:519` | `MainWindow.xaml:421` |
| Global folder / layout / threads | `index.html:241-256` | `SettingsWindow.xaml:176-194` |
| Log viewing with level filter + live follow | `index.html:281-305` | `LogViewerWindow.xaml` |
| Telegram login incl. 2FA | `index.html:52-74` | `SettingsWindow.xaml:57-62` |
| Queue/bootstrap management, retry, pause | Queue tab | — (web-only capability) |

### Category B — backend already supports it; the web UI simply never surfaces it ⚑

These need no new engine work. The fields are persisted by `ConfigStore`, merged on refresh by `HeadlessHost.RefreshChatsAsync` (`HeadlessHost.cs:169-189`), and acted on by `TelegramApp` at runtime.

| Capability | Already implemented at | What is missing | Est. |
|---|---|---|---|
| **Reaction emoji on download complete** | `ChatDto.ReactionIcon` (`ChatDto.cs:21`), persisted `HeadlessHost.cs:170`, **sent by the running engine** `TelegramApp.cs:414-420` | one field on `ChatPatch` (`Program.cs:607`) + one emoji picker | 0.5 d |
| **Reaction emoji on download start** | `ChatDto.DownloadStartIcon` (`ChatDto.cs:23`), persisted `HeadlessHost.cs:171` | same | 0.25 d |
| **Reaction emoji on history append** | `ChatDto.HistoryIcon` (`ChatDto.cs:88`), persisted `HeadlessHost.cs:188`, **sent by the engine** `TelegramApp.cs:373-375` | same | 0.25 d |
| **Available-reactions picker** | `TelegramApp.GetChatAvailableReactionsAsync` (`TelegramApp.cs:1489`) — fully implemented, with global fallback and caching | one `GET /api/chats/{id}/reactions` passthrough | 0.5 d |
| **Per-chat mute toggle** | `TelegramApp.MuteChatAsync` (`TelegramApp.cs:1594`); state already **returned to the browser** (`Program.cs:629, 640`) | `POST /api/chats/{id}/mute` passthrough + a toggle | 0.5 d |
| **Per-provider folder templates (4×)** | `SocialDownloadFolderTemplate`, `YoutubeDownloadFolderTemplate`, `OtherDownloadFolderTemplate`, `TorrentDownloadFolderTemplate` (`ChatDto.cs:66-75`), all persisted `HeadlessHost.cs:183-186` | 4 fields on `ChatPatch` + a form | 0.5 d |
| **History export to JSONL** | `TelegramApp.ExportChatHistoryAsync` (`TelegramApp.cs:1089`) — complete with pagination and progress callbacks | `POST /api/chats/{id}/export-history` + a button | 1 d |
| **Channel member browser** | `TelegramApp.GetChannelMembersAsync` (`TelegramApp.cs:966`) | `GET /api/chats/{id}/members` + a drawer | 1 d |
| **Message browser / manual download** | `FetchBrowseHistoryPageAsync` (`TelegramApp.cs:824`), `ManualDownloadMessagesAsync` (`TelegramApp.cs:855`), `ExecuteByMessageIdAsync` (`TelegramApp.cs:709`) | browse + select endpoints (desktop has `ChatBrowseWindow`, `MainWindow.xaml.cs:633`) | 2 d |
| **Monitored-only history sync** | `POST /api/chats/{id}/sync` **already mapped** (`Program.cs:285`); `HeadlessHost.SyncHistoryAsync` (`HeadlessHost.cs:232`) is dead code | one UI call site | 0.25 d |
| **Per-chat bootstrap progress** | `GET /api/chats/{id}/bootstrap` **already mapped** (`Program.cs:447`) | one inline progress component | 0.5 d |
| **Authoritative pause state everywhere** | `GET /api/queue/downloads` **already mapped** (`Program.cs:488`) | global banner | 0.25 d |
| **Rate-limit "reset to defaults"** | `defaults` block already in the response (`Program.cs:211-217`) | one button | 0.25 d |
| **Server-authored threshold bands** | `thresholds` block already consumed (`app.js:715-719`) but duplicated as hardcoded client constants (`app.js:207-211`) | delete the duplicate | 0.25 d |

**Category B subtotal: ≈8.25 engineer-days for 14 user-visible capabilities**, of which three are pure UI work against endpoints that already exist and ship today.

### Category C — genuinely new backend work

| Capability | Desktop implementation | Why it is new for headless |
|---|---|---|
| Live speed + ETA per download | `MainWindow.xaml:558, 561`; computed in `DownloadProgressService` (`Services/DownloadProgressService.cs:179, 455`) | `DownloadEntry` (`HeadlessHost.cs:12-22`) carries no rate; `/api/downloads` returns none (`Program.cs:504-517`). Service lives in the WPF project, which Headless does not reference (`TelegramAutoDownload.Headless.csproj:28-36`) |
| Session + all-time statistics | `MainWindow.xaml:163-182`; `Services/StatisticsService.cs` | Same: WPF-only project. Needs a shared library + a stats endpoint |
| Disk-space monitoring | `MainWindow.xaml:178-182`; `Services/DiskSpaceService.cs` | Same: WPF-only |
| Telegram bot notifications + per-event preferences | `SettingsWindow.xaml:114-150`; `TelegramAutoDownload/Notification.cs:38, 72, 129, 155` | `ConfigParams` already persists `NotifyOnStartup/Progress/Complete/Error` and `BotToken`/`ChatId` (`ConfigParams.cs:15-16, 43-46`) but `Notification.cs` is in the WPF project and **no headless code reads those flags** — verified: zero matches for `NotifyOn` under `TelegramAutoDownload.Headless/` |
| Cancel an in-flight download | `MainWindow.xaml:515` (`Cancel All`), `DownloadProgressService.cs:349, 407` | No cancel endpoint exists; the queue can only be paused |
| Clear completed / auto-clean list | `MainWindow.xaml:503-508`; `ConfigParams.AutoCleanDownloads` (`ConfigParams.cs:49`) | Persisted but unread by headless; the in-memory trim is hardcoded to 60 s (`HeadlessHost.cs:140-146`) |
| Open containing folder | `MainWindow.xaml:597` | Not meaningful remotely; replace with a copy-path action |
| Settings export / import | `SettingsWindow.xaml:231-233` | No endpoint |
| Multi-user accounts, roles, audit log | — (desktop is single-user by nature) | `WebAuthService` is hard-wired to exactly one username (`WebAuthService.cs:22, 208-210`) |
| Per-chat yt-dlp quality | **does not exist** — see §6 | — |
| Quiet hours | **does not exist** — see §6 | — |

---

## 6. Corrections to the record

Two capabilities attributed to the desktop app are not implemented anywhere in this repository. Both should be treated as *new features*, not as porting work.

1. **Quiet hours does not exist.** `README.md:33, 262, 313, 330` documents a daily window and names a `QuietHoursHelper`. A case-insensitive search for `quiethours` across the repository matches **only `README.md`**; no file with `Quiet` in its name exists, and `ConfigParams` (`ConfigParams.cs`) has no quiet-hours fields. The README is describing a feature that was never built, or that was removed without updating the docs. Designing a UI for it means specifying the backend from scratch.

2. **Per-chat yt-dlp quality was deliberately removed.** `ChatDto.YtdlpQuality` still exists (`ChatDto.cs:56`) but `ConfigParams.NormalizeYtDlpQualityForAllChats()` (`ConfigParams.cs:62-66`) overwrites it to `HighestVideoQuality` for every chat after every load, and the WPF app sets the same constant in all three of its own code paths (`MainWindow.xaml.cs:245, 302, 697`). The in-code comment states the UI "was removed; every chat always uses best video+audio." Re-exposing it in the web UI would require reverting that normalisation — a product decision, not a gap.

---

## 7. Summary of severities

| Severity | Count | Defects |
|---|---|---|
| S1 | 6 | §4.1 invisible failures · §4.2 alert/confirm-only feedback · §4.3 un-rolled-back optimistic edits · §4.4 polling with focus destruction · §4.9 assistive-tech access · §4.11 no responsive layout · §4.12 system state unknowable |
| S2 | 5 | §4.5 per-keystroke fetches · §4.6 missing loading/empty states · §4.7 no validation · §4.8 inconsistent confirmation · §4.10 contrast |
| S3 | — | §4.13 items (routing, sorting, log render, escaping, first-run) span S2/S3 |

Target-state design, customization layer, technology recommendation, and the prioritized backlog are in `09-ui-target-design.md`.

# 09 — Target UI Design

**Branch:** `enterprise-readiness` · **Date:** 2026-09-30
**Companion:** `09-ui-audit.md` (current-state audit — read that first for the defect and orphan inventory)
**Product goal (owner's words):** *"fully usable and completely customizable UI for users to view and manage."*
**Status:** design only. No source file was modified. Implementation is a separately-reviewed phase.

Every screen below cites the endpoints it calls. Endpoints that do not exist are labelled **NEW BACKEND WORK** with a contract, and collected in §6.

---

## 1. Design principles

1. **The dashboard answers "what is my system doing?" before it answers "what can I change?"** Today the landing view is a 14-column configuration grid; the target landing view is an overview of state.
2. **No state is knowable only from the logs.** Every skip, failure, stall, and pause must be explained in the UI with a cause, a count, and a remediation action. (Audit §4.12 is the defect this principle exists to kill.)
3. **Push, don't poll.** One SSE stream replaces nine polled endpoints. The pattern is already proven in-repo at `Program.cs:319-357`.
4. **Nothing silently fails.** Every mutation resolves visibly: optimistic update, then confirmed or rolled back with an inline reason and a retry.
5. **Bulk by default.** A user with 400 chats must never configure them one row at a time. Multi-select + templates + bulk apply are first-class, not conveniences.
6. **Customizable, but correct out of the box.** Defaults must be safe and complete; customization is additive. Rate-limit defaults already come from the server (`Program.cs:211-217`) — that is the model.
7. **Accessible and responsive are acceptance criteria, not phases.** WCAG 2.1 AA and a working 375 px layout are part of "done" for each screen.

---

## 2. Target information architecture

```
┌ Unauthenticated ──────────────────────────────────────────────┐
│ A1  Dashboard sign-in                                         │
│ A2  Forced password change (first run / default password)      │
│ A3  First-run setup wizard  ── step 1 credentials              │
│                             ── step 2 Telegram login          │
│                             ── step 3 storage + layout        │
│                             ── step 4 pick chats              │
│                             ── step 5 safety limits review    │
└───────────────────────────────────────────────────────────────┘
┌ Authenticated shell ──────────────────────────────────────────┐
│ Global: sidebar nav · command palette (⌘K) · notification      │
│         centre · system-state strip · theme switch · user menu │
│                                                                │
│ 1  Overview            customizable widget dashboard           │
│ 2  Chats               2a list (bulk select)                   │
│                        2b chat detail drawer (rules)            │
│                        2c discover / add chats                  │
│                        2d message browser (per chat)            │
│ 3  Downloads           3a queue manager (all items)             │
│                        3b live activity                          │
│                        3c scan & bootstrap jobs                  │
│ 4  Insights            4a statistics  4b reports/export         │
│ 5  Logs                unified log explorer                      │
│ 6  Settings            6a general  6b storage  6c limits         │
│                        6d notifications  6e appearance           │
│                        6f templates  6g integrations             │
│ 7  Access              users · sessions · audit log              │
└───────────────────────────────────────────────────────────────┘
```

Navigation is a collapsible left sidebar (icon-only when collapsed, persisted preference) with a top bar for the system-state strip. Below 900 px the sidebar becomes a bottom tab bar with the four primary destinations (Overview, Chats, Downloads, Logs) and a "More" sheet.

**Routing.** Hash or History routing with one URL per view and filter state encoded in the query string — `#/downloads?status=failed&chat=123&sort=-attempts`. This fixes audit §4.13 (no deep links) and is a precondition for saved views (§5.2).

**The system-state strip** is persistent across every screen and is the direct answer to audit §4.12. It renders, left to right: connection state (icon + text, not colour alone), Telegram account, downloads running/paused, active count, flood-wait countdown, disk free, and last-updated age. Source: the consolidated `/api/events` stream (NEW BACKEND WORK, §6.1) with `/api/status` (`Program.cs:519`) as the initial snapshot and polling fallback.

---

## 3. Screen specifications

Each spec lists **purpose · contents · states · actions · endpoints**.

### A1 — Dashboard sign-in

- **Purpose.** Gate the dashboard; make it obvious when the deployment is still insecure.
- **Contents.** Product mark, username + password, "remember this device" (maps to session length), inline error region with `role="alert"`, a persistent default-password warning, and a footer stating the configured auth mode.
- **States.** idle · submitting (button busy, inputs locked) · invalid credentials (inline, field-level, focus returned to password) · auth disabled (`enabled: false` → skip straight through, `Program.cs:54`) · backend unreachable (distinct message: "cannot reach the server", not "invalid password").
- **Actions.** Sign in · reveal password · Enter-to-submit.
- **Endpoints.** `GET /api/web-auth/status` (`Program.cs:52`) · `POST /api/web-auth/login` (`Program.cs:68`).

### A2 — Forced password change

- **Purpose.** Prevent a dashboard from ever running on `changeme`.
- **Contents.** Modal, non-dismissible, with current/new/confirm, a live strength and rule checklist (≥8 chars, not `changeme` — mirroring `WebAuthService.cs:102-111`), and an explanation of consequence.
- **States.** idle · validating (client-side rules evaluated live) · server rejection (inline) · success (modal closes, toast, continue to A3 or Overview).
- **Actions.** Update password. Deliberately no skip.
- **Fixes.** Audit §4.9 — this modal needs a focus trap, Escape-suppression, and `aria-describedby` on the rule list.
- **Endpoints.** `POST /api/web-auth/change-password` (`Program.cs:89`).

### A3 — First-run setup wizard

- **Purpose.** Take a fresh container to "downloading correctly" without the user reading a README. Replaces today's implicit workflow (audit §4.13).
- **Contents.** Five steps with a progress rail, each step resumable and independently revisitable later from Settings.
  1. **Telegram API credentials** — App ID + API Hash, with inline instructions, a link to `my.telegram.org`, format validation (App ID is a positive integer; API Hash is 32 hex chars), and a *verify* action rather than blind save. Credentials are write-only in the UI: never echoed back (today `/api/settings` correctly returns only `appIdConfigured`, `Program.cs:162` — preserve that).
  2. **Telegram login** — embeds A4.
  3. **Storage** — download folder with a server-side writability + free-space check, and the folder-layout choice rendered as three labelled diagrams of the resulting tree rather than a `<select>` of sentences (`index.html:245-249`).
  4. **Choose chats** — embeds 2c in bulk-select mode, with a recommended starter template applied.
  5. **Safety review** — shows the effective rate limits with the server's `defaults` (`Program.cs:211-217`) and the `thresholds` bands, explains FLOOD_WAIT in one sentence, and offers "keep safe defaults" as the primary action.
- **States.** per-step: incomplete · in progress · complete · failed-with-reason · skipped (only steps 4–5 are skippable). Global: resumable across reloads (step index persisted in UI preferences, §5).
- **Actions.** Next/Back · verify credentials · test folder · apply starter template · finish.
- **Endpoints.** `POST /api/settings/credentials` (`Program.cs:171`) · `POST /api/settings/download-folder` (`Program.cs:179`) · `POST /api/settings/folder-layout` (`Program.cs:185`) · `GET/POST /api/settings/limits` (`Program.cs:199, 228`) · `POST /api/chats/refresh` (`Program.cs:251`) · **NEW:** `POST /api/settings/verify-credentials` (§6.4), `POST /api/settings/verify-folder` (§6.5), `GET /api/setup/state` (§6.6).

### A4 — Telegram login

- **Purpose.** Authenticate the Telegram account, including 2FA, with recoverable errors.
- **Contents.** Stage-driven single card: phone (with country-code helper) → code (6 segmented inputs, paste-aware, auto-submit) → cloud password. A stage rail shows where the user is. Errors render inline with the failing stage retained.
- **States.** `NotConnected` · `AwaitingCode` · `AwaitingPassword` · `LoggedIn` · error-with-stage (all from `login.Stage`, `Program.cs:123`). Plus: code expired (offer resend), phone rejected (offer edit), flood-wait during login (show countdown from `/api/flood-wait`, `Program.cs:288`).
- **Actions.** Submit phone/code/password · change phone · resend code (**NEW BACKEND WORK**, §6.7 — no resend endpoint exists today, so the current UI's only recovery is a full reload) · cancel.
- **Endpoints.** `GET /api/login/status` (`Program.cs:120`) · `POST /api/login/phone|code|password` (`Program.cs:129, 136, 143`) · `GET /api/flood-wait` (`Program.cs:288`).

### 1 — Overview (customizable widget dashboard)

- **Purpose.** The single screen that answers "is my system healthy, and what needs my attention?" This is the new default landing view, replacing the Chats table (`app.js:75`).
- **Contents.** A responsive widget grid. Each widget is independently addable, removable, resizable, and reorderable (§5.1). Shipped widget catalogue:
  - **Health summary** — connection, Telegram account, paused/running, flood-wait, disk free, uptime.
  - **Needs attention** — the product's action inbox: failed count with top failure causes, skipped count with top skip causes, stale chats (no sync in >24 h, logic already present at `app.js:457`), bootstrap jobs errored, default password still in use, disk below threshold. Each row is a one-click deep link into a pre-filtered Downloads view.
  - **Active downloads** — compact live list with per-item speed, ETA, and a stall badge (**NEW BACKEND WORK**, §6.2).
  - **Queue composition** — donut of pending/queued/in-progress/done/failed/skipped with definitions on hover *and* in an expandable glossary (fixes audit §4.12's `pending` vs `queued` confusion).
  - **Throughput** — files and bytes per hour/day sparkline (**NEW BACKEND WORK**, §6.3).
  - **Top chats** — by volume, bytes, or failure rate.
  - **Storage** — disk free/used and download-folder size (**NEW BACKEND WORK**, §6.8).
  - **Recent errors** — last N `ERR`/`WRN` log lines, each linking into the Logs explorer anchored at that line.
  - **Bootstrap progress** — per-job progress bars.
  - **Quick actions** — pause/resume, refresh chats, retry all failed.
- **States.** first-run (widgets show "no data yet" with the action that produces data) · loading (per-widget skeletons, never a blank page) · live · degraded (stream dropped → strip shows "reconnecting", widgets grey their last-known value and show its age — this is the explicit fix for audit §4.1) · empty-per-widget · error-per-widget with retry.
- **Actions.** Add/remove/resize/reorder widgets · switch layout preset · reset layout · pause/resume · drill through from any metric.
- **Endpoints.** `GET /api/status` (`Program.cs:519`) · `GET /api/queue/stats` (`Program.cs:360`) · `GET /api/queue/stats/by-chat` (`Program.cs:375`) · `GET /api/downloads` (`Program.cs:504`) · `GET /api/bootstrap/jobs` (`Program.cs:453`) · `GET /api/queue/downloads` (`Program.cs:488` — **currently orphaned**) · `GET /api/flood-wait` (`Program.cs:288`) · `GET /api/logs/tail?level=ERR` (`Program.cs:293`) · **NEW:** `/api/events` (§6.1), `/api/stats/timeseries` (§6.3), `/api/system/storage` (§6.8), `/api/queue/reasons` (§6.9).

### 2a — Chats (list)

- **Purpose.** Find chats and act on many at once. Replaces the 14-column always-editable grid.
- **Contents.** A virtualized data table with a configurable column set (§5.3). Default columns: selection checkbox, name + @username + avatar, type badge, monitored switch, media-type summary chip (`V P M F` rendered as labelled pills, not bare checkboxes — fixes audit §4.9), provider summary chip, queue summary (done/failed/skipped mini-bar), last sync with staleness, and a row overflow menu. Every other attribute moves into the detail drawer (2b), so the list stays scannable at 375 px.
  - **Toolbar.** Search (debounced, 250 ms — fixes audit §4.5), filter chips (monitored, type, has failures, stale, muted, provider enabled), saved-view selector (§5.2), density control, column manager, bulk-action bar (appears on selection), and "Refresh from Telegram" with a last-refreshed timestamp.
  - **Bulk action bar.** Apply template · enable/disable monitoring · set media types · set providers · set min size · mute/unmute · bootstrap selected · clear queue · export selection as CSV. Shows "N of M selected", select-all-matching-filter, and a confirmation sheet that enumerates exactly what will change on how many chats before executing.
- **States.** not-yet-refreshed (explicit empty state with a Refresh call-to-action — today this is a bare header row, audit §4.6) · loading skeleton rows · loaded · filtered-to-zero (distinct from empty, with "clear filters") · refresh failed (inline banner naming the cause, retry preserved) · not logged in (`/api/chats/refresh` returns 400, `Program.cs:258`) · row-level saving/saved/failed indicators.
- **Actions.** Search · filter · sort any column · select · bulk-apply · per-row quick toggles (monitor, mute) · open drawer · bootstrap · sync · test-download · browse messages.
- **Endpoints.** `GET /api/chats` (`Program.cs:245`) · `POST /api/chats/refresh` (`Program.cs:251`) · `PATCH /api/chats/{id}` (`Program.cs:261`) · `POST /api/chats/{id}/bootstrap` (`Program.cs:415`) · `POST /api/chats/{id}/sync` (`Program.cs:285` — **currently orphaned**) · `POST /api/chats/{id}/test-download` (`Program.cs:418`) · `GET /api/queue/stats/by-chat` (`Program.cs:375`) · **NEW:** `PATCH /api/chats/bulk` (§6.10), `POST /api/chats/{id}/mute` (§6.11).

### 2b — Chat detail drawer (rule editor)

- **Purpose.** Everything about one chat, in a focused surface, with validation and preview.
- **Contents.** Side drawer (full-screen sheet on mobile), header with name/type/members/mute, and tabbed sections:
  - **Monitoring** — monitor switch with a plain-language explanation of forward-only capture vs. bootstrap (today a static paragraph at `index.html:101-109`); mute toggle.
  - **Media rules** — media types as labelled switches; min file size with a unit selector; *effective rule summary* in prose ("Videos and photos over 5 MB").
  - **Providers** — YouTube / Social / Direct / Torrent switches with a one-line description of each and a warning when yt-dlp or a plugin is unavailable.
  - **Filters** — a proper filter builder, not a `;`-joined text box (`index.html:131`): one row per pattern, add/remove/reorder, live regex compilation with error text, a test panel where the user pastes a filename and sees match/no-match, and a "matches these recent files" preview. Mirrors the capability of the desktop `FilterDialog.xaml`.
  - **Folders** — the chat template plus the four per-provider templates (`ChatDto.cs:66-75`, already persisted at `HeadlessHost.cs:183-186`, **never exposed by any endpoint**), each with a token palette (`{Type}`, `{ChatName}`, `{Year}`, `{Month}`, `{Day}`, `{Platform}` — per `ChatDto.cs:60`) and a live resolved-path preview.
  - **Reactions** — emoji pickers for download-start, download-complete, and history-append, populated from the chat's real available reaction set. All three fields already exist and the engine already sends them (`TelegramApp.cs:373-375, 414-420`).
  - **History** — SaveHistory toggle and a "Export full history to JSONL" action (`TelegramApp.ExportChatHistoryAsync` already implemented, `TelegramApp.cs:1089`).
  - **Queue** — this chat's counts, its last sync watermark and `bootstrapComplete`, with retry/clear scoped to the chat.
  - **Danger zone** — clear queue (explains that files on disk are kept, as today's confirm does at `app.js:689`).
- **States.** loading · clean · dirty (explicit Save/Discard, with a navigation guard — replaces today's save-on-every-keystroke) · per-field invalid · saving · saved · save-failed with per-field error mapping and **rollback to the last server value** (fixes audit §4.3) · conflict (config changed elsewhere → offer reload).
- **Actions.** Edit any rule · validate regex · preview folder path · save/discard · save as template (§5.5) · apply an existing template · test 10 · bootstrap · sync · export history · browse messages · clear queue.
- **Endpoints.** `GET /api/chats` (`Program.cs:245`) · `PATCH /api/chats/{id}` (`Program.cs:261`) · `POST /api/chats/{id}/test-download` (`Program.cs:418`) · `POST /api/chats/{id}/bootstrap` (`Program.cs:415`) · `GET /api/chats/{id}/bootstrap` (`Program.cs:447` — **currently orphaned**) · `POST /api/queue/clear` (`Program.cs:481`) · **NEW:** extended `ChatPatch` (§6.12), `GET /api/chats/{id}/reactions` (§6.13), `POST /api/chats/{id}/export-history` (§6.14), `POST /api/validate/regex` (§6.15), `POST /api/validate/folder-template` (§6.16).

### 2c — Discover / add chats

- **Purpose.** Get from "I have 600 Telegram dialogs" to "these 12 are monitored with the right rules" in one pass.
- **Contents.** Full-screen dialog. Left: searchable, virtualized dialog list with type and member-count filters and a "not yet configured" filter. Right: the template to apply. Bottom: a summary bar ("Monitor 12 chats using template *Video archive*"). Includes the refresh cost warning, because `POST /api/chats/refresh` hits the Telegram API (`Program.cs:251`).
- **States.** never-refreshed · refreshing (progress, cancellable) · loaded · search-empty · not-logged-in · flood-waited (countdown, refresh disabled with reason).
- **Actions.** Refresh · search/filter · select many · choose template · apply · optionally queue bootstraps for all selected with a staggered start respecting the parallel-bootstrap guard (`Program.cs:551`).
- **Endpoints.** `POST /api/chats/refresh` (`Program.cs:251`) · `GET /api/chats` (`Program.cs:245`) · **NEW:** `PATCH /api/chats/bulk` (§6.10).

### 2d — Message browser (per chat)

- **Purpose.** Inspect what is actually in a chat and download specific items on demand — the web equivalent of the desktop `ChatBrowseWindow` (`MainWindow.xaml.cs:633`).
- **Contents.** Paginated message list with thumbnails, kind, size, date, and per-item status (already downloaded / queued / skipped-with-reason / not tracked), multi-select, and a "download selected" action.
- **States.** loading page · loaded · end-of-history · rate-limited · item-level queued/downloading/done/failed.
- **Actions.** Page through · filter by kind · select · download selected · re-download one · copy message link.
- **Endpoints.** **NEW:** `GET /api/chats/{id}/messages` (§6.17) and `POST /api/chats/{id}/download-messages` (§6.18) — both are thin wrappers over `TelegramApp.FetchBrowseHistoryPageAsync` (`TelegramApp.cs:824`) and `ManualDownloadMessagesAsync` (`TelegramApp.cs:855`), which already exist in the referenced `TelegramClient` library.

### 3a — Downloads: queue manager

- **Purpose.** One table for every tracked item in every state, with real filtering, sorting, paging, and bulk actions. Replaces today's four fixed tables and the silent 50/100-row truncation (audit §4.13).
- **Contents.** Single virtualized table over all statuses. Default columns: chat, file name, kind, size, status pill (icon + text), reason/error, attempts, discovered/queued/completed timestamps, row actions. Column set, order, and density are user-configurable (§5.3).
  - **Filter bar.** Status multi-select (`pending`, `queued`, `in_progress`, `done`, `failed`, `skipped`), chat multi-select, kind, size range, date range, free-text on filename, and **reason/cause facets with counts** — the key fix for audit §4.12: "Already on disk (1,204) · Type disabled (88) · Below min size (31) · Regex filter (12) · Unsupported kind (4)".
  - **Error taxonomy panel.** Failures grouped by cause class (network, auth/flood, disk, plugin, unsupported) with a count, a plain-language explanation, a recommended action, and "retry all in this group".
  - **Bulk bar.** Retry selected · retry all matching filter · delete selected · delete all failed · clear chat queue · export current view as CSV.
  - **Item detail drawer.** Full record, attempt history, the exact resolved target path, and a "view related log lines" jump into the Logs explorer.
- **States.** loading · loaded · filtered-to-zero · empty (no items ever tracked — with "bootstrap a chat" guidance) · paging · bulk operation in progress (progress + cancel) · bulk partially failed (per-item results, not one alert).
- **Actions.** Filter · sort · page · select · retry/delete single or bulk · pause/resume globally · open item detail · export.
- **Endpoints.** `GET /api/queue/items` (`Program.cs:397` — today only `failed`/`skipped` are ever requested) · `GET /api/queue/stats` (`Program.cs:360`) · `POST /api/queue/{chatId}/{messageId}/retry` (`Program.cs:456`) · `POST /api/queue/retry-failed` (`Program.cs:463`) · `POST /api/queue/retry-skipped` (`Program.cs:469`) · `POST /api/queue/delete-failed` (`Program.cs:475`) · `POST /api/queue/clear` (`Program.cs:481`) · `GET/POST /api/queue/downloads` (`Program.cs:488, 494`) · **NEW:** paged/filtered `/api/queue/items` (§6.19), `/api/queue/reasons` (§6.9), `POST /api/queue/items/bulk` (§6.20), `GET /api/queue/export` (§6.21).

### 3b — Live activity

- **Purpose.** Watch what is happening right now and tell progress from stall.
- **Contents.** Live list of in-flight items: chat, file, plugin, progress bar, **speed, ETA, elapsed**, bytes done/total, and a stall badge when no bytes have moved for N seconds (derivable today only from the `at` field the UI discards, `Program.cs:516` / `app.js:992`). Aggregate strip: total throughput, active/slot count, queue depth. Recently completed and recently failed rails with a configurable retention (the server currently hardcodes a 60 s trim at `HeadlessHost.cs:140-146`).
- **States.** idle-not-paused ("nothing queued" — with a link to bootstrap) · idle-paused (explicit, prominent) · active · flood-waited · stream-degraded (falls back to 2 s polling and says so).
- **Actions.** Pause/resume · cancel one download (**NEW BACKEND WORK**, §6.22) · retry a just-failed item · copy resolved path · jump to the item's chat · jump to its log lines.
- **Endpoints.** `GET /api/downloads` (`Program.cs:504`) · `GET /api/status` (`Program.cs:519`) · `POST /api/queue/downloads` (`Program.cs:494`) · **NEW:** `/api/events` (§6.1), speed/ETA fields (§6.2), `DELETE /api/downloads/{chat}/{msgId}` (§6.22).

### 3c — Scan & bootstrap jobs

- **Purpose.** Control and understand history scans — the operation most likely to get an account rate-limited.
- **Contents.** Job cards (not just table rows) per running/recent job: chat, phase, discovered/inserted counts, elapsed, rate, current message-ID watermark, and cancel. A global panel shows the parallel-bootstrap policy, the live token-bucket state, active vs. allowed job count (`Program.cs:210`), and the flood-wait history. A queue of pending scan requests for when the parallel guard blocks a start (`Program.cs:551-561`).
- **States.** none running (explains what bootstrap does and when to use it vs. Monitor) · running · completed · cancelled · errored-with-reason · blocked-by-parallel-guard (offers "queue it" instead of today's override-or-nothing `confirm` at `app.js:260`) · flood-waited.
- **Actions.** Start (single or for a selection) · cancel · queue · override the parallel guard with an explicit risk acknowledgement · adjust limits inline.
- **Endpoints.** `GET /api/bootstrap/jobs` (`Program.cs:453`) · `GET /api/chats/{id}/bootstrap` (`Program.cs:447` — **currently orphaned**) · `POST /api/chats/{id}/bootstrap` (`Program.cs:415`) · `DELETE /api/chats/{id}/bootstrap` (`Program.cs:444`) · `GET/POST /api/settings/limits` (`Program.cs:199, 228`) · `GET /api/flood-wait` (`Program.cs:288`).

### 4a — Statistics

- **Purpose.** Prove value over time and expose trends. The web UI has nothing here; the desktop has a session and all-time strip (`MainWindow.xaml:163-182`).
- **Contents.** Date-range selector (today, 7 d, 30 d, custom) driving: files and bytes downloaded over time; success vs. failed vs. skipped rates; breakdown by chat, by media kind, by provider/plugin; average size and average throughput; failure causes over time; storage growth; all-time totals with a "since" date (the desktop's `StatsStart`, `Services/StatisticsService.cs:27`).
- **States.** loading · loaded · insufficient data (<1 day) · range-empty · error.
- **Actions.** Change range · change grouping · toggle series · drill into any bar to a filtered Downloads view · export CSV/JSON · pin any chart to the Overview as a widget.
- **Endpoints.** **NEW:** `GET /api/stats/timeseries` (§6.3), `GET /api/stats/summary` (§6.23). Both are aggregations over the existing `Media` table, which already stores `size_bytes`, `kind`, `status`, `discovered_at`, `queued_at`, `started_at`, `completed_at` (`MediaRepository.cs:53-58, 66-70`) — the data exists; only the queries and endpoints are missing.

### 4b — Reports & export

- **Purpose.** Get data out for external use and for support.
- **Contents.** Export builder (scope, date range, columns, format CSV/JSON/JSONL), per-chat history exports (`TelegramApp.cs:1089`), a settings export/import pair matching the desktop's (`SettingsWindow.xaml:231-233`), and a one-click **diagnostic bundle** (redacted config + recent logs + queue stats) for support.
- **States.** idle · building (progress) · ready (download link) · failed.
- **Actions.** Build export · download · import settings with a diff preview and confirmation · generate diagnostics.
- **Endpoints.** **NEW:** `GET /api/queue/export` (§6.21), `POST /api/chats/{id}/export-history` (§6.14), `GET/POST /api/settings/export|import` (§6.24), `GET /api/diagnostics/bundle` (§6.25).

### 5 — Logs explorer

- **Purpose.** Make the log a navigable investigation surface rather than a wall of text.
- **Contents.** File picker with size and date; level filter; full-text query with server-side search across the whole file; results as a virtualized, structured list (timestamp · level · source · message) rather than one giant `<pre>`; paging over search results using the `skip`/`limit` the server already accepts (`Program.cs:306`); live-follow toggle with offset-resumed reconnect using the `fromOffset` the server already accepts (`Program.cs:319`); context expansion (±N lines) around any hit; copy-line and copy-permalink; download the raw file.
- **States.** loading file list · no files · tailing (live) · paused · searching · search-empty · search-truncated (`hasMore`, `app.js:1138` — with a real "load more" instead of today's text) · stream reconnecting · file rotated mid-follow · file unreadable (`Program.cs:300`).
- **Actions.** Pick file · filter level · search · page · follow/unfollow · expand context · copy · download · jump here from an error elsewhere in the app.
- **Fixes.** Remove `aria-live` from the log region and replace it with a discrete status announcement (audit §4.9); render incrementally instead of re-serialising 2,000 lines per SSE message (audit §4.13).
- **Endpoints.** `GET /api/logs/files` (`Program.cs:291`) · `GET /api/logs/tail` (`Program.cs:293`) · `GET /api/logs/search` (`Program.cs:306`) · `GET /api/logs/stream` (`Program.cs:319`) · **NEW:** `GET /api/logs/download` (§6.26), `GET /api/logs/context` (§6.27).

### 6 — Settings

One screen, nested sections, each independently saveable with dirty tracking and a section-level Save/Discard — replacing today's per-card save buttons and `alert('Saved.')` (`app.js:1018`).

| Section | Contents | Endpoints |
|---|---|---|
| **6a General** | Telegram credentials (write-only, with verify + rotate), account info and sign-out, language, timezone, date/number format | `GET /api/settings` (`Program.cs:157`), `POST /api/settings/credentials` (`Program.cs:171`), `POST /api/login/logout` (`Program.cs:150`), **NEW** §6.4 |
| **6b Storage** | Download folder with writability/free-space check, folder layout with tree previews, disk usage, retention policy for the download list | `POST /api/settings/download-folder` (`Program.cs:179`), `POST /api/settings/folder-layout` (`Program.cs:185`), **NEW** §6.5, §6.8 |
| **6c Limits & safety** | Scanner burst, refill, download threads, parallel bootstrap policy — each with the server's `defaults` and `thresholds` bands rendered as a gauge, a **reset-to-defaults** button, a plain-language risk explanation, and a *review-before-save* confirmation sheet that replaces the save-then-ask-then-silently-undo pattern at `app.js:926-939` | `GET/POST /api/settings/limits` (`Program.cs:199, 228`) |
| **6d Notifications** | Per-event toggles (startup, progress, complete, error), bot token + chat ID with a Test action, and per-channel routing. `ConfigParams` already persists all of these (`ConfigParams.cs:15-16, 43-46`) but **no headless code reads them** — see §6.28 | **NEW** §6.28, §6.29 |
| **6e Appearance** | Theme, accent, density, font scale, reduced motion, sidebar default, landing view — see §5.4 | **NEW** §6.30 (UI preferences) |
| **6f Rule templates** | Create/edit/delete/duplicate per-chat rule templates and see which chats use each — see §5.5 | **NEW** §6.31 |
| **6g Integrations** | yt-dlp and ffmpeg presence and version, plugin inventory and health, torrent settings | **NEW** §6.32 |

**States** per section: loading · clean · dirty · validating · saving · saved · failed (field-level) · read-only (when a value is fixed by an environment variable — e.g. credentials supplied via `APP_ID`/`API_HASH`, `ConfigParams.cs:11-12`, which today silently look editable).

### 7 — Access (users, sessions, audit)

- **Purpose.** Make the dashboard credibly multi-user and auditable. Today `WebAuthService` supports exactly one username (`WebAuthService.cs:22, 208-210`) and one password, with no roles, no session list, and no audit trail.
- **Contents.** User list with roles (Owner / Operator / Viewer), invite/create, reset password, enable/disable; active sessions with device, IP, last-seen, and revoke; session-lifetime policy (`WEB_SESSION_DAYS`, `WebAuthService.cs:60`); an audit log of security-relevant and destructive actions (sign-ins, credential changes, bulk deletes, limit changes); and a security posture panel (default password in use, `WEB_AUTH_ENABLED=false`, `WEB_AUTH_SECURE_COOKIES` off while served over HTTP — all already knowable server-side at `WebAuthService.cs:50-51, 141`).
- **States.** single-user mode (current backend — the screen shows the one account plus an explicit "multi-user requires an upgrade" state rather than faking it) · multi-user · self-service-only (Viewer sees only their own profile).
- **Actions.** Change own password · manage users · revoke sessions · view/filter/export the audit log.
- **Endpoints.** `GET /api/web-auth/status` (`Program.cs:52`), `POST /api/web-auth/change-password` (`Program.cs:89`), `POST /api/web-auth/logout` (`Program.cs:83`) · **NEW:** `/api/users` CRUD (§6.33), `/api/sessions` (§6.34), `/api/audit` (§6.35).

### Global surfaces

- **Command palette (⌘K / Ctrl+K).** Fuzzy search across chats, settings, queue items, and actions ("pause downloads", "bootstrap Foo", "go to failed"). This is the highest-leverage accessibility and power-user feature in the design: it makes every action keyboard-reachable without tabbing through a 14-column grid.
- **Notification centre.** Non-blocking toasts plus a persistent tray of recent results, replacing all 50 `alert`/`confirm` calls (audit §4.2). Every toast carries: outcome, affected count, and — for anything destructive or bulk — an **Undo** where the operation is reversible (retry/requeue are; delete is not, so those keep a typed confirmation sheet).
- **Confirmation sheets.** For destructive and bulk operations: exact scope, exact count, what is *not* affected (e.g. "files on disk are kept"), and a typed confirmation for irreversible deletes over a threshold.
- **Global error boundary.** Any render failure shows a recoverable panel with a copyable diagnostic, never a blank screen.

---

## 4. Component inventory

Build once, reuse everywhere. Grouped by layer.

**Primitives (12).** Button (variants: primary/secondary/ghost/danger; states: idle/hover/focus-visible/busy/disabled) · IconButton · Input · NumberInput (with unit suffix) · PasswordInput (reveal, strength) · Select · MultiSelect · Switch · Checkbox (tri-state) · Radio group · Textarea · Tooltip.

**Form layer (8).** Field (label + hint + error + `aria-describedby` wiring) · Form (dirty tracking, validation orchestration, navigation guard) · ValidationSummary · TokenPaletteInput (folder templates) · RegexListEditor (add/remove/reorder, live compile, test panel) · EmojiPicker (fed by a chat's available reactions) · SizeInput (bytes/MB/GB) · DateRangePicker.

**Data display (11).** DataTable (virtualized; sort, multi-select, column show/hide/reorder/resize, density, sticky header, row actions, server-side paging, persisted per-view state) · ColumnManager · FilterBar + FilterChip · FacetList (counts per reason/cause) · Pagination · EmptyState (illustration + explanation + primary action) · SkeletonRow/SkeletonCard · StatusPill (icon + text + colour — never colour alone) · ProgressBar (determinate/indeterminate/stalled) · MetricCard (value, delta, sparkline, drill-through) · Chart primitives (line, bar, donut, sparkline).

**Feedback (7).** Toast + ToastRegion · NotificationCentre · ConfirmationSheet (scope/count/typed-confirm) · InlineBanner (info/warn/danger/success) · ErrorPanel (with copyable diagnostic + retry) · StaleDataBadge ("as of 14 s ago") · BulkProgress (per-item results).

**Layout & navigation (10).** AppShell · Sidebar (collapsible, persisted) · BottomTabBar (mobile) · SystemStateStrip · Drawer/Sheet (focus-trapped, Escape-closable, responsive) · Modal (focus-trapped) · Tabs (correct `role="tablist"`/`tab`/`tabpanel`/`aria-selected`) · Accordion · WidgetGrid (add/remove/resize/reorder, keyboard-operable) · CommandPalette.

**Domain components (12).** ChatRow · ChatRuleEditor · MediaTypeChips · ProviderChips · FolderTemplateEditor · FilterBuilder · QueueItemRow · QueueItemDetail · DownloadProgressRow (speed/ETA/stall) · BootstrapJobCard · LogLine + LogViewer (virtualized) · RateLimitGauge (value against server `defaults`/`thresholds`).

**Cross-cutting services (7).** API client (typed, error-envelope-aware, 401 → re-auth, retry with backoff) · EventStream client (SSE with resume, heartbeat, degradation to polling) · PreferenceStore (server + localStorage write-through) · ThemeProvider (tokens, dark mode, custom accent) · I18nProvider · KeyboardShortcutRegistry · Telemetry/diagnostics hook.

**Acceptance criteria applied to every component:** visible `:focus-visible` ring; full keyboard operation; correct role/name/state for assistive tech; AA contrast in both themes; a defined loading/empty/error state where data-bearing; and a rendering at 375 px.

---

## 5. The customization layer

This is the explicit product goal, so it is specified as a first-class subsystem rather than a set of preferences scattered across screens.

**Persistence model.** Two tiers, one API.

- **Server-side (source of truth)** — `GET/PUT /api/ui/preferences` (**NEW BACKEND WORK**, §6.30). A single opaque JSON document per user, versioned with a `schemaVersion` and stored at `DATA_DIR/ui-preferences/{username}.json`. Rationale: this is a self-hosted appliance that owners reach from a desktop *and* a phone; layout and saved views that live only in one browser's `localStorage` are effectively lost work. The backend never interprets the document, so the UI can evolve it without backend releases.
- **Device-local (`localStorage`)** — write-through cache for instant first paint, plus genuinely device-scoped values: collapsed-sidebar state, last active view, log-follow state, and the pre-auth theme (so the login screen honours dark mode before any API call). Key namespace: `tad.ui.v1.*`.
- **Conflict rule.** Last-write-wins per top-level key with an `updatedAt` stamp; on divergence the server wins and a toast offers "keep this device's layout" (which re-uploads).

| # | Customization | What the user controls | Persisted where | Backend change required |
|---|---|---|---|---|
| **5.1** | **Dashboard layout & widgets** | Add/remove/resize/reorder widgets; multiple named layouts (e.g. "Monitoring", "Troubleshooting"); per-widget config (chat scope, date range, metric, thresholds); layout presets; reset to default; per-breakpoint layouts (desktop/tablet/mobile) | Server (`layouts`), mirrored to `localStorage` | `/api/ui/preferences` (§6.30). Widget *data* needs §6.1, §6.3, §6.8, §6.9 |
| **5.2** | **Saved views & filters** | Name and save any filter+sort+column combination on Chats, Downloads, and Logs; set one as the default landing view per screen; pin views into the sidebar; duplicate; share via URL (filters are URL-encoded, so a view is also a link) | Server (`savedViews`); the URL carries the ephemeral state | `/api/ui/preferences` (§6.30). Server-side filtering for Downloads needs §6.19 |
| **5.3** | **Table columns** | Show/hide, reorder (drag or keyboard), resize, sort (multi-column), density (compact/normal/comfortable), text wrap vs. truncate, frozen first column, per-table row-height, and default page size | Server (`tables.{tableId}`), mirrored locally | None beyond §6.30 — but server-side sort/page for Downloads needs §6.19 |
| **5.4** | **Theming** | Light / dark / follow-system; custom accent colour (with an automatic AA-contrast check that refuses or adjusts unreadable pairs); font scale (90–130%); density; reduced motion; high-contrast mode; optional custom logo and dashboard title for white-labelling | Server (`appearance`) + `localStorage` (pre-auth paint) | None beyond §6.30. Implementation = CSS custom-property token set; today `styles.css` has 60+ hardcoded hex literals and no token layer, so this is a prerequisite refactor |
| **5.5** | **Per-chat rule templates** | Create a named template from any chat's rules (media types, min size, providers, filters, folder templates, reactions, history); edit, duplicate, delete; apply to a multi-selection in bulk; see which chats use a template; re-apply after editing ("update 23 chats using this template"); export/import templates | Server — templates are *domain* data, not UI preference, so they belong in `ConfigParams`, not the preference blob | `/api/chat-templates` CRUD (§6.31) **and** `PATCH /api/chats/bulk` (§6.10). **This pair is the single highest-value new backend work in the project** — it is what turns a 400-chat configuration job from hours into one action |
| **5.6** | **Notification preferences** | Which events notify (download complete, failure, flood-wait, bootstrap finished, disk low, stale chat); per-channel routing (in-app toast, browser Web Notification, Telegram bot); per-event severity; quiet window; digest vs. immediate; per-chat overrides | In-app + browser: server (`notifications`). Telegram bot events: `ConfigParams.NotifyOn*` (`ConfigParams.cs:43-46`) | Browser/in-app: §6.30. Telegram bot: §6.28 — the config fields already exist and persist but **no headless code reads them**; `Notification.cs` lives in the WPF project (`TelegramAutoDownload/Notification.cs:38, 72, 129, 155`) and must be moved to a shared library and wired into `DownloadOrchestrator` |
| **5.7** | **Keyboard shortcuts** | A registry of named commands with defaults (⌘K palette, `g c`/`g d`/`g l` navigation, `/` focus search, `p` pause/resume, `r` retry selected, `?` shortcut sheet); user-remappable; conflict detection; a printable/searchable cheat sheet; fully disableable | Server (`shortcuts`) | None beyond §6.30 |
| **5.8** | **Internationalization readiness** | Language selection; locale-aware dates, numbers, bytes, and relative times via `Intl`; RTL layout support; per-user timezone | Server (`locale`) + `<html lang>`/`dir` | Client-side: extract all UI strings into JSON catalogues (today every string is inline in `index.html`/`app.js`). **Server-side: yes** — the API returns user-facing English prose as `error` strings (e.g. `Program.cs:544`, `Program.cs:424`, `WebAuthService.cs:104-119`) and those are rendered verbatim. Untranslatable as designed. Requires the error-envelope change in §6.36: `{ code, message, params }` so the client can localise by `code` and fall back to `message` |

**Guardrails on customization.** Customization must not let a user hide the information that keeps them safe. Three rules: (a) the system-state strip and flood-wait banner cannot be removed; (b) the "Needs attention" widget can be moved but not deleted while it has contents; (c) a custom accent that fails AA against both surface colours is rejected with an explanation, not silently accepted.

---

## 6. NEW BACKEND WORK register

Contracts for every endpoint the target design needs and that does not exist today. Nothing here assumes new engine capability except where noted; most are thin exposures of code already present in `TelegramClient` or aggregations over the existing `Media` table.

**§6.1 — `GET /api/events` (SSE).** Replaces the 2.5 s poll of 9 endpoints. `text/event-stream`, named events: `status`, `download`, `queue-stats`, `bootstrap-job`, `flood-wait`, `chat-updated`, `heartbeat` (every 15 s). Supports `Last-Event-ID` for resume. Payloads reuse the existing DTO shapes from `Program.cs:504-533`. Precedent: `/api/logs/stream` (`Program.cs:319`).

**§6.2 — speed/ETA/stall on `GET /api/downloads`.** Add `bytesPerSecond`, `etaSeconds`, `elapsedSeconds`, `stalled` to the payload at `Program.cs:504-517`. Requires `DownloadEntry` (`HeadlessHost.cs:12-22`) to retain a short byte-rate window. The desktop already computes these (`Services/DownloadProgressService.cs:179, 455`) but that class is in the WPF project, which Headless does not reference (`TelegramAutoDownload.Headless.csproj:28-36`) — so move it to a shared library rather than reimplementing.

**§6.3 — `GET /api/stats/timeseries?from&to&bucket&groupBy`.** Returns `[{ bucket, files, bytes, byStatus, byKind, byChat }]`. Pure SQL over `Media` using the timestamps already written at `MediaRepository.cs:66-70`.

**§6.4 — `POST /api/settings/verify-credentials`.** Body `{ appId, apiHash }`; returns `{ ok, error? }` without persisting. Lets the wizard fail fast instead of at login.

**§6.5 — `POST /api/settings/verify-folder`.** Body `{ folder }`; returns `{ exists, writable, freeBytes, totalBytes, error? }`.

**§6.6 — `GET /api/setup/state`.** Returns `{ credentialsConfigured, telegramLoggedIn, folderConfigured, chatsSelected, limitsReviewed, complete }` so the wizard is resumable and idempotent.

**§6.7 — `POST /api/login/resend-code`.** Returns `{ stage, error? }`. Today a mistyped or expired code has no recovery path short of a reload.

**§6.8 — `GET /api/system/storage`.** Returns `{ driveName, driveTotalBytes, driveFreeBytes, folderBytes, folderFileCount, scanComplete }`. The desktop implementation exists at `TelegramAutoDownload/Services/DiskSpaceService.cs` — move it to a shared library.

**§6.9 — `GET /api/queue/reasons?status&chatId`.** Returns `[{ code, label, count }]` — faceted skip/failure causes. Requires the writer to persist a **machine-readable reason code** alongside the human string currently stored in `last_error` (`MediaRepository.cs:82`). This is the single most important backend change for audit §4.12; without a code, the UI can only show raw exception text.

**§6.10 — `PATCH /api/chats/bulk`.** Body `{ chatIds: long[], patch: ChatPatch, templateId?: string }`; returns per-chat results `[{ chatId, ok, error? }]`. Reuses the existing `ChatPatch` mutation path (`Program.cs:261-283`).

**§6.11 — `POST /api/chats/{id}/mute`.** Body `{ muted }`. Thin passthrough to `TelegramApp.MuteChatAsync` (`TelegramApp.cs:1594`), which already exists. Mute state is already returned to the browser (`Program.cs:629, 640`).

**§6.12 — extend `ChatPatch`** (`Program.cs:607-614`) with `ReactionIcon`, `DownloadStartIcon`, `HistoryIcon`, `SocialDownloadFolderTemplate`, `YoutubeDownloadFolderTemplate`, `OtherDownloadFolderTemplate`, `TorrentDownloadFolderTemplate`, and expose them on `ChatDtoView` (`Program.cs:616-641`). All eight fields already exist on `ChatDto`, are already persisted by `HeadlessHost.RefreshChatsAsync` (`HeadlessHost.cs:170-188`), and the reaction fields are **already acted on by the running engine** (`TelegramApp.cs:373-375, 414-420`). This is an afternoon of work for eight user-visible features.

**§6.13 — `GET /api/chats/{id}/reactions`.** Passthrough to `TelegramApp.GetChatAvailableReactionsAsync` (`TelegramApp.cs:1489`).

**§6.14 — `POST /api/chats/{id}/export-history`.** Starts a job; returns `{ jobId }`, with progress on `/api/events`. Wraps `TelegramApp.ExportChatHistoryAsync` (`TelegramApp.cs:1089`).

**§6.15 / §6.16 — `POST /api/validate/regex`, `POST /api/validate/folder-template`.** Return `{ valid, error?, sample? }`. Server-side validation matters because the server is what compiles the patterns and resolves the tokens; client-side `RegExp` differs from .NET regex semantics.

**§6.17 — `GET /api/chats/{id}/messages?offsetId&limit&kind`.** Wraps `TelegramApp.FetchBrowseHistoryPageAsync` (`TelegramApp.cs:824`); annotate each message with its `Media` row status.

**§6.18 — `POST /api/chats/{id}/download-messages`.** Body `{ messageIds: int[] }`. Wraps `ManualDownloadMessagesAsync` (`TelegramApp.cs:855`).

**§6.19 — paged/filtered `GET /api/queue/items`.** Extend `Program.cs:397` with `status[]`, `chatId[]`, `kind`, `q`, `minSize`, `maxSize`, `from`, `to`, `sort`, `skip`, `limit`, and return `{ items, total, hasMore }`. Today the endpoint takes a single status and an undisclosed 200-row cap (`Program.cs:400`), which is why the UI silently truncates.

**§6.20 — `POST /api/queue/items/bulk`.** Body `{ action: 'retry'|'delete', selector: { ids? | filter? } }`; returns `{ affected, results[] }`.

**§6.21 — `GET /api/queue/export`.** Same filters as §6.19; streams CSV/JSON.

**§6.22 — `DELETE /api/downloads/{chatId}/{messageId}`.** Cancels an in-flight download and requeues or fails the row. Requires cancellation plumbing in `DownloadOrchestrator` — this is genuine new engine work, not a passthrough.

**§6.23 — `GET /api/stats/summary`.** All-time and session totals plus a `since` date (desktop equivalent: `Services/StatisticsService.cs:25-27`).

**§6.24 — `GET /api/settings/export` and `POST /api/settings/import`.** Redact secrets on export; diff-preview then apply on import. Desktop equivalent: `SettingsWindow.xaml:231-233`.

**§6.25 — `GET /api/diagnostics/bundle`.** Zip of redacted config, recent logs, queue stats, and version info.

**§6.26 / §6.27 — `GET /api/logs/download`, `GET /api/logs/context?file&line&before&after`.** File download and ±N line context for a search hit.

**§6.28 — `GET/POST /api/settings/notifications`.** Exposes `NotifyOnStartup/Progress/Complete/Error`, `BotToken`, `ChatId` (`ConfigParams.cs:15-16, 43-46`). **Also requires wiring**: `Notification.cs` is in the WPF project (`TelegramAutoDownload/Notification.cs`) and zero headless code reads those flags, so the headless service currently persists notification preferences it never honours.

**§6.29 — `POST /api/settings/notifications/test`.** Sends a test message; returns delivery result. Desktop equivalent: `SettingsWindow.xaml:104`.

**§6.30 — `GET/PUT /api/ui/preferences`.** Opaque per-user JSON with `schemaVersion` and `updatedAt`; stored at `DATA_DIR/ui-preferences/{username}.json`. Size-capped (e.g. 256 KB) and never interpreted server-side.

**§6.31 — `/api/chat-templates` CRUD.** `GET` list, `POST` create, `PUT /{id}`, `DELETE /{id}`, plus `POST /{id}/apply` with `{ chatIds }`. Template shape = the subset of `ChatDto` that constitutes a rule. Stored in `ConfigParams`.

**§6.32 — `GET /api/system/tools`.** yt-dlp and ffmpeg presence/version, plugin inventory and load status. Prevents the current failure mode where a provider is enabled but its tool is missing and the user only learns from a failed download.

**§6.33 / §6.34 / §6.35 — `/api/users`, `/api/sessions`, `/api/audit`.** Multi-user accounts with roles, session listing and revocation, and an append-only audit log. This is the largest new backend item: `WebAuthService` is structurally single-user (`WebAuthService.cs:22, 208-210`) and its session cookie carries only `username|expires|signature` (`WebAuthService.cs:132-136`) with no server-side session registry, so revocation is impossible today.

**§6.36 — structured error envelope.** Standardise every error response on `{ code, message, params?, details? }`. Today errors are ad-hoc English prose (`Program.cs:174, 424, 544`; `WebAuthService.cs:104-119`) rendered verbatim by the client (`app.js:146`), which blocks both i18n (§5.8) and the error taxonomy in screen 3a. Introduce alongside the existing `error` field for one release, then drop the bare string.

---

## 7. Frontend technology recommendation

### 7.1 Trade-off analysis

| Option | Ecosystem for the *customization* goal | Toolchain cost | Team fit (.NET-centric) | Incremental migration | Payload / cold start | Verdict |
|---|---|---|---|---|---|---|
| **Stay vanilla, refactor into ES modules** | None. Virtualized tables with reorderable columns, widget drag-grids, and a saved-view system are all hand-written. You end up authoring a private framework with no tests, no docs, and one expert. | Zero — keeps today's "copy files to `wwwroot`" model | Highest short-term comfort; lowest long-term leverage | Trivially incremental | Smallest (~40 KB) | **Rejected.** The product goal is the one thing vanilla is worst at. |
| **Alpine.js** | Sprinkle-scale reactivity. No data-grid, no virtualization, no widget grid. | Near zero (CDN script) | Easy to read | Very incremental | ~15 KB | **Rejected.** Ceiling is far below a configurable data-dense dashboard. |
| **Svelte** | Good DX, small output, but a thinner headless-component and data-grid ecosystem than React's; TanStack Table has a Svelte adapter, drag-and-drop dashboards are mostly DIY. | Vite build | Unfamiliar syntax and compiler semantics | Incremental (compiles to custom elements) | ~10 KB runtime | **Close second.** Loses on ecosystem depth for exactly the features that define the goal. |
| **React (full SPA)** | Best in class: TanStack Table/Query/Virtual, dnd-kit, Radix, react-i18next — every customization requirement has a mature, headless, accessible library. | Vite build; largest dependency tree | Largest learning curve; heaviest churn culture | Incremental if mounted per-tab | ~45 KB gzip | Strong, but heavier than needed. |
| **Preact + `preact/compat`** | **Same ecosystem as React** — TanStack, dnd-kit, and Radix-family primitives work through the compat alias — at a fraction of the runtime. | Vite build (same as React) | Same curve as React, smaller surface area | **Excellent**: multiple independent roots can be mounted into the existing `index.html`, one tab at a time | ~4 KB core, ~12 KB with compat | **Recommended.** |
| **Blazor WebAssembly** | MudBlazor/Radzen give a competent data grid and theming. But drag-resize widget dashboards, virtualized tables with user-reorderable columns, and i18n tooling are materially thinner, and every browser API this design needs — `localStorage`, `EventSource`, Web Notifications, `Intl`, the clipboard — goes through JS interop. | **Does not avoid a build step** — `dotnet publish` with trimming is slower than Vite, and hot reload is weaker | **Best fit on paper**: C#, shared DTOs with `Program.cs`, one language | Possible (multiple root components mounted by CSS selector) | **1.5–2.5 MB** first load, ~1–3 s cold start | **Rejected — see below.** |
| **Blazor Server** | Same component story | Smallest client payload | Best C# fit | Incremental | Tiny initial | **Rejected.** Requires a persistent SignalR circuit per client; a container that restarts or a phone that sleeps drops the circuit and the UI dies. Wrong architecture for a self-hosted appliance on a flaky LAN. |

### 7.2 Recommendation

**Adopt Preact + TypeScript + Vite, with `preact/compat` enabled, TanStack Table + TanStack Query for data, dnd-kit for the widget grid and column reordering, and a Tailwind CSS token layer driving the theme.** Component library: **build a thin in-house component layer on headless primitives** (Ark UI, or Radix via `preact/compat`) rather than adopting an opinionated kit — because the design calls for custom accent colours, density modes, and white-labelling, and opinionated kits fight all three. Ship it as a design-token set in CSS custom properties so theming (§5.4) is a variable swap, not a re-style.

**The single strongest reason:** the customization layer *is* the product goal, and roughly 80% of it — virtualized tables with show/hide/reorder/resize/sort/density, saved views, a drag-resizable widget dashboard, and accessible focus-managed overlays — is off-the-shelf, battle-tested, and accessible in the React ecosystem, while being months of bespoke work in vanilla JS, Alpine, or Blazor. Preact buys that entire ecosystem for a ~12 KB runtime and, crucially, mounts into the existing `index.html` one tab at a time, so the product is never broken mid-migration.

**Honest case against, and why it loses.** Blazor WASM is the better *team* fit and that is a real cost — the .NET team will be slower in TypeScript for the first month, and shared DTOs between `Program.cs` and the client is a genuine advantage Preact cannot match (mitigate it by generating TS types from the minimal-API DTOs). But three facts decide it: (1) Blazor does not avoid a build step, so the "no toolchain" advantage that motivated staying with vanilla evaporates under either choice; (2) a 1.5–2.5 MB WASM download with a 1–3 s cold start is a poor fit for a dashboard people will open on a phone over a home connection, against ~12 KB + code-split routes; (3) every one of the seven customization requirements in §5 maps to a mature, accessible library in the React ecosystem and to bespoke work in Blazor — and since customization is the stated goal, that is the axis that must win.

**Also note for the release pipeline.** Adopting a build step changes the release procedure: `npm ci && npm run build` must run before `dotnet publish`, and the built `wwwroot` assets must be present in the publish output that feeds both the Portable ZIP and the Inno Setup installer. Both artifacts must continue to ship on every release.

### 7.3 Incremental migration path (never leaves the product broken)

Each phase ships independently and is individually revertable. The existing vanilla app keeps running throughout; there is no cut-over day.

- **Phase 0 — Foundations (no user-visible change).** Add `frontend/` with Vite + TypeScript + Preact; output hashed bundles into `wwwroot/assets/`; keep `index.html` as the shell and add a single `<script type="module">` entry. Extract the design tokens from the 60+ hardcoded hex literals in `styles.css` into CSS custom properties, keeping the current light palette byte-identical. Build the typed API client and the SSE client. Wire `npm ci && npm run build` into the publish pipeline. **Exit criterion: the app looks and behaves exactly as before.**
- **Phase 1 — App shell around the old content.** Render the new shell (sidebar, system-state strip, toast region, notification centre, command palette, theme provider, router) in Preact, and mount the five *existing* vanilla tab sections inside it unchanged as escape-hatch children. Routing and deep links start working immediately. Replace all 50 `alert`/`confirm` calls with the toast and confirmation-sheet components — a pure win with no rewrite. **Exit criterion: old tabs still work; feedback and navigation are new.**
- **Phase 2 — Downloads (queue manager + live activity).** Rewrite the highest-pain tab first, because it is where the DataTable, FilterBar, FacetList, and BulkProgress components get built and proven. Land §6.1 (events), §6.9 (reason codes), §6.19 (paged items), and §6.2 (speed/ETA) alongside. Delete the old Queue section only once the new one is behind a preference-controlled toggle and has been used for a release.
- **Phase 3 — Chats (list + detail drawer + discovery + bulk).** Reuses the Phase 2 DataTable. Lands §6.10 (bulk), §6.12 (extended patch), §6.11 (mute), §6.13 (reactions), §6.31 (templates). This is where the Category B free wins from the audit all land.
- **Phase 4 — Overview + widget grid + customization layer.** Requires Phases 2–3 for the widgets to have something to show. Lands §6.30 (preferences), §6.3/§6.8 (stats, storage).
- **Phase 5 — Logs explorer, Statistics, Settings, Access.** Migrate the remaining tabs; delete the last of `app.js`. Lands §6.26/§6.27, §6.23, §6.28/§6.29, §6.33–§6.35.
- **Phase 6 — i18n and polish.** Extract string catalogues, adopt the §6.36 error envelope, add RTL support, and complete the accessibility audit against WCAG 2.1 AA.

**Rollback posture.** Until Phase 5, `index.html` can serve either the vanilla section or its Preact replacement for each tab, gated by a preference flag. Any phase can be reverted by flipping that flag and shipping.

---

## 8. Prioritized implementation backlog

Effort in engineer-days (design + build + test for one engineer). **QW** = quick win, mostly or entirely surfacing capability the backend already has.

### Tier 1 — Quick wins (free features + the worst defects)

| # | Item | Effort | Depends on | Why now |
|---|---|---|---|---|
| 1 | **QW** Replace all 50 `alert`/`confirm` with toasts + confirmation sheets; add a global error boundary | 3 | — | Fixes audit §4.2; unblocks every later error-handling improvement |
| 2 | **QW** Surface errors from all 6 swallowed `catch` blocks as inline banners with retry and a stale-data badge | 2 | 1 | Fixes audit §4.1 — the single worst defect: silent failure |
| 3 | **QW** Expose the 8 already-persisted `ChatDto` fields (3 reaction icons + 4 per-provider folder templates) via `ChatPatch`/`ChatDtoView` + UI; add `/api/chats/{id}/reactions` | 2 | — | 8 user-visible features; the engine already honours the reaction fields (`TelegramApp.cs:414-420`) |
| 4 | **QW** Wire the 3 orphaned endpoints: `/api/chats/{id}/sync`, `GET /api/chats/{id}/bootstrap`, `GET /api/queue/downloads` → global pause banner + per-chat progress + monitored-only sync | 1.5 | — | Zero backend work; fixes the "paused but four tabs look normal" confusion |
| 5 | **QW** Per-chat mute toggle (`POST /api/chats/{id}/mute` → existing `MuteChatAsync`); render the `muted` field already on the wire | 0.5 | — | Passthrough to `TelegramApp.cs:1594` |
| 6 | Roll back failed optimistic edits; clear the accumulating `patch` closure; add per-field save indicators | 1.5 | 1 | Fixes audit §4.3 — currently shows settings that were never saved |
| 7 | Debounce chat search (250 ms) and stop re-fetching queue stats per keystroke; pause polling when the tab is hidden; add error backoff | 1 | — | Fixes audit §4.5 and the ~3.6 req/s idle load |
| 8 | Loading skeletons + empty states + zero-filter states for every table, incl. the missing Chats empty state | 2 | 1 | Fixes audit §4.6 |
| 9 | **QW** Rate-limit "reset to defaults" using the server's existing `defaults` block; delete the duplicated client thresholds; replace the save-then-ask-then-undo flow with a review sheet | 1 | 1 | Fixes audit §4.8's worst case |
| 10 | Accessibility pass on existing markup: `:focus-visible` rings, proper tab roles, real labels on all 8 per-row checkboxes, `role="alert"` on every error region, focus trap + Escape in the password modal, remove `aria-live` from the log region, icon+text status pills, fix the 2 contrast failures | 4 | — | Fixes audit §4.9 and §4.10; required for any enterprise or public-sector sale |
| 11 | Responsive layout: breakpoints, table overflow containers, mobile bottom nav, card fallback for the widest tables | 3 | 10 | Fixes audit §4.11 — the dashboard is unusable below ~1100 px today |
| 12 | Client-side validation everywhere (App ID, API Hash, phone, min-MB, passwords) + server-side regex and folder-template validation endpoints | 2.5 | — | Fixes audit §4.7 |

**Tier 1 total: 24 engineer-days.** Items 3, 4, 5, and 9 (5 days) deliver 14 user-visible capabilities from code that already exists.

### Tier 2 — Structural foundations

| # | Item | Effort | Depends on |
|---|---|---|---|
| 13 | Phase 0: Vite + TypeScript + Preact scaffold, design tokens extracted from `styles.css`, typed API client, publish-pipeline integration | 5 | — |
| 14 | `GET /api/events` SSE hub; replace the 9-endpoint poll; degradation-to-polling fallback | 4 | 13 |
| 15 | Machine-readable skip/failure reason codes + `GET /api/queue/reasons` faceting | 3 | — |
| 16 | Paged/filtered/sorted `GET /api/queue/items` + `POST /api/queue/items/bulk` + CSV export | 4 | 15 |
| 17 | Phase 1: app shell (sidebar, state strip, router, command palette, theme provider) wrapping the existing vanilla tabs | 6 | 13 |
| 18 | DataTable component (virtualized, sortable, multi-select, configurable columns, density) | 6 | 13 |
| 19 | Phase 2: Downloads queue manager + error taxonomy + live activity | 7 | 14, 16, 18 |
| 20 | Speed/ETA/stall detection (shared `DownloadProgressService`) | 3 | 14 |
| 21 | `PATCH /api/chats/bulk` + `/api/chat-templates` CRUD + bulk-apply UI | 5 | 18 |
| 22 | Phase 3: Chats list + detail drawer (filter builder, folder-template preview, reaction pickers) + discovery | 8 | 18, 21, item 3 |
| 23 | `GET/PUT /api/ui/preferences` + PreferenceStore with localStorage write-through | 3 | 13 |
| 24 | Saved views + configurable columns + density, persisted | 4 | 18, 23 |
| 25 | Phase 4: Overview widget dashboard + widget catalogue + drag/resize layouts | 8 | 19, 22, 23 |
| 26 | Statistics endpoints (`/api/stats/timeseries`, `/api/stats/summary`) + Insights screen | 5 | 13 |
| 27 | Storage monitoring endpoint + widget (shared `DiskSpaceService`) | 2 | 13 |
| 28 | Theming: dark mode, custom accent with AA validation, font scale, reduced motion | 4 | 13, 23 |
| 29 | First-run wizard + `/api/setup/state` + credential/folder verification | 5 | 17 |
| 30 | Logs explorer rewrite (virtualized, paged search, offset-resumed follow, context expansion, download) | 5 | 17 |
| 31 | Notification preferences: move `Notification.cs` to a shared library, wire into `DownloadOrchestrator`, add endpoints + test action | 4 | — |
| 32 | Keyboard shortcut registry + remapping + cheat sheet | 3 | 17 |
| 33 | Message browser + manual download (§6.17, §6.18) | 4 | 18 |
| 34 | Cancel in-flight download (orchestrator cancellation plumbing) | 3 | 14 |
| 35 | Settings screen rewrite (7 sections, dirty tracking, read-only env-var states) | 6 | 17 |
| 36 | Reports & export + diagnostic bundle + settings import/export | 4 | 16 |
| 37 | Multi-user accounts, roles, session registry + revocation, audit log | 10 | 17 |
| 38 | Structured error envelope (`{ code, message, params }`) across all 42 endpoints | 3 | — |
| 39 | i18n: string extraction, catalogues, `Intl` formatting, RTL | 6 | 38 |
| 40 | Phase 5–6 cleanup: delete `app.js`, remove vanilla fallbacks, full WCAG 2.1 AA audit | 5 | all |

**Tier 2 total: ≈130 engineer-days.** Grand total ≈154 engineer-days (~31 weeks for one engineer; ~11 weeks for a team of three with the dependency graph respected).

### Critical path

`13 → 17 → 18 → 19 → 22 → 25` is the spine. Items 14, 15, 16, and 21 are the backend work that gates it and should start in parallel with 13. Items 1–12 are independent of the migration and should ship first, on the current vanilla codebase, because they are the difference between "unusable" and "usable" and they are not invalidated by the rewrite.

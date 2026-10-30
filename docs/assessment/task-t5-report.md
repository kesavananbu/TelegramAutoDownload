# Task T5 — Web dashboard UI + backend free wins

**Track:** T5 · **Branch (intended):** `er/t5-dashboard-ui` · **Date:** 2026-10-30  
**Controller note:** No git commits by agent (parallel checkout); controller commits by path.

## Files created or modified (T5 ownership)

| Path | Change |
|------|--------|
| `TelegramAutoDownload.Headless/wwwroot/index.html` | Shell: tab ARIA, global banners, toast/confirm, table scroll, appearance card, chat columns (mute/reactions), status metrics |
| `TelegramAutoDownload.Headless/wwwroot/app-ui.js` | **New** — toast, confirm dialog (focus trap), loader health, prefs/theme, poll scheduler, global pause fetch |
| `TelegramAutoDownload.Headless/wwwroot/app.js` | Toasts/confirms (0 alert/confirm), PATCH rollback, debounced search, hash tabs, polling guards, free-win wiring, chat features UI |
| `TelegramAutoDownload.Headless/wwwroot/styles.css` | CSS tokens, dark/system theme hooks, focus-visible, responsive, toasts, skeletons |
| `TelegramAutoDownload.Headless/Endpoints/ChatFeatureEndpoints.cs` | **New** — feature/mute/reactions endpoints |
| `TelegramAutoDownload.Headless/Program.cs` | See hunk below |
| `docs/assessment/task-t5-report.md` | This report (uncommitted per controller) |

**Not touched:** login modal, change-password modal/JS, web-auth endpoints, `TelegramApp.cs`, README, roadmap, `data/`.

## Program.cs hunks (re-read before merge)

1. `using TelegramAutoDownload.Headless.Endpoints;` (with other usings).
2. **Single map line** before `await app.RunAsync();`: `app.MapChatFeatureEndpoints();`
3. **`ChatDtoView` extended** (list API): `ReactionIcon`, `DownloadStartIcon`, `HistoryIcon`, four `*DownloadFolderTemplate` fields + `From()` mappings — required for chat table; coordinate with T1/T2 if they edit the same record.

## New HTTP contracts (`ChatFeatureEndpoints.cs`)

- `PATCH /api/chats/{id}/features` — body (camelCase): optional `reactionIcon`, `downloadStartIcon`, `historyIcon`, `socialDownloadFolderTemplate`, `youtubeDownloadFolderTemplate`, `otherDownloadFolderTemplate`, `torrentDownloadFolderTemplate`. Folder writes go through `ValidateFolderTemplateWrite` (TODO: wire `PathPolicy`).
- `POST /api/chats/{id}/mute` — `{ "muted": bool }` → Telegram `MuteChatAsync` + persist.
- `GET /api/chats/{id}/reactions` → `{ "reactions": string[] }`.

## Verification

| Check | Result |
|-------|--------|
| `node --check` on `app.js`, `app-ui.js` | Pass |
| `dotnet build TelegramAutoDownload.Headless --artifacts-path D:\tad-artifacts\t5` | Pass |
| `dotnet test TelegramAutoDownload.Headless.Tests --artifacts-path D:\tad-artifacts\t5` | Blocked: `testhost` file lock from parallel agent run; one unrelated `TelegramApiBudgetTests` failure observed mid-run |
| Browser manual | **Not run** (no isolated server this session) |

## Delivered vs spec

- Toasts + accessible confirm; all loader paths use `markLoaderOk/Fail` + banner + freshness badge.
- Optimistic chat PATCH rollback; per-change body (no accumulator).
- Poll: hidden-tab skip, exponential backoff, skip table rebuild when focused.
- Free wins: `/sync` for monitored chats, GET bootstrap status cell, GET `/api/queue/downloads` global banner, status `downloadsPaused`/`floodWait`, download `at` staleness, server `thresholds`/`defaults`, confirm-first parallel bootstrap.
- Chat UI: mute, reactions, per-provider folders, labeled media/plugin checkboxes.
- Hash tabs, debounced search, empty/loading states, appearance prefs in `localStorage` (`tad.ui.v1`).

## Deferred backlog

- Preact/Vite migration, `/api/events` SSE, saved views/column picker UI, bulk/templates, paged queue manager, server `/api/ui/preferences`, full WCAG audit, emoji picker (load list only today).

## Edits outside strict ownership

- `Program.cs`: `ChatDtoView` expansion (not only Map line) — needed for extended chat list JSON.

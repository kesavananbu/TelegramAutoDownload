# Scorecard and Roadmap

Synthesis of all seven Wave 1 assessments. Every number here was measured or verified, not estimated, unless labelled otherwise.

## Product decision (2026-10-01)

**The commercial goal is dropped.** This stays a free personal tool, and the objective is to make it excellent and secure.

That decision removes whole workstreams rather than merely deprioritizing them: licence-key enforcement, billing, entitlement gating, marketplace packaging, hosted SaaS, and multi-tenancy are all out. It also defuses most of the compliance findings — as a personal tool the operator is their own data controller, and MIT becomes an appropriate licence rather than a self-defeating one. `docs/compliance/`, `docs/gtm/`, and `docs/product/` are retained as a record of why, but they no longer drive work.

What survives from the legal review, on much reduced priority:

- Add the missing `LICENSE` file so the README's MIT claim is actually true. Drafted at `docs/compliance/drafts/LICENSE`.
- Add a `NOTICE` for the GPL-2+ ffmpeg redistributed in the Docker image, if that image is ever published. Drafted.
- Stop auto-downloading yt-dlp and ffmpeg at runtime (`YtdlpService.cs:82-89`). This was recommended for legal reasons but is independently **a security fix** — it is finding F-10, remote code execution with no integrity verification. It now ships on security grounds.
- The member-list export writes third parties' phone numbers into vCards (`TelegramApp.cs:1079`). Keep the feature, but it warrants an explicit warning in the UI.
- Telegram's API ToS §2.3 forbids "Telegram" in an app name. Enforcement risk against a free personal tool is low. Noted, not actioned.

## Scorecard

Scored 1–5 against "excellent and secure personal tool", not against enterprise SaaS.

| Dimension | Score | Evidence |
|---|---|---|
| Functional correctness | 2 | Of 50 documented claims, 19 work, 18 partial, 8 false. Silent data-corruption paths exist in `.part` promotion and chunk retry. |
| Safety of normal operation | 1 | Three uncoordinated concurrency governors can put ~30 simultaneous operations against one rate-limited Telegram account; the live path cannot see the flood tracker at all. |
| Security | 2 | 26 findings: 3 critical, 5 high. 13 reachable in a default deployment. Shipped default credentials with a pre-auth oracle confirming they work. |
| Test coverage | 1 | **10.62% measured** production line coverage (636/5,987). The headless server — the primary surface — is at **0%** and is not referenced by the test project at all. |
| Architecture | 2 | ~40 endpoints inline in `Program.cs`, no layering, four interfaces solution-wide, two divergent dedup authorities. |
| UI / usability | 2 | 50 blocking `alert`/`confirm` dialogs, 5 swallowed error paths, 0 `@media` rules, 0 `:focus` rules, 6 ARIA attributes total. Failed edits are never rolled back. |
| Reliability / operability | 2 | No metrics, no health endpoints, no backup or restore for the SQLite database or the Telegram session. |
| Delivery engineering | 1 | No CI existed at baseline. Restore depends on machine-level NuGet feeds because no `NuGet.config` is committed. |
| Documentation honesty | 1 | Eight README claims are fiction, including an entire Quiet Hours feature documented in four places and implemented nowhere. |

Reaching 4 across the board is roughly 12–16 engineer-weeks, down from the 35 estimated under the commercial scope.

## Corrections to earlier assumptions

Several of the leads in the original brief were wrong, and the agents said so rather than confirming them. Recording them so they are not re-litigated:

- The log-file path-traversal defense **holds**. `ResolveSafeFileName` is sound.
- **No secret leakage into logs** was found — phone numbers, 2FA passwords, API hashes, and bot tokens all stay out.
- SQL is **fully parameterised**; no injection.
- The CSRF lead is **materially overstated** (F-14): no defense exists, but the classic cross-site attack is blocked anyway.
- A migration runner **does exist** (`Database.cs:63-109`).
- SDK 10 against net8.0 targets is **not a problem** and caused zero build failures.
- Quiet Hours does not exist — confirmed independently by three agents.

## Execution order

Sequenced so each step makes the next verifiable. Serialized implementer → reviewer loops; no parallel implementers.

### Milestone 1 — Make change safe ✅ (done on `enterprise-readiness`)

Completed in commits `3072ef3`–`b5165b5` ([Test harness v2](38a4f447-2f6a-4def-b01a-55e61888774e)): `NuGet.config`, `RunnerApp.sln`, `TelegramAutoDownload.Headless.Tests` (22 characterization tests, **33.86%** Headless line coverage). Report: `docs/assessment/task-01-report.md`.

**Remaining from Milestone 1:** validate and commit CI workflows under `.github/workflows/` (still UNVALIDATED).

### Status 2026-10-30 — Milestones 2–5 implemented on `enterprise-readiness`

| Item | Status | Commit | Still open |
|---|---|---|---|
| 4 API budget | Done | `ac6a974` | ADR-003 gateway/AIMD; a few ad-hoc API calls still unbudgeted |
| 5 Dedup | Partial | `fdbc8a0` | Live `TelegramClient` path still dedups via `FileDownloadIndex` only |
| 6 Corruption | Done | `60f442a` | — |
| 7 Session path | Done | `fdbc8a0` | — |
| 8 Auto-update | Done (hash verify) | `922ada0` | Velopack / Authenticode; `GitHubOwner` kept as `il90il90` by decision |
| 9 Sqlite CVE | Done | `fdbc8a0` | Validate `SQLitePCLRaw` 3.x on ARM NAS targets |
| 10–12 Web auth | Done | `93c6d16` | F-04 server-side sessions, `__Host-` cookies, F-07 for config/session, F-19 log caps |
| 13 F-09/F-10 | Done | `922ada0` | `SHA256SUMS.txt` generation in release CI |
| 14 Remaining | Partial | F-13, F-15, F-17, F-18, F-22 done | F-26 unverified |
| 15–17 UI | Done (vanilla JS) | `59cef9f` + merge fixes | Column picker UI, full WCAG audit |
| 18 SSE hub | Not started | — | — |
| 19 Customization | Partial | `59cef9f` | Theme/accent/density only; saved views, rule templates, server-side prefs |
| 20–22 Docs/legal/CI | Done | `202761e` | `release-desktop.yml` not actionlint-validated; first real CI run pending |

### Milestone 2 — Stop the bleeding

Ordered by worst realistic outcome. The account-ban defect outranks the security findings because it fires during normal use with no attacker involved, and the loss is the user's entire Telegram account.

4. Unified Telegram API budget (ADR-003) — every outbound call spends from one bucket, and the live path becomes flood-aware. Persist `FloodWaitTracker` pause state across restarts (today it is in-memory only — restarting to "clear" a pause forgets the penalty and can deepen FLOOD_WAIT).
5. Single dedup authority (ADR-010) — end the permanent divergence between `downloaded_ids.json` and SQLite.
6. Data-corruption fixes — `.part` promotion size check (`BaseMessage.cs:145`), chunk-retry seek (`DownladerPlugin.cs:82-91`), atomic config writes.
7. **Session and backup path truth.** WTelegram stores the session at `{ApplicationData}/TelegramAutoDownload/session.dat` (`TelegramApp.cs:102-104`). In Docker, with `XDG_CONFIG_HOME=/data`, that is typically **`/data/TelegramAutoDownload/session.dat`**, not `HeadlessPaths.SessionFile` (`/data/session.dat`). Any backup script or doc that uses `HeadlessPaths.SessionFile` silently backs up nothing. Align the constant or document the canonical path in the runbook and `/healthz` diagnostics.
8. **Desktop auto-update source.** `AppVersion.GitHubOwner` is `il90il90` while this clone's `origin` is `kesavananbu` (`upstream` is `il90il90`). Installed builds pull releases from whatever owner is compiled in, with no signature or hash check (F-09). Point at the repo you actually ship from, add checksum verification, or adopt a safer updater (Velopack recommended in `docs/ops/05-operations.md`).
9. Bump `Microsoft.Data.Sqlite` (High CVE in container via `SQLitePCLRaw.lib.e_sqlite3` 2.1.6 per baseline). Pin `FluentAssertions` to `[6.12.0,8.0.0)` when touching test projects.

### Milestone 3 — Security

10. F-01 / F-03 / F-20 / F-23: remove shipped default credentials and the pre-auth oracle, migrate to a memory-hard KDF with a versioned hash format (single change set), fix `WEB_PASSWORD` precedence.
11. F-02: arbitrary filesystem write via unvalidated download root and folder template.
12. F-04 / F-05 / F-06: session revocation, brute-force protection with authentication logging, secure cookie defaults.
13. F-09 / F-10: integrity verification for the self-updater and for yt-dlp/ffmpeg.
14. Remaining high and medium findings; verify F-26 (yt-dlp argument construction) when touching URL plugins.

### Milestone 4 — Make it genuinely usable

15. The free wins — roughly 14 capabilities for ~8 days, because the engine already implements them and only the HTTP surface and UI are missing.
16. Surface the 5 swallowed errors, replace 50 blocking dialogs with toasts, roll back failed edits.
17. Accessibility, responsive layout, validation, loading and empty states.
18. Replace 2.5-second polling of 9 endpoints with the SSE hub.
19. The customization layer: configurable columns, saved views, theming, rule templates.

### Milestone 5 — Truthful documentation and repo hygiene

20. Correct the eight false README claims. Either build Quiet Hours or stop documenting it.
21. **Legal packaging (free tool, per compliance re-run):** copy MIT **Option C** from `docs/compliance/drafts/LICENSE` to `/LICENSE` (set copyright holder first), ship `/NOTICE` from draft, publish `/SECURITY.md`, wire `installer/setup.iss` `LicenseFile=`, and include notices in release artifacts. Remove default `admin`/`changeme` from `docker-compose.yml` examples.
22. Commit `NuGet.config`, Headless test project, and CI workflows once validated (after [Test harness v2](38a4f447-2f6a-4def-b01a-55e61888774e) completes).

## Deferred decision

The frontend framework choice (recommended: Preact + TypeScript + Vite) is **not on the critical path**. Backlog items 12–14 ship on the existing vanilla JS and survive a later migration, so the decision can wait for better information.

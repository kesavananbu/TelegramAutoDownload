# Enterprise Readiness — Progress Ledger

Branch: `enterprise-readiness` (created from `main`)
Plan: `docs/enterprise-readiness-prompt.md`

This ledger is the recovery map. If context is lost, trust this file and `git log` over recollection.

## Concurrency policy

Analysis is parallelized; implementation is not. Read-only agents cannot conflict because each owns exactly one set of output files and only one agent is permitted to invoke the .NET build (concurrent `dotnet build` on shared `obj/`/`bin/` directories causes file-lock failures).

Implementation waves run **one implementer at a time**, each followed by a review, per `superpowers:subagent-driven-development`. Parallel implementers editing the same C# projects would conflict.

## Wave 1 — analysis (read-only, parallel): **COMPLETE**

| Agent | Scope | Owns these files | May build? |
|---|---|---|---|
| A | Phase 0 baseline: build, test, coverage, dependency + CVE audit, repo hygiene | `docs/assessment/00-baseline.md` | **Yes — exclusive** |
| B | Phase 4 security: STRIDE threat model, findings with proven attack paths | `docs/security/04-threat-model.md`, `docs/security/04-findings.md` | No |
| C | Phase 3 functional vetting: README claims vs code, desktop↔web parity, test gaps | `docs/quality/03-feature-verification.md`, `docs/quality/03-test-strategy.md` | No |
| D | Phase 2 architecture: as-is defects, to-be design, ADRs | `docs/architecture/02-target-architecture.md`, `docs/architecture/02-adr/` | No |
| E | UI audit + customizable UI design (highest product priority) | `docs/ux/09-ui-audit.md`, `docs/ux/09-ui-target-design.md` | No |
| F | Phases 1/6/7: product definition, licensing, compliance, marketplace | `docs/compliance/`, `docs/product/`, `docs/gtm/` | No |
| G | Phase 5 ops: SLOs, observability, backup/DR, CI/CD pipelines | `docs/ops/`, `.github/workflows/` | No |

### Wave 1 results

**Agent C — functional vetting: COMPLETE.** Wrote `docs/quality/03-feature-verification.md` and `docs/quality/03-test-strategy.md`. Of 50 documented claims: 19 work, 18 partial, 8 broken, 5 unverifiable by reading.

Controller spot-checked the four most consequential claims independently; all four confirmed:

| Claim | Verified against |
|---|---|
| Quiet Hours is entirely fictional | Repo-wide search matches only `README.md` and the new docs — no implementing code, no config field |
| Per-chat yt-dlp quality is unreachable | `ConfigParams.cs:62-66` overwrites it on every read, called from `ConfigFile.cs:31`, `ConfigStore.cs:31`, `SettingsWindow.xaml.cs:218` |
| Dark mode does not exist | `App.xaml.cs:89-92` hardcodes `Light.Blue` under the comment "dark mode removed" |
| Settings export leaks credentials | `SettingsWindow.xaml.cs:184-185` exports `ApiHash` and `BotToken`; README `:263` claims credentials are excluded |

Findings that change the plan:

1. **The commercial surface is completely untested.** `TelegramAutoDownload.Tests` does not reference the headless project at all, leaving ~3,300 lines of C# and ~1,000 lines of JS — including `WebAuthService`, which guards the user's entire Telegram account — with zero automated coverage. This makes the test-harness task a prerequisite for the security remediation wave, not a parallel track.
2. **Settings export is a credential-disclosure hazard,** not merely a documentation error. Users are invited to share exported configs for support, and the export carries the API hash and bot token. Route this into the security findings set rather than the docs cleanup.
3. **Silent data-corruption paths exist:** `.part` files are promoted with no size check (`BaseMessage.cs:145`), and chunk retry in `DownladerPlugin` appends without seeking (`:82-91`). A media archiver that silently corrupts archives fails at its core promise, so these outrank most feature work.
4. **A single corrupt byte in `config.txt` bricks the desktop app.** `ConfigFile.Read()` throws rather than falling back to defaults, and `Save()` is a bare `File.WriteAllText` with no atomic temp-and-rename — despite two sibling classes in the same codebase doing it correctly.
5. **Most desktop→web gaps are routing gaps, not engine gaps.** The shared `TelegramClient` layer already implements reactions, mute, notifications, statistics, and cancel; the headless API simply never exposes them. This corroborates the "free wins" thesis and is cheap UI/API surface work.

**Agent E — UI audit and target design: COMPLETE.** Wrote `docs/ux/09-ui-audit.md` and `docs/ux/09-ui-target-design.md`. Independently reached the same conclusions as Agent C on quiet hours and yt-dlp quality without either agent seeing the other's work, which raises confidence in both reports.

Controller re-counted the quantitative claims directly against the source; every figure checked out:

| Claim | Measured |
|---|---|
| ~50 blocking dialogs | 39 `alert(` + 11 `confirm(` = 50 in `app.js` |
| No responsive support | 0 `@media` rules in `styles.css` |
| No focus styling | 0 `:focus` rules in `styles.css` |
| Accessibility near-absent | 6 `aria-` attributes in all of `index.html` |
| Errors swallowed | 5 `console.warn` sites |
| SSE exists but is used only for logs | 2 `EventSource` references |

The orphaned-endpoint claim was verb-precise and holds: `POST /api/chats/{id}/sync` is never called from anywhere in the dashboard, and the `GET` forms of `/api/queue/downloads` and `/api/chats/{id}/bootstrap` are unused even though their `POST`/`DELETE` counterparts are wired (`app.js:199, 253, 785`).

Findings that change the plan:

1. **The free-wins estimate is concrete and large:** roughly 14 user-visible capabilities for about 8 engineer-days, because the engine already implements them and only the HTTP surface and UI are missing. This is the highest value-per-day work identified in the entire assessment.
2. **The frontend framework decision can be deferred.** Backlog items 1–12 (~24 days) ship on the existing vanilla JS and are not invalidated by a later migration. That removes the framework choice from the critical path and lets it be made with better information.
3. **Failed edits are not rolled back** (`app.js:525-539`), so the UI displays settings that were never persisted, and a stale patch closure re-sends old values on subsequent edits. This is a correctness bug that silently misrepresents system state, not a polish issue.
4. **Polling ~3.6 requests/second forever** across 9 endpoints, running full-table `GROUP BY` aggregates with no backoff, while destroying focus and overwriting fields mid-typing. It is simultaneously a UX defect and a load problem that will worsen as the queue table grows.

**Agent D — architecture: COMPLETE.** Wrote `docs/architecture/02-target-architecture.md` and 10 ADRs. Third independent confirmation that quiet hours does not exist.

Controller verified the two highest-stakes claims:

- **Dedup divergence — CONFIRMED.** `LegacyDedupMigration.RunAsync` early-returns whenever the table is non-empty (`LegacyDedupMigration.cs:18-22`), and its own doc comment states it "runs at most once per database." Meanwhile the WPF `FileDownloadIndex` keeps writing `downloaded_ids.json` on a 10-second debounce. Two authorities, imported across exactly once, diverging from day two. Precondition: both front ends sharing a data directory.
- **Ungoverned concurrency — CONFIRMED, with one correction.** The download orchestrator sizes its capacity purely from `DownloadThreads` (`DownloadOrchestrator.cs:61-62`) and consumes no rate-limit tokens at all. The agent stated the token bucket has exactly one consumer; it actually has two (`BootstrapScanner.cs:66` and `ChatDownloadTester.cs:148`). The correction does not weaken the finding — the download and live-update paths remain entirely ungoverned.
- **Live path cannot honour a flood pause — CONFIRMED structurally.** `FloodWaitTracker` lives in the Headless project, which references `TelegramClient` and not the reverse, so the live update path in `TelegramApp` is architecturally incapable of seeing it.

Correction to the controller's own brief: a migration runner **does** exist (`Database.cs:63-109`). The original prompt claimed otherwise; `docs/enterprise-readiness-prompt.md` has been corrected so later runs are not misled.

Findings that change the plan:

1. **Account-ban risk is now the top-ranked technical defect,** ahead of the security findings. Three uncoordinated governors can put ~30 concurrent operations against one rate-limited Telegram account, and the `UpdateChannelTooLong` catch-up path forms a positive feedback loop into deeper FLOOD_WAIT. Losing the user's Telegram account is a worse outcome than most vulnerabilities, and it happens during normal operation rather than requiring an attacker.
2. **Both the multi-tenancy and database questions are answered, and both answers are "don't."** Stay single-tenant per deployment (the tenancy unit is a Telegram account; `session.dat` is exclusively locked and its path is a process-global constant, and co-tenancy adds no capacity because FLOOD_WAIT is per-account). Keep SQLite (peak ~101 writes/s against a ~1,000–2,000/s ceiling, and Postgres's decisive advantage — multi-process writes — is structurally unusable here). This removes the largest speculative work item from the roadmap: ~22 engineer-weeks of tenancy retrofit avoided.
3. **Milestone A (9 engineer-weeks) is the minimum defensible scope** and retires every defect that can ban an account or corrupt data.

**Agent F — compliance, product, marketplace: COMPLETE.** Wrote `docs/compliance/06-compliance.md`, `docs/product/01-product-definition.md`, `docs/gtm/07-marketplace-readiness.md`, and eight drafts under `docs/compliance/drafts/`.

Controller verified every repository-level fact the legal analysis rests on:

| Claim | Verified |
|---|---|
| Product name contains "Telegram", breaching API ToS §2.3 | `installer/setup.iss:1` — `#define AppName "Telegram Auto Download"`; `README.md:1` |
| No EULA is ever presented at install | `installer/setup.iss:22` — `LicenseFile=` is empty |
| Member export harvests third parties' phone numbers | `TelegramApp.cs:1079` — `Phone = user?.phone ?? string.Empty`, unconditional |
| GPL-2+ ffmpeg redistributed in the image | `Dockerfile:36` apt-installs Debian ffmpeg |
| No LICENSE file despite README claiming MIT | Confirmed earlier in Wave 1 |

**This is the first finding that constrains what should be built at all, rather than how well it is built.** Every other workstream assumed the product is sellable and asked how to make it enterprise-grade. This one questions the premise.

Escalated to the product owner as a blocking decision. Technical hardening that is valuable under every possible answer (account-ban prevention, dedup divergence, data corruption, security, UI) proceeds regardless and is not gated on it.

Findings that change the plan:

1. **Hosted SaaS appears ruled out** on Telegram's non-sublicensable data licence, independent of any engineering choice. This retroactively validates Agent D's single-tenant recommendation — the two agents reached the same destination from completely different directions, legal and architectural.
2. **MIT is actively hostile to the commercial goal.** It is not merely a missing file; adopting it would grant everyone the right to resell the product and strip any licence check. The absence of a `LICENSE` file is currently ambiguity rather than a grant, which is recoverable — but prior releases claiming MIT may not be, which is a lawyer question.
3. **Dependency licensing is clean:** all 20 NuGet packages are MIT or Apache-2.0, zero copyleft. The GPL exposure comes solely from redistributing ffmpeg in the container.
4. **Cheap, unambiguous wins available now:** stop auto-downloading yt-dlp/ffmpeg (`YtdlpService.cs:82-89`), which improves both the §1201 and GPL positions at once; pin `FluentAssertions` to `[6.12.0,8.0.0)` before version 8's paid licence is pulled in; replace the `admin`/`changeme` defaults.

**Documentation correction is now its own roadmap item.** Eight README claims are false. For a product being sold, shipping documentation that describes non-existent features is a support-cost and trust problem independent of the code.

**Agents A, B, G — COMPLETE.** All seven Wave 1 deliverables landed.

- **A (baseline):** 398 tests total, 393 pass, 5 fail (391 unit + 7 UITest). Unit suite is **flaky under parallel xUnit** — five consecutive runs failed a *different* test each time, all racing on `AppLogAlertService` singleton state; do not treat a single named failure as stable. UITests 3/7 pass when a desktop session exists. **Measured production line coverage 10.62%** (636/5,987). Headless and Logger at 0%. High CVE in container via outdated `Microsoft.Data.Sqlite` / `SQLitePCLRaw.lib.e_sqlite3`. No `NuGet.config` committed → restore depends on machine feeds.
- **B (security):** 26 findings (3 critical, 5 high). Dominant chain: F-01 (`admin`/`changeme` + pre-auth oracle at `/api/web-auth/status`) → dashboard → F-02 arbitrary write (folder template can overwrite signing secret) → Telegram session theft. F-03 + F-23 must ship together when upgrading password hashing (KDF alone opens timing oracle). CSRF lead downgraded — `SameSite=Lax` + JSON POST only. README does not document Docker; baseline deployment for scoring is shared-network container behind a proxy.
- **G (ops):** Five workflows under `.github/workflows/` (unvalidated, untracked). **Session backup trap verified:** real session at `{ApplicationData}/TelegramAutoDownload/session.dat` ≠ `HeadlessPaths.SessionFile`. Auto-updater targets `il90il90` per `AppVersion.cs:6`; this repo's `origin` is `kesavananbu`, `upstream` is `il90il90`. Container yt-dlp from apt never self-updates; desktop self-updates every launch.

## Wave 2 — synthesis: COMPLETE

**Product decision (2026-10-01): commercial goal dropped.** Free personal tool; objective is excellent and secure. This removes licence enforcement, billing, marketplace packaging, SaaS, and multi-tenancy from scope, and reduces the roadmap from ~35 engineer-weeks to ~12–16. Scorecard and execution order in `docs/roadmap/08-scorecard-and-roadmap.md`.

Surviving legal items, reprioritized: add the missing `LICENSE`; add `NOTICE` for GPL ffmpeg if the image is published; stop auto-downloading yt-dlp/ffmpeg — which now ships as security finding F-10 rather than as legal compliance.

## Wave 3 — implementation: IN PROGRESS

**Task 1 (test harness):** First run [cancelled](3b0057ec-2b94-451e-b5aa-ee8c826a9ed8) at user request (no commits). **Re-run with `composer-2.5-fast`** — see new agent id in controller message after dispatch.

**Wave 1 re-run (2026-10-01, `composer-2.5-fast`):** User requested stop + re-run. Prior snapshot in commit `1c3753e`.

| Re-run agent | Status | Notes |
|---|---|---|
| [Architecture v2](0be1ceec-c4ef-450f-8c4c-45bd3c346ef3) | Complete | Same D-1…D-5, SQLite keep, 35 ew / Milestone A = 9 ew |
| [Security v2](de7a97b8-a2ff-4784-a186-1f717789da05) | Complete | 26 findings unchanged; F-01→F-02 chain; F-03+F-23 paired |
| [Compliance v2](d40621eb-0d90-4724-a6b9-6c518601e799) | Complete | **Free-tool scope**; MIT **Option C** recommended; operational checklist § below |
| [Ops v2](3c99a1db-9645-422a-8cd3-c21755f4c2db) | Complete | Workflows/docs refreshed (UNVALIDATED) |
| [Features v2](41e89070-34e0-4907-b0a7-c20475c65363) | Complete | 19/18/8/5 unchanged; 42 headless routes; new risk #10 `PromotePendingToQueuedAsync` |
| [UI v2](83a922aa-0407-46f2-bd8e-f0caf7d9333a) | Complete | Customization needs **new** APIs: `GET/PUT /api/ui/preferences`, `PATCH /api/chats/bulk`, templates; ~15 d vanilla fixes parallel to Preact migration |
| [Test harness v2](38a4f447-2f6a-4def-b01a-55e61888774e) | **Complete** | Commits `3072ef3`…`b5165b5`; **22/22** tests; Headless **33.86%** line coverage (was 0%). Controller verified **22 passed** locally. |
| [Baseline v2](6c0f64d6-f5c1-4a93-9b89-78a5edf09d79) | Pending or in progress | Check working tree |

## Milestone 1 — COMPLETE (2026-10-01)

`NuGet.config`, `RunnerApp.sln`, `TelegramAutoDownload.Headless.Tests`, `docs/assessment/task-01-report.md`. **Next:** Milestone 2 item 4 — unified Telegram API budget (ADR-003).

**Compliance v2 operational checklist (no code yet):** root `LICENSE` (MIT Option C in `docs/compliance/drafts/LICENSE`), `/NOTICE`, `/SECURITY.md`, installer `LicenseFile=`, remove `admin`/`changeme` from compose **examples**, member-export phone warning or gate, Telegram unofficial naming/disclosure.

Serialized implementer → reviewer loops after Task 1. Order: Milestone 2 (account ban, dedup, corruption, session path) → Milestone 3 (security F-01 chain first) → Milestone 4 (UI free wins).

## Decisions log

| Date | Decision | Rationale |
|---|---|---|
| 2026-09-30 | Work on branch `enterprise-readiness`, not `main` | Desktop app has real installed users and an auto-updater; `main` must stay releasable. |
| 2026-09-30 | Analysis parallel, implementation serial | Read-only agents with disjoint outputs cannot conflict; concurrent C# implementers would. |
| 2026-09-30 | Exactly one agent owns `dotnet build` | Concurrent builds contend on `obj/`/`bin/` file locks. |
| 2026-10-01 | Commercial goal dropped; harden as free personal tool | User decision via questionnaire. Compliance/marketplace docs kept as archive only. |

## Open questions (non-blocking)

1. **Desktop vs web primary surface** — parity gap work is large; deprecating WPF would shrink scope but affects existing installers.
2. **Auto-update repo** — compile `AppVersion.GitHubOwner` as `kesavananbu` (origin) or keep tracking `upstream` (`il90il90`) releases.
3. **Root MIT licence** — draft at `docs/compliance/drafts/LICENSE` is multi-option and not adoption-ready; needs a single chosen text before copying to `/LICENSE`.

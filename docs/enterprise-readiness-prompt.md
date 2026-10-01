# Enterprise Readiness & Commercialization Prompt

Copy everything below the line into a fresh agent session rooted at this repository.

---

## Role

You are a principal engineer + product architect + application security lead, jointly responsible for taking **TelegramAutoDownload** from a working proof-of-concept to a commercially sellable, enterprise-deployable product. You own the technical judgement end to end: architecture, scale, correctness, security, operations, compliance, and packaging.

You are not writing a wish list. Every claim you make must be backed by a file path and line range you actually read, a command you actually ran, or an explicitly labelled assumption.

## Mission

Produce (a) an honest assessment of the current state, (b) a target design for an enterprise-ready and marketplace-ready product, and (c) an executable, prioritized plan that closes the gap — including security risks identified *and fixed*, not just listed.

---

## Ground truth about this repository

Verify each of these before relying on them; they are a starting map, not gospel.

**What it is.** A .NET 8 solution with two front ends over a shared Telegram automation core:

- `TelegramAutoDownload/` — WPF desktop app for Windows (v2.9.3 per `README.md`), the original product surface. Distributed as an Inno Setup installer and a portable ZIP via GitHub Releases, with a self-update path in `Services/AutoUpdateService.cs`.
- `TelegramAutoDownload.Headless/` — ASP.NET Core minimal-API server plus a static vanilla-JS dashboard (`wwwroot/app.js`, ~1000 lines), containerized via `Dockerfile` and `docker-compose.yml`. This is the newer surface and the plausible basis for a hosted or self-hosted commercial product.
- `TelegramClient/` — Telegram API layer on `WTelegramClient`; `TelegramApp.cs` is ~1600 lines and is the de facto orchestrator.
- `BasePlugin/`, `DownloadService/`, `YoutubePlugin/`, `SocialMediaPlugin/`, `TorrentPlugin/` — a priority-ordered plugin chain (direct Telegram download, yt-dlp for YouTube and generic URLs, MonoTorrent for magnets/`.torrent`).
- `Logger/` — Serilog wiring, including a Seq sink.
- `TelegramAutoDownload.Tests/` (xUnit, ~28 files) and `TelegramAutoDownload.UITests/` (FlaUI, Windows-only).

**State that matters for this exercise.** Confirm each:

- Authentication for the headless web UI lives in `WebAuthService.cs` / `WebAuthMiddleware.cs`. Passwords are hashed with bare `SHA256.HashData` (no salt, no KDF). The session cookie is `username|expiry|HMAC-SHA256`, stateless and therefore non-revocable. `Secure` is opt-in via `WEB_AUTH_SECURE_COOKIES`, default off. Auth can be turned off entirely with `WEB_AUTH_ENABLED=false`.
- `docker-compose.yml` ships `WEB_USERNAME: admin` / `WEB_PASSWORD: changeme` as literal defaults.
- There is no CSRF protection, no login rate limiting or lockout, no account model beyond a single operator, no authorization/RBAC, and no audit trail. `index.html`, `app.js`, `styles.css`, and the auth status/login endpoints are intentionally public (`WebAuthService.IsPublicPath`).
- The API in `Program.cs` (~570 lines, all endpoints in one file) is single-tenant by construction: `HeadlessHost`, `LoginCoordinator`, `ConfigStore`, `MediaRepository` are process-wide singletons bound to one Telegram session.
- State is a single SQLite file (`Data/Database.cs`, Dapper) plus JSON config and a `session.dat` Telegram session on a mounted volume. A migration runner does exist at `Data/Database.cs:63-109` (an earlier draft of this document wrongly claimed there was none) with known gaps. No backup, encryption at rest, or multi-node story.
- `POST /api/settings/download-folder` accepts an arbitrary filesystem path from the client and writes it to config; downloads then land there. Folder templates and regex filters are user-supplied and flow into path construction and `Regex` evaluation.
- yt-dlp and ffmpeg are invoked as external processes and yt-dlp is auto-updated from the internet at runtime.
- There is **no** `.sln` tracked in the repo (despite `README.md` referencing `RunnerApp.sln`), **no** `.github/` CI workflows, and **no** `LICENSE` file (despite `README.md` claiming MIT).
- Release process is manual and documented in `.cursor/rules/release-process.mdc`; every release must ship both the Setup.exe and the Portable ZIP.

---

## Operating rules

1. **Evidence over assertion.** Cite `path:line` for every finding. If you did not read it, say so.
2. **Exploit or downgrade.** For each security finding, either demonstrate a concrete attack path (preconditions, steps, impact) or explicitly downgrade it to "theoretical / defense-in-depth". No CVSS theater on findings you cannot reach.
3. **Preserve existing users.** The desktop app has real installs and an auto-update path. Any change must define its migration story for existing `config.txt`, `downloaded_ids.json`, `stats.json`, `session.dat`, and the SQLite database, or explicitly declare a breaking change with a version gate.
4. **Tests before refactors.** Do not restructure `TelegramApp.cs` or `Program.cs` until characterization tests pin current behavior.
5. **Small, reviewable commits.** One concern per commit, each independently revertable, each leaving `dotnet build` and `dotnet test` green.
6. **Never commit secrets.** No API IDs, hashes, bot tokens, or session files. Flag any that are already committed or present in history.
7. **Ask before deciding the business model.** Hosted SaaS, self-hosted licensed, and open-core lead to materially different architectures. If the answer isn't given to you, present the trade-offs and stop for a decision rather than guessing.

---

## Phase 0 — Baseline (do this first, report before proceeding)

- Build and test the full solution. Record what passes, what fails, what is Windows-only, and actual test execution time.
- Measure real coverage (e.g. `coverlet`), per project. Report the number, not an impression.
- Inventory all NuGet dependencies with versions; check each for known CVEs, maintenance status, and license compatibility with a commercial offering. `WTelegramClient`, `MonoTorrent`, `Ytdlp.NET`, and the yt-dlp/ffmpeg binaries deserve individual attention.
- Enumerate the full HTTP API surface: method, path, auth requirement, input validation, side effects, and blast radius.
- Map all persisted state: file, format, location, size growth, concurrency model, and what breaks if it's corrupted or lost.
- Produce a dependency graph across projects and call out cycles, god objects, and layering violations.

**Deliverable:** `docs/assessment/00-baseline.md`.

## Phase 1 — Product definition

- Define who this is for. Distinguish at least: individual power user (today's user), team/media-archive operator, and enterprise/compliance-driven archival. For each: jobs to be done, willingness to pay, and deployment expectations.
- Decide and justify the commercial shape: self-hosted with a license key, managed SaaS, or open-core with paid tiers. Name the architectural consequences of the recommendation.
- Define tiers and what gates each one (seats, monitored chats, throughput, retention, SSO, audit log, support SLA).
- Identify the hard constraints that could kill the business case: Telegram's API terms for user-account automation, the torrent and yt-dlp features' legal posture, and the copyright/DMCA exposure of an archival tool. Be blunt about which features a commercial vendor can ship and which must be removed, made opt-in, or shipped only for self-hosted deployments.

**Deliverable:** `docs/product/01-product-definition.md`.

## Phase 2 — Target architecture

- Produce a target architecture that addresses, explicitly: multi-tenancy (or a defensible decision to stay single-tenant per deployment), horizontal scale of the scanner and downloader, durable queueing, Telegram FLOOD_WAIT and rate-limit behavior under concurrency (`Scanning/FloodWaitTracker.cs`, `TokenBucketRateLimiter.cs`), storage backends beyond a local volume, and graceful restart/resume semantics.
- Decide the persistence story: keep SQLite with WAL and clear single-writer boundaries, or move to Postgres. Justify with the concurrency and scale numbers you derived, not preference. Define a real migration framework either way.
- Separate the API process from the worker process, or justify keeping them together. `DownloadOrchestrator` is currently a hosted service inside the web app.
- Define the plugin contract as a stable, versioned, sandboxable boundary — including what happens when a plugin hangs, crashes, or consumes unbounded disk.
- Specify configuration and secret management (`ConfigStore`, env vars, `session.dat`) suitable for enterprise deployment: encryption at rest, rotation, and no plaintext credentials on disk.
- Give each proposal a migration path from today's code, with effort estimates in engineer-weeks.

**Deliverables:** `docs/architecture/02-target-architecture.md`, `docs/architecture/02-adr/` (one ADR per significant decision).

## Phase 3 — Functional vetting

- Write a feature inventory from `README.md` and `CHANGELOG.md`, then verify each feature against the code and, where feasible, at runtime. Mark each: works / partially works / broken / undocumented behavior. This is the "function-wetted" step — claims in the README are hypotheses until tested.
- Define the test pyramid: unit, integration (real SQLite, faked Telegram), contract tests for the plugin interface, API tests against the headless server, and end-to-end with a mocked Telegram transport. State target coverage per layer and the rationale.
- Close the highest-risk gaps first. Candidates: resume/`.part` correctness under crash, dedup index under concurrency, bootstrap scan pagination and restart, FLOOD_WAIT backoff, quiet hours boundaries, folder-template and regex-filter edge cases, and the web auth flow.
- Add deterministic failure-injection tests: network loss mid-download, disk full, permission denied, corrupt database, killed process.
- Define load and soak targets (e.g. N monitored chats, M concurrent downloads, 72-hour soak) and actually measure against them.

**Deliverables:** `docs/quality/03-feature-verification.md`, `docs/quality/03-test-strategy.md`, plus the tests themselves.

## Phase 4 — Security: identify, then fix

- Build a threat model (STRIDE or equivalent) with a trust-boundary diagram. Include the Telegram session as a crown-jewel asset: it is full account access, not just a download token.
- Review against OWASP ASVS L2 and the OWASP Top 10. At minimum, reach a verdict on each of these, with evidence:
  - Password storage and the unsalted SHA-256 hash; migrate to a memory-hard KDF with a versioned hash format and transparent rehash on next login.
  - Session design: revocation, rotation on privilege change, idle timeout, absolute timeout, and `Secure`/`SameSite` defaults that are safe out of the box rather than opt-in.
  - CSRF on every state-changing endpoint (all of them are cookie-authenticated `POST`/`PATCH`/`DELETE` today).
  - Brute-force protection: rate limiting, lockout, and login attempt logging.
  - The shipped default credentials in `docker-compose.yml` and the first-run bootstrap flow.
  - Path handling: `download-folder`, folder templates, and `HeadlessLogReader.ResolveSafeFileName`. Prove traversal is or is not reachable.
  - ReDoS via user-supplied regex filters (`IgnoreFileByRegex`) — no timeout is currently set on `Regex` evaluation.
  - Command and argument injection into yt-dlp/ffmpeg process invocation, and the trust placed in the auto-updated yt-dlp binary (integrity verification, pinning).
  - SSRF and content-trust in the social/YouTube plugins, which fetch arbitrary user-supplied URLs.
  - Torrent plugin exposure: inbound connections, DHT, disk amplification, and untrusted peers.
  - Log hygiene: confirm phone numbers, 2FA passwords, API hashes, bot tokens, and chat content are not written to logs, and that the SSE log stream is not a data-exfiltration channel.
  - Secrets at rest: `session.dat`, `web-auth.json`, `.web-auth-secret`, config JSON, file permissions, and container user (`user: "1029:100"` in compose).
  - Container hardening: non-root, read-only rootfs, dropped capabilities, no unnecessary packages, pinned base image digests.
  - Dependency and supply-chain risk: SBOM, pinned versions, and a vulnerability scan in CI.
- Rank findings by real-world exploitability and business impact. **Then fix them**, highest first, each fix accompanied by a regression test that fails before and passes after.

**Deliverables:** `docs/security/04-threat-model.md`, `docs/security/04-findings.md` (with status per finding), and the remediation commits.

## Phase 5 — Reliability and operations

- Define SLOs that a customer would accept, and instrument to prove them: structured logging with correlation IDs, metrics (Prometheus/OpenTelemetry), health and readiness endpoints, and useful failure alerting.
- Define backup, restore, and disaster recovery for the database, config, and Telegram session — including a tested restore procedure.
- Specify upgrade and rollback for both surfaces: container image upgrades and the desktop auto-updater. The updater currently shells out to a generated batch file via `cmd.exe`; audit it as part of Phase 4 and harden it here.
- Build CI/CD from scratch (there is none): build, test, coverage gate, static analysis, dependency and secret scanning, container build and scan, signed artifacts, and an automated release that respects `.cursor/rules/release-process.mdc` (both Setup.exe and Portable ZIP, always).

**Deliverable:** `docs/ops/05-operations.md` plus working `.github/workflows/`.

## Phase 6 — Compliance and legal readiness

- Data protection: classify every piece of personal data the system touches (phone numbers, chat content, member exports, message history JSONL). Map GDPR obligations — lawful basis, retention, deletion, export, processor vs controller — for both self-hosted and hosted models.
- Licensing: add a real `LICENSE` file matching the stated intent, verify every dependency's license is compatible with that intent and with commercial distribution, and produce a third-party notices file.
- Telegram platform terms and the realistic risk of account bans for automated user-account scanning; document what the product does to stay within limits and what it discloses to customers.
- Content liability: acceptable use policy, DMCA process, and the posture on the torrent and arbitrary-URL download features.

**Deliverable:** `docs/compliance/06-compliance.md`.

## Phase 7 — Marketplace and go-to-market packaging

- Choose target channels and enumerate their concrete technical requirements: Docker Hub / GitHub Container Registry, Microsoft Store or signed MSIX for the desktop app, Unraid / Synology / TrueNAS app catalogs (this is the natural fit given the existing compose file), and cloud marketplaces if SaaS is chosen.
- Specify licensing and entitlement enforcement, billing integration, and telemetry — with privacy defaults an enterprise buyer will accept (opt-in, documented, disableable).
- Define the onboarding experience: first-run wizard, secure credential bootstrap that cannot leave a default password in place, and time-to-first-successful-download as a tracked metric.
- Define the support surface: documentation set, diagnostics bundle export, versioning and support policy, and security disclosure process (`SECURITY.md`).
- Code signing for Windows binaries and reproducible, verifiable builds.

**Deliverable:** `docs/gtm/07-marketplace-readiness.md`.

## Phase 8 — Scorecard and roadmap

- Score the current state 1–5 on each dimension: architecture, scalability, functional correctness, test coverage, security, reliability, observability, operability, compliance, documentation, packaging. Justify each score in one sentence with evidence, and state what a 5 would require.
- Produce a prioritized backlog (RICE or equivalent) with effort in engineer-weeks and explicit dependencies.
- Group into milestones with clear exit criteria — for example: *Hardened Single-Tenant* (security fixes + CI + tests), *Production Self-Hosted* (observability, backup, packaging, docs), *Multi-Tenant/SaaS* (if the business model calls for it).
- State plainly what you would **not** build, and why.

**Deliverable:** `docs/roadmap/08-scorecard-and-roadmap.md`.

---

## Execution protocol

- Work phase by phase. After each phase, post a short summary: what you found, what changed, what surprised you, and what you need decided. Wait for a go-ahead before starting the next phase.
- Phase 4 remediation and Phase 3 test-gap work are the only phases where you should be writing significant production code unprompted. Everything else is analysis until the plan is approved.
- Keep a running `docs/assessment/decisions.md` of open questions and their resolutions.
- If you discover that a documented feature does not exist, or that a stated fact in this prompt is wrong, correct the record in your report — do not quietly work around it.

## Definition of done

1. `dotnet build` and `dotnet test` pass in CI on every push, with a coverage gate that is enforced rather than aspirational.
2. Every security finding rated high or critical is fixed and covered by a regression test; the rest are documented with an owner and a decision.
3. No default credentials, no plaintext secrets at rest, no auth bypass reachable from a default deployment.
4. Load and soak targets are defined and measured, with results published.
5. Every feature in the README is verified true or the README is corrected.
6. A new operator can go from zero to a first successful download following only the documentation, on both the desktop and container paths.
7. The scorecard, roadmap, and ADRs exist and are consistent with the code as it now stands.

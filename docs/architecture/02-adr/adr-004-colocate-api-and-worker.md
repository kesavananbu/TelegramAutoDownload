# ADR-004 — Co-locate the API and worker; split out the plugin host instead

- **Status:** Proposed
- **Date:** 2026-10-01
- **Review:** Composer 2.5 re-run — read-only verification on `enterprise-readiness` @ `1c3753e`; no build
- **Deciders:** Architecture
- **Related:** ADR-001, ADR-006

## Context

`DownloadOrchestrator` runs as an `IHostedService` inside the web process (`Program.cs:40`), sharing all singletons with the HTTP endpoints (`Program.cs:28-41`). The brief asks whether to split the API process from the worker process, or defend keeping them together.

The usual motivation for splitting is independent scaling. That motivation is absent here: ADR-001 and ADR-003 establish that worker count is pinned at **one** by `session.dat` exclusivity (`TelegramClient/SessionLockHelper.cs:12-23`) and by a per-account API budget that more processes cannot raise.

The remaining motivation is fault isolation — so the question becomes *what actually faults?*

**Three endpoint families need the live session synchronously in the request path:**

- Login: `POST /api/login/{phone,code,password}` drives an interactive state machine over `Client.Login` (`Program.cs:129,136,143` → `LoginCoordinator.cs:73`).
- Chat refresh: `POST /api/chats/refresh` calls `Telegram.GetAllChats()` and returns the result (`Program.cs:255` → `HeadlessHost.cs:159`).
- Test download: `POST /api/chats/{id}/test-download` runs a live probe and returns a report (`Program.cs:430`).

Splitting forces an IPC contract for all three, including a *stateful, streaming* one for the login machine.

**The shared singletons are genuinely shared.** `HeadlessHost` is simultaneously the session holder (`HeadlessHost.cs:35`), the config applier (`:221`) and the live-progress cache read by `/api/downloads` (`:150`). `BootstrapManager` holds job state in memory and serves it to the UI (`BootstrapManager.cs:20,81`).

**Availability does not improve.** If the worker dies, the session dies; a surviving API can then serve only stale database rows — worse than an honest 503.

**But the real fault source is plugins.** They are the only untrusted, third-party-dependency-laden code in the system: `Assembly.LoadFrom` into the default `AssemblyLoadContext` with no unload (`Factory/Service/MessageTextFactoryService.cs:64`), no `CancellationToken` on the contract (`BasePlugin/IBasePlugin.cs:8`), no timeout at any layer, and they shell out to `yt-dlp` and run a BitTorrent stack. A plugin hang permanently consumes an orchestrator slot (`DownloadOrchestrator.cs:80` increments; the `finally` at `:188` never runs). A plugin crash takes down the dashboard, the orchestrator and the Telegram session together.

## Decision

**Two processes:**

1. **`tad-server`** — Kestrel, all endpoints, the Telegram gateway, the orchestrator, the scan worker and the janitor. One process, one session, one API budget.
2. **`tad-pluginhost`** — a supervised child process executing all plugin code, with a runtime timeout, an RSS cap, a byte quota and automatic restart (see ADR-006).

This is a **supervised-process boundary, not a microservice boundary**: no service discovery, no network exposure, no independent deployment, no schema registry. Transport is a Unix domain socket (Linux) or named pipe (Windows) carrying framed protobuf. The plugin host is started, monitored and killed by `tad-server`, and its lifetime is a child of the server's.

Inside `tad-server`, API and worker concerns are separated by **layer**, not by process (ADR-009): endpoint modules → application services → ports → infrastructure. Hosted services and endpoints call the same application services. This gives the testability benefit of a split without the IPC cost.

Define a **reversal trigger**: if a future feature requires the API to remain available while the Telegram engine is being restarted or upgraded, reconsider — that is a real availability argument, unlike scaling.

## Consequences

**Positive**

- The fault domain that actually faults is isolated. A plugin that hangs, crashes or writes 1 TB degrades one queue item instead of the whole deployment.
- No IPC contract for login, chat refresh or test-download, so no stateful cross-process login machine to design, version and debug.
- Single process for the session means no `session.dat` lock contention, so `SessionLockHelper`'s 40-second retry loop (`SessionLockHelper.cs:32-33`) stops being load-bearing.
- One deployment artifact plus one supervised child. Operationally close to today's container.
- Layered separation still delivers the testability benefit: application services are unit-testable without Kestrel and without Telegram.

**Negative**

- A crash in `tad-server` still takes down both HTTP and downloading. Accepted: the session is gone in that scenario anyway, so a surviving API would only serve stale data. Mitigated by `restart: unless-stopped` and fast crash recovery (`Database.cs:56-58` already resets stuck rows; ADR-005 makes that a lease reaper measured in seconds).
- Memory pressure from downloads and from HTTP share one heap. Bounded because the large allocations — plugin buffers and `yt-dlp` — move out to the plugin host.
- Two artifacts to build, version and ship together. Mitigated by publishing them from one solution with a shared version and an `ApiVersion` handshake at startup.
- IPC introduces a new failure mode (socket death, protocol skew). Mitigated by treating any IPC failure as a plugin failure: mark the item failed, restart the host, circuit-break after 3 crashes in 5 minutes.

**Neutral**

- Blocks a future "scale the workers" instinct. That instinct is wrong here (ADR-003) and forcing the conversation is a feature.

## Alternatives considered

### A. Full API/worker split (two peer services over a queue)

Rejected. Requires IPC for three interactive endpoint families including a stateful login machine; requires moving `HeadlessHost`'s in-memory progress cache (`HeadlessHost.cs:33`) and `BootstrapManager`'s job state (`BootstrapManager.cs:20`) into shared storage; and delivers no availability gain because the session dies with the worker. Estimated **5–6 ew** for negative value.

### B. Keep everything in one process, including plugins (status quo)

Rejected. It leaves D-5 unfixed: a hung plugin permanently consumes a download slot and a crashing plugin takes down the session. This is the highest-probability non-ban outage in the system.

### C. Three processes — API, engine, plugin host

Rejected. The API/engine boundary is the one with no benefit (alternative A) and the highest coupling. Adding it alongside the plugin split pays alternative A's cost on top of ADR-006's.

### D. AppDomain / collectible `AssemblyLoadContext` isolation instead of a process

Tempting: cheaper than IPC, and .NET's collectible ALC allows unloading. Rejected because it does not contain the failures that matter. A collectible ALC cannot enforce a memory cap, cannot survive `Environment.Exit` or a `StackOverflowException`, and cannot reliably abort a thread stuck in native code — which is precisely what a wedged `yt-dlp` subprocess looks like. Only an OS process boundary with Job Objects or cgroups contains all four failure modes in ADR-006's table.

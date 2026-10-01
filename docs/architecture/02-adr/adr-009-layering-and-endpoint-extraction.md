# ADR-009 — Extract endpoints from `Program.cs` and introduce an application layer

- **Status:** Proposed
- **Date:** 2026-09-30
- **Deciders:** Architecture
- **Related:** ADR-003, ADR-004, ADR-005

## Context

`TelegramAutoDownload.Headless/Program.cs` is 641 lines. Lines 52–533 are ~40 inline endpoint lambdas; 537–575 are business logic; 586–641 are DTOs. Startup side effects are interleaved at 105–117.

**Business logic lives in the composition root.** `TryStartBootstrap` (`Program.cs:537-575`) performs a config lookup, a login check, a monitoring-policy decision, a call into `BootstrapManager`, and conflict-to-HTTP-status mapping. It is a static local function in `Program.cs`, so it cannot be referenced, tested or reused.

**Endpoints reach past the domain into persistence.** `/api/queue/retry-failed` calls `repo.RequeueAllFailedAsync` directly (`Program.cs:465`), as do `/retry-skipped` (`:471`), `/delete-failed` (`:477`), `/clear` (`:484`) and `/api/queue/{chatId}/{messageId}/retry` (`:459`). There is no layer between HTTP and SQL where a policy — "do not revive dead-lettered rows", "audit this destructive action" — could live.

**Almost no abstractions exist.** The entire solution declares **four** interfaces: `IBasePlugin`, `IConfigTelegramLogger`, `IConfigLogger`, `IChannel`. None abstracts Telegram, storage, the filesystem or the clock. Concrete types are `sealed` and constructed directly: `BootstrapManager.cs:52` news a `BootstrapScanner`; `HeadlessHost.cs:54` news a `TelegramApp`; `MediaRepository` takes a concrete `Database` (`MediaRepository.cs:14`).

**The server has zero test coverage.** `TelegramAutoDownload.Tests.csproj` references seven projects (lines 21-29) and **`TelegramAutoDownload.Headless` is not among them**. It also targets `net8.0-windows` (line 4), so it cannot run on the Linux CI that builds the container. The 296 existing test cases cover zero lines of `DownloadOrchestrator`, `MediaRepository`, `TokenBucketRateLimiter`, `FloodWaitTracker`, `BootstrapScanner`, `BootstrapManager`, or any endpoint.

**Cross-cutting concerns are absent.** No `UseExceptionHandler`, no `ProblemDetails`, no `UseStatusCodePages`. Error shapes are inconsistent: `{ error }` (`Program.cs:74`), a five-field conflict object (`:554-560`), a bare `499` (`:435`). Validation is scattered inline as `Math.Clamp` calls (`:195, 232-239, 400`).

**God objects compound it.** `TelegramClient/TelegramApp.cs` is 1,799 lines carrying at least nine responsibilities, with `Client_OnUpdates` alone at 245 lines (`:257-501`), nesting depth 9 and eight `catch` blocks. `MainWindow.xaml.cs` is 1,478 lines.

The cost is velocity: no change to download behaviour can be verified without manually running a container and logging into Telegram.

## Decision

**Introduce a four-layer structure inside the single process (ADR-004), and extract every endpoint out of `Program.cs`.**

```
Api            endpoint modules · ProblemDetails · FluentValidation · health · metrics
Application    ChatService · QueueService · ScanService · LoginService · SettingsService
               + pure policies: ScanPolicy · RetryPolicy · DedupPolicy
Core           ports: ITelegramGateway · IMediaStore · IConfigProvider · ISecretStore
               · IPluginHost · IClock · IFileSystem       (interfaces only, no dependencies)
Infrastructure adapters: TelegramGateway · SqliteMediaStore · JsonConfigProvider · ...
```

Concretely:

1. **Endpoint modules.** One `static class` per area — `LoginEndpoints`, `ChatEndpoints`, `QueueEndpoints`, `SettingsEndpoints`, `ScanEndpoints`, `LogEndpoints` — each exposing `MapXxx(this IEndpointRouteBuilder)`. Target ~40 lines each, containing routing and model binding only. `Program.cs` drops below 120 lines: configuration, DI registration, middleware, `MapXxx` calls.
2. **Application services.** `TryStartBootstrap` (`Program.cs:537`) becomes `ScanService.StartBootstrapAsync`, returning a result type that the endpoint maps to HTTP. The direct repository calls at `Program.cs:459-484` move behind `QueueService`, which is where ADR-005's "never bulk-revive `dead` rows" policy will live.
3. **Ports.** Six interfaces in `Core`, with today's concrete classes as their first adapters. `ITelegramGateway` is also ADR-003's chokepoint, so this work is shared. `IClock` in particular makes `TokenBucketRateLimiter` (`TokenBucketRateLimiter.cs:64-70`) and `FloodWaitTracker` (`FloodWaitTracker.cs:40`) testable without `Task.Delay`.
4. **Cross-cutting middleware.** `UseExceptionHandler` emitting RFC 7807 `ProblemDetails`, a single error contract, FluentValidation on all request DTOs with 422 responses, and correlation IDs.
5. **A cross-platform test project.** New `TelegramAutoDownload.Headless.Tests` targeting `net8.0` (not `net8.0-windows`), running on Linux CI. Target ≥ 70 % line coverage on the orchestrator, media store, API budget and scanner, plus `WebApplicationFactory` integration tests over the endpoint modules with a fake gateway.
6. **Architecture tests.** NetArchTest rules: `Api` must not reference `Infrastructure`; `Core` must have no outbound project references; only `TelegramGateway` may reference `WTelegram.Client` (which enforces ADR-003).
7. **God-object decomposition, opportunistically.** `TelegramApp` splits along the seams already visible in the file — `SessionManager` (`:99-164`), `UpdatePump` (`:257-501`), `HistorySync` (`:507`), `PeerResolver` (`:731`), `ChatDirectory` (`:1560-1741`), `MemberExport` (`:966`), `HistoryExport` (`:1089`). Done as each area is touched, not as a big-bang rewrite.

## Consequences

**Positive**

- Download behaviour becomes verifiable in CI without a Telegram account. Today it is verifiable only by hand.
- Retry policy, dedup policy and scan policy become pure functions — the highest-risk logic in the system becomes the easiest to test.
- A place exists for cross-cutting policy. ADR-005's dead-letter rule and ADR-008's audit logging have somewhere to live that is not an endpoint lambda.
- One error contract makes the dashboard's error handling uniform instead of special-cased per endpoint.
- Architecture tests make ADR-003's single-chokepoint rule mechanically enforced rather than a convention that erodes.
- Reviewing a change stops requiring a 641-line file scroll.

**Negative**

- Roughly 3 ew of pure refactoring with no user-visible benefit. This is the hardest item to justify to a stakeholder, and it should be sequenced *after* Milestone A so the safety fixes land first.
- More files and more indirection. Mitigated by keeping `Core` interface-only and resisting per-entity repositories.
- Refactoring without tests is risky, which is circular. Mitigated by ordering: characterisation tests over the HTTP surface first (they pin current behaviour without needing internal seams), then extract beneath them.
- A second test project to maintain. Justified: the existing one cannot run on Linux and cannot reference the server.

**Neutral**

- No runtime behaviour change is intended. Any observed change is a bug in the refactor.

## Alternatives considered

### A. Leave `Program.cs` as-is; add tests around it

`WebApplicationFactory` can exercise the endpoints without extraction. Rejected as the endpoint but **adopted as step 1** — those characterisation tests are exactly how the extraction is made safe. Insufficient alone: without ports, every test needs a real Telegram session and a real filesystem, so the orchestrator and budget stay untested.

### B. Full Clean Architecture with separate assemblies per layer

Compiler-enforced layering. Rejected as heavier than warranted: four assemblies for a ~5k-line server adds build and navigation friction. NetArchTest gives the same enforcement over namespaces at a fraction of the cost. Revisit if the team grows past ~4 engineers.

### C. Vertical slices with MediatR

Each feature as a request/handler pair. Rejected: MediatR's indirection pays off with many handlers and rich pipeline behaviours; with ~40 thin endpoints it mostly obscures the call graph. Endpoint modules plus application services give the same cohesion with a traceable call stack.

### D. Rewrite `TelegramApp` in one pass

Rejected. It is 1,799 lines with no test coverage and it is the integration point with a third-party MTProto library. A big-bang rewrite is how a working system becomes a broken one. Incremental extraction along the existing method seams, behind `ITelegramGateway`, is slower and safe.

### E. Add `TelegramAutoDownload.Headless` to the existing test project

Rejected. That project targets `net8.0-windows` (`TelegramAutoDownload.Tests.csproj:4`) because it references the WPF app (`:28`). Adding the server there would make the server's tests un-runnable on the Linux CI that builds and ships the container — the one place they most need to run.

# ADR-001 — Remain single-tenant per deployment

- **Status:** Proposed
- **Date:** 2026-10-01
- **Review:** Composer 2.5 re-run — read-only verification on `enterprise-readiness` @ `1c3753e`; no build
- **Deciders:** Architecture
- **Related:** ADR-002, ADR-004

## Context

The brief asks for multi-tenancy or a rigorously defended decision to remain single-tenant. The current runtime is single-tenant by construction: every service in `TelegramAutoDownload.Headless/Program.cs:28-41` is a process-wide singleton bound to one Telegram session.

Four facts from the code constrain the answer.

**1. The runtime cannot hold two Telegram sessions.** The session path is a process-global constant computed from `SpecialFolder.ApplicationData`, hardcoded in two places (`TelegramClient/TelegramApp.cs:101-104` and `:161-164`). `SessionLockHelper` exists solely because `session.dat` is exclusively locked and a second holder must retry — up to 20 attempts at 2 s intervals (`TelegramClient/SessionLockHelper.cs:32-33`). Nothing parameterises the path per tenant.

**2. The persistence schema has no tenant dimension, and its keys are not globally unique.** `Media`'s primary key is `(chat_id, message_id)` (`Data/Schema/001_initial.sql:32`). `ChatScanState` keys on `chat_id` alone (`:43`) and `LegacyDedup` on `document_id` alone (`:55`). Telegram `chat_id` and `document_id` are global identifiers, so two accounts monitoring the same public channel would write to the same rows — cross-tenant data corruption by default, not by bug.

**3. A tenant fault is currently unrecoverable without human action.** Plugins load into the default `AssemblyLoadContext` with no isolation (`TelegramClient/Factory/Service/MessageTextFactoryService.cs:64`) and `IBasePlugin.ExecuteAsync` accepts no `CancellationToken` (`BasePlugin/IBasePlugin.cs:8`). A crash therefore terminates the process. Under co-tenancy that terminates every co-located session, and re-establishing a Telegram session needs an **interactive SMS code** — so recovery is N manual re-authentications, not a pod restart.

**4. Co-tenancy adds nothing to the scaling ceiling.** Throughput is bounded by a per-account FLOOD_WAIT budget of roughly 1–5 requests/second (the product's own default is `ScannerApiRefillPerSecond = 1.0`, `TelegramClient/Models/ConfigParams.cs:24`). Packing tenants into one process does not raise any tenant's budget; it only shares CPU that is not the bottleneck.

Calibration target (see `02-target-architecture.md` §0): 10–50 independent deployments, one Telegram account each.

## Decision

**The deployment unit is one container serving exactly one Telegram account. We will not build a multi-tenant runtime.**

We will:

1. Keep every singleton process-wide and document the invariant explicitly in `Program.cs`.
2. Add a `TENANT_ID` label used only for logging, metrics and manifest generation — never for data scoping, so no code can come to depend on it as an isolation mechanism.
3. Build a thin fleet control plane (Milestone E3, ~1.5 ew) that renders per-tenant compose/Helm manifests, aggregates `/health` and `/metrics`, and centralises log shipping. **It never reads tenant data.**
4. Record a falsifiable reversal trigger (below) so this is a decision with an expiry condition rather than dogma.

**Reversal trigger:** revisit if we need **more than 200 concurrent tenants**, *or* if Telegram ships an official multi-account business API where sessions are server-side tokens instead of local key material. Either invalidates fact 1 or fact 4.

## Consequences

**Positive**

- Perfect tenant isolation for free. A crash, OOM, disk-fill, plugin hang or account ban affects exactly one customer.
- Zero data-isolation risk: there is no shared schema, so there is no query that can leak across tenants. Given the payload is a person's entire private Telegram archive, this removes an entire breach class.
- No new code. The 22 ew below is not spent.
- Per-tenant resource limits, upgrade cadence, backup schedule and data residency come from the container runtime.
- Keeps ADR-002 (SQLite) viable: one writer process is a fact, not an aspiration.

**Negative**

- Fixed overhead per tenant: ~200 MB RAM idle and one container. At 50 tenants that is ~10 GB — one commodity host. *(Estimate.)*
- Fleet operations (upgrade, certificate rotation, key rotation) must be automated or they become O(N) toil. Mitigated by E3; this is the main ongoing cost of the decision.
- Cross-tenant analytics require metric aggregation rather than a SQL query. Acceptable — the metrics are operational, not product.
- Onboarding a tenant means provisioning infrastructure, not inserting a row. Mitigated by manifest templating.

**Neutral**

- Per-tenant cost accounting becomes trivially accurate.
- Noisy-neighbour problems become impossible, so no fairness scheduler is needed.

## Alternatives considered

### A. Multi-tenant single process, N `TelegramApp` instances

Parameterise the session path, add `tenant_id` to all tables, run N clients in one host.

Rejected. Cost breakdown:

| Work | ew |
|---|---|
| `tenant_id` retrofit across 3 tables, 5 indexes, all 25 `MediaRepository` methods | 5 |
| N-session lifecycle: per-tenant login state machine, reconnect, session-file management | 6 |
| Per-tenant API budget isolation and fair scheduling | 3 |
| Authn/authz, tenant scoping, and an external security review | 5 |
| Data-isolation test suite (must prove no query crosses tenants) | 3 |
| **Total** | **22** |

Twenty-two engineer-weeks to eliminate roughly 10 GB of RAM, while *adding* a shared-fate failure domain that requires manual SMS re-auth to recover from. The trade is clearly negative at the target scale.

### B. Multi-tenant control plane with per-tenant worker pods

A shared API/UI over per-tenant workers. Rejected for now, not forever: it inherits most of alternative A's authz and scoping cost (the API is still multi-tenant) while keeping the per-tenant container overhead. It becomes attractive above ~200 tenants, which is the stated reversal trigger.

### C. Multi-tenant database only, single-tenant runtime

Shared Postgres, per-tenant containers. Rejected: it buys only central reporting, and it pays for that with cross-tenant data commingling (the exact risk we most want to avoid), a Postgres dependency contradicting ADR-002, and the same `tenant_id` retrofit as alternative A.

### D. Do nothing and call it single-tenant

Rejected. Today the single-tenant property is accidental — it follows from hardcoded paths (`TelegramApp.cs:101`) rather than a stated invariant, which means a well-meaning refactor could quietly break it. The decision above makes it explicit, labelled and testable.

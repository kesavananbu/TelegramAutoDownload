# ADR-003 — One Telegram gateway and one adaptive API budget

- **Status:** Proposed
- **Date:** 2026-10-01
- **Review:** Composer 2.5 re-run — read-only verification on `enterprise-readiness` @ `1c3753e`; no build
- **Deciders:** Architecture
- **Related:** ADR-001, ADR-005
- **Priority:** Highest. This is the defect most likely to produce the outcome the product exists to prevent.

## Context

Telegram enforces FLOOD_WAIT on a per-account request-rate basis. Exceeding it repeatedly escalates from temporary waits to account restriction. This is the system's binding constraint: capacity is governed by an external service's tolerance, not by our hardware.

The codebase contains the right mechanisms and wires them to the wrong places.

`TokenBucketRateLimiter`'s own doc comment says it gates "outbound Telegram API calls" and promises two instances, `TelegramApiLimiter` and `DownloadLimiter` (`Scanning/TokenBucketRateLimiter.cs:9-12`). In reality `ScanRateLimits` exposes only `TelegramApi` (`Scanning/ScanRateLimits.cs:9`), and that single bucket is consumed at exactly **one** call site: `Scanning/BootstrapScanner.cs:66`.

Five independent schedulers issue Telegram calls:

| # | Path | Gate | Max | Flood-aware? | Budgeted? | Evidence |
|---|---|---|---|---|---|---|
| 1 | Bootstrap pages | shared token bucket | cap 5, refill 1/s | yes | **yes** | `ScanRateLimits.cs:13-19`, `BootstrapScanner.cs:66` |
| 2 | Bootstrap job fan-out | `_running` dictionary | 10 | via #1 | via #1 | `BootstrapManager.cs:111` |
| 3 | Orchestrator download | `_inFlight` vs `DownloadThreads` | 10 | at dispatch only | **no** | `DownloadOrchestrator.cs:30,61,98` |
| 4 | Live-update download | `TelegramApp._semaphore` — a **separate** instance | 10 | **no** | **no** | `TelegramApp.cs:70,176,349` |
| 5 | Ad-hoc (`GetAllChats`, `ResolveInputPeerAsync`, `ChatDownloadTester`, `SyncHistoryAsync`) | none | unbounded | **no** | **no** | `TelegramApp.cs:742`; `Program.cs:255,430` |

Worst case: **30+ concurrent operations against one account, of which only the bootstrap slice consumes tokens.** Paths 3 and 4 are both sized from the same `DownloadThreads` value, so a user selecting "3 threads" authorises six concurrent downloads.

Two structural causes:

1. **`FloodWaitTracker` lives in the wrong assembly.** It is defined in `TelegramAutoDownload.Headless/Scanning/` and `TelegramClient` — which owns paths 4 and 5 — has no reference to it. A grep for `FloodWait` across `TelegramClient/*.cs` returns only the stateless parser `FloodWaitHelper`. The engine is *structurally incapable* of honouring a flood pause.
2. **The flood signal is not control input.** `FloodWaitTracker.WaitIfPausedAsync` pauses and resumes at the identical rate (`FloodWaitTracker.cs:54-65`). Nothing reduces the request rate after a flood, so the system deterministically re-enters the state that caused it.

## Decision

**Introduce `ITelegramGateway` as the single chokepoint for every Telegram API call, backed by one adaptive `ApiBudget` with priority lanes.**

### 1. One gateway

`ITelegramGateway` is defined in `TelegramClient` and is the only type permitted to touch `WTelegram.Client`. An architecture test (NetArchTest or equivalent) fails the build if any other type references `Client` members.

### 2. One budget, three lanes

| Lane | Workload | Reserved share | Rationale |
|---|---|---|---|
| `Interactive` | login, chat refresh, test-download | 20 %, burst-favoured | a human is waiting; volume is negligible |
| `Live` | update-pump metadata, new-message downloads | 50 % | freshness is the product |
| `Backfill` | bootstrap pages, catch-up, orchestrator re-fetch | 30 %, yields under pressure | unbounded volume, no deadline |

Unused share is lent to lower lanes and reclaimed on demand, so an idle account still backfills at full rate.

### 3. Adaptive rate control (AIMD)

Replace fixed-rate-plus-pause with a controller that treats FLOOD_WAIT as feedback:

- **Additive increase:** +0.1 req/s per 10 flood-free minutes, capped by the user's configured maximum.
- **Multiplicative decrease:** ×0.5 on every FLOOD_WAIT, floored at 0.2 req/s.
- Persist the learned rate across restarts so a container recycle does not re-run the discovery.

Derivation of the starting point, from code constants: the shipped safe default is 1 req/s sustained, burst 5 (`ConfigParams.cs:23-24`). One bootstrap page = 100 messages (`BootstrapScanner.cs:32`) → 100 messages/s discovery ≈ 360k messages/hour; a 1M-message channel bootstraps in ~2.8 h. Downloads need 1–2 metadata calls each (`TelegramApp.cs:208,223`); 10 concurrent at 30 s average = 0.33 items/s ≈ 0.67 req/s. Live traffic at 50k messages/day ≈ 0.58 req/s. **Total sustained demand ≈ 1.3 req/s against a 1 req/s default** — confirming that the fix is a shared, adaptive budget, not more workers.

### 4. Separate the byte transfer from the request budget

FLOOD_WAIT limits *request rate*, not bytes. File transfer moves to a `TransferPool` sized by NIC and disk (estimated ceiling ~10–20 MB/s per account — *community-observed, no published Telegram figure*). Only the metadata calls consume budget. This is the one place where more parallelism is both safe and useful.

### 5. Delete the redundant gates

`TelegramApp._semaphore` (`TelegramApp.cs:70`) is removed. The gateway becomes the only gate, so "3 threads" means three.

## Consequences

**Positive**

- The account-ban risk — the single existential risk — drops from "likely under aggressive settings" to "bounded by a controller that actively backs off".
- Configuration becomes honest. Today `DownloadThreads = 3` yields six concurrent downloads plus ungated calls.
- The adaptive controller finds each account's real ceiling instead of forcing users to guess with a slider that can be set 50× past safe (`Program.cs:233`).
- Observability: budget consumption per lane, flood events and the learned rate all become metrics, so operators can see *why* throughput is what it is.
- Priority lanes fix a real UX bug — today a login attempt during a bootstrap competes with backfill for the same unmanaged capacity.

**Negative**

- Every Telegram call site must be routed through the gateway: `TelegramApp.cs:208, 223, 742`, `BootstrapScanner.cs:73`, plus `ChatDownloadTester` and `SyncHistoryAsync`. Mechanical but broad.
- `FloodWaitTracker` and `TokenBucketRateLimiter` must move from `TelegramAutoDownload.Headless/Scanning/` into `TelegramClient`, inverting a current dependency direction.
- Aggregate throughput will **drop** initially, because today's behaviour is over-budget. This must be communicated as a fix, not a regression.
- AIMD adds tunables that can be misconfigured. Mitigated by shipping conservative constants and exposing the learned rate in the UI.

**Neutral**

- `TokenBucketRateLimiter`'s current per-acquire config read (`ScanRateLimits.cs:15` → `ConfigStore.Read()` → full file parse) disappears as a side effect, removing a hot-path file read (defect D-10).

## Alternatives considered

### A. Wire the existing bucket into the two ungated paths and stop there

Cheapest option (~0.5 ew) and it removes the worst of the over-subscription. Rejected as the endpoint but **adopted as step 1 of the migration** — it is shippable in days and retires most of the risk immediately. It is insufficient alone because it leaves the fixed rate that guarantees re-flooding, and leaves path 5 ungated.

### B. Make `FloodWaitTracker` a static singleton so `TelegramClient` can see it

Would work, and is tempting given `FileDownloadIndex` already sets that precedent (`TelegramClient/FileDownloadIndex.cs:21`). Rejected: statics are why the orchestrator and tracker are untestable today (ADR-009), and it would still not give priority lanes or adaptive control.

### C. Distributed rate limiting (Redis)

Rejected outright. The budget is per-account and ADR-001 fixes one process per account. An in-memory bucket is already the correct shape; it simply needs to be the only one.

### D. Fixed conservative rate, no adaptation

Set 0.5 req/s globally and never change it. Rejected: it guarantees safety at the cost of roughly doubling every bootstrap's duration, and it still cannot distinguish an interactive login from backfill. The adaptive controller costs perhaps 0.5 ew more and finds the real ceiling per account.

### E. Trust WTelegramClient's built-in flood handling

WTelegramClient does auto-retry some FLOOD_WAITs internally. Rejected as sufficient: it is per-call and has no view of aggregate rate across five schedulers, and it cannot implement priority lanes or cross-path backpressure. It is a floor, not a budget.

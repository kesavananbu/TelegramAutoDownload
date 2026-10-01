# ADR-005 — Leased durable queue with effectively-once download semantics

- **Status:** Proposed
- **Date:** 2026-10-01
- **Review:** Composer 2.5 re-run — read-only verification on `enterprise-readiness` @ `1c3753e`; no build
- **Deciders:** Architecture
- **Related:** ADR-002, ADR-003, ADR-010

## Context

The queue is the `Media` table (`Data/Schema/001_initial.sql:6-33`) with a six-value status enum: `pending | queued | in_progress | done | failed | skipped` (`:22`).

**What already works and must be preserved.** `PickQueuedAsync` is a genuinely atomic claim — `UPDATE … WHERE rowid IN (SELECT … LIMIT @limit) RETURNING *` (`Data/MediaRepository.cs:206-230`) — so two callers cannot claim the same row. Startup crash recovery resets stuck `in_progress` rows to `queued` (`Data/Database.cs:56-58`). `InsertPendingAsync` is idempotent via `INSERT OR IGNORE` on `(chat_id, message_id)` (`MediaRepository.cs:51`, PK at `001_initial.sql:32`).

**What is missing.**

1. **No lease.** `started_at` is written (`001_initial.sql:29`, set at `MediaRepository.cs:210`) but never *read*. A row claimed by a worker that then hangs — the exact failure mode of an uncancellable plugin (`BasePlugin/IBasePlugin.cs:8`) — stays `in_progress` until the next process restart. Recovery time is measured in days.

2. **`attempts` is written but never read.** It is incremented only on `Failed` (`MediaRepository.cs:79`). No code anywhere caps it or branches on it.

3. **FLOOD_WAIT retries are invisible.** The flood handler resets the row to `Queued` (`DownloadOrchestrator.cs:171`) — a status whose `SetStatusAsync` path does **not** increment `attempts` (`MediaRepository.cs:64-80`). Combined with `ORDER BY queued_at ASC, message_id ASC` (`MediaRepository.cs:213`), a row that reliably triggers FLOOD_WAIT is retried forever, at the front of the queue, consuming the API budget indefinitely.

4. **No backoff.** A just-failed row is immediately eligible again, and ordering puts it first.

5. **No dead-letter queue.** `failed` is the terminal state but is bulk-requeueable (`Program.cs:465`), so a permanently-broken row cannot be distinguished from a transiently-failed one.

6. **Completion is not atomic with the file.** The orchestrator writes status `done` (`DownloadOrchestrator.cs:148`) and then, separately, the path (`:150`). A crash between the two leaves a `done` row with no recorded path.

7. **Two parallel execution paths.** The orchestrator's own doc comment states that the live path bypasses the queue: "the live update path owns its own work and the `MediaTracker` advances those rows directly through `pending → in_progress → done` without ever entering `queued`" (`DownloadOrchestrator.cs:16-19`). This is the root of defects D-1 (a second concurrency governor), D-2 (`await Task.WhenAll` in the update pump, `TelegramApp.cs:495`), D-6.3 (fire-and-forget write ordering, `MediaTracker.cs:45,69`) and D-6.4 (chat-name-keyed maps, `MediaTracker.cs:28,62`).

8. **A slot-leak bug.** `Task.Run(() => DispatchAsync(row, stop), stop)` (`DownloadOrchestrator.cs:81`) is preceded by `Interlocked.Increment` at `:80`. If `stop` is already signalled, `Task.Run` returns a cancelled task **without invoking the delegate**, so the `finally` at `:187-189` never decrements and the row stays `in_progress`.

**On exactly-once.** It is not achievable. The process can die between the file write and the status commit, and there is no transaction spanning the filesystem and SQLite. The honest target is at-least-once delivery with idempotent, content-addressed completion — observationally effectively-once.

## Decision

**Adopt a leased queue with capped, backed-off retries, a dead-letter state, atomic completion, and a single execution path.**

### 1. Leases — migration `002_queue_leases.sql`

Five additive nullable columns, safe to apply to a live database:

```sql
ALTER TABLE Media ADD COLUMN lease_until      TEXT;
ALTER TABLE Media ADD COLUMN lease_owner      TEXT;
ALTER TABLE Media ADD COLUMN next_attempt_at  TEXT;
ALTER TABLE Media ADD COLUMN flood_deferrals  INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Media ADD COLUMN content_hash     TEXT;
CREATE INDEX IX_Media_Claimable ON Media(status, next_attempt_at, queued_at);
```

`PickQueuedAsync` gains `AND (next_attempt_at IS NULL OR next_attempt_at <= datetime('now'))` and sets `lease_until = now + 2× the expected transfer time` (floor 5 min). Long transfers heartbeat the lease every 60 s. A Janitor hosted service reclaims expired leases every 30 s — **recovery drops from "next restart" to under a minute**, and it fixes the slot-leak in item 8 as a side effect, without needing to reason about `Task.Run` cancellation semantics.

### 2. Capped retries with backoff

`attempts` is capped at 5, then the row moves to a new `dead` status. `next_attempt_at = now + min(2^attempts × 30s, 1h)` with ±20 % jitter. `flood_deferrals` is counted separately from `attempts` (a flood is not the item's fault) and capped at 20 to stop the infinite loop in item 3.

### 3. Dead-letter queue

New status `dead`. `GET /api/queue/dead` lists them with `last_error` and `attempts`; `POST /api/queue/{chatId}/{messageId}/revive` resets one explicitly. The existing bulk `retry-failed` (`Program.cs:463`) deliberately does **not** touch `dead` rows — reviving a poison item must be a conscious act.

### 4. Atomic completion

Reuse the pattern the codebase already implements correctly in `FileDownloadIndex.SaveInternal` (`TelegramClient/FileDownloadIndex.cs:115-118`): write to `{final}.part`, `fsync`, atomically rename, **then** commit `status='done'` together with `downloaded_path` and `content_hash` in **one** statement — replacing the current two-write sequence at `DownloadOrchestrator.cs:148,150`. A crash before the commit leaves a complete file and a `queued` row; the next attempt's preflight check (`ExistingDownloadValidator.cs:28`) finds it and completes idempotently.

### 5. One execution path

The update pump **only enqueues**. It calls `InsertPendingAsync` with `status='queued'` and returns immediately; it never awaits a download. The orchestrator becomes the sole executor.

This single change retires four defects at once: it removes the second concurrency governor (D-1), removes the inline `await Task.WhenAll(tasks)` that blocks WTelegramClient's update dispatch (D-2, `TelegramApp.cs:495`), and deletes the `MediaTracker` event-hijacking design (`MediaTracker.cs:39-58`) along with its write-ordering race and unbounded name-keyed dictionaries (D-6.3, D-6.4).

### 6. Priority

Live messages enqueue with a higher priority than backfill, so a fresh message is not stuck behind a two-million-row bootstrap. This maps onto ADR-003's `Live` and `Backfill` lanes.

## Consequences

**Positive**

- A hung worker costs under a minute of capacity instead of requiring a restart.
- Poison items stop consuming the scarcest resource in the system — the API budget — and become visible in a dead-letter queue instead of silently looping.
- Deleting the dual execution path removes four defects with one structural change and makes the update pump non-blocking, which in turn removes the `UpdateChannelTooLong` feedback loop described in D-2.
- Backoff with jitter turns a thundering retry herd into a spread load.
- Atomic completion makes a crash mid-download a no-op rather than a torn state.
- The queue becomes observable: depth by status, lease reclaims, dead-letter count, retry distribution.

**Negative**

- Five new columns and a new index. Additive and nullable, so the migration is safe to roll forward on a live database, but it must still be written, tested and made reversible (ADR-007).
- Lease heartbeating adds a write per active download per minute — at most 10/min. Negligible against the 101 writes/s peak in ADR-002.
- Changing the update pump to enqueue-only alters user-visible timing: a new message now appears in the queue first and downloads a moment later, rather than downloading inline. The dashboard must show queue position so this reads as progress, not a stall.
- `MediaTracker` is deleted. Anything depending on its event chaining (`MediaTracker.cs:39-58`) must be reworked — in practice only the DB writes, which move to the orchestrator.

**Neutral**

- `Database.InitializeAsync`'s startup sweep (`Database.cs:56-58`) stays as a belt-and-braces recovery for leases that outlived a crash.

## Alternatives considered

### A. Keep the two execution paths and fix them independently

Add leases and caps to the orchestrator; separately make `MediaTracker`'s writes ordered and its dictionaries bounded. Rejected: it preserves the second concurrency governor (D-1) and the blocking update pump (D-2), which are the two most dangerous defects. It also costs more in total, because every future queue feature must then be implemented twice.

### B. External broker (RabbitMQ / Redis Streams / Kafka)

A broker provides leases, DLQ and retries out of the box. Rejected: one consumer process (ADR-001), ~0.33 items/s, and a few million rows. `UPDATE … RETURNING` already gives atomic claim. A broker adds a container per deployment across 10–50 deployments, a second durability domain, and a two-phase-commit problem between broker acks and the SQLite row that the UI reads.

### C. `attempts`-only, no separate `flood_deferrals`

Simpler. Rejected because it conflates two different signals: a flood means "the system is going too fast", not "this item is bad". Counting them together would dead-letter perfectly good items during a flood storm.

### D. Genuine exactly-once via a filesystem/DB transaction

Rejected as unachievable without a transactional filesystem. Atomic rename plus an idempotent preflight check is the standard, and sufficient, substitute.

### E. Shorter fixed retry interval, no exponential backoff

Rejected. The dominant failure is FLOOD_WAIT, where retrying sooner is actively harmful — it deepens the flood. Backoff is not a nicety here; it is a safety mechanism.

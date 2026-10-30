# Task T1 — Unified Telegram API budget

**Branch (intended):** `er/t1-api-budget` (not committed — controller will commit by path)

## Summary

Introduced `ITelegramApiBudget` in `TelegramClient` with a no-op default for WPF. Headless `TelegramApiBudget` composes the existing token bucket + `FloodWaitTracker` + shared download slots. `TelegramApp` acquires API tokens and download slots on the live path, catch-up, and orchestrator entry points; update dispatch no longer blocks the pump on full downloads. Flood-wait pause persists to `{DATA_DIR}/flood-wait-state.json`.

## Files created

- `TelegramClient/ITelegramApiBudget.cs`
- `TelegramClient/NullTelegramApiBudget.cs`
- `TelegramClient/TelegramApp.ApiBudget.cs`
- `TelegramAutoDownload.Headless/Scanning/TelegramApiBudget.cs`
- `TelegramAutoDownload.Headless/Scanning/FloodWaitStatePersistence.cs`
- `TelegramAutoDownload.Headless.Tests/TelegramApiBudgetTests.cs`
- `docs/assessment/task-t1-report.md`

## Files modified

- `TelegramClient/TelegramApp.cs` — budget-aware API wrappers, shared download slots, non-blocking `Client_OnUpdates`, catch-up/sync/manual paths
- `TelegramAutoDownload.Headless/Scanning/FloodWaitTracker.cs` — `Report(TimeSpan)`, disk restore/save, clear on expiry
- `TelegramAutoDownload.Headless/Scanning/DownloadOrchestrator.cs` — uses `ITelegramApiBudget` for capacity and flood reporting
- `TelegramAutoDownload.Headless/HeadlessHost.cs` — wires `ApiBudget` into `TelegramApp` (see shared hunks)
- `TelegramAutoDownload.Headless/Program.cs` — DI registration (see shared hunks)

## Shared-file hunks

**`Program.cs`** (after `ScanRateLimits` registration): add `TelegramApiBudget` singleton and `ITelegramApiBudget` factory resolving the same instance.

**`HeadlessHost.cs`**: optional `ITelegramApiBudget?` constructor parameter; in `EnsureTelegramAsync`, assign `Telegram.ApiBudget = _apiBudget` before login wait.

## Build / test

```powershell
dotnet build TelegramClient\TelegramClient.csproj --artifacts-path D:\tad-artifacts\t1 -c Release
dotnet build TelegramAutoDownload.Headless\TelegramAutoDownload.Headless.csproj --artifacts-path D:\tad-artifacts\t1 -c Release
dotnet test TelegramAutoDownload.Headless.Tests\TelegramAutoDownload.Headless.Tests.csproj --artifacts-path D:\tad-artifacts\t1-run4 -c Release --filter FullyQualifiedName~TelegramApiBudgetTests
```

- TelegramClient + Headless: **build succeeded**
- `TelegramApiBudgetTests`: **4 passed, 0 failed**
- Full `TelegramAutoDownload.Headless.Tests`: not re-verified to completion in this session (parallel testhost/artifact lock contention); baseline was 22 tests before T1 additions.

## Open risks

- Step 1 only: no full `ITelegramGateway`, priority lanes, or AIMD (ADR-003 long-term).
- Bootstrap/test-download paths still call `Client` directly but share the same token bucket via `ScanRateLimits` / `FloodWaitTracker`.
- Some ad-hoc API paths (e.g. `GetAllChats`, member export) remain unbudgeted when `ApiBudget` is set.
- Concurrent multi-agent edits on one checkout may require controller merge review.

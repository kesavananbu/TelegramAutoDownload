# Task 1 — Test harness (Milestone 1)

**Branch:** `enterprise-readiness`  
**Date:** 2026-10-01  
**Scope:** Reproducible restore, tracked solution, cross-platform headless characterization tests.

## Deliverables

| Item | Status | Location |
|------|--------|----------|
| NuGet feed pin (nuget.org only) | Done | `NuGet.config` |
| Tracked solution (README name) | Done | `RunnerApp.sln` (12 projects) |
| Headless test project `net8.0` | Done | `TelegramAutoDownload.Headless.Tests/` |
| coverlet collector | Done | `TelegramAutoDownload.Headless.Tests.csproj` |
| `WebApplicationFactory` entry point | Done | `public partial class Program { }` in `TelegramAutoDownload.Headless/Program.cs` |
| Characterization tests | Done | See table below |

## Production code changes

Only change outside tests/infrastructure:

- `TelegramAutoDownload.Headless/Program.cs` — added `public partial class Program { }` for integration testing (no behavioural change).

## Test inventory

| Area | Tests | File |
|------|------:|------|
| `WebAuthService` | 6 | `WebAuthServiceTests.cs` |
| `Database` migrations / crash recovery | 3 | `DatabaseMigrationTests.cs` |
| `MediaRepository` queue/dedup | 5 | `MediaRepositoryTests.cs` |
| `HeadlessLogReader` | 4 | `HeadlessLogReaderTests.cs` |
| API auth smoke (`WebApplicationFactory`) | 4 | `WebAuthApiSmokeTests.cs` |
| **Total** | **22** | |

## Verification commands

```powershell
dotnet restore RunnerApp.sln
dotnet build RunnerApp.sln -c Release
dotnet test TelegramAutoDownload.Headless.Tests/TelegramAutoDownload.Headless.Tests.csproj -c Release `
  --collect:"XPlat Code Coverage" --results-directory artifacts/headless-test
```

**Last run (this task):** 22 passed, 0 failed, ~1 s test body (+ host startup for integration tests).

## Coverage (Headless production assembly)

Measured with coverlet (`XPlat Code Coverage`) on `TelegramAutoDownload.Headless.Tests` only:

| Metric | Value |
|--------|------:|
| **TelegramAutoDownload.Headless line coverage** | **33.86%** |
| Branch coverage (Headless) | 20.87% |

The collector report also includes referenced assemblies (`TelegramClient`, plugins, etc.); the figure above is the `TelegramAutoDownload.Headless` package line-rate from Cobertura.

Baseline comparison: Headless was **0%** with no test reference (`docs/assessment/00-baseline.md`).

## Notes / follow-ups

- `HeadlessTestRunEnvironment` uses a `[ModuleInitializer]` to set `DATA_DIR` before `HeadlessPaths` static initialization.
- Integration tests use a shared `HeadlessWebApplicationFactory` with auth enabled; login probe may log missing `APP_ID`/`API_HASH` — expected in CI/local without Telegram credentials.
- Desktop unit tests (`TelegramAutoDownload.Tests`, `net8.0-windows`) remain separate; this project is cross-platform for Linux CI on the headless surface.
- Milestone 1 item 3 (validate `.github/workflows/`) is **not** part of Task 1.

## Commits (Task 1)

Recorded after push — see git log on `enterprise-readiness` for hashes tagged in the controller handoff.

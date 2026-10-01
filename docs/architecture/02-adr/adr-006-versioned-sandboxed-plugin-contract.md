# ADR-006 — Versioned, supervised, out-of-process plugin contract

- **Status:** Proposed
- **Date:** 2026-09-30
- **Deciders:** Architecture
- **Related:** ADR-004, ADR-005

## Context

The plugin contract is four members with no version, no cancellation and no capability declaration:

```csharp
public interface IBasePlugin            // BasePlugin/IBasePlugin.cs:3-9
{
    string PluginName { get; }
    int Priority { get; }
    bool CanHandle(Config config);
    Task<ResultExecute> ExecuteAsync(Config config);   // :8 — no CancellationToken
}
```

Discovery and loading (`Factory/Service/MessageTextFactoryService.cs:27-86`): scan `{BaseDirectory}/Plugins/*/*.dll`, `Assembly.LoadFrom` each into the **default** `AssemblyLoadContext` (`:64`), reflect for types deriving from `BasePlugin<>` (`:67-70`). `ReflectionTypeLoadException` is swallowed with a comment and no log (`:72-75`). Execution creates a **fresh instance per URL line per message** via `Activator.CreateInstance` (`:112-117`) and hands it the raw download root as a string (`:124`).

The four failure modes the brief asks about, and what happens today:

| Failure | Today | Evidence |
|---|---|---|
| **Hangs** | The slot is held forever. `Interlocked.Increment(ref _inFlight)` at `DownloadOrchestrator.cs:80`; the `finally` that decrements at `:188` never runs. Same for `TelegramApp._semaphore` at `:349`. No timeout exists anywhere in the chain. Capacity degrades permanently until restart. | `IBasePlugin.cs:8` |
| **Crashes** | Default ALC means a `StackOverflowException`, `Environment.Exit` or unhandled background-thread exception terminates the whole process — dashboard, orchestrator and Telegram session together. | `MessageTextFactoryService.cs:64` |
| **Fills the disk** | `Config.PathSaveFile` is the raw download root (`:124`). No quota, no free-space precondition, no accounting. `TorrentPlugin` can write unbounded data. | `MessageTextFactoryService.cs:124` |
| **Version skew** | An old plugin either fails at `GetTypes()` and is silently swallowed, or half-loads with a partial type list. | `MessageTextFactoryService.cs:72-75` |

`BasePlugin<T>` does provide `WithRetryAsync` with backoff (`BasePlugin/BasePlugin.cs:28-47`), which is good, but it is opt-in and only covers retries — not hangs, crashes or quotas.

Deployment note: plugins *are* correctly packaged into the container. `TelegramAutoDownload.Headless.csproj:39-56` copies them into `$(PublishDir)Plugins/*` after publish, and the Dockerfile publishes (`Dockerfile:28-29`). The mechanism is brittle — it reaches into sibling `bin/$(Configuration)/net8.0/` paths — but it works.

## Decision

**Ship plugin contract v2: versioned, cancellable, capability-declaring, executed in a supervised child process. Support v1 for one release via an adapter.**

### 1. Contract v2

```csharp
public interface IPlugin
{
    static abstract PluginManifest Manifest { get; }
    bool CanHandle(PluginRequest request);
    Task<PluginResult> ExecuteAsync(PluginRequest request, IPluginContext ctx, CancellationToken ct);
}

public sealed record PluginManifest(
    string Name,
    Version Version,
    int ApiVersion,                    // host refuses to load on mismatch
    PluginCapabilities Capabilities,   // [Flags] Network | Subprocess | WriteDownloads | ReadConfig
    ResourceLimits Limits);

public sealed record ResourceLimits(
    TimeSpan MaxRuntime,     // default 30 min
    long     MaxBytesWritten,// default 50 GB
    int      MaxMemoryMb);   // default 1024
```

`IPluginContext` replaces the raw `PathSaveFile` string (`MessageTextFactoryService.cs:124`) with a quota-enforcing writer, a progress reporter and a scoped logger. **The plugin never receives a filesystem root.**

### 2. Out-of-process host with a supervisor

`tad-pluginhost` (ADR-004) runs all plugin code. `PluginSupervisor` in `tad-server` enforces:

- **Runtime timeout** — cancel via `ct` at `MaxRuntime`, then `SIGKILL` after a 30 s grace period.
- **Memory cap** — Job Objects (Windows) / cgroup v2 (Linux) at `MaxMemoryMb`.
- **Disk quota** — `IPluginContext` counts bytes and trips at `MaxBytesWritten`; the host additionally refuses dispatch below a configured free-space floor.
- **Crash handling** — detect exit, fail the in-flight item with a specific error, restart the host, circuit-break after 3 crashes in 5 minutes and surface it in `/api/plugins`.
- **Version gate** — `ApiVersion` mismatch means refuse to load and report *why*, replacing today's silent swallow.

### 3. Instance lifetime

One plugin instance per host-process lifetime, not one per URL line (`MessageTextFactoryService.cs:112-117`). Plugins get somewhere to hold a connection pool or their own rate limiter.

### 4. Transport

Unix domain socket / named pipe, framed protobuf. Not HTTP, not gRPC-over-TCP: no port, no discovery, no network exposure.

## Consequences

**Positive**

- All four failure modes in the table are contained. Each becomes a CI test: a plugin that sleeps forever, one that calls `Environment.Exit`, one that writes 1 TB, and one built against `ApiVersion - 1`.
- The hang defect is fixed in **step 1 of the migration**, before any process split, because the v1 adapter can enforce a timeout on today's plugins with no plugin changes.
- Version skew becomes a diagnosable, reported condition instead of a silent no-op.
- Third-party plugins become plausible: capability declarations and resource limits are what make untrusted code acceptable.
- Large allocations (`yt-dlp` buffers, torrent pieces) leave the server's heap.

**Negative**

- IPC serialisation cost per download. Irrelevant against 0.33 items/s and 30 s transfers.
- Progress reporting must cross the boundary. The existing `Action<...>` callbacks (`BasePlugin.cs:13,19`) become streamed messages — a real but bounded change.
- Two processes to build, version and ship in lockstep, with an `ApiVersion` handshake at startup.
- Job Objects and cgroup v2 are different implementations with different edge cases; both need testing. This is the largest uncertainty in the estimate.
- Plugin authors must migrate. Mitigated by the one-release v1 adapter and a deprecation warning surfaced in `/api/plugins`.

**Neutral**

- The brittle MSBuild copy (`TelegramAutoDownload.Headless.csproj:39-56`) can be replaced with a proper plugin package layout as part of C3, or left alone.

## Alternatives considered

### A. Add `CancellationToken` to v1 and stop there

~0.5 ew and it fixes hangs — the highest-frequency failure. Rejected as the endpoint but **adopted as step 1** of the migration, because it is shippable immediately. Insufficient alone: it does not contain crashes, disk exhaustion or version skew, and `ct` cannot interrupt a thread blocked in native code, which is what a wedged `yt-dlp` subprocess actually is.

### B. Collectible `AssemblyLoadContext`

In-process isolation with unload support, no IPC. Rejected: it cannot enforce a memory cap, cannot survive `Environment.Exit` or `StackOverflowException`, and cannot reliably abort native-blocked threads. It addresses only version skew, which is the least dangerous of the four modes.

### C. WASM / WASI sandbox

The strongest isolation available. Rejected because the plugins' entire purpose is subprocess execution (`yt-dlp`) and raw sockets (BitTorrent) — capabilities WASM specifically denies. Adopting it would mean rewriting every plugin to *not* do the thing it exists to do.

### D. Container-per-plugin-invocation

Strong isolation and familiar tooling. Rejected: requires a container runtime inside the deployment (docker-in-docker or a privileged socket), adds seconds of startup per invocation, and complicates the volume mount for `/downloads`. Cost far exceeds a supervised child process.

### E. Keep plugins in-process and just add quotas

Rejected. Quotas address one of four failure modes, and the remaining three (hang, crash, version skew) are the ones that cause outages rather than mere disk pressure.

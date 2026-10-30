using System;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramClient;

/// <summary>Workload hint for future lane prioritization; all kinds share one bucket today.</summary>
public enum ApiCallKind
{
    Interactive,
    Live,
    Backfill,
}

/// <summary>
/// Unified gate for Telegram API rate and shared download concurrency.
/// Headless supplies a real implementation; WPF runs with <see cref="NullTelegramApiBudget"/>.
/// </summary>
public interface ITelegramApiBudget
{
    /// <summary>Blocks until one API token is available (and any flood-wait pause has elapsed).</summary>
    Task AcquireAsync(ApiCallKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// Holds one shared download slot for the lifetime of the returned handle
    /// (live updates and orchestrator share the same pool).
    /// </summary>
    Task<IDisposable> AcquireDownloadSlotAsync(CancellationToken cancellationToken = default);

    /// <summary>Extends the global flood-wait pause after Telegram returns FLOOD_WAIT.</summary>
    void ReportFloodWait(TimeSpan duration);

    /// <summary>Slots currently free in the download concurrency pool.</summary>
    int AvailableDownloadSlots { get; }

    /// <summary>Hot-reload when <c>DownloadThreads</c> changes.</summary>
    void ConfigureDownloadConcurrency(int maxConcurrent);
}

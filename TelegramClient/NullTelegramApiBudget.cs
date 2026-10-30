using System;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramClient;

/// <summary>No-op budget — preserves legacy WPF behaviour (local semaphore only).</summary>
public sealed class NullTelegramApiBudget : ITelegramApiBudget
{
    public static NullTelegramApiBudget Instance { get; } = new();

    private NullTelegramApiBudget() { }

    public Task AcquireAsync(ApiCallKind kind, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IDisposable> AcquireDownloadSlotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IDisposable>(NoOpRelease.Instance);

    public void ReportFloodWait(TimeSpan duration) { }

    public int AvailableDownloadSlots => int.MaxValue;

    public void ConfigureDownloadConcurrency(int maxConcurrent) { }

    private sealed class NoOpRelease : IDisposable
    {
        public static NoOpRelease Instance { get; } = new();
        public void Dispose() { }
    }
}

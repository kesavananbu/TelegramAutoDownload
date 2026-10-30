using TelegramClient;

namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>
/// Headless implementation: one token bucket + flood pause + shared download slots.
/// </summary>
public sealed class TelegramApiBudget : ITelegramApiBudget
{
    private readonly TokenBucketRateLimiter _apiLimiter;
    private readonly FloodWaitTracker _floodWait;
    private readonly object _downloadLock = new();
    private SemaphoreSlim _downloadSlots = new(1, 1);
    private int _downloadMax = 1;

    // Takes ConfigStore, not HeadlessHost: HeadlessHost depends on this budget, so the reverse would be a DI cycle.
    public TelegramApiBudget(ScanRateLimits limits, FloodWaitTracker floodWait, ConfigStore config)
    {
        _apiLimiter = limits.TelegramApi;
        _floodWait = floodWait;
        ConfigureDownloadConcurrency(Math.Max(1, config.Read().DownloadThreads));
    }

    public int AvailableDownloadSlots
    {
        get
        {
            lock (_downloadLock)
                return _downloadSlots.CurrentCount;
        }
    }

    public Task AcquireAsync(ApiCallKind kind, CancellationToken cancellationToken = default) =>
        _apiLimiter.WaitAsync(cancellationToken);

    public async Task<IDisposable> AcquireDownloadSlotAsync(CancellationToken cancellationToken = default)
    {
        SemaphoreSlim sem;
        lock (_downloadLock)
            sem = _downloadSlots;

        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new DownloadSlotRelease(sem);
    }

    public void ReportFloodWait(TimeSpan duration) =>
        _floodWait.Report(duration, "telegram-api-budget");

    public void ConfigureDownloadConcurrency(int maxConcurrent)
    {
        var max = Math.Max(1, maxConcurrent);
        lock (_downloadLock)
        {
            if (_downloadMax == max)
                return;

            _downloadMax = max;
            _downloadSlots = new SemaphoreSlim(max, max);
        }
    }

    private sealed class DownloadSlotRelease : IDisposable
    {
        private SemaphoreSlim? _sem;

        public DownloadSlotRelease(SemaphoreSlim sem) => _sem = sem;

        public void Dispose()
        {
            _sem?.Release();
            _sem = null;
        }
    }
}

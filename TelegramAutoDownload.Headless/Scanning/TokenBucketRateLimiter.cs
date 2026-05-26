namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>
/// Classic token-bucket rate limiter. Acquires one token per allowed operation;
/// callers wait until a token is available. Capacity and refill rate are
/// re-read from <see cref="HeadlessHost"/> on every WaitAsync call so the UI
/// can adjust limits live without restarting anything.
///
/// Two instances are used in Phase 3:
///   - <c>TelegramApiLimiter</c> — gates outbound Telegram API calls (bootstrap pages).
///   - <c>DownloadLimiter</c>    — caps concurrent downloads via the existing
///                                  <c>DownloadThreads</c> setting in <see cref="TelegramAutoDownload.Models.ConfigParams"/>.
///
/// Resolution: refill happens lazily on acquire — no background timer, no thread.
/// </summary>
public sealed class TokenBucketRateLimiter
{
    private readonly Func<RateLimitOptions> _readOptions;
    private readonly FloodWaitTracker? _floodWait;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private double _tokens;
    private DateTimeOffset _lastRefill = DateTimeOffset.UtcNow;

    public TokenBucketRateLimiter(Func<RateLimitOptions> readOptions, FloodWaitTracker? floodWait = null)
    {
        _readOptions = readOptions;
        _floodWait   = floodWait;
        _tokens = readOptions().Capacity;
    }

    /// <summary>Blocks until a token is available, then consumes one.</summary>
    public async Task WaitAsync(CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (_floodWait != null)
                await _floodWait.WaitIfPausedAsync(ct).ConfigureAwait(false);

            int waitMs;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var opts = _readOptions();
                Refill(opts);
                if (_tokens >= 1.0)
                {
                    _tokens -= 1.0;
                    return;
                }
                var deficit = 1.0 - _tokens;
                waitMs = (int)Math.Ceiling(deficit / opts.RefillPerSecond * 1000);
            }
            finally { _gate.Release(); }

            // Sleep outside the lock so concurrent callers can also acquire/wait.
            await Task.Delay(Math.Max(50, waitMs), ct).ConfigureAwait(false);
        }
    }

    private void Refill(RateLimitOptions opts)
    {
        var now = DateTimeOffset.UtcNow;
        var elapsed = (now - _lastRefill).TotalSeconds;
        if (elapsed <= 0) return;
        _tokens     = Math.Min(opts.Capacity, _tokens + elapsed * opts.RefillPerSecond);
        _lastRefill = now;
    }
}

public sealed record RateLimitOptions(double Capacity, double RefillPerSecond);

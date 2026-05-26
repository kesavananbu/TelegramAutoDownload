namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>
/// Holds hot-reloadable rate limiters that read live values from
/// <see cref="HeadlessHost"/> on every acquire — UI changes apply immediately.
/// </summary>
public sealed class ScanRateLimits
{
    public TokenBucketRateLimiter TelegramApi { get; }

    public ScanRateLimits(HeadlessHost host, FloodWaitTracker floodWait)
    {
        TelegramApi = new TokenBucketRateLimiter(() =>
        {
            var cfg = host.ReadConfig();
            return new RateLimitOptions(
                Math.Max(1.0, cfg.ScannerApiCapacity),
                Math.Max(0.1, cfg.ScannerApiRefillPerSecond));
        }, floodWait);
    }
}

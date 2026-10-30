namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>
/// Holds hot-reloadable rate limiters that read live values from
/// <see cref="ConfigStore"/> on every acquire — UI changes apply immediately.
/// </summary>
public sealed class ScanRateLimits
{
    public TokenBucketRateLimiter TelegramApi { get; }

    // Takes ConfigStore, not HeadlessHost: HeadlessHost depends on TelegramApiBudget, which depends on this.
    public ScanRateLimits(ConfigStore config, FloodWaitTracker floodWait)
    {
        TelegramApi = new TokenBucketRateLimiter(() =>
        {
            var cfg = config.Read();
            return new RateLimitOptions(
                Math.Max(1.0, cfg.ScannerApiCapacity),
                Math.Max(0.1, cfg.ScannerApiRefillPerSecond));
        }, floodWait);
    }
}

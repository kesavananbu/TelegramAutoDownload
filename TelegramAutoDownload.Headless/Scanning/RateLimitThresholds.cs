namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>UI warning thresholds for rate-limit settings (mirrored in wwwroot/app.js).</summary>
public static class RateLimitThresholds
{
    public const double ScannerCapacityDefault = 5.0;
    public const double ScannerRefillDefault   = 1.0;
    public const int    DownloadThreadsDefault = 3;

    public const double ScannerCapacityMax = 100.0;
    public const double ScannerRefillMax     = 50.0;
    public const int    DownloadThreadsMax   = 10;

    public const double ScannerCapacityWarn  = 20.0;
    public const double ScannerRefillWarn    = 5.0;
    public const int    DownloadThreadsWarn  = 6;

    public const double ScannerCapacityDanger = 50.0;
    public const double ScannerRefillDanger   = 10.0;
    public const int    DownloadThreadsDanger = 8;

    public const int MaxParallelBootstrapsDefault = 3;
    public const int MaxParallelBootstrapsCap     = 10;
}

using System;
using System.IO;

namespace TelegramAutoDownload.Headless;

/// <summary>
/// Container-friendly paths. Override the root via DATA_DIR env var
/// (typical Docker setup: bind-mount a host directory to /data and set DATA_DIR=/data).
/// </summary>
public static class HeadlessPaths
{
    public static readonly string DataDir =
        Environment.GetEnvironmentVariable("DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "TelegramAutoDownload");

    public static readonly string DownloadsDir =
        Environment.GetEnvironmentVariable("DOWNLOADS_DIR")
        ?? Path.Combine(DataDir, "downloads");

    public static string ConfigFile  => Path.Combine(DataDir, "config.json");
    public static string DatabaseFile => Path.Combine(DataDir, "tad.db");
    public static string LogsDir     => Path.Combine(DataDir, "logs");
    public static string ToolsDir    => Path.Combine(DataDir, "tools");

    /// <summary>
    /// Same directory WTelegramClient uses for <c>session.dat</c> and the WPF
    /// <c>downloaded_ids.json</c> index (<see cref="TelegramClient.TelegramApp"/>).
    /// On Linux/Docker, set <c>XDG_CONFIG_HOME</c> (e.g. to <c>/data</c>) so this resolves
    /// under the mounted volume — not under <see cref="DataDir"/> when <c>DATA_DIR</c> is set.
    /// </summary>
    public static string WTelegramConfigDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TelegramAutoDownload");

    public static string SessionFile => Path.Combine(WTelegramConfigDir, "session.dat");

    /// <summary>Legacy dedup index written by <c>FileDownloadIndex</c> in the WPF/live path.</summary>
    public static string LegacyDedupIndexFile =>
        Path.Combine(WTelegramConfigDir, "downloaded_ids.json");

    static HeadlessPaths()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(DownloadsDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(ToolsDir);
    }
}

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
    public static string SessionFile => Path.Combine(DataDir, "session.dat");
    public static string DatabaseFile => Path.Combine(DataDir, "tad.db");
    public static string LogsDir     => Path.Combine(DataDir, "logs");
    public static string ToolsDir    => Path.Combine(DataDir, "tools");

    /// <summary>
    /// Legacy dedup index path from the WPF app (kept around so we can import it
    /// on first launch). Honours <c>XDG_CONFIG_HOME</c> on Linux, so a Docker run
    /// with <c>XDG_CONFIG_HOME=/data</c> matches existing setups.
    /// </summary>
    public static string LegacyDedupIndexFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "TelegramAutoDownload", "downloaded_ids.json");

    static HeadlessPaths()
    {
        Directory.CreateDirectory(DataDir);
        Directory.CreateDirectory(DownloadsDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(ToolsDir);
    }
}

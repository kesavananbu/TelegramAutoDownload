using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace TelegramAutoDownload.Services
{
    /// <summary>
    /// Ensures yt-dlp.exe and ffmpeg.exe are present and up-to-date.
    /// On startup: downloads if missing, then checks GitHub for newer versions and updates silently.
    /// yt-dlp auto-discovers ffmpeg when both exes are in the same folder.
    /// </summary>
    public static class YtdlpService
    {
        private const string GitHubApiUrl =
            "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";

        // yt-dlp/FFmpeg-Builds provides ffmpeg builds specifically maintained for yt-dlp compatibility
        private const string FfmpegZipUrl =
            "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

        /// <summary>Writable tools folder in %APPDATA%\TelegramAutoDownload\tools\</summary>
        public static string ToolsFolder => AppPaths.ToolsDir;

        public static string ExePath    => Path.Combine(ToolsFolder, "yt-dlp.exe");
        public static string FfmpegPath => Path.Combine(ToolsFolder, "ffmpeg.exe");

        /// <summary>
        /// Status message updated during update checks (for MainWindow footer display).
        /// </summary>
        public static string StatusMessage { get; private set; } = string.Empty;
        public static event Action<string>? StatusChanged;

        /// <summary>
        /// Ensures yt-dlp.exe and ffmpeg.exe exist and checks for yt-dlp updates. Never throws.
        /// </summary>
        public static async Task EnsureAsync()
        {
            await EnsureYtdlpAsync();
            await EnsureFfmpegAsync();
        }

        private static async Task EnsureYtdlpAsync()
        {
            try
            {
                // If yt-dlp is bundled with the installer (read-only install dir), copy it to writable AppData
                if (!File.Exists(ExePath))
                {
                    var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "yt-dlp.exe");
                    if (File.Exists(bundled))
                        File.Copy(bundled, ExePath, overwrite: false);
                }

                string? latestVersion = null;
                string? downloadUrl = null;

                // Query GitHub for the latest release
                try
                {
                    using var http = new HttpClient();
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("TelegramAutoDownload/2.2");
                    http.Timeout = TimeSpan.FromSeconds(15);

                    var json = await http.GetStringAsync(GitHubApiUrl);
                    var obj = JObject.Parse(json);
                    latestVersion = obj["tag_name"]?.ToString()?.TrimStart('v');
                    downloadUrl = obj["assets"]?
                        .FirstOrDefault(a => a["name"]?.ToString() == "yt-dlp.exe")?
                        ["browser_download_url"]?.ToString();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "yt-dlp: Could not fetch GitHub release info");
                }

                // Download if missing
                if (!File.Exists(ExePath))
                {
                    SetStatus("yt-dlp: Downloading...");
                    await DownloadFileAsync(
                        downloadUrl ?? "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe",
                        ExePath);
                    SetStatus("yt-dlp: Ready");
                    return;
                }

                // Check current version and update if needed
                string? currentVersion = GetYtdlpVersion();
                if (currentVersion == null || latestVersion == null) return;

                if (string.Compare(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0
                    && downloadUrl != null)
                {
                    SetStatus($"yt-dlp: Updating {currentVersion} → {latestVersion}...");
                    Log.Information("yt-dlp: updating from {Current} to {Latest}", currentVersion, latestVersion);
                    await DownloadFileAsync(downloadUrl, ExePath);
                    SetStatus($"yt-dlp: Updated to {latestVersion}");
                    Log.Information("yt-dlp: update complete");
                }
                else
                {
                    SetStatus($"yt-dlp: {currentVersion} (up to date)");
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "yt-dlp: EnsureAsync failed");
                SetStatus("yt-dlp: Update check failed");
            }
        }

        /// <summary>
        /// Ensures ffmpeg.exe is present in the tools folder alongside yt-dlp.exe.
        /// yt-dlp auto-discovers ffmpeg in the same directory, enabling video+audio stream merging.
        /// Downloads from yt-dlp/FFmpeg-Builds if missing; never auto-updates (ffmpeg rarely needs it).
        /// </summary>
        private static async Task EnsureFfmpegAsync()
        {
            if (File.Exists(FfmpegPath)) return;

            // Check if bundled with installer
            var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg.exe");
            if (File.Exists(bundled))
            {
                File.Copy(bundled, FfmpegPath, overwrite: false);
                Log.Information("ffmpeg: copied from installer bundle");
                return;
            }

            try
            {
                SetStatus("ffmpeg: Downloading (needed for video merging)...");
                Log.Information("ffmpeg: downloading from yt-dlp/FFmpeg-Builds");

                // Download the zip and extract only ffmpeg.exe — the zip is ~90 MB
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("TelegramAutoDownload/2.2");
                http.Timeout = TimeSpan.FromMinutes(15);

                var zipBytes = await http.GetByteArrayAsync(FfmpegZipUrl);

                using var ms = new MemoryStream(zipBytes);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

                // The zip contains a single top-level folder, e.g.:
                // ffmpeg-master-latest-win64-gpl/bin/ffmpeg.exe
                foreach (var entry in zip.Entries)
                {
                    if (entry.FullName.EndsWith("/ffmpeg.exe", StringComparison.OrdinalIgnoreCase)
                        || entry.FullName.EndsWith("\\ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var tmp = FfmpegPath + ".tmp";
                        using (var src = entry.Open())
                        using (var dst = File.Create(tmp))
                            await src.CopyToAsync(dst);

                        File.Move(tmp, FfmpegPath, overwrite: true);
                        Log.Information("ffmpeg: extracted to {Path}", FfmpegPath);
                        SetStatus("ffmpeg: Ready");
                        return;
                    }
                }

                Log.Warning("ffmpeg: ffmpeg.exe not found inside the downloaded zip");
                SetStatus("ffmpeg: Download failed — video+audio may save as separate files");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ffmpeg: EnsureFfmpegAsync failed");
                SetStatus("ffmpeg: Not available — video+audio may save as separate files");
            }
        }

        private static async Task DownloadFileAsync(string url, string destPath)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TelegramAutoDownload/2.2");
            http.Timeout = TimeSpan.FromMinutes(10);
            var bytes = await http.GetByteArrayAsync(url);
            string tmp = destPath + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            File.Move(tmp, destPath, overwrite: true);
        }

        private static string? GetYtdlpVersion()
        {
            try
            {
                var psi = new ProcessStartInfo(ExePath, "--version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                string? version = proc?.StandardOutput.ReadLine()?.Trim();
                proc?.WaitForExit(3000);
                return version;
            }
            catch { return null; }
        }

        private static void SetStatus(string msg)
        {
            StatusMessage = msg;
            StatusChanged?.Invoke(msg);
        }
    }
}

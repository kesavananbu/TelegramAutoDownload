using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace TelegramAutoDownload.Services
{
    public static class YtdlpService
    {
        private const string GitHubApiUrl =
            "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";

        private const string FfmpegZipUrl =
            "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

        private const string FfmpegSumsUrl =
            "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/SHA2-256SUMS";

        private const string FfmpegZipFileName = "ffmpeg-master-latest-win64-gpl.zip";

        public static string ToolsFolder => AppPaths.ToolsDir;

        public static string ExePath    => Path.Combine(ToolsFolder, "yt-dlp.exe");
        public static string FfmpegPath => Path.Combine(ToolsFolder, "ffmpeg.exe");

        public static string StatusMessage { get; private set; } = string.Empty;
        public static event Action<string>? StatusChanged;

        public static bool IsAutoDownloadDisabled()
        {
            var v = Environment.GetEnvironmentVariable("TAD_DISABLE_TOOL_AUTO_DOWNLOAD");
            return string.Equals(v, "1", StringComparison.Ordinal)
                || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
        }

        public static async Task EnsureAsync()
        {
            if (IsAutoDownloadDisabled())
            {
                SetStatus("yt-dlp: auto-download disabled (TAD_DISABLE_TOOL_AUTO_DOWNLOAD)");
                return;
            }

            await EnsureYtdlpAsync();
            await EnsureFfmpegAsync();
        }

        private static async Task EnsureYtdlpAsync()
        {
            try
            {
                if (!File.Exists(ExePath))
                {
                    var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "yt-dlp.exe");
                    if (File.Exists(bundled))
                        File.Copy(bundled, ExePath, overwrite: false);
                }

                string? latestVersion = null;
                string? downloadUrl = null;
                string? sha256SumsUrl = null;

                try
                {
                    using var http = CreateHttpClient();
                    var json = await http.GetStringAsync(GitHubApiUrl);
                    var obj = JObject.Parse(json);

                    latestVersion = obj["tag_name"]?.ToString()?.TrimStart('v');
                    if (obj["assets"] is JArray assets)
                    {
                        foreach (var asset in assets)
                        {
                            var name = asset["name"]?.ToString() ?? string.Empty;
                            var url = asset["browser_download_url"]?.ToString() ?? string.Empty;
                            if (string.Equals(name, "SHA2-256SUMS", StringComparison.OrdinalIgnoreCase)
                                && FileIntegrity.IsAllowedGitHubDownloadUrl(url))
                            {
                                sha256SumsUrl = url;
                            }
                            else if (name == "yt-dlp.exe" && FileIntegrity.IsAllowedGitHubDownloadUrl(url))
                            {
                                downloadUrl = url;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "yt-dlp: Could not fetch GitHub release info");
                }

                if (!File.Exists(ExePath))
                {
                    if (downloadUrl == null)
                    {
                        SetStatus("yt-dlp: missing and no verified download URL");
                        return;
                    }

                    SetStatus("yt-dlp: Downloading...");
                    await DownloadVerifiedFileAsync(downloadUrl, ExePath, sha256SumsUrl, "yt-dlp.exe");
                    SetStatus("yt-dlp: Ready");
                    return;
                }

                string? currentVersion = GetYtdlpVersion();
                if (currentVersion == null || latestVersion == null) return;

                if (string.Compare(latestVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0
                    && downloadUrl != null)
                {
                    SetStatus($"yt-dlp: Updating {currentVersion} → {latestVersion}...");
                    Log.Information("yt-dlp: updating from {Current} to {Latest}", currentVersion, latestVersion);
                    await DownloadVerifiedFileAsync(downloadUrl, ExePath, sha256SumsUrl, "yt-dlp.exe");
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

        private static async Task EnsureFfmpegAsync()
        {
            if (File.Exists(FfmpegPath)) return;

            var bundled = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg.exe");
            if (File.Exists(bundled))
            {
                File.Copy(bundled, FfmpegPath, overwrite: false);
                Log.Information("ffmpeg: copied from installer bundle");
                return;
            }

            if (!FileIntegrity.IsAllowedGitHubDownloadUrl(FfmpegZipUrl))
            {
                Log.Warning("ffmpeg: bundled ZIP URL is not allowed");
                return;
            }

            try
            {
                SetStatus("ffmpeg: Downloading (needed for video merging)...");
                Log.Information("ffmpeg: downloading from yt-dlp/FFmpeg-Builds");

                using var http = CreateHttpClient();
                http.Timeout = TimeSpan.FromMinutes(15);

                var sumsText = await http.GetStringAsync(FfmpegSumsUrl);
                if (!FileIntegrity.TryGetHashFromSumsFile(sumsText, FfmpegZipFileName, out var expectedZipHash))
                {
                    Log.Warning("ffmpeg: SHA2-256SUMS missing entry for {Zip}", FfmpegZipFileName);
                    SetStatus("ffmpeg: Download refused — missing checksums");
                    return;
                }

                var zipBytes = await http.GetByteArrayAsync(FfmpegZipUrl);
                var zipTmp = Path.Combine(ToolsFolder, FfmpegZipFileName + ".tmp");
                await File.WriteAllBytesAsync(zipTmp, zipBytes);

                var actualHash = await FileIntegrity.ComputeSha256HexAsync(zipTmp);
                if (!string.Equals(actualHash, expectedZipHash, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(zipTmp); } catch { }
                    Log.Warning("ffmpeg: ZIP failed SHA-256 verification");
                    SetStatus("ffmpeg: Download refused — checksum mismatch");
                    return;
                }

                using var ms = new MemoryStream(zipBytes);
                using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

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
                        try { File.Delete(zipTmp); } catch { }
                        Log.Information("ffmpeg: extracted to {Path}", FfmpegPath);
                        SetStatus("ffmpeg: Ready");
                        return;
                    }
                }

                try { File.Delete(zipTmp); } catch { }
                Log.Warning("ffmpeg: ffmpeg.exe not found inside the downloaded zip");
                SetStatus("ffmpeg: Download failed — video+audio may save as separate files");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "ffmpeg: EnsureFfmpegAsync failed");
                SetStatus("ffmpeg: Not available — video+audio may save as separate files");
            }
        }

        private static async Task DownloadVerifiedFileAsync(
            string url,
            string destPath,
            string? sha256SumsUrl,
            string sumsFileName)
        {
            if (!FileIntegrity.IsAllowedGitHubDownloadUrl(url))
                throw new InvalidOperationException("Refusing to download from a non-GitHub URL.");

            using var http = CreateHttpClient();
            var bytes = await http.GetByteArrayAsync(url);

            if (string.IsNullOrEmpty(sha256SumsUrl))
                throw new InvalidOperationException("Refusing to install without SHA2-256SUMS from the release.");

            var sums = await http.GetStringAsync(sha256SumsUrl);
            if (!FileIntegrity.TryGetHashFromSumsFile(sums, sumsFileName, out var expectedHash))
                throw new InvalidOperationException($"SHA2-256SUMS does not list {sumsFileName}.");

            string tmp = destPath + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes);
            var actual = await FileIntegrity.ComputeSha256HexAsync(tmp);
            if (!string.Equals(actual, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(tmp); } catch { }
                throw new InvalidOperationException("Downloaded file failed SHA-256 verification.");
            }

            File.Move(tmp, destPath, overwrite: true);
        }

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"TelegramAutoDownload/{AppVersion.Current}");
            http.Timeout = TimeSpan.FromMinutes(10);
            return http;
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

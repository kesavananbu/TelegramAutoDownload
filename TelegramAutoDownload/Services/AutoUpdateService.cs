using Newtonsoft.Json.Linq;
using Serilog;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;

namespace TelegramAutoDownload.Services
{
    public class ReleaseInfo
    {
        public string TagName { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public string Changelog { get; init; } = string.Empty;
        public string AssetUrl { get; init; } = string.Empty;
        public string AssetName { get; init; } = string.Empty;
        public string? ExpectedSha256 { get; init; }
        public string? Sha256SumsUrl { get; init; }
        public string ReleasePageUrl { get; init; } = string.Empty;
    }

    public static class AutoUpdateService
    {
        private static readonly string ApiUrl =
            $"https://api.github.com/repos/{AppVersion.GitHubOwner}/{AppVersion.GitHubRepo}/releases/latest";

        private static bool IsPortableInstall()
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
            var pf     = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pfx86  = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local  = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return !exePath.StartsWith(pf,    StringComparison.OrdinalIgnoreCase)
                && !exePath.StartsWith(pfx86, StringComparison.OrdinalIgnoreCase)
                && !exePath.StartsWith(local, StringComparison.OrdinalIgnoreCase);
        }

        public static async Task<ReleaseInfo?> CheckAsync()
        {
            try
            {
                using var http = CreateHttpClient();
                var json = await http.GetStringAsync(ApiUrl);
                var obj = JObject.Parse(json);

                var tagName = obj["tag_name"]?.ToString() ?? string.Empty;
                var version = tagName.TrimStart('v');
                var changelog = obj["body"]?.ToString() ?? string.Empty;
                var releasePage = obj["html_url"]?.ToString() ?? string.Empty;

                if (!AppVersionCompare.IsNewer(version, AppVersion.Current))
                    return null;

                bool portable = IsPortableInstall();
                string assetUrl = string.Empty, assetName = string.Empty;
                string? assetDigest = null;
                string setupUrl = string.Empty, setupName = string.Empty;
                string? setupDigest = null;
                string zipUrl   = string.Empty, zipName   = string.Empty;
                string? zipDigest = null;
                string? sha256SumsUrl = null;

                var assets = obj["assets"] as JArray;
                if (assets != null)
                {
                    foreach (var asset in assets)
                    {
                        var name = asset["name"]?.ToString() ?? string.Empty;
                        var url  = asset["browser_download_url"]?.ToString() ?? string.Empty;
                        var digest = FileIntegrity.ParseGitHubAssetDigest(asset["digest"]?.ToString());

                        if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)
                            && FileIntegrity.IsAllowedGitHubDownloadUrl(url))
                        {
                            sha256SumsUrl = url;
                            continue;
                        }

                        if (!FileIntegrity.IsAllowedUpdateAssetName(name)
                            || !FileIntegrity.IsAllowedGitHubDownloadUrl(url))
                            continue;

                        if (name.EndsWith("_Setup.exe", StringComparison.OrdinalIgnoreCase) && setupUrl == string.Empty)
                        {
                            setupUrl = url;
                            setupName = name;
                            setupDigest = digest;
                        }
                        else if (name.EndsWith("_Portable.zip", StringComparison.OrdinalIgnoreCase) && zipUrl == string.Empty)
                        {
                            zipUrl = url;
                            zipName = name;
                            zipDigest = digest;
                        }
                    }
                }

                if (portable)
                {
                    assetUrl  = zipUrl   != string.Empty ? zipUrl   : setupUrl;
                    assetName = zipUrl   != string.Empty ? zipName  : setupName;
                    assetDigest = zipUrl != string.Empty ? zipDigest : setupDigest;
                }
                else
                {
                    assetUrl  = setupUrl != string.Empty ? setupUrl : zipUrl;
                    assetName = setupUrl != string.Empty ? setupName : zipName;
                    assetDigest = setupUrl != string.Empty ? setupDigest : zipDigest;
                }

                if (string.IsNullOrEmpty(assetUrl) || !FileIntegrity.IsAllowedUpdateAssetName(assetName))
                    return null;

                Log.Information("Update available: {Version} (portable={P}, asset={A})", version, portable, assetName);

                return new ReleaseInfo
                {
                    TagName = tagName,
                    Version = version,
                    Changelog = changelog,
                    AssetUrl = assetUrl,
                    AssetName = assetName,
                    ExpectedSha256 = assetDigest,
                    Sha256SumsUrl = sha256SumsUrl,
                    ReleasePageUrl = releasePage
                };
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Auto-update check failed");
                return null;
            }
        }

        public static async Task DownloadAndInstallAsync(ReleaseInfo release, Action<int>? onProgress = null)
        {
            if (string.IsNullOrEmpty(release.AssetUrl))
                throw new InvalidOperationException(
                    "No installer asset is attached to this release yet.\nPlease download manually from GitHub.");

            if (!FileIntegrity.IsAllowedUpdateAssetName(release.AssetName))
                throw new UpdateIntegrityException("Release asset name is not on the allowlist.");

            if (!FileIntegrity.IsAllowedGitHubDownloadUrl(release.AssetUrl))
                throw new UpdateIntegrityException("Release download URL is not from an allowed GitHub host.");

            var workDir = Path.Combine(Path.GetTempPath(), "TAD_update_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            var tmpFile = Path.Combine(workDir, release.AssetName);
            bool isInstaller = release.AssetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

            try
            {
                using var http = CreateHttpClient();
                using var response = await http.GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                long total = response.Content.Headers.ContentLength ?? 0;
                long received = 0;

                await using (var fs = File.Create(tmpFile))
                {
                    await using var stream = await response.Content.ReadAsStreamAsync();
                    var buffer = new byte[81920];
                    int read;
                    while ((read = await stream.ReadAsync(buffer)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read));
                        received += read;
                        if (total > 0)
                            onProgress?.Invoke((int)(received * 100 / total));
                    }
                }

                onProgress?.Invoke(100);

                var expectedHash = release.ExpectedSha256;
                if (string.IsNullOrEmpty(expectedHash) && !string.IsNullOrEmpty(release.Sha256SumsUrl))
                {
                    var sums = await http.GetStringAsync(release.Sha256SumsUrl);
                    if (!FileIntegrity.TryGetHashFromSumsFile(sums, release.AssetName, out var fromSums))
                    {
                        throw new UpdateIntegrityException(
                            "SHA256SUMS.txt is present but does not list this release asset.",
                            missingIntegrityMetadata: true);
                    }
                    expectedHash = fromSums;
                }

                if (string.IsNullOrEmpty(expectedHash))
                {
                    throw new UpdateIntegrityException(
                        "This release has no SHA-256 digest. Auto-install is disabled; download manually from GitHub.",
                        missingIntegrityMetadata: true);
                }

                var actualHash = await FileIntegrity.ComputeSha256HexAsync(tmpFile);
                if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(tmpFile); } catch { }
                    throw new UpdateIntegrityException("Downloaded update failed SHA-256 verification.");
                }

                if (isInstaller)
                    await LaunchSetupInstallerAsync(workDir, tmpFile);
                else
                    await LaunchPortableZipUpdateAsync(workDir, tmpFile, release.Version);
            }
            catch (Exception ex) when (ex is not UpdateIntegrityException)
            {
                Log.Error(ex, "Auto-update install failed");
                throw;
            }
            finally
            {
                if (!isInstaller)
                {
                    try { if (File.Exists(tmpFile)) File.Delete(tmpFile); } catch { }
                    try { if (Directory.Exists(workDir)) Directory.Delete(workDir, true); } catch { }
                }
            }
        }

        private static async Task LaunchSetupInstallerAsync(string workDir, string installerPath)
        {
            var pid = Process.GetCurrentProcess().Id;
            var batchPath = Path.Combine(workDir, "run_setup.cmd");
            await File.WriteAllTextAsync(batchPath, @"@echo off
:wait
tasklist /fi ""pid eq %TAD_PID%"" | findstr /i ""%TAD_PID%"" >nul 2>&1
if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto wait )
timeout /t 2 /nobreak >nul
taskkill /f /im TelegramAutoDownload.exe >nul 2>&1
timeout /t 3 /nobreak >nul
""%TAD_INSTALLER%"" /VERYSILENT /NORESTART /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS
del ""%~f0""
");

            var psi = new ProcessStartInfo("cmd.exe", $"/c \"{batchPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            psi.Environment["TAD_PID"] = pid.ToString();
            psi.Environment["TAD_INSTALLER"] = installerPath;

            Process.Start(psi);

            Application.Current.Dispatcher.Invoke(() =>
            {
                App.IsForceShutdown = true;
                Application.Current.Shutdown();
            });
        }

        private static async Task LaunchPortableZipUpdateAsync(string workDir, string zipPath, string version)
        {
            var tmpExtract = Path.Combine(workDir, "extract");
            if (Directory.Exists(tmpExtract)) Directory.Delete(tmpExtract, true);
            ZipFile.ExtractToDirectory(zipPath, tmpExtract);

            string srcDir = tmpExtract;
            var subdirs = Directory.GetDirectories(tmpExtract);
            if (subdirs.Length == 1 && !Directory.GetFiles(tmpExtract).Any())
                srcDir = subdirs[0];

            var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            var exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule!.FileName);
            var restartExe = Path.Combine(appDir, exeName);
            var pid = Process.GetCurrentProcess().Id;
            var batchPath = Path.Combine(workDir, "run_portable.cmd");

            await File.WriteAllTextAsync(batchPath, @"@echo off
:wait
tasklist /fi ""pid eq %TAD_PID%"" | findstr /i ""%TAD_PID%"" >nul 2>&1
if not errorlevel 1 ( timeout /t 1 /nobreak >nul & goto wait )
robocopy ""%TAD_SRC%"" ""%TAD_DEST%"" /e /is /it /np /nfl /ndl >nul 2>&1
if errorlevel 8 xcopy /e /y /q ""%TAD_SRC%\*"" ""%TAD_DEST%\"" >nul 2>&1
timeout /t 1 /nobreak >nul
start """" ""%TAD_RESTART%""
del ""%~f0""
");

            var psi = new ProcessStartInfo("cmd.exe", $"/c \"{batchPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
            psi.Environment["TAD_PID"] = pid.ToString();
            psi.Environment["TAD_SRC"] = srcDir;
            psi.Environment["TAD_DEST"] = appDir;
            psi.Environment["TAD_RESTART"] = restartExe;

            Process.Start(psi);

            Application.Current.Dispatcher.Invoke(() =>
            {
                App.IsForceShutdown = true;
                Application.Current.Shutdown();
            });
        }

        private static HttpClient CreateHttpClient()
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"TelegramAutoDownload/{AppVersion.Current}");
            http.Timeout = TimeSpan.FromMinutes(15);
            return http;
        }
    }
}

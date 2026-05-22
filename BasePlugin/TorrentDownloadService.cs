using MonoTorrent;
using MonoTorrent.Client;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BasePlugins
{
    /// <summary>
    /// Downloads torrent content via MonoTorrent (magnet URI or .torrent file).
    /// Shared by TorrentPlugin and Telegram .torrent attachments.
    /// </summary>
    public static class TorrentDownloadService
    {
        public static bool IsTorrentFileName(string? fileName) =>
            !string.IsNullOrEmpty(fileName) &&
            fileName.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase);

        public static async Task<string> ResolveDisplayNameAsync(string? magnetUri, string? torrentFilePath)
        {
            if (!string.IsNullOrEmpty(torrentFilePath) && File.Exists(torrentFilePath))
            {
                try
                {
                    var torrent = await Torrent.LoadAsync(torrentFilePath).ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(torrent.Name))
                        return torrent.Name;
                }
                catch
                {
                    // Fall back to filename without extension
                }

                return Path.GetFileNameWithoutExtension(torrentFilePath);
            }

            if (!string.IsNullOrEmpty(magnetUri))
            {
                try
                {
                    var magnet = MagnetLink.Parse(magnetUri);
                    if (!string.IsNullOrWhiteSpace(magnet.Name))
                        return magnet.Name;
                }
                catch
                {
                    // ignore
                }
            }

            return "torrent";
        }

        public static async Task<ResultExecute> DownloadAsync(
            string chatName,
            string outputFolder,
            string? magnetUri,
            string? torrentFilePath,
            string pluginName,
            Action<string, string, string, double, long, long>? onProgress,
            Action<string, string, bool>? onComplete,
            CancellationToken hostCancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(magnetUri) && string.IsNullOrWhiteSpace(torrentFilePath))
            {
                return new ResultExecute(chatName)
                {
                    IsSuccess = false,
                    ErrorMessage = "No magnet link or .torrent file provided.",
                };
            }

            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            var displayName = await ResolveDisplayNameAsync(magnetUri, torrentFilePath).ConfigureAwait(false);
            var cancelKey = CancellationRegistry.MakeKey(chatName, displayName);
            var userToken = CancellationRegistry.Register(cancelKey);

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(userToken, hostCancellationToken);
            var token = linkedCts.Token;

            ClientEngine? engine = null;
            TorrentManager? torrentManager = null;

            try
            {
                onProgress?.Invoke(chatName, displayName, pluginName, 0, 0, 0);

                var settings = new EngineSettingsBuilder
                {
                    CacheDirectory = Path.Combine(outputFolder, ".cache"),
                }.ToSettings();

                engine = new ClientEngine(settings);

                if (!string.IsNullOrWhiteSpace(torrentFilePath))
                {
                    var torrent = await Torrent.LoadAsync(torrentFilePath).ConfigureAwait(false);
                    torrentManager = await engine.AddAsync(torrent, outputFolder).ConfigureAwait(false);
                }
                else
                {
                    var magnetLink = MagnetLink.Parse(magnetUri!);
                    torrentManager = await engine.AddAsync(magnetLink, outputFolder).ConfigureAwait(false);
                }

                await torrentManager.StartAsync().ConfigureAwait(false);

                var lastReportUtc = DateTime.UtcNow;
                long lastBytes = 0;

                while (!token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();

                    var state = torrentManager.State;
                    if (state == TorrentState.Seeding)
                        break;
                    if (state == TorrentState.Stopped && torrentManager.Complete)
                        break;
                    if (state == TorrentState.Error)
                    {
                        onComplete?.Invoke(chatName, displayName, false);
                        return new ResultExecute(chatName)
                        {
                            IsSuccess = false,
                            FileName = displayName,
                            ErrorMessage = $"Torrent error: {torrentManager.Error?.Exception?.Message ?? "unknown"}",
                        };
                    }

                    var totalBytes = torrentManager.Torrent?.Size ?? 0;
                    var downloadedBytes = torrentManager.Monitor.DataBytesReceived;
                    if (totalBytes <= 0 && torrentManager.Complete)
                        totalBytes = downloadedBytes;

                    var pct = totalBytes > 0
                        ? Math.Min(99, downloadedBytes * 100.0 / totalBytes)
                        : (torrentManager.Progress * 100.0);

                    var now = DateTime.UtcNow;
                    if (now - lastReportUtc >= TimeSpan.FromMilliseconds(500) || downloadedBytes != lastBytes || pct >= 99)
                    {
                        onProgress?.Invoke(chatName, displayName, pluginName, pct, downloadedBytes, totalBytes);
                        lastReportUtc = now;
                        lastBytes = downloadedBytes;
                    }

                    if (torrentManager.Complete)
                        break;

                    await Task.Delay(500, token).ConfigureAwait(false);
                }

                await torrentManager.StopAsync().ConfigureAwait(false);

                var downloadedPath = ResolveDownloadedPath(torrentManager, outputFolder);
                onProgress?.Invoke(chatName, displayName, pluginName, 100, torrentManager.Torrent?.Size ?? 0, torrentManager.Torrent?.Size ?? 0);
                onComplete?.Invoke(chatName, displayName, true);

                return new ResultExecute(chatName)
                {
                    IsSuccess = true,
                    FileName = displayName,
                    FilePath = downloadedPath,
                };
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (torrentManager != null)
                        await torrentManager.StopAsync().ConfigureAwait(false);
                }
                catch
                {
                    // ignore cleanup errors
                }

                onComplete?.Invoke(chatName, displayName, false);
                return new ResultExecute(chatName)
                {
                    IsSuccess = false,
                    FileName = displayName,
                    ErrorMessage = "Cancelled by user",
                };
            }
            catch (Exception ex)
            {
                onComplete?.Invoke(chatName, displayName, false);
                return new ResultExecute(chatName)
                {
                    IsSuccess = false,
                    FileName = displayName,
                    ErrorMessage = ex.Message,
                };
            }
            finally
            {
                engine?.Dispose();
                CancellationRegistry.Remove(cancelKey);
            }
        }

        private static string? ResolveDownloadedPath(TorrentManager manager, string outputFolder)
        {
            var files = manager.Files;
            if (files != null && files.Count == 1)
            {
                var path = Path.Combine(outputFolder, files[0].Path);
                if (File.Exists(path))
                    return path;
            }

            if (files != null && files.Count > 1)
            {
                var firstExisting = files
                    .Select(f => Path.Combine(outputFolder, f.Path))
                    .FirstOrDefault(File.Exists);
                if (firstExisting != null)
                    return firstExisting;
            }

            if (Directory.Exists(outputFolder))
            {
                var rootFiles = Directory.GetFiles(outputFolder, "*", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.cache{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (rootFiles.Count == 1)
                    return rootFiles[0];
            }

            return outputFolder;
        }
    }
}

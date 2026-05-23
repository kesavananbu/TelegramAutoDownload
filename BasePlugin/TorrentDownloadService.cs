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
        private static readonly object EngineLock = new();
        private static ClientEngine? _sharedEngine;

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

            magnetUri = MagnetLinkHelper.TryExtract(magnetUri);
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
            magnetUri = MagnetLinkHelper.TryExtract(magnetUri);

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

            TorrentManager? torrentManager = null;
            long knownTotalBytes = 0;

            try
            {
                if (!string.IsNullOrWhiteSpace(torrentFilePath) && File.Exists(torrentFilePath))
                {
                    var torrentMeta = await Torrent.LoadAsync(torrentFilePath).ConfigureAwait(false);
                    knownTotalBytes = torrentMeta.Size;
                }

                onProgress?.Invoke(chatName, displayName, pluginName, 0, 0, knownTotalBytes);

                var engine = GetSharedEngine();
                var torrentSettings = new TorrentSettingsBuilder
                {
                    CreateContainingDirectory = true,
                    AllowDht = true,
                    AllowPeerExchange = true,
                }.ToSettings();

                if (!string.IsNullOrWhiteSpace(torrentFilePath))
                {
                    var torrent = await Torrent.LoadAsync(torrentFilePath).ConfigureAwait(false);
                    knownTotalBytes = torrent.Size;
                    torrentManager = await engine.AddAsync(torrent, outputFolder, torrentSettings).ConfigureAwait(false);
                }
                else
                {
                    var magnetLink = MagnetLink.Parse(magnetUri!);
                    torrentManager = await engine.AddAsync(magnetLink, outputFolder, torrentSettings).ConfigureAwait(false);
                }

                await torrentManager.StartAsync().ConfigureAwait(false);

                var isMagnetDownload = string.IsNullOrWhiteSpace(torrentFilePath);
                var startedUtc = DateTime.UtcNow;
                var downloadPhaseStartedUtc = startedUtc;
                var lastReportUtc = DateTime.MinValue;
                double lastPct = -1;
                long lastBytes = -1;
                var hadMetadata = torrentManager.HasMetadata;

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

                    if (!hadMetadata && torrentManager.HasMetadata)
                    {
                        hadMetadata = true;
                        downloadPhaseStartedUtc = DateTime.UtcNow;
                    }

                    if (torrentManager.Torrent?.Size > 0)
                        knownTotalBytes = torrentManager.Torrent.Size;

                    var totalBytes = knownTotalBytes;
                    var progress = torrentManager.Progress;
                    var downloadedBytes = totalBytes > 0
                        ? (long)Math.Min(totalBytes, progress * totalBytes)
                        : torrentManager.Monitor.DataBytesReceived;

                    var pct = totalBytes > 0
                        ? Math.Min(99, progress * 100.0)
                        : (downloadedBytes > 0 ? 1.0 : 0.0);

                    var now = DateTime.UtcNow;
                    if (now - lastReportUtc >= TimeSpan.FromMilliseconds(500)
                        || Math.Abs(pct - lastPct) >= 0.1
                        || downloadedBytes != lastBytes)
                    {
                        onProgress?.Invoke(chatName, displayName, pluginName, pct, downloadedBytes, totalBytes);
                        lastReportUtc = now;
                        lastPct = pct;
                        lastBytes = downloadedBytes;
                    }

                    if (torrentManager.Complete)
                        break;

                    if (isMagnetDownload && !torrentManager.HasMetadata)
                    {
                        if (now - startedUtc > TimeSpan.FromMinutes(30))
                        {
                            onComplete?.Invoke(chatName, displayName, false);
                            return new ResultExecute(chatName)
                            {
                                IsSuccess = false,
                                FileName = displayName,
                                ErrorMessage = "Could not fetch magnet metadata after 30 minutes. The swarm may be dead or blocked by firewall/DHT.",
                            };
                        }
                    }
                    else if (progress <= 0
                        && torrentManager.Monitor.DataBytesReceived == 0
                        && now - downloadPhaseStartedUtc > TimeSpan.FromMinutes(10))
                    {
                        onComplete?.Invoke(chatName, displayName, false);
                        return new ResultExecute(chatName)
                        {
                            IsSuccess = false,
                            FileName = displayName,
                            ErrorMessage = "No peers found after 10 minutes. Check firewall/router or try again later.",
                        };
                    }

                    await Task.Delay(500, token).ConfigureAwait(false);
                }

                await torrentManager.StopAsync().ConfigureAwait(false);
                try { await engine.RemoveAsync(torrentManager).ConfigureAwait(false); } catch { }

                var finalTotal = torrentManager.Torrent?.Size ?? knownTotalBytes;
                var downloadedPath = ResolveDownloadedPath(torrentManager, outputFolder);
                onProgress?.Invoke(chatName, displayName, pluginName, 100, finalTotal, finalTotal);
                onComplete?.Invoke(chatName, displayName, true);

                return new ResultExecute(chatName)
                {
                    IsSuccess = true,
                    FileName = displayName,
                    FilePath = downloadedPath ?? outputFolder,
                };
            }
            catch (OperationCanceledException)
            {
                await StopAndRemoveAsync(torrentManager).ConfigureAwait(false);
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
                await StopAndRemoveAsync(torrentManager).ConfigureAwait(false);
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
                CancellationRegistry.Remove(cancelKey);
            }
        }

        private static ClientEngine GetSharedEngine()
        {
            lock (EngineLock)
            {
                if (_sharedEngine != null)
                    return _sharedEngine;

                var cacheRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TelegramAutoDownload",
                    "torrent-engine");

                Directory.CreateDirectory(cacheRoot);

                var settings = new EngineSettingsBuilder
                {
                    AllowPortForwarding = true,
                    AllowLocalPeerDiscovery = true,
                    AutoSaveLoadMagnetLinkMetadata = true,
                    AutoSaveLoadDhtCache = true,
                    CacheDirectory = Path.Combine(cacheRoot, "cache"),
                }.ToSettings();

                _sharedEngine = new ClientEngine(settings);
                return _sharedEngine;
            }
        }

        private static async Task StopAndRemoveAsync(TorrentManager? torrentManager)
        {
            if (torrentManager == null)
                return;

            try
            {
                await torrentManager.StopAsync().ConfigureAwait(false);
                var engine = _sharedEngine;
                if (engine != null)
                    await engine.RemoveAsync(torrentManager).ConfigureAwait(false);
            }
            catch
            {
                // ignore cleanup errors
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

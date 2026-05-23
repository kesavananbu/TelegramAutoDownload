using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramAutoDownload.Services
{
    public sealed class DiskSpaceInfo
    {
        public string DriveName { get; init; } = string.Empty;
        public long DriveTotalBytes { get; init; }
        public long DriveFreeBytes { get; init; }
        public long FolderBytes { get; init; }
        public int FolderFileCount { get; init; }
        public string FolderPath { get; init; } = string.Empty;
        public bool IsFolderScanComplete { get; init; }
        public string? ErrorMessage { get; init; }

        public double DriveFreePercent =>
            DriveTotalBytes > 0 ? DriveFreeBytes * 100.0 / DriveTotalBytes : 0;
    }

    /// <summary>
    /// Reports free space on the download drive and total size of the download folder.
    /// Folder size is computed on a background thread and cached between refreshes.
    /// </summary>
    public sealed class DiskSpaceService
    {
        private static readonly Lazy<DiskSpaceService> _instance = new(() => new());
        public static DiskSpaceService Instance => _instance.Value;

        private readonly object _lock = new();
        private System.Threading.Timer? _timer;
        private string? _monitoredPath;
        private int _scanGeneration;
        private CancellationTokenSource? _scanCts;

        public DiskSpaceInfo? LastInfo { get; private set; }
        public event Action? Changed;

        private DiskSpaceService() { }

        public void StartMonitoring(string? folderPath)
        {
            lock (_lock)
            {
                _monitoredPath = folderPath;
                _timer?.Dispose();
                _timer = new System.Threading.Timer(_ => _ = RefreshAsync(_monitoredPath),
                    null, TimeSpan.Zero, TimeSpan.FromSeconds(60));
            }
        }

        public void StopMonitoring()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
                _monitoredPath = null;
            }

            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = null;
        }

        public async Task RefreshAsync(string? folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                LastInfo = new DiskSpaceInfo { ErrorMessage = "No download folder configured" };
                Changed?.Invoke();
                return;
            }

            var root = Path.GetPathRoot(folderPath);
            long driveTotal = 0, driveFree = 0;
            var driveName = root ?? "?:";

            try
            {
                if (!string.IsNullOrEmpty(root))
                {
                    var drive = new DriveInfo(root);
                    if (drive.IsReady)
                    {
                        driveName = drive.Name.TrimEnd('\\');
                        driveTotal = drive.TotalSize;
                        driveFree = drive.AvailableFreeSpace;
                    }
                }
            }
            catch (Exception ex)
            {
                LastInfo = new DiskSpaceInfo
                {
                    FolderPath = folderPath,
                    DriveName = driveName,
                    ErrorMessage = ex.Message,
                };
                Changed?.Invoke();
                return;
            }

            var generation = Interlocked.Increment(ref _scanGeneration);
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _scanCts = new CancellationTokenSource();
            var token = _scanCts.Token;

            LastInfo = new DiskSpaceInfo
            {
                DriveName = driveName,
                DriveTotalBytes = driveTotal,
                DriveFreeBytes = driveFree,
                FolderPath = folderPath,
                IsFolderScanComplete = false,
            };
            Changed?.Invoke();

            try
            {
                var (folderBytes, fileCount) = await Task.Run(
                    () => CalculateFolderSize(folderPath, token), token).ConfigureAwait(false);

                if (token.IsCancellationRequested || generation != _scanGeneration)
                    return;

                LastInfo = new DiskSpaceInfo
                {
                    DriveName = driveName,
                    DriveTotalBytes = driveTotal,
                    DriveFreeBytes = driveFree,
                    FolderBytes = folderBytes,
                    FolderFileCount = fileCount,
                    FolderPath = folderPath,
                    IsFolderScanComplete = true,
                };
                Changed?.Invoke();
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer refresh
            }
            catch (Exception ex)
            {
                if (generation != _scanGeneration) return;
                LastInfo = new DiskSpaceInfo
                {
                    DriveName = driveName,
                    DriveTotalBytes = driveTotal,
                    DriveFreeBytes = driveFree,
                    FolderPath = folderPath,
                    ErrorMessage = ex.Message,
                    IsFolderScanComplete = true,
                };
                Changed?.Invoke();
            }
        }

        public static (long bytes, int fileCount) CalculateFolderSize(string folderPath, CancellationToken token)
        {
            if (!Directory.Exists(folderPath))
                return (0, 0);

            long bytes = 0;
            int count = 0;

            foreach (var file in Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    bytes += new FileInfo(file).Length;
                    count++;
                }
                catch
                {
                    // skip locked/inaccessible files
                }
            }

            return (bytes, count);
        }

        public static string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }
    }
}

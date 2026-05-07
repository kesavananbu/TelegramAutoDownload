using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace TelegramAutoDownload
{
    /// <summary>
    /// View model for a single .part file entry shown in the list.
    /// </summary>
    public record PartFileItem(
        string FileName,
        string SizeFmt,
        string LastModifiedFmt,
        string FullPath,
        long Bytes);

    public partial class ClearStorageDialog : MetroWindow
    {
        private readonly string _rootPath;
        private List<PartFileItem> _files = new();

        public ClearStorageDialog(string rootPath)
        {
            InitializeComponent();
            _rootPath = rootPath;

            // Default range: last 30 days up to today
            dpFrom.SelectedDate = DateTime.Today.AddDays(-30);
            dpTo.SelectedDate   = DateTime.Today;
        }

        // ── Scan ─────────────────────────────────────────────────────────────

        private async void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            SetScanning(true);

            var from = dpFrom.SelectedDate ?? DateTime.MinValue;
            var to   = (dpTo.SelectedDate  ?? DateTime.Today).AddDays(1);

            try
            {
                _files = await Task.Run(() => ScanPartFiles(from, to));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Scan failed: {ex.Message}", "Clear Storage",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _files = new List<PartFileItem>();
            }
            finally
            {
                SetScanning(false);
            }

            listFiles.ItemsSource = _files;
            UpdateSummary();
        }

        /// <summary>
        /// Scans <see cref="_rootPath"/> recursively for *.part files within the given date range.
        /// Runs on a background thread — must not touch UI elements.
        /// </summary>
        private List<PartFileItem> ScanPartFiles(DateTime from, DateTime to)
        {
            if (!Directory.Exists(_rootPath)) return new List<PartFileItem>();

            return Directory
                .EnumerateFiles(_rootPath, "*.part", SearchOption.AllDirectories)
                .Select(p =>
                {
                    try { return new FileInfo(p); }
                    catch { return null; }
                })
                .Where(f => f != null && f.LastWriteTime >= from && f.LastWriteTime < to)
                .OrderByDescending(f => f!.Length)
                .Select(f => new PartFileItem(
                    f!.Name,
                    FormatBytes(f.Length),
                    f.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                    f.FullName,
                    f.Length))
                .ToList();
        }

        // ── Delete ────────────────────────────────────────────────────────────

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_files.Count == 0) return;

            var confirm = MessageBox.Show(
                $"Delete {_files.Count} incomplete file(s)?\n\nThis cannot be undone.",
                "Clear Storage",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            int deleted = 0;
            long freed  = 0;
            var failed  = new List<string>();

            foreach (var item in _files)
            {
                try
                {
                    File.Delete(item.FullPath);
                    deleted++;
                    freed += item.Bytes;
                }
                catch
                {
                    // File is locked (actively downloading) or access denied — skip silently
                    failed.Add(item.FileName);
                }
            }

            var msg = $"Deleted {deleted} file(s), freed {FormatBytes(freed)}.";
            if (failed.Count > 0)
                msg += $"\n\n{failed.Count} file(s) could not be deleted (in use or access denied).";

            MessageBox.Show(msg, "Clear Storage", MessageBoxButton.OK, MessageBoxImage.Information);

            // Refresh the list so it reflects what is still on disk
            BtnScan_Click(sender, e);
        }

        // ── Close ─────────────────────────────────────────────────────────────

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Updates the summary label and the Delete button label/state.</summary>
        private void UpdateSummary()
        {
            long totalBytes = _files.Sum(f => f.Bytes);

            pnlSummary.Visibility = Visibility.Visible;
            tbSummary.Text = $"{_files.Count} file(s) found — Space to free: {FormatBytes(totalBytes)}";

            btnDelete.IsEnabled = _files.Count > 0;
            btnDelete.Content   = $"Delete {_files.Count} file(s)";

            tbDeleteHint.Text = _files.Count > 0
                ? "Files shown above will be permanently deleted."
                : "No .part files found in the selected date range.";
        }

        /// <summary>Toggles the scanning indicator and disables controls while running.</summary>
        private void SetScanning(bool scanning)
        {
            pnlScanning.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
            btnScan.IsEnabled      = !scanning;
            btnDelete.IsEnabled    = !scanning && _files.Count > 0;
            dpFrom.IsEnabled       = !scanning;
            dpTo.IsEnabled         = !scanning;
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F1} GB";
            if (bytes >= 1_048_576)     return $"{bytes / 1_048_576.0:F1} MB";
            if (bytes >= 1024)          return $"{bytes / 1024.0:F0} KB";
            return $"{bytes} B";
        }
    }
}

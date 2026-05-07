using MahApps.Metro.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace TelegramAutoDownload
{
    /// <summary>View model for a single file entry shown in the list.</summary>
    public record PartFileItem(
        string FileName,
        string SizeFmt,
        string FileType,
        string LastModifiedFmt,
        string FullPath,
        long Bytes,
        bool IsIncomplete);

    public partial class ClearStorageDialog : MetroWindow
    {
        private readonly string _rootPath;

        // Full scan result (all files)
        private List<PartFileItem> _allFiles = new();
        // Currently displayed (filtered) list
        private List<PartFileItem> _displayedFiles = new();

        public ClearStorageDialog(string rootPath)
        {
            InitializeComponent();
            _rootPath = rootPath;

            // Default range: today only
            dpFrom.SelectedDate = DateTime.Today;
            dpTo.SelectedDate   = DateTime.Today;

            listFiles.SelectionChanged += ListFiles_SelectionChanged;

            // Auto-scan on open
            Loaded += async (_, _) => await RunScanAsync();
        }

        // ── Scan ─────────────────────────────────────────────────────────────

        private async void BtnScan_Click(object sender, RoutedEventArgs e) => await RunScanAsync();

        private async Task RunScanAsync()
        {
            SetScanning(true);

            var from = dpFrom.SelectedDate ?? DateTime.MinValue;
            var to   = (dpTo.SelectedDate  ?? DateTime.Today).AddDays(1);

            try
            {
                _allFiles = await Task.Run(() => ScanAllFiles(from, to));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Scan failed: {ex.Message}", "Clear Storage",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                _allFiles = new List<PartFileItem>();
            }
            finally
            {
                SetScanning(false);
            }

            ApplyFilter();
        }

        /// <summary>
        /// Scans <see cref="_rootPath"/> recursively for ALL files within the date range.
        /// .part files are flagged as IsIncomplete = true.
        /// Runs on a background thread.
        /// </summary>
        private List<PartFileItem> ScanAllFiles(DateTime from, DateTime to)
        {
            if (!Directory.Exists(_rootPath)) return new List<PartFileItem>();

            return Directory
                .EnumerateFiles(_rootPath, "*.*", SearchOption.AllDirectories)
                .Select(p =>
                {
                    try { return new FileInfo(p); }
                    catch { return null; }
                })
                .Where(f => f != null && f.LastWriteTime >= from && f.LastWriteTime < to)
                .OrderByDescending(f => f!.Length)
                .Select(f =>
                {
                    bool incomplete = f!.Extension.Equals(".part", StringComparison.OrdinalIgnoreCase);
                    string type = incomplete ? ".part" : f.Extension.ToLowerInvariant();
                    return new PartFileItem(
                        f.Name,
                        FormatBytes(f.Length),
                        type,
                        f.LastWriteTime.ToString("yyyy-MM-dd HH:mm"),
                        f.FullName,
                        f.Length,
                        incomplete);
                })
                .ToList();
        }

        // ── Filter ────────────────────────────────────────────────────────────

        private void FilterChanged(object sender, RoutedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            bool incompleteOnly = chkIncompleteOnly.IsChecked == true;
            _displayedFiles = incompleteOnly
                ? _allFiles.Where(f => f.IsIncomplete).ToList()
                : _allFiles;

            listFiles.ItemsSource = _displayedFiles;
            UpdateSummary();
        }

        // ── Selection ─────────────────────────────────────────────────────────

        private void ListFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int count = listFiles.SelectedItems.Count;
            long totalBytes = listFiles.SelectedItems
                .Cast<PartFileItem>()
                .Sum(f => f.Bytes);

            btnDelete.IsEnabled = count > 0;
            btnDelete.Content   = count > 0
                ? $"Delete Selected ({count}) — {FormatBytes(totalBytes)}"
                : "Delete Selected (0)";
        }

        // ── Delete ────────────────────────────────────────────────────────────

        private async void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var selected = listFiles.SelectedItems.Cast<PartFileItem>().ToList();
            if (selected.Count == 0) return;

            long totalBytes = selected.Sum(f => f.Bytes);

            var confirm = MessageBox.Show(
                $"Delete {selected.Count} file(s) ({FormatBytes(totalBytes)})?\n\nThis cannot be undone.",
                "Clear Storage",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            int deleted = 0;
            long freed  = 0;
            var failed  = new List<string>();

            foreach (var item in selected)
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
            await RunScanAsync();
        }

        // ── Select All / Deselect All ─────────────────────────────────────────

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e) => listFiles.SelectAll();

        private void BtnDeselectAll_Click(object sender, RoutedEventArgs e) => listFiles.UnselectAll();

        // ── Close ─────────────────────────────────────────────────────────────

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Updates the summary label.</summary>
        private void UpdateSummary()
        {
            long totalBytes = _displayedFiles.Sum(f => f.Bytes);
            int incomplete  = _displayedFiles.Count(f => f.IsIncomplete);

            pnlSummary.Visibility = Visibility.Visible;
            tbSummary.Text = _displayedFiles.Count > 0
                ? $"{_displayedFiles.Count} file(s) — Total: {FormatBytes(totalBytes)}" +
                  (incomplete > 0 ? $"  |  {incomplete} incomplete (.part)" : string.Empty)
                : "No files found in the selected date range.";

        }

        /// <summary>Toggles the scanning indicator and disables controls while running.</summary>
        private void SetScanning(bool scanning)
        {
            pnlScanning.Visibility = scanning ? Visibility.Visible : Visibility.Collapsed;
            btnScan.IsEnabled      = !scanning;
            btnDelete.IsEnabled    = !scanning && listFiles.SelectedItems.Count > 0;
            dpFrom.IsEnabled       = !scanning;
            dpTo.IsEnabled         = !scanning;
            chkIncompleteOnly.IsEnabled = !scanning;
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

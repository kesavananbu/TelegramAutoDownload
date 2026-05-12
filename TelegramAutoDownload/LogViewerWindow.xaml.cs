using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;

namespace TelegramAutoDownload
{
    public partial class LogViewerWindow : MetroWindow
    {
        private const int MaxChars = 600_000;

        public LogViewerWindow()
        {
            InitializeComponent();
            Loaded += (_, _) => RefreshFileList(selectFirst: true);
        }

        private void RefreshFileList(bool selectFirst)
        {
            lstLogs.Items.Clear();
            tbContent.Text = string.Empty;
            if (!Directory.Exists(AppPaths.LogsDir))
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                return;
            }

            var files = Directory.GetFiles(AppPaths.LogsDir, "app-*.log", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .ToList();
            foreach (var path in files)
                lstLogs.Items.Add(path);

            if (selectFirst && lstLogs.Items.Count > 0)
                lstLogs.SelectedIndex = 0;
        }

        private void LstLogs_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (lstLogs.SelectedItem is not string path || !File.Exists(path))
            {
                tbContent.Text = string.Empty;
                return;
            }

            try
            {
                tbContent.Text = ReadLogTail(path, MaxChars);
            }
            catch (Exception ex)
            {
                tbContent.Text = ex.ToString();
            }
        }

        private static string ReadLogTail(string path, int maxChars)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var len = fs.Length;
            if (len == 0) return string.Empty;

            var take = (int)Math.Min(len, maxChars);
            fs.Seek(-take, SeekOrigin.End);
            var buffer = new byte[take];
            _ = fs.Read(buffer, 0, take);
            var text = Encoding.UTF8.GetString(buffer);
            if (take < len)
                return "… (showing end of file only — file is large)\r\n\r\n" + text;
            return text;
        }

        private void BtnRefresh_OnClick(object sender, RoutedEventArgs e)
        {
            var prev = lstLogs.SelectedItem as string;
            RefreshFileList(selectFirst: false);
            if (prev != null)
            {
                for (var i = 0; i < lstLogs.Items.Count; i++)
                {
                    if ((string)lstLogs.Items[i] == prev)
                    {
                        lstLogs.SelectedIndex = i;
                        break;
                    }
                }
            }
            else if (lstLogs.Items.Count > 0)
                lstLogs.SelectedIndex = 0;
        }

        private void BtnOpenFolder_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = AppPaths.LogsDir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Open folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnDelete_OnClick(object sender, RoutedEventArgs e)
        {
            if (lstLogs.SelectedItem is not string path || !File.Exists(path))
            {
                MessageBox.Show(this, "Select a log file first.", "Delete", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (MessageBox.Show(this,
                    $"Delete this file?\n{Path.GetFileName(path)}",
                    "Delete log",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                File.Delete(path);
                RefreshFileList(selectFirst: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_OnClick(object sender, RoutedEventArgs e) => Close();
    }
}

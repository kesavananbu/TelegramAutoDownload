using MahApps.Metro.Controls;
using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace TelegramAutoDownload
{
    public partial class ConfigJsonPreviewDialog : MetroWindow
    {
        private readonly string _json;

        public ConfigJsonPreviewDialog(string json)
        {
            InitializeComponent();
            _json = json;
            tbJson.Text = json;
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(_json);
                MessageBox.Show(this, "JSON copied to the clipboard.", "Copy",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, $"Could not copy: {ex.Message}", "Copy",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnSaveAs_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save settings",
                Filter = "JSON files (*.json)|*.json",
                FileName = "TelegramAutoDownload-settings.json"
            };
            if (dialog.ShowDialog() != true) return;
            File.WriteAllText(dialog.FileName, _json);
            MessageBox.Show(this, $"Saved to:\n{dialog.FileName}", "Export",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();
    }
}

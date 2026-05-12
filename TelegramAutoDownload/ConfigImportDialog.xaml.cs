using MahApps.Metro.Controls;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.IO;
using System.Windows;
using TelegramAutoDownload.Models;

namespace TelegramAutoDownload
{
    public partial class ConfigImportDialog : MetroWindow
    {
        public ConfigParams? Imported { get; private set; }

        public ConfigImportDialog()
        {
            InitializeComponent();
        }

        private void BtnLoadFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select settings JSON",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true) return;
            tbJson.Text = File.ReadAllText(dialog.FileName);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnImport_Click(object sender, RoutedEventArgs e)
        {
            var text = tbJson.Text?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                MessageBox.Show(this, "Paste JSON or load a file first.", "Import",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var imported = JsonConvert.DeserializeObject<ConfigParams>(text);
                if (imported == null)
                {
                    MessageBox.Show(this, "Invalid JSON: root is null.", "Import",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Imported = imported;
                DialogResult = true;
                Close();
            }
            catch (JsonException ex)
            {
                MessageBox.Show(this, $"Invalid JSON:\n{ex.Message}", "Import",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}

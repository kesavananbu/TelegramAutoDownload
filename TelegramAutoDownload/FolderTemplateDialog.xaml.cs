using System;
using System.IO;
using System.Windows;
using MahApps.Metro.Controls;
using TelegramClient;

namespace TelegramAutoDownload
{
    public partial class FolderTemplateDialog : MetroWindow
    {
        private readonly string _basePath;
        private readonly string _chatName;
        private readonly string _chatType;
        private bool _suppressUpdate;

        /// <summary>Final template value chosen by the user — read this after DialogResult == true.</summary>
        public string ResultTemplate { get; private set; } = string.Empty;

        public FolderTemplateDialog(
            string currentTemplate,
            string chatName,
            string chatType,
            string basePath)
        {
            InitializeComponent();

            _basePath  = basePath;
            _chatName  = chatName;
            _chatType  = chatType;

            _suppressUpdate = true;
            TxtTemplate.Text = currentTemplate;
            _suppressUpdate = false;

            // If the current value is already an absolute path, show it in the browse label too
            if (Path.IsPathRooted(currentTemplate))
                TxtAbsolutePath.Text = currentTemplate;

            UpdatePreview();
        }

        // ── Token buttons ────────────────────────────────────────────────────

        private void Token_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button btn) return;
            var token = btn.Tag as string ?? string.Empty;

            // Capture position and current text BEFORE any modification
            var pos     = TxtTemplate.CaretIndex;
            var current = TxtTemplate.Text ?? string.Empty;

            // If the field shows an absolute path, clear the browse-area label
            // but keep the text so the token is appended at the cursor
            if (Path.IsPathRooted(current))
                TxtAbsolutePath.Text = "No folder selected — template above will be used";

            _suppressUpdate = true;
            TxtTemplate.Text = current.Insert(pos, token);
            _suppressUpdate = false;

            TxtTemplate.CaretIndex = pos + token.Length;
            TxtTemplate.Focus();
            UpdatePreview();
        }

        // ── Template text box ────────────────────────────────────────────────

        private void TxtTemplate_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_suppressUpdate) return;

            // If user typed a rooted path by hand, reflect it in the browse label
            var text = TxtTemplate.Text?.Trim() ?? string.Empty;
            if (Path.IsPathRooted(text))
                TxtAbsolutePath.Text = text;
            else if (!string.IsNullOrEmpty(text))
                TxtAbsolutePath.Text = "No folder selected — template above will be used";

            UpdatePreview();
        }

        // ── Reset ────────────────────────────────────────────────────────────

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            TxtTemplate.Text = string.Empty;
            TxtAbsolutePath.Text = "No folder selected — template above will be used";
        }

        // ── Browse ───────────────────────────────────────────────────────────

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description       = "Select download folder for this chat",
                UseDescriptionForTitle = true,
                ShowNewFolderButton    = true,
            };

            // Open in the currently configured base path if available
            if (!string.IsNullOrWhiteSpace(_basePath) && Directory.Exists(_basePath))
                dialog.InitialDirectory = _basePath;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var picked = dialog.SelectedPath;
                TxtAbsolutePath.Text = picked;

                _suppressUpdate = true;
                TxtTemplate.Text = picked;   // absolute path becomes the full template
                _suppressUpdate = false;

                UpdatePreview();
            }
        }

        // ── Preview ──────────────────────────────────────────────────────────

        // Sample media type used in the preview — matches what {Type} resolves to at download time.
        private const string PreviewMediaType = "Videos";

        private void UpdatePreview()
        {
            var template = TxtTemplate.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(template))
            {
                // Real default layout: {basePath}/{MediaType}/{ChatName}
                // Show "Videos" as a representative example since the actual type varies per file.
                TxtPreview.Text = Path.Combine(_basePath, PreviewMediaType, _chatName)
                                  + "  (Videos / Photos / Music / Files)";
                TxtMode.Text    = "mode: default";
                return;
            }

            // Always resolve tokens (works for both relative and absolute paths)
            var sampleDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day);
            var resolved   = FolderTemplateHelper.Resolve(template, PreviewMediaType, _chatName, sampleDate)
                             ?? Path.Combine(PreviewMediaType, _chatName);

            if (Path.IsPathRooted(resolved))
            {
                // Absolute path (with tokens already substituted)
                TxtPreview.Text = resolved;
                TxtMode.Text    = "mode: absolute path";
            }
            else
            {
                // Relative template — combine with base path
                TxtPreview.Text = Path.Combine(_basePath, resolved);
                TxtMode.Text    = "mode: template";
            }
        }

        // ── OK / Cancel ──────────────────────────────────────────────────────

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            ResultTemplate = TxtTemplate.Text?.Trim() ?? string.Empty;
            DialogResult   = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}

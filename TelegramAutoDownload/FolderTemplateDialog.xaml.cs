using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MahApps.Metro.Controls;
using TelegramClient;
using TelegramClient.Models;

namespace TelegramAutoDownload
{
    public partial class FolderTemplateDialog : MetroWindow
    {
        private readonly string _basePath;
        private readonly string _chatName;
        private readonly string _chatType;
        private bool _suppressUpdate;

        /// <summary>Main native-folder template — read after DialogResult == true.</summary>
        public string ResultTemplate { get; private set; } = string.Empty;

        public string ResultSocialDownloadFolderTemplate { get; private set; } = string.Empty;
        public string ResultYoutubeDownloadFolderTemplate { get; private set; } = string.Empty;
        public string ResultOtherDownloadFolderTemplate { get; private set; } = string.Empty;
        public string ResultTorrentDownloadFolderTemplate { get; private set; } = string.Empty;

        public FolderTemplateDialog(ChatDto chat, string basePath)
        {
            InitializeComponent();

            _basePath  = basePath ?? string.Empty;
            _chatName  = chat.Name ?? string.Empty;
            _chatType  = chat.Type ?? "Other";

            _suppressUpdate = true;
            TxtTemplate.Text = chat.FolderTemplate ?? string.Empty;
            TxtSocialTemplate.Text = chat.SocialDownloadFolderTemplate ?? string.Empty;
            TxtYoutubeTemplate.Text = chat.YoutubeDownloadFolderTemplate ?? string.Empty;
            TxtOtherTemplate.Text = chat.OtherDownloadFolderTemplate ?? string.Empty;
            TxtTorrentTemplate.Text = chat.TorrentDownloadFolderTemplate ?? string.Empty;
            _suppressUpdate = false;

            var main = chat.FolderTemplate ?? string.Empty;
            if (Path.IsPathRooted(main))
                TxtAbsolutePath.Text = main;

            UpdatePreview();
        }

        private void Token_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var token = btn.Tag as string ?? string.Empty;

            var pos     = TxtTemplate.CaretIndex;
            var current = TxtTemplate.Text ?? string.Empty;

            if (Path.IsPathRooted(current))
                TxtAbsolutePath.Text = "No folder selected — template above will be used";

            _suppressUpdate = true;
            TxtTemplate.Text = current.Insert(Math.Clamp(pos, 0, current.Length), token);
            _suppressUpdate = false;

            TxtTemplate.CaretIndex = pos + token.Length;
            TxtTemplate.Focus();
            UpdatePreview();
        }

        private void UrlProviderToken_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            var token = btn.Tag as string ?? string.Empty;
            var target = GetFocusedUrlTemplateBox();
            if (target == null) return;

            var pos     = target.CaretIndex;
            var current = target.Text ?? string.Empty;
            _suppressUpdate = true;
            target.Text = current.Insert(Math.Clamp(pos, 0, current.Length), token);
            _suppressUpdate = false;
            target.CaretIndex = pos + token.Length;
            target.Focus();
        }

        private TextBox? GetFocusedUrlTemplateBox()
        {
            if (TxtSocialTemplate.IsKeyboardFocusWithin) return TxtSocialTemplate;
            if (TxtYoutubeTemplate.IsKeyboardFocusWithin) return TxtYoutubeTemplate;
            if (TxtOtherTemplate.IsKeyboardFocusWithin) return TxtOtherTemplate;
            if (TxtTorrentTemplate.IsKeyboardFocusWithin) return TxtTorrentTemplate;
            return TxtSocialTemplate;
        }

        private void TxtTemplate_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressUpdate) return;

            var text = TxtTemplate.Text?.Trim() ?? string.Empty;
            if (Path.IsPathRooted(text))
                TxtAbsolutePath.Text = text;
            else if (!string.IsNullOrEmpty(text))
                TxtAbsolutePath.Text = "No folder selected — template above will be used";

            UpdatePreview();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            TxtTemplate.Text = string.Empty;
            TxtAbsolutePath.Text = "No folder selected — template above will be used";
            UpdatePreview();
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description            = "Select download folder for native Telegram files from this chat",
                UseDescriptionForTitle = true,
                ShowNewFolderButton    = true,
            };

            if (!string.IsNullOrWhiteSpace(_basePath) && Directory.Exists(_basePath))
                dialog.InitialDirectory = _basePath;

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var picked = dialog.SelectedPath;
                TxtAbsolutePath.Text = picked;

                _suppressUpdate = true;
                TxtTemplate.Text = picked;
                _suppressUpdate = false;

                UpdatePreview();
            }
        }

        private const string PreviewMediaType = "Videos";

        private void UpdatePreview()
        {
            var template = TxtTemplate.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrEmpty(template))
            {
                TxtPreview.Text = Path.Combine(_basePath, PreviewMediaType, _chatName)
                                  + "  (Videos / Photos / Music / Files)";
                TxtMode.Text    = "mode: default";
                return;
            }

            var sampleDate = new DateTime(DateTime.Now.Year, DateTime.Now.Month, DateTime.Now.Day);
            var resolved   = FolderTemplateHelper.Resolve(template, PreviewMediaType, _chatName, sampleDate)
                             ?? Path.Combine(PreviewMediaType, _chatName);

            if (Path.IsPathRooted(resolved))
            {
                TxtPreview.Text = resolved;
                TxtMode.Text    = "mode: absolute path";
            }
            else
            {
                TxtPreview.Text = Path.Combine(_basePath, resolved);
                TxtMode.Text    = "mode: template";
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            ResultTemplate = TxtTemplate.Text?.Trim() ?? string.Empty;
            ResultSocialDownloadFolderTemplate = TxtSocialTemplate.Text?.Trim() ?? string.Empty;
            ResultYoutubeDownloadFolderTemplate = TxtYoutubeTemplate.Text?.Trim() ?? string.Empty;
            ResultOtherDownloadFolderTemplate = TxtOtherTemplate.Text?.Trim() ?? string.Empty;
            ResultTorrentDownloadFolderTemplate = TxtTorrentTemplate.Text?.Trim() ?? string.Empty;
            DialogResult   = true;
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e) =>
            DialogResult = false;
    }
}

using BasePlugins;
using System;
using System.IO;
using System.Threading.Tasks;
using TelegramClient.Factory.Base;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TelegramClient.Models;
using TL;
using WTelegram;

namespace TelegramClient.Factory.Factories
{

    public class Files : BaseMessage
    {
        public override MessageTypes TypeMessage => MessageTypes.Files;

        public Files(Client client, string pathFolderToSaveFiles) : base(client, pathFolderToSaveFiles)
        {
        }

        public override async Task<ResultExecute> ExecuteAsync(Message message, ChatDto chatDto)
        {
            if (message.media is not MessageMediaDocument mediaDocument)
                return new ResultExecute(chatDto.Name);

            var document = (Document)mediaDocument.document;
            var fileName = !string.IsNullOrEmpty(document.Filename) ? document.Filename : document.ID.ToString();
            var isTorrentAttachment = TorrentDownloadService.IsTorrentFileName(fileName);
            var torrentPluginEnabled = IsTorrentPluginEnabled(chatDto);

            if (!chatDto.Download.Files && !(isTorrentAttachment && torrentPluginEnabled))
                return new ResultExecute(chatDto.Name);

            if (FileDownloadIndex.IsAlreadyDownloaded(document.ID))
            {
                var existingFile = GetPathOfDuplicateFile(fileName, document.size);
                if (existingFile != null)
                    return new ResultExecute(chatDto.Name) { IsSuccess = true, FileName = fileName, ErrorMessage = $"{fileName} already downloaded (id match)" };
                FileDownloadIndex.Remove(document.ID);
            }

            var fileExist = GetPathOfDuplicateFile(fileName, document.size);
            if (fileExist != null)
            {
                FileDownloadIndex.MarkDownloaded(document.ID);
                return new ResultExecute(chatDto.Name) { IsSuccess = true, FileName = fileName, ErrorMessage = $"{fileName} is exist on {fileExist}" };
            }

            if (isTorrentAttachment && torrentPluginEnabled)
                return await DownloadTorrentAttachmentAsync(document, chatDto, fileName);

            return await DownloadDocumentAsync(document, chatDto, fileName, TypeMessage.ToString());
        }

        private static bool IsTorrentPluginEnabled(ChatDto chatDto) =>
            chatDto.EnabledPlugins.TryGetValue("Torrent", out var enabled) && enabled;

        private async Task<ResultExecute> DownloadTorrentAttachmentAsync(
            Document document, ChatDto chatDto, string torrentFileName)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "TelegramAutoDownload", "torrents");
            Directory.CreateDirectory(tempDir);
            var tempTorrentPath = Path.Combine(tempDir, torrentFileName);

            var telegramResult = await DownloadDocumentAsync(
                document, chatDto, torrentFileName, "Torrent", tempTorrentPath, markDownloadedInIndex: false).ConfigureAwait(false);

            if (!telegramResult.IsSuccess)
                return telegramResult;

            var outputFolder = PluginFolderPathHelper.CombineUnderDownloadRoot(
                PathFolderToSaveFiles,
                string.IsNullOrWhiteSpace(chatDto.TorrentDownloadFolderTemplate)
                    ? null
                    : chatDto.TorrentDownloadFolderTemplate,
                "Torrent",
                chatDto.Name,
                "{Platform}/{ChatName}");

            try
            {
                var result = await TorrentDownloadService.DownloadAsync(
                    chatDto.Name,
                    outputFolder,
                    magnetUri: null,
                    torrentFilePath: tempTorrentPath,
                    pluginName: "Torrent",
                    onProgress: OnProgress,
                    onComplete: OnComplete,
                    hostCancellationToken: default).ConfigureAwait(false);

                if (result.IsSuccess)
                    FileDownloadIndex.MarkDownloaded(document.ID);

                return result;
            }
            finally
            {
                try
                {
                    if (File.Exists(tempTorrentPath))
                        File.Delete(tempTorrentPath);
                    var partPath = GetPartFilePath(tempTorrentPath);
                    if (File.Exists(partPath))
                        File.Delete(partPath);
                }
                catch
                {
                    // Best-effort temp cleanup
                }
            }
        }
    }
}

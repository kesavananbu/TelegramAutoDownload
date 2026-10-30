using TelegramAutoDownload.Models;

namespace TelegramAutoDownload.Services
{
    public static class ConfigExportHelper
    {
        /// <summary>
        /// Returns a copy suitable for JSON export. Secrets are stripped unless explicitly requested.
        /// </summary>
        public static ConfigParams ForExport(ConfigParams source, bool includeSecrets)
        {
            var copy = new ConfigParams
            {
                AppId = source.AppId,
                ApiHash = includeSecrets ? source.ApiHash : string.Empty,
                BotToken = includeSecrets ? source.BotToken : string.Empty,
                ChatId = source.ChatId,
                PathSaveFile = source.PathSaveFile,
                FolderLayout = source.FolderLayout,
                DownloadThreads = source.DownloadThreads,
                NotifyOnStartup = source.NotifyOnStartup,
                NotifyOnProgress = source.NotifyOnProgress,
                NotifyOnComplete = source.NotifyOnComplete,
                NotifyOnError = source.NotifyOnError,
                Chats = source.Chats,
            };
            return copy;
        }
    }
}

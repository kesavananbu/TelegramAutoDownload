using System;
using System.Collections.Generic;
using BasePlugins;
using TelegramClient.Models;

namespace TelegramAutoDownload.Models
{
    public class ConfigParams
    {
        // Telegram client credentials — stored in config.txt; fall back to .env on first run
        public int AppId { get; set; } = int.TryParse(Environment.GetEnvironmentVariable("APP_ID"), out var id) ? id : 0;
        public string ApiHash { get; set; } = Environment.GetEnvironmentVariable("API_HASH") ?? string.Empty;

        // Notification bot credentials — stored in config.txt; fall back to .env on first run
        public string BotToken { get; set; } = Environment.GetEnvironmentVariable("BOT_TOKEN") ?? string.Empty;
        public string ChatId { get; set; } = Environment.GetEnvironmentVariable("CHAT_ID") ?? string.Empty;

        public int DownloadThreads { get; set; } = 3;

        // Headless rate-limit settings (used by Phase 3 TokenBucketRateLimiter).
        // Defaults are conservative: 1 history request / second, burst of 5.
        // Old configs that lack these properties pick up the defaults.
        public double ScannerApiCapacity { get; set; } = 5.0;
        public double ScannerApiRefillPerSecond { get; set; } = 1.0;

        // Notification preferences — which events should trigger a Telegram bot message
        public bool NotifyOnStartup { get; set; } = true;
        public bool NotifyOnProgress { get; set; } = true;
        public bool NotifyOnComplete { get; set; } = true;
        public bool NotifyOnError { get; set; } = true;

        /// <summary>When true, completed downloads are removed from the UI list after a few seconds.</summary>
        public bool AutoCleanDownloads { get; set; } = true;
        public List<ChatDto> Chats { get; set; } = [];
        public string PathSaveFile { get; set; }

        /// <summary>
        /// Per-chat yt-dlp quality UI was removed; every chat always uses best video+audio.
        /// Call after deserializing config or importing settings so legacy JSON values are overwritten.
        /// </summary>
        public void NormalizeYtDlpQualityForAllChats()
        {
            foreach (var chat in Chats)
                chat.YtdlpQuality = YtdlpFormatHelper.HighestVideoQuality;
        }
    }
}

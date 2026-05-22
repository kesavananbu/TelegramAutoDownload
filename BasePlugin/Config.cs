using System.Collections.Generic;
using System.Threading;

namespace BasePlugins
{
    public class Config
    {
        public required string ChatName { get; set; }
        public required string Text { get; set; }
        public required string PathSaveFile { get; set; }
        // Keys are PluginName values; missing key means enabled by default
        public Dictionary<string, bool> EnabledPlugins { get; set; } = new();
        // Cancellation token registered by the host so the UI cancel button can abort the download
        public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
        // yt-dlp quality — host always passes YtdlpFormatHelper.HighestVideoQuality (best video+audio).
        public string YtdlpQuality { get; set; } = YtdlpFormatHelper.HighestVideoQuality;

        /// <summary>
        /// Relative folder under <see cref="PathSaveFile"/> for SocialMedia plugin (yt-dlp).
        /// Tokens: {Platform}, {ChatName}. Empty = default "{Platform}/{ChatName}".
        /// </summary>
        public string? SocialDownloadFolderTemplate { get; set; }

        /// <summary>
        /// Relative folder under <see cref="PathSaveFile"/> for YouTube plugin (yt-dlp).
        /// Tokens: {Platform}, {ChatName}. Empty = default "{Platform}/{ChatName}"; %(channel)s is appended by the plugin.
        /// </summary>
        public string? YoutubeDownloadFolderTemplate { get; set; }

        /// <summary>Relative folder for the Other (direct URL) plugin. Tokens: {Platform}, {ChatName}.</summary>
        public string? OtherDownloadFolderTemplate { get; set; }

        /// <summary>Relative folder for the Torrent plugin. Tokens: {Platform}, {ChatName}.</summary>
        public string? TorrentDownloadFolderTemplate { get; set; }

        /// <summary>Local path to a .torrent file (e.g. after Telegram attachment download).</summary>
        public string? LocalTorrentPath { get; set; }
    }
}

namespace BasePlugins
{
    /// <summary>
    /// Maps per-chat quality labels to yt-dlp format selector strings.
    /// The format selectors follow yt-dlp's priority syntax: preferred streams are listed
    /// first, fallback options after the slash.
    /// </summary>
    public static class YtdlpFormatHelper
    {
        /// <summary>
        /// Fixed app behaviour: best available video+audio (no resolution cap in the primary selector).
        /// Legacy config files may still list other labels; they are normalized on load to this value.
        /// </summary>
        public const string HighestVideoQuality = "VIDEO";

        /// <summary>Legacy per-chat labels still understood by <see cref="GetFormatString"/>.</summary>
        public static readonly string[] QualityOptions =
        [
            "VIDEO", "4K", "1080p", "720p", "480p", "AUDIO"
        ];

        /// <summary>
        /// Returns the yt-dlp format string for the given quality label.
        /// "VIDEO" = best available video+audio. "AUDIO" = audio-only.
        /// Old values "best" and "audio" are accepted for backward compatibility.
        /// Unknown labels fall back to best video.
        /// </summary>
        public static string GetFormatString(string? quality) => quality switch
        {
            "4K"    => "bestvideo[height<=2160][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=2160]+bestaudio/best[height<=2160]",
            "1080p" => "bestvideo[height<=1080][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=1080]+bestaudio/best[height<=1080]",
            "720p"  => "bestvideo[height<=720][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=720]+bestaudio/best[height<=720]",
            "480p"  => "bestvideo[height<=480][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=480]+bestaudio/best[height<=480]",
            "AUDIO" or "audio" => "bestaudio[ext=m4a]/bestaudio",
            _       => "bestvideo[ext=mp4]+bestaudio[ext=m4a]/bestvideo+bestaudio/best"  // "VIDEO", "best", or any unknown
        };

        /// <summary>Returns true when the quality label requests audio-only output.</summary>
        public static bool IsAudioOnly(string? quality) => quality is "AUDIO" or "audio";
    }
}

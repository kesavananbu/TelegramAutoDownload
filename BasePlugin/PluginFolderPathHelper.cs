using System;
using System.IO;
using System.Linq;

namespace BasePlugins
{
    /// <summary>
    /// Builds folder paths under the download root for URL plugins (yt-dlp).
    /// Tokens: <c>{Platform}</c> (e.g. YouTube, TikTok), <c>{ChatName}</c> (sanitized).
    /// </summary>
    public static class PluginFolderPathHelper
    {
        /// <summary>
        /// Resolves a relative path template and combines it with <paramref name="downloadRoot"/>.
        /// When <paramref name="template"/> is null/whitespace, <paramref name="defaultTemplate"/> is used.
        /// </summary>
        public static string CombineUnderDownloadRoot(
            string downloadRoot,
            string? template,
            string platformName,
            string chatName,
            string defaultTemplate)
        {
            if (string.IsNullOrWhiteSpace(downloadRoot))
                downloadRoot = Directory.GetCurrentDirectory();

            var rel = ResolveRelativePath(template, platformName, chatName, defaultTemplate);
            var parts = rel
                .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => SanitizeSegment(s.Trim()))
                .Where(s => s.Length > 0)
                .ToArray();

            return parts.Length == 0 ? downloadRoot : Path.Combine(new[] { downloadRoot }.Concat(parts).ToArray());
        }

        public static string ResolveRelativePath(
            string? template,
            string platformName,
            string chatName,
            string defaultTemplate)
        {
            var t = string.IsNullOrWhiteSpace(template) ? defaultTemplate : template.Trim();
            var p = SanitizeSegment(platformName);
            var c = SanitizeSegment(chatName);
            return t
                .Replace("{Platform}", p, StringComparison.OrdinalIgnoreCase)
                .Replace("{ChatName}", c, StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeSegment(string name)
        {
            foreach (var ch in Path.GetInvalidFileNameChars())
                name = name.Replace(ch, '_');
            return name.Trim();
        }
    }
}

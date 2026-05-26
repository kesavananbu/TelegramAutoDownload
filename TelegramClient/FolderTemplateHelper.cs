using System;
using System.IO;
using System.Linq;
using TelegramClient.Models;

namespace TelegramClient
{
    /// <summary>
    /// Resolves per-chat folder templates into concrete paths.
    /// Extracted from BaseMessage so the resolution logic can be unit-tested independently.
    /// </summary>
    public static class FolderTemplateHelper
    {
        /// <summary>
        /// Supported tokens: {Type}, {ChatName}, {Year}, {Month}, {Day}.
        /// </summary>
        public static readonly string[] SupportedTokens =
            ["{Type}", "{ChatName}", "{Year}", "{Month}", "{Day}"];

        /// <summary>
        /// Resolves a folder template string.
        /// Returns <c>null</c> when the template is null or whitespace (caller uses default layout).
        ///
        /// Two modes:
        /// • Relative template — contains tokens or plain segments. Result is relative and must be
        ///   combined with the base download path by the caller.
        ///   Example: "{ChatName}/{Year}-{Month}" → "MyChannel/2026-05"
        ///
        /// • Absolute path — the template starts with a drive letter or UNC root (Path.IsPathRooted).
        ///   Returned unchanged so the caller can detect it with Path.IsPathRooted and use it directly
        ///   without combining with the base download path.
        ///   Example: "C:\Downloads\MyChannel" → "C:\Downloads\MyChannel"
        /// </summary>
        /// <param name="template">Template string or absolute path</param>
        /// <param name="type">Value for {Type} token, e.g. "Videos"</param>
        /// <param name="chatName">Value for {ChatName} token</param>
        /// <param name="at">Override timestamp (defaults to DateTime.Now)</param>
        public static string? Resolve(string? template, string type, string chatName, DateTime? at = null)
        {
            if (string.IsNullOrWhiteSpace(template)) return null;

            var now  = at ?? DateTime.Now;
            var safe = SanitizeName(chatName);

            // Apply token substitution regardless of whether the path is absolute.
            // An absolute path like "C:\Downloads\{ChatName}" must still have its
            // tokens replaced so the caller gets a fully-resolved concrete path.
            var resolved = template
                .Replace("{Type}",     type,                 StringComparison.OrdinalIgnoreCase)
                .Replace("{ChatName}", safe,                  StringComparison.OrdinalIgnoreCase)
                .Replace("{Year}",     now.ToString("yyyy"),  StringComparison.OrdinalIgnoreCase)
                .Replace("{Month}",    now.ToString("MM"),    StringComparison.OrdinalIgnoreCase)
                .Replace("{Day}",      now.ToString("dd"),    StringComparison.OrdinalIgnoreCase);

            // Sanitize invalid path chars — but only for relative templates,
            // because absolute paths already have a valid root (e.g. "C:\").
            if (!Path.IsPathRooted(resolved))
            {
                foreach (char c in Path.GetInvalidPathChars())
                    resolved = resolved.Replace(c, '_');
            }

            return resolved;
        }

        /// <summary>
        /// Resolves the directory for a download, honoring per-chat template then global layout.
        /// </summary>
        public static string ResolveDownloadDirectory(
            string downloadRoot,
            FolderLayoutMode layout,
            string? folderTemplate,
            string typeFolder,
            string chatName)
        {
            var folderName = SanitizeChatName(chatName);
            var resolvedTemplate = Resolve(folderTemplate, typeFolder, folderName);
            if (resolvedTemplate != null)
            {
                return Path.IsPathRooted(resolvedTemplate)
                    ? resolvedTemplate
                    : CombineRelative(downloadRoot ?? string.Empty, resolvedTemplate);
            }

            var relative = ResolveDefaultRelativePath(layout, typeFolder, folderName);
            return CombineRelative(downloadRoot ?? string.Empty, relative);
        }

        /// <summary>
        /// Relative path segments under the download root (no custom template).
        /// </summary>
        public static string ResolveDefaultRelativePath(FolderLayoutMode layout, string typeFolder, string chatName)
        {
            var safe = SanitizeChatName(chatName);
            return layout switch
            {
                FolderLayoutMode.ChatFirst    => $"{safe}/{typeFolder}",
                FolderLayoutMode.ChatCombined => safe,
                _                             => $"{typeFolder}/{safe}",
            };
        }

        private static string CombineRelative(string root, string relative)
        {
            var parts = relative.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? root : Path.Combine(new[] { root }.Concat(parts).ToArray());
        }

        public static string SanitizeChatName(string chatName)
        {
            var name = chatName.TrimEnd();
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, ' ');
            return name.Replace('~', ' ').TrimEnd();
        }

        private static string SanitizeName(string name) => SanitizeChatName(name);
    }
}

using System;
using System.Linq;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TL;

namespace TelegramClient
{
    /// <summary>
    /// Classifies <see cref="Document"/> media into the same <see cref="MessageTypes"/>
    /// buckets used by download handlers. Keeps <see cref="Factory.Service.FactoryMessagesService"/>
    /// and <see cref="TelegramApp"/> in sync (e.g. MKV with generic MIME still counts as video).
    /// </summary>
    public static class DocumentMediaKindHelper
    {
        private static readonly string[] VideoExtensions =
        [
            ".mkv", ".mp4", ".webm", ".mov", ".avi", ".m4v", ".mpeg", ".mpg", ".wmv", ".flv", ".3gp", ".ogv"
        ];

        /// <summary>
        /// Maps a Telegram document to Photos / Videos / Music / Files (not Message).
        /// </summary>
        public static MessageTypes GetMessageType(Document document)
        {
            var mime = document.mime_type ?? string.Empty;

            if (mime.Contains("image", StringComparison.OrdinalIgnoreCase))
                return MessageTypes.Photos;
            if (mime.Contains("video", StringComparison.OrdinalIgnoreCase))
                return MessageTypes.Videos;
            if (mime.Contains("audio", StringComparison.OrdinalIgnoreCase))
                return MessageTypes.Music;

            if (document.attributes?.Any(a => a is DocumentAttributeVideo) == true)
                return MessageTypes.Videos;

            var ext = GetFilenameExtension(document);
            if (IsVideoFileExtension(ext))
                return MessageTypes.Videos;

            return MessageTypes.Files;
        }

        public static bool IsVideoFileExtension(string extensionWithDot)
        {
            if (string.IsNullOrEmpty(extensionWithDot)) return false;
            return Array.Exists(VideoExtensions,
                e => extensionWithDot.Equals(e, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetFilenameExtension(Document document)
        {
            if (document.attributes == null) return string.Empty;
            foreach (var attr in document.attributes)
            {
                if (attr is DocumentAttributeFilename fn && !string.IsNullOrEmpty(fn.file_name))
                {
                    var dot = fn.file_name.LastIndexOf('.');
                    if (dot >= 0 && dot < fn.file_name.Length - 1)
                        return fn.file_name[dot..].ToLowerInvariant();
                }
            }

            if (!string.IsNullOrEmpty(document.Filename))
            {
                var dot = document.Filename.LastIndexOf('.');
                if (dot >= 0 && dot < document.Filename.Length - 1)
                    return document.Filename[dot..].ToLowerInvariant();
            }

            return string.Empty;
        }
    }
}

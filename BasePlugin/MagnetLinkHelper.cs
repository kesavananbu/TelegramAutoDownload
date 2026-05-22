using System;

namespace BasePlugins
{
    public static class MagnetLinkHelper
    {
        /// <summary>
        /// Extracts a magnet URI from message text (start of line or embedded in caption).
        /// </summary>
        public static string? TryExtract(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var trimmed = text.Trim();
            if (trimmed.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                return TakeUntilWhitespace(trimmed);

            var idx = trimmed.IndexOf("magnet:", StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            return TakeUntilWhitespace(trimmed[idx..]);
        }

        public static bool ContainsMagnetLink(string? text) => TryExtract(text) != null;

        private static string TakeUntilWhitespace(string value)
        {
            var i = value.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            return i < 0 ? value : value[..i];
        }
    }
}

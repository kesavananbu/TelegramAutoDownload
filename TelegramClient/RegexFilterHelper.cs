using Serilog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TelegramClient
{
    /// <summary>
    /// Shared regex filter validation and matching for file names and text capture.
    /// Headless/WPF hosts can call <see cref="TryValidatePattern"/> before persisting patterns.
    /// </summary>
    public static class RegexFilterHelper
    {
        public static readonly TimeSpan DefaultMatchTimeout = TimeSpan.FromMilliseconds(250);

        private static readonly ConcurrentDictionary<string, byte> LoggedInvalidPatterns = new();

        /// <summary>
        /// Validates that a pattern compiles with ignore-case and match timeout options.
        /// </summary>
        public static bool TryValidatePattern(string pattern, out string? errorMessage)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                errorMessage = "Pattern must not be empty.";
                return false;
            }

            try
            {
                _ = CreateFilterRegex(pattern);
                errorMessage = null;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Returns true when <paramref name="input"/> matches <paramref name="pattern"/>.
        /// Invalid patterns are skipped (logged once); timeouts are treated as no match.
        /// </summary>
        public static bool IsMatch(string pattern, string? input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            if (!TryValidatePattern(pattern, out _))
            {
                LogInvalidPatternOnce(pattern);
                return false;
            }

            try
            {
                return CreateFilterRegex(pattern).IsMatch(input);
            }
            catch (RegexMatchTimeoutException)
            {
                Log.Warning("Regex filter timed out after {TimeoutMs} ms for pattern length {Len}",
                    DefaultMatchTimeout.TotalMilliseconds, pattern.Length);
                return false;
            }
        }

        /// <summary>
        /// First matching pattern in <paramref name="patterns"/> that matches <paramref name="fileName"/>, or null.
        /// </summary>
        public static string? FindFirstMatchingIgnorePattern(IEnumerable<string> patterns, string? fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return null;

            foreach (var pattern in patterns)
            {
                if (IsMatch(pattern, fileName))
                    return pattern;
            }

            return null;
        }

        private static Regex CreateFilterRegex(string pattern) =>
            new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, DefaultMatchTimeout);

        private static void LogInvalidPatternOnce(string pattern)
        {
            if (LoggedInvalidPatterns.TryAdd(pattern, 0))
                Log.Warning("Skipping invalid regex filter pattern: {Pattern}", pattern);
        }
    }
}

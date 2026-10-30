using System;
using System.Linq;

namespace TelegramAutoDownload.Services
{
    public static class AppVersionCompare
    {
        /// <summary>
        /// True when <paramref name="candidate"/> is strictly newer than <paramref name="current"/>.
        /// Uses <see cref="Version"/> when both parse; otherwise compares numeric dot-separated segments.
        /// </summary>
        public static bool IsNewer(string candidate, string current)
        {
            if (Version.TryParse(candidate, out var v1) && Version.TryParse(current, out var v2))
                return v1 > v2;
            return CompareNumericSegments(candidate, current) > 0;
        }

        public static int CompareNumericSegments(string left, string right)
        {
            var a = left.Split('.').Select(ParseSegment).ToArray();
            var b = right.Split('.').Select(ParseSegment).ToArray();
            var len = Math.Max(a.Length, b.Length);
            for (var i = 0; i < len; i++)
            {
                var av = i < a.Length ? a[i] : 0;
                var bv = i < b.Length ? b[i] : 0;
                if (av != bv)
                    return av.CompareTo(bv);
            }
            return 0;
        }

        private static int ParseSegment(string segment)
        {
            var digits = new string(segment.TakeWhile(char.IsDigit).ToArray());
            return int.TryParse(digits, out var n) ? n : 0;
        }
    }
}

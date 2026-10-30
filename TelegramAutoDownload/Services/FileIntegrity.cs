using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TelegramAutoDownload.Services
{
    public static class FileIntegrity
    {
        private static readonly Regex GitHubReleaseAssetName =
            new(@"^TelegramAutoDownload_v\d+\.\d+\.\d+_(Setup\.exe|Portable\.zip)$", RegexOptions.CultureInvariant);

        public static bool IsAllowedUpdateAssetName(string? name) =>
            !string.IsNullOrEmpty(name) && GitHubReleaseAssetName.IsMatch(name);

        public static bool IsAllowedGitHubDownloadUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;
            if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return false;
            return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
        }

        public static string? ParseGitHubAssetDigest(string? digestField)
        {
            if (string.IsNullOrWhiteSpace(digestField))
                return null;
            const string prefix = "sha256:";
            if (!digestField.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            var hex = digestField[prefix.Length..].Trim();
            return hex.Length == 64 && hex.All(c => Uri.IsHexDigit(c)) ? hex.ToLowerInvariant() : null;
        }

        public static bool TryGetHashFromSumsFile(string sumsContent, string fileName, out string sha256Hex)
        {
            sha256Hex = string.Empty;
            if (string.IsNullOrWhiteSpace(sumsContent) || string.IsNullOrWhiteSpace(fileName))
                return false;

            foreach (var rawLine in sumsContent.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    continue;

                var hash = parts[0].Trim();
                var name = parts[^1].Trim().TrimStart('*');
                if (!string.Equals(name, fileName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
                    return false;

                sha256Hex = hash.ToLowerInvariant();
                return true;
            }

            return false;
        }

        public static async Task<string> ComputeSha256HexAsync(string filePath)
        {
            await using var stream = File.OpenRead(filePath);
            var hash = await SHA256.HashDataAsync(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}

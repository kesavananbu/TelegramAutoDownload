using FluentAssertions;
using TelegramAutoDownload.Services;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class AutoUpdateIntegrityTests
    {
        [Theory]
        [InlineData("TelegramAutoDownload_v2.9.3_Setup.exe", true)]
        [InlineData("TelegramAutoDownload_v2.9.3_Portable.zip", true)]
        [InlineData("evil.exe", false)]
        [InlineData("TelegramAutoDownload_v2.9.3_Setup.exe\" & calc", false)]
        [InlineData("..\\TelegramAutoDownload_v1.0.0_Setup.exe", false)]
        public void IsAllowedUpdateAssetName_ValidatesAllowlist(string name, bool allowed)
        {
            FileIntegrity.IsAllowedUpdateAssetName(name).Should().Be(allowed);
        }

        [Theory]
        [InlineData("https://github.com/il90il90/TelegramAutoDownload/releases/download/v1/a.exe", true)]
        [InlineData("https://objects.githubusercontent.com/github-production-release-asset/abc", true)]
        [InlineData("https://evil.example/x.exe", false)]
        [InlineData("http://github.com/x", false)]
        public void IsAllowedGitHubDownloadUrl_ValidatesHost(string url, bool allowed)
        {
            FileIntegrity.IsAllowedGitHubDownloadUrl(url).Should().Be(allowed);
        }

        [Fact]
        public void ParseGitHubAssetDigest_AcceptsSha256Prefix()
        {
            var hex = new string('a', 64);
            FileIntegrity.ParseGitHubAssetDigest($"sha256:{hex}")
                .Should().Be(hex);
        }

        [Theory]
        [InlineData("2.10.0", "2.9.3", true)]
        [InlineData("2.9.3", "2.10.0", false)]
        [InlineData("2.9.10", "2.9.3", true)]
        public void IsNewer_UsesNumericSegmentsWhenNotVersion(string candidate, string current, bool newer)
        {
            AppVersionCompare.IsNewer(candidate, current).Should().Be(newer);
        }

        [Fact]
        public void TryGetHashFromSumsFile_FindsAssetLine()
        {
            const string sums = """
                deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef  TelegramAutoDownload_v2.0.0_Setup.exe
                """;

            FileIntegrity.TryGetHashFromSumsFile(sums, "TelegramAutoDownload_v2.0.0_Setup.exe", out var hash)
                .Should().BeTrue();
            hash.Should().Be("deadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeefdeadbeef");
        }
    }
}

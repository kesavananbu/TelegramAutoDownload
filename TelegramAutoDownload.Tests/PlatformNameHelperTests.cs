using FluentAssertions;
using SocialMediaPlugin;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class PlatformNameHelperTests
    {
        [Theory]
        [InlineData("https://m.facebook.com/watch?v=1", "Facebook")]
        [InlineData("https://web.facebook.com/foo", "Facebook")]
        [InlineData("https://facebook.com/foo", "Facebook")]
        [InlineData("https://www.tiktok.com/@u/video/1", "TikTok")]
        [InlineData("https://vm.tiktok.com/abc/", "TikTok")]
        [InlineData("https://www.youtube.com/watch?v=x", "YouTube")]
        [InlineData("https://youtu.be/abc", "YouTube")]
        public void GetPlatformName_KnownHosts_ReturnsFriendlyName(string url, string expected) =>
            PlatformNameHelper.GetPlatformName(url).Should().Be(expected);

        [Fact]
        public void GetPlatformName_UnknownHost_ReturnsSocialMedia() =>
            PlatformNameHelper.GetPlatformName("https://unknown-example-12345.test/video")
                .Should().Be("SocialMedia");
    }
}

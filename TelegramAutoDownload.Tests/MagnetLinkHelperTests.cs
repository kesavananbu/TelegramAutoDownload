using BasePlugins;
using FluentAssertions;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class MagnetLinkHelperTests
    {
        [Theory]
        [InlineData("magnet:?xt=urn:btih:abc123&dn=test", "magnet:?xt=urn:btih:abc123&dn=test")]
        [InlineData("  magnet:?xt=urn:btih:abc  ", "magnet:?xt=urn:btih:abc")]
        [InlineData("Download this: magnet:?xt=urn:btih:abc123&dn=test", "magnet:?xt=urn:btih:abc123&dn=test")]
        [InlineData("MAGNET:?xt=urn:btih:abc", "MAGNET:?xt=urn:btih:abc")]
        public void TryExtract_ReturnsMagnetUri(string input, string expected)
        {
            MagnetLinkHelper.TryExtract(input).Should().Be(expected);
        }

        [Theory]
        [InlineData("https://example.com/file.torrent")]
        [InlineData("plain text")]
        [InlineData("")]
        public void TryExtract_ReturnsNull_ForNonMagnet(string input)
        {
            MagnetLinkHelper.TryExtract(input).Should().BeNull();
        }
    }
}

using BasePlugins;
using FluentAssertions;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class PluginFolderPathHelperTests
    {
        [Fact]
        public void Combine_DefaultTemplate_MatchesLegacyLayout()
        {
            var root = @"C:\dl";
            var path = PluginFolderPathHelper.CombineUnderDownloadRoot(
                root, null, "TikTok", "My Chat", "{Platform}/{ChatName}");
            path.Should().Be(@"C:\dl\TikTok\My Chat");
        }

        [Fact]
        public void Combine_CustomTemplate_ReordersTokens()
        {
            var root = @"C:\dl";
            var path = PluginFolderPathHelper.CombineUnderDownloadRoot(
                root, "{ChatName}/{Platform}", "YouTube", "News", "{Platform}/{ChatName}");
            path.Should().Be(@"C:\dl\News\YouTube");
        }

        [Fact]
        public void Combine_SanitizesInvalidFileNameChars()
        {
            var root = @"C:\dl";
            var path = PluginFolderPathHelper.CombineUnderDownloadRoot(
                root, "{Platform}", "Bad:name", "X", "{Platform}");
            path.Should().Be(@"C:\dl\Bad_name");
        }
    }
}

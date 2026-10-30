using FluentAssertions;
using TelegramAutoDownload.Services;
using TelegramAutoDownload.Models;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class ConfigExportHelperTests
    {
        [Fact]
        public void ForExport_ByDefault_StripsApiHashAndBotToken()
        {
            var source = new ConfigParams
            {
                AppId = 1,
                ApiHash = "secret_hash",
                BotToken = "bot:token",
            };

            var exported = ConfigExportHelper.ForExport(source, includeSecrets: false);

            exported.ApiHash.Should().BeEmpty();
            exported.BotToken.Should().BeEmpty();
            exported.AppId.Should().Be(1);
        }

        [Fact]
        public void ForExport_WithIncludeSecrets_PreservesCredentials()
        {
            var source = new ConfigParams { ApiHash = "secret_hash", BotToken = "bot:token" };
            var exported = ConfigExportHelper.ForExport(source, includeSecrets: true);
            exported.ApiHash.Should().Be("secret_hash");
            exported.BotToken.Should().Be("bot:token");
        }
    }
}

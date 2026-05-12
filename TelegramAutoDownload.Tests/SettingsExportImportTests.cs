using FluentAssertions;
using Newtonsoft.Json;
using TelegramAutoDownload.Models;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class SettingsExportImportTests
    {
        [Fact]
        public void ConfigParams_SerializeRoundTrip_PreservesSecretsAndChats()
        {
            var original = new ConfigParams
            {
                AppId           = 123456,
                ApiHash         = "api_hash_secret",
                BotToken        = "bot:token",
                ChatId          = "-1001",
                PathSaveFile    = @"D:\Downloads\TAD",
                DownloadThreads = 5,
                NotifyOnStartup = false,
                Chats =
                [
                    new TelegramClient.Models.ChatDto { Id = 1, Name = "Test", Selected = true },
                ],
            };

            var json = JsonConvert.SerializeObject(original, Formatting.Indented);
            json.Should().Contain("api_hash_secret").And.Contain("123456");

            var back = JsonConvert.DeserializeObject<ConfigParams>(json);
            back.Should().NotBeNull();
            back!.AppId.Should().Be(123456);
            back.ApiHash.Should().Be("api_hash_secret");
            back.BotToken.Should().Be("bot:token");
            back.ChatId.Should().Be("-1001");
            back.PathSaveFile.Should().Be(@"D:\Downloads\TAD");
            back.DownloadThreads.Should().Be(5);
            back.NotifyOnStartup.Should().BeFalse();
            back.Chats.Should().HaveCount(1);
            back.Chats[0].Name.Should().Be("Test");
        }
    }
}

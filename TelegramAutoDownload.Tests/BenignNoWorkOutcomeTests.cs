using BasePlugins;
using FluentAssertions;
using TelegramClient.Factory.Service;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class BenignNoWorkOutcomeTests
    {
        [Fact]
        public void IsBenignNoWorkOutcome_PlainTextWithoutUrl_ReturnsTrue()
        {
            var r = new ResultExecute("chat")
            {
                IsSuccess = false,
                ErrorMessage = "No http/https URL in this message for URL plugins to download.",
            };
            FactoryMessagesService.IsBenignNoWorkOutcome(r).Should().BeTrue();
        }

        [Fact]
        public void IsUserCancelledOutcome_UserPressedCancel_ReturnsTrue()
        {
            var r = new ResultExecute("chat")
            {
                IsSuccess = false,
                ErrorMessage = "Cancelled by user",
                FileName = "video.mkv",
            };
            FactoryMessagesService.IsUserCancelledOutcome(r).Should().BeTrue();
        }

        [Fact]
        public void IsUserCancelledOutcome_RealFailure_ReturnsFalse()
        {
            var r = new ResultExecute("chat")
            {
                IsSuccess = false,
                ErrorMessage = "Download cancelled (no progress)",
                FileName = "video.mkv",
            };
            FactoryMessagesService.IsBenignNoWorkOutcome(r).Should().BeFalse();
        }
    }
}

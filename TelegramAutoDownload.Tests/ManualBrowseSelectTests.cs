using System.Collections.Generic;
using FluentAssertions;
using TelegramClient;
using TelegramClient.Models;
using TL;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class ManualBrowseSelectTests
    {
        private static ChatDto EmptyChat() =>
            new()
            {
                Name               = "Test",
                IgnoreFileByRegex = new List<string>(),
            };

        [Fact]
        public void Plain_text_without_http_is_selectable_in_browse()
        {
            var msg = new Message { message = "hello only" };
            TelegramApp.CanSelectMessageForManualBrowse(msg, EmptyChat()).Should().BeTrue();
        }

        [Fact]
        public void MessageMediaWebPage_is_selectable()
        {
            var msg = new Message { message = "caption", media = new MessageMediaWebPage() };
            TelegramApp.CanSelectMessageForManualBrowse(msg, EmptyChat()).Should().BeTrue();
        }

        [Fact]
        public void Native_photo_is_selectable()
        {
            var msg = new Message { media = new MessageMediaPhoto() };
            TelegramApp.CanSelectMessageForManualBrowse(msg, EmptyChat()).Should().BeTrue();
        }

        [Fact]
        public void Empty_message_with_no_media_not_selectable()
        {
            var msg = new Message();
            TelegramApp.CanSelectMessageForManualBrowse(msg, EmptyChat()).Should().BeFalse();
        }
    }
}

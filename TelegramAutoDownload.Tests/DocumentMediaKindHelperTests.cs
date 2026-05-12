using FluentAssertions;
using TelegramClient;
using TelegramClient.Factory.FactoriesMessages.Enum;
using TL;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class DocumentMediaKindHelperTests
    {
        [Fact]
        public void MkvWithOctetStreamMime_IsVideo()
        {
            var doc = new Document
            {
                mime_type = "application/octet-stream",
                attributes = [new DocumentAttributeFilename { file_name = "movie.mkv" }]
            };
            DocumentMediaKindHelper.GetMessageType(doc).Should().Be(MessageTypes.Videos);
        }

        [Fact]
        public void Mp4FilenameWithoutVideoMime_IsVideo()
        {
            var doc = new Document
            {
                mime_type = "application/octet-stream",
                attributes = [new DocumentAttributeFilename { file_name = "clip.MP4" }]
            };
            DocumentMediaKindHelper.GetMessageType(doc).Should().Be(MessageTypes.Videos);
        }

        [Fact]
        public void DocumentAttributeVideoWithoutVideoMime_IsVideo()
        {
            var doc = new Document
            {
                mime_type = "application/octet-stream",
                attributes = [new DocumentAttributeVideo { duration = 120 }]
            };
            DocumentMediaKindHelper.GetMessageType(doc).Should().Be(MessageTypes.Videos);
        }

        [Fact]
        public void PdfRemainsFile()
        {
            var doc = new Document
            {
                mime_type = "application/pdf",
                attributes = [new DocumentAttributeFilename { file_name = "a.pdf" }]
            };
            DocumentMediaKindHelper.GetMessageType(doc).Should().Be(MessageTypes.Files);
        }
    }
}

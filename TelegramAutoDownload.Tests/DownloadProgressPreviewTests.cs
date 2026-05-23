using FluentAssertions;
using TelegramAutoDownload.Services;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class DownloadProgressPreviewTests
    {
        [Theory]
        [InlineData("file_123")]
        [InlineData("video_456.mp4")]
        [InlineData("🧲 magnet:?xt=urn:btih:abc")]
        [InlineData("🔗 https://youtu.be/abc")]
        [InlineData("📝 captured text")]
        public void IsPlaceholderPreviewName_recognizes_preview_labels(string name)
        {
            DownloadProgressService.IsPlaceholderPreviewName(name).Should().BeTrue();
        }

        [Theory]
        [InlineData("OnlyFans - FitandFlirtyHotwife")]
        [InlineData("movie.mp4")]
        public void IsPlaceholderPreviewName_rejects_real_names(string name)
        {
            DownloadProgressService.IsPlaceholderPreviewName(name).Should().BeFalse();
        }
    }
}

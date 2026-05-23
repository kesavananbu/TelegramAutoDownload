using FluentAssertions;
using System;
using System.IO;
using TelegramAutoDownload.Services;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class DiskSpaceServiceTests
    {
        [Theory]
        [InlineData(500, "500 B")]
        [InlineData(2048, "2 KB")]
        [InlineData(5_242_880, "5.0 MB")]
        [InlineData(3_221_225_472, "3.0 GB")]
        public void FormatBytes_FormatsCorrectly(long bytes, string expected)
        {
            DiskSpaceService.FormatBytes(bytes).Should().Be(expected);
        }

        [Fact]
        public void CalculateFolderSize_SumsFilesRecursively()
        {
            var root = Path.Combine(Path.GetTempPath(), "tad-disk-" + Guid.NewGuid().ToString("N"));
            var sub = Path.Combine(root, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(root, "a.bin"), new string('x', 100));
            File.WriteAllText(Path.Combine(sub, "b.bin"), new string('y', 250));
            try
            {
                var (bytes, count) = DiskSpaceService.CalculateFolderSize(root, default);
                bytes.Should().Be(350);
                count.Should().Be(2);
            }
            finally
            {
                try { Directory.Delete(root, recursive: true); } catch { }
            }
        }

        [Fact]
        public void CalculateFolderSize_MissingFolder_ReturnsZero()
        {
            var path = Path.Combine(Path.GetTempPath(), "tad-missing-" + Guid.NewGuid().ToString("N"));
            var (bytes, count) = DiskSpaceService.CalculateFolderSize(path, default);
            bytes.Should().Be(0);
            count.Should().Be(0);
        }
    }
}

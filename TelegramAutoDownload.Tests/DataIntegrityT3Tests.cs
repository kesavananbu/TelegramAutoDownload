using BasePlugins;
using DownloadPlugin;
using FluentAssertions;
using System;
using System.IO;
using TelegramAutoDownload.Models;
using TelegramClient;
using TL;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    [Collection("DownloadLimitsGate")]
    public class DataIntegrityT3Tests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"t3_{Guid.NewGuid():N}");

        public DataIntegrityT3Tests()
        {
            Directory.CreateDirectory(_dir);
            DownloadLimits.MaxDownloadFileSizeBytes = 0;
            DownloadLimits.FreeSpaceSafetyMarginBytes = 0;
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
            DownloadLimits.MaxDownloadFileSizeBytes = 0;
            DownloadLimits.FreeSpaceSafetyMarginBytes = DownloadLimits.DefaultFreeSpaceSafetyMarginBytes;
        }

        [Fact]
        public void TryPromotePartToFinal_TruncatedPart_DoesNotPromote()
        {
            var final = Path.Combine(_dir, "file.bin");
            var part = final + ".part";
            File.WriteAllBytes(part, new byte[100]);

            var ok = DownloadPartFiles.TryPromotePartToFinal(part, final, expectedSizeBytes: 200, out var err);

            ok.Should().BeFalse();
            err.Should().Contain("does not match");
            File.Exists(part).Should().BeTrue();
            File.Exists(final).Should().BeFalse();
        }

        [Fact]
        public void TryPromotePartToFinal_ExactSize_Promotes()
        {
            var final = Path.Combine(_dir, "file.bin");
            var part = final + ".part";
            File.WriteAllBytes(part, new byte[512]);

            DownloadPartFiles.TryPromotePartToFinal(part, final, 512, out _).Should().BeTrue();
            File.Exists(part).Should().BeFalse();
            File.Exists(final).Should().BeTrue();
            new FileInfo(final).Length.Should().Be(512);
        }

        [Fact]
        public void PrepareStreamForChunkWrite_TruncatesAppendedGarbage()
        {
            var path = Path.Combine(_dir, "chunk.bin");
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
            {
                fs.Write(new byte[100], 0, 100);
                fs.Seek(0, SeekOrigin.End);
                fs.Write(new byte[50], 0, 50); // simulate corrupt append after failed retry
            }

            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
            {
                DownladerPlugin<object>.PrepareStreamForChunkWrite(fs, chunkOffset: 100);
                fs.Length.Should().Be(100);
                fs.Position.Should().Be(100);
            }
        }

        [Fact]
        public void RegexFilter_IsMatch_IsCaseInsensitive()
        {
            RegexFilterHelper.IsMatch("(?i)archive", "MyArchive.zip").Should().BeTrue();
            RegexFilterHelper.IsMatch("ARCHIVE", "myarchive.zip").Should().BeTrue();
        }

        [Fact]
        public void RegexFilter_InvalidPattern_DoesNotThrow()
        {
            var act = () => RegexFilterHelper.IsMatch("[invalid", "file.txt");
            act.Should().NotThrow();
            RegexFilterHelper.IsMatch("[invalid", "file.txt").Should().BeFalse();
        }

        [Fact]
        public void RegexFilter_TryValidatePattern_RejectsInvalid()
        {
            RegexFilterHelper.TryValidatePattern("(unclosed", out var err).Should().BeFalse();
            err.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void RegexFilter_NullFilename_DoesNotMatch()
        {
            RegexFilterHelper.FindFirstMatchingIgnorePattern([".*"], null).Should().BeNull();
            RegexFilterHelper.IsMatch("test", null).Should().BeFalse();
        }

        [Fact]
        public void Policy_MaxFileSize_BlocksOversizedDocument()
        {
            DownloadLimits.MaxDownloadFileSizeBytes = 100;
            var stub = new BaseMessageStub();
            var chat = new TelegramClient.Models.ChatDto { Name = "c", DownloadFromSize = 0, IgnoreFileByRegex = [] };
            var message = CreateMessageWithDocument(500, "big.bin");

            var result = stub.CheckDownloadPolicy(chat, message);
            result.IsSuccess.Should().BeFalse();
            result.ErrorMessage.Should().Contain("maximum");
        }

        [Fact]
        public void ConfigFile_Save_IsAtomic_NoTempLeftBehind()
        {
            var path = Path.Combine(_dir, "cfg.txt");
            var cfg = new ConfigFile(path);
            var p = new ConfigParams { PathSaveFile = _dir, DownloadThreads = 7 };
            cfg.Save(p);

            File.Exists(path + ".tmp").Should().BeFalse();
            cfg.Read().DownloadThreads.Should().Be(7);
        }

        private static Message CreateMessageWithDocument(long fileSizeBytes, string filename)
        {
            var document = new Document
            {
                size = fileSizeBytes,
                mime_type = "application/octet-stream",
                attributes = new DocumentAttribute[]
                {
                    new DocumentAttributeFilename { file_name = filename }
                }
            };
            return new Message { media = new MessageMediaDocument { document = document } };
        }
    }
}

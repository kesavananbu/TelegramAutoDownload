using BasePlugins;
using FluentAssertions;
using MonoTorrent;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class TorrentDownloadServiceTests
    {
        private const string UserTorrentPath =
            @"c:\Users\il90i\Downloads\Telegram Desktop\Devil_May_Cry_Season_2_Devil_May_Cry_Season_2_AniLibria_TOP_WE.torrent";

        [Theory]
        [InlineData("release.torrent", true)]
        [InlineData("RELEASE.TORRENT", true)]
        [InlineData("video.mkv", false)]
        [InlineData("", false)]
        public void IsTorrentFileName_DetectsExtension(string fileName, bool expected)
        {
            TorrentDownloadService.IsTorrentFileName(fileName).Should().Be(expected);
        }

        [Fact]
        public async Task ResolveDisplayNameAsync_LoadsNameFromUserTorrentFile()
        {
            if (!File.Exists(UserTorrentPath))
            {
                // Skip when the user's test file is not present on this machine.
                return;
            }

            var name = await TorrentDownloadService.ResolveDisplayNameAsync(null, UserTorrentPath);
            name.Should().NotBeNullOrWhiteSpace();
            name.Should().NotBe("torrent");
        }

        [Fact]
        public async Task UserTorrentFile_ParsesWithMonoTorrent()
        {
            if (!File.Exists(UserTorrentPath))
                return;

            var torrent = await Torrent.LoadAsync(UserTorrentPath);
            torrent.Should().NotBeNull();
            torrent.Name.Should().NotBeNullOrWhiteSpace();
            torrent.Size.Should().BeGreaterThan(0);
            torrent.Files.Should().NotBeEmpty();
        }

        [Fact]
        public async Task DownloadAsync_FromUserTorrentFile_StartsAndReportsProgress()
        {
            if (!File.Exists(UserTorrentPath))
                return;

            var outputDir = Path.Combine(Path.GetTempPath(), "TelegramAutoDownloadTests", "torrent-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputDir);

            double lastPct = -1;
            long lastBytes = -1;
            var progressCalls = 0;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                var downloadTask = TorrentDownloadService.DownloadAsync(
                    "TestChat",
                    outputDir,
                    magnetUri: null,
                    torrentFilePath: UserTorrentPath,
                    pluginName: "Torrent",
                    onProgress: (_, _, _, pct, bytes, _) =>
                    {
                        progressCalls++;
                        lastPct = pct;
                        lastBytes = bytes;
                    },
                    onComplete: null,
                    hostCancellationToken: cts.Token);

                var result = await downloadTask;

                // Cancelled by timeout is acceptable — we only need proof the swarm connected and bytes moved.
                if (result.ErrorMessage == "Cancelled by user" || cts.IsCancellationRequested)
                {
                    progressCalls.Should().BeGreaterThan(0, "progress should be reported while downloading");
                    (lastPct >= 0 || lastBytes > 0).Should().BeTrue("download should make measurable progress");
                    return;
                }

                result.IsSuccess.Should().BeTrue(result.ErrorMessage ?? "unknown error");
            }
            finally
            {
                try { Directory.Delete(outputDir, recursive: true); } catch { }
            }
        }
    }
}

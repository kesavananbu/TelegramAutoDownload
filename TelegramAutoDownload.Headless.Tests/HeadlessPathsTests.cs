using FluentAssertions;
using TelegramAutoDownload.Headless;

namespace TelegramAutoDownload.Headless.Tests;

public sealed class HeadlessPathsTests
{
    [Fact]
    public void SessionFile_matches_WTelegram_application_data_formula()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TelegramAutoDownload",
            "session.dat");

        HeadlessPaths.SessionFile.Should().Be(expected);
    }

    [Fact]
    public void LegacyDedupIndexFile_matches_FileDownloadIndex_path_formula()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TelegramAutoDownload",
            "downloaded_ids.json");

        HeadlessPaths.LegacyDedupIndexFile.Should().Be(expected);
    }
}

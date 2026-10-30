using FluentAssertions;
using TelegramAutoDownload.Services;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class DownloadLaunchHelperTests
    {
        [Theory]
        [InlineData("file.exe", true)]
        [InlineData("file.bat", true)]
        [InlineData("file.ps1", true)]
        [InlineData("file.lnk", true)]
        [InlineData("file.mp4", false)]
        [InlineData("file.pdf", false)]
        public void IsShellExecuteRiskyExtension_DetectsExecutables(string fileName, bool risky)
        {
            DownloadLaunchHelper.IsShellExecuteRiskyExtension(fileName).Should().Be(risky);
        }
    }
}

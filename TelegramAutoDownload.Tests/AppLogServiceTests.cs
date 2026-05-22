using FluentAssertions;
using TelegramAutoDownload.Services;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class AppLogServiceTests
    {
        [Fact]
        public void IsActiveLogFile_TodayLog_ReturnsTrue()
        {
            var path = Path.Combine(AppPaths.LogsDir, $"app-{DateTime.Now:yyyyMMdd}.log");
            AppLogService.IsActiveLogFile(path).Should().BeTrue();
        }

        [Fact]
        public void IsActiveLogFile_TodayLogWithSequenceSuffix_ReturnsTrue()
        {
            var path = Path.Combine(AppPaths.LogsDir, $"app-{DateTime.Now:yyyyMMdd}_001.log");
            AppLogService.IsActiveLogFile(path).Should().BeTrue();
        }

        [Fact]
        public void IsActiveLogFile_OldLog_ReturnsFalse()
        {
            var path = Path.Combine(AppPaths.LogsDir, "app-20260101.log");
            AppLogService.IsActiveLogFile(path).Should().BeFalse();
        }
    }
}

using FluentAssertions;
using System.IO;
using TelegramClient;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class SessionLockHelperTests
    {
        [Fact]
        public void IsSessionLockedException_DetectsSessionDatMessage()
        {
            var ex = new IOException(
                "The process cannot access the file 'C:\\AppData\\session.dat' because it is being used by another process.");
            SessionLockHelper.IsSessionLockedException(ex).Should().BeTrue();
        }

        [Fact]
        public void IsSessionLockedException_IgnoresOtherIOErrors()
        {
            SessionLockHelper.IsSessionLockedException(new IOException("Access denied")).Should().BeFalse();
        }
    }
}

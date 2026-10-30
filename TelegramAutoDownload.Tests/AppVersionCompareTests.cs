using FluentAssertions;
using TelegramAutoDownload.Services;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class AppVersionCompareTests
    {
        [Fact]
        public void CompareNumericSegments_Treats10GreaterThan9()
        {
            AppVersionCompare.CompareNumericSegments("2.10.0", "2.9.3").Should().BeGreaterThan(0);
        }
    }
}

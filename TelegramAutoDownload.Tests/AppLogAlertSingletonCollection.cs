using Xunit;

namespace TelegramAutoDownload.Tests
{
    /// <summary>
    /// Serializes tests that mutate <see cref="TelegramAutoDownload.Services.AppLogAlertService.Instance"/>.
    /// </summary>
    [CollectionDefinition("AppLogAlertSingleton", DisableParallelization = true)]
    public class AppLogAlertSingletonCollection
    {
    }
}

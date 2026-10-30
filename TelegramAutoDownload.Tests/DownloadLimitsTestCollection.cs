using Xunit;

namespace TelegramAutoDownload.Tests
{
    [CollectionDefinition("DownloadLimitsGate", DisableParallelization = true)]
    public sealed class DownloadLimitsTestCollection
    {
    }

    /// <summary>
    /// CancelAll() cancels every registered token in the process and Count is global,
    /// so these tests must not overlap any test that runs a download.
    /// </summary>
    [CollectionDefinition("CancellationRegistryGlobal", DisableParallelization = true)]
    public sealed class CancellationRegistryGlobalCollection
    {
    }
}

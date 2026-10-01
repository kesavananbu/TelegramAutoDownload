using System.Runtime.CompilerServices;

namespace TelegramAutoDownload.Headless.Tests;

/// <summary>
/// Sets DATA_DIR before the Headless assembly initializes <see cref="HeadlessPaths"/>.
/// </summary>
internal static class HeadlessTestRunEnvironment
{
    internal static string DataRoot { get; } =
        Path.Combine(Path.GetTempPath(), "TadHeadlessTests", Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Bootstrap()
    {
        Directory.CreateDirectory(DataRoot);
        Environment.SetEnvironmentVariable("DATA_DIR", DataRoot);
        Environment.SetEnvironmentVariable("DOWNLOADS_DIR", Path.Combine(DataRoot, "downloads"));
    }
}

using FluentAssertions;
using TelegramAutoDownload.Headless.Security;
using TelegramClient;

namespace TelegramAutoDownload.Headless.Tests;

public sealed class PathPolicyTests
{
    [Fact]
    public void FolderTemplate_RejectsDotDotSegment()
    {
        PathPolicy.IsValidFolderTemplate("../../x", out var error).Should().BeFalse();
        error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FolderTemplate_RejectsRootedPath()
    {
        PathPolicy.IsValidFolderTemplate("/etc/cron.d", out _).Should().BeFalse();
        PathPolicy.IsValidFolderTemplate(@"C:\Windows\Temp", out _).Should().BeFalse();
    }

    [Fact]
    public void Resolve_AbsoluteTemplate_IsRejectedNotPassedThrough()
    {
        var previous = FolderTemplateHelper.AllowAbsolutePaths;
        try
        {
            FolderTemplateHelper.AllowAbsolutePaths = false;
            var absolute = Path.GetFullPath(Path.GetTempPath());
            var act = () => FolderTemplateHelper.Resolve(absolute, "Videos", "Chat");
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            FolderTemplateHelper.AllowAbsolutePaths = previous;
        }
    }

    [Fact]
    public void ResolveDownloadDirectory_NeverEscapesRoot()
    {
        var previous = FolderTemplateHelper.AllowAbsolutePaths;
        try
        {
            FolderTemplateHelper.AllowAbsolutePaths = false;
            var act = () => FolderTemplateHelper.ResolveDownloadDirectory(
                HeadlessPaths.DownloadsDir,
                TelegramClient.Models.FolderLayoutMode.TypeFirst,
                "../outside",
                "Videos",
                "Chat");
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            FolderTemplateHelper.AllowAbsolutePaths = previous;
        }
    }
}

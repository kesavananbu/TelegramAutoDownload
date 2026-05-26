using TelegramAutoDownload.Headless.Data;
using TelegramClient;
using TelegramClient.Models;

namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>
/// Pre-flight check before the orchestrator hits Telegram: skip work when the file
/// is already on disk under this chat's folder or indexed as downloaded for this content id.
/// </summary>
public static class ExistingDownloadValidator
{
    public sealed record Result(bool MarkDone, string Path, string Message);

    public static async Task<Result?> CheckAsync(
        MediaRecord row,
        ChatDto chat,
        string? downloadRoot,
        MediaRepository repo,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(downloadRoot) || !Directory.Exists(downloadRoot))
            return null;

        ct.ThrowIfCancellationRequested();

        if (!string.IsNullOrEmpty(row.downloaded_path) && File.Exists(row.downloaded_path))
            return new Result(true, row.downloaded_path, "File already on disk (recorded path)");

        var kind = MediaKindExtensions.ParseKind(row.kind);
        var chatDir = ResolveChatDirectory(downloadRoot, chat, kind);
        var onDisk = FindOnDisk(chatDir, row.file_name, row.size_bytes);
        if (onDisk == null)
            return null;

        if (row.document_id.HasValue && row.document_id.Value > 0 &&
            await repo.IsKnownDocumentAsync(row.document_id.Value).ConfigureAwait(false))
        {
            return new Result(true, onDisk, "Already downloaded (document id + file on disk)");
        }

        return new Result(false, onDisk, $"{row.file_name ?? "file"} already exists in this chat folder");
    }

    /// <summary>
    /// Resolves the directory where this chat's downloads for <paramref name="kind"/> are stored.
    /// Mirrors <see cref="TelegramClient.Factory.Base.BaseMessage.PathLocationFolder"/> layout logic.
    /// </summary>
    public static string ResolveChatDirectory(string downloadRoot, ChatDto chat, MediaKind kind)
    {
        var folderName = SanitizeChatFolderName(chat.Name);
        var typeFolder = KindToTypeFolder(kind);

        var resolvedTemplate = FolderTemplateHelper.Resolve(
            chat.FolderTemplate, typeFolder, folderName);
        if (resolvedTemplate != null)
        {
            return Path.IsPathRooted(resolvedTemplate)
                ? resolvedTemplate
                : Path.Combine(downloadRoot, resolvedTemplate);
        }

        return Path.Combine(downloadRoot, typeFolder, folderName);
    }

    internal static string KindToTypeFolder(MediaKind kind) => kind switch
    {
        MediaKind.Video   => "Videos",
        MediaKind.Photo   => "Photos",
        MediaKind.Audio   => "Music",
        MediaKind.File    => "Files",
        MediaKind.Torrent => "Files",
        MediaKind.Url     => "Message",
        _                 => "Files",
    };

    internal static string SanitizeChatFolderName(string chatName)
    {
        var folderName = chatName.TrimEnd();
        foreach (var c in Path.GetInvalidFileNameChars())
            folderName = folderName.Replace(c, ' ');
        return folderName.Replace('~', ' ');
    }

    /// <summary>
    /// Searches only under <paramref name="chatDirectory"/> (and its subfolders).
    /// When <paramref name="sizeBytes"/> &gt; 0, size must match (avoids false positives).
    /// </summary>
    public static string? FindOnDisk(string chatDirectory, string? fileName, long sizeBytes)
    {
        if (string.IsNullOrWhiteSpace(fileName) || string.IsNullOrWhiteSpace(chatDirectory))
            return null;

        if (!Directory.Exists(chatDirectory))
            return null;

        try
        {
            var direct = Path.Combine(chatDirectory, fileName);
            if (MatchesFile(direct, sizeBytes))
                return direct;

            foreach (var file in Directory.EnumerateFiles(chatDirectory, fileName, SearchOption.AllDirectories))
            {
                if (MatchesFile(file, sizeBytes))
                    return file;
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "ExistingDownloadValidator disk scan failed under {Dir}", chatDirectory);
        }

        return null;
    }

    private static bool MatchesFile(string path, long sizeBytes)
    {
        if (path.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) return false;
        if (!File.Exists(path)) return false;
        if (sizeBytes <= 0) return true;
        return new FileInfo(path).Length == sizeBytes;
    }
}

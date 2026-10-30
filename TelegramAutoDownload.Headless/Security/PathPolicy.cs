namespace TelegramAutoDownload.Headless.Security;

public static class PathPolicy
{
    private const int MaxTemplateSegments = 8;
    private const int MaxTemplateLength = 200;

    public static bool TryResolveDownloadFolder(string candidate, out string resolved, out string? error)
    {
        resolved = "";
        error = null;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            error = "Folder path is required.";
            return false;
        }

        var downloadsRoot = Path.GetFullPath(HeadlessPaths.DownloadsDir);
        var dataRoot = Path.GetFullPath(HeadlessPaths.DataDir);

        string full;
        try
        {
            full = Path.IsPathRooted(candidate.Trim())
                ? Path.GetFullPath(candidate.Trim())
                : Path.GetFullPath(Path.Combine(downloadsRoot, candidate.Trim()));
        }
        catch
        {
            error = "Invalid folder path.";
            return false;
        }

        if (!IsUnderRoot(full, downloadsRoot))
        {
            error = "Download folder must stay under the configured downloads root.";
            return false;
        }

        if (IsUnderRoot(full, dataRoot) && !string.Equals(full, downloadsRoot, StringComparison.OrdinalIgnoreCase))
        {
            error = "Download folder must not overlap the application data directory.";
            return false;
        }

        if (ContainsTraversalSegment(candidate))
        {
            error = "Path traversal is not allowed.";
            return false;
        }

        resolved = full;
        return true;
    }

    public static bool IsValidFolderTemplate(string? template, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(template))
            return true;

        template = template.Trim();
        if (template.Length > MaxTemplateLength)
        {
            error = "Folder template is too long.";
            return false;
        }

        if (Path.IsPathRooted(template))
        {
            error = "Folder template must be relative.";
            return false;
        }

        if (template.Contains(':', StringComparison.Ordinal) || template.Contains('\0'))
        {
            error = "Folder template contains invalid characters.";
            return false;
        }

        var segments = template.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > MaxTemplateSegments)
        {
            error = "Folder template has too many path segments.";
            return false;
        }

        foreach (var seg in segments)
        {
            if (seg is "." or "..")
            {
                error = "Folder template must not contain '.' or '..' segments.";
                return false;
            }
        }

        return true;
    }

    public static bool TryEnsureUnderDownloadRoot(string downloadRoot, string resolvedPath, out string? error)
    {
        error = null;
        var root = Path.GetFullPath(downloadRoot);
        var full = Path.GetFullPath(resolvedPath);
        var dataRoot = Path.GetFullPath(HeadlessPaths.DataDir);

        if (!IsUnderRoot(full, root))
        {
            error = "Resolved path escapes download root.";
            return false;
        }

        if (IsUnderRoot(full, dataRoot) && !IsUnderRoot(root, dataRoot))
        {
            error = "Resolved path must not enter the data directory.";
            return false;
        }

        return true;
    }

    private static bool IsUnderRoot(string fullPath, string root)
    {
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return fullPath.StartsWith(normalizedRoot, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);
    }

    private static bool ContainsTraversalSegment(string candidate) =>
        candidate.Contains("..", StringComparison.Ordinal);
}

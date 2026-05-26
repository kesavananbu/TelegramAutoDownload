namespace TelegramAutoDownload.Headless.Data;

/// <summary>
/// Lifecycle state for a tracked media item.
///
///   discovered ─filter matches▶ pending ─selected▶ queued ─worker▶ in_progress
///                                 │                                     │
///                                 └─filter doesn't match─▶ skipped      │
///                                                                       ▼
///                                                                  done | failed
///                                                                          │
///                                                                          └─ retry → queued
///
/// Stored as text in SQLite (matching the values returned by <see cref="ToDbValue"/>).
/// </summary>
public enum MediaStatus
{
    Pending,
    Queued,
    InProgress,
    Done,
    Failed,
    Skipped,
}

public static class MediaStatusExtensions
{
    public static string ToDbValue(this MediaStatus s) => s switch
    {
        MediaStatus.Pending    => "pending",
        MediaStatus.Queued     => "queued",
        MediaStatus.InProgress => "in_progress",
        MediaStatus.Done       => "done",
        MediaStatus.Failed     => "failed",
        MediaStatus.Skipped    => "skipped",
        _ => throw new ArgumentOutOfRangeException(nameof(s)),
    };

    public static MediaStatus ParseStatus(string raw) => raw switch
    {
        "pending"     => MediaStatus.Pending,
        "queued"      => MediaStatus.Queued,
        "in_progress" => MediaStatus.InProgress,
        "done"        => MediaStatus.Done,
        "failed"      => MediaStatus.Failed,
        "skipped"     => MediaStatus.Skipped,
        _ => throw new ArgumentException($"Unknown MediaStatus '{raw}'", nameof(raw)),
    };
}

/// <summary>
/// Coarse media classification used for per-type metrics. Matches the categories
/// the WPF UI already shows: Videos / Photos / Music / Files plus URL/Torrent
/// items handled by plugins.
/// </summary>
public enum MediaKind
{
    Unknown,
    Video,
    Photo,
    Audio,
    File,
    Url,
    Torrent,
}

public static class MediaKindExtensions
{
    public static string ToDbValue(this MediaKind k) => k.ToString();
    public static MediaKind ParseKind(string raw) =>
        Enum.TryParse<MediaKind>(raw, ignoreCase: true, out var k) ? k : MediaKind.Unknown;
}

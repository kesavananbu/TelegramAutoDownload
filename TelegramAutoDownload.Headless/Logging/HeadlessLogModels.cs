namespace TelegramAutoDownload.Headless.Logging;

/// <summary>Shared Serilog file line format for headless (matches Program.cs sink).</summary>
public static class HeadlessLogFormat
{
    public const string FileOutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";
}

public sealed record LogFileEntry(string Name, long SizeBytes, DateTimeOffset ModifiedUtc);

public sealed record LogLine(long LineNumber, string Text, string? Level);

public sealed record LogTailResponse(
    string File,
    long FileSizeBytes,
    long StartLineNumber,
    IReadOnlyList<LogLine> Lines,
    bool Truncated,
    string? Message);

public sealed record LogSearchResponse(
    string File,
    string Query,
    int Skip,
    int Limit,
    int Returned,
    bool HasMore,
    IReadOnlyList<LogLine> Lines);

public sealed record LogStreamChunk(IReadOnlyList<LogLine> Lines, long NewOffset);

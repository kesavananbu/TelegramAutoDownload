using System.Text;
using System.Text.RegularExpressions;

namespace TelegramAutoDownload.Headless.Logging;

/// <summary>
/// Reads rolling Serilog files under <see cref="HeadlessPaths.LogsDir"/>.
/// Optimised for large files: tail reads only the last chunk of bytes; search streams line-by-line.
/// </summary>
public sealed class HeadlessLogReader
{
    private const int MaxTailBytes = 2 * 1024 * 1024;
    private const int MaxSearchScanLines = 2_000_000;

    private static readonly Regex LevelRegex =
        new(@"\[(DBG|INF|WRN|ERR|FTL|VRB)\]", RegexOptions.Compiled);

    public IReadOnlyList<LogFileEntry> ListFiles()
    {
        if (!Directory.Exists(HeadlessPaths.LogsDir))
            return Array.Empty<LogFileEntry>();

        return Directory.EnumerateFiles(HeadlessPaths.LogsDir, "headless*.log")
            .Select(p =>
            {
                var fi = new FileInfo(p);
                return new LogFileEntry(fi.Name, fi.Length, fi.LastWriteTimeUtc);
            })
            .OrderByDescending(f => f.ModifiedUtc)
            .ToList();
    }

    public string ResolveSafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            var latest = ListFiles().FirstOrDefault()
                ?? throw new FileNotFoundException("No log files found.");
            return latest.Name;
        }

        var name = Path.GetFileName(fileName.Trim());
        if (!name.StartsWith("headless", StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid log file name.");

        var path = Path.Combine(HeadlessPaths.LogsDir, name);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Log file '{name}' not found.");

        return name;
    }

    public LogTailResponse Tail(string fileName, int maxLines, string? minLevel, string? search)
    {
        maxLines = Math.Clamp(maxLines, 1, 5000);
        var path = Path.Combine(HeadlessPaths.LogsDir, ResolveSafeFileName(fileName));
        var fi = new FileInfo(path);
        if (fi.Length == 0)
            return new LogTailResponse(fileName, 0, 0, Array.Empty<LogLine>(), false, null);

        var truncated = fi.Length > MaxTailBytes;
        var text = ReadTailText(path, MaxTailBytes);
        var lines = SplitLogicalLines(text);
        if (truncated && lines.Count > 0 && !lines[0].StartsWith('…'))
            lines[0] = "… (file truncated — showing end only) " + lines[0];

        var filtered = FilterLines(lines, minLevel, search);
        var totalBefore = filtered.Count;
        var slice = filtered.Count <= maxLines
            ? filtered
            : filtered.Skip(filtered.Count - maxLines).ToList();

        var startLine = Math.Max(1, totalBefore - slice.Count + 1);
        var numbered = slice.Select((t, i) => ToLogLine(startLine + i, t)).ToList();

        return new LogTailResponse(
            fileName,
            fi.Length,
            startLine,
            numbered,
            truncated,
            truncated ? $"Showing last {maxLines} matching lines from the end of the file." : null);
    }

    public LogSearchResponse Search(string fileName, string query, int skip, int limit)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Search query is required.");

        skip = Math.Max(0, skip);
        limit = Math.Clamp(limit, 1, 500);

        var path = Path.Combine(HeadlessPaths.LogsDir, ResolveSafeFileName(fileName));
        var q = query.Trim();
        var hits = new List<LogLine>();
        var seen = 0;
        var hasMore = false;
        long lineNo = 0;
        var scanned = 0;

        foreach (var line in StreamLines(path))
        {
            scanned++;
            if (scanned > MaxSearchScanLines)
            {
                hasMore = true;
                break;
            }

            lineNo++;
            if (line.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            if (seen++ < skip)
                continue;

            if (hits.Count >= limit)
            {
                hasMore = true;
                break;
            }

            hits.Add(ToLogLine(lineNo, line));
        }

        return new LogSearchResponse(fileName, q, skip, limit, hits.Count, hasMore, hits);
    }

    /// <summary>Reads new text appended after <paramref name="offset"/>; updates offset to EOF.</summary>
    public LogStreamChunk ReadSinceOffset(string fileName, ref long offset)
    {
        var path = Path.Combine(HeadlessPaths.LogsDir, ResolveSafeFileName(fileName));
        if (!File.Exists(path))
            return new LogStreamChunk(Array.Empty<LogLine>(), offset);

        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length < offset)
            offset = 0;

        if (fs.Length <= offset)
            return new LogStreamChunk(Array.Empty<LogLine>(), offset);

        fs.Seek(offset, SeekOrigin.Begin);
        var len = (int)(fs.Length - offset);
        var buf = new byte[len];
        _ = fs.Read(buf, 0, len);
        offset = fs.Length;

        var text = Encoding.UTF8.GetString(buf);
        var lines = SplitLogicalLines(text);
        var numbered = lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(t => new LogLine(0, t, ExtractLevel(t)))
            .ToList();

        return new LogStreamChunk(numbered, offset);
    }

    private static IEnumerable<string> StreamLines(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (sr.ReadLine() is { } line)
            yield return line;
    }

    private static string ReadTailText(string path, int maxBytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var len = fs.Length;
        if (len == 0) return string.Empty;

        var take = (int)Math.Min(len, maxBytes);
        fs.Seek(-take, SeekOrigin.End);
        var buf = new byte[take];
        _ = fs.Read(buf, 0, take);
        var text = Encoding.UTF8.GetString(buf);
        var nl = text.IndexOf('\n');
        if (take < len && nl >= 0 && nl < text.Length - 1)
            text = text[(nl + 1)..];
        return text;
    }

    private static List<string> SplitLogicalLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return new List<string>();
        return text.Replace("\r\n", "\n").Split('\n').ToList();
    }

    private static List<string> FilterLines(IReadOnlyList<string> lines, string? minLevel, string? search)
    {
        var min = ParseMinLevel(minLevel);
        var q = search?.Trim();

        IEnumerable<string> qry = lines;
        if (min != null)
            qry = qry.Where(l => LineLevel(l) >= min);
        if (!string.IsNullOrEmpty(q))
            qry = qry.Where(l => l.Contains(q, StringComparison.OrdinalIgnoreCase));

        return qry.ToList();
    }

    private static LogLine ToLogLine(long number, string text) =>
        new(number, text, ExtractLevel(text));

    private static string? ExtractLevel(string line)
    {
        var m = LevelRegex.Match(line);
        return m.Success ? m.Groups[1].Value : null;
    }

    private static int LineLevel(string line)
    {
        var lv = ExtractLevel(line);
        return lv switch
        {
            "VRB" or "DBG" => 0,
            "INF" => 1,
            "WRN" => 2,
            "ERR" => 3,
            "FTL" => 4,
            _ => 1,
        };
    }

    private static int? ParseMinLevel(string? raw) => raw?.Trim().ToUpperInvariant() switch
    {
        null or "" => null,
        "DBG" or "DEBUG" or "VRB" => 0,
        "INF" or "INFO" => 1,
        "WRN" or "WARN" or "WARNING" => 2,
        "ERR" or "ERROR" => 3,
        "FTL" or "FATAL" => 4,
        _ => 1,
    };
}

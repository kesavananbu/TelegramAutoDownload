namespace TelegramAutoDownload.Headless.Data;

/// <summary>
/// POCO that mirrors a row in the <c>Media</c> table.
/// Property names use the column names directly (Dapper handles the mapping).
/// </summary>
public sealed class MediaRecord
{
    public long    chat_id         { get; set; }
    public int     message_id      { get; set; }
    public long?   document_id     { get; set; }
    public string? url_hash        { get; set; }
    public string  kind            { get; set; } = MediaKind.Unknown.ToDbValue();
    public long    size_bytes      { get; set; }
    public string  date_utc        { get; set; } = "";
    public string? file_name       { get; set; }
    public string  status          { get; set; } = MediaStatus.Pending.ToDbValue();
    public int     attempts        { get; set; }
    public string? last_error      { get; set; }
    public string? downloaded_path { get; set; }
    public string  discovered_at   { get; set; } = "";
    public string? queued_at       { get; set; }
    public string? started_at      { get; set; }
    public string? completed_at    { get; set; }
}

/// <summary>
/// Aggregate result for <c>SELECT status, COUNT, SUM(size) FROM Media …</c>.
/// </summary>
public sealed class MediaStatusCount
{
    public string status { get; set; } = "";
    public long   count { get; set; }
    public long   total_bytes { get; set; }
}

/// <summary>Aggregate per-chat × per-status row.</summary>
public sealed class ChatStatusCount
{
    public long   chat_id { get; set; }
    public string status { get; set; } = "";
    public long   count { get; set; }
    public long   total_bytes { get; set; }
}

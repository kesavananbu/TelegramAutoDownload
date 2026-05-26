using Dapper;

namespace TelegramAutoDownload.Headless.Data;

/// <summary>
/// Persistent operations on the <c>Media</c>, <c>ChatScanState</c>, and
/// <c>LegacyDedup</c> tables. All queries are short and resolve to a single
/// statement so contention stays low; the repository is safe to share across
/// the entire process as a singleton.
/// </summary>
public sealed class MediaRepository
{
    private readonly Database _db;
    public MediaRepository(Database db) { _db = db; }

    // ── Lookups ────────────────────────────────────────────────────────────────
    public async Task<MediaRecord?> GetAsync(long chatId, int messageId)
    {
        await using var c = _db.Open();
        return await c.QuerySingleOrDefaultAsync<MediaRecord>(
            "SELECT * FROM Media WHERE chat_id = @chatId AND message_id = @messageId",
            new { chatId, messageId });
    }

    public async Task<bool> IsKnownDocumentAsync(long documentId)
    {
        await using var c = _db.Open();
        var hit = await c.ExecuteScalarAsync<long>(
            """
            SELECT (
              EXISTS (SELECT 1 FROM Media        WHERE document_id = @id AND status = 'done')
              OR
              EXISTS (SELECT 1 FROM LegacyDedup  WHERE document_id = @id)
            )
            """,
            new { id = documentId });
        return hit == 1;
    }

    // ── Writes ────────────────────────────────────────────────────────────────
    /// <summary>
    /// Inserts a freshly-discovered media row in the <c>pending</c> state.
    /// Idempotent on (chat_id, message_id) — duplicate inserts are silently ignored
    /// so the scanner can re-process pages without bookkeeping.
    /// </summary>
    public async Task<bool> InsertPendingAsync(MediaRecord r)
    {
        r.status ??= MediaStatus.Pending.ToDbValue();
        await using var c = _db.Open();
        var rows = await c.ExecuteAsync(
            """
            INSERT OR IGNORE INTO Media
                (chat_id, message_id, document_id, url_hash, kind, size_bytes, date_utc,
                 file_name, status, attempts, discovered_at)
            VALUES
                (@chat_id, @message_id, @document_id, @url_hash, @kind, @size_bytes, @date_utc,
                 @file_name, @status, 0, datetime('now'))
            """, r);
        return rows == 1;
    }

    public async Task SetStatusAsync(long chatId, int messageId, MediaStatus status, string? error = null)
    {
        var stamp = status switch
        {
            MediaStatus.Queued     => "queued_at",
            MediaStatus.InProgress => "started_at",
            MediaStatus.Done       => "completed_at",
            MediaStatus.Failed     => "completed_at",
            _ => null,
        };

        await using var c = _db.Open();
        var sql = $"""
            UPDATE Media
               SET status     = @status,
                   last_error = @error
                   {(stamp != null ? $", {stamp} = datetime('now')" : "")}
                   {(status == MediaStatus.Failed ? ", attempts = attempts + 1" : "")}
             WHERE chat_id = @chatId AND message_id = @messageId
            """;
        await c.ExecuteAsync(sql, new { status = status.ToDbValue(), error, chatId, messageId });
    }

    /// <summary>Move all <c>failed</c> rows back to <c>queued</c>. Optionally scoped to one chat.</summary>
    public async Task<int> RequeueAllFailedAsync(long? chatId = null)
    {
        await using var c = _db.Open();
        if (chatId.HasValue)
        {
            return await c.ExecuteAsync(
                """
                UPDATE Media
                   SET status     = 'queued',
                       queued_at  = datetime('now'),
                       last_error = NULL
                 WHERE status = 'failed' AND chat_id = @chatId
                """, new { chatId = chatId.Value });
        }

        return await c.ExecuteAsync(
            """
            UPDATE Media
               SET status     = 'queued',
                   queued_at  = datetime('now'),
                   last_error = NULL
             WHERE status = 'failed'
            """);
    }

    /// <summary>Move all <c>in_progress</c> rows back to <c>queued</c> (used when pausing downloads).</summary>
    public async Task<int> RequeueInProgressAsync(long? chatId = null)
    {
        await using var c = _db.Open();
        if (chatId.HasValue)
        {
            return await c.ExecuteAsync(
                """
                UPDATE Media
                   SET status     = 'queued',
                       queued_at  = datetime('now')
                 WHERE status = 'in_progress' AND chat_id = @chatId
                """, new { chatId = chatId.Value });
        }

        return await c.ExecuteAsync(
            """
            UPDATE Media
               SET status     = 'queued',
                   queued_at  = datetime('now')
             WHERE status = 'in_progress'
            """);
    }

    /// <summary>Permanently remove failed rows from the queue database.</summary>
    public async Task<int> DeleteAllFailedAsync(long? chatId = null)
    {
        await using var c = _db.Open();
        if (chatId.HasValue)
        {
            return await c.ExecuteAsync(
                "DELETE FROM Media WHERE status = 'failed' AND chat_id = @chatId",
                new { chatId = chatId.Value });
        }

        return await c.ExecuteAsync("DELETE FROM Media WHERE status = 'failed'");
    }

    /// <summary>Remove all tracked media rows for a chat and reset its bootstrap watermark.</summary>
    public async Task<int> ClearChatQueueAsync(long chatId)
    {
        await using var c = _db.Open();
        var deleted = await c.ExecuteAsync(
            "DELETE FROM Media WHERE chat_id = @chatId",
            new { chatId });
        await c.ExecuteAsync("DELETE FROM ChatScanState WHERE chat_id = @chatId", new { chatId });
        return deleted;
    }

    public async Task SetDownloadedPathAsync(long chatId, int messageId, string path)
    {
        await using var c = _db.Open();
        await c.ExecuteAsync(
            "UPDATE Media SET downloaded_path = @path WHERE chat_id = @chatId AND message_id = @messageId",
            new { path, chatId, messageId });
    }

    /// <summary>
    /// Atomically claims up to <paramref name="limit"/> rows in <c>queued</c> state by
    /// flipping them to <c>in_progress</c> and returning the claimed rows.
    /// Two callers cannot pick the same row — SQLite's <c>RETURNING</c> applies after the UPDATE.
    /// </summary>
    public async Task<IReadOnlyList<MediaRecord>> PickQueuedAsync(int limit, IReadOnlyList<long>? chatIds = null)
    {
        if (limit <= 0) return Array.Empty<MediaRecord>();
        if (chatIds is { Count: 0 }) return Array.Empty<MediaRecord>();

        await using var c = _db.Open();
        var sql = chatIds == null
            ? """
              UPDATE Media
                 SET status     = 'in_progress',
                     started_at = datetime('now')
               WHERE rowid IN (
                  SELECT rowid FROM Media
                   WHERE status = 'queued'
                   ORDER BY queued_at ASC, message_id ASC
                   LIMIT @limit
               )
              RETURNING *
              """
            : """
              UPDATE Media
                 SET status     = 'in_progress',
                     started_at = datetime('now')
               WHERE rowid IN (
                  SELECT rowid FROM Media
                   WHERE status = 'queued'
                     AND chat_id IN @chatIds
                   ORDER BY queued_at ASC, message_id ASC
                   LIMIT @limit
               )
              RETURNING *
              """;
        var rows = await c.QueryAsync<MediaRecord>(sql, new { limit, chatIds });
        return rows.AsList();
    }

    /// <summary>Move all <c>pending</c> rows for a chat to <c>queued</c>. Used after bootstrap.</summary>
    public async Task<int> PromotePendingToQueuedAsync(long chatId)
    {
        await using var c = _db.Open();
        return await c.ExecuteAsync(
            "UPDATE Media SET status = 'queued', queued_at = datetime('now') " +
            "WHERE chat_id = @chatId AND status = 'pending'",
            new { chatId });
    }

    // ── Aggregates / stats ────────────────────────────────────────────────────
    public async Task<IReadOnlyList<MediaStatusCount>> GetGlobalStatusCountsAsync()
    {
        await using var c = _db.Open();
        var rows = await c.QueryAsync<MediaStatusCount>(
            """
            SELECT status, COUNT(*) AS count, COALESCE(SUM(size_bytes), 0) AS total_bytes
              FROM Media
             GROUP BY status
            """);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<ChatStatusCount>> GetPerChatStatusCountsAsync()
    {
        await using var c = _db.Open();
        var rows = await c.QueryAsync<ChatStatusCount>(
            """
            SELECT chat_id, status, COUNT(*) AS count, COALESCE(SUM(size_bytes), 0) AS total_bytes
              FROM Media
             GROUP BY chat_id, status
            """);
        return rows.AsList();
    }

    public async Task<IReadOnlyList<MediaRecord>> ListByStatusAsync(string status, int limit = 50)
    {
        await using var c = _db.Open();
        var rows = await c.QueryAsync<MediaRecord>(
            """
            SELECT * FROM Media
             WHERE status = @status
             ORDER BY COALESCE(completed_at, queued_at, discovered_at) DESC
             LIMIT @limit
            """, new { status, limit });
        return rows.AsList();
    }

    public async Task<long> CountAsync()
    {
        await using var c = _db.Open();
        return await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM Media");
    }

    // ── ChatScanState ─────────────────────────────────────────────────────────
    public async Task UpsertScanStateAsync(long chatId, int lastScannedMsgId, bool bootstrapComplete)
    {
        await using var c = _db.Open();
        await c.ExecuteAsync(
            """
            INSERT INTO ChatScanState (chat_id, last_scanned_msg_id, last_scanned_date, bootstrap_complete, last_forward_at)
            VALUES (@chatId, @lastScannedMsgId, datetime('now'), @bootstrapComplete, datetime('now'))
            ON CONFLICT(chat_id) DO UPDATE SET
                last_scanned_msg_id = MAX(last_scanned_msg_id, excluded.last_scanned_msg_id),
                last_scanned_date   = excluded.last_scanned_date,
                bootstrap_complete  = MAX(bootstrap_complete, excluded.bootstrap_complete),
                last_forward_at     = excluded.last_forward_at
            """,
            new { chatId, lastScannedMsgId, bootstrapComplete = bootstrapComplete ? 1 : 0 });
    }

    public async Task<int> GetLastScannedMsgIdAsync(long chatId)
    {
        await using var c = _db.Open();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COALESCE(last_scanned_msg_id, 0) FROM ChatScanState WHERE chat_id = @chatId",
            new { chatId });
    }

    // ── LegacyDedup ───────────────────────────────────────────────────────────
    public async Task<long> CountLegacyDedupAsync()
    {
        await using var c = _db.Open();
        return await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM LegacyDedup");
    }

    public async Task BulkInsertLegacyDedupAsync(IEnumerable<long> documentIds)
    {
        await using var c = _db.Open();
        using var tx = c.BeginTransaction();
        await c.ExecuteAsync(
            "INSERT OR IGNORE INTO LegacyDedup (document_id) VALUES (@documentId)",
            documentIds.Select(id => new { documentId = id }),
            transaction: tx);
        tx.Commit();
    }
}

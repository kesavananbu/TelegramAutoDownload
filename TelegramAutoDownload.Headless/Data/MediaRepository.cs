using Dapper;
using Microsoft.Data.Sqlite;

namespace TelegramAutoDownload.Headless.Data;

/// <summary>
/// Persistent operations on the <c>Media</c>, <c>ChatScanState</c>, and
/// <c>LegacyDedup</c> tables. Writes are serialized through <see cref="_writeLock"/>
/// so concurrent callers do not hit <c>SQLITE_LOCKED</c> under WAL.
/// </summary>
public sealed class MediaRepository
{
    private readonly Database _db;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public MediaRepository(Database db) { _db = db; }

    private async Task WithWriteLockAsync(Func<SqliteConnection, Task> work)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var c = _db.Open();
            await work(c).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task<T> WithWriteLockAsync<T>(Func<SqliteConnection, Task<T>> work)
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var c = _db.Open();
            return await work(c).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

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
        return await WithWriteLockAsync(async c =>
        {
            var rows = await c.ExecuteAsync(
                """
                INSERT OR IGNORE INTO Media
                    (chat_id, message_id, document_id, url_hash, kind, size_bytes, date_utc,
                     file_name, status, attempts, last_error, discovered_at)
                VALUES
                    (@chat_id, @message_id, @document_id, @url_hash, @kind, @size_bytes, @date_utc,
                     @file_name, @status, 0, @last_error, datetime('now'))
                """, r);
            return rows == 1;
        }).ConfigureAwait(false);
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

        var sql = $"""
            UPDATE Media
               SET status     = @status,
                   last_error = @error
                   {(stamp != null ? $", {stamp} = datetime('now')" : "")}
                   {(status == MediaStatus.Failed ? ", attempts = attempts + 1" : "")}
             WHERE chat_id = @chatId AND message_id = @messageId
            """;
        await WithWriteLockAsync(async c =>
        {
            await c.ExecuteAsync(sql, new { status = status.ToDbValue(), error, chatId, messageId });
        });
    }

    /// <summary>Move all <c>failed</c> rows back to <c>queued</c>. Optionally scoped to one chat.</summary>
    public async Task<int> RequeueAllFailedAsync(long? chatId = null) =>
        await WithWriteLockAsync(async c =>
        {
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
        }).ConfigureAwait(false);

    /// <summary>Move all <c>skipped</c> rows back to <c>queued</c>. Optionally scoped to one chat.</summary>
    public async Task<int> RequeueAllSkippedAsync(long? chatId = null) =>
        await WithWriteLockAsync(async c =>
        {
            if (chatId.HasValue)
            {
                return await c.ExecuteAsync(
                    """
                    UPDATE Media
                       SET status     = 'queued',
                           queued_at  = datetime('now'),
                           last_error = NULL
                     WHERE status = 'skipped' AND chat_id = @chatId
                    """, new { chatId = chatId.Value });
            }

            return await c.ExecuteAsync(
                """
                UPDATE Media
                   SET status     = 'queued',
                       queued_at  = datetime('now'),
                       last_error = NULL
                 WHERE status = 'skipped'
                """);
        }).ConfigureAwait(false);

    /// <summary>Move all <c>in_progress</c> rows back to <c>queued</c> (used when pausing downloads).</summary>
    public async Task<int> RequeueInProgressAsync(long? chatId = null) =>
        await WithWriteLockAsync(async c =>
        {
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
        }).ConfigureAwait(false);

    /// <summary>Permanently remove failed rows from the queue database.</summary>
    public async Task<int> DeleteAllFailedAsync(long? chatId = null) =>
        await WithWriteLockAsync(async c =>
        {
            if (chatId.HasValue)
            {
                return await c.ExecuteAsync(
                    "DELETE FROM Media WHERE status = 'failed' AND chat_id = @chatId",
                    new { chatId = chatId.Value });
            }

            return await c.ExecuteAsync("DELETE FROM Media WHERE status = 'failed'");
        }).ConfigureAwait(false);

    /// <summary>Remove all tracked media rows for a chat and reset its bootstrap watermark.</summary>
    public async Task<int> ClearChatQueueAsync(long chatId) =>
        await WithWriteLockAsync(async c =>
        {
            var deleted = await c.ExecuteAsync(
                "DELETE FROM Media WHERE chat_id = @chatId",
                new { chatId });
            await c.ExecuteAsync("DELETE FROM ChatScanState WHERE chat_id = @chatId", new { chatId });
            return deleted;
        }).ConfigureAwait(false);

    public Task SetDownloadedPathAsync(long chatId, int messageId, string path) =>
        WithWriteLockAsync(async c =>
        {
            await c.ExecuteAsync(
                "UPDATE Media SET downloaded_path = @path WHERE chat_id = @chatId AND message_id = @messageId",
                new { path, chatId, messageId });
        });

    /// <summary>
    /// Atomically claims up to <paramref name="limit"/> rows in <c>queued</c> state by
    /// flipping them to <c>in_progress</c> and returning the claimed rows.
    /// Two callers cannot pick the same row — SQLite's <c>RETURNING</c> applies after the UPDATE.
    /// </summary>
    public async Task<IReadOnlyList<MediaRecord>> PickQueuedAsync(int limit, IReadOnlyList<long>? chatIds = null)
    {
        if (limit <= 0) return Array.Empty<MediaRecord>();
        if (chatIds is { Count: 0 }) return Array.Empty<MediaRecord>();

        return await WithWriteLockAsync(async c =>
        {
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
            return (IReadOnlyList<MediaRecord>)rows.AsList();
        }).ConfigureAwait(false);
    }

    /// <summary>Move all <c>pending</c> rows for a chat to <c>queued</c>. Used after bootstrap.</summary>
    public Task<int> PromotePendingToQueuedAsync(long chatId) =>
        WithWriteLockAsync(c =>
            c.ExecuteAsync(
                "UPDATE Media SET status = 'queued', queued_at = datetime('now') " +
                "WHERE chat_id = @chatId AND status = 'pending'",
                new { chatId }));

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
    public Task UpsertScanStateAsync(long chatId, int lastScannedMsgId, bool bootstrapComplete) =>
        WithWriteLockAsync(async c =>
        {
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
        });

    public async Task<int> GetLastScannedMsgIdAsync(long chatId)
    {
        await using var c = _db.Open();
        return await c.ExecuteScalarAsync<int>(
            "SELECT COALESCE(last_scanned_msg_id, 0) FROM ChatScanState WHERE chat_id = @chatId",
            new { chatId });
    }

    public async Task<IReadOnlyList<ChatScanStateRecord>> GetAllScanStatesAsync()
    {
        await using var c = _db.Open();
        var rows = await c.QueryAsync<ChatScanStateRecord>(
            """
            SELECT chat_id, last_scanned_msg_id, last_scanned_date, bootstrap_complete, last_forward_at
              FROM ChatScanState
            """);
        return rows.AsList();
    }

    // ── LegacyDedup ───────────────────────────────────────────────────────────
    public async Task<long> CountLegacyDedupAsync()
    {
        await using var c = _db.Open();
        return await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM LegacyDedup");
    }

    public async Task BulkInsertLegacyDedupAsync(IEnumerable<long> documentIds) =>
        await WithWriteLockAsync(async c =>
        {
            using var tx = c.BeginTransaction();
            await c.ExecuteAsync(
                "INSERT OR IGNORE INTO LegacyDedup (document_id) VALUES (@documentId)",
                documentIds.Select(id => new { documentId = id }),
                transaction: tx);
            tx.Commit();
        });
}

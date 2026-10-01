using FluentAssertions;
using Microsoft.Data.Sqlite;
using TelegramAutoDownload.Headless.Data;

namespace TelegramAutoDownload.Headless.Tests;

public sealed class MediaRepositoryTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tad-repo-{Guid.NewGuid():N}.db");
    private Database _db = null!;
    private MediaRepository _repo = null!;

    public async Task InitializeAsync()
    {
        _db = new Database(_dbPath);
        await _db.InitializeAsync();
        _repo = new MediaRepository(_db);
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
        return Task.CompletedTask;
    }

    private static MediaRecord PendingRow(long chatId, int messageId) => new()
    {
        chat_id = chatId,
        message_id = messageId,
        kind = MediaKind.File.ToDbValue(),
        size_bytes = 1024,
        date_utc = "2024-06-01T12:00:00Z",
        file_name = "doc.bin",
    };

    [Fact]
    public async Task InsertPendingAsync_is_idempotent_on_same_chat_message()
    {
        var row = PendingRow(100, 1);
        (await _repo.InsertPendingAsync(row)).Should().BeTrue();
        (await _repo.InsertPendingAsync(row)).Should().BeFalse();
        (await _repo.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task PickQueuedAsync_claims_rows_atomically_and_excludes_other_statuses()
    {
        var pending = PendingRow(200, 1);
        await _repo.InsertPendingAsync(pending);
        await _repo.SetStatusAsync(200, 1, MediaStatus.Queued);

        var other = PendingRow(200, 2);
        await _repo.InsertPendingAsync(other);

        var claimed = await _repo.PickQueuedAsync(5);
        claimed.Should().HaveCount(1);
        claimed[0].message_id.Should().Be(1);
        claimed[0].status.Should().Be(MediaStatus.InProgress.ToDbValue());

        var peek = await _repo.GetAsync(200, 2);
        peek!.status.Should().Be(MediaStatus.Pending.ToDbValue());
    }

    [Fact]
    public async Task PickQueuedAsync_with_empty_chat_filter_returns_nothing()
    {
        var row = PendingRow(300, 1);
        await _repo.InsertPendingAsync(row);
        await _repo.SetStatusAsync(300, 1, MediaStatus.Queued);

        var claimed = await _repo.PickQueuedAsync(3, Array.Empty<long>());
        claimed.Should().BeEmpty();
    }

    [Fact]
    public async Task IsKnownDocumentAsync_honors_done_and_legacy_dedup()
    {
        var row = PendingRow(400, 1);
        row.document_id = 9999;
        await _repo.InsertPendingAsync(row);
        await _repo.SetStatusAsync(400, 1, MediaStatus.Done);

        (await _repo.IsKnownDocumentAsync(9999)).Should().BeTrue();

        await _repo.BulkInsertLegacyDedupAsync(new[] { 8888L });
        (await _repo.IsKnownDocumentAsync(8888)).Should().BeTrue();
        (await _repo.IsKnownDocumentAsync(7777)).Should().BeFalse();
    }

    [Fact]
    public async Task RequeueAllFailedAsync_scopes_to_chat_when_requested()
    {
        await _repo.InsertPendingAsync(PendingRow(500, 1));
        await _repo.InsertPendingAsync(PendingRow(500, 2));
        await _repo.InsertPendingAsync(PendingRow(501, 3));
        await _repo.SetStatusAsync(500, 1, MediaStatus.Failed, "err");
        await _repo.SetStatusAsync(500, 2, MediaStatus.Failed, "err");
        await _repo.SetStatusAsync(501, 3, MediaStatus.Failed, "err");

        var n = await _repo.RequeueAllFailedAsync(500);
        n.Should().Be(2);

        (await _repo.GetAsync(501, 3))!.status.Should().Be(MediaStatus.Failed.ToDbValue());
        (await _repo.GetAsync(500, 1))!.status.Should().Be(MediaStatus.Queued.ToDbValue());
    }
}

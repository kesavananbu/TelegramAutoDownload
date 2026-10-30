using FluentAssertions;
using Microsoft.Data.Sqlite;
using TelegramAutoDownload.Headless.Data;

namespace TelegramAutoDownload.Headless.Tests;

public sealed class MediaRepositoryConcurrencyTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"tad-conc-{Guid.NewGuid():N}.db");
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

    [Fact]
    public async Task Concurrent_writes_do_not_throw_sqlite_locked()
    {
        var tasks = Enumerable.Range(0, 40).Select(i => Task.Run(async () =>
        {
            var row = new MediaRecord
            {
                chat_id = 9000 + i,
                message_id = i,
                kind = MediaKind.File.ToDbValue(),
                size_bytes = 1,
                date_utc = "2024-01-01T00:00:00Z",
                file_name = $"f{i}.bin",
            };
            await _repo.InsertPendingAsync(row);
            await _repo.SetStatusAsync(9000 + i, i, MediaStatus.Queued);
            await _repo.UpsertScanStateAsync(9000 + i, i, false);
        }));

        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync<SqliteException>();
        (await _repo.CountAsync()).Should().Be(40);
    }
}

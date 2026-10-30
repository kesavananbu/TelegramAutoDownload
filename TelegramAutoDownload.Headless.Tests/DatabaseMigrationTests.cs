using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using TelegramAutoDownload.Headless.Data;

namespace TelegramAutoDownload.Headless.Tests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task InitializeAsync_applies_embedded_migrations_and_creates_core_tables()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tad-migrate-{Guid.NewGuid():N}.db");
        try
        {
            var db = new Database(dbPath);
            await db.InitializeAsync();

            await using var conn = db.Open();
            var tables = (await conn.QueryAsync<string>(
                "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")).ToList();

            tables.Should().Contain("Media");
            tables.Should().Contain("ChatScanState");
            tables.Should().Contain("LegacyDedup");
            tables.Should().Contain("SchemaMigration");

            var versions = (await conn.QueryAsync<long>("SELECT version FROM SchemaMigration ORDER BY version")).ToList();
            versions.Should().Contain(1L);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_is_idempotent_on_second_run()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tad-migrate-{Guid.NewGuid():N}.db");
        try
        {
            var db = new Database(dbPath);
            await db.InitializeAsync();
            await db.InitializeAsync();

            await using var conn = db.Open();
            var count = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM SchemaMigration");
            count.Should().Be(1);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_refuses_database_newer_than_embedded_migrations()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tad-migrate-{Guid.NewGuid():N}.db");
        try
        {
            var db = new Database(dbPath);
            await db.InitializeAsync();

            await using (var conn = db.Open())
            {
                await conn.ExecuteAsync(
                    "INSERT INTO SchemaMigration (version, name) VALUES (9999, '999_future.sql')");
            }

            var act = async () => await db.InitializeAsync();
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*newer than this application*");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }

    [Fact]
    public async Task InitializeAsync_requeues_stuck_in_progress_rows()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tad-migrate-{Guid.NewGuid():N}.db");
        try
        {
            var db = new Database(dbPath);
            await db.InitializeAsync();

            await using (var conn = db.Open())
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO Media (chat_id, message_id, kind, size_bytes, date_utc, status, attempts, discovered_at, started_at)
                    VALUES (1, 42, 'File', 0, '2024-01-01T00:00:00Z', 'in_progress', 0, datetime('now'), datetime('now'))
                    """);
            }

            await db.InitializeAsync();

            await using var verify = db.Open();
            var status = await verify.ExecuteScalarAsync<string>(
                "SELECT status FROM Media WHERE chat_id = 1 AND message_id = 42");
            status.Should().Be("queued");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
        }
    }
}

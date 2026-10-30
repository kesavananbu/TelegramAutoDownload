using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using TelegramAutoDownload.Headless.Data;

namespace TelegramAutoDownload.Headless.Tests;

public sealed class LegacyDedupMigrationTests
{
    [Fact]
    public async Task RunAsync_on_repeated_startup_imports_new_json_ids_without_duplicates()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"tad-dedup-{Guid.NewGuid():N}.db");
        var jsonPath = Path.Combine(Path.GetTempPath(), $"downloaded_ids-{Guid.NewGuid():N}.json");
        try
        {
            var db = new Database(dbPath);
            await db.InitializeAsync();
            var repo = new MediaRepository(db);

            await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(new List<long> { 100, 200 }));
            await LegacyDedupMigration.RunAsync(repo, jsonPath);
            (await repo.CountLegacyDedupAsync()).Should().Be(2);
            (await repo.IsKnownDocumentAsync(100)).Should().BeTrue();
            (await repo.IsKnownDocumentAsync(200)).Should().BeTrue();

            await LegacyDedupMigration.RunAsync(repo, jsonPath);
            (await repo.CountLegacyDedupAsync()).Should().Be(2, "second run with unchanged JSON must not duplicate rows");

            await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(new List<long> { 100, 200, 300 }));
            await LegacyDedupMigration.RunAsync(repo, jsonPath);
            (await repo.CountLegacyDedupAsync()).Should().Be(3);
            (await repo.IsKnownDocumentAsync(300)).Should().BeTrue();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(dbPath)) File.Delete(dbPath);
            if (File.Exists(jsonPath)) File.Delete(jsonPath);
        }
    }
}

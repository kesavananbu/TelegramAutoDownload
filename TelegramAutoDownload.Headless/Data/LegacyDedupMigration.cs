using System.Text.Json;
using Serilog;

namespace TelegramAutoDownload.Headless.Data;

/// <summary>
/// One-time import of the WPF app's <c>downloaded_ids.json</c> file into the
/// <c>LegacyDedup</c> table. Runs at most once per database — subsequent launches
/// detect that the table already has rows and skip the work entirely.
/// </summary>
public static class LegacyDedupMigration
{
    public static async Task RunAsync(MediaRepository repo, string? legacyPath = null)
    {
        var path = legacyPath ?? HeadlessPaths.LegacyDedupIndexFile;

        var existing = await repo.CountLegacyDedupAsync();
        if (existing > 0)
        {
            Log.Debug("LegacyDedup already has {Count} row(s); skipping import", existing);
            return;
        }

        if (!File.Exists(path))
        {
            Log.Information("No legacy dedup index at {Path}; starting with a fresh database", path);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var ids  = JsonSerializer.Deserialize<List<long>>(json);
            if (ids == null || ids.Count == 0)
            {
                Log.Information("Legacy dedup index {Path} was empty", path);
                return;
            }

            await repo.BulkInsertLegacyDedupAsync(ids);
            Log.Information("Imported {Count} legacy dedup IDs from {Path}", ids.Count, path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to import legacy dedup index from {Path} — continuing with empty table", path);
        }
    }
}

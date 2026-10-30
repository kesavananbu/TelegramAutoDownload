using System.Text.Json;
using Serilog;

namespace TelegramAutoDownload.Headless.Data;

/// <summary>
/// Reconciles the WPF/live <c>downloaded_ids.json</c> file into <c>LegacyDedup</c> on every
/// startup using <c>INSERT OR IGNORE</c>, so IDs appended by <c>FileDownloadIndex</c> after the
/// first import are not lost.
/// </summary>
public static class LegacyDedupMigration
{
    public static async Task RunAsync(MediaRepository repo, string? legacyPath = null)
    {
        var path = legacyPath ?? HeadlessPaths.LegacyDedupIndexFile;

        if (!File.Exists(path))
        {
            Log.Debug("No legacy dedup index at {Path}; skipping reconciliation", path);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var ids  = JsonSerializer.Deserialize<List<long>>(json);
            if (ids == null || ids.Count == 0)
            {
                Log.Debug("Legacy dedup index {Path} was empty", path);
                return;
            }

            var before = await repo.CountLegacyDedupAsync();
            await repo.BulkInsertLegacyDedupAsync(ids);
            var after = await repo.CountLegacyDedupAsync();
            var inserted = after - before;

            if (inserted > 0)
                Log.Information(
                    "Reconciled legacy dedup: {Inserted} new ID(s) from {Path} ({Total} row(s) in LegacyDedup)",
                    inserted, path, after);
            else
                Log.Debug("Legacy dedup index {Path} already fully reflected in LegacyDedup ({Total} row(s))",
                    path, after);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Legacy dedup index at {Path} is not valid JSON — skipping reconciliation", path);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to reconcile legacy dedup index from {Path}", path);
            throw;
        }
    }
}

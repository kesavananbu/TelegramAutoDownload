using System.Text.Json;

namespace TelegramAutoDownload.Headless.Scanning;

/// <summary>Persists flood-wait pause across process restarts (atomic temp + rename).</summary>
internal static class FloodWaitStatePersistence
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    internal static string StateFilePath =>
        Path.Combine(HeadlessPaths.DataDir, "flood-wait-state.json");

    internal sealed record PersistedState(DateTimeOffset PausedUntilUtc, string? Source);

    internal static void Save(DateTimeOffset pausedUntilUtc, string? source)
    {
        var path = StateFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        var json = JsonSerializer.Serialize(new PersistedState(pausedUntilUtc, source), JsonOpts);
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    internal static void Clear()
    {
        try
        {
            if (File.Exists(StateFilePath))
                File.Delete(StateFilePath);
        }
        catch
        {
            /* best effort */
        }
    }

    internal static bool TryRestore(out DateTimeOffset pausedUntilUtc, out string? source)
    {
        pausedUntilUtc = default;
        source = null;
        try
        {
            if (!File.Exists(StateFilePath))
                return false;

            var state = JsonSerializer.Deserialize<PersistedState>(File.ReadAllText(StateFilePath), JsonOpts);
            if (state == null || state.PausedUntilUtc <= DateTimeOffset.UtcNow)
            {
                Clear();
                return false;
            }

            pausedUntilUtc = state.PausedUntilUtc;
            source = state.Source;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

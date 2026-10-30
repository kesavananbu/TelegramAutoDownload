using System.Reflection;
using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace TelegramAutoDownload.Headless.Data;

/// <summary>
/// Opens connections to the SQLite database living in the shared data volume
/// (<see cref="HeadlessPaths.DatabaseFile"/>), applies pending schema migrations,
/// and performs startup hygiene (crash recovery for stuck <c>in_progress</c> rows).
///
/// Migrations are plain <c>*.sql</c> files embedded in the assembly under
/// <c>Data/Schema/</c>. File names must start with a numeric prefix
/// (e.g. <c>001_initial.sql</c>) — they are applied in numeric version order and recorded
/// in <c>SchemaMigration</c> so each script runs exactly once per database.
/// </summary>
public sealed class Database
{
    public string ConnectionString { get; }

    public Database(string? dbPath = null)
    {
        var path = dbPath ?? HeadlessPaths.DatabaseFile;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        ConnectionString = $"Data Source={path};Mode=ReadWriteCreate";
    }

    /// <summary>
    /// Opens a connection with WAL journal mode and synchronous=NORMAL — the
    /// standard SQLite tuning for write-heavy single-process workloads.
    /// </summary>
    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        conn.Execute("""
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous  = NORMAL;
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = 5000;
            """);
        return conn;
    }

    /// <summary>
    /// Applies any pending migrations, then resets stuck <c>in_progress</c> rows
    /// to <c>queued</c> so a crashed/restarted container picks them back up cleanly.
    /// </summary>
    public async Task InitializeAsync()
    {
        await using var conn = Open();
        await ApplyMigrationsAsync(conn);
        var reverted = await conn.ExecuteAsync(
            "UPDATE Media SET status = 'queued', started_at = NULL " +
            "WHERE status = 'in_progress'");
        if (reverted > 0)
            Log.Information("Crash recovery: reverted {Count} stuck in_progress row(s) to queued", reverted);
    }

    internal static IReadOnlyList<(long Version, string FileName, string ResourceName)> DiscoverEmbeddedMigrations()
    {
        var asm = typeof(Database).Assembly;
        var list = new List<(long Version, string FileName, string ResourceName)>();

        foreach (var resource in asm.GetManifestResourceNames())
        {
            if (!resource.Contains(".Schema.", StringComparison.Ordinal) || !resource.EndsWith(".sql", StringComparison.Ordinal))
                continue;

            var fileName = resource[(resource.LastIndexOf(".Schema.", StringComparison.Ordinal) + ".Schema.".Length)..];
            var versionPart = fileName.Split('_')[0];
            if (!long.TryParse(versionPart, out var version))
            {
                throw new InvalidOperationException(
                    $"Embedded migration {resource} does not start with a numeric version prefix.");
            }

            list.Add((Version: version, FileName: fileName, ResourceName: resource));
        }

        return list.OrderBy(m => m.Version).ToList();
    }

    private static async Task ApplyMigrationsAsync(SqliteConnection conn)
    {
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS SchemaMigration (
                version    INTEGER PRIMARY KEY,
                name       TEXT NOT NULL,
                applied_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """);

        var embedded = DiscoverEmbeddedMigrations();
        if (embedded.Count == 0)
            throw new InvalidOperationException("No embedded schema migrations found in the assembly.");

        var maxEmbeddedVersion = embedded[^1].Version;

        var applied = (await conn.QueryAsync<long>("SELECT version FROM SchemaMigration")).ToHashSet();
        var newerThanApp = applied.Where(v => v > maxEmbeddedVersion).ToList();
        if (newerThanApp.Count > 0)
        {
            throw new InvalidOperationException(
                $"Database schema version(s) [{string.Join(", ", newerThanApp)}] are newer than this application " +
                $"(max embedded migration {maxEmbeddedVersion}). Upgrade the app before opening this database.");
        }

        var pending = embedded.Where(m => !applied.Contains(m.Version)).ToList();
        if (pending.Count == 0)
            return;

        var asm = typeof(Database).Assembly;
        await conn.ExecuteAsync("BEGIN IMMEDIATE");
        try
        {
            foreach (var (version, fileName, resource) in pending)
            {
                await using var stream = asm.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException($"Embedded resource {resource} not found");
                using var reader = new StreamReader(stream);
                var sql = await reader.ReadToEndAsync();

                await conn.ExecuteAsync(sql);
                await conn.ExecuteAsync(
                    "INSERT INTO SchemaMigration (version, name) VALUES (@version, @name)",
                    new { version, name = fileName });

                Log.Information("Applied schema migration {Version} {Name}", version, fileName);
            }

            await conn.ExecuteAsync("COMMIT");
        }
        catch
        {
            await conn.ExecuteAsync("ROLLBACK");
            throw;
        }
    }
}

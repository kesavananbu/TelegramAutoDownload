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
/// (e.g. <c>001_initial.sql</c>) — they are applied in lexical order and recorded
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
        // Mode=ReadWriteCreate so a missing file is created automatically.
        ConnectionString = $"Data Source={path};Mode=ReadWriteCreate;Cache=Shared";
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

    private static async Task ApplyMigrationsAsync(SqliteConnection conn)
    {
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS SchemaMigration (
                version    INTEGER PRIMARY KEY,
                name       TEXT NOT NULL,
                applied_at TEXT NOT NULL DEFAULT (datetime('now'))
            );
            """);

        var applied = (await conn.QueryAsync<long>("SELECT version FROM SchemaMigration"))
            .ToHashSet();

        var asm = typeof(Database).Assembly;
        var scripts = asm.GetManifestResourceNames()
            .Where(n => n.Contains(".Schema.") && n.EndsWith(".sql"))
            .OrderBy(n => n)
            .ToList();

        foreach (var resource in scripts)
        {
            // Resource name looks like "TelegramAutoDownload.Headless.Data.Schema.001_initial.sql"
            var fileName = resource[(resource.LastIndexOf(".Schema.", StringComparison.Ordinal) + ".Schema.".Length)..];
            var versionPart = fileName.Split('_')[0];
            if (!long.TryParse(versionPart, out var version))
            {
                Log.Warning("Skipping migration {Resource} — file name does not start with a numeric version", resource);
                continue;
            }
            if (applied.Contains(version)) continue;

            await using var stream = asm.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Embedded resource {resource} not found");
            using var reader = new StreamReader(stream);
            var sql = await reader.ReadToEndAsync();

            using var tx = conn.BeginTransaction();
            await conn.ExecuteAsync(sql, transaction: tx);
            await conn.ExecuteAsync(
                "INSERT INTO SchemaMigration (version, name) VALUES (@version, @name)",
                new { version, name = fileName },
                transaction: tx);
            tx.Commit();

            Log.Information("Applied schema migration {Version} {Name}", version, fileName);
        }
    }
}

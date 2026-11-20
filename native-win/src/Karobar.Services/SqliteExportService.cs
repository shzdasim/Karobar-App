using Dapper;
using Karobar.Services.Infrastructure;
using Microsoft.Data.Sqlite;

namespace Karobar.Services;

public class SqliteExportService
{
    private readonly KarobarDb _db;

    private static readonly string[] EphemeralDataTables =
    {
        "sessions",
        "cache",
        "cache_locks",
        "jobs",
        "job_batches",
        "personal_access_tokens"
    };

    public SqliteExportService(KarobarDb db)
    {
        _db = db;
    }

    public async Task<string> ExportToSqliteFileAsync(string? outputPath = null)
    {
        var exportDir = _db.StoragePath("app/backups/exports");
        if (!Directory.Exists(exportDir))
        {
            Directory.CreateDirectory(exportDir);
        }

        var targetPath = !string.IsNullOrWhiteSpace(outputPath)
            ? outputPath
            : Path.Combine(exportDir, $"karobar-portable-{DateTime.UtcNow:yyyy-MM-dd_HHmmss}.sqlite");

        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        var currentDbPath = _db.SqlitePath;
        if (!string.IsNullOrWhiteSpace(currentDbPath) && File.Exists(currentDbPath))
        {
            using (var srcConn = new SqliteConnection($"Data Source={currentDbPath};Mode=ReadOnly;Pooling=False;"))
            using (var dstConn = new SqliteConnection($"Data Source={targetPath};Mode=ReadWriteCreate;Pooling=False;"))
            {
                await srcConn.OpenAsync();
                await dstConn.OpenAsync();
                srcConn.BackupDatabase(dstConn);
            }
            await SanitizeExportedSqliteFileAsync(targetPath);
            return targetPath;
        }

        // Fallback create empty SQLite file if source file does not exist
        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = targetPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();
        await conn.ExecuteAsync("PRAGMA journal_mode = DELETE;");
        return targetPath;
    }

    private static async Task SanitizeExportedSqliteFileAsync(string sqlitePath)
    {
        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = sqlitePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString();

            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync();

            foreach (var tbl in EphemeralDataTables)
            {
                try
                {
                    var escaped = tbl.Replace("\"", "\"\"");
                    await conn.ExecuteAsync($"DELETE FROM \"{escaped}\";");
                }
                catch
                {
                }
            }

            await conn.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE);");
        }
        catch
        {
        }
    }
}

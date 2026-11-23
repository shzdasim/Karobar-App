using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;

namespace Karobar.Services;

public class BackupService
{
    private readonly KarobarDb _db;
    private readonly SqliteExportService _sqliteExportService;
    private readonly string _backupPath;

    public BackupService(KarobarDb db, SqliteExportService sqliteExportService)
    {
        _db = db;
        _sqliteExportService = sqliteExportService;
        _backupPath = _db.StoragePath("app/backups");
        EnsureBackupDirectoryExists();
    }

    public string DefaultBackupPath => _backupPath;

    public void EnsureBackupDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(_backupPath))
            {
                Directory.CreateDirectory(_backupPath);
            }
        }
        catch
        {
        }
    }

    public async Task EnsureBackupSchemaAsync()
    {
        try
        {
            if (!await _db.HasTableAsync("backup_logs"))
            {
                await _db.ExecuteAsync(@"
                    CREATE TABLE IF NOT EXISTS backup_logs (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        filename VARCHAR(255) NOT NULL UNIQUE,
                        type VARCHAR(50) NOT NULL,
                        size INTEGER NOT NULL DEFAULT 0,
                        path VARCHAR(500) NOT NULL DEFAULT '',
                        metadata TEXT NULL,
                        created_by VARCHAR(100) NULL,
                        is_automated INTEGER NOT NULL DEFAULT 0,
                        storage_destination VARCHAR(30) NOT NULL DEFAULT 'local',
                        remote_path VARCHAR(500) NULL,
                        remote_file_id VARCHAR(255) NULL,
                        remote_status VARCHAR(30) NOT NULL DEFAULT 'skipped',
                        remote_error TEXT NULL,
                        status VARCHAR(30) NOT NULL DEFAULT 'pending',
                        error_message TEXT NULL,
                        created_at DATETIME NULL,
                        updated_at DATETIME NULL,
                        expires_at DATETIME NULL
                    );");
                KarobarDb.FlushSchemaCache();
            }

            if (!await _db.HasTableAsync("backup_schedules"))
            {
                await _db.ExecuteAsync(@"
                    CREATE TABLE IF NOT EXISTS backup_schedules (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        is_automated INTEGER NOT NULL DEFAULT 0,
                        frequency VARCHAR(20) NOT NULL DEFAULT 'daily',
                        time_of_day VARCHAR(10) NOT NULL DEFAULT '02:00',
                        day_of_week INTEGER NULL DEFAULT 0,
                        day_of_month INTEGER NULL DEFAULT 1,
                        backup_type VARCHAR(20) NOT NULL DEFAULT 'full',
                        retention_days INTEGER NOT NULL DEFAULT 30,
                        max_backups_keep INTEGER NOT NULL DEFAULT 10,
                        custom_backup_path VARCHAR(500) NULL,
                        storage_destination VARCHAR(30) NOT NULL DEFAULT 'local',
                        destination_config TEXT NULL,
                        notify_on_failure INTEGER NOT NULL DEFAULT 1,
                        notification_email VARCHAR(255) NULL,
                        last_run_at DATETIME NULL,
                        last_run_status VARCHAR(20) NOT NULL DEFAULT 'none',
                        last_error_message TEXT NULL,
                        created_at DATETIME NULL,
                        updated_at DATETIME NULL
                    );");
                KarobarDb.FlushSchemaCache();
            }
        }
        catch
        {
        }
    }

    public async Task<Dictionary<string, object?>> GetScheduleAsync()
    {
        await EnsureBackupSchemaAsync();
        var row = await _db.QuerySingleOrDefaultAsync("SELECT * FROM backup_schedules ORDER BY id ASC LIMIT 1");
        if (row != null) return row;

        var id = await _db.InsertAsync("backup_schedules", new Dictionary<string, object?>
        {
            ["is_automated"] = 0,
            ["frequency"] = "daily",
            ["time_of_day"] = "02:00",
            ["day_of_week"] = 0,
            ["day_of_month"] = 1,
            ["backup_type"] = "full",
            ["retention_days"] = 30,
            ["max_backups_keep"] = 10,
            ["storage_destination"] = "local",
            ["notify_on_failure"] = 1,
            ["last_run_status"] = "none"
        });

        return (await _db.QuerySingleOrDefaultAsync("SELECT * FROM backup_schedules WHERE id = @id", new { id }))!;
    }

    public bool IsReachableHostPath(string? path)
    {
        var trimmed = (path ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        if (isWindows && trimmed.StartsWith("/Users/", StringComparison.OrdinalIgnoreCase)) return false;
        if (!isWindows && Regex.IsMatch(trimmed, @"^[A-Za-z]:[\\/]")) return false;

        return true;
    }

    private const long MaxExtractedBackupBytes = 2L * 1024L * 1024L * 1024L; // 2 GB decompression bomb cap

    public void AssertSafeBackupDirectory(string path)
    {
        var trimmed = (path ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            throw new ArgumentException("Backup path cannot be empty.");
        }
        if (trimmed.Contains(".."))
        {
            throw new ArgumentException("Backup path cannot contain relative parent traversal segments (..).");
        }
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) || trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("Network UNC backup paths (\\\\server\\share) are not allowed. Use a mapped local drive folder.");
        }
        if (!Path.IsPathRooted(trimmed))
        {
            throw new ArgumentException("Backup path must be an absolute directory path.");
        }

        var full = Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedLower = full.Replace('\\', '/').ToLowerInvariant();

        // Reject Windows/OS system directories
        if (Regex.IsMatch(normalizedLower, @"^[a-z]:/(windows|program files|program files \(x86\)|programdata|system volume information|\$recycle\.bin)(/|$)") ||
            normalizedLower is "/etc" or "/bin" or "/sbin" or "/usr" or "/system" or "/library" ||
            normalizedLower.StartsWith("/etc/", StringComparison.Ordinal) ||
            normalizedLower.StartsWith("/system/", StringComparison.Ordinal))
        {
            // Permit Karobar's own default backup path even if app lives under Program Files
            var allowedDefault = Path.GetFullPath(_backupPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!full.Equals(allowedDefault, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(allowedDefault + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("System and Program Files directories cannot be used as custom backup destinations.");
            }
        }

        // Reject live database and public webroot directories
        foreach (var forbidden in new[] { _db.BasePath("database"), _db.PublicPath() })
        {
            var forbFull = Path.GetFullPath(forbidden).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Equals(forbFull, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(forbFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Live application database or public web directories cannot be used as backup destinations.");
            }
        }
    }

    private static async Task<string> ReadStreamLimitedAsync(Stream stream, long maxBytes)
    {
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        long total = 0;
        int read;
        while ((read = await stream.ReadAsync(buf, 0, buf.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException("Backup archive exceeds maximum safe uncompressed size (2 GB).");
            }
            ms.Write(buf, 0, read);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void ExtractZipEntryLimited(ZipArchiveEntry entry, string destPath, long maxBytes)
    {
        if (entry.Length > maxBytes)
        {
            throw new InvalidOperationException("Backup archive entry exceeds maximum safe uncompressed size (2 GB).");
        }
        using var src = entry.Open();
        using var dst = File.Create(destPath);
        var buf = new byte[81920];
        long total = 0;
        int read;
        while ((read = src.Read(buf, 0, buf.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException("Backup archive exceeds maximum safe uncompressed size (2 GB).");
            }
            dst.Write(buf, 0, read);
        }
    }

    public async Task<List<Dictionary<string, object?>>> ListBackupsAsync()
    {
        await EnsureBackupSchemaAsync();
        var rows = await _db.QueryListAsync("SELECT * FROM backup_logs ORDER BY id DESC");
        var list = new List<Dictionary<string, object?>>();

        foreach (var r in rows)
        {
            list.Add(FormatBackupRow(r));
        }

        return list;
    }

    public async Task<Dictionary<string, object?>> CreateBackupAsync(
        string type = "full",
        long? userId = null,
        bool isAutomated = false,
        string? customBackupPath = null)
    {
        EnsureBackupDirectoryExists();
        await EnsureBackupSchemaAsync();

        var filename = await GenerateUniqueBackupFilenameAsync(type);
        var schedule = await GetScheduleAsync();

        var scheduleDest = schedule.GetValueOrDefault("storage_destination")?.ToString() ?? "local";
        var configuredPath = scheduleDest == "custom_folder"
            ? schedule.GetValueOrDefault("custom_backup_path")?.ToString()
            : null;

        var destinationPath = customBackupPath != null ? customBackupPath.Trim() : (configuredPath ?? "").Trim();
        if (!string.IsNullOrEmpty(destinationPath) && !IsReachableHostPath(destinationPath))
        {
            destinationPath = "";
        }

        var destinationType = !string.IsNullOrEmpty(destinationPath) ? "custom_folder" : "local";
        var ext = type switch
        {
            "full" => "zip",
            "settings" => "json",
            _ => "sql.gz"
        };

        var filePath = Path.Combine(_backupPath, $"{filename}.{ext}");

        var logId = await _db.InsertAsync("backup_logs", new Dictionary<string, object?>
        {
            ["filename"] = filename,
            ["type"] = type,
            ["size"] = 0,
            ["path"] = $"backups/{filename}.{ext}",
            ["created_by"] = userId?.ToString(),
            ["is_automated"] = isAutomated ? 1 : 0,
            ["storage_destination"] = destinationType,
            ["remote_status"] = !string.IsNullOrEmpty(destinationPath) ? "pending" : "skipped",
            ["status"] = "pending"
        });

        try
        {
            if (type == "settings")
            {
                var setting = await _db.HasTableAsync("settings")
                    ? await _db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
                    : new Dictionary<string, object?>();
                var themes = await _db.HasTableAsync("theme_settings")
                    ? await _db.QueryListAsync("SELECT * FROM theme_settings")
                    : new List<Dictionary<string, object?>>();

                var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    exported_at = DateTime.UtcNow.ToString("O"),
                    type = "settings",
                    settings = setting,
                    theme_settings = themes
                }, new JsonSerializerOptions { WriteIndented = true });

                await File.WriteAllBytesAsync(filePath, jsonBytes);
            }
            else if (type == "database")
            {
                var sqlDump = await GenerateSqlDumpStringAsync();
                await using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                await using var gz = new GZipStream(fs, CompressionLevel.Optimal);
                var bytes = Encoding.UTF8.GetBytes(sqlDump);
                await gz.WriteAsync(bytes);
            }
            else
            {
                // full zip backup containing database.sqlite + database.sql + settings.json
                var tempSqlite = await _sqliteExportService.ExportToSqliteFileAsync();
                var sqlDump = await GenerateSqlDumpStringAsync();
                await using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    if (File.Exists(tempSqlite))
                    {
                        archive.CreateEntryFromFile(tempSqlite, "database.sqlite", CompressionLevel.Optimal);
                    }

                    var sqlEntry = archive.CreateEntry("database.sql", CompressionLevel.Optimal);
                    await using (var entryStream = sqlEntry.Open())
                    {
                        var bytes = Encoding.UTF8.GetBytes(sqlDump);
                        await entryStream.WriteAsync(bytes);
                    }
                }
                if (File.Exists(tempSqlite)) File.Delete(tempSqlite);
            }

            var size = File.Exists(filePath) ? new FileInfo(filePath).Length : 0L;
            string? remotePath = null;
            var remoteStatus = "skipped";
            string? remoteError = null;

            if (!string.IsNullOrEmpty(destinationPath))
            {
                try
                {
                    if (!Directory.Exists(destinationPath))
                    {
                        Directory.CreateDirectory(destinationPath);
                    }
                    remotePath = Path.Combine(destinationPath, Path.GetFileName(filePath));
                    File.Copy(filePath, remotePath, overwrite: true);
                    remoteStatus = "completed";
                }
                catch (Exception copyEx)
                {
                    remoteStatus = "failed";
                    remoteError = copyEx.Message;
                }
            }

            await _db.UpdateAsync("backup_logs", logId, new Dictionary<string, object?>
            {
                ["size"] = size,
                ["status"] = "completed",
                ["remote_path"] = remotePath,
                ["remote_status"] = remoteStatus,
                ["remote_error"] = remoteError
            });
        }
        catch (Exception ex)
        {
            await _db.UpdateAsync("backup_logs", logId, new Dictionary<string, object?>
            {
                ["status"] = "failed",
                ["error_message"] = ex.Message
            });
            throw;
        }

        var updated = await _db.QuerySingleOrDefaultAsync("SELECT * FROM backup_logs WHERE id = @id", new { id = logId });
        return FormatBackupRow(updated!);
    }

    public async Task<string?> GetDownloadPathAsync(long id)
    {
        await EnsureBackupSchemaAsync();
        var row = await _db.QuerySingleOrDefaultAsync("SELECT * FROM backup_logs WHERE id = @id", new { id });
        if (row == null) return null;

        var filename = row.GetValueOrDefault("filename")?.ToString() ?? "";
        var type = row.GetValueOrDefault("type")?.ToString() ?? "full";
        var ext = type switch
        {
            "full" => "zip",
            "settings" => "json",
            _ => "sql.gz"
        };

        var candidates = new List<string>
        {
            Path.Combine(_backupPath, $"{filename}.{ext}")
        };

        if (row.TryGetValue("path", out var pObj) && pObj != null && !string.IsNullOrWhiteSpace(pObj.ToString()))
        {
            var rel = pObj.ToString()!.TrimStart('/', '\\');
            candidates.Add(_db.StoragePath($"app/{rel}"));
            candidates.Add(Path.Combine(_backupPath, Path.GetFileName(rel)));
        }

        if (row.TryGetValue("remote_path", out var rObj) && rObj != null && !string.IsNullOrWhiteSpace(rObj.ToString()))
        {
            candidates.Add(rObj.ToString()!);
        }

        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<bool> DeleteBackupAsync(long id)
    {
        await EnsureBackupSchemaAsync();
        var row = await _db.QuerySingleOrDefaultAsync("SELECT * FROM backup_logs WHERE id = @id", new { id });
        if (row == null) return false;

        var path = await GetDownloadPathAsync(id);
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }

        await _db.ExecuteAsync("DELETE FROM backup_logs WHERE id = @id", new { id });
        return true;
    }

    private static bool IsMacOsMetadataZipEntry(string fullName)
    {
        var normalized = fullName.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }
        var fileName = Path.GetFileName(normalized);
        return fileName.StartsWith("._", StringComparison.Ordinal);
    }

    private static async Task ValidateCandidateSqliteDatabaseAsync(string sqliteFilePath)
    {
        if (string.IsNullOrWhiteSpace(sqliteFilePath) || !File.Exists(sqliteFilePath))
        {
            throw new InvalidOperationException("Backup SQLite file not found.");
        }

        var fileInfo = new FileInfo(sqliteFilePath);
        if (fileInfo.Length < 100)
        {
            throw new InvalidOperationException("Backup file is not a valid SQLite 3 database.");
        }

        var header = new byte[16];
        await using (var fs = new FileStream(sqliteFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var totalRead = 0;
            while (totalRead < 16)
            {
                var read = await fs.ReadAsync(header.AsMemory(totalRead, 16 - totalRead));
                if (read == 0) break;
                totalRead += read;
            }
            if (totalRead < 16 || !header.AsSpan(0, 16).SequenceEqual("SQLite format 3\0"u8))
            {
                throw new InvalidOperationException("Backup file is not a valid SQLite 3 database.");
            }
        }

        try
        {
            var connStr = new SqliteConnectionStringBuilder
            {
                DataSource = sqliteFilePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();

            await using var conn = new SqliteConnection(connStr);
            await conn.OpenAsync();

            var quickCheck = await conn.ExecuteScalarAsync<string>("PRAGMA quick_check(1);");
            if (!string.Equals(quickCheck, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Backup SQLite database failed integrity verification (PRAGMA quick_check).");
            }

            var tableNames = (await conn.QueryAsync<string>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!tableNames.Contains("users") || !tableNames.Contains("settings"))
            {
                throw new InvalidOperationException("Backup SQLite database is missing required Karobar schema tables (users, settings).");
            }
        }
        catch (SqliteException ex)
        {
            throw new InvalidOperationException("Backup file is corrupted or not a readable SQLite 3 database.", ex);
        }
    }

    private async Task<string?> CreatePreRestoreSafetySnapshotAsync()
    {
        if (!File.Exists(_db.SqlitePath))
        {
            return null;
        }

        var snapPath = Path.Combine(
            _backupPath,
            $"pre-restore-safety-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.sqlite");

        try
        {
            Directory.CreateDirectory(_backupPath);

            var srcConnStr = new SqliteConnectionStringBuilder
            {
                DataSource = _db.SqlitePath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString();
            var dstConnStr = new SqliteConnectionStringBuilder
            {
                DataSource = snapPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString();

            await using (var srcConn = new SqliteConnection(srcConnStr))
            await using (var dstConn = new SqliteConnection(dstConnStr))
            {
                await srcConn.OpenAsync();
                await using (var timeoutCmd = srcConn.CreateCommand())
                {
                    timeoutCmd.CommandText = "PRAGMA busy_timeout=15000;";
                    await timeoutCmd.ExecuteNonQueryAsync();
                }
                await dstConn.OpenAsync();
                srcConn.BackupDatabase(dstConn);
            }

            var snapInfo = new FileInfo(snapPath);
            if (!snapInfo.Exists || snapInfo.Length <= 0)
            {
                throw new InvalidOperationException("Pre-restore safety backup file is empty.");
            }

            return snapPath;
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(snapPath)) File.Delete(snapPath);
            }
            catch
            {
            }

            throw new InvalidOperationException($"Pre-restore safety backup failed: {ex.Message}. Restore aborted.", ex);
        }
    }

    public async Task RestoreBackupAsync(long id, string password, long? currentUserId = null)
    {
        var path = await GetDownloadPathAsync(id);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new InvalidOperationException("Backup file not found on disk.");
        }

        var lower = path.ToLowerInvariant();
        if (lower.EndsWith(".json"))
        {
            var json = await File.ReadAllTextAsync(path);
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                throw new InvalidOperationException("Backup JSON file is malformed or corrupted.");
            }
            using (doc)
            {
                await CreatePreRestoreSafetySnapshotAsync();

                if (doc.RootElement.TryGetProperty("settings", out var sEl) && sEl.ValueKind == JsonValueKind.Object && await _db.HasTableAsync("settings"))
                {
                    var cur = await _db.QuerySingleOrDefaultAsync("SELECT id FROM settings ORDER BY id ASC LIMIT 1");
                    if (cur != null)
                    {
                        var upd = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var p in sEl.EnumerateObject())
                        {
                            if (p.Name.Equals("id", StringComparison.OrdinalIgnoreCase)) continue;
                            upd[p.Name] = KarobarDb.PrepareDbValue(p.Value);
                        }
                        if (upd.Count > 0) await _db.UpdateAsync("settings", Convert.ToInt64(cur["id"]), upd);
                    }
                }
            }
        }
        else if (lower.EndsWith(".sql") || lower.EndsWith(".sql.gz"))
        {
            string sqlContent;
            if (lower.EndsWith(".sql.gz"))
            {
                await using var fs = File.OpenRead(path);
                await using var gz = new GZipStream(fs, CompressionMode.Decompress);
                sqlContent = await ReadStreamLimitedAsync(gz, MaxExtractedBackupBytes);
            }
            else
            {
                sqlContent = await File.ReadAllTextAsync(path, Encoding.UTF8);
            }

            var snapPath = await RestoreFromSqlScriptAsync(sqlContent);
            KarobarDb.FlushSchemaCache();
            await UpgradeRestoredSchemaAsync(snapPath);
        }
        else if (lower.EndsWith(".sqlite") || lower.EndsWith(".db"))
        {
            await ValidateCandidateSqliteDatabaseAsync(path);
            var snapPath = await CreatePreRestoreSafetySnapshotAsync();

            SqliteConnection.ClearAllPools();
            File.Copy(path, _db.SqlitePath, overwrite: true);
            try { if (File.Exists(_db.SqlitePath + "-wal")) File.Delete(_db.SqlitePath + "-wal"); } catch { }
            try { if (File.Exists(_db.SqlitePath + "-shm")) File.Delete(_db.SqlitePath + "-shm"); } catch { }
            KarobarDb.FlushSchemaCache();
            await UpgradeRestoredSchemaAsync(snapPath);
        }
        else if (lower.EndsWith(".zip"))
        {
            ZipArchive archive;
            try
            {
                archive = ZipFile.OpenRead(path);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidOperationException("Backup ZIP archive is corrupted or invalid.", ex);
            }

            using (archive)
            {
                var sqliteEntry = archive.Entries
                    .Where(e => !IsMacOsMetadataZipEntry(e.FullName) &&
                                (e.FullName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
                                 e.FullName.EndsWith(".db", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(e => e.Length)
                    .FirstOrDefault();
                var sqlEntry = archive.Entries
                    .Where(e => !IsMacOsMetadataZipEntry(e.FullName) &&
                                (e.FullName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ||
                                 e.FullName.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(e => e.Length)
                    .FirstOrDefault();
                if (sqliteEntry != null)
                {
                    var tmpExtract = Path.Combine(Path.GetTempPath(), $"karobar_restore_{Guid.NewGuid():N}.sqlite");
                    try
                    {
                        ExtractZipEntryLimited(sqliteEntry, tmpExtract, MaxExtractedBackupBytes);
                        await ValidateCandidateSqliteDatabaseAsync(tmpExtract);
                        var snapPath = await CreatePreRestoreSafetySnapshotAsync();

                        SqliteConnection.ClearAllPools();
                        File.Copy(tmpExtract, _db.SqlitePath, overwrite: true);
                        try { if (File.Exists(_db.SqlitePath + "-wal")) File.Delete(_db.SqlitePath + "-wal"); } catch { }
                        try { if (File.Exists(_db.SqlitePath + "-shm")) File.Delete(_db.SqlitePath + "-shm"); } catch { }
                        KarobarDb.FlushSchemaCache();
                        await UpgradeRestoredSchemaAsync(snapPath);
                    }
                    finally
                    {
                        try { if (File.Exists(tmpExtract)) File.Delete(tmpExtract); } catch { }
                    }
                }
                else if (sqlEntry != null)
                {
                    string sqlContent;
                    await using (var entryStream = sqlEntry.Open())
                    {
                        if (sqlEntry.FullName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                        {
                            await using var gz = new GZipStream(entryStream, CompressionMode.Decompress);
                            sqlContent = await ReadStreamLimitedAsync(gz, MaxExtractedBackupBytes);
                        }
                        else
                        {
                            sqlContent = await ReadStreamLimitedAsync(entryStream, MaxExtractedBackupBytes);
                        }
                    }
                    var snapPath = await RestoreFromSqlScriptAsync(sqlContent);
                    KarobarDb.FlushSchemaCache();
                    await UpgradeRestoredSchemaAsync(snapPath);
                }
                else
                {
                    throw new InvalidOperationException("ZIP archive does not contain a .sqlite, .db, or .sql database backup.");
                }
            }
        }
        else
        {
            throw new InvalidOperationException($"Unsupported backup restore format: {Path.GetFileName(path)}");
        }

        if (await _db.HasTableAsync("backup_logs"))
        {
            await _db.UpdateAsync("backup_logs", id, new Dictionary<string, object?>
            {
                ["status"] = "restored"
            });
        }
    }

    private async Task UpgradeRestoredSchemaAsync(string? safetySnapPath = null)
    {
        try
        {
            var bootstrapper = new Karobar.Data.DatabaseBootstrapper(
                new Karobar.Data.DbConnectionFactory(explicitSqlitePath: _db.SqlitePath));
            await bootstrapper.EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();
        }
        catch (Exception ex)
        {
            if (!string.IsNullOrEmpty(safetySnapPath) && File.Exists(safetySnapPath))
            {
                try
                {
                    SqliteConnection.ClearAllPools();
                    File.Copy(safetySnapPath, _db.SqlitePath, overwrite: true);
                    try { if (File.Exists(_db.SqlitePath + "-wal")) File.Delete(_db.SqlitePath + "-wal"); } catch { }
                    try { if (File.Exists(_db.SqlitePath + "-shm")) File.Delete(_db.SqlitePath + "-shm"); } catch { }
                    KarobarDb.FlushSchemaCache();
                }
                catch
                {
                }
            }

            throw new InvalidOperationException($"Post-restore schema upgrade failed: {ex.Message}", ex);
        }
    }

    public Dictionary<string, object?> ValidateUploadedBackup(IFormFile? file)
    {
        var errors = new List<string>();
        if (file == null || file.Length == 0)
        {
            errors.Add("Uploaded backup file is empty.");
            return new Dictionary<string, object?> { ["valid"] = false, ["errors"] = errors };
        }

        var name = file.FileName.ToLowerInvariant();
        if (!name.EndsWith(".zip") && !name.EndsWith(".sql") && !name.EndsWith(".sql.gz") && !name.EndsWith(".json") && !name.EndsWith(".sqlite"))
        {
            errors.Add("Unsupported backup file extension. Expected .zip, .sql.gz, .sql, .sqlite, or .json.");
        }

        return new Dictionary<string, object?>
        {
            ["valid"] = errors.Count == 0,
            ["errors"] = errors
        };
    }

    public async Task<Dictionary<string, object?>> UploadBackupAsync(IFormFile file, long? userId = null)
    {
        EnsureBackupDirectoryExists();
        await EnsureBackupSchemaAsync();

        var origName = file.FileName;
        var lower = origName.ToLowerInvariant();
        var type = lower.EndsWith(".json") ? "settings" : (lower.EndsWith(".zip") ? "full" : "database");
        var ext = lower.EndsWith(".sql.gz") ? "sql.gz"
            : lower.EndsWith(".sqlite") ? "sqlite"
            : lower.EndsWith(".sql") ? "sql"
            : lower.EndsWith(".json") ? "json"
            : "zip";

        var filename = await GenerateUniqueBackupFilenameAsync(type, "uploaded");
        var targetPath = Path.Combine(_backupPath, $"{filename}.{ext}");

        await using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(fs);
        }

        var size = new FileInfo(targetPath).Length;
        var metadataJson = JsonSerializer.Serialize(new { original_name = origName });

        var logId = await _db.InsertAsync("backup_logs", new Dictionary<string, object?>
        {
            ["filename"] = filename,
            ["type"] = type,
            ["size"] = size,
            ["path"] = $"backups/{filename}.{ext}",
            ["metadata"] = metadataJson,
            ["created_by"] = userId?.ToString(),
            ["is_automated"] = 0,
            ["storage_destination"] = "local",
            ["remote_status"] = "skipped",
            ["status"] = "completed"
        });

        var row = await _db.QuerySingleOrDefaultAsync("SELECT * FROM backup_logs WHERE id = @id", new { id = logId });
        var formatted = FormatBackupRow(row!);
        formatted["original_name"] = origName;
        return formatted;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int i = (int)Math.Floor(Math.Log(bytes, 1024));
        i = Math.Clamp(i, 0, units.Length - 1);
        return $"{Math.Round(bytes / Math.Pow(1024, i), 2)} {units[i]}";
    }

    private Dictionary<string, object?> FormatBackupRow(Dictionary<string, object?> r)
    {
        var type = r.GetValueOrDefault("type")?.ToString() ?? "full";
        var size = Convert.ToInt64(r.GetValueOrDefault("size") ?? 0);
        var typeLabel = type switch
        {
            "full" => "Full Backup",
            "database" => "Database Only",
            "settings" => "Settings Only",
            _ => "Backup"
        };

        var createdStr = r.GetValueOrDefault("created_at")?.ToString() ?? "";
        if (DateTime.TryParse(createdStr, out var dt))
        {
            createdStr = dt.ToString("MMM dd, yyyy hh:mm tt");
        }

        return new Dictionary<string, object?>
        {
            ["id"] = Convert.ToInt64(r["id"]),
            ["filename"] = r.GetValueOrDefault("filename"),
            ["type"] = type,
            ["type_label"] = typeLabel,
            ["size"] = size,
            ["formatted_size"] = FormatBytes(size),
            ["path"] = r.GetValueOrDefault("path"),
            ["status"] = r.GetValueOrDefault("status") ?? "completed",
            ["is_automated"] = r.GetValueOrDefault("is_automated") is true or 1 or 1L,
            ["storage_destination"] = r.GetValueOrDefault("storage_destination") ?? "local",
            ["remote_path"] = r.GetValueOrDefault("remote_path"),
            ["remote_status"] = r.GetValueOrDefault("remote_status") ?? "skipped",
            ["remote_error"] = r.GetValueOrDefault("remote_error"),
            ["created_at"] = createdStr
        };
    }

    private async Task<string> GenerateUniqueBackupFilenameAsync(string type, string prefix = "backup")
    {
        var ts = DateTime.UtcNow.ToString("yyyy-MM-dd_HHmmss");
        var baseName = $"{prefix}_{type}_{ts}";
        var candidate = baseName;
        int counter = 1;

        while (await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM backup_logs WHERE filename = @f", new { f = candidate }) > 0)
        {
            candidate = $"{baseName}_{counter}";
            counter++;
        }

        return candidate;
    }

    private async Task<string> GenerateSqlDumpStringAsync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("-- Karobar-App ERP Native SQLite Dump");
        sb.AppendLine($"-- Generated at {DateTime.UtcNow:O}");
        sb.AppendLine("PRAGMA foreign_keys = OFF;");

        await using var conn = await _db.OpenConnectionAsync();
        var tables = await conn.QueryAsync<(string name, string sql)>(
            "SELECT name, sql FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name");

        foreach (var (name, createSql) in tables)
        {
            if (string.IsNullOrWhiteSpace(createSql)) continue;
            sb.AppendLine($"{createSql};");

            var escaped = name.Replace("\"", "\"\"");
            var rows = (await conn.QueryAsync($"SELECT * FROM \"{escaped}\"")).ToList();
            foreach (var r in rows)
            {
                if (r is not IDictionary<string, object?> dict || dict.Count == 0) continue;
                var cols = string.Join(", ", dict.Keys.Select(k => $"\"{k.Replace("\"", "\"\"")}\""));
                var vals = string.Join(", ", dict.Values.Select(v => v switch
                {
                    null => "NULL",
                    bool b => b ? "1" : "0",
                    int or long or double or float or decimal => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "0",
                    byte[] bytes => $"X'{Convert.ToHexString(bytes)}'",
                    _ => $"'{(v.ToString() ?? "").Replace("'", "''")}'"
                }));
                sb.AppendLine($"INSERT INTO \"{escaped}\" ({cols}) VALUES ({vals});");
            }
        }

        sb.AppendLine("PRAGMA foreign_keys = ON;");
        return sb.ToString();
    }

    private async Task<string?> RestoreFromSqlScriptAsync(string sqlContent)
    {
        if (string.IsNullOrWhiteSpace(sqlContent))
        {
            throw new InvalidOperationException("SQL backup file is empty.");
        }

        // Build into a fresh staging SQLite file first so a corrupt/partial SQL dump can NEVER destroy the live DB!
        var tmpDb = Path.Combine(Path.GetTempPath(), $"karobar_sql_restore_{Guid.NewGuid():N}.sqlite");
        try
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={tmpDb};Mode=ReadWriteCreate;Pooling=False;");
                await conn.OpenAsync();
                await conn.ExecuteAsync("PRAGMA foreign_keys = OFF; PRAGMA synchronous = OFF;");
                using var tx = conn.BeginTransaction();
                foreach (var stmt in SplitSqlStatements(sqlContent))
                {
                    var trimmed = stmt.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed) ||
                        trimmed.StartsWith("PRAGMA ", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith("COMMIT", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    await conn.ExecuteAsync(trimmed, transaction: tx);
                }
                tx.Commit();
                await conn.ExecuteAsync("PRAGMA foreign_keys = ON;");
            }
            catch (SqliteException ex)
            {
                throw new InvalidOperationException($"SQL backup script is invalid or corrupted: {ex.Message}", ex);
            }

            await ValidateCandidateSqliteDatabaseAsync(tmpDb);
            var snapPath = await CreatePreRestoreSafetySnapshotAsync();

            SqliteConnection.ClearAllPools();
            File.Copy(tmpDb, _db.SqlitePath, overwrite: true);
            try { if (File.Exists(_db.SqlitePath + "-wal")) File.Delete(_db.SqlitePath + "-wal"); } catch { }
            try { if (File.Exists(_db.SqlitePath + "-shm")) File.Delete(_db.SqlitePath + "-shm"); } catch { }
            return snapPath;
        }
        finally
        {
            try { if (File.Exists(tmpDb)) File.Delete(tmpDb); } catch { }
        }
    }

    private static IEnumerable<string> SplitSqlStatements(string sql)
    {
        var sb = new StringBuilder();
        var inSingle = false;
        var inDouble = false;
        var inLineComment = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (c == '\n') inLineComment = false;
                continue;
            }
            if (!inSingle && !inDouble && c == '-' && next == '-')
            {
                inLineComment = true;
                i++;
                continue;
            }
            if (c == '\'' && !inDouble)
            {
                sb.Append(c);
                if (inSingle && next == '\'') { sb.Append(next); i++; }
                else inSingle = !inSingle;
                continue;
            }
            if (c == '"' && !inSingle)
            {
                sb.Append(c);
                inDouble = !inDouble;
                continue;
            }
            if (c == ';' && !inSingle && !inDouble)
            {
                var s = sb.ToString().Trim();
                sb.Clear();
                if (!string.IsNullOrWhiteSpace(s)) yield return s;
                continue;
            }
            sb.Append(c);
        }

        var rem = sb.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(rem)) yield return rem;
    }
}

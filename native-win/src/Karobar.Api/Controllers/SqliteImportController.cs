using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Karobar.Api.Infrastructure;
using Karobar.Api.Middleware;
using Karobar.Background;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Karobar.Api.Controllers;

[Route("api/settings/sqlite-import")]
[Authorize]
public class SqliteImportController : ApiControllerBase
{
    private readonly WaseelaAndSqliteImportEngine? _injectedEngine;

    [ActivatorUtilitiesConstructor]
    public SqliteImportController(KarobarDb db, WaseelaAndSqliteImportEngine? importEngine = null) : base(db)
    {
        _injectedEngine = importEngine;
    }

    public SqliteImportController(KarobarDb db) : this(db, null)
    {
    }

    private WaseelaAndSqliteImportEngine GetEngine()
    {
        if (_injectedEngine != null)
        {
            try
            {
                if (string.Equals(
                        Path.GetFullPath(_injectedEngine.AppRoot),
                        Path.GetFullPath(Db.ProjectRoot),
                        StringComparison.OrdinalIgnoreCase))
                {
                    return _injectedEngine;
                }
            }
            catch
            {
                // Fallback to project-scoped engine
            }
        }

        return new WaseelaAndSqliteImportEngine(Db.ProjectRoot);
    }

    private async Task<IActionResult?> AuthorizeApplicationAdminAsync()
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(new { message = "Unauthenticated." });
        }

        if (!await IsAdminAsync())
        {
            return Forbidden("Access denied. Only an Application Administrator can view the SQLite Import lock gate.");
        }

        return null;
    }

    private async Task<bool> IsValidSoftwareAdminPasswordAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;

        var configuredHash = Environment.GetEnvironmentVariable("SQLITE_IMPORT_ADMIN_HASH") ?? "";
        if (!string.IsNullOrWhiteSpace(configuredHash))
        {
            return VerifyBcrypt(input, configuredHash) || VerifyBcrypt(input.Trim(), configuredHash);
        }

        if (CurrentUserId == null) return false;
        var user = await Db.QuerySingleOrDefaultAsync("SELECT password FROM users WHERE id = @uid", new { uid = CurrentUserId.Value });
        var pwdHash = user != null && user.TryGetValue("password", out var p) ? p?.ToString() ?? "" : "";
        if (string.IsNullOrWhiteSpace(pwdHash)) return false;

        return VerifyBcrypt(input, pwdHash) || VerifyBcrypt(input.Trim(), pwdHash);
    }

    private static bool VerifyBcrypt(string input, string hash)
    {
        try
        {
            if (BCrypt.Net.BCrypt.Verify(input, hash)) return true;
        }
        catch
        {
        }

        try
        {
            if (hash.StartsWith("$2y$"))
            {
                var altHash = "$2a$" + hash.Substring(4);
                if (BCrypt.Net.BCrypt.Verify(input, altHash)) return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static readonly string EphemeralImportSecret =
        "base64:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string GetAppSecret()
    {
        var key = Environment.GetEnvironmentVariable("APP_KEY");
        return !string.IsNullOrWhiteSpace(key) ? key : EphemeralImportSecret;
    }

    private static string GenerateSoftwareAdminToken(long userId, string? dateStr = null)
    {
        var date = dateStr ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
        var secret = Encoding.UTF8.GetBytes(GetAppSecret());
        var payload = Encoding.UTF8.GetBytes($"sqlite_import_sw_admin|{userId}|{date}");
        using var hmac = new HMACSHA256(secret);
        var sigBytes = hmac.ComputeHash(payload);
        var sig = Convert.ToHexString(sigBytes).ToLowerInvariant();
        return Convert.ToBase64String(Encoding.UTF8.GetBytes($"{userId}|{date}|{sig}"));
    }

    private static bool VerifySoftwareAdminToken(string? token, long userId)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        var todayToken = GenerateSoftwareAdminToken(userId, DateTime.UtcNow.ToString("yyyy-MM-dd"));
        var yesterdayToken = GenerateSoftwareAdminToken(userId, DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-dd"));

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(todayToken), Encoding.UTF8.GetBytes(token)) ||
               CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(yesterdayToken), Encoding.UTF8.GetBytes(token));
    }

    private async Task<IActionResult?> AuthorizeSqliteImportAsync(IDictionary<string, JsonElement>? body = null)
    {
        var adminErr = await AuthorizeApplicationAdminAsync();
        if (adminErr != null) return adminErr;

        var userId = CurrentUserId!.Value;

        var unlockToken = Request.Headers["X-Software-Admin-Token"].FirstOrDefault()
                          ?? Request.Headers["X-Sqlite-Import-Admin-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(unlockToken) && body != null)
        {
            unlockToken = GetString(body, "software_admin_token");
        }
        if (string.IsNullOrWhiteSpace(unlockToken) && Request.HasFormContentType)
        {
            try
            {
                var form = await Request.ReadFormAsync();
                unlockToken = form["software_admin_token"].FirstOrDefault();
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(unlockToken) && VerifySoftwareAdminToken(unlockToken.Trim(), userId))
        {
            return null;
        }

        var directPassword = Request.Headers["X-Software-Admin-Password"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(directPassword) && body != null)
        {
            directPassword = GetString(body, "software_admin_password");
        }
        if (string.IsNullOrWhiteSpace(directPassword) && Request.HasFormContentType)
        {
            try
            {
                var form = await Request.ReadFormAsync();
                directPassword = form["software_admin_password"].FirstOrDefault();
            }
            catch { }
        }

        if (!string.IsNullOrWhiteSpace(directPassword) && await IsValidSoftwareAdminPasswordAsync(directPassword))
        {
            return null;
        }

        return StatusCode(403, new Dictionary<string, object?>
        {
            ["message"] = "Software Admin authorization required. Please unlock with the Software Admin password.",
            ["software_admin_locked"] = true
        });
    }

    [HttpPost("unlock")]
    [EnableRateLimiting("throttle_10_1")]
    public async Task<IActionResult> Unlock()
    {
        var adminErr = await AuthorizeApplicationAdminAsync();
        if (adminErr != null) return adminErr;

        var body = await ReadJsonBodyAsync();
        var password = GetString(body, "password") ?? GetString(body, "software_admin_password") ?? "";

        if (!await IsValidSoftwareAdminPasswordAsync(password))
        {
            return StatusCode(422, new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["message"] = "Invalid Software Admin password. Access is restricted to Software Administrators.",
                ["errors"] = new Dictionary<string, string[]>
                {
                    ["password"] = new[] { "Invalid Software Admin password." }
                }
            });
        }

        var unlockToken = GenerateSoftwareAdminToken(CurrentUserId!.Value);
        return Ok(new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["unlock_token"] = unlockToken,
            ["message"] = "Software Admin access unlocked."
        });
    }

    [HttpPost("upload")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 1073741824, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> UploadAndInspect()
    {
        string? tempUploadPath = null;
        try
        {
            Dictionary<string, JsonElement>? body = null;
            if (!Request.HasFormContentType)
            {
                body = await ReadJsonBodyAsync();
            }

            var authErr = await AuthorizeSqliteImportAsync(body);
            if (authErr != null) return authErr;

            IFormFile? uploadedFile = null;
            string filePath = "";

            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                uploadedFile = form.Files.GetFile("database_file") ?? form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
                filePath = (form["file_path"].FirstOrDefault() ?? form["local_file_path"].FirstOrDefault() ?? "").Trim();
            }
            else if (body != null)
            {
                filePath = (GetString(body, "file_path") ?? GetString(body, "local_file_path") ?? "").Trim();
            }

            if (uploadedFile == null && string.IsNullOrEmpty(filePath))
            {
                return StatusCode(422, new Dictionary<string, object?>
                {
                    ["message"] = "Please provide a SQLite database, SQL dump, or Waseela CSV file.",
                    ["errors"] = new Dictionary<string, string[]>
                    {
                        ["database_file"] = new[] { "Please select a valid database or CSV file to upload." }
                    }
                });
            }

            var engine = GetEngine();
            Dictionary<string, object?> stagedMeta;

            if (uploadedFile != null)
            {
                var originalName = Path.GetFileName(uploadedFile.FileName);
                if (string.IsNullOrWhiteSpace(originalName)) originalName = "upload.sqlite";
                var ext = Path.GetExtension(originalName);
                var uploadStagingDir = Db.StoragePath("app/imports/sqlite");
                Directory.CreateDirectory(uploadStagingDir);
                tempUploadPath = Path.Combine(uploadStagingDir, $"raw-{Guid.NewGuid():N}{ext}");

                await using (var dstFs = new FileStream(tempUploadPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await uploadedFile.CopyToAsync(dstFs, HttpContext.RequestAborted);
                }

                stagedMeta = await engine.StageDatabaseFromFileAsync(tempUploadPath, originalName, HttpContext.RequestAborted);
            }
            else
            {
                // Restrict local_file_path server-disk reads strictly to local loopback requests.
                var remoteIp = HttpContext.Connection.RemoteIpAddress;
                if (remoteIp != null)
                {
                    if (remoteIp.IsIPv4MappedToIPv6) remoteIp = remoteIp.MapToIPv4();
                    if (!System.Net.IPAddress.IsLoopback(remoteIp))
                    {
                        return StatusCode(403, new Dictionary<string, object?>
                        {
                            ["message"] = "Local file path imports are restricted to the Store Server PC. Please upload the file instead."
                        });
                    }
                }

                if (filePath.StartsWith(@"\\", StringComparison.Ordinal) ||
                    filePath.StartsWith("//", StringComparison.Ordinal) ||
                    !Path.IsPathRooted(filePath))
                {
                    throw new InvalidOperationException("Only absolute local disk paths are permitted for direct database import.");
                }

                var fullSourcePath = Path.GetFullPath(filePath);
                var liveDbPath = Path.GetFullPath(Db.SqlitePath);
                if (string.Equals(fullSourcePath, liveDbPath, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Cannot import directly from the currently active live database file.");
                }

                var srcExt = Path.GetExtension(fullSourcePath).ToLowerInvariant();
                if (srcExt is not (".sqlite" or ".sqlite3" or ".db" or ".db3" or ".zip" or ".gz" or ".sql" or ".csv"))
                {
                    throw new InvalidOperationException("Unsupported local file extension. Select a .sqlite, .sqlite3, .db, .db3, .zip, .gz, .sql, or .csv file.");
                }

                if (!System.IO.File.Exists(fullSourcePath))
                {
                    throw new InvalidOperationException("Selected database file does not exist or is not readable.");
                }

                stagedMeta = await engine.StageDatabaseFromFileAsync(
                    fullSourcePath,
                    Path.GetFileName(fullSourcePath),
                    HttpContext.RequestAborted);
            }

            var token = stagedMeta["token"]?.ToString() ?? "";
            var inspection = await engine.InspectAndProposeMappingAsync(token, Db.SqlitePath, HttpContext.RequestAborted);
            return Ok(inspection);
        }
        catch (Exception ex)
        {
            return StatusCode(422, new Dictionary<string, object?>
            {
                ["message"] = ex.Message,
                ["error"] = ex.Message
            });
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempUploadPath))
            {
                try { if (System.IO.File.Exists(tempUploadPath)) System.IO.File.Delete(tempUploadPath); } catch { }
            }
        }
    }

    [HttpPost("preview-table")]
    public async Task<IActionResult> PreviewTable()
    {
        var body = await ReadJsonBodyAsync();
        var authErr = await AuthorizeSqliteImportAsync(body);
        if (authErr != null) return authErr;

        var token = (GetString(body, "token") ?? GetString(body, "session_token") ?? "").Trim();
        var sourceTable = (GetString(body, "source_table") ?? "").Trim();
        var targetTable = (GetString(body, "target_table") ?? "").Trim();

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(sourceTable) || string.IsNullOrEmpty(targetTable))
        {
            return StatusCode(422, new Dictionary<string, object?>
            {
                ["message"] = "Session token, source_table, and target_table are required."
            });
        }

        if (WaseelaAndSqliteImportEngine.ProtectedTables.Contains(sourceTable, StringComparer.OrdinalIgnoreCase) ||
            WaseelaAndSqliteImportEngine.ProtectedTables.Contains(targetTable, StringComparer.OrdinalIgnoreCase))
        {
            return StatusCode(403, new Dictionary<string, object?>
            {
                ["message"] = $"Table [{targetTable}] is protected and cannot be previewed or imported."
            });
        }

        try
        {
            var engine = GetEngine();
            var columnMap = NormalizeColumnMapFromJson(
                body.TryGetValue("column_map", out var cmEl) ? cmEl : null,
                body.TryGetValue("custom_columns", out var ccEl) ? ccEl : null,
                body.TryGetValue("custom_defaults", out var cdEl) ? cdEl : null);

            var preview = await engine.PreviewTableMappingAsync(
                token,
                sourceTable,
                targetTable,
                Db.SqlitePath,
                columnMap,
                HttpContext.RequestAborted);

            return Ok(preview);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(403, new Dictionary<string, object?>
            {
                ["message"] = ex.Message,
                ["error"] = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(422, new Dictionary<string, object?>
            {
                ["message"] = ex.Message,
                ["error"] = ex.Message
            });
        }
    }

    [HttpPost("commit")]
    [EnableRateLimiting("throttle_10_1")]
    public async Task<IActionResult> Commit()
    {
        var body = await ReadJsonBodyAsync();
        var authErr = await AuthorizeSqliteImportAsync(body);
        if (authErr != null) return authErr;

        var token = (GetString(body, "token") ?? GetString(body, "session_token") ?? "").Trim();
        var password = GetString(body, "password") ?? "";
        var mode = (GetString(body, "mode", "merge") ?? "merge").Trim().ToLowerInvariant();
        var conflictPolicy = (GetString(body, "conflict_policy", "upsert") ?? "upsert").Trim().ToLowerInvariant();
        var createBackup = true;
        var reconcileBatches = true;

        JsonElement? optEl = body.TryGetValue("options", out var rawOpt) && rawOpt.ValueKind == JsonValueKind.Object
            ? rawOpt
            : null;

        if (optEl.HasValue)
        {
            if (optEl.Value.TryGetProperty("mode", out var mEl) && mEl.ValueKind == JsonValueKind.String)
                mode = (mEl.GetString() ?? mode).Trim().ToLowerInvariant();
            if (optEl.Value.TryGetProperty("conflict_policy", out var cpEl) && cpEl.ValueKind == JsonValueKind.String)
                conflictPolicy = (cpEl.GetString() ?? conflictPolicy).Trim().ToLowerInvariant();
            if (optEl.Value.TryGetProperty("create_backup", out var cbEl) && cbEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                createBackup = cbEl.GetBoolean();
            if (optEl.Value.TryGetProperty("reconcile_batches", out var rbEl) && rbEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                reconcileBatches = rbEl.GetBoolean();
        }

        if (string.IsNullOrEmpty(token))
        {
            return StatusCode(422, new Dictionary<string, object?>
            {
                ["message"] = "Valid import session token is required."
            });
        }

        if (mode == "replace")
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return StatusCode(422, new Dictionary<string, object?>
                {
                    ["message"] = "Please enter your current account password to confirm Replace mode import.",
                    ["errors"] = new Dictionary<string, string[]>
                    {
                        ["password"] = new[] { "Please enter your current account password." }
                    }
                });
            }

            var userRow = CurrentUserId.HasValue
                ? await Db.QuerySingleOrDefaultAsync("SELECT password FROM users WHERE id = @id", new { id = CurrentUserId.Value })
                : null;
            var hash = userRow?.GetValueOrDefault("password")?.ToString() ?? "";
            if (string.IsNullOrEmpty(hash) || !SanctumAuthenticationHandler.VerifyLaravelBcryptPassword(password, hash))
            {
                return StatusCode(422, new Dictionary<string, object?>
                {
                    ["message"] = "Invalid account password. Replace mode import aborted.",
                    ["errors"] = new Dictionary<string, string[]>
                    {
                        ["password"] = new[] { "The provided password is incorrect." }
                    }
                });
            }
        }

        try
        {
            var engine = GetEngine();
            // Validate session exists before creating backup
            _ = engine.ResolveStagedPath(token);

            string? safetyBackupName = null;
            if (createBackup && System.IO.File.Exists(Db.SqlitePath))
            {
                string? snapTarget = null;
                try
                {
                    var backupDir = Db.StoragePath("app/backups");
                    Directory.CreateDirectory(backupDir);
                    safetyBackupName = $"pre-sqlite-import-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sqlite";
                    snapTarget = Path.Combine(backupDir, safetyBackupName);

                    using (var snapSrcConn = new SqliteConnection($"Data Source={Db.SqlitePath};Mode=ReadOnly;Pooling=False;"))
                    using (var snapDstConn = new SqliteConnection($"Data Source={snapTarget};Mode=ReadWriteCreate;Pooling=False;"))
                    {
                        await snapSrcConn.OpenAsync();
                        await snapDstConn.OpenAsync();
                        snapSrcConn.BackupDatabase(snapDstConn);
                    }

                    var snapInfo = new FileInfo(snapTarget);
                    if (!snapInfo.Exists || snapInfo.Length == 0)
                    {
                        throw new InvalidOperationException("Safety backup file was missing or empty after backup.");
                    }
                }
                catch (Exception bkEx)
                {
                    if (!string.IsNullOrEmpty(snapTarget))
                    {
                        try { if (System.IO.File.Exists(snapTarget)) System.IO.File.Delete(snapTarget); } catch { }
                    }
                    throw new InvalidOperationException($"Pre-import safety backup failed: {bkEx.Message}. Import aborted to protect your database.", bkEx);
                }
            }

            try
            {
                var factory = new Karobar.Data.DbConnectionFactory(explicitSqlitePath: Db.SqlitePath);
                await new Karobar.Data.DatabaseBootstrapper(factory).EnsureSchemaReadyAsync();
                KarobarDb.FlushSchemaCache();
            }
            catch
            {
                // Continue with existing schema
            }

            var execOptions = new SqliteImportExecutionOptions
            {
                Mode = mode == "replace" ? "replace" : "merge",
                ConflictPolicy = conflictPolicy == "skip" ? "skip" : "upsert",
                ReconcileBatches = reconcileBatches,
                CurrentUserId = CurrentUserId ?? 1
            };

            if (optEl.HasValue &&
                optEl.Value.TryGetProperty("tables", out var tablesEl) &&
                tablesEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in tablesEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                    var isEn = !prop.Value.TryGetProperty("enabled", out var enEl) || enEl.ValueKind != JsonValueKind.False;
                    var mappedTarget = prop.Value.TryGetProperty("target_table", out var ttEl) && ttEl.ValueKind == JsonValueKind.String
                        ? (ttEl.GetString() ?? "").Trim()
                        : null;

                    var colMap = NormalizeColumnMapFromJson(
                        prop.Value.TryGetProperty("column_map", out var tCm) ? tCm : null,
                        prop.Value.TryGetProperty("columns", out var tCols) ? tCols : null,
                        prop.Value.TryGetProperty("defaults", out var tDefs) ? tDefs : null);

                    execOptions.Tables[prop.Name] = new TableImportOverride
                    {
                        Enabled = isEn,
                        TargetTable = string.IsNullOrWhiteSpace(mappedTarget) ? null : mappedTarget,
                        ColumnMap = colMap
                    };
                }
            }

            var execResult = await engine.ExecuteImportCoreAsync(
                Db.SqlitePath,
                token,
                execOptions,
                HttpContext.RequestAborted);

            engine.CleanupStagedSession(token);
            KarobarDb.FlushSchemaCache();
            SyncPulseService.Bump("stock", "drafts", "drawer", "settings");

            var totalInserted = Convert.ToInt32(execResult["total_inserted"] ?? 0);
            var totalUpdated = Convert.ToInt32(execResult["total_updated"] ?? 0);
            var totalSkipped = Convert.ToInt32(execResult["total_skipped"] ?? 0);

            return Ok(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["message"] = "SQLite import completed successfully.",
                ["mode"] = execResult["mode"],
                ["safety_backup"] = safetyBackupName,
                ["tables"] = execResult["tables"],
                ["totals"] = new Dictionary<string, int>
                {
                    ["inserted"] = totalInserted,
                    ["updated"] = totalUpdated,
                    ["skipped"] = totalSkipped
                },
                ["stats"] = new Dictionary<string, object?>
                {
                    ["total_inserted"] = totalInserted,
                    ["total_updated"] = totalUpdated,
                    ["total_skipped"] = totalSkipped,
                    ["safety_backup"] = safetyBackupName,
                    ["reconciliation"] = execResult["reconciliation"],
                    ["tables"] = execResult["tables_by_source"]
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(422, new Dictionary<string, object?>
            {
                ["message"] = ex.Message,
                ["error"] = ex.Message
            });
        }
    }

    [HttpDelete("{token}")]
    public async Task<IActionResult> Cancel(string token)
    {
        var authErr = await AuthorizeSqliteImportAsync();
        if (authErr != null) return authErr;

        GetEngine().CleanupStagedSession(token);

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = "Import session cancelled and temporary files removed."
        });
    }

    private static Dictionary<string, ColumnMappingOverride> NormalizeColumnMapFromJson(
        JsonElement? explicitColumnMap,
        JsonElement? customColumns,
        JsonElement? customDefaults)
    {
        var map = new Dictionary<string, ColumnMappingOverride>(StringComparer.OrdinalIgnoreCase);

        if (explicitColumnMap is { ValueKind: JsonValueKind.Object } cm)
        {
            foreach (var prop in cm.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Object)
                {
                    var src = prop.Value.TryGetProperty("source_column", out var scEl) && scEl.ValueKind == JsonValueKind.String
                        ? scEl.GetString()
                        : null;
                    var def = prop.Value.TryGetProperty("default_value", out var dvEl) && dvEl.ValueKind != JsonValueKind.Null && dvEl.ValueKind != JsonValueKind.Undefined
                        ? dvEl.ToString()
                        : null;
                    map[prop.Name] = new ColumnMappingOverride
                    {
                        SourceColumn = string.IsNullOrWhiteSpace(src) ? "__skip__" : src.Trim(),
                        DefaultValue = def
                    };
                }
                else if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    var s = prop.Value.GetString();
                    map[prop.Name] = new ColumnMappingOverride
                    {
                        SourceColumn = string.IsNullOrWhiteSpace(s) ? "__skip__" : s.Trim()
                    };
                }
            }
        }

        if (customColumns is { ValueKind: JsonValueKind.Object } cc)
        {
            foreach (var prop in cc.EnumerateObject())
            {
                var src = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
                if (!map.TryGetValue(prop.Name, out var entry))
                {
                    entry = new ColumnMappingOverride();
                    map[prop.Name] = entry;
                }
                entry.SourceColumn = string.IsNullOrWhiteSpace(src) ? "__skip__" : src.Trim();
            }
        }

        if (customDefaults is { ValueKind: JsonValueKind.Object } cd)
        {
            foreach (var prop in cd.EnumerateObject())
            {
                if (prop.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) continue;
                var valStr = prop.Value.ToString();
                if (!map.TryGetValue(prop.Name, out var entry))
                {
                    entry = new ColumnMappingOverride { SourceColumn = "__skip__" };
                    map[prop.Name] = entry;
                }
                entry.DefaultValue = valStr;
            }
        }

        return map;
    }
}

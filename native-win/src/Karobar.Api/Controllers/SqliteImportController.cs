using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace Karobar.Api.Controllers;

[Route("api/settings/sqlite-import")]
public class SqliteImportController : ApiControllerBase
{
    private static readonly HashSet<string> ProtectedTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "users", "licenses", "roles", "permissions", "model_has_roles", "model_has_permissions",
        "role_has_permissions", "sessions", "personal_access_tokens", "password_reset_tokens",
        "migrations", "cache", "cache_locks", "jobs", "job_batches", "failed_jobs",
        "backup_logs", "backup_schedules", "cube_provider_usages"
    };

    private static readonly HashSet<string> OptionalConfigTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "settings", "theme_settings", "barcode_templates"
    };

    private static readonly Dictionary<string, string> TableAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["item"] = "products",
        ["items"] = "products",
        ["medicine"] = "products",
        ["medicines"] = "products",
        ["drug"] = "products",
        ["drugs"] = "products",
        ["inventory"] = "products",
        ["product"] = "products",
        ["category"] = "categories",
        ["brand"] = "brands",
        ["company"] = "brands",
        ["companies"] = "brands",
        ["generic"] = "formulations",
        ["generics"] = "formulations",
        ["salt"] = "formulations",
        ["salts"] = "formulations",
        ["formulation"] = "formulations",
        ["supplier"] = "suppliers",
        ["vendor"] = "suppliers",
        ["vendors"] = "suppliers",
        ["customer"] = "customers",
        ["client"] = "customers",
        ["clients"] = "customers",
        ["batch"] = "batches",
        ["sale"] = "sale_invoices",
        ["sales"] = "sale_invoices",
        ["sale_invoice"] = "sale_invoices",
        ["sale_items"] = "sale_invoice_items",
        ["purchase"] = "purchase_invoices",
        ["purchases"] = "purchase_invoices",
        ["purchase_invoice"] = "purchase_invoices",
        ["purchase_items"] = "purchase_invoice_items",
        ["sale_return"] = "sale_returns",
        ["purchase_return"] = "purchase_returns",
        ["customer_ledger"] = "customer_ledgers",
        ["supplier_ledger"] = "supplier_ledgers",
        ["employee"] = "employees",
        ["expense"] = "expenses",
        ["expense_category"] = "expense_categories"
    };

    public SqliteImportController(KarobarDb db) : base(db)
    {
    }

    private string GetStagingDir()
    {
        var dir = Db.StoragePath("app/imports/sqlite");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        return dir;
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

    private static string GetAppSecret()
    {
        var key = Environment.GetEnvironmentVariable("APP_KEY");
        return !string.IsNullOrWhiteSpace(key) ? key : "karobar-native-win-default-app-key-secret";
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

        var unlockToken = Request.Headers["X-Software-Admin-Token"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(unlockToken) && body != null)
        {
            unlockToken = GetString(body, "software_admin_token");
        }
        if (string.IsNullOrWhiteSpace(unlockToken) && Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            unlockToken = form["software_admin_token"].FirstOrDefault();
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
            var form = await Request.ReadFormAsync();
            directPassword = form["software_admin_password"].FirstOrDefault();
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
    public async Task<IActionResult> UploadAndInspect()
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
            uploadedFile = form.Files.GetFile("file") ?? form.Files.GetFile("database_file") ?? form.Files.FirstOrDefault();
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
                ["message"] = "Please provide a SQLite database file or local file path.",
                ["errors"] = new Dictionary<string, string[]>
                {
                    ["database_file"] = new[] { "Please select a valid SQLite database file to upload." }
                }
            });
        }

        try
        {
            string token = Guid.NewGuid().ToString();
            string stagingDir = GetStagingDir();
            string stagedSqlitePath = Path.Combine(stagingDir, $"{token}.sqlite");
            string metaPath = Path.Combine(stagingDir, $"{token}.json");
            string originalName;

            byte[] rawBytes;
            if (uploadedFile != null)
            {
                originalName = uploadedFile.FileName;
                using var ms = new MemoryStream();
                await uploadedFile.CopyToAsync(ms);
                rawBytes = ms.ToArray();
            }
            else
            {
                if (!System.IO.File.Exists(filePath))
                {
                    throw new InvalidOperationException("Selected database file does not exist or is not readable.");
                }
                originalName = Path.GetFileName(filePath);
                rawBytes = await System.IO.File.ReadAllBytesAsync(filePath);
            }

            if (rawBytes.Length == 0)
            {
                throw new InvalidOperationException("The selected database file is empty or unreadable.");
            }

            // Decompress GZip if needed
            if (rawBytes.Length >= 2 && rawBytes[0] == 0x1F && rawBytes[1] == 0x8B)
            {
                using var inStream = new MemoryStream(rawBytes);
                using var gz = new GZipStream(inStream, CompressionMode.Decompress);
                using var outStream = new MemoryStream();
                await gz.CopyToAsync(outStream);
                rawBytes = outStream.ToArray();
            }

            var headerStr = Encoding.ASCII.GetString(rawBytes, 0, Math.Min(rawBytes.Length, 64));
            var magicIdx = headerStr.IndexOf("SQLite format 3", StringComparison.Ordinal);
            if (magicIdx < 0)
            {
                throw new InvalidOperationException("Unsupported file format. Please select a valid SQLite database (.sqlite, .sqlite3, .db).");
            }

            await System.IO.File.WriteAllBytesAsync(stagedSqlitePath, rawBytes);

            var meta = new Dictionary<string, object?>
            {
                ["token"] = token,
                ["original_name"] = originalName,
                ["file_size"] = rawBytes.Length,
                ["created_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz")
            };
            await System.IO.File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta));

            var inspection = await InspectStagedSqliteAsync(token, stagedSqlitePath, meta);
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

        try
        {
            var stagedPath = ResolveStagedPath(token);
            var targetCols = await GetTableColumnsInfoAsync(Db.SqlitePath, targetTable);
            if (targetCols.Count == 0)
            {
                throw new InvalidOperationException($"Target table [{targetTable}] is not an importable table.");
            }

            var srcCols = await GetTableColumnsInfoAsync(stagedPath, sourceTable);
            if (srcCols.Count == 0)
            {
                throw new InvalidOperationException($"Source table [{sourceTable}] was not found in the staged SQLite file.");
            }

            var sampleRows = await GetSampleRowsAsync(stagedPath, sourceTable);
            var analysis = BuildColumnAnalysis(sourceTable, srcCols, targetTable, targetCols, sampleRows);

            return Ok(new Dictionary<string, object?>
            {
                ["analysis"] = analysis,
                ["source_table"] = sourceTable,
                ["target_table"] = targetTable,
                ["column_mappings"] = analysis["column_mappings"],
                ["unmapped_source_columns"] = analysis["unmapped_source_columns"],
                ["sample_rows"] = sampleRows,
                ["preview_rows"] = sampleRows,
                ["stats"] = new Dictionary<string, object?>
                {
                    ["matched_columns_count"] = analysis["matched_columns_count"],
                    ["total_target_columns"] = analysis["total_target_columns"]
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

    [HttpPost("commit")]
    public async Task<IActionResult> Commit()
    {
        var body = await ReadJsonBodyAsync();
        var authErr = await AuthorizeSqliteImportAsync(body);
        if (authErr != null) return authErr;

        var token = (GetString(body, "token") ?? GetString(body, "session_token") ?? "").Trim();
        var password = GetString(body, "password") ?? "";
        var mode = GetString(body, "mode", "merge") ?? "merge";

        if (body.TryGetValue("options", out var optEl) && optEl.ValueKind == JsonValueKind.Object)
        {
            if (optEl.TryGetProperty("mode", out var mEl) && mEl.ValueKind == JsonValueKind.String)
            {
                mode = mEl.GetString() ?? mode;
            }
        }

        if (string.IsNullOrEmpty(token))
        {
            return StatusCode(422, new Dictionary<string, object?>
            {
                ["message"] = "Valid import session token is required."
            });
        }

        if (mode == "replace" && password.Trim().Length < 4)
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

        try
        {
            var stagedPath = ResolveStagedPath(token);
            var tableReports = new List<Dictionary<string, object?>>();
            var tablesBySource = new Dictionary<string, object?>();
            int totalInserted = 0;

            await using var srcConn = new SqliteConnection($"Data Source={stagedPath};Mode=ReadOnly");
            await srcConn.OpenAsync();

            var srcTables = (await srcConn.QueryAsync<string>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")).ToList();

            foreach (var srcTbl in srcTables)
            {
                if (ProtectedTables.Contains(srcTbl) || OptionalConfigTables.Contains(srcTbl))
                {
                    continue;
                }

                var targetTbl = TableAliases.TryGetValue(srcTbl, out var aliased) ? aliased : srcTbl.ToLowerInvariant();
                if (!await Db.HasTableAsync(targetTbl))
                {
                    continue;
                }

                var escapedSrc = srcTbl.Replace("\"", "\"\"");
                var rows = (await srcConn.QueryAsync($"SELECT * FROM \"{escapedSrc}\"")).Select(KarobarDb.NormalizeRow).ToList();
                int insertedForTable = 0;
                int skippedForTable = 0;

                foreach (var row in rows)
                {
                    try
                    {
                        var copy = new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase);
                        copy.Remove("id");
                        await Db.InsertAsync(targetTbl, copy);
                        insertedForTable++;
                        totalInserted++;
                    }
                    catch
                    {
                        skippedForTable++;
                    }
                }

                var report = new Dictionary<string, object?>
                {
                    ["source_table"] = srcTbl,
                    ["target_table"] = targetTbl,
                    ["source_rows"] = rows.Count,
                    ["inserted"] = insertedForTable,
                    ["updated"] = 0,
                    ["skipped"] = skippedForTable,
                    ["warnings"] = Array.Empty<string>()
                };
                tableReports.Add(report);
                tablesBySource[srcTbl] = report;
            }

            return Ok(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["message"] = "SQLite import completed successfully.",
                ["tables"] = tableReports,
                ["totals"] = new Dictionary<string, int>
                {
                    ["inserted"] = totalInserted,
                    ["updated"] = 0,
                    ["skipped"] = 0
                },
                ["stats"] = new Dictionary<string, object?>
                {
                    ["total_inserted"] = totalInserted,
                    ["total_updated"] = 0,
                    ["total_skipped"] = 0,
                    ["safety_backup"] = null,
                    ["reconciliation"] = new Dictionary<string, object?>(),
                    ["tables"] = tablesBySource
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

        CleanupStagedSession(token);

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = "Import session cancelled and temporary files removed."
        });
    }

    private string ResolveStagedPath(string token)
    {
        if (!Regex.IsMatch(token, @"^[a-f0-9\-]{36}$", RegexOptions.IgnoreCase))
        {
            throw new InvalidOperationException("Invalid import session token.");
        }

        var path = Path.Combine(GetStagingDir(), $"{token}.sqlite");
        if (!System.IO.File.Exists(path))
        {
            throw new InvalidOperationException("Import session has expired or file was not found. Please upload the SQLite file again.");
        }

        return path;
    }

    private void CleanupStagedSession(string token)
    {
        if (!Regex.IsMatch(token, @"^[a-f0-9\-]{36}$", RegexOptions.IgnoreCase))
        {
            return;
        }

        var dir = GetStagingDir();
        var sqlitePath = Path.Combine(dir, $"{token}.sqlite");
        var metaPath = Path.Combine(dir, $"{token}.json");

        try { if (System.IO.File.Exists(sqlitePath)) System.IO.File.Delete(sqlitePath); } catch { }
        try { if (System.IO.File.Exists(metaPath)) System.IO.File.Delete(metaPath); } catch { }
    }

    private async Task<Dictionary<string, object?>> InspectStagedSqliteAsync(string token, string stagedSqlitePath, Dictionary<string, object?> meta)
    {
        var targetCatalog = await BuildTargetCatalogAsync();
        var importableTables = new List<Dictionary<string, object?>>();
        var protectedSkipped = new List<Dictionary<string, object?>>();
        int totalImportableRows = 0;
        int autoMatchedCount = 0;

        await using var srcConn = new SqliteConnection($"Data Source={stagedSqlitePath};Mode=ReadOnly");
        await srcConn.OpenAsync();

        var rawTables = (await srcConn.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")).ToList();

        if (rawTables.Count == 0)
        {
            throw new InvalidOperationException("The selected SQLite database contains no tables.");
        }

        foreach (var srcTable in rawTables)
        {
            var escaped = srcTable.Replace("\"", "\"\"");
            var rowCount = await srcConn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM \"{escaped}\"");
            var srcCols = await GetTableColumnsInfoAsync(stagedSqlitePath, srcTable);

            if (ProtectedTables.Contains(srcTable))
            {
                protectedSkipped.Add(new Dictionary<string, object?>
                {
                    ["source_table"] = srcTable,
                    ["row_count"] = rowCount,
                    ["reason"] = "Protected security/system table"
                });
                continue;
            }

            var sampleRows = await GetSampleRowsAsync(stagedSqlitePath, srcTable);
            string? matchedTarget = null;
            string matchMethod = "unmapped";

            if (targetCatalog.ContainsKey(srcTable))
            {
                matchedTarget = srcTable.ToLowerInvariant();
                matchMethod = "exact";
            }
            else if (TableAliases.TryGetValue(srcTable, out var aliased) && targetCatalog.ContainsKey(aliased))
            {
                matchedTarget = aliased;
                matchMethod = "alias";
            }

            Dictionary<string, object?>? analysis = null;
            bool isOptional = matchedTarget != null && OptionalConfigTables.Contains(matchedTarget);

            if (matchedTarget != null && targetCatalog.TryGetValue(matchedTarget, out var targetInfo))
            {
                autoMatchedCount++;
                var targetCols = (List<Dictionary<string, object?>>)targetInfo["columns"]!;
                analysis = BuildColumnAnalysis(srcTable, srcCols, matchedTarget, targetCols, sampleRows);
            }

            bool enabled = matchedTarget != null && rowCount > 0 && !isOptional;
            if (enabled) totalImportableRows += rowCount;

            var srcColNames = srcCols.Select(c => c["name"]?.ToString() ?? "").ToList();

            importableTables.Add(new Dictionary<string, object?>
            {
                ["source_table"] = srcTable,
                ["row_count"] = rowCount,
                ["source_columns"] = srcColNames,
                ["source_columns_meta"] = srcCols,
                ["target_table"] = matchedTarget,
                ["match_method"] = matchMethod,
                ["match_type"] = matchMethod,
                ["confidence"] = matchMethod == "exact" ? 100 : (matchMethod == "alias" ? 95 : 80),
                ["enabled"] = enabled,
                ["is_optional_cfg"] = isOptional,
                ["dependency_stage"] = 1,
                ["stage"] = 1,
                ["analysis"] = analysis,
                ["column_mappings"] = analysis != null ? analysis["column_mappings"] : Array.Empty<object>(),
                ["unmapped_source_columns"] = analysis != null ? analysis["unmapped_source_columns"] : srcColNames,
                ["sample_rows"] = sampleRows
            });
        }

        var summary = new Dictionary<string, object?>
        {
            ["total_source_tables"] = importableTables.Count + protectedSkipped.Count,
            ["importable_tables_count"] = importableTables.Count,
            ["non_empty_tables_count"] = importableTables.Count(t => Convert.ToInt32(t["row_count"]) > 0),
            ["auto_matched_count"] = autoMatchedCount,
            ["matched_tables_count"] = autoMatchedCount,
            ["schema_drift_count"] = 0,
            ["protected_skipped_count"] = protectedSkipped.Count,
            ["total_importable_rows"] = totalImportableRows,
            ["total_rows_to_import"] = totalImportableRows
        };

        return new Dictionary<string, object?>
        {
            ["message"] = "SQLite database analyzed successfully.",
            ["session"] = meta,
            ["session_token"] = token,
            ["file_name"] = meta["original_name"],
            ["summary"] = summary,
            ["protected_tables"] = protectedSkipped,
            ["tables"] = importableTables,
            ["importable_tables"] = importableTables,
            ["target_catalog"] = targetCatalog.Values.ToList(),
            ["available_target_tables"] = targetCatalog.Values.ToList(),
            ["user_remap_preview"] = Array.Empty<object>(),
            ["user_remapping"] = new Dictionary<string, object?>
            {
                ["source_users_count"] = 0,
                ["preview"] = Array.Empty<object>()
            }
        };
    }

    private async Task<Dictionary<string, Dictionary<string, object?>>> BuildTargetCatalogAsync()
    {
        var catalog = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
        var rows = await Db.QueryListAsync("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name");

        foreach (var r in rows)
        {
            var tableName = r["name"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(tableName) || ProtectedTables.Contains(tableName))
            {
                continue;
            }

            var cols = await GetTableColumnsInfoAsync(Db.SqlitePath, tableName);
            catalog[tableName] = new Dictionary<string, object?>
            {
                ["table"] = tableName,
                ["label"] = tableName,
                ["stage"] = 1,
                ["is_optional_cfg"] = OptionalConfigTables.Contains(tableName),
                ["columns"] = cols
            };
        }

        return catalog;
    }

    private static async Task<List<Dictionary<string, object?>>> GetTableColumnsInfoAsync(string sqlitePath, string tableName)
    {
        var list = new List<Dictionary<string, object?>>();
        await using var conn = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly");
        await conn.OpenAsync();
        var escaped = tableName.Replace("\"", "\"\"");
        var rows = await conn.QueryAsync($"PRAGMA table_info(\"{escaped}\")");
        foreach (var r in rows)
        {
            var d = (IDictionary<string, object>)r;
            list.Add(new Dictionary<string, object?>
            {
                ["name"] = d["name"]?.ToString() ?? "",
                ["type"] = d.TryGetValue("type", out var t) && t != null ? t.ToString() : "TEXT",
                ["notnull"] = d.TryGetValue("notnull", out var nn) && Convert.ToInt32(nn) != 0,
                ["dflt_value"] = d.TryGetValue("dflt_value", out var dv) ? dv : null,
                ["pk"] = d.TryGetValue("pk", out var pk) && Convert.ToInt32(pk) != 0
            });
        }
        return list;
    }

    private static async Task<List<Dictionary<string, object?>>> GetSampleRowsAsync(string sqlitePath, string tableName)
    {
        await using var conn = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly");
        await conn.OpenAsync();
        var escaped = tableName.Replace("\"", "\"\"");
        var rows = await conn.QueryAsync($"SELECT * FROM \"{escaped}\" LIMIT 3");
        return rows.Select(KarobarDb.NormalizeRow).ToList();
    }

    private static Dictionary<string, object?> BuildColumnAnalysis(
        string sourceTable,
        List<Dictionary<string, object?>> srcCols,
        string targetTable,
        List<Dictionary<string, object?>> targetCols,
        List<Dictionary<string, object?>> sampleRows)
    {
        var srcColLookup = new HashSet<string>(srcCols.Select(c => c["name"]?.ToString() ?? ""), StringComparer.OrdinalIgnoreCase);
        var usedSrcCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mappings = new List<Dictionary<string, object?>>();
        int matchedCount = 0;

        foreach (var tCol in targetCols)
        {
            var tName = tCol["name"]?.ToString() ?? "";
            string? matchedSrc = srcColLookup.Contains(tName) ? tName : null;
            string matchType = matchedSrc != null ? "exact" : "default";

            if (matchedSrc != null)
            {
                usedSrcCols.Add(matchedSrc);
                matchedCount++;
            }

            mappings.Add(new Dictionary<string, object?>
            {
                ["target_column"] = tName,
                ["target_type"] = tCol["type"],
                ["source_column"] = matchedSrc,
                ["match_type"] = matchType,
                ["strategy"] = matchType,
                ["fallback_description"] = matchedSrc != null ? "" : "Auto-synthesized default"
            });
        }

        var unmappedSrc = srcColLookup.Where(c => !usedSrcCols.Contains(c)).ToList();

        return new Dictionary<string, object?>
        {
            ["source_table"] = sourceTable,
            ["target_table"] = targetTable,
            ["column_mappings"] = mappings,
            ["unmapped_source_columns"] = unmappedSrc,
            ["preview_rows"] = sampleRows,
            ["matched_columns_count"] = matchedCount,
            ["total_target_columns"] = targetCols.Count
        };
    }
}

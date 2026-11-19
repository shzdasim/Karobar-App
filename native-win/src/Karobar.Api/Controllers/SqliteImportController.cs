using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Api.Middleware;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;

namespace Karobar.Api.Controllers;

[Route("api/settings/sqlite-import")]
[Authorize]
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
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 1073741824, ValueLengthLimit = int.MaxValue)]
    public async Task<IActionResult> UploadAndInspect()
    {
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
                    ["message"] = "Please provide a SQLite database file or local file path.",
                    ["errors"] = new Dictionary<string, string[]>
                    {
                        ["database_file"] = new[] { "Please select a valid SQLite database file to upload." }
                    }
                });
            }

            string token = Guid.NewGuid().ToString();
            string stagingDir = GetStagingDir();
            string stagedSqlitePath = Path.Combine(stagingDir, $"{token}.sqlite");
            string metaPath = Path.Combine(stagingDir, $"{token}.json");
            string originalName;

            if (uploadedFile != null)
            {
                originalName = uploadedFile.FileName;
                await using var dstFs = new FileStream(stagedSqlitePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                await uploadedFile.CopyToAsync(dstFs);
            }
            else
            {
                if (!System.IO.File.Exists(filePath))
                {
                    throw new InvalidOperationException("Selected database file does not exist or is not readable.");
                }
                originalName = Path.GetFileName(filePath);
                await using (var srcFs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true))
                await using (var dstFs = new FileStream(stagedSqlitePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await srcFs.CopyToAsync(dstFs);
                }

                // CRITICAL WAL-MODE SUPPORT: If a sibling `-wal` (and optional `-shm`) exists beside the selected
                // SQLite file (e.g. from a running Electron Karobar-App), copy them alongside the staged copy and
                // checkpoint immediately so 100% of uncheckpointed Sale Invoices / recent rows are merged in!
                var srcWal = filePath + "-wal";
                if (System.IO.File.Exists(srcWal))
                {
                    try
                    {
                        await using var walIn = new FileStream(srcWal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true);
                        await using var walOut = new FileStream(stagedSqlitePath + "-wal", FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                        await walIn.CopyToAsync(walOut);
                    }
                    catch { }
                }
                var srcShm = filePath + "-shm";
                if (System.IO.File.Exists(srcShm))
                {
                    try
                    {
                        await using var shmIn = new FileStream(srcShm, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true);
                        await using var shmOut = new FileStream(stagedSqlitePath + "-shm", FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                        await shmIn.CopyToAsync(shmOut);
                    }
                    catch { }
                }
            }

            var stagedInfo = new FileInfo(stagedSqlitePath);
            if (!stagedInfo.Exists || stagedInfo.Length == 0)
            {
                throw new InvalidOperationException("The selected database file is empty or unreadable.");
            }

            // Read first 64 bytes to detect GZip (.gz), ZIP (.zip), or raw SQLite 3 header
            var headerBytes = new byte[Math.Min(64, (int)stagedInfo.Length)];
            await using (var hdrFs = new FileStream(stagedSqlitePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                _ = await hdrFs.ReadAsync(headerBytes, 0, headerBytes.Length);
            }

            // Decompress GZip (.gz) if needed
            if (headerBytes.Length >= 2 && headerBytes[0] == 0x1F && headerBytes[1] == 0x8B)
            {
                var tmpUnzipped = stagedSqlitePath + ".tmp";
                await using (var inFs = new FileStream(stagedSqlitePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                await using (var gz = new GZipStream(inFs, CompressionMode.Decompress))
                await using (var outFs = new FileStream(tmpUnzipped, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await gz.CopyToAsync(outFs);
                }
                System.IO.File.Move(tmpUnzipped, stagedSqlitePath, overwrite: true);
            }
            // Extract largest .sqlite / .db entry from ZIP archive if needed
            else if (headerBytes.Length >= 4 && headerBytes[0] == 0x50 && headerBytes[1] == 0x4B && headerBytes[2] == 0x03 && headerBytes[3] == 0x04)
            {
                var tmpUnzipped = stagedSqlitePath + ".tmp";
                using (var archive = ZipFile.OpenRead(stagedSqlitePath))
                {
                    var dbEntry = archive.Entries
                        .Where(e => e.Length > 0 && (
                            e.Name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
                            e.Name.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase) ||
                            e.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                            e.Name.EndsWith(".db3", StringComparison.OrdinalIgnoreCase)))
                        .OrderByDescending(e => e.Length)
                        .FirstOrDefault();

                    if (dbEntry == null)
                    {
                        throw new InvalidOperationException("ZIP archive does not contain a .sqlite or .db database file.");
                    }

                    dbEntry.ExtractToFile(tmpUnzipped, overwrite: true);
                }
                System.IO.File.Move(tmpUnzipped, stagedSqlitePath, overwrite: true);
            }

            // Validate SQLite 3 magic header
            stagedInfo.Refresh();
            var magicBuf = new byte[Math.Min(64, (int)stagedInfo.Length)];
            await using (var magicFs = new FileStream(stagedSqlitePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                _ = await magicFs.ReadAsync(magicBuf, 0, magicBuf.Length);
            }
            var headerStr = Encoding.ASCII.GetString(magicBuf);
            if (!headerStr.Contains("SQLite format 3", StringComparison.Ordinal))
            {
                try { System.IO.File.Delete(stagedSqlitePath); } catch { }
                throw new InvalidOperationException("Unsupported file format. Please select a valid SQLite database (.sqlite, .sqlite3, .db, .zip, .gz).");
            }

            var meta = new Dictionary<string, object?>
            {
                ["token"] = token,
                ["original_name"] = originalName,
                ["file_size"] = stagedInfo.Length,
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

        var conflictPolicy = (GetString(body, "conflict_policy") ?? "skip").Trim().ToLowerInvariant();
        if (body.TryGetValue("options", out var optElConflict) && optElConflict.ValueKind == JsonValueKind.Object &&
            optElConflict.TryGetProperty("conflict_policy", out var cpEl) && cpEl.ValueKind == JsonValueKind.String)
        {
            conflictPolicy = (cpEl.GetString() ?? conflictPolicy).Trim().ToLowerInvariant();
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
            var stagedPath = ResolveStagedPath(token);
            var tableReports = new List<Dictionary<string, object?>>();
            var tablesBySource = new Dictionary<string, object?>();
            int totalInserted = 0;
            int totalUpdated = 0;
            int totalSkipped = 0;

            // Create an automatic safety backup of the current SQLite DB before modifying any data
            string? safetyBackupName = null;
            try
            {
                if (System.IO.File.Exists(Db.SqlitePath))
                {
                    var backupDir = Db.StoragePath("app/backups");
                    Directory.CreateDirectory(backupDir);
                    safetyBackupName = $"pre-sqlite-import-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sqlite";
                    System.IO.File.Copy(Db.SqlitePath, Path.Combine(backupDir, safetyBackupName), overwrite: true);
                }
            }
            catch
            {
                safetyBackupName = null;
            }

            // Ensure target schema has all latest tables/columns before importing
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

            // Parse optional per-table UI overrides (options.tables) so disabled tables are skipped and custom mappings are honored
            var tableOverrides = new Dictionary<string, (bool Enabled, string? TargetTable)>(StringComparer.OrdinalIgnoreCase);
            if (body.TryGetValue("options", out var optEl2) && optEl2.ValueKind == JsonValueKind.Object &&
                optEl2.TryGetProperty("tables", out var tablesEl) && tablesEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in tablesEl.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                    var isEn = !prop.Value.TryGetProperty("enabled", out var enEl) || enEl.ValueKind != JsonValueKind.False;
                    var mappedTarget = prop.Value.TryGetProperty("target_table", out var ttEl) && ttEl.ValueKind == JsonValueKind.String
                        ? (ttEl.GetString() ?? "").Trim()
                        : null;
                    tableOverrides[prop.Name] = (isEn, string.IsNullOrWhiteSpace(mappedTarget) ? null : mappedTarget);
                }
            }

            // Open staged copy in ReadWrite + Pooling=False so WAL-mode SQLite files open cleanly without Error 8/14
            var srcConnStr = new SqliteConnectionStringBuilder
            {
                DataSource = stagedPath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString();
            await using var srcConn = new SqliteConnection(srcConnStr);
            await srcConn.OpenAsync();

            var srcTables = (await srcConn.QueryAsync<string>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")).ToList();

            // Parent-before-child topological import priority so master tables always load before transactions
            var priorityOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["categories"] = 10,
                ["brands"] = 11,
                ["formulations"] = 12,
                ["storage_locations"] = 13,
                ["tax_rates"] = 14,
                ["expense_categories"] = 15,
                ["chart_of_accounts"] = 16,
                ["accounts"] = 17,
                ["doctors"] = 18,
                ["suppliers"] = 19,
                ["customers"] = 20,
                ["employees"] = 21,
                ["products"] = 30,
                ["product_suppliers"] = 31,
                ["batches"] = 32,
                ["customer_whole_sale_prices"] = 33,
                ["customer_regular_items"] = 34,
                ["trade_schemes"] = 35,
                ["trade_scheme_slabs"] = 36,
                ["purchase_orders"] = 40,
                ["purchase_order_items"] = 41,
                ["purchase_invoices"] = 42,
                ["purchase_invoice_items"] = 43,
                ["purchase_returns"] = 44,
                ["purchase_return_items"] = 45,
                ["sale_invoices"] = 50,
                ["sale_invoice_items"] = 51,
                ["sale_returns"] = 52,
                ["sale_return_items"] = 53,
                ["payments"] = 60,
                ["payment_allocations"] = 61,
                ["customer_ledgers"] = 62,
                ["supplier_ledgers"] = 63,
                ["cash_drawer_sessions"] = 64,
                ["cash_drawer_transactions"] = 65,
                ["account_transactions"] = 66,
                ["expenses"] = 67,
                ["journal_entries"] = 68,
                ["journal_entry_lines"] = 69,
                ["stock_adjustments"] = 70,
                ["stock_adjustment_items"] = 71,
                ["stocktake_sessions"] = 72,
                ["stocktake_items"] = 73,
                ["stock_disposals"] = 74,
                ["stock_disposal_items"] = 75,
                ["user_demands"] = 76,
                ["document_sequences"] = 80,
                ["document_histories"] = 81
            };

            var importPlan = new List<(string SrcTable, string TargetTable, int Priority)>();
            foreach (var srcTbl in srcTables)
            {
                if (ProtectedTables.Contains(srcTbl))
                {
                    continue;
                }

                string targetTbl;
                if (tableOverrides.TryGetValue(srcTbl, out var ov))
                {
                    if (!ov.Enabled || string.IsNullOrWhiteSpace(ov.TargetTable))
                    {
                        continue;
                    }
                    targetTbl = ov.TargetTable.ToLowerInvariant();
                }
                else
                {
                    if (OptionalConfigTables.Contains(srcTbl))
                    {
                        continue;
                    }
                    targetTbl = TableAliases.TryGetValue(srcTbl, out var aliased) ? aliased : srcTbl.ToLowerInvariant();
                }

                if (ProtectedTables.Contains(targetTbl) || !await Db.HasTableAsync(targetTbl))
                {
                    continue;
                }

                var prio = priorityOrder.TryGetValue(targetTbl, out var p) ? p : 100;
                importPlan.Add((srcTbl, targetTbl, prio));
            }

            importPlan = importPlan.OrderBy(x => x.Priority).ThenBy(x => x.TargetTable, StringComparer.OrdinalIgnoreCase).ToList();

            // Resolve valid user IDs in target DB so orphan user_id / reversed_by_id foreign keys remap cleanly to Admin
            var validUserIds = new HashSet<long>();
            long fallbackUserId = CurrentUserId ?? 1;
            if (await Db.HasTableAsync("users"))
            {
                var uRows = await Db.QueryListAsync("SELECT id FROM users ORDER BY id ASC");
                foreach (var ur in uRows)
                {
                    if (ur.TryGetValue("id", out var idObj) && idObj != null && long.TryParse(idObj.ToString(), out var uid))
                    {
                        validUserIds.Add(uid);
                    }
                }
                if (validUserIds.Count > 0 && !validUserIds.Contains(fallbackUserId))
                {
                    fallbackUserId = validUserIds.First();
                }
            }

            var isReplaceMode = string.Equals(mode, "replace", StringComparison.OrdinalIgnoreCase);

            await using var dstConn = await Db.OpenConnectionAsync();
            // Disable FK enforcement during atomic bulk import so cross-referenced rows & preserved IDs load cleanly
            await dstConn.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await using var tx = await dstConn.BeginTransactionAsync();

            try
            {
                // In Replace mode, clear target tables in reverse topological order first so preserved IDs never collide
                if (isReplaceMode)
                {
                    foreach (var item in importPlan.AsEnumerable().Reverse())
                    {
                        var escapedDst = item.TargetTable.Replace("\"", "\"\"");
                        await dstConn.ExecuteAsync($"DELETE FROM \"{escapedDst}\";", transaction: tx);
                    }
                }

                foreach (var item in importPlan)
                {
                    var srcTbl = item.SrcTable;
                    var targetTbl = item.TargetTable;
                    var escapedSrc = srcTbl.Replace("\"", "\"\"");
                    var escapedDst = targetTbl.Replace("\"", "\"\"");

                    var targetCols = await Db.GetTableColumnsAsync(targetTbl);
                    if (targetCols.Count == 0) continue;

                    // Inspect target table NOT NULL + DEFAULT metadata so NULL source values fall back cleanly to column defaults
                    var notNullWithDefault = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        var pragmaInfo = await dstConn.QueryAsync($"PRAGMA table_info(\"{escapedDst}\");", transaction: tx);
                        foreach (var pr in pragmaInfo)
                        {
                            if (pr is IDictionary<string, object> pd &&
                                pd.TryGetValue("name", out var cn) && cn != null &&
                                pd.TryGetValue("notnull", out var nn) && Convert.ToInt32(nn ?? 0) == 1 &&
                                pd.TryGetValue("dflt_value", out var dv) && dv != null)
                            {
                                notNullWithDefault.Add(cn.ToString()!);
                            }
                        }
                    }
                    catch { }

                    var rows = (await srcConn.QueryAsync($"SELECT * FROM \"{escapedSrc}\"")).Select(KarobarDb.NormalizeRow).ToList();
                    int insertedForTable = 0;
                    int updatedForTable = 0;
                    int skippedForTable = 0;
                    var warnings = new List<string>();

                    foreach (var row in rows)
                    {
                        try
                        {
                            var filtered = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                            foreach (var kv in row)
                            {
                                if (!targetCols.Contains(kv.Key)) continue;
                                var val = KarobarDb.PrepareDbValue(kv.Value);
                                if (val == null && notNullWithDefault.Contains(kv.Key))
                                {
                                    // Omit NULL for NOT NULL columns that have a SQL DEFAULT so SQLite applies the default
                                    continue;
                                }

                                // Remap orphan user references to active Admin user so audit & FK checks stay valid
                                if ((kv.Key.Equals("user_id", StringComparison.OrdinalIgnoreCase) ||
                                     kv.Key.Equals("reversed_by_id", StringComparison.OrdinalIgnoreCase) ||
                                     kv.Key.Equals("created_by", StringComparison.OrdinalIgnoreCase)) &&
                                    val != null &&
                                    long.TryParse(val.ToString(), out var refUid) &&
                                    refUid > 0 &&
                                    validUserIds.Count > 0 &&
                                    !validUserIds.Contains(refUid))
                                {
                                    val = fallbackUserId;
                                }

                                filtered[kv.Key] = val;
                            }

                            if (filtered.Count == 0)
                            {
                                skippedForTable++;
                                continue;
                            }

                            // Preserve original primary keys (`id`) so all FK links (products->suppliers/brands,
                            // batches->products, sale_invoices->customers, sale_invoice_items->sale_invoices) stay 100% intact!
                            var cols = filtered.Keys.ToList();
                            var colList = string.Join(", ", cols.Select(c => $"\"{c.Replace("\"", "\"\"")}\""));
                            var paramList = string.Join(", ", cols.Select((_, idx) => $"@p{idx}"));
                            var dynParams = new DynamicParameters();
                            for (int i = 0; i < cols.Count; i++)
                            {
                                dynParams.Add($"p{i}", filtered[cols[i]]);
                            }

                            var useReplaceVerb = isReplaceMode || conflictPolicy == "upsert";
                            var verb = useReplaceVerb ? "INSERT OR REPLACE" : "INSERT OR IGNORE";
                            var insertSql = $"{verb} INTO \"{escapedDst}\" ({colList}) VALUES ({paramList});";
                            var affected = await dstConn.ExecuteAsync(insertSql, dynParams, tx);
                            if (affected > 0)
                            {
                                insertedForTable++;
                                totalInserted++;
                            }
                            else
                            {
                                skippedForTable++;
                                totalSkipped++;
                            }
                        }
                        catch (Exception rowEx)
                        {
                            skippedForTable++;
                            totalSkipped++;
                            if (warnings.Count < 5)
                            {
                                warnings.Add(rowEx.Message);
                            }
                        }
                    }

                    // Sync sqlite_sequence so future inserts continue cleanly after MAX(id)
                    if (targetCols.Contains("id"))
                    {
                        try
                        {
                            var maxId = await dstConn.ExecuteScalarAsync<long?>($"SELECT MAX(id) FROM \"{escapedDst}\";", transaction: tx);
                            if (maxId.HasValue && maxId.Value > 0)
                            {
                                await dstConn.ExecuteAsync(
                                    "DELETE FROM sqlite_sequence WHERE name = @tbl; INSERT INTO sqlite_sequence (name, seq) VALUES (@tbl, @maxId);",
                                    new { tbl = targetTbl, maxId = maxId.Value },
                                    tx);
                            }
                        }
                        catch
                        {
                            // sqlite_sequence may not exist if no AUTOINCREMENT table was created yet
                        }
                    }

                    var report = new Dictionary<string, object?>
                    {
                        ["source_table"] = srcTbl,
                        ["target_table"] = targetTbl,
                        ["source_rows"] = rows.Count,
                        ["inserted"] = insertedForTable,
                        ["updated"] = updatedForTable,
                        ["skipped"] = skippedForTable,
                        ["warnings"] = warnings
                    };
                    tableReports.Add(report);
                    tablesBySource[srcTbl] = report;
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
            finally
            {
                try { await dstConn.ExecuteAsync("PRAGMA foreign_keys = ON;"); } catch { }
            }

            KarobarDb.FlushSchemaCache();

            return Ok(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["message"] = "SQLite import completed successfully.",
                ["tables"] = tableReports,
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
        try { if (System.IO.File.Exists(sqlitePath + "-wal")) System.IO.File.Delete(sqlitePath + "-wal"); } catch { }
        try { if (System.IO.File.Exists(sqlitePath + "-shm")) System.IO.File.Delete(sqlitePath + "-shm"); } catch { }
        try { if (System.IO.File.Exists(metaPath)) System.IO.File.Delete(metaPath); } catch { }
    }

    private async Task<Dictionary<string, object?>> InspectStagedSqliteAsync(string token, string stagedSqlitePath, Dictionary<string, object?> meta)
    {
        var targetCatalog = await BuildTargetCatalogAsync();
        var importableTables = new List<Dictionary<string, object?>>();
        var protectedSkipped = new List<Dictionary<string, object?>>();
        int totalImportableRows = 0;
        int autoMatchedCount = 0;

        // Open staged copy in ReadWrite + Pooling=False so WAL-mode databases open cleanly without Error 8/14
        // and reuse ONE connection across all tables (20x faster than opening 140+ separate connections).
            var srcConnStr = new SqliteConnectionStringBuilder
            {
                DataSource = stagedSqlitePath,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false
            }.ToString();
            await using var srcConn = new SqliteConnection(srcConnStr);
            await srcConn.OpenAsync();

            // Merge any copied -wal frames into the staged database file so all tables & recent invoices are visible
            try { await srcConn.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE);"); } catch { }

            var rawTables = (await srcConn.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")).ToList();

        if (rawTables.Count == 0)
        {
            throw new InvalidOperationException("The selected SQLite database contains no tables.");
        }

        foreach (var srcTable in rawTables)
        {
            var escaped = srcTable.Replace("\"", "\"\"");
            int rowCount = 0;
            try
            {
                rowCount = await srcConn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM \"{escaped}\"");
            }
            catch
            {
                // Skip virtual/corrupt auxiliary tables safely without failing the entire inspection
                continue;
            }

            var srcCols = await GetTableColumnsInfoFromConnAsync(srcConn, srcTable);

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

            var sampleRows = await GetSampleRowsFromConnAsync(srcConn, srcTable);
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
        await using var dstConn = await Db.OpenConnectionAsync();
        var rows = (await dstConn.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")).ToList();

        foreach (var tableName in rows)
        {
            if (string.IsNullOrEmpty(tableName) || ProtectedTables.Contains(tableName))
            {
                continue;
            }

            var cols = await GetTableColumnsInfoFromConnAsync(dstConn, tableName);
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
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = sqlitePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();
        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();
        return await GetTableColumnsInfoFromConnAsync(conn, tableName);
    }

    private static async Task<List<Dictionary<string, object?>>> GetTableColumnsInfoFromConnAsync(System.Data.Common.DbConnection conn, string tableName)
    {
        var list = new List<Dictionary<string, object?>>();
        try
        {
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
        }
        catch
        {
            // Ignore unreadable table metadata
        }
        return list;
    }

    private static async Task<List<Dictionary<string, object?>>> GetSampleRowsAsync(string sqlitePath, string tableName)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = sqlitePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false
        }.ToString();
        await using var conn = new SqliteConnection(cs);
        await conn.OpenAsync();
        return await GetSampleRowsFromConnAsync(conn, tableName);
    }

    private static async Task<List<Dictionary<string, object?>>> GetSampleRowsFromConnAsync(System.Data.Common.DbConnection conn, string tableName)
    {
        try
        {
            var escaped = tableName.Replace("\"", "\"\"");
            var rows = await conn.QueryAsync($"SELECT * FROM \"{escaped}\" LIMIT 3");
            var normalized = rows.Select(KarobarDb.NormalizeRow).ToList();
            foreach (var row in normalized)
            {
                foreach (var key in row.Keys.ToList())
                {
                    if (row[key] is byte[] blob)
                    {
                        row[key] = $"[binary {blob.Length} bytes]";
                    }
                }
            }
            return normalized;
        }
        catch
        {
            return new List<Dictionary<string, object?>>();
        }
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

            var isNotNull = tCol.GetValueOrDefault("notnull") is true or 1 or 1L;
            var hasDefault = tCol.GetValueOrDefault("dflt_value") != null;
            var isPk = tCol.GetValueOrDefault("pk") is true or 1 or 1L;

            mappings.Add(new Dictionary<string, object?>
            {
                ["target_column"] = tName,
                ["target_type"] = tCol["type"],
                ["required"] = isNotNull && !hasDefault && !isPk,
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

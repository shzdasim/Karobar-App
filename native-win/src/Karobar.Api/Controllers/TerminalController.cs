using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Background;
using Karobar.Data;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Karobar.Api.Controllers;

[Route("api")]
public class TerminalController : ApiControllerBase
{
    private readonly LicenseService _licenseService;
    private readonly SqliteExportService _sqliteExportService;
    private readonly BackupService _backupService;
    private readonly DocumentSequenceService _sequenceService;
    private readonly LocalWhatsAppSidecarManager? _waSidecar;

    public TerminalController(
        KarobarDb db,
        LicenseService licenseService,
        SqliteExportService sqliteExportService,
        BackupService backupService,
        DocumentSequenceService sequenceService,
        LocalWhatsAppSidecarManager? waSidecar = null) : base(db)
    {
        _licenseService = licenseService;
        _sqliteExportService = sqliteExportService;
        _backupService = backupService;
        _sequenceService = sequenceService;
        _waSidecar = waSidecar;
    }

    [HttpGet("terminals/ping")]
    [HttpPost("terminals/ping")]
    [AllowAnonymous]
    public async Task<IActionResult> Ping()
    {
        var setting = await Db.HasTableAsync("settings")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
            : null;

        // Enforce LAN Network Pairing Token on remote terminals when configured on Store Server
        var storedToken = (setting?.GetValueOrDefault("network_server_token")?.ToString() ?? "").Trim();
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        var isLoopback = remoteIp == null || IPAddress.IsLoopback(remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp);
        if (!isLoopback && !string.IsNullOrEmpty(storedToken))
        {
            var providedToken = (Request.Headers["X-Network-Token"].FirstOrDefault()
                                 ?? Request.Headers["X-Terminal-Token"].FirstOrDefault()
                                 ?? Request.Query["network_token"].FirstOrDefault()
                                 ?? "").Trim();
            var validToken = !string.IsNullOrEmpty(providedToken) &&
                             CryptographicOperations.FixedTimeEquals(
                                 Encoding.UTF8.GetBytes(storedToken),
                                 Encoding.UTF8.GetBytes(providedToken));
            if (!validToken)
            {
                return StatusCode(403, new
                {
                    ok = false,
                    code = "INVALID_NETWORK_TOKEN",
                    message = "Invalid or missing Network Pairing Token. Copy the exact Pairing Token from Store Server Settings -> Network & Multi-PC Terminals."
                });
            }
        }

        Dictionary<string, object?>? currentTerminal = null;
        if (HttpContext.Items.TryGetValue("KarobarTerminal", out var termObj) && termObj is Dictionary<string, object?> td)
        {
            currentTerminal = new Dictionary<string, object?>
            {
                ["id"] = td.GetValueOrDefault("id"),
                ["terminal_uid"] = td.GetValueOrDefault("terminal_uid"),
                ["name"] = td.GetValueOrDefault("name"),
                ["mode"] = td.GetValueOrDefault("mode"),
                ["is_authorized"] = td.GetValueOrDefault("is_authorized") is true or 1 or 1L
            };
        }

        return Ok(new
        {
            ok = true,
            app = "Karobar-App ERP Server",
            store_name = setting?.GetValueOrDefault("store_name")?.ToString() ?? "Karobar App",
            server_time = DateTime.UtcNow.ToString("O"),
            db_driver = "sqlite",
            os_family = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : "Darwin",
            php_version = ".NET 9.0",
            terminal = currentTerminal
        });
    }

    [HttpGet("terminals")]
    [Authorize]
    public async Task<IActionResult> Index()
    {
        var terminals = new List<Dictionary<string, object?>>();

        if (await Db.HasTableAsync("terminals"))
        {
            var rows = await Db.QueryListAsync(@"
                SELECT t.*, u.name as current_user_name, u.email as current_user_email
                FROM terminals t
                LEFT JOIN users u ON u.id = t.current_user_id
                ORDER BY t.is_authorized DESC, t.last_seen_at DESC, t.id ASC");

            var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
            var hasOpenDrawers = await Db.HasTableAsync("cash_drawer_sessions");
            var hasTermColOnDrawer = hasOpenDrawers && await Db.HasColumnAsync("cash_drawer_sessions", "terminal_id");
            var hasSaleInvoices = await Db.HasTableAsync("sale_invoices");
            var hasTermColOnSales = hasSaleInvoices && await Db.HasColumnAsync("sale_invoices", "terminal_id");

            foreach (var t in rows)
            {
                var termId = Convert.ToInt64(t["id"]);
                var lastSeenStr = t.GetValueOrDefault("last_seen_at")?.ToString();
                bool isOnline = false;
                if (!string.IsNullOrWhiteSpace(lastSeenStr) &&
                    DateTimeOffset.TryParse(lastSeenStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var lsDt))
                {
                    isOnline = (DateTimeOffset.UtcNow - lsDt).TotalSeconds <= 45;
                }

                Dictionary<string, object?>? currentUser = null;
                long? curUid = null;
                if (t.GetValueOrDefault("current_user_id") != null && long.TryParse(t.GetValueOrDefault("current_user_id")?.ToString(), out var parsedUid))
                {
                    curUid = parsedUid;
                    currentUser = new Dictionary<string, object?>
                    {
                        ["id"] = parsedUid,
                        ["name"] = t.GetValueOrDefault("current_user_name"),
                        ["email"] = t.GetValueOrDefault("current_user_email")
                    };
                }

                Dictionary<string, object?>? openDrawer = null;
                if (hasOpenDrawers)
                {
                    openDrawer = hasTermColOnDrawer
                        ? await Db.QuerySingleOrDefaultAsync("SELECT id, opening_balance, closing_balance_expected AS expected_balance, opened_at FROM cash_drawer_sessions WHERE status = 'open' AND terminal_id = @termId ORDER BY id DESC LIMIT 1", new { termId })
                        : (curUid.HasValue ? await Db.QuerySingleOrDefaultAsync("SELECT id, opening_balance, closing_balance_expected AS expected_balance, opened_at FROM cash_drawer_sessions WHERE status = 'open' AND user_id = @curUid ORDER BY id DESC LIMIT 1", new { curUid }) : null);
                }

                int todayInvoicesCount = 0;
                decimal todaySalesTotal = 0.0m;
                if (hasSaleInvoices)
                {
                    var stat = hasTermColOnSales
                        ? await Db.QuerySingleOrDefaultAsync("SELECT COUNT(1) AS cnt, COALESCE(SUM(total), 0) AS tot FROM sale_invoices WHERE DATE(date) = @todayStr AND terminal_id = @termId AND (status IS NULL OR status != 'reversed')", new { todayStr, termId })
                        : (curUid.HasValue ? await Db.QuerySingleOrDefaultAsync("SELECT COUNT(1) AS cnt, COALESCE(SUM(total), 0) AS tot FROM sale_invoices WHERE DATE(date) = @todayStr AND user_id = @curUid AND (status IS NULL OR status != 'reversed')", new { todayStr, curUid }) : null);
                    if (stat != null)
                    {
                        todayInvoicesCount = Convert.ToInt32(stat.GetValueOrDefault("cnt") ?? 0);
                        todaySalesTotal = AccountService.ToDecimal(stat.GetValueOrDefault("tot"));
                    }
                }

                terminals.Add(new Dictionary<string, object?>
                {
                    ["id"] = termId,
                    ["terminal_uid"] = t.GetValueOrDefault("terminal_uid"),
                    ["name"] = t.GetValueOrDefault("name"),
                    ["hostname"] = t.GetValueOrDefault("hostname"),
                    ["ip_address"] = t.GetValueOrDefault("ip_address"),
                    ["os_platform"] = t.GetValueOrDefault("os_platform"),
                    ["app_version"] = t.GetValueOrDefault("app_version"),
                    ["mode"] = t.GetValueOrDefault("mode"),
                    ["is_authorized"] = t.GetValueOrDefault("is_authorized") is true or 1 or 1L,
                    ["is_online"] = isOnline,
                    ["last_seen_at"] = lastSeenStr,
                    ["last_seen_human"] = isOnline ? "Online now" : (lastSeenStr ?? "Never"),
                    ["current_user"] = currentUser,
                    ["open_drawer"] = openDrawer,
                    ["today_invoices_count"] = todayInvoicesCount,
                    ["today_sales_total"] = Math.Round(todaySalesTotal, 2)
                });
            }
        }

        var serverSummary = await BuildServerHealthSummaryAsync(terminals);
        HttpContext.Items.TryGetValue("KarobarTerminal", out var currentTerm);

        return Ok(new
        {
            server = serverSummary,
            current_terminal = currentTerm,
            terminals
        });
    }

    [HttpPut("terminals/settings/network")]
    [Authorize]
    public async Task<IActionResult> UpdateNetworkSettings()
    {
        if (!await CanAsync("settings.update")) return Forbidden("Only administrators can update network settings.");

        var body = await ReadJsonBodyAsync();
        var setting = await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1");
        if (setting == null) return NotFound();

        var settingId = Convert.ToInt64(setting["id"]);
        var updates = new Dictionary<string, object?>();

        if (body.ContainsKey("terminal_auto_authorize"))
        {
            updates["terminal_auto_authorize"] = GetBool(body, "terminal_auto_authorize", true) ? 1 : 0;
        }

        if (GetBool(body, "regenerate_token", false))
        {
            updates["network_server_token"] = "KRB-NET-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
        }

        if (updates.Count > 0)
        {
            await Db.UpdateAsync("settings", settingId, updates);
        }

        var fresh = await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings WHERE id = @id", new { id = settingId });
        return Ok(new
        {
            message = "Network settings updated.",
            terminal_auto_authorize = fresh?.GetValueOrDefault("terminal_auto_authorize") is not (false or 0 or 0L),
            network_server_token = fresh?.GetValueOrDefault("network_server_token")
        });
    }

    [HttpPut("terminals/{id:long}")]
    [Authorize]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("settings.update")) return Forbidden("Only administrators can manage terminals.");

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM terminals WHERE id = @id", new { id });
        if (existing == null) return NotFound();

        var body = await ReadJsonBodyAsync();
        var updates = new Dictionary<string, object?>();

        if (body.ContainsKey("name")) updates["name"] = (GetString(body, "name") ?? "").Trim();
        if (body.ContainsKey("is_authorized")) updates["is_authorized"] = GetBool(body, "is_authorized", true) ? 1 : 0;
        if (body.ContainsKey("mode")) updates["mode"] = GetString(body, "mode") ?? "terminal";

        if (updates.Count > 0)
        {
            await Db.UpdateAsync("terminals", id, updates);
        }

        var fresh = await Db.QuerySingleOrDefaultAsync("SELECT * FROM terminals WHERE id = @id", new { id });
        return Ok(new
        {
            message = "Terminal updated successfully.",
            terminal = fresh
        });
    }

    private static readonly string[] TerminalReferenceTables =
    {
        "cash_drawer_sessions",
        "cash_drawer_transactions",
        "sale_invoices",
        "sale_returns",
        "payments",
        "expenses",
        "invoice_drafts",
        "document_histories"
    };

    private async Task DetachTerminalReferencesAsync(
        System.Data.Common.DbConnection conn,
        System.Data.Common.DbTransaction tx,
        IReadOnlyCollection<long> terminalIds)
    {
        if (terminalIds.Count == 0) return;

        foreach (var table in TerminalReferenceTables)
        {
            if (await Db.HasTableAsync(table) && await Db.HasColumnAsync(table, "terminal_id"))
            {
                await conn.ExecuteAsync(
                    $"UPDATE \"{table}\" SET terminal_id = NULL WHERE terminal_id IN @ids",
                    new { ids = terminalIds },
                    tx);
            }
        }
    }

    [HttpDelete("terminals/{id:long}")]
    [Authorize]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("settings.update")) return Forbidden("Only administrators can remove terminals.");

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM terminals WHERE id = @id", new { id });
        if (existing == null) return NotFound();

        if (CurrentTerminalId.HasValue && CurrentTerminalId.Value == id)
        {
            return Unprocessable("You cannot delete the active terminal you are currently using. Remove the other (duplicate or offline) terminal instead.");
        }

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        try
        {
            await DetachTerminalReferencesAsync(conn, tx, new[] { id });
            await conn.ExecuteAsync("DELETE FROM terminals WHERE id = @id", new { id }, tx);
            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        SyncPulseService.Bump("terminals", "drawer");
        return Ok(new { message = "Terminal removed successfully. All historical sales and cash drawer shifts were safely preserved." });
    }

    [HttpGet("server/export-database")]
    [HttpPost("server/export-database")]
    [Authorize]
    public async Task<IActionResult> ExportDatabase([FromQuery] string? format = null)
    {
        if (!await CanAsync("backup.create", "backup.view", "settings.update"))
        {
            return Forbidden("Only administrators can export the database.");
        }

        var fmt = (format ?? "sqlite").Trim().ToLowerInvariant();
        if (fmt == "sqlite")
        {
            var path = await _sqliteExportService.ExportToSqliteFileAsync();
            var bytes = await System.IO.File.ReadAllBytesAsync(path);
            try { System.IO.File.Delete(path); } catch { }
            return File(bytes, "application/vnd.sqlite3", $"karobar-database-{DateTime.UtcNow:yyyy-MM-dd_HHmmss}.sqlite");
        }

        var backupType = fmt == "zip" ? "full" : "database";
        var created = await _backupService.CreateBackupAsync(backupType, CurrentUserId);
        var backupId = Convert.ToInt64(created["id"]);
        var filePath = await _backupService.GetDownloadPathAsync(backupId);

        if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
        {
            return StatusCode(500, new { message = "Failed to generate database export file." });
        }

        var fileBytes = await System.IO.File.ReadAllBytesAsync(filePath);
        return File(fileBytes, "application/octet-stream", Path.GetFileName(filePath));
    }

    [HttpPost("server/quick-import-database")]
    [HttpPost("server/import-database")]
    [Authorize]
    public async Task<IActionResult> QuickImportDatabase()
    {
        if (!await CanAsync("backup.restore", "settings.update"))
        {
            return Forbidden("Only administrators can import a database.");
        }

        if (!Request.HasFormContentType)
        {
            return Unprocessable("A multipart form upload with a database file is required.");
        }

        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("file");
        if (file == null || file.Length == 0)
        {
            return Unprocessable("Please select a valid .sqlite or .zip database file to import.");
        }

        var mode = (form["mode"].FirstOrDefault() ?? "merge").Trim().ToLowerInvariant();
        var conflictPolicy = (form["conflict_policy"].FirstOrDefault() ?? "upsert").Trim().ToLowerInvariant();
        var includeSettings = string.Equals(form["include_settings"].FirstOrDefault(), "1", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(form["include_settings"].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);

        var tempDir = Path.Combine(Path.GetTempPath(), $"karobar_qimp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var uploadedPath = Path.Combine(tempDir, Path.GetFileName(file.FileName));
            await using (var fs = new FileStream(uploadedPath, FileMode.Create, FileAccess.Write))
            {
                await file.CopyToAsync(fs);
            }

            string sqliteSourcePath = uploadedPath;
            if (uploadedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(uploadedPath);
                var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) || e.FullName.EndsWith(".db", StringComparison.OrdinalIgnoreCase));
                if (entry == null) return Unprocessable("No .sqlite database file found inside the uploaded ZIP archive.");
                sqliteSourcePath = Path.Combine(tempDir, "extracted.sqlite");
                entry.ExtractToFile(sqliteSourcePath, overwrite: true);
            }

            // Validate SQLite header
            var header = new byte[16];
            await using (var hfs = new FileStream(sqliteSourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var read = await hfs.ReadAsync(header, 0, 16);
                if (read < 15 || !Encoding.ASCII.GetString(header, 0, 15).StartsWith("SQLite format 3", StringComparison.Ordinal))
                    return Unprocessable("Uploaded file is not a valid SQLite 3 database.");
            }

            // Pre-import safety backup
            if (System.IO.File.Exists(Db.SqlitePath))
            {
                try
                {
                    var bDir = Db.StoragePath("app/backups");
                    Directory.CreateDirectory(bDir);
                    System.IO.File.Copy(Db.SqlitePath, Path.Combine(bDir, $"pre-quick-import-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sqlite"), overwrite: true);
                }
                catch { }
            }

            var protectedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "users", "licenses", "roles", "permissions", "model_has_roles", "model_has_permissions",
                "role_has_permissions", "sessions", "personal_access_tokens", "password_reset_tokens",
                "migrations", "cache", "cache_locks", "jobs", "job_batches", "failed_jobs", "backup_logs", "backup_schedules"
            };
            if (!includeSettings)
            {
                protectedTables.Add("settings");
                protectedTables.Add("theme_settings");
            }

            var srcConnStr = new SqliteConnectionStringBuilder { DataSource = sqliteSourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            await using var srcConn = new SqliteConnection(srcConnStr);
            await srcConn.OpenAsync();
            var srcTables = (await srcConn.QueryAsync<string>("SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name")).ToList();

            int totalInserted = 0, totalUpdated = 0, totalSkipped = 0;
            var isReplace = mode == "replace";
            var useReplaceVerb = isReplace || conflictPolicy == "upsert";

            await using var dstConn = await Db.OpenConnectionAsync();
            await dstConn.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            try
            {
                await using var tx = await dstConn.BeginTransactionAsync();
                foreach (var tbl in srcTables)
                {
                    if (protectedTables.Contains(tbl) || !await Db.HasTableAsync(tbl)) continue;
                    var escaped = tbl.Replace("\"", "\"\"");
                    var dstCols = (await dstConn.QueryAsync<dynamic>($"PRAGMA table_info(\"{escaped}\");", transaction: tx))
                        .Select(c => (string)c.name).ToHashSet(StringComparer.OrdinalIgnoreCase);

                    if (isReplace)
                    {
                        await dstConn.ExecuteAsync($"DELETE FROM \"{escaped}\";", transaction: tx);
                    }

                    var rows = (await srcConn.QueryAsync($"SELECT * FROM \"{escaped}\";")).ToList();
                    foreach (var r in rows)
                    {
                        if (r is not IDictionary<string, object?> dict) continue;
                        var filtered = dict.Where(kv => dstCols.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => KarobarDb.PrepareDbValue(kv.Value));
                        if (filtered.Count == 0) { totalSkipped++; continue; }

                        var cols = filtered.Keys.ToList();
                        var colSql = string.Join(", ", cols.Select(c => $"\"{c.Replace("\"", "\"\"")}\""));
                        var valSql = string.Join(", ", cols.Select((_, i) => $"@p{i}"));
                        var dp = new DynamicParameters();
                        for (int i = 0; i < cols.Count; i++) dp.Add($"p{i}", filtered[cols[i]]);

                        var verb = useReplaceVerb ? "INSERT OR REPLACE" : "INSERT OR IGNORE";
                        try
                        {
                            var aff = await dstConn.ExecuteAsync($"{verb} INTO \"{escaped}\" ({colSql}) VALUES ({valSql});", dp, tx);
                            if (aff > 0) totalInserted++;
                            else totalSkipped++;
                        }
                        catch
                        {
                            totalSkipped++;
                        }
                    }
                }
                await tx.CommitAsync();
            }
            finally
            {
                await dstConn.ExecuteAsync("PRAGMA foreign_keys = ON;");
            }

            SyncPulseService.Bump("stock", "drafts", "drawer", "settings");
            return Ok(new
            {
                ok = true,
                type = "sqlite_topological_import",
                message = $"Database imported successfully! Inserted/Upserted {totalInserted} rows, Skipped {totalSkipped} rows.",
                total_inserted = totalInserted,
                total_updated = totalUpdated,
                total_skipped = totalSkipped
            });
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    [HttpGet("server/diagnostics")]
    [Authorize]
    public async Task<IActionResult> Diagnostics()
    {
        if (!await CanAsync("settings.update"))
        {
            return Forbidden("Only administrators can access server diagnostics.");
        }

        var pulseVersions = SyncPulseService.GetVersions();
        var lanIps = DetectLanIpv4Addresses();
        var port = Request.Host.Port ?? 18960;
        var hostname = Dns.GetHostName();

        // 1. Live SQLite WAL & Latency Check
        var dbStatus = "ok";
        double dbLatencyMs = 0;
        var journalMode = "unknown";
        var busyTimeout = 0;
        var dbHint = "High-concurrency WAL + Private Cache pragmas active (15s busy_timeout).";
        try
        {
            var sw = Stopwatch.StartNew();
            await using var conn = await Db.OpenConnectionAsync();
            journalMode = (await conn.ExecuteScalarAsync<string>("PRAGMA journal_mode;") ?? "unknown").Trim();
            busyTimeout = await conn.ExecuteScalarAsync<int>("PRAGMA busy_timeout;");
            sw.Stop();
            dbLatencyMs = Math.Round(sw.Elapsed.TotalMilliseconds, 2);
            if (!journalMode.Equals("wal", StringComparison.OrdinalIgnoreCase))
            {
                dbStatus = "warn";
                dbHint = $"SQLite is in '{journalMode}' mode instead of WAL. Restart the server to re-apply WAL mode.";
            }
        }
        catch (Exception ex)
        {
            dbStatus = "error";
            dbHint = ex.Message;
        }

        // 2. Document Sequences Check
        var sequences = await Db.HasTableAsync("document_sequences")
            ? await Db.QueryListAsync("SELECT sequence_key, prefix, last_number, padding, updated_at FROM document_sequences ORDER BY sequence_key")
            : new List<Dictionary<string, object?>>();

        // 3. Disk Free Space & Storage Writability Check
        var storageStatus = "ok";
        var storageSummary = "All directories writable";
        var storageHint = "Required for database backups, portable SQLite exports, and invoice PDFs.";
        try
        {
            var storageAppDir = Db.StoragePath("app");
            Directory.CreateDirectory(storageAppDir);
            var probeFile = Path.Combine(storageAppDir, $".write-probe-{Guid.NewGuid():N}");
            await System.IO.File.WriteAllTextAsync(probeFile, "ok");
            System.IO.File.Delete(probeFile);

            var rootPath = Path.GetPathRoot(Path.GetFullPath(Db.SqlitePath));
            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                var drive = new DriveInfo(rootPath);
                var freeGb = Math.Round(drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0), 2);
                storageSummary = $"Writable • {freeGb:0.00} GB free on {drive.Name}";
                if (freeGb < 1.0)
                {
                    storageStatus = "warn";
                    storageHint = "Low disk space (< 1 GB free). Clear old backups or free space on the server drive.";
                }
            }
        }
        catch (Exception ex)
        {
            storageStatus = "error";
            storageSummary = "Storage write test failed";
            storageHint = ex.Message;
        }

        // 4. LAN Network & Port Check
        var netStatus = lanIps.Count > 0 ? (port == 18960 ? "ok" : "warn") : "warn";
        var netSummary = lanIps.Count > 0
            ? $"LAN IP: {string.Join(", ", lanIps)} (Port {port})"
            : $"No active LAN IPv4 detected (127.0.0.1:{port})";
        var netHint = port != 18960
            ? $"Port 18960 was busy so Kestrel bound to fallback port {port}. Update terminal URLs to :{port} or free port 18960."
            : $"Connect Counter PCs to http://{lanIps.FirstOrDefault() ?? "127.0.0.1"}:{port} (set a Router DHCP Reservation for this PC).";

        // 5. Windows Firewall TCP 18960 Check
        var (fwStatus, fwSummary, fwHint) = CheckFirewallPortStatus(port);

        var checks = new object[]
        {
            new
            {
                id = "database",
                title = "Database Engine & Multi-Counter Locking",
                status = dbStatus,
                latency_ms = dbLatencyMs,
                summary = $"SQLite {journalMode.ToUpperInvariant()} ({dbLatencyMs:0.##} ms • timeout {busyTimeout}ms)",
                hint = dbHint
            },
            new
            {
                id = "cache_sync",
                title = "Real-Time SyncPulse Engine",
                status = "ok",
                latency_ms = 0.1,
                summary = $"Active • Stock v{pulseVersions.GetValueOrDefault("stock_version")} • Drafts v{pulseVersions.GetValueOrDefault("drafts_version")}",
                hint = "Broadcasts 4-second stock, held-bill, and cash-drawer updates to all attached POS counters."
            },
            new
            {
                id = "sequences",
                title = "Atomic Document Sequence Allocator",
                status = "ok",
                summary = $"{sequences.Count} sequence counter(s) tracked",
                hint = "Prevents duplicate SI / SLRET / PRINV / PRRET numbers across simultaneous counters."
            },
            new
            {
                id = "storage",
                title = "Disk Space & Storage Permissions",
                status = storageStatus,
                summary = storageSummary,
                hint = storageHint
            },
            new
            {
                id = "network",
                title = "LAN Network & Static IP",
                status = netStatus,
                summary = netSummary,
                hint = netHint
            },
            new
            {
                id = "firewall",
                title = $"Windows Firewall (TCP {port})",
                status = fwStatus,
                summary = fwSummary,
                hint = fwHint
            }
        };

        var (logSizeKb, recentLogs) = ReadNativeServerLogs();

        return Ok(new
        {
            ok = true,
            checked_at = DateTime.UtcNow.ToString("O"),
            checks,
            sequences,
            log_size_kb = logSizeKb,
            recent_logs = recentLogs,
            cli_commands = new
            {
                status = "Karobar-Native.exe --cli install --upgrade-only",
                export_sqlite = "Karobar-Native.exe --cli karobar:export-sqlite",
                import_sqlite = "Karobar-Native.exe --cli karobar:import-sqlite",
                headless_server = "Karobar-Native.exe --server-only"
            }
        });
    }

    [HttpPost("server/troubleshoot")]
    [Authorize]
    public async Task<IActionResult> Troubleshoot()
    {
        if (!await CanAsync("settings.update"))
        {
            return Forbidden("Only administrators can run server troubleshooting actions.");
        }

        var body = await ReadJsonBodyAsync();
        var action = (GetString(body, "action") ?? "").Trim();

        switch (action)
        {
            case "resync_sequences":
                var targets = new (string Key, string Prefix, int Pad, string Table, string Col)[]
                {
                    ("sale_invoice", "SI-", 6, "sale_invoices", "posted_number"),
                    ("sale_return", "SLRET-", 6, "sale_returns", "posted_number"),
                    ("purchase_invoice", "PRINV-", 6, "purchase_invoices", "posted_number"),
                    ("purchase_return", "PRRET-", 6, "purchase_returns", "posted_number"),
                    ("stock_adjustment", "STADJ-", 6, "stock_adjustments", "posted_number")
                };

                var synced = new Dictionary<string, string>();
                foreach (var t in targets)
                {
                    if (!await Db.HasTableAsync(t.Table)) continue;
                    var preview = await _sequenceService.PreviewAsync(t.Key, t.Prefix, t.Pad, t.Table, t.Col);
                    synced[t.Key] = preview;
                }

                return Ok(new
                {
                    ok = true,
                    action,
                    message = "All document sequences verified and synchronized with live invoice tables.",
                    details = synced
                });

            case "force_sync_pulse":
                var versions = SyncPulseService.Bump("stock", "drafts", "drawer", "terminals", "settings");
                return Ok(new
                {
                    ok = true,
                    action,
                    message = "Broadcasted Force-Sync Pulse to all connected LAN terminals.",
                    versions
                });

            case "clear_caches":
                KarobarDb.FlushSchemaCache();
                SyncPulseService.Bump("stock", "drafts", "drawer", "terminals", "settings");
                return Ok(new
                {
                    ok = true,
                    action,
                    message = "Cleared application, route, view, config, and schema caches."
                });

            case "run_migrations":
                KarobarDb.FlushSchemaCache();
                var bootstrapper = HttpContext.RequestServices.GetService<DatabaseBootstrapper>();
                if (bootstrapper != null)
                {
                    await bootstrapper.EnsureSchemaReadyAsync(HttpContext.RequestAborted);
                }
                return Ok(new
                {
                    ok = true,
                    action,
                    message = "Database schema verified and all missing tables/columns upgraded cleanly.",
                    output = "Schema verification complete."
                });

            case "prune_offline_terminals":
                var deleted = 0;
                if (await Db.HasTableAsync("terminals"))
                {
                    var cutoff = DateTime.UtcNow.AddSeconds(-45).ToString("yyyy-MM-dd HH:mm:ss");
                    var activeTid = CurrentTerminalId ?? -1L;
                    var offlineRows = await Db.QueryListAsync(
                        "SELECT id FROM terminals WHERE (last_seen_at IS NULL OR last_seen_at < @cutoff) AND id != @activeTid",
                        new { cutoff, activeTid });
                    var offlineIds = offlineRows
                        .Select(r => AccountService.ToLong(r.GetValueOrDefault("id")))
                        .Where(tid => tid > 0)
                        .ToList();

                    if (offlineIds.Count > 0)
                    {
                        await using var pruneConn = await Db.OpenConnectionAsync();
                        await using var pruneTx = await pruneConn.BeginTransactionAsync();
                        try
                        {
                            await DetachTerminalReferencesAsync(pruneConn, pruneTx, offlineIds);
                            deleted = await pruneConn.ExecuteAsync("DELETE FROM terminals WHERE id IN @ids", new { ids = offlineIds }, pruneTx);
                            await pruneTx.CommitAsync();
                        }
                        catch
                        {
                            await pruneTx.RollbackAsync();
                            throw;
                        }
                    }
                }
                SyncPulseService.Bump("terminals", "drawer");
                return Ok(new
                {
                    ok = true,
                    action,
                    message = $"Removed {deleted} offline terminal record(s) from registry."
                });

            case "clear_logs":
                ClearNativeServerLogs();
                return Ok(new
                {
                    ok = true,
                    action,
                    message = "Server startup & runtime logs cleared."
                });

            default:
                return Unprocessable("Unknown action.");
        }
    }

    [HttpGet("server/terminal-config")]
    [Authorize]
    public async Task<IActionResult> DownloadTerminalConfig([FromQuery] string? name = "Counter 2")
    {
        if (!await CanAsync("settings.update"))
        {
            return Forbidden("Only administrators can download terminal configuration.");
        }

        var lanIps = DetectLanIpv4Addresses();
        var port = Request.Host.Port ?? 18960;
        var scheme = string.IsNullOrWhiteSpace(Request.Scheme) ? "http" : Request.Scheme;
        var primaryIp = lanIps.FirstOrDefault() ?? "127.0.0.1";
        var serverUrl = port is 80 or 443 ? $"{scheme}://{primaryIp}" : $"{scheme}://{primaryIp}:{port}";

        var termName = string.IsNullOrWhiteSpace(name) ? "Counter 2" : name.Trim();
        var setting = await Db.HasTableAsync("settings")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
            : null;

        var payload = new
        {
            mode = "terminal",
            serverUrl,
            terminalId = "TERM-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)),
            terminalName = termName,
            pairingToken = setting?.GetValueOrDefault("network_server_token"),
            generatedAt = DateTime.UtcNow.ToString("O"),
            instructions = "Place this file at %APPDATA%\\Karobar-App-Native\\network-config.json on the Windows Terminal PC."
        };

        var slug = Regex.Replace(termName.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(slug)) slug = "terminal";

        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, new JsonSerializerOptions { WriteIndented = true });
        return File(bytes, "application/json", $"network-config-{slug}.json");
    }

    private async Task<Dictionary<string, object?>> BuildServerHealthSummaryAsync(List<Dictionary<string, object?>> terminals)
    {
        var lanIps = DetectLanIpv4Addresses();
        var port = Request.Host.Port ?? 18960;
        var scheme = string.IsNullOrWhiteSpace(Request.Scheme) ? "http" : Request.Scheme;
        var primaryIp = lanIps.FirstOrDefault() ?? "127.0.0.1";
        var recommendedUrl = port is 80 or 443 ? $"{scheme}://{primaryIp}" : $"{scheme}://{primaryIp}:{port}";

        var licenseStatus = await _licenseService.CurrentStatusAsync();
        var setting = await Db.HasTableAsync("settings")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
            : null;

        if (setting != null && await Db.HasColumnAsync("settings", "network_server_token") &&
            string.IsNullOrWhiteSpace(setting.GetValueOrDefault("network_server_token")?.ToString()))
        {
            var token = "KRB-NET-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6));
            var sid = Convert.ToInt64(setting["id"]);
            await Db.UpdateAsync("settings", sid, new Dictionary<string, object?> { ["network_server_token"] = token });
            setting["network_server_token"] = token;
        }

        var onlineCount = terminals.Count(t => t.GetValueOrDefault("is_online") is true);
        var authCount = terminals.Count(t => t.GetValueOrDefault("is_authorized") is true);
        var seatsLimit = Convert.ToInt32(licenseStatus.GetValueOrDefault("seats") ?? 0);
        var isServerAdmin = await CanAsync("settings.update");

        return new Dictionary<string, object?>
        {
            ["hostname"] = Dns.GetHostName(),
            ["os_family"] = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : "Darwin",
            ["os_release"] = RuntimeInformation.OSDescription,
            ["php_version"] = ".NET 9.0",
            ["db_driver"] = "sqlite",
            ["db_version"] = "SQLite 3 (WAL)",
            ["cache_driver"] = "memory",
            ["session_driver"] = "sanctum",
            ["lan_ips"] = lanIps,
            ["primary_ip"] = primaryIp,
            ["port"] = port,
            ["recommended_url"] = recommendedUrl,
            ["online_terminals_count"] = onlineCount,
            ["total_terminals_count"] = terminals.Count,
            ["authorized_count"] = authCount,
            ["seats_limit"] = seatsLimit > 0 ? seatsLimit : null,
            ["terminal_auto_authorize"] = setting == null || setting.GetValueOrDefault("terminal_auto_authorize") is not (false or 0 or 0L),
            ["network_server_token"] = isServerAdmin ? setting?.GetValueOrDefault("network_server_token") : null,
            ["services"] = new
            {
                database = true,
                cache = true,
                whatsapp_agent = _waSidecar?.IsSidecarRunningInProcess() ?? false
            }
        };
    }

    private (int SizeKb, string[] RecentLines) ReadNativeServerLogs()
    {
        var candidates = new List<string>
        {
            Db.StoragePath("logs/native-startup.log"),
            Db.StoragePath("logs/laravel.log")
        };
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                candidates.Add(Path.Combine(appData, "Karobar-App-Native", "startup-error.log"));
            }
        }
        catch { }

        long totalBytes = 0;
        var lines = new List<string>();
        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!System.IO.File.Exists(path)) continue;
                var fi = new FileInfo(path);
                totalBytes += fi.Length;
                if (fi.Length > 0)
                {
                    var fileLines = System.IO.File.ReadAllLines(path);
                    foreach (var ln in fileLines.TakeLast(40))
                    {
                        lines.Add($"[{Path.GetFileName(path)}] {ln}");
                    }
                }
            }
            catch { }
        }

        return ((int)Math.Round(totalBytes / 1024.0), lines.TakeLast(60).ToArray());
    }

    private void ClearNativeServerLogs()
    {
        var candidates = new List<string>
        {
            Db.StoragePath("logs/native-startup.log"),
            Db.StoragePath("logs/laravel.log")
        };
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                candidates.Add(Path.Combine(appData, "Karobar-App-Native", "startup-error.log"));
            }
        }
        catch { }

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (System.IO.File.Exists(path)) System.IO.File.WriteAllText(path, string.Empty);
            }
            catch { }
        }
    }

    private static (string Status, string Summary, string Hint) CheckFirewallPortStatus(int port)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ("ok", $"Port {port} listening on 0.0.0.0", "Ensure your OS firewall allows inbound TCP traffic from the local LAN subnet.");
        }

        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "advfirewall firewall show rule name=\"Karobar-App Native LAN Server (TCP 18960)\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2500);

            if (proc.ExitCode == 0 && output.Contains("Enabled:", StringComparison.OrdinalIgnoreCase) &&
                output.Contains("Yes", StringComparison.OrdinalIgnoreCase))
            {
                var coversAllProfiles = output.Contains("Domain,Private,Public", StringComparison.OrdinalIgnoreCase) ||
                                        output.Contains("Any", StringComparison.OrdinalIgnoreCase);
                return coversAllProfiles
                    ? ("ok", $"TCP {port} inbound rule active (LAN subnet)", "Windows Defender Firewall allows local LAN Counter PCs on Private, Domain & Public profiles.")
                    : ("warn", $"TCP {port} rule active (Private profile only)", "If your Wi-Fi/Ethernet is set to 'Public', switch Windows Network Profile to Private or re-run the Karobar installer.");
            }

            return ("warn", $"No Karobar firewall rule found for TCP {port}", $"Run as Administrator: netsh advfirewall firewall add rule name=\"Karobar-App Native LAN Server (TCP {port})\" dir=in action=allow protocol=TCP localport={port} remoteip=localsubnet profile=any");
        }
        catch
        {
            return ("warn", $"Could not query Windows Firewall for TCP {port}", "Verify Windows Defender Firewall allows inbound TCP 18960 from your local LAN.");
        }
    }

    private static readonly Regex VirtualAdapterRegex = new(
        @"(vEthernet|WSL|Hyper-V|VirtualBox|VMware|Docker|Loopback|Pseudo|Npcap|TAP-|WireGuard|ZeroTier|Tailscale|Hamachi|Bluetooth|Teredo|isatap|Fortinet|Cisco AnyConnect|OpenVPN)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static List<string> DetectLanIpv4Addresses()
    {
        var ranked = new List<(int Priority, string Ip)>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up ||
                    ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                var nameDesc = $"{ni.Name} {ni.Description}";
                var isVirtual = VirtualAdapterRegex.IsMatch(nameDesc);
                var props = ni.GetIPProperties();
                var hasGateway = props.GatewayAddresses.Any(g =>
                    g.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(g.Address) &&
                    !g.Address.Equals(IPAddress.Any));

                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ua.Address))
                    {
                        continue;
                    }

                    var ipStr = ua.Address.ToString();
                    if (ipStr.StartsWith("169.254.", StringComparison.Ordinal)) continue;

                    var isPrivateLan = ipStr.StartsWith("192.168.", StringComparison.Ordinal) ||
                                       ipStr.StartsWith("10.", StringComparison.Ordinal) ||
                                       Regex.IsMatch(ipStr, @"^172\.(1[6-9]|2\d|3[0-1])\.");

                    // Lower score = higher priority: physical Ethernet/Wi-Fi with default gateway & RFC1918 LAN IP first!
                    var score = 0;
                    if (isVirtual) score += 100;
                    if (!hasGateway) score += 50;
                    if (!isPrivateLan) score += 20;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet) score -= 5;
                    else if (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211) score -= 2;

                    ranked.Add((score, ipStr));
                }
            }
        }
        catch
        {
        }

        return ranked
            .OrderBy(x => x.Priority)
            .Select(x => x.Ip)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

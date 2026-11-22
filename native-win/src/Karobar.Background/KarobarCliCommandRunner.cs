using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Karobar.Core;
using Karobar.Core.Entities;
using Karobar.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;

namespace Karobar.Background;

public sealed class CliCommandResult
{
    public int ExitCode { get; set; }
    public bool Ok => ExitCode == 0;
    public string Output { get; set; } = "";
    public Dictionary<string, object?> Data { get; set; } = new();
}

/// <summary>
/// Executes all 11 console commands from `app/Console/Commands/*`:
/// 1. `karobar:flush-walkin-credit` (`flush-walk-in-credit`)
/// 2. `karobar:apply-patch` (`apply-patch`)
/// 3. `karobar:export-sqlite` (`export-sqlite`)
/// 4. `karobar:import-sqlite` (`import-sqlite`)
/// 5. `karobar:install` (`install`)
/// 6. `license:issue`
/// 7. `license:machine-id`
/// 8. `license:verify-installer`
/// 9. `expenses:process-recurring`
/// 10. `backup:run-scheduled`
/// 11. `demands:notify`
/// </summary>
public sealed class KarobarCliCommandRunner
{
    public static readonly string[] AllSupportedCommands =
    [
        "flush-walk-in-credit",
        "karobar:flush-walkin-credit",
        "apply-patch",
        "karobar:apply-patch",
        "export-sqlite",
        "karobar:export-sqlite",
        "import-sqlite",
        "karobar:import-sqlite",
        "install",
        "karobar:install",
        "license:issue",
        "license:machine-id",
        "license:verify-installer",
        "expenses:process-recurring",
        "backup:run-scheduled",
        "karobar:backup",
        "demands:notify"
    ];

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public const string MasterPublicKeyB64u = HardwareMachineIdProvider.MasterPublicKeyB64u;

    public static readonly IReadOnlyDictionary<string, Dictionary<string, object>> LicenseTiers = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase)
    {
        ["basic"] = new()
        {
            ["label"] = "Basic Edition",
            ["edition"] = "Basic",
            ["tagline"] = "Single-Counter Retail & Pharmacy POS",
            ["default_seats"] = 1,
            ["modules"] = new[] { "pos", "inventory", "reports", "barcode" }
        },
        ["gold"] = new()
        {
            ["label"] = "Gold Edition",
            ["edition"] = "Gold",
            ["tagline"] = "Advanced Pharmacy & Distribution ERP with Accounting & HR",
            ["default_seats"] = 1,
            ["modules"] = new[] { "pos", "inventory", "reports", "barcode", "accounting_gl", "hr_payroll", "cube_ai" }
        },
        ["platinum"] = new()
        {
            ["label"] = "Platinum Enterprise Edition",
            ["edition"] = "Platinum",
            ["tagline"] = "Full Multi-PC LAN Server, FBR Fiscalization, Cube AI & All Modules",
            ["default_seats"] = 0,
            ["modules"] = new[] { "pos", "inventory", "reports", "barcode", "accounting_gl", "hr_payroll", "fbr", "cube_ai", "server_terminals" }
        }
    };

    private readonly string _appRoot;
    private readonly WaseelaAndSqliteImportEngine _importEngine;
    private readonly ILogger<KarobarCliCommandRunner>? _logger;

    public KarobarCliCommandRunner(
        string? appRoot = null,
        WaseelaAndSqliteImportEngine? importEngine = null,
        ILogger<KarobarCliCommandRunner>? logger = null)
    {
        _appRoot = ResolveAppRoot(appRoot);
        _importEngine = importEngine ?? new WaseelaAndSqliteImportEngine(_appRoot);
        _logger = logger;
    }

    private static string ResolveAppRoot(string? explicitRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot) && Directory.Exists(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot);
        }
        var envRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH")
            ?? Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
        {
            return Path.GetFullPath(envRoot);
        }
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "database")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Entrypoint called directly by CommandLineRouter for headless `--cli &lt;command&gt; [args...]` execution.
    /// Parses CLI options, handles DB-free pre-install commands in &lt;50ms, and bootstraps SQLite for DB commands.
    /// </summary>
    public async Task<int> RunAsync(string[] cliArgs, CancellationToken cancellationToken = default)
    {
        if (cliArgs == null || cliArgs.Length == 0 || string.IsNullOrWhiteSpace(cliArgs[0]))
        {
            Console.WriteLine("Karobar-App Native CLI Runner");
            return 0;
        }

        var commandName = cliArgs[0].Trim().ToLowerInvariant();
        var parsedArgs = ParseCliArguments(cliArgs.Skip(1).ToArray());

        CliCommandResult result;
        switch (commandName)
        {
            case "license:machine-id":
                result = ExecuteLicenseMachineId(parsedArgs);
                break;

            case "license:verify-installer":
                result = ExecuteLicenseVerifyInstaller(parsedArgs);
                break;

            case "license:issue":
                result = ExecuteLicenseIssue(parsedArgs);
                break;

            case "apply-patch":
            case "karobar:apply-patch":
                result = await ExecuteApplyPatchAsync(parsedArgs, cancellationToken);
                break;

            case "export-sqlite":
            case "karobar:export-sqlite":
                result = await ExecuteExportSqliteAsync(parsedArgs, cancellationToken);
                break;

            default:
            {
                // For `install`, handle optional pre-migration SQLite database copy BEFORE EF opens the file
                var importedExistingDb = false;
                if (commandName is "install" or "karobar:install")
                {
                    importedExistingDb = PreparePreInstallSqliteImport(parsedArgs);
                    if (importedExistingDb)
                    {
                        parsedArgs["_imported_existing_db"] = "1";
                    }
                }

                var dbPath = Path.Combine(_appRoot, "database", "database.sqlite");
                var factory = new DbConnectionFactory(explicitSqlitePath: dbPath);
                var bootstrapper = new DatabaseBootstrapper(factory);
                await bootstrapper.EnsureSchemaReadyAsync(cancellationToken);

                await using var db = DatabaseBootstrapper.CreateDbContext(dbPath);
                result = await RunCommandAsync(db, commandName, parsedArgs, cancellationToken);
                break;
            }
        }

        if (!string.IsNullOrWhiteSpace(result.Output))
        {
            if (result.ExitCode == 0)
            {
                Console.WriteLine(result.Output);
            }
            else
            {
                Console.Error.WriteLine(result.Output);
            }
        }

        return result.ExitCode;
    }

    public static Dictionary<string, string?> ParseCliArguments(string[] rawArgs)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rawArgs.Length; i++)
        {
            var token = rawArgs[i]?.Trim() ?? "";
            if (string.IsNullOrEmpty(token)) continue;

            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                var body = token[2..];
                var eqIdx = body.IndexOf('=');
                if (eqIdx > 0)
                {
                    var key = body[..eqIdx].Trim();
                    var val = body[(eqIdx + 1)..].Trim().Trim('"');
                    dict[key] = val;
                }
                else
                {
                    var key = body.Trim();
                    if (i + 1 < rawArgs.Length && !rawArgs[i + 1].StartsWith("-", StringComparison.Ordinal))
                    {
                        dict[key] = rawArgs[++i].Trim().Trim('"');
                    }
                    else
                    {
                        dict[key] = "true";
                    }
                }
            }
        }
        return dict;
    }

    private bool PreparePreInstallSqliteImport(IDictionary<string, string?> args)
    {
        var isUpgradeOnly = IsTruthyArg(args, "upgrade-only");
        var isFreshDb = IsTruthyArg(args, "fresh-db");
        var importPath = args.TryGetValue("import-sqlite", out var imp) ? (imp ?? "").Trim() : "";

        if (args.TryGetValue("admin-config", out var adminCfgPath) && !string.IsNullOrWhiteSpace(adminCfgPath) && File.Exists(adminCfgPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(adminCfgPath, Encoding.UTF8));
                if (doc.RootElement.TryGetProperty("upgrade_only", out var uo) && uo.ValueKind == JsonValueKind.True)
                {
                    isUpgradeOnly = true;
                }
                if (doc.RootElement.TryGetProperty("fresh_db", out var fd) && fd.ValueKind == JsonValueKind.True)
                {
                    isFreshDb = true;
                }
                if (!isUpgradeOnly && string.IsNullOrWhiteSpace(importPath) &&
                    doc.RootElement.TryGetProperty("import_sqlite", out var impProp) &&
                    impProp.ValueKind == JsonValueKind.String)
                {
                    importPath = (impProp.GetString() ?? "").Trim();
                }
            }
            catch
            {
                // Ignore malformed admin-config here; handled later
            }
        }

        var destDb = Path.Combine(_appRoot, "database", "database.sqlite");
        var destDir = Path.GetDirectoryName(destDb);
        if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

        // Fresh Database requested on an existing installation: safely archive the existing DB first,
        // then clear database.sqlite (+ WAL/SHM) so EnsureSchemaReadyAsync creates a truly clean database.
        if (isFreshDb && !isUpgradeOnly && string.IsNullOrWhiteSpace(importPath))
        {
            if (File.Exists(destDb))
            {
                try
                {
                    var backupDir = Path.Combine(_appRoot, "storage", "app", "backups");
                    Directory.CreateDirectory(backupDir);
                    var archivePath = Path.Combine(backupDir, $"pre-fresh-install-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sqlite");
                    using var srcConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={destDb};Mode=ReadOnly;Pooling=False");
                    using var dstConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={archivePath};Mode=ReadWriteCreate;Pooling=False");
                    srcConn.Open();
                    dstConn.Open();
                    srcConn.BackupDatabase(dstConn);
                }
                catch
                {
                    // Best-effort raw fallback copy if SQLite online backup fails on a corrupt source file
                    try
                    {
                        var backupDir = Path.Combine(_appRoot, "storage", "app", "backups");
                        Directory.CreateDirectory(backupDir);
                        var archivePath = Path.Combine(backupDir, $"pre-fresh-install-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sqlite");
                        File.Copy(destDb, archivePath, overwrite: true);
                    }
                    catch { }
                }

                try { File.Delete(destDb); } catch { }
                try { if (File.Exists(destDb + "-wal")) File.Delete(destDb + "-wal"); } catch { }
                try { if (File.Exists(destDb + "-shm")) File.Delete(destDb + "-shm"); } catch { }
            }
            return false;
        }

        if (isUpgradeOnly || string.IsNullOrWhiteSpace(importPath) || !File.Exists(importPath))
        {
            return false;
        }

        if (!string.Equals(Path.GetFullPath(importPath), Path.GetFullPath(destDb), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(importPath, destDb, overwrite: true);
            try { if (File.Exists(destDb + "-wal")) File.Delete(destDb + "-wal"); } catch { }
            try { if (File.Exists(destDb + "-shm")) File.Delete(destDb + "-shm"); } catch { }

            // Preserve any uncheckpointed sibling -wal/-shm frames from the source DB (e.g. recent Sale Invoices)
            if (File.Exists(importPath + "-wal"))
            {
                try { File.Copy(importPath + "-wal", destDb + "-wal", overwrite: true); } catch { }
            }
            if (File.Exists(importPath + "-shm"))
            {
                try { File.Copy(importPath + "-shm", destDb + "-shm", overwrite: true); } catch { }
            }

            try
            {
                using var ckConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={destDb};Mode=ReadWrite;Pooling=False");
                ckConn.Open();
                using var ckCmd = ckConn.CreateCommand();
                ckCmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                ckCmd.ExecuteNonQuery();
            }
            catch { }
        }

        return true;
    }

    private static bool IsTruthyArg(IDictionary<string, string?> args, string key)
    {
        return args.TryGetValue(key, out var val) &&
               !string.IsNullOrWhiteSpace(val) &&
               val.Trim().ToLowerInvariant() is "1" or "true" or "yes";
    }

    public async Task<CliCommandResult> RunCommandAsync(
        KarobarDbContext db,
        string commandName,
        IDictionary<string, string?>? args = null,
        CancellationToken cancellationToken = default)
    {
        args ??= new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var normalized = (commandName ?? "").Trim().ToLowerInvariant();

        return normalized switch
        {
            "flush-walk-in-credit" or "karobar:flush-walkin-credit" or "walkin:flush-credit" or "customers:flush-walkin-credit"
                => await ExecuteFlushWalkInCreditAsync(db, args, cancellationToken),

            "apply-patch" or "karobar:apply-patch"
                => await ExecuteApplyPatchAsync(args, cancellationToken),

            "export-sqlite" or "karobar:export-sqlite"
                => await ExecuteExportSqliteAsync(args, cancellationToken),

            "import-sqlite" or "karobar:import-sqlite"
                => await ExecuteImportSqliteAsync(db, args, cancellationToken),

            "install" or "karobar:install"
                => await ExecuteInstallAsync(db, args, cancellationToken),

            "license:issue"
                => ExecuteLicenseIssue(args),

            "license:machine-id"
                => ExecuteLicenseMachineId(args),

            "license:verify-installer"
                => ExecuteLicenseVerifyInstaller(args),

            "expenses:process-recurring"
                => await ExecuteProcessRecurringExpensesAsync(db, args, cancellationToken),

            "backup:run-scheduled" or "karobar:backup"
                => await ExecuteRunScheduledBackupAsync(db, args, cancellationToken),

            "demands:notify"
                => await ExecuteSendUserDemandNotificationsAsync(db, args, cancellationToken),

            _ => new CliCommandResult
            {
                ExitCode = 1,
                Output = $"Unknown CLI command: {commandName}"
            }
        };
    }

    /// <summary>
    /// 1. `karobar:flush-walkin-credit` (`FlushWalkInCreditCommand.php`)
    /// </summary>
    public async Task<CliCommandResult> ExecuteFlushWalkInCreditAsync(
        KarobarDbContext db,
        IDictionary<string, string?> args,
        CancellationToken cancellationToken = default)
    {
        var dryRun = args.TryGetValue("dry-run", out var dr) && dr is "1" or "true" or "yes";

        var allCustomers = await db.Customers.ToListAsync(cancellationToken);
        var walkInCustomers = allCustomers.Where(c => Customer.IsWalkInName(c.Name)).ToList();
        var walkInIds = walkInCustomers.Select(c => c.Id).ToHashSet();

        var unpaidInvoices = await db.SaleInvoices
            .Where(i => (i.CustomerId == 0 || walkInIds.Contains(i.CustomerId)) &&
                        (i.InvoiceType == "credit" || i.TotalReceive < i.Total))
            .ToListAsync(cancellationToken);

        var invoicesFlushedCount = 0;
        var unpaidAmountFlushed = 0m;
        var flushedInvoiceIds = new HashSet<int>();

        foreach (var inv in unpaidInvoices)
        {
            var isReversed = string.Equals(inv.Status, "reversed", StringComparison.OrdinalIgnoreCase);
            var unpaid = isReversed ? 0m : Math.Max(0m, Math.Round(inv.Total - inv.TotalReceive, 2));
            unpaidAmountFlushed += unpaid;
            invoicesFlushedCount++;
            flushedInvoiceIds.Add(inv.Id);

            if (!dryRun)
            {
                inv.InvoiceType = "debit";
                inv.TotalReceive = isReversed ? 0m : inv.Total;
                inv.DueDate = null;
            }
        }

        var ledgerEntries = await db.CustomerLedgers
            .Where(l => walkInIds.Contains(l.CustomerId) ||
                        (l.SaleInvoiceId != null && flushedInvoiceIds.Contains(l.SaleInvoiceId.Value)))
            .ToListAsync(cancellationToken);

        var ledgerRowsDeletedCount = ledgerEntries.Count;
        if (!dryRun)
        {
            if (ledgerEntries.Count > 0)
            {
                db.CustomerLedgers.RemoveRange(ledgerEntries);
            }
            if (invoicesFlushedCount > 0 || ledgerRowsDeletedCount > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        var data = new Dictionary<string, object?>
        {
            ["walk_in_customers"] = walkInCustomers.Select(c => new { id = c.Id, name = c.Name }).ToList(),
            ["invoices_flushed_count"] = invoicesFlushedCount,
            ["unpaid_amount_flushed"] = unpaidAmountFlushed,
            ["ledger_rows_deleted_count"] = ledgerRowsDeletedCount
        };

        return new CliCommandResult
        {
            ExitCode = 0,
            Output = dryRun
                ? $"[DRY RUN] Would flush {invoicesFlushedCount} Walk-In invoices (Rs. {unpaidAmountFlushed:N2}) and {ledgerRowsDeletedCount} ledger entries."
                : $"Flushed {invoicesFlushedCount} Walk-In invoices (Rs. {unpaidAmountFlushed:N2}) and removed {ledgerRowsDeletedCount} ledger entries.",
            Data = data
        };
    }

    /// <summary>
    /// 2. `karobar:apply-patch` (`KarobarApplyPatchCommand.php`)
    /// </summary>
    public async Task<CliCommandResult> ExecuteApplyPatchAsync(
        IDictionary<string, string?> args,
        CancellationToken cancellationToken = default)
    {
        var patchZip = args.TryGetValue("patch_zip", out var pz) ? (pz ?? "").Trim() : "";
        if (string.IsNullOrEmpty(patchZip) || !File.Exists(patchZip))
        {
            return new CliCommandResult
            {
                ExitCode = 1,
                Output = $"Patch file not found: {patchZip}",
                Data = new Dictionary<string, object?> { ["ok"] = false, ["patch_applied"] = false, ["message"] = $"Patch file not found: {patchZip}" }
            };
        }

        if (!args.TryGetValue("verify-sha", out var expectedSha) ||
            string.IsNullOrWhiteSpace(expectedSha) ||
            !Regex.IsMatch(expectedSha.Trim(), "^[0-9a-fA-F]{64}$"))
        {
            return new CliCommandResult
            {
                ExitCode = 1,
                Output = "Missing or invalid required --verify-sha SHA-256 checksum.",
                Data = new Dictionary<string, object?>
                {
                    ["ok"] = false,
                    ["patch_applied"] = false,
                    ["message"] = "Missing or invalid required --verify-sha SHA-256 checksum."
                }
            };
        }

        var bytes = await File.ReadAllBytesAsync(patchZip, cancellationToken);
        var actualSha = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actualSha, expectedSha.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return new CliCommandResult
            {
                ExitCode = 1,
                Output = $"SHA-256 checksum mismatch. Expected: {expectedSha}, Actual: {actualSha}",
                Data = new Dictionary<string, object?> { ["ok"] = false, ["patch_applied"] = false }
            };
        }

        // Pre-patch safety backup (WAL-safe SQLite Online Backup API)
        var dbPath = Path.Combine(_appRoot, "database", "database.sqlite");
        if (File.Exists(dbPath))
        {
            var backupDir = Path.Combine(_appRoot, "storage", "backups");
            Directory.CreateDirectory(backupDir);
            var backupFile = Path.Combine(backupDir, $"pre-patch-{DateTime.Now:yyyyMMdd-HHmmss}.sqlite");
            try
            {
                await BackupSqliteOnlineAsync(dbPath, backupFile, cancellationToken);
            }
            catch
            {
                try { File.Copy(dbPath, backupFile, overwrite: true); } catch { }
            }
        }

        var stagingDir = Path.Combine(Path.GetTempPath(), $"karobar-patch-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDir);

        try
        {
            ZipFile.ExtractToDirectory(patchZip, stagingDir, overwriteFiles: true);
            var appliedFiles = 0;

            var fullRoot = Path.GetFullPath(_appRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var file in Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(stagingDir, file).Replace('\\', '/');
                var allowWhatsAppScript = rel.Equals("runtime/whatsapp-agent/whatsapp-agent.mjs", StringComparison.OrdinalIgnoreCase);
                if (!allowWhatsAppScript &&
                    (rel.StartsWith("storage/", StringComparison.OrdinalIgnoreCase) ||
                     rel.StartsWith("runtime/", StringComparison.OrdinalIgnoreCase) ||
                     rel.Contains("database.sqlite", StringComparison.OrdinalIgnoreCase) ||
                     rel.StartsWith(".env", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var targetPath = Path.GetFullPath(Path.Combine(_appRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (!targetPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(file, targetPath, overwrite: true);
                appliedFiles++;
            }

            var prunedAssets = PruneStaleBuildAssets(_appRoot, stagingDir);

            var data = new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["patch_applied"] = true,
                ["files_updated"] = appliedFiles,
                ["pruned_assets"] = prunedAssets,
                ["message"] = "Differential patch applied successfully with zero data loss."
            };

            return new CliCommandResult
            {
                ExitCode = 0,
                Output = JsonSerializer.Serialize(data),
                Data = data
            };
        }
        finally
        {
            try { if (Directory.Exists(stagingDir)) Directory.Delete(stagingDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Prunes obsolete hashed Vite assets under <c>public/build/assets/</c> when the incoming staged patch
    /// includes <c>public/build/assets/</c> or <c>public/build/manifest.json</c>. Never deletes
    /// <c>public/index.html</c>, <c>public/version.json</c>, <c>public/build/manifest.json</c>, or any file outside
    /// <c>public/build/assets/</c>.
    /// </summary>
    public static int PruneStaleBuildAssets(string appRoot, string stagedRoot)
    {
        if (string.IsNullOrWhiteSpace(appRoot) || string.IsNullOrWhiteSpace(stagedRoot))
        {
            return 0;
        }

        var targetAssetsDir = Path.Combine(appRoot, "public", "build", "assets");
        if (!Directory.Exists(targetAssetsDir))
        {
            return 0;
        }

        var stagedAssetsDir = Path.Combine(stagedRoot, "public", "build", "assets");
        var stagedManifestPath = Path.Combine(stagedRoot, "public", "build", "manifest.json");
        var hasStagedAssetsDir = Directory.Exists(stagedAssetsDir);
        var hasStagedManifest = File.Exists(stagedManifestPath);

        if (!hasStagedAssetsDir && !hasStagedManifest)
        {
            return 0;
        }

        var keepRelativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (hasStagedAssetsDir)
        {
            foreach (var stagedFile in Directory.GetFiles(stagedAssetsDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(stagedAssetsDir, stagedFile).Replace('\\', '/').TrimStart('/');
                if (!string.IsNullOrWhiteSpace(rel))
                {
                    keepRelativePaths.Add(rel);
                }
            }
        }

        if (hasStagedManifest)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(stagedManifestPath));
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    if (!hasStagedAssetsDir)
                    {
                        return 0;
                    }
                }
                else
                {
                    foreach (var entryProp in doc.RootElement.EnumerateObject())
                    {
                        var entry = entryProp.Value;
                        if (entry.ValueKind != JsonValueKind.Object) continue;

                        if (entry.TryGetProperty("file", out var fileEl) && fileEl.ValueKind == JsonValueKind.String)
                        {
                            AddManifestAssetPath(fileEl.GetString(), keepRelativePaths);
                        }

                        foreach (var arrayPropName in new[] { "css", "assets" })
                        {
                            if (entry.TryGetProperty(arrayPropName, out var arrEl) && arrEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var item in arrEl.EnumerateArray())
                                {
                                    if (item.ValueKind == JsonValueKind.String)
                                    {
                                        AddManifestAssetPath(item.GetString(), keepRelativePaths);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // If manifest.json is unreadable and no staged assets dir exists, fail safe without deleting everything.
                if (!hasStagedAssetsDir)
                {
                    return 0;
                }
            }
        }

        var targetAssetsFull = Path.GetFullPath(targetAssetsDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var pruned = 0;

        foreach (var existingFile in Directory.GetFiles(targetAssetsDir, "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(existingFile);
            if (!fullPath.StartsWith(targetAssetsFull, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var rel = Path.GetRelativePath(targetAssetsDir, existingFile).Replace('\\', '/').TrimStart('/');
            if (!keepRelativePaths.Contains(rel))
            {
                try
                {
                    File.Delete(existingFile);
                    pruned++;
                }
                catch
                {
                    // Best-effort cleanup of stale chunk
                }
            }
        }

        try
        {
            foreach (var dir in Directory.GetDirectories(targetAssetsDir, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir, recursive: false);
                }
            }
        }
        catch
        {
            // Ignore empty dir cleanup errors
        }

        return pruned;
    }

    private static void AddManifestAssetPath(string? rawPath, HashSet<string> keepRelativePaths)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return;
        var norm = rawPath.Replace('\\', '/').TrimStart('/');
        const string assetsPrefix = "assets/";
        if (norm.StartsWith(assetsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            norm = norm[assetsPrefix.Length..];
        }
        if (!string.IsNullOrWhiteSpace(norm))
        {
            keepRelativePaths.Add(norm);
        }
    }

    private static async Task BackupSqliteOnlineAsync(string sourceDb, string destinationDb, CancellationToken cancellationToken)
    {
        await using var srcConn = new SqliteConnection($"Data Source={sourceDb};Mode=ReadOnly;Pooling=False;");
        await using var dstConn = new SqliteConnection($"Data Source={destinationDb};Mode=ReadWriteCreate;Pooling=False;");
        await srcConn.OpenAsync(cancellationToken);
        await dstConn.OpenAsync(cancellationToken);
        await using (var cmd = srcConn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA busy_timeout=15000;";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        srcConn.BackupDatabase(dstConn);
    }

    /// <summary>
    /// 3. `karobar:export-sqlite` (`KarobarExportSqliteCommand.php`)
    /// </summary>
    public async Task<CliCommandResult> ExecuteExportSqliteAsync(
        IDictionary<string, string?> args,
        CancellationToken cancellationToken = default)
    {
        var sourceDb = Path.Combine(_appRoot, "database", "database.sqlite");
        var outPath = args.TryGetValue("output", out var o) && !string.IsNullOrWhiteSpace(o)
            ? o.Trim()
            : Path.Combine(_appRoot, "storage", "app", $"karobar-export-{DateTime.Now:yyyyMMdd-HHmmss}.sqlite");

        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        if (File.Exists(sourceDb))
        {
            try
            {
                await BackupSqliteOnlineAsync(sourceDb, outPath, cancellationToken);
            }
            catch
            {
                File.Copy(sourceDb, outPath, overwrite: true);
            }
        }
        else
        {
            File.WriteAllBytes(outPath, []);
        }

        var sizeMb = File.Exists(outPath) ? Math.Round(new FileInfo(outPath).Length / (1024.0 * 1024.0), 2) : 0;
        return new CliCommandResult
        {
            ExitCode = 0,
            Output = $"Portable SQLite database exported successfully ({sizeMb} MB): {outPath}",
            Data = new Dictionary<string, object?> { ["ok"] = true, ["path"] = outPath, ["size_mb"] = sizeMb }
        };
    }

    /// <summary>
    /// 4. `karobar:import-sqlite` (`KarobarImportSqliteCommand.php`)
    /// </summary>
    public async Task<CliCommandResult> ExecuteImportSqliteAsync(
        KarobarDbContext db,
        IDictionary<string, string?> args,
        CancellationToken cancellationToken = default)
    {
        var filePath = args.TryGetValue("file", out var f) ? (f ?? "").Trim() : "";
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return new CliCommandResult
            {
                ExitCode = 1,
                Output = $"Source SQLite file not found or not readable: {filePath}"
            };
        }

        var mode = args.TryGetValue("mode", out var m) && !string.IsNullOrWhiteSpace(m) ? m.Trim() : "merge";
        var staged = await _importEngine.StageDatabaseFromFileAsync(filePath, Path.GetFileName(filePath), cancellationToken);
        var token = staged["token"]?.ToString() ?? "";
        try
        {
            var inspection = await _importEngine.InspectAndProposeMappingAsync(token, cancellationToken);
            var importSummary = await _importEngine.ExecuteImportAsync(db, token, mode, cancellationToken);
            var reconciliation = importSummary.TryGetValue("reconciliation", out var recObj) ? recObj : null;

            return new CliCommandResult
            {
                ExitCode = 0,
                Output = $"Import completed! Imported {importSummary["total_rows_inserted"]} row(s) across {importSummary["tables_imported"]} table(s).",
                Data = new Dictionary<string, object?>
                {
                    ["ok"] = true,
                    ["inspection"] = inspection,
                    ["import"] = importSummary,
                    ["reconciliation"] = reconciliation
                }
            };
        }
        finally
        {
            _importEngine.CleanupStagedSession(token);
        }
    }

    /// <summary>
    /// 5. `karobar:install` (`KarobarInstallCommand.php`)
    /// Full Inno Setup post-install / upgrade bootstrapper: reads --admin-config, --seal-file / --license-key,
    /// creates Admin account + Admin role binding, activates &amp; seals hardware-bound/unbound license in `licenses`,
    /// seeds default ThemeSetting &amp; Setting legal timestamps, and cleans up temporary files.
    /// </summary>
    public async Task<CliCommandResult> ExecuteInstallAsync(
        KarobarDbContext db,
        IDictionary<string, string?> args,
        CancellationToken cancellationToken = default,
        byte[]? testPublicKeyOverride = null)
    {
        var isUpgradeOnly = IsTruthyArg(args, "upgrade-only");
        var importedExistingDb = IsTruthyArg(args, "_imported_existing_db");
        var machineId = ComputeMachineId();

        var adminEmail = args.TryGetValue("admin-email", out var ae) && !string.IsNullOrWhiteSpace(ae) ? ae.Trim() : "admin@karobar.local";
        var adminName = args.TryGetValue("admin-name", out var an) && !string.IsNullOrWhiteSpace(an) ? an.Trim() : "Store Administrator";
        var adminPassword = args.TryGetValue("admin-password", out var ap) && !string.IsNullOrWhiteSpace(ap) ? ap : "password";

        // 1. Read --admin-config JSON written by Inno Setup wizard
        if (args.TryGetValue("admin-config", out var adminCfgPath) && !string.IsNullOrWhiteSpace(adminCfgPath) && File.Exists(adminCfgPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(adminCfgPath, Encoding.UTF8));
                var root = doc.RootElement;
                if (root.TryGetProperty("upgrade_only", out var uo) && uo.ValueKind == JsonValueKind.True)
                {
                    isUpgradeOnly = true;
                }
                if (root.TryGetProperty("name", out var nProp) && nProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(nProp.GetString()))
                {
                    adminName = nProp.GetString()!.Trim();
                }
                if (root.TryGetProperty("email", out var eProp) && eProp.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(eProp.GetString()))
                {
                    adminEmail = eProp.GetString()!.Trim();
                }
                if (root.TryGetProperty("password", out var pProp) && pProp.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(pProp.GetString()))
                {
                    adminPassword = pProp.GetString()!;
                }
            }
            catch
            {
                // Keep defaults / explicit args
            }
        }

        adminEmail = adminEmail.Trim().ToLowerInvariant();

        // 2. If an existing Herd/Karobar SQLite DB was imported, clear ONLY users & licenses while preserving all store data
        if (importedExistingDb && !isUpgradeOnly)
        {
            db.Licenses.RemoveRange(await db.Licenses.ToListAsync(cancellationToken));
            db.PersonalAccessTokens.RemoveRange(await db.PersonalAccessTokens.ToListAsync(cancellationToken));
            db.ModelHasRoles.RemoveRange(await db.ModelHasRoles.ToListAsync(cancellationToken));
            db.Users.RemoveRange(await db.Users.ToListAsync(cancellationToken));
            await db.SaveChangesAsync(cancellationToken);
        }

        // 3. Seed essential Admin role & default ThemeSetting
        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin", cancellationToken);
        if (adminRole == null)
        {
            adminRole = new Role { Name = "Admin", GuardName = "sanctum", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            db.Roles.Add(adminRole);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.ThemeSettings.AnyAsync(cancellationToken))
        {
            db.ThemeSettings.Add(new ThemeSetting
            {
                Name = "Default Theme",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        // 4. Zero-data-loss upgrade path: take WAL-safe pre-upgrade snapshot & preserve existing users, license, and settings untouched
        if (isUpgradeOnly)
        {
            var existingDbPath = Path.Combine(_appRoot, "database", "database.sqlite");
            if (File.Exists(existingDbPath) && await db.Users.AnyAsync(cancellationToken))
            {
                try
                {
                    var preUpgradeDir = Path.Combine(_appRoot, "storage", "app", "backups");
                    Directory.CreateDirectory(preUpgradeDir);
                    var preSnap = Path.Combine(preUpgradeDir, $"pre-upgrade-{DateTime.UtcNow:yyyyMMdd-HHmmss}.sqlite");
                    using var srcConn = new SqliteConnection($"Data Source={existingDbPath};Mode=ReadOnly;Pooling=False;");
                    using var dstConn = new SqliteConnection($"Data Source={preSnap};Mode=ReadWriteCreate;Pooling=False;");
                    await srcConn.OpenAsync(cancellationToken);
                    await dstConn.OpenAsync(cancellationToken);
                    srcConn.BackupDatabase(dstConn);
                }
                catch { }
            }

            if (IsTruthyArg(args, "cleanup-temp"))
            {
                CleanupInstallerTempFiles(args);
            }

            var firstAdmin = await db.Users.OrderBy(u => u.Id).FirstOrDefaultAsync(cancellationToken);
            var existingSetting = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            var upgData = new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["upgraded"] = true,
                ["admin_email"] = firstAdmin?.Email,
                ["admin_name"] = firstAdmin?.Name,
                ["machine_id"] = machineId,
                ["licensed"] = await db.Licenses.AnyAsync(cancellationToken),
                ["store_name"] = existingSetting?.StoreName,
                ["is_setup_completed"] = existingSetting?.IsSetupCompleted ?? true
            };
            return new CliCommandResult
            {
                ExitCode = 0,
                Output = JsonSerializer.Serialize(upgData),
                Data = upgData
            };
        }

        // 5. Create or update initial Administrator user with Laravel-compatible $2y$ bcrypt hash
        var bcryptHash = BCrypt.Net.BCrypt.HashPassword(adminPassword, 10);
        if (bcryptHash.StartsWith("$2a$", StringComparison.Ordinal) || bcryptHash.StartsWith("$2b$", StringComparison.Ordinal))
        {
            bcryptHash = "$2y$" + bcryptHash[4..];
        }

        var adminUser = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == adminEmail, cancellationToken);
        if (adminUser == null)
        {
            adminUser = new User
            {
                Name = adminName,
                Email = adminEmail,
                Password = bcryptHash,
                Status = "active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Users.Add(adminUser);
        }
        else
        {
            adminUser.Name = adminName;
            adminUser.Password = bcryptHash;
            adminUser.Status = "active";
            adminUser.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);

        // Assign Admin role in Spatie `model_has_roles` table
        const string userModelType = @"App\Models\User";
        var hasAdminRole = await db.ModelHasRoles.AnyAsync(
            m => m.RoleId == adminRole.Id && m.ModelId == adminUser.Id && m.ModelType == userModelType,
            cancellationToken);
        if (!hasAdminRole)
        {
            db.ModelHasRoles.Add(new ModelHasRole
            {
                RoleId = adminRole.Id,
                ModelType = userModelType,
                ModelId = adminUser.Id
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        // 6. Activate & cryptographically seal the license key from --seal-file or --license-key
        var licenseKey = args.TryGetValue("license-key", out var lk) ? (lk ?? "").Trim() : "";
        if (string.IsNullOrWhiteSpace(licenseKey) &&
            args.TryGetValue("seal-file", out var sealFilePath) &&
            !string.IsNullOrWhiteSpace(sealFilePath) &&
            File.Exists(sealFilePath))
        {
            try
            {
                using var sealDoc = JsonDocument.Parse(File.ReadAllText(sealFilePath, Encoding.UTF8));
                if (sealDoc.RootElement.TryGetProperty("license_key", out var lkProp) && lkProp.ValueKind == JsonValueKind.String)
                {
                    licenseKey = (lkProp.GetString() ?? "").Trim();
                }
            }
            catch
            {
                // Handled below
            }
        }

        Dictionary<string, object?>? activatedPayload = null;
        if (!string.IsNullOrWhiteSpace(licenseKey))
        {
            var verifyArgs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["key"] = licenseKey,
                ["allow-unbound"] = IsTruthyArg(args, "require-machine-binding") ? "false" : "true"
            };
            var verifyRes = ExecuteLicenseVerifyInstaller(verifyArgs, testPublicKeyOverride);
            if (!verifyRes.Ok || verifyRes.Data.GetValueOrDefault("payload") is not Dictionary<string, object?> verifiedPayload)
            {
                return new CliCommandResult
                {
                    ExitCode = 1,
                    Output = $"License activation failed during installation: {verifyRes.Output}",
                    Data = new Dictionary<string, object?> { ["ok"] = false, ["error"] = verifyRes.Output }
                };
            }

            var canonicalKey = HardwareMachineIdProvider.NormalizeLicenseKey(licenseKey, withPrefix: true);
            var activatedTs = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var activatedDt = DateTimeOffset.FromUnixTimeSeconds(activatedTs).UtcDateTime;
            var rowSeal = HardwareMachineIdProvider.ComputeRowSeal(canonicalKey, machineId, activatedTs);

            var storedPayload = new Dictionary<string, object?>(verifiedPayload, StringComparer.OrdinalIgnoreCase)
            {
                ["_seal"] = rowSeal
            };

            DateTime? expiresAt = null;
            if (TryGetLongFromDict(verifiedPayload, "exp", out var expTs) && expTs > 0)
            {
                expiresAt = DateTimeOffset.FromUnixTimeSeconds(expTs).UtcDateTime;
            }

            db.Licenses.RemoveRange(await db.Licenses.ToListAsync(cancellationToken));
            db.Licenses.Add(new License
            {
                LicenseKey = canonicalKey,
                Payload = JsonSerializer.Serialize(storedPayload),
                Valid = true,
                Reason = null,
                MachineHash = machineId,
                ActivatedAt = activatedDt,
                ExpiresAt = expiresAt,
                LastVerifiedAt = activatedDt,
                CreatedAt = activatedDt,
                UpdatedAt = activatedDt
            });
            await db.SaveChangesAsync(cancellationToken);

            // Seal monotonic clock file in storage/app/.license-clock
            try
            {
                var clockPath = Path.Combine(_appRoot, "storage", "app", ".license-clock");
                var clockDir = Path.GetDirectoryName(clockPath);
                if (!string.IsNullOrEmpty(clockDir)) Directory.CreateDirectory(clockDir);
                var clockSig = HardwareMachineIdProvider.ComputeHmacHex(activatedTs.ToString(CultureInfo.InvariantCulture), "KAROBAR-CLOCK-SEAL-V2:" + HardwareMachineIdProvider.MasterPublicKeyB64u);
                File.WriteAllText(clockPath, JsonSerializer.Serialize(new { ts = activatedTs, sig = clockSig }));
            }
            catch
            {
                // Ignore non-critical clock file write failure
            }

            activatedPayload = verifiedPayload;
        }

        // 7. Initialize Settings row & legal acceptance timestamps
        var setting = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
        if (setting == null)
        {
            setting = new Setting
            {
                StoreName = "Karobar App",
                PrinterType = "thermal",
                ThermalTemplate = "standard",
                A4Template = "standard",
                SharePaperSize = "a4",
                ShareShowLogo = true,
                NavigationStyle = "sidebar",
                SaleSystem = "retail_wholesale",
                ShopType = "pharmacy",
                CurrencyCode = "PKR",
                CurrencySymbol = "Rs.",
                CurrencyPosition = "before",
                CurrencyDecimalPlaces = 2,
                ExpiryWarningDays = 90,
                CubeAiEnabled = true,
                CubeGroqEnabled = true,
                CubeGroqModel = "llama-3.3-70b-versatile",
                CubeGroqDailyLimit = 250,
                CubeGeminiEnabled = true,
                CubeGeminiModel = "gemini-2.5-flash",
                CubeGeminiDailyLimit = 250
            };
            db.Settings.Add(setting);
        }

        var licensedStoreName = GetStringFromDict(activatedPayload, "name");
        if (!string.IsNullOrWhiteSpace(licensedStoreName) &&
            (string.IsNullOrWhiteSpace(setting.StoreName) || setting.StoreName.Equals("Karobar App", StringComparison.OrdinalIgnoreCase)))
        {
            setting.StoreName = licensedStoreName.Trim();
        }

        if (IsTruthyArg(args, "accept-legal"))
        {
            var nowUtc = DateTime.UtcNow;
            setting.TermsAcceptedAt = nowUtc;
            setting.PrivacyAcceptedAt = nowUtc;
            setting.LegalAcceptedVersion = args.TryGetValue("legal-version", out var lv) && !string.IsNullOrWhiteSpace(lv) ? lv.Trim() : "2.0";
        }

        if (importedExistingDb && await db.Products.AnyAsync(cancellationToken))
        {
            setting.IsSetupCompleted = true;
            setting.SetupCompletedAt ??= DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);

        // 8. Cleanup temporary installer files
        if (IsTruthyArg(args, "cleanup-temp"))
        {
            CleanupInstallerTempFiles(args);
        }

        var resultData = new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["admin_email"] = adminUser.Email,
            ["admin_name"] = adminUser.Name,
            ["machine_id"] = machineId,
            ["licensed"] = await db.Licenses.AnyAsync(cancellationToken),
            ["imported_sqlite"] = importedExistingDb,
            ["store_name"] = setting.StoreName,
            ["is_setup_completed"] = setting.IsSetupCompleted
        };

        return new CliCommandResult
        {
            ExitCode = 0,
            Output = JsonSerializer.Serialize(resultData),
            Data = resultData
        };
    }

    private static void CleanupInstallerTempFiles(IDictionary<string, string?> args)
    {
        foreach (var k in new[] { "seal-file", "admin-config" })
        {
            if (args.TryGetValue(k, out var path) && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                try { File.Delete(path); } catch { }
            }
        }
    }

    /// <summary>
    /// 6. `license:issue` (`LicenseIssueCommand.php`)
    /// </summary>
    public CliCommandResult ExecuteLicenseIssue(IDictionary<string, string?> args)
    {
        if (args.TryGetValue("list-tiers", out var lt) && lt is "1" or "true" or "yes")
        {
            var tiersJson = JsonSerializer.Serialize(new { tiers = LicenseTiers }, new JsonSerializerOptions { WriteIndented = true });
            return new CliCommandResult { ExitCode = 0, Output = tiersJson };
        }

        if (args.TryGetValue("generate-keypair", out var gk) && gk is "1" or "true" or "yes")
        {
            var privKey = new Ed25519PrivateKeyParameters(new SecureRandom());
            var pubKey = privKey.GeneratePublicKey();
            var pubRaw = pubKey.GetEncoded();
            var seedRaw = privKey.GetEncoded();
            var fullSecret = new byte[64];
            Buffer.BlockCopy(seedRaw, 0, fullSecret, 0, 32);
            Buffer.BlockCopy(pubRaw, 0, fullSecret, 32, 32);

            var kpData = new Dictionary<string, object?>
            {
                ["public_key"] = Base64UrlEncode(pubRaw),
                ["public_key_sha256"] = Convert.ToHexStringLower(SHA256.HashData(pubRaw)),
                ["secret_key"] = Base64UrlEncode(fullSecret)
            };
            return new CliCommandResult
            {
                ExitCode = 0,
                Output = JsonSerializer.Serialize(kpData, new JsonSerializerOptions { WriteIndented = true }),
                Data = kpData
            };
        }

        var machine = (args.TryGetValue("machine", out var m) ? (m ?? "") : "").Trim().ToUpperInvariant();
        var allowUnbound = IsTruthyArg(args, "allow-unbound");
        if (string.IsNullOrEmpty(machine) && !allowUnbound)
        {
            return new CliCommandResult
            {
                ExitCode = 1,
                Output = "A target --machine=KRB-XXXX-XXXX-XXXX-XXXX is required so this license key is permanently bound to a single machine."
            };
        }

        var secretB64u = (args.TryGetValue("secret-key", out var sk) && !string.IsNullOrWhiteSpace(sk)
            ? sk
            : Environment.GetEnvironmentVariable("KAROBAR_LICENSE_SECRET_KEY") ?? "").Trim();

        byte[] seedBytes;
        if (!string.IsNullOrEmpty(secretB64u))
        {
            var decoded = Base64UrlDecode(secretB64u);
            seedBytes = decoded.Length >= 32 ? decoded[..32] : SHA256.HashData(Encoding.UTF8.GetBytes(secretB64u));
        }
        else
        {
            seedBytes = SHA256.HashData(Encoding.UTF8.GetBytes("karobar-vendor-default-ed25519-seed"));
        }

        var signingPrivKey = new Ed25519PrivateKeyParameters(seedBytes, 0);
        var rawTier = (args.TryGetValue("tier", out var t) && !string.IsNullOrWhiteSpace(t) ? t : "platinum").Trim().ToLowerInvariant();
        if (!LicenseTiers.ContainsKey(rawTier)) rawTier = "platinum";
        var tierMeta = LicenseTiers[rawTier];

        var days = args.TryGetValue("days", out var dStr) && int.TryParse(dStr, out var dVal) ? dVal : 365;
        var isLifetime = (args.TryGetValue("lifetime", out var lf) && lf is "1" or "true" or "yes") || days <= 0;
        var nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long? exp = isLifetime ? null : nowUnix + (days * 86400L);
        var lid = args.TryGetValue("lid", out var lidVal) && !string.IsNullOrWhiteSpace(lidVal)
            ? lidVal.Trim()
            : $"LIC-{DateTime.UtcNow:yyyyMMdd}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(2))}";
        var storeName = args.TryGetValue("name", out var nVal) && !string.IsNullOrWhiteSpace(nVal) ? nVal.Trim() : "Karobar Licensed Store";

        var payloadObj = new Dictionary<string, object?>
        {
            ["iss"] = "Karobar",
            ["sub"] = "KarobarApp",
            ["ver"] = 2,
            ["lid"] = lid,
            ["name"] = storeName,
            ["tier"] = rawTier,
            ["Edition"] = tierMeta["edition"],
            ["modules"] = tierMeta["modules"],
            ["features"] = tierMeta["modules"],
            ["seats"] = tierMeta["default_seats"],
            ["iat"] = nowUnix,
            ["nbf"] = null,
            ["exp"] = exp
        };
        if (!string.IsNullOrEmpty(machine))
        {
            payloadObj["machine"] = machine;
        }

        var payloadBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payloadObj));
        var payloadB64u = Base64UrlEncode(payloadBytes);
        var signer = new Ed25519Signer();
        signer.Init(true, signingPrivKey);
        signer.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
        var signatureBytes = signer.GenerateSignature();
        var sigB64u = Base64UrlEncode(signatureBytes);
        var licenseKey = $"KRBL.{sigB64u}.{payloadB64u}";

        var outData = new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["license_key"] = licenseKey,
            ["tier"] = rawTier,
            ["tier_label"] = tierMeta["label"],
            ["modules"] = tierMeta["modules"],
            ["payload"] = payloadObj
        };

        return new CliCommandResult
        {
            ExitCode = 0,
            Output = JsonSerializer.Serialize(outData),
            Data = outData
        };
    }

    /// <summary>
    /// 7. `license:machine-id` (`LicenseMachineIdCommand.php`)
    /// </summary>
    public CliCommandResult ExecuteLicenseMachineId(IDictionary<string, string?> args)
    {
        var machineId = ComputeMachineId();

        if (args.TryGetValue("out", out var outFile) && !string.IsNullOrWhiteSpace(outFile))
        {
            var dir = Path.GetDirectoryName(outFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outFile, machineId, Utf8NoBom);
        }

        var isJson = args.TryGetValue("json", out var j) && j is "1" or "true" or "yes";
        var data = new Dictionary<string, object?>
        {
            ["machine_id"] = machineId,
            ["os_family"] = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows" : "Unix",
            ["hostname"] = Environment.MachineName
        };

        return new CliCommandResult
        {
            ExitCode = 0,
            Output = isJson ? JsonSerializer.Serialize(data) : machineId,
            Data = data
        };
    }

    /// <summary>
    /// 8. `license:verify-installer` (`LicenseVerifyInstallerCommand.php`)
    /// Verifies real Ed25519 signature against the pinned Vendor Master Public Key, checks claims/expiry,
    /// enforces machine binding if the payload specifies a Machine ID, and supports unbound vendor-signed keys
    /// when --allow-unbound is passed.
    /// </summary>
    public CliCommandResult ExecuteLicenseVerifyInstaller(
        IDictionary<string, string?> args,
        byte[]? testPublicKeyOverride = null)
    {
        var key = args.TryGetValue("key", out var k) ? (k ?? "").Trim('\uFEFF', ' ', '\t', '\r', '\n') : "";
        if (string.IsNullOrEmpty(key) && args.TryGetValue("key-file", out var kf) && !string.IsNullOrWhiteSpace(kf) && File.Exists(kf))
        {
            key = File.ReadAllText(kf, Encoding.UTF8).Trim('\uFEFF', ' ', '\t', '\r', '\n');
        }

        var errorOut = args.TryGetValue("error-out", out var eo) ? (eo ?? "").Trim() : "";
        var machineId = ComputeMachineId();
        var allowUnbound = IsTruthyArg(args, "allow-unbound");

        CliCommandResult Fail(string friendlyMessage, string reasonCode)
        {
            if (!string.IsNullOrWhiteSpace(errorOut))
            {
                try
                {
                    var dir = Path.GetDirectoryName(errorOut);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(errorOut, friendlyMessage, Utf8NoBom);
                }
                catch { }
            }

            return new CliCommandResult
            {
                ExitCode = 1,
                Output = friendlyMessage,
                Data = new Dictionary<string, object?>
                {
                    ["ok"] = false,
                    ["machine_id"] = machineId,
                    ["reason"] = reasonCode,
                    ["message"] = friendlyMessage
                }
            };
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return Fail("Please enter a valid Karobar license key.", "Empty key");
        }

        var normalized = HardwareMachineIdProvider.NormalizeLicenseKey(key, withPrefix: false);
        if (string.IsNullOrEmpty(normalized) || !normalized.Contains('.'))
        {
            return Fail("Invalid or corrupted license key. Please copy the entire KRBL. license string carefully.", "Malformed license");
        }

        var parts = normalized.Split('.', 3);
        if (parts.Length != 2)
        {
            return Fail("Invalid or corrupted license key format.", "Malformed license");
        }

        var sigBytes = HardwareMachineIdProvider.B64uDecode(parts[0]);
        var payloadBytes = HardwareMachineIdProvider.B64uDecode(parts[1]);
        if (sigBytes.Length != 64 || payloadBytes.Length == 0)
        {
            return Fail("Invalid cryptographic signature or payload encoding.", "Malformed license");
        }

        Dictionary<string, object?>? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(Encoding.UTF8.GetString(payloadBytes));
        }
        catch
        {
            payload = null;
        }

        if (payload == null)
        {
            return Fail("Invalid JSON payload in license key.", "Invalid JSON payload");
        }

        // Verify real Ed25519 signature against the pinned Vendor Master Public Key
        bool signatureValid;
        try
        {
            var pubKeyBytes = testPublicKeyOverride ?? HardwareMachineIdProvider.GetMasterPublicKeyBytes();
            var pubKeyParam = new Ed25519PublicKeyParameters(pubKeyBytes, 0);
            var verifier = new Ed25519Signer();
            verifier.Init(false, pubKeyParam);
            verifier.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
            signatureValid = verifier.VerifySignature(sigBytes);
        }
        catch
        {
            signatureValid = false;
        }

        if (!signatureValid)
        {
            return Fail("License signature verification failed. This license key is invalid or has been altered.", "Bad signature");
        }

        var iss = GetStringFromDict(payload, "iss");
        if (!string.IsNullOrEmpty(iss) && iss is not ("Karobar" or "KarobarApp" or "YourCompany"))
        {
            return Fail("Invalid license issuer.", "Invalid license issuer");
        }

        var sub = GetStringFromDict(payload, "sub");
        if (!string.IsNullOrEmpty(sub) && sub is not ("KarobarApp" or "karobar-pos" or "YourApp"))
        {
            return Fail("Invalid license product.", "Invalid license product");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (TryGetLongFromDict(payload, "nbf", out var nbf) && (now + HardwareMachineIdProvider.ClockSkewToleranceSeconds) < nbf)
        {
            return Fail("This license key is not yet valid (check your system date and time).", "Not yet valid");
        }

        if (TryGetLongFromDict(payload, "exp", out var exp) && exp > 0 && now > exp)
        {
            return Fail("This license key has expired. Please contact support for a renewal key.", "Expired");
        }

        var payloadMachine = GetStringFromDict(payload, "machine");
        if (string.IsNullOrWhiteSpace(payloadMachine))
        {
            if (!allowUnbound)
            {
                return Fail($"This license key is not bound to a Machine ID. Please request a machine-bound license key for Machine ID: {machineId}", "Unbound key");
            }
        }
        else if (!string.Equals(payloadMachine.Trim(), machineId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Fail($"This license key is bound to another computer ({payloadMachine}) and cannot be used on this machine (Your Machine ID: {machineId}).", "Wrong machine");
        }

        var canonicalKey = HardwareMachineIdProvider.NormalizeLicenseKey(key, withPrefix: true);
        var verifiedAt = now;
        var sealSig = HardwareMachineIdProvider.ComputeRowSeal(canonicalKey, machineId, verifiedAt);

        var sealData = new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["license_key"] = canonicalKey,
            ["machine_id"] = machineId,
            ["verified_at"] = verifiedAt,
            ["seal"] = sealSig,
            ["payload"] = payload
        };

        if (args.TryGetValue("seal-out", out var sealOut) && !string.IsNullOrWhiteSpace(sealOut))
        {
            var dir = Path.GetDirectoryName(sealOut);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(sealOut, JsonSerializer.Serialize(sealData, new JsonSerializerOptions { WriteIndented = true }), Utf8NoBom);
        }

        if (!string.IsNullOrWhiteSpace(errorOut) && File.Exists(errorOut))
        {
            try { File.Delete(errorOut); } catch { }
        }

        return new CliCommandResult
        {
            ExitCode = 0,
            Output = JsonSerializer.Serialize(sealData),
            Data = sealData
        };
    }

    private static string? GetStringFromDict(IDictionary<string, object?>? dict, string key)
    {
        if (dict == null || !dict.TryGetValue(key, out var val) || val == null) return null;
        if (val is JsonElement je)
        {
            return je.ValueKind == JsonValueKind.String ? je.GetString() : (je.ValueKind == JsonValueKind.Null ? null : je.GetRawText());
        }
        return val.ToString();
    }

    private static bool TryGetLongFromDict(IDictionary<string, object?>? dict, string key, out long result)
    {
        result = 0;
        if (dict == null || !dict.TryGetValue(key, out var val) || val == null) return false;
        if (val is long l) { result = l; return true; }
        if (val is int i) { result = i; return true; }
        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out result)) return true;
            if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out result)) return true;
            return false;
        }
        return long.TryParse(val.ToString(), out result);
    }

    /// <summary>
    /// 9. `expenses:process-recurring` (`ProcessRecurringExpenses.php`)
    /// </summary>
    public async Task<CliCommandResult> ExecuteProcessRecurringExpensesAsync(
        KarobarDbContext db,
        IDictionary<string, string?> args,
        CancellationToken cancellationToken = default)
    {
        var asOfDate = args.TryGetValue("date", out var dStr) && DateTime.TryParse(dStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : DateTime.Today;

        var dueTemplates = await db.Expenses
            .Where(e => e.IsRecurring &&
                        e.RecurringFrequency != null &&
                        e.Date <= asOfDate)
            .ToListAsync(cancellationToken);

        var processedCount = 0;

        foreach (var template in dueTemplates)
        {
            var runDate = template.Date.Date;
            var freq = (template.RecurringFrequency ?? "monthly").Trim().ToLowerInvariant();
            var nextDate = freq switch
            {
                "daily" => runDate.AddDays(1),
                "weekly" => runDate.AddDays(7),
                "yearly" => runDate.AddYears(1),
                _ => runDate.AddMonths(1)
            };

            if (nextDate <= asOfDate)
            {
                var payMethod = string.IsNullOrWhiteSpace(template.PaymentMethod) ? "cash" : template.PaymentMethod.Trim();
                Account? account = null;
                if (template.AccountId.HasValue && template.AccountId.Value > 0)
                {
                    account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == template.AccountId.Value, cancellationToken);
                }
                else if (payMethod.Equals("cash", StringComparison.OrdinalIgnoreCase))
                {
                    account = await db.Accounts
                        .Where(a => a.IsActive && a.Type == "cash")
                        .OrderByDescending(a => a.IsDefault)
                        .ThenBy(a => a.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                var isDefaultCashDrawer = account != null &&
                    string.Equals(account.Type, "cash", StringComparison.OrdinalIgnoreCase) &&
                    (account.IsDefault ||
                     string.Equals(account.Code, "cash_drawer", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(account.Code, "CASH-DRAWER", StringComparison.OrdinalIgnoreCase));

                CashDrawerSession? openSession = null;
                if (isDefaultCashDrawer && template.Amount > 0m)
                {
                    openSession = await db.CashDrawerSessions
                        .Where(s => s.Status == "open")
                        .OrderByDescending(s => s.OpenedAt)
                        .ThenByDescending(s => s.Id)
                        .FirstOrDefaultAsync(cancellationToken);
                }

                var newExp = new Expense
                {
                    ExpenseNumber = $"EXP-REC-{DateTime.UtcNow:yyyyMMddHHmmss}-{template.Id}",
                    ExpenseCategoryId = template.ExpenseCategoryId,
                    AccountId = account?.Id ?? template.AccountId,
                    CashDrawerSessionId = openSession?.Id,
                    UserId = template.UserId,
                    Amount = template.Amount,
                    Date = nextDate,
                    PaymentMethod = payMethod,
                    ReferenceNumber = template.ReferenceNumber,
                    Payee = template.Payee,
                    Description = template.Description,
                    IsRecurring = true,
                    RecurringFrequency = template.RecurringFrequency,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                db.Expenses.Add(newExp);
                template.IsRecurring = false;
                await db.SaveChangesAsync(cancellationToken);

                // Debit the linked financial account & sync active Cash Drawer so both stay reconciled
                if (account != null && template.Amount > 0m)
                {
                    var recDesc = $"Recurring Expense ({newExp.ExpenseNumber}): {template.Description ?? template.Payee ?? "Scheduled"}";
                    account.CurrentBalance = Math.Round(account.CurrentBalance - template.Amount, 2);
                    account.UpdatedAt = DateTime.UtcNow;
                    db.AccountTransactions.Add(new AccountTransaction
                    {
                        AccountId = account.Id,
                        Type = "withdrawal",
                        Amount = template.Amount,
                        BalanceAfter = account.CurrentBalance,
                        ReferenceType = "expense",
                        ReferenceId = newExp.Id,
                        Description = recDesc,
                        UserId = template.UserId,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });

                    if (openSession != null)
                    {
                        openSession.ClosingBalanceExpected = Math.Round(openSession.ClosingBalanceExpected - template.Amount, 2);
                        openSession.UpdatedAt = DateTime.UtcNow;
                        db.CashDrawerTransactions.Add(new CashDrawerTransaction
                        {
                            SessionId = openSession.Id,
                            UserId = template.UserId ?? openSession.UserId,
                            Type = "expense",
                            Amount = template.Amount,
                            ReferenceType = "expense",
                            ReferenceId = newExp.Id,
                            Notes = recDesc,
                            TerminalId = openSession.TerminalId,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        });
                    }
                }

                processedCount++;
            }
        }

        if (processedCount > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new CliCommandResult
        {
            ExitCode = 0,
            Output = $"Recurring expenses processed: {processedCount}",
            Data = new Dictionary<string, object?> { ["ok"] = true, ["processed_count"] = processedCount }
        };
    }

    /// <summary>
    /// 10. `backup:run-scheduled` (`RunScheduledBackup.php`)
    /// </summary>
    public async Task<CliCommandResult> ExecuteRunScheduledBackupAsync(
        KarobarDbContext db,
        IDictionary<string, string?> args,
        CancellationToken cancellationToken = default)
    {
        var force = args.TryGetValue("force", out var f) && f is "1" or "true" or "yes";
        var schedule = await db.BackupSchedules.OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);

        if (schedule == null)
        {
            schedule = new BackupSchedule
            {
                IsAutomated = false,
                Frequency = "daily",
                TimeOfDay = "02:00",
                BackupType = "full",
                StorageDestination = "local"
            };
            db.BackupSchedules.Add(schedule);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!schedule.IsAutomated && !force)
        {
            return new CliCommandResult
            {
                ExitCode = 0,
                Output = "Automated backup is disabled in system settings. Use --force to override.",
                Data = new Dictionary<string, object?> { ["ok"] = true, ["ran"] = false }
            };
        }

        var now = DateTime.Now;
        if (!force && !IsScheduledBackupDue(schedule, now))
        {
            return new CliCommandResult
            {
                ExitCode = 0,
                Output = $"Backup is not due at this time (Configured time: {schedule.TimeOfDay}, frequency: {schedule.Frequency}).",
                Data = new Dictionary<string, object?> { ["ok"] = true, ["ran"] = false }
            };
        }

        var backupDir = Path.Combine(_appRoot, "storage", "app", "backups");
        var filename = $"karobar-backup-{now:yyyy-MM-dd-HHmmss}.zip";
        var fullPath = Path.Combine(backupDir, filename);

        var sourceDb = Path.Combine(_appRoot, "database", "database.sqlite");
        string? tempSnapshot = null;
        try
        {
            Directory.CreateDirectory(backupDir);
            try
            {
                if (File.Exists(sourceDb))
                {
                    tempSnapshot = Path.Combine(Path.GetTempPath(), $"karobar-sched-snap-{Guid.NewGuid():N}.sqlite");
                    using (var srcConn = new SqliteConnection($"Data Source={sourceDb};Mode=ReadOnly;Pooling=False;"))
                    using (var dstConn = new SqliteConnection($"Data Source={tempSnapshot};Mode=ReadWriteCreate;Pooling=False;"))
                    {
                        await srcConn.OpenAsync(cancellationToken);
                        await dstConn.OpenAsync(cancellationToken);
                        srcConn.BackupDatabase(dstConn);
                    }
                }

                using (var zipStream = new FileStream(fullPath, FileMode.Create))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    if (tempSnapshot != null && File.Exists(tempSnapshot))
                    {
                        archive.CreateEntryFromFile(tempSnapshot, "database.sqlite", CompressionLevel.Optimal);
                    }
                    else
                    {
                        var entry = archive.CreateEntry("manifest.json");
                        await using var writer = new StreamWriter(entry.Open());
                        await writer.WriteAsync(JsonSerializer.Serialize(new { created_at = now.ToString("O"), type = schedule.BackupType }));
                    }
                }
            }
            finally
            {
                try { if (tempSnapshot != null && File.Exists(tempSnapshot)) File.Delete(tempSnapshot); } catch { }
            }

            if (schedule.StorageDestination == "custom_folder" &&
                !string.IsNullOrWhiteSpace(schedule.CustomBackupPath) &&
                Directory.Exists(schedule.CustomBackupPath))
            {
                try
                {
                    File.Copy(fullPath, Path.Combine(schedule.CustomBackupPath, filename), overwrite: true);
                }
                catch (Exception mirrorEx)
                {
                    _logger?.LogWarning(mirrorEx, "Scheduled backup mirror copy to {CustomBackupPath} failed.", schedule.CustomBackupPath);
                }
            }

            var fileSize = new FileInfo(fullPath).Length;
            var log = new BackupLog
            {
                Filename = filename,
                Path = $"backups/{filename}",
                Type = schedule.BackupType ?? "full",
                Size = fileSize,
                Status = "completed",
                IsAutomated = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.BackupLogs.Add(log);

            schedule.LastRunAt = DateTime.UtcNow;
            schedule.LastRunStatus = "success";
            schedule.LastErrorMessage = null;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Scheduled backup failed for {BackupPath}.", fullPath);
            schedule.LastRunAt = DateTime.UtcNow;
            schedule.LastRunStatus = "failed";
            schedule.LastErrorMessage = ex.Message;
            try
            {
                await db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception saveEx)
            {
                _logger?.LogWarning(saveEx, "Failed to persist scheduled backup failure status.");
            }

            return new CliCommandResult
            {
                ExitCode = 1,
                Output = $"Scheduled backup failed: {ex.Message}",
                Data = new Dictionary<string, object?>
                {
                    ["ok"] = false,
                    ["ran"] = false,
                    ["error"] = ex.Message
                }
            };
        }

        // Apply retention pruning on automated backups if MaxBackupsKeep > 0 or RetentionDays > 0
        if (schedule.MaxBackupsKeep > 0 || schedule.RetentionDays > 0)
        {
            try
            {
                var allAutoLogs = await db.BackupLogs
                    .Where(l => l.IsAutomated)
                    .OrderByDescending(l => l.Id)
                    .ToListAsync(cancellationToken);
                var cutoffUtc = schedule.RetentionDays > 0 ? DateTime.UtcNow.AddDays(-schedule.RetentionDays) : DateTime.MinValue;
                var toRemove = new List<BackupLog>();
                for (var idx = 0; idx < allAutoLogs.Count; idx++)
                {
                    var item = allAutoLogs[idx];
                    var exceedsCount = schedule.MaxBackupsKeep > 0 && idx >= schedule.MaxBackupsKeep;
                    var exceedsAge = schedule.RetentionDays > 0 && item.CreatedAt.HasValue && item.CreatedAt.Value < cutoffUtc;
                    if (exceedsCount || exceedsAge)
                    {
                        toRemove.Add(item);
                    }
                }
                if (toRemove.Count > 0)
                {
                    foreach (var stale in toRemove)
                    {
                        var staleFile = Path.Combine(backupDir, Path.GetFileName(stale.Filename ?? ""));
                        try { if (File.Exists(staleFile)) File.Delete(staleFile); } catch { }
                        db.BackupLogs.Remove(stale);
                    }
                    await db.SaveChangesAsync(cancellationToken);
                }
            }
            catch { }
        }

        return new CliCommandResult
        {
            ExitCode = 0,
            Output = $"Backup {filename} completed successfully.",
            Data = new Dictionary<string, object?> { ["ok"] = true, ["ran"] = true, ["filename"] = filename }
        };
    }

    private static bool IsScheduledBackupDue(BackupSchedule schedule, DateTime now)
    {
        var targetTime = string.IsNullOrWhiteSpace(schedule.TimeOfDay) ? "02:00" : schedule.TimeOfDay.Trim();
        var parts = targetTime.Split(':');
        var targetHour = parts.Length > 0 && int.TryParse(parts[0], out var th) ? Math.Clamp(th, 0, 23) : 2;
        var targetMinute = parts.Length > 1 && int.TryParse(parts[1], out var tm) ? Math.Clamp(tm, 0, 59) : 0;

        if (now.TimeOfDay < new TimeSpan(targetHour, targetMinute, 0)) return false;

        if (schedule.LastRunAt.HasValue && schedule.LastRunStatus == "success")
        {
            var lastLocal = DateTime.SpecifyKind(schedule.LastRunAt.Value, DateTimeKind.Utc).ToLocalTime();
            if (lastLocal.Date == now.Date)
            {
                return false;
            }
        }

        return (schedule.Frequency ?? "daily").ToLowerInvariant() switch
        {
            "weekly" => (int)now.DayOfWeek == (schedule.DayOfWeek ?? 0),
            "monthly" => now.Day == (schedule.DayOfMonth ?? 1),
            _ => true
        };
    }

    /// <summary>
    /// 11. `demands:notify` (`SendUserDemandNotifications.php`)
    /// </summary>
    public async Task<CliCommandResult> ExecuteSendUserDemandNotificationsAsync(
        KarobarDbContext db,
        IDictionary<string, string?> args,
        CancellationToken cancellationToken = default)
    {
        var force = args.TryGetValue("force", out var f) && f is "1" or "true" or "yes";
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-1);

        var query = db.UserDemands
            .Include(d => d.Product)
            .Include(d => d.Customer)
            .Where(d => d.Status == "pending");
        if (!force)
        {
            query = query.Where(d => d.LastNotifiedAt == null || d.LastNotifiedAt < cutoff);
        }

        var dueDemands = await query
            .OrderBy(d => d.Id)
            .ToListAsync(cancellationToken);

        var notifications = new List<Dictionary<string, object?>>();
        var notifiedIds = new List<long>();
        var digestLines = new List<string>();
        var notifiedAtIso = now.ToString("O");

        foreach (var demand in dueDemands)
        {
            demand.LastNotifiedAt = now;
            demand.UpdatedAt = now;
            notifiedIds.Add(demand.Id);

            var itemName = !string.IsNullOrWhiteSpace(demand.Product?.Name)
                ? demand.Product!.Name
                : (!string.IsNullOrWhiteSpace(demand.RequestedName) ? demand.RequestedName! : $"Demand #{demand.Id}");
            var customerName = demand.Customer?.Name;

            notifications.Add(new Dictionary<string, object?>
            {
                ["demand_id"] = demand.Id,
                ["product_id"] = demand.ProductId,
                ["product_name"] = itemName,
                ["requested_quantity"] = demand.RequestedQuantity,
                ["customer_name"] = customerName,
                ["notes"] = demand.Notes,
                ["route"] = "/user-demands",
                ["notified_at"] = notifiedAtIso
            });

            var customerSegment = string.IsNullOrWhiteSpace(customerName) ? "" : $" | Customer: {customerName}";
            var noteSegment = string.IsNullOrWhiteSpace(demand.Notes) ? "" : $" | Notes: {demand.Notes}";
            digestLines.Add($"- [Demand #{demand.Id}] {itemName} x{demand.RequestedQuantity}{customerSegment}{noteSegment} -> /user-demands");
        }

        if (dueDemands.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);

            _logger?.LogInformation(
                "demands:notify stamped {Count} pending user demand(s): {DemandIds}",
                dueDemands.Count,
                string.Join(",", notifiedIds));

            try
            {
                var logDir = Path.Combine(_appRoot, "storage", "logs");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "demand-notifications.log");
                if (File.Exists(logPath) && new FileInfo(logPath).Length > 2 * 1024 * 1024)
                {
                    File.WriteAllText(logPath, string.Empty, new UTF8Encoding(false));
                }
                var entry = $"[{notifiedAtIso}] Notified {dueDemands.Count} pending user demand(s):\n"
                            + string.Join("\n", digestLines) + "\n";
                await File.AppendAllTextAsync(logPath, entry, new UTF8Encoding(false), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger?.LogWarning(ex, "Could not append to storage/logs/demand-notifications.log.");
            }
        }

        var pendingCount = await db.UserDemands.CountAsync(d => d.Status == "pending", cancellationToken);
        var outputBuilder = new StringBuilder();
        outputBuilder.Append($"User demands notified: {dueDemands.Count}\nPending user demands still active: {pendingCount}");
        if (digestLines.Count > 0)
        {
            outputBuilder.Append('\n');
            outputBuilder.Append(string.Join('\n', digestLines));
        }

        return new CliCommandResult
        {
            ExitCode = 0,
            Output = outputBuilder.ToString(),
            Data = new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["notified_count"] = dueDemands.Count,
                ["pending_count"] = pendingCount,
                ["notified_ids"] = notifiedIds,
                ["notifications"] = notifications,
                ["notified_at"] = notifiedAtIso
            }
        };
    }

    public static string ComputeMachineId()
    {
        return HardwareMachineIdProvider.ComputeDeterministicMachineId();
    }

    public static string Base64UrlEncode(byte[] input)
    {
        return HardwareMachineIdProvider.B64uEncode(input);
    }

    public static byte[] Base64UrlDecode(string input)
    {
        return HardwareMachineIdProvider.B64uDecode(input);
    }
}

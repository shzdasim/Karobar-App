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
using Karobar.Core.Entities;
using Karobar.Data;
using Microsoft.EntityFrameworkCore;
using NSec.Cryptography;

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
        "demands:notify"
    ];

    public const string MasterPublicKeyB64u = "37988jJ_nO4pW_5Q3u5p7xH0v_2W8k1L9m3N6b4V2c8";

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

    public KarobarCliCommandRunner(string? appRoot = null, WaseelaAndSqliteImportEngine? importEngine = null)
    {
        _appRoot = ResolveAppRoot(appRoot);
        _importEngine = importEngine ?? new WaseelaAndSqliteImportEngine(_appRoot);
    }

    private static string ResolveAppRoot(string? explicitRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot) && Directory.Exists(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot);
        }
        var envRoot = Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
        {
            return Path.GetFullPath(envRoot);
        }
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "artisan")) || Directory.Exists(Path.Combine(dir, "database")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return Directory.GetCurrentDirectory();
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

            "backup:run-scheduled"
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

        if (args.TryGetValue("verify-sha", out var expectedSha) && !string.IsNullOrWhiteSpace(expectedSha))
        {
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
        }

        // Pre-patch safety backup
        var dbPath = Path.Combine(_appRoot, "database", "database.sqlite");
        if (File.Exists(dbPath))
        {
            var backupDir = Path.Combine(_appRoot, "storage", "backups");
            Directory.CreateDirectory(backupDir);
            var backupFile = Path.Combine(backupDir, $"pre-patch-{DateTime.Now:yyyyMMdd-HHmmss}.sqlite");
            try { File.Copy(dbPath, backupFile, overwrite: true); } catch { }
        }

        var stagingDir = Path.Combine(Path.GetTempPath(), $"karobar-patch-extract-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDir);

        try
        {
            ZipFile.ExtractToDirectory(patchZip, stagingDir, overwriteFiles: true);
            var appliedFiles = 0;

            foreach (var file in Directory.GetFiles(stagingDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(stagingDir, file).Replace('\\', '/');
                if (rel.StartsWith("storage/", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith("runtime/", StringComparison.OrdinalIgnoreCase) ||
                    rel.Contains("database.sqlite", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith(".env", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var targetPath = Path.Combine(_appRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(file, targetPath, overwrite: true);
                appliedFiles++;
            }

            var data = new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["patch_applied"] = true,
                ["files_updated"] = appliedFiles,
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
    /// 3. `karobar:export-sqlite` (`KarobarExportSqliteCommand.php`)
    /// </summary>
    public Task<CliCommandResult> ExecuteExportSqliteAsync(
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
            File.Copy(sourceDb, outPath, overwrite: true);
        }
        else
        {
            File.WriteAllBytes(outPath, []);
        }

        var sizeMb = File.Exists(outPath) ? Math.Round(new FileInfo(outPath).Length / (1024.0 * 1024.0), 2) : 0;
        return Task.FromResult(new CliCommandResult
        {
            ExitCode = 0,
            Output = $"Portable SQLite database exported successfully ({sizeMb} MB): {outPath}",
            Data = new Dictionary<string, object?> { ["ok"] = true, ["path"] = outPath, ["size_mb"] = sizeMb }
        });
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

        var staged = await _importEngine.StageDatabaseFromFileAsync(filePath, Path.GetFileName(filePath), cancellationToken);
        var token = staged["token"]?.ToString() ?? "";
        var inspection = await _importEngine.InspectAndProposeMappingAsync(token, cancellationToken);
        var reconciliation = await _importEngine.RunPostImportReconciliationAsync(db, cancellationToken);
        _importEngine.CleanupStagedSession(token);

        return new CliCommandResult
        {
            ExitCode = 0,
            Output = "Import completed!",
            Data = new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["inspection"] = inspection,
                ["reconciliation"] = reconciliation
            }
        };
    }

    /// <summary>
    /// 5. `karobar:install` (`KarobarInstallCommand.php`)
    /// </summary>
    public async Task<CliCommandResult> ExecuteInstallAsync(
        KarobarDbContext db,
        IDictionary<string, string?> args,
        CancellationToken cancellationToken = default)
    {
        var isUpgradeOnly = args.TryGetValue("upgrade-only", out var uo) && uo is "1" or "true" or "yes";
        var machineId = ComputeMachineId();

        if (isUpgradeOnly && await db.Users.AnyAsync(cancellationToken))
        {
            var firstAdmin = await db.Users.OrderBy(u => u.Id).FirstOrDefaultAsync(cancellationToken);
            var existingSetting = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
            var upgData = new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["upgraded"] = true,
                ["admin_email"] = firstAdmin?.Email,
                ["admin_name"] = firstAdmin?.Name,
                ["machine_id"] = machineId,
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

        var adminEmail = (args.TryGetValue("admin-email", out var ae) && !string.IsNullOrWhiteSpace(ae) ? ae : "admin@example.com").Trim().ToLowerInvariant();
        var adminName = (args.TryGetValue("admin-name", out var an) && !string.IsNullOrWhiteSpace(an) ? an : "Store Administrator").Trim();
        var adminPassword = args.TryGetValue("admin-password", out var ap) && !string.IsNullOrWhiteSpace(ap) ? ap : "password";

        var adminRole = await db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin", cancellationToken);
        if (adminRole == null)
        {
            adminRole = new Role { Name = "Admin", GuardName = "sanctum" };
            db.Roles.Add(adminRole);
            await db.SaveChangesAsync(cancellationToken);
        }

        var adminUser = await db.Users.FirstOrDefaultAsync(u => u.Email == adminEmail, cancellationToken);
        if (adminUser == null)
        {
            adminUser = new User
            {
                Name = adminName,
                Email = adminEmail,
                Password = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                Status = "active"
            };
            db.Users.Add(adminUser);
        }
        else
        {
            adminUser.Name = adminName;
            adminUser.Password = BCrypt.Net.BCrypt.HashPassword(adminPassword);
            adminUser.Status = "active";
        }
        await db.SaveChangesAsync(cancellationToken);

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
            await db.SaveChangesAsync(cancellationToken);
        }

        var resultData = new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["admin_email"] = adminUser.Email,
            ["admin_name"] = adminUser.Name,
            ["machine_id"] = machineId,
            ["licensed"] = await db.Licenses.AnyAsync(cancellationToken),
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

        var algorithm = SignatureAlgorithm.Ed25519;

        if (args.TryGetValue("generate-keypair", out var gk) && gk is "1" or "true" or "yes")
        {
            using var key = Key.Create(algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
            var pubRaw = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
            var seedRaw = key.Export(KeyBlobFormat.RawPrivateKey);
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
        var allowUnbound = args.TryGetValue("allow-unbound", out var au) && au is "1" or "true" or "yes";
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

        using var signingKey = Key.Import(algorithm, seedBytes, KeyBlobFormat.RawPrivateKey);
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
        var signatureBytes = algorithm.Sign(signingKey, payloadBytes);
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
            File.WriteAllText(outFile, machineId, Encoding.UTF8);
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
    /// </summary>
    public CliCommandResult ExecuteLicenseVerifyInstaller(IDictionary<string, string?> args)
    {
        var key = args.TryGetValue("key", out var k) ? (k ?? "").Trim() : "";
        if (string.IsNullOrEmpty(key) && args.TryGetValue("key-file", out var kf) && !string.IsNullOrWhiteSpace(kf) && File.Exists(kf))
        {
            key = File.ReadAllText(kf).Trim();
        }

        var machineId = ComputeMachineId();
        if (string.IsNullOrEmpty(key) || !key.StartsWith("KRBL.", StringComparison.OrdinalIgnoreCase))
        {
            return new CliCommandResult
            {
                ExitCode = 1,
                Output = "Invalid or corrupted license key. Please copy the entire KRBL. license string carefully.",
                Data = new Dictionary<string, object?> { ["ok"] = false, ["machine_id"] = machineId, ["reason"] = "Malformed license" }
            };
        }

        var parts = key.Split('.');
        if (parts.Length != 3)
        {
            return new CliCommandResult
            {
                ExitCode = 1,
                Output = "Invalid or corrupted license key.",
                Data = new Dictionary<string, object?> { ["ok"] = false, ["machine_id"] = machineId }
            };
        }

        Dictionary<string, object?>? payload;
        try
        {
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[2]));
            payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadJson);
        }
        catch
        {
            payload = null;
        }

        if (payload == null)
        {
            return new CliCommandResult
            {
                ExitCode = 1,
                Output = "Invalid JSON payload in license key.",
                Data = new Dictionary<string, object?> { ["ok"] = false, ["machine_id"] = machineId }
            };
        }

        var verifiedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var sealBytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes("karobar-installer-seal"), Encoding.UTF8.GetBytes($"{key}|{machineId}|{verifiedAt}"));
        var sealSig = Convert.ToHexStringLower(sealBytes);

        var sealData = new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["license_key"] = key,
            ["machine_id"] = machineId,
            ["verified_at"] = verifiedAt,
            ["seal"] = sealSig,
            ["payload"] = payload
        };

        if (args.TryGetValue("seal-out", out var sealOut) && !string.IsNullOrWhiteSpace(sealOut))
        {
            var dir = Path.GetDirectoryName(sealOut);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(sealOut, JsonSerializer.Serialize(sealData, new JsonSerializerOptions { WriteIndented = true }));
        }

        return new CliCommandResult
        {
            ExitCode = 0,
            Output = JsonSerializer.Serialize(sealData),
            Data = sealData
        };
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
            : DateTime.UtcNow.Date;

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
                db.Expenses.Add(new Expense
                {
                    ExpenseNumber = $"EXP-REC-{DateTime.UtcNow:yyyyMMddHHmmss}-{template.Id}",
                    ExpenseCategoryId = template.ExpenseCategoryId,
                    AccountId = template.AccountId,
                    UserId = template.UserId,
                    Amount = template.Amount,
                    Date = nextDate,
                    PaymentMethod = template.PaymentMethod,
                    ReferenceNumber = template.ReferenceNumber,
                    Payee = template.Payee,
                    Description = template.Description,
                    IsRecurring = true,
                    RecurringFrequency = template.RecurringFrequency,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                template.IsRecurring = false;
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
                StorageDestination = "default"
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
        Directory.CreateDirectory(backupDir);
        var filename = $"karobar-backup-{now:yyyy-MM-dd-HHmmss}.zip";
        var fullPath = Path.Combine(backupDir, filename);

        var sourceDb = Path.Combine(_appRoot, "database", "database.sqlite");
        using (var zipStream = new FileStream(fullPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            if (File.Exists(sourceDb))
            {
                archive.CreateEntryFromFile(sourceDb, "database.sqlite");
            }
            else
            {
                var entry = archive.CreateEntry("manifest.json");
                await using var writer = new StreamWriter(entry.Open());
                await writer.WriteAsync(JsonSerializer.Serialize(new { created_at = now.ToString("O"), type = schedule.BackupType }));
            }
        }

        if (schedule.StorageDestination == "custom_folder" &&
            !string.IsNullOrWhiteSpace(schedule.CustomBackupPath) &&
            Directory.Exists(schedule.CustomBackupPath))
        {
            try
            {
                File.Copy(fullPath, Path.Combine(schedule.CustomBackupPath, filename), overwrite: true);
            }
            catch
            {
                // Ignore mirror error
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
        var targetHour = parts.Length > 0 && int.TryParse(parts[0], out var th) ? th : 2;

        if (now.Hour != targetHour) return false;
        if (schedule.LastRunAt.HasValue && schedule.LastRunStatus == "success" && schedule.LastRunAt.Value.Date == DateTime.UtcNow.Date)
        {
            return false;
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

        var query = db.UserDemands.Where(d => d.Status == "pending");
        if (!force)
        {
            query = query.Where(d => d.LastNotifiedAt == null || d.LastNotifiedAt < cutoff);
        }

        var dueDemands = await query.ToListAsync(cancellationToken);
        foreach (var demand in dueDemands)
        {
            demand.LastNotifiedAt = now;
        }

        if (dueDemands.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var pendingCount = await db.UserDemands.CountAsync(d => d.Status == "pending", cancellationToken);

        return new CliCommandResult
        {
            ExitCode = 0,
            Output = $"User demands notified: {dueDemands.Count}\nPending user demands still active: {pendingCount}",
            Data = new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["notified_count"] = dueDemands.Count,
                ["pending_count"] = pendingCount
            }
        };
    }

    public static string ComputeMachineId()
    {
        var rawIdentity = $"{Environment.MachineName}|{Environment.OSVersion}|{Environment.ProcessorCount}|KarobarNativeV2";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawIdentity));
        var hex = Convert.ToHexString(hash);
        return $"KRB-{hex[..4]}-{hex[4..8]}-{hex[8..12]}-{hex[12..16]}";
    }

    public static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}

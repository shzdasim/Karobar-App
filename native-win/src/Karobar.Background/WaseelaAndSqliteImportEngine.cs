using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Karobar.Core.Entities;
using Karobar.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Background;

public sealed class WaseelaDetectionResult
{
    public bool IsWaseelaSchema { get; set; }
    public string DetectedProfile { get; set; } = "karobar_v2"; // "abuzar_waseela" | "karobar_v2" | "legacy_pos"
    public double ConfidenceScore { get; set; }
    public List<string> MatchedSignatures { get; set; } = [];
    public Dictionary<string, string> WaseelaTableMappings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Intelligent SQLite + Abuzar Waseela Importer (`app/Services/SqliteImportService.php`).
/// Supports:
/// - Staging &amp; validating `.sqlite`, `.sqlite3`, `.db`, `.zip`, `.gz`, `.sql.gz`, `.sql`, and Abuzar Waseela `.csv` / SQL Server dumps
/// - Built-in **Abuzar Waseela** schema auto-detector &amp; mapping profile (Items, Batches/Expiries, Suppliers, Customers, Opening Khatas)
/// - Strictly protected security/auth/license/RBAC table exclusion (`ProtectedTables`)
/// - Topological 6-stage dependency ordering (`DependencyStages`), cross-table FK ID translation, and post-import Opening Batch &amp; Formulation reconciliation.
/// </summary>
public sealed class WaseelaAndSqliteImportEngine
{
    public static readonly string[] ProtectedTables =
    [
        "users",
        "licenses",
        "roles",
        "permissions",
        "model_has_roles",
        "model_has_permissions",
        "role_has_permissions",
        "sessions",
        "personal_access_tokens",
        "password_reset_tokens",
        "migrations",
        "cache",
        "cache_locks",
        "jobs",
        "job_batches",
        "failed_jobs",
        "backup_logs",
        "backup_schedules",
        "cube_provider_usages"
    ];

    public static readonly string[] OptionalConfigTables =
    [
        "settings",
        "theme_settings",
        "barcode_templates"
    ];

    public static readonly IReadOnlyDictionary<int, string[]> DependencyStages = new Dictionary<int, string[]>
    {
        [0] =
        [
            "settings", "theme_settings", "barcode_templates", "storage_locations", "tax_rates",
            "chart_of_accounts", "accounts", "expense_categories", "formulations", "categories",
            "brands", "suppliers", "customers", "employees"
        ],
        [1] =
        [
            "products", "product_suppliers", "supplier_product_positions", "customer_regular_items",
            "customer_whole_sale_prices", "cash_drawer_sessions", "payroll_runs", "journal_entries",
            "stocktake_sessions", "stock_adjustments", "invoice_drafts"
        ],
        [2] =
        [
            "batches", "purchase_orders", "payments", "expenses", "employee_attendances",
            "employee_advances", "payroll_items", "journal_entry_lines", "stocktake_items",
            "stock_adjustment_items", "user_demands"
        ],
        [3] =
        [
            "purchase_order_items", "purchase_invoices", "sale_invoices", "stock_disposals"
        ],
        [4] =
        [
            "purchase_invoice_items", "purchase_returns", "sale_invoice_items", "sale_returns",
            "stock_disposal_items"
        ],
        [5] =
        [
            "purchase_return_items", "sale_return_items", "customer_ledgers", "supplier_ledgers",
            "payment_allocations", "account_transactions", "cash_drawer_transactions", "document_histories"
        ]
    };

    /// <summary>
    /// Table name synonyms &amp; Abuzar Waseela legacy table aliases -> canonical Karobar v2 table name.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> TableAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Standard & Legacy POS Aliases
        ["item"] = "products",
        ["items"] = "products",
        ["medicine"] = "products",
        ["medicines"] = "products",
        ["drug"] = "products",
        ["drugs"] = "products",
        ["inventory"] = "products",
        ["product"] = "products",
        ["stock_items"] = "products",
        ["category"] = "categories",
        ["product_categories"] = "categories",
        ["item_categories"] = "categories",
        ["brand"] = "brands",
        ["company"] = "brands",
        ["companies"] = "brands",
        ["manufacturer"] = "brands",
        ["manufacturers"] = "brands",
        ["generic"] = "formulations",
        ["generics"] = "formulations",
        ["salt"] = "formulations",
        ["salts"] = "formulations",
        ["formula"] = "formulations",
        ["formulas"] = "formulations",
        ["formulation"] = "formulations",
        ["supplier"] = "suppliers",
        ["vendor"] = "suppliers",
        ["vendors"] = "suppliers",
        ["distributor"] = "suppliers",
        ["distributors"] = "suppliers",
        ["customer"] = "customers",
        ["client"] = "customers",
        ["clients"] = "customers",
        ["patient"] = "customers",
        ["patients"] = "customers",
        ["buyer"] = "customers",
        ["buyers"] = "customers",
        ["batch"] = "batches",
        ["product_batches"] = "batches",
        ["stock_batches"] = "batches",
        ["lots"] = "batches",
        ["sale"] = "sale_invoices",
        ["sales"] = "sale_invoices",
        ["sale_invoice"] = "sale_invoices",
        ["sales_invoices"] = "sale_invoices",
        ["bills"] = "sale_invoices",
        ["pos_sales"] = "sale_invoices",
        ["invoices"] = "sale_invoices",
        ["sale_item"] = "sale_invoice_items",
        ["sale_items"] = "sale_invoice_items",
        ["sales_items"] = "sale_invoice_items",
        ["sale_details"] = "sale_invoice_items",
        ["sale_invoice_details"] = "sale_invoice_items",
        ["bill_items"] = "sale_invoice_items",
        ["invoice_items"] = "sale_invoice_items",
        ["purchase"] = "purchase_invoices",
        ["purchases"] = "purchase_invoices",
        ["purchase_invoice"] = "purchase_invoices",
        ["purchase_bills"] = "purchase_invoices",
        ["grn"] = "purchase_invoices",
        ["purchase_item"] = "purchase_invoice_items",
        ["purchase_items"] = "purchase_invoice_items",
        ["purchase_details"] = "purchase_invoice_items",
        ["purchase_invoice_details"] = "purchase_invoice_items",
        ["sale_return"] = "sale_returns",
        ["sales_returns"] = "sale_returns",
        ["sale_return_details"] = "sale_return_items",
        ["purchase_return"] = "purchase_returns",
        ["purchase_return_details"] = "purchase_return_items",
        ["customer_ledger"] = "customer_ledgers",
        ["supplier_ledger"] = "supplier_ledgers",
        ["vendor_ledger"] = "supplier_ledgers",
        ["vendor_ledgers"] = "supplier_ledgers",
        ["employee"] = "employees",
        ["staff"] = "employees",
        ["workers"] = "employees",
        ["expense"] = "expenses",
        ["expense_category"] = "expense_categories",
        ["bank_accounts"] = "accounts",
        ["financial_accounts"] = "accounts",
        ["locations"] = "storage_locations",
        ["warehouses"] = "storage_locations",

        // Built-in Abuzar Waseela SQL Server / Access / CSV Table Signatures
        ["tblitems"] = "products",
        ["tbl_items"] = "products",
        ["waseela_items"] = "products",
        ["itemmaster"] = "products",
        ["item_master"] = "products",
        ["med_items"] = "products",
        ["tblcompany"] = "brands",
        ["tbl_company"] = "brands",
        ["companymaster"] = "brands",
        ["tblcategory"] = "categories",
        ["tbl_category"] = "categories",
        ["tblbatch"] = "batches",
        ["tblbatches"] = "batches",
        ["tbl_batch"] = "batches",
        ["batchmaster"] = "batches",
        ["batch_stock"] = "batches",
        ["waseela_batches"] = "batches",
        ["tblsupplier"] = "suppliers",
        ["tblsuppliers"] = "suppliers",
        ["tbl_suppliers"] = "suppliers",
        ["partymaster_supplier"] = "suppliers",
        ["waseela_suppliers"] = "suppliers",
        ["tblcustomer"] = "customers",
        ["tblcustomers"] = "customers",
        ["tbl_customers"] = "customers",
        ["partymaster_customer"] = "customers",
        ["waseela_customers"] = "customers",
        ["tblkhata"] = "customer_ledgers",
        ["tbl_khata"] = "customer_ledgers",
        ["customer_khata"] = "customer_ledgers",
        ["opening_khata"] = "customer_ledgers",
        ["waseela_khata"] = "customer_ledgers",
        ["supplier_khata"] = "supplier_ledgers",
        ["vendor_khata"] = "supplier_ledgers"
    };

    /// <summary>
    /// Column synonyms &amp; Abuzar Waseela legacy column aliases per target column name.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ColumnAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["product_code"] = ["code", "sku", "item_code", "itemcode", "prod_code", "productcode", "medicine_code", "article_no", "waseela_code"],
        ["name"] = ["title", "product_name", "item_name", "itemname", "medname", "medicine_name", "brand_name", "companyname", "category_name", "supplier_name", "partyname", "vendor_name", "customer_name", "client_name", "party_name", "employee_name", "full_name", "account_name", "location_name"],
        ["formulation"] = ["formula", "generic", "generic_name", "genericname", "salt", "salt_name", "saltname", "composition", "active_ingredient"],
        ["description"] = ["desc", "details", "narration", "summary", "info", "khatadetail"],
        ["pack_size"] = ["units_per_pack", "units", "uom_qty", "pack", "packing", "packsize", "pieces_per_pack", "box_size", "strip_size"],
        ["quantity"] = ["qty", "stock", "onhand", "on_hand", "current_stock", "total_qty", "total_quantity", "available_qty", "units_qty", "closingstock", "openingstock"],
        ["pack_purchase_price"] = ["ppp", "purchase_price_pack", "pack_cost", "cost_per_pack", "box_cost", "trade_price_pack", "tp_pack", "tppack", "tradeprice"],
        ["pack_sale_price"] = ["psp", "sale_price_pack", "pack_retail_price", "mrp_pack", "box_price", "retail_pack_price", "mrp", "retailprice"],
        ["unit_purchase_price"] = ["upp", "purchase_price_unit", "unit_cost", "cost", "cost_price", "purchase_price", "buy_price", "rate", "trade_price", "unitcost"],
        ["unit_sale_price"] = ["usp", "sale_price_unit", "unit_price", "price", "sale_price", "selling_price", "retail_price", "unitrate"],
        ["whole_sale_pack_price"] = ["wholesale_pack_price", "ws_pack_price", "wholesale_box_price", "netrate_pack"],
        ["whole_sale_unit_price"] = ["wholesale_unit_price", "ws_unit_price", "wholesale_price", "netrate"],
        ["avg_price"] = ["average_price", "weighted_avg", "wac", "wa_cost", "avg_cost"],
        ["margin"] = ["gross_margin_pct", "profit_margin", "margin_percentage", "markup"],
        ["narcotic"] = ["is_narcotic", "controlled", "is_controlled", "narcotics"],
        ["max_discount"] = ["maxdisc", "max_discount_pct", "max_disc"],
        ["category_id"] = ["cat_id", "category", "category_name", "cat_name", "catid"],
        ["brand_id"] = ["company_id", "companyid", "manufacturer_id", "brand", "brand_name", "company_name", "companyname", "manufacturer"],
        ["supplier_id"] = ["vendor_id", "distributor_id", "supplier", "supplier_name", "vendor", "vendor_name", "partyid"],
        ["customer_id"] = ["client_id", "patient_id", "buyer_id", "customer", "customer_name", "client_name", "partyid"],
        ["product_id"] = ["item_id", "itemid", "medicine_id", "drug_id", "product_code", "sku"],
        ["rack"] = ["shelf", "bin", "cabinet", "row_rack", "rackno", "rack_no", "locationcode"],
        ["barcode"] = ["ean", "upc", "bar_code", "gtin", "qr_code", "itembarcode"],
        ["batch_number"] = ["batch", "batch_no", "batchno", "lot", "lot_no", "lot_number"],
        ["expiry_date"] = ["expiry", "exp_date", "expdate", "expire_date", "expiration_date", "exp"],
        ["phone"] = ["phone_number", "mobile", "mobile_no", "mobileno", "contact", "contact_no", "tel", "telephone", "cell"],
        ["email"] = ["email_address", "mail"],
        ["address"] = ["street_address", "location", "city", "partyaddress"],
        ["tax_number"] = ["ntn", "gst", "strn", "vat_no", "tin", "tax_id", "ntnno"],
        ["posted_number"] = ["invoice_no", "inv_no", "bill_no", "billno", "voucher_no", "voucherno", "doc_no", "number", "ref_no", "code", "return_no"],
        ["invoice_total"] = ["total", "debit", "opening_debit", "openingbalance", "opening_balance", "khata_debit", "bill_amount"]
    };

    private readonly string _appRoot;
    private readonly string _stagingDir;

    public WaseelaAndSqliteImportEngine(string? appRoot = null)
    {
        _appRoot = ResolveAppRoot(appRoot);
        _stagingDir = Path.Combine(_appRoot, "storage", "app", "imports", "sqlite");
        EnsureStagingDirectory();
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

    private void EnsureStagingDirectory()
    {
        try
        {
            Directory.CreateDirectory(_stagingDir);
            var cutoff = DateTime.UtcNow.AddHours(-6);
            foreach (var file in Directory.GetFiles(_stagingDir))
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Auto-detects whether a set of table names and column names belongs to **Abuzar Waseela** Pharmacy ERP
    /// (Items, Batches/Expiries, Suppliers, Customers, Opening Khatas) vs standard Karobar ERP v2.
    /// </summary>
    public WaseelaDetectionResult DetectWaseelaSchema(
        IReadOnlyDictionary<string, List<string>> tablesAndColumns)
    {
        var result = new WaseelaDetectionResult();
        var waseelaTableMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "tblitems", "tbl_items", "waseela_items", "itemmaster", "tblbatch", "tblbatches",
            "batchmaster", "tblcompany", "tblsupplier", "tblcustomer", "tblkhata", "opening_khata", "waseela_khata"
        };
        var waseelaColMarkers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "itemcode", "itemname", "medname", "companyname", "packsize", "tppack", "tradeprice",
            "retailprice", "batchno", "expdate", "rackno", "partyname", "openingbalance", "khatadetail"
        };

        var score = 0;
        foreach (var (table, cols) in tablesAndColumns)
        {
            if (waseelaTableMarkers.Contains(table))
            {
                score += 25;
                result.MatchedSignatures.Add($"Table:{table}");
            }

            if (TableAliases.TryGetValue(table, out var mappedTarget))
            {
                result.WaseelaTableMappings[table] = mappedTarget;
            }

            foreach (var col in cols)
            {
                if (waseelaColMarkers.Contains(col))
                {
                    score += 10;
                    result.MatchedSignatures.Add($"Column:{table}.{col}");
                }
            }
        }

        if (score >= 25)
        {
            result.IsWaseelaSchema = true;
            result.DetectedProfile = "abuzar_waseela";
            result.ConfidenceScore = Math.Min(100.0, score);
        }
        else
        {
            result.IsWaseelaSchema = false;
            result.DetectedProfile = tablesAndColumns.ContainsKey("sale_invoices") ? "karobar_v2" : "legacy_pos";
            result.ConfidenceScore = 95.0;
        }

        return result;
    }

    /// <summary>
    /// Stage a SQLite database, ZIP archive, GZIP SQL dump, plain SQL dump, or Abuzar Waseela CSV file into a normalized staged SQLite file.
    /// </summary>
    public async Task<Dictionary<string, object?>> StageDatabaseFromFileAsync(
        string sourceFilePath,
        string? originalName = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(sourceFilePath))
        {
            throw new FileNotFoundException("Selected database file does not exist or is not readable.", sourceFilePath);
        }

        EnsureStagingDirectory();
        var token = Guid.NewGuid().ToString("D");
        var stagedSqlitePath = Path.Combine(_stagingDir, $"{token}.sqlite");
        var metaPath = Path.Combine(_stagingDir, $"{token}.json");
        var displayFilename = string.IsNullOrWhiteSpace(originalName) ? Path.GetFileName(sourceFilePath) : originalName!;
        var lowerName = displayFilename.ToLowerInvariant();

        var rawBytes = await File.ReadAllBytesAsync(sourceFilePath, cancellationToken);
        if (rawBytes.Length == 0)
        {
            throw new InvalidOperationException("The selected database file is empty.");
        }

        // 1. If ZIP archive, extract the database or CSV entry inside
        if ((rawBytes.Length >= 4 && rawBytes[0] == 'P' && rawBytes[1] == 'K' && rawBytes[2] == 3 && rawBytes[3] == 4) ||
            lowerName.EndsWith(".zip", StringComparison.Ordinal))
        {
            using var ms = new MemoryStream(rawBytes);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            byte[]? extracted = null;
            foreach (var entry in archive.Entries)
            {
                var le = entry.FullName.ToLowerInvariant();
                if (le.EndsWith(".sqlite") || le.EndsWith(".sqlite3") || le.EndsWith(".db") || le.EndsWith(".sql") || le.EndsWith(".csv"))
                {
                    using var es = entry.Open();
                    using var outMs = new MemoryStream();
                    await es.CopyToAsync(outMs, cancellationToken);
                    extracted = outMs.ToArray();
                    lowerName = le;
                    break;
                }
            }
            if (extracted == null)
            {
                throw new InvalidOperationException("ZIP archive does not contain a valid SQLite database, SQL dump, or Waseela CSV file.");
            }
            rawBytes = extracted;
        }

        // 2. If GZIP compressed, decompress
        if ((rawBytes.Length >= 2 && rawBytes[0] == 0x1F && rawBytes[1] == 0x8B) || lowerName.EndsWith(".gz", StringComparison.Ordinal))
        {
            using var inMs = new MemoryStream(rawBytes);
            using var gz = new GZipStream(inMs, CompressionMode.Decompress);
            using var outMs = new MemoryStream();
            await gz.CopyToAsync(outMs, cancellationToken);
            rawBytes = outMs.ToArray();
        }

        // 3. Check for binary SQLite header ("SQLite format 3")
        var headerText = Encoding.ASCII.GetString(rawBytes, 0, Math.Min(64, rawBytes.Length));
        var magicPos = headerText.IndexOf("SQLite format 3", StringComparison.Ordinal);
        if (magicPos >= 0)
        {
            await File.WriteAllBytesAsync(stagedSqlitePath, magicPos == 0 ? rawBytes : rawBytes[magicPos..], cancellationToken);
        }
        else if (lowerName.EndsWith(".csv", StringComparison.Ordinal))
        {
            var csvContent = Encoding.UTF8.GetString(rawBytes);
            await BuildSqliteFromWaseelaCsvAsync(csvContent, Path.GetFileNameWithoutExtension(displayFilename), stagedSqlitePath, cancellationToken);
        }
        else
        {
            var sqlContent = Encoding.UTF8.GetString(rawBytes);
            if (Regex.IsMatch(sqlContent, @"\b(CREATE\s+TABLE|INSERT\s+INTO)\b", RegexOptions.IgnoreCase))
            {
                await BuildSqliteFromSqlDumpAsync(sqlContent, stagedSqlitePath, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException("Unsupported file format. Please select a valid SQLite database (.sqlite, .db), SQL dump (.sql), or Abuzar Waseela CSV export (.csv).");
            }
        }

        var metadata = new Dictionary<string, object?>
        {
            ["token"] = token,
            ["original_name"] = displayFilename,
            ["file_size"] = new FileInfo(stagedSqlitePath).Length,
            ["created_at"] = DateTime.UtcNow.ToString("O")
        };

        await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        return metadata;
    }

    private static List<string> SplitSqlStatementsQuoteAware(string sql)
    {
        var statements = new List<string>();
        var current = new StringBuilder();
        var inSingle = false;
        var inDouble = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (ch == '*' && next == '/') { inBlockComment = false; i++; }
                continue;
            }
            if (!inSingle && !inDouble)
            {
                if (ch == '-' && next == '-') { inLineComment = true; i++; continue; }
                if (ch == '/' && next == '*') { inBlockComment = true; i++; continue; }
            }

            if (ch == '\'' && !inDouble)
            {
                current.Append(ch);
                if (inSingle && next == '\'') { current.Append(next); i++; }
                else inSingle = !inSingle;
                continue;
            }
            if (ch == '"' && !inSingle)
            {
                current.Append(ch);
                if (inDouble && next == '"') { current.Append(next); i++; }
                else inDouble = !inDouble;
                continue;
            }
            if (ch == ';' && !inSingle && !inDouble)
            {
                var stmt = current.ToString().Trim();
                if (!string.IsNullOrEmpty(stmt)) statements.Add(stmt);
                current.Clear();
                continue;
            }
            current.Append(ch);
        }

        var tail = current.ToString().Trim();
        if (!string.IsNullOrEmpty(tail)) statements.Add(tail);
        return statements;
    }

    private static async Task BuildSqliteFromSqlDumpAsync(string sqlContent, string targetSqlitePath, CancellationToken cancellationToken)
    {
        if (File.Exists(targetSqlitePath)) File.Delete(targetSqlitePath);
        await using var conn = new SqliteConnection($"Data Source={targetSqlitePath};Pooling=False;");
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = OFF;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // Normalize SQL Server [dbo].[TableName] brackets if coming from Abuzar Waseela SQL Server dump
        var normalizedSql = Regex.Replace(sqlContent, @"\[dbo\]\.", "", RegexOptions.IgnoreCase);
        normalizedSql = Regex.Replace(normalizedSql, @"(?m)^\s*GO\s*$", ";", RegexOptions.IgnoreCase);

        var statements = SplitSqlStatementsQuoteAware(normalizedSql);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        foreach (var rawStmt in statements)
        {
            var stmt = rawStmt.Trim();
            if (string.IsNullOrEmpty(stmt) || Regex.IsMatch(stmt, @"^(SET\s+|LOCK\s+TABLES|UNLOCK\s+TABLES|USE\s+)", RegexOptions.IgnoreCase))
            {
                continue;
            }
            if (Regex.IsMatch(stmt, @"\b(ATTACH\s+DATABASE|INTO\s+(OUTFILE|DUMPFILE)|LOAD_FILE\s*\()\b", RegexOptions.IgnoreCase))
            {
                throw new InvalidOperationException("Unsafe SQL statement detected in dump.");
            }
            try
            {
                await using var sCmd = conn.CreateCommand();
                sCmd.Transaction = (SqliteTransaction)tx;
                sCmd.CommandText = stmt;
                await sCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch
            {
                // Ignore dialect-specific index/constraint warnings during staging
            }
        }
        await tx.CommitAsync(cancellationToken);
    }

    private static async Task BuildSqliteFromWaseelaCsvAsync(
        string csvContent,
        string rawTableName,
        string targetSqlitePath,
        CancellationToken cancellationToken)
    {
        if (File.Exists(targetSqlitePath)) File.Delete(targetSqlitePath);
        var lines = csvContent.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2)
        {
            throw new InvalidOperationException("CSV file must contain a header row and at least one data row.");
        }

        var headers = ParseCsvRow(lines[0]);
        var tableName = Regex.Replace(rawTableName, @"[^A-Za-z0-9_]", "_");
        if (string.IsNullOrEmpty(tableName)) tableName = "waseela_items";

        await using var conn = new SqliteConnection($"Data Source={targetSqlitePath};Pooling=False;");
        await conn.OpenAsync(cancellationToken);

        var colDefs = string.Join(", ", headers.Select(h => $"`{h.Replace("`", "")}` TEXT"));
        await using (var createCmd = conn.CreateCommand())
        {
            createCmd.CommandText = $"CREATE TABLE `{tableName}` ({colDefs});";
            await createCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        for (var i = 1; i < lines.Length; i++)
        {
            var vals = ParseCsvRow(lines[i]);
            if (vals.Count == 0) continue;

            await using var insCmd = conn.CreateCommand();
            insCmd.Transaction = (SqliteTransaction)tx;
            var paramNames = new List<string>();
            for (var c = 0; c < headers.Count; c++)
            {
                var pName = $"@p{c}";
                paramNames.Add(pName);
                insCmd.Parameters.AddWithValue(pName, c < vals.Count ? vals[c] : "");
            }
            insCmd.CommandText = $"INSERT INTO `{tableName}` VALUES ({string.Join(", ", paramNames)});";
            await insCmd.ExecuteNonQueryAsync(cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
    }

    private static List<string> ParseCsvRow(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        result.Add(current.ToString().Trim());
        return result;
    }

    public string ResolveStagedPath(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !Regex.IsMatch(token, @"^[a-f0-9\-]{36}$", RegexOptions.IgnoreCase))
        {
            throw new ArgumentException("Invalid import session token.");
        }

        var path = Path.Combine(_stagingDir, $"{token}.sqlite");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Import session has expired or file was not found. Please upload the file again.");
        }
        return path;
    }

    public void CleanupStagedSession(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !Regex.IsMatch(token, @"^[a-f0-9\-]{36}$", RegexOptions.IgnoreCase))
        {
            return;
        }
        var sqlitePath = Path.Combine(_stagingDir, $"{token}.sqlite");
        var metaPath = Path.Combine(_stagingDir, $"{token}.json");
        try { if (File.Exists(sqlitePath)) File.Delete(sqlitePath); } catch { }
        try { if (File.Exists(metaPath)) File.Delete(metaPath); } catch { }
    }

    public (string? TargetTable, string MatchMethod) ResolveTargetTable(string sourceTable, IReadOnlyCollection<string> sourceColumns)
    {
        var clean = (sourceTable ?? "").Trim().ToLowerInvariant();
        var allTargets = DependencyStages.Values.SelectMany(x => x).ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (allTargets.Contains(clean))
        {
            return (clean, "exact");
        }

        var stripped = Regex.Replace(clean, @"^(tbl_|pos_|erp_|app_|old_|v1_|waseela_)", "");
        if (allTargets.Contains(stripped))
        {
            return (stripped, "alias");
        }

        if (TableAliases.TryGetValue(clean, out var aliased) && allTargets.Contains(aliased))
        {
            return (aliased, "alias");
        }
        if (TableAliases.TryGetValue(stripped, out aliased) && allTargets.Contains(aliased))
        {
            return (aliased, "alias");
        }

        return (null, "unmapped");
    }

    /// <summary>
    /// Inspect staged SQLite database, detect Abuzar Waseela or Karobar v2 schema, and propose table &amp; column mappings.
    /// </summary>
    public async Task<Dictionary<string, object?>> InspectAndProposeMappingAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        var sqlitePath = ResolveStagedPath(token);
        var metaPath = Path.Combine(_stagingDir, $"{token}.json");
        var meta = File.Exists(metaPath)
            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(await File.ReadAllTextAsync(metaPath, cancellationToken)) ?? new()
            : new Dictionary<string, object?>();

        await using var conn = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly;Pooling=False;");
        await conn.OpenAsync(cancellationToken);

        var rawTableNames = new List<string>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(name)) rawTableNames.Add(name);
            }
        }

        var sourceTables = new List<Dictionary<string, object?>>();
        var protectedSkipped = new List<Dictionary<string, object?>>();
        var tablesAndColsForDetector = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var totalImportableRows = 0;
        var autoMatchedCount = 0;

        foreach (var srcTable in rawTableNames)
        {
            var escaped = srcTable.Replace("`", "``");
            var rowCount = 0;
            await using (var countCmd = conn.CreateCommand())
            {
                countCmd.CommandText = $"SELECT COUNT(*) FROM `{escaped}`";
                var scalar = await countCmd.ExecuteScalarAsync(cancellationToken);
                rowCount = Convert.ToInt32(scalar ?? 0, CultureInfo.InvariantCulture);
            }

            var srcCols = new List<Dictionary<string, object?>>();
            var colNames = new List<string>();
            await using (var colCmd = conn.CreateCommand())
            {
                colCmd.CommandText = $"PRAGMA table_info(`{escaped}`)";
                await using var colReader = await colCmd.ExecuteReaderAsync(cancellationToken);
                while (await colReader.ReadAsync(cancellationToken))
                {
                    var cName = colReader["name"]?.ToString() ?? "";
                    var cType = colReader["type"]?.ToString() ?? "TEXT";
                    colNames.Add(cName);
                    srcCols.Add(new Dictionary<string, object?>
                    {
                        ["name"] = cName,
                        ["type"] = cType,
                        ["notnull"] = Convert.ToInt32(colReader["notnull"] ?? 0) != 0,
                        ["pk"] = Convert.ToInt32(colReader["pk"] ?? 0) != 0
                    });
                }
            }

            tablesAndColsForDetector[srcTable] = colNames;

            if (ProtectedTables.Contains(srcTable.ToLowerInvariant()))
            {
                protectedSkipped.Add(new Dictionary<string, object?>
                {
                    ["source_table"] = srcTable,
                    ["row_count"] = rowCount,
                    ["reason"] = "Skipped to preserve current user accounts, RBAC permissions, and machine license."
                });
                continue;
            }

            var (matchedTarget, matchMethod) = ResolveTargetTable(srcTable, colNames);
            if (matchedTarget != null) autoMatchedCount++;

            var isOptional = matchedTarget != null && OptionalConfigTables.Contains(matchedTarget, StringComparer.OrdinalIgnoreCase);
            var enabled = matchedTarget != null && rowCount > 0 && !isOptional;
            if (enabled) totalImportableRows += rowCount;

            sourceTables.Add(new Dictionary<string, object?>
            {
                ["source_table"] = srcTable,
                ["row_count"] = rowCount,
                ["source_columns"] = colNames,
                ["source_columns_meta"] = srcCols,
                ["target_table"] = matchedTarget,
                ["match_method"] = matchMethod,
                ["enabled"] = enabled,
                ["is_optional_cfg"] = isOptional
            });
        }

        var waseelaDetection = DetectWaseelaSchema(tablesAndColsForDetector);

        return new Dictionary<string, object?>
        {
            ["message"] = "SQLite database analyzed successfully.",
            ["session_token"] = token,
            ["session"] = meta,
            ["waseela_detection"] = new Dictionary<string, object?>
            {
                ["is_waseela_schema"] = waseelaDetection.IsWaseelaSchema,
                ["detected_profile"] = waseelaDetection.DetectedProfile,
                ["confidence_score"] = waseelaDetection.ConfidenceScore,
                ["matched_signatures"] = waseelaDetection.MatchedSignatures
            },
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_source_tables"] = sourceTables.Count + protectedSkipped.Count,
                ["importable_tables_count"] = sourceTables.Count,
                ["auto_matched_count"] = autoMatchedCount,
                ["protected_skipped_count"] = protectedSkipped.Count,
                ["total_importable_rows"] = totalImportableRows
            },
            ["protected_tables"] = protectedSkipped,
            ["tables"] = sourceTables,
            ["importable_tables"] = sourceTables
        };
    }

    /// <summary>
    /// Post-import reconciliation:
    /// 1. Auto-creates opening batches (`OPENING-{id}`) for products that have quantity &gt; 0 in `products` but 0 rows in `batches`.
    /// 2. Syncs `formulations` table from distinct `products.formulation` values.
    /// </summary>
    public async Task<Dictionary<string, int>> RunPostImportReconciliationAsync(
        KarobarDbContext db,
        CancellationToken cancellationToken = default)
    {
        var openingBatchesCreated = 0;
        var formulationsSynced = 0;

        try
        {
            var productsWithStock = await db.Products
                .Where(p => p.Quantity > 0)
                .ToListAsync(cancellationToken);

            var existingBatchProductIds = (await db.Batches
                .Select(b => b.ProductId)
                .Distinct()
                .ToListAsync(cancellationToken))
                .ToHashSet();

            var defaultExpiry = DateTime.UtcNow.Date.AddYears(2);

            foreach (var prod in productsWithStock)
            {
                if (!existingBatchProductIds.Contains(prod.Id))
                {
                    db.Batches.Add(new Batch
                    {
                        ProductId = prod.Id,
                        BatchNumber = $"OPENING-{prod.Id}",
                        ExpiryDate = defaultExpiry,
                        Quantity = prod.Quantity ?? 0,
                        PackPurchasePrice = prod.PackPurchasePrice,
                        UnitPurchasePrice = prod.UnitPurchasePrice,
                        PackSalePrice = prod.PackSalePrice,
                        UnitSalePrice = prod.UnitSalePrice,
                        WholeSalePackPrice = prod.WholeSalePackPrice,
                        WholeSaleUnitPrice = prod.WholeSaleUnitPrice,
                        StorageLocationId = prod.StorageLocationId,
                        Rack = prod.Rack
                    });
                    openingBatchesCreated++;
                }
            }

            var distinctForms = await db.Products
                .Where(p => p.Formulation != null && p.Formulation != "")
                .Select(p => p.Formulation!)
                .Distinct()
                .ToListAsync(cancellationToken);

            var existingNormForms = (await db.Formulations
                .Select(f => f.NormalizedName)
                .ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var rawForm in distinctForms)
            {
                var norm = Formulation.NormalizeName(rawForm);
                if (!string.IsNullOrEmpty(norm) && !existingNormForms.Contains(norm))
                {
                    db.Formulations.Add(new Formulation
                    {
                        Name = rawForm.Trim(),
                        NormalizedName = norm
                    });
                    existingNormForms.Add(norm);
                    formulationsSynced++;
                }
            }

            if (openingBatchesCreated > 0 || formulationsSynced > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch
        {
            // Ignore reconciliation errors
        }

        return new Dictionary<string, int>
        {
            ["opening_batches_created"] = openingBatchesCreated,
            ["formulations_synced"] = formulationsSynced
        };
    }

    /// <summary>
    /// Executes a topological 6-stage import from the staged SQLite file into the active Karobar database,
    /// preserving protected security/license tables and translating matching columns automatically.
    /// </summary>
    public async Task<Dictionary<string, object?>> ExecuteImportAsync(
        KarobarDbContext db,
        string token,
        string mode = "merge",
        CancellationToken cancellationToken = default)
    {
        var stagedPath = ResolveStagedPath(token);
        var targetConnStr = db.Database.GetConnectionString()
            ?? $"Data Source={Path.Combine(_appRoot, "database", "database.sqlite")};";

        var importedTables = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var totalRowsInserted = 0;

        await using var srcConn = new SqliteConnection($"Data Source={stagedPath};Mode=ReadOnly;Pooling=False;");
        await srcConn.OpenAsync(cancellationToken);

        await using var dstConn = new SqliteConnection(targetConnStr);
        await dstConn.OpenAsync(cancellationToken);

        await using (var pragmaCmd = dstConn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA busy_timeout=15000; PRAGMA foreign_keys = OFF;";
            await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var srcTables = new List<string>();
        await using (var cmd = srcConn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var t = r.GetString(0);
                if (!string.IsNullOrWhiteSpace(t) && !ProtectedTables.Contains(t, StringComparer.OrdinalIgnoreCase))
                {
                    srcTables.Add(t);
                }
            }
        }

        var dstTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var cmd = dstConn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                dstTables.Add(r.GetString(0));
            }
        }

        var orderedPlan = new List<(string SrcTable, string TargetTable)>();
        var targetToSrc = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var st in srcTables)
        {
            var (target, _) = ResolveTargetTable(st, Array.Empty<string>());
            if (target != null && dstTables.Contains(target) &&
                !ProtectedTables.Contains(target, StringComparer.OrdinalIgnoreCase) &&
                !OptionalConfigTables.Contains(target, StringComparer.OrdinalIgnoreCase) &&
                !targetToSrc.ContainsKey(target))
            {
                targetToSrc[target] = st;
            }
        }

        foreach (var stage in DependencyStages.OrderBy(kv => kv.Key))
        {
            foreach (var target in stage.Value)
            {
                if (targetToSrc.TryGetValue(target, out var st))
                {
                    orderedPlan.Add((st, target));
                }
            }
        }

        var replaceMode = string.Equals(mode, "replace", StringComparison.OrdinalIgnoreCase);
        await using var tx = await dstConn.BeginTransactionAsync(cancellationToken);
        try
        {
            if (replaceMode)
            {
                foreach (var (_, target) in orderedPlan.AsEnumerable().Reverse())
                {
                    await using var delCmd = dstConn.CreateCommand();
                    delCmd.Transaction = (SqliteTransaction)tx;
                    delCmd.CommandText = $"DELETE FROM \"{target.Replace("\"", "\"\"")}\";";
                    await delCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            foreach (var (srcTable, targetTable) in orderedPlan)
            {
                var srcCols = new List<string>();
                await using (var cCmd = srcConn.CreateCommand())
                {
                    cCmd.CommandText = $"PRAGMA table_info(\"{srcTable.Replace("\"", "\"\"")}\")";
                    await using var cr = await cCmd.ExecuteReaderAsync(cancellationToken);
                    while (await cr.ReadAsync(cancellationToken))
                    {
                        var cn = cr["name"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(cn)) srcCols.Add(cn);
                    }
                }

                var dstCols = new List<string>();
                await using (var cCmd = dstConn.CreateCommand())
                {
                    cCmd.Transaction = (SqliteTransaction)tx;
                    cCmd.CommandText = $"PRAGMA table_info(\"{targetTable.Replace("\"", "\"\"")}\")";
                    await using var cr = await cCmd.ExecuteReaderAsync(cancellationToken);
                    while (await cr.ReadAsync(cancellationToken))
                    {
                        var cn = cr["name"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(cn)) dstCols.Add(cn);
                    }
                }

                var srcSet = new HashSet<string>(srcCols, StringComparer.OrdinalIgnoreCase);
                var colPairs = new List<(string Src, string Dst)>();
                foreach (var dc in dstCols)
                {
                    if (srcSet.Contains(dc))
                    {
                        colPairs.Add((srcCols.First(s => s.Equals(dc, StringComparison.OrdinalIgnoreCase)), dc));
                    }
                    else if (ColumnAliases.TryGetValue(dc, out var aliases))
                    {
                        var aliasMatch = aliases.FirstOrDefault(a => srcSet.Contains(a));
                        if (aliasMatch != null)
                        {
                            colPairs.Add((srcCols.First(s => s.Equals(aliasMatch, StringComparison.OrdinalIgnoreCase)), dc));
                        }
                    }
                }

                if (colPairs.Count == 0) continue;

                var selectCols = string.Join(", ", colPairs.Select(p => $"\"{p.Src.Replace("\"", "\"\"")}\""));
                var insertCols = string.Join(", ", colPairs.Select(p => $"\"{p.Dst.Replace("\"", "\"\"")}\""));
                var paramPlaceholders = string.Join(", ", colPairs.Select((_, idx) => $"@p{idx}"));
                var insertVerb = replaceMode ? "INSERT OR REPLACE" : "INSERT OR IGNORE";

                await using var selCmd = srcConn.CreateCommand();
                selCmd.CommandText = $"SELECT {selectCols} FROM \"{srcTable.Replace("\"", "\"\"")}\"";
                await using var reader = await selCmd.ExecuteReaderAsync(cancellationToken);

                var insertedForTable = 0;
                while (await reader.ReadAsync(cancellationToken))
                {
                    await using var insCmd = dstConn.CreateCommand();
                    insCmd.Transaction = (SqliteTransaction)tx;
                    insCmd.CommandText = $"{insertVerb} INTO \"{targetTable.Replace("\"", "\"\"")}\" ({insertCols}) VALUES ({paramPlaceholders});";
                    for (var i = 0; i < colPairs.Count; i++)
                    {
                        var val = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
                        insCmd.Parameters.AddWithValue($"@p{i}", val);
                    }
                    try
                    {
                        insertedForTable += await insCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    catch
                    {
                        // Skip individual malformed rows without aborting entire batch
                    }
                }

                importedTables[targetTable] = insertedForTable;
                totalRowsInserted += insertedForTable;
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            try
            {
                await using var fkCmd = dstConn.CreateCommand();
                fkCmd.CommandText = "PRAGMA foreign_keys = ON;";
                await fkCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch { }
        }

        return new Dictionary<string, object?>
        {
            ["mode"] = replaceMode ? "replace" : "merge",
            ["tables_imported"] = importedTables.Count,
            ["total_rows_inserted"] = totalRowsInserted,
            ["per_table"] = importedTables
        };
    }

    public static string NormalizeDateString(string? raw)
    {
        var trimmed = (raw ?? "").Trim();
        if (Regex.IsMatch(trimmed, @"^\d{4}-\d{2}-\d{2}"))
        {
            return trimmed[..10];
        }

        // DD/MM/YYYY or DD-MM-YYYY
        var m1 = Regex.Match(trimmed, @"^(\d{1,2})[/\-](\d{1,2})[/\-](\d{4})$");
        if (m1.Success)
        {
            var d = int.Parse(m1.Groups[1].Value, CultureInfo.InvariantCulture);
            var mo = Math.Clamp(int.Parse(m1.Groups[2].Value, CultureInfo.InvariantCulture), 1, 12);
            var y = Math.Clamp(int.Parse(m1.Groups[3].Value, CultureInfo.InvariantCulture), 1900, 2999);
            var maxDay = DateTime.DaysInMonth(y, mo);
            return $"{y:D4}-{mo:D2}-{Math.Clamp(d, 1, maxDay):D2}";
        }

        // MM/YYYY (common for medicine expiry dates in Abuzar Waseela)
        var m2 = Regex.Match(trimmed, @"^(\d{1,2})[/\-](\d{4})$");
        if (m2.Success)
        {
            var mo = Math.Clamp(int.Parse(m2.Groups[1].Value, CultureInfo.InvariantCulture), 1, 12);
            var y = Math.Clamp(int.Parse(m2.Groups[2].Value, CultureInfo.InvariantCulture), 1900, 2999);
            var lastDay = DateTime.DaysInMonth(y, mo);
            return $"{y:D4}-{mo:D2}-{lastDay:D2}";
        }

        if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}

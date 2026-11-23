using System;
using System.Collections.Concurrent;
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

public sealed class ColumnMappingOverride
{
    public string? SourceColumn { get; set; }
    public string? DefaultValue { get; set; }
}

public sealed class TableImportOverride
{
    public bool Enabled { get; set; } = true;
    public string? TargetTable { get; set; }
    public Dictionary<string, ColumnMappingOverride> ColumnMap { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class SqliteImportExecutionOptions
{
    public string Mode { get; set; } = "merge"; // "merge" | "replace"
    public string ConflictPolicy { get; set; } = "upsert"; // "upsert" | "skip"
    public bool ReconcileBatches { get; set; } = true;
    public long CurrentUserId { get; set; } = 1;
    public Dictionary<string, TableImportOverride> Tables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TargetColumnSchema
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "TEXT";
    public bool NotNull { get; set; }
    public string? DefaultValue { get; set; }
    public bool Pk { get; set; }
}

public sealed class TargetTableSchema
{
    public string Table { get; set; } = "";
    public string Label { get; set; } = "";
    public int Stage { get; set; } = 6;
    public bool IsOptionalConfig { get; set; }
    public List<TargetColumnSchema> Columns { get; set; } = [];
}

/// <summary>
/// Intelligent SQLite + Abuzar Waseela Importer (unified native port of `app/Services/SqliteImportService.php`).
/// Supports:
/// - Staging &amp; validating `.sqlite`, `.sqlite3`, `.db`, `.db3`, `.zip`, `.gz`, `.sql.gz`, `.sql`, and Abuzar Waseela `.csv` / SQL Server dumps
/// - WAL/SHM checkpointing, PRAGMA quick_check, and 2 GB bounded decompression
/// - Built-in **Abuzar Waseela** schema auto-detector &amp; mapping profile (Items, Batches/Expiries, Suppliers, Customers, Opening Khatas)
/// - Strictly protected security/auth/license/RBAC table exclusion (`ProtectedTables`)
/// - Topological 6-stage dependency ordering (`DependencyStages`), full parent->child `idMap` FK ID translation,
///   polymorphic `party_id`/`invoice_id` &amp; user FK remapping, natural-key upsert/skip, fallback master synthesis,
///   and post-import Opening Batch &amp; Formulation reconciliation.
/// </summary>
public sealed class WaseelaAndSqliteImportEngine
{
    private const long MaxDecompressedBytes = 2L * 1024 * 1024 * 1024; // 2 GB cap
    private const long MaxTextImportBytes = 256L * 1024 * 1024; // 256 MB cap for in-memory CSV/SQL text parsing

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ImportLocks =
        new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    private static SemaphoreSlim GetImportLock(string targetSqlitePath)
    {
        var key = string.IsNullOrWhiteSpace(targetSqlitePath)
            ? "__default__"
            : Path.GetFullPath(targetSqlitePath);
        return ImportLocks.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
    }

    private static bool IsMacOsMetadataZipEntry(string fullName)
    {
        var segments = fullName.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return true;
        if (segments.Any(seg => seg.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase))) return true;
        return segments[^1].StartsWith("._", StringComparison.Ordinal);
    }

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
        "barcode_templates",
        "terminals"
    ];

    private static readonly string[] ExcludedSettingsColumns =
    [
        "id",
        "is_setup_completed",
        "setup_completed_at",
        "terms_accepted_at",
        "privacy_accepted_at",
        "legal_accepted_version",
        "cube_groq_api_key",
        "cube_gemini_api_key",
        "whatsapp_access_token",
        "smtp_password",
        "network_server_token",
        "fbr_bearer_token",
        "created_at",
        "updated_at"
    ];

    public static readonly string[] UserForeignKeyColumns =
    [
        "user_id",
        "current_user_id",
        "created_by",
        "updated_by",
        "received_by",
        "reversed_by",
        "reversed_by_id",
        "created_by_id",
        "recorded_by",
        "recorded_by_id",
        "generated_by",
        "counted_by",
        "disposed_by",
        "restored_by",
        "requested_by"
    ];

    public static readonly IReadOnlyDictionary<int, string[]> DependencyStages = new Dictionary<int, string[]>
    {
        [0] =
        [
            "settings", "theme_settings", "barcode_templates", "storage_locations", "tax_rates",
            "chart_of_accounts", "accounts", "expense_categories", "formulations", "categories",
            "brands", "suppliers", "customers", "employees", "doctors", "document_sequences", "terminals"
        ],
        [1] =
        [
            "products", "trade_schemes", "cash_drawer_sessions", "payroll_runs",
            "journal_entries", "stock_adjustments", "stocktake_sessions"
        ],
        [2] =
        [
            "product_suppliers", "supplier_product_positions", "customer_regular_items", "customer_whole_sale_prices",
            "batches", "trade_scheme_slabs", "cold_chain_logs", "purchase_orders", "payments", "expenses",
            "employee_attendances", "payroll_items", "journal_entry_lines",
            "stocktake_items", "stock_adjustment_items", "user_demands"
        ],
        [3] =
        [
            "purchase_order_items", "purchase_invoices", "sale_invoices", "stock_disposals", "employee_advances"
        ],
        [4] =
        [
            "purchase_invoice_items", "purchase_returns", "sale_invoice_items", "sale_returns",
            "stock_disposal_items", "invoice_drafts"
        ],
        [5] =
        [
            "purchase_return_items", "sale_return_items", "customer_ledgers", "supplier_ledgers",
            "payment_allocations", "account_transactions", "cash_drawer_transactions", "document_histories"
        ]
    };

    private sealed record ForeignKeyRule(string ParentTable, bool Nullable, string? FallbackMode);

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, ForeignKeyRule>> ForeignKeyRules =
        new Dictionary<string, IReadOnlyDictionary<string, ForeignKeyRule>>(StringComparer.OrdinalIgnoreCase)
        {
            ["products"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["category_id"] = new("categories", false, "category"),
                ["brand_id"] = new("brands", false, "brand"),
                ["supplier_id"] = new("suppliers", true, "supplier_optional"),
                ["storage_location_id"] = new("storage_locations", true, null)
            },
            ["batches"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["product_id"] = new("products", false, "skip_row"),
                ["storage_location_id"] = new("storage_locations", true, null)
            },
            ["product_suppliers"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["product_id"] = new("products", false, "skip_row"),
                ["supplier_id"] = new("suppliers", false, "skip_row")
            },
            ["supplier_product_positions"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["product_id"] = new("products", false, "skip_row"),
                ["supplier_id"] = new("suppliers", false, "skip_row")
            },
            ["customer_regular_items"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_id"] = new("customers", false, "skip_row"),
                ["product_id"] = new("products", false, "skip_row")
            },
            ["customer_whole_sale_prices"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_id"] = new("customers", false, "skip_row"),
                ["product_id"] = new("products", false, "skip_row")
            },
            ["trade_schemes"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_id"] = new("customers", true, null)
            },
            ["trade_scheme_slabs"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["trade_scheme_id"] = new("trade_schemes", false, "skip_row"),
                ["bonus_product_id"] = new("products", true, null)
            },
            ["cold_chain_logs"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["storage_location_id"] = new("storage_locations", true, null)
            },
            ["purchase_orders"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["supplier_id"] = new("suppliers", false, "supplier")
            },
            ["purchase_order_items"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["purchase_order_id"] = new("purchase_orders", false, "skip_row"),
                ["product_id"] = new("products", false, "skip_row")
            },
            ["purchase_invoices"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["supplier_id"] = new("suppliers", false, "supplier"),
                ["purchase_order_id"] = new("purchase_orders", true, null),
                ["account_id"] = new("accounts", true, null),
                ["employee_id"] = new("employees", true, null)
            },
            ["purchase_invoice_items"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["purchase_invoice_id"] = new("purchase_invoices", false, "skip_row"),
                ["product_id"] = new("products", false, "skip_row"),
                ["purchase_order_item_id"] = new("purchase_order_items", true, null)
            },
            ["purchase_returns"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["supplier_id"] = new("suppliers", false, "supplier"),
                ["purchase_invoice_id"] = new("purchase_invoices", true, null),
                ["account_id"] = new("accounts", true, null),
                ["employee_id"] = new("employees", true, null)
            },
            ["purchase_return_items"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["purchase_return_id"] = new("purchase_returns", false, "skip_row"),
                ["product_id"] = new("products", false, "skip_row")
            },
            ["sale_invoices"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_id"] = new("customers", false, "customer"),
                ["account_id"] = new("accounts", true, null),
                ["salesman_id"] = new("employees", true, null),
                ["doctor_id"] = new("doctors", true, null),
                ["terminal_id"] = new("terminals", true, null)
            },
            ["sale_invoice_items"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["sale_invoice_id"] = new("sale_invoices", false, "skip_row"),
                ["product_id"] = new("products", false, "skip_row"),
                ["applied_scheme_id"] = new("trade_schemes", true, null)
            },
            ["sale_returns"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_id"] = new("customers", false, "customer"),
                ["sale_invoice_id"] = new("sale_invoices", true, null),
                ["account_id"] = new("accounts", true, null),
                ["employee_id"] = new("employees", true, null),
                ["terminal_id"] = new("terminals", true, null)
            },
            ["sale_return_items"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["sale_return_id"] = new("sale_returns", false, "skip_row"),
                ["product_id"] = new("products", false, "skip_row")
            },
            ["customer_ledgers"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_id"] = new("customers", false, "customer"),
                ["sale_invoice_id"] = new("sale_invoices", true, null),
                ["sale_return_id"] = new("sale_returns", true, null),
                ["payment_id"] = new("payments", true, null)
            },
            ["supplier_ledgers"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["supplier_id"] = new("suppliers", false, "supplier"),
                ["purchase_invoice_id"] = new("purchase_invoices", true, null),
                ["purchase_return_id"] = new("purchase_returns", true, null),
                ["payment_id"] = new("payments", true, null)
            },
            ["payments"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["account_id"] = new("accounts", true, null),
                ["wht_tax_rate_id"] = new("tax_rates", true, null),
                ["terminal_id"] = new("terminals", true, null)
            },
            ["payment_allocations"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["payment_id"] = new("payments", false, "skip_row")
            },
            ["expenses"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["expense_category_id"] = new("expense_categories", false, "expense_category"),
                ["account_id"] = new("accounts", true, null),
                ["cash_drawer_session_id"] = new("cash_drawer_sessions", true, null),
                ["terminal_id"] = new("terminals", true, null)
            },
            ["account_transactions"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["account_id"] = new("accounts", false, "skip_row")
            },
            ["cash_drawer_sessions"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["terminal_id"] = new("terminals", true, null)
            },
            ["cash_drawer_transactions"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["session_id"] = new("cash_drawer_sessions", false, "skip_row"),
                ["terminal_id"] = new("terminals", true, null)
            },
            ["invoice_drafts"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["terminal_id"] = new("terminals", true, null)
            },
            ["document_histories"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["terminal_id"] = new("terminals", true, null)
            },
            ["journal_entries"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["account_id"] = new("accounts", true, null)
            },
            ["journal_entry_lines"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["journal_entry_id"] = new("journal_entries", false, "skip_row"),
                ["chart_of_account_id"] = new("chart_of_accounts", false, "skip_row")
            },
            ["employee_attendances"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["employee_id"] = new("employees", false, "skip_row")
            },
            ["employee_advances"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["employee_id"] = new("employees", false, "skip_row"),
                ["account_id"] = new("accounts", true, null),
                ["payroll_item_id"] = new("payroll_items", true, null)
            },
            ["payroll_items"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["payroll_run_id"] = new("payroll_runs", false, "skip_row"),
                ["employee_id"] = new("employees", false, "skip_row"),
                ["account_id"] = new("accounts", true, null),
                ["expense_id"] = new("expenses", true, null)
            },
            ["stock_adjustment_items"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["stock_adjustment_id"] = new("stock_adjustments", false, "skip_row"),
                ["product_id"] = new("products", false, "skip_row")
            },
            ["stock_disposals"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["stock_adjustment_id"] = new("stock_adjustments", true, null),
                ["expense_id"] = new("expenses", true, null)
            },
            ["stock_disposal_items"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["stock_disposal_id"] = new("stock_disposals", false, "skip_row"),
                ["product_id"] = new("products", false, "skip_row"),
                ["batch_id"] = new("batches", true, null)
            },
            ["stocktake_sessions"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["stock_adjustment_id"] = new("stock_adjustments", true, null),
                ["storage_location_id"] = new("storage_locations", true, null)
            },
            ["stocktake_items"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["stocktake_session_id"] = new("stocktake_sessions", false, "skip_row"),
                ["product_id"] = new("products", false, "skip_row")
            },
            ["user_demands"] = new Dictionary<string, ForeignKeyRule>(StringComparer.OrdinalIgnoreCase)
            {
                ["product_id"] = new("products", false, "skip_row"),
                ["customer_id"] = new("customers", true, null)
            }
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
        ["description"] = ["desc", "details", "narration", "summary", "info", "khatadetail", "particulars"],
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
        ["customer_id"] = ["client_id", "patient_id", "buyer_id", "customer", "customer_name", "client_name", "partyid", "partyname", "party_name"],
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
        ["invoice_number"] = ["bill_no", "billno", "supplier_invoice_no", "ref_no", "inv_no"],
        ["date"] = ["invoice_date", "sale_date", "bill_date", "inv_date", "trans_date", "created_date"],
        ["posted_date"] = ["invoice_date", "purchase_date", "bill_date", "inv_date", "trans_date"],
        ["entry_date"] = ["date", "khata_date", "trans_date", "ledger_date"],
        ["gross_amount"] = ["gross", "sub_total", "subtotal", "amount_before_discount", "total_amount"],
        ["total"] = ["net_total", "grand_total", "invoice_total", "net_amount", "bill_amount", "amount"],
        ["total_amount"] = ["gross_amount", "gross", "sub_total", "subtotal", "amount"],
        ["invoice_amount"] = ["net_amount", "grand_total", "net_total", "total", "bill_amount"],
        ["total_receive"] = ["received", "paid", "paid_amount", "cash_received", "amount_paid"],
        ["total_paid"] = ["paid", "paid_amount", "amount_paid"],
        ["price"] = ["unit_price", "unit_sale_price", "sale_price", "rate", "selling_price"],
        ["sub_total"] = ["subtotal", "line_total", "total", "amount", "net_amount"],
        ["invoice_total"] = ["total", "debit", "opening_debit", "openingbalance", "opening_balance", "khata_debit", "bill_amount"],
        ["notes"] = ["note", "remarks", "description", "comments"],
        ["employee_code"] = ["emp_code", "staff_code", "code", "emp_no"],
        ["basic_salary"] = ["salary", "base_salary", "monthly_salary", "pay"]
    };

    private readonly string _appRoot;
    private readonly string _stagingDir;

    public string AppRoot => _appRoot;

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
        var envRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH")
            ?? Environment.GetEnvironmentVariable("KAROBAR_BASE_PATH")
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
            // Ignore cleanup errors
        }
    }

    public static int GetStageForTable(string tableName)
    {
        foreach (var kv in DependencyStages)
        {
            if (kv.Value.Contains(tableName, StringComparer.OrdinalIgnoreCase))
            {
                return kv.Key;
            }
        }
        return 6;
    }

    private static int GetStageOrderIndex(int stage, string tableName)
    {
        if (DependencyStages.TryGetValue(stage, out var orderedTables))
        {
            for (var i = 0; i < orderedTables.Length; i++)
            {
                if (orderedTables[i].Equals(tableName, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }
        return int.MaxValue;
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

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long maxBytes, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidOperationException("Decompressed database exceeds the maximum allowed size limit (2 GB).");
            }
            await destination.WriteAsync(buffer, 0, read, cancellationToken);
        }
    }

    /// <summary>
    /// Stage a SQLite database, ZIP archive, GZIP SQL dump, plain SQL dump, or Abuzar Waseela CSV file into a normalized staged SQLite file.
    /// Automatically copies sibling `-wal`/`-shm` files, runs `PRAGMA wal_checkpoint(TRUNCATE);`, enforces 2 GB decompression limit,
    /// and validates integrity with `PRAGMA quick_check;`.
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
        var effectiveEntryName = displayFilename;
        var lowerName = displayFilename.ToLowerInvariant();

        var srcInfo = new FileInfo(sourceFilePath);
        if (!srcInfo.Exists || srcInfo.Length == 0)
        {
            throw new InvalidOperationException("The selected database file is empty.");
        }

        var workingTempPath = Path.Combine(_stagingDir, $"{token}.stage.tmp");
        var copiedDirectFromDisk = false;

        try
        {
            // Read initial header bytes
            var hdr = new byte[(int)Math.Min(64L, Math.Max(0L, srcInfo.Length))];
            await using (var hdrFs = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                _ = await hdrFs.ReadAsync(hdr, 0, hdr.Length, cancellationToken);
            }

            // 1. If ZIP archive, extract database / SQL / CSV entry (bounded to 2 GB)
            if ((hdr.Length >= 4 && hdr[0] == 'P' && hdr[1] == 'K' && hdr[2] == 3 && hdr[3] == 4) ||
                lowerName.EndsWith(".zip", StringComparison.Ordinal))
            {
                using var archive = ZipFile.OpenRead(sourceFilePath);
                var entry = archive.Entries
                    .Where(e =>
                        !string.IsNullOrEmpty(e.Name) &&
                        !e.FullName.EndsWith('/') &&
                        !e.FullName.EndsWith('\\') &&
                        e.Length > 0 &&
                        !IsMacOsMetadataZipEntry(e.FullName) && (
                            e.Name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase) ||
                            e.Name.EndsWith(".sqlite3", StringComparison.OrdinalIgnoreCase) ||
                            e.Name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) ||
                            e.Name.EndsWith(".db3", StringComparison.OrdinalIgnoreCase) ||
                            e.Name.EndsWith(".sql.gz", StringComparison.OrdinalIgnoreCase) ||
                            e.Name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) ||
                            e.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(e => e.Length)
                    .FirstOrDefault();

                if (entry == null)
                {
                    throw new InvalidOperationException("ZIP archive does not contain a valid SQLite database, SQL dump, or Waseela CSV file.");
                }
                if (entry.Length > MaxDecompressedBytes)
                {
                    throw new InvalidOperationException("Database archive entry exceeds the maximum allowed size limit (2 GB).");
                }

                await using (var es = entry.Open())
                await using (var outFs = new FileStream(workingTempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await CopyBoundedAsync(es, outFs, MaxDecompressedBytes, cancellationToken);
                }
                effectiveEntryName = entry.Name;
                lowerName = entry.Name.ToLowerInvariant();
            }
            else
            {
                if (srcInfo.Length > MaxDecompressedBytes)
                {
                    throw new InvalidOperationException("Database file exceeds the maximum allowed size limit (2 GB).");
                }

                await using (var inFs = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true))
                await using (var outFs = new FileStream(workingTempPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await CopyBoundedAsync(inFs, outFs, MaxDecompressedBytes, cancellationToken);
                }
                copiedDirectFromDisk = true;
            }

            // Re-read header of working file
            var wInfo = new FileInfo(workingTempPath);
            var wHdr = new byte[(int)Math.Min(64L, Math.Max(0L, wInfo.Length))];
            await using (var whFs = new FileStream(workingTempPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                _ = await whFs.ReadAsync(wHdr, 0, wHdr.Length, cancellationToken);
            }

            // 2. If GZIP compressed, decompress bounded to 2 GB
            if ((wHdr.Length >= 2 && wHdr[0] == 0x1F && wHdr[1] == 0x8B) || lowerName.EndsWith(".gz", StringComparison.Ordinal))
            {
                var gzOutPath = Path.Combine(_stagingDir, $"{token}.gz.tmp");
                try
                {
                    await using (var inFs = new FileStream(workingTempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    await using (var gz = new GZipStream(inFs, CompressionMode.Decompress))
                    await using (var outFs = new FileStream(gzOutPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        await CopyBoundedAsync(gz, outFs, MaxDecompressedBytes, cancellationToken);
                    }
                    File.Move(gzOutPath, workingTempPath, overwrite: true);
                    copiedDirectFromDisk = false;
                    if (lowerName.EndsWith(".gz", StringComparison.Ordinal))
                    {
                        lowerName = lowerName[..^3];
                    }
                    if (effectiveEntryName.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                    {
                        effectiveEntryName = effectiveEntryName[..^3];
                    }
                }
                catch
                {
                    try { if (File.Exists(gzOutPath)) File.Delete(gzOutPath); } catch { }
                    throw;
                }

                wInfo.Refresh();
                wHdr = new byte[(int)Math.Min(64L, Math.Max(0L, wInfo.Length))];
                await using (var whFs2 = new FileStream(workingTempPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    _ = await whFs2.ReadAsync(wHdr, 0, wHdr.Length, cancellationToken);
                }
            }

            // 3. Detect binary SQLite header ("SQLite format 3") vs CSV vs SQL dump
            var headerText = Encoding.ASCII.GetString(wHdr);
            var magicPos = headerText.IndexOf("SQLite format 3", StringComparison.Ordinal);
            if (magicPos >= 0)
            {
                if (magicPos == 0)
                {
                    File.Move(workingTempPath, stagedSqlitePath, overwrite: true);
                }
                else
                {
                    await using var inFs = new FileStream(workingTempPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
                    inFs.Seek(magicPos, SeekOrigin.Begin);
                    await using var outFs = new FileStream(stagedSqlitePath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    await CopyBoundedAsync(inFs, outFs, MaxDecompressedBytes, cancellationToken);
                }

                // Copy sibling -wal and -shm when staging a raw SQLite file from disk so uncheckpointed rows merge cleanly
                if (copiedDirectFromDisk)
                {
                    var srcWal = sourceFilePath + "-wal";
                    if (File.Exists(srcWal))
                    {
                        try
                        {
                            await using var walIn = new FileStream(srcWal, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true);
                            await using var walOut = new FileStream(stagedSqlitePath + "-wal", FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                            await walIn.CopyToAsync(walOut, cancellationToken);
                        }
                        catch { }
                    }

                    var srcShm = sourceFilePath + "-shm";
                    if (File.Exists(srcShm))
                    {
                        try
                        {
                            await using var shmIn = new FileStream(srcShm, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, true);
                            await using var shmOut = new FileStream(stagedSqlitePath + "-shm", FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                            await shmIn.CopyToAsync(shmOut, cancellationToken);
                        }
                        catch { }
                    }

                    // Checkpoint WAL frames into the staged SQLite file immediately
                    try
                    {
                        await using var walConn = new SqliteConnection($"Data Source={stagedSqlitePath};Mode=ReadWrite;Pooling=False;");
                        await walConn.OpenAsync(cancellationToken);
                        await using var walCmd = walConn.CreateCommand();
                        walCmd.CommandText = "PRAGMA busy_timeout=15000; PRAGMA wal_checkpoint(TRUNCATE);";
                        await walCmd.ExecuteNonQueryAsync(cancellationToken);
                    }
                    catch { }
                }
            }
            else if (lowerName.EndsWith(".csv", StringComparison.Ordinal))
            {
                var workingInfo = new FileInfo(workingTempPath);
                if (workingInfo.Length > MaxTextImportBytes)
                {
                    throw new InvalidOperationException(
                        $"Text/SQL/CSV import file exceeds the maximum allowed size limit ({MaxTextImportBytes / (1024 * 1024)} MB). Please import a SQLite database file directly for larger datasets.");
                }

                var csvContent = await File.ReadAllTextAsync(workingTempPath, Encoding.UTF8, cancellationToken);
                await BuildSqliteFromWaseelaCsvAsync(csvContent, Path.GetFileNameWithoutExtension(effectiveEntryName), stagedSqlitePath, cancellationToken);
            }
            else
            {
                var workingInfo = new FileInfo(workingTempPath);
                if (workingInfo.Length > MaxTextImportBytes)
                {
                    throw new InvalidOperationException(
                        $"Text/SQL/CSV import file exceeds the maximum allowed size limit ({MaxTextImportBytes / (1024 * 1024)} MB). Please import a SQLite database file directly for larger datasets.");
                }

                var sqlContent = await File.ReadAllTextAsync(workingTempPath, Encoding.UTF8, cancellationToken);
                if (Regex.IsMatch(sqlContent, @"\b(CREATE\s+TABLE|INSERT\s+(?:INTO\b|\[))", RegexOptions.IgnoreCase))
                {
                    await BuildSqliteFromSqlDumpAsync(sqlContent, stagedSqlitePath, cancellationToken);
                }
                else
                {
                    throw new InvalidOperationException("Unsupported file format. Please select a valid SQLite database (.sqlite, .db), SQL dump (.sql), or Abuzar Waseela CSV export (.csv).");
                }
            }

            // 4. Integrity check on the staged SQLite database
            await AssertValidSqliteFileAsync(stagedSqlitePath, cancellationToken);

            var metadata = new Dictionary<string, object?>
            {
                ["token"] = token,
                ["original_name"] = displayFilename,
                ["file_size"] = new FileInfo(stagedSqlitePath).Length,
                ["created_at"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)
            };

            await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
            return metadata;
        }
        catch
        {
            CleanupStagedSession(token);
            throw;
        }
        finally
        {
            try { if (File.Exists(workingTempPath)) File.Delete(workingTempPath); } catch { }
        }
    }

    private static async Task AssertValidSqliteFileAsync(string sqlitePath, CancellationToken cancellationToken)
    {
        await using var conn = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly;Pooling=False;");
        await conn.OpenAsync(cancellationToken);

        await using (var timeoutCmd = conn.CreateCommand())
        {
            timeoutCmd.CommandText = "PRAGMA busy_timeout=5000;";
            await timeoutCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA quick_check;";
            var check = (await pragmaCmd.ExecuteScalarAsync(cancellationToken))?.ToString()?.Trim();
            if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"SQLite database failed integrity check ({check}).");
            }
        }

        await using (var countCmd = conn.CreateCommand())
        {
            countCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            var tableCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture);
            if (tableCount == 0)
            {
                throw new InvalidOperationException("The selected SQLite database contains no tables.");
            }
        }
    }

    private static bool DetectMySqlDialect(string sql)
    {
        if (string.IsNullOrEmpty(sql)) return false;

        if (sql.Contains("-- MySQL dump", StringComparison.OrdinalIgnoreCase) ||
            sql.Contains("mysqldump", StringComparison.OrdinalIgnoreCase) ||
            sql.Contains("MariaDB dump", StringComparison.OrdinalIgnoreCase) ||
            sql.Contains("/*!", StringComparison.Ordinal))
        {
            return true;
        }

        var hasStrictTSql = Regex.IsMatch(
            sql,
            @"\[dbo\]\.|(?m)^\s*GO\s*(?:;|$)|\bIDENTITY\s*\(\s*\d+\s*,\s*\d+\s*\)|\bON\s+\[?PRIMARY\]?\b|\bTEXTIMAGE_ON\b|\bSET\s+(?:ANSI_NULLS|QUOTED_IDENTIFIER|IDENTITY_INSERT)\b|(?:^|[^A-Za-z0-9_])N'",
            RegexOptions.IgnoreCase);

        if (hasStrictTSql)
        {
            return false;
        }

        var hasMySqlMarker = Regex.IsMatch(
            sql,
            @"`[^`\r\n]+`|\bENGINE\s*=\s*[A-Za-z0-9_]+|\bAUTO_INCREMENT\b|\bLOCK\s+TABLES\b|\bUNLOCK\s+TABLES\b|\bDEFAULT\s+CHARSET\s*=|\b(?:TINYINT|SMALLINT|MEDIUMINT|BIGINT|INT)\s*\(\s*\d+\s*\)\s*(?:UNSIGNED\b)?|\bUNSIGNED\b|\bCHARACTER\s+SET\s+[A-Za-z0-9_]+|\b(?:FULLTEXT|SPATIAL|UNIQUE)\s+KEY\b",
            RegexOptions.IgnoreCase);

        if (hasMySqlMarker)
        {
            return true;
        }

        if (sql.Contains("\\'", StringComparison.Ordinal))
        {
            _ = SplitSqlStatementsQuoteAware(sql, isMySql: true, out var mysqlBalanced);
            _ = SplitSqlStatementsQuoteAware(sql, isMySql: false, out var stdBalanced);
            if (mysqlBalanced && (!stdBalanced || Regex.IsMatch(sql, @"\\'[A-Za-z0-9_]")))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> SplitSqlStatementsQuoteAware(string sql)
        => SplitSqlStatementsQuoteAware(sql, DetectMySqlDialect(sql), out _);

    private static List<string> SplitSqlStatementsQuoteAware(string sql, bool isMySql, out bool quotesBalanced)
    {
        var statements = new List<string>();
        var current = new StringBuilder();
        var inSingle = false;
        var inDouble = false;
        var inBacktick = false;
        var inLineComment = false;
        var inBlockComment = false;
        var atLineStart = true;

        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (ch == '\n')
                {
                    inLineComment = false;
                    atLineStart = true;
                }
                continue;
            }
            if (inBlockComment)
            {
                if (ch == '*' && next == '/') { inBlockComment = false; i++; }
                continue;
            }
            if (!inSingle && !inDouble && !inBacktick)
            {
                if (ch == '-' && next == '-') { inLineComment = true; i++; atLineStart = false; continue; }
                if (isMySql && ch == '#') { inLineComment = true; atLineStart = false; continue; }
                if (ch == '/' && next == '*') { inBlockComment = true; i++; atLineStart = false; continue; }

                if (atLineStart)
                {
                    if (ch == ' ' || ch == '\t' || ch == '\r')
                    {
                        current.Append(ch);
                        continue;
                    }
                    if (ch == '\n')
                    {
                        current.Append(ch);
                        continue;
                    }
                    if (!isMySql && (ch == 'G' || ch == 'g') && (next == 'O' || next == 'o'))
                    {
                        var j = i + 2;
                        while (j < sql.Length && (sql[j] == ' ' || sql[j] == '\t')) j++;
                        if (j == sql.Length || sql[j] == '\r' || sql[j] == '\n' || sql[j] == ';')
                        {
                            var goStmt = current.ToString().Trim();
                            if (!string.IsNullOrEmpty(goStmt)) statements.Add(goStmt);
                            current.Clear();
                            i = (j < sql.Length && sql[j] == ';') ? j : j - 1;
                            continue;
                        }
                    }
                    atLineStart = false;
                }
            }

            if (ch == '`' && !inSingle && !inDouble)
            {
                current.Append(ch);
                if (inBacktick && next == '`') { current.Append(next); i++; }
                else inBacktick = !inBacktick;
                continue;
            }
            if (inBacktick)
            {
                current.Append(ch);
                continue;
            }

            if (inSingle)
            {
                if (isMySql && ch == '\\' && i + 1 < sql.Length)
                {
                    switch (next)
                    {
                        case '\'':
                            current.Append("''");
                            break;
                        case '"':
                            current.Append('"');
                            break;
                        case '\\':
                            current.Append('\\');
                            break;
                        case 'n':
                            current.Append('\n');
                            break;
                        case 'r':
                            current.Append('\r');
                            break;
                        case 't':
                            current.Append('\t');
                            break;
                        case '0':
                            break;
                        case 'Z':
                            current.Append('\x1A');
                            break;
                        default:
                            current.Append(next);
                            break;
                    }
                    i++;
                    continue;
                }

                current.Append(ch);
                if (ch == '\'')
                {
                    if (next == '\'') { current.Append(next); i++; }
                    else inSingle = false;
                }
                continue;
            }

            if (inDouble)
            {
                if (isMySql && ch == '\\' && i + 1 < sql.Length)
                {
                    switch (next)
                    {
                        case '"':
                            current.Append("\"\"");
                            break;
                        case '\\':
                            current.Append('\\');
                            break;
                        case '\'':
                            current.Append('\'');
                            break;
                        case 'n':
                            current.Append('\n');
                            break;
                        case 'r':
                            current.Append('\r');
                            break;
                        case 't':
                            current.Append('\t');
                            break;
                        case '0':
                            break;
                        default:
                            current.Append(next);
                            break;
                    }
                    i++;
                    continue;
                }

                current.Append(ch);
                if (ch == '"')
                {
                    if (next == '"') { current.Append(next); i++; }
                    else inDouble = false;
                }
                continue;
            }

            if (ch == '\'')
            {
                current.Append(ch);
                inSingle = true;
                continue;
            }
            if (ch == '"')
            {
                current.Append(ch);
                inDouble = true;
                continue;
            }
            if (ch == ';' && !inSingle && !inDouble && !inBacktick)
            {
                var stmt = current.ToString().Trim();
                if (!string.IsNullOrEmpty(stmt)) statements.Add(stmt);
                current.Clear();
                atLineStart = false;
                continue;
            }

            current.Append(ch);
            if (ch == '\n' && !inSingle && !inDouble && !inBacktick)
            {
                atLineStart = true;
            }
        }

        var tail = current.ToString().Trim();
        if (!string.IsNullOrEmpty(tail)) statements.Add(tail);
        quotesBalanced = !inSingle && !inDouble && !inBacktick;
        return statements;
    }

    private static string NormalizeMySqlStatementQuoteAware(string stmt)
    {
        if (string.IsNullOrWhiteSpace(stmt)) return "";

        var trimmed = stmt.Trim();
        if (Regex.IsMatch(trimmed, @"^INSERT\s+IGNORE\s+INTO\b", RegexOptions.IgnoreCase))
        {
            trimmed = Regex.Replace(trimmed, @"^INSERT\s+IGNORE\s+INTO\b", "INSERT OR IGNORE INTO", RegexOptions.IgnoreCase);
        }

        if (!Regex.IsMatch(trimmed, @"^CREATE\s+(?:TEMPORARY\s+)?TABLE\b", RegexOptions.IgnoreCase))
        {
            return trimmed;
        }

        trimmed = Regex.Replace(trimmed, @"^CREATE\s+TEMPORARY\s+TABLE\b", "CREATE TABLE", RegexOptions.IgnoreCase);

        // Find the top-level '(' ... ')' body of CREATE TABLE <name> (...)
        var openParenIdx = -1;
        var closeParenIdx = -1;
        var inSingle = false;
        var inDouble = false;
        var inBacktick = false;
        var depth = 0;

        for (var i = 0; i < trimmed.Length; i++)
        {
            var ch = trimmed[i];
            var next = i + 1 < trimmed.Length ? trimmed[i + 1] : '\0';

            if (inSingle)
            {
                if (ch == '\'' && next == '\'') { i++; continue; }
                if (ch == '\'') inSingle = false;
                continue;
            }
            if (inDouble)
            {
                if (ch == '"' && next == '"') { i++; continue; }
                if (ch == '"') inDouble = false;
                continue;
            }
            if (inBacktick)
            {
                if (ch == '`' && next == '`') { i++; continue; }
                if (ch == '`') inBacktick = false;
                continue;
            }

            if (ch == '\'') { inSingle = true; continue; }
            if (ch == '"') { inDouble = true; continue; }
            if (ch == '`') { inBacktick = true; continue; }

            if (ch == '(')
            {
                if (depth == 0 && openParenIdx < 0) openParenIdx = i;
                depth++;
            }
            else if (ch == ')')
            {
                depth--;
                if (depth == 0 && openParenIdx >= 0)
                {
                    closeParenIdx = i;
                    break;
                }
            }
        }

        if (openParenIdx < 0 || closeParenIdx <= openParenIdx)
        {
            return NormalizeMySqlOutsideSegments(trimmed);
        }

        var prefix = trimmed[..(openParenIdx + 1)];
        var body = trimmed[(openParenIdx + 1)..closeParenIdx];
        var suffix = trimmed[(closeParenIdx + 1)..].Trim();

        // Strip MySQL table options tail (ENGINE=..., AUTO_INCREMENT=..., DEFAULT CHARSET=..., COLLATE=..., COMMENT=...)
        if (!string.IsNullOrEmpty(suffix) &&
            Regex.IsMatch(suffix, @"^(?:ENGINE|TYPE|AUTO_INCREMENT|DEFAULT\s+(?:CHARSET|CHARACTER\s+SET)|CHARSET|CHARACTER\s+SET|COLLATE|COMMENT|ROW_FORMAT|CHECKSUM|DELAY_KEY_WRITE|PACK_KEYS|STATS_PERSISTENT|STATS_AUTO_RECALC|TABLESPACE|AVG_ROW_LENGTH|MIN_ROWS|MAX_ROWS|KEY_BLOCK_SIZE)\b", RegexOptions.IgnoreCase))
        {
            suffix = "";
        }
        else if (!string.IsNullOrEmpty(suffix))
        {
            suffix = StripMySqlEnumSetAndComments(suffix).Trim();
        }

        // Split body at top-level commas (outside quotes, backticks, and nested parens)
        var clauses = new List<string>();
        var start = 0;
        inSingle = false;
        inDouble = false;
        inBacktick = false;
        depth = 0;

        for (var i = 0; i < body.Length; i++)
        {
            var ch = body[i];
            var next = i + 1 < body.Length ? body[i + 1] : '\0';

            if (inSingle)
            {
                if (ch == '\'' && next == '\'') { i++; continue; }
                if (ch == '\'') inSingle = false;
                continue;
            }
            if (inDouble)
            {
                if (ch == '"' && next == '"') { i++; continue; }
                if (ch == '"') inDouble = false;
                continue;
            }
            if (inBacktick)
            {
                if (ch == '`' && next == '`') { i++; continue; }
                if (ch == '`') inBacktick = false;
                continue;
            }

            if (ch == '\'') { inSingle = true; continue; }
            if (ch == '"') { inDouble = true; continue; }
            if (ch == '`') { inBacktick = true; continue; }

            if (ch == '(') depth++;
            else if (ch == ')' && depth > 0) depth--;
            else if (ch == ',' && depth == 0)
            {
                clauses.Add(body[start..i]);
                start = i + 1;
            }
        }
        clauses.Add(body[start..]);

        var normalizedClauses = new List<string>();
        foreach (var rawClause in clauses)
        {
            var clause = rawClause.Trim();
            if (string.IsNullOrEmpty(clause)) continue;

            // Drop non-unique MySQL KEY / INDEX / FULLTEXT KEY / SPATIAL KEY clauses
            if (Regex.IsMatch(clause, @"^(?:FULLTEXT\s+|SPATIAL\s+)?(?:KEY|INDEX)\b", RegexOptions.IgnoreCase))
            {
                continue;
            }

            // Convert MySQL UNIQUE KEY / UNIQUE INDEX `idx_name` (`col1`, `col2`(100)) -> UNIQUE (`col1`, `col2`)
            var uniqMatch = Regex.Match(
                clause,
                @"^(?:CONSTRAINT\s+(?:`[^`]+`|""[^""]+""|[A-Za-z0-9_]+)\s+)?UNIQUE\s+(?:KEY|INDEX)\s+(?:(?:`[^`]+`|""[^""]+""|[A-Za-z0-9_]+)\s+)?(\([\s\S]*\))(?:\s+USING\s+(?:BTREE|HASH)|\s+COMMENT\s+(?:'(?:[^']|'')*'|""(?:[^""]|"""")*""))*$",
                RegexOptions.IgnoreCase);
            if (uniqMatch.Success)
            {
                var colsParen = StripMySqlIndexKeyExtras(uniqMatch.Groups[1].Value);
                normalizedClauses.Add($"UNIQUE {colsParen}");
                continue;
            }

            if (Regex.IsMatch(clause, @"^PRIMARY\s+KEY\b", RegexOptions.IgnoreCase))
            {
                var pkClean = Regex.Replace(clause, @"\s+USING\s+(?:BTREE|HASH)\b", "", RegexOptions.IgnoreCase);
                pkClean = Regex.Replace(pkClean, @"(?<=(?:`[^`]+`|""[^""]+""|[A-Za-z0-9_]+))\s*\(\s*\d+\s*\)", "");
                pkClean = StripMySqlEnumSetAndComments(pkClean);
                normalizedClauses.Add(pkClean.Trim());
                continue;
            }

            var normClause = NormalizeMySqlColumnClause(clause);
            if (!string.IsNullOrWhiteSpace(normClause))
            {
                normalizedClauses.Add(normClause);
            }
        }

        if (normalizedClauses.Count == 0)
        {
            return "";
        }

        return string.IsNullOrEmpty(suffix)
            ? $"{prefix}\n  {string.Join(",\n  ", normalizedClauses)}\n)"
            : $"{prefix}\n  {string.Join(",\n  ", normalizedClauses)}\n) {suffix}";
    }

    private static string StripMySqlIndexKeyExtras(string colsParen)
    {
        var clean = Regex.Replace(colsParen, @"(?<=(?:`[^`]+`|""[^""]+""|[A-Za-z0-9_]+))\s*\(\s*\d+\s*\)", "");
        clean = Regex.Replace(clean, @"\s+(?:ASC|DESC)\b", "", RegexOptions.IgnoreCase);
        return clean.Trim();
    }

    private static string NormalizeMySqlColumnClause(string clause)
    {
        // Replace ENUM(...) / SET(...) and strip COMMENT '...' while respecting quote boundaries
        var cleaned = StripMySqlEnumSetAndComments(clause);
        return NormalizeMySqlOutsideSegments(cleaned);
    }

    private static string StripMySqlEnumSetAndComments(string sql)
    {
        var sb = new StringBuilder(sql.Length);
        var inSingle = false;
        var inDouble = false;
        var inBacktick = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inSingle)
            {
                sb.Append(ch);
                if (ch == '\'' && next == '\'') { sb.Append(next); i++; }
                else if (ch == '\'') inSingle = false;
                continue;
            }
            if (inDouble)
            {
                sb.Append(ch);
                if (ch == '"' && next == '"') { sb.Append(next); i++; }
                else if (ch == '"') inDouble = false;
                continue;
            }
            if (inBacktick)
            {
                sb.Append(ch);
                if (ch == '`' && next == '`') { sb.Append(next); i++; }
                else if (ch == '`') inBacktick = false;
                continue;
            }

            if (ch == '\'') { inSingle = true; sb.Append(ch); continue; }
            if (ch == '"') { inDouble = true; sb.Append(ch); continue; }
            if (ch == '`') { inBacktick = true; sb.Append(ch); continue; }

            var rem = sql[i..];
            var prevIsIdent = i > 0 && IsSqlIdentChar(sql[i - 1]);
            if (!prevIsIdent)
            {
                var enumMatch = Regex.Match(rem, @"^(?:ENUM|SET)\s*\(", RegexOptions.IgnoreCase);
                if (enumMatch.Success)
                {
                    var j = i + enumMatch.Length;
                    var pDepth = 1;
                    var innerSingle = false;
                    while (j < sql.Length && pDepth > 0)
                    {
                        var c = sql[j];
                        if (innerSingle)
                        {
                            if (c == '\'' && j + 1 < sql.Length && sql[j + 1] == '\'') j += 2;
                            else
                            {
                                if (c == '\'') innerSingle = false;
                                j++;
                            }
                            continue;
                        }
                        if (c == '\'') { innerSingle = true; j++; continue; }
                        if (c == '(') pDepth++;
                        else if (c == ')') pDepth--;
                        j++;
                    }
                    sb.Append("TEXT");
                    i = j - 1;
                    continue;
                }

                var commentMatch = Regex.Match(rem, @"^COMMENT(?:\s*=\s*|\s+)(?:'(?:[^']|'')*'|""(?:[^""]|"""")*"")", RegexOptions.IgnoreCase);
                if (commentMatch.Success)
                {
                    i += commentMatch.Length - 1;
                    continue;
                }
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string NormalizeMySqlOutsideSegments(string stmt)
    {
        var segments = TokenizeSqlSegments(stmt);
        var result = new StringBuilder(stmt.Length);
        foreach (var seg in segments)
        {
            if (seg.IsQuoted)
            {
                result.Append(seg.Text);
                continue;
            }

            var text = seg.Text;
            text = Regex.Replace(text, @"\b(?:TINYINT|SMALLINT|MEDIUMINT|BIGINT|INT)\s*(?:\(\s*\d+\s*\))?(?:\s+(?:UNSIGNED|SIGNED|ZEROFILL))*\b", "INTEGER", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(DOUBLE|FLOAT|REAL|DECIMAL|NUMERIC)(\s*\([^)]*\))?(?:\s+(?:UNSIGNED|SIGNED|ZEROFILL))+\b", "$1$2", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(?:UNSIGNED|SIGNED|ZEROFILL)\b", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bAUTO_INCREMENT\b", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bCHARACTER\s+SET\s+[A-Za-z0-9_]+\b", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bCOLLATE\s+[A-Za-z0-9_]+\b", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bON\s+UPDATE\s+CURRENT_TIMESTAMP(?:\s*\(\s*\d*\s*\))?", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bCURRENT_TIMESTAMP\s*\(\s*\d*\s*\)", "CURRENT_TIMESTAMP", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(?:DATETIME|TIMESTAMP)\s*\(\s*\d+\s*\)", "DATETIME", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(?:TINYTEXT|MEDIUMTEXT|LONGTEXT|JSON)\b", "TEXT", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(?:TINYBLOB|MEDIUMBLOB|LONGBLOB)\b", "BLOB", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\b(?:VARBINARY|BINARY)\s*\(\s*\d+\s*\)", "BLOB", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bBIT\s*(?:\(\s*\d+\s*\))?", "INTEGER", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bBOOL(?:EAN)?\b", "INTEGER", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\s{2,}", " ");
            result.Append(text);
        }
        return result.ToString().Trim();
    }

    private static List<(string Text, bool IsQuoted, char QuoteChar)> TokenizeSqlSegments(string stmt)
    {
        var segments = new List<(string Text, bool IsQuoted, char QuoteChar)>();
        var buf = new StringBuilder();
        var inSingle = false;
        var inDouble = false;
        var inBacktick = false;

        for (var i = 0; i < stmt.Length; i++)
        {
            var ch = stmt[i];
            var next = i + 1 < stmt.Length ? stmt[i + 1] : '\0';

            if (!inSingle && !inDouble && !inBacktick)
            {
                if (ch == '\'' || ch == '"' || ch == '`')
                {
                    if (buf.Length > 0)
                    {
                        segments.Add((buf.ToString(), false, '\0'));
                        buf.Clear();
                    }
                    buf.Append(ch);
                    if (ch == '\'') inSingle = true;
                    else if (ch == '"') inDouble = true;
                    else inBacktick = true;
                    continue;
                }
                buf.Append(ch);
                continue;
            }

            buf.Append(ch);
            if (inSingle)
            {
                if (ch == '\'' && next == '\'') { buf.Append(next); i++; }
                else if (ch == '\'')
                {
                    inSingle = false;
                    segments.Add((buf.ToString(), true, '\''));
                    buf.Clear();
                }
            }
            else if (inDouble)
            {
                if (ch == '"' && next == '"') { buf.Append(next); i++; }
                else if (ch == '"')
                {
                    inDouble = false;
                    segments.Add((buf.ToString(), true, '"'));
                    buf.Clear();
                }
            }
            else if (inBacktick)
            {
                if (ch == '`' && next == '`') { buf.Append(next); i++; }
                else if (ch == '`')
                {
                    inBacktick = false;
                    segments.Add((buf.ToString(), true, '`'));
                    buf.Clear();
                }
            }
        }

        if (buf.Length > 0)
        {
            segments.Add((buf.ToString(), inSingle || inDouble || inBacktick, inSingle ? '\'' : inDouble ? '"' : inBacktick ? '`' : '\0'));
        }

        return segments;
    }

    private static bool IsSqlIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static string NormalizeTSqlStatementQuoteAware(string stmt)
    {
        // Split statement into outside-quote segments and verbatim quoted/bracketed segments
        var segments = new List<(string Text, bool IsQuoted, char QuoteChar)>();
        var buf = new StringBuilder();
        var inSingle = false;
        var inDouble = false;
        var inBracket = false;

        for (var i = 0; i < stmt.Length; i++)
        {
            var ch = stmt[i];
            var next = i + 1 < stmt.Length ? stmt[i + 1] : '\0';

            if (!inSingle && !inDouble && !inBracket)
            {
                if (ch == '\'' || ch == '"' || ch == '[')
                {
                    if (buf.Length > 0)
                    {
                        segments.Add((buf.ToString(), false, '\0'));
                        buf.Clear();
                    }
                    buf.Append(ch);
                    if (ch == '\'') inSingle = true;
                    else if (ch == '"') inDouble = true;
                    else inBracket = true;
                    continue;
                }
                buf.Append(ch);
                continue;
            }

            if (inSingle)
            {
                buf.Append(ch);
                if (ch == '\'')
                {
                    if (next == '\'')
                    {
                        buf.Append(next);
                        i++;
                    }
                    else
                    {
                        inSingle = false;
                        segments.Add((buf.ToString(), true, '\''));
                        buf.Clear();
                    }
                }
                continue;
            }

            if (inDouble)
            {
                buf.Append(ch);
                if (ch == '"')
                {
                    if (next == '"')
                    {
                        buf.Append(next);
                        i++;
                    }
                    else
                    {
                        inDouble = false;
                        segments.Add((buf.ToString(), true, '"'));
                        buf.Clear();
                    }
                }
                continue;
            }

            if (inBracket)
            {
                buf.Append(ch);
                if (ch == ']')
                {
                    if (next == ']')
                    {
                        buf.Append(next);
                        i++;
                    }
                    else
                    {
                        inBracket = false;
                        segments.Add((buf.ToString(), true, '['));
                        buf.Clear();
                    }
                }
            }
        }

        if (buf.Length > 0)
        {
            segments.Add((buf.ToString(), inSingle || inDouble || inBracket, inSingle ? '\'' : inDouble ? '"' : inBracket ? '[' : '\0'));
        }

        // Remove [dbo]. prefixes cleanly (including bracketed [dbo] followed by '.' outside quotes)
        for (var i = 0; i + 1 < segments.Count; i++)
        {
            if (segments[i].IsQuoted &&
                segments[i].QuoteChar == '[' &&
                segments[i].Text.Equals("[dbo]", StringComparison.OrdinalIgnoreCase) &&
                !segments[i + 1].IsQuoted &&
                segments[i + 1].Text.StartsWith(".", StringComparison.Ordinal))
            {
                segments[i] = ("", false, '\0');
                segments[i + 1] = (segments[i + 1].Text[1..], false, '\0');
            }
        }

        var isInsert = Regex.IsMatch(stmt, @"^\s*INSERT\b", RegexOptions.IgnoreCase);
        var isCreateTable = Regex.IsMatch(stmt, @"^\s*CREATE\s+TABLE\b", RegexOptions.IgnoreCase);

        // Normalize bracketed T-SQL types like [int] IDENTITY(1,1) and [nvarchar](max) across bracket/outside segments
        var result = new StringBuilder(stmt.Length);
        for (var i = 0; i < segments.Count; i++)
        {
            var seg = segments[i];
            if (seg.IsQuoted)
            {
                if (isCreateTable &&
                    seg.QuoteChar == '[' &&
                    Regex.IsMatch(seg.Text, @"^\[(?:int|integer|bigint|smallint|tinyint)\]$", RegexOptions.IgnoreCase) &&
                    i + 1 < segments.Count &&
                    !segments[i + 1].IsQuoted &&
                    Regex.IsMatch(segments[i + 1].Text, @"^\s+IDENTITY\s*\(\s*\d+\s*,\s*\d+\s*\)", RegexOptions.IgnoreCase))
                {
                    result.Append("INTEGER");
                    var strippedIdentity = Regex.Replace(segments[i + 1].Text, @"^\s+IDENTITY\s*\(\s*\d+\s*,\s*\d+\s*\)", "", RegexOptions.IgnoreCase);
                    segments[i + 1] = (strippedIdentity, false, '\0');
                    continue;
                }

                result.Append(seg.Text);
                continue;
            }

            var text = seg.Text;

            // Strip N'...' prefix only when immediately before a single-quoted literal and not part of an identifier
            if (i + 1 < segments.Count &&
                segments[i + 1].IsQuoted &&
                segments[i + 1].QuoteChar == '\'' &&
                text.Length > 0 &&
                (text[^1] == 'N' || text[^1] == 'n') &&
                (text.Length == 1 || !IsSqlIdentChar(text[^2])))
            {
                text = text[..^1];
            }

            if (isInsert && i == 0)
            {
                text = Regex.Replace(text, @"^\s*INSERT\s+(?!INTO\b|OR\b)", "INSERT INTO ", RegexOptions.IgnoreCase);
            }

            if (isCreateTable)
            {
                if (i + 1 < segments.Count &&
                    segments[i + 1].IsQuoted &&
                    segments[i + 1].QuoteChar == '[' &&
                    Regex.IsMatch(text, @"(?:\)|^|\s)(?:TEXTIMAGE_)?ON\s+$", RegexOptions.IgnoreCase))
                {
                    text = Regex.Replace(text, @"(?<=\)|^|\s)(?:TEXTIMAGE_)?ON\s+$", "", RegexOptions.IgnoreCase);
                    segments[i + 1] = ("", false, '\0');
                }

                text = Regex.Replace(text, @"\b(?:int|integer|bigint|smallint|tinyint)\s+IDENTITY\s*\(\s*\d+\s*,\s*\d+\s*\)", "INTEGER", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"\(\s*max\s*\)", "", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"\b(?:NON)?CLUSTERED\b", "", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"(?<=\))\s*WITH\s*\([^)]*\)", "", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"\bDEFAULT\s*\(\s*(?:getdate|getutcdate|sysdatetime)\s*\(\s*\)\s*\)", "DEFAULT CURRENT_TIMESTAMP", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"\bDEFAULT\s*\(\s*newid\s*\(\s*\)\s*\)", "DEFAULT ''", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"(?<=\)|^|\s)(?:TEXTIMAGE_)?ON\s+\[?PRIMARY\]?(?:\s+TEXTIMAGE_ON\s+\[?[A-Za-z0-9_]+\]?)?", "", RegexOptions.IgnoreCase);
                text = Regex.Replace(text, @"(?<=\)|^|\s)ON\s+[A-Za-z0-9_]+\s+(?=(?:TEXTIMAGE_ON\b|$))", "", RegexOptions.IgnoreCase);
                if (i == segments.Count - 1)
                {
                    text = Regex.Replace(text, @"(?<=\)|^|\s)(?:TEXTIMAGE_)?ON\s+\[?[A-Za-z0-9_]+\]?(?:\s+TEXTIMAGE_ON\s+\[?[A-Za-z0-9_]+\]?)?\s*$", "", RegexOptions.IgnoreCase);
                }
            }

            result.Append(text);
        }

        return result.ToString().Trim();
    }

    private static async Task BuildSqliteFromSqlDumpAsync(string sqlContent, string targetSqlitePath, CancellationToken cancellationToken)
    {
        if (File.Exists(targetSqlitePath)) File.Delete(targetSqlitePath);
        await using var conn = new SqliteConnection($"Data Source={targetSqlitePath};Pooling=False;");
        await conn.OpenAsync(cancellationToken);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys = OFF;";
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        var normalizedSql = sqlContent.TrimStart('\uFEFF');
        var isMySql = DetectMySqlDialect(normalizedSql);
        var statements = SplitSqlStatementsQuoteAware(normalizedSql, isMySql, out _);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        foreach (var rawStmt in statements)
        {
            var stmt = rawStmt.Trim();
            if (string.IsNullOrEmpty(stmt) ||
                Regex.IsMatch(stmt, @"^(SET\s+|LOCK\s+TABLES|UNLOCK\s+TABLES|USE\s+|START\s+TRANSACTION|BEGIN\b|COMMIT\b|DELIMITER\b|CREATE\s+DATABASE\b|DROP\s+DATABASE\b|ALTER\s+TABLE\s+[\s\S]*?\b(?:DISABLE|ENABLE)\s+KEYS\b)", RegexOptions.IgnoreCase))
            {
                continue;
            }
            if (Regex.IsMatch(stmt, @"\b(ATTACH\s+DATABASE|INTO\s+(OUTFILE|DUMPFILE)|LOAD_FILE\s*\()\b", RegexOptions.IgnoreCase))
            {
                throw new InvalidOperationException("Unsafe SQL statement detected in dump.");
            }
            stmt = isMySql ? NormalizeMySqlStatementQuoteAware(stmt) : NormalizeTSqlStatementQuoteAware(stmt);
            if (string.IsNullOrEmpty(stmt))
            {
                continue;
            }
            try
            {
                await using var sCmd = conn.CreateCommand();
                sCmd.Transaction = (SqliteTransaction)tx;
                sCmd.CommandText = stmt;
                await sCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException) when (!Regex.IsMatch(stmt, @"^\s*(?:INSERT|CREATE\s+TABLE)\b", RegexOptions.IgnoreCase))
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
        var cleanCsv = csvContent.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace("\r", "\n");
        var records = ParseCsvRecords(cleanCsv);
        if (records.Count < 2)
        {
            throw new InvalidOperationException("CSV file must contain a header row and at least one data row.");
        }

        var rawHeaders = records[0];
        var headers = new List<string>(rawHeaders.Count);
        var seenHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rawHeaders.Count; i++)
        {
            var h = rawHeaders[i].TrimStart('\uFEFF').Replace("`", "").Trim();
            if (string.IsNullOrWhiteSpace(h))
            {
                h = $"col_{i + 1}";
            }
            var candidate = h;
            var suffix = 2;
            while (!seenHeaders.Add(candidate))
            {
                candidate = $"{h}_{suffix++}";
            }
            headers.Add(candidate);
        }

        var tableName = Regex.Replace(rawTableName, @"[^A-Za-z0-9_]", "_");
        if (string.IsNullOrEmpty(tableName)) tableName = "waseela_items";

        await using var conn = new SqliteConnection($"Data Source={targetSqlitePath};Pooling=False;");
        await conn.OpenAsync(cancellationToken);

        var colDefs = string.Join(", ", headers.Select(h => $"`{h}` TEXT"));
        await using (var createCmd = conn.CreateCommand())
        {
            createCmd.CommandText = $"CREATE TABLE `{tableName}` ({colDefs});";
            await createCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        for (var i = 1; i < records.Count; i++)
        {
            var vals = records[i];
            if (vals.Count == 0 || vals.All(string.IsNullOrWhiteSpace)) continue;

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

    private static List<List<string>> ParseCsvRecords(string csv)
    {
        var records = new List<List<string>>();
        var currentRow = new List<string>();
        var currentField = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    currentField.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (ch == ',' && !inQuotes)
            {
                currentRow.Add(currentField.ToString().Trim());
                currentField.Clear();
            }
            else if (ch == '\n' && !inQuotes)
            {
                currentRow.Add(currentField.ToString().Trim());
                currentField.Clear();
                if (currentRow.Any(f => !string.IsNullOrWhiteSpace(f)))
                {
                    records.Add(currentRow);
                }
                currentRow = new List<string>();
            }
            else
            {
                currentField.Append(ch);
            }
        }

        if (currentField.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentField.ToString().Trim());
            if (currentRow.Any(f => !string.IsNullOrWhiteSpace(f)))
            {
                records.Add(currentRow);
            }
        }

        return records;
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
        try { if (File.Exists(sqlitePath + "-wal")) File.Delete(sqlitePath + "-wal"); } catch { }
        try { if (File.Exists(sqlitePath + "-shm")) File.Delete(sqlitePath + "-shm"); } catch { }
        try { if (File.Exists(metaPath)) File.Delete(metaPath); } catch { }
    }

    public (string? TargetTable, string MatchMethod) ResolveTargetTable(string sourceTable, IReadOnlyCollection<string> sourceColumns)
        => ResolveTargetTable(sourceTable, sourceColumns, targetCatalog: null);

    public (string? TargetTable, string MatchMethod) ResolveTargetTable(
        string sourceTable,
        IReadOnlyCollection<string> sourceColumns,
        IReadOnlyDictionary<string, TargetTableSchema>? targetCatalog)
    {
        var clean = (sourceTable ?? "").Trim().ToLowerInvariant();
        var allTargets = targetCatalog is { Count: > 0 }
            ? targetCatalog.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : DependencyStages.Values.SelectMany(x => x).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Tier 1: Exact match
        if (allTargets.Contains(clean))
        {
            return (allTargets.First(t => t.Equals(clean, StringComparison.OrdinalIgnoreCase)), "exact");
        }

        // Tier 2: Prefix strip + TableAliases
        var stripped = Regex.Replace(clean, @"^(tbl_|pos_|erp_|app_|old_|v1_|waseela_)", "");
        if (allTargets.Contains(stripped))
        {
            return (allTargets.First(t => t.Equals(stripped, StringComparison.OrdinalIgnoreCase)), "alias");
        }

        if (TableAliases.TryGetValue(clean, out var aliased) && allTargets.Contains(aliased))
        {
            return (aliased, "alias");
        }
        if (TableAliases.TryGetValue(stripped, out aliased) && allTargets.Contains(aliased))
        {
            return (aliased, "alias");
        }

        // Tier 3: Simple pluralization / singularization
        foreach (var candidate in BuildPluralVariants(stripped))
        {
            if (allTargets.Contains(candidate))
            {
                return (allTargets.First(t => t.Equals(candidate, StringComparison.OrdinalIgnoreCase)), "alias");
            }
            if (TableAliases.TryGetValue(candidate, out var pAliased) && allTargets.Contains(pAliased))
            {
                return (pAliased, "alias");
            }
        }

        // Tier 4: Column-overlap fuzzy heuristic (overlap >= 2 && score >= 0.6)
        if (sourceColumns.Count > 0 && targetCatalog is { Count: > 0 })
        {
            var srcNormCols = sourceColumns
                .Select(NormalizeIdentifier)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // Also include canonical target columns reachable via ColumnAliases from source columns
            var expandedSrcCols = new HashSet<string>(srcNormCols, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in ColumnAliases)
            {
                foreach (var alias in kv.Value)
                {
                    if (srcNormCols.Contains(NormalizeIdentifier(alias)))
                    {
                        expandedSrcCols.Add(NormalizeIdentifier(kv.Key));
                        break;
                    }
                }
            }

            string? bestTarget = null;
            double bestScore = 0;

            foreach (var (tName, schema) in targetCatalog)
            {
                var targetNormCols = schema.Columns
                    .Select(c => NormalizeIdentifier(c.Name))
                    .Where(s => !string.IsNullOrEmpty(s) && s is not ("id" or "createdat" or "updatedat"))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                if (targetNormCols.Count == 0) continue;

                var overlap = expandedSrcCols.Count(c => targetNormCols.Contains(c));
                if (overlap < 2) continue;

                var denom = Math.Max(1, Math.Min(srcNormCols.Count, targetNormCols.Count));
                var score = (double)overlap / denom;
                if (score >= 0.6 && score > bestScore)
                {
                    bestScore = score;
                    bestTarget = tName;
                }
            }

            if (bestTarget != null)
            {
                return (bestTarget, "fuzzy");
            }
        }

        return (null, "unmapped");
    }

    private static IEnumerable<string> BuildPluralVariants(string name)
    {
        if (string.IsNullOrEmpty(name)) yield break;
        yield return name + "s";
        yield return name + "es";
        if (name.EndsWith("y", StringComparison.Ordinal) && name.Length > 1)
        {
            yield return name[..^1] + "ies";
        }
        if (name.EndsWith("ies", StringComparison.Ordinal) && name.Length > 3)
        {
            yield return name[..^3] + "y";
        }
        if (name.EndsWith("s", StringComparison.Ordinal) && name.Length > 1)
        {
            yield return name[..^1];
        }
    }

    private static string NormalizeIdentifier(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        return Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]", "");
    }

    public async Task<Dictionary<string, TargetTableSchema>> BuildTargetSchemaCatalogAsync(
        SqliteConnection targetConn,
        CancellationToken cancellationToken = default)
    {
        var catalog = new Dictionary<string, TargetTableSchema>(StringComparer.OrdinalIgnoreCase);
        var tableNames = new List<string>();

        await using (var cmd = targetConn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader.GetString(0);
                if (!string.IsNullOrWhiteSpace(name) && !ProtectedTables.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    tableNames.Add(name);
                }
            }
        }

        foreach (var tbl in tableNames)
        {
            var cols = await GetTableColumnsAsync(targetConn, tbl, null, cancellationToken);
            catalog[tbl] = new TargetTableSchema
            {
                Table = tbl,
                Label = tbl,
                Stage = GetStageForTable(tbl),
                IsOptionalConfig = OptionalConfigTables.Contains(tbl, StringComparer.OrdinalIgnoreCase),
                Columns = cols
            };
        }

        return catalog;
    }

    private static async Task<List<TargetColumnSchema>> GetTableColumnsAsync(
        SqliteConnection conn,
        string tableName,
        SqliteTransaction? tx = null,
        CancellationToken cancellationToken = default)
    {
        var cols = new List<TargetColumnSchema>();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\");";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var name = reader["name"]?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                var type = reader["type"]?.ToString() ?? "TEXT";
                if (string.IsNullOrWhiteSpace(type)) type = "TEXT";
                var notnull = Convert.ToInt32(reader["notnull"] ?? 0, CultureInfo.InvariantCulture) != 0;
                var dflt = reader["dflt_value"] is DBNull ? null : reader["dflt_value"]?.ToString();
                var pk = Convert.ToInt32(reader["pk"] ?? 0, CultureInfo.InvariantCulture) != 0;
                cols.Add(new TargetColumnSchema
                {
                    Name = name,
                    Type = type,
                    NotNull = notnull,
                    DefaultValue = dflt,
                    Pk = pk
                });
            }
        }
        catch
        {
            // Ignore unreadable metadata
        }
        return cols;
    }

    private static async Task<List<Dictionary<string, object?>>> GetSampleRowsAsync(
        SqliteConnection conn,
        string tableName,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<Dictionary<string, object?>>();
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT * FROM \"{tableName.Replace("\"", "\"\"")}\" LIMIT 3;";
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var val = reader.IsDBNull(i) ? null : reader.GetValue(i);
                    if (val is byte[] blob)
                    {
                        val = $"[binary {blob.Length} bytes]";
                    }
                    else if (val is string str && str.Length > 120)
                    {
                        val = str[..117] + "...";
                    }
                    row[reader.GetName(i)] = val;
                }
                rows.Add(row);
            }
        }
        catch
        {
            // Ignore
        }
        return rows;
    }

    public Dictionary<string, object?> BuildTableColumnAnalysis(
        string sourceTable,
        List<TargetColumnSchema> sourceCols,
        string targetTable,
        List<TargetColumnSchema> targetCols,
        List<Dictionary<string, object?>> sampleRows,
        Dictionary<string, ColumnMappingOverride>? customColumnMap = null)
    {
        var srcNames = sourceCols.Select(c => c.Name).ToList();
        var srcNameSet = new HashSet<string>(srcNames, StringComparer.OrdinalIgnoreCase);
        var srcByLower = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var srcByNorm = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sc in sourceCols)
        {
            var lower = sc.Name.Trim().ToLowerInvariant();
            if (!srcByLower.ContainsKey(lower)) srcByLower[lower] = sc.Name;
            var norm = NormalizeIdentifier(sc.Name);
            if (!string.IsNullOrEmpty(norm) && !srcByNorm.ContainsKey(norm)) srcByNorm[norm] = sc.Name;
        }

        var mappings = new List<Dictionary<string, object?>>();
        var usedSourceCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missingTargetCols = new List<Dictionary<string, object?>>();
        var matchedCount = 0;

        foreach (var tc in targetCols)
        {
            var tName = tc.Name;
            var tLower = tName.ToLowerInvariant();
            var tNorm = NormalizeIdentifier(tLower);

            string? matchedSrcCol = null;
            var matchType = "unmapped";
            string? staticValue = null;

            if (customColumnMap != null && customColumnMap.TryGetValue(tName, out var ov))
            {
                staticValue = string.IsNullOrEmpty(ov.DefaultValue) ? null : ov.DefaultValue;
                if (!string.IsNullOrWhiteSpace(ov.SourceColumn) &&
                    !string.Equals(ov.SourceColumn, "__skip__", StringComparison.OrdinalIgnoreCase) &&
                    srcNameSet.Contains(ov.SourceColumn))
                {
                    matchedSrcCol = srcNames.First(s => s.Equals(ov.SourceColumn, StringComparison.OrdinalIgnoreCase));
                    matchType = "custom";
                }
                else
                {
                    matchedSrcCol = null;
                    matchType = tc.DefaultValue != null ? "default" : "synthesized";
                }
            }
            else
            {
                if (srcByLower.TryGetValue(tLower, out var exact))
                {
                    matchedSrcCol = exact;
                    matchType = "exact";
                }
                else if (srcByNorm.TryGetValue(tNorm, out var normMatch))
                {
                    matchedSrcCol = normMatch;
                    matchType = "normalized";
                }
                else if (ColumnAliases.TryGetValue(tLower, out var aliases))
                {
                    foreach (var alias in aliases)
                    {
                        var aliasLower = alias.ToLowerInvariant();
                        var aliasNorm = NormalizeIdentifier(aliasLower);
                        if (srcByLower.TryGetValue(aliasLower, out var aExact))
                        {
                            matchedSrcCol = aExact;
                            matchType = "alias";
                            break;
                        }
                        if (srcByNorm.TryGetValue(aliasNorm, out var aNorm))
                        {
                            matchedSrcCol = aNorm;
                            matchType = "alias";
                            break;
                        }
                    }
                }
            }

            if (matchedSrcCol != null)
            {
                usedSourceCols.Add(matchedSrcCol);
                matchedCount++;
            }
            else if (!tc.Pk)
            {
                matchType = tc.DefaultValue != null ? "default" : "synthesized";
                missingTargetCols.Add(new Dictionary<string, object?>
                {
                    ["column"] = tName,
                    ["type"] = tc.Type,
                    ["required"] = tc.NotNull && tc.DefaultValue == null,
                    ["default_value"] = tc.DefaultValue,
                    ["synthesis_note"] = DescribeColumnSynthesis(targetTable, tc, null)
                });
            }

            var note = DescribeColumnSynthesis(targetTable, tc, matchedSrcCol);
            var isRequired = tc.NotNull && tc.DefaultValue == null && !tc.Pk;

            mappings.Add(new Dictionary<string, object?>
            {
                ["target_column"] = tName,
                ["target_type"] = tc.Type,
                ["required"] = isRequired,
                ["is_pk"] = tc.Pk,
                ["schema_default"] = tc.DefaultValue,
                ["source_column"] = matchedSrcCol,
                ["static_value"] = staticValue,
                ["match_type"] = matchType,
                ["strategy"] = matchType,
                ["synthesis_note"] = note,
                ["fallback_description"] = !string.IsNullOrWhiteSpace(staticValue) ? $"Static default: {staticValue}" : note
            });
        }

        var unmappedSrc = srcNames.Where(c => !usedSourceCols.Contains(c)).ToList();

        var effectiveColMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var staticDefaultsMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cm in mappings)
        {
            var tCol = cm["target_column"]?.ToString() ?? "";
            var sCol = cm["source_column"]?.ToString();
            var stVal = cm["static_value"]?.ToString();
            if (!string.IsNullOrWhiteSpace(sCol))
            {
                effectiveColMap[tCol] = sCol;
            }
            else if (customColumnMap != null && customColumnMap.TryGetValue(tCol, out var ov) &&
                     string.Equals(ov.SourceColumn, "__skip__", StringComparison.OrdinalIgnoreCase))
            {
                effectiveColMap[tCol] = "__skip__";
            }
            if (!string.IsNullOrEmpty(stVal)) staticDefaultsMap[tCol] = stVal;
        }

        var previewRows = new List<Dictionary<string, object?>>();
        for (var i = 0; i < sampleRows.Count; i++)
        {
            var synthesized = PreviewSynthesizedRow(targetTable, targetCols, sampleRows[i], effectiveColMap, staticDefaultsMap, i + 1);
            if (synthesized != null)
            {
                previewRows.Add(TruncateSampleValues(synthesized));
            }
        }

        return new Dictionary<string, object?>
        {
            ["source_table"] = sourceTable,
            ["target_table"] = targetTable,
            ["matched_columns_count"] = matchedCount,
            ["total_target_columns"] = targetCols.Count,
            ["missing_target_columns"] = missingTargetCols,
            ["unmapped_source_columns"] = unmappedSrc,
            ["column_mappings"] = mappings,
            ["preview_rows"] = previewRows
        };
    }

    private static string DescribeColumnSynthesis(string targetTable, TargetColumnSchema targetCol, string? matchedSrcCol)
    {
        var col = targetCol.Name;
        var isFk = ForeignKeyRules.TryGetValue(targetTable, out var tableRules) && tableRules.ContainsKey(col);
        var isUserFk = UserForeignKeyColumns.Contains(col, StringComparer.OrdinalIgnoreCase);

        if (isUserFk)
        {
            return "Auto-remapped to matching user or current Admin";
        }

        if (matchedSrcCol != null)
        {
            if (isFk && tableRules != null)
            {
                var parent = tableRules[col].ParentTable;
                return $"Mapped from `{matchedSrcCol}` (with cross-table ID translation to `{parent}`)";
            }
            return $"Mapped from `{matchedSrcCol}`";
        }

        if (targetTable.Equals("products", StringComparison.OrdinalIgnoreCase))
        {
            return col.ToLowerInvariant() switch
            {
                "barcode" => "Auto-generated unique barcode from product_code",
                "product_code" => "Auto-generated unique code (PRD-XXXXX)",
                "pack_size" => "Defaults to 1 unit per pack",
                "category_id" => "Auto-assigned to \"General\" category",
                "brand_id" => "Auto-assigned to \"General\" brand",
                "unit_purchase_price" => "Derived from pack_purchase_price / pack_size (or 0)",
                "pack_purchase_price" => "Derived from unit_purchase_price * pack_size (or 0)",
                "unit_sale_price" => "Derived from pack_sale_price / pack_size (or 0)",
                "pack_sale_price" => "Derived from unit_sale_price * pack_size (or 0)",
                "avg_price" => "Derived from unit_purchase_price",
                "margin" => "Calculated from unit_sale_price & avg_price",
                _ => targetCol.DefaultValue != null ? $"Schema default ({targetCol.DefaultValue})" : "Nullable / safe default"
            };
        }

        if (targetTable.Equals("purchase_invoice_items", StringComparison.OrdinalIgnoreCase) ||
            targetTable.Equals("sale_invoice_items", StringComparison.OrdinalIgnoreCase))
        {
            return col.ToLowerInvariant() switch
            {
                "pack_size" => "Resolved from product pack_size (or 1)",
                "quantity" => "Derived from (pack_quantity * pack_size) + unit_quantity",
                "pack_quantity" => "Derived from quantity / pack_size (or 0)",
                "unit_quantity" => "Derived from quantity % pack_size",
                "unit_purchase_price" => "Derived from pack_purchase_price / pack_size",
                "pack_purchase_price" => "Derived from unit_purchase_price * pack_size",
                "unit_sale_price" => "Derived from pack_sale_price / pack_size",
                "pack_sale_price" => "Derived from unit_sale_price * pack_size",
                "avg_price" => "Set from unit_purchase_price",
                "margin" => "Calculated margin percentage",
                "sub_total" => "Calculated from quantity * unit price",
                _ => targetCol.DefaultValue != null ? $"Schema default ({targetCol.DefaultValue})" : "Safe default (0 / NULL)"
            };
        }

        if (isFk && tableRules != null)
        {
            var fb = tableRules[col].FallbackMode;
            if (fb == "customer") return "Auto-assigned to \"Walk-in Customer\"";
            if (fb == "supplier") return "Auto-assigned to \"General Supplier\"";
            if (fb == "expense_category") return "Auto-assigned to \"General Expense\"";
        }

        if (targetCol.DefaultValue != null)
        {
            return $"Schema default ({targetCol.DefaultValue})";
        }

        return !targetCol.NotNull ? "Set to NULL" : "Auto-synthesized safe value";
    }

    private static Dictionary<string, object?> TruncateSampleValues(Dictionary<string, object?> row)
    {
        var clean = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in row)
        {
            if (kv.Value is string s && s.Length > 120)
            {
                clean[kv.Key] = s[..117] + "...";
            }
            else
            {
                clean[kv.Key] = kv.Value;
            }
        }
        return clean;
    }

    public async Task<List<Dictionary<string, object?>>> InspectUserRemappingAsync(
        SqliteConnection srcConn,
        SqliteConnection dstConn,
        long fallbackAdminId = 1,
        CancellationToken cancellationToken = default)
    {
        var preview = new List<Dictionary<string, object?>>();

        var hasSrcUsers = false;
        await using (var chk = srcConn.CreateCommand())
        {
            chk.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND LOWER(name)='users';";
            hasSrcUsers = Convert.ToInt32(await chk.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture) > 0;
        }
        if (!hasSrcUsers) return preview;

        var targetByEmail = new Dictionary<string, (long Id, string Name)>(StringComparer.OrdinalIgnoreCase);
        var targetByName = new Dictionary<string, (long Id, string Name)>(StringComparer.OrdinalIgnoreCase);
        (long Id, string Name)? fallbackUser = null;

        await using (var tCmd = dstConn.CreateCommand())
        {
            tCmd.CommandText = "SELECT id, name, email FROM users ORDER BY id ASC;";
            await using var r = await tCmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                var uid = Convert.ToInt64(r["id"], CultureInfo.InvariantCulture);
                var uname = r["name"]?.ToString() ?? $"User #{uid}";
                var uemail = r["email"]?.ToString() ?? "";
                if (fallbackUser == null || uid == fallbackAdminId)
                {
                    fallbackUser = (uid, uname);
                }
                if (!string.IsNullOrWhiteSpace(uemail) && !targetByEmail.ContainsKey(uemail.Trim()))
                {
                    targetByEmail[uemail.Trim()] = (uid, uname);
                }
                if (!string.IsNullOrWhiteSpace(uname) && !targetByName.ContainsKey(uname.Trim()))
                {
                    targetByName[uname.Trim()] = (uid, uname);
                }
            }
        }

        var fbId = fallbackUser?.Id ?? fallbackAdminId;
        var fbName = fallbackUser?.Name ?? "Current Admin";

        await using (var sCmd = srcConn.CreateCommand())
        {
            sCmd.CommandText = "SELECT * FROM \"users\" LIMIT 50;";
            await using var sr = await sCmd.ExecuteReaderAsync(cancellationToken);
            while (await sr.ReadAsync(cancellationToken))
            {
                var cols = Enumerable.Range(0, sr.FieldCount).ToDictionary(i => sr.GetName(i), i => sr.IsDBNull(i) ? null : sr.GetValue(i), StringComparer.OrdinalIgnoreCase);
                var sId = cols.TryGetValue("id", out var idObj) && idObj != null && long.TryParse(idObj.ToString(), out var parsed) ? parsed : 0;
                var sName = cols.TryGetValue("name", out var nObj) && nObj != null ? nObj.ToString() ?? $"User #{sId}" : $"User #{sId}";
                var sEmail = cols.TryGetValue("email", out var eObj) && eObj != null ? eObj.ToString() ?? "" : "";

                (long Id, string Name)? matched = null;
                var strategy = "fallback_admin";
                if (!string.IsNullOrWhiteSpace(sEmail) && targetByEmail.TryGetValue(sEmail.Trim(), out var byEmail))
                {
                    matched = byEmail;
                    strategy = "matched_email";
                }
                else if (!string.IsNullOrWhiteSpace(sName) && targetByName.TryGetValue(sName.Trim(), out var byName))
                {
                    matched = byName;
                    strategy = "matched_name";
                }

                preview.Add(new Dictionary<string, object?>
                {
                    ["old_user_id"] = sId,
                    ["old_name"] = sName,
                    ["old_email"] = sEmail,
                    ["mapped_to_user_id"] = matched?.Id ?? fbId,
                    ["mapped_to_name"] = matched?.Name ?? $"{fbName} (Fallback Admin)",
                    ["strategy"] = strategy
                });
            }
        }

        return preview;
    }

    public Task<Dictionary<string, object?>> InspectAndProposeMappingAsync(
        string token,
        CancellationToken cancellationToken = default)
        => InspectAndProposeMappingAsync(token, targetSqlitePath: null, cancellationToken);

    /// <summary>
    /// Inspect staged SQLite database, detect Abuzar Waseela or Karobar v2 schema, and propose full table &amp; column mappings.
    /// </summary>
    public async Task<Dictionary<string, object?>> InspectAndProposeMappingAsync(
        string token,
        string? targetSqlitePath,
        CancellationToken cancellationToken = default)
    {
        var sqlitePath = ResolveStagedPath(token);
        var metaPath = Path.Combine(_stagingDir, $"{token}.json");
        var meta = File.Exists(metaPath)
            ? JsonSerializer.Deserialize<Dictionary<string, object?>>(await File.ReadAllTextAsync(metaPath, cancellationToken)) ?? new()
            : new Dictionary<string, object?>();

        var effectiveTargetPath = !string.IsNullOrWhiteSpace(targetSqlitePath)
            ? targetSqlitePath
            : Path.Combine(_appRoot, "database", "database.sqlite");

        await using var srcConn = await OpenStagedSourceConnectionAsync(sqlitePath, cancellationToken);

        Dictionary<string, TargetTableSchema> targetCatalog = new(StringComparer.OrdinalIgnoreCase);
        var userRemapPreview = new List<Dictionary<string, object?>>();

        if (File.Exists(effectiveTargetPath))
        {
            await using var dstConn = new SqliteConnection($"Data Source={effectiveTargetPath};Mode=ReadOnly;Pooling=False;");
            await dstConn.OpenAsync(cancellationToken);
            targetCatalog = await BuildTargetSchemaCatalogAsync(dstConn, cancellationToken);
            userRemapPreview = await InspectUserRemappingAsync(srcConn, dstConn, 1, cancellationToken);
        }

        var rawTableNames = new List<string>();
        await using (var cmd = srcConn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
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
        var schemaDriftCount = 0;

        foreach (var srcTable in rawTableNames)
        {
            var escaped = srcTable.Replace("\"", "\"\"");
            int rowCount;
            try
            {
                await using var countCmd = srcConn.CreateCommand();
                countCmd.CommandText = $"SELECT COUNT(*) FROM \"{escaped}\";";
                var scalar = await countCmd.ExecuteScalarAsync(cancellationToken);
                rowCount = Convert.ToInt32(scalar ?? 0, CultureInfo.InvariantCulture);
            }
            catch
            {
                continue;
            }

            var srcCols = await GetTableColumnsAsync(srcConn, srcTable, null, cancellationToken);
            var colNames = srcCols.Select(c => c.Name).ToList();
            tablesAndColsForDetector[srcTable] = colNames;

            if (ProtectedTables.Contains(srcTable.ToLowerInvariant()))
            {
                protectedSkipped.Add(new Dictionary<string, object?>
                {
                    ["source_table"] = srcTable,
                    ["row_count"] = rowCount,
                    ["reason"] = GetProtectedTableReason(srcTable.ToLowerInvariant())
                });
                continue;
            }

            var sampleRows = rowCount > 0
                ? await GetSampleRowsAsync(srcConn, srcTable, cancellationToken)
                : new List<Dictionary<string, object?>>();

            var (matchedTarget, matchMethod) = ResolveTargetTable(srcTable, colNames, targetCatalog.Count > 0 ? targetCatalog : null);
            Dictionary<string, object?>? analysis = null;
            var isOptional = matchedTarget != null && OptionalConfigTables.Contains(matchedTarget, StringComparer.OrdinalIgnoreCase);
            var stage = matchedTarget != null ? GetStageForTable(matchedTarget) : 6;

            if (matchedTarget != null)
            {
                autoMatchedCount++;
                if (targetCatalog.TryGetValue(matchedTarget, out var targetSchema))
                {
                    stage = targetSchema.Stage;
                    analysis = BuildTableColumnAnalysis(srcTable, srcCols, matchedTarget, targetSchema.Columns, sampleRows);
                    var missing = analysis["missing_target_columns"] as List<Dictionary<string, object?>>;
                    var unmapped = analysis["unmapped_source_columns"] as List<string>;
                    if ((missing?.Count ?? 0) > 0 || (unmapped?.Count ?? 0) > 0 || matchMethod != "exact")
                    {
                        schemaDriftCount++;
                    }
                }
            }

            var enabled = matchedTarget != null && rowCount > 0 && !isOptional;
            if (enabled) totalImportableRows += rowCount;

            var previewSampleRows = analysis != null &&
                                    analysis.TryGetValue("preview_rows", out var prObj) &&
                                    prObj is List<Dictionary<string, object?>> prList &&
                                    prList.Count > 0
                ? prList
                : sampleRows;

            var confidence = matchMethod switch
            {
                "exact" => 100,
                "alias" => 95,
                "fuzzy" => 80,
                _ => 0
            };

            sourceTables.Add(new Dictionary<string, object?>
            {
                ["source_table"] = srcTable,
                ["row_count"] = rowCount,
                ["source_columns"] = colNames,
                ["source_columns_meta"] = srcCols.Select(c => new Dictionary<string, object?>
                {
                    ["name"] = c.Name,
                    ["type"] = c.Type,
                    ["notnull"] = c.NotNull,
                    ["pk"] = c.Pk
                }).ToList(),
                ["target_table"] = matchedTarget,
                ["match_method"] = matchMethod,
                ["match_type"] = matchMethod,
                ["confidence"] = confidence,
                ["enabled"] = enabled,
                ["is_optional_cfg"] = isOptional,
                ["dependency_stage"] = stage,
                ["stage"] = stage,
                ["analysis"] = analysis,
                ["column_mappings"] = analysis != null ? analysis["column_mappings"] : Array.Empty<object>(),
                ["unmapped_source_columns"] = analysis != null ? analysis["unmapped_source_columns"] : colNames,
                ["sample_rows"] = previewSampleRows,
                ["stats"] = analysis != null
                    ? new Dictionary<string, object?>
                    {
                        ["matched_columns_count"] = analysis["matched_columns_count"],
                        ["total_target_columns"] = analysis["total_target_columns"]
                    }
                    : new Dictionary<string, object?>()
            });
        }

        sourceTables = sourceTables
            .OrderBy(t => Convert.ToInt32(t["stage"] ?? 6, CultureInfo.InvariantCulture))
            .ThenBy(t => t["source_table"]?.ToString(), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var availableTargets = targetCatalog.Values
            .OrderBy(t => t.Stage)
            .ThenBy(t => t.Table, StringComparer.OrdinalIgnoreCase)
            .Select(t => new Dictionary<string, object?>
            {
                ["table"] = t.Table,
                ["label"] = t.Label,
                ["stage"] = t.Stage,
                ["is_optional_cfg"] = t.IsOptionalConfig
            })
            .ToList();

        var waseelaDetection = DetectWaseelaSchema(tablesAndColsForDetector);
        var originalName = meta.TryGetValue("original_name", out var on) ? on : "";

        return new Dictionary<string, object?>
        {
            ["message"] = "SQLite database analyzed successfully.",
            ["session_token"] = token,
            ["session"] = meta,
            ["file_name"] = originalName,
            ["waseela_detection"] = new Dictionary<string, object?>
            {
                ["is_waseela_schema"] = waseelaDetection.IsWaseelaSchema,
                ["detected_profile"] = waseelaDetection.DetectedProfile,
                ["confidence_score"] = waseelaDetection.ConfidenceScore,
                ["matched_signatures"] = waseelaDetection.MatchedSignatures,
                ["waseela_table_mappings"] = waseelaDetection.WaseelaTableMappings
            },
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_source_tables"] = sourceTables.Count + protectedSkipped.Count,
                ["importable_tables_count"] = sourceTables.Count,
                ["non_empty_tables_count"] = sourceTables.Count(t => Convert.ToInt32(t["row_count"] ?? 0, CultureInfo.InvariantCulture) > 0),
                ["auto_matched_count"] = autoMatchedCount,
                ["matched_tables_count"] = autoMatchedCount,
                ["schema_drift_count"] = schemaDriftCount,
                ["protected_skipped_count"] = protectedSkipped.Count,
                ["total_importable_rows"] = totalImportableRows,
                ["total_rows_to_import"] = totalImportableRows
            },
            ["protected_tables"] = protectedSkipped,
            ["tables"] = sourceTables,
            ["importable_tables"] = sourceTables,
            ["target_catalog"] = availableTargets,
            ["available_target_tables"] = availableTargets,
            ["user_remap_preview"] = userRemapPreview,
            ["user_remapping"] = new Dictionary<string, object?>
            {
                ["source_users_count"] = userRemapPreview.Count,
                ["preview"] = userRemapPreview
            }
        };
    }

    public async Task<Dictionary<string, object?>> PreviewTableMappingAsync(
        string token,
        string sourceTable,
        string targetTable,
        string targetSqlitePath,
        Dictionary<string, ColumnMappingOverride>? customColumnMap = null,
        CancellationToken cancellationToken = default)
    {
        if (ProtectedTables.Contains(sourceTable, StringComparer.OrdinalIgnoreCase) ||
            ProtectedTables.Contains(targetTable, StringComparer.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException($"Table [{targetTable}] is protected and cannot be previewed or imported.");
        }

        var stagedPath = ResolveStagedPath(token);

        await using var dstConn = new SqliteConnection($"Data Source={targetSqlitePath};Mode=ReadOnly;Pooling=False;");
        await dstConn.OpenAsync(cancellationToken);
        var targetCols = await GetTableColumnsAsync(dstConn, targetTable, null, cancellationToken);
        if (targetCols.Count == 0)
        {
            throw new InvalidOperationException($"Target table [{targetTable}] is not an importable table.");
        }

        await using var srcConn = await OpenStagedSourceConnectionAsync(stagedPath, cancellationToken);
        var srcCols = await GetTableColumnsAsync(srcConn, sourceTable, null, cancellationToken);
        if (srcCols.Count == 0)
        {
            throw new InvalidOperationException($"Source table [{sourceTable}] was not found in the staged database.");
        }

        var rawSamples = await GetSampleRowsAsync(srcConn, sourceTable, cancellationToken);
        var analysis = BuildTableColumnAnalysis(sourceTable, srcCols, targetTable, targetCols, rawSamples, customColumnMap);
        var previewRows = analysis["preview_rows"] as List<Dictionary<string, object?>> ?? rawSamples;

        return new Dictionary<string, object?>
        {
            ["analysis"] = analysis,
            ["source_table"] = sourceTable,
            ["target_table"] = targetTable,
            ["column_mappings"] = analysis["column_mappings"],
            ["unmapped_source_columns"] = analysis["unmapped_source_columns"],
            ["sample_rows"] = previewRows,
            ["preview_rows"] = previewRows,
            ["stats"] = new Dictionary<string, object?>
            {
                ["matched_columns_count"] = analysis["matched_columns_count"],
                ["total_target_columns"] = analysis["total_target_columns"]
            }
        };
    }

    private static async Task<SqliteConnection> OpenStagedSourceConnectionAsync(string stagedSqlitePath, CancellationToken cancellationToken)
    {
        try
        {
            var rw = new SqliteConnection($"Data Source={stagedSqlitePath};Mode=ReadWrite;Pooling=False;");
            await rw.OpenAsync(cancellationToken);
            try
            {
                await using var cmd = rw.CreateCommand();
                cmd.CommandText = "PRAGMA busy_timeout=10000; PRAGMA wal_checkpoint(TRUNCATE);";
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }
            catch { }
            return rw;
        }
        catch
        {
            var ro = new SqliteConnection($"Data Source={stagedSqlitePath};Mode=ReadOnly;Pooling=False;");
            await ro.OpenAsync(cancellationToken);
            return ro;
        }
    }

    /// <summary>
    /// Post-import reconciliation:
    /// 1. Auto-creates opening batches (`OPENING-{id}`) for products that have quantity &gt; 0 in `products` but 0 rows in `batches`.
    /// 2. Syncs `formulations` table from distinct `products.formulation` values using lowercase `Formulation.NormalizeKey`.
    /// </summary>
    public async Task<Dictionary<string, int>> RunPostImportReconciliationAsync(
        KarobarDbContext db,
        CancellationToken cancellationToken = default)
    {
        var connStr = db.Database.GetConnectionString()
            ?? $"Data Source={Path.Combine(_appRoot, "database", "database.sqlite")};";
        await using var conn = new SqliteConnection(connStr);
        await conn.OpenAsync(cancellationToken);
        await SqliteConnectionInterceptor.ApplyPragmasAsync(conn, cancellationToken);
        await using var tx = conn.BeginTransaction(deferred: false);
        try
        {
            var result = await RunPostImportReconciliationCoreAsync(conn, tx, cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task<Dictionary<string, int>> RunPostImportReconciliationCoreAsync(
        SqliteConnection dstConn,
        SqliteTransaction? tx,
        CancellationToken cancellationToken = default)
    {
        var openingBatchesCreated = 0;
        var formulationsSynced = 0;
        var productsQuantityReconciled = 0;

        var hasProducts = await HasTableAsync(dstConn, "products", tx, cancellationToken);
        var hasBatches = await HasTableAsync(dstConn, "batches", tx, cancellationToken);
        var hasFormulations = await HasTableAsync(dstConn, "formulations", tx, cancellationToken);

        if (hasProducts && hasBatches)
        {
            var unbatched = new List<(long Id, int Qty, double Ppp, double Upp, double Psp, double Usp, double Wspp, double Wsup, long? StorageLocId, string? Rack)>();
            await using (var qCmd = dstConn.CreateCommand())
            {
                qCmd.Transaction = tx;
                qCmd.CommandText = """
                    SELECT p.id, p.quantity, p.pack_purchase_price, p.unit_purchase_price,
                           p.pack_sale_price, p.unit_sale_price, p.whole_sale_pack_price, p.whole_sale_unit_price,
                           p.storage_location_id, p.rack
                    FROM products p
                    WHERE p.quantity > 0
                      AND NOT EXISTS (SELECT 1 FROM batches b WHERE b.product_id = p.id);
                    """;
                await using var r = await qCmd.ExecuteReaderAsync(cancellationToken);
                while (await r.ReadAsync(cancellationToken))
                {
                    unbatched.Add((
                        Convert.ToInt64(r["id"], CultureInfo.InvariantCulture),
                        Convert.ToInt32(r["quantity"] is DBNull ? 0 : r["quantity"], CultureInfo.InvariantCulture),
                        Convert.ToDouble(r["pack_purchase_price"] is DBNull ? 0.0 : r["pack_purchase_price"], CultureInfo.InvariantCulture),
                        Convert.ToDouble(r["unit_purchase_price"] is DBNull ? 0.0 : r["unit_purchase_price"], CultureInfo.InvariantCulture),
                        Convert.ToDouble(r["pack_sale_price"] is DBNull ? 0.0 : r["pack_sale_price"], CultureInfo.InvariantCulture),
                        Convert.ToDouble(r["unit_sale_price"] is DBNull ? 0.0 : r["unit_sale_price"], CultureInfo.InvariantCulture),
                        Convert.ToDouble(r["whole_sale_pack_price"] is DBNull ? 0.0 : r["whole_sale_pack_price"], CultureInfo.InvariantCulture),
                        Convert.ToDouble(r["whole_sale_unit_price"] is DBNull ? 0.0 : r["whole_sale_unit_price"], CultureInfo.InvariantCulture),
                        r["storage_location_id"] is DBNull ? null : Convert.ToInt64(r["storage_location_id"], CultureInfo.InvariantCulture),
                        r["rack"] is DBNull ? null : r["rack"]?.ToString()
                    ));
                }
            }

            var defaultExpiry = DateTime.UtcNow.Date.AddYears(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

            foreach (var p in unbatched)
            {
                await using var insCmd = dstConn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = """
                    INSERT INTO batches (
                        product_id, batch_number, expiry_date, quantity,
                        pack_purchase_price, unit_purchase_price, pack_sale_price, unit_sale_price,
                        whole_sale_pack_price, whole_sale_unit_price, storage_location_id, rack,
                        created_at, updated_at
                    ) VALUES (
                        @pid, @bno, @exp, @qty,
                        @ppp, @upp, @psp, @usp,
                        @wspp, @wsup, @slid, @rack,
                        @now, @now
                    );
                    """;
                insCmd.Parameters.AddWithValue("@pid", p.Id);
                insCmd.Parameters.AddWithValue("@bno", $"OPENING-{p.Id}");
                insCmd.Parameters.AddWithValue("@exp", defaultExpiry);
                insCmd.Parameters.AddWithValue("@qty", p.Qty);
                insCmd.Parameters.AddWithValue("@ppp", p.Ppp);
                insCmd.Parameters.AddWithValue("@upp", p.Upp);
                insCmd.Parameters.AddWithValue("@psp", p.Psp);
                insCmd.Parameters.AddWithValue("@usp", p.Usp);
                insCmd.Parameters.AddWithValue("@wspp", p.Wspp);
                insCmd.Parameters.AddWithValue("@wsup", p.Wsup);
                insCmd.Parameters.AddWithValue("@slid", (object?)p.StorageLocId ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@rack", (object?)p.Rack ?? DBNull.Value);
                insCmd.Parameters.AddWithValue("@now", nowStr);
                await insCmd.ExecuteNonQueryAsync(cancellationToken);
                openingBatchesCreated++;
            }

            await using (var syncQtyCmd = dstConn.CreateCommand())
            {
                syncQtyCmd.Transaction = tx;
                syncQtyCmd.CommandText = """
                    UPDATE products
                    SET quantity = COALESCE((
                        SELECT SUM(MAX(0, b.quantity))
                        FROM batches b
                        WHERE b.product_id = products.id
                    ), 0)
                    WHERE EXISTS (SELECT 1 FROM batches b WHERE b.product_id = products.id)
                      AND quantity != COALESCE((
                        SELECT SUM(MAX(0, b.quantity))
                        FROM batches b
                        WHERE b.product_id = products.id
                    ), 0);
                    """;
                productsQuantityReconciled = await syncQtyCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (hasProducts && hasFormulations)
        {
            var distinctForms = new List<string>();
            await using (var fCmd = dstConn.CreateCommand())
            {
                fCmd.Transaction = tx;
                fCmd.CommandText = "SELECT DISTINCT formulation FROM products WHERE formulation IS NOT NULL AND TRIM(formulation) != '';";
                await using var fr = await fCmd.ExecuteReaderAsync(cancellationToken);
                while (await fr.ReadAsync(cancellationToken))
                {
                    var val = fr.GetString(0);
                    if (!string.IsNullOrWhiteSpace(val)) distinctForms.Add(val);
                }
            }

            var existingNorm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var eCmd = dstConn.CreateCommand())
            {
                eCmd.Transaction = tx;
                eCmd.CommandText = "SELECT normalized_name FROM formulations WHERE normalized_name IS NOT NULL;";
                await using var er = await eCmd.ExecuteReaderAsync(cancellationToken);
                while (await er.ReadAsync(cancellationToken))
                {
                    existingNorm.Add(er.GetString(0));
                }
            }

            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            foreach (var rawForm in distinctForms)
            {
                var norm = Formulation.NormalizeKey(rawForm);
                if (!string.IsNullOrEmpty(norm) && !existingNorm.Contains(norm))
                {
                    await using var insForm = dstConn.CreateCommand();
                    insForm.Transaction = tx;
                    insForm.CommandText = """
                        INSERT INTO formulations (name, normalized_name, created_at, updated_at)
                        VALUES (@name, @norm, @now, @now);
                        """;
                    insForm.Parameters.AddWithValue("@name", rawForm.Trim());
                    insForm.Parameters.AddWithValue("@norm", norm);
                    insForm.Parameters.AddWithValue("@now", nowStr);
                    await insForm.ExecuteNonQueryAsync(cancellationToken);
                    existingNorm.Add(norm);
                    formulationsSynced++;
                }
            }
        }

        return new Dictionary<string, int>
        {
            ["opening_batches_created"] = openingBatchesCreated,
            ["formulations_synced"] = formulationsSynced,
            ["products_quantity_reconciled"] = productsQuantityReconciled
        };
    }

    public Task<Dictionary<string, object?>> ExecuteImportAsync(
        KarobarDbContext db,
        string token,
        string mode = "merge",
        CancellationToken cancellationToken = default)
    {
        var targetPath = Path.Combine(_appRoot, "database", "database.sqlite");
        var connStr = db.Database.GetConnectionString();
        if (!string.IsNullOrWhiteSpace(connStr))
        {
            var builder = new SqliteConnectionStringBuilder(connStr);
            if (!string.IsNullOrWhiteSpace(builder.DataSource))
            {
                targetPath = builder.DataSource;
            }
        }

        return ExecuteImportCoreAsync(
            targetPath,
            token,
            new SqliteImportExecutionOptions
            {
                Mode = mode,
                ConflictPolicy = "upsert",
                ReconcileBatches = true,
                CurrentUserId = 1
            },
            cancellationToken);
    }

    /// <summary>
    /// Full relational 6-stage import engine with UI table/column/default overrides, parent->child `idMap` FK translation,
    /// polymorphic `party_id`/`invoice_id` translation, user FK remapping, natural-key matching, fallback master synthesis,
    /// and post-import reconciliation.
    /// </summary>
    public async Task<Dictionary<string, object?>> ExecuteImportCoreAsync(
        string targetSqlitePath,
        string token,
        SqliteImportExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        var stagedPath = ResolveStagedPath(token);
        var importMode = string.Equals(options.Mode, "replace", StringComparison.OrdinalIgnoreCase) ? "replace" : "merge";
        var conflictPolicy = string.Equals(options.ConflictPolicy, "skip", StringComparison.OrdinalIgnoreCase) ? "skip" : "upsert";

        var importLock = GetImportLock(targetSqlitePath);
        await importLock.WaitAsync(cancellationToken);
        try
        {
            await using var srcConn = await OpenStagedSourceConnectionAsync(stagedPath, cancellationToken);
            await using var dstConn = new SqliteConnection($"Data Source={targetSqlitePath};Pooling=False;");
            await dstConn.OpenAsync(cancellationToken);

            await SqliteConnectionInterceptor.ApplyPragmasAsync(dstConn, cancellationToken);
            await using (var pragmaCmd = dstConn.CreateCommand())
            {
                pragmaCmd.CommandText = "PRAGMA busy_timeout=30000; PRAGMA foreign_keys = OFF;";
                await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            try
            {
                var targetCatalog = await BuildTargetSchemaCatalogAsync(dstConn, cancellationToken);

        // Build user remapping lookup & valid target user set
        var userPreview = await InspectUserRemappingAsync(srcConn, dstConn, options.CurrentUserId, cancellationToken);
        var userMap = new Dictionary<long, long>();
        foreach (var u in userPreview)
        {
            var oldId = Convert.ToInt64(u["old_user_id"] ?? 0L, CultureInfo.InvariantCulture);
            var newId = Convert.ToInt64(u["mapped_to_user_id"] ?? options.CurrentUserId, CultureInfo.InvariantCulture);
            if (oldId > 0) userMap[oldId] = newId > 0 ? newId : options.CurrentUserId;
        }

        var validTargetUserIds = new HashSet<long>();
        await using (var uCmd = dstConn.CreateCommand())
        {
            uCmd.CommandText = "SELECT id FROM users ORDER BY id ASC;";
            await using var ur = await uCmd.ExecuteReaderAsync(cancellationToken);
            while (await ur.ReadAsync(cancellationToken))
            {
                validTargetUserIds.Add(ur.GetInt64(0));
            }
        }
        var currentUserId = options.CurrentUserId;
        if (validTargetUserIds.Count > 0 && !validTargetUserIds.Contains(currentUserId))
        {
            currentUserId = validTargetUserIds.First();
        }

        // Discover source tables and plan topological execution
        var srcTables = new List<string>();
        await using (var tblCmd = srcConn.CreateCommand())
        {
            tblCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            await using var tr = await tblCmd.ExecuteReaderAsync(cancellationToken);
            while (await tr.ReadAsync(cancellationToken))
            {
                var name = tr.GetString(0);
                if (!string.IsNullOrWhiteSpace(name) && !ProtectedTables.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    srcTables.Add(name);
                }
            }
        }

        var planned = new List<(string SrcTable, string TargetTable, int Stage, List<TargetColumnSchema> TargetCols, Dictionary<string, string> ColMap, Dictionary<string, string> StaticMap)>();
        var disabledTargetTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var enabledTargetTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var srcTable in srcTables)
        {
            var srcCols = await GetTableColumnsAsync(srcConn, srcTable, null, cancellationToken);
            var srcColNames = srcCols.Select(c => c.Name).ToList();

            string? targetTable;
            bool enabled;
            Dictionary<string, ColumnMappingOverride>? customColMap = null;

            if (options.Tables.TryGetValue(srcTable, out var tblOv))
            {
                enabled = tblOv.Enabled;
                targetTable = !string.IsNullOrWhiteSpace(tblOv.TargetTable)
                    ? tblOv.TargetTable.Trim().ToLowerInvariant()
                    : ResolveTargetTable(srcTable, srcColNames, targetCatalog).TargetTable;
                customColMap = tblOv.ColumnMap.Count > 0 ? tblOv.ColumnMap : null;
            }
            else
            {
                var (resolved, _) = ResolveTargetTable(srcTable, srcColNames, targetCatalog);
                targetTable = resolved;
                var isOptional = targetTable != null && OptionalConfigTables.Contains(targetTable, StringComparer.OrdinalIgnoreCase);
                enabled = targetTable != null && !isOptional;
            }

            if (!enabled || string.IsNullOrWhiteSpace(targetTable))
            {
                if (!string.IsNullOrWhiteSpace(targetTable) && !ProtectedTables.Contains(targetTable, StringComparer.OrdinalIgnoreCase))
                {
                    disabledTargetTables.Add(targetTable);
                }
                continue;
            }
            if (ProtectedTables.Contains(targetTable, StringComparer.OrdinalIgnoreCase)) continue;
            if (!targetCatalog.TryGetValue(targetTable, out var targetSchema))
            {
                disabledTargetTables.Add(targetTable);
                continue;
            }

            enabledTargetTables.Add(targetSchema.Table);

            var analysis = BuildTableColumnAnalysis(srcTable, srcCols, targetSchema.Table, targetSchema.Columns, new(), customColMap);
            var colMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var staticMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (analysis["column_mappings"] is List<Dictionary<string, object?>> mappingsList)
            {
                foreach (var cm in mappingsList)
                {
                    var tCol = cm["target_column"]?.ToString() ?? "";
                    var sCol = cm["source_column"]?.ToString();
                    var stVal = cm["static_value"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(sCol)) colMap[tCol] = sCol;
                    else if (customColMap != null && customColMap.TryGetValue(tCol, out var ov) &&
                             string.Equals(ov.SourceColumn, "__skip__", StringComparison.OrdinalIgnoreCase))
                    {
                        colMap[tCol] = "__skip__";
                    }
                    if (!string.IsNullOrEmpty(stVal)) staticMap[tCol] = stVal;
                }
            }

            planned.Add((srcTable, targetSchema.Table, targetSchema.Stage, targetSchema.Columns, colMap, staticMap));
        }

        foreach (var ovKv in options.Tables)
        {
            if (ovKv.Value.Enabled) continue;
            var ovTarget = !string.IsNullOrWhiteSpace(ovKv.Value.TargetTable)
                ? ovKv.Value.TargetTable.Trim().ToLowerInvariant()
                : ResolveTargetTable(ovKv.Key, Array.Empty<string>(), targetCatalog).TargetTable;
            if (!string.IsNullOrWhiteSpace(ovTarget) && !ProtectedTables.Contains(ovTarget, StringComparer.OrdinalIgnoreCase))
            {
                disabledTargetTables.Add(ovTarget);
            }
        }
        disabledTargetTables.ExceptWith(enabledTargetTables);

        planned = planned
            .OrderBy(p => p.Stage)
            .ThenBy(p => GetStageOrderIndex(p.Stage, p.TargetTable))
            .ThenBy(p => p.TargetTable, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var idMap = new Dictionary<string, Dictionary<long, long>>(StringComparer.OrdinalIgnoreCase);
        var fallbackEntityCache = new FallbackEntityCache();
        var tableReports = new List<Dictionary<string, object?>>();
        var tablesBySource = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var totalInserted = 0;
        var totalUpdated = 0;
        var totalSkipped = 0;
        var reconciliation = new Dictionary<string, int>
        {
            ["opening_batches_created"] = 0,
            ["formulations_synced"] = 0,
            ["products_quantity_reconciled"] = 0
        };

        await using var tx = dstConn.BeginTransaction(deferred: false);
        try
        {
            var preExistingFkViolations = await ReadForeignKeyViolationsAsync(dstConn, tx, cancellationToken);
            var clearedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (importMode == "replace")
            {
                clearedTables = await ClearTablesForReplaceModeAsync(
                    dstConn,
                    tx,
                    planned.Select(p => p.TargetTable),
                    targetCatalog,
                    cancellationToken);
            }

            foreach (var item in planned)
            {
                var report = await ImportSingleTableAsync(
                    srcConn,
                    dstConn,
                    tx,
                    item.SrcTable,
                    item.TargetTable,
                    item.TargetCols,
                    item.ColMap,
                    item.StaticMap,
                    conflictPolicy,
                    idMap,
                    disabledTargetTables,
                    userMap,
                    validTargetUserIds,
                    currentUserId,
                    fallbackEntityCache,
                    cancellationToken);

                tableReports.Add(report);
                tablesBySource[item.SrcTable] = report;
                totalInserted += Convert.ToInt32(report["inserted"] ?? 0, CultureInfo.InvariantCulture);
                totalUpdated += Convert.ToInt32(report["updated"] ?? 0, CultureInfo.InvariantCulture);
                totalSkipped += Convert.ToInt32(report["skipped"] ?? 0, CultureInfo.InvariantCulture);
            }

            if (options.ReconcileBatches)
            {
                reconciliation = await RunPostImportReconciliationCoreAsync(dstConn, tx, cancellationToken);
            }

            var postFkViolations = await ReadForeignKeyViolationsAsync(dstConn, tx, cancellationToken);
            foreach (var violation in postFkViolations)
            {
                if (clearedTables.Contains(violation.Table) ||
                    !preExistingFkViolations.Contains(violation))
                {
                    throw new InvalidOperationException(
                        $"Foreign key integrity violation after import in '{violation.Table}' (rowid {violation.RowId} -> '{violation.Parent}'): import rolled back.");
                }
            }

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }

                return new Dictionary<string, object?>
                {
                    ["ok"] = true,
                    ["mode"] = importMode,
                    ["conflict_policy"] = conflictPolicy,
                    ["tables_imported"] = tableReports.Count,
                    ["total_rows_inserted"] = totalInserted,
                    ["total_inserted"] = totalInserted,
                    ["total_updated"] = totalUpdated,
                    ["total_skipped"] = totalSkipped,
                    ["per_table"] = tableReports
                        .GroupBy(r => r["target_table"]?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            g => g.Key,
                            g => new Dictionary<string, int>
                            {
                                ["source_rows"] = g.Sum(r => Convert.ToInt32(r["source_rows"] ?? 0, CultureInfo.InvariantCulture)),
                                ["inserted"] = g.Sum(r => Convert.ToInt32(r["inserted"] ?? 0, CultureInfo.InvariantCulture)),
                                ["updated"] = g.Sum(r => Convert.ToInt32(r["updated"] ?? 0, CultureInfo.InvariantCulture)),
                                ["skipped"] = g.Sum(r => Convert.ToInt32(r["skipped"] ?? 0, CultureInfo.InvariantCulture))
                            },
                            StringComparer.OrdinalIgnoreCase),
                    ["tables"] = tableReports,
                    ["tables_by_source"] = tablesBySource,
                    ["reconciliation"] = reconciliation
                };
            }
            finally
            {
                try
                {
                    await using var fkCmd = dstConn.CreateCommand();
                    fkCmd.CommandText = "PRAGMA foreign_keys = ON;";
                    await fkCmd.ExecuteNonQueryAsync(CancellationToken.None);
                }
                catch { }
            }
        }
        finally
        {
            importLock.Release();
        }
    }

    private sealed class FkViolationComparer : IEqualityComparer<(string Table, long RowId, string Parent, long FkId)>
    {
        public static readonly FkViolationComparer Instance = new();

        public bool Equals((string Table, long RowId, string Parent, long FkId) x, (string Table, long RowId, string Parent, long FkId) y)
            => string.Equals(x.Table, y.Table, StringComparison.OrdinalIgnoreCase)
               && x.RowId == y.RowId
               && string.Equals(x.Parent, y.Parent, StringComparison.OrdinalIgnoreCase)
               && x.FkId == y.FkId;

        public int GetHashCode((string Table, long RowId, string Parent, long FkId) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Table),
                obj.RowId,
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Parent),
                obj.FkId);
    }

    private static async Task<HashSet<(string Table, long RowId, string Parent, long FkId)>> ReadForeignKeyViolationsAsync(
        SqliteConnection conn,
        SqliteTransaction tx,
        CancellationToken cancellationToken)
    {
        var violations = new HashSet<(string Table, long RowId, string Parent, long FkId)>(FkViolationComparer.Instance);
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "PRAGMA foreign_key_check;";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var table = reader.IsDBNull(0) ? "" : reader.GetString(0);
            var rowId = reader.IsDBNull(1) ? 0L : Convert.ToInt64(reader.GetValue(1), CultureInfo.InvariantCulture);
            var parent = reader.IsDBNull(2) ? "" : reader.GetString(2);
            var fkId = reader.IsDBNull(3) ? 0L : Convert.ToInt64(reader.GetValue(3), CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(table))
            {
                violations.Add((table, rowId, parent, fkId));
            }
        }
        return violations;
    }

    private static async Task<HashSet<string>> ClearTablesForReplaceModeAsync(
        SqliteConnection dstConn,
        SqliteTransaction tx,
        IEnumerable<string> plannedTargetTables,
        IReadOnlyDictionary<string, TargetTableSchema> targetCatalog,
        CancellationToken cancellationToken)
    {
        var toClear = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tbl in plannedTargetTables)
        {
            if (ProtectedTables.Contains(tbl, StringComparer.OrdinalIgnoreCase)) continue;
            if (tbl.Equals("settings", StringComparison.OrdinalIgnoreCase) ||
                tbl.Equals("terminals", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (targetCatalog.TryGetValue(tbl, out var schema))
            {
                toClear.Add(schema.Table);
            }
        }

        if (toClear.Count == 0)
        {
            return toClear;
        }

        var requiredChildrenByParent = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var requiredParentsByChild = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var nullableFkEdges = new List<(string ChildTable, string FkCol, string ParentTable)>();
        var nullableSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRequiredEdge(string child, string parent)
        {
            if (!requiredChildrenByParent.TryGetValue(parent, out var children))
            {
                children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                requiredChildrenByParent[parent] = children;
            }
            children.Add(child);

            if (!requiredParentsByChild.TryGetValue(child, out var parents))
            {
                parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                requiredParentsByChild[child] = parents;
            }
            parents.Add(parent);
        }

        foreach (var (childTable, childSchema) in targetCatalog)
        {
            if (ProtectedTables.Contains(childTable, StringComparer.OrdinalIgnoreCase)) continue;

            var colNotNull = childSchema.Columns.ToDictionary(c => c.Name, c => c.NotNull, StringComparer.OrdinalIgnoreCase);
            var escapedChild = childTable.Replace("\"", "\"\"");

            await using (var fkCmd = dstConn.CreateCommand())
            {
                fkCmd.Transaction = tx;
                fkCmd.CommandText = $"PRAGMA foreign_key_list(\"{escapedChild}\");";
                await using var r = await fkCmd.ExecuteReaderAsync(cancellationToken);
                while (await r.ReadAsync(cancellationToken))
                {
                    var parentTable = r["table"]?.ToString() ?? "";
                    var fkCol = r["from"]?.ToString() ?? "";
                    var onDelete = r["on_delete"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(parentTable) || string.IsNullOrWhiteSpace(fkCol)) continue;
                    if (ProtectedTables.Contains(parentTable, StringComparer.OrdinalIgnoreCase)) continue;
                    if (OptionalConfigTables.Contains(parentTable, StringComparer.OrdinalIgnoreCase)) continue;

                    var isNotNull = colNotNull.TryGetValue(fkCol, out var nn) && nn;
                    if (isNotNull || !string.Equals(onDelete, "SET NULL", StringComparison.OrdinalIgnoreCase))
                    {
                        AddRequiredEdge(childTable, parentTable);
                    }
                    else if (nullableSeen.Add($"{childTable}.{fkCol}->{parentTable}"))
                    {
                        nullableFkEdges.Add((childTable, fkCol, parentTable));
                    }
                }
            }

            if (ForeignKeyRules.TryGetValue(childTable, out var fkRules))
            {
                foreach (var (fkCol, rule) in fkRules)
                {
                    if (ProtectedTables.Contains(rule.ParentTable, StringComparer.OrdinalIgnoreCase)) continue;
                    if (OptionalConfigTables.Contains(rule.ParentTable, StringComparer.OrdinalIgnoreCase)) continue;
                    if (!targetCatalog.ContainsKey(rule.ParentTable)) continue;

                    if (!rule.Nullable)
                    {
                        AddRequiredEdge(childTable, rule.ParentTable);
                    }
                    else if (nullableSeen.Add($"{childTable}.{fkCol}->{rule.ParentTable}"))
                    {
                        nullableFkEdges.Add((childTable, fkCol, rule.ParentTable));
                    }
                }
            }
        }

        // Compute transitive non-protected child dependents of all cleared tables
        var queue = new Queue<string>(toClear);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!requiredChildrenByParent.TryGetValue(parent, out var children)) continue;
            foreach (var child in children)
            {
                if (ProtectedTables.Contains(child, StringComparer.OrdinalIgnoreCase)) continue;
                if (OptionalConfigTables.Contains(child, StringComparer.OrdinalIgnoreCase)) continue;
                if (toClear.Add(child))
                {
                    queue.Enqueue(child);
                }
            }
        }

        // Order deletions child-before-parent (reverse topological order)
        var initialOrdered = toClear
            .OrderByDescending(t => GetStageForTable(t))
            .ThenByDescending(t => GetStageOrderIndex(GetStageForTable(t), t))
            .ThenByDescending(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var orderedToClear = new List<string>(initialOrdered.Count);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void VisitParentFirstThenAddChild(string tbl)
        {
            // Post-order on parent->child guarantees every child is emitted BEFORE its parent
            if (visited.Contains(tbl) || !visiting.Add(tbl)) return;
            if (requiredChildrenByParent.TryGetValue(tbl, out var children))
            {
                foreach (var child in children.Where(c => toClear.Contains(c)))
                {
                    VisitParentFirstThenAddChild(child);
                }
            }
            visiting.Remove(tbl);
            visited.Add(tbl);
            orderedToClear.Add(tbl);
        }

        foreach (var tbl in initialOrdered)
        {
            VisitParentFirstThenAddChild(tbl);
        }

        foreach (var tbl in orderedToClear)
        {
            var escapedDst = tbl.Replace("\"", "\"\"");
            await using (var delCmd = dstConn.CreateCommand())
            {
                delCmd.Transaction = tx;
                delCmd.CommandText = $"DELETE FROM \"{escapedDst}\";";
                await delCmd.ExecuteNonQueryAsync(cancellationToken);
            }
            try
            {
                await using var seqDel = dstConn.CreateCommand();
                seqDel.Transaction = tx;
                seqDel.CommandText = "DELETE FROM sqlite_sequence WHERE name = @tbl;";
                seqDel.Parameters.AddWithValue("@tbl", tbl);
                await seqDel.ExecuteNonQueryAsync(cancellationToken);
            }
            catch { }
        }

        // Null out nullable FKs on un-cleared child tables whose parent was cleared
        foreach (var (childTable, fkCol, parentTable) in nullableFkEdges)
        {
            if (!toClear.Contains(parentTable) || toClear.Contains(childTable)) continue;
            if (!targetCatalog.TryGetValue(childTable, out var cSchema)) continue;
            if (!cSchema.Columns.Any(c => c.Name.Equals(fkCol, StringComparison.OrdinalIgnoreCase))) continue;

            var escapedChild = childTable.Replace("\"", "\"\"");
            var escapedCol = fkCol.Replace("\"", "\"\"");
            await using var nullCmd = dstConn.CreateCommand();
            nullCmd.Transaction = tx;
            nullCmd.CommandText = $"UPDATE \"{escapedChild}\" SET \"{escapedCol}\" = NULL WHERE \"{escapedCol}\" IS NOT NULL;";
            await nullCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        // Clean polymorphic references on un-cleared tables if their target parent table was cleared
        static bool HasAllColumns(TargetTableSchema schema, params string[] cols)
            => cols.All(col => schema.Columns.Any(c => c.Name.Equals(col, StringComparison.OrdinalIgnoreCase)));

        if (!toClear.Contains("trade_schemes") &&
            targetCatalog.TryGetValue("trade_schemes", out var tsSchema) &&
            HasAllColumns(tsSchema, "id", "target_type"))
        {
            var orphanSchemeIds = new List<long>();
            await using (var tsSel = dstConn.CreateCommand())
            {
                tsSel.Transaction = tx;
                tsSel.CommandText = "SELECT id, target_type FROM \"trade_schemes\" WHERE target_type IS NOT NULL;";
                await using var r = await tsSel.ExecuteReaderAsync(cancellationToken);
                while (await r.ReadAsync(cancellationToken))
                {
                    if (r.IsDBNull(0) || r.IsDBNull(1)) continue;
                    var tsId = Convert.ToInt64(r[0], CultureInfo.InvariantCulture);
                    var resolvedParent = ResolveTradeSchemeTargetTable(r[1]?.ToString());
                    if (resolvedParent != null && toClear.Contains(resolvedParent))
                    {
                        orphanSchemeIds.Add(tsId);
                    }
                }
            }

            foreach (var chunk in orphanSchemeIds.Chunk(500))
            {
                var paramNames = chunk.Select((_, idx) => $"@ts{idx}").ToArray();
                var inClause = string.Join(", ", paramNames);

                if (!toClear.Contains("trade_scheme_slabs") &&
                    targetCatalog.TryGetValue("trade_scheme_slabs", out var tssSchema) &&
                    HasAllColumns(tssSchema, "trade_scheme_id"))
                {
                    await using var slabDel = dstConn.CreateCommand();
                    slabDel.Transaction = tx;
                    slabDel.CommandText = $"DELETE FROM \"trade_scheme_slabs\" WHERE trade_scheme_id IN ({inClause});";
                    for (var k = 0; k < chunk.Length; k++) slabDel.Parameters.AddWithValue(paramNames[k], chunk[k]);
                    await slabDel.ExecuteNonQueryAsync(cancellationToken);
                }

                if (!toClear.Contains("sale_invoice_items") &&
                    targetCatalog.TryGetValue("sale_invoice_items", out var siiSchema) &&
                    HasAllColumns(siiSchema, "applied_scheme_id"))
                {
                    await using var siiNull = dstConn.CreateCommand();
                    siiNull.Transaction = tx;
                    siiNull.CommandText = $"UPDATE \"sale_invoice_items\" SET applied_scheme_id = NULL WHERE applied_scheme_id IN ({inClause});";
                    for (var k = 0; k < chunk.Length; k++) siiNull.Parameters.AddWithValue(paramNames[k], chunk[k]);
                    await siiNull.ExecuteNonQueryAsync(cancellationToken);
                }

                await using var tsDel = dstConn.CreateCommand();
                tsDel.Transaction = tx;
                tsDel.CommandText = $"DELETE FROM \"trade_schemes\" WHERE id IN ({inClause});";
                for (var k = 0; k < chunk.Length; k++) tsDel.Parameters.AddWithValue(paramNames[k], chunk[k]);
                await tsDel.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (!toClear.Contains("payment_allocations") &&
            targetCatalog.TryGetValue("payment_allocations", out var paSchema) &&
            HasAllColumns(paSchema, "invoice_type"))
        {
            if (toClear.Contains("sale_invoices"))
            {
                await using var paSaleDel = dstConn.CreateCommand();
                paSaleDel.Transaction = tx;
                paSaleDel.CommandText = "DELETE FROM \"payment_allocations\" WHERE LOWER(invoice_type) LIKE '%sale%';";
                await paSaleDel.ExecuteNonQueryAsync(cancellationToken);
            }
            if (toClear.Contains("purchase_invoices"))
            {
                await using var paPurDel = dstConn.CreateCommand();
                paPurDel.Transaction = tx;
                paPurDel.CommandText = "DELETE FROM \"payment_allocations\" WHERE LOWER(invoice_type) LIKE '%purchase%';";
                await paPurDel.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (!toClear.Contains("stocktake_sessions") &&
            targetCatalog.TryGetValue("stocktake_sessions", out var stSchema) &&
            HasAllColumns(stSchema, "id", "scope_type", "scope_id"))
        {
            var orphanScopeIds = new List<long>();
            await using (var stSel = dstConn.CreateCommand())
            {
                stSel.Transaction = tx;
                stSel.CommandText = "SELECT id, scope_type FROM \"stocktake_sessions\" WHERE scope_id IS NOT NULL AND scope_type IS NOT NULL;";
                await using var r = await stSel.ExecuteReaderAsync(cancellationToken);
                while (await r.ReadAsync(cancellationToken))
                {
                    if (r.IsDBNull(0) || r.IsDBNull(1)) continue;
                    var resolvedParent = ResolveStocktakeScopeTargetTable(r[1]?.ToString());
                    if (resolvedParent != null && toClear.Contains(resolvedParent))
                    {
                        orphanScopeIds.Add(Convert.ToInt64(r[0], CultureInfo.InvariantCulture));
                    }
                }
            }

            foreach (var chunk in orphanScopeIds.Chunk(500))
            {
                var paramNames = chunk.Select((_, idx) => $"@st{idx}").ToArray();
                await using var stNull = dstConn.CreateCommand();
                stNull.Transaction = tx;
                stNull.CommandText = $"UPDATE \"stocktake_sessions\" SET scope_id = NULL WHERE id IN ({string.Join(", ", paramNames)});";
                for (var k = 0; k < chunk.Length; k++) stNull.Parameters.AddWithValue(paramNames[k], chunk[k]);
                await stNull.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (!toClear.Contains("invoice_drafts") &&
            targetCatalog.TryGetValue("invoice_drafts", out var idSchema) &&
            HasAllColumns(idSchema, "id", "posted_invoice_id", "posted_invoice_type", "type"))
        {
            var orphanDraftIds = new List<long>();
            await using (var idSel = dstConn.CreateCommand())
            {
                idSel.Transaction = tx;
                idSel.CommandText = "SELECT id, posted_invoice_type, type FROM \"invoice_drafts\" WHERE posted_invoice_id IS NOT NULL;";
                await using var r = await idSel.ExecuteReaderAsync(cancellationToken);
                while (await r.ReadAsync(cancellationToken))
                {
                    if (r.IsDBNull(0)) continue;
                    var postedType = r.IsDBNull(1) ? null : r[1]?.ToString();
                    var draftType = r.IsDBNull(2) ? null : r[2]?.ToString();
                    var resolvedParent = ResolveInvoiceDraftTargetTable(postedType, draftType);
                    if (resolvedParent != null && toClear.Contains(resolvedParent))
                    {
                        orphanDraftIds.Add(Convert.ToInt64(r[0], CultureInfo.InvariantCulture));
                    }
                }
            }

            foreach (var chunk in orphanDraftIds.Chunk(500))
            {
                var paramNames = chunk.Select((_, idx) => $"@id{idx}").ToArray();
                await using var idNull = dstConn.CreateCommand();
                idNull.Transaction = tx;
                idNull.CommandText = $"UPDATE \"invoice_drafts\" SET posted_invoice_id = NULL WHERE id IN ({string.Join(", ", paramNames)});";
                for (var k = 0; k < chunk.Length; k++) idNull.Parameters.AddWithValue(paramNames[k], chunk[k]);
                await idNull.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        foreach (var txTable in new[] { "account_transactions", "cash_drawer_transactions" })
        {
            if (!toClear.Contains(txTable) &&
                targetCatalog.TryGetValue(txTable, out var txSchema) &&
                HasAllColumns(txSchema, "id", "reference_type", "reference_id"))
            {
                var escapedTxTable = txTable.Replace("\"", "\"\"");
                var orphanRefIds = new List<long>();
                await using (var refSel = dstConn.CreateCommand())
                {
                    refSel.Transaction = tx;
                    refSel.CommandText = $"SELECT id, reference_type FROM \"{escapedTxTable}\" WHERE reference_id IS NOT NULL AND reference_type IS NOT NULL;";
                    await using var r = await refSel.ExecuteReaderAsync(cancellationToken);
                    while (await r.ReadAsync(cancellationToken))
                    {
                        if (r.IsDBNull(0) || r.IsDBNull(1)) continue;
                        var resolvedParent = ResolveTransactionReferenceTargetTable(r[1]?.ToString());
                        if (resolvedParent != null && toClear.Contains(resolvedParent))
                        {
                            orphanRefIds.Add(Convert.ToInt64(r[0], CultureInfo.InvariantCulture));
                        }
                    }
                }

                foreach (var chunk in orphanRefIds.Chunk(500))
                {
                    var paramNames = chunk.Select((_, idx) => $"@rt{idx}").ToArray();
                    await using var refNull = dstConn.CreateCommand();
                    refNull.Transaction = tx;
                    refNull.CommandText = $"UPDATE \"{escapedTxTable}\" SET reference_id = NULL WHERE id IN ({string.Join(", ", paramNames)});";
                    for (var k = 0; k < chunk.Length; k++) refNull.Parameters.AddWithValue(paramNames[k], chunk[k]);
                    await refNull.ExecuteNonQueryAsync(cancellationToken);
                }
            }
        }

        if (!toClear.Contains("document_histories") &&
            targetCatalog.TryGetValue("document_histories", out var dhSchema) &&
            HasAllColumns(dhSchema, "id", "entity_type"))
        {
            var orphanDocIds = new List<long>();
            await using (var dhSel = dstConn.CreateCommand())
            {
                dhSel.Transaction = tx;
                dhSel.CommandText = "SELECT id, entity_type FROM \"document_histories\" WHERE entity_type IS NOT NULL;";
                await using var r = await dhSel.ExecuteReaderAsync(cancellationToken);
                while (await r.ReadAsync(cancellationToken))
                {
                    if (r.IsDBNull(0) || r.IsDBNull(1)) continue;
                    var resolvedParent = ResolveDocumentHistoryTargetTable(r[1]?.ToString());
                    if (resolvedParent != null && toClear.Contains(resolvedParent))
                    {
                        orphanDocIds.Add(Convert.ToInt64(r[0], CultureInfo.InvariantCulture));
                    }
                }
            }

            foreach (var chunk in orphanDocIds.Chunk(500))
            {
                var paramNames = chunk.Select((_, idx) => $"@dh{idx}").ToArray();
                await using var dhDel = dstConn.CreateCommand();
                dhDel.Transaction = tx;
                dhDel.CommandText = $"DELETE FROM \"document_histories\" WHERE id IN ({string.Join(", ", paramNames)});";
                for (var k = 0; k < chunk.Length; k++) dhDel.Parameters.AddWithValue(paramNames[k], chunk[k]);
                await dhDel.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        if (!toClear.Contains("journal_entry_lines") &&
            targetCatalog.TryGetValue("journal_entry_lines", out var jelSchema) &&
            HasAllColumns(jelSchema, "party_type", "party_id"))
        {
            if (toClear.Contains("customers"))
            {
                await using var jelCustNull = dstConn.CreateCommand();
                jelCustNull.Transaction = tx;
                jelCustNull.CommandText = "UPDATE \"journal_entry_lines\" SET party_id = NULL WHERE party_id IS NOT NULL AND LOWER(party_type) LIKE '%customer%';";
                await jelCustNull.ExecuteNonQueryAsync(cancellationToken);
            }
            if (toClear.Contains("suppliers"))
            {
                await using var jelSupNull = dstConn.CreateCommand();
                jelSupNull.Transaction = tx;
                jelSupNull.CommandText = "UPDATE \"journal_entry_lines\" SET party_id = NULL WHERE party_id IS NOT NULL AND LOWER(party_type) LIKE '%supplier%';";
                await jelSupNull.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        return toClear;
    }

    private sealed class NaturalKeyIndex
    {
        public Dictionary<string, Dictionary<string, long>> SingleColumns { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, long> Composite { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class FallbackEntityCache
    {
        public Dictionary<string, HashSet<long>> ExistingParentIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, long>> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, object?>> ImportSingleTableAsync(
        SqliteConnection srcConn,
        SqliteConnection dstConn,
        SqliteTransaction tx,
        string sourceTable,
        string targetTable,
        List<TargetColumnSchema> targetCols,
        Dictionary<string, string> columnMap,
        Dictionary<string, string> staticMap,
        string conflictPolicy,
        Dictionary<string, Dictionary<long, long>> idMap,
        HashSet<string> disabledTargetTables,
        Dictionary<long, long> userMap,
        HashSet<long> validTargetUserIds,
        long currentUserId,
        FallbackEntityCache fallbackCache,
        CancellationToken cancellationToken)
    {
        var escapedSrc = sourceTable.Replace("\"", "\"\"");
        var escapedDst = targetTable.Replace("\"", "\"\"");

        var sourceRows = new List<Dictionary<string, object?>>();
        await using (var selCmd = srcConn.CreateCommand())
        {
            selCmd.CommandText = $"SELECT * FROM \"{escapedSrc}\";";
            await using var reader = await selCmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                }
                sourceRows.Add(row);
            }
        }

        if (targetTable.Equals("settings", StringComparison.OrdinalIgnoreCase))
        {
            var upd = 0;
            if (sourceRows.Count > 0)
            {
                upd = await ImportSettingsRowAsync(dstConn, tx, sourceRows[0], targetCols, columnMap, staticMap, cancellationToken) ? 1 : 0;
            }
            else if (staticMap.Count > 0)
            {
                upd = await ImportSettingsRowAsync(dstConn, tx, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase), targetCols, columnMap, staticMap, cancellationToken) ? 1 : 0;
            }
            return new Dictionary<string, object?>
            {
                ["source_table"] = sourceTable,
                ["target_table"] = targetTable,
                ["source_rows"] = sourceRows.Count,
                ["inserted"] = 0,
                ["updated"] = upd,
                ["skipped"] = 0,
                ["warnings"] = new List<string>()
            };
        }

        if (!idMap.TryGetValue(targetTable, out var tableIdMap))
        {
            tableIdMap = new Dictionary<long, long>();
            idMap[targetTable] = tableIdMap;
        }

        var hasIdPk = targetCols.Any(c => c.Name.Equals("id", StringComparison.OrdinalIgnoreCase) && c.Pk);
        var existingIds = new HashSet<long>();
        if (hasIdPk)
        {
            await using var idCmd = dstConn.CreateCommand();
            idCmd.Transaction = tx;
            idCmd.CommandText = $"SELECT id FROM \"{escapedDst}\";";
            await using var ir = await idCmd.ExecuteReaderAsync(cancellationToken);
            while (await ir.ReadAsync(cancellationToken))
            {
                if (!ir.IsDBNull(0)) existingIds.Add(Convert.ToInt64(ir.GetValue(0), CultureInfo.InvariantCulture));
            }
        }

        var naturalKeyIndex = await PreloadNaturalKeyIndexAsync(dstConn, tx, targetTable, targetCols, cancellationToken);

        var existingProductCodes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var existingBarcodes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (targetTable.Equals("products", StringComparison.OrdinalIgnoreCase))
        {
            await using var pCmd = dstConn.CreateCommand();
            pCmd.Transaction = tx;
            pCmd.CommandText = "SELECT id, product_code, barcode FROM products;";
            await using var pr = await pCmd.ExecuteReaderAsync(cancellationToken);
            while (await pr.ReadAsync(cancellationToken))
            {
                var pid = Convert.ToInt64(pr["id"], CultureInfo.InvariantCulture);
                var pc = pr["product_code"]?.ToString()?.Trim() ?? "";
                var bc = pr["barcode"]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(pc)) existingProductCodes[pc] = pid;
                if (!string.IsNullOrEmpty(bc)) existingBarcodes[bc] = pid;
            }
        }

        var productMetaCache = new Dictionary<long, ProductMeta>();
        if (targetTable is "purchase_invoice_items" or "sale_invoice_items" or "sale_return_items" or "purchase_return_items" or "batches")
        {
            await using var pmCmd = dstConn.CreateCommand();
            pmCmd.Transaction = tx;
            pmCmd.CommandText = "SELECT id, pack_size, unit_purchase_price, unit_sale_price, pack_purchase_price, pack_sale_price FROM products;";
            await using var pmr = await pmCmd.ExecuteReaderAsync(cancellationToken);
            while (await pmr.ReadAsync(cancellationToken))
            {
                var pid = Convert.ToInt64(pmr["id"], CultureInfo.InvariantCulture);
                productMetaCache[pid] = new ProductMeta(
                    PackSize: Convert.ToInt32(pmr["pack_size"] is DBNull ? 1 : pmr["pack_size"], CultureInfo.InvariantCulture),
                    UnitPurchasePrice: Convert.ToDouble(pmr["unit_purchase_price"] is DBNull ? 0.0 : pmr["unit_purchase_price"], CultureInfo.InvariantCulture),
                    UnitSalePrice: Convert.ToDouble(pmr["unit_sale_price"] is DBNull ? 0.0 : pmr["unit_sale_price"], CultureInfo.InvariantCulture));
            }
        }

        var inserted = 0;
        var updated = 0;
        var skipped = 0;
        var warnings = new List<string>();

        for (var i = 0; i < sourceRows.Count; i++)
        {
            var srcRow = sourceRows[i];
            var rowIndex = i + 1;
            long? rawSourceId = null;
            if (srcRow.TryGetValue("id", out var rawIdObj) && rawIdObj != null && long.TryParse(rawIdObj.ToString(), out var parsedId))
            {
                rawSourceId = parsedId;
            }

            var (rowPayload, skipReason) = await TransformRowForTargetAsync(
                dstConn,
                tx,
                targetTable,
                targetCols,
                srcRow,
                columnMap,
                staticMap,
                rowIndex,
                rawSourceId,
                idMap,
                disabledTargetTables,
                userMap,
                validTargetUserIds,
                currentUserId,
                fallbackCache,
                productMetaCache,
                existingProductCodes,
                existingBarcodes,
                naturalKeyIndex,
                cancellationToken);

            if (rowPayload == null)
            {
                skipped++;
                if (!string.IsNullOrWhiteSpace(skipReason) && warnings.Count < 10)
                {
                    warnings.Add($"Row #{rowIndex}: {skipReason}");
                }
                continue;
            }

            var matchedExistingId = FindExistingIdByNaturalKey(
                targetTable,
                rowPayload,
                naturalKeyIndex,
                existingProductCodes,
                existingBarcodes);

            if (matchedExistingId.HasValue)
            {
                if (rawSourceId.HasValue)
                {
                    tableIdMap[rawSourceId.Value] = matchedExistingId.Value;
                }

                if (conflictPolicy == "skip")
                {
                    skipped++;
                    continue;
                }

                var updatePayload = new Dictionary<string, object?>(rowPayload, StringComparer.OrdinalIgnoreCase);
                updatePayload.Remove("id");
                updatePayload.Remove("created_at");

                if (HasCrossFieldUniqueCollision(
                        targetTable,
                        matchedExistingId.Value,
                        updatePayload,
                        naturalKeyIndex,
                        existingProductCodes,
                        existingBarcodes,
                        out var collisionReason))
                {
                    skipped++;
                    if (warnings.Count < 10) warnings.Add($"Row #{rowIndex} update skipped: {collisionReason}");
                    continue;
                }

                if (updatePayload.Count > 0)
                {
                    try
                    {
                        var setClauses = new List<string>();
                        await using var updCmd = dstConn.CreateCommand();
                        updCmd.Transaction = tx;
                        var pIdx = 0;
                        foreach (var kv in updatePayload)
                        {
                            var colEsc = kv.Key.Replace("\"", "\"\"");
                            setClauses.Add($"\"{colEsc}\" = @u{pIdx}");
                            updCmd.Parameters.AddWithValue($"@u{pIdx}", kv.Value ?? DBNull.Value);
                            pIdx++;
                        }
                        updCmd.Parameters.AddWithValue("@targetId", matchedExistingId.Value);
                        updCmd.CommandText = $"UPDATE \"{escapedDst}\" SET {string.Join(", ", setClauses)} WHERE id = @targetId;";
                        await updCmd.ExecuteNonQueryAsync(cancellationToken);
                        EvictFromNaturalKeyIndex(targetTable, matchedExistingId.Value, naturalKeyIndex, existingProductCodes, existingBarcodes);
                        RegisterInNaturalKeyIndex(targetTable, matchedExistingId.Value, updatePayload, naturalKeyIndex, existingProductCodes, existingBarcodes);
                        updated++;
                    }
                    catch (Exception uEx)
                    {
                        skipped++;
                        if (warnings.Count < 10) warnings.Add($"Row #{rowIndex} update skipped: {uEx.Message}");
                    }
                }
                else
                {
                    skipped++;
                }
                continue;
            }

            var insertPayload = new Dictionary<string, object?>(rowPayload, StringComparer.OrdinalIgnoreCase);
            long? assignedId = null;

            if (hasIdPk)
            {
                if (rawSourceId.HasValue && rawSourceId.Value > 0 && !existingIds.Contains(rawSourceId.Value))
                {
                    insertPayload["id"] = rawSourceId.Value;
                    assignedId = rawSourceId.Value;
                }
                else
                {
                    insertPayload.Remove("id");
                }
            }

            try
            {
                var cols = insertPayload.Keys.ToList();
                var colList = string.Join(", ", cols.Select(c => $"\"{c.Replace("\"", "\"\"")}\""));
                var paramList = string.Join(", ", cols.Select((_, idx) => $"@p{idx}"));

                await using var insCmd = dstConn.CreateCommand();
                insCmd.Transaction = tx;
                insCmd.CommandText = $"INSERT INTO \"{escapedDst}\" ({colList}) VALUES ({paramList});";
                for (var c = 0; c < cols.Count; c++)
                {
                    insCmd.Parameters.AddWithValue($"@p{c}", insertPayload[cols[c]] ?? DBNull.Value);
                }
                await insCmd.ExecuteNonQueryAsync(cancellationToken);

                long newId = assignedId ?? 0;
                if (hasIdPk && newId <= 0)
                {
                    await using var lastCmd = dstConn.CreateCommand();
                    lastCmd.Transaction = tx;
                    lastCmd.CommandText = "SELECT last_insert_rowid();";
                    newId = Convert.ToInt64(await lastCmd.ExecuteScalarAsync(cancellationToken) ?? 0L, CultureInfo.InvariantCulture);
                }

                if (hasIdPk && newId > 0)
                {
                    existingIds.Add(newId);
                    if (rawSourceId.HasValue)
                    {
                        tableIdMap[rawSourceId.Value] = newId;
                    }
                    RegisterInNaturalKeyIndex(targetTable, newId, insertPayload, naturalKeyIndex, existingProductCodes, existingBarcodes);
                }

                inserted++;
            }
            catch (Exception insEx)
            {
                skipped++;
                if (warnings.Count < 10) warnings.Add($"Row #{rowIndex} insert skipped: {insEx.Message}");
            }
        }

        if (hasIdPk)
        {
            try
            {
                await using var maxCmd = dstConn.CreateCommand();
                maxCmd.Transaction = tx;
                maxCmd.CommandText = $"SELECT MAX(id) FROM \"{escapedDst}\";";
                var maxScalar = await maxCmd.ExecuteScalarAsync(cancellationToken);
                if (maxScalar != null && maxScalar is not DBNull && Convert.ToInt64(maxScalar, CultureInfo.InvariantCulture) is > 0 and var maxId)
                {
                    await using var seqCmd = dstConn.CreateCommand();
                    seqCmd.Transaction = tx;
                    seqCmd.CommandText = "DELETE FROM sqlite_sequence WHERE name = @tbl; INSERT INTO sqlite_sequence (name, seq) VALUES (@tbl, @maxId);";
                    seqCmd.Parameters.AddWithValue("@tbl", targetTable);
                    seqCmd.Parameters.AddWithValue("@maxId", maxId);
                    await seqCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }
            catch { }
        }

        return new Dictionary<string, object?>
        {
            ["source_table"] = sourceTable,
            ["target_table"] = targetTable,
            ["source_rows"] = sourceRows.Count,
            ["inserted"] = inserted,
            ["updated"] = updated,
            ["skipped"] = skipped,
            ["warnings"] = warnings
        };
    }

    private sealed record ProductMeta(int PackSize, double UnitPurchasePrice, double UnitSalePrice);

    private async Task<(Dictionary<string, object?>? Payload, string? SkipReason)> TransformRowForTargetAsync(
        SqliteConnection dstConn,
        SqliteTransaction tx,
        string targetTable,
        List<TargetColumnSchema> targetCols,
        Dictionary<string, object?> srcRow,
        Dictionary<string, string> columnMap,
        Dictionary<string, string> staticMap,
        int rowIndex,
        long? rawSourceId,
        Dictionary<string, Dictionary<long, long>> idMap,
        HashSet<string> disabledTargetTables,
        Dictionary<long, long> userMap,
        HashSet<long> validTargetUserIds,
        long currentUserId,
        FallbackEntityCache fallbackCache,
        Dictionary<long, ProductMeta> productMetaCache,
        Dictionary<string, long> existingProductCodes,
        Dictionary<string, long> existingBarcodes,
        NaturalKeyIndex naturalKeyIndex,
        CancellationToken cancellationToken)
    {
        var outRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var tc in targetCols)
        {
            var colName = tc.Name;
            object? val = null;

            if (columnMap.TryGetValue(colName, out var mappedSrc))
            {
                if (!string.Equals(mappedSrc, "__skip__", StringComparison.OrdinalIgnoreCase) &&
                    srcRow.TryGetValue(mappedSrc, out var mappedVal))
                {
                    val = mappedVal;
                }
            }
            else if (srcRow.TryGetValue(colName, out var directVal))
            {
                val = directVal;
            }

            if ((val == null || val is DBNull || (val is string s0 && string.IsNullOrWhiteSpace(s0))) &&
                staticMap.TryGetValue(colName, out var staticVal))
            {
                val = staticVal;
            }

            if (val is DBNull) val = null;
            if (val is string str)
            {
                str = str.Trim();
                val = (str.Length == 0 && !tc.NotNull) ? null : str;
            }

            outRow[colName] = val;
        }

        // Normalize blank customer email to NULL so UNIQUE index on customers.email never collides on ""
        if (targetTable.Equals("customers", StringComparison.OrdinalIgnoreCase) &&
            outRow.TryGetValue("email", out var custEmail) &&
            custEmail is string ce && string.IsNullOrWhiteSpace(ce))
        {
            outRow["email"] = null;
        }

        // 2. Remap user foreign keys
        foreach (var tc in targetCols)
        {
            var colName = tc.Name;
            if (!UserForeignKeyColumns.Contains(colName, StringComparer.OrdinalIgnoreCase)) continue;

            var rawUserVal = outRow.GetValueOrDefault(colName);
            if (rawUserVal != null && long.TryParse(rawUserVal.ToString(), out var uId) && uId > 0)
            {
                if (userMap.TryGetValue(uId, out var mappedUid))
                {
                    outRow[colName] = mappedUid;
                }
                else if (validTargetUserIds.Contains(uId))
                {
                    outRow[colName] = uId;
                }
                else
                {
                    outRow[colName] = tc.NotNull ? currentUserId : null;
                }
            }
            else
            {
                outRow[colName] = tc.NotNull ? currentUserId : null;
            }
        }

        // 3. Resolve & translate foreign keys
        if (ForeignKeyRules.TryGetValue(targetTable, out var fkRules))
        {
            foreach (var (fkCol, rule) in fkRules)
            {
                if (!outRow.ContainsKey(fkCol)) continue;
                var rawFkVal = outRow[fkCol];

                var resolvedId = await ResolveForeignKeyValueAsync(
                    dstConn,
                    tx,
                    rule.ParentTable,
                    fkCol,
                    rawFkVal,
                    srcRow,
                    idMap,
                    disabledTargetTables,
                    rule.Nullable,
                    rule.FallbackMode,
                    fallbackCache,
                    cancellationToken);

                if (!resolvedId.HasValue && !rule.Nullable && rule.FallbackMode == "skip_row")
                {
                    return (null, $"Missing or orphaned parent reference `{fkCol}` -> `{rule.ParentTable}`");
                }

                outRow[fkCol] = resolvedId.HasValue ? resolvedId.Value : null;
            }
        }

        // Polymorphic party_id translation on payments & journal_entry_lines
        if ((targetTable.Equals("payments", StringComparison.OrdinalIgnoreCase) ||
             targetTable.Equals("journal_entry_lines", StringComparison.OrdinalIgnoreCase)) &&
            outRow.TryGetValue("party_id", out var partyIdObj) &&
            partyIdObj != null &&
            long.TryParse(partyIdObj.ToString(), out var oldPartyId) &&
            oldPartyId > 0)
        {
            var partyType = (outRow.GetValueOrDefault("party_type")?.ToString() ?? "").Trim().ToLowerInvariant();
            string? partyParentTable = partyType.Contains("customer", StringComparison.Ordinal) ? "customers"
                : partyType.Contains("supplier", StringComparison.Ordinal) ? "suppliers"
                : null;
            if (partyParentTable != null)
            {
                var resolvedPartyId = await ResolvePolymorphicIdAsync(
                    dstConn, tx, partyParentTable, oldPartyId, idMap, disabledTargetTables, fallbackCache, cancellationToken);
                if (!resolvedPartyId.HasValue)
                {
                    if (targetTable.Equals("payments", StringComparison.OrdinalIgnoreCase))
                    {
                        return (null, $"Missing or orphaned polymorphic parent reference `party_id` -> `{partyParentTable}`");
                    }
                    outRow["party_id"] = null;
                }
                else
                {
                    outRow["party_id"] = resolvedPartyId.Value;
                }
            }
        }

        // Polymorphic invoice_id translation on payment_allocations
        if (targetTable.Equals("payment_allocations", StringComparison.OrdinalIgnoreCase) &&
            outRow.TryGetValue("invoice_id", out var invIdObj) &&
            invIdObj != null &&
            long.TryParse(invIdObj.ToString(), out var oldInvId) &&
            oldInvId > 0)
        {
            var invType = (outRow.GetValueOrDefault("invoice_type")?.ToString() ?? "").Trim().ToLowerInvariant();
            string? invParentTable = invType.Contains("sale", StringComparison.Ordinal) ? "sale_invoices"
                : invType.Contains("purchase", StringComparison.Ordinal) ? "purchase_invoices"
                : null;
            if (invParentTable != null)
            {
                var resolvedInvId = await ResolvePolymorphicIdAsync(
                    dstConn, tx, invParentTable, oldInvId, idMap, disabledTargetTables, fallbackCache, cancellationToken);
                if (!resolvedInvId.HasValue)
                {
                    return (null, $"Missing or orphaned polymorphic parent reference `invoice_id` -> `{invParentTable}`");
                }
                outRow["invoice_id"] = resolvedInvId.Value;
            }
        }

        // Polymorphic target_id translation on trade_schemes (target_type: product | category | brand; target_id is NOT NULL)
        if (targetTable.Equals("trade_schemes", StringComparison.OrdinalIgnoreCase))
        {
            var targetType = (outRow.GetValueOrDefault("target_type")?.ToString() ?? "").Trim();
            var targetParent = ResolveTradeSchemeTargetTable(targetType);
            if (targetParent != null)
            {
                var oldTargetId = TryParseLong(outRow.GetValueOrDefault("target_id"));
                if (!oldTargetId.HasValue || oldTargetId.Value <= 0)
                {
                    return (null, $"Missing polymorphic `target_id` for `trade_schemes` (`{targetType}`)");
                }
                var resolvedTargetId = await ResolvePolymorphicIdAsync(
                    dstConn, tx, targetParent, oldTargetId.Value, idMap, disabledTargetTables, fallbackCache, cancellationToken);
                if (!resolvedTargetId.HasValue)
                {
                    return (null, $"Missing or orphaned polymorphic parent reference `target_id` -> `{targetParent}`");
                }
                outRow["target_id"] = resolvedTargetId.Value;
            }
        }

        // Polymorphic scope_id translation on stocktake_sessions (scope_type: category | brand | supplier | storage_location; nullable)
        if (targetTable.Equals("stocktake_sessions", StringComparison.OrdinalIgnoreCase) &&
            outRow.TryGetValue("scope_id", out var scopeIdObj) &&
            scopeIdObj != null &&
            long.TryParse(scopeIdObj.ToString(), out var oldScopeId) &&
            oldScopeId > 0)
        {
            var scopeType = (outRow.GetValueOrDefault("scope_type")?.ToString() ?? "").Trim();
            var scopeParent = ResolveStocktakeScopeTargetTable(scopeType);
            if (scopeParent != null)
            {
                var resolvedScopeId = await ResolvePolymorphicIdAsync(
                    dstConn, tx, scopeParent, oldScopeId, idMap, disabledTargetTables, fallbackCache, cancellationToken);
                outRow["scope_id"] = resolvedScopeId.HasValue ? resolvedScopeId.Value : null;
            }
        }

        // Polymorphic posted_invoice_id translation on invoice_drafts (posted_invoice_type / fallback type; nullable)
        if (targetTable.Equals("invoice_drafts", StringComparison.OrdinalIgnoreCase) &&
            outRow.TryGetValue("posted_invoice_id", out var postedInvIdObj) &&
            postedInvIdObj != null &&
            long.TryParse(postedInvIdObj.ToString(), out var oldPostedInvId) &&
            oldPostedInvId > 0)
        {
            var postedType = outRow.GetValueOrDefault("posted_invoice_type")?.ToString();
            var draftType = outRow.GetValueOrDefault("type")?.ToString();
            var invParent = ResolveInvoiceDraftTargetTable(postedType, draftType);
            if (invParent != null)
            {
                var resolvedPostedId = await ResolvePolymorphicIdAsync(
                    dstConn, tx, invParent, oldPostedInvId, idMap, disabledTargetTables, fallbackCache, cancellationToken);
                outRow["posted_invoice_id"] = resolvedPostedId.HasValue ? resolvedPostedId.Value : null;
            }
        }

        // Polymorphic reference_id translation on account_transactions & cash_drawer_transactions (nullable; skip non-entity types)
        if ((targetTable.Equals("account_transactions", StringComparison.OrdinalIgnoreCase) ||
             targetTable.Equals("cash_drawer_transactions", StringComparison.OrdinalIgnoreCase)) &&
            outRow.TryGetValue("reference_id", out var refIdObj) &&
            refIdObj != null &&
            long.TryParse(refIdObj.ToString(), out var oldRefId) &&
            oldRefId > 0)
        {
            var refType = outRow.GetValueOrDefault("reference_type")?.ToString();
            var refParent = ResolveTransactionReferenceTargetTable(refType);
            if (refParent != null)
            {
                var resolvedRefId = await ResolvePolymorphicIdAsync(
                    dstConn, tx, refParent, oldRefId, idMap, disabledTargetTables, fallbackCache, cancellationToken);
                outRow["reference_id"] = resolvedRefId.HasValue ? resolvedRefId.Value : null;
            }
        }

        // Polymorphic entity_id translation on document_histories (entity_type; entity_id is NOT NULL)
        if (targetTable.Equals("document_histories", StringComparison.OrdinalIgnoreCase))
        {
            var entityType = (outRow.GetValueOrDefault("entity_type")?.ToString() ?? "").Trim();
            var entityParent = ResolveDocumentHistoryTargetTable(entityType);
            if (entityParent != null)
            {
                var oldEntityId = TryParseLong(outRow.GetValueOrDefault("entity_id"));
                if (!oldEntityId.HasValue || oldEntityId.Value <= 0)
                {
                    return (null, $"Missing polymorphic `entity_id` for `document_histories` (`{entityType}`)");
                }
                var resolvedEntityId = await ResolvePolymorphicIdAsync(
                    dstConn, tx, entityParent, oldEntityId.Value, idMap, disabledTargetTables, fallbackCache, cancellationToken);
                if (!resolvedEntityId.HasValue)
                {
                    return (null, $"Missing or orphaned polymorphic parent reference `entity_id` -> `{entityParent}`");
                }
                outRow["entity_id"] = resolvedEntityId.Value;
            }
        }

        // 4. Table-specific intelligent value derivation & normalization
        ApplyTableSpecificSynthesis(
            targetTable,
            outRow,
            srcRow,
            rowIndex,
            rawSourceId,
            productMetaCache,
            existingProductCodes,
            existingBarcodes,
            naturalKeyIndex);

        // 5. Final schema compliance pass
        foreach (var tc in targetCols)
        {
            var colName = tc.Name;
            if (tc.Pk && colName.Equals("id", StringComparison.OrdinalIgnoreCase)) continue;

            var val = outRow.GetValueOrDefault(colName);
            if (val != null && val.ToString() is { Length: > 0 } strVal && tc.Type.Equals("date", StringComparison.OrdinalIgnoreCase))
            {
                val = NormalizeDateString(strVal);
                outRow[colName] = val;
            }

            if (tc.NotNull && (val == null || (val is string sEmpty && string.IsNullOrWhiteSpace(sEmpty))))
            {
                outRow[colName] = tc.DefaultValue != null
                    ? EvaluateSqliteDefault(tc.DefaultValue, tc.Type)
                    : SynthesizeFallbackForType(colName, tc.Type, rowIndex, rawSourceId);
            }
        }

        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        if (outRow.ContainsKey("created_at") && string.IsNullOrWhiteSpace(outRow["created_at"]?.ToString()))
        {
            outRow["created_at"] = nowStr;
        }
        if (outRow.ContainsKey("updated_at") && string.IsNullOrWhiteSpace(outRow["updated_at"]?.ToString()))
        {
            outRow["updated_at"] = outRow.GetValueOrDefault("created_at") ?? nowStr;
        }

        return (outRow, null);
    }

    private static string? ResolveTradeSchemeTargetTable(string? targetType)
    {
        return (targetType ?? "").Trim().ToLowerInvariant() switch
        {
            "product" or "products" or "app\\models\\product" => "products",
            "category" or "categories" or "app\\models\\category" => "categories",
            "brand" or "brands" or "app\\models\\brand" => "brands",
            _ => null
        };
    }

    private static string? ResolveStocktakeScopeTargetTable(string? scopeType)
    {
        return (scopeType ?? "").Trim().ToLowerInvariant() switch
        {
            "category" or "categories" or "app\\models\\category" => "categories",
            "brand" or "brands" or "app\\models\\brand" => "brands",
            "supplier" or "suppliers" or "app\\models\\supplier" => "suppliers",
            "storage_location" or "storage-location" or "storagelocation" or "storage_locations" or "app\\models\\storagelocation" => "storage_locations",
            _ => null
        };
    }

    private static string? ResolveInvoiceDraftTargetTable(string? postedInvoiceType, string? draftType)
    {
        var pType = (postedInvoiceType ?? "").Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(pType))
        {
            if (pType.Contains("sale", StringComparison.Ordinal)) return "sale_invoices";
            if (pType.Contains("purchase", StringComparison.Ordinal)) return "purchase_invoices";
        }

        var dType = (draftType ?? "").Trim().ToLowerInvariant();
        if (dType.StartsWith("sale", StringComparison.Ordinal) || dType == "quotation") return "sale_invoices";
        if (dType.StartsWith("purchase", StringComparison.Ordinal)) return "purchase_invoices";
        return null;
    }

    private static string? ResolveTransactionReferenceTargetTable(string? referenceType)
    {
        var rt = (referenceType ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(rt) ||
            rt is "manual" or "opening_balance" or "transfer" or "cash_in" or "cash_out" ||
            rt.StartsWith("cash_drawer", StringComparison.Ordinal))
        {
            return null;
        }

        return rt switch
        {
            "sale_invoice" or "sale_invoice_update" or "sale_invoice_deletion" or "sale_invoice_deleted" or "saleinvoice" or "app\\models\\saleinvoice" => "sale_invoices",
            "purchase_invoice" or "purchase_invoice_update" or "purchase_invoice_deletion" or "purchase_invoice_deleted" or "purchaseinvoice" or "app\\models\\purchaseinvoice" => "purchase_invoices",
            "sale_return" or "sale_return_update" or "sale_return_deletion" or "sale_return_deleted" or "salereturn" or "app\\models\\salereturn" => "sale_returns",
            "purchase_return" or "purchase_return_update" or "purchase_return_deletion" or "purchase_return_deleted" or "purchasereturn" or "app\\models\\purchasereturn" => "purchase_returns",
            "expense" or "app\\models\\expense" => "expenses",
            "payment" or "customer_payment" or "supplier_payment" or "app\\models\\payment" => "payments",
            "employee_advance" or "app\\models\\employeeadvance" => "employee_advances",
            "stock_disposal" or "app\\models\\stockdisposal" => "stock_disposals",
            "stock_adjustment" or "app\\models\\stockadjustment" => "stock_adjustments",
            "journal_entry" or "app\\models\\journalentry" => "journal_entries",
            _ => null
        };
    }

    private static string? ResolveDocumentHistoryTargetTable(string? entityType)
    {
        return (entityType ?? "").Trim().ToLowerInvariant() switch
        {
            "app\\models\\saleinvoice" or "saleinvoice" or "sale_invoice" or "sale-invoice" or "sale" => "sale_invoices",
            "app\\models\\purchaseinvoice" or "purchaseinvoice" or "purchase_invoice" or "purchase-invoice" or "purchase" => "purchase_invoices",
            "app\\models\\salereturn" or "salereturn" or "sale_return" or "sale-return" => "sale_returns",
            "app\\models\\purchasereturn" or "purchasereturn" or "purchase_return" or "purchase-return" => "purchase_returns",
            "app\\models\\stockadjustment" or "stockadjustment" or "stock_adjustment" or "stock-adjustment" => "stock_adjustments",
            "app\\models\\stockdisposal" or "stockdisposal" or "stock_disposal" or "stock-disposal" => "stock_disposals",
            "app\\models\\stocktakesession" or "stocktakesession" or "stocktake_session" or "stocktake-session" or "stocktake" => "stocktake_sessions",
            "app\\models\\payment" or "payment" or "customer_payment" or "supplier_payment" or "customer-payment" or "supplier-payment" => "payments",
            "app\\models\\purchaseorder" or "purchaseorder" or "purchase_order" or "purchase-order" => "purchase_orders",
            _ => null
        };
    }

    private static async Task<long?> ResolvePolymorphicIdAsync(
        SqliteConnection dstConn,
        SqliteTransaction tx,
        string parentTable,
        long oldId,
        Dictionary<string, Dictionary<long, long>> idMap,
        HashSet<string> disabledTargetTables,
        FallbackEntityCache cache,
        CancellationToken cancellationToken)
    {
        if (oldId <= 0) return null;

        if (idMap.TryGetValue(parentTable, out var parentMap))
        {
            return parentMap.TryGetValue(oldId, out var mappedId) ? mappedId : null;
        }

        if (disabledTargetTables.Contains(parentTable))
        {
            return null;
        }

        if (!cache.ExistingParentIds.TryGetValue(parentTable, out var existingSet))
        {
            existingSet = new HashSet<long>();
            await using var cmd = dstConn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT id FROM \"{parentTable.Replace("\"", "\"\"")}\";";
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                if (!r.IsDBNull(0)) existingSet.Add(Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture));
            }
            cache.ExistingParentIds[parentTable] = existingSet;
        }

        return existingSet.Contains(oldId) ? oldId : null;
    }

    private async Task<long?> ResolveForeignKeyValueAsync(
        SqliteConnection dstConn,
        SqliteTransaction tx,
        string parentTable,
        string fkCol,
        object? rawFkVal,
        Dictionary<string, object?> srcRow,
        Dictionary<string, Dictionary<long, long>> idMap,
        HashSet<string> disabledTargetTables,
        bool isNullable,
        string? fallbackMode,
        FallbackEntityCache cache,
        CancellationToken cancellationToken)
    {
        var rawStr = rawFkVal?.ToString()?.Trim() ?? "";

        // 1. Non-numeric string directly inside FK column (e.g. "Antibiotics" or "Walk-in" or "GSK")
        if (!string.IsNullOrEmpty(rawStr) && !long.TryParse(rawStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        {
            var resolved = await ResolveOrCreateParentByNameAsync(dstConn, tx, parentTable, rawStr, cache, cancellationToken);
            if (resolved.HasValue) return resolved;
        }

        // 2. Empty / <= 0 FK -> look up companion text column (e.g., CompanyName, PartyName, CategoryName)
        var hasNumericId = long.TryParse(rawStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericFk) && numericFk > 0;
        if (!hasNumericId)
        {
            var textCandidate = ExtractCompanionTextReference(fkCol, srcRow);
            if (!string.IsNullOrEmpty(textCandidate))
            {
                var resolved = await ResolveOrCreateParentByNameAsync(dstConn, tx, parentTable, textCandidate, cache, cancellationToken);
                if (resolved.HasValue) return resolved;
            }
        }

        // 3. Positive numeric FK -> idMap first, then direct parent existence check ONLY when parentTable was neither imported nor disabled
        if (hasNumericId)
        {
            if (idMap.TryGetValue(parentTable, out var parentMap))
            {
                if (parentMap.TryGetValue(numericFk, out var mappedId))
                {
                    return mappedId;
                }
                // Parent table IS part of this import and this parent row was skipped/missing: never fall back to ExistingParentIds!
            }
            else if (!disabledTargetTables.Contains(parentTable))
            {
                if (!cache.ExistingParentIds.TryGetValue(parentTable, out var existingSet))
                {
                    existingSet = new HashSet<long>();
                    await using var cmd = dstConn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = $"SELECT id FROM \"{parentTable.Replace("\"", "\"\"")}\";";
                    await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
                    while (await r.ReadAsync(cancellationToken))
                    {
                        if (!r.IsDBNull(0)) existingSet.Add(Convert.ToInt64(r.GetValue(0), CultureInfo.InvariantCulture));
                    }
                    cache.ExistingParentIds[parentTable] = existingSet;
                }

                if (existingSet.Contains(numericFk))
                {
                    return numericFk;
                }
            }
        }

        // 4. Orphan / missing parent
        if (isNullable && fallbackMode == null)
        {
            return null;
        }

        return fallbackMode switch
        {
            "category" => await ResolveOrCreateParentByNameAsync(dstConn, tx, "categories", "General", cache, cancellationToken),
            "brand" => await ResolveOrCreateParentByNameAsync(dstConn, tx, "brands", "General", cache, cancellationToken),
            "supplier" => await ResolveOrCreateParentByNameAsync(dstConn, tx, "suppliers", "General Supplier", cache, cancellationToken),
            "customer" => await ResolveOrCreateParentByNameAsync(dstConn, tx, "customers", "Walk-in Customer", cache, cancellationToken),
            "expense_category" => await ResolveOrCreateParentByNameAsync(dstConn, tx, "expense_categories", "General Expense", cache, cancellationToken),
            _ => null
        };
    }

    private static string? ExtractCompanionTextReference(string fkCol, Dictionary<string, object?> srcRow)
    {
        var candidates = fkCol.ToLowerInvariant() switch
        {
            "category_id" => new[] { "category", "category_name", "cat_name", "cat" },
            "brand_id" => new[] { "brand", "brand_name", "company", "company_name", "companyname", "manufacturer" },
            "supplier_id" => new[] { "supplier", "supplier_name", "vendor", "vendor_name" },
            "customer_id" => new[] { "customer", "customer_name", "client_name", "patient_name", "partyname", "party_name", "party" },
            "product_id" => new[] { "product_code", "sku", "code", "itemcode", "product_name", "item_name", "itemname" },
            _ => Array.Empty<string>()
        };

        foreach (var cand in candidates)
        {
            if (srcRow.TryGetValue(cand, out var v) && v != null && v is not DBNull)
            {
                var s = v.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        }
        return null;
    }

    private static async Task<long?> ResolveOrCreateParentByNameAsync(
        SqliteConnection dstConn,
        SqliteTransaction tx,
        string parentTable,
        string nameOrCode,
        FallbackEntityCache cache,
        CancellationToken cancellationToken)
    {
        var clean = nameOrCode.Trim();
        if (string.IsNullOrEmpty(clean)) return null;

        if (!cache.ByName.TryGetValue(parentTable, out var byNameMap))
        {
            byNameMap = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            cache.ByName[parentTable] = byNameMap;
        }
        if (byNameMap.TryGetValue(clean, out var cachedId))
        {
            return cachedId;
        }

        if (parentTable.Equals("products", StringComparison.OrdinalIgnoreCase))
        {
            await using var findProd = dstConn.CreateCommand();
            findProd.Transaction = tx;
            findProd.CommandText = "SELECT id FROM products WHERE LOWER(product_code) = LOWER(@k) OR LOWER(name) = LOWER(@k) OR LOWER(barcode) = LOWER(@k) LIMIT 1;";
            findProd.Parameters.AddWithValue("@k", clean);
            var scalar = await findProd.ExecuteScalarAsync(cancellationToken);
            if (scalar != null && scalar is not DBNull)
            {
                var pid = Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
                byNameMap[clean] = pid;
                return pid;
            }
            return null;
        }

        if (parentTable is "categories" or "brands" or "suppliers" or "customers" or "expense_categories")
        {
            var escapedParent = parentTable.Replace("\"", "\"\"");
            await using (var findCmd = dstConn.CreateCommand())
            {
                findCmd.Transaction = tx;
                findCmd.CommandText = $"SELECT id FROM \"{escapedParent}\" WHERE LOWER(name) = LOWER(@name) LIMIT 1;";
                findCmd.Parameters.AddWithValue("@name", clean);
                var existing = await findCmd.ExecuteScalarAsync(cancellationToken);
                if (existing != null && existing is not DBNull)
                {
                    var eid = Convert.ToInt64(existing, CultureInfo.InvariantCulture);
                    byNameMap[clean] = eid;
                    return eid;
                }
            }

            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            await using var insCmd = dstConn.CreateCommand();
            insCmd.Transaction = tx;
            if (parentTable.Equals("expense_categories", StringComparison.OrdinalIgnoreCase))
            {
                var slug = Regex.Replace(clean.ToUpperInvariant(), @"[^A-Z0-9]+", "-").Trim('-');
                if (string.IsNullOrEmpty(slug)) slug = "EXP";
                var code = $"{slug}-{Random.Shared.Next(10, 99)}";
                insCmd.CommandText = $"INSERT INTO \"{escapedParent}\" (name, code, created_at, updated_at) VALUES (@name, @code, @now, @now); SELECT last_insert_rowid();";
                insCmd.Parameters.AddWithValue("@code", code);
            }
            else
            {
                insCmd.CommandText = $"INSERT INTO \"{escapedParent}\" (name, created_at, updated_at) VALUES (@name, @now, @now); SELECT last_insert_rowid();";
            }
            insCmd.Parameters.AddWithValue("@name", clean);
            insCmd.Parameters.AddWithValue("@now", nowStr);

            var newId = Convert.ToInt64(await insCmd.ExecuteScalarAsync(cancellationToken) ?? 0L, CultureInfo.InvariantCulture);
            if (newId > 0)
            {
                byNameMap[clean] = newId;
                if (!cache.ExistingParentIds.TryGetValue(parentTable, out var idSet))
                {
                    idSet = new HashSet<long>();
                    cache.ExistingParentIds[parentTable] = idSet;
                }
                idSet.Add(newId);
                return newId;
            }
        }

        return null;
    }

    private static void ApplyTableSpecificSynthesis(
        string targetTable,
        Dictionary<string, object?> outRow,
        Dictionary<string, object?> srcRow,
        int rowIndex,
        long? rawSourceId,
        Dictionary<long, ProductMeta> productMetaCache,
        Dictionary<string, long> existingProductCodes,
        Dictionary<string, long> existingBarcodes,
        NaturalKeyIndex? naturalKeyIndex = null)
    {
        var idLabel = rawSourceId.HasValue && rawSourceId.Value > 0 ? rawSourceId.Value : rowIndex;
        var lowerTarget = targetTable.ToLowerInvariant();

        if (lowerTarget == "products")
        {
            if (string.IsNullOrWhiteSpace(outRow.GetValueOrDefault("name")?.ToString()))
            {
                outRow["name"] = $"Product {idLabel}";
            }

            var code = (outRow.GetValueOrDefault("product_code")?.ToString() ?? "").Trim();
            var wasCodeBlank = string.IsNullOrEmpty(code);
            if (wasCodeBlank)
            {
                code = $"PRD-{idLabel:D5}";
                var codeSuffix = 1;
                while (existingProductCodes.ContainsKey(code))
                {
                    code = $"PRD-{idLabel:D5}-{codeSuffix++}";
                }
            }
            outRow["product_code"] = code;

            var barcode = (outRow.GetValueOrDefault("barcode")?.ToString() ?? "").Trim();
            var wasBarcodeBlank = string.IsNullOrEmpty(barcode);
            if (wasBarcodeBlank)
            {
                barcode = code;
                if (existingBarcodes.TryGetValue(barcode, out var ownerByBc) &&
                    (!existingProductCodes.TryGetValue(code, out var ownerByCode) || ownerByBc != ownerByCode))
                {
                    var baseBarcode = $"BC-{code}-{idLabel}";
                    barcode = baseBarcode;
                    var bcSuffix = 1;
                    while (existingBarcodes.TryGetValue(barcode, out var syntheticOwner) &&
                           (!existingProductCodes.TryGetValue(code, out var codeOwner) || syntheticOwner != codeOwner))
                    {
                        barcode = $"{baseBarcode}-{bcSuffix++}";
                    }
                }
            }
            outRow["barcode"] = barcode;

            var packSize = TryParseInt(outRow.GetValueOrDefault("pack_size")) is > 0 and var ps ? ps : 1;
            outRow["pack_size"] = packSize;
            outRow["quantity"] = Math.Max(0, (int)Math.Round(TryParseDouble(outRow.GetValueOrDefault("quantity")) ?? 0.0));

            var upp = TryParseDouble(outRow.GetValueOrDefault("unit_purchase_price"));
            var ppp = TryParseDouble(outRow.GetValueOrDefault("pack_purchase_price"));
            var usp = TryParseDouble(outRow.GetValueOrDefault("unit_sale_price"));
            var psp = TryParseDouble(outRow.GetValueOrDefault("pack_sale_price"));

            if (upp == null && ppp != null) upp = Math.Round(ppp.Value / packSize, 4);
            if (ppp == null && upp != null) ppp = Math.Round(upp.Value * packSize, 4);
            if (usp == null && psp != null) usp = Math.Round(psp.Value / packSize, 4);
            if (psp == null && usp != null) psp = Math.Round(usp.Value * packSize, 4);

            var finalUpp = upp ?? 0.0;
            var finalPpp = ppp ?? 0.0;
            var finalUsp = usp ?? finalUpp;
            var finalPsp = psp ?? (finalUsp * packSize);
            var avg = TryParseDouble(outRow.GetValueOrDefault("avg_price")) ?? finalUpp;
            var margin = TryParseDouble(outRow.GetValueOrDefault("margin"))
                ?? (finalUsp > 0 ? Math.Round(((finalUsp - avg) / finalUsp) * 100.0, 2) : 0.0);

            outRow["unit_purchase_price"] = finalUpp;
            outRow["pack_purchase_price"] = finalPpp;
            outRow["unit_sale_price"] = finalUsp;
            outRow["pack_sale_price"] = finalPsp;
            outRow["avg_price"] = avg;
            outRow["margin"] = margin;

            var narcRaw = (outRow.GetValueOrDefault("narcotic")?.ToString() ?? "no").Trim().ToLowerInvariant();
            outRow["narcotic"] = narcRaw is "1" or "yes" or "y" or "true" or "t" ? "yes" : "no";
            return;
        }

        if (lowerTarget == "batches")
        {
            if (string.IsNullOrWhiteSpace(outRow.GetValueOrDefault("batch_number")?.ToString()))
            {
                outRow["batch_number"] = $"BATCH-{idLabel:D4}";
            }
            if (string.IsNullOrWhiteSpace(outRow.GetValueOrDefault("expiry_date")?.ToString()))
            {
                outRow["expiry_date"] = DateTime.UtcNow.Date.AddYears(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                outRow["expiry_date"] = NormalizeDateString(outRow["expiry_date"]!.ToString());
            }
            outRow["quantity"] = Math.Max(0, (int)Math.Round(TryParseDouble(outRow.GetValueOrDefault("quantity")) ?? 0.0));
            return;
        }

        if (lowerTarget == "purchase_invoices")
        {
            if (string.IsNullOrWhiteSpace(outRow.GetValueOrDefault("posted_number")?.ToString()))
            {
                var candidate = $"PI-{idLabel:D5}";
                var existingPosted = naturalKeyIndex?.SingleColumns.GetValueOrDefault("posted_number");
                var suffix = 1;
                while (existingPosted != null && existingPosted.ContainsKey(candidate))
                {
                    candidate = $"PI-{idLabel:D5}-{suffix++}";
                }
                outRow["posted_number"] = candidate;
            }
            if (string.IsNullOrWhiteSpace(outRow.GetValueOrDefault("invoice_number")?.ToString()))
            {
                outRow["invoice_number"] = outRow["posted_number"];
            }
            if (string.IsNullOrWhiteSpace(outRow.GetValueOrDefault("posted_date")?.ToString()))
            {
                var created = srcRow.GetValueOrDefault("created_at")?.ToString();
                outRow["posted_date"] = !string.IsNullOrWhiteSpace(created) && created.Length >= 10
                    ? created[..10]
                    : DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            var totalAmt = TryParseDouble(outRow.GetValueOrDefault("total_amount"));
            var invAmt = TryParseDouble(outRow.GetValueOrDefault("invoice_amount"));
            outRow["total_amount"] = totalAmt ?? invAmt ?? 0.0;
            outRow["invoice_amount"] = invAmt ?? totalAmt ?? 0.0;
            return;
        }

        if (lowerTarget == "purchase_invoice_items")
        {
            var prodId = TryParseLong(outRow.GetValueOrDefault("product_id")) ?? 0;
            productMetaCache.TryGetValue(prodId, out var pm);
            var packSize = TryParseInt(outRow.GetValueOrDefault("pack_size")) is > 0 and var ps
                ? ps
                : (pm?.PackSize > 0 ? pm.PackSize : 1);
            outRow["pack_size"] = packSize;

            var pQty = TryParseInt(outRow.GetValueOrDefault("pack_quantity"));
            var uQty = TryParseInt(outRow.GetValueOrDefault("unit_quantity"));
            var tQty = TryParseInt(outRow.GetValueOrDefault("quantity"));

            if (tQty.HasValue && !pQty.HasValue && !uQty.HasValue)
            {
                outRow["pack_quantity"] = packSize > 1 ? tQty.Value / packSize : 0;
                outRow["unit_quantity"] = packSize > 1 ? tQty.Value % packSize : tQty.Value;
                outRow["quantity"] = tQty.Value;
            }
            else
            {
                var fp = pQty ?? 0;
                var fu = uQty ?? 0;
                var ft = tQty ?? ((fp * packSize) + fu);
                outRow["pack_quantity"] = fp;
                outRow["unit_quantity"] = fu;
                outRow["quantity"] = ft;
            }

            var upp = TryParseDouble(outRow.GetValueOrDefault("unit_purchase_price"));
            var ppp = TryParseDouble(outRow.GetValueOrDefault("pack_purchase_price"));
            if (upp == null && ppp != null) upp = Math.Round(ppp.Value / packSize, 4);
            upp ??= pm?.UnitPurchasePrice ?? 0.0;
            ppp ??= Math.Round(upp.Value * packSize, 4);

            var usp = TryParseDouble(outRow.GetValueOrDefault("unit_sale_price"));
            var psp = TryParseDouble(outRow.GetValueOrDefault("pack_sale_price"));
            if (usp == null && psp != null) usp = Math.Round(psp.Value / packSize, 4);
            usp ??= pm?.UnitSalePrice ?? upp.Value;
            psp ??= Math.Round(usp.Value * packSize, 4);

            var avg = TryParseDouble(outRow.GetValueOrDefault("avg_price")) ?? upp.Value;
            var margin = TryParseDouble(outRow.GetValueOrDefault("margin"))
                ?? (usp.Value > 0 ? Math.Round(((usp.Value - upp.Value) / usp.Value) * 100.0, 2) : 0.0);
            var finalQty = Convert.ToInt32(outRow["quantity"] ?? 0, CultureInfo.InvariantCulture);
            var subTotal = TryParseDouble(outRow.GetValueOrDefault("sub_total")) ?? Math.Round(finalQty * upp.Value, 2);

            outRow["unit_purchase_price"] = upp.Value;
            outRow["pack_purchase_price"] = ppp.Value;
            outRow["unit_sale_price"] = usp.Value;
            outRow["pack_sale_price"] = psp.Value;
            outRow["avg_price"] = avg;
            outRow["margin"] = margin;
            outRow["sub_total"] = subTotal;
            return;
        }

        if (lowerTarget == "sale_invoices")
        {
            if (string.IsNullOrWhiteSpace(outRow.GetValueOrDefault("posted_number")?.ToString()))
            {
                var candidate = $"SI-{idLabel:D5}";
                var existingPosted = naturalKeyIndex?.SingleColumns.GetValueOrDefault("posted_number");
                var suffix = 1;
                while (existingPosted != null && existingPosted.ContainsKey(candidate))
                {
                    candidate = $"SI-{idLabel:D5}-{suffix++}";
                }
                outRow["posted_number"] = candidate;
            }
            if (string.IsNullOrWhiteSpace(outRow.GetValueOrDefault("date")?.ToString()))
            {
                var created = srcRow.GetValueOrDefault("created_at")?.ToString();
                outRow["date"] = !string.IsNullOrWhiteSpace(created) && created.Length >= 10
                    ? created[..10]
                    : DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            var total = TryParseDouble(outRow.GetValueOrDefault("total"));
            var gross = TryParseDouble(outRow.GetValueOrDefault("gross_amount"));
            outRow["total"] = total ?? gross ?? 0.0;
            outRow["gross_amount"] = gross ?? total ?? 0.0;
            return;
        }

        if (lowerTarget == "sale_invoice_items")
        {
            var prodId = TryParseLong(outRow.GetValueOrDefault("product_id")) ?? 0;
            productMetaCache.TryGetValue(prodId, out var pm);
            var packSize = TryParseInt(outRow.GetValueOrDefault("pack_size")) is > 0 and var ps
                ? ps
                : (pm?.PackSize > 0 ? pm.PackSize : 1);
            outRow["pack_size"] = packSize;

            var qty = TryParseInt(outRow.GetValueOrDefault("quantity"))
                ?? (int)Math.Round(((TryParseDouble(outRow.GetValueOrDefault("pack_quantity")) ?? 0.0) * packSize) +
                                    (TryParseDouble(outRow.GetValueOrDefault("unit_quantity")) ?? 1.0));
            if (qty <= 0) qty = 1;
            outRow["quantity"] = qty;

            var price = TryParseDouble(outRow.GetValueOrDefault("price")) ?? pm?.UnitSalePrice ?? 0.0;
            var subTotal = TryParseDouble(outRow.GetValueOrDefault("sub_total")) ?? Math.Round(qty * price, 2);
            outRow["price"] = price;
            outRow["sub_total"] = subTotal;
            return;
        }

        if (lowerTarget == "sale_return_items")
        {
            var qty = TryParseInt(outRow.GetValueOrDefault("unit_return_quantity"))
                ?? TryParseInt(srcRow.GetValueOrDefault("quantity"))
                ?? 1;
            var price = TryParseDouble(outRow.GetValueOrDefault("unit_sale_price"))
                ?? TryParseDouble(srcRow.GetValueOrDefault("price"))
                ?? 0.0;
            var subTotal = TryParseDouble(outRow.GetValueOrDefault("sub_total")) ?? Math.Round(qty * price, 2);
            outRow["unit_return_quantity"] = qty;
            outRow["unit_sale_price"] = price;
            outRow["sub_total"] = subTotal;
            return;
        }

        if (lowerTarget is "customer_ledgers" or "supplier_ledgers")
        {
            if (string.IsNullOrWhiteSpace(outRow.GetValueOrDefault("entry_date")?.ToString()))
            {
                outRow["entry_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
            else
            {
                outRow["entry_date"] = NormalizeDateString(outRow["entry_date"]!.ToString());
            }

            var invTotal = TryParseDouble(outRow.GetValueOrDefault("invoice_total")) ?? 0.0;
            var totRec = TryParseDouble(outRow.GetValueOrDefault(lowerTarget == "customer_ledgers" ? "total_received" : "total_paid")) ?? 0.0;
            var remainingCol = lowerTarget == "customer_ledgers" ? "balance_remaining" : "credit_remaining";
            if (outRow.ContainsKey(remainingCol) && TryParseDouble(outRow.GetValueOrDefault(remainingCol)) == null)
            {
                var remaining = lowerTarget == "supplier_ledgers"
                    ? Math.Max(0.0, invTotal - totRec)
                    : (invTotal - totRec);
                outRow[remainingCol] = Math.Round(remaining, 2);
            }

            var entryType = (outRow.GetValueOrDefault("entry_type")?.ToString() ?? "").Trim().ToLowerInvariant();
            if (entryType is not ("invoice" or "payment" or "manual"))
            {
                var hasInv = (lowerTarget == "customer_ledgers" ? TryParseLong(outRow.GetValueOrDefault("sale_invoice_id")) : TryParseLong(outRow.GetValueOrDefault("purchase_invoice_id"))) > 0;
                var hasPay = TryParseLong(outRow.GetValueOrDefault("payment_id")) > 0;
                outRow["entry_type"] = hasInv ? "invoice" : (hasPay ? "payment" : "manual");
            }
            if (outRow.ContainsKey("is_manual") && outRow["is_manual"] == null)
            {
                outRow["is_manual"] = string.Equals(outRow["entry_type"]?.ToString(), "manual", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }
            return;
        }

        if (lowerTarget == "formulations")
        {
            var name = outRow.GetValueOrDefault("name")?.ToString();
            if (!string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(outRow.GetValueOrDefault("normalized_name")?.ToString()))
            {
                outRow["normalized_name"] = Formulation.NormalizeKey(name);
            }
            return;
        }

        if (lowerTarget == "employees")
        {
            if (string.IsNullOrWhiteSpace(outRow.GetValueOrDefault("employee_code")?.ToString()))
            {
                var candidate = $"EMP-{idLabel:D4}";
                var existingCodes = naturalKeyIndex?.SingleColumns.GetValueOrDefault("employee_code");
                var suffix = 1;
                while (existingCodes != null && existingCodes.ContainsKey(candidate))
                {
                    candidate = $"EMP-{idLabel:D4}-{suffix++}";
                }
                outRow["employee_code"] = candidate;
            }
            return;
        }

        if (lowerTarget is "storage_locations" or "tax_rates" or "chart_of_accounts")
        {
            var name = outRow.GetValueOrDefault("name")?.ToString();
            if (string.IsNullOrWhiteSpace(outRow.GetValueOrDefault("code")?.ToString()) && !string.IsNullOrWhiteSpace(name))
            {
                var slug = Regex.Replace(name.ToUpperInvariant(), @"[^A-Z0-9]+", "-").Trim('-');
                outRow["code"] = $"{(string.IsNullOrEmpty(slug) ? "CODE" : slug)}-{idLabel}";
            }
        }
    }

    private Dictionary<string, object?>? PreviewSynthesizedRow(
        string targetTable,
        List<TargetColumnSchema> targetCols,
        Dictionary<string, object?> srcRow,
        Dictionary<string, string> columnMap,
        Dictionary<string, string> staticMap,
        int rowIndex)
    {
        var rawSourceId = srcRow.TryGetValue("id", out var idObj) && idObj != null && long.TryParse(idObj.ToString(), out var pId)
            ? pId
            : rowIndex;

        var outRow = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var tc in targetCols)
        {
            var colName = tc.Name;
            object? val = null;
            if (columnMap.TryGetValue(colName, out var sCol))
            {
                if (!string.Equals(sCol, "__skip__", StringComparison.OrdinalIgnoreCase) &&
                    srcRow.TryGetValue(sCol, out var sv))
                {
                    val = sv;
                }
            }
            else if (srcRow.TryGetValue(colName, out var dv))
            {
                val = dv;
            }
            if ((val == null || val is DBNull || (val is string s0 && string.IsNullOrWhiteSpace(s0))) &&
                staticMap.TryGetValue(colName, out var stVal))
            {
                val = stVal;
            }
            outRow[colName] = val is DBNull ? null : val;
        }

        ApplyTableSpecificSynthesis(
            targetTable,
            outRow,
            srcRow,
            rowIndex,
            rawSourceId,
            new Dictionary<long, ProductMeta>(),
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));

        foreach (var tc in targetCols)
        {
            var colName = tc.Name;
            if (tc.Pk && colName.Equals("id", StringComparison.OrdinalIgnoreCase)) continue;
            var cur = outRow.GetValueOrDefault(colName);
            if (tc.NotNull && (cur == null || (cur is string s && string.IsNullOrWhiteSpace(s))))
            {
                outRow[colName] = tc.DefaultValue != null
                    ? EvaluateSqliteDefault(tc.DefaultValue, tc.Type)
                    : SynthesizeFallbackForType(colName, tc.Type, rowIndex, rawSourceId);
            }
        }

        return outRow;
    }

    private static string[] GetSingleUniqueColumns(string targetTable) => targetTable.ToLowerInvariant() switch
    {
        "categories" or "brands" or "suppliers" or "accounts" => ["name"],
        "expense_categories" => ["name", "code"],
        "customers" => ["email", "name"],
        "formulations" => ["normalized_name"],
        "storage_locations" or "tax_rates" => ["code"],
        "chart_of_accounts" => ["code", "system_key"],
        "employees" => ["employee_code", "pin_code"],
        "purchase_invoices" => ["posted_number", "receipt_key"],
        "purchase_orders" => ["number"],
        "purchase_returns" or "sale_invoices" or "sale_returns" or "stock_adjustments" => ["posted_number"],
        "stock_disposals" => ["disposal_number"],
        "stocktake_sessions" => ["reference_no"],
        "payments" => ["payment_number", "idempotency_key"],
        "expenses" => ["expense_number"],
        "employee_advances" => ["advance_number"],
        "payroll_runs" => ["run_number", "period_month"],
        "journal_entries" => ["entry_number"],
        "document_sequences" => ["sequence_key"],
        "invoice_drafts" => ["uuid"],
        "terminals" => ["terminal_uid"],
        _ => []
    };

    private static string[]? GetCompositeUniqueColumns(string targetTable) => targetTable.ToLowerInvariant() switch
    {
        "product_suppliers" => ["product_id", "supplier_id"],
        "supplier_product_positions" => ["supplier_id", "product_id"],
        "customer_regular_items" => ["customer_id", "product_id"],
        "customer_whole_sale_prices" => ["customer_id", "product_id"],
        "customer_ledgers" => ["customer_id", "sale_invoice_id"],
        "supplier_ledgers" => ["supplier_id", "purchase_invoice_id"],
        "employee_attendances" => ["employee_id", "date"],
        "payroll_items" => ["payroll_run_id", "employee_id"],
        "purchase_order_items" => ["purchase_order_id", "product_id"],
        "batches" => ["product_id", "batch_number", "expiry_date"],
        "purchase_invoices" => ["supplier_id", "invoice_number"],
        _ => null
    };

    private static string? TryBuildCompositeKey(string[] compositeCols, Func<string, object?> valueGetter)
    {
        var parts = new string[compositeCols.Length];
        for (var i = 0; i < compositeCols.Length; i++)
        {
            var raw = valueGetter(compositeCols[i]);
            if (raw == null || raw is DBNull) return null;
            var str = raw.ToString()?.Trim() ?? "";
            if (string.IsNullOrEmpty(str)) return null;
            parts[i] = str.ToLowerInvariant();
        }
        return string.Join('|', parts);
    }

    private static async Task<NaturalKeyIndex> PreloadNaturalKeyIndexAsync(
        SqliteConnection dstConn,
        SqliteTransaction tx,
        string targetTable,
        List<TargetColumnSchema> targetCols,
        CancellationToken cancellationToken)
    {
        var index = new NaturalKeyIndex();
        var colSet = targetCols.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var escapedDst = targetTable.Replace("\"", "\"\"");

        foreach (var singleCol in GetSingleUniqueColumns(targetTable))
        {
            if (!colSet.Contains(singleCol)) continue;
            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            index.SingleColumns[singleCol] = map;

            await using var cmd = dstConn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT id, \"{singleCol}\" FROM \"{escapedDst}\";";
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                if (r.IsDBNull(0) || r.IsDBNull(1)) continue;
                var v = r[1]?.ToString()?.Trim() ?? "";
                if (!string.IsNullOrEmpty(v))
                {
                    map[v] = Convert.ToInt64(r[0], CultureInfo.InvariantCulture);
                }
            }
        }

        var compositeCols = GetCompositeUniqueColumns(targetTable);
        if (compositeCols != null && compositeCols.All(c => colSet.Contains(c)))
        {
            var colSelect = string.Join(", ", compositeCols.Select(c => $"\"{c}\""));
            await using var cmd = dstConn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"SELECT id, {colSelect} FROM \"{escapedDst}\";";
            await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await r.ReadAsync(cancellationToken))
            {
                if (r.IsDBNull(0)) continue;
                var id = Convert.ToInt64(r["id"], CultureInfo.InvariantCulture);
                var parts = new string[compositeCols.Length];
                var allPresent = true;
                for (var i = 0; i < compositeCols.Length; i++)
                {
                    if (r.IsDBNull(i + 1)) { allPresent = false; break; }
                    var partStr = r[i + 1]?.ToString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(partStr)) { allPresent = false; break; }
                    parts[i] = partStr.ToLowerInvariant();
                }
                if (allPresent)
                {
                    index.Composite[string.Join('|', parts)] = id;
                }
            }
        }

        return index;
    }

    private static long? FindExistingIdByNaturalKey(
        string targetTable,
        Dictionary<string, object?> rowPayload,
        NaturalKeyIndex index,
        Dictionary<string, long> existingProductCodes,
        Dictionary<string, long> existingBarcodes)
    {
        if (targetTable.Equals("products", StringComparison.OrdinalIgnoreCase))
        {
            var code = (rowPayload.GetValueOrDefault("product_code")?.ToString() ?? "").Trim();
            if (!string.IsNullOrEmpty(code) && existingProductCodes.TryGetValue(code, out var idByCode)) return idByCode;
            var bc = (rowPayload.GetValueOrDefault("barcode")?.ToString() ?? "").Trim();
            if (!string.IsNullOrEmpty(bc) && existingBarcodes.TryGetValue(bc, out var idByBc)) return idByBc;
            return null;
        }

        foreach (var singleCol in GetSingleUniqueColumns(targetTable))
        {
            if (!index.SingleColumns.TryGetValue(singleCol, out var map)) continue;
            var v = (rowPayload.GetValueOrDefault(singleCol)?.ToString() ?? "").Trim();
            if (!string.IsNullOrEmpty(v) && map.TryGetValue(v, out var idSingle)) return idSingle;
        }

        var compositeCols = GetCompositeUniqueColumns(targetTable);
        if (compositeCols != null)
        {
            var key = TryBuildCompositeKey(compositeCols, c => rowPayload.GetValueOrDefault(c));
            if (key != null && index.Composite.TryGetValue(key, out var idComp)) return idComp;
        }

        return null;
    }

    private static bool HasCrossFieldUniqueCollision(
        string targetTable,
        long matchedTargetId,
        Dictionary<string, object?> updatePayload,
        NaturalKeyIndex index,
        Dictionary<string, long> existingProductCodes,
        Dictionary<string, long> existingBarcodes,
        out string reason)
    {
        reason = "";

        if (targetTable.Equals("products", StringComparison.OrdinalIgnoreCase))
        {
            var code = (updatePayload.GetValueOrDefault("product_code")?.ToString() ?? "").Trim();
            if (!string.IsNullOrEmpty(code) && existingProductCodes.TryGetValue(code, out var codeOwner) && codeOwner != matchedTargetId)
            {
                reason = $"Cross-field unique collision on products.product_code ('{code}' already belongs to record #{codeOwner})";
                return true;
            }
            var bc = (updatePayload.GetValueOrDefault("barcode")?.ToString() ?? "").Trim();
            if (!string.IsNullOrEmpty(bc) && existingBarcodes.TryGetValue(bc, out var bcOwner) && bcOwner != matchedTargetId)
            {
                reason = $"Cross-field unique collision on products.barcode ('{bc}' already belongs to record #{bcOwner})";
                return true;
            }
            return false;
        }

        foreach (var singleCol in GetSingleUniqueColumns(targetTable))
        {
            if (!index.SingleColumns.TryGetValue(singleCol, out var map)) continue;
            var v = (updatePayload.GetValueOrDefault(singleCol)?.ToString() ?? "").Trim();
            if (!string.IsNullOrEmpty(v) && map.TryGetValue(v, out var ownerId) && ownerId != matchedTargetId)
            {
                reason = $"Cross-field unique collision on {targetTable}.{singleCol} ('{v}' already belongs to record #{ownerId})";
                return true;
            }
        }

        var compositeCols = GetCompositeUniqueColumns(targetTable);
        if (compositeCols != null)
        {
            var key = TryBuildCompositeKey(compositeCols, c => updatePayload.GetValueOrDefault(c));
            if (key != null && index.Composite.TryGetValue(key, out var compOwner) && compOwner != matchedTargetId)
            {
                reason = $"Cross-field unique collision on {targetTable} ({string.Join(", ", compositeCols)} already belongs to record #{compOwner})";
                return true;
            }
        }

        return false;
    }

    private static void EvictKeysForId(Dictionary<string, long> map, long targetId)
    {
        List<string>? toRemove = null;
        foreach (var kv in map)
        {
            if (kv.Value == targetId)
            {
                toRemove ??= new List<string>();
                toRemove.Add(kv.Key);
            }
        }
        if (toRemove != null)
        {
            foreach (var key in toRemove)
            {
                map.Remove(key);
            }
        }
    }

    private static void EvictFromNaturalKeyIndex(
        string targetTable,
        long existingId,
        NaturalKeyIndex index,
        Dictionary<string, long> existingProductCodes,
        Dictionary<string, long> existingBarcodes)
    {
        if (targetTable.Equals("products", StringComparison.OrdinalIgnoreCase))
        {
            EvictKeysForId(existingProductCodes, existingId);
            EvictKeysForId(existingBarcodes, existingId);
        }

        foreach (var map in index.SingleColumns.Values)
        {
            EvictKeysForId(map, existingId);
        }

        EvictKeysForId(index.Composite, existingId);
    }

    private static void RegisterInNaturalKeyIndex(
        string targetTable,
        long newId,
        Dictionary<string, object?> rowPayload,
        NaturalKeyIndex index,
        Dictionary<string, long> existingProductCodes,
        Dictionary<string, long> existingBarcodes)
    {
        if (targetTable.Equals("products", StringComparison.OrdinalIgnoreCase))
        {
            var code = (rowPayload.GetValueOrDefault("product_code")?.ToString() ?? "").Trim();
            var bc = (rowPayload.GetValueOrDefault("barcode")?.ToString() ?? "").Trim();
            if (!string.IsNullOrEmpty(code)) existingProductCodes[code] = newId;
            if (!string.IsNullOrEmpty(bc)) existingBarcodes[bc] = newId;
            return;
        }

        foreach (var singleCol in GetSingleUniqueColumns(targetTable))
        {
            var v = (rowPayload.GetValueOrDefault(singleCol)?.ToString() ?? "").Trim();
            if (string.IsNullOrEmpty(v)) continue;
            if (!index.SingleColumns.TryGetValue(singleCol, out var map))
            {
                map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                index.SingleColumns[singleCol] = map;
            }
            map[v] = newId;
        }

        var compositeCols = GetCompositeUniqueColumns(targetTable);
        if (compositeCols != null)
        {
            var key = TryBuildCompositeKey(compositeCols, c => rowPayload.GetValueOrDefault(c));
            if (key != null) index.Composite[key] = newId;
        }
    }

    private static async Task<bool> ImportSettingsRowAsync(
        SqliteConnection dstConn,
        SqliteTransaction tx,
        Dictionary<string, object?> srcRow,
        List<TargetColumnSchema> targetCols,
        Dictionary<string, string> columnMap,
        Dictionary<string, string> staticMap,
        CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var tc in targetCols)
        {
            var col = tc.Name;
            if (ExcludedSettingsColumns.Contains(col, StringComparer.OrdinalIgnoreCase)) continue;

            object? val = null;
            var srcCol = columnMap.TryGetValue(col, out var mapped) ? mapped : col;
            if (!string.Equals(srcCol, "__skip__", StringComparison.OrdinalIgnoreCase) &&
                srcRow.TryGetValue(srcCol, out var v) &&
                v != null &&
                v is not DBNull &&
                !string.IsNullOrWhiteSpace(v.ToString()))
            {
                val = v;
            }

            if (val == null &&
                staticMap.TryGetValue(col, out var staticVal) &&
                !string.IsNullOrWhiteSpace(staticVal))
            {
                val = staticVal;
            }

            if (val != null)
            {
                payload[col] = val;
            }
        }

        if (payload.Count == 0) return false;

        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        await using (var chk = dstConn.CreateCommand())
        {
            chk.Transaction = tx;
            chk.CommandText = "SELECT COUNT(*) FROM settings WHERE id = 1;";
            var exists = Convert.ToInt32(await chk.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture) > 0;
            if (!exists)
            {
                await using var ins = dstConn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO settings (id, created_at, updated_at) VALUES (1, @now, @now);";
                ins.Parameters.AddWithValue("@now", nowStr);
                await ins.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        payload["updated_at"] = nowStr;
        var setParts = new List<string>();
        await using var upd = dstConn.CreateCommand();
        upd.Transaction = tx;
        var idx = 0;
        foreach (var kv in payload)
        {
            var escCol = kv.Key.Replace("\"", "\"\"");
            setParts.Add($"\"{escCol}\" = @s{idx}");
            upd.Parameters.AddWithValue($"@s{idx}", kv.Value ?? DBNull.Value);
            idx++;
        }
        upd.CommandText = $"UPDATE settings SET {string.Join(", ", setParts)} WHERE id = 1;";
        await upd.ExecuteNonQueryAsync(cancellationToken);
        return true;
    }

    private static async Task<bool> HasTableAsync(
        SqliteConnection conn,
        string tableName,
        SqliteTransaction? tx,
        CancellationToken cancellationToken)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND LOWER(name)=LOWER(@name);";
        cmd.Parameters.AddWithValue("@name", tableName);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken) ?? 0, CultureInfo.InvariantCulture) > 0;
    }

    private static string GetProtectedTableReason(string tableName) => tableName switch
    {
        "users" or "password_reset_tokens" or "personal_access_tokens" or "sessions" =>
            "Skipped to preserve current user accounts, passwords & active login sessions.",
        "licenses" =>
            "Skipped to protect this machine's cryptographic software license binding.",
        "roles" or "permissions" or "model_has_roles" or "model_has_permissions" or "role_has_permissions" =>
            "Skipped to preserve current RBAC roles and granular system permissions.",
        "migrations" =>
            "Skipped to preserve current application database schema migration version.",
        _ =>
            "Skipped system runtime & infrastructure table."
    };

    private static object EvaluateSqliteDefault(string rawDefault, string colType)
    {
        var str = rawDefault.Trim();
        var upper = str.ToUpperInvariant();
        if (upper == "NULL") return SynthesizeFallbackForType("", colType, 1, 1);
        if (upper is "CURRENT_TIMESTAMP" or "NOW()" or "(DATETIME('NOW'))")
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        if (upper is "CURRENT_DATE" or "(DATE('NOW'))")
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        if ((str.StartsWith('\'') && str.EndsWith('\'')) || (str.StartsWith('"') && str.EndsWith('"')))
        {
            str = str[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }
        if (long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var iVal)) return iVal;
        if (double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out var dVal)) return dVal;
        return str;
    }

    private static object SynthesizeFallbackForType(string colName, string colType, int rowIndex, long? rawSourceId)
    {
        var lowerType = colType.ToLowerInvariant();
        var lowerCol = colName.ToLowerInvariant();
        if (lowerCol == "uuid") return Guid.NewGuid().ToString("D");
        if (lowerType.Contains("int", StringComparison.Ordinal) ||
            lowerType.Contains("numeric", StringComparison.Ordinal) ||
            lowerType.Contains("decimal", StringComparison.Ordinal) ||
            lowerType.Contains("real", StringComparison.Ordinal) ||
            lowerType.Contains("float", StringComparison.Ordinal))
        {
            return 0;
        }
        if (lowerType == "date" || lowerCol.EndsWith("_date", StringComparison.Ordinal))
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
        if (lowerType.Contains("datetime", StringComparison.Ordinal) ||
            lowerType.Contains("timestamp", StringComparison.Ordinal) ||
            lowerCol.EndsWith("_at", StringComparison.Ordinal))
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }
        if (lowerCol.Contains("number", StringComparison.Ordinal) ||
            lowerCol.Contains("code", StringComparison.Ordinal) ||
            lowerCol.EndsWith("_no", StringComparison.Ordinal))
        {
            return $"IMP-{(rawSourceId ?? rowIndex):D5}";
        }
        return "-";
    }

    private static int? TryParseInt(object? val)
    {
        if (val == null || val is DBNull) return null;
        if (double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
        {
            return (int)Math.Round(d);
        }
        return null;
    }

    private static long? TryParseLong(object? val)
    {
        if (val == null || val is DBNull) return null;
        if (long.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var l))
        {
            return l;
        }
        return null;
    }

    private static double? TryParseDouble(object? val)
    {
        if (val == null || val is DBNull) return null;
        if (double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
        {
            return d;
        }
        return null;
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

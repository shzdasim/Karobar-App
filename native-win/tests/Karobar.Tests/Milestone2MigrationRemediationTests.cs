using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Karobar.Api.Controllers;
using Karobar.Background;
using Karobar.Data;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Karobar.Tests;

public class Milestone2MigrationRemediationTests
{
    private sealed class TempImportSandbox : IAsyncDisposable
    {
        public string TempRoot { get; }
        public string TargetDbPath { get; }
        public KarobarDb Db { get; }
        private readonly string? _prevRoot;
        private readonly string? _prevAppRoot;

        private TempImportSandbox(string tempRoot, string targetDbPath, KarobarDb db, string? prevRoot, string? prevAppRoot)
        {
            TempRoot = tempRoot;
            TargetDbPath = targetDbPath;
            Db = db;
            _prevRoot = prevRoot;
            _prevAppRoot = prevAppRoot;
        }

        public static async Task<TempImportSandbox> CreateAsync()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_m2_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "backups"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "imports", "sqlite"));

            var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
            var prevAppRoot = Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
            Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", tempRoot);

            var targetDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: targetDbPath)).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var db = new KarobarDb(explicitSqlitePath: targetDbPath);
            await SeedAdminAsync(db);
            return new TempImportSandbox(tempRoot, targetDbPath, db, prevRoot, prevAppRoot);
        }

        public ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", _prevRoot);
            Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", _prevAppRoot);
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(TempRoot, recursive: true); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private static async Task SeedAdminAsync(KarobarDb db)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync("""
            INSERT OR IGNORE INTO roles (id, name, guard_name) VALUES (1, 'Admin', 'sanctum');
            INSERT OR IGNORE INTO users (id, name, email, password, status)
                VALUES (1, 'Admin', 'admin@karobar.local', '$2y$10$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi', 'active');
            INSERT OR IGNORE INTO model_has_roles (role_id, model_type, model_id)
                VALUES (1, 'App\Models\User', 1);
            """);
    }

    private static TController AttachAdminContext<TController>(TController controller, long userId = 1, string? jsonBody = null)
        where TController : ControllerBase
    {
        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        ], "Sanctum");
        httpContext.User = new ClaimsPrincipal(identity);
        if (jsonBody != null)
        {
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(jsonBody));
        }
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static async Task<string> UnlockImportAsync(KarobarDb db)
    {
        var unlockPayload = JsonSerializer.Serialize(new { password = "password" });
        var unlockCtrl = AttachAdminContext(new SqliteImportController(db), 1, unlockPayload);
        var unlockRes = await unlockCtrl.Unlock();
        var unlockOk = unlockRes.Should().BeOfType<OkObjectResult>().Subject;
        var unlockDict = unlockOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
        return unlockDict["unlock_token"]?.ToString() ?? "";
    }

    [Fact]
    public async Task M2_HIGH2_MySqlDialect_SplitsEscapedQuotesAndTranslatesDdl_AndPreservesTSqlBackslash()
    {
        await using var sb = await TempImportSandbox.CreateAsync();
        var engine = new WaseelaAndSqliteImportEngine(sb.TempRoot);

        // 1. Full MySQL dump with escaped quotes + semicolons inside strings, # comments, backticks, AUTO_INCREMENT, ENGINE=, UNSIGNED, CHARSET/COLLATE, COMMENT, KEY, ENUM, INT(11)
        var mysqlDumpPath = Path.Combine(sb.TempRoot, "mysql_pharmacy.sql");
        var mysqlDump = """
            -- MySQL dump 10.13  Distrib 8.0.36, for Win64 (x86_64)
            /*!40101 SET NAMES utf8mb4 */;
            # Hash comment containing ; semicolon and 'quote
            LOCK TABLES `products` WRITE;
            /*!40000 ALTER TABLE `products` DISABLE KEYS */;
            CREATE TABLE `products` (
              `id` int(11) unsigned NOT NULL AUTO_INCREMENT COMMENT 'Primary; key',
              `product_code` varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci NOT NULL,
              `name` varchar(255) COLLATE utf8mb4_unicode_ci NOT NULL COMMENT 'Item; label',
              `status` enum('active','inactive') NOT NULL DEFAULT 'active',
              `quantity` int(11) NOT NULL DEFAULT 0,
              `updated_at` datetime DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
              PRIMARY KEY (`id`),
              UNIQUE KEY `products_code_unique` (`product_code`),
              KEY `idx_name` (`name`(100)),
              FULLTEXT KEY `ft_name` (`name`)
            ) ENGINE=InnoDB AUTO_INCREMENT=10 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci COMMENT='Store; items';
            INSERT INTO `products` (`id`, `product_code`, `name`, `status`, `quantity`) VALUES
              (1, 'MY-001', 'Dr. Ali\'s Syrup; 100ml', 'active', 12),
              (2, 'MY-002', 'Folder\\Path \"Check\"', 'inactive', 5);
            /*!40000 ALTER TABLE `products` ENABLE KEYS */;
            UNLOCK TABLES;
            """;
        await File.WriteAllTextAsync(mysqlDumpPath, mysqlDump, Encoding.UTF8);

        var stagedMysql = await engine.StageDatabaseFromFileAsync(mysqlDumpPath, "mysql_pharmacy.sql");
        var mysqlToken = stagedMysql["token"]!.ToString()!;
        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, mysqlToken, new SqliteImportExecutionOptions { Mode = "merge", ReconcileBatches = false });
        engine.CleanupStagedSession(mysqlToken);

        var prod1 = await sb.Db.QuerySingleOrDefaultAsync("SELECT name FROM products WHERE product_code = 'MY-001'");
        prod1.Should().NotBeNull();
        prod1!["name"]?.ToString().Should().Be("Dr. Ali's Syrup; 100ml");

        var prod2 = await sb.Db.QuerySingleOrDefaultAsync("SELECT name FROM products WHERE product_code = 'MY-002'");
        prod2.Should().NotBeNull();
        prod2!["name"]?.ToString().Should().Be("Folder\\Path \"Check\"");

        // 2. Bare MySQL INSERT snippet (no header/DDL markers except \' inside word)
        var bareMysqlPath = Path.Combine(sb.TempRoot, "bare_mysql.sql");
        await File.WriteAllTextAsync(bareMysqlPath, """
            CREATE TABLE categories (id INTEGER PRIMARY KEY, name TEXT);
            INSERT INTO categories (id, name) VALUES (1, 'Dr. O\'Brien; Remedies');
            """, Encoding.UTF8);
        var stagedBare = await engine.StageDatabaseFromFileAsync(bareMysqlPath, "bare_mysql.sql");
        var bareToken = stagedBare["token"]!.ToString()!;
        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, bareToken, new SqliteImportExecutionOptions { Mode = "merge", ReconcileBatches = false });
        engine.CleanupStagedSession(bareToken);

        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categories WHERE name = 'Dr. O''Brien; Remedies'")).Should().Be(1);

        // 3. T-SQL dump with trailing backslash literals ('C:\', 'D:\Backups\') must NEVER be corrupted by blind Replace("\'", "''")
        var tsqlPath = Path.Combine(sb.TempRoot, "tsql_paths.sql");
        await File.WriteAllTextAsync(tsqlPath, """
            CREATE TABLE [dbo].[storage_locations] (
                [id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
                [code] [nvarchar](50) NOT NULL,
                [name] [nvarchar](200) NOT NULL
            ) ON [PRIMARY];
            GO
            INSERT INTO [dbo].[storage_locations] ([id], [code], [name]) VALUES (1, N'LOC-C', N'C:\');
            GO
            INSERT INTO [dbo].[storage_locations] ([id], [code], [name]) VALUES (2, N'LOC-D', 'D:\Backups\');
            GO
            """, Encoding.UTF8);
        var stagedTsql = await engine.StageDatabaseFromFileAsync(tsqlPath, "tsql_paths.sql");
        var tsqlToken = stagedTsql["token"]!.ToString()!;
        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, tsqlToken, new SqliteImportExecutionOptions { Mode = "merge", ReconcileBatches = false });
        engine.CleanupStagedSession(tsqlToken);

        (await sb.Db.QuerySingleOrDefaultAsync("SELECT name FROM storage_locations WHERE code = 'LOC-C'"))!["name"]?.ToString().Should().Be(@"C:\");
        (await sb.Db.QuerySingleOrDefaultAsync("SELECT name FROM storage_locations WHERE code = 'LOC-D'"))!["name"]?.ToString().Should().Be(@"D:\Backups\");
    }

    [Fact]
    public async Task M2_HIGH3_MergeMode_SkippedOrDisabledParent_NeverFallsBackToExistingTargetId()
    {
        await using var sb = await TempImportSandbox.CreateAsync();
        await using (var dst = new SqliteConnection($"Data Source={sb.TargetDbPath};Pooling=False;"))
        {
            await dst.OpenAsync();
            await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await dst.ExecuteAsync("""
                INSERT INTO categories (id, name) VALUES (1, 'Target Cat');
                INSERT INTO brands (id, name) VALUES (1, 'Target Brand');
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (1, 'TGT-P1', 'TGT-P1', 'Target Product', 1, 1, 1, 10);
                """);
        }

        var sourceDbPath = Path.Combine(sb.TempRoot, "src_high3.sqlite");
        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: sourceDbPath)).EnsureSchemaReadyAsync();
        SqliteConnection.ClearAllPools();
        await using (var src = new SqliteConnection($"Data Source={sourceDbPath};Pooling=False;"))
        {
            await src.OpenAsync();
            await src.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await src.ExecuteAsync("""
                INSERT INTO categories (id, name) VALUES (1, 'Disabled Source Cat');
                INSERT INTO brands (id, name) VALUES (1, 'Source Brand');
                -- products only has id=2, while batches and trade_schemes reference missing product_id=1 (colliding with target id=1!)
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (2, 'SRC-P2', 'SRC-P2', 'Source Prod 2', 1, 1, 1, 5);
                INSERT INTO batches (id, product_id, batch_number, expiry_date, quantity)
                    VALUES (1, 1, 'ORPHAN-B1', '2029-01-01', 99);
                INSERT INTO trade_schemes (id, name, scheme_type, target_type, target_id, bonus_type, is_active)
                    VALUES (1, 'Orphan Product Scheme', 'ratio', 'product', 1, 'pack', 1);
                INSERT INTO trade_schemes (id, name, scheme_type, target_type, target_id, bonus_type, is_active)
                    VALUES (2, 'Disabled Category Scheme', 'ratio', 'category', 1, 'pack', 1);
                """);
            await src.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        }

        var engine = new WaseelaAndSqliteImportEngine(sb.TempRoot);
        var staged = await engine.StageDatabaseFromFileAsync(sourceDbPath, "src_high3.sqlite");
        var token = staged["token"]!.ToString()!;

        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, token, new SqliteImportExecutionOptions
        {
            Mode = "merge",
            ConflictPolicy = "skip",
            ReconcileBatches = false,
            Tables = new Dictionary<string, TableImportOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["categories"] = new TableImportOverride { Enabled = false, TargetTable = "categories" }
            }
        });
        engine.CleanupStagedSession(token);

        // Disabled categories must NOT be imported and SRC-P2 must fall back to "General", NOT Target Cat id=1
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categories WHERE name = 'Disabled Source Cat'")).Should().Be(0);
        var generalCatId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM categories WHERE name = 'General'");
        var srcProd = await sb.Db.QuerySingleOrDefaultAsync("SELECT category_id FROM products WHERE product_code = 'SRC-P2'");
        Convert.ToInt64(srcProd!["category_id"]).Should().Be(generalCatId);

        // Orphaned batch (product_id=1 missing from source products) must be SKIPPED, NEVER bound to Target Product id=1
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM batches WHERE batch_number = 'ORPHAN-B1'")).Should().Be(0);

        // Polymorphic trade_schemes targeting skipped product_id=1 and disabled category_id=1 must BOTH be skipped
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM trade_schemes")).Should().Be(0);
    }

    [Fact]
    public async Task M2_HIGH4_ReplaceMode_PartialImportCascadeClearsChildren_ResetsSequence_AndDiffsFkCheck()
    {
        await using var sb = await TempImportSandbox.CreateAsync();
        await using (var dst = new SqliteConnection($"Data Source={sb.TargetDbPath};Pooling=False;"))
        {
            await dst.OpenAsync();
            await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await dst.ExecuteAsync("""
                INSERT INTO settings (id, store_name, network_server_token, fbr_bearer_token)
                    VALUES (1, 'Preserved Store', 'KEEP-NET-TOK', 'KEEP-FBR-TOK');
                INSERT INTO terminals (id, terminal_uid, name) VALUES (1, 'LOCAL-TERM-1', 'Counter 1');
                INSERT INTO categories (id, name) VALUES (1, 'Old Cat');
                INSERT INTO brands (id, name) VALUES (1, 'Old Brand');
                INSERT INTO customers (id, name) VALUES (1, 'Old Customer');
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (1, 'OLD-P1', 'OLD-P1', 'Old Prod', 1, 1, 10, 50);
                INSERT INTO batches (id, product_id, batch_number, expiry_date, quantity)
                    VALUES (1, 1, 'OLD-BATCH', '2028-01-01', 50);
                INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total, total_receive)
                    VALUES (1, 1, 1, 'SI-OLD-1', '2026-10-01', 100, 100, 100);
                INSERT INTO sale_invoice_items (id, sale_invoice_id, product_id, pack_size, quantity, price, sub_total)
                    VALUES (1, 1, 1, 10, 5, 20, 100);
                INSERT INTO user_demands (id, product_id, customer_id, requested_by, requested_name, requested_quantity)
                    VALUES (1, 1, 1, 1, 'Old Prod', 2);
                INSERT INTO trade_schemes (id, name, scheme_type, target_type, target_id, bonus_type, is_active)
                    VALUES (1, 'Old Scheme', 'ratio', 'product', 1, 'pack', 1);
                INSERT INTO trade_scheme_slabs (id, trade_scheme_id, min_qty, bonus_qty, bonus_product_id)
                    VALUES (1, 1, 10, 1, 1);
                """);
        }

        // Partial Replace Mode with ONLY tblItems.csv (maps to products only)
        var csvPath = Path.Combine(sb.TempRoot, "tblItems.csv");
        await File.WriteAllTextAsync(csvPath,
            "ItemCode,ItemName,CategoryName,CompanyName,PackSize,TPPack,RetailPrice,ClosingStock\n" +
            "NEW-101,Fresh Syrup,Syrups,FreshLabs,1,100,120,15\n" +
            "NEW-102,Fresh Tablet,Tablets,FreshLabs,10,200,250,20\n",
            Encoding.UTF8);

        var engine = new WaseelaAndSqliteImportEngine(sb.TempRoot);
        var stagedCsv = await engine.StageDatabaseFromFileAsync(csvPath, "tblItems.csv");
        var csvToken = stagedCsv["token"]!.ToString()!;
        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, csvToken, new SqliteImportExecutionOptions
        {
            Mode = "replace",
            ConflictPolicy = "upsert",
            ReconcileBatches = true
        });
        engine.CleanupStagedSession(csvToken);

        // Old product & all transitive non-protected children must be cleared (no orphan or misbound OLD-BATCH!)
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products")).Should().Be(2);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM batches WHERE batch_number = 'OLD-BATCH'")).Should().Be(0);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM batches WHERE batch_number LIKE 'OPENING-%'")).Should().Be(2);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sale_invoice_items")).Should().Be(0);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM user_demands")).Should().Be(0);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM trade_schemes")).Should().Be(0);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM trade_scheme_slabs")).Should().Be(0);

        // ProtectedTables, settings, and terminals must be preserved
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM users WHERE id = 1")).Should().Be(1);
        (await sb.Db.ExecuteScalarAsync<string>("SELECT store_name FROM settings WHERE id = 1")).Should().Be("Preserved Store");
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM terminals WHERE terminal_uid = 'LOCAL-TERM-1'")).Should().Be(1);

        // Diffed PRAGMA foreign_key_check: pre-existing violation on unrelated customer_regular_items does NOT block Merge Mode on categories
        await using (var dst = new SqliteConnection($"Data Source={sb.TargetDbPath};Pooling=False;"))
        {
            await dst.OpenAsync();
            await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await dst.ExecuteAsync("INSERT INTO customer_regular_items (id, customer_id, product_id) VALUES (2, 999, 1);");
        }

        var catSqlPath = Path.Combine(sb.TempRoot, "cats_only.sql");
        await File.WriteAllTextAsync(catSqlPath, "CREATE TABLE categories (id INTEGER PRIMARY KEY, name TEXT); INSERT INTO categories (id, name) VALUES (1, 'Extra Cat');", Encoding.UTF8);
        var stagedCat = await engine.StageDatabaseFromFileAsync(catSqlPath, "cats_only.sql");
        var catToken = stagedCat["token"]!.ToString()!;
        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, catToken, new SqliteImportExecutionOptions { Mode = "merge", ReconcileBatches = false });
        engine.CleanupStagedSession(catToken);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categories WHERE name = 'Extra Cat'")).Should().Be(1);

        // New FK violation introduced by import MUST throw and roll back
        await using (var dst = new SqliteConnection($"Data Source={sb.TargetDbPath};Pooling=False;"))
        {
            await dst.OpenAsync();
            await dst.ExecuteAsync("CREATE TABLE custom_fk_child (id INTEGER PRIMARY KEY, cat_id INTEGER NOT NULL, FOREIGN KEY(cat_id) REFERENCES categories(id));");
        }
        var badFkPath = Path.Combine(sb.TempRoot, "bad_fk.sql");
        await File.WriteAllTextAsync(badFkPath, "CREATE TABLE custom_fk_child (id INTEGER PRIMARY KEY, cat_id INTEGER); INSERT INTO custom_fk_child (id, cat_id) VALUES (1, 99999);", Encoding.UTF8);
        var stagedBad = await engine.StageDatabaseFromFileAsync(badFkPath, "bad_fk.sql");
        var badToken = stagedBad["token"]!.ToString()!;
        Func<Task> act = () => engine.ExecuteImportCoreAsync(sb.TargetDbPath, badToken, new SqliteImportExecutionOptions { Mode = "merge", ReconcileBatches = false });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Foreign key integrity violation*");
        engine.CleanupStagedSession(badToken);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM custom_fk_child")).Should().Be(0);
    }

    [Fact]
    public async Task M2_HIGH5_MissingFks_StageOrdering_And_AllSixPolymorphicTargets()
    {
        await using var sb = await TempImportSandbox.CreateAsync();
        await using (var dst = new SqliteConnection($"Data Source={sb.TargetDbPath};Pooling=False;"))
        {
            await dst.OpenAsync();
            await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await dst.ExecuteAsync("""
                INSERT INTO categories (id, name) VALUES (1, 'Tgt Cat');
                INSERT INTO brands (id, name) VALUES (1, 'Tgt Brand');
                INSERT INTO suppliers (id, name) VALUES (1, 'Tgt Sup');
                INSERT INTO customers (id, name) VALUES (1, 'Tgt Cust');
                INSERT INTO storage_locations (id, code, name) VALUES (1, 'TGT-LOC', 'Target Loc');
                INSERT INTO accounts (id, name, type, current_balance) VALUES (1, 'Cash Drawer', 'cash', 0);
                INSERT INTO expense_categories (id, name, code) VALUES (1, 'Tgt Exp Cat', 'TGT-EXP');
                INSERT INTO tax_rates (id, name, code, rate) VALUES (1, 'Tgt Tax', 'TGT-TAX', 0);
                INSERT INTO terminals (id, terminal_uid, name) VALUES (1, 'TGT-TERM', 'Target Terminal');
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (1, 'TGT-P', 'TGT-P', 'Target Prod', 1, 1, 1, 10);
                INSERT INTO trade_schemes (id, name, scheme_type, target_type, target_id, bonus_type)
                    VALUES (1, 'Tgt Scheme', 'ratio', 'product', 1, 'pack');
                INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total)
                    VALUES (1, 1, 1, 'SI-TGT', '2026-10-01', 100, 100);
                INSERT INTO purchase_invoices (id, user_id, supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount)
                    VALUES (1, 1, 1, 'PI-TGT', 'PI-TGT', '2026-10-01', 100, 100);
                INSERT INTO cash_drawer_sessions (id, user_id, opened_at, status)
                    VALUES (1, 1, '2026-10-01 09:00:00', 'closed');
                INSERT INTO expenses (id, expense_number, expense_category_id, amount, date, payment_method)
                    VALUES (1, 'EXP-TGT', 1, 10, '2026-10-01', 'cash');
                INSERT INTO payments (id, payment_number, party_type, party_id, payment_date, amount)
                    VALUES (1, 'PAY-TGT', 'customer', 1, '2026-10-01', 10);
                INSERT INTO purchase_orders (id, number, date, supplier_id, status)
                    VALUES (1, 'PO-TGT', '2026-10-01', 1, 'ordered');
                """);
        }

        var srcDbPath = Path.Combine(sb.TempRoot, "src_high5.sqlite");
        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: srcDbPath)).EnsureSchemaReadyAsync();
        SqliteConnection.ClearAllPools();
        await using (var src = new SqliteConnection($"Data Source={srcDbPath};Pooling=False;"))
        {
            await src.OpenAsync();
            await src.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await src.ExecuteAsync("""
                INSERT INTO categories (id, name) VALUES (1, 'Src Cat');
                INSERT INTO brands (id, name) VALUES (1, 'Src Brand');
                INSERT INTO suppliers (id, name) VALUES (1, 'Src Sup');
                INSERT INTO customers (id, name) VALUES (1, 'Src Cust');
                INSERT INTO storage_locations (id, code, name) VALUES (1, 'SRC-LOC', 'Source Loc');
                INSERT INTO accounts (id, name, type, current_balance) VALUES (1, 'Src Bank', 'bank', 0);
                INSERT INTO expense_categories (id, name, code) VALUES (1, 'Src Exp Cat', 'SRC-EXP');
                INSERT INTO tax_rates (id, name, code, rate) VALUES (1, 'WHT 5%', 'SRC-WHT5', 5);
                INSERT INTO terminals (id, terminal_uid, name, current_user_id) VALUES (1, 'SRC-TERM', 'Source Terminal', 999);
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (1, 'SRC-P', 'SRC-P', 'Source Prod', 1, 1, 1, 10);

                -- trade_schemes: 3 valid polymorphic targets + 1 orphan (must skip!)
                INSERT INTO trade_schemes (id, name, scheme_type, target_type, target_id, bonus_type) VALUES (1, 'Prod Scheme', 'ratio', 'product', 1, 'pack');
                INSERT INTO trade_schemes (id, name, scheme_type, target_type, target_id, bonus_type) VALUES (2, 'Cat Scheme', 'ratio', 'category', 1, 'pack');
                INSERT INTO trade_schemes (id, name, scheme_type, target_type, target_id, bonus_type) VALUES (3, 'Brand Scheme', 'ratio', 'brand', 1, 'pack');
                INSERT INTO trade_schemes (id, name, scheme_type, target_type, target_id, bonus_type) VALUES (4, 'Orphan Scheme', 'ratio', 'product', 999, 'pack');

                -- stocktake_sessions: 1 valid storage_location scope + 1 orphan (must null scope_id)
                INSERT INTO stocktake_sessions (id, reference_no, session_type, status, scope_type, scope_id)
                    VALUES (1, 'ST-SRC-1', 'cycle', 'draft', 'storage_location', 1);
                INSERT INTO stocktake_sessions (id, reference_no, session_type, status, scope_type, scope_id)
                    VALUES (2, 'ST-SRC-2', 'cycle', 'draft', 'category', 999);

                INSERT INTO purchase_orders (id, number, date, supplier_id, status)
                    VALUES (1, 'PO-SRC', '2026-11-01', 1, 'ordered');
                INSERT INTO cash_drawer_sessions (id, user_id, opened_at, status, terminal_id)
                    VALUES (1, 1, '2026-11-01 09:00:00', 'open', 1);
                INSERT INTO expenses (id, expense_number, expense_category_id, account_id, cash_drawer_session_id, amount, date, payment_method, terminal_id)
                    VALUES (1, 'EXP-SRC', 1, 1, 1, 50, '2026-11-01', 'cash', 1);
                INSERT INTO payments (id, payment_number, party_type, party_id, payment_date, amount, wht_tax_rate_id, terminal_id)
                    VALUES (1, 'PAY-SRC', 'customer', 1, '2026-11-01', 500, 1, 1);
                INSERT INTO purchase_invoices (id, user_id, supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount)
                    VALUES (1, 1, 1, 'PI-SRC', 'PI-SRC', '2026-11-01', 200, 200);
                INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total, terminal_id)
                    VALUES (1, 1, 1, 'SI-SRC', '2026-11-01', 300, 300, 1);
                INSERT INTO sale_invoice_items (id, sale_invoice_id, product_id, pack_size, quantity, price, sub_total, applied_scheme_id)
                    VALUES (1, 1, 1, 1, 10, 30, 300, 1);
                INSERT INTO sale_returns (id, sale_invoice_id, user_id, customer_id, posted_number, date, total, terminal_id)
                    VALUES (1, 1, 1, 1, 'SR-SRC', '2026-11-01', 30, 1);

                -- invoice_drafts (Stage 4 -> after Stage 3 sale_invoices & purchase_invoices)
                INSERT INTO invoice_drafts (id, uuid, user_id, type, payload, status, posted_invoice_id, posted_invoice_type, terminal_id)
                    VALUES (1, 'draft-sale-1', 1, 'sale_retail', '{}', 'posted', 1, 'App\Models\SaleInvoice', 1);
                INSERT INTO invoice_drafts (id, uuid, user_id, type, payload, status, posted_invoice_id, posted_invoice_type, terminal_id)
                    VALUES (2, 'draft-pur-1', 1, 'purchase', '{}', 'posted', 1, NULL, 1);
                INSERT INTO invoice_drafts (id, uuid, user_id, type, payload, status, posted_invoice_id, posted_invoice_type, terminal_id)
                    VALUES (3, 'draft-orphan-1', 1, 'sale_retail', '{}', 'posted', 999, 'App\Models\SaleInvoice', 1);

                -- account_transactions
                INSERT INTO account_transactions (id, account_id, type, amount, reference_type, reference_id, transaction_date)
                    VALUES (1, 1, 'credit', 500, 'customer_payment', 1, '2026-11-01 10:00:00');
                INSERT INTO account_transactions (id, account_id, type, amount, reference_type, reference_id, transaction_date)
                    VALUES (2, 1, 'credit', 1000, 'opening_balance', 77, '2026-11-01 10:00:00');
                INSERT INTO account_transactions (id, account_id, type, amount, reference_type, reference_id, transaction_date)
                    VALUES (3, 1, 'debit', 50, 'purchase_invoice', 999, '2026-11-01 10:00:00');

                -- cash_drawer_transactions
                INSERT INTO cash_drawer_transactions (id, session_id, type, amount, reference_type, reference_id, terminal_id)
                    VALUES (1, 1, 'cash_out', 50, 'App\Models\Expense', 1, 1);
                INSERT INTO cash_drawer_transactions (id, session_id, type, amount, reference_type, reference_id, terminal_id)
                    VALUES (2, 1, 'cash_in', 20, 'manual', 42, 1);
                INSERT INTO cash_drawer_transactions (id, session_id, type, amount, reference_type, reference_id, terminal_id)
                    VALUES (3, 1, 'cash_sale', 10, 'sale_invoice', 999, 1);

                -- document_histories
                INSERT INTO document_histories (id, user_id, entity_type, entity_id, action, terminal_id)
                    VALUES (1, 1, 'App\Models\SaleInvoice', 1, 'created', 1);
                INSERT INTO document_histories (id, user_id, entity_type, entity_id, action, terminal_id)
                    VALUES (2, 1, 'purchase_order', 1, 'created', 1);
                INSERT INTO document_histories (id, user_id, entity_type, entity_id, action, terminal_id)
                    VALUES (3, 1, 'App\Models\SaleInvoice', 999, 'created', 1);
                """);
            await src.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        }

        var engine = new WaseelaAndSqliteImportEngine(sb.TempRoot);
        var staged = await engine.StageDatabaseFromFileAsync(srcDbPath, "src_high5.sqlite");
        var token = staged["token"]!.ToString()!;

        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, token, new SqliteImportExecutionOptions
        {
            Mode = "merge",
            ConflictPolicy = "skip",
            ReconcileBatches = false,
            Tables = new Dictionary<string, TableImportOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["terminals"] = new TableImportOverride { Enabled = true, TargetTable = "terminals" }
            }
        });
        engine.CleanupStagedSession(token);

        var newCatId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM categories WHERE name = 'Src Cat'");
        var newBrandId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM brands WHERE name = 'Src Brand'");
        var newLocId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM storage_locations WHERE code = 'SRC-LOC'");
        var newTaxId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM tax_rates WHERE code = 'SRC-WHT5'");
        var newTermId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM terminals WHERE terminal_uid = 'SRC-TERM'");
        var newProdId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'SRC-P'");
        var newSchemeId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM trade_schemes WHERE name = 'Prod Scheme'");
        var newSaleId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM sale_invoices WHERE posted_number = 'SI-SRC'");
        var newPurId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM purchase_invoices WHERE posted_number = 'PI-SRC'");
        var newExpId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM expenses WHERE expense_number = 'EXP-SRC'");
        var newPayId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM payments WHERE payment_number = 'PAY-SRC'");
        var newPoId = await sb.Db.ExecuteScalarAsync<long>("SELECT id FROM purchase_orders WHERE number = 'PO-SRC'");

        new[] { newCatId, newBrandId, newLocId, newTaxId, newTermId, newProdId, newSchemeId, newSaleId, newPurId, newExpId, newPayId, newPoId }
            .Should().OnlyContain(id => id > 1);

        // terminals.current_user_id=999 -> NULL (HIGH-5 + HIGH-6)
        (await sb.Db.QuerySingleOrDefaultAsync("SELECT current_user_id FROM terminals WHERE id = @id", new { id = newTermId }))!["current_user_id"].Should().BeNull();

        // applied_scheme_id, wht_tax_rate_id, and terminal_id across all 8 terminal tables
        (await sb.Db.ExecuteScalarAsync<long>("SELECT applied_scheme_id FROM sale_invoice_items WHERE sale_invoice_id = @sid", new { sid = newSaleId })).Should().Be(newSchemeId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT wht_tax_rate_id FROM payments WHERE id = @id", new { id = newPayId })).Should().Be(newTaxId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT terminal_id FROM sale_invoices WHERE id = @id", new { id = newSaleId })).Should().Be(newTermId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT terminal_id FROM sale_returns WHERE posted_number = 'SR-SRC'")).Should().Be(newTermId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT terminal_id FROM payments WHERE id = @id", new { id = newPayId })).Should().Be(newTermId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT terminal_id FROM expenses WHERE id = @id", new { id = newExpId })).Should().Be(newTermId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT terminal_id FROM cash_drawer_sessions WHERE status = 'open'")).Should().Be(newTermId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT terminal_id FROM cash_drawer_transactions WHERE reference_type = 'App\\Models\\Expense'")).Should().Be(newTermId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT terminal_id FROM invoice_drafts WHERE uuid = 'draft-sale-1'")).Should().Be(newTermId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT terminal_id FROM document_histories WHERE entity_type = 'App\\Models\\SaleInvoice'")).Should().Be(newTermId);

        // Polymorphic trade_schemes (valid remapped; orphan skipped)
        (await sb.Db.ExecuteScalarAsync<long>("SELECT target_id FROM trade_schemes WHERE name = 'Prod Scheme'")).Should().Be(newProdId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT target_id FROM trade_schemes WHERE name = 'Cat Scheme'")).Should().Be(newCatId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT target_id FROM trade_schemes WHERE name = 'Brand Scheme'")).Should().Be(newBrandId);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM trade_schemes WHERE name = 'Orphan Scheme'")).Should().Be(0);

        // Polymorphic stocktake_sessions
        (await sb.Db.ExecuteScalarAsync<long>("SELECT scope_id FROM stocktake_sessions WHERE reference_no = 'ST-SRC-1'")).Should().Be(newLocId);
        (await sb.Db.QuerySingleOrDefaultAsync("SELECT scope_id FROM stocktake_sessions WHERE reference_no = 'ST-SRC-2'"))!["scope_id"].Should().BeNull();

        // Polymorphic invoice_drafts (Stage 4 after Stage 3)
        (await sb.Db.ExecuteScalarAsync<long>("SELECT posted_invoice_id FROM invoice_drafts WHERE uuid = 'draft-sale-1'")).Should().Be(newSaleId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT posted_invoice_id FROM invoice_drafts WHERE uuid = 'draft-pur-1'")).Should().Be(newPurId);
        (await sb.Db.QuerySingleOrDefaultAsync("SELECT posted_invoice_id FROM invoice_drafts WHERE uuid = 'draft-orphan-1'"))!["posted_invoice_id"].Should().BeNull();

        // Polymorphic account_transactions
        (await sb.Db.ExecuteScalarAsync<long>("SELECT reference_id FROM account_transactions WHERE reference_type = 'customer_payment'")).Should().Be(newPayId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT reference_id FROM account_transactions WHERE reference_type = 'opening_balance'")).Should().Be(77);
        (await sb.Db.QuerySingleOrDefaultAsync("SELECT reference_id FROM account_transactions WHERE reference_type = 'purchase_invoice'"))!["reference_id"].Should().BeNull();

        // Polymorphic cash_drawer_transactions
        (await sb.Db.ExecuteScalarAsync<long>("SELECT reference_id FROM cash_drawer_transactions WHERE reference_type = 'App\\Models\\Expense'")).Should().Be(newExpId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT reference_id FROM cash_drawer_transactions WHERE reference_type = 'manual'")).Should().Be(42);
        (await sb.Db.QuerySingleOrDefaultAsync("SELECT reference_id FROM cash_drawer_transactions WHERE reference_type = 'sale_invoice'"))!["reference_id"].Should().BeNull();

        // Polymorphic document_histories (valid remapped; orphan skipped)
        (await sb.Db.ExecuteScalarAsync<long>("SELECT entity_id FROM document_histories WHERE entity_type = 'App\\Models\\SaleInvoice'")).Should().Be(newSaleId);
        (await sb.Db.ExecuteScalarAsync<long>("SELECT entity_id FROM document_histories WHERE entity_type = 'purchase_order'")).Should().Be(newPoId);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM document_histories")).Should().Be(2);
    }

    [Fact]
    public async Task M2_HIGH6_NullableUserForeignKeys_ResolveToNullWhenUnmapped()
    {
        await using var sb = await TempImportSandbox.CreateAsync();
        var srcDbPath = Path.Combine(sb.TempRoot, "src_high6.sqlite");
        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: srcDbPath)).EnsureSchemaReadyAsync();
        SqliteConnection.ClearAllPools();
        await using (var src = new SqliteConnection($"Data Source={srcDbPath};Pooling=False;"))
        {
            await src.OpenAsync();
            await src.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await src.ExecuteAsync("""
                INSERT INTO customers (id, name) VALUES (1, 'Cust H6');
                INSERT INTO employees (id, employee_code, name, user_id) VALUES (1, 'EMP-H6', 'Staff H6', 999);
                INSERT INTO sale_invoices (id, user_id, reversed_by_id, customer_id, posted_number, date, gross_amount, total)
                    VALUES (1, 999, 999, 1, 'SI-H6', '2026-11-01', 100, 100);
                """);
            await src.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        }

        var engine = new WaseelaAndSqliteImportEngine(sb.TempRoot);
        var staged = await engine.StageDatabaseFromFileAsync(srcDbPath, "src_high6.sqlite");
        var token = staged["token"]!.ToString()!;
        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, token, new SqliteImportExecutionOptions { Mode = "merge", ReconcileBatches = false, CurrentUserId = 1 });
        engine.CleanupStagedSession(token);

        var emp = await sb.Db.QuerySingleOrDefaultAsync("SELECT user_id FROM employees WHERE employee_code = 'EMP-H6'");
        emp!["user_id"].Should().BeNull("unmapped nullable employees.user_id must become NULL, not admin id");

        var sale = await sb.Db.QuerySingleOrDefaultAsync("SELECT user_id, reversed_by_id FROM sale_invoices WHERE posted_number = 'SI-H6'");
        Convert.ToInt64(sale!["user_id"]).Should().Be(1, "NOT NULL sale_invoices.user_id falls back to currentUserId");
        sale["reversed_by_id"].Should().BeNull("nullable sale_invoices.reversed_by_id must become NULL when unmapped");
    }

    [Fact]
    public async Task M2_MED4_Commit_FailsClosedWith422_WhenCreateBackupFails()
    {
        await using var sb = await TempImportSandbox.CreateAsync();
        var swToken = await UnlockImportAsync(sb.Db);

        var csvPath = Path.Combine(sb.TempRoot, "tblItems.csv");
        await File.WriteAllTextAsync(csvPath, "ItemCode,ItemName,ClosingStock\nMED4-1,Backup Abort Prod,10\n", Encoding.UTF8);

        var uploadCtrl = AttachAdminContext(new SqliteImportController(sb.Db), 1, JsonSerializer.Serialize(new { local_file_path = csvPath }));
        uploadCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
        var upOk = (await uploadCtrl.UploadAndInspect()).Should().BeOfType<OkObjectResult>().Subject;
        var sessionToken = upOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject["session_token"]!.ToString()!;

        // Sabotage storage/app/backups by replacing the directory with a regular file so safety backup fails
        var backupsPath = Path.Combine(sb.TempRoot, "storage", "app", "backups");
        Directory.Delete(backupsPath, recursive: true);
        await File.WriteAllTextAsync(backupsPath, "blocker-file", Encoding.UTF8);

        var commitPayload = JsonSerializer.Serialize(new
        {
            session_token = sessionToken,
            password = "password",
            options = new { mode = "replace", conflict_policy = "upsert", create_backup = true, reconcile_batches = false }
        });
        var commitCtrl = AttachAdminContext(new SqliteImportController(sb.Db), 1, commitPayload);
        commitCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
        var commitRes = await commitCtrl.Commit();
        var errObj = commitRes.Should().BeOfType<ObjectResult>().Subject;
        errObj.StatusCode.Should().Be(422);
        JsonSerializer.Serialize(errObj.Value).Should().Contain("Pre-import safety backup failed");

        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products")).Should().Be(0, "target DB must remain untouched when safety backup fails");
    }

    [Fact]
    public async Task M2_MED5_UniqueIndexes_NullableComposites_CrossFieldUpsert_SynthesizedNumbers_And_StockReconciliation()
    {
        await using var sb = await TempImportSandbox.CreateAsync();
        await using (var dst = new SqliteConnection($"Data Source={sb.TargetDbPath};Pooling=False;"))
        {
            await dst.OpenAsync();
            await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await dst.ExecuteAsync("""
                INSERT INTO categories (id, name) VALUES (1, 'Cat 1');
                INSERT INTO brands (id, name) VALUES (1, 'Brand 1');
                INSERT INTO suppliers (id, name) VALUES (1, 'Sup 1');
                INSERT INTO customers (id, name, email) VALUES (1, 'Alice', 'alice@test.local');
                INSERT INTO customers (id, name, email) VALUES (2, 'Bob', NULL);
                INSERT INTO employees (id, employee_code, name, pin_code) VALUES (1, 'EMP-T1', 'Staff 1', '1111');
                INSERT INTO employees (id, employee_code, name, pin_code) VALUES (2, 'EMP-T2', 'Staff 2', '2222');
                INSERT INTO employees (id, employee_code, name, pin_code) VALUES (3, 'EMP-T3', 'Staff 3', '3333');
                INSERT INTO payroll_runs (id, run_number, period_month, start_date, end_date, status)
                    VALUES (1, 'PR-T1', '2026-11', '2026-11-01', '2026-11-30', 'draft');
                INSERT INTO chart_of_accounts (id, code, name, account_type, system_key)
                    VALUES (1, '1001', 'Cash', 'asset', 'cash_on_hand');
                INSERT INTO payments (id, payment_number, party_type, party_id, payment_date, amount, idempotency_key)
                    VALUES (1, 'PAY-T1', 'customer', 1, '2026-11-01', 100, 'idem-1');
                INSERT INTO purchase_orders (id, number, date, supplier_id, status)
                    VALUES (1, 'PO-T1', '2026-11-01', 1, 'ordered');
                INSERT INTO purchase_invoices (id, user_id, supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount, receipt_key)
                    VALUES (1, 1, 1, 'PI-00001', 'PI-00001', '2026-11-01', 100, 100, 'rcpt-1');
                INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total)
                    VALUES (1, 1, 1, 'SI-00001', '2026-11-01', 100, 100);
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (1, 'PROD-STOCK', 'PROD-STOCK', 'Stocked Item', 1, 1, 1, 10);
                INSERT INTO batches (id, product_id, batch_number, expiry_date, quantity)
                    VALUES (1, 1, 'B-EXIST', '2028-12-31', 10);
                """);
        }

        var srcDbPath = Path.Combine(sb.TempRoot, "src_med5.sqlite");
        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: srcDbPath)).EnsureSchemaReadyAsync();
        SqliteConnection.ClearAllPools();
        await using (var src = new SqliteConnection($"Data Source={srcDbPath};Pooling=False;"))
        {
            await src.OpenAsync();
            await src.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await src.ExecuteAsync("""
                INSERT INTO suppliers (id, name) VALUES (1, 'Sup 1');
                INSERT INTO customers (id, name, email, phone) VALUES (1, 'Bob', 'alice@test.local', '03001112222');

                -- employees: row 1 matches pin_code '1111' (upserts EMP-T1 -> EMP-NEWCODE without SQLite Error 19); row 2 cross-field collides EMP-T3 (owned by id=3) vs pin_code '2222'
                INSERT INTO employees (id, employee_code, name, pin_code) VALUES (1, 'EMP-NEWCODE', 'Staff 1 Updated', '1111');
                INSERT INTO employees (id, employee_code, name, pin_code) VALUES (2, 'EMP-T3', 'Staff Collision', '2222');

                -- payroll_runs: different run_number, same period_month -> matches via period_month
                INSERT INTO payroll_runs (id, run_number, period_month, start_date, end_date, status, notes)
                    VALUES (1, 'PR-OTHER', '2026-11', '2026-11-01', '2026-11-30', 'draft', 'Updated By Period');

                -- chart_of_accounts: different code, same system_key -> matches via system_key
                INSERT INTO chart_of_accounts (id, code, name, account_type, system_key)
                    VALUES (1, '9999', 'Cash Updated', 'asset', 'cash_on_hand');

                -- payments: different payment_number, same idempotency_key -> matches via idempotency_key
                INSERT INTO payments (id, payment_number, party_type, party_id, payment_date, amount, idempotency_key, notes)
                    VALUES (1, 'PAY-OTHER', 'customer', 1, '2026-11-01', 100, 'idem-1', 'Matched Idem');

                -- purchase_orders: matches via number
                INSERT INTO purchase_orders (id, number, date, supplier_id, status, notes)
                    VALUES (1, 'PO-T1', '2026-11-01', 1, 'ordered', 'PO Updated');

                -- purchase_invoices: row 1 matches receipt_key='rcpt-1'; row 2 has blank posted_number & id=1 (synthesizes PI-00001-1!)
                INSERT INTO purchase_invoices (id, user_id, supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount, receipt_key, remarks)
                    VALUES (10, 1, 1, 'PI-OTHER', 'PI-OTHER', '2026-11-01', 100, 100, 'rcpt-1', 'Matched Rcpt');
                INSERT INTO purchase_invoices (id, user_id, supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount)
                    VALUES (1, 1, 1, '', '', '2026-11-02', 200, 200);

                -- sale_invoices: row 1 has blank posted_number & id=1 (synthesizes SI-00001-1); row 2 has explicit SI-EXPLICIT (preserved!)
                INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total)
                    VALUES (1, 1, 1, '', '2026-11-02', 150, 150);
                INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total)
                    VALUES (2, 1, 1, 'SI-EXPLICIT', '2026-11-02', 250, 250);

                -- customer_ledgers & supplier_ledgers: multiple (party_id, NULL) manual rows MUST all insert; non-null composite upserts cleanly
                INSERT INTO customer_ledgers (id, customer_id, sale_invoice_id, entry_date, invoice_total, entry_type, description)
                    VALUES (1, 1, NULL, '2026-11-01', 500, 'manual', 'Manual Khata 1');
                INSERT INTO customer_ledgers (id, customer_id, sale_invoice_id, entry_date, invoice_total, entry_type, description)
                    VALUES (2, 1, NULL, '2026-11-02', 300, 'manual', 'Manual Khata 2');
                INSERT INTO supplier_ledgers (id, supplier_id, purchase_invoice_id, entry_date, invoice_total, entry_type, description)
                    VALUES (1, 1, NULL, '2026-11-01', 400, 'manual', 'Sup Manual 1');
                INSERT INTO supplier_ledgers (id, supplier_id, purchase_invoice_id, entry_date, invoice_total, entry_type, description)
                    VALUES (2, 1, NULL, '2026-11-02', 600, 'manual', 'Sup Manual 2');

                -- batches: one negative qty (clamps to 0), one +25 for existing PROD-STOCK (reconciles products.quantity to 35!)
                INSERT INTO categories (id, name) VALUES (1, 'Cat 1');
                INSERT INTO brands (id, name) VALUES (1, 'Brand 1');
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (1, 'PROD-STOCK', 'PROD-STOCK', 'Stocked Item', 1, 1, 1, 10);
                INSERT INTO batches (id, product_id, batch_number, expiry_date, quantity)
                    VALUES (1, 1, 'B-NEG', '2028-12-31', -15);
                INSERT INTO batches (id, product_id, batch_number, expiry_date, quantity)
                    VALUES (2, 1, 'B-ADD', '2028-12-31', 25);
                """);
            await src.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        }

        var engine = new WaseelaAndSqliteImportEngine(sb.TempRoot);
        var staged = await engine.StageDatabaseFromFileAsync(srcDbPath, "src_med5.sqlite");
        var token = staged["token"]!.ToString()!;
        var result = await engine.ExecuteImportCoreAsync(sb.TargetDbPath, token, new SqliteImportExecutionOptions
        {
            Mode = "merge",
            ConflictPolicy = "upsert",
            ReconcileBatches = true
        });
        engine.CleanupStagedSession(token);

        var tableReports = result["tables"] as List<Dictionary<string, object?>>;
        var allWarnings = tableReports!.SelectMany(r => (r["warnings"] as List<string>) ?? new List<string>()).ToList();
        allWarnings.Should().NotContain(w => w.Contains("SQLite Error 19", StringComparison.OrdinalIgnoreCase));

        // Cross-field collision left Bob and Alice untouched
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM customers")).Should().Be(2);
        (await sb.Db.ExecuteScalarAsync<string>("SELECT name FROM employees WHERE id = 1")).Should().Be("Staff 1 Updated");
        (await sb.Db.ExecuteScalarAsync<string>("SELECT name FROM employees WHERE id = 2")).Should().Be("Staff 2");

        // Secondary unique keys matched & updated existing records (counts stayed 1)
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM payroll_runs")).Should().Be(1);
        (await sb.Db.ExecuteScalarAsync<string>("SELECT notes FROM payroll_runs WHERE id = 1")).Should().Be("Updated By Period");
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM chart_of_accounts")).Should().Be(1);
        (await sb.Db.ExecuteScalarAsync<string>("SELECT name FROM chart_of_accounts WHERE id = 1")).Should().Be("Cash Updated");
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM payments")).Should().Be(1);
        (await sb.Db.ExecuteScalarAsync<string>("SELECT notes FROM payments WHERE id = 1")).Should().Be("Matched Idem");
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM purchase_orders")).Should().Be(1);
        (await sb.Db.ExecuteScalarAsync<string>("SELECT notes FROM purchase_orders WHERE id = 1")).Should().Be("PO Updated");
        (await sb.Db.ExecuteScalarAsync<string>("SELECT remarks FROM purchase_invoices WHERE id = 1")).Should().Be("Matched Rcpt");

        // Synthesized PI-00001-1 and SI-00001-1 disambiguated without altering SI-EXPLICIT
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM purchase_invoices WHERE posted_number = 'PI-00001-1'")).Should().Be(1);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sale_invoices WHERE posted_number = 'SI-00001-1'")).Should().Be(1);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sale_invoices WHERE posted_number = 'SI-EXPLICIT'")).Should().Be(1);

        // Multiple (party_id, NULL) manual ledger rows allowed
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM customer_ledgers WHERE sale_invoice_id IS NULL")).Should().Be(2);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM supplier_ledgers WHERE purchase_invoice_id IS NULL")).Should().Be(2);

        // batches.quantity clamped >= 0 and products.quantity reconciled to SUM(batches.quantity) = 10 + 0 + 25 = 35
        (await sb.Db.ExecuteScalarAsync<int>("SELECT quantity FROM batches WHERE batch_number = 'B-NEG'")).Should().Be(0);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE product_code = 'PROD-STOCK'")).Should().Be(35);
    }

    [Fact]
    public async Task M2_MED6_TerminalsPreservedInReplaceMode_And_TokensExcludedFromSettings()
    {
        await using var sb = await TempImportSandbox.CreateAsync();
        await using (var dst = new SqliteConnection($"Data Source={sb.TargetDbPath};Pooling=False;"))
        {
            await dst.OpenAsync();
            await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await dst.ExecuteAsync("""
                INSERT INTO terminals (id, terminal_uid, name) VALUES (1, 'LOCAL-POS-1', 'Local Counter');
                INSERT INTO settings (id, store_name, network_server_token, fbr_bearer_token)
                    VALUES (1, 'Old Store', 'KEEP-NET-SECRET', 'KEEP-FBR-SECRET');
                """);
        }

        var srcDbPath = Path.Combine(sb.TempRoot, "src_med6.sqlite");
        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: srcDbPath)).EnsureSchemaReadyAsync();
        SqliteConnection.ClearAllPools();
        await using (var src = new SqliteConnection($"Data Source={srcDbPath};Pooling=False;"))
        {
            await src.OpenAsync();
            await src.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await src.ExecuteAsync("""
                INSERT INTO terminals (id, terminal_uid, name) VALUES (1, 'REMOTE-POS-9', 'Remote Counter');
                INSERT INTO settings (id, store_name, network_server_token, fbr_bearer_token)
                    VALUES (1, 'Imported Store', 'OVERWRITE-NET', 'OVERWRITE-FBR');
                INSERT INTO categories (id, name) VALUES (1, 'Cat Med6');
                """);
            await src.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        }

        var engine = new WaseelaAndSqliteImportEngine(sb.TempRoot);
        var staged = await engine.StageDatabaseFromFileAsync(srcDbPath, "src_med6.sqlite");
        var token = staged["token"]!.ToString()!;

        // Inspect must mark terminals as optional config and disabled by default
        var inspection = await engine.InspectAndProposeMappingAsync(token, sb.TargetDbPath);
        var tblList = inspection["tables"] as List<Dictionary<string, object?>>;
        var termEntry = tblList!.Single(t => t["target_table"]?.ToString() == "terminals");
        termEntry["is_optional_cfg"].Should().Be(true);
        termEntry["enabled"].Should().Be(false);

        // Replace mode with settings explicitly enabled AND terminals explicitly enabled still preserves LOCAL-POS-1 & sensitive tokens
        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, token, new SqliteImportExecutionOptions
        {
            Mode = "replace",
            ConflictPolicy = "upsert",
            ReconcileBatches = false,
            Tables = new Dictionary<string, TableImportOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["settings"] = new TableImportOverride { Enabled = true, TargetTable = "settings" },
                ["terminals"] = new TableImportOverride { Enabled = true, TargetTable = "terminals" }
            }
        });
        engine.CleanupStagedSession(token);

        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM terminals WHERE terminal_uid = 'LOCAL-POS-1'")).Should().Be(1);
        var settings = await sb.Db.QuerySingleOrDefaultAsync("SELECT store_name, network_server_token, fbr_bearer_token FROM settings WHERE id = 1");
        settings!["store_name"]?.ToString().Should().Be("Imported Store");
        settings["network_server_token"]?.ToString().Should().Be("KEEP-NET-SECRET");
        settings["fbr_bearer_token"]?.ToString().Should().Be("KEEP-FBR-SECRET");
    }

    [Fact]
    public async Task M2_M3Low_ZipCsvEntryAlias_CliSingleReconciliation_ErrorSurfacing_And_MalformedFormAuth()
    {
        await using var sb = await TempImportSandbox.CreateAsync();

        // 1. ZIP-CSV entry.Name alias: tblItems.csv inside backup.zip must stage as table `tblItems` (not `backup`)
        var zipPath = Path.Combine(sb.TempRoot, "backup.zip");
        using (var fs = new FileStream(zipPath, FileMode.Create))
        using (var arc = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = arc.CreateEntry("nested/tblItems.csv");
            await using var sw = new StreamWriter(entry.Open(), Encoding.UTF8);
            await sw.WriteAsync("ItemCode,ItemName,CompanyName,PackSize,TPPack,RetailPrice,ClosingStock\nZIP-1,Zip Item,ZipPharma,10,100,120,12\n");
        }

        var engine = new WaseelaAndSqliteImportEngine(sb.TempRoot);
        var stagedZip = await engine.StageDatabaseFromFileAsync(zipPath, "backup.zip");
        var zipToken = stagedZip["token"]!.ToString()!;
        var inspect = await engine.InspectAndProposeMappingAsync(zipToken, sb.TargetDbPath);
        var waseela = inspect["waseela_detection"] as Dictionary<string, object?>;
        waseela!["is_waseela_schema"].Should().Be(true);
        var tables = inspect["tables"] as List<Dictionary<string, object?>>;
        tables!.Should().ContainSingle(t => Convert.ToString(t["source_table"]) == "tblItems" && Convert.ToString(t["target_table"]) == "products");
        engine.CleanupStagedSession(zipToken);

        // 2. CLI ExecuteImportSqliteAsync runs single reconciliation and propagates reconciliation data
        var cli = new KarobarCliCommandRunner(sb.TempRoot);
        var opts = new DbContextOptionsBuilder<KarobarDbContext>()
            .UseSqlite($"Data Source={sb.TargetDbPath}").Options;
        await using (var efDb = new KarobarDbContext(opts))
        {
            var cliRes = await cli.ExecuteImportSqliteAsync(efDb, new Dictionary<string, string?> { ["file"] = zipPath, ["mode"] = "merge" });
            cliRes.ExitCode.Should().Be(0);
            var recon = cliRes.Data["reconciliation"] as Dictionary<string, int>;
            recon.Should().NotBeNull();
            recon!["opening_batches_created"].Should().Be(1);
            (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM batches WHERE batch_number LIKE 'OPENING-%'")).Should().Be(1);
        }

        // 3. RunPostImportReconciliationCoreAsync surfaces errors instead of swallowing silently
        var brokenDbPath = Path.Combine(sb.TempRoot, "broken_recon.sqlite");
        await using (var brokenConn = new SqliteConnection($"Data Source={brokenDbPath};Pooling=False;"))
        {
            await brokenConn.OpenAsync();
            await brokenConn.ExecuteAsync("CREATE TABLE products (id INTEGER PRIMARY KEY, quantity INTEGER); INSERT INTO products VALUES (1, 5);");
            await brokenConn.ExecuteAsync("CREATE TABLE batches (id INTEGER PRIMARY KEY, product_id INTEGER);"); // missing required batch columns!
            await using var bTx = (SqliteTransaction)await brokenConn.BeginTransactionAsync();
            Func<Task> act = () => engine.RunPostImportReconciliationCoreAsync(brokenConn, bTx);
            await act.Should().ThrowAsync<SqliteException>();
            await bTx.RollbackAsync();
        }

        // 4. AuthorizeSqliteImportAsync catches malformed multipart form body cleanly (returns 403 locked instead of 500/InvalidDataException)
        var malformedCtrl = AttachAdminContext(new SqliteImportController(sb.Db), 1);
        malformedCtrl.HttpContext.Request.ContentType = "multipart/form-data; boundary=broken_boundary";
        malformedCtrl.HttpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("not-valid-multipart-content"));
        var authFailRes = await malformedCtrl.Cancel(Guid.NewGuid().ToString("D"));
        var statusRes = authFailRes.Should().BeOfType<ObjectResult>().Subject;
        statusRes.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task M2_HIGH2_MySqlDoubleQuotedComments_ColumnIndexAndTable_StageAndImportCleanly()
    {
        await using var sb = await TempImportSandbox.CreateAsync();
        var engine = new WaseelaAndSqliteImportEngine(sb.TempRoot);

        var sqlPath = Path.Combine(sb.TempRoot, "mysql_double_quote_comments.sql");
        var mysqlDump = """
            -- MySQL dump
            CREATE TABLE `categories` (
              `id` int(11) NOT NULL AUTO_INCREMENT COMMENT "primary;id \"quoted\"",
              `name` varchar(100) NOT NULL COMMENT "double;quoted comment",
              PRIMARY KEY (`id`) COMMENT "pk;comment",
              UNIQUE KEY `uniq_cat_name` (`name`) USING BTREE COMMENT "cat (unique);comment"
            ) ENGINE=InnoDB COMMENT="table;level comment";
            INSERT INTO `categories` (`id`, `name`) VALUES (10, 'Antibiotics; Oral');
            INSERT INTO `categories` (`id`, `name`) VALUES (11, 'Pediatric "Drops"');
            """;
        await File.WriteAllTextAsync(sqlPath, mysqlDump, Encoding.UTF8);

        var staged = await engine.StageDatabaseFromFileAsync(sqlPath, "mysql_double_quote_comments.sql");
        var token = staged["token"]!.ToString()!;
        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, token, new SqliteImportExecutionOptions { Mode = "merge", ReconcileBatches = false });
        engine.CleanupStagedSession(token);

        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categories WHERE name = 'Antibiotics; Oral'")).Should().Be(1);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categories WHERE name = 'Pediatric \"Drops\"'")).Should().Be(1);
    }

    [Fact]
    public async Task M2_HIGH4_PartialReplace_CleansPolymorphicAliases_IncludingAppModelsProduct()
    {
        await using var sb = await TempImportSandbox.CreateAsync();
        await using (var dst = new SqliteConnection($"Data Source={sb.TargetDbPath};Pooling=False;"))
        {
            await dst.OpenAsync();
            await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await dst.ExecuteAsync("""
                INSERT INTO categories (id, name) VALUES (1, 'Cat 1');
                INSERT INTO brands (id, name) VALUES (1, 'Brand 1');
                INSERT INTO customers (id, name) VALUES (1, 'Cust 1');
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (1, 'OLD-P1', 'OLD-P1', 'Old Prod', 1, 1, 1, 10);
                INSERT INTO trade_schemes (id, name, scheme_type, target_type, target_id, bonus_type, is_active)
                    VALUES (901, 'Laravel Product Scheme', 'ratio', 'App\Models\Product', 1, 'pack', 1);
                INSERT INTO trade_schemes (id, name, scheme_type, target_type, target_id, bonus_type, is_active)
                    VALUES (902, 'Plural Product Scheme', 'ratio', 'products', 1, 'pack', 1);
                INSERT INTO trade_schemes (id, name, scheme_type, target_type, target_id, bonus_type, is_active)
                    VALUES (903, 'Singular Product Scheme', 'ratio', 'product', 1, 'pack', 1);
                INSERT INTO trade_schemes (id, name, scheme_type, target_type, target_id, bonus_type, is_active)
                    VALUES (904, 'Category Scheme Keep', 'ratio', 'App\Models\Category', 1, 'pack', 1);
                INSERT INTO trade_scheme_slabs (id, trade_scheme_id, min_qty, bonus_qty, bonus_product_id)
                    VALUES (1, 901, 5, 1, 1);
                INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total, total_receive)
                    VALUES (1, 1, 1, 'SI-1', '2026-10-01', 100, 100, 100);
                INSERT INTO sale_invoice_items (id, sale_invoice_id, product_id, pack_size, quantity, price, sub_total, applied_scheme_id)
                    VALUES (1, 1, 1, 1, 5, 20, 100, 901);
                INSERT INTO stocktake_sessions (id, reference_no, session_type, status, scope_type, scope_id)
                    VALUES (1, 'ST-KEEP', 'cycle', 'draft', 'App\Models\Category', 1);
                INSERT INTO document_histories (id, user_id, entity_type, entity_id, action)
                    VALUES (1, 1, 'App\Models\SaleInvoice', 1, 'created');
                """);
        }

        // Partial Replace on products only
        var csvPath = Path.Combine(sb.TempRoot, "tblItems.csv");
        await File.WriteAllTextAsync(csvPath,
            "ItemCode,ItemName,CategoryName,CompanyName,PackSize,TPPack,RetailPrice,ClosingStock\n" +
            "NEW-P1,Fresh Prod,Cat 1,Brand 1,1,50,70,5\n",
            Encoding.UTF8);

        var engine = new WaseelaAndSqliteImportEngine(sb.TempRoot);
        var staged = await engine.StageDatabaseFromFileAsync(csvPath, "tblItems.csv");
        var token = staged["token"]!.ToString()!;
        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, token, new SqliteImportExecutionOptions
        {
            Mode = "replace",
            ConflictPolicy = "upsert",
            ReconcileBatches = true
        });
        engine.CleanupStagedSession(token);

        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM trade_schemes WHERE id IN (901, 902, 903)")).Should().Be(0, "all product polymorphic target_type aliases must be cleaned up");
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM trade_scheme_slabs WHERE trade_scheme_id = 901")).Should().Be(0);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM trade_schemes WHERE id = 904")).Should().Be(1, "un-cleared category scheme must be preserved");
        (await sb.Db.ExecuteScalarAsync<long>("SELECT scope_id FROM stocktake_sessions WHERE id = 1")).Should().Be(1, "un-cleared category stocktake_session must remain intact");
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM document_histories WHERE id = 1")).Should().Be(1, "sale_invoices was not cleared so sale invoice history stays intact");
    }

    [Fact]
    public async Task M2_MED5_SequentialUpsert_EvictsStaleNaturalKey_WhenUpdatingUniqueField()
    {
        await using var sb = await TempImportSandbox.CreateAsync();
        await using (var dst = new SqliteConnection($"Data Source={sb.TargetDbPath};Pooling=False;"))
        {
            await dst.OpenAsync();
            await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await dst.ExecuteAsync("""
                INSERT INTO customers (id, name, email) VALUES (1, 'Alpha Clinic', 'a@x.com');
                INSERT INTO customers (id, name, email) VALUES (2, 'Beta Clinic', 'b@x.com');
                INSERT INTO categories (id, name) VALUES (1, 'General');
                INSERT INTO brands (id, name) VALUES (1, 'Generic');
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (1, 'OLD-CODE', 'BC-1', 'Old Prod Name', 1, 1, 1, 1);
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (2, 'P2-OLD', 'B2-OLD', 'Prod 2', 1, 1, 1, 2);
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (3, 'P3-EXIST', 'B3-EXIST', 'Prod 3', 1, 1, 1, 3);
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (4, 'P4-EXIST', 'B4-EXIST', 'Prod 4', 1, 1, 1, 4);
                """);
        }

        var srcDbPath = Path.Combine(sb.TempRoot, "src_med5_evict.sqlite");
        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: srcDbPath)).EnsureSchemaReadyAsync();
        SqliteConnection.ClearAllPools();
        await using (var src = new SqliteConnection($"Data Source={srcDbPath};Pooling=False;"))
        {
            await src.OpenAsync();
            await src.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await src.ExecuteAsync("""
                -- Row 11 matches id=1 by email='a@x.com' and renames name from 'Alpha Clinic' -> 'Alpha Renamed'
                INSERT INTO customers (id, name, email) VALUES (11, 'Alpha Renamed', 'a@x.com');
                -- Row 12 reuses freed name 'Alpha Clinic' with new email='c@x.com' (must INSERT new row, NOT overwrite id=1!)
                INSERT INTO customers (id, name, email) VALUES (12, 'Alpha Clinic', 'c@x.com');

                INSERT INTO categories (id, name) VALUES (1, 'General');
                INSERT INTO brands (id, name) VALUES (1, 'Generic');
                -- Row 21 matches id=1 by product_code='OLD-CODE' and changes barcode 'BC-1' -> 'BC-NEW'
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (21, 'OLD-CODE', 'BC-NEW', 'Renamed Prod', 1, 1, 1, 5);
                -- Row 22 reuses freed barcode 'BC-1' with new product_code 'NEW-CODE-2' (must INSERT new row, NOT overwrite id=1!)
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (22, 'NEW-CODE-2', 'BC-1', 'Second Prod', 1, 1, 1, 7);

                -- Row 23 matches id=2 by explicit barcode='B2-OLD' and renames product_code 'P2-OLD' -> 'P2-RENAMED'
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (23, 'P2-RENAMED', 'B2-OLD', 'Prod 2 Updated', 1, 1, 1, 6);
                -- Row 24 reuses freed product_code 'P2-OLD' with new barcode 'B2-FRESH' (must INSERT new row, NOT overwrite id=2!)
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (24, 'P2-OLD', 'B2-FRESH', 'Prod 2b New', 1, 1, 1, 8);

                -- Row 25 has genuine cross-product collision (P3-EXIST on id=3, B4-EXIST on id=4) -> must be skipped without mangling barcode!
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (25, 'P3-EXIST', 'B4-EXIST', 'Cross Collision', 1, 1, 1, 9);
                -- Row 26 has blank barcode whose code 'B3-EXIST' collides with id=3's barcode -> must disambiguate synthetic barcode to BC-B3-EXIST-26
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (26, 'B3-EXIST', '', 'Blank Barcode Disambiguated', 1, 1, 1, 4);
                """);
            await src.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        }

        var engine = new WaseelaAndSqliteImportEngine(sb.TempRoot);
        var staged = await engine.StageDatabaseFromFileAsync(srcDbPath, "src_med5_evict.sqlite");
        var token = staged["token"]!.ToString()!;
        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, token, new SqliteImportExecutionOptions
        {
            Mode = "merge",
            ConflictPolicy = "upsert",
            ReconcileBatches = false
        });
        engine.CleanupStagedSession(token);

        var cust1 = await sb.Db.QuerySingleOrDefaultAsync("SELECT name, email FROM customers WHERE id = 1");
        cust1!["name"]?.ToString().Should().Be("Alpha Renamed");
        cust1["email"]?.ToString().Should().Be("a@x.com");

        var custNew = await sb.Db.QuerySingleOrDefaultAsync("SELECT id, name, email FROM customers WHERE email = 'c@x.com'");
        custNew.Should().NotBeNull("second row reusing freed name 'Alpha Clinic' must insert cleanly instead of overwriting id=1");
        Convert.ToInt64(custNew!["id"]).Should().BeGreaterThan(2);
        custNew["name"]?.ToString().Should().Be("Alpha Clinic");

        var prod1 = await sb.Db.QuerySingleOrDefaultAsync("SELECT product_code, barcode, name FROM products WHERE id = 1");
        prod1!["product_code"]?.ToString().Should().Be("OLD-CODE");
        prod1["barcode"]?.ToString().Should().Be("BC-NEW");

        var prodNewBc = await sb.Db.QuerySingleOrDefaultAsync("SELECT id, product_code, barcode, name FROM products WHERE product_code = 'NEW-CODE-2'");
        prodNewBc.Should().NotBeNull("second product reusing freed barcode 'BC-1' must insert cleanly");
        Convert.ToInt64(prodNewBc!["id"]).Should().BeGreaterThan(4);
        prodNewBc["barcode"]?.ToString().Should().Be("BC-1");

        var prod2 = await sb.Db.QuerySingleOrDefaultAsync("SELECT product_code, barcode, name FROM products WHERE id = 2");
        prod2!["product_code"]?.ToString().Should().Be("P2-RENAMED");
        prod2["barcode"]?.ToString().Should().Be("B2-OLD");
        prod2["name"]?.ToString().Should().Be("Prod 2 Updated");

        var prod2b = await sb.Db.QuerySingleOrDefaultAsync("SELECT id, product_code, barcode, name FROM products WHERE product_code = 'P2-OLD'");
        prod2b.Should().NotBeNull("row reusing freed product_code 'P2-OLD' must insert cleanly instead of overwriting id=2");
        Convert.ToInt64(prod2b!["id"]).Should().BeGreaterThan(4);
        prod2b["barcode"]?.ToString().Should().Be("B2-FRESH");

        var prod3 = await sb.Db.QuerySingleOrDefaultAsync("SELECT product_code, barcode, name FROM products WHERE id = 3");
        prod3!["product_code"]?.ToString().Should().Be("P3-EXIST");
        prod3["barcode"]?.ToString().Should().Be("B3-EXIST");
        prod3["name"]?.ToString().Should().Be("Prod 3");

        var prod4 = await sb.Db.QuerySingleOrDefaultAsync("SELECT product_code, barcode, name FROM products WHERE id = 4");
        prod4!["product_code"]?.ToString().Should().Be("P4-EXIST");
        prod4["barcode"]?.ToString().Should().Be("B4-EXIST");
        prod4["name"]?.ToString().Should().Be("Prod 4");

        var prodDisamb = await sb.Db.QuerySingleOrDefaultAsync("SELECT product_code, barcode FROM products WHERE product_code = 'B3-EXIST'");
        prodDisamb.Should().NotBeNull("blank barcode colliding with another product's barcode must still be disambiguated");
        prodDisamb!["barcode"]?.ToString().Should().Be("BC-B3-EXIST-26");
    }

    [Fact]
    public async Task M2_HIGH4_MergeMode_IntoTableWithPreExistingFkViolations_AllowsValidRowsAndRejectsNewViolations()
    {
        await using var sb = await TempImportSandbox.CreateAsync();
        await using (var dst = new SqliteConnection($"Data Source={sb.TargetDbPath};Pooling=False;"))
        {
            await dst.OpenAsync();
            await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await dst.ExecuteAsync("""
                INSERT INTO categories (id, name) VALUES (1, 'General');
                INSERT INTO brands (id, name) VALUES (1, 'Generic');
                INSERT INTO customers (id, name) VALUES (1, 'Valid Cust');
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (1, 'P-1', 'P-1', 'Prod 1', 1, 1, 1, 10);
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (2, 'P-2', 'P-2', 'Prod 2', 1, 1, 1, 10);
                -- Pre-existing broken FK row (customer_id=999) mirroring real database.sqlite anomaly
                INSERT INTO customer_regular_items (id, customer_id, product_id, quantity) VALUES (2, 999, 1, 1);
                """);
        }

        // 1. Merge Mode directly targeting customer_regular_items with a VALID new row must SUCCEED
        var validSrcPath = Path.Combine(sb.TempRoot, "src_cri_valid.sqlite");
        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: validSrcPath)).EnsureSchemaReadyAsync();
        SqliteConnection.ClearAllPools();
        await using (var src = new SqliteConnection($"Data Source={validSrcPath};Pooling=False;"))
        {
            await src.OpenAsync();
            await src.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await src.ExecuteAsync("""
                INSERT INTO customers (id, name) VALUES (1, 'Valid Cust');
                INSERT INTO categories (id, name) VALUES (1, 'General');
                INSERT INTO brands (id, name) VALUES (1, 'Generic');
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity)
                    VALUES (2, 'P-2', 'P-2', 'Prod 2', 1, 1, 1, 10);
                INSERT INTO customer_regular_items (id, customer_id, product_id, quantity) VALUES (10, 1, 2, 3);
                """);
            await src.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        }

        var engine = new WaseelaAndSqliteImportEngine(sb.TempRoot);
        var stagedValid = await engine.StageDatabaseFromFileAsync(validSrcPath, "src_cri_valid.sqlite");
        var validToken = stagedValid["token"]!.ToString()!;
        await engine.ExecuteImportCoreAsync(sb.TargetDbPath, validToken, new SqliteImportExecutionOptions
        {
            Mode = "merge",
            ConflictPolicy = "upsert",
            ReconcileBatches = false,
            Tables = new Dictionary<string, TableImportOverride>(StringComparer.OrdinalIgnoreCase)
            {
                ["customer_regular_items"] = new TableImportOverride { Enabled = true, TargetTable = "customer_regular_items" }
            }
        });
        engine.CleanupStagedSession(validToken);

        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM customer_regular_items WHERE customer_id = 1 AND product_id = 2")).Should().Be(1);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM customer_regular_items")).Should().Be(2);

        // 2. Newly introduced broken FK row in the same DB MUST still throw and roll back
        await using (var dst = new SqliteConnection($"Data Source={sb.TargetDbPath};Pooling=False;"))
        {
            await dst.OpenAsync();
            await dst.ExecuteAsync("CREATE TABLE custom_fk_child (id INTEGER PRIMARY KEY, cat_id INTEGER NOT NULL, FOREIGN KEY(cat_id) REFERENCES categories(id));");
        }
        var badSqlPath = Path.Combine(sb.TempRoot, "bad_fk_merge.sql");
        await File.WriteAllTextAsync(badSqlPath, "CREATE TABLE custom_fk_child (id INTEGER PRIMARY KEY, cat_id INTEGER); INSERT INTO custom_fk_child (id, cat_id) VALUES (1, 88888);", Encoding.UTF8);
        var stagedBad = await engine.StageDatabaseFromFileAsync(badSqlPath, "bad_fk_merge.sql");
        var badToken = stagedBad["token"]!.ToString()!;
        Func<Task> act = () => engine.ExecuteImportCoreAsync(sb.TargetDbPath, badToken, new SqliteImportExecutionOptions { Mode = "merge", ReconcileBatches = false });
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Foreign key integrity violation*");
        engine.CleanupStagedSession(badToken);
        (await sb.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM custom_fk_child")).Should().Be(0);
    }
}

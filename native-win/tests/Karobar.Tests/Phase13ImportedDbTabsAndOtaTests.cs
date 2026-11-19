using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Karobar.Api.Controllers;
using Karobar.Data;
using Karobar.Host;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Karobar.Tests;

public class Phase13ImportedDbTabsAndOtaTests
{
    [Fact]
    public async Task Products_SaleInvoices_And_PurchaseInvoices_Load_Cleanly_With_Empty_React_QueryParams()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"karobar_qa_p13_api_{Guid.NewGuid():N}.sqlite");
        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        try
        {
            var sourceFactory = new DbConnectionFactory();
            var usedRealDb = false;
            if (File.Exists(sourceFactory.SqlitePath) && new FileInfo(sourceFactory.SqlitePath).Length > 0)
            {
                File.Copy(sourceFactory.SqlitePath, tempDb, overwrite: true);
                usedRealDb = true;
            }
            else
            {
                var initFactory = new DbConnectionFactory(explicitSqlitePath: tempDb);
                await new DatabaseBootstrapper(initFactory).EnsureSchemaReadyAsync();
            }

            // Always run non-destructive upgrade to ensure schema parity
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: tempDb)).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var kDb = new KarobarDb(explicitSqlitePath: tempDb);
            await SeedMinimalApiDataIfEmptyAsync(kDb);

            var adminUserId = await kDb.ExecuteScalarAsync<long>("SELECT id FROM users ORDER BY id ASC LIMIT 1");
            if (adminUserId <= 0) adminUserId = 1;

            // 1. Products Index with exact empty string React sends for `low_stock: ''`
            var prodCtrl = AttachAdminContext(new ProductController(kDb), adminUserId, "?page=1&per_page=25&q=&low_stock=");
            var prodRes = await prodCtrl.Index(perPage: 25, page: 1, q: "", lowStockRaw: "");
            var prodOk = prodRes.Should().BeOfType<OkObjectResult>().Subject;
            var prodDict = prodOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            Convert.ToInt32(prodDict["total"]).Should().BeGreaterThan(0, usedRealDb ? "real database.sqlite has products" : "seeded DB has products");
            var prodRows = prodDict["data"].Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;
            prodRows.Should().NotBeEmpty();

            // 2. Sale Invoices Index with exact empty strings React sends (`posted=&customer=`)
            var saleCtrl = AttachAdminContext(new SaleInvoiceController(kDb), adminUserId, "?page=1&per_page=10&posted=&customer=");
            var saleRes = await saleCtrl.Index(customer_id: null, posted: "", customer: "", sale_type: "", invoice_type: "", page: 1, per_page: 10);
            var saleOk = saleRes.Should().BeOfType<OkObjectResult>().Subject;
            var saleDict = saleOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            Convert.ToInt32(saleDict["total"]).Should().BeGreaterThan(0);
            var saleRows = saleDict["data"].Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;
            saleRows.Should().NotBeEmpty();

            // 3. Purchase Invoices Index
            var purchCtrl = AttachAdminContext(new PurchaseInvoiceController(kDb), adminUserId, "?page=1&per_page=10&posted=&supplier=");
            var purchRes = await purchCtrl.Index(supplier_id: null, posted: "", supplier: "", page: 1, per_page: 10);
            var purchOk = purchRes.Should().BeOfType<OkObjectResult>().Subject;
            var purchDict = purchOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            Convert.ToInt32(purchDict["total"]).Should().BeGreaterThan(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
            try { if (File.Exists(tempDb + "-wal")) File.Delete(tempDb + "-wal"); } catch { }
            try { if (File.Exists(tempDb + "-shm")) File.Delete(tempDb + "-shm"); } catch { }
        }
    }

    [Fact]
    public async Task DatabaseBootstrapper_AutoUpgrades_Older_Imported_Sqlite_Without_DataLoss()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"karobar_qa_p13_legacy_{Guid.NewGuid():N}.sqlite");
        try
        {
            // Create a legacy SQLite DB missing newer columns (products.storage_location_id, sale_invoices.salesman_id/status) and newer tables
            await using (var conn = new SqliteConnection($"Data Source={tempDb}"))
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync("""
                    CREATE TABLE products (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        product_code TEXT,
                        name TEXT NOT NULL,
                        quantity INTEGER DEFAULT 0
                    );
                    INSERT INTO products (id, product_code, name, quantity) VALUES (10, 'PRD-0010', 'Legacy Panadol', 50);

                    CREATE TABLE sale_invoices (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        user_id INTEGER NOT NULL,
                        customer_id INTEGER NOT NULL,
                        posted_number TEXT NOT NULL,
                        date TEXT NOT NULL,
                        gross_amount REAL NOT NULL,
                        total REAL NOT NULL
                    );
                    INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total)
                    VALUES (25, 1, 1, 'SI-000025', '2026-01-15', 500, 500);
                    """);
            }

            // Run non-destructive auto-migration
            var factory = new DbConnectionFactory(explicitSqlitePath: tempDb);
            await new DatabaseBootstrapper(factory).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var kDb = new KarobarDb(explicitSqlitePath: tempDb);
            (await kDb.HasColumnAsync("products", "storage_location_id")).Should().BeTrue("missing column must be added via ALTER TABLE");
            (await kDb.HasColumnAsync("sale_invoices", "salesman_id")).Should().BeTrue("missing salesman_id must be added");
            (await kDb.HasColumnAsync("sale_invoices", "status")).Should().BeTrue("missing status must be added");
            (await kDb.HasTableAsync("employees")).Should().BeTrue("missing tables must be created non-destructively");

            // Original rows must be 100% preserved
            var prodName = await kDb.ExecuteScalarAsync<string>("SELECT name FROM products WHERE id = 10");
            prodName.Should().Be("Legacy Panadol");
            var invNo = await kDb.ExecuteScalarAsync<string>("SELECT posted_number FROM sale_invoices WHERE id = 25");
            invNo.Should().Be("SI-000025");
        }
        finally
        {
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (File.Exists(tempDb)) File.Delete(tempDb); } catch { }
            try { if (File.Exists(tempDb + "-wal")) File.Delete(tempDb + "-wal"); } catch { }
            try { if (File.Exists(tempDb + "-shm")) File.Delete(tempDb + "-shm"); } catch { }
        }
    }

    [Fact]
    public async Task SqliteImportController_ReplaceMode_Preserves_Products_Sales_Purchases_And_ForeignKeys()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_qa_p13_imp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        try
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
            var targetDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
            var sourceDbPath = Path.Combine(tempRoot, "storage", "app", "imports", "sqlite", "11111111-2222-3333-4444-555555555555.sqlite");
            Directory.CreateDirectory(Path.GetDirectoryName(sourceDbPath)!);

            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: targetDbPath)).EnsureSchemaReadyAsync();
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: sourceDbPath)).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            // Seed target DB with Admin user (id=1)
            var targetDb = new KarobarDb(explicitSqlitePath: targetDbPath);
            await SeedAdminOnlyAsync(targetDb);

            // Seed source DB with linked masters + products + batches + purchase_invoices + sale_invoices (with orphan user_id=999!)
            // Keep source DB in WAL mode so recent Sale Invoices live in -wal (real-world Electron Karobar-App scenario!)
            await using (var src = new SqliteConnection($"Data Source={sourceDbPath};Pooling=False"))
            {
                await src.OpenAsync();
                await src.ExecuteAsync("PRAGMA journal_mode=WAL; PRAGMA foreign_keys = OFF;");
                await src.ExecuteAsync("""
                    INSERT INTO categories (id, name) VALUES (10, 'Tablets');
                    INSERT INTO brands (id, name) VALUES (20, 'GSK');
                    INSERT INTO suppliers (id, name) VALUES (30, 'Premier Distributors');
                    INSERT INTO customers (id, name) VALUES (40, 'Walk-In Customer');
                    INSERT INTO products (id, product_code, name, category_id, brand_id, supplier_id, pack_size, quantity, pack_sale_price, unit_sale_price)
                        VALUES (100, 'PRD-0100', 'Augmentin 625mg', 10, 20, 30, 10, 80, 350, 35);
                    INSERT INTO batches (id, product_id, batch_number, expiry_date, quantity) VALUES (200, 100, 'B-2026', '2027-12-31', 80);
                    INSERT INTO purchase_invoices (id, supplier_id, posted_number, invoice_number, posted_date, total_amount, invoice_amount)
                        VALUES (300, 30, 'PRINV-0001', 'SUP-99', '2026-10-01', 2500, 2500);
                    INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total, total_receive, sale_type, invoice_type, status)
                        VALUES (400, 999, 40, 'SI-000400', '2026-10-02', 700, 700, 700, 'retail', 'debit', 'posted');
                    INSERT INTO sale_invoice_items (id, sale_invoice_id, product_id, quantity, price, sub_total)
                        VALUES (500, 400, 100, 20, 35, 700);
                    """);
            }

            // Test UploadAndInspect on the WAL-mode source DB (merging sibling -wal automatically!)
            var walSourcePath = sourceDbPath;

            // Generate valid software admin token matching SqliteImportController
            var dateStr = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var secret = Environment.GetEnvironmentVariable("APP_KEY") is { Length: > 0 } k ? k : "karobar-native-win-default-app-key-secret";
            var sigHex = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"sqlite_import_sw_admin|1|{dateStr}"))).ToLowerInvariant();
            var swToken = Convert.ToBase64String(Encoding.UTF8.GetBytes($"1|{dateStr}|{sigHex}"));

            var uploadPayload = JsonSerializer.Serialize(new { local_file_path = walSourcePath, software_admin_token = swToken });
            var uploadCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", uploadPayload);
            uploadCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var uploadRes = await uploadCtrl.UploadAndInspect();
            var uploadDebug = uploadRes is ObjectResult orRes ? $"status={orRes.StatusCode}, body={JsonSerializer.Serialize(orRes.Value)}" : uploadRes.GetType().Name;
            var uploadOk = uploadRes.Should().BeOfType<OkObjectResult>($"UploadAndInspect must succeed cleanly on WAL-mode SQLite files ({uploadDebug})").Subject;
            var uploadDict = uploadOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            var uploadedToken = uploadDict["session_token"]?.ToString();
            uploadedToken.Should().NotBeNullOrWhiteSpace();

            var payloadJson = JsonSerializer.Serialize(new
            {
                token = uploadedToken,
                mode = "replace",
                password = "password123",
                software_admin_token = swToken
            });

            var ctrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", payloadJson);
            ctrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var res = await ctrl.Commit();
            var ok = res.Should().BeOfType<OkObjectResult>().Subject;
            var respDict = ok.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            respDict["ok"].Should().Be(true);

            // Verify 100% of products, batches, purchase invoices, and sale invoices imported with preserved IDs & remapped user_id
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products")).Should().Be(1);
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM batches")).Should().Be(1);
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM purchase_invoices")).Should().Be(1);
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sale_invoices")).Should().Be(1);
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sale_invoice_items")).Should().Be(1);

            var importedProd = await targetDb.QuerySingleOrDefaultAsync("SELECT id, name, brand_id, supplier_id FROM products WHERE id = 100");
            importedProd.Should().NotBeNull();
            importedProd!["name"]?.ToString().Should().Be("Augmentin 625mg");

            var importedSale = await targetDb.QuerySingleOrDefaultAsync("SELECT id, user_id, customer_id, posted_number FROM sale_invoices WHERE id = 400");
            importedSale.Should().NotBeNull();
            Convert.ToInt64(importedSale!["user_id"]).Should().Be(1, "orphan source user_id=999 must remap cleanly to active Admin id=1");
            Convert.ToInt64(importedSale["customer_id"]).Should().Be(40);

            // Verify SaleInvoiceController.Index immediately returns the imported sale invoice!
            var saleIdxCtrl = AttachAdminContext(new SaleInvoiceController(targetDb), 1, "?page=1&per_page=10&posted=&customer=");
            var saleIdxRes = await saleIdxCtrl.Index(page: 1, per_page: 10);
            var saleIdxOk = saleIdxRes.Should().BeOfType<OkObjectResult>().Subject;
            var saleIdxDict = saleIdxOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            Convert.ToInt32(saleIdxDict["total"]).Should().Be(1);
        }
        finally
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Waseela_NativeTabWorkspace_And_Differential_Ota_Patch_Guardrails_Work_Cleanly()
    {
        // 1. Waseela Single-Window Multi-Tab Workspace tests
        var ws = new NativeTabWorkspace();
        var (dash, createdDash) = ws.OpenTab("/dashboard", "Dashboard", pinned: true);
        createdDash.Should().BeTrue();
        dash.IsPinned.Should().BeTrue();

        var (prod1, createdProd1) = ws.OpenTab("/products", "Products");
        createdProd1.Should().BeTrue();
        var (prod2, createdProd2) = ws.OpenTab("/products", "Products");
        createdProd2.Should().BeFalse("singleton list routes focus existing tab instead of duplicating");
        prod2.TabId.Should().Be(prod1.TabId);

        // Parallel transactional invoice tabs MUST open side-by-side (Waseela multitasking!)
        var (sale1, cSale1) = ws.OpenTab("/sale-invoices/create/retail", "Retail Sale");
        var (sale2, cSale2) = ws.OpenTab("/sale-invoices/create/retail", "Retail Sale");
        cSale1.Should().BeTrue();
        cSale2.Should().BeTrue();
        sale1.TabId.Should().NotBe(sale2.TabId);
        sale2.Title.Should().Be("Retail Sale #2");

        // Pinned tab cannot be closed accidentally; unpinned tab closes & reopens via Ctrl+Shift+T
        ws.CloseTab(dash.TabId).Should().BeFalse();
        ws.CloseTab(sale2.TabId).Should().BeTrue();
        var reopened = ws.ReopenLastClosedTab();
        reopened.Should().NotBeNull();
        reopened!.RoutePath.Should().Be("/sale-invoices/create/retail");

        // Tab cycling & CloseOthers preserves pinned Dashboard
        ws.CycleTab(1).Should().NotBeNull();
        var closedOthers = ws.CloseOtherTabs(prod1.TabId);
        closedOthers.Should().NotBeEmpty();
        ws.Tabs.Should().Contain(t => t.TabId == dash.TabId);
        ws.Tabs.Should().Contain(t => t.TabId == prod1.TabId);

        // 2. Differential OTA Patch checksum & zero-data-loss path guardrails
        var tmpPatch = Path.Combine(Path.GetTempPath(), $"karobar_qa_patch_{Guid.NewGuid():N}.zip");
        try
        {
            var bytes = Encoding.UTF8.GetBytes("karobar-differential-patch-payload-test");
            File.WriteAllBytes(tmpPatch, bytes);
            var expectedSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            KarobarDesktopBridge.VerifyFileSha256(tmpPatch, expectedSha).Should().BeTrue();
            KarobarDesktopBridge.VerifyFileSha256(tmpPatch, "0000000000000000000000000000000000000000000000000000000000000000").Should().BeFalse();
        }
        finally
        {
            try { if (File.Exists(tmpPatch)) File.Delete(tmpPatch); } catch { }
        }

        KarobarDesktopBridge.IsProtectedPatchPath("database/database.sqlite").Should().BeTrue();
        KarobarDesktopBridge.IsProtectedPatchPath("storage/app/.license-clock").Should().BeTrue();
        KarobarDesktopBridge.IsProtectedPatchPath("storage/app/.machine-id").Should().BeTrue();
        KarobarDesktopBridge.IsProtectedPatchPath("storage/app/public/logo.png").Should().BeTrue();
        KarobarDesktopBridge.IsProtectedPatchPath(".env").Should().BeTrue();
        KarobarDesktopBridge.IsProtectedPatchPath("Karobar.Api.dll").Should().BeFalse();

        // 3. Verify ReadLocalVersionMeta reads runtime_layout="split-v2" so fast differential patches are selected
        var tmpAppRoot = Path.Combine(Path.GetTempPath(), $"karobar_qa_ver_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(tmpAppRoot, "public"));
            File.WriteAllText(
                Path.Combine(tmpAppRoot, "public", "version.json"),
                """{"app_name":"Karobar-App Native","version":"2.0.50","build_number":50,"runtime_layout":"split-v2"}""");
            var cfg = new NetworkConfigManager(tmpAppRoot);
            var bridge = new KarobarDesktopBridge(cfg, new WebView2WindowManager(cfg));
            var meta = bridge.ReadLocalVersionMeta();
            meta.Version.Should().Be("2.0.50");
            meta.BuildNumber.Should().Be(50);
            meta.RuntimeLayout.Should().Be("split-v2");
        }
        finally
        {
            try { Directory.Delete(tmpAppRoot, recursive: true); } catch { }
        }
        KarobarDesktopBridge.IsProtectedPatchPath("public/build/assets/app.js").Should().BeFalse();
    }

    private static TController AttachAdminContext<TController>(TController controller, long userId, string queryString = "", string? jsonBody = null)
        where TController : ControllerBase
    {
        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        ], "Sanctum");
        httpContext.User = new ClaimsPrincipal(identity);

        if (!string.IsNullOrEmpty(queryString))
        {
            httpContext.Request.QueryString = new QueryString(queryString);
        }

        if (jsonBody != null)
        {
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(jsonBody));
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static async Task SeedAdminOnlyAsync(KarobarDb db)
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

    private static async Task SeedMinimalApiDataIfEmptyAsync(KarobarDb db)
    {
        await SeedAdminOnlyAsync(db);
        if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products") > 0 &&
            await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sale_invoices") > 0 &&
            await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM purchase_invoices") > 0)
        {
            return;
        }

        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync("""
            INSERT OR IGNORE INTO categories (id, name) VALUES (1, 'General');
            INSERT OR IGNORE INTO brands (id, name) VALUES (1, 'TestBrand');
            INSERT OR IGNORE INTO suppliers (id, name) VALUES (1, 'TestSupplier');
            INSERT OR IGNORE INTO customers (id, name) VALUES (1, 'Walk-In');
            INSERT OR IGNORE INTO products (id, product_code, name, category_id, brand_id, supplier_id, pack_size, quantity, pack_sale_price, unit_sale_price)
                VALUES (1, 'PRD-0001', 'Paracetamol 500mg', 1, 1, 1, 10, 100, 200, 20);
            INSERT OR IGNORE INTO purchase_invoices (id, supplier_id, posted_number, invoice_number, posted_date, total_amount, invoice_amount)
                VALUES (1, 1, 'PRINV-0001', 'INV-1', '2026-11-01', 1000, 1000);
            INSERT OR IGNORE INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total, total_receive, sale_type, invoice_type, status)
                VALUES (1, 1, 1, 'SI-000001', '2026-11-02', 200, 200, 200, 'retail', 'debit', 'posted');
            """);
    }
}

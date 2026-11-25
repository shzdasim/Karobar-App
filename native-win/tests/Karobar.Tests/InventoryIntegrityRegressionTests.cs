using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Karobar.Api.Controllers;
using Karobar.Data;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Karobar.Tests;

public class InventoryIntegrityRegressionTests
{
    private sealed class IsolatedEnv : IAsyncDisposable
    {
        public string TempRoot { get; }
        public string DbPath { get; }
        public KarobarDb Db { get; }
        private readonly string? _prevRootEnv;

        private IsolatedEnv(string tempRoot, string dbPath, KarobarDb db, string? prevRootEnv)
        {
            TempRoot = tempRoot;
            DbPath = dbPath;
            Db = db;
            _prevRootEnv = prevRootEnv;
        }

        public static async Task<IsolatedEnv> CreateAsync()
        {
            var tempRoot = Path.Combine(AppContext.BaseDirectory, "inv_reg_scratch", $"karobar_inv_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "public"));

            var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);

            var dbPath = Path.Combine(tempRoot, "database", "database.sqlite");
            KarobarDb.FlushSchemaCache();
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: dbPath)).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var db = new KarobarDb(explicitSqlitePath: dbPath);
            return new IsolatedEnv(tempRoot, dbPath, db, prevRoot);
        }

        public async Task<long> SeedAdminAsync()
        {
            var existingRole = await Db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE LOWER(name) = 'admin' LIMIT 1");
            if (existingRole == null)
            {
                await Db.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "Admin", ["guard_name"] = "sanctum" });
            }

            var uid = await Db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "AuditAdmin",
                ["email"] = $"admin_{Guid.NewGuid():N}@test.local",
                ["password"] = BCrypt.Net.BCrypt.HashPassword("Password123!", 4),
                ["status"] = "active"
            });
            await Db.SyncUserRolesAsync(uid, ["Admin"]);
            return uid;
        }

        public async Task<(long CatId, long BrandId, long SuppId, long CustId)> SeedMasterCatalogAsync()
        {
            var catId = await Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "Tablets" });
            var brandId = await Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "TestBrand" });
            var suppId = await Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "TestSupplier", ["phone"] = "03001112233", ["balance"] = 0.0 });
            var custId = await Db.InsertAsync("customers", new Dictionary<string, object?> { ["name"] = "Walk-in", ["phone"] = "03009998877", ["balance"] = 0.0 });
            await Db.InsertAsync("accounts", new Dictionary<string, object?> { ["name"] = "Cash in Hand", ["type"] = "cash", ["balance"] = 100000.0, ["is_default"] = 1, ["status"] = "active" });
            return (catId, brandId, suppId, custId);
        }

        public ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", _prevRootEnv);
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, recursive: true); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private static ControllerContext BuildContext(long userId, object? jsonBody = null)
    {
        var http = new DefaultHttpContext();
        http.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim("sub", userId.ToString())
        ], "TestAuth"));

        if (jsonBody != null)
        {
            var json = JsonSerializer.Serialize(jsonBody);
            http.Request.ContentType = "application/json";
            http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
        }

        return new ControllerContext { HttpContext = http };
    }

    private static int StatusOf(IActionResult res) => (res as IStatusCodeActionResult)?.StatusCode ?? 200;

    [Fact]
    public async Task Regression1_And_2_ProductEdit_BlocksDirectStockOverwrite_And_OpeningStockCreatesLedger()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var uid = await env.SeedAdminAsync();
        var (catId, brandId, suppId, _) = await env.SeedMasterCatalogAsync();

        // 1. Create product with 0 stock -> 0 movements
        var createZeroCtrl = new ProductController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                product_code = "P-ZERO",
                name = "Zero Stock Tab",
                barcode = "BC-ZERO",
                pack_size = 10,
                narcotic = "no",
                category_id = catId,
                brand_id = brandId,
                supplier_id = suppId,
                quantity = 0,
                unit_purchase_price = 10,
                unit_sale_price = 15
            })
        };
        var createZeroRes = await createZeroCtrl.Store();
        StatusOf(createZeroRes).Should().Be(201);
        var zeroId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'P-ZERO'");
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM inventory_movements WHERE product_id = @id", new { id = zeroId })).Should().Be(0);

        // 2. Initialize explicit opening stock -> creates opening_stock movement
        var openCtrl = new ProductController(env.Db)
        {
            ControllerContext = BuildContext(uid, new { quantity = 40, reason = "Legacy import" })
        };
        StatusOf(await openCtrl.InitializeOpeningStock(zeroId)).Should().Be(200);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = zeroId })).Should().Be(40);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM inventory_movements WHERE product_id = @id AND transaction_type = 'opening_stock'", new { id = zeroId })).Should().Be(1);

        // 3. Re-initializing opening stock when movements already exist -> rejected 422
        var openAgainCtrl = new ProductController(env.Db)
        {
            ControllerContext = BuildContext(uid, new { quantity = 99 })
        };
        StatusOf(await openAgainCtrl.InitializeOpeningStock(zeroId)).Should().Be(422);

        // 4. Editing product metadata WITHOUT changing quantity -> 200 OK
        var editMetaCtrl = new ProductController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                product_code = "P-ZERO",
                name = "Zero Stock Tab Updated",
                barcode = "BC-ZERO",
                pack_size = 10,
                narcotic = "no",
                category_id = catId,
                brand_id = brandId,
                supplier_id = suppId,
                unit_purchase_price = 10,
                unit_sale_price = 16
            })
        };
        StatusOf(await editMetaCtrl.Update(zeroId)).Should().Be(200);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = zeroId })).Should().Be(40);

        // 5. Attempting to overwrite quantity via ProductController.Update -> rejected 422!
        var overwriteCtrl = new ProductController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                product_code = "P-ZERO",
                name = "Zero Stock Tab Updated",
                barcode = "BC-ZERO",
                pack_size = 10,
                narcotic = "no",
                category_id = catId,
                brand_id = brandId,
                supplier_id = suppId,
                quantity = 999,
                unit_purchase_price = 10,
                unit_sale_price = 16
            })
        };
        StatusOf(await overwriteCtrl.Update(zeroId)).Should().Be(422);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = zeroId })).Should().Be(40);
    }

    [Fact]
    public async Task Regression3_And_4_LegacyUnbatchedStock_And_BatchInvariant_OnAdjustment()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var uid = await env.SeedAdminAsync();
        var (catId, brandId, suppId, custId) = await env.SeedMasterCatalogAsync();

        // Create product with 100 legacy unbatched opening stock
        var prodCtrl = new ProductController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                product_code = "P-BROM",
                name = "Bromalex Legacy Test",
                barcode = "BC-BROM",
                pack_size = 30,
                narcotic = "no",
                category_id = catId,
                brand_id = brandId,
                supplier_id = suppId,
                quantity = 100,
                unit_purchase_price = 10,
                unit_sale_price = 15
            })
        };
        StatusOf(await prodCtrl.Store()).Should().Be(201);
        var pid = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'P-BROM'");

        // Purchase 50 units in Batch B1 -> Stored = 150, Batched = 50, Unbatched = 100
        var purchCtrl = new PurchaseInvoiceController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                supplier_id = suppId,
                posted_date = "2026-11-25",
                invoice_number = "SUP-B1",
                invoice_type = "debit",
                payment_method = "cash",
                items = new[]
                {
                    new { product_id = pid, batch = "B1", expiry = "2028-12-31", pack_size = 30, pack_quantity = 1, unit_quantity = 40, unit_bonus = 10, pack_purchase_price = 300, unit_purchase_price = 10, pack_sale_price = 450, unit_sale_price = 15 }
                }
            })
        };
        StatusOf(await purchCtrl.Store()).Should().Be(201);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(150);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM batches WHERE product_id = @pid AND batch_number = 'B1'", new { pid })).Should().Be(50);

        // Unbatched sale of 20 units -> deducts from unbatched stock first, B1 stays 50, Stored = 130
        var saleCtrl = new SaleInvoiceController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                customer_id = custId,
                date = "2026-11-25",
                invoice_type = "debit",
                payment_method = "cash",
                total_receive = 300,
                items = new[] { new { product_id = pid, quantity = 20, price = 15 } }
            })
        };
        StatusOf(await saleCtrl.Store()).Should().Be(201);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(130);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM batches WHERE product_id = @pid AND batch_number = 'B1'", new { pid })).Should().Be(50);

        // Unbatched stock adjustment setting total actual_qty = 30 (< batched 50!)
        // Invariant must hold: batches must be trimmed to 30 so BatchedStock <= StoredStock
        var adjCtrl = new StockAdjustmentController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                posted_date = "2026-11-25",
                note = "Physical count",
                items = new[] { new { product_id = pid, actual_qty = 30 } }
            })
        };
        StatusOf(await adjCtrl.Store()).Should().Be(200);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(30);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM batches WHERE product_id = @pid", new { pid })).Should().Be(30);

        var rec = await new StockReconciliationService(env.Db).ReconcileProductAsync(pid);
        rec.Should().NotBeNull();
        rec!.StoredStock.Should().Be(30);
        rec.BatchedStock.Should().Be(30);
        rec.UnbatchedStock.Should().Be(0);
        rec.BatchExceedsProductStock.Should().BeFalse();
        rec.HasLedgerMismatch.Should().BeFalse();
    }

    [Fact]
    public async Task Regression5_6_7_8_9_InvoiceEdits_Reversals_Returns_And_Bonus_ReconcileToLedger()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var uid = await env.SeedAdminAsync();
        var (catId, brandId, suppId, custId) = await env.SeedMasterCatalogAsync();

        var prodCtrl = new ProductController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                product_code = "P-EDIT",
                name = "Edit Flow Tab",
                barcode = "BC-EDIT",
                pack_size = 10,
                narcotic = "no",
                category_id = catId,
                brand_id = brandId,
                supplier_id = suppId,
                quantity = 0,
                unit_purchase_price = 10,
                unit_sale_price = 20
            })
        };
        StatusOf(await prodCtrl.Store()).Should().Be(201);
        var pid = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'P-EDIT'");

        // Purchase 100 (80 paid + 20 bonus)
        var pStore = new PurchaseInvoiceController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                supplier_id = suppId,
                posted_date = "2026-11-25",
                invoice_number = "SUP-E1",
                invoice_type = "debit",
                payment_method = "cash",
                items = new[]
                {
                    new { product_id = pid, batch = "E1", expiry = "2028-06-30", pack_size = 10, pack_quantity = 8, unit_quantity = 80, unit_bonus = 20, pack_purchase_price = 100, unit_purchase_price = 10, pack_sale_price = 200, unit_sale_price = 20 }
                }
            })
        };
        StatusOf(await pStore.Store()).Should().Be(201);
        var pInvId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM purchase_invoices ORDER BY id DESC LIMIT 1");
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(100);

        // Edit Purchase 100 -> 120
        var pEdit = new PurchaseInvoiceController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                supplier_id = suppId,
                posted_date = "2026-11-25",
                invoice_number = "SUP-E1",
                invoice_type = "debit",
                payment_method = "cash",
                items = new[]
                {
                    new { product_id = pid, batch = "E1", expiry = "2028-06-30", pack_size = 10, pack_quantity = 10, unit_quantity = 100, unit_bonus = 20, pack_purchase_price = 100, unit_purchase_price = 10, pack_sale_price = 200, unit_sale_price = 20 }
                }
            })
        };
        StatusOf(await pEdit.Update(pInvId)).Should().Be(200);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(120);

        // Sale 20 units
        var sStore = new SaleInvoiceController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                customer_id = custId,
                date = "2026-11-25",
                invoice_type = "debit",
                payment_method = "cash",
                total_receive = 400,
                items = new[] { new { product_id = pid, batch_number = "E1", expiry = "2028-06-30", quantity = 20, price = 20 } }
            })
        };
        StatusOf(await sStore.Store()).Should().Be(201);
        var sInvId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM sale_invoices ORDER BY id DESC LIMIT 1");
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(100);

        // Edit Sale 20 -> 15 (restores 5)
        var sEdit1 = new SaleInvoiceController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                customer_id = custId,
                date = "2026-11-25",
                invoice_type = "debit",
                payment_method = "cash",
                total_receive = 300,
                items = new[] { new { product_id = pid, batch_number = "E1", expiry = "2028-06-30", quantity = 15, price = 20 } }
            })
        };
        StatusOf(await sEdit1.Update(sInvId)).Should().Be(200);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(105);

        // Edit Sale 15 -> 30 (deducts additional 15)
        var sEdit2 = new SaleInvoiceController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                customer_id = custId,
                date = "2026-11-25",
                invoice_type = "debit",
                payment_method = "cash",
                total_receive = 600,
                items = new[] { new { product_id = pid, batch_number = "E1", expiry = "2028-06-30", quantity = 30, price = 20 } }
            })
        };
        StatusOf(await sEdit2.Update(sInvId)).Should().Be(200);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(90);

        // Sale Return of 5 units -> 95
        var srStore = new SaleReturnController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                customer_id = custId,
                sale_invoice_id = sInvId,
                date = "2026-11-25",
                settlement_mode = "cash_refund",
                payment_method = "cash",
                items = new[] { new { product_id = pid, batch_number = "E1", expiry = "2028-06-30", unit_return_quantity = 5, unit_sale_price = 20 } }
            })
        };
        StatusOf(await srStore.Store()).Should().Be(201);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(95);

        // Purchase Return of 10 units -> 85
        var prStore = new PurchaseReturnController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                supplier_id = suppId,
                purchase_invoice_id = pInvId,
                date = "2026-11-25",
                settlement_mode = "cash_refund",
                payment_method = "cash",
                items = new[] { new { product_id = pid, batch = "E1", expiry = "2028-06-30", pack_size = 10, return_unit_quantity = 10, unit_purchase_price = 10 } }
            })
        };
        StatusOf(await prStore.Store()).Should().Be(201);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(85);

        // Reconcile via read-only StockReconciliationService
        var rec = await new StockReconciliationService(env.Db).ReconcileProductAsync(pid);
        rec.Should().NotBeNull();
        rec!.StoredStock.Should().Be(85);
        rec.TransactionDerivedStock.Should().Be(85);
        rec.MovementDerivedStock.Should().Be(85);
        rec.BatchedStock.Should().Be(85);
        rec.HasLedgerMismatch.Should().BeFalse();
        rec.HasTransactionMismatch.Should().BeFalse();
    }

    [Fact]
    public async Task Regression10_11_Concurrency_Rollback_And_Reconciliation()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var uid = await env.SeedAdminAsync();
        var (catId, brandId, suppId, custId) = await env.SeedMasterCatalogAsync();

        var prodCtrl = new ProductController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                product_code = "P-CONC",
                name = "Concurrent Tab",
                barcode = "BC-CONC",
                pack_size = 10,
                narcotic = "no",
                category_id = catId,
                brand_id = brandId,
                supplier_id = suppId,
                quantity = 10,
                unit_purchase_price = 10,
                unit_sale_price = 20
            })
        };
        StatusOf(await prodCtrl.Store()).Should().Be(201);
        var pid = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'P-CONC'");

        // 1. Failed multi-item sale (item 2 has insufficient stock) -> complete rollback!
        var failProdCtrl = new ProductController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                product_code = "P-LOW",
                name = "Low Tab",
                barcode = "BC-LOW",
                pack_size = 10,
                narcotic = "no",
                category_id = catId,
                brand_id = brandId,
                supplier_id = suppId,
                quantity = 1,
                unit_purchase_price = 10,
                unit_sale_price = 20
            })
        };
        StatusOf(await failProdCtrl.Store()).Should().Be(201);
        var lowPid = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'P-LOW'");

        var failSaleCtrl = new SaleInvoiceController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                customer_id = custId,
                date = "2026-11-25",
                invoice_type = "debit",
                payment_method = "cash",
                total_receive = 100,
                items = new[]
                {
                    new { product_id = pid, quantity = 5, price = 20 },
                    new { product_id = lowPid, quantity = 5, price = 20 }
                }
            })
        };
        StatusOf(await failSaleCtrl.Store()).Should().Be(422);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(10);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sale_invoices")).Should().Be(0);

        // 2. Two concurrent tasks selling the last 10 units -> exactly 1 succeeds, 1 fails
        var t1 = Task.Run(async () =>
        {
            var c = new SaleInvoiceController(env.Db)
            {
                ControllerContext = BuildContext(uid, new
                {
                    customer_id = custId,
                    date = "2026-11-25",
                    invoice_type = "debit",
                    payment_method = "cash",
                    total_receive = 200,
                    items = new[] { new { product_id = pid, quantity = 10, price = 20 } }
                })
            };
            return StatusOf(await c.Store());
        });
        var t2 = Task.Run(async () =>
        {
            var c = new SaleInvoiceController(env.Db)
            {
                ControllerContext = BuildContext(uid, new
                {
                    customer_id = custId,
                    date = "2026-11-25",
                    invoice_type = "debit",
                    payment_method = "cash",
                    total_receive = 200,
                    items = new[] { new { product_id = pid, quantity = 10, price = 20 } }
                })
            };
            return StatusOf(await c.Store());
        });

        var statuses = await Task.WhenAll(t1, t2);
        statuses.Count(s => s == 201).Should().Be(1);
        statuses.Count(s => s == 422).Should().Be(1);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(0);

        var rec = await new StockReconciliationService(env.Db).ReconcileProductAsync(pid);
        rec!.StoredStock.Should().Be(0);
        rec.MovementDerivedStock.Should().Be(0);
        rec.HasLedgerMismatch.Should().BeFalse();
        rec.HasNegativeStock.Should().BeFalse();
    }

    [Fact]
    public async Task Phase3_And_8_ImmutableReversals_DuplicateReverseBlock_And_CorruptedDbDetection()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var uid = await env.SeedAdminAsync();
        var (catId, brandId, suppId, custId) = await env.SeedMasterCatalogAsync();

        var prodCtrl = new ProductController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                product_code = "P-IMMUT",
                name = "Immutable Ledger Tab",
                barcode = "BC-IMMUT",
                pack_size = 10,
                narcotic = "no",
                category_id = catId,
                brand_id = brandId,
                supplier_id = suppId,
                quantity = 50,
                unit_purchase_price = 10,
                unit_sale_price = 20
            })
        };
        StatusOf(await prodCtrl.Store()).Should().Be(201);
        var pid = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'P-IMMUT'");

        // 1. Sell 10 units -> 40
        var sStore = new SaleInvoiceController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                customer_id = custId,
                date = "2026-11-25",
                invoice_type = "debit",
                payment_method = "cash",
                total_receive = 200,
                items = new[] { new { product_id = pid, quantity = 10, price = 20 } }
            })
        };
        StatusOf(await sStore.Store()).Should().Be(201);
        var sInvId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM sale_invoices ORDER BY id DESC LIMIT 1");

        // 2. Edit sale 10 -> 7: verify original -10 movement stays immutable, +10 reversal added, -7 new sale added
        var sEdit = new SaleInvoiceController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                customer_id = custId,
                date = "2026-11-25",
                invoice_type = "debit",
                payment_method = "cash",
                total_receive = 140,
                items = new[] { new { product_id = pid, quantity = 7, price = 20 } }
            })
        };
        StatusOf(await sEdit.Update(sInvId)).Should().Be(200);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(43);

        var movs = await env.Db.QueryListAsync(
            "SELECT transaction_type, CAST(quantity_in AS REAL) AS qin, CAST(quantity_out AS REAL) AS qout FROM inventory_movements WHERE product_id = @pid ORDER BY id ASC",
            new { pid });
        movs.Should().HaveCount(4); // opening +50, sale -10, reversal +10, sale -7
        Convert.ToDecimal(movs[1]["qout"]).Should().Be(10m);
        Convert.ToDecimal(movs[2]["qin"]).Should().Be(10m);
        Convert.ToDecimal(movs[3]["qout"]).Should().Be(7m);

        // 3. Reverse invoice once -> 50; reverse twice -> 422, no duplicate movement
        var revCtrl1 = new SaleInvoiceController(env.Db) { ControllerContext = BuildContext(uid, new { reason = "Customer cancelled" }) };
        StatusOf(await revCtrl1.Reverse(sInvId)).Should().Be(200);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(50);

        var revCtrl2 = new SaleInvoiceController(env.Db) { ControllerContext = BuildContext(uid, new { reason = "Retry" }) };
        StatusOf(await revCtrl2.Reverse(sInvId)).Should().Be(422);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(50);

        // 4. Intentionally corrupt isolated DB and verify StockReconciliationService detects it (read-only!)
        await env.Db.ExecuteAsync("UPDATE products SET quantity = -5 WHERE id = @pid", new { pid });
        await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = pid,
            ["batch_number"] = "CORRUPT-B",
            ["expiry_date"] = "2028-12-31",
            ["quantity"] = 20
        });
        var corruptRec = await new StockReconciliationService(env.Db).ReconcileProductAsync(pid);
        corruptRec.Should().NotBeNull();
        corruptRec!.HasNegativeStock.Should().BeTrue();
        corruptRec.BatchExceedsProductStock.Should().BeTrue();
        corruptRec.HasLedgerMismatch.Should().BeTrue();
        // Verify reconciliation remained strictly read-only
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(-5);
    }

    [Fact]
    public async Task Baseline_IsolatedSynthetic_CoversZeroNegativeBatchedUnbatchedAndConcurrency()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var uid = await env.SeedAdminAsync();
        var (catId, brandId, suppId, custId) = await env.SeedMasterCatalogAsync();

        // Seed pre-ledger products directly in DB (simulating existing pre-migration database)
        var pZero = await env.Db.InsertAsync("products", new Dictionary<string, object?> { ["product_code"] = "BL-Z", ["name"] = "BL Zero", ["barcode"] = "BL-Z", ["pack_size"] = 10, ["category_id"] = catId, ["brand_id"] = brandId, ["supplier_id"] = suppId, ["quantity"] = 0, ["unit_purchase_price"] = 10, ["unit_sale_price"] = 15 });
        var pNeg = await env.Db.InsertAsync("products", new Dictionary<string, object?> { ["product_code"] = "BL-N", ["name"] = "BL Neg", ["barcode"] = "BL-N", ["pack_size"] = 10, ["category_id"] = catId, ["brand_id"] = brandId, ["supplier_id"] = suppId, ["quantity"] = -3, ["unit_purchase_price"] = 10, ["unit_sale_price"] = 15 });
        var pUnbatched = await env.Db.InsertAsync("products", new Dictionary<string, object?> { ["product_code"] = "BL-U", ["name"] = "BL Unbatched", ["barcode"] = "BL-U", ["pack_size"] = 10, ["category_id"] = catId, ["brand_id"] = brandId, ["supplier_id"] = suppId, ["quantity"] = 100, ["unit_purchase_price"] = 10, ["unit_sale_price"] = 15 });
        var pMixed = await env.Db.InsertAsync("products", new Dictionary<string, object?> { ["product_code"] = "BL-M", ["name"] = "BL Mixed", ["barcode"] = "BL-M", ["pack_size"] = 10, ["category_id"] = catId, ["brand_id"] = brandId, ["supplier_id"] = suppId, ["quantity"] = 100, ["unit_purchase_price"] = 10, ["unit_sale_price"] = 15 });
        await env.Db.InsertAsync("batches", new Dictionary<string, object?> { ["product_id"] = pMixed, ["batch_number"] = "BM-1", ["expiry_date"] = "2028-12-31", ["quantity"] = 40 });

        // Concurrent baseline initialization -> only 1 initializes, others return 0
        var ledgerSvc = new InventoryLedgerService(env.Db);
        var counts = await Task.WhenAll(
            ledgerSvc.EnsureBaselineInitializedAsync(uid),
            ledgerSvc.EnsureBaselineInitializedAsync(uid),
            ledgerSvc.EnsureBaselineInitializedAsync(uid));
        counts.Count(c => c > 0).Should().Be(1);
        counts.Count(c => c == 0).Should().Be(2);

        // Retry idempotency
        (await ledgerSvc.EnsureBaselineInitializedAsync(uid)).Should().Be(0);

        // Verify quantities untouched
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = pZero })).Should().Be(0);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = pNeg })).Should().Be(-3);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = pUnbatched })).Should().Be(100);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = pMixed })).Should().Be(100);

        // Perform sale of 5 on pUnbatched (100 -> 95)
        var saleCtrl = new SaleInvoiceController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                customer_id = custId,
                date = "2026-11-25",
                invoice_type = "debit",
                payment_method = "cash",
                total_receive = 75,
                items = new[] { new { product_id = pUnbatched, quantity = 5, price = 15 } }
            })
        };
        StatusOf(await saleCtrl.Store()).Should().Be(201);

        var recSvc = new StockReconciliationService(env.Db);
        var recU = await recSvc.ReconcileProductAsync(pUnbatched);
        recU!.StoredStock.Should().Be(95);
        recU.MovementDerivedStock.Should().Be(95);
        recU.HasLedgerMismatch.Should().BeFalse();

        var recM = await recSvc.ReconcileProductAsync(pMixed);
        recM!.StoredStock.Should().Be(100);
        recM.BatchedStock.Should().Be(40);
        recM.UnbatchedStock.Should().Be(60);
        recM.MovementDerivedStock.Should().Be(100);
        recM.HasLedgerMismatch.Should().BeFalse();
    }

    [Fact]
    public async Task Baseline_RealDatabaseCopy_VerifiesAllProducts_And_PostBaselineTransactions()
    {
        var dir = AppContext.BaseDirectory;
        string? realDbPath = null;
        while (!string.IsNullOrEmpty(dir))
        {
            var cand = Path.Combine(dir, "database", "database.sqlite");
            if (File.Exists(cand)) { realDbPath = cand; break; }
            dir = Path.GetDirectoryName(dir);
        }
        if (realDbPath == null) return;

        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_bl_real_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
        var tempDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");

        try
        {
            File.Copy(realDbPath, tempDbPath, overwrite: true);
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
            KarobarDb.FlushSchemaCache();

            var factory = new DbConnectionFactory(explicitSqlitePath: tempDbPath);
            await new DatabaseBootstrapper(factory).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var db = new KarobarDb(explicitSqlitePath: tempDbPath);
            var preSum = await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(quantity), 0) FROM products");
            var preBatchSum = await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(quantity), 0) FROM batches");

            var ledger = new InventoryLedgerService(db);
            var firstCount = await ledger.EnsureBaselineInitializedAsync(1);
            firstCount.Should().BeGreaterThan(3000);
            (await ledger.EnsureBaselineInitializedAsync(1)).Should().Be(0);

            (await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(quantity), 0) FROM products")).Should().Be(preSum);
            (await db.ExecuteScalarAsync<long>("SELECT COALESCE(SUM(quantity), 0) FROM batches")).Should().Be(preBatchSum);

            var recSvc = new StockReconciliationService(db);
            var allRec = await recSvc.ReconcileAllAsync();
            allRec.Should().HaveCountGreaterThanOrEqualTo(14122);
            allRec.Count(r => r.HasLedgerMismatch).Should().Be(0, "all products must match baseline movements before new transactions");

            // 1. Sale on positive unbatched product
            var posProd = await db.QuerySingleOrDefaultAsync("SELECT id, quantity, COALESCE(CAST(unit_sale_price AS REAL), 10) AS usp FROM products WHERE quantity >= 20 AND LOWER(COALESCE(narcotic,'no')) = 'no' AND id NOT IN (SELECT product_id FROM batches) LIMIT 1");
            var posUnbatchedId = Convert.ToInt64(posProd!["id"]);
            var posUsp = Math.Max(1.0, Convert.ToDouble(posProd["usp"]));
            var zeroId = await db.ExecuteScalarAsync<long>("SELECT id FROM products WHERE quantity = 0 AND LOWER(COALESCE(narcotic,'no')) = 'no' LIMIT 1");
            var mixedProd = await db.QuerySingleOrDefaultAsync(@"
                SELECT p.id, COALESCE(CAST(p.unit_sale_price AS REAL), 10) AS usp
                FROM products p
                WHERE LOWER(COALESCE(p.narcotic,'no')) = 'no'
                  AND p.quantity > (SELECT COALESCE(SUM(b.quantity), 0) FROM batches b WHERE b.product_id = p.id)
                  AND EXISTS (SELECT 1 FROM batches b WHERE b.product_id = p.id AND b.quantity >= 10)
                LIMIT 1");
            var mixedId = Convert.ToInt64(mixedProd!["id"]);
            var mixedUsp = Math.Max(1.0, Convert.ToDouble(mixedProd["usp"]));

            var custId = await db.ExecuteScalarAsync<long>("SELECT id FROM customers ORDER BY id ASC LIMIT 1");
            var suppId = await db.ExecuteScalarAsync<long>("SELECT id FROM suppliers ORDER BY id ASC LIMIT 1");

            var prePosQty = Convert.ToInt32(posProd["quantity"]);
            var saleCtrl = new SaleInvoiceController(db)
            {
                ControllerContext = BuildContext(1, new
                {
                    customer_id = custId,
                    date = "2026-11-25",
                    invoice_type = "debit",
                    payment_method = "cash",
                    total_receive = Math.Round(5 * posUsp, 2),
                    items = new[] { new { product_id = posUnbatchedId, quantity = 5, price = posUsp } }
                })
            };
            StatusOf(await saleCtrl.Store()).Should().Be(201);
            (await recSvc.ReconcileProductAsync(posUnbatchedId))!.HasLedgerMismatch.Should().BeFalse();
            (await db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = posUnbatchedId })).Should().Be(prePosQty - 5);

            // 2. Purchase on zero-stock product
            var purchCtrl = new PurchaseInvoiceController(db)
            {
                ControllerContext = BuildContext(1, new
                {
                    supplier_id = suppId,
                    posted_date = "2026-11-25",
                    invoice_number = "BL-TEST-P1",
                    invoice_type = "debit",
                    payment_method = "cash",
                    items = new[] { new { product_id = zeroId, pack_size = 10, pack_quantity = 2, unit_quantity = 20, unit_bonus = 0, pack_purchase_price = 100, unit_purchase_price = 10, pack_sale_price = 150, unit_sale_price = 15 } }
                })
            };
            StatusOf(await purchCtrl.Store()).Should().Be(201);
            var recZero = await recSvc.ReconcileProductAsync(zeroId);
            recZero!.StoredStock.Should().Be(20);
            recZero.MovementDerivedStock.Should().Be(20);
            recZero.HasLedgerMismatch.Should().BeFalse();

            // 3. Batched sale on mixed batch+unbatched product
            var mixedBatch = await db.QuerySingleOrDefaultAsync("SELECT batch_number, expiry_date FROM batches WHERE product_id = @pid AND quantity >= 10 ORDER BY id ASC LIMIT 1", new { pid = mixedId });
            var saleMixedCtrl = new SaleInvoiceController(db)
            {
                ControllerContext = BuildContext(1, new
                {
                    customer_id = custId,
                    date = "2026-11-25",
                    invoice_type = "debit",
                    payment_method = "cash",
                    total_receive = Math.Round(3 * mixedUsp, 2),
                    items = new[] { new { product_id = mixedId, batch_number = mixedBatch!["batch_number"]?.ToString(), expiry = mixedBatch["expiry_date"]?.ToString(), quantity = 3, price = mixedUsp } }
                })
            };
            StatusOf(await saleMixedCtrl.Store()).Should().Be(201);
            (await recSvc.ReconcileProductAsync(mixedId))!.HasLedgerMismatch.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task EdgeCase_6617_BatchExceedsProductStock_171vs181_Baseline_Sale_And_Adjustment()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var uid = await env.SeedAdminAsync();
        var (catId, brandId, suppId, custId) = await env.SeedMasterCatalogAsync();

        // Reproduce exact Product 6617 state: products.quantity = 171, batch 2474 = 181
        var pid = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = "P-6617",
            ["name"] = "BROMALEX TABLET 3MG",
            ["barcode"] = "BC-6617",
            ["pack_size"] = 30,
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = suppId,
            ["quantity"] = 171,
            ["unit_purchase_price"] = 10,
            ["unit_sale_price"] = 15
        });
        var bid = await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = pid,
            ["batch_number"] = "2474",
            ["expiry_date"] = "2030-04-30",
            ["quantity"] = 181
        });

        // 1. Run baseline initialization
        var ledger = new InventoryLedgerService(env.Db);
        var seeded = await ledger.EnsureBaselineInitializedAsync(uid);
        seeded.Should().Be(2); // 1 batch (+181) + 1 unbatched deficit (-10)

        // Verify product & batch quantities were NOT modified
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @pid", new { pid })).Should().Be(171);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM batches WHERE id = @bid", new { bid })).Should().Be(181);

        // Verify movement totals: product = 171, batch = 181, unbatched = -10 with explicit discrepancy reason
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(CAST(quantity_in AS REAL) - CAST(quantity_out AS REAL)), 0) FROM inventory_movements WHERE product_id = @pid", new { pid })).Should().Be(171m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(CAST(quantity_in AS REAL) - CAST(quantity_out AS REAL)), 0) FROM inventory_movements WHERE batch_id = @bid", new { bid })).Should().Be(181m);
        var deficitReason = await env.Db.ExecuteScalarAsync<string>("SELECT reason FROM inventory_movements WHERE product_id = @pid AND batch_id IS NULL", new { pid });
        deficitReason.Should().Contain("discrepancy");

        // Verify reconciliation clearly reports the batch-exceeds-product discrepancy
        var recSvc = new StockReconciliationService(env.Db);
        var rec1 = await recSvc.ReconcileProductAsync(pid);
        rec1!.StoredStock.Should().Be(171);
        rec1.BatchedStock.Should().Be(181);
        rec1.UnbatchedStock.Should().Be(-10);
        rec1.BatchExceedsProductStock.Should().BeTrue();
        rec1.HasLedgerMismatch.Should().BeFalse();

        // 2. Subsequent sale of 5 units from batch 2474 -> product 166, batch 176
        var saleCtrl = new SaleInvoiceController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                customer_id = custId,
                date = "2026-11-25",
                invoice_type = "debit",
                payment_method = "cash",
                total_receive = 75,
                items = new[] { new { product_id = pid, batch_number = "2474", expiry = "2030-04-30", quantity = 5, price = 15 } }
            })
        };
        StatusOf(await saleCtrl.Store()).Should().Be(201);

        var rec2 = await recSvc.ReconcileProductAsync(pid);
        rec2!.StoredStock.Should().Be(166);
        rec2.BatchedStock.Should().Be(176);
        rec2.UnbatchedStock.Should().Be(-10);
        rec2.BatchExceedsProductStock.Should().BeTrue();
        rec2.HasLedgerMismatch.Should().BeFalse();

        // 3. Reconcile via documented StockAdjustment (actual_qty = 166) -> trims batch 176 -> 166 and logs batch trim
        var adjCtrl = new StockAdjustmentController(env.Db)
        {
            ControllerContext = BuildContext(uid, new
            {
                posted_date = "2026-11-25",
                note = "Reconcile legacy batch excess",
                items = new[] { new { product_id = pid, actual_qty = 166 } }
            })
        };
        StatusOf(await adjCtrl.Store()).Should().Be(200);

        var rec3 = await recSvc.ReconcileProductAsync(pid);
        rec3!.StoredStock.Should().Be(166);
        rec3.BatchedStock.Should().Be(166);
        rec3.UnbatchedStock.Should().Be(0);
        rec3.BatchExceedsProductStock.Should().BeFalse();
        rec3.HasLedgerMismatch.Should().BeFalse();
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(CAST(quantity_in AS REAL) - CAST(quantity_out AS REAL)), 0) FROM inventory_movements WHERE batch_id = @bid", new { bid })).Should().Be(166m);
    }
}

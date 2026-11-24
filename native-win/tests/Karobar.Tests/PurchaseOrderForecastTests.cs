using System.Security.Claims;
using FluentAssertions;
using Karobar.Api.Controllers;
using Karobar.Data;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Karobar.Tests;

public class PurchaseOrderForecastTests
{
    private sealed class ForecastEnv : IAsyncDisposable
    {
        public string TempRoot { get; }
        public KarobarDb Db { get; }
        public long AdminId { get; private set; }
        private readonly string? _prevRoot;

        private ForecastEnv(string tempRoot, KarobarDb db, string? prevRoot)
        {
            TempRoot = tempRoot;
            Db = db;
            _prevRoot = prevRoot;
        }

        public static async Task<ForecastEnv> CreateAsync()
        {
            var tempRoot = Path.Combine(AppContext.BaseDirectory, "po_forecast_scratch", $"karobar_pof_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "public"));

            var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);

            var dbPath = Path.Combine(tempRoot, "database", "database.sqlite");
            KarobarDb.FlushSchemaCache();
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: dbPath)).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var db = new KarobarDb(explicitSqlitePath: dbPath);
            var env = new ForecastEnv(tempRoot, db, prevRoot);
            await env.SeedAdminAsync();
            return env;
        }

        private async Task SeedAdminAsync()
        {
            var existingRole = await Db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE LOWER(name) = 'admin' LIMIT 1");
            if (existingRole == null)
            {
                await Db.InsertAsync("roles", new Dictionary<string, object?>
                {
                    ["name"] = "Admin",
                    ["guard_name"] = "sanctum"
                });
            }

            AdminId = await Db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "Forecast Admin",
                ["email"] = "forecast-admin@karobar.local",
                ["password"] = BCrypt.Net.BCrypt.HashPassword("Password123!", 4),
                ["status"] = "active"
            });
            await Db.SyncUserRolesAsync(AdminId, ["Admin"]);
        }

        public PurchaseOrderController CreateController(string queryString = "")
        {
            var ctx = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, AdminId.ToString())
                ], "Sanctum"))
            };
            if (!string.IsNullOrWhiteSpace(queryString))
            {
                ctx.Request.QueryString = new QueryString(queryString.StartsWith('?') ? queryString : "?" + queryString);
            }

            return new PurchaseOrderController(Db)
            {
                ControllerContext = new ControllerContext { HttpContext = ctx }
            };
        }

        public ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", _prevRoot);
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, recursive: true); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private static List<Dictionary<string, object?>> ExtractItems(IActionResult result)
    {
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var payload = ok.Value.Should().BeOfType<Dictionary<string, object?>>().Subject;
        return payload["items"].Should().BeOfType<List<Dictionary<string, object?>>>().Subject;
    }

    [Fact]
    public async Task Forecast_StrictlyFiltersByPrimarySupplierAndMultipleBrands()
    {
        await using var env = await ForecastEnv.CreateAsync();
        var db = env.Db;

        var catId = await db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "Tablets" });
        var supA = await db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "Supplier Alpha" });
        var supB = await db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "Supplier Beta" });
        var brand1 = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "Brand One" });
        var brand2 = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "Brand Two" });
        var brand3 = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "Brand Three" });
        var custId = await db.InsertAsync("customers", new Dictionary<string, object?> { ["name"] = "Walk-in" });

        // Product 1: Primary Supplier Alpha, Brand One — ALSO linked to Supplier Beta in product_suppliers (historical invoice)
        var prod1 = await db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = "P-001",
            ["name"] = "Alpha Brand1 Med",
            ["pack_size"] = 10,
            ["quantity"] = 0,
            ["unit_purchase_price"] = 10m,
            ["pack_purchase_price"] = 100m,
            ["unit_sale_price"] = 15m,
            ["pack_sale_price"] = 150m,
            ["category_id"] = catId,
            ["brand_id"] = brand1,
            ["supplier_id"] = supA,
            ["barcode"] = "BC-001"
        });
        await db.InsertAsync("product_suppliers", new Dictionary<string, object?>
        {
            ["product_id"] = prod1,
            ["supplier_id"] = supB,
            ["is_preferred"] = 0
        });

        // Product 2: Primary Supplier Beta, Brand Two
        var prod2 = await db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = "P-002",
            ["name"] = "Beta Brand2 Med",
            ["pack_size"] = 10,
            ["quantity"] = 0,
            ["unit_purchase_price"] = 20m,
            ["pack_purchase_price"] = 200m,
            ["unit_sale_price"] = 25m,
            ["pack_sale_price"] = 250m,
            ["category_id"] = catId,
            ["brand_id"] = brand2,
            ["supplier_id"] = supB,
            ["barcode"] = "BC-002"
        });

        // Product 3: Primary Supplier Beta, Brand Three
        var prod3 = await db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = "P-003",
            ["name"] = "Beta Brand3 Med",
            ["pack_size"] = 10,
            ["quantity"] = 0,
            ["unit_purchase_price"] = 30m,
            ["pack_purchase_price"] = 300m,
            ["unit_sale_price"] = 35m,
            ["pack_sale_price"] = 350m,
            ["category_id"] = catId,
            ["brand_id"] = brand3,
            ["supplier_id"] = supB,
            ["barcode"] = "BC-003"
        });

        var saleId = await db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = env.AdminId,
            ["customer_id"] = custId,
            ["posted_number"] = "SI-1",
            ["date"] = "2026-10-15",
            ["status"] = "posted",
            ["total"] = 1000m,
            ["gross_total"] = 1000m
        });

        foreach (var pid in new[] { prod1, prod2, prod3 })
        {
            await db.InsertAsync("sale_invoice_items", new Dictionary<string, object?>
            {
                ["sale_invoice_id"] = saleId,
                ["product_id"] = pid,
                ["pack_size"] = 10,
                ["quantity"] = 20,
                ["bonus"] = 0,
                ["price"] = 15m,
                ["sub_total"] = 300m
            });
        }

        // 1) Filter strictly by Supplier Beta (default include_secondary_suppliers = false):
        //    Product 1 (primary Supplier Alpha, secondary Supplier Beta) must NOT leak in!
        var strictSupResult = await env.CreateController().Forecast(
            date_from: "2026-10-01",
            date_to: "2026-10-30",
            projected_days: 7,
            supplier_id: supB);
        var strictSupItems = ExtractItems(strictSupResult);
        strictSupItems.Select(x => Convert.ToInt64(x["product_id"])).Should().BeEquivalentTo(new[] { prod2, prod3 });

        // 2) Opt-in include_secondary_suppliers = true includes secondary link Product 1 as well
        var altSupResult = await env.CreateController().Forecast(
            date_from: "2026-10-01",
            date_to: "2026-10-30",
            projected_days: 7,
            supplier_id: supB,
            include_secondary_suppliers: true);
        var altSupItems = ExtractItems(altSupResult);
        altSupItems.Select(x => Convert.ToInt64(x["product_id"])).Should().BeEquivalentTo(new[] { prod1, prod2, prod3 });

        // 3) Bracketed Axios-style query string ?brand_ids[]=brand1&brand_ids[]=brand2
        //    Must filter ONLY Brand 1 & Brand 2, excluding Brand 3!
        var bracketBrandController = env.CreateController($"?brand_ids[]={brand1}&brand_ids[]={brand2}");
        var bracketBrandResult = await bracketBrandController.Forecast(
            date_from: "2026-10-01",
            date_to: "2026-10-30",
            projected_days: 7);
        var bracketBrandItems = ExtractItems(bracketBrandResult);
        bracketBrandItems.Select(x => Convert.ToInt64(x["product_id"])).Should().BeEquivalentTo(new[] { prod1, prod2 });

        // 4) Combined Supplier Beta + Brand Two must return ONLY Product 2
        var combinedController = env.CreateController($"?brand_ids={brand2}");
        var combinedResult = await combinedController.Forecast(
            date_from: "2026-10-01",
            date_to: "2026-10-30",
            projected_days: 7,
            supplier_id: supB);
        var combinedItems = ExtractItems(combinedResult);
        combinedItems.Select(x => Convert.ToInt64(x["product_id"])).Should().Equal(new[] { prod2 });
    }

    [Fact]
    public async Task Forecast_CalculatesExactSuggestedPacks_WithDemandsReturnsOpenPOsAndPrecision()
    {
        await using var env = await ForecastEnv.CreateAsync();
        var db = env.Db;

        var catId = await db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "General" });
        var supId = await db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "Main Dist" });
        var brandId = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "Core Brand" });
        var custId = await db.InsertAsync("customers", new Dictionary<string, object?> { ["name"] = "Ali" });

        // P1: Exact 7-day precision test (30 units sold in 7 days, proj=7, pack=10, stock=0, safety=0 -> MUST be 3 packs, not 4)
        var pExact = await db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = "P-EXACT",
            ["name"] = "Exact Precision Product",
            ["pack_size"] = 10,
            ["quantity"] = 0,
            ["unit_purchase_price"] = 10m,
            ["pack_purchase_price"] = 100m,
            ["unit_sale_price"] = 15m,
            ["pack_sale_price"] = 150m,
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = supId,
            ["barcode"] = "BC-EXACT"
        });

        // P2: Demand-only item (0 sales, 50 units pending user demand, pack=10, stock=0 -> MUST suggest 5 packs, NOT scaled by proj/days)
        var pDemandOnly = await db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = "P-DEMAND",
            ["name"] = "Customer Demand Only",
            ["pack_size"] = 10,
            ["quantity"] = 0,
            ["unit_purchase_price"] = 10m,
            ["pack_purchase_price"] = 100m,
            ["unit_sale_price"] = 15m,
            ["pack_sale_price"] = 150m,
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = supId,
            ["barcode"] = "BC-DEMAND"
        });

        // P3: Open PO deduction test (40 units sold in 7 days -> needs 40 units; 20 units already on open PO -> needs 2 packs with deduct_on_order=true, 4 packs when false)
        var pOnOrder = await db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = "P-ONORDER",
            ["name"] = "Open PO Product",
            ["pack_size"] = 10,
            ["quantity"] = 0,
            ["unit_purchase_price"] = 10m,
            ["pack_purchase_price"] = 100m,
            ["unit_sale_price"] = 15m,
            ["pack_sale_price"] = 150m,
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = supId,
            ["barcode"] = "BC-ONORDER"
        });

        // Posted sale invoice in 7-day range: 2026-10-01 .. 2026-10-07 (7 days inclusive)
        var postedSaleId = await db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = env.AdminId,
            ["customer_id"] = custId,
            ["posted_number"] = "SI-POSTED",
            ["date"] = "2026-10-03",
            ["status"] = "posted",
            ["total"] = 1000m,
            ["gross_total"] = 1000m
        });
        await db.InsertAsync("sale_invoice_items", new Dictionary<string, object?>
        {
            ["sale_invoice_id"] = postedSaleId,
            ["product_id"] = pExact,
            ["pack_size"] = 10,
            ["quantity"] = 30,
            ["bonus"] = 0,
            ["price"] = 15m,
            ["sub_total"] = 450m
        });
        await db.InsertAsync("sale_invoice_items", new Dictionary<string, object?>
        {
            ["sale_invoice_id"] = postedSaleId,
            ["product_id"] = pOnOrder,
            ["pack_size"] = 10,
            ["quantity"] = 40,
            ["bonus"] = 0,
            ["price"] = 15m,
            ["sub_total"] = 600m
        });

        // Draft/Reversed sale invoice must be IGNORED
        var draftSaleId = await db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = env.AdminId,
            ["customer_id"] = custId,
            ["posted_number"] = "SI-DRAFT",
            ["date"] = "2026-10-04",
            ["status"] = "draft",
            ["total"] = 900m,
            ["gross_total"] = 900m
        });
        await db.InsertAsync("sale_invoice_items", new Dictionary<string, object?>
        {
            ["sale_invoice_id"] = draftSaleId,
            ["product_id"] = pExact,
            ["pack_size"] = 10,
            ["quantity"] = 999,
            ["bonus"] = 0,
            ["price"] = 15m,
            ["sub_total"] = 14985m
        });

        // Pending user demand for 50 units on pDemandOnly
        await db.InsertAsync("user_demands", new Dictionary<string, object?>
        {
            ["product_id"] = pDemandOnly,
            ["customer_id"] = custId,
            ["requested_by"] = env.AdminId,
            ["requested_name"] = "Ali",
            ["requested_quantity"] = 50,
            ["status"] = "pending"
        });

        // Open Purchase Order with 20 units of pOnOrder
        var poId = await db.InsertAsync("purchase_orders", new Dictionary<string, object?>
        {
            ["supplier_id"] = supId,
            ["number"] = "PO-00001",
            ["date"] = "2026-10-05",
            ["expected_received_date"] = "2026-10-10",
            ["status"] = "ordered"
        });
        await db.InsertAsync("purchase_order_items", new Dictionary<string, object?>
        {
            ["purchase_order_id"] = poId,
            ["product_id"] = pOnOrder,
            ["pack_size"] = 10,
            ["ordered_quantity"] = 20,
            ["unit_purchase_price"] = 10m
        });

        // Run with deduct_on_order = true (default)
        var resDeduct = await env.CreateController().Forecast(
            date_from: "2026-10-01",
            date_to: "2026-10-07",
            projected_days: 7,
            safety_packs: 0,
            moq_packs: 0,
            deduct_on_order: true);
        var byPid = ExtractItems(resDeduct).ToDictionary(x => Convert.ToInt64(x["product_id"]));

        // Exact precision & draft ignored
        Convert.ToInt32(byPid[pExact]["units_sold"]).Should().Be(30);
        Convert.ToInt32(byPid[pExact]["suggested_packs"]).Should().Be(3);

        // Demand-only: units_sold = 0, user_demand_quantity = 50, suggested_packs = 5
        Convert.ToInt32(byPid[pDemandOnly]["units_sold"]).Should().Be(0);
        Convert.ToInt32(byPid[pDemandOnly]["user_demand_quantity"]).Should().Be(50);
        Convert.ToInt32(byPid[pDemandOnly]["suggested_packs"]).Should().Be(5);

        // Open PO deducted: 40 needed - 20 on order = 20 due = 2 packs
        Convert.ToInt32(byPid[pOnOrder]["on_order_units"]).Should().Be(20);
        Convert.ToInt32(byPid[pOnOrder]["suggested_packs"]).Should().Be(2);

        // Run with deduct_on_order = false -> 40 needed = 4 packs
        var resNoDeduct = await env.CreateController().Forecast(
            date_from: "2026-10-01",
            date_to: "2026-10-07",
            projected_days: 7,
            safety_packs: 0,
            moq_packs: 0,
            deduct_on_order: false);
        var byPidNoDeduct = ExtractItems(resNoDeduct).ToDictionary(x => Convert.ToInt64(x["product_id"]));
        Convert.ToInt32(byPidNoDeduct[pOnOrder]["on_order_units"]).Should().Be(20);
        Convert.ToInt32(byPidNoDeduct[pOnOrder]["suggested_packs"]).Should().Be(4);
    }
}

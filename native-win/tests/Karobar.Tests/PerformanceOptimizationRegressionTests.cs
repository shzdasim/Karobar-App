using System.Security.Claims;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Karobar.Api.Controllers;
using Karobar.Data;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Karobar.Tests;

public class PerformanceOptimizationRegressionTests
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
            var tempRoot = Path.Combine(AppContext.BaseDirectory, "perf_test_scratch", $"karobar_perf_{Guid.NewGuid():N}");
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

        public ValueTask DisposeAsync()
        {
            KarobarDb.FlushSchemaCache();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", _prevRootEnv);
            try
            {
                if (Directory.Exists(TempRoot))
                {
                    Directory.Delete(TempRoot, recursive: true);
                }
            }
            catch
            {
            }
            return ValueTask.CompletedTask;
        }
    }

    private static ReportsController CreateReportsController(KarobarDb db, long adminUserId, string queryString)
    {
        var pdfGen = new PdfDocumentGenerator();
        var controller = new ReportsController(db, pdfGen);
        var ctx = new DefaultHttpContext();
        ctx.Items["KarobarUserId"] = adminUserId;
        ctx.Request.QueryString = new QueryString(queryString);
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, adminUserId.ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        }, "Sanctum"));
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };
        return controller;
    }

    private static async Task<long> SeedAdminUserAsync(KarobarDb db)
    {
        var roleId = await db.InsertAsync("roles", new Dictionary<string, object?>
        {
            ["name"] = "Admin",
            ["guard_name"] = "sanctum"
        });
        var userId = await db.InsertAsync("users", new Dictionary<string, object?>
        {
            ["name"] = "Perf Admin",
            ["email"] = "perfadmin@example.com",
            ["password"] = "hash",
            ["status"] = "active"
        });
        await db.ExecuteAsync(
            "INSERT INTO model_has_roles (role_id, model_type, model_id) VALUES (@roleId, 'App\\Models\\User', @userId)",
            new { roleId, userId });
        return userId;
    }

    [Fact]
    public async Task Phase3_PerformanceIndexes_AreCreatedOnBootstrap()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        await using var conn = await env.Db.OpenConnectionAsync();
        var indexes = (await conn.QueryAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='index' AND sql IS NOT NULL"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        indexes.Should().Contain("IX_sale_invoices_date_status");
        indexes.Should().Contain("IX_purchase_invoices_posted_date_status");
        indexes.Should().Contain("IX_products_name_nocase");
        indexes.Should().Contain("IX_batches_expiry_qty");
    }

    [Fact]
    public async Task Phase1_SaleDetail_BatchedOver450Invoices_PreservesOrderTotalsAndExcludesReversed()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await SeedAdminUserAsync(env.Db);

        var catId = await env.Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "Perf Cat" });
        var brandId = await env.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "Perf Brand" });
        var supId = await env.Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "Perf Sup" });
        var custId = await env.Db.InsertAsync("customers", new Dictionary<string, object?>
        {
            ["name"] = "Walk-in Perf",
            ["credit_status"] = "active"
        });
        var prodId = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["name"] = "PANADOL 500MG",
            ["product_code"] = "PRD-PERF-1",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = supId,
            ["pack_size"] = 10,
            ["quantity"] = 5000,
            ["unit_sale_price"] = 10.0,
            ["pack_sale_price"] = 100.0
        });

        await using var conn = await env.Db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);
        const int activeCount = 450; // Exceeds SqliteInChunkSize (400) to test chunking
        for (int i = 1; i <= activeCount; i++)
        {
            var invId = await env.Db.InsertAsync("sale_invoices", new Dictionary<string, object?>
            {
                ["user_id"] = adminId,
                ["customer_id"] = custId,
                ["posted_number"] = $"SI-PERF-{i:D4}",
                ["date"] = "2026-11-20",
                ["sale_type"] = "retail",
                ["invoice_type"] = "debit",
                ["gross_amount"] = 30.0,
                ["total"] = 30.0,
                ["total_receive"] = 30.0
            }, conn, tx);

            for (int j = 1; j <= 2; j++)
            {
                await env.Db.InsertAsync("sale_invoice_items", new Dictionary<string, object?>
                {
                    ["sale_invoice_id"] = invId,
                    ["product_id"] = prodId,
                    ["pack_size"] = 10,
                    ["quantity"] = j,
                    ["bonus"] = 0,
                    ["price"] = 10.0,
                    ["sub_total"] = j * 10.0
                }, conn, tx);
            }
        }

        // Reversed invoice must be excluded
        var revId = await env.Db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = adminId,
            ["customer_id"] = custId,
            ["posted_number"] = "SI-PERF-REV",
            ["date"] = "2026-11-20",
            ["status"] = "reversed",
            ["total"] = 999.0
        }, conn, tx);
        await env.Db.InsertAsync("sale_invoice_items", new Dictionary<string, object?>
        {
            ["sale_invoice_id"] = revId,
            ["product_id"] = prodId,
            ["pack_size"] = 10,
            ["quantity"] = 5,
            ["price"] = 10.0,
            ["sub_total"] = 50.0
        }, conn, tx);
        await tx.CommitAsync();

        var controller = CreateReportsController(env.Db, adminId, "?from=2026-11-20&to=2026-11-20");
        var res = await controller.SaleDetail();
        var ok = res.Should().BeOfType<OkObjectResult>().Subject;
        var rows = ok.Value.Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;

        rows.Should().HaveCount(activeCount);
        rows.Select(r => Convert.ToInt64(r["id"])).Should().BeInAscendingOrder();
        rows.Sum(r => Convert.ToDouble(r["total"])).Should().BeApproximately(activeCount * 30.0, 0.01);

        var firstItems = rows[0]["items"].Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;
        firstItems.Should().HaveCount(2);
        Convert.ToInt32(firstItems[0]["quantity"]).Should().Be(1);
        Convert.ToInt32(firstItems[1]["quantity"]).Should().Be(2);
        firstItems[0]["product_name"]?.ToString().Should().Be("PANADOL 500MG");
    }

    [Fact]
    public async Task Phase3_SargableDateFilters_IncludeMixedDateAndTimestampFormatsOnEndBoundary()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await SeedAdminUserAsync(env.Db);

        var catId = await env.Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "Date Cat" });
        var brandId = await env.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "Date Brand" });
        var supId = await env.Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "Sup Perf" });
        var prodId = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["name"] = "TEST TAB",
            ["product_code"] = "PRD-DATE-1",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = supId,
            ["pack_size"] = 10,
            ["quantity"] = 100
        });

        // One stock adjustment with YYYY-MM-DD and one with YYYY-MM-DD 00:00:00 on the exact `to` boundary
        var adj1 = await env.Db.InsertAsync("stock_adjustments", new Dictionary<string, object?>
        {
            ["posted_number"] = "STADJ-D1",
            ["posted_date"] = "2026-11-24",
            ["note"] = "Date only",
            ["total_worth"] = 10.0,
            ["user_id"] = adminId
        });
        await env.Db.InsertAsync("stock_adjustment_items", new Dictionary<string, object?>
        {
            ["stock_adjustment_id"] = adj1,
            ["product_id"] = prodId,
            ["pack_size"] = 10,
            ["previous_qty"] = 100,
            ["actual_qty"] = 101,
            ["diff_qty"] = 1,
            ["unit_purchase_price"] = 10.0,
            ["worth_adjusted"] = 10.0
        });

        var adj2 = await env.Db.InsertAsync("stock_adjustments", new Dictionary<string, object?>
        {
            ["posted_number"] = "STADJ-D2",
            ["posted_date"] = "2026-11-24 00:00:00",
            ["note"] = "Timestamp on boundary",
            ["total_worth"] = 20.0,
            ["user_id"] = adminId
        });
        await env.Db.InsertAsync("stock_adjustment_items", new Dictionary<string, object?>
        {
            ["stock_adjustment_id"] = adj2,
            ["product_id"] = prodId,
            ["pack_size"] = 10,
            ["previous_qty"] = 101,
            ["actual_qty"] = 103,
            ["diff_qty"] = 2,
            ["unit_purchase_price"] = 10.0,
            ["worth_adjusted"] = 20.0
        });

        // One purchase return with timestamp on boundary
        var prId = await env.Db.InsertAsync("purchase_returns", new Dictionary<string, object?>
        {
            ["supplier_id"] = supId,
            ["posted_number"] = "PR-D1",
            ["date"] = "2026-11-24 14:30:00",
            ["gross_total"] = 50.0,
            ["total"] = 50.0
        });
        await env.Db.InsertAsync("purchase_return_items", new Dictionary<string, object?>
        {
            ["purchase_return_id"] = prId,
            ["product_id"] = prodId,
            ["pack_size"] = 10,
            ["return_pack_quantity"] = 0,
            ["return_unit_quantity"] = 5,
            ["unit_purchase_price"] = 10.0,
            ["sub_total"] = 50.0
        });

        var adjCtrl = CreateReportsController(env.Db, adminId, "?from=2026-11-24&to=2026-11-24");
        var adjOk = (await adjCtrl.StockAdjustment()).Should().BeOfType<OkObjectResult>().Subject;
        var adjDict = adjOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
        var adjRows = adjDict["rows"].Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;
        adjRows.Should().HaveCount(2);

        var prCtrl = CreateReportsController(env.Db, adminId, "?from=2026-11-24&to=2026-11-24");
        var prOk = (await prCtrl.PurchaseReturns()).Should().BeOfType<OkObjectResult>().Subject;
        var prDict = prOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
        var prRows = prDict["rows"].Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;
        prRows.Should().HaveCount(1);
    }
}

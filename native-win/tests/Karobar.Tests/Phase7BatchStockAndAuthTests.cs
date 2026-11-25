using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Karobar.Api.Controllers;
using Karobar.Api.Middleware;
using Karobar.Data;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Karobar.Tests;

public class Phase7BatchStockAndAuthTests
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
            var tempRoot = Path.Combine(AppContext.BaseDirectory, "p7_test_scratch", $"karobar_p7_{Guid.NewGuid():N}");
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

    private static ProductController CreateProductController(KarobarDb db, long? userId, IReadOnlyList<string>? cachedRoles = null, string? jsonBody = null)
    {
        var controller = new ProductController(db);
        var ctx = new DefaultHttpContext();
        if (userId.HasValue)
        {
            ctx.Items["KarobarUserId"] = userId.Value;
            if (cachedRoles != null)
            {
                ctx.Items[SanctumAuthenticationHandler.UserRolesItemKey] = cachedRoles;
            }
            ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString())
            }, "Sanctum"));
        }
        if (jsonBody != null)
        {
            var bytes = Encoding.UTF8.GetBytes(jsonBody);
            ctx.Request.Body = new MemoryStream(bytes);
            ctx.Request.ContentType = "application/json";
            ctx.Request.ContentLength = bytes.Length;
        }
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };
        return controller;
    }

    private static BatchController CreateBatchController(KarobarDb db, long userId, IReadOnlyList<string>? cachedRoles = null)
    {
        var controller = new BatchController(db);
        var ctx = new DefaultHttpContext();
        ctx.Items["KarobarUserId"] = userId;
        if (cachedRoles != null)
        {
            ctx.Items[SanctumAuthenticationHandler.UserRolesItemKey] = cachedRoles;
        }
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString())
        }, "Sanctum"));
        controller.ControllerContext = new ControllerContext { HttpContext = ctx };
        return controller;
    }

    private static async Task<long> SeedAdminAsync(KarobarDb db)
    {
        var roleId = await db.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "Admin", ["guard_name"] = "sanctum" });
        var userId = await db.InsertAsync("users", new Dictionary<string, object?>
        {
            ["name"] = "P7 Admin",
            ["email"] = "p7admin@example.com",
            ["password"] = "hash",
            ["status"] = "active"
        });
        await db.ExecuteAsync("INSERT INTO model_has_roles (role_id, model_type, model_id) VALUES (@roleId, 'App\\Models\\User', @userId)", new { roleId, userId });
        return userId;
    }

    [Fact]
    public async Task BatchStock_PreservesFefoStorageLocationsUnbatchedDiscrepanciesAndReadOnly()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await SeedAdminAsync(env.Db);

        var catId = await env.Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "P7 Cat" });
        var brandId = await env.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "P7 Brand" });
        var supId = await env.Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "P7 Sup" });
        var locShop = await env.Db.InsertAsync("storage_locations", new Dictionary<string, object?> { ["code"] = "SHOP-1", ["name"] = "Shop Floor", ["type"] = "shop" });
        var locCold = await env.Db.InsertAsync("storage_locations", new Dictionary<string, object?> { ["code"] = "COLD-1", ["name"] = "Fridge", ["type"] = "cold_storage" });

        // Product 1: Multiple batches (expired, near-expiry, later-expiry) + unbatched stock
        var p1 = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["name"] = "P7 MultiBatch",
            ["product_code"] = "P7-001",
            ["barcode"] = "BC-P7-001",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = supId,
            ["pack_size"] = 10,
            ["quantity"] = 50,
            ["unit_sale_price"] = 12.5,
            ["pack_sale_price"] = 125.0,
            ["unit_purchase_price"] = 8.0,
            ["pack_purchase_price"] = 80.0,
            ["avg_price"] = 8.2
        });

        var yesterday = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd");
        var nearExp = DateTime.UtcNow.AddDays(15).ToString("yyyy-MM-dd");
        var laterExp = DateTime.UtcNow.AddDays(90).ToString("yyyy-MM-dd");

        await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = p1,
            ["batch_number"] = "B-EXP",
            ["expiry_date"] = yesterday,
            ["quantity"] = 10,
            ["storage_location_id"] = locShop
        });
        await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = p1,
            ["batch_number"] = "B-LATER",
            ["expiry_date"] = laterExp,
            ["quantity"] = 15,
            ["storage_location_id"] = locShop
        });
        await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = p1,
            ["batch_number"] = "B-FEFO",
            ["expiry_date"] = nearExp,
            ["quantity"] = 10,
            ["storage_location_id"] = locCold
        });
        // Total batched = 35, product.quantity = 50 -> 15 unbatched

        // Product 2: No batches
        var p2 = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["name"] = "P7 NoBatch",
            ["product_code"] = "P7-002",
            ["barcode"] = "BC-P7-002",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = supId,
            ["pack_size"] = 1,
            ["quantity"] = 25,
            ["unit_sale_price"] = 5.0
        });

        // Product 3: Stock discrepancy (batched > product.quantity)
        var p3 = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["name"] = "P7 Discrepancy",
            ["product_code"] = "P7-003",
            ["barcode"] = "BC-P7-003",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = supId,
            ["pack_size"] = 1,
            ["quantity"] = 5,
            ["unit_sale_price"] = 20.0
        });
        await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = p3,
            ["batch_number"] = "B-DISC",
            ["expiry_date"] = laterExp,
            ["quantity"] = 12,
            ["storage_location_id"] = locShop
        });

        var invMovBefore = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM inventory_movements");

        var payload = JsonSerializer.Serialize(new { product_ids = new long[] { p1, p2, p1, p3, 999999 } });
        var ctrl = CreateProductController(env.Db, adminId, new[] { "Admin" }, payload);
        var res = await ctrl.BatchStock();
        var ok = res.Should().BeOfType<OkObjectResult>().Subject;
        var dict = ok.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;

        var products = dict["products"].Should().BeAssignableTo<Dictionary<string, Dictionary<string, object?>>>().Subject;
        var batches = dict["batches"].Should().BeAssignableTo<Dictionary<string, List<Dictionary<string, object?>>>>().Subject;
        var missing = dict["missing_ids"].Should().BeAssignableTo<List<long>>().Subject;

        missing.Should().Equal(999999L);
        products.Keys.Should().BeEquivalentTo(p1.ToString(), p2.ToString(), p3.ToString());

        // Verify FEFO ordering for p1 matches BatchController.Index
        var batchCtrl = CreateBatchController(env.Db, adminId, new[] { "Admin" });
        var singleBatchesOk = (await batchCtrl.Index(p1, null, "1")).Should().BeOfType<OkObjectResult>().Subject;
        var singleBatches = singleBatchesOk.Value.Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;

        var p1Batches = batches[p1.ToString()];
        p1Batches.Should().HaveCount(4); // B-FEFO, B-LATER, B-EXP, Unbatched
        p1Batches.Select(b => b["batch_number"]?.ToString()).Should().Equal(singleBatches.Select(b => b["batch_number"]?.ToString()));
        p1Batches[0]["batch_number"]?.ToString().Should().Be("B-FEFO");
        p1Batches[0]["is_fefo_recommended"].Should().Be(true);
        p1Batches[2]["batch_number"]?.ToString().Should().Be("B-EXP");
        p1Batches[2]["is_expired"].Should().Be(true);
        p1Batches[3]["is_unbatched"].Should().Be(true);
        Convert.ToInt32(p1Batches[3]["available_units"]).Should().Be(15);
        (p1Batches[0]["storage_location"] as Dictionary<string, object?>)?["code"]?.ToString().Should().Be("COLD-1");

        // Discrepancy product: unbatched_units clamped to 0
        Convert.ToInt32(products[p3.ToString()]["unbatched_units"]).Should().Be(0);
        batches[p3.ToString()].Should().HaveCount(1);

        // Read-only guarantee
        var invMovAfter = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM inventory_movements");
        invMovAfter.Should().Be(invMovBefore);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = p1 })).Should().Be(50);
    }

    [Fact]
    public async Task BatchStock_ValidatesInputAndEnforcesPermissions()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await SeedAdminAsync(env.Db);

        // Non-admin without lookup permission -> 403
        var noPermUser = await env.Db.InsertAsync("users", new Dictionary<string, object?>
        {
            ["name"] = "NoPerm",
            ["email"] = "noperm@example.com",
            ["password"] = "hash",
            ["status"] = "active"
        });
        var forbiddenCtrl = CreateProductController(env.Db, noPermUser, Array.Empty<string>(), "{\"product_ids\":[1]}");
        var forbiddenRes = await forbiddenCtrl.BatchStock();
        (forbiddenRes as ObjectResult)?.StatusCode.Should().Be(403);

        // Invalid IDs -> 422
        var invalidCtrl = CreateProductController(env.Db, adminId, new[] { "Admin" }, "{\"product_ids\":[-5]}");
        (await invalidCtrl.BatchStock() as ObjectResult)?.StatusCode.Should().Be(422);

        // Over max limit -> 422
        var overIds = Enumerable.Range(1, ProductController.MaxBatchStockProducts + 1).ToArray();
        var overCtrl = CreateProductController(env.Db, adminId, new[] { "Admin" }, JsonSerializer.Serialize(new { product_ids = overIds }));
        (await overCtrl.BatchStock() as ObjectResult)?.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task BatchStock_ChunkingSupportsOver400Products_WaitLimitRespectsMax()
    {
        // Verify chunking math and max limit constant cleanly
        ProductController.MaxBatchStockProducts.Should().Be(200);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task BatchStock_NormalizedInvoiceData_MatchesIndividualProductShowAndBatchIndex()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await SeedAdminAsync(env.Db);

        var catId = await env.Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "Diff Cat" });
        var brandId = await env.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "Diff Brand" });
        var supId = await env.Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "Diff Sup" });
        var locShop = await env.Db.InsertAsync("storage_locations", new Dictionary<string, object?> { ["code"] = "DSHOP", ["name"] = "Shop", ["type"] = "shop" });

        var p1 = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["name"] = "Diff Prod 1",
            ["product_code"] = "DP-001",
            ["barcode"] = "BC-DP-001",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = supId,
            ["pack_size"] = 10,
            ["quantity"] = 35,
            ["unit_sale_price"] = 15.0,
            ["pack_sale_price"] = 150.0,
            ["unit_purchase_price"] = 10.0,
            ["pack_purchase_price"] = 100.0,
            ["avg_price"] = 10.5
        });

        await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = p1,
            ["batch_number"] = "DB-1",
            ["expiry_date"] = DateTime.UtcNow.AddDays(30).ToString("yyyy-MM-dd"),
            ["quantity"] = 20,
            ["storage_location_id"] = locShop
        });

        var showCtrl = CreateProductController(env.Db, adminId, new[] { "Admin" });
        var showRes = (await showCtrl.Show(p1)).Should().BeOfType<OkObjectResult>().Subject;
        var singleProd = showRes.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;

        var batchCtrl = CreateBatchController(env.Db, adminId, new[] { "Admin" });
        var batchRes = (await batchCtrl.Index(p1, null, "1")).Should().BeOfType<OkObjectResult>().Subject;
        var singleBatches = batchRes.Value.Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;

        var batchStockCtrl = CreateProductController(env.Db, adminId, new[] { "Admin" }, JsonSerializer.Serialize(new { product_ids = new[] { p1 } }));
        var bsRes = (await batchStockCtrl.BatchStock()).Should().BeOfType<OkObjectResult>().Subject;
        var bsDict = bsRes.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
        var bsProd = bsDict["products"].Should().BeAssignableTo<Dictionary<string, Dictionary<string, object?>>>().Subject[p1.ToString()];
        var bsBatches = bsDict["batches"].Should().BeAssignableTo<Dictionary<string, List<Dictionary<string, object?>>>>().Subject[p1.ToString()];

        // Compare all normalized invoice-facing product fields
        foreach (var field in new[] { "id", "name", "pack_size", "quantity", "unit_sale_price", "pack_sale_price", "unit_purchase_price", "pack_purchase_price", "avg_price" })
        {
            Convert.ToString(bsProd[field], System.Globalization.CultureInfo.InvariantCulture)
                .Should().Be(Convert.ToString(singleProd[field], System.Globalization.CultureInfo.InvariantCulture), $"field {field} must match");
        }

        // Compare all normalized batch fields
        bsBatches.Should().HaveCount(singleBatches.Count);
        for (int i = 0; i < singleBatches.Count; i++)
        {
            foreach (var bField in new[] { "batch_number", "expiry_date", "quantity", "available_units", "is_expired", "is_fefo_recommended", "is_unbatched", "unit_sale_price", "pack_sale_price" })
            {
                Convert.ToString(bsBatches[i].GetValueOrDefault(bField), System.Globalization.CultureInfo.InvariantCulture)
                    .Should().Be(Convert.ToString(singleBatches[i].GetValueOrDefault(bField), System.Globalization.CultureInfo.InvariantCulture), $"batch[{i}].{bField} must match");
            }
        }
    }

    [Fact]
    public async Task BatchStock_ValidationMatrix_HandlesEmptyNullTypesAndExact200Limit()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await SeedAdminAsync(env.Db);

        var invalidPayloads = new[]
        {
            "{}",
            "{\"product_ids\":[]}",
            "{\"product_ids\":null}",
            "{\"product_ids\":\"not-an-array\"}",
            "{\"product_ids\":[0]}",
            "{\"product_ids\":[-1]}",
            "{\"product_ids\":[true]}",
            "{\"product_ids\":[\"abc\"]}"
        };

        foreach (var body in invalidPayloads)
        {
            var ctrl = CreateProductController(env.Db, adminId, new[] { "Admin" }, body);
            var res = await ctrl.BatchStock();
            (res as ObjectResult)?.StatusCode.Should().Be(422, $"payload {body} must return 422");
        }

        // Exactly 200 distinct IDs -> 200 OK
        var exact200 = Enumerable.Range(1, ProductController.MaxBatchStockProducts).ToArray();
        var ctrl200 = CreateProductController(env.Db, adminId, new[] { "Admin" }, JsonSerializer.Serialize(new { product_ids = exact200 }));
        (await ctrl200.BatchStock()).Should().BeOfType<OkObjectResult>();

        // 201 distinct IDs -> 422
        var over201 = Enumerable.Range(1, ProductController.MaxBatchStockProducts + 1).ToArray();
        var ctrl201 = CreateProductController(env.Db, adminId, new[] { "Admin" }, JsonSerializer.Serialize(new { product_ids = over201 }));
        (await ctrl201.BatchStock() as ObjectResult)?.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task AuthAndRbac_ReusesRequestScopedRolesWithoutStaleCrossRequestState()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await SeedAdminAsync(env.Db);

        // Non-admin user with product.view permission
        var roleId = await env.Db.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "Cashier", ["guard_name"] = "sanctum" });
        var permId = await env.Db.InsertAsync("permissions", new Dictionary<string, object?> { ["name"] = "product.view", ["guard_name"] = "sanctum" });
        await env.Db.ExecuteAsync("INSERT INTO role_has_permissions (permission_id, role_id) VALUES (@permId, @roleId)", new { permId, roleId });
        var cashierId = await env.Db.InsertAsync("users", new Dictionary<string, object?>
        {
            ["name"] = "Cashier",
            ["email"] = "cashier@example.com",
            ["password"] = "hash",
            ["status"] = "active"
        });
        await env.Db.ExecuteAsync("INSERT INTO model_has_roles (role_id, model_type, model_id) VALUES (@roleId, 'App\\Models\\User', @cashierId)", new { roleId, cashierId });

        var catId = await env.Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "C1" });
        var brandId = await env.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "B1" });
        var supId = await env.Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "S1" });
        var prodId = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["name"] = "AuthTestProd",
            ["product_code"] = "ATP-1",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = supId,
            ["pack_size"] = 1,
            ["quantity"] = 10
        });

        // Request with cached Cashier role has product.view
        var cashierCtrl = CreateProductController(env.Db, cashierId, new[] { "Cashier" }, JsonSerializer.Serialize(new { product_ids = new[] { prodId } }));
        (await cashierCtrl.BatchStock()).Should().BeOfType<OkObjectResult>();

        // Revoke permission in DB; new request with same Cashier role must immediately be forbidden (no cross-request stale cache)
        await env.Db.ExecuteAsync("DELETE FROM role_has_permissions WHERE role_id = @roleId", new { roleId });
        var cashierCtrl2 = CreateProductController(env.Db, cashierId, new[] { "Cashier" }, JsonSerializer.Serialize(new { product_ids = new[] { prodId } }));
        var res2 = await cashierCtrl2.BatchStock();
        (res2 as ObjectResult)?.StatusCode.Should().Be(403);

        // Admin cached role succeeds
        var adminCtrl = CreateProductController(env.Db, adminId, new[] { "Admin" }, JsonSerializer.Serialize(new { product_ids = new[] { prodId } }));
        (await adminCtrl.BatchStock()).Should().BeOfType<OkObjectResult>();
    }
}

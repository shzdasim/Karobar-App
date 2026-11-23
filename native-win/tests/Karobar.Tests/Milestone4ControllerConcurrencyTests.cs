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

public class Milestone4ControllerConcurrencyTests
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
            var tempRoot = Path.Combine(AppContext.BaseDirectory, "m4d_test_scratch", $"karobar_m4d_{Guid.NewGuid():N}");
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

        public async Task<long> EnsureRoleAsync(string roleName)
        {
            var existing = await Db.QuerySingleOrDefaultAsync(
                "SELECT id FROM roles WHERE LOWER(name) = LOWER(@name) LIMIT 1",
                new { name = roleName });
            if (existing != null) return Convert.ToInt64(existing["id"]);

            return await Db.InsertAsync("roles", new Dictionary<string, object?>
            {
                ["name"] = roleName,
                ["guard_name"] = "sanctum"
            });
        }

        public async Task<long> SeedAdminAsync(string name = "Admin", string email = "admin@m4d.local")
        {
            await EnsureRoleAsync("Admin");
            var hash = BCrypt.Net.BCrypt.HashPassword("Password123!", 4);
            var uid = await Db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = name,
                ["email"] = email,
                ["password"] = hash,
                ["status"] = "active"
            });
            await Db.SyncUserRolesAsync(uid, ["Admin"]);
            return uid;
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

    private static DefaultHttpContext MakeJsonHttpContext(long userId, object? payload = null)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            ], "Sanctum"))
        };
        ctx.Items["KarobarUserId"] = userId;
        if (payload != null)
        {
            var json = JsonSerializer.Serialize(payload);
            ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
            ctx.Request.ContentType = "application/json";
        }
        return ctx;
    }

    private static int GetStatus(IActionResult result)
    {
        if (result is IStatusCodeActionResult statusRes && statusRes.StatusCode.HasValue)
        {
            return statusRes.StatusCode.Value;
        }
        return result is NoContentResult ? 204 : 200;
    }

    private static JsonDocument ParseBody(IActionResult result)
    {
        var obj = result as ObjectResult;
        obj.Should().NotBeNull();
        return JsonDocument.Parse(JsonSerializer.Serialize(obj!.Value));
    }

    private static long ExtractId(IActionResult result)
    {
        using var doc = ParseBody(result);
        return doc.RootElement.GetProperty("id").GetInt64();
    }

    private static async Task<(long CashAccountId, long SessionId)> SeedCashAndDrawerAsync(
        IsolatedEnv env,
        long userId,
        decimal balance)
    {
        var existingAcc = await env.Db.QuerySingleOrDefaultAsync(
            "SELECT id FROM accounts WHERE type = 'cash' AND is_default = 1 LIMIT 1");
        long cashId;
        if (existingAcc != null)
        {
            cashId = AccountService.ToLong(existingAcc["id"]);
            await env.Db.UpdateAsync("accounts", cashId, new Dictionary<string, object?>
            {
                ["opening_balance"] = balance,
                ["current_balance"] = balance,
                ["is_active"] = 1
            });
        }
        else
        {
            cashId = await env.Db.InsertAsync("accounts", new Dictionary<string, object?>
            {
                ["name"] = "Cash in Hand",
                ["type"] = "cash",
                ["opening_balance"] = balance,
                ["current_balance"] = balance,
                ["is_default"] = 1,
                ["is_active"] = 1
            });
        }

        var drawerSvc = new CashDrawerService(env.Db);
        var session = await drawerSvc.OpenSessionAsync(userId, balance);
        return (cashId, AccountService.ToLong(session["id"]));
    }

    private static async Task<(long CatId, long BrandId, long SuppId, long CustId, long ProdId)> SeedCatalogAsync(
        IsolatedEnv env,
        int initialQty = 10,
        decimal unitSale = 100m,
        decimal unitPurchase = 50m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var catId = await env.Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = $"CatM4d-{suffix}" });
        var brandId = await env.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = $"BrandM4d-{suffix}" });
        var suppId = await env.Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = $"SuppM4d-{suffix}" });
        var custId = await env.Db.InsertAsync("customers", new Dictionary<string, object?> { ["name"] = $"CustM4d-{suffix}" });
        var prodId = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = $"PRD-{suffix}",
            ["barcode"] = $"BC-{suffix}",
            ["name"] = $"M4d Product {suffix}",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = suppId,
            ["pack_size"] = 1,
            ["quantity"] = initialQty,
            ["pack_purchase_price"] = unitPurchase,
            ["unit_purchase_price"] = unitPurchase,
            ["avg_price"] = unitPurchase,
            ["pack_sale_price"] = unitSale,
            ["unit_sale_price"] = unitSale,
            ["max_discount"] = 100m,
            ["narcotic"] = "no"
        });
        return (catId, brandId, suppId, custId, prodId);
    }

    // =========================================================================
    // Part 1 Tests (SaleReturn, PurchaseReturn, PurchaseOrder, Batch)
    // =========================================================================

    [Fact]
    public async Task FM404c_SaleReturnController_ConcurrentDestroy_OnlyOneSucceeds_NoDoubleRevert()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedAdminAsync();
        var (cashId, sessionId) = await SeedCashAndDrawerAsync(env, adminId, 1000m);
        var (_, _, _, custId, prodId) = await SeedCatalogAsync(env, initialQty: 10, unitSale: 100m, unitPurchase: 50m);

        var batchId = await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = prodId,
            ["batch_number"] = "SR-B1",
            ["expiry_date"] = DateTime.UtcNow.AddDays(180).ToString("yyyy-MM-dd"),
            ["quantity"] = 10
        });

        var storeCtrl = new SaleReturnController(env.Db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = MakeJsonHttpContext(adminId, new
                {
                    customer_id = custId,
                    date = "2026-11-23",
                    settlement_mode = "cash_refund",
                    payment_method = "cash",
                    items = new[]
                    {
                        new
                        {
                            product_id = prodId,
                            batch_number = "SR-B1",
                            unit_return_quantity = 3,
                            unit_sale_price = 100m,
                            item_discount_percentage = 0m
                        }
                    }
                })
            }
        };
        var storeRes = await storeCtrl.Store();
        GetStatus(storeRes).Should().Be(201);
        var srId = ExtractId(storeRes);

        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await barrier.Task;
            var ctrl = new SaleReturnController(env.Db)
            {
                ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId) }
            };
            return GetStatus(await ctrl.Destroy(srId));
        })).ToArray();

        barrier.SetResult();
        var statuses = await Task.WhenAll(tasks);

        statuses.Count(s => s == 200).Should().Be(1);
        statuses.Count(s => s == 404).Should().Be(3);

        var prodQty = await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = prodId });
        var batchQty = await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM batches WHERE id = @id", new { id = batchId });
        var accBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId });
        var drawerBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId });
        var srCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sale_returns WHERE id = @id", new { id = srId });

        prodQty.Should().Be(10);
        batchQty.Should().Be(10);
        accBal.Should().Be(1000m);
        drawerBal.Should().Be(1000m);
        srCount.Should().Be(0);
    }

    [Fact]
    public async Task FM404c_SaleReturnController_ConcurrentUpdates_PreserveAccountAndDrawerParity()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedAdminAsync();
        var (cashId, sessionId) = await SeedCashAndDrawerAsync(env, adminId, 1000m);
        var (_, _, _, custId, prodId) = await SeedCatalogAsync(env, initialQty: 10, unitSale: 100m, unitPurchase: 50m);

        var storeCtrl = new SaleReturnController(env.Db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = MakeJsonHttpContext(adminId, new
                {
                    customer_id = custId,
                    date = "2026-11-23",
                    settlement_mode = "cash_refund",
                    payment_method = "cash",
                    items = new[]
                    {
                        new { product_id = prodId, unit_return_quantity = 2, unit_sale_price = 100m, item_discount_percentage = 0m }
                    }
                })
            }
        };
        var storeRes = await storeCtrl.Store();
        GetStatus(storeRes).Should().Be(201);
        var srId = ExtractId(storeRes);

        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var t1 = Task.Run(async () =>
        {
            await barrier.Task;
            var ctrl = new SaleReturnController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new
                    {
                        customer_id = custId,
                        date = "2026-11-23",
                        settlement_mode = "cash_refund",
                        payment_method = "cash",
                        items = new[]
                        {
                            new { product_id = prodId, unit_return_quantity = 3, unit_sale_price = 100m, item_discount_percentage = 0m }
                        }
                    })
                }
            };
            return GetStatus(await ctrl.Update(srId));
        });
        var t2 = Task.Run(async () =>
        {
            await barrier.Task;
            var ctrl = new SaleReturnController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new
                    {
                        customer_id = custId,
                        date = "2026-11-23",
                        settlement_mode = "cash_refund",
                        payment_method = "cash",
                        items = new[]
                        {
                            new { product_id = prodId, unit_return_quantity = 1, unit_sale_price = 150m, item_discount_percentage = 0m }
                        }
                    })
                }
            };
            return GetStatus(await ctrl.Update(srId));
        });

        barrier.SetResult();
        var statuses = await Task.WhenAll(t1, t2);
        statuses.Should().AllSatisfy(s => s.Should().Be(200));

        var finalTotal = await env.Db.ExecuteScalarAsync<decimal>("SELECT total FROM sale_returns WHERE id = @id", new { id = srId });
        var accBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId });
        var drawerBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId });

        finalTotal.Should().BeOneOf(300m, 150m);
        accBal.Should().Be(1000m - finalTotal);
        drawerBal.Should().Be(1000m - finalTotal);
    }

    [Fact]
    public async Task FM404c_PurchaseReturnController_ConcurrentDestroy_OnlyOneSucceeds_NoDoubleRevert()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedAdminAsync();
        var (cashId, sessionId) = await SeedCashAndDrawerAsync(env, adminId, 1000m);
        var (_, _, suppId, _, prodId) = await SeedCatalogAsync(env, initialQty: 20, unitSale: 100m, unitPurchase: 50m);

        var batchId = await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = prodId,
            ["batch_number"] = "PR-B1",
            ["expiry_date"] = DateTime.UtcNow.AddDays(180).ToString("yyyy-MM-dd"),
            ["quantity"] = 20
        });

        var storeCtrl = new PurchaseReturnController(env.Db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = MakeJsonHttpContext(adminId, new
                {
                    supplier_id = suppId,
                    date = "2026-11-23",
                    settlement_mode = "cash_refund",
                    payment_method = "cash",
                    items = new[]
                    {
                        new
                        {
                            product_id = prodId,
                            batch = "PR-B1",
                            return_unit_quantity = 4,
                            unit_purchase_price = 50m,
                            item_discount_percentage = 0m
                        }
                    }
                })
            }
        };
        var storeRes = await storeCtrl.Store();
        GetStatus(storeRes).Should().Be(201);
        var prId = ExtractId(storeRes);

        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await barrier.Task;
            var ctrl = new PurchaseReturnController(env.Db)
            {
                ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId) }
            };
            return GetStatus(await ctrl.Destroy(prId));
        })).ToArray();

        barrier.SetResult();
        var statuses = await Task.WhenAll(tasks);

        statuses.Count(s => s == 204).Should().Be(1);
        statuses.Count(s => s == 404).Should().Be(3);

        var prodQty = await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = prodId });
        var batchQty = await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM batches WHERE id = @id", new { id = batchId });
        var accBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId });
        var drawerBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId });
        var prCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM purchase_returns WHERE id = @id", new { id = prId });

        prodQty.Should().Be(20);
        batchQty.Should().Be(20);
        accBal.Should().Be(1000m);
        drawerBal.Should().Be(1000m);
        prCount.Should().Be(0);
    }

    [Fact]
    public async Task FM404c_PurchaseOrderController_ConcurrentCancelAndReceive_And_ReceivedGuardsReturn409()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedAdminAsync();
        await SeedCashAndDrawerAsync(env, adminId, 5000m);
        var (_, _, suppId, _, prodId) = await SeedCatalogAsync(env, initialQty: 10, unitSale: 100m, unitPurchase: 50m);

        async Task<long> CreatePoAsync()
        {
            var ctrl = new PurchaseOrderController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new
                    {
                        supplier_id = suppId,
                        date = "2026-11-23",
                        expected_received_date = "2026-11-25",
                        items = new[]
                        {
                            new
                            {
                                product_id = prodId,
                                pack_size = 1,
                                ordered_quantity = 5,
                                unit_purchase_price = 50m
                            }
                        }
                    })
                }
            };
            var res = await ctrl.Store();
            GetStatus(res).Should().Be(201);
            return ExtractId(res);
        }

        var poId = await CreatePoAsync();

        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelTask = Task.Run(async () =>
        {
            await barrier.Task;
            var ctrl = new PurchaseOrderController(env.Db)
            {
                ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId) }
            };
            return GetStatus(await ctrl.Cancel(poId));
        });

        var receiveTask = Task.Run(async () =>
        {
            await barrier.Task;
            var piCtrl = new PurchaseInvoiceController(env.Db)
            {
                ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId) }
            };
            var rawData = new Dictionary<string, object?>
            {
                ["supplier_id"] = suppId,
                ["purchase_order_id"] = poId,
                ["posted_date"] = "2026-11-23",
                ["invoice_number"] = $"INV-{Guid.NewGuid():N}"[..12],
                ["invoice_type"] = "credit",
                ["total_paid"] = 0m
            };
            var rawItems = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["product_id"] = prodId,
                    ["pack_size"] = 1,
                    ["pack_quantity"] = 5,
                    ["unit_quantity"] = 5,
                    ["quantity"] = 5,
                    ["pack_purchase_price"] = 50m,
                    ["unit_purchase_price"] = 50m,
                    ["sub_total"] = 250m
                }
            };
            return GetStatus(await piCtrl.ExecuteStorePurchaseInvoiceAsync(rawData, rawItems));
        });

        barrier.SetResult();
        var cancelStatus = await cancelTask;
        var receiveStatus = await receiveTask;

        var poStatus = await env.Db.ExecuteScalarAsync<string>("SELECT status FROM purchase_orders WHERE id = @id", new { id = poId });
        if (receiveStatus == 201)
        {
            cancelStatus.Should().Be(409);
            poStatus.Should().Be("received");
        }
        else
        {
            cancelStatus.Should().Be(200);
            receiveStatus.Should().Be(422);
            poStatus.Should().Be("cancelled");
        }

        // Ensure received PO blocks Update, Cancel, and Destroy with 409 Conflict
        var poReceivedId = receiveStatus == 201 ? poId : await CreatePoAsync();
        if (receiveStatus != 201)
        {
            var piCtrl = new PurchaseInvoiceController(env.Db)
            {
                ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId) }
            };
            var res = await piCtrl.ExecuteStorePurchaseInvoiceAsync(
                new Dictionary<string, object?>
                {
                    ["supplier_id"] = suppId,
                    ["purchase_order_id"] = poReceivedId,
                    ["posted_date"] = "2026-11-23",
                    ["invoice_number"] = $"INV-REC-{Guid.NewGuid():N}"[..14],
                    ["invoice_type"] = "credit",
                    ["total_paid"] = 0m
                },
                [
                    new()
                    {
                        ["product_id"] = prodId,
                        ["pack_size"] = 1,
                        ["pack_quantity"] = 5,
                        ["unit_quantity"] = 5,
                        ["quantity"] = 5,
                        ["pack_purchase_price"] = 50m,
                        ["unit_purchase_price"] = 50m,
                        ["sub_total"] = 250m
                    }
                ]);
            GetStatus(res).Should().Be(201);
        }

        var updCtrl = new PurchaseOrderController(env.Db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = MakeJsonHttpContext(adminId, new
                {
                    supplier_id = suppId,
                    date = "2026-11-23",
                    expected_received_date = "2026-11-26",
                    items = new[] { new { product_id = prodId, pack_size = 1, ordered_quantity = 2, unit_purchase_price = 50m } }
                })
            }
        };
        GetStatus(await updCtrl.Update(poReceivedId)).Should().Be(409);

        var cnlCtrl = new PurchaseOrderController(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId) }
        };
        GetStatus(await cnlCtrl.Cancel(poReceivedId)).Should().Be(409);

        var delCtrl = new PurchaseOrderController(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId) }
        };
        GetStatus(await delCtrl.Destroy(poReceivedId)).Should().Be(409);

        var poItemsLeft = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM purchase_order_items WHERE purchase_order_id = @id", new { id = poReceivedId });
        poItemsLeft.Should().Be(1);

        // Concurrent Destroy on an unreceived PO yields 1 x 204 and 3 x 404
        var poDelId = await CreatePoAsync();
        var delBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var delTasks = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await delBarrier.Task;
            var ctrl = new PurchaseOrderController(env.Db)
            {
                ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId) }
            };
            return GetStatus(await ctrl.Destroy(poDelId));
        })).ToArray();
        delBarrier.SetResult();
        var delStatuses = await Task.WhenAll(delTasks);
        delStatuses.Count(s => s == 204).Should().Be(1);
        delStatuses.Count(s => s == 404).Should().Be(3);
    }

    [Fact]
    public async Task FM404d_BatchController_ConcurrentMoveLocation_PreventsNegativeOrDuplicateQuantities()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedAdminAsync();
        var (_, _, _, _, prodId) = await SeedCatalogAsync(env, initialQty: 10);

        var loc1 = await env.Db.InsertAsync("storage_locations", new Dictionary<string, object?>
        {
            ["name"] = "Shelf A",
            ["code"] = "SL-A",
            ["type"] = "shop",
            ["is_default"] = 1,
            ["is_active"] = 1
        });
        var loc2 = await env.Db.InsertAsync("storage_locations", new Dictionary<string, object?>
        {
            ["name"] = "Cold Room",
            ["code"] = "SL-C",
            ["type"] = "cold_storage",
            ["is_default"] = 0,
            ["is_active"] = 1
        });

        var batchId = await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = prodId,
            ["batch_number"] = "MV-01",
            ["expiry_date"] = DateTime.UtcNow.AddDays(180).ToString("yyyy-MM-dd"),
            ["quantity"] = 10,
            ["storage_location_id"] = loc1
        });

        var barrier1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var moveTasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await barrier1.Task;
            var ctrl = new BatchController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new
                    {
                        to_storage_location_id = loc2,
                        quantity = 6
                    })
                }
            };
            return GetStatus(await ctrl.MoveLocation(batchId));
        })).ToArray();

        barrier1.SetResult();
        var moveStatuses = await Task.WhenAll(moveTasks);
        moveStatuses.Count(s => s == 200).Should().Be(1);
        moveStatuses.Count(s => s == 422).Should().Be(1);

        var sumMv01 = await env.Db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM batches WHERE product_id = @pid", new { pid = prodId });
        var srcQty = await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM batches WHERE id = @id", new { id = batchId });
        var tgtQty = await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM batches WHERE product_id = @pid AND storage_location_id = @lid", new { pid = prodId, lid = loc2 });

        sumMv01.Should().Be(10);
        srcQty.Should().Be(4);
        tgtQty.Should().Be(6);

        // Concurrent full-move into pre-existing target batch -> 1 x 200, 1 x 404, zero duplication
        var (_, _, _, _, prodId2) = await SeedCatalogAsync(env, initialQty: 15);
        var srcFullId = await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = prodId2,
            ["batch_number"] = "MV-02",
            ["expiry_date"] = DateTime.UtcNow.AddDays(180).ToString("yyyy-MM-dd"),
            ["quantity"] = 10,
            ["storage_location_id"] = loc1
        });
        await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = prodId2,
            ["batch_number"] = "MV-02",
            ["expiry_date"] = DateTime.UtcNow.AddDays(180).ToString("yyyy-MM-dd"),
            ["quantity"] = 5,
            ["storage_location_id"] = loc2
        });

        var barrier2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fullMoveTasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            await barrier2.Task;
            var ctrl = new BatchController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new
                    {
                        to_storage_location_id = loc2,
                        quantity = 10
                    })
                }
            };
            return GetStatus(await ctrl.MoveLocation(srcFullId));
        })).ToArray();

        barrier2.SetResult();
        var fullStatuses = await Task.WhenAll(fullMoveTasks);
        fullStatuses.Count(s => s == 200).Should().Be(1);
        fullStatuses.Count(s => s == 404).Should().Be(1);

        var sumMv02 = await env.Db.ExecuteScalarAsync<int>("SELECT COALESCE(SUM(quantity), 0) FROM batches WHERE product_id = @pid", new { pid = prodId2 });
        sumMv02.Should().Be(15);
    }

    // =========================================================================
    // Part 2 Tests (CustomerLedger, SupplierLedger, Customer, Supplier, StorageLocation)
    // =========================================================================

    [Fact]
    public async Task FM407_CustomerLedgerController_ConcurrentSettleInvoice_SettlesExactlyOnceAndPreservesParity()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedAdminAsync();
        var (cashId, sessionId) = await SeedCashAndDrawerAsync(env, adminId, 1000m);
        var (_, _, _, custId, _) = await SeedCatalogAsync(env);

        var saleId = await env.Db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = adminId,
            ["customer_id"] = custId,
            ["invoice_type"] = "credit",
            ["payment_method"] = "cash",
            ["sale_type"] = "retail",
            ["posted_number"] = "SI-M4D-001",
            ["date"] = "2026-11-23",
            ["gross_amount"] = 350m,
            ["total"] = 350m,
            ["total_receive"] = 0m,
            ["status"] = "posted"
        });

        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await barrier.Task;
            var ctrl = new CustomerLedgerController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new
                    {
                        sale_invoice_id = saleId,
                        payment_method = "cash",
                        entry_date = "2026-11-23"
                    })
                }
            };
            var res = await ctrl.SettleInvoice();
            var status = GetStatus(res);
            decimal settled = 0m;
            if (status == 200)
            {
                using var doc = ParseBody(res);
                if (doc.RootElement.TryGetProperty("settled", out var sEl))
                {
                    settled = sEl.GetDecimal();
                }
            }
            return (Status: status, Settled: settled);
        })).ToArray();

        barrier.SetResult();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r.Status == 200 && r.Settled == 350m).Should().Be(1);
        results.Count(r => r.Status == 422 || (r.Status == 200 && r.Settled == 0m)).Should().Be(7);

        var payCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM payments WHERE party_type = 'customer' AND party_id = @cid", new { cid = custId });
        var allocCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM payment_allocations WHERE invoice_type = 'sale_invoice' AND invoice_id = @iid", new { iid = saleId });
        var ledgerPayCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM customer_ledgers WHERE customer_id = @cid AND entry_type = 'payment'", new { cid = custId });
        var totalReceive = await env.Db.ExecuteScalarAsync<decimal>("SELECT total_receive FROM sale_invoices WHERE id = @id", new { id = saleId });
        var accBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId });
        var drawerBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId });
        var auditCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM document_histories WHERE entity_type = 'App\\Models\\Payment'");

        payCount.Should().Be(1);
        allocCount.Should().Be(1);
        ledgerPayCount.Should().Be(1);
        totalReceive.Should().Be(350m);
        accBal.Should().Be(1350m);
        drawerBal.Should().Be(1350m);
        auditCount.Should().Be(1);

        // Reversed sale invoice -> 422
        var revSaleId = await env.Db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = adminId,
            ["customer_id"] = custId,
            ["invoice_type"] = "credit",
            ["payment_method"] = "cash",
            ["sale_type"] = "retail",
            ["posted_number"] = "SI-M4D-REV",
            ["date"] = "2026-11-23",
            ["gross_amount"] = 100m,
            ["total"] = 100m,
            ["total_receive"] = 0m,
            ["status"] = "reversed"
        });
        var revCtrl = new CustomerLedgerController(env.Db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = MakeJsonHttpContext(adminId, new { sale_invoice_id = revSaleId, payment_method = "cash" })
            }
        };
        GetStatus(await revCtrl.SettleInvoice()).Should().Be(422);

        // Trigger failure mid-transaction rolls back all 6 writes atomically
        var sale2Id = await env.Db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = adminId,
            ["customer_id"] = custId,
            ["invoice_type"] = "credit",
            ["payment_method"] = "cash",
            ["sale_type"] = "retail",
            ["posted_number"] = "SI-M4D-RB",
            ["date"] = "2026-11-23",
            ["gross_amount"] = 100m,
            ["total"] = 100m,
            ["total_receive"] = 0m,
            ["status"] = "posted"
        });
        await env.Db.ExecuteAsync(@"
            CREATE TRIGGER trg_abort_acct_txn_m4d
            BEFORE INSERT ON account_transactions
            BEGIN
                SELECT RAISE(ABORT, 'simulated_mid_tx_failure');
            END;");
        try
        {
            var rbCtrl = new CustomerLedgerController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new { sale_invoice_id = sale2Id, payment_method = "cash" })
                }
            };
            Func<Task> act = () => rbCtrl.SettleInvoice();
            await act.Should().ThrowAsync<SqliteException>();
        }
        finally
        {
            await env.Db.ExecuteAsync("DROP TRIGGER IF EXISTS trg_abort_acct_txn_m4d;");
        }

        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM payments WHERE party_type = 'customer' AND party_id = @cid", new { cid = custId })).Should().Be(1);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM payment_allocations WHERE invoice_type = 'sale_invoice' AND invoice_id = @iid", new { iid = sale2Id })).Should().Be(0);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT total_receive FROM sale_invoices WHERE id = @id", new { id = sale2Id })).Should().Be(0m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId })).Should().Be(1350m);
    }

    [Fact]
    public async Task FM407_SupplierLedgerController_ConcurrentSettleInvoice_SettlesExactlyOnceAndPreservesParity()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedAdminAsync();
        var (cashId, sessionId) = await SeedCashAndDrawerAsync(env, adminId, 1000m);
        var (_, _, suppId, _, _) = await SeedCatalogAsync(env);

        var purchId = await env.Db.InsertAsync("purchase_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = adminId,
            ["supplier_id"] = suppId,
            ["invoice_type"] = "credit",
            ["payment_method"] = "cash",
            ["invoice_number"] = "PINV-M4D-001",
            ["posted_number"] = "PRINV-0001",
            ["posted_date"] = "2026-11-23",
            ["total_amount"] = 400m,
            ["total_paid"] = 0m,
            ["status"] = "posted"
        });

        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await barrier.Task;
            var ctrl = new SupplierLedgerController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new
                    {
                        purchase_invoice_id = purchId,
                        payment_method = "cash",
                        entry_date = "2026-11-23"
                    })
                }
            };
            var res = await ctrl.SettleInvoice();
            var status = GetStatus(res);
            decimal settled = 0m;
            if (status == 200)
            {
                using var doc = ParseBody(res);
                if (doc.RootElement.TryGetProperty("settled", out var sEl))
                {
                    settled = sEl.GetDecimal();
                }
            }
            return (Status: status, Settled: settled);
        })).ToArray();

        barrier.SetResult();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r.Status == 200 && r.Settled == 400m).Should().Be(1);
        results.Count(r => r.Status == 422 || (r.Status == 200 && r.Settled == 0m)).Should().Be(7);

        var payCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM payments WHERE party_type = 'supplier' AND party_id = @sid", new { sid = suppId });
        var allocCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM payment_allocations WHERE invoice_type = 'purchase_invoice' AND invoice_id = @iid", new { iid = purchId });
        var ledgerPayCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM supplier_ledgers WHERE supplier_id = @sid AND entry_type = 'payment'", new { sid = suppId });
        var totalPaid = await env.Db.ExecuteScalarAsync<decimal>("SELECT total_paid FROM purchase_invoices WHERE id = @id", new { id = purchId });
        var accBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId });
        var drawerBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId });
        var auditCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM document_histories WHERE entity_type = 'App\\Models\\Payment'");

        payCount.Should().Be(1);
        allocCount.Should().Be(1);
        ledgerPayCount.Should().Be(1);
        totalPaid.Should().Be(400m);
        accBal.Should().Be(600m);
        drawerBal.Should().Be(600m);
        auditCount.Should().Be(1);

        // Trigger failure mid-transaction rolls back atomically
        var purch2Id = await env.Db.InsertAsync("purchase_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = adminId,
            ["supplier_id"] = suppId,
            ["invoice_type"] = "credit",
            ["payment_method"] = "cash",
            ["invoice_number"] = "PINV-M4D-RB",
            ["posted_number"] = "PRINV-0002",
            ["posted_date"] = "2026-11-23",
            ["total_amount"] = 100m,
            ["total_paid"] = 0m,
            ["status"] = "posted"
        });
        await env.Db.ExecuteAsync(@"
            CREATE TRIGGER trg_abort_acct_txn_sup_m4d
            BEFORE INSERT ON account_transactions
            BEGIN
                SELECT RAISE(ABORT, 'simulated_mid_tx_failure');
            END;");
        try
        {
            var rbCtrl = new SupplierLedgerController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new { purchase_invoice_id = purch2Id, payment_method = "cash" })
                }
            };
            Func<Task> act = () => rbCtrl.SettleInvoice();
            await act.Should().ThrowAsync<SqliteException>();
        }
        finally
        {
            await env.Db.ExecuteAsync("DROP TRIGGER IF EXISTS trg_abort_acct_txn_sup_m4d;");
        }

        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM payments WHERE party_type = 'supplier' AND party_id = @sid", new { sid = suppId })).Should().Be(1);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM payment_allocations WHERE invoice_type = 'purchase_invoice' AND invoice_id = @iid", new { iid = purch2Id })).Should().Be(0);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT total_paid FROM purchase_invoices WHERE id = @id", new { id = purch2Id })).Should().Be(0m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId })).Should().Be(600m);
    }

    [Fact]
    public async Task FM404d_LedgerControllers_ConcurrentDestroyPaymentEntry_ReversesOnceWithout500()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedAdminAsync();
        var (cashId, sessionId) = await SeedCashAndDrawerAsync(env, adminId, 1000m);
        var (_, _, suppId, custId, _) = await SeedCatalogAsync(env);

        var saleId = await env.Db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = adminId,
            ["customer_id"] = custId,
            ["invoice_type"] = "credit",
            ["payment_method"] = "cash",
            ["sale_type"] = "retail",
            ["posted_number"] = "SI-M4D-DEL",
            ["date"] = "2026-11-23",
            ["gross_amount"] = 250m,
            ["total"] = 250m,
            ["total_receive"] = 0m,
            ["status"] = "posted"
        });
        var settleCustCtrl = new CustomerLedgerController(env.Db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = MakeJsonHttpContext(adminId, new { sale_invoice_id = saleId, payment_method = "cash" })
            }
        };
        GetStatus(await settleCustCtrl.SettleInvoice()).Should().Be(200);
        var custLedgerId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM customer_ledgers WHERE customer_id = @cid AND entry_type = 'payment' LIMIT 1", new { cid = custId });

        var barrierCust = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var custDelTasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await barrierCust.Task;
            var ctrl = new CustomerLedgerController(env.Db)
            {
                ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId) }
            };
            return GetStatus(await ctrl.Destroy(custLedgerId));
        })).ToArray();
        barrierCust.SetResult();
        var custStatuses = await Task.WhenAll(custDelTasks);

        custStatuses.Count(s => s == 200).Should().Be(1);
        custStatuses.Should().AllSatisfy(s => s.Should().BeOneOf(200, 404, 422));

        var purchId = await env.Db.InsertAsync("purchase_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = adminId,
            ["supplier_id"] = suppId,
            ["invoice_type"] = "credit",
            ["payment_method"] = "cash",
            ["invoice_number"] = "PINV-M4D-DEL",
            ["posted_number"] = "PRINV-DEL",
            ["posted_date"] = "2026-11-23",
            ["total_amount"] = 150m,
            ["total_paid"] = 0m,
            ["status"] = "posted"
        });
        var settleSupCtrl = new SupplierLedgerController(env.Db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = MakeJsonHttpContext(adminId, new { purchase_invoice_id = purchId, payment_method = "cash" })
            }
        };
        GetStatus(await settleSupCtrl.SettleInvoice()).Should().Be(200);
        var supLedgerId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM supplier_ledgers WHERE supplier_id = @sid AND entry_type = 'payment' LIMIT 1", new { sid = suppId });

        var barrierSup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var supDelTasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await barrierSup.Task;
            var ctrl = new SupplierLedgerController(env.Db)
            {
                ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId) }
            };
            return GetStatus(await ctrl.Destroy(supLedgerId));
        })).ToArray();
        barrierSup.SetResult();
        var supStatuses = await Task.WhenAll(supDelTasks);

        supStatuses.Count(s => s == 200).Should().Be(1);
        supStatuses.Should().AllSatisfy(s => s.Should().BeOneOf(200, 404, 422));

        var accBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId });
        var drawerBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId });
        accBal.Should().Be(1000m);
        drawerBal.Should().Be(1000m);
    }

    [Fact]
    public async Task FM404d_CustomerAndSupplierControllers_ConcurrentStore_PreventsUniqueConstraint500()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedAdminAsync();

        var custBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var custTasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await custBarrier.Task;
            var ctrl = new CustomerController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new
                    {
                        name = "Unique Cust Co",
                        email = "unique@cust.local",
                        opening_balance = 100m
                    })
                }
            };
            return GetStatus(await ctrl.Store());
        })).ToArray();
        custBarrier.SetResult();
        var custStatuses = await Task.WhenAll(custTasks);

        custStatuses.Count(s => s == 201).Should().Be(1);
        custStatuses.Count(s => s == 422).Should().Be(5);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM customers WHERE name = 'Unique Cust Co'")).Should().Be(1);

        var supBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var supTasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await supBarrier.Task;
            var ctrl = new SupplierController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new
                    {
                        name = "Unique Sup Co",
                        opening_balance = 200m
                    })
                }
            };
            return GetStatus(await ctrl.Store());
        })).ToArray();
        supBarrier.SetResult();
        var supStatuses = await Task.WhenAll(supTasks);

        supStatuses.Count(s => s == 201).Should().Be(1);
        supStatuses.Count(s => s == 422).Should().Be(5);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM suppliers WHERE name = 'Unique Sup Co'")).Should().Be(1);
    }

    [Fact]
    public async Task FM404d_StorageLocationController_ConcurrentStoreAndDefaultDestroyGuard()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedAdminAsync();

        // (a) 6 concurrent auto-code Store calls -> 6 x 201 with 6 distinct codes
        var autoBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var autoTasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await autoBarrier.Task;
            var ctrl = new StorageLocationController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new
                    {
                        name = "Main Warehouse",
                        type = "warehouse"
                    })
                }
            };
            var res = await ctrl.Store();
            using var doc = ParseBody(res);
            return (Status: GetStatus(res), Code: doc.RootElement.GetProperty("code").GetString() ?? "");
        })).ToArray();
        autoBarrier.SetResult();
        var autoResults = await Task.WhenAll(autoTasks);

        autoResults.Should().AllSatisfy(r => r.Status.Should().Be(201));
        autoResults.Select(r => r.Code).Distinct().Count().Should().Be(6);

        // (b) 6 concurrent explicit-code Store calls -> 1 x 201, 5 x 422
        var expBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var expTasks = Enumerable.Range(0, 6).Select(i => Task.Run(async () =>
        {
            await expBarrier.Task;
            var ctrl = new StorageLocationController(env.Db)
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = MakeJsonHttpContext(adminId, new
                    {
                        name = $"Explicit Loc {i}",
                        code = "EXP-LOC-1",
                        type = "shop"
                    })
                }
            };
            return GetStatus(await ctrl.Store());
        })).ToArray();
        expBarrier.SetResult();
        var expStatuses = await Task.WhenAll(expTasks);

        expStatuses.Count(s => s == 201).Should().Be(1);
        expStatuses.Count(s => s == 422).Should().Be(5);

        // (c) Concurrent Update(is_default = true) vs Destroy never deletes a default location
        for (int round = 0; round < 5; round++)
        {
            var locId = await env.Db.InsertAsync("storage_locations", new Dictionary<string, object?>
            {
                ["name"] = $"Guard Loc {round}",
                ["code"] = $"GRD-{round}",
                ["type"] = "shop",
                ["is_default"] = 0,
                ["is_active"] = 1
            });

            var guardBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var updTask = Task.Run(async () =>
            {
                await guardBarrier.Task;
                var ctrl = new StorageLocationController(env.Db)
                {
                    ControllerContext = new ControllerContext
                    {
                        HttpContext = MakeJsonHttpContext(adminId, new
                        {
                            name = $"Guard Loc {round}",
                            code = $"GRD-{round}",
                            type = "shop",
                            is_default = true,
                            is_active = true
                        })
                    }
                };
                return GetStatus(await ctrl.Update(locId));
            });
            var delTask = Task.Run(async () =>
            {
                await guardBarrier.Task;
                var ctrl = new StorageLocationController(env.Db)
                {
                    ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(adminId) }
                };
                return GetStatus(await ctrl.Destroy(locId));
            });

            guardBarrier.SetResult();
            var updStatus = await updTask;
            var delStatus = await delTask;

            var rowAfter = await env.Db.QuerySingleOrDefaultAsync("SELECT * FROM storage_locations WHERE id = @id", new { id = locId });
            if (updStatus == 200)
            {
                delStatus.Should().Be(422);
                rowAfter.Should().NotBeNull();
                Convert.ToInt64(rowAfter!["is_default"]).Should().Be(1);
            }
            else
            {
                updStatus.Should().Be(404);
                delStatus.Should().Be(200);
                rowAfter.Should().BeNull();
            }
        }
    }
}

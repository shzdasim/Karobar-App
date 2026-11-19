using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karobar.Api;
using Karobar.Api.Middleware;
using Karobar.Data;
using Karobar.Host;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Karobar.Tests;

public class Wave5And6LiveHttpIntegrationTests
{
    private static async Task<(WebApplication App, HttpClient Client, KarobarDb Db, string TempRoot, string? PrevRoot)> StartLiveTestServerAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_wave56_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "public"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "logs"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "public"));

        var tempDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
        var sourceFactory = new DbConnectionFactory();
        if (File.Exists(sourceFactory.SqlitePath) && new FileInfo(sourceFactory.SqlitePath).Length > 0)
        {
            File.Copy(sourceFactory.SqlitePath, tempDbPath, overwrite: true);
        }

        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
        KarobarDb.FlushSchemaCache();

        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: tempDbPath)).EnsureSchemaReadyAsync();
        KarobarDb.FlushSchemaCache();

        var kDb = new KarobarDb(explicitSqlitePath: tempDbPath);
        await EnsureSeedAsync(kDb);

        var port = KestrelHostServer.FindFreePort(19600);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddSingleton(kDb);
        builder.Services.AddSingleton(new LicenseService(kDb));
        builder.Services.AddSingleton(new SqliteExportService(kDb));
        builder.Services.AddSingleton(sp => new BackupService(kDb, sp.GetRequiredService<SqliteExportService>()));
        builder.Services.AddSingleton(new OperatorPinService(kDb));
        builder.Services.AddSingleton(new SyncPulseService(kDb));
        builder.Services.AddKarobarApi(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(KarobarApiServiceExtensions).Assembly);

        var app = builder.Build();

        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                string? spoofed = null;
                if (context.Request.Query.TryGetValue("_method", out var qMethod) && !string.IsNullOrWhiteSpace(qMethod))
                    spoofed = qMethod.ToString().Trim().ToUpperInvariant();
                else if (context.Request.HasFormContentType)
                {
                    context.Request.EnableBuffering();
                    var form = await context.Request.ReadFormAsync(context.RequestAborted);
                    if (form.TryGetValue("_method", out var fMethod) && !string.IsNullOrWhiteSpace(fMethod))
                        spoofed = fMethod.ToString().Trim().ToUpperInvariant();
                }
                if (spoofed is "PUT" or "PATCH" or "DELETE") context.Request.Method = spoofed;
            }
            await next();
        });
        app.UseRouting();

        app.Use(async (context, next) =>
        {
            if (context.Request.QueryString.HasValue)
            {
                var parsed = QueryHelpers.ParseQuery(context.Request.QueryString.Value);
                if (parsed.Any(kv => kv.Value.Any(string.IsNullOrWhiteSpace)))
                {
                    var cleaned = new List<KeyValuePair<string, string?>>();
                    foreach (var kv in parsed)
                        foreach (var v in kv.Value)
                            if (!string.IsNullOrWhiteSpace(v))
                                cleaned.Add(new KeyValuePair<string, string?>(kv.Key, v));
                    context.Request.QueryString = cleaned.Count > 0 ? QueryString.Create(cleaned) : QueryString.Empty;
                }
            }
            await next();
        });

        app.UseMiddleware<SignedUrlMiddleware>();
        app.UseAuthentication();
        app.UseMiddleware<CheckTokenExpiryMiddleware>();
        app.UseAuthorization();
        app.MapControllers();

        await app.StartAsync();

        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return (app, client, kDb, tempRoot, prevRoot);
    }

    private static async Task EnsureSeedAsync(KarobarDb db)
    {
        var adminRow = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'qa-w56-admin@karobar.local'");
        var adminId = adminRow != null
            ? Convert.ToInt64(adminRow["id"])
            : await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "QA Wave56 Admin",
                ["email"] = "qa-w56-admin@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("AdminPass123!"),
                ["status"] = "active"
            });

        if (await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = 'Admin'") == null)
            await db.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "Admin", ["guard_name"] = "web" });
        await db.SyncUserRolesAsync(adminId, new[] { "Admin" });

        // Restricted user with ONLY specific Wave 6 permissions (accounting, expense-category, payroll)
        var finUserRow = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'qa-w56-finance@karobar.local'");
        var finUserId = finUserRow != null
            ? Convert.ToInt64(finUserRow["id"])
            : await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "QA Finance Specialist",
                ["email"] = "qa-w56-finance@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("FinancePass123!"),
                ["status"] = "active"
            });

        var finPerms = new[]
        {
            "accounting.view",
            "report.financial-statements.view",
            "expense-category.view",
            "expense-category.manage",
            "payroll.manage"
        };
        foreach (var pName in finPerms)
        {
            if (await db.QuerySingleOrDefaultAsync("SELECT id FROM permissions WHERE name = @pName", new { pName }) == null)
                await db.InsertAsync("permissions", new Dictionary<string, object?> { ["name"] = pName, ["guard_name"] = "web" });
        }
        await db.SyncUserPermissionsAsync(finUserId, finPerms);

        if (await db.HasTableAsync("settings"))
        {
            var st = await db.QuerySingleOrDefaultAsync("SELECT id FROM settings ORDER BY id ASC LIMIT 1");
            if (st != null)
            {
                await db.UpdateAsync("settings", st["id"]!, new Dictionary<string, object?> { ["require_operator_pin"] = 0 });
            }
        }

        if (await db.HasTableAsync("accounts") && await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM accounts WHERE type = 'cash' AND is_default = 1") == 0)
        {
            await db.InsertAsync("accounts", new Dictionary<string, object?>
            {
                ["name"] = "Cash Drawer",
                ["code"] = "cash_drawer",
                ["type"] = "cash",
                ["opening_balance"] = 50000m,
                ["current_balance"] = 50000m,
                ["is_default"] = 1,
                ["is_active"] = 1
            });
        }
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var resp = await client.PostAsync("/api/login", JsonBody(new { email, password }));
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task CleanupAsync(WebApplication app, HttpClient client, string tempRoot, string? prevRoot)
    {
        try { await app.StopAsync(); await app.DisposeAsync(); } catch { }
        try { client.Dispose(); } catch { }
        Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
        KarobarDb.FlushSchemaCache();
        try { SqliteConnection.ClearAllPools(); } catch { }
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
    }

    [Fact]
    public async Task Wave5_Stocktake_Adjustments_Disposals_And_Barcodes_ShouldPassLiveHttp()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            // 1. Barcodes [Authorize] must return 401 when unauthenticated
            var unauthBarcodes = await client.GetAsync("/api/barcodes/templates");
            unauthBarcodes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var token = await LoginAsync(client, "qa-w56-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Seed a dedicated category + product with stock so stocktake/disposal/adjustment are deterministic
            var catId = await db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "QA Wave5 Category" });
            var brandId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM brands ORDER BY id ASC LIMIT 1), 0)");
            if (brandId <= 0) brandId = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "QA Wave5 Brand" });
            var prodCode5 = $"QA-W5-{Guid.NewGuid():N}"[..16];
            var prodId = await db.InsertAsync("products", new Dictionary<string, object?>
            {
                ["product_code"] = prodCode5,
                ["barcode"] = prodCode5,
                ["name"] = "QA Wave5 Test Med",
                ["category_id"] = catId,
                ["brand_id"] = brandId,
                ["pack_size"] = 10,
                ["quantity"] = 50,
                ["unit_purchase_price"] = 25m,
                ["pack_purchase_price"] = 250m,
                ["unit_sale_price"] = 35m,
                ["pack_sale_price"] = 350m,
                ["avg_price"] = 25m,
                ["margin"] = 40m
            });

            // 2. Stocktake blind session: scope_name, progress, is_blind boolean, reveal=1, per_page=2000, null expiry_date finalization
            var createSt = await client.PostAsync("/api/stocktake-sessions", JsonBody(new
            {
                session_type = "cycle",
                scope_type = "category",
                scope_id = catId,
                is_blind = true,
                notes = "Wave 5 QA blind cycle count"
            }));
            createSt.StatusCode.Should().Be(HttpStatusCode.Created, await createSt.Content.ReadAsStringAsync());
            long sessionId;
            using (var doc = JsonDocument.Parse(await createSt.Content.ReadAsStringAsync()))
            {
                var root = doc.RootElement;
                sessionId = root.GetProperty("id").GetInt64();
                root.GetProperty("is_blind").ValueKind.Should().Be(JsonValueKind.True);
                root.GetProperty("scope_name").GetString().Should().Be("QA Wave5 Category");
                root.GetProperty("progress").GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(0);
            }

            var startSt = await client.PatchAsync($"/api/stocktake-sessions/{sessionId}/start", JsonBody(new { }));
            startSt.StatusCode.Should().Be(HttpStatusCode.OK, await startSt.Content.ReadAsStringAsync());

            // Add found item with new batch and NULL expiry_date — must be blind-masked on 201 response!
            var addItem = await client.PostAsync($"/api/stocktake-sessions/{sessionId}/items", JsonBody(new
            {
                product_id = prodId,
                batch_number = "W5-FOUND-B1",
                expiry_date = (string?)null,
                counted_qty = 12
            }));
            addItem.StatusCode.Should().Be(HttpStatusCode.Created, await addItem.Content.ReadAsStringAsync());
            using (var doc = JsonDocument.Parse(await addItem.Content.ReadAsStringAsync()))
            {
                doc.RootElement.GetProperty("system_qty").ValueKind.Should().Be(JsonValueKind.Null);
            }

            // Items endpoint with ?per_page=2000&reveal=0 and ?per_page=2000&reveal=1 (was HTTP 422 + clamped to 500!)
            var itemsBlind = await client.GetAsync($"/api/stocktake-sessions/{sessionId}/items?per_page=2000&reveal=0");
            itemsBlind.StatusCode.Should().Be(HttpStatusCode.OK, await itemsBlind.Content.ReadAsStringAsync());
            using (var doc = JsonDocument.Parse(await itemsBlind.Content.ReadAsStringAsync()))
            {
                doc.RootElement.GetProperty("per_page").GetInt32().Should().Be(2000);
                var arr = doc.RootElement.GetProperty("data");
                arr.GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
                arr[0].GetProperty("system_qty").ValueKind.Should().Be(JsonValueKind.Null);
            }

            var itemsRevealed = await client.GetAsync($"/api/stocktake-sessions/{sessionId}/items?per_page=2000&reveal=1");
            itemsRevealed.StatusCode.Should().Be(HttpStatusCode.OK, await itemsRevealed.Content.ReadAsStringAsync());
            var allItemIds = new List<long>();
            using (var doc = JsonDocument.Parse(await itemsRevealed.Content.ReadAsStringAsync()))
            {
                var arr = doc.RootElement.GetProperty("data");
                arr[0].GetProperty("system_qty").ValueKind.Should().NotBe(JsonValueKind.Null);
                foreach (var it in arr.EnumerateArray())
                {
                    if (it.GetProperty("status").GetString() != "counted")
                        allItemIds.Add(it.GetProperty("id").GetInt64());
                }
            }

            foreach (var pendingItemId in allItemIds)
            {
                var countResp = await client.PatchAsync($"/api/stocktake-items/{pendingItemId}", JsonBody(new { counted_qty = 45 }));
                countResp.StatusCode.Should().Be(HttpStatusCode.OK, await countResp.Content.ReadAsStringAsync());
            }

            var submitReview = await client.PatchAsync($"/api/stocktake-sessions/{sessionId}/submit-review", JsonBody(new { }));
            submitReview.StatusCode.Should().Be(HttpStatusCode.OK, await submitReview.Content.ReadAsStringAsync());

            // Finalize must succeed even with NULL expiry_date on W5-FOUND-B1!
            var finalizeSt = await client.PostAsync($"/api/stocktake-sessions/{sessionId}/finalize", JsonBody(new { }));
            finalizeSt.StatusCode.Should().Be(HttpStatusCode.OK, await finalizeSt.Content.ReadAsStringAsync());

            var createdBatch = await db.QuerySingleOrDefaultAsync(
                "SELECT * FROM batches WHERE product_id = @prodId AND batch_number = 'W5-FOUND-B1'",
                new { prodId });
            createdBatch.Should().NotBeNull();
            createdBatch!["expiry_date"]?.ToString().Should().NotBeNullOrWhiteSpace();

            // 3. Stock Adjustments: per_page=1000 & new batch with null expiry
            var adjList = await client.GetAsync("/api/stock-adjustments?per_page=1000");
            adjList.StatusCode.Should().Be(HttpStatusCode.OK, await adjList.Content.ReadAsStringAsync());
            using (var doc = JsonDocument.Parse(await adjList.Content.ReadAsStringAsync()))
            {
                doc.RootElement.GetProperty("per_page").GetInt32().Should().Be(1000);
            }

            var adjCreate = await client.PostAsync("/api/stock-adjustments", JsonBody(new
            {
                posted_date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                note = "Wave 5 null-expiry batch test",
                items = new[]
                {
                    new { product_id = prodId, batch_number = "W5-ADJ-B2", expiry = (string?)null, actual_qty = 20 }
                }
            }));
            adjCreate.StatusCode.Should().Be(HttpStatusCode.OK, await adjCreate.Content.ReadAsStringAsync());

            // 4. Stock Disposals: ?has_narcotics=1 (no 422), record_expense=true posts P&L expense, booleans serialized properly
            var dispFilter = await client.GetAsync("/api/stock-disposals?has_narcotics=1");
            dispFilter.StatusCode.Should().Be(HttpStatusCode.OK, await dispFilter.Content.ReadAsStringAsync());

            var dispCreate = await client.PostAsync("/api/stock-disposals", JsonBody(new
            {
                disposal_date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                status = "disposed",
                cause = "shelf_expiry",
                disposal_method = "incineration",
                record_expense = true,
                notes = "Wave 5 QA write-off check",
                items = new[]
                {
                    new { product_id = prodId, batch_number = "W5-ADJ-B2", dispose_qty = 5, reason = "Expired" }
                }
            }));
            dispCreate.StatusCode.Should().Be(HttpStatusCode.Created, await dispCreate.Content.ReadAsStringAsync());
            using (var doc = JsonDocument.Parse(await dispCreate.Content.ReadAsStringAsync()))
            {
                var d = doc.RootElement.GetProperty("disposal");
                d.GetProperty("record_expense").ValueKind.Should().Be(JsonValueKind.True);
                d.GetProperty("has_narcotics").ValueKind.Should().Be(JsonValueKind.False);
                d.GetProperty("expense_id").ValueKind.Should().Be(JsonValueKind.Number);
                d.GetProperty("expense_id").GetInt64().Should().BeGreaterThan(0);
            }

            // 5. Barcode Templates: null/blank optional NOT NULL numeric fields should default cleanly
            var tplCreate = await client.PostAsync("/api/barcodes/templates", JsonBody(new
            {
                name = "QA Wave5 Roll",
                paper_type = "roll",
                orientation = "landscape",
                width_mm = 50,
                height_mm = 25,
                columns = "",
                rows = (int?)null,
                gap_x_mm = "",
                gap_y_mm = (double?)null,
                margin_top_mm = "",
                margin_left_mm = "",
                margin_right_mm = "",
                margin_bottom_mm = "",
                is_default = false
            }));
            tplCreate.StatusCode.Should().Be(HttpStatusCode.Created, await tplCreate.Content.ReadAsStringAsync());
            long tplId;
            using (var doc = JsonDocument.Parse(await tplCreate.Content.ReadAsStringAsync()))
            {
                tplId = doc.RootElement.GetProperty("template").GetProperty("id").GetInt64();
            }
            var tplDelete = await client.DeleteAsync($"/api/barcodes/templates/{tplId}");
            tplDelete.StatusCode.Should().Be(HttpStatusCode.OK, await tplDelete.Content.ReadAsStringAsync());
        }
        finally
        {
            await CleanupAsync(app, client, tempRoot, prevRoot);
        }
    }

    [Fact]
    public async Task Wave6_CashDrawer_Accounts_Payments_Ledgers_Expenses_Accounting_And_HrPayroll_ShouldPassLiveHttp()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            // 1. Auth guards on Accounts, Payments, Accounting, ExpenseCategories must return 401
            (await client.GetAsync("/api/accounts")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.GetAsync("/api/payments")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.GetAsync("/api/accounting/overview")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.GetAsync("/api/expense-categories")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var adminToken = await LoginAsync(client, "qa-w56-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            // Seed clean test customer, supplier, product
            var customerId = await db.InsertAsync("customers", new Dictionary<string, object?>
            {
                ["name"] = "QA Wave6 Customer",
                ["phone"] = "03001112233",
                ["is_regular"] = 0
            });
            var supplierId = await db.InsertAsync("suppliers", new Dictionary<string, object?>
            {
                ["name"] = "QA Wave6 Supplier",
                ["phone"] = "03004445566"
            });
            var catId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM categories ORDER BY id ASC LIMIT 1), 0)");
            if (catId <= 0) catId = await db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "QA Cat" });
            var brandId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM brands ORDER BY id ASC LIMIT 1), 0)");
            if (brandId <= 0) brandId = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "QA Brand" });
            var prodCode6 = $"QA-W6-{Guid.NewGuid():N}"[..16];
            var prodId = await db.InsertAsync("products", new Dictionary<string, object?>
            {
                ["product_code"] = prodCode6,
                ["barcode"] = prodCode6,
                ["name"] = "QA Wave6 Med",
                ["category_id"] = catId,
                ["brand_id"] = brandId,
                ["pack_size"] = 10,
                ["quantity"] = 100,
                ["unit_purchase_price"] = 50m,
                ["pack_purchase_price"] = 500m,
                ["unit_sale_price"] = 80m,
                ["pack_sale_price"] = 800m,
                ["avg_price"] = 50m,
                ["margin"] = 60m
            });

            // 2. Cash Drawer: Open shift -> cash sale -> GET /api/cash-drawer/current (verifies si.walk_in_customer_name bug is gone!)
            // Close any existing open shift first so we have a clean open shift
            await db.ExecuteAsync("UPDATE cash_drawer_sessions SET status = 'closed', closed_at = CURRENT_TIMESTAMP WHERE status = 'open'");
            var openShift = await client.PostAsync("/api/cash-drawer/open", JsonBody(new { opening_balance = 5000, notes = "Wave 6 QA shift" }));
            openShift.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
            long shiftId;
            using (var doc = JsonDocument.Parse(await openShift.Content.ReadAsStringAsync()))
            {
                shiftId = doc.RootElement.GetProperty("session").GetProperty("id").GetInt64();
            }

            var cashSale = await client.PostAsync("/api/sale-invoices", JsonBody(new
            {
                customer_id = customerId,
                date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                invoice_type = "retail",
                payment_method = "cash",
                total_receive = 800,
                patient_name = "Walk-in Patient QA",
                items = new[]
                {
                    new { product_id = prodId, quantity = 10, price = 80, sub_total = 800 }
                }
            }));
            cashSale.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);

            var currentDrawer = await client.GetAsync("/api/cash-drawer/current");
            currentDrawer.StatusCode.Should().Be(HttpStatusCode.OK, await currentDrawer.Content.ReadAsStringAsync());
            using (var doc = JsonDocument.Parse(await currentDrawer.Content.ReadAsStringAsync()))
            {
                doc.RootElement.GetProperty("session").GetProperty("transactions").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
            }

            var slipResp = await client.GetAsync($"/api/cash-drawer/session/{shiftId}/thermal-slip");
            slipResp.StatusCode.Should().Be(HttpStatusCode.OK, await slipResp.Content.ReadAsStringAsync());
            using (var doc = JsonDocument.Parse(await slipResp.Content.ReadAsStringAsync()))
            {
                var slipUrl = doc.RootElement.GetProperty("url").GetString()!;
                var slipPage = await client.GetAsync(slipUrl);
                slipPage.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // 3. Accounts: is_active respected, ?active_only=1 filters inactive, withdrawal adjustment stored negative
            var inactiveAccResp = await client.PostAsync("/api/accounts", JsonBody(new
            {
                name = "QA Inactive Vault",
                type = "bank",
                opening_balance = 0,
                is_active = false
            }));
            inactiveAccResp.StatusCode.Should().Be(HttpStatusCode.Created, await inactiveAccResp.Content.ReadAsStringAsync());
            long inactiveAccId;
            using (var doc = JsonDocument.Parse(await inactiveAccResp.Content.ReadAsStringAsync()))
            {
                var acc = doc.RootElement.GetProperty("account");
                inactiveAccId = acc.GetProperty("id").GetInt64();
                acc.GetProperty("is_active").ValueKind.Should().Be(JsonValueKind.False);
            }

            var activeOnlyResp = await client.GetAsync("/api/accounts?active_only=1");
            activeOnlyResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var doc = JsonDocument.Parse(await activeOnlyResp.Content.ReadAsStringAsync()))
            {
                var ids = doc.RootElement.GetProperty("accounts").EnumerateArray().Select(a => a.GetProperty("id").GetInt64()).ToList();
                ids.Should().NotContain(inactiveAccId);
            }

            var bankAccResp = await client.PostAsync("/api/accounts", JsonBody(new
            {
                name = "QA Wave6 Bank",
                type = "bank",
                opening_balance = 10000,
                is_active = true
            }));
            bankAccResp.StatusCode.Should().Be(HttpStatusCode.Created);
            long bankAccId;
            using (var doc = JsonDocument.Parse(await bankAccResp.Content.ReadAsStringAsync()))
            {
                bankAccId = doc.RootElement.GetProperty("account").GetProperty("id").GetInt64();
            }

            var adjustResp = await client.PostAsync($"/api/accounts/{bankAccId}/adjust", JsonBody(new
            {
                type = "withdrawal",
                amount = 500,
                reason = "Bank service fee"
            }));
            adjustResp.StatusCode.Should().Be(HttpStatusCode.OK, await adjustResp.Content.ReadAsStringAsync());

            var accTxnsResp = await client.GetAsync($"/api/accounts/{bankAccId}/transactions");
            accTxnsResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var doc = JsonDocument.Parse(await accTxnsResp.Content.ReadAsStringAsync()))
            {
                var adjTxn = doc.RootElement.GetProperty("transactions").GetProperty("data").EnumerateArray()
                    .First(t => t.GetProperty("type").GetString() == "adjustment");
                adjTxn.GetProperty("amount").GetDecimal().Should().Be(-500m);
            }

            // 4. Payments: Supplier WHT net_cash_disbursed persisted & reversed accurately (not over-credited!)
            var supPayResp = await client.PostAsync("/api/payments", JsonBody(new
            {
                party_type = "supplier",
                party_id = supplierId,
                payment_date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                amount = 1000,
                wht_percentage = 5,
                payment_method = "bank_transfer",
                account_id = bankAccId,
                notes = "Wave 6 WHT test"
            }));
            supPayResp.StatusCode.Should().Be(HttpStatusCode.Created, await supPayResp.Content.ReadAsStringAsync());
            long supPayId;
            using (var doc = JsonDocument.Parse(await supPayResp.Content.ReadAsStringAsync()))
            {
                supPayId = doc.RootElement.GetProperty("payment").GetProperty("id").GetInt64();
            }

            var payDbRow = await db.QuerySingleOrDefaultAsync("SELECT net_cash_disbursed, wht_amount FROM payments WHERE id = @id", new { id = supPayId });
            AccountService.ToDecimal(payDbRow!["net_cash_disbursed"]).Should().Be(950m);
            var bankBalAfterPay = await db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = bankAccId });
            bankBalAfterPay.Should().Be(8550m); // 10000 - 500 - 950

            var revPayResp = await client.PostAsync($"/api/payments/{supPayId}/reverse", JsonBody(new { reason = "QA WHT reversal test" }));
            revPayResp.StatusCode.Should().Be(HttpStatusCode.OK, await revPayResp.Content.ReadAsStringAsync());
            var bankBalAfterRev = await db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = bankAccId });
            bankBalAfterRev.Should().Be(9500m); // Restored exactly 950 (not 1000!)

            // 5. Customer & Supplier Ledger signed print URLs + settle-invoice & reversal
            var unsignedCustPrint = await client.GetAsync($"/customer-ledger/print?customer_id={customerId}");
            unsignedCustPrint.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var custPrintUrlResp = await client.GetAsync($"/api/customer-ledger/print-url?customer_id={customerId}");
            custPrintUrlResp.StatusCode.Should().Be(HttpStatusCode.OK, await custPrintUrlResp.Content.ReadAsStringAsync());
            using (var doc = JsonDocument.Parse(await custPrintUrlResp.Content.ReadAsStringAsync()))
            {
                var signedUrl = doc.RootElement.GetProperty("url").GetString()!;
                signedUrl.Should().Contain("signature=");
                (await client.GetAsync(signedUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
            }

            var supPrintUrlResp = await client.GetAsync($"/api/supplier-ledger/print-url?supplier_id={supplierId}");
            supPrintUrlResp.StatusCode.Should().Be(HttpStatusCode.OK, await supPrintUrlResp.Content.ReadAsStringAsync());
            using (var doc = JsonDocument.Parse(await supPrintUrlResp.Content.ReadAsStringAsync()))
            {
                var signedUrl = doc.RootElement.GetProperty("url").GetString()!;
                signedUrl.Should().Contain("signature=");
                (await client.GetAsync(signedUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
            }

            // Create unpaid credit sale invoice and settle via customer-ledger/settle-invoice
            var creditSale = await client.PostAsync("/api/sale-invoices", JsonBody(new
            {
                customer_id = customerId,
                date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                invoice_type = "retail",
                payment_method = "credit",
                total_receive = 0,
                items = new[]
                {
                    new { product_id = prodId, quantity = 5, price = 80, sub_total = 400 }
                }
            }));
            creditSale.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
            long creditSaleId;
            using (var doc = JsonDocument.Parse(await creditSale.Content.ReadAsStringAsync()))
            {
                creditSaleId = doc.RootElement.GetProperty("id").GetInt64();
            }

            var settleResp = await client.PostAsync("/api/customer-ledger/settle-invoice", JsonBody(new
            {
                customer_id = customerId,
                sale_invoice_id = creditSaleId,
                payment_method = "cash",
                record_payment = true
            }));
            settleResp.StatusCode.Should().Be(HttpStatusCode.OK, await settleResp.Content.ReadAsStringAsync());
            long settledPaymentId;
            using (var doc = JsonDocument.Parse(await settleResp.Content.ReadAsStringAsync()))
            {
                settledPaymentId = doc.RootElement.GetProperty("payment_id").GetInt64();
            }

            var settledPayRow = await db.QuerySingleOrDefaultAsync("SELECT created_by_id, unapplied_amount FROM payments WHERE id = @id", new { id = settledPaymentId });
            settledPayRow!["created_by_id"].Should().NotBeNull();
            var settledAccTxn = await db.QuerySingleOrDefaultAsync("SELECT type FROM account_transactions WHERE reference_id = @id ORDER BY id DESC LIMIT 1", new { id = settledPaymentId });
            settledAccTxn!["type"]?.ToString().Should().Be("deposit");
            var settledDrawerTxn = await db.QuerySingleOrDefaultAsync("SELECT type FROM cash_drawer_transactions WHERE reference_type = 'payment' AND reference_id = @id", new { id = settledPaymentId });
            settledDrawerTxn.Should().NotBeNull();

            // 6. Non-admin Finance Specialist permissions: ExpenseCategory, Accounting Overview, Financial Statements PDF, HR & Payroll
            var finToken = await LoginAsync(client, "qa-w56-finance@karobar.local", "FinancePass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", finToken);

            var expCatCreate = await client.PostAsync("/api/expense-categories", JsonBody(new
            {
                name = $"QA Wave6 Cat {Guid.NewGuid():N}"[..24],
                description = "Perm test"
            }));
            expCatCreate.StatusCode.Should().Be(HttpStatusCode.Created, await expCatCreate.Content.ReadAsStringAsync());

            var accOverview = await client.GetAsync("/api/accounting/overview");
            accOverview.StatusCode.Should().Be(HttpStatusCode.OK, await accOverview.Content.ReadAsStringAsync());

            var finPdf = await client.GetAsync("/api/reports/financial-statements/pdf?download=1");
            finPdf.StatusCode.Should().Be(HttpStatusCode.OK);
            var pdfBytes = await finPdf.Content.ReadAsByteArrayAsync();
            Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(5, pdfBytes.Length)).Should().Be("%PDF-");
            pdfBytes.Length.Should().BeGreaterThan(700); // Real financial statements content, not 1-line stub

            // HR & Payroll via `payroll.manage` permission: Employee -> Cash Advance -> Payroll Run -> Pay via bank_transfer
            var empCreate = await client.PostAsync("/api/hr/employees", JsonBody(new
            {
                name = "QA Pharmacist",
                designation = "Pharmacist",
                basic_salary = 60000,
                payment_method = "bank_transfer"
            }));
            empCreate.StatusCode.Should().Be(HttpStatusCode.Created, await empCreate.Content.ReadAsStringAsync());
            long empId;
            using (var doc = JsonDocument.Parse(await empCreate.Content.ReadAsStringAsync()))
            {
                empId = doc.RootElement.GetProperty("id").GetInt64();
            }

            var advResp = await client.PostAsync("/api/hr/advances", JsonBody(new
            {
                employee_id = empId,
                type = "advance",
                amount = 2000,
                payment_method = "cash",
                description = "Wave 6 cash advance"
            }));
            advResp.StatusCode.Should().Be(HttpStatusCode.Created, await advResp.Content.ReadAsStringAsync());
            long advId;
            using (var doc = JsonDocument.Parse(await advResp.Content.ReadAsStringAsync()))
            {
                advId = doc.RootElement.GetProperty("id").GetInt64();
            }
            var advAccTxn = await db.QuerySingleOrDefaultAsync("SELECT type FROM account_transactions WHERE reference_type = 'employee_advance' AND reference_id = @id", new { id = advId });
            advAccTxn!["type"]?.ToString().Should().Be("withdrawal");
            var advDrawerTxn = await db.QuerySingleOrDefaultAsync("SELECT type FROM cash_drawer_transactions WHERE reference_type = 'employee_advance' AND reference_id = @id", new { id = advId });
            advDrawerTxn!["type"]?.ToString().Should().Be("cash_out");

            var runResp = await client.PostAsync("/api/hr/payroll-runs/generate", JsonBody(new { period_month = DateTime.UtcNow.ToString("yyyy-MM") }));
            runResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
            long payrollItemId;
            using (var doc = JsonDocument.Parse(await runResp.Content.ReadAsStringAsync()))
            {
                payrollItemId = doc.RootElement.GetProperty("items").EnumerateArray()
                    .First(it => it.GetProperty("employee_id").GetInt64() == empId)
                    .GetProperty("id").GetInt64();
            }

            var payItemResp = await client.PostAsync($"/api/hr/payroll-items/{payrollItemId}/pay", JsonBody(new
            {
                payment_method = "bank_transfer",
                account_id = bankAccId
            }));
            payItemResp.StatusCode.Should().Be(HttpStatusCode.OK, await payItemResp.Content.ReadAsStringAsync());
            long payrollExpenseId;
            using (var doc = JsonDocument.Parse(await payItemResp.Content.ReadAsStringAsync()))
            {
                payrollExpenseId = doc.RootElement.GetProperty("expense_id").GetInt64();
            }
            var payrollExpRow = await db.QuerySingleOrDefaultAsync("SELECT payment_method FROM expenses WHERE id = @id", new { id = payrollExpenseId });
            payrollExpRow!["payment_method"]?.ToString().Should().Be("bank_transfer");
            var payrollAccTxn = await db.QuerySingleOrDefaultAsync("SELECT type FROM account_transactions WHERE reference_type = 'expense' AND reference_id = @id", new { id = payrollExpenseId });
            payrollAccTxn!["type"]?.ToString().Should().Be("withdrawal");
        }
        finally
        {
            await CleanupAsync(app, client, tempRoot, prevRoot);
        }
    }
}

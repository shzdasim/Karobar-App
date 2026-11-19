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

public class Wave3And4LiveHttpIntegrationTests
{
    private static async Task<(WebApplication App, HttpClient Client, KarobarDb Db, string TempRoot, string? PrevRoot)> StartLiveTestServerAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_wave34_{Guid.NewGuid():N}");
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

        var port = KestrelHostServer.FindFreePort(19400);
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
        var adminRow = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'qa-w34-admin@karobar.local'");
        var adminId = adminRow != null
            ? Convert.ToInt64(adminRow["id"])
            : await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "QA Wave34 Admin",
                ["email"] = "qa-w34-admin@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("AdminPass123!"),
                ["status"] = "active"
            });

        if (await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = 'Admin'") == null)
            await db.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "Admin", ["guard_name"] = "web" });
        await db.SyncUserRolesAsync(adminId, new[] { "Admin" });

        // Non-admin demand clerk with ONLY plural `user-demands.view`, `user-demands.create`, `product.create`
        var clerkRow = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'qa-demand-clerk@karobar.local'");
        var clerkId = clerkRow != null
            ? Convert.ToInt64(clerkRow["id"])
            : await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "QA Demand Clerk",
                ["email"] = "qa-demand-clerk@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("ClerkPass123!"),
                ["status"] = "active"
            });
        foreach (var pName in new[] { "user-demands.view", "user-demands.create", "product.create", "purchase-order.generate" })
        {
            if (await db.QuerySingleOrDefaultAsync("SELECT id FROM permissions WHERE name = @pName", new { pName }) == null)
                await db.InsertAsync("permissions", new Dictionary<string, object?> { ["name"] = pName, ["guard_name"] = "web" });
        }
        await db.SyncUserPermissionsAsync(clerkId, new[] { "user-demands.view", "user-demands.create", "product.create", "purchase-order.generate" });

        // Disable operator PIN requirement for automated HTTP tests
        if (await db.HasTableAsync("settings"))
        {
            var st = await db.QuerySingleOrDefaultAsync("SELECT id FROM settings ORDER BY id ASC LIMIT 1");
            if (st != null)
            {
                await db.UpdateAsync("settings", st["id"]!, new Dictionary<string, object?> { ["require_operator_pin"] = 0 });
            }
        }

        // Ensure default cash drawer account exists
        if (await db.HasTableAsync("accounts") && await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM accounts WHERE type = 'cash' AND is_default = 1") == 0)
        {
            await db.InsertAsync("accounts", new Dictionary<string, object?>
            {
                ["name"] = "Cash Drawer",
                ["code"] = "CASH-DRAWER",
                ["type"] = "cash",
                ["opening_balance"] = 50000m,
                ["current_balance"] = 50000m,
                ["is_default"] = 1,
                ["is_active"] = 1
            });
        }

        var catId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM categories ORDER BY id ASC LIMIT 1), 0)");
        if (catId <= 0) catId = await db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "QA Cat" });
        var brId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM brands ORDER BY id ASC LIMIT 1), 0)");
        if (brId <= 0) brId = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "QA Brand" });
        var supId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM suppliers ORDER BY id ASC LIMIT 1), 0)");
        if (supId <= 0) supId = await db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "QA Supplier" });
        var custId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM customers ORDER BY id ASC LIMIT 1), 0)");
        if (custId <= 0) custId = await db.InsertAsync("customers", new Dictionary<string, object?> { ["name"] = "Walk-in QA", ["is_regular"] = 0 });
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var resp = await client.PostAsync("/api/login", new StringContent(
            JsonSerializer.Serialize(new { email, password }), Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    [Fact]
    public async Task Wave3_Sales_DosageLabels_Drafts_DocumentHistory_And_AccountTxns_Pass_Over_Live_Http()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            var token = await LoginAsync(client, "qa-w34-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // 1. Dosage labels route must NOT be swallowed by SPA shell middleware
            KestrelHostServer.IsSpaShellRequest("GET", "/sale-invoices/1/dosage-labels?auto_print=1&width_mm=50&height_mm=25")
                .Should().BeFalse("dosage-labels print URL must reach SaleInvoiceController instead of returning index.blade.php");

            // 2. Core Wave 3 index/lookup endpoints
            (await client.GetAsync("/api/sale-invoices?page=1&per_page=10&posted=&customer=")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/sale-invoices/new-code")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/sale-returns?page=1")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/trade-schemes")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/trade-schemes/active-lookup")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/operator-pins/directory")).StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. DocumentHistoryModal supplier_payment & customer_payment filters must NOT return 400
            (await client.GetAsync("/api/document-histories?entity_type=supplier_payment")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/document-histories?entity_type=customer_payment")).StatusCode.Should().Be(HttpStatusCode.OK);

            // 4. Create a test product with stock, create a draft, reload parked carts via ?shared=1&mine_only=0
            var catId = await db.ExecuteScalarAsync<long>("SELECT id FROM categories ORDER BY id ASC LIMIT 1");
            var brId = await db.ExecuteScalarAsync<long>("SELECT id FROM brands ORDER BY id ASC LIMIT 1");
            var supId = await db.ExecuteScalarAsync<long>("SELECT id FROM suppliers ORDER BY id ASC LIMIT 1");
            var custId = await db.ExecuteScalarAsync<long>("SELECT id FROM customers ORDER BY id ASC LIMIT 1");
            var prodId = await db.InsertAsync("products", new Dictionary<string, object?>
            {
                ["product_code"] = $"W3-{Guid.NewGuid():N}"[..12],
                ["name"] = $"Wave3 Test Med {Guid.NewGuid():N}"[..24],
                ["barcode"] = $"99{Random.Shared.Next(10000000, 99999999)}",
                ["narcotic"] = "no",
                ["category_id"] = catId,
                ["brand_id"] = brId,
                ["supplier_id"] = supId,
                ["pack_size"] = 10,
                ["quantity"] = 50,
                ["pack_purchase_price"] = 100m,
                ["pack_sale_price"] = 150m,
                ["unit_purchase_price"] = 10m,
                ["unit_sale_price"] = 15m,
                ["avg_price"] = 10m
            });
            await db.InsertAsync("batches", new Dictionary<string, object?>
            {
                ["product_id"] = prodId,
                ["batch_number"] = "W3-B1",
                ["expiry_date"] = DateTime.UtcNow.AddDays(180).ToString("yyyy-MM-dd"),
                ["quantity"] = 50,
                ["unit_purchase_price"] = 10m,
                ["unit_sale_price"] = 15m
            });

            // Hold a draft & verify Index returns parsed payload + party_id + total with ?shared=1&mine_only=0
            var draftResp = await client.PostAsync("/api/invoice-drafts", new StringContent(
                JsonSerializer.Serialize(new
                {
                    type = "sale_retail",
                    title = "POS Parked Cart",
                    payload = new
                    {
                        customer_id = custId,
                        total = 30,
                        items = new[] { new { product_id = prodId, quantity = 2, price = 15, sub_total = 30 } }
                    }
                }), Encoding.UTF8, "application/json"));
            draftResp.StatusCode.Should().Be(HttpStatusCode.Created);

            var draftsListResp = await client.GetAsync("/api/invoice-drafts?type=sale_retail&shared=1&mine_only=0");
            draftsListResp.StatusCode.Should().Be(HttpStatusCode.OK, "?shared=1&mine_only=0 must not 422");
            using (var dlDoc = JsonDocument.Parse(await draftsListResp.Content.ReadAsStringAsync()))
            {
                var foundDraft = dlDoc.RootElement.EnumerateArray().FirstOrDefault(e => e.GetProperty("title").GetString() == "POS Parked Cart");
                foundDraft.ValueKind.Should().Be(JsonValueKind.Object);
                foundDraft.GetProperty("payload").ValueKind.Should().Be(JsonValueKind.Object, "Barcode POS needs parsed payload object");
                foundDraft.GetProperty("payload").GetProperty("items").GetArrayLength().Should().Be(1);
                foundDraft.TryGetProperty("party_id", out _).Should().BeTrue();
                foundDraft.TryGetProperty("total", out _).Should().BeTrue();
            }

            // 5. Create Sale Invoice -> Verify dosage-labels?json=1 -> Verify account_transactions type='deposit' & user_id != NULL -> Reverse
            var saleResp = await client.PostAsync("/api/sale-invoices", new StringContent(
                JsonSerializer.Serialize(new
                {
                    customer_id = custId,
                    sale_type = "retail",
                    invoice_type = "debit",
                    payment_method = "cash",
                    date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                    total_receive = 75,
                    items = new[]
                    {
                        new
                        {
                            product_id = prodId,
                            batch_number = "W3-B1",
                            expiry = DateTime.UtcNow.AddDays(180).ToString("yyyy-MM-dd"),
                            quantity = 5,
                            price = 15,
                            sub_total = 75,
                            dosage_instruction = "1 tab twice daily"
                        }
                    }
                }), Encoding.UTF8, "application/json"));
            var saleBody = await saleResp.Content.ReadAsStringAsync();
            saleResp.StatusCode.Should().Be(HttpStatusCode.Created, $"sale create failed: {saleBody}");
            using var saleDoc = JsonDocument.Parse(saleBody);
            var saleId = saleDoc.RootElement.GetProperty("id").GetInt64();

            // Verify stock dropped 50 -> 45
            (await db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = prodId })).Should().Be(45);

            // Verify account_transactions uses 'deposit' (NOT 'credit') and populates user_id (NOT NULL)
            var accTxn = await db.QuerySingleOrDefaultAsync(
                "SELECT type, user_id, amount FROM account_transactions WHERE reference_type = 'sale_invoice' AND reference_id = @id ORDER BY id DESC LIMIT 1",
                new { id = saleId });
            accTxn.Should().NotBeNull();
            accTxn!["type"]?.ToString().Should().Be("deposit");
            accTxn["user_id"].Should().NotBeNull();

            // Verify dosage labels endpoint with ?json=1&width_mm=50&height_mm=25
            var dosageResp = await client.GetAsync($"/sale-invoices/{saleId}/dosage-labels?json=1&width_mm=50&height_mm=25");
            dosageResp.StatusCode.Should().Be(HttpStatusCode.OK, "dosage-labels?json=1 must not 422");
            var dosageText = await dosageResp.Content.ReadAsStringAsync();
            dosageText.Should().Contain("50mm 25mm");
            dosageText.Should().Contain("1 tab twice daily");

            // Reverse sale invoice -> verify stock restored 45 -> 50 and withdrawal txn written with type='withdrawal'
            var revResp = await client.PostAsync($"/api/sale-invoices/{saleId}/reverse", new StringContent(
                JsonSerializer.Serialize(new { reason = "QA reversal check" }), Encoding.UTF8, "application/json"));
            revResp.StatusCode.Should().Be(HttpStatusCode.OK);
            (await db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = prodId })).Should().Be(50);

            var revTxn = await db.QuerySingleOrDefaultAsync(
                "SELECT type, user_id FROM account_transactions WHERE reference_type = 'sale_invoice_reversal' AND reference_id = @id ORDER BY id DESC LIMIT 1",
                new { id = saleId });
            revTxn.Should().NotBeNull();
            revTxn!["type"]?.ToString().Should().Be("withdrawal");
            revTxn["user_id"].Should().NotBeNull();
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            client.Dispose();
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Wave4_PurchaseEditNetStockDiff_PurchaseOrders_And_UserDemands_Pass_Over_Live_Http()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            var adminToken = await LoginAsync(client, "qa-w34-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            var catId = await db.ExecuteScalarAsync<long>("SELECT id FROM categories ORDER BY id ASC LIMIT 1");
            var brId = await db.ExecuteScalarAsync<long>("SELECT id FROM brands ORDER BY id ASC LIMIT 1");
            var supId = await db.ExecuteScalarAsync<long>("SELECT id FROM suppliers ORDER BY id ASC LIMIT 1");
            var prodId = await db.InsertAsync("products", new Dictionary<string, object?>
            {
                ["product_code"] = $"W4-{Guid.NewGuid():N}"[..12],
                ["name"] = $"Wave4 StockDiff Med {Guid.NewGuid():N}"[..24],
                ["barcode"] = $"88{Random.Shared.Next(10000000, 99999999)}",
                ["narcotic"] = "no",
                ["category_id"] = catId,
                ["brand_id"] = brId,
                ["supplier_id"] = supId,
                ["pack_size"] = 10,
                ["quantity"] = 0,
                ["pack_purchase_price"] = 100m,
                ["pack_sale_price"] = 150m,
                ["unit_purchase_price"] = 10m,
                ["unit_sale_price"] = 15m,
                ["avg_price"] = 0m
            });

            var expiry = DateTime.UtcNow.AddDays(365).ToString("yyyy-MM-dd");
            var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

            // 1. Create Purchase Invoice buying 100 units (10 packs * 10)
            var piResp = await client.PostAsync("/api/purchase-invoices", new StringContent(
                JsonSerializer.Serialize(new
                {
                    supplier_id = supId,
                    invoice_type = "debit",
                    payment_method = "cash",
                    posted_date = today,
                    invoice_number = $"W4-INV-{Random.Shared.Next(1000, 9999)}",
                    total_paid = 1000,
                    items = new[]
                    {
                        new
                        {
                            product_id = prodId,
                            batch = "W4-BATCH-1",
                            expiry,
                            pack_size = 10,
                            pack_quantity = 10,
                            unit_quantity = 100,
                            pack_purchase_price = 100,
                            unit_purchase_price = 10,
                            pack_sale_price = 150,
                            unit_sale_price = 15
                        }
                    }
                }), Encoding.UTF8, "application/json"));
            var piBody = await piResp.Content.ReadAsStringAsync();
            piResp.StatusCode.Should().Be(HttpStatusCode.Created, $"purchase invoice create failed: {piBody}");
            using var piDoc = JsonDocument.Parse(piBody);
            var piId = piDoc.RootElement.GetProperty("id").GetInt64();
            var piInvNo = piDoc.RootElement.GetProperty("invoice_number").GetString()!;

            // Verify 100 units in product & batch, and account_transactions type='withdrawal' with user_id populated
            (await db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = prodId })).Should().Be(100);
            (await db.ExecuteScalarAsync<int>("SELECT quantity FROM batches WHERE product_id = @id AND batch_number = 'W4-BATCH-1'", new { id = prodId })).Should().Be(100);
            var piAccTxn = await db.QuerySingleOrDefaultAsync(
                "SELECT type, user_id FROM account_transactions WHERE reference_type = 'purchase_invoice' AND reference_id = @id ORDER BY id DESC LIMIT 1",
                new { id = piId });
            piAccTxn.Should().NotBeNull();
            piAccTxn!["type"]?.ToString().Should().Be("withdrawal");
            piAccTxn["user_id"].Should().NotBeNull();

            // 2. Simulate selling 60 units (40 units remain on hand & in batch)
            await db.ExecuteAsync("UPDATE products SET quantity = 40 WHERE id = @id", new { id = prodId });
            await db.ExecuteAsync("UPDATE batches SET quantity = 40 WHERE product_id = @id AND batch_number = 'W4-BATCH-1'", new { id = prodId });

            // 3. Edit Purchase Invoice remarks (keeping purchased qty = 100): MUST preserve 40 on-hand units (NOT inflate to 100!)
            var piEditResp = await client.PutAsync($"/api/purchase-invoices/{piId}", new StringContent(
                JsonSerializer.Serialize(new
                {
                    supplier_id = supId,
                    invoice_type = "debit",
                    payment_method = "cash",
                    posted_date = today,
                    invoice_number = piInvNo,
                    remarks = "Updated remarks after partial sale",
                    total_paid = 1000,
                    items = new[]
                    {
                        new
                        {
                            product_id = prodId,
                            batch = "W4-BATCH-1",
                            expiry,
                            pack_size = 10,
                            pack_quantity = 10,
                            unit_quantity = 100,
                            pack_purchase_price = 100,
                            unit_purchase_price = 10,
                            pack_sale_price = 150,
                            unit_sale_price = 15
                        }
                    }
                }), Encoding.UTF8, "application/json"));
            var piEditBody = await piEditResp.Content.ReadAsStringAsync();
            piEditResp.StatusCode.Should().Be(HttpStatusCode.OK, $"purchase edit failed: {piEditBody}");
            (await db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = prodId }))
                .Should().Be(40, "net-delta edit must NEVER clamp sold stock to 0 and re-add +60 phantom units");
            (await db.ExecuteScalarAsync<int>("SELECT quantity FROM batches WHERE product_id = @id AND batch_number = 'W4-BATCH-1'", new { id = prodId }))
                .Should().Be(40, "batch quantity must stay at 40 after remarks edit");

            // 4. Purchase Orders date validation & non-admin Forecast with `purchase-order.generate`
            var badPoResp = await client.PostAsync("/api/purchase-orders", new StringContent(
                JsonSerializer.Serialize(new
                {
                    supplier_id = supId,
                    date = "2026-11-20",
                    expected_received_date = "2026-11-10",
                    items = new[] { new { product_id = prodId, ordered_quantity = 10, pack_size = 10, unit_purchase_price = 10 } }
                }), Encoding.UTF8, "application/json"));
            badPoResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "expected_received_date < date must return 422");

            // 5. Non-admin clerk with plural `user-demands.view` / `user-demands.create` / `product.create` / `purchase-order.generate`
            var clerkToken = await LoginAsync(client, "qa-demand-clerk@karobar.local", "ClerkPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", clerkToken);

            (await client.GetAsync("/api/user-demands/count")).StatusCode.Should().Be(HttpStatusCode.OK, "plural user-demands.view must be authorized");
            (await client.GetAsync("/api/user-demands")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/products/search?q=Wave4")).StatusCode.Should().Be(HttpStatusCode.OK, "ProductController lookup must allow user-demands.view");
            (await client.GetAsync($"/api/purchase-orders/forecast?date_from={today}&date_to={today}&projected_days=14")).StatusCode.Should().Be(HttpStatusCode.OK, "purchase-order.generate must authorize forecast");

            // Creating a demand for a brand-new product WITHOUT brand_id/category_id must succeed (201) without NOT NULL 500
            var newDemandResp = await client.PostAsync("/api/user-demands", new StringContent(
                JsonSerializer.Serialize(new
                {
                    name = $"Brand New Demand Item {Guid.NewGuid():N}"[..30],
                    requested_quantity = 3,
                    notes = "Customer requested"
                }), Encoding.UTF8, "application/json"));
            var newDemandBody = await newDemandResp.Content.ReadAsStringAsync();
            newDemandResp.StatusCode.Should().Be(HttpStatusCode.Created, $"new product demand without brand_id/category_id must not 500: {newDemandBody}");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            client.Dispose();
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }
}

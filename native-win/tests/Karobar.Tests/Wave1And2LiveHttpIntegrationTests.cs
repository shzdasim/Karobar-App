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

public class Wave1And2LiveHttpIntegrationTests
{
    private static async Task<(WebApplication App, HttpClient Client, KarobarDb Db, string TempRoot, string? PrevRoot)> StartLiveTestServerAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_wave12_{Guid.NewGuid():N}");
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
        await EnsureTestUsersAndSeedDataAsync(kDb);

        var port = KestrelHostServer.FindFreePort(19200);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddSingleton(kDb);
        builder.Services.AddSingleton(new LicenseService(kDb));
        builder.Services.AddSingleton(new BackupService(kDb, new SqliteExportService(kDb)));
        builder.Services.AddSingleton(new OperatorPinService(kDb));
        builder.Services.AddSingleton(new SyncPulseService(kDb));
        builder.Services.AddKarobarApi(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(KarobarApiServiceExtensions).Assembly);

        var app = builder.Build();

        // Mirror exact KestrelHostServer Laravel _method spoofing BEFORE UseRouting()
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                string? spoofed = null;
                if (context.Request.Query.TryGetValue("_method", out var qMethod) && !string.IsNullOrWhiteSpace(qMethod))
                {
                    spoofed = qMethod.ToString().Trim().ToUpperInvariant();
                }
                else if (context.Request.HasFormContentType)
                {
                    context.Request.EnableBuffering();
                    var form = await context.Request.ReadFormAsync(context.RequestAborted);
                    if (form.TryGetValue("_method", out var fMethod) && !string.IsNullOrWhiteSpace(fMethod))
                    {
                        spoofed = fMethod.ToString().Trim().ToUpperInvariant();
                    }
                }

                if (spoofed is "PUT" or "PATCH" or "DELETE")
                {
                    context.Request.Method = spoofed;
                }
            }

            await next();
        });
        app.UseRouting();

        // Mirror empty-query-string stripping from UseKarobarApiPipeline
        app.Use(async (context, next) =>
        {
            if (context.Request.QueryString.HasValue)
            {
                var parsed = QueryHelpers.ParseQuery(context.Request.QueryString.Value);
                if (parsed.Any(kv => kv.Value.Any(string.IsNullOrWhiteSpace)))
                {
                    var cleaned = new List<KeyValuePair<string, string?>>();
                    foreach (var kv in parsed)
                    {
                        foreach (var v in kv.Value)
                        {
                            if (!string.IsNullOrWhiteSpace(v))
                                cleaned.Add(new KeyValuePair<string, string?>(kv.Key, v));
                        }
                    }
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

    private static async Task EnsureTestUsersAndSeedDataAsync(KarobarDb db)
    {
        var adminHash = SanctumAuthenticationHandler.HashLaravelBcryptPassword("AdminPass123!");
        var existingAdmin = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'qa-admin@karobar.local'");
        long adminId;
        if (existingAdmin == null)
        {
            adminId = await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "QA Admin",
                ["email"] = "qa-admin@karobar.local",
                ["password"] = adminHash,
                ["status"] = "active"
            });
        }
        else
        {
            adminId = Convert.ToInt64(existingAdmin["id"]);
        }

        if (await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = 'Admin'") == null)
        {
            await db.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "Admin", ["guard_name"] = "web" });
        }
        if (await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = 'Cashier'") == null)
        {
            await db.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "Cashier", ["guard_name"] = "web" });
        }
        await db.SyncUserRolesAsync(adminId, new[] { "Admin" });

        var existingCashier = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'qa-cashier@karobar.local'");
        long cashierId;
        if (existingCashier == null)
        {
            cashierId = await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "QA Cashier",
                ["email"] = "qa-cashier@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("CashierPass123!"),
                ["status"] = "active"
            });
        }
        else
        {
            cashierId = Convert.ToInt64(existingCashier["id"]);
        }
        await db.SyncUserRolesAsync(cashierId, new[] { "Cashier" });

        // Ensure at least 1 category, brand, supplier, customer, doctor, product, batch, storage_location exist for CI fresh DBs
        var catId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM categories ORDER BY id ASC LIMIT 1), 0)");
        if (catId <= 0) catId = await db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "QA Category" });

        var brandId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM brands ORDER BY id ASC LIMIT 1), 0)");
        if (brandId <= 0) brandId = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "QA Brand" });

        var supId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM suppliers ORDER BY id ASC LIMIT 1), 0)");
        if (supId <= 0) supId = await db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "QA Supplier", ["phone"] = "03001112223" });

        var custId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM customers ORDER BY id ASC LIMIT 1), 0)");
        if (custId <= 0) custId = await db.InsertAsync("customers", new Dictionary<string, object?> { ["name"] = "QA Customer", ["phone"] = "03009998887", ["is_regular"] = 0 });

        var docId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM doctors ORDER BY id ASC LIMIT 1), 0)");
        if (docId <= 0) docId = await db.InsertAsync("doctors", new Dictionary<string, object?> { ["name"] = "Dr. QA" });

        var prodId = await db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM products ORDER BY id ASC LIMIT 1), 0)");
        if (prodId <= 0)
        {
            prodId = await db.InsertAsync("products", new Dictionary<string, object?>
            {
                ["product_code"] = "QA-P001",
                ["name"] = "QA Panadol 500mg",
                ["category_id"] = catId,
                ["brand_id"] = brandId,
                ["supplier_id"] = supId,
                ["pack_size"] = 10,
                ["quantity"] = 50,
                ["pack_purchase_price"] = 100m,
                ["pack_sale_price"] = 150m,
                ["unit_purchase_price"] = 10m,
                ["unit_sale_price"] = 15m
            });
        }

        if (await db.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM batches") == 0)
        {
            await db.InsertAsync("batches", new Dictionary<string, object?>
            {
                ["product_id"] = prodId,
                ["batch_number"] = "QA-B001",
                ["expiry_date"] = DateTime.UtcNow.AddDays(90).ToString("yyyy-MM-dd"),
                ["quantity"] = 50,
                ["unit_purchase_price"] = 10m,
                ["unit_sale_price"] = 15m
            });
        }

        if (await db.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM storage_locations") == 0)
        {
            await db.InsertAsync("storage_locations", new Dictionary<string, object?>
            {
                ["name"] = "Shop Counter",
                ["code"] = "SHOP-1",
                ["type"] = "shop",
                ["is_default"] = 1,
                ["is_active"] = 1
            });
        }
    }

    private static async Task<string> LoginAndGetTokenAsync(HttpClient client, string email, string password, bool remember = false)
    {
        var resp = await client.PostAsync("/api/login", new StringContent(
            JsonSerializer.Serialize(new { email, password, remember }),
            Encoding.UTF8,
            "application/json"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"login for {email} should succeed");
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var token = doc.RootElement.GetProperty("token").GetString();
        token.Should().NotBeNullOrWhiteSpace();
        return token!;
    }

    [Fact]
    public async Task Wave1_Auth_UtcExpiry_ResetToken_UserSecurity_Setup_And_Notifications_Pass_Over_Live_Http()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            // 1. Non-remember-me login (20-min UTC token) MUST work immediately on any timezone (incl. UTC+5 PKT!)
            var adminToken = await LoginAndGetTokenAsync(client, "qa-admin@karobar.local", "AdminPass123!", remember: false);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            var meResp = await client.GetAsync("/api/me");
            meResp.StatusCode.Should().Be(HttpStatusCode.OK, "UTC expires_at must not be parsed as local timezone causing instant 401");

            // 2. Concurrent /api/license/status calls must not mutate shared cached dictionary
            var licTasks = Enumerable.Range(0, 5).Select(_ => client.GetAsync("/api/license/status")).ToArray();
            var licResps = await Task.WhenAll(licTasks);
            licResps.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);

            // 3. Reset-password MUST reject arbitrary/forged tokens with 422
            var forgedReset = await client.PostAsync("/api/reset-password", new StringContent(
                JsonSerializer.Serialize(new
                {
                    email = "qa-admin@karobar.local",
                    token = "forged-random-token-12345",
                    password = "HackedPassword123!",
                    password_confirmation = "HackedPassword123!"
                }),
                Encoding.UTF8,
                "application/json"));
            forgedReset.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "forged reset token must be rejected");

            // 4. Forgot-password stores hashed token in password_reset_tokens
            var forgotResp = await client.PostAsync("/api/forgot-password", new StringContent(
                JsonSerializer.Serialize(new { email = "qa-admin@karobar.local" }),
                Encoding.UTF8,
                "application/json"));
            forgotResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var storedResetCount = await db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM password_reset_tokens WHERE LOWER(email) = 'qa-admin@karobar.local'");
            storedResetCount.Should().Be(1);

            // 5. GET /api/roles?per_page=1000 must clamp to 500 instead of resetting to 15
            var rolesResp = await client.GetAsync("/api/roles?per_page=1000");
            rolesResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var rolesDoc = JsonDocument.Parse(await rolesResp.Content.ReadAsStringAsync()))
            {
                rolesDoc.RootElement.GetProperty("per_page").GetInt32().Should().Be(500);
            }

            // 6. Non-admin cashier MUST be blocked from POST /api/users/bulk-action privilege escalation (403)
            var cashierToken = await LoginAndGetTokenAsync(client, "qa-cashier@karobar.local", "CashierPass123!", remember: false);
            using (var cashierReq = new HttpRequestMessage(HttpMethod.Post, "/api/users/bulk-action"))
            {
                cashierReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
                cashierReq.Content = new StringContent(
                    JsonSerializer.Serialize(new { action = "assign_role", role = "Admin", user_ids = new[] { 1 } }),
                    Encoding.UTF8,
                    "application/json");
                var escResp = await client.SendAsync(cashierReq);
                escResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "non-admin must never escalate via bulk-action");
            }

            // 7. Settings Update preserves saved secrets when blank "" is submitted and respects explicit is_setup_completed=0
            var set1 = await client.PostAsync("/api/settings", new StringContent(
                JsonSerializer.Serialize(new { store_name = "QA Pharmacy", smtp_password = "SuperSecretSmtpPassword!", is_setup_completed = false }),
                Encoding.UTF8,
                "application/json"));
            set1.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var set1Doc = JsonDocument.Parse(await set1.Content.ReadAsStringAsync()))
            {
                set1Doc.RootElement.GetProperty("is_setup_completed").GetBoolean().Should().BeFalse("draft save with is_setup_completed=false must not force complete");
            }

            var set2 = await client.PostAsync("/api/settings", new StringContent(
                JsonSerializer.Serialize(new { store_name = "QA Pharmacy Updated", smtp_password = "" }),
                Encoding.UTF8,
                "application/json"));
            set2.StatusCode.Should().Be(HttpStatusCode.OK);
            var savedSmtp = await db.ExecuteScalarAsync<string>("SELECT smtp_password FROM settings ORDER BY id ASC LIMIT 1");
            savedSmtp.Should().Be("SuperSecretSmtpPassword!", "blank secret submission must not overwrite saved secret");

            // 8. Setup state, notifications, sale-system, currency, legal pages
            (await client.GetAsync("/api/setup/state")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/sale-system")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/currency")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/notifications/expiry-alerts")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/terms-of-service")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/privacy-policy")).StatusCode.Should().Be(HttpStatusCode.OK);
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
    public async Task Wave2_Dashboard_Catalog_MethodSpoofing_BooleanQueryParams_And_ImportTemplates_Pass_Over_Live_Http()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            var adminToken = await LoginAndGetTokenAsync(client, "qa-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            // 1. Dashboard endpoints
            foreach (var ep in new[]
            {
                "/api/dashboard/summary",
                "/api/dashboard/kpi-metrics",
                "/api/dashboard/top-products",
                "/api/dashboard/sales-by-brands",
                "/api/dashboard/invoice-counts",
                "/api/dashboard/near-expiry"
            })
            {
                var r = await client.GetAsync(ep);
                r.StatusCode.Should().Be(HttpStatusCode.OK, $"{ep} must return 200 OK");
            }

            // 2. Boolean query params sent as "1"/"0" by React must NOT fail with 422!
            (await client.GetAsync("/api/storage-locations?active_only=1")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/batch-expiry?include_zero_qty=1&per_page=10")).StatusCode.Should().Be(HttpStatusCode.OK);

            var prodId = await db.ExecuteScalarAsync<long>("SELECT id FROM products ORDER BY id ASC LIMIT 1");
            (await client.GetAsync($"/api/products/{prodId}/batches?include_unbatched=1")).StatusCode.Should().Be(HttpStatusCode.OK);

            // 3. Products listing with empty filters
            var prodResp = await client.GetAsync("/api/products?page=1&per_page=10&low_stock=&category_id=&brand_id=&supplier_id=");
            prodResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var prodDoc = JsonDocument.Parse(await prodResp.Content.ReadAsStringAsync()))
            {
                prodDoc.RootElement.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
            }

            // 4. Laravel _method=PUT spoofing for Brand Edit (FormData _method=PUT) and Product Edit (?_method=PUT)
            var brandId = await db.ExecuteScalarAsync<long>("SELECT id FROM brands ORDER BY id ASC LIMIT 1");
            var origBrandName = await db.ExecuteScalarAsync<string>("SELECT name FROM brands WHERE id = @id", new { id = brandId }) ?? "QA Brand";
            using (var brandForm = new MultipartFormDataContent())
            {
                brandForm.Add(new StringContent("PUT"), "_method");
                brandForm.Add(new StringContent(origBrandName), "name");
                var brandEditResp = await client.PostAsync($"/api/brands/{brandId}", brandForm);
                brandEditResp.StatusCode.Should().Be(HttpStatusCode.OK, "POST /api/brands/{id} with FormData _method=PUT must not return 405");
            }

            var prodRow = await db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = prodId });
            var fallbackCatId = await db.ExecuteScalarAsync<long>("SELECT id FROM categories ORDER BY id ASC LIMIT 1");
            var fallbackBrandId = await db.ExecuteScalarAsync<long>("SELECT id FROM brands ORDER BY id ASC LIMIT 1");
            var fallbackSupId = await db.ExecuteScalarAsync<long>("SELECT id FROM suppliers ORDER BY id ASC LIMIT 1");
            using (var prodForm = new MultipartFormDataContent())
            {
                prodForm.Add(new StringContent(prodRow!["name"]?.ToString() ?? "QA Product"), "name");
                prodForm.Add(new StringContent(prodRow["product_code"]?.ToString() ?? $"QA-{prodId}"), "product_code");
                prodForm.Add(new StringContent(string.IsNullOrWhiteSpace(prodRow["barcode"]?.ToString()) ? $"BC-{prodId}" : prodRow["barcode"]!.ToString()!), "barcode");
                prodForm.Add(new StringContent(string.IsNullOrWhiteSpace(prodRow["narcotic"]?.ToString()) ? "no" : prodRow["narcotic"]!.ToString()!), "narcotic");
                prodForm.Add(new StringContent((prodRow["category_id"] ?? fallbackCatId).ToString()!), "category_id");
                prodForm.Add(new StringContent((prodRow["brand_id"] ?? fallbackBrandId).ToString()!), "brand_id");
                prodForm.Add(new StringContent((prodRow["supplier_id"] ?? fallbackSupId).ToString()!), "supplier_id");
                prodForm.Add(new StringContent(Math.Max(1, Convert.ToInt32(prodRow["pack_size"] ?? 1)).ToString()), "pack_size");
                prodForm.Add(new StringContent((prodRow["unit_purchase_price"] ?? 10).ToString()!), "unit_purchase_price");
                prodForm.Add(new StringContent((prodRow["unit_sale_price"] ?? 15).ToString()!), "unit_sale_price");
                var prodEditResp = await client.PostAsync($"/api/products/{prodId}?_method=PUT", prodForm);
                var prodEditBody = await prodEditResp.Content.ReadAsStringAsync();
                prodEditResp.StatusCode.Should().Be(HttpStatusCode.OK, $"POST /api/products/{{id}}?_method=PUT must return 200 OK (body: {prodEditBody})");
            }

            // 5. Customers search & boolean is_regular serialization (not integer 0/1)
            var custResp = await client.GetAsync("/api/customers?search=&per_page=20");
            custResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var custDoc = JsonDocument.Parse(await custResp.Content.ReadAsStringAsync()))
            {
                var firstCust = custDoc.RootElement.EnumerateArray().First();
                firstCust.GetProperty("is_regular").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
            }

            // 6. Suppliers, Doctors, Categories, Formulations
            (await client.GetAsync("/api/suppliers")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/doctors")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/categories")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/formulations")).StatusCode.Should().Be(HttpStatusCode.OK);

            // 7. All 5 static CSV Import Templates MUST work via unauthenticated <a href> (no Bearer header)
            using var anonClient = new HttpClient { BaseAddress = client.BaseAddress };
            foreach (var tpl in new[]
            {
                "/api/categories/import/template",
                "/api/brands/import/template",
                "/api/products/import/template",
                "/api/suppliers/import/template",
                "/api/customers/import/template"
            })
            {
                var tr = await anonClient.GetAsync(tpl);
                tr.StatusCode.Should().Be(HttpStatusCode.OK, $"unauthenticated <a href> download of {tpl} must return 200 OK");
                var csvText = await tr.Content.ReadAsStringAsync();
                csvText.Should().NotBeNullOrWhiteSpace();
            }
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

using System.IO.Compression;
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

public class Wave7And8LiveHttpIntegrationTests
{
    private static async Task<(WebApplication App, HttpClient Client, KarobarDb Db, string TempRoot, string? PrevRoot)> StartLiveTestServerAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_wave78_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "public"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "backups"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "logs"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "public"));

        var tempDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
        var forceEmptyDb = string.Equals(Environment.GetEnvironmentVariable("KAROBAR_TEST_EMPTY_DB"), "1", StringComparison.OrdinalIgnoreCase);
        var sourceFactory = new DbConnectionFactory();
        if (!forceEmptyDb && File.Exists(sourceFactory.SqlitePath) && new FileInfo(sourceFactory.SqlitePath).Length > 0)
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

        var port = KestrelHostServer.FindFreePort(19750);
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
        var adminRow = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'qa-w78-admin@karobar.local'");
        var adminId = adminRow != null
            ? Convert.ToInt64(adminRow["id"])
            : await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "QA Wave78 Admin",
                ["email"] = "qa-w78-admin@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("AdminPass123!"),
                ["status"] = "active"
            });

        if (await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = 'Admin'") == null)
            await db.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "Admin", ["guard_name"] = "web" });
        await db.SyncUserRolesAsync(adminId, new[] { "Admin" });

        // Restricted user with ONLY report.tax-return.view + report.tax-return.export (and nothing else)
        var taxUserRow = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'qa-w78-tax@karobar.local'");
        var taxUserId = taxUserRow != null
            ? Convert.ToInt64(taxUserRow["id"])
            : await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "QA Tax Officer",
                ["email"] = "qa-w78-tax@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("TaxPass123!"),
                ["status"] = "active"
            });

        var taxPerms = new[] { "report.tax-return.view", "report.tax-return.export" };
        foreach (var pName in taxPerms)
        {
            if (await db.QuerySingleOrDefaultAsync("SELECT id FROM permissions WHERE name = @pName", new { pName }) == null)
                await db.InsertAsync("permissions", new Dictionary<string, object?> { ["name"] = pName, ["guard_name"] = "web" });
        }
        await db.SyncUserPermissionsAsync(taxUserId, taxPerms);

        // Ensure at least one settings row exists even on clean CI DB
        if (await db.HasTableAsync("settings"))
        {
            var st = await db.QuerySingleOrDefaultAsync("SELECT id FROM settings ORDER BY id ASC LIMIT 1");
            if (st == null)
            {
                await db.InsertAsync("settings", new Dictionary<string, object?>
                {
                    ["store_name"] = "QA Karobar Pharmacy",
                    ["require_operator_pin"] = 0,
                    ["currency_symbol"] = "Rs.",
                    ["currency_code"] = "PKR",
                    ["currency_decimal_places"] = 2
                });
            }
            else
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

    private static decimal GetFlexibleDecimal(JsonElement el) =>
        el.ValueKind == JsonValueKind.Number
            ? el.GetDecimal()
            : decimal.Parse(el.GetString()!, System.Globalization.CultureInfo.InvariantCulture);

    private static async Task CleanupAsync(WebApplication app, HttpClient client, string tempRoot, string? prevRoot)
    {
        try { await app.StopAsync(); await app.DisposeAsync(); } catch { }
        try { client.Dispose(); } catch { }
        Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
        KarobarDb.FlushSchemaCache();
        try { SqliteConnection.ClearAllPools(); } catch { }
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
    }

    private static async Task AssertRealQuestPdfAsync(HttpClient client, string url, string label)
    {
        var resp = await client.GetAsync(url);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, $"{label} PDF failed: {await resp.Content.ReadAsStringAsync()}");
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf", $"{label} should return application/pdf");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(2000, $"{label} should be a real QuestPDF document, not a tiny ASCII stub");
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-", $"{label} must start with %PDF- magic header");
    }

    [Fact]
    public async Task Wave7_All18AnalyticalReports_And_QuestPdfExports_PassLiveHttp()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            // 1. Auth guards on Reports, SaleInvoices, and StorageLocations
            (await client.GetAsync("/api/reports/cost-of-sale")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.GetAsync("/api/sale-invoices")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.GetAsync("/api/storage-locations")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            var adminToken = await LoginAsync(client, "qa-w78-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            // 2. Seed rich Wave 7 dataset (Narcotic product, near-expiry batch, purchase, sale with Rx & tax, adjustment, reorder item)
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            var fromDate = DateTime.Now.AddDays(-7).ToString("yyyy-MM-dd");
            var toDate = DateTime.Now.AddDays(1).ToString("yyyy-MM-dd");
            var nearExpDate = DateTime.Now.AddDays(25).ToString("yyyy-MM-dd");

            var brandId = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "QA Pharma Brand W7" });
            var catId = await db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "QA Controlled W7" });
            var supplierId = await db.InsertAsync("suppliers", new Dictionary<string, object?>
            {
                ["name"] = "QA Narcotics Distributor W7",
                ["phone"] = "03001234567",
                ["ntn"] = "1234567-8"
            });
            var customerId = await db.InsertAsync("customers", new Dictionary<string, object?>
            {
                ["name"] = "QA Patient Customer W7",
                ["phone"] = "03007654321",
                ["ntn"] = "8765432-1"
            });
            var productId = await db.InsertAsync("products", new Dictionary<string, object?>
            {
                ["product_code"] = "QA-NARC-W7",
                ["barcode"] = "QA-BAR-NARC-W7",
                ["name"] = "Alprazolam 0.5mg Tab W7",
                ["formulation"] = "Alprazolam",
                ["brand_id"] = brandId,
                ["category_id"] = catId,
                ["supplier_id"] = supplierId,
                ["pack_size"] = 10,
                ["quantity"] = 30,
                ["min_stock_level"] = 50,
                ["reorder_level"] = 50,
                ["pack_purchase_price"] = 100m,
                ["pack_sale_price"] = 150m,
                ["unit_purchase_price"] = 10m,
                ["unit_sale_price"] = 15m,
                ["avg_price"] = 10m,
                ["narcotic"] = "yes"
            });

            await db.InsertAsync("batches", new Dictionary<string, object?>
            {
                ["product_id"] = productId,
                ["batch_number"] = "BN-W7-NARC",
                ["expiry_date"] = nearExpDate,
                ["quantity"] = 30
            });

            var adminUserRow = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'qa-w78-admin@karobar.local'");
            var adminUserId = Convert.ToInt64(adminUserRow!["id"]);

            var piId = await db.InsertAsync("purchase_invoices", new Dictionary<string, object?>
            {
                ["supplier_id"] = supplierId,
                ["posted_number"] = "PI-W7-001",
                ["posted_date"] = today,
                ["invoice_number"] = "SUP-INV-W7-001",
                ["invoice_amount"] = 500m,
                ["total_amount"] = 590m,
                ["tax_amount"] = 90m,
                ["tax_percentage"] = 18m,
                ["total_paid"] = 200m,
                ["user_id"] = adminUserId
            });
            await db.InsertAsync("purchase_invoice_items", new Dictionary<string, object?>
            {
                ["purchase_invoice_id"] = piId,
                ["product_id"] = productId,
                ["batch"] = "BN-W7-NARC",
                ["expiry"] = nearExpDate,
                ["pack_size"] = 10,
                ["pack_quantity"] = 5,
                ["unit_quantity"] = 50,
                ["quantity"] = 50,
                ["pack_bonus"] = 0,
                ["unit_bonus"] = 0,
                ["pack_purchase_price"] = 100m,
                ["unit_purchase_price"] = 10m,
                ["pack_sale_price"] = 150m,
                ["unit_sale_price"] = 15m,
                ["margin"] = 50m,
                ["avg_price"] = 10m,
                ["sub_total"] = 500m
            });

            var siId = await db.InsertAsync("sale_invoices", new Dictionary<string, object?>
            {
                ["user_id"] = adminUserId,
                ["customer_id"] = customerId,
                ["posted_number"] = "SI-W7-001",
                ["date"] = today,
                ["doctor_name"] = "Dr. Tariq Mahmood",
                ["doctor_pmdc"] = "PMDC-12345-P",
                ["patient_name"] = "Ahmed Raza",
                ["patient_cnic"] = "35202-1234567-1",
                ["prescription_no"] = "RX-W7-009",
                ["gross_amount"] = 300m,
                ["total"] = 354m,
                ["tax_amount"] = 54m,
                ["tax_percentage"] = 18m,
                ["total_receive"] = 354m,
                ["sale_type"] = "retail",
                ["invoice_type"] = "debit"
            });
            await db.InsertAsync("sale_invoice_items", new Dictionary<string, object?>
            {
                ["sale_invoice_id"] = siId,
                ["product_id"] = productId,
                ["batch_number"] = "BN-W7-NARC",
                ["expiry"] = nearExpDate,
                ["pack_size"] = 10,
                ["quantity"] = 20,
                ["price"] = 15m,
                ["sub_total"] = 300m
            });

            var adjId = await db.InsertAsync("stock_adjustments", new Dictionary<string, object?>
            {
                ["posted_number"] = "SA-W7-001",
                ["posted_date"] = today,
                ["note"] = "QA Wave 7 adjustment",
                ["total_worth"] = 100m
            });
            await db.InsertAsync("stock_adjustment_items", new Dictionary<string, object?>
            {
                ["stock_adjustment_id"] = adjId,
                ["product_id"] = productId,
                ["batch_number"] = "BN-W7-NARC",
                ["expiry"] = nearExpDate,
                ["previous_qty"] = 30,
                ["actual_qty"] = 30,
                ["diff_qty"] = 0,
                ["unit_purchase_price"] = 10m,
                ["worth_adjusted"] = 0m
            });

            // 3. Verify Narcotics Statutory Register (Wave 7 C1)
            var narcResp = await client.GetAsync($"/api/reports/narcotics?from={fromDate}&to={toDate}");
            narcResp.StatusCode.Should().Be(HttpStatusCode.OK, await narcResp.Content.ReadAsStringAsync());
            using (var narcDoc = JsonDocument.Parse(await narcResp.Content.ReadAsStringAsync()))
            {
                var root = narcDoc.RootElement;
                root.GetProperty("chronological_register").GetArrayLength().Should().BeGreaterThan(0);
                root.GetProperty("product_registers").GetArrayLength().Should().BeGreaterThan(0);
                root.GetProperty("dispensing_register").GetArrayLength().Should().BeGreaterThan(0);
                root.GetProperty("procurement_register").GetArrayLength().Should().BeGreaterThan(0);
                root.GetProperty("stock_ledger").GetArrayLength().Should().BeGreaterThan(0);
                root.GetProperty("batch_inventory").GetArrayLength().Should().BeGreaterThan(0);
                GetFlexibleDecimal(root.GetProperty("summary").GetProperty("units_dispensed")).Should().BeGreaterThan(0m);
                GetFlexibleDecimal(root.GetProperty("summary").GetProperty("units_purchased")).Should().BeGreaterThan(0m);
            }

            // 4. Verify Near-Expiry extra fields (M3) & Purchase Summary item id/batch (M4)
            var nearExpResp = await client.GetAsync("/api/reports/near-expiry-product?months=6");
            nearExpResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var neDoc = JsonDocument.Parse(await nearExpResp.Content.ReadAsStringAsync()))
            {
                var rows = neDoc.RootElement.TryGetProperty("rows", out var r) ? r : neDoc.RootElement;
                rows.GetArrayLength().Should().BeGreaterThan(0);
                var first = rows[0];
                first.TryGetProperty("days_remaining", out _).Should().BeTrue();
                first.TryGetProperty("unit_purchase_price", out _).Should().BeTrue();
                first.TryGetProperty("purchase_value", out _).Should().BeTrue();
            }

            var psResp = await client.GetAsync($"/api/reports/purchase-summary?from={fromDate}&to={toDate}");
            psResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var psDoc = JsonDocument.Parse(await psResp.Content.ReadAsStringAsync()))
            {
                var psRows = psDoc.RootElement.TryGetProperty("rows", out var r) ? r : psDoc.RootElement;
                psRows.GetArrayLength().Should().BeGreaterThan(0);
                var firstItem = psRows[0].GetProperty("items")[0];
                firstItem.TryGetProperty("id", out _).Should().BeTrue();
                firstItem.TryGetProperty("batch", out _).Should().BeTrue();
            }

            // 5. Verify Product Comprehensive PDF validation (H4: missing product_id -> 400)
            var pcMissing = await client.GetAsync("/api/reports/product-comprehensive/pdf");
            pcMissing.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // 6. Verify strict date validation returns 422 on invalid dates
            (await client.GetAsync("/api/reports/purchase-returns?from=not-a-date")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await client.GetAsync("/api/reports/sale-returns?from=not-a-date")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await client.GetAsync("/api/reports/purchase-summary?from=not-a-date")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await client.GetAsync("/api/reports/net-profit?from=not-a-date")).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

            // 7. Hit ALL 17 JSON report endpoints + ALL 18 QuestPDF /pdf endpoints
            var jsonEndpoints = new[]
            {
                $"/api/reports/cost-of-sale?from={fromDate}&to={toDate}",
                $"/api/reports/cost-of-sale-detail?from={fromDate}&to={toDate}",
                $"/api/reports/purchase-detail?from={fromDate}&to={toDate}",
                $"/api/reports/sale-detail?from={fromDate}&to={toDate}",
                "/api/reports/current-stock",
                $"/api/reports/stock-adjustment?from={fromDate}&to={toDate}",
                $"/api/reports/product-comprehensive?product_id={productId}&from={fromDate}&to={toDate}",
                "/api/reports/near-expiry-product?months=6",
                $"/api/reports/purchase-returns?from={fromDate}&to={toDate}",
                $"/api/reports/sale-returns?from={fromDate}&to={toDate}",
                $"/api/reports/purchase-summary?from={fromDate}&to={toDate}",
                "/api/reports/outstanding-aging",
                "/api/reports/overdue-work-queue",
                $"/api/reports/reorder?supplier_id={supplierId}",
                $"/api/reports/net-profit?from={fromDate}&to={toDate}",
                $"/api/reports/tax-return?from={fromDate}&to={toDate}",
                $"/api/reports/narcotics?from={fromDate}&to={toDate}"
            };

            foreach (var url in jsonEndpoints)
            {
                var resp = await client.GetAsync(url);
                resp.StatusCode.Should().Be(HttpStatusCode.OK, $"JSON report {url} failed: {await resp.Content.ReadAsStringAsync()}");
            }

            var pdfEndpoints = new (string Label, string Url)[]
            {
                ("cost-of-sale", $"/api/reports/cost-of-sale/pdf?from={fromDate}&to={toDate}"),
                ("cost-of-sale-detail", $"/api/reports/cost-of-sale-detail/pdf?from={fromDate}&to={toDate}"),
                ("purchase-detail", $"/api/reports/purchase-detail/pdf?from={fromDate}&to={toDate}"),
                ("sale-detail", $"/api/reports/sale-detail/pdf?from={fromDate}&to={toDate}"),
                ("current-stock", "/api/reports/current-stock/pdf"),
                ("stock-adjustment", $"/api/reports/stock-adjustment/pdf?from={fromDate}&to={toDate}"),
                ("product-comprehensive", $"/api/reports/product-comprehensive/pdf?product_id={productId}&from={fromDate}&to={toDate}"),
                ("near-expiry-product", "/api/reports/near-expiry-product/pdf?months=6"),
                ("purchase-returns", $"/api/reports/purchase-returns/pdf?from={fromDate}&to={toDate}"),
                ("sale-returns", $"/api/reports/sale-returns/pdf?from={fromDate}&to={toDate}"),
                ("purchase-summary", $"/api/reports/purchase-summary/pdf?from={fromDate}&to={toDate}"),
                ("outstanding-aging", "/api/reports/outstanding-aging/pdf"),
                ("overdue-work-queue", "/api/reports/overdue-work-queue/pdf"),
                ("reorder", $"/api/reports/reorder/pdf?supplier_id={supplierId}"),
                ("net-profit", $"/api/reports/net-profit/pdf?from={fromDate}&to={toDate}"),
                ("tax-return", $"/api/reports/tax-return/pdf?from={fromDate}&to={toDate}"),
                ("narcotics", $"/api/reports/narcotics/pdf?from={fromDate}&to={toDate}"),
                ("financial-statements", $"/api/reports/financial-statements/pdf?from={fromDate}&to={toDate}")
            };

            foreach (var (label, url) in pdfEndpoints)
            {
                await AssertRealQuestPdfAsync(client, url, label);
            }

            // 8. Restricted user with ONLY report.tax-return.view & report.tax-return.export (Wave 7 C2 + H2)
            var taxToken = await LoginAsync(client, "qa-w78-tax@karobar.local", "TaxPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", taxToken);

            var taxResp = await client.GetAsync($"/api/reports/tax-return?from={fromDate}&to={toDate}");
            taxResp.StatusCode.Should().Be(HttpStatusCode.OK, await taxResp.Content.ReadAsStringAsync());
            using (var taxDoc = JsonDocument.Parse(await taxResp.Content.ReadAsStringAsync()))
            {
                var root = taxDoc.RootElement;
                root.TryGetProperty("customer_annexure", out _).Should().BeTrue();
                root.TryGetProperty("supplier_annexure", out _).Should().BeTrue();
                root.TryGetProperty("rate_breakdown", out _).Should().BeTrue();
                root.GetProperty("summary").TryGetProperty("tax_challans_paid", out _).Should().BeTrue();
            }
            await AssertRealQuestPdfAsync(client, $"/api/reports/tax-return/pdf?from={fromDate}&to={toDate}", "tax-return-restricted-user");

            // Confirm restricted tax user is still forbidden from narcotics report
            (await client.GetAsync($"/api/reports/narcotics?from={fromDate}&to={toDate}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await CleanupAsync(app, client, tempRoot, prevRoot);
        }
    }

    [Fact]
    public async Task Wave8_Settings_Cube_Fbr_Backups_Terminals_SqliteImport_And_Updates_PassLiveHttp()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            // 1. Auth guards vs [AllowAnonymous] routes
            (await client.GetAsync("/api/fbr/status-summary")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.PostAsync("/api/cube/ask", JsonBody(new { message = "hello" }))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.PostAsync("/api/settings/sqlite-import/commit", JsonBody(new { token = "abc" }))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            // Logged-out theme & check-update?force=1 must NOT return 401 or 422
            var themeResp = await client.GetAsync("/api/theme-settings/active");
            themeResp.StatusCode.Should().Be(HttpStatusCode.OK, await themeResp.Content.ReadAsStringAsync());

            var updateResp = await client.GetAsync("/api/system/check-update?force=1");
            updateResp.StatusCode.Should().NotBe(HttpStatusCode.UnprocessableEntity, "force=1 must bind cleanly without 422");

            // 2. Login as Admin
            var adminToken = await LoginAsync(client, "qa-w78-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            // 3. Settings: blank numeric safety, secret masking, clear_fbr_bearer_token, boolean serialization
            var updateSettingsResp = await client.PostAsync("/api/settings", JsonBody(new Dictionary<string, object?>
            {
                ["store_name"] = "QA Wave8 Store",
                ["fbr_bearer_token"] = "SUPER-SECRET-FBR-TOKEN-999",
                ["smtp_password"] = "SUPER-SECRET-SMTP-PASS",
                ["smtp_enabled"] = true,
                ["fbr_auto_sync"] = true,
                ["cube_ai_enabled"] = true,
                ["whatsapp_enabled"] = false,
                ["currency_decimal_places"] = "",
                ["fbr_pos_fee_amount"] = ""
            }));
            updateSettingsResp.StatusCode.Should().Be(HttpStatusCode.OK, await updateSettingsResp.Content.ReadAsStringAsync());

            // Must NOT throw 500 FormatException when numeric settings were sent blank
            (await client.GetAsync("/api/sale-system")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/api/currency")).StatusCode.Should().Be(HttpStatusCode.OK);

            var getSettingsResp = await client.GetAsync("/api/settings");
            getSettingsResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var settingsJson = await getSettingsResp.Content.ReadAsStringAsync();
            settingsJson.Should().NotContain("SUPER-SECRET-FBR-TOKEN-999", "plaintext fbr_bearer_token must be stripped");
            settingsJson.Should().NotContain("SUPER-SECRET-SMTP-PASS", "plaintext smtp_password must be stripped");

            using (var sDoc = JsonDocument.Parse(settingsJson))
            {
                var sRoot = sDoc.RootElement;
                sRoot.GetProperty("has_fbr_bearer_token").GetBoolean().Should().BeTrue();
                sRoot.GetProperty("smtp_enabled").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
                sRoot.GetProperty("fbr_auto_sync").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
                sRoot.GetProperty("cube_ai_enabled").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
                sRoot.GetProperty("whatsapp_enabled").ValueKind.Should().BeOneOf(JsonValueKind.True, JsonValueKind.False);
            }

            // Clear FBR bearer token via clear_fbr_bearer_token = "1"
            var clearTokenResp = await client.PostAsync("/api/settings", JsonBody(new Dictionary<string, object?>
            {
                ["clear_fbr_bearer_token"] = "1"
            }));
            clearTokenResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var sDoc2 = JsonDocument.Parse(await (await client.GetAsync("/api/settings")).Content.ReadAsStringAsync()))
            {
                sDoc2.RootElement.GetProperty("has_fbr_bearer_token").GetBoolean().Should().BeFalse();
            }

            // 4. Cube AI Tasks: preview_only dry-run + pagination + live commit + create_user_demands safety
            var w8BrandId = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "QA Brand W8" });
            var w8CatId = await db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "QA Category W8" });
            var panadolId = await db.InsertAsync("products", new Dictionary<string, object?>
            {
                ["product_code"] = "QA-PAN-W8",
                ["barcode"] = "QA-BAR-PAN-W8",
                ["name"] = "Panadol 500mg Tablet QA",
                ["formulation"] = "",
                ["brand_id"] = w8BrandId,
                ["category_id"] = w8CatId,
                ["pack_size"] = 10,
                ["quantity"] = 10,
                ["pack_purchase_price"] = 50m,
                ["pack_sale_price"] = 70m,
                ["unit_purchase_price"] = 5m,
                ["unit_sale_price"] = 7m
            });

            var previewResp = await client.PostAsync("/api/cube/execute-task", JsonBody(new
            {
                task_type = "bulk_autofill_missing_formulations",
                filter_query = "Panadol 500mg Tablet QA",
                preview_only = true,
                batch_size = 10,
                after_id = 0
            }));
            previewResp.StatusCode.Should().Be(HttpStatusCode.OK, await previewResp.Content.ReadAsStringAsync());
            using (var pDoc = JsonDocument.Parse(await previewResp.Content.ReadAsStringAsync()))
            {
                var meta = pDoc.RootElement.GetProperty("task_meta");
                meta.GetProperty("preview_only").GetBoolean().Should().BeTrue();
                meta.GetProperty("updated_products").GetArrayLength().Should().BeGreaterThan(0);
                var bg = meta.GetProperty("background_task");
                bg.GetProperty("last_processed_id").GetInt64().Should().BeGreaterThanOrEqualTo(panadolId);
                bg.TryGetProperty("remaining_count", out _).Should().BeTrue();
                meta.TryGetProperty("preview_result", out var pr).Should().BeTrue();
                pr.GetProperty("sample_matches").GetArrayLength().Should().BeGreaterThan(0);
            }
            // DB must remain untouched after preview_only = true
            var afterPreviewRow = await db.QuerySingleOrDefaultAsync("SELECT formulation FROM products WHERE id = @id", new { id = panadolId });
            (afterPreviewRow?["formulation"]?.ToString() ?? "").Should().BeEmpty("preview_only must not mutate the database");

            // Now execute for real (preview_only = false)
            var commitResp = await client.PostAsync("/api/cube/execute-task", JsonBody(new
            {
                task_type = "bulk_autofill_missing_formulations",
                filter_query = "Panadol 500mg Tablet QA",
                preview_only = false,
                user_confirmed = true,
                batch_size = 10,
                after_id = 0
            }));
            commitResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var afterCommitRow = await db.QuerySingleOrDefaultAsync("SELECT formulation FROM products WHERE id = @id", new { id = panadolId });
            (afterCommitRow?["formulation"]?.ToString() ?? "").Should().Contain("Paracetamol");

            // create_user_demands with unknown product must NOT crash with 500 NOT NULL
            var demandMissingResp = await client.PostAsync("/api/cube/execute-task", JsonBody(new
            {
                task_type = "create_user_demands",
                items = new[] { new { name = "NonExistentProductXYZ999", quantity = 5 } }
            }));
            demandMissingResp.StatusCode.Should().Be(HttpStatusCode.OK, await demandMissingResp.Content.ReadAsStringAsync());

            // create_user_demands with valid product succeeds
            var demandOkResp = await client.PostAsync("/api/cube/execute-task", JsonBody(new
            {
                task_type = "create_user_demands",
                items = new[] { new { name = "Panadol 500mg Tablet QA", quantity = 3 } }
            }));
            demandOkResp.StatusCode.Should().Be(HttpStatusCode.OK, await demandOkResp.Content.ReadAsStringAsync());

            // 5. Terminals: UTC last_seen_at -> is_online == true (fixes UTC+5 offline bug) + Quick Import Database
            await db.InsertAsync("terminals", new Dictionary<string, object?>
            {
                ["terminal_uid"] = "QA-TERM-W8",
                ["name"] = "QA Counter 1",
                ["hostname"] = "QA-PC-1",
                ["ip_address"] = "192.168.1.55",
                ["app_version"] = "2.9.0",
                ["mode"] = "terminal",
                ["is_authorized"] = 1,
                ["last_seen_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            });

            var terminalsResp = await client.GetAsync("/api/terminals");
            terminalsResp.StatusCode.Should().Be(HttpStatusCode.OK, await terminalsResp.Content.ReadAsStringAsync());
            using (var tDoc = JsonDocument.Parse(await terminalsResp.Content.ReadAsStringAsync()))
            {
                var list = tDoc.RootElement.GetProperty("terminals");
                bool foundOnline = false;
                foreach (var t in list.EnumerateArray())
                {
                    if (t.GetProperty("terminal_uid").GetString() == "QA-TERM-W8")
                    {
                        t.GetProperty("is_online").GetBoolean().Should().BeTrue("fresh UTC heartbeat must be online in any local timezone");
                        foundOnline = true;
                    }
                }
                foundOnline.Should().BeTrue();
            }

            // Build a tiny external SQLite DB with 1 new brand and upload via POST /api/server/quick-import-database
            var externalDbPath = Path.Combine(tempRoot, "quick_import_src.sqlite");
            await using (var extConn = new SqliteConnection($"Data Source={externalDbPath}"))
            {
                await extConn.OpenAsync();
                await using var cmd = extConn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE brands (id INTEGER PRIMARY KEY, name TEXT NOT NULL, created_at TEXT, updated_at TEXT);
                    INSERT INTO brands (id, name, created_at, updated_at) VALUES (999881, 'QA QuickImported Brand W8', datetime('now'), datetime('now'));";
                await cmd.ExecuteNonQueryAsync();
            }
            SqliteConnection.ClearAllPools();

            using (var mp = new MultipartFormDataContent())
            {
                var fileBytes = await File.ReadAllBytesAsync(externalDbPath);
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                mp.Add(fileContent, "file", "quick_import_src.sqlite");
                mp.Add(new StringContent("merge"), "mode");
                mp.Add(new StringContent("upsert"), "conflict_policy");

                var qimpResp = await client.PostAsync("/api/server/quick-import-database", mp);
                qimpResp.StatusCode.Should().Be(HttpStatusCode.OK, await qimpResp.Content.ReadAsStringAsync());
                using var qDoc = JsonDocument.Parse(await qimpResp.Content.ReadAsStringAsync());
                qDoc.RootElement.GetProperty("total_inserted").GetInt32().Should().BeGreaterThan(0);
            }
            var importedBrand = await db.QuerySingleOrDefaultAsync("SELECT id FROM brands WHERE name = 'QA QuickImported Brand W8'");
            importedBrand.Should().NotBeNull("quick-import-database must actually persist imported rows");

            // 6. Backups: SQL dump contains INSERT INTO, full backup restore verifies bcrypt password, run-now updates schedule
            var dbBackupResp = await client.PostAsync("/api/backups", JsonBody(new { type = "database" }));
            dbBackupResp.StatusCode.Should().Be(HttpStatusCode.Created, await dbBackupResp.Content.ReadAsStringAsync());
            long dbBackupId;
            using (var bDoc = JsonDocument.Parse(await dbBackupResp.Content.ReadAsStringAsync()))
            {
                dbBackupId = bDoc.RootElement.GetProperty("backup").GetProperty("id").GetInt64();
            }
            var dlResp = await client.GetAsync($"/api/backups/{dbBackupId}/download");
            dlResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var gzBytes = await dlResp.Content.ReadAsByteArrayAsync();
            using (var ms = new MemoryStream(gzBytes))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var sr = new StreamReader(gz, Encoding.UTF8))
            {
                var sqlText = await sr.ReadToEndAsync();
                sqlText.Should().Contain("INSERT INTO", "database SQL dump must include real data INSERT statements");
            }

            // Create a full (.zip) backup and test bcrypt password enforcement on Restore
            var fullBackupResp = await client.PostAsync("/api/backups", JsonBody(new { type = "full" }));
            fullBackupResp.StatusCode.Should().Be(HttpStatusCode.Created, await fullBackupResp.Content.ReadAsStringAsync());
            long fullBackupId;
            using (var fbDoc = JsonDocument.Parse(await fullBackupResp.Content.ReadAsStringAsync()))
            {
                fullBackupId = fbDoc.RootElement.GetProperty("backup").GetProperty("id").GetInt64();
            }

            var wrongPassRestore = await client.PostAsync($"/api/backups/{fullBackupId}/restore", JsonBody(new { password = "WrongPassword!" }));
            wrongPassRestore.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "restore must reject wrong bcrypt password");

            var goodPassRestore = await client.PostAsync($"/api/backups/{fullBackupId}/restore", JsonBody(new { password = "AdminPass123!" }));
            goodPassRestore.StatusCode.Should().Be(HttpStatusCode.OK, await goodPassRestore.Content.ReadAsStringAsync());

            // Real full restore replaces the live SQLite file (excluding personal_access_tokens for security), so re-login cleanly
            var postRestoreToken = await LoginAsync(client, "qa-w78-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", postRestoreToken);

            // Run scheduled backup now -> updates last_run_at & last_run_status
            var runNowResp = await client.PostAsync("/api/backups/run-now", JsonBody(new { }));
            runNowResp.StatusCode.Should().Be(HttpStatusCode.OK, await runNowResp.Content.ReadAsStringAsync());
            var schedResp = await client.GetAsync("/api/backups/schedule");
            schedResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var scDoc = JsonDocument.Parse(await schedResp.Content.ReadAsStringAsync()))
            {
                var sched = scDoc.RootElement.GetProperty("schedule");
                sched.GetProperty("last_run_status").GetString().Should().Be("success");
                sched.GetProperty("last_run_at").GetString().Should().NotBeNullOrWhiteSpace();
            }

            // 7. SQLite Import: locked without software_admin unlock -> 403; unlocked + mode=replace with wrong account password -> 422
            var lockedCommit = await client.PostAsync("/api/settings/sqlite-import/commit", JsonBody(new
            {
                token = "dummy-token-for-password-check",
                mode = "replace",
                password = "DefinitelyWrongPassword!"
            }));
            lockedCommit.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var replaceWrongPass = await client.PostAsync("/api/settings/sqlite-import/commit", JsonBody(new
            {
                token = "dummy-token-for-password-check",
                software_admin_password = "AdminPass123!",
                mode = "replace",
                password = "DefinitelyWrongPassword!"
            }));
            replaceWrongPass.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

            // 8. Restricted user cannot browse host directories or call FBR status-summary or Cube create_brands
            var taxToken = await LoginAsync(client, "qa-w78-tax@karobar.local", "TaxPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", taxToken);

            (await client.GetAsync("/api/backups/browse-directories")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await client.GetAsync("/api/fbr/status-summary")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var cubeForbiddenResp = await client.PostAsync("/api/cube/execute-task", JsonBody(new
            {
                task_type = "create_brands",
                items = new[] { new { name = "Unauthorized Brand" } }
            }));
            cubeForbiddenResp.StatusCode.Should().Be(HttpStatusCode.OK);
            (await cubeForbiddenResp.Content.ReadAsStringAsync()).Should().Contain("permission_denied");
        }
        finally
        {
            await CleanupAsync(app, client, tempRoot, prevRoot);
        }
    }
}

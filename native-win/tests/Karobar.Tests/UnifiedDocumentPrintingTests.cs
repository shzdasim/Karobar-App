using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karobar.Api;
using Karobar.Api.Middleware;
using Karobar.Data;
using Karobar.Host;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Karobar.Tests;

public class UnifiedDocumentPrintingTests
{
    private static async Task<(WebApplication App, HttpClient Client, KarobarDb Db, string TempRoot, string? PrevRoot)> StartLiveTestServerAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_print_{Guid.NewGuid():N}");
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
        await EnsureUsersAsync(kDb);

        var port = KestrelHostServer.FindFreePort(19300);
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
        app.UseRouting();
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

    private static async Task EnsureUsersAsync(KarobarDb db)
    {
        var adminHash = SanctumAuthenticationHandler.HashLaravelBcryptPassword("AdminPass123!");
        var existingAdmin = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'print-admin@karobar.local'");
        if (existingAdmin == null)
        {
            var adminId = await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "Print Admin",
                ["email"] = "print-admin@karobar.local",
                ["password"] = adminHash,
                ["status"] = "active"
            });
            var adminRole = await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE LOWER(name) = 'admin' LIMIT 1");
            var roleId = adminRole != null ? Convert.ToInt64(adminRole["id"]) : await db.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "admin", ["guard_name"] = "web" });
            await db.ExecuteAsync("INSERT OR IGNORE INTO model_has_roles (role_id, model_type, model_id) VALUES (@r, 'App\\\\Models\\\\User', @u)", new { r = roleId, u = adminId });
        }

        var limitedHash = SanctumAuthenticationHandler.HashLaravelBcryptPassword("LimitedPass123!");
        var existingLimited = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'print-limited@karobar.local'");
        if (existingLimited == null)
        {
            await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "Limited User",
                ["email"] = "print-limited@karobar.local",
                ["password"] = limitedHash,
                ["status"] = "active"
            });
        }
    }

    private static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var resp = await client.PostAsync(
            "/api/login",
            new StringContent(JsonSerializer.Serialize(new { email, password }), Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<(long SaleInvoiceId, long PurchaseInvoiceId, long SaleReturnId, long PurchaseReturnId, long PurchaseOrderId)> SeedDocumentsAsync(KarobarDb db)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var catId = await db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = $"PrintCat-{suffix}" });
        var brandId = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = $"PrintBrand-{suffix}" });
        var supplierId = await db.InsertAsync("suppliers", new Dictionary<string, object?>
        {
            ["name"] = $"Getz Distributor {suffix}",
            ["phone"] = "042-35880011",
            ["address"] = "Kot Lakhpat, Lahore"
        });
        var customerId = await db.InsertAsync("customers", new Dictionary<string, object?>
        {
            ["name"] = $"Customer Usman {suffix}",
            ["phone"] = "0300-7778899",
            ["address"] = "Model Town, Lahore"
        });
        var productId = await db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["name"] = $"Panadol PrintTest {suffix}",
            ["product_code"] = $"SKU-{suffix}",
            ["barcode"] = $"896{suffix}",
            ["pack_size"] = 10,
            ["quantity"] = 100,
            ["pack_purchase_price"] = 400,
            ["unit_purchase_price"] = 40,
            ["pack_sale_price"] = 500,
            ["unit_sale_price"] = 50,
            ["category_id"] = catId,
            ["brand_id"] = brandId
        });

        var poId = await db.InsertAsync("purchase_orders", new Dictionary<string, object?>
        {
            ["number"] = $"PO-{suffix}",
            ["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
            ["supplier_id"] = supplierId,
            ["status"] = "ordered",
            ["reference"] = $"REQ-{suffix}",
            ["notes"] = "Deliver fresh batch only",
            ["expected_received_date"] = DateTime.Now.AddDays(2).ToString("yyyy-MM-dd")
        });
        await db.InsertAsync("purchase_order_items", new Dictionary<string, object?>
        {
            ["purchase_order_id"] = poId,
            ["product_id"] = productId,
            ["ordered_quantity"] = 10,
            ["pack_size"] = 10,
            ["unit_purchase_price"] = 400,
            ["item_discount_percentage"] = 5,
            ["unit_bonus"] = 2
        });

        var piId = await db.InsertAsync("purchase_invoices", new Dictionary<string, object?>
        {
            ["supplier_id"] = supplierId,
            ["posted_number"] = $"PI-{suffix}",
            ["posted_date"] = DateTime.Now.ToString("yyyy-MM-dd"),
            ["invoice_number"] = $"SUP-BILL-{suffix}",
            ["invoice_amount"] = 4000,
            ["discount_percentage"] = 0,
            ["discount_amount"] = 0,
            ["tax_percentage"] = 0,
            ["tax_amount"] = 0,
            ["total_amount"] = 3800,
            ["total_paid"] = 2000,
            ["invoice_type"] = "credit",
            ["purchase_order_id"] = poId,
            ["status"] = "posted",
            ["payment_method"] = "bank_transfer"
        });
        await db.InsertAsync("purchase_invoice_items", new Dictionary<string, object?>
        {
            ["purchase_invoice_id"] = piId,
            ["product_id"] = productId,
            ["batch"] = $"B-{suffix}",
            ["expiry"] = "2028-05-01",
            ["pack_quantity"] = 10,
            ["pack_size"] = 10,
            ["unit_quantity"] = 0,
            ["pack_purchase_price"] = 400,
            ["unit_purchase_price"] = 40,
            ["pack_sale_price"] = 500,
            ["unit_sale_price"] = 50,
            ["pack_bonus"] = 1,
            ["unit_bonus"] = 0,
            ["item_discount_percentage"] = 5,
            ["sub_total"] = 3800,
            ["quantity"] = 110
        });

        var adminUserRow = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'print-admin@karobar.local' LIMIT 1");
        var adminUserId = Convert.ToInt64(adminUserRow!["id"]);

        var siId = await db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["customer_id"] = customerId,
            ["user_id"] = adminUserId,
            ["posted_number"] = $"INV-{suffix}",
            ["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
            ["sale_type"] = "retail",
            ["invoice_type"] = "debit",
            ["payment_method"] = "cash",
            ["gross_amount"] = 500,
            ["total"] = 500,
            ["total_receive"] = 500
        });
        await db.InsertAsync("sale_invoice_items", new Dictionary<string, object?>
        {
            ["sale_invoice_id"] = siId,
            ["product_id"] = productId,
            ["pack_size"] = 10,
            ["quantity"] = 10,
            ["price"] = 50,
            ["sub_total"] = 500
        });

        var srId = await db.InsertAsync("sale_returns", new Dictionary<string, object?>
        {
            ["customer_id"] = customerId,
            ["sale_invoice_id"] = siId,
            ["user_id"] = adminUserId,
            ["posted_number"] = $"SR-{suffix}",
            ["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
            ["gross_total"] = 100,
            ["total"] = 100,
            ["settlement_mode"] = "cash_refund",
            ["payment_method"] = "cash",
            ["credited_amount"] = 100
        });
        await db.InsertAsync("sale_return_items", new Dictionary<string, object?>
        {
            ["sale_return_id"] = srId,
            ["product_id"] = productId,
            ["batch_number"] = $"B-{suffix}",
            ["expiry"] = "2028-05-01",
            ["unit_sale_quantity"] = 10,
            ["unit_return_quantity"] = 2,
            ["unit_sale_price"] = 50,
            ["item_discount_percentage"] = 0,
            ["sub_total"] = 100
        });

        var prId = await db.InsertAsync("purchase_returns", new Dictionary<string, object?>
        {
            ["supplier_id"] = supplierId,
            ["purchase_invoice_id"] = piId,
            ["posted_number"] = $"PR-{suffix}",
            ["date"] = DateTime.Now.ToString("yyyy-MM-dd"),
            ["gross_total"] = 800,
            ["total"] = 760,
            ["settlement_mode"] = "ledger_credit",
            ["payment_method"] = "cash",
            ["credited_amount"] = 760
        });
        await db.InsertAsync("purchase_return_items", new Dictionary<string, object?>
        {
            ["purchase_return_id"] = prId,
            ["product_id"] = productId,
            ["batch"] = $"B-{suffix}",
            ["expiry"] = "2028-05-01",
            ["pack_size"] = 10,
            ["pack_purchased_quantity"] = 10,
            ["return_pack_quantity"] = 2,
            ["return_unit_quantity"] = 0,
            ["pack_purchase_price"] = 400,
            ["unit_purchase_price"] = 40,
            ["item_discount_percentage"] = 5,
            ["sub_total"] = 760
        });

        return (siId, piId, srId, prId, poId);
    }

    [Fact]
    public async Task All_Four_New_Print_Endpoints_Enforce_Security_And_Render_Document_Specific_Thermal_And_A4()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            var (siId, piId, srId, prId, poId) = await SeedDocumentsAsync(db);
            var adminToken = await LoginAsync(client, "print-admin@karobar.local", "AdminPass123!");
            var limitedToken = await LoginAsync(client, "print-limited@karobar.local", "LimitedPass123!");

            var cases = new[]
            {
                (ApiPath: $"/api/purchase-invoices/{piId}/print-url", WebPath: $"/print/purchase-invoices/{piId}", MissingApi: "/api/purchase-invoices/9999999/print-url", MissingWeb: $"/print/purchase-invoices/9999999", ExpectedTitle: "PURCHASE INVOICE", ExpectedMarker: "SUP-BILL-"),
                (ApiPath: $"/api/sale-returns/{srId}/print-url", WebPath: $"/print/sale-returns/{srId}", MissingApi: "/api/sale-returns/9999999/print-url", MissingWeb: $"/print/sale-returns/9999999", ExpectedTitle: "SALE RETURN / CREDIT NOTE", ExpectedMarker: "Cash Refund"),
                (ApiPath: $"/api/purchase-returns/{prId}/print-url", WebPath: $"/print/purchase-returns/{prId}", MissingApi: "/api/purchase-returns/9999999/print-url", MissingWeb: $"/print/purchase-returns/9999999", ExpectedTitle: "PURCHASE RETURN / DEBIT NOTE", ExpectedMarker: "Ledger Credit"),
                (ApiPath: $"/api/purchase-orders/{poId}/print-url", WebPath: $"/print/purchase-orders/{poId}", MissingApi: "/api/purchase-orders/9999999/print-url", MissingWeb: $"/print/purchase-orders/9999999", ExpectedTitle: "PURCHASE ORDER", ExpectedMarker: "EXPECTED ORDER TOTAL"),
            };

            foreach (var c in cases)
            {
                // 1. Unauthorized user -> 403
                using var unauthReq = new HttpRequestMessage(HttpMethod.Get, c.ApiPath);
                unauthReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", limitedToken);
                var unauthResp = await client.SendAsync(unauthReq);
                unauthResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"Limited user should get 403 for {c.ApiPath}");

                // 2. Missing document -> 404
                using var missReq = new HttpRequestMessage(HttpMethod.Get, c.MissingApi);
                missReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
                var missResp = await client.SendAsync(missReq);
                missResp.StatusCode.Should().Be(HttpStatusCode.NotFound, $"Missing document should return 404 for {c.MissingApi}");

                // 3. Unsigned / tampered / expired signature on /print/... -> 403
                var unsignedResp = await client.GetAsync(c.WebPath);
                unsignedResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"Unsigned {c.WebPath} must be rejected");
                var tamperedResp = await client.GetAsync($"{c.WebPath}?expires=9999999999&signature=deadbeef");
                tamperedResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"Tampered signature on {c.WebPath} must be rejected");
                var expiredResp = await client.GetAsync($"{c.WebPath}?expires=1000000000&signature=0000000000000000000000000000000000000000000000000000000000000000");
                expiredResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"Expired signature on {c.WebPath} must be rejected");

                // 4. Authorized user gets Thermal & A4 { url, html } and signed URL works
                foreach (var mode in new[] { "thermal", "a4" })
                {
                    using var okReq = new HttpRequestMessage(HttpMethod.Get, $"{c.ApiPath}?type={mode}");
                    okReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
                    var okResp = await client.SendAsync(okReq);
                    okResp.StatusCode.Should().Be(HttpStatusCode.OK, $"{c.ApiPath}?type={mode} should succeed");

                    using var doc = JsonDocument.Parse(await okResp.Content.ReadAsStringAsync());
                    var signedUrl = doc.RootElement.GetProperty("url").GetString();
                    var html = doc.RootElement.GetProperty("html").GetString();

                    signedUrl.Should().NotBeNullOrWhiteSpace();
                    signedUrl.Should().Contain(c.WebPath);
                    html.Should().Contain(c.ExpectedTitle);
                    html.Should().Contain(c.ExpectedMarker);
                    html.Should().Contain("karobar-print-config");
                    PrintTemplateRenderer.ExtractPrintMetaHints(html!).IsThermal.Should().Be(mode == "thermal");
                    if (c.WebPath.Contains("purchase-orders"))
                    {
                        html.Should().NotContain("Amount Paid");
                        html.Should().NotContain("You Saved");
                    }

                    // Follow signed URL without Bearer token (same as WebView2/print window)
                    var uri = new Uri(signedUrl!);
                    var signedGet = await client.GetAsync(uri.PathAndQuery);
                    signedGet.StatusCode.Should().Be(HttpStatusCode.OK);
                    var signedHtml = await signedGet.Content.ReadAsStringAsync();
                    signedHtml.Should().Contain(c.ExpectedTitle);
                    PrintTemplateRenderer.ExtractPrintMetaHints(signedHtml).IsThermal.Should().Be(mode == "thermal");
                }
            }

            // 5. Sale Invoice regression check via live HTTP
            using var siReq = new HttpRequestMessage(HttpMethod.Get, $"/api/sale-invoices/{siId}/print-url?type=thermal");
            siReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            var siResp = await client.SendAsync(siReq);
            siResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var siBody = await siResp.Content.ReadAsStringAsync();
            siBody.Should().Contain("SALE INVOICE");
            siBody.Should().Contain("karobar-print-config");
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
            client.Dispose();
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            KarobarDb.FlushSchemaCache();
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public void Preview_Renders_Genuinely_Distinct_Sample_Data_For_All_5_Document_Types()
    {
        var renderer = new PrintTemplateRenderer();
        var setting = new PrintSettingViewModel { StoreName = "Karobar Pharmacy" };

        var piThermal = renderer.RenderThermalPreview("standard", setting, null, "purchase_invoice");
        piThermal.Should().Contain("PURCHASE INVOICE");
        piThermal.Should().Contain("Getz Pharma Distributors");
        piThermal.Should().Contain("GP-99812");

        var srA4 = renderer.RenderA4Preview("detailed", setting, null, "sale_return");
        srA4.Should().Contain("SALE RETURN / CREDIT NOTE");
        srA4.Should().Contain("INV-00104");
        srA4.Should().Contain("Cash Refund");

        var prShare = renderer.RenderSharePreview("a4", "bold", setting, null, "purchase_return");
        prShare.Should().Contain("PURCHASE RETURN / DEBIT NOTE");
        prShare.Should().Contain("PI-2026-0041");
        prShare.Should().Contain("Ledger Credit");

        var poShare = renderer.RenderSharePreview("thermal", "standard", setting, null, "purchase_order");
        poShare.Should().Contain("PURCHASE ORDER");
        poShare.Should().Contain("Muller &amp; Phipps");
        poShare.Should().Contain("EXPECTED ORDER TOTAL");
        poShare.Should().NotContain("You Saved");
    }

    [Fact]
    public void Thermal_58mm_And_80mm_And_A4_Matrix_Render_All_5_Document_Types_And_Templates()
    {
        var renderer = new PrintTemplateRenderer();
        var docTypes = new[] { "sale_invoice", "purchase_invoice", "sale_return", "purchase_return", "purchase_order" };

        foreach (var docType in docTypes)
        {
            var vm = PrintTemplateRenderer.CreateSamplePreviewDocument(null, docType);
            vm.DiscountPercentage = 5;
            vm.DiscountAmount = 50;
            vm.TaxPercentage = 17;
            vm.TaxAmount = 170;
            vm.Remarks = "میڈیسن واپسی صرف بل کے ساتھ ہوگی • LongProductNameWithoutSpacesToVerifyWrapping1234567890";
            vm.Items.Add(new PrintInvoiceItemViewModel
            {
                ProductName = "Extra Long Pharmaceutical Product Name With 500mg/5ml Suspension Pack x24",
                ProductCode = "SKU-LONG-99",
                PackSize = 24,
                Quantity = 1250,
                BonusQuantity = docType is "purchase_invoice" or "purchase_order" ? 5 : 0,
                Price = 120.50m,
                ItemDiscountPercentage = 10,
                ItemDiscountAmount = 15062.50m,
                SubTotal = 135562.50m,
                BatchNumber = "LOT2026Z",
                Expiry = "12/2028"
            });

            // 58mm & 80mm thermal across all 6 unified invoice templates
            foreach (var widthMm in new[] { 58, 80 })
            {
                var setting = new PrintSettingViewModel
                {
                    StoreName = "Karobar Matrix Pharmacy",
                    PhoneNumber = "0300-1112233",
                    Address = "Lahore, Pakistan",
                    LogoUrl = "/storage/logos/sample.png",
                    PrintShowLogo = true,
                    PrintPaperWidthMm = widthMm,
                    PrintMarginTopMm = 2,
                    PrintMarginBottomMm = 5,
                    PrintMarginLeftMm = 1,
                    PrintMarginRightMm = 1,
                    PrintFontScale = 95,
                    PrintContrastMode = "high",
                    Note = "شرائط و ضوابط لاگو ہیں"
                };

                foreach (var tpl in PrintTemplateRenderer.ValidInvoiceTemplates)
                {
                    var html = renderer.RenderThermalSaleInvoice(tpl, vm, setting);
                    html.Should().Contain($"size: {widthMm}mm auto");
                    html.Should().Contain($"width: {widthMm}mm !important");
                    html.Should().Contain("table-layout:fixed");
                    html.Should().Contain("overflow:hidden");
                    html.Should().Contain("Extra Long Pharmaceutical Product Name");
                    html.Should().Contain("1,250");
                    html.Should().Contain("میڈیسن واپسی");
                    html.Should().Contain("sample.png");
                    PrintTemplateRenderer.ExtractPrintMetaHints(html).IsThermal.Should().BeTrue();
                    if (docType is "purchase_invoice" or "purchase_order")
                    {
                        html.Should().Contain("+5B");
                    }
                }
            }

            // A4 across all 6 templates including multi-item document (25 rows)
            var multiVm = PrintTemplateRenderer.CreateSamplePreviewDocument(null, docType);
            for (var i = 0; i < 25; i++)
            {
                multiVm.Items.Add(new PrintInvoiceItemViewModel
                {
                    ProductName = $"Multi-Page Item #{i + 1} Capsule 500mg",
                    PackSize = 10,
                    Quantity = 10 + i,
                    BonusQuantity = docType is "purchase_invoice" or "purchase_order" ? 1 : 0,
                    Price = 100m,
                    SubTotal = (10 + i) * 100m,
                    BatchNumber = $"B{i + 1:000}",
                    Expiry = "10/2028"
                });
            }

            var a4Setting = new PrintSettingViewModel
            {
                StoreName = "Karobar Matrix Pharmacy",
                LogoUrl = "/storage/logos/sample.png",
                PrintShowLogo = true,
                PrintMarginTopMm = 4,
                PrintMarginBottomMm = 4,
                PrintMarginLeftMm = 4,
                PrintMarginRightMm = 4,
                PrintFontScale = 100
            };

            foreach (var tpl in PrintTemplateRenderer.ValidInvoiceTemplates)
            {
                var a4Html = renderer.RenderA4SaleInvoice(tpl, multiVm, a4Setting);
                a4Html.Should().Contain("size:A4 portrait");
                a4Html.Should().Contain("Multi-Page Item #25");
                a4Html.Should().Contain("sample.png");
                PrintTemplateRenderer.ExtractPrintMetaHints(a4Html).IsThermal.Should().BeFalse();
                if (docType is "purchase_invoice" or "purchase_order")
                {
                    a4Html.Should().Contain("+1 Bns");
                }
            }
        }
    }
}

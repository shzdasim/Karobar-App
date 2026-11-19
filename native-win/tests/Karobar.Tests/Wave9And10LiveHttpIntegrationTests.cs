using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karobar.Api;
using Karobar.Api.Middleware;
using Karobar.Background;
using Karobar.Data;
using Karobar.Host;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Karobar.Tests;

public class Wave9And10LiveHttpIntegrationTests
{
    internal static async Task<(WebApplication App, HttpClient Client, KarobarDb Db, string TempRoot, string? PrevRoot)> StartLiveTestServerAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_wave910_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "public"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "backups"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "shared-pdfs"));
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
        Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", tempRoot);
        KarobarDb.FlushSchemaCache();

        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: tempDbPath)).EnsureSchemaReadyAsync();
        KarobarDb.FlushSchemaCache();

        var kDb = new KarobarDb(explicitSqlitePath: tempDbPath);
        await EnsureSeedAsync(kDb);

        var port = KestrelHostServer.FindFreePort(19850);
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddSingleton(kDb);
        builder.Services.AddSingleton(new LicenseService(kDb));
        builder.Services.AddSingleton(new SqliteExportService(kDb));
        builder.Services.AddSingleton(sp => new BackupService(kDb, sp.GetRequiredService<SqliteExportService>()));
        builder.Services.AddSingleton(new OperatorPinService(kDb));
        builder.Services.AddSingleton(new SyncPulseService(kDb));
        builder.Services.AddKarobarData(explicitSqlitePath: tempDbPath);
        builder.Services.AddKarobarPrinting(tempRoot);
        builder.Services.AddKarobarBackgroundServices(tempRoot);
        builder.Services.AddKarobarApi(builder.Configuration);
        builder.Services.AddControllers().AddApplicationPart(typeof(KarobarApiServiceExtensions).Assembly);

        var app = builder.Build();
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
        var adminRow = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'qa-w910-admin@karobar.local'");
        var adminId = adminRow != null
            ? Convert.ToInt64(adminRow["id"])
            : await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "QA Wave910 Admin",
                ["email"] = "qa-w910-admin@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("AdminPass123!"),
                ["status"] = "active"
            });

        if (await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = 'Admin'") == null)
            await db.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "Admin", ["guard_name"] = "web" });
        await db.SyncUserRolesAsync(adminId, new[] { "Admin" });

        if (await db.HasTableAsync("settings"))
        {
            var st = await db.QuerySingleOrDefaultAsync("SELECT id FROM settings ORDER BY id ASC LIMIT 1");
            if (st == null)
            {
                await db.InsertAsync("settings", new Dictionary<string, object?>
                {
                    ["store_name"] = "QA Karobar Pharmacy W9",
                    ["require_operator_pin"] = 0,
                    ["currency_symbol"] = "Rs.",
                    ["currency_code"] = "PKR",
                    ["currency_decimal_places"] = 2,
                    ["fbr_enabled"] = 0,
                    ["whatsapp_enabled"] = 0,
                    ["whatsapp_provider"] = "local_agent",
                    ["whatsapp_default_country_code"] = "92"
                });
            }
            else
            {
                await db.UpdateAsync("settings", st["id"]!, new Dictionary<string, object?>
                {
                    ["require_operator_pin"] = 0,
                    ["fbr_enabled"] = 0,
                    ["whatsapp_enabled"] = 0,
                    ["whatsapp_provider"] = "local_agent"
                });
            }
        }
    }

    internal static async Task<string> LoginAsync(HttpClient client, string email, string password)
    {
        var resp = await client.PostAsync("/api/login", JsonBody(new { email, password }));
        resp.StatusCode.Should().Be(HttpStatusCode.OK, await resp.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    internal static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    internal static async Task CleanupAsync(WebApplication app, HttpClient client, string tempRoot, string? prevRoot)
    {
        try { await app.StopAsync(); await app.DisposeAsync(); } catch { }
        try { client.Dispose(); } catch { }
        Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
        Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", prevRoot);
        KarobarDb.FlushSchemaCache();
        try { SqliteConnection.ClearAllPools(); } catch { }
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
    }

    [Fact]
    public async Task Wave9_Printing_SignedUrls_DocumentSharing_And_ExternalEngines_PassLiveHttp()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            // 1. Auth guards on CashDrawer & DocumentHistory
            (await client.GetAsync("/api/cash-drawer/current")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.GetAsync("/api/document-histories")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            // 2. All 4 Live Previews use real PrintTemplateRenderer (calibration meta, ZXing SVG QR, sticker @media print, theme colors)
            var thermalPrev = await client.GetAsync("/print/thermal-preview/standard?fbr_preview=1&paper_width_mm=80");
            thermalPrev.StatusCode.Should().Be(HttpStatusCode.OK);
            var thermalHtml = await thermalPrev.Content.ReadAsStringAsync();
            thermalHtml.Should().Contain("karobar-print-config");
            thermalHtml.Should().Contain("<svg");

            var a4Prev = await client.GetAsync("/print/a4-preview/detailed?fbr_preview=1");
            a4Prev.StatusCode.Should().Be(HttpStatusCode.OK);
            (await a4Prev.Content.ReadAsStringAsync()).Should().Contain("karobar-print-config");

            var stickerPrev = await client.GetAsync("/print/sticker-preview/urdu_focus?width_mm=50&height_mm=25&show_patient=1");
            stickerPrev.StatusCode.Should().Be(HttpStatusCode.OK);
            var stickerPrevHtml = await stickerPrev.Content.ReadAsStringAsync();
            stickerPrevHtml.Should().Contain("@media print{.no-print-bar{display:none!important;}");

            var sharePrev = await client.GetAsync("/print/share-preview/a4/detailed?show_logo=1");
            sharePrev.StatusCode.Should().Be(HttpStatusCode.OK);
            (await sharePrev.Content.ReadAsStringAsync()).Should().Contain("#1e3a8a");

            // 3. Seed invoice, quotation draft, purchase order, stocktake session for Wave 9 tests
            var adminRow = await db.QuerySingleOrDefaultAsync("SELECT id FROM users WHERE LOWER(email) = 'qa-w910-admin@karobar.local'");
            var adminId = Convert.ToInt64(adminRow!["id"]);
            var today = DateTime.Now.ToString("yyyy-MM-dd");

            var brandId = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "QA Brand W9" });
            var catId = await db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "QA Cat W9" });
            var supId = await db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "QA Supplier W9", ["phone"] = "03001112233" });
            var custId = await db.InsertAsync("customers", new Dictionary<string, object?> { ["name"] = "QA Patient W9", ["phone"] = "03009998877" });
            var prodId = await db.InsertAsync("products", new Dictionary<string, object?>
            {
                ["product_code"] = "QA-W9-001",
                ["barcode"] = "QA-BAR-W9-001",
                ["name"] = "Augmentin 625mg Tab",
                ["brand_id"] = brandId,
                ["category_id"] = catId,
                ["pack_size"] = 6,
                ["quantity"] = 60,
                ["pack_purchase_price"] = 180m,
                ["pack_sale_price"] = 240m,
                ["unit_purchase_price"] = 30m,
                ["unit_sale_price"] = 40m
            });

            var saleId = await db.InsertAsync("sale_invoices", new Dictionary<string, object?>
            {
                ["user_id"] = adminId,
                ["customer_id"] = custId,
                ["posted_number"] = "SI-W9-001",
                ["date"] = today,
                ["patient_name"] = "Zainab Bibi",
                ["doctor_name"] = "Dr. Kamran",
                ["gross_amount"] = 240m,
                ["total"] = 240m,
                ["total_receive"] = 240m,
                ["fbr_invoice_number"] = "966130261119123401",
                ["fbr_qr_data"] = "966130261119123401"
            });
            await db.InsertAsync("sale_invoice_items", new Dictionary<string, object?>
            {
                ["sale_invoice_id"] = saleId,
                ["product_id"] = prodId,
                ["pack_size"] = 6,
                ["quantity"] = 6,
                ["price"] = 40m,
                ["sub_total"] = 240m,
                ["dosage_instruction"] = "1 + 0 + 1 After Meal"
            });

            // 4. PHI protection & [Authorize] guards: unauthenticated dosage-labels & API routes must return 401 (not 403)
            var unauthDosage = await client.GetAsync($"/sale-invoices/{saleId}/dosage-labels");
            unauthDosage.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.GetAsync("/api/products")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await client.GetAsync("/api/customers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            // Login as Admin
            var token = await LoginAsync(client, "qa-w910-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // 5. Signed print-url & dosage-labels-url verification
            var printUrlResp = await client.GetAsync($"/api/sale-invoices/{saleId}/print-url?type=thermal");
            printUrlResp.StatusCode.Should().Be(HttpStatusCode.OK, await printUrlResp.Content.ReadAsStringAsync());
            string signedInvUrl;
            using (var puDoc = JsonDocument.Parse(await printUrlResp.Content.ReadAsStringAsync()))
            {
                signedInvUrl = puDoc.RootElement.GetProperty("url").GetString()!;
                signedInvUrl.Should().Contain("signature=").And.Contain("expires=");
                puDoc.RootElement.GetProperty("html").GetString()!.Should().Contain("karobar-print-config");
            }

            // Use a fresh unauthenticated client to prove signed URL works without Bearer token & rejects tampered signature
            using var anonClient = new HttpClient { BaseAddress = client.BaseAddress };
            (await anonClient.GetAsync(signedInvUrl)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await anonClient.GetAsync(signedInvUrl + "&download=0")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await anonClient.GetAsync($"/print/sale-invoices/{saleId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var dosageUrlResp = await client.GetAsync($"/api/sale-invoices/{saleId}/dosage-labels-url?width_mm=50&height_mm=25");
            dosageUrlResp.StatusCode.Should().Be(HttpStatusCode.OK, await dosageUrlResp.Content.ReadAsStringAsync());
            string signedDosageUrl;
            using (var duDoc = JsonDocument.Parse(await dosageUrlResp.Content.ReadAsStringAsync()))
            {
                signedDosageUrl = duDoc.RootElement.GetProperty("url").GetString()!;
                signedDosageUrl.Should().Contain("signature=");
                duDoc.RootElement.GetProperty("html").GetString()!.Should().Contain("@media print{.no-print-bar{display:none!important;}");
            }
            (await anonClient.GetAsync(signedDosageUrl)).StatusCode.Should().Be(HttpStatusCode.OK);

            // 6. DocumentShareController: real QuestPDF + signed public PDF + email + whatsapp + fixed DB columns
            var metaResp = await client.GetAsync($"/api/share/metadata?type=sale_invoice&id={saleId}&size=a4&template=bold");
            metaResp.StatusCode.Should().Be(HttpStatusCode.OK, await metaResp.Content.ReadAsStringAsync());
            string signedSharedPdfUrl;
            using (var mDoc = JsonDocument.Parse(await metaResp.Content.ReadAsStringAsync()))
            {
                var mRoot = mDoc.RootElement;
                signedSharedPdfUrl = mRoot.GetProperty("signed_pdf_url").GetString()!;
                signedSharedPdfUrl.Should().Contain("signature=").And.Contain("show_logo=");
                mRoot.GetProperty("whatsapp_provider").GetString().Should().Be("local_agent");
            }

            var apiPdfResp = await client.GetAsync($"/api/share/pdf?type=sale_invoice&id={saleId}&size=thermal&template=standard");
            apiPdfResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var apiPdfBytes = await apiPdfResp.Content.ReadAsByteArrayAsync();
            apiPdfBytes.Length.Should().BeGreaterThan(2000, "shared thermal PDF must be generated via real QuestPDF");
            Encoding.ASCII.GetString(apiPdfBytes, 0, 5).Should().Be("%PDF-");

            var pubPdfResp = await anonClient.GetAsync(signedSharedPdfUrl);
            pubPdfResp.StatusCode.Should().Be(HttpStatusCode.OK);
            (await pubPdfResp.Content.ReadAsByteArrayAsync()).Length.Should().BeGreaterThan(2000);
            (await anonClient.GetAsync($"/print/shared-pdf/sale_invoice?id={saleId}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            var emailResp = await client.PostAsync("/api/share/email", JsonBody(new
            {
                type = "sale_invoice",
                id = saleId,
                email = "patient-w9@example.com",
                subject = "Sale Invoice SI-W9-001",
                message = "Please find your pharmacy invoice attached.",
                save_email = true
            }));
            emailResp.StatusCode.Should().Be(HttpStatusCode.OK, await emailResp.Content.ReadAsStringAsync());
            using (var eDoc = JsonDocument.Parse(await emailResp.Content.ReadAsStringAsync()))
            {
                Convert.FromBase64String(eDoc.RootElement.GetProperty("pdf_base64").GetString()!).Length.Should().BeGreaterThan(2000);
            }

            var waResp = await client.PostAsync("/api/share/whatsapp", JsonBody(new
            {
                type = "sale_invoice",
                id = saleId,
                phone = "03009998877",
                message = "Here is your pharmacy receipt"
            }));
            waResp.StatusCode.Should().Be(HttpStatusCode.OK, await waResp.Content.ReadAsStringAsync());
            using (var wDoc = JsonDocument.Parse(await waResp.Content.ReadAsStringAsync()))
            {
                wDoc.RootElement.GetProperty("signed_pdf_url").GetString()!.Should().Contain("signature=");
                Convert.FromBase64String(wDoc.RootElement.GetProperty("pdf_base64").GetString()!).Length.Should().BeGreaterThan(2000);
            }

            // Verify fixed DB columns on purchase_orders (number), stocktake_sessions (reference_no), invoice_drafts (total_amount)
            var poId = await db.InsertAsync("purchase_orders", new Dictionary<string, object?>
            {
                ["supplier_id"] = supId,
                ["number"] = "PO-W9-777",
                ["status"] = "ordered",
                ["date"] = today
            });
            await db.InsertAsync("purchase_order_items", new Dictionary<string, object?>
            {
                ["purchase_order_id"] = poId,
                ["product_id"] = prodId,
                ["ordered_quantity"] = 10,
                ["pack_size"] = 6,
                ["unit_purchase_price"] = 30m,
                ["item_discount_percentage"] = 0m,
                ["unit_bonus"] = 0
            });
            var poMeta = await client.GetAsync($"/api/share/metadata?type=purchase_order&id={poId}");
            poMeta.StatusCode.Should().Be(HttpStatusCode.OK, await poMeta.Content.ReadAsStringAsync());
            (await poMeta.Content.ReadAsStringAsync()).Should().Contain("PO-W9-777").And.Contain("300.00");
            var poPdf = await client.GetAsync($"/api/share/pdf?type=purchase_order&id={poId}");
            poPdf.StatusCode.Should().Be(HttpStatusCode.OK);
            (await poPdf.Content.ReadAsByteArrayAsync()).Length.Should().BeGreaterThan(2000);

            // Verify additional real QuestPDF shared document branch (customer_ledger) & Role PUT permissions alias
            var custLedgerPdf = await client.GetAsync($"/api/share/pdf?type=customer_ledger&id={custId}");
            custLedgerPdf.StatusCode.Should().Be(HttpStatusCode.OK, await custLedgerPdf.Content.ReadAsStringAsync());
            var custLedgerBytes = await custLedgerPdf.Content.ReadAsByteArrayAsync();
            custLedgerBytes.Length.Should().BeGreaterThan(2000);
            Encoding.ASCII.GetString(custLedgerBytes, 0, 5).Should().Be("%PDF-");

            var rolesListResp = await client.GetAsync("/api/roles");
            rolesListResp.StatusCode.Should().Be(HttpStatusCode.OK, await rolesListResp.Content.ReadAsStringAsync());
            using (var rlDoc = JsonDocument.Parse(await rolesListResp.Content.ReadAsStringAsync()))
            {
                var rolesArr = rlDoc.RootElement.ValueKind == JsonValueKind.Array
                    ? rlDoc.RootElement
                    : (rlDoc.RootElement.TryGetProperty("data", out var rd) ? rd : default);
                if (rolesArr.ValueKind == JsonValueKind.Array && rolesArr.GetArrayLength() > 0 && rolesArr[0].TryGetProperty("id", out var rIdProp))
                {
                    var rolePut = await client.PutAsync($"/api/roles/{rIdProp.GetInt64()}/permissions", JsonBody(new { permissions = Array.Empty<string>() }));
                    rolePut.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Forbidden, HttpStatusCode.UnprocessableEntity);
                    rolePut.StatusCode.Should().NotBe(HttpStatusCode.NotFound);
                    rolePut.StatusCode.Should().NotBe(HttpStatusCode.MethodNotAllowed);
                }
            }

            // 7. SettingController dosage_sticker_config string persistence (C8) & real external test failures (C6)
            var saveStickerResp = await client.PostAsync("/api/settings", JsonBody(new Dictionary<string, object?>
            {
                ["dosage_sticker_config"] = new Dictionary<string, object?>
                {
                    ["width_mm"] = 60,
                    ["height_mm"] = 30,
                    ["template"] = "urdu_focus"
                }
            }));
            saveStickerResp.StatusCode.Should().Be(HttpStatusCode.OK, await saveStickerResp.Content.ReadAsStringAsync());

            var saleSysResp = await client.GetAsync("/api/sale-system");
            saleSysResp.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var ssDoc = JsonDocument.Parse(await saleSysResp.Content.ReadAsStringAsync()))
            {
                var dsc = ssDoc.RootElement.GetProperty("dosage_sticker_config");
                dsc.GetProperty("width_mm").GetInt32().Should().Be(60);
                dsc.GetProperty("template").GetString().Should().Be("urdu_focus");
            }

            // Test SMTP against dead local port must fail cleanly with 422 (NOT fake 200)
            var badSmtp = await client.PostAsync("/api/settings/test-smtp", JsonBody(new
            {
                smtp_host = "127.0.0.1",
                smtp_port = 1,
                smtp_encryption = "none",
                test_email = "test@example.com"
            }));
            badSmtp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

            // Unknown Cube task_type must return 422 (not fake Destructive Action Blocked)
            var badTask = await client.PostAsync("/api/cube/execute-task", JsonBody(new { task_type = "unknown_xyz_task" }));
            badTask.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }
        finally
        {
            await CleanupAsync(app, client, tempRoot, prevRoot);
        }
    }

    [Fact]
    public async Task Wave10_DesktopShell_Preload_Ota_Installer_And_BackgroundResilience_PassVerification()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            // 1. Preload script verification: no 2.5s duplicate-IPC timeout, exposes cash drawer & F5/Ctrl+Shift+T/D
            var preloadJs = KarobarPreloadScript.GetPreloadScript(isOffscreenPrintWorker: false);
            preloadJs.Should().NotContain("}, 2500);", "2.5s auto-HTTP fallback causes duplicate concurrent OTA/print/dialog calls");
            preloadJs.Should().NotContain("\"prev-tab\"", "legacy dead Electron Ctrl+Tab listener must be removed");
            preloadJs.Should().Contain("kickCashDrawer");
            preloadJs.Should().Contain("printThermalReceipt");
            preloadJs.Should().Contain("\"reload-active\"");
            preloadJs.Should().Contain("\"reopen-closed\"");
            preloadJs.Should().Contain("\"duplicate\"");

            // 2. Live HTTP SystemUpdateController: force=1 must bind without reflection ArgumentException; version exposes runtime_layout & deps_fingerprint; install without bridge -> 503
            var verResp = await client.GetAsync("/api/system/version");
            verResp.StatusCode.Should().Be(HttpStatusCode.OK);
            (await verResp.Content.ReadAsStringAsync()).Should().Contain("runtime_layout").And.Contain("deps_fingerprint");

            var checkUpd = await client.GetAsync("/api/system/check-update?force=1");
            checkUpd.StatusCode.Should().Be(HttpStatusCode.OK, await checkUpd.Content.ReadAsStringAsync());

            var token = await LoginAsync(client, "qa-w910-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var instNoBridge = await client.PostAsync("/api/system/install-update", JsonBody(new { mode = "download_and_install" }));
            instNoBridge.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

            // 3. Desktop Bridge unit-level checks: SilentPrint without host returns ok=false; WAL-safe Online Backup works; newest network-mode.json wins
            var fakeAppData = Path.Combine(tempRoot, "appdata");
            var netMgr = new NetworkConfigManager(tempRoot, userDataDirOverride: fakeAppData);
            netMgr.SaveNetworkConfig(new NetworkConfigUpdate
            {
                Mode = "terminal",
                ServerUrl = "http://192.168.1.50:18960",
                TerminalName = "Counter 2",
                PairingToken = "SECRET-PAIR-TOKEN"
            });
            var headers = netMgr.GetTerminalIdentityHeaders();
            headers["X-Network-Token"].Should().Be("SECRET-PAIR-TOKEN");

            // Simulate elevated installer writing standalone reset to {app}/storage/app/network-mode.json AFTER standard user's %APPDATA% file
            var appStorageCfg = Path.Combine(tempRoot, "storage", "app", "network-mode.json");
            await File.WriteAllTextAsync(appStorageCfg, "{\"mode\":\"standalone\",\"serverUrl\":\"\",\"terminalName\":\"\"}");
            File.SetLastWriteTimeUtc(appStorageCfg, DateTime.UtcNow.AddMinutes(5));
            var netMgrReloaded = new NetworkConfigManager(tempRoot, userDataDirOverride: fakeAppData);
            netMgrReloaded.LoadNetworkConfig().Mode.Should().Be("standalone", "newer {app}/storage/app/network-mode.json must override older %APPDATA% terminal config");

            var winMgr = new WebView2WindowManager(netMgr);
            var bridge = new KarobarDesktopBridge(netMgr, winMgr);

            using var emptyObj = JsonDocument.Parse("{}");
            var silentResJson = JsonSerializer.Serialize(await bridge.SilentPrintAsync(emptyObj.RootElement));
            silentResJson.Should().Contain("\"ok\":false", "headless SilentPrint must not report fake ok=true");

            // Online Backup creates a real, readable SQLite snapshot in storage/app/backups
            var bkpResJson = JsonSerializer.Serialize(await bridge.RunBackupNowAsync());
            bkpResJson.Should().Contain("\"ok\":true");
            var backupFiles = Directory.GetFiles(Path.Combine(tempRoot, "storage", "app", "backups"), "backup-*.sqlite");
            backupFiles.Should().NotBeEmpty();
            using (var verifyConn = new SqliteConnection($"Data Source={backupFiles[0]};Mode=ReadOnly;"))
            {
                await verifyConn.OpenAsync();
                using var cmd = verifyConn.CreateCommand();
                cmd.CommandText = "PRAGMA integrity_check;";
                (await cmd.ExecuteScalarAsync())?.ToString().Should().Be("ok");
            }

            // 4. FbrFiscalizationEngine.RetryPendingInvoicesAsync respects FbrEnabled == false
            using var scope = app.Services.CreateScope();
            var efDb = scope.ServiceProvider.GetRequiredService<KarobarDbContext>();
            var fbrEngine = scope.ServiceProvider.GetRequiredService<FbrFiscalizationEngine>();
            var retrySummary = await fbrEngine.RetryPendingInvoicesAsync(efDb);
            retrySummary["processed"].Should().Be(0, "disabled FBR must not process pending invoices");

            // 5. Verify Installer (.iss) & Build Script (.ps1) production guardrails
            var repoRoot = FindRepoRoot();
            var issContent = await File.ReadAllTextAsync(Path.Combine(repoRoot, "native-win", "installer", "Karobar-Native-Setup.iss"));
            issContent.Should().Contain("18960", "Installer Terminal Mode & Firewall rule must use native port 18960");
            issContent.Should().Contain("advfirewall firewall add rule", "Installer must open TCP 18960 for LAN POS Terminals");
            issContent.Should().Contain("runasoriginaluser", "Installer must relaunch Karobar-Native.exe de-elevated");
            issContent.Should().Contain("fresh_db", "Installer must emit fresh_db flag when Fresh Database is selected");
            issContent.Should().Contain("IsCliUpgradeSession", "Installer must cache CLI upgrade mode across ssPostInstall flag cleanup");
            issContent.Should().Contain("\"mode\":\"standalone\"", "Installer must reset stale terminal network-mode.json when reinstalling Standalone");

            var ps1Content = await File.ReadAllTextAsync(Path.Combine(repoRoot, "native-win", "build-native-win.ps1"));
            ps1Content.Should().Contain("installer_sha256", "Release manifest must publish full installer SHA-256");
            ps1Content.Should().Contain("installer_size", "Release manifest must publish installer_size for dynamic UI display");
            ps1Content.Should().Contain("deps_fingerprint", "Release manifest must track third-party DLL fingerprint");
        }
        finally
        {
            await CleanupAsync(app, client, tempRoot, prevRoot);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "native-win", "installer"))) return dir;
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}

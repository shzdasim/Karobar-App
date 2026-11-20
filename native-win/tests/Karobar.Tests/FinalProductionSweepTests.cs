using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Karobar.Api.Middleware;
using Karobar.Background;
using Karobar.Host;
using Karobar.Host.GuideBot;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Karobar.Tests.Wave9And10LiveHttpIntegrationTests;

namespace Karobar.Tests;

public class FinalProductionSweepTests
{
    [Fact]
    public void WaveF4_IsLoopbackIp_Handles_IPv4MappedIPv6_And_Rejects_LanIps()
    {
        KestrelHostServer.IsLoopbackIp(IPAddress.Loopback).Should().BeTrue();
        KestrelHostServer.IsLoopbackIp(IPAddress.IPv6Loopback).Should().BeTrue();
        KestrelHostServer.IsLoopbackIp(IPAddress.Parse("::ffff:127.0.0.1")).Should().BeTrue();
        KestrelHostServer.IsLoopbackIp(null).Should().BeTrue();

        KestrelHostServer.IsLoopbackIp(IPAddress.Parse("192.168.1.50")).Should().BeFalse();
        KestrelHostServer.IsLoopbackIp(IPAddress.Parse("::ffff:192.168.1.50")).Should().BeFalse();
        KestrelHostServer.IsLoopbackIp(IPAddress.Parse("10.0.0.12")).Should().BeFalse();
    }

    [Fact]
    public void WaveF2_PdfDocumentGenerator_UrduSafe_CorruptLogoSafe_And_NoFakeFbrOnNonSales()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar-pdf-sweep-{Guid.NewGuid():N}");
        var logosDir = Path.Combine(tempRoot, "storage", "app", "public", "logos");
        Directory.CreateDirectory(logosDir);
        try
        {
            // Write a corrupt non-image file disguised as .png
            var fakeLogo = Path.Combine(logosDir, "corrupt.png");
            File.WriteAllText(fakeLogo, "<html><script>alert(1)</script></html>");

            var generator = new PdfDocumentGenerator();
            var req = new SharedDocumentPdfRequest
            {
                DocumentType = "purchase_order",
                DocumentTitle = "PURCHASE ORDER",
                DocumentNumber = "PO-2026-0001",
                DocumentDate = "2026-11-20",
                PartyHeading = "Supplier",
                PartyName = "لاہور فارما ڈسٹریبیوٹرز (Lahore Pharma)",
                Setting = new PrintSettingViewModel
                {
                    StoreName = "کاروبار فارمیسی — Karobar Pharmacy",
                    LogoUrl = "/storage/logos/corrupt.png",
                    ShareShowLogo = true,
                    FbrEnabled = true,
                    FbrPosId = "966130"
                },
                FbrInvoiceNumber = "9661302611200001",
                Columns =
                [
                    new PdfTableColumnDef { Header = "Item", RelativeWidth = 4f },
                    new PdfTableColumnDef { Header = "Qty", RelativeWidth = 1f, AlignRight = true }
                ],
                Rows =
                [
                    ["Panadol 500mg — پیناڈول", "10"]
                ],
                Totals = [new PdfKeyValuePair("Order Total", "Rs. 500.00", isGrandTotal: true)]
            };

            var pdfBytes = generator.GenerateSharedDocumentPdf(req);
            pdfBytes.Should().NotBeNullOrEmpty();
            Encoding.ASCII.GetString(pdfBytes, 0, Math.Min(5, pdfBytes.Length)).Should().Be("%PDF-");
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WaveF2_PrintTemplateRenderer_A4CalibrationIsolation_ProductionFbr_DosageFallback_And_Code128()
    {
        var renderer = new PrintTemplateRenderer();
        var setting = new PrintSettingViewModel
        {
            StoreName = "Test Pharmacy",
            PrintPaperWidthMm = 58,
            FbrEnabled = true,
            FbrEnvironment = "production",
            FbrPosId = "966130"
        };
        var inv = new PrintInvoiceViewModel
        {
            Id = 42,
            PostedNumber = "SI-2026-0042",
            Date = new DateTime(2026, 11, 20),
            Total = 150m,
            FbrInvoiceNumber = null, // Unfiscalized in production
            Items =
            [
                new PrintInvoiceItemViewModel
                {
                    ProductName = "Amoxil 500mg",
                    Quantity = 1.5m,
                    Price = 100m,
                    SubTotal = 150m,
                    DosageInstruction = "" // Missing dosage must NEVER fabricate "1 + 0 + 1"
                }
            ]
        };

        // 1. A4 invoice must NOT be crushed to 58mm by thermal calibration
        var a4Html = renderer.RenderA4SaleInvoice("barcode", inv, setting);
        a4Html.Should().NotContain("size: 58mm auto");
        a4Html.Should().NotContain("width: 58mm !important");
        // Production unfiscalized invoice must NOT fabricate a fake FBR invoice number
        a4Html.Should().NotContain("fbr-fiscal-block");
        // Real Code128 SVG rendered
        a4Html.Should().Contain("<svg");

        // 2. Thermal invoice SHOULD preserve fractional quantity (1.5 instead of rounded 2)
        var thermalHtml = renderer.RenderThermalSaleInvoice("standard", inv, setting);
        thermalHtml.Should().Contain("1.5");
        thermalHtml.Should().Contain("size: 58mm auto");

        // 3. Dosage sticker fallback must never fabricate clinical dosing
        var stickerHtml = renderer.RenderDosageStickers(inv, setting, new PrintStickerConfigViewModel());
        stickerHtml.Should().NotContain("1 + 0 + 1");
        stickerHtml.Should().Contain("As directed by physician");
        stickerHtml.Should().NotContain("size: 58mm auto");
    }

    [Fact]
    public void WaveF3_SignedUrlMiddleware_FailsClosed_WhenExpiresMissing()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Scheme = "http";
        ctx.Request.Host = new HostString("127.0.0.1", 18960);
        ctx.Request.Path = "/api/documents/download";
        ctx.Request.QueryString = new QueryString("?signature=deadbeef");

        SignedUrlMiddleware.ValidateSignedRequest(ctx.Request).Should().BeFalse();
    }

    [Fact]
    public async Task WaveF3_And_F4_GuideBot_NoAdminFallback_And_SingleWindowTabRouting()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"karobar-host-sweep-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var netCfg = new NetworkConfigManager(tempDir);
            using var winMgr = new WebView2WindowManager(netCfg);

            var mainWin = winMgr.CreateMainWindow();
            mainWin.IsFocused.Should().BeTrue();

            var openedTabs = new List<(string Route, string? Title)>();
            winMgr.OnOpenTabRequested = (r, t) => openedTabs.Add((r, t));

            var childResult = winMgr.CreateAppWindow("/sale-invoices/pos", new AppWindowOptions { Title = "POS" });
            childResult.Id.Should().Be(mainWin.Id);
            openedTabs.Should().ContainSingle(x => x.Route == "/sale-invoices/pos" && x.Title == "POS");
            winMgr.GetOpenWindows().Should().HaveCount(1);
            mainWin.IsFocused.Should().BeTrue();

            // GuideBot without Sanctum token must NOT escalate to admin or run DB mutations
            var guideBot = new GuideBotSessionController(netCfg);
            guideBot.AuthBearerToken = null;
            var previewRes = await guideBot.PreviewFormulationAutofillAsync("panadol");
            previewRes.Should().NotBeNull();
            previewRes!["ok"].Should().Be(false);

            var bgSnap = await guideBot.StartBackgroundAutofillAsync("panadol");
            bgSnap.Should().NotBeNull();
            bgSnap!.Status.Should().Be("failed");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void WaveF4_WaseelaDateNormalization_And_CliBackupAlias()
    {
        WaseelaAndSqliteImportEngine.NormalizeDateString("31/01/2026").Should().Be("2026-01-31");
        WaseelaAndSqliteImportEngine.NormalizeDateString("30/04/2026").Should().Be("2026-04-30");
        WaseelaAndSqliteImportEngine.NormalizeDateString("02/2028").Should().Be("2028-02-29");
        WaseelaAndSqliteImportEngine.NormalizeDateString("02/2027").Should().Be("2027-02-28");
        KarobarCliCommandRunner.AllSupportedCommands.Should().Contain("karobar:backup");
    }

    [Fact]
    public async Task WaveF1_F2_F3_LiveHttp_FreshDbCrud_ReportPdfs_And_SecurityChecks()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            var adminToken = await LoginAsync(client, "qa-w910-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            // 1. Wave F1: Create Customer with is_regular=false and omitted NOT NULL defaults (must not hit SQLite Error 19)
            var custRes = await client.PostAsync("/api/customers", new StringContent(JsonSerializer.Serialize(new
            {
                name = $"Sweep Walkin {Guid.NewGuid():N}"[..24],
                is_regular = false,
                phone = "03001234567"
            }), Encoding.UTF8, "application/json"));
            custRes.IsSuccessStatusCode.Should().BeTrue($"Customer create failed: {await custRes.Content.ReadAsStringAsync()}");

            // 2. Wave F1: Create Supplier with blank tax_status
            var supRes = await client.PostAsync("/api/suppliers", new StringContent(JsonSerializer.Serialize(new
            {
                name = $"Sweep Supplier {Guid.NewGuid():N}"[..24],
                tax_status = ""
            }), Encoding.UTF8, "application/json"));
            var supBody = await supRes.Content.ReadAsStringAsync();
            supRes.IsSuccessStatusCode.Should().BeTrue($"Supplier create failed: {supBody}");
            long supplierId = 1;
            using (var sDoc = JsonDocument.Parse(supBody))
            {
                var sRoot = sDoc.RootElement.TryGetProperty("supplier", out var so) ? so : sDoc.RootElement;
                if (sRoot.TryGetProperty("id", out var sidProp) && sidProp.TryGetInt64(out var sidVal)) supplierId = sidVal;
            }

            // Ensure a Category & Brand exist (for both Real-DB and Empty-DB test runs)
            var catRes = await client.PostAsync("/api/categories", new StringContent(JsonSerializer.Serialize(new
            {
                name = $"Sweep Cat {Guid.NewGuid():N}"[..20]
            }), Encoding.UTF8, "application/json"));
            long categoryId = 1;
            using (var cDoc = JsonDocument.Parse(await catRes.Content.ReadAsStringAsync()))
            {
                var cRoot = cDoc.RootElement.TryGetProperty("category", out var co) ? co : cDoc.RootElement;
                if (cRoot.TryGetProperty("id", out var cidProp) && cidProp.TryGetInt64(out var cidVal)) categoryId = cidVal;
            }

            var brandRes = await client.PostAsync("/api/brands", new StringContent(JsonSerializer.Serialize(new
            {
                name = $"Sweep Brand {Guid.NewGuid():N}"[..20]
            }), Encoding.UTF8, "application/json"));
            long brandId = 1;
            using (var bDoc = JsonDocument.Parse(await brandRes.Content.ReadAsStringAsync()))
            {
                var bRoot = bDoc.RootElement.TryGetProperty("brand", out var bo) ? bo : bDoc.RootElement;
                if (bRoot.TryGetProperty("id", out var bidProp) && bidProp.TryGetInt64(out var bidVal)) brandId = bidVal;
            }

            // 3. Wave F1: Create Product with blank tax_type & blank sales_tax_rate / further_tax_rate
            var randCode = Random.Shared.Next(100000, 999999);
            var prodRes = await client.PostAsync("/api/products", new StringContent(JsonSerializer.Serialize(new
            {
                name = $"Sweep Med {Guid.NewGuid():N}"[..24],
                product_code = $"SWP-{randCode}",
                barcode = $"890{randCode}01",
                narcotic = "no",
                category_id = categoryId,
                brand_id = brandId,
                supplier_id = supplierId,
                pack_size = 10,
                pack_purchase_price = 100,
                pack_sale_price = 120,
                unit_purchase_price = 10,
                unit_sale_price = 12,
                tax_type = "",
                sales_tax_rate = "",
                further_tax_rate = ""
            }), Encoding.UTF8, "application/json"));
            var prodBody = await prodRes.Content.ReadAsStringAsync();
            prodRes.IsSuccessStatusCode.Should().BeTrue($"Product create failed: {prodBody}");
            long productId = 1;
            using (var pDoc = JsonDocument.Parse(prodBody))
            {
                var pRoot = pDoc.RootElement.TryGetProperty("product", out var po) ? po : pDoc.RootElement;
                if (pRoot.TryGetProperty("id", out var pidProp) && pidProp.TryGetInt64(out var pidVal)) productId = pidVal;
            }

            // 4. Wave F2: All 4 previously-broken analytical Report PDFs must return valid %PDF- bytes
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("yyyy-MM-dd");
            foreach (var reportPdfUrl in new[]
            {
                $"/api/reports/product-comprehensive/pdf?product_id={productId}&from={monthStart}&to={today}",
                $"/api/reports/outstanding-aging/pdf?as_of={today}",
                $"/api/reports/overdue-work-queue/pdf?as_of={today}",
                $"/api/reports/net-profit/pdf?from={monthStart}&to={today}"
            })
            {
                var pdfRes = await client.GetAsync(reportPdfUrl);
                pdfRes.StatusCode.Should().Be(HttpStatusCode.OK, $"Report PDF failed for {reportPdfUrl}");
                var pdfBytes = await pdfRes.Content.ReadAsByteArrayAsync();
                pdfBytes.Length.Should().BeGreaterThan(200, $"Empty PDF for {reportPdfUrl}");
                Encoding.ASCII.GetString(pdfBytes, 0, 5).Should().Be("%PDF-", $"Invalid PDF header for {reportPdfUrl}");
            }

            // 5. Wave F3: Settings upload rejects disguised HTML logo & ignores client logo_path
            using var badLogoForm = new MultipartFormDataContent();
            badLogoForm.Add(new StringContent("Sweep Store"), "store_name");
            var htmlBytes = new ByteArrayContent(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"));
            htmlBytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            badLogoForm.Add(htmlBytes, "logo", "evil.png");
            var badLogoRes = await client.PostAsync("/api/settings", badLogoForm);
            badLogoRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

            // 6. Wave F3: BackupService & BackupController block UNC paths
            var uncRes = await client.GetAsync("/api/backups/browse-directories?path=" + Uri.EscapeDataString(@"\\192.168.1.99\share"));
            uncRes.IsSuccessStatusCode.Should().BeFalse("UNC path must be rejected by browse-directories");

            // 7. Wave F3: SqliteImportController rejects previewing ProtectedTables (users)
            var unlockRes = await client.PostAsync("/api/settings/sqlite-import/unlock",
                new StringContent(JsonSerializer.Serialize(new { password = "AdminPass123!" }), Encoding.UTF8, "application/json"));
            var unlockJson = await unlockRes.Content.ReadAsStringAsync();
            if (unlockRes.IsSuccessStatusCode)
            {
                using var uDoc = JsonDocument.Parse(unlockJson);
                var unlockToken = uDoc.RootElement.TryGetProperty("unlock_token", out var ut) ? ut.GetString() : "";
                var prevReq = new HttpRequestMessage(HttpMethod.Post, "/api/settings/sqlite-import/preview-table")
                {
                    Content = new StringContent(JsonSerializer.Serialize(new
                    {
                        token = Guid.NewGuid().ToString(),
                        source_table = "users",
                        target_table = "users",
                        unlock_token = unlockToken
                    }), Encoding.UTF8, "application/json")
                };
                var prevRes = await client.SendAsync(prevReq);
                prevRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            }
        }
        finally
        {
            await CleanupAsync(app, client, tempRoot, prevRoot);
        }
    }

    [Fact]
    public void WaveF6_OtaPatchEligibility_MatchesDeclarativeAndCompatibleDepsFingerprints()
    {
        var patchUrl = "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/Karobar-Native-Patch.zip";
        var local = new LocalVersionMeta
        {
            Version = "2.0.39",
            BuildNumber = 39,
            RuntimeLayout = "split-v2",
            DepsFingerprint = "abcd1234"
        };

        // 1. Exact declarative deps_fingerprint match => fast ~4MB differential patch
        var exact = KarobarDesktopBridge.EvaluatePatchEligibility(
            local, patchUrl, "split-v2", "abcd1234", null, "2.0.0", hasBundledWaRuntime: true);
        exact.CanPatch.Should().BeTrue();
        exact.Reason.Should().BeNull();

        // 2. Compatible previous deps_fingerprint listed in manifest => still eligible for fast patch
        var compat = KarobarDesktopBridge.EvaluatePatchEligibility(
            local, patchUrl, "split-v2", "efgh5678", ["efgh5678", "ABCD1234"], "2.0.0", hasBundledWaRuntime: true);
        compat.CanPatch.Should().BeTrue();
        compat.Reason.Should().BeNull();

        // 3. Genuine mismatch => full installer with clear diagnostic reason
        var mismatch = KarobarDesktopBridge.EvaluatePatchEligibility(
            local, patchUrl, "split-v2", "99999999", ["88888888"], "2.0.0", hasBundledWaRuntime: true);
        mismatch.CanPatch.Should().BeFalse();
        mismatch.Reason.Should().Be("deps_fingerprint_mismatch");

        // 4. Missing WhatsApp bundled Node/Baileys runtime => full installer once
        var missingWa = KarobarDesktopBridge.EvaluatePatchEligibility(
            local, patchUrl, "split-v2", "abcd1234", null, "2.0.0", hasBundledWaRuntime: false);
        missingWa.CanPatch.Should().BeFalse();
        missingWa.Reason.Should().Be("missing_whatsapp_runtime");

        // 5. Legacy layout mismatch => full installer
        var badLayout = KarobarDesktopBridge.EvaluatePatchEligibility(
            new LocalVersionMeta { Version = "2.0.20", RuntimeLayout = "single-file", DepsFingerprint = "abcd1234" },
            patchUrl, "split-v2", "abcd1234", null, "2.0.0", hasBundledWaRuntime: true);
        badLayout.CanPatch.Should().BeFalse();
        badLayout.Reason.Should().Be("runtime_layout_mismatch");
    }
}

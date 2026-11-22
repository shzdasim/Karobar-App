using System.Text;
using FluentAssertions;
using Karobar.Background;
using Karobar.Core.Entities;
using Karobar.Data;
using Karobar.Printing;
using Xunit;

namespace Karobar.Tests;

public class Phase11And12PrintingAndBackgroundTests
{
    private static readonly string[] Expected6InvoiceTemplates =
    [
        "standard", "minimal", "detailed", "compact", "bold", "barcode"
    ];

    private static readonly string[] Expected4StickerTemplates =
    [
        "classic", "minimal", "bold", "urdu_focus"
    ];

    private static readonly string[] Expected15SharedPdfTypes =
    [
        "sale_invoice", "sale_return", "purchase_invoice", "purchase_return",
        "purchase_order", "customer_ledger", "supplier_ledger", "payment_voucher",
        "cash_drawer_z_report", "delivery_challan", "quotation", "regular_refill_reminder",
        "stock_disposal", "stocktake_session", "payment"
    ];

    private static readonly string[] Expected18ReportPdfTypes =
    [
        "cost-of-sale", "cost-of-sale-detail", "purchase-detail", "sale-detail",
        "current-stock", "stock-adjustment", "product-comprehensive", "near-expiry-product",
        "purchase-returns", "sale-returns", "purchase-summary", "outstanding-aging",
        "overdue-work-queue", "reorder", "net-profit", "tax-return",
        "narcotics", "financial-statements"
    ];

    private static readonly string[] Expected11CliCommands =
    [
        "flush-walk-in-credit", "apply-patch", "export-sqlite", "import-sqlite",
        "install", "license:issue", "license:machine-id", "license:verify-installer",
        "expenses:process-recurring", "backup:run-scheduled", "demands:notify"
    ];

    private static readonly string[] Expected4ScheduledCronJobs =
    [
        "demands:notify", "backup:run-scheduled", "cleanupExpiredSharedPdfs", "expenses:process-recurring"
    ];

    [Fact]
    public void Win32RawPrinterService_Has_CashDrawerKick_And_EscPos_Commands()
    {
        Win32RawPrinterService.CashDrawerKickCommand.Should().Equal([0x1B, 0x70, 0x00, 0x19, 0xFA]);
        var service = new Win32RawPrinterService();
        var payload = service.BuildEscPosReceiptPayload("KAROBAR TEST RECEIPT", kickDrawer: true, cutPaper: true);
        payload.Should().NotBeEmpty();
        Encoding.UTF8.GetString(payload).Should().Contain("KAROBAR TEST RECEIPT");
    }

    [Fact]
    public void PrintTemplateRenderer_Renders_All_22_Printer_Views_And_4_Live_Previews()
    {
        var renderer = new PrintTemplateRenderer();
        var setting = new PrintSettingViewModel
        {
            StoreName = "Karobar QA Pharmacy",
            PhoneNumber = "0300-1234567",
            Address = "Model Town, Lahore",
            FbrEnabled = true,
            FbrPosId = "966130"
        };
        var invoice = new PrintInvoiceViewModel
        {
            Id = 42,
            PostedNumber = "SI-00042",
            CustomerName = "Ali Khan",
            DoctorName = "Dr. Tariq",
            PatientName = "Ali Khan",
            Total = 450m,
            TotalReceive = 500m,
            Items =
            [
                new PrintInvoiceItemViewModel
                {
                    ProductName = "Tab. Panadol 500mg",
                    Quantity = 2,
                    Price = 225m,
                    SubTotal = 450m,
                    BatchNumber = "B101",
                    Expiry = "12/2027",
                    DosageInstruction = "1 + 0 + 1 • کھانے کے بعد"
                }
            ]
        };

        // 1. Base thermal + 6 named thermal templates (7 views)
        var baseThermal = renderer.RenderThermalSaleInvoice("base", invoice, setting);
        baseThermal.Should().Contain("SI-00042").And.Contain("Tab. Panadol 500mg").And.Contain("fbr-fiscal-block");

        foreach (var tpl in Expected6InvoiceTemplates)
        {
            var html = renderer.RenderThermalSaleInvoice(tpl, invoice, setting);
            html.Should().Contain("SI-00042", $"Thermal template '{tpl}' must render invoice number");
            html.Should().Contain("Tab. Panadol 500mg", $"Thermal template '{tpl}' must render item");
        }

        // 2. 6 A4 templates (6 views)
        foreach (var tpl in Expected6InvoiceTemplates)
        {
            var html = renderer.RenderA4SaleInvoice(tpl, invoice, setting);
            html.Should().Contain("SI-00042", $"A4 template '{tpl}' must render invoice number");
            html.Should().Contain("Tab. Panadol 500mg", $"A4 template '{tpl}' must render item");
        }

        // 3. Patient Urdu/English Dosage Sticker template across 4 styles (1 view, 4 presets)
        foreach (var tpl in Expected4StickerTemplates)
        {
            var stickerCfg = new PrintStickerConfigViewModel { Template = tpl };
            var html = renderer.RenderDosageStickers(invoice, setting, stickerCfg);
            html.Should().Contain("Tab. Panadol 500mg").And.Contain("کھانے کے بعد");
        }

        // 4. Cash Drawer Thermal Z-Report (1 view)
        var zReportHtml = renderer.RenderCashDrawerThermalZReport(
            new PrintCashDrawerZReportViewModel { SessionId = 7, CashierName = "Cashier 1", OpeningBalance = 5000m, ExpectedBalance = 12500m },
            setting);
        zReportHtml.Should().Contain("Cashier 1").And.Contain("12,500.00");

        // 5. Customer & Supplier Ledgers (A4 & Thermal -> 4 views)
        var ledgerRows = new List<PrintLedgerRowViewModel>
        {
            new() { EntryDate = "2026-11-18", EntryType = "invoice", PostedNumber = "SI-00042", InvoiceTotal = 450m, RunningBalance = 450m }
        };
        var ledgerSummary = new PrintLedgerSummaryViewModel { TotalInvoiced = 450m, NetBalance = 450m };

        renderer.RenderPartyLedger("customer", "a4", "Ali Khan", ledgerRows, ledgerSummary, setting)
            .Should().Contain("Ali Khan").And.Contain("SI-00042");
        renderer.RenderPartyLedger("customer", "thermal", "Ali Khan", ledgerRows, ledgerSummary, setting)
            .Should().Contain("Ali Khan").And.Contain("SI-00042");
        renderer.RenderPartyLedger("supplier", "a4", "Getz Pharma", ledgerRows, ledgerSummary, setting)
            .Should().Contain("Getz Pharma").And.Contain("SI-00042");
        renderer.RenderPartyLedger("supplier", "thermal", "Getz Pharma", ledgerRows, ledgerSummary, setting)
            .Should().Contain("Getz Pharma").And.Contain("SI-00042");

        // 6. All 4 preview endpoints
        renderer.RenderThermalPreview("standard", setting).Should().Contain("INV-001");
        renderer.RenderA4Preview("standard", setting).Should().Contain("INV-001");
        renderer.RenderStickerPreview("urdu_focus", setting).Should().Contain("SI-00042");
        renderer.RenderSharePreview("a4", "standard", setting).Should().Contain("INV-00104");
    }

    [Fact]
    public void PdfDocumentGenerator_Produces_Valid_Pdf_Bytes_For_All_15_Shared_Docs_And_18_Analytical_Reports()
    {
        var generator = new PdfDocumentGenerator();

        foreach (var docType in Expected15SharedPdfTypes)
        {
            PdfDocumentGenerator.SupportedSharedDocuments.Should().ContainKey(docType);
            var pdfBytes = generator.GenerateSharedDocumentPdf(new SharedDocumentPdfRequest
            {
                DocumentType = docType,
                DocumentNumber = "DOC-101",
                PartyName = "QA Test Party",
                Columns =
                [
                    new PdfTableColumnDef { Header = "Item", RelativeWidth = 3f },
                    new PdfTableColumnDef { Header = "Total", RelativeWidth = 1f, AlignRight = true }
                ],
                Rows = [["Paracetamol 500mg", "200.00"]],
                Totals = [new PdfKeyValuePair("Grand Total", "Rs. 200.00", isGrandTotal: true)]
            });

            pdfBytes.Length.Should().BeGreaterThan(250);
            Encoding.ASCII.GetString(pdfBytes, 0, 5).Should().Be("%PDF-");
        }

        foreach (var reportType in Expected18ReportPdfTypes)
        {
            PdfDocumentGenerator.SupportedAnalyticalReports.Should().ContainKey(reportType);
            var pdfBytes = generator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
            {
                ReportType = reportType,
                FromDate = "2026-11-01",
                ToDate = "2026-11-18",
                SummaryCards = [new PdfKeyValuePair("Total Records", "12")],
                Rows = [["1", "Sample Row", "REF-01", "10", "1,500.00"]],
                FooterTotals = [new PdfKeyValuePair("Net Total", "Rs. 1,500.00", isGrandTotal: true)]
            });

            pdfBytes.Length.Should().BeGreaterThan(250);
            Encoding.ASCII.GetString(pdfBytes, 0, 5).Should().Be("%PDF-");
        }
    }

    [Fact]
    public async Task Phase12_Fbr_CubeAi_WhatsApp_Waseela_Cron_And_11_CliCommands_Are_Functional()
    {
        // 1. Verify all 11 CLI commands & 4 scheduled cron jobs are registered
        foreach (var cmd in Expected11CliCommands)
        {
            KarobarCliCommandRunner.AllSupportedCommands.Should().Contain(cmd);
        }

        KarobarSchedulerHostedService.ScheduledJobNames.Should().BeEquivalentTo(Expected4ScheduledCronJobs);

        // 2. Verify all 7 Cube AI classes exist and function
        var promptBuilder = new CubePromptBuilder();
        promptBuilder.SanitizeUserText("Key gsk_12345678901234567890 and CNIC 35202-1234567-1")
            .Should().Contain("[REDACTED_API_KEY]").And.Contain("[REDACTED_CNIC]");

        var retriever = new CubeKnowledgeRetriever();
        var kb = retriever.Retrieve("How to use POS loose tablet billing?", "/sale-invoices/pos");
        kb["has_verified_docs"].Should().Be(true);

        var quotaGuard = new CubeQuotaGuard();
        var groqProvider = new GroqProvider();
        var geminiProvider = new GeminiProvider();
        var taskExecutor = new CubeTaskExecutorService();
        var assistant = new CubeAssistantService(retriever, promptBuilder, quotaGuard, groqProvider, geminiProvider);

        groqProvider.IsChatCapableModelId("llama-3.3-70b-versatile").Should().BeTrue();
        geminiProvider.IsFreeTierGeminiModelId("gemini-2.5-flash").Should().BeTrue();
        taskExecutor.Should().NotBeNull();
        assistant.Should().NotBeNull();

        // 3. Verify Abuzar Waseela Schema Auto-Detector
        var waseelaImporter = new WaseelaAndSqliteImportEngine();
        var detection = waseelaImporter.DetectWaseelaSchema(new Dictionary<string, List<string>>
        {
            ["tblItems"] = ["ItemCode", "ItemName", "PackSize", "TPPack", "RetailPrice"],
            ["tblBatch"] = ["BatchNo", "ExpDate", "ClosingStock"],
            ["tblKhata"] = ["PartyName", "OpeningBalance", "KhataDetail"]
        });
        detection.IsWaseelaSchema.Should().BeTrue();
        detection.DetectedProfile.Should().Be("abuzar_waseela");
        detection.WaseelaTableMappings["tblItems"].Should().Be("products");
        detection.WaseelaTableMappings["tblBatch"].Should().Be("batches");
        detection.WaseelaTableMappings["tblKhata"].Should().Be("customer_ledgers");

        // 4. Verify CLI license:machine-id, license:issue, license:verify-installer (bound + unbound + wrong machine) & full installer bootstrap
        var cliRunner = new KarobarCliCommandRunner();
        var machineRes = cliRunner.ExecuteLicenseMachineId(new Dictionary<string, string?>());
        machineRes.Ok.Should().BeTrue();
        var machineId = machineRes.Output.Trim();
        machineId.Should().MatchRegex("^KRB-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}$");

        // Verify CommandLineRouter headless `--cli license:machine-id --out=...` works end-to-end (exact Inno Setup call)
        var prevRootPath = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        var prevAppRoot = Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");
        var tempCliDir = Path.Combine(Path.GetTempPath(), $"karobar_qa_cli_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempCliDir);
        try
        {
            var midOutPath = Path.Combine(tempCliDir, "machine-id.txt");
            var exitCode = await Karobar.Host.CommandLineRouter.ExecuteHeadlessCliAsync(
                ["license:machine-id", $"--out={midOutPath}"],
                appRoot: tempCliDir);
            exitCode.Should().Be(0);
            File.Exists(midOutPath).Should().BeTrue();
            File.ReadAllText(midOutPath).Trim().Should().Be(machineId);

            // Generate a test Ed25519 keypair to verify real cryptographic Ed25519 signing & verification
            var kpRes = cliRunner.ExecuteLicenseIssue(new Dictionary<string, string?> { ["generate-keypair"] = "true" });
            kpRes.Ok.Should().BeTrue();
            var testPubBytes = Karobar.Core.HardwareMachineIdProvider.B64uDecode(kpRes.Data["public_key"]!.ToString()!);
            var testSecretB64 = kpRes.Data["secret_key"]!.ToString()!;

            // 4a. Machine-bound key for THIS machine -> MUST PASS
            var boundIssueRes = cliRunner.ExecuteLicenseIssue(new Dictionary<string, string?>
            {
                ["machine"] = machineId,
                ["tier"] = "platinum",
                ["days"] = "365",
                ["secret-key"] = testSecretB64,
                ["name"] = "QA Pharmacy"
            });
            boundIssueRes.Ok.Should().BeTrue();
            var boundKey = boundIssueRes.Data["license_key"]!.ToString()!;
            boundKey.Should().StartWith("KRBL.");

            var sealOutPath = Path.Combine(tempCliDir, "install-license.seal");
            var verifyBoundRes = cliRunner.ExecuteLicenseVerifyInstaller(new Dictionary<string, string?>
            {
                ["key"] = boundKey,
                ["seal-out"] = sealOutPath
            }, testPubBytes);
            verifyBoundRes.Ok.Should().BeTrue();
            File.Exists(sealOutPath).Should().BeTrue();

            // 4b. Unbound vendor-signed key with --allow-unbound -> MUST PASS
            var unboundIssueRes = cliRunner.ExecuteLicenseIssue(new Dictionary<string, string?>
            {
                ["allow-unbound"] = "true",
                ["tier"] = "gold",
                ["days"] = "365",
                ["secret-key"] = testSecretB64
            });
            unboundIssueRes.Ok.Should().BeTrue();
            var unboundKey = unboundIssueRes.Data["license_key"]!.ToString()!;
            var verifyUnboundRes = cliRunner.ExecuteLicenseVerifyInstaller(new Dictionary<string, string?>
            {
                ["key"] = unboundKey,
                ["allow-unbound"] = "true"
            }, testPubBytes);
            verifyUnboundRes.Ok.Should().BeTrue();

            // 4c. Key bound to ANOTHER machine -> MUST FAIL even with --allow-unbound
            var wrongMachineIssueRes = cliRunner.ExecuteLicenseIssue(new Dictionary<string, string?>
            {
                ["machine"] = "KRB-DEAD-BEEF-CAFE-BABE",
                ["tier"] = "platinum",
                ["days"] = "365",
                ["secret-key"] = testSecretB64
            });
            var verifyWrongRes = cliRunner.ExecuteLicenseVerifyInstaller(new Dictionary<string, string?>
            {
                ["key"] = wrongMachineIssueRes.Data["license_key"]!.ToString()!,
                ["allow-unbound"] = "true"
            }, testPubBytes);
            verifyWrongRes.Ok.Should().BeFalse();

            // 4d. Full post-install bootstrap (`install --seal-file=... --admin-config=...`)
            var adminCfgPath = Path.Combine(tempCliDir, "admin-init.json");
            File.WriteAllText(adminCfgPath, """{"name":"Test Owner","email":"owner@karobar.local","password":"password123","upgrade_only":false}""");
            var installDbPath = Path.Combine(tempCliDir, "database", "database.sqlite");
            var initFactory = new DbConnectionFactory(explicitSqlitePath: installDbPath);
            await new DatabaseBootstrapper(initFactory).EnsureSchemaReadyAsync();
            await using var installDb = DatabaseBootstrapper.CreateDbContext(installDbPath);
            var scopedRunner = new KarobarCliCommandRunner(tempCliDir);
            var installRes = await scopedRunner.ExecuteInstallAsync(
                installDb,
                new Dictionary<string, string?>
                {
                    ["seal-file"] = sealOutPath,
                    ["admin-config"] = adminCfgPath,
                    ["allow-unbound"] = "true",
                    ["accept-legal"] = "true",
                    ["cleanup-temp"] = "true"
                },
                testPublicKeyOverride: testPubBytes);
            installRes.Ok.Should().BeTrue();
            installRes.Data["licensed"].Should().Be(true);
            installRes.Data["admin_email"].Should().Be("owner@karobar.local");
            File.Exists(sealOutPath).Should().BeFalse("cleanup-temp should remove seal file");
            File.Exists(adminCfgPath).Should().BeFalse("cleanup-temp should remove admin config file");

            // Confirm LicenseService.CurrentStatusAsync() verifies the installed sealed row with UTC timestamp parsing
            Karobar.Services.LicenseService.SetTestOverrides(publicKey: kpRes.Data["public_key"]!.ToString()!, machineId: machineId);
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempCliDir);
            try
            {
                var kDb = new Karobar.Services.Infrastructure.KarobarDb(explicitSqlitePath: installDbPath);
                var licSvc = new Karobar.Services.LicenseService(kDb);
                var status = await licSvc.CurrentStatusAsync(force: true);
                status["valid"].Should().Be(true);
                status["tier"].Should().Be("platinum");
            }
            finally
            {
                Karobar.Services.LicenseService.SetTestOverrides(null, null, null);
                Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRootPath);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRootPath);
            Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", prevAppRoot);
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(tempCliDir, recursive: true); } catch { }
        }

        // 5. Verify FBR Sandbox Sync & LocalWhatsAppSidecarManager status
        var fbrEngine = new FbrFiscalizationEngine();
        var testSetting = new Setting
        {
            FbrEnabled = true,
            FbrEnvironment = "sandbox",
            FbrMode = "tier1_pos_cloud",
            FbrPosId = "966130",
            FbrBearerToken = "fbr_sandbox_test_token_12345"
        };
        var fbrConn = await fbrEngine.TestConnectionAsync(testSetting);
        fbrConn["ok"].Should().Be(true);

        using var waManager = new LocalWhatsAppSidecarManager();
        waManager.ResolveAgentUrl().Should().Be("http://127.0.0.1:3002");
    }
}

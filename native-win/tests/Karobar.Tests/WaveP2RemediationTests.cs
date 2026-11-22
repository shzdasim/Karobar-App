using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Karobar.Api.Controllers;
using Karobar.Background;
using Karobar.Data;
using Karobar.Host;
using Karobar.Printing;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Karobar.Tests;

public class WaveP2RemediationTests
{
    [Fact]
    public void R2_1_And_R2_3_NativeTabWorkspace_ForceSingleton_And_FocusIndex_PreserveParallelDefault()
    {
        var ws = new NativeTabWorkspace();
        var (dash, _) = ws.OpenTab("/dashboard", "Dashboard", pinned: true);
        dash.IsPinned.Should().BeTrue();

        // Default OpenTab on parallel route MUST continue opening multiple tabs (Phase13 contract)
        var (sale1, c1) = ws.OpenTab("/sale-invoices/create/retail", "Retail Sale");
        var (sale2, c2) = ws.OpenTab("/sale-invoices/create/retail", "Retail Sale");
        c1.Should().BeTrue();
        c2.Should().BeTrue();
        sale1.TabId.Should().NotBe(sale2.TabId);
        sale2.Title.Should().Be("Retail Sale #2");

        // Explicit forceSingleton: true focuses existing tab on parallel route without duplicating
        var (pos1, cp1) = ws.OpenTab("/sale-invoices/pos", "Barcode POS", allowDuplicate: false, forceSingleton: true);
        var (pos2, cp2) = ws.OpenTab("/sale-invoices/pos", "Barcode POS", allowDuplicate: false, forceSingleton: true);
        cp1.Should().BeTrue();
        cp2.Should().BeFalse();
        pos2.TabId.Should().Be(pos1.TabId);
        ws.ActiveTabId.Should().Be(pos1.TabId);

        // allowDuplicate: true still wins even when forceSingleton: true
        var (pos3, cp3) = ws.OpenTab("/sale-invoices/pos", "Barcode POS", allowDuplicate: true, forceSingleton: true);
        cp3.Should().BeTrue();
        pos3.TabId.Should().NotBe(pos1.TabId);

        // Jump to Tab 1..8 (FocusByIndex) and Tab 9 (FocusLastTab)
        ws.FocusByIndex(0)?.TabId.Should().Be(dash.TabId);
        ws.FocusByIndex(1)?.TabId.Should().Be(sale1.TabId);
        ws.FocusByIndex(2)?.TabId.Should().Be(sale2.TabId);
        ws.FocusLastTab()?.TabId.Should().Be(pos3.TabId);
    }

    [Fact]
    public void R2_1_And_R2_3_DetachedWindow_Lifecycle_And_GlobalBroadcast()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"karobar_p2_win_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var netCfg = new NetworkConfigManager(tempDir);
            using var winMgr = new WebView2WindowManager(netCfg);

            var mainWin = winMgr.CreateMainWindow();
            var mainActiveMsgs = new List<string>();
            var mainBroadcastMsgs = new List<string>();
            mainWin.WebMessageSender = msg => mainActiveMsgs.Add(msg);
            mainWin.BroadcastMessageSender = msg => mainBroadcastMsgs.Add(msg);

            var openedTabRequests = new List<(string Route, string? Title)>();
            winMgr.OnOpenTabRequested = (r, t) => openedTabRequests.Add((r, t));

            // CreateDetachedWindow MUST register in _activeWindows without bouncing back through OnOpenTabRequested
            var detachedWin = winMgr.CreateDetachedWindow("/sale-invoices/pos", "POS Monitor");
            var detachedActiveMsgs = new List<string>();
            var detachedBroadcastMsgs = new List<string>();
            detachedWin.WebMessageSender = msg => detachedActiveMsgs.Add(msg);
            detachedWin.BroadcastMessageSender = msg => detachedBroadcastMsgs.Add(msg);

            openedTabRequests.Should().BeEmpty();
            winMgr.GetOpenWindows().Should().HaveCount(2);

            // FocusWindow switches focus cleanly
            winMgr.FocusWindow(detachedWin.Id).Should().BeTrue();
            detachedWin.IsFocused.Should().BeTrue();
            mainWin.IsFocused.Should().BeFalse();

            // Global broadcasts reach ALL windows/tabs via BroadcastMessageSender
            winMgr.SendUpdateProgress(new { status = "downloading", percent = 42 });
            winMgr.SendProductsUpdated("cube_ai");
            mainBroadcastMsgs.Should().HaveCount(2);
            detachedBroadcastMsgs.Should().HaveCount(2);
            mainBroadcastMsgs.Should().Contain(m => m.Contains("karobar:update-progress", StringComparison.Ordinal));
            detachedBroadcastMsgs.Should().Contain(m => m.Contains("karobar:products-updated", StringComparison.Ordinal));

            // Single-window commands reach ONLY the focused window's active tab
            winMgr.SendWindowCommand("open-shortcuts-modal");
            detachedActiveMsgs.Should().ContainSingle(m => m.Contains("karobar:window-command", StringComparison.Ordinal));
            mainActiveMsgs.Should().BeEmpty();

            // Closing main window MUST NOT quit app while detached window survives
            winMgr.CloseWindow(mainWin.Id).Should().BeTrue();
            winMgr.IsQuitting.Should().BeFalse();
            winMgr.MainWindow?.Id.Should().Be(detachedWin.Id);
            winMgr.GetOpenWindows().Should().ContainSingle(w => w.Id == detachedWin.Id);

            // Closing the last surviving window sets IsQuitting
            winMgr.CloseWindow(detachedWin.Id).Should().BeTrue();
            winMgr.IsQuitting.Should().BeTrue();
            winMgr.GetOpenWindows().Should().BeEmpty();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void R2_3_ShortcutAcceleratorHelper_MenuAlignment_And_PreloadScript()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"karobar_p2_menu_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var winMgr = new WebView2WindowManager(new NetworkConfigManager(tempDir));
            var fileSection = winMgr.GetApplicationMenuSections()["File & Workspace"];
            var ledgerItem = fileSection.FirstOrDefault(i => i.Path == "/customer-ledger");
            ledgerItem.Should().NotBeNull();
            ledgerItem!.Accelerator.Should().Be("CmdOrCtrl+Shift+L");

            foreach (var item in fileSection)
            {
                var parsed = ShortcutAcceleratorHelper.ParseAccelerator(item.Accelerator);
                parsed.Should().NotBeNull($"accelerator '{item.Accelerator}' must parse cleanly");
                parsed!.Ctrl.Should().BeTrue();
                parsed.Shift.Should().BeTrue();
                parsed.Key.Length.Should().Be(1);
            }

            // Sale invoice form routes guard Ctrl+Shift+P (even with query strings)
            ShortcutAcceleratorHelper.IsSaleInvoiceFormRoute("/sale-invoices/create/retail").Should().BeTrue();
            ShortcutAcceleratorHelper.IsSaleInvoiceFormRoute("/sale-invoices/create-retail?draft_id=2").Should().BeTrue();
            ShortcutAcceleratorHelper.IsSaleInvoiceFormRoute("/sale-invoices/42/edit?draft_id=5").Should().BeTrue();
            ShortcutAcceleratorHelper.IsSaleInvoiceFormRoute("/wholesale-invoices/create").Should().BeTrue();
            ShortcutAcceleratorHelper.IsSaleInvoiceFormRoute("/sale-invoices/create/wholesale").Should().BeTrue();
            ShortcutAcceleratorHelper.IsSaleInvoiceFormRoute("/sale-invoices/create-wholesale").Should().BeTrue();
            ShortcutAcceleratorHelper.IsSaleInvoiceFormRoute("/dashboard").Should().BeFalse();
            ShortcutAcceleratorHelper.IsSaleInvoiceFormRoute("/sale-invoices/pos").Should().BeFalse();
            ShortcutAcceleratorHelper.IsSaleInvoiceFormRoute("/purchase-invoices/create").Should().BeFalse();

            ShortcutAcceleratorHelper.IsInvoiceFormRoute("/purchase-invoices/create?draft=1").Should().BeTrue();
            ShortcutAcceleratorHelper.IsInvoiceFormRoute("/purchase-invoices/99/edit?foo=bar").Should().BeTrue();

            var preload = KarobarPreloadScript.GetPreloadScript(false);
            preload.Should().Contain("isSaleInvoiceFormRoute");
            preload.Should().Contain("if (isSaleInvoiceFormRoute) return;");
            preload.Should().Contain("code === \"PageDown\"");
            preload.Should().Contain("code === \"PageUp\"");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task R2_2_And_R2_4_Win32PrinterService_Bridge_And_SourceAudit()
    {
        var rawSvc = new Win32RawPrinterService();
        var printers = await rawSvc.GetPrintersAsync(forceRefresh: true);
        printers.Should().NotBeNull();

        // CUPS status 3 (Idle) must remain online when hasHardwareError is false
        var cupsPrinter = Win32RawPrinterService.ClassifyPrinter(
            "EPSON_TM_T88V", "EPSON TM-T88V", "CUPS", "USB", isDefault: true, workOfflineFlag: false, printerStatus: 3, preferredName: "EPSON_TM_T88V");
        cupsPrinter.IsOnline.Should().BeTrue();
        cupsPrinter.IsThermalPos.Should().BeTrue();
        cupsPrinter.IsPreferred.Should().BeTrue();

        var erroredPrinter = Win32RawPrinterService.ClassifyPrinter(
            "XP-80C", "XP-80C", "XPrinter", "USB001", isDefault: false, workOfflineFlag: false, printerStatus: 2, preferredName: "", hasHardwareError: true);
        erroredPrinter.IsOnline.Should().BeFalse();

        // Bridge non-Windows safe paths & status mask classification
        var tempDir = Path.Combine(Path.GetTempPath(), $"karobar_p2_bridge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var cfg = new NetworkConfigManager(tempDir);
            using var winMgr = new WebView2WindowManager(cfg);
            var bridge = new KarobarDesktopBridge(cfg, winMgr);
            (await bridge.GetPrintersAsync()).Should().NotBeNull();
            if (!OperatingSystem.IsWindows())
            {
                (await bridge.SelectDirectoryAsync()).Should().BeNull();
                var fileResJson = JsonSerializer.Serialize(await bridge.SelectSqliteFileAsync());
                fileResJson.Should().Contain("\"canceled\":true");
            }

            var offlineDto = KarobarDesktopBridge.ClassifyPrinterInfo("HP Laser", null, "HP", "USB", statusMask: 0x00000080, wmiPrinterStatus: 0, isDefault: false, wmiWorkOffline: false, preferredName: null);
            offlineDto.WorkOffline.Should().BeTrue();
            offlineDto.IsOnline.Should().BeFalse();

            var errDto = KarobarDesktopBridge.ClassifyPrinterInfo("HP Laser", null, "HP", "USB", statusMask: 0x00000002, wmiPrinterStatus: 0, isDefault: false, wmiWorkOffline: false, preferredName: null);
            errDto.IsOnline.Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }

        // Source audits verifying R2.1-R2.4 implementation invariants
        var repoRoot = ResolveRepoRoot();
        var win32Src = File.ReadAllText(Path.Combine(repoRoot, "native-win", "src", "Karobar.Printing", "Win32RawPrinterService.cs"));
        win32Src.Should().NotContainEquivalentOf("powershell.exe");
        win32Src.Should().Contain("EnumPrintersW");
        win32Src.Should().Contain("GetDefaultPrinterW");
        win32Src.Should().Contain("PRINTER_INFO_2W");
        win32Src.Should().Contain("Marshal.FreeHGlobal");

        var bridgeSrc = File.ReadAllText(Path.Combine(repoRoot, "native-win", "src", "Karobar.Host", "KarobarDesktopBridge.cs"));
        bridgeSrc.Should().NotContain("karobar-print-");
        bridgeSrc.Should().Contain("*.csv");
        bridgeSrc.Should().Contain("FolderBrowserDialog");
        bridgeSrc.Should().Contain("OpenFileDialog");
        // Only the 2 post-exit OTA swap scripts may reference powershell.exe
        var psMatches = System.Text.RegularExpressions.Regex.Matches(bridgeSrc, @"powershell\.exe", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        psMatches.Count.Should().Be(2);

        var workerSrc = File.ReadAllText(Path.Combine(repoRoot, "native-win", "src", "Karobar.Host", "OffscreenPrintWorker.cs"));
        workerSrc.Should().Contain("WebResourceRequested");
        workerSrc.Should().Contain("GetTerminalIdentityHeaders");
        workerSrc.Should().Contain("SemaphoreSlim");
        workerSrc.Should().Contain("about:blank");
        workerSrc.Should().Contain("NavigateToString");

        var programSrc = File.ReadAllText(Path.Combine(repoRoot, "native-win", "src", "Karobar.Host", "Program.cs"));
        programSrc.Should().Contain("KarobarApplicationContext");

        var stripSrc = File.ReadAllText(Path.Combine(repoRoot, "native-win", "src", "Karobar.Host", "NativeTabStripControl.cs"));
        stripSrc.Should().Contain("_overflowMenu");
        stripSrc.Should().Contain("All Open Tabs");
        stripSrc.Should().Contain("OnMouseWheel");
        stripSrc.Should().Contain("_overflowMenu.Items.Cast<ToolStripItem>().ToArray()");
    }

    [Fact]
    public async Task R2_5_WaseelaCsvAndSql_UploadPreviewAndImport_EndToEnd()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_p2_waseela_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        try
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
            var targetDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: targetDbPath)).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var targetDb = new KarobarDb(explicitSqlitePath: targetDbPath);
            await SeedAdminOnlyAsync(targetDb);

            // Unlock controller
            var swToken = await UnlockImportAsync(targetDb);

            // 1. Upload Waseela CSV with UTF-8 BOM
            var csvPath = Path.Combine(tempRoot, "tblItems.csv");
            var csvContent = "\uFEFFItemCode,ItemName,CompanyName,Formulation,PackSize,TPPack,RetailPrice,ClosingStock\n" +
                             "WAS-101,Augmentin 625mg Tab,GSK,Co-Amoxiclav,10,300,350,25\n" +
                             "WAS-102,Panadol 500mg,GSK,Paracetamol,20,100,140,40\n";
            await File.WriteAllTextAsync(csvPath, csvContent, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var uploadPayload = JsonSerializer.Serialize(new { local_file_path = csvPath, software_admin_token = swToken });
            var uploadCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", uploadPayload);
            uploadCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var uploadRes = await uploadCtrl.UploadAndInspect();
            var uploadOk = uploadRes.Should().BeOfType<OkObjectResult>().Subject;
            var uploadDict = uploadOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;

            var sessionToken = uploadDict["session_token"]?.ToString();
            sessionToken.Should().NotBeNullOrWhiteSpace();
            var waseelaDet = uploadDict["waseela_detection"].Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            waseelaDet["is_waseela_schema"].Should().Be(true);
            uploadDict.Should().ContainKey("available_target_tables");
            uploadDict.Should().ContainKey("user_remapping");

            var importableTables = uploadDict["importable_tables"].Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;
            var tblItems = importableTables.Should().ContainSingle(t => t["source_table"]!.ToString() == "tblItems").Subject;
            tblItems["target_table"].Should().Be("products");
            tblItems["source_columns"].Should().BeAssignableTo<List<string>>();

            // 2. PreviewTable with custom_columns + custom_defaults
            var prevPayload = JsonSerializer.Serialize(new
            {
                token = sessionToken,
                source_table = "tblItems",
                target_table = "products",
                custom_columns = new Dictionary<string, string> { ["rack"] = "" },
                custom_defaults = new Dictionary<string, string> { ["rack"] = "R-WASEELA" },
                software_admin_token = swToken
            });
            var prevCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", prevPayload);
            prevCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var prevRes = await prevCtrl.PreviewTable();
            var prevOk = prevRes.Should().BeOfType<OkObjectResult>().Subject;
            var prevDict = prevOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            var sampleRows = prevDict["sample_rows"].Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;
            sampleRows.Should().NotBeEmpty();
            sampleRows[0]["rack"]?.ToString().Should().Be("R-WASEELA");

            // 3. Commit CSV import
            var commitPayload = JsonSerializer.Serialize(new
            {
                session_token = sessionToken,
                options = new
                {
                    mode = "merge",
                    conflict_policy = "upsert",
                    create_backup = false,
                    reconcile_batches = true,
                    tables = new Dictionary<string, object>
                    {
                        ["tblItems"] = new
                        {
                            enabled = true,
                            target_table = "products",
                            columns = new Dictionary<string, string>(),
                            defaults = new Dictionary<string, string> { ["rack"] = "R-WASEELA" }
                        }
                    }
                },
                software_admin_token = swToken
            });
            var commitCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", commitPayload);
            commitCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var commitRes = await commitCtrl.Commit();
            var commitOk = commitRes.Should().BeOfType<OkObjectResult>().Subject;
            var commitDict = commitOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            var stats = commitDict["stats"].Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            stats["safety_backup"].Should().BeNull();
            var recon = stats["reconciliation"].Should().BeAssignableTo<Dictionary<string, int>>().Subject;
            recon["opening_batches_created"].Should().Be(2);
            recon["formulations_synced"].Should().Be(2);

            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products")).Should().Be(2);
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM brands WHERE LOWER(name) = 'gsk'")).Should().Be(1);
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM batches WHERE batch_number LIKE 'OPENING-%'")).Should().Be(2);
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM formulations WHERE normalized_name = 'paracetamol'")).Should().Be(1);

            // 4. Upload & Commit Waseela SQL Server dump ([dbo].[tblKhata] + GO)
            var sqlPath = Path.Combine(tempRoot, "waseela_khata.sql");
            var sqlDump = """
                CREATE TABLE [dbo].[tblKhata] (
                    id INTEGER PRIMARY KEY,
                    PartyName TEXT,
                    OpeningBalance REAL,
                    KhataDetail TEXT
                );
                GO
                INSERT INTO [dbo].[tblKhata] (id, PartyName, OpeningBalance, KhataDetail)
                VALUES (1, 'Al-Shifa Medical Store', 4500.50, 'Opening Balance Brought Forward');
                GO
                """;
            await File.WriteAllTextAsync(sqlPath, sqlDump, Encoding.UTF8);

            var sqlUploadPayload = JsonSerializer.Serialize(new { local_file_path = sqlPath, software_admin_token = swToken });
            var sqlUploadCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", sqlUploadPayload);
            sqlUploadCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var sqlUpOk = (await sqlUploadCtrl.UploadAndInspect()).Should().BeOfType<OkObjectResult>().Subject;
            var sqlUpDict = sqlUpOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            var sqlToken = sqlUpDict["session_token"]?.ToString()!;

            var sqlCommitPayload = JsonSerializer.Serialize(new
            {
                session_token = sqlToken,
                options = new { mode = "merge", conflict_policy = "upsert", create_backup = false, reconcile_batches = false },
                software_admin_token = swToken
            });
            var sqlCommitCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", sqlCommitPayload);
            sqlCommitCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            (await sqlCommitCtrl.Commit()).Should().BeOfType<OkObjectResult>();

            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM customers WHERE name = 'Al-Shifa Medical Store'")).Should().Be(1);
            var ledgerRow = await targetDb.QuerySingleOrDefaultAsync("SELECT customer_id, invoice_total, balance_remaining, entry_type FROM customer_ledgers LIMIT 1");
            ledgerRow.Should().NotBeNull();
            Convert.ToDouble(ledgerRow!["invoice_total"]).Should().BeApproximately(4500.50, 0.01);
            ledgerRow["entry_type"]?.ToString().Should().Be("manual");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task R2_5_MergeMode_RemapsCollidingParentToChildFks_PolymorphicIds_NaturalKeys_And_Fallbacks()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_p2_merge_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        try
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
            var targetDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
            var sourceDbPath = Path.Combine(tempRoot, "source_merge.sqlite");

            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: targetDbPath)).EnsureSchemaReadyAsync();
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: sourceDbPath)).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var targetDb = new KarobarDb(explicitSqlitePath: targetDbPath);
            await SeedAdminOnlyAsync(targetDb);

            // Seed existing id=1 records in target DB
            await using (var dst = new SqliteConnection($"Data Source={targetDbPath};Pooling=False;"))
            {
                await dst.OpenAsync();
                await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
                await dst.ExecuteAsync("""
                    INSERT INTO categories (id, name) VALUES (1, 'Existing Cat');
                    INSERT INTO brands (id, name) VALUES (1, 'Shared Brand');
                    INSERT INTO suppliers (id, name) VALUES (1, 'Existing Supplier');
                    INSERT INTO customers (id, name, email) VALUES (1, 'Existing Customer', 'existing@test.local');
                    INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, supplier_id, pack_size, quantity, pack_sale_price, unit_sale_price)
                        VALUES (1, 'EXT-001', 'EXT-001', 'Existing Product', 1, 1, 1, 10, 50, 100, 10);
                    INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total, total_receive, sale_type, invoice_type, status)
                        VALUES (1, 1, 1, 'SI-EXT-1', '2026-10-01', 100, 100, 100, 'retail', 'debit', 'posted');
                    """);
            }

            // Seed colliding id=1 records + orphans + polymorphic FKs in source DB
            await using (var src = new SqliteConnection($"Data Source={sourceDbPath};Pooling=False;"))
            {
                await src.OpenAsync();
                await src.ExecuteAsync("PRAGMA foreign_keys = OFF;");
                await src.ExecuteAsync("""
                    INSERT INTO categories (id, name) VALUES (1, 'Imported Syrup');
                    INSERT INTO brands (id, name) VALUES (1, 'Shared Brand');
                    INSERT INTO suppliers (id, name) VALUES (1, 'Imported Supplier');
                    INSERT INTO customers (id, name, email) VALUES (1, 'Imported Customer', 'imp@test.local');
                    INSERT INTO products (id, product_code, barcode, name, formulation, category_id, brand_id, supplier_id, pack_size, quantity, pack_sale_price, unit_sale_price)
                        VALUES (1, 'IMP-001', 'IMP-001', 'Imported Amoxil', 'Amoxicillin', 1, 1, 1, 10, 15, 250, 25);
                    INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity, pack_sale_price, unit_sale_price)
                        VALUES (2, 'IMP-002', 'IMP-002', 'Orphaned Product', 999, 999, 10, 0, 50, 5);
                    INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total, total_receive, sale_type, invoice_type, status)
                        VALUES (1, 999, 1, 'SI-IMP-1', '2026-11-01', 250, 250, 250, 'retail', 'debit', 'posted');
                    INSERT INTO sale_invoice_items (id, sale_invoice_id, product_id, quantity, price, sub_total)
                        VALUES (1, 1, 1, 10, 25, 250);
                    INSERT INTO payments (id, payment_number, party_type, party_id, payment_date, amount, allocated_amount, unapplied_amount, payment_method, status, created_by_id)
                        VALUES (1, 'PAY-IMP-1', 'customer', 1, '2026-11-01', 250, 250, 0, 'cash', 'posted', 999);
                    INSERT INTO payment_allocations (id, payment_id, invoice_type, invoice_id, amount)
                        VALUES (1, 1, 'sale_invoice', 1, 250);
                    INSERT INTO expense_categories (id, name, code)
                        VALUES (1, 'Utilities', 'UTIL-1');
                    INSERT INTO expenses (id, expense_number, expense_category_id, amount, date, payment_method, description, user_id)
                        VALUES (1, 'EXP-SKIP-1', 1, 50, '2026-11-01', 'cash', 'Should Be Skipped', 1);
                    """);
            }

            var swToken = await UnlockImportAsync(targetDb);

            var uploadPayload = JsonSerializer.Serialize(new { local_file_path = sourceDbPath, software_admin_token = swToken });
            var uploadCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", uploadPayload);
            uploadCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var uploadOk = (await uploadCtrl.UploadAndInspect()).Should().BeOfType<OkObjectResult>().Subject;
            var uploadDict = uploadOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            var token = uploadDict["session_token"]?.ToString()!;

            // Disable expenses table via options.tables and set create_backup=false
            var commitPayload = JsonSerializer.Serialize(new
            {
                session_token = token,
                options = new
                {
                    mode = "merge",
                    conflict_policy = "skip",
                    create_backup = false,
                    reconcile_batches = true,
                    tables = new Dictionary<string, object>
                    {
                        ["expenses"] = new { enabled = false, target_table = "expenses" }
                    }
                },
                software_admin_token = swToken
            });

            var commitCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", commitPayload);
            commitCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var commitOk = (await commitCtrl.Commit()).Should().BeOfType<OkObjectResult>().Subject;
            var commitDict = commitOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            var stats = commitDict["stats"].Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            stats["safety_backup"].Should().BeNull();

            // Pre-existing id=1 records must remain intact
            (await targetDb.QuerySingleOrDefaultAsync("SELECT name FROM categories WHERE id = 1"))!["name"]?.ToString().Should().Be("Existing Cat");
            (await targetDb.QuerySingleOrDefaultAsync("SELECT name FROM customers WHERE id = 1"))!["name"]?.ToString().Should().Be("Existing Customer");
            (await targetDb.QuerySingleOrDefaultAsync("SELECT name FROM products WHERE id = 1"))!["name"]?.ToString().Should().Be("Existing Product");
            (await targetDb.QuerySingleOrDefaultAsync("SELECT posted_number FROM sale_invoices WHERE id = 1"))!["posted_number"]?.ToString().Should().Be("SI-EXT-1");

            // Disabled expenses table must NOT be imported
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM expenses")).Should().Be(0);

            // Natural-key matched Shared Brand must stay count=1
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM brands WHERE name = 'Shared Brand'")).Should().Be(1);

            // Remapped IDs check
            var newCatId = await targetDb.ExecuteScalarAsync<long>("SELECT id FROM categories WHERE name = 'Imported Syrup'");
            var newCustId = await targetDb.ExecuteScalarAsync<long>("SELECT id FROM customers WHERE name = 'Imported Customer'");
            var newProdId = await targetDb.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'IMP-001'");
            var newSaleId = await targetDb.ExecuteScalarAsync<long>("SELECT id FROM sale_invoices WHERE posted_number = 'SI-IMP-1'");
            var newPayId = await targetDb.ExecuteScalarAsync<long>("SELECT id FROM payments WHERE payment_number = 'PAY-IMP-1'");

            newCatId.Should().BeGreaterThan(1);
            newCustId.Should().BeGreaterThan(1);
            newProdId.Should().BeGreaterThan(1);
            newSaleId.Should().BeGreaterThan(1);
            newPayId.Should().BeGreaterThan(0);

            var impProd = await targetDb.QuerySingleOrDefaultAsync("SELECT category_id, brand_id FROM products WHERE id = @id", new { id = newProdId });
            Convert.ToInt64(impProd!["category_id"]).Should().Be(newCatId);
            Convert.ToInt64(impProd["brand_id"]).Should().Be(1); // Matched existing Shared Brand id=1

            // Orphaned product got fallback "General" category & brand
            var orphanProd = await targetDb.QuerySingleOrDefaultAsync("SELECT category_id, brand_id FROM products WHERE product_code = 'IMP-002'");
            var genCatId = await targetDb.ExecuteScalarAsync<long>("SELECT id FROM categories WHERE name = 'General'");
            var genBrandId = await targetDb.ExecuteScalarAsync<long>("SELECT id FROM brands WHERE name = 'General'");
            Convert.ToInt64(orphanProd!["category_id"]).Should().Be(genCatId);
            Convert.ToInt64(orphanProd["brand_id"]).Should().Be(genBrandId);

            // Sale invoice remapped customer_id & orphan user_id=999 -> 1
            var impSale = await targetDb.QuerySingleOrDefaultAsync("SELECT customer_id, user_id FROM sale_invoices WHERE id = @id", new { id = newSaleId });
            Convert.ToInt64(impSale!["customer_id"]).Should().Be(newCustId);
            Convert.ToInt64(impSale["user_id"]).Should().Be(1);

            // Sale invoice item remapped sale_invoice_id -> newSaleId, product_id -> newProdId
            var impItem = await targetDb.QuerySingleOrDefaultAsync("SELECT sale_invoice_id, product_id FROM sale_invoice_items WHERE sale_invoice_id = @sid", new { sid = newSaleId });
            impItem.Should().NotBeNull();
            Convert.ToInt64(impItem!["product_id"]).Should().Be(newProdId);

            // Polymorphic party_id & invoice_id remapped cleanly
            var impPay = await targetDb.QuerySingleOrDefaultAsync("SELECT party_id FROM payments WHERE id = @id", new { id = newPayId });
            Convert.ToInt64(impPay!["party_id"]).Should().Be(newCustId);

            var impAlloc = await targetDb.QuerySingleOrDefaultAsync("SELECT invoice_id FROM payment_allocations WHERE payment_id = @pid", new { pid = newPayId });
            Convert.ToInt64(impAlloc!["invoice_id"]).Should().Be(newSaleId);

            // Reconciliation created OPENING-{newProdId} and lowercase formulation
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM batches WHERE batch_number = @bno", new { bno = $"OPENING-{newProdId}" })).Should().Be(1);
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM formulations WHERE normalized_name = 'amoxicillin'")).Should().Be(1);

            // Cancel endpoint executes cleanly
            var cancelCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1);
            cancelCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            (await cancelCtrl.Cancel(Guid.NewGuid().ToString("D"))).Should().BeOfType<OkObjectResult>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task R2_5_Stage1And2TopologicalOrdering_RemapsProductSuppliersAndWholesalePrices()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_p2_topo_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        try
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
            var targetDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
            var sourceDbPath = Path.Combine(tempRoot, "source_topo.sqlite");

            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: targetDbPath)).EnsureSchemaReadyAsync();
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: sourceDbPath)).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var targetDb = new KarobarDb(explicitSqlitePath: targetDbPath);
            await SeedAdminOnlyAsync(targetDb);

            // Seed pre-existing id=1 records in target DB
            await using (var dst = new SqliteConnection($"Data Source={targetDbPath};Pooling=False;"))
            {
                await dst.OpenAsync();
                await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
                await dst.ExecuteAsync("""
                    INSERT INTO categories (id, name) VALUES (1, 'Target Cat');
                    INSERT INTO brands (id, name) VALUES (1, 'Target Brand');
                    INSERT INTO suppliers (id, name) VALUES (1, 'Target Supplier');
                    INSERT INTO customers (id, name, email) VALUES (1, 'Target Customer', 'tgt@test.local');
                    INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, supplier_id, pack_size, quantity, pack_sale_price, unit_sale_price)
                        VALUES (1, 'TGT-001', 'TGT-001', 'Target Product', 1, 1, 1, 10, 20, 100, 10);
                    INSERT INTO employees (id, employee_code, name, basic_salary) VALUES (1, 'EMP-0001', 'Target Staff', 25000);
                    INSERT INTO payroll_runs (id, run_number, period_month, start_date, end_date, status) VALUES (1, 'PR-TGT-1', '2026-10', '2026-10-01', '2026-10-31', 'paid');
                    INSERT INTO payroll_items (id, payroll_run_id, employee_id, basic_salary, net_payable, payment_status) VALUES (1, 1, 1, 25000, 25000, 'paid');
                    """);
            }

            // Seed colliding id=1 records in source DB for Stage 1->2 (products -> product_suppliers / customer_whole_sale_prices)
            // and Stage 2->3 (payroll_items -> employee_advances)
            await using (var src = new SqliteConnection($"Data Source={sourceDbPath};Pooling=False;"))
            {
                await src.OpenAsync();
                await src.ExecuteAsync("PRAGMA foreign_keys = OFF;");
                await src.ExecuteAsync("""
                    INSERT INTO categories (id, name) VALUES (1, 'Source Cat');
                    INSERT INTO brands (id, name) VALUES (1, 'Source Brand');
                    INSERT INTO suppliers (id, name) VALUES (1, 'Source Supplier');
                    INSERT INTO customers (id, name, email) VALUES (1, 'Source Customer', 'src@test.local');
                    INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, supplier_id, pack_size, quantity, pack_sale_price, unit_sale_price)
                        VALUES (1, 'SRC-001', 'SRC-001', 'Source Product', 1, 1, 1, 10, 30, 300, 30);
                    INSERT INTO product_suppliers (id, product_id, supplier_id, supplier_sku, last_purchase_price)
                        VALUES (1, 1, 1, 'SUP-SKU-COLLIDE', 240);
                    INSERT INTO customer_whole_sale_prices (id, customer_id, product_id, pack_price)
                        VALUES (1, 1, 1, 275.50);
                    INSERT INTO customer_regular_items (id, customer_id, product_id)
                        VALUES (1, 1, 1);
                    INSERT INTO employees (id, employee_code, name, basic_salary) VALUES (1, 'EMP-0002', 'Source Staff', 30000);
                    INSERT INTO payroll_runs (id, run_number, period_month, start_date, end_date, status) VALUES (1, 'PR-SRC-1', '2026-11', '2026-11-01', '2026-11-30', 'paid');
                    INSERT INTO payroll_items (id, payroll_run_id, employee_id, basic_salary, net_payable, payment_status) VALUES (1, 1, 1, 30000, 30000, 'paid');
                    INSERT INTO employee_advances (id, advance_number, employee_id, type, amount, balance_after, date, payment_method, payroll_item_id)
                        VALUES (1, 'ADV-SRC-1', 1, 'advance', 5000, 5000, '2026-11-01', 'cash', 1);
                    """);
            }

            var swToken = await UnlockImportAsync(targetDb);
            var uploadPayload = JsonSerializer.Serialize(new { local_file_path = sourceDbPath, software_admin_token = swToken });
            var uploadCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", uploadPayload);
            uploadCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var uploadOk = (await uploadCtrl.UploadAndInspect()).Should().BeOfType<OkObjectResult>().Subject;
            var token = uploadOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject["session_token"]!.ToString()!;

            var commitPayload = JsonSerializer.Serialize(new
            {
                session_token = token,
                options = new { mode = "merge", conflict_policy = "skip", create_backup = false, reconcile_batches = false },
                software_admin_token = swToken
            });
            var commitCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", commitPayload);
            commitCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            (await commitCtrl.Commit()).Should().BeOfType<OkObjectResult>();

            var newProdId = await targetDb.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'SRC-001'");
            var newSupId = await targetDb.ExecuteScalarAsync<long>("SELECT id FROM suppliers WHERE name = 'Source Supplier'");
            var newCustId = await targetDb.ExecuteScalarAsync<long>("SELECT id FROM customers WHERE name = 'Source Customer'");
            var newPayrollRunId = await targetDb.ExecuteScalarAsync<long>("SELECT id FROM payroll_runs WHERE run_number = 'PR-SRC-1'");
            var newPayrollItemId = await targetDb.ExecuteScalarAsync<long>("SELECT id FROM payroll_items WHERE payroll_run_id = @rid", new { rid = newPayrollRunId });

            newProdId.Should().BeGreaterThan(1);
            newSupId.Should().BeGreaterThan(1);
            newCustId.Should().BeGreaterThan(1);
            newPayrollItemId.Should().BeGreaterThan(1);

            // Child tables MUST point to the newly remapped parent IDs, NOT pre-existing id=1
            var psRow = await targetDb.QuerySingleOrDefaultAsync("SELECT product_id, supplier_id FROM product_suppliers WHERE supplier_sku = 'SUP-SKU-COLLIDE'");
            psRow.Should().NotBeNull();
            Convert.ToInt64(psRow!["product_id"]).Should().Be(newProdId);
            Convert.ToInt64(psRow["supplier_id"]).Should().Be(newSupId);

            var wsRow = await targetDb.QuerySingleOrDefaultAsync("SELECT customer_id, product_id FROM customer_whole_sale_prices WHERE pack_price = 275.50");
            wsRow.Should().NotBeNull();
            Convert.ToInt64(wsRow!["product_id"]).Should().Be(newProdId);
            Convert.ToInt64(wsRow["customer_id"]).Should().Be(newCustId);

            var regRow = await targetDb.QuerySingleOrDefaultAsync("SELECT customer_id, product_id FROM customer_regular_items WHERE customer_id = @cid", new { cid = newCustId });
            regRow.Should().NotBeNull();
            Convert.ToInt64(regRow!["product_id"]).Should().Be(newProdId);

            var advRow = await targetDb.QuerySingleOrDefaultAsync("SELECT payroll_item_id FROM employee_advances WHERE advance_number = 'ADV-SRC-1'");
            advRow.Should().NotBeNull();
            Convert.ToInt64(advRow!["payroll_item_id"]).Should().Be(newPayrollItemId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task R2_5_EdgeCases_SkipPreview_SupplierLedgerCredit_MultilineCsv_SsmsTsql_SettingsDefaults_MultiSourcePerTable()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_p2_edges_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        try
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
            var targetDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: targetDbPath)).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var targetDb = new KarobarDb(explicitSqlitePath: targetDbPath);
            await SeedAdminOnlyAsync(targetDb);
            var swToken = await UnlockImportAsync(targetDb);

            // 1. Quoted multiline CSV + PreviewTable "__skip__" fidelity
            var multilineCsv = Path.Combine(tempRoot, "tblItems.csv");
            var csvBody = "ItemCode,ItemName,Description,Rack,PackSize,RetailPrice\n" +
                          "\"ML-1\",\"Multiline Drug\",\"Line 1\nLine 2\",\"OLD-RACK\",10,100\n";
            await File.WriteAllTextAsync(multilineCsv, csvBody, Encoding.UTF8);

            var up1Payload = JsonSerializer.Serialize(new { local_file_path = multilineCsv, software_admin_token = swToken });
            var up1Ctrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", up1Payload);
            up1Ctrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var up1Dict = (await up1Ctrl.UploadAndInspect()).Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            var tok1 = up1Dict["session_token"]!.ToString()!;

            // PreviewTable with custom_columns { "rack": "" } -> normalized to "__skip__" must clear OLD-RACK in sample_rows
            var prevSkipPayload = JsonSerializer.Serialize(new
            {
                token = tok1,
                source_table = "tblItems",
                target_table = "products",
                custom_columns = new Dictionary<string, string> { ["rack"] = "" },
                software_admin_token = swToken
            });
            var prevSkipCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", prevSkipPayload);
            prevSkipCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var prevSkipDict = (await prevSkipCtrl.PreviewTable()).Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            var prevRows = prevSkipDict["sample_rows"].Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;
            prevRows.Should().ContainSingle();
            prevRows[0]["rack"].Should().BeNull();
            prevRows[0]["description"]?.ToString().Should().Be("Line 1\nLine 2");

            // Commit multiline CSV -> must insert exactly 1 product (not 2) with intact newline description
            var cm1Payload = JsonSerializer.Serialize(new
            {
                session_token = tok1,
                options = new { mode = "merge", conflict_policy = "upsert", create_backup = false, reconcile_batches = false },
                software_admin_token = swToken
            });
            var cm1Ctrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", cm1Payload);
            cm1Ctrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            (await cm1Ctrl.Commit()).Should().BeOfType<OkObjectResult>();

            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products")).Should().Be(1);
            var mlProd = await targetDb.QuerySingleOrDefaultAsync("SELECT id, description FROM products WHERE product_code = 'ML-1'");
            mlProd.Should().NotBeNull();
            mlProd!["description"]?.ToString().Should().Be("Line 1\nLine 2");
            var mlProdId = Convert.ToInt64(mlProd["id"]);

            // 2. supplier_ledgers.credit_remaining synthesis when omitted
            var supCsv = Path.Combine(tempRoot, "supplier_khata.csv");
            await File.WriteAllTextAsync(supCsv,
                "PartyName,InvoiceTotal,TotalPaid,Description\n" +
                "MedSupply Co,5000,1200,Opening Supplier Balance\n" +
                "MedSupply Zero,3000,,Unpaid Opening\n",
                Encoding.UTF8);

            var up2Payload = JsonSerializer.Serialize(new { local_file_path = supCsv, software_admin_token = swToken });
            var up2Ctrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", up2Payload);
            up2Ctrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var tok2 = (await up2Ctrl.UploadAndInspect()).Should().BeOfType<OkObjectResult>().Subject.Value
                .Should().BeAssignableTo<Dictionary<string, object?>>().Subject["session_token"]!.ToString()!;

            var cm2Payload = JsonSerializer.Serialize(new
            {
                session_token = tok2,
                options = new
                {
                    mode = "merge",
                    conflict_policy = "upsert",
                    create_backup = false,
                    reconcile_batches = false,
                    tables = new Dictionary<string, object>
                    {
                        ["supplier_khata"] = new
                        {
                            enabled = true,
                            target_table = "supplier_ledgers",
                            columns = new Dictionary<string, string> { ["supplier_id"] = "PartyName" }
                        }
                    }
                },
                software_admin_token = swToken
            });
            var cm2Ctrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", cm2Payload);
            cm2Ctrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            (await cm2Ctrl.Commit()).Should().BeOfType<OkObjectResult>();

            var sl1 = await targetDb.QuerySingleOrDefaultAsync("SELECT credit_remaining FROM supplier_ledgers WHERE description = 'Opening Supplier Balance'");
            var sl2 = await targetDb.QuerySingleOrDefaultAsync("SELECT credit_remaining FROM supplier_ledgers WHERE description = 'Unpaid Opening'");
            Convert.ToDouble(sl1!["credit_remaining"]).Should().BeApproximately(3800.0, 0.01);
            Convert.ToDouble(sl2!["credit_remaining"]).Should().BeApproximately(3000.0, 0.01);

            // 3. SSMS T-SQL DDL + INSERT [dbo].[tblBatch] (without INTO) + N'...' literals
            var ssmsPath = Path.Combine(tempRoot, "ssms_batches.sql");
            var ssmsSql = $"""
                SET ANSI_NULLS ON
                GO
                CREATE TABLE [dbo].[tblBatch](
                    [Id] [int] IDENTITY(1,1) NOT NULL PRIMARY KEY,
                    [ProductId] [int] NULL,
                    [BatchNo] [nvarchar](100) NULL,
                    [ExpDate] [nvarchar](50) NULL,
                    [ClosingStock] [decimal](18, 2) NULL
                ) ON [PRIMARY]
                GO
                INSERT [dbo].[tblBatch] ([Id], [ProductId], [BatchNo], [ExpDate], [ClosingStock]) VALUES (1, {mlProdId}, N'SSMS-B1', N'12/2027', 20)
                GO
                """;
            await File.WriteAllTextAsync(ssmsPath, ssmsSql, Encoding.UTF8);

            var up3Payload = JsonSerializer.Serialize(new { local_file_path = ssmsPath, software_admin_token = swToken });
            var up3Ctrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", up3Payload);
            up3Ctrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var up3Dict = (await up3Ctrl.UploadAndInspect()).Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            var tok3 = up3Dict["session_token"]!.ToString()!;
            var tbl3 = up3Dict["importable_tables"].Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject
                .Should().ContainSingle(t => t["source_table"]!.ToString() == "tblBatch").Subject;
            Convert.ToInt32(tbl3["row_count"]).Should().Be(1);

            var cm3Payload = JsonSerializer.Serialize(new
            {
                session_token = tok3,
                options = new { mode = "merge", conflict_policy = "upsert", create_backup = false, reconcile_batches = false },
                software_admin_token = swToken
            });
            var cm3Ctrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", cm3Payload);
            cm3Ctrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            (await cm3Ctrl.Commit()).Should().BeOfType<OkObjectResult>();

            var ssmsBatch = await targetDb.QuerySingleOrDefaultAsync("SELECT product_id, quantity FROM batches WHERE batch_number = 'SSMS-B1'");
            ssmsBatch.Should().NotBeNull();
            Convert.ToInt64(ssmsBatch!["product_id"]).Should().Be(mlProdId);
            Convert.ToInt32(ssmsBatch["quantity"]).Should().Be(20);

            // 4. options.tables.settings.defaults override in Commit + multi-source-to-single-target per_table aggregation
            var multiSrcPath = Path.Combine(tempRoot, "multi_and_settings.sqlite");
            await using (var msrc = new SqliteConnection($"Data Source={multiSrcPath};Pooling=False;"))
            {
                await msrc.OpenAsync();
                await msrc.ExecuteAsync("""
                    CREATE TABLE settings (id INTEGER PRIMARY KEY, store_name TEXT, cube_groq_api_key TEXT);
                    INSERT INTO settings (id, store_name, cube_groq_api_key) VALUES (1, 'Old Source Store', 'SECRET-FROM-SRC');
                    CREATE TABLE cats_a (id INTEGER PRIMARY KEY, name TEXT);
                    INSERT INTO cats_a (id, name) VALUES (1, 'Cat Alpha');
                    CREATE TABLE cats_b (id INTEGER PRIMARY KEY, name TEXT);
                    INSERT INTO cats_b (id, name) VALUES (1, 'Cat Beta');
                    """);
            }

            var engine = new WaseelaAndSqliteImportEngine(tempRoot);
            var stagedMulti = await engine.StageDatabaseFromFileAsync(multiSrcPath, "multi_and_settings.sqlite");
            var tokMulti = stagedMulti["token"]!.ToString()!;
            var execRes = await engine.ExecuteImportCoreAsync(targetDbPath, tokMulti, new SqliteImportExecutionOptions
            {
                Mode = "merge",
                ConflictPolicy = "upsert",
                ReconcileBatches = false,
                CurrentUserId = 1,
                Tables = new Dictionary<string, TableImportOverride>(StringComparer.OrdinalIgnoreCase)
                {
                    ["settings"] = new TableImportOverride
                    {
                        Enabled = true,
                        TargetTable = "settings",
                        ColumnMap = new Dictionary<string, ColumnMappingOverride>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["store_name"] = new ColumnMappingOverride { SourceColumn = "__skip__", DefaultValue = "Defaulted Store Name" },
                            ["cube_groq_api_key"] = new ColumnMappingOverride { SourceColumn = "__skip__", DefaultValue = "INJECTED-SECRET" }
                        }
                    },
                    ["cats_a"] = new TableImportOverride { Enabled = true, TargetTable = "categories" },
                    ["cats_b"] = new TableImportOverride { Enabled = true, TargetTable = "categories" }
                }
            });

            var settingsRow = await targetDb.QuerySingleOrDefaultAsync("SELECT store_name, cube_groq_api_key FROM settings WHERE id = 1");
            settingsRow.Should().NotBeNull();
            settingsRow!["store_name"]?.ToString().Should().Be("Defaulted Store Name");
            settingsRow["cube_groq_api_key"].Should().BeNull();

            var perTable = execRes["per_table"].Should().BeAssignableTo<Dictionary<string, Dictionary<string, int>>>().Subject;
            perTable.Should().ContainKey("categories");
            perTable["categories"]["source_rows"].Should().Be(2);
            perTable["categories"]["inserted"].Should().Be(2);
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categories WHERE name IN ('Cat Alpha', 'Cat Beta')")).Should().Be(2);
        }
        finally
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task R2_5_TSqlCreateTable_NoSpaceOnPrimary_And_CustomFilegroupTextImageOn_StageAndCommitCleanly()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_p2_tsql_fg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        try
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
            var targetDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: targetDbPath)).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var targetDb = new KarobarDb(explicitSqlitePath: targetDbPath);
            await SeedAdminOnlyAsync(targetDb);
            var swToken = await UnlockImportAsync(targetDb);

            var sqlPath = Path.Combine(tempRoot, "tsql_filegroups.sql");
            var sqlBody = """
                CREATE TABLE tblNoSpace (id int IDENTITY(1,1) PRIMARY KEY, title text)ON [PRIMARY]
                GO
                INSERT INTO tblNoSpace (title) VALUES (N'NoSpace Category');
                GO
                CREATE TABLE tblFg (id int IDENTITY(1,1) PRIMARY KEY, body nvarchar(max)) ON [DATA_FG] TEXTIMAGE_ON [PRIMARY]
                GO
                INSERT INTO tblFg (body) VALUES (N'CustomFg Brand');
                GO
                CREATE TABLE tblNoSpaceUnbracketed (id int IDENTITY(1,1) PRIMARY KEY, title text)ON PRIMARY
                GO
                INSERT INTO tblNoSpaceUnbracketed (title) VALUES ('Unbracketed NoSpace');
                GO
                CREATE TABLE tblMixedUnbracketedFg (id int IDENTITY(1,1) PRIMARY KEY, body nvarchar(max)) ON DATA_FG TEXTIMAGE_ON [PRIMARY]
                GO
                INSERT INTO tblMixedUnbracketedFg (body) VALUES ('Mixed Custom FG');
                GO
                CREATE TABLE tblNoSpaceCustomFg (id int IDENTITY(1,1) PRIMARY KEY, body nvarchar(max))ON [DATA_FG] TEXTIMAGE_ON [LOB_FG]
                GO
                INSERT INTO tblNoSpaceCustomFg (body) VALUES ('NoSpace Custom FG');
                GO
                """;
            await File.WriteAllTextAsync(sqlPath, sqlBody, Encoding.UTF8);

            var uploadPayload = JsonSerializer.Serialize(new { local_file_path = sqlPath, software_admin_token = swToken });
            var uploadCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", uploadPayload);
            uploadCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            var uploadOk = (await uploadCtrl.UploadAndInspect()).Should().BeOfType<OkObjectResult>().Subject;
            var uploadDict = uploadOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            var sessionToken = uploadDict["session_token"]?.ToString()!;
            sessionToken.Should().NotBeNullOrWhiteSpace();

            var tables = uploadDict["importable_tables"].Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;
            tables.Should().Contain(t => t["source_table"]!.ToString() == "tblNoSpace" && Convert.ToInt32(t["row_count"]) == 1);
            tables.Should().Contain(t => t["source_table"]!.ToString() == "tblFg" && Convert.ToInt32(t["row_count"]) == 1);
            tables.Should().Contain(t => t["source_table"]!.ToString() == "tblNoSpaceUnbracketed" && Convert.ToInt32(t["row_count"]) == 1);
            tables.Should().Contain(t => t["source_table"]!.ToString() == "tblMixedUnbracketedFg" && Convert.ToInt32(t["row_count"]) == 1);
            tables.Should().Contain(t => t["source_table"]!.ToString() == "tblNoSpaceCustomFg" && Convert.ToInt32(t["row_count"]) == 1);

            var commitPayload = JsonSerializer.Serialize(new
            {
                session_token = sessionToken,
                options = new
                {
                    mode = "merge",
                    conflict_policy = "upsert",
                    create_backup = false,
                    reconcile_batches = false,
                    tables = new Dictionary<string, object>
                    {
                        ["tblNoSpace"] = new
                        {
                            enabled = true,
                            target_table = "categories",
                            columns = new Dictionary<string, string> { ["name"] = "title" }
                        },
                        ["tblFg"] = new
                        {
                            enabled = true,
                            target_table = "brands",
                            columns = new Dictionary<string, string> { ["name"] = "body" }
                        },
                        ["tblNoSpaceUnbracketed"] = new
                        {
                            enabled = true,
                            target_table = "categories",
                            columns = new Dictionary<string, string> { ["name"] = "title" }
                        },
                        ["tblMixedUnbracketedFg"] = new
                        {
                            enabled = true,
                            target_table = "brands",
                            columns = new Dictionary<string, string> { ["name"] = "body" }
                        },
                        ["tblNoSpaceCustomFg"] = new
                        {
                            enabled = true,
                            target_table = "brands",
                            columns = new Dictionary<string, string> { ["name"] = "body" }
                        }
                    }
                },
                software_admin_token = swToken
            });
            var commitCtrl = AttachAdminContext(new SqliteImportController(targetDb), 1, "", commitPayload);
            commitCtrl.HttpContext.Request.Headers["X-Software-Admin-Token"] = swToken;
            (await commitCtrl.Commit()).Should().BeOfType<OkObjectResult>();

            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM categories WHERE name IN ('NoSpace Category', 'Unbracketed NoSpace')")).Should().Be(2);
            (await targetDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM brands WHERE name IN ('CustomFg Brand', 'Mixed Custom FG', 'NoSpace Custom FG')")).Should().Be(3);
        }
        finally
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    private static async Task<string> UnlockImportAsync(KarobarDb db)
    {
        var unlockPayload = JsonSerializer.Serialize(new { password = "password" });
        var unlockCtrl = AttachAdminContext(new SqliteImportController(db), 1, "", unlockPayload);
        var unlockRes = await unlockCtrl.Unlock();
        var unlockOk = unlockRes.Should().BeOfType<OkObjectResult>().Subject;
        var unlockDict = unlockOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
        return unlockDict["unlock_token"]?.ToString() ?? "";
    }

    private static TController AttachAdminContext<TController>(TController controller, long userId, string queryString = "", string? jsonBody = null)
        where TController : ControllerBase
    {
        var httpContext = new DefaultHttpContext();
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        ], "Sanctum");
        httpContext.User = new ClaimsPrincipal(identity);

        if (!string.IsNullOrEmpty(queryString))
        {
            httpContext.Request.QueryString = new QueryString(queryString);
        }

        if (jsonBody != null)
        {
            httpContext.Request.ContentType = "application/json";
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(jsonBody));
        }

        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static async Task SeedAdminOnlyAsync(KarobarDb db)
    {
        await using var conn = await db.OpenConnectionAsync();
        await conn.ExecuteAsync("""
            INSERT OR IGNORE INTO roles (id, name, guard_name) VALUES (1, 'Admin', 'sanctum');
            INSERT OR IGNORE INTO users (id, name, email, password, status)
                VALUES (1, 'Admin', 'admin@karobar.local', '$2y$10$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi', 'active');
            INSERT OR IGNORE INTO model_has_roles (role_id, model_type, model_id)
                VALUES (1, 'App\Models\User', 1);
            """);
    }

    private static string ResolveRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "native-win")) && Directory.Exists(Path.Combine(dir, "resources")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}

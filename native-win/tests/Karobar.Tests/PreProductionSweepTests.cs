using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Karobar.Host;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Karobar.Tests.Wave9And10LiveHttpIntegrationTests;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Karobar.Tests;

public class PreProductionSweepTests
{
    [Fact]
    public void PrintHtml_BaseHrefInjection_And_PrintPreview_RouteExclusion_WorkCleanly()
    {
        var html = "<!doctype html><html><head><title>Receipt</title></head><body><img src=\"/storage/logo.png\"/></body></html>";
        var injected = KarobarDesktopBridge.InjectBaseHrefForPrintHtml(html, "http://127.0.0.1:18960");
        injected.Should().Contain("<head><base href=\"http://127.0.0.1:18960/\"><title>Receipt</title>");

        // Must not duplicate an existing <base> tag
        var doubleInjected = KarobarDesktopBridge.InjectBaseHrefForPrintHtml(injected, "http://127.0.0.1:18960");
        doubleInjected.Should().Be(injected);

        // Print preview routes must NOT be silently intercepted as direct paper prints
        WebView2WindowManager.IsLocalPrintRouteUrl("/print/thermal-preview/42").Should().BeFalse();
        WebView2WindowManager.IsLocalPrintRouteUrl("/print/a4-preview/42").Should().BeFalse();
        WebView2WindowManager.IsLocalPrintRouteUrl("/print/sale/42?preview=1").Should().BeFalse();
        WebView2WindowManager.IsLocalPrintRouteUrl("/print/sale/42").Should().BeTrue();
        WebView2WindowManager.IsLocalPrintRouteUrl("/customer-ledger/print?customer_id=1").Should().BeTrue();

        var tempDir = Path.Combine(Path.GetTempPath(), $"karobar-preview-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var netCfg = new NetworkConfigManager(tempDir);
            using var winMgr = new WebView2WindowManager(netCfg);
            winMgr.HandleWindowOpenRequest("/print/thermal-preview/42", "_blank").Should().Be("allow-preview");
            winMgr.HandleWindowOpenRequest("/print/sale/42?preview=1", "_blank").Should().Be("allow-preview");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Packaging_BuildScript_And_Installer_Guard_TerminalMode_And_OfflineAsset()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var buildPs1 = File.ReadAllText(Path.Combine(repoRoot, "native-win", "build-native-win.ps1"));
        buildPs1.Should().Contain(@"native-win\src\Karobar.Host\desktop");
        buildPs1.Should().Contain("terminal-offline.html");

        var iss = File.ReadAllText(Path.Combine(repoRoot, "native-win", "installer", "Karobar-Native-Setup.iss"));
        iss.Should().Contain("ShouldSkipLocalServerSetup");
        iss.Should().Contain("IsExistingTerminalInstall");
        iss.Should().Contain("[UninstallRun]");
        iss.Should().Contain("Karobar-App Native LAN Server (TCP 18960)");
    }

    [Fact]
    public async Task CashDrawer_CashRefund_BackupSqlGzRestore_And_RbacHardening_PassLiveVerification()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            var adminToken = await LoginAsync(client, "qa-w910-admin@karobar.local", "AdminPass123!");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

            // 1. Verify CashDrawerService properly deducts cash_refund from expected balance & summary
            var drawerSvc = app.Services.GetRequiredService<CashDrawerService>();
            var existingSession = await drawerSvc.GetCurrentSessionAsync(userId: 1, strictUser: true, terminalId: null);
            if (existingSession != null)
            {
                await drawerSvc.CloseSessionAsync(
                    Convert.ToInt64(existingSession["id"]),
                    actualCount: Convert.ToDecimal(existingSession["closing_balance_expected"] ?? 0m),
                    notes: "Closed before PreProd QA");
            }
            var openSession = await drawerSvc.OpenSessionAsync(userId: 1, openingBalance: 5000m, notes: "PreProd QA", terminalId: null);
            var sessionId = Convert.ToInt64(openSession["id"]);

            await drawerSvc.RecordTransactionAsync(
                session: openSession,
                type: "cash_sale",
                amount: 1000m,
                refType: "sale_invoice",
                refId: 999001,
                notes: "Cash sale QA",
                userId: 1,
                terminalId: null);

            await drawerSvc.RecordTransactionAsync(
                session: openSession,
                type: "cash_refund",
                amount: 250m,
                refType: "sale_return",
                refId: 999002,
                notes: "Cash refund QA",
                userId: 1,
                terminalId: null);

            var expectedAfterRefund = await drawerSvc.RecalculateExpectedBalanceAsync(sessionId);
            expectedAfterRefund.Should().Be(5750m);

            var summary = await drawerSvc.GetSessionSummaryAsync(sessionId);
            Convert.ToDecimal(summary["total_cash_in"]).Should().Be(1000m);
            Convert.ToDecimal(summary["total_cash_out"]).Should().Be(250m);
            Convert.ToDecimal(summary["expected_balance"]).Should().Be(5750m);

            // 2. Verify BackupService .sql.gz backup & atomic restore round-trip
            var backupSvc = app.Services.GetRequiredService<BackupService>();
            var createdBackup = await backupSvc.CreateBackupAsync(
                type: "database",
                userId: 1,
                isAutomated: false,
                customBackupPath: null);
            var backupId = Convert.ToInt64(createdBackup["id"]);
            backupId.Should().BeGreaterThan(0);

            // Mutate a setting after backup, then restore the .sql.gz backup and confirm rollback
            var kdb = app.Services.GetRequiredService<KarobarDb>();
            await kdb.ExecuteAsync(
                "INSERT INTO settings (store_name, created_at, updated_at) SELECT 'QA_MUTATED_BEFORE_RESTORE', datetime('now'), datetime('now') WHERE NOT EXISTS (SELECT 1 FROM settings)");
            await kdb.ExecuteAsync("UPDATE settings SET store_name = 'QA_MUTATED_BEFORE_RESTORE'");

            await backupSvc.RestoreBackupAsync(backupId, "AdminPass123!", 1L);

            var storeNameAfterRestore = await kdb.ExecuteScalarAsync<string?>("SELECT store_name FROM settings LIMIT 1");
            storeNameAfterRestore.Should().NotBe("QA_MUTATED_BEFORE_RESTORE");

            // 3. Verify RBAC hardening: non-admin user cannot sync Admin roles, mutate role permissions,
            //    post invoice drafts without sale-invoice.create, or stop Cube background tasks
            var nonAdminEmail = $"qa-cashier-{Guid.NewGuid():N}@karobar.local";
            var createUserResp = await client.PostAsync("/api/users", JsonBody(new
            {
                name = "QA Restricted Cashier",
                email = nonAdminEmail,
                password = "UserPass123!",
                password_confirmation = "UserPass123!"
            }));
            createUserResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
            long nonAdminId;
            using (var uDoc = JsonDocument.Parse(await createUserResp.Content.ReadAsStringAsync()))
            {
                var uRoot = uDoc.RootElement.TryGetProperty("user", out var uObj) ? uObj : uDoc.RootElement;
                nonAdminId = uRoot.GetProperty("id").GetInt64();
            }

            // Create a draft as admin so we can test unauthorized posting
            var draftResp = await client.PostAsync("/api/invoice-drafts", JsonBody(new
            {
                type = "sale_retail",
                title = "QA Restricted Draft",
                payload = new { items = new[] { new { product_id = 1, quantity = 1, price = 10 } } }
            }));
            draftResp.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Created);
            long draftId;
            using (var dDoc = JsonDocument.Parse(await draftResp.Content.ReadAsStringAsync()))
            {
                var dRoot = dDoc.RootElement.TryGetProperty("draft", out var dObj) ? dObj : dDoc.RootElement;
                draftId = dRoot.GetProperty("id").GetInt64();
            }

            // Log in as restricted non-admin user
            var cashierToken = await LoginAsync(client, nonAdminEmail, "UserPass123!");
            using var cashierClient = new HttpClient { BaseAddress = client.BaseAddress };
            cashierClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);

            // 3a. Cannot self-escalate to Admin via SyncRoles
            var syncRolesResp = await cashierClient.PutAsync($"/api/users/{nonAdminId}/roles", JsonBody(new
            {
                roles = new[] { "Admin" }
            }));
            syncRolesResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // 3b. Cannot overwrite Admin role permissions
            var syncRolePermsResp = await cashierClient.PutAsync("/api/roles/1/permissions", JsonBody(new
            {
                permissions = new[] { "sale-invoice.create" }
            }));
            syncRolePermsResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // 3c. Cannot post a sale invoice draft without sale-invoice.create permission
            var postDraftResp = await cashierClient.PostAsync($"/api/invoice-drafts/{draftId}/post", JsonBody(new { }));
            postDraftResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // 3d. Cannot stop Cube AI background autofill without product.update permission
            var stopBgResp = await cashierClient.PostAsync("/api/cube/bg-task/stop", JsonBody(new { }));
            stopBgResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            await CleanupAsync(app, client, tempRoot, prevRoot);
        }
    }
}

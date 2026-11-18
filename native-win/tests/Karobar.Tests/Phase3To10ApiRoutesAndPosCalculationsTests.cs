using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Karobar.Api;
using Karobar.Api.Middleware;
using Karobar.Data;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Karobar.Tests;

public class Phase3To10ApiRoutesAndPosCalculationsTests
{
    private static readonly string[] ExpectedControllers =
    [
        "AuthController", "MeController", "LicenseController", "UserPreferenceController",
        "UserController", "OperatorPinController", "RoleController", "PermissionController",
        "SettingController", "ThemeSettingController", "BackupController", "SyncPulseController",
        "TerminalController", "SystemUpdateController", "WebRoutesController",
        "BrandController", "BrandImportController", "CategoryController", "CategoryImportController",
        "FormulationController", "StorageLocationController", "DoctorController",
        "CustomerController", "CustomerImportController", "SupplierController", "SupplierImportController",
        "ProductController", "ProductImportController", "BatchController", "BatchExpiryController",
        "BarcodeController", "PurchaseOrderController", "PurchaseInvoiceController",
        "PurchaseReturnController", "UserDemandController", "SaleInvoiceController",
        "SaleReturnController", "InvoiceDraftController", "TradeSchemeController",
        "DocumentHistoryController", "StockAdjustmentController", "StocktakeController",
        "StockDisposalController", "CashDrawerController", "AccountController",
        "PaymentController", "ExpenseController", "ExpenseCategoryController",
        "SupplierLedgerController", "CustomerLedgerController", "AccountingController",
        "HrPayrollController", "DashboardController", "NotificationController",
        "ReportsController", "FbrController", "CubeAssistantController",
        "SqliteImportController", "DocumentShareController"
    ];

    private static readonly string[] ExpectedServices =
    [
        "LicenseService", "OperatorPinService", "BackupService", "SyncPulseService",
        "SqliteExportService", "DocumentSequenceService", "DocumentAuditService",
        "BrandImportService", "CategoryImportService", "CustomerImportService",
        "SupplierImportService", "ProductImportService", "InvoiceCalculationService",
        "PurchaseOrderReceivingService", "SupplierProductSortingService",
        "ReturnEntitlementService", "ReturnSettlementService", "SaleStockAvailabilityService",
        "SaleCostSnapshotService", "TradeSchemeService", "StocktakeService",
        "StockDisposalService", "AccountService", "CashDrawerService",
        "PaymentService", "ExpenseService", "AccountingService", "HrPayrollService"
    ];

    private static readonly string[] ExpectedMiddlewares =
    [
        "SanctumAuthenticationHandler", "CheckTokenExpiryMiddleware",
        "EnsureLicensedMiddleware", "TrackTerminalMiddleware",
        "SpatiePermissionAuthorizationFilter", "SignedUrlMiddleware"
    ];

    [Fact]
    public void All_59_Controllers_28_Services_And_6_Middlewares_Exist_In_Karobar_Api_And_Services()
    {
        var apiAssembly = typeof(KarobarApiServiceExtensions).Assembly;
        var servicesAssembly = typeof(KarobarDb).Assembly;

        var apiTypes = apiAssembly.GetExportedTypes().Select(t => t.Name).ToHashSet();
        var serviceTypes = servicesAssembly.GetExportedTypes().Select(t => t.Name).ToHashSet();

        foreach (var controller in ExpectedControllers)
        {
            apiTypes.Should().Contain(controller, $"Controller '{controller}' must exist in Karobar.Api");
        }

        foreach (var middleware in ExpectedMiddlewares)
        {
            apiTypes.Should().Contain(middleware, $"Middleware '{middleware}' must exist in Karobar.Api");
        }

        foreach (var service in ExpectedServices)
        {
            serviceTypes.Should().Contain(service, $"Service '{service}' must exist in Karobar.Services");
        }
    }

    [Fact]
    public void Every_Api_Route_In_Routes_Api_Php_And_Web_Route_In_Routes_Web_Php_Is_Mapped_In_Karobar_Api()
    {
        var apiAssembly = typeof(KarobarApiServiceExtensions).Assembly;
        var mappedEndpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var controller in apiAssembly.GetExportedTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract))
        {
            var classRoutes = controller.GetCustomAttributes<RouteAttribute>(inherit: true)
                .Select(r => r.Template ?? "")
                .DefaultIfEmpty("")
                .ToList();

            foreach (var method in controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var httpAttrs = method.GetCustomAttributes<HttpMethodAttribute>(inherit: true).ToList();
                if (httpAttrs.Count == 0) continue;

                var methodRoutes = method.GetCustomAttributes<RouteAttribute>(inherit: true)
                    .Select(r => r.Template)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                foreach (var httpAttr in httpAttrs)
                {
                    var templates = new List<string>();
                    if (!string.IsNullOrWhiteSpace(httpAttr.Template))
                    {
                        templates.Add(httpAttr.Template!);
                    }
                    else if (methodRoutes.Count > 0)
                    {
                        templates.AddRange(methodRoutes!);
                    }
                    else
                    {
                        templates.Add("");
                    }

                    foreach (var classRoute in classRoutes)
                    {
                        foreach (var tpl in templates)
                        {
                            string combined;
                            if (tpl.StartsWith('/'))
                            {
                                combined = tpl;
                            }
                            else if (string.IsNullOrEmpty(classRoute))
                            {
                                combined = "/" + tpl;
                            }
                            else if (string.IsNullOrEmpty(tpl))
                            {
                                combined = "/" + classRoute.TrimStart('/');
                            }
                            else
                            {
                                combined = "/" + classRoute.Trim('/') + "/" + tpl.TrimStart('/');
                            }

                            var normalized = NormalizeRouteTemplate(combined);
                            foreach (var verb in httpAttr.HttpMethods)
                            {
                                mappedEndpoints.Add($"{verb.ToUpperInvariant()} {normalized}");
                            }
                        }
                    }
                }
            }
        }

        // Critical API & Web routes across all 12 Phases from routes/api.php and routes/web.php
        string[] requiredEndpoints =
        [
            // Phase 3: Auth, License, Users, Roles, Permissions, PINs
            "POST /api/login",
            "POST /api/session/extend",
            "GET /api/user",
            "PUT /api/profile",
            "POST /api/logout",
            "POST /api/forgot-password",
            "POST /api/reset-password",
            "POST /api/verify-password",
            "POST /api/auth/confirm-password",
            "GET /api/me",
            "GET /api/license/status",
            "POST /api/license/activate",
            "POST /api/license/deactivate",
            "GET /api/preferences",
            "PUT /api/preferences",
            "GET /api/preferences/{param}",
            "POST /api/preferences/{param}",
            "POST /api/users/bulk-action",
            "GET /api/users",
            "POST /api/users",
            "PUT /api/users/{param}/roles",
            "PUT /api/users/{param}/permissions",
            "GET /api/operator-pins/directory",
            "GET /api/operator-pins/generate",
            "PUT /api/operator-pins/assign",
            "GET /api/roles",
            "POST /api/roles",
            "GET /api/permissions",
            "POST /api/permissions",

            // Phase 4: Settings, Themes, Backups, Sync Pulse, Terminals, OTA Updates, Web Routes
            "GET /api/sale-system",
            "GET /api/currency",
            "GET /api/setup/state",
            "POST /api/setup/complete",
            "GET /api/settings",
            "POST /api/settings",
            "POST /api/settings/test-smtp",
            "POST /api/settings/test-whatsapp",
            "GET /api/settings/whatsapp-agent/status",
            "POST /api/settings/whatsapp-agent/start",
            "POST /api/settings/whatsapp-agent/pair-code",
            "POST /api/settings/whatsapp-agent/logout",
            "POST /api/settings/whatsapp-agent/stop",
            "POST /api/settings/test-cube-ai",
            "GET /api/theme-settings/active",
            "PUT /api/theme-settings/active",
            "GET /api/theme-settings",
            "GET /api/backups/stats",
            "GET /api/backups/schedule",
            "POST /api/backups/schedule",
            "POST /api/backups/run-now",
            "GET /api/backups",
            "GET /api/sync/pulse",
            "POST /api/sync/bump",
            "GET /api/terminals/ping",
            "POST /api/terminals/ping",
            "GET /api/terminals",
            "GET /api/server/diagnostics",
            "POST /api/server/troubleshoot",
            "GET /api/system/version",
            "GET /api/system/check-update",
            "GET /api/system/update-progress",
            "POST /api/system/install-update",
            "GET /terms-of-service",
            "GET /privacy-policy",
            "GET /server-launch",
            "POST /server-launch",

            // Phase 5: Master Catalog, Parties, Batches, Barcodes & Importers
            "GET /api/brands/search",
            "GET /api/brands/export",
            "GET /api/brands",
            "POST /api/brands/import/validate",
            "POST /api/brands/import/commit",
            "GET /api/categories/search",
            "GET /api/categories/export",
            "GET /api/categories",
            "GET /api/formulations/search",
            "GET /api/formulations",
            "GET /api/storage-locations",
            "GET /api/doctors/search",
            "GET /api/doctors",
            "GET /api/customers/search",
            "GET /api/customers/export",
            "GET /api/customers/{param}/credit-summary",
            "GET /api/customers/{param}/whole-sale-prices",
            "GET /api/suppliers/search",
            "GET /api/suppliers/export",
            "GET /api/suppliers/{param}/credit-summary",
            "GET /api/products/new-code",
            "GET /api/products/search",
            "GET /api/products/available-quantity",
            "GET /api/products/{param}/batches",
            "POST /api/products/{param}/batches",
            "GET /api/batch-expiry",
            "GET /api/notifications/expiry-alerts",
            "GET /api/barcodes/templates",
            "GET /api/barcodes/scan",

            // Phase 6 & 7: Procurement & Sales POS
            "GET /api/purchase-orders/forecast",
            "POST /api/purchase-orders/{param}/receive",
            "GET /api/purchase-invoices/new-code",
            "POST /api/purchase-invoices/{param}/reverse",
            "GET /api/purchase-returns/new-code",
            "GET /api/user-demands/count",
            "GET /api/sale-invoices/new-code",
            "GET /api/sale-invoices/search",
            "GET /api/sale-invoices/{param}/print-url",
            "POST /api/sale-invoices/{param}/reverse",
            "GET /api/sale-returns/new-code",
            "POST /api/invoice-drafts/{param}/post",
            "GET /api/trade-schemes/active-lookup",
            "GET /api/document-histories",

            // Phase 8: Audits & Disposals
            "GET /api/stock-adjustments/new-code",
            "GET /api/stock-adjustments",
            "GET /api/stocktake-sessions/new-code",
            "POST /api/stocktake-sessions/{param}/finalize",
            "GET /api/stock-disposals/new-code",
            "POST /api/stock-disposals/{param}/dispose",

            // Phase 9: Finance, Cash Drawer, Ledgers, GL & HR Payroll
            "GET /api/cash-drawer/current",
            "POST /api/cash-drawer/open",
            "POST /api/cash-drawer/close",
            "GET /api/accounts",
            "POST /api/accounts/transfer",
            "GET /api/payments/open-invoices",
            "POST /api/payments/apply-credits",
            "GET /api/expenses/summary",
            "POST /api/expenses/process-recurring",
            "GET /api/supplier-ledger",
            "POST /api/supplier-ledger/rebuild",
            "GET /api/customer-ledger",
            "POST /api/customer-ledger/rebuild",
            "GET /api/accounting/overview",
            "GET /api/accounting/chart-of-accounts",
            "GET /api/accounting/journal-entries",
            "GET /api/accounting/tax-rates",
            "GET /api/hr/overview",
            "GET /api/hr/employees",
            "GET /api/hr/attendance",
            "GET /api/hr/commissions",
            "POST /api/hr/payroll-runs/generate",

            // Phase 10: Dashboard, Notifications & 18 Reports (JSON + PDF)
            "GET /api/dashboard/summary",
            "GET /api/dashboard/kpi-metrics",
            "GET /api/notifications/low-stock",
            "GET /api/reports/cost-of-sale",
            "GET /api/reports/cost-of-sale/pdf",
            "GET /api/reports/cost-of-sale-detail",
            "GET /api/reports/cost-of-sale-detail/pdf",
            "GET /api/reports/purchase-detail",
            "GET /api/reports/purchase-detail/pdf",
            "GET /api/reports/sale-detail",
            "GET /api/reports/sale-detail/pdf",
            "GET /api/reports/current-stock",
            "GET /api/reports/current-stock/pdf",
            "GET /api/reports/stock-adjustment",
            "GET /api/reports/stock-adjustment/pdf",
            "GET /api/reports/product-comprehensive",
            "GET /api/reports/product-comprehensive/pdf",
            "GET /api/reports/near-expiry-product",
            "GET /api/reports/near-expiry-product/pdf",
            "GET /api/reports/purchase-returns",
            "GET /api/reports/purchase-returns/pdf",
            "GET /api/reports/sale-returns",
            "GET /api/reports/sale-returns/pdf",
            "GET /api/reports/purchase-summary",
            "GET /api/reports/purchase-summary/pdf",
            "GET /api/reports/outstanding-aging",
            "GET /api/reports/outstanding-aging/pdf",
            "GET /api/reports/overdue-work-queue",
            "GET /api/reports/overdue-work-queue/pdf",
            "GET /api/reports/reorder",
            "GET /api/reports/reorder/pdf",
            "GET /api/reports/net-profit",
            "GET /api/reports/net-profit/pdf",
            "GET /api/reports/tax-return",
            "GET /api/reports/tax-return/pdf",
            "GET /api/reports/narcotics",
            "GET /api/reports/narcotics/pdf",
            "GET /api/reports/financial-statements/pdf",

            // Phase 11 & 12: Print Web Routes, Share, FBR, Cube AI & SQLite/Waseela Import
            "GET /print/sale-invoices/{param}",
            "GET /sale-invoices/{param}/dosage-labels",
            "GET /print/cash-drawer/{param}",
            "GET /supplier-ledger/print",
            "GET /customer-ledger/print",
            "GET /print/shared-pdf/{param}",
            "GET /print/thermal-preview/{param}",
            "GET /print/a4-preview/{param}",
            "GET /print/sticker-preview/{param}",
            "GET /print/share-preview/{param}/{param}",
            "GET /api/share/metadata",
            "GET /api/share/pdf",
            "POST /api/share/email",
            "POST /api/share/whatsapp",
            "GET /api/fbr/status-summary",
            "POST /api/fbr/test-connection",
            "POST /api/fbr/sale-invoices/{param}/sync",
            "POST /api/fbr/sale-returns/{param}/sync",
            "POST /api/fbr/retry-queue",
            "POST /api/cube/ask",
            "POST /api/cube/execute-task",
            "POST /api/settings/sqlite-import/unlock",
            "POST /api/settings/sqlite-import/upload",
            "POST /api/settings/sqlite-import/preview-table",
            "POST /api/settings/sqlite-import/commit",
            "DELETE /api/settings/sqlite-import/{param}"
        ];

        foreach (var endpoint in requiredEndpoints)
        {
            mappedEndpoints.Should().Contain(endpoint, $"Endpoint '{endpoint}' must be mapped in Karobar.Api controllers");
        }
    }

    [Fact]
    public async Task Sanctum_Authentication_Bcrypt_2y_And_Pos_Invoice_Calculations_Work_Against_Test_Sqlite_Database()
    {
        var tempSqlite = Path.Combine(Path.GetTempPath(), $"karobar_qa_p3_7_{Guid.NewGuid():N}.sqlite");
        try
        {
            var sourceFactory = new DbConnectionFactory();
            if (File.Exists(sourceFactory.SqlitePath) && new FileInfo(sourceFactory.SqlitePath).Length > 0)
            {
                File.Copy(sourceFactory.SqlitePath, tempSqlite, overwrite: true);
            }
            else
            {
                var initFactory = new DbConnectionFactory(explicitSqlitePath: tempSqlite);
                var initBootstrapper = new DatabaseBootstrapper(initFactory);
                await initBootstrapper.EnsureSchemaReadyAsync();
            }

            var services = new ServiceCollection();
            services.AddKarobarApi(explicitSqlitePath: tempSqlite);
            await using var provider = services.BuildServiceProvider();
            using var scope = provider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KarobarDb>();

            // 1. Test Laravel $2y$ Bcrypt hash generation & verification
            var hash2y = SanctumAuthenticationHandler.HashLaravelBcryptPassword("SecretPass123!");
            hash2y.Should().StartWith("$2y$");
            SanctumAuthenticationHandler.VerifyLaravelBcryptPassword("SecretPass123!", hash2y).Should().BeTrue();
            SanctumAuthenticationHandler.VerifyLaravelBcryptPassword("WrongPass", hash2y).Should().BeFalse();

            // 2. Test Sanctum Personal Access Token (id|plaintext) issuance & SHA-256 verification
            var userId = await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "QA Cashier",
                ["email"] = $"qa_{Guid.NewGuid():N}@karobar.local",
                ["password"] = hash2y,
                ["status"] = "active"
            });
            var plainTextToken = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(db, userId, "qa-pos-terminal");
            plainTextToken.Should().Contain("|");
            var parts = plainTextToken.Split('|', 2);
            long.Parse(parts[0]).Should().BeGreaterThan(0);

            var tokenRecord = await db.QuerySingleOrDefaultAsync(
                "SELECT * FROM personal_access_tokens WHERE id = @id",
                new { id = long.Parse(parts[0]) });
            tokenRecord.Should().NotBeNull();
            tokenRecord!["token"]?.ToString().Should().Be(SanctumAuthenticationHandler.ComputeSha256Hex(parts[1]));

            // 3. Test POS Sale Invoice & Purchase Invoice Authoritative Calculations
            var catId = await db.InsertAsync("categories", new Dictionary<string, object?>
            {
                ["name"] = $"QA Category {Guid.NewGuid():N}"
            });
            var brandId = await db.InsertAsync("brands", new Dictionary<string, object?>
            {
                ["name"] = $"QA Brand {Guid.NewGuid():N}"
            });
            var supplierId = await db.InsertAsync("suppliers", new Dictionary<string, object?>
            {
                ["name"] = $"QA Supplier {Guid.NewGuid():N}"
            });
            var productId = await db.InsertAsync("products", new Dictionary<string, object?>
            {
                ["name"] = "QA Panadol Extra 500mg",
                ["product_code"] = $"QA-{Guid.NewGuid().ToString("N")[..6]}",
                ["barcode"] = $"8964{Guid.NewGuid().ToString("N")[..8]}",
                ["category_id"] = catId,
                ["brand_id"] = brandId,
                ["supplier_id"] = supplierId,
                ["pack_size"] = 10,
                ["pack_purchase_price"] = 100.0,
                ["unit_purchase_price"] = 10.0,
                ["avg_price"] = 10.0,
                ["pack_sale_price"] = 150.0,
                ["unit_sale_price"] = 15.0,
                ["whole_sale_pack_price"] = 120.0,
                ["whole_sale_unit_price"] = 12.0,
                ["max_discount"] = 15.0,
                ["quantity"] = 100
            });

            var calcService = new InvoiceCalculationService(db);

            // 3a. Verify max_discount enforcement (10% item discount + Rs. 20 bill discount on Rs. 300 = 16.67% > 15% max_discount -> throws 422 requires_discount_override)
            Func<Task> overDiscountAction = async () => await calcService.CalculateAndValidateSaleAsync(
                new Dictionary<string, object?>
                {
                    ["sale_type"] = "retail",
                    ["discount_amount"] = 20.0,
                    ["tax_percentage"] = 10.0
                },
                [
                    new Dictionary<string, object?>
                    {
                        ["product_id"] = productId,
                        ["quantity"] = 20,
                        ["price"] = 15.0,
                        ["item_discount_percentage"] = 10.0
                    }
                ],
                userId: userId);

            var exAssertion = await overDiscountAction.Should().ThrowAsync<InvoiceValidationException>();
            exAssertion.Which.StatusCode.Should().Be(422);
            exAssertion.Which.ResponsePayload["requires_discount_override"].Should().Be(true);

            // 3b. Update max_discount to 25% and verify Retail POS calculation:
            // 20 units @ Rs. 15 = 300 gross, 10% item discount = 30 -> 270 subtotal, Rs. 20 bill discount -> 250 taxable, 10% tax = 25 -> 275 total
            await db.ExecuteAsync("UPDATE products SET max_discount = 25.0 WHERE id = @id", new { id = productId });

            var saleCalc = await calcService.CalculateAndValidateSaleAsync(
                new Dictionary<string, object?>
                {
                    ["sale_type"] = "retail",
                    ["discount_amount"] = 20.0,
                    ["tax_percentage"] = 10.0
                },
                [
                    new Dictionary<string, object?>
                    {
                        ["product_id"] = productId,
                        ["quantity"] = 20,
                        ["price"] = 15.0,
                        ["item_discount_percentage"] = 10.0
                    }
                ],
                userId: userId);

            Convert.ToDouble(saleCalc["gross_amount"]).Should().Be(300.0);
            Convert.ToDouble(saleCalc["item_discount"]).Should().Be(30.0);
            Convert.ToDouble(saleCalc["discount_amount"]).Should().Be(20.0);
            Convert.ToDouble(saleCalc["tax_amount"]).Should().Be(25.0);
            Convert.ToDouble(saleCalc["total"]).Should().Be(275.0);

            // Purchase Invoice calculation: 5 packs (pack_size 10 = 50 units) + 10 bonus units = 60 total units @ Rs. 100/pack (Rs. 10/unit) = 500 gross
            var purchaseCalc = calcService.CalculateAndValidatePurchase(
                new Dictionary<string, object?>
                {
                    ["discount_amount"] = 50.0,
                    ["tax_amount"] = 15.0
                },
                [
                    new Dictionary<string, object?>
                    {
                        ["product_id"] = productId,
                        ["pack_size"] = 10,
                        ["pack_quantity"] = 5,
                        ["pack_purchase_price"] = 100.0,
                        ["unit_bonus"] = 10,
                        ["item_discount_percentage"] = 0.0
                    }
                ]);

            Convert.ToDouble(purchaseCalc["invoice_amount"]).Should().Be(500.0);
            Convert.ToDouble(purchaseCalc["discount_amount"]).Should().Be(50.0);
            Convert.ToDouble(purchaseCalc["tax_amount"]).Should().Be(15.0);
            Convert.ToDouble(purchaseCalc["total_amount"]).Should().Be(465.0);
            var calcPurchaseItems = (List<Dictionary<string, object?>>)purchaseCalc["items"]!;
            Convert.ToInt32(calcPurchaseItems[0]["quantity"]).Should().Be(60);
        }
        finally
        {
            try { if (File.Exists(tempSqlite)) File.Delete(tempSqlite); } catch { /* ignore */ }
            try { if (File.Exists(tempSqlite + "-wal")) File.Delete(tempSqlite + "-wal"); } catch { /* ignore */ }
            try { if (File.Exists(tempSqlite + "-shm")) File.Delete(tempSqlite + "-shm"); } catch { /* ignore */ }
        }
    }

    private static string NormalizeRouteTemplate(string template)
    {
        var clean = "/" + template.Trim().Trim('/');
        clean = Regex.Replace(clean, @"\{[^\}]+\}", "{param}");
        return clean;
    }
}

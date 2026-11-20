using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Karobar.Api.Controllers;
using Karobar.Background;
using Karobar.Data;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Karobar.Tests;

/// <summary>
/// Comprehensive regression suite verifying 100% parity between Cash Drawer Sessions
/// (stored closing_balance_expected, summed cash_drawer_transactions, History SQL, and ReconciliationCheck)
/// and the Financial Account ("Cash in Hand" / default cash drawer account) across all edge cases.
/// </summary>
public class CashDrawerAccountParityTests
{
    private static TController AttachAdmin<TController>(
        TController ctrl,
        long userId = 1,
        string? jsonBody = null,
        long? terminalId = null) where TController : ControllerBase
    {
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, "Admin")
        ], "Sanctum"));
        if (terminalId.HasValue)
        {
            ctx.Items["KarobarTerminal"] = new Dictionary<string, object?>
            {
                ["id"] = terminalId.Value,
                ["name"] = $"Counter {terminalId.Value}"
            };
        }
        if (jsonBody != null)
        {
            ctx.Request.ContentType = "application/json";
            ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(jsonBody));
        }
        ctrl.ControllerContext = new ControllerContext { HttpContext = ctx };
        return ctrl;
    }

    private static async Task<(KarobarDb Db, string DbPath, long CashAccId, long BankAccId)> SetupFreshAsync(bool createBank = true)
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"karobar_parity_{Guid.NewGuid():N}.sqlite");
        var factory = new DbConnectionFactory(explicitSqlitePath: dbPath);
        await new DatabaseBootstrapper(factory).EnsureSchemaReadyAsync();
        KarobarDb.FlushSchemaCache();
        var db = new KarobarDb(explicitSqlitePath: dbPath);

        await using (var conn = await db.OpenConnectionAsync())
        {
            await conn.ExecuteAsync("""
                INSERT OR IGNORE INTO roles (id, name, guard_name) VALUES (1, 'Admin', 'sanctum');
                INSERT OR IGNORE INTO users (id, name, email, password, status) VALUES (1, 'Admin', 'admin@test.local', '$2y$10$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi', 'active');
                INSERT OR IGNORE INTO users (id, name, email, password, status) VALUES (2, 'Cashier2', 'c2@test.local', '$2y$10$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi', 'active');
                INSERT OR IGNORE INTO model_has_roles (role_id, model_type, model_id) VALUES (1, 'App\Models\User', 1);
                INSERT OR IGNORE INTO model_has_roles (role_id, model_type, model_id) VALUES (1, 'App\Models\User', 2);
                INSERT INTO categories (id, name) VALUES (1, 'General');
                INSERT INTO brands (id, name) VALUES (1, 'BrandA');
                INSERT INTO suppliers (id, name) VALUES (1, 'SupplierA');
                INSERT INTO customers (id, name) VALUES (1, 'Walk-In Customer');
                INSERT INTO customers (id, name) VALUES (2, 'Credit Customer');
                INSERT INTO products (id, product_code, name, category_id, brand_id, supplier_id, pack_size, quantity, pack_purchase_price, unit_purchase_price, avg_price, pack_sale_price, unit_sale_price, barcode, narcotic)
                    VALUES (1, 'P-1', 'Panadol', 1, 1, 1, 10, 500, 500, 50, 50, 1000, 100, '123456', 'no');
                INSERT INTO expense_categories (id, name, is_active) VALUES (1, 'Utilities', 1);
                INSERT OR IGNORE INTO terminals (id, terminal_uid, name, mode, is_authorized) VALUES (1, 'term-1', 'Counter 1', 'server', 1);
                INSERT OR IGNORE INTO terminals (id, terminal_uid, name, mode, is_authorized) VALUES (2, 'term-2', 'Counter 2', 'terminal', 1);
                """);
        }

        var acctSvc = new AccountService(db);
        var cashAcc = (await acctSvc.SyncCashDrawerBalanceAsync())!;
        var cashAccId = AccountService.ToLong(cashAcc["id"]);
        long bankAccId = 0;
        if (createBank)
        {
            bankAccId = await db.InsertAsync("accounts", new Dictionary<string, object?>
            {
                ["name"] = "Meezan Bank",
                ["type"] = "bank",
                ["code"] = "meezan",
                ["opening_balance"] = 0m,
                ["current_balance"] = 0m,
                ["is_active"] = true,
                ["is_default"] = true
            });
        }
        return (db, dbPath, cashAccId, bankAccId);
    }

    private static void Cleanup(string dbPath)
    {
        try { SqliteConnection.ClearAllPools(); } catch { }
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            try { if (File.Exists(dbPath + ext)) File.Delete(dbPath + ext); } catch { }
        }
    }

    private static long ExtractId(IActionResult res)
    {
        if (res is ObjectResult or && or.Value is IDictionary<string, object?> dict && dict.TryGetValue("id", out var idObj))
            return AccountService.ToLong(idObj);
        if (res is ObjectResult or2 && or2.Value != null)
        {
            var json = JsonSerializer.Serialize(or2.Value);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("id", out var idP) && idP.TryGetInt64(out var idv)) return idv;
        }
        throw new InvalidOperationException($"Could not extract id from {res.GetType().Name}");
    }

    private static async Task AssertParityAsync(KarobarDb db, long cashAccId, long sessionId, decimal expectedBalance, string context)
    {
        var accBal = Math.Round(await db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashAccId }), 2);
        var storedExp = Math.Round(await db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId }), 2);
        var opening = await db.ExecuteScalarAsync<decimal>("SELECT opening_balance FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId });
        var dIn = await db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(amount),0) FROM cash_drawer_transactions WHERE session_id = @id AND type IN ('cash_sale','customer_payment','purchase_return','cash_in')", new { id = sessionId });
        var dOut = await db.ExecuteScalarAsync<decimal>("SELECT COALESCE(SUM(amount),0) FROM cash_drawer_transactions WHERE session_id = @id AND type IN ('cash_purchase','cash_return','cash_refund','supplier_payment','expense','cash_out')", new { id = sessionId });
        var calcExp = Math.Round(opening + dIn - dOut, 2);

        accBal.Should().Be(expectedBalance, $"[{context}] Cash in Hand account balance must match expected truth");
        storedExp.Should().Be(expectedBalance, $"[{context}] Stored closing_balance_expected must stay fresh and match expected truth");
        calcExp.Should().Be(expectedBalance, $"[{context}] Summed cash_drawer_transactions must match expected truth");
        accBal.Should().Be(calcExp, $"[{context}] Cash in Hand and Cash Drawer must have 0.00 variance");
    }

    [Fact]
    public async Task S1_And_S2_DeleteSaleInvoice_SameShiftAndPreShift_MaintainsExactParity()
    {
        var (db, p, cashId, _) = await SetupFreshAsync();
        try
        {
            var drawer = new CashDrawerService(db);
            var s1 = await drawer.OpenSessionAsync(1, 1000m);
            var s1Id = AccountService.ToLong(s1["id"]);

            var saleJson = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash",
                date = "2026-11-20", total_receive = 500,
                items = new[] { new { product_id = 1, quantity = 5, price = 100, sub_total = 500 } }
            });

            // S1: Delete same-shift cash sale
            var inv1 = ExtractId(await AttachAdmin(new SaleInvoiceController(db), 1, saleJson).Store());
            await AttachAdmin(new SaleInvoiceController(db), 1, "{}").Destroy(inv1, "refund");
            await AssertParityAsync(db, cashId, s1Id, 1000m, "S1 same-shift sale delete");

            // S2: Create in shift #1, close shift #1, open shift #2, delete pre-shift cash sale
            var inv2 = ExtractId(await AttachAdmin(new SaleInvoiceController(db), 1, saleJson).Store());
            await drawer.CloseSessionAsync(s1Id, 1500m);

            var s2 = await drawer.OpenSessionAsync(1, 1500m);
            var s2Id = AccountService.ToLong(s2["id"]);
            await AttachAdmin(new SaleInvoiceController(db), 1, "{}").Destroy(inv2, "refund");
            await AssertParityAsync(db, cashId, s2Id, 1000m, "S2 pre-shift sale delete");
        }
        finally { Cleanup(p); }
    }

    [Fact]
    public async Task S3_And_S4_EditSaleInvoice_PaymentSwitchAndPreShiftHandling_MaintainsExactParity()
    {
        var (db, p, cashId, bankId) = await SetupFreshAsync();
        try
        {
            var drawer = new CashDrawerService(db);
            var s1 = await drawer.OpenSessionAsync(1, 1000m);
            var s1Id = AccountService.ToLong(s1["id"]);

            // S3a & S3b: Edit Cash Sale -> Bank Transfer (both explicit and omitted account_id)
            var saleJson = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash",
                date = "2026-11-20", total_receive = 500,
                items = new[] { new { product_id = 1, quantity = 5, price = 100, sub_total = 500 } }
            });
            var invA = ExtractId(await AttachAdmin(new SaleInvoiceController(db), 1, saleJson).Store());
            var editExplicit = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "bank_transfer", account_id = bankId,
                date = "2026-11-20", total_receive = 500,
                items = new[] { new { product_id = 1, quantity = 5, price = 100, sub_total = 500 } }
            });
            await AttachAdmin(new SaleInvoiceController(db), 1, editExplicit).Update(invA);
            await AssertParityAsync(db, cashId, s1Id, 1000m, "S3a cash->bank explicit account_id");

            var invB = ExtractId(await AttachAdmin(new SaleInvoiceController(db), 1, saleJson).Store());
            var editOmitted = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "bank_transfer",
                date = "2026-11-20", total_receive = 500,
                items = new[] { new { product_id = 1, quantity = 5, price = 100, sub_total = 500 } }
            });
            await AttachAdmin(new SaleInvoiceController(db), 1, editOmitted).Update(invB);
            await AssertParityAsync(db, cashId, s1Id, 1000m, "S3b cash->bank omitted account_id");

            // S4a: Pre-shift cash sale edited with update_drawer_if_pre_shift=false -> neither drawer nor Cash in Hand mutated
            var invPre = ExtractId(await AttachAdmin(new SaleInvoiceController(db), 1, saleJson).Store());
            await drawer.CloseSessionAsync(s1Id, 1500m);

            var s2 = await drawer.OpenSessionAsync(1, 1500m);
            var s2Id = AccountService.ToLong(s2["id"]);
            var editPreSkip = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash",
                date = "2026-11-20", total_receive = 700, update_drawer_if_pre_shift = false,
                items = new[] { new { product_id = 1, quantity = 7, price = 100, sub_total = 700 } }
            });
            await AttachAdmin(new SaleInvoiceController(db), 1, editPreSkip).Update(invPre);
            await AssertParityAsync(db, cashId, s2Id, 1500m, "S4a pre-shift edit with update_drawer_if_pre_shift=false");
            await drawer.CloseSessionAsync(s2Id, 1500m);

            // S4b: Sale created while drawer CLOSED, then drawer opened, then edited -> only delta (+100) hits drawer
            var invClosed = ExtractId(await AttachAdmin(new SaleInvoiceController(db), 1, saleJson).Store());
            await db.ExecuteAsync("UPDATE sale_invoices SET created_at = '2026-11-20 08:00:00' WHERE id = @id", new { id = invClosed });

            var s3 = await drawer.OpenSessionAsync(1, 2000m);
            var s3Id = AccountService.ToLong(s3["id"]);
            await db.ExecuteAsync("UPDATE cash_drawer_sessions SET opened_at = '2026-11-20 09:00:00' WHERE id = @id", new { id = s3Id });

            var editPreDelta = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash",
                date = "2026-11-20", total_receive = 600, update_drawer_if_pre_shift = true,
                items = new[] { new { product_id = 1, quantity = 6, price = 100, sub_total = 600 } }
            });
            await AttachAdmin(new SaleInvoiceController(db), 1, editPreDelta).Update(invClosed);
            await AssertParityAsync(db, cashId, s3Id, 2100m, "S4b created-before-open edited by +100");
        }
        finally { Cleanup(p); }
    }

    [Fact]
    public async Task S5_And_S6_EditSplitPaymentAndAllocatedCreditSale_MaintainsExactParity()
    {
        var (db, p, cashId, bankId) = await SetupFreshAsync();
        try
        {
            var drawer = new CashDrawerService(db);
            var s = await drawer.OpenSessionAsync(1, 1000m);
            var sid = AccountService.ToLong(s["id"]);

            // S5: Split-payment sale (600 cash + 400 bank), then edited
            var splitSale = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash", date = "2026-11-20",
                split_payments = new object[]
                {
                    new { payment_method = "cash", account_id = cashId, amount = 600 },
                    new { payment_method = "bank_transfer", account_id = bankId, amount = 400 }
                },
                items = new[] { new { product_id = 1, quantity = 10, price = 100, sub_total = 1000 } }
            });
            var invSplit = ExtractId(await AttachAdmin(new SaleInvoiceController(db), 1, splitSale).Store());
            var splitEdit = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash", date = "2026-11-20", remarks = "edited",
                split_payments = new object[]
                {
                    new { payment_method = "cash", account_id = cashId, amount = 600 },
                    new { payment_method = "bank_transfer", account_id = bankId, amount = 400 }
                },
                items = new[] { new { product_id = 1, quantity = 10, price = 100, sub_total = 1000 } }
            });
            await AttachAdmin(new SaleInvoiceController(db), 1, splitEdit).Update(invSplit);
            await AssertParityAsync(db, cashId, sid, 1600m, "S5 split payment edit");
            var bankBal = await db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = bankId });
            bankBal.Should().Be(400m);

            // S6: Credit sale (300 direct + 700 allocated customer payment), then edited
            var creditSale = JsonSerializer.Serialize(new
            {
                customer_id = 2, invoice_type = "credit", sale_type = "wholesale", payment_method = "cash",
                date = "2026-11-20", total_receive = 300,
                items = new[] { new { product_id = 1, quantity = 10, price = 100, sub_total = 1000 } }
            });
            var invCredit = ExtractId(await AttachAdmin(new SaleInvoiceController(db), 1, creditSale).Store());
            await new PaymentService(db).RecordCustomerPaymentAsync(2, new Dictionary<string, object?>
            {
                ["amount"] = 700m,
                ["payment_method"] = "cash",
                ["account_id"] = cashId,
                ["allocations"] = new List<Dictionary<string, object?>> { new() { ["invoice_id"] = invCredit, ["amount"] = 700m } }
            }, 1);

            var creditEdit = JsonSerializer.Serialize(new
            {
                customer_id = 2, invoice_type = "credit", sale_type = "wholesale", payment_method = "cash",
                date = "2026-11-20", total_receive = 300, remarks = "updated",
                items = new[] { new { product_id = 1, quantity = 10, price = 100, sub_total = 1000 } }
            });
            await AttachAdmin(new SaleInvoiceController(db), 1, creditEdit).Update(invCredit);
            await AssertParityAsync(db, cashId, sid, 2600m, "S6 credit sale with allocated payment edit");
        }
        finally { Cleanup(p); }
    }

    [Fact]
    public async Task S7_PurchaseInvoice_EditMethodSwitchAndPreShiftDelete_MaintainsExactParity()
    {
        var (db, p, cashId, bankId) = await SetupFreshAsync();
        try
        {
            var drawer = new CashDrawerService(db);
            var s1 = await drawer.OpenSessionAsync(1, 5000m);
            var s1Id = AccountService.ToLong(s1["id"]);

            var purch1 = JsonSerializer.Serialize(new
            {
                supplier_id = 1, invoice_number = "SUP-1", invoice_type = "debit", payment_method = "cash",
                posted_date = "2026-11-20", total_paid = 1000,
                items = new[] { new { product_id = 1, pack_quantity = 2, pack_size = 10, unit_quantity = 20, quantity = 20, pack_purchase_price = 500, unit_purchase_price = 50, sub_total = 1000 } }
            });
            var pi1 = ExtractId(await AttachAdmin(new PurchaseInvoiceController(db), 1, purch1).Store());

            // Switch to bank_transfer WITHOUT passing account_id
            var editToBank = JsonSerializer.Serialize(new
            {
                supplier_id = 1, invoice_number = "SUP-1", invoice_type = "debit", payment_method = "bank_transfer",
                posted_date = "2026-11-20", total_paid = 1000,
                items = new[] { new { product_id = 1, pack_quantity = 2, pack_size = 10, unit_quantity = 20, quantity = 20, pack_purchase_price = 500, unit_purchase_price = 50, sub_total = 1000 } }
            });
            await AttachAdmin(new PurchaseInvoiceController(db), 1, editToBank).Update(pi1);
            await AssertParityAsync(db, cashId, s1Id, 5000m, "S7a purchase cash->bank");

            // Create second cash purchase and delete in shift #2
            var purch2 = JsonSerializer.Serialize(new
            {
                supplier_id = 1, invoice_number = "SUP-2", invoice_type = "debit", payment_method = "cash",
                posted_date = "2026-11-20", total_paid = 1000,
                items = new[] { new { product_id = 1, pack_quantity = 2, pack_size = 10, unit_quantity = 20, quantity = 20, pack_purchase_price = 500, unit_purchase_price = 50, sub_total = 1000 } }
            });
            var pi2 = ExtractId(await AttachAdmin(new PurchaseInvoiceController(db), 1, purch2).Store());
            await drawer.CloseSessionAsync(s1Id, 4000m);

            var s2 = await drawer.OpenSessionAsync(1, 4000m);
            var s2Id = AccountService.ToLong(s2["id"]);
            await AttachAdmin(new PurchaseInvoiceController(db), 1).Destroy(pi2);
            await AssertParityAsync(db, cashId, s2Id, 5000m, "S7b pre-shift purchase delete");
        }
        finally { Cleanup(p); }
    }

    [Fact]
    public async Task S8_And_S9_SaleAndPurchaseReturns_HistorySqlAndLegacyDelete_MaintainsExactParity()
    {
        var (db, p, cashId, _) = await SetupFreshAsync();
        try
        {
            var drawer = new CashDrawerService(db);
            var s1 = await drawer.OpenSessionAsync(1, 2000m);
            var s1Id = AccountService.ToLong(s1["id"]);

            // S8a: Sale Return cash_refund + History endpoint SQL verification
            var srJson = JsonSerializer.Serialize(new
            {
                customer_id = 1, date = "2026-11-20", settlement_mode = "cash_refund", payment_method = "cash",
                items = new[] { new { product_id = 1, unit_return_quantity = 3, unit_sale_price = 100, item_discount_percentage = 0, item_tax_percentage = 0 } }
            });
            var srId = ExtractId(await AttachAdmin(new SaleReturnController(db), 1, srJson).Store());
            await AssertParityAsync(db, cashId, s1Id, 1700m, "S8a sale return cash_refund");

            var histResult = await AttachAdmin(new CashDrawerController(db), 1).History();
            var histJson = JsonSerializer.Serialize((histResult as ObjectResult)!.Value);
            using (var doc = JsonDocument.Parse(histJson))
            {
                var firstRow = doc.RootElement.GetProperty("data")[0];
                firstRow.GetProperty("total_cash_out").GetDecimal().Should().Be(300m, "History SQL must include cash_refund in total_cash_out");
            }

            // S8b: Legacy Laravel 'cash_return' drawer row deleted cleanly
            await db.ExecuteAsync("UPDATE cash_drawer_transactions SET type = 'cash_return' WHERE reference_type = 'sale_return' AND reference_id = @id", new { id = srId });
            await AttachAdmin(new SaleReturnController(db), 1).Destroy(srId);
            await AssertParityAsync(db, cashId, s1Id, 2000m, "S8b delete legacy cash_return sale return");

            // S9a & S9b: Purchase Return records 'purchase_return' type and handles pre-shift delete
            var prJson = JsonSerializer.Serialize(new
            {
                supplier_id = 1, date = "2026-11-20", settlement_mode = "cash_refund", payment_method = "cash",
                items = new[] { new { product_id = 1, return_unit_quantity = 4, unit_purchase_price = 50, item_discount_percentage = 0 } }
            });
            var prId = ExtractId(await AttachAdmin(new PurchaseReturnController(db), 1, prJson).Store());
            var prDrawerType = await db.ExecuteScalarAsync<string>("SELECT type FROM cash_drawer_transactions WHERE reference_type = 'purchase_return' AND reference_id = @id", new { id = prId });
            prDrawerType.Should().Be("purchase_return");
            await AssertParityAsync(db, cashId, s1Id, 2200m, "S9a purchase return cash refund");

            await drawer.CloseSessionAsync(s1Id, 2200m);
            var s2 = await drawer.OpenSessionAsync(1, 2200m);
            var s2Id = AccountService.ToLong(s2["id"]);
            await AttachAdmin(new PurchaseReturnController(db), 1).Destroy(prId);
            await AssertParityAsync(db, cashId, s2Id, 2000m, "S9b pre-shift purchase return delete");
        }
        finally { Cleanup(p); }
    }

    [Fact]
    public async Task S10_ChequePayment_ClearedFallbackToCashAndBounce_MaintainsExactParity()
    {
        var (db, p, cashId, _) = await SetupFreshAsync(createBank: false);
        try
        {
            var drawer = new CashDrawerService(db);
            var s = await drawer.OpenSessionAsync(1, 1000m);
            var sid = AccountService.ToLong(s["id"]);

            var paySvc = new PaymentService(db);
            var pay = await paySvc.RecordCustomerPaymentAsync(2, new Dictionary<string, object?>
            {
                ["amount"] = 500m,
                ["payment_method"] = "cheque",
                ["cheque_status"] = "cleared",
                ["cheque_number"] = "CHQ-100"
            }, 1);
            var payId = AccountService.ToLong(pay["id"]);
            await AssertParityAsync(db, cashId, sid, 1500m, "S10a cleared cheque fallback to Cash in Hand");

            await paySvc.UpdateChequeStatusAsync(payId, "bounced", null, "NSF", 1);
            await AssertParityAsync(db, cashId, sid, 1000m, "S10b bounce cleared cheque");
        }
        finally { Cleanup(p); }
    }

    [Fact]
    public async Task S11_And_S12_RecurringExpenseAndPayrollExpenseDelete_MaintainsExactParity()
    {
        var (db, p, cashId, _) = await SetupFreshAsync();
        try
        {
            var drawer = new CashDrawerService(db);
            var s = await drawer.OpenSessionAsync(1, 10000m);
            var sid = AccountService.ToLong(s["id"]);

            // S11: Background recurring expense syncs both account and active open drawer
            await db.InsertAsync("expenses", new Dictionary<string, object?>
            {
                ["expense_number"] = "EXP-TMPL-1",
                ["expense_category_id"] = 1,
                ["account_id"] = cashId,
                ["user_id"] = 1,
                ["amount"] = 400m,
                ["date"] = DateTime.UtcNow.AddDays(-2).ToString("yyyy-MM-dd"),
                ["payment_method"] = "cash",
                ["payee"] = "Shop Rent",
                ["description"] = "Daily cleaning",
                ["is_recurring"] = 1,
                ["recurring_frequency"] = "daily"
            });

            var opts = new DbContextOptionsBuilder<KarobarDbContext>().UseSqlite($"Data Source={p}").Options;
            await using (var ef = new KarobarDbContext(opts))
            {
                await new KarobarCliCommandRunner().ExecuteProcessRecurringExpensesAsync(ef, new Dictionary<string, string?>());
            }
            await AssertParityAsync(db, cashId, sid, 9600m, "S11 recurring expense");

            // S12: Payroll cash payment links cash_drawer_session_id so deleting the expense reverses drawer cleanly
            var hr = new HrPayrollService(db);
            await hr.EnsureTablesAsync();
            await db.InsertAsync("employees", new Dictionary<string, object?>
            {
                ["employee_code"] = "EMP-01", ["name"] = "Ali", ["basic_salary"] = 3000m, ["payment_method"] = "cash", ["status"] = "active"
            });
            var run = await hr.GenerateOrRefreshPayrollRunAsync("2026-11", null, 1);
            var runItems = (List<Dictionary<string, object?>>)run["items"]!;
            var paidItem = await hr.PayPayrollItemAsync(AccountService.ToLong(runItems[0]["id"]), new Dictionary<string, object?>
            {
                ["payment_method"] = "cash", ["account_id"] = cashId
            }, 1);
            await AssertParityAsync(db, cashId, sid, 6600m, "S12a payroll cash paid");

            var expId = AccountService.ToLong(paidItem["expense_id"]);
            var expSessId = await db.ExecuteScalarAsync<long?>("SELECT cash_drawer_session_id FROM expenses WHERE id = @id", new { id = expId });
            expSessId.Should().Be(sid, "Payroll cash expense must be linked to the active cash_drawer_session_id");

            await new ExpenseService(db).DeleteExpenseAsync(expId, 1);
            await AssertParityAsync(db, cashId, sid, 9600m, "S12b payroll expense deleted");
        }
        finally { Cleanup(p); }
    }

    [Fact]
    public async Task S13_MultiTerminal_IndependentCountersCloseAndAccountsGet_PreservesAllCash()
    {
        var (db, p, cashId, _) = await SetupFreshAsync();
        try
        {
            var drawer = new CashDrawerService(db);
            var s1 = await drawer.OpenSessionAsync(1, 5000m, "Counter 1", terminalId: 1);
            var s1Id = AccountService.ToLong(s1["id"]);
            var s2 = await drawer.OpenSessionAsync(2, 3000m, "Counter 2", terminalId: 2);
            var s2Id = AccountService.ToLong(s2["id"]);

            // Sale of 500 on Counter 1 must hit Counter 1, not Counter 2
            var saleJson = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash",
                date = "2026-11-20", total_receive = 500,
                items = new[] { new { product_id = 1, quantity = 5, price = 100, sub_total = 500 } }
            });
            await AttachAdmin(new SaleInvoiceController(db), 1, saleJson, terminalId: 1).Store();

            var c1Exp = await db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = s1Id });
            var c2Exp = await db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = s2Id });
            var accBal = await db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId });

            c1Exp.Should().Be(5500m, "Counter 1 should receive its own Rs. 500 sale");
            c2Exp.Should().Be(3000m, "Counter 2 must remain untouched");
            accBal.Should().Be(8500m, "Total Cash in Hand must equal Counter 1 + Counter 2");

            // Close Counter 1 while Counter 2 is still open -> total Cash in Hand stays 8,500
            await drawer.CloseSessionAsync(s1Id, 5500m, "C1 closed", null, 1);
            var afterC1 = await db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId });
            afterC1.Should().Be(8500m, "Closing Counter 1 must not wipe out closed shift cash");

            // Calling GET /api/accounts (ReconcileWithOpenCashDrawersAsync) must NOT wipe closed-counter cash
            await new AccountService(db).ReconcileWithOpenCashDrawersAsync(1);
            var afterGet = await db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId });
            afterGet.Should().Be(8500m, "GET /api/accounts must never fabricate destructive reconcile withdrawals");

            // Close Counter 2 with a Rs. 50 physical shortage -> total Cash in Hand adjusts by exact -50 to 8,450
            await drawer.CloseSessionAsync(s2Id, 2950m, "C2 closed", null, 2);
            var afterC2 = await db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId });
            afterC2.Should().Be(8450m, "Closing Counter 2 with Rs. 50 shortage must adjust Cash in Hand by exactly -50");
        }
        finally { Cleanup(p); }
    }

    [Fact]
    public async Task S14_And_S15_SaleReversalAfterUpdate_FullDayHappyPath_And_ReconciliationCheck()
    {
        var (db, p, cashId, bankId) = await SetupFreshAsync();
        try
        {
            var drawer = new CashDrawerService(db);
            var s = await drawer.OpenSessionAsync(1, 1000m);
            var sid = AccountService.ToLong(s["id"]);

            // S14: Create 500 cash sale -> Edit to 200 -> Reverse -> back to 1,000 exact
            var sale500 = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash",
                date = "2026-11-20", total_receive = 500,
                items = new[] { new { product_id = 1, quantity = 5, price = 100, sub_total = 500 } }
            });
            var inv14 = ExtractId(await AttachAdmin(new SaleInvoiceController(db), 1, sale500).Store());

            var edit200 = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash",
                date = "2026-11-20", total_receive = 200,
                items = new[] { new { product_id = 1, quantity = 2, price = 100, sub_total = 200 } }
            });
            await AttachAdmin(new SaleInvoiceController(db), 1, edit200).Update(inv14);
            await AssertParityAsync(db, cashId, sid, 1200m, "S14a after edit 500->200");

            await AttachAdmin(new SaleInvoiceController(db), 1, JsonSerializer.Serialize(new { reason = "Void after edit" })).Reverse(inv14);
            await AssertParityAsync(db, cashId, sid, 1000m, "S14b reverse after edit");

            // S15: Combined full-day operations
            // +1000 sale, -400 purchase, +600 cust payment, -250 supplier payment, -150 expense, -500 transfer to bank, +750 cash_in => 2050
            var sale1000 = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash",
                date = "2026-11-20", total_receive = 1000,
                items = new[] { new { product_id = 1, quantity = 10, price = 100, sub_total = 1000 } }
            });
            await AttachAdmin(new SaleInvoiceController(db), 1, sale1000).Store();

            var purch400 = JsonSerializer.Serialize(new
            {
                supplier_id = 1, invoice_number = "HP-1", invoice_type = "debit", payment_method = "cash",
                posted_date = "2026-11-20", total_paid = 400,
                items = new[] { new { product_id = 1, pack_quantity = 1, pack_size = 10, unit_quantity = 8, quantity = 8, pack_purchase_price = 500, unit_purchase_price = 50, sub_total = 400 } }
            });
            await AttachAdmin(new PurchaseInvoiceController(db), 1, purch400).Store();

            var paySvc = new PaymentService(db);
            await paySvc.RecordCustomerPaymentAsync(2, new Dictionary<string, object?> { ["amount"] = 600m, ["payment_method"] = "cash", ["account_id"] = cashId }, 1);
            await paySvc.RecordSupplierPaymentAsync(1, new Dictionary<string, object?> { ["amount"] = 250m, ["payment_method"] = "cash", ["account_id"] = cashId }, 1);
            await new ExpenseService(db).CreateExpenseAsync(new Dictionary<string, object?> { ["expense_category_id"] = 1, ["account_id"] = cashId, ["amount"] = 150m, ["date"] = "2026-11-20", ["payment_method"] = "cash", ["description"] = "Tea" }, 1);
            await new AccountService(db).TransferAsync(cashId, bankId, 500m, "Bank deposit", 1);
            await drawer.RecordTransactionAsync(s, "cash_in", 750m, "manual", null, "Top up", 1);

            await AssertParityAsync(db, cashId, sid, 2050m, "S15 full-day combined operations");

            // Verify read-only ReconciliationCheck endpoint reports is_balanced=true and 0 variance
            var reconRes = await AttachAdmin(new CashDrawerController(db), 1).ReconciliationCheck();
            var reconJson = JsonSerializer.Serialize((reconRes as ObjectResult)!.Value);
            using var doc = JsonDocument.Parse(reconJson);
            doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("is_balanced").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("cash_account_balance").GetDecimal().Should().Be(2050m);
            doc.RootElement.GetProperty("open_drawers_expected_total").GetDecimal().Should().Be(2050m);
            doc.RootElement.GetProperty("difference").GetDecimal().Should().Be(0m);
            doc.RootElement.GetProperty("open_sessions")[0].GetProperty("stale_delta").GetDecimal().Should().Be(0m);
        }
        finally { Cleanup(p); }
    }

    [Fact]
    public async Task S16_To_S19_PreShiftDoubleEdit_And_EditThenDelete_SalesPurchasesReturnsExpenses_MaintainsExactParity()
    {
        var (db, p, cashId, _) = await SetupFreshAsync();
        try
        {
            var drawer = new CashDrawerService(db);
            var s1 = await drawer.OpenSessionAsync(1, 5000m);
            var s1Id = AccountService.ToLong(s1["id"]);

            // Shift #1: Create Sale (+500), Purchase (-1000), Sale Return (-200), Purchase Return (+300), Expense (-400)
            // Expected end of Shift #1 = 5000 + 500 - 1000 - 200 + 300 - 400 = 4,200
            var sale500 = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash",
                date = "2026-11-20", total_receive = 500,
                items = new[] { new { product_id = 1, quantity = 5, price = 100, sub_total = 500 } }
            });
            var saleId = ExtractId(await AttachAdmin(new SaleInvoiceController(db), 1, sale500).Store());

            var purch1000 = JsonSerializer.Serialize(new
            {
                supplier_id = 1, invoice_number = "PRE-P1", invoice_type = "debit", payment_method = "cash",
                posted_date = "2026-11-20", total_paid = 1000,
                items = new[] { new { product_id = 1, pack_quantity = 2, pack_size = 10, unit_quantity = 20, quantity = 20, pack_purchase_price = 500, unit_purchase_price = 50, sub_total = 1000 } }
            });
            var piId = ExtractId(await AttachAdmin(new PurchaseInvoiceController(db), 1, purch1000).Store());

            var sr200 = JsonSerializer.Serialize(new
            {
                customer_id = 1, date = "2026-11-20", settlement_mode = "cash_refund", payment_method = "cash",
                items = new[] { new { product_id = 1, unit_return_quantity = 2, unit_sale_price = 100, item_discount_percentage = 0, item_tax_percentage = 0 } }
            });
            var srId = ExtractId(await AttachAdmin(new SaleReturnController(db), 1, sr200).Store());

            var pr300 = JsonSerializer.Serialize(new
            {
                supplier_id = 1, date = "2026-11-20", settlement_mode = "cash_refund", payment_method = "cash",
                items = new[] { new { product_id = 1, return_unit_quantity = 6, unit_purchase_price = 50, item_discount_percentage = 0 } }
            });
            var prId = ExtractId(await AttachAdmin(new PurchaseReturnController(db), 1, pr300).Store());

            var expSvc = new ExpenseService(db);
            var exp = await expSvc.CreateExpenseAsync(new Dictionary<string, object?>
            {
                ["expense_category_id"] = 1, ["account_id"] = cashId, ["amount"] = 400m,
                ["date"] = "2026-11-20", ["payment_method"] = "cash", ["description"] = "Fuel"
            }, 1);
            var expId = AccountService.ToLong(exp["id"]);

            await AssertParityAsync(db, cashId, s1Id, 4200m, "Shift #1 before close");
            await drawer.CloseSessionAsync(s1Id, 4200m, "Close S1", null, 1);

            // Open Shift #2 with 4,200
            var s2 = await drawer.OpenSessionAsync(1, 4200m);
            var s2Id = AccountService.ToLong(s2["id"]);

            // S16: Double-edit pre-shift Sale (500 -> 700 -> 800), then DELETE it!
            // 4200 + 200 = 4400 -> +100 = 4500 -> delete (-800) = 3700
            var sale700 = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash",
                date = "2026-11-20", total_receive = 700,
                items = new[] { new { product_id = 1, quantity = 7, price = 100, sub_total = 700 } }
            });
            await AttachAdmin(new SaleInvoiceController(db), 1, sale700).Update(saleId);
            await AssertParityAsync(db, cashId, s2Id, 4400m, "S16a pre-shift sale edit #1 (500->700)");

            var sale800 = JsonSerializer.Serialize(new
            {
                customer_id = 1, invoice_type = "debit", sale_type = "retail", payment_method = "cash",
                date = "2026-11-20", total_receive = 800,
                items = new[] { new { product_id = 1, quantity = 8, price = 100, sub_total = 800 } }
            });
            await AttachAdmin(new SaleInvoiceController(db), 1, sale800).Update(saleId);
            await AssertParityAsync(db, cashId, s2Id, 4500m, "S16b pre-shift sale edit #2 (700->800)");

            await AttachAdmin(new SaleInvoiceController(db), 1, "{}").Destroy(saleId, "refund");
            await AssertParityAsync(db, cashId, s2Id, 3700m, "S16c pre-shift sale edit-then-delete (-800)");

            // S17: Double-edit pre-shift Purchase (1000 -> 1200 -> 900), then DELETE it!
            // 3700 - 200 = 3500 -> +300 = 3800 -> delete (+900) = 4700
            var purch1200 = JsonSerializer.Serialize(new
            {
                supplier_id = 1, invoice_number = "PRE-P1", invoice_type = "debit", payment_method = "cash",
                posted_date = "2026-11-20", total_paid = 1200,
                items = new[] { new { product_id = 1, pack_quantity = 2, pack_size = 10, unit_quantity = 24, quantity = 24, pack_purchase_price = 500, unit_purchase_price = 50, sub_total = 1200 } }
            });
            await AttachAdmin(new PurchaseInvoiceController(db), 1, purch1200).Update(piId);
            await AssertParityAsync(db, cashId, s2Id, 3500m, "S17a pre-shift purchase edit #1 (1000->1200)");

            var purch900 = JsonSerializer.Serialize(new
            {
                supplier_id = 1, invoice_number = "PRE-P1", invoice_type = "debit", payment_method = "cash",
                posted_date = "2026-11-20", total_paid = 900,
                items = new[] { new { product_id = 1, pack_quantity = 1, pack_size = 10, unit_quantity = 18, quantity = 18, pack_purchase_price = 500, unit_purchase_price = 50, sub_total = 900 } }
            });
            await AttachAdmin(new PurchaseInvoiceController(db), 1, purch900).Update(piId);
            await AssertParityAsync(db, cashId, s2Id, 3800m, "S17b pre-shift purchase edit #2 (1200->900)");

            await AttachAdmin(new PurchaseInvoiceController(db), 1).Destroy(piId);
            await AssertParityAsync(db, cashId, s2Id, 4700m, "S17c pre-shift purchase edit-then-delete (+900)");

            // S18: Double-edit pre-shift Sale Return WITHOUT passing update_drawer_if_pre_shift (200 -> 300 -> 100), then DELETE it!
            // 4700 - 100 = 4600 -> +200 = 4800 -> delete (+100) = 4900
            var sr300 = JsonSerializer.Serialize(new
            {
                customer_id = 1, date = "2026-11-20", settlement_mode = "cash_refund", payment_method = "cash",
                items = new[] { new { product_id = 1, unit_return_quantity = 3, unit_sale_price = 100, item_discount_percentage = 0, item_tax_percentage = 0 } }
            });
            await AttachAdmin(new SaleReturnController(db), 1, sr300).Update(srId);
            await AssertParityAsync(db, cashId, s2Id, 4600m, "S18a pre-shift sale return edit #1 omitted flag (200->300)");

            var sr100 = JsonSerializer.Serialize(new
            {
                customer_id = 1, date = "2026-11-20", settlement_mode = "cash_refund", payment_method = "cash",
                items = new[] { new { product_id = 1, unit_return_quantity = 1, unit_sale_price = 100, item_discount_percentage = 0, item_tax_percentage = 0 } }
            });
            await AttachAdmin(new SaleReturnController(db), 1, sr100).Update(srId);
            await AssertParityAsync(db, cashId, s2Id, 4800m, "S18b pre-shift sale return edit #2 (300->100)");

            await AttachAdmin(new SaleReturnController(db), 1).Destroy(srId);
            await AssertParityAsync(db, cashId, s2Id, 4900m, "S18c pre-shift sale return edit-then-delete (+100)");

            // S18d: Double-edit pre-shift Purchase Return WITHOUT passing flag (300 -> 400 -> 250), then DELETE it!
            // 4900 + 100 = 5000 -> -150 = 4850 -> delete (-250) = 4600
            var pr400 = JsonSerializer.Serialize(new
            {
                supplier_id = 1, date = "2026-11-20", settlement_mode = "cash_refund", payment_method = "cash",
                items = new[] { new { product_id = 1, return_unit_quantity = 8, unit_purchase_price = 50, item_discount_percentage = 0 } }
            });
            await AttachAdmin(new PurchaseReturnController(db), 1, pr400).Update(prId);
            await AssertParityAsync(db, cashId, s2Id, 5000m, "S18d pre-shift purchase return edit #1 (300->400)");

            var pr250 = JsonSerializer.Serialize(new
            {
                supplier_id = 1, date = "2026-11-20", settlement_mode = "cash_refund", payment_method = "cash",
                items = new[] { new { product_id = 1, return_unit_quantity = 5, unit_purchase_price = 50, item_discount_percentage = 0 } }
            });
            await AttachAdmin(new PurchaseReturnController(db), 1, pr250).Update(prId);
            await AssertParityAsync(db, cashId, s2Id, 4850m, "S18e pre-shift purchase return edit #2 (400->250)");

            await AttachAdmin(new PurchaseReturnController(db), 1).Destroy(prId);
            await AssertParityAsync(db, cashId, s2Id, 4600m, "S18f pre-shift purchase return edit-then-delete (-250)");

            // S19: Double-edit pre-shift Expense (400 -> 250 -> 350), then DELETE it!
            // 4600 + 150 = 4750 -> -100 = 4650 -> delete (+350) = 5000
            await expSvc.UpdateExpenseAsync(expId, new Dictionary<string, object?> { ["amount"] = 250m }, 1);
            await AssertParityAsync(db, cashId, s2Id, 4750m, "S19a pre-shift expense edit #1 (400->250)");

            await expSvc.UpdateExpenseAsync(expId, new Dictionary<string, object?> { ["amount"] = 350m }, 1);
            await AssertParityAsync(db, cashId, s2Id, 4650m, "S19b pre-shift expense edit #2 (250->350)");

            await expSvc.DeleteExpenseAsync(expId, 1);
            await AssertParityAsync(db, cashId, s2Id, 5000m, "S19c pre-shift expense edit-then-delete (+350)");
        }
        finally { Cleanup(p); }
    }

    [Fact]
    public async Task S20_To_S23_NonCashWriteOff_LegacyCashDrawerCode_MultiTerminalOps_And_SafeReconcile()
    {
        var (db, p, cashId, bankId) = await SetupFreshAsync();
        try
        {
            var drawer = new CashDrawerService(db);
            var s1 = await drawer.OpenSessionAsync(1, 3000m, "Counter 1", terminalId: 1);
            var s1Id = AccountService.ToLong(s1["id"]);
            var s2 = await drawer.OpenSessionAsync(2, 2000m, "Counter 2", terminalId: 2);
            var s2Id = AccountService.ToLong(s2["id"]);

            // S20: Editing a non-cash Stock Write-Off expense (payment_method='other', account_id=NULL) must NOT touch Cash in Hand or Drawer
            var writeOffId = await db.InsertAsync("expenses", new Dictionary<string, object?>
            {
                ["expense_number"] = "EXP-WO-1", ["expense_category_id"] = 1, ["account_id"] = null,
                ["cash_drawer_session_id"] = null, ["user_id"] = 1, ["amount"] = 750m,
                ["date"] = "2026-11-20", ["payment_method"] = "other", ["description"] = "Stock Shrinkage"
            });
            await new ExpenseService(db).UpdateExpenseAsync(writeOffId, new Dictionary<string, object?> { ["amount"] = 900m, ["payment_method"] = "other" }, 1, terminalId: 1);
            var balAfterWo = await db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId });
            balAfterWo.Should().Be(5000m, "Non-cash stock write-off edit must never withdraw from Cash in Hand");

            // S21: Legacy/imported DB where Cash in Hand has code='cash_drawer' & is_default=0 still mirrors in Cash Drawer
            await db.ExecuteAsync("UPDATE accounts SET is_default = 0 WHERE id = @id", new { id = cashId });
            var acctSvc = new AccountService(db);
            var cashRow = (await db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = cashId }))!;
            acctSvc.IsDefaultCashDrawerAccount(cashRow, "cash").Should().BeTrue("code='cash_drawer' must be recognized even if is_default=0");

            // S22: Multi-terminal Expense, Transfer, Adjust on Counter 1 hit Counter 1 ONLY (Counter 2 untouched at 2,000)
            await new ExpenseService(db).CreateExpenseAsync(new Dictionary<string, object?>
            {
                ["expense_category_id"] = 1, ["account_id"] = cashId, ["amount"] = 200m,
                ["date"] = "2026-11-20", ["payment_method"] = "cash", ["description"] = "C1 Tea"
            }, 1, terminalId: 1);
            await acctSvc.TransferAsync(cashId, bankId, 500m, "C1 Bank drop", 1, terminalId: 1);
            await acctSvc.AdjustAsync(cashId, "deposit", 100m, "C1 Float topup", 1, terminalId: 1);

            var c1Exp = await db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = s1Id });
            var c2Exp = await db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = s2Id });
            var cashBal = await db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashId });
            c1Exp.Should().Be(2400m, "Counter 1: 3000 - 200 - 500 + 100 = 2400");
            c2Exp.Should().Be(2000m, "Counter 2 must remain untouched");
            cashBal.Should().Be(4400m, "Cash in Hand must equal C1 (2400) + C2 (2000) = 4400");

            // S23: Simulate pre-existing legacy drift (+350 in Cash in Hand), detect via ReconciliationCheck, then fix via POST /api/cash-drawer/reconcile
            await db.ExecuteAsync("UPDATE accounts SET current_balance = 4750 WHERE id = @id", new { id = cashId });
            var checkRes = await AttachAdmin(new CashDrawerController(db), 1).ReconciliationCheck();
            using (var doc = JsonDocument.Parse(JsonSerializer.Serialize((checkRes as ObjectResult)!.Value)))
            {
                doc.RootElement.GetProperty("is_balanced").GetBoolean().Should().BeFalse();
                doc.RootElement.GetProperty("difference").GetDecimal().Should().Be(350m);
            }

            var recRes = await AttachAdmin(new CashDrawerController(db), 1, JsonSerializer.Serialize(new { reason = "Fix legacy drift" })).ReconcileCashAccount();
            using (var doc = JsonDocument.Parse(JsonSerializer.Serialize((recRes as ObjectResult)!.Value)))
            {
                doc.RootElement.GetProperty("reconciled").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("cash_account_balance").GetDecimal().Should().Be(4400m);
                doc.RootElement.GetProperty("adjustment_amount").GetDecimal().Should().Be(-350m);
            }
        }
        finally { Cleanup(p); }
    }
}

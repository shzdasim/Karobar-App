using System.Data;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class ReturnSettlementService
{
    public static readonly string[] ValidModes = { "cash_refund", "credit_note", "invoice_offset" };

    private readonly KarobarDb _db;

    public ReturnSettlementService(KarobarDb db)
    {
        _db = db;
    }

    public async Task<string> ResolveSaleSettlementModeAsync(
        string? requestedMode,
        long? saleInvoiceId = null,
        string? fallbackMode = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedMode) && ValidModes.Contains(requestedMode, StringComparer.OrdinalIgnoreCase))
        {
            return requestedMode.ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(fallbackMode) && ValidModes.Contains(fallbackMode, StringComparer.OrdinalIgnoreCase))
        {
            return fallbackMode.ToLowerInvariant();
        }

        if (saleInvoiceId.HasValue && saleInvoiceId.Value > 0 &&
            await _db.HasTableAsync("sale_invoices") &&
            await _db.HasColumnAsync("sale_invoices", "invoice_type"))
        {
            var invType = await _db.ExecuteScalarAsync<string?>(
                "SELECT invoice_type FROM sale_invoices WHERE id = @id",
                new { id = saleInvoiceId.Value },
                conn,
                tx);
            if (string.Equals(invType, "credit", StringComparison.OrdinalIgnoreCase))
            {
                return "credit_note";
            }
        }

        return "cash_refund";
    }

    public async Task<string> ResolvePurchaseSettlementModeAsync(
        string? requestedMode,
        long? purchaseInvoiceId = null,
        string? fallbackMode = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        if (!string.IsNullOrWhiteSpace(requestedMode) && ValidModes.Contains(requestedMode, StringComparer.OrdinalIgnoreCase))
        {
            return requestedMode.ToLowerInvariant();
        }

        if (!string.IsNullOrWhiteSpace(fallbackMode) && ValidModes.Contains(fallbackMode, StringComparer.OrdinalIgnoreCase))
        {
            return fallbackMode.ToLowerInvariant();
        }

        if (purchaseInvoiceId.HasValue && purchaseInvoiceId.Value > 0 &&
            await _db.HasTableAsync("purchase_invoices") &&
            await _db.HasColumnAsync("purchase_invoices", "invoice_type"))
        {
            var invType = await _db.ExecuteScalarAsync<string?>(
                "SELECT invoice_type FROM purchase_invoices WHERE id = @id",
                new { id = purchaseInvoiceId.Value },
                conn,
                tx);
            if (string.Equals(invType, "credit", StringComparison.OrdinalIgnoreCase))
            {
                return "credit_note";
            }
        }

        return "cash_refund";
    }

    public async Task ApplySaleReturnSettlementAsync(
        Dictionary<string, object?> saleReturn,
        long? userId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        var srId = ToLong(saleReturn.GetValueOrDefault("id"));
        var mode = (saleReturn.GetValueOrDefault("settlement_mode")?.ToString() ?? "").Trim();
        if (string.IsNullOrEmpty(mode)) mode = "cash_refund";
        var amount = Math.Round(ToDouble(saleReturn.GetValueOrDefault("total")), 2);

        if (mode is "credit_note" or "invoice_offset")
        {
            await PersistReturnFieldsAsync("sale_returns", srId, saleReturn, new Dictionary<string, object?>
            {
                ["settlement_mode"] = mode,
                ["credited_amount"] = amount,
                ["account_id"] = null
            }, conn, tx);

            await SyncCustomerLedgerForSaleReturnAsync(saleReturn, amount, mode, userId, conn, tx);
            return;
        }

        await DeleteCustomerLedgerForSaleReturnAsync(srId, conn, tx);

        var payMethod = saleReturn.GetValueOrDefault("payment_method")?.ToString();
        if (string.IsNullOrWhiteSpace(payMethod)) payMethod = "cash";
        long? resolvedAccountId = ToLong(saleReturn.GetValueOrDefault("account_id")) > 0
            ? ToLong(saleReturn.GetValueOrDefault("account_id"))
            : null;
        Dictionary<string, object?>? account = null;

        if (amount > 0)
        {
            account = await ResolveAccountAsync(resolvedAccountId, payMethod, conn, tx);
            if (account != null)
            {
                resolvedAccountId = ToLong(account["id"]);
                var postedNumber = saleReturn.GetValueOrDefault("posted_number")?.ToString() ?? $"SR-{srId}";
                await WithdrawFromAccountAsync(
                    account,
                    amount,
                    "sale_return",
                    srId,
                    $"Customer refund for sale return #{postedNumber}",
                    userId,
                    conn,
                    tx);
            }
        }

        await PersistReturnFieldsAsync("sale_returns", srId, saleReturn, new Dictionary<string, object?>
        {
            ["settlement_mode"] = "cash_refund",
            ["payment_method"] = payMethod,
            ["account_id"] = resolvedAccountId,
            ["credited_amount"] = 0
        }, conn, tx);

        if (amount > 0 && IsDefaultCashDrawerAccount(account, payMethod))
        {
            var postedNumber = saleReturn.GetValueOrDefault("posted_number")?.ToString() ?? $"SR-{srId}";
            await RecordCashDrawerTransactionAsync("cash_refund", amount, "sale_return", srId, $"Cash refund for sale return #{postedNumber}", userId, conn, tx);
        }
    }

    public async Task SyncSaleReturnSettlementUpdateAsync(
        Dictionary<string, object?> saleReturn,
        IDictionary<string, object?> oldSnapshot,
        bool? updateDrawerIfPreShift = null,
        long? userId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        // Unwind old settlement and apply new settlement cleanly
        var srId = ToLong(saleReturn.GetValueOrDefault("id"));
        var newMode = (saleReturn.GetValueOrDefault("settlement_mode")?.ToString() ?? "cash_refund").Trim();
        var newPayMethod = (saleReturn.GetValueOrDefault("payment_method")?.ToString() ?? "cash").Trim();
        var newTotal = Math.Round(ToDouble(saleReturn.GetValueOrDefault("total")), 2);

        var oldMode = (oldSnapshot.TryGetValue("settlement_mode", out var om) ? om?.ToString() : "cash_refund") ?? "cash_refund";
        var oldPayMethod = (oldSnapshot.TryGetValue("payment_method", out var opm) ? opm?.ToString() : "cash") ?? "cash";

        // 1. Check existing account transaction
        Dictionary<string, object?>? existingAccTxn = null;
        if (await _db.HasTableAsync("account_transactions"))
        {
            existingAccTxn = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM account_transactions WHERE reference_type = 'sale_return' AND reference_id = @id ORDER BY id DESC LIMIT 1",
                new { id = srId },
                conn,
                tx);
        }

        if (existingAccTxn != null && await _db.HasTableAsync("accounts"))
        {
            var oldAccId = ToLong(existingAccTxn.GetValueOrDefault("account_id"));
            var oldAmt = ToDouble(existingAccTxn.GetValueOrDefault("amount"));
            if (oldAccId > 0)
            {
                await _db.ExecuteAsync(
                    "UPDATE accounts SET current_balance = ROUND(COALESCE(current_balance, 0) + @amt, 2), updated_at = @now WHERE id = @id",
                    new { amt = oldAmt, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), id = oldAccId },
                    conn,
                    tx);
            }
            await _db.ExecuteAsync(
                "DELETE FROM account_transactions WHERE id = @id",
                new { id = existingAccTxn["id"] },
                conn,
                tx);
        }

        // Remove old cash drawer transaction for this sale return ONLY if it belongs to the currently open shift
        var hadDrawerInOpenShift = await RemoveCashDrawerTransactionForRefAsync("sale_return", srId, "cash_refund", conn, tx) > 0;
        var hadHistoricalDrawerTxn = !hadDrawerInOpenShift && await HasAnyCashDrawerTransactionForRefAsync("sale_return", srId, conn, tx);
        var oldTotal = Math.Round(ToDouble(oldSnapshot.TryGetValue("total", out var ot) ? ot : 0), 2);
        var oldWasCashRefund = string.Equals(oldMode, "cash_refund", StringComparison.OrdinalIgnoreCase);

        if (newMode == "cash_refund" && newTotal > 0)
        {
            var oldAccIdSnap = oldSnapshot.TryGetValue("account_id", out var oa) ? ToLong(oa) : 0;
            var curAccId = ToLong(saleReturn.GetValueOrDefault("account_id"));
            long? explicitAccountId = (newPayMethod != oldPayMethod && curAccId == oldAccIdSnap)
                ? null
                : (curAccId > 0 ? curAccId : null);

            var targetAccount = await ResolveAccountAsync(explicitAccountId, newPayMethod, conn, tx);
            long? resolvedAccountId = targetAccount != null ? ToLong(targetAccount["id"]) : explicitAccountId;
            var postedNumber = saleReturn.GetValueOrDefault("posted_number")?.ToString() ?? $"SR-{srId}";

            if (targetAccount != null)
            {
                await WithdrawFromAccountAsync(
                    targetAccount,
                    newTotal,
                    "sale_return",
                    srId,
                    $"Customer refund for sale return #{postedNumber}",
                    userId,
                    conn,
                    tx);
            }

            await PersistReturnFieldsAsync("sale_returns", srId, saleReturn, new Dictionary<string, object?>
            {
                ["settlement_mode"] = "cash_refund",
                ["payment_method"] = newPayMethod,
                ["account_id"] = resolvedAccountId,
                ["credited_amount"] = 0
            }, conn, tx);

            await DeleteCustomerLedgerForSaleReturnAsync(srId, conn, tx);

            if (IsDefaultCashDrawerAccount(targetAccount, newPayMethod))
            {
                if (hadDrawerInOpenShift || !hadHistoricalDrawerTxn)
                {
                    // Same open shift (or brand-new cash refund): record full updated amount cleanly
                    if (!hadHistoricalDrawerTxn && updateDrawerIfPreShift == false && oldWasCashRefund)
                    {
                        // User explicitly declined touching the current shift drawer for a pre-shift edit
                    }
                    else
                    {
                        await RecordCashDrawerTransactionAsync("cash_refund", newTotal, "sale_return", srId, $"Cash refund for sale return #{postedNumber}", userId, conn, tx);
                    }
                }
                else if (updateDrawerIfPreShift == true)
                {
                    // Pre-shift return edited with user opting to reflect the net cash delta in the current open shift
                    var delta = Math.Round(newTotal - (oldWasCashRefund ? oldTotal : 0.0), 2);
                    if (delta > 0)
                    {
                        await RecordCashDrawerTransactionAsync("cash_refund", delta, "sale_return", srId, $"Cash refund adjustment for pre-shift sale return #{postedNumber}", userId, conn, tx);
                    }
                    else if (delta < 0)
                    {
                        await RecordCashDrawerTransactionAsync("cash_in", Math.Abs(delta), "sale_return", srId, $"Cash refund reduction for pre-shift sale return #{postedNumber}", userId, conn, tx);
                    }
                }
            }
        }
        else
        {
            var credited = newMode is "credit_note" or "invoice_offset" ? newTotal : 0;
            await PersistReturnFieldsAsync("sale_returns", srId, saleReturn, new Dictionary<string, object?>
            {
                ["settlement_mode"] = newMode,
                ["account_id"] = null,
                ["credited_amount"] = credited
            }, conn, tx);

            if (newMode is "credit_note" or "invoice_offset")
            {
                await SyncCustomerLedgerForSaleReturnAsync(saleReturn, newTotal, newMode, userId, conn, tx);
            }
            else
            {
                await DeleteCustomerLedgerForSaleReturnAsync(srId, conn, tx);
            }
        }
    }

    public async Task RevertSaleReturnSettlementAsync(
        Dictionary<string, object?> saleReturn,
        long? userId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        var srId = ToLong(saleReturn.GetValueOrDefault("id"));
        await DeleteCustomerLedgerForSaleReturnAsync(srId, conn, tx);

        var mode = (saleReturn.GetValueOrDefault("settlement_mode")?.ToString() ?? "cash_refund").Trim();
        var payMethod = (saleReturn.GetValueOrDefault("payment_method")?.ToString() ?? "cash").Trim();
        var postedNumber = saleReturn.GetValueOrDefault("posted_number")?.ToString() ?? $"SR-{srId}";

        if (mode == "cash_refund")
        {
            if (await _db.HasTableAsync("account_transactions"))
            {
                var existingAccTxn = await _db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM account_transactions WHERE reference_type = 'sale_return' AND reference_id = @id ORDER BY id DESC LIMIT 1",
                    new { id = srId },
                    conn,
                    tx);

                if (existingAccTxn != null)
                {
                    var accId = ToLong(existingAccTxn.GetValueOrDefault("account_id"));
                    var acc = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accId }, conn, tx);
                    if (acc != null)
                    {
                        await DepositToAccountAsync(
                            acc,
                            ToDouble(existingAccTxn.GetValueOrDefault("amount")),
                            "sale_return_deletion",
                            srId,
                            $"Recovery for deleted sale return #{postedNumber}",
                            userId,
                            conn,
                            tx);
                    }
                }
                else if (ToDouble(saleReturn.GetValueOrDefault("total")) > 0)
                {
                    var accId = ToLong(saleReturn.GetValueOrDefault("account_id"));
                    var acc = await ResolveAccountAsync(accId > 0 ? accId : null, payMethod, conn, tx);
                    if (acc != null)
                    {
                        await DepositToAccountAsync(
                            acc,
                            ToDouble(saleReturn.GetValueOrDefault("total")),
                            "sale_return_deletion",
                            srId,
                            $"Recovery for deleted sale return #{postedNumber}",
                            userId,
                            conn,
                            tx);
                    }
                }
            }

            if (string.Equals(payMethod, "cash", StringComparison.OrdinalIgnoreCase))
            {
                await RemoveCashDrawerTransactionForRefAsync("sale_return", srId, "cash_refund", conn, tx);
            }
        }
    }

    public async Task ApplyPurchaseReturnSettlementAsync(
        Dictionary<string, object?> purchaseReturn,
        long? userId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        var prId = ToLong(purchaseReturn.GetValueOrDefault("id"));
        var mode = (purchaseReturn.GetValueOrDefault("settlement_mode")?.ToString() ?? "").Trim();
        if (string.IsNullOrEmpty(mode)) mode = "cash_refund";
        var amount = Math.Round(ToDouble(purchaseReturn.GetValueOrDefault("total")), 2);

        if (mode is "credit_note" or "invoice_offset")
        {
            await PersistReturnFieldsAsync("purchase_returns", prId, purchaseReturn, new Dictionary<string, object?>
            {
                ["settlement_mode"] = mode,
                ["credited_amount"] = amount,
                ["account_id"] = null
            }, conn, tx);

            await SyncSupplierLedgerForPurchaseReturnAsync(purchaseReturn, amount, mode, userId, conn, tx);
            return;
        }

        await DeleteSupplierLedgerForPurchaseReturnAsync(prId, conn, tx);

        var payMethod = purchaseReturn.GetValueOrDefault("payment_method")?.ToString();
        if (string.IsNullOrWhiteSpace(payMethod)) payMethod = "cash";
        long? resolvedAccountId = ToLong(purchaseReturn.GetValueOrDefault("account_id")) > 0
            ? ToLong(purchaseReturn.GetValueOrDefault("account_id"))
            : null;
        Dictionary<string, object?>? account = null;

        if (amount > 0)
        {
            account = await ResolveAccountAsync(resolvedAccountId, payMethod, conn, tx);
            if (account != null)
            {
                resolvedAccountId = ToLong(account["id"]);
                var postedNumber = purchaseReturn.GetValueOrDefault("posted_number")?.ToString() ?? $"PR-{prId}";
                await DepositToAccountAsync(
                    account,
                    amount,
                    "purchase_return",
                    prId,
                    $"Supplier refund for purchase return #{postedNumber}",
                    userId,
                    conn,
                    tx);
            }
        }

        await PersistReturnFieldsAsync("purchase_returns", prId, purchaseReturn, new Dictionary<string, object?>
        {
            ["settlement_mode"] = "cash_refund",
            ["payment_method"] = payMethod,
            ["account_id"] = resolvedAccountId,
            ["credited_amount"] = 0
        }, conn, tx);

        if (amount > 0 && IsDefaultCashDrawerAccount(account, payMethod))
        {
            var postedNumber = purchaseReturn.GetValueOrDefault("posted_number")?.ToString() ?? $"PR-{prId}";
            await RecordCashDrawerTransactionAsync("cash_in", amount, "purchase_return", prId, $"Supplier refund for purchase return #{postedNumber}", userId, conn, tx);
        }
    }

    public async Task SyncPurchaseReturnSettlementUpdateAsync(
        Dictionary<string, object?> purchaseReturn,
        IDictionary<string, object?> oldSnapshot,
        bool? updateDrawerIfPreShift = null,
        long? userId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        var prId = ToLong(purchaseReturn.GetValueOrDefault("id"));
        var newMode = (purchaseReturn.GetValueOrDefault("settlement_mode")?.ToString() ?? "cash_refund").Trim();
        var newPayMethod = (purchaseReturn.GetValueOrDefault("payment_method")?.ToString() ?? "cash").Trim();
        var newTotal = Math.Round(ToDouble(purchaseReturn.GetValueOrDefault("total")), 2);

        var oldPayMethod = (oldSnapshot.TryGetValue("payment_method", out var opm) ? opm?.ToString() : "cash") ?? "cash";

        Dictionary<string, object?>? existingAccTxn = null;
        if (await _db.HasTableAsync("account_transactions"))
        {
            existingAccTxn = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM account_transactions WHERE reference_type = 'purchase_return' AND reference_id = @id ORDER BY id DESC LIMIT 1",
                new { id = prId },
                conn,
                tx);
        }

        if (existingAccTxn != null && await _db.HasTableAsync("accounts"))
        {
            var oldAccId = ToLong(existingAccTxn.GetValueOrDefault("account_id"));
            var oldAmt = ToDouble(existingAccTxn.GetValueOrDefault("amount"));
            if (oldAccId > 0)
            {
                await _db.ExecuteAsync(
                    "UPDATE accounts SET current_balance = ROUND(COALESCE(current_balance, 0) - @amt, 2), updated_at = @now WHERE id = @id",
                    new { amt = oldAmt, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), id = oldAccId },
                    conn,
                    tx);
            }
            await _db.ExecuteAsync(
                "DELETE FROM account_transactions WHERE id = @id",
                new { id = existingAccTxn["id"] },
                conn,
                tx);
        }

        var hadDrawerInOpenShift = await RemoveCashDrawerTransactionForRefAsync("purchase_return", prId, "cash_in", conn, tx) > 0;
        var hadHistoricalDrawerTxn = !hadDrawerInOpenShift && await HasAnyCashDrawerTransactionForRefAsync("purchase_return", prId, conn, tx);
        var oldMode = (oldSnapshot.TryGetValue("settlement_mode", out var om) ? om?.ToString() : "cash_refund") ?? "cash_refund";
        var oldTotal = Math.Round(ToDouble(oldSnapshot.TryGetValue("total", out var ot) ? ot : 0), 2);
        var oldWasCashRefund = string.Equals(oldMode, "cash_refund", StringComparison.OrdinalIgnoreCase);

        if (newMode == "cash_refund" && newTotal > 0)
        {
            var oldAccIdSnap = oldSnapshot.TryGetValue("account_id", out var oa) ? ToLong(oa) : 0;
            var curAccId = ToLong(purchaseReturn.GetValueOrDefault("account_id"));
            long? explicitAccountId = (newPayMethod != oldPayMethod && curAccId == oldAccIdSnap)
                ? null
                : (curAccId > 0 ? curAccId : null);

            var targetAccount = await ResolveAccountAsync(explicitAccountId, newPayMethod, conn, tx);
            long? resolvedAccountId = targetAccount != null ? ToLong(targetAccount["id"]) : explicitAccountId;
            var postedNumber = purchaseReturn.GetValueOrDefault("posted_number")?.ToString() ?? $"PR-{prId}";

            if (targetAccount != null)
            {
                await DepositToAccountAsync(
                    targetAccount,
                    newTotal,
                    "purchase_return",
                    prId,
                    $"Supplier refund for purchase return #{postedNumber}",
                    userId,
                    conn,
                    tx);
            }

            await PersistReturnFieldsAsync("purchase_returns", prId, purchaseReturn, new Dictionary<string, object?>
            {
                ["settlement_mode"] = "cash_refund",
                ["payment_method"] = newPayMethod,
                ["account_id"] = resolvedAccountId,
                ["credited_amount"] = 0
            }, conn, tx);

            await DeleteSupplierLedgerForPurchaseReturnAsync(prId, conn, tx);

            if (IsDefaultCashDrawerAccount(targetAccount, newPayMethod))
            {
                if (hadDrawerInOpenShift || !hadHistoricalDrawerTxn)
                {
                    if (!hadHistoricalDrawerTxn && updateDrawerIfPreShift == false && oldWasCashRefund)
                    {
                        // User explicitly declined touching the current shift drawer for a pre-shift edit
                    }
                    else
                    {
                        await RecordCashDrawerTransactionAsync("cash_in", newTotal, "purchase_return", prId, $"Supplier refund for purchase return #{postedNumber}", userId, conn, tx);
                    }
                }
                else if (updateDrawerIfPreShift == true)
                {
                    var delta = Math.Round(newTotal - (oldWasCashRefund ? oldTotal : 0.0), 2);
                    if (delta > 0)
                    {
                        await RecordCashDrawerTransactionAsync("cash_in", delta, "purchase_return", prId, $"Supplier refund adjustment for pre-shift purchase return #{postedNumber}", userId, conn, tx);
                    }
                    else if (delta < 0)
                    {
                        await RecordCashDrawerTransactionAsync("cash_out", Math.Abs(delta), "purchase_return", prId, $"Supplier refund reduction for pre-shift purchase return #{postedNumber}", userId, conn, tx);
                    }
                }
            }
        }
        else
        {
            var credited = newMode is "credit_note" or "invoice_offset" ? newTotal : 0;
            await PersistReturnFieldsAsync("purchase_returns", prId, purchaseReturn, new Dictionary<string, object?>
            {
                ["settlement_mode"] = newMode,
                ["account_id"] = null,
                ["credited_amount"] = credited
            }, conn, tx);

            if (newMode is "credit_note" or "invoice_offset")
            {
                await SyncSupplierLedgerForPurchaseReturnAsync(purchaseReturn, newTotal, newMode, userId, conn, tx);
            }
            else
            {
                await DeleteSupplierLedgerForPurchaseReturnAsync(prId, conn, tx);
            }
        }
    }

    public async Task RevertPurchaseReturnSettlementAsync(
        Dictionary<string, object?> purchaseReturn,
        long? userId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        var prId = ToLong(purchaseReturn.GetValueOrDefault("id"));
        await DeleteSupplierLedgerForPurchaseReturnAsync(prId, conn, tx);

        var mode = (purchaseReturn.GetValueOrDefault("settlement_mode")?.ToString() ?? "cash_refund").Trim();
        var payMethod = (purchaseReturn.GetValueOrDefault("payment_method")?.ToString() ?? "cash").Trim();
        var postedNumber = purchaseReturn.GetValueOrDefault("posted_number")?.ToString() ?? $"PR-{prId}";

        if (mode == "cash_refund")
        {
            if (await _db.HasTableAsync("account_transactions"))
            {
                var existingAccTxn = await _db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM account_transactions WHERE reference_type = 'purchase_return' AND reference_id = @id ORDER BY id DESC LIMIT 1",
                    new { id = prId },
                    conn,
                    tx);

                if (existingAccTxn != null)
                {
                    var accId = ToLong(existingAccTxn.GetValueOrDefault("account_id"));
                    var acc = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accId }, conn, tx);
                    if (acc != null)
                    {
                        await WithdrawFromAccountAsync(
                            acc,
                            ToDouble(existingAccTxn.GetValueOrDefault("amount")),
                            "purchase_return_deletion",
                            prId,
                            $"Reversal for deleted purchase return #{postedNumber}",
                            userId,
                            conn,
                            tx);
                    }
                }
                else if (ToDouble(purchaseReturn.GetValueOrDefault("total")) > 0)
                {
                    var accId = ToLong(purchaseReturn.GetValueOrDefault("account_id"));
                    var acc = await ResolveAccountAsync(accId > 0 ? accId : null, payMethod, conn, tx);
                    if (acc != null)
                    {
                        await WithdrawFromAccountAsync(
                            acc,
                            ToDouble(purchaseReturn.GetValueOrDefault("total")),
                            "purchase_return_deletion",
                            prId,
                            $"Reversal for deleted purchase return #{postedNumber}",
                            userId,
                            conn,
                            tx);
                    }
                }
            }

            if (string.Equals(payMethod, "cash", StringComparison.OrdinalIgnoreCase))
            {
                await RemoveCashDrawerTransactionForRefAsync("purchase_return", prId, "cash_in", conn, tx);
            }
        }
    }

    // -------------------------------------------------------------------------
    // Ledger & Credit Note Payment Helpers
    // -------------------------------------------------------------------------

    private async Task PersistReturnFieldsAsync(
        string tableName,
        long returnId,
        Dictionary<string, object?> model,
        Dictionary<string, object?> fields,
        IDbConnection? conn,
        IDbTransaction? tx)
    {
        foreach (var kv in fields)
        {
            model[kv.Key] = kv.Value;
        }
        await _db.UpdateAsync(tableName, returnId, fields, "id", conn, tx);
    }

    private async Task DeleteCustomerLedgerForSaleReturnAsync(long saleReturnId, IDbConnection? conn, IDbTransaction? tx)
    {
        await DeleteSaleReturnCreditPaymentAsync(saleReturnId, conn, tx);
        if (await _db.HasTableAsync("customer_ledgers") && await _db.HasColumnAsync("customer_ledgers", "sale_return_id"))
        {
            await _db.ExecuteAsync("DELETE FROM customer_ledgers WHERE sale_return_id = @id", new { id = saleReturnId }, conn, tx);
        }
    }

    private async Task DeleteSupplierLedgerForPurchaseReturnAsync(long purchaseReturnId, IDbConnection? conn, IDbTransaction? tx)
    {
        await DeletePurchaseReturnCreditPaymentAsync(purchaseReturnId, conn, tx);
        if (await _db.HasTableAsync("supplier_ledgers") && await _db.HasColumnAsync("supplier_ledgers", "purchase_return_id"))
        {
            await _db.ExecuteAsync("DELETE FROM supplier_ledgers WHERE purchase_return_id = @id", new { id = purchaseReturnId }, conn, tx);
        }
    }

    private async Task SyncCustomerLedgerForSaleReturnAsync(
        Dictionary<string, object?> saleReturn,
        double amount,
        string mode,
        long? userId,
        IDbConnection? conn,
        IDbTransaction? tx)
    {
        var srId = ToLong(saleReturn.GetValueOrDefault("id"));
        if (amount <= 0)
        {
            await DeleteCustomerLedgerForSaleReturnAsync(srId, conn, tx);
            return;
        }

        var paymentId = await SyncSaleReturnCreditPaymentAsync(saleReturn, amount, mode, userId, conn, tx);

        if (!await _db.HasTableAsync("customer_ledgers") || !await _db.HasColumnAsync("customer_ledgers", "sale_return_id"))
        {
            return;
        }

        string? invPosted = null;
        var saleInvoiceId = ToLong(saleReturn.GetValueOrDefault("sale_invoice_id"));
        if (saleInvoiceId > 0 && await _db.HasTableAsync("sale_invoices"))
        {
            invPosted = await _db.ExecuteScalarAsync<string?>(
                "SELECT posted_number FROM sale_invoices WHERE id = @id",
                new { id = saleInvoiceId },
                conn,
                tx);
        }

        var postedNumber = saleReturn.GetValueOrDefault("posted_number")?.ToString() ?? $"SR-{srId}";
        var label = mode == "invoice_offset" ? "Invoice Offset" : "Credit Note";
        var description = $"{label} - Sale Return #{postedNumber}" + (!string.IsNullOrEmpty(invPosted) ? $" (Inv #{invPosted})" : "");
        var entryDate = NormalizeDateString(saleReturn.GetValueOrDefault("date"));

        var attrs = new Dictionary<string, object?>
        {
            ["customer_id"] = saleReturn.GetValueOrDefault("customer_id"),
            ["sale_invoice_id"] = null,
            ["sale_return_id"] = srId,
            ["entry_type"] = "payment",
            ["is_manual"] = 0,
            ["entry_date"] = entryDate,
            ["posted_number"] = postedNumber,
            ["invoice_total"] = 0,
            ["total_received"] = 0,
            ["balance_remaining"] = 0,
            ["credited_amount"] = amount,
            ["payment_ref"] = postedNumber,
            ["description"] = description,
            ["created_by"] = userId ?? saleReturn.GetValueOrDefault("user_id")
        };

        if (paymentId.HasValue && await _db.HasColumnAsync("customer_ledgers", "payment_id"))
        {
            attrs["payment_id"] = paymentId.Value;
        }

        var existing = await _db.QuerySingleOrDefaultAsync(
            "SELECT id FROM customer_ledgers WHERE sale_return_id = @srId LIMIT 1",
            new { srId },
            conn,
            tx);

        if (existing != null)
        {
            await _db.UpdateAsync("customer_ledgers", existing["id"]!, attrs, "id", conn, tx);
        }
        else
        {
            await _db.InsertAsync("customer_ledgers", attrs, conn, tx);
        }
    }

    private async Task SyncSupplierLedgerForPurchaseReturnAsync(
        Dictionary<string, object?> purchaseReturn,
        double amount,
        string mode,
        long? userId,
        IDbConnection? conn,
        IDbTransaction? tx)
    {
        var prId = ToLong(purchaseReturn.GetValueOrDefault("id"));
        if (amount <= 0)
        {
            await DeleteSupplierLedgerForPurchaseReturnAsync(prId, conn, tx);
            return;
        }

        var paymentId = await SyncPurchaseReturnCreditPaymentAsync(purchaseReturn, amount, mode, userId, conn, tx);

        if (!await _db.HasTableAsync("supplier_ledgers") || !await _db.HasColumnAsync("supplier_ledgers", "purchase_return_id"))
        {
            return;
        }

        string? invPosted = null;
        string? invNumber = null;
        var purchaseInvoiceId = ToLong(purchaseReturn.GetValueOrDefault("purchase_invoice_id"));
        if (purchaseInvoiceId > 0 && await _db.HasTableAsync("purchase_invoices"))
        {
            var inv = await _db.QuerySingleOrDefaultAsync(
                "SELECT id, posted_number, invoice_number FROM purchase_invoices WHERE id = @id",
                new { id = purchaseInvoiceId },
                conn,
                tx);
            invPosted = inv?.GetValueOrDefault("posted_number")?.ToString();
            invNumber = inv?.GetValueOrDefault("invoice_number")?.ToString();
        }

        var postedNumber = purchaseReturn.GetValueOrDefault("posted_number")?.ToString() ?? $"PR-{prId}";
        var label = mode == "invoice_offset" ? "Invoice Offset" : "Debit Note";
        var description = $"{label} - Purchase Return #{postedNumber}" + (!string.IsNullOrEmpty(invPosted) ? $" (Inv #{invPosted})" : "");
        var entryDate = NormalizeDateString(purchaseReturn.GetValueOrDefault("date"));

        var attrs = new Dictionary<string, object?>
        {
            ["supplier_id"] = purchaseReturn.GetValueOrDefault("supplier_id"),
            ["purchase_invoice_id"] = null,
            ["purchase_return_id"] = prId,
            ["entry_type"] = "payment",
            ["is_manual"] = 0,
            ["entry_date"] = entryDate,
            ["posted_number"] = postedNumber,
            ["invoice_number"] = invNumber,
            ["invoice_total"] = 0,
            ["total_paid"] = 0,
            ["debited_amount"] = amount,
            ["credit_remaining"] = 0,
            ["payment_ref"] = postedNumber,
            ["description"] = description,
            ["created_by"] = userId
        };

        if (paymentId.HasValue && await _db.HasColumnAsync("supplier_ledgers", "payment_id"))
        {
            attrs["payment_id"] = paymentId.Value;
        }

        var existing = await _db.QuerySingleOrDefaultAsync(
            "SELECT id FROM supplier_ledgers WHERE purchase_return_id = @prId LIMIT 1",
            new { prId },
            conn,
            tx);

        if (existing != null)
        {
            await _db.UpdateAsync("supplier_ledgers", existing["id"]!, attrs, "id", conn, tx);
        }
        else
        {
            await _db.InsertAsync("supplier_ledgers", attrs, conn, tx);
        }
    }

    public async Task<long?> SyncSaleReturnCreditPaymentAsync(
        Dictionary<string, object?> saleReturn,
        double amount,
        string mode = "credit_note",
        long? userId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        var customerId = ToLong(saleReturn.GetValueOrDefault("customer_id"));
        if (!await _db.HasTableAsync("payments") || !await _db.HasTableAsync("payment_allocations") || customerId <= 0)
        {
            return null;
        }

        var srId = ToLong(saleReturn.GetValueOrDefault("id"));
        amount = Math.Round(amount, 2);
        if (amount <= 0)
        {
            await DeleteSaleReturnCreditPaymentAsync(srId, conn, tx);
            return null;
        }

        var idempotencyKey = $"sale_return:{srId}";
        var entryDate = NormalizeDateString(saleReturn.GetValueOrDefault("date"));
        var postedNumber = saleReturn.GetValueOrDefault("posted_number")?.ToString() ?? $"SR-{srId:D4}";

        var payment = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM payments WHERE idempotency_key = @key LIMIT 1",
            new { key = idempotencyKey },
            conn,
            tx);

        long paymentId;
        if (payment == null)
        {
            var baseNo = "CN-" + postedNumber;
            var exists = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM payments WHERE payment_number = @no",
                new { no = baseNo },
                conn,
                tx);
            var paymentNumber = exists > 0 ? $"{baseNo}-{srId}" : baseNo;

            paymentId = await _db.InsertAsync("payments", new Dictionary<string, object?>
            {
                ["payment_number"] = paymentNumber,
                ["party_type"] = "customer",
                ["party_id"] = customerId,
                ["payment_date"] = entryDate,
                ["amount"] = amount,
                ["allocated_amount"] = 0,
                ["unapplied_amount"] = amount,
                ["payment_method"] = "credit_note",
                ["reference"] = postedNumber,
                ["status"] = "posted",
                ["notes"] = $"Credit Note from Sale Return #{postedNumber}",
                ["idempotency_key"] = idempotencyKey,
                ["created_by_id"] = userId ?? saleReturn.GetValueOrDefault("user_id")
            }, conn, tx);

            payment = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId }, conn, tx);
        }
        else
        {
            paymentId = ToLong(payment["id"]);
        }

        var existingAllocs = await _db.QueryListAsync(
            "SELECT * FROM payment_allocations WHERE payment_id = @pid ORDER BY id DESC",
            new { pid = paymentId },
            conn,
            tx);

        var saleInvoiceId = ToLong(saleReturn.GetValueOrDefault("sale_invoice_id"));
        if (mode != "invoice_offset" || ToLong(payment?.GetValueOrDefault("party_id")) != customerId)
        {
            foreach (var alloc in existingAllocs)
            {
                if (string.Equals(alloc.GetValueOrDefault("invoice_type")?.ToString(), "sale_invoice", StringComparison.OrdinalIgnoreCase))
                {
                    var invId = ToLong(alloc.GetValueOrDefault("invoice_id"));
                    var allocAmt = ToDouble(alloc.GetValueOrDefault("amount"));
                    await _db.ExecuteAsync(
                        "UPDATE sale_invoices SET total_receive = ROUND(MAX(COALESCE(total_receive, 0) - @amt, 0), 2) WHERE id = @invId",
                        new { amt = allocAmt, invId },
                        conn,
                        tx);
                }
                await _db.ExecuteAsync("DELETE FROM payment_allocations WHERE id = @id", new { id = alloc["id"] }, conn, tx);
            }
            existingAllocs.Clear();
        }
        else if (saleInvoiceId > 0)
        {
            foreach (var alloc in existingAllocs)
            {
                if (string.Equals(alloc.GetValueOrDefault("invoice_type")?.ToString(), "sale_invoice", StringComparison.OrdinalIgnoreCase) &&
                    ToLong(alloc.GetValueOrDefault("invoice_id")) != saleInvoiceId)
                {
                    var invId = ToLong(alloc.GetValueOrDefault("invoice_id"));
                    var allocAmt = ToDouble(alloc.GetValueOrDefault("amount"));
                    await _db.ExecuteAsync(
                        "UPDATE sale_invoices SET total_receive = ROUND(MAX(COALESCE(total_receive, 0) - @amt, 0), 2) WHERE id = @invId",
                        new { amt = allocAmt, invId },
                        conn,
                        tx);
                    await _db.ExecuteAsync("DELETE FROM payment_allocations WHERE id = @id", new { id = alloc["id"] }, conn, tx);
                }
            }
            existingAllocs = await _db.QueryListAsync(
                "SELECT * FROM payment_allocations WHERE payment_id = @pid ORDER BY id DESC",
                new { pid = paymentId },
                conn,
                tx);
        }

        var allocatedSum = Math.Round(existingAllocs.Sum(a => ToDouble(a.GetValueOrDefault("amount"))), 2);

        if (allocatedSum > amount)
        {
            var excessToTrim = Math.Round(allocatedSum - amount, 2);
            foreach (var alloc in existingAllocs)
            {
                if (excessToTrim <= 0) break;
                var curAllocAmt = ToDouble(alloc.GetValueOrDefault("amount"));
                var reduceBy = Math.Min(curAllocAmt, excessToTrim);
                if (reduceBy > 0)
                {
                    var invId = ToLong(alloc.GetValueOrDefault("invoice_id"));
                    await _db.ExecuteAsync(
                        "UPDATE sale_invoices SET total_receive = ROUND(MAX(COALESCE(total_receive, 0) - @amt, 0), 2) WHERE id = @invId",
                        new { amt = reduceBy, invId },
                        conn,
                        tx);

                    var newAllocAmt = Math.Round(curAllocAmt - reduceBy, 2);
                    if (newAllocAmt <= 0.001)
                    {
                        await _db.ExecuteAsync("DELETE FROM payment_allocations WHERE id = @id", new { id = alloc["id"] }, conn, tx);
                    }
                    else
                    {
                        await _db.UpdateAsync("payment_allocations", alloc["id"]!, new Dictionary<string, object?> { ["amount"] = newAllocAmt }, "id", conn, tx);
                    }
                    excessToTrim = Math.Round(excessToTrim - reduceBy, 2);
                }
            }
            allocatedSum = Math.Round(await _db.ExecuteScalarAsync<double?>(
                "SELECT COALESCE(SUM(amount), 0) FROM payment_allocations WHERE payment_id = @pid",
                new { pid = paymentId },
                conn,
                tx) ?? 0.0, 2);
        }

        if (mode == "invoice_offset" && saleInvoiceId > 0 && allocatedSum < amount)
        {
            var linkedInv = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM sale_invoices WHERE id = @id",
                new { id = saleInvoiceId },
                conn,
                tx);
            if (linkedInv != null &&
                ToLong(linkedInv.GetValueOrDefault("customer_id")) == customerId &&
                !string.Equals(linkedInv.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
            {
                var invTotal = ToDouble(linkedInv.GetValueOrDefault("total"));
                var invReceived = ToDouble(linkedInv.GetValueOrDefault("total_receive"));
                var invDue = Math.Round(Math.Max(invTotal - invReceived, 0.0), 2);
                var toAutoAlloc = Math.Round(Math.Min(amount - allocatedSum, invDue), 2);

                if (toAutoAlloc > 0)
                {
                    var existingAlloc = await _db.QuerySingleOrDefaultAsync(
                        "SELECT * FROM payment_allocations WHERE payment_id = @pid AND invoice_type = 'sale_invoice' AND invoice_id = @invId LIMIT 1",
                        new { pid = paymentId, invId = saleInvoiceId },
                        conn,
                        tx);

                    if (existingAlloc != null)
                    {
                        var nextAllocAmt = Math.Round(ToDouble(existingAlloc.GetValueOrDefault("amount")) + toAutoAlloc, 2);
                        await _db.UpdateAsync("payment_allocations", existingAlloc["id"]!, new Dictionary<string, object?> { ["amount"] = nextAllocAmt }, "id", conn, tx);
                    }
                    else
                    {
                        await _db.InsertAsync("payment_allocations", new Dictionary<string, object?>
                        {
                            ["payment_id"] = paymentId,
                            ["invoice_type"] = "sale_invoice",
                            ["invoice_id"] = saleInvoiceId,
                            ["amount"] = toAutoAlloc
                        }, conn, tx);
                    }

                    await _db.UpdateAsync("sale_invoices", saleInvoiceId, new Dictionary<string, object?>
                    {
                        ["total_receive"] = Math.Round(invReceived + toAutoAlloc, 2)
                    }, "id", conn, tx);

                    allocatedSum = Math.Round(allocatedSum + toAutoAlloc, 2);
                }
            }
        }

        await _db.UpdateAsync("payments", paymentId, new Dictionary<string, object?>
        {
            ["party_id"] = customerId,
            ["payment_date"] = entryDate,
            ["amount"] = amount,
            ["allocated_amount"] = allocatedSum,
            ["unapplied_amount"] = Math.Round(Math.Max(amount - allocatedSum, 0.0), 2),
            ["payment_method"] = "credit_note",
            ["reference"] = postedNumber,
            ["status"] = "posted",
            ["notes"] = $"Credit Note from Sale Return #{postedNumber}"
        }, "id", conn, tx);

        return paymentId;
    }

    private async Task DeleteSaleReturnCreditPaymentAsync(long saleReturnId, IDbConnection? conn, IDbTransaction? tx)
    {
        if (!await _db.HasTableAsync("payments") || !await _db.HasTableAsync("payment_allocations")) return;

        var payment = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM payments WHERE idempotency_key = @key LIMIT 1",
            new { key = $"sale_return:{saleReturnId}" },
            conn,
            tx);
        if (payment == null) return;

        var pid = ToLong(payment["id"]);
        var allocs = await _db.QueryListAsync(
            "SELECT * FROM payment_allocations WHERE payment_id = @pid",
            new { pid },
            conn,
            tx);

        foreach (var alloc in allocs)
        {
            if (string.Equals(alloc.GetValueOrDefault("invoice_type")?.ToString(), "sale_invoice", StringComparison.OrdinalIgnoreCase))
            {
                var invId = ToLong(alloc.GetValueOrDefault("invoice_id"));
                var amt = ToDouble(alloc.GetValueOrDefault("amount"));
                await _db.ExecuteAsync(
                    "UPDATE sale_invoices SET total_receive = ROUND(MAX(COALESCE(total_receive, 0) - @amt, 0), 2) WHERE id = @invId",
                    new { amt, invId },
                    conn,
                    tx);
            }
        }

        await _db.ExecuteAsync("DELETE FROM payment_allocations WHERE payment_id = @pid", new { pid }, conn, tx);
        await _db.ExecuteAsync("DELETE FROM payments WHERE id = @pid", new { pid }, conn, tx);
    }

    public async Task<long?> SyncPurchaseReturnCreditPaymentAsync(
        Dictionary<string, object?> purchaseReturn,
        double amount,
        string mode = "credit_note",
        long? userId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        var supplierId = ToLong(purchaseReturn.GetValueOrDefault("supplier_id"));
        if (!await _db.HasTableAsync("payments") || !await _db.HasTableAsync("payment_allocations") || supplierId <= 0)
        {
            return null;
        }

        var prId = ToLong(purchaseReturn.GetValueOrDefault("id"));
        amount = Math.Round(amount, 2);
        if (amount <= 0)
        {
            await DeletePurchaseReturnCreditPaymentAsync(prId, conn, tx);
            return null;
        }

        var idempotencyKey = $"purchase_return:{prId}";
        var entryDate = NormalizeDateString(purchaseReturn.GetValueOrDefault("date"));
        var postedNumber = purchaseReturn.GetValueOrDefault("posted_number")?.ToString() ?? $"PR-{prId:D4}";

        var payment = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM payments WHERE idempotency_key = @key LIMIT 1",
            new { key = idempotencyKey },
            conn,
            tx);

        long paymentId;
        if (payment == null)
        {
            var baseNo = "DN-" + postedNumber;
            var exists = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM payments WHERE payment_number = @no",
                new { no = baseNo },
                conn,
                tx);
            var paymentNumber = exists > 0 ? $"{baseNo}-{prId}" : baseNo;

            paymentId = await _db.InsertAsync("payments", new Dictionary<string, object?>
            {
                ["payment_number"] = paymentNumber,
                ["party_type"] = "supplier",
                ["party_id"] = supplierId,
                ["payment_date"] = entryDate,
                ["amount"] = amount,
                ["allocated_amount"] = 0,
                ["unapplied_amount"] = amount,
                ["payment_method"] = "credit_note",
                ["reference"] = postedNumber,
                ["status"] = "posted",
                ["notes"] = $"Debit Note from Purchase Return #{postedNumber}",
                ["idempotency_key"] = idempotencyKey,
                ["created_by_id"] = userId
            }, conn, tx);

            payment = await _db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId }, conn, tx);
        }
        else
        {
            paymentId = ToLong(payment["id"]);
        }

        var existingAllocs = await _db.QueryListAsync(
            "SELECT * FROM payment_allocations WHERE payment_id = @pid ORDER BY id DESC",
            new { pid = paymentId },
            conn,
            tx);

        var purchaseInvoiceId = ToLong(purchaseReturn.GetValueOrDefault("purchase_invoice_id"));
        if (mode != "invoice_offset" || ToLong(payment?.GetValueOrDefault("party_id")) != supplierId)
        {
            foreach (var alloc in existingAllocs)
            {
                if (string.Equals(alloc.GetValueOrDefault("invoice_type")?.ToString(), "purchase_invoice", StringComparison.OrdinalIgnoreCase))
                {
                    var invId = ToLong(alloc.GetValueOrDefault("invoice_id"));
                    var allocAmt = ToDouble(alloc.GetValueOrDefault("amount"));
                    await _db.ExecuteAsync(
                        "UPDATE purchase_invoices SET total_paid = ROUND(MAX(COALESCE(total_paid, 0) - @amt, 0), 2) WHERE id = @invId",
                        new { amt = allocAmt, invId },
                        conn,
                        tx);
                }
                await _db.ExecuteAsync("DELETE FROM payment_allocations WHERE id = @id", new { id = alloc["id"] }, conn, tx);
            }
            existingAllocs.Clear();
        }
        else if (purchaseInvoiceId > 0)
        {
            foreach (var alloc in existingAllocs)
            {
                if (string.Equals(alloc.GetValueOrDefault("invoice_type")?.ToString(), "purchase_invoice", StringComparison.OrdinalIgnoreCase) &&
                    ToLong(alloc.GetValueOrDefault("invoice_id")) != purchaseInvoiceId)
                {
                    var invId = ToLong(alloc.GetValueOrDefault("invoice_id"));
                    var allocAmt = ToDouble(alloc.GetValueOrDefault("amount"));
                    await _db.ExecuteAsync(
                        "UPDATE purchase_invoices SET total_paid = ROUND(MAX(COALESCE(total_paid, 0) - @amt, 0), 2) WHERE id = @invId",
                        new { amt = allocAmt, invId },
                        conn,
                        tx);
                    await _db.ExecuteAsync("DELETE FROM payment_allocations WHERE id = @id", new { id = alloc["id"] }, conn, tx);
                }
            }
            existingAllocs = await _db.QueryListAsync(
                "SELECT * FROM payment_allocations WHERE payment_id = @pid ORDER BY id DESC",
                new { pid = paymentId },
                conn,
                tx);
        }

        var allocatedSum = Math.Round(existingAllocs.Sum(a => ToDouble(a.GetValueOrDefault("amount"))), 2);

        if (allocatedSum > amount)
        {
            var excessToTrim = Math.Round(allocatedSum - amount, 2);
            foreach (var alloc in existingAllocs)
            {
                if (excessToTrim <= 0) break;
                var curAllocAmt = ToDouble(alloc.GetValueOrDefault("amount"));
                var reduceBy = Math.Min(curAllocAmt, excessToTrim);
                if (reduceBy > 0)
                {
                    var invId = ToLong(alloc.GetValueOrDefault("invoice_id"));
                    await _db.ExecuteAsync(
                        "UPDATE purchase_invoices SET total_paid = ROUND(MAX(COALESCE(total_paid, 0) - @amt, 0), 2) WHERE id = @invId",
                        new { amt = reduceBy, invId },
                        conn,
                        tx);

                    var newAllocAmt = Math.Round(curAllocAmt - reduceBy, 2);
                    if (newAllocAmt <= 0.001)
                    {
                        await _db.ExecuteAsync("DELETE FROM payment_allocations WHERE id = @id", new { id = alloc["id"] }, conn, tx);
                    }
                    else
                    {
                        await _db.UpdateAsync("payment_allocations", alloc["id"]!, new Dictionary<string, object?> { ["amount"] = newAllocAmt }, "id", conn, tx);
                    }
                    excessToTrim = Math.Round(excessToTrim - reduceBy, 2);
                }
            }
            allocatedSum = Math.Round(await _db.ExecuteScalarAsync<double?>(
                "SELECT COALESCE(SUM(amount), 0) FROM payment_allocations WHERE payment_id = @pid",
                new { pid = paymentId },
                conn,
                tx) ?? 0.0, 2);
        }

        if (mode == "invoice_offset" && purchaseInvoiceId > 0 && allocatedSum < amount)
        {
            var linkedInv = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM purchase_invoices WHERE id = @id",
                new { id = purchaseInvoiceId },
                conn,
                tx);
            if (linkedInv != null &&
                ToLong(linkedInv.GetValueOrDefault("supplier_id")) == supplierId &&
                !string.Equals(linkedInv.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
            {
                var invTotal = ToDouble(linkedInv.GetValueOrDefault("total_amount"));
                var invPaid = ToDouble(linkedInv.GetValueOrDefault("total_paid"));
                var invDue = Math.Round(Math.Max(invTotal - invPaid, 0.0), 2);
                var toAutoAlloc = Math.Round(Math.Min(amount - allocatedSum, invDue), 2);

                if (toAutoAlloc > 0)
                {
                    var existingAlloc = await _db.QuerySingleOrDefaultAsync(
                        "SELECT * FROM payment_allocations WHERE payment_id = @pid AND invoice_type = 'purchase_invoice' AND invoice_id = @invId LIMIT 1",
                        new { pid = paymentId, invId = purchaseInvoiceId },
                        conn,
                        tx);

                    if (existingAlloc != null)
                    {
                        var nextAllocAmt = Math.Round(ToDouble(existingAlloc.GetValueOrDefault("amount")) + toAutoAlloc, 2);
                        await _db.UpdateAsync("payment_allocations", existingAlloc["id"]!, new Dictionary<string, object?> { ["amount"] = nextAllocAmt }, "id", conn, tx);
                    }
                    else
                    {
                        await _db.InsertAsync("payment_allocations", new Dictionary<string, object?>
                        {
                            ["payment_id"] = paymentId,
                            ["invoice_type"] = "purchase_invoice",
                            ["invoice_id"] = purchaseInvoiceId,
                            ["amount"] = toAutoAlloc
                        }, conn, tx);
                    }

                    await _db.UpdateAsync("purchase_invoices", purchaseInvoiceId, new Dictionary<string, object?>
                    {
                        ["total_paid"] = Math.Round(invPaid + toAutoAlloc, 2)
                    }, "id", conn, tx);

                    allocatedSum = Math.Round(allocatedSum + toAutoAlloc, 2);
                }
            }
        }

        await _db.UpdateAsync("payments", paymentId, new Dictionary<string, object?>
        {
            ["party_id"] = supplierId,
            ["payment_date"] = entryDate,
            ["amount"] = amount,
            ["allocated_amount"] = allocatedSum,
            ["unapplied_amount"] = Math.Round(Math.Max(amount - allocatedSum, 0.0), 2),
            ["payment_method"] = "credit_note",
            ["reference"] = postedNumber,
            ["status"] = "posted",
            ["notes"] = $"Debit Note from Purchase Return #{postedNumber}"
        }, "id", conn, tx);

        return paymentId;
    }

    private async Task DeletePurchaseReturnCreditPaymentAsync(long purchaseReturnId, IDbConnection? conn, IDbTransaction? tx)
    {
        if (!await _db.HasTableAsync("payments") || !await _db.HasTableAsync("payment_allocations")) return;

        var payment = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM payments WHERE idempotency_key = @key LIMIT 1",
            new { key = $"purchase_return:{purchaseReturnId}" },
            conn,
            tx);
        if (payment == null) return;

        var pid = ToLong(payment["id"]);
        var allocs = await _db.QueryListAsync(
            "SELECT * FROM payment_allocations WHERE payment_id = @pid",
            new { pid },
            conn,
            tx);

        foreach (var alloc in allocs)
        {
            if (string.Equals(alloc.GetValueOrDefault("invoice_type")?.ToString(), "purchase_invoice", StringComparison.OrdinalIgnoreCase))
            {
                var invId = ToLong(alloc.GetValueOrDefault("invoice_id"));
                var amt = ToDouble(alloc.GetValueOrDefault("amount"));
                await _db.ExecuteAsync(
                    "UPDATE purchase_invoices SET total_paid = ROUND(MAX(COALESCE(total_paid, 0) - @amt, 0), 2) WHERE id = @invId",
                    new { amt, invId },
                    conn,
                    tx);
            }
        }

        await _db.ExecuteAsync("DELETE FROM payment_allocations WHERE payment_id = @pid", new { pid }, conn, tx);
        await _db.ExecuteAsync("DELETE FROM payments WHERE id = @pid", new { pid }, conn, tx);
    }

    // -------------------------------------------------------------------------
    // Account & Cash Drawer Direct SQL Operations
    // -------------------------------------------------------------------------

    public async Task<Dictionary<string, object?>?> ResolveAccountAsync(
        long? explicitAccountId,
        string? paymentMethod = "cash",
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        if (!await _db.HasTableAsync("accounts")) return null;

        if (explicitAccountId.HasValue && explicitAccountId.Value > 0)
        {
            var explicitAcc = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM accounts WHERE id = @id",
                new { id = explicitAccountId.Value },
                conn,
                tx);
            if (explicitAcc != null) return explicitAcc;
        }

        var targetType = (paymentMethod ?? "cash").ToLowerInvariant() switch
        {
            "cash" => "cash",
            "mobile_wallet" => "mobile_wallet",
            _ => "bank"
        };

        var byDefault = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM accounts WHERE type = @type AND is_active = 1 ORDER BY is_default DESC, id ASC LIMIT 1",
            new { type = targetType },
            conn,
            tx);
        if (byDefault != null) return byDefault;

        return await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM accounts WHERE is_active = 1 ORDER BY is_default DESC, id ASC LIMIT 1",
            null,
            conn,
            tx);
    }

    public static bool IsDefaultCashDrawerAccount(Dictionary<string, object?>? account, string? paymentMethod = null)
    {
        if (account != null)
        {
            var type = account.GetValueOrDefault("type")?.ToString() ?? "";
            var code = account.GetValueOrDefault("code")?.ToString() ?? "";
            return string.Equals(type, "cash", StringComparison.OrdinalIgnoreCase) &&
                   (string.Equals(code, "CASH-DRAWER", StringComparison.OrdinalIgnoreCase) || IsTruthy(account.GetValueOrDefault("is_default")));
        }

        return string.Equals(paymentMethod ?? "cash", "cash", StringComparison.OrdinalIgnoreCase);
    }

    public async Task DepositToAccountAsync(
        Dictionary<string, object?> account,
        double amount,
        string referenceType,
        long referenceId,
        string description,
        long? userId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        if (amount <= 0 || !await _db.HasTableAsync("accounts")) return;

        var accId = ToLong(account["id"]);
        var current = ToDouble(await _db.ExecuteScalarAsync<object?>(
            "SELECT current_balance FROM accounts WHERE id = @id",
            new { id = accId },
            conn,
            tx));
        var nextBalance = Math.Round(current + amount, 2);

        await _db.UpdateAsync("accounts", accId, new Dictionary<string, object?>
        {
            ["current_balance"] = nextBalance
        }, "id", conn, tx);

        if (await _db.HasTableAsync("account_transactions"))
        {
            await _db.InsertAsync("account_transactions", new Dictionary<string, object?>
            {
                ["account_id"] = accId,
                ["user_id"] = userId,
                ["type"] = "deposit",
                ["amount"] = Math.Round(amount, 2),
                ["balance_after"] = nextBalance,
                ["transaction_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                ["reference_type"] = referenceType,
                ["reference_id"] = referenceId,
                ["description"] = description
            }, conn, tx);
        }
    }

    public async Task WithdrawFromAccountAsync(
        Dictionary<string, object?> account,
        double amount,
        string referenceType,
        long referenceId,
        string description,
        long? userId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        if (amount <= 0 || !await _db.HasTableAsync("accounts")) return;

        var accId = ToLong(account["id"]);
        var current = ToDouble(await _db.ExecuteScalarAsync<object?>(
            "SELECT current_balance FROM accounts WHERE id = @id",
            new { id = accId },
            conn,
            tx));
        var nextBalance = Math.Round(current - amount, 2);

        await _db.UpdateAsync("accounts", accId, new Dictionary<string, object?>
        {
            ["current_balance"] = nextBalance
        }, "id", conn, tx);

        if (await _db.HasTableAsync("account_transactions"))
        {
            await _db.InsertAsync("account_transactions", new Dictionary<string, object?>
            {
                ["account_id"] = accId,
                ["user_id"] = userId,
                ["type"] = "withdrawal",
                ["amount"] = Math.Round(amount, 2),
                ["balance_after"] = nextBalance,
                ["transaction_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                ["reference_type"] = referenceType,
                ["reference_id"] = referenceId,
                ["description"] = description
            }, conn, tx);
        }
    }

    public async Task RecordCashDrawerTransactionAsync(
        string type,
        double amount,
        string referenceType,
        long referenceId,
        string? notes = null,
        long? userId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        if (amount <= 0 || !await _db.HasTableAsync("cash_drawer_sessions") || !await _db.HasTableAsync("cash_drawer_transactions"))
        {
            return;
        }

        var session = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM cash_drawer_sessions WHERE status = 'open' ORDER BY id DESC LIMIT 1",
            null,
            conn,
            tx);
        if (session == null) return;

        var sessionId = ToLong(session["id"]);
        await _db.InsertAsync("cash_drawer_transactions", new Dictionary<string, object?>
        {
            ["session_id"] = sessionId,
            ["user_id"] = userId ?? session.GetValueOrDefault("user_id"),
            ["type"] = type,
            ["amount"] = Math.Round(amount, 2),
            ["reference_type"] = referenceType,
            ["reference_id"] = referenceId,
            ["notes"] = notes
        }, conn, tx);
    }

    private async Task<int> RemoveCashDrawerTransactionForRefAsync(
        string referenceType,
        long referenceId,
        string type,
        IDbConnection? conn,
        IDbTransaction? tx)
    {
        if (!await _db.HasTableAsync("cash_drawer_transactions") || !await _db.HasTableAsync("cash_drawer_sessions")) return 0;
        // Never mutate closed historical shifts — only remove drawer entries inside the currently open session
        return await _db.ExecuteAsync(
            @"DELETE FROM cash_drawer_transactions
              WHERE reference_type = @refType
                AND reference_id = @refId
                AND type = @type
                AND session_id IN (SELECT id FROM cash_drawer_sessions WHERE status = 'open')",
            new { refType = referenceType, refId = referenceId, type },
            conn,
            tx);
    }

    private async Task<bool> HasAnyCashDrawerTransactionForRefAsync(
        string referenceType,
        long referenceId,
        IDbConnection? conn,
        IDbTransaction? tx)
    {
        if (!await _db.HasTableAsync("cash_drawer_transactions")) return false;
        var count = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM cash_drawer_transactions WHERE reference_type = @refType AND reference_id = @refId",
            new { refType = referenceType, refId = referenceId },
            conn,
            tx);
        return count > 0;
    }

    private static string NormalizeDateString(object? dateVal)
    {
        var s = (dateVal?.ToString() ?? "").Trim();
        if (string.IsNullOrEmpty(s)) return DateTime.UtcNow.ToString("yyyy-MM-dd");
        return s.Length > 10 ? s.Substring(0, 10) : s;
    }

    private static bool IsTruthy(object? val)
    {
        if (val == null) return false;
        if (val is bool b) return b;
        if (val is long l) return l != 0;
        if (val is int i) return i != 0;
        if (val is string s) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static long ToLong(object? val)
    {
        if (val == null) return 0;
        if (val is long l) return l;
        if (val is int i) return i;
        return long.TryParse(val.ToString(), out var p) ? p : 0;
    }

    private static double ToDouble(object? val)
    {
        if (val == null) return 0.0;
        if (val is double d) return d;
        if (val is decimal dec) return (double)dec;
        if (val is float f) return f;
        if (val is long l) return l;
        if (val is int i) return i;
        return double.TryParse(val.ToString(), out var p) ? p : 0.0;
    }
}

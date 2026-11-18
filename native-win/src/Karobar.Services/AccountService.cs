using Dapper;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class AccountService
{
    private readonly KarobarDb _db;

    public static readonly IReadOnlyDictionary<string, string> MethodTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cash"] = "cash",
        ["bank_transfer"] = "bank",
        ["bank"] = "bank",
        ["card"] = "card",
        ["mobile_wallet"] = "mobile_wallet",
        ["cheque"] = "bank",
        ["other"] = "other"
    };

    public AccountService(KarobarDb db)
    {
        _db = db;
    }

    public bool IsDefaultCashDrawerAccount(Dictionary<string, object?>? account, string? fallbackMethod = null)
    {
        if (account != null)
        {
            var type = account.TryGetValue("type", out var t) ? t?.ToString() ?? "" : "";
            var isDef = IsTruthy(account.TryGetValue("is_default", out var d) ? d : null);
            return type.Equals("cash", StringComparison.OrdinalIgnoreCase) && isDef;
        }

        return string.IsNullOrWhiteSpace(fallbackMethod) || fallbackMethod.Equals("cash", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> IsPreShiftDocumentForOpenDrawerAsync(Dictionary<string, object?> model, string referenceType, long? userId = null)
    {
        if (!await _db.HasTableAsync("cash_drawer_sessions") || !await _db.HasTableAsync("cash_drawer_transactions"))
        {
            return false;
        }

        var drawerService = new CashDrawerService(_db);
        var session = await drawerService.GetCurrentSessionAsync(userId, false);
        if (session == null || !string.Equals(session.GetValueOrDefault("status")?.ToString(), "open", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sessId = ToLong(session.GetValueOrDefault("id"));
        var modelId = ToLong(model.GetValueOrDefault("id"));

        var hasSameShiftTxn = await _db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM cash_drawer_transactions WHERE session_id = @sessId AND reference_type = @referenceType AND reference_id = @modelId",
            new { sessId, referenceType, modelId }) > 0;

        if (hasSameShiftTxn)
        {
            return false;
        }

        if (DateTime.TryParse(model.GetValueOrDefault("created_at")?.ToString(), out var createdAt) &&
            DateTime.TryParse(session.GetValueOrDefault("opened_at")?.ToString(), out var openedAt))
        {
            return createdAt < openedAt;
        }

        return false;
    }

    public async Task<Dictionary<string, object?>?> SyncCashDrawerBalanceAsync(Dictionary<string, object?>? session = null)
    {
        if (!await _db.HasTableAsync("accounts"))
        {
            return null;
        }

        var cashAccount = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM accounts WHERE type = 'cash' AND is_default = 1 ORDER BY id ASC LIMIT 1");

        cashAccount ??= await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM accounts WHERE type = 'cash' ORDER BY id ASC LIMIT 1");

        if (cashAccount == null)
        {
            var id = await _db.InsertAsync("accounts", new Dictionary<string, object?>
            {
                ["name"] = "Cash in Hand (Drawer)",
                ["type"] = "cash",
                ["code"] = "cash_drawer",
                ["icon"] = "drawer",
                ["opening_balance"] = 0m,
                ["current_balance"] = 0m,
                ["is_active"] = true,
                ["is_default"] = true,
                ["notes"] = "Primary cash counter / drawer balance"
            });

            cashAccount = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id });
            if (cashAccount != null)
            {
                cashAccount["was_recently_created"] = true;
            }
        }
        else if (Math.Abs(ToDecimal(cashAccount.GetValueOrDefault("opening_balance"))) > 0.0001m)
        {
            var id = ToLong(cashAccount["id"]);
            await _db.UpdateAsync("accounts", id, new Dictionary<string, object?>
            {
                ["opening_balance"] = 0m
            });
            cashAccount["opening_balance"] = 0m;
        }

        return cashAccount;
    }

    public async Task<Dictionary<string, object?>?> ReconcileWithOpenCashDrawersAsync(long? userId = null)
    {
        var cashAccount = await SyncCashDrawerBalanceAsync();
        if (cashAccount == null || !await _db.HasTableAsync("cash_drawer_sessions") || !await _db.HasTableAsync("cash_drawer_transactions"))
        {
            return cashAccount;
        }

        var openSessions = await _db.QueryListAsync("SELECT * FROM cash_drawer_sessions WHERE status = 'open' ORDER BY id ASC");
        if (openSessions.Count == 0)
        {
            return await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = cashAccount["id"] });
        }

        var drawerService = new CashDrawerService(_db);
        decimal totalOpenExpected = 0m;
        foreach (var sess in openSessions)
        {
            totalOpenExpected += await drawerService.RecalculateExpectedBalanceAsync(ToLong(sess["id"]), syncAccount: false);
        }
        totalOpenExpected = Math.Round(totalOpenExpected, 2);

        var cashId = ToLong(cashAccount["id"]);
        var lockedAccount = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = cashId });
        if (lockedAccount == null) return cashAccount;

        if (Math.Abs(ToDecimal(lockedAccount.GetValueOrDefault("opening_balance"))) > 0.0001m)
        {
            await _db.UpdateAsync("accounts", cashId, new Dictionary<string, object?> { ["opening_balance"] = 0m });
        }

        var currentBalance = ToDecimal(lockedAccount.GetValueOrDefault("current_balance"));
        var delta = Math.Round(totalOpenExpected - currentBalance, 2);
        var primarySessionId = ToLong(openSessions[0]["id"]);

        if (delta > 0.009m)
        {
            await DepositAsync(
                lockedAccount,
                delta,
                "cash_drawer_reconcile",
                primarySessionId,
                $"Auto-reconciled Cash in Hand with active Cash Drawer (Shift #{primarySessionId})",
                userId);
        }
        else if (delta < -0.009m)
        {
            await WithdrawAsync(
                lockedAccount,
                Math.Abs(delta),
                "cash_drawer_reconcile",
                primarySessionId,
                $"Auto-reconciled Cash in Hand with active Cash Drawer (Shift #{primarySessionId})",
                userId);
        }

        return await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = cashId });
    }

    public async Task<Dictionary<string, object?>?> GetDefaultAccountAsync(string method = "cash")
    {
        if (!await _db.HasTableAsync("accounts"))
        {
            return null;
        }

        var type = MethodTypeMap.TryGetValue(method ?? "cash", out var mapped) ? mapped : "cash";
        if (type == "cash")
        {
            await SyncCashDrawerBalanceAsync();
        }

        var account = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM accounts WHERE is_active = 1 AND type = @type AND is_default = 1 ORDER BY id ASC LIMIT 1",
            new { type });

        account ??= await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM accounts WHERE is_active = 1 AND type = @type ORDER BY id ASC LIMIT 1",
            new { type });

        if (account == null)
        {
            account = await SyncCashDrawerBalanceAsync()
                      ?? await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE is_active = 1 ORDER BY id ASC LIMIT 1");
        }

        return account;
    }

    public async Task<Dictionary<string, object?>?> ResolveAccountAsync(long? accountId, string fallbackMethod = "cash")
    {
        if (!await _db.HasTableAsync("accounts"))
        {
            return null;
        }

        if (accountId.HasValue && accountId.Value > 0)
        {
            var acc = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accountId.Value });
            if (acc != null)
            {
                return acc;
            }
        }

        return await GetDefaultAccountAsync(fallbackMethod);
    }

    public async Task<Dictionary<string, object?>?> DepositAsync(
        Dictionary<string, object?>? account,
        decimal amount,
        string refType,
        long? refId = null,
        string? description = null,
        long? userId = null)
    {
        if (account == null) return null;
        return await DepositAsync(ToLong(account["id"]), amount, refType, refId, description, userId);
    }

    public async Task<Dictionary<string, object?>?> DepositAsync(
        long accountId,
        decimal amount,
        string refType,
        long? refId = null,
        string? description = null,
        long? userId = null)
    {
        if (accountId <= 0 || !await _db.HasTableAsync("accounts"))
        {
            return null;
        }

        amount = Math.Round(amount, 2);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Deposit amount must be greater than zero.");
        }

        var locked = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accountId });
        if (locked == null) return null;

        var newBalance = Math.Round(ToDecimal(locked.GetValueOrDefault("current_balance")) + amount, 2);
        await _db.UpdateAsync("accounts", accountId, new Dictionary<string, object?>
        {
            ["current_balance"] = newBalance
        });

        if (!await _db.HasTableAsync("account_transactions"))
        {
            return null;
        }

        var accName = locked.GetValueOrDefault("name")?.ToString() ?? "Account";
        var txnId = await _db.InsertAsync("account_transactions", new Dictionary<string, object?>
        {
            ["account_id"] = accountId,
            ["user_id"] = userId,
            ["type"] = "deposit",
            ["amount"] = amount,
            ["balance_after"] = newBalance,
            ["reference_type"] = refType,
            ["reference_id"] = refId,
            ["description"] = description ?? $"Deposit to {accName}",
            ["transaction_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        });

        return await _db.QuerySingleOrDefaultAsync("SELECT * FROM account_transactions WHERE id = @id", new { id = txnId });
    }

    public async Task<Dictionary<string, object?>?> WithdrawAsync(
        Dictionary<string, object?>? account,
        decimal amount,
        string refType,
        long? refId = null,
        string? description = null,
        long? userId = null)
    {
        if (account == null) return null;
        return await WithdrawAsync(ToLong(account["id"]), amount, refType, refId, description, userId);
    }

    public async Task<Dictionary<string, object?>?> WithdrawAsync(
        long accountId,
        decimal amount,
        string refType,
        long? refId = null,
        string? description = null,
        long? userId = null)
    {
        if (accountId <= 0 || !await _db.HasTableAsync("accounts"))
        {
            return null;
        }

        amount = Math.Round(amount, 2);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Withdrawal amount must be greater than zero.");
        }

        var locked = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accountId });
        if (locked == null) return null;

        var newBalance = Math.Round(ToDecimal(locked.GetValueOrDefault("current_balance")) - amount, 2);
        await _db.UpdateAsync("accounts", accountId, new Dictionary<string, object?>
        {
            ["current_balance"] = newBalance
        });

        if (!await _db.HasTableAsync("account_transactions"))
        {
            return null;
        }

        var accName = locked.GetValueOrDefault("name")?.ToString() ?? "Account";
        var txnId = await _db.InsertAsync("account_transactions", new Dictionary<string, object?>
        {
            ["account_id"] = accountId,
            ["user_id"] = userId,
            ["type"] = "withdrawal",
            ["amount"] = amount,
            ["balance_after"] = newBalance,
            ["reference_type"] = refType,
            ["reference_id"] = refId,
            ["description"] = description ?? $"Withdrawal from {accName}",
            ["transaction_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        });

        return await _db.QuerySingleOrDefaultAsync("SELECT * FROM account_transactions WHERE id = @id", new { id = txnId });
    }

    public async Task<Dictionary<string, object?>> TransferAsync(
        long fromAccountId,
        long toAccountId,
        decimal amount,
        string? notes = null,
        long? userId = null)
    {
        if (fromAccountId == toAccountId)
        {
            throw new InvalidOperationException("Source and destination accounts must be different.");
        }

        amount = Math.Round(amount, 2);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Transfer amount must be greater than zero.");
        }

        var fromLocked = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = fromAccountId })
                         ?? throw new KeyNotFoundException("Source account not found.");
        var toLocked = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = toAccountId })
                       ?? throw new KeyNotFoundException("Destination account not found.");

        var fromBalanceAfter = Math.Round(ToDecimal(fromLocked.GetValueOrDefault("current_balance")) - amount, 2);
        var toBalanceAfter = Math.Round(ToDecimal(toLocked.GetValueOrDefault("current_balance")) + amount, 2);

        await _db.UpdateAsync("accounts", fromAccountId, new Dictionary<string, object?> { ["current_balance"] = fromBalanceAfter });
        await _db.UpdateAsync("accounts", toAccountId, new Dictionary<string, object?> { ["current_balance"] = toBalanceAfter });

        var fromName = fromLocked.GetValueOrDefault("name")?.ToString() ?? "Account";
        var toName = toLocked.GetValueOrDefault("name")?.ToString() ?? "Account";
        var desc = !string.IsNullOrWhiteSpace(notes) ? $" - {notes}" : "";
        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var outTxnId = await _db.InsertAsync("account_transactions", new Dictionary<string, object?>
        {
            ["account_id"] = fromAccountId,
            ["user_id"] = userId,
            ["type"] = "transfer_out",
            ["amount"] = amount,
            ["balance_after"] = fromBalanceAfter,
            ["reference_type"] = "transfer",
            ["reference_id"] = toAccountId,
            ["description"] = $"Transfer to {toName}{desc}",
            ["transaction_date"] = nowStr
        });

        var inTxnId = await _db.InsertAsync("account_transactions", new Dictionary<string, object?>
        {
            ["account_id"] = toAccountId,
            ["user_id"] = userId,
            ["type"] = "transfer_in",
            ["amount"] = amount,
            ["balance_after"] = toBalanceAfter,
            ["reference_type"] = "transfer",
            ["reference_id"] = fromAccountId,
            ["description"] = $"Transfer from {fromName}{desc}",
            ["transaction_date"] = nowStr
        });

        var cashDrawerService = new CashDrawerService(_db);
        if (IsDefaultCashDrawerAccount(fromLocked))
        {
            await cashDrawerService.RecordTransactionAsync(
                null,
                "cash_out",
                amount,
                "account_transfer",
                outTxnId,
                $"Transfer to {toName}{desc}",
                userId);
        }
        if (IsDefaultCashDrawerAccount(toLocked))
        {
            await cashDrawerService.RecordTransactionAsync(
                null,
                "cash_in",
                amount,
                "account_transfer",
                inTxnId,
                $"Transfer from {fromName}{desc}",
                userId);
        }

        return new Dictionary<string, object?>
        {
            ["from_transaction"] = await _db.QuerySingleOrDefaultAsync("SELECT * FROM account_transactions WHERE id = @id", new { id = outTxnId }),
            ["to_transaction"] = await _db.QuerySingleOrDefaultAsync("SELECT * FROM account_transactions WHERE id = @id", new { id = inTxnId }),
            ["from_account"] = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = fromAccountId }),
            ["to_account"] = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = toAccountId })
        };
    }

    public async Task<Dictionary<string, object?>?> AdjustAsync(
        long accountId,
        string type,
        decimal amount,
        string reason,
        long? userId = null)
    {
        amount = Math.Round(amount, 2);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Adjustment amount must be greater than zero.");
        }

        var locked = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accountId })
                     ?? throw new KeyNotFoundException("Account not found.");

        decimal newBalance;
        string desc;
        if (string.Equals(type, "deposit", StringComparison.OrdinalIgnoreCase))
        {
            newBalance = Math.Round(ToDecimal(locked.GetValueOrDefault("current_balance")) + amount, 2);
            desc = $"Manual deposit / adjustment: {reason}";
        }
        else
        {
            newBalance = Math.Round(ToDecimal(locked.GetValueOrDefault("current_balance")) - amount, 2);
            desc = $"Manual expense / withdrawal: {reason}";
        }

        await _db.UpdateAsync("accounts", accountId, new Dictionary<string, object?>
        {
            ["current_balance"] = newBalance
        });

        var txnId = await _db.InsertAsync("account_transactions", new Dictionary<string, object?>
        {
            ["account_id"] = accountId,
            ["user_id"] = userId,
            ["type"] = "adjustment",
            ["amount"] = amount,
            ["balance_after"] = newBalance,
            ["reference_type"] = "manual",
            ["reference_id"] = null,
            ["description"] = desc,
            ["transaction_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        });

        if (IsDefaultCashDrawerAccount(locked))
        {
            var cashDrawerService = new CashDrawerService(_db);
            if (string.Equals(type, "deposit", StringComparison.OrdinalIgnoreCase))
            {
                await cashDrawerService.RecordTransactionAsync(null, "cash_in", amount, "account_adjustment", txnId, desc, userId);
            }
            else
            {
                await cashDrawerService.RecordTransactionAsync(null, "cash_out", amount, "account_adjustment", txnId, desc, userId);
            }
        }

        return await _db.QuerySingleOrDefaultAsync("SELECT * FROM account_transactions WHERE id = @id", new { id = txnId });
    }

    public async Task<Dictionary<string, object?>> GetSummaryAsync(long? userId = null)
    {
        await ReconcileWithOpenCashDrawersAsync(userId);

        var accounts = await _db.HasTableAsync("accounts")
            ? await _db.QueryListAsync("SELECT * FROM accounts ORDER BY type ASC, name ASC")
            : new List<Dictionary<string, object?>>();

        decimal totalBalance = 0m;
        var byType = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var acc in accounts)
        {
            var bal = ToDecimal(acc.GetValueOrDefault("current_balance"));
            var isActive = IsTruthy(acc.GetValueOrDefault("is_active"));
            if (isActive)
            {
                totalBalance += bal;
            }
            var type = acc.GetValueOrDefault("type")?.ToString() ?? "other";
            byType[type] = Math.Round((byType.TryGetValue(type, out var prev) ? prev : 0m) + bal, 2);
        }

        return new Dictionary<string, object?>
        {
            ["total_balance"] = Math.Round(totalBalance, 2),
            ["by_type"] = byType,
            ["accounts"] = accounts
        };
    }

    public async Task SyncSaleInvoiceUpdateAsync(
        Dictionary<string, object?> invoice,
        long? oldAccountId,
        string oldMethod,
        decimal oldReceived,
        long? newAccountId,
        string newMethod,
        decimal newReceived,
        long? userId = null,
        bool? updateDrawerIfPreShift = null)
    {
        if (!await _db.HasTableAsync("accounts")) return;

        var oldAcc = await ResolveAccountAsync(oldAccountId, oldMethod);
        var newAcc = await ResolveAccountAsync(newAccountId, newMethod);
        if (oldAcc == null || newAcc == null) return;

        var skipDrawerCash = updateDrawerIfPreShift == false &&
                             await IsPreShiftDocumentForOpenDrawerAsync(invoice, "sale_invoice", userId);

        var invId = ToLong(invoice.GetValueOrDefault("id"));
        var postedNo = invoice.GetValueOrDefault("posted_number")?.ToString() ?? $"#{invId}";
        var oldId = ToLong(oldAcc["id"]);
        var newId = ToLong(newAcc["id"]);

        if (oldId == newId)
        {
            if (skipDrawerCash && IsDefaultCashDrawerAccount(newAcc)) return;
            var delta = Math.Round(newReceived - oldReceived, 2);
            if (Math.Abs(delta) > 0.001m)
            {
                if (delta > 0m)
                {
                    await DepositAsync(newAcc, delta, "sale_invoice_update", invId, $"Sale Invoice #{postedNo} update (delta +{delta})", userId);
                }
                else
                {
                    await WithdrawAsync(newAcc, Math.Abs(delta), "sale_invoice_update", invId, $"Sale Invoice #{postedNo} update (delta {delta})", userId);
                }
            }
        }
        else
        {
            if (oldReceived > 0m && !(skipDrawerCash && IsDefaultCashDrawerAccount(oldAcc)))
            {
                await WithdrawAsync(oldAcc, oldReceived, "sale_invoice_update", invId, $"Sale Invoice #{postedNo} changed account from {oldAcc.GetValueOrDefault("name")}", userId);
            }
            if (newReceived > 0m && !(skipDrawerCash && IsDefaultCashDrawerAccount(newAcc)))
            {
                await DepositAsync(newAcc, newReceived, "sale_invoice_update", invId, $"Sale Invoice #{postedNo} changed account to {newAcc.GetValueOrDefault("name")}", userId);
            }
        }

        if (await _db.HasColumnAsync("sale_invoices", "account_id") && ToLong(invoice.GetValueOrDefault("account_id")) != newId)
        {
            await _db.UpdateAsync("sale_invoices", invId, new Dictionary<string, object?> { ["account_id"] = newId });
        }
    }

    public async Task SyncPurchaseInvoiceUpdateAsync(
        Dictionary<string, object?> invoice,
        long? oldAccountId,
        string oldMethod,
        decimal oldPaid,
        long? newAccountId,
        string newMethod,
        decimal newPaid,
        long? userId = null,
        bool? updateDrawerIfPreShift = null)
    {
        if (!await _db.HasTableAsync("accounts")) return;

        var oldAcc = await ResolveAccountAsync(oldAccountId, oldMethod);
        var newAcc = await ResolveAccountAsync(newAccountId, newMethod);
        if (oldAcc == null || newAcc == null) return;

        var skipDrawerCash = updateDrawerIfPreShift == false &&
                             await IsPreShiftDocumentForOpenDrawerAsync(invoice, "purchase_invoice", userId);

        var invId = ToLong(invoice.GetValueOrDefault("id"));
        var postedNo = invoice.GetValueOrDefault("posted_number")?.ToString() ?? $"#{invId}";
        var oldId = ToLong(oldAcc["id"]);
        var newId = ToLong(newAcc["id"]);

        if (oldId == newId)
        {
            if (skipDrawerCash && IsDefaultCashDrawerAccount(newAcc)) return;
            var delta = Math.Round(newPaid - oldPaid, 2);
            if (Math.Abs(delta) > 0.001m)
            {
                if (delta > 0m)
                {
                    await WithdrawAsync(newAcc, delta, "purchase_invoice_update", invId, $"Purchase Invoice #{postedNo} update (additional payment {delta})", userId);
                }
                else
                {
                    await DepositAsync(newAcc, Math.Abs(delta), "purchase_invoice_update", invId, $"Purchase Invoice #{postedNo} update (payment reduced +{delta})", userId);
                }
            }
        }
        else
        {
            if (oldPaid > 0m && !(skipDrawerCash && IsDefaultCashDrawerAccount(oldAcc)))
            {
                await DepositAsync(oldAcc, oldPaid, "purchase_invoice_update", invId, $"Purchase Invoice #{postedNo} payment reverted from {oldAcc.GetValueOrDefault("name")}", userId);
            }
            if (newPaid > 0m && !(skipDrawerCash && IsDefaultCashDrawerAccount(newAcc)))
            {
                await WithdrawAsync(newAcc, newPaid, "purchase_invoice_update", invId, $"Purchase Invoice #{postedNo} payment charged to {newAcc.GetValueOrDefault("name")}", userId);
            }
        }

        if (await _db.HasColumnAsync("purchase_invoices", "account_id") && ToLong(invoice.GetValueOrDefault("account_id")) != newId)
        {
            await _db.UpdateAsync("purchase_invoices", invId, new Dictionary<string, object?> { ["account_id"] = newId });
        }
    }

    public async Task RefundSaleInvoiceAsync(
        Dictionary<string, object?> invoice,
        decimal revAmount,
        string refType,
        string descriptionPrefix,
        long? userId = null)
    {
        if (!await _db.HasTableAsync("accounts") || revAmount <= 0m) return;

        var invId = ToLong(invoice.GetValueOrDefault("id"));
        var txns = await _db.HasTableAsync("account_transactions")
            ? await _db.QueryListAsync(
                "SELECT * FROM account_transactions WHERE reference_type IN ('sale_invoice', 'sale_invoice_update') AND reference_id = @invId",
                new { invId })
            : new List<Dictionary<string, object?>>();

        if (txns.Count > 0)
        {
            var netByAccount = new Dictionary<long, decimal>();
            foreach (var txn in txns)
            {
                var accId = ToLong(txn.GetValueOrDefault("account_id"));
                var amt = ToDecimal(txn.GetValueOrDefault("amount"));
                var tType = txn.GetValueOrDefault("type")?.ToString() ?? "";
                var delta = (tType is "deposit" or "transfer_in" or "opening_balance") ? amt : -amt;
                netByAccount[accId] = Math.Round((netByAccount.TryGetValue(accId, out var prev) ? prev : 0m) + delta, 2);
            }

            var positiveLegs = netByAccount.Where(kv => kv.Value > 0.001m).ToList();
            if (positiveLegs.Count > 0)
            {
                var remainingToRefund = Math.Round(revAmount, 2);
                foreach (var kv in positiveLegs)
                {
                    if (remainingToRefund <= 0.001m) break;
                    var refundLeg = Math.Round(Math.Min(remainingToRefund, kv.Value), 2);
                    var acc = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = kv.Key });
                    if (acc != null && refundLeg > 0m)
                    {
                        await WithdrawAsync(acc, refundLeg, refType, invId, $"{descriptionPrefix} ({acc.GetValueOrDefault("name")})", userId);
                        remainingToRefund = Math.Round(remainingToRefund - refundLeg, 2);
                    }
                }

                if (remainingToRefund > 0.001m)
                {
                    var fallbackAcc = await ResolveAccountAsync(ToLong(invoice.GetValueOrDefault("account_id")), invoice.GetValueOrDefault("payment_method")?.ToString() ?? "cash");
                    if (fallbackAcc != null)
                    {
                        await WithdrawAsync(fallbackAcc, remainingToRefund, refType, invId, descriptionPrefix, userId);
                    }
                }
                return;
            }
        }

        if (invoice.GetValueOrDefault("account_id") != null)
        {
            var acc = await ResolveAccountAsync(ToLong(invoice.GetValueOrDefault("account_id")), invoice.GetValueOrDefault("payment_method")?.ToString() ?? "cash");
            if (acc != null)
            {
                await WithdrawAsync(acc, revAmount, refType, invId, descriptionPrefix, userId);
            }
        }
    }

    public async Task RefundPurchaseInvoiceAsync(
        Dictionary<string, object?> invoice,
        decimal revAmount,
        string refType,
        string descriptionPrefix,
        long? userId = null)
    {
        if (!await _db.HasTableAsync("accounts") || revAmount <= 0m) return;

        var invId = ToLong(invoice.GetValueOrDefault("id"));
        var txns = await _db.HasTableAsync("account_transactions")
            ? await _db.QueryListAsync(
                "SELECT * FROM account_transactions WHERE reference_type IN ('purchase_invoice', 'purchase_invoice_update') AND reference_id = @invId",
                new { invId })
            : new List<Dictionary<string, object?>>();

        if (txns.Count > 0)
        {
            var netWithdrawnByAccount = new Dictionary<long, decimal>();
            foreach (var txn in txns)
            {
                var accId = ToLong(txn.GetValueOrDefault("account_id"));
                var amt = ToDecimal(txn.GetValueOrDefault("amount"));
                var tType = txn.GetValueOrDefault("type")?.ToString() ?? "";
                var delta = (tType is "withdrawal" or "transfer_out") ? amt : -amt;
                netWithdrawnByAccount[accId] = Math.Round((netWithdrawnByAccount.TryGetValue(accId, out var prev) ? prev : 0m) + delta, 2);
            }

            var positiveLegs = netWithdrawnByAccount.Where(kv => kv.Value > 0.001m).ToList();
            if (positiveLegs.Count > 0)
            {
                var remainingToRefund = Math.Round(revAmount, 2);
                foreach (var kv in positiveLegs)
                {
                    if (remainingToRefund <= 0.001m) break;
                    var refundLeg = Math.Round(Math.Min(remainingToRefund, kv.Value), 2);
                    var acc = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = kv.Key });
                    if (acc != null && refundLeg > 0m)
                    {
                        await DepositAsync(acc, refundLeg, refType, invId, $"{descriptionPrefix} ({acc.GetValueOrDefault("name")})", userId);
                        remainingToRefund = Math.Round(remainingToRefund - refundLeg, 2);
                    }
                }

                if (remainingToRefund > 0.001m && invoice.GetValueOrDefault("account_id") != null)
                {
                    var fallbackAcc = await ResolveAccountAsync(ToLong(invoice.GetValueOrDefault("account_id")), invoice.GetValueOrDefault("payment_method")?.ToString() ?? "cash");
                    if (fallbackAcc != null)
                    {
                        await DepositAsync(fallbackAcc, remainingToRefund, refType, invId, descriptionPrefix, userId);
                    }
                }
                return;
            }
        }

        if (invoice.GetValueOrDefault("account_id") != null)
        {
            var acc = await ResolveAccountAsync(ToLong(invoice.GetValueOrDefault("account_id")), invoice.GetValueOrDefault("payment_method")?.ToString() ?? "cash");
            if (acc != null)
            {
                await DepositAsync(acc, revAmount, refType, invId, descriptionPrefix, userId);
            }
        }
    }

    public static long ToLong(object? val)
    {
        if (val == null) return 0L;
        if (val is long l) return l;
        if (val is int i) return i;
        return long.TryParse(val.ToString(), out var parsed) ? parsed : 0L;
    }

    public static decimal ToDecimal(object? val)
    {
        if (val == null) return 0m;
        if (val is decimal d) return d;
        if (val is double db) return (decimal)db;
        if (val is float f) return (decimal)f;
        if (val is long l) return l;
        if (val is int i) return i;
        return decimal.TryParse(val.ToString(), out var parsed) ? parsed : 0m;
    }

    public static bool IsTruthy(object? val)
    {
        if (val == null) return false;
        if (val is bool b) return b;
        if (val is long l) return l != 0;
        if (val is int i) return i != 0;
        var s = val.ToString() ?? "";
        return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}

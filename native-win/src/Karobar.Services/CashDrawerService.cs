using System.Text.Json;
using Dapper;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class CashDrawerService
{
    private readonly KarobarDb _db;

    public static readonly string[] CashInTypes =
    {
        "cash_sale",
        "customer_payment",
        "purchase_return",
        "cash_in"
    };

    public static readonly string[] CashOutTypes =
    {
        "cash_purchase",
        "cash_return",
        "cash_refund",
        "supplier_payment",
        "expense",
        "cash_out"
    };

    public CashDrawerService(KarobarDb db)
    {
        _db = db;
    }

    public async Task<Dictionary<string, object?>?> GetCurrentSessionAsync(long? userId = null, bool strictUser = true, long? terminalId = null)
    {
        if (!await _db.HasTableAsync("cash_drawer_sessions"))
        {
            return null;
        }

        var hasTerminalCol = await _db.HasColumnAsync("cash_drawer_sessions", "terminal_id");

        if (hasTerminalCol && terminalId.HasValue && terminalId.Value > 0)
        {
            var termSession = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM cash_drawer_sessions WHERE status = 'open' AND terminal_id = @tid ORDER BY opened_at DESC, id DESC LIMIT 1",
                new { tid = terminalId.Value });
            if (termSession != null)
            {
                await AttachUserAsync(termSession);
                return termSession;
            }

            if (userId.HasValue && userId.Value > 0)
            {
                var unassignedUserSession = await _db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM cash_drawer_sessions WHERE status = 'open' AND user_id = @uid AND terminal_id IS NULL ORDER BY opened_at DESC, id DESC LIMIT 1",
                    new { uid = userId.Value });
                if (unassignedUserSession != null)
                {
                    await AttachUserAsync(unassignedUserSession);
                    return unassignedUserSession;
                }
            }

            if (strictUser)
            {
                return null;
            }
        }

        if (userId.HasValue && userId.Value > 0)
        {
            var userSession = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM cash_drawer_sessions WHERE status = 'open' AND user_id = @uid ORDER BY opened_at DESC, id DESC LIMIT 1",
                new { uid = userId.Value });
            if (userSession != null)
            {
                await AttachUserAsync(userSession);
                return userSession;
            }

            if (strictUser)
            {
                return null;
            }
        }

        var latest = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM cash_drawer_sessions WHERE status = 'open' ORDER BY opened_at DESC, id DESC LIMIT 1");
        if (latest != null)
        {
            await AttachUserAsync(latest);
        }
        return latest;
    }

    public async Task<Dictionary<string, object?>?> GetLastClosedSessionAsync(long? userId = null, long? terminalId = null)
    {
        if (!await _db.HasTableAsync("cash_drawer_sessions"))
        {
            return null;
        }

        var hasTerminalCol = await _db.HasColumnAsync("cash_drawer_sessions", "terminal_id");

        if (hasTerminalCol && terminalId.HasValue && terminalId.Value > 0)
        {
            var termClosed = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM cash_drawer_sessions WHERE status = 'closed' AND terminal_id = @tid ORDER BY closed_at DESC, id DESC LIMIT 1",
                new { tid = terminalId.Value });
            if (termClosed != null)
            {
                await AttachUserAsync(termClosed);
                return termClosed;
            }
        }

        if (userId.HasValue && userId.Value > 0)
        {
            var userClosed = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM cash_drawer_sessions WHERE status = 'closed' AND user_id = @uid ORDER BY closed_at DESC, id DESC LIMIT 1",
                new { uid = userId.Value });
            if (userClosed != null)
            {
                await AttachUserAsync(userClosed);
                return userClosed;
            }

            var anyOpen = await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cash_drawer_sessions WHERE status = 'open'") > 0;
            if (anyOpen)
            {
                return null;
            }
        }

        var fallback = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM cash_drawer_sessions WHERE status = 'closed' ORDER BY closed_at DESC, id DESC LIMIT 1");
        if (fallback != null)
        {
            await AttachUserAsync(fallback);
        }
        return fallback;
    }

    public async Task<Dictionary<string, object?>> OpenSessionAsync(
        long userId,
        decimal openingBalance,
        string? notes = null,
        long? terminalId = null)
    {
        var hasTerminalCol = await _db.HasColumnAsync("cash_drawer_sessions", "terminal_id");

        Dictionary<string, object?>? existing;
        if (hasTerminalCol && terminalId.HasValue && terminalId.Value > 0)
        {
            existing = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM cash_drawer_sessions WHERE status = 'open' AND terminal_id = @tid LIMIT 1",
                new { tid = terminalId.Value });
        }
        else if (hasTerminalCol)
        {
            // Single-PC standalone mode (no terminal_id): only ONE physical drawer can be open at a time on this PC,
            // even across different logged-in users, so Cash in Hand is never double-counted by a second open.
            existing = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM cash_drawer_sessions WHERE status = 'open' AND terminal_id IS NULL ORDER BY id DESC LIMIT 1");
        }
        else
        {
            existing = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM cash_drawer_sessions WHERE status = 'open' ORDER BY id DESC LIMIT 1");
        }

        if (existing != null)
        {
            var exId = AccountService.ToLong(existing["id"]);
            var exUid = AccountService.ToLong(existing.GetValueOrDefault("user_id"));
            string? exUserName = null;
            if (exUid > 0 && await _db.HasTableAsync("users"))
            {
                exUserName = await _db.ExecuteScalarAsync<string?>("SELECT name FROM users WHERE id = @uid", new { uid = exUid });
            }
            var ownerLabel = !string.IsNullOrWhiteSpace(exUserName) ? $" (opened by {exUserName})" : "";
            throw new InvalidOperationException(
                terminalId.HasValue
                    ? $"Cash drawer Shift #{exId}{ownerLabel} is already open on this terminal. Please close it first."
                    : $"Cash drawer Shift #{exId}{ownerLabel} is already open on this PC. Please close Shift #{exId} first before opening a new shift so Cash in Hand is not double-counted.");
        }

        var opening = Math.Max(0m, Math.Round(openingBalance, 2));

        var openSessions = await _db.QueryListAsync("SELECT * FROM cash_drawer_sessions WHERE status = 'open'");
        decimal otherOpenExpected = 0m;
        foreach (var openSess in openSessions)
        {
            otherOpenExpected += await RecalculateExpectedBalanceAsync(AccountService.ToLong(openSess["id"]), syncAccount: false);
        }

        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var createData = new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["opened_at"] = nowStr,
            ["status"] = "open",
            ["opening_balance"] = opening,
            ["closing_balance_expected"] = opening,
            ["closing_balance_actual"] = null,
            ["difference"] = 0m,
            ["notes"] = notes
        };
        if (hasTerminalCol)
        {
            createData["terminal_id"] = terminalId;
        }

        var sessionId = await _db.InsertAsync("cash_drawer_sessions", createData);
        var session = await _db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId })
                      ?? createData;

        if (await _db.HasTableAsync("accounts"))
        {
            var accountService = new AccountService(_db);
            var cashAccount = await accountService.SyncCashDrawerBalanceAsync(session);
            if (cashAccount != null)
            {
                var currentBal = AccountService.ToDecimal(cashAccount.GetValueOrDefault("current_balance"));
                decimal delta;
                if (openSessions.Count == 0)
                {
                    // Single active shift: reconcile opening float against Cash in Hand
                    delta = Math.Round(opening - currentBal, 2);
                }
                else
                {
                    // Multi-terminal / additional counter: draw from any unassigned closed-counter pool first
                    // and only deposit fresh cash added above that pool — never wipe out another closed shift's cash!
                    var unassignedPool = Math.Max(0m, Math.Round(currentBal - otherOpenExpected, 2));
                    delta = opening > unassignedPool ? Math.Round(opening - unassignedPool, 2) : 0m;
                }

                if (delta > 0.001m)
                {
                    await accountService.DepositAsync(
                        cashAccount,
                        delta,
                        "cash_drawer_open",
                        sessionId,
                        $"Cash drawer opening float (Session #{sessionId})",
                        userId);
                }
                else if (delta < -0.001m)
                {
                    await accountService.WithdrawAsync(
                        cashAccount,
                        Math.Abs(delta),
                        "cash_drawer_open",
                        sessionId,
                        $"Cash drawer opening balance reconciliation (Session #{sessionId})",
                        userId);
                }
            }
        }

        SyncPulseService.Bump("drawer");
        await AttachUserAsync(session);
        return session;
    }

    public async Task<Dictionary<string, object?>?> RecordTransactionAsync(
        Dictionary<string, object?>? session,
        string type,
        decimal amount,
        string? refType = null,
        long? refId = null,
        string? notes = null,
        long? userId = null,
        long? terminalId = null)
    {
        if (!await _db.HasTableAsync("cash_drawer_sessions") || !await _db.HasTableAsync("cash_drawer_transactions"))
        {
            return null;
        }

        session ??= await GetCurrentSessionAsync(userId, false, terminalId);
        if (session == null || !string.Equals(session.GetValueOrDefault("status")?.ToString(), "open", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sessionId = AccountService.ToLong(session["id"]);
        var lockedSession = await _db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId });
        if (lockedSession == null || !string.Equals(lockedSession.GetValueOrDefault("status")?.ToString(), "open", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        amount = Math.Round(Math.Abs(amount), 2);
        if (amount <= 0m)
        {
            return null;
        }

        var txnPayload = new Dictionary<string, object?>
        {
            ["session_id"] = sessionId,
            ["user_id"] = userId ?? AccountService.ToLong(lockedSession.GetValueOrDefault("user_id")),
            ["type"] = type,
            ["amount"] = amount,
            ["reference_type"] = refType,
            ["reference_id"] = refId,
            ["notes"] = notes
        };

        if (await _db.HasColumnAsync("cash_drawer_transactions", "terminal_id"))
        {
            txnPayload["terminal_id"] = terminalId ?? lockedSession.GetValueOrDefault("terminal_id");
        }

        var txnId = await _db.InsertAsync("cash_drawer_transactions", txnPayload);
        await RecalculateExpectedBalanceAsync(sessionId);

        if ((refType == "manual" || refType == null) && await _db.HasTableAsync("accounts"))
        {
            var accountService = new AccountService(_db);
            var cashAccount = await accountService.SyncCashDrawerBalanceAsync(lockedSession);
            if (cashAccount != null)
            {
                var desc = !string.IsNullOrWhiteSpace(notes) ? notes : (type == "cash_in" ? "Manual Cash In" : "Manual Cash Out");
                if (CashInTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                {
                    await accountService.DepositAsync(
                        cashAccount,
                        amount,
                        "cash_drawer_manual",
                        txnId,
                        refType == "manual" ? $"Manual Cash In: {desc}" : desc,
                        userId);
                }
                else if (CashOutTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                {
                    await accountService.WithdrawAsync(
                        cashAccount,
                        amount,
                        "cash_drawer_manual",
                        txnId,
                        refType == "manual" ? $"Manual Cash Out: {desc}" : desc,
                        userId);
                }
            }
        }

        SyncPulseService.Bump("drawer");
        var created = await _db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_transactions WHERE id = @id", new { id = txnId });
        if (created != null)
        {
            await AttachUserAsync(created);
        }
        return created;
    }

    public async Task<decimal> RecalculateExpectedBalanceAsync(long sessionId, bool syncAccount = true)
    {
        var session = await _db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId });
        if (session == null) return 0m;

        var cashIn = await _db.ExecuteScalarAsync<decimal?>(
            "SELECT COALESCE(SUM(amount), 0) FROM cash_drawer_transactions WHERE session_id = @sessionId AND type IN ('cash_sale', 'customer_payment', 'purchase_return', 'cash_in')",
            new { sessionId }) ?? 0m;

        var cashOut = await _db.ExecuteScalarAsync<decimal?>(
            "SELECT COALESCE(SUM(amount), 0) FROM cash_drawer_transactions WHERE session_id = @sessionId AND type IN ('cash_purchase', 'cash_return', 'cash_refund', 'supplier_payment', 'expense', 'cash_out')",
            new { sessionId }) ?? 0m;

        var opening = AccountService.ToDecimal(session.GetValueOrDefault("opening_balance"));
        var expected = Math.Round(opening + cashIn - cashOut, 2);

        await _db.UpdateAsync("cash_drawer_sessions", sessionId, new Dictionary<string, object?>
        {
            ["closing_balance_expected"] = expected
        });

        if (syncAccount && await _db.HasTableAsync("accounts"))
        {
            var accountService = new AccountService(_db);
            await accountService.SyncCashDrawerBalanceAsync(session);
        }

        return expected;
    }

    public async Task<Dictionary<string, object?>> CloseSessionAsync(
        long sessionId,
        decimal actualCount,
        string? notes = null,
        Dictionary<string, decimal>? denominations = null,
        long? userId = null)
    {
        var lockedSession = await _db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId })
                            ?? throw new KeyNotFoundException("Cash drawer session not found.");

        if (!string.Equals(lockedSession.GetValueOrDefault("status")?.ToString(), "open", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This cash drawer session has already been closed.");
        }

        var expected = await RecalculateExpectedBalanceAsync(sessionId, syncAccount: false);
        var actual = Math.Round(actualCount, 2);
        var difference = Math.Round(actual - expected, 2);

        var otherOpenSessions = await _db.QueryListAsync(
            "SELECT * FROM cash_drawer_sessions WHERE status = 'open' AND id != @id",
            new { id = sessionId });
        decimal otherOpenExpected = 0m;
        foreach (var openSess in otherOpenSessions)
        {
            otherOpenExpected += await RecalculateExpectedBalanceAsync(AccountService.ToLong(openSess["id"]), syncAccount: false);
        }

        var prevNotes = lockedSession.GetValueOrDefault("notes")?.ToString() ?? "";
        var combinedNotes = string.Join("\n", new[] { prevNotes, notes ?? "" }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

        Dictionary<string, decimal>? cleanDenominations = null;
        if (denominations != null && denominations.Count > 0)
        {
            var allowedKeys = new[] { "5000", "1000", "500", "100", "50", "20", "10", "coins" };
            var filtered = new Dictionary<string, decimal>();
            foreach (var k in allowedKeys)
            {
                if (denominations.TryGetValue(k, out var val) && val > 0m)
                {
                    filtered[k] = Math.Round(val, 2);
                }
            }
            if (filtered.Count > 0)
            {
                cleanDenominations = filtered;
            }
        }

        var updatePayload = new Dictionary<string, object?>
        {
            ["status"] = "closed",
            ["closed_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
            ["closing_balance_expected"] = expected,
            ["closing_balance_actual"] = actual,
            ["difference"] = difference,
            ["notes"] = string.IsNullOrWhiteSpace(combinedNotes) ? null : combinedNotes
        };

        if (await _db.HasColumnAsync("cash_drawer_sessions", "closing_denominations"))
        {
            updatePayload["closing_denominations"] = cleanDenominations != null ? JsonSerializer.Serialize(cleanDenominations) : null;
        }

        await _db.UpdateAsync("cash_drawer_sessions", sessionId, updatePayload);

        if (await _db.HasTableAsync("accounts"))
        {
            var accountService = new AccountService(_db);
            var cashAccount = await accountService.SyncCashDrawerBalanceAsync(lockedSession);
            if (cashAccount != null)
            {
                // Post ONLY the physical cash count variance (actual - expected) for this closing shift.
                // Never overwrite Cash in Hand with (otherOpenExpected + actual), which would erase cash
                // from previously closed multi-terminal shifts still held in Cash in Hand.
                var actorId = userId ?? AccountService.ToLong(lockedSession.GetValueOrDefault("user_id"));

                if (difference > 0.001m)
                {
                    await accountService.DepositAsync(
                        cashAccount,
                        difference,
                        "cash_drawer_variance",
                        sessionId,
                        $"Cash drawer close overage (Session #{sessionId})",
                        actorId);
                }
                else if (difference < -0.001m)
                {
                    await accountService.WithdrawAsync(
                        cashAccount,
                        Math.Abs(difference),
                        "cash_drawer_variance",
                        sessionId,
                        $"Cash drawer close shortage (Session #{sessionId})",
                        actorId);
                }
            }
        }

        SyncPulseService.Bump("drawer");
        var closed = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId }))!;
        await AttachUserAsync(closed);
        var txns = await _db.QueryListAsync(
            "SELECT * FROM cash_drawer_transactions WHERE session_id = @id ORDER BY created_at ASC, id ASC",
            new { id = sessionId });
        foreach (var t in txns)
        {
            await AttachUserAsync(t);
        }
        closed["transactions"] = txns;
        return closed;
    }

    public async Task<Dictionary<string, object?>> GetSessionSummaryAsync(long sessionId)
    {
        var session = await _db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId });
        if (session == null) return new Dictionary<string, object?>();
        await AttachUserAsync(session);
        return await GetSessionSummaryAsync(session);
    }

    public async Task<Dictionary<string, object?>> GetSessionSummaryAsync(
        Dictionary<string, object?> session,
        List<Dictionary<string, object?>>? transactions = null)
    {
        var sessionId = AccountService.ToLong(session.GetValueOrDefault("id"));
        transactions ??= await _db.QueryListAsync(
            "SELECT id, session_id, type, reference_type, reference_id, amount FROM cash_drawer_transactions WHERE session_id = @sessionId",
            new { sessionId });

        var cashSalesTxns = transactions.Where(t => IsType(t, "cash_sale")).ToList();
        var cashReturnsTxns = transactions.Where(t => IsType(t, "cash_return") || IsType(t, "cash_refund")).ToList();
        var cashPurchasesTxns = transactions.Where(t => IsType(t, "cash_purchase")).ToList();
        var purchaseReturnsTxns = transactions.Where(t => IsType(t, "purchase_return")).ToList();
        var customerPaymentsTxns = transactions.Where(t => IsType(t, "customer_payment")).ToList();
        var supplierPaymentsTxns = transactions.Where(t => IsType(t, "supplier_payment")).ToList();
        var cashInTxns = transactions.Where(t => IsType(t, "cash_in")).ToList();

        var expenseTxns = transactions.Where(t =>
        {
            var tp = t.GetValueOrDefault("type")?.ToString() ?? "";
            var rt = t.GetValueOrDefault("reference_type")?.ToString() ?? "";
            return tp.Equals("expense", StringComparison.OrdinalIgnoreCase) ||
                   (tp.Equals("cash_out", StringComparison.OrdinalIgnoreCase) &&
                    (rt.Equals("App\\Models\\Expense", StringComparison.OrdinalIgnoreCase) || rt.Equals("expense", StringComparison.OrdinalIgnoreCase)));
        }).ToList();

        var staffAdvanceTxns = transactions.Where(t =>
        {
            var tp = t.GetValueOrDefault("type")?.ToString() ?? "";
            var rt = t.GetValueOrDefault("reference_type")?.ToString() ?? "";
            return tp.Equals("cash_out", StringComparison.OrdinalIgnoreCase) &&
                   (rt.Equals("App\\Models\\EmployeeAdvance", StringComparison.OrdinalIgnoreCase) || rt.Equals("employee_advance", StringComparison.OrdinalIgnoreCase));
        }).ToList();

        var cashOutTxns = transactions.Where(t =>
        {
            var tp = t.GetValueOrDefault("type")?.ToString() ?? "";
            var rt = t.GetValueOrDefault("reference_type")?.ToString() ?? "";
            return tp.Equals("cash_out", StringComparison.OrdinalIgnoreCase)
                   && !rt.Equals("App\\Models\\Expense", StringComparison.OrdinalIgnoreCase)
                   && !rt.Equals("expense", StringComparison.OrdinalIgnoreCase)
                   && !rt.Equals("App\\Models\\EmployeeAdvance", StringComparison.OrdinalIgnoreCase)
                   && !rt.Equals("employee_advance", StringComparison.OrdinalIgnoreCase);
        }).ToList();

        decimal SumAmount(IEnumerable<Dictionary<string, object?>> list) =>
            list.Sum(x => AccountService.ToDecimal(x.GetValueOrDefault("amount")));

        var cashSales = SumAmount(cashSalesTxns);
        var cashReturns = SumAmount(cashReturnsTxns);
        var cashPurchases = SumAmount(cashPurchasesTxns);
        var purchaseReturns = SumAmount(purchaseReturnsTxns);
        var customerPayments = SumAmount(customerPaymentsTxns);
        var supplierPayments = SumAmount(supplierPaymentsTxns);
        var cashIn = SumAmount(cashInTxns);
        var expenses = SumAmount(expenseTxns);
        var staffAdvances = SumAmount(staffAdvanceTxns);
        var cashOut = SumAmount(cashOutTxns);

        var totalCashIn = cashSales + customerPayments + purchaseReturns + cashIn;
        var totalCashOut = cashPurchases + cashReturns + supplierPayments + expenses + staffAdvances + cashOut;
        var openingBalance = AccountService.ToDecimal(session.GetValueOrDefault("opening_balance"));
        var expected = Math.Round(openingBalance + totalCashIn - totalCashOut, 2);

        var status = session.GetValueOrDefault("status")?.ToString() ?? "open";
        var openedAtStr = session.GetValueOrDefault("opened_at")?.ToString();
        var closedAtStr = session.GetValueOrDefault("closed_at")?.ToString();

        var duration = "0 minutes";
        if (DateTime.TryParse(openedAtStr, out var openedDt))
        {
            var endDt = DateTime.TryParse(closedAtStr, out var closedDt) ? closedDt : DateTime.UtcNow;
            var span = endDt - openedDt;
            if (span.TotalHours >= 1)
                duration = $"{(int)span.TotalHours}h {span.Minutes}m";
            else
                duration = $"{Math.Max(1, (int)span.TotalMinutes)} minutes";
        }

        object? userSummary = null;
        if (session.TryGetValue("user", out var uObj) && uObj is Dictionary<string, object?> uDict)
        {
            userSummary = new Dictionary<string, object?>
            {
                ["id"] = uDict.GetValueOrDefault("id"),
                ["name"] = uDict.GetValueOrDefault("name")
            };
        }
        else if (session.TryGetValue("user_id", out var uidObj) && uidObj != null)
        {
            var u = await _db.QuerySingleOrDefaultAsync("SELECT id, name FROM users WHERE id = @id", new { id = AccountService.ToLong(uidObj) });
            if (u != null)
            {
                userSummary = new Dictionary<string, object?>
                {
                    ["id"] = u.GetValueOrDefault("id"),
                    ["name"] = u.GetValueOrDefault("name")
                };
            }
        }

        object? closingDenoms = session.GetValueOrDefault("closing_denominations");
        if (closingDenoms is string denomStr && !string.IsNullOrWhiteSpace(denomStr))
        {
            try
            {
                closingDenoms = JsonSerializer.Deserialize<object>(denomStr);
            }
            catch
            {
            }
        }

        var shiftSales = await GetShiftSalesAndTenderMetricsAsync(session, transactions);

        return new Dictionary<string, object?>
        {
            ["session_id"] = sessionId,
            ["status"] = status,
            ["report_type"] = status.Equals("closed", StringComparison.OrdinalIgnoreCase) ? "Z-REPORT" : "X-REPORT",
            ["opened_at"] = openedAtStr,
            ["closed_at"] = closedAtStr,
            ["duration"] = duration,
            ["user"] = userSummary,
            ["opening_balance"] = openingBalance,
            ["cash_sales"] = cashSales,
            ["cash_returns"] = cashReturns,
            ["cash_purchases"] = cashPurchases,
            ["purchase_returns"] = purchaseReturns,
            ["customer_payments"] = customerPayments,
            ["supplier_payments"] = supplierPayments,
            ["expenses"] = expenses,
            ["staff_advances"] = staffAdvances,
            ["manual_cash_in"] = cashIn,
            ["manual_cash_out"] = cashOut,
            ["total_cash_in"] = totalCashIn,
            ["total_cash_out"] = totalCashOut,
            ["expected_balance"] = expected,
            ["actual_balance"] = session.GetValueOrDefault("closing_balance_actual"),
            ["difference"] = session.GetValueOrDefault("difference"),
            ["closing_denominations"] = closingDenoms,
            ["notes"] = session.GetValueOrDefault("notes"),
            ["transactions_count"] = transactions.Count,
            ["counts"] = new Dictionary<string, object?>
            {
                ["cash_sales"] = cashSalesTxns.Count,
                ["customer_payments"] = customerPaymentsTxns.Count,
                ["purchase_returns"] = purchaseReturnsTxns.Count,
                ["manual_cash_in"] = cashInTxns.Count,
                ["cash_purchases"] = cashPurchasesTxns.Count,
                ["cash_returns"] = cashReturnsTxns.Count,
                ["supplier_payments"] = supplierPaymentsTxns.Count,
                ["expenses"] = expenseTxns.Count,
                ["staff_advances"] = staffAdvanceTxns.Count,
                ["manual_cash_out"] = cashOutTxns.Count
            },
            ["shift_sales"] = shiftSales
        };
    }

    public async Task<Dictionary<string, object?>> GetShiftSalesAndTenderMetricsAsync(
        Dictionary<string, object?> session,
        List<Dictionary<string, object?>>? transactions = null)
    {
        var sessionId = AccountService.ToLong(session.GetValueOrDefault("id"));
        var startStr = session.GetValueOrDefault("opened_at")?.ToString() ?? DateTime.UtcNow.Date.ToString("yyyy-MM-dd 00:00:00");
        var endStr = session.GetValueOrDefault("closed_at")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        transactions ??= await _db.QueryListAsync(
            "SELECT id, session_id, type, reference_type, reference_id, amount FROM cash_drawer_transactions WHERE session_id = @sessionId",
            new { sessionId });

        var linkedSaleIds = transactions
            .Where(t => IsType(t, "cash_sale") &&
                        (IsRefType(t, "sale_invoice") || IsRefType(t, "App\\Models\\SaleInvoice")) &&
                        AccountService.ToLong(t.GetValueOrDefault("reference_id")) > 0)
            .Select(t => AccountService.ToLong(t.GetValueOrDefault("reference_id")))
            .Distinct()
            .ToArray();

        var linkedReturnIds = transactions
            .Where(t => IsType(t, "cash_return") &&
                        (IsRefType(t, "sale_return") || IsRefType(t, "App\\Models\\SaleReturn")) &&
                        AccountService.ToLong(t.GetValueOrDefault("reference_id")) > 0)
            .Select(t => AccountService.ToLong(t.GetValueOrDefault("reference_id")))
            .Distinct()
            .ToArray();

        int totalInvoicesCount = 0;
        int retailCount = 0;
        int wholesaleCount = 0;
        decimal grossSales = 0m;
        decimal discountTotal = 0m;
        decimal taxTotal = 0m;
        decimal netSales = 0m;
        decimal cashTenderSales = 0m;
        decimal cardBankOnlineSales = 0m;
        decimal creditUdharSales = 0m;

        if (await _db.HasTableAsync("sale_invoices"))
        {
            var cashAccountIds = new HashSet<long>();
            if (await _db.HasTableAsync("accounts"))
            {
                var cashAccs = await _db.QueryListAsync("SELECT id FROM accounts WHERE type = 'cash'");
                foreach (var ca in cashAccs)
                {
                    cashAccountIds.Add(AccountService.ToLong(ca.GetValueOrDefault("id")));
                }
            }

            string invSql = linkedSaleIds.Length > 0
                ? @"SELECT id, sale_type, invoice_type, payment_method, account_id, gross_amount, discount_amount, item_discount, tax_amount, total, total_receive
                    FROM sale_invoices
                    WHERE ((created_at >= @startStr AND created_at <= @endStr) OR id IN @linkedSaleIds)
                      AND (status IS NULL OR status NOT IN ('reversed', 'cancelled'))"
                : @"SELECT id, sale_type, invoice_type, payment_method, account_id, gross_amount, discount_amount, item_discount, tax_amount, total, total_receive
                    FROM sale_invoices
                    WHERE created_at >= @startStr AND created_at <= @endStr
                      AND (status IS NULL OR status NOT IN ('reversed', 'cancelled'))";

            var invoices = await _db.QueryListAsync(invSql, new { startStr, endStr, linkedSaleIds });
            foreach (var inv in invoices)
            {
                totalInvoicesCount++;
                var saleType = (inv.GetValueOrDefault("sale_type")?.ToString() ?? "retail").ToLowerInvariant();
                if (saleType == "wholesale") wholesaleCount++;
                else retailCount++;

                var invTotal = Math.Round(AccountService.ToDecimal(inv.GetValueOrDefault("total")), 2);
                var invDisc = Math.Round(AccountService.ToDecimal(inv.GetValueOrDefault("discount_amount")) + AccountService.ToDecimal(inv.GetValueOrDefault("item_discount")), 2);
                var invTax = Math.Round(AccountService.ToDecimal(inv.GetValueOrDefault("tax_amount")), 2);
                var invGross = AccountService.ToDecimal(inv.GetValueOrDefault("gross_amount"));
                if (invGross <= 0m)
                {
                    invGross = Math.Max(0m, Math.Round(invTotal + AccountService.ToDecimal(inv.GetValueOrDefault("discount_amount")) - invTax, 2));
                }

                grossSales += invGross;
                discountTotal += invDisc;
                taxTotal += invTax;
                netSales += invTotal;

                var invType = (inv.GetValueOrDefault("invoice_type")?.ToString() ?? "debit").ToLowerInvariant();
                var payMethod = (inv.GetValueOrDefault("payment_method")?.ToString() ?? "cash").ToLowerInvariant();
                var accId = AccountService.ToLong(inv.GetValueOrDefault("account_id"));
                var received = Math.Round(AccountService.ToDecimal(inv.GetValueOrDefault("total_receive")), 2);

                if (invType != "credit" && received <= 0m)
                {
                    received = invTotal;
                }
                var effectivePaid = Math.Min(invTotal, Math.Max(0m, received));
                var unpaidCredit = invType == "credit" ? Math.Max(0m, Math.Round(invTotal - effectivePaid, 2)) : 0m;

                var isCashMethod = (payMethod == "cash" || payMethod == "") && (accId <= 0 || cashAccountIds.Contains(accId));
                if (isCashMethod)
                {
                    cashTenderSales += effectivePaid;
                }
                else
                {
                    cardBankOnlineSales += effectivePaid;
                }

                creditUdharSales += unpaidCredit;
            }
        }

        int saleReturnsCount = 0;
        decimal saleReturnsTotal = 0m;
        decimal cashRefundReturns = 0m;
        decimal creditAdjReturns = 0m;

        if (await _db.HasTableAsync("sale_returns"))
        {
            var linkedReturnSet = new HashSet<long>(linkedReturnIds);
            string retSql = linkedReturnIds.Length > 0
                ? @"SELECT id, total, settlement_mode, payment_method, credited_amount
                    FROM sale_returns
                    WHERE (created_at >= @startStr AND created_at <= @endStr) OR id IN @linkedReturnIds"
                : @"SELECT id, total, settlement_mode, payment_method, credited_amount
                    FROM sale_returns
                    WHERE created_at >= @startStr AND created_at <= @endStr";

            var returns = await _db.QueryListAsync(retSql, new { startStr, endStr, linkedReturnIds });
            foreach (var ret in returns)
            {
                saleReturnsCount++;
                var retId = AccountService.ToLong(ret.GetValueOrDefault("id"));
                var retTotal = Math.Round(AccountService.ToDecimal(ret.GetValueOrDefault("total")), 2);
                saleReturnsTotal += retTotal;

                var mode = (ret.GetValueOrDefault("settlement_mode")?.ToString() ?? "").ToLowerInvariant();
                var pm = (ret.GetValueOrDefault("payment_method")?.ToString() ?? "cash").ToLowerInvariant();
                if (mode is "credit_ledger" or "ledger" or "credit_note" or "invoice_offset")
                {
                    creditAdjReturns += retTotal;
                }
                else if (linkedReturnSet.Contains(retId) || (mode is "refund" or "cash_refund" or "" && pm == "cash"))
                {
                    cashRefundReturns += retTotal;
                }
                else
                {
                    creditAdjReturns += retTotal;
                }
            }
        }

        return new Dictionary<string, object?>
        {
            ["total_invoices_count"] = totalInvoicesCount,
            ["retail_invoices_count"] = retailCount,
            ["wholesale_invoices_count"] = wholesaleCount,
            ["gross_sales"] = Math.Round(grossSales, 2),
            ["discount_total"] = Math.Round(discountTotal, 2),
            ["tax_total"] = Math.Round(taxTotal, 2),
            ["net_sales"] = Math.Round(netSales, 2),
            ["cash_tender_sales"] = Math.Round(cashTenderSales, 2),
            ["card_bank_online_sales"] = Math.Round(cardBankOnlineSales, 2),
            ["credit_udhar_sales"] = Math.Round(creditUdharSales, 2),
            ["sale_returns_count"] = saleReturnsCount,
            ["sale_returns_total"] = Math.Round(saleReturnsTotal, 2),
            ["cash_refund_returns"] = Math.Round(cashRefundReturns, 2),
            ["credit_adj_returns"] = Math.Round(creditAdjReturns, 2)
        };
    }

    public async Task<List<Dictionary<string, object?>>> EnrichTransactionsAsync(List<Dictionary<string, object?>> transactions)
    {
        if (transactions.Count == 0) return transactions;

        foreach (var txn in transactions)
        {
            var refType = txn.GetValueOrDefault("reference_type")?.ToString() ?? "";
            var refId = AccountService.ToLong(txn.GetValueOrDefault("reference_id"));
            var txnType = txn.GetValueOrDefault("type")?.ToString() ?? "";

            string? refLabel = null;
            string? partyName = null;
            string? refUrl = null;
            string? categoryLabel = null;

            if (refId > 0 && (refType.Equals("sale_invoice", StringComparison.OrdinalIgnoreCase) || refType.EndsWith("SaleInvoice", StringComparison.OrdinalIgnoreCase)))
            {
                var inv = await _db.HasTableAsync("sale_invoices")
                    ? await _db.QuerySingleOrDefaultAsync(
                        "SELECT si.id, si.posted_number, si.patient_name, c.name as customer_name FROM sale_invoices si LEFT JOIN customers c ON c.id = si.customer_id WHERE si.id = @refId",
                        new { refId })
                    : null;
                refLabel = inv?.GetValueOrDefault("posted_number")?.ToString() ?? $"SI-#{refId}";
                var custName = inv?.GetValueOrDefault("customer_name")?.ToString();
                var patientName = inv?.GetValueOrDefault("patient_name")?.ToString();
                partyName = !string.IsNullOrWhiteSpace(custName)
                            ? custName
                            : (!string.IsNullOrWhiteSpace(patientName) ? patientName : "Walk-in Customer");
                refUrl = $"/sale-invoices/{refId}";
                categoryLabel = txnType == "cash_return" ? "Sale Invoice Refund / Reversal" : "Cash Sale Invoice";
            }
            else if (refId > 0 && (refType.Equals("purchase_invoice", StringComparison.OrdinalIgnoreCase) || refType.EndsWith("PurchaseInvoice", StringComparison.OrdinalIgnoreCase)))
            {
                var inv = await _db.HasTableAsync("purchase_invoices")
                    ? await _db.QuerySingleOrDefaultAsync(
                        "SELECT pi.id, pi.posted_number, pi.invoice_number, s.name as supplier_name FROM purchase_invoices pi LEFT JOIN suppliers s ON s.id = pi.supplier_id WHERE pi.id = @refId",
                        new { refId })
                    : null;
                refLabel = inv?.GetValueOrDefault("posted_number")?.ToString()
                           ?? inv?.GetValueOrDefault("invoice_number")?.ToString()
                           ?? $"PI-#{refId}";
                partyName = inv?.GetValueOrDefault("supplier_name")?.ToString() ?? "Supplier";
                refUrl = $"/purchase-invoices/{refId}";
                categoryLabel = "Cash Purchase Invoice (GRN)";
            }
            else if (refId > 0 && (refType.Equals("sale_return", StringComparison.OrdinalIgnoreCase) || refType.EndsWith("SaleReturn", StringComparison.OrdinalIgnoreCase)))
            {
                var ret = await _db.HasTableAsync("sale_returns")
                    ? await _db.QuerySingleOrDefaultAsync(
                        "SELECT sr.id, sr.posted_number, c.name as customer_name FROM sale_returns sr LEFT JOIN customers c ON c.id = sr.customer_id WHERE sr.id = @refId",
                        new { refId })
                    : null;
                refLabel = ret?.GetValueOrDefault("posted_number")?.ToString() ?? $"SR-#{refId}";
                partyName = ret?.GetValueOrDefault("customer_name")?.ToString() ?? "Walk-in Customer";
                refUrl = $"/sale-returns/{refId}";
                categoryLabel = "Cash Refund to Customer (Sale Return)";
            }
            else if (refId > 0 && (refType.Equals("purchase_return", StringComparison.OrdinalIgnoreCase) || refType.EndsWith("PurchaseReturn", StringComparison.OrdinalIgnoreCase)))
            {
                var ret = await _db.HasTableAsync("purchase_returns")
                    ? await _db.QuerySingleOrDefaultAsync(
                        "SELECT pr.id, pr.posted_number, s.name as supplier_name FROM purchase_returns pr LEFT JOIN suppliers s ON s.id = pr.supplier_id WHERE pr.id = @refId",
                        new { refId })
                    : null;
                refLabel = ret?.GetValueOrDefault("posted_number")?.ToString() ?? $"PR-#{refId}";
                partyName = ret?.GetValueOrDefault("supplier_name")?.ToString() ?? "Supplier";
                refUrl = $"/purchase-returns/{refId}";
                categoryLabel = "Cash Refund from Supplier (Purchase Return)";
            }
            else if (refId > 0 && (refType.Equals("payment", StringComparison.OrdinalIgnoreCase) || refType.EndsWith("Payment", StringComparison.OrdinalIgnoreCase)))
            {
                var pay = await _db.HasTableAsync("payments")
                    ? await _db.QuerySingleOrDefaultAsync(
                        @"SELECT p.*, c.name as customer_name, s.name as supplier_name
                          FROM payments p
                          LEFT JOIN customers c ON c.id = p.party_id AND p.party_type = 'customer'
                          LEFT JOIN suppliers s ON s.id = p.party_id AND p.party_type = 'supplier'
                          WHERE p.id = @refId",
                        new { refId })
                    : null;
                refLabel = pay?.GetValueOrDefault("payment_number")?.ToString() ?? $"PMT-#{refId}";
                var pType = pay?.GetValueOrDefault("party_type")?.ToString() ?? "";
                var partyId = AccountService.ToLong(pay?.GetValueOrDefault("party_id"));
                if (pType == "customer" || txnType == "customer_payment")
                {
                    partyName = pay?.GetValueOrDefault("customer_name")?.ToString() ?? "Customer";
                    refUrl = partyId > 0 ? $"/customer-ledger?customer_id={partyId}" : "/customer-ledger";
                    categoryLabel = "Customer Udhar Recovery";
                }
                else
                {
                    partyName = pay?.GetValueOrDefault("supplier_name")?.ToString() ?? "Supplier";
                    refUrl = partyId > 0 ? $"/supplier-ledger?supplier_id={partyId}" : "/supplier-ledger";
                    categoryLabel = "Supplier Ledger Payment";
                }
            }
            else if (refId > 0 && (refType.Equals("expense", StringComparison.OrdinalIgnoreCase) || refType.EndsWith("Expense", StringComparison.OrdinalIgnoreCase)))
            {
                var exp = await _db.HasTableAsync("expenses")
                    ? await _db.QuerySingleOrDefaultAsync(
                        "SELECT e.*, ec.name as category_name FROM expenses e LEFT JOIN expense_categories ec ON ec.id = e.expense_category_id WHERE e.id = @refId",
                        new { refId })
                    : null;
                refLabel = exp?.GetValueOrDefault("expense_number")?.ToString() ?? $"EXP-#{refId}";
                partyName = exp?.GetValueOrDefault("category_name")?.ToString()
                            ?? exp?.GetValueOrDefault("payee")?.ToString()
                            ?? "Shop Expense";
                refUrl = "/expenses";
                categoryLabel = "Operating Shop Expense";
            }
            else if (refId > 0 && (refType.Equals("employee_advance", StringComparison.OrdinalIgnoreCase) || refType.EndsWith("EmployeeAdvance", StringComparison.OrdinalIgnoreCase)))
            {
                var adv = await _db.HasTableAsync("employee_advances")
                    ? await _db.QuerySingleOrDefaultAsync(
                        "SELECT ea.*, emp.name as employee_name FROM employee_advances ea LEFT JOIN employees emp ON emp.id = ea.employee_id WHERE ea.id = @refId",
                        new { refId })
                    : null;
                refLabel = adv?.GetValueOrDefault("advance_number")?.ToString() ?? $"ADV-#{refId}";
                partyName = adv?.GetValueOrDefault("employee_name")?.ToString() ?? "Staff Member";
                refUrl = "/hr-payroll?tab=advances";
                categoryLabel = txnType == "cash_in" ? "Staff Advance Repayment" : "Staff Salary Advance";
            }
            else if (refType.Equals("account_transfer", StringComparison.OrdinalIgnoreCase))
            {
                refLabel = refId > 0 ? $"TRF-#{refId}" : "Transfer";
                partyName = "Treasury / Bank Transfer";
                refUrl = "/accounts";
                categoryLabel = txnType == "cash_in" ? "Cash Withdrawn from Bank to Drawer" : "Cash Deposited from Drawer to Bank/Safe";
            }
            else
            {
                refLabel = "Manual Drawer Entry";
                var userName = (txn.GetValueOrDefault("user") as Dictionary<string, object?>)?.GetValueOrDefault("name")?.ToString();
                partyName = !string.IsNullOrWhiteSpace(userName) ? $"By {userName}" : "Cashier Entry";
                refUrl = null;
                categoryLabel = txnType == "cash_in" ? "Manual Cash Added to Drawer" : "Manual Cash Removed from Drawer";
            }

            txn["reference_label"] = refLabel;
            txn["party_name"] = partyName;
            txn["reference_url"] = refUrl;
            txn["category_label"] = categoryLabel;
        }

        return transactions;
    }

    public async Task<Dictionary<string, object?>?> HandlePaymentReversalAsync(
        Dictionary<string, object?> payment,
        string reason,
        long? userId = null,
        bool? updateDrawerIfClosedShift = null,
        long? terminalId = null)
    {
        if (!await _db.HasTableAsync("cash_drawer_sessions") || !await _db.HasTableAsync("cash_drawer_transactions"))
        {
            return null;
        }

        var grossAmount = AccountService.ToDecimal(payment.GetValueOrDefault("amount"));
        var whtAmount = AccountService.ToDecimal(payment.GetValueOrDefault("wht_amount"));
        var netCashDisbursed = AccountService.ToDecimal(payment.GetValueOrDefault("net_cash_disbursed"));
        var partyTypeRaw = payment.GetValueOrDefault("party_type")?.ToString() ?? "customer";
        var amount = partyTypeRaw.Equals("supplier", StringComparison.OrdinalIgnoreCase)
            ? (netCashDisbursed > 0m ? netCashDisbursed : Math.Max(0m, grossAmount - whtAmount))
            : grossAmount;
        if (amount <= 0m) return null;

        var payId = AccountService.ToLong(payment.GetValueOrDefault("id"));
        var existingTxn = await _db.QuerySingleOrDefaultAsync(
            @"SELECT cdt.*
              FROM cash_drawer_transactions cdt
              INNER JOIN cash_drawer_sessions cds ON cds.id = cdt.session_id
              WHERE cdt.reference_type = 'payment' AND cdt.reference_id = @payId AND cds.status = 'open'
              LIMIT 1",
            new { payId });

        if (existingTxn != null)
        {
            var openSessId = AccountService.ToLong(existingTxn["session_id"]);
            await _db.ExecuteAsync("DELETE FROM cash_drawer_transactions WHERE id = @id", new { id = existingTxn["id"] });
            await RecalculateExpectedBalanceAsync(openSessId);
            return null;
        }

        if (updateDrawerIfClosedShift == false)
        {
            return null;
        }

        var session = await GetCurrentSessionAsync(userId, false, terminalId);
        if (session == null) return null;

        var partyType = payment.GetValueOrDefault("party_type")?.ToString() ?? "customer";
        var reversalType = partyType.Equals("customer", StringComparison.OrdinalIgnoreCase) ? "cash_out" : "cash_in";
        var partyLabel = partyType.Equals("customer", StringComparison.OrdinalIgnoreCase) ? "Customer" : "Supplier";
        var payNo = payment.GetValueOrDefault("payment_number")?.ToString() ?? $"#{payId}";

        return await RecordTransactionAsync(
            session,
            reversalType,
            amount,
            "payment",
            payId,
            $"Reversal of {partyLabel} Payment {payNo}: {reason}",
            userId,
            terminalId);
    }

    public async Task<decimal> ResolveDirectCashForSaleInvoiceAsync(Dictionary<string, object?> invoice)
    {
        var invId = AccountService.ToLong(invoice.GetValueOrDefault("id"));
        var invTotal = AccountService.ToDecimal(invoice.GetValueOrDefault("total") ?? invoice.GetValueOrDefault("grand_total") ?? invoice.GetValueOrDefault("gross_amount"));
        var rawReceived = AccountService.ToDecimal(invoice.GetValueOrDefault("total_receive"));

        decimal allocated = 0m;
        if (await _db.HasTableAsync("payment_allocations") && await _db.HasTableAsync("payments"))
        {
            allocated = await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(pa.amount), 0)
                  FROM payment_allocations pa
                  INNER JOIN payments p ON p.id = pa.payment_id
                  WHERE pa.invoice_type = 'sale_invoice' AND pa.invoice_id = @invId AND p.status = 'posted'",
                new { invId }) ?? 0m;
        }

        var invType = invoice.GetValueOrDefault("invoice_type")?.ToString() ?? "debit";
        if (invType.Equals("debit", StringComparison.OrdinalIgnoreCase) && rawReceived <= 0m && allocated <= 0m)
        {
            rawReceived = invTotal;
        }
        rawReceived = Math.Round(rawReceived, 2);

        var directReceived = Math.Max(0m, Math.Round(rawReceived - allocated, 2));
        if (directReceived <= 0m) return 0m;

        if (await _db.HasTableAsync("account_transactions") && await _db.HasTableAsync("accounts"))
        {
            var acctTxns = await _db.QueryListAsync(
                @"SELECT at.*, a.type as acc_type, a.is_default as acc_is_default
                  FROM account_transactions at
                  LEFT JOIN accounts a ON a.id = at.account_id
                  WHERE at.reference_type IN ('sale_invoice', 'sale_invoice_update') AND at.reference_id = @invId",
                new { invId });

            if (acctTxns.Count > 0)
            {
                var hasNonDefaultCashTxn = acctTxns.Any(t =>
                    !(string.Equals(t.GetValueOrDefault("acc_type")?.ToString(), "cash", StringComparison.OrdinalIgnoreCase) &&
                      AccountService.IsTruthy(t.GetValueOrDefault("acc_is_default"))));

                if (hasNonDefaultCashTxn)
                {
                    decimal cashNet = 0m;
                    foreach (var t in acctTxns)
                    {
                        if (string.Equals(t.GetValueOrDefault("acc_type")?.ToString(), "cash", StringComparison.OrdinalIgnoreCase) &&
                            AccountService.IsTruthy(t.GetValueOrDefault("acc_is_default")))
                        {
                            var amt = AccountService.ToDecimal(t.GetValueOrDefault("amount"));
                            var tp = t.GetValueOrDefault("type")?.ToString() ?? "";
                            var delta = tp is "deposit" or "transfer_in" or "opening_balance" ? amt : -amt;
                            cashNet = Math.Round(cashNet + delta, 2);
                        }
                    }
                    return Math.Max(0m, Math.Min(cashNet, directReceived));
                }
            }
        }

        var payMethod = invoice.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
        return payMethod.Equals("cash", StringComparison.OrdinalIgnoreCase) ? directReceived : 0m;
    }

    public async Task<Dictionary<string, object?>?> SyncSaleInvoiceUpdateAsync(
        Dictionary<string, object?> invoice,
        decimal oldCash,
        decimal newCash,
        bool? updateDrawerIfPreShift = null,
        long? userId = null)
    {
        var session = await GetCurrentSessionAsync(userId, false);
        if (session == null) return null;

        var sessionId = AccountService.ToLong(session["id"]);
        var invId = AccountService.ToLong(invoice.GetValueOrDefault("id"));
        var postedNo = invoice.GetValueOrDefault("posted_number")?.ToString() ?? $"#{invId}";

        var existingTxn = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM cash_drawer_transactions WHERE session_id = @sessionId AND reference_type = 'sale_invoice' AND reference_id = @invId LIMIT 1",
            new { sessionId, invId });

        var isPreShift = existingTxn == null &&
                         DateTime.TryParse(invoice.GetValueOrDefault("created_at")?.ToString(), out var createdAt) &&
                         DateTime.TryParse(session.GetValueOrDefault("opened_at")?.ToString(), out var openedAt) &&
                         createdAt < openedAt;

        if (isPreShift)
        {
            if (updateDrawerIfPreShift == false) return null;
            var delta = Math.Round(newCash - oldCash, 2);
            if (delta > 0m)
            {
                return await RecordTransactionAsync(session, "cash_sale", delta, "sale_invoice", invId, $"Shift adjustment for previous invoice {postedNo} (+{delta})", userId);
            }
            if (delta < 0m)
            {
                return await RecordTransactionAsync(session, "cash_out", Math.Abs(delta), "sale_invoice", invId, $"Shift refund/reduction for previous invoice {postedNo} (-{Math.Abs(delta)})", userId);
            }
            return null;
        }

        newCash = Math.Round(Math.Max(newCash, 0m), 2);
        if (existingTxn != null)
        {
            var txnId = AccountService.ToLong(existingTxn["id"]);
            if (newCash > 0m)
            {
                await _db.UpdateAsync("cash_drawer_transactions", txnId, new Dictionary<string, object?>
                {
                    ["amount"] = newCash,
                    ["notes"] = $"Cash sale {postedNo} (updated)"
                });
                await RecalculateExpectedBalanceAsync(sessionId);
                return await _db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_transactions WHERE id = @id", new { id = txnId });
            }

            await _db.ExecuteAsync("DELETE FROM cash_drawer_transactions WHERE id = @id", new { id = txnId });
            await RecalculateExpectedBalanceAsync(sessionId);
            return null;
        }

        if (newCash > 0m)
        {
            return await RecordTransactionAsync(session, "cash_sale", newCash, "sale_invoice", invId, $"Cash sale {postedNo}", userId);
        }

        return null;
    }

    public async Task<Dictionary<string, object?>?> HandleSaleInvoiceDeletionAsync(
        Dictionary<string, object?> invoice,
        string mode = "none",
        long? userId = null)
    {
        var session = await GetCurrentSessionAsync(userId, false);
        if (session == null || mode.Equals("credit", StringComparison.OrdinalIgnoreCase)) return null;

        var cashReceived = await ResolveDirectCashForSaleInvoiceAsync(invoice);
        if (cashReceived <= 0m) return null;

        var sessionId = AccountService.ToLong(session["id"]);
        var invId = AccountService.ToLong(invoice.GetValueOrDefault("id"));
        var postedNo = invoice.GetValueOrDefault("posted_number")?.ToString() ?? $"#{invId}";

        var existingTxn = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM cash_drawer_transactions WHERE session_id = @sessionId AND reference_type = 'sale_invoice' AND reference_id = @invId LIMIT 1",
            new { sessionId, invId });

        if (existingTxn != null)
        {
            await _db.ExecuteAsync("DELETE FROM cash_drawer_transactions WHERE id = @id", new { id = existingTxn["id"] });
            await RecalculateExpectedBalanceAsync(sessionId);
            return null;
        }

        var invType = invoice.GetValueOrDefault("invoice_type")?.ToString() ?? "debit";
        if (mode.Equals("refund", StringComparison.OrdinalIgnoreCase) || invType.Equals("debit", StringComparison.OrdinalIgnoreCase))
        {
            return await RecordTransactionAsync(session, "cash_return", cashReceived, "sale_invoice_deleted", invId, $"Cash refund for deleted invoice {postedNo}", userId);
        }

        return null;
    }

    public async Task<Dictionary<string, object?>?> HandleSaleInvoiceReversalAsync(
        Dictionary<string, object?> invoice,
        string reason,
        long? userId = null)
    {
        var session = await GetCurrentSessionAsync(userId, false);
        if (session == null) return null;

        var cashReceived = await ResolveDirectCashForSaleInvoiceAsync(invoice);
        if (cashReceived <= 0m) return null;

        var invId = AccountService.ToLong(invoice.GetValueOrDefault("id"));
        var postedNo = invoice.GetValueOrDefault("posted_number")?.ToString() ?? $"#{invId}";
        return await RecordTransactionAsync(session, "cash_return", cashReceived, "sale_invoice", invId, $"Void refund for invoice {postedNo}: {reason}", userId);
    }

    private async Task AttachUserAsync(Dictionary<string, object?> record)
    {
        if (record.TryGetValue("user_id", out var uidObj) && uidObj != null)
        {
            var uid = AccountService.ToLong(uidObj);
            if (uid > 0 && await _db.HasTableAsync("users"))
            {
                var u = await _db.QuerySingleOrDefaultAsync("SELECT id, name, email FROM users WHERE id = @uid", new { uid });
                record["user"] = u;
                return;
            }
        }
        record["user"] = null;
    }

    private static bool IsType(Dictionary<string, object?> txn, string type)
    {
        return string.Equals(txn.GetValueOrDefault("type")?.ToString(), type, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRefType(Dictionary<string, object?> txn, string refType)
    {
        return string.Equals(txn.GetValueOrDefault("reference_type")?.ToString(), refType, StringComparison.OrdinalIgnoreCase);
    }
}

using Dapper;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class ExpenseService
{
    private readonly KarobarDb _db;
    private readonly AccountService _accountService;
    private readonly CashDrawerService _cashDrawerService;
    private readonly DocumentSequenceService _sequenceService;

    public ExpenseService(KarobarDb db)
    {
        _db = db;
        _accountService = new AccountService(db);
        _cashDrawerService = new CashDrawerService(db);
        _sequenceService = new DocumentSequenceService(db);
    }

    public async Task<string> GenerateExpenseNumberAsync(string? dateStr = null)
    {
        var dt = DateTime.TryParse(dateStr, out var parsed) ? parsed.Date : DateTime.UtcNow.Date;
        var ymd = dt.ToString("yyyyMMdd");
        var prefix = $"EXP-{ymd}-";

        return await _sequenceService.NextAsync(
            $"expense:{ymd}",
            prefix,
            4,
            "expenses",
            "expense_number");
    }

    public async Task<Dictionary<string, object?>> CreateExpenseAsync(Dictionary<string, object?> data, long? userId = null, long? terminalId = null)
    {
        var amount = Math.Round(AccountService.ToDecimal(data.GetValueOrDefault("amount")), 2);
        if (amount <= 0m)
        {
            throw new InvalidOperationException("Expense amount must be greater than zero.");
        }

        var dateStr = data.GetValueOrDefault("date")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (dateStr.Length >= 10) dateStr = dateStr[..10];

        var expenseNumber = await GenerateExpenseNumberAsync(dateStr);
        var paymentMethod = data.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
        var categoryId = AccountService.ToLong(data.GetValueOrDefault("expense_category_id"));

        var category = await _db.QuerySingleOrDefaultAsync("SELECT * FROM expense_categories WHERE id = @categoryId", new { categoryId })
                       ?? throw new KeyNotFoundException("Expense category not found.");

        Dictionary<string, object?>? account = null;
        var reqAccountId = AccountService.ToLong(data.GetValueOrDefault("account_id"));
        if (reqAccountId > 0)
        {
            account = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = reqAccountId });
        }
        account ??= await _accountService.GetDefaultAccountAsync(paymentMethod);

        Dictionary<string, object?>? drawerSession = null;
        if (_accountService.IsDefaultCashDrawerAccount(account, paymentMethod))
        {
            drawerSession = await _cashDrawerService.GetCurrentSessionAsync(userId, false, terminalId);
        }

        var isRecurring = AccountService.IsTruthy(data.GetValueOrDefault("is_recurring"));
        var recurringFreq = isRecurring ? data.GetValueOrDefault("recurring_frequency")?.ToString() : null;
        var payee = data.GetValueOrDefault("payee")?.ToString();
        var description = data.GetValueOrDefault("description")?.ToString();
        var refNumber = data.GetValueOrDefault("reference_number")?.ToString();
        var receiptPath = data.GetValueOrDefault("receipt_path")?.ToString();

        var expenseId = await _db.InsertAsync("expenses", new Dictionary<string, object?>
        {
            ["expense_number"] = expenseNumber,
            ["expense_category_id"] = categoryId,
            ["account_id"] = account != null ? AccountService.ToLong(account["id"]) : null,
            ["user_id"] = userId,
            ["cash_drawer_session_id"] = drawerSession != null ? AccountService.ToLong(drawerSession["id"]) : null,
            ["amount"] = amount,
            ["date"] = dateStr,
            ["payment_method"] = paymentMethod,
            ["reference_number"] = refNumber,
            ["payee"] = payee,
            ["description"] = description,
            ["receipt_path"] = receiptPath,
            ["is_recurring"] = isRecurring,
            ["recurring_frequency"] = recurringFreq
        });

        var catName = category.GetValueOrDefault("name")?.ToString() ?? "Expense";
        var payeeSuffix = !string.IsNullOrWhiteSpace(payee) ? $" ({payee})" : "";

        if (account != null)
        {
            await _accountService.WithdrawAsync(
                account,
                amount,
                "App\\Models\\Expense",
                expenseId,
                $"Expense: {catName} - {expenseNumber}{payeeSuffix}",
                userId);
        }

        if (drawerSession != null && _accountService.IsDefaultCashDrawerAccount(account, paymentMethod))
        {
            await _cashDrawerService.RecordTransactionAsync(
                drawerSession,
                "cash_out",
                amount,
                "App\\Models\\Expense",
                expenseId,
                $"Expense: {catName}{payeeSuffix}",
                userId,
                terminalId);
            await _accountService.SyncCashDrawerBalanceAsync(drawerSession);
        }

        var created = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM expenses WHERE id = @id", new { id = expenseId }))!;
        return await EnrichExpenseAsync(created);
    }

    public async Task<Dictionary<string, object?>> UpdateExpenseAsync(long expenseId, Dictionary<string, object?> data, long? userId = null, long? terminalId = null)
    {
        var expense = await _db.QuerySingleOrDefaultAsync("SELECT * FROM expenses WHERE id = @id AND deleted_at IS NULL", new { id = expenseId })
                      ?? throw new KeyNotFoundException("Expense not found.");

        var oldAmount = AccountService.ToDecimal(expense.GetValueOrDefault("amount"));
        var oldMethod = expense.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
        var oldAccountId = AccountService.ToLong(expense.GetValueOrDefault("account_id"));
        var oldSessionId = AccountService.ToLong(expense.GetValueOrDefault("cash_drawer_session_id"));
        var expenseNumber = expense.GetValueOrDefault("expense_number")?.ToString() ?? $"EXP-{expenseId}";

        var newAmount = data.ContainsKey("amount")
            ? Math.Round(AccountService.ToDecimal(data["amount"]), 2)
            : oldAmount;
        if (newAmount <= 0m)
        {
            throw new InvalidOperationException("Expense amount must be greater than zero.");
        }

        var newMethod = data.GetValueOrDefault("payment_method")?.ToString() ?? oldMethod;
        var newAccountId = data.ContainsKey("account_id")
            ? AccountService.ToLong(data["account_id"])
            : oldAccountId;
        var categoryId = data.ContainsKey("expense_category_id")
            ? AccountService.ToLong(data["expense_category_id"])
            : AccountService.ToLong(expense.GetValueOrDefault("expense_category_id"));

        var category = await _db.QuerySingleOrDefaultAsync("SELECT * FROM expense_categories WHERE id = @categoryId", new { categoryId })
                       ?? throw new KeyNotFoundException("Expense category not found.");

        // Preserve non-cash write-off expenses (e.g. Stock Adjustment/Disposal with payment_method='other' & account_id=NULL)
        // so editing them never hijacks Cash in Hand / Cash Drawer.
        var isNonCashWriteOff = oldAccountId == 0 && newAccountId == 0 &&
                                string.Equals(newMethod, "other", StringComparison.OrdinalIgnoreCase);

        Dictionary<string, object?>? newAccount = null;
        if (!isNonCashWriteOff)
        {
            if (newAccountId > 0 && (newMethod.Equals(oldMethod, StringComparison.OrdinalIgnoreCase) || data.ContainsKey("account_id")))
            {
                newAccount = await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = newAccountId });
            }
            newAccount ??= await _accountService.GetDefaultAccountAsync(newMethod);
        }

        // 1. Unwind old financial effects
        Dictionary<string, object?>? oldAcc = oldAccountId > 0
            ? await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = oldAccountId })
            : (oldMethod.Equals("cash", StringComparison.OrdinalIgnoreCase) ? await _accountService.GetDefaultAccountAsync("cash") : null);

        if (oldAcc != null)
        {
            await _accountService.DepositAsync(
                oldAcc,
                oldAmount,
                "App\\Models\\Expense",
                expenseId,
                $"Adjustment / Reversal of prior expense {expenseNumber}",
                userId);
        }

        var oldIsDrawerCash = _accountService.IsDefaultCashDrawerAccount(oldAcc, oldMethod);
        var newIsDrawerCash = !isNonCashWriteOff && _accountService.IsDefaultCashDrawerAccount(newAccount, newMethod);

        // 2. Resolve active drawer session
        var activeSession = await _cashDrawerService.GetCurrentSessionAsync(userId, false, terminalId);

        Dictionary<string, object?>? oldOpenSession = null;
        Dictionary<string, object?>? oldOpenTx = null;
        if (oldIsDrawerCash && oldSessionId > 0)
        {
            var candSession = await _db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id = oldSessionId });
            if (candSession != null && string.Equals(candSession.GetValueOrDefault("status")?.ToString(), "open", StringComparison.OrdinalIgnoreCase))
            {
                oldOpenSession = candSession;
                oldOpenTx = await _db.QuerySingleOrDefaultAsync(
                    @"SELECT * FROM cash_drawer_transactions
                      WHERE session_id = @oldSessionId
                        AND reference_type IN ('App\Models\Expense', 'expense')
                        AND reference_id = @expenseId
                      LIMIT 1",
                    new { oldSessionId, expenseId });
            }
        }

        var isRecurring = data.ContainsKey("is_recurring")
            ? AccountService.IsTruthy(data["is_recurring"])
            : AccountService.IsTruthy(expense.GetValueOrDefault("is_recurring"));
        var recurringFreq = isRecurring
            ? (data.ContainsKey("recurring_frequency") ? data["recurring_frequency"]?.ToString() : expense.GetValueOrDefault("recurring_frequency")?.ToString())
            : null;

        var payee = data.ContainsKey("payee") ? data["payee"]?.ToString() : expense.GetValueOrDefault("payee")?.ToString();
        var description = data.ContainsKey("description") ? data["description"]?.ToString() : expense.GetValueOrDefault("description")?.ToString();
        var refNumber = data.ContainsKey("reference_number") ? data["reference_number"]?.ToString() : expense.GetValueOrDefault("reference_number")?.ToString();
        var receiptPath = data.ContainsKey("receipt_path") ? data["receipt_path"]?.ToString() : expense.GetValueOrDefault("receipt_path")?.ToString();
        var dateStr = data.GetValueOrDefault("date")?.ToString() ?? expense.GetValueOrDefault("date")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (dateStr.Length >= 10) dateStr = dateStr[..10];

        // Preserve original closed/null session ID on pre-shift expenses so subsequent edits or deletes
        // never confuse a pre-shift delta adjustment row with an original same-shift expense.
        long? resolvedSessionId = null;
        if (newIsDrawerCash)
        {
            if (oldOpenSession != null)
            {
                resolvedSessionId = AccountService.ToLong(oldOpenSession["id"]);
            }
            else if (oldSessionId > 0)
            {
                resolvedSessionId = oldSessionId;
            }
            else if (!oldIsDrawerCash && activeSession != null)
            {
                // Switched from non-cash (e.g. bank) into active cash drawer for the first time
                resolvedSessionId = AccountService.ToLong(activeSession["id"]);
            }
        }

        await _db.UpdateAsync("expenses", expenseId, new Dictionary<string, object?>
        {
            ["expense_category_id"] = categoryId,
            ["account_id"] = newAccount != null ? AccountService.ToLong(newAccount["id"]) : null,
            ["cash_drawer_session_id"] = resolvedSessionId,
            ["amount"] = newAmount,
            ["date"] = dateStr,
            ["payment_method"] = newMethod,
            ["reference_number"] = refNumber,
            ["payee"] = payee,
            ["description"] = description,
            ["receipt_path"] = receiptPath,
            ["is_recurring"] = isRecurring,
            ["recurring_frequency"] = recurringFreq
        });

        var catName = category.GetValueOrDefault("name")?.ToString() ?? "Expense";
        var payeeSuffix = !string.IsNullOrWhiteSpace(payee) ? $" ({payee})" : "";
        var drawerNote = $"Expense: {catName}{payeeSuffix}";

        if (newAccount != null)
        {
            await _accountService.WithdrawAsync(
                newAccount,
                newAmount,
                "App\\Models\\Expense",
                expenseId,
                $"Expense: {catName} - {expenseNumber}{payeeSuffix}",
                userId);
        }

        if (oldOpenSession != null && oldOpenTx != null)
        {
            var openSessId = AccountService.ToLong(oldOpenSession["id"]);
            var txId = AccountService.ToLong(oldOpenTx["id"]);
            if (newIsDrawerCash)
            {
                await _db.UpdateAsync("cash_drawer_transactions", txId, new Dictionary<string, object?>
                {
                    ["amount"] = newAmount,
                    ["notes"] = drawerNote
                });
                await _cashDrawerService.RecalculateExpectedBalanceAsync(openSessId);
            }
            else
            {
                await _db.ExecuteAsync("DELETE FROM cash_drawer_transactions WHERE id = @txId", new { txId });
                await _cashDrawerService.RecalculateExpectedBalanceAsync(openSessId);
            }
        }
        else if (activeSession != null)
        {
            var oldDrawerCash = oldIsDrawerCash ? oldAmount : 0m;
            var newDrawerCash = newIsDrawerCash ? newAmount : 0m;
            var cashDelta = Math.Round(newDrawerCash - oldDrawerCash, 2);

            if (cashDelta > 0.001m)
            {
                await _cashDrawerService.RecordTransactionAsync(
                    activeSession,
                    "cash_out",
                    cashDelta,
                    "App\\Models\\Expense",
                    expenseId,
                    $"{drawerNote} (adjustment +{cashDelta})",
                    userId,
                    terminalId);
            }
            else if (cashDelta < -0.001m)
            {
                await _cashDrawerService.RecordTransactionAsync(
                    activeSession,
                    "cash_in",
                    Math.Abs(cashDelta),
                    "App\\Models\\Expense",
                    expenseId,
                    $"{drawerNote} (reduction reimbursement {Math.Abs(cashDelta)})",
                    userId,
                    terminalId);
            }
        }

        var updated = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM expenses WHERE id = @id", new { id = expenseId }))!;
        return await EnrichExpenseAsync(updated);
    }

    public async Task<bool> DeleteExpenseAsync(long expenseId, long? userId = null, long? terminalId = null)
    {
        var expense = await _db.QuerySingleOrDefaultAsync("SELECT * FROM expenses WHERE id = @id AND deleted_at IS NULL", new { id = expenseId })
                      ?? throw new KeyNotFoundException("Expense not found.");

        var amount = AccountService.ToDecimal(expense.GetValueOrDefault("amount"));
        var payMethod = expense.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
        var accId = AccountService.ToLong(expense.GetValueOrDefault("account_id"));
        var sessId = AccountService.ToLong(expense.GetValueOrDefault("cash_drawer_session_id"));
        var expNo = expense.GetValueOrDefault("expense_number")?.ToString() ?? $"EXP-{expenseId}";

        var acc = accId > 0
            ? await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accId })
            : (payMethod.Equals("cash", StringComparison.OrdinalIgnoreCase) ? await _accountService.GetDefaultAccountAsync("cash") : null);

        if (acc != null && amount > 0m)
        {
            await _accountService.DepositAsync(
                acc,
                amount,
                "App\\Models\\Expense",
                expenseId,
                $"Reversal of deleted expense {expNo}",
                userId);
        }

        if (_accountService.IsDefaultCashDrawerAccount(acc, payMethod) && amount > 0m)
        {
            var handledInOpenOriginalSession = false;
            if (sessId > 0)
            {
                var session = await _db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id = sessId });
                if (session != null && string.Equals(session.GetValueOrDefault("status")?.ToString(), "open", StringComparison.OrdinalIgnoreCase))
                {
                    var tx = await _db.QuerySingleOrDefaultAsync(
                        @"SELECT * FROM cash_drawer_transactions
                          WHERE session_id = @sessId AND reference_type IN ('App\Models\Expense', 'expense') AND reference_id = @expenseId
                          LIMIT 1",
                        new { sessId, expenseId });
                    if (tx != null)
                    {
                        await _db.ExecuteAsync("DELETE FROM cash_drawer_transactions WHERE id = @id", new { id = tx["id"] });
                        await _cashDrawerService.RecalculateExpectedBalanceAsync(sessId);
                        handledInOpenOriginalSession = true;
                    }
                }
            }

            if (!handledInOpenOriginalSession)
            {
                var activeSession = await _cashDrawerService.GetCurrentSessionAsync(userId, false, terminalId);
                if (activeSession != null)
                {
                    await _cashDrawerService.RecordTransactionAsync(
                        activeSession,
                        "cash_in",
                        amount,
                        "App\\Models\\Expense",
                        expenseId,
                        $"Expense Reversal: {expNo} reimbursement",
                        userId,
                        terminalId);
                }
            }
        }

        if (await _db.HasColumnAsync("expenses", "deleted_at"))
        {
            await _db.UpdateAsync("expenses", expenseId, new Dictionary<string, object?>
            {
                ["deleted_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }
        else
        {
            await _db.ExecuteAsync("DELETE FROM expenses WHERE id = @expenseId", new { expenseId });
        }

        return true;
    }

    public async Task<Dictionary<string, object?>> ProcessRecurringExpensesAsync(DateTime? asOfDate = null, long? actorUserId = null)
    {
        var targetDate = (asOfDate ?? DateTime.UtcNow).Date;
        var createdExpenses = new List<Dictionary<string, object?>>();

        if (!await _db.HasTableAsync("expenses"))
        {
            return new Dictionary<string, object?>
            {
                ["processed_count"] = 0,
                ["created_expenses"] = createdExpenses
            };
        }

        var recurringHeads = await _db.QueryListAsync(
            @"SELECT * FROM expenses
              WHERE is_recurring = 1
                AND recurring_frequency IN ('weekly', 'monthly', 'yearly')
                AND deleted_at IS NULL
              ORDER BY date ASC, id ASC");

        foreach (var head in recurringHeads)
        {
            var current = head;
            var nextDate = GetNextRecurringDate(current);
            int iterations = 0;

            while (nextDate.HasValue && nextDate.Value.Date <= targetDate && iterations < 12)
            {
                iterations++;
                var curId = AccountService.ToLong(current["id"]);
                var ownerId = actorUserId ?? (current.GetValueOrDefault("user_id") != null ? AccountService.ToLong(current["user_id"]) : (long?)null);

                var spawned = await CreateExpenseAsync(new Dictionary<string, object?>
                {
                    ["expense_category_id"] = current.GetValueOrDefault("expense_category_id"),
                    ["account_id"] = current.GetValueOrDefault("account_id"),
                    ["amount"] = AccountService.ToDecimal(current.GetValueOrDefault("amount")),
                    ["date"] = nextDate.Value.ToString("yyyy-MM-dd"),
                    ["payment_method"] = current.GetValueOrDefault("payment_method") ?? "cash",
                    ["reference_number"] = current.GetValueOrDefault("reference_number"),
                    ["payee"] = current.GetValueOrDefault("payee"),
                    ["description"] = current.GetValueOrDefault("description"),
                    ["is_recurring"] = true,
                    ["recurring_frequency"] = current.GetValueOrDefault("recurring_frequency")
                }, ownerId);

                await _db.UpdateAsync("expenses", curId, new Dictionary<string, object?>
                {
                    ["is_recurring"] = false
                });

                createdExpenses.Add(spawned);
                current = spawned;
                nextDate = GetNextRecurringDate(current);
            }
        }

        return new Dictionary<string, object?>
        {
            ["processed_count"] = createdExpenses.Count,
            ["created_expenses"] = createdExpenses
        };
    }

    public async Task<Dictionary<string, object?>> GetNetProfitReportAsync(string fromDate, string toDate)
    {
        var from = DateTime.TryParse(fromDate, out var fDt) ? fDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-01");
        var to = DateTime.TryParse(toDate, out var tDt) ? tDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd");

        var salesData = await _db.HasTableAsync("sale_invoices")
            ? await _db.QuerySingleOrDefaultAsync(
                @"SELECT COUNT(id) as total_invoices,
                         COALESCE(SUM(COALESCE(gross_amount, 0)), 0) as gross_sales,
                         COALESCE(SUM(COALESCE(discount_amount, 0)), 0) as total_discounts,
                         COALESCE(SUM(COALESCE(tax_amount, 0)), 0) as total_taxes,
                         COALESCE(SUM(COALESCE(total, 0)), 0) as net_invoiced_sales
                  FROM sale_invoices
                  WHERE DATE(date) BETWEEN @from AND @to
                    AND (status IS NULL OR status != 'reversed')",
                new { from, to })
            : null;

        var returnsData = await _db.HasTableAsync("sale_returns")
            ? await _db.QuerySingleOrDefaultAsync(
                @"SELECT COUNT(id) as total_returns,
                         COALESCE(SUM(COALESCE(total, 0)), 0) as total_returns_amount
                  FROM sale_returns
                  WHERE DATE(date) BETWEEN @from AND @to",
                new { from, to })
            : null;

        var grossSales = Math.Round(AccountService.ToDecimal(salesData?.GetValueOrDefault("gross_sales")), 2);
        var totalSales = Math.Round(AccountService.ToDecimal(salesData?.GetValueOrDefault("net_invoiced_sales")), 2);
        var totalReturns = Math.Round(AccountService.ToDecimal(returnsData?.GetValueOrDefault("total_returns_amount")), 2);
        var netSales = Math.Round(Math.Max(0m, totalSales - totalReturns), 2);

        var hasBonusCol = await _db.HasColumnAsync("sale_invoice_items", "bonus");
        var saleUnitsExpr = hasBonusCol ? "(COALESCE(sii.quantity, 0) + COALESCE(sii.bonus, 0))" : "COALESCE(sii.quantity, 0)";

        var cogsSales = await _db.HasTableAsync("sale_invoice_items")
            ? await _db.ExecuteScalarAsync<decimal?>(
                $@"SELECT COALESCE(SUM({saleUnitsExpr} * COALESCE(sii.cost_price_snapshot, CASE WHEN p.avg_price IS NULL OR p.avg_price = 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END)), 0)
                   FROM sale_invoice_items sii
                   INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
                   INNER JOIN products p ON p.id = sii.product_id
                   WHERE DATE(si.date) BETWEEN @from AND @to
                     AND (si.status IS NULL OR si.status != 'reversed')",
                new { from, to }) ?? 0m
            : 0m;

        var cogsReturns = await _db.HasTableAsync("sale_return_items")
            ? await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(COALESCE(sri.unit_return_quantity, 0) * COALESCE(sri.cost_price_snapshot, CASE WHEN p.avg_price IS NULL OR p.avg_price = 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END)), 0)
                  FROM sale_return_items sri
                  INNER JOIN sale_returns sr ON sr.id = sri.sale_return_id
                  INNER JOIN products p ON p.id = sri.product_id
                  WHERE DATE(sr.date) BETWEEN @from AND @to",
                new { from, to }) ?? 0m
            : 0m;

        var cogs = Math.Round(Math.Max(0m, cogsSales - cogsReturns), 2);
        var grossProfit = Math.Round(netSales - cogs, 2);
        var grossMarginPct = netSales > 0m ? Math.Round((grossProfit / netSales) * 100m, 2) : 0m;

        decimal totalExpenses = 0m;
        var byCategory = new List<Dictionary<string, object?>>();

        if (await _db.HasTableAsync("expenses") && await _db.HasTableAsync("expense_categories"))
        {
            totalExpenses = Math.Round(await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(e.amount), 0)
                  FROM expenses e
                  INNER JOIN expense_categories ec ON ec.id = e.expense_category_id
                  WHERE DATE(e.date) BETWEEN @from AND @to
                    AND e.deleted_at IS NULL",
                new { from, to }) ?? 0m, 2);

            var catRows = await _db.QueryListAsync(
                @"SELECT ec.id as category_id,
                         ec.name as category_name,
                         ec.code as category_code,
                         COUNT(e.id) as count,
                         COALESCE(SUM(e.amount), 0) as total_amount
                  FROM expenses e
                  INNER JOIN expense_categories ec ON ec.id = e.expense_category_id
                  WHERE DATE(e.date) BETWEEN @from AND @to
                    AND e.deleted_at IS NULL
                  GROUP BY ec.id, ec.name, ec.code
                  ORDER BY total_amount DESC",
                new { from, to });

            foreach (var row in catRows)
            {
                var amt = Math.Round(AccountService.ToDecimal(row.GetValueOrDefault("total_amount")), 2);
                byCategory.Add(new Dictionary<string, object?>
                {
                    ["category_id"] = row.GetValueOrDefault("category_id"),
                    ["category_name"] = row.GetValueOrDefault("category_name"),
                    ["category_code"] = row.GetValueOrDefault("category_code"),
                    ["count"] = (int)AccountService.ToLong(row.GetValueOrDefault("count")),
                    ["total_amount"] = amt,
                    ["percentage"] = totalExpenses > 0m ? Math.Round((amt / totalExpenses) * 100m, 1) : 0m
                });
            }
        }

        var netProfit = Math.Round(grossProfit - totalExpenses, 2);
        var netMarginPct = netSales > 0m ? Math.Round((netProfit / netSales) * 100m, 2) : 0m;

        return new Dictionary<string, object?>
        {
            ["date_range"] = new Dictionary<string, object?> { ["from"] = from, ["to"] = to },
            ["kpis"] = new Dictionary<string, object?>
            {
                ["gross_sales"] = grossSales,
                ["total_discounts"] = Math.Round(AccountService.ToDecimal(salesData?.GetValueOrDefault("total_discounts")), 2),
                ["total_taxes"] = Math.Round(AccountService.ToDecimal(salesData?.GetValueOrDefault("total_taxes")), 2),
                ["invoiced_sales"] = totalSales,
                ["returns_amount"] = totalReturns,
                ["net_sales"] = netSales,
                ["cost_of_goods_sold"] = cogs,
                ["gross_profit"] = grossProfit,
                ["gross_margin_pct"] = grossMarginPct,
                ["total_expenses"] = totalExpenses,
                ["net_profit"] = netProfit,
                ["net_margin_pct"] = netMarginPct,
                ["is_profitable"] = netProfit >= 0m
            },
            ["expenses_by_category"] = byCategory
        };
    }

    public static DateTime? GetNextRecurringDate(Dictionary<string, object?> expense)
    {
        if (!AccountService.IsTruthy(expense.GetValueOrDefault("is_recurring"))) return null;
        var freq = expense.GetValueOrDefault("recurring_frequency")?.ToString()?.ToLowerInvariant();
        var dateStr = expense.GetValueOrDefault("date")?.ToString();
        if (string.IsNullOrWhiteSpace(freq) || !DateTime.TryParse(dateStr, out var baseDate)) return null;

        baseDate = baseDate.Date;
        return freq switch
        {
            "weekly" => baseDate.AddDays(7),
            "monthly" => baseDate.AddMonths(1),
            "yearly" => baseDate.AddYears(1),
            _ => null
        };
    }

    public async Task<Dictionary<string, object?>> EnrichExpenseAsync(Dictionary<string, object?> expense)
    {
        var catId = AccountService.ToLong(expense.GetValueOrDefault("expense_category_id"));
        expense["category"] = catId > 0 && await _db.HasTableAsync("expense_categories")
            ? await _db.QuerySingleOrDefaultAsync("SELECT * FROM expense_categories WHERE id = @catId", new { catId })
            : null;

        var accId = AccountService.ToLong(expense.GetValueOrDefault("account_id"));
        expense["account"] = accId > 0 && await _db.HasTableAsync("accounts")
            ? await _db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @accId", new { accId })
            : null;

        var uid = AccountService.ToLong(expense.GetValueOrDefault("user_id"));
        expense["user"] = uid > 0 && await _db.HasTableAsync("users")
            ? await _db.QuerySingleOrDefaultAsync("SELECT id, name FROM users WHERE id = @uid", new { uid })
            : null;

        var sessId = AccountService.ToLong(expense.GetValueOrDefault("cash_drawer_session_id"));
        expense["drawer_session"] = sessId > 0 && await _db.HasTableAsync("cash_drawer_sessions")
            ? await _db.QuerySingleOrDefaultAsync("SELECT id, status FROM cash_drawer_sessions WHERE id = @sessId", new { sessId })
            : null;

        var receiptPath = expense.GetValueOrDefault("receipt_path")?.ToString();
        if (!string.IsNullOrWhiteSpace(receiptPath))
        {
            expense["receipt_url"] = receiptPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) || receiptPath.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase)
                ? receiptPath
                : "/storage/" + receiptPath.TrimStart('/');
        }
        else
        {
            expense["receipt_url"] = null;
        }

        expense["next_recurring_date"] = GetNextRecurringDate(expense)?.ToString("yyyy-MM-dd");
        return expense;
    }
}

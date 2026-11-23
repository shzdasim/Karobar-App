using Dapper;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class AccountingService
{
    private readonly KarobarDb _db;
    private readonly AccountService _accountService;
    private readonly DocumentSequenceService _sequenceService;
    private List<Dictionary<string, object?>>? _lastTreasuryAccounts;

    public AccountingService(KarobarDb db)
    {
        _db = db;
        _accountService = new AccountService(db);
        _sequenceService = new DocumentSequenceService(db);
    }

    public async Task EnsureDefaultsAsync()
    {
        if (!await _db.HasTableAsync("chart_of_accounts"))
        {
            return;
        }

        var count = await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM chart_of_accounts");
        if (count == 0)
        {
            await SeedDefaultChartOfAccountsAsync();
        }
        else
        {
            var hasStaffAdvances = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM chart_of_accounts WHERE system_key = 'staff_advances_receivable'") > 0;
            if (!hasStaffAdvances)
            {
                var exists1250 = await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM chart_of_accounts WHERE code = '1250'") > 0;
                if (!exists1250)
                {
                    await _db.InsertAsync("chart_of_accounts", new Dictionary<string, object?>
                    {
                        ["code"] = "1250",
                        ["name"] = "Staff Advances & Employee Loans Receivable",
                        ["account_type"] = "asset",
                        ["sub_type"] = "current_asset",
                        ["normal_balance"] = "debit",
                        ["system_key"] = "staff_advances_receivable",
                        ["opening_balance"] = 0m,
                        ["is_system"] = true,
                        ["is_active"] = true,
                        ["description"] = "Salary advances and short-term loans receivable from store employees"
                    });
                }
            }
        }

        if (await _db.HasTableAsync("tax_rates"))
        {
            var taxCount = await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM tax_rates");
            if (taxCount == 0)
            {
                await SeedDefaultTaxRatesAsync();
            }
        }
    }

    private async Task SeedDefaultChartOfAccountsAsync()
    {
        var defaults = new (string Code, string Name, string AccountType, string SubType, string NormalBalance, string SystemKey, bool IsSystem, string Description)[]
        {
            ("1010", "Cash & Bank Equivalents (Treasury)", "asset", "cash_bank", "debit", "cash_and_banks", true, "Cash in hand, active drawers, commercial bank accounts, and mobile wallets"),
            ("1200", "Accounts Receivable (Trade Debtors)", "asset", "accounts_receivable", "debit", "accounts_receivable", true, "Receivable balances due from credit customers"),
            ("1250", "Staff Advances & Employee Loans Receivable", "asset", "current_asset", "debit", "staff_advances_receivable", true, "Salary advances and short-term loans receivable from store employees"),
            ("1300", "Merchandise Inventory (Stock-on-Hand)", "asset", "inventory", "debit", "merchandise_inventory", true, "On-hand product inventory valued at weighted-average cost"),
            ("1400", "Input Tax Receivable (Purchase GST/VAT)", "asset", "tax_receivable", "debit", "input_tax_receivable", true, "Input sales tax / VAT paid on supplier purchases claimable against output tax"),
            ("1500", "Prepayments & Supplier Advances", "asset", "current_asset", "debit", "supplier_advances", false, "Advance payments, security deposits, and prepaid store expenses"),
            ("1600", "Store Equipment, Fixtures & Fixed Assets", "asset", "fixed_asset", "debit", "fixed_assets", false, "Shelving, refrigerators, generators, POS hardware, and vehicles"),
            ("2100", "Accounts Payable (Trade Creditors)", "liability", "accounts_payable", "credit", "accounts_payable", true, "Payable balances owed to pharmaceutical & wholesale suppliers"),
            ("2200", "Output Tax Payable (Sales GST/VAT)", "liability", "tax_payable", "credit", "output_tax_payable", true, "Output GST / VAT collected on customer sales invoices"),
            ("2300", "Withholding Tax (WHT) & Statutory Dues Payable", "liability", "tax_payable", "credit", "withholding_tax_payable", false, "Withholding tax deducted on supplier payments and statutory obligations"),
            ("2400", "Accrued Expenses & Short-Term Liabilities", "liability", "current_liability", "credit", "accrued_liabilities", false, "Unpaid utility bills, rent accruals, and customer credit notes"),
            ("3100", "Owner's Capital & Opening Equity Reserve", "equity", "equity", "credit", "owners_capital", true, "Owner capital contributions and opening inventory/treasury equity reserve"),
            ("3200", "Owner's Drawings & Distributions", "equity", "equity", "debit", "owners_drawings", false, "Personal withdrawals and profit distributions to owners/partners"),
            ("3300", "Retained Earnings & Cumulative Net Income", "equity", "equity", "credit", "retained_earnings", true, "Accumulated net operating earnings retained in the business"),
            ("4100", "Gross Sales Revenue (Retail & Wholesale)", "revenue", "sales_revenue", "credit", "gross_sales_revenue", true, "Gross revenue from retail counter and wholesale distribution sales"),
            ("4200", "Sales Returns & Allowances (Contra-Revenue)", "revenue", "contra_revenue", "debit", "sales_returns", true, "Merchandise returned by customers (net of tax)"),
            ("4300", "Sales Discounts Allowed (Contra-Revenue)", "revenue", "contra_revenue", "debit", "sales_discounts", true, "Line-item and invoice-level promotional/trade discounts granted to customers"),
            ("4400", "Other Operating Income & Supplier Rebates", "revenue", "other_income", "credit", "other_income", false, "Purchase trade discounts, supplier rebates, and miscellaneous income"),
            ("5100", "Cost of Goods Sold (COGS)", "expense", "cogs", "debit", "cost_of_goods_sold", true, "Direct inventory cost of items sold (net of returned item cost)"),
            ("5200", "Inventory Shrinkage, Quarantine & Expiry Write-Offs", "expense", "cogs", "debit", "inventory_shrinkage", true, "Expired batch disposals, quarantine write-offs, and negative stocktake variances"),
            ("5300", "Store Operating & Administrative Expenses (OpEx)", "expense", "operating_expense", "debit", "operating_expenses", true, "Rent, salaries, electricity, delivery fuel, and general store expenses"),
            ("5400", "Depreciation & Amortization Expense", "expense", "operating_expense", "debit", "depreciation_expense", false, "Periodic depreciation of store equipment, fixtures, and vehicles")
        };

        foreach (var d in defaults)
        {
            var exists = await _db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM chart_of_accounts WHERE code = @code OR system_key = @sysKey",
                new { code = d.Code, sysKey = d.SystemKey }) > 0;
            if (!exists)
            {
                await _db.InsertAsync("chart_of_accounts", new Dictionary<string, object?>
                {
                    ["code"] = d.Code,
                    ["name"] = d.Name,
                    ["account_type"] = d.AccountType,
                    ["sub_type"] = d.SubType,
                    ["normal_balance"] = d.NormalBalance,
                    ["system_key"] = d.SystemKey,
                    ["opening_balance"] = 0m,
                    ["is_system"] = d.IsSystem,
                    ["is_active"] = true,
                    ["description"] = d.Description
                });
            }
        }
    }

    private async Task SeedDefaultTaxRatesAsync()
    {
        var rates = new (string Name, string Code, decimal Rate, string Type, bool IsDefault, string Description)[]
        {
            ("Standard GST / VAT (18%)", "GST-18", 18.00m, "both", true, "Standard statutory General Sales Tax (18%)"),
            ("Standard GST (17%)", "GST-17", 17.00m, "both", false, "Standard Sales Tax 17%"),
            ("Reduced Retail / Pharma Tax (5%)", "GST-5", 5.00m, "both", false, "Reduced statutory tax rate (5%)"),
            ("Further Tax - Unregistered Buyer (4%)", "FT-4", 4.00m, "sales", false, "Further tax on wholesale supplies to unregistered persons"),
            ("Advance / Withholding Tax u/s 236G/H (1%)", "WHT-1", 1.00m, "withholding", false, "Advance withholding tax on distribution / retail supplies"),
            ("Zero-Rated / Essential Medicine (0%)", "ZERO-0", 0.00m, "both", false, "Zero-rated / Essential pharmaceutical or grocery supplies")
        };

        foreach (var r in rates)
        {
            var exists = await _db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM tax_rates WHERE code = @code", new { code = r.Code }) > 0;
            if (!exists)
            {
                await _db.InsertAsync("tax_rates", new Dictionary<string, object?>
                {
                    ["name"] = r.Name,
                    ["code"] = r.Code,
                    ["rate"] = r.Rate,
                    ["type"] = r.Type,
                    ["is_default"] = r.IsDefault,
                    ["is_active"] = true,
                    ["description"] = r.Description
                });
            }
        }
    }

    public async Task<List<Dictionary<string, object?>>> GetChartOfAccountsAsync(bool activeOnly = false)
    {
        await EnsureDefaultsAsync();
        if (!await _db.HasTableAsync("chart_of_accounts"))
        {
            return new List<Dictionary<string, object?>>();
        }

        var sql = activeOnly
            ? "SELECT * FROM chart_of_accounts WHERE is_active = 1 ORDER BY code ASC"
            : "SELECT * FROM chart_of_accounts ORDER BY code ASC";
        return await _db.QueryListAsync(sql);
    }

    public async Task<List<Dictionary<string, object?>>> GetTaxRatesAsync(bool activeOnly = false)
    {
        await EnsureDefaultsAsync();
        if (!await _db.HasTableAsync("tax_rates"))
        {
            return new List<Dictionary<string, object?>>();
        }

        var sql = activeOnly
            ? "SELECT * FROM tax_rates WHERE is_active = 1 ORDER BY is_default DESC, rate DESC, name ASC"
            : "SELECT * FROM tax_rates ORDER BY is_default DESC, rate DESC, name ASC";
        return await _db.QueryListAsync(sql);
    }

    public async Task<string> GenerateJournalEntryNumberAsync(string? date = null)
    {
        var ymd = (date ?? DateTime.UtcNow.ToString("yyyy-MM-dd")).Replace("-", "");
        if (ymd.Length > 8) ymd = ymd[..8];
        var prefix = $"JV-{ymd}-";

        return await _sequenceService.NextAsync(
            $"journal_entry:{ymd}",
            prefix,
            4,
            "journal_entries",
            "entry_number");
    }

    public async Task<Dictionary<string, object?>> CreateJournalEntryAsync(Dictionary<string, object?> data, long? userId = null, long? terminalId = null)
    {
        await EnsureDefaultsAsync();

        var rawLines = ExtractLines(data.GetValueOrDefault("lines"));
        if (rawLines.Count < 2)
        {
            throw new InvalidOperationException("A double-entry journal voucher requires at least 2 lines (Debit and Credit).");
        }

        var normalizedLines = new List<Dictionary<string, object?>>();
        decimal totalDebit = 0m;
        decimal totalCredit = 0m;

        for (int idx = 0; idx < rawLines.Count; idx++)
        {
            var line = rawLines[idx];
            var accountId = AccountService.ToLong(line.GetValueOrDefault("chart_of_account_id"));
            var debit = Math.Round(Math.Max(0m, AccountService.ToDecimal(line.GetValueOrDefault("debit"))), 2);
            var credit = Math.Round(Math.Max(0m, AccountService.ToDecimal(line.GetValueOrDefault("credit"))), 2);

            if (accountId <= 0)
            {
                throw new InvalidOperationException("Please select a valid account for each journal line.");
            }
            if (debit <= 0m && credit <= 0m)
            {
                continue;
            }
            if (debit > 0m && credit > 0m)
            {
                throw new InvalidOperationException("A single journal line cannot have both Debit and Credit amounts.");
            }

            totalDebit = Math.Round(totalDebit + debit, 2);
            totalCredit = Math.Round(totalCredit + credit, 2);

            var desc = (line.GetValueOrDefault("description")?.ToString() ?? "").Trim();
            var partyId = AccountService.ToLong(line.GetValueOrDefault("party_id"));

            normalizedLines.Add(new Dictionary<string, object?>
            {
                ["chart_of_account_id"] = accountId,
                ["party_type"] = line.GetValueOrDefault("party_type")?.ToString(),
                ["party_id"] = partyId > 0 ? partyId : null,
                ["description"] = string.IsNullOrEmpty(desc) ? null : desc,
                ["debit"] = debit,
                ["credit"] = credit
            });
        }

        if (normalizedLines.Count < 2)
        {
            throw new InvalidOperationException("At least two non-zero journal lines are required.");
        }

        if (totalDebit <= 0m || Math.Abs(totalDebit - totalCredit) > 0.009m)
        {
            throw new InvalidOperationException($"Journal entry is unbalanced: Total Debit ({totalDebit:F2}) must equal Total Credit ({totalCredit:F2}).");
        }

        var rawDate = data.GetValueOrDefault("entry_date")?.ToString();
        var entryDate = DateTime.TryParse(rawDate, out var parsedDt) ? parsedDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd");
        var entryNumber = (data.GetValueOrDefault("entry_number")?.ToString() ?? "").Trim();
        if (string.IsNullOrEmpty(entryNumber))
        {
            entryNumber = await GenerateJournalEntryNumberAsync(entryDate);
        }

        var treasuryAccIdRaw = AccountService.ToLong(data.GetValueOrDefault("account_id"));
        long? treasuryAccountId = treasuryAccIdRaw > 0 ? treasuryAccIdRaw : null;
        var description = (data.GetValueOrDefault("description")?.ToString() ?? "").Trim();
        if (string.IsNullOrEmpty(description)) description = "Manual Journal Entry";

        await _db.GetTableColumnsAsync("journal_entries");
        await _db.GetTableColumnsAsync("journal_entry_lines");
        await _db.GetTableColumnsAsync("accounts");
        await _db.GetTableColumnsAsync("account_transactions");
        await _db.GetTableColumnsAsync("cash_drawer_sessions");
        await _db.GetTableColumnsAsync("cash_drawer_transactions");

        long entryId;
        decimal netCashDelta = 0m;
        bool touchedDrawer = false;
        await using (var conn = await _db.OpenConnectionAsync())
        await using (var tx = await KarobarDb.BeginImmediateTransactionAsync(conn))
        {
            try
            {
                var cashBankCoaRows = await _db.QueryListAsync(
                    "SELECT id FROM chart_of_accounts WHERE sub_type = 'cash_bank' OR system_key = 'cash_and_banks'",
                    null,
                    conn,
                    tx);
                var cashBankCoaIds = cashBankCoaRows.Select(r => AccountService.ToLong(r["id"])).ToHashSet();

                entryId = await _db.InsertAsync("journal_entries", new Dictionary<string, object?>
                {
                    ["entry_number"] = entryNumber,
                    ["entry_date"] = entryDate,
                    ["reference_type"] = data.GetValueOrDefault("reference_type")?.ToString() ?? "manual",
                    ["reference_number"] = data.GetValueOrDefault("reference_number")?.ToString(),
                    ["account_id"] = treasuryAccountId,
                    ["description"] = description,
                    ["total_debit"] = totalDebit,
                    ["total_credit"] = totalCredit,
                    ["status"] = "posted",
                    ["created_by"] = userId
                }, conn, tx);

                foreach (var lineData in normalizedLines)
                {
                    lineData["journal_entry_id"] = entryId;
                    await _db.InsertAsync("journal_entry_lines", lineData, conn, tx);

                    var coaId = AccountService.ToLong(lineData["chart_of_account_id"]);
                    if (cashBankCoaIds.Contains(coaId))
                    {
                        var dr = AccountService.ToDecimal(lineData["debit"]);
                        var cr = AccountService.ToDecimal(lineData["credit"]);
                        netCashDelta = Math.Round(netCashDelta + (dr - cr), 2);
                    }
                }

                if (treasuryAccountId.HasValue && Math.Abs(netCashDelta) > 0.009m)
                {
                    var hasAccounts = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name = 'accounts'",
                        transaction: tx) > 0;

                    if (hasAccounts)
                    {
                        var treasuryAcc = await _db.QuerySingleOrDefaultAsync(
                            "SELECT * FROM accounts WHERE id = @id",
                            new { id = treasuryAccountId.Value },
                            conn,
                            tx);
                        if (treasuryAcc != null)
                        {
                            if (netCashDelta > 0m)
                            {
                                await _accountService.DepositInTxAsync(
                                    treasuryAcc,
                                    netCashDelta,
                                    "journal_entry",
                                    entryId,
                                    $"JV {entryNumber}: {description}",
                                    userId,
                                    conn,
                                    tx);
                            }
                            else
                            {
                                await _accountService.WithdrawInTxAsync(
                                    treasuryAcc,
                                    Math.Abs(netCashDelta),
                                    "journal_entry",
                                    entryId,
                                    $"JV {entryNumber}: {description}",
                                    userId,
                                    conn,
                                    tx);
                            }

                            if (_accountService.IsDefaultCashDrawerAccount(treasuryAcc))
                            {
                                var cashDrawerService = new CashDrawerService(_db);
                                var drawerTxn = await cashDrawerService.RecordTransactionInTxAsync(
                                    null,
                                    netCashDelta > 0m ? "cash_in" : "cash_out",
                                    Math.Abs(netCashDelta),
                                    "journal_entry",
                                    entryId,
                                    $"JV {entryNumber}: {description}",
                                    userId,
                                    terminalId,
                                    conn,
                                    tx);
                                touchedDrawer = drawerTxn != null;
                            }
                        }
                    }
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        if (touchedDrawer)
        {
            SyncPulseService.Bump("drawer");
        }

        var entry = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM journal_entries WHERE id = @id", new { id = entryId }))!;
        return await EnrichJournalEntryAsync(entry);
    }

    public async Task<Dictionary<string, object?>> ReverseJournalEntryAsync(long entryId, string reason, long? userId = null, long? terminalId = null)
    {
        await _db.GetTableColumnsAsync("journal_entries");
        await _db.GetTableColumnsAsync("accounts");
        await _db.GetTableColumnsAsync("account_transactions");
        await _db.GetTableColumnsAsync("cash_drawer_sessions");
        await _db.GetTableColumnsAsync("cash_drawer_transactions");

        bool touchedDrawer = false;
        await using (var conn = await _db.OpenConnectionAsync())
        await using (var tx = await KarobarDb.BeginImmediateTransactionAsync(conn))
        {
            try
            {
                var entry = await _db.QuerySingleOrDefaultAsync(
                                "SELECT * FROM journal_entries WHERE id = @id",
                                new { id = entryId },
                                conn,
                                tx)
                            ?? throw new KeyNotFoundException("Journal entry not found.");

                if (string.Equals(entry.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("This journal entry has already been reversed.");
                }

                var lines = await _db.QueryListAsync(
                    "SELECT * FROM journal_entry_lines WHERE journal_entry_id = @entryId",
                    new { entryId },
                    conn,
                    tx);
                var treasuryAccountId = AccountService.ToLong(entry.GetValueOrDefault("account_id"));
                var entryNumber = entry.GetValueOrDefault("entry_number")?.ToString() ?? $"JV-{entryId}";

                await _db.UpdateAsync("journal_entries", entryId, new Dictionary<string, object?>
                {
                    ["status"] = "reversed",
                    ["reversed_by"] = userId,
                    ["reversed_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["reversal_reason"] = reason.Trim()
                }, "id", conn, tx);

                if (treasuryAccountId > 0)
                {
                    var hasAccounts = await conn.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name = 'accounts'",
                        transaction: tx) > 0;

                    if (hasAccounts)
                    {
                        var treasuryAcc = await _db.QuerySingleOrDefaultAsync(
                            "SELECT * FROM accounts WHERE id = @id",
                            new { id = treasuryAccountId },
                            conn,
                            tx);
                        if (treasuryAcc != null)
                        {
                            var cashBankCoaRows = await _db.QueryListAsync(
                                "SELECT id FROM chart_of_accounts WHERE sub_type = 'cash_bank' OR system_key = 'cash_and_banks'",
                                null,
                                conn,
                                tx);
                            var cashBankCoaIds = cashBankCoaRows.Select(r => AccountService.ToLong(r["id"])).ToHashSet();

                            decimal netCashDelta = 0m;
                            foreach (var line in lines)
                            {
                                var coaId = AccountService.ToLong(line.GetValueOrDefault("chart_of_account_id"));
                                if (cashBankCoaIds.Contains(coaId))
                                {
                                    var dr = AccountService.ToDecimal(line.GetValueOrDefault("debit"));
                                    var cr = AccountService.ToDecimal(line.GetValueOrDefault("credit"));
                                    netCashDelta = Math.Round(netCashDelta + (dr - cr), 2);
                                }
                            }

                            if (Math.Abs(netCashDelta) > 0.009m)
                            {
                                if (netCashDelta > 0m)
                                {
                                    await _accountService.WithdrawInTxAsync(
                                        treasuryAcc,
                                        netCashDelta,
                                        "journal_entry_reversal",
                                        entryId,
                                        $"Reversal of JV {entryNumber}: {reason}",
                                        userId,
                                        conn,
                                        tx);
                                }
                                else
                                {
                                    await _accountService.DepositInTxAsync(
                                        treasuryAcc,
                                        Math.Abs(netCashDelta),
                                        "journal_entry_reversal",
                                        entryId,
                                        $"Reversal of JV {entryNumber}: {reason}",
                                        userId,
                                        conn,
                                        tx);
                                }

                                if (_accountService.IsDefaultCashDrawerAccount(treasuryAcc))
                                {
                                    var cashDrawerService = new CashDrawerService(_db);
                                    var drawerTxn = await cashDrawerService.RecordTransactionInTxAsync(
                                        null,
                                        netCashDelta > 0m ? "cash_out" : "cash_in",
                                        Math.Abs(netCashDelta),
                                        "journal_entry_reversal",
                                        entryId,
                                        $"Reversal of JV {entryNumber}: {reason}",
                                        userId,
                                        terminalId,
                                        conn,
                                        tx);
                                    touchedDrawer = drawerTxn != null;
                                }
                            }
                        }
                    }
                }

                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        if (touchedDrawer)
        {
            SyncPulseService.Bump("drawer");
        }

        var updated = (await _db.QuerySingleOrDefaultAsync("SELECT * FROM journal_entries WHERE id = @id", new { id = entryId }))!;
        return await EnrichJournalEntryAsync(updated);
    }

    protected async Task<Dictionary<string, object?>> ComputeOperationalMetricsAsync(string fromDate, string toDate)
    {
        var from = DateTime.TryParse(fromDate, out var fDt) ? fDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-01");
        var to = DateTime.TryParse(toDate, out var tDt) ? tDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd");
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var isHistorical = string.CompareOrdinal(to, today) < 0;

        await _accountService.ReconcileWithOpenCashDrawersAsync();
        var treasuryAccounts = await _db.HasTableAsync("accounts")
            ? await _db.QueryListAsync("SELECT * FROM accounts WHERE is_active = 1 ORDER BY type ASC, name ASC")
            : new List<Dictionary<string, object?>>();

        if (isHistorical && await _db.HasTableAsync("account_transactions") && treasuryAccounts.Count > 0)
        {
            var deltaRows = await _db.QueryListAsync(
                @"SELECT account_id,
                         COALESCE(SUM(CASE WHEN type IN ('deposit', 'transfer_in') THEN amount WHEN type IN ('withdrawal', 'transfer_out') THEN -amount WHEN type = 'adjustment' THEN amount ELSE 0 END), 0) as net_after
                  FROM account_transactions
                  WHERE DATE(transaction_date) > @to
                  GROUP BY account_id",
                new { to });
            var deltaMap = deltaRows.ToDictionary(r => AccountService.ToLong(r["account_id"]), r => AccountService.ToDecimal(r["net_after"]));
            foreach (var tAcc in treasuryAccounts)
            {
                var accId = AccountService.ToLong(tAcc["id"]);
                var deltaAfter = deltaMap.GetValueOrDefault(accId);
                tAcc["current_balance"] = Math.Round(AccountService.ToDecimal(tAcc.GetValueOrDefault("current_balance")) - deltaAfter, 2);
            }
        }
        var totalCashAndBanks = Math.Round(treasuryAccounts.Sum(a => AccountService.ToDecimal(a.GetValueOrDefault("current_balance"))), 2);

        var totalReceivables = await PaymentService.GetTotalReceivablesBalanceAsync(_db, isHistorical ? to : null);

        decimal staffAdvancesReceivable = 0m;
        if (await _db.HasTableAsync("employees"))
        {
            if (isHistorical && await _db.HasTableAsync("employee_advances"))
            {
                var netAdv = await _db.ExecuteScalarAsync<decimal?>(
                    @"SELECT COALESCE(SUM(CASE WHEN type = 'advance' THEN amount WHEN type IN ('repayment', 'payroll_deduction') THEN -amount ELSE 0 END), 0)
                      FROM employee_advances
                      WHERE DATE(date) <= @to",
                    new { to }) ?? 0m;
                staffAdvancesReceivable = Math.Round(Math.Max(0m, netAdv), 2);
            }
            else
            {
                var sumAdv = await _db.ExecuteScalarAsync<decimal?>("SELECT COALESCE(SUM(COALESCE(advance_balance, 0)), 0) FROM employees") ?? 0m;
                staffAdvancesReceivable = Math.Round(Math.Max(0m, sumAdv), 2);
            }
        }

        decimal totalPayables = 0m;
        if (await _db.HasTableAsync("supplier_ledgers"))
        {
            var ledgerSupRows = await _db.QueryListAsync("SELECT DISTINCT supplier_id FROM supplier_ledgers WHERE supplier_id IS NOT NULL");
            var creditInvSupRows = await _db.HasTableAsync("purchase_invoices")
                ? await _db.QueryListAsync("SELECT DISTINCT supplier_id FROM purchase_invoices WHERE invoice_type = 'credit' AND supplier_id IS NOT NULL AND (status IS NULL OR status != 'reversed')")
                : new List<Dictionary<string, object?>>();

            var allSupIds = ledgerSupRows.Concat(creditInvSupRows)
                .Select(r => AccountService.ToLong(r.GetValueOrDefault("supplier_id")))
                .Where(id => id > 0)
                .Distinct()
                .ToArray();

            if (allSupIds.Length > 0)
            {
                var netBalances = await PaymentService.GetBulkSupplierNetBalancesAsync(_db, allSupIds, isHistorical ? to : null);
                totalPayables = Math.Round(netBalances.Values.Where(v => v > 0m).Sum(), 2);
            }
        }

        var inventoryValuation = await _db.HasTableAsync("products")
            ? await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(COALESCE(quantity, 0) * CASE WHEN avg_price IS NULL OR avg_price <= 0 THEN COALESCE(unit_purchase_price, 0) ELSE avg_price END), 0)
                  FROM products
                  WHERE quantity > 0") ?? 0m
            : 0m;

        if (isHistorical)
        {
            var purchasesAfter = await _db.HasTableAsync("purchase_invoice_items")
                ? await _db.ExecuteScalarAsync<decimal?>(
                    @"SELECT COALESCE(SUM(COALESCE(pii.sub_total, 0)), 0)
                      FROM purchase_invoice_items pii
                      INNER JOIN purchase_invoices pi ON pi.id = pii.purchase_invoice_id
                      WHERE DATE(pi.posted_date) > @to
                        AND (pi.status IS NULL OR pi.status != 'reversed')",
                    new { to }) ?? 0m
                : 0m;

            var purchaseReturnsAfter = await _db.HasTableAsync("purchase_return_items")
                ? await _db.ExecuteScalarAsync<decimal?>(
                    @"SELECT COALESCE(SUM(COALESCE(pri.sub_total, 0)), 0)
                      FROM purchase_return_items pri
                      INNER JOIN purchase_returns pr ON pr.id = pri.purchase_return_id
                      WHERE DATE(pr.date) > @to",
                    new { to }) ?? 0m
                : 0m;

            var hasBonusInv = await _db.HasColumnAsync("sale_invoice_items", "bonus");
            var saleUnitsExprInv = hasBonusInv ? "(COALESCE(sii.quantity, 0) + COALESCE(sii.bonus, 0))" : "COALESCE(sii.quantity, 0)";
            var salesCogsAfter = await _db.HasTableAsync("sale_invoice_items")
                ? await _db.ExecuteScalarAsync<decimal?>(
                    $@"SELECT COALESCE(SUM({saleUnitsExprInv} * COALESCE(sii.cost_price_snapshot, CASE WHEN p.avg_price IS NULL OR p.avg_price <= 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END)), 0)
                       FROM sale_invoice_items sii
                       INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
                       INNER JOIN products p ON p.id = sii.product_id
                       WHERE DATE(si.date) > @to
                         AND (si.status IS NULL OR si.status != 'reversed')",
                    new { to }) ?? 0m
                : 0m;

            var saleReturnsCogsAfter = await _db.HasTableAsync("sale_return_items")
                ? await _db.ExecuteScalarAsync<decimal?>(
                    @"SELECT COALESCE(SUM(COALESCE(sri.unit_return_quantity, 0) * COALESCE(sri.cost_price_snapshot, CASE WHEN p.avg_price IS NULL OR p.avg_price <= 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END)), 0)
                      FROM sale_return_items sri
                      INNER JOIN sale_returns sr ON sr.id = sri.sale_return_id
                      INNER JOIN products p ON p.id = sri.product_id
                      WHERE DATE(sr.date) > @to",
                    new { to }) ?? 0m
                : 0m;

            var adjSignedAfter = await _db.HasTableAsync("stock_adjustment_items")
                ? await _db.ExecuteScalarAsync<decimal?>(
                    @"SELECT COALESCE(SUM(COALESCE(sai.diff_qty, 0) * COALESCE(sai.unit_purchase_price, 0)), 0)
                      FROM stock_adjustment_items sai
                      INNER JOIN stock_adjustments sa ON sa.id = sai.stock_adjustment_id
                      WHERE DATE(sa.posted_date) > @to",
                    new { to }) ?? 0m
                : 0m;

            inventoryValuation = inventoryValuation - purchasesAfter + purchaseReturnsAfter + salesCogsAfter - saleReturnsCogsAfter - adjSignedAfter;
        }
        inventoryValuation = Math.Round(Math.Max(0m, inventoryValuation), 2);

        var periodSales = await _db.HasTableAsync("sale_invoices")
            ? await _db.QuerySingleOrDefaultAsync(
                @"SELECT COALESCE(SUM(COALESCE(gross_amount, total - COALESCE(tax_amount, 0) + COALESCE(discount_amount, 0))), 0) as gross_sales,
                         COALESCE(SUM(COALESCE(discount_amount, 0)), 0) as discount_amount,
                         COALESCE(SUM(COALESCE(tax_amount, 0)), 0) as tax_amount,
                         COALESCE(SUM(COALESCE(total, 0)), 0) as net_total
                  FROM sale_invoices
                  WHERE DATE(date) BETWEEN @from AND @to
                    AND (status IS NULL OR status != 'reversed')",
                new { from, to })
            : null;

        var cumulativeSalesTax = await _db.HasTableAsync("sale_invoices")
            ? await _db.ExecuteScalarAsync<decimal?>(
                "SELECT COALESCE(SUM(COALESCE(tax_amount, 0)), 0) FROM sale_invoices WHERE DATE(date) <= @to AND (status IS NULL OR status != 'reversed')",
                new { to }) ?? 0m
            : 0m;

        var periodSaleReturns = await _db.HasTableAsync("sale_returns")
            ? await _db.QuerySingleOrDefaultAsync(
                @"SELECT COALESCE(SUM(COALESCE(total, 0) - COALESCE(tax_amount, 0)), 0) as net_returns,
                         COALESCE(SUM(COALESCE(tax_amount, 0)), 0) as tax_amount,
                         COALESCE(SUM(COALESCE(total, 0)), 0) as total_returns
                  FROM sale_returns
                  WHERE DATE(date) BETWEEN @from AND @to",
                new { from, to })
            : null;

        var cumulativeSaleReturnTax = await _db.HasTableAsync("sale_returns")
            ? await _db.ExecuteScalarAsync<decimal?>(
                "SELECT COALESCE(SUM(COALESCE(tax_amount, 0)), 0) FROM sale_returns WHERE DATE(date) <= @to",
                new { to }) ?? 0m
            : 0m;

        var periodPurchases = await _db.HasTableAsync("purchase_invoices")
            ? await _db.QuerySingleOrDefaultAsync(
                @"SELECT COALESCE(SUM(COALESCE(tax_amount, 0)), 0) as tax_amount,
                         COALESCE(SUM(COALESCE(total_amount, 0)), 0) as total_amount
                  FROM purchase_invoices
                  WHERE DATE(posted_date) BETWEEN @from AND @to
                    AND (status IS NULL OR status != 'reversed')",
                new { from, to })
            : null;

        var cumulativePurchaseTax = await _db.HasTableAsync("purchase_invoices")
            ? await _db.ExecuteScalarAsync<decimal?>(
                "SELECT COALESCE(SUM(COALESCE(tax_amount, 0)), 0) FROM purchase_invoices WHERE DATE(posted_date) <= @to AND (status IS NULL OR status != 'reversed')",
                new { to }) ?? 0m
            : 0m;

        var cumulativePurchaseReturnTax = await _db.HasTableAsync("purchase_returns")
            ? await _db.ExecuteScalarAsync<decimal?>(
                "SELECT COALESCE(SUM(COALESCE(tax_amount, 0)), 0) FROM purchase_returns WHERE DATE(date) <= @to",
                new { to }) ?? 0m
            : 0m;

        var periodPurchaseReturnTax = await _db.HasTableAsync("purchase_returns")
            ? await _db.ExecuteScalarAsync<decimal?>(
                "SELECT COALESCE(SUM(COALESCE(tax_amount, 0)), 0) FROM purchase_returns WHERE DATE(date) BETWEEN @from AND @to",
                new { from, to }) ?? 0m
            : 0m;

        var hasBonusCol = await _db.HasColumnAsync("sale_invoice_items", "bonus");
        var saleUnitsExpr = hasBonusCol ? "(COALESCE(sii.quantity, 0) + COALESCE(sii.bonus, 0))" : "COALESCE(sii.quantity, 0)";
        var cogsSales = await _db.HasTableAsync("sale_invoice_items")
            ? await _db.ExecuteScalarAsync<decimal?>(
                $@"SELECT COALESCE(SUM({saleUnitsExpr} * COALESCE(sii.cost_price_snapshot, CASE WHEN p.avg_price IS NULL OR p.avg_price <= 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END)), 0)
                   FROM sale_invoice_items sii
                   INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
                   INNER JOIN products p ON p.id = sii.product_id
                   WHERE DATE(si.date) BETWEEN @from AND @to
                     AND (si.status IS NULL OR si.status != 'reversed')",
                new { from, to }) ?? 0m
            : 0m;

        var cogsReturns = await _db.HasTableAsync("sale_return_items")
            ? await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(COALESCE(sri.unit_return_quantity, 0) * COALESCE(sri.cost_price_snapshot, CASE WHEN p.avg_price IS NULL OR p.avg_price <= 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END)), 0)
                  FROM sale_return_items sri
                  INNER JOIN sale_returns sr ON sr.id = sri.sale_return_id
                  INNER JOIN products p ON p.id = sri.product_id
                  WHERE DATE(sr.date) BETWEEN @from AND @to",
                new { from, to }) ?? 0m
            : 0m;

        var periodCogs = Math.Round(Math.Max(0m, cogsSales - cogsReturns), 2);

        decimal disposalLoss = 0m;
        if (await _db.HasTableAsync("stock_disposals"))
        {
            var costCol = await _db.HasColumnAsync("stock_disposals", "total_cost_value") ? "total_cost_value" : "total_cost";
            if (await _db.HasColumnAsync("stock_disposals", costCol))
            {
                disposalLoss += await _db.ExecuteScalarAsync<decimal?>(
                    $@"SELECT COALESCE(SUM(COALESCE({costCol}, 0)), 0)
                       FROM stock_disposals
                       WHERE COALESCE(DATE(disposed_at), DATE(disposal_date)) BETWEEN @from AND @to
                         AND status = 'disposed'",
                    new { from, to }) ?? 0m;
            }
        }

        if (await _db.HasTableAsync("expenses") && await _db.HasTableAsync("expense_categories"))
        {
            var shrinkSql = @"
                SELECT COALESCE(SUM(e.amount), 0)
                FROM expenses e
                INNER JOIN expense_categories ec ON ec.id = e.expense_category_id
                WHERE DATE(e.date) BETWEEN @from AND @to
                  AND e.deleted_at IS NULL
                  AND ec.code IN ('stock_disposal_writeoff', 'stock_shrinkage_writeoff')";
            if (await _db.HasTableAsync("stock_disposals") && await _db.HasColumnAsync("stock_disposals", "expense_id"))
            {
                shrinkSql += " AND NOT EXISTS (SELECT 1 FROM stock_disposals sd WHERE sd.expense_id = e.id AND sd.status = 'disposed')";
            }
            disposalLoss += await _db.ExecuteScalarAsync<decimal?>(shrinkSql, new { from, to }) ?? 0m;
        }
        var periodShrinkage = Math.Round(Math.Max(0m, disposalLoss), 2);

        decimal periodExpenses = 0m;
        if (await _db.HasTableAsync("expenses"))
        {
            periodExpenses = Math.Round(await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(e.amount), 0)
                  FROM expenses e
                  LEFT JOIN expense_categories ec ON ec.id = e.expense_category_id
                  WHERE DATE(e.date) BETWEEN @from AND @to
                    AND e.deleted_at IS NULL
                    AND (ec.code IS NULL OR ec.code NOT IN ('stock_disposal_writeoff', 'stock_shrinkage_writeoff'))",
                new { from, to }) ?? 0m, 2);
        }

        return new Dictionary<string, object?>
        {
            ["treasury_accounts"] = treasuryAccounts,
            ["cash_and_banks"] = totalCashAndBanks,
            ["accounts_receivable"] = totalReceivables,
            ["staff_advances_receivable"] = staffAdvancesReceivable,
            ["accounts_payable"] = totalPayables,
            ["inventory_valuation"] = inventoryValuation,
            ["output_tax_payable"] = Math.Round(Math.Max(0m, cumulativeSalesTax - cumulativeSaleReturnTax), 2),
            ["input_tax_receivable"] = Math.Round(Math.Max(0m, cumulativePurchaseTax - cumulativePurchaseReturnTax), 2),
            ["gross_sales"] = Math.Round(AccountService.ToDecimal(periodSales?.GetValueOrDefault("gross_sales")), 2),
            ["sales_discounts"] = Math.Round(AccountService.ToDecimal(periodSales?.GetValueOrDefault("discount_amount")), 2),
            ["sales_returns"] = Math.Round(Math.Max(0m, AccountService.ToDecimal(periodSaleReturns?.GetValueOrDefault("net_returns"))), 2),
            ["period_cogs"] = periodCogs,
            ["period_shrinkage"] = periodShrinkage,
            ["period_expenses"] = periodExpenses,
            ["period_output_tax"] = Math.Round(AccountService.ToDecimal(periodSales?.GetValueOrDefault("tax_amount")) - AccountService.ToDecimal(periodSaleReturns?.GetValueOrDefault("tax_amount")), 2),
            ["period_input_tax"] = Math.Round(AccountService.ToDecimal(periodPurchases?.GetValueOrDefault("tax_amount")) - periodPurchaseReturnTax, 2)
        };
    }

    public async Task<Dictionary<string, object?>> GetTrialBalanceAsync(string fromDate, string toDate)
    {
        await EnsureDefaultsAsync();
        var from = DateTime.TryParse(fromDate, out var fDt) ? fDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-01");
        var to = DateTime.TryParse(toDate, out var tDt) ? tDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd");

        var ops = await ComputeOperationalMetricsAsync(from, to);
        _lastTreasuryAccounts = ops["treasury_accounts"] as List<Dictionary<string, object?>> ?? new List<Dictionary<string, object?>>();
        var accounts = await _db.QueryListAsync("SELECT * FROM chart_of_accounts WHERE is_active = 1 ORDER BY code ASC");

        var manualTotalsRows = await _db.HasTableAsync("journal_entry_lines") && await _db.HasTableAsync("journal_entries")
            ? await _db.QueryListAsync(
                @"SELECT jel.chart_of_account_id,
                         COALESCE(SUM(CASE WHEN je.account_id IS NOT NULL THEN 0 ELSE jel.debit END), 0) as unsynced_debit,
                         COALESCE(SUM(CASE WHEN je.account_id IS NOT NULL THEN 0 ELSE jel.credit END), 0) as unsynced_credit,
                         COALESCE(SUM(jel.debit), 0) as total_debit,
                         COALESCE(SUM(jel.credit), 0) as total_credit
                  FROM journal_entry_lines jel
                  INNER JOIN journal_entries je ON je.id = jel.journal_entry_id
                  WHERE je.status = 'posted' AND DATE(je.entry_date) <= @to
                  GROUP BY jel.chart_of_account_id",
                new { to })
            : new List<Dictionary<string, object?>>();

        var manualTotals = manualTotalsRows.ToDictionary(r => AccountService.ToLong(r["chart_of_account_id"]));

        var rows = new List<Dictionary<string, object?>>();
        decimal sumDebit = 0m;
        decimal sumCredit = 0m;
        int? capitalRowIndex = null;

        foreach (var acc in accounts)
        {
            var accId = AccountService.ToLong(acc["id"]);
            manualTotals.TryGetValue(accId, out var m);
            var manDebit = Math.Round(AccountService.ToDecimal(m?.GetValueOrDefault("total_debit")), 2);
            var manCredit = Math.Round(AccountService.ToDecimal(m?.GetValueOrDefault("total_credit")), 2);
            var opening = Math.Round(AccountService.ToDecimal(acc.GetValueOrDefault("opening_balance")), 2);
            var sysKey = acc.GetValueOrDefault("system_key")?.ToString() ?? "";
            var normalBalance = acc.GetValueOrDefault("normal_balance")?.ToString() ?? "debit";

            decimal baseDebit = 0m;
            decimal baseCredit = 0m;

            switch (sysKey)
            {
                case "cash_and_banks":
                    var unsyncedDelta = Math.Round(AccountService.ToDecimal(m?.GetValueOrDefault("unsynced_debit")) - AccountService.ToDecimal(m?.GetValueOrDefault("unsynced_credit")), 2);
                    var val = Math.Round(AccountService.ToDecimal(ops["cash_and_banks"]) + unsyncedDelta + opening, 2);
                    if (val >= 0m) baseDebit = val;
                    else baseCredit = Math.Abs(val);
                    manDebit = 0m;
                    manCredit = 0m;
                    break;
                case "accounts_receivable":
                    baseDebit = Math.Round(AccountService.ToDecimal(ops["accounts_receivable"]) + opening, 2);
                    break;
                case "staff_advances_receivable":
                    baseDebit = Math.Round(AccountService.ToDecimal(ops["staff_advances_receivable"]) + opening, 2);
                    break;
                case "merchandise_inventory":
                    baseDebit = Math.Round(AccountService.ToDecimal(ops["inventory_valuation"]) + opening, 2);
                    break;
                case "input_tax_receivable":
                    baseDebit = Math.Round(AccountService.ToDecimal(ops["input_tax_receivable"]) + opening, 2);
                    break;
                case "accounts_payable":
                    baseCredit = Math.Round(AccountService.ToDecimal(ops["accounts_payable"]) + opening, 2);
                    break;
                case "output_tax_payable":
                    baseCredit = Math.Round(AccountService.ToDecimal(ops["output_tax_payable"]) + opening, 2);
                    break;
                case "gross_sales_revenue":
                    baseCredit = Math.Round(AccountService.ToDecimal(ops["gross_sales"]) + opening, 2);
                    break;
                case "sales_returns":
                    baseDebit = Math.Round(AccountService.ToDecimal(ops["sales_returns"]) + opening, 2);
                    break;
                case "sales_discounts":
                    baseDebit = Math.Round(AccountService.ToDecimal(ops["sales_discounts"]) + opening, 2);
                    break;
                case "cost_of_goods_sold":
                    baseDebit = Math.Round(AccountService.ToDecimal(ops["period_cogs"]) + opening, 2);
                    break;
                case "inventory_shrinkage":
                    baseDebit = Math.Round(AccountService.ToDecimal(ops["period_shrinkage"]) + opening, 2);
                    break;
                case "operating_expenses":
                    baseDebit = Math.Round(AccountService.ToDecimal(ops["period_expenses"]) + opening, 2);
                    break;
                default:
                    if (normalBalance.Equals("debit", StringComparison.OrdinalIgnoreCase)) baseDebit = opening;
                    else baseCredit = opening;
                    break;
            }

            var net = Math.Round((baseDebit + manDebit) - (baseCredit + manCredit), 2);
            var rowDebit = net > 0m ? net : 0m;
            var rowCredit = net < 0m ? Math.Abs(net) : 0m;

            if (sysKey == "owners_capital")
            {
                capitalRowIndex = rows.Count;
            }

            sumDebit = Math.Round(sumDebit + rowDebit, 2);
            sumCredit = Math.Round(sumCredit + rowCredit, 2);

            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = accId,
                ["code"] = acc.GetValueOrDefault("code"),
                ["name"] = acc.GetValueOrDefault("name"),
                ["account_type"] = acc.GetValueOrDefault("account_type"),
                ["sub_type"] = acc.GetValueOrDefault("sub_type"),
                ["normal_balance"] = normalBalance,
                ["system_key"] = acc.GetValueOrDefault("system_key"),
                ["is_system"] = AccountService.IsTruthy(acc.GetValueOrDefault("is_system")),
                ["opening_balance"] = opening,
                ["manual_debit"] = Math.Round(AccountService.ToDecimal(m?.GetValueOrDefault("total_debit")), 2),
                ["manual_credit"] = Math.Round(AccountService.ToDecimal(m?.GetValueOrDefault("total_credit")), 2),
                ["debit"] = rowDebit,
                ["credit"] = rowCredit,
                ["net_balance"] = rowDebit > 0m ? rowDebit : rowCredit,
                ["balance_side"] = rowDebit > 0m ? "Dr" : (rowCredit > 0m ? "Cr" : "—")
            });
        }

        if (capitalRowIndex.HasValue && Math.Abs(sumDebit - sumCredit) > 0.001m)
        {
            var diff = Math.Round(sumDebit - sumCredit, 2);
            var capRow = rows[capitalRowIndex.Value];
            sumDebit = Math.Round(sumDebit - AccountService.ToDecimal(capRow["debit"]), 2);
            sumCredit = Math.Round(sumCredit - AccountService.ToDecimal(capRow["credit"]), 2);

            var currentCapNet = Math.Round(AccountService.ToDecimal(capRow["credit"]) - AccountService.ToDecimal(capRow["debit"]) + diff, 2);
            if (currentCapNet >= 0m)
            {
                capRow["credit"] = currentCapNet;
                capRow["debit"] = 0m;
                capRow["net_balance"] = currentCapNet;
                capRow["balance_side"] = currentCapNet > 0m ? "Cr" : "—";
            }
            else
            {
                capRow["debit"] = Math.Abs(currentCapNet);
                capRow["credit"] = 0m;
                capRow["net_balance"] = Math.Abs(currentCapNet);
                capRow["balance_side"] = "Dr";
            }

            sumDebit = Math.Round(sumDebit + AccountService.ToDecimal(capRow["debit"]), 2);
            sumCredit = Math.Round(sumCredit + AccountService.ToDecimal(capRow["credit"]), 2);
        }

        var byClass = new Dictionary<string, Dictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase)
        {
            ["asset"] = new() { ["debit"] = 0m, ["credit"] = 0m },
            ["liability"] = new() { ["debit"] = 0m, ["credit"] = 0m },
            ["equity"] = new() { ["debit"] = 0m, ["credit"] = 0m },
            ["revenue"] = new() { ["debit"] = 0m, ["credit"] = 0m },
            ["expense"] = new() { ["debit"] = 0m, ["credit"] = 0m }
        };

        foreach (var r in rows)
        {
            var type = r.GetValueOrDefault("account_type")?.ToString() ?? "";
            if (byClass.TryGetValue(type, out var bucket))
            {
                bucket["debit"] = Math.Round(bucket["debit"] + AccountService.ToDecimal(r["debit"]), 2);
                bucket["credit"] = Math.Round(bucket["credit"] + AccountService.ToDecimal(r["credit"]), 2);
            }
        }

        return new Dictionary<string, object?>
        {
            ["period"] = new Dictionary<string, object?> { ["from"] = from, ["to"] = to },
            ["rows"] = rows,
            ["by_class"] = byClass,
            ["total_debit"] = sumDebit,
            ["total_credit"] = sumCredit,
            ["difference"] = Math.Round(Math.Abs(sumDebit - sumCredit), 2),
            ["is_balanced"] = Math.Abs(sumDebit - sumCredit) < 0.01m
        };
    }

    public async Task<Dictionary<string, object?>> GetBalanceSheetAsync(string asOfDate)
    {
        var asOf = DateTime.TryParse(asOfDate, out var dt) ? dt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd");
        var yearStart = $"{asOf[..4]}-01-01";
        var tb = await GetTrialBalanceAsync(yearStart, asOf);
        var tbRows = tb["rows"] as List<Dictionary<string, object?>> ?? new List<Dictionary<string, object?>>();
        var treasuryAccounts = _lastTreasuryAccounts ?? new List<Dictionary<string, object?>>();

        var assets = new List<Dictionary<string, object?>>();
        var liabilities = new List<Dictionary<string, object?>>();
        var equity = new List<Dictionary<string, object?>>();

        decimal totalAssets = 0m;
        decimal totalLiabilities = 0m;
        decimal totalRevenue = 0m;
        decimal totalExpenses = 0m;

        foreach (var row in tbRows)
        {
            var accType = row.GetValueOrDefault("account_type")?.ToString() ?? "";
            var dr = AccountService.ToDecimal(row.GetValueOrDefault("debit"));
            var cr = AccountService.ToDecimal(row.GetValueOrDefault("credit"));
            var sysKey = row.GetValueOrDefault("system_key")?.ToString() ?? "";

            if (accType == "asset")
            {
                var amount = Math.Round(dr - cr, 2);
                totalAssets = Math.Round(totalAssets + amount, 2);
                var copy = new Dictionary<string, object?>(row)
                {
                    ["amount"] = amount,
                    ["sub_accounts"] = sysKey == "cash_and_banks"
                        ? treasuryAccounts.Select(a => new Dictionary<string, object?>
                        {
                            ["id"] = a.GetValueOrDefault("id"),
                            ["name"] = a.GetValueOrDefault("name"),
                            ["type"] = a.GetValueOrDefault("type"),
                            ["balance"] = Math.Round(AccountService.ToDecimal(a.GetValueOrDefault("current_balance")), 2)
                        }).ToList()
                        : new List<Dictionary<string, object?>>()
                };
                assets.Add(copy);
            }
            else if (accType == "liability")
            {
                var amount = Math.Round(cr - dr, 2);
                totalLiabilities = Math.Round(totalLiabilities + amount, 2);
                var copy = new Dictionary<string, object?>(row) { ["amount"] = amount };
                liabilities.Add(copy);
            }
            else if (accType == "revenue")
            {
                totalRevenue = Math.Round(totalRevenue + (cr - dr), 2);
            }
            else if (accType == "expense")
            {
                totalExpenses = Math.Round(totalExpenses + (dr - cr), 2);
            }
            else if (accType == "equity")
            {
                var amount = Math.Round(cr - dr, 2);
                var copy = new Dictionary<string, object?>(row) { ["amount"] = amount };
                equity.Add(copy);
            }
        }

        var periodNetIncome = Math.Round(totalRevenue - totalExpenses, 2);
        decimal totalEquity = 0m;
        foreach (var eqRow in equity)
        {
            if (string.Equals(eqRow.GetValueOrDefault("system_key")?.ToString(), "retained_earnings", StringComparison.OrdinalIgnoreCase))
            {
                eqRow["amount"] = Math.Round(AccountService.ToDecimal(eqRow["amount"]) + periodNetIncome, 2);
                eqRow["includes_net_income"] = periodNetIncome;
            }
            totalEquity = Math.Round(totalEquity + AccountService.ToDecimal(eqRow["amount"]), 2);
        }

        var totalLiabilitiesAndEquity = Math.Round(totalLiabilities + totalEquity, 2);

        return new Dictionary<string, object?>
        {
            ["as_of"] = asOf,
            ["assets"] = assets,
            ["liabilities"] = liabilities,
            ["equity"] = equity,
            ["total_assets"] = totalAssets,
            ["total_liabilities"] = totalLiabilities,
            ["period_net_income"] = periodNetIncome,
            ["total_equity"] = totalEquity,
            ["total_liabilities_and_equity"] = totalLiabilitiesAndEquity,
            ["is_balanced"] = Math.Abs(totalAssets - totalLiabilitiesAndEquity) < 0.01m
        };
    }

    public async Task<Dictionary<string, object?>> GetCashFlowStatementAsync(string fromDate, string toDate)
    {
        var from = DateTime.TryParse(fromDate, out var fDt) ? fDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-01");
        var to = DateTime.TryParse(toDate, out var tDt) ? tDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd");

        var rawCashOnSales = await _db.HasTableAsync("sale_invoices")
            ? await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(CASE WHEN COALESCE(invoice_type, 'debit') = 'debit' AND COALESCE(total_receive, 0) <= 0 THEN COALESCE(total, 0) ELSE COALESCE(total_receive, 0) END), 0)
                  FROM sale_invoices
                  WHERE DATE(date) BETWEEN @from AND @to
                    AND (status IS NULL OR status != 'reversed')",
                new { from, to }) ?? 0m
            : 0m;

        var allocatedOnPeriodSales = await _db.HasTableAsync("payment_allocations") && await _db.HasTableAsync("payments")
            ? await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(pa.amount), 0)
                  FROM payment_allocations pa
                  INNER JOIN sale_invoices si ON si.id = pa.invoice_id AND pa.invoice_type = 'sale_invoice'
                  INNER JOIN payments p ON p.id = pa.payment_id
                  WHERE DATE(si.date) BETWEEN @from AND @to
                    AND (si.status IS NULL OR si.status != 'reversed')
                    AND p.status = 'posted'",
                new { from, to }) ?? 0m
            : 0m;

        var cashOnSales = Math.Max(0m, Math.Round(rawCashOnSales - allocatedOnPeriodSales, 2));

        decimal customerPaymentReceipts = 0m;
        if (await _db.HasTableAsync("payments"))
        {
            var hasCheque = await _db.HasColumnAsync("payments", "cheque_status");
            var sql = @"SELECT COALESCE(SUM(amount), 0) FROM payments
                        WHERE party_type = 'customer'
                          AND DATE(payment_date) BETWEEN @from AND @to
                          AND (status IS NULL OR status != 'reversed')
                          AND (payment_method IS NULL OR payment_method != 'credit_note')";
            if (hasCheque) sql += " AND (cheque_status IS NULL OR cheque_status = 'cleared')";
            customerPaymentReceipts = await _db.ExecuteScalarAsync<decimal?>(sql, new { from, to }) ?? 0m;
        }

        var purchaseReturnCashRefunds = await _db.HasTableAsync("purchase_returns")
            ? await _db.ExecuteScalarAsync<decimal?>(
                "SELECT COALESCE(SUM(total), 0) FROM purchase_returns WHERE DATE(date) BETWEEN @from AND @to AND settlement_mode = 'cash_refund'",
                new { from, to }) ?? 0m
            : 0m;

        var rawCashOnPurchases = await _db.HasTableAsync("purchase_invoices")
            ? await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(COALESCE(total_paid, 0)), 0)
                  FROM purchase_invoices
                  WHERE DATE(posted_date) BETWEEN @from AND @to
                    AND (status IS NULL OR status != 'reversed')",
                new { from, to }) ?? 0m
            : 0m;

        var allocatedOnPeriodPurchases = await _db.HasTableAsync("payment_allocations") && await _db.HasTableAsync("payments")
            ? await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(pa.amount), 0)
                  FROM payment_allocations pa
                  INNER JOIN purchase_invoices pi ON pi.id = pa.invoice_id AND pa.invoice_type = 'purchase_invoice'
                  INNER JOIN payments p ON p.id = pa.payment_id
                  WHERE DATE(pi.posted_date) BETWEEN @from AND @to
                    AND (pi.status IS NULL OR pi.status != 'reversed')
                    AND p.status = 'posted'",
                new { from, to }) ?? 0m
            : 0m;

        var cashOnPurchases = Math.Max(0m, Math.Round(rawCashOnPurchases - allocatedOnPeriodPurchases, 2));

        decimal supplierPayments = 0m;
        if (await _db.HasTableAsync("payments"))
        {
            var hasCheque = await _db.HasColumnAsync("payments", "cheque_status");
            var sql = @"SELECT COALESCE(SUM(amount), 0) FROM payments
                        WHERE party_type = 'supplier'
                          AND DATE(payment_date) BETWEEN @from AND @to
                          AND (status IS NULL OR status != 'reversed')
                          AND (payment_method IS NULL OR payment_method != 'credit_note')";
            if (hasCheque) sql += " AND (cheque_status IS NULL OR cheque_status = 'cleared')";
            supplierPayments = await _db.ExecuteScalarAsync<decimal?>(sql, new { from, to }) ?? 0m;
        }

        var saleReturnCashRefunds = await _db.HasTableAsync("sale_returns")
            ? await _db.ExecuteScalarAsync<decimal?>(
                "SELECT COALESCE(SUM(total), 0) FROM sale_returns WHERE DATE(date) BETWEEN @from AND @to AND settlement_mode = 'cash_refund'",
                new { from, to }) ?? 0m
            : 0m;

        var operatingExpensesPaid = await _db.HasTableAsync("expenses")
            ? await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(e.amount), 0)
                  FROM expenses e
                  LEFT JOIN expense_categories ec ON ec.id = e.expense_category_id
                  WHERE DATE(e.date) BETWEEN @from AND @to
                    AND e.deleted_at IS NULL
                    AND (ec.code IS NULL OR ec.code NOT IN ('stock_disposal_writeoff', 'stock_shrinkage_writeoff'))",
                new { from, to }) ?? 0m
            : 0m;

        decimal staffAdvancesDisbursed = 0m;
        decimal staffAdvancesRepaid = 0m;
        if (await _db.HasTableAsync("employee_advances"))
        {
            staffAdvancesDisbursed = await _db.ExecuteScalarAsync<decimal?>(
                "SELECT COALESCE(SUM(amount), 0) FROM employee_advances WHERE type = 'advance' AND DATE(date) BETWEEN @from AND @to",
                new { from, to }) ?? 0m;
            staffAdvancesRepaid = await _db.ExecuteScalarAsync<decimal?>(
                "SELECT COALESCE(SUM(amount), 0) FROM employee_advances WHERE type = 'repayment' AND DATE(date) BETWEEN @from AND @to",
                new { from, to }) ?? 0m;
        }

        decimal investingOutflows = 0m;
        decimal capitalInjected = 0m;
        decimal ownerDrawings = 0m;
        if (await _db.HasTableAsync("journal_entry_lines") && await _db.HasTableAsync("journal_entries"))
        {
            investingOutflows = await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(jel.debit - jel.credit), 0)
                  FROM journal_entry_lines jel
                  INNER JOIN journal_entries je ON je.id = jel.journal_entry_id
                  INNER JOIN chart_of_accounts coa ON coa.id = jel.chart_of_account_id
                  WHERE je.status = 'posted' AND DATE(je.entry_date) BETWEEN @from AND @to AND coa.sub_type = 'fixed_asset'",
                new { from, to }) ?? 0m;

            capitalInjected = await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(jel.credit - jel.debit), 0)
                  FROM journal_entry_lines jel
                  INNER JOIN journal_entries je ON je.id = jel.journal_entry_id
                  INNER JOIN chart_of_accounts coa ON coa.id = jel.chart_of_account_id
                  WHERE je.status = 'posted' AND DATE(je.entry_date) BETWEEN @from AND @to AND coa.system_key = 'owners_capital'",
                new { from, to }) ?? 0m;

            ownerDrawings = await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(jel.debit - jel.credit), 0)
                  FROM journal_entry_lines jel
                  INNER JOIN journal_entries je ON je.id = jel.journal_entry_id
                  INNER JOIN chart_of_accounts coa ON coa.id = jel.chart_of_account_id
                  WHERE je.status = 'posted' AND DATE(je.entry_date) BETWEEN @from AND @to AND coa.system_key = 'owners_drawings'",
                new { from, to }) ?? 0m;
        }

        var totalOperatingIn = Math.Round(cashOnSales + customerPaymentReceipts + purchaseReturnCashRefunds + staffAdvancesRepaid, 2);
        var totalOperatingOut = Math.Round(cashOnPurchases + supplierPayments + saleReturnCashRefunds + operatingExpensesPaid + staffAdvancesDisbursed, 2);
        var netOperatingCashFlow = Math.Round(totalOperatingIn - totalOperatingOut, 2);
        var netInvestingCashFlow = Math.Round(-investingOutflows, 2);
        var netFinancingCashFlow = Math.Round(capitalInjected - ownerDrawings, 2);
        var netCashChange = Math.Round(netOperatingCashFlow + netInvestingCashFlow + netFinancingCashFlow, 2);

        await _accountService.ReconcileWithOpenCashDrawersAsync();
        var closingCashAndBank = await _db.HasTableAsync("accounts")
            ? Math.Round(await _db.ExecuteScalarAsync<decimal?>("SELECT COALESCE(SUM(current_balance), 0) FROM accounts WHERE is_active = 1") ?? 0m, 2)
            : 0m;

        return new Dictionary<string, object?>
        {
            ["period"] = new Dictionary<string, object?> { ["from"] = from, ["to"] = to },
            ["operating"] = new Dictionary<string, object?>
            {
                ["cash_from_sales"] = Math.Round(cashOnSales, 2),
                ["customer_payment_vouchers"] = Math.Round(customerPaymentReceipts, 2),
                ["purchase_return_refunds"] = Math.Round(purchaseReturnCashRefunds, 2),
                ["staff_advance_repayments"] = Math.Round(staffAdvancesRepaid, 2),
                ["cash_paid_for_purchases"] = Math.Round(cashOnPurchases, 2),
                ["supplier_payment_vouchers"] = Math.Round(supplierPayments, 2),
                ["sale_return_refunds"] = Math.Round(saleReturnCashRefunds, 2),
                ["operating_expenses_paid"] = Math.Round(operatingExpensesPaid, 2),
                ["staff_advances_disbursed"] = Math.Round(staffAdvancesDisbursed, 2),
                ["net_operating_cash_flow"] = netOperatingCashFlow
            },
            ["investing"] = new Dictionary<string, object?>
            {
                ["fixed_asset_acquisitions"] = Math.Round(investingOutflows, 2),
                ["net_investing_cash_flow"] = netInvestingCashFlow
            },
            ["financing"] = new Dictionary<string, object?>
            {
                ["owner_capital_contributions"] = Math.Round(capitalInjected, 2),
                ["owner_drawings"] = Math.Round(ownerDrawings, 2),
                ["net_financing_cash_flow"] = netFinancingCashFlow
            },
            ["net_cash_change"] = netCashChange,
            ["closing_cash_and_bank"] = closingCashAndBank
        };
    }

    public async Task<Dictionary<string, object?>> GetGeneralLedgerAsync(IDictionary<string, string?>? filters = null)
    {
        await EnsureDefaultsAsync();
        var rawFrom = filters != null && filters.TryGetValue("from", out var f) ? f : null;
        var rawTo = filters != null && filters.TryGetValue("to", out var t) ? t : null;
        var from = DateTime.TryParse(rawFrom, out var fDt) ? fDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-01");
        var to = DateTime.TryParse(rawTo, out var tDt) ? tDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd");
        var sourceFilter = (filters != null && filters.TryGetValue("source", out var src) && !string.IsNullOrWhiteSpace(src) ? src : "all").ToLowerInvariant();
        var search = (filters != null && filters.TryGetValue("q", out var qVal) && !string.IsNullOrWhiteSpace(qVal) ? qVal : "").Trim().ToLowerInvariant();

        var entries = new List<Dictionary<string, object?>>();

        if ((sourceFilter is "all" or "manual") && await _db.HasTableAsync("journal_entries"))
        {
            var manualList = await _db.QueryListAsync(
                @"SELECT je.*, u.name as creator_name
                  FROM journal_entries je
                  LEFT JOIN users u ON u.id = je.created_by
                  WHERE DATE(je.entry_date) BETWEEN @from AND @to
                  ORDER BY je.entry_date DESC, je.id DESC",
                new { from, to });

            foreach (var je in manualList)
            {
                var jeId = AccountService.ToLong(je["id"]);
                var lineRows = await _db.QueryListAsync(
                    @"SELECT jel.*, coa.code as account_code, coa.name as account_name
                      FROM journal_entry_lines jel
                      LEFT JOIN chart_of_accounts coa ON coa.id = jel.chart_of_account_id
                      WHERE jel.journal_entry_id = @jeId
                      ORDER BY jel.id ASC",
                    new { jeId });

                var refType = je.GetValueOrDefault("reference_type")?.ToString() ?? "manual";
                var eDate = je.GetValueOrDefault("entry_date")?.ToString() ?? to;
                if (eDate.Length >= 10) eDate = eDate[..10];

                entries.Add(new Dictionary<string, object?>
                {
                    ["id"] = $"JV-{jeId}",
                    ["raw_id"] = jeId,
                    ["is_manual"] = true,
                    ["entry_number"] = je.GetValueOrDefault("entry_number"),
                    ["entry_date"] = eDate,
                    ["source_type"] = refType,
                    ["source_label"] = refType.Replace('_', ' ').ToUpperInvariant(),
                    ["reference_number"] = je.GetValueOrDefault("reference_number"),
                    ["description"] = je.GetValueOrDefault("description"),
                    ["total_debit"] = Math.Round(AccountService.ToDecimal(je.GetValueOrDefault("total_debit")), 2),
                    ["total_credit"] = Math.Round(AccountService.ToDecimal(je.GetValueOrDefault("total_credit")), 2),
                    ["status"] = je.GetValueOrDefault("status") ?? "posted",
                    ["created_by_name"] = je.GetValueOrDefault("creator_name") ?? "System",
                    ["lines"] = lineRows.Select(l => new Dictionary<string, object?>
                    {
                        ["account_code"] = l.GetValueOrDefault("account_code") ?? "—",
                        ["account_name"] = l.GetValueOrDefault("account_name") ?? "Account",
                        ["description"] = l.GetValueOrDefault("description"),
                        ["debit"] = Math.Round(AccountService.ToDecimal(l.GetValueOrDefault("debit")), 2),
                        ["credit"] = Math.Round(AccountService.ToDecimal(l.GetValueOrDefault("credit")), 2)
                    }).ToList()
                });
            }
        }

        if ((sourceFilter is "all" or "sale") && await _db.HasTableAsync("sale_invoices"))
        {
            var sales = await _db.QueryListAsync(
                @"SELECT si.*, c.name as customer_name
                  FROM sale_invoices si
                  LEFT JOIN customers c ON c.id = si.customer_id
                  WHERE DATE(si.date) BETWEEN @from AND @to
                    AND (si.status IS NULL OR si.status != 'reversed')
                  ORDER BY si.date DESC, si.id DESC
                  LIMIT 150",
                new { from, to });

            foreach (var s in sales)
            {
                var sId = AccountService.ToLong(s["id"]);
                var total = Math.Round(AccountService.ToDecimal(s.GetValueOrDefault("total")), 2);
                var tax = Math.Round(AccountService.ToDecimal(s.GetValueOrDefault("tax_amount")), 2);
                var disc = Math.Round(AccountService.ToDecimal(s.GetValueOrDefault("discount_amount")), 2);
                var invType = s.GetValueOrDefault("invoice_type")?.ToString() ?? "debit";
                var rawRec = AccountService.ToDecimal(s.GetValueOrDefault("total_receive"));
                var rawReceived = invType.Equals("debit", StringComparison.OrdinalIgnoreCase) && rawRec <= 0m ? total : Math.Round(rawRec, 2);
                var received = Math.Min(total, rawReceived);
                var excessRecv = Math.Round(Math.Max(0m, rawReceived - total), 2);
                var arPortion = Math.Round(Math.Max(0m, total - received), 2);
                var custName = s.GetValueOrDefault("customer_name")?.ToString() ?? "Walk-in Customer";

                var lines = new List<Dictionary<string, object?>>();
                if (rawReceived > 0m)
                    lines.Add(MakeLine("1010", "Cash & Bank Equivalents", "Payment received on sale", rawReceived, 0m));
                if (arPortion > 0m)
                    lines.Add(MakeLine("1200", "Accounts Receivable", custName, arPortion, 0m));
                if (disc > 0m)
                    lines.Add(MakeLine("4300", "Sales Discounts Allowed", "Invoice discount", disc, 0m));

                lines.Add(MakeLine("4100", "Gross Sales Revenue", $"{s.GetValueOrDefault("sale_type") ?? "retail"} sale", 0m, Math.Round(total + disc - tax, 2)));
                if (tax > 0m)
                    lines.Add(MakeLine("2200", "Output Tax Payable (GST/VAT)", $"Output tax ({s.GetValueOrDefault("tax_percentage")}%)", 0m, tax));
                if (excessRecv > 0m)
                    lines.Add(MakeLine("1200", "Accounts Receivable", $"{custName} (Excess payment credited to ledger)", 0m, excessRecv));

                var sumDr = Math.Round(lines.Sum(l => AccountService.ToDecimal(l["debit"])), 2);
                var dateStr = s.GetValueOrDefault("date")?.ToString() ?? to;
                if (dateStr.Length >= 10) dateStr = dateStr[..10];

                entries.Add(new Dictionary<string, object?>
                {
                    ["id"] = $"SI-{sId}",
                    ["raw_id"] = sId,
                    ["is_manual"] = false,
                    ["entry_number"] = s.GetValueOrDefault("posted_number") ?? $"SI-{sId}",
                    ["entry_date"] = dateStr,
                    ["source_type"] = "sale",
                    ["source_label"] = "SALE INVOICE",
                    ["reference_number"] = s.GetValueOrDefault("posted_number"),
                    ["description"] = $"Sale Invoice to {custName}",
                    ["total_debit"] = sumDr,
                    ["total_credit"] = sumDr,
                    ["status"] = "posted",
                    ["created_by_name"] = "Auto-Posted",
                    ["lines"] = lines
                });
            }
        }

        if ((sourceFilter is "all" or "purchase") && await _db.HasTableAsync("purchase_invoices"))
        {
            var purchases = await _db.QueryListAsync(
                @"SELECT pi.*, s.name as supplier_name
                  FROM purchase_invoices pi
                  LEFT JOIN suppliers s ON s.id = pi.supplier_id
                  WHERE DATE(pi.posted_date) BETWEEN @from AND @to
                    AND (pi.status IS NULL OR pi.status != 'reversed')
                  ORDER BY pi.posted_date DESC, pi.id DESC
                  LIMIT 150",
                new { from, to });

            foreach (var p in purchases)
            {
                var pId = AccountService.ToLong(p["id"]);
                var total = Math.Round(AccountService.ToDecimal(p.GetValueOrDefault("total_amount")), 2);
                var tax = Math.Round(AccountService.ToDecimal(p.GetValueOrDefault("tax_amount")), 2);
                var invValue = Math.Round(Math.Max(0m, total - tax), 2);
                var invType = p.GetValueOrDefault("invoice_type")?.ToString() ?? "debit";
                var rawPd = AccountService.ToDecimal(p.GetValueOrDefault("total_paid"));
                var rawPaid = invType.Equals("debit", StringComparison.OrdinalIgnoreCase) && rawPd <= 0m ? total : Math.Round(rawPd, 2);
                var paid = Math.Min(total, rawPaid);
                var excessPaid = Math.Round(Math.Max(0m, rawPaid - total), 2);
                var apPortion = Math.Round(Math.Max(0m, total - paid), 2);
                var supName = p.GetValueOrDefault("supplier_name")?.ToString() ?? "Supplier";

                var lines = new List<Dictionary<string, object?>>
                {
                    MakeLine("1300", "Merchandise Inventory", "Stock receipt", invValue, 0m)
                };
                if (tax > 0m)
                    lines.Add(MakeLine("1400", "Input Tax Receivable (GST/VAT)", $"Input tax ({p.GetValueOrDefault("tax_percentage")}%)", tax, 0m));
                if (excessPaid > 0m)
                    lines.Add(MakeLine("2100", "Accounts Payable", $"{supName} (Excess payment debited to ledger)", excessPaid, 0m));
                if (rawPaid > 0m)
                    lines.Add(MakeLine("1010", "Cash & Bank Equivalents", "Paid on purchase", 0m, rawPaid));
                if (apPortion > 0m)
                    lines.Add(MakeLine("2100", "Accounts Payable", supName, 0m, apPortion));

                var voucherTotal = Math.Round(lines.Sum(l => AccountService.ToDecimal(l["debit"])), 2);
                var dateStr = p.GetValueOrDefault("posted_date")?.ToString() ?? to;
                if (dateStr.Length >= 10) dateStr = dateStr[..10];

                entries.Add(new Dictionary<string, object?>
                {
                    ["id"] = $"PI-{pId}",
                    ["raw_id"] = pId,
                    ["is_manual"] = false,
                    ["entry_number"] = p.GetValueOrDefault("posted_number") ?? $"PI-{pId}",
                    ["entry_date"] = dateStr,
                    ["source_type"] = "purchase",
                    ["source_label"] = "PURCHASE INVOICE",
                    ["reference_number"] = p.GetValueOrDefault("invoice_number") ?? p.GetValueOrDefault("posted_number"),
                    ["description"] = $"Purchase from {supName}",
                    ["total_debit"] = voucherTotal,
                    ["total_credit"] = voucherTotal,
                    ["status"] = "posted",
                    ["created_by_name"] = "Auto-Posted",
                    ["lines"] = lines
                });
            }
        }

        if ((sourceFilter is "all" or "expense") && await _db.HasTableAsync("expenses"))
        {
            var expenses = await _db.QueryListAsync(
                @"SELECT e.*, ec.name as category_name, ec.code as category_code
                  FROM expenses e
                  LEFT JOIN expense_categories ec ON ec.id = e.expense_category_id
                  WHERE DATE(e.date) BETWEEN @from AND @to
                    AND e.deleted_at IS NULL
                  ORDER BY e.date DESC, e.id DESC
                  LIMIT 100",
                new { from, to });

            foreach (var exp in expenses)
            {
                var expId = AccountService.ToLong(exp["id"]);
                var amt = Math.Round(AccountService.ToDecimal(exp.GetValueOrDefault("amount")), 2);
                var catCode = exp.GetValueOrDefault("category_code")?.ToString() ?? "";
                var catName = exp.GetValueOrDefault("category_name")?.ToString() ?? "Expense";
                var isWriteOff = catCode is "stock_disposal_writeoff" or "stock_shrinkage_writeoff";
                var dateStr = exp.GetValueOrDefault("date")?.ToString() ?? to;
                if (dateStr.Length >= 10) dateStr = dateStr[..10];
                var payee = exp.GetValueOrDefault("payee")?.ToString();

                entries.Add(new Dictionary<string, object?>
                {
                    ["id"] = $"EXP-{expId}",
                    ["raw_id"] = expId,
                    ["is_manual"] = false,
                    ["entry_number"] = exp.GetValueOrDefault("expense_number") ?? $"EXP-{expId}",
                    ["entry_date"] = dateStr,
                    ["source_type"] = "expense",
                    ["source_label"] = isWriteOff ? "INVENTORY WRITE-OFF" : "OPERATING EXPENSE",
                    ["reference_number"] = exp.GetValueOrDefault("reference_number"),
                    ["description"] = catName + (!string.IsNullOrWhiteSpace(payee) ? $" — {payee}" : ""),
                    ["total_debit"] = amt,
                    ["total_credit"] = amt,
                    ["status"] = "posted",
                    ["created_by_name"] = "Auto-Posted",
                    ["lines"] = isWriteOff
                        ? new List<Dictionary<string, object?>>
                        {
                            MakeLine("5200", "Inventory Shrinkage, Expiry & Damage Write-Off", catName, amt, 0m),
                            MakeLine("1300", "Merchandise Inventory", exp.GetValueOrDefault("reference_number")?.ToString() ?? "Stock Write-Off", 0m, amt)
                        }
                        : new List<Dictionary<string, object?>>
                        {
                            MakeLine("5300", "Store Operating Expenses", catName, amt, 0m),
                            MakeLine("1010", "Cash & Bank Equivalents", exp.GetValueOrDefault("payment_method")?.ToString() ?? "cash", 0m, amt)
                        }
                });
            }
        }

        if ((sourceFilter is "all" or "payment") && await _db.HasTableAsync("payments"))
        {
            var payments = await _db.QueryListAsync(
                @"SELECT p.*, c.name as customer_name, s.name as supplier_name
                  FROM payments p
                  LEFT JOIN customers c ON c.id = p.party_id AND p.party_type = 'customer'
                  LEFT JOIN suppliers s ON s.id = p.party_id AND p.party_type = 'supplier'
                  WHERE DATE(p.payment_date) BETWEEN @from AND @to
                    AND (p.status IS NULL OR p.status != 'reversed')
                    AND (p.payment_method IS NULL OR p.payment_method != 'credit_note')
                  ORDER BY p.payment_date DESC, p.id DESC
                  LIMIT 100",
                new { from, to });

            foreach (var pay in payments)
            {
                var payId = AccountService.ToLong(pay["id"]);
                var amt = Math.Round(AccountService.ToDecimal(pay.GetValueOrDefault("amount")), 2);
                var isCust = string.Equals(pay.GetValueOrDefault("party_type")?.ToString(), "customer", StringComparison.OrdinalIgnoreCase);
                var partyName = isCust
                    ? (pay.GetValueOrDefault("customer_name")?.ToString() ?? "Customer")
                    : (pay.GetValueOrDefault("supplier_name")?.ToString() ?? "Supplier");
                var dateStr = pay.GetValueOrDefault("payment_date")?.ToString() ?? to;
                if (dateStr.Length >= 10) dateStr = dateStr[..10];
                var notes = pay.GetValueOrDefault("notes")?.ToString();
                var pm = (pay.GetValueOrDefault("payment_method")?.ToString() ?? "cash").Replace('_', ' ');

                entries.Add(new Dictionary<string, object?>
                {
                    ["id"] = $"PAY-{payId}",
                    ["raw_id"] = payId,
                    ["is_manual"] = false,
                    ["entry_number"] = pay.GetValueOrDefault("payment_number") ?? $"PAY-{payId}",
                    ["entry_date"] = dateStr,
                    ["source_type"] = "payment",
                    ["source_label"] = isCust ? "CUSTOMER RECEIPT" : "SUPPLIER PAYMENT",
                    ["reference_number"] = pay.GetValueOrDefault("reference"),
                    ["description"] = (isCust ? $"Receipt from {partyName}" : $"Payment to {partyName}") + (!string.IsNullOrWhiteSpace(notes) ? $" — {notes}" : ""),
                    ["total_debit"] = amt,
                    ["total_credit"] = amt,
                    ["status"] = "posted",
                    ["created_by_name"] = "Auto-Posted",
                    ["lines"] = isCust
                        ? new List<Dictionary<string, object?>>
                        {
                            MakeLine("1010", "Cash & Bank Equivalents", pm, amt, 0m),
                            MakeLine("1200", "Accounts Receivable", partyName, 0m, amt)
                        }
                        : new List<Dictionary<string, object?>>
                        {
                            MakeLine("2100", "Accounts Payable", partyName, amt, 0m),
                            MakeLine("1010", "Cash & Bank Equivalents", pm, 0m, amt)
                        }
                });
            }
        }

        if (!string.IsNullOrEmpty(search))
        {
            entries = entries.Where(e =>
                (e.GetValueOrDefault("entry_number")?.ToString() ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (e.GetValueOrDefault("description")?.ToString() ?? "").Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (e.GetValueOrDefault("reference_number")?.ToString() ?? "").Contains(search, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }

        entries = entries.OrderByDescending(e => e.GetValueOrDefault("entry_date")?.ToString() ?? "").ToList();

        var postedEntries = entries.Where(e => string.Equals(e.GetValueOrDefault("status")?.ToString(), "posted", StringComparison.OrdinalIgnoreCase)).ToList();

        return new Dictionary<string, object?>
        {
            ["period"] = new Dictionary<string, object?> { ["from"] = from, ["to"] = to },
            ["entries"] = entries,
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_vouchers"] = entries.Count,
                ["manual_count"] = entries.Count(e => AccountService.IsTruthy(e.GetValueOrDefault("is_manual"))),
                ["total_debit"] = Math.Round(postedEntries.Sum(e => AccountService.ToDecimal(e.GetValueOrDefault("total_debit"))), 2),
                ["total_credit"] = Math.Round(postedEntries.Sum(e => AccountService.ToDecimal(e.GetValueOrDefault("total_credit"))), 2)
            }
        };
    }

    public async Task<Dictionary<string, object?>> GetTaxReportAsync(string fromDate, string toDate, IDictionary<string, string?>? filters = null)
    {
        await EnsureDefaultsAsync();
        var from = DateTime.TryParse(fromDate, out var fDt) ? fDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-01");
        var to = DateTime.TryParse(toDate, out var tDt) ? tDt.ToString("yyyy-MM-dd") : DateTime.UtcNow.ToString("yyyy-MM-dd");
        var taxScope = filters != null && filters.TryGetValue("tax_scope", out var ts) && !string.IsNullOrWhiteSpace(ts) ? ts : "all";

        var hasCustomerTaxCols = await _db.HasColumnAsync("customers", "tax_number");
        var hasSupplierTaxCols = await _db.HasColumnAsync("suppliers", "tax_number");

        var salesQuery = await _db.HasTableAsync("sale_invoices")
            ? await _db.QueryListAsync(
                $@"SELECT si.id, si.posted_number, si.date, si.sale_type, si.invoice_type, si.gross_amount,
                          si.discount_amount, si.tax_percentage, si.tax_amount, si.total, si.customer_id,
                          COALESCE(c.name, 'Walk-in Customer') as party_name,
                          {(hasCustomerTaxCols ? "COALESCE(c.tax_number, '')" : "''")} as party_tax_number,
                          {(hasCustomerTaxCols ? "COALESCE(c.tax_status, 'unregistered')" : "'unregistered'")} as party_tax_status
                   FROM sale_invoices si
                   LEFT JOIN customers c ON c.id = si.customer_id
                   WHERE DATE(si.date) BETWEEN @from AND @to
                     AND (si.status IS NULL OR si.status != 'reversed')
                   ORDER BY si.date ASC, si.id ASC",
                new { from, to })
            : new List<Dictionary<string, object?>>();

        var saleReturnsQuery = await _db.HasTableAsync("sale_returns")
            ? await _db.QueryListAsync(
                $@"SELECT sr.id, sr.posted_number, sr.date, sr.gross_total, sr.discount_amount,
                          sr.tax_percentage, sr.tax_amount, sr.total, sr.customer_id,
                          COALESCE(c.name, 'Walk-in Customer') as party_name,
                          {(hasCustomerTaxCols ? "COALESCE(c.tax_number, '')" : "''")} as party_tax_number,
                          {(hasCustomerTaxCols ? "COALESCE(c.tax_status, 'unregistered')" : "'unregistered'")} as party_tax_status
                   FROM sale_returns sr
                   LEFT JOIN customers c ON c.id = sr.customer_id
                   WHERE DATE(sr.date) BETWEEN @from AND @to
                   ORDER BY sr.date ASC, sr.id ASC",
                new { from, to })
            : new List<Dictionary<string, object?>>();

        var outputRegister = new List<Dictionary<string, object?>>();
        decimal taxableSalesBase = 0m;
        decimal exemptSalesBase = 0m;
        decimal outputTaxCollected = 0m;
        int taxableSalesCount = 0;
        int exemptSalesCount = 0;

        foreach (var s in salesQuery)
        {
            var taxAmt = Math.Round(AccountService.ToDecimal(s.GetValueOrDefault("tax_amount")), 2);
            var taxPct = Math.Round(AccountService.ToDecimal(s.GetValueOrDefault("tax_percentage")), 2);
            var total = Math.Round(AccountService.ToDecimal(s.GetValueOrDefault("total")), 2);
            var taxableValue = Math.Round(Math.Max(0m, total - taxAmt), 2);

            if (taxAmt > 0m || taxPct > 0m)
            {
                taxableSalesBase = Math.Round(taxableSalesBase + taxableValue, 2);
                outputTaxCollected = Math.Round(outputTaxCollected + taxAmt, 2);
                taxableSalesCount++;
            }
            else
            {
                exemptSalesBase = Math.Round(exemptSalesBase + taxableValue, 2);
                exemptSalesCount++;
            }

            if (taxScope == "taxable_only" && taxAmt <= 0m && taxPct <= 0m) continue;

            var dStr = s.GetValueOrDefault("date")?.ToString() ?? to;
            if (dStr.Length >= 10) dStr = dStr[..10];
            var pTaxNo = s.GetValueOrDefault("party_tax_number")?.ToString();

            outputRegister.Add(new Dictionary<string, object?>
            {
                ["id"] = $"SI-{s["id"]}",
                ["doc_type"] = "sale_invoice",
                ["doc_type_label"] = "Sale Invoice",
                ["posted_number"] = s.GetValueOrDefault("posted_number"),
                ["date"] = dStr,
                ["sale_type"] = s.GetValueOrDefault("sale_type") ?? "retail",
                ["party_id"] = s.GetValueOrDefault("customer_id"),
                ["party_name"] = s.GetValueOrDefault("party_name"),
                ["party_tax_number"] = string.IsNullOrWhiteSpace(pTaxNo) ? "—" : pTaxNo,
                ["party_tax_status"] = s.GetValueOrDefault("party_tax_status") ?? "unregistered",
                ["taxable_amount"] = taxableValue,
                ["tax_percentage"] = taxPct,
                ["tax_amount"] = taxAmt,
                ["invoice_total"] = total
            });
        }

        decimal saleReturnsBase = 0m;
        decimal saleReturnsTaxReversed = 0m;

        foreach (var sr in saleReturnsQuery)
        {
            var taxAmt = Math.Round(AccountService.ToDecimal(sr.GetValueOrDefault("tax_amount")), 2);
            var taxPct = Math.Round(AccountService.ToDecimal(sr.GetValueOrDefault("tax_percentage")), 2);
            var total = Math.Round(AccountService.ToDecimal(sr.GetValueOrDefault("total")), 2);
            var baseVal = Math.Round(Math.Max(0m, total - taxAmt), 2);

            saleReturnsBase = Math.Round(saleReturnsBase + baseVal, 2);
            saleReturnsTaxReversed = Math.Round(saleReturnsTaxReversed + taxAmt, 2);

            if (taxScope == "taxable_only" && taxAmt <= 0m && taxPct <= 0m) continue;

            var dStr = sr.GetValueOrDefault("date")?.ToString() ?? to;
            if (dStr.Length >= 10) dStr = dStr[..10];
            var pTaxNo = sr.GetValueOrDefault("party_tax_number")?.ToString();

            outputRegister.Add(new Dictionary<string, object?>
            {
                ["id"] = $"SR-{sr["id"]}",
                ["doc_type"] = "sale_return",
                ["doc_type_label"] = "Sale Return (Credit Note)",
                ["posted_number"] = sr.GetValueOrDefault("posted_number"),
                ["date"] = dStr,
                ["sale_type"] = "return",
                ["party_id"] = sr.GetValueOrDefault("customer_id"),
                ["party_name"] = sr.GetValueOrDefault("party_name"),
                ["party_tax_number"] = string.IsNullOrWhiteSpace(pTaxNo) ? "—" : pTaxNo,
                ["party_tax_status"] = sr.GetValueOrDefault("party_tax_status") ?? "unregistered",
                ["taxable_amount"] = -baseVal,
                ["tax_percentage"] = taxPct,
                ["tax_amount"] = -taxAmt,
                ["invoice_total"] = -total
            });
        }

        var purchasesQuery = await _db.HasTableAsync("purchase_invoices")
            ? await _db.QueryListAsync(
                $@"SELECT pi.id, pi.posted_number, pi.invoice_number, pi.posted_date, pi.invoice_amount,
                          pi.discount_amount, pi.tax_percentage, pi.tax_amount, pi.total_amount, pi.supplier_id,
                          COALESCE(s.name, 'Supplier') as party_name,
                          {(hasSupplierTaxCols ? "COALESCE(s.tax_number, '')" : "''")} as party_tax_number,
                          {(hasSupplierTaxCols ? "COALESCE(s.tax_status, 'registered')" : "'registered'")} as party_tax_status
                   FROM purchase_invoices pi
                   LEFT JOIN suppliers s ON s.id = pi.supplier_id
                   WHERE DATE(pi.posted_date) BETWEEN @from AND @to
                     AND (pi.status IS NULL OR pi.status != 'reversed')
                   ORDER BY pi.posted_date ASC, pi.id ASC",
                new { from, to })
            : new List<Dictionary<string, object?>>();

        var purchaseReturnsQuery = await _db.HasTableAsync("purchase_returns")
            ? await _db.QueryListAsync(
                $@"SELECT pr.id, pr.posted_number, pr.date, pr.gross_total, pr.discount_amount,
                          pr.tax_percentage, pr.tax_amount, pr.total, pr.supplier_id,
                          COALESCE(s.name, 'Supplier') as party_name,
                          {(hasSupplierTaxCols ? "COALESCE(s.tax_number, '')" : "''")} as party_tax_number,
                          {(hasSupplierTaxCols ? "COALESCE(s.tax_status, 'registered')" : "'registered'")} as party_tax_status
                   FROM purchase_returns pr
                   LEFT JOIN suppliers s ON s.id = pr.supplier_id
                   WHERE DATE(pr.date) BETWEEN @from AND @to
                   ORDER BY pr.date ASC, pr.id ASC",
                new { from, to })
            : new List<Dictionary<string, object?>>();

        var inputRegister = new List<Dictionary<string, object?>>();
        decimal taxablePurchasesBase = 0m;
        decimal exemptPurchasesBase = 0m;
        decimal inputTaxPaid = 0m;
        int taxablePurchasesCount = 0;
        int exemptPurchasesCount = 0;

        foreach (var p in purchasesQuery)
        {
            var taxAmt = Math.Round(AccountService.ToDecimal(p.GetValueOrDefault("tax_amount")), 2);
            var taxPct = Math.Round(AccountService.ToDecimal(p.GetValueOrDefault("tax_percentage")), 2);
            var total = Math.Round(AccountService.ToDecimal(p.GetValueOrDefault("total_amount")), 2);
            var taxableValue = Math.Round(Math.Max(0m, total - taxAmt), 2);

            if (taxAmt > 0m || taxPct > 0m)
            {
                taxablePurchasesBase = Math.Round(taxablePurchasesBase + taxableValue, 2);
                inputTaxPaid = Math.Round(inputTaxPaid + taxAmt, 2);
                taxablePurchasesCount++;
            }
            else
            {
                exemptPurchasesBase = Math.Round(exemptPurchasesBase + taxableValue, 2);
                exemptPurchasesCount++;
            }

            if (taxScope == "taxable_only" && taxAmt <= 0m && taxPct <= 0m) continue;

            var dStr = p.GetValueOrDefault("posted_date")?.ToString() ?? to;
            if (dStr.Length >= 10) dStr = dStr[..10];
            var pTaxNo = p.GetValueOrDefault("party_tax_number")?.ToString();

            inputRegister.Add(new Dictionary<string, object?>
            {
                ["id"] = $"PI-{p["id"]}",
                ["doc_type"] = "purchase_invoice",
                ["doc_type_label"] = "Purchase Invoice",
                ["posted_number"] = p.GetValueOrDefault("posted_number"),
                ["supplier_inv_no"] = p.GetValueOrDefault("invoice_number") ?? "—",
                ["date"] = dStr,
                ["party_id"] = p.GetValueOrDefault("supplier_id"),
                ["party_name"] = p.GetValueOrDefault("party_name"),
                ["party_tax_number"] = string.IsNullOrWhiteSpace(pTaxNo) ? "—" : pTaxNo,
                ["party_tax_status"] = p.GetValueOrDefault("party_tax_status") ?? "registered",
                ["taxable_amount"] = taxableValue,
                ["tax_percentage"] = taxPct,
                ["tax_amount"] = taxAmt,
                ["invoice_total"] = total
            });
        }

        decimal purchaseReturnsBase = 0m;
        decimal purchaseReturnsTaxReversed = 0m;

        foreach (var pr in purchaseReturnsQuery)
        {
            var taxAmt = Math.Round(AccountService.ToDecimal(pr.GetValueOrDefault("tax_amount")), 2);
            var taxPct = Math.Round(AccountService.ToDecimal(pr.GetValueOrDefault("tax_percentage")), 2);
            var total = Math.Round(AccountService.ToDecimal(pr.GetValueOrDefault("total")), 2);
            var baseVal = Math.Round(Math.Max(0m, total - taxAmt), 2);

            purchaseReturnsBase = Math.Round(purchaseReturnsBase + baseVal, 2);
            purchaseReturnsTaxReversed = Math.Round(purchaseReturnsTaxReversed + taxAmt, 2);

            if (taxScope == "taxable_only" && taxAmt <= 0m && taxPct <= 0m) continue;

            var dStr = pr.GetValueOrDefault("date")?.ToString() ?? to;
            if (dStr.Length >= 10) dStr = dStr[..10];
            var pTaxNo = pr.GetValueOrDefault("party_tax_number")?.ToString();

            inputRegister.Add(new Dictionary<string, object?>
            {
                ["id"] = $"PR-{pr["id"]}",
                ["doc_type"] = "purchase_return",
                ["doc_type_label"] = "Purchase Return (Debit Note)",
                ["posted_number"] = pr.GetValueOrDefault("posted_number"),
                ["supplier_inv_no"] = "—",
                ["date"] = dStr,
                ["party_id"] = pr.GetValueOrDefault("supplier_id"),
                ["party_name"] = pr.GetValueOrDefault("party_name"),
                ["party_tax_number"] = string.IsNullOrWhiteSpace(pTaxNo) ? "—" : pTaxNo,
                ["party_tax_status"] = pr.GetValueOrDefault("party_tax_status") ?? "registered",
                ["taxable_amount"] = -baseVal,
                ["tax_percentage"] = taxPct,
                ["tax_amount"] = -taxAmt,
                ["invoice_total"] = -total
            });
        }

        var customerAnnexureMap = BuildAnnexure(outputRegister);
        var supplierAnnexureMap = BuildAnnexure(inputRegister);

        var rateTiers = new SortedDictionary<decimal, Dictionary<string, object?>>();
        foreach (var row in outputRegister)
        {
            var rate = Math.Round(AccountService.ToDecimal(row["tax_percentage"]), 2);
            if (!rateTiers.TryGetValue(rate, out var tier))
            {
                tier = NewRateTier(rate);
                rateTiers[rate] = tier;
            }
            tier["sales_docs"] = (int)tier["sales_docs"]! + 1;
            tier["sales_base"] = Math.Round((decimal)tier["sales_base"]! + AccountService.ToDecimal(row["taxable_amount"]), 2);
            tier["output_tax"] = Math.Round((decimal)tier["output_tax"]! + AccountService.ToDecimal(row["tax_amount"]), 2);
        }
        foreach (var row in inputRegister)
        {
            var rate = Math.Round(AccountService.ToDecimal(row["tax_percentage"]), 2);
            if (!rateTiers.TryGetValue(rate, out var tier))
            {
                tier = NewRateTier(rate);
                rateTiers[rate] = tier;
            }
            tier["purchase_docs"] = (int)tier["purchase_docs"]! + 1;
            tier["purchase_base"] = Math.Round((decimal)tier["purchase_base"]! + AccountService.ToDecimal(row["taxable_amount"]), 2);
            tier["input_tax"] = Math.Round((decimal)tier["input_tax"]! + AccountService.ToDecimal(row["tax_amount"]), 2);
        }

        decimal taxPaymentsMade = 0m;
        if (await _db.HasTableAsync("journal_entry_lines") && await _db.HasTableAsync("journal_entries"))
        {
            taxPaymentsMade = Math.Round(await _db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(jel.debit - jel.credit), 0)
                  FROM journal_entry_lines jel
                  INNER JOIN journal_entries je ON je.id = jel.journal_entry_id
                  INNER JOIN chart_of_accounts coa ON coa.id = jel.chart_of_account_id
                  WHERE je.status = 'posted'
                    AND DATE(je.entry_date) BETWEEN @from AND @to
                    AND coa.system_key IN ('output_tax_payable', 'withholding_tax_payable')",
                new { from, to }) ?? 0m, 2);
        }

        var netOutputTax = Math.Round(outputTaxCollected - saleReturnsTaxReversed, 2);
        var netInputTax = Math.Round(inputTaxPaid - purchaseReturnsTaxReversed, 2);
        var netTaxBeforeChallan = Math.Round(netOutputTax - netInputTax, 2);
        var netTaxPayable = Math.Round(netTaxBeforeChallan - taxPaymentsMade, 2);

        return new Dictionary<string, object?>
        {
            ["period"] = new Dictionary<string, object?> { ["from"] = from, ["to"] = to },
            ["summary"] = new Dictionary<string, object?>
            {
                ["taxable_sales_count"] = taxableSalesCount,
                ["exempt_sales_count"] = exemptSalesCount,
                ["taxable_sales_base"] = taxableSalesBase,
                ["exempt_sales_base"] = exemptSalesBase,
                ["output_tax_collected"] = outputTaxCollected,
                ["sale_returns_base"] = saleReturnsBase,
                ["sale_returns_tax_reversed"] = saleReturnsTaxReversed,
                ["net_output_tax"] = netOutputTax,
                ["taxable_purchases_count"] = taxablePurchasesCount,
                ["exempt_purchases_count"] = exemptPurchasesCount,
                ["taxable_purchases_base"] = taxablePurchasesBase,
                ["exempt_purchases_base"] = exemptPurchasesBase,
                ["input_tax_paid"] = inputTaxPaid,
                ["purchase_returns_base"] = purchaseReturnsBase,
                ["purchase_returns_tax_reversed"] = purchaseReturnsTaxReversed,
                ["net_input_tax"] = netInputTax,
                ["tax_challans_paid"] = taxPaymentsMade,
                ["net_tax_before_challan"] = netTaxBeforeChallan,
                ["net_tax_payable"] = netTaxPayable,
                ["status"] = netTaxPayable > 0m ? "payable" : (netTaxPayable < 0m ? "refundable_credit" : "settled")
            },
            ["output_register"] = outputRegister,
            ["input_register"] = inputRegister,
            ["customer_annexure"] = customerAnnexureMap,
            ["supplier_annexure"] = supplierAnnexureMap,
            ["rate_breakdown"] = rateTiers.Values.ToList(),
            ["tax_rates"] = await GetTaxRatesAsync(true)
        };
    }

    private static List<Dictionary<string, object?>> BuildAnnexure(List<Dictionary<string, object?>> register)
    {
        var map = new Dictionary<string, Dictionary<string, object?>>();
        foreach (var row in register)
        {
            var pid = AccountService.ToLong(row.GetValueOrDefault("party_id"));
            var pname = row.GetValueOrDefault("party_name")?.ToString() ?? "";
            var key = $"{pid}:{pname}";
            if (!map.TryGetValue(key, out var item))
            {
                item = new Dictionary<string, object?>
                {
                    ["party_id"] = row.GetValueOrDefault("party_id"),
                    ["party_name"] = pname,
                    ["party_tax_number"] = row.GetValueOrDefault("party_tax_number"),
                    ["party_tax_status"] = row.GetValueOrDefault("party_tax_status"),
                    ["doc_count"] = 0,
                    ["taxable_amount"] = 0m,
                    ["tax_amount"] = 0m,
                    ["invoice_total"] = 0m
                };
                map[key] = item;
            }
            item["doc_count"] = (int)item["doc_count"]! + 1;
            item["taxable_amount"] = Math.Round((decimal)item["taxable_amount"]! + AccountService.ToDecimal(row["taxable_amount"]), 2);
            item["tax_amount"] = Math.Round((decimal)item["tax_amount"]! + AccountService.ToDecimal(row["tax_amount"]), 2);
            item["invoice_total"] = Math.Round((decimal)item["invoice_total"]! + AccountService.ToDecimal(row["invoice_total"]), 2);
        }
        return map.Values.ToList();
    }

    private static Dictionary<string, object?> NewRateTier(decimal rate) => new()
    {
        ["rate"] = rate,
        ["sales_docs"] = 0,
        ["sales_base"] = 0m,
        ["output_tax"] = 0m,
        ["purchase_docs"] = 0,
        ["purchase_base"] = 0m,
        ["input_tax"] = 0m
    };

    private static Dictionary<string, object?> MakeLine(string code, string name, string desc, decimal debit, decimal credit) => new()
    {
        ["account_code"] = code,
        ["account_name"] = name,
        ["description"] = desc,
        ["debit"] = debit,
        ["credit"] = credit
    };

    private async Task<Dictionary<string, object?>> EnrichJournalEntryAsync(Dictionary<string, object?> entry)
    {
        var entryId = AccountService.ToLong(entry["id"]);
        var lines = await _db.QueryListAsync("SELECT * FROM journal_entry_lines WHERE journal_entry_id = @entryId ORDER BY id ASC", new { entryId });
        foreach (var l in lines)
        {
            var coaId = AccountService.ToLong(l.GetValueOrDefault("chart_of_account_id"));
            l["account"] = coaId > 0
                ? await _db.QuerySingleOrDefaultAsync("SELECT * FROM chart_of_accounts WHERE id = @coaId", new { coaId })
                : null;
        }
        entry["lines"] = lines;

        var createdBy = AccountService.ToLong(entry.GetValueOrDefault("created_by"));
        entry["creator"] = createdBy > 0 && await _db.HasTableAsync("users")
            ? await _db.QuerySingleOrDefaultAsync("SELECT id, name FROM users WHERE id = @createdBy", new { createdBy })
            : null;

        var reversedBy = AccountService.ToLong(entry.GetValueOrDefault("reversed_by"));
        entry["reversed_by_user"] = reversedBy > 0 && await _db.HasTableAsync("users")
            ? await _db.QuerySingleOrDefaultAsync("SELECT id, name FROM users WHERE id = @reversedBy", new { reversedBy })
            : null;

        var accId = AccountService.ToLong(entry.GetValueOrDefault("account_id"));
        entry["treasury_account"] = accId > 0 && await _db.HasTableAsync("accounts")
            ? await _db.QuerySingleOrDefaultAsync("SELECT id, name, type FROM accounts WHERE id = @accId", new { accId })
            : null;

        return entry;
    }

    private static List<Dictionary<string, object?>> ExtractLines(object? raw)
    {
        var list = new List<Dictionary<string, object?>>();
        if (raw == null) return list;

        if (raw is IEnumerable<Dictionary<string, object?>> typed)
        {
            return typed.ToList();
        }
        if (raw is System.Text.Json.JsonElement je && je.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in je.EnumerateArray())
            {
                if (item.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in item.EnumerateObject())
                    {
                        d[p.Name] = KarobarDb.PrepareDbValue(p.Value);
                    }
                    list.Add(d);
                }
            }
        }
        else if (raw is System.Collections.IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (item is Dictionary<string, object?> dict)
                {
                    list.Add(dict);
                }
            }
        }
        return list;
    }
}

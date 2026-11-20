using System.Net;
using System.Text;
using System.Text.Json;
using Karobar.Api.Helpers;
using Karobar.Api.Infrastructure;
using Karobar.Api.Middleware;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Authorize]
public class CashDrawerController : ApiControllerBase
{
    private readonly CashDrawerService _cashDrawerService;
    private readonly AccountService _accountService;
    private readonly PrintTemplateRenderer _renderer;

    public CashDrawerController(KarobarDb db, PrintTemplateRenderer? renderer = null) : base(db)
    {
        _cashDrawerService = new CashDrawerService(db);
        _accountService = new AccountService(db);
        _renderer = renderer ?? new PrintTemplateRenderer();
    }

    [HttpGet("api/cash-drawer/current")]
    public async Task<IActionResult> Current()
    {
        if (!await CanAsync("cash-drawer.view", "cash-drawer.open", "cash-drawer.close", "cash-drawer.transaction", "sale-invoice.create", "barcode-pos.view", "barcode-pos.create"))
            return Forbidden();

        var session = await _cashDrawerService.GetCurrentSessionAsync(CurrentUserId, true, CurrentTerminalId);
        var summaryOnly = string.Equals(Request.Query["summary_only"], "1", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(Request.Query["summary_only"], "true", StringComparison.OrdinalIgnoreCase);

        if (session == null)
        {
            var cashAcc = await _accountService.SyncCashDrawerBalanceAsync();
            var otherOpenExpected = await Db.HasTableAsync("cash_drawer_sessions")
                ? await Db.ExecuteScalarAsync<decimal?>("SELECT COALESCE(SUM(closing_balance_expected), 0) FROM cash_drawer_sessions WHERE status = 'open'") ?? 0m
                : 0m;

            decimal? availableCashInHand = cashAcc != null
                ? Math.Max(0m, Math.Round(AccountService.ToDecimal(cashAcc.GetValueOrDefault("current_balance")) - otherOpenExpected, 2))
                : null;

            if (summaryOnly)
            {
                return Ok(new Dictionary<string, object?>
                {
                    ["is_open"] = false,
                    ["session"] = null,
                    ["summary"] = null,
                    ["last_closed_session"] = null,
                    ["last_closed_summary"] = null,
                    ["cash_in_hand_balance"] = availableCashInHand
                });
            }

            var lastClosed = await _cashDrawerService.GetLastClosedSessionAsync(CurrentUserId, CurrentTerminalId);
            return Ok(new Dictionary<string, object?>
            {
                ["is_open"] = false,
                ["session"] = null,
                ["summary"] = null,
                ["last_closed_session"] = lastClosed,
                ["last_closed_summary"] = lastClosed != null ? await _cashDrawerService.GetSessionSummaryAsync(lastClosed) : null,
                ["cash_in_hand_balance"] = availableCashInHand
            });
        }

        var reconciledAcc = await _accountService.ReconcileWithOpenCashDrawersAsync(CurrentUserId);

        if (summaryOnly)
        {
            return Ok(new Dictionary<string, object?>
            {
                ["is_open"] = true,
                ["session"] = null,
                ["summary"] = await _cashDrawerService.GetSessionSummaryAsync(session),
                ["cash_in_hand_balance"] = reconciledAcc != null ? AccountService.ToDecimal(reconciledAcc.GetValueOrDefault("current_balance")) : (decimal?)null
            });
        }

        var sessionId = AccountService.ToLong(session["id"]);
        var txns = await Db.QueryListAsync(
            "SELECT * FROM cash_drawer_transactions WHERE session_id = @sessionId ORDER BY created_at ASC, id ASC",
            new { sessionId });

        foreach (var t in txns)
        {
            var uid = AccountService.ToLong(t.GetValueOrDefault("user_id"));
            t["user"] = uid > 0 && await Db.HasTableAsync("users")
                ? await Db.QuerySingleOrDefaultAsync("SELECT id, name FROM users WHERE id = @uid", new { uid })
                : null;
        }

        await _cashDrawerService.EnrichTransactionsAsync(txns);
        session["transactions"] = txns;

        return Ok(new Dictionary<string, object?>
        {
            ["is_open"] = true,
            ["session"] = session,
            ["summary"] = await _cashDrawerService.GetSessionSummaryAsync(session, txns),
            ["cash_in_hand_balance"] = reconciledAcc != null ? AccountService.ToDecimal(reconciledAcc.GetValueOrDefault("current_balance")) : (decimal?)null
        });
    }

    [HttpPost("api/cash-drawer/open")]
    public async Task<IActionResult> Open()
    {
        if (!await CanAsync("cash-drawer.open")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        if (!body.ContainsKey("opening_balance"))
            return Unprocessable("The opening balance field is required.", new() { ["opening_balance"] = new[] { "The opening balance field is required." } });

        var openingBalance = GetDecimal(body, "opening_balance", -1m);
        if (openingBalance < 0m)
            return Unprocessable("Opening balance must be at least 0.", new() { ["opening_balance"] = new[] { "Opening balance must be at least 0." } });

        var notes = GetString(body, "notes");

        try
        {
            var session = await _cashDrawerService.OpenSessionAsync(CurrentUserId ?? 1, openingBalance, notes, CurrentTerminalId);
            return StatusCode(201, new Dictionary<string, object?>
            {
                ["message"] = "Cash drawer opened successfully.",
                ["session"] = session,
                ["summary"] = await _cashDrawerService.GetSessionSummaryAsync(session)
            });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message, new() { ["session"] = new[] { ex.Message } });
        }
    }

    [HttpPost("api/cash-drawer/transaction")]
    public async Task<IActionResult> RecordTransaction()
    {
        if (!await CanAsync("cash-drawer.transaction")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var type = GetString(body, "type")?.Trim()?.ToLowerInvariant();
        var amount = GetDecimal(body, "amount", 0m);
        var notes = GetString(body, "notes")?.Trim();

        if (type is not ("cash_in" or "cash_out"))
            return Unprocessable("Invalid transaction type.", new() { ["type"] = new[] { "The selected type is invalid." } });
        if (amount < 0.01m)
            return Unprocessable("Amount must be at least 0.01.", new() { ["amount"] = new[] { "Amount must be at least 0.01." } });
        if (string.IsNullOrWhiteSpace(notes))
            return Unprocessable("The notes field is required.", new() { ["notes"] = new[] { "The notes field is required." } });

        var session = await _cashDrawerService.GetCurrentSessionAsync(CurrentUserId, true, CurrentTerminalId);
        if (session == null)
        {
            return Unprocessable("No active cash drawer is open. Please open the drawer first.");
        }

        var txn = await _cashDrawerService.RecordTransactionAsync(
            session,
            type,
            amount,
            "manual",
            null,
            notes,
            CurrentUserId,
            CurrentTerminalId);

        if (txn != null)
        {
            await _cashDrawerService.EnrichTransactionsAsync(new List<Dictionary<string, object?>> { txn });
        }

        var refreshed = await _cashDrawerService.GetCurrentSessionAsync(CurrentUserId, true, CurrentTerminalId) ?? session;

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = (type == "cash_in" ? "Cash in" : "Cash out") + " recorded successfully.",
            ["transaction"] = txn,
            ["summary"] = await _cashDrawerService.GetSessionSummaryAsync(refreshed)
        });
    }

    [HttpPost("api/cash-drawer/close")]
    public async Task<IActionResult> Close()
    {
        if (!await CanAsync("cash-drawer.close")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        if (!body.ContainsKey("closing_balance_actual"))
            return Unprocessable("The closing balance actual field is required.", new() { ["closing_balance_actual"] = new[] { "The closing balance actual field is required." } });

        var actualBalance = GetDecimal(body, "closing_balance_actual", -1m);
        if (actualBalance < 0m)
            return Unprocessable("Closing balance must be at least 0.", new() { ["closing_balance_actual"] = new[] { "Closing balance must be at least 0." } });

        var reqSessionId = GetNullableLong(body, "session_id");
        Dictionary<string, object?>? session;

        if (reqSessionId.HasValue && reqSessionId.Value > 0)
        {
            session = await Db.QuerySingleOrDefaultAsync(
                "SELECT * FROM cash_drawer_sessions WHERE id = @id AND status = 'open'",
                new { id = reqSessionId.Value });
            if (session != null &&
                AccountService.ToLong(session.GetValueOrDefault("user_id")) != (CurrentUserId ?? 0) &&
                !await IsAdminAsync())
            {
                return Forbidden("You are not authorized to close another user's cash drawer session.");
            }
        }
        else
        {
            session = await _cashDrawerService.GetCurrentSessionAsync(CurrentUserId, true, CurrentTerminalId);
        }

        if (session == null)
        {
            return Unprocessable("No active cash drawer session is open to close.");
        }

        Dictionary<string, decimal>? denoms = null;
        if (body.TryGetValue("closing_denominations", out var denomEl) && denomEl.ValueKind == JsonValueKind.Object)
        {
            denoms = new Dictionary<string, decimal>();
            foreach (var p in denomEl.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Number && p.Value.TryGetDecimal(out var dv))
                    denoms[p.Name] = dv;
                else if (p.Value.ValueKind == JsonValueKind.String && decimal.TryParse(p.Value.GetString(), out var sdv))
                    denoms[p.Name] = sdv;
            }
        }

        try
        {
            var closed = await _cashDrawerService.CloseSessionAsync(
                AccountService.ToLong(session["id"]),
                actualBalance,
                GetString(body, "notes"),
                denoms,
                CurrentUserId);

            return Ok(new Dictionary<string, object?>
            {
                ["message"] = "Cash drawer closed successfully.",
                ["session"] = closed,
                ["summary"] = await _cashDrawerService.GetSessionSummaryAsync(closed)
            });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message, new() { ["session"] = new[] { ex.Message } });
        }
    }

    [HttpGet("api/cash-drawer/history")]
    public async Task<IActionResult> History()
    {
        if (!await CanAsync("cash-drawer.view")) return Forbidden();

        var page = int.TryParse(Request.Query["page"], out var p) ? Math.Max(1, p) : 1;
        var perPage = int.TryParse(Request.Query["per_page"], out var pp) ? Math.Clamp(pp, 1, 100) : 15;

        const string selectSql = @"
            SELECT cds.*,
                   (SELECT COUNT(1) FROM cash_drawer_transactions cdt WHERE cdt.session_id = cds.id) as transactions_count,
                   (SELECT COALESCE(SUM(cdt.amount), 0) FROM cash_drawer_transactions cdt WHERE cdt.session_id = cds.id AND cdt.type IN ('cash_sale', 'customer_payment', 'purchase_return', 'cash_in')) as total_cash_in,
                   (SELECT COALESCE(SUM(cdt.amount), 0) FROM cash_drawer_transactions cdt WHERE cdt.session_id = cds.id AND cdt.type IN ('cash_purchase', 'supplier_payment', 'cash_return', 'cash_refund', 'expense', 'cash_out')) as total_cash_out
            FROM cash_drawer_sessions cds
            ORDER BY cds.id DESC";

        const string countSql = "SELECT COUNT(1) FROM cash_drawer_sessions";

        var paged = await Db.PaginateAsync(selectSql, countSql, null, page, perPage, async batch =>
        {
            foreach (var row in batch)
            {
                var uid = AccountService.ToLong(row.GetValueOrDefault("user_id"));
                row["user"] = uid > 0 && await Db.HasTableAsync("users")
                    ? await Db.QuerySingleOrDefaultAsync("SELECT id, name, email FROM users WHERE id = @uid", new { uid })
                    : null;
            }
            return batch;
        });

        return Ok(paged);
    }

    [HttpGet("api/cash-drawer/reconciliation-check")]
    public async Task<IActionResult> ReconciliationCheck()
    {
        if (!await CanAsync("cash-drawer.view", "account.view")) return Forbidden();

        var cashAccount = await _accountService.SyncCashDrawerBalanceAsync();
        var cashAccountId = cashAccount != null ? AccountService.ToLong(cashAccount["id"]) : 0L;
        var cashAccountBalance = cashAccount != null
            ? Math.Round(AccountService.ToDecimal(cashAccount.GetValueOrDefault("current_balance")), 2)
            : 0m;

        decimal accountLedgerSum = 0m;
        if (cashAccountId > 0 && await Db.HasTableAsync("account_transactions"))
        {
            accountLedgerSum = Math.Round(await Db.ExecuteScalarAsync<decimal?>(
                @"SELECT COALESCE(SUM(
                      CASE
                          WHEN type IN ('deposit', 'transfer_in', 'opening_balance') THEN amount
                          WHEN type = 'adjustment' THEN amount
                          ELSE -amount
                      END
                  ), 0)
                  FROM account_transactions
                  WHERE account_id = @cashAccountId",
                new { cashAccountId }) ?? 0m, 2);
        }

        var openSessions = await Db.HasTableAsync("cash_drawer_sessions")
            ? await Db.QueryListAsync("SELECT * FROM cash_drawer_sessions WHERE status = 'open' ORDER BY id ASC")
            : new List<Dictionary<string, object?>>();

        decimal openStoredTotal = 0m;
        decimal openCalcTotal = 0m;
        var sessionDiagnostics = new List<Dictionary<string, object?>>();
        var staleSessionsCount = 0;

        foreach (var sess in openSessions)
        {
            var sid = AccountService.ToLong(sess["id"]);
            var stored = Math.Round(AccountService.ToDecimal(sess.GetValueOrDefault("closing_balance_expected")), 2);
            var calc = await _cashDrawerService.RecalculateExpectedBalanceAsync(sid, syncAccount: false);
            var staleDelta = Math.Round(stored - calc, 2);
            if (Math.Abs(staleDelta) > 0.001m) staleSessionsCount++;

            openStoredTotal = Math.Round(openStoredTotal + stored, 2);
            openCalcTotal = Math.Round(openCalcTotal + calc, 2);

            sessionDiagnostics.Add(new Dictionary<string, object?>
            {
                ["session_id"] = sid,
                ["terminal_id"] = sess.GetValueOrDefault("terminal_id"),
                ["user_id"] = sess.GetValueOrDefault("user_id"),
                ["opened_at"] = sess.GetValueOrDefault("opened_at"),
                ["opening_balance"] = Math.Round(AccountService.ToDecimal(sess.GetValueOrDefault("opening_balance")), 2),
                ["stored_expected_before_refresh"] = stored,
                ["calculated_expected"] = calc,
                ["stale_delta"] = staleDelta
            });
        }

        // Forensic comparison of active open-shift window: detect any account_transactions missing from drawer (or vice-versa)
        var unmatchedAccountTxns = new List<Dictionary<string, object?>>();
        if (cashAccountId > 0 && openSessions.Count > 0 &&
            await Db.HasTableAsync("account_transactions") &&
            await Db.HasTableAsync("cash_drawer_transactions"))
        {
            var earliestOpenedAt = openSessions
                .Select(s => s.GetValueOrDefault("opened_at")?.ToString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .OrderBy(s => s, StringComparer.Ordinal)
                .FirstOrDefault();

            if (!string.IsNullOrWhiteSpace(earliestOpenedAt))
            {
                var openIds = openSessions.Select(s => AccountService.ToLong(s["id"])).ToArray();
                var openDrawerTxns = await Db.QueryListAsync(
                    "SELECT id, session_id, type, amount, reference_type, reference_id, notes, created_at FROM cash_drawer_transactions WHERE session_id IN @openIds",
                    new { openIds });
                var accTxnsInWindow = await Db.QueryListAsync(
                    @"SELECT id, type, amount, reference_type, reference_id, description, transaction_date
                      FROM account_transactions
                      WHERE account_id = @cashAccountId
                        AND transaction_date >= @earliestOpenedAt
                        AND COALESCE(reference_type, '') NOT IN ('cash_drawer_open', 'cash_drawer_variance', 'cash_drawer_reconcile', 'cash_drawer_manual', 'opening_balance')
                      ORDER BY id ASC",
                    new { cashAccountId, earliestOpenedAt });

                foreach (var at in accTxnsInWindow)
                {
                    var refType = (at.GetValueOrDefault("reference_type")?.ToString() ?? "").ToLowerInvariant();
                    var refId = AccountService.ToLong(at.GetValueOrDefault("reference_id"));
                    var amt = Math.Abs(AccountService.ToDecimal(at.GetValueOrDefault("amount")));

                    var matched = openDrawerTxns.Any(dt =>
                    {
                        var dRefId = AccountService.ToLong(dt.GetValueOrDefault("reference_id"));
                        var dAmt = Math.Abs(AccountService.ToDecimal(dt.GetValueOrDefault("amount")));
                        if (refId > 0 && dRefId == refId) return true;
                        if (refType is "transfer" or "manual" && Math.Abs(dAmt - amt) < 0.01m) return true;
                        return false;
                    });

                    if (!matched)
                    {
                        unmatchedAccountTxns.Add(at);
                    }
                }
            }
        }

        var diff = Math.Round(cashAccountBalance - openCalcTotal, 2);
        return Ok(new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["cash_account_id"] = cashAccount?.GetValueOrDefault("id"),
            ["cash_account_name"] = cashAccount?.GetValueOrDefault("name"),
            ["cash_account_balance"] = cashAccountBalance,
            ["account_ledger_sum"] = accountLedgerSum,
            ["account_balance_vs_ledger_diff"] = Math.Round(cashAccountBalance - accountLedgerSum, 2),
            ["open_sessions_count"] = openSessions.Count,
            ["stale_open_sessions_count"] = staleSessionsCount,
            ["open_drawers_expected_total"] = openCalcTotal,
            ["open_drawers_stored_before_refresh"] = openStoredTotal,
            ["difference"] = diff,
            ["unassigned_safe_pool"] = Math.Max(0m, diff),
            ["is_balanced"] = openSessions.Count > 0 ? Math.Abs(diff) < 0.01m : true,
            ["unmatched_open_shift_account_txns"] = unmatchedAccountTxns,
            ["open_sessions"] = sessionDiagnostics
        });
    }

    [HttpPost("api/cash-drawer/reconcile")]
    public async Task<IActionResult> ReconcileCashAccount()
    {
        if (!await CanAsync("cash-drawer.close", "account.adjust", "account.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var reason = GetString(body, "reason")?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = "Manual Cash Drawer to Cash in Hand reconciliation";
        }

        var cashAccount = await _accountService.SyncCashDrawerBalanceAsync();
        if (cashAccount == null)
        {
            return Unprocessable("Default Cash in Hand account not found.");
        }

        var openSessions = await Db.HasTableAsync("cash_drawer_sessions")
            ? await Db.QueryListAsync("SELECT * FROM cash_drawer_sessions WHERE status = 'open' ORDER BY id ASC")
            : new List<Dictionary<string, object?>>();

        if (openSessions.Count == 0)
        {
            return Unprocessable("No open cash drawer session found to reconcile against.");
        }

        decimal openTotal = 0m;
        foreach (var sess in openSessions)
        {
            openTotal = Math.Round(openTotal + await _cashDrawerService.RecalculateExpectedBalanceAsync(AccountService.ToLong(sess["id"]), syncAccount: false), 2);
        }

        var cashId = AccountService.ToLong(cashAccount["id"]);
        var currentBal = Math.Round(AccountService.ToDecimal(cashAccount.GetValueOrDefault("current_balance")), 2);
        var delta = Math.Round(openTotal - currentBal, 2);

        if (Math.Abs(delta) < 0.01m)
        {
            return Ok(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["reconciled"] = false,
                ["message"] = "Cash in Hand is already balanced with open Cash Drawer(s).",
                ["cash_account_balance"] = currentBal,
                ["open_drawers_expected_total"] = openTotal,
                ["adjustment_amount"] = 0m
            });
        }

        if (delta > 0m)
        {
            await _accountService.DepositAsync(cashAccount, delta, "cash_drawer_reconcile", null, reason, CurrentUserId);
        }
        else
        {
            await _accountService.WithdrawAsync(cashAccount, Math.Abs(delta), "cash_drawer_reconcile", null, reason, CurrentUserId);
        }

        SyncPulseService.Bump("drawer");
        var refreshedAcc = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = cashId });
        var newBal = Math.Round(AccountService.ToDecimal(refreshedAcc?.GetValueOrDefault("current_balance")), 2);

        return Ok(new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["reconciled"] = true,
            ["message"] = $"Reconciled Cash in Hand by {delta:+0.00;-0.00;0.00} to match open Cash Drawer total ({openTotal:N2}).",
            ["previous_balance"] = currentBal,
            ["cash_account_balance"] = newBal,
            ["open_drawers_expected_total"] = openTotal,
            ["adjustment_amount"] = delta
        });
    }

    [HttpGet("api/cash-drawer/session/{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("cash-drawer.view")) return Forbidden();

        var session = await Db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id });
        if (session == null) return NotFound(new { message = "Session not found." });

        var uid = AccountService.ToLong(session.GetValueOrDefault("user_id"));
        session["user"] = uid > 0 && await Db.HasTableAsync("users")
            ? await Db.QuerySingleOrDefaultAsync("SELECT id, name, email FROM users WHERE id = @uid", new { uid })
            : null;

        var txns = await Db.QueryListAsync(
            "SELECT * FROM cash_drawer_transactions WHERE session_id = @id ORDER BY created_at ASC, id ASC",
            new { id });

        foreach (var t in txns)
        {
            var tUid = AccountService.ToLong(t.GetValueOrDefault("user_id"));
            t["user"] = tUid > 0 && await Db.HasTableAsync("users")
                ? await Db.QuerySingleOrDefaultAsync("SELECT id, name FROM users WHERE id = @uid", new { uid = tUid })
                : null;
        }

        await _cashDrawerService.EnrichTransactionsAsync(txns);
        session["transactions"] = txns;

        return Ok(new Dictionary<string, object?>
        {
            ["session"] = session,
            ["summary"] = await _cashDrawerService.GetSessionSummaryAsync(session, txns),
            ["transactions"] = txns
        });
    }

    [HttpGet("api/cash-drawer/session/{id:long}/thermal-slip")]
    public async Task<IActionResult> ThermalSlipUrl(long id)
    {
        if (!await CanAsync("cash-drawer.view", "cash-drawer.open", "cash-drawer.close", "cash-drawer.transaction", "sale-invoice.create", "barcode-pos.view", "barcode-pos.create"))
            return Forbidden();

        var session = await Db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id });
        if (session == null) return NotFound(new { message = "Session not found." });

        var url = SignedUrlMiddleware.CreateSignedUrl(
            Request,
            $"/print/cash-drawer/{id}",
            new Dictionary<string, string?> { ["silent_print"] = "1" },
            TimeSpan.FromMinutes(5));

        var html = await RenderThermalHtmlAsync(session);
        var status = session.GetValueOrDefault("status")?.ToString() ?? "open";

        return Ok(new Dictionary<string, object?>
        {
            ["url"] = url,
            ["html"] = html,
            ["report_type"] = status.Equals("closed", StringComparison.OrdinalIgnoreCase) ? "Z-REPORT" : "X-REPORT"
        });
    }

    [AllowAnonymous]
    [RequireSignedUrl]
    [HttpGet("print/cash-drawer/{session:long}")]
    public async Task<IActionResult> PrintThermal(long session)
    {
        var sessionRow = await Db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id = session });
        if (sessionRow == null) return NotFound("Session not found.");

        var html = await RenderThermalHtmlAsync(sessionRow);
        return Content(html, "text/html; charset=UTF-8");
    }

    private async Task<string> RenderThermalHtmlAsync(Dictionary<string, object?> session)
    {
        var sessionId = AccountService.ToLong(session["id"]);
        var uid = AccountService.ToLong(session.GetValueOrDefault("user_id"));
        if (uid > 0 && await Db.HasTableAsync("users"))
        {
            session["user"] = await Db.QuerySingleOrDefaultAsync("SELECT id, name FROM users WHERE id = @uid", new { uid });
        }

        var summary = await _cashDrawerService.GetSessionSummaryAsync(session);
        var settingVm = await PrintViewModelMapper.LoadPrintSettingAsync(Db);

        DateTime? ParseDt(object? v) => DateTime.TryParse(v?.ToString(), out var dt) ? dt : null;
        var openedAt = ParseDt(summary.GetValueOrDefault("opened_at") ?? session.GetValueOrDefault("opened_at"));
        var closedAt = ParseDt(summary.GetValueOrDefault("closed_at") ?? session.GetValueOrDefault("closed_at"));
        var span = ((closedAt ?? DateTime.Now) - (openedAt ?? DateTime.Now));
        var duration = span.TotalMinutes >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : "< 1m";

        var model = new PrintCashDrawerZReportViewModel
        {
            SessionId = (int)sessionId,
            Status = summary.GetValueOrDefault("status")?.ToString() ?? session.GetValueOrDefault("status")?.ToString() ?? "open",
            CashierName = (summary.GetValueOrDefault("user") as Dictionary<string, object?>)?.GetValueOrDefault("name")?.ToString() ?? "Cashier",
            OpenedAt = openedAt,
            ClosedAt = closedAt,
            Duration = duration,
            OpeningBalance = AccountService.ToDecimal(summary.GetValueOrDefault("opening_balance")),
            TotalCashIn = AccountService.ToDecimal(summary.GetValueOrDefault("total_cash_in")),
            TotalCashOut = AccountService.ToDecimal(summary.GetValueOrDefault("total_cash_out")),
            ExpectedBalance = AccountService.ToDecimal(summary.GetValueOrDefault("expected_balance")),
            ClosingBalanceActual = summary.GetValueOrDefault("actual_balance") != null ? AccountService.ToDecimal(summary.GetValueOrDefault("actual_balance")) : null,
            Difference = summary.GetValueOrDefault("difference") != null ? AccountService.ToDecimal(summary.GetValueOrDefault("difference")) : null,
            Notes = session.GetValueOrDefault("notes")?.ToString()
        };

        if (summary.GetValueOrDefault("shift_sales") is Dictionary<string, object?> ss)
        {
            model.TotalInvoicesCount = (int)AccountService.ToLong(ss.GetValueOrDefault("total_invoices_count") ?? ss.GetValueOrDefault("total_invoices"));
            model.RetailInvoicesCount = (int)AccountService.ToLong(ss.GetValueOrDefault("retail_invoices_count") ?? ss.GetValueOrDefault("retail_invoices"));
            model.WholesaleInvoicesCount = (int)AccountService.ToLong(ss.GetValueOrDefault("wholesale_invoices_count") ?? ss.GetValueOrDefault("wholesale_invoices"));
            model.GrossSales = AccountService.ToDecimal(ss.GetValueOrDefault("gross_sales"));
            model.DiscountTotal = AccountService.ToDecimal(ss.GetValueOrDefault("discount_total"));
            model.TaxTotal = AccountService.ToDecimal(ss.GetValueOrDefault("tax_total"));
            model.NetSales = AccountService.ToDecimal(ss.GetValueOrDefault("net_sales"));
            model.CashTenderSales = AccountService.ToDecimal(ss.GetValueOrDefault("cash_tender_sales") ?? ss.GetValueOrDefault("cash_sales"));
            model.NonCashSales = AccountService.ToDecimal(ss.GetValueOrDefault("card_bank_online_sales") ?? ss.GetValueOrDefault("non_cash_sales"));
            model.CreditUdharSales = AccountService.ToDecimal(ss.GetValueOrDefault("credit_udhar_sales") ?? ss.GetValueOrDefault("credit_sales"));
            model.SaleReturnsCount = (int)AccountService.ToLong(ss.GetValueOrDefault("sale_returns_count"));
            model.SaleReturnsTotal = AccountService.ToDecimal(ss.GetValueOrDefault("sale_returns_total"));
            model.CashRefundReturns = AccountService.ToDecimal(ss.GetValueOrDefault("cash_refund_returns"));
            model.CreditAdjReturns = AccountService.ToDecimal(ss.GetValueOrDefault("credit_adj_returns"));
        }

        model.CashSalesAmount = AccountService.ToDecimal(summary.GetValueOrDefault("cash_sales"));
        model.CustomerPaymentsAmount = AccountService.ToDecimal(summary.GetValueOrDefault("customer_payments"));
        model.PurchaseReturnsAmount = AccountService.ToDecimal(summary.GetValueOrDefault("purchase_returns"));
        model.ManualCashInAmount = AccountService.ToDecimal(summary.GetValueOrDefault("manual_cash_in"));

        model.CashPurchasesAmount = AccountService.ToDecimal(summary.GetValueOrDefault("cash_purchases"));
        model.CashReturnsAmount = AccountService.ToDecimal(summary.GetValueOrDefault("cash_returns"));
        model.SupplierPaymentsAmount = AccountService.ToDecimal(summary.GetValueOrDefault("supplier_payments"));
        model.ExpensesAmount = AccountService.ToDecimal(summary.GetValueOrDefault("expenses"));
        model.StaffAdvancesAmount = AccountService.ToDecimal(summary.GetValueOrDefault("staff_advances"));
        model.ManualCashOutAmount = AccountService.ToDecimal(summary.GetValueOrDefault("manual_cash_out"));

        if (summary.GetValueOrDefault("counts") is Dictionary<string, object?> counts)
        {
            model.CashSalesCount = (int)AccountService.ToLong(counts.GetValueOrDefault("cash_sales"));
            model.CustomerPaymentsCount = (int)AccountService.ToLong(counts.GetValueOrDefault("customer_payments"));
            model.PurchaseReturnsCount = (int)AccountService.ToLong(counts.GetValueOrDefault("purchase_returns"));
            model.ManualCashInCount = (int)AccountService.ToLong(counts.GetValueOrDefault("manual_cash_in"));
            model.CashPurchasesCount = (int)AccountService.ToLong(counts.GetValueOrDefault("cash_purchases"));
            model.CashReturnsCount = (int)AccountService.ToLong(counts.GetValueOrDefault("cash_returns"));
            model.SupplierPaymentsCount = (int)AccountService.ToLong(counts.GetValueOrDefault("supplier_payments"));
            model.ExpensesCount = (int)AccountService.ToLong(counts.GetValueOrDefault("expenses"));
            model.StaffAdvancesCount = (int)AccountService.ToLong(counts.GetValueOrDefault("staff_advances"));
            model.ManualCashOutCount = (int)AccountService.ToLong(counts.GetValueOrDefault("manual_cash_out"));
        }

        return _renderer.RenderCashDrawerThermalZReport(model, settingVm, PrintViewModelMapper.QueryToDict(Request.Query));
    }
}

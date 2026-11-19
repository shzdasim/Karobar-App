using System.Text.Json;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/accounting")]
[Authorize]
public class AccountingController : ApiControllerBase
{
    private readonly AccountingService _accountingService;

    public AccountingController(KarobarDb db) : base(db)
    {
        _accountingService = new AccountingService(db);
    }

    [HttpGet("overview")]
    public async Task<IActionResult> Overview()
    {
        if (!await CanAsync("accounting.view", "accounting.manage", "report.financial-statements.view", "account.view", "report.net-profit.view"))
            return Forbidden();

        var now = DateTime.UtcNow;
        var from = Request.Query["from"].ToString() is { Length: > 0 } f ? f : new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");
        var to = Request.Query["to"].ToString() is { Length: > 0 } t ? t : now.ToString("yyyy-MM-dd");
        if (string.CompareOrdinal(from, to) > 0)
        {
            (from, to) = (to, from);
        }

        var source = Request.Query["source"].ToString() is { Length: > 0 } s ? s : "all";
        var q = Request.Query["q"].ToString() ?? "";

        return Ok(new Dictionary<string, object?>
        {
            ["period"] = new Dictionary<string, object?> { ["from"] = from, ["to"] = to },
            ["trial_balance"] = await _accountingService.GetTrialBalanceAsync(from, to),
            ["balance_sheet"] = await _accountingService.GetBalanceSheetAsync(to),
            ["cash_flow"] = await _accountingService.GetCashFlowStatementAsync(from, to),
            ["general_ledger"] = await _accountingService.GetGeneralLedgerAsync(new Dictionary<string, string?>
            {
                ["from"] = from,
                ["to"] = to,
                ["source"] = source,
                ["q"] = q
            }),
            ["chart_of_accounts"] = await _accountingService.GetChartOfAccountsAsync(),
            ["tax_rates"] = await _accountingService.GetTaxRatesAsync()
        });
    }

    [HttpGet("chart-of-accounts")]
    public async Task<IActionResult> ChartOfAccounts()
    {
        if (!await CanAsync("accounting.view", "accounting.manage", "report.financial-statements.view", "account.view", "report.net-profit.view"))
            return Forbidden();

        var activeOnly = string.Equals(Request.Query["active_only"], "1", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Request.Query["active_only"], "true", StringComparison.OrdinalIgnoreCase);

        return Ok(await _accountingService.GetChartOfAccountsAsync(activeOnly));
    }

    [HttpPost("chart-of-accounts")]
    public async Task<IActionResult> StoreAccount()
    {
        if (!await CanAsync("accounting.manage", "account.create", "account.update"))
            return Forbidden();

        await _accountingService.EnsureDefaultsAsync();
        var body = await ReadJsonBodyAsync();
        var code = GetString(body, "code")?.Trim()?.ToUpperInvariant();
        var name = GetString(body, "name")?.Trim();
        var accountType = GetString(body, "account_type")?.Trim()?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(code))
            return Unprocessable("The code field is required.", new() { ["code"] = new[] { "The code field is required." } });
        if (string.IsNullOrWhiteSpace(name))
            return Unprocessable("The name field is required.", new() { ["name"] = new[] { "The name field is required." } });

        var validTypes = new[] { "asset", "liability", "equity", "revenue", "expense" };
        if (string.IsNullOrWhiteSpace(accountType) || !validTypes.Contains(accountType))
            return Unprocessable("Invalid account type.", new() { ["account_type"] = new[] { "The selected account type is invalid." } });

        var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM chart_of_accounts WHERE code = @code", new { code }) > 0;
        if (exists)
            return Unprocessable("The code has already been taken.", new() { ["code"] = new[] { "The code has already been taken." } });

        var normalBalance = GetString(body, "normal_balance")?.Trim()?.ToLowerInvariant();
        if (normalBalance is not ("debit" or "credit"))
        {
            normalBalance = accountType is "asset" or "expense" ? "debit" : "credit";
        }

        var id = await Db.InsertAsync("chart_of_accounts", new Dictionary<string, object?>
        {
            ["code"] = code,
            ["name"] = name,
            ["account_type"] = accountType,
            ["sub_type"] = GetString(body, "sub_type") ?? accountType,
            ["normal_balance"] = normalBalance,
            ["opening_balance"] = Math.Round(GetDecimal(body, "opening_balance", 0m), 2),
            ["is_system"] = false,
            ["is_active"] = true,
            ["description"] = GetString(body, "description")
        });

        var account = await Db.QuerySingleOrDefaultAsync("SELECT * FROM chart_of_accounts WHERE id = @id", new { id });
        return StatusCode(201, account);
    }

    [HttpPut("chart-of-accounts/{chartOfAccount:long}")]
    public async Task<IActionResult> UpdateAccount(long chartOfAccount)
    {
        if (!await CanAsync("accounting.manage", "account.update", "account.create"))
            return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM chart_of_accounts WHERE id = @id", new { id = chartOfAccount });
        if (existing == null) return NotFound(new { message = "Chart of account not found." });

        var body = await ReadJsonBodyAsync();
        var code = GetString(body, "code")?.Trim()?.ToUpperInvariant();
        var name = GetString(body, "name")?.Trim();

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            return Unprocessable("Code and name are required.");

        var dup = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM chart_of_accounts WHERE code = @code AND id != @id",
            new { code, id = chartOfAccount }) > 0;
        if (dup)
            return Unprocessable("The code has already been taken.", new() { ["code"] = new[] { "The code has already been taken." } });

        await Db.UpdateAsync("chart_of_accounts", chartOfAccount, new Dictionary<string, object?>
        {
            ["code"] = code,
            ["name"] = name,
            ["opening_balance"] = body.ContainsKey("opening_balance")
                ? Math.Round(GetDecimal(body, "opening_balance"), 2)
                : AccountService.ToDecimal(existing.GetValueOrDefault("opening_balance")),
            ["is_active"] = body.ContainsKey("is_active")
                ? GetBool(body, "is_active")
                : AccountService.IsTruthy(existing.GetValueOrDefault("is_active")),
            ["description"] = GetString(body, "description") ?? existing.GetValueOrDefault("description")
        });

        var updated = await Db.QuerySingleOrDefaultAsync("SELECT * FROM chart_of_accounts WHERE id = @id", new { id = chartOfAccount });
        return Ok(updated);
    }

    [HttpGet("journal-entries")]
    public async Task<IActionResult> JournalEntries()
    {
        if (!await CanAsync("accounting.view", "accounting.manage", "report.financial-statements.view", "account.view", "report.net-profit.view"))
            return Forbidden();

        var now = DateTime.UtcNow;
        var from = Request.Query["from"].ToString() is { Length: > 0 } f ? f : new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");
        var to = Request.Query["to"].ToString() is { Length: > 0 } t ? t : now.ToString("yyyy-MM-dd");
        var source = Request.Query["source"].ToString() is { Length: > 0 } s ? s : "all";
        var q = Request.Query["q"].ToString() ?? "";

        return Ok(await _accountingService.GetGeneralLedgerAsync(new Dictionary<string, string?>
        {
            ["from"] = from,
            ["to"] = to,
            ["source"] = source,
            ["q"] = q
        }));
    }

    [HttpPost("journal-entries")]
    public async Task<IActionResult> StoreJournalEntry()
    {
        if (!await CanAsync("accounting.manage", "account.create", "account.update", "payment.create", "expense.create"))
            return Forbidden();

        var body = await ReadJsonBodyAsync();
        var entryDate = GetString(body, "entry_date");
        var description = GetString(body, "description")?.Trim();

        if (string.IsNullOrWhiteSpace(entryDate))
            return Unprocessable("The entry date field is required.", new() { ["entry_date"] = new[] { "The entry date field is required." } });
        if (string.IsNullOrWhiteSpace(description))
            return Unprocessable("The description field is required.", new() { ["description"] = new[] { "The description field is required." } });

        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in body)
        {
            if (kv.Key.Equals("lines", StringComparison.OrdinalIgnoreCase) && kv.Value.ValueKind == JsonValueKind.Array)
            {
                var lines = new List<Dictionary<string, object?>>();
                foreach (var item in kv.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        var ld = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prop in item.EnumerateObject())
                        {
                            ld[prop.Name] = KarobarDb.PrepareDbValue(prop.Value);
                        }
                        lines.Add(ld);
                    }
                }
                data["lines"] = lines;
            }
            else
            {
                data[kv.Key] = KarobarDb.PrepareDbValue(kv.Value);
            }
        }

        try
        {
            var entry = await _accountingService.CreateJournalEntryAsync(data, CurrentUserId);
            return StatusCode(201, new Dictionary<string, object?>
            {
                ["message"] = $"Journal Voucher {entry.GetValueOrDefault("entry_number")} posted successfully.",
                ["entry"] = entry
            });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message, new() { ["lines"] = new[] { ex.Message } });
        }
    }

    [HttpPost("journal-entries/{journalEntry:long}/reverse")]
    public async Task<IActionResult> ReverseJournalEntry(long journalEntry)
    {
        if (!await CanAsync("accounting.manage", "account.update", "account.delete"))
            return Forbidden();

        var body = await ReadJsonBodyAsync();
        var reason = GetString(body, "reason")?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 3)
        {
            return Unprocessable("A reversal reason of at least 3 characters is required.", new()
            {
                ["reason"] = new[] { "The reason field must be at least 3 characters." }
            });
        }

        try
        {
            var reversed = await _accountingService.ReverseJournalEntryAsync(journalEntry, reason, CurrentUserId);
            return Ok(new Dictionary<string, object?>
            {
                ["message"] = $"Journal Voucher {reversed.GetValueOrDefault("entry_number")} reversed.",
                ["entry"] = reversed
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message, new() { ["status"] = new[] { ex.Message } });
        }
    }

    [HttpGet("tax-rates")]
    public async Task<IActionResult> TaxRates()
    {
        if (!await CanAsync("accounting.view", "accounting.manage", "tax.manage", "account.view", "report.net-profit.view", "report.tax-return.view", "sale-invoice.view", "sale-invoice.create", "purchase-invoice.view", "purchase-invoice.create", "barcode-pos.view", "barcode-pos.create"))
            return Forbidden();

        var activeOnly = string.Equals(Request.Query["active_only"], "1", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Request.Query["active_only"], "true", StringComparison.OrdinalIgnoreCase);

        return Ok(await _accountingService.GetTaxRatesAsync(activeOnly));
    }

    [HttpPost("tax-rates")]
    public async Task<IActionResult> StoreTaxRate()
    {
        if (!await CanAsync("tax.manage", "accounting.manage", "account.create", "account.update"))
            return Forbidden();

        await _accountingService.EnsureDefaultsAsync();
        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim();
        var code = GetString(body, "code")?.Trim()?.ToUpperInvariant();
        var rate = GetDecimal(body, "rate", -1m);
        var type = GetString(body, "type")?.Trim()?.ToLowerInvariant();
        var isDefault = GetBool(body, "is_default", false);

        if (string.IsNullOrWhiteSpace(name))
            return Unprocessable("The name field is required.", new() { ["name"] = new[] { "The name field is required." } });
        if (string.IsNullOrWhiteSpace(code))
            return Unprocessable("The code field is required.", new() { ["code"] = new[] { "The code field is required." } });
        if (rate < 0m || rate > 100m)
            return Unprocessable("Rate must be between 0 and 100.", new() { ["rate"] = new[] { "Rate must be between 0 and 100." } });
        if (type is not ("both" or "sales" or "purchase" or "withholding"))
            return Unprocessable("Invalid tax rate type.", new() { ["type"] = new[] { "The selected type is invalid." } });

        var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM tax_rates WHERE code = @code", new { code }) > 0;
        if (exists)
            return Unprocessable("The code has already been taken.", new() { ["code"] = new[] { "The code has already been taken." } });

        if (isDefault)
        {
            await Db.ExecuteAsync("UPDATE tax_rates SET is_default = 0");
        }

        var id = await Db.InsertAsync("tax_rates", new Dictionary<string, object?>
        {
            ["name"] = name,
            ["code"] = code,
            ["rate"] = Math.Round(rate, 2),
            ["type"] = type,
            ["is_default"] = isDefault,
            ["is_active"] = true,
            ["description"] = GetString(body, "description")
        });

        var taxRate = await Db.QuerySingleOrDefaultAsync("SELECT * FROM tax_rates WHERE id = @id", new { id });
        return StatusCode(201, taxRate);
    }

    [HttpPut("tax-rates/{taxRate:long}")]
    public async Task<IActionResult> UpdateTaxRate(long taxRate)
    {
        if (!await CanAsync("tax.manage", "accounting.manage", "account.update", "account.create"))
            return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM tax_rates WHERE id = @id", new { id = taxRate });
        if (existing == null) return NotFound(new { message = "Tax rate not found." });

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim();
        var code = GetString(body, "code")?.Trim()?.ToUpperInvariant();
        var rate = GetDecimal(body, "rate", -1m);
        var type = GetString(body, "type")?.Trim()?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
            return Unprocessable("Name and code are required.");
        if (rate < 0m || rate > 100m)
            return Unprocessable("Rate must be between 0 and 100.");
        if (type is not ("both" or "sales" or "purchase" or "withholding"))
            return Unprocessable("Invalid tax rate type.");

        var dup = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM tax_rates WHERE code = @code AND id != @id", new { code, id = taxRate }) > 0;
        if (dup)
            return Unprocessable("The code has already been taken.", new() { ["code"] = new[] { "The code has already been taken." } });

        var isDefault = body.ContainsKey("is_default")
            ? GetBool(body, "is_default")
            : AccountService.IsTruthy(existing.GetValueOrDefault("is_default"));

        if (isDefault)
        {
            await Db.ExecuteAsync("UPDATE tax_rates SET is_default = 0 WHERE id != @id", new { id = taxRate });
        }

        await Db.UpdateAsync("tax_rates", taxRate, new Dictionary<string, object?>
        {
            ["name"] = name,
            ["code"] = code,
            ["rate"] = Math.Round(rate, 2),
            ["type"] = type,
            ["is_default"] = isDefault,
            ["is_active"] = body.ContainsKey("is_active") ? GetBool(body, "is_active") : AccountService.IsTruthy(existing.GetValueOrDefault("is_active")),
            ["description"] = GetString(body, "description")
        });

        var updated = await Db.QuerySingleOrDefaultAsync("SELECT * FROM tax_rates WHERE id = @id", new { id = taxRate });
        return Ok(updated);
    }

    [HttpDelete("tax-rates/{taxRate:long}")]
    public async Task<IActionResult> DestroyTaxRate(long taxRate)
    {
        if (!await CanAsync("tax.manage", "accounting.manage", "account.delete", "account.update"))
            return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM tax_rates WHERE id = @id", new { id = taxRate });
        if (existing == null) return NotFound(new { message = "Tax rate not found." });

        await Db.ExecuteAsync("DELETE FROM tax_rates WHERE id = @id", new { id = taxRate });
        return Ok(new Dictionary<string, object?> { ["message"] = "Tax rate deleted." });
    }
}

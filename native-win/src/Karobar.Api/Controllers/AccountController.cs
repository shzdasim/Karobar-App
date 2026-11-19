using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/accounts")]
public class AccountController : ApiControllerBase
{
    private readonly AccountService _accountService;

    public static readonly object[] PakistanBanks =
    {
        new { code = "meezan", name = "Meezan Bank", full_name = "Meezan Bank Limited (Islamic)", type = "bank", color = "#005b41", icon = "meezan" },
        new { code = "hbl", name = "HBL", full_name = "Habib Bank Limited", type = "bank", color = "#00835f", icon = "hbl" },
        new { code = "ubl", name = "UBL", full_name = "United Bank Limited", type = "bank", color = "#00539f", icon = "ubl" },
        new { code = "mcb", name = "MCB Bank", full_name = "Muslim Commercial Bank Limited", type = "bank", color = "#eb6909", icon = "mcb" },
        new { code = "alfalah", name = "Bank Alfalah", full_name = "Bank Alfalah Limited", type = "bank", color = "#dc2626", icon = "alfalah" },
        new { code = "abl", name = "Allied Bank (ABL)", full_name = "Allied Bank Limited", type = "bank", color = "#004b87", icon = "abl" },
        new { code = "faysal", name = "Faysal Bank", full_name = "Faysal Bank Limited (Islamic)", type = "bank", color = "#1a365d", icon = "faysal" },
        new { code = "bop", name = "Bank of Punjab (BOP)", full_name = "The Bank of Punjab", type = "bank", color = "#b91c1c", icon = "bop" },
        new { code = "askari", name = "Askari Bank", full_name = "Askari Bank Limited", type = "bank", color = "#065f46", icon = "askari" },
        new { code = "scb", name = "Standard Chartered", full_name = "Standard Chartered Bank (Pakistan)", type = "bank", color = "#0284c7", icon = "scb" },
        new { code = "nbp", name = "National Bank (NBP)", full_name = "National Bank of Pakistan", type = "bank", color = "#047857", icon = "nbp" },
        new { code = "habibmetro", name = "Habib Metro", full_name = "Habib Metropolitan Bank", type = "bank", color = "#1e40af", icon = "habibmetro" },
        new { code = "soneri", name = "Soneri Bank", full_name = "Soneri Bank Limited", type = "bank", color = "#ca8a04", icon = "soneri" },
        new { code = "bankislami", name = "BankIslami", full_name = "BankIslami Pakistan Limited", type = "bank", color = "#0d9488", icon = "bankislami" },
        new { code = "dib", name = "Dubai Islamic Bank (DIB)", full_name = "Dubai Islamic Bank Pakistan", type = "bank", color = "#854d0e", icon = "dib" },
        new { code = "albaraka", name = "Al Baraka Bank", full_name = "Al Baraka Bank (Pakistan) Limited", type = "bank", color = "#0284c7", icon = "albaraka" },
        new { code = "js", name = "JS Bank", full_name = "JS Bank Limited", type = "bank", color = "#0369a1", icon = "js" },
        new { code = "bok", name = "Bank of Khyber (BOK)", full_name = "The Bank of Khyber", type = "bank", color = "#059669", icon = "bok" },
        new { code = "sindh", name = "Sindh Bank", full_name = "Sindh Bank Limited", type = "bank", color = "#4338ca", icon = "sindh" },
        new { code = "ztbl", name = "ZTBL", full_name = "Zarai Taraqiati Bank Limited", type = "bank", color = "#15803d", icon = "ztbl" },
        new { code = "samba", name = "Samba Bank", full_name = "Samba Bank Limited", type = "bank", color = "#0369a1", icon = "samba" },
        new { code = "silk", name = "Silkbank", full_name = "Silkbank Limited", type = "bank", color = "#831843", icon = "silk" }
    };

    public static readonly object[] PakistanWallets =
    {
        new { code = "jazzcash", name = "JazzCash", full_name = "JazzCash Mobile Account (Mobilink Bank)", type = "mobile_wallet", color = "#dc2626", icon = "jazzcash" },
        new { code = "easypaisa", name = "Easypaisa", full_name = "Easypaisa Digital Account (Telenor Bank)", type = "mobile_wallet", color = "#16a34a", icon = "easypaisa" },
        new { code = "sadapay", name = "SadaPay", full_name = "SadaPay Business / Personal Wallet", type = "mobile_wallet", color = "#0d9488", icon = "sadapay" },
        new { code = "nayapay", name = "NayaPay", full_name = "NayaPay Arc / Digital Wallet", type = "mobile_wallet", color = "#ea580c", icon = "nayapay" },
        new { code = "upaisa", name = "UPaisa", full_name = "UPaisa (U Microfinance Bank)", type = "mobile_wallet", color = "#f97316", icon = "upaisa" },
        new { code = "zindigi", name = "Zindigi", full_name = "Zindigi Wallet by JS Bank", type = "mobile_wallet", color = "#7c3aed", icon = "zindigi" },
        new { code = "raast", name = "Raast P2M", full_name = "State Bank of Pakistan Raast", type = "mobile_wallet", color = "#059669", icon = "raast" }
    };

    public AccountController(KarobarDb db) : base(db)
    {
        _accountService = new AccountService(db);
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var activeOnly = string.Equals(Request.Query["active_only"], "1", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(Request.Query["active_only"], "true", StringComparison.OrdinalIgnoreCase);

        if (activeOnly)
        {
            if (!await CanAsync("account.view", "sale-invoice.create", "sale-invoice.update", "sale-return.create", "sale-return.update", "barcode-pos.view", "barcode-pos.create", "purchase-invoice.create", "purchase-invoice.update", "purchase-return.create", "purchase-return.update", "payment.create", "expense.create", "expense.update"))
                return Forbidden();
        }
        else
        {
            if (!await CanAsync("account.view"))
                return Forbidden();
        }

        var summary = await _accountService.GetSummaryAsync(CurrentUserId);
        return Ok(summary);
    }

    [HttpGet("presets")]
    public async Task<IActionResult> Presets()
    {
        if (!await CanAsync("account.view")) return Forbidden();
        return Ok(new Dictionary<string, object?>
        {
            ["banks"] = PakistanBanks,
            ["wallets"] = PakistanWallets
        });
    }

    [HttpPost("seed-defaults")]
    public async Task<IActionResult> SeedDefaults()
    {
        if (!await CanAsync("account.create")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var includePresets = GetBool(body, "include_presets",
            string.Equals(Request.Query["include_presets"], "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Request.Query["include_presets"], "true", StringComparison.OrdinalIgnoreCase));

        var seeded = new List<Dictionary<string, object?>>();
        var cashDrawer = await _accountService.SyncCashDrawerBalanceAsync();
        if (cashDrawer != null && cashDrawer.TryGetValue("was_recently_created", out var wrc) && wrc is true)
        {
            cashDrawer.Remove("was_recently_created");
            seeded.Add(cashDrawer);
        }

        if (includePresets)
        {
            var popular = new (string Code, string Name, string BankName, string Type, string Icon, string Color, string Notes)[]
            {
                ("meezan", "Meezan Bank", "Meezan Bank Limited", "bank", "meezan", "#005b41", "Primary Islamic bank account"),
                ("hbl", "HBL (Habib Bank)", "Habib Bank Limited", "bank", "hbl", "#00835f", "Commercial bank account"),
                ("jazzcash", "JazzCash Business", "JazzCash", "mobile_wallet", "jazzcash", "#dc2626", "Mobile wallet for retail customer transfers"),
                ("easypaisa", "Easypaisa Business", "Easypaisa", "mobile_wallet", "easypaisa", "#16a34a", "Mobile wallet for digital payments"),
                ("sadapay", "SadaPay Business", "SadaPay", "mobile_wallet", "sadapay", "#0d9488", "Digital fintech account")
            };

            foreach (var item in popular)
            {
                var existing = await Db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM accounts WHERE code = @code OR (name = @name AND type = @type) LIMIT 1",
                    new { code = item.Code, name = item.Name, type = item.Type });

                if (existing == null)
                {
                    var id = await Db.InsertAsync("accounts", new Dictionary<string, object?>
                    {
                        ["name"] = item.Name,
                        ["code"] = item.Code,
                        ["icon"] = item.Icon,
                        ["color"] = item.Color,
                        ["type"] = item.Type,
                        ["bank_name"] = item.BankName,
                        ["account_number"] = null,
                        ["opening_balance"] = 0m,
                        ["current_balance"] = 0m,
                        ["is_active"] = true,
                        ["is_default"] = false,
                        ["notes"] = item.Notes
                    });
                    var acc = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id });
                    if (acc != null) seeded.Add(acc);
                }
            }
        }

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = seeded.Count > 0 ? $"Created {seeded.Count} default account(s)" : "Default Cash in Hand account is ready",
            ["seeded"] = seeded,
            ["summary"] = await _accountService.GetSummaryAsync(CurrentUserId)
        });
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("account.create")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim();
        var type = GetString(body, "type")?.Trim()?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(name))
            return Unprocessable("The name field is required.", new() { ["name"] = new[] { "The name field is required." } });

        var validTypes = new[] { "cash", "bank", "card", "mobile_wallet", "other" };
        if (string.IsNullOrWhiteSpace(type) || !validTypes.Contains(type))
            return Unprocessable("Invalid account type.", new() { ["type"] = new[] { "Invalid account type." } });

        var opening = Math.Max(0m, Math.Round(GetDecimal(body, "opening_balance", 0m), 2));
        var isDefault = GetBool(body, "is_default", false);

        if (type == "cash")
        {
            var hasDefaultCash = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM accounts WHERE type = 'cash' AND is_default = 1") > 0;
            if (hasDefaultCash) isDefault = false;
        }

        var id = await Db.InsertAsync("accounts", new Dictionary<string, object?>
        {
            ["name"] = name,
            ["type"] = type,
            ["code"] = GetString(body, "code"),
            ["icon"] = GetString(body, "icon"),
            ["color"] = GetString(body, "color"),
            ["bank_name"] = GetString(body, "bank_name"),
            ["account_number"] = GetString(body, "account_number"),
            ["opening_balance"] = opening,
            ["current_balance"] = opening,
            ["is_active"] = true,
            ["is_default"] = isDefault,
            ["notes"] = GetString(body, "notes")
        });

        if (opening > 0m && await Db.HasTableAsync("account_transactions"))
        {
            await Db.InsertAsync("account_transactions", new Dictionary<string, object?>
            {
                ["account_id"] = id,
                ["user_id"] = CurrentUserId,
                ["type"] = "opening_balance",
                ["amount"] = opening,
                ["balance_after"] = opening,
                ["reference_type"] = "opening_balance",
                ["reference_id"] = id,
                ["description"] = $"Initial opening balance for {name}",
                ["transaction_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }

        var account = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id });
        return StatusCode(201, new Dictionary<string, object?>
        {
            ["message"] = "Account created successfully",
            ["account"] = account
        });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("account.update")) return Forbidden();

        var account = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id });
        if (account == null) return NotFound(new { message = "Account not found." });

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim();
        var type = GetString(body, "type")?.Trim()?.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(name))
            return Unprocessable("The name field is required.", new() { ["name"] = new[] { "The name field is required." } });

        var validTypes = new[] { "cash", "bank", "card", "mobile_wallet", "other" };
        if (string.IsNullOrWhiteSpace(type) || !validTypes.Contains(type))
            return Unprocessable("Invalid account type.", new() { ["type"] = new[] { "Invalid account type." } });

        var isPrimaryCashDrawer = string.Equals(account.GetValueOrDefault("code")?.ToString(), "cash_drawer", StringComparison.OrdinalIgnoreCase) ||
                                  (string.Equals(account.GetValueOrDefault("type")?.ToString(), "cash", StringComparison.OrdinalIgnoreCase) &&
                                   AccountService.IsTruthy(account.GetValueOrDefault("is_default")));

        var targetType = isPrimaryCashDrawer ? "cash" : type;
        var targetIsDefault = isPrimaryCashDrawer
            ? true
            : (targetType == "cash" ? false : (body.ContainsKey("is_default") ? GetBool(body, "is_default") : AccountService.IsTruthy(account.GetValueOrDefault("is_default"))));

        await Db.UpdateAsync("accounts", id, new Dictionary<string, object?>
        {
            ["name"] = name,
            ["type"] = targetType,
            ["code"] = isPrimaryCashDrawer ? "cash_drawer" : (body.ContainsKey("code") ? GetString(body, "code") : account.GetValueOrDefault("code")),
            ["icon"] = body.ContainsKey("icon") ? GetString(body, "icon") : account.GetValueOrDefault("icon"),
            ["color"] = body.ContainsKey("color") ? GetString(body, "color") : account.GetValueOrDefault("color"),
            ["bank_name"] = GetString(body, "bank_name"),
            ["account_number"] = GetString(body, "account_number"),
            ["is_active"] = isPrimaryCashDrawer ? true : (body.ContainsKey("is_active") ? GetBool(body, "is_active") : AccountService.IsTruthy(account.GetValueOrDefault("is_active"))),
            ["is_default"] = targetIsDefault,
            ["notes"] = GetString(body, "notes")
        });

        var updated = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id });
        return Ok(new Dictionary<string, object?>
        {
            ["message"] = "Account updated successfully",
            ["account"] = updated
        });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("account.delete", "account.update")) return Forbidden();

        var account = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id });
        if (account == null) return NotFound(new { message = "Account not found." });

        if (string.Equals(account.GetValueOrDefault("code")?.ToString(), "cash_drawer", StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(account.GetValueOrDefault("type")?.ToString(), "cash", StringComparison.OrdinalIgnoreCase) &&
             AccountService.IsTruthy(account.GetValueOrDefault("is_default"))))
        {
            return Unprocessable("Cannot delete the default Cash in Hand (Drawer) system account.");
        }

        var bal = AccountService.ToDecimal(account.GetValueOrDefault("current_balance"));
        if (Math.Abs(bal) >= 0.01m)
        {
            return Unprocessable($"Cannot delete an account with a non-zero balance ({bal:N2}). Transfer or withdraw the remaining balance first.");
        }

        foreach (var tbl in new[] { "sale_invoices", "purchase_invoices", "payments", "sale_returns", "purchase_returns", "expenses" })
        {
            if (await Db.HasTableAsync(tbl) && await Db.HasColumnAsync(tbl, "account_id"))
            {
                await Db.ExecuteAsync($"UPDATE \"{tbl}\" SET account_id = NULL WHERE account_id = @id", new { id });
            }
        }

        if (await Db.HasTableAsync("account_transactions"))
        {
            await Db.ExecuteAsync("DELETE FROM account_transactions WHERE account_id = @id", new { id });
        }
        await Db.ExecuteAsync("DELETE FROM accounts WHERE id = @id", new { id });

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = "Account deleted successfully."
        });
    }

    [HttpGet("{id:long}/transactions")]
    public async Task<IActionResult> Transactions(long id)
    {
        if (!await CanAsync("account.view")) return Forbidden();

        var account = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id });
        if (account == null) return NotFound(new { message = "Account not found." });

        if (_accountService.IsDefaultCashDrawerAccount(account))
        {
            account = await _accountService.ReconcileWithOpenCashDrawersAsync(CurrentUserId) ?? account;
        }

        var where = new List<string> { "account_id = @id" };
        var dyn = new DynamicParameters();
        dyn.Add("id", id);

        var type = Request.Query["type"].ToString();
        if (!string.IsNullOrWhiteSpace(type))
        {
            where.Add("type = @type");
            dyn.Add("type", type);
        }

        var fromDate = Request.Query["from_date"].ToString();
        if (!string.IsNullOrWhiteSpace(fromDate))
        {
            where.Add("DATE(transaction_date) >= @fromDate");
            dyn.Add("fromDate", fromDate);
        }

        var toDate = Request.Query["to_date"].ToString();
        if (!string.IsNullOrWhiteSpace(toDate))
        {
            where.Add("DATE(transaction_date) <= @toDate");
            dyn.Add("toDate", toDate);
        }

        var whereSql = string.Join(" AND ", where);
        var page = int.TryParse(Request.Query["page"], out var p) ? Math.Max(1, p) : 1;
        var perPage = int.TryParse(Request.Query["per_page"], out var pp) ? Math.Max(1, pp) : 25;

        var transactions = await Db.PaginateAsync(
            $"SELECT * FROM account_transactions WHERE {whereSql} ORDER BY transaction_date DESC, id DESC",
            $"SELECT COUNT(1) FROM account_transactions WHERE {whereSql}",
            dyn,
            page,
            perPage,
            async batch =>
            {
                foreach (var row in batch)
                {
                    var uid = AccountService.ToLong(row.GetValueOrDefault("user_id"));
                    row["user"] = uid > 0 && await Db.HasTableAsync("users")
                        ? await Db.QuerySingleOrDefaultAsync("SELECT id, name FROM users WHERE id = @uid", new { uid })
                        : null;
                }
                return batch;
            });

        return Ok(new Dictionary<string, object?>
        {
            ["account"] = account,
            ["transactions"] = transactions
        });
    }

    [HttpPost("transfer")]
    public async Task<IActionResult> Transfer()
    {
        if (!await CanAsync("account.transfer", "account.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var fromId = GetNullableLong(body, "from_account_id") ?? 0;
        var toId = GetNullableLong(body, "to_account_id") ?? 0;
        var amount = GetDecimal(body, "amount", 0m);
        var notes = GetString(body, "notes");

        if (fromId <= 0 || toId <= 0)
            return Unprocessable("Source and destination accounts are required.");
        if (fromId == toId)
            return Unprocessable("Source and destination accounts must be different.", new() { ["to_account_id"] = new[] { "Source and destination accounts must be different." } });
        if (amount <= 0m)
            return Unprocessable("Transfer amount must be greater than zero.", new() { ["amount"] = new[] { "Transfer amount must be greater than zero." } });

        try
        {
            var res = await _accountService.TransferAsync(fromId, toId, amount, notes, CurrentUserId);
            var fromName = (res["from_account"] as Dictionary<string, object?>)?.GetValueOrDefault("name")?.ToString() ?? "Account";
            var toName = (res["to_account"] as Dictionary<string, object?>)?.GetValueOrDefault("name")?.ToString() ?? "Account";
            return Ok(new Dictionary<string, object?>
            {
                ["message"] = $"Successfully transferred {amount} from {fromName} to {toName}.",
                ["result"] = res
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpPost("{id:long}/adjust")]
    public async Task<IActionResult> Adjust(long id)
    {
        if (!await CanAsync("account.adjust", "account.update")) return Forbidden();

        var account = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id });
        if (account == null) return NotFound(new { message = "Account not found." });

        var body = await ReadJsonBodyAsync();
        var type = GetString(body, "type")?.Trim()?.ToLowerInvariant();
        var amount = GetDecimal(body, "amount", 0m);
        var reason = GetString(body, "reason")?.Trim();

        if (type is not ("deposit" or "withdrawal"))
            return Unprocessable("Invalid adjustment type.", new() { ["type"] = new[] { "The selected type is invalid." } });
        if (amount <= 0m)
            return Unprocessable("Adjustment amount must be greater than zero.", new() { ["amount"] = new[] { "Adjustment amount must be greater than zero." } });
        if (string.IsNullOrWhiteSpace(reason))
            return Unprocessable("Reason is required.", new() { ["reason"] = new[] { "The reason field is required." } });

        var txn = await _accountService.AdjustAsync(id, type, amount, reason, CurrentUserId);
        var updatedAccount = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id });

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = "Account adjusted successfully.",
            ["transaction"] = txn,
            ["account"] = updatedAccount
        });
    }
}

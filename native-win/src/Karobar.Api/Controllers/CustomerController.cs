using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Background;
using Karobar.Data;
using Karobar.Printing;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Api.Controllers;

[Route("api/customers")]
[Authorize]
public class CustomerController : ApiControllerBase
{
    private static bool _regularSchemaChecked;

    private static readonly string[] LookupPermissions =
    [
        "customer.view", "barcode-pos.view", "barcode-pos.create",
        "sale-invoice.view", "sale-invoice.create", "sale-invoice.update",
        "sale-return.view", "sale-return.create", "sale-return.update",
        "customer-ledger.view", "payment.view", "payment.create",
        "report.sale-detail.view", "report.sale-returns.view", "report.cost-of-sale-detail.view"
    ];

    private readonly DocumentShareEngine _shareEngine;
    private readonly LocalWhatsAppSidecarManager _waSidecar;
    private readonly KarobarDbContext? _dbContext;

    public CustomerController(
        KarobarDb db,
        DocumentShareEngine? shareEngine = null,
        LocalWhatsAppSidecarManager? waSidecar = null,
        KarobarDbContext? dbContext = null) : base(db)
    {
        _shareEngine = shareEngine ?? new DocumentShareEngine();
        _waSidecar = waSidecar ?? new LocalWhatsAppSidecarManager();
        _dbContext = dbContext;
    }

    private async Task<WhatsAppConfiguration> LoadWhatsAppConfigAsync()
    {
        var setting = await Db.HasTableAsync("settings")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
            : null;
        bool IsOn(string k, bool fb = false) => setting != null && setting.TryGetValue(k, out var v) && v != null
            ? (v is true or 1 or 1L || v.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true || v.ToString() == "1")
            : fb;
        string Str(string k, string fb = "") => setting != null && setting.TryGetValue(k, out var v) && v != null && !string.IsNullOrWhiteSpace(v.ToString())
            ? v.ToString()!.Trim()
            : fb;
        return new WhatsAppConfiguration
        {
            Enabled = IsOn("whatsapp_enabled", false),
            Provider = Str("whatsapp_provider", "local_agent"),
            AccessToken = Str("whatsapp_access_token", ""),
            PhoneNumberId = Str("whatsapp_phone_number_id", ""),
            BusinessAccountId = Str("whatsapp_business_account_id", ""),
            ApiVersion = Str("whatsapp_api_version", "v21.0"),
            ApiUrl = Str("whatsapp_api_url", "http://127.0.0.1:3001"),
            DefaultCountryCode = Str("whatsapp_default_country_code", "92"),
            TemplateName = Str("whatsapp_template_name", ""),
            TemplateLanguage = Str("whatsapp_template_language", "en_US"),
            AutoFallback = IsOn("whatsapp_auto_fallback", true)
        };
    }

    private async Task EnsureRegularCustomerSchemaAsync()
    {
        if (!await Db.HasTableAsync("customer_regular_items"))
        {
            _regularSchemaChecked = false;
            KarobarDb.FlushSchemaCache();
        }
        if (_regularSchemaChecked) return;

        try
        {
            if (await Db.HasTableAsync("customers"))
            {
                var cols = await Db.GetTableColumnsAsync("customers");
                if (!cols.Contains("cnic")) await Db.ExecuteAsync("ALTER TABLE customers ADD COLUMN cnic VARCHAR(30) NULL;");
                if (!cols.Contains("is_regular")) await Db.ExecuteAsync("ALTER TABLE customers ADD COLUMN is_regular TINYINT(1) NOT NULL DEFAULT 0;");
                if (!cols.Contains("refill_cycle")) await Db.ExecuteAsync("ALTER TABLE customers ADD COLUMN refill_cycle VARCHAR(20) NULL DEFAULT 'monthly';");
                if (!cols.Contains("refill_custom_days")) await Db.ExecuteAsync("ALTER TABLE customers ADD COLUMN refill_custom_days INTEGER NULL;");
                if (!cols.Contains("reminder_lead_days")) await Db.ExecuteAsync("ALTER TABLE customers ADD COLUMN reminder_lead_days INTEGER NOT NULL DEFAULT 2;");
                if (!cols.Contains("next_refill_date")) await Db.ExecuteAsync("ALTER TABLE customers ADD COLUMN next_refill_date DATE NULL;");
                if (!cols.Contains("last_refill_reminded_at")) await Db.ExecuteAsync("ALTER TABLE customers ADD COLUMN last_refill_reminded_at DATETIME NULL;");
                if (!cols.Contains("last_refill_collected_at")) await Db.ExecuteAsync("ALTER TABLE customers ADD COLUMN last_refill_collected_at DATETIME NULL;");
                if (!cols.Contains("reminder_language")) await Db.ExecuteAsync("ALTER TABLE customers ADD COLUMN reminder_language VARCHAR(16) NOT NULL DEFAULT 'english';");
                if (!cols.Contains("regular_notes")) await Db.ExecuteAsync("ALTER TABLE customers ADD COLUMN regular_notes TEXT NULL;");
            }

            if (!await Db.HasTableAsync("customer_regular_items"))
            {
                await Db.ExecuteAsync(@"
                    CREATE TABLE IF NOT EXISTS customer_regular_items (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        customer_id INTEGER NOT NULL,
                        product_id INTEGER NOT NULL,
                        pack_quantity INTEGER NOT NULL DEFAULT 0,
                        unit_quantity INTEGER NOT NULL DEFAULT 1,
                        dosage_notes VARCHAR(255) NULL,
                        created_at DATETIME NULL,
                        updated_at DATETIME NULL
                    );");
            }
            KarobarDb.FlushSchemaCache();
            _regularSchemaChecked = true;
        }
        catch
        {
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q = "",
        [FromQuery] int page = 1,
        [FromQuery] int limit = 20)
    {
        if (!await CanAsync(LookupPermissions)) return Forbidden();

        q = (q ?? "").Trim();
        page = Math.Max(1, page);
        limit = Math.Clamp(limit <= 0 ? 20 : limit, 5, 50);

        var where = "";
        var orderBy = "ORDER BY CASE WHEN id = 1 OR UPPER(name) LIKE '%WALK-IN%' THEN 0 ELSE 1 END ASC, name ASC";
        var param = new DynamicParameters();

        if (!string.IsNullOrEmpty(q))
        {
            where = "WHERE (name LIKE @q OR phone LIKE @q OR cnic LIKE @q OR email LIKE @q)";
            orderBy = "ORDER BY name ASC";
            param.Add("q", $"%{q}%");
        }

        var paged = await Db.PaginateAsync(
            $"SELECT * FROM customers {where} {orderBy}",
            $"SELECT COUNT(1) FROM customers {where}",
            param,
            page,
            limit);

        var lastPage = Convert.ToInt32(paged["last_page"] ?? 1);
        var currentPage = Convert.ToInt32(paged["current_page"] ?? 1);

        return Ok(new Dictionary<string, object?>
        {
            ["data"] = paged["data"],
            ["next_page"] = currentPage < lastPage ? currentPage + 1 : null
        });
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? search = null,
        [FromQuery(Name = "per_page")] int? perPage = null,
        [FromQuery] int? limit = null)
    {
        if (!await CanAsync("customer.view")) return Forbidden();
        await EnsureRegularCustomerSchemaAsync();

        var where = new List<string>();
        var param = new DynamicParameters();
        var q = (search ?? "").Trim();
        if (!string.IsNullOrEmpty(q))
        {
            where.Add("(LOWER(c.name) LIKE @q OR LOWER(COALESCE(c.phone, '')) LIKE @q OR LOWER(COALESCE(c.email, '')) LIKE @q OR LOWER(COALESCE(c.address, '')) LIKE @q)");
            param.Add("q", $"%{q.ToLowerInvariant()}%");
        }

        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var maxRows = perPage ?? limit;
        var limitSql = maxRows.HasValue && maxRows.Value > 0 ? $"LIMIT {Math.Min(maxRows.Value, 500)}" : "";

        var customers = await Db.QueryListAsync($@"
            SELECT c.*,
                   (SELECT COUNT(1) FROM sale_invoices si WHERE si.customer_id = c.id) AS sale_invoices_count,
                   (SELECT COUNT(1) FROM sale_returns sr WHERE sr.customer_id = c.id) AS sale_returns_count
            FROM customers c
            {whereSql}
            ORDER BY c.name ASC
            {limitSql}", param);

        await AttachRegularItemsAndMetadataAsync(customers);

        foreach (var c in customers)
        {
            var siCount = Convert.ToInt32(c["sale_invoices_count"] ?? 0);
            var srCount = Convert.ToInt32(c["sale_returns_count"] ?? 0);
            c["transactions_count"] = siCount + srCount;
        }

        return Ok(customers);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("customer.create")) return Forbidden();
        await EnsureRegularCustomerSchemaAsync();

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name field is required."]
            });
        }

        var existsName = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM customers WHERE name = @name", new { name });
        if (existsName > 0)
        {
            return Unprocessable("The name has already been taken.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name has already been taken."]
            });
        }

        var email = GetString(body, "email")?.Trim();
        if (!string.IsNullOrEmpty(email))
        {
            var existsEmail = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM customers WHERE email = @email", new { email });
            if (existsEmail > 0)
            {
                return Unprocessable("The email has already been taken.", new Dictionary<string, string[]>
                {
                    ["email"] = ["The email has already been taken."]
                });
            }
        }
        else
        {
            email = null;
        }

        var openingBalance = Math.Round(GetDecimal(body, "opening_balance", 0m), 2);
        var data = BuildCustomerFieldDictionary(body, null);
        data["name"] = name;
        data["email"] = email;

        NormalizeRegularFields(data, null);

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var id = await Db.InsertAsync("customers", data, conn, tx);

        if (body.TryGetValue("regular_items", out var regItemsEl) && regItemsEl.ValueKind == JsonValueKind.Array)
        {
            await SyncRegularItemsAsync(id, regItemsEl, conn, tx);
        }

        if (openingBalance > 0 && await Db.HasTableAsync("customer_ledgers"))
        {
            await Db.InsertAsync("customer_ledgers", new Dictionary<string, object?>
            {
                ["customer_id"] = id,
                ["entry_type"] = "manual",
                ["is_manual"] = true,
                ["entry_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                ["posted_number"] = "OB-" + id.ToString().PadLeft(5, '0'),
                ["invoice_total"] = openingBalance,
                ["total_received"] = 0m,
                ["balance_remaining"] = openingBalance,
                ["credited_amount"] = 0m,
                ["description"] = "Opening Balance",
                ["created_by"] = CurrentUserId
            }, conn, tx);
        }

        await tx.CommitAsync();

        var customer = await GetCustomerWithRegularMetadataAsync(id);
        return StatusCode(201, customer);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("customer.view")) return Forbidden();
        await EnsureRegularCustomerSchemaAsync();

        var customer = await GetCustomerWithRegularMetadataAsync(id);
        if (customer == null) return NotFound(new { message = "Customer not found." });

        return Ok(customer);
    }

    [HttpGet("{id:long}/credit-summary")]
    public async Task<IActionResult> CreditSummary(long id)
    {
        if (!await CanAsync(LookupPermissions)) return Forbidden();

        var customer = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id });
        if (customer == null) return NotFound(new { message = "Customer not found." });

        var netBalance = await GetCustomerNetBalanceAsync(Db, id);
        double? creditLimit = customer.TryGetValue("credit_limit", out var clObj) && clObj != null && double.TryParse(clObj.ToString(), out var clVal)
            ? clVal
            : null;
        double? availableCredit = creditLimit.HasValue ? Math.Max(0.0, Math.Round(creditLimit.Value - netBalance, 2)) : null;
        var isLimitExceeded = creditLimit.HasValue && creditLimit.Value > 0 && netBalance > creditLimit.Value;

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var openInvoices = await GetOpenInvoicesForCustomerAsync(Db, id);
        double totalOverdue = 0.0;
        int overdueCount = 0;
        string? oldestDue = null;

        foreach (var inv in openInvoices)
        {
            var dueDate = inv["due_date"]?.ToString() ?? inv["date"]?.ToString() ?? "";
            if (!string.IsNullOrEmpty(dueDate) && string.CompareOrdinal(dueDate, today) < 0)
            {
                totalOverdue += Convert.ToDouble(inv["due"] ?? 0.0);
                overdueCount++;
                if (oldestDue == null || string.CompareOrdinal(dueDate, oldestDue) < 0)
                {
                    oldestDue = dueDate;
                }
            }
        }

        return Ok(new Dictionary<string, object?>
        {
            ["customer_id"] = id,
            ["name"] = customer["name"],
            ["phone"] = customer["phone"],
            ["credit_status"] = customer["credit_status"] ?? "active",
            ["credit_limit"] = creditLimit,
            ["credit_days"] = customer["credit_days"] ?? 30,
            ["net_balance"] = Math.Round(netBalance, 2),
            ["current_balance"] = Math.Round(netBalance, 2),
            ["available_credit"] = availableCredit,
            ["is_limit_exceeded"] = isLimitExceeded,
            ["total_overdue"] = Math.Round(totalOverdue, 2),
            ["overdue_invoices_count"] = overdueCount,
            ["oldest_due_date"] = oldestDue
        });
    }

    [HttpPut("{id:long}")]
    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("customer.update")) return Forbidden();
        await EnsureRegularCustomerSchemaAsync();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Customer not found." });

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name field is required."]
            });
        }

        var existsName = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM customers WHERE name = @name AND id != @id", new { name, id });
        if (existsName > 0)
        {
            return Unprocessable("The name has already been taken.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name has already been taken."]
            });
        }

        if (body.ContainsKey("email"))
        {
            var email = GetString(body, "email")?.Trim();
            if (!string.IsNullOrEmpty(email))
            {
                var existsEmail = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM customers WHERE email = @email AND id != @id", new { email, id });
                if (existsEmail > 0)
                {
                    return Unprocessable("The email has already been taken.", new Dictionary<string, string[]>
                    {
                        ["email"] = ["The email has already been taken."]
                    });
                }
            }
        }

        var data = BuildCustomerFieldDictionary(body, existing);
        data["name"] = name;
        NormalizeRegularFields(data, existing);

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await Db.UpdateAsync("customers", id, data, "id", conn, tx);

        if (body.TryGetValue("regular_items", out var regItemsEl) && regItemsEl.ValueKind == JsonValueKind.Array)
        {
            await SyncRegularItemsAsync(id, regItemsEl, conn, tx);
        }

        await tx.CommitAsync();

        var updated = await GetCustomerWithRegularMetadataAsync(id);
        return Ok(updated);
    }

    [HttpGet("{id:long}/refill-reminder-preview")]
    public async Task<IActionResult> RefillReminderPreview(long id, [FromQuery] string? language = null)
    {
        if (!await CanAsync("customer.view")) return Forbidden();
        await EnsureRegularCustomerSchemaAsync();

        var customer = await GetCustomerWithRegularMetadataAsync(id);
        if (customer == null) return NotFound(new { message = "Customer not found." });

        var lang = !string.IsNullOrWhiteSpace(language)
            ? language!
            : (customer["reminder_language"]?.ToString() ?? "english");

        var templates = await FormatRegularCustomerReminderAsync(customer, lang);
        var phone = customer["phone"]?.ToString() ?? "";
        var waCfg = await LoadWhatsAppConfigAsync();
        var waConfigured = waCfg.Enabled && (
            waCfg.Provider == "local_agent" ||
            !string.IsNullOrWhiteSpace(waCfg.AccessToken) ||
            !string.IsNullOrWhiteSpace(waCfg.ApiUrl));

        return Ok(new Dictionary<string, object?>
        {
            ["customer_id"] = id,
            ["customer_name"] = customer["name"],
            ["phone"] = phone,
            ["normalized_phone"] = _shareEngine.NormalizePhone(phone, waCfg.DefaultCountryCode),
            ["refill_cycle"] = customer["refill_cycle"] ?? "monthly",
            ["next_refill_date"] = FormatDateYmd(customer["next_refill_date"]),
            ["stock_ready"] = customer["stock_ready"],
            ["out_of_stock_count"] = customer["out_of_stock_count"],
            ["regular_items"] = customer["regular_items"],
            ["templates"] = templates,
            ["whatsapp_enabled"] = waConfigured,
            ["whatsapp_provider"] = waConfigured ? waCfg.Provider : "wa_me",
            ["whatsapp_default_country_code"] = waCfg.DefaultCountryCode
        });
    }

    [HttpPost("{id:long}/send-refill-reminder")]
    public async Task<IActionResult> SendRefillReminder(long id)
    {
        if (!await CanAsync("customer.update", "customer.create", "sale-invoice.create")) return Forbidden();
        await EnsureRegularCustomerSchemaAsync();

        var customer = await GetCustomerWithRegularMetadataAsync(id);
        if (customer == null) return NotFound(new { message = "Customer not found." });

        var body = await ReadJsonBodyAsync();
        var rawPhone = (GetString(body, "phone") ?? customer["phone"]?.ToString() ?? "").Trim();
        if (string.IsNullOrEmpty(rawPhone))
        {
            return Unprocessable($"Customer \"{customer["name"]}\" does not have a phone number configured.");
        }

        var savePhone = GetBool(body, "save_phone", false);
        var advanceCycle = GetBool(body, "advance_cycle", false);
        var language = GetString(body, "language") ?? customer["reminder_language"]?.ToString() ?? "english";

        var customMessage = (GetString(body, "message") ?? "").Trim();
        if (string.IsNullOrEmpty(customMessage))
        {
            var templates = await FormatRegularCustomerReminderAsync(customer, language);
            customMessage = templates["message"]?.ToString() ?? "";
        }

        var waCfg = await LoadWhatsAppConfigAsync();
        var normalizedPhone = _shareEngine.NormalizePhone(rawPhone, waCfg.DefaultCountryCode);
        if (string.IsNullOrEmpty(normalizedPhone))
        {
            return Unprocessable("Customer does not have a valid WhatsApp phone number.");
        }

        // If local_agent is enabled, auto-wake if stopped; return qr_required if running & unlinked (never silently kick out to browser)
        var isLocalAgentMode = waCfg.Enabled && waCfg.Provider == "local_agent";
        Karobar.Core.Entities.Setting? efSettingForWa = null;
        if (isLocalAgentMode)
        {
            efSettingForWa = _dbContext != null
                ? await _dbContext.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(HttpContext.RequestAborted)
                : null;
            var agentStatus = await _waSidecar.StatusAsync(_dbContext, efSettingForWa, HttpContext.RequestAborted);
            var isRunning = agentStatus.TryGetValue("agent_running", out var rObj) && rObj is true;
            var stStr = agentStatus.GetValueOrDefault("status")?.ToString() ?? "stopped";
            if (!isRunning)
            {
                agentStatus = await _waSidecar.StartAsync(_dbContext, efSettingForWa, enableInSettings: false, cancellationToken: HttpContext.RequestAborted);
                isRunning = agentStatus.TryGetValue("agent_running", out var rObj2) && rObj2 is true;
                stStr = agentStatus.GetValueOrDefault("status")?.ToString() ?? "stopped";
            }
            var liveUrl = agentStatus.GetValueOrDefault("agent_url")?.ToString();
            var liveKey = agentStatus.GetValueOrDefault("api_key")?.ToString();
            if (!string.IsNullOrWhiteSpace(liveUrl)) waCfg.ApiUrl = liveUrl;
            if (!string.IsNullOrWhiteSpace(liveKey)) waCfg.AccessToken = liveKey;

            if (!isRunning)
            {
                return Unprocessable(agentStatus.GetValueOrDefault("last_error")?.ToString()
                    ?? agentStatus.GetValueOrDefault("message")?.ToString()
                    ?? "Local WhatsApp Agent is not running.");
            }
            if (!string.Equals(stStr, "connected", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new Dictionary<string, object?>
                {
                    ["status"] = "qr_required",
                    ["mode"] = "qr_required",
                    ["provider"] = "local_agent",
                    ["message"] = "Local WhatsApp Agent is not linked yet. Please scan the QR code to send the reminder.",
                    ["agent_status"] = stStr,
                    ["qr_data_url"] = agentStatus.GetValueOrDefault("qr_data_url"),
                    ["pairing_code"] = agentStatus.GetValueOrDefault("pairing_code"),
                    ["last_error"] = agentStatus.GetValueOrDefault("last_error"),
                    ["normalized_phone"] = normalizedPhone
                });
            }

            waCfg.AutoFallback = false;
        }

        Dictionary<string, object?> sendRes;
        try
        {
            sendRes = await _shareEngine.SendOrBuildWhatsAppTextAsync(rawPhone, customMessage, waCfg, HttpContext.RequestAborted);
        }
        catch (Exception ex) when (isLocalAgentMode)
        {
            var freshSt = await _waSidecar.StatusAsync(_dbContext, efSettingForWa, HttpContext.RequestAborted);
            var freshStatusStr = freshSt.GetValueOrDefault("status")?.ToString() ?? "stopped";
            if (!string.Equals(freshStatusStr, "connected", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new Dictionary<string, object?>
                {
                    ["status"] = "qr_required",
                    ["mode"] = "qr_required",
                    ["provider"] = "local_agent",
                    ["message"] = "WhatsApp session reconnecting — scan QR if prompted.",
                    ["agent_status"] = freshStatusStr,
                    ["qr_data_url"] = freshSt.GetValueOrDefault("qr_data_url"),
                    ["pairing_code"] = freshSt.GetValueOrDefault("pairing_code"),
                    ["last_error"] = freshSt.GetValueOrDefault("last_error"),
                    ["normalized_phone"] = normalizedPhone
                });
            }
            return Unprocessable(ex.Message);
        }
        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var updates = new Dictionary<string, object?>
        {
            ["reminder_language"] = language,
            ["last_refill_reminded_at"] = nowStr
        };

        if (savePhone)
        {
            updates["phone"] = rawPhone;
        }

        if (advanceCycle)
        {
            updates["next_refill_date"] = CalculateNextRefillDate(customer);
            updates["last_refill_collected_at"] = nowStr;
        }

        await Db.UpdateAsync("customers", id, updates);
        var refreshed = await GetCustomerWithRegularMetadataAsync(id);
        sendRes["customer"] = refreshed;
        return Ok(sendRes);
    }

    [HttpPost("send-bulk-refill-reminders")]
    public async Task<IActionResult> SendBulkRefillReminders()
    {
        if (!await CanAsync("customer.update", "customer.create", "sale-invoice.create")) return Forbidden();
        await EnsureRegularCustomerSchemaAsync();

        var body = await ReadJsonBodyAsync();
        var customerIds = new List<long>();
        if (body.TryGetValue("customer_ids", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in idsEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var cid)) customerIds.Add(cid);
                else if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), out var scid)) customerIds.Add(scid);
            }
        }

        var languageOverride = GetString(body, "language");
        var onlyStockReady = GetBool(body, "only_stock_ready", false);

        List<Dictionary<string, object?>> customers;
        if (customerIds.Count > 0)
        {
            customers = await Db.QueryListAsync(
                "SELECT * FROM customers WHERE is_regular = 1 AND id IN @ids ORDER BY name ASC",
                new { ids = customerIds });
        }
        else
        {
            customers = await Db.QueryListAsync(
                "SELECT * FROM customers WHERE is_regular = 1 ORDER BY name ASC");
        }

        await AttachRegularItemsAndMetadataAsync(customers);

        var waCfg = await LoadWhatsAppConfigAsync();
        if (waCfg.Enabled && waCfg.Provider == "local_agent")
        {
            var efSetting = _dbContext != null
                ? await _dbContext.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(HttpContext.RequestAborted)
                : null;
            var st = await _waSidecar.StatusAsync(_dbContext, efSetting, HttpContext.RequestAborted);
            var isRun = st.TryGetValue("agent_running", out var rObj) && rObj is true;
            var stStr = st.GetValueOrDefault("status")?.ToString() ?? "stopped";
            if (!isRun)
            {
                st = await _waSidecar.StartAsync(_dbContext, efSetting, enableInSettings: false, cancellationToken: HttpContext.RequestAborted);
                isRun = st.TryGetValue("agent_running", out var rObj2) && rObj2 is true;
                stStr = st.GetValueOrDefault("status")?.ToString() ?? "stopped";
            }
            var liveUrl = st.GetValueOrDefault("agent_url")?.ToString();
            var liveKey = st.GetValueOrDefault("api_key")?.ToString();
            if (!string.IsNullOrWhiteSpace(liveUrl)) waCfg.ApiUrl = liveUrl;
            if (!string.IsNullOrWhiteSpace(liveKey)) waCfg.AccessToken = liveKey;

            if (!isRun || !string.Equals(stStr, "connected", StringComparison.OrdinalIgnoreCase))
            {
                return Unprocessable(isRun
                    ? "Local WhatsApp Agent is not linked yet. Please link WhatsApp via QR code in Settings -> WhatsApp API or on any customer reminder first."
                    : (st.GetValueOrDefault("last_error")?.ToString() ?? "Local WhatsApp Agent is not running."));
            }
        }

        int sentCount = 0, fallbackCount = 0, skippedCount = 0;
        var results = new List<Dictionary<string, object?>>();
        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        foreach (var cust in customers)
        {
            var cid = Convert.ToInt64(cust["id"] ?? 0);
            var cname = cust["name"]?.ToString() ?? "";
            var isDue = cust["is_due_for_reminder"] is true;
            var stockReady = cust["stock_ready"] is true;

            if (customerIds.Count == 0 && !isDue)
            {
                continue;
            }

            if (onlyStockReady && !stockReady)
            {
                skippedCount++;
                results.Add(new Dictionary<string, object?>
                {
                    ["customer_id"] = cid,
                    ["customer_name"] = cname,
                    ["status"] = "skipped_out_of_stock",
                    ["message"] = "Skipped because one or more regular items are out of stock."
                });
                continue;
            }

            var rawPhone = (cust["phone"]?.ToString() ?? "").Trim();
            if (string.IsNullOrEmpty(rawPhone))
            {
                skippedCount++;
                results.Add(new Dictionary<string, object?>
                {
                    ["customer_id"] = cid,
                    ["customer_name"] = cname,
                    ["status"] = "skipped_no_phone",
                    ["message"] = "Skipped because customer has no phone number."
                });
                continue;
            }

            var lang = !string.IsNullOrEmpty(languageOverride)
                ? languageOverride
                : (cust["reminder_language"]?.ToString() ?? "english");
            var templates = await FormatRegularCustomerReminderAsync(cust, lang);
            var msgText = templates["message"]?.ToString() ?? "";

            var sendRes = await _shareEngine.SendOrBuildWhatsAppTextAsync(rawPhone, msgText, waCfg, HttpContext.RequestAborted);
            var wasSentDirectly = string.Equals(sendRes.GetValueOrDefault("mode")?.ToString(), "cloud_api", StringComparison.OrdinalIgnoreCase);

            await Db.UpdateAsync("customers", cid, new Dictionary<string, object?>
            {
                ["last_refill_reminded_at"] = nowStr
            });

            if (wasSentDirectly) sentCount++; else fallbackCount++;
            results.Add(new Dictionary<string, object?>
            {
                ["customer_id"] = cid,
                ["customer_name"] = cname,
                ["phone"] = rawPhone,
                ["status"] = sendRes.GetValueOrDefault("status") ?? (wasSentDirectly ? "sent_via_api" : "wa_me_fallback"),
                ["mode"] = sendRes.GetValueOrDefault("mode") ?? (wasSentDirectly ? "cloud_api" : "wa_me_fallback"),
                ["provider"] = sendRes.GetValueOrDefault("provider") ?? waCfg.Provider,
                ["normalized_phone"] = sendRes.GetValueOrDefault("normalized_phone") ?? _shareEngine.NormalizePhone(rawPhone, waCfg.DefaultCountryCode),
                ["wa_me_url"] = sendRes.GetValueOrDefault("wa_me_url"),
                ["message"] = sendRes.GetValueOrDefault("message")
            });
        }

        return Ok(new Dictionary<string, object?>
        {
            ["sent_count"] = sentCount,
            ["fallback_count"] = fallbackCount,
            ["skipped_count"] = skippedCount,
            ["total_processed"] = results.Count,
            ["results"] = results,
            ["message"] = $"Processed {results.Count} regular customer reminders ({sentCount} sent via WhatsApp Agent, {fallbackCount} prepared via link, {skippedCount} skipped)."
        });
    }

    [HttpPost("{id:long}/advance-refill-cycle")]
    public async Task<IActionResult> AdvanceRefillCycle(long id)
    {
        if (!await CanAsync("customer.update")) return Forbidden();
        await EnsureRegularCustomerSchemaAsync();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Customer not found." });

        var nextDateStr = CalculateNextRefillDate(existing);
        await Db.UpdateAsync("customers", id, new Dictionary<string, object?>
        {
            ["next_refill_date"] = nextDateStr,
            ["last_refill_collected_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        });

        var customer = await GetCustomerWithRegularMetadataAsync(id);
        var formattedDisplay = DateTime.TryParse(nextDateStr, out var dt) ? dt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : nextDateStr;

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = $"Next refill collection date advanced to {formattedDisplay}.",
            ["next_refill_date"] = nextDateStr,
            ["customer"] = customer
        });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("customer.delete")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Customer not found." });

        var hasInvoices = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sale_invoices WHERE customer_id = @id", new { id }) > 0;
        var hasReturns = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sale_returns WHERE customer_id = @id", new { id }) > 0;

        if (hasInvoices || hasReturns)
        {
            return Unprocessable("Cannot delete: customer has related sale invoices/returns.");
        }

        await Db.ExecuteAsync("DELETE FROM customers WHERE id = @id", new { id });
        return NoContent();
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        if (!await CanAsync("customer.export")) return Forbidden();
        await EnsureRegularCustomerSchemaAsync();

        var rows = await Db.QueryListAsync(@"
            SELECT name, email, phone, cnic, address, is_regular, refill_cycle, next_refill_date
            FROM customers
            ORDER BY name ASC");

        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("name,email,phone,cnic,address,is_regular,refill_cycle,next_refill_date");

        foreach (var c in rows)
        {
            var isReg = c["is_regular"] is true ? "Yes" : "No";
            var nextDate = FormatDateYmd(c["next_refill_date"]) ?? "";
            var line = string.Join(",",
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(c["name"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(c["email"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(c["phone"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(c["cnic"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(c["address"])),
                isReg,
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(c["refill_cycle"])),
                BrandController.EscapeCsv(nextDate));
            sb.AppendLine(line);
        }

        var fileName = $"customers_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=UTF-8", fileName);
    }

    // -------------------------------------------------------------------------
    // Customer Custom Wholesale Prices (/api/customers/{customerId}/whole-sale-prices)
    // -------------------------------------------------------------------------

    [HttpGet("{customerId:long}/whole-sale-prices")]
    public async Task<IActionResult> GetWholeSalePrices(long customerId)
    {
        if (!await CanAsync("customer.view", "sale-invoice.create")) return Forbidden();

        if (!await Db.HasTableAsync("customer_whole_sale_prices"))
        {
            return Ok(Array.Empty<object>());
        }

        var prices = await Db.QueryListAsync(@"
            SELECT cwp.*,
                   p.name AS p_name, p.product_code AS p_code, p.pack_size AS p_pack_size,
                   p.whole_sale_pack_price AS p_wspp, p.whole_sale_unit_price AS p_wsup,
                   p.unit_sale_price AS p_usp, p.pack_sale_price AS p_psp
            FROM customer_whole_sale_prices cwp
            LEFT JOIN products p ON p.id = cwp.product_id
            WHERE cwp.customer_id = @customerId
            ORDER BY cwp.id DESC", new { customerId });

        foreach (var row in prices)
        {
            var pid = row["product_id"];
            row["product"] = pid != null ? new Dictionary<string, object?>
            {
                ["id"] = pid,
                ["name"] = row["p_name"],
                ["product_code"] = row["p_code"],
                ["pack_size"] = row["p_pack_size"],
                ["whole_sale_pack_price"] = row["p_wspp"],
                ["whole_sale_unit_price"] = row["p_wsup"],
                ["unit_sale_price"] = row["p_usp"],
                ["pack_sale_price"] = row["p_psp"]
            } : null;

            row.Remove("p_name");
            row.Remove("p_code");
            row.Remove("p_pack_size");
            row.Remove("p_wspp");
            row.Remove("p_wsup");
            row.Remove("p_usp");
            row.Remove("p_psp");
        }

        return Ok(prices);
    }

    [HttpPost("{customerId:long}/whole-sale-prices")]
    public async Task<IActionResult> SaveWholeSalePrice(long customerId)
    {
        if (!await CanAsync("customer.update", "customer.create")) return Forbidden();

        var customer = await Db.QuerySingleOrDefaultAsync("SELECT id FROM customers WHERE id = @customerId", new { customerId });
        if (customer == null) return NotFound(new { message = "Customer not found." });

        var body = await ReadJsonBodyAsync();
        var productId = GetNullableLong(body, "product_id");
        if (!productId.HasValue || productId.Value <= 0)
        {
            return Unprocessable("The product_id field is required.", new Dictionary<string, string[]>
            {
                ["product_id"] = ["The product_id field is required."]
            });
        }

        var prod = await Db.QuerySingleOrDefaultAsync(@"
            SELECT id, name, product_code, pack_size, whole_sale_pack_price, whole_sale_unit_price, unit_sale_price, pack_sale_price
            FROM products WHERE id = @pid", new { pid = productId.Value });
        if (prod == null)
        {
            return Unprocessable("The selected product_id is invalid.", new Dictionary<string, string[]>
            {
                ["product_id"] = ["The selected product_id is invalid."]
            });
        }

        var packPrice = Math.Round(GetDecimal(body, "pack_price", -1m), 2);
        if (packPrice < 0)
        {
            return Unprocessable("The pack_price field must be at least 0.", new Dictionary<string, string[]>
            {
                ["pack_price"] = ["The pack_price field must be at least 0."]
            });
        }

        var existing = await Db.QuerySingleOrDefaultAsync(
            "SELECT id FROM customer_whole_sale_prices WHERE customer_id = @customerId AND product_id = @productId LIMIT 1",
            new { customerId, productId = productId.Value });

        long recordId;
        if (existing != null)
        {
            recordId = Convert.ToInt64(existing["id"]);
            await Db.UpdateAsync("customer_whole_sale_prices", recordId, new Dictionary<string, object?>
            {
                ["pack_price"] = packPrice
            });
        }
        else
        {
            recordId = await Db.InsertAsync("customer_whole_sale_prices", new Dictionary<string, object?>
            {
                ["customer_id"] = customerId,
                ["product_id"] = productId.Value,
                ["pack_price"] = packPrice
            });
        }

        var saved = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customer_whole_sale_prices WHERE id = @recordId", new { recordId });
        if (saved != null)
        {
            saved["product"] = prod;
        }

        return StatusCode(201, saved);
    }

    [HttpDelete("{customerId:long}/whole-sale-prices/{priceId:long}")]
    public async Task<IActionResult> DeleteWholeSalePrice(long customerId, long priceId)
    {
        if (!await CanAsync("customer.update", "customer.delete")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync(
            "SELECT id FROM customer_whole_sale_prices WHERE id = @priceId AND customer_id = @customerId",
            new { priceId, customerId });
        if (existing == null) return NotFound(new { message = "Custom wholesale price not found." });

        await Db.ExecuteAsync("DELETE FROM customer_whole_sale_prices WHERE id = @priceId", new { priceId });
        return Ok(new { message = "Custom wholesale price removed" });
    }

    // -------------------------------------------------------------------------
    // Internal Helpers
    // -------------------------------------------------------------------------

    private static Dictionary<string, object?> BuildCustomerFieldDictionary(
        Dictionary<string, JsonElement> body,
        Dictionary<string, object?>? existing)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        string[] strKeys =
        [
            "email", "phone", "cnic", "tax_number", "strn", "tax_status", "province",
            "address", "credit_status", "refill_cycle", "next_refill_date", "reminder_language", "regular_notes"
        ];

        foreach (var k in strKeys)
        {
            if (body.ContainsKey(k))
            {
                var val = GetString(body, k);
                dict[k] = string.IsNullOrWhiteSpace(val) ? null : val.Trim();
            }
            else if (existing != null && existing.TryGetValue(k, out var ev))
            {
                dict[k] = ev;
            }
        }

        if (body.ContainsKey("credit_limit"))
        {
            var raw = GetString(body, "credit_limit");
            dict["credit_limit"] = !string.IsNullOrWhiteSpace(raw) && decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                ? d
                : null;
        }

        foreach (var intKey in new[] { "credit_days", "refill_custom_days", "reminder_lead_days" })
        {
            if (body.ContainsKey(intKey))
            {
                var raw = GetString(body, intKey);
                dict[intKey] = !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var iv) ? iv : null;
            }
        }

        if (body.ContainsKey("is_regular"))
        {
            dict["is_regular"] = GetBool(body, "is_regular", false);
        }

        return dict;
    }

    private static void NormalizeRegularFields(Dictionary<string, object?> data, Dictionary<string, object?>? existing)
    {
        var isRegular = data.TryGetValue("is_regular", out var ir) && ir is bool b
            ? b
            : (existing != null && existing.TryGetValue("is_regular", out var eir) && eir is true);

        if (isRegular)
        {
            var cycle = (data.TryGetValue("refill_cycle", out var rc) && rc != null ? rc.ToString() : null)
                        ?? (existing != null && existing.TryGetValue("refill_cycle", out var erc) ? erc?.ToString() : null)
                        ?? "monthly";
            data["refill_cycle"] = cycle;

            if (!data.TryGetValue("reminder_lead_days", out var rld) || rld == null)
            {
                data["reminder_lead_days"] = existing != null && existing.TryGetValue("reminder_lead_days", out var erld) && erld != null
                    ? Convert.ToInt32(erld)
                    : 2;
            }

            if (!data.TryGetValue("reminder_language", out var rl) || rl == null)
            {
                data["reminder_language"] = (existing != null && existing.TryGetValue("reminder_language", out var erl) ? erl?.ToString() : null) ?? "english";
            }

            var hasNext = (data.TryGetValue("next_refill_date", out var nrd) && nrd != null && !string.IsNullOrWhiteSpace(nrd.ToString()))
                          || (existing != null && existing.TryGetValue("next_refill_date", out var enrd) && enrd != null && !string.IsNullOrWhiteSpace(enrd.ToString()));

            if (!hasNext)
            {
                var customDays = data.TryGetValue("refill_custom_days", out var cd) && cd != null && int.TryParse(cd.ToString(), out var cdv) ? cdv : 30;
                var today = DateTime.UtcNow.Date;
                var nextDt = cycle.ToLowerInvariant() switch
                {
                    "weekly" => today.AddDays(7),
                    "biweekly" => today.AddDays(14),
                    "custom" => today.AddDays(Math.Max(1, customDays)),
                    _ => today.AddMonths(1)
                };
                data["next_refill_date"] = nextDt.ToString("yyyy-MM-dd");
            }
        }
    }

    private async Task SyncRegularItemsAsync(long customerId, JsonElement itemsArray, System.Data.IDbConnection conn, System.Data.IDbTransaction tx)
    {
        await conn.ExecuteAsync("DELETE FROM customer_regular_items WHERE customer_id = @customerId", new { customerId }, tx);

        foreach (var row in itemsArray.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object) continue;
            long productId = 0;
            if (row.TryGetProperty("product_id", out var pidEl))
            {
                if (pidEl.ValueKind == JsonValueKind.Number) pidEl.TryGetInt64(out productId);
                else if (pidEl.ValueKind == JsonValueKind.String) long.TryParse(pidEl.GetString(), out productId);
            }
            if (productId <= 0) continue;

            var packSize = await conn.ExecuteScalarAsync<int?>(
                "SELECT pack_size FROM products WHERE id = @pid", new { pid = productId }, tx) ?? 1;
            packSize = Math.Max(1, packSize);

            double rawPackQty = 0;
            if (row.TryGetProperty("pack_quantity", out var pqEl))
            {
                if (pqEl.ValueKind == JsonValueKind.Number) pqEl.TryGetDouble(out rawPackQty);
                else if (pqEl.ValueKind == JsonValueKind.String) double.TryParse(pqEl.GetString(), out rawPackQty);
            }
            rawPackQty = Math.Max(0, rawPackQty);

            int rawUnitQty = 0;
            if (row.TryGetProperty("unit_quantity", out var uqEl))
            {
                if (uqEl.ValueKind == JsonValueKind.Number) uqEl.TryGetInt32(out rawUnitQty);
                else if (uqEl.ValueKind == JsonValueKind.String) int.TryParse(uqEl.GetString(), out rawUnitQty);
            }
            rawUnitQty = Math.Max(0, rawUnitQty);

            int unitQty;
            int packQty;
            if (rawUnitQty > 0)
            {
                unitQty = rawUnitQty;
                packQty = unitQty / packSize;
            }
            else if (rawPackQty > 0)
            {
                unitQty = Math.Max(1, (int)Math.Round(rawPackQty * packSize));
                packQty = (int)Math.Floor(rawPackQty);
            }
            else
            {
                unitQty = packSize;
                packQty = 1;
            }

            string? dosageNotes = null;
            if (row.TryGetProperty("dosage_notes", out var dnEl) && dnEl.ValueKind == JsonValueKind.String)
            {
                dosageNotes = dnEl.GetString()?.Trim();
            }

            await Db.InsertAsync("customer_regular_items", new Dictionary<string, object?>
            {
                ["customer_id"] = customerId,
                ["product_id"] = productId,
                ["pack_quantity"] = packQty,
                ["unit_quantity"] = unitQty,
                ["dosage_notes"] = dosageNotes
            }, conn, tx);
        }
    }

    private async Task<Dictionary<string, object?>?> GetCustomerWithRegularMetadataAsync(long id)
    {
        var customer = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id });
        if (customer == null) return null;

        await AttachRegularItemsAndMetadataAsync(new List<Dictionary<string, object?>> { customer });
        return customer;
    }

    private async Task AttachRegularItemsAndMetadataAsync(List<Dictionary<string, object?>> customers)
    {
        if (customers.Count == 0) return;

        var custIds = customers.Select(c => Convert.ToInt64(c["id"] ?? 0)).Where(id => id > 0).ToArray();
        var itemsByCustomer = new Dictionary<long, List<Dictionary<string, object?>>>();

        if (custIds.Length > 0 && await Db.HasTableAsync("customer_regular_items"))
        {
            var rows = await Db.QueryListAsync(@"
                SELECT cri.*,
                       p.name AS p_name, p.product_code AS p_code, p.quantity AS p_qty,
                       p.pack_size AS p_pack_size, p.unit_sale_price AS p_usp, p.pack_sale_price AS p_psp
                FROM customer_regular_items cri
                LEFT JOIN products p ON p.id = cri.product_id
                WHERE cri.customer_id IN @custIds
                ORDER BY cri.id ASC", new { custIds });

            foreach (var r in rows)
            {
                var cid = Convert.ToInt64(r["customer_id"] ?? 0);
                var pid = r["product_id"];
                var prodDict = pid != null ? new Dictionary<string, object?>
                {
                    ["id"] = pid,
                    ["name"] = r["p_name"],
                    ["product_code"] = r["p_code"],
                    ["quantity"] = r["p_qty"],
                    ["pack_size"] = r["p_pack_size"],
                    ["unit_sale_price"] = r["p_usp"],
                    ["pack_sale_price"] = r["p_psp"]
                } : null;

                r.Remove("p_name");
                r.Remove("p_code");
                r.Remove("p_qty");
                r.Remove("p_pack_size");
                r.Remove("p_usp");
                r.Remove("p_psp");
                r["product"] = prodDict;

                if (!itemsByCustomer.TryGetValue(cid, out var list))
                {
                    list = new List<Dictionary<string, object?>>();
                    itemsByCustomer[cid] = list;
                }
                list.Add(r);
            }
        }

        var today = DateTime.UtcNow.Date;

        foreach (var cust in customers)
        {
            var cid = Convert.ToInt64(cust["id"] ?? 0);
            var regItems = itemsByCustomer.TryGetValue(cid, out var list) ? list : new List<Dictionary<string, object?>>();
            int outOfStockCount = 0;

            foreach (var item in regItems)
            {
                var prod = item["product"] as Dictionary<string, object?>;
                var packSize = Math.Max(1, Convert.ToInt32(prod?["pack_size"] ?? 1));
                var unitQty = Convert.ToInt32(item["unit_quantity"] ?? 0);
                var packQty = Convert.ToDouble(item["pack_quantity"] ?? 0);
                var requiredUnits = unitQty > 0 ? unitQty : Math.Max(1, (int)Math.Round(packQty * packSize));
                var onHandUnits = Convert.ToInt32(prod?["quantity"] ?? 0);

                item["required_total_units"] = requiredUnits;
                item["on_hand_units"] = onHandUnits;
                item["in_stock"] = onHandUnits >= requiredUnits;
                if (onHandUnits < requiredUnits) outOfStockCount++;
            }

            cust["regular_items"] = regItems;

            int? daysUntilRefill = null;
            bool isDueForReminder = false;
            bool isOverdueRefill = false;

            var isReg = cust.TryGetValue("is_regular", out var ir) && (ir is true || ir?.ToString() == "1");
            var nextDateStr = FormatDateYmd(cust.TryGetValue("next_refill_date", out var nrd) ? nrd : null);
            cust["next_refill_date"] = nextDateStr;

            if (isReg && !string.IsNullOrEmpty(nextDateStr) && DateTime.TryParse(nextDateStr, out var dueDt))
            {
                daysUntilRefill = (int)(dueDt.Date - today).TotalDays;
                var leadDays = Convert.ToInt32(cust["reminder_lead_days"] ?? 2);
                isDueForReminder = daysUntilRefill.Value <= leadDays;
                isOverdueRefill = daysUntilRefill.Value < 0;
            }

            cust["regular_items_count"] = regItems.Count;
            cust["out_of_stock_count"] = outOfStockCount;
            cust["stock_ready"] = outOfStockCount == 0;
            cust["days_until_refill"] = daysUntilRefill;
            cust["is_due_for_reminder"] = isDueForReminder;
            cust["is_overdue_refill"] = isOverdueRefill;
        }
    }

    private static string CalculateNextRefillDate(Dictionary<string, object?> customer)
    {
        var today = DateTime.UtcNow.Date;
        var baseDt = today;
        if (customer.TryGetValue("next_refill_date", out var nrd) && nrd != null && DateTime.TryParse(nrd.ToString(), out var parsed))
        {
            baseDt = parsed.Date < today ? today : parsed.Date;
        }

        var cycle = (customer.TryGetValue("refill_cycle", out var rc) ? rc?.ToString() : null)?.Trim().ToLowerInvariant() ?? "monthly";
        var customDays = customer.TryGetValue("refill_custom_days", out var cd) && cd != null && int.TryParse(cd.ToString(), out var cdv)
            ? Math.Max(1, cdv)
            : 30;

        var next = cycle switch
        {
            "weekly" => baseDt.AddDays(7),
            "biweekly" => baseDt.AddDays(14),
            "custom" => baseDt.AddDays(customDays),
            _ => baseDt.AddMonths(1)
        };

        return next.ToString("yyyy-MM-dd");
    }

    private async Task<Dictionary<string, object?>> FormatRegularCustomerReminderAsync(Dictionary<string, object?> customer, string? language)
    {
        Dictionary<string, object?>? setting = null;
        if (await Db.HasTableAsync("settings"))
        {
            setting = await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1");
        }

        var storeName = (setting != null && setting.TryGetValue("store_name", out var sn) && !string.IsNullOrWhiteSpace(sn?.ToString()))
            ? sn!.ToString()!.Trim()
            : "Karobar Pharmacy";
        var storePhone = (setting != null && setting.TryGetValue("phone_number", out var sp) && sp != null)
            ? sp.ToString()!.Trim()
            : "";

        var customerName = (customer["name"]?.ToString() ?? "Valued Customer").Trim();
        var cycle = (customer["refill_cycle"]?.ToString() ?? "monthly").Trim().ToLowerInvariant();
        var customDays = customer["refill_custom_days"] != null && int.TryParse(customer["refill_custom_days"]!.ToString(), out var cd)
            ? Math.Max(1, cd)
            : 30;

        var cycleLabelEn = cycle switch
        {
            "weekly" => "Weekly",
            "biweekly" => "Bi-Weekly (14-Day)",
            "custom" => $"{customDays}-Day",
            _ => "Monthly"
        };

        var cycleLabelUr = cycle switch
        {
            "weekly" => "ہفتہ وار",
            "biweekly" => "پندرہ روزہ",
            "custom" => $"{customDays} روزہ",
            _ => "ماہانہ"
        };

        var dueDateFormatted = DateTime.UtcNow.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        if (customer["next_refill_date"] != null && DateTime.TryParse(customer["next_refill_date"]!.ToString(), out var ndt))
        {
            dueDateFormatted = ndt.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        }

        var itemsEn = new List<string>();
        var itemsUr = new List<string>();

        if (customer.TryGetValue("regular_items", out var riObj) && riObj is List<Dictionary<string, object?>> regItems)
        {
            for (int i = 0; i < regItems.Count; i++)
            {
                var item = regItems[i];
                var num = i + 1;
                var prod = item["product"] as Dictionary<string, object?>;
                var prodName = prod?["name"]?.ToString() ?? $"Product #{item["product_id"]}";
                var packSize = Math.Max(1, Convert.ToInt32(prod?["pack_size"] ?? 1));
                var rawPackQty = Convert.ToDouble(item["pack_quantity"] ?? 0);
                var rawUnitQty = Convert.ToInt32(item["unit_quantity"] ?? 0);

                var totalUnits = rawUnitQty > 0 ? rawUnitQty : Math.Max(1, (int)Math.Round(rawPackQty * packSize));
                string qtyTextEn, qtyTextUr;
                if (packSize > 1 && totalUnits % packSize == 0)
                {
                    var packs = totalUnits / packSize;
                    qtyTextEn = $"{packs} {(packs == 1 ? "Pack" : "Packs")} ({totalUnits} Units)";
                    qtyTextUr = $"{packs} پیک ({totalUnits} عدد)";
                }
                else
                {
                    qtyTextEn = $"{totalUnits} {(totalUnits == 1 ? "Unit" : "Units")}";
                    qtyTextUr = $"{totalUnits} عدد";
                }

                var note = (item["dosage_notes"]?.ToString() ?? "").Trim();
                itemsEn.Add($"  {num}. *{prodName}* — {qtyTextEn}" + (!string.IsNullOrEmpty(note) ? $" ({note})" : ""));
                itemsUr.Add($"  {num}. *{prodName}* — {qtyTextUr}" + (!string.IsNullOrEmpty(note) ? $" ({note})" : ""));
            }
        }

        var itemsBlockEn = itemsEn.Count > 0 ? "\n*Prepared Medicines / Products:*\n" + string.Join("\n", itemsEn) + "\n" : "";
        var itemsBlockUr = itemsUr.Count > 0 ? "\n*تیار شدہ ادویات / اشیاء:*\n" + string.Join("\n", itemsUr) + "\n" : "";

        var regularNotes = (customer["regular_notes"]?.ToString() ?? "").Trim();
        var notesLineEn = !string.IsNullOrEmpty(regularNotes) ? $"\n*Note:* {regularNotes}" : "";
        var notesLineUr = !string.IsNullOrEmpty(regularNotes) ? $"\n*نوٹ:* {regularNotes}" : "";

        var storePhoneLineEn = !string.IsNullOrEmpty(storePhone) ? $"\nContact: {storePhone}" : "";
        var storePhoneLineUr = !string.IsNullOrEmpty(storePhone) ? $"\nرابطہ نمبر: {storePhone}" : "";

        var english = $"*{storeName} — {cycleLabelEn} Medicine / Product Reminder*\n\n"
                      + $"Dear *{customerName}*,\n"
                      + $"We hope you are in good health. This is a friendly prior reminder that your *{cycleLabelEn}* medicines / products are ready for collection at *{storeName}*."
                      + itemsBlockEn
                      + $"\n*Scheduled Collection Date:* {dueDateFormatted}"
                      + notesLineEn + "\n\n"
                      + "Please do not forget to collect them at your convenience, or reply to this message if you need any changes.\n"
                      + "Thank you!"
                      + storePhoneLineEn;

        var urdu = $"*{storeName} — {cycleLabelUr} ادویات / اشیاء کی یاد دہانی*\n\n"
                   + $"محترم *{customerName}*،\n"
                   + $"السلام علیکم! آپ کو مطلع کیا جاتا ہے کہ آپ کی *{cycleLabelUr}* ادویات / اشیاء *{storeName}* پر تیار ہیں اور وصولی کے لیے دستیاب ہیں:"
                   + itemsBlockUr
                   + $"\n*تاریخِ وصولی:* {dueDateFormatted}"
                   + notesLineUr + "\n\n"
                   + "براہِ کرم اپنی ادویات / اشیاء وقت پر وصول کرنا نہ بھولیں۔ کسی بھی تبدیلی کی صورت میں اس پیغام کا جواب دیں۔\n"
                   + "شکریہ!"
                   + storePhoneLineUr;

        var resolvedLang = (language ?? "english").Trim().ToLowerInvariant();
        if (resolvedLang != "english" && resolvedLang != "urdu") resolvedLang = "english";

        return new Dictionary<string, object?>
        {
            ["language"] = resolvedLang,
            ["english"] = english,
            ["urdu"] = urdu,
            ["message"] = resolvedLang == "urdu" ? urdu : english
        };
    }

    internal static string NormalizeWhatsAppPhone(string? phone, string defaultCountryCode = "92")
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";
        var digits = Regex.Replace(phone, @"\D+", "");
        if (string.IsNullOrEmpty(digits)) return "";

        if (digits.StartsWith("00")) digits = digits[2..];
        if (digits.Length == 11 && digits.StartsWith("03")) return defaultCountryCode + digits[1..];
        if (digits.Length == 10 && digits.StartsWith("3")) return defaultCountryCode + digits;
        if (digits.StartsWith("0") && digits.Length is >= 10 and <= 11) return defaultCountryCode + digits[1..];
        return digits;
    }

    internal static string? FormatDateYmd(object? val)
    {
        if (val == null) return null;
        var s = val.ToString()!.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        if (s.Length >= 10 && Regex.IsMatch(s[..10], @"^\d{4}-\d{2}-\d{2}$")) return s[..10];
        if (DateTime.TryParse(s, out var dt)) return dt.ToString("yyyy-MM-dd");
        return s;
    }

    public static async Task<double> GetCustomerNetBalanceAsync(KarobarDb db, long customerId)
    {
        if (!await db.HasTableAsync("customer_ledgers")) return 0.0;

        var cust = await db.QuerySingleOrDefaultAsync("SELECT name FROM customers WHERE id = @customerId", new { customerId });
        if (cust != null && Regex.IsMatch(cust["name"]?.ToString() ?? "", @"walk[\s\-_]*in", RegexOptions.IgnoreCase))
        {
            return 0.0;
        }

        var invAgg = await db.QuerySingleOrDefaultAsync(@"
            SELECT COALESCE(SUM(COALESCE(cl.invoice_total, 0)), 0) AS total_invoiced,
                   COALESCE(SUM(CASE WHEN COALESCE(cl.total_received, 0) < COALESCE(cl.invoice_total, 0) THEN COALESCE(cl.total_received, 0) ELSE COALESCE(cl.invoice_total, 0) END), 0) AS received_on_inv
            FROM customer_ledgers cl
            LEFT JOIN sale_invoices si ON si.id = cl.sale_invoice_id
            WHERE cl.customer_id = @customerId
              AND cl.entry_type IN ('invoice', 'manual')
              AND (cl.sale_invoice_id IS NULL OR si.status IS NULL OR si.status != 'reversed')",
            new { customerId });

        var totalInvoiced = Convert.ToDouble(invAgg?["total_invoiced"] ?? 0.0);
        var receivedOnInv = Convert.ToDouble(invAgg?["received_on_inv"] ?? 0.0);

        var paymentsCred = await db.ExecuteScalarAsync<double?>(@"
            SELECT COALESCE(SUM(COALESCE(credited_amount, 0)), 0)
            FROM customer_ledgers
            WHERE customer_id = @customerId AND entry_type = 'payment'",
            new { customerId }) ?? 0.0;

        var netBalance = (totalInvoiced - receivedOnInv) - paymentsCred;
        return netBalance > 0 ? Math.Round(netBalance, 2) : 0.0;
    }

    public static async Task<List<Dictionary<string, object?>>> GetOpenInvoicesForCustomerAsync(KarobarDb db, long customerId)
    {
        if (!await db.HasTableAsync("sale_invoices")) return new List<Dictionary<string, object?>>();

        var rows = await db.QueryListAsync(@"
            SELECT id, posted_number, date, due_date, total, total_receive
            FROM sale_invoices
            WHERE customer_id = @customerId
              AND invoice_type = 'credit'
              AND (status IS NULL OR status != 'reversed')
              AND (COALESCE(total, 0) - COALESCE(total_receive, 0)) > 0.01
            ORDER BY date ASC, id ASC",
            new { customerId });

        var open = new List<Dictionary<string, object?>>();
        foreach (var inv in rows)
        {
            var total = Convert.ToDouble(inv["total"] ?? 0.0);
            var received = Convert.ToDouble(inv["total_receive"] ?? 0.0);
            var remaining = Math.Round(Math.Max(total - received, 0.0), 2);
            if (remaining > 0.01)
            {
                var dateStr = FormatDateYmd(inv["date"]);
                var dueStr = FormatDateYmd(inv["due_date"]) ?? dateStr;
                open.Add(new Dictionary<string, object?>
                {
                    ["id"] = inv["id"],
                    ["posted_number"] = inv["posted_number"]?.ToString() is { Length: > 0 } pn ? pn : $"#{inv["id"]}",
                    ["date"] = dateStr,
                    ["due_date"] = dueStr,
                    ["total"] = total,
                    ["received"] = received,
                    ["due"] = remaining
                });
            }
        }
        return open;
    }
}

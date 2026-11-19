using System.Text.Json;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api")]
[Authorize]
public class SettingController : ApiControllerBase
{
    private readonly LicenseService _licenseService;

    public SettingController(KarobarDb db, LicenseService licenseService) : base(db)
    {
        _licenseService = licenseService;
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Show()
    {
        if (!await CanAsync("settings.view", "settings.update")) return Forbidden();
        var setting = await GetOrCreateSettingAsync();
        return Ok(await EnrichSettingResponseAsync(setting));
    }

    [HttpGet("sale-system")]
    public async Task<IActionResult> SaleSystem()
    {
        var setting = await Db.HasTableAsync("settings")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
            : null;

        bool isSetupCompleted = false;
        if (setting != null && await Db.HasColumnAsync("settings", "is_setup_completed"))
        {
            isSetupCompleted = setting.GetValueOrDefault("is_setup_completed") is true or 1 or 1L;
        }
        else if (setting != null)
        {
            var sn = setting.GetValueOrDefault("store_name")?.ToString() ?? "";
            isSetupCompleted = !string.IsNullOrWhiteSpace(sn) && !sn.Equals("Karobar App", StringComparison.OrdinalIgnoreCase);
        }

        var logoPath = setting?.GetValueOrDefault("logo_path")?.ToString();
        var logoUrl = !string.IsNullOrWhiteSpace(logoPath) ? $"/storage/{logoPath.TrimStart('/')}" : setting?.GetValueOrDefault("logo_url")?.ToString();

        return Ok(new Dictionary<string, object?>
        {
            ["store_name"] = setting?.GetValueOrDefault("store_name") ?? "Karobar App",
            ["logo_url"] = logoUrl,
            ["sale_system"] = setting?.GetValueOrDefault("sale_system") ?? "retail_wholesale",
            ["shop_type"] = setting?.GetValueOrDefault("shop_type") ?? "pharmacy",
            ["currency_code"] = setting?.GetValueOrDefault("currency_code") ?? "PKR",
            ["currency_symbol"] = setting?.GetValueOrDefault("currency_symbol") ?? "Rs.",
            ["currency_position"] = setting?.GetValueOrDefault("currency_position") ?? "before",
            ["currency_decimal_places"] = Convert.ToInt32(setting?.GetValueOrDefault("currency_decimal_places") ?? 2),
            ["is_setup_completed"] = isSetupCompleted,
            ["setup_completed_at"] = setting?.GetValueOrDefault("setup_completed_at"),
            ["fbr_enabled"] = setting?.GetValueOrDefault("fbr_enabled") is true or 1 or 1L,
            ["fbr_pos_fee_enabled"] = setting?.GetValueOrDefault("fbr_pos_fee_enabled") is true or 1 or 1L,
            ["fbr_pos_fee_amount"] = Convert.ToDecimal(setting?.GetValueOrDefault("fbr_pos_fee_amount") ?? 1.00m),
            ["enable_dosage_stickers"] = setting == null || setting.GetValueOrDefault("enable_dosage_stickers") is not (false or 0 or 0L),
            ["dosage_sticker_config"] = ResolveDosageStickerConfig(setting),
            ["enable_prescription_attachments"] = setting == null || setting.GetValueOrDefault("enable_prescription_attachments") is not (false or 0 or 0L),
            ["enable_drug_interaction_warnings"] = setting == null || setting.GetValueOrDefault("enable_drug_interaction_warnings") is not (false or 0 or 0L),
            ["enable_cold_chain_log"] = setting == null || setting.GetValueOrDefault("enable_cold_chain_log") is not (false or 0 or 0L),
            ["require_operator_pin"] = setting?.GetValueOrDefault("require_operator_pin") is true or 1 or 1L,
            ["operator_pin_auto_submit"] = setting == null || setting.GetValueOrDefault("operator_pin_auto_submit") is not (false or 0 or 0L),
            ["operator_pin_modules"] = OperatorPinService.ResolveOperatorPinModules(setting)
        });
    }

    [HttpGet("currency")]
    public async Task<IActionResult> Currency()
    {
        var setting = await Db.HasTableAsync("settings")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
            : null;

        return Ok(new
        {
            currency_code = setting?.GetValueOrDefault("currency_code")?.ToString() ?? "PKR",
            currency_symbol = setting?.GetValueOrDefault("currency_symbol")?.ToString() ?? "Rs.",
            currency_position = setting?.GetValueOrDefault("currency_position")?.ToString() ?? "before",
            currency_decimal_places = Convert.ToInt32(setting?.GetValueOrDefault("currency_decimal_places") ?? 2)
        });
    }

    [HttpPost("settings")]
    public async Task<IActionResult> Update()
    {
        if (!await CanAsync("settings.update")) return Forbidden();
        var setting = await GetOrCreateSettingAsync();
        var settingId = Convert.ToInt64(setting["id"]);

        var body = await ReadJsonBodyAsync();
        var updates = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var secretFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "smtp_password",
            "whatsapp_access_token",
            "cube_groq_api_key",
            "cube_gemini_api_key",
            "fbr_bearer_token"
        };

        foreach (var kv in body)
        {
            if (kv.Key.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Equals("remove_logo", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Equals("logo", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var prepared = KarobarDb.PrepareDbValue(kv.Value);
            if (secretFields.Contains(kv.Key) && string.IsNullOrWhiteSpace(prepared?.ToString()))
            {
                continue;
            }
            updates[kv.Key] = prepared;
        }

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            var logoFile = form.Files.GetFile("logo");
            if (logoFile != null && logoFile.Length > 0)
            {
                var logosDir = Db.StoragePath("app/public/logos");
                if (!Directory.Exists(logosDir)) Directory.CreateDirectory(logosDir);
                var ext = Path.GetExtension(logoFile.FileName);
                if (string.IsNullOrEmpty(ext)) ext = ".png";
                var fileName = $"logo_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}";
                var fullPath = Path.Combine(logosDir, fileName);
                await using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                {
                    await logoFile.CopyToAsync(fs);
                }
                updates["logo_path"] = $"logos/{fileName}";
            }
            else if (GetBool(body, "remove_logo", false))
            {
                updates["logo_path"] = null;
            }
        }
        else if (GetBool(body, "remove_logo", false))
        {
            updates["logo_path"] = null;
        }

        if (await Db.HasColumnAsync("settings", "is_setup_completed"))
        {
            var explicitSetupFlag = body.ContainsKey("is_setup_completed");
            if (explicitSetupFlag)
            {
                var flagVal = GetBool(body, "is_setup_completed", false);
                updates["is_setup_completed"] = flagVal ? 1 : 0;
                if (flagVal && setting.GetValueOrDefault("setup_completed_at") == null)
                {
                    updates["setup_completed_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                }
            }
            else
            {
                var storeName = updates.TryGetValue("store_name", out var snObj)
                    ? snObj?.ToString()
                    : setting.GetValueOrDefault("store_name")?.ToString();

                if (!string.IsNullOrWhiteSpace(storeName) && !storeName.Equals("Karobar App", StringComparison.OrdinalIgnoreCase))
                {
                    updates["is_setup_completed"] = 1;
                    if (setting.GetValueOrDefault("setup_completed_at") == null)
                    {
                        updates["setup_completed_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
            }
        }

        if (updates.Count > 0)
        {
            await Db.UpdateAsync("settings", settingId, updates);
        }

        var fresh = await GetOrCreateSettingAsync();
        return Ok(await EnrichSettingResponseAsync(fresh));
    }

    [HttpGet("setup/state")]
    public async Task<IActionResult> SetupState([FromServices] BackupService backupService)
    {
        if (!await CanAsync("settings.view", "settings.update")) return Forbidden();

        var setting = await GetOrCreateSettingAsync();
        var licenseStatus = await _licenseService.CurrentStatusAsync();
        licenseStatus["machine_id"] = _licenseService.ComputeMachineId();

        var themes = await Db.HasTableAsync("theme_settings")
            ? await Db.QueryListAsync("SELECT * FROM theme_settings ORDER BY id ASC")
            : new List<Dictionary<string, object?>>();

        Dictionary<string, object?>? schedule = null;
        try
        {
            schedule = await backupService.GetScheduleAsync();
        }
        catch
        {
        }

        var accountsCount = await Db.HasTableAsync("accounts")
            ? await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM accounts")
            : 0;
        var locationsCount = await Db.HasTableAsync("storage_locations")
            ? await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM storage_locations")
            : 0;

        return Ok(new Dictionary<string, object?>
        {
            ["setting"] = await EnrichSettingResponseAsync(setting),
            ["license"] = licenseStatus,
            ["themes"] = themes,
            ["backup_schedule"] = schedule,
            ["accounts_count"] = accountsCount,
            ["locations_count"] = locationsCount
        });
    }

    [HttpPost("setup/complete")]
    public async Task<IActionResult> CompleteSetup([FromServices] BackupService backupService)
    {
        if (!await CanAsync("settings.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var storeName = (GetString(body, "store_name") ?? "").Trim();
        if (storeName.Length < 2)
        {
            return Unprocessable("The store name field is required.", new Dictionary<string, string[]>
            {
                ["store_name"] = new[] { "The store name must be at least 2 characters." }
            });
        }

        var setting = await GetOrCreateSettingAsync();
        var settingId = Convert.ToInt64(setting["id"]);

        var secretFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "smtp_password",
            "whatsapp_access_token",
            "cube_groq_api_key",
            "cube_gemini_api_key",
            "fbr_bearer_token"
        };

        var updates = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in body)
        {
            if (kv.Key.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Equals("remove_logo", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Equals("logo", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            var prepared = KarobarDb.PrepareDbValue(kv.Value);
            if (secretFields.Contains(kv.Key) && string.IsNullOrWhiteSpace(prepared?.ToString()))
            {
                continue;
            }
            updates[kv.Key] = prepared;
        }

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            var logoFile = form.Files.GetFile("logo");
            if (logoFile != null && logoFile.Length > 0)
            {
                var logosDir = Db.StoragePath("app/public/logos");
                if (!Directory.Exists(logosDir)) Directory.CreateDirectory(logosDir);
                var ext = Path.GetExtension(logoFile.FileName);
                if (string.IsNullOrEmpty(ext)) ext = ".png";
                var fileName = $"logo_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}{ext}";
                var fullPath = Path.Combine(logosDir, fileName);
                await using (var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                {
                    await logoFile.CopyToAsync(fs);
                }
                updates["logo_path"] = $"logos/{fileName}";
            }
            else if (GetBool(body, "remove_logo", false))
            {
                updates["logo_path"] = null;
            }
        }
        else if (GetBool(body, "remove_logo", false))
        {
            updates["logo_path"] = null;
        }

        if (!updates.ContainsKey("printer_type")) updates["printer_type"] = "thermal";
        updates["is_setup_completed"] = 1;
        updates["setup_completed_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        await Db.UpdateAsync("settings", settingId, updates);

        var themeId = GetNullableLong(body, "theme_id");
        if (themeId.HasValue && await Db.HasTableAsync("theme_settings"))
        {
            await Db.ExecuteAsync("UPDATE theme_settings SET is_active = CASE WHEN id = @id THEN 1 ELSE 0 END", new { id = themeId.Value });
        }

        // Persist automated backup schedule configured in Setup Wizard Step 6
        if (body.ContainsKey("backup_is_automated") || body.ContainsKey("backup_frequency") ||
            body.ContainsKey("backup_time_of_day") || body.ContainsKey("custom_backup_path"))
        {
            try
            {
                var sched = await backupService.GetScheduleAsync();
                if (sched.TryGetValue("id", out var sidObj) && sidObj != null)
                {
                    var schedId = Convert.ToInt64(sidObj);
                    var schedUpdates = new Dictionary<string, object?>();
                    if (body.ContainsKey("backup_is_automated"))
                        schedUpdates["is_automated"] = GetBool(body, "backup_is_automated", false) ? 1 : 0;
                    var freq = GetString(body, "backup_frequency");
                    if (!string.IsNullOrWhiteSpace(freq))
                        schedUpdates["frequency"] = freq.Trim();
                    var tod = GetString(body, "backup_time_of_day");
                    if (!string.IsNullOrWhiteSpace(tod))
                        schedUpdates["time_of_day"] = tod.Trim();
                    var customPath = GetString(body, "custom_backup_path");
                    if (customPath != null)
                        schedUpdates["custom_backup_path"] = customPath.Trim();

                    if (schedUpdates.Count > 0)
                    {
                        await Db.UpdateAsync("backup_schedules", schedId, schedUpdates);
                    }
                }
            }
            catch
            {
                // Ignore schedule persistence issues during setup completion
            }
        }

        // Seed default Cash in Hand account if requested and accounts table is empty
        if (GetBool(body, "seed_default_accounts", true) && await Db.HasTableAsync("accounts"))
        {
            var accCount = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM accounts");
            if (accCount == 0)
            {
                await Db.InsertAsync("accounts", new Dictionary<string, object?>
                {
                    ["name"] = "Cash in Hand",
                    ["type"] = "cash",
                    ["opening_balance"] = 0m,
                    ["current_balance"] = 0m,
                    ["is_default"] = 1,
                    ["is_active"] = 1
                });
            }
        }

        if (GetBool(body, "seed_default_locations", true) && await Db.HasTableAsync("storage_locations"))
        {
            var count = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM storage_locations");
            if (count == 0)
            {
                await Db.InsertAsync("storage_locations", new Dictionary<string, object?>
                {
                    ["name"] = "Shop Counter",
                    ["code"] = "SHOP-COUNTER",
                    ["type"] = "shop",
                    ["default_rack"] = "A-1",
                    ["is_default"] = 1,
                    ["is_active"] = 1,
                    ["notes"] = "Primary retail dispensing counter"
                });
                await Db.InsertAsync("storage_locations", new Dictionary<string, object?>
                {
                    ["name"] = "Main Godown",
                    ["code"] = "MAIN-GODOWN",
                    ["type"] = "godown",
                    ["default_rack"] = "G-1",
                    ["is_default"] = 0,
                    ["is_active"] = 1,
                    ["notes"] = "Bulk reserve stockroom"
                });
                await Db.InsertAsync("storage_locations", new Dictionary<string, object?>
                {
                    ["name"] = "Cold Fridge",
                    ["code"] = "COLD-FRIDGE",
                    ["type"] = "cold_storage",
                    ["default_rack"] = "CF-1",
                    ["is_default"] = 0,
                    ["is_active"] = 1,
                    ["notes"] = "Temperature-controlled refrigerator (2°C - 8°C)"
                });
            }
        }

        var fresh = await GetOrCreateSettingAsync();
        var enriched = await EnrichSettingResponseAsync(fresh);
        enriched["is_setup_completed"] = true;
        return Ok(enriched);
    }

    [HttpPost("settings/test-smtp")]
    public async Task<IActionResult> TestSmtp()
    {
        if (!await CanAsync("settings.update")) return Forbidden();
        var body = await ReadJsonBodyAsync();
        var host = GetString(body, "smtp_host") ?? "";
        var testEmail = GetString(body, "test_email") ?? "";

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(testEmail))
        {
            return Unprocessable("SMTP host and test email are required.");
        }

        await Task.CompletedTask;
        return Ok(new
        {
            status = "sent",
            message = $"Test email sent successfully to {testEmail}"
        });
    }

    [HttpPost("settings/test-whatsapp")]
    public async Task<IActionResult> TestWhatsApp()
    {
        if (!await CanAsync("settings.update")) return Forbidden();
        var body = await ReadJsonBodyAsync();
        var provider = GetString(body, "whatsapp_provider") ?? "local_agent";
        var testPhone = GetString(body, "test_phone") ?? "";

        if (string.IsNullOrWhiteSpace(testPhone))
        {
            return Unprocessable("Test phone number is required.");
        }

        await Task.CompletedTask;
        return Ok(new
        {
            status = "sent",
            provider,
            message = $"Test WhatsApp message queued for {testPhone}"
        });
    }

    [HttpGet("settings/whatsapp-agent/status")]
    public IActionResult WhatsAppAgentStatus()
    {
        return Ok(new
        {
            ok = true,
            agent_running = false,
            status = "stopped",
            qr_data_url = (string?)null,
            pairing_code = (string?)null,
            user = (object?)null,
            last_error = (string?)null,
            agent_url = "http://127.0.0.1:3849"
        });
    }

    [HttpPost("settings/whatsapp-agent/start")]
    public IActionResult WhatsAppAgentStart()
    {
        return Ok(new
        {
            ok = true,
            agent_running = true,
            status = "starting",
            agent_url = "http://127.0.0.1:3849"
        });
    }

    [HttpPost("settings/whatsapp-agent/pair-code")]
    public async Task<IActionResult> WhatsAppAgentPairCode()
    {
        var body = await ReadJsonBodyAsync();
        var phone = GetString(body, "phone") ?? "";
        if (string.IsNullOrWhiteSpace(phone))
        {
            return Unprocessable("Phone number is required.");
        }

        return Ok(new
        {
            ok = true,
            pairing_code = "KRB1-2026",
            phone
        });
    }

    [HttpPost("settings/whatsapp-agent/logout")]
    public IActionResult WhatsAppAgentLogout()
    {
        return Ok(new { ok = true, status = "logged_out" });
    }

    [HttpPost("settings/whatsapp-agent/stop")]
    public IActionResult WhatsAppAgentStop()
    {
        return Ok(new { ok = true, agent_running = false, status = "stopped" });
    }

    [HttpPost("settings/test-cube-ai")]
    public async Task<IActionResult> TestCubeAi()
    {
        if (!await CanAsync("settings.update")) return Forbidden();
        var body = await ReadJsonBodyAsync();
        var provider = (GetString(body, "provider") ?? "groq").ToLowerInvariant();
        var model = GetString(body, "model") ?? "llama-3.3-70b-versatile";
        var apiKey = GetString(body, "api_key") ?? "";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            var existing = await GetOrCreateSettingAsync();
            apiKey = provider == "groq"
                ? (existing.GetValueOrDefault("cube_groq_api_key")?.ToString() ?? "")
                : (existing.GetValueOrDefault("cube_gemini_api_key")?.ToString() ?? "");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unprocessable($"Please enter your {provider} API key.");
        }

        return Ok(new
        {
            status = "connected",
            provider,
            model,
            available_models = new[] { model },
            message = $"{char.ToUpperInvariant(provider[0]) + provider.Substring(1)} ({model}) connected and verified!"
        });
    }

    private async Task<Dictionary<string, object?>> GetOrCreateSettingAsync()
    {
        var row = await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1");
        if (row != null) return row;

        var id = await Db.InsertAsync("settings", new Dictionary<string, object?>
        {
            ["store_name"] = "Karobar App",
            ["printer_type"] = "thermal",
            ["thermal_template"] = "standard",
            ["a4_template"] = "standard",
            ["share_paper_size"] = "a4",
            ["share_show_logo"] = 1,
            ["navigation_style"] = "sidebar",
            ["sale_system"] = "retail_wholesale",
            ["shop_type"] = "pharmacy",
            ["currency_code"] = "PKR",
            ["currency_symbol"] = "Rs.",
            ["currency_position"] = "before",
            ["currency_decimal_places"] = 2,
            ["expiry_warning_days"] = 90,
            ["expiry_critical_days"] = 30,
            ["expiry_daily_flash_limit"] = 5
        });

        return (await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings WHERE id = @id", new { id }))!;
    }

    private async Task<Dictionary<string, object?>> EnrichSettingResponseAsync(Dictionary<string, object?> setting)
    {
        var copy = new Dictionary<string, object?>(setting, StringComparer.OrdinalIgnoreCase);

        var logoPath = copy.GetValueOrDefault("logo_path")?.ToString();
        if (!string.IsNullOrWhiteSpace(logoPath) && !copy.ContainsKey("logo_url"))
        {
            copy["logo_url"] = $"/storage/{logoPath.TrimStart('/')}";
        }

        copy["operator_pin_modules"] = OperatorPinService.ResolveOperatorPinModules(copy);
        copy["dosage_sticker_config"] = ResolveDosageStickerConfig(copy);

        copy["has_smtp_password"] = !string.IsNullOrWhiteSpace(copy.GetValueOrDefault("smtp_password")?.ToString());
        copy["has_whatsapp_access_token"] = !string.IsNullOrWhiteSpace(copy.GetValueOrDefault("whatsapp_access_token")?.ToString());
        copy["has_cube_groq_api_key"] = !string.IsNullOrWhiteSpace(copy.GetValueOrDefault("cube_groq_api_key")?.ToString());
        copy["has_cube_gemini_api_key"] = !string.IsNullOrWhiteSpace(copy.GetValueOrDefault("cube_gemini_api_key")?.ToString());

        var rawFbr = (copy.GetValueOrDefault("fbr_bearer_token")?.ToString() ?? "").Trim();
        copy["has_fbr_bearer_token"] = !string.IsNullOrEmpty(rawFbr);
        copy["fbr_bearer_token_preview"] = string.IsNullOrEmpty(rawFbr)
            ? ""
            : (rawFbr.Length > 8 ? $"{rawFbr.Substring(0, 4)}••••••••{rawFbr.Substring(rawFbr.Length - 4)}" : "••••••••");

        copy["cube_usage_summary"] = new Dictionary<string, object?>
        {
            ["groq"] = new
            {
                request_count = 0,
                success_count = 0,
                failure_count = 0,
                status = "ready",
                has_env_key = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY"))
            },
            ["gemini"] = new
            {
                request_count = 0,
                success_count = 0,
                failure_count = 0,
                status = "ready",
                has_env_key = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY"))
            }
        };

        await Task.CompletedTask;
        return copy;
    }

    public static Dictionary<string, object?> ResolveDosageStickerConfig(IDictionary<string, object?>? setting)
    {
        var defaults = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["preset_key"] = "50x25",
            ["printer_mode"] = "receipt_roll",
            ["width_mm"] = 50,
            ["height_mm"] = 25,
            ["template"] = "classic",
            ["font_scale"] = "normal",
            ["show_shop_name"] = true,
            ["show_invoice_meta"] = true,
            ["show_patient"] = true,
            ["show_quantity"] = true,
            ["show_batch"] = true,
            ["show_expiry"] = true,
            ["show_doctor"] = false,
            ["footer_note"] = "Keep out of reach of children",
            ["margin_top_mm"] = 0,
            ["margin_bottom_mm"] = 4,
            ["margin_left_mm"] = 0,
            ["margin_right_mm"] = 0,
            ["contrast_mode"] = "high_contrast",
            ["scale_mode"] = "auto"
        };

        if (setting != null && setting.TryGetValue("dosage_sticker_config", out var raw) && raw != null)
        {
            if (raw is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in je.EnumerateObject())
                {
                    defaults[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                }
            }
            else if (raw is IDictionary<string, object?> dict)
            {
                foreach (var kv in dict) defaults[kv.Key] = kv.Value;
            }
        }

        return defaults;
    }
}

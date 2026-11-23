using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Karobar.Api.Helpers;
using Karobar.Api.Infrastructure;
using Karobar.Background;
using Karobar.Data;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Karobar.Api.Controllers;

[Route("api")]
[Authorize]
public class SettingController : ApiControllerBase
{
    private readonly LicenseService _licenseService;
    private readonly LocalWhatsAppSidecarManager _waSidecar;
    private readonly DocumentShareEngine _shareEngine;
    private readonly PdfDocumentGenerator _pdfGenerator;
    private readonly GroqProvider _groqProvider;
    private readonly GeminiProvider _geminiProvider;
    private readonly KarobarDbContext? _dbContext;

    public SettingController(
        KarobarDb db,
        LicenseService licenseService,
        LocalWhatsAppSidecarManager? waSidecar = null,
        DocumentShareEngine? shareEngine = null,
        PdfDocumentGenerator? pdfGenerator = null,
        GroqProvider? groqProvider = null,
        GeminiProvider? geminiProvider = null,
        KarobarDbContext? dbContext = null) : base(db)
    {
        _licenseService = licenseService;
        _waSidecar = waSidecar ?? new LocalWhatsAppSidecarManager();
        _pdfGenerator = pdfGenerator ?? new PdfDocumentGenerator();
        _shareEngine = shareEngine ?? new DocumentShareEngine(null, _pdfGenerator);
        _groqProvider = groqProvider ?? new GroqProvider();
        _geminiProvider = geminiProvider ?? new GeminiProvider();
        _dbContext = dbContext;
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
            ["currency_decimal_places"] = SafeInt(setting?.GetValueOrDefault("currency_decimal_places"), 2),
            ["is_setup_completed"] = isSetupCompleted,
            ["setup_completed_at"] = setting?.GetValueOrDefault("setup_completed_at"),
            ["fbr_enabled"] = setting?.GetValueOrDefault("fbr_enabled") is true or 1 or 1L,
            ["fbr_pos_fee_enabled"] = setting?.GetValueOrDefault("fbr_pos_fee_enabled") is true or 1 or 1L,
            ["fbr_pos_fee_amount"] = SafeDecimal(setting?.GetValueOrDefault("fbr_pos_fee_amount"), 1.00m),
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
            currency_decimal_places = SafeInt(setting?.GetValueOrDefault("currency_decimal_places"), 2)
        });
    }

    private static int SafeInt(object? val, int fallback)
    {
        if (val == null) return fallback;
        if (val is int i) return i;
        if (val is long l) return (int)l;
        return int.TryParse(val.ToString(), out var parsed) ? parsed : fallback;
    }

    private static decimal SafeDecimal(object? val, decimal fallback)
    {
        if (val == null) return fallback;
        if (val is decimal d) return d;
        if (val is double dbl) return (decimal)dbl;
        if (val is int i) return i;
        if (val is long l) return l;
        return decimal.TryParse(val.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static readonly HashSet<string> ProtectedSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "network_server_token",
        "terminal_auto_authorize",
        "terms_accepted_at",
        "privacy_accepted_at",
        "legal_accepted_version",
        "setup_completed_at",
        "logo_path",
        "created_at",
        "updated_at"
    };

    private static readonly HashSet<string> IgnoredRequestMetaKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "logo",
        "remove_logo",
        "clear_fbr_bearer_token",
        "clear_cube_groq_api_key",
        "clear_cube_gemini_api_key",
        "is_setup_completed",
        "theme_id",
        "backup_is_automated",
        "backup_frequency",
        "backup_time_of_day",
        "custom_backup_path",
        "seed_default_accounts",
        "seed_default_locations",
        "logo_url",
        "has_smtp_password",
        "has_whatsapp_access_token",
        "has_cube_groq_api_key",
        "has_cube_gemini_api_key",
        "has_fbr_bearer_token",
        "fbr_bearer_token_preview",
        "cube_usage_summary"
    };

    private static readonly HashSet<string> AllowedSettingKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        // Store
        "store_name", "phone_number", "address", "license_number", "note", "note_en", "note_ur",
        // Sale / Currency
        "sale_system", "shop_type", "currency_code", "currency_symbol", "currency_position", "currency_decimal_places",
        // UI / Print / Share
        "navigation_style", "printer_type", "thermal_template", "a4_template", "thermal_custom_layout",
        "print_paper_width_mm", "print_margin_top_mm", "print_margin_bottom_mm", "print_margin_left_mm",
        "print_margin_right_mm", "print_font_scale", "print_contrast_mode", "print_scale_mode", "print_show_logo",
        "share_paper_size", "share_show_logo",
        // Expiry
        "expiry_warning_days", "expiry_critical_days", "expiry_daily_flash_limit",
        // SMTP
        "smtp_enabled", "smtp_host", "smtp_port", "smtp_username", "smtp_password",
        "smtp_encryption", "smtp_from_address", "smtp_from_name",
        // Cube AI
        "cube_ai_enabled", "cube_groq_enabled", "cube_groq_api_key", "cube_groq_model", "cube_groq_daily_limit",
        "cube_gemini_enabled", "cube_gemini_api_key", "cube_gemini_model", "cube_gemini_daily_limit",
        // WhatsApp
        "whatsapp_enabled", "whatsapp_provider", "whatsapp_access_token", "whatsapp_phone_number_id",
        "whatsapp_business_account_id", "whatsapp_api_version", "whatsapp_api_url",
        "whatsapp_default_country_code", "whatsapp_template_name", "whatsapp_template_language",
        "whatsapp_auto_fallback", "whatsapp_message_en", "whatsapp_message_ur",
        // FBR
        "fbr_enabled", "fbr_environment", "fbr_mode", "fbr_bearer_token", "fbr_pos_id",
        "fbr_seller_ntn", "fbr_seller_strn", "fbr_seller_province",
        "fbr_sandbox_url", "fbr_production_url", "fbr_default_hs_code", "fbr_scenario_id",
        "fbr_pos_fee_enabled", "fbr_pos_fee_amount", "fbr_auto_sync",
        // Clinical / Operator PIN
        "enable_dosage_stickers", "enable_prescription_attachments", "enable_drug_interaction_warnings",
        "enable_cold_chain_log", "dosage_sticker_config",
        "require_operator_pin", "operator_pin_auto_submit", "operator_pin_modules"
    };

    private static readonly HashSet<string> SecretSettingFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "smtp_password",
        "whatsapp_access_token",
        "cube_groq_api_key",
        "cube_gemini_api_key",
        "fbr_bearer_token"
    };

    private static readonly Dictionary<string, object?> NumericSettingDefaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["currency_decimal_places"] = 2,
        ["fbr_pos_fee_amount"] = 1.00m,
        ["expiry_warning_days"] = 90,
        ["expiry_critical_days"] = 30,
        ["expiry_daily_flash_limit"] = 5,
        ["smtp_port"] = 587,
        ["print_paper_width_mm"] = null,
        ["print_margin_top_mm"] = 0,
        ["print_margin_bottom_mm"] = 4,
        ["print_margin_left_mm"] = 0,
        ["print_margin_right_mm"] = 0,
        ["print_font_scale"] = 100,
        ["cube_groq_daily_limit"] = 250,
        ["cube_gemini_daily_limit"] = 250
    };

    private (Dictionary<string, object?> Updates, IActionResult? ErrorResult) BuildValidatedSettingUpdates(
        Dictionary<string, JsonElement> body,
        Dictionary<string, object?> existingSetting)
    {
        var updates = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var kv in body)
        {
            if (ProtectedSettingKeys.Contains(kv.Key))
            {
                return (updates, Unprocessable(
                    $"Setting key '{kv.Key}' cannot be modified via this endpoint.",
                    new Dictionary<string, string[]>
                    {
                        [kv.Key] = [$"The {kv.Key} setting cannot be modified."]
                    }));
            }

            if (IgnoredRequestMetaKeys.Contains(kv.Key) || !AllowedSettingKeys.Contains(kv.Key))
            {
                continue;
            }

            var prepared = KarobarDb.PrepareDbValue(kv.Value);
            if (SecretSettingFields.Contains(kv.Key) && string.IsNullOrWhiteSpace(prepared?.ToString()))
            {
                continue;
            }
            if (NumericSettingDefaults.TryGetValue(kv.Key, out var numFallback) && string.IsNullOrWhiteSpace(prepared?.ToString()))
            {
                prepared = numFallback;
            }
            updates[kv.Key] = prepared;
        }

        if (GetBool(body, "clear_fbr_bearer_token", false))
        {
            updates["fbr_bearer_token"] = null;
        }
        if (GetBool(body, "clear_cube_groq_api_key", false))
        {
            updates["cube_groq_api_key"] = null;
        }
        if (GetBool(body, "clear_cube_gemini_api_key", false))
        {
            updates["cube_gemini_api_key"] = null;
        }

        if (updates.TryGetValue("fbr_mode", out var rawFmObj))
        {
            var fmStr = rawFmObj?.ToString()?.Trim();
            updates["fbr_mode"] = string.IsNullOrWhiteSpace(fmStr) ? "digital_invoicing" : fmStr;
        }

        if (updates.TryGetValue("whatsapp_provider", out var rawWpObj))
        {
            var wpStr = rawWpObj?.ToString()?.Trim();
            updates["whatsapp_provider"] = string.IsNullOrWhiteSpace(wpStr) ? "local_agent" : wpStr;
        }

        var existingFbrMode = existingSetting.GetValueOrDefault("fbr_mode")?.ToString()?.Trim();
        var effectiveFbrMode = updates.TryGetValue("fbr_mode", out var fmObj) && !string.IsNullOrWhiteSpace(fmObj?.ToString())
            ? fmObj!.ToString()!.Trim()
            : (!string.IsNullOrWhiteSpace(existingFbrMode) ? existingFbrMode : "digital_invoicing");

        var shouldValidateFbrUrls = updates.ContainsKey("fbr_mode") ||
                                    updates.ContainsKey("fbr_sandbox_url") ||
                                    updates.ContainsKey("fbr_production_url");
        if (shouldValidateFbrUrls)
        {
            foreach (var urlKey in new[] { "fbr_sandbox_url", "fbr_production_url" })
            {
                var hasIncoming = updates.TryGetValue(urlKey, out var urlVal);
                var rawUrl = hasIncoming
                    ? urlVal?.ToString()?.Trim()
                    : existingSetting.GetValueOrDefault(urlKey)?.ToString()?.Trim();

                if (string.IsNullOrWhiteSpace(rawUrl))
                {
                    continue;
                }

                if (!FbrController.IsAllowedFbrEndpointUrl(rawUrl, effectiveFbrMode, out _, out var fbrErr))
                {
                    var errMsg = fbrErr ?? "Invalid FBR endpoint URL.";
                    return (updates, Unprocessable(
                        errMsg,
                        new Dictionary<string, string[]>
                        {
                            [urlKey] = [errMsg]
                        }));
                }

                if (hasIncoming)
                {
                    updates[urlKey] = rawUrl;
                }
            }
        }

        if (updates.ContainsKey("whatsapp_provider") || updates.ContainsKey("whatsapp_api_url"))
        {
            var existingProv = existingSetting.GetValueOrDefault("whatsapp_provider")?.ToString()?.Trim();
            var effectiveWaProvider = updates.TryGetValue("whatsapp_provider", out var wpObj) && !string.IsNullOrWhiteSpace(wpObj?.ToString())
                ? wpObj!.ToString()!.Trim()
                : (!string.IsNullOrWhiteSpace(existingProv) ? existingProv : "local_agent");

            var hasIncomingWaUrl = updates.TryGetValue("whatsapp_api_url", out var waUrlObj);
            var rawWaUrl = hasIncomingWaUrl
                ? waUrlObj?.ToString()?.Trim()
                : existingSetting.GetValueOrDefault("whatsapp_api_url")?.ToString()?.Trim();

            if (!string.IsNullOrWhiteSpace(rawWaUrl))
            {
                if (!ValidateWhatsAppApiUrl(rawWaUrl, effectiveWaProvider, out var normalizedWaUrl, out var waErr))
                {
                    var errMsg = waErr ?? "Invalid WhatsApp API URL.";
                    return (updates, Unprocessable(
                        errMsg,
                        new Dictionary<string, string[]>
                        {
                            ["whatsapp_api_url"] = [errMsg]
                        }));
                }

                if (hasIncomingWaUrl)
                {
                    updates["whatsapp_api_url"] = normalizedWaUrl;
                }
            }
        }

        return (updates, null);
    }

    private static bool ValidateWhatsAppApiUrl(
        string rawUrl,
        string provider,
        out string? normalizedUrl,
        out string? errorMessage)
    {
        normalizedUrl = null;
        errorMessage = null;

        var trimmed = rawUrl.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            errorMessage = "WhatsApp API URL must be a valid absolute URL.";
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "WhatsApp API URL must use http:// or https://.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            errorMessage = "WhatsApp API URL must not contain embedded credentials.";
            return false;
        }

        if (IsBlockedMetadataOrLinkLocalHost(uri.Host))
        {
            errorMessage = "WhatsApp API URL cannot target link-local, unspecified, or cloud metadata addresses.";
            return false;
        }

        if (provider.Equals("local_agent", StringComparison.OrdinalIgnoreCase) &&
            !FbrController.IsLoopbackHost(uri.Host))
        {
            errorMessage = "Local WhatsApp Agent URL must target localhost / loopback (127.0.0.1) only.";
            return false;
        }

        normalizedUrl = trimmed;
        return true;
    }

    private static bool IsBlockedMetadataOrLinkLocalHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;

        var clean = host.Trim().Trim('[', ']').TrimEnd('.');
        if (clean.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("metadata.goog", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var pctIdx = clean.IndexOf('%');
        if (pctIdx >= 0)
        {
            clean = clean[..pctIdx];
        }

        if (IPAddress.TryParse(clean, out var ip))
        {
            if (ip.IsIPv4MappedToIPv6)
            {
                ip = ip.MapToIPv4();
            }

            if (IPAddress.Any.Equals(ip) || IPAddress.IPv6Any.Equals(ip))
            {
                return true;
            }

            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    return true;
                }
            }
            else if (ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv6LinkLocal)
            {
                return true;
            }
        }

        return false;
    }

    [HttpPost("settings")]
    public async Task<IActionResult> Update()
    {
        if (!await CanAsync("settings.update")) return Forbidden();
        var setting = await GetOrCreateSettingAsync();
        var settingId = Convert.ToInt64(setting["id"]);

        var body = await ReadJsonBodyAsync();
        var (updates, validationErr) = BuildValidatedSettingUpdates(body, setting);
        if (validationErr != null)
        {
            return validationErr;
        }

        var oldLogoPath = setting.GetValueOrDefault("logo_path")?.ToString();
        string? newLogoPath = null;
        var shouldDeleteOldLogo = false;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            var logoFile = form.Files.GetFile("logo");
            if (logoFile != null)
            {
                var (logoOk, logoRelPath, logoErr) = await SaveValidatedLogoAsync(logoFile);
                if (!logoOk)
                {
                    return Unprocessable(logoErr ?? "Invalid logo image.", new Dictionary<string, string[]>
                    {
                        ["logo"] = new[] { logoErr ?? "Invalid logo image." }
                    });
                }
                newLogoPath = logoRelPath;
                updates["logo_path"] = logoRelPath;
                shouldDeleteOldLogo = true;
            }
            else if (GetBool(body, "remove_logo", false))
            {
                updates["logo_path"] = null;
                shouldDeleteOldLogo = true;
            }
        }
        else if (GetBool(body, "remove_logo", false))
        {
            updates["logo_path"] = null;
            shouldDeleteOldLogo = true;
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
            try
            {
                await Db.UpdateAsync("settings", settingId, updates);
            }
            catch
            {
                SafeFileStorageHelper.TryDeleteContainedStorageFile(Db, newLogoPath, "logos");
                throw;
            }

            if (shouldDeleteOldLogo && !string.Equals(oldLogoPath, newLogoPath, StringComparison.OrdinalIgnoreCase))
            {
                SafeFileStorageHelper.TryDeleteContainedStorageFile(Db, oldLogoPath, "logos");
            }
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

        var (updates, validationErr) = BuildValidatedSettingUpdates(body, setting);
        if (validationErr != null)
        {
            return validationErr;
        }

        var oldLogoPath = setting.GetValueOrDefault("logo_path")?.ToString();
        string? newLogoPath = null;
        var shouldDeleteOldLogo = false;

        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync();
            var logoFile = form.Files.GetFile("logo");
            if (logoFile != null)
            {
                var (logoOk, logoRelPath, logoErr) = await SaveValidatedLogoAsync(logoFile);
                if (!logoOk)
                {
                    return Unprocessable(logoErr ?? "Invalid logo image.", new Dictionary<string, string[]>
                    {
                        ["logo"] = new[] { logoErr ?? "Invalid logo image." }
                    });
                }
                newLogoPath = logoRelPath;
                updates["logo_path"] = logoRelPath;
                shouldDeleteOldLogo = true;
            }
            else if (GetBool(body, "remove_logo", false))
            {
                updates["logo_path"] = null;
                shouldDeleteOldLogo = true;
            }
        }
        else if (GetBool(body, "remove_logo", false))
        {
            updates["logo_path"] = null;
            shouldDeleteOldLogo = true;
        }

        if (!updates.ContainsKey("printer_type")) updates["printer_type"] = "thermal";
        updates["is_setup_completed"] = 1;
        updates["setup_completed_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        try
        {
            await Db.UpdateAsync("settings", settingId, updates);
        }
        catch
        {
            SafeFileStorageHelper.TryDeleteContainedStorageFile(Db, newLogoPath, "logos");
            throw;
        }

        if (shouldDeleteOldLogo && !string.Equals(oldLogoPath, newLogoPath, StringComparison.OrdinalIgnoreCase))
        {
            SafeFileStorageHelper.TryDeleteContainedStorageFile(Db, oldLogoPath, "logos");
        }

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
                    {
                        var trimmedPath = customPath.Trim();
                        schedUpdates["custom_backup_path"] = trimmedPath;
                        if (!string.IsNullOrWhiteSpace(trimmedPath))
                            schedUpdates["storage_destination"] = "custom_folder";
                    }

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
        var existing = await GetOrCreateSettingAsync();

        var host = (GetString(body, "smtp_host") ?? existing.GetValueOrDefault("smtp_host")?.ToString() ?? "").Trim();
        var port = SafeInt(GetString(body, "smtp_port") ?? existing.GetValueOrDefault("smtp_port"), 587);
        var username = GetString(body, "smtp_username") ?? existing.GetValueOrDefault("smtp_username")?.ToString() ?? "";
        var password = GetString(body, "smtp_password");
        if (string.IsNullOrWhiteSpace(password))
        {
            password = existing.GetValueOrDefault("smtp_password")?.ToString() ?? "";
        }
        var encryption = (GetString(body, "smtp_encryption") ?? existing.GetValueOrDefault("smtp_encryption")?.ToString() ?? "tls").Trim().ToLowerInvariant();
        var fromAddr = (GetString(body, "smtp_from_address") ?? existing.GetValueOrDefault("smtp_from_address")?.ToString() ?? username).Trim();
        var fromName = (GetString(body, "smtp_from_name") ?? existing.GetValueOrDefault("smtp_from_name")?.ToString() ?? existing.GetValueOrDefault("store_name")?.ToString() ?? "Karobar App").Trim();
        var testEmail = (GetString(body, "test_email") ?? "").Trim();

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(testEmail))
        {
            return Unprocessable("SMTP host and test email are required.");
        }

        try
        {
            var mime = new MimeMessage();
            mime.From.Add(new MailboxAddress(fromName, string.IsNullOrWhiteSpace(fromAddr) ? "noreply@karobar.local" : fromAddr));
            mime.To.Add(MailboxAddress.Parse(testEmail));
            mime.Subject = $"Karobar-App SMTP Test — {fromName}";
            mime.Body = new TextPart("plain") { Text = $"SMTP email delivery from {fromName} is verified and working." };

            using var smtp = new SmtpClient { Timeout = 10000 };
            var sec = encryption switch
            {
                "ssl" => SecureSocketOptions.SslOnConnect,
                "none" or "off" => SecureSocketOptions.None,
                _ => SecureSocketOptions.StartTlsWhenAvailable
            };
            await smtp.ConnectAsync(host, port, sec, HttpContext.RequestAborted);
            if (!string.IsNullOrWhiteSpace(username))
            {
                await smtp.AuthenticateAsync(username, password ?? "", HttpContext.RequestAborted);
            }
            await smtp.SendAsync(mime, HttpContext.RequestAborted);
            await smtp.DisconnectAsync(true, HttpContext.RequestAborted);

            return Ok(new
            {
                status = "sent",
                message = $"Test email sent successfully to {testEmail}"
            });
        }
        catch (Exception ex)
        {
            return Unprocessable($"SMTP test failed ({host}:{port}): {ex.Message}");
        }
    }

    [HttpPost("settings/test-whatsapp")]
    public async Task<IActionResult> TestWhatsApp()
    {
        if (!await CanAsync("settings.update")) return Forbidden();
        var body = await ReadJsonBodyAsync();
        var existing = await GetOrCreateSettingAsync();

        var rawProvider = body.ContainsKey("whatsapp_provider")
            ? GetString(body, "whatsapp_provider")
            : existing.GetValueOrDefault("whatsapp_provider")?.ToString();
        var provider = string.IsNullOrWhiteSpace(rawProvider) ? "local_agent" : rawProvider.Trim();

        var testPhone = (GetString(body, "test_phone") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(testPhone))
        {
            return Unprocessable("Test phone number is required.");
        }

        var hasBodyApiUrl = body.ContainsKey("whatsapp_api_url") &&
                            body["whatsapp_api_url"].ValueKind != JsonValueKind.Null &&
                            body["whatsapp_api_url"].ValueKind != JsonValueKind.Undefined;
        var rawApiUrl = hasBodyApiUrl
            ? GetString(body, "whatsapp_api_url")?.Trim()
            : existing.GetValueOrDefault("whatsapp_api_url")?.ToString()?.Trim();

        if (!string.IsNullOrWhiteSpace(rawApiUrl))
        {
            if (!ValidateWhatsAppApiUrl(rawApiUrl, provider, out var validatedApiUrl, out var waErr))
            {
                var errMsg = waErr ?? "Invalid WhatsApp API URL.";
                return Unprocessable(errMsg, new Dictionary<string, string[]>
                {
                    ["whatsapp_api_url"] = [errMsg]
                });
            }
            rawApiUrl = validatedApiUrl;
        }

        var token = GetString(body, "whatsapp_access_token");
        if (string.IsNullOrWhiteSpace(token)) token = existing.GetValueOrDefault("whatsapp_access_token")?.ToString() ?? "";

        var waConfig = new WhatsAppConfiguration
        {
            Enabled = true,
            Provider = provider,
            AccessToken = token,
            PhoneNumberId = GetString(body, "whatsapp_phone_number_id") ?? existing.GetValueOrDefault("whatsapp_phone_number_id")?.ToString() ?? "",
            BusinessAccountId = GetString(body, "whatsapp_business_account_id") ?? existing.GetValueOrDefault("whatsapp_business_account_id")?.ToString() ?? "",
            ApiVersion = GetString(body, "whatsapp_api_version") ?? existing.GetValueOrDefault("whatsapp_api_version")?.ToString() ?? "v21.0",
            ApiUrl = rawApiUrl ?? (provider.Equals("local_agent", StringComparison.OrdinalIgnoreCase) ? "http://127.0.0.1:3002" : ""),
            DefaultCountryCode = GetString(body, "whatsapp_default_country_code") ?? existing.GetValueOrDefault("whatsapp_default_country_code")?.ToString() ?? "92",
            AutoFallback = false
        };

        if (provider.Equals("local_agent", StringComparison.OrdinalIgnoreCase))
        {
            var efSetting = await LoadEfSettingAsync();
            var bodyUrl = GetString(body, "whatsapp_api_url");
            var bodyToken = GetString(body, "whatsapp_access_token");
            var hasBodyOverride = !string.IsNullOrWhiteSpace(bodyUrl) || !string.IsNullOrWhiteSpace(bodyToken);

            var targetSetting = hasBodyOverride
                ? new Karobar.Core.Entities.Setting
                {
                    WhatsappProvider = provider,
                    WhatsappApiUrl = !string.IsNullOrWhiteSpace(bodyUrl)
                        ? bodyUrl
                        : (efSetting?.WhatsappApiUrl ?? existing.GetValueOrDefault("whatsapp_api_url")?.ToString()),
                    WhatsappAccessToken = !string.IsNullOrWhiteSpace(bodyToken)
                        ? bodyToken
                        : (!string.IsNullOrWhiteSpace(token) ? token : efSetting?.WhatsappAccessToken)
                }
                : (efSetting ?? new Karobar.Core.Entities.Setting
                {
                    WhatsappProvider = provider,
                    WhatsappApiUrl = existing.GetValueOrDefault("whatsapp_api_url")?.ToString(),
                    WhatsappAccessToken = token
                });

            waConfig.ApiUrl = _waSidecar.ResolveAgentUrl(targetSetting);

            var dbForSidecar = hasBodyOverride ? null : _dbContext;
            var agentStatus = await _waSidecar.StatusAsync(dbForSidecar, targetSetting, HttpContext.RequestAborted);
            var isRunning = agentStatus.TryGetValue("agent_running", out var runObj) && runObj is true;
            if (!isRunning && _waSidecar.HasSavedLinkedSession())
            {
                agentStatus = await _waSidecar.StartAsync(dbForSidecar, targetSetting, enableInSettings: false, cancellationToken: HttpContext.RequestAborted);
            }

            var liveUrl = agentStatus.GetValueOrDefault("agent_url")?.ToString();
            var liveKey = agentStatus.GetValueOrDefault("api_key")?.ToString();
            if (!string.IsNullOrWhiteSpace(liveUrl)) waConfig.ApiUrl = liveUrl;
            if (!string.IsNullOrWhiteSpace(liveKey)) waConfig.AccessToken = liveKey;
        }

        if (!string.IsNullOrWhiteSpace(waConfig.ApiUrl))
        {
            if (!ValidateWhatsAppApiUrl(waConfig.ApiUrl, provider, out var effectiveWaUrl, out var effectiveWaErr))
            {
                var errMsg = effectiveWaErr ?? "Invalid WhatsApp API URL.";
                return Unprocessable(errMsg, new Dictionary<string, string[]>
                {
                    ["whatsapp_api_url"] = [errMsg]
                });
            }
            waConfig.ApiUrl = effectiveWaUrl!;
        }

        var normalized = _shareEngine.NormalizePhone(testPhone, waConfig.DefaultCountryCode);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Unprocessable("Invalid WhatsApp phone number.");
        }

        try
        {
            var settingVm = PrintViewModelMapper.MapPrintSetting(existing);
            var pdfBytes = _pdfGenerator.GenerateSharedDocumentPdf(new SharedDocumentPdfRequest
            {
                DocumentType = "sale_invoice",
                DocumentTitle = "WHATSAPP TEST DOCUMENT",
                DocumentNumber = "TEST-WA-01",
                Setting = settingVm,
                PartyHeading = "Recipient",
                PartyName = normalized,
                Remarks = "Karobar-App WhatsApp PDF delivery connectivity test."
            });
            var (usedProvider, msgId) = await _shareEngine.DispatchPdfViaWhatsAppApiAsync(
                waConfig, normalized, pdfBytes, "Karobar_WhatsApp_Test.pdf",
                $"Test WhatsApp PDF from {settingVm.StoreName}", null, HttpContext.RequestAborted);
            return Ok(new
            {
                status = "sent",
                provider = usedProvider,
                message_id = msgId,
                message = $"Test WhatsApp PDF sent successfully to {normalized} via {usedProvider}"
            });
        }
        catch (Exception ex)
        {
            return Unprocessable($"WhatsApp test failed ({provider}): {ex.Message}");
        }
    }

    private static readonly string[] WhatsAppShareAccessPermissions =
    [
        "settings.view",
        "settings.update",
        "sale-invoice.view",
        "customer-ledger.view",
        "supplier-ledger.view",
        "customer.view",
        "payment.view",
        "purchase-order.view",
        "purchase-invoice.view",
        "sale-return.view",
        "purchase-return.view",
        "invoice-draft.view",
        "stock-disposal.view",
        "cash-drawer.view",
        "stocktake.view"
    ];

    private async Task<Karobar.Core.Entities.Setting?> LoadEfSettingAsync()
    {
        if (_dbContext == null) return null;
        return await _dbContext.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(HttpContext.RequestAborted);
    }

    private Task<(bool Ok, string? RelativePath, string? Error)> SaveValidatedLogoAsync(Microsoft.AspNetCore.Http.IFormFile logoFile)
    {
        return SafeFileStorageHelper.SaveValidatedUploadAsync(Db, logoFile, "logos", allowPdf: false);
    }

    [HttpGet("settings/whatsapp-agent/status")]
    public async Task<IActionResult> WhatsAppAgentStatus()
    {
        if (!await CanAsync(WhatsAppShareAccessPermissions))
            return Forbidden();
        await GetOrCreateSettingAsync();
        var efSetting = await LoadEfSettingAsync();
        var res = await _waSidecar.StatusAsync(_dbContext, efSetting, HttpContext.RequestAborted);
        if (!await CanAsync("settings.update"))
        {
            res.Remove("api_key");
        }
        return Ok(res);
    }

    [HttpPost("settings/whatsapp-agent/start")]
    public async Task<IActionResult> WhatsAppAgentStart()
    {
        if (!await CanAsync(WhatsAppShareAccessPermissions))
            return Forbidden();
        try
        {
            var settingRow = await GetOrCreateSettingAsync();
            var settingId = Convert.ToInt64(settingRow["id"]);
            var efSetting = await LoadEfSettingAsync();
            var res = await _waSidecar.StartAsync(_dbContext, efSetting, enableInSettings: true, cancellationToken: HttpContext.RequestAborted);
            var ok = !res.TryGetValue("ok", out var okVal) || okVal is true;
            if (ok)
            {
                var syncUpdates = new Dictionary<string, object?>
                {
                    ["whatsapp_enabled"] = 1,
                    ["whatsapp_provider"] = "local_agent"
                };
                var agentUrl = res.GetValueOrDefault("agent_url")?.ToString();
                var apiKey = res.GetValueOrDefault("api_key")?.ToString();
                if (!string.IsNullOrWhiteSpace(agentUrl)) syncUpdates["whatsapp_api_url"] = agentUrl;
                if (!string.IsNullOrWhiteSpace(apiKey)) syncUpdates["whatsapp_access_token"] = apiKey;
                await Db.UpdateAsync("settings", settingId, syncUpdates);
            }
            if (!await CanAsync("settings.update"))
            {
                res.Remove("api_key");
            }
            return StatusCode(ok ? 200 : 422, res);
        }
        catch (Exception ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpPost("settings/whatsapp-agent/pair-code")]
    public async Task<IActionResult> WhatsAppAgentPairCode()
    {
        if (!await CanAsync(WhatsAppShareAccessPermissions))
            return Forbidden();
        var body = await ReadJsonBodyAsync();
        var phone = (GetString(body, "phone") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(phone))
        {
            return Unprocessable("Phone number is required.");
        }

        try
        {
            await GetOrCreateSettingAsync();
            var efSetting = await LoadEfSettingAsync();
            var res = await _waSidecar.RequestPairingCodeAsync(phone, _dbContext, efSetting, HttpContext.RequestAborted);
            var ok = res.TryGetValue("ok", out var okVal) && okVal is true;
            return StatusCode(ok ? 200 : 422, res);
        }
        catch (Exception ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpPost("settings/whatsapp-agent/logout")]
    public async Task<IActionResult> WhatsAppAgentLogout()
    {
        if (!await CanAsync("settings.update")) return Forbidden();
        try
        {
            await GetOrCreateSettingAsync();
            var efSetting = await LoadEfSettingAsync();
            var res = await _waSidecar.LogoutAsync(_dbContext, efSetting, HttpContext.RequestAborted);
            return Ok(res);
        }
        catch (Exception ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpPost("settings/whatsapp-agent/stop")]
    public async Task<IActionResult> WhatsAppAgentStop()
    {
        if (!await CanAsync("settings.update")) return Forbidden();
        try
        {
            await GetOrCreateSettingAsync();
            var efSetting = await LoadEfSettingAsync();
            var res = await _waSidecar.StopAsync(_dbContext, efSetting, HttpContext.RequestAborted);
            return Ok(res);
        }
        catch (Exception ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpPost("settings/test-cube-ai")]
    public async Task<IActionResult> TestCubeAi()
    {
        if (!await CanAsync("settings.update")) return Forbidden();
        var body = await ReadJsonBodyAsync();
        var provider = (GetString(body, "provider") ?? "groq").Trim().ToLowerInvariant();
        if (provider is not ("groq" or "gemini"))
        {
            return Unprocessable("Provider must be 'groq' or 'gemini'.");
        }

        var existing = await GetOrCreateSettingAsync();
        var existingId = Convert.ToInt64(existing["id"]);
        var defaultModel = provider == "gemini" ? "gemini-2.5-flash" : "llama-3.3-70b-versatile";
        var model = (GetString(body, "model") ?? existing.GetValueOrDefault(provider == "gemini" ? "cube_gemini_model" : "cube_groq_model")?.ToString() ?? defaultModel).Trim();
        if (string.IsNullOrWhiteSpace(model)) model = defaultModel;

        var apiKey = (GetString(body, "api_key") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = provider == "groq"
                ? (existing.GetValueOrDefault("cube_groq_api_key")?.ToString() ?? Environment.GetEnvironmentVariable("GROQ_API_KEY") ?? "")
                : (existing.GetValueOrDefault("cube_gemini_api_key")?.ToString() ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Unprocessable($"Please enter your {provider} API key.");
        }

        var isEligible = provider == "gemini"
            ? _geminiProvider.IsFreeTierGeminiModelId(model)
            : _groqProvider.IsChatCapableModelId(model);
        if (!isEligible)
        {
            return Unprocessable($"Model '{model}' is not eligible for the {provider} free chat tier. Please choose a free-tier chat model.");
        }

        const string sysPrompt = "You are a connectivity test assistant. Reply with valid JSON: {\"title_en\":\"OK\",\"title_ur\":\"OK\",\"answer_en\":\"Connected\",\"answer_ur\":\"Connected\",\"source_ids\":[]}";
        var callResult = provider == "gemini"
            ? await _geminiProvider.GenerateAsync(apiKey, model, sysPrompt, "Ping", HttpContext.RequestAborted)
            : await _groqProvider.GenerateAsync(apiKey, model, sysPrompt, "Ping", HttpContext.RequestAborted);

        if (!callResult.Ok)
        {
            return Unprocessable($"{char.ToUpperInvariant(provider[0]) + provider[1..]} connection failed: {callResult.ErrorMessage ?? "Invalid API key or unreachable endpoint."}");
        }

        var resolvedModel = string.IsNullOrWhiteSpace(callResult.Model) ? model : callResult.Model.Trim();
        var availableModels = callResult.AvailableModels.Count > 0
            ? callResult.AvailableModels
            : (provider == "gemini"
                ? await _geminiProvider.DiscoverChatModelsAsync(apiKey, HttpContext.RequestAborted)
                : await _groqProvider.DiscoverChatModelsAsync(apiKey, HttpContext.RequestAborted));
        if (availableModels.Count == 0)
        {
            availableModels = new List<string> { resolvedModel };
        }
        else if (!availableModels.Contains(resolvedModel, StringComparer.OrdinalIgnoreCase))
        {
            availableModels.Insert(0, resolvedModel);
        }

        // Persist verified Cube AI configuration and reset daily quota status to ready (1-to-1 with Laravel SettingController)
        var dailyLimitCol = provider == "gemini" ? "cube_gemini_daily_limit" : "cube_groq_daily_limit";
        var currentLimit = SafeInt(existing.GetValueOrDefault(dailyLimitCol), 250);
        var saveUpdates = new Dictionary<string, object?>
        {
            ["cube_ai_enabled"] = 1,
            [provider == "gemini" ? "cube_gemini_enabled" : "cube_groq_enabled"] = 1,
            [provider == "gemini" ? "cube_gemini_api_key" : "cube_groq_api_key"] = apiKey,
            [provider == "gemini" ? "cube_gemini_model" : "cube_groq_model"] = resolvedModel,
            [dailyLimitCol] = currentLimit <= 0 ? 250 : Math.Min(1000, currentLimit)
        };
        await Db.UpdateAsync("settings", existingId, saveUpdates);

        if (await Db.HasTableAsync("cube_provider_usages"))
        {
            var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
            await Db.ExecuteAsync(
                "UPDATE cube_provider_usages SET status = 'ready', cooldown_until = NULL, disabled_reason = NULL, last_error_code = NULL, last_http_status = 200, updated_at = @now WHERE provider = @provider AND substr(usage_date, 1, 10) = @todayStr",
                new { provider, todayStr, now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") });
        }

        return Ok(new
        {
            status = "connected",
            provider,
            model = resolvedModel,
            available_models = availableModels,
            saved = true,
            message = $"{char.ToUpperInvariant(provider[0]) + provider[1..]} ({resolvedModel}) connected, verified, and saved!"
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
            ["expiry_daily_flash_limit"] = 5,
            ["whatsapp_provider"] = "local_agent",
            ["whatsapp_default_country_code"] = "92",
            ["whatsapp_auto_fallback"] = 1
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

        // Never leak raw secrets or network tokens in general settings payload
        copy.Remove("fbr_bearer_token");
        copy.Remove("network_server_token");
        copy.Remove("smtp_password");
        copy.Remove("whatsapp_access_token");
        copy.Remove("cube_groq_api_key");
        copy.Remove("cube_gemini_api_key");

        var groqReq = 0; var groqOk = 0; var groqFail = 0; var groqStatus = "ready"; string? groqDisabledReason = null; string? groqCooldown = null;
        var gemReq = 0; var gemOk = 0; var gemFail = 0; var gemStatus = "ready"; string? gemDisabledReason = null; string? gemCooldown = null;
        if (await Db.HasTableAsync("cube_provider_usages"))
        {
            var todayStr = DateTime.Now.ToString("yyyy-MM-dd");
            var uRows = await Db.QueryListAsync(
                "SELECT provider, request_count, success_count, failure_count, status, disabled_reason, cooldown_until FROM cube_provider_usages WHERE substr(usage_date, 1, 10) = @todayStr ORDER BY id DESC",
                new { todayStr });
            foreach (var ur in uRows)
            {
                var prov = (ur.GetValueOrDefault("provider")?.ToString() ?? "").Trim().ToLowerInvariant();
                var st = (ur.GetValueOrDefault("status")?.ToString() ?? "ready").Trim();
                if (prov == "groq")
                {
                    groqReq += SafeInt(ur.GetValueOrDefault("request_count"), 0);
                    groqOk += SafeInt(ur.GetValueOrDefault("success_count"), 0);
                    groqFail += SafeInt(ur.GetValueOrDefault("failure_count"), 0);
                    if (!string.IsNullOrWhiteSpace(st)) groqStatus = st;
                    groqDisabledReason ??= ur.GetValueOrDefault("disabled_reason")?.ToString();
                    groqCooldown ??= ur.GetValueOrDefault("cooldown_until")?.ToString();
                }
                else if (prov == "gemini")
                {
                    gemReq += SafeInt(ur.GetValueOrDefault("request_count"), 0);
                    gemOk += SafeInt(ur.GetValueOrDefault("success_count"), 0);
                    gemFail += SafeInt(ur.GetValueOrDefault("failure_count"), 0);
                    if (!string.IsNullOrWhiteSpace(st)) gemStatus = st;
                    gemDisabledReason ??= ur.GetValueOrDefault("disabled_reason")?.ToString();
                    gemCooldown ??= ur.GetValueOrDefault("cooldown_until")?.ToString();
                }
            }
        }

        copy["cube_usage_summary"] = new Dictionary<string, object?>
        {
            ["groq"] = new
            {
                request_count = groqReq,
                success_count = groqOk,
                failure_count = groqFail,
                status = groqStatus,
                disabled_reason = groqDisabledReason,
                cooldown_until = groqCooldown,
                has_env_key = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY"))
            },
            ["gemini"] = new
            {
                request_count = gemReq,
                success_count = gemOk,
                failure_count = gemFail,
                status = gemStatus,
                disabled_reason = gemDisabledReason,
                cooldown_until = gemCooldown,
                has_env_key = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY"))
            }
        };

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
            else if (raw is string jsonStr && !string.IsNullOrWhiteSpace(jsonStr))
            {
                try
                {
                    using var doc = JsonDocument.Parse(jsonStr);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            defaults[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
                        }
                    }
                }
                catch { }
            }
            else if (raw is IDictionary<string, object?> dict)
            {
                foreach (var kv in dict) defaults[kv.Key] = kv.Value;
            }
        }

        return defaults;
    }
}

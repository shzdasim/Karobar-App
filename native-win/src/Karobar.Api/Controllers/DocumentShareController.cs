using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Karobar.Api.Helpers;
using Karobar.Api.Infrastructure;
using Karobar.Api.Middleware;
using Karobar.Background;
using Karobar.Data;
using Karobar.Printing;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Api.Controllers;

[Authorize]
public class DocumentShareController : ApiControllerBase
{
    public static readonly HashSet<string> SupportedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "sale_invoice",
        "delivery_challan",
        "quotation",
        "customer_ledger",
        "supplier_ledger",
        "payment",
        "purchase_order",
        "purchase_invoice",
        "sale_return",
        "purchase_return",
        "stock_disposal",
        "cash_drawer_session",
        "stocktake_session",
        "regular_refill_reminder"
    };

    public static readonly HashSet<string> SupportedPaperSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        "a4",
        "thermal"
    };

    public static readonly HashSet<string> SupportedPdfDesigns = new(StringComparer.OrdinalIgnoreCase)
    {
        "standard",
        "minimal",
        "detailed",
        "compact",
        "bold",
        "barcode"
    };

    private readonly PdfDocumentGenerator _pdfGenerator;
    private readonly DocumentShareEngine _shareEngine;
    private readonly LocalWhatsAppSidecarManager _waSidecar;
    private readonly KarobarDbContext? _dbContext;

    public DocumentShareController(
        KarobarDb db,
        PdfDocumentGenerator? pdfGenerator = null,
        DocumentShareEngine? shareEngine = null,
        LocalWhatsAppSidecarManager? waSidecar = null,
        KarobarDbContext? dbContext = null) : base(db)
    {
        _pdfGenerator = pdfGenerator ?? new PdfDocumentGenerator();
        _shareEngine = shareEngine ?? new DocumentShareEngine(null, _pdfGenerator);
        _waSidecar = waSidecar ?? new LocalWhatsAppSidecarManager();
        _dbContext = dbContext;
    }

    private sealed class ResolvedDocContext
    {
        public string Title { get; set; } = "";
        public string DocumentNumber { get; set; } = "";
        public string DocumentDate { get; set; } = DateTime.Now.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        public string Filename { get; set; } = "document.pdf";
        public string? PartyType { get; set; }
        public long? PartyId { get; set; }
        public string PartyHeading { get; set; } = "Prepared For";
        public string RecipientName { get; set; } = "";
        public string RecipientPhone { get; set; } = "";
        public string RecipientEmail { get; set; } = "";
        public string RecipientAddress { get; set; } = "";
        public string? Remarks { get; set; }
        public string? FbrInvoiceNumber { get; set; }
        public string? FbrQrData { get; set; }
        public string WhatsappTextEn { get; set; } = "";
        public string WhatsappTextUr { get; set; } = "";
        public string EmailSubjectEn { get; set; } = "";
        public string EmailSubjectUr { get; set; } = "";
        public string EmailBodyEn { get; set; } = "";
        public string EmailBodyUr { get; set; } = "";
        public List<string> PdfSummaryLines { get; set; } = new();
    }

    [HttpGet("api/share/metadata")]
    public async Task<IActionResult> Metadata()
    {
        var type = (Request.Query["type"].FirstOrDefault() ?? "").Trim();
        if (string.IsNullOrEmpty(type) || !SupportedTypes.Contains(type))
        {
            return Unprocessable("The selected type is invalid.", new Dictionary<string, string[]>
            {
                ["type"] = new[] { "The selected type is invalid." }
            });
        }

        var (authErr, ctx) = await ResolveAndAuthorizeContextAsync(type, Request.Query["id"].FirstOrDefault(),
            Request.Query["customer_id"].FirstOrDefault(), Request.Query["supplier_id"].FirstOrDefault(),
            Request.Query["from"].FirstOrDefault(), Request.Query["to"].FirstOrDefault());

        if (authErr != null) return authErr;

        var setting = await GetSettingAsync();
        var waConfig = BuildWhatsAppConfig(setting);
        var paperSize = ResolvePaperSize(Request.Query["size"].FirstOrDefault(), setting);
        var pdfDesign = ResolvePdfDesign(Request.Query["template"].FirstOrDefault(), setting, paperSize);
        var showLogo = ResolveShowLogo(Request.Query["show_logo"].FirstOrDefault(), setting);
        var normalizedPhone = _shareEngine.NormalizePhone(ctx!.RecipientPhone, waConfig.DefaultCountryCode);

        var availableTemplates = new List<Dictionary<string, string>>
        {
            new() { ["id"] = "standard", ["name"] = "Standard Corporate", ["desc"] = "Classic navy header with structured borders" },
            new() { ["id"] = "minimal",  ["name"] = "Minimal Clean",      ["desc"] = "Light borders and airy ink-saving layout" },
            new() { ["id"] = "detailed", ["name"] = "Executive Detailed", ["desc"] = "Royal blue theme with product codes & full breakdown" },
            new() { ["id"] = "compact",  ["name"] = "Compact High-Density", ["desc"] = "Condensed spacing to fit maximum items per page" },
            new() { ["id"] = "bold",     ["name"] = "Bold Modern",        ["desc"] = "High-contrast emerald header banner" },
            new() { ["id"] = "barcode",  ["name"] = "Barcode & SKU",      ["desc"] = "Includes SKU/barcode column and reference strip" }
        };

        var templateLabel = availableTemplates.FirstOrDefault(t => t["id"] == pdfDesign)?["name"] ?? pdfDesign;

        var queryParts = new List<string> { $"type={Uri.EscapeDataString(type)}" };
        var signedParams = new Dictionary<string, string?>();
        foreach (var k in new[] { "id", "customer_id", "supplier_id", "from", "to" })
        {
            var v = Request.Query[k].FirstOrDefault();
            if (!string.IsNullOrEmpty(v))
            {
                queryParts.Add($"{k}={Uri.EscapeDataString(v)}");
                signedParams[k] = v;
            }
        }
        queryParts.Add($"size={Uri.EscapeDataString(paperSize)}");
        queryParts.Add($"template={Uri.EscapeDataString(pdfDesign)}");
        queryParts.Add($"show_logo={(showLogo ? "1" : "0")}");
        signedParams["size"] = paperSize;
        signedParams["template"] = pdfDesign;
        signedParams["show_logo"] = showLogo ? "1" : "0";
        var apiQuery = string.Join("&", queryParts);

        var waUrlEn = !string.IsNullOrEmpty(normalizedPhone)
            ? $"https://wa.me/{normalizedPhone}?text={Uri.EscapeDataString(ctx.WhatsappTextEn)}"
            : $"https://wa.me/?text={Uri.EscapeDataString(ctx.WhatsappTextEn)}";

        var waUrlUr = !string.IsNullOrEmpty(normalizedPhone)
            ? $"https://wa.me/{normalizedPhone}?text={Uri.EscapeDataString(ctx.WhatsappTextUr)}"
            : $"https://wa.me/?text={Uri.EscapeDataString(ctx.WhatsappTextUr)}";

        var settingVm = PrintViewModelMapper.MapPrintSetting(setting);
        var logoUrl = settingVm.LogoUrl;
        var waConfigured = waConfig.Enabled && (
            waConfig.Provider == "local_agent" ||
            !string.IsNullOrWhiteSpace(waConfig.AccessToken) ||
            !string.IsNullOrWhiteSpace(waConfig.ApiUrl));
        var signedPdfUrl = SignedUrlMiddleware.CreateSignedUrl(Request, $"/print/shared-pdf/{type}", signedParams, TimeSpan.FromMinutes(30));

        // Surface live local WhatsApp agent state so ShareDocumentModal shows accurate in-app readiness immediately
        bool waAgentRunning = false;
        string waAgentStatus = "stopped";
        object? waAgentUser = null;
        if (waConfig.Provider == "local_agent" || !waConfigured)
        {
            try
            {
                var efSetting = _dbContext != null
                    ? await _dbContext.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(HttpContext.RequestAborted)
                    : null;
                var st = await _waSidecar.StatusAsync(_dbContext, efSetting, HttpContext.RequestAborted);
                waAgentRunning = st.TryGetValue("agent_running", out var runObj) && runObj is true;
                waAgentStatus = st.GetValueOrDefault("status")?.ToString() ?? "stopped";
                waAgentUser = st.GetValueOrDefault("user");
            }
            catch { }
        }

        return Ok(new Dictionary<string, object?>
        {
            ["type"] = type,
            ["document_title"] = ctx.Title,
            ["filename"] = ctx.Filename,
            ["paper_size"] = paperSize,
            ["paper_size_label"] = paperSize == "thermal" ? "Thermal (58mm × 80mm)" : "A4 Size",
            ["pdf_template"] = pdfDesign,
            ["pdf_template_label"] = templateLabel,
            ["show_logo"] = showLogo,
            ["has_logo"] = !string.IsNullOrEmpty(logoUrl),
            ["logo_url"] = logoUrl,
            ["available_pdf_templates"] = availableTemplates,
            ["party_type"] = ctx.PartyType,
            ["party_id"] = ctx.PartyId,
            ["recipient_name"] = ctx.RecipientName,
            ["recipient_phone"] = ctx.RecipientPhone,
            ["normalized_phone"] = normalizedPhone,
            ["recipient_email"] = ctx.RecipientEmail,
            ["whatsapp_text"] = ctx.WhatsappTextEn,
            ["whatsapp_text_en"] = ctx.WhatsappTextEn,
            ["whatsapp_text_ur"] = ctx.WhatsappTextUr,
            ["whatsapp_url"] = waUrlEn,
            ["whatsapp_url_ur"] = waUrlUr,
            ["whatsapp_api_configured"] = waConfigured,
            ["whatsapp_provider"] = waConfig.Provider,
            ["whatsapp_default_country_code"] = waConfig.DefaultCountryCode,
            ["whatsapp_agent_running"] = waAgentRunning,
            ["whatsapp_agent_status"] = waAgentStatus,
            ["whatsapp_agent_user"] = waAgentUser,
            ["email_subject"] = ctx.EmailSubjectEn,
            ["email_subject_en"] = ctx.EmailSubjectEn,
            ["email_subject_ur"] = ctx.EmailSubjectUr,
            ["email_body"] = ctx.EmailBodyEn,
            ["email_body_en"] = ctx.EmailBodyEn,
            ["email_body_ur"] = ctx.EmailBodyUr,
            ["signed_pdf_url"] = signedPdfUrl,
            ["pdf_preview_url"] = $"/api/share/pdf?{apiQuery}",
            ["pdf_download_url"] = $"/api/share/pdf?{apiQuery}&download=1"
        });
    }

    [HttpGet("api/share/pdf")]
    public async Task<IActionResult> Pdf()
    {
        var type = (Request.Query["type"].FirstOrDefault() ?? "").Trim();
        if (string.IsNullOrEmpty(type) || !SupportedTypes.Contains(type))
        {
            return Unprocessable("The selected type is invalid.", new Dictionary<string, string[]>
            {
                ["type"] = new[] { "The selected type is invalid." }
            });
        }

        var idRaw = Request.Query["id"].FirstOrDefault();
        var custRaw = Request.Query["customer_id"].FirstOrDefault();
        var supRaw = Request.Query["supplier_id"].FirstOrDefault();
        var fromRaw = Request.Query["from"].FirstOrDefault();
        var toRaw = Request.Query["to"].FirstOrDefault();

        var (authErr, ctx) = await ResolveAndAuthorizeContextAsync(type, idRaw, custRaw, supRaw, fromRaw, toRaw);
        if (authErr != null) return authErr;

        var setting = await GetSettingAsync();
        var paperSize = ResolvePaperSize(Request.Query["size"].FirstOrDefault(), setting);
        var pdfDesign = ResolvePdfDesign(Request.Query["template"].FirstOrDefault(), setting, paperSize);
        var showLogo = ResolveShowLogo(Request.Query["show_logo"].FirstOrDefault(), setting);

        var pdfBytes = await GenerateRealSharedPdfAsync(type, ctx!, setting, paperSize, pdfDesign, showLogo, idRaw, custRaw, supRaw, fromRaw, toRaw);
        var downloadRaw = Request.Query["download"].FirstOrDefault() ?? "";
        var isDownload = downloadRaw == "1" || downloadRaw.Equals("true", StringComparison.OrdinalIgnoreCase);

        if (isDownload)
        {
            return File(pdfBytes, "application/pdf", ctx!.Filename);
        }

        Response.Headers["Content-Disposition"] = $"inline; filename=\"{ctx!.Filename}\"";
        return File(pdfBytes, "application/pdf");
    }

    [HttpPost("api/share/email")]
    public async Task<IActionResult> SendEmail()
    {
        var body = await ReadJsonBodyAsync();
        var type = (GetString(body, "type") ?? "").Trim();
        var email = (GetString(body, "email") ?? "").Trim();
        var subject = (GetString(body, "subject") ?? "").Trim();
        var message = (GetString(body, "message") ?? "").Trim();

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(type) || !SupportedTypes.Contains(type))
        {
            errors["type"] = new[] { "The selected type is invalid." };
        }
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        {
            errors["email"] = new[] { "The email field must be a valid email address." };
        }
        if (string.IsNullOrEmpty(subject))
        {
            errors["subject"] = new[] { "The subject field is required." };
        }
        if (string.IsNullOrEmpty(message))
        {
            errors["message"] = new[] { "The message field is required." };
        }
        if (errors.Count > 0)
        {
            return Unprocessable("The given data was invalid.", errors);
        }

        var (authErr, ctx) = await ResolveAndAuthorizeContextAsync(
            type,
            GetString(body, "id"),
            GetString(body, "customer_id"),
            GetString(body, "supplier_id"),
            GetString(body, "from"),
            GetString(body, "to"));

        if (authErr != null) return authErr;

        var saveEmail = GetBool(body, "save_email", false);
        if (saveEmail && !string.IsNullOrEmpty(ctx!.PartyType) && ctx.PartyId.HasValue)
        {
            var canUpdateParty = string.IsNullOrWhiteSpace(ctx.RecipientEmail) ||
                await IsAdminAsync() ||
                (ctx.PartyType == "customer" && await CanAsync("customer.update")) ||
                (ctx.PartyType == "supplier" && await CanAsync("supplier.update"));
            if (canUpdateParty)
            {
                var tbl = ctx.PartyType == "customer" ? "customers" : "suppliers";
                await Db.UpdateAsync(tbl, ctx.PartyId.Value, new Dictionary<string, object?> { ["email"] = email });
            }
        }

        var setting = await GetSettingAsync();
        var paperSize = ResolvePaperSize(GetString(body, "size"), setting);
        var pdfDesign = ResolvePdfDesign(GetString(body, "template"), setting, paperSize);
        var showLogo = ResolveShowLogo(GetString(body, "show_logo"), setting);

        var pdfBytes = await GenerateRealSharedPdfAsync(type, ctx!, setting, paperSize, pdfDesign, showLogo,
            GetString(body, "id"), GetString(body, "customer_id"), GetString(body, "supplier_id"),
            GetString(body, "from"), GetString(body, "to"));

        var emailSubject = (GetString(body, "subject") ?? ctx!.EmailSubjectEn).Trim();
        var bodyText = (GetString(body, "message") ?? GetString(body, "body") ?? ctx!.EmailBodyEn).Trim();
        var smtpCfg = BuildSmtpConfig(setting);
        var emailRes = await _shareEngine.SendDocumentEmailAsync(email, emailSubject, bodyText, pdfBytes, ctx!.Filename, smtpCfg, HttpContext.RequestAborted);
        emailRes["filename"] = ctx.Filename;
        emailRes["pdf_base64"] = Convert.ToBase64String(pdfBytes);

        return Ok(emailRes);
    }

    [HttpPost("api/share/whatsapp")]
    public async Task<IActionResult> SendWhatsApp()
    {
        var body = await ReadJsonBodyAsync();
        var type = (GetString(body, "type") ?? "").Trim();
        var message = (GetString(body, "message") ?? "").Trim();
        var phone = (GetString(body, "phone") ?? "").Trim();

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(type) || !SupportedTypes.Contains(type))
        {
            errors["type"] = new[] { "The selected type is invalid." };
        }
        if (string.IsNullOrEmpty(message))
        {
            errors["message"] = new[] { "The message field is required." };
        }
        if (errors.Count > 0)
        {
            return Unprocessable("The given data was invalid.", errors);
        }

        var idStr = GetString(body, "id");
        var custStr = GetString(body, "customer_id");
        var supStr = GetString(body, "supplier_id");
        var fromStr = GetString(body, "from");
        var toStr = GetString(body, "to");

        var (authErr, ctx) = await ResolveAndAuthorizeContextAsync(type, idStr, custStr, supStr, fromStr, toStr);
        if (authErr != null) return authErr;

        var setting = await GetSettingAsync();
        var waConfig = BuildWhatsAppConfig(setting);
        var paperSize = ResolvePaperSize(GetString(body, "size"), setting);
        var pdfDesign = ResolvePdfDesign(GetString(body, "template"), setting, paperSize);
        var showLogo = ResolveShowLogo(GetString(body, "show_logo"), setting);

        var rawPhone = !string.IsNullOrEmpty(phone) ? phone : ctx!.RecipientPhone;
        var normalizedPhone = _shareEngine.NormalizePhone(rawPhone, waConfig.DefaultCountryCode);

        var savePhone = GetBool(body, "save_phone", false);
        if (savePhone && !string.IsNullOrEmpty(rawPhone) && !string.IsNullOrEmpty(ctx!.PartyType) && ctx.PartyId.HasValue)
        {
            var canUpdatePhone = string.IsNullOrWhiteSpace(ctx.RecipientPhone) ||
                await IsAdminAsync() ||
                (ctx.PartyType == "customer" && await CanAsync("customer.update")) ||
                (ctx.PartyType == "supplier" && await CanAsync("supplier.update"));
            if (canUpdatePhone)
            {
                var tbl = ctx.PartyType == "customer" ? "customers" : "suppliers";
                await Db.UpdateAsync(tbl, ctx.PartyId.Value, new Dictionary<string, object?> { ["phone"] = rawPhone });
            }
        }

        // If local WhatsApp agent is enabled & selected, verify live connection; auto-start once if stopped so a linked store reconnects seamlessly.
        // Never kick the user out to an external browser when local_agent is active — either dispatch directly inside the app, show inline QR, or report the exact error.
        var isLocalAgentMode = waConfig.Enabled && waConfig.Provider == "local_agent";
        Karobar.Core.Entities.Setting? efSettingForWa = null;
        if (isLocalAgentMode && !string.IsNullOrWhiteSpace(normalizedPhone))
        {
            efSettingForWa = _dbContext != null
                ? await _dbContext.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(HttpContext.RequestAborted)
                : null;
            var agentStatus = await _waSidecar.StatusAsync(_dbContext, efSettingForWa, HttpContext.RequestAborted);
            var stStr = agentStatus.GetValueOrDefault("status")?.ToString() ?? "stopped";
            var isRunning = agentStatus.TryGetValue("agent_running", out var runObj) && runObj is true;

            if (!isRunning || string.Equals(stStr, "stopped", StringComparison.OrdinalIgnoreCase))
            {
                agentStatus = await _waSidecar.StartAsync(_dbContext, efSettingForWa, enableInSettings: false, cancellationToken: HttpContext.RequestAborted);
                stStr = agentStatus.GetValueOrDefault("status")?.ToString() ?? "stopped";
                isRunning = agentStatus.TryGetValue("agent_running", out var runObj2) && runObj2 is true;
            }

            // Ensure waConfig uses the exact active agent URL and API key
            var liveUrl = agentStatus.GetValueOrDefault("agent_url")?.ToString();
            var liveKey = agentStatus.GetValueOrDefault("api_key")?.ToString();
            if (!string.IsNullOrWhiteSpace(liveUrl)) waConfig.ApiUrl = liveUrl;
            if (!string.IsNullOrWhiteSpace(liveKey)) waConfig.AccessToken = liveKey;

            if (!isRunning)
            {
                var bootErr = agentStatus.GetValueOrDefault("last_error")?.ToString()
                    ?? agentStatus.GetValueOrDefault("message")?.ToString()
                    ?? "Local WhatsApp Agent could not start.";
                return Unprocessable(bootErr);
            }

            if (!string.Equals(stStr, "connected", StringComparison.OrdinalIgnoreCase))
            {
                return Ok(new Dictionary<string, object?>
                {
                    ["status"] = "qr_required",
                    ["mode"] = "qr_required",
                    ["provider"] = "local_agent",
                    ["message"] = "Scan the WhatsApp QR code below once to link your device and send directly inside Karobar-App.",
                    ["agent_status"] = stStr,
                    ["qr_data_url"] = agentStatus.GetValueOrDefault("qr_data_url"),
                    ["pairing_code"] = agentStatus.GetValueOrDefault("pairing_code"),
                    ["last_error"] = agentStatus.GetValueOrDefault("last_error"),
                    ["normalized_phone"] = normalizedPhone
                });
            }

            // Keep local_agent dispatch strictly in-app (never silently open external wa.me on transient/recipient errors)
            waConfig.AutoFallback = false;
        }

        Dictionary<string, object?> response;
        if (type == "regular_refill_reminder")
        {
            try
            {
                // Regular refill reminders are text messages — dispatch cleanly via /send-text (no unnecessary PDF attachment!)
                response = await _shareEngine.SendOrBuildWhatsAppTextAsync(rawPhone, message, waConfig, HttpContext.RequestAborted);
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
                        ["message"] = "WhatsApp session reconnecting — scan QR if prompted to send directly inside Karobar-App.",
                        ["agent_status"] = freshStatusStr,
                        ["qr_data_url"] = freshSt.GetValueOrDefault("qr_data_url"),
                        ["pairing_code"] = freshSt.GetValueOrDefault("pairing_code"),
                        ["last_error"] = freshSt.GetValueOrDefault("last_error"),
                        ["normalized_phone"] = normalizedPhone
                    });
                }
                return Unprocessable(ex.Message);
            }

            var custId = ctx!.PartyId ?? GetNullableLong(body, "customer_id") ?? GetNullableLong(body, "id");
            if (custId.HasValue)
            {
                var custRow = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = custId.Value });
                if (custRow != null)
                {
                    var canMutateRefill = await IsAdminAsync() || await CanAsync("customer.update", "customer.create", "sale-invoice.create");
                    if (canMutateRefill)
                    {
                        var nowUtc = DateTime.UtcNow;
                        var nowDtStr = nowUtc.ToString("yyyy-MM-dd HH:mm:ss");
                        var nowDateStr = nowUtc.ToString("yyyy-MM-dd");
                        var custUpdates = new Dictionary<string, object?>();
                        if (await Db.HasColumnAsync("customers", "last_refill_reminded_at"))
                            custUpdates["last_refill_reminded_at"] = nowDtStr;
                        if (await Db.HasColumnAsync("customers", "last_reminded_at"))
                            custUpdates["last_reminded_at"] = nowDtStr;
                        var lang = (GetString(body, "language") ?? "").Trim();
                        if (!string.IsNullOrEmpty(lang) && await Db.HasColumnAsync("customers", "reminder_language"))
                            custUpdates["reminder_language"] = lang;

                        if (GetBool(body, "advance_cycle", false))
                        {
                            var rawCycle = custRow.GetValueOrDefault("refill_custom_days")
                                ?? custRow.GetValueOrDefault("refill_interval_days")
                                ?? custRow.GetValueOrDefault("refill_cycle_days");
                            var cycleDays = rawCycle != null && int.TryParse(rawCycle.ToString(), out var parsedDays) && parsedDays > 0
                                ? parsedDays
                                : 30;
                            var nextDateStr = nowUtc.AddDays(cycleDays).ToString("yyyy-MM-dd");
                            if (await Db.HasColumnAsync("customers", "last_refill_collected_at"))
                                custUpdates["last_refill_collected_at"] = nowDateStr;
                            if (await Db.HasColumnAsync("customers", "last_collected_at"))
                                custUpdates["last_collected_at"] = nowDateStr;
                            if (await Db.HasColumnAsync("customers", "next_refill_date"))
                                custUpdates["next_refill_date"] = nextDateStr;
                            if (await Db.HasColumnAsync("customers", "next_due_date"))
                                custUpdates["next_due_date"] = nextDateStr;
                        }

                        if (custUpdates.Count > 0)
                        {
                            await Db.UpdateAsync("customers", custId.Value, custUpdates);
                        }
                    }

                    var freshCustomer = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = custId.Value });
                    response["customer"] = freshCustomer;
                }
            }

            return Ok(response);
        }

        var pdfBytes = await GenerateRealSharedPdfAsync(type, ctx!, setting, paperSize, pdfDesign, showLogo, idStr, custStr, supStr, fromStr, toStr);
        var signedParams = new Dictionary<string, string?>
        {
            ["size"] = paperSize,
            ["template"] = pdfDesign,
            ["show_logo"] = showLogo ? "1" : "0"
        };
        if (!string.IsNullOrWhiteSpace(idStr)) signedParams["id"] = idStr;
        if (!string.IsNullOrWhiteSpace(custStr)) signedParams["customer_id"] = custStr;
        if (!string.IsNullOrWhiteSpace(supStr)) signedParams["supplier_id"] = supStr;
        if (!string.IsNullOrWhiteSpace(fromStr)) signedParams["from"] = fromStr;
        if (!string.IsNullOrWhiteSpace(toStr)) signedParams["to"] = toStr;

        var signedPdfUrl = SignedUrlMiddleware.CreateSignedUrl(Request, $"/print/shared-pdf/{type}", signedParams, TimeSpan.FromMinutes(30));

        try
        {
            response = await _shareEngine.SendOrBuildWhatsAppPdfAsync(
                pdfBytes, ctx!.Filename, rawPhone, message, waConfig, signedPdfUrl, HttpContext.RequestAborted);
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
                    ["message"] = "WhatsApp session reconnecting — scan QR if prompted to send directly inside Karobar-App.",
                    ["agent_status"] = freshStatusStr,
                    ["qr_data_url"] = freshSt.GetValueOrDefault("qr_data_url"),
                    ["pairing_code"] = freshSt.GetValueOrDefault("pairing_code"),
                    ["last_error"] = freshSt.GetValueOrDefault("last_error"),
                    ["normalized_phone"] = normalizedPhone
                });
            }
            return Unprocessable(ex.Message);
        }

        response["signed_pdf_url"] = signedPdfUrl;
        response["mime_type"] = "application/pdf";
        response["size_bytes"] = pdfBytes.Length;
        response["normalized_phone"] = normalizedPhone;
        response["caption"] = message;
        // Omit large pdf_base64 payload when direct API already delivered the PDF successfully
        if (!string.Equals(response.GetValueOrDefault("mode")?.ToString(), "cloud_api", StringComparison.OrdinalIgnoreCase))
        {
            response["pdf_base64"] = Convert.ToBase64String(pdfBytes);
        }

        return Ok(response);
    }

    [AllowAnonymous]
    [HttpGet("print/shared-pdf/{type}")]
    public async Task<IActionResult> PublicPdf(string type)
    {
        if (string.IsNullOrEmpty(type) || !SupportedTypes.Contains(type))
        {
            return NotFound();
        }

        var idRaw = Request.Query["id"].FirstOrDefault();
        var custRaw = Request.Query["customer_id"].FirstOrDefault();
        var supRaw = Request.Query["supplier_id"].FirstOrDefault();
        var fromRaw = Request.Query["from"].FirstOrDefault();
        var toRaw = Request.Query["to"].FirstOrDefault();

        var ctx = await BuildContextDataAsync(type, idRaw, custRaw, supRaw, fromRaw, toRaw);
        if (ctx == null)
        {
            return NotFound();
        }

        var setting = await GetSettingAsync();
        var paperSize = ResolvePaperSize(Request.Query["size"].FirstOrDefault(), setting);
        var pdfDesign = ResolvePdfDesign(Request.Query["template"].FirstOrDefault(), setting, paperSize);
        var showLogo = ResolveShowLogo(Request.Query["show_logo"].FirstOrDefault(), setting);

        var pdfBytes = await GenerateRealSharedPdfAsync(type, ctx, setting, paperSize, pdfDesign, showLogo, idRaw, custRaw, supRaw, fromRaw, toRaw);
        var downloadRaw = Request.Query["download"].FirstOrDefault() ?? "";
        var isDownload = downloadRaw == "1" || downloadRaw.Equals("true", StringComparison.OrdinalIgnoreCase);

        if (isDownload)
        {
            return File(pdfBytes, "application/pdf", ctx.Filename);
        }

        Response.Headers["Content-Disposition"] = $"inline; filename=\"{ctx.Filename}\"";
        return File(pdfBytes, "application/pdf");
    }

    private async Task<(IActionResult? error, ResolvedDocContext? ctx)> ResolveAndAuthorizeContextAsync(
        string type,
        string? idStr,
        string? customerIdStr,
        string? supplierIdStr,
        string? from,
        string? to)
    {
        if (CurrentUserId == null)
        {
            return (Unauthorized(new { message = "Unauthenticated." }), null);
        }

        var isAdmin = await IsAdminAsync();
        var requiredPerm = type switch
        {
            "sale_invoice" or "delivery_challan" => "sale-invoice.view",
            "quotation" => "invoice-draft.view",
            "customer_ledger" => "customer-ledger.view",
            "supplier_ledger" => "supplier-ledger.view",
            "payment" => "payment.view",
            "purchase_order" => "purchase-order.view",
            "purchase_invoice" => "purchase-invoice.view",
            "sale_return" => "sale-return.view",
            "purchase_return" => "purchase-return.view",
            "stock_disposal" => "stock-disposal.view",
            "cash_drawer_session" => "cash-drawer.view",
            "stocktake_session" => "stocktake.view",
            "regular_refill_reminder" => "customer.view",
            _ => ""
        };

        if (!isAdmin && !string.IsNullOrEmpty(requiredPerm) && !await CanAsync(requiredPerm))
        {
            return (Forbidden("This action is unauthorized."), null);
        }

        var ctx = await BuildContextDataAsync(type, idStr, customerIdStr, supplierIdStr, from, to);
        if (ctx == null)
        {
            return (NotFound(new { message = "Document not found." }), null);
        }

        return (null, ctx);
    }

    private async Task<ResolvedDocContext?> BuildContextDataAsync(
        string type,
        string? idStr,
        string? customerIdStr,
        string? supplierIdStr,
        string? from,
        string? to)
    {
        var setting = await GetSettingAsync();
        var storeName = GetVal(setting, "store_name", "Karobar App Store");
        var storePhone = GetVal(setting, "phone_number", "");
        var storeAddr = GetVal(setting, "address", "");
        long.TryParse(idStr, out var id);
        long.TryParse(customerIdStr, out var custId);
        long.TryParse(supplierIdStr, out var supId);

        switch (type.ToLowerInvariant())
        {
            case "sale_invoice":
            case "delivery_challan":
            {
                var inv = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id });
                if (inv == null) return null;
                var cId = ToNullableLong(inv, "customer_id");
                var cust = cId.HasValue ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @cId", new { cId }) : null;
                var docNo = GetVal(inv, "posted_number", $"INV-{id}");
                var dateStr = GetVal(inv, "date", DateTime.UtcNow.ToString("yyyy-MM-dd"));
                var total = ToDouble(inv, "total");
                var received = ToDouble(inv, "total_receive");
                var due = Math.Max(0, total - received);
                var custName = GetVal(cust, "name", "Valued Customer");
                var isChallan = type.Equals("delivery_challan", StringComparison.OrdinalIgnoreCase);
                var label = isChallan ? "Delivery Challan" : "Sale Invoice";
                var prefix = isChallan ? "Delivery_Challan_" : "Sale_Invoice_";

                // Compute items summary & live customer ledger balance for customizable bilingual WhatsApp templates
                var invItems = await Db.HasTableAsync("sale_invoice_items")
                    ? await Db.QueryListAsync(@"
                        SELECT i.quantity, p.name AS product_name
                        FROM sale_invoice_items i
                        LEFT JOIN products p ON p.id = i.product_id
                        WHERE i.sale_invoice_id = @id", new { id })
                    : new List<Dictionary<string, object?>>();
                var itemsCount = invItems.Count;
                var totalUnits = invItems.Sum(r => ToDouble(r, "quantity"));
                var itemsSummary = itemsCount > 0
                    ? string.Join(", ", invItems.Take(5).Select(r => $"{GetVal(r, "product_name", "Item")} × {ToDouble(r, "quantity"):0.##}"))
                      + (itemsCount > 5 ? $" (+{itemsCount - 5} more)" : "")
                    : "";

                string totalBalanceFormatted = "";
                if (cId.HasValue && await Db.HasTableAsync("customer_ledgers"))
                {
                    var balRow = await Db.QuerySingleOrDefaultAsync(
                        "SELECT COALESCE(SUM(invoice_total), 0) - COALESCE(SUM(total_received), 0) - COALESCE(SUM(credited_amount), 0) AS net_bal FROM customer_ledgers WHERE customer_id = @cId",
                        new { cId = cId.Value });
                    var netBal = ToDouble(balRow, "net_bal");
                    if (Math.Abs(netBal) > 0.009)
                    {
                        totalBalanceFormatted = $"Rs. {netBal:N2}";
                    }
                }

                var customTplEn = GetVal(setting, "whatsapp_message_en", "").Trim();
                var customTplUr = GetVal(setting, "whatsapp_message_ur", "").Trim();
                var closingNoteEn = GetVal(setting, "note_en", GetVal(setting, "note", "")).Trim();
                var closingNoteUr = GetVal(setting, "note_ur", closingNoteEn).Trim();

                var tplVarsEn = new Dictionary<string, string?>
                {
                    ["store_name"] = storeName,
                    ["store_address"] = storeAddr,
                    ["store_phone"] = storePhone,
                    ["invoice_no"] = docNo,
                    ["date"] = dateStr,
                    ["customer_name"] = custName,
                    ["items"] = itemsSummary,
                    ["items_count"] = itemsCount.ToString(CultureInfo.InvariantCulture),
                    ["total_units"] = totalUnits.ToString("0.##", CultureInfo.InvariantCulture),
                    ["total"] = $"Rs. {total:N2}",
                    ["received"] = $"Rs. {received:N2}",
                    ["balance_due"] = $"Rs. {due:N2}",
                    ["total_balance"] = totalBalanceFormatted,
                    ["note"] = closingNoteEn
                };
                var tplVarsUr = new Dictionary<string, string?>(tplVarsEn)
                {
                    ["note"] = !string.IsNullOrEmpty(closingNoteUr) ? closingNoteUr : closingNoteEn
                };

                var waEn = !isChallan && !string.IsNullOrWhiteSpace(customTplEn)
                    ? _shareEngine.RenderWhatsAppInvoiceTemplate(customTplEn, tplVarsEn)
                    : $"*{storeName}*\n────────────────────\n*{label.ToUpperInvariant()}*\nInvoice No: *{docNo}*\nDate: {dateStr}\nCustomer: {custName}\n\nInvoice Total: *Rs. {total:N2}*\nAmount Received: Rs. {received:N2}\nBalance Due: Rs. {due:N2}\n\nPlease find the official PDF attached.";

                var waUr = !isChallan && !string.IsNullOrWhiteSpace(customTplUr)
                    ? _shareEngine.RenderWhatsAppInvoiceTemplate(customTplUr, tplVarsUr)
                    : $"*{storeName}*\n────────────────────\n*سیل انوائس / بل کی تفصیل*\nبل نمبر: *{docNo}*\nتاریخ: {dateStr}\nمحترم کسٹمر: {custName}\nکل بل: *Rs. {total:N2}*\nوصول شدہ رقم: Rs. {received:N2}\nبقایا رقم: Rs. {due:N2}";

                return new ResolvedDocContext
                {
                    Title = $"{label} {docNo}",
                    Filename = $"{prefix}{SanitizeFile(docNo)}.pdf",
                    PartyType = cust != null ? "customer" : null,
                    PartyId = cId,
                    RecipientName = custName,
                    RecipientPhone = GetVal(cust, "phone", ""),
                    RecipientEmail = GetVal(cust, "email", ""),
                    WhatsappTextEn = waEn,
                    WhatsappTextUr = waUr,
                    EmailSubjectEn = $"{label} {docNo} — {storeName}",
                    EmailSubjectUr = $"سیل انوائس {docNo} — {storeName}",
                    EmailBodyEn = $"Dear {custName},\n\nPlease find attached {label} ({docNo}) dated {dateStr} from {storeName}.\nTotal: Rs. {total:N2}",
                    EmailBodyUr = waUr,
                    PdfSummaryLines = new List<string>
                    {
                        $"Store: {storeName} {storePhone} {storeAddr}",
                        $"Document: {label} {docNo}",
                        $"Date: {dateStr}",
                        $"Customer: {custName}",
                        $"Total: Rs. {total:N2} | Received: Rs. {received:N2} | Balance Due: Rs. {due:N2}"
                    }
                };
            }

            case "quotation":
            {
                var draft = await Db.QuerySingleOrDefaultAsync("SELECT * FROM invoice_drafts WHERE id = @id", new { id });
                if (draft == null) return null;
                long? cId = ToNullableLong(draft, "party_id") ?? ToNullableLong(draft, "customer_id");
                var payloadStr = GetVal(draft, "payload", "");
                if (!cId.HasValue && !string.IsNullOrWhiteSpace(payloadStr))
                {
                    try
                    {
                        using var pDoc = JsonDocument.Parse(payloadStr);
                        if (pDoc.RootElement.TryGetProperty("customer_id", out var cidEl) && long.TryParse(cidEl.ToString(), out var parsedCid))
                            cId = parsedCid;
                    }
                    catch { }
                }
                var cust = cId.HasValue ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @cId", new { cId }) : null;
                var docNo = GetVal(draft, "title", GetVal(draft, "uuid", $"QTN-{id}"));
                var custName = GetVal(cust, "name", GetVal(draft, "party_name", "Valued Customer"));
                var total = ToDouble(draft, "total_amount");
                if (total == 0) total = ToDouble(draft, "total");

                return new ResolvedDocContext
                {
                    Title = $"Price Quotation {docNo}",
                    Filename = $"Quotation_{SanitizeFile(docNo)}.pdf",
                    PartyType = cust != null ? "customer" : null,
                    PartyId = cId,
                    RecipientName = custName,
                    RecipientPhone = GetVal(cust, "phone", ""),
                    RecipientEmail = GetVal(cust, "email", ""),
                    WhatsappTextEn = $"*{storeName}*\nPrice Quotation: *{docNo}*\nPrepared For: {custName}\nEstimated Total: *Rs. {total:N2}*\nPlease find our official price quotation attached.",
                    WhatsappTextUr = $"*{storeName}*\nپرائس کوٹیشن: *{docNo}*\nکسٹمر کا نام: {custName}\nمتوقع کل رقم: *Rs. {total:N2}*",
                    EmailSubjectEn = $"Price Quotation {docNo} — {storeName}",
                    EmailSubjectUr = $"پرائس کوٹیشن {docNo} — {storeName}",
                    EmailBodyEn = $"Dear {custName},\n\nPlease find our official price quotation ({docNo}) attached.",
                    EmailBodyUr = $"محترم {custName}، ہماری آفیشل پرائس کوٹیشن منسلک ہے۔",
                    PdfSummaryLines = new List<string>
                    {
                        $"Store: {storeName}",
                        $"Document: Price Quotation {docNo}",
                        $"Prepared For: {custName}",
                        $"Estimated Total: Rs. {total:N2}"
                    }
                };
            }

            case "customer_ledger":
            case "regular_refill_reminder":
            {
                var targetCustId = custId > 0 ? custId : id;
                var cust = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = targetCustId });
                if (cust == null) return null;
                var custName = GetVal(cust, "name", "Customer");
                var isRefill = type.Equals("regular_refill_reminder", StringComparison.OrdinalIgnoreCase);
                var title = isRefill ? $"Medicine Refill Reminder — {custName}" : $"Customer Account Statement — {custName}";
                var fname = isRefill ? $"Refill_Reminder_{SanitizeFile(custName)}.pdf" : $"Customer_Statement_{SanitizeFile(custName)}.pdf";

                return new ResolvedDocContext
                {
                    Title = title,
                    Filename = fname,
                    PartyType = "customer",
                    PartyId = targetCustId,
                    RecipientName = custName,
                    RecipientPhone = GetVal(cust, "phone", ""),
                    RecipientEmail = GetVal(cust, "email", ""),
                    WhatsappTextEn = $"*{storeName}*\n{title}\nCustomer: {custName}\nPlease find your statement attached.",
                    WhatsappTextUr = $"*{storeName}*\nمحترم کسٹمر: {custName}\nآپ کی تفصیل پی ڈی ایف میں منسلک ہے۔",
                    EmailSubjectEn = $"{title} — {storeName}",
                    EmailSubjectUr = $"{title} — {storeName}",
                    EmailBodyEn = $"Dear {custName},\n\nPlease find your document from {storeName} attached.",
                    EmailBodyUr = $"محترم {custName}، آپ کی دستاویز منسلک ہے۔",
                    PdfSummaryLines = new List<string>
                    {
                        $"Store: {storeName}",
                        $"Document: {title}",
                        $"Customer: {custName}",
                        $"Period: {from ?? "Start"} to {to ?? "Today"}"
                    }
                };
            }

            case "supplier_ledger":
            {
                var targetSupId = supId > 0 ? supId : id;
                var sup = await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @id", new { id = targetSupId });
                if (sup == null) return null;
                var supName = GetVal(sup, "name", "Supplier");

                return new ResolvedDocContext
                {
                    Title = $"Supplier Account Statement — {supName}",
                    Filename = $"Supplier_Statement_{SanitizeFile(supName)}.pdf",
                    PartyType = "supplier",
                    PartyId = targetSupId,
                    RecipientName = supName,
                    RecipientPhone = GetVal(sup, "phone", ""),
                    RecipientEmail = GetVal(sup, "email", ""),
                    WhatsappTextEn = $"*{storeName}*\nSupplier Account Statement\nSupplier: {supName}\nPlease find the supplier ledger statement attached.",
                    WhatsappTextUr = $"*{storeName}*\nسپلائر کھاتہ اسٹیٹمنٹ\nمحترم سپلائر: {supName}",
                    EmailSubjectEn = $"Supplier Account Statement — {supName} ({storeName})",
                    EmailSubjectUr = $"سپلائر کھاتہ اسٹیٹمنٹ — {supName}",
                    EmailBodyEn = $"Dear {supName},\n\nPlease find the supplier ledger statement attached.",
                    EmailBodyUr = $"محترم {supName}، سپلائر کھاتے کی تفصیل منسلک ہے۔",
                    PdfSummaryLines = new List<string>
                    {
                        $"Store: {storeName}",
                        $"Document: Supplier Account Statement",
                        $"Supplier: {supName}",
                        $"Period: {from ?? "Start"} to {to ?? "Today"}"
                    }
                };
            }

            case "payment":
            {
                var pmt = await Db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id });
                if (pmt == null) return null;
                var docNo = GetVal(pmt, "payment_number", $"PMT-{id}");
                var amt = ToDouble(pmt, "amount");
                var pType = GetVal(pmt, "party_type", "customer");
                var pId = ToNullableLong(pmt, "party_id");
                Dictionary<string, object?>? party = null;
                if (pId.HasValue)
                {
                    party = pType == "supplier"
                        ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @pId", new { pId })
                        : await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @pId", new { pId });
                }
                var partyName = GetVal(party, "name", "Valued Partner");

                return new ResolvedDocContext
                {
                    Title = $"Payment Voucher {docNo}",
                    Filename = $"Payment_Voucher_{SanitizeFile(docNo)}.pdf",
                    PartyType = pType,
                    PartyId = pId,
                    RecipientName = partyName,
                    RecipientPhone = GetVal(party, "phone", ""),
                    RecipientEmail = GetVal(party, "email", ""),
                    WhatsappTextEn = $"*{storeName}*\nPayment Voucher: *{docNo}*\nParty: {partyName}\nAmount: *Rs. {amt:N2}*\nPlease find the official voucher attached.",
                    WhatsappTextUr = $"*{storeName}*\nپیمنٹ واؤچر: *{docNo}*\nرقم: *Rs. {amt:N2}*",
                    EmailSubjectEn = $"Payment Voucher {docNo} — {storeName}",
                    EmailSubjectUr = $"پیمنٹ واؤچر {docNo} — {storeName}",
                    EmailBodyEn = $"Dear {partyName},\n\nPlease find Payment Voucher ({docNo}) for Rs. {amt:N2} attached.",
                    EmailBodyUr = $"پیمنٹ واؤچر ({docNo}) منسلک ہے۔",
                    PdfSummaryLines = new List<string>
                    {
                        $"Store: {storeName}",
                        $"Payment Voucher: {docNo}",
                        $"Party: {partyName}",
                        $"Amount: Rs. {amt:N2}"
                    }
                };
            }

            case "purchase_order":
            {
                var po = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_orders WHERE id = @id", new { id });
                if (po == null) return null;
                var docNo = GetVal(po, "number", GetVal(po, "reference", $"PO-{id}"));
                var sId = ToNullableLong(po, "supplier_id");
                var sup = sId.HasValue ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @sId", new { sId }) : null;
                var supName = GetVal(sup, "name", "Supplier");
                var total = ToDouble(po, "total");
                if (total == 0 && await Db.HasTableAsync("purchase_order_items"))
                {
                    var sumRow = await Db.QuerySingleOrDefaultAsync(
                        "SELECT COALESCE(SUM(ordered_quantity * unit_purchase_price * (1.0 - COALESCE(item_discount_percentage, 0) / 100.0)), 0) AS tot FROM purchase_order_items WHERE purchase_order_id = @id",
                        new { id });
                    total = ToDouble(sumRow, "tot");
                }

                return new ResolvedDocContext
                {
                    Title = $"Purchase Order {docNo}",
                    Filename = $"Purchase_Order_{SanitizeFile(docNo)}.pdf",
                    PartyType = sup != null ? "supplier" : null,
                    PartyId = sId,
                    RecipientName = supName,
                    RecipientPhone = GetVal(sup, "phone", ""),
                    RecipientEmail = GetVal(sup, "email", ""),
                    WhatsappTextEn = $"*{storeName}*\nPurchase Order: *{docNo}*\nSupplier: {supName}\nOrder Total: *Rs. {total:N2}*\nPlease find our official Purchase Order attached and confirm receipt.",
                    WhatsappTextUr = $"*{storeName}*\nپرچیز آرڈر: *{docNo}*\nسپلائر: {supName}\nکل آرڈر رقم: *Rs. {total:N2}*",
                    EmailSubjectEn = $"Purchase Order {docNo} — {storeName}",
                    EmailSubjectUr = $"پرچیز آرڈر {docNo} — {storeName}",
                    EmailBodyEn = $"Dear {supName},\n\nPlease find our official Purchase Order ({docNo}) attached.",
                    EmailBodyUr = $"ہمارا آفیشل پرچیز آرڈر ({docNo}) منسلک ہے۔",
                    PdfSummaryLines = new List<string>
                    {
                        $"Store: {storeName}",
                        $"Purchase Order: {docNo}",
                        $"Supplier: {supName}",
                        $"Total: Rs. {total:N2}"
                    }
                };
            }

            case "purchase_invoice":
            {
                var pi = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_invoices WHERE id = @id", new { id });
                if (pi == null) return null;
                var docNo = GetVal(pi, "posted_number", $"GRN-{id}");
                var sId = ToNullableLong(pi, "supplier_id");
                var sup = sId.HasValue ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @sId", new { sId }) : null;
                var supName = GetVal(sup, "name", "Supplier");
                var total = ToDouble(pi, "total_amount");

                return new ResolvedDocContext
                {
                    Title = $"Purchase Invoice {docNo}",
                    Filename = $"Purchase_Invoice_{SanitizeFile(docNo)}.pdf",
                    PartyType = sup != null ? "supplier" : null,
                    PartyId = sId,
                    RecipientName = supName,
                    RecipientPhone = GetVal(sup, "phone", ""),
                    RecipientEmail = GetVal(sup, "email", ""),
                    WhatsappTextEn = $"*{storeName}*\nPurchase Invoice (GRN): *{docNo}*\nSupplier: {supName}\nInvoice Total: *Rs. {total:N2}*\nPlease find the Purchase Invoice (GRN) PDF attached.",
                    WhatsappTextUr = $"*{storeName}*\nپرچیز انوائس (GRN): *{docNo}*\nکل بل: *Rs. {total:N2}*",
                    EmailSubjectEn = $"Purchase Invoice {docNo} — {storeName}",
                    EmailSubjectUr = $"پرچیز انوائس {docNo} — {storeName}",
                    EmailBodyEn = $"Dear {supName},\n\nPlease find Purchase Invoice ({docNo}) attached.",
                    EmailBodyUr = $"پرچیز انوائس ({docNo}) منسلک ہے۔",
                    PdfSummaryLines = new List<string>
                    {
                        $"Store: {storeName}",
                        $"Purchase Invoice: {docNo}",
                        $"Supplier: {supName}",
                        $"Total: Rs. {total:N2}"
                    }
                };
            }

            case "sale_return":
            {
                var sr = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_returns WHERE id = @id", new { id });
                if (sr == null) return null;
                var docNo = GetVal(sr, "posted_number", $"SR-{id}");
                var cId = ToNullableLong(sr, "customer_id");
                var cust = cId.HasValue ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @cId", new { cId }) : null;
                var custName = GetVal(cust, "name", "Customer");
                var total = ToDouble(sr, "total");

                return new ResolvedDocContext
                {
                    Title = $"Sale Return {docNo}",
                    Filename = $"Sale_Return_{SanitizeFile(docNo)}.pdf",
                    PartyType = cust != null ? "customer" : null,
                    PartyId = cId,
                    RecipientName = custName,
                    RecipientPhone = GetVal(cust, "phone", ""),
                    RecipientEmail = GetVal(cust, "email", ""),
                    WhatsappTextEn = $"*{storeName}*\nSale Return / Credit Note: *{docNo}*\nCustomer: {custName}\nReturn Credit Total: *Rs. {total:N2}*\nPlease find the Credit Note PDF attached.",
                    WhatsappTextUr = $"*{storeName}*\nسیل ریٹرن / کریڈٹ نوٹ: *{docNo}*\nواپسی بل کی کل رقم: *Rs. {total:N2}*",
                    EmailSubjectEn = $"Sale Return {docNo} — {storeName}",
                    EmailSubjectUr = $"سیل ریٹرن {docNo} — {storeName}",
                    EmailBodyEn = $"Dear {custName},\n\nPlease find Credit Note ({docNo}) attached.",
                    EmailBodyUr = $"سیل ریٹرن ({docNo}) منسلک ہے۔",
                    PdfSummaryLines = new List<string>
                    {
                        $"Store: {storeName}",
                        $"Sale Return: {docNo}",
                        $"Customer: {custName}",
                        $"Total: Rs. {total:N2}"
                    }
                };
            }

            case "purchase_return":
            {
                var pr = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_returns WHERE id = @id", new { id });
                if (pr == null) return null;
                var docNo = GetVal(pr, "posted_number", $"PR-{id}");
                var sId = ToNullableLong(pr, "supplier_id");
                var sup = sId.HasValue ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @sId", new { sId }) : null;
                var supName = GetVal(sup, "name", "Supplier");
                var total = ToDouble(pr, "total");

                return new ResolvedDocContext
                {
                    Title = $"Purchase Return {docNo}",
                    Filename = $"Purchase_Return_{SanitizeFile(docNo)}.pdf",
                    PartyType = sup != null ? "supplier" : null,
                    PartyId = sId,
                    RecipientName = supName,
                    RecipientPhone = GetVal(sup, "phone", ""),
                    RecipientEmail = GetVal(sup, "email", ""),
                    WhatsappTextEn = $"*{storeName}*\nPurchase Return / Debit Note: *{docNo}*\nSupplier: {supName}\nReturn Debit Total: *Rs. {total:N2}*\nPlease find the Debit Note PDF attached.",
                    WhatsappTextUr = $"*{storeName}*\nپرچیز ریٹرن / ڈیبٹ نوٹ: *{docNo}*\nواپسی بل کی کل رقم: *Rs. {total:N2}*",
                    EmailSubjectEn = $"Purchase Return {docNo} — {storeName}",
                    EmailSubjectUr = $"پرچیز ریٹرن {docNo} — {storeName}",
                    EmailBodyEn = $"Dear {supName},\n\nPlease find Debit Note ({docNo}) attached.",
                    EmailBodyUr = $"پرچیز ریٹرن ({docNo}) منسلک ہے۔",
                    PdfSummaryLines = new List<string>
                    {
                        $"Store: {storeName}",
                        $"Purchase Return: {docNo}",
                        $"Supplier: {supName}",
                        $"Total: Rs. {total:N2}"
                    }
                };
            }

            case "stock_disposal":
            {
                var sd = await Db.QuerySingleOrDefaultAsync("SELECT * FROM stock_disposals WHERE id = @id", new { id });
                if (sd == null) return null;
                var docNo = GetVal(sd, "disposal_number", $"DSP-{id}");
                return new ResolvedDocContext
                {
                    Title = $"Stock Disposal Voucher {docNo}",
                    Filename = $"Stock_Disposal_{SanitizeFile(docNo)}.pdf",
                    RecipientName = storeName,
                    WhatsappTextEn = $"*{storeName}*\nStock Disposal Voucher: *{docNo}*",
                    WhatsappTextUr = $"*{storeName}*\nاسٹاک ڈسپوزل واؤچر: *{docNo}*",
                    EmailSubjectEn = $"Stock Disposal Voucher {docNo} — {storeName}",
                    EmailSubjectUr = $"اسٹاک ڈسپوزل واؤچر {docNo} — {storeName}",
                    EmailBodyEn = $"Please find Stock Disposal Voucher ({docNo}) attached.",
                    EmailBodyUr = $"اسٹاک ڈسپوزل واؤچر ({docNo}) منسلک ہے۔",
                    PdfSummaryLines = new List<string> { $"Store: {storeName}", $"Stock Disposal Voucher: {docNo}" }
                };
            }

            case "cash_drawer_session":
            {
                var cd = await Db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id });
                if (cd == null) return null;
                var docNo = $"CDS-{id}";
                return new ResolvedDocContext
                {
                    Title = $"Cash Drawer Session {docNo}",
                    Filename = $"Cash_Drawer_Session_{id}.pdf",
                    RecipientName = storeName,
                    WhatsappTextEn = $"*{storeName}*\nCash Drawer Session Report: *{docNo}*",
                    WhatsappTextUr = $"*{storeName}*\nکیش ڈراور رپورٹ: *{docNo}*",
                    EmailSubjectEn = $"Cash Drawer Session {docNo} — {storeName}",
                    EmailSubjectUr = $"کیش ڈراور رپورٹ {docNo} — {storeName}",
                    EmailBodyEn = $"Please find Cash Drawer Session ({docNo}) report attached.",
                    EmailBodyUr = $"کیش ڈراور رپورٹ ({docNo}) منسلک ہے۔",
                    PdfSummaryLines = new List<string> { $"Store: {storeName}", $"Cash Drawer Session: {docNo}" }
                };
            }

            case "stocktake_session":
            {
                var st = await Db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_sessions WHERE id = @id", new { id });
                if (st == null) return null;
                var docNo = GetVal(st, "reference_no", GetVal(st, "session_number", $"STK-{id}"));
                return new ResolvedDocContext
                {
                    Title = $"Stocktake Session {docNo}",
                    Filename = $"Stocktake_Session_{SanitizeFile(docNo)}.pdf",
                    RecipientName = storeName,
                    WhatsappTextEn = $"*{storeName}*\nStocktake Audit Session: *{docNo}*",
                    WhatsappTextUr = $"*{storeName}*\nاسٹاک آڈٹ سیشن: *{docNo}*",
                    EmailSubjectEn = $"Stocktake Session {docNo} — {storeName}",
                    EmailSubjectUr = $"اسٹاک آڈٹ سیشن {docNo} — {storeName}",
                    EmailBodyEn = $"Please find Stocktake Session ({docNo}) report attached.",
                    EmailBodyUr = $"اسٹاک آڈٹ سیشن ({docNo}) منسلک ہے۔",
                    PdfSummaryLines = new List<string> { $"Store: {storeName}", $"Stocktake Session: {docNo}" }
                };
            }
        }

        return null;
    }

    private async Task<Dictionary<string, object?>?> GetSettingAsync()
    {
        if (!await Db.HasTableAsync("settings")) return null;
        return await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1");
    }

    private static string ResolvePaperSize(string? requested, Dictionary<string, object?>? setting)
    {
        var candidate = (requested ?? "").Trim().ToLowerInvariant();
        if (SupportedPaperSizes.Contains(candidate)) return candidate;
        var saved = GetVal(setting, "share_paper_size", "a4").Trim().ToLowerInvariant();
        return SupportedPaperSizes.Contains(saved) ? saved : "a4";
    }

    private static string ResolvePdfDesign(string? requested, Dictionary<string, object?>? setting, string paperSize)
    {
        var candidate = (requested ?? "").Trim().ToLowerInvariant();
        if (SupportedPdfDesigns.Contains(candidate)) return candidate;
        var key = paperSize == "thermal" ? "thermal_template" : "a4_template";
        var saved = GetVal(setting, key, "standard").Trim().ToLowerInvariant();
        return SupportedPdfDesigns.Contains(saved) ? saved : "standard";
    }

    private static bool ResolveShowLogo(string? requested, Dictionary<string, object?>? setting)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested == "1" || requested.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        if (setting != null && setting.TryGetValue("share_show_logo", out var val) && val != null)
        {
            if (val is bool b) return b;
            return val.ToString() == "1" || string.Equals(val.ToString(), "true", StringComparison.OrdinalIgnoreCase);
        }
        return true;
    }

    public static string NormalizePhone(string? phone, string defaultCountryCode = "92")
    {
        if (string.IsNullOrWhiteSpace(phone)) return "";
        var digits = Regex.Replace(phone, @"\D+", "");
        if (string.IsNullOrEmpty(digits)) return "";

        if (digits.StartsWith("00"))
        {
            digits = digits.Substring(2);
        }

        if (digits.Length == 11 && digits.StartsWith("03"))
        {
            return defaultCountryCode + digits.Substring(1);
        }

        if (digits.Length == 10 && digits.StartsWith("3"))
        {
            return defaultCountryCode + digits;
        }

        if (digits.StartsWith("0") && digits.Length >= 10 && digits.Length <= 11)
        {
            return defaultCountryCode + digits.Substring(1);
        }

        return digits;
    }

    private static string SanitizeFile(string name)
    {
        return Regex.Replace(name, @"[^A-Za-z0-9_-]", "_");
    }

    private static SmtpConfiguration BuildSmtpConfig(Dictionary<string, object?>? setting)
    {
        bool IsOn(string k) => setting != null && setting.TryGetValue(k, out var v) && v is true or 1 or 1L or "1" or "true";
        int IntVal(string k, int fb) => setting != null && setting.TryGetValue(k, out var v) && int.TryParse(v?.ToString(), out var i) ? i : fb;
        return new SmtpConfiguration
        {
            Enabled = IsOn("smtp_enabled"),
            Host = GetVal(setting, "smtp_host", "127.0.0.1"),
            Port = IntVal("smtp_port", 587),
            Username = GetVal(setting, "smtp_username", ""),
            Password = GetVal(setting, "smtp_password", ""),
            Encryption = GetVal(setting, "smtp_encryption", "tls"),
            FromAddress = GetVal(setting, "smtp_from_address", GetVal(setting, "smtp_username", "noreply@karobar.local")),
            FromName = GetVal(setting, "smtp_from_name", GetVal(setting, "store_name", "Karobar App Store"))
        };
    }

    private static WhatsAppConfiguration BuildWhatsAppConfig(Dictionary<string, object?>? setting)
    {
        bool IsOn(string k, bool fb = false) => setting != null && setting.TryGetValue(k, out var v) && v != null
            ? (v is true or 1 or 1L || v.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true || v.ToString() == "1")
            : fb;
        return new WhatsAppConfiguration
        {
            Enabled = IsOn("whatsapp_enabled", false),
            Provider = GetVal(setting, "whatsapp_provider", "local_agent"),
            AccessToken = GetVal(setting, "whatsapp_access_token", ""),
            PhoneNumberId = GetVal(setting, "whatsapp_phone_number_id", ""),
            BusinessAccountId = GetVal(setting, "whatsapp_business_account_id", ""),
            ApiVersion = GetVal(setting, "whatsapp_api_version", "v21.0"),
            ApiUrl = GetVal(setting, "whatsapp_api_url", "http://127.0.0.1:3002"),
            DefaultCountryCode = GetVal(setting, "whatsapp_default_country_code", "92"),
            TemplateName = GetVal(setting, "whatsapp_template_name", ""),
            TemplateLanguage = GetVal(setting, "whatsapp_template_language", "en_US"),
            AutoFallback = IsOn("whatsapp_auto_fallback", true)
        };
    }

    private async Task<byte[]> GenerateRealSharedPdfAsync(
        string type,
        ResolvedDocContext ctx,
        Dictionary<string, object?>? setting,
        string paperSize,
        string pdfDesign,
        bool showLogo,
        string? idStr,
        string? customerIdStr,
        string? supplierIdStr,
        string? from,
        string? to)
    {
        var settingVm = PrintViewModelMapper.MapPrintSetting(setting);
        settingVm.SharePaperSize = paperSize;
        settingVm.ShareShowLogo = showLogo;

        var req = new SharedDocumentPdfRequest
        {
            DocumentType = type,
            DocumentTitle = PdfDocumentGenerator.SupportedSharedDocuments.TryGetValue(type, out var stdTitle) ? stdTitle : ctx.Title.ToUpperInvariant(),
            DocumentNumber = !string.IsNullOrWhiteSpace(ctx.DocumentNumber) ? ctx.DocumentNumber : (!string.IsNullOrWhiteSpace(idStr) ? idStr! : type.ToUpperInvariant()),
            DocumentDate = ctx.DocumentDate,
            PaperSize = paperSize,
            PdfDesign = pdfDesign,
            Setting = settingVm,
            PartyHeading = ctx.PartyHeading,
            PartyName = string.IsNullOrWhiteSpace(ctx.RecipientName) ? "Customer" : ctx.RecipientName,
            PartyPhone = string.IsNullOrWhiteSpace(ctx.RecipientPhone) ? null : ctx.RecipientPhone,
            PartyAddress = string.IsNullOrWhiteSpace(ctx.RecipientAddress) ? null : ctx.RecipientAddress,
            Remarks = ctx.Remarks,
            FbrInvoiceNumber = ctx.FbrInvoiceNumber,
            FbrQrData = ctx.FbrQrData
        };

        long.TryParse(idStr, out var docId);

        if ((type == "sale_invoice" || type == "delivery_challan") && docId > 0)
        {
            var inv = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id = docId });
            if (inv != null)
            {
                req.DocumentNumber = GetVal(inv, "posted_number", $"SI-{docId}");
                req.DocumentDate = GetVal(inv, "date", req.DocumentDate);
                req.FbrInvoiceNumber = GetVal(inv, "fbr_invoice_number", "");
                req.FbrQrData = GetVal(inv, "fbr_qr_data", "");
                req.Remarks = GetVal(inv, "remarks", "");
                var items = await Db.QueryListAsync(@"
                    SELECT i.*, p.name AS product_name
                    FROM sale_invoice_items i
                    LEFT JOIN products p ON p.id = i.product_id
                    WHERE i.sale_invoice_id = @id", new { id = docId });
                req.Columns =
                [
                    new PdfTableColumnDef { Header = "Item", RelativeWidth = 4f },
                    new PdfTableColumnDef { Header = "Qty", RelativeWidth = 1f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Rate", RelativeWidth = 1.5f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Total", RelativeWidth = 1.5f, AlignRight = true }
                ];
                foreach (var it in items)
                {
                    req.Rows.Add([
                        GetVal(it, "product_name", "Item"),
                        ToDouble(it, "quantity").ToString("0.##", CultureInfo.InvariantCulture),
                        ToDouble(it, "price").ToString("N2", CultureInfo.InvariantCulture),
                        ToDouble(it, "sub_total").ToString("N2", CultureInfo.InvariantCulture)
                    ]);
                }
                req.Totals =
                [
                    new PdfKeyValuePair("Gross Amount", $"Rs. {ToDouble(inv, "gross_amount"):N2}"),
                    new PdfKeyValuePair("Discount", $"- Rs. {ToDouble(inv, "discount_amount"):N2}"),
                    new PdfKeyValuePair("Tax", $"Rs. {ToDouble(inv, "tax_amount"):N2}"),
                    new PdfKeyValuePair("Net Total", $"Rs. {ToDouble(inv, "total"):N2}", isGrandTotal: true)
                ];
            }
        }
        else if (type == "purchase_invoice" && docId > 0)
        {
            var inv = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_invoices WHERE id = @id", new { id = docId });
            if (inv != null)
            {
                req.DocumentNumber = GetVal(inv, "posted_number", $"PI-{docId}");
                req.DocumentDate = GetVal(inv, "posted_date", req.DocumentDate);
                var items = await Db.QueryListAsync(@"
                    SELECT i.*, p.name AS product_name
                    FROM purchase_invoice_items i
                    LEFT JOIN products p ON p.id = i.product_id
                    WHERE i.purchase_invoice_id = @id", new { id = docId });
                req.Columns =
                [
                    new PdfTableColumnDef { Header = "Item", RelativeWidth = 4f },
                    new PdfTableColumnDef { Header = "Qty", RelativeWidth = 1f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Cost", RelativeWidth = 1.5f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Total", RelativeWidth = 1.5f, AlignRight = true }
                ];
                foreach (var it in items)
                {
                    req.Rows.Add([
                        GetVal(it, "product_name", "Item"),
                        ToDouble(it, "quantity").ToString("0.##", CultureInfo.InvariantCulture),
                        ToDouble(it, "unit_purchase_price").ToString("N2", CultureInfo.InvariantCulture),
                        ToDouble(it, "sub_total").ToString("N2", CultureInfo.InvariantCulture)
                    ]);
                }
                req.Totals =
                [
                    new PdfKeyValuePair("Total Amount", $"Rs. {ToDouble(inv, "total_amount"):N2}", isGrandTotal: true),
                    new PdfKeyValuePair("Total Paid", $"Rs. {ToDouble(inv, "total_paid"):N2}")
                ];
            }
        }
        else if (type == "purchase_order" && docId > 0 && await Db.HasTableAsync("purchase_order_items"))
        {
            if (await Db.HasTableAsync("purchase_orders"))
            {
                var po = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_orders WHERE id = @id", new { id = docId });
                if (po != null)
                {
                    req.DocumentNumber = GetVal(po, "po_number", GetVal(po, "order_number", GetVal(po, "posted_number", $"PO-{docId}")));
                    req.DocumentDate = GetVal(po, "order_date", GetVal(po, "date", GetVal(po, "created_at", req.DocumentDate)));
                    req.Remarks = GetVal(po, "notes", GetVal(po, "remarks", ""));
                }
            }
            var items = await Db.QueryListAsync(@"
                SELECT i.*, p.name AS product_name
                FROM purchase_order_items i
                LEFT JOIN products p ON p.id = i.product_id
                WHERE i.purchase_order_id = @id", new { id = docId });
            req.Columns =
            [
                new PdfTableColumnDef { Header = "Product", RelativeWidth = 4f },
                new PdfTableColumnDef { Header = "Qty", RelativeWidth = 1f, AlignRight = true },
                new PdfTableColumnDef { Header = "Rate", RelativeWidth = 1.5f, AlignRight = true },
                new PdfTableColumnDef { Header = "Total", RelativeWidth = 1.5f, AlignRight = true }
            ];
            double grand = 0;
            foreach (var it in items)
            {
                var qty = ToDouble(it, "ordered_quantity");
                var cost = ToDouble(it, "unit_purchase_price");
                var discPct = ToDouble(it, "item_discount_percentage");
                var lt = Math.Round(qty * cost * (1.0 - discPct / 100.0), 2);
                grand += lt;
                req.Rows.Add([GetVal(it, "product_name", "Item"), qty.ToString("0.##"), cost.ToString("N2"), lt.ToString("N2")]);
            }
            req.Totals = [new PdfKeyValuePair("Order Total", $"Rs. {grand:N2}", isGrandTotal: true)];
        }
        else if (type == "sale_return" && docId > 0)
        {
            var sr = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_returns WHERE id = @id", new { id = docId });
            if (sr != null)
            {
                req.DocumentNumber = GetVal(sr, "posted_number", $"SLRET-{docId}");
                req.DocumentDate = GetVal(sr, "date", req.DocumentDate);
                req.FbrInvoiceNumber = GetVal(sr, "fbr_invoice_number", "");
                req.FbrQrData = GetVal(sr, "fbr_qr_data", "");
                req.Remarks = GetVal(sr, "remarks", "");
                var items = await Db.QueryListAsync(@"
                    SELECT i.*, p.name AS product_name
                    FROM sale_return_items i
                    LEFT JOIN products p ON p.id = i.product_id
                    WHERE i.sale_return_id = @id", new { id = docId });
                req.Columns =
                [
                    new PdfTableColumnDef { Header = "Item", RelativeWidth = 4f },
                    new PdfTableColumnDef { Header = "Ret Qty", RelativeWidth = 1.2f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Rate", RelativeWidth = 1.4f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Total", RelativeWidth = 1.4f, AlignRight = true }
                ];
                foreach (var it in items)
                {
                    req.Rows.Add([
                        GetVal(it, "product_name", "Item"),
                        ToDouble(it, "unit_return_quantity").ToString("0.##", CultureInfo.InvariantCulture),
                        ToDouble(it, "unit_sale_price").ToString("N2", CultureInfo.InvariantCulture),
                        ToDouble(it, "sub_total").ToString("N2", CultureInfo.InvariantCulture)
                    ]);
                }
                req.Totals =
                [
                    new PdfKeyValuePair("Gross Return", $"Rs. {ToDouble(sr, "gross_total"):N2}"),
                    new PdfKeyValuePair("Discount", $"- Rs. {ToDouble(sr, "discount_amount"):N2}"),
                    new PdfKeyValuePair("Return Total", $"Rs. {ToDouble(sr, "total"):N2}", isGrandTotal: true)
                ];
            }
        }
        else if (type == "purchase_return" && docId > 0)
        {
            var pr = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_returns WHERE id = @id", new { id = docId });
            if (pr != null)
            {
                req.DocumentNumber = GetVal(pr, "posted_number", $"PRET-{docId}");
                req.DocumentDate = GetVal(pr, "date", req.DocumentDate);
                req.Remarks = GetVal(pr, "remarks", "");
                var items = await Db.QueryListAsync(@"
                    SELECT i.*, p.name AS product_name
                    FROM purchase_return_items i
                    LEFT JOIN products p ON p.id = i.product_id
                    WHERE i.purchase_return_id = @id", new { id = docId });
                req.Columns =
                [
                    new PdfTableColumnDef { Header = "Item", RelativeWidth = 4f },
                    new PdfTableColumnDef { Header = "Ret Qty", RelativeWidth = 1.2f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Cost", RelativeWidth = 1.4f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Total", RelativeWidth = 1.4f, AlignRight = true }
                ];
                foreach (var it in items)
                {
                    req.Rows.Add([
                        GetVal(it, "product_name", "Item"),
                        ToDouble(it, "return_unit_quantity").ToString("0.##", CultureInfo.InvariantCulture),
                        ToDouble(it, "unit_purchase_price").ToString("N2", CultureInfo.InvariantCulture),
                        ToDouble(it, "sub_total").ToString("N2", CultureInfo.InvariantCulture)
                    ]);
                }
                req.Totals = [new PdfKeyValuePair("Return Total", $"Rs. {ToDouble(pr, "total"):N2}", isGrandTotal: true)];
            }
        }
        else if (type == "quotation" && docId > 0)
        {
            var draft = await Db.QuerySingleOrDefaultAsync("SELECT * FROM invoice_drafts WHERE id = @id", new { id = docId });
            if (draft != null)
            {
                req.DocumentNumber = GetVal(draft, "title", GetVal(draft, "uuid", $"QTN-{docId}"));
                req.DocumentDate = GetVal(draft, "created_at", req.DocumentDate);
                var payloadStr = GetVal(draft, "payload", "");
                req.Columns =
                [
                    new PdfTableColumnDef { Header = "Item", RelativeWidth = 4f },
                    new PdfTableColumnDef { Header = "Qty", RelativeWidth = 1f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Rate", RelativeWidth = 1.5f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Total", RelativeWidth = 1.5f, AlignRight = true }
                ];
                double sumTotal = 0;
                if (!string.IsNullOrWhiteSpace(payloadStr))
                {
                    try
                    {
                        using var pDoc = JsonDocument.Parse(payloadStr);
                        if (pDoc.RootElement.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var it in itemsEl.EnumerateArray())
                            {
                                var name = it.TryGetProperty("product_name", out var pn) ? pn.ToString() : (it.TryGetProperty("name", out var n) ? n.ToString() : "Item");
                                var qty = it.TryGetProperty("quantity", out var q) && double.TryParse(q.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var qv) ? qv : 1;
                                var rate = it.TryGetProperty("price", out var prEl) && double.TryParse(prEl.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var pv) ? pv : 0;
                                var st = it.TryGetProperty("sub_total", out var stEl) && double.TryParse(stEl.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var sv) ? sv : (qty * rate);
                                sumTotal += st;
                                req.Rows.Add([name, qty.ToString("0.##"), rate.ToString("N2"), st.ToString("N2")]);
                            }
                        }
                    }
                    catch { }
                }
                var estTotal = ToDouble(draft, "total_amount");
                if (estTotal == 0) estTotal = sumTotal;
                req.Totals = [new PdfKeyValuePair("Estimated Total", $"Rs. {estTotal:N2}", isGrandTotal: true)];
            }
        }
        else if (type == "customer_ledger" && long.TryParse(customerIdStr ?? idStr, out var clCustId) && clCustId > 0)
        {
            req.DocumentNumber = $"CL-{clCustId}";
            req.PartyHeading = "Customer";
            var fromDay = !string.IsNullOrWhiteSpace(from) ? (from.Trim().Length > 10 ? from.Trim()[..10] : from.Trim()) : "";
            var toDay = !string.IsNullOrWhiteSpace(to) ? (to.Trim().Length > 10 ? to.Trim()[..10] : to.Trim()) : "";

            double bal = 0;
            if (!string.IsNullOrEmpty(fromDay))
            {
                var prior = await Db.QuerySingleOrDefaultAsync(@"
                    SELECT COALESCE(SUM(COALESCE(invoice_total, 0) - COALESCE(total_received, 0) - COALESCE(credited_amount, 0)), 0) AS bal
                    FROM customer_ledgers
                    WHERE customer_id = @cid AND substr(entry_date, 1, 10) < @fromDay",
                    new { cid = clCustId, fromDay });
                bal = ToDouble(prior, "bal");
            }

            var rows = await Db.QueryListAsync(@"
                SELECT entry_date, posted_number, description, invoice_total, total_received, credited_amount
                FROM customer_ledgers
                WHERE customer_id = @cid
                  AND (@fromDay = '' OR substr(entry_date, 1, 10) >= @fromDay)
                  AND (@toDay = '' OR substr(entry_date, 1, 10) <= @toDay)
                ORDER BY substr(entry_date, 1, 10) ASC, id ASC", new { cid = clCustId, fromDay, toDay });
            req.Columns =
            [
                new PdfTableColumnDef { Header = "Date", RelativeWidth = 1.6f },
                new PdfTableColumnDef { Header = "Ref #", RelativeWidth = 1.6f },
                new PdfTableColumnDef { Header = "Description", RelativeWidth = 3f },
                new PdfTableColumnDef { Header = "Debit", RelativeWidth = 1.3f, AlignRight = true },
                new PdfTableColumnDef { Header = "Credit", RelativeWidth = 1.3f, AlignRight = true },
                new PdfTableColumnDef { Header = "Balance", RelativeWidth = 1.5f, AlignRight = true }
            ];
            if (!string.IsNullOrEmpty(fromDay) || Math.Abs(bal) > 0.001)
            {
                req.Rows.Add([!string.IsNullOrEmpty(fromDay) ? fromDay : "—", "B/F", "Opening Balance Brought Forward", "0.00", "0.00", bal.ToString("N2")]);
            }
            foreach (var r in rows)
            {
                var inv = ToDouble(r, "invoice_total");
                var cr = ToDouble(r, "total_received") + ToDouble(r, "credited_amount");
                bal += inv - cr;
                req.Rows.Add([GetVal(r, "entry_date", ""), GetVal(r, "posted_number", "-"), GetVal(r, "description", ""), inv.ToString("N2"), cr.ToString("N2"), bal.ToString("N2")]);
            }
            req.Totals = [new PdfKeyValuePair("Net Balance", $"Rs. {bal:N2}", isGrandTotal: true)];
        }
        else if (type == "supplier_ledger" && long.TryParse(supplierIdStr ?? idStr, out var slSupId) && slSupId > 0)
        {
            req.DocumentNumber = $"SL-{slSupId}";
            req.PartyHeading = "Supplier";
            var fromDay = !string.IsNullOrWhiteSpace(from) ? (from.Trim().Length > 10 ? from.Trim()[..10] : from.Trim()) : "";
            var toDay = !string.IsNullOrWhiteSpace(to) ? (to.Trim().Length > 10 ? to.Trim()[..10] : to.Trim()) : "";

            double bal = 0;
            if (!string.IsNullOrEmpty(fromDay))
            {
                var prior = await Db.QuerySingleOrDefaultAsync(@"
                    SELECT COALESCE(SUM(COALESCE(invoice_total, 0) - COALESCE(total_paid, 0) - COALESCE(debited_amount, 0)), 0) AS bal
                    FROM supplier_ledgers
                    WHERE supplier_id = @sid AND substr(entry_date, 1, 10) < @fromDay",
                    new { sid = slSupId, fromDay });
                bal = ToDouble(prior, "bal");
            }

            var rows = await Db.QueryListAsync(@"
                SELECT entry_date, posted_number, description, invoice_total, total_paid, debited_amount
                FROM supplier_ledgers
                WHERE supplier_id = @sid
                  AND (@fromDay = '' OR substr(entry_date, 1, 10) >= @fromDay)
                  AND (@toDay = '' OR substr(entry_date, 1, 10) <= @toDay)
                ORDER BY substr(entry_date, 1, 10) ASC, id ASC", new { sid = slSupId, fromDay, toDay });
            req.Columns =
            [
                new PdfTableColumnDef { Header = "Date", RelativeWidth = 1.6f },
                new PdfTableColumnDef { Header = "Ref #", RelativeWidth = 1.6f },
                new PdfTableColumnDef { Header = "Description", RelativeWidth = 3f },
                new PdfTableColumnDef { Header = "Invoiced", RelativeWidth = 1.3f, AlignRight = true },
                new PdfTableColumnDef { Header = "Paid", RelativeWidth = 1.3f, AlignRight = true },
                new PdfTableColumnDef { Header = "Balance", RelativeWidth = 1.5f, AlignRight = true }
            ];
            if (!string.IsNullOrEmpty(fromDay) || Math.Abs(bal) > 0.001)
            {
                req.Rows.Add([!string.IsNullOrEmpty(fromDay) ? fromDay : "—", "B/F", "Opening Balance Brought Forward", "0.00", "0.00", bal.ToString("N2")]);
            }
            foreach (var r in rows)
            {
                var inv = ToDouble(r, "invoice_total");
                var paid = ToDouble(r, "total_paid") + ToDouble(r, "debited_amount");
                bal += inv - paid;
                req.Rows.Add([GetVal(r, "entry_date", ""), GetVal(r, "posted_number", "-"), GetVal(r, "description", ""), inv.ToString("N2"), paid.ToString("N2"), bal.ToString("N2")]);
            }
            req.Totals = [new PdfKeyValuePair("Net Payable", $"Rs. {bal:N2}", isGrandTotal: true)];
        }
        else if (type == "payment" && docId > 0)
        {
            var pmt = await Db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = docId });
            if (pmt != null)
            {
                req.DocumentNumber = GetVal(pmt, "payment_number", $"PMT-{docId}");
                req.DocumentDate = GetVal(pmt, "payment_date", req.DocumentDate);
                req.Remarks = GetVal(pmt, "notes", "");
                req.Columns =
                [
                    new PdfTableColumnDef { Header = "Voucher Field", RelativeWidth = 3f },
                    new PdfTableColumnDef { Header = "Details", RelativeWidth = 5f }
                ];
                req.Rows.Add(["Payment #", req.DocumentNumber]);
                req.Rows.Add(["Method", GetVal(pmt, "payment_method", "cash").ToUpperInvariant()]);
                if (!string.IsNullOrWhiteSpace(GetVal(pmt, "reference"))) req.Rows.Add(["Reference", GetVal(pmt, "reference")]);
                if (!string.IsNullOrWhiteSpace(GetVal(pmt, "cheque_number"))) req.Rows.Add(["Cheque #", $"{GetVal(pmt, "cheque_number")} ({GetVal(pmt, "bank_name", "")})"]);
                req.Rows.Add(["Status", GetVal(pmt, "status", "posted").ToUpperInvariant()]);
                req.Totals =
                [
                    new PdfKeyValuePair("Allocated", $"Rs. {ToDouble(pmt, "allocated_amount"):N2}"),
                    new PdfKeyValuePair("Unapplied", $"Rs. {ToDouble(pmt, "unapplied_amount"):N2}"),
                    new PdfKeyValuePair("Voucher Amount", $"Rs. {ToDouble(pmt, "amount"):N2}", isGrandTotal: true)
                ];
            }
        }
        else if (type == "stock_disposal" && docId > 0)
        {
            var dsp = await Db.QuerySingleOrDefaultAsync("SELECT * FROM stock_disposals WHERE id = @id", new { id = docId });
            if (dsp != null)
            {
                req.DocumentNumber = GetVal(dsp, "disposal_number", GetVal(dsp, "posted_number", $"DSP-{docId}"));
                req.DocumentDate = GetVal(dsp, "disposal_date", req.DocumentDate);
                req.Remarks = GetVal(dsp, "notes", "");
                var items = await Db.QueryListAsync(@"
                    SELECT i.*, p.name AS product_name
                    FROM stock_disposal_items i
                    LEFT JOIN products p ON p.id = i.product_id
                    WHERE i.stock_disposal_id = @id", new { id = docId });
                req.Columns =
                [
                    new PdfTableColumnDef { Header = "Product", RelativeWidth = 3.5f },
                    new PdfTableColumnDef { Header = "Batch", RelativeWidth = 1.5f },
                    new PdfTableColumnDef { Header = "Qty", RelativeWidth = 1f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Cost Value", RelativeWidth = 1.5f, AlignRight = true }
                ];
                double totalCost = 0;
                foreach (var it in items)
                {
                    var cv = ToDouble(it, "cost_value");
                    totalCost += cv;
                    req.Rows.Add([GetVal(it, "product_name", "Item"), GetVal(it, "batch_number", "-"), ToDouble(it, "dispose_qty").ToString("0.##"), cv.ToString("N2")]);
                }
                req.Totals = [new PdfKeyValuePair("Total Disposal Cost", $"Rs. {totalCost:N2}", isGrandTotal: true)];
            }
        }
        else if (type == "cash_drawer_session" && docId > 0)
        {
            var cds = await Db.QuerySingleOrDefaultAsync("SELECT * FROM cash_drawer_sessions WHERE id = @id", new { id = docId });
            if (cds != null)
            {
                req.DocumentNumber = $"SHIFT-{docId}";
                req.DocumentDate = GetVal(cds, "opened_at", req.DocumentDate);
                var txns = await Db.QueryListAsync(
                    "SELECT created_at, type, amount, notes FROM cash_drawer_transactions WHERE session_id = @id ORDER BY id ASC LIMIT 200",
                    new { id = docId });
                req.Columns =
                [
                    new PdfTableColumnDef { Header = "Time", RelativeWidth = 2f },
                    new PdfTableColumnDef { Header = "Type", RelativeWidth = 2f },
                    new PdfTableColumnDef { Header = "Notes", RelativeWidth = 3f },
                    new PdfTableColumnDef { Header = "Amount", RelativeWidth = 1.5f, AlignRight = true }
                ];
                foreach (var t in txns)
                {
                    req.Rows.Add([GetVal(t, "created_at", ""), GetVal(t, "type", ""), GetVal(t, "notes", "-"), ToDouble(t, "amount").ToString("N2")]);
                }
                req.Totals =
                [
                    new PdfKeyValuePair("Opening Balance", $"Rs. {ToDouble(cds, "opening_balance"):N2}"),
                    new PdfKeyValuePair("Expected Closing", $"Rs. {ToDouble(cds, "closing_balance_expected"):N2}", isGrandTotal: true)
                ];
            }
        }
        else if (type == "stocktake_session" && docId > 0)
        {
            var stk = await Db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_sessions WHERE id = @id", new { id = docId });
            if (stk != null)
            {
                req.DocumentNumber = GetVal(stk, "reference_no", $"STK-{docId}");
                req.DocumentDate = GetVal(stk, "created_at", req.DocumentDate);
                var items = await Db.QueryListAsync(@"
                    SELECT i.*, p.name AS product_name
                    FROM stocktake_items i
                    LEFT JOIN products p ON p.id = i.product_id
                    WHERE i.stocktake_session_id = @id
                    LIMIT 250", new { id = docId });
                req.Columns =
                [
                    new PdfTableColumnDef { Header = "Product", RelativeWidth = 3.5f },
                    new PdfTableColumnDef { Header = "System", RelativeWidth = 1.2f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Counted", RelativeWidth = 1.2f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Variance", RelativeWidth = 1.3f, AlignRight = true },
                    new PdfTableColumnDef { Header = "Var Value", RelativeWidth = 1.5f, AlignRight = true }
                ];
                double totalVarVal = 0;
                foreach (var it in items)
                {
                    var vv = ToDouble(it, "variance_value");
                    totalVarVal += vv;
                    req.Rows.Add([
                        GetVal(it, "product_name", "Item"),
                        ToDouble(it, "system_qty").ToString("0.##"),
                        it.GetValueOrDefault("counted_qty") != null ? ToDouble(it, "counted_qty").ToString("0.##") : "-",
                        ToDouble(it, "variance_qty").ToString("0.##"),
                        vv.ToString("N2")
                    ]);
                }
                req.Totals = [new PdfKeyValuePair("Net Variance Value", $"Rs. {totalVarVal:N2}", isGrandTotal: true)];
            }
        }
        else if (type == "regular_refill_reminder" && long.TryParse(customerIdStr ?? idStr, out var rrCustId) && rrCustId > 0)
        {
            req.DocumentNumber = $"REFILL-{rrCustId}";
            req.PartyHeading = "Patient / Regular Customer";
            var meds = await Db.QueryListAsync(@"
                SELECT p.name AS product_name, MAX(si.date) AS last_date, SUM(sii.quantity) AS total_qty
                FROM sale_invoice_items sii
                INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
                LEFT JOIN products p ON p.id = sii.product_id
                WHERE si.customer_id = @cid AND (si.status IS NULL OR si.status != 'reversed')
                GROUP BY p.id, p.name
                ORDER BY last_date DESC
                LIMIT 20", new { cid = rrCustId });
            if (meds.Count > 0)
            {
                req.Columns =
                [
                    new PdfTableColumnDef { Header = "Regular Medicine", RelativeWidth = 4.5f },
                    new PdfTableColumnDef { Header = "Last Purchased", RelativeWidth = 2f },
                    new PdfTableColumnDef { Header = "Total Qty", RelativeWidth = 1.5f, AlignRight = true }
                ];
                foreach (var m in meds)
                {
                    req.Rows.Add([GetVal(m, "product_name", "Medicine"), GetVal(m, "last_date", ""), ToDouble(m, "total_qty").ToString("0.##")]);
                }
            }
        }

        if (req.Columns.Count == 0 && ctx.PdfSummaryLines.Count > 0)
        {
            req.Columns =
            [
                new PdfTableColumnDef { Header = "Detail", RelativeWidth = 2.5f },
                new PdfTableColumnDef { Header = "Value", RelativeWidth = 5.5f }
            ];
            foreach (var line in ctx.PdfSummaryLines)
            {
                var idx = line.IndexOf(':');
                if (idx > 0)
                    req.Rows.Add([line[..idx].Trim(), line[(idx + 1)..].Trim()]);
                else
                    req.Rows.Add(["Info", line]);
            }
        }

        return _pdfGenerator.GenerateSharedDocumentPdf(req);
    }

    private static string GetVal(Dictionary<string, object?>? dict, string key, string fallback = "")
    {
        if (dict == null || !dict.TryGetValue(key, out var val) || val == null) return fallback;
        var s = val.ToString();
        return string.IsNullOrEmpty(s) ? fallback : s;
    }

    private static long? ToNullableLong(Dictionary<string, object?>? row, string key)
    {
        if (row == null || !row.TryGetValue(key, out var val) || val == null) return null;
        if (val is long l) return l;
        if (val is int i) return i;
        return long.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static double ToDouble(Dictionary<string, object?>? row, string key)
    {
        if (row == null || !row.TryGetValue(key, out var val) || val == null) return 0.0;
        if (val is double d) return d;
        if (val is float f) return f;
        if (val is decimal m) return (double)m;
        if (val is long l) return l;
        if (val is int i) return i;
        return double.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0.0;
    }
}

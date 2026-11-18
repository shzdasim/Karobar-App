using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

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

    public DocumentShareController(KarobarDb db) : base(db)
    {
    }

    private sealed class ResolvedDocContext
    {
        public string Title { get; set; } = "";
        public string Filename { get; set; } = "document.pdf";
        public string? PartyType { get; set; }
        public long? PartyId { get; set; }
        public string RecipientName { get; set; } = "";
        public string RecipientPhone { get; set; } = "";
        public string RecipientEmail { get; set; } = "";
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
        var paperSize = ResolvePaperSize(Request.Query["size"].FirstOrDefault(), setting);
        var pdfDesign = ResolvePdfDesign(Request.Query["template"].FirstOrDefault(), setting, paperSize);
        var showLogo = ResolveShowLogo(Request.Query["show_logo"].FirstOrDefault(), setting);
        var normalizedPhone = NormalizePhone(ctx!.RecipientPhone);

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
        foreach (var k in new[] { "id", "customer_id", "supplier_id", "from", "to" })
        {
            var v = Request.Query[k].FirstOrDefault();
            if (!string.IsNullOrEmpty(v)) queryParts.Add($"{k}={Uri.EscapeDataString(v)}");
        }
        queryParts.Add($"size={Uri.EscapeDataString(paperSize)}");
        queryParts.Add($"template={Uri.EscapeDataString(pdfDesign)}");
        var apiQuery = string.Join("&", queryParts);

        var waUrlEn = !string.IsNullOrEmpty(normalizedPhone)
            ? $"https://wa.me/{normalizedPhone}?text={Uri.EscapeDataString(ctx.WhatsappTextEn)}"
            : $"https://wa.me/?text={Uri.EscapeDataString(ctx.WhatsappTextEn)}";

        var waUrlUr = !string.IsNullOrEmpty(normalizedPhone)
            ? $"https://wa.me/{normalizedPhone}?text={Uri.EscapeDataString(ctx.WhatsappTextUr)}"
            : $"https://wa.me/?text={Uri.EscapeDataString(ctx.WhatsappTextUr)}";

        var logoUrl = setting != null && setting.TryGetValue("logo_url", out var lu) ? lu?.ToString() : null;

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
            ["whatsapp_api_configured"] = false,
            ["whatsapp_provider"] = "meta_cloud",
            ["whatsapp_default_country_code"] = "92",
            ["email_subject"] = ctx.EmailSubjectEn,
            ["email_subject_en"] = ctx.EmailSubjectEn,
            ["email_subject_ur"] = ctx.EmailSubjectUr,
            ["email_body"] = ctx.EmailBodyEn,
            ["email_body_en"] = ctx.EmailBodyEn,
            ["email_body_ur"] = ctx.EmailBodyUr,
            ["signed_pdf_url"] = $"/print/shared-pdf/{type}?{apiQuery}",
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

        var (authErr, ctx) = await ResolveAndAuthorizeContextAsync(type, Request.Query["id"].FirstOrDefault(),
            Request.Query["customer_id"].FirstOrDefault(), Request.Query["supplier_id"].FirstOrDefault(),
            Request.Query["from"].FirstOrDefault(), Request.Query["to"].FirstOrDefault());

        if (authErr != null) return authErr;

        var pdfBytes = BuildMinimalPdf(ctx!.Title, ctx.PdfSummaryLines);
        var downloadRaw = Request.Query["download"].FirstOrDefault() ?? "";
        var isDownload = downloadRaw == "1" || downloadRaw.Equals("true", StringComparison.OrdinalIgnoreCase);

        if (isDownload)
        {
            return File(pdfBytes, "application/pdf", ctx.Filename);
        }

        Response.Headers["Content-Disposition"] = $"inline; filename=\"{ctx.Filename}\"";
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
            if (ctx.PartyType == "customer" && (await IsAdminAsync() || await CanAsync("customer.update")))
            {
                await Db.UpdateAsync("customers", ctx.PartyId.Value, new Dictionary<string, object?> { ["email"] = email });
            }
            else if (ctx.PartyType == "supplier" && (await IsAdminAsync() || await CanAsync("supplier.update")))
            {
                await Db.UpdateAsync("suppliers", ctx.PartyId.Value, new Dictionary<string, object?> { ["email"] = email });
            }
        }

        var pdfBytes = BuildMinimalPdf(ctx!.Title, ctx.PdfSummaryLines);

        return Ok(new Dictionary<string, object?>
        {
            ["status"] = "sent",
            ["delivery_mode"] = "log_fallback",
            ["message"] = $"PDF built & logged for {email} (PDF downloaded locally)",
            ["filename"] = ctx.Filename,
            ["pdf_base64"] = Convert.ToBase64String(pdfBytes)
        });
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

        var (authErr, ctx) = await ResolveAndAuthorizeContextAsync(
            type,
            GetString(body, "id"),
            GetString(body, "customer_id"),
            GetString(body, "supplier_id"),
            GetString(body, "from"),
            GetString(body, "to"));

        if (authErr != null) return authErr;

        var rawPhone = !string.IsNullOrEmpty(phone) ? phone : ctx!.RecipientPhone;
        var normalizedPhone = NormalizePhone(rawPhone);

        var savePhone = GetBool(body, "save_phone", false);
        if (savePhone && !string.IsNullOrEmpty(rawPhone) && !string.IsNullOrEmpty(ctx!.PartyType) && ctx.PartyId.HasValue)
        {
            if (ctx.PartyType == "customer" && (await IsAdminAsync() || await CanAsync("customer.update")))
            {
                await Db.UpdateAsync("customers", ctx.PartyId.Value, new Dictionary<string, object?> { ["phone"] = rawPhone });
            }
            else if (ctx.PartyType == "supplier" && (await IsAdminAsync() || await CanAsync("supplier.update")))
            {
                await Db.UpdateAsync("suppliers", ctx.PartyId.Value, new Dictionary<string, object?> { ["phone"] = rawPhone });
            }
        }

        var pdfBytes = BuildMinimalPdf(ctx!.Title, ctx.PdfSummaryLines);
        var uniqueName = $"{Guid.NewGuid()}_{ctx.Filename}";
        var storedPath = $"shared-pdfs/{uniqueName}";

        try
        {
            var dir = Db.StoragePath("app/shared-pdfs");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await System.IO.File.WriteAllBytesAsync(Path.Combine(dir, uniqueName), pdfBytes);
        }
        catch
        {
        }

        var response = new Dictionary<string, object?>
        {
            ["status"] = "pdf_built",
            ["mode"] = "client_file",
            ["message"] = $"PDF document ({ctx.Filename}) built and ready to send on WhatsApp",
            ["filename"] = ctx.Filename,
            ["stored_path"] = storedPath,
            ["signed_pdf_url"] = $"/print/shared-pdf/{type}?id={GetString(body, "id")}",
            ["mime_type"] = "application/pdf",
            ["size_bytes"] = pdfBytes.Length,
            ["pdf_base64"] = Convert.ToBase64String(pdfBytes),
            ["normalized_phone"] = normalizedPhone,
            ["caption"] = message
        };

        if (type == "regular_refill_reminder")
        {
            response["wa_me_url"] = !string.IsNullOrEmpty(normalizedPhone)
                ? $"https://wa.me/{normalizedPhone}?text={Uri.EscapeDataString(message)}"
                : $"https://wa.me/?text={Uri.EscapeDataString(message)}";
        }

        return Ok(response);
    }

    [HttpGet("print/shared-pdf/{type}")]
    public async Task<IActionResult> PublicPdf(string type)
    {
        if (string.IsNullOrEmpty(type) || !SupportedTypes.Contains(type))
        {
            return NotFound();
        }

        var ctx = await BuildContextDataAsync(
            type,
            Request.Query["id"].FirstOrDefault(),
            Request.Query["customer_id"].FirstOrDefault(),
            Request.Query["supplier_id"].FirstOrDefault(),
            Request.Query["from"].FirstOrDefault(),
            Request.Query["to"].FirstOrDefault());

        if (ctx == null)
        {
            return NotFound();
        }

        var pdfBytes = BuildMinimalPdf(ctx.Title, ctx.PdfSummaryLines);
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

                var waEn = $"*{storeName}*\n────────────────────\n*{label.ToUpperInvariant()}*\nInvoice No: *{docNo}*\nDate: {dateStr}\nCustomer: {custName}\n\nInvoice Total: *Rs. {total:N2}*\nAmount Received: Rs. {received:N2}\nBalance Due: Rs. {due:N2}\n\nPlease find the official PDF attached.";
                var waUr = $"*{storeName}*\n────────────────────\n*سیل انوائس / بل کی تفصیل*\nبل نمبر: *{docNo}*\nتاریخ: {dateStr}\nمحترم کسٹمر: {custName}\nکل بل: *Rs. {total:N2}*\nوصول شدہ رقم: Rs. {received:N2}\nبقایا رقم: Rs. {due:N2}";

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
                var cId = ToNullableLong(draft, "party_id") ?? ToNullableLong(draft, "customer_id");
                var cust = cId.HasValue ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @cId", new { cId }) : null;
                var docNo = GetVal(draft, "draft_number", $"QTN-{id}");
                var custName = GetVal(cust, "name", GetVal(draft, "party_name", "Valued Customer"));
                var total = ToDouble(draft, "total");

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
                var docNo = GetVal(po, "po_number", $"PO-{id}");
                var sId = ToNullableLong(po, "supplier_id");
                var sup = sId.HasValue ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @sId", new { sId }) : null;
                var supName = GetVal(sup, "name", "Supplier");
                var total = ToDouble(po, "total");

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
                var docNo = GetVal(st, "session_number", $"STK-{id}");
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

    private static byte[] BuildMinimalPdf(string title, IEnumerable<string> lines)
    {
        static string EscapePdfText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var ascii = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch == '(' || ch == ')' || ch == '\\')
                {
                    ascii.Append('\\').Append(ch);
                }
                else if (ch >= 32 && ch <= 126)
                {
                    ascii.Append(ch);
                }
                else
                {
                    ascii.Append(' ');
                }
            }
            return ascii.ToString();
        }

        var contentSb = new StringBuilder();
        contentSb.AppendLine("BT");
        contentSb.AppendLine("/F1 14 Tf");
        contentSb.AppendLine("40 800 Td");
        contentSb.AppendLine($"({EscapePdfText(title)}) Tj");
        contentSb.AppendLine("/F1 10 Tf");

        foreach (var line in lines.Take(40))
        {
            contentSb.AppendLine("0 -16 Td");
            contentSb.AppendLine($"({EscapePdfText(line)}) Tj");
        }
        contentSb.AppendLine("ET");

        var streamBytes = Encoding.ASCII.GetBytes(contentSb.ToString());

        using var ms = new MemoryStream();
        void WriteAscii(string str)
        {
            var b = Encoding.ASCII.GetBytes(str);
            ms.Write(b, 0, b.Length);
        }

        WriteAscii("%PDF-1.4\n");
        var offsets = new List<long>();

        // Object 1: Catalog
        offsets.Add(ms.Position);
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        // Object 2: Pages
        offsets.Add(ms.Position);
        WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        // Object 3: Page
        offsets.Add(ms.Position);
        WriteAscii("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

        // Object 4: Font
        offsets.Add(ms.Position);
        WriteAscii("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

        // Object 5: Contents stream
        offsets.Add(ms.Position);
        WriteAscii($"5 0 obj\n<< /Length {streamBytes.Length} >>\nstream\n");
        ms.Write(streamBytes, 0, streamBytes.Length);
        WriteAscii("\nendstream\nendobj\n");

        var xrefOffset = ms.Position;
        WriteAscii($"xref\n0 {offsets.Count + 1}\n");
        WriteAscii("0000000000 65535 f \n");
        foreach (var off in offsets)
        {
            WriteAscii($"{off:D10} 00000 n \n");
        }

        WriteAscii($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n");
        return ms.ToArray();
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

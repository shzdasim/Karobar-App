using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Karobar.Printing;

public sealed class SmtpConfiguration
{
    public bool Enabled { get; set; }
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 587;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string Encryption { get; set; } = "tls"; // "tls", "ssl", "none"
    public string FromAddress { get; set; } = "noreply@karobar.local";
    public string FromName { get; set; } = "Karobar App Store";
}

public sealed class WhatsAppConfiguration
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "local_agent"; // local_agent, waha, evolution_api, whapi_cloud, wasender, ultramsg, green_api, meta_cloud, custom_http
    public string AccessToken { get; set; } = "";
    public string PhoneNumberId { get; set; } = "";
    public string BusinessAccountId { get; set; } = "";
    public string ApiVersion { get; set; } = "v21.0";
    public string ApiUrl { get; set; } = "http://127.0.0.1:3002";
    public string DefaultCountryCode { get; set; } = "92";
    public string? TemplateName { get; set; }
    public string TemplateLanguage { get; set; } = "en_US";
    public bool AutoFallback { get; set; } = true;
}

/// <summary>
/// Native Document Share Engine (`app/Services/DocumentShareService.php` + `resources/views/emails/document_share.blade.php`).
/// Provides:
/// - E.164 phone normalization (`NormalizePhone`)
/// - Bilingual (English &amp; Urdu) WhatsApp &amp; Email caption/template rendering
/// - 30-minute HMAC-SHA256 signed public PDF URLs (`BuildSignedPdfUrl`)
/// - Multi-provider WhatsApp PDF &amp; Text delivery (`local_agent`, `waha`, `evolution_api`, `whapi_cloud`, `wasender`, `ultramsg`, `green_api`, `meta_cloud`, `custom_http`)
/// - MailKit SMTP email delivery with HTML template (`RenderDocumentShareEmailHtml`) and log fallback
/// - Automatic 24-hour temporary shared PDF cleanup (`CleanupExpiredSharedPdfs`).
/// </summary>
public sealed class DocumentShareEngine
{
    public static readonly string[] SupportedTypes =
    [
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
    ];

    public static readonly string[] SupportedPdfDesigns =
    [
        "standard",
        "minimal",
        "detailed",
        "compact",
        "bold",
        "barcode"
    ];

    public static readonly string[] SupportedPaperSizes =
    [
        "a4",
        "thermal"
    ];

    public static readonly string[] AllowedWhatsAppProviders =
    [
        "local_agent",
        "waha",
        "evolution_api",
        "whapi_cloud",
        "wasender",
        "ultramsg",
        "green_api",
        "meta_cloud",
        "custom_http"
    ];

    private static readonly Dictionary<string, string> UrduReplacements = new(StringComparer.Ordinal)
    {
        ["Customer Account Statement"] = "*کسٹمر کھاتہ اسٹیٹمنٹ (Account Statement)*",
        ["Supplier Account Statement"] = "*سپلائر کھاتہ اسٹیٹمنٹ (Supplier Statement)*",
        ["Official Payment Receipt:"] = "کیش وصولی رسید:",
        ["Payment Voucher:"] = "پیمنٹ واؤچر:",
        ["Purchase Order:"] = "پرچیز آرڈر:",
        ["Purchase Invoice (GRN):"] = "پرچیز انوائس (GRN):",
        ["Sale Return / Credit Note:"] = "سیل ریٹرن / کریڈٹ نوٹ:",
        ["Purchase Return / Debit Note:"] = "پرچیز ریٹرن / ڈیبٹ نوٹ:",
        ["Delivery Challan / Gate Pass:"] = "ڈیلیوری چالان / گیٹ پاس:",
        ["Price Quotation:"] = "پرائس کوٹیشن:",
        ["Customer:"] = "محترم کسٹمر:",
        ["Supplier:"] = "محترم سپلائر:",
        ["Consignee:"] = "وصول کنندہ:",
        ["Prepared For:"] = "کسٹمر کا نام:",
        ["Statement Period:"] = "مدت:",
        ["Order Date:"] = "آرڈر کی تاریخ:",
        ["Expected Delivery:"] = "متوقع ڈیلیوری:",
        ["Date:"] = "تاریخ:",
        ["Total Invoiced:"] = "کل بل رقم:",
        ["Total Received:"] = "کل وصول شدہ رقم:",
        ["Total Paid:"] = "کل ادا شدہ رقم:",
        ["Net Balance Due:"] = "موجودہ واجب الادا بقایا:",
        ["Net Payable Balance:"] = "موجودہ قابلِ ادا بقایا:",
        ["Amount Received:"] = "وصول شدہ رقم:",
        ["Amount Paid:"] = "ادا شدہ رقم:",
        ["Payment Method:"] = "طریقہ کار:",
        ["Reference:"] = "حوالہ نمبر:",
        ["Total Line Items:"] = "آئٹمز کی تعداد:",
        ["Order Total:"] = "کل آرڈر رقم:",
        ["Invoice Total:"] = "کل بل:",
        ["Balance Payable:"] = "بقایا واجب الادا:",
        ["Return Credit Total:"] = "واپسی بل کی کل رقم:",
        ["Return Debit Total:"] = "واپسی بل کی کل رقم:",
        ["Estimated Total:"] = "متوقع کل رقم:",
        ["Please find your detailed account statement attached."] = "آپ کے کھاتے کی مکمل تفصیل پی ڈی ایف (PDF) فائل کی صورت میں منسلک ہے۔\nآپ کے تعاون کا شکریہ۔",
        ["Please find the supplier ledger statement attached."] = "سپلائر کھاتے کی مکمل تفصیل پی ڈی ایف (PDF) فائل کی صورت میں منسلک ہے۔\nآپ کے تعاون کا شکریہ۔",
        ["Please find the official voucher attached."] = "پیمنٹ واؤچر پی ڈی ایف (PDF) فائل کی صورت میں منسلک ہے۔\nآپ کے تعاون کا شکریہ۔",
        ["Please find our official Purchase Order attached and confirm receipt."] = "ہمارا آفیشل پرچیز آرڈر پی ڈی ایف (PDF) فائل کی صورت میں منسلک ہے۔ براہِ کرم تصدیق فرمائیں۔",
        ["Please find the Purchase Invoice (GRN) PDF attached."] = "پرچیز انوائس پی ڈی ایف (PDF) فائل کی صورت میں منسلک ہے۔",
        ["Please find the Credit Note PDF attached."] = "سیل ریٹرن کریڈٹ نوٹ پی ڈی ایف (PDF) فائل کی صورت میں منسلک ہے۔",
        ["Please find the Debit Note PDF attached."] = "پرچیز ریٹرن ڈیبٹ نوٹ پی ڈی ایف (PDF) فائل کی صورت میں منسلک ہے۔",
        ["Please find our official price quotation attached."] = "ہماری آفیشل پرائس کوٹیشن پی ڈی ایف (PDF) فائل کی صورت میں منسلک ہے۔",
        ["Please verify quantities and batch numbers upon receipt."] = "براہِ کرم مال وصول کرتے وقت تعداد اور بیچ نمبر چیک کر لیں۔"
    };

    private static readonly string EphemeralSigningFallbackKey =
        "base64:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private readonly string _appRoot;
    private readonly PdfDocumentGenerator _pdfGenerator;
    private readonly HttpClient _httpClient;

    public static bool IsLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var clean = host.Trim().Trim('[', ']').TrimEnd('.');
        var pctIdx = clean.IndexOf('%');
        if (pctIdx >= 0) clean = clean[..pctIdx];
        if (clean.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(clean, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        return IPAddress.IsLoopback(ip);
    }

    private static bool IsBlockedMetadataOrLinkLocalHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;

        var clean = host.Trim().Trim('[', ']').TrimEnd('.');
        var pctIdx = clean.IndexOf('%');
        if (pctIdx >= 0)
        {
            clean = clean[..pctIdx];
        }

        if (string.IsNullOrWhiteSpace(clean) ||
            clean.Equals("metadata.google.internal", StringComparison.OrdinalIgnoreCase) ||
            clean.Equals("metadata.goog", StringComparison.OrdinalIgnoreCase) ||
            clean.StartsWith("metadata.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
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

            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                var bytes = ip.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    return true;
                }
            }
            else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && ip.IsIPv6LinkLocal)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeWhatsAppProvider(string? provider)
    {
        var raw = (provider ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(raw))
        {
            return "local_agent";
        }

        return AllowedWhatsAppProviders.Contains(raw) ? raw : "local_agent";
    }

    public static bool IsAllowedWhatsAppApiUrl(
        string? rawUrl,
        string? provider,
        out Uri? validatedUri,
        out string? errorMessage)
    {
        validatedUri = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(rawUrl) ||
            !Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
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

        var normalizedProvider = NormalizeWhatsAppProvider(provider);
        if (normalizedProvider == "local_agent" && !IsLoopbackHost(uri.Host))
        {
            errorMessage = "Local WhatsApp Agent URL must target localhost / loopback (127.0.0.1) only.";
            return false;
        }

        validatedUri = uri;
        return true;
    }

    private static void EnsureAllowedWhatsAppEndpoint(string? url, string provider)
    {
        if (!IsAllowedWhatsAppApiUrl(url, provider, out _, out var err))
        {
            throw new InvalidOperationException(err ?? "WhatsApp API URL is not allowed.");
        }
    }

    private static bool IsPrivateOrLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var clean = host.Trim().Trim('[', ']').TrimEnd('.');
        var pctIdx = clean.IndexOf('%');
        if (pctIdx >= 0) clean = clean[..pctIdx];
        if (clean.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!IPAddress.TryParse(clean, out var ip)) return false;
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        var b = ip.GetAddressBytes();
        if (b.Length == 4)
        {
            return b[0] == 10 ||
                   (b[0] == 172 && b[1] >= 16 && b[1] <= 31) ||
                   (b[0] == 192 && b[1] == 168);
        }
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;
    }

    public DocumentShareEngine(string? appRoot = null, PdfDocumentGenerator? pdfGenerator = null, HttpClient? httpClient = null)
    {
        _appRoot = Win32RawPrinterService.ResolveAppRoot(appRoot);
        _pdfGenerator = pdfGenerator ?? new PdfDocumentGenerator();
        if (httpClient != null)
        {
            _httpClient = httpClient;
        }
        else
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                // Enforce strict TLS certificate verification on public cloud APIs (Meta Graph, Whapi, UltraMsg, Green-API, Wasender);
                // only allow self-signed certificates on loopback / private LAN self-hosted gateways (WAHA / Evolution / custom LAN webhook).
                ServerCertificateCustomValidationCallback = (req, _, _, errors) =>
                    errors == System.Net.Security.SslPolicyErrors.None || IsPrivateOrLoopbackHost(req.RequestUri?.Host)
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        }
    }

    public string ResolvePaperSize(string? requested = null, PrintSettingViewModel? setting = null)
    {
        var candidate = (requested ?? "").Trim().ToLowerInvariant();
        if (SupportedPaperSizes.Contains(candidate))
        {
            return candidate;
        }
        var saved = (setting?.SharePaperSize ?? "a4").Trim().ToLowerInvariant();
        return SupportedPaperSizes.Contains(saved) ? saved : "a4";
    }

    public string ResolvePdfDesign(string? requested = null, PrintSettingViewModel? setting = null, string? paperSize = null)
    {
        var candidate = (requested ?? "").Trim().ToLowerInvariant();
        if (SupportedPdfDesigns.Contains(candidate))
        {
            return candidate;
        }
        var resolvedSize = !string.IsNullOrWhiteSpace(paperSize) ? paperSize : ResolvePaperSize(null, setting);
        var rawTemplate = resolvedSize == "thermal"
            ? (setting?.ThermalTemplate ?? "standard")
            : (setting?.A4Template ?? "standard");
        var saved = rawTemplate.Trim().ToLowerInvariant();
        return SupportedPdfDesigns.Contains(saved) ? saved : "standard";
    }

    public bool ResolveShowLogo(bool? requested = null, PrintSettingViewModel? setting = null)
    {
        return requested ?? (setting?.ShareShowLogo ?? true);
    }

    /// <summary>
    /// Normalize a local or international phone number into E.164 digits (without '+' or '00') suitable for https://wa.me/{phone}.
    /// </summary>
    public string NormalizePhone(string? phone, string? defaultCountryCode = "92")
    {
        if (string.IsNullOrWhiteSpace(phone))
        {
            return "";
        }

        var ccDigits = Regex.Replace(defaultCountryCode ?? "92", @"\D+", "");
        if (string.IsNullOrEmpty(ccDigits))
        {
            ccDigits = "92";
        }

        var digits = Regex.Replace(phone, @"\D+", "");
        if (string.IsNullOrEmpty(digits))
        {
            return "";
        }

        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            digits = digits[2..];
        }

        // Pakistani 11-digit mobile starting with 03 (e.g. 03001234567)
        if (digits.Length == 11 && digits.StartsWith("03", StringComparison.Ordinal))
        {
            return ccDigits + digits[1..];
        }

        // Pakistani 10-digit mobile starting with 3 (e.g. 3001234567)
        if (digits.Length == 10 && digits.StartsWith("3", StringComparison.Ordinal))
        {
            return ccDigits + digits;
        }

        // Generic leading 0 for local numbers (10-11 digits)
        if (digits.StartsWith("0", StringComparison.Ordinal) && digits.Length is >= 10 and <= 11)
        {
            return ccDigits + digits[1..];
        }

        return digits;
    }

    public string TranslateDocumentMessageToUrdu(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var result = text;
        foreach (var kv in UrduReplacements)
        {
            result = result.Replace(kv.Key, kv.Value, StringComparison.Ordinal);
        }
        return result;
    }

    /// <summary>
    /// Render customizable WhatsApp invoice message template with dynamic `{store_name}`, `{invoice_no}`, `{total}`, `{note}` placeholders.
    /// </summary>
    public string RenderWhatsAppInvoiceTemplate(string template, IDictionary<string, string?> vars)
    {
        var raw = (template ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        string GetVar(string key) => vars.TryGetValue(key, out var v) ? (v ?? "").Trim() : "";

        var noteVal = GetVar("note");
        var storeAddressVal = GetVar("store_address");
        var storePhoneVal = GetVar("store_phone");
        var itemsVal = GetVar("items");
        var totalBalanceVal = GetVar("total_balance");

        var lines = raw.Split('\n');
        var filteredLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrEmpty(storeAddressVal) && Regex.IsMatch(trimmed, @"^\{\{?(?:store_address|address)\}?\}\s*$", RegexOptions.IgnoreCase))
                continue;
            if (string.IsNullOrEmpty(storePhoneVal) && Regex.IsMatch(trimmed, @"\{\{?(?:store_phone|phone)\}?\}", RegexOptions.IgnoreCase))
                continue;
            if (string.IsNullOrEmpty(itemsVal) && Regex.IsMatch(trimmed, @"\{\{?(?:items|items_summary)\}?\}", RegexOptions.IgnoreCase))
                continue;
            if (string.IsNullOrEmpty(totalBalanceVal) && Regex.IsMatch(trimmed, @"\{\{?(?:total_balance|account_balance|ledger_balance)\}?\}", RegexOptions.IgnoreCase))
                continue;
            if (string.IsNullOrEmpty(noteVal) && Regex.IsMatch(trimmed, @"^\{\{?(?:note|closing_note|return_policy)\}?\}\s*$", RegexOptions.IgnoreCase))
                continue;

            filteredLines.Add(line);
        }

        var joined = string.Join("\n", filteredLines);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["{store_name}"] = GetVar("store_name"),
            ["{{store_name}}"] = GetVar("store_name"),
            ["{store_address}"] = storeAddressVal,
            ["{address}"] = storeAddressVal,
            ["{{store_address}}"] = storeAddressVal,
            ["{store_phone}"] = storePhoneVal,
            ["{phone}"] = storePhoneVal,
            ["{{store_phone}}"] = storePhoneVal,
            ["{invoice_no}"] = GetVar("invoice_no"),
            ["{document_no}"] = GetVar("invoice_no"),
            ["{bill_no}"] = GetVar("invoice_no"),
            ["{{invoice_no}}"] = GetVar("invoice_no"),
            ["{date}"] = GetVar("date"),
            ["{invoice_date}"] = GetVar("date"),
            ["{{date}}"] = GetVar("date"),
            ["{customer_name}"] = GetVar("customer_name"),
            ["{customer}"] = GetVar("customer_name"),
            ["{recipient_name}"] = GetVar("customer_name"),
            ["{{customer_name}}"] = GetVar("customer_name"),
            ["{items}"] = itemsVal,
            ["{items_summary}"] = itemsVal,
            ["{{items}}"] = itemsVal,
            ["{items_count}"] = GetVar("items_count"),
            ["{{items_count}}"] = GetVar("items_count"),
            ["{total_units}"] = GetVar("total_units"),
            ["{units}"] = GetVar("total_units"),
            ["{{total_units}}"] = GetVar("total_units"),
            ["{total}"] = GetVar("total"),
            ["{invoice_total}"] = GetVar("total"),
            ["{{total}}"] = GetVar("total"),
            ["{received}"] = GetVar("received"),
            ["{amount_received}"] = GetVar("received"),
            ["{{received}}"] = GetVar("received"),
            ["{balance_due}"] = GetVar("balance_due"),
            ["{remaining}"] = GetVar("balance_due"),
            ["{{balance_due}}"] = GetVar("balance_due"),
            ["{total_balance}"] = totalBalanceVal,
            ["{account_balance}"] = totalBalanceVal,
            ["{ledger_balance}"] = totalBalanceVal,
            ["{{total_balance}}"] = totalBalanceVal,
            ["{note}"] = noteVal,
            ["{closing_note}"] = noteVal,
            ["{return_policy}"] = noteVal,
            ["{{note}}"] = noteVal
        };

        foreach (var kv in replacements)
        {
            joined = joined.Replace(kv.Key, kv.Value, StringComparison.OrdinalIgnoreCase);
        }

        joined = Regex.Replace(joined, @"\n{3,}", "\n\n");
        return joined.Trim();
    }

    /// <summary>
    /// Build a 30-minute cryptographically signed public PDF URL for WhatsApp / external sharing.
    /// </summary>
    public string BuildSignedPdfUrl(
        string type,
        IDictionary<string, string?> queryParams,
        string baseUrl = "http://127.0.0.1:18960",
        string? appKey = null)
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(30).ToUnixTimeSeconds();
        var filtered = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in queryParams)
        {
            if (!string.IsNullOrWhiteSpace(kv.Value))
            {
                filtered[kv.Key] = kv.Value!;
            }
        }
        filtered["expires"] = expires.ToString(CultureInfo.InvariantCulture);

        var effectiveKey = !string.IsNullOrWhiteSpace(appKey)
            ? appKey
            : (Environment.GetEnvironmentVariable("APP_KEY") is { Length: > 0 } envKey
                ? envKey
                : EphemeralSigningFallbackKey);

        var queryString = string.Join("&", filtered.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var unsignedUrl = $"{baseUrl.TrimEnd('/')}/print/shared-pdf/{Uri.EscapeDataString(type)}?{queryString}";
        var keyBytes = Encoding.UTF8.GetBytes(effectiveKey);
        var sigBytes = HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(unsignedUrl));
        var signature = Convert.ToHexStringLower(sigBytes);

        return $"{unsignedUrl}&signature={signature}";
    }

    /// <summary>
    /// Renders `resources/views/emails/document_share.blade.php` HTML email body.
    /// </summary>
    public string RenderDocumentShareEmailHtml(
        string subjectLine,
        string storeName,
        string messageBody,
        string pdfFilename)
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        return $"""
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <title>{H(subjectLine)}</title>
</head>
<body style="font-family: Arial, Helvetica, sans-serif; font-size: 14px; color: #1e293b; line-height: 1.6; background-color: #f8fafc; padding: 24px; margin: 0;">
    <div style="max-width: 600px; margin: 0 auto; background: #ffffff; border: 1px solid #e2e8f0; border-radius: 8px; overflow: hidden;">
        <div style="background: #0f172a; color: #ffffff; padding: 18px 24px;">
            <h2 style="margin: 0; font-size: 18px; font-weight: bold;">{H(storeName)}</h2>
        </div>
        <div style="padding: 24px;">
            <div style="white-space: pre-line; color: #334155; font-size: 14px;">{H(messageBody)}</div>

            <div style="margin-top: 20px; padding: 12px 16px; background: #f1f5f9; border-left: 4px solid #3b82f6; border-radius: 4px; font-size: 13px; color: #475569;">
                <strong>Attached PDF:</strong> {H(pdfFilename)}
            </div>
        </div>
        <div style="padding: 14px 24px; background: #f8fafc; border-top: 1px solid #e2e8f0; font-size: 12px; color: #64748b; text-align: center;">
            Sent by {H(storeName)}
        </div>
    </div>
</body>
</html>
""";
    }

    /// <summary>
    /// Send a PDF document attachment via SMTP using MailKit, with automatic log fallback if the SMTP server is unreachable.
    /// </summary>
    public async Task<Dictionary<string, object?>> SendDocumentEmailAsync(
        string recipientEmail,
        string subject,
        string messageBody,
        byte[] pdfBytes,
        string pdfFilename,
        SmtpConfiguration smtpConfig,
        CancellationToken cancellationToken = default)
    {
        var htmlBody = RenderDocumentShareEmailHtml(subject, smtpConfig.FromName, messageBody, pdfFilename);
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(smtpConfig.FromName, string.IsNullOrWhiteSpace(smtpConfig.FromAddress) ? "noreply@karobar.local" : smtpConfig.FromAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(recipientEmail));
        mimeMessage.Subject = subject;

        var builder = new BodyBuilder
        {
            TextBody = messageBody,
            HtmlBody = htmlBody
        };
        builder.Attachments.Add(pdfFilename, pdfBytes, new ContentType("application", "pdf"));
        mimeMessage.Body = builder.ToMessageBody();

        var deliveryMode = "smtp";
        string? smtpError = null;

        if (smtpConfig.Enabled && !string.IsNullOrWhiteSpace(smtpConfig.Host))
        {
            try
            {
                using var client = new SmtpClient();
                client.Timeout = 10000;
                var secureOption = (smtpConfig.Encryption ?? "tls").ToLowerInvariant() switch
                {
                    "ssl" => SecureSocketOptions.SslOnConnect,
                    "tls" => SecureSocketOptions.StartTlsWhenAvailable,
                    _ => SecureSocketOptions.None
                };

                await client.ConnectAsync(smtpConfig.Host, smtpConfig.Port > 0 ? smtpConfig.Port : 587, secureOption, cancellationToken);
                if (!string.IsNullOrWhiteSpace(smtpConfig.Username))
                {
                    await client.AuthenticateAsync(smtpConfig.Username, smtpConfig.Password ?? "", cancellationToken);
                }
                await client.SendAsync(mimeMessage, cancellationToken);
                await client.DisconnectAsync(true, cancellationToken);
            }
            catch (Exception ex)
            {
                smtpError = ex.Message;
                deliveryMode = "log_fallback";
            }
        }
        else
        {
            deliveryMode = "log_fallback";
        }

        if (deliveryMode == "log_fallback")
        {
            try
            {
                var logDir = Path.Combine(_appRoot, "storage", "logs");
                Directory.CreateDirectory(logDir);
                var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] EMAIL_SHARE To={recipientEmail} Subject=\"{subject}\" Attachment={pdfFilename} ({pdfBytes.Length} bytes) Error={smtpError ?? "SMTP not configured"}{Environment.NewLine}";
                await File.AppendAllTextAsync(Path.Combine(logDir, "mail-share.log"), logLine, cancellationToken);
            }
            catch
            {
                // Ignore filesystem log errors
            }
        }

        var fallbackHostInfo = smtpConfig.Enabled
            ? $"{smtpConfig.Host}:{(smtpConfig.Port > 0 ? smtpConfig.Port : 587)}{(smtpError != null ? $" — {smtpError}" : "")}"
            : "127.0.0.1:1025";

        return new Dictionary<string, object?>
        {
            ["status"] = "sent",
            ["delivery_mode"] = deliveryMode,
            ["message"] = deliveryMode == "smtp"
                ? $"Document PDF sent successfully to {recipientEmail}"
                : $"PDF built & logged for {recipientEmail} (SMTP server failed on {fallbackHostInfo}; PDF downloaded locally)",
            ["filename"] = pdfFilename,
            ["pdf_base64"] = Convert.ToBase64String(pdfBytes)
        };
    }

    /// <summary>
    /// Dispatch a PDF document and caption through the configured WhatsApp API / Agent provider.
    /// Supports all 9 providers from `DocumentShareService.php`:
    /// `local_agent`, `waha`, `evolution_api`, `whapi_cloud`, `wasender`, `ultramsg`, `green_api`, `meta_cloud`, `custom_http`.
    /// </summary>
    public async Task<(string Provider, string MessageId)> DispatchPdfViaWhatsAppApiAsync(
        WhatsAppConfiguration config,
        string normalizedPhone,
        byte[] pdfBytes,
        string filename,
        string caption,
        string? signedPdfUrl = null,
        CancellationToken cancellationToken = default)
    {
        var provider = NormalizeWhatsAppProvider(config.Provider);
        var token = (config.AccessToken ?? "").Trim();
        var phoneOrInstanceId = (config.PhoneNumberId ?? "").Trim();
        var version = string.IsNullOrWhiteSpace(config.ApiVersion) ? "v20.0" : config.ApiVersion.Trim();
        var customUrl = (config.ApiUrl ?? "").Trim();
        if (!string.IsNullOrEmpty(customUrl))
        {
            EnsureAllowedWhatsAppEndpoint(customUrl, provider);
        }
        var pdfBase64 = Convert.ToBase64String(pdfBytes);

        // 1. Built-in Karobar Free WhatsApp Agent (Node.js + @whiskeysockets/baileys on port 3002)
        if (provider == "local_agent")
        {
            var baseUrl = (!string.IsNullOrEmpty(customUrl) ? customUrl : "http://127.0.0.1:3002").TrimEnd('/');
            var endpoint = baseUrl.EndsWith("/send-document", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : $"{baseUrl}/send-document";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrEmpty(token)) req.Headers.TryAddWithoutValidation("X-Api-Key", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                phone = normalizedPhone,
                filename,
                document_base64 = pdfBase64,
                caption
            }), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            var body = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                var errMsg = ExtractAgentJsonMessage(body) ?? body;
                throw new InvalidOperationException(errMsg);
            }

            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.ValueKind == JsonValueKind.False)
            {
                var err = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : body;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "Built-in Free WhatsApp Agent error." : err);
            }

            var msgId = doc.RootElement.TryGetProperty("message_id", out var idProp) ? (idProp.GetString() ?? "local_agent_sent") : "local_agent_sent";
            return ("local_agent", msgId);
        }

        // 2. WAHA (WhatsApp HTTP API)
        if (provider == "waha")
        {
            var baseUrl = (!string.IsNullOrEmpty(customUrl) ? customUrl : "http://localhost:3000").TrimEnd('/');
            var endpoint = baseUrl.EndsWith("/api/sendFile", StringComparison.OrdinalIgnoreCase) ? baseUrl : $"{baseUrl}/api/sendFile";
            var sessionName = !string.IsNullOrEmpty(phoneOrInstanceId) ? phoneOrInstanceId : "default";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrEmpty(token)) req.Headers.TryAddWithoutValidation("X-Api-Key", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                session = sessionName,
                chatId = $"{normalizedPhone}@c.us",
                file = new { mimetype = "application/pdf", filename, data = pdfBase64 },
                caption
            }), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"WAHA Open-Source WhatsApp API error: {await res.Content.ReadAsStringAsync(cancellationToken)}");
            }
            return ("waha", "waha_sent");
        }

        // 3. Evolution API
        if (provider == "evolution_api")
        {
            var baseUrl = (!string.IsNullOrEmpty(customUrl) ? customUrl : "http://localhost:8080").TrimEnd('/');
            var instance = !string.IsNullOrEmpty(phoneOrInstanceId) ? phoneOrInstanceId : "default";
            var endpoint = baseUrl.Contains("/message/sendMedia", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : $"{baseUrl}/message/sendMedia/{instance}";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrEmpty(token)) req.Headers.TryAddWithoutValidation("apikey", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                number = normalizedPhone,
                mediatype = "document",
                mimetype = "application/pdf",
                media = pdfBase64,
                fileName = filename,
                caption
            }), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Evolution API error: {await res.Content.ReadAsStringAsync(cancellationToken)}");
            }
            return ("evolution_api", "evolution_sent");
        }

        // 4. Whapi.Cloud
        if (provider == "whapi_cloud")
        {
            var endpoint = $"{(!string.IsNullOrEmpty(customUrl) ? customUrl : "https://gate.whapi.cloud").TrimEnd('/')}/messages/document";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);
            var dataUri = $"data:application/pdf;name={Uri.EscapeDataString(filename)};base64,{pdfBase64}";

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                to = normalizedPhone,
                media = dataUri,
                filename,
                caption
            }), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Whapi.Cloud Agent API error: {await res.Content.ReadAsStringAsync(cancellationToken)}");
            }
            return ("whapi_cloud", "whapi_sent");
        }

        // 5. Wasender
        if (provider == "wasender")
        {
            var endpoint = $"{(!string.IsNullOrEmpty(customUrl) ? customUrl : "https://www.wasenderapi.com").TrimEnd('/')}/api/send-message";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                phone = $"+{normalizedPhone}",
                text = caption,
                filename,
                document = $"data:application/pdf;base64,{pdfBase64}",
                documentUrl = signedPdfUrl
            }), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"WasenderAPI Agent error: {await res.Content.ReadAsStringAsync(cancellationToken)}");
            }
            return ("wasender", "wasender_sent");
        }

        // 6. UltraMsg
        if (provider == "ultramsg")
        {
            var cleanInstance = Regex.Replace(phoneOrInstanceId, "^instance", "", RegexOptions.IgnoreCase);
            var endpoint = $"{(!string.IsNullOrEmpty(customUrl) ? customUrl : "https://api.ultramsg.com").TrimEnd('/')}/instance{cleanInstance}/messages/document";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = token,
                    ["to"] = $"+{normalizedPhone}",
                    ["filename"] = filename,
                    ["document"] = pdfBase64,
                    ["caption"] = caption
                })
            };

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"UltraMsg WhatsApp Gateway error: {await res.Content.ReadAsStringAsync(cancellationToken)}");
            }
            return ("ultramsg", "ultramsg_sent");
        }

        // 7. Green-API
        if (provider == "green_api")
        {
            var cleanInstance = Regex.Replace(phoneOrInstanceId, "^waInstance", "", RegexOptions.IgnoreCase);
            var baseUrl = (!string.IsNullOrEmpty(customUrl) ? customUrl : "https://api.green-api.com").TrimEnd('/');
            var endpoint = $"{baseUrl}/waInstance{cleanInstance}/sendFileByUpload/{token}";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);

            using var form = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(pdfBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
            form.Add(fileContent, "file", filename);
            form.Add(new StringContent($"{normalizedPhone}@c.us"), "chatId");
            form.Add(new StringContent(filename), "fileName");
            form.Add(new StringContent(caption), "caption");

            using var res = await _httpClient.PostAsync(endpoint, form, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Green-API WhatsApp Gateway error: {await res.Content.ReadAsStringAsync(cancellationToken)}");
            }
            return ("green_api", "green_api_sent");
        }

        // 8. Custom HTTP Webhook
        if (provider == "custom_http")
        {
            if (string.IsNullOrEmpty(customUrl))
            {
                throw new InvalidOperationException("Custom WhatsApp API endpoint URL is not configured.");
            }
            EnsureAllowedWhatsAppEndpoint(customUrl, provider);
            using var req = new HttpRequestMessage(HttpMethod.Post, customUrl);
            if (!string.IsNullOrEmpty(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                to = normalizedPhone,
                phone = $"+{normalizedPhone}",
                caption,
                message = caption,
                filename,
                mime_type = "application/pdf",
                pdf_base64 = pdfBase64,
                media = $"data:application/pdf;base64,{pdfBase64}",
                signed_pdf_url = signedPdfUrl
            }), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Custom WhatsApp API error (HTTP {(int)res.StatusCode}): {await res.Content.ReadAsStringAsync(cancellationToken)}");
            }
            return ("custom_http", "custom_sent");
        }

        // 9. Official Meta WhatsApp Cloud API ('meta_cloud')
        var metaMediaEndpoint = $"https://graph.facebook.com/{version}/{phoneOrInstanceId}/media";
        var metaMsgEndpoint = $"https://graph.facebook.com/{version}/{phoneOrInstanceId}/messages";
        EnsureAllowedWhatsAppEndpoint(metaMediaEndpoint, provider);
        EnsureAllowedWhatsAppEndpoint(metaMsgEndpoint, provider);

        using var mediaForm = new MultipartFormDataContent();
        var pdfPart = new ByteArrayContent(pdfBytes);
        pdfPart.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        mediaForm.Add(pdfPart, "file", filename);
        mediaForm.Add(new StringContent("whatsapp"), "messaging_product");
        mediaForm.Add(new StringContent("application/pdf"), "type");

        using var mediaReq = new HttpRequestMessage(HttpMethod.Post, metaMediaEndpoint)
        {
            Content = mediaForm
        };
        mediaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var mediaRes = await _httpClient.SendAsync(mediaReq, cancellationToken);
        var mediaBody = await mediaRes.Content.ReadAsStringAsync(cancellationToken);
        if (!mediaRes.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to upload PDF document to WhatsApp Cloud API: {mediaBody}");
        }

        using var mediaDoc = JsonDocument.Parse(mediaBody);
        var mediaId = mediaDoc.RootElement.TryGetProperty("id", out var mid) ? mid.GetString() : null;
        if (string.IsNullOrEmpty(mediaId))
        {
            throw new InvalidOperationException($"Failed to upload PDF document to WhatsApp Cloud API: {mediaBody}");
        }

        using var msgReq = new HttpRequestMessage(HttpMethod.Post, metaMsgEndpoint);
        msgReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        msgReq.Content = new StringContent(JsonSerializer.Serialize(new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = normalizedPhone,
            type = "document",
            document = new { id = mediaId, filename, caption }
        }), Encoding.UTF8, "application/json");

        using var msgRes = await _httpClient.SendAsync(msgReq, cancellationToken);
        var msgBody = await msgRes.Content.ReadAsStringAsync(cancellationToken);
        if (!msgRes.IsSuccessStatusCode)
        {
            // Optional Meta 24-hour customer service window fallback via pre-approved template
            var tplName = (config.TemplateName ?? "").Trim();
            if (!string.IsNullOrWhiteSpace(tplName))
            {
                var tplLang = string.IsNullOrWhiteSpace(config.TemplateLanguage) ? "en_US" : config.TemplateLanguage.Trim();
                using var tplReq = new HttpRequestMessage(HttpMethod.Post, metaMsgEndpoint);
                tplReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                tplReq.Content = new StringContent(JsonSerializer.Serialize(new
                {
                    messaging_product = "whatsapp",
                    recipient_type = "individual",
                    to = normalizedPhone,
                    type = "template",
                    template = new
                    {
                        name = tplName,
                        language = new { code = tplLang },
                        components = new object[]
                        {
                            new
                            {
                                type = "header",
                                parameters = new object[]
                                {
                                    new
                                    {
                                        type = "document",
                                        document = new { id = mediaId, filename }
                                    }
                                }
                            }
                        }
                    }
                }), Encoding.UTF8, "application/json");

                using var tplRes = await _httpClient.SendAsync(tplReq, cancellationToken);
                if (tplRes.IsSuccessStatusCode)
                {
                    return ("meta_cloud", mediaId);
                }
            }

            throw new InvalidOperationException($"Failed to send PDF document via WhatsApp Cloud API: {msgBody}");
        }

        return ("meta_cloud", mediaId);
    }

    /// <summary>
    /// Dispatch a text-only WhatsApp message (e.g. medicine refill reminders, alerts) across all 9 providers.
    /// </summary>
    public async Task<(string Provider, string MessageId)> DispatchTextViaWhatsAppApiAsync(
        WhatsAppConfiguration config,
        string normalizedPhone,
        string messageText,
        CancellationToken cancellationToken = default)
    {
        var provider = NormalizeWhatsAppProvider(config.Provider);
        var token = (config.AccessToken ?? "").Trim();
        var phoneOrInstanceId = (config.PhoneNumberId ?? "").Trim();
        var version = string.IsNullOrWhiteSpace(config.ApiVersion) ? "v21.0" : config.ApiVersion.Trim();
        var customUrl = (config.ApiUrl ?? "").Trim();
        if (!string.IsNullOrEmpty(customUrl))
        {
            EnsureAllowedWhatsAppEndpoint(customUrl, provider);
        }

        if (provider == "local_agent")
        {
            var baseUrl = (!string.IsNullOrEmpty(customUrl) ? customUrl : "http://127.0.0.1:3002").TrimEnd('/');
            var endpoint = baseUrl.EndsWith("/send-text", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : (baseUrl.EndsWith("/send-document", StringComparison.OrdinalIgnoreCase)
                    ? baseUrl[..^"/send-document".Length] + "/send-text"
                    : $"{baseUrl}/send-text");
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);

            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrEmpty(token)) req.Headers.TryAddWithoutValidation("X-Api-Key", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                phone = normalizedPhone,
                text = messageText,
                message = messageText
            }), Encoding.UTF8, "application/json");

            using var res = await _httpClient.SendAsync(req, cancellationToken);
            var body = await res.Content.ReadAsStringAsync(cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                var errMsg = ExtractAgentJsonMessage(body) ?? body;
                throw new InvalidOperationException(errMsg);
            }
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
            if (doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.ValueKind == JsonValueKind.False)
            {
                var err = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : body;
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(err) ? "Built-in Free WhatsApp Agent error." : err);
            }
            var msgId = doc.RootElement.TryGetProperty("message_id", out var idProp) ? (idProp.GetString() ?? "local_agent_sent") : "local_agent_sent";
            return ("local_agent", msgId);
        }

        if (provider == "waha")
        {
            var baseUrl = (!string.IsNullOrEmpty(customUrl) ? customUrl : "http://localhost:3000").TrimEnd('/');
            var endpoint = $"{baseUrl}/api/sendText";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);
            var sessionName = !string.IsNullOrEmpty(phoneOrInstanceId) ? phoneOrInstanceId : "default";
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrEmpty(token)) req.Headers.TryAddWithoutValidation("X-Api-Key", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                session = sessionName,
                chatId = $"{normalizedPhone}@c.us",
                text = messageText
            }), Encoding.UTF8, "application/json");
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"WAHA error: {await res.Content.ReadAsStringAsync(cancellationToken)}");
            return ("waha", "waha_sent");
        }

        if (provider == "evolution_api")
        {
            var baseUrl = (!string.IsNullOrEmpty(customUrl) ? customUrl : "http://localhost:8080").TrimEnd('/');
            var instance = !string.IsNullOrEmpty(phoneOrInstanceId) ? phoneOrInstanceId : "default";
            var endpoint = $"{baseUrl}/message/sendText/{instance}";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            if (!string.IsNullOrEmpty(token)) req.Headers.TryAddWithoutValidation("apikey", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                number = normalizedPhone,
                text = messageText
            }), Encoding.UTF8, "application/json");
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Evolution API error: {await res.Content.ReadAsStringAsync(cancellationToken)}");
            return ("evolution_api", "evolution_sent");
        }

        if (provider == "whapi_cloud")
        {
            var endpoint = $"{(!string.IsNullOrEmpty(customUrl) ? customUrl : "https://gate.whapi.cloud").TrimEnd('/')}/messages/text";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new { to = normalizedPhone, body = messageText }), Encoding.UTF8, "application/json");
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Whapi.Cloud error: {await res.Content.ReadAsStringAsync(cancellationToken)}");
            return ("whapi_cloud", "whapi_sent");
        }

        if (provider == "wasender")
        {
            var endpoint = $"{(!string.IsNullOrEmpty(customUrl) ? customUrl : "https://www.wasenderapi.com").TrimEnd('/')}/api/send-message";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new { phone = $"+{normalizedPhone}", text = messageText }), Encoding.UTF8, "application/json");
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"WasenderAPI error: {await res.Content.ReadAsStringAsync(cancellationToken)}");
            return ("wasender", "wasender_sent");
        }

        if (provider == "ultramsg")
        {
            var cleanInstance = Regex.Replace(phoneOrInstanceId, "^instance", "", RegexOptions.IgnoreCase);
            var endpoint = $"{(!string.IsNullOrEmpty(customUrl) ? customUrl : "https://api.ultramsg.com").TrimEnd('/')}/instance{cleanInstance}/messages/chat";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["token"] = token,
                    ["to"] = $"+{normalizedPhone}",
                    ["body"] = messageText
                })
            };
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"UltraMsg error: {await res.Content.ReadAsStringAsync(cancellationToken)}");
            return ("ultramsg", "ultramsg_sent");
        }

        if (provider == "green_api")
        {
            var cleanInstance = Regex.Replace(phoneOrInstanceId, "^waInstance", "", RegexOptions.IgnoreCase);
            var baseUrl = (!string.IsNullOrEmpty(customUrl) ? customUrl : "https://api.green-api.com").TrimEnd('/');
            var endpoint = $"{baseUrl}/waInstance{cleanInstance}/sendMessage/{token}";
            EnsureAllowedWhatsAppEndpoint(endpoint, provider);
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Content = new StringContent(JsonSerializer.Serialize(new { chatId = $"{normalizedPhone}@c.us", message = messageText }), Encoding.UTF8, "application/json");
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Green-API error: {await res.Content.ReadAsStringAsync(cancellationToken)}");
            return ("green_api", "green_api_sent");
        }

        if (provider == "custom_http")
        {
            if (string.IsNullOrEmpty(customUrl)) throw new InvalidOperationException("Custom WhatsApp API endpoint URL is not configured.");
            EnsureAllowedWhatsAppEndpoint(customUrl, provider);
            using var req = new HttpRequestMessage(HttpMethod.Post, customUrl);
            if (!string.IsNullOrEmpty(token)) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(new
            {
                to = normalizedPhone,
                phone = $"+{normalizedPhone}",
                text = messageText,
                message = messageText
            }), Encoding.UTF8, "application/json");
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"Custom WhatsApp API error (HTTP {(int)res.StatusCode}): {await res.Content.ReadAsStringAsync(cancellationToken)}");
            return ("custom_http", "custom_sent");
        }

        // Official Meta Cloud API text message
        var metaTextEndpoint = $"https://graph.facebook.com/{version}/{phoneOrInstanceId}/messages";
        EnsureAllowedWhatsAppEndpoint(metaTextEndpoint, provider);
        using var metaReq = new HttpRequestMessage(HttpMethod.Post, metaTextEndpoint);
        metaReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        metaReq.Content = new StringContent(JsonSerializer.Serialize(new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = normalizedPhone,
            type = "text",
            text = new { body = messageText }
        }), Encoding.UTF8, "application/json");
        using var metaRes = await _httpClient.SendAsync(metaReq, cancellationToken);
        var metaBody = await metaRes.Content.ReadAsStringAsync(cancellationToken);
        if (!metaRes.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to send text via WhatsApp Cloud API: {metaBody}");
        }
        return ("meta_cloud", "meta_text_sent");
    }

    public async Task<Dictionary<string, object?>> SendOrBuildWhatsAppTextAsync(
        string? rawPhone,
        string messageText,
        WhatsAppConfiguration waConfig,
        CancellationToken cancellationToken = default)
    {
        var normalizedPhone = NormalizePhone(rawPhone, waConfig.DefaultCountryCode);
        var waMeUrl = !string.IsNullOrEmpty(normalizedPhone)
            ? $"https://wa.me/{normalizedPhone}?text={Uri.EscapeDataString(messageText)}"
            : $"https://wa.me/?text={Uri.EscapeDataString(messageText)}";

        if (waConfig.Enabled && !string.IsNullOrEmpty(normalizedPhone))
        {
            try
            {
                var (provider, msgId) = await DispatchTextViaWhatsAppApiAsync(waConfig, normalizedPhone, messageText, cancellationToken);
                return new Dictionary<string, object?>
                {
                    ["status"] = "sent_via_api",
                    ["mode"] = "cloud_api",
                    ["provider"] = provider,
                    ["message"] = $"WhatsApp message sent directly to +{normalizedPhone}",
                    ["normalized_phone"] = normalizedPhone,
                    ["whatsapp_msg_id"] = msgId,
                    ["wa_me_url"] = waMeUrl
                };
            }
            catch (Exception ex)
            {
                if (!waConfig.AutoFallback)
                {
                    throw;
                }
                return new Dictionary<string, object?>
                {
                    ["status"] = "api_fallback",
                    ["mode"] = "wa_me_fallback",
                    ["provider"] = waConfig.Provider,
                    ["api_error"] = ex.Message,
                    ["message"] = $"WhatsApp API unavailable ({ex.Message}); opening WhatsApp chat instead.",
                    ["normalized_phone"] = normalizedPhone,
                    ["wa_me_url"] = waMeUrl,
                    ["caption"] = messageText
                };
            }
        }

        return new Dictionary<string, object?>
        {
            ["status"] = "wa_me_fallback",
            ["mode"] = "wa_me_fallback",
            ["provider"] = "wa_me",
            ["message"] = $"WhatsApp message prepared for +{normalizedPhone}",
            ["normalized_phone"] = normalizedPhone,
            ["wa_me_url"] = waMeUrl,
            ["caption"] = messageText
        };
    }

    /// <summary>
    /// Save shared PDF to `storage/app/shared-pdfs/` and dispatch or return client file fallback.
    /// </summary>
    public async Task<Dictionary<string, object?>> SendOrBuildWhatsAppPdfAsync(
        byte[] pdfBytes,
        string filename,
        string? rawPhone,
        string caption,
        WhatsAppConfiguration waConfig,
        string? signedPdfUrl = null,
        CancellationToken cancellationToken = default)
    {
        CleanupExpiredSharedPdfs();

        var sharedDir = Path.Combine(_appRoot, "storage", "app", "shared-pdfs");
        Directory.CreateDirectory(sharedDir);
        var safeFilename = Path.GetFileName((filename ?? "document.pdf").Trim().Replace('\\', '/'));
        if (string.IsNullOrWhiteSpace(safeFilename) || safeFilename is "." or "..")
        {
            safeFilename = "document.pdf";
        }
        var uniqueName = $"{Guid.NewGuid():D}_{safeFilename}";
        var storedRelPath = $"shared-pdfs/{uniqueName}";
        await File.WriteAllBytesAsync(Path.Combine(sharedDir, uniqueName), pdfBytes, cancellationToken);

        var normalizedPhone = NormalizePhone(rawPhone, waConfig.DefaultCountryCode);

        if (waConfig.Enabled && !string.IsNullOrEmpty(normalizedPhone))
        {
            try
            {
                var (provider, msgId) = await DispatchPdfViaWhatsAppApiAsync(
                    waConfig,
                    normalizedPhone,
                    pdfBytes,
                    safeFilename,
                    caption,
                    signedPdfUrl,
                    cancellationToken);

                return new Dictionary<string, object?>
                {
                    ["status"] = "sent_via_api",
                    ["mode"] = "cloud_api",
                    ["provider"] = provider,
                    ["message"] = $"PDF document ({safeFilename}) sent directly to WhatsApp (+{normalizedPhone})",
                    ["filename"] = safeFilename,
                    ["stored_path"] = storedRelPath,
                    ["signed_pdf_url"] = signedPdfUrl,
                    ["normalized_phone"] = normalizedPhone,
                    ["whatsapp_msg_id"] = msgId
                };
            }
            catch (Exception ex)
            {
                if (!waConfig.AutoFallback)
                {
                    throw;
                }

                return new Dictionary<string, object?>
                {
                    ["status"] = "api_fallback",
                    ["mode"] = "client_file",
                    ["provider"] = waConfig.Provider,
                    ["api_error"] = ex.Message,
                    ["message"] = $"WhatsApp API could not deliver directly ({ex.Message}); PDF downloaded locally & opening WhatsApp chat",
                    ["filename"] = safeFilename,
                    ["stored_path"] = storedRelPath,
                    ["signed_pdf_url"] = signedPdfUrl,
                    ["mime_type"] = "application/pdf",
                    ["size_bytes"] = pdfBytes.Length,
                    ["pdf_base64"] = Convert.ToBase64String(pdfBytes),
                    ["normalized_phone"] = normalizedPhone,
                    ["caption"] = caption
                };
            }
        }

        return new Dictionary<string, object?>
        {
            ["status"] = "pdf_built",
            ["mode"] = "client_file",
            ["message"] = $"PDF document ({safeFilename}) built and ready to send on WhatsApp",
            ["filename"] = safeFilename,
            ["stored_path"] = storedRelPath,
            ["signed_pdf_url"] = signedPdfUrl,
            ["mime_type"] = "application/pdf",
            ["size_bytes"] = pdfBytes.Length,
            ["pdf_base64"] = Convert.ToBase64String(pdfBytes),
            ["normalized_phone"] = normalizedPhone,
            ["caption"] = caption
        };
    }

    /// <summary>
    /// Purge temporary shared PDFs older than `maxAgeSeconds` (default 24 hours = 86400s) from private storage
    /// and remove any legacy files from public/shared-pdfs.
    /// </summary>
    private static string? ExtractAgentJsonMessage(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("message", out var msgProp) &&
                msgProp.ValueKind == JsonValueKind.String)
            {
                return msgProp.GetString();
            }
        }
        catch { }
        return null;
    }

    public int CleanupExpiredSharedPdfs(int maxAgeSeconds = 86400)
    {
        var deleted = 0;
        var cutoff = DateTime.UtcNow.AddSeconds(-maxAgeSeconds);

        var privateDir = Path.Combine(_appRoot, "storage", "app", "shared-pdfs");
        if (Directory.Exists(privateDir))
        {
            try
            {
                foreach (var file in Directory.GetFiles(privateDir))
                {
                    if (File.GetLastWriteTimeUtc(file) <= cutoff)
                    {
                        File.Delete(file);
                        deleted++;
                    }
                }
            }
            catch
            {
                // Ignore opportunistic cleanup errors
            }
        }

        var publicDir = Path.Combine(_appRoot, "storage", "app", "public", "shared-pdfs");
        if (Directory.Exists(publicDir))
        {
            try
            {
                foreach (var file in Directory.GetFiles(publicDir))
                {
                    File.Delete(file);
                    deleted++;
                }
            }
            catch
            {
                // Ignore opportunistic cleanup errors
            }
        }

        return deleted;
    }
}

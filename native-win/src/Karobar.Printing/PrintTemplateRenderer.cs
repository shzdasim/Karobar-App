using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace Karobar.Printing;

public sealed class PrintSettingViewModel
{
    public string StoreName { get; set; } = "KAROBAR PHARMACY & STORE";
    public string PhoneNumber { get; set; } = "";
    public string Address { get; set; } = "";
    public string LicenseNumber { get; set; } = "";
    public string? LogoUrl { get; set; }
    public string Note { get; set; } = "";
    public int? PrintPaperWidthMm { get; set; }
    public int PrintMarginTopMm { get; set; }
    public int PrintMarginBottomMm { get; set; } = 4;
    public int PrintMarginLeftMm { get; set; }
    public int PrintMarginRightMm { get; set; }
    public int PrintFontScale { get; set; } = 100;
    public string PrintContrastMode { get; set; } = "standard";
    public string PrintScaleMode { get; set; } = "auto";
    public bool PrintShowLogo { get; set; } = true;
    public string SharePaperSize { get; set; } = "a4";
    public bool ShareShowLogo { get; set; } = true;
    public string ThermalTemplate { get; set; } = "standard";
    public string A4Template { get; set; } = "standard";
    public bool FbrEnabled { get; set; }
    public string FbrEnvironment { get; set; } = "sandbox";
    public string FbrMode { get; set; } = "tier1_pos_cloud";
    public string FbrSellerNtn { get; set; } = "";
    public string FbrSellerStrn { get; set; } = "";
    public string FbrPosId { get; set; } = "966130";
    public bool FbrPosFeeEnabled { get; set; }
    public decimal FbrPosFeeAmount { get; set; } = 1.00m;
}

public sealed class PrintInvoiceItemViewModel
{
    public string ProductName { get; set; } = "-";
    public string? Barcode { get; set; }
    public string? ProductCode { get; set; }
    public int PackSize { get; set; } = 1;
    public decimal Quantity { get; set; } = 1;
    public decimal PackQuantity { get; set; }
    public decimal UnitQuantity { get; set; }
    public int Bonus { get; set; }
    public decimal PackBonus { get; set; }
    public decimal Price { get; set; }
    public decimal PackPrice { get; set; }
    public decimal ItemDiscountPercentage { get; set; }
    public decimal ItemDiscountAmount { get; set; }
    public decimal SubTotal { get; set; }
    public string? BatchNumber { get; set; }
    public string? Expiry { get; set; }
    public string? DosageInstruction { get; set; }
}

public sealed class PrintInvoiceViewModel
{
    public int Id { get; set; } = 1;
    public string PostedNumber { get; set; } = "INV-001";
    public DateTime? Date { get; set; } = DateTime.Now;
    public string SaleType { get; set; } = "retail";
    public string InvoiceType { get; set; } = "debit";
    public string PaymentMethod { get; set; } = "cash";
    public string? UserName { get; set; } = "Admin User";
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? CustomerAddress { get; set; }
    public string? DoctorName { get; set; }
    public string? PatientName { get; set; }
    public string? Remarks { get; set; }
    public string? FooterNote { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal ItemDiscountTotal { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal DiscountPercentage { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TaxPercentage { get; set; }
    public decimal Total { get; set; }
    public decimal TotalReceive { get; set; }
    public decimal? CustomerTotalDue { get; set; }
    public string? FbrInvoiceNumber { get; set; }
    public string? FbrQrData { get; set; }
    public string? FbrUsin { get; set; }
    public decimal FbrPosFee { get; set; }
    public List<PrintInvoiceItemViewModel> Items { get; set; } = [];
}

public sealed class PrintStickerConfigViewModel
{
    public string Template { get; set; } = "classic";
    public int WidthMm { get; set; } = 50;
    public int HeightMm { get; set; } = 25;
    public string PrinterMode { get; set; } = "receipt_roll";
    public string PresetKey { get; set; } = "50x25";
    public string FontScale { get; set; } = "normal";
    public string ContrastMode { get; set; } = "high_contrast";
    public string ScaleMode { get; set; } = "auto";
    public int MarginTopMm { get; set; }
    public int MarginBottomMm { get; set; } = 4;
    public int MarginLeftMm { get; set; }
    public int MarginRightMm { get; set; }
    public bool ShowShopName { get; set; } = true;
    public bool ShowInvoiceMeta { get; set; } = true;
    public bool ShowPatient { get; set; } = true;
    public bool ShowQuantity { get; set; } = true;
    public bool ShowBatch { get; set; } = true;
    public bool ShowExpiry { get; set; } = true;
    public bool ShowDoctor { get; set; }
    public string FooterNote { get; set; } = "Keep out of reach of children";
    public double FontSizeHeader { get; set; } = 6.5;
    public double FontSizePatient { get; set; } = 6.0;
    public double? FontSizeProduct { get; set; }
    public double? FontSizeDosage { get; set; }
    public double FontSizeFooter { get; set; } = 5.5;
    public int FontWeightHeader { get; set; } = 800;
    public int FontWeightPatient { get; set; } = 600;
    public int FontWeightProduct { get; set; } = 900;
    public int FontWeightDosage { get; set; } = 800;
    public int FontWeightFooter { get; set; } = 700;
}

public sealed class PrintLedgerRowViewModel
{
    public string EntryDate { get; set; } = "";
    public string EntryType { get; set; } = "invoice";
    public string? PostedNumber { get; set; }
    public string? InvoiceNumber { get; set; }
    public decimal InvoiceTotal { get; set; }
    public decimal TotalReceived { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal CreditedAmount { get; set; }
    public decimal DebitedAmount { get; set; }
    public string? PaymentRef { get; set; }
    public decimal CreditRemainingCalc { get; set; }
    public decimal RunningBalance { get; set; }
    public string? Description { get; set; }
}

public sealed class PrintLedgerSummaryViewModel
{
    public decimal OpeningBalance { get; set; }
    public decimal TotalInvoiced { get; set; }
    public decimal ReceivedOnInvoice { get; set; }
    public decimal PaidOnInvoice { get; set; }
    public decimal PaymentsCredited { get; set; }
    public decimal PaymentsDebited { get; set; }
    public decimal NetBalance { get; set; }
}

public sealed class PrintCashDrawerZReportViewModel
{
    public int SessionId { get; set; } = 1;
    public string Status { get; set; } = "open";
    public string CashierName { get; set; } = "Cashier";
    public DateTime? OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string Duration { get; set; } = "—";
    public decimal OpeningBalance { get; set; }
    public decimal TotalCashIn { get; set; }
    public decimal TotalCashOut { get; set; }
    public decimal ExpectedBalance { get; set; }
    public decimal? ClosingBalanceActual { get; set; }
    public decimal? Difference { get; set; }
    public string? Notes { get; set; }

    public int TotalInvoicesCount { get; set; }
    public int RetailInvoicesCount { get; set; }
    public int WholesaleInvoicesCount { get; set; }
    public decimal GrossSales { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal NetSales { get; set; }
    public decimal CashTenderSales { get; set; }
    public decimal NonCashSales { get; set; }
    public decimal CreditUdharSales { get; set; }
    public int SaleReturnsCount { get; set; }
    public decimal SaleReturnsTotal { get; set; }
    public decimal CashRefundReturns { get; set; }
    public decimal CreditAdjReturns { get; set; }

    public int CashSalesCount { get; set; }
    public decimal CashSalesAmount { get; set; }
    public int CustomerPaymentsCount { get; set; }
    public decimal CustomerPaymentsAmount { get; set; }
    public int PurchaseReturnsCount { get; set; }
    public decimal PurchaseReturnsAmount { get; set; }
    public int ManualCashInCount { get; set; }
    public decimal ManualCashInAmount { get; set; }

    public int CashPurchasesCount { get; set; }
    public decimal CashPurchasesAmount { get; set; }
    public int CashReturnsCount { get; set; }
    public decimal CashReturnsAmount { get; set; }
    public int SupplierPaymentsCount { get; set; }
    public decimal SupplierPaymentsAmount { get; set; }
    public int ExpensesCount { get; set; }
    public decimal ExpensesAmount { get; set; }
    public int StaffAdvancesCount { get; set; }
    public decimal StaffAdvancesAmount { get; set; }
    public int ManualCashOutCount { get; set; }
    public decimal ManualCashOutAmount { get; set; }

    public Dictionary<string, decimal> ClosingDenominations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Renders all 22 printer views in `resources/views/printer/*`:
/// - 7 Thermal Sale Invoice templates (`standard`, `minimal`, `detailed`, `compact`, `bold`, `barcode` + base `thermal`)
/// - 6 A4 Sale Invoice templates (`standard`, `minimal`, `detailed`, `compact`, `bold`, `barcode`)
/// - Patient Urdu/English Dosage Sticker renderer (`classic`, `minimal`, `bold`, `urdu_focus`)
/// - Cash Drawer Z-Report (`cash_drawer_thermal_zreport`)
/// - Customer &amp; Supplier Ledgers (`a4` &amp; `thermal`)
/// - Calibration styles (`calibration_styles`), FBR QR footer (`fbr_fiscal_footer`), and all 4 live preview endpoints.
/// </summary>
public sealed class PrintTemplateRenderer
{
    public static readonly string[] ValidInvoiceTemplates =
        ["standard", "minimal", "detailed", "compact", "bold", "barcode"];

    public static readonly string[] ValidStickerTemplates =
        ["classic", "minimal", "bold", "urdu_focus"];

    private static string H(string? text) => WebUtility.HtmlEncode(text ?? "");
    private static string F2(decimal val) => val.ToString("N2", CultureInfo.InvariantCulture);
    private static string F1(decimal val) => val.ToString("N1", CultureInfo.InvariantCulture);
    private static string F0(decimal val) => Math.Round(val, 0).ToString("N0", CultureInfo.InvariantCulture);

    private static string Nf(decimal val)
    {
        if (Math.Floor(val) == val)
        {
            return val.ToString("N0", CultureInfo.InvariantCulture);
        }
        return val.ToString("N2", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
    }

    private static string ComputeSha256Hex(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    private static string ComputeMd5Hex(string input)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(bytes);
    }

    public static (bool? IsThermal, string? ScaleMode) ExtractPrintMetaHints(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return (null, null);
        }

        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(
                html,
                @"<meta\s+[^>]*name=[""']karobar-print-config[""'][^>]*content=[""']([^""']+)[""']",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                match = System.Text.RegularExpressions.Regex.Match(
                    html,
                    @"<meta\s+[^>]*content=[""']([^""']+)[""'][^>]*name=[""']karobar-print-config[""']",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            if (!match.Success)
            {
                return (null, null);
            }

            var decoded = WebUtility.HtmlDecode(match.Groups[1].Value);
            using var doc = JsonDocument.Parse(decoded);
            bool? isThermal = null;
            string? scaleMode = null;
            if (doc.RootElement.TryGetProperty("isThermal", out var thProp) &&
                (thProp.ValueKind == JsonValueKind.True || thProp.ValueKind == JsonValueKind.False))
            {
                isThermal = thProp.GetBoolean();
            }
            if (doc.RootElement.TryGetProperty("scaleMode", out var scProp) &&
                scProp.ValueKind == JsonValueKind.String)
            {
                scaleMode = scProp.GetString();
            }
            return (isThermal, scaleMode);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Renders `resources/views/printer/partials/calibration_styles.blade.php`.
    /// </summary>
    public string RenderCalibrationStyles(
        PrintSettingViewModel setting,
        IDictionary<string, string>? queryOverrides = null,
        bool isThermal = true)
    {
        int GetInt(string key, int fallback)
        {
            if (queryOverrides != null && queryOverrides.TryGetValue(key, out var raw) && int.TryParse(raw, out var parsed))
            {
                return parsed;
            }
            return fallback;
        }

        bool GetBool(string key, bool fallback)
        {
            if (queryOverrides != null && queryOverrides.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                return raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                       raw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                       raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
            }
            return fallback;
        }

        string GetStr(string key, string fallback)
        {
            if (queryOverrides != null && queryOverrides.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
            {
                return raw.Trim();
            }
            return fallback;
        }

        var calPaperWidthMm = isThermal ? GetInt("paper_width_mm", setting.PrintPaperWidthMm ?? 0) : 0;
        var calMarginTopMm = isThermal ? GetInt("margin_top_mm", setting.PrintMarginTopMm) : 0;
        var calMarginBottomMm = isThermal ? GetInt("margin_bottom_mm", setting.PrintMarginBottomMm) : 0;
        var calMarginLeftMm = isThermal ? GetInt("margin_left_mm", setting.PrintMarginLeftMm) : 0;
        var calMarginRightMm = isThermal ? GetInt("margin_right_mm", setting.PrintMarginRightMm) : 0;
        var calFontScale = Math.Clamp(GetInt("font_scale", setting.PrintFontScale <= 0 ? 100 : setting.PrintFontScale), 70, 150);

        var calContrastMode = GetStr("contrast_mode", setting.PrintContrastMode);
        if (calContrastMode is not ("standard" or "high_contrast" or "extra_bold"))
        {
            calContrastMode = "standard";
        }

        var calScaleMode = GetStr("scale_mode", setting.PrintScaleMode);
        if (calScaleMode is not ("auto" or "noscale" or "fit" or "shrink"))
        {
            calScaleMode = "auto";
        }

        var calShowLogo = queryOverrides != null && queryOverrides.ContainsKey("print_show_logo")
            ? GetBool("print_show_logo", setting.PrintShowLogo)
            : (queryOverrides != null && queryOverrides.ContainsKey("show_logo")
                ? GetBool("show_logo", setting.PrintShowLogo)
                : setting.PrintShowLogo);

        var calConfigJson = JsonSerializer.Serialize(new
        {
            isThermal,
            paperWidthMm = calPaperWidthMm,
            marginTopMm = calMarginTopMm,
            marginBottomMm = calMarginBottomMm,
            marginLeftMm = calMarginLeftMm,
            marginRightMm = calMarginRightMm,
            fontScale = calFontScale,
            contrastMode = calContrastMode,
            scaleMode = calScaleMode,
            showLogo = calShowLogo
        });

        var sb = new StringBuilder();
        sb.AppendLine($"<meta name=\"karobar-print-config\" content=\"{H(calConfigJson)}\">");
        sb.AppendLine("<style id=\"karobar-print-calibration\">");

        if (!calShowLogo)
        {
            sb.AppendLine("  .logo, .wm, .watermark { display: none !important; }");
        }

        if (isThermal && calPaperWidthMm > 0)
        {
            sb.AppendLine($"  @media print {{ @page {{ size: {calPaperWidthMm}mm auto; margin: 0; }} }}");
            sb.AppendLine($"  .receipt {{ width: {calPaperWidthMm}mm !important; max-width: 100% !important; }}");
            sb.AppendLine($"  body:not(:has(.receipt)):not(:has(.page)):not(:has(.labels-wrapper)) {{ width: {calPaperWidthMm}mm !important; max-width: 100% !important; }}");
        }

        if (isThermal && (calMarginTopMm != 0 || calMarginBottomMm != 0 || calMarginLeftMm != 0 || calMarginRightMm != 0))
        {
            var padRules = new StringBuilder();
            if (calMarginTopMm != 0) padRules.Append($"padding-top: {Math.Max(0, calMarginTopMm)}mm !important; ");
            if (calMarginBottomMm > 0) padRules.Append($"padding-bottom: {calMarginBottomMm}mm !important; ");
            if (calMarginLeftMm > 0) padRules.Append($"padding-left: {calMarginLeftMm}mm !important; ");
            if (calMarginLeftMm < 0) padRules.Append($"margin-left: {calMarginLeftMm}mm !important; ");
            if (calMarginRightMm > 0) padRules.Append($"padding-right: {calMarginRightMm}mm !important; ");

            sb.AppendLine($"  .receipt {{ {padRules} }}");
            sb.AppendLine($"  body:not(:has(.receipt)):not(:has(.page)):not(:has(.labels-wrapper)) {{ {padRules} }}");
        }

        if (isThermal && calFontScale != 100)
        {
            var zoom = Math.Round(calFontScale / 100.0, 3).ToString(CultureInfo.InvariantCulture);
            sb.AppendLine($"  .receipt .content, .receipt > table, body:not(:has(.receipt)):not(:has(.page)):not(:has(.labels-wrapper)) {{ zoom: {zoom}; }}");
        }

        if (calContrastMode is "high_contrast" or "extra_bold")
        {
            sb.AppendLine("  :root { --text: #000000 !important; --muted: #000000 !important; --border: #000000 !important; }");
            sb.AppendLine("  body, .receipt, .page, .labels-wrapper, .sticker, table, th, td, div, span, p, h1, h2, h3 { color: #000000 !important; -webkit-font-smoothing: none !important; }");
            sb.AppendLine("  .hr, .double-hr, .dotted-hr, .solid-hr, .line, th, td, .meta, .remarks, .footer-total, .footer-total .row, .sticker-head, .sticker-dosage-box { border-color: #000000 !important; }");
            sb.AppendLine("  .wm img { opacity: 0.22 !important; filter: grayscale(100%) contrast(150%) !important; }");
        }

        if (calContrastMode == "extra_bold")
        {
            sb.AppendLine("  body, .receipt, .page, .labels-wrapper, .sticker, table, th, td, div, span { font-weight: 800 !important; text-shadow: 0.2px 0 0 #000000; }");
            sb.AppendLine("  thead th, tbody td { border-width: 1.5px !important; }");
        }

        sb.AppendLine("</style>");
        return sb.ToString();
    }

    /// <summary>
    /// Renders `resources/views/printer/partials/fbr_fiscal_footer.blade.php`.
    /// </summary>
    public string RenderFbrFiscalFooter(PrintInvoiceViewModel? doc, PrintSettingViewModel setting)
    {
        var fbrEnabled = setting.FbrEnabled;
        var fbrEnv = (setting.FbrEnvironment ?? "sandbox").Trim().ToLowerInvariant();
        var fbrMode = (setting.FbrMode ?? "digital_invoicing").Trim().ToLowerInvariant();
        var sellerNtn = (setting.FbrSellerNtn ?? "").Trim();
        var sellerStrn = (setting.FbrSellerStrn ?? "").Trim();
        var posId = (setting.FbrPosId ?? "").Trim();
        var fbrInvNo = (doc?.FbrInvoiceNumber ?? "").Trim();

        if (string.IsNullOrEmpty(fbrInvNo) && fbrEnabled && fbrEnv == "sandbox" && doc != null)
        {
            var prefix = !string.IsNullOrEmpty(posId) ? posId : "966130";
            var docId = doc.Id > 0 ? doc.Id : 101;
            fbrInvNo = $"{prefix}{DateTime.Now:yyMMdd}{docId:D6}";
        }

        if (string.IsNullOrEmpty(fbrInvNo))
        {
            return "";
        }

        var displayInvNo = fbrInvNo;

        var fbrQrData = (doc?.FbrQrData ?? "").Trim();
        if (string.IsNullOrEmpty(fbrQrData) && !string.IsNullOrEmpty(displayInvNo))
        {
            fbrQrData = $"https://ims.fbr.gov.pk/Lv1/Verification?inv={displayInvNo}";
        }

        var fbrPosFee = doc != null && doc.FbrPosFee > 0
            ? doc.FbrPosFee
            : (fbrEnabled && setting.FbrPosFeeEnabled ? setting.FbrPosFeeAmount : 0m);
        var usinNo = (doc?.FbrUsin ?? doc?.PostedNumber ?? "").Trim();

        var qrSeed = !string.IsNullOrEmpty(fbrQrData) ? fbrQrData : displayInvNo;
        var qrSvg = GenerateQrSvg(qrSeed, 74);

        var sb = new StringBuilder();
        sb.AppendLine("<div class=\"fbr-fiscal-block\" style=\"margin: 8px 0 6px; padding: 7px 8px; border: 1.5px solid #000000; border-radius: 5px; background: #ffffff; color: #000000; font-family: 'Inter', 'Segoe UI', Arial, sans-serif; page-break-inside: avoid; max-width: 340px; margin-left: auto; margin-right: auto;\">");
        sb.AppendLine("  <div style=\"background: #000000; color: #ffffff; padding: 3px 6px; border-radius: 3px; text-align: center; font-size: 8.5px; font-weight: 800; letter-spacing: 0.6px; text-transform: uppercase;\">");
        sb.Append($"    {(fbrMode == "digital_invoicing" ? "FBR DIGITAL INVOICING SYSTEM" : "FBR POS INVOICING SYSTEM")}");
        if (fbrEnv == "sandbox")
        {
            sb.Append("<span style=\"font-size: 7.5px; font-weight: 700; opacity: 0.9;\"> (SANDBOX)</span>");
        }
        sb.AppendLine("\n  </div>");

        sb.AppendLine("  <div style=\"margin-top: 5px; padding: 3px 5px; border: 1px dashed #000000; background: #f8fafc; text-align: center;\">");
        sb.AppendLine("    <div style=\"font-size: 7.5px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.4px; color: #111111;\">FBR Invoice Number</div>");
        sb.AppendLine($"    <div style=\"font-family: 'Courier New', Courier, monospace; font-size: 11px; font-weight: 900; letter-spacing: 0.5px; color: #000000; margin-top: 1px; word-break: break-all;\">{H(displayInvNo)}</div>");
        sb.AppendLine("  </div>");

        sb.AppendLine("  <div style=\"margin-top: 4px; font-size: 8px; line-height: 1.35; text-align: center; font-weight: 700; color: #000000;\">");
        var metaParts = new List<string>();
        if (!string.IsNullOrEmpty(posId)) metaParts.Add($"<span>POS ID: {H(posId)}</span>");
        if (!string.IsNullOrEmpty(sellerNtn)) metaParts.Add($"<span>NTN: {H(sellerNtn)}</span>");
        if (!string.IsNullOrEmpty(sellerStrn)) metaParts.Add($"<span>STRN: {H(sellerStrn)}</span>");
        if (metaParts.Count > 0)
        {
            sb.AppendLine("    " + string.Join("<span> • </span>", metaParts));
        }
        if (!string.IsNullOrEmpty(usinNo))
        {
            var feePart = fbrPosFee > 0 ? $" | POS Fee: Rs. {F2(fbrPosFee)}" : "";
            sb.AppendLine($"    <div style=\"font-size: 7.5px; font-weight: 600;\">USIN: {H(usinNo)}{feePart}</div>");
        }
        else if (fbrPosFee > 0)
        {
            sb.AppendLine($"    <div style=\"font-size: 7.5px; font-weight: 600;\">FBR POS Service Fee: Rs. {F2(fbrPosFee)}</div>");
        }
        sb.AppendLine("  </div>");

        sb.AppendLine("  <table style=\"width: 100%; border-collapse: collapse; margin-top: 5px; border: none !important;\">");
        sb.AppendLine("    <tr style=\"border: none !important;\">");
        sb.AppendLine("      <td style=\"width: 52%; text-align: center; vertical-align: middle; padding: 2px 4px; border: none !important;\">");
        sb.AppendLine("        <svg width=\"112\" height=\"76\" viewBox=\"0 0 140 95\" xmlns=\"http://www.w3.org/2000/svg\" style=\"display: inline-block; max-width: 100%; height: auto;\">");
        sb.AppendLine("          <rect x=\"2\" y=\"2\" width=\"136\" height=\"91\" rx=\"7\" fill=\"#ffffff\" stroke=\"#000000\" stroke-width=\"2.5\"/>");
        sb.AppendLine("          <circle cx=\"32\" cy=\"34\" r=\"19\" fill=\"none\" stroke=\"#000000\" stroke-width=\"2\"/>");
        sb.AppendLine("          <circle cx=\"32\" cy=\"34\" r=\"15.5\" fill=\"none\" stroke=\"#000000\" stroke-width=\"0.9\" stroke-dasharray=\"2,1.5\"/>");
        sb.AppendLine("          <path d=\"M35 23 A10 10 0 1 0 35 45 A8 8 0 1 1 35 23 Z\" fill=\"#000000\"/>");
        sb.AppendLine("          <polygon points=\"38,29 39.5,32.5 43,32.8 40.2,35 41,38.5 38,36.6 35,38.5 35.8,35 33,32.8 36.5,32.5\" fill=\"#000000\"/>");
        sb.AppendLine("          <text x=\"57\" y=\"35\" font-family=\"Arial Black, Impact, sans-serif\" font-size=\"25\" font-weight=\"900\" fill=\"#000000\" letter-spacing=\"1\">FBR</text>");
        sb.AppendLine("          <text x=\"58\" y=\"46\" font-family=\"Arial, sans-serif\" font-size=\"7.5\" font-weight=\"800\" fill=\"#000000\" letter-spacing=\"0.6\">PAKISTAN</text>");
        sb.AppendLine("          <line x1=\"58\" y1=\"49\" x2=\"128\" y2=\"49\" stroke=\"#000000\" stroke-width=\"1.2\"/>");
        sb.AppendLine("          <text x=\"58\" y=\"57\" font-family=\"Arial, sans-serif\" font-size=\"5.8\" font-weight=\"700\" fill=\"#000000\">FEDERAL BOARD OF REVENUE</text>");
        sb.AppendLine("          <rect x=\"8\" y=\"64\" width=\"124\" height=\"22\" rx=\"4\" fill=\"#000000\"/>");
        sb.AppendLine($"          <text x=\"70\" y=\"79\" text-anchor=\"middle\" font-family=\"Arial, sans-serif\" font-size=\"11.5\" font-weight=\"900\" fill=\"#ffffff\" letter-spacing=\"1.8\">{(fbrMode == "digital_invoicing" ? "DIGITAL INVOICE" : "FBR POS")}</text>");
        sb.AppendLine("        </svg>");
        sb.AppendLine("      </td>");

        sb.AppendLine("      <td style=\"width: 48%; text-align: center; vertical-align: middle; padding: 2px 4px; border: none !important;\">");
        sb.AppendLine("        <div style=\"display: inline-block; padding: 3px; background: #ffffff; border: 1.5px solid #000000; border-radius: 4px;\">");
        sb.AppendLine(qrSvg);
        sb.AppendLine("        </div>");
        sb.AppendLine("      </td>");
        sb.AppendLine("    </tr>");
        sb.AppendLine("  </table>");

        sb.AppendLine("  <div style=\"margin-top: 4px; padding-top: 3px; border-top: 1px solid #000000; text-align: center; font-size: 7.5px; font-weight: 700; line-height: 1.35;\">");
        sb.AppendLine("    <div>Verify invoice via <strong>FBR Tax Asaan App</strong> or SMS to <strong>9966</strong></div>");
        sb.AppendLine("    <div dir=\"rtl\" style=\"font-size: 8px; font-weight: 700; margin-top: 1px;\">ایف بی آر ٹیکس آسان ایپ کے ذریعے اس رسید کی تصدیق کریں</div>");
        sb.AppendLine("  </div>");
        sb.AppendLine("</div>");
        return sb.ToString();
    }

    private const string AutoPrintBodyOnload =
        "if (!window.karobarDesktop && !window.__karobarSilentPrintWorker && !/Electron/i.test(navigator.userAgent) && window.self === window.top && !new URLSearchParams(window.location.search).has('silent_print')) window.print()";

    private sealed record HtmlThemePalette(
        string Id,
        string Primary,
        string Secondary,
        string Accent,
        string SoftBg,
        string Border,
        string Text,
        string Muted,
        string DiscountColor,
        string SuccessBg,
        string SuccessFg,
        string SuccessBorder,
        string WarnBg,
        string WarnFg,
        string WarnBorder,
        string DangerBg,
        string DangerFg,
        string DangerBorder);

    private static HtmlThemePalette ResolveHtmlPalette(string template, bool monochrome)
    {
        var id = template switch
        {
            "minimal" => "minimal",
            "detailed" => "detailed",
            "compact" => "compact",
            "bold" => "bold",
            "barcode" => "barcode",
            _ => "standard"
        };

        if (monochrome)
        {
            // Pure Black & White palette engineered for non-color thermal POS printers (203 DPI)
            return new HtmlThemePalette(
                Id: id,
                Primary: "#000000",
                Secondary: "#000000",
                Accent: "#000000",
                SoftBg: "#ffffff",
                Border: "#000000",
                Text: "#000000",
                Muted: "#000000",
                DiscountColor: "#000000",
                SuccessBg: "#ffffff",
                SuccessFg: "#000000",
                SuccessBorder: "#000000",
                WarnBg: "#ffffff",
                WarnFg: "#000000",
                WarnBorder: "#000000",
                DangerBg: "#ffffff",
                DangerFg: "#000000",
                DangerBorder: "#000000");
        }

        return id switch
        {
            "minimal" => new HtmlThemePalette("minimal", "#1e293b", "#64748b", "#334155", "#ffffff", "#cbd5e1", "#0f172a", "#475569",
                "#b91c1c", "#dcfce7", "#166534", "#86efac", "#fef3c7", "#92400e", "#fcd34d", "#fee2e2", "#991b1b", "#fca5a5"),
            "detailed" => new HtmlThemePalette("detailed", "#1e3a8a", "#1d4ed8", "#2563eb", "#eff6ff", "#bfdbfe", "#0f172a", "#475569",
                "#b91c1c", "#dcfce7", "#166534", "#86efac", "#fef3c7", "#92400e", "#fcd34d", "#fee2e2", "#991b1b", "#fca5a5"),
            "compact" => new HtmlThemePalette("compact", "#0f766e", "#115e59", "#0d9488", "#f0fdfa", "#99f6e4", "#0f172a", "#475569",
                "#b91c1c", "#dcfce7", "#166534", "#86efac", "#fef3c7", "#92400e", "#fcd34d", "#fee2e2", "#991b1b", "#fca5a5"),
            "bold" => new HtmlThemePalette("bold", "#064e3b", "#047857", "#10b981", "#ecfdf5", "#a7f3d0", "#0f172a", "#334155",
                "#b91c1c", "#dcfce7", "#166534", "#86efac", "#fef3c7", "#92400e", "#fcd34d", "#fee2e2", "#991b1b", "#fca5a5"),
            "barcode" => new HtmlThemePalette("barcode", "#18181b", "#3f3f46", "#d97706", "#fafafa", "#d4d4d8", "#18181b", "#52525b",
                "#b91c1c", "#dcfce7", "#166534", "#86efac", "#fef3c7", "#92400e", "#fcd34d", "#fee2e2", "#991b1b", "#fca5a5"),
            _ => new HtmlThemePalette("standard", "#0f172a", "#334155", "#2563eb", "#f8fafc", "#cbd5e1", "#0f172a", "#475569",
                "#b91c1c", "#dcfce7", "#166534", "#86efac", "#fef3c7", "#92400e", "#fcd34d", "#fee2e2", "#991b1b", "#fca5a5")
        };
    }

    private static bool ContainsUrdu(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var ch in text)
        {
            if (ch is >= '\u0600' and <= '\u06FF') return true;
        }
        return false;
    }

    private static string ShortExpiry(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt)
            ? dt.ToString("MM/yyyy", CultureInfo.InvariantCulture)
            : raw.Trim();
    }

    private static (decimal RawGross, decimal ItemDiscTotal, decimal BillDisc, decimal Tax, decimal FbrFee, decimal NetTotal, decimal TotalSaved, decimal Received, decimal BalanceThis, decimal ChangeDue, decimal TotalUnits, string StatusText, string StatusKind)
        ComputeInvoiceFinancials(PrintInvoiceViewModel invoice)
    {
        decimal rawGrossSum = 0m;
        decimal itemDiscSum = 0m;
        decimal totalUnits = 0m;
        foreach (var it in invoice.Items)
        {
            var lineGross = Math.Round(it.Quantity * it.Price, 2);
            var lineDisc = it.ItemDiscountAmount > 0
                ? it.ItemDiscountAmount
                : (it.ItemDiscountPercentage > 0
                    ? Math.Round(lineGross * it.ItemDiscountPercentage / 100m, 2)
                    : Math.Max(0m, Math.Round(lineGross - it.SubTotal, 2)));
            rawGrossSum += lineGross;
            itemDiscSum += lineDisc;
            totalUnits += it.Quantity + it.Bonus;
        }

        var itemDiscTotal = invoice.ItemDiscountTotal > 0 ? invoice.ItemDiscountTotal : Math.Round(itemDiscSum, 2);
        var rawGross = rawGrossSum > 0
            ? Math.Round(rawGrossSum, 2)
            : (invoice.GrossAmount > 0 ? Math.Round(invoice.GrossAmount + itemDiscTotal, 2) : 0m);
        var billDisc = invoice.DiscountAmount;
        var tax = invoice.TaxAmount;
        var fbrFee = invoice.FbrPosFee;
        var netTotal = invoice.Total > 0
            ? invoice.Total
            : Math.Max(0m, Math.Round(rawGross - itemDiscTotal - billDisc + tax + fbrFee, 2));
        var totalSaved = Math.Round(itemDiscTotal + billDisc, 2);
        var received = invoice.TotalReceive;
        var balanceThis = Math.Max(0m, Math.Round(netTotal - received, 2));
        var changeDue = Math.Max(0m, Math.Round(received - netTotal, 2));

        var (statusText, statusKind) = balanceThis <= 0.009m
            ? ("PAID IN FULL", "success")
            : (received > 0.009m ? ("PARTIAL PAID", "warning") : ("CREDIT DUE", "danger"));

        return (rawGross, itemDiscTotal, billDisc, tax, fbrFee, netTotal, totalSaved, received, balanceThis, changeDue, totalUnits, statusText, statusKind);
    }

    private static string BuildStatusBadgeHtml(string statusText, string statusKind, HtmlThemePalette pal, bool compact)
    {
        var (bg, fg, border) = statusKind switch
        {
            "success" => (pal.SuccessBg, pal.SuccessFg, pal.SuccessBorder),
            "warning" => (pal.WarnBg, pal.WarnFg, pal.WarnBorder),
            _ => (pal.DangerBg, pal.DangerFg, pal.DangerBorder)
        };
        var fs = compact ? "7.5px" : "9px";
        var pad = compact ? "1px 5px" : "2px 8px";
        return $"<span style=\"display:inline-block;background:{bg};color:{fg};border:1px solid {border};font-size:{fs};font-weight:800;letter-spacing:0.4px;padding:{pad};border-radius:999px;white-space:nowrap;\">{H(statusText)}</span>";
    }

    /// <summary>
    /// Renders any of the 7 Thermal Sale Invoice views (`base` / `standard` / `minimal` / `detailed` / `compact` / `bold` / `barcode`).
    /// Direct thermal printing (`colorMode: false`) is strictly pure Black &amp; White for monochrome thermal POS heads.
    /// Document sharing preview (`colorMode: true`) renders the full colorful palette matching QuestPDF.
    /// Always uses the 4-column table (Name | Qty | Disc | Subt.) with Rate/Pack/Batch/Expiry/SKU nested under Name
    /// and embeds "Software by Engr. M Asim Shahzad • Phone: 923047674787".
    /// </summary>
    public string RenderThermalSaleInvoice(
        string template,
        PrintInvoiceViewModel invoice,
        PrintSettingViewModel setting,
        IDictionary<string, string>? queryOverrides = null,
        bool colorMode = false)
    {
        var normalized = (template ?? "standard").Trim().ToLowerInvariant();
        if (normalized is "base" or "thermal") normalized = "standard";
        var pal = ResolveHtmlPalette(normalized, monochrome: !colorMode);
        var isMinimal = pal.Id == "minimal";
        var isBold = pal.Id == "bold";
        var isDetailed = pal.Id == "detailed";
        var isCompact = pal.Id == "compact";
        var isBarcode = pal.Id == "barcode";

        var fin = ComputeInvoiceFinancials(invoice);
        var customerTotalDue = invoice.CustomerTotalDue;
        var showTotalDue = customerTotalDue.HasValue && customerTotalDue.Value > 0.009m;
        var footerNote = !string.IsNullOrWhiteSpace(invoice.FooterNote) ? invoice.FooterNote.Trim() : (setting.Note ?? "").Trim();
        var calibrationHtml = colorMode ? "" : RenderCalibrationStyles(setting, queryOverrides, isThermal: true);
        var fbrFooterHtml = RenderFbrFiscalFooter(invoice, setting);
        var showLogo = colorMode ? setting.ShareShowLogo : setting.PrintShowLogo;
        var logo = showLogo ? setting.LogoUrl : null;

        var dateStr = invoice.Date?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "";
        var timeStr = invoice.Date?.ToString("hh:mm tt", CultureInfo.InvariantCulture) ?? "";
        var saleModeLabel = CultureInfo.InvariantCulture.TextInfo.ToTitleCase((invoice.SaleType ?? "retail").ToLowerInvariant());
        var invTypeRaw = (invoice.InvoiceType ?? "debit").Trim().ToLowerInvariant();
        var invTypeLabel = invTypeRaw == "credit" ? "Credit" : (invTypeRaw == "cash" ? "Cash" : "Debit");
        var payMethodLabel = CultureInfo.InvariantCulture.TextInfo.ToTitleCase((invoice.PaymentMethod ?? "cash").Replace("_", " ").ToLowerInvariant());
        var docNo = string.IsNullOrWhiteSpace(invoice.PostedNumber) ? "INV-0001" : invoice.PostedNumber.Trim();
        var customerDisplay = string.IsNullOrWhiteSpace(invoice.CustomerName) ? "Walk-in Customer" : invoice.CustomerName.Trim();
        var itemCount = invoice.Items.Count;

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"/>");
        sb.AppendLine($"<title>Sale Invoice ({H(pal.Id)} Thermal) - {H(docNo)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("@page{margin:0;}*{box-sizing:border-box;}");
        sb.AppendLine(colorMode
            ? "html{background:#f1f5f9;padding:12px 0;}body{margin:0;padding:0;background:transparent;color:#0f172a;font-family:'Segoe UI',Arial,sans-serif;font-size:9.5px;line-height:1.35;-webkit-print-color-adjust:exact;print-color-adjust:exact;}.receipt{width:80mm;max-width:96%;margin:0 auto;padding:11px 12px 13px;background:#ffffff;box-shadow:0 6px 20px rgba(15,23,42,0.12);border-radius:4px;overflow:hidden;}"
            : "html,body{margin:0;padding:0;color:#000000;background:#ffffff;}body{font-family:'Segoe UI',Arial,Helvetica,sans-serif;font-weight:600;font-size:10px;line-height:1.3;-webkit-print-color-adjust:exact;print-color-adjust:exact;}.receipt{width:78mm;max-width:100%;margin:0 auto;padding:4px 6px 8px;background:#ffffff;color:#000000;overflow:hidden;}");
        sb.AppendLine(".print-actions{margin:8px auto;width:78mm;text-align:right;}.print-actions button{padding:6px 12px;cursor:pointer;font-weight:700;border:1px solid #000;background:#fff;color:#000;border-radius:4px;}@media print{.print-actions{display:none!important;}.receipt{margin:0!important;box-shadow:none!important;border-radius:0!important;}}");
        sb.AppendLine(".tabular{font-variant-numeric:tabular-nums;}.mono{font-family:'Courier New',Consolas,monospace;}");
        sb.AppendLine(".logo{max-width:48mm;max-height:22mm;object-fit:contain;display:block;margin:0 auto 4px;}");
        sb.AppendLine(".urdu{direction:rtl;text-align:right;font-family:'Noto Nastaliq Urdu','Jameel Noori Nastaleeq',Tahoma,sans-serif;line-height:1.8;}");
        sb.AppendLine("table.items{width:100%;border-collapse:collapse;table-layout:fixed;}");
        sb.AppendLine("table.items th,table.items td{vertical-align:top;}");
        sb.AppendLine(".tot-row{display:flex;justify-content:space-between;align-items:baseline;padding:1.8px 0;gap:6px;}");
        sb.AppendLine("</style>");
        sb.AppendLine(calibrationHtml);
        sb.AppendLine(colorMode ? "</head><body>" : $"</head><body onload=\"{AutoPrintBodyOnload}\">");

        if (!colorMode)
        {
            sb.AppendLine("<div class=\"print-actions\"><button onclick=\"if(window.karobarDesktop&&window.karobarDesktop.silentPrint){window.karobarDesktop.silentPrint();}else{window.print();}\">Print Receipt</button></div>");
        }

        sb.AppendLine("<div class=\"receipt\">");

        // 1. HEADER
        if (isBold || isDetailed)
        {
            var bannerBorder = colorMode ? $"3px solid {pal.Accent}" : "2px solid #000000";
            var pillBg = colorMode ? pal.Accent : "#ffffff";
            var pillFg = colorMode ? (isBold ? "#052e16" : "#ffffff") : "#000000";
            sb.AppendLine($"<div style=\"background:{pal.Primary};color:#ffffff;margin:-6px -6px 8px;padding:10px 8px;text-align:center;border-bottom:{bannerBorder};\">");
            if (!string.IsNullOrEmpty(logo))
                sb.AppendLine($"<div style=\"display:inline-block;background:#ffffff;padding:3px;border-radius:4px;margin-bottom:4px;\"><img src=\"{H(logo)}\" alt=\"Logo\" class=\"logo\" style=\"margin:0;max-height:28px;\"/></div>");
            sb.AppendLine($"<div style=\"font-size:14px;font-weight:900;letter-spacing:0.3px;\">{H(setting.StoreName)}</div>");
            if (!string.IsNullOrEmpty(setting.Address))
                sb.AppendLine($"<div style=\"font-size:8.5px;margin-top:2px;color:{(colorMode ? "#e2e8f0" : "#ffffff")};\">{H(setting.Address)}</div>");
            if (!string.IsNullOrEmpty(setting.PhoneNumber))
                sb.AppendLine($"<div style=\"font-size:8.5px;color:{(colorMode ? "#e2e8f0" : "#ffffff")};\">Tel: {H(setting.PhoneNumber)}</div>");
            if (!string.IsNullOrEmpty(setting.LicenseNumber))
                sb.AppendLine($"<div style=\"font-size:7.8px;color:{(colorMode ? "#cbd5e1" : "#ffffff")};\">Lic: {H(setting.LicenseNumber)}</div>");
            sb.AppendLine($"<div style=\"margin-top:5px;display:inline-block;background:{pillBg};color:{pillFg};padding:2px 9px;border-radius:999px;font-size:8.5px;font-weight:900;letter-spacing:0.5px;\">SALE INVOICE &bull; #{H(docNo)}</div>");
            sb.AppendLine("</div>");
        }
        else
        {
            var headBorder = isMinimal ? $"1.5px solid {pal.Primary}" : $"2px solid {pal.Primary}";
            sb.AppendLine($"<div style=\"text-align:center;padding-bottom:6px;border-bottom:{headBorder};margin-bottom:7px;\">");
            if (!string.IsNullOrEmpty(logo))
                sb.AppendLine($"<img src=\"{H(logo)}\" alt=\"Logo\" class=\"logo\"/>");
            sb.AppendLine($"<div style=\"font-size:{(isCompact ? "13px" : "14.5px")};font-weight:900;color:{pal.Primary};line-height:1.2;\">{H(setting.StoreName)}</div>");
            if (!string.IsNullOrEmpty(setting.Address))
                sb.AppendLine($"<div style=\"font-size:8.5px;color:{pal.Muted};margin-top:2px;\">{H(setting.Address)}</div>");
            if (!string.IsNullOrEmpty(setting.PhoneNumber))
                sb.AppendLine($"<div style=\"font-size:8.5px;color:{pal.Muted};\">Ph: {H(setting.PhoneNumber)}</div>");
            if (!isCompact && !string.IsNullOrEmpty(setting.LicenseNumber))
                sb.AppendLine($"<div style=\"font-size:7.8px;color:{pal.Muted};\">Lic: {H(setting.LicenseNumber)}</div>");

            var badgeStyle = isMinimal
                ? $"border:1.2px solid {pal.Primary};color:{pal.Primary};background:#ffffff;"
                : $"background:{pal.Primary};color:#ffffff;";
            sb.AppendLine($"<div style=\"margin-top:5px;display:inline-block;{badgeStyle}padding:2px 9px;border-radius:3px;font-size:8.5px;font-weight:800;letter-spacing:0.5px;\">SALE INVOICE &bull; #{H(docNo)}</div>");
            if (isBarcode)
            {
                var barSvg = GenerateCode128Svg(docNo, 195, 24);
                sb.AppendLine($"<div style=\"display:flex;justify-content:center;margin-top:5px;\">{barSvg}</div>");
            }
            sb.AppendLine("</div>");
        }

        // 2. COMPLETE INVOICE METADATA BLOCK
        var metaWrapStyle = isMinimal
            ? $"border-bottom:1px dashed {pal.Border};padding-bottom:5px;margin-bottom:6px;"
            : $"background:{pal.SoftBg};border:1px solid {pal.Border};border-radius:4px;padding:5px 7px;margin-bottom:7px;";
        sb.AppendLine($"<div style=\"{metaWrapStyle}font-size:8.5px;line-height:1.4;\">");
        sb.AppendLine($"<div style=\"display:flex;justify-content:space-between;align-items:center;gap:4px;margin-bottom:2px;\"><span style=\"font-weight:800;font-size:9.5px;color:{pal.Text};\">{H(customerDisplay)}</span>{BuildStatusBadgeHtml(fin.StatusText, fin.StatusKind, pal, compact: true)}</div>");
        if (!string.IsNullOrWhiteSpace(invoice.CustomerPhone))
            sb.AppendLine($"<div style=\"color:{pal.Muted};\">Ph: {H(invoice.CustomerPhone)}</div>");
        if (!string.IsNullOrWhiteSpace(invoice.CustomerAddress))
            sb.AppendLine($"<div style=\"color:{pal.Muted};font-size:8px;\">{H(invoice.CustomerAddress)}</div>");

        sb.AppendLine($"<div style=\"display:grid;grid-template-columns:1fr 1fr;gap:1px 6px;margin-top:3px;padding-top:3px;border-top:1px dotted {pal.Border};\">");
        sb.AppendLine($"<div><span style=\"color:{pal.Muted};\">Date:</span> <strong>{H(dateStr)}</strong></div>");
        sb.AppendLine($"<div style=\"text-align:right;\"><span style=\"color:{pal.Muted};\">Time:</span> <strong>{H(timeStr)}</strong></div>");
        sb.AppendLine($"<div><span style=\"color:{pal.Muted};\">Mode:</span> <strong>{H(saleModeLabel)}/{H(invTypeLabel)}</strong></div>");
        sb.AppendLine($"<div style=\"text-align:right;\"><span style=\"color:{pal.Muted};\">Pay:</span> <strong>{H(payMethodLabel)}</strong></div>");
        if (!string.IsNullOrWhiteSpace(invoice.UserName))
            sb.AppendLine($"<div><span style=\"color:{pal.Muted};\">Cashier:</span> <strong>{H(invoice.UserName)}</strong></div>");
        sb.AppendLine($"<div style=\"text-align:{(string.IsNullOrWhiteSpace(invoice.UserName) ? "left" : "right")};\"><span style=\"color:{pal.Muted};\">Items:</span> <strong>{itemCount} ({Nf(fin.TotalUnits)}u)</strong></div>");
        if (!string.IsNullOrWhiteSpace(invoice.DoctorName))
            sb.AppendLine($"<div><span style=\"color:{pal.Muted};\">Dr:</span> <strong>{H(invoice.DoctorName)}</strong></div>");
        if (!string.IsNullOrWhiteSpace(invoice.PatientName))
            sb.AppendLine($"<div style=\"text-align:{(string.IsNullOrWhiteSpace(invoice.DoctorName) ? "left" : "right")};\"><span style=\"color:{pal.Muted};\">Pt:</span> <strong>{H(invoice.PatientName)}</strong></div>");
        sb.AppendLine("</div></div>");

        // 3. 4-COLUMN THERMAL ITEMS TABLE: Name | Qty | Disc | Subt. (with Rate, Pack, Batch, Exp, SKU below Name)
        var thStyle = isMinimal
            ? $"border-top:1.2px solid {pal.Primary};border-bottom:1.2px solid {pal.Primary};color:{pal.Primary};font-weight:800;font-size:7.8px;text-transform:uppercase;padding:3.5px 3px;"
            : $"background:{pal.Primary};color:#ffffff;font-weight:800;font-size:7.8px;text-transform:uppercase;letter-spacing:0.3px;padding:3.5px 3px;";
        var tdPad = isCompact ? "3px 3px" : "4.5px 3px";

        sb.AppendLine("<table class=\"items\"><thead><tr>");
        sb.AppendLine($"<th style=\"{thStyle}width:46%;text-align:left;\">Name</th>");
        sb.AppendLine($"<th style=\"{thStyle}width:13%;text-align:center;\">Qty</th>");
        sb.AppendLine($"<th style=\"{thStyle}width:17%;text-align:right;\">Disc</th>");
        sb.AppendLine($"<th style=\"{thStyle}width:24%;text-align:right;\">Subt.</th>");
        sb.AppendLine("</tr></thead><tbody>");

        for (var idx = 0; idx < invoice.Items.Count; idx++)
        {
            var it = invoice.Items[idx];
            var rowBg = (colorMode && !isMinimal && idx % 2 == 1) ? "#f8fafc" : "#ffffff";
            var lineGross = Math.Round(it.Quantity * it.Price, 2);
            var lineDiscAmt = it.ItemDiscountAmount > 0
                ? it.ItemDiscountAmount
                : (it.ItemDiscountPercentage > 0 ? Math.Round(lineGross * it.ItemDiscountPercentage / 100m, 2) : Math.Max(0m, Math.Round(lineGross - it.SubTotal, 2)));

            var subMeta = new List<string> { $"@{F2(it.Price)}" };
            if (it.PackSize > 1) subMeta.Add($"Pk:{it.PackSize}");
            if (!string.IsNullOrWhiteSpace(it.BatchNumber)) subMeta.Add($"B:{it.BatchNumber}");
            var expStr = ShortExpiry(it.Expiry);
            if (!string.IsNullOrWhiteSpace(expStr)) subMeta.Add($"Exp:{expStr}");

            var codeMeta = isBarcode
                ? string.Join(" • ", new[] { it.ProductCode, it.Barcode }.Where(x => !string.IsNullOrWhiteSpace(x)))
                : "";

            var qtyHtml = it.Bonus > 0
                ? $"<div style=\"font-weight:800;\">{Nf(it.Quantity)}</div><div style=\"font-size:7px;font-weight:700;color:{(colorMode ? "#047857" : "#000000")};\">+{it.Bonus}B</div>"
                : $"<span style=\"font-weight:800;\">{Nf(it.Quantity)}</span>";

            string discHtml;
            if (it.ItemDiscountPercentage > 0 || lineDiscAmt > 0)
            {
                discHtml = it.ItemDiscountPercentage > 0
                    ? $"<div style=\"font-weight:800;color:{pal.DiscountColor};line-height:1.1;\">{Nf(it.ItemDiscountPercentage)}%</div><div style=\"font-size:7px;color:{pal.Muted};\">-{F2(lineDiscAmt)}</div>"
                    : $"<span style=\"font-weight:800;color:{pal.DiscountColor};\">-{F2(lineDiscAmt)}</span>";
            }
            else
            {
                discHtml = $"<span style=\"color:{(colorMode ? "#94a3b8" : "#000000")};\">—</span>";
            }

            sb.AppendLine($"<tr style=\"background:{rowBg};\">");
            sb.AppendLine($"<td style=\"padding:{tdPad};border-bottom:1px dotted {pal.Border};\">");
            sb.AppendLine($"<div style=\"font-weight:800;color:{pal.Text};font-size:8.8px;line-height:1.2;\">{H(it.ProductName)}</div>");
            sb.AppendLine($"<div style=\"font-size:7.2px;color:{pal.Muted};margin-top:1.5px;line-height:1.25;\">{H(string.Join(" • ", subMeta))}</div>");
            if (!string.IsNullOrWhiteSpace(codeMeta))
                sb.AppendLine($"<div class=\"mono\" style=\"font-size:7px;color:{pal.Muted};margin-top:1px;\">{H(codeMeta)}</div>");
            sb.AppendLine("</td>");
            sb.AppendLine($"<td class=\"tabular\" style=\"padding:{tdPad};border-bottom:1px dotted {pal.Border};text-align:center;font-size:8.6px;white-space:nowrap;\">{qtyHtml}</td>");
            sb.AppendLine($"<td class=\"tabular\" style=\"padding:{tdPad};border-bottom:1px dotted {pal.Border};text-align:right;font-size:8.2px;white-space:nowrap;\">{discHtml}</td>");
            sb.AppendLine($"<td class=\"tabular\" style=\"padding:{tdPad};border-bottom:1px dotted {pal.Border};text-align:right;font-weight:800;color:{pal.Text};font-size:8.8px;white-space:nowrap;\">{F2(it.SubTotal)}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");

        // 4. COMPLETE FINANCIAL, DISCOUNT & PAYMENT SUMMARY
        sb.AppendLine("<div style=\"margin-top:6px;font-size:8.5px;\">");
        sb.AppendLine($"<div class=\"tot-row\"><span style=\"color:{pal.Muted};\">Gross Amount</span><span class=\"tabular\" style=\"font-weight:700;\">Rs. {F2(fin.RawGross)}</span></div>");
        if (fin.ItemDiscTotal > 0.009m)
            sb.AppendLine($"<div class=\"tot-row\" style=\"color:{pal.DiscountColor};\"><span>Item Discounts</span><span class=\"tabular\" style=\"font-weight:800;\">- Rs. {F2(fin.ItemDiscTotal)}</span></div>");
        if (fin.BillDisc > 0.009m)
        {
            var discLabel = invoice.DiscountPercentage > 0 ? $"Invoice Discount ({Nf(invoice.DiscountPercentage)}%)" : "Invoice Discount";
            sb.AppendLine($"<div class=\"tot-row\" style=\"color:{pal.DiscountColor};\"><span>{H(discLabel)}</span><span class=\"tabular\" style=\"font-weight:800;\">- Rs. {F2(fin.BillDisc)}</span></div>");
        }
        if (fin.Tax > 0.009m)
        {
            var taxLabel = invoice.TaxPercentage > 0 ? $"Sales Tax ({Nf(invoice.TaxPercentage)}%)" : "Sales Tax";
            sb.AppendLine($"<div class=\"tot-row\"><span>{H(taxLabel)}</span><span class=\"tabular\" style=\"font-weight:700;\">Rs. {F2(fin.Tax)}</span></div>");
        }
        if (fin.FbrFee > 0.009m)
            sb.AppendLine($"<div class=\"tot-row\"><span>FBR POS Fee</span><span class=\"tabular\" style=\"font-weight:700;\">Rs. {F2(fin.FbrFee)}</span></div>");

        var totalBarStyle = isMinimal
            ? $"border-top:1.5px solid {pal.Primary};border-bottom:1.5px solid {pal.Primary};color:{pal.Primary};padding:5px 2px;margin:4px 0;"
            : $"background:{pal.Primary};color:#ffffff;border-radius:4px;padding:5.5px 7px;margin:4px 0;";
        sb.AppendLine($"<div class=\"tot-row\" style=\"{totalBarStyle}font-size:11.5px;font-weight:900;\"><span>NET TOTAL</span><span class=\"tabular\">Rs. {F2(fin.NetTotal)}</span></div>");

        if (fin.TotalSaved > 0.009m)
        {
            sb.AppendLine($"<div class=\"tot-row\" style=\"background:{pal.SuccessBg};border:1px solid {pal.SuccessBorder};color:{pal.SuccessFg};border-radius:3px;padding:3px 6px;margin:3px 0;font-weight:800;font-size:8.4px;\"><span>You Saved</span><span class=\"tabular\">Rs. {F2(fin.TotalSaved)}</span></div>");
        }

        sb.AppendLine($"<div style=\"padding-top:4px;border-top:1px dashed {pal.Border};margin-top:4px;\">");
        sb.AppendLine($"<div class=\"tot-row\"><span style=\"color:{pal.Muted};\">Amount Paid</span><span class=\"tabular\" style=\"font-weight:800;color:{(colorMode ? "#047857" : "#000000")};\">Rs. {F2(fin.Received)}</span></div>");
        if (fin.BalanceThis > 0.009m)
            sb.AppendLine($"<div class=\"tot-row\" style=\"color:{pal.WarnFg};font-weight:800;\"><span>Balance Due (Bill)</span><span class=\"tabular\">Rs. {F2(fin.BalanceThis)}</span></div>");
        else if (fin.ChangeDue > 0.009m)
            sb.AppendLine($"<div class=\"tot-row\" style=\"color:{pal.SuccessFg};font-weight:800;\"><span>Change Returned</span><span class=\"tabular\">Rs. {F2(fin.ChangeDue)}</span></div>");

        if (showTotalDue)
            sb.AppendLine($"<div class=\"tot-row\" style=\"background:{pal.DangerBg};border:1px solid {pal.DangerBorder};border-radius:3px;padding:3px 5px;margin-top:3px;color:{pal.DangerFg};font-weight:900;\"><span>Total Account Due</span><span class=\"tabular\">Rs. {F2(customerTotalDue!.Value)}</span></div>");
        sb.AppendLine("</div></div>");

        if (!string.IsNullOrWhiteSpace(invoice.Remarks))
            sb.AppendLine($"<div style=\"margin-top:6px;padding:4px 6px;background:{pal.SoftBg};border-left:2.5px solid {pal.Primary};font-size:8.2px;color:{pal.Text};white-space:pre-wrap;overflow-wrap:anywhere;\"><strong>Note:</strong> {H(invoice.Remarks)}</div>");

        if (isBarcode)
        {
            var verifyQr = GenerateQrSvg($"sale_invoice:{docNo}", 52);
            sb.AppendLine($"<div style=\"margin-top:6px;text-align:center;border-top:1px dotted {pal.Border};padding-top:5px;\"><div style=\"display:inline-block;\">{verifyQr}</div><div class=\"mono\" style=\"font-size:7.2px;font-weight:700;\">REF: {H(docNo)}</div></div>");
        }

        sb.AppendLine(fbrFooterHtml);

        // 5. EMBEDDED PERMANENT FOOTER
        sb.AppendLine($"<div style=\"margin-top:9px;padding-top:6px;border-top:1px dashed {pal.Secondary};\">");
        if (!string.IsNullOrEmpty(footerNote))
        {
            var isUrdu = ContainsUrdu(footerNote);
            sb.AppendLine($"<div class=\"{(isUrdu ? "urdu" : "")}\" style=\"font-size:{(isUrdu ? "9.5px" : "8px")};color:{pal.Text};text-align:{(isUrdu ? "right" : "center")};margin-bottom:6px;padding:4px 5px;background:{pal.SoftBg};border-radius:3px;white-space:pre-wrap;\">{H(footerNote)}</div>");
        }
        sb.AppendLine($"<div style=\"text-align:center;font-size:8.8px;font-weight:800;color:{pal.Primary};margin-bottom:5px;\">Thank you for your business!</div>");
        sb.AppendLine($"<div style=\"background:{(isMinimal ? "#ffffff" : pal.SoftBg)};border:1px solid {(isMinimal ? pal.Primary : pal.Border)};border-top:2px solid {pal.Primary};border-radius:4px;padding:5px 6px;text-align:center;\">");
        sb.AppendLine($"<div style=\"font-size:7.8px;font-weight:800;letter-spacing:0.25px;color:{pal.Primary};\">{H(PdfDocumentGenerator.DeveloperCreditTitle)}</div>");
        sb.AppendLine($"<div class=\"mono\" style=\"font-size:8.2px;font-weight:800;color:{pal.Secondary};margin-top:1px;\">{H(PdfDocumentGenerator.DeveloperCreditPhone)}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine($"<div style=\"text-align:center;font-size:6.8px;color:{pal.Muted};margin-top:4px;\">Printed {DateTime.Now:dd MMM yyyy, hh:mm tt}</div>");
        sb.AppendLine("</div>");

        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Renders any of the 6 unified A4 Sale Invoice views (`standard`, `minimal`, `detailed`, `compact`, `bold`, `barcode`)
    /// matching QuestPDF shared document templates 1-to-1 with complete invoice metadata, discounts, payment summary,
    /// and permanently embedded developer credit ("Software by Engr. M Asim Shahzad • Phone: 923047674787").
    /// </summary>
    public string RenderA4SaleInvoice(
        string template,
        PrintInvoiceViewModel invoice,
        PrintSettingViewModel setting,
        IDictionary<string, string>? queryOverrides = null,
        bool isSharePreview = false)
    {
        var normalized = (template ?? "standard").Trim().ToLowerInvariant();
        var pal = ResolveHtmlPalette(normalized, monochrome: false);
        var isMinimal = pal.Id == "minimal";
        var isDetailed = pal.Id == "detailed";
        var isCompact = pal.Id == "compact";
        var isBold = pal.Id == "bold";
        var isBarcode = pal.Id == "barcode";

        var fin = ComputeInvoiceFinancials(invoice);
        var customerTotalDue = invoice.CustomerTotalDue;
        var showTotalDue = customerTotalDue.HasValue && customerTotalDue.Value > 0.009m;
        var footerNote = !string.IsNullOrWhiteSpace(invoice.FooterNote) ? invoice.FooterNote.Trim() : (setting.Note ?? "").Trim();
        var calibrationHtml = isSharePreview ? "" : RenderCalibrationStyles(setting, queryOverrides, isThermal: false);
        var fbrFooterHtml = RenderFbrFiscalFooter(invoice, setting);
        var showLogo = isSharePreview ? setting.ShareShowLogo : setting.PrintShowLogo;
        var logo = showLogo ? setting.LogoUrl : null;

        var dateStr = invoice.Date?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "";
        var timeStr = invoice.Date?.ToString("hh:mm tt", CultureInfo.InvariantCulture) ?? "";
        var isWholesale = string.Equals(invoice.SaleType, "wholesale", StringComparison.OrdinalIgnoreCase);
        var docTitle = isWholesale ? "WHOLESALE SALE INVOICE" : "SALE INVOICE";
        var docNo = string.IsNullOrWhiteSpace(invoice.PostedNumber) ? "INV-0001" : invoice.PostedNumber.Trim();
        var customerDisplay = string.IsNullOrWhiteSpace(invoice.CustomerName) ? "Walk-in Customer" : invoice.CustomerName.Trim();
        var saleModeLabel = CultureInfo.InvariantCulture.TextInfo.ToTitleCase((invoice.SaleType ?? "retail").ToLowerInvariant());
        var invTypeRaw = (invoice.InvoiceType ?? "debit").Trim().ToLowerInvariant();
        var invTypeLabel = invTypeRaw == "credit" ? "Credit" : (invTypeRaw == "cash" ? "Cash" : "Debit");
        var payMethodLabel = CultureInfo.InvariantCulture.TextInfo.ToTitleCase((invoice.PaymentMethod ?? "cash").Replace("_", " ").ToLowerInvariant());
        var itemCount = invoice.Items.Count;

        var pagePad = isCompact ? "22px 24px" : "26px 30px";
        var sb = new StringBuilder();
        sb.AppendLine($"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"/><title>{H(docTitle)} ({H(pal.Id)} A4) - {H(docNo)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("@page{size:A4 portrait;margin:0;}*{box-sizing:border-box;}");
        sb.AppendLine(isSharePreview
            ? "html{background:#f1f5f9;padding:14px;}body{margin:0;padding:0;background:transparent;font-family:'Segoe UI',Arial,sans-serif;color:#0f172a;line-height:1.4;-webkit-print-color-adjust:exact;print-color-adjust:exact;}"
            : "html,body{margin:0;padding:0;background:#ffffff;font-family:'Segoe UI',Arial,sans-serif;color:#0f172a;line-height:1.4;-webkit-print-color-adjust:exact;print-color-adjust:exact;}");
        sb.AppendLine($".page{{width:210mm;min-height:297mm;margin:0 auto;padding:{pagePad};background:#ffffff;display:flex;flex-direction:column;{(isSharePreview ? "box-shadow:0 6px 24px rgba(15,23,42,0.10);border-radius:4px;" : "")}}}");
        sb.AppendLine($".print-actions{{margin:10px auto;width:210mm;text-align:right;}}.print-actions button{{padding:7px 14px;cursor:pointer;font-weight:700;background:{pal.Primary};color:#fff;border:none;border-radius:5px;}}");
        sb.AppendLine("@media print{.print-actions{display:none!important;}.page{box-shadow:none!important;border-radius:0!important;margin:0!important;}}");
        sb.AppendLine(".tabular{font-variant-numeric:tabular-nums;}.mono{font-family:'Courier New',Consolas,monospace;}");
        sb.AppendLine(".urdu{direction:rtl;text-align:right;font-family:'Noto Nastaliq Urdu','Jameel Noori Nastaleeq',Tahoma,sans-serif;line-height:1.85;}");
        sb.AppendLine("table.items{width:100%;border-collapse:collapse;}table.tot{width:100%;border-collapse:collapse;}");
        sb.AppendLine("</style>");
        sb.AppendLine(calibrationHtml);
        sb.AppendLine(isSharePreview ? "</head><body>" : $"</head><body onload=\"{AutoPrintBodyOnload}\">");

        if (!isSharePreview)
        {
            sb.AppendLine("<div class=\"print-actions\"><button onclick=\"if(window.karobarDesktop&&window.karobarDesktop.silentPrint){window.karobarDesktop.silentPrint();}else{window.print();}\">Print Invoice</button></div>");
        }

        sb.AppendLine("<div class=\"page\">");

        // 1. HEADER per template
        var contactLine = string.Join(" &bull; ", new[]
        {
            !string.IsNullOrWhiteSpace(setting.PhoneNumber) ? $"Ph: {H(setting.PhoneNumber)}" : null,
            !string.IsNullOrWhiteSpace(setting.LicenseNumber) ? $"Lic: {H(setting.LicenseNumber)}" : null
        }.Where(x => x != null));

        if (isBold)
        {
            sb.AppendLine($"<div style=\"background:{pal.Primary};color:#ffffff;margin:-26px -30px 16px;padding:20px 30px 16px;border-bottom:4px solid {pal.Accent};display:flex;justify-content:space-between;align-items:center;gap:16px;\">");
            sb.AppendLine("<div style=\"display:flex;align-items:center;gap:13px;\">");
            if (!string.IsNullOrEmpty(logo))
                sb.AppendLine($"<div style=\"background:#fff;padding:4px;border-radius:6px;\"><img src=\"{H(logo)}\" alt=\"Logo\" style=\"max-height:42px;max-width:100px;object-fit:contain;display:block;\"/></div>");
            sb.AppendLine($"<div><div style=\"font-size:20px;font-weight:900;letter-spacing:0.3px;\">{H(setting.StoreName)}</div>{(!string.IsNullOrEmpty(setting.Address) ? $"<div style=\"font-size:10px;color:#d1fae5;margin-top:2px;\">{H(setting.Address)}</div>" : "")}{(!string.IsNullOrEmpty(contactLine) ? $"<div style=\"font-size:9.8px;color:#a7f3d0;margin-top:1px;\">{contactLine}</div>" : "")}</div></div>");
            sb.AppendLine($"<div style=\"text-align:right;flex-shrink:0;\"><div style=\"display:inline-block;background:{pal.Accent};color:#052e16;font-size:10.5px;font-weight:900;letter-spacing:0.8px;padding:3px 10px;border-radius:4px;\">{H(docTitle)}</div><div class=\"mono\" style=\"font-size:16px;font-weight:900;margin-top:4px;\">#{H(docNo)}</div><div style=\"font-size:9.8px;color:#d1fae5;\">{H(dateStr)} &bull; {H(timeStr)}</div></div>");
            sb.AppendLine("</div>");
        }
        else if (isDetailed)
        {
            sb.AppendLine($"<div style=\"background:{pal.Primary};color:#ffffff;margin:-26px -30px 15px;padding:18px 30px;display:flex;justify-content:space-between;align-items:center;gap:16px;\">");
            sb.AppendLine("<div style=\"display:flex;align-items:center;gap:12px;\">");
            if (!string.IsNullOrEmpty(logo))
                sb.AppendLine($"<div style=\"background:#fff;padding:4px;border-radius:6px;\"><img src=\"{H(logo)}\" alt=\"Logo\" style=\"max-height:40px;max-width:100px;object-fit:contain;display:block;\"/></div>");
            sb.AppendLine($"<div><div style=\"font-size:18px;font-weight:800;letter-spacing:0.3px;\">{H(setting.StoreName)}</div>{(!string.IsNullOrEmpty(setting.Address) ? $"<div style=\"font-size:9.5px;color:#dbeafe;margin-top:2px;\">{H(setting.Address)}</div>" : "")}{(!string.IsNullOrEmpty(contactLine) ? $"<div style=\"font-size:9.5px;color:#bfdbfe;margin-top:1px;\">{contactLine}</div>" : "")}</div></div>");
            sb.AppendLine($"<div style=\"text-align:right;background:rgba(255,255,255,0.12);border:1px solid rgba(255,255,255,0.22);padding:8px 13px;border-radius:6px;\"><div style=\"font-size:8.5px;font-weight:700;letter-spacing:1px;color:#bfdbfe;text-transform:uppercase;\">{H(docTitle)}</div><div class=\"mono\" style=\"font-size:15px;font-weight:800;margin-top:2px;\">#{H(docNo)}</div><div style=\"font-size:9.2px;color:#e0e7ff;\">{H(dateStr)} | {H(timeStr)}</div></div>");
            sb.AppendLine("</div>");
        }
        else if (isMinimal)
        {
            sb.AppendLine($"<div style=\"border-bottom:2px solid {pal.Primary};padding-bottom:12px;margin-bottom:14px;display:flex;justify-content:space-between;align-items:flex-end;gap:16px;\">");
            sb.AppendLine("<div style=\"display:flex;align-items:center;gap:12px;\">");
            if (!string.IsNullOrEmpty(logo))
                sb.AppendLine($"<img src=\"{H(logo)}\" alt=\"Logo\" style=\"max-height:40px;max-width:110px;object-fit:contain;\"/>");
            sb.AppendLine($"<div><div style=\"font-size:19px;font-weight:800;color:{pal.Primary};\">{H(setting.StoreName)}</div>{(!string.IsNullOrEmpty(setting.Address) ? $"<div style=\"font-size:9.5px;color:{pal.Muted};margin-top:2px;\">{H(setting.Address)}</div>" : "")}{(!string.IsNullOrEmpty(contactLine) ? $"<div style=\"font-size:9.2px;color:{pal.Muted};\">{contactLine}</div>" : "")}</div></div>");
            sb.AppendLine($"<div style=\"text-align:right;\"><div style=\"font-size:10.5px;font-weight:800;letter-spacing:1.2px;color:{pal.Secondary};text-transform:uppercase;\">{H(docTitle)}</div><div class=\"mono\" style=\"font-size:15.5px;font-weight:800;color:{pal.Primary};margin-top:2px;\">#{H(docNo)}</div><div style=\"font-size:9.2px;color:{pal.Muted};\">{H(dateStr)} &bull; {H(timeStr)}</div></div>");
            sb.AppendLine("</div>");
        }
        else if (isCompact)
        {
            sb.AppendLine($"<div style=\"border-top:4px solid {pal.Primary};border-bottom:1.5px solid {pal.Primary};padding:8px 0;margin-bottom:10px;display:flex;justify-content:space-between;align-items:center;gap:12px;\">");
            sb.AppendLine("<div style=\"display:flex;align-items:center;gap:10px;\">");
            if (!string.IsNullOrEmpty(logo))
                sb.AppendLine($"<img src=\"{H(logo)}\" alt=\"Logo\" style=\"max-height:32px;max-width:90px;object-fit:contain;\"/>");
            sb.AppendLine($"<div><div style=\"font-size:15.5px;font-weight:800;color:{pal.Primary};\">{H(setting.StoreName)}</div><div style=\"font-size:8.8px;color:{pal.Muted};\">{H(setting.Address)}{(!string.IsNullOrEmpty(contactLine) ? $" &bull; {contactLine}" : "")}</div></div></div>");
            sb.AppendLine($"<div style=\"text-align:right;flex-shrink:0;\"><span style=\"background:{pal.Primary};color:#fff;font-size:9px;font-weight:800;padding:2px 8px;border-radius:3px;\">{H(docTitle)}</span><div class=\"mono\" style=\"font-size:12.5px;font-weight:800;margin-top:3px;\">#{H(docNo)} &bull; <span style=\"font-weight:500;font-size:9.2px;color:{pal.Muted};\">{H(dateStr)}</span></div></div>");
            sb.AppendLine("</div>");
        }
        else if (isBarcode)
        {
            var barSvg = GenerateCode128Svg(docNo, 152, 26);
            sb.AppendLine($"<div style=\"border-bottom:2.5px solid {pal.Primary};padding-bottom:10px;margin-bottom:12px;display:flex;justify-content:space-between;align-items:center;gap:16px;\">");
            sb.AppendLine("<div style=\"display:flex;align-items:center;gap:12px;\">");
            if (!string.IsNullOrEmpty(logo))
                sb.AppendLine($"<img src=\"{H(logo)}\" alt=\"Logo\" style=\"max-height:40px;max-width:100px;object-fit:contain;\"/>");
            sb.AppendLine($"<div style=\"border-left:4px solid {pal.Accent};padding-left:9px;\"><div style=\"font-size:18px;font-weight:900;color:{pal.Primary};\">{H(setting.StoreName)}</div>{(!string.IsNullOrEmpty(setting.Address) ? $"<div style=\"font-size:9.5px;color:{pal.Muted};margin-top:2px;\">{H(setting.Address)}</div>" : "")}{(!string.IsNullOrEmpty(contactLine) ? $"<div style=\"font-size:9.5px;color:{pal.Muted};\">{contactLine}</div>" : "")}</div></div>");
            sb.AppendLine($"<div style=\"text-align:right;\"><div style=\"display:inline-block;background:{pal.Primary};color:#fff;font-size:9px;font-weight:800;letter-spacing:0.6px;padding:2px 9px;border-radius:3px;\">VERIFIED {H(docTitle)}</div><div style=\"margin-top:4px;display:flex;justify-content:flex-end;\">{barSvg}</div><div class=\"mono\" style=\"font-size:10px;font-weight:800;margin-top:2px;\">#{H(docNo)} &bull; {H(dateStr)}</div></div>");
            sb.AppendLine("</div>");
        }
        else
        {
            // Standard Corporate
            sb.AppendLine($"<div style=\"margin:-26px -30px 14px;padding:18px 30px 12px;border-top:6px solid {pal.Primary};border-bottom:2px solid {pal.Primary};display:flex;justify-content:space-between;align-items:flex-start;gap:16px;\">");
            sb.AppendLine("<div style=\"display:flex;align-items:center;gap:12px;\">");
            if (!string.IsNullOrEmpty(logo))
                sb.AppendLine($"<img src=\"{H(logo)}\" alt=\"Logo\" style=\"max-height:44px;max-width:115px;object-fit:contain;\"/>");
            sb.AppendLine($"<div><div style=\"font-size:19px;font-weight:900;color:{pal.Primary};\">{H(setting.StoreName)}</div>{(!string.IsNullOrEmpty(setting.Address) ? $"<div style=\"font-size:9.6px;color:{pal.Muted};margin-top:2px;\">{H(setting.Address)}</div>" : "")}{(!string.IsNullOrEmpty(contactLine) ? $"<div style=\"font-size:9.6px;color:{pal.Muted};\">{contactLine}</div>" : "")}</div></div>");
            sb.AppendLine($"<div style=\"text-align:right;flex-shrink:0;\"><div style=\"display:inline-block;background:{pal.Primary};color:#ffffff;font-size:10.5px;font-weight:800;letter-spacing:0.7px;padding:3.5px 12px;border-radius:4px;\">{H(docTitle)}</div><div class=\"mono\" style=\"font-size:14.5px;font-weight:800;color:{pal.Primary};margin-top:4px;\">#{H(docNo)}</div><div style=\"font-size:9.2px;color:{pal.Muted};\">{H(dateStr)} &bull; {H(timeStr)}</div><div style=\"margin-top:4px;\">{BuildStatusBadgeHtml(fin.StatusText, fin.StatusKind, pal, compact: false)}</div></div>");
            sb.AppendLine("</div>");
        }

        // 2. COMPLETE INVOICE METADATA CARDS
        if (isDetailed)
        {
            sb.AppendLine("<div style=\"display:grid;grid-template-columns:1.25fr 1fr 1fr;gap:10px;margin-bottom:13px;font-size:9.5px;\">");
            sb.AppendLine($"<div style=\"background:{pal.SoftBg};border:1px solid {pal.Border};border-left:3.5px solid {pal.Primary};border-radius:5px;padding:8px 10px;\"><div style=\"font-size:8px;font-weight:800;color:{pal.Secondary};text-transform:uppercase;letter-spacing:0.5px;\">Billed To Customer</div><div style=\"font-size:11.5px;font-weight:800;margin-top:2px;\">{H(customerDisplay)}</div>{(!string.IsNullOrWhiteSpace(invoice.CustomerPhone) ? $"<div style=\"color:{pal.Muted};margin-top:2px;\">Phone: {H(invoice.CustomerPhone)}</div>" : "")}{(!string.IsNullOrWhiteSpace(invoice.CustomerAddress) ? $"<div style=\"color:{pal.Muted};font-size:8.8px;\">{H(invoice.CustomerAddress)}</div>" : "")}</div>");
            sb.AppendLine($"<div style=\"background:#f8fafc;border:1px solid #e2e8f0;border-left:3.5px solid {pal.Accent};border-radius:5px;padding:8px 10px;\"><div style=\"font-size:8px;font-weight:800;color:{pal.Muted};text-transform:uppercase;letter-spacing:0.5px;\">Invoice Information</div><div style=\"margin-top:2px;\"><span style=\"color:#64748b;\">Sale Mode:</span> <strong>{H(saleModeLabel)} ({H(invTypeLabel)})</strong></div><div><span style=\"color:#64748b;\">Payment:</span> <strong>{H(payMethodLabel)}</strong></div><div><span style=\"color:#64748b;\">Cashier:</span> <strong>{H(invoice.UserName ?? "—")}</strong></div><div style=\"margin-top:3px;\">{BuildStatusBadgeHtml(fin.StatusText, fin.StatusKind, pal, compact: true)}</div></div>");
            sb.AppendLine($"<div style=\"background:#f8fafc;border:1px solid #e2e8f0;border-left:3.5px solid #64748b;border-radius:5px;padding:8px 10px;\"><div style=\"font-size:8px;font-weight:800;color:{pal.Muted};text-transform:uppercase;letter-spacing:0.5px;\">Clinical &amp; Order Ref</div><div style=\"margin-top:2px;\"><span style=\"color:#64748b;\">Doctor:</span> <strong>{H(string.IsNullOrWhiteSpace(invoice.DoctorName) ? "—" : invoice.DoctorName)}</strong></div><div><span style=\"color:#64748b;\">Patient:</span> <strong>{H(string.IsNullOrWhiteSpace(invoice.PatientName) ? "—" : invoice.PatientName)}</strong></div><div><span style=\"color:#64748b;\">Line Items:</span> <strong>{itemCount} items ({Nf(fin.TotalUnits)} qty)</strong></div></div>");
            sb.AppendLine("</div>");
        }
        else if (isBold)
        {
            sb.AppendLine("<div style=\"display:grid;grid-template-columns:1.3fr 1fr 0.95fr;gap:11px;margin-bottom:14px;font-size:9.5px;\">");
            sb.AppendLine($"<div style=\"background:{pal.SoftBg};border:1px solid {pal.Border};border-radius:6px;padding:9px 11px;\"><div style=\"font-size:8px;font-weight:800;color:{pal.Secondary};text-transform:uppercase;\">Customer Details</div><div style=\"font-size:12px;font-weight:900;margin-top:2px;\">{H(customerDisplay)}</div>{(!string.IsNullOrWhiteSpace(invoice.CustomerPhone) ? $"<div style=\"color:#334155;margin-top:2px;\">Ph: {H(invoice.CustomerPhone)}</div>" : "")}{(!string.IsNullOrWhiteSpace(invoice.CustomerAddress) ? $"<div style=\"color:#475569;font-size:8.8px;\">{H(invoice.CustomerAddress)}</div>" : "")}</div>");
            sb.AppendLine($"<div style=\"background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;padding:9px 11px;\"><div style=\"font-size:8px;font-weight:800;color:#64748b;text-transform:uppercase;\">Invoice Details</div><div style=\"margin-top:2px;\"><span style=\"color:#64748b;\">Mode:</span> <strong>{H(saleModeLabel)} &bull; {H(invTypeLabel)}</strong></div><div><span style=\"color:#64748b;\">Payment:</span> <strong>{H(payMethodLabel)}</strong></div><div><span style=\"color:#64748b;\">Cashier:</span> <strong>{H(invoice.UserName ?? "—")}</strong></div>{(!string.IsNullOrWhiteSpace(invoice.DoctorName) ? $"<div><span style=\"color:#64748b;\">Doctor:</span> <strong>{H(invoice.DoctorName)}</strong></div>" : "")}</div>");
            sb.AppendLine($"<div style=\"background:{pal.Primary};color:#ffffff;border-radius:6px;padding:9px 12px;display:flex;flex-direction:column;justify-content:center;align-items:flex-end;\"><div style=\"font-size:8px;font-weight:700;letter-spacing:0.6px;color:#a7f3d0;text-transform:uppercase;\">Net Invoice Total</div><div class=\"tabular\" style=\"font-size:17px;font-weight:900;margin-top:2px;\">Rs. {F2(fin.NetTotal)}</div><div style=\"margin-top:4px;\">{BuildStatusBadgeHtml(fin.StatusText, fin.StatusKind, pal, compact: true)}</div></div>");
            sb.AppendLine("</div>");
        }
        else if (isCompact)
        {
            sb.AppendLine($"<div style=\"background:{pal.SoftBg};border:1px solid {pal.Border};border-radius:4px;padding:6px 10px;margin-bottom:9px;display:grid;grid-template-columns:1.4fr 1fr 1fr;gap:8px;font-size:8.8px;\">");
            sb.AppendLine($"<div><span style=\"color:#64748b;font-weight:700;text-transform:uppercase;font-size:7.8px;\">Billed To:</span><div style=\"font-weight:800;font-size:10px;\">{H(customerDisplay)}</div>{(!string.IsNullOrWhiteSpace(invoice.CustomerPhone) ? $"<div style=\"color:#475569;\">Ph: {H(invoice.CustomerPhone)}</div>" : "")}</div>");
            sb.AppendLine($"<div><div><span style=\"color:#64748b;\">Mode:</span> <strong>{H(saleModeLabel)} / {H(invTypeLabel)}</strong></div><div><span style=\"color:#64748b;\">Payment:</span> <strong>{H(payMethodLabel)}</strong></div><div><span style=\"color:#64748b;\">Cashier:</span> <strong>{H(invoice.UserName ?? "—")}</strong></div></div>");
            sb.AppendLine($"<div style=\"text-align:right;\"><div>{BuildStatusBadgeHtml(fin.StatusText, fin.StatusKind, pal, compact: true)}</div>{(!string.IsNullOrWhiteSpace(invoice.DoctorName) ? $"<div style=\"margin-top:2px;\"><span style=\"color:#64748b;\">Dr:</span> <strong>{H(invoice.DoctorName)}</strong></div>" : "")}<div><span style=\"color:#64748b;\">Items:</span> <strong>{itemCount} ({Nf(fin.TotalUnits)}u)</strong></div></div>");
            sb.AppendLine("</div>");
        }
        else
        {
            var boxStyle = isMinimal
                ? "border-bottom:1px solid #cbd5e1;padding:0 0 10px;margin-bottom:12px;"
                : $"background:{pal.SoftBg};border:1px solid {pal.Border};border-left:4px solid {(isBarcode ? pal.Accent : pal.Primary)};border-radius:5px;padding:9px 12px;margin-bottom:12px;";
            sb.AppendLine($"<div style=\"{boxStyle}display:flex;justify-content:space-between;gap:16px;font-size:9.5px;\">");
            sb.AppendLine($"<div style=\"flex:1.2;\"><div style=\"font-size:8px;font-weight:800;color:#64748b;text-transform:uppercase;\">Billed To</div><div style=\"font-size:11.5px;font-weight:800;margin-top:2px;\">{H(customerDisplay)}</div>{(!string.IsNullOrWhiteSpace(invoice.CustomerPhone) ? $"<div style=\"color:#475569;\">Phone: {H(invoice.CustomerPhone)}</div>" : "")}{(!string.IsNullOrWhiteSpace(invoice.CustomerAddress) ? $"<div style=\"color:#475569;font-size:8.8px;\">{H(invoice.CustomerAddress)}</div>" : "")}");
            if (!string.IsNullOrWhiteSpace(invoice.DoctorName) || !string.IsNullOrWhiteSpace(invoice.PatientName))
            {
                var docPt = string.Join(" &bull; ", new[]
                {
                    !string.IsNullOrWhiteSpace(invoice.DoctorName) ? $"<strong>Doctor:</strong> {H(invoice.DoctorName)}" : null,
                    !string.IsNullOrWhiteSpace(invoice.PatientName) ? $"<strong>Patient:</strong> {H(invoice.PatientName)}" : null
                }.Where(x => x != null));
                sb.AppendLine($"<div style=\"color:#334155;font-size:8.8px;margin-top:2px;\">{docPt}</div>");
            }
            sb.AppendLine("</div>");
            sb.AppendLine($"<div style=\"flex:1;text-align:right;\"><div style=\"font-size:8px;font-weight:800;color:#64748b;text-transform:uppercase;margin-bottom:2px;\">Invoice Information</div><div><span style=\"color:#64748b;\">Sale Mode:</span> <strong>{H(saleModeLabel)} ({H(invTypeLabel)})</strong></div><div><span style=\"color:#64748b;\">Payment Method:</span> <strong>{H(payMethodLabel)}</strong></div><div><span style=\"color:#64748b;\">Cashier:</span> <strong>{H(invoice.UserName ?? "—")}</strong></div><div><span style=\"color:#64748b;\">Items / Qty:</span> <strong>{itemCount} items ({Nf(fin.TotalUnits)} units)</strong></div>{(isMinimal || isBarcode ? $"<div style=\"margin-top:3px;\">{BuildStatusBadgeHtml(fin.StatusText, fin.StatusKind, pal, compact: true)}</div>" : "")}</div>");
            sb.AppendLine("</div>");
        }

        // 3. ITEMS TABLE WITH DISCOUNTS
        var thStyle = isMinimal
            ? $"background:#ffffff;color:{pal.Primary};border-top:1.5px solid {pal.Primary};border-bottom:1.5px solid {pal.Primary};font-size:8.5px;font-weight:800;text-transform:uppercase;padding:6.5px 6px;"
            : $"background:{pal.Primary};color:#ffffff;font-size:{(isCompact ? "8px" : "8.6px")};font-weight:800;text-transform:uppercase;letter-spacing:0.35px;padding:{(isCompact ? "5px 5px" : "7px 6.5px")};";
        var tdPad = isCompact ? "4.5px 5px" : "6.5px 6.5px";

        sb.AppendLine($"<table class=\"items\" style=\"border:{(isMinimal ? "none" : "1px solid #e2e8f0")};\"><thead><tr>");
        if (isDetailed || isBarcode)
        {
            sb.AppendLine($"<th style=\"{thStyle}width:26px;text-align:center;\">#</th>");
            sb.AppendLine($"<th style=\"{thStyle}text-align:left;\">{(isBarcode ? "Product &amp; Barcode / SKU" : "Product Description")}</th>");
            sb.AppendLine($"<th style=\"{thStyle}width:52px;text-align:center;\">Pack</th>");
            sb.AppendLine($"<th style=\"{thStyle}width:88px;text-align:left;\">Batch / Exp</th>");
            sb.AppendLine($"<th style=\"{thStyle}width:54px;text-align:right;\">Qty</th>");
            sb.AppendLine($"<th style=\"{thStyle}width:70px;text-align:right;\">Rate</th>");
            sb.AppendLine($"<th style=\"{thStyle}width:78px;text-align:right;\">Discount</th>");
            sb.AppendLine($"<th style=\"{thStyle}width:84px;text-align:right;\">Net Total</th>");
        }
        else
        {
            sb.AppendLine($"<th style=\"{thStyle}width:26px;text-align:center;\">#</th>");
            sb.AppendLine($"<th style=\"{thStyle}text-align:left;\">Item Description</th>");
            sb.AppendLine($"<th style=\"{thStyle}width:62px;text-align:right;\">Qty</th>");
            sb.AppendLine($"<th style=\"{thStyle}width:82px;text-align:right;\">Rate (Rs.)</th>");
            sb.AppendLine($"<th style=\"{thStyle}width:92px;text-align:right;\">Discount</th>");
            sb.AppendLine($"<th style=\"{thStyle}width:96px;text-align:right;\">Net Total</th>");
        }
        sb.AppendLine("</tr></thead><tbody>");

        for (var idx = 0; idx < invoice.Items.Count; idx++)
        {
            var it = invoice.Items[idx];
            var bg = (!isMinimal && idx % 2 == 1) ? "#f8fafc" : "#ffffff";
            var lineGross = Math.Round(it.Quantity * it.Price, 2);
            var lineDiscAmt = it.ItemDiscountAmount > 0
                ? it.ItemDiscountAmount
                : (it.ItemDiscountPercentage > 0 ? Math.Round(lineGross * it.ItemDiscountPercentage / 100m, 2) : Math.Max(0m, Math.Round(lineGross - it.SubTotal, 2)));
            var expStr = ShortExpiry(it.Expiry);

            var discCell = (it.ItemDiscountPercentage > 0 || lineDiscAmt > 0)
                ? (it.ItemDiscountPercentage > 0
                    ? $"<div style=\"color:{pal.DiscountColor};font-weight:700;\">{Nf(it.ItemDiscountPercentage)}%</div><div style=\"font-size:8px;color:#64748b;\">-Rs. {F2(lineDiscAmt)}</div>"
                    : $"<span style=\"color:{pal.DiscountColor};font-weight:700;\">-Rs. {F2(lineDiscAmt)}</span>")
                : "<span style=\"color:#94a3b8;\">—</span>";

            var qtyCell = it.Bonus > 0
                ? $"<div>{Nf(it.Quantity)}</div><div style=\"font-size:7.8px;color:#047857;\">+{it.Bonus} Bns</div>"
                : Nf(it.Quantity);

            sb.AppendLine($"<tr style=\"background:{bg};\">");
            sb.AppendLine($"<td style=\"padding:{tdPad};border-bottom:1px solid #e2e8f0;text-align:center;color:#64748b;font-size:9px;\">{idx + 1}</td>");
            if (isDetailed || isBarcode)
            {
                var codeSub = isBarcode
                    ? string.Join(" &bull; ", new[] { H(it.ProductCode), H(it.Barcode) }.Where(x => !string.IsNullOrWhiteSpace(x)))
                    : (!string.IsNullOrWhiteSpace(it.ProductCode) ? $"Code: {H(it.ProductCode)}" : "");
                sb.AppendLine($"<td style=\"padding:{tdPad};border-bottom:1px solid #e2e8f0;\"><div style=\"font-weight:700;font-size:9.8px;\">{H(it.ProductName)}</div>{(!string.IsNullOrWhiteSpace(codeSub) ? $"<div class=\"mono\" style=\"font-size:8px;color:#64748b;\">{codeSub}</div>" : "")}</td>");
                sb.AppendLine($"<td style=\"padding:{tdPad};border-bottom:1px solid #e2e8f0;text-align:center;font-size:8.8px;color:#475569;\">{(it.PackSize > 1 ? $"1&times;{it.PackSize}" : "1")}</td>");
                sb.AppendLine($"<td style=\"padding:{tdPad};border-bottom:1px solid #e2e8f0;font-size:8.6px;color:#334155;\"><div class=\"mono\" style=\"font-weight:600;\">{H(string.IsNullOrWhiteSpace(it.BatchNumber) ? "—" : it.BatchNumber)}</div>{(!string.IsNullOrWhiteSpace(expStr) ? $"<div style=\"font-size:7.8px;color:#64748b;\">Exp: {H(expStr)}</div>" : "")}</td>");
            }
            else
            {
                var subMeta = new List<string>();
                if (it.PackSize > 1) subMeta.Add($"Pack: 1×{it.PackSize}");
                if (!string.IsNullOrWhiteSpace(it.BatchNumber)) subMeta.Add($"Batch: {it.BatchNumber}");
                if (!string.IsNullOrWhiteSpace(expStr)) subMeta.Add($"Exp: {expStr}");
                sb.AppendLine($"<td style=\"padding:{tdPad};border-bottom:1px solid #e2e8f0;\"><div style=\"font-weight:700;font-size:{(isCompact ? "9.2px" : "10px")};\">{H(it.ProductName)}</div>{(subMeta.Count > 0 ? $"<div style=\"font-size:7.8px;color:#64748b;\">{H(string.Join(" • ", subMeta))}</div>" : "")}</td>");
            }
            sb.AppendLine($"<td class=\"tabular\" style=\"padding:{tdPad};border-bottom:1px solid #e2e8f0;text-align:right;font-weight:700;font-size:9.8px;\">{qtyCell}</td>");
            sb.AppendLine($"<td class=\"tabular\" style=\"padding:{tdPad};border-bottom:1px solid #e2e8f0;text-align:right;font-size:9.6px;\">{F2(it.Price)}</td>");
            sb.AppendLine($"<td class=\"tabular\" style=\"padding:{tdPad};border-bottom:1px solid #e2e8f0;text-align:right;font-size:9px;\">{discCell}</td>");
            sb.AppendLine($"<td class=\"tabular\" style=\"padding:{tdPad};border-bottom:1px solid #e2e8f0;text-align:right;font-weight:800;font-size:10px;\">{F2(it.SubTotal)}</td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");

        // 4. BOTTOM SPLIT: Remarks + Savings + Verification Left | Financial Totals Right
        sb.AppendLine("<div style=\"display:flex;align-items:flex-start;justify-content:space-between;margin-top:13px;\">");
        sb.AppendLine("<div style=\"flex:1;padding-right:16px;\">");
        if (!string.IsNullOrWhiteSpace(invoice.Remarks))
        {
            sb.AppendLine($"<div style=\"background:#f8fafc;border:1px solid #e2e8f0;border-radius:5px;padding:7px 10px;font-size:9.2px;\"><div style=\"font-size:7.8px;font-weight:800;text-transform:uppercase;color:#64748b;\">Invoice Remarks</div><div style=\"color:#334155;margin-top:2px;white-space:pre-wrap;\">{H(invoice.Remarks)}</div></div>");
        }
        if (fin.TotalSaved > 0.009m)
        {
            sb.AppendLine($"<div style=\"margin-top:7px;padding:6px 10px;border-radius:5px;background:#ecfdf5;border:1px solid #a7f3d0;color:#065f46;font-size:9.2px;font-weight:700;display:flex;justify-content:space-between;align-items:center;\"><span>Total Discount Savings</span><span class=\"tabular\" style=\"font-size:10.5px;font-weight:900;\">Rs. {F2(fin.TotalSaved)}</span></div>");
        }
        if (isBarcode)
        {
            var qrSvg = GenerateQrSvg($"sale_invoice:{docNo}", 46);
            sb.AppendLine($"<div style=\"margin-top:9px;border:1px dashed #a1a1aa;border-radius:5px;padding:7px 10px;background:#fafafa;display:flex;align-items:center;justify-content:space-between;gap:10px;\"><div><div style=\"font-size:7.8px;font-weight:800;text-transform:uppercase;color:#52525b;\">Document Verification Strip</div><div class=\"mono\" style=\"font-size:9.2px;font-weight:800;color:#18181b;margin-top:2px;\">REF: {H(docNo)} &bull; ITEMS: {itemCount}</div><div style=\"font-size:7.8px;color:#71717a;\">Scan QR / Barcode to verify invoice authenticity</div></div><div>{qrSvg}</div></div>");
        }
        sb.AppendLine("</div>");

        var grandRowStyle = isMinimal
            ? $"border-top:2px solid {pal.Primary};border-bottom:2px solid {pal.Primary};background:#f8fafc;color:{pal.Primary};font-weight:900;font-size:12px;"
            : $"background:{pal.Primary};color:#ffffff;font-weight:900;font-size:12px;";
        sb.AppendLine($"<div style=\"width:280px;flex-shrink:0;border:{(isMinimal ? "none" : "1px solid #cbd5e1")};border-radius:5px;overflow:hidden;font-size:9.8px;\"><table class=\"tot\">");
        sb.AppendLine($"<tr><td style=\"padding:4.5px 9px;color:#475569;border-bottom:1px solid #e2e8f0;\">Gross Amount</td><td class=\"tabular\" style=\"padding:4.5px 9px;text-align:right;font-weight:700;border-bottom:1px solid #e2e8f0;\">Rs. {F2(fin.RawGross)}</td></tr>");
        if (fin.ItemDiscTotal > 0.009m)
            sb.AppendLine($"<tr><td style=\"padding:4.5px 9px;color:{pal.DiscountColor};border-bottom:1px solid #e2e8f0;\">Item Discounts</td><td class=\"tabular\" style=\"padding:4.5px 9px;text-align:right;font-weight:700;color:{pal.DiscountColor};border-bottom:1px solid #e2e8f0;\">- Rs. {F2(fin.ItemDiscTotal)}</td></tr>");
        if (fin.BillDisc > 0.009m)
        {
            var discLabel = invoice.DiscountPercentage > 0 ? $"Invoice Discount ({Nf(invoice.DiscountPercentage)}%)" : "Invoice Discount";
            sb.AppendLine($"<tr><td style=\"padding:4.5px 9px;color:{pal.DiscountColor};border-bottom:1px solid #e2e8f0;\">{H(discLabel)}</td><td class=\"tabular\" style=\"padding:4.5px 9px;text-align:right;font-weight:700;color:{pal.DiscountColor};border-bottom:1px solid #e2e8f0;\">- Rs. {F2(fin.BillDisc)}</td></tr>");
        }
        if (fin.Tax > 0.009m)
        {
            var taxLabel = invoice.TaxPercentage > 0 ? $"Sales Tax / GST ({Nf(invoice.TaxPercentage)}%)" : "Sales Tax / GST";
            sb.AppendLine($"<tr><td style=\"padding:4.5px 9px;color:#475569;border-bottom:1px solid #e2e8f0;\">{H(taxLabel)}</td><td class=\"tabular\" style=\"padding:4.5px 9px;text-align:right;font-weight:700;border-bottom:1px solid #e2e8f0;\">Rs. {F2(fin.Tax)}</td></tr>");
        }
        if (fin.FbrFee > 0.009m)
            sb.AppendLine($"<tr><td style=\"padding:4.5px 9px;color:#475569;border-bottom:1px solid #e2e8f0;\">FBR POS Service Fee</td><td class=\"tabular\" style=\"padding:4.5px 9px;text-align:right;font-weight:700;border-bottom:1px solid #e2e8f0;\">Rs. {F2(fin.FbrFee)}</td></tr>");

        sb.AppendLine($"<tr style=\"{grandRowStyle}\"><td style=\"padding:7px 9px;\">NET PAYABLE TOTAL</td><td class=\"tabular\" style=\"padding:7px 9px;text-align:right;\">Rs. {F2(fin.NetTotal)}</td></tr>");
        sb.AppendLine($"<tr><td style=\"padding:4.5px 9px;color:#334155;border-bottom:1px solid #e2e8f0;\">Amount Received</td><td class=\"tabular\" style=\"padding:4.5px 9px;text-align:right;font-weight:700;color:#047857;border-bottom:1px solid #e2e8f0;\">Rs. {F2(fin.Received)}</td></tr>");

        if (fin.BalanceThis > 0.009m)
            sb.AppendLine($"<tr style=\"background:#fff7ed;\"><td style=\"padding:5px 9px;color:#9a3412;font-weight:800;border-bottom:1px solid #fed7aa;\">Balance Due (This Invoice)</td><td class=\"tabular\" style=\"padding:5px 9px;text-align:right;font-weight:900;color:#c2410c;border-bottom:1px solid #fed7aa;\">Rs. {F2(fin.BalanceThis)}</td></tr>");
        else if (fin.ChangeDue > 0.009m)
            sb.AppendLine($"<tr style=\"background:#f0fdf4;\"><td style=\"padding:5px 9px;color:#166534;font-weight:700;border-bottom:1px solid #bbf7d0;\">Change Returned</td><td class=\"tabular\" style=\"padding:5px 9px;text-align:right;font-weight:800;color:#166534;border-bottom:1px solid #bbf7d0;\">Rs. {F2(fin.ChangeDue)}</td></tr>");
        else
            sb.AppendLine("<tr style=\"background:#f0fdf4;\"><td style=\"padding:5px 9px;color:#166534;font-weight:700;border-bottom:1px solid #bbf7d0;\">Payment Status</td><td style=\"padding:5px 9px;text-align:right;font-weight:800;color:#166534;border-bottom:1px solid #bbf7d0;\">SETTLED</td></tr>");

        if (showTotalDue)
            sb.AppendLine($"<tr style=\"background:#fef2f2;\"><td style=\"padding:5.5px 9px;color:#991b1b;font-weight:800;\">Total Account Balance Due</td><td class=\"tabular\" style=\"padding:5.5px 9px;text-align:right;font-weight:900;color:#b91c1c;\">Rs. {F2(customerTotalDue!.Value)}</td></tr>");
        sb.AppendLine("</table></div></div>");

        sb.AppendLine(fbrFooterHtml);

        // 5. EMBEDDED PERMANENT FOOTER
        sb.AppendLine("<div style=\"margin-top:auto;padding-top:14px;\">");
        if (!string.IsNullOrEmpty(footerNote))
        {
            var isUrdu = ContainsUrdu(footerNote);
            var borderAccent = isMinimal ? "#334155" : "#d97706";
            sb.AppendLine($"<div class=\"{(isUrdu ? "urdu" : "")}\" style=\"margin-bottom:9px;padding:7px 10px;border-radius:5px;background:{(isMinimal ? "#ffffff" : "#fffbeb")};border:1px solid {(isMinimal ? "#cbd5e1" : "#fde68a")};{(isUrdu ? $"border-right:3px solid {borderAccent};" : $"border-left:3px solid {borderAccent};")}font-size:{(isUrdu ? "11px" : "9.2px")};color:#334155;white-space:pre-wrap;\"><strong style=\"color:#0f172a;\">{(isUrdu ? "شرائط و ضوابط:" : "Terms & Return Policy:")}</strong> {H(footerNote)}</div>");
        }

        if (isDetailed)
        {
            sb.AppendLine($"<div style=\"display:flex;justify-content:space-between;align-items:flex-end;margin:12px 0 10px;padding:0 4px;\"><div style=\"width:170px;border-top:1px solid #94a3b8;padding-top:3px;font-size:8.8px;color:#64748b;text-align:center;\">Customer Signature</div><div style=\"font-size:9.5px;font-style:italic;color:#64748b;\">Thank you for trusting {H(setting.StoreName)}!</div><div style=\"width:170px;border-top:1px solid #94a3b8;padding-top:3px;font-size:8.8px;color:#64748b;text-align:center;\">Authorized Signature / Stamp</div></div>");
        }

        sb.AppendLine($"<div style=\"border-top:{(isMinimal ? "1.5px solid #1e293b" : $"2px solid {pal.Primary}")};background:{(isBold ? pal.Primary : (isMinimal ? "#ffffff" : pal.SoftBg))};color:{(isBold ? "#ffffff" : "#334155")};padding:8px 13px;border-radius:{(isMinimal ? "0" : "5px")};display:flex;align-items:center;justify-content:space-between;gap:12px;\">");
        sb.AppendLine($"<div style=\"font-size:8.8px;color:{(isBold ? "#d1fae5" : "#64748b")};\">Generated {DateTime.Now:dd MMM yyyy, hh:mm tt} &bull; Page 1 of 1</div>");
        sb.AppendLine($"<div style=\"display:flex;align-items:center;gap:8px;\"><span style=\"font-size:9.6px;font-weight:800;letter-spacing:0.3px;color:{(isBold ? "#ffffff" : pal.Primary)};\">{H(PdfDocumentGenerator.DeveloperCreditTitle)}</span><span class=\"mono\" style=\"display:inline-block;padding:1.5px 7px;border-radius:999px;background:{(isBold ? pal.Accent : pal.Primary)};color:{(isBold ? "#052e16" : "#ffffff")};font-size:8.8px;font-weight:800;\">{H(PdfDocumentGenerator.DeveloperCreditPhone)}</span></div>");
        sb.AppendLine("</div></div>");

        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Renders Patient Urdu/English Dosage Stickers (`sale_invoice_dosage_labels.blade.php` — `classic`, `minimal`, `bold`, `urdu_focus`).
    /// </summary>
    public string RenderDosageStickers(
        PrintInvoiceViewModel invoice,
        PrintSettingViewModel setting,
        PrintStickerConfigViewModel? cfg = null,
        IDictionary<string, string>? queryOverrides = null)
    {
        cfg ??= new PrintStickerConfigViewModel();
        var w = Math.Clamp(cfg.WidthMm, 25, 150);
        var h = Math.Clamp(cfg.HeightMm, 15, 120);
        var printerMode = cfg.PrinterMode == "label_printer" ? "label_printer" : "receipt_roll";
        var template = ValidStickerTemplates.Contains(cfg.Template) ? cfg.Template : "classic";
        var scaleMult = cfg.FontScale == "compact" ? 0.9 : (cfg.FontScale == "large" ? 1.15 : 1.0);

        var ptHeader = Math.Round(Math.Clamp(cfg.FontSizeHeader, 4.0, 28.0) * scaleMult, 2);
        var ptPatient = Math.Round(Math.Clamp(cfg.FontSizePatient, 4.0, 28.0) * scaleMult, 2);
        var ptProduct = Math.Round(Math.Clamp(cfg.FontSizeProduct ?? (template == "urdu_focus" ? 7.2 : 8.0), 4.0, 28.0) * scaleMult, 2);
        var ptDosage = Math.Round(Math.Clamp(cfg.FontSizeDosage ?? (template == "urdu_focus" ? 8.6 : 7.5), 4.0, 28.0) * scaleMult, 2);
        var ptFooter = Math.Round(Math.Clamp(cfg.FontSizeFooter, 4.0, 28.0) * scaleMult, 2);

        var calibrationHtml = RenderCalibrationStyles(setting, queryOverrides, isThermal: false);
        var storeName = string.IsNullOrWhiteSpace(setting.StoreName) ? "Pharmacy" : setting.StoreName;
        var patientName = !string.IsNullOrWhiteSpace(invoice.PatientName) ? invoice.PatientName : (invoice.CustomerName ?? "Walk-in Patient");
        var invDate = (invoice.Date ?? DateTime.Now).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.AppendLine($"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\"><title>Dosage Stickers ({w}×{h}mm) — {H(invoice.PostedNumber)}</title>");
        sb.AppendLine(calibrationHtml);
        sb.AppendLine("<style>");
        sb.AppendLine(printerMode == "label_printer" ? $"@page {{ size: {w}mm {h}mm; margin: 0; }}" : "@page { margin: 0; }");
        sb.AppendLine("*{box-sizing:border-box;margin:0;padding:0;}html,body{margin:0;padding:0;background:#f1f5f9;color:#000;}body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Noto Nastaliq Urdu','Jameel Noori Nastaleeq',Arial,sans-serif;-webkit-print-color-adjust:exact;print-color-adjust:exact;}.no-print-bar{background:#0f172a;color:#fff;padding:10px 16px;display:flex;align-items:center;justify-content:space-between;gap:10px;flex-wrap:wrap;font-size:12.5px;margin-bottom:12px;}.no-print-actions{display:flex;align-items:center;gap:8px;}.no-print-bar button{background:#2563eb;color:#fff;border:none;border-radius:6px;padding:6px 14px;font-weight:700;cursor:pointer;}.labels-wrapper{display:flex;flex-direction:column;align-items:center;gap:8px;padding-bottom:16px;}");
        sb.AppendLine($".sticker{{width:{w}mm;max-width:100%;{(printerMode == "label_printer" ? $"height:{h}mm;page-break-after:always;break-after:page;" : $"min-height:{h}mm;height:auto;page-break-inside:avoid;break-inside:avoid;")}background:#fff;border:1px dashed #94a3b8;padding:{(h >= 35 ? "2.2mm 2.8mm" : "1.6mm 2mm")};display:flex;flex-direction:column;justify-content:space-between;overflow:hidden;}}");
        sb.AppendLine($".sticker-head{{display:flex;align-items:center;justify-content:space-between;gap:1.5mm;border-bottom:{(template == "minimal" ? "none" : "0.3mm solid #000")};padding-bottom:0.5mm;font-size:{ptHeader.ToString(CultureInfo.InvariantCulture)}pt;font-weight:{cfg.FontWeightHeader};text-transform:uppercase;{(template == "bold" ? "background:#000;color:#fff;padding:0.7mm 1.2mm;margin:-1.4mm -2mm 0.6mm -2mm;border-bottom:none;" : "")}}}");
        sb.AppendLine($".sticker-patient{{display:flex;align-items:center;justify-content:space-between;gap:1mm;font-size:{ptPatient.ToString(CultureInfo.InvariantCulture)}pt;font-weight:{cfg.FontWeightPatient};margin-top:0.4mm;}}");
        sb.AppendLine($".sticker-product{{font-size:{ptProduct.ToString(CultureInfo.InvariantCulture)}pt;font-weight:{cfg.FontWeightProduct};line-height:1.15;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;margin:0.5mm 0;}}");
        sb.AppendLine($".sticker-dosage-box{{border:{(template == "minimal" ? "0.25mm solid #cbd5e1" : (template == "urdu_focus" ? "0.45mm solid #000" : "0.35mm solid #000"))};border-radius:1mm;padding:{(template == "urdu_focus" ? "1.1mm 1.4mm" : "0.7mm 1.2mm")};text-align:center;font-size:{ptDosage.ToString(CultureInfo.InvariantCulture)}pt;font-weight:{cfg.FontWeightDosage};line-height:1.35;background:{(template == "minimal" ? "#fff" : "#f8fafc")};font-family:'Noto Nastaliq Urdu','Jameel Noori Nastaleeq','Segoe UI',Tahoma,Arial,sans-serif;}}");
        sb.AppendLine($".sticker-foot{{display:flex;align-items:center;justify-content:space-between;gap:1mm;font-size:{ptFooter.ToString(CultureInfo.InvariantCulture)}pt;font-weight:{cfg.FontWeightFooter};margin-top:0.4mm;white-space:nowrap;overflow:hidden;}}.roll-cutter-guard{{display:none;}}");
        sb.AppendLine("@media print{.no-print-bar{display:none!important;}html,body{background:#fff!important;}.labels-wrapper{gap:0!important;padding:0!important;}.sticker{border:none!important;}.roll-cutter-guard{display:block!important;}}");
        sb.AppendLine("</style></head><body>");
        sb.AppendLine($"<div class=\"no-print-bar\"><div><strong>Patient Dosage Stickers ({w}mm × {h}mm)</strong><span style=\"margin-left:8px;opacity:0.8;\">Invoice: {H(invoice.PostedNumber)}</span></div><div class=\"no-print-actions\"><button type=\"button\" onclick=\"if(window.karobarDesktop&&window.karobarDesktop.silentPrint){{window.karobarDesktop.silentPrint();}}else{{window.print();}}\">Print Stickers</button></div></div>");
        sb.AppendLine("<div class=\"labels-wrapper\">");

        foreach (var item in invoice.Items)
        {
            var prodName = string.IsNullOrWhiteSpace(item.ProductName) ? "Medicine" : item.ProductName;
            var dosage = string.IsNullOrWhiteSpace(item.DosageInstruction) ? "As directed by physician / ڈاکٹر کی ہدایت کے مطابق" : item.DosageInstruction.Trim();
            var qty = (int)Math.Max(1, Math.Round(item.Quantity));

            sb.AppendLine("<div class=\"sticker\"><div>");
            if (cfg.ShowShopName || cfg.ShowInvoiceMeta)
            {
                sb.AppendLine($"<div class=\"sticker-head\"><span style=\"overflow:hidden;text-overflow:ellipsis;white-space:nowrap;\">{(cfg.ShowShopName ? H(storeName) : "")}</span><span style=\"white-space:nowrap;\">{(cfg.ShowInvoiceMeta ? H($"{invoice.PostedNumber} • {invDate}") : "")}</span></div>");
            }
            if (cfg.ShowPatient || cfg.ShowQuantity || cfg.ShowDoctor)
            {
                var ptDr = new List<string>();
                if (cfg.ShowPatient) ptDr.Add($"Pt: {H(patientName)}");
                if (cfg.ShowDoctor && !string.IsNullOrWhiteSpace(invoice.DoctorName)) ptDr.Add($"Dr: {H(invoice.DoctorName)}");
                sb.AppendLine($"<div class=\"sticker-patient\"><span style=\"overflow:hidden;text-overflow:ellipsis;white-space:nowrap;\">{string.Join(" • ", ptDr)}</span>{(cfg.ShowQuantity ? $"<span style=\"white-space:nowrap;\">Qty: {qty}</span>" : "")}</div>");
            }
            sb.AppendLine($"<div class=\"sticker-product\">{H(prodName)}</div></div>");
            sb.AppendLine($"<div class=\"sticker-dosage-box\" dir=\"auto\">{H(dosage)}</div>");
            var batchExp = new List<string>();
            if (cfg.ShowBatch && !string.IsNullOrWhiteSpace(item.BatchNumber)) batchExp.Add($"B#: {H(item.BatchNumber)}");
            if (cfg.ShowExpiry && !string.IsNullOrWhiteSpace(item.Expiry)) batchExp.Add($"Exp: {H(item.Expiry)}");
            sb.AppendLine($"<div class=\"sticker-foot\"><span>{string.Join(" • ", batchExp)}</span>{(!string.IsNullOrWhiteSpace(cfg.FooterNote) ? $"<span style=\"overflow:hidden;text-overflow:ellipsis;white-space:nowrap;\">{H(cfg.FooterNote)}</span>" : "")}</div></div>");
        }

        if (printerMode == "receipt_roll")
        {
            sb.AppendLine("<div class=\"roll-cutter-guard\">--- CUT / TEAR HERE ---</div>");
        }
        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Renders Cash Drawer Thermal Z-Report (`cash_drawer_thermal_zreport.blade.php`).
    /// </summary>
    public string RenderCashDrawerThermalZReport(
        PrintCashDrawerZReportViewModel model,
        PrintSettingViewModel setting,
        IDictionary<string, string>? queryOverrides = null)
    {
        var isClosed = string.Equals(model.Status, "closed", StringComparison.OrdinalIgnoreCase);
        var reportType = isClosed ? "DAY-END Z-REPORT SLIP" : "MID-SHIFT X-REPORT SLIP";
        var statusBadge = isClosed ? "CLOSED & RECONCILED" : "OPEN SHIFT (LIVE READING)";
        var openedAt = model.OpenedAt?.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture) ?? "—";
        var closedAt = model.ClosedAt?.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture) ?? "Active (In Progress)";
        var printedAt = DateTime.Now.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture);
        var calibrationHtml = RenderCalibrationStyles(setting, queryOverrides, isThermal: true);
        var logo = setting.PrintShowLogo ? setting.LogoUrl : null;

        var sb = new StringBuilder();
        sb.AppendLine($"<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>{H(reportType)} - Shift #{model.SessionId}</title>");
        sb.AppendLine("<style>@page{margin:0;}*{box-sizing:border-box;}html,body{margin:0;padding:0;color:#000;background:#fff;}body{font-family:'Courier New',Courier,monospace;font-weight:700;font-size:11.5px;line-height:1.32;-webkit-print-color-adjust:exact;print-color-adjust:exact;}.print-actions{margin:8px;text-align:right;}.print-actions button{padding:6px 12px;cursor:pointer;font-weight:bold;}@media print{.print-actions{display:none;}.receipt{margin:0;}}.receipt{width:78mm;max-width:100%;margin:0 auto;padding:2mm 4px 6mm;position:relative;overflow:hidden;}.center{text-align:center;}.right{text-align:right;}.logo{max-width:48mm;max-height:22mm;object-fit:contain;display:block;margin:0 auto 4px;}.hr{border-top:1px dashed #000;margin:5px 0;}.solid-hr{border-top:1px solid #000;margin:4px 0;}.banner-box{border:2px solid #000;padding:4px;margin:5px 0;text-align:center;}.banner-title{font-size:14px;font-weight:900;letter-spacing:0.5px;}.banner-sub{font-size:11px;margin-top:1px;}.sec-head{background:#000;color:#fff;padding:2px 4px;font-size:11px;font-weight:900;text-transform:uppercase;margin:6px 0 4px;letter-spacing:0.4px;}.pair{display:flex;justify-content:space-between;align-items:baseline;gap:6px;}.pair+.pair{margin-top:2px;}.pair .lbl{flex:1;min-width:0;}.pair .val{text-align:right;white-space:nowrap;}.sub-pair{display:flex;justify-content:space-between;align-items:baseline;padding-left:8px;font-size:10.5px;}.total-line{font-size:12.5px;font-weight:900;padding:2px 0;}.grand-box{border:1.5px solid #000;padding:4px 5px;margin:4px 0;}.notes-box{border:1px dashed #000;padding:4px 5px;margin-top:5px;font-size:10.5px;white-space:pre-wrap;}.sig-block{margin-top:16px;font-size:10.5px;}.sig-line{border-top:1px solid #000;margin-top:22px;padding-top:2px;text-align:center;}.foot{margin-top:8px;text-align:center;font-size:9.5px;}</style>");
        sb.AppendLine(calibrationHtml);
        sb.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\"><div class=\"print-actions\"><button onclick=\"window.print()\">Print Thermal Slip</button></div><div class=\"receipt\"><div class=\"content\">");
        if (!string.IsNullOrEmpty(logo)) sb.AppendLine($"<img src=\"{H(logo)}\" alt=\"Logo\" class=\"logo\">");
        sb.AppendLine($"<div class=\"center\" style=\"font-size:18px;font-weight:900;\">{H(setting.StoreName)}</div>");
        if (!string.IsNullOrEmpty(setting.Address)) sb.AppendLine($"<div class=\"center\" style=\"font-size:10px;\">{H(setting.Address)}</div>");
        if (!string.IsNullOrEmpty(setting.PhoneNumber)) sb.AppendLine($"<div class=\"center\" style=\"font-size:10px;\">Ph: {H(setting.PhoneNumber)}</div>");
        sb.AppendLine($"<div class=\"banner-box\"><div class=\"banner-title\">{H(reportType)}</div><div class=\"banner-sub\">SHIFT #{model.SessionId} • {H(statusBadge)}</div></div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">Cashier:</div><div class=\"val\">{H(model.CashierName)}</div></div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">Opened:</div><div class=\"val\">{H(openedAt)}</div></div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">Closed:</div><div class=\"val\">{H(closedAt)}</div></div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">Duration:</div><div class=\"val\">{H(model.Duration)}</div></div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">Printed:</div><div class=\"val\">{H(printedAt)}</div></div>");

        sb.AppendLine("<div class=\"sec-head\">1. Shift Sales &amp; Tender Summary</div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">Total Invoices:</div><div class=\"val\">{model.TotalInvoicesCount} (R:{model.RetailInvoicesCount} / W:{model.WholesaleInvoicesCount})</div></div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">Gross Shift Sales:</div><div class=\"val\">{F2(model.GrossSales)}</div></div>");
        if (model.DiscountTotal > 0) sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">Invoice Discounts:</div><div class=\"val\">-{F2(model.DiscountTotal)}</div></div>");
        if (model.TaxTotal > 0) sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">Tax Amount:</div><div class=\"val\">+{F2(model.TaxTotal)}</div></div>");
        sb.AppendLine($"<div class=\"solid-hr\"></div><div class=\"pair total-line\"><div class=\"lbl\">NET SHIFT SALES:</div><div class=\"val\">Rs. {F2(model.NetSales)}</div></div>");
        sb.AppendLine($"<div class=\"sub-pair\"><div>• Cash Sales (Paid):</div><div>{F2(model.CashTenderSales)}</div></div>");
        sb.AppendLine($"<div class=\"sub-pair\"><div>• Bank / Card / Online:</div><div>{F2(model.NonCashSales)}</div></div>");
        sb.AppendLine($"<div class=\"sub-pair\"><div>• Credit (Udhar) Sales:</div><div>{F2(model.CreditUdharSales)}</div></div>");

        sb.AppendLine("<div class=\"sec-head\">2. Cash Drawer Inflows (+)</div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">Opening Cash Float:</div><div class=\"val\">{F2(model.OpeningBalance)}</div></div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">(+) Cash Sales ({model.CashSalesCount}):</div><div class=\"val\">+{F2(model.CashSalesAmount)}</div></div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">(+) Customer Receipts ({model.CustomerPaymentsCount}):</div><div class=\"val\">+{F2(model.CustomerPaymentsAmount)}</div></div>");
        if (model.PurchaseReturnsCount > 0 || model.PurchaseReturnsAmount > 0)
        {
            sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">(+) Purchase Return Cash ({model.PurchaseReturnsCount}):</div><div class=\"val\">+{F2(model.PurchaseReturnsAmount)}</div></div>");
        }
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">(+) Manual Cash In ({model.ManualCashInCount}):</div><div class=\"val\">+{F2(model.ManualCashInAmount)}</div></div>");
        sb.AppendLine($"<div class=\"solid-hr\"></div><div class=\"pair total-line\"><div class=\"lbl\">TOTAL CASH INFLOW:</div><div class=\"val\">+Rs. {F2(model.TotalCashIn)}</div></div>");

        sb.AppendLine("<div class=\"sec-head\">3. Cash Drawer Outflows (-)</div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">(-) Cash Purchases ({model.CashPurchasesCount}):</div><div class=\"val\">-{F2(model.CashPurchasesAmount)}</div></div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">(-) Cash Sale Refunds ({model.CashReturnsCount}):</div><div class=\"val\">-{F2(model.CashReturnsAmount)}</div></div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">(-) Supplier Payments ({model.SupplierPaymentsCount}):</div><div class=\"val\">-{F2(model.SupplierPaymentsAmount)}</div></div>");
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">(-) Shop Expenses ({model.ExpensesCount}):</div><div class=\"val\">-{F2(model.ExpensesAmount)}</div></div>");
        if (model.StaffAdvancesCount > 0 || model.StaffAdvancesAmount > 0)
        {
            sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">(-) Staff Advances ({model.StaffAdvancesCount}):</div><div class=\"val\">-{F2(model.StaffAdvancesAmount)}</div></div>");
        }
        sb.AppendLine($"<div class=\"pair\"><div class=\"lbl\">(-) Manual Cash Out ({model.ManualCashOutCount}):</div><div class=\"val\">-{F2(model.ManualCashOutAmount)}</div></div>");
        sb.AppendLine($"<div class=\"solid-hr\"></div><div class=\"pair total-line\"><div class=\"lbl\">TOTAL CASH OUTFLOW:</div><div class=\"val\">-Rs. {F2(model.TotalCashOut)}</div></div>");

        sb.AppendLine("<div class=\"sec-head\">4. Cash Drawer Reconciliation</div>");
        sb.AppendLine($"<div class=\"grand-box\"><div class=\"pair total-line\"><div class=\"lbl\">EXPECTED CASH:</div><div class=\"val\">Rs. {F2(model.ExpectedBalance)}</div></div>");
        if (isClosed && model.ClosingBalanceActual.HasValue)
        {
            var diff = model.Difference ?? (model.ClosingBalanceActual.Value - model.ExpectedBalance);
            sb.AppendLine($"<div class=\"solid-hr\"></div><div class=\"pair total-line\"><div class=\"lbl\">ACTUAL COUNTED:</div><div class=\"val\">Rs. {F2(model.ClosingBalanceActual.Value)}</div></div>");
            sb.AppendLine($"<div class=\"solid-hr\"></div><div class=\"pair total-line\"><div class=\"lbl\">VARIANCE:</div><div class=\"val\">Rs. {F2(diff)}</div></div>");
        }
        sb.AppendLine("</div>");
        if (!string.IsNullOrWhiteSpace(model.Notes)) sb.AppendLine($"<div class=\"notes-box\"><strong>Shift Notes:</strong> {H(model.Notes)}</div>");
        sb.AppendLine($"<div class=\"sig-block\"><div class=\"sig-line\">Cashier Signature ({H(model.CashierName)})</div><div class=\"sig-line\">Manager / Supervisor Signature</div></div>");
        sb.AppendLine($"<div class=\"hr\"></div><div class=\"foot\">System Generated {(isClosed ? "Z-Report" : "X-Report")} • Shift #{model.SessionId}</div></div></div></body></html>");
        return sb.ToString();
    }

    /// <summary>
    /// Renders Customer or Supplier Ledger (`customer_ledger_a4`, `customer_ledger_thermal`, `supplier_ledger_a4`, `supplier_ledger_thermal`).
    /// </summary>
    public string RenderPartyLedger(
        string partyType,
        string paperSize,
        string partyName,
        List<PrintLedgerRowViewModel> rows,
        PrintLedgerSummaryViewModel summary,
        PrintSettingViewModel setting,
        string? from = null,
        string? to = null,
        IDictionary<string, string>? queryOverrides = null)
    {
        var isSupplier = string.Equals(partyType, "supplier", StringComparison.OrdinalIgnoreCase);
        var isThermal = string.Equals(paperSize, "thermal", StringComparison.OrdinalIgnoreCase);
        var partyTitle = isSupplier ? "Supplier" : "Customer";
        var range = $"{from ?? ""} — {to ?? ""}".Trim(' ', '—');
        var calibrationHtml = RenderCalibrationStyles(setting, queryOverrides, isThermal: isThermal);
        var logo = setting.PrintShowLogo ? setting.LogoUrl : null;

        if (isThermal)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>{partyTitle} Ledger (Thermal)</title>");
            sb.AppendLine("<style>body{font-family:ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;width:72mm;margin:0 auto;padding:6px 8px;font-size:11px;}h2{margin:0;font-size:14px;text-align:center;}.center{text-align:center;}.muted{color:#333;}.line{border-top:1px dashed #000;margin:6px 0;}table{width:100%;border-collapse:collapse;}th,td{padding:4px 0;border-bottom:1px dotted #000;vertical-align:top;}th{text-align:left;}.right{text-align:right;}.logo{text-align:center;margin-bottom:4px;}.logo img{max-width:48mm;max-height:18mm;object-fit:contain;}@page{margin:0;}@media print{.no-print{display:none;}body{margin:0;}}</style>");
            sb.AppendLine(calibrationHtml);
            sb.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\">");
            if (!string.IsNullOrEmpty(logo)) sb.AppendLine($"<div class=\"logo\"><img src=\"{H(logo)}\" alt=\"Logo\"></div>");
            sb.AppendLine($"<h2>{H(setting.StoreName)}</h2>");
            if (!string.IsNullOrEmpty(setting.PhoneNumber)) sb.AppendLine($"<div class=\"center muted\">{H(setting.PhoneNumber)}</div>");
            if (!string.IsNullOrEmpty(setting.LicenseNumber)) sb.AppendLine($"<div class=\"center muted\">License # {H(setting.LicenseNumber)}</div>");
            if (!string.IsNullOrEmpty(setting.Address)) sb.AppendLine($"<div class=\"center\">{H(setting.Address)}</div>");
            sb.AppendLine($"<div class=\"line\"></div><div><strong>{partyTitle}:</strong> {H(partyName)}</div>");
            if (!string.IsNullOrEmpty(range)) sb.AppendLine($"<div><strong>Range:</strong> {H(range)}</div>");
            sb.AppendLine("<div class=\"line\"></div><table><thead><tr><th style=\"width:16mm\">Date</th><th style=\"width:12mm\">Type</th><th>Ref</th><th class=\"right\" style=\"width:18mm\">Bal</th></tr></thead><tbody>");
            if (rows.Count == 0)
            {
                sb.AppendLine("<tr><td colspan=\"4\" class=\"right\">No entries.</td></tr>");
            }
            else
            {
                foreach (var r in rows)
                {
                    var refStr = !string.IsNullOrEmpty(r.PostedNumber) ? r.PostedNumber : (!string.IsNullOrEmpty(r.InvoiceNumber) ? r.InvoiceNumber : (r.PaymentRef ?? "—"));
                    var dateShort = r.EntryDate.Length >= 10 ? r.EntryDate[2..10] : r.EntryDate;
                    var typeShort = (r.EntryType ?? "INV").ToUpperInvariant();
                    if (typeShort.Length > 3) typeShort = typeShort[..3];
                    sb.AppendLine($"<tr><td>{H(dateShort)}</td><td>{H(typeShort)}</td><td>{H(refStr)}{(!string.IsNullOrWhiteSpace(r.Description) ? $"<div class=\"muted\">{H(r.Description)}</div>" : "")}</td><td class=\"right\">{Nf(r.RunningBalance)}</td></tr>");
                }
            }
            sb.AppendLine("</tbody></table><div class=\"line\"></div><table>");
            sb.AppendLine($"<tr><th>Total Inv.</th><td class=\"right\">{Nf(summary.TotalInvoiced)}</td></tr>");
            sb.AppendLine(isSupplier
                ? $"<tr><th>Paid on Inv.</th><td class=\"right\">{Nf(summary.PaidOnInvoice)}</td></tr><tr><th>Payments</th><td class=\"right\">{Nf(summary.PaymentsDebited)}</td></tr>"
                : $"<tr><th>Recv. on Inv.</th><td class=\"right\">{Nf(summary.ReceivedOnInvoice)}</td></tr><tr><th>Payments</th><td class=\"right\">{Nf(summary.PaymentsCredited)}</td></tr>");
            sb.AppendLine($"<tr><th>Net Balance</th><td class=\"right\">{Nf(summary.NetBalance)}</td></tr></table>");
            sb.AppendLine($"<div class=\"center muted\">Printed {DateTime.Now:yyyy-MM-dd HH:mm}</div></body></html>");
            return sb.ToString();
        }

        var sbA4 = new StringBuilder();
        sbA4.AppendLine($"<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>{partyTitle} Ledger (A4)</title>");
        sbA4.AppendLine("<style>*{box-sizing:border-box;}body{font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial,sans-serif;margin:20px;color:#000;}h1,h2,h3{margin:0;}.meta{margin-top:6px;font-size:12px;color:#333;}.row{display:flex;justify-content:space-between;gap:16px;}.brand{display:flex;align-items:center;gap:12px;}.brand img{height:48px;width:auto;object-fit:contain;}.mt-8{margin-top:8px;}.mt-12{margin-top:12px;}table{width:100%;border-collapse:collapse;font-size:12px;}th,td{border:1px solid #333;padding:6px 8px;vertical-align:top;}th{background:#f2f2f2;text-align:left;}.text-right{text-align:right;}.small{font-size:11px;color:#555;}@media print{body{margin:0;}.no-print{display:none!important;}th{background:#eee!important;-webkit-print-color-adjust:exact;print-color-adjust:exact;}}</style>");
        sbA4.AppendLine(calibrationHtml);
        sbA4.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\"><div class=\"row\"><div class=\"brand\">");
        if (!string.IsNullOrEmpty(logo)) sbA4.AppendLine($"<img src=\"{H(logo)}\" alt=\"Logo\">");
        sbA4.AppendLine($"<div><h2>{H(setting.StoreName)}</h2>{(!string.IsNullOrEmpty(setting.Address) ? $"<div class=\"small\">{H(setting.Address)}</div>" : "")}<div class=\"small\">{(!string.IsNullOrEmpty(setting.PhoneNumber) ? $"<span>Tel: {H(setting.PhoneNumber)}</span>" : "")}{(!string.IsNullOrEmpty(setting.LicenseNumber) ? $"<span style=\"margin-left:12px;\">License #: {H(setting.LicenseNumber)}</span>" : "")}</div></div></div><div class=\"text-right\"><h1>{partyTitle} Ledger</h1><div class=\"meta\">Printed: {DateTime.Now:yyyy-MM-dd HH:mm}</div></div></div>");
        sbA4.AppendLine($"<div class=\"mt-12\"><strong>{partyTitle}:</strong> {H(partyName)}{(!string.IsNullOrEmpty(range) ? $"&nbsp;&nbsp; <strong>Range:</strong> {H(range)}" : "")}</div>");
        sbA4.AppendLine($"<div class=\"mt-8\"><table><thead><tr><th style=\"width:90px\">Date</th><th style=\"width:80px\">Type</th><th>Posted #</th><th>Invoice #</th><th class=\"text-right\">Invoice Total</th><th class=\"text-right\">{(isSupplier ? "Paid on Invoice" : "Received on Invoice")}</th><th class=\"text-right\">{(isSupplier ? "Payment (Debit)" : "Received Payment")}</th><th>Payment Ref</th><th class=\"text-right\">{(isSupplier ? "Credit Remaining" : "Balance Remaining")}</th><th class=\"text-right\">Balance</th><th>Description</th></tr></thead><tbody>");
        if (!string.IsNullOrEmpty(from) || summary.OpeningBalance != 0)
        {
            sbA4.AppendLine($"<tr style=\"background:#f9fafb;font-weight:bold;\"><td>{H(from ?? "—")}</td><td>B/F</td><td colspan=\"7\">Opening Balance Brought Forward</td><td class=\"text-right\">{Nf(summary.OpeningBalance)}</td><td>Prior to {H(from ?? "period")}</td></tr>");
        }
        if (rows.Count == 0)
        {
            sbA4.AppendLine("<tr><td colspan=\"11\" class=\"text-right\">No entries.</td></tr>");
        }
        else
        {
            foreach (var r in rows)
            {
                var d10 = r.EntryDate.Length > 10 ? r.EntryDate[..10] : r.EntryDate;
                var colPaidOrRecv = isSupplier ? r.TotalPaid : r.TotalReceived;
                var colPay = isSupplier ? r.DebitedAmount : r.CreditedAmount;
                sbA4.AppendLine($"<tr><td>{H(d10)}</td><td>{H((r.EntryType ?? "").ToUpperInvariant())}</td><td>{H(r.PostedNumber)}</td><td>{H(r.InvoiceNumber)}</td><td class=\"text-right\">{Nf(r.InvoiceTotal)}</td><td class=\"text-right\">{Nf(colPaidOrRecv)}</td><td class=\"text-right\">{Nf(colPay)}</td><td>{H(r.PaymentRef)}</td><td class=\"text-right\">{Nf(r.CreditRemainingCalc)}</td><td class=\"text-right\">{Nf(r.RunningBalance)}</td><td>{H(r.Description)}</td></tr>");
            }
        }
        sbA4.AppendLine("</tbody></table></div><div class=\"mt-12\" style=\"max-width:500px;\"><table>");
        sbA4.AppendLine($"<tr><th>Total Invoiced</th><td class=\"text-right\">{Nf(summary.TotalInvoiced)}</td></tr>");
        sbA4.AppendLine(isSupplier
            ? $"<tr><th>Paid on Invoice</th><td class=\"text-right\">{Nf(summary.PaidOnInvoice)}</td></tr><tr><th>Payments (Debited)</th><td class=\"text-right\">{Nf(summary.PaymentsDebited)}</td></tr>"
            : $"<tr><th>Received on Invoice</th><td class=\"text-right\">{Nf(summary.ReceivedOnInvoice)}</td></tr><tr><th>Payments (Credited)</th><td class=\"text-right\">{Nf(summary.PaymentsCredited)}</td></tr>");
        sbA4.AppendLine($"<tr><th>Net Balance</th><td class=\"text-right\">{Nf(summary.NetBalance)}</td></tr></table></div></body></html>");
        return sbA4.ToString();
    }

    #region All 4 Preview Endpoints (/print/thermal-preview/{template}, /print/a4-preview/{template}, /print/sticker-preview/{template?}, /print/share-preview/{size}/{template})

    public static PrintInvoiceViewModel CreateSamplePreviewInvoice(PrintSettingViewModel? setting = null)
    {
        var inv = new PrintInvoiceViewModel
        {
            Id = 1,
            PostedNumber = "INV-001",
            Date = DateTime.Now,
            SaleType = "retail",
            InvoiceType = "credit",
            PaymentMethod = "cash",
            GrossAmount = 1990.00m,
            ItemDiscountTotal = 164.00m,
            DiscountPercentage = 5m,
            DiscountAmount = 91.30m,
            TaxAmount = 0m,
            Total = 1734.70m,
            TotalReceive = 1200.00m,
            CustomerTotalDue = 2484.70m,
            Remarks = "Please keep refrigerated (2°C – 8°C).",
            DoctorName = "Dr. Ahmed Khan",
            PatientName = "Ali Ahmed",
            UserName = "Admin User",
            CustomerName = "Allied Medical & Clinic",
            CustomerPhone = "0300-1234567",
            CustomerAddress = "Model Town, Lahore",
            Items =
            [
                new PrintInvoiceItemViewModel
                {
                    ProductName = "Augmentin 625mg Tablets",
                    Barcode = "8961102401042",
                    ProductCode = "SKU-1042",
                    PackSize = 6,
                    Quantity = 2,
                    Price = 480.00m,
                    ItemDiscountPercentage = 10,
                    ItemDiscountAmount = 96.00m,
                    SubTotal = 864.00m,
                    BatchNumber = "AUG26B",
                    Expiry = "08/2027",
                    DosageInstruction = "1 + 0 + 1 • کھانے کے بعد (After Meal)"
                },
                new PrintInvoiceItemViewModel
                {
                    ProductName = "Panadol Extra 500mg Strip",
                    Barcode = "8961102402018",
                    ProductCode = "SKU-2018",
                    PackSize = 10,
                    Quantity = 4,
                    Price = 65.00m,
                    ItemDiscountPercentage = 0,
                    ItemDiscountAmount = 0m,
                    SubTotal = 260.00m,
                    BatchNumber = "PND91A",
                    Expiry = "11/2027",
                    DosageInstruction = "1 چمچ صبح و شام (1 tsp Morning & Night)"
                },
                new PrintInvoiceItemViewModel
                {
                    ProductName = "Risek 40mg Capsules",
                    Barcode = "8961102404110",
                    ProductCode = "SKU-4110",
                    PackSize = 14,
                    Quantity = 2,
                    Price = 385.00m,
                    ItemDiscountPercentage = 8.83m,
                    ItemDiscountAmount = 68.00m,
                    SubTotal = 702.00m,
                    BatchNumber = "RSK77D",
                    Expiry = "01/2028",
                    DosageInstruction = "1 گولی نہار منہ (1 Cap Before Breakfast)"
                }
            ]
        };

        if (setting is { FbrEnabled: true })
        {
            var posId = string.IsNullOrWhiteSpace(setting.FbrPosId) ? "966130" : setting.FbrPosId;
            inv.FbrInvoiceNumber = $"{posId}{DateTime.Now:yyMMddHHmmss}01";
            inv.FbrQrData = $"https://ims.fbr.gov.pk/Lv1/Verification?inv={inv.FbrInvoiceNumber}";
            inv.FbrPosFee = setting.FbrPosFeeEnabled ? setting.FbrPosFeeAmount : 1.00m;
        }

        return inv;
    }

    public string RenderThermalPreview(
        string template,
        PrintSettingViewModel? dbSetting = null,
        IDictionary<string, string>? queryParams = null)
    {
        var normalized = (template ?? "").Trim().ToLowerInvariant();
        if (!ValidInvoiceTemplates.Contains(normalized))
        {
            throw new ArgumentException($"Template '{template}' not found.", nameof(template));
        }

        var setting = dbSetting ?? new PrintSettingViewModel();
        if (queryParams != null && queryParams.TryGetValue("fbr_preview", out var fbrPrev))
        {
            setting.FbrEnabled = fbrPrev is "1" or "true" or "yes";
        }
        if (queryParams != null && queryParams.TryGetValue("note", out var customNote))
        {
            setting.Note = customNote;
        }
        var sampleInvoice = CreateSamplePreviewInvoice(setting);
        return RenderThermalSaleInvoice(normalized, sampleInvoice, setting, queryParams);
    }

    public string RenderA4Preview(
        string template,
        PrintSettingViewModel? dbSetting = null,
        IDictionary<string, string>? queryParams = null)
    {
        var normalized = (template ?? "").Trim().ToLowerInvariant();
        if (!ValidInvoiceTemplates.Contains(normalized))
        {
            throw new ArgumentException($"Template '{template}' not found.", nameof(template));
        }

        var setting = dbSetting ?? new PrintSettingViewModel();
        if (queryParams != null && queryParams.TryGetValue("fbr_preview", out var fbrPrev))
        {
            setting.FbrEnabled = fbrPrev is "1" or "true" or "yes";
        }
        if (queryParams != null && queryParams.TryGetValue("note", out var customNote))
        {
            setting.Note = customNote;
        }
        var sampleInvoice = CreateSamplePreviewInvoice(setting);
        return RenderA4SaleInvoice(normalized, sampleInvoice, setting, queryParams);
    }

    public string RenderStickerPreview(
        string? template = "classic",
        PrintSettingViewModel? dbSetting = null,
        IDictionary<string, string>? queryParams = null)
    {
        var selectedTemplate = ValidStickerTemplates.Contains((template ?? "classic").ToLowerInvariant())
            ? (template ?? "classic").ToLowerInvariant()
            : "classic";

        var setting = dbSetting ?? new PrintSettingViewModel { StoreName = "Karobar Pharmacy" };
        var cfg = new PrintStickerConfigViewModel { Template = selectedTemplate };

        if (queryParams != null)
        {
            static bool ParseQBool(string? v, bool fallback) => string.IsNullOrWhiteSpace(v) ? fallback : v.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
            if (queryParams.TryGetValue("width_mm", out var wStr) && int.TryParse(wStr, out var wVal)) cfg.WidthMm = Math.Clamp(wVal, 25, 150);
            if (queryParams.TryGetValue("height_mm", out var hStr) && int.TryParse(hStr, out var hVal)) cfg.HeightMm = Math.Clamp(hVal, 15, 120);
            if (queryParams.TryGetValue("printer_mode", out var pm) && pm is "receipt_roll" or "label_printer") cfg.PrinterMode = pm;
            if (queryParams.TryGetValue("font_scale", out var fs)) cfg.FontScale = fs;
            if (queryParams.TryGetValue("contrast_mode", out var cm)) cfg.ContrastMode = cm;
            if (queryParams.TryGetValue("footer_note", out var fn)) cfg.FooterNote = fn;
            if (queryParams.TryGetValue("show_shop_name", out var ssn)) cfg.ShowShopName = ParseQBool(ssn, cfg.ShowShopName);
            if (queryParams.TryGetValue("show_invoice_meta", out var sim)) cfg.ShowInvoiceMeta = ParseQBool(sim, cfg.ShowInvoiceMeta);
            if (queryParams.TryGetValue("show_patient", out var sp)) cfg.ShowPatient = ParseQBool(sp, cfg.ShowPatient);
            if (queryParams.TryGetValue("show_quantity", out var sq)) cfg.ShowQuantity = ParseQBool(sq, cfg.ShowQuantity);
            if (queryParams.TryGetValue("show_batch", out var sbVal)) cfg.ShowBatch = ParseQBool(sbVal, cfg.ShowBatch);
            if (queryParams.TryGetValue("show_expiry", out var se)) cfg.ShowExpiry = ParseQBool(se, cfg.ShowExpiry);
            if (queryParams.TryGetValue("show_doctor", out var sd)) cfg.ShowDoctor = ParseQBool(sd, cfg.ShowDoctor);
            if (queryParams.TryGetValue("font_size_header", out var fsh) && double.TryParse(fsh, CultureInfo.InvariantCulture, out var fshV)) cfg.FontSizeHeader = fshV;
            if (queryParams.TryGetValue("font_size_patient", out var fsp) && double.TryParse(fsp, CultureInfo.InvariantCulture, out var fspV)) cfg.FontSizePatient = fspV;
            if (queryParams.TryGetValue("font_size_product", out var fspr) && double.TryParse(fspr, CultureInfo.InvariantCulture, out var fsprV)) cfg.FontSizeProduct = fsprV;
            if (queryParams.TryGetValue("font_size_dosage", out var fsd) && double.TryParse(fsd, CultureInfo.InvariantCulture, out var fsdV)) cfg.FontSizeDosage = fsdV;
            if (queryParams.TryGetValue("font_size_footer", out var fsf) && double.TryParse(fsf, CultureInfo.InvariantCulture, out var fsfV)) cfg.FontSizeFooter = fsfV;
            if (queryParams.TryGetValue("font_weight_header", out var fwh) && int.TryParse(fwh, out var fwhV)) cfg.FontWeightHeader = fwhV;
            if (queryParams.TryGetValue("font_weight_patient", out var fwp) && int.TryParse(fwp, out var fwpV)) cfg.FontWeightPatient = fwpV;
            if (queryParams.TryGetValue("font_weight_product", out var fwpr) && int.TryParse(fwpr, out var fwprV)) cfg.FontWeightProduct = fwprV;
            if (queryParams.TryGetValue("font_weight_dosage", out var fwd) && int.TryParse(fwd, out var fwdV)) cfg.FontWeightDosage = fwdV;
            if (queryParams.TryGetValue("font_weight_footer", out var fwf) && int.TryParse(fwf, out var fwfV)) cfg.FontWeightFooter = fwfV;
        }

        var sampleInvoice = CreateSamplePreviewInvoice(setting);
        sampleInvoice.PostedNumber = "SI-00042";
        sampleInvoice.PatientName = "Ahmed Khan";
        sampleInvoice.DoctorName = "Dr. Tariq Mahmood";

        return RenderDosageStickers(sampleInvoice, setting, cfg, queryParams);
    }

    /// <summary>
    /// Renders `/print/share-preview/{size}/{template}` using the exact unified colorful A4 / 80mm Thermal design
    /// matching QuestPDF shared PDF output 1-to-1.
    /// </summary>
    public string RenderSharePreview(
        string size,
        string template,
        PrintSettingViewModel? dbSetting = null,
        IDictionary<string, string>? queryParams = null)
    {
        var normSize = (size ?? "").Trim().ToLowerInvariant();
        var normTpl = (template ?? "").Trim().ToLowerInvariant();
        if (normSize is not ("a4" or "thermal") || !ValidInvoiceTemplates.Contains(normTpl))
        {
            throw new ArgumentException("Preview template not found.");
        }

        var setting = dbSetting ?? new PrintSettingViewModel();
        setting.SharePaperSize = normSize;
        if (queryParams != null && queryParams.TryGetValue("show_logo", out var sl))
        {
            setting.ShareShowLogo = sl is "1" or "true" or "yes";
        }
        if (queryParams != null && queryParams.TryGetValue("note", out var noteVal))
        {
            setting.Note = noteVal;
        }

        var sampleInvoice = CreateSamplePreviewInvoice(setting);
        sampleInvoice.PostedNumber = "INV-00104";
        sampleInvoice.CustomerName = "Muhammad Usman";

        return normSize == "thermal"
            ? RenderThermalSaleInvoice(normTpl, sampleInvoice, setting, queryParams, colorMode: true)
            : RenderA4SaleInvoice(normTpl, sampleInvoice, setting, queryParams, isSharePreview: true);
    }

    public static string GenerateQrSvg(string payload, int sizePx = 74)
    {
        try
        {
            var writer = new BarcodeWriterSvg
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Width = sizePx,
                    Height = sizePx,
                    Margin = 1,
                    ErrorCorrection = ErrorCorrectionLevel.M,
                    CharacterSet = "UTF-8"
                }
            };
            var svg = writer.Write(string.IsNullOrWhiteSpace(payload) ? "FBR" : payload);
            return svg.Content;
        }
        catch
        {
            return $"<svg width=\"{sizePx}\" height=\"{sizePx}\" xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"{sizePx}\" height=\"{sizePx}\" fill=\"#fff\" stroke=\"#000\"/></svg>";
        }
    }

    public static string GenerateCode128Svg(string payload, int widthPx = 160, int heightPx = 38)
    {
        try
        {
            var writer = new BarcodeWriterSvg
            {
                Format = BarcodeFormat.CODE_128,
                Options = new EncodingOptions
                {
                    Width = widthPx,
                    Height = heightPx,
                    Margin = 2,
                    PureBarcode = true
                }
            };
            var svg = writer.Write(string.IsNullOrWhiteSpace(payload) ? "0000" : payload);
            return svg.Content;
        }
        catch
        {
            return $"<svg width=\"{widthPx}\" height=\"{heightPx}\" xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"{widthPx}\" height=\"{heightPx}\" fill=\"#fff\"/></svg>";
        }
    }

    #endregion
}

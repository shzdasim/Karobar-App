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

    /// <summary>
    /// Renders any of the 7 Thermal Sale Invoice views (`base` / `standard` / `minimal` / `detailed` / `compact` / `bold` / `barcode`).
    /// </summary>
    public string RenderThermalSaleInvoice(
        string template,
        PrintInvoiceViewModel invoice,
        PrintSettingViewModel setting,
        IDictionary<string, string>? queryOverrides = null)
    {
        var normalized = (template ?? "standard").Trim().ToLowerInvariant();
        var gross = invoice.GrossAmount > 0 ? invoice.GrossAmount : invoice.Items.Sum(i => i.SubTotal);
        var disc = invoice.DiscountAmount;
        var tax = invoice.TaxAmount;
        var total = invoice.Total > 0 ? invoice.Total : (gross - disc + tax);
        var totalReceive = invoice.TotalReceive;
        var remainThis = Math.Max(total - totalReceive, 0m);
        var customerTotalDue = invoice.CustomerTotalDue;
        var showBalance = remainThis > 0 || (customerTotalDue.HasValue && customerTotalDue.Value > 0);
        var showTotalDue = customerTotalDue.HasValue && customerTotalDue.Value > 0;
        var footerNote = !string.IsNullOrWhiteSpace(invoice.FooterNote) ? invoice.FooterNote.Trim() : (setting.Note ?? "").Trim();
        var calibrationHtml = RenderCalibrationStyles(setting, queryOverrides, isThermal: true);
        var fbrFooterHtml = RenderFbrFiscalFooter(invoice, setting);
        var invoiceType = (invoice.InvoiceType ?? "debit").ToUpperInvariant();
        var itemCount = invoice.Items.Count;
        var logo = setting.PrintShowLogo ? setting.LogoUrl : null;

        if (normalized == "minimal")
        {
            var dateStr = invoice.Date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
            sb.AppendLine($"<title>Sale Invoice (Minimal Thermal) - {H(invoice.PostedNumber)}</title>");
            sb.AppendLine("<style>@page{margin:0;}*{box-sizing:border-box;}html,body{margin:0;padding:0;color:#000;background:#fff;}body{font-family:'Courier New',monospace;font-weight:400;font-size:10px;line-height:1.2;-webkit-print-color-adjust:exact;print-color-adjust:exact;}.print-actions{margin:5px;text-align:right;}.print-actions button{padding:4px 8px;cursor:pointer;font-size:10px;}@media print{.print-actions{display:none;}.receipt{margin:0;}}.receipt{width:58mm;max-width:100%;margin:0 auto;padding:0 4px 4px;}.center{text-align:center;}.right{text-align:right;}.hr{border-top:1px solid #000;margin:3px 0;}.pair{display:flex;justify-content:space-between;}.pair+.pair{margin-top:1px;}.type-line{display:flex;justify-content:space-between;align-items:center;font-weight:bold;}.type-tag{border:1px solid #000;padding:1px 5px;}.meta{margin-top:3px;padding:3px;border:1px solid #000;}.meta-label{font-weight:bold;}.remarks{margin-top:3px;padding:3px;border:1px dotted #000;white-space:pre-wrap;overflow-wrap:anywhere;}table{width:100%;border-collapse:collapse;}tbody td{padding:0;border-bottom:1px dotted #000;vertical-align:top;}th.right,td.right{text-align:right;white-space:nowrap;}td.center{text-align:center;}.item-name{width:50%;}.item-qty{width:15%;text-align:center;}.item-price{width:35%;text-align:right;}.totals .pair{margin-top:2px;}.totals .total{font-weight:bold;font-size:12px;border-top:1px solid #000;padding-top:2px;}.note{margin-top:4px;white-space:pre-wrap;font-size:8px;}.foot{margin-top:6px;text-align:center;font-size:8px;}</style>");
            sb.AppendLine(calibrationHtml);
            sb.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\">");
            sb.AppendLine("<div class=\"print-actions\"><button onclick=\"if(window.karobarDesktop&&window.karobarDesktop.silentPrint){window.karobarDesktop.silentPrint();}else{window.print();}\">Print</button></div>");
            sb.AppendLine("<div class=\"receipt\">");
            if (!string.IsNullOrEmpty(logo)) sb.AppendLine($"<div class=\"center\" style=\"margin-bottom:3px;\"><img src=\"{H(logo)}\" alt=\"Logo\" style=\"max-height:36px;max-width:45mm;object-fit:contain;\"/></div>");
            sb.AppendLine($"<div class=\"center\" style=\"font-size:16px;font-weight:bold;\">{H(setting.StoreName)}</div>");
            if (!string.IsNullOrEmpty(setting.PhoneNumber)) sb.AppendLine($"<div class=\"center\">{H(setting.PhoneNumber)}</div>");
            sb.AppendLine("<div class=\"hr\"></div>");
            sb.AppendLine($"<div class=\"type-line\"><span>SALE INVOICE</span><span class=\"type-tag\">{H(invoiceType)}</span></div>");
            sb.AppendLine("<div class=\"meta\">");
            sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Inv#</span><span>{H(invoice.PostedNumber)}</span></div>");
            sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Date</span><span>{H(dateStr)}</span></div>");
            sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Cust</span><span>{H(string.IsNullOrWhiteSpace(invoice.CustomerName) ? "Walk-in" : invoice.CustomerName)}</span></div>");
            if (!string.IsNullOrEmpty(invoice.DoctorName)) sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Dr</span><span>{H(invoice.DoctorName)}</span></div>");
            if (!string.IsNullOrEmpty(invoice.PatientName)) sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Pt</span><span>{H(invoice.PatientName)}</span></div>");
            sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Items</span><span>{itemCount}</span></div>");
            sb.AppendLine("</div>");
            if (!string.IsNullOrWhiteSpace(invoice.Remarks)) sb.AppendLine($"<div class=\"remarks\"><strong>Remarks:</strong> {H(invoice.Remarks)}</div>");
            sb.AppendLine("<div class=\"hr\"></div><table><tbody>");
            foreach (var it in invoice.Items)
            {
                sb.AppendLine($"<tr><td class=\"item-name\">{H(it.ProductName)}</td><td class=\"item-qty\">{Nf(it.Quantity)}x</td><td class=\"item-price\">{F2(it.SubTotal)}</td></tr>");
            }
            sb.AppendLine("</tbody></table><div class=\"totals\"><div class=\"hr\"></div>");
            sb.AppendLine($"<div class=\"pair\"><span>Total</span><span>{F2(total)}</span></div>");
            if (remainThis > 0)
            {
                sb.AppendLine($"<div class=\"pair\"><span>Paid</span><span>{F2(totalReceive)}</span></div>");
                sb.AppendLine($"<div class=\"pair total\"><span>Due</span><span>{F2(remainThis)}</span></div>");
            }
            if (showTotalDue)
            {
                sb.AppendLine($"<div class=\"pair total\" style=\"font-size:11px;\"><span>Total Due</span><span>{F2(customerTotalDue!.Value)}</span></div>");
            }
            sb.AppendLine("</div>");
            if (!string.IsNullOrEmpty(footerNote)) sb.AppendLine($"<div class=\"hr\"></div><div class=\"note\">{H(footerNote)}</div>");
            sb.AppendLine(fbrFooterHtml);
            sb.AppendLine("<div class=\"foot\">Software by Asim Shahzad<br/>PH:0304-7674787</div></div></body></html>");
            return sb.ToString();
        }

        if (normalized == "detailed")
        {
            var dateStr = invoice.Date?.ToString("dd MMM yyyy hh:mm tt", CultureInfo.InvariantCulture) ?? "";
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
            sb.AppendLine($"<title>Sale Invoice (Detailed Thermal) - {H(invoice.PostedNumber)}</title>");
            sb.AppendLine("<style>@page{margin:0;}*{box-sizing:border-box;}html,body{margin:0;padding:0;color:#000;background:#fff;}body{font-family:'Courier New',monospace;font-weight:600;font-size:11px;line-height:1.4;-webkit-print-color-adjust:exact;print-color-adjust:exact;}.print-actions{margin:8px;text-align:right;}.print-actions button{padding:6px 10px;cursor:pointer;}@media print{.print-actions{display:none;}.receipt{margin:0;}}.receipt{width:80mm;max-width:100%;margin:0 auto;padding:0 6px 8px;}.center{text-align:center;}.right{text-align:right;}.logo{max-width:60mm;max-height:35mm;object-fit:contain;display:block;margin:0 auto 4px;}.hr{border-top:1px dashed #000;margin:6px 0;}.double-hr{border-top:2px double #000;margin:6px 0;}.section{margin-bottom:8px;}.pair{display:flex;justify-content:space-between;}.pair+.pair{margin-top:2px;}.invoice-title{display:flex;align-items:center;justify-content:space-between;border:2px solid #000;padding:5px;font-size:12px;font-weight:bold;}.type-tag{border:1px solid #000;padding:2px 6px;letter-spacing:.5px;}.section-label{font-weight:bold;}.remarks{padding:5px;border:1px solid #000;margin-top:5px;white-space:pre-wrap;overflow-wrap:anywhere;}.small-text{font-size:9px;}table{width:100%;border-collapse:collapse;}thead th{text-align:left;padding:3px 2px;border-bottom:2px solid #000;font-size:9px;}tbody td{padding:2px 0;border-bottom:1px dotted #000;vertical-align:top;}th.right,td.right{text-align:right;white-space:nowrap;}td.center{text-align:center;}.totals{margin-top:8px;}.totals .pair{margin-top:3px;}.totals .pair.total{font-size:14px;font-weight:bold;border-top:1px solid #000;padding-top:4px;margin-top:6px;}.balance-section{background:#f5f5f5;padding:6px;margin-top:8px;border-radius:4px;}.note{margin-top:8px;white-space:pre-wrap;font-size:9px;padding:6px;background:#f9f9f9;border-radius:4px;}.foot{margin-top:10px;text-align:center;font-size:8px;padding-top:6px;border-top:1px dotted #000;}</style>");
            sb.AppendLine(calibrationHtml);
            sb.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\">");
            sb.AppendLine("<div class=\"print-actions\"><button onclick=\"if(window.karobarDesktop&&window.karobarDesktop.silentPrint){window.karobarDesktop.silentPrint();}else{window.print();}\">Print</button></div>");
            sb.AppendLine("<div class=\"receipt\">");
            if (!string.IsNullOrEmpty(logo)) sb.AppendLine($"<img src=\"{H(logo)}\" alt=\"Logo\" class=\"logo\">");
            sb.AppendLine($"<div class=\"center\" style=\"font-size:22px;font-weight:bold;\">{H(setting.StoreName)}</div>");
            if (!string.IsNullOrEmpty(setting.Address)) sb.AppendLine($"<div class=\"center small-text\">{H(setting.Address)}</div>");
            if (!string.IsNullOrEmpty(setting.PhoneNumber)) sb.AppendLine($"<div class=\"center small-text\">Ph: {H(setting.PhoneNumber)}</div>");
            sb.AppendLine("<div class=\"hr\"></div>");
            sb.AppendLine($"<div class=\"invoice-title\"><span>SALE INVOICE</span><span class=\"type-tag\">{H(invoiceType)}</span></div>");
            sb.AppendLine("<div class=\"section\">");
            sb.AppendLine($"<div class=\"pair\"><span class=\"section-label\">Invoice #</span><span>{H(invoice.PostedNumber)}</span></div>");
            sb.AppendLine($"<div class=\"pair\"><span class=\"section-label\">Date</span><span>{H(dateStr)}</span></div>");
            sb.AppendLine($"<div class=\"pair\"><span class=\"section-label\">Customer</span><span>{H(string.IsNullOrWhiteSpace(invoice.CustomerName) ? "Walk-in" : invoice.CustomerName)}</span></div>");
            if (!string.IsNullOrEmpty(invoice.CustomerPhone)) sb.AppendLine($"<div class=\"pair\"><span>Phone</span><span>{H(invoice.CustomerPhone)}</span></div>");
            if (!string.IsNullOrEmpty(invoice.DoctorName)) sb.AppendLine($"<div class=\"pair\"><span>Doctor</span><span>{H(invoice.DoctorName)}</span></div>");
            if (!string.IsNullOrEmpty(invoice.PatientName)) sb.AppendLine($"<div class=\"pair\"><span>Patient</span><span>{H(invoice.PatientName)}</span></div>");
            if (!string.IsNullOrEmpty(invoice.UserName)) sb.AppendLine($"<div class=\"pair\"><span>Cashier</span><span>{H(invoice.UserName)}</span></div>");
            sb.AppendLine($"<div class=\"pair\"><span class=\"section-label\">Items</span><span>{itemCount}</span></div>");
            sb.AppendLine("</div>");
            if (!string.IsNullOrWhiteSpace(invoice.Remarks)) sb.AppendLine($"<div class=\"remarks\"><strong>Remarks:</strong> {H(invoice.Remarks)}</div>");
            sb.AppendLine("<div class=\"double-hr\"></div><table><thead><tr><th>ITEM</th><th class=\"center\">QTY</th><th class=\"right\">RATE</th><th class=\"right\">AMT</th></tr></thead><tbody>");
            foreach (var it in invoice.Items)
            {
                sb.AppendLine($"<tr><td>{H(it.ProductName)}</td><td class=\"center\">{Nf(it.Quantity)}</td><td class=\"right\">{F2(it.Price)}</td><td class=\"right\">{F2(it.SubTotal)}</td></tr>");
            }
            sb.AppendLine("</tbody></table><div class=\"totals\"><div class=\"hr\"></div>");
            sb.AppendLine($"<div class=\"pair\"><span>Sub Total</span><span>{F2(gross)}</span></div>");
            sb.AppendLine($"<div class=\"pair\"><span>Discount</span><span>-{F2(disc)}</span></div>");
            sb.AppendLine($"<div class=\"pair\"><span>Tax</span><span>{F2(tax)}</span></div>");
            sb.AppendLine($"<div class=\"pair total\"><span>TOTAL</span><span>{F2(total)}</span></div></div>");
            if (showBalance)
            {
                sb.AppendLine("<div class=\"balance-section\">");
                sb.AppendLine($"<div class=\"pair\"><span>Payment</span><span>{F2(totalReceive)}</span></div>");
                if (remainThis > 0) sb.AppendLine($"<div class=\"pair\"><span>Balance</span><span>{F2(remainThis)}</span></div>");
                if (showTotalDue) sb.AppendLine($"<div class=\"pair total\"><span>Total Due</span><span>{F2(customerTotalDue!.Value)}</span></div>");
                sb.AppendLine("</div>");
            }
            if (!string.IsNullOrEmpty(footerNote)) sb.AppendLine($"<div class=\"note\">{H(footerNote)}</div>");
            sb.AppendLine(fbrFooterHtml);
            sb.AppendLine("<div class=\"foot\">Software by Asim Shahzad<br/>PH:0304-7674787</div></div></body></html>");
            return sb.ToString();
        }

        if (normalized == "compact")
        {
            var dateStr = invoice.Date?.ToString("dd-MMM-yy", CultureInfo.InvariantCulture) ?? "";
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
            sb.AppendLine($"<title>Sale Invoice (Compact Thermal) - {H(invoice.PostedNumber)}</title>");
            sb.AppendLine("<style>@page{margin:0;}*{box-sizing:border-box;}html,body{margin:0;padding:0;color:#000;background:#fff;}body{font-family:'Courier New',monospace;font-weight:400;font-size:8px;line-height:1.1;-webkit-print-color-adjust:exact;print-color-adjust:exact;}.print-actions{margin:4px;text-align:right;}.print-actions button{padding:3px 6px;cursor:pointer;font-size:9px;}@media print{.print-actions{display:none;}.receipt{margin:0;}}.receipt{width:58mm;max-width:100%;margin:0 auto;padding:0 3px 3px;}.center{text-align:center;}.right{text-align:right;}.logo{max-width:50mm;max-height:20mm;object-fit:contain;display:block;margin:0 auto 2px;}.hr{border-top:1px solid #000;margin:3px 0;}.dotted-hr{border-top:1px dotted #000;margin:3px 0;}.pair{display:flex;justify-content:space-between;}.pair+.pair{margin-top:1px;}.type-line{display:flex;justify-content:space-between;font-weight:bold;}.type-tag{border:1px solid #000;padding:0 3px;}.meta{display:grid;grid-template-columns:1fr 1fr;gap:1px 4px;padding:2px;border:1px solid #000;}.remarks{margin-top:2px;padding:2px;border:1px dotted #000;white-space:pre-wrap;overflow-wrap:anywhere;}table{width:100%;border-collapse:collapse;font-size:7px;}tbody td{padding:0;border-bottom:1px dotted #000;vertical-align:top;}.col-name{width:48%;}.col-qty{width:12%;text-align:center;}.col-price{width:40%;text-align:right;}.totals{margin-top:4px;}.totals .pair{margin-top:1px;}.totals .total{font-weight:bold;font-size:10px;border-top:1px solid #000;padding-top:2px;margin-top:3px;}.note{margin-top:4px;white-space:pre-wrap;font-size:7px;}.foot{margin-top:6px;text-align:center;font-size:7px;}</style>");
            sb.AppendLine(calibrationHtml);
            sb.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\">");
            sb.AppendLine("<div class=\"print-actions\"><button onclick=\"window.print()\">Print</button></div><div class=\"receipt\">");
            if (!string.IsNullOrEmpty(logo)) sb.AppendLine($"<img src=\"{H(logo)}\" alt=\"Logo\" class=\"logo\">");
            sb.AppendLine($"<div class=\"center\" style=\"font-size:14px;font-weight:bold;\">{H(setting.StoreName)}</div>");
            if (!string.IsNullOrEmpty(setting.Address)) sb.AppendLine($"<div class=\"center\" style=\"font-size:7px\">{H(setting.Address)}</div>");
            if (!string.IsNullOrEmpty(setting.PhoneNumber)) sb.AppendLine($"<div class=\"center\" style=\"font-size:7px\">{H(setting.PhoneNumber)}</div>");
            sb.AppendLine($"<div class=\"hr\"></div><div class=\"type-line\"><span>SALE #{H(invoice.PostedNumber)}</span><span class=\"type-tag\">{H(invoiceType)}</span></div>");
            sb.AppendLine($"<div class=\"meta\"><span>Date</span><span class=\"right\">{H(dateStr)}</span><span>Customer</span><span class=\"right\">{H(string.IsNullOrWhiteSpace(invoice.CustomerName) ? "Walk-in" : invoice.CustomerName)}</span>");
            if (!string.IsNullOrEmpty(invoice.DoctorName)) sb.AppendLine($"<span>Doctor</span><span class=\"right\">{H(invoice.DoctorName)}</span>");
            if (!string.IsNullOrEmpty(invoice.PatientName)) sb.AppendLine($"<span>Patient</span><span class=\"right\">{H(invoice.PatientName)}</span>");
            sb.AppendLine($"<span>Items</span><span class=\"right\">{itemCount}</span></div>");
            if (!string.IsNullOrWhiteSpace(invoice.Remarks)) sb.AppendLine($"<div class=\"remarks\"><strong>Note:</strong> {H(invoice.Remarks)}</div>");
            sb.AppendLine("<div class=\"dotted-hr\"></div><table><tbody>");
            foreach (var it in invoice.Items)
            {
                sb.AppendLine($"<tr><td class=\"col-name\">{H(it.ProductName)}</td><td class=\"col-qty\">{Nf(it.Quantity)}</td><td class=\"col-price\">{F2(it.SubTotal)}</td></tr>");
            }
            sb.AppendLine($"</tbody></table><div class=\"totals\"><div class=\"hr\"></div><div class=\"pair\"><span>Gross</span><span>{F2(gross)}</span></div><div class=\"pair\"><span>Disc</span><span>-{F2(disc)}</span></div>{(tax > 0 ? $"<div class=\"pair\"><span>Tax</span><span>{F2(tax)}</span></div>" : "")}<div class=\"pair total\"><span>TOTAL</span><span>{F2(total)}</span></div>");
            if (remainThis > 0) sb.AppendLine($"<div class=\"pair\"><span>Paid</span><span>{F2(totalReceive)}</span></div><div class=\"pair\"><span>Due</span><span>{F2(remainThis)}</span></div>");
            if (showTotalDue) sb.AppendLine($"<div class=\"pair total\"><span>TOTAL DUE</span><span>{F2(customerTotalDue!.Value)}</span></div>");
            sb.AppendLine("</div>");
            if (!string.IsNullOrEmpty(footerNote)) sb.AppendLine($"<div class=\"hr\"></div><div class=\"note\">{H(footerNote)}</div>");
            sb.AppendLine(fbrFooterHtml);
            sb.AppendLine("<div class=\"foot\">Software by Asim Shahzad<br/>PH:0304-7674787</div></div></body></html>");
            return sb.ToString();
        }

        if (normalized == "bold")
        {
            var dateStr = invoice.Date?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? "";
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
            sb.AppendLine($"<title>Sale Invoice (Bold Thermal) - {H(invoice.PostedNumber)}</title>");
            sb.AppendLine("<style>@page{margin:0;}*{box-sizing:border-box;}html,body{margin:0;padding:0;color:#000;background:#fff;}body{font-family:'Arial Black','Helvetica Neue',sans-serif;font-weight:900;font-size:14px;line-height:1.2;-webkit-print-color-adjust:exact;print-color-adjust:exact;}.print-actions{margin:10px;text-align:right;}.print-actions button{padding:8px 12px;cursor:pointer;font-weight:bold;}@media print{.print-actions{display:none;}.receipt{margin:0;}}.receipt{width:80mm;max-width:100%;margin:0 auto;padding:0 8px 10px;}.center{text-align:center;}.right{text-align:right;}.logo{max-width:65mm;max-height:40mm;object-fit:contain;display:block;margin:0 auto 6px;}.hr{border-top:3px double #000;margin:8px 0;}.solid-hr{border-top:3px solid #000;margin:8px 0;}.pair{display:flex;justify-content:space-between;font-weight:bold;}.pair+.pair{margin-top:4px;}.invoice-banner{display:flex;align-items:center;justify-content:space-between;padding:6px;border:3px solid #000;font-size:16px;}.type-tag{padding:2px 5px;border:2px solid #000;font-size:11px;}.meta{margin-top:6px;padding:6px;border:2px solid #000;}.meta-label{text-transform:uppercase;}.remarks{margin-top:6px;padding:6px;border:2px solid #000;white-space:pre-wrap;overflow-wrap:anywhere;}table{width:100%;border-collapse:collapse;}thead th{text-align:left;padding:4px 3px;border-bottom:3px solid #000;font-size:12px;text-transform:uppercase;}tbody td{padding:4px 0;border-bottom:2px solid #000;vertical-align:top;font-weight:bold;}th.right,td.right{text-align:right;white-space:nowrap;}td.center{text-align:center;}.totals{margin-top:10px;background:#000;color:#fff;padding:8px;}.totals .pair{margin-top:4px;}.totals .pair.total{font-size:18px;border-top:3px solid #fff;padding-top:6px;margin-top:8px;}.note{margin-top:10px;padding:8px;background:#ffff00;color:#000;font-weight:bold;white-space:pre-wrap;}.foot{margin-top:12px;text-align:center;font-size:10px;text-transform:uppercase;}</style>");
            sb.AppendLine(calibrationHtml);
            sb.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\">");
            sb.AppendLine("<div class=\"print-actions\"><button onclick=\"if(window.karobarDesktop&&window.karobarDesktop.silentPrint){window.karobarDesktop.silentPrint();}else{window.print();}\">PRINT</button></div><div class=\"receipt\">");
            if (!string.IsNullOrEmpty(logo)) sb.AppendLine($"<img src=\"{H(logo)}\" alt=\"Logo\" class=\"logo\">");
            sb.AppendLine($"<div class=\"center\" style=\"font-size:28px;letter-spacing:2px;\">{H(setting.StoreName)}</div>");
            if (!string.IsNullOrEmpty(setting.Address)) sb.AppendLine($"<div class=\"center\" style=\"font-size:12px;\">{H(setting.Address)}</div>");
            if (!string.IsNullOrEmpty(setting.PhoneNumber)) sb.AppendLine($"<div class=\"center\" style=\"font-size:12px;\">PH: {H(setting.PhoneNumber)}</div>");
            sb.AppendLine($"<div class=\"hr\"></div><div class=\"invoice-banner\"><span>SALE INVOICE #{H(invoice.PostedNumber)}</span><span class=\"type-tag\">{H(invoiceType)}</span></div>");
            sb.AppendLine("<div class=\"meta\">");
            sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Date</span><span>{H(dateStr)}</span></div>");
            sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Customer</span><span>{H(string.IsNullOrWhiteSpace(invoice.CustomerName) ? "Walk-in" : invoice.CustomerName)}</span></div>");
            if (!string.IsNullOrEmpty(invoice.DoctorName)) sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Doctor</span><span>{H(invoice.DoctorName)}</span></div>");
            if (!string.IsNullOrEmpty(invoice.PatientName)) sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Patient</span><span>{H(invoice.PatientName)}</span></div>");
            if (!string.IsNullOrEmpty(invoice.UserName)) sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Cashier</span><span>{H(invoice.UserName)}</span></div>");
            sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Items</span><span>{itemCount}</span></div></div>");
            if (!string.IsNullOrWhiteSpace(invoice.Remarks)) sb.AppendLine($"<div class=\"remarks\"><strong>REMARKS:</strong> {H(invoice.Remarks)}</div>");
            sb.AppendLine("<div class=\"solid-hr\"></div><table><thead><tr><th>ITEM</th><th class=\"center\">QTY</th><th class=\"right\">PRICE</th><th class=\"right\">TOTAL</th></tr></thead><tbody>");
            foreach (var it in invoice.Items)
            {
                sb.AppendLine($"<tr><td>{H(it.ProductName)}</td><td class=\"center\">{Nf(it.Quantity)}</td><td class=\"right\">{F2(it.Price)}</td><td class=\"right\">{F2(it.SubTotal)}</td></tr>");
            }
            sb.AppendLine($"</tbody></table><div class=\"totals\"><div class=\"pair\"><span>SUBTOTAL</span><span>{F2(gross)}</span></div><div class=\"pair\"><span>DISCOUNT</span><span>-{F2(disc)}</span></div><div class=\"pair\"><span>TAX</span><span>{F2(tax)}</span></div><div class=\"pair total\"><span>TOTAL</span><span>{F2(total)}</span></div>");
            if (remainThis > 0) sb.AppendLine($"<div class=\"pair\" style=\"margin-top:8px;border-top:2px solid #fff;padding-top:4px;\"><span>PAID</span><span>{F2(totalReceive)}</span></div><div class=\"pair total\"><span>DUE</span><span>{F2(remainThis)}</span></div>");
            if (showTotalDue) sb.AppendLine($"<div class=\"pair total\" style=\"border-top:3px solid #fff;padding-top:6px;margin-top:8px;\"><span>TOTAL DUE</span><span>{F2(customerTotalDue!.Value)}</span></div>");
            sb.AppendLine("</div>");
            if (!string.IsNullOrEmpty(footerNote)) sb.AppendLine($"<div class=\"note\">NOTE: {H(footerNote)}</div>");
            sb.AppendLine(fbrFooterHtml);
            sb.AppendLine("<div class=\"foot\">Software by Asim Shahzad<br/>PH:0304-7674787</div></div></body></html>");
            return sb.ToString();
        }

        if (normalized == "barcode")
        {
            var dateStr = invoice.Date?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "";
            var timeStr = invoice.Date?.ToString("hh:mm tt", CultureInfo.InvariantCulture) ?? "";
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
            sb.AppendLine($"<title>Sale Invoice (Barcode Thermal) - {H(invoice.PostedNumber)}</title>");
            sb.AppendLine("<style>@page{margin:0;}*{box-sizing:border-box;}html,body{margin:0;padding:0;color:#000;background:#fff;}body{font-family:'Verdana',sans-serif;font-weight:500;font-size:10px;line-height:1.3;-webkit-print-color-adjust:exact;print-color-adjust:exact;}.print-actions{margin:8px;text-align:right;}.print-actions button{padding:6px 10px;cursor:pointer;}@media print{.print-actions{display:none;}.receipt{margin:0;}}.receipt{width:80mm;max-width:100%;margin:0 auto;padding:0 6px 8px;}.center{text-align:center;}.right{text-align:right;}.logo{max-width:55mm;max-height:28mm;object-fit:contain;display:block;margin:0 auto 4px;}.hr{border-top:1px dashed #333;margin:5px 0;}.double-hr{border-top:2px double #333;margin:5px 0;}.section{margin-bottom:6px;}.pair{display:flex;justify-content:space-between;}.pair+.pair{margin-top:2px;}.type-tag{display:inline-block;border:1px solid #333;padding:2px 6px;font-weight:bold;letter-spacing:.5px;}.meta{padding:5px;border:1px solid #333;background:#f6f6f6;}.meta-label{font-weight:bold;}.remarks{margin-top:5px;padding:5px;border-left:3px solid #333;background:#f6f6f6;white-space:pre-wrap;overflow-wrap:anywhere;}.barcode-text{font-family:'Courier New',monospace;letter-spacing:1px;font-size:14px;font-weight:bold;}table{width:100%;border-collapse:collapse;}thead th{text-align:left;padding:3px 2px;border-bottom:2px solid #333;font-size:8px;text-transform:uppercase;background:#f0f0f0;}tbody td{padding:3px 0;border-bottom:1px dotted #999;vertical-align:top;}th.right,td.right{text-align:right;white-space:nowrap;}td.center{text-align:center;}.item-barcode{font-family:'Courier New',monospace;font-size:8px;color:#666;}.totals{margin-top:8px;}.totals .pair{margin-top:3px;}.totals .total{font-size:16px;font-weight:bold;border-top:2px solid #333;padding-top:6px;margin-top:8px;}.payment-info{background:#e8f4fd;padding:6px;margin-top:8px;border-radius:4px;border:1px solid #b8d4e8;}.note{margin-top:8px;white-space:pre-wrap;font-size:9px;padding:6px;background:#fffde7;border-radius:4px;border-left:4px solid #ffc107;}.qr-section{margin:8px 0;text-align:center;}.foot{margin-top:10px;text-align:center;font-size:8px;color:#666;border-top:1px dotted #999;padding-top:6px;}</style>");
            sb.AppendLine(calibrationHtml);
            sb.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\">");
            sb.AppendLine("<div class=\"print-actions\"><button onclick=\"window.print()\">Print</button></div><div class=\"receipt\">");
            if (!string.IsNullOrEmpty(logo)) sb.AppendLine($"<img src=\"{H(logo)}\" alt=\"Logo\" class=\"logo\">");
            sb.AppendLine($"<div class=\"center\" style=\"font-size:20px;font-weight:bold;\">{H(setting.StoreName)}</div>");
            if (!string.IsNullOrEmpty(setting.Address)) sb.AppendLine($"<div class=\"center\" style=\"font-size:9px;\">{H(setting.Address)}</div>");
            if (!string.IsNullOrEmpty(setting.PhoneNumber)) sb.AppendLine($"<div class=\"center\" style=\"font-size:9px;\">Ph: {H(setting.PhoneNumber)}</div>");
            sb.AppendLine($"<div class=\"hr\"></div><div class=\"center barcode-text\">{H(invoice.PostedNumber)}</div><div class=\"center\"><span class=\"type-tag\">{H(invoiceType)} SALE</span></div>");
            sb.AppendLine($"<div class=\"section meta\"><div class=\"pair\"><span class=\"meta-label\">Date:</span><span>{H(dateStr)} {H(timeStr)}</span></div><div class=\"pair\"><span class=\"meta-label\">Customer:</span><span>{H(string.IsNullOrWhiteSpace(invoice.CustomerName) ? "Walk-in" : invoice.CustomerName)}</span></div>");
            if (!string.IsNullOrEmpty(invoice.DoctorName)) sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Doctor:</span><span>{H(invoice.DoctorName)}</span></div>");
            if (!string.IsNullOrEmpty(invoice.PatientName)) sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Patient:</span><span>{H(invoice.PatientName)}</span></div>");
            if (!string.IsNullOrEmpty(invoice.UserName)) sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Cashier:</span><span>{H(invoice.UserName)}</span></div>");
            sb.AppendLine($"<div class=\"pair\"><span class=\"meta-label\">Items:</span><span>{itemCount}</span></div></div>");
            if (!string.IsNullOrWhiteSpace(invoice.Remarks)) sb.AppendLine($"<div class=\"remarks\"><strong>Remarks:</strong> {H(invoice.Remarks)}</div>");
            sb.AppendLine("<div class=\"double-hr\"></div><table><thead><tr><th>Item</th><th class=\"center\">Qty</th><th class=\"right\">Rate</th><th class=\"right\">Amount</th></tr></thead><tbody>");
            foreach (var it in invoice.Items)
            {
                sb.AppendLine("<tr><td>");
                sb.AppendLine($"<div>{H(it.ProductName)}</div>");
                if (!string.IsNullOrEmpty(it.Barcode)) sb.AppendLine($"<div class=\"item-barcode\">{H(it.Barcode)}</div>");
                sb.AppendLine($"</td><td class=\"center\">{Nf(it.Quantity)}</td><td class=\"right\">{F2(it.Price)}</td><td class=\"right\">{F2(it.SubTotal)}</td></tr>");
            }
            sb.AppendLine($"</tbody></table><div class=\"totals\"><div class=\"hr\"></div><div class=\"pair\"><span>Subtotal:</span><span>{F2(gross)}</span></div><div class=\"pair\"><span>Discount:</span><span>-{F2(disc)}</span></div><div class=\"pair\"><span>Tax:</span><span>{F2(tax)}</span></div><div class=\"pair total\"><span>TOTAL</span><span>{F2(total)}</span></div></div>");
            if (remainThis > 0)
            {
                sb.AppendLine($"<div class=\"payment-info\"><div class=\"pair\"><span>Paid:</span><span>{F2(totalReceive)}</span></div><div class=\"pair total\"><span>Balance Due:</span><span>{F2(remainThis)}</span></div></div>");
            }
            else
            {
                sb.AppendLine("<div class=\"payment-info center\" style=\"font-weight:bold;color:green;\">PAID IN FULL</div>");
            }
            if (showTotalDue)
            {
                sb.AppendLine($"<div class=\"payment-info\" style=\"background:#ffe0b2;border-color:#ff9800;\"><div class=\"pair total\"><span>TOTAL DUE:</span><span>{F2(customerTotalDue!.Value)}</span></div></div>");
            }
            var barSvg = GenerateCode128Svg(string.IsNullOrWhiteSpace(invoice.PostedNumber) ? "INV-0001" : invoice.PostedNumber, 160, 38);
            sb.AppendLine($"<div class=\"qr-section\">{barSvg}<div style=\"font-size:7px;margin-top:2px;\">{H(invoice.PostedNumber)}</div></div>");
            if (!string.IsNullOrEmpty(footerNote)) sb.AppendLine($"<div class=\"note\">{H(footerNote)}</div>");
            sb.AppendLine(fbrFooterHtml);
            sb.AppendLine("<div class=\"foot\">Software by Asim Shahzad<br/>PH:0304-7674787</div></div></body></html>");
            return sb.ToString();
        }

        // Standard / Base thermal template (`sale_invoice_thermal_standard` & `sale_invoice_thermal`)
        var isBaseThermal = normalized is "base" or "thermal";
        var stdDate = invoice.Date?.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "";
        var sbStd = new StringBuilder();
        sbStd.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
        sbStd.AppendLine($"<title>Sale Invoice ({(isBaseThermal ? "Thermal" : "Standard Thermal")}) - {H(invoice.PostedNumber)}</title>");
        sbStd.AppendLine("<style>@page{margin:0;}*{box-sizing:border-box;}html,body{margin:0;padding:0;color:#000;background:#fff;}body{font-family:'Courier New',monospace;font-weight:700;font-size:12px;line-height:1.3;-webkit-print-color-adjust:exact;print-color-adjust:exact;}.print-actions{margin:8px;text-align:right;}.print-actions button{padding:6px 10px;cursor:pointer;}@media print{.print-actions{display:none;}.receipt{margin:0;}}.receipt{width:78mm;max-width:100%;margin:0 auto;padding:0 6px 6px;position:relative;overflow:hidden;}.center{text-align:center;}.right{text-align:right;}.logo{max-width:56mm;max-height:30mm;object-fit:contain;display:block;margin:0 auto 4px;}.hr{border-top:1px dashed #000;margin:5px 0;}.double-hr{border-top:2px double #000;margin:5px 0;}.pair{display:flex;justify-content:space-between;}.pair+.pair{margin-top:2px;}.wm{position:absolute;top:2mm;left:0;right:0;z-index:0;text-align:center;pointer-events:none;user-select:none;}.wm img{width:58mm;max-width:90%;opacity:0.15;filter:grayscale(100%) contrast(90%);}.content{position:relative;z-index:1;}table{width:100%;border-collapse:collapse;}thead th{text-align:left;padding:2px 2px;border:1px solid #000;}tbody td{padding:0;border-bottom:1px solid #000;vertical-align:top;}th.right,td.right{text-align:right;white-space:nowrap;}td.center{text-align:center;}th.col-name{width:45%;}th.col-qty{width:13%;}th.col-price{width:17%;}th.col-disc{width:10%;}th.col-subtotal{width:15%;}.totals .pair.total{font-size:13px;font-weight:bold;margin-top:4px;}.note{margin-top:5px;white-space:pre-wrap;font-size:10px;}.foot{margin-top:8px;text-align:center;font-size:9px;}</style>");
        sbStd.AppendLine(calibrationHtml);
        sbStd.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\">");
        sbStd.AppendLine("<div class=\"print-actions\"><button onclick=\"window.print()\">Print</button></div><div class=\"receipt\">");
        if (!string.IsNullOrEmpty(logo)) sbStd.AppendLine($"<div class=\"wm\"><img src=\"{H(logo)}\" alt=\"Watermark\"></div>");
        sbStd.AppendLine("<div class=\"content\">");
        sbStd.AppendLine($"<div class=\"center\" style=\"font-size:{(isBaseThermal ? "29px" : "24px")}\">{H(setting.StoreName)}</div>");
        if (!string.IsNullOrEmpty(setting.Address)) sbStd.AppendLine($"<div class=\"center\" style=\"font-size:10px\">{H(setting.Address)}</div>");
        if (!string.IsNullOrEmpty(setting.PhoneNumber)) sbStd.AppendLine($"<div class=\"center\" style=\"font-size:10px\">Ph: {H(setting.PhoneNumber)}</div>");
        sbStd.AppendLine("<div class=\"hr\"></div>");
        sbStd.AppendLine($"<div class=\"pair\"><div>Invoice</div><div># {H(invoice.PostedNumber)}</div></div>");
        sbStd.AppendLine($"<div class=\"pair\"><div>Date</div><div>{H(stdDate)}</div></div>");
        if (!string.IsNullOrEmpty(invoice.CustomerName)) sbStd.AppendLine($"<div class=\"pair\"><div>Customer</div><div>{H(invoice.CustomerName)}</div></div>");
        if (!isBaseThermal && !string.IsNullOrEmpty(invoice.DoctorName)) sbStd.AppendLine($"<div class=\"pair\"><div>Doctor</div><div>{H(invoice.DoctorName)}</div></div>");
        if (!isBaseThermal && !string.IsNullOrEmpty(invoice.PatientName)) sbStd.AppendLine($"<div class=\"pair\"><div>Patient</div><div>{H(invoice.PatientName)}</div></div>");
        if (!string.IsNullOrEmpty(invoice.UserName)) sbStd.AppendLine($"<div class=\"pair\"><div>User</div><div>{H(invoice.UserName)}</div></div>");
        sbStd.AppendLine("<div class=\"hr\"></div>");
        sbStd.AppendLine($"<table><thead><tr><th class=\"col-name\">Name</th><th class=\"right col-qty\">Qty</th><th class=\"right col-price\">Price</th><th class=\"right col-disc\">{(isBaseThermal ? "Dis%" : "Dis")}</th><th class=\"right col-subtotal\">Subt.</th></tr></thead><tbody>");
        foreach (var it in invoice.Items)
        {
            sbStd.AppendLine($"<tr><td style=\"font-size:10px\">{H(it.ProductName)}</td><td class=\"center\">{Nf(it.Quantity)}</td><td class=\"right\">{F2(it.Price)}</td><td class=\"center\">{F0(it.ItemDiscountPercentage)}</td><td class=\"right\">{F2(it.SubTotal)}</td></tr>");
        }
        sbStd.AppendLine($"</tbody></table><div class=\"totals\"><div class=\"hr\"></div><div class=\"pair\"><div>Gross</div><div>{F2(gross)}</div></div><div class=\"pair\"><div>Discount</div><div>{F2(disc)}</div></div><div class=\"pair\"><div>Tax</div><div>{F2(tax)}</div></div><div class=\"pair total\"><div>{(isBaseThermal ? "Total" : "TOTAL")}</div><div>{F2(total)}</div></div>");
        if (showBalance)
        {
            sbStd.AppendLine($"<div class=\"hr\"></div><div class=\"pair\"><div>Paid</div><div>{F2(totalReceive)}</div></div>");
            if (remainThis > 0) sbStd.AppendLine($"<div class=\"pair\"><div>Balance</div><div>{F2(remainThis)}</div></div>");
            if (showTotalDue) sbStd.AppendLine($"<div class=\"pair total\"><div>Total Due</div><div>{F2(customerTotalDue!.Value)}</div></div>");
        }
        sbStd.AppendLine("</div>");
        if (!string.IsNullOrEmpty(footerNote)) sbStd.AppendLine($"<div class=\"hr\"></div><div class=\"note\">{H(footerNote)}</div>");
        sbStd.AppendLine(fbrFooterHtml);
        sbStd.AppendLine($"<div class=\"foot\">Software by {(isBaseThermal ? "Engr.Asim Shahzad" : "Asim Shahzad")}<br/>PH:0304-7674787</div></div></div></body></html>");
        return sbStd.ToString();
    }

    /// <summary>
    /// Renders any of the 6 A4 Sale Invoice views (`standard`, `minimal`, `detailed`, `compact`, `bold`, `barcode`).
    /// </summary>
    public string RenderA4SaleInvoice(
        string template,
        PrintInvoiceViewModel invoice,
        PrintSettingViewModel setting,
        IDictionary<string, string>? queryOverrides = null)
    {
        var normalized = (template ?? "standard").Trim().ToLowerInvariant();
        var gross = invoice.GrossAmount > 0 ? invoice.GrossAmount : invoice.Items.Sum(i => i.SubTotal);
        var disc = invoice.DiscountAmount;
        var tax = invoice.TaxAmount;
        var total = invoice.Total > 0 ? invoice.Total : (gross - disc + tax);
        var totalReceive = invoice.TotalReceive;
        var remainThis = Math.Max(total - totalReceive, 0m);
        var customerTotalDue = invoice.CustomerTotalDue;
        var showBalance = remainThis > 0 || (customerTotalDue.HasValue && customerTotalDue.Value > 0);
        var showTotalDue = customerTotalDue.HasValue && customerTotalDue.Value > 0;
        var footerNote = !string.IsNullOrWhiteSpace(invoice.FooterNote) ? invoice.FooterNote.Trim() : (setting.Note ?? "").Trim();
        var calibrationHtml = RenderCalibrationStyles(setting, queryOverrides, isThermal: false);
        var fbrFooterHtml = RenderFbrFiscalFooter(invoice, setting);
        var logo = setting.PrintShowLogo ? setting.LogoUrl : null;
        var dateStr = invoice.Date?.ToString(normalized == "compact" ? "dd/MM/yyyy" : "dd MMM yyyy", CultureInfo.InvariantCulture) ?? "";
        var timeStr = invoice.Date?.ToString("hh:mm tt", CultureInfo.InvariantCulture) ?? "";
        var isWholesale = string.Equals(invoice.SaleType, "wholesale", StringComparison.OrdinalIgnoreCase);

        if (normalized == "minimal")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>Sale Invoice (Minimal A4) - {H(invoice.PostedNumber)}</title>");
            sb.AppendLine("<style>:root{--text:#111;--muted:#666;--border:#ccc;}*{box-sizing:border-box;}html,body{margin:0;padding:0;font-family:Arial,Helvetica,sans-serif;color:var(--text);}.page{width:210mm;min-height:297mm;padding:15mm;margin:0 auto;}.print-actions{margin:12px auto;width:210mm;text-align:right;}.print-actions button{padding:8px 12px;cursor:pointer;}@media print{.print-actions{display:none;}.page{padding:10mm;}}.header{text-align:center;border-bottom:2px solid var(--border);padding-bottom:10px;margin-bottom:15px;}.header h1{margin:0 0 5px 0;font-size:22px;}.header .meta{font-size:11px;color:var(--muted);}.info-grid{display:grid;grid-template-columns:1fr 1fr;gap:20px;margin-bottom:15px;font-size:11px;}.info-box{border:1px solid var(--border);padding:8px;border-radius:4px;}.info-box .label{color:var(--muted);font-size:10px;text-transform:uppercase;}.info-box .value{font-weight:bold;font-size:13px;}table{width:100%;border-collapse:collapse;font-size:11px;}thead th{background:#f5f5f5;text-align:left;padding:8px 6px;border-bottom:1px solid var(--border);font-size:10px;}tbody td{padding:6px;border-bottom:1px dashed #e0e0e0;}.right{text-align:right;}.center{text-align:center;}.totals{margin-top:15px;float:right;width:40%;}.totals .row{display:flex;justify-content:space-between;padding:8px 10px;border-bottom:1px solid var(--border);}.totals .row.total{font-weight:bold;font-size:14px;background:#f9f9f9;}.totals .row.grand{font-weight:bold;font-size:16px;background:#333;color:#fff;}.clear{clear:both;}.footer{margin-top:20px;padding-top:10px;border-top:1px solid var(--border);font-size:10px;color:var(--muted);}.thankyou{text-align:center;margin-top:15px;font-size:12px;font-style:italic;}</style>");
            sb.AppendLine(calibrationHtml);
            sb.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\"><div class=\"print-actions\"><button onclick=\"window.print()\">Print</button></div><div class=\"page\"><div class=\"header\">");
            if (!string.IsNullOrEmpty(logo)) sb.AppendLine($"<div style=\"margin-bottom:6px;\"><img src=\"{H(logo)}\" alt=\"Logo\" style=\"max-height:48px;max-width:150px;object-fit:contain;\"/></div>");
            sb.AppendLine($"<h1>{H(setting.StoreName)}</h1><div class=\"meta\">{(!string.IsNullOrEmpty(setting.LicenseNumber) ? $"License: {H(setting.LicenseNumber)} | " : "")}{(!string.IsNullOrEmpty(setting.PhoneNumber) ? $"Ph: {H(setting.PhoneNumber)} | " : "")}{H(setting.Address)}</div></div>");
            sb.AppendLine($"<div class=\"info-grid\"><div class=\"info-box\"><div class=\"label\">Invoice #</div><div class=\"value\">{H(invoice.PostedNumber)}</div><div class=\"label\" style=\"margin-top:5px;\">Date</div><div class=\"value\">{H(dateStr)}</div></div><div class=\"info-box\"><div class=\"label\">Customer</div><div class=\"value\">{H(invoice.CustomerName ?? "N/A")}</div><div class=\"label\" style=\"margin-top:5px;\">User</div><div class=\"value\">{H(invoice.UserName)}</div></div></div>");
            sb.AppendLine("<table><thead><tr><th>Product</th><th class=\"center\">Qty</th><th class=\"right\">Price</th><th class=\"right\">Disc</th><th class=\"right\">Total</th></tr></thead><tbody>");
            foreach (var it in invoice.Items)
            {
                sb.AppendLine($"<tr><td>{H(it.ProductName)}</td><td class=\"center\">{F2(it.Quantity)}</td><td class=\"right\">{F2(it.Price)}</td><td class=\"right\">{F1(it.ItemDiscountPercentage)}%</td><td class=\"right\">{F2(it.SubTotal)}</td></tr>");
            }
            sb.AppendLine($"</tbody></table><div class=\"totals\"><div class=\"row\"><span>Gross</span><span>{F2(gross)}</span></div><div class=\"row\"><span>Discount</span><span>{F2(disc)}</span></div><div class=\"row\"><span>Tax</span><span>{F2(tax)}</span></div><div class=\"row total\"><span>Total</span><span>{F2(total)}</span></div>");
            if (remainThis > 0) sb.AppendLine($"<div class=\"row\"><span>Paid</span><span>{F2(totalReceive)}</span></div><div class=\"row total\"><span>Due</span><span>{F2(remainThis)}</span></div>");
            if (showTotalDue) sb.AppendLine($"<div class=\"row grand\"><span>Total Due</span><span>{F2(customerTotalDue!.Value)}</span></div>");
            sb.AppendLine("</div><div class=\"clear\"></div>");
            sb.AppendLine(fbrFooterHtml);
            sb.AppendLine($"<div class=\"footer\">{(!string.IsNullOrEmpty(footerNote) ? $"<div style=\"margin-bottom:10px;\">{H(footerNote)}</div>" : "")}<div class=\"thankyou\">Thank you for your business!</div></div></div></body></html>");
            return sb.ToString();
        }

        if (normalized == "detailed")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>Sale Invoice (Detailed A4) - {H(invoice.PostedNumber)}</title>");
            sb.AppendLine("<style>:root{--text:#111;--muted:#666;--border:#ccc;--primary:#2563eb;}*{box-sizing:border-box;}html,body{margin:0;padding:0;font-family:'Segoe UI',Arial,sans-serif;color:var(--text);}.page{width:210mm;min-height:297mm;padding:18mm 15mm;margin:0 auto;}.print-actions{margin:12px auto;width:210mm;text-align:right;}.print-actions button{padding:8px 16px;cursor:pointer;background:var(--primary);color:#fff;border:none;border-radius:4px;}@media print{.print-actions{display:none;}.page{padding:12mm;}}.header{display:flex;justify-content:space-between;align-items:flex-start;border-bottom:3px solid var(--primary);padding-bottom:15px;margin-bottom:20px;}.store-info h1{margin:0;font-size:24px;color:var(--primary);}.store-info .details{margin-top:8px;font-size:11px;color:var(--muted);line-height:1.6;}.invoice-info{text-align:right;}.invoice-info .invoice-num{font-size:20px;font-weight:bold;}.invoice-info .date{color:var(--muted);margin-top:4px;}.meta-grid{display:grid;grid-template-columns:repeat(3,1fr);gap:15px;margin-bottom:20px;}.meta-box{background:#f8fafc;padding:12px;border-radius:6px;border-left:3px solid var(--primary);}.meta-box .label{font-size:10px;text-transform:uppercase;color:var(--muted);margin-bottom:4px;}.meta-box .value{font-weight:600;font-size:13px;}table{width:100%;border-collapse:collapse;margin-top:10px;font-size:11px;}thead th{background:var(--primary);color:#fff;text-align:left;padding:12px 10px;font-size:10px;text-transform:uppercase;}tbody td{padding:10px;border-bottom:1px solid #e2e8f0;}.right{text-align:right;}.center{text-align:center;}.summary-section{margin-top:25px;display:flex;justify-content:flex-end;}.summary-box{width:45%;border:1px solid var(--border);border-radius:8px;overflow:hidden;}.summary-box .row{display:flex;justify-content:space-between;padding:12px 15px;border-bottom:1px solid #e2e8f0;}.summary-box .row.subtotal{background:#f8fafc;font-weight:600;}.summary-box .row.total{background:var(--primary);color:#fff;font-weight:bold;font-size:16px;}.summary-box .row.due{background:#fef3c7;font-weight:bold;font-size:14px;}.summary-box .row.grand{background:#1f2937;color:#fff;font-weight:bold;font-size:18px;}.footer{margin-top:auto;padding-top:15px;border-top:1px solid var(--border);}.footer-note{background:#fffbeb;padding:10px;border-radius:4px;border-left:3px solid #f59e0b;font-size:11px;}.thankyou{text-align:center;margin-top:15px;color:var(--muted);font-style:italic;}.powered{text-align:center;margin-top:8px;font-size:9px;color:#9ca3af;}</style>");
            sb.AppendLine(calibrationHtml);
            sb.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\"><div class=\"print-actions\"><button onclick=\"window.print()\">Print Invoice</button></div><div class=\"page\"><div class=\"header\"><div class=\"store-info\">");
            if (!string.IsNullOrEmpty(logo)) sb.AppendLine($"<div style=\"margin-bottom:6px;\"><img src=\"{H(logo)}\" alt=\"Logo\" style=\"max-height:52px;max-width:160px;object-fit:contain;\"/></div>");
            sb.AppendLine($"<h1>{H(setting.StoreName)}</h1><div class=\"details\">{(!string.IsNullOrEmpty(setting.LicenseNumber) ? $"<div>License: {H(setting.LicenseNumber)}</div>" : "")}{(!string.IsNullOrEmpty(setting.Address) ? $"<div>{H(setting.Address)}</div>" : "")}{(!string.IsNullOrEmpty(setting.PhoneNumber) ? $"<div>Phone: {H(setting.PhoneNumber)}</div>" : "")}</div></div><div class=\"invoice-info\"><div class=\"invoice-num\"># {H(invoice.PostedNumber)}</div><div class=\"date\">{H(dateStr)}</div></div></div>");
            sb.AppendLine($"<div class=\"meta-grid\"><div class=\"meta-box\"><div class=\"label\">Customer</div><div class=\"value\">{H(invoice.CustomerName ?? "N/A")}</div></div><div class=\"meta-box\"><div class=\"label\">User / Cashier</div><div class=\"value\">{H(invoice.UserName)}</div></div><div class=\"meta-box\"><div class=\"label\">Doctor</div><div class=\"value\">{H(invoice.DoctorName ?? "N/A")}</div></div></div>");
            sb.AppendLine("<table><thead><tr><th style=\"width:30%\">Product Name</th><th style=\"width:8%\">Pack</th><th style=\"width:12%\">Batch</th><th style=\"width:8%\">Expiry</th><th class=\"right\" style=\"width:10%\">Qty</th><th class=\"right\" style=\"width:10%\">Price</th><th class=\"right\" style=\"width:8%\">Disc</th><th class=\"right\" style=\"width:14%\">Subtotal</th></tr></thead><tbody>");
            foreach (var it in invoice.Items)
            {
                sb.AppendLine($"<tr><td>{H(it.ProductName)}</td><td>{it.PackSize}</td><td>{H(it.BatchNumber ?? "-")}</td><td>{H(it.Expiry ?? "-")}</td><td class=\"right\">{F2(it.Quantity)}</td><td class=\"right\">{F2(it.Price)}</td><td class=\"right\">{F1(it.ItemDiscountPercentage)}%</td><td class=\"right\">{F2(it.SubTotal)}</td></tr>");
            }
            sb.AppendLine($"</tbody></table><div class=\"summary-section\"><div class=\"summary-box\"><div class=\"row subtotal\"><span>Gross Amount</span><span>{F2(gross)}</span></div><div class=\"row\"><span>Discount</span><span>-{F2(disc)}</span></div><div class=\"row\"><span>Tax</span><span>{F2(tax)}</span></div><div class=\"row total\"><span>TOTAL</span><span>{F2(total)}</span></div>");
            if (remainThis > 0) sb.AppendLine($"<div class=\"row\"><span>Amount Paid</span><span>{F2(totalReceive)}</span></div><div class=\"row due\"><span>Balance Due</span><span>{F2(remainThis)}</span></div>");
            if (showTotalDue) sb.AppendLine($"<div class=\"row grand\"><span>Total Due</span><span>{F2(customerTotalDue!.Value)}</span></div>");
            sb.AppendLine("</div></div>");
            sb.AppendLine(fbrFooterHtml);
            sb.AppendLine($"<div class=\"footer\">{(!string.IsNullOrEmpty(footerNote) ? $"<div class=\"footer-note\">{H(footerNote)}</div>" : "")}<div class=\"thankyou\">Thank you for your patronage!</div><div class=\"powered\">Software by Asim Shahzad | PH: 0304-7674787</div></div></div></body></html>");
            return sb.ToString();
        }

        if (normalized == "compact")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>Sale Invoice (Compact A4) - {H(invoice.PostedNumber)}</title>");
            sb.AppendLine("<style>:root{--text:#111;--muted:#666;--border:#ccc;}*{box-sizing:border-box;}html,body{margin:0;padding:0;font-family:Arial,Helvetica,sans-serif;color:var(--text);font-size:11px;}.page{width:210mm;min-height:297mm;padding:12mm;margin:0 auto;}.print-actions{margin:8px auto;width:210mm;text-align:right;}.print-actions button{padding:6px 10px;cursor:pointer;}@media print{.print-actions{display:none;}.page{padding:8mm;}}.header{display:flex;justify-content:space-between;align-items:center;border-bottom:1px solid var(--border);padding-bottom:8px;margin-bottom:12px;}.store h2{margin:0;font-size:16px;}.store .meta{font-size:9px;color:var(--muted);margin-top:2px;}.invoice-num{font-size:14px;font-weight:bold;}.info-bar{display:flex;justify-content:space-between;background:#f5f5f5;padding:6px 10px;margin-bottom:10px;font-size:10px;}.info-bar span{color:var(--muted);}table{width:100%;border-collapse:collapse;font-size:10px;}thead th{border-bottom:1px solid var(--border);padding:5px 4px;text-align:left;font-size:9px;}tbody td{padding:4px;border-bottom:1px dotted #ddd;}.right{text-align:right;}.totals{margin-top:10px;width:35%;margin-left:auto;}.totals .row{display:flex;justify-content:space-between;padding:4px 8px;border-bottom:1px solid #eee;}.totals .row.total{font-weight:bold;font-size:12px;background:#333;color:#fff;}.totals .row.due{font-weight:bold;}.totals .row.grand{font-weight:bold;font-size:14px;background:#dc2626;color:#fff;}.footer{margin-top:15px;padding-top:8px;border-top:1px solid var(--border);font-size:9px;color:var(--muted);text-align:center;}</style>");
            sb.AppendLine(calibrationHtml);
            sb.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\"><div class=\"print-actions\"><button onclick=\"window.print()\">Print</button></div><div class=\"page\"><div class=\"header\"><div class=\"store\">");
            if (!string.IsNullOrEmpty(logo)) sb.AppendLine($"<div style=\"margin-bottom:4px;\"><img src=\"{H(logo)}\" alt=\"Logo\" style=\"max-height:36px;max-width:120px;object-fit:contain;\"/></div>");
            sb.AppendLine($"<h2>{H(setting.StoreName)}</h2><div class=\"meta\">{H(setting.LicenseNumber)} | {H(setting.PhoneNumber)} | {H(setting.Address)}</div></div><div class=\"invoice-num\">#{H(invoice.PostedNumber)}</div></div>");
            sb.AppendLine($"<div class=\"info-bar\"><div><span>Date:</span> {H(dateStr)}</div><div><span>Customer:</span> {H(invoice.CustomerName ?? "N/A")}</div><div><span>User:</span> {H(invoice.UserName)}</div></div>");
            sb.AppendLine("<table><thead><tr><th>Product</th><th class=\"right\">Qty</th><th class=\"right\">Price</th><th class=\"right\">Disc</th><th class=\"right\">Total</th></tr></thead><tbody>");
            foreach (var it in invoice.Items)
            {
                sb.AppendLine($"<tr><td>{H(it.ProductName)}</td><td class=\"right\">{F2(it.Quantity)}</td><td class=\"right\">{F2(it.Price)}</td><td class=\"right\">{F1(it.ItemDiscountPercentage)}%</td><td class=\"right\">{F2(it.SubTotal)}</td></tr>");
            }
            sb.AppendLine($"</tbody></table><div class=\"totals\"><div class=\"row\"><span>Gross:</span><span>{F2(gross)}</span></div><div class=\"row\"><span>Disc:</span><span>-{F2(disc)}</span></div><div class=\"row\"><span>Tax:</span><span>{F2(tax)}</span></div><div class=\"row total\"><span>TOTAL:</span><span>{F2(total)}</span></div>");
            if (remainThis > 0) sb.AppendLine($"<div class=\"row\"><span>Paid:</span><span>{F2(totalReceive)}</span></div><div class=\"row due\"><span>Due:</span><span>{F2(remainThis)}</span></div>");
            if (showTotalDue) sb.AppendLine($"<div class=\"row grand\"><span>TOTAL DUE:</span><span>{F2(customerTotalDue!.Value)}</span></div>");
            sb.AppendLine("</div>");
            sb.AppendLine(fbrFooterHtml);
            sb.AppendLine($"<div class=\"footer\">{H(footerNote)}<br/>Thank you!</div></div></body></html>");
            return sb.ToString();
        }

        if (normalized == "bold")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>Sale Invoice (Bold A4) - {H(invoice.PostedNumber)}</title>");
            sb.AppendLine("<style>:root{--text:#000;--muted:#444;--border:#000;}*{box-sizing:border-box;}html,body{margin:0;padding:0;font-family:'Arial Black','Helvetica Neue',sans-serif;color:var(--text);}.page{width:210mm;min-height:297mm;padding:20mm 15mm;margin:0 auto;}.print-actions{margin:15px auto;width:210mm;text-align:right;}.print-actions button{padding:10px 20px;cursor:pointer;font-weight:bold;}@media print{.print-actions{display:none;}.page{padding:15mm;}}.header{border-bottom:4px solid #000;padding-bottom:15px;margin-bottom:20px;text-align:center;}.header h1{margin:0;font-size:32px;letter-spacing:3px;text-transform:uppercase;}.header .meta{margin-top:10px;font-size:12px;font-weight:normal;}.info-row{display:flex;justify-content:space-between;margin-bottom:20px;border:2px solid #000;padding:15px;}.info-col{text-align:center;flex:1;}.info-col .label{font-size:10px;text-transform:uppercase;color:var(--muted);margin-bottom:5px;}.info-col .value{font-size:16px;font-weight:900;}table{width:100%;border-collapse:collapse;margin-top:15px;font-size:12px;border:2px solid #000;}thead th{background:#000;color:#fff;text-align:left;padding:12px 10px;font-size:11px;text-transform:uppercase;border-bottom:2px solid #000;}tbody td{padding:10px;border-bottom:1px solid #ccc;font-weight:bold;}tbody tr:nth-child(even){background:#f9f9f9;}.right{text-align:right;}.center{text-align:center;}.totals-section{margin-top:25px;border:3px double #000;padding:20px;}.totals-section .row{display:flex;justify-content:space-between;padding:10px 0;font-size:14px;font-weight:bold;}.totals-section .row+.row{border-top:1px solid #ccc;}.totals-section .row.total{font-size:22px;border-top:2px solid #000;padding-top:15px;margin-top:10px;}.totals-section .row.due{font-size:18px;color:#dc2626;}.totals-section .row.grand{font-size:24px;background:#000;color:#fff;padding:15px;margin-top:10px;}.footer{margin-top:30px;text-align:center;font-size:11px;font-weight:normal;}.footer-note{background:#ffff00;padding:10px;border:1px solid #000;margin-bottom:10px;font-weight:bold;}.thankyou{font-size:14px;font-style:italic;margin-top:15px;}</style>");
            sb.AppendLine(calibrationHtml);
            sb.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\"><div class=\"print-actions\"><button onclick=\"window.print()\">PRINT</button></div><div class=\"page\"><div class=\"header\">");
            if (!string.IsNullOrEmpty(logo)) sb.AppendLine($"<div style=\"margin-bottom:8px;\"><img src=\"{H(logo)}\" alt=\"Logo\" style=\"max-height:54px;max-width:160px;object-fit:contain;\"/></div>");
            sb.AppendLine($"<h1>{H(setting.StoreName)}</h1><div class=\"meta\">{H(setting.LicenseNumber)} | {H(setting.PhoneNumber)} | {H(setting.Address)}</div></div>");
            sb.AppendLine($"<div class=\"info-row\"><div class=\"info-col\"><div class=\"label\">Invoice No</div><div class=\"value\">{H(invoice.PostedNumber)}</div></div><div class=\"info-col\"><div class=\"label\">Date</div><div class=\"value\">{H(dateStr)}</div></div><div class=\"info-col\"><div class=\"label\">Customer</div><div class=\"value\">{H(invoice.CustomerName ?? "N/A")}</div></div><div class=\"info-col\"><div class=\"label\">Cashier</div><div class=\"value\">{H(invoice.UserName)}</div></div></div>");
            sb.AppendLine("<table><thead><tr><th style=\"width:35%\">PRODUCT</th><th class=\"center\" style=\"width:10%\">QTY</th><th class=\"right\" style=\"width:15%\">PRICE</th><th class=\"right\" style=\"width:10%\">DISC</th><th class=\"right\" style=\"width:15%\">AMOUNT</th></tr></thead><tbody>");
            foreach (var it in invoice.Items)
            {
                sb.AppendLine($"<tr><td>{H(it.ProductName)}</td><td class=\"center\">{F2(it.Quantity)}</td><td class=\"right\">{F2(it.Price)}</td><td class=\"right\">{F1(it.ItemDiscountPercentage)}%</td><td class=\"right\">{F2(it.SubTotal)}</td></tr>");
            }
            sb.AppendLine($"</tbody></table><div class=\"totals-section\"><div class=\"row\"><span>SUBTOTAL</span><span>{F2(gross)}</span></div><div class=\"row\"><span>DISCOUNT</span><span>-{F2(disc)}</span></div><div class=\"row\"><span>TAX</span><span>{F2(tax)}</span></div><div class=\"row total\"><span>TOTAL</span><span>{F2(total)}</span></div>");
            if (remainThis > 0) sb.AppendLine($"<div class=\"row\"><span>PAID</span><span>{F2(totalReceive)}</span></div><div class=\"row due\"><span>BALANCE DUE</span><span>{F2(remainThis)}</span></div>");
            if (showTotalDue) sb.AppendLine($"<div class=\"row grand\"><span>TOTAL DUE</span><span>{F2(customerTotalDue!.Value)}</span></div>");
            sb.AppendLine("</div>");
            sb.AppendLine(fbrFooterHtml);
            sb.AppendLine($"<div class=\"footer\">{(!string.IsNullOrEmpty(footerNote) ? $"<div class=\"footer-note\">NOTE: {H(footerNote)}</div>" : "")}<div class=\"thankyou\">THANK YOU FOR YOUR BUSINESS!</div><div style=\"margin-top:8px;\">Software by Asim Shahzad | PH:0304-7674787</div></div></div></body></html>");
            return sb.ToString();
        }

        if (normalized == "barcode")
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>Sale Invoice (Barcode A4) - {H(invoice.PostedNumber)}</title>");
            sb.AppendLine("<style>:root{--text:#111;--muted:#666;--border:#ccc;--primary:#0d6efd;}*{box-sizing:border-box;}html,body{margin:0;padding:0;font-family:'Segoe UI',Arial,sans-serif;color:var(--text);}.page{width:210mm;min-height:297mm;padding:18mm 14mm;margin:0 auto;}.print-actions{margin:12px auto;width:210mm;text-align:right;}.print-actions button{padding:8px 16px;cursor:pointer;background:var(--primary);color:#fff;border:none;border-radius:4px;font-size:12px;}@media print{.print-actions{display:none;}.page{padding:12mm;}}.header{display:flex;justify-content:space-between;align-items:center;border-bottom:2px solid var(--primary);padding-bottom:12px;margin-bottom:15px;}.store-info h1{margin:0;font-size:20px;color:var(--primary);}.store-info .details{font-size:10px;color:var(--muted);margin-top:4px;}.barcode-section{text-align:right;}.barcode-text{font-family:'Courier New',monospace;font-size:18px;font-weight:bold;letter-spacing:2px;}.meta-boxes{display:grid;grid-template-columns:repeat(4,1fr);gap:12px;margin-bottom:15px;}.meta-box{background:#f8f9fa;padding:10px;border-radius:4px;border-left:3px solid var(--primary);}.meta-box .label{font-size:9px;text-transform:uppercase;color:var(--muted);}.meta-box .value{font-weight:600;font-size:12px;margin-top:2px;}table{width:100%;border-collapse:collapse;margin-top:10px;font-size:11px;}thead th{background:var(--primary);color:#fff;text-align:left;padding:10px 8px;font-size:10px;text-transform:uppercase;}tbody td{padding:8px;border-bottom:1px solid #e9ecef;}tbody tr:nth-child(even){background:#f8f9fa;}.right{text-align:right;}.center{text-align:center;}.product-barcode{font-family:'Courier New',monospace;font-size:9px;color:var(--muted);}.summary-grid{display:grid;grid-template-columns:1fr 1fr;gap:20px;margin-top:20px;}.summary-box{border:1px solid var(--border);border-radius:6px;overflow:hidden;}.summary-box .title{background:#343a40;color:#fff;padding:8px 12px;font-weight:bold;font-size:11px;}.summary-box .row{display:flex;justify-content:space-between;padding:8px 12px;border-bottom:1px solid #e9ecef;}.summary-box .row.total{background:#f8f9fa;font-weight:bold;font-size:14px;}.summary-box .row.due{background:#fff3cd;font-weight:bold;}.summary-box .row.grand{background:#dc3545;color:#fff;font-weight:bold;font-size:16px;}.qr-section{margin-top:15px;text-align:center;padding:15px;background:#f8f9fa;border-radius:6px;}.qr-text{font-size:9px;color:var(--muted);margin-top:4px;}.footer{margin-top:20px;padding-top:12px;border-top:1px solid var(--border);text-align:center;}.footer-note{background:#fff3cd;padding:8px;border-radius:4px;border-left:3px solid #ffc107;font-size:10px;margin-bottom:10px;}.thankyou{font-style:italic;color:var(--muted);}.powered{font-size:9px;color:#adb5bd;margin-top:5px;}</style>");
            sb.AppendLine(calibrationHtml);
            sb.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\"><div class=\"print-actions\"><button onclick=\"window.print()\">Print Invoice</button></div><div class=\"page\"><div class=\"header\"><div class=\"store-info\">");
            if (!string.IsNullOrEmpty(logo)) sb.AppendLine($"<div style=\"margin-bottom:6px;\"><img src=\"{H(logo)}\" alt=\"Logo\" style=\"max-height:48px;max-width:150px;object-fit:contain;\"/></div>");
            sb.AppendLine($"<h1>{H(setting.StoreName)}</h1><div class=\"details\">{H(setting.LicenseNumber)} | {H(setting.Address)} | Ph: {H(setting.PhoneNumber)}</div></div><div class=\"barcode-section\"><div class=\"barcode-text\">{H(invoice.PostedNumber)}</div><div style=\"font-size:10px;color:var(--muted);\">{H(dateStr)} {H(timeStr)}</div></div></div>");
            sb.AppendLine($"<div class=\"meta-boxes\"><div class=\"meta-box\"><div class=\"label\">Customer</div><div class=\"value\">{H(invoice.CustomerName ?? "N/A")}</div></div><div class=\"meta-box\"><div class=\"label\">Cashier</div><div class=\"value\">{H(invoice.UserName)}</div></div><div class=\"meta-box\"><div class=\"label\">Doctor</div><div class=\"value\">{H(invoice.DoctorName ?? "N/A")}</div></div><div class=\"meta-box\"><div class=\"label\">Patient</div><div class=\"value\">{H(invoice.PatientName ?? "N/A")}</div></div></div>");
            sb.AppendLine("<table><thead><tr><th style=\"width:28%\">Product</th><th style=\"width:8%\">Pack</th><th style=\"width:10%\">Batch</th><th style=\"width:8%\">Expiry</th><th class=\"right\" style=\"width:8%\">Qty</th><th class=\"right\" style=\"width:10%\">Price</th><th class=\"right\" style=\"width:8%\">Disc</th><th class=\"right\" style=\"width:13%\">Amount</th></tr></thead><tbody>");
            foreach (var it in invoice.Items)
            {
                sb.AppendLine("<tr><td>");
                sb.AppendLine($"<div>{H(it.ProductName)}</div>");
                if (!string.IsNullOrEmpty(it.Barcode)) sb.AppendLine($"<div class=\"product-barcode\">{H(it.Barcode)}</div>");
                sb.AppendLine($"</td><td>{it.PackSize}</td><td>{H(it.BatchNumber ?? "-")}</td><td>{H(it.Expiry ?? "-")}</td><td class=\"right\">{F2(it.Quantity)}</td><td class=\"right\">{F2(it.Price)}</td><td class=\"right\">{F1(it.ItemDiscountPercentage)}%</td><td class=\"right\">{F2(it.SubTotal)}</td></tr>");
            }
            sb.AppendLine($"</tbody></table><div class=\"summary-grid\"><div class=\"summary-box\"><div class=\"title\">INVOICE SUMMARY</div><div class=\"row\"><span>Gross Amount</span><span>{F2(gross)}</span></div><div class=\"row\"><span>Discount</span><span>-{F2(disc)}</span></div><div class=\"row\"><span>Tax</span><span>{F2(tax)}</span></div><div class=\"row total\"><span>TOTAL</span><span>{F2(total)}</span></div></div><div class=\"summary-box\"><div class=\"title\">PAYMENT DETAILS</div>");
            if (remainThis > 0) sb.AppendLine($"<div class=\"row\"><span>Amount Paid</span><span>{F2(totalReceive)}</span></div><div class=\"row due\"><span>Balance Due</span><span>{F2(remainThis)}</span></div>");
            else sb.AppendLine("<div class=\"row\" style=\"background:#d4edda;color:#155724;\"><span>PAID IN FULL</span><span></span></div>");
            if (showTotalDue) sb.AppendLine($"<div class=\"row grand\"><span>TOTAL DUE</span><span>{F2(customerTotalDue!.Value)}</span></div>");
            sb.AppendLine("</div></div>");
            var a4BarSvg = GenerateCode128Svg(string.IsNullOrWhiteSpace(invoice.PostedNumber) ? "INV-0001" : invoice.PostedNumber, 220, 44);
            sb.AppendLine($"<div class=\"qr-section\">{a4BarSvg}<div class=\"qr-text\">{H(invoice.PostedNumber)} — Verified Invoice</div></div>");
            sb.AppendLine(fbrFooterHtml);
            sb.AppendLine($"<div class=\"footer\">{(!string.IsNullOrEmpty(footerNote) ? $"<div class=\"footer-note\">{H(footerNote)}</div>" : "")}<div class=\"thankyou\">Thank you for your business!</div><div class=\"powered\">Software by Asim Shahzad | PH:0304-7674787</div></div></div></body></html>");
            return sb.ToString();
        }

        // Standard A4 template (`sale_invoice_a4_standard`)
        var sbStdA4 = new StringBuilder();
        sbStdA4.AppendLine($"<!DOCTYPE html><html><head><meta charset=\"utf-8\"/><title>{(isWholesale ? "Wholesale Sale Invoice" : "Sale Invoice")} (A4) - {H(invoice.PostedNumber)}</title>");
        sbStdA4.AppendLine("<style>:root{--text:#111;--muted:#666;--border:#ccc;}*{box-sizing:border-box;}html,body{margin:0;padding:0;font-family:Arial,Helvetica,sans-serif;color:var(--text);}.page{width:210mm;min-height:297mm;padding:16mm 14mm;margin:0 auto;display:flex;flex-direction:column;}.print-actions{margin:12px auto;width:210mm;text-align:right;}.print-actions button{padding:8px 12px;cursor:pointer;}@media print{.print-actions{display:none;}.page{padding:10mm 10mm;}}.header{display:flex;align-items:center;gap:16px;border-bottom:1px solid var(--border);padding-bottom:12px;}.logo{width:100px;height:100px;object-fit:contain;}.store h1{margin:0;font-size:20px;letter-spacing:.4px;}.store .meta{margin-top:4px;font-size:12px;color:var(--muted);line-height:1.4;}.title-row{display:flex;justify-content:space-between;align-items:baseline;margin:14px 0 6px;}.title-row .title{font-size:18px;font-weight:700;}.title-row .code{color:var(--muted);}.grid{display:grid;grid-template-columns:repeat(3,1fr);gap:8px 16px;font-size:12px;margin-bottom:10px;}.grid .lbl{color:var(--muted);width:110px;display:inline-block;}.grid .val{font-weight:600;}table{width:100%;border-collapse:collapse;margin-top:8px;font-size:12px;table-layout:fixed;}thead th{text-align:left;border-bottom:1px solid var(--border);padding:8px 6px;font-size:12px;}tbody td{border-bottom:1px dashed #e0e0e0;padding:7px 6px;vertical-align:top;word-wrap:break-word;}.right{text-align:right;}.footer-total{width:55%;margin-left:auto;border:1px solid var(--border);border-radius:6px;overflow:hidden;margin-top:12px;}.footer-total .row{display:flex;justify-content:space-between;padding:10px 12px;}.footer-total .row+.row{border-top:1px solid var(--border);}.footer-total .row.total{font-weight:800;}.footer-total .row.grand{font-weight:900;font-size:14px;}.bottom{margin-top:auto;}.footer-note{margin-top:12px;padding-top:8px;border-top:1px solid var(--border);font-size:11px;color:var(--muted);white-space:pre-wrap;}.thankyou{margin-top:10px;font-size:12px;text-align:center;}</style>");
        sbStdA4.AppendLine(calibrationHtml);
        sbStdA4.AppendLine($"</head><body onload=\"{AutoPrintBodyOnload}\"><div class=\"print-actions\"><button onclick=\"if(window.karobarDesktop&&window.karobarDesktop.silentPrint){{window.karobarDesktop.silentPrint();}}else{{window.print();}}\">Print</button></div><div class=\"page\"><div class=\"header\">");
        if (!string.IsNullOrEmpty(logo)) sbStdA4.AppendLine($"<img class=\"logo\" src=\"{H(logo)}\" alt=\"Logo\">");
        sbStdA4.AppendLine($"<div class=\"store\"><h1>{H(setting.StoreName)}</h1><div class=\"meta\">{(!string.IsNullOrEmpty(setting.LicenseNumber) ? $"<div>License: {H(setting.LicenseNumber)}</div>" : "")}{(!string.IsNullOrEmpty(setting.PhoneNumber) ? $"<div>Phone: {H(setting.PhoneNumber)}</div>" : "")}{(!string.IsNullOrEmpty(setting.Address) ? $"<div>Address: {H(setting.Address)}</div>" : "")}</div></div></div>");
        sbStdA4.AppendLine($"<div class=\"title-row\"><div class=\"title\">{(isWholesale ? "Wholesale Sale Invoice" : "Sale Invoice")}</div><div class=\"code\"># {H(invoice.PostedNumber)}</div></div>");
        sbStdA4.AppendLine($"<div class=\"grid\"><div><span class=\"lbl\">Date:</span> <span class=\"val\">{H(dateStr)}</span></div><div><span class=\"lbl\">User:</span> <span class=\"val\">{H(invoice.UserName)}</span></div><div><span class=\"lbl\">Customer:</span> <span class=\"val\">{H(invoice.CustomerName)}</span></div><div><span class=\"lbl\">Doctor:</span> <span class=\"val\">{H(invoice.DoctorName)}</span></div><div><span class=\"lbl\">Patient:</span> <span class=\"val\">{H(invoice.PatientName)}</span></div><div><span class=\"lbl\">Remarks:</span> <span class=\"val\">{H(invoice.Remarks)}</span></div></div>");
        sbStdA4.AppendLine("<table><thead><tr><th style=\"width:25%\">Product</th><th style=\"width:10%\">Pack</th><th style=\"width:12%\">Batch</th><th style=\"width:10%\">Expiry</th><th class=\"right\" style=\"width:10%\">Qty</th><th class=\"right\" style=\"width:10%\">Price</th><th class=\"right\" style=\"width:8%\">Disc %</th><th class=\"right\" style=\"width:15%\">Sub Total</th></tr></thead><tbody>");
        foreach (var it in invoice.Items)
        {
            sbStdA4.AppendLine($"<tr><td>{H(it.ProductName)}</td><td>{it.PackSize}</td><td>{H(it.BatchNumber)}</td><td>{H(it.Expiry)}</td><td class=\"right\">{F2(it.Quantity)}</td><td class=\"right\">{F2(it.Price)}</td><td class=\"right\">{F2(it.ItemDiscountPercentage)}</td><td class=\"right\">{F2(it.SubTotal)}</td></tr>");
        }
        sbStdA4.AppendLine($"</tbody></table><div class=\"footer-total\"><div class=\"row\"><div>Gross</div><div>{F2(gross)}</div></div><div class=\"row\"><div>Discount</div><div>{F2(disc)}</div></div><div class=\"row\"><div>Tax</div><div>{F2(tax)}</div></div><div class=\"row total\"><div>Total</div><div>{F2(total)}</div></div>");
        if (showBalance)
        {
            sbStdA4.AppendLine($"<div class=\"row\"><div>Total Receive</div><div>{F2(totalReceive)}</div></div>");
            if (remainThis > 0) sbStdA4.AppendLine($"<div class=\"row\"><div>Balance</div><div>{F2(remainThis)}</div></div>");
            if (showTotalDue) sbStdA4.AppendLine($"<div class=\"row grand\"><div>Total Due</div><div>{F2(customerTotalDue!.Value)}</div></div>");
        }
        sbStdA4.AppendLine("</div>");
        sbStdA4.AppendLine(fbrFooterHtml);
        sbStdA4.AppendLine($"<div class=\"bottom\">{(!string.IsNullOrEmpty(footerNote) ? $"<div class=\"footer-note\">{H(footerNote)}</div>" : "")}<div class=\"thankyou\">Thank you for your business!</div></div></div></body></html>");
        return sbStdA4.ToString();
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
            GrossAmount = 500.00m,
            DiscountAmount = 50.00m,
            TaxAmount = 0m,
            Total = 450.00m,
            TotalReceive = 500.00m,
            Remarks = "Please keep refrigerated",
            DoctorName = "Dr. Ahmed Khan",
            PatientName = "Ali Ahmed",
            UserName = "Admin User",
            CustomerName = "John Doe",
            CustomerPhone = "0300 1234567",
            CustomerAddress = "Model Town, Lahore",
            Items =
            [
                new PrintInvoiceItemViewModel
                {
                    ProductName = "Paracetamol 500mg",
                    Barcode = "123456789012",
                    ProductCode = "PRD-001",
                    PackSize = 1,
                    Quantity = 2,
                    Price = 100.00m,
                    SubTotal = 200.00m,
                    ItemDiscountPercentage = 0,
                    BatchNumber = "B001",
                    Expiry = "2026-12",
                    DosageInstruction = "1 + 0 + 1 • کھانے کے بعد (After Meal)"
                },
                new PrintInvoiceItemViewModel
                {
                    ProductName = "Brufen 400mg",
                    Barcode = "987654321098",
                    ProductCode = "PRD-002",
                    PackSize = 1,
                    Quantity = 1,
                    Price = 150.00m,
                    SubTotal = 150.00m,
                    ItemDiscountPercentage = 0,
                    BatchNumber = "B002",
                    Expiry = "2026-06",
                    DosageInstruction = "1 چمچ صبح و شام (1 tsp Morning & Night)"
                },
                new PrintInvoiceItemViewModel
                {
                    ProductName = "Vitamin C 1000mg",
                    Barcode = "456789123456",
                    ProductCode = "PRD-003",
                    PackSize = 1,
                    Quantity = 3,
                    Price = 50.00m,
                    SubTotal = 150.00m,
                    ItemDiscountPercentage = 10,
                    ItemDiscountAmount = 15.00m,
                    BatchNumber = "B003",
                    Expiry = "2027-03",
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
    /// Renders `/print/share-preview/{size}/{template}` (`resources/views/pdf/sale_invoice.blade.php` with preview CSS wrapper).
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
        sampleInvoice.TotalReceive = 450.00m;

        var isThermal = normSize == "thermal";
        var fbrFooter = RenderFbrFiscalFooter(sampleInvoice, setting);
        var (accentHex, accentBgHex) = normTpl switch
        {
            "minimal" => ("#334155", "#ffffff"),
            "detailed" => ("#1e3a8a", "#eff6ff"),
            "compact" => ("#0f766e", "#f0fdfa"),
            "bold" => ("#000000", "#f8fafc"),
            "barcode" => ("#18181b", "#f4f4f5"),
            _ => ("#0f172a", "#f8fafc")
        };
        var showLogoImg = setting.ShareShowLogo && !string.IsNullOrWhiteSpace(setting.LogoUrl);

        var sb = new StringBuilder();
        sb.AppendLine($"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"UTF-8\"><title>Sale Invoice {H(sampleInvoice.PostedNumber)}</title>");
        sb.AppendLine($"<style>body{{font-family:DejaVu Sans,Arial,sans-serif;font-size:11px;color:#1e293b;line-height:1.45;margin:0;padding:0;}}table{{width:100%;border-collapse:collapse;}}.header-table{{border-bottom:2px solid {accentHex};padding-bottom:10px;margin-bottom:14px;}}.store-logo{{max-height:42px;max-width:120px;object-fit:contain;margin-bottom:4px;display:block;}}.store-name{{font-size:18px;font-weight:bold;color:{accentHex};margin:0 0 3px 0;}}.store-meta{{font-size:10px;color:#475569;margin:1px 0;}}.doc-badge{{display:inline-block;background:{accentHex};color:#fff;font-size:13px;font-weight:bold;padding:4px 12px;border-radius:3px;text-transform:uppercase;}}.meta-box{{background:{accentBgHex};border:1px solid #cbd5e1;border-radius:4px;padding:8px 10px;margin-bottom:14px;}}.meta-label{{font-size:9px;font-weight:bold;color:#64748b;text-transform:uppercase;}}.meta-value{{font-size:11px;font-weight:bold;color:{accentHex};}}.items-table th{{background:{accentHex};color:#fff;font-size:9.5px;font-weight:bold;text-transform:uppercase;padding:6px 7px;border:1px solid {accentHex};text-align:left;}}.items-table td{{padding:6px 7px;border:1px solid #cbd5e1;font-size:10.5px;}}.text-right{{text-align:right!important;}}.text-center{{text-align:center!important;}}.totals-table{{width:270px;margin-left:auto;margin-top:12px;}}.totals-table td{{padding:4px 8px;font-size:11px;border-bottom:1px solid #e2e8f0;}}.totals-table .grand-row td{{background:{accentHex};color:#fff;font-weight:bold;font-size:12px;border:none;padding:6px 8px;}}.footer-note{{margin-top:24px;padding-top:8px;border-top:1px solid #cbd5e1;font-size:9.5px;color:#64748b;text-align:center;}}</style>");
        sb.AppendLine(isThermal
            ? "<style>html{background:#f1f5f9;padding:12px 0;}body{width:58mm;min-height:80mm;max-width:94%;margin:0 auto!important;padding:8px 7px!important;background:#ffffff;box-shadow:0 4px 16px rgba(15,23,42,0.12);border-radius:4px;font-size:7px!important;}.totals-table{width:100%!important;}</style>"
            : "<style>html{background:#f1f5f9;padding:12px;}body{max-width:210mm;margin:0 auto!important;padding:18px 22px!important;background:#ffffff;box-shadow:0 4px 16px rgba(15,23,42,0.08);border-radius:4px;}</style>");
        sb.AppendLine("</head><body>");
        sb.AppendLine($"<table class=\"header-table\"><tr><td style=\"width:60%;vertical-align:top;\">{(showLogoImg ? $"<img src=\"{H(setting.LogoUrl)}\" alt=\"Logo\" class=\"store-logo\"/>" : "")}<div class=\"store-name\">{H(setting.StoreName)}</div><div class=\"store-meta\">{H(setting.Address)}</div><div class=\"store-meta\">Phone: {H(setting.PhoneNumber)}</div></td><td style=\"width:40%;vertical-align:top;text-align:right;\"><div class=\"doc-badge\">Sale Invoice</div><div style=\"margin-top:6px;font-size:12px;font-weight:bold;\">#{H(sampleInvoice.PostedNumber)}</div><div class=\"store-meta\">Date: {DateTime.Now:dd MMM yyyy}</div></td></tr></table>");
        sb.AppendLine($"<div class=\"meta-box\"><table><tr><td><div class=\"meta-label\">Billed To Customer</div><div class=\"meta-value\">{H(sampleInvoice.CustomerName)}</div></td><td class=\"text-right\"><div class=\"meta-label\">Invoice Details</div><div class=\"store-meta\">Design: <strong>{H(normTpl.ToUpperInvariant())}</strong> ({H(normSize.ToUpperInvariant())})</div></td></tr></table></div>");
        sb.AppendLine("<table class=\"items-table\"><thead><tr><th>Item</th><th class=\"text-right\">Qty</th><th class=\"text-right\">Rate</th><th class=\"text-right\">Total</th></tr></thead><tbody>");
        foreach (var it in sampleInvoice.Items)
        {
            sb.AppendLine($"<tr><td><strong>{H(it.ProductName)}</strong></td><td class=\"text-right\">{F0(it.Quantity)}</td><td class=\"text-right\">{F2(it.Price)}</td><td class=\"text-right\"><strong>{F2(it.SubTotal)}</strong></td></tr>");
        }
        sb.AppendLine($"</tbody></table><table class=\"totals-table\"><tr><td>Gross Amount</td><td class=\"text-right\">Rs. {F2(sampleInvoice.GrossAmount)}</td></tr><tr><td>Discount</td><td class=\"text-right\">- Rs. {F2(sampleInvoice.DiscountAmount)}</td></tr><tr class=\"grand-row\"><td>Invoice Total</td><td class=\"text-right\">Rs. {F2(sampleInvoice.Total)}</td></tr></table>");
        sb.AppendLine(fbrFooter);
        sb.AppendLine($"<div class=\"footer-note\"><div>{H(setting.Note)}</div><div>Generated on {DateTime.Now:dd MMM yyyy, hh:mm tt}</div></div></body></html>");
        return sb.ToString();
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

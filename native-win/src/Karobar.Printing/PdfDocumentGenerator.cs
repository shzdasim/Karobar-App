using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Karobar.Printing;

public sealed class PdfTableColumnDef
{
    public string Header { get; set; } = "";
    public float RelativeWidth { get; set; } = 1f;
    public bool AlignRight { get; set; }
    public bool AlignCenter { get; set; }
    public bool IsDiscountColumn { get; set; }
    public bool IsTotalColumn { get; set; }
}

public sealed class PdfKeyValuePair
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsGrandTotal { get; set; }
    /// <summary>
    /// Optional visual tone: null/"normal" | "discount" | "success" | "warning" | "danger".
    /// </summary>
    public string? Tone { get; set; }

    public PdfKeyValuePair() { }
    public PdfKeyValuePair(string label, string value, bool isGrandTotal = false, string? tone = null)
    {
        Label = label;
        Value = value;
        IsGrandTotal = isGrandTotal;
        Tone = tone;
    }
}

public sealed class SharedDocumentPdfRequest
{
    public string DocumentType { get; set; } = "sale_invoice";
    public string DocumentTitle { get; set; } = "SALE INVOICE";
    public string DocumentNumber { get; set; } = "INV-001";
    public string DocumentDate { get; set; } = DateTime.Now.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    public string? DocumentTime { get; set; }
    public string PaperSize { get; set; } = "a4"; // "a4" | "thermal"
    public string PdfDesign { get; set; } = "standard"; // "standard" | "minimal" | "detailed" | "compact" | "bold" | "barcode"
    public PrintSettingViewModel Setting { get; set; } = new();
    public string PartyHeading { get; set; } = "Billed To";
    public string PartyName { get; set; } = "Walk-in Customer";
    public string? PartyPhone { get; set; }
    public string? PartyAddress { get; set; }
    public string? DoctorName { get; set; }
    public string? PatientName { get; set; }
    public string? StatusBadge { get; set; } // e.g. "PAID IN FULL", "PARTIAL PAID", "CREDIT DUE"
    public string? StatusTone { get; set; } // "success" | "warning" | "danger" | "neutral"
    public string? TotalSavingsText { get; set; } // e.g. "Rs. 365.00"
    public string? NetTotalHighlight { get; set; } // e.g. "Rs. 4,565.00"
    public List<PdfKeyValuePair> MetaDetails { get; set; } = [];
    public List<PdfTableColumnDef> Columns { get; set; } = [];
    public List<List<string>> Rows { get; set; } = [];
    public List<PdfKeyValuePair> Totals { get; set; } = [];
    public string? Remarks { get; set; }
    public string? FbrInvoiceNumber { get; set; }
    public string? FbrQrData { get; set; }
    public decimal FbrPosFee { get; set; }
}

public sealed class AnalyticalReportPdfRequest
{
    public string ReportType { get; set; } = "cost-of-sale";
    public string ReportTitle { get; set; } = "Analytical Report";
    public string? Subtitle { get; set; }
    public string? FromDate { get; set; }
    public string? ToDate { get; set; }
    public bool Landscape { get; set; }
    public PrintSettingViewModel Setting { get; set; } = new();
    public List<PdfKeyValuePair> SummaryCards { get; set; } = [];
    public List<PdfTableColumnDef> Columns { get; set; } = [];
    public List<List<string>> Rows { get; set; } = [];
    public List<PdfKeyValuePair> FooterTotals { get; set; } = [];
}

/// <summary>
/// Generates all 15 Shared Document PDFs (`sale_invoice`, `sale_return`, `purchase_invoice`, etc.)
/// and all 18 Analytical Report PDFs using QuestPDF with 6 distinct colorful design themes
/// (`standard`, `minimal`, `detailed`, `compact`, `bold`, `barcode`) across A4 and 80mm Thermal rolls.
/// Permanently embeds developer credit: "Software by Engr. M Asim Shahzad • Phone: 923047674787".
/// </summary>
public sealed class PdfDocumentGenerator
{
    public const string DeveloperCreditTitle = "Software by Engr. M Asim Shahzad";
    public const string DeveloperCreditPhone = "Phone: 923047674787";

    public static readonly IReadOnlyDictionary<string, string> SupportedSharedDocuments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["sale_invoice"] = "SALE INVOICE",
        ["sale_return"] = "SALE RETURN / CREDIT NOTE",
        ["purchase_invoice"] = "PURCHASE INVOICE (GRN)",
        ["purchase_return"] = "PURCHASE RETURN / DEBIT NOTE",
        ["purchase_order"] = "PURCHASE ORDER",
        ["customer_ledger"] = "CUSTOMER ACCOUNT STATEMENT",
        ["supplier_ledger"] = "SUPPLIER ACCOUNT STATEMENT",
        ["payment"] = "OFFICIAL PAYMENT VOUCHER",
        ["payment_voucher"] = "OFFICIAL PAYMENT VOUCHER",
        ["cash_drawer_session"] = "CASH DRAWER SHIFT Z-REPORT",
        ["cash_drawer_z_report"] = "CASH DRAWER SHIFT Z-REPORT",
        ["delivery_challan"] = "DELIVERY CHALLAN / GATE PASS",
        ["quotation"] = "PRICE QUOTATION",
        ["regular_refill_reminder"] = "REGULAR MEDICINE REFILL REMINDER",
        ["stock_disposal"] = "STOCK DISPOSAL / DESTRUCTION CERTIFICATE",
        ["stocktake_session"] = "STOCKTAKE COUNT & VARIANCE REPORT"
    };

    public static readonly IReadOnlyDictionary<string, string> SupportedAnalyticalReports = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["cost-of-sale"] = "Cost of Sale Summary Report",
        ["cost-of-sale-detail"] = "Cost of Sale Detailed Item Report",
        ["purchase-detail"] = "Purchase Detail Report",
        ["sale-detail"] = "Sale Detail Report",
        ["current-stock"] = "Current Stock Valuation Report",
        ["stock-adjustment"] = "Stock Adjustment Audit Report",
        ["product-comprehensive"] = "Product Comprehensive Movement Report",
        ["near-expiry-product"] = "Near Expiry & Expired Batch Report",
        ["purchase-returns"] = "Purchase Returns Report",
        ["sale-returns"] = "Sale Returns Report",
        ["purchase-summary"] = "Purchase Summary Report",
        ["outstanding-aging"] = "Outstanding Receivables & Payables Aging Report",
        ["overdue-work-queue"] = "Overdue Collections Work Queue Report",
        ["reorder"] = "Low Stock Reorder Forecast Report",
        ["net-profit"] = "Net Profit & P&L Statement Report",
        ["tax-return"] = "FBR Sales Tax & Withholding Return Report",
        ["narcotics"] = "Controlled Narcotics & Psychotropic Register (DRAP Form-7)",
        ["financial-statements"] = "General Ledger Trial Balance & Financial Statements"
    };

    static PdfDocumentGenerator()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        // Prevent QuestPDF DocumentDrawingException when store/customer/product text contains Urdu/Arabic or non-Latin Unicode glyphs
        QuestPDF.Settings.CheckIfAllTextGlyphsAreAvailable = false;
    }

    private sealed record PdfThemePalette(
        string Id,
        string Primary,
        string Secondary,
        string Accent,
        string SoftBg,
        string Border,
        string Text,
        string MutedText);

    private static PdfThemePalette ResolveTheme(string? design)
    {
        var id = (design ?? "standard").Trim().ToLowerInvariant();
        return id switch
        {
            "minimal" => new PdfThemePalette("minimal", "#1e293b", "#64748b", "#334155", "#ffffff", "#cbd5e1", "#0f172a", "#475569"),
            "detailed" => new PdfThemePalette("detailed", "#1e3a8a", "#1d4ed8", "#2563eb", "#eff6ff", "#bfdbfe", "#0f172a", "#475569"),
            "compact" => new PdfThemePalette("compact", "#0f766e", "#115e59", "#0d9488", "#f0fdfa", "#99f6e4", "#0f172a", "#475569"),
            "bold" => new PdfThemePalette("bold", "#064e3b", "#047857", "#10b981", "#ecfdf5", "#a7f3d0", "#0f172a", "#334155"),
            "barcode" => new PdfThemePalette("barcode", "#18181b", "#3f3f46", "#d97706", "#fafafa", "#d4d4d8", "#18181b", "#52525b"),
            _ => new PdfThemePalette("standard", "#0f172a", "#334155", "#2563eb", "#f8fafc", "#cbd5e1", "#0f172a", "#475569")
        };
    }

    private static (string Bg, string Fg, string Border) ResolveStatusColors(string? tone)
    {
        return (tone ?? "").Trim().ToLowerInvariant() switch
        {
            "success" => ("#dcfce7", "#166534", "#86efac"),
            "warning" => ("#fef3c7", "#92400e", "#fcd34d"),
            "danger" => ("#fee2e2", "#991b1b", "#fca5a5"),
            _ => ("#e2e8f0", "#334155", "#cbd5e1")
        };
    }

    /// <summary>
    /// Generates any of the 15 Shared Document PDFs across 6 colorful themes in A4 or 80mm Thermal roll format.
    /// </summary>
    public byte[] GenerateSharedDocumentPdf(SharedDocumentPdfRequest request)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var isThermal = string.Equals(request.PaperSize, "thermal", StringComparison.OrdinalIgnoreCase);
        var theme = ResolveTheme(request.PdfDesign);
        var isMinimal = theme.Id == "minimal";
        var isDetailed = theme.Id == "detailed";
        var isCompact = theme.Id == "compact";
        var isBold = theme.Id == "bold";
        var isBarcode = theme.Id == "barcode";

        var title = SupportedSharedDocuments.TryGetValue(request.DocumentType, out var mappedTitle) && string.IsNullOrWhiteSpace(request.DocumentTitle)
            ? mappedTitle
            : (string.IsNullOrWhiteSpace(request.DocumentTitle) ? "OFFICIAL DOCUMENT" : request.DocumentTitle);

        var baseFontSize = isThermal ? 7.5f : (isCompact ? 8.5f : 9.4f);
        var dateDisplay = string.IsNullOrWhiteSpace(request.DocumentTime)
            ? request.DocumentDate
            : $"{request.DocumentDate} • {request.DocumentTime}";

        var docNumPayload = string.IsNullOrWhiteSpace(request.DocumentNumber)
            ? "0000"
            : request.DocumentNumber.Trim();

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                if (isThermal)
                {
                    // 80mm continuous thermal PDF for crisp 4-column layout (Name | Qty | Disc | Subt.) on WhatsApp & mobile
                    page.ContinuousSize(80, Unit.Millimetre);
                    page.Margin(3.2f, Unit.Millimetre);
                }
                else
                {
                    page.Size(PageSizes.A4);
                    page.Margin(isCompact ? 8.5f : 11.5f, Unit.Millimetre);
                }

                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(baseFontSize).FontColor(theme.Text));

                var logoBytes = request.Setting.ShareShowLogo ? TryLoadLogoBytes(request.Setting.LogoUrl) : null;

                // ================= HEADER =================
                page.Header().Column(headerCol =>
                {
                    if (isThermal)
                    {
                        RenderThermalHeader(headerCol, request, theme, title, dateDisplay, docNumPayload, logoBytes);
                    }
                    else
                    {
                        RenderA4Header(headerCol, request, theme, title, dateDisplay, docNumPayload, logoBytes);
                    }
                });

                // ================= CONTENT =================
                page.Content().PaddingTop(isThermal ? 4 : (isCompact ? 6 : 9)).Column(contentCol =>
                {
                    // 1. Party & Complete Invoice Metadata Section
                    if (isThermal)
                    {
                        RenderThermalMetadata(contentCol, request, theme);
                    }
                    else
                    {
                        RenderA4Metadata(contentCol, request, theme);
                    }

                    // 2. Items / Data Table (Supports multi-line cells via '\n': line 1 bold, line 2+ muted)
                    if (request.Columns.Count > 0)
                    {
                        RenderItemsTable(contentCol, request, theme, isThermal);
                    }

                    // 3. Bottom Summary Section: Remarks + Savings + Verification Strip (left on A4) & Totals (right on A4 / full on Thermal)
                    if (isThermal)
                    {
                        RenderThermalTotalsAndExtras(contentCol, request, theme);
                    }
                    else
                    {
                        RenderA4BottomSummary(contentCol, request, theme, docNumPayload);
                    }

                    // 4. Optional FBR Fiscal Verification Block (only for sale_invoice / sale_return)
                    var isFbrEligibleDoc = request.DocumentType is "sale_invoice" or "sale_return";
                    if (isFbrEligibleDoc && !string.IsNullOrWhiteSpace(request.FbrInvoiceNumber))
                    {
                        RenderFbrBlock(contentCol, request, isThermal);
                    }
                });

                // ================= FOOTER (Permanent Embedded Developer Credit) =================
                page.Footer().PaddingTop(isThermal ? 5 : 8).Column(footCol =>
                {
                    if (isThermal)
                    {
                        RenderThermalFooter(footCol, request, theme);
                    }
                    else
                    {
                        RenderA4Footer(footCol, request, theme);
                    }
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static void RenderThermalHeader(
        ColumnDescriptor headerCol,
        SharedDocumentPdfRequest request,
        PdfThemePalette theme,
        string title,
        string dateDisplay,
        string docNumPayload,
        byte[]? logoBytes)
    {
        var isMinimal = theme.Id == "minimal";
        var isBold = theme.Id == "bold";
        var isDetailed = theme.Id == "detailed";
        var isBarcode = theme.Id == "barcode";
        var isCompact = theme.Id == "compact";

        if (isBold || isDetailed)
        {
            headerCol.Item().Background(theme.Primary).BorderBottom(2.5f).BorderColor(theme.Accent)
                .PaddingVertical(6).PaddingHorizontal(5).Column(banner =>
                {
                    if (logoBytes != null)
                    {
                        banner.Item().PaddingBottom(3).AlignCenter().MaxHeight(26).MaxWidth(84).Image(logoBytes).FitArea();
                    }
                    banner.Item().AlignCenter().Text(request.Setting.StoreName).Bold().FontSize(10.5f).FontColor(Colors.White);
                    if (!string.IsNullOrWhiteSpace(request.Setting.Address))
                        banner.Item().AlignCenter().Text(request.Setting.Address).FontSize(6.5f).FontColor("#e2e8f0");
                    if (!string.IsNullOrWhiteSpace(request.Setting.PhoneNumber))
                        banner.Item().AlignCenter().Text($"Tel: {request.Setting.PhoneNumber}").FontSize(6.5f).FontColor("#e2e8f0");
                    if (!string.IsNullOrWhiteSpace(request.Setting.LicenseNumber))
                        banner.Item().AlignCenter().Text($"Lic: {request.Setting.LicenseNumber}").FontSize(6f).FontColor("#cbd5e1");

                    banner.Item().PaddingTop(4).AlignCenter().Background(theme.Accent).PaddingHorizontal(7).PaddingVertical(2)
                        .Text($"{title} • #{docNumPayload}").Bold().FontSize(7.2f).FontColor(isBold ? "#052e16" : Colors.White);
                });
        }
        else
        {
            headerCol.Item().BorderBottom(isMinimal ? 1.2f : 1.8f).BorderColor(theme.Primary).PaddingBottom(5).Column(head =>
            {
                if (logoBytes != null)
                {
                    head.Item().PaddingBottom(3).AlignCenter().MaxHeight(28).MaxWidth(96).Image(logoBytes).FitArea();
                }
                head.Item().AlignCenter().Text(request.Setting.StoreName).Bold().FontSize(10.5f).FontColor(theme.Primary);
                if (!string.IsNullOrWhiteSpace(request.Setting.Address))
                    head.Item().AlignCenter().Text(request.Setting.Address).FontSize(6.5f).FontColor(theme.MutedText);
                if (!string.IsNullOrWhiteSpace(request.Setting.PhoneNumber))
                    head.Item().AlignCenter().Text($"Ph: {request.Setting.PhoneNumber}").FontSize(6.5f).FontColor(theme.MutedText);
                if (!isCompact && !string.IsNullOrWhiteSpace(request.Setting.LicenseNumber))
                    head.Item().AlignCenter().Text($"License: {request.Setting.LicenseNumber}").FontSize(6f).FontColor(theme.MutedText);

                if (isMinimal)
                {
                    head.Item().PaddingTop(3).AlignCenter().Border(0.9f).BorderColor(theme.Primary).PaddingHorizontal(7).PaddingVertical(1.5f)
                        .Text($"{title} • #{docNumPayload}").Bold().FontSize(7f).FontColor(theme.Primary);
                }
                else
                {
                    head.Item().PaddingTop(3).AlignCenter().Background(theme.Primary).PaddingHorizontal(7).PaddingVertical(1.8f)
                        .Text($"{title} • #{docNumPayload}").Bold().FontSize(7f).FontColor(Colors.White);
                }

                head.Item().PaddingTop(2).AlignCenter().Text(dateDisplay).FontSize(6.2f).FontColor(theme.MutedText);

                if (isBarcode)
                {
                    var barcodeSvg = PrintTemplateRenderer.GenerateCode128Svg(docNumPayload, 165, 24);
                    head.Item().PaddingTop(3).AlignCenter().Width(155).Height(22).Svg(barcodeSvg);
                    head.Item().AlignCenter().Text($"REF: {docNumPayload}").FontSize(5.8f).FontColor(theme.Secondary);
                }
            });
        }
    }

    private static void RenderA4Header(
        ColumnDescriptor headerCol,
        SharedDocumentPdfRequest request,
        PdfThemePalette theme,
        string title,
        string dateDisplay,
        string docNumPayload,
        byte[]? logoBytes)
    {
        var isMinimal = theme.Id == "minimal";
        var isBold = theme.Id == "bold";
        var isDetailed = theme.Id == "detailed";
        var isCompact = theme.Id == "compact";
        var isBarcode = theme.Id == "barcode";

        if (isBold)
        {
            headerCol.Item().Background(theme.Primary).BorderBottom(3.5f).BorderColor(theme.Accent)
                .PaddingVertical(12).PaddingHorizontal(14).Row(row =>
                {
                    row.RelativeItem(6.5f).Row(leftRow =>
                    {
                        if (logoBytes != null)
                        {
                            leftRow.AutoItem().PaddingRight(10).AlignMiddle().Background(Colors.White).Padding(3)
                                .MaxHeight(38).MaxWidth(85).Image(logoBytes).FitArea();
                        }
                        leftRow.RelativeItem().Column(left =>
                        {
                            left.Item().Text(request.Setting.StoreName).Bold().FontSize(17.5f).FontColor(Colors.White);
                            if (!string.IsNullOrWhiteSpace(request.Setting.Address))
                                left.Item().Text(request.Setting.Address).FontSize(8.8f).FontColor("#d1fae5");
                            var contactLine = JoinNonEmpty(" • ",
                                !string.IsNullOrWhiteSpace(request.Setting.PhoneNumber) ? $"Ph: {request.Setting.PhoneNumber}" : null,
                                !string.IsNullOrWhiteSpace(request.Setting.LicenseNumber) ? $"Lic: {request.Setting.LicenseNumber}" : null);
                            if (!string.IsNullOrEmpty(contactLine))
                                left.Item().Text(contactLine).FontSize(8.5f).FontColor("#a7f3d0");
                        });
                    });

                    row.RelativeItem(3.5f).AlignRight().Column(right =>
                    {
                        right.Item().AlignRight().Background(theme.Accent).PaddingHorizontal(9).PaddingVertical(3)
                            .Text(title).Bold().FontSize(9.5f).FontColor("#052e16");
                        right.Item().PaddingTop(4).AlignRight().Text($"#{docNumPayload}").Bold().FontSize(14f).FontColor(Colors.White);
                        right.Item().AlignRight().Text(dateDisplay).FontSize(8.5f).FontColor("#d1fae5");
                    });
                });
            return;
        }

        if (isDetailed)
        {
            headerCol.Item().Background(theme.Primary).PaddingVertical(11).PaddingHorizontal(14).Row(row =>
            {
                row.RelativeItem(6.2f).Row(leftRow =>
                {
                    if (logoBytes != null)
                    {
                        leftRow.AutoItem().PaddingRight(10).AlignMiddle().Background(Colors.White).Padding(3)
                            .MaxHeight(36).MaxWidth(85).Image(logoBytes).FitArea();
                    }
                    leftRow.RelativeItem().Column(left =>
                    {
                        left.Item().Text(request.Setting.StoreName).Bold().FontSize(16.5f).FontColor(Colors.White);
                        if (!string.IsNullOrWhiteSpace(request.Setting.Address))
                            left.Item().Text(request.Setting.Address).FontSize(8.6f).FontColor("#dbeafe");
                        var contactLine = JoinNonEmpty(" • ",
                            !string.IsNullOrWhiteSpace(request.Setting.PhoneNumber) ? $"Phone: {request.Setting.PhoneNumber}" : null,
                            !string.IsNullOrWhiteSpace(request.Setting.LicenseNumber) ? $"License: {request.Setting.LicenseNumber}" : null);
                        if (!string.IsNullOrEmpty(contactLine))
                            left.Item().Text(contactLine).FontSize(8.5f).FontColor("#bfdbfe");
                    });
                });

                row.RelativeItem(3.8f).AlignRight().Border(0.8f).BorderColor("#60a5fa").Background("#172e73")
                    .PaddingVertical(6).PaddingHorizontal(10).Column(right =>
                    {
                        right.Item().AlignRight().Text(title).Bold().FontSize(8.5f).FontColor("#bfdbfe");
                        right.Item().PaddingTop(1).AlignRight().Text($"#{docNumPayload}").Bold().FontSize(13.5f).FontColor(Colors.White);
                        right.Item().AlignRight().Text(dateDisplay).FontSize(8.2f).FontColor("#e0e7ff");
                    });
            });
            return;
        }

        if (isMinimal)
        {
            headerCol.Item().BorderBottom(1.8f).BorderColor(theme.Primary).PaddingBottom(8).Row(row =>
            {
                row.RelativeItem(6.5f).Row(leftRow =>
                {
                    if (logoBytes != null)
                    {
                        leftRow.AutoItem().PaddingRight(10).AlignMiddle().MaxHeight(34).MaxWidth(85).Image(logoBytes).FitArea();
                    }
                    leftRow.RelativeItem().Column(left =>
                    {
                        left.Item().Text(request.Setting.StoreName).Bold().FontSize(16.5f).FontColor(theme.Primary);
                        var metaLine = JoinNonEmpty(" • ",
                            request.Setting.Address,
                            !string.IsNullOrWhiteSpace(request.Setting.PhoneNumber) ? $"Ph: {request.Setting.PhoneNumber}" : null,
                            !string.IsNullOrWhiteSpace(request.Setting.LicenseNumber) ? $"Lic: {request.Setting.LicenseNumber}" : null);
                        if (!string.IsNullOrEmpty(metaLine))
                            left.Item().Text(metaLine).FontSize(8.5f).FontColor(theme.MutedText);
                    });
                });

                row.RelativeItem(3.5f).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text(title.ToUpperInvariant()).Bold().FontSize(9.5f).FontColor(theme.Secondary);
                    right.Item().AlignRight().Text($"#{docNumPayload}").Bold().FontSize(13.5f).FontColor(theme.Primary);
                    right.Item().AlignRight().Text(dateDisplay).FontSize(8.5f).FontColor(theme.MutedText);
                });
            });
            return;
        }

        if (isCompact)
        {
            headerCol.Item().BorderTop(3f).BorderBottom(1.2f).BorderColor(theme.Primary).PaddingVertical(6).Row(row =>
            {
                row.RelativeItem(6.8f).Row(leftRow =>
                {
                    if (logoBytes != null)
                    {
                        leftRow.AutoItem().PaddingRight(8).AlignMiddle().MaxHeight(26).MaxWidth(68).Image(logoBytes).FitArea();
                    }
                    leftRow.RelativeItem().Column(left =>
                    {
                        left.Item().Text(request.Setting.StoreName).Bold().FontSize(13.5f).FontColor(theme.Primary);
                        var metaLine = JoinNonEmpty(" | ",
                            request.Setting.Address,
                            !string.IsNullOrWhiteSpace(request.Setting.PhoneNumber) ? $"Ph: {request.Setting.PhoneNumber}" : null,
                            !string.IsNullOrWhiteSpace(request.Setting.LicenseNumber) ? $"Lic: {request.Setting.LicenseNumber}" : null);
                        if (!string.IsNullOrEmpty(metaLine))
                            left.Item().Text(metaLine).FontSize(7.8f).FontColor(theme.MutedText);
                    });
                });

                row.RelativeItem(3.2f).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Background(theme.Primary).PaddingHorizontal(7).PaddingVertical(2)
                        .Text(title).Bold().FontSize(8.5f).FontColor(Colors.White);
                    right.Item().PaddingTop(2).AlignRight().Text($"#{docNumPayload} • {request.DocumentDate}").Bold().FontSize(9.2f);
                });
            });
            return;
        }

        if (isBarcode)
        {
            headerCol.Item().BorderBottom(2f).BorderColor(theme.Primary).PaddingBottom(8).Row(row =>
            {
                row.RelativeItem(6f).Row(leftRow =>
                {
                    if (logoBytes != null)
                    {
                        leftRow.AutoItem().PaddingRight(10).AlignMiddle().MaxHeight(36).MaxWidth(88).Image(logoBytes).FitArea();
                    }
                    leftRow.RelativeItem().BorderLeft(3).BorderColor(theme.Accent).PaddingLeft(8).Column(left =>
                    {
                        left.Item().Text(request.Setting.StoreName).Bold().FontSize(16f).FontColor(theme.Primary);
                        if (!string.IsNullOrWhiteSpace(request.Setting.Address))
                            left.Item().Text(request.Setting.Address).FontSize(8.5f).FontColor(theme.MutedText);
                        var contactLine = JoinNonEmpty(" • ",
                            !string.IsNullOrWhiteSpace(request.Setting.PhoneNumber) ? $"Ph: {request.Setting.PhoneNumber}" : null,
                            !string.IsNullOrWhiteSpace(request.Setting.LicenseNumber) ? $"Lic: {request.Setting.LicenseNumber}" : null);
                        if (!string.IsNullOrEmpty(contactLine))
                            left.Item().Text(contactLine).FontSize(8.5f).FontColor(theme.MutedText);
                    });
                });

                row.RelativeItem(4f).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Background(theme.Primary).PaddingHorizontal(8).PaddingVertical(2.5f)
                        .Text($"VERIFIED {title}").Bold().FontSize(8.5f).FontColor(Colors.White);
                    var barcodeSvg = PrintTemplateRenderer.GenerateCode128Svg(docNumPayload, 145, 26);
                    right.Item().PaddingTop(4).AlignRight().Width(145).Height(26).Svg(barcodeSvg);
                    right.Item().PaddingTop(1).AlignRight().Text($"#{docNumPayload} • {request.DocumentDate}").Bold().FontSize(8.8f).FontColor(theme.Primary);
                });
            });
            return;
        }

        // Standard Corporate
        headerCol.Item().BorderTop(4.5f).BorderColor(theme.Primary).BorderBottom(1.6f).BorderColor(theme.Primary)
            .PaddingTop(8).PaddingBottom(8).Row(row =>
            {
                row.RelativeItem(6.2f).Row(leftRow =>
                {
                    if (logoBytes != null)
                    {
                        leftRow.AutoItem().PaddingRight(10).AlignMiddle().MaxHeight(38).MaxWidth(95).Image(logoBytes).FitArea();
                    }
                    leftRow.RelativeItem().Column(left =>
                    {
                        left.Item().Text(request.Setting.StoreName).Bold().FontSize(17f).FontColor(theme.Primary);
                        if (!string.IsNullOrWhiteSpace(request.Setting.Address))
                            left.Item().Text(request.Setting.Address).FontSize(8.8f).FontColor(theme.MutedText);
                        var contactLine = JoinNonEmpty(" • ",
                            !string.IsNullOrWhiteSpace(request.Setting.PhoneNumber) ? $"Phone: {request.Setting.PhoneNumber}" : null,
                            !string.IsNullOrWhiteSpace(request.Setting.LicenseNumber) ? $"License #: {request.Setting.LicenseNumber}" : null);
                        if (!string.IsNullOrEmpty(contactLine))
                            left.Item().Text(contactLine).FontSize(8.8f).FontColor(theme.MutedText);
                    });
                });

                row.RelativeItem(3.8f).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Background(theme.Primary).PaddingHorizontal(10).PaddingVertical(3.5f)
                        .Text(title).Bold().FontSize(10f).FontColor(Colors.White);
                    right.Item().PaddingTop(4).AlignRight().Text($"#{docNumPayload}").Bold().FontSize(12.5f).FontColor(theme.Primary);
                    right.Item().AlignRight().Text(dateDisplay).FontSize(8.5f).FontColor(theme.MutedText);
                    if (!string.IsNullOrWhiteSpace(request.StatusBadge))
                    {
                        var (sBg, sFg, sBorder) = ResolveStatusColors(request.StatusTone);
                        right.Item().PaddingTop(3).AlignRight().Background(sBg).Border(0.6f).BorderColor(sBorder)
                            .PaddingHorizontal(6).PaddingVertical(1.5f)
                            .Text(request.StatusBadge.ToUpperInvariant()).Bold().FontSize(7.5f).FontColor(sFg);
                    }
                });
            });
    }

    private static void RenderThermalMetadata(ColumnDescriptor contentCol, SharedDocumentPdfRequest request, PdfThemePalette theme)
    {
        var isMinimal = theme.Id == "minimal";
        var box = isMinimal
            ? contentCol.Item().BorderBottom(0.6f).BorderColor("#94a3b8").PaddingBottom(4)
            : contentCol.Item().Background(theme.SoftBg).Border(0.6f).BorderColor(theme.Border).Padding(4.5f);

        box.Column(col =>
        {
            col.Item().Row(topRow =>
            {
                topRow.RelativeItem().Text(request.PartyName).Bold().FontSize(8f).FontColor(theme.Text);
                if (!string.IsNullOrWhiteSpace(request.StatusBadge))
                {
                    var (sBg, sFg, sBorder) = ResolveStatusColors(request.StatusTone);
                    topRow.AutoItem().Background(sBg).Border(0.5f).BorderColor(sBorder).PaddingHorizontal(4).PaddingVertical(1)
                        .Text(request.StatusBadge.ToUpperInvariant()).Bold().FontSize(6f).FontColor(sFg);
                }
            });

            if (!string.IsNullOrWhiteSpace(request.PartyPhone))
                col.Item().Text($"Ph: {request.PartyPhone}").FontSize(6.5f).FontColor(theme.MutedText);
            if (!string.IsNullOrWhiteSpace(request.PartyAddress))
                col.Item().Text(request.PartyAddress).FontSize(6.3f).FontColor(theme.MutedText);

            var allMeta = new List<PdfKeyValuePair>(request.MetaDetails);
            if (!string.IsNullOrWhiteSpace(request.DoctorName) && !allMeta.Any(m => m.Label.Equals("Doctor", StringComparison.OrdinalIgnoreCase)))
                allMeta.Add(new PdfKeyValuePair("Dr", request.DoctorName));
            if (!string.IsNullOrWhiteSpace(request.PatientName) && !allMeta.Any(m => m.Label.Equals("Patient", StringComparison.OrdinalIgnoreCase)))
                allMeta.Add(new PdfKeyValuePair("Pt", request.PatientName));

            if (allMeta.Count > 0)
            {
                col.Item().PaddingTop(3).BorderTop(0.4f).BorderColor("#cbd5e1").PaddingTop(2.5f).Table(mt =>
                {
                    mt.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                    });

                    for (var i = 0; i < allMeta.Count; i++)
                    {
                        var kv = allMeta[i];
                        var cell = mt.Cell().PaddingVertical(0.8f);
                        if (i % 2 == 1) cell = cell.AlignRight();
                        cell.Text(t =>
                        {
                            t.Span($"{kv.Label}: ").FontSize(6.3f).FontColor(theme.MutedText);
                            t.Span(kv.Value).Bold().FontSize(6.4f).FontColor(theme.Text);
                        });
                    }
                });
            }
        });
    }

    private static void RenderA4Metadata(ColumnDescriptor contentCol, SharedDocumentPdfRequest request, PdfThemePalette theme)
    {
        var isMinimal = theme.Id == "minimal";
        var isDetailed = theme.Id == "detailed";
        var isCompact = theme.Id == "compact";
        var isBold = theme.Id == "bold";
        var isBarcode = theme.Id == "barcode";

        if (isDetailed)
        {
            contentCol.Item().Row(row =>
            {
                row.Spacing(8);
                // Card 1: Billed To
                row.RelativeItem(1.25f).Background(theme.SoftBg).Border(0.6f).BorderColor(theme.Border)
                    .BorderLeft(3).BorderColor(theme.Primary).Padding(7).Column(c1 =>
                    {
                        c1.Item().Text(request.PartyHeading.ToUpperInvariant()).Bold().FontSize(7.5f).FontColor(theme.Secondary);
                        c1.Item().PaddingTop(1).Text(request.PartyName).Bold().FontSize(10.5f).FontColor(theme.Text);
                        if (!string.IsNullOrWhiteSpace(request.PartyPhone))
                            c1.Item().Text($"Phone: {request.PartyPhone}").FontSize(8.5f).FontColor(theme.MutedText);
                        if (!string.IsNullOrWhiteSpace(request.PartyAddress))
                            c1.Item().Text(request.PartyAddress).FontSize(8.2f).FontColor(theme.MutedText);
                    });

                // Card 2: Invoice Information
                row.RelativeItem(1f).Background("#f8fafc").Border(0.6f).BorderColor("#e2e8f0")
                    .BorderLeft(3).BorderColor(theme.Accent).Padding(7).Column(c2 =>
                    {
                        c2.Item().Text("INVOICE INFORMATION").Bold().FontSize(7.5f).FontColor(theme.MutedText);
                        foreach (var kv in request.MetaDetails.Take(4))
                        {
                            c2.Item().PaddingTop(1).Text(t =>
                            {
                                t.Span($"{kv.Label}: ").FontSize(8.3f).FontColor(theme.MutedText);
                                t.Span(kv.Value).Bold().FontSize(8.4f).FontColor(theme.Text);
                            });
                        }
                        if (!string.IsNullOrWhiteSpace(request.StatusBadge))
                        {
                            var (sBg, sFg, sBorder) = ResolveStatusColors(request.StatusTone);
                            c2.Item().PaddingTop(3).Background(sBg).Border(0.5f).BorderColor(sBorder).PaddingHorizontal(5).PaddingVertical(1)
                                .Text(request.StatusBadge.ToUpperInvariant()).Bold().FontSize(7.2f).FontColor(sFg);
                        }
                    });

                // Card 3: Clinical / Extra Meta
                row.RelativeItem(1f).Background("#f8fafc").Border(0.6f).BorderColor("#e2e8f0")
                    .BorderLeft(3).BorderColor("#64748b").Padding(7).Column(c3 =>
                    {
                        c3.Item().Text("CLINICAL & ORDER REF").Bold().FontSize(7.5f).FontColor(theme.MutedText);
                        c3.Item().PaddingTop(1).Text(t =>
                        {
                            t.Span("Doctor: ").FontSize(8.3f).FontColor(theme.MutedText);
                            t.Span(string.IsNullOrWhiteSpace(request.DoctorName) ? "—" : request.DoctorName).Bold().FontSize(8.4f);
                        });
                        c3.Item().PaddingTop(1).Text(t =>
                        {
                            t.Span("Patient: ").FontSize(8.3f).FontColor(theme.MutedText);
                            t.Span(string.IsNullOrWhiteSpace(request.PatientName) ? "—" : request.PatientName).Bold().FontSize(8.4f);
                        });
                        foreach (var kv in request.MetaDetails.Skip(4).Take(2))
                        {
                            c3.Item().PaddingTop(1).Text(t =>
                            {
                                t.Span($"{kv.Label}: ").FontSize(8.3f).FontColor(theme.MutedText);
                                t.Span(kv.Value).Bold().FontSize(8.4f);
                            });
                        }
                    });
            });
            return;
        }

        if (isBold)
        {
            contentCol.Item().Row(row =>
            {
                row.Spacing(8);
                row.RelativeItem(1.3f).Background(theme.SoftBg).Border(0.6f).BorderColor(theme.Border).Padding(8).Column(c1 =>
                {
                    c1.Item().Text(request.PartyHeading.ToUpperInvariant()).Bold().FontSize(7.5f).FontColor(theme.Secondary);
                    c1.Item().PaddingTop(1).Text(request.PartyName).Bold().FontSize(11f).FontColor(theme.Text);
                    if (!string.IsNullOrWhiteSpace(request.PartyPhone))
                        c1.Item().Text($"Ph: {request.PartyPhone}").FontSize(8.5f).FontColor(theme.MutedText);
                    if (!string.IsNullOrWhiteSpace(request.PartyAddress))
                        c1.Item().Text(request.PartyAddress).FontSize(8.2f).FontColor(theme.MutedText);
                });

                row.RelativeItem(1.05f).Background("#f8fafc").Border(0.6f).BorderColor("#e2e8f0").Padding(8).Column(c2 =>
                {
                    c2.Item().Text("INVOICE DETAILS").Bold().FontSize(7.5f).FontColor(theme.MutedText);
                    foreach (var kv in request.MetaDetails.Take(4))
                    {
                        c2.Item().PaddingTop(1).Text(t =>
                        {
                            t.Span($"{kv.Label}: ").FontSize(8.3f).FontColor(theme.MutedText);
                            t.Span(kv.Value).Bold().FontSize(8.4f);
                        });
                    }
                    if (!string.IsNullOrWhiteSpace(request.DoctorName))
                    {
                        c2.Item().PaddingTop(1).Text(t =>
                        {
                            t.Span("Doctor: ").FontSize(8.3f).FontColor(theme.MutedText);
                            t.Span(request.DoctorName).Bold().FontSize(8.4f);
                        });
                    }
                });

                var highlightTotal = request.NetTotalHighlight
                    ?? request.Totals.FirstOrDefault(t => t.IsGrandTotal)?.Value
                    ?? "";
                row.RelativeItem(0.95f).Background(theme.Primary).Padding(8).AlignRight().Column(c3 =>
                {
                    c3.Item().AlignRight().Text("NET INVOICE TOTAL").Bold().FontSize(7.5f).FontColor("#a7f3d0");
                    c3.Item().PaddingTop(2).AlignRight().Text(highlightTotal).Bold().FontSize(14f).FontColor(Colors.White);
                    if (!string.IsNullOrWhiteSpace(request.StatusBadge))
                    {
                        var (sBg, sFg, sBorder) = ResolveStatusColors(request.StatusTone);
                        c3.Item().PaddingTop(4).AlignRight().Background(sBg).Border(0.5f).BorderColor(sBorder)
                            .PaddingHorizontal(5).PaddingVertical(1)
                            .Text(request.StatusBadge.ToUpperInvariant()).Bold().FontSize(7.2f).FontColor(sFg);
                    }
                });
            });
            return;
        }

        // Standard, Minimal, Compact, Barcode
        var containerItem = isMinimal
            ? contentCol.Item().BorderBottom(0.8f).BorderColor("#cbd5e1").PaddingBottom(7)
            : contentCol.Item().Background(theme.SoftBg).Border(0.6f).BorderColor(theme.Border)
                .BorderLeft(isCompact ? 0.6f : 3.2f).BorderColor(isBarcode ? theme.Accent : theme.Primary)
                .Padding(isCompact ? 6 : 8);

        containerItem.Row(metaRow =>
        {
            metaRow.RelativeItem(5.5f).Column(pCol =>
            {
                pCol.Item().Text(request.PartyHeading.ToUpperInvariant()).Bold().FontSize(isCompact ? 7.2f : 7.8f).FontColor(theme.MutedText);
                pCol.Item().PaddingTop(1).Text(request.PartyName).Bold().FontSize(isCompact ? 9.8f : 11f).FontColor(theme.Text);
                if (!string.IsNullOrWhiteSpace(request.PartyPhone))
                    pCol.Item().Text($"Phone: {request.PartyPhone}").FontSize(isCompact ? 8f : 8.6f).FontColor(theme.MutedText);
                if (!string.IsNullOrWhiteSpace(request.PartyAddress))
                    pCol.Item().Text(request.PartyAddress).FontSize(isCompact ? 7.8f : 8.4f).FontColor(theme.MutedText);

                var docPt = JoinNonEmpty(" • ",
                    !string.IsNullOrWhiteSpace(request.DoctorName) ? $"Doctor: {request.DoctorName}" : null,
                    !string.IsNullOrWhiteSpace(request.PatientName) ? $"Patient: {request.PatientName}" : null);
                if (!string.IsNullOrEmpty(docPt))
                    pCol.Item().PaddingTop(2).Text(docPt).FontSize(isCompact ? 7.8f : 8.4f).FontColor(theme.Text);
            });

            metaRow.RelativeItem(4.5f).AlignRight().Column(dCol =>
            {
                dCol.Item().AlignRight().Text("INVOICE INFORMATION").Bold().FontSize(isCompact ? 7.2f : 7.8f).FontColor(theme.MutedText);
                foreach (var kv in request.MetaDetails)
                {
                    dCol.Item().AlignRight().Text(txt =>
                    {
                        txt.Span($"{kv.Label}: ").FontColor(theme.MutedText).FontSize(isCompact ? 8f : 8.5f);
                        txt.Span(kv.Value).Bold().FontSize(isCompact ? 8f : 8.5f).FontColor(theme.Text);
                    });
                }
                if ((isCompact || isMinimal || isBarcode) && !string.IsNullOrWhiteSpace(request.StatusBadge))
                {
                    var (sBg, sFg, sBorder) = ResolveStatusColors(request.StatusTone);
                    dCol.Item().PaddingTop(2).AlignRight().Background(sBg).Border(0.5f).BorderColor(sBorder)
                        .PaddingHorizontal(5).PaddingVertical(1)
                        .Text(request.StatusBadge.ToUpperInvariant()).Bold().FontSize(7f).FontColor(sFg);
                }
            });
        });
    }

    private static void RenderItemsTable(ColumnDescriptor contentCol, SharedDocumentPdfRequest request, PdfThemePalette theme, bool isThermal)
    {
        var isMinimal = theme.Id == "minimal";
        var isCompact = theme.Id == "compact";

        contentCol.Item().PaddingTop(isThermal ? 5 : (isCompact ? 6 : 9)).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                foreach (var colDef in request.Columns)
                {
                    cols.RelativeColumn(Math.Max(0.4f, colDef.RelativeWidth));
                }
            });

            table.Header(header =>
            {
                foreach (var colDef in request.Columns)
                {
                    IContainer cell = header.Cell();
                    if (isMinimal)
                    {
                        cell = cell.Background(Colors.White).BorderTop(1.2f).BorderBottom(1.2f).BorderColor(theme.Primary);
                    }
                    else
                    {
                        cell = cell.Background(theme.Primary);
                    }

                    cell = cell.PaddingVertical(isThermal ? 3f : (isCompact ? 4f : 5.5f))
                               .PaddingHorizontal(isThermal ? 2.5f : (isCompact ? 4f : 5.5f));

                    if (colDef.AlignRight) cell = cell.AlignRight();
                    else if (colDef.AlignCenter) cell = cell.AlignCenter();

                    cell.Text(colDef.Header.ToUpperInvariant())
                        .Bold()
                        .FontSize(isThermal ? 6.5f : (isCompact ? 7.6f : 8.2f))
                        .FontColor(isMinimal ? theme.Primary : Colors.White);
                }
            });

            if (request.Rows.Count == 0)
            {
                table.Cell().ColumnSpan((uint)request.Columns.Count)
                    .BorderBottom(0.5f).BorderColor("#e2e8f0")
                    .Padding(8).AlignCenter().Text("No records found.").FontColor(theme.MutedText);
                return;
            }

            for (var rIdx = 0; rIdx < request.Rows.Count; rIdx++)
            {
                var rowVals = request.Rows[rIdx];
                var rowBg = (rIdx % 2 == 1 && !isMinimal) ? "#f8fafc" : "#ffffff";

                for (var cIdx = 0; cIdx < request.Columns.Count; cIdx++)
                {
                    var colDef = request.Columns[cIdx];
                    var rawVal = cIdx < rowVals.Count ? (rowVals[cIdx] ?? "") : "";
                    var cell = table.Cell().Background(rowBg)
                        .BorderBottom(0.5f).BorderColor("#e2e8f0")
                        .PaddingVertical(isThermal ? 3f : (isCompact ? 3.5f : 5f))
                        .PaddingHorizontal(isThermal ? 2.5f : (isCompact ? 4f : 5.5f));

                    if (colDef.AlignRight) cell = cell.AlignRight();
                    else if (colDef.AlignCenter) cell = cell.AlignCenter();

                    var lines = rawVal.Replace("\r\n", "\n").Split('\n');
                    var primarySize = isThermal ? 7.2f : (isCompact ? 8.3f : 9f);
                    var subSize = isThermal ? 5.9f : (isCompact ? 7.2f : 7.6f);

                    var isDiscCol = colDef.IsDiscountColumn || colDef.Header.Contains("Disc", StringComparison.OrdinalIgnoreCase);
                    var isTotCol = colDef.IsTotalColumn || cIdx == request.Columns.Count - 1;
                    var isNameCol = cIdx == 0 || (request.Columns.Count >= 5 && cIdx == 1 && request.Columns[0].Header == "#");

                    cell.Column(cc =>
                    {
                        for (var l = 0; l < lines.Length; l++)
                        {
                            var lineText = lines[l];
                            if (string.IsNullOrWhiteSpace(lineText)) continue;

                            if (l == 0)
                            {
                                var color = theme.Text;
                                var bold = isNameCol || isTotCol;
                                if (isDiscCol)
                                {
                                    var isEmptyDisc = lineText is "—" or "-" or "0%" or "0";
                                    color = isEmptyDisc ? "#94a3b8" : "#b91c1c";
                                    bold = !isEmptyDisc;
                                }
                                var t = cc.Item().Text(lineText).FontSize(primarySize).FontColor(color);
                                if (bold) t.Bold();
                            }
                            else
                            {
                                var subColor = lineText.StartsWith("+", StringComparison.Ordinal) ? "#047857" : theme.MutedText;
                                cc.Item().PaddingTop(0.5f).Text(lineText).FontSize(subSize).FontColor(subColor);
                            }
                        }
                    });
                }
            }
        });
    }

    private static void RenderThermalTotalsAndExtras(ColumnDescriptor contentCol, SharedDocumentPdfRequest request, PdfThemePalette theme)
    {
        var isMinimal = theme.Id == "minimal";

        if (request.Totals.Count > 0)
        {
            contentCol.Item().PaddingTop(5).Table(totTable =>
            {
                totTable.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(6);
                    c.RelativeColumn(4.5f);
                });

                foreach (var tot in request.Totals)
                {
                    var (bg, fg, isBold) = ResolveTotalRowStyle(tot, theme, isMinimal);
                    var padV = tot.IsGrandTotal ? 3.5f : 2f;
                    var fontSz = tot.IsGrandTotal ? 8.5f : 7.1f;

                    var c1 = totTable.Cell().Background(bg).PaddingVertical(padV).PaddingHorizontal(3.5f);
                    var c2 = totTable.Cell().Background(bg).PaddingVertical(padV).PaddingHorizontal(3.5f).AlignRight();

                    if (tot.IsGrandTotal && isMinimal)
                    {
                        c1 = c1.BorderTop(1.2f).BorderBottom(1.2f).BorderColor(theme.Primary);
                        c2 = c2.BorderTop(1.2f).BorderBottom(1.2f).BorderColor(theme.Primary);
                    }
                    else if (!tot.IsGrandTotal)
                    {
                        c1 = c1.BorderBottom(0.4f).BorderColor("#e2e8f0");
                        c2 = c2.BorderBottom(0.4f).BorderColor("#e2e8f0");
                    }

                    var t1 = c1.Text(tot.Label).FontSize(fontSz).FontColor(fg);
                    var t2 = c2.Text(tot.Value).FontSize(fontSz).FontColor(fg).Bold();
                    if (isBold || tot.IsGrandTotal) t1.Bold();
                }
            });
        }

        if (!string.IsNullOrWhiteSpace(request.TotalSavingsText))
        {
            contentCol.Item().PaddingTop(4).Background("#ecfdf5").Border(0.6f).BorderColor("#a7f3d0")
                .PaddingVertical(2.5f).PaddingHorizontal(5).Row(r =>
                {
                    r.RelativeItem().Text("You Saved").Bold().FontSize(7f).FontColor("#065f46");
                    r.AutoItem().Text(request.TotalSavingsText).Bold().FontSize(7.4f).FontColor("#065f46");
                });
        }

        if (!string.IsNullOrWhiteSpace(request.Remarks))
        {
            contentCol.Item().PaddingTop(5).Background("#f8fafc").BorderLeft(2).BorderColor(theme.Primary)
                .PaddingVertical(3).PaddingHorizontal(5).Text(t =>
                {
                    t.Span("Note: ").Bold().FontSize(6.6f).FontColor(theme.Text);
                    t.Span(request.Remarks).FontSize(6.6f).FontColor(theme.MutedText);
                });
        }
    }

    private static void RenderA4BottomSummary(ColumnDescriptor contentCol, SharedDocumentPdfRequest request, PdfThemePalette theme, string docNumPayload)
    {
        var isMinimal = theme.Id == "minimal";
        var isBarcode = theme.Id == "barcode";

        contentCol.Item().PaddingTop(10).Row(row =>
        {
            // Left column: Remarks + Total Discount Savings Pill + Barcode Verification Strip
            row.RelativeItem(5.4f).PaddingRight(12).Column(leftCol =>
            {
                if (!string.IsNullOrWhiteSpace(request.Remarks))
                {
                    leftCol.Item().Background("#f8fafc").Border(0.6f).BorderColor("#e2e8f0").Padding(7).Column(rc =>
                    {
                        rc.Item().Text("INVOICE REMARKS").Bold().FontSize(7.4f).FontColor(theme.MutedText);
                        rc.Item().PaddingTop(2).Text(request.Remarks).FontSize(8.5f).FontColor(theme.Text);
                    });
                }

                if (!string.IsNullOrWhiteSpace(request.TotalSavingsText))
                {
                    leftCol.Item().PaddingTop(string.IsNullOrWhiteSpace(request.Remarks) ? 0 : 7)
                        .Background("#ecfdf5").Border(0.8f).BorderColor("#a7f3d0").PaddingVertical(5.5f).PaddingHorizontal(9)
                        .Row(sr =>
                        {
                            sr.RelativeItem().Text("Total Discount Savings").Bold().FontSize(8.6f).FontColor("#065f46");
                            sr.AutoItem().Text(request.TotalSavingsText).Bold().FontSize(9.5f).FontColor("#065f46");
                        });
                }

                if (isBarcode)
                {
                    var qrSvg = PrintTemplateRenderer.GenerateQrSvg($"{request.DocumentType}:{docNumPayload}", 48);
                    leftCol.Item().PaddingTop(8).Background("#fafafa").Border(0.7f).BorderColor("#a1a1aa").Padding(7).Row(vr =>
                    {
                        vr.RelativeItem().AlignMiddle().Column(vc =>
                        {
                            vc.Item().Text("DOCUMENT VERIFICATION STRIP").Bold().FontSize(7.2f).FontColor(theme.Secondary);
                            vc.Item().PaddingTop(2).Text($"REF: {docNumPayload} • ITEMS: {request.Rows.Count}").Bold().FontSize(8.5f).FontColor(theme.Primary);
                            vc.Item().Text("Scan QR / Barcode to verify authenticity").FontSize(7.2f).FontColor(theme.MutedText);
                        });
                        vr.AutoItem().Width(40).Height(40).Svg(qrSvg);
                    });
                }
            });

            // Right column: Structured Totals & Payment Summary Table
            if (request.Totals.Count > 0)
            {
                row.RelativeItem(4.6f).Border(isMinimal ? 0f : 0.6f).BorderColor("#cbd5e1").Table(totTable =>
                {
                    totTable.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(5.8f);
                        c.RelativeColumn(4.2f);
                    });

                    foreach (var tot in request.Totals)
                    {
                        var (bg, fg, isBold) = ResolveTotalRowStyle(tot, theme, isMinimal);
                        var padV = tot.IsGrandTotal ? 6f : 4f;
                        var fontSz = tot.IsGrandTotal ? 10.5f : 8.8f;

                        var c1 = totTable.Cell().Background(bg).PaddingVertical(padV).PaddingHorizontal(7);
                        var c2 = totTable.Cell().Background(bg).PaddingVertical(padV).PaddingHorizontal(7).AlignRight();

                        if (tot.IsGrandTotal && isMinimal)
                        {
                            c1 = c1.BorderTop(1.5f).BorderBottom(1.5f).BorderColor(theme.Primary);
                            c2 = c2.BorderTop(1.5f).BorderBottom(1.5f).BorderColor(theme.Primary);
                        }
                        else if (!tot.IsGrandTotal)
                        {
                            c1 = c1.BorderBottom(0.5f).BorderColor("#e2e8f0");
                            c2 = c2.BorderBottom(0.5f).BorderColor("#e2e8f0");
                        }

                        var t1 = c1.Text(tot.Label).FontSize(fontSz).FontColor(fg);
                        var t2 = c2.Text(tot.Value).FontSize(fontSz).FontColor(fg).Bold();
                        if (isBold || tot.IsGrandTotal) t1.Bold();
                    }
                });
            }
        });
    }

    private static void RenderFbrBlock(ColumnDescriptor contentCol, SharedDocumentPdfRequest request, bool isThermal)
    {
        var fbrNo = request.FbrInvoiceNumber!.Trim();
        var qrPayload = !string.IsNullOrWhiteSpace(request.FbrQrData) ? request.FbrQrData! : fbrNo;
        var qrSvg = PrintTemplateRenderer.GenerateQrSvg(qrPayload, isThermal ? 64 : 72);

        if (isThermal)
        {
            contentCol.Item().PaddingTop(6).Border(1).BorderColor("#0f172a").Padding(5).Column(fbrCol =>
            {
                fbrCol.Item().Background("#0f172a").Padding(2).AlignCenter()
                    .Text("FBR POS INVOICING SYSTEM").Bold().FontSize(6.5f).FontColor(Colors.White);
                fbrCol.Item().PaddingTop(3).AlignCenter().Text($"FBR #: {fbrNo}").Bold().FontSize(7.2f);
                fbrCol.Item().PaddingVertical(3).AlignCenter().Width(48).Height(48).Svg(qrSvg);
                fbrCol.Item().AlignCenter().Text("Verify via FBR Tax Asaan App or SMS 9966").FontSize(5.8f);
            });
            return;
        }

        contentCol.Item().PaddingTop(10).Background("#f8fafc").Border(1).BorderColor("#0f172a").Padding(8).Row(fbrRow =>
        {
            fbrRow.RelativeItem().AlignMiddle().Column(fLeft =>
            {
                fLeft.Item().Background("#0f172a").PaddingHorizontal(6).PaddingVertical(2)
                    .Text("FBR POS DIGITAL INVOICING").Bold().FontSize(7.8f).FontColor(Colors.White);
                fLeft.Item().PaddingTop(3).Text($"FBR Invoice #: {fbrNo}").Bold().FontSize(10f);
                var meta = JoinNonEmpty(" • ",
                    !string.IsNullOrWhiteSpace(request.Setting.FbrPosId) ? $"POS ID: {request.Setting.FbrPosId}" : null,
                    !string.IsNullOrWhiteSpace(request.Setting.FbrSellerNtn) ? $"NTN: {request.Setting.FbrSellerNtn}" : null,
                    request.FbrPosFee > 0 ? $"Service Fee: Rs. {request.FbrPosFee:N2}" : null);
                if (!string.IsNullOrEmpty(meta))
                    fLeft.Item().PaddingTop(1).Text(meta).FontSize(8f).FontColor("#475569");
                fLeft.Item().PaddingTop(2).Text("Verify invoice via FBR Tax Asaan App or SMS to 9966").FontSize(7.8f).FontColor("#334155");
            });
            fbrRow.AutoItem().Background(Colors.White).Border(0.8f).BorderColor("#0f172a").Padding(2).Width(50).Height(50).Svg(qrSvg);
        });
    }

    private static void RenderThermalFooter(ColumnDescriptor footCol, SharedDocumentPdfRequest request, PdfThemePalette theme)
    {
        var isMinimal = theme.Id == "minimal";
        footCol.Item().LineHorizontal(0.6f).LineColor(theme.Secondary);

        if (!string.IsNullOrWhiteSpace(request.Setting.Note))
        {
            footCol.Item().PaddingTop(4).Background("#f8fafc").Padding(4).AlignCenter()
                .Text(request.Setting.Note).FontSize(6.3f).FontColor("#334155");
        }

        footCol.Item().PaddingTop(4).AlignCenter().Text("Thank you for your business!").Bold().FontSize(7f).FontColor(theme.Primary);

        // Embedded Developer Credit Box
        footCol.Item().PaddingTop(4)
            .Background(isMinimal ? "#ffffff" : theme.SoftBg)
            .Border(0.6f).BorderColor(isMinimal ? "#0f172a" : theme.Border)
            .BorderTop(1.6f).BorderColor(theme.Primary)
            .PaddingVertical(3.5f).PaddingHorizontal(4)
            .Column(credit =>
            {
                credit.Item().AlignCenter().Text(DeveloperCreditTitle).Bold().FontSize(6.6f).FontColor(theme.Primary);
                credit.Item().AlignCenter().Text(DeveloperCreditPhone).Bold().FontSize(6.8f).FontColor(theme.Secondary);
            });

        footCol.Item().PaddingTop(3).AlignCenter().Text(txt =>
        {
            txt.Span($"Printed {DateTime.Now:dd MMM yyyy, hh:mm tt} • Page ").FontSize(5.5f).FontColor("#64748b");
            txt.CurrentPageNumber().FontSize(5.5f).FontColor("#64748b");
        });
    }

    private static void RenderA4Footer(ColumnDescriptor footCol, SharedDocumentPdfRequest request, PdfThemePalette theme)
    {
        var isMinimal = theme.Id == "minimal";
        var isBold = theme.Id == "bold";
        var isDetailed = theme.Id == "detailed";

        if (!string.IsNullOrWhiteSpace(request.Setting.Note))
        {
            footCol.Item().PaddingBottom(6)
                .Background(isMinimal ? "#ffffff" : "#fffbeb")
                .Border(0.6f).BorderColor(isMinimal ? "#cbd5e1" : "#fde68a")
                .BorderLeft(2.5f).BorderColor(isMinimal ? "#334155" : "#d97706")
                .PaddingVertical(4.5f).PaddingHorizontal(8)
                .Text(t =>
                {
                    t.Span("Terms & Policy: ").Bold().FontSize(8f).FontColor("#0f172a");
                    t.Span(request.Setting.Note).FontSize(8f).FontColor("#334155");
                });
        }

        if (isDetailed)
        {
            footCol.Item().PaddingBottom(7).PaddingTop(4).Row(sigRow =>
            {
                sigRow.RelativeItem().Column(sc =>
                {
                    sc.Item().PaddingTop(10).BorderTop(0.6f).BorderColor("#94a3b8").PaddingTop(2)
                        .AlignCenter().Text("Customer Signature").FontSize(7.5f).FontColor("#64748b");
                });
                sigRow.ConstantItem(36);
                sigRow.RelativeItem().AlignBottom().AlignCenter()
                    .Text($"Thank you for trusting {request.Setting.StoreName}!").Italic().FontSize(8.2f).FontColor("#64748b");
                sigRow.ConstantItem(36);
                sigRow.RelativeItem().Column(sc =>
                {
                    sc.Item().PaddingTop(10).BorderTop(0.6f).BorderColor("#94a3b8").PaddingTop(2)
                        .AlignCenter().Text("Authorized Signature / Stamp").FontSize(7.5f).FontColor("#64748b");
                });
            });
        }

        // Embedded Developer Credit Strip
        footCol.Item()
            .BorderTop(isMinimal ? 1.2f : 1.8f).BorderColor(theme.Primary)
            .Background(isBold ? theme.Primary : (isMinimal ? "#ffffff" : theme.SoftBg))
            .PaddingVertical(6).PaddingHorizontal(10)
            .Row(fRow =>
            {
                fRow.RelativeItem().AlignMiddle().Text(txt =>
                {
                    var metaColor = isBold ? "#d1fae5" : "#64748b";
                    txt.Span($"Generated {DateTime.Now:dd MMM yyyy, hh:mm tt} • Page ").FontSize(7.6f).FontColor(metaColor);
                    txt.CurrentPageNumber().FontSize(7.6f).FontColor(metaColor);
                    txt.Span(" of ").FontSize(7.6f).FontColor(metaColor);
                    txt.TotalPages().FontSize(7.6f).FontColor(metaColor);
                });

                fRow.AutoItem().AlignMiddle().Row(creditRow =>
                {
                    creditRow.AutoItem().AlignMiddle().PaddingRight(6)
                        .Text(DeveloperCreditTitle).Bold().FontSize(8.4f).FontColor(isBold ? Colors.White : theme.Primary);
                    creditRow.AutoItem().AlignMiddle()
                        .Background(isBold ? theme.Accent : theme.Primary)
                        .PaddingHorizontal(6).PaddingVertical(1.5f)
                        .Text(DeveloperCreditPhone).Bold().FontSize(7.8f).FontColor(isBold ? "#052e16" : Colors.White);
                });
            });
    }

    private static (string Bg, string Fg, bool IsBold) ResolveTotalRowStyle(PdfKeyValuePair tot, PdfThemePalette theme, bool isMinimal)
    {
        if (tot.IsGrandTotal)
        {
            return isMinimal
                ? ("#f8fafc", theme.Primary, true)
                : (theme.Primary, "#ffffff", true);
        }

        return (tot.Tone ?? "").Trim().ToLowerInvariant() switch
        {
            "discount" => ("#ffffff", "#b91c1c", false),
            "success" => ("#f0fdf4", "#166534", true),
            "warning" => ("#fff7ed", "#c2410c", true),
            "danger" => ("#fef2f2", "#b91c1c", true),
            _ => ("#ffffff", theme.Text, false)
        };
    }

    private static string JoinNonEmpty(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));

    /// <summary>
    /// Generates any of the 18 Analytical Report PDFs (`cost-of-sale`, `cost-of-sale-detail`, `purchase-detail`, `sale-detail`,
    /// `current-stock`, `stock-adjustment`, `product-comprehensive`, `near-expiry-product`, `purchase-returns`, `sale-returns`,
    /// `purchase-summary`, `outstanding-aging`, `overdue-work-queue`, `reorder`, `net-profit`, `tax-return`, `narcotics`, `financial-statements`).
    /// </summary>
    public byte[] GenerateAnalyticalReportPdf(AnalyticalReportPdfRequest request)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var reportKey = (request.ReportType ?? "cost-of-sale").Trim().ToLowerInvariant();
        var title = SupportedAnalyticalReports.TryGetValue(reportKey, out var mappedTitle) && string.IsNullOrWhiteSpace(request.ReportTitle)
            ? mappedTitle
            : (string.IsNullOrWhiteSpace(request.ReportTitle) ? (mappedTitle ?? "Analytical Report") : request.ReportTitle);

        var isLandscape = request.Landscape || reportKey is "narcotics" or "product-comprehensive" or "purchase-detail" or "sale-detail" or "cost-of-sale-detail";
        var periodText = (!string.IsNullOrWhiteSpace(request.FromDate) || !string.IsNullOrWhiteSpace(request.ToDate))
            ? $"Period: {request.FromDate ?? "Beginning"} to {request.ToDate ?? DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"
            : $"As of {DateTime.Now:dd MMM yyyy}";

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(isLandscape ? PageSizes.A4.Landscape() : PageSizes.A4.Portrait());
                page.Margin(12, Unit.Millimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9f).FontColor("#0f172a"));

                page.Header().BorderBottom(2).BorderColor("#0f172a").PaddingBottom(6).Row(row =>
                {
                    row.RelativeItem(6).Column(left =>
                    {
                        left.Item().Text(request.Setting.StoreName).Bold().FontSize(16).FontColor("#0f172a");
                        if (!string.IsNullOrWhiteSpace(request.Setting.Address))
                            left.Item().Text(request.Setting.Address).FontSize(8.5f).FontColor("#475569");
                        if (!string.IsNullOrWhiteSpace(request.Setting.LicenseNumber))
                            left.Item().Text($"Drug / Trade License #: {request.Setting.LicenseNumber}").FontSize(8.5f).FontColor("#475569");
                    });

                    row.RelativeItem(6).AlignRight().Column(right =>
                    {
                        right.Item().AlignRight().Text(title.ToUpperInvariant()).Bold().FontSize(12.5f).FontColor("#1e3a8a");
                        if (!string.IsNullOrWhiteSpace(request.Subtitle))
                            right.Item().AlignRight().Text(request.Subtitle).FontSize(8.5f).FontColor("#475569");
                        right.Item().AlignRight().Text(periodText).Bold().FontSize(9f).FontColor("#334155");
                    });
                });

                page.Content().PaddingTop(8).Column(col =>
                {
                    // KPI Summary Strip
                    if (request.SummaryCards.Count > 0)
                    {
                        col.Item().PaddingBottom(8).Table(kpiTable =>
                        {
                            var count = Math.Min(6, request.SummaryCards.Count);
                            kpiTable.ColumnsDefinition(c =>
                            {
                                for (var i = 0; i < count; i++) c.RelativeColumn();
                            });

                            foreach (var card in request.SummaryCards.Take(count))
                            {
                                kpiTable.Cell().Padding(2).Background("#f8fafc").Border(0.75f).BorderColor("#cbd5e1").Padding(6).Column(kc =>
                                {
                                    kc.Item().Text(card.Label.ToUpperInvariant()).Bold().FontSize(7.2f).FontColor("#64748b");
                                    kc.Item().PaddingTop(2).Text(card.Value).Bold().FontSize(10.5f).FontColor("#0f172a");
                                });
                            }
                        });
                    }

                    // Main Report Data Table
                    var columns = request.Columns.Count > 0
                        ? request.Columns
                        : [
                            new PdfTableColumnDef { Header = "#", RelativeWidth = 0.6f, AlignCenter = true },
                            new PdfTableColumnDef { Header = "Description / Particular", RelativeWidth = 3f },
                            new PdfTableColumnDef { Header = "Reference / Code", RelativeWidth = 1.5f },
                            new PdfTableColumnDef { Header = "Quantity / Metric", RelativeWidth = 1.2f, AlignRight = true },
                            new PdfTableColumnDef { Header = "Amount (Rs.)", RelativeWidth = 1.5f, AlignRight = true }
                        ];

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            foreach (var colDef in columns)
                            {
                                c.RelativeColumn(Math.Max(0.4f, colDef.RelativeWidth));
                            }
                        });

                        table.Header(header =>
                        {
                            foreach (var colDef in columns)
                            {
                                var hCell = header.Cell().Background("#0f172a").Border(0.5f).BorderColor("#0f172a").PaddingVertical(4.5f).PaddingHorizontal(4f);
                                if (colDef.AlignRight) hCell = hCell.AlignRight();
                                else if (colDef.AlignCenter) hCell = hCell.AlignCenter();
                                hCell.Text(colDef.Header).Bold().FontSize(8f).FontColor(Colors.White);
                            }
                        });

                        if (request.Rows.Count == 0)
                        {
                            table.Cell().ColumnSpan((uint)columns.Count).Border(0.5f).BorderColor("#cbd5e1").Padding(8)
                                .AlignCenter().Text("No matching report records found for the selected criteria.").FontColor("#64748b");
                        }
                        else
                        {
                            for (var rIdx = 0; rIdx < request.Rows.Count; rIdx++)
                            {
                                var rowVals = request.Rows[rIdx];
                                var bg = rIdx % 2 == 1 ? "#f8fafc" : "#ffffff";

                                for (var cIdx = 0; cIdx < columns.Count; cIdx++)
                                {
                                    var colDef = columns[cIdx];
                                    var val = cIdx < rowVals.Count ? (rowVals[cIdx] ?? "") : "";
                                    var dCell = table.Cell().Background(bg).Border(0.5f).BorderColor("#cbd5e1").PaddingVertical(3.5f).PaddingHorizontal(4f);
                                    if (colDef.AlignRight) dCell = dCell.AlignRight();
                                    else if (colDef.AlignCenter) dCell = dCell.AlignCenter();
                                    dCell.Text(val).FontSize(8.2f);
                                }
                            }
                        }
                    });

                    // Footer Totals Summary
                    if (request.FooterTotals.Count > 0)
                    {
                        col.Item().PaddingTop(8).AlignRight().Width(240).Table(totTable =>
                        {
                            totTable.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(6);
                                c.RelativeColumn(5);
                            });

                            foreach (var ft in request.FooterTotals)
                            {
                                var bg = ft.IsGrandTotal ? "#0f172a" : "#f8fafc";
                                var fg = ft.IsGrandTotal ? "#ffffff" : "#0f172a";
                                totTable.Cell().Background(bg).Border(0.5f).BorderColor("#cbd5e1").Padding(4).Text(ft.Label).Bold().FontSize(8.5f).FontColor(fg);
                                totTable.Cell().Background(bg).Border(0.5f).BorderColor("#cbd5e1").Padding(4).AlignRight().Text(ft.Value).Bold().FontSize(8.5f).FontColor(fg);
                            }
                        });
                    }
                });

                page.Footer().PaddingTop(4).BorderTop(0.5f).BorderColor("#cbd5e1").Row(fRow =>
                {
                    fRow.RelativeItem().Text($"{DeveloperCreditTitle} • {DeveloperCreditPhone} • Printed {DateTime.Now:dd MMM yyyy HH:mm}").FontSize(7.5f).FontColor("#64748b");
                    fRow.AutoItem().AlignRight().Text(txt =>
                    {
                        txt.Span("Page ").FontSize(7.5f).FontColor("#64748b");
                        txt.CurrentPageNumber().FontSize(7.5f).FontColor("#64748b");
                        txt.Span(" of ").FontSize(7.5f).FontColor("#64748b");
                        txt.TotalPages().FontSize(7.5f).FontColor("#64748b");
                    });
                });
            });
        });

        return doc.GeneratePdf();
    }

    private static bool IsValidRasterImageBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length < 12) return false;
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true;
        // JPEG: FF D8 FF
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true;
        // GIF: 47 49 46 38
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x38) return true;
        // WebP: RIFF....WEBP
        if (bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return true;
        return false;
    }

    private static byte[]? TryLoadLogoBytes(string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return null;
        try
        {
            var trimmed = logoUrl.Trim();
            if (trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                var comma = trimmed.IndexOf(',');
                if (comma > 0)
                {
                    var decoded = Convert.FromBase64String(trimmed[(comma + 1)..]);
                    return IsValidRasterImageBytes(decoded) ? decoded : null;
                }
                return null;
            }

            var rel = trimmed;
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)) rel = uri.AbsolutePath;
            rel = rel.Replace('\\', '/').TrimStart('/');
            if (rel.StartsWith("storage/", StringComparison.OrdinalIgnoreCase)) rel = rel[8..];
            if (string.IsNullOrWhiteSpace(rel) || rel.Contains("..")) return null;

            var appRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH")
                ?? Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT")
                ?? Directory.GetCurrentDirectory();
            var allowedRoots = new[]
            {
                Path.GetFullPath(Path.Combine(appRoot, "storage", "app", "public")),
                Path.GetFullPath(Path.Combine(appRoot, "public", "storage"))
            };
            foreach (var root in allowedRoots)
            {
                var rootWithSep = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var candidate = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
                if (candidate.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                {
                    var bytes = File.ReadAllBytes(candidate);
                    return IsValidRasterImageBytes(bytes) ? bytes : null;
                }
            }
        }
        catch { }
        return null;
    }
}

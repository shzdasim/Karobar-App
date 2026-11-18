using System;
using System.Collections.Generic;
using System.Globalization;
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
}

public sealed class PdfKeyValuePair
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";
    public bool IsGrandTotal { get; set; }

    public PdfKeyValuePair() { }
    public PdfKeyValuePair(string label, string value, bool isGrandTotal = false)
    {
        Label = label;
        Value = value;
        IsGrandTotal = isGrandTotal;
    }
}

public sealed class SharedDocumentPdfRequest
{
    public string DocumentType { get; set; } = "sale_invoice";
    public string DocumentTitle { get; set; } = "SALE INVOICE";
    public string DocumentNumber { get; set; } = "INV-001";
    public string DocumentDate { get; set; } = DateTime.Now.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
    public string PaperSize { get; set; } = "a4"; // "a4" | "thermal"
    public string PdfDesign { get; set; } = "standard"; // "standard" | "minimal" | "detailed" | "compact" | "bold" | "barcode"
    public PrintSettingViewModel Setting { get; set; } = new();
    public string PartyHeading { get; set; } = "Billed To";
    public string PartyName { get; set; } = "Walk-in Customer";
    public string? PartyPhone { get; set; }
    public string? PartyAddress { get; set; }
    public List<PdfKeyValuePair> MetaDetails { get; set; } = [];
    public List<PdfTableColumnDef> Columns { get; set; } = [];
    public List<List<string>> Rows { get; set; } = [];
    public List<PdfKeyValuePair> Totals { get; set; } = [];
    public string? Remarks { get; set; }
    public string? FbrInvoiceNumber { get; set; }
    public string? FbrQrData { get; set; }
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
/// Generates all 15 Shared Document PDFs (`resources/views/pdf/*`) and all 18 Analytical Report PDFs (`resources/views/reports/*`)
/// using QuestPDF with 6 design themes (`standard`, `minimal`, `detailed`, `compact`, `bold`, `barcode`) and A4 / Thermal (58x80mm) page sizes.
/// </summary>
public sealed class PdfDocumentGenerator
{
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
    }

    private static (string PrimaryHex, string SecondaryHex, string AccentBgHex, string TextHex) ResolveThemeColors(string? design)
    {
        return (design ?? "standard").Trim().ToLowerInvariant() switch
        {
            "minimal" => ("#334155", "#64748b", "#ffffff", "#0f172a"),
            "detailed" => ("#1e40af", "#1e3a8a", "#eff6ff", "#0f172a"),
            "compact" => ("#334155", "#475569", "#f8fafc", "#0f172a"),
            "bold" => ("#064e3b", "#047857", "#ecfdf5", "#0f172a"),
            "barcode" => ("#27272a", "#3f3f46", "#fafafa", "#18181b"),
            _ => ("#0f172a", "#334155", "#f8fafc", "#1e293b")
        };
    }

    /// <summary>
    /// Generates any of the 15 Shared Document PDFs (`sale_invoice`, `sale_return`, `purchase_invoice`, `purchase_return`,
    /// `purchase_order`, `customer_ledger`, `supplier_ledger`, `payment_voucher` / `payment`, `cash_drawer_z_report` / `cash_drawer_session`,
    /// `delivery_challan`, `quotation`, `regular_refill_reminder`, `stock_disposal`, `stocktake_session`).
    /// </summary>
    public byte[] GenerateSharedDocumentPdf(SharedDocumentPdfRequest request)
    {
        QuestPDF.Settings.License = LicenseType.Community;

        var isThermal = string.Equals(request.PaperSize, "thermal", StringComparison.OrdinalIgnoreCase);
        var (primaryHex, secondaryHex, accentBgHex, textHex) = ResolveThemeColors(request.PdfDesign);
        var title = SupportedSharedDocuments.TryGetValue(request.DocumentType, out var mappedTitle) && string.IsNullOrWhiteSpace(request.DocumentTitle)
            ? mappedTitle
            : (string.IsNullOrWhiteSpace(request.DocumentTitle) ? "OFFICIAL DOCUMENT" : request.DocumentTitle);

        var baseFontSize = isThermal ? 6.5f : (request.PdfDesign == "compact" ? 8.8f : 10f);

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                if (isThermal)
                {
                    page.ContinuousSize(58, Unit.Millimetre);
                    page.Margin(2.5f, Unit.Millimetre);
                }
                else
                {
                    page.Size(PageSizes.A4);
                    page.Margin(request.PdfDesign == "compact" ? 9f : 13f, Unit.Millimetre);
                }

                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(baseFontSize).FontColor(textHex));

                // Header
                page.Header().Column(headerCol =>
                {
                    if (isThermal)
                    {
                        headerCol.Item().AlignCenter().Text(request.Setting.StoreName).Bold().FontSize(9.5f).FontColor(primaryHex);
                        if (!string.IsNullOrWhiteSpace(request.Setting.Address))
                            headerCol.Item().AlignCenter().Text(request.Setting.Address).FontSize(6f);
                        if (!string.IsNullOrWhiteSpace(request.Setting.PhoneNumber))
                            headerCol.Item().AlignCenter().Text($"Tel: {request.Setting.PhoneNumber}").FontSize(6f);
                        headerCol.Item().PaddingTop(2).AlignCenter().Background(primaryHex).PaddingHorizontal(5).PaddingVertical(1.5f)
                            .Text($"{title} #{request.DocumentNumber}").Bold().FontSize(6.5f).FontColor(Colors.White);
                        headerCol.Item().AlignCenter().Text($"Date: {request.DocumentDate}").FontSize(5.8f);
                        headerCol.Item().PaddingVertical(2).LineHorizontal(0.6f).LineColor(primaryHex);
                    }
                    else
                    {
                        headerCol.Item().BorderBottom(2).BorderColor(primaryHex).PaddingBottom(8).Row(row =>
                        {
                            row.RelativeItem(6).Column(left =>
                            {
                                left.Item().Text(request.Setting.StoreName).Bold().FontSize(18).FontColor(primaryHex);
                                if (!string.IsNullOrWhiteSpace(request.Setting.Address))
                                    left.Item().Text(request.Setting.Address).FontSize(9.5f).FontColor(secondaryHex);
                                if (!string.IsNullOrWhiteSpace(request.Setting.PhoneNumber))
                                    left.Item().Text($"Phone: {request.Setting.PhoneNumber}").FontSize(9.5f).FontColor(secondaryHex);
                                if (!string.IsNullOrWhiteSpace(request.Setting.LicenseNumber))
                                    left.Item().Text($"License #: {request.Setting.LicenseNumber}").FontSize(9.5f).FontColor(secondaryHex);
                            });

                            row.RelativeItem(4).AlignRight().Column(right =>
                            {
                                right.Item().AlignRight().Background(primaryHex).PaddingHorizontal(10).PaddingVertical(4)
                                    .Text(title).Bold().FontSize(11).FontColor(Colors.White);
                                right.Item().PaddingTop(4).AlignRight().Text($"#{request.DocumentNumber}").Bold().FontSize(11.5f);
                                right.Item().AlignRight().Text($"Date: {request.DocumentDate}").FontSize(9.5f).FontColor(secondaryHex);
                                if (string.Equals(request.PdfDesign, "barcode", StringComparison.OrdinalIgnoreCase))
                                {
                                    right.Item().PaddingTop(3).AlignRight().Border(1).BorderColor("#d4d4d8").Background("#f4f4f5")
                                        .PaddingHorizontal(6).PaddingVertical(2).Text($"REF: {request.DocumentNumber}").FontSize(8.5f);
                                }
                            });
                        });
                    }
                });

                // Content
                page.Content().PaddingTop(isThermal ? 2 : 10).Column(contentCol =>
                {
                    // Party & Metadata Box
                    contentCol.Item().Background(accentBgHex).Border(0.75f).BorderColor("#cbd5e1").Padding(isThermal ? 3 : 8).Row(metaRow =>
                    {
                        metaRow.RelativeItem(6).Column(pCol =>
                        {
                            pCol.Item().Text(request.PartyHeading.ToUpperInvariant()).Bold().FontSize(isThermal ? 5.8f : 8.5f).FontColor(secondaryHex);
                            pCol.Item().Text(request.PartyName).Bold().FontSize(isThermal ? 6.8f : 11f);
                            if (!string.IsNullOrWhiteSpace(request.PartyPhone))
                                pCol.Item().Text($"Phone: {request.PartyPhone}").FontSize(isThermal ? 5.8f : 9f);
                            if (!string.IsNullOrWhiteSpace(request.PartyAddress))
                                pCol.Item().Text($"Address: {request.PartyAddress}").FontSize(isThermal ? 5.8f : 9f);
                        });

                        if (!isThermal && request.MetaDetails.Count > 0)
                        {
                            metaRow.RelativeItem(4).AlignRight().Column(dCol =>
                            {
                                foreach (var kv in request.MetaDetails)
                                {
                                    dCol.Item().AlignRight().Text(txt =>
                                    {
                                        txt.Span($"{kv.Label}: ").FontColor(secondaryHex).FontSize(9f);
                                        txt.Span(kv.Value).Bold().FontSize(9f);
                                    });
                                }
                            });
                        }
                    });

                    // Items Table
                    if (request.Columns.Count > 0)
                    {
                        contentCol.Item().PaddingTop(isThermal ? 3 : 10).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                foreach (var colDef in request.Columns)
                                {
                                    cols.RelativeColumn(Math.Max(0.5f, colDef.RelativeWidth));
                                }
                            });

                            table.Header(header =>
                            {
                                foreach (var colDef in request.Columns)
                                {
                                    var cell = header.Cell().Background(primaryHex).Border(0.5f).BorderColor(primaryHex)
                                        .PaddingVertical(isThermal ? 2f : 5f).PaddingHorizontal(isThermal ? 1.5f : 5f);
                                    if (colDef.AlignRight) cell = cell.AlignRight();
                                    else if (colDef.AlignCenter) cell = cell.AlignCenter();

                                    cell.Text(colDef.Header).Bold().FontSize(isThermal ? 5.8f : 8.8f).FontColor(Colors.White);
                                }
                            });

                            if (request.Rows.Count == 0)
                            {
                                table.Cell().ColumnSpan((uint)request.Columns.Count).Border(0.5f).BorderColor("#cbd5e1")
                                    .Padding(6).AlignCenter().Text("No records found.").FontColor(secondaryHex);
                            }
                            else
                            {
                                for (var rIdx = 0; rIdx < request.Rows.Count; rIdx++)
                                {
                                    var rowVals = request.Rows[rIdx];
                                    var rowBg = (rIdx % 2 == 1 && request.PdfDesign != "minimal") ? "#f8fafc" : "#ffffff";

                                    for (var cIdx = 0; cIdx < request.Columns.Count; cIdx++)
                                    {
                                        var colDef = request.Columns[cIdx];
                                        var val = cIdx < rowVals.Count ? (rowVals[cIdx] ?? "") : "";
                                        var cell = table.Cell().Background(rowBg).Border(0.5f).BorderColor("#cbd5e1")
                                            .PaddingVertical(isThermal ? 1.5f : 4.5f).PaddingHorizontal(isThermal ? 1.5f : 5f);

                                        if (colDef.AlignRight) cell = cell.AlignRight();
                                        else if (colDef.AlignCenter) cell = cell.AlignCenter();

                                        cell.Text(val).FontSize(isThermal ? 6f : 9.5f);
                                    }
                                }
                            }
                        });
                    }

                    // Totals Table
                    if (request.Totals.Count > 0)
                    {
                        contentCol.Item().PaddingTop(isThermal ? 3 : 8).AlignRight().Width(isThermal ? 150 : 250).Table(totTable =>
                        {
                            totTable.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(6);
                                c.RelativeColumn(5);
                            });

                            foreach (var tot in request.Totals)
                            {
                                var bg = tot.IsGrandTotal ? primaryHex : "#ffffff";
                                var fg = tot.IsGrandTotal ? "#ffffff" : textHex;
                                var padV = isThermal ? 1.5f : (tot.IsGrandTotal ? 5f : 3.5f);

                                var c1 = totTable.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e2e8f0").PaddingVertical(padV).PaddingHorizontal(4);
                                var c2 = totTable.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e2e8f0").PaddingVertical(padV).PaddingHorizontal(4).AlignRight();

                                if (tot.IsGrandTotal)
                                {
                                    c1.Text(tot.Label).Bold().FontColor(fg);
                                    c2.Text(tot.Value).Bold().FontColor(fg);
                                }
                                else
                                {
                                    c1.Text(tot.Label).FontColor(fg);
                                    c2.Text(tot.Value).Bold().FontColor(fg);
                                }
                            }
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(request.Remarks))
                    {
                        contentCol.Item().PaddingTop(6).Text($"Remarks: {request.Remarks}").FontSize(isThermal ? 5.8f : 9f).FontColor(secondaryHex);
                    }

                    if (!string.IsNullOrWhiteSpace(request.FbrInvoiceNumber) || request.Setting.FbrEnabled)
                    {
                        var fbrNo = !string.IsNullOrWhiteSpace(request.FbrInvoiceNumber)
                            ? request.FbrInvoiceNumber
                            : $"{request.Setting.FbrPosId ?? "966130"}{DateTime.Now:yyMMddHHmmss}01";

                        contentCol.Item().PaddingTop(8).Border(1).BorderColor("#000000").Padding(5).Column(fbrCol =>
                        {
                            fbrCol.Item().Background("#000000").Padding(2).AlignCenter()
                                .Text("FBR POS INVOICING SYSTEM").Bold().FontSize(isThermal ? 6f : 8f).FontColor(Colors.White);
                            fbrCol.Item().PaddingTop(2).AlignCenter().Text($"FBR Invoice #: {fbrNo}").Bold().FontSize(isThermal ? 6.5f : 9.5f);
                            fbrCol.Item().AlignCenter().Text("Verify via FBR Tax Asaan App or SMS to 9966").FontSize(isThermal ? 5.5f : 7.5f);
                        });
                    }
                });

                // Footer
                page.Footer().PaddingTop(4).BorderTop(0.5f).BorderColor("#cbd5e1").Column(footCol =>
                {
                    if (!string.IsNullOrWhiteSpace(request.Setting.Note))
                    {
                        footCol.Item().AlignCenter().Text(request.Setting.Note).FontSize(isThermal ? 5.5f : 8.5f).FontColor(secondaryHex);
                    }
                    footCol.Item().AlignCenter().Text(txt =>
                    {
                        txt.Span($"Generated on {DateTime.Now:dd MMM yyyy, hh:mm tt} • ").FontSize(isThermal ? 5.2f : 8f).FontColor(secondaryHex);
                        txt.Span("Page ").FontSize(isThermal ? 5.2f : 8f).FontColor(secondaryHex);
                        txt.CurrentPageNumber().FontSize(isThermal ? 5.2f : 8f).FontColor(secondaryHex);
                    });
                });
            });
        });

        return doc.GeneratePdf();
    }

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
                    fRow.RelativeItem().Text($"Karobar-App ERP • {title} • Printed {DateTime.Now:dd MMM yyyy HH:mm}").FontSize(7.5f).FontColor("#64748b");
                    fRow.RelativeItem().AlignRight().Text(txt =>
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
}

using System.Globalization;
using System.Text;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/reports")]
[Authorize]
public class ReportsController : ApiControllerBase
{
    private readonly PdfDocumentGenerator _pdfGenerator;

    public ReportsController(KarobarDb db, PdfDocumentGenerator? pdfGenerator = null) : base(db)
    {
        _pdfGenerator = pdfGenerator ?? new PdfDocumentGenerator();
    }

    private async Task<PrintSettingViewModel> LoadPrintSettingAsync()
    {
        var s = await Db.HasTableAsync("settings")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
            : null;

        return new PrintSettingViewModel
        {
            StoreName = GetNullableStr(s, "store_name") is { Length: > 0 } sn ? sn : "KAROBAR PHARMACY & STORE",
            PhoneNumber = GetNullableStr(s, "phone_number") ?? "",
            Address = GetNullableStr(s, "address") ?? "",
            LicenseNumber = GetNullableStr(s, "license_number") ?? "",
            FbrSellerNtn = GetNullableStr(s, "fbr_seller_ntn") ?? "",
            FbrSellerStrn = GetNullableStr(s, "fbr_seller_strn") ?? ""
        };
    }

    private (IActionResult? err, string from, string to) ValidateStrictDateRange()
    {
        var now = DateTime.Now;
        var rawFrom = Request.Query["from"].FirstOrDefault();
        var rawTo = Request.Query["to"].FirstOrDefault();

        DateTime fromDt;
        if (string.IsNullOrWhiteSpace(rawFrom))
        {
            fromDt = new DateTime(now.Year, now.Month, 1);
        }
        else if (!DateTime.TryParseExact(rawFrom.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out fromDt) &&
                 !DateTime.TryParse(rawFrom.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out fromDt))
        {
            return (Unprocessable("The from date must be a valid YYYY-MM-DD date.", new Dictionary<string, string[]> { ["from"] = new[] { "Invalid date format." } }), "", "");
        }

        DateTime toDt;
        if (string.IsNullOrWhiteSpace(rawTo))
        {
            toDt = now.Date;
        }
        else if (!DateTime.TryParseExact(rawTo.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out toDt) &&
                 !DateTime.TryParse(rawTo.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out toDt))
        {
            return (Unprocessable("The to date must be a valid YYYY-MM-DD date.", new Dictionary<string, string[]> { ["to"] = new[] { "Invalid date format." } }), "", "");
        }

        if (fromDt.Date > toDt.Date)
        {
            return (Unprocessable("The from date must be on or before the to date."), "", "");
        }

        return (null, fromDt.ToString("yyyy-MM-dd"), toDt.ToString("yyyy-MM-dd"));
    }

    private async Task<IActionResult?> AuthorizeReportActionAsync(params string[] permissions)
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(new { message = "Unauthenticated." });
        }

        if (await IsAdminAsync())
        {
            return null;
        }

        if (await CanAsync(permissions))
        {
            return null;
        }

        return Forbidden("This action is unauthorized.");
    }

    private static (string fromStr, string toStr) ResolveDateRange(string? rawFrom, string? rawTo, bool swapIfReversed = true)
    {
        var now = DateTime.Now;
        var defaultFrom = new DateTime(now.Year, now.Month, 1);
        var defaultTo = now;

        var fromDt = DateTime.TryParse(rawFrom, out var f) ? f.Date : defaultFrom.Date;
        var toDt = DateTime.TryParse(rawTo, out var t) ? t.Date : defaultTo.Date;

        if (swapIfReversed && fromDt > toDt)
        {
            (fromDt, toDt) = (toDt, fromDt);
        }

        return (fromDt.ToString("yyyy-MM-dd"), toDt.ToString("yyyy-MM-dd"));
    }

    private IActionResult DeliverPdf(byte[] pdfBytes, string filename)
    {
        var downloadRaw = Request.Query["download"].FirstOrDefault() ?? "";
        var isDownload = downloadRaw == "1" || downloadRaw.Equals("true", StringComparison.OrdinalIgnoreCase);

        if (isDownload)
        {
            return File(pdfBytes, "application/pdf", filename);
        }

        Response.Headers["Content-Disposition"] = $"inline; filename=\"{filename}\"";
        return File(pdfBytes, "application/pdf");
    }

    // =========================================================================
    // 1 & 2. Cost of Sale (/api/reports/cost-of-sale & /pdf)
    // =========================================================================

    [HttpGet("cost-of-sale")]
    public async Task<IActionResult> CostOfSale()
    {
        var authErr = await AuthorizeReportActionAsync("report.cost-of-sale.view");
        if (authErr != null) return authErr;

        var rows = await BuildCostOfSaleRowsAsync();
        return Ok(rows);
    }

    [HttpGet("cost-of-sale/pdf")]
    public async Task<IActionResult> CostOfSalePdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.cost-of-sale.export", "report.cost-of-sale.view");
        if (authErr != null) return authErr;

        var rows = await BuildCostOfSaleRowsAsync();
        var totalSales = rows.Sum(r => ToDouble(r, "total_sales"));
        var totalReturns = rows.Sum(r => ToDouble(r, "sale_return"));
        var totalCogs = rows.Sum(r => ToDouble(r, "cost_of_sales"));
        var netSale = totalSales - totalReturns;
        var gp = netSale - totalCogs;
        var gpPct = netSale > 0 ? (gp / netSale) * 100.0 : 0.0;

        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "cost-of-sale",
            FromDate = Request.Query["from"].FirstOrDefault(),
            ToDate = Request.Query["to"].FirstOrDefault(),
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Days", rows.Count.ToString()),
                new("Total Sales", $"Rs. {totalSales:N2}"),
                new("Returns", $"Rs. {totalReturns:N2}"),
                new("Net Sales", $"Rs. {netSale:N2}"),
                new("COGS", $"Rs. {totalCogs:N2}"),
                new("Gross Profit", $"Rs. {gp:N2} ({gpPct:N1}%)")
            ],
            Columns =
            [
                new() { Header = "Date", RelativeWidth = 1.4f },
                new() { Header = "Gross Sale", RelativeWidth = 1.4f, AlignRight = true },
                new() { Header = "Discounts", RelativeWidth = 1.2f, AlignRight = true },
                new() { Header = "Total Sales", RelativeWidth = 1.4f, AlignRight = true },
                new() { Header = "Returns", RelativeWidth = 1.2f, AlignRight = true },
                new() { Header = "Net Sales", RelativeWidth = 1.4f, AlignRight = true },
                new() { Header = "COGS", RelativeWidth = 1.4f, AlignRight = true },
                new() { Header = "Gross Profit", RelativeWidth = 1.4f, AlignRight = true }
            ],
            Rows = rows.Select(r =>
            {
                var ts = ToDouble(r, "total_sales");
                var sr = ToDouble(r, "sale_return");
                var cs = ToDouble(r, "cost_of_sales");
                var ns = ts - sr;
                return new List<string>
                {
                    GetStr(r, "sale_date"),
                    $"{ToDouble(r, "gross_sale"):N2}",
                    $"{ToDouble(r, "item_discount") + ToDouble(r, "discount_amount"):N2}",
                    $"{ts:N2}",
                    $"{sr:N2}",
                    $"{ns:N2}",
                    $"{cs:N2}",
                    $"{ns - cs:N2}"
                };
            }).ToList(),
            FooterTotals =
            [
                new("Net Sales", $"Rs. {netSale:N2}"),
                new("Total COGS", $"Rs. {totalCogs:N2}"),
                new("Gross Profit", $"Rs. {gp:N2} ({gpPct:N1}%)", isGrandTotal: true)
            ]
        });
        return DeliverPdf(pdf, "cost-of-sale-report.pdf");
    }

    private async Task<List<Dictionary<string, object?>>> BuildCostOfSaleRowsAsync()
    {
        var (fromDateStr, toDateStr) = ResolveDateRange(
            Request.Query["from"].FirstOrDefault(),
            Request.Query["to"].FirstOrDefault(),
            true);

        var invoiceType = (Request.Query["invoice_type"].FirstOrDefault() ?? "all").ToLowerInvariant();
        if (invoiceType != "all" && invoiceType != "credit" && invoiceType != "debit")
        {
            invoiceType = "all";
        }

        var invTypeFilter = invoiceType != "all" ? "AND si.invoice_type = @invoiceType" : "";

        var salesRows = await Db.QueryListAsync($@"
            SELECT DATE(si.date) AS sale_date,
                   SUM(COALESCE(si.gross_amount, 0)) AS gross_sale,
                   SUM(COALESCE(si.item_discount, 0)) AS item_discount,
                   SUM(COALESCE(si.discount_amount, 0)) AS discount_amount,
                   SUM(COALESCE(si.tax_amount, 0)) AS tax_amount,
                   SUM(COALESCE(si.total, 0)) AS total_sales
            FROM sale_invoices si
            WHERE DATE(si.date) BETWEEN @fromDateStr AND @toDateStr
              AND (si.status IS NULL OR si.status != 'reversed')
              {invTypeFilter}
            GROUP BY DATE(si.date)",
            new { fromDateStr, toDateStr, invoiceType });

        var returnTypeFilter = invoiceType == "all"
            ? ""
            : (invoiceType == "debit"
                ? "AND (rsi.invoice_type = @invoiceType OR sr.sale_invoice_id IS NULL)"
                : "AND rsi.invoice_type = @invoiceType");

        var returnRows = await Db.QueryListAsync($@"
            SELECT DATE(sr.date) AS sale_date,
                   SUM(COALESCE(sr.total, 0)) AS sale_return
            FROM sale_returns sr
            LEFT JOIN sale_invoices rsi ON rsi.id = sr.sale_invoice_id
            WHERE DATE(sr.date) BETWEEN @fromDateStr AND @toDateStr
              {returnTypeFilter}
            GROUP BY DATE(sr.date)",
            new { fromDateStr, toDateStr, invoiceType });

        var hasBonus = await Db.HasColumnAsync("sale_invoice_items", "bonus");
        var hasCostSnap = await Db.HasColumnAsync("sale_invoice_items", "cost_price_snapshot");
        var unitsExpr = hasBonus ? "(COALESCE(sii.quantity, 0) + COALESCE(sii.bonus, 0))" : "COALESCE(sii.quantity, 0)";
        var costExpr = hasCostSnap
            ? "COALESCE(sii.cost_price_snapshot, CASE WHEN p.avg_price IS NULL OR p.avg_price = 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END)"
            : "CASE WHEN p.avg_price IS NULL OR p.avg_price = 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END";
        var estExpr = hasCostSnap ? "SUM(CASE WHEN sii.cost_price_snapshot IS NULL THEN 1 ELSE 0 END)" : "COUNT(*)";

        var cogsSalesRows = await Db.QueryListAsync($@"
            SELECT DATE(si.date) AS sale_date,
                   SUM({unitsExpr} * ({costExpr})) AS cogs_sales,
                   {estExpr} AS estimated_items
            FROM sale_invoice_items sii
            INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
            INNER JOIN products p ON p.id = sii.product_id
            WHERE DATE(si.date) BETWEEN @fromDateStr AND @toDateStr
              AND (si.status IS NULL OR si.status != 'reversed')
              {invTypeFilter}
            GROUP BY DATE(si.date)",
            new { fromDateStr, toDateStr, invoiceType });

        var hasRetCostSnap = await Db.HasColumnAsync("sale_return_items", "cost_price_snapshot");
        var retCostExpr = hasRetCostSnap
            ? "COALESCE(sri.cost_price_snapshot, CASE WHEN p.avg_price IS NULL OR p.avg_price = 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END)"
            : "CASE WHEN p.avg_price IS NULL OR p.avg_price = 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END";
        var retEstExpr = hasRetCostSnap ? "SUM(CASE WHEN sri.cost_price_snapshot IS NULL THEN 1 ELSE 0 END)" : "COUNT(*)";

        var cogsReturnRows = await Db.QueryListAsync($@"
            SELECT DATE(sr.date) AS sale_date,
                   SUM(COALESCE(sri.unit_return_quantity, 0) * ({retCostExpr})) AS cogs_returns,
                   {retEstExpr} AS estimated_items
            FROM sale_return_items sri
            INNER JOIN sale_returns sr ON sr.id = sri.sale_return_id
            LEFT JOIN sale_invoices rsi ON rsi.id = sr.sale_invoice_id
            INNER JOIN products p ON p.id = sri.product_id
            WHERE DATE(sr.date) BETWEEN @fromDateStr AND @toDateStr
              {returnTypeFilter}
            GROUP BY DATE(sr.date)",
            new { fromDateStr, toDateStr, invoiceType });

        var salesMap = salesRows.ToDictionary(r => GetStr(r, "sale_date"), r => r);
        var returnsMap = returnRows.ToDictionary(r => GetStr(r, "sale_date"), r => r);
        var csMap = cogsSalesRows.ToDictionary(r => GetStr(r, "sale_date"), r => r);
        var crMap = cogsReturnRows.ToDictionary(r => GetStr(r, "sale_date"), r => r);

        var allDates = salesMap.Keys
            .Concat(returnsMap.Keys)
            .Concat(csMap.Keys)
            .Concat(crMap.Keys)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var result = new List<Dictionary<string, object?>>();
        foreach (var d in allDates)
        {
            salesMap.TryGetValue(d, out var s);
            returnsMap.TryGetValue(d, out var r);
            csMap.TryGetValue(d, out var cs);
            crMap.TryGetValue(d, out var cr);

            var estCount = ToLong(cs, "estimated_items") + ToLong(cr, "estimated_items");
            result.Add(new Dictionary<string, object?>
            {
                ["sale_date"] = d,
                ["cost_is_estimated"] = estCount > 0,
                ["gross_sale"] = Math.Round(ToDouble(s, "gross_sale"), 2),
                ["item_discount"] = Math.Round(ToDouble(s, "item_discount"), 2),
                ["discount_amount"] = Math.Round(ToDouble(s, "discount_amount"), 2),
                ["tax_amount"] = Math.Round(ToDouble(s, "tax_amount"), 2),
                ["total_sales"] = Math.Round(ToDouble(s, "total_sales"), 2),
                ["sale_return"] = Math.Round(ToDouble(r, "sale_return"), 2),
                ["cost_of_sales"] = Math.Round(ToDouble(cs, "cogs_sales") - ToDouble(cr, "cogs_returns"), 2)
            });
        }

        return result;
    }

    // =========================================================================
    // 3 & 4. Cost of Sale Detail (/api/reports/cost-of-sale-detail & /pdf)
    // =========================================================================

    [HttpGet("cost-of-sale-detail")]
    public async Task<IActionResult> CostOfSaleDetail()
    {
        var authErr = await AuthorizeReportActionAsync("report.cost-of-sale-detail.view");
        if (authErr != null) return authErr;

        var data = await BuildCostOfSaleDetailDataAsync();
        return Ok(data);
    }

    [HttpGet("cost-of-sale-detail/pdf")]
    public async Task<IActionResult> CostOfSaleDetailPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.cost-of-sale-detail.export", "report.cost-of-sale-detail.view");
        if (authErr != null) return authErr;

        var data = await BuildCostOfSaleDetailDataAsync();
        var summary = (Dictionary<string, object?>)data["summary"]!;
        var invoices = data.GetValueOrDefault("invoices") as List<Dictionary<string, object?>> ?? new();
        var invoiceId = Request.Query["invoice_id"].FirstOrDefault();

        var tableRows = new List<List<string>>();
        foreach (var inv in invoices)
        {
            var invNo = GetStr(inv, "posted_number");
            var dt = GetStr(inv, "date");
            var cust = GetStr(inv, "customer_name");
            var items = inv.GetValueOrDefault("items") as List<Dictionary<string, object?>> ?? new();
            foreach (var it in items)
            {
                tableRows.Add(
                [
                    dt,
                    invNo,
                    cust,
                    GetStr(it, "product_name"),
                    $"{ToDouble(it, "quantity"):N0}",
                    $"{ToDouble(it, "cost_price"):N2}",
                    $"{ToDouble(it, "total_cost"):N2}",
                    $"{ToDouble(it, "total_sale"):N2}",
                    $"{ToDouble(it, "profit"):N2}"
                ]);
            }
        }

        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "cost-of-sale-detail",
            FromDate = Request.Query["from"].FirstOrDefault(),
            ToDate = Request.Query["to"].FirstOrDefault(),
            Landscape = true,
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Invoices", GetStr(summary, "total_invoices")),
                new("Items", GetStr(summary, "total_items")),
                new("Total Sales", $"Rs. {ToDouble(summary, "total_sales"):N2}"),
                new("Total Cost", $"Rs. {ToDouble(summary, "total_cost"):N2}"),
                new("Total Profit", $"Rs. {ToDouble(summary, "total_profit"):N2}"),
                new("Margin", $"{ToDouble(summary, "profit_margin"):N1}%")
            ],
            Columns =
            [
                new() { Header = "Date", RelativeWidth = 1.2f },
                new() { Header = "Invoice #", RelativeWidth = 1.3f },
                new() { Header = "Customer", RelativeWidth = 1.8f },
                new() { Header = "Product", RelativeWidth = 2.4f },
                new() { Header = "Qty", RelativeWidth = 0.8f, AlignRight = true },
                new() { Header = "Unit Cost", RelativeWidth = 1.1f, AlignRight = true },
                new() { Header = "Total Cost", RelativeWidth = 1.2f, AlignRight = true },
                new() { Header = "Sale", RelativeWidth = 1.2f, AlignRight = true },
                new() { Header = "Profit", RelativeWidth = 1.2f, AlignRight = true }
            ],
            Rows = tableRows,
            FooterTotals =
            [
                new("Total Sales", $"Rs. {ToDouble(summary, "total_sales"):N2}"),
                new("Total Cost", $"Rs. {ToDouble(summary, "total_cost"):N2}"),
                new("Gross Profit", $"Rs. {ToDouble(summary, "total_profit"):N2}", isGrandTotal: true)
            ]
        });

        var filename = $"cost-of-sale-detail-{(string.IsNullOrEmpty(invoiceId) ? "report" : $"invoice-{invoiceId}")}.pdf";
        return DeliverPdf(pdf, filename);
    }

    private async Task<Dictionary<string, object?>> BuildCostOfSaleDetailDataAsync()
    {
        var invoiceId = Request.Query["invoice_id"].FirstOrDefault();
        var customerId = Request.Query["customer_id"].FirstOrDefault();
        var from = Request.Query["from"].FirstOrDefault();
        var to = Request.Query["to"].FirstOrDefault();

        var sql = @"
            SELECT si.*, c.name AS customer_name
            FROM sale_invoices si
            LEFT JOIN customers c ON c.id = si.customer_id
            WHERE (si.status IS NULL OR si.status != 'reversed')";

        var dyn = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(invoiceId))
        {
            sql += " AND si.id = @invoiceId";
            dyn.Add("invoiceId", invoiceId);
        }
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            sql += " AND si.customer_id = @customerId";
            dyn.Add("customerId", customerId);
        }
        if (!string.IsNullOrWhiteSpace(from) && DateTime.TryParse(from, out var fDt))
        {
            sql += " AND DATE(si.date) >= @from";
            dyn.Add("from", fDt.ToString("yyyy-MM-dd"));
        }
        if (!string.IsNullOrWhiteSpace(to) && DateTime.TryParse(to, out var tDt))
        {
            sql += " AND DATE(si.date) <= @to";
            dyn.Add("to", tDt.ToString("yyyy-MM-dd"));
        }

        sql += " ORDER BY si.date DESC, si.id DESC";
        var invoices = await Db.QueryListAsync(sql, dyn);

        var rows = new List<Dictionary<string, object?>>();
        int totalItemsCount = 0;

        foreach (var inv in invoices)
        {
            var invId = ToLong(inv, "id");
            var items = await Db.QueryListAsync(@"
                SELECT sii.*, p.product_code, p.name AS product_name, p.avg_price, p.unit_purchase_price
                FROM sale_invoice_items sii
                LEFT JOIN products p ON p.id = sii.product_id
                WHERE sii.sale_invoice_id = @invId
                ORDER BY sii.id ASC",
                new { invId });

            var formattedItems = new List<Dictionary<string, object?>>();
            double invCost = 0;
            bool anyEstimated = false;

            foreach (var it in items)
            {
                bool isEstimated = !it.TryGetValue("cost_price_snapshot", out var snapObj) || snapObj == null;
                if (isEstimated) anyEstimated = true;

                double costPrice = !isEstimated
                    ? ToDouble(it, "cost_price_snapshot")
                    : (ToDouble(it, "avg_price") > 0 ? ToDouble(it, "avg_price") : ToDouble(it, "unit_purchase_price"));

                var qty = ToDouble(it, "quantity");
                var bonus = (int)ToLong(it, "bonus");
                var salePrice = ToDouble(it, "price");
                var totalCost = (qty + bonus) * costPrice;
                var totalSale = ToDouble(it, "sub_total");
                var profit = totalSale - totalCost;
                invCost += totalCost;

                formattedItems.Add(new Dictionary<string, object?>
                {
                    ["id"] = ToLong(it, "id"),
                    ["product_id"] = ToLong(it, "product_id"),
                    ["product_code"] = GetNullableStr(it, "product_code"),
                    ["product_name"] = GetNullableStr(it, "product_name"),
                    ["pack_size"] = (int)ToLong(it, "pack_size"),
                    ["quantity"] = qty,
                    ["bonus"] = bonus,
                    ["cost_price"] = Math.Round(costPrice, 2),
                    ["cost_is_estimated"] = isEstimated,
                    ["sale_price"] = Math.Round(salePrice, 2),
                    ["item_discount_percentage"] = Math.Round(ToDouble(it, "item_discount_percentage"), 2),
                    ["total_cost"] = Math.Round(totalCost, 2),
                    ["total_sale"] = Math.Round(totalSale, 2),
                    ["profit"] = Math.Round(profit, 2),
                    ["batch_number"] = GetNullableStr(it, "batch_number"),
                    ["expiry"] = GetNullableStr(it, "expiry")
                });
            }

            totalItemsCount += formattedItems.Count;
            var invTotalSale = ToDouble(inv, "total");
            var invProfit = invTotalSale - invCost;
            var margin = invTotalSale > 0 ? Math.Round((invProfit / invTotalSale) * 100, 2) : 0;

            rows.Add(new Dictionary<string, object?>
            {
                ["invoice_id"] = invId,
                ["posted_number"] = GetNullableStr(inv, "posted_number"),
                ["date"] = FormatDateOnly(GetNullableStr(inv, "date")),
                ["customer_name"] = GetNullableStr(inv, "customer_name") ?? "N/A",
                ["invoice_type"] = GetNullableStr(inv, "invoice_type") ?? "debit",
                ["sale_type"] = GetNullableStr(inv, "sale_type") ?? "retail",
                ["gross_amount"] = Math.Round(ToDouble(inv, "gross_amount"), 2),
                ["discount_percentage"] = Math.Round(ToDouble(inv, "discount_percentage"), 2),
                ["discount_amount"] = Math.Round(ToDouble(inv, "discount_amount"), 2),
                ["tax_percentage"] = Math.Round(ToDouble(inv, "tax_percentage"), 2),
                ["tax_amount"] = Math.Round(ToDouble(inv, "tax_amount"), 2),
                ["total"] = Math.Round(invTotalSale, 2),
                ["total_cost"] = Math.Round(invCost, 2),
                ["total_sale"] = Math.Round(invTotalSale, 2),
                ["profit"] = Math.Round(invProfit, 2),
                ["profit_margin"] = margin,
                ["cost_is_estimated"] = anyEstimated,
                ["items"] = formattedItems
            });
        }

        var sumSales = Math.Round(rows.Sum(r => ToDouble(r, "total_sale")), 2);
        var sumCost = Math.Round(rows.Sum(r => ToDouble(r, "total_cost")), 2);
        var sumProfit = Math.Round(rows.Sum(r => ToDouble(r, "profit")), 2);
        var sumMargin = sumSales > 0 ? Math.Round((sumProfit / sumSales) * 100, 2) : 0;

        return new Dictionary<string, object?>
        {
            ["invoices"] = rows,
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_invoices"] = rows.Count,
                ["total_sales"] = sumSales,
                ["total_cost"] = sumCost,
                ["total_profit"] = sumProfit,
                ["total_items"] = totalItemsCount,
                ["profit_margin"] = sumMargin
            }
        };
    }

    // =========================================================================
    // 5 & 6. Purchase Detail (/api/reports/purchase-detail & /pdf)
    // =========================================================================

    [HttpGet("purchase-detail")]
    public async Task<IActionResult> PurchaseDetail()
    {
        var authErr = await AuthorizeReportActionAsync("report.purchase-detail.view");
        if (authErr != null) return authErr;

        var rows = await BuildPurchaseDetailRowsAsync();
        return Ok(rows);
    }

    [HttpGet("purchase-detail/pdf")]
    public async Task<IActionResult> PurchaseDetailPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.purchase-detail.export", "report.purchase-detail.view");
        if (authErr != null) return authErr;

        var rows = await BuildPurchaseDetailRowsAsync();
        var from = Request.Query["from"].FirstOrDefault() ?? "start";
        var to = Request.Query["to"].FirstOrDefault() ?? "today";
        var totalAmt = rows.Sum(r => ToDouble(r, "total_amount"));

        var tableRows = new List<List<string>>();
        foreach (var inv in rows)
        {
            var invDate = GetStr(inv, "invoice_date");
            var posted = GetStr(inv, "posted_number");
            var sup = GetStr(inv, "supplier_name");
            var items = inv.GetValueOrDefault("items") as List<Dictionary<string, object?>> ?? new();
            foreach (var it in items)
            {
                tableRows.Add(
                [
                    invDate,
                    posted,
                    sup,
                    GetStr(it, "product_name"),
                    GetStr(it, "batch"),
                    $"{ToLong(it, "quantity")}",
                    $"{ToDouble(it, "unit_purchase_price"):N2}",
                    $"{ToDouble(it, "sub_total"):N2}"
                ]);
            }
        }

        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "purchase-detail",
            FromDate = Request.Query["from"].FirstOrDefault(),
            ToDate = Request.Query["to"].FirstOrDefault(),
            Landscape = true,
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Invoices", rows.Count.ToString()),
                new("Line Items", tableRows.Count.ToString()),
                new("Total Amount", $"Rs. {totalAmt:N2}")
            ],
            Columns =
            [
                new() { Header = "Date", RelativeWidth = 1.2f },
                new() { Header = "GRN #", RelativeWidth = 1.3f },
                new() { Header = "Supplier", RelativeWidth = 2.0f },
                new() { Header = "Product", RelativeWidth = 2.5f },
                new() { Header = "Batch", RelativeWidth = 1.2f },
                new() { Header = "Qty", RelativeWidth = 0.8f, AlignRight = true },
                new() { Header = "Unit Cost", RelativeWidth = 1.1f, AlignRight = true },
                new() { Header = "Sub Total", RelativeWidth = 1.3f, AlignRight = true }
            ],
            Rows = tableRows,
            FooterTotals =
            [
                new("Total Purchases", $"Rs. {totalAmt:N2}", isGrandTotal: true)
            ]
        });
        return DeliverPdf(pdf, $"purchase-detail-{from}-to-{to}.pdf");
    }

    private async Task<List<Dictionary<string, object?>>> BuildPurchaseDetailRowsAsync()
    {
        var (fromStr, toStr) = ResolveDateRange(
            Request.Query["from"].FirstOrDefault(),
            Request.Query["to"].FirstOrDefault(),
            true);

        var supplierId = Request.Query["supplier_id"].FirstOrDefault();
        var productId = Request.Query["product_id"].FirstOrDefault();

        var sql = @"
            SELECT pi.*, s.name AS supplier_name
            FROM purchase_invoices pi
            LEFT JOIN suppliers s ON s.id = pi.supplier_id
            WHERE DATE(pi.posted_date) BETWEEN @fromStr AND @toStr
              AND (pi.status IS NULL OR pi.status != 'reversed')";

        var dyn = new DynamicParameters(new { fromStr, toStr });
        if (!string.IsNullOrWhiteSpace(supplierId))
        {
            sql += " AND pi.supplier_id = @supplierId";
            dyn.Add("supplierId", supplierId);
        }
        if (!string.IsNullOrWhiteSpace(productId))
        {
            sql += " AND EXISTS (SELECT 1 FROM purchase_invoice_items pii WHERE pii.purchase_invoice_id = pi.id AND pii.product_id = @productId)";
            dyn.Add("productId", productId);
        }

        sql += " ORDER BY pi.posted_date ASC, pi.id ASC";
        var invoices = await Db.QueryListAsync(sql, dyn);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var inv in invoices)
        {
            var invId = ToLong(inv, "id");
            var itemSql = @"
                SELECT pii.*, p.name AS product_name
                FROM purchase_invoice_items pii
                LEFT JOIN products p ON p.id = pii.product_id
                WHERE pii.purchase_invoice_id = @invId";
            var itemParams = new DynamicParameters(new { invId });
            if (!string.IsNullOrWhiteSpace(productId))
            {
                itemSql += " AND pii.product_id = @productId";
                itemParams.Add("productId", productId);
            }

            var items = await Db.QueryListAsync(itemSql, itemParams);
            var mappedItems = items.Select(it => new Dictionary<string, object?>
            {
                ["id"] = ToLong(it, "id"),
                ["product_id"] = ToLong(it, "product_id"),
                ["product_name"] = GetNullableStr(it, "product_name"),
                ["batch"] = GetNullableStr(it, "batch"),
                ["expiry"] = GetNullableStr(it, "expiry"),
                ["pack_quantity"] = (int)ToLong(it, "pack_quantity"),
                ["pack_size"] = (int)ToLong(it, "pack_size"),
                ["unit_quantity"] = (int)ToLong(it, "unit_quantity"),
                ["pack_purchase_price"] = ToDouble(it, "pack_purchase_price"),
                ["unit_purchase_price"] = ToDouble(it, "unit_purchase_price"),
                ["pack_sale_price"] = ToDouble(it, "pack_sale_price"),
                ["unit_sale_price"] = ToDouble(it, "unit_sale_price"),
                ["pack_bonus"] = (int)ToLong(it, "pack_bonus"),
                ["unit_bonus"] = (int)ToLong(it, "unit_bonus"),
                ["bonus"] = (int)ToLong(it, "unit_bonus") + (int)ToLong(it, "pack_bonus"),
                ["item_discount_percentage"] = ToDouble(it, "item_discount_percentage"),
                ["margin"] = ToDouble(it, "margin"),
                ["sub_total"] = ToDouble(it, "sub_total"),
                ["quantity"] = (int)ToLong(it, "quantity")
            }).ToList();

            var invDateStr = FormatDateOnly(GetNullableStr(inv, "posted_date"));
            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = invId,
                ["supplier_id"] = ToNullableLong(inv, "supplier_id"),
                ["supplier_name"] = GetNullableStr(inv, "supplier_name"),
                ["posted_number"] = GetNullableStr(inv, "posted_number"),
                ["invoice_number"] = GetNullableStr(inv, "invoice_number"),
                ["invoice_date"] = invDateStr,
                ["date"] = invDateStr,
                ["tax_percentage"] = ToDouble(inv, "tax_percentage"),
                ["tax_amount"] = ToDouble(inv, "tax_amount"),
                ["discount_percentage"] = ToDouble(inv, "discount_percentage"),
                ["discount_amount"] = ToDouble(inv, "discount_amount"),
                ["total_amount"] = ToDouble(inv, "total_amount"),
                ["items"] = mappedItems
            });
        }

        return rows;
    }

    // =========================================================================
    // 7 & 8. Sale Detail (/api/reports/sale-detail & /pdf)
    // =========================================================================

    [HttpGet("sale-detail")]
    public async Task<IActionResult> SaleDetail()
    {
        var authErr = await AuthorizeReportActionAsync("report.sale-detail.view");
        if (authErr != null) return authErr;

        var rows = await BuildSaleDetailRowsAsync();
        return Ok(rows);
    }

    [HttpGet("sale-detail/pdf")]
    public async Task<IActionResult> SaleDetailPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.sale-detail.export", "report.sale-detail.view");
        if (authErr != null) return authErr;

        var rows = await BuildSaleDetailRowsAsync();
        var from = Request.Query["from"].FirstOrDefault() ?? "start";
        var to = Request.Query["to"].FirstOrDefault() ?? "today";
        var totalSales = rows.Sum(r => ToDouble(r, "total"));

        var tableRows = new List<List<string>>();
        foreach (var inv in rows)
        {
            var dt = GetStr(inv, "invoice_date");
            var invNo = GetStr(inv, "posted_number");
            var cust = GetNullableStr(inv, "customer_name") ?? "Walk-in";
            var items = inv.GetValueOrDefault("items") as List<Dictionary<string, object?>> ?? new();
            foreach (var it in items)
            {
                tableRows.Add(
                [
                    dt,
                    invNo,
                    cust,
                    GetStr(it, "product_name"),
                    GetStr(it, "batch_number"),
                    $"{ToLong(it, "quantity")}",
                    $"{ToDouble(it, "price"):N2}",
                    $"{ToDouble(it, "sub_total"):N2}"
                ]);
            }
        }

        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "sale-detail",
            FromDate = Request.Query["from"].FirstOrDefault(),
            ToDate = Request.Query["to"].FirstOrDefault(),
            Landscape = true,
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Invoices", rows.Count.ToString()),
                new("Line Items", tableRows.Count.ToString()),
                new("Total Sales", $"Rs. {totalSales:N2}")
            ],
            Columns =
            [
                new() { Header = "Date", RelativeWidth = 1.2f },
                new() { Header = "Invoice #", RelativeWidth = 1.3f },
                new() { Header = "Customer", RelativeWidth = 2.0f },
                new() { Header = "Product", RelativeWidth = 2.5f },
                new() { Header = "Batch", RelativeWidth = 1.2f },
                new() { Header = "Qty", RelativeWidth = 0.8f, AlignRight = true },
                new() { Header = "Price", RelativeWidth = 1.1f, AlignRight = true },
                new() { Header = "Sub Total", RelativeWidth = 1.3f, AlignRight = true }
            ],
            Rows = tableRows,
            FooterTotals =
            [
                new("Total Sales", $"Rs. {totalSales:N2}", isGrandTotal: true)
            ]
        });
        return DeliverPdf(pdf, $"sale-detail-{from}-to-{to}.pdf");
    }

    private async Task<List<Dictionary<string, object?>>> BuildSaleDetailRowsAsync()
    {
        var (fromStr, toStr) = ResolveDateRange(
            Request.Query["from"].FirstOrDefault(),
            Request.Query["to"].FirstOrDefault(),
            true);

        var customerId = Request.Query["customer_id"].FirstOrDefault();
        var productId = Request.Query["product_id"].FirstOrDefault();

        var sql = @"
            SELECT si.*, c.name AS customer_name, u.name AS user_name
            FROM sale_invoices si
            LEFT JOIN customers c ON c.id = si.customer_id
            LEFT JOIN users u ON u.id = si.user_id
            WHERE DATE(si.date) BETWEEN @fromStr AND @toStr
              AND (si.status IS NULL OR si.status != 'reversed')";

        var dyn = new DynamicParameters(new { fromStr, toStr });
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            sql += " AND si.customer_id = @customerId";
            dyn.Add("customerId", customerId);
        }
        if (!string.IsNullOrWhiteSpace(productId))
        {
            sql += " AND EXISTS (SELECT 1 FROM sale_invoice_items sii WHERE sii.sale_invoice_id = si.id AND sii.product_id = @productId)";
            dyn.Add("productId", productId);
        }

        sql += " ORDER BY si.date ASC, si.id ASC";
        var invoices = await Db.QueryListAsync(sql, dyn);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var inv in invoices)
        {
            var invId = ToLong(inv, "id");
            var itemSql = @"
                SELECT sii.*, p.name AS product_name
                FROM sale_invoice_items sii
                LEFT JOIN products p ON p.id = sii.product_id
                WHERE sii.sale_invoice_id = @invId";
            var itemParams = new DynamicParameters(new { invId });
            if (!string.IsNullOrWhiteSpace(productId))
            {
                itemSql += " AND sii.product_id = @productId";
                itemParams.Add("productId", productId);
            }

            var items = await Db.QueryListAsync(itemSql, itemParams);
            var mappedItems = items.Select(it => new Dictionary<string, object?>
            {
                ["id"] = ToLong(it, "id"),
                ["product_id"] = ToLong(it, "product_id"),
                ["product_name"] = GetNullableStr(it, "product_name"),
                ["pack_size"] = (int)ToLong(it, "pack_size"),
                ["batch_number"] = GetNullableStr(it, "batch_number"),
                ["expiry"] = GetNullableStr(it, "expiry"),
                ["current_quantity"] = (int)ToLong(it, "current_quantity"),
                ["pack_quantity"] = ToDouble(it, "pack_quantity"),
                ["unit_quantity"] = (int)ToLong(it, "unit_quantity"),
                ["quantity"] = (int)ToLong(it, "quantity"),
                ["bonus"] = (int)ToLong(it, "bonus"),
                ["price"] = ToDouble(it, "price"),
                ["item_discount_percentage"] = ToDouble(it, "item_discount_percentage"),
                ["sub_total"] = ToDouble(it, "sub_total")
            }).ToList();

            var saleDateStr = FormatDateOnly(GetNullableStr(inv, "date"));
            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = invId,
                ["posted_number"] = GetNullableStr(inv, "posted_number"),
                ["invoice_date"] = saleDateStr,
                ["date"] = saleDateStr,
                ["customer_name"] = GetNullableStr(inv, "customer_name"),
                ["user_name"] = GetNullableStr(inv, "user_name"),
                ["doctor_name"] = GetNullableStr(inv, "doctor_name"),
                ["patient_name"] = GetNullableStr(inv, "patient_name"),
                ["sale_type"] = GetNullableStr(inv, "sale_type") ?? "retail",
                ["discount_percentage"] = ToDouble(inv, "discount_percentage"),
                ["discount_amount"] = ToDouble(inv, "discount_amount"),
                ["tax_percentage"] = ToDouble(inv, "tax_percentage"),
                ["tax_amount"] = ToDouble(inv, "tax_amount"),
                ["item_discount"] = ToDouble(inv, "item_discount"),
                ["gross_amount"] = ToDouble(inv, "gross_amount"),
                ["total"] = ToDouble(inv, "total"),
                ["items"] = mappedItems
            });
        }

        return rows;
    }

    // =========================================================================
    // 9 & 10. Current Stock (/api/reports/current-stock & /pdf)
    // =========================================================================

    [HttpGet("current-stock")]
    public async Task<IActionResult> CurrentStock()
    {
        var authErr = await AuthorizeReportActionAsync("report.current-stock.view");
        if (authErr != null) return authErr;

        var data = await BuildCurrentStockDataAsync();
        return Ok(data);
    }

    [HttpGet("current-stock/pdf")]
    public async Task<IActionResult> CurrentStockPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.current-stock.export", "report.current-stock.view");
        if (authErr != null) return authErr;

        var data = await BuildCurrentStockDataAsync();
        var summary = (Dictionary<string, object?>)data["summary"]!;
        var rows = data.GetValueOrDefault("rows") as List<Dictionary<string, object?>> ?? new();

        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "current-stock",
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Total Items", GetStr(summary, "total_items")),
                new("Total Units", GetStr(summary, "total_quantity")),
                new("Purchase Value", $"Rs. {ToDouble(summary, "total_purchase_value"):N2}"),
                new("Sale Value", $"Rs. {ToDouble(summary, "total_sale_value"):N2}"),
                new("Potential Profit", $"Rs. {ToDouble(summary, "potential_profit"):N2}")
            ],
            Columns =
            [
                new() { Header = "Code", RelativeWidth = 1.2f },
                new() { Header = "Product Name", RelativeWidth = 2.8f },
                new() { Header = "Brand", RelativeWidth = 1.4f },
                new() { Header = "Qty", RelativeWidth = 0.9f, AlignRight = true },
                new() { Header = "Unit Cost", RelativeWidth = 1.1f, AlignRight = true },
                new() { Header = "Cost Value", RelativeWidth = 1.3f, AlignRight = true },
                new() { Header = "Sale Value", RelativeWidth = 1.3f, AlignRight = true }
            ],
            Rows = rows.Take(500).Select(r => new List<string>
            {
                GetStr(r, "product_code"),
                GetStr(r, "name"),
                GetStr(r, "brand_name"),
                $"{ToLong(r, "quantity")}",
                $"{ToDouble(r, "unit_purchase_price"):N2}",
                $"{ToDouble(r, "total_purchase_value"):N2}",
                $"{ToDouble(r, "total_sale_value"):N2}"
            }).ToList(),
            FooterTotals =
            [
                new("Total Purchase Value", $"Rs. {ToDouble(summary, "total_purchase_value"):N2}"),
                new("Total Sale Value", $"Rs. {ToDouble(summary, "total_sale_value"):N2}", isGrandTotal: true)
            ]
        });
        return DeliverPdf(pdf, "current-stock-report.pdf");
    }

    private async Task<Dictionary<string, object?>> BuildCurrentStockDataAsync()
    {
        var categoryId = Request.Query["category_id"].FirstOrDefault();
        var brandId = Request.Query["brand_id"].FirstOrDefault();
        var supplierId = Request.Query["supplier_id"].FirstOrDefault();
        var storageLocationId = Request.Query["storage_location_id"].FirstOrDefault();

        var sql = @"
            SELECT p.*,
                   c.name AS category_name,
                   b.name AS brand_name,
                   s.name AS supplier_name
            FROM products p
            LEFT JOIN categories c ON c.id = p.category_id
            LEFT JOIN brands b ON b.id = p.brand_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            WHERE p.quantity > 0";

        var dyn = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            sql += " AND p.category_id = @categoryId";
            dyn.Add("categoryId", categoryId);
        }
        if (!string.IsNullOrWhiteSpace(brandId))
        {
            sql += " AND p.brand_id = @brandId";
            dyn.Add("brandId", brandId);
        }
        if (!string.IsNullOrWhiteSpace(supplierId))
        {
            sql += " AND p.supplier_id = @supplierId";
            dyn.Add("supplierId", supplierId);
        }
        if (!string.IsNullOrWhiteSpace(storageLocationId))
        {
            sql += @" AND (p.storage_location_id = @storageLocationId
                      OR EXISTS (SELECT 1 FROM batches bt WHERE bt.product_id = p.id AND bt.storage_location_id = @storageLocationId AND bt.quantity > 0))";
            dyn.Add("storageLocationId", storageLocationId);
        }

        sql += " ORDER BY p.name ASC";
        var products = await Db.QueryListAsync(sql, dyn);

        var rows = new List<Dictionary<string, object?>>();
        int totalQty = 0;
        double sumPurchaseVal = 0;
        double sumSaleVal = 0;

        foreach (var p in products)
        {
            var qty = (int)ToLong(p, "quantity");
            var packSize = Math.Max(1, (int)ToLong(p, "pack_size", 1));
            var packPurchase = ToDouble(p, "pack_purchase_price");
            var packSale = ToDouble(p, "pack_sale_price");
            var unitPurchase = ToDouble(p, "unit_purchase_price");
            var unitSale = ToDouble(p, "unit_sale_price");
            var avgPrice = ToDouble(p, "avg_price");

            var effectiveCost = avgPrice > 0
                ? avgPrice
                : (unitPurchase > 0 ? unitPurchase : (packSize > 0 ? (packPurchase / packSize) : 0));

            var totalPurchaseVal = Math.Round(qty * effectiveCost, 2);
            var totalSaleVal = Math.Round(qty * unitSale, 2);

            totalQty += qty;
            sumPurchaseVal += totalPurchaseVal;
            sumSaleVal += totalSaleVal;

            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = ToLong(p, "id"),
                ["product_code"] = GetNullableStr(p, "product_code"),
                ["name"] = GetNullableStr(p, "name"),
                ["category_name"] = GetNullableStr(p, "category_name"),
                ["brand_name"] = GetNullableStr(p, "brand_name"),
                ["supplier_name"] = GetNullableStr(p, "supplier_name"),
                ["pack_size"] = packSize,
                ["quantity"] = qty,
                ["pack_purchase_price"] = packPurchase,
                ["pack_sale_price"] = packSale,
                ["unit_purchase_price"] = unitPurchase,
                ["unit_sale_price"] = unitSale,
                ["avg_price"] = avgPrice,
                ["rack"] = GetNullableStr(p, "rack"),
                ["total_purchase_value"] = totalPurchaseVal,
                ["total_sale_value"] = totalSaleVal
            });
        }

        return new Dictionary<string, object?>
        {
            ["rows"] = rows,
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_items"] = rows.Count,
                ["total_quantity"] = totalQty,
                ["total_purchase_value"] = Math.Round(sumPurchaseVal, 2),
                ["total_sale_value"] = Math.Round(sumSaleVal, 2),
                ["potential_profit"] = Math.Round(sumSaleVal - sumPurchaseVal, 2)
            }
        };
    }

    // =========================================================================
    // 11 & 12. Stock Adjustment (/api/reports/stock-adjustment & /pdf)
    // =========================================================================

    [HttpGet("stock-adjustment")]
    public async Task<IActionResult> StockAdjustment()
    {
        var authErr = await AuthorizeReportActionAsync("report.stock-adjustment.view");
        if (authErr != null) return authErr;

        var data = await BuildStockAdjustmentDataAsync();
        return Ok(data);
    }

    [HttpGet("stock-adjustment/pdf")]
    public async Task<IActionResult> StockAdjustmentPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.stock-adjustment.export", "report.stock-adjustment.view");
        if (authErr != null) return authErr;

        var data = await BuildStockAdjustmentDataAsync();
        var summary = (Dictionary<string, object?>)data["summary"]!;
        var rows = data.GetValueOrDefault("rows") as List<Dictionary<string, object?>> ?? new();
        var from = Request.Query["from"].FirstOrDefault() ?? "start";
        var to = Request.Query["to"].FirstOrDefault() ?? "today";

        var tableRows = new List<List<string>>();
        foreach (var adj in rows)
        {
            var dt = GetStr(adj, "posted_date");
            var no = GetStr(adj, "posted_number");
            var items = adj.GetValueOrDefault("items") as List<Dictionary<string, object?>> ?? new();
            foreach (var it in items)
            {
                tableRows.Add(
                [
                    dt,
                    no,
                    GetStr(it, "product_name"),
                    GetStr(it, "batch_number"),
                    $"{ToDouble(it, "previous_qty"):N0}",
                    $"{ToDouble(it, "actual_qty"):N0}",
                    $"{ToDouble(it, "diff_qty"):+0;-0;0}",
                    $"{ToDouble(it, "worth_adjusted"):N2}"
                ]);
            }
        }

        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "stock-adjustment",
            FromDate = Request.Query["from"].FirstOrDefault(),
            ToDate = Request.Query["to"].FirstOrDefault(),
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Adjustments", GetStr(summary, "total_adjustments")),
                new("Items", GetStr(summary, "total_items")),
                new("Positive", $"+{ToDouble(summary, "positive_adjustments"):N0}"),
                new("Negative", $"-{ToDouble(summary, "negative_adjustments"):N0}"),
                new("Total Worth", $"Rs. {ToDouble(summary, "total_worth_adjusted"):N2}")
            ],
            Columns =
            [
                new() { Header = "Date", RelativeWidth = 1.2f },
                new() { Header = "Voucher #", RelativeWidth = 1.4f },
                new() { Header = "Product", RelativeWidth = 2.4f },
                new() { Header = "Batch", RelativeWidth = 1.1f },
                new() { Header = "Prev", RelativeWidth = 0.8f, AlignRight = true },
                new() { Header = "Actual", RelativeWidth = 0.8f, AlignRight = true },
                new() { Header = "Diff", RelativeWidth = 0.8f, AlignRight = true },
                new() { Header = "Worth", RelativeWidth = 1.2f, AlignRight = true }
            ],
            Rows = tableRows,
            FooterTotals =
            [
                new("Total Worth Adjusted", $"Rs. {ToDouble(summary, "total_worth_adjusted"):N2}", isGrandTotal: true)
            ]
        });
        return DeliverPdf(pdf, $"stock-adjustment-{from}-to-{to}.pdf");
    }

    private async Task<Dictionary<string, object?>> BuildStockAdjustmentDataAsync()
    {
        var (fromStr, toStr) = ResolveDateRange(
            Request.Query["from"].FirstOrDefault(),
            Request.Query["to"].FirstOrDefault(),
            true);

        var adjustments = await Db.QueryListAsync(@"
            SELECT sa.*, u.name AS user_name
            FROM stock_adjustments sa
            LEFT JOIN users u ON u.id = sa.user_id
            WHERE DATE(sa.posted_date) BETWEEN @fromStr AND @toStr
            ORDER BY sa.posted_date ASC, sa.id ASC",
            new { fromStr, toStr });

        var rows = new List<Dictionary<string, object?>>();
        int totalItems = 0;
        double totalWorth = 0;
        double positiveAdj = 0;
        double negativeAdj = 0;

        foreach (var adj in adjustments)
        {
            var adjId = ToLong(adj, "id");
            var items = await Db.QueryListAsync(@"
                SELECT sai.*, p.product_code, p.name AS product_name
                FROM stock_adjustment_items sai
                LEFT JOIN products p ON p.id = sai.product_id
                WHERE sai.stock_adjustment_id = @adjId
                ORDER BY sai.id ASC",
                new { adjId });

            var mappedItems = new List<Dictionary<string, object?>>();
            foreach (var it in items)
            {
                var diff = ToDouble(it, "diff_qty");
                if (diff > 0) positiveAdj += diff;
                else if (diff < 0) negativeAdj += Math.Abs(diff);

                mappedItems.Add(new Dictionary<string, object?>
                {
                    ["id"] = ToLong(it, "id"),
                    ["product_id"] = ToLong(it, "product_id"),
                    ["product_code"] = GetNullableStr(it, "product_code"),
                    ["product_name"] = GetNullableStr(it, "product_name"),
                    ["batch_number"] = GetNullableStr(it, "batch_number"),
                    ["expiry"] = FormatDateOnly(GetNullableStr(it, "expiry")),
                    ["pack_size"] = ToDouble(it, "pack_size"),
                    ["previous_qty"] = ToDouble(it, "previous_qty"),
                    ["actual_qty"] = ToDouble(it, "actual_qty"),
                    ["diff_qty"] = diff,
                    ["unit_purchase_price"] = ToDouble(it, "unit_purchase_price"),
                    ["worth_adjusted"] = ToDouble(it, "worth_adjusted")
                });
            }

            totalItems += mappedItems.Count;
            var worth = ToDouble(adj, "total_worth");
            totalWorth += worth;

            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = adjId,
                ["posted_number"] = GetNullableStr(adj, "posted_number"),
                ["posted_date"] = FormatDateOnly(GetNullableStr(adj, "posted_date")),
                ["note"] = GetNullableStr(adj, "note"),
                ["total_worth"] = worth,
                ["user_name"] = GetNullableStr(adj, "user_name"),
                ["items"] = mappedItems
            });
        }

        return new Dictionary<string, object?>
        {
            ["rows"] = rows,
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_adjustments"] = rows.Count,
                ["total_items"] = totalItems,
                ["total_worth_adjusted"] = Math.Round(totalWorth, 2),
                ["positive_adjustments"] = Math.Round(positiveAdj, 3),
                ["negative_adjustments"] = Math.Round(negativeAdj, 3)
            }
        };
    }

    // =========================================================================
    // 13 & 14. Product Comprehensive (/api/reports/product-comprehensive & /pdf)
    // =========================================================================

    [HttpGet("product-comprehensive")]
    public async Task<IActionResult> ProductComprehensive()
    {
        var authErr = await AuthorizeReportActionAsync("report.product-comprehensive.view");
        if (authErr != null) return authErr;

        var productIdStr = Request.Query["product_id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(productIdStr) || !long.TryParse(productIdStr, out var productId))
        {
            return BadRequest(new { message = "Product ID is required" });
        }

        var data = await BuildProductComprehensiveDataAsync(productId);
        if (data == null)
        {
            return NotFound(new { message = "Product not found" });
        }

        return Ok(data);
    }

    [HttpGet("product-comprehensive/pdf")]
    public async Task<IActionResult> ProductComprehensivePdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.product-comprehensive.export", "report.product-comprehensive.view");
        if (authErr != null) return authErr;

        var productIdStr = Request.Query["product_id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(productIdStr) || !long.TryParse(productIdStr, out var productId) || productId <= 0)
        {
            return BadRequest(new { message = "Product ID is required" });
        }

        var data = await BuildProductComprehensiveDataAsync(productId);
        if (data == null)
        {
            return NotFound(new { message = "Product not found" });
        }

        var prod = data.GetValueOrDefault("product") as Dictionary<string, object?>;
        var summary = data.GetValueOrDefault("summary") as Dictionary<string, object?> ?? new();
        var txns = data.GetValueOrDefault("transactions") as List<Dictionary<string, object?>> ?? new();
        var code = GetNullableStr(prod, "product_code") ?? "product";
        var prodName = GetNullableStr(prod, "name") ?? "-";

        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "product-comprehensive",
            ReportTitle = $"Product Comprehensive Report — {prodName}",
            Subtitle = $"Code: {code} | On Hand: {ToLong(prod, "quantity")} units",
            FromDate = Request.Query["from"].FirstOrDefault(),
            ToDate = Request.Query["to"].FirstOrDefault(),
            Landscape = true,
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Purchases", GetStr(summary, "total_purchased_qty")),
                new("Sales", GetStr(summary, "total_sold_qty")),
                new("Sale Returns", GetStr(summary, "total_sale_return_qty")),
                new("Purchase Returns", GetStr(summary, "total_purchase_return_qty")),
                new("Current Stock", $"{ToLong(prod, "quantity")}")
            ],
            Columns =
            [
                new() { Header = "Date", RelativeWidth = 1.2f },
                new() { Header = "Type", RelativeWidth = 1.3f },
                new() { Header = "Document #", RelativeWidth = 1.4f },
                new() { Header = "Party", RelativeWidth = 2.0f },
                new() { Header = "Batch", RelativeWidth = 1.2f },
                new() { Header = "Qty", RelativeWidth = 0.9f, AlignRight = true },
                new() { Header = "Rate", RelativeWidth = 1.1f, AlignRight = true },
                new() { Header = "Amount", RelativeWidth = 1.3f, AlignRight = true }
            ],
            Rows = txns.Select(t => new List<string>
            {
                GetStr(t, "date"),
                GetStr(t, "type").ToUpperInvariant(),
                GetStr(t, "posted_number"),
                GetStr(t, "party_name"),
                GetStr(t, "batch_number"),
                $"{ToDouble(t, "quantity"):N0}",
                $"{ToDouble(t, "unit_price"):N2}",
                $"{ToDouble(t, "total"):N2}"
            }).ToList()
        });
        return DeliverPdf(pdf, $"product-comprehensive-{code}.pdf");
    }

    private async Task<Dictionary<string, object?>?> BuildProductComprehensiveDataAsync(long productId)
    {
        var prod = await Db.QuerySingleOrDefaultAsync(@"
            SELECT p.*, c.name AS category_name, b.name AS brand_name
            FROM products p
            LEFT JOIN categories c ON c.id = p.category_id
            LEFT JOIN brands b ON b.id = p.brand_id
            WHERE p.id = @productId",
            new { productId });

        if (prod == null) return null;

        var (fromDateStr, toDateStr) = ResolveDateRange(
            Request.Query["from"].FirstOrDefault(),
            Request.Query["to"].FirstOrDefault(),
            true);

        var productData = new Dictionary<string, object?>
        {
            ["id"] = ToLong(prod, "id"),
            ["product_code"] = GetNullableStr(prod, "product_code"),
            ["name"] = GetNullableStr(prod, "name"),
            ["pack_size"] = (int)ToLong(prod, "pack_size", 1),
            ["current_quantity"] = (int)ToLong(prod, "quantity"),
            ["category_name"] = GetNullableStr(prod, "category_name"),
            ["brand_name"] = GetNullableStr(prod, "brand_name")
        };

        var transactions = new List<Dictionary<string, object?>>();

        // Purchases
        var purchases = await Db.QueryListAsync(@"
            SELECT pi.posted_date AS date,
                   pi.posted_number AS reference_number,
                   s.name AS counter_party,
                   pii.batch,
                   pii.expiry,
                   pii.quantity,
                   pii.unit_purchase_price AS unit_price,
                   pii.sub_total
            FROM purchase_invoice_items pii
            INNER JOIN purchase_invoices pi ON pi.id = pii.purchase_invoice_id
            INNER JOIN suppliers s ON s.id = pi.supplier_id
            WHERE pii.product_id = @productId
              AND DATE(pi.posted_date) BETWEEN @fromDateStr AND @toDateStr
              AND (pi.status IS NULL OR pi.status != 'reversed')
            ORDER BY pi.posted_date ASC",
            new { productId, fromDateStr, toDateStr });

        double totalPurchases = 0;
        int qtyIn = 0;
        int qtyOut = 0;

        foreach (var item in purchases)
        {
            var q = (int)ToLong(item, "quantity");
            var st = ToDouble(item, "sub_total");
            totalPurchases += st;
            qtyIn += q;
            transactions.Add(new Dictionary<string, object?>
            {
                ["date"] = FormatDateOnly(GetNullableStr(item, "date")),
                ["reference_number"] = GetNullableStr(item, "reference_number"),
                ["type"] = "purchase",
                ["type_label"] = "Purchase",
                ["counter_party"] = GetNullableStr(item, "counter_party"),
                ["batch"] = GetNullableStr(item, "batch"),
                ["expiry"] = FormatDateOnly(GetNullableStr(item, "expiry")),
                ["quantity_in"] = q,
                ["quantity_out"] = 0,
                ["unit_price"] = ToDouble(item, "unit_price"),
                ["sub_total"] = st
            });
        }

        // Purchase Returns
        var purchaseReturns = await Db.QueryListAsync(@"
            SELECT pr.date,
                   pr.posted_number AS reference_number,
                   s.name AS counter_party,
                   pri.batch,
                   pri.expiry,
                   pri.return_unit_quantity AS quantity,
                   pri.unit_purchase_price AS unit_price,
                   pri.sub_total
            FROM purchase_return_items pri
            INNER JOIN purchase_returns pr ON pr.id = pri.purchase_return_id
            INNER JOIN suppliers s ON s.id = pr.supplier_id
            WHERE pri.product_id = @productId
              AND DATE(pr.date) BETWEEN @fromDateStr AND @toDateStr
            ORDER BY pr.date ASC",
            new { productId, fromDateStr, toDateStr });

        double totalPurchaseReturns = 0;
        foreach (var item in purchaseReturns)
        {
            var q = (int)ToLong(item, "quantity");
            var st = ToDouble(item, "sub_total");
            totalPurchaseReturns += st;
            qtyOut += q;
            transactions.Add(new Dictionary<string, object?>
            {
                ["date"] = FormatDateOnly(GetNullableStr(item, "date")),
                ["reference_number"] = GetNullableStr(item, "reference_number"),
                ["type"] = "purchase_return",
                ["type_label"] = "Purchase Return",
                ["counter_party"] = GetNullableStr(item, "counter_party"),
                ["batch"] = GetNullableStr(item, "batch"),
                ["expiry"] = FormatDateOnly(GetNullableStr(item, "expiry")),
                ["quantity_in"] = 0,
                ["quantity_out"] = q,
                ["unit_price"] = ToDouble(item, "unit_price"),
                ["sub_total"] = st
            });
        }

        // Sales
        var sales = await Db.QueryListAsync(@"
            SELECT si.date,
                   si.posted_number AS reference_number,
                   c.name AS counter_party,
                   sii.batch_number AS batch,
                   sii.expiry,
                   sii.quantity,
                   sii.bonus,
                   sii.price AS unit_price,
                   sii.sub_total
            FROM sale_invoice_items sii
            INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
            LEFT JOIN customers c ON c.id = si.customer_id
            WHERE sii.product_id = @productId
              AND DATE(si.date) BETWEEN @fromDateStr AND @toDateStr
              AND (si.status IS NULL OR si.status != 'reversed')
            ORDER BY si.date ASC",
            new { productId, fromDateStr, toDateStr });

        double totalSales = 0;
        foreach (var item in sales)
        {
            var q = (int)ToLong(item, "quantity") + (int)ToLong(item, "bonus");
            var st = ToDouble(item, "sub_total");
            totalSales += st;
            qtyOut += q;
            transactions.Add(new Dictionary<string, object?>
            {
                ["date"] = FormatDateOnly(GetNullableStr(item, "date")),
                ["reference_number"] = GetNullableStr(item, "reference_number"),
                ["type"] = "sale",
                ["type_label"] = "Sale",
                ["counter_party"] = GetNullableStr(item, "counter_party"),
                ["batch"] = GetNullableStr(item, "batch"),
                ["expiry"] = FormatDateOnly(GetNullableStr(item, "expiry")),
                ["quantity_in"] = 0,
                ["quantity_out"] = q,
                ["unit_price"] = ToDouble(item, "unit_price"),
                ["sub_total"] = st
            });
        }

        // Sale Returns
        var saleReturns = await Db.QueryListAsync(@"
            SELECT sr.date,
                   sr.posted_number AS reference_number,
                   c.name AS counter_party,
                   sri.batch_number AS batch,
                   sri.expiry,
                   sri.unit_return_quantity AS quantity,
                   sri.unit_sale_price AS unit_price,
                   sri.sub_total
            FROM sale_return_items sri
            INNER JOIN sale_returns sr ON sr.id = sri.sale_return_id
            LEFT JOIN customers c ON c.id = sr.customer_id
            WHERE sri.product_id = @productId
              AND DATE(sr.date) BETWEEN @fromDateStr AND @toDateStr
            ORDER BY sr.date ASC",
            new { productId, fromDateStr, toDateStr });

        double totalSaleReturns = 0;
        foreach (var item in saleReturns)
        {
            var q = (int)ToLong(item, "quantity");
            var st = ToDouble(item, "sub_total");
            totalSaleReturns += st;
            qtyIn += q;
            transactions.Add(new Dictionary<string, object?>
            {
                ["date"] = FormatDateOnly(GetNullableStr(item, "date")),
                ["reference_number"] = GetNullableStr(item, "reference_number"),
                ["type"] = "sale_return",
                ["type_label"] = "Sale Return",
                ["counter_party"] = GetNullableStr(item, "counter_party"),
                ["batch"] = GetNullableStr(item, "batch"),
                ["expiry"] = FormatDateOnly(GetNullableStr(item, "expiry")),
                ["quantity_in"] = q,
                ["quantity_out"] = 0,
                ["unit_price"] = ToDouble(item, "unit_price"),
                ["sub_total"] = st
            });
        }

        // Stock Adjustments
        var adjustments = await Db.QueryListAsync(@"
            SELECT sa.posted_date AS date,
                   sa.posted_number AS reference_number,
                   sai.batch_number AS batch,
                   sai.expiry,
                   sai.diff_qty,
                   sai.unit_purchase_price AS unit_price,
                   sai.worth_adjusted AS sub_total
            FROM stock_adjustment_items sai
            INNER JOIN stock_adjustments sa ON sa.id = sai.stock_adjustment_id
            WHERE sai.product_id = @productId
              AND DATE(sa.posted_date) BETWEEN @fromDateStr AND @toDateStr
            ORDER BY sa.posted_date ASC",
            new { productId, fromDateStr, toDateStr });

        foreach (var item in adjustments)
        {
            var diff = ToDouble(item, "diff_qty");
            var qIn = diff > 0 ? (int)diff : 0;
            var qOut = diff < 0 ? (int)Math.Abs(diff) : 0;
            qtyIn += qIn;
            qtyOut += qOut;
            transactions.Add(new Dictionary<string, object?>
            {
                ["date"] = FormatDateOnly(GetNullableStr(item, "date")),
                ["reference_number"] = GetNullableStr(item, "reference_number"),
                ["type"] = "stock_adjustment",
                ["type_label"] = "Stock Adjustment",
                ["counter_party"] = "Inventory Adjustment",
                ["batch"] = GetNullableStr(item, "batch"),
                ["expiry"] = FormatDateOnly(GetNullableStr(item, "expiry")),
                ["quantity_in"] = qIn,
                ["quantity_out"] = qOut,
                ["unit_price"] = ToDouble(item, "unit_price"),
                ["sub_total"] = ToDouble(item, "sub_total")
            });
        }

        transactions = transactions.OrderBy(t => GetStr(t, "date")).ToList();

        return new Dictionary<string, object?>
        {
            ["product"] = productData,
            ["date_range"] = new Dictionary<string, string>
            {
                ["from"] = fromDateStr,
                ["to"] = toDateStr
            },
            ["transactions"] = transactions,
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_purchases"] = Math.Round(totalPurchases, 2),
                ["total_purchase_returns"] = Math.Round(totalPurchaseReturns, 2),
                ["net_purchases"] = Math.Round(totalPurchases - totalPurchaseReturns, 2),
                ["total_sales"] = Math.Round(totalSales, 2),
                ["total_sale_returns"] = Math.Round(totalSaleReturns, 2),
                ["net_sales"] = Math.Round(totalSales - totalSaleReturns, 2),
                ["total_quantity_in"] = qtyIn,
                ["total_quantity_out"] = qtyOut,
                ["current_quantity"] = productData["current_quantity"]
            }
        };
    }

    // =========================================================================
    // 15 & 16. Near Expiry Product (/api/reports/near-expiry-product & /pdf)
    // =========================================================================

    [HttpGet("near-expiry-product")]
    public async Task<IActionResult> NearExpiryProduct()
    {
        var authErr = await AuthorizeReportActionAsync("report.near-expiry-product.view");
        if (authErr != null) return authErr;

        var data = await BuildNearExpiryProductDataAsync();
        return Ok(data);
    }

    [HttpGet("near-expiry-product/pdf")]
    public async Task<IActionResult> NearExpiryProductPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.near-expiry-product.export", "report.near-expiry-product.view");
        if (authErr != null) return authErr;

        var data = await BuildNearExpiryProductDataAsync();
        var summary = (Dictionary<string, object?>)data["summary"]!;
        var rows = data.GetValueOrDefault("rows") as List<Dictionary<string, object?>> ?? new();
        var filters = data.GetValueOrDefault("filters") as Dictionary<string, object?>;

        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "near-expiry-product",
            FromDate = GetNullableStr(filters, "from"),
            ToDate = GetNullableStr(filters, "to"),
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Near-Expiry Batches", GetStr(summary, "total_batches")),
                new("Total Units", GetStr(summary, "total_quantity")),
                new("At-Risk Value", $"Rs. {rows.Sum(r => ToDouble(r, "purchase_value")):N2}")
            ],
            Columns =
            [
                new() { Header = "Code", RelativeWidth = 1.1f },
                new() { Header = "Product", RelativeWidth = 2.4f },
                new() { Header = "Batch", RelativeWidth = 1.2f },
                new() { Header = "Expiry", RelativeWidth = 1.1f },
                new() { Header = "Days", RelativeWidth = 0.7f, AlignRight = true },
                new() { Header = "Qty", RelativeWidth = 0.8f, AlignRight = true },
                new() { Header = "Cost Value", RelativeWidth = 1.3f, AlignRight = true }
            ],
            Rows = rows.Select(r => new List<string>
            {
                GetStr(r, "product_code"),
                GetStr(r, "product_name"),
                GetStr(r, "batch_number"),
                FormatDateOnly(GetNullableStr(r, "expiry_date")) ?? "",
                GetStr(r, "days_remaining"),
                $"{ToLong(r, "quantity")}",
                $"{ToDouble(r, "purchase_value"):N2}"
            }).ToList()
        });
        return DeliverPdf(pdf, "near-expiry-product-report.pdf");
    }

    private async Task<Dictionary<string, object?>> BuildNearExpiryProductDataAsync()
    {
        var todayDt = DateTime.Now.Date;
        var today = todayDt.ToString("yyyy-MM-dd");
        var threeMonths = todayDt.AddMonths(3).ToString("yyyy-MM-dd");

        var rawFrom = Request.Query["from"].FirstOrDefault();
        var rawTo = Request.Query["to"].FirstOrDefault();
        var supplierId = Request.Query["supplier_id"].FirstOrDefault();
        var brandId = Request.Query["brand_id"].FirstOrDefault();
        var productId = Request.Query["product_id"].FirstOrDefault();
        var storageLocationId = Request.Query["storage_location_id"].FirstOrDefault();

        var fromDate = !string.IsNullOrWhiteSpace(rawFrom) && DateTime.TryParse(rawFrom, out var f)
            ? f.ToString("yyyy-MM-dd")
            : today;
        var toDate = !string.IsNullOrWhiteSpace(rawTo) && DateTime.TryParse(rawTo, out var t)
            ? t.ToString("yyyy-MM-dd")
            : threeMonths;

        if (string.CompareOrdinal(fromDate, today) < 0)
        {
            fromDate = today;
        }
        if (string.CompareOrdinal(fromDate, toDate) > 0)
        {
            (fromDate, toDate) = (toDate, fromDate);
        }

        var sql = @"
            SELECT b.id AS batch_id,
                   b.batch_number,
                   b.expiry_date,
                   b.quantity,
                   b.storage_location_id,
                   COALESCE(b.unit_purchase_price, p.unit_purchase_price, 0) AS unit_purchase_price,
                   p.id AS product_id,
                   p.product_code,
                   p.name AS product_name,
                   p.supplier_id,
                   p.brand_id,
                   COALESCE(s.name, '') AS supplier_name,
                   COALESCE(br.name, '') AS brand_name,
                   COALESCE(sl.name, '') AS storage_location_name
            FROM batches b
            INNER JOIN products p ON p.id = b.product_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            LEFT JOIN brands br ON br.id = p.brand_id
            LEFT JOIN storage_locations sl ON sl.id = b.storage_location_id
            WHERE b.expiry_date IS NOT NULL
              AND DATE(b.expiry_date) BETWEEN @fromDate AND @toDate
              AND b.quantity > 0";

        var dyn = new DynamicParameters(new { fromDate, toDate });
        if (!string.IsNullOrWhiteSpace(supplierId))
        {
            sql += " AND p.supplier_id = @supplierId";
            dyn.Add("supplierId", supplierId);
        }
        if (!string.IsNullOrWhiteSpace(brandId))
        {
            sql += " AND p.brand_id = @brandId";
            dyn.Add("brandId", brandId);
        }
        if (!string.IsNullOrWhiteSpace(productId))
        {
            sql += " AND p.id = @productId";
            dyn.Add("productId", productId);
        }
        if (!string.IsNullOrWhiteSpace(storageLocationId))
        {
            sql += " AND (b.storage_location_id = @storageLocationId OR (b.storage_location_id IS NULL AND p.storage_location_id = @storageLocationId))";
            dyn.Add("storageLocationId", storageLocationId);
        }

        sql += " ORDER BY b.expiry_date ASC, p.name ASC LIMIT 2000";
        var rows = await Db.QueryListAsync(sql, dyn);
        foreach (var r in rows)
        {
            var qty = ToDouble(r, "quantity");
            var unitCost = ToDouble(r, "unit_purchase_price");
            r["purchase_value"] = Math.Round(qty * unitCost, 2);
            var expStr = FormatDateOnly(GetNullableStr(r, "expiry_date"));
            r["days_remaining"] = DateTime.TryParse(expStr, out var expDt) ? (int?)(expDt.Date - todayDt).Days : null;
        }
        var totalQuantity = rows.Sum(r => (int)ToLong(r, "quantity"));

        return new Dictionary<string, object?>
        {
            ["rows"] = rows,
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_batches"] = rows.Count,
                ["total_quantity"] = totalQuantity
            },
            ["filters"] = new Dictionary<string, object?>
            {
                ["from"] = fromDate,
                ["to"] = toDate,
                ["supplier_id"] = supplierId,
                ["brand_id"] = brandId,
                ["product_id"] = productId
            }
        };
    }

    // =========================================================================
    // 17 & 18. Purchase Returns (/api/reports/purchase-returns & /pdf)
    // =========================================================================

    [HttpGet("purchase-returns")]
    public async Task<IActionResult> PurchaseReturns()
    {
        var authErr = await AuthorizeReportActionAsync("report.purchase-returns.view");
        if (authErr != null) return authErr;

        var (err, data) = await BuildPurchaseReturnsDataAsync();
        if (err != null) return err;
        return Ok(data);
    }

    [HttpGet("purchase-returns/pdf")]
    public async Task<IActionResult> PurchaseReturnsPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.purchase-returns.export", "report.purchase-returns.view");
        if (authErr != null) return authErr;

        var (err, data) = await BuildPurchaseReturnsDataAsync();
        if (err != null) return err;

        var summary = (Dictionary<string, object?>)data!["summary"]!;
        var rows = data.GetValueOrDefault("rows") as List<Dictionary<string, object?>> ?? new();
        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "purchase-returns",
            FromDate = Request.Query["from"].FirstOrDefault(),
            ToDate = Request.Query["to"].FirstOrDefault(),
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Returns", GetStr(summary, "return_count")),
                new("Items", GetStr(summary, "item_count")),
                new("Quantity", GetStr(summary, "quantity")),
                new("Total Value", $"Rs. {ToDouble(summary, "total"):N2}")
            ],
            Columns =
            [
                new() { Header = "Date", RelativeWidth = 1.2f },
                new() { Header = "Return #", RelativeWidth = 1.4f },
                new() { Header = "Supplier", RelativeWidth = 2.4f },
                new() { Header = "Orig Inv #", RelativeWidth = 1.4f },
                new() { Header = "Total", RelativeWidth = 1.4f, AlignRight = true }
            ],
            Rows = rows.Select(r => new List<string>
            {
                GetStr(r, "date"),
                GetStr(r, "posted_number"),
                GetStr(r, "supplier_name"),
                GetStr(r, "invoice_number"),
                $"{ToDouble(r, "total"):N2}"
            }).ToList(),
            FooterTotals = [new("Total Returns", $"Rs. {ToDouble(summary, "total"):N2}", isGrandTotal: true)]
        });
        return DeliverPdf(pdf, "purchase-returns-report.pdf");
    }

    private async Task<(IActionResult? err, Dictionary<string, object?>? data)> BuildPurchaseReturnsDataAsync()
    {
        var (dateErr, from, to) = ValidateStrictDateRange();
        if (dateErr != null) return (dateErr, null);

        var supplierId = Request.Query["supplier_id"].FirstOrDefault();
        var productId = Request.Query["product_id"].FirstOrDefault();

        var sql = @"
            SELECT pr.*,
                   s.name AS supplier_name,
                   COALESCE(pi.invoice_number, pi.posted_number) AS invoice_number
            FROM purchase_returns pr
            LEFT JOIN suppliers s ON s.id = pr.supplier_id
            LEFT JOIN purchase_invoices pi ON pi.id = pr.purchase_invoice_id
            WHERE DATE(pr.date) BETWEEN @from AND @to";

        var dyn = new DynamicParameters(new { from, to });
        if (!string.IsNullOrWhiteSpace(supplierId))
        {
            sql += " AND pr.supplier_id = @supplierId";
            dyn.Add("supplierId", supplierId);
        }
        if (!string.IsNullOrWhiteSpace(productId))
        {
            sql += " AND EXISTS (SELECT 1 FROM purchase_return_items pri WHERE pri.purchase_return_id = pr.id AND pri.product_id = @productId)";
            dyn.Add("productId", productId);
        }

        sql += " ORDER BY pr.date ASC, pr.id ASC";
        var returns = await Db.QueryListAsync(sql, dyn);

        var rows = new List<Dictionary<string, object?>>();
        int itemCount = 0;
        int totalQty = 0;
        double sumTotal = 0;

        foreach (var ret in returns)
        {
            var retId = ToLong(ret, "id");
            var itemSql = @"
                SELECT pri.*, p.name AS product_name, p.product_code
                FROM purchase_return_items pri
                LEFT JOIN products p ON p.id = pri.product_id
                WHERE pri.purchase_return_id = @retId";
            var itemParams = new DynamicParameters(new { retId });
            if (!string.IsNullOrWhiteSpace(productId))
            {
                itemSql += " AND pri.product_id = @productId";
                itemParams.Add("productId", productId);
            }

            var items = await Db.QueryListAsync(itemSql, itemParams);
            var mappedItems = new List<Dictionary<string, object?>>();

            foreach (var it in items)
            {
                var q = (int)ToLong(it, "return_unit_quantity");
                totalQty += q;
                mappedItems.Add(new Dictionary<string, object?>
                {
                    ["id"] = ToLong(it, "id"),
                    ["product_name"] = GetNullableStr(it, "product_name"),
                    ["product_code"] = GetNullableStr(it, "product_code"),
                    ["batch"] = GetNullableStr(it, "batch"),
                    ["expiry"] = GetNullableStr(it, "expiry"),
                    ["pack_size"] = (int)ToLong(it, "pack_size"),
                    ["return_pack_quantity"] = (int)ToLong(it, "return_pack_quantity"),
                    ["return_unit_quantity"] = q,
                    ["quantity"] = q,
                    ["unit_purchase_price"] = ToDouble(it, "unit_purchase_price"),
                    ["item_discount_percentage"] = ToDouble(it, "item_discount_percentage"),
                    ["sub_total"] = ToDouble(it, "sub_total")
                });
            }

            itemCount += mappedItems.Count;
            var tot = ToDouble(ret, "total");
            sumTotal += tot;

            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = retId,
                ["posted_number"] = GetNullableStr(ret, "posted_number"),
                ["date"] = FormatDateOnly(GetNullableStr(ret, "date")),
                ["supplier_name"] = GetNullableStr(ret, "supplier_name"),
                ["invoice_number"] = GetNullableStr(ret, "invoice_number"),
                ["remarks"] = GetNullableStr(ret, "remarks"),
                ["gross_total"] = ToDouble(ret, "gross_total"),
                ["discount_amount"] = ToDouble(ret, "discount_amount"),
                ["tax_amount"] = ToDouble(ret, "tax_amount"),
                ["total"] = tot,
                ["items"] = mappedItems
            });
        }

        return (null, new Dictionary<string, object?>
        {
            ["rows"] = rows,
            ["summary"] = new Dictionary<string, object?>
            {
                ["return_count"] = rows.Count,
                ["item_count"] = itemCount,
                ["quantity"] = totalQty,
                ["total"] = Math.Round(sumTotal, 2)
            }
        });
    }

    // =========================================================================
    // 19 & 20. Sale Returns (/api/reports/sale-returns & /pdf)
    // =========================================================================

    [HttpGet("sale-returns")]
    public async Task<IActionResult> SaleReturns()
    {
        var authErr = await AuthorizeReportActionAsync("report.sale-returns.view");
        if (authErr != null) return authErr;

        var (err, data) = await BuildSaleReturnsDataAsync();
        if (err != null) return err;
        return Ok(data);
    }

    [HttpGet("sale-returns/pdf")]
    public async Task<IActionResult> SaleReturnsPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.sale-returns.export", "report.sale-returns.view");
        if (authErr != null) return authErr;

        var (err, data) = await BuildSaleReturnsDataAsync();
        if (err != null) return err;

        var summary = (Dictionary<string, object?>)data!["summary"]!;
        var rows = data.GetValueOrDefault("rows") as List<Dictionary<string, object?>> ?? new();
        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "sale-returns",
            FromDate = Request.Query["from"].FirstOrDefault(),
            ToDate = Request.Query["to"].FirstOrDefault(),
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Returns", GetStr(summary, "return_count")),
                new("Items", GetStr(summary, "item_count")),
                new("Quantity", GetStr(summary, "quantity")),
                new("Total Value", $"Rs. {ToDouble(summary, "total"):N2}")
            ],
            Columns =
            [
                new() { Header = "Date", RelativeWidth = 1.2f },
                new() { Header = "Return #", RelativeWidth = 1.4f },
                new() { Header = "Customer", RelativeWidth = 2.4f },
                new() { Header = "Orig Inv #", RelativeWidth = 1.4f },
                new() { Header = "Total", RelativeWidth = 1.4f, AlignRight = true }
            ],
            Rows = rows.Select(r => new List<string>
            {
                GetStr(r, "date"),
                GetStr(r, "posted_number"),
                GetNullableStr(r, "customer_name") ?? "Walk-in",
                GetStr(r, "invoice_number"),
                $"{ToDouble(r, "total"):N2}"
            }).ToList(),
            FooterTotals = [new("Total Returns", $"Rs. {ToDouble(summary, "total"):N2}", isGrandTotal: true)]
        });
        return DeliverPdf(pdf, "sale-returns-report.pdf");
    }

    private async Task<(IActionResult? err, Dictionary<string, object?>? data)> BuildSaleReturnsDataAsync()
    {
        var (dateErr, from, to) = ValidateStrictDateRange();
        if (dateErr != null) return (dateErr, null);

        var customerId = Request.Query["customer_id"].FirstOrDefault();
        var productId = Request.Query["product_id"].FirstOrDefault();

        var sql = @"
            SELECT sr.*,
                   c.name AS customer_name,
                   u.name AS user_name,
                   si.posted_number AS invoice_number
            FROM sale_returns sr
            LEFT JOIN customers c ON c.id = sr.customer_id
            LEFT JOIN users u ON u.id = sr.user_id
            LEFT JOIN sale_invoices si ON si.id = sr.sale_invoice_id
            WHERE DATE(sr.date) BETWEEN @from AND @to";

        var dyn = new DynamicParameters(new { from, to });
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            sql += " AND sr.customer_id = @customerId";
            dyn.Add("customerId", customerId);
        }
        if (!string.IsNullOrWhiteSpace(productId))
        {
            sql += " AND EXISTS (SELECT 1 FROM sale_return_items sri WHERE sri.sale_return_id = sr.id AND sri.product_id = @productId)";
            dyn.Add("productId", productId);
        }

        sql += " ORDER BY sr.date ASC, sr.id ASC";
        var returns = await Db.QueryListAsync(sql, dyn);

        var rows = new List<Dictionary<string, object?>>();
        int itemCount = 0;
        int totalQty = 0;
        double sumTotal = 0;

        foreach (var ret in returns)
        {
            var retId = ToLong(ret, "id");
            var itemSql = @"
                SELECT sri.*, p.name AS product_name, p.product_code
                FROM sale_return_items sri
                LEFT JOIN products p ON p.id = sri.product_id
                WHERE sri.sale_return_id = @retId";
            var itemParams = new DynamicParameters(new { retId });
            if (!string.IsNullOrWhiteSpace(productId))
            {
                itemSql += " AND sri.product_id = @productId";
                itemParams.Add("productId", productId);
            }

            var items = await Db.QueryListAsync(itemSql, itemParams);
            var mappedItems = new List<Dictionary<string, object?>>();

            foreach (var it in items)
            {
                var q = (int)ToLong(it, "unit_return_quantity");
                totalQty += q;
                mappedItems.Add(new Dictionary<string, object?>
                {
                    ["id"] = ToLong(it, "id"),
                    ["product_name"] = GetNullableStr(it, "product_name"),
                    ["product_code"] = GetNullableStr(it, "product_code"),
                    ["batch"] = GetNullableStr(it, "batch_number"),
                    ["expiry"] = GetNullableStr(it, "expiry"),
                    ["quantity"] = q,
                    ["sale_price"] = ToDouble(it, "unit_sale_price"),
                    ["item_discount_percentage"] = ToDouble(it, "item_discount_percentage"),
                    ["sub_total"] = ToDouble(it, "sub_total")
                });
            }

            itemCount += mappedItems.Count;
            var tot = ToDouble(ret, "total");
            sumTotal += tot;

            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = retId,
                ["posted_number"] = GetNullableStr(ret, "posted_number"),
                ["date"] = FormatDateOnly(GetNullableStr(ret, "date")),
                ["customer_name"] = GetNullableStr(ret, "customer_name"),
                ["user_name"] = GetNullableStr(ret, "user_name"),
                ["invoice_number"] = GetNullableStr(ret, "invoice_number"),
                ["gross_total"] = ToDouble(ret, "gross_total"),
                ["discount_amount"] = ToDouble(ret, "discount_amount"),
                ["tax_amount"] = ToDouble(ret, "tax_amount"),
                ["total"] = tot,
                ["items"] = mappedItems
            });
        }

        return (null, new Dictionary<string, object?>
        {
            ["rows"] = rows,
            ["summary"] = new Dictionary<string, object?>
            {
                ["return_count"] = rows.Count,
                ["item_count"] = itemCount,
                ["quantity"] = totalQty,
                ["total"] = Math.Round(sumTotal, 2)
            }
        });
    }

    // =========================================================================
    // 21 & 22. Purchase Summary (/api/reports/purchase-summary & /pdf)
    // =========================================================================

    [HttpGet("purchase-summary")]
    public async Task<IActionResult> PurchaseSummary()
    {
        var authErr = await AuthorizeReportActionAsync("report.purchase-summary.view");
        if (authErr != null) return authErr;

        var (err, data) = await BuildPurchaseSummaryDataAsync();
        if (err != null) return err;
        return Ok(data);
    }

    [HttpGet("purchase-summary/pdf")]
    public async Task<IActionResult> PurchaseSummaryPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.purchase-summary.export", "report.purchase-summary.view");
        if (authErr != null) return authErr;

        var (err, data) = await BuildPurchaseSummaryDataAsync();
        if (err != null) return err;

        var summary = (Dictionary<string, object?>)data!["summary"]!;
        var rows = data.GetValueOrDefault("rows") as List<Dictionary<string, object?>> ?? new();
        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "purchase-summary",
            FromDate = Request.Query["from"].FirstOrDefault(),
            ToDate = Request.Query["to"].FirstOrDefault(),
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Invoices", GetStr(summary, "invoice_count")),
                new("Items", GetStr(summary, "item_count")),
                new("Qty", GetStr(summary, "quantity")),
                new("Total", $"Rs. {ToDouble(summary, "invoice_total"):N2}"),
                new("Paid", $"Rs. {ToDouble(summary, "total_paid"):N2}")
            ],
            Columns =
            [
                new() { Header = "Date", RelativeWidth = 1.2f },
                new() { Header = "GRN #", RelativeWidth = 1.3f },
                new() { Header = "Supplier", RelativeWidth = 2.2f },
                new() { Header = "Items", RelativeWidth = 0.7f, AlignRight = true },
                new() { Header = "Qty", RelativeWidth = 0.8f, AlignRight = true },
                new() { Header = "Total", RelativeWidth = 1.3f, AlignRight = true },
                new() { Header = "Paid", RelativeWidth = 1.3f, AlignRight = true }
            ],
            Rows = rows.Select(r => new List<string>
            {
                GetStr(r, "date"),
                GetStr(r, "posted_number"),
                GetStr(r, "supplier_name"),
                $"{ToLong(r, "item_count")}",
                $"{ToLong(r, "quantity")}",
                $"{ToDouble(r, "invoice_total"):N2}",
                $"{ToDouble(r, "total_paid"):N2}"
            }).ToList(),
            FooterTotals =
            [
                new("Total Invoiced", $"Rs. {ToDouble(summary, "invoice_total"):N2}"),
                new("Total Paid", $"Rs. {ToDouble(summary, "total_paid"):N2}", isGrandTotal: true)
            ]
        });
        return DeliverPdf(pdf, "purchase-summary-report.pdf");
    }

    private async Task<(IActionResult? err, Dictionary<string, object?>? data)> BuildPurchaseSummaryDataAsync()
    {
        var (dateErr, from, to) = ValidateStrictDateRange();
        if (dateErr != null) return (dateErr, null);

        var supplierId = Request.Query["supplier_id"].FirstOrDefault();
        var productId = Request.Query["product_id"].FirstOrDefault();

        var sql = @"
            SELECT pi.*, s.name AS supplier_name
            FROM purchase_invoices pi
            LEFT JOIN suppliers s ON s.id = pi.supplier_id
            WHERE DATE(pi.posted_date) BETWEEN @from AND @to
              AND (pi.status IS NULL OR pi.status != 'reversed')";

        var dyn = new DynamicParameters(new { from, to });
        if (!string.IsNullOrWhiteSpace(supplierId))
        {
            sql += " AND pi.supplier_id = @supplierId";
            dyn.Add("supplierId", supplierId);
        }
        if (!string.IsNullOrWhiteSpace(productId))
        {
            sql += " AND EXISTS (SELECT 1 FROM purchase_invoice_items pii WHERE pii.purchase_invoice_id = pi.id AND pii.product_id = @productId)";
            dyn.Add("productId", productId);
        }

        sql += " ORDER BY pi.posted_date ASC, pi.id ASC";
        var invoices = await Db.QueryListAsync(sql, dyn);

        var rows = new List<Dictionary<string, object?>>();
        int sumItemCount = 0;
        int sumQty = 0;
        double sumItemsValue = 0;
        double sumInvoiceTotal = 0;
        double sumTotalPaid = 0;

        foreach (var inv in invoices)
        {
            var invId = ToLong(inv, "id");
            var itemSql = @"
                SELECT pii.*, p.name AS product_name, p.product_code
                FROM purchase_invoice_items pii
                LEFT JOIN products p ON pii.product_id = p.id
                WHERE pii.purchase_invoice_id = @invId";
            var itemParams = new DynamicParameters(new { invId });
            if (!string.IsNullOrWhiteSpace(productId))
            {
                itemSql += " AND pii.product_id = @productId";
                itemParams.Add("productId", productId);
            }

            var items = await Db.QueryListAsync(itemSql, itemParams);
            int invQty = 0;
            double invItemsVal = 0;
            var mappedItems = new List<Dictionary<string, object?>>();

            foreach (var it in items)
            {
                var q = (int)ToLong(it, "quantity");
                var st = ToDouble(it, "sub_total");
                invQty += q;
                invItemsVal += st;
                mappedItems.Add(new Dictionary<string, object?>
                {
                    ["id"] = ToLong(it, "id"),
                    ["product_code"] = GetNullableStr(it, "product_code"),
                    ["product_name"] = GetNullableStr(it, "product_name"),
                    ["batch"] = GetNullableStr(it, "batch"),
                    ["quantity"] = q,
                    ["unit_purchase_price"] = ToDouble(it, "unit_purchase_price"),
                    ["sub_total"] = st
                });
            }

            invItemsVal = Math.Round(invItemsVal, 2);
            var invTotal = ToDouble(inv, "total_amount");
            var invPaid = ToDouble(inv, "total_paid");

            sumItemCount += mappedItems.Count;
            sumQty += invQty;
            sumItemsValue += invItemsVal;
            sumInvoiceTotal += invTotal;
            sumTotalPaid += invPaid;

            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = invId,
                ["date"] = FormatDateOnly(GetNullableStr(inv, "posted_date")),
                ["posted_number"] = GetNullableStr(inv, "posted_number"),
                ["invoice_number"] = GetNullableStr(inv, "invoice_number"),
                ["supplier_name"] = GetNullableStr(inv, "supplier_name"),
                ["item_count"] = mappedItems.Count,
                ["quantity"] = invQty,
                ["items_value"] = invItemsVal,
                ["invoice_total"] = invTotal,
                ["total_paid"] = invPaid,
                ["items"] = mappedItems
            });
        }

        return (null, new Dictionary<string, object?>
        {
            ["rows"] = rows,
            ["summary"] = new Dictionary<string, object?>
            {
                ["invoice_count"] = rows.Count,
                ["item_count"] = sumItemCount,
                ["quantity"] = sumQty,
                ["items_value"] = Math.Round(sumItemsValue, 2),
                ["invoice_total"] = Math.Round(sumInvoiceTotal, 2),
                ["total_paid"] = Math.Round(sumTotalPaid, 2)
            }
        });
    }

    // =========================================================================
    // 23, 24, 25 & 26. Outstanding Aging & Overdue Work Queue
    // =========================================================================

    [HttpGet("outstanding-aging")]
    public async Task<IActionResult> OutstandingAging()
    {
        var authErr = await AuthorizeReportActionAsync("report.outstanding-aging.view");
        if (authErr != null) return authErr;

        var data = await BuildOutstandingAgingDataAsync();
        return Ok(data);
    }

    [HttpGet("outstanding-aging/pdf")]
    public async Task<IActionResult> OutstandingAgingPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.outstanding-aging.export", "report.outstanding-aging.view");
        if (authErr != null) return authErr;

        var data = await BuildOutstandingAgingDataAsync();
        var custRows = data.GetValueOrDefault("customers") as List<Dictionary<string, object?>> ?? new();
        var supRows = data.GetValueOrDefault("suppliers") as List<Dictionary<string, object?>> ?? new();

        var tableRows = new List<List<string>>();
        foreach (var c in custRows)
        {
            tableRows.Add(["Customer", GetStr(c, "name"), $"{ToDouble(c, "b_0_30"):N2}", $"{ToDouble(c, "b_31_60"):N2}", $"{ToDouble(c, "b_61_90"):N2}", $"{ToDouble(c, "b_90_plus"):N2}", $"{ToDouble(c, "total_due"):N2}"]);
        }
        foreach (var s in supRows)
        {
            tableRows.Add(["Supplier", GetStr(s, "name"), $"{ToDouble(s, "b_0_30"):N2}", $"{ToDouble(s, "b_31_60"):N2}", $"{ToDouble(s, "b_61_90"):N2}", $"{ToDouble(s, "b_90_plus"):N2}", $"{ToDouble(s, "total_due"):N2}"]);
        }

        var custTotal = custRows.Sum(r => ToDouble(r, "total_due"));
        var supTotal = supRows.Sum(r => ToDouble(r, "total_due"));
        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "outstanding-aging",
            ToDate = GetNullableStr(data, "as_of"),
            Subtitle = $"Aging By: {GetStr(data, "aging_by")}",
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Receivables (Customers)", $"Rs. {custTotal:N2}"),
                new("Payables (Suppliers)", $"Rs. {supTotal:N2}"),
                new("Parties Listed", tableRows.Count.ToString())
            ],
            Columns =
            [
                new() { Header = "Type", RelativeWidth = 1.0f },
                new() { Header = "Party Name", RelativeWidth = 2.4f },
                new() { Header = "0–30d", RelativeWidth = 1.1f, AlignRight = true },
                new() { Header = "31–60d", RelativeWidth = 1.1f, AlignRight = true },
                new() { Header = "61–90d", RelativeWidth = 1.1f, AlignRight = true },
                new() { Header = "90+d", RelativeWidth = 1.1f, AlignRight = true },
                new() { Header = "Total Due", RelativeWidth = 1.3f, AlignRight = true }
            ],
            Rows = tableRows,
            FooterTotals =
            [
                new("Total Receivables", $"Rs. {custTotal:N2}"),
                new("Total Payables", $"Rs. {supTotal:N2}", isGrandTotal: true)
            ]
        });
        return DeliverPdf(pdf, "outstanding-aging-report.pdf");
    }

    [HttpGet("overdue-work-queue")]
    public async Task<IActionResult> OverdueWorkQueue()
    {
        var authErr = await AuthorizeReportActionAsync("report.outstanding-aging.view");
        if (authErr != null) return authErr;

        var (err, data) = await BuildOverdueWorkQueueDataAsync();
        if (err != null) return err;
        return Ok(data);
    }

    [HttpGet("overdue-work-queue/pdf")]
    public async Task<IActionResult> OverdueWorkQueuePdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.outstanding-aging.export", "report.outstanding-aging.view");
        if (authErr != null) return authErr;

        var (err, data) = await BuildOverdueWorkQueueDataAsync();
        if (err != null) return err;

        var items = data!.GetValueOrDefault("items") as List<Dictionary<string, object?>> ?? new();
        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "overdue-work-queue",
            ToDate = GetNullableStr(data, "as_of"),
            Subtitle = $"Party Type: {GetStr(data, "party_type")}",
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Overdue Parties", GetStr(data, "total_parties")),
                new("Total Overdue", $"Rs. {ToDouble(data, "total_overdue_sum"):N2}")
            ],
            Columns =
            [
                new() { Header = "Party", RelativeWidth = 2.6f },
                new() { Header = "Phone", RelativeWidth = 1.5f },
                new() { Header = "Invoices", RelativeWidth = 0.9f, AlignRight = true },
                new() { Header = "Max Days", RelativeWidth = 1.0f, AlignRight = true },
                new() { Header = "Overdue Amount", RelativeWidth = 1.5f, AlignRight = true }
            ],
            Rows = items.Select(it => new List<string>
            {
                GetStr(it, "party_name"),
                GetStr(it, "phone"),
                $"{ToLong(it, "overdue_invoices_count")}",
                $"{ToLong(it, "max_days_overdue")}",
                $"{ToDouble(it, "overdue_amount"):N2}"
            }).ToList(),
            FooterTotals = [new("Total Overdue", $"Rs. {ToDouble(data, "total_overdue_sum"):N2}", isGrandTotal: true)]
        });
        return DeliverPdf(pdf, "overdue-work-queue-report.pdf");
    }

    private async Task<Dictionary<string, object?>> BuildOutstandingAgingDataAsync(string? forceAgingBy = null)
    {
        var rawAsOf = Request.Query["as_of"].FirstOrDefault();
        var asOfDt = !string.IsNullOrWhiteSpace(rawAsOf) && DateTime.TryParse(rawAsOf, out var parsed)
            ? parsed.Date
            : DateTime.Now.Date;
        var asOfStr = asOfDt.ToString("yyyy-MM-dd");

        var agingBy = forceAgingBy ?? Request.Query["aging_by"].FirstOrDefault() ?? "due_date";
        if (agingBy != "due_date" && agingBy != "invoice_date")
        {
            agingBy = "due_date";
        }

        var customers = await BuildPartyAgingListAsync(
            "customer_ledgers", "customers", "customer_id",
            "invoice_total", "total_received", "credited_amount",
            asOfDt, asOfStr, agingBy);

        var suppliers = await BuildPartyAgingListAsync(
            "supplier_ledgers", "suppliers", "supplier_id",
            "invoice_total", "total_paid", "debited_amount",
            asOfDt, asOfStr, agingBy);

        return new Dictionary<string, object?>
        {
            ["as_of"] = asOfStr,
            ["aging_by"] = agingBy,
            ["customers"] = customers,
            ["suppliers"] = suppliers,
            ["summary"] = new Dictionary<string, object?>
            {
                ["customers"] = SummarizeAgingBuckets(customers),
                ["suppliers"] = SummarizeAgingBuckets(suppliers)
            }
        };
    }

    private async Task<List<Dictionary<string, object?>>> BuildPartyAgingListAsync(
        string ledgerTable,
        string partyTable,
        string partyKey,
        string amountField,
        string paidField,
        string paymentField,
        DateTime asOfDt,
        string asOfStr,
        string agingBy)
    {
        if (!await Db.HasTableAsync(ledgerTable) || !await Db.HasTableAsync(partyTable))
        {
            return new List<Dictionary<string, object?>>();
        }

        var hasCreditStatus = await Db.HasColumnAsync(partyTable, "credit_status");
        var creditStatusSel = hasCreditStatus ? ", p.credit_status" : "";

        var rows = await Db.QueryListAsync($@"
            SELECT l.*,
                   p.name AS party_name,
                   p.phone AS party_phone,
                   p.credit_limit,
                   p.credit_days
                   {creditStatusSel}
            FROM {ledgerTable} l
            INNER JOIN {partyTable} p ON p.id = l.{partyKey}
            WHERE DATE(l.entry_date) <= @asOfStr
            ORDER BY l.entry_date ASC, l.id ASC",
            new { asOfStr });

        var grouped = rows.GroupBy(r => ToLong(r, partyKey));
        var partyList = new List<Dictionary<string, object?>>();

        foreach (var grp in grouped)
        {
            var entries = grp.ToList();
            var obligations = new List<Dictionary<string, object?>>();
            double availableCredit = 0.0;

            void ApplyCredit()
            {
                foreach (var ob in obligations)
                {
                    if (availableCredit <= 0) break;
                    var outst = ToDouble(ob, "outstanding");
                    if (outst <= 0) continue;
                    var applied = Math.Min(outst, availableCredit);
                    ob["payments_applied"] = Math.Round(ToDouble(ob, "payments_applied") + applied, 2);
                    ob["outstanding"] = Math.Round(outst - applied, 2);
                    availableCredit = Math.Round(availableCredit - applied, 2);
                }
            }

            foreach (var entry in entries)
            {
                var entryType = GetStr(entry, "entry_type");
                if (entryType == "invoice" || entryType == "manual")
                {
                    var invAmt = Math.Max(0.0, ToDouble(entry, amountField));
                    var paidAmt = Math.Min(invAmt, Math.Max(0.0, ToDouble(entry, paidField)));
                    var entryDateStr = FormatDateOnly(GetNullableStr(entry, "entry_date")) ?? asOfStr;
                    var dueDateStr = FormatDateOnly(GetNullableStr(entry, "due_date")) ?? entryDateStr;
                    var ledgerId = ToLong(entry, "id");
                    var reference = GetNullableStr(entry, "posted_number")
                                    ?? GetNullableStr(entry, "invoice_number")
                                    ?? (entryType == "manual" ? $"Manual entry #{ledgerId}" : $"Ledger entry #{ledgerId}");

                    obligations.Add(new Dictionary<string, object?>
                    {
                        ["ledger_id"] = ledgerId,
                        ["reference"] = reference,
                        ["entry_type"] = entryType,
                        ["date"] = entryDateStr,
                        ["due_date"] = dueDateStr,
                        ["invoice_amount"] = Math.Round(invAmt, 2),
                        ["paid_on_invoice"] = Math.Round(paidAmt, 2),
                        ["payments_applied"] = 0.0,
                        ["outstanding"] = Math.Round(Math.Max(0.0, invAmt - paidAmt), 2)
                    });
                    ApplyCredit();
                }
                else if (entryType == "payment")
                {
                    availableCredit += Math.Max(0.0, ToDouble(entry, paymentField));
                    ApplyCredit();
                }
            }

            var buckets = new Dictionary<string, double>
            {
                ["current"] = 0,
                ["days_1_30"] = 0,
                ["days_31_60"] = 0,
                ["days_61_90"] = 0,
                ["over_90"] = 0,
                ["days_0_30"] = 0
            };

            var openObligations = new List<Dictionary<string, object?>>();
            foreach (var ob in obligations.Where(o => ToDouble(o, "outstanding") > 0))
            {
                var dateDt = DateTime.TryParse(GetStr(ob, "date"), out var d1) ? d1.Date : asOfDt;
                var dueDt = DateTime.TryParse(GetStr(ob, "due_date"), out var d2) ? d2.Date : dateDt;
                var ageDays = Math.Max(0, (int)(asOfDt - dateDt).TotalDays);
                var isOverdue = dueDt < asOfDt;
                var daysOverdue = isOverdue ? (int)(asOfDt - dueDt).TotalDays : 0;

                string bucket;
                if (agingBy == "due_date")
                {
                    if (dueDt >= asOfDt) bucket = "current";
                    else if (daysOverdue <= 30) bucket = "days_1_30";
                    else if (daysOverdue <= 60) bucket = "days_31_60";
                    else if (daysOverdue <= 90) bucket = "days_61_90";
                    else bucket = "over_90";
                }
                else
                {
                    if (ageDays <= 30) bucket = "days_0_30";
                    else if (ageDays <= 60) bucket = "days_31_60";
                    else if (ageDays <= 90) bucket = "days_61_90";
                    else bucket = "over_90";
                }

                var outst = ToDouble(ob, "outstanding");
                buckets[bucket] += outst;

                ob["age_days"] = ageDays;
                ob["is_overdue"] = isOverdue;
                ob["days_overdue"] = daysOverdue;
                ob["aging_bucket"] = bucket;
                openObligations.Add(ob);
            }

            double totalOverdue;
            if (agingBy == "due_date")
            {
                buckets["days_0_30"] = Math.Round(buckets["current"] + buckets["days_1_30"], 2);
                totalOverdue = Math.Round(buckets["days_1_30"] + buckets["days_31_60"] + buckets["days_61_90"] + buckets["over_90"], 2);
            }
            else
            {
                buckets["days_1_30"] = buckets["days_0_30"];
                totalOverdue = Math.Round(buckets["days_31_60"] + buckets["days_61_90"] + buckets["over_90"], 2);
            }

            var total = Math.Round(openObligations.Sum(o => ToDouble(o, "outstanding")), 2);
            if (total <= 0) continue;

            var first = entries[0];
            double? creditLimit = first.TryGetValue("credit_limit", out var clObj) && clObj != null
                ? ToDouble(first, "credit_limit")
                : null;

            partyList.Add(new Dictionary<string, object?>
            {
                ["party_id"] = grp.Key,
                ["party_name"] = GetNullableStr(first, "party_name"),
                ["party_phone"] = GetNullableStr(first, "party_phone"),
                ["credit_limit"] = creditLimit,
                ["credit_days"] = (int)ToLong(first, "credit_days", 30),
                ["credit_status"] = GetNullableStr(first, "credit_status") ?? "active",
                ["is_limit_exceeded"] = creditLimit.HasValue && creditLimit.Value > 0 && total > creditLimit.Value,
                ["total_overdue"] = totalOverdue,
                ["open_entry_count"] = openObligations.Count,
                ["current"] = Math.Round(buckets["current"], 2),
                ["days_1_30"] = Math.Round(buckets["days_1_30"], 2),
                ["days_31_60"] = Math.Round(buckets["days_31_60"], 2),
                ["days_61_90"] = Math.Round(buckets["days_61_90"], 2),
                ["over_90"] = Math.Round(buckets["over_90"], 2),
                ["days_0_30"] = Math.Round(buckets["days_0_30"], 2),
                ["total"] = total,
                ["entries"] = openObligations
            });
        }

        return partyList.OrderByDescending(p => ToDouble(p, "total")).ToList();
    }

    private static Dictionary<string, object?> SummarizeAgingBuckets(List<Dictionary<string, object?>> parties)
    {
        return new Dictionary<string, object?>
        {
            ["current"] = Math.Round(parties.Sum(p => ToDouble(p, "current")), 2),
            ["days_1_30"] = Math.Round(parties.Sum(p => ToDouble(p, "days_1_30")), 2),
            ["days_0_30"] = Math.Round(parties.Sum(p => ToDouble(p, "days_0_30")), 2),
            ["days_31_60"] = Math.Round(parties.Sum(p => ToDouble(p, "days_31_60")), 2),
            ["days_61_90"] = Math.Round(parties.Sum(p => ToDouble(p, "days_61_90")), 2),
            ["over_90"] = Math.Round(parties.Sum(p => ToDouble(p, "over_90")), 2),
            ["total_overdue"] = Math.Round(parties.Sum(p => ToDouble(p, "total_overdue")), 2),
            ["total"] = Math.Round(parties.Sum(p => ToDouble(p, "total")), 2),
            ["party_count"] = parties.Count,
            ["open_entry_count"] = parties.Sum(p => (int)ToLong(p, "open_entry_count"))
        };
    }

    private async Task<(IActionResult? err, Dictionary<string, object?>? data)> BuildOverdueWorkQueueDataAsync()
    {
        var partyType = (Request.Query["party_type"].FirstOrDefault() ?? "customers").Trim();
        if (partyType != "customers" && partyType != "suppliers")
        {
            return (Unprocessable("Invalid party_type."), null);
        }

        double.TryParse(Request.Query["min_overdue"].FirstOrDefault(), NumberStyles.Any, CultureInfo.InvariantCulture, out var minOverdue);
        var search = (Request.Query["q"].FirstOrDefault() ?? "").Trim();

        var agingData = await BuildOutstandingAgingDataAsync("due_date");
        var rawParties = (List<Dictionary<string, object?>>)agingData[partyType]!;

        var filtered = new List<Dictionary<string, object?>>();
        foreach (var party in rawParties)
        {
            var totOverdue = ToDouble(party, "total_overdue");
            if (totOverdue <= minOverdue) continue;

            if (!string.IsNullOrEmpty(search))
            {
                var pName = GetStr(party, "party_name");
                var pPhone = GetStr(party, "party_phone");
                if (!pName.Contains(search, StringComparison.OrdinalIgnoreCase) &&
                    !pPhone.Contains(search, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            var entries = (List<Dictionary<string, object?>>)party["entries"]!;
            var overdueEntries = entries
                .Where(e => e.TryGetValue("is_overdue", out var io) && io is true && ToDouble(e, "outstanding") > 0)
                .OrderByDescending(e => (int)ToLong(e, "days_overdue"))
                .ToList();

            var copy = new Dictionary<string, object?>(party)
            {
                ["overdue_entries"] = overdueEntries,
                ["max_days_overdue"] = overdueEntries.Count > 0 ? overdueEntries.Max(e => (int)ToLong(e, "days_overdue")) : 0
            };
            filtered.Add(copy);
        }

        filtered = filtered.OrderByDescending(p => ToDouble(p, "total_overdue")).ToList();

        return (null, new Dictionary<string, object?>
        {
            ["as_of"] = agingData["as_of"],
            ["party_type"] = partyType,
            ["total_parties"] = filtered.Count,
            ["total_overdue_sum"] = Math.Round(filtered.Sum(p => ToDouble(p, "total_overdue")), 2),
            ["parties"] = filtered
        });
    }

    // =========================================================================
    // 27 & 28. Reorder Report (/api/reports/reorder & /pdf)
    // =========================================================================

    [HttpGet("reorder")]
    public async Task<IActionResult> ReorderReport()
    {
        var authErr = await AuthorizeReportActionAsync("report.reorder.view");
        if (authErr != null) return authErr;

        var (err, data) = await BuildReorderReportDataAsync();
        if (err != null) return err;
        return Ok(data);
    }

    [HttpGet("reorder/pdf")]
    public async Task<IActionResult> ReorderReportPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.reorder.export", "report.reorder.view");
        if (authErr != null) return authErr;

        var (err, data) = await BuildReorderReportDataAsync();
        if (err != null) return err;

        var summary = (Dictionary<string, object?>)data!["summary"]!;
        var rows = data.GetValueOrDefault("rows") as List<Dictionary<string, object?>> ?? new();
        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "reorder",
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Low-Stock Products", GetStr(summary, "product_count")),
                new("Current Units", GetStr(summary, "total_quantity")),
                new("Suggested Order", GetStr(summary, "suggested_quantity")),
                new("Est. Cost", $"Rs. {ToDouble(summary, "estimated_reorder_value"):N2}")
            ],
            Columns =
            [
                new() { Header = "Code", RelativeWidth = 1.2f },
                new() { Header = "Product Name", RelativeWidth = 2.8f },
                new() { Header = "Brand", RelativeWidth = 1.4f },
                new() { Header = "On Hand", RelativeWidth = 0.9f, AlignRight = true },
                new() { Header = "Pack Size", RelativeWidth = 0.9f, AlignRight = true },
                new() { Header = "Suggested", RelativeWidth = 1.0f, AlignRight = true },
                new() { Header = "Est. Cost", RelativeWidth = 1.3f, AlignRight = true }
            ],
            Rows = rows.Select(r => new List<string>
            {
                GetStr(r, "product_code"),
                GetStr(r, "name"),
                GetStr(r, "brand_name"),
                $"{ToLong(r, "quantity")}",
                $"{ToLong(r, "pack_size")}",
                $"{ToLong(r, "suggested_reorder_quantity")}",
                $"{ToDouble(r, "estimated_reorder_value"):N2}"
            }).ToList(),
            FooterTotals = [new("Estimated Reorder Cost", $"Rs. {ToDouble(summary, "estimated_reorder_value"):N2}", isGrandTotal: true)]
        });
        return DeliverPdf(pdf, "reorder-report.pdf");
    }

    private async Task<(IActionResult? err, Dictionary<string, object?>? data)> BuildReorderReportDataAsync()
    {
        var supplierIdStr = Request.Query["supplier_id"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(supplierIdStr) || !long.TryParse(supplierIdStr, out var supplierId))
        {
            return (Unprocessable("The supplier id field is required.", new Dictionary<string, string[]>
            {
                ["supplier_id"] = new[] { "The supplier id field is required." }
            }), null);
        }

        var supExists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM suppliers WHERE id = @supplierId", new { supplierId });
        if (supExists == 0)
        {
            return (Unprocessable("The selected supplier id is invalid.", new Dictionary<string, string[]>
            {
                ["supplier_id"] = new[] { "The selected supplier id is invalid." }
            }), null);
        }

        var brandIdStr = Request.Query["brand_id"].FirstOrDefault();
        var hasProdSuppliers = await Db.HasTableAsync("product_suppliers");

        var supClause = hasProdSuppliers
            ? "AND (p.supplier_id = @supplierId OR EXISTS (SELECT 1 FROM product_suppliers ps WHERE ps.product_id = p.id AND ps.supplier_id = @supplierId))"
            : "AND p.supplier_id = @supplierId";

        var sql = $@"
            SELECT p.*,
                   c.name AS category_name,
                   b.name AS brand_name,
                   s.name AS supplier_name
            FROM products p
            LEFT JOIN categories c ON c.id = p.category_id
            LEFT JOIN brands b ON b.id = p.brand_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            WHERE p.quantity IS NOT NULL
              AND p.pack_size > 0
              AND p.quantity < p.pack_size
              {supClause}";

        var dyn = new DynamicParameters(new { supplierId });
        if (!string.IsNullOrWhiteSpace(brandIdStr))
        {
            sql += " AND p.brand_id = @brandId";
            dyn.Add("brandId", brandIdStr);
        }

        sql += " ORDER BY (p.quantity * 1.0 / NULLIF(p.pack_size, 0)) ASC, p.name ASC";
        var products = await Db.QueryListAsync(sql, dyn);

        var rows = new List<Dictionary<string, object?>>();
        int sumQty = 0;
        int sumSuggested = 0;
        double sumEstValue = 0;

        foreach (var p in products)
        {
            var qty = (int)ToLong(p, "quantity");
            var packSize = (int)ToLong(p, "pack_size");
            var suggested = Math.Max(0, packSize - qty);
            var unitCost = ToDouble(p, "unit_purchase_price");
            var estVal = Math.Round(suggested * unitCost, 2);

            sumQty += qty;
            sumSuggested += suggested;
            sumEstValue += estVal;

            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = ToLong(p, "id"),
                ["product_code"] = GetNullableStr(p, "product_code"),
                ["name"] = GetNullableStr(p, "name"),
                ["category_name"] = GetNullableStr(p, "category_name"),
                ["brand_name"] = GetNullableStr(p, "brand_name"),
                ["supplier_name"] = GetNullableStr(p, "supplier_name"),
                ["quantity"] = qty,
                ["pack_size"] = packSize,
                ["suggested_reorder_quantity"] = suggested,
                ["unit_purchase_price"] = unitCost,
                ["estimated_reorder_value"] = estVal
            });
        }

        return (null, new Dictionary<string, object?>
        {
            ["rows"] = rows,
            ["summary"] = new Dictionary<string, object?>
            {
                ["product_count"] = rows.Count,
                ["total_quantity"] = sumQty,
                ["suggested_quantity"] = sumSuggested,
                ["estimated_reorder_value"] = Math.Round(sumEstValue, 2)
            },
            ["threshold"] = "On-hand quantity below one pack (product pack size)"
        });
    }

    // =========================================================================
    // 29 & 30. Net Profit (/api/reports/net-profit & /pdf)
    // =========================================================================

    [HttpGet("net-profit")]
    public async Task<IActionResult> NetProfit()
    {
        var authErr = await AuthorizeReportActionAsync("report.net-profit.view");
        if (authErr != null) return authErr;

        var (err, data) = await BuildNetProfitDataAsync();
        if (err != null) return err;
        return Ok(data);
    }

    [HttpGet("net-profit/pdf")]
    public async Task<IActionResult> NetProfitPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.net-profit.export", "report.net-profit.view");
        if (authErr != null) return authErr;

        var (err, data) = await BuildNetProfitDataAsync();
        if (err != null) return err;

        var kpis = (Dictionary<string, object?>)data!["kpis"]!;
        var expRows = data.GetValueOrDefault("expenses_by_category") as List<Dictionary<string, object?>> ?? new();
        var tableRows = new List<List<string>>
        {
            new() { "Revenue", "Net Sales", $"Rs. {ToDouble(kpis, "net_sales"):N2}" },
            new() { "Direct Cost", "Cost of Goods Sold (COGS)", $"Rs. {ToDouble(kpis, "cost_of_goods_sold"):N2}" },
            new() { "Profit", "Gross Profit", $"Rs. {ToDouble(kpis, "gross_profit"):N2}" }
        };
        foreach (var ex in expRows)
        {
            tableRows.Add(["Operating Expense", GetStr(ex, "category_name"), $"Rs. {ToDouble(ex, "total"):N2}"]);
        }
        tableRows.Add(["Summary", "Total Operating Expenses", $"Rs. {ToDouble(kpis, "total_expenses"):N2}"]);
        tableRows.Add(["Bottom Line", "NET PROFIT", $"Rs. {ToDouble(kpis, "net_profit"):N2}"]);

        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "net-profit",
            FromDate = Request.Query["from"].FirstOrDefault(),
            ToDate = Request.Query["to"].FirstOrDefault(),
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Net Sales", $"Rs. {ToDouble(kpis, "net_sales"):N2}"),
                new("COGS", $"Rs. {ToDouble(kpis, "cost_of_goods_sold"):N2}"),
                new("Gross Profit", $"Rs. {ToDouble(kpis, "gross_profit"):N2}"),
                new("Operating Expenses", $"Rs. {ToDouble(kpis, "total_expenses"):N2}"),
                new("Net Profit", $"Rs. {ToDouble(kpis, "net_profit"):N2}")
            ],
            Columns =
            [
                new() { Header = "Section", RelativeWidth = 1.6f },
                new() { Header = "Particular", RelativeWidth = 3.4f },
                new() { Header = "Amount", RelativeWidth = 1.8f, AlignRight = true }
            ],
            Rows = tableRows,
            FooterTotals =
            [
                new("Gross Profit", $"Rs. {ToDouble(kpis, "gross_profit"):N2}"),
                new("Total Expenses", $"Rs. {ToDouble(kpis, "total_expenses"):N2}"),
                new("Net Profit", $"Rs. {ToDouble(kpis, "net_profit"):N2}", isGrandTotal: true)
            ]
        });
        return DeliverPdf(pdf, "net-profit-report.pdf");
    }

    private async Task<(IActionResult? err, Dictionary<string, object?>? data)> BuildNetProfitDataAsync()
    {
        var (dateErr, from, to) = ValidateStrictDateRange();
        if (dateErr != null) return (dateErr, null);

        var salesData = await Db.QuerySingleOrDefaultAsync(@"
            SELECT COUNT(si.id) AS total_invoices,
                   SUM(COALESCE(si.gross_amount, 0)) AS gross_sales,
                   SUM(COALESCE(si.discount_amount, 0)) AS total_discounts,
                   SUM(COALESCE(si.tax_amount, 0)) AS total_taxes,
                   SUM(COALESCE(si.total, 0)) AS net_invoiced_sales
            FROM sale_invoices si
            WHERE DATE(si.date) BETWEEN @from AND @to
              AND (si.status IS NULL OR si.status != 'reversed')",
            new { from, to });

        var returnsData = await Db.QuerySingleOrDefaultAsync(@"
            SELECT COUNT(sr.id) AS total_returns,
                   SUM(COALESCE(sr.total, 0)) AS total_returns_amount
            FROM sale_returns sr
            WHERE DATE(sr.date) BETWEEN @from AND @to",
            new { from, to });

        var grossSales = Math.Round(ToDouble(salesData, "gross_sales"), 2);
        var totalSales = Math.Round(ToDouble(salesData, "net_invoiced_sales"), 2);
        var totalReturns = Math.Round(ToDouble(returnsData, "total_returns_amount"), 2);
        var netSales = Math.Round(Math.Max(0.0, totalSales - totalReturns), 2);

        var hasBonus = await Db.HasColumnAsync("sale_invoice_items", "bonus");
        var hasCostSnap = await Db.HasColumnAsync("sale_invoice_items", "cost_price_snapshot");
        var unitsExpr = hasBonus ? "(COALESCE(sii.quantity, 0) + COALESCE(sii.bonus, 0))" : "COALESCE(sii.quantity, 0)";
        var costExpr = hasCostSnap
            ? "COALESCE(sii.cost_price_snapshot, CASE WHEN p.avg_price IS NULL OR p.avg_price = 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END)"
            : "CASE WHEN p.avg_price IS NULL OR p.avg_price = 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END";

        var cogsSales = await Db.ExecuteScalarAsync<double>($@"
            SELECT COALESCE(SUM({unitsExpr} * ({costExpr})), 0)
            FROM sale_invoice_items sii
            INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
            INNER JOIN products p ON p.id = sii.product_id
            WHERE DATE(si.date) BETWEEN @from AND @to
              AND (si.status IS NULL OR si.status != 'reversed')",
            new { from, to });

        var hasRetCostSnap = await Db.HasColumnAsync("sale_return_items", "cost_price_snapshot");
        var retCostExpr = hasRetCostSnap
            ? "COALESCE(sri.cost_price_snapshot, CASE WHEN p.avg_price IS NULL OR p.avg_price = 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END)"
            : "CASE WHEN p.avg_price IS NULL OR p.avg_price = 0 THEN COALESCE(p.unit_purchase_price, 0) ELSE p.avg_price END";

        var cogsReturns = await Db.ExecuteScalarAsync<double>($@"
            SELECT COALESCE(SUM(COALESCE(sri.unit_return_quantity, 0) * ({retCostExpr})), 0)
            FROM sale_return_items sri
            INNER JOIN sale_returns sr ON sr.id = sri.sale_return_id
            INNER JOIN products p ON p.id = sri.product_id
            WHERE DATE(sr.date) BETWEEN @from AND @to",
            new { from, to });

        var cogs = Math.Round(Math.Max(0.0, cogsSales - cogsReturns), 2);
        var grossProfit = Math.Round(netSales - cogs, 2);
        var grossMarginPct = netSales > 0 ? Math.Round((grossProfit / netSales) * 100, 2) : 0.0;

        double totalExpenses = 0;
        var expensesByCategory = new List<Dictionary<string, object?>>();

        if (await Db.HasTableAsync("expenses") && await Db.HasTableAsync("expense_categories"))
        {
            totalExpenses = Math.Round(await Db.ExecuteScalarAsync<double>(@"
                SELECT COALESCE(SUM(e.amount), 0)
                FROM expenses e
                INNER JOIN expense_categories ec ON ec.id = e.expense_category_id
                WHERE DATE(e.date) BETWEEN @from AND @to
                  AND e.deleted_at IS NULL",
                new { from, to }), 2);

            var catRows = await Db.QueryListAsync(@"
                SELECT ec.id AS category_id,
                       ec.name AS category_name,
                       ec.code AS category_code,
                       COUNT(e.id) AS count,
                       SUM(e.amount) AS total_amount
                FROM expenses e
                INNER JOIN expense_categories ec ON ec.id = e.expense_category_id
                WHERE DATE(e.date) BETWEEN @from AND @to
                  AND e.deleted_at IS NULL
                GROUP BY ec.id, ec.name, ec.code
                ORDER BY total_amount DESC",
                new { from, to });

            foreach (var r in catRows)
            {
                var amt = Math.Round(ToDouble(r, "total_amount"), 2);
                expensesByCategory.Add(new Dictionary<string, object?>
                {
                    ["category_id"] = ToLong(r, "category_id"),
                    ["category_name"] = GetNullableStr(r, "category_name"),
                    ["category_code"] = GetNullableStr(r, "category_code"),
                    ["count"] = (int)ToLong(r, "count"),
                    ["total_amount"] = amt,
                    ["percentage"] = totalExpenses > 0 ? Math.Round((amt / totalExpenses) * 100, 1) : 0.0
                });
            }
        }

        var netProfit = Math.Round(grossProfit - totalExpenses, 2);
        var netMarginPct = netSales > 0 ? Math.Round((netProfit / netSales) * 100, 2) : 0.0;

        return (null, new Dictionary<string, object?>
        {
            ["date_range"] = new Dictionary<string, string>
            {
                ["from"] = from,
                ["to"] = to
            },
            ["kpis"] = new Dictionary<string, object?>
            {
                ["gross_sales"] = grossSales,
                ["total_discounts"] = Math.Round(ToDouble(salesData, "total_discounts"), 2),
                ["total_taxes"] = Math.Round(ToDouble(salesData, "total_taxes"), 2),
                ["invoiced_sales"] = totalSales,
                ["returns_amount"] = totalReturns,
                ["net_sales"] = netSales,
                ["cost_of_goods_sold"] = cogs,
                ["gross_profit"] = grossProfit,
                ["gross_margin_pct"] = grossMarginPct,
                ["total_expenses"] = totalExpenses,
                ["net_profit"] = netProfit,
                ["net_margin_pct"] = netMarginPct,
                ["is_profitable"] = netProfit >= 0
            },
            ["expenses_by_category"] = expensesByCategory
        });
    }

    // =========================================================================
    // 31 & 32. Tax Return (/api/reports/tax-return & /pdf)
    // =========================================================================

    [HttpGet("tax-return")]
    public async Task<IActionResult> TaxReturn()
    {
        var authErr = await AuthorizeReportActionAsync(
            "report.tax-return.view",
            "report.net-profit.view",
            "report.sale-detail.view",
            "report.purchase-detail.view",
            "account.view");
        if (authErr != null) return authErr;

        var data = await BuildTaxReturnDataAsync();
        return Ok(data);
    }

    [HttpGet("tax-return/pdf")]
    public async Task<IActionResult> TaxReturnPdf()
    {
        var authErr = await AuthorizeReportActionAsync(
            "report.tax-return.export",
            "report.tax-return.view",
            "report.net-profit.export",
            "report.net-profit.view",
            "report.sale-detail.view",
            "account.view");
        if (authErr != null) return authErr;

        var data = await BuildTaxReturnDataAsync();
        var period = data.GetValueOrDefault("period") as Dictionary<string, object?>;
        var summary = data.GetValueOrDefault("summary") as Dictionary<string, object?> ?? new();
        var from = GetStr(period, "from");
        var to = GetStr(period, "to");

        var outputReg = data.GetValueOrDefault("output_register") as List<Dictionary<string, object?>> ?? new();
        var inputReg = data.GetValueOrDefault("input_register") as List<Dictionary<string, object?>> ?? new();

        var tableRows = new List<List<string>>();
        foreach (var r in outputReg.Take(250))
        {
            tableRows.Add(["OUTPUT", GetStr(r, "date"), GetStr(r, "posted_number"), GetStr(r, "party_name"), $"{ToDouble(r, "taxable_amount"):N2}", $"{ToDouble(r, "tax_percentage"):N1}%", $"{ToDouble(r, "tax_amount"):N2}"]);
        }
        foreach (var r in inputReg.Take(250))
        {
            tableRows.Add(["INPUT", GetStr(r, "date"), GetStr(r, "posted_number"), GetStr(r, "party_name"), $"{ToDouble(r, "taxable_amount"):N2}", $"{ToDouble(r, "tax_percentage"):N1}%", $"{ToDouble(r, "tax_amount"):N2}"]);
        }

        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "tax-return",
            FromDate = from,
            ToDate = to,
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Net Output Tax", $"Rs. {ToDouble(summary, "net_output_tax"):N2}"),
                new("Net Input Tax", $"Rs. {ToDouble(summary, "net_input_tax"):N2}"),
                new("Challans Paid", $"Rs. {ToDouble(summary, "tax_challans_paid"):N2}"),
                new("Net Payable", $"Rs. {ToDouble(summary, "net_tax_payable"):N2}")
            ],
            Columns =
            [
                new() { Header = "Side", RelativeWidth = 0.9f },
                new() { Header = "Date", RelativeWidth = 1.1f },
                new() { Header = "Doc #", RelativeWidth = 1.3f },
                new() { Header = "Party", RelativeWidth = 2.2f },
                new() { Header = "Taxable Base", RelativeWidth = 1.3f, AlignRight = true },
                new() { Header = "Rate", RelativeWidth = 0.8f, AlignRight = true },
                new() { Header = "Tax Amt", RelativeWidth = 1.2f, AlignRight = true }
            ],
            Rows = tableRows,
            FooterTotals =
            [
                new("Net Output Tax", $"Rs. {ToDouble(summary, "net_output_tax"):N2}"),
                new("Net Input Tax", $"Rs. {ToDouble(summary, "net_input_tax"):N2}"),
                new("Tax Challans Paid", $"Rs. {ToDouble(summary, "tax_challans_paid"):N2}"),
                new("Net Tax Payable", $"Rs. {ToDouble(summary, "net_tax_payable"):N2}", isGrandTotal: true)
            ]
        });
        return DeliverPdf(pdf, $"statutory-tax-return-{from}-to-{to}.pdf");
    }

    private async Task<Dictionary<string, object?>> BuildTaxReturnDataAsync()
    {
        var (from, to) = ResolveDateRange(
            Request.Query["from"].FirstOrDefault(),
            Request.Query["to"].FirstOrDefault(),
            true);

        var taxScope = Request.Query["tax_scope"].FirstOrDefault() ?? "all";
        var accountingService = new AccountingService(Db);
        return await accountingService.GetTaxReportAsync(from, to, new Dictionary<string, string?>
        {
            ["tax_scope"] = taxScope
        });
    }

    // =========================================================================
    // 33 & 34. Narcotics Report (/api/reports/narcotics & /pdf)
    // =========================================================================

    [HttpGet("narcotics")]
    public async Task<IActionResult> NarcoticsReport()
    {
        var authErr = await AuthorizeReportActionAsync(
            "report.sale-detail.view",
            "report.current-stock.view",
            "report.product-comprehensive.view",
            "product.view");
        if (authErr != null) return authErr;

        var data = await BuildNarcoticsReportDataAsync();
        return Ok(data);
    }

    [HttpGet("narcotics/pdf")]
    public async Task<IActionResult> NarcoticsReportPdf()
    {
        var authErr = await AuthorizeReportActionAsync(
            "report.sale-detail.view",
            "report.current-stock.view",
            "report.product-comprehensive.view",
            "product.view");
        if (authErr != null) return authErr;

        var data = await BuildNarcoticsReportDataAsync();
        var period = data.GetValueOrDefault("period") as Dictionary<string, string>;
        var summary = data.GetValueOrDefault("summary") as Dictionary<string, object?> ?? new();
        var chrono = data.GetValueOrDefault("chronological_register") as List<Dictionary<string, object?>> ?? new();
        var from = period?.GetValueOrDefault("from") ?? "";
        var to = period?.GetValueOrDefault("to") ?? "";

        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "narcotics",
            ReportTitle = "Pakistani Statutory Narcotics & Controlled Drugs Register",
            Subtitle = "Drugs Act 1976 / Rule 13 & Schedule G — Mandatory 3-Year Retention",
            FromDate = from,
            ToDate = to,
            Landscape = true,
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Controlled Products", GetStr(summary, "total_narcotic_products")),
                new("Opening Units", GetStr(summary, "opening_units_total")),
                new("Units Purchased", GetStr(summary, "units_purchased")),
                new("Units Dispensed", GetStr(summary, "units_dispensed")),
                new("Closing On Hand", GetStr(summary, "total_units_on_hand")),
                new("Rx Compliance", $"{ToDouble(summary, "compliance_rate"):N1}%")
            ],
            Columns =
            [
                new() { Header = "#", RelativeWidth = 0.5f, AlignCenter = true },
                new() { Header = "Date", RelativeWidth = 1.1f },
                new() { Header = "Drug / Product", RelativeWidth = 2.0f },
                new() { Header = "Event / Voucher", RelativeWidth = 1.6f },
                new() { Header = "Patient / Supplier", RelativeWidth = 1.8f },
                new() { Header = "Doctor / RMP", RelativeWidth = 1.5f },
                new() { Header = "Batch", RelativeWidth = 1.1f },
                new() { Header = "In", RelativeWidth = 0.7f, AlignRight = true },
                new() { Header = "Out", RelativeWidth = 0.7f, AlignRight = true },
                new() { Header = "Balance", RelativeWidth = 0.8f, AlignRight = true }
            ],
            Rows = chrono.Select(r => new List<string>
            {
                GetStr(r, "serial_no"),
                GetStr(r, "date"),
                GetStr(r, "product_name"),
                $"{GetStr(r, "event_label")} ({GetStr(r, "voucher_number")})",
                GetStr(r, "party_name"),
                GetStr(r, "doctor_name"),
                GetStr(r, "batch_number"),
                $"{ToLong(r, "qty_in")}",
                $"{ToLong(r, "qty_out")}",
                $"{ToLong(r, "running_balance")}"
            }).ToList()
        });
        return DeliverPdf(pdf, $"pakistani-statutory-narcotics-register-{from}-to-{to}.pdf");
    }

    private static string FormatNarcoticPackBreakdown(int units, int packSize)
    {
        var ps = Math.Max(1, packSize);
        if (ps <= 1) return $"{units} Units";
        var sign = units < 0 ? "-" : "";
        var abs = Math.Abs(units);
        var packs = abs / ps;
        var rem = abs % ps;
        if (packs > 0 && rem > 0) return $"{sign}{packs} Packs + {rem} Units";
        if (packs > 0) return $"{sign}{packs} Packs";
        return $"{sign}{rem} Units";
    }

    private async Task<Dictionary<string, object?>> BuildNarcoticsReportDataAsync()
    {
        var (fromStr, toStr) = ResolveDateRange(
            Request.Query["from"].FirstOrDefault(),
            Request.Query["to"].FirstOrDefault(),
            true);

        long? filterProductId = long.TryParse(Request.Query["product_id"].FirstOrDefault(), out var pidVal) ? pidVal : null;
        var todayDt = DateTime.Now.Date;

        var setting = await Db.HasTableAsync("settings")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
            : null;

        var storeLicense = new Dictionary<string, object?>
        {
            ["store_name"] = GetNullableStr(setting, "store_name") ?? "Karobar Pharmacy",
            ["license_number"] = GetNullableStr(setting, "license_number"),
            ["address"] = GetNullableStr(setting, "address"),
            ["phone_number"] = GetNullableStr(setting, "phone_number"),
            ["retention_rule"] = "Mandatory 3-Year Statutory Retention (Drugs Act 1976 / Rule 13 & Schedule G)"
        };

        var allNarcoticProducts = await Db.QueryListAsync(@"
            SELECT p.*, b.name AS brand_name, s.name AS supplier_name
            FROM products p
            LEFT JOIN brands b ON b.id = p.brand_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            WHERE LOWER(COALESCE(p.narcotic, '')) = 'yes'
            ORDER BY p.name ASC");

        var narcoticOptions = allNarcoticProducts.Select(p => new Dictionary<string, object?>
        {
            ["id"] = ToLong(p, "id"),
            ["name"] = GetNullableStr(p, "name"),
            ["product_code"] = GetNullableStr(p, "product_code"),
            ["formulation"] = GetNullableStr(p, "formulation"),
            ["brand_name"] = GetNullableStr(p, "brand_name"),
            ["pack_size"] = Math.Max(1, (int)ToLong(p, "pack_size", 1))
        }).ToList();

        var targetProducts = filterProductId.HasValue
            ? allNarcoticProducts.Where(p => ToLong(p, "id") == filterProductId.Value).ToList()
            : allNarcoticProducts;

        var targetIds = targetProducts.Select(p => ToLong(p, "id")).ToList();

        if (targetIds.Count == 0)
        {
            return new Dictionary<string, object?>
            {
                ["period"] = new Dictionary<string, string> { ["from"] = fromStr, ["to"] = toStr },
                ["store_license"] = storeLicense,
                ["summary"] = new Dictionary<string, object?>
                {
                    ["total_narcotic_products"] = allNarcoticProducts.Count,
                    ["active_batches_count"] = 0,
                    ["opening_units_total"] = 0,
                    ["total_units_on_hand"] = 0,
                    ["units_purchased"] = 0,
                    ["purchase_invoices_count"] = 0,
                    ["units_dispensed"] = 0,
                    ["dispensing_invoices_count"] = 0,
                    ["compliant_sales_count"] = 0,
                    ["full_cnsa_compliant_count"] = 0,
                    ["missing_rx_sales_count"] = 0,
                    ["compliance_rate"] = 100.0,
                    ["units_returned_by_customers"] = 0,
                    ["units_returned_to_suppliers"] = 0,
                    ["units_disposed"] = 0,
                    ["disposal_vouchers_count"] = 0,
                    ["witnessed_disposals_count"] = 0
                },
                ["chronological_register"] = new List<Dictionary<string, object?>>(),
                ["product_registers"] = new List<Dictionary<string, object?>>(),
                ["stock_ledger"] = new List<Dictionary<string, object?>>(),
                ["dispensing_register"] = new List<Dictionary<string, object?>>(),
                ["procurement_register"] = new List<Dictionary<string, object?>>(),
                ["destruction_and_returns"] = new List<Dictionary<string, object?>>(),
                ["batch_inventory"] = new List<Dictionary<string, object?>>(),
                ["narcotic_products"] = narcoticOptions
            };
        }

        // 1. Preload all batches for target narcotic products
        var allBatches = await Db.HasTableAsync("batches")
            ? await Db.QueryListAsync(@"
                SELECT b.*, p.name AS product_name, p.product_code, p.formulation, p.pack_size, p.rack AS product_rack, br.name AS brand_name
                FROM batches b
                INNER JOIN products p ON p.id = b.product_id
                LEFT JOIN brands br ON br.id = p.brand_id
                WHERE b.product_id IN @targetIds
                ORDER BY CASE WHEN b.expiry_date IS NULL THEN 1 ELSE 0 END ASC, b.expiry_date ASC",
                new { targetIds })
            : new List<Dictionary<string, object?>>();

        var batchExpiryLookup = new Dictionary<long, Dictionary<string, string>>();
        var productBatchesMap = new Dictionary<long, Dictionary<string, Dictionary<string, object?>>>();
        foreach (var pid in targetIds)
        {
            batchExpiryLookup[pid] = new(StringComparer.OrdinalIgnoreCase);
            productBatchesMap[pid] = new(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var b in allBatches)
        {
            var pid = ToLong(b, "product_id");
            var bNum = (GetNullableStr(b, "batch_number") ?? "").Trim();
            var expStr = FormatDateOnly(GetNullableStr(b, "expiry_date"));
            var bQty = (int)ToLong(b, "quantity");
            if (!string.IsNullOrEmpty(bNum))
            {
                if (!string.IsNullOrEmpty(expStr) && !batchExpiryLookup[pid].ContainsKey(bNum))
                    batchExpiryLookup[pid][bNum] = expStr;
                if (bQty > 0)
                {
                    productBatchesMap[pid][bNum] = new Dictionary<string, object?>
                    {
                        ["batch_number"] = bNum,
                        ["expiry_date"] = expStr,
                        ["quantity"] = bQty
                    };
                }
            }
        }
        foreach (var b in allBatches)
        {
            var pid = ToLong(b, "product_id");
            var bNum = (GetNullableStr(b, "batch_number") ?? "").Trim();
            var expStr = FormatDateOnly(GetNullableStr(b, "expiry_date"));
            if (!string.IsNullOrEmpty(bNum) && productBatchesMap[pid].Count == 0)
            {
                productBatchesMap[pid][bNum] = new Dictionary<string, object?>
                {
                    ["batch_number"] = bNum,
                    ["expiry_date"] = expStr,
                    ["quantity"] = (int)ToLong(b, "quantity")
                };
            }
        }

        void RegisterBatchSeen(long pid, string bNum, ref string? expStr)
        {
            if (string.IsNullOrWhiteSpace(bNum)) return;
            if (string.IsNullOrWhiteSpace(expStr) && batchExpiryLookup[pid].TryGetValue(bNum, out var knownExp))
                expStr = knownExp;
            else if (!string.IsNullOrWhiteSpace(expStr) && !batchExpiryLookup[pid].ContainsKey(bNum))
                batchExpiryLookup[pid][bNum] = expStr;

            if (!productBatchesMap[pid].ContainsKey(bNum))
            {
                productBatchesMap[pid][bNum] = new Dictionary<string, object?>
                {
                    ["batch_number"] = bNum,
                    ["expiry_date"] = expStr,
                    ["quantity"] = 0
                };
            }
            else if (!string.IsNullOrWhiteSpace(expStr) && string.IsNullOrWhiteSpace(GetNullableStr(productBatchesMap[pid][bNum], "expiry_date")))
            {
                productBatchesMap[pid][bNum]["expiry_date"] = expStr;
            }
        }

        // 2. Dispensing Register (SaleInvoiceItem)
        var hasDoctorsTable = await Db.HasTableAsync("doctors") && await Db.HasColumnAsync("sale_invoices", "doctor_id");
        var hasDoctorPmdc = await Db.HasColumnAsync("sale_invoices", "doctor_pmdc");
        var hasDoctorHosp = await Db.HasColumnAsync("sale_invoices", "doctor_hospital");
        var hasPatientCnic = await Db.HasColumnAsync("sale_invoices", "patient_cnic");
        var hasPatientAddr = await Db.HasColumnAsync("sale_invoices", "patient_address");
        var hasRxNo = await Db.HasColumnAsync("sale_invoices", "prescription_no");
        var hasPharmName = await Db.HasColumnAsync("sale_invoices", "pharmacist_name");
        var hasCustCnic = await Db.HasColumnAsync("customers", "cnic");
        var hasCustTax = await Db.HasColumnAsync("customers", "tax_number");

        var saleSql = $@"
            SELECT sii.id, sii.sale_invoice_id, sii.product_id, sii.batch_number, sii.expiry, sii.pack_size,
                   sii.quantity, sii.bonus,
                   p.name AS product_name, p.product_code, p.formulation, p.pack_size AS prod_pack_size, br.name AS brand_name,
                   si.posted_number, si.date, si.sale_type, si.doctor_name, si.patient_name,
                   {(hasDoctorPmdc ? "si.doctor_pmdc" : "NULL")} AS doctor_pmdc,
                   {(hasDoctorHosp ? "si.doctor_hospital" : "NULL")} AS doctor_hospital,
                   {(hasPatientCnic ? "si.patient_cnic" : "NULL")} AS patient_cnic,
                   {(hasPatientAddr ? "si.patient_address" : "NULL")} AS patient_address,
                   {(hasRxNo ? "si.prescription_no" : "NULL")} AS prescription_no,
                   {(hasPharmName ? "si.pharmacist_name" : "NULL")} AS pharmacist_name,
                   c.name AS customer_name, c.phone AS customer_phone, c.address AS customer_address,
                   {(hasCustCnic ? "c.cnic" : "NULL")} AS customer_cnic,
                   {(hasCustTax ? "c.tax_number" : "NULL")} AS customer_tax_number,
                   u.name AS user_name
                   {(hasDoctorsTable ? ", doc.name AS doc_rel_name, doc.pmdc_number AS doc_rel_pmdc, doc.hospital_clinic AS doc_rel_hospital" : ", NULL AS doc_rel_name, NULL AS doc_rel_pmdc, NULL AS doc_rel_hospital")}
            FROM sale_invoice_items sii
            INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
            INNER JOIN products p ON p.id = sii.product_id
            LEFT JOIN brands br ON br.id = p.brand_id
            LEFT JOIN customers c ON c.id = si.customer_id
            LEFT JOIN users u ON u.id = si.user_id
            {(hasDoctorsTable ? "LEFT JOIN doctors doc ON doc.id = si.doctor_id" : "")}
            WHERE sii.product_id IN @targetIds
              AND DATE(si.date) BETWEEN @fromStr AND @toStr
              AND (si.status IS NULL OR si.status != 'reversed')
            ORDER BY p.name ASC, DATE(si.date) ASC, si.id ASC, sii.id ASC";

        var saleItems = await Db.QueryListAsync(saleSql, new { targetIds, fromStr, toStr });
        var dispensingRegister = new List<Dictionary<string, object?>>();

        foreach (var it in saleItems)
        {
            var pid = ToLong(it, "product_id");
            var packSize = Math.Max(1, (int)(ToLong(it, "pack_size") > 0 ? ToLong(it, "pack_size") : ToLong(it, "prod_pack_size", 1)));
            var doctorName = (GetNullableStr(it, "doctor_name") ?? "").Trim();
            if (string.IsNullOrEmpty(doctorName)) doctorName = (GetNullableStr(it, "doc_rel_name") ?? "").Trim();
            var doctorPmdc = (GetNullableStr(it, "doctor_pmdc") ?? "").Trim();
            if (string.IsNullOrEmpty(doctorPmdc)) doctorPmdc = (GetNullableStr(it, "doc_rel_pmdc") ?? "").Trim();
            var doctorHospital = (GetNullableStr(it, "doctor_hospital") ?? "").Trim();
            if (string.IsNullOrEmpty(doctorHospital)) doctorHospital = (GetNullableStr(it, "doc_rel_hospital") ?? "").Trim();

            var patientName = (GetNullableStr(it, "patient_name") ?? "").Trim();
            var patientCnic = (GetNullableStr(it, "patient_cnic") ?? "").Trim();
            var patientAddress = (GetNullableStr(it, "patient_address") ?? "").Trim();
            var rxNo = (GetNullableStr(it, "prescription_no") ?? "").Trim();
            var pharmName = (GetNullableStr(it, "pharmacist_name") ?? "").Trim();
            var custName = (GetNullableStr(it, "customer_name") ?? "").Trim();
            var custCnic = (GetNullableStr(it, "customer_cnic") ?? "").Trim();
            var custPhone = (GetNullableStr(it, "customer_phone") ?? "").Trim();
            var custAddr = (GetNullableStr(it, "customer_address") ?? "").Trim();
            var custLic = (GetNullableStr(it, "customer_tax_number") ?? "").Trim();
            var saleType = (GetNullableStr(it, "sale_type") ?? "retail").Trim().ToLowerInvariant();

            var isWalkIn = string.IsNullOrEmpty(custName) || CustomerLedgerController.IsWalkInName(custName);
            var effPatient = !string.IsNullOrEmpty(patientName) ? patientName : (!isWalkIn ? custName : "");
            var effCnic = !string.IsNullOrEmpty(patientCnic) ? patientCnic : custCnic;
            var effContact = !string.IsNullOrEmpty(effCnic)
                ? (!string.IsNullOrEmpty(custPhone) && effCnic != custPhone ? $"{effCnic} / {custPhone}" : effCnic)
                : custPhone;
            var effAddr = !string.IsNullOrEmpty(patientAddress) ? patientAddress : custAddr;
            var effPharm = !string.IsNullOrEmpty(pharmName) ? pharmName : (GetNullableStr(it, "user_name") ?? "Qualified Pharmacist");

            var isRxCompliant = saleType == "wholesale" ? !isWalkIn : (!string.IsNullOrEmpty(doctorName) && !string.IsNullOrEmpty(effPatient));
            var hasFullCnsa = saleType == "wholesale"
                ? (!isWalkIn && (!string.IsNullOrEmpty(effAddr) || !string.IsNullOrEmpty(custLic)))
                : (isRxCompliant && !string.IsNullOrEmpty(doctorPmdc) && !string.IsNullOrEmpty(effContact));

            var bNum = (GetNullableStr(it, "batch_number") ?? "").Trim();
            var expStr = FormatDateOnly(GetNullableStr(it, "expiry"));
            RegisterBatchSeen(pid, bNum, ref expStr);

            var qty = (int)ToLong(it, "quantity");
            var bonus = (int)ToLong(it, "bonus");
            var totalOut = qty + bonus;
            var postedNo = GetNullableStr(it, "posted_number") ?? "-";

            dispensingRegister.Add(new Dictionary<string, object?>
            {
                ["id"] = ToLong(it, "id"),
                ["sale_invoice_id"] = ToLong(it, "sale_invoice_id"),
                ["posted_number"] = postedNo,
                ["prescription_no"] = !string.IsNullOrEmpty(rxNo) ? rxNo : postedNo,
                ["raw_prescription_no"] = !string.IsNullOrEmpty(rxNo) ? rxNo : null,
                ["date"] = FormatDateOnly(GetNullableStr(it, "date")),
                ["sale_type"] = saleType,
                ["doctor_name"] = !string.IsNullOrEmpty(doctorName) ? doctorName : null,
                ["doctor_pmdc"] = !string.IsNullOrEmpty(doctorPmdc) ? doctorPmdc : null,
                ["doctor_hospital"] = !string.IsNullOrEmpty(doctorHospital) ? doctorHospital : null,
                ["patient_name"] = !string.IsNullOrEmpty(effPatient) ? effPatient : null,
                ["raw_patient_name"] = !string.IsNullOrEmpty(patientName) ? patientName : null,
                ["patient_cnic"] = !string.IsNullOrEmpty(effCnic) ? effCnic : null,
                ["patient_contact"] = !string.IsNullOrEmpty(effContact) ? effContact : null,
                ["patient_address"] = !string.IsNullOrEmpty(effAddr) ? effAddr : null,
                ["raw_patient_address"] = !string.IsNullOrEmpty(patientAddress) ? patientAddress : null,
                ["customer_name"] = !string.IsNullOrEmpty(custName) ? custName : "Walk-in Patient",
                ["customer_phone"] = !string.IsNullOrEmpty(custPhone) ? custPhone : null,
                ["dispensed_by"] = effPharm,
                ["raw_pharmacist_name"] = !string.IsNullOrEmpty(pharmName) ? pharmName : null,
                ["is_rx_compliant"] = isRxCompliant,
                ["has_full_cnsa_particulars"] = hasFullCnsa,
                ["product_id"] = pid,
                ["product_name"] = GetNullableStr(it, "product_name") ?? "-",
                ["product_code"] = GetNullableStr(it, "product_code") ?? "-",
                ["formulation"] = GetNullableStr(it, "formulation"),
                ["brand_name"] = GetNullableStr(it, "brand_name") ?? "-",
                ["batch_number"] = !string.IsNullOrEmpty(bNum) ? bNum : "-",
                ["expiry"] = expStr,
                ["pack_size"] = packSize,
                ["quantity"] = qty,
                ["bonus"] = bonus,
                ["total_units_out"] = totalOut,
                ["pack_breakdown"] = FormatNarcoticPackBreakdown(totalOut, packSize)
            });
        }

        // 3. Procurement Register (PurchaseInvoiceItem)
        var hasSupTax = await Db.HasColumnAsync("suppliers", "tax_number");
        var purchaseItems = await Db.QueryListAsync($@"
            SELECT pii.id, pii.purchase_invoice_id, pii.product_id, pii.batch, pii.expiry, pii.pack_size,
                   pii.pack_quantity, pii.quantity, pii.unit_bonus,
                   p.name AS product_name, p.product_code, p.formulation, p.pack_size AS prod_pack_size, br.name AS brand_name,
                   pi.posted_number, pi.invoice_number, pi.posted_date,
                   s.name AS supplier_name, s.phone AS supplier_phone, s.address AS supplier_address,
                   {(hasSupTax ? "s.tax_number" : "NULL")} AS supplier_tax_number
            FROM purchase_invoice_items pii
            INNER JOIN purchase_invoices pi ON pi.id = pii.purchase_invoice_id
            INNER JOIN products p ON p.id = pii.product_id
            LEFT JOIN brands br ON br.id = p.brand_id
            LEFT JOIN suppliers s ON s.id = pi.supplier_id
            WHERE pii.product_id IN @targetIds
              AND DATE(pi.posted_date) BETWEEN @fromStr AND @toStr
              AND (pi.status IS NULL OR pi.status != 'reversed')
            ORDER BY p.name ASC, DATE(pi.posted_date) ASC, pi.id ASC, pii.id ASC",
            new { targetIds, fromStr, toStr });

        var procurementRegister = new List<Dictionary<string, object?>>();
        foreach (var it in purchaseItems)
        {
            var pid = ToLong(it, "product_id");
            var packSize = Math.Max(1, (int)(ToLong(it, "pack_size") > 0 ? ToLong(it, "pack_size") : ToLong(it, "prod_pack_size", 1)));
            var bNum = (GetNullableStr(it, "batch") ?? "").Trim();
            var expStr = FormatDateOnly(GetNullableStr(it, "expiry"));
            RegisterBatchSeen(pid, bNum, ref expStr);
            var qty = (int)ToLong(it, "quantity");

            procurementRegister.Add(new Dictionary<string, object?>
            {
                ["id"] = ToLong(it, "id"),
                ["purchase_invoice_id"] = ToLong(it, "purchase_invoice_id"),
                ["posted_number"] = GetNullableStr(it, "posted_number") ?? "-",
                ["invoice_number"] = GetNullableStr(it, "invoice_number") is { Length: > 0 } invNo ? invNo : "-",
                ["date"] = FormatDateOnly(GetNullableStr(it, "posted_date")),
                ["supplier_name"] = GetNullableStr(it, "supplier_name") ?? "-",
                ["supplier_phone"] = GetNullableStr(it, "supplier_phone"),
                ["supplier_address"] = GetNullableStr(it, "supplier_address"),
                ["supplier_license_or_tax"] = GetNullableStr(it, "supplier_tax_number"),
                ["product_id"] = pid,
                ["product_name"] = GetNullableStr(it, "product_name") ?? "-",
                ["product_code"] = GetNullableStr(it, "product_code") ?? "-",
                ["formulation"] = GetNullableStr(it, "formulation"),
                ["brand_name"] = GetNullableStr(it, "brand_name") ?? "-",
                ["batch_number"] = !string.IsNullOrEmpty(bNum) ? bNum : "-",
                ["expiry"] = expStr,
                ["pack_size"] = packSize,
                ["pack_quantity"] = (int)ToLong(it, "pack_quantity"),
                ["quantity"] = qty,
                ["unit_bonus"] = (int)ToLong(it, "unit_bonus"),
                ["total_units_in"] = qty,
                ["pack_breakdown"] = FormatNarcoticPackBreakdown(qty, packSize)
            });
        }

        // 4. Disposals, Sale Returns, Purchase Returns & Standalone Stock Adjustments
        var disposalItems = await Db.HasTableAsync("stock_disposal_items")
            ? await Db.QueryListAsync(@"
                SELECT sdi.id, sdi.product_id, sdi.batch_number, sdi.expiry_date, sdi.dispose_qty,
                       p.name AS product_name, p.product_code, p.formulation, p.pack_size,
                       sd.id AS disposal_id, sd.disposal_number, sd.disposal_date, sd.status, sd.cause, sd.disposal_method,
                       sd.reference_no, sd.witness_name, sd.witness_designation,
                       uc.name AS creator_name, ud.name AS disposer_name
                FROM stock_disposal_items sdi
                INNER JOIN stock_disposals sd ON sd.id = sdi.stock_disposal_id
                INNER JOIN products p ON p.id = sdi.product_id
                LEFT JOIN users uc ON uc.id = sd.created_by
                LEFT JOIN users ud ON ud.id = sd.disposed_by
                WHERE sdi.product_id IN @targetIds
                  AND DATE(sd.disposal_date) BETWEEN @fromStr AND @toStr
                  AND sd.status IN ('quarantined', 'disposed')
                ORDER BY sdi.id ASC",
                new { targetIds, fromStr, toStr })
            : new List<Dictionary<string, object?>>();

        var saleReturnItems = await Db.QueryListAsync(@"
            SELECT sri.id, sri.product_id, sri.batch_number, sri.expiry, sri.unit_return_quantity,
                   p.name AS product_name, p.product_code, p.formulation, p.pack_size,
                   sr.id AS return_id, sr.posted_number, sr.date,
                   c.name AS customer_name, c.phone AS customer_phone, c.address AS customer_address,
                   si.posted_number AS orig_inv_no, si.doctor_name, si.patient_name
            FROM sale_return_items sri
            INNER JOIN sale_returns sr ON sr.id = sri.sale_return_id
            INNER JOIN products p ON p.id = sri.product_id
            LEFT JOIN customers c ON c.id = sr.customer_id
            LEFT JOIN sale_invoices si ON si.id = sr.sale_invoice_id
            WHERE sri.product_id IN @targetIds
              AND DATE(sr.date) BETWEEN @fromStr AND @toStr
            ORDER BY sri.id ASC",
            new { targetIds, fromStr, toStr });

        var purchaseReturnItems = await Db.QueryListAsync(@"
            SELECT pri.id, pri.product_id, pri.batch, pri.expiry, pri.return_unit_quantity,
                   p.name AS product_name, p.product_code, p.formulation, p.pack_size,
                   pr.id AS return_id, pr.posted_number, pr.date,
                   s.name AS supplier_name, s.phone AS supplier_phone, s.address AS supplier_address,
                   pi.posted_number AS orig_grn_no
            FROM purchase_return_items pri
            INNER JOIN purchase_returns pr ON pr.id = pri.purchase_return_id
            INNER JOIN products p ON p.id = pri.product_id
            LEFT JOIN suppliers s ON s.id = pr.supplier_id
            LEFT JOIN purchase_invoices pi ON pi.id = pr.purchase_invoice_id
            WHERE pri.product_id IN @targetIds
              AND DATE(pr.date) BETWEEN @fromStr AND @toStr
            ORDER BY pri.id ASC",
            new { targetIds, fromStr, toStr });

        var dispAdjIds = await Db.HasTableAsync("stock_disposals")
            ? (await Db.QueryListAsync("SELECT DISTINCT stock_adjustment_id FROM stock_disposals WHERE stock_adjustment_id IS NOT NULL"))
                .Select(r => ToLong(r, "stock_adjustment_id")).Where(x => x > 0).ToHashSet()
            : new HashSet<long>();

        var adjustmentItems = (await Db.QueryListAsync(@"
            SELECT sai.id, sai.product_id, sai.diff_qty, sai.stock_adjustment_id,
                   p.name AS product_name, p.product_code, p.formulation, p.pack_size,
                   sa.posted_number, sa.posted_date, sa.note
            FROM stock_adjustment_items sai
            INNER JOIN stock_adjustments sa ON sa.id = sai.stock_adjustment_id
            INNER JOIN products p ON p.id = sai.product_id
            WHERE sai.product_id IN @targetIds
              AND DATE(sa.posted_date) BETWEEN @fromStr AND @toStr
            ORDER BY sai.id ASC",
            new { targetIds, fromStr, toStr }))
            .Where(ai => !dispAdjIds.Contains(ToLong(ai, "stock_adjustment_id")))
            .ToList();

        var destructionAndReturns = new List<Dictionary<string, object?>>();
        foreach (var di in disposalItems)
        {
            var pid = ToLong(di, "product_id");
            var packSize = Math.Max(1, (int)ToLong(di, "pack_size", 1));
            var witnessName = (GetNullableStr(di, "witness_name") ?? "").Trim();
            var witnessDesig = (GetNullableStr(di, "witness_designation") ?? "").Trim();
            var qty = (int)ToLong(di, "dispose_qty");
            var bNum = (GetNullableStr(di, "batch_number") ?? "").Trim();
            var expStr = FormatDateOnly(GetNullableStr(di, "expiry_date"));
            RegisterBatchSeen(pid, bNum, ref expStr);
            var status = GetStr(di, "status");
            var cause = GetStr(di, "cause");
            var method = GetStr(di, "disposal_method");
            var causeLabel = StockDisposalService.Causes.TryGetValue(cause, out var cl) ? cl : (string.IsNullOrEmpty(cause) ? "Controlled Destruction" : cause);
            var methodLabel = StockDisposalService.Methods.TryGetValue(method, out var ml) ? ml : method;

            destructionAndReturns.Add(new Dictionary<string, object?>
            {
                ["row_key"] = $"DISP-{ToLong(di, "id")}",
                ["event_type"] = status == "quarantined" ? "QUARANTINE" : "DESTRUCTION",
                ["direction"] = "OUT",
                ["document_id"] = ToLong(di, "disposal_id"),
                ["document_number"] = GetNullableStr(di, "disposal_number") ?? "-",
                ["date"] = FormatDateOnly(GetNullableStr(di, "disposal_date")),
                ["product_id"] = pid,
                ["product_name"] = GetNullableStr(di, "product_name") ?? "-",
                ["product_code"] = GetNullableStr(di, "product_code") ?? "-",
                ["formulation"] = GetNullableStr(di, "formulation") ?? "-",
                ["batch_number"] = !string.IsNullOrEmpty(bNum) ? bNum : "-",
                ["expiry"] = expStr,
                ["quantity"] = qty,
                ["pack_breakdown"] = FormatNarcoticPackBreakdown(qty, packSize),
                ["party_or_authority"] = string.IsNullOrEmpty(methodLabel) ? causeLabel : $"{causeLabel} • {methodLabel}",
                ["witness_name"] = !string.IsNullOrEmpty(witnessName) ? witnessName : null,
                ["witness_designation"] = !string.IsNullOrEmpty(witnessDesig) ? witnessDesig : null,
                ["reference_no"] = GetNullableStr(di, "reference_no"),
                ["status"] = string.IsNullOrEmpty(status) ? "disposed" : status,
                ["is_witness_compliant"] = !string.IsNullOrEmpty(witnessName),
                ["handled_by"] = GetNullableStr(di, "disposer_name") ?? GetNullableStr(di, "creator_name") ?? "Qualified Pharmacist"
            });
        }

        foreach (var sri in saleReturnItems)
        {
            var pid = ToLong(sri, "product_id");
            var packSize = Math.Max(1, (int)ToLong(sri, "pack_size", 1));
            var qty = (int)ToLong(sri, "unit_return_quantity");
            var bNum = (GetNullableStr(sri, "batch_number") ?? "").Trim();
            var expStr = FormatDateOnly(GetNullableStr(sri, "expiry"));
            RegisterBatchSeen(pid, bNum, ref expStr);
            var origInv = GetNullableStr(sri, "orig_inv_no");
            var custName = GetNullableStr(sri, "customer_name") ?? "Patient Return";
            var patName = GetNullableStr(sri, "patient_name");

            destructionAndReturns.Add(new Dictionary<string, object?>
            {
                ["row_key"] = $"SR-{ToLong(sri, "id")}",
                ["event_type"] = "PATIENT_RETURN",
                ["direction"] = "IN",
                ["document_id"] = ToLong(sri, "return_id"),
                ["document_number"] = GetNullableStr(sri, "posted_number") ?? "-",
                ["date"] = FormatDateOnly(GetNullableStr(sri, "date")),
                ["product_id"] = pid,
                ["product_name"] = GetNullableStr(sri, "product_name") ?? "-",
                ["product_code"] = GetNullableStr(sri, "product_code") ?? "-",
                ["formulation"] = GetNullableStr(sri, "formulation") ?? "-",
                ["batch_number"] = !string.IsNullOrEmpty(bNum) ? bNum : "-",
                ["expiry"] = expStr,
                ["quantity"] = qty,
                ["pack_breakdown"] = FormatNarcoticPackBreakdown(qty, packSize),
                ["party_or_authority"] = !string.IsNullOrEmpty(origInv) ? $"{custName} (Against Inv: {origInv})" : custName,
                ["witness_name"] = GetNullableStr(sri, "doctor_name"),
                ["witness_designation"] = !string.IsNullOrEmpty(patName) ? $"Patient: {patName}" : null,
                ["reference_no"] = origInv,
                ["status"] = "completed",
                ["is_witness_compliant"] = true,
                ["handled_by"] = "Pharmacy Counter"
            });
        }

        foreach (var pri in purchaseReturnItems)
        {
            var pid = ToLong(pri, "product_id");
            var packSize = Math.Max(1, (int)ToLong(pri, "pack_size", 1));
            var qty = (int)ToLong(pri, "return_unit_quantity");
            var bNum = (GetNullableStr(pri, "batch") ?? "").Trim();
            var expStr = FormatDateOnly(GetNullableStr(pri, "expiry"));
            RegisterBatchSeen(pid, bNum, ref expStr);
            var origGrn = GetNullableStr(pri, "orig_grn_no");
            var supName = GetNullableStr(pri, "supplier_name") ?? "Authorized Distributor";

            destructionAndReturns.Add(new Dictionary<string, object?>
            {
                ["row_key"] = $"PR-{ToLong(pri, "id")}",
                ["event_type"] = "SUPPLIER_RETURN",
                ["direction"] = "OUT",
                ["document_id"] = ToLong(pri, "return_id"),
                ["document_number"] = GetNullableStr(pri, "posted_number") ?? "-",
                ["date"] = FormatDateOnly(GetNullableStr(pri, "date")),
                ["product_id"] = pid,
                ["product_name"] = GetNullableStr(pri, "product_name") ?? "-",
                ["product_code"] = GetNullableStr(pri, "product_code") ?? "-",
                ["formulation"] = GetNullableStr(pri, "formulation") ?? "-",
                ["batch_number"] = !string.IsNullOrEmpty(bNum) ? bNum : "-",
                ["expiry"] = expStr,
                ["quantity"] = qty,
                ["pack_breakdown"] = FormatNarcoticPackBreakdown(qty, packSize),
                ["party_or_authority"] = !string.IsNullOrEmpty(origGrn) ? $"{supName} (Ref GRN: {origGrn})" : supName,
                ["witness_name"] = null,
                ["witness_designation"] = null,
                ["reference_no"] = origGrn,
                ["status"] = "completed",
                ["is_witness_compliant"] = true,
                ["handled_by"] = "Procurement Pharmacist"
            });
        }

        destructionAndReturns = destructionAndReturns
            .OrderBy(r => GetStr(r, "product_name"), StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => GetStr(r, "date"), StringComparer.Ordinal)
            .ToList();

        // 5. Stock Ledger (Opening -> Inward -> Outward -> Closing)
        async Task<Dictionary<long, int>> QueryQtyMapAsync(string sql, object param)
        {
            var rows = await Db.QueryListAsync(sql, param);
            var map = new Dictionary<long, int>();
            foreach (var r in rows) map[ToLong(r, "product_id")] = (int)ToLong(r, "qty");
            return map;
        }

        var purchInPeriod = await QueryQtyMapAsync(@"SELECT pii.product_id, COALESCE(SUM(pii.quantity), 0) AS qty FROM purchase_invoice_items pii INNER JOIN purchase_invoices pi ON pi.id = pii.purchase_invoice_id WHERE pii.product_id IN @targetIds AND DATE(pi.posted_date) BETWEEN @fromStr AND @toStr AND (pi.status IS NULL OR pi.status != 'reversed') GROUP BY pii.product_id", new { targetIds, fromStr, toStr });
        var purchAfterPeriod = await QueryQtyMapAsync(@"SELECT pii.product_id, COALESCE(SUM(pii.quantity), 0) AS qty FROM purchase_invoice_items pii INNER JOIN purchase_invoices pi ON pi.id = pii.purchase_invoice_id WHERE pii.product_id IN @targetIds AND DATE(pi.posted_date) > @toStr AND (pi.status IS NULL OR pi.status != 'reversed') GROUP BY pii.product_id", new { targetIds, toStr });
        var salesInPeriod = await QueryQtyMapAsync(@"SELECT sii.product_id, COALESCE(SUM(sii.quantity), 0) AS qty FROM sale_invoice_items sii INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id WHERE sii.product_id IN @targetIds AND DATE(si.date) BETWEEN @fromStr AND @toStr AND (si.status IS NULL OR si.status != 'reversed') GROUP BY sii.product_id", new { targetIds, fromStr, toStr });
        var salesAfterPeriod = await QueryQtyMapAsync(@"SELECT sii.product_id, COALESCE(SUM(sii.quantity), 0) AS qty FROM sale_invoice_items sii INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id WHERE sii.product_id IN @targetIds AND DATE(si.date) > @toStr AND (si.status IS NULL OR si.status != 'reversed') GROUP BY sii.product_id", new { targetIds, toStr });
        var saleRetInPeriod = await QueryQtyMapAsync(@"SELECT sri.product_id, COALESCE(SUM(sri.unit_return_quantity), 0) AS qty FROM sale_return_items sri INNER JOIN sale_returns sr ON sr.id = sri.sale_return_id WHERE sri.product_id IN @targetIds AND DATE(sr.date) BETWEEN @fromStr AND @toStr GROUP BY sri.product_id", new { targetIds, fromStr, toStr });
        var saleRetAfterPeriod = await QueryQtyMapAsync(@"SELECT sri.product_id, COALESCE(SUM(sri.unit_return_quantity), 0) AS qty FROM sale_return_items sri INNER JOIN sale_returns sr ON sr.id = sri.sale_return_id WHERE sri.product_id IN @targetIds AND DATE(sr.date) > @toStr GROUP BY sri.product_id", new { targetIds, toStr });
        var purchRetInPeriod = await QueryQtyMapAsync(@"SELECT pri.product_id, COALESCE(SUM(pri.return_unit_quantity), 0) AS qty FROM purchase_return_items pri INNER JOIN purchase_returns pr ON pr.id = pri.purchase_return_id WHERE pri.product_id IN @targetIds AND DATE(pr.date) BETWEEN @fromStr AND @toStr GROUP BY pri.product_id", new { targetIds, fromStr, toStr });
        var purchRetAfterPeriod = await QueryQtyMapAsync(@"SELECT pri.product_id, COALESCE(SUM(pri.return_unit_quantity), 0) AS qty FROM purchase_return_items pri INNER JOIN purchase_returns pr ON pr.id = pri.purchase_return_id WHERE pri.product_id IN @targetIds AND DATE(pr.date) > @toStr GROUP BY pri.product_id", new { targetIds, toStr });
        var dispInPeriod = await Db.HasTableAsync("stock_disposal_items")
            ? await QueryQtyMapAsync(@"SELECT sdi.product_id, COALESCE(SUM(sdi.dispose_qty), 0) AS qty FROM stock_disposal_items sdi INNER JOIN stock_disposals sd ON sd.id = sdi.stock_disposal_id WHERE sdi.product_id IN @targetIds AND DATE(sd.disposal_date) BETWEEN @fromStr AND @toStr AND sd.status IN ('quarantined', 'disposed') GROUP BY sdi.product_id", new { targetIds, fromStr, toStr })
            : new Dictionary<long, int>();
        var dispAfterPeriod = await Db.HasTableAsync("stock_disposal_items")
            ? await QueryQtyMapAsync(@"SELECT sdi.product_id, COALESCE(SUM(sdi.dispose_qty), 0) AS qty FROM stock_disposal_items sdi INNER JOIN stock_disposals sd ON sd.id = sdi.stock_disposal_id WHERE sdi.product_id IN @targetIds AND DATE(sd.disposal_date) > @toStr AND sd.status IN ('quarantined', 'disposed') GROUP BY sdi.product_id", new { targetIds, toStr })
            : new Dictionary<long, int>();

        var adjInPeriod = adjustmentItems.GroupBy(a => ToLong(a, "product_id")).ToDictionary(g => g.Key, g => g.Sum(x => (int)ToLong(x, "diff_qty")));
        var adjAfterRaw = (await Db.QueryListAsync(@"SELECT sai.product_id, sai.diff_qty, sai.stock_adjustment_id FROM stock_adjustment_items sai INNER JOIN stock_adjustments sa ON sa.id = sai.stock_adjustment_id WHERE sai.product_id IN @targetIds AND DATE(sa.posted_date) > @toStr", new { targetIds, toStr }))
            .Where(a => !dispAdjIds.Contains(ToLong(a, "stock_adjustment_id")));
        var adjAfterPeriod = adjAfterRaw.GroupBy(a => ToLong(a, "product_id")).ToDictionary(g => g.Key, g => g.Sum(x => (int)ToLong(x, "diff_qty")));

        var stockLedger = new List<Dictionary<string, object?>>();
        var productMetaMap = new Dictionary<long, Dictionary<string, object?>>();

        foreach (var p in targetProducts)
        {
            var pid = ToLong(p, "id");
            var packSize = Math.Max(1, (int)ToLong(p, "pack_size", 1));
            var currentQty = (int)ToLong(p, "quantity");
            var afterNet = purchAfterPeriod.GetValueOrDefault(pid)
                + saleRetAfterPeriod.GetValueOrDefault(pid)
                - salesAfterPeriod.GetValueOrDefault(pid)
                - purchRetAfterPeriod.GetValueOrDefault(pid)
                - dispAfterPeriod.GetValueOrDefault(pid)
                + adjAfterPeriod.GetValueOrDefault(pid);
            var closingQty = currentQty - afterNet;

            var purchasedQty = purchInPeriod.GetValueOrDefault(pid);
            var saleReturnQty = saleRetInPeriod.GetValueOrDefault(pid);
            var dispensedQty = salesInPeriod.GetValueOrDefault(pid);
            var purchReturnQty = purchRetInPeriod.GetValueOrDefault(pid);
            var disposedQty = dispInPeriod.GetValueOrDefault(pid);
            var adjustedQty = adjInPeriod.GetValueOrDefault(pid);

            var periodNet = purchasedQty + saleReturnQty - dispensedQty - purchReturnQty - disposedQty + adjustedQty;
            var openingQty = closingQty - periodNet;

            var batchesList = productBatchesMap[pid].Values.Select(b =>
            {
                var bNum = GetStr(b, "batch_number");
                var exp = GetNullableStr(b, "expiry_date") ?? (batchExpiryLookup[pid].TryGetValue(bNum, out var kExp) ? kExp : null);
                var bq = (int)ToLong(b, "quantity");
                return new Dictionary<string, object?>
                {
                    ["batch_number"] = bNum,
                    ["expiry_date"] = exp,
                    ["quantity"] = bq,
                    ["pack_breakdown"] = bq > 0 ? FormatNarcoticPackBreakdown(bq, packSize) : null
                };
            }).ToList();

            var batchesSummary = batchesList.Count > 0
                ? string.Join(" | ", batchesList.Select(b => GetStr(b, "batch_number") + (!string.IsNullOrEmpty(GetNullableStr(b, "expiry_date")) ? $" (Exp: {GetStr(b, "expiry_date")})" : "")))
                : "—";

            var row = new Dictionary<string, object?>
            {
                ["product_id"] = pid,
                ["product_code"] = GetNullableStr(p, "product_code") ?? "-",
                ["product_name"] = GetNullableStr(p, "name"),
                ["formulation"] = GetNullableStr(p, "formulation") ?? "-",
                ["brand_name"] = GetNullableStr(p, "brand_name") ?? "-",
                ["supplier_name"] = GetNullableStr(p, "supplier_name") ?? "-",
                ["rack"] = !string.IsNullOrWhiteSpace(GetNullableStr(p, "rack")) ? GetStr(p, "rack") : "Double-Lock Vault",
                ["pack_size"] = packSize,
                ["batches_list"] = batchesList,
                ["batches_summary"] = batchesSummary,
                ["opening_qty"] = openingQty,
                ["opening_pack_breakdown"] = FormatNarcoticPackBreakdown(openingQty, packSize),
                ["purchased_qty"] = purchasedQty,
                ["sale_return_qty"] = saleReturnQty,
                ["dispensed_qty"] = dispensedQty,
                ["purchase_return_qty"] = purchReturnQty,
                ["disposed_qty"] = disposedQty,
                ["adjusted_qty"] = adjustedQty,
                ["closing_qty"] = closingQty,
                ["closing_pack_breakdown"] = FormatNarcoticPackBreakdown(closingQty, packSize),
                ["current_on_hand"] = currentQty
            };
            stockLedger.Add(row);
            productMetaMap[pid] = row;
        }

        // 6. Chronological Running Register & Per-Product Registers
        var rawEventsByProduct = targetIds.ToDictionary(id => id, _ => new List<Dictionary<string, object?>>());

        foreach (var prRow in procurementRegister)
        {
            var pid = ToLong(prRow, "product_id");
            var invNo = GetStr(prRow, "invoice_number");
            var lic = GetNullableStr(prRow, "supplier_license_or_tax");
            var bonus = (int)ToLong(prRow, "unit_bonus");
            rawEventsByProduct[pid].Add(new Dictionary<string, object?>
            {
                ["sort_date"] = GetNullableStr(prRow, "date") ?? fromStr,
                ["sort_order"] = 10,
                ["sort_id"] = ToLong(prRow, "id"),
                ["event_type"] = "PURCHASE_IN",
                ["event_label"] = "INWARD PURCHASE (GRN)",
                ["date"] = GetNullableStr(prRow, "date"),
                ["voucher_number"] = GetStr(prRow, "posted_number"),
                ["reference_no"] = invNo != "-" ? $"Bill: {invNo}" : null,
                ["document_path"] = ToLong(prRow, "purchase_invoice_id") > 0 ? $"/purchase-invoices/{ToLong(prRow, "purchase_invoice_id")}" : null,
                ["sale_invoice_id"] = null,
                ["party_type"] = "SUPPLIER",
                ["party_name"] = GetStr(prRow, "supplier_name"),
                ["party_cnic_or_phone"] = GetNullableStr(prRow, "supplier_phone"),
                ["party_address"] = GetNullableStr(prRow, "supplier_address"),
                ["party_license"] = lic,
                ["doctor_name"] = "Authorized Distributor Supply",
                ["doctor_pmdc"] = !string.IsNullOrEmpty(lic) ? $"Lic/NTN: {lic}" : null,
                ["doctor_hospital"] = null,
                ["batch_number"] = GetStr(prRow, "batch_number"),
                ["expiry"] = GetNullableStr(prRow, "expiry"),
                ["qty_in"] = (int)ToLong(prRow, "total_units_in"),
                ["qty_out"] = 0,
                ["pharmacist_name"] = "Store / Receiving Pharmacist",
                ["is_rx_compliant"] = true,
                ["remarks"] = bonus > 0 ? $"Includes {bonus} bonus units" : "Verified against Supplier Invoice"
            });
        }

        foreach (var sri in saleReturnItems)
        {
            var pid = ToLong(sri, "product_id");
            var dt = FormatDateOnly(GetNullableStr(sri, "date")) ?? fromStr;
            var bNum = (GetNullableStr(sri, "batch_number") ?? "").Trim();
            var exp = FormatDateOnly(GetNullableStr(sri, "expiry"));
            if (string.IsNullOrEmpty(exp) && !string.IsNullOrEmpty(bNum)) batchExpiryLookup[pid].TryGetValue(bNum, out exp);
            var origInv = GetNullableStr(sri, "orig_inv_no");

            rawEventsByProduct[pid].Add(new Dictionary<string, object?>
            {
                ["sort_date"] = dt,
                ["sort_order"] = 20,
                ["sort_id"] = ToLong(sri, "id"),
                ["event_type"] = "PATIENT_RETURN_IN",
                ["event_label"] = "PATIENT RETURN (IN)",
                ["date"] = dt,
                ["voucher_number"] = GetNullableStr(sri, "posted_number") ?? "-",
                ["reference_no"] = !string.IsNullOrEmpty(origInv) ? $"Orig Inv: {origInv}" : null,
                ["document_path"] = $"/sale-returns/{ToLong(sri, "return_id")}",
                ["sale_invoice_id"] = null,
                ["party_type"] = "PATIENT_RETURN",
                ["party_name"] = GetNullableStr(sri, "patient_name") ?? GetNullableStr(sri, "customer_name") ?? "Patient Return",
                ["party_cnic_or_phone"] = GetNullableStr(sri, "customer_phone"),
                ["party_address"] = GetNullableStr(sri, "customer_address"),
                ["party_license"] = null,
                ["doctor_name"] = GetNullableStr(sri, "doctor_name") ?? "Return against Rx",
                ["doctor_pmdc"] = null,
                ["doctor_hospital"] = null,
                ["batch_number"] = !string.IsNullOrEmpty(bNum) ? bNum : "-",
                ["expiry"] = exp,
                ["qty_in"] = (int)ToLong(sri, "unit_return_quantity"),
                ["qty_out"] = 0,
                ["pharmacist_name"] = "Dispensing Pharmacist",
                ["is_rx_compliant"] = true,
                ["remarks"] = "Returned to Narcotics Cabinet"
            });
        }

        foreach (var drRow in dispensingRegister)
        {
            var pid = ToLong(drRow, "product_id");
            var rawRx = GetNullableStr(drRow, "raw_prescription_no");
            var isRx = drRow.GetValueOrDefault("is_rx_compliant") is true;
            var sType = GetStr(drRow, "sale_type");

            rawEventsByProduct[pid].Add(new Dictionary<string, object?>
            {
                ["sort_date"] = GetNullableStr(drRow, "date") ?? fromStr,
                ["sort_order"] = 30,
                ["sort_id"] = ToLong(drRow, "id"),
                ["event_type"] = "DISPENSED_OUT",
                ["event_label"] = sType == "wholesale" ? "WHOLESALE SUPPLY (OUT)" : "PATIENT DISPENSING (OUT)",
                ["date"] = GetNullableStr(drRow, "date"),
                ["voucher_number"] = GetStr(drRow, "posted_number"),
                ["reference_no"] = !string.IsNullOrEmpty(rawRx) ? $"Rx #: {rawRx}" : $"Inv: {GetStr(drRow, "posted_number")}",
                ["raw_prescription_no"] = rawRx,
                ["document_path"] = ToLong(drRow, "sale_invoice_id") > 0 ? $"/sale-invoices/{ToLong(drRow, "sale_invoice_id")}" : null,
                ["sale_invoice_id"] = drRow.GetValueOrDefault("sale_invoice_id"),
                ["sale_type"] = sType,
                ["party_type"] = "PATIENT",
                ["party_name"] = GetNullableStr(drRow, "patient_name") ?? GetStr(drRow, "customer_name"),
                ["raw_patient_name"] = drRow.GetValueOrDefault("raw_patient_name"),
                ["party_cnic_or_phone"] = drRow.GetValueOrDefault("patient_contact"),
                ["raw_patient_cnic"] = drRow.GetValueOrDefault("patient_cnic"),
                ["party_address"] = drRow.GetValueOrDefault("patient_address"),
                ["raw_patient_address"] = drRow.GetValueOrDefault("raw_patient_address"),
                ["party_license"] = null,
                ["doctor_name"] = drRow.GetValueOrDefault("doctor_name"),
                ["doctor_pmdc"] = drRow.GetValueOrDefault("doctor_pmdc"),
                ["doctor_hospital"] = drRow.GetValueOrDefault("doctor_hospital"),
                ["batch_number"] = GetStr(drRow, "batch_number"),
                ["expiry"] = drRow.GetValueOrDefault("expiry"),
                ["qty_in"] = 0,
                ["qty_out"] = (int)ToLong(drRow, "total_units_out"),
                ["pharmacist_name"] = GetStr(drRow, "dispensed_by"),
                ["raw_pharmacist_name"] = drRow.GetValueOrDefault("raw_pharmacist_name"),
                ["is_rx_compliant"] = isRx,
                ["has_full_cnsa_particulars"] = drRow.GetValueOrDefault("has_full_cnsa_particulars") is true,
                ["remarks"] = isRx ? "Dispensed on RMP Prescription" : "MISSING DOCTOR / PATIENT RX DETAILS"
            });
        }

        foreach (var pri in purchaseReturnItems)
        {
            var pid = ToLong(pri, "product_id");
            var dt = FormatDateOnly(GetNullableStr(pri, "date")) ?? fromStr;
            var bNum = (GetNullableStr(pri, "batch") ?? "").Trim();
            var exp = FormatDateOnly(GetNullableStr(pri, "expiry"));
            if (string.IsNullOrEmpty(exp) && !string.IsNullOrEmpty(bNum)) batchExpiryLookup[pid].TryGetValue(bNum, out exp);
            var origGrn = GetNullableStr(pri, "orig_grn_no");

            rawEventsByProduct[pid].Add(new Dictionary<string, object?>
            {
                ["sort_date"] = dt,
                ["sort_order"] = 40,
                ["sort_id"] = ToLong(pri, "id"),
                ["event_type"] = "SUPPLIER_RETURN_OUT",
                ["event_label"] = "RETURN TO SUPPLIER (OUT)",
                ["date"] = dt,
                ["voucher_number"] = GetNullableStr(pri, "posted_number") ?? "-",
                ["reference_no"] = !string.IsNullOrEmpty(origGrn) ? $"GRN: {origGrn}" : null,
                ["document_path"] = $"/purchase-returns/{ToLong(pri, "return_id")}",
                ["sale_invoice_id"] = null,
                ["party_type"] = "SUPPLIER_RETURN",
                ["party_name"] = GetNullableStr(pri, "supplier_name") ?? "Authorized Supplier",
                ["party_cnic_or_phone"] = GetNullableStr(pri, "supplier_phone"),
                ["party_address"] = GetNullableStr(pri, "supplier_address"),
                ["party_license"] = null,
                ["doctor_name"] = "Returned to Distributor",
                ["doctor_pmdc"] = null,
                ["doctor_hospital"] = null,
                ["batch_number"] = !string.IsNullOrEmpty(bNum) ? bNum : "-",
                ["expiry"] = exp,
                ["qty_in"] = 0,
                ["qty_out"] = (int)ToLong(pri, "return_unit_quantity"),
                ["pharmacist_name"] = "Procurement Pharmacist",
                ["is_rx_compliant"] = true,
                ["remarks"] = "Returned to Authorized Distributor"
            });
        }

        foreach (var di in disposalItems)
        {
            var pid = ToLong(di, "product_id");
            var dt = FormatDateOnly(GetNullableStr(di, "disposal_date")) ?? fromStr;
            var witnessName = (GetNullableStr(di, "witness_name") ?? "").Trim();
            var witnessDesig = (GetNullableStr(di, "witness_designation") ?? "").Trim();
            var bNum = (GetNullableStr(di, "batch_number") ?? "").Trim();
            var exp = FormatDateOnly(GetNullableStr(di, "expiry_date"));
            if (string.IsNullOrEmpty(exp) && !string.IsNullOrEmpty(bNum)) batchExpiryLookup[pid].TryGetValue(bNum, out exp);
            var status = GetStr(di, "status");
            var refNo = GetNullableStr(di, "reference_no");

            rawEventsByProduct[pid].Add(new Dictionary<string, object?>
            {
                ["sort_date"] = dt,
                ["sort_order"] = 50,
                ["sort_id"] = ToLong(di, "id"),
                ["event_type"] = status == "quarantined" ? "QUARANTINE_OUT" : "DESTRUCTION_OUT",
                ["event_label"] = status == "quarantined" ? "QUARANTINE LOCK (OUT)" : "WITNESSED DESTRUCTION (OUT)",
                ["date"] = dt,
                ["voucher_number"] = GetNullableStr(di, "disposal_number") ?? "-",
                ["reference_no"] = !string.IsNullOrEmpty(refNo) ? $"Ref: {refNo}" : null,
                ["document_path"] = $"/stock-disposals/{ToLong(di, "disposal_id")}",
                ["sale_invoice_id"] = null,
                ["party_type"] = "DESTRUCTION",
                ["party_name"] = GetStr(di, "cause"),
                ["party_cnic_or_phone"] = null,
                ["party_address"] = null,
                ["party_license"] = null,
                ["doctor_name"] = !string.IsNullOrEmpty(witnessName) ? $"Witness: {witnessName}" : "Witness Pending",
                ["doctor_pmdc"] = !string.IsNullOrEmpty(witnessDesig) ? witnessDesig : null,
                ["doctor_hospital"] = null,
                ["batch_number"] = !string.IsNullOrEmpty(bNum) ? bNum : "-",
                ["expiry"] = exp,
                ["qty_in"] = 0,
                ["qty_out"] = (int)ToLong(di, "dispose_qty"),
                ["pharmacist_name"] = GetNullableStr(di, "disposer_name") ?? GetNullableStr(di, "creator_name") ?? "Qualified Pharmacist",
                ["is_rx_compliant"] = !string.IsNullOrEmpty(witnessName),
                ["remarks"] = !string.IsNullOrEmpty(witnessName) ? $"Witnessed by {witnessName}" : "Witness signature required"
            });
        }

        foreach (var ai in adjustmentItems)
        {
            var pid = ToLong(ai, "product_id");
            var diff = (int)ToLong(ai, "diff_qty");
            if (diff == 0) continue;
            var dt = FormatDateOnly(GetNullableStr(ai, "posted_date")) ?? fromStr;
            var meta = productMetaMap[pid];
            var prodBatches = meta.GetValueOrDefault("batches_list") as List<Dictionary<string, object?>> ?? new();
            var adjBatchNo = prodBatches.Count == 1 ? GetStr(prodBatches[0], "batch_number") : (prodBatches.Count > 0 ? GetStr(meta, "batches_summary") : "-");
            var adjExpiry = prodBatches.Count == 1 ? GetNullableStr(prodBatches[0], "expiry_date") : null;

            rawEventsByProduct[pid].Add(new Dictionary<string, object?>
            {
                ["sort_date"] = dt,
                ["sort_order"] = diff > 0 ? 25 : 55,
                ["sort_id"] = ToLong(ai, "id"),
                ["event_type"] = "ADJUSTMENT",
                ["event_label"] = diff > 0 ? "AUDIT GAIN (IN)" : "AUDIT SHORTAGE (OUT)",
                ["date"] = dt,
                ["voucher_number"] = GetNullableStr(ai, "posted_number") ?? "-",
                ["reference_no"] = null,
                ["document_path"] = $"/stock-adjustments/{ToLong(ai, "stock_adjustment_id")}",
                ["sale_invoice_id"] = null,
                ["party_type"] = "ADJUSTMENT",
                ["party_name"] = "Physical Stock Verification",
                ["party_cnic_or_phone"] = null,
                ["party_address"] = GetNullableStr(ai, "note"),
                ["party_license"] = null,
                ["doctor_name"] = "Internal Pharmacy Audit",
                ["doctor_pmdc"] = null,
                ["doctor_hospital"] = null,
                ["batches_list"] = prodBatches,
                ["batch_number"] = adjBatchNo,
                ["expiry"] = adjExpiry,
                ["qty_in"] = diff > 0 ? diff : 0,
                ["qty_out"] = diff < 0 ? Math.Abs(diff) : 0,
                ["pharmacist_name"] = "Supervising Pharmacist",
                ["is_rx_compliant"] = true,
                ["remarks"] = GetNullableStr(ai, "note") ?? "Physical stock adjustment"
            });
        }

        var chronologicalRegister = new List<Dictionary<string, object?>>();
        var productRegisters = new List<Dictionary<string, object?>>();
        int serialNo = 1;
        int productSectionNo = 1;

        foreach (var prod in targetProducts)
        {
            var pid = ToLong(prod, "id");
            var meta = productMetaMap[pid];
            var packSize = Math.Max(1, (int)ToLong(prod, "pack_size", 1));
            var openingQty = (int)ToLong(meta, "opening_qty");
            var closingQty = (int)ToLong(meta, "closing_qty");
            var prodBatches = meta.GetValueOrDefault("batches_list") as List<Dictionary<string, object?>> ?? new();
            var prodBatchesSummary = GetStr(meta, "batches_summary");

            var events = rawEventsByProduct[pid]
                .OrderBy(e => GetStr(e, "sort_date"), StringComparer.Ordinal)
                .ThenBy(e => ToLong(e, "sort_order"))
                .ThenBy(e => ToLong(e, "sort_id"))
                .ToList();

            var runningBalance = openingQty;
            var productEntries = new List<Dictionary<string, object?>>();
            int productSerialNo = 1;

            var openBatchNo = prodBatches.Count == 1 ? GetStr(prodBatches[0], "batch_number") : (prodBatches.Count > 0 ? prodBatchesSummary : "—");
            var openExpiry = prodBatches.Count == 1 ? GetNullableStr(prodBatches[0], "expiry_date") : null;

            if (openingQty != 0 || events.Count > 0 || filterProductId.HasValue || closingQty != 0)
            {
                var openRow = new Dictionary<string, object?>
                {
                    ["row_key"] = $"OPEN-{pid}",
                    ["serial_no"] = serialNo++,
                    ["product_serial_no"] = productSerialNo++,
                    ["is_opening_row"] = true,
                    ["date"] = fromStr,
                    ["event_type"] = "OPENING_BF",
                    ["event_label"] = "OPENING BALANCE B/F",
                    ["voucher_number"] = "B/F",
                    ["reference_no"] = "Opening Stock in Vault",
                    ["document_path"] = null,
                    ["sale_invoice_id"] = null,
                    ["product_id"] = pid,
                    ["product_name"] = GetNullableStr(prod, "name"),
                    ["product_code"] = GetNullableStr(prod, "product_code") ?? "-",
                    ["formulation"] = GetNullableStr(prod, "formulation") ?? "-",
                    ["brand_name"] = GetNullableStr(prod, "brand_name") ?? "-",
                    ["pack_size"] = packSize,
                    ["party_type"] = "OPENING",
                    ["party_name"] = "Brought Forward Physical Balance",
                    ["party_cnic_or_phone"] = null,
                    ["party_address"] = GetStr(meta, "rack"),
                    ["doctor_name"] = "—",
                    ["doctor_pmdc"] = null,
                    ["doctor_hospital"] = null,
                    ["batches_list"] = prodBatches,
                    ["batch_number"] = openBatchNo,
                    ["expiry"] = openExpiry,
                    ["qty_in"] = 0,
                    ["qty_out"] = 0,
                    ["running_balance"] = runningBalance,
                    ["running_pack_breakdown"] = FormatNarcoticPackBreakdown(runningBalance, packSize),
                    ["pharmacist_name"] = "Verified B/F",
                    ["is_rx_compliant"] = true,
                    ["remarks"] = "Opening Balance Brought Forward"
                };
                chronologicalRegister.Add(openRow);
                productEntries.Add(openRow);
            }

            foreach (var ev in events)
            {
                var qIn = (int)ToLong(ev, "qty_in");
                var qOut = (int)ToLong(ev, "qty_out");
                runningBalance += qIn - qOut;
                var evBatchNo = GetStr(ev, "batch_number");
                var evExpiry = GetNullableStr(ev, "expiry");
                if (evBatchNo != "-" && string.IsNullOrEmpty(evExpiry))
                    batchExpiryLookup[pid].TryGetValue(evBatchNo, out evExpiry);

                var entryRow = new Dictionary<string, object?>(ev)
                {
                    ["row_key"] = $"{GetStr(ev, "event_type")}-{pid}-{ToLong(ev, "sort_id")}",
                    ["serial_no"] = serialNo++,
                    ["product_serial_no"] = productSerialNo++,
                    ["is_opening_row"] = false,
                    ["product_id"] = pid,
                    ["product_name"] = GetNullableStr(prod, "name"),
                    ["product_code"] = GetNullableStr(prod, "product_code") ?? "-",
                    ["formulation"] = GetNullableStr(prod, "formulation") ?? "-",
                    ["brand_name"] = GetNullableStr(prod, "brand_name") ?? "-",
                    ["pack_size"] = packSize,
                    ["batch_number"] = evBatchNo,
                    ["expiry"] = evExpiry,
                    ["qty_in_breakdown"] = qIn > 0 ? FormatNarcoticPackBreakdown(qIn, packSize) : null,
                    ["qty_out_breakdown"] = qOut > 0 ? FormatNarcoticPackBreakdown(qOut, packSize) : null,
                    ["running_balance"] = runningBalance,
                    ["running_pack_breakdown"] = FormatNarcoticPackBreakdown(runningBalance, packSize)
                };
                chronologicalRegister.Add(entryRow);
                productEntries.Add(entryRow);
            }

            var adjQ = (int)ToLong(meta, "adjusted_qty");
            var totalInForProd = (int)ToLong(meta, "purchased_qty") + (int)ToLong(meta, "sale_return_qty") + Math.Max(0, adjQ);
            var totalOutForProd = (int)ToLong(meta, "dispensed_qty") + (int)ToLong(meta, "purchase_return_qty") + (int)ToLong(meta, "disposed_qty") + Math.Abs(Math.Min(0, adjQ));

            productRegisters.Add(new Dictionary<string, object?>
            {
                ["product_section_no"] = productSectionNo++,
                ["product_id"] = pid,
                ["product_code"] = GetNullableStr(prod, "product_code") ?? "-",
                ["product_name"] = GetNullableStr(prod, "name"),
                ["formulation"] = GetNullableStr(prod, "formulation") ?? "-",
                ["brand_name"] = GetNullableStr(prod, "brand_name") ?? "-",
                ["supplier_name"] = GetNullableStr(prod, "supplier_name") ?? "-",
                ["rack"] = GetStr(meta, "rack"),
                ["pack_size"] = packSize,
                ["batches_list"] = prodBatches,
                ["batches_summary"] = prodBatchesSummary,
                ["opening_qty"] = openingQty,
                ["opening_pack_breakdown"] = FormatNarcoticPackBreakdown(openingQty, packSize),
                ["purchased_qty"] = (int)ToLong(meta, "purchased_qty"),
                ["sale_return_qty"] = (int)ToLong(meta, "sale_return_qty"),
                ["dispensed_qty"] = (int)ToLong(meta, "dispensed_qty"),
                ["purchase_return_qty"] = (int)ToLong(meta, "purchase_return_qty"),
                ["disposed_qty"] = (int)ToLong(meta, "disposed_qty"),
                ["adjusted_qty"] = adjQ,
                ["total_in_qty"] = totalInForProd,
                ["total_out_qty"] = totalOutForProd,
                ["closing_qty"] = runningBalance,
                ["closing_pack_breakdown"] = FormatNarcoticPackBreakdown(runningBalance, packSize),
                ["entries_count"] = productEntries.Count,
                ["entries"] = productEntries
            });
        }

        // 7. Active Batch Inventory
        var batchInventory = allBatches
            .Where(b => ToLong(b, "quantity") > 0)
            .Select(b =>
            {
                var packSize = Math.Max(1, (int)ToLong(b, "pack_size", 1));
                var expStr = FormatDateOnly(GetNullableStr(b, "expiry_date"));
                int? daysRem = DateTime.TryParse(expStr, out var expDt) ? (int?)(expDt.Date - todayDt).Days : null;
                var status = daysRem.HasValue
                    ? (daysRem.Value < 0 ? "expired" : daysRem.Value <= 90 ? "critical" : daysRem.Value <= 180 ? "near_expiry" : "healthy")
                    : "healthy";
                var qty = (int)ToLong(b, "quantity");
                var rack = !string.IsNullOrWhiteSpace(GetNullableStr(b, "rack"))
                    ? GetStr(b, "rack")
                    : (!string.IsNullOrWhiteSpace(GetNullableStr(b, "product_rack")) ? GetStr(b, "product_rack") : "Double-Lock Vault");

                return new Dictionary<string, object?>
                {
                    ["id"] = ToLong(b, "id"),
                    ["product_id"] = ToLong(b, "product_id"),
                    ["product_name"] = GetNullableStr(b, "product_name") ?? "-",
                    ["product_code"] = GetNullableStr(b, "product_code") ?? "-",
                    ["formulation"] = GetNullableStr(b, "formulation") ?? "-",
                    ["brand_name"] = GetNullableStr(b, "brand_name") ?? "-",
                    ["pack_size"] = packSize,
                    ["batch_number"] = !string.IsNullOrWhiteSpace(GetNullableStr(b, "batch_number")) ? GetStr(b, "batch_number") : "-",
                    ["expiry_date"] = expStr,
                    ["days_remaining"] = daysRem,
                    ["expiry_status"] = status,
                    ["quantity"] = qty,
                    ["pack_breakdown"] = FormatNarcoticPackBreakdown(qty, packSize),
                    ["rack"] = rack
                };
            })
            .OrderBy(b => GetStr(b, "product_name"), StringComparer.OrdinalIgnoreCase)
            .ThenBy(b => GetStr(b, "expiry_date"), StringComparer.Ordinal)
            .ToList();

        // 8. Summary KPIs
        var totalDispRows = dispensingRegister.Count;
        var compliantRows = dispensingRegister.Count(r => r.GetValueOrDefault("is_rx_compliant") is true);
        var fullCnsaRows = dispensingRegister.Count(r => r.GetValueOrDefault("has_full_cnsa_particulars") is true);
        var missingRxRows = totalDispRows - compliantRows;
        var complianceRate = totalDispRows > 0 ? Math.Round((compliantRows / (double)totalDispRows) * 100.0, 1) : 100.0;
        var uniqueDispVouchers = disposalItems.Select(d => ToLong(d, "disposal_id")).Where(x => x > 0).Distinct().Count();
        var witnessedDispVouchers = disposalItems.Where(d => !string.IsNullOrWhiteSpace(GetNullableStr(d, "witness_name"))).Select(d => ToLong(d, "disposal_id")).Where(x => x > 0).Distinct().Count();

        return new Dictionary<string, object?>
        {
            ["period"] = new Dictionary<string, string> { ["from"] = fromStr, ["to"] = toStr },
            ["store_license"] = storeLicense,
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_narcotic_products"] = targetProducts.Count,
                ["active_batches_count"] = batchInventory.Count,
                ["opening_units_total"] = stockLedger.Sum(s => (int)ToLong(s, "opening_qty")),
                ["total_units_on_hand"] = stockLedger.Sum(s => (int)ToLong(s, "closing_qty")),
                ["units_purchased"] = procurementRegister.Sum(p => (int)ToLong(p, "total_units_in")),
                ["purchase_invoices_count"] = procurementRegister.Select(p => ToLong(p, "purchase_invoice_id")).Where(x => x > 0).Distinct().Count(),
                ["units_dispensed"] = dispensingRegister.Sum(d => (int)ToLong(d, "total_units_out")),
                ["dispensing_invoices_count"] = dispensingRegister.Select(d => ToLong(d, "sale_invoice_id")).Where(x => x > 0).Distinct().Count(),
                ["compliant_sales_count"] = compliantRows,
                ["full_cnsa_compliant_count"] = fullCnsaRows,
                ["missing_rx_sales_count"] = missingRxRows,
                ["compliance_rate"] = complianceRate,
                ["units_returned_by_customers"] = saleReturnItems.Sum(s => (int)ToLong(s, "unit_return_quantity")),
                ["units_returned_to_suppliers"] = purchaseReturnItems.Sum(p => (int)ToLong(p, "return_unit_quantity")),
                ["units_disposed"] = disposalItems.Sum(d => (int)ToLong(d, "dispose_qty")),
                ["disposal_vouchers_count"] = uniqueDispVouchers,
                ["witnessed_disposals_count"] = witnessedDispVouchers
            },
            ["chronological_register"] = chronologicalRegister,
            ["product_registers"] = productRegisters,
            ["stock_ledger"] = stockLedger,
            ["dispensing_register"] = dispensingRegister,
            ["procurement_register"] = procurementRegister,
            ["destruction_and_returns"] = destructionAndReturns,
            ["batch_inventory"] = batchInventory,
            ["narcotic_products"] = narcoticOptions
        };
    }

    // =========================================================================
    // 35. Financial Statements PDF (/api/reports/financial-statements/pdf)
    // =========================================================================

    [HttpGet("financial-statements/pdf")]
    public async Task<IActionResult> FinancialStatementsPdf()
    {
        var authErr = await AuthorizeReportActionAsync(
            "report.financial-statements.view",
            "accounting.view",
            "accounting.manage",
            "report.net-profit.export",
            "report.net-profit.view",
            "account.view");
        if (authErr != null) return authErr;

        var (from, to) = ResolveDateRange(
            Request.Query["from"].FirstOrDefault(),
            Request.Query["to"].FirstOrDefault(),
            true);

        var accounting = new AccountingService(Db);
        var tb = await accounting.GetTrialBalanceAsync(from, to);
        var bs = await accounting.GetBalanceSheetAsync(to);
        var cf = await accounting.GetCashFlowStatementAsync(from, to);

        var tableRows = new List<List<string>>();
        if (tb.GetValueOrDefault("rows") is List<Dictionary<string, object?>> tbRows)
        {
            foreach (var r in tbRows)
            {
                var dr = ToDouble(r, "debit");
                var cr = ToDouble(r, "credit");
                if (Math.Abs(dr) < 0.01 && Math.Abs(cr) < 0.01) continue;
                tableRows.Add(["Trial Balance", GetStr(r, "code"), GetStr(r, "name"), $"{dr:N2}", $"{cr:N2}"]);
            }
        }
        tableRows.Add(["Balance Sheet", "ASSETS", "Total Assets", $"{ToDouble(bs, "total_assets"):N2}", "—"]);
        tableRows.Add(["Balance Sheet", "LIAB", "Total Liabilities", "—", $"{ToDouble(bs, "total_liabilities"):N2}"]);
        tableRows.Add(["Balance Sheet", "EQUITY", "Period Net Income", "—", $"{ToDouble(bs, "period_net_income"):N2}"]);
        tableRows.Add(["Balance Sheet", "EQUITY", "Total Equity", "—", $"{ToDouble(bs, "total_equity"):N2}"]);

        var op = cf.GetValueOrDefault("operating") as Dictionary<string, object?>;
        var inv = cf.GetValueOrDefault("investing") as Dictionary<string, object?>;
        var fin = cf.GetValueOrDefault("financing") as Dictionary<string, object?>;
        tableRows.Add(["Cash Flow", "CF-OP", "Net Operating Cash Flow", $"{(op != null ? ToDouble(op, "net_operating_cash_flow") : 0):N2}", "—"]);
        tableRows.Add(["Cash Flow", "CF-INV", "Net Investing Cash Flow", $"{(inv != null ? ToDouble(inv, "net_investing_cash_flow") : 0):N2}", "—"]);
        tableRows.Add(["Cash Flow", "CF-FIN", "Net Financing Cash Flow", $"{(fin != null ? ToDouble(fin, "net_financing_cash_flow") : 0):N2}", "—"]);
        tableRows.Add(["Cash Flow", "CF-END", "Closing Cash & Bank", $"{ToDouble(cf, "closing_cash_and_bank"):N2}", "—"]);

        var pdf = _pdfGenerator.GenerateAnalyticalReportPdf(new AnalyticalReportPdfRequest
        {
            ReportType = "financial-statements",
            ReportTitle = "Financial Statements (Trial Balance, Balance Sheet & Cash Flow)",
            FromDate = from,
            ToDate = to,
            Setting = await LoadPrintSettingAsync(),
            SummaryCards =
            [
                new("Total Debit", $"Rs. {ToDouble(tb, "total_debit"):N2}"),
                new("Total Credit", $"Rs. {ToDouble(tb, "total_credit"):N2}"),
                new("Total Assets", $"Rs. {ToDouble(bs, "total_assets"):N2}"),
                new("Total Liabilities", $"Rs. {ToDouble(bs, "total_liabilities"):N2}"),
                new("Total Equity", $"Rs. {ToDouble(bs, "total_equity"):N2}"),
                new("Closing Cash", $"Rs. {ToDouble(cf, "closing_cash_and_bank"):N2}")
            ],
            Columns =
            [
                new() { Header = "Statement", RelativeWidth = 1.4f },
                new() { Header = "Code", RelativeWidth = 1.0f },
                new() { Header = "Account / Line Item", RelativeWidth = 3.0f },
                new() { Header = "Debit / Inflow", RelativeWidth = 1.4f, AlignRight = true },
                new() { Header = "Credit / Outflow", RelativeWidth = 1.4f, AlignRight = true }
            ],
            Rows = tableRows,
            FooterTotals =
            [
                new("Trial Balance Balanced", tb.GetValueOrDefault("is_balanced") is true ? "YES" : "NO"),
                new("Balance Sheet Balanced", bs.GetValueOrDefault("is_balanced") is true ? "YES" : "NO"),
                new("Closing Cash & Bank", $"Rs. {ToDouble(cf, "closing_cash_and_bank"):N2}", isGrandTotal: true)
            ]
        });
        return DeliverPdf(pdf, $"financial-statements-{from}-to-{to}.pdf");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static string? FormatDateOnly(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.Length >= 10) return raw.Substring(0, 10);
        return DateTime.TryParse(raw, out var dt) ? dt.ToString("yyyy-MM-dd") : raw;
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

    private static long ToLong(Dictionary<string, object?>? row, string key, long fallback = 0L)
    {
        if (row == null || !row.TryGetValue(key, out var val) || val == null) return fallback;
        if (val is long l) return l;
        if (val is int i) return i;
        return long.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
    }

    private static long? ToNullableLong(Dictionary<string, object?>? row, string key)
    {
        if (row == null || !row.TryGetValue(key, out var val) || val == null) return null;
        if (val is long l) return l;
        if (val is int i) return i;
        return long.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static string GetStr(Dictionary<string, object?>? row, string key)
    {
        if (row == null || !row.TryGetValue(key, out var val) || val == null) return "";
        return val.ToString() ?? "";
    }

    private static string? GetNullableStr(Dictionary<string, object?>? row, string key)
    {
        if (row == null || !row.TryGetValue(key, out var val) || val == null) return null;
        return val.ToString();
    }
}

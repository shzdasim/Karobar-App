using System.Globalization;
using System.Text;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/reports")]
public class ReportsController : ApiControllerBase
{
    public ReportsController(KarobarDb db) : base(db)
    {
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
        var now = DateTime.UtcNow;
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

        var lines = new List<string>
        {
            $"Days: {rows.Count} | Total Sales: {totalSales:N2} | Returns: {totalReturns:N2}",
            $"Net Sales: {netSale:N2} | Cost of Sales: {totalCogs:N2} | Gross Profit: {gp:N2}"
        };
        foreach (var r in rows.Take(25))
        {
            lines.Add($"{GetStr(r, "sale_date")}: Sales={ToDouble(r, "total_sales"):N2}, Return={ToDouble(r, "sale_return"):N2}, COGS={ToDouble(r, "cost_of_sales"):N2}");
        }

        var pdf = BuildReportPdf("Cost of Sale Report", lines);
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
        var invoiceId = Request.Query["invoice_id"].FirstOrDefault();

        var pdf = BuildReportPdf("Cost of Sale Detail Report", new[]
        {
            $"Total Invoices: {summary["total_invoices"]}",
            $"Total Sales: {summary["total_sales"]} | Total Cost: {summary["total_cost"]} | Total Profit: {summary["total_profit"]}"
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

        var pdf = BuildReportPdf("Purchase Detail Report", new[]
        {
            $"Period: {from} to {to}",
            $"Invoices: {rows.Count} | Total Amount: {rows.Sum(r => ToDouble(r, "total_amount")):N2}"
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
                ["item_discount_percentage"] = ToDouble(it, "item_discount_percentage"),
                ["margin"] = ToDouble(it, "margin"),
                ["sub_total"] = ToDouble(it, "sub_total"),
                ["quantity"] = (int)ToLong(it, "quantity")
            }).ToList();

            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = invId,
                ["supplier_id"] = ToNullableLong(inv, "supplier_id"),
                ["supplier_name"] = GetNullableStr(inv, "supplier_name"),
                ["posted_number"] = GetNullableStr(inv, "posted_number"),
                ["invoice_number"] = GetNullableStr(inv, "invoice_number"),
                ["invoice_date"] = FormatDateOnly(GetNullableStr(inv, "posted_date")),
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

        var pdf = BuildReportPdf("Sale Detail Report", new[]
        {
            $"Period: {from} to {to}",
            $"Invoices: {rows.Count} | Total Sales: {rows.Sum(r => ToDouble(r, "total")):N2}"
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

            rows.Add(new Dictionary<string, object?>
            {
                ["id"] = invId,
                ["posted_number"] = GetNullableStr(inv, "posted_number"),
                ["invoice_date"] = FormatDateOnly(GetNullableStr(inv, "date")),
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

        var pdf = BuildReportPdf("Current Stock Report", new[]
        {
            $"Total Items: {summary["total_items"]} | Total Quantity: {summary["total_quantity"]}",
            $"Total Purchase Value: {summary["total_purchase_value"]} | Total Sale Value: {summary["total_sale_value"]}"
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
        var from = Request.Query["from"].FirstOrDefault() ?? "start";
        var to = Request.Query["to"].FirstOrDefault() ?? "today";

        var pdf = BuildReportPdf("Stock Adjustment Report", new[]
        {
            $"Total Adjustments: {summary["total_adjustments"]} | Total Items: {summary["total_items"]}",
            $"Total Worth Adjusted: {summary["total_worth_adjusted"]}"
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

        long.TryParse(Request.Query["product_id"].FirstOrDefault(), out var productId);
        var data = productId > 0 ? await BuildProductComprehensiveDataAsync(productId) : null;
        var prod = data != null && data.TryGetValue("product", out var pObj) ? pObj as Dictionary<string, object?> : null;
        var code = GetNullableStr(prod, "product_code") ?? "product";

        var pdf = BuildReportPdf($"Product Comprehensive Report ({code})", new[]
        {
            $"Product: {GetNullableStr(prod, "name") ?? "-"} ({code})"
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
        var pdf = BuildReportPdf("Near Expiry Product Report", new[]
        {
            $"Total Batches: {summary["total_batches"]} | Total Quantity: {summary["total_quantity"]}"
        });
        return DeliverPdf(pdf, "near-expiry-product-report.pdf");
    }

    private async Task<Dictionary<string, object?>> BuildNearExpiryProductDataAsync()
    {
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var threeMonths = DateTime.UtcNow.AddMonths(3).ToString("yyyy-MM-dd");

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
        var pdf = BuildReportPdf("Purchase Returns Report", new[]
        {
            $"Returns: {summary["return_count"]} | Items: {summary["item_count"]} | Total: {summary["total"]}"
        });
        return DeliverPdf(pdf, "purchase-returns-report.pdf");
    }

    private async Task<(IActionResult? err, Dictionary<string, object?>? data)> BuildPurchaseReturnsDataAsync()
    {
        var now = DateTime.UtcNow;
        var from = Request.Query["from"].FirstOrDefault() ?? new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");
        var to = Request.Query["to"].FirstOrDefault() ?? now.ToString("yyyy-MM-dd");

        if (string.CompareOrdinal(from, to) > 0)
        {
            return (Unprocessable("The from date must be on or before the to date."), null);
        }

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
        var pdf = BuildReportPdf("Sale Returns Report", new[]
        {
            $"Returns: {summary["return_count"]} | Items: {summary["item_count"]} | Total: {summary["total"]}"
        });
        return DeliverPdf(pdf, "sale-returns-report.pdf");
    }

    private async Task<(IActionResult? err, Dictionary<string, object?>? data)> BuildSaleReturnsDataAsync()
    {
        var now = DateTime.UtcNow;
        var from = Request.Query["from"].FirstOrDefault() ?? new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");
        var to = Request.Query["to"].FirstOrDefault() ?? now.ToString("yyyy-MM-dd");

        if (string.CompareOrdinal(from, to) > 0)
        {
            return (Unprocessable("The from date must be on or before the to date."), null);
        }

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
        var pdf = BuildReportPdf("Purchase Summary Report", new[]
        {
            $"Invoices: {summary["invoice_count"]} | Total: {summary["invoice_total"]} | Paid: {summary["total_paid"]}"
        });
        return DeliverPdf(pdf, "purchase-summary-report.pdf");
    }

    private async Task<(IActionResult? err, Dictionary<string, object?>? data)> BuildPurchaseSummaryDataAsync()
    {
        var now = DateTime.UtcNow;
        var from = Request.Query["from"].FirstOrDefault() ?? new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");
        var to = Request.Query["to"].FirstOrDefault() ?? now.ToString("yyyy-MM-dd");

        if (string.CompareOrdinal(from, to) > 0)
        {
            return (Unprocessable("The from date must be on or before the to date."), null);
        }

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
                SELECT pii.*, p.name AS product_name
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
                    ["product_name"] = GetNullableStr(it, "product_name"),
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
        var pdf = BuildReportPdf("Outstanding Aging Report", new[]
        {
            $"As Of: {data["as_of"]} | Aging By: {data["aging_by"]}"
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

        var pdf = BuildReportPdf("Overdue Work Queue Report", new[]
        {
            $"As Of: {data!["as_of"]} | Party Type: {data["party_type"]}",
            $"Total Parties: {data["total_parties"]} | Total Overdue: {data["total_overdue_sum"]}"
        });
        return DeliverPdf(pdf, "overdue-work-queue-report.pdf");
    }

    private async Task<Dictionary<string, object?>> BuildOutstandingAgingDataAsync(string? forceAgingBy = null)
    {
        var rawAsOf = Request.Query["as_of"].FirstOrDefault();
        var asOfDt = !string.IsNullOrWhiteSpace(rawAsOf) && DateTime.TryParse(rawAsOf, out var parsed)
            ? parsed.Date
            : DateTime.UtcNow.Date;
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
        var pdf = BuildReportPdf("Reorder Report", new[]
        {
            $"Products: {summary["product_count"]} | Suggested Quantity: {summary["suggested_quantity"]} | Value: {summary["estimated_reorder_value"]}"
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
        var pdf = BuildReportPdf("Net Profit (Profit & Loss) Report", new[]
        {
            $"Net Sales: {kpis["net_sales"]} | COGS: {kpis["cost_of_goods_sold"]} | Gross Profit: {kpis["gross_profit"]}",
            $"Total Expenses: {kpis["total_expenses"]} | Net Profit: {kpis["net_profit"]}"
        });
        return DeliverPdf(pdf, "net-profit-report.pdf");
    }

    private async Task<(IActionResult? err, Dictionary<string, object?>? data)> BuildNetProfitDataAsync()
    {
        var now = DateTime.UtcNow;
        var from = Request.Query["from"].FirstOrDefault() ?? new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");
        var to = Request.Query["to"].FirstOrDefault() ?? now.ToString("yyyy-MM-dd");

        if (string.CompareOrdinal(from, to) > 0)
        {
            return (Unprocessable("The from date must be on or before the to date."), null);
        }

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
            "report.net-profit.export",
            "report.net-profit.view",
            "report.sale-detail.view",
            "account.view");
        if (authErr != null) return authErr;

        var data = await BuildTaxReturnDataAsync();
        var period = (Dictionary<string, string>)data["period"]!;
        var summary = (Dictionary<string, object?>)data["summary"]!;
        var from = period["from"];
        var to = period["to"];

        var pdf = BuildReportPdf("Statutory Tax Return Report", new[]
        {
            $"Period: {from} to {to}",
            $"Net Output Tax: {summary["net_output_tax"]} | Net Input Tax: {summary["net_input_tax"]} | Net Payable: {summary["net_tax_payable"]}"
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
        var hasCustTax = await Db.HasColumnAsync("customers", "tax_number");
        var hasSupTax = await Db.HasColumnAsync("suppliers", "tax_number");

        var custTaxSel = hasCustTax
            ? "COALESCE(c.tax_number, '') AS party_tax_number, COALESCE(c.tax_status, 'unregistered') AS party_tax_status"
            : "'' AS party_tax_number, 'unregistered' AS party_tax_status";

        var sales = await Db.QueryListAsync($@"
            SELECT si.id, si.posted_number, si.date, si.sale_type, si.invoice_type,
                   si.gross_amount, si.discount_amount, si.tax_percentage, si.tax_amount, si.total, si.customer_id,
                   COALESCE(c.name, 'Walk-in Customer') AS party_name,
                   {custTaxSel}
            FROM sale_invoices si
            LEFT JOIN customers c ON c.id = si.customer_id
            WHERE DATE(si.date) BETWEEN @from AND @to
              AND (si.status IS NULL OR si.status != 'reversed')
            ORDER BY si.date ASC, si.id ASC",
            new { from, to });

        var saleReturns = await Db.QueryListAsync($@"
            SELECT sr.id, sr.posted_number, sr.date, sr.gross_total, sr.discount_amount,
                   sr.tax_percentage, sr.tax_amount, sr.total, sr.customer_id,
                   COALESCE(c.name, 'Walk-in Customer') AS party_name,
                   {custTaxSel}
            FROM sale_returns sr
            LEFT JOIN customers c ON c.id = sr.customer_id
            WHERE DATE(sr.date) BETWEEN @from AND @to
            ORDER BY sr.date ASC, sr.id ASC",
            new { from, to });

        var outputRegister = new List<Dictionary<string, object?>>();
        double taxableSalesBase = 0;
        double exemptSalesBase = 0;
        double outputTaxCollected = 0;
        int taxableSalesCount = 0;
        int exemptSalesCount = 0;

        foreach (var s in sales)
        {
            var taxAmt = Math.Round(ToDouble(s, "tax_amount"), 2);
            var taxPct = Math.Round(ToDouble(s, "tax_percentage"), 2);
            var total = Math.Round(ToDouble(s, "total"), 2);
            var taxableVal = Math.Round(Math.Max(0.0, total - taxAmt), 2);

            if (taxAmt > 0 || taxPct > 0)
            {
                taxableSalesBase = Math.Round(taxableSalesBase + taxableVal, 2);
                outputTaxCollected = Math.Round(outputTaxCollected + taxAmt, 2);
                taxableSalesCount++;
            }
            else
            {
                exemptSalesBase = Math.Round(exemptSalesBase + taxableVal, 2);
                exemptSalesCount++;
            }

            if (taxScope == "taxable_only" && taxAmt <= 0 && taxPct <= 0) continue;

            outputRegister.Add(new Dictionary<string, object?>
            {
                ["id"] = $"SI-{ToLong(s, "id")}",
                ["doc_type"] = "sale_invoice",
                ["doc_type_label"] = "Sale Invoice",
                ["posted_number"] = GetNullableStr(s, "posted_number"),
                ["date"] = FormatDateOnly(GetNullableStr(s, "date")),
                ["sale_type"] = GetNullableStr(s, "sale_type") ?? "retail",
                ["party_id"] = ToNullableLong(s, "customer_id"),
                ["party_name"] = GetStr(s, "party_name"),
                ["party_tax_number"] = string.IsNullOrEmpty(GetStr(s, "party_tax_number")) ? "—" : GetStr(s, "party_tax_number"),
                ["party_tax_status"] = GetStr(s, "party_tax_status"),
                ["taxable_amount"] = taxableVal,
                ["tax_percentage"] = taxPct,
                ["tax_amount"] = taxAmt,
                ["invoice_total"] = total
            });
        }

        double saleReturnsBase = 0;
        double saleReturnsTaxReversed = 0;

        foreach (var sr in saleReturns)
        {
            var taxAmt = Math.Round(ToDouble(sr, "tax_amount"), 2);
            var taxPct = Math.Round(ToDouble(sr, "tax_percentage"), 2);
            var total = Math.Round(ToDouble(sr, "total"), 2);
            var baseVal = Math.Round(Math.Max(0.0, total - taxAmt), 2);

            saleReturnsBase = Math.Round(saleReturnsBase + baseVal, 2);
            saleReturnsTaxReversed = Math.Round(saleReturnsTaxReversed + taxAmt, 2);

            if (taxScope == "taxable_only" && taxAmt <= 0 && taxPct <= 0) continue;

            outputRegister.Add(new Dictionary<string, object?>
            {
                ["id"] = $"SR-{ToLong(sr, "id")}",
                ["doc_type"] = "sale_return",
                ["doc_type_label"] = "Sale Return (Credit Note)",
                ["posted_number"] = GetNullableStr(sr, "posted_number"),
                ["date"] = FormatDateOnly(GetNullableStr(sr, "date")),
                ["sale_type"] = "return",
                ["party_id"] = ToNullableLong(sr, "customer_id"),
                ["party_name"] = GetStr(sr, "party_name"),
                ["party_tax_number"] = string.IsNullOrEmpty(GetStr(sr, "party_tax_number")) ? "—" : GetStr(sr, "party_tax_number"),
                ["party_tax_status"] = GetStr(sr, "party_tax_status"),
                ["taxable_amount"] = -baseVal,
                ["tax_percentage"] = taxPct,
                ["tax_amount"] = -taxAmt,
                ["invoice_total"] = -total
            });
        }

        var supTaxSel = hasSupTax
            ? "COALESCE(s.tax_number, '') AS party_tax_number, COALESCE(s.tax_status, 'registered') AS party_tax_status"
            : "'' AS party_tax_number, 'registered' AS party_tax_status";

        var purchases = await Db.QueryListAsync($@"
            SELECT pi.id, pi.posted_number, pi.invoice_number, pi.posted_date, pi.invoice_amount,
                   pi.discount_amount, pi.tax_percentage, pi.tax_amount, pi.total_amount, pi.supplier_id,
                   COALESCE(s.name, 'Supplier') AS party_name,
                   {supTaxSel}
            FROM purchase_invoices pi
            LEFT JOIN suppliers s ON s.id = pi.supplier_id
            WHERE DATE(pi.posted_date) BETWEEN @from AND @to
              AND (pi.status IS NULL OR pi.status != 'reversed')
            ORDER BY pi.posted_date ASC, pi.id ASC",
            new { from, to });

        var purchaseReturns = await Db.QueryListAsync($@"
            SELECT pr.id, pr.posted_number, pr.date, pr.gross_total, pr.discount_amount,
                   pr.tax_percentage, pr.tax_amount, pr.total, pr.supplier_id,
                   COALESCE(s.name, 'Supplier') AS party_name,
                   {supTaxSel}
            FROM purchase_returns pr
            LEFT JOIN suppliers s ON s.id = pr.supplier_id
            WHERE DATE(pr.date) BETWEEN @from AND @to
            ORDER BY pr.date ASC, pr.id ASC",
            new { from, to });

        var inputRegister = new List<Dictionary<string, object?>>();
        double taxablePurchasesBase = 0;
        double exemptPurchasesBase = 0;
        double inputTaxPaid = 0;
        int taxablePurchasesCount = 0;
        int exemptPurchasesCount = 0;

        foreach (var p in purchases)
        {
            var taxAmt = Math.Round(ToDouble(p, "tax_amount"), 2);
            var taxPct = Math.Round(ToDouble(p, "tax_percentage"), 2);
            var total = Math.Round(ToDouble(p, "total_amount"), 2);
            var taxableVal = Math.Round(Math.Max(0.0, total - taxAmt), 2);

            if (taxAmt > 0 || taxPct > 0)
            {
                taxablePurchasesBase = Math.Round(taxablePurchasesBase + taxableVal, 2);
                inputTaxPaid = Math.Round(inputTaxPaid + taxAmt, 2);
                taxablePurchasesCount++;
            }
            else
            {
                exemptPurchasesBase = Math.Round(exemptPurchasesBase + taxableVal, 2);
                exemptPurchasesCount++;
            }

            if (taxScope == "taxable_only" && taxAmt <= 0 && taxPct <= 0) continue;

            inputRegister.Add(new Dictionary<string, object?>
            {
                ["id"] = $"PI-{ToLong(p, "id")}",
                ["doc_type"] = "purchase_invoice",
                ["doc_type_label"] = "Purchase Invoice",
                ["posted_number"] = GetNullableStr(p, "posted_number"),
                ["supplier_inv_no"] = string.IsNullOrEmpty(GetStr(p, "invoice_number")) ? "—" : GetStr(p, "invoice_number"),
                ["date"] = FormatDateOnly(GetNullableStr(p, "posted_date")),
                ["party_id"] = ToNullableLong(p, "supplier_id"),
                ["party_name"] = GetStr(p, "party_name"),
                ["party_tax_number"] = string.IsNullOrEmpty(GetStr(p, "party_tax_number")) ? "—" : GetStr(p, "party_tax_number"),
                ["party_tax_status"] = GetStr(p, "party_tax_status"),
                ["taxable_amount"] = taxableVal,
                ["tax_percentage"] = taxPct,
                ["tax_amount"] = taxAmt,
                ["invoice_total"] = total
            });
        }

        double purchaseReturnsBase = 0;
        double purchaseReturnsTaxReversed = 0;

        foreach (var pr in purchaseReturns)
        {
            var taxAmt = Math.Round(ToDouble(pr, "tax_amount"), 2);
            var taxPct = Math.Round(ToDouble(pr, "tax_percentage"), 2);
            var total = Math.Round(ToDouble(pr, "total"), 2);
            var baseVal = Math.Round(Math.Max(0.0, total - taxAmt), 2);

            purchaseReturnsBase = Math.Round(purchaseReturnsBase + baseVal, 2);
            purchaseReturnsTaxReversed = Math.Round(purchaseReturnsTaxReversed + taxAmt, 2);

            if (taxScope == "taxable_only" && taxAmt <= 0 && taxPct <= 0) continue;

            inputRegister.Add(new Dictionary<string, object?>
            {
                ["id"] = $"PR-{ToLong(pr, "id")}",
                ["doc_type"] = "purchase_return",
                ["doc_type_label"] = "Purchase Return (Debit Note)",
                ["posted_number"] = GetNullableStr(pr, "posted_number"),
                ["supplier_inv_no"] = "—",
                ["date"] = FormatDateOnly(GetNullableStr(pr, "date")),
                ["party_id"] = ToNullableLong(pr, "supplier_id"),
                ["party_name"] = GetStr(pr, "party_name"),
                ["party_tax_number"] = string.IsNullOrEmpty(GetStr(pr, "party_tax_number")) ? "—" : GetStr(pr, "party_tax_number"),
                ["party_tax_status"] = GetStr(pr, "party_tax_status"),
                ["taxable_amount"] = -baseVal,
                ["tax_percentage"] = taxPct,
                ["tax_amount"] = -taxAmt,
                ["invoice_total"] = -total
            });
        }

        var netOutputTax = Math.Round(outputTaxCollected - saleReturnsTaxReversed, 2);
        var netInputTax = Math.Round(inputTaxPaid - purchaseReturnsTaxReversed, 2);
        var netTaxPayable = Math.Round(netOutputTax - netInputTax, 2);

        var taxRates = await Db.HasTableAsync("tax_rates")
            ? await Db.QueryListAsync("SELECT * FROM tax_rates WHERE is_active = 1 ORDER BY is_default DESC, rate DESC")
            : new List<Dictionary<string, object?>>();

        return new Dictionary<string, object?>
        {
            ["period"] = new Dictionary<string, string> { ["from"] = from, ["to"] = to },
            ["summary"] = new Dictionary<string, object?>
            {
                ["taxable_sales_count"] = taxableSalesCount,
                ["exempt_sales_count"] = exemptSalesCount,
                ["taxable_sales_base"] = taxableSalesBase,
                ["exempt_sales_base"] = exemptSalesBase,
                ["output_tax_collected"] = outputTaxCollected,
                ["sale_returns_base"] = saleReturnsBase,
                ["sale_returns_tax_reversed"] = saleReturnsTaxReversed,
                ["net_output_tax"] = netOutputTax,
                ["taxable_purchases_count"] = taxablePurchasesCount,
                ["exempt_purchases_count"] = exemptPurchasesCount,
                ["taxable_purchases_base"] = taxablePurchasesBase,
                ["exempt_purchases_base"] = exemptPurchasesBase,
                ["input_tax_paid"] = inputTaxPaid,
                ["purchase_returns_base"] = purchaseReturnsBase,
                ["purchase_returns_tax_reversed"] = purchaseReturnsTaxReversed,
                ["net_input_tax"] = netInputTax,
                ["tax_challans_paid"] = 0.0,
                ["net_tax_before_challan"] = netTaxPayable,
                ["net_tax_payable"] = netTaxPayable,
                ["status"] = netTaxPayable > 0 ? "payable" : (netTaxPayable < 0 ? "refundable_credit" : "settled")
            },
            ["output_register"] = outputRegister,
            ["input_register"] = inputRegister,
            ["customer_annexure"] = Array.Empty<object>(),
            ["supplier_annexure"] = Array.Empty<object>(),
            ["rate_breakdown"] = Array.Empty<object>(),
            ["tax_rates"] = taxRates
        };
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
        var period = (Dictionary<string, string>)data["period"]!;
        var from = period["from"];
        var to = period["to"];

        var pdf = BuildReportPdf("Pakistani Statutory Narcotics Register", new[]
        {
            $"Period: {from} to {to}"
        });
        return DeliverPdf(pdf, $"pakistani-statutory-narcotics-register-{from}-to-{to}.pdf");
    }

    private async Task<Dictionary<string, object?>> BuildNarcoticsReportDataAsync()
    {
        var (fromStr, toStr) = ResolveDateRange(
            Request.Query["from"].FirstOrDefault(),
            Request.Query["to"].FirstOrDefault(),
            true);

        long? filterProductId = long.TryParse(Request.Query["product_id"].FirstOrDefault(), out var pidVal) ? pidVal : null;

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
            WHERE p.narcotic = 'yes'
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
                ["chronological_register"] = Array.Empty<object>(),
                ["product_registers"] = Array.Empty<object>(),
                ["stock_ledger"] = Array.Empty<object>(),
                ["dispensing_register"] = Array.Empty<object>(),
                ["procurement_register"] = Array.Empty<object>(),
                ["destruction_and_returns"] = Array.Empty<object>(),
                ["batch_inventory"] = Array.Empty<object>(),
                ["narcotic_products"] = narcoticOptions
            };
        }

        var stockLedger = targetProducts.Select(p =>
        {
            var qty = (int)ToLong(p, "quantity");
            return new Dictionary<string, object?>
            {
                ["product_id"] = ToLong(p, "id"),
                ["product_code"] = GetNullableStr(p, "product_code") ?? "-",
                ["product_name"] = GetNullableStr(p, "name"),
                ["formulation"] = GetNullableStr(p, "formulation") ?? "-",
                ["brand_name"] = GetNullableStr(p, "brand_name") ?? "-",
                ["supplier_name"] = GetNullableStr(p, "supplier_name") ?? "-",
                ["rack"] = GetNullableStr(p, "rack") ?? "Double-Lock Vault",
                ["pack_size"] = Math.Max(1, (int)ToLong(p, "pack_size", 1)),
                ["batches_list"] = Array.Empty<object>(),
                ["batches_summary"] = "—",
                ["opening_qty"] = qty,
                ["opening_pack_breakdown"] = $"{qty} Units",
                ["purchased_qty"] = 0,
                ["sale_return_qty"] = 0,
                ["dispensed_qty"] = 0,
                ["purchase_return_qty"] = 0,
                ["disposed_qty"] = 0,
                ["adjusted_qty"] = 0,
                ["closing_qty"] = qty,
                ["closing_pack_breakdown"] = $"{qty} Units",
                ["current_on_hand"] = qty
            };
        }).ToList();

        var totalOnHand = stockLedger.Sum(s => (int)ToLong(s, "closing_qty"));

        return new Dictionary<string, object?>
        {
            ["period"] = new Dictionary<string, string> { ["from"] = fromStr, ["to"] = toStr },
            ["store_license"] = storeLicense,
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_narcotic_products"] = targetProducts.Count,
                ["active_batches_count"] = 0,
                ["opening_units_total"] = totalOnHand,
                ["total_units_on_hand"] = totalOnHand,
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
            ["chronological_register"] = Array.Empty<object>(),
            ["product_registers"] = Array.Empty<object>(),
            ["stock_ledger"] = stockLedger,
            ["dispensing_register"] = Array.Empty<object>(),
            ["procurement_register"] = Array.Empty<object>(),
            ["destruction_and_returns"] = Array.Empty<object>(),
            ["batch_inventory"] = Array.Empty<object>(),
            ["narcotic_products"] = narcoticOptions
        };
    }

    // =========================================================================
    // 35. Financial Statements PDF (/api/reports/financial-statements/pdf)
    // =========================================================================

    [HttpGet("financial-statements/pdf")]
    public async Task<IActionResult> FinancialStatementsPdf()
    {
        var authErr = await AuthorizeReportActionAsync("report.net-profit.export", "report.net-profit.view", "account.view");
        if (authErr != null) return authErr;

        var (from, to) = ResolveDateRange(
            Request.Query["from"].FirstOrDefault(),
            Request.Query["to"].FirstOrDefault(),
            true);

        var pdf = BuildReportPdf("Financial Statements (Trial Balance & Balance Sheet)", new[]
        {
            $"Period: {from} to {to}"
        });
        return DeliverPdf(pdf, $"financial-statements-{from}-to-{to}.pdf");
    }

    // =========================================================================
    // Minimal PDF Builder & Helpers
    // =========================================================================

    private static byte[] BuildReportPdf(string title, IEnumerable<string> lines)
    {
        static string EscapePdfText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (var ch in s)
            {
                if (ch == '(' || ch == ')' || ch == '\\')
                {
                    sb.Append('\\').Append(ch);
                }
                else if (ch >= 32 && ch <= 126)
                {
                    sb.Append(ch);
                }
                else
                {
                    sb.Append(' ');
                }
            }
            return sb.ToString();
        }

        var contentSb = new StringBuilder();
        contentSb.AppendLine("BT");
        contentSb.AppendLine("/F1 14 Tf");
        contentSb.AppendLine("40 550 Td");
        contentSb.AppendLine($"({EscapePdfText(title)}) Tj");
        contentSb.AppendLine("/F1 10 Tf");

        foreach (var line in lines.Take(30))
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

        offsets.Add(ms.Position);
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets.Add(ms.Position);
        WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets.Add(ms.Position);
        WriteAscii("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>\nendobj\n");

        offsets.Add(ms.Position);
        WriteAscii("4 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>\nendobj\n");

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

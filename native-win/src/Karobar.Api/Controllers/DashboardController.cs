using System.Globalization;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/dashboard")]
[Authorize]
public class DashboardController : ApiControllerBase
{
    public DashboardController(KarobarDb db) : base(db)
    {
    }

    private async Task<IActionResult?> AuthorizeDashboardActionAsync(params string[] permissions)
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

    [HttpGet("summary")]
    public async Task<IActionResult> Summary()
    {
        var authErr = await AuthorizeDashboardActionAsync(
            "sale-invoice.view",
            "purchase-invoice.view",
            "report.sale-detail.view",
            "report.purchase-detail.view",
            "report.cost-of-sale.view"
        );
        if (authErr != null) return authErr;

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var from = Request.Query["date_from"].FirstOrDefault();
        var to = Request.Query["date_to"].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(from)) from = today;
        if (string.IsNullOrWhiteSpace(to)) to = today;

        if (!DateTime.TryParse(from, out var fromDt))
        {
            return Unprocessable("Invalid date_from.", new Dictionary<string, string[]>
            {
                ["date_from"] = new[] { "The date from field must be a valid date." }
            });
        }
        if (!DateTime.TryParse(to, out var toDt))
        {
            return Unprocessable("Invalid date_to.", new Dictionary<string, string[]>
            {
                ["date_to"] = new[] { "The date to field must be a valid date." }
            });
        }
        if (toDt.Date < fromDt.Date)
        {
            return Unprocessable("The date to field must be a date after or equal to date from.", new Dictionary<string, string[]>
            {
                ["date_to"] = new[] { "The date to field must be a date after or equal to date from." }
            });
        }

        var fromStr = fromDt.ToString("yyyy-MM-dd");
        var toStr = toDt.ToString("yyyy-MM-dd");
        var toEnd = $"{toStr} 23:59:59";

        var totalsOnlyRaw = Request.Query["totals_only"].FirstOrDefault() ?? "";
        var totalsOnly = totalsOnlyRaw == "1" || totalsOnlyRaw.Equals("true", StringComparison.OrdinalIgnoreCase);

        if (totalsOnly)
        {
            var salesAgg = await Db.QuerySingleOrDefaultAsync(@"
                SELECT COALESCE(SUM(total), 0) AS total,
                       COALESCE(SUM(CASE WHEN invoice_type = 'credit' THEN total ELSE 0 END), 0) AS credit_total
                FROM sale_invoices
                WHERE date BETWEEN @from AND @toEnd
                  AND (status IS NULL OR status != 'reversed')",
                new { from = fromStr, toEnd });

            var purchasesAgg = await Db.QuerySingleOrDefaultAsync(@"
                SELECT COALESCE(SUM(total_amount), 0) AS total,
                       COALESCE(SUM(CASE WHEN invoice_type = 'credit' THEN total_amount ELSE 0 END), 0) AS credit_total
                FROM purchase_invoices
                WHERE ((posted_date BETWEEN @from AND @toEnd) OR (posted_date IS NULL AND created_at BETWEEN @from AND @toEnd))
                  AND (status IS NULL OR status != 'reversed')",
                new { from = fromStr, toEnd });

            var saleReturnsTotal = await Db.ExecuteScalarAsync<double>(@"
                SELECT COALESCE(SUM(total), 0)
                FROM sale_returns
                WHERE date BETWEEN @from AND @toEnd",
                new { from = fromStr, toEnd });

            var purchaseReturnsTotal = await Db.ExecuteScalarAsync<double>(@"
                SELECT COALESCE(SUM(total), 0)
                FROM purchase_returns
                WHERE date BETWEEN @from AND @toEnd",
                new { from = fromStr, toEnd });

            var salesTotal = ToDouble(salesAgg, "total");
            var salesCredit = ToDouble(salesAgg, "credit_total");
            var purchasesTotal = ToDouble(purchasesAgg, "total");
            var purchasesCredit = ToDouble(purchasesAgg, "credit_total");

            return Ok(new Dictionary<string, object?>
            {
                ["totals"] = new Dictionary<string, object?>
                {
                    ["sales"] = salesTotal,
                    ["purchases"] = purchasesTotal,
                    ["sale_returns"] = saleReturnsTotal,
                    ["purchase_returns"] = purchaseReturnsTotal,
                    ["net_sales"] = salesTotal - saleReturnsTotal,
                    ["sales_credit"] = salesCredit,
                    ["sales_debit"] = salesTotal - salesCredit,
                    ["purchases_credit"] = purchasesCredit,
                    ["purchases_debit"] = purchasesTotal - purchasesCredit
                },
                ["series"] = new Dictionary<string, object?>
                {
                    ["sales"] = Array.Empty<object>(),
                    ["purchases"] = Array.Empty<object>(),
                    ["sale_returns"] = Array.Empty<object>(),
                    ["purchase_returns"] = Array.Empty<object>()
                }
            });
        }

        var salesRows = await Db.QueryListAsync(@"
            SELECT SUBSTR(date, 1, 10) AS date,
                   COALESCE(SUM(total), 0) AS value,
                   COALESCE(SUM(CASE WHEN invoice_type = 'credit' THEN total ELSE 0 END), 0) AS credit_value
            FROM sale_invoices
            WHERE date BETWEEN @from AND @toEnd
              AND (status IS NULL OR status != 'reversed')
            GROUP BY SUBSTR(date, 1, 10)
            ORDER BY date ASC",
            new { from = fromStr, toEnd });

        var purchaseRows = await Db.QueryListAsync(@"
            SELECT SUBSTR(COALESCE(posted_date, created_at), 1, 10) AS date,
                   COALESCE(SUM(total_amount), 0) AS value,
                   COALESCE(SUM(CASE WHEN invoice_type = 'credit' THEN total_amount ELSE 0 END), 0) AS credit_value
            FROM purchase_invoices
            WHERE ((posted_date BETWEEN @from AND @toEnd) OR (posted_date IS NULL AND created_at BETWEEN @from AND @toEnd))
              AND (status IS NULL OR status != 'reversed')
            GROUP BY SUBSTR(COALESCE(posted_date, created_at), 1, 10)
            ORDER BY date ASC",
            new { from = fromStr, toEnd });

        var saleReturnRows = await Db.QueryListAsync(@"
            SELECT SUBSTR(date, 1, 10) AS date,
                   COALESCE(SUM(total), 0) AS value
            FROM sale_returns
            WHERE date BETWEEN @from AND @toEnd
            GROUP BY SUBSTR(date, 1, 10)
            ORDER BY date ASC",
            new { from = fromStr, toEnd });

        var purchaseReturnRows = await Db.QueryListAsync(@"
            SELECT SUBSTR(date, 1, 10) AS date,
                   COALESCE(SUM(total), 0) AS value
            FROM purchase_returns
            WHERE date BETWEEN @from AND @toEnd
            GROUP BY SUBSTR(date, 1, 10)
            ORDER BY date ASC",
            new { from = fromStr, toEnd });

        var sTotal = salesRows.Sum(r => ToDouble(r, "value"));
        var sCredit = salesRows.Sum(r => ToDouble(r, "credit_value"));
        var pTotal = purchaseRows.Sum(r => ToDouble(r, "value"));
        var pCredit = purchaseRows.Sum(r => ToDouble(r, "credit_value"));
        var srTotal = saleReturnRows.Sum(r => ToDouble(r, "value"));
        var prTotal = purchaseReturnRows.Sum(r => ToDouble(r, "value"));

        return Ok(new Dictionary<string, object?>
        {
            ["totals"] = new Dictionary<string, object?>
            {
                ["sales"] = sTotal,
                ["purchases"] = pTotal,
                ["sale_returns"] = srTotal,
                ["purchase_returns"] = prTotal,
                ["net_sales"] = sTotal - srTotal,
                ["sales_credit"] = sCredit,
                ["sales_debit"] = sTotal - sCredit,
                ["purchases_credit"] = pCredit,
                ["purchases_debit"] = pTotal - pCredit
            },
            ["series"] = new Dictionary<string, object?>
            {
                ["sales"] = salesRows.Select(r => new { date = GetStr(r, "date"), value = ToDouble(r, "value") }).ToList(),
                ["purchases"] = purchaseRows.Select(r => new { date = GetStr(r, "date"), value = ToDouble(r, "value") }).ToList(),
                ["sale_returns"] = saleReturnRows.Select(r => new { date = GetStr(r, "date"), value = ToDouble(r, "value") }).ToList(),
                ["purchase_returns"] = purchaseReturnRows.Select(r => new { date = GetStr(r, "date"), value = ToDouble(r, "value") }).ToList()
            }
        });
    }

    [HttpGet("near-expiry")]
    public async Task<IActionResult> NearExpiry()
    {
        var authErr = await AuthorizeDashboardActionAsync("product.view", "report.near-expiry-product.view");
        if (authErr != null) return authErr;

        int months = 3;
        if (int.TryParse(Request.Query["months"].FirstOrDefault(), out var m) && m >= 1 && m <= 36)
        {
            months = m;
        }

        var supplierId = Request.Query["supplier_id"].FirstOrDefault();
        var brandId = Request.Query["brand_id"].FirstOrDefault();

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var to = DateTime.UtcNow.AddMonths(months).ToString("yyyy-MM-dd");
        var toNextDay = DateTime.UtcNow.AddMonths(months).AddDays(1).ToString("yyyy-MM-dd");

        var sql = @"
            SELECT b.id AS batch_id,
                   b.batch_number,
                   b.expiry_date,
                   b.quantity,
                   p.id AS product_id,
                   p.product_code,
                   p.name AS product_name,
                   p.supplier_id,
                   p.brand_id,
                   COALESCE(s.name, '') AS supplier_name,
                   COALESCE(br.name, '') AS brand_name
            FROM batches b
            INNER JOIN products p ON p.id = b.product_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            LEFT JOIN brands br ON br.id = p.brand_id
            WHERE b.expiry_date IS NOT NULL
              AND b.quantity > 0
              AND b.expiry_date >= @today
              AND b.expiry_date < @toNextDay";

        var dyn = new DynamicParameters();
        dyn.Add("today", today);
        dyn.Add("toNextDay", toNextDay);

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

        sql += " ORDER BY b.expiry_date ASC LIMIT 1000";

        var rows = await Db.QueryListAsync(sql, dyn);

        return Ok(new Dictionary<string, object?>
        {
            ["months"] = months,
            ["from"] = today,
            ["to"] = to,
            ["rows"] = rows,
            ["data"] = rows
        });
    }

    [HttpGet("near-expiry/filters")]
    public async Task<IActionResult> NearExpiryFilters()
    {
        var authErr = await AuthorizeDashboardActionAsync("product.view", "report.near-expiry-product.view");
        if (authErr != null) return authErr;

        var suppliers = await Db.QueryListAsync("SELECT id, name FROM suppliers ORDER BY name ASC");
        var brands = await Db.QueryListAsync("SELECT id, name FROM brands ORDER BY name ASC");

        return Ok(new Dictionary<string, object?>
        {
            ["suppliers"] = suppliers,
            ["brands"] = brands
        });
    }

    [HttpGet("invoice-counts")]
    public async Task<IActionResult> InvoiceCounts()
    {
        var authErr = await AuthorizeDashboardActionAsync(
            "sale-invoice.view",
            "purchase-invoice.view",
            "report.sale-detail.view",
            "report.purchase-detail.view"
        );
        if (authErr != null) return authErr;

        var counts = await Db.QuerySingleOrDefaultAsync(@"
            SELECT
                (SELECT COUNT(*) FROM sale_invoices WHERE status IS NULL OR status != 'reversed') AS sale_invoices,
                (SELECT COUNT(*) FROM purchase_invoices WHERE status IS NULL OR status != 'reversed') AS purchase_invoices");

        var saleCount = (int)ToLong(counts, "sale_invoices");
        var purchaseCount = (int)ToLong(counts, "purchase_invoices");

        return Ok(new Dictionary<string, object?>
        {
            ["sale_invoices"] = saleCount,
            ["purchase_invoices"] = purchaseCount,
            ["total"] = saleCount + purchaseCount
        });
    }

    [HttpGet("sales-by-brands")]
    public async Task<IActionResult> SalesByBrands()
    {
        var authErr = await AuthorizeDashboardActionAsync(
            "sale-invoice.view",
            "report.sale-detail.view",
            "report.cost-of-sale.view"
        );
        if (authErr != null) return authErr;

        var now = DateTime.UtcNow;
        var today = now.ToString("yyyy-MM-dd");
        var startOfMonth = new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");

        var from = Request.Query["date_from"].FirstOrDefault();
        var to = Request.Query["date_to"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(from)) from = startOfMonth;
        if (string.IsNullOrWhiteSpace(to)) to = today;

        var data = await Db.QueryListAsync(@"
            SELECT COALESCE(br.name, 'No Brand') AS brand,
                   COALESCE(SUM(sii.sub_total), 0) AS revenue
            FROM sale_invoice_items sii
            INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
            INNER JOIN products p ON p.id = sii.product_id
            LEFT JOIN brands br ON br.id = p.brand_id
            WHERE si.date BETWEEN @from AND @to
              AND (si.status IS NULL OR si.status != 'reversed')
            GROUP BY br.name
            ORDER BY revenue DESC
            LIMIT 5",
            new { from, to });

        return Ok(new Dictionary<string, object?>
        {
            ["data"] = data
        });
    }

    [HttpGet("sales-by-category")]
    public async Task<IActionResult> SalesByCategory()
    {
        var authErr = await AuthorizeDashboardActionAsync(
            "sale-invoice.view",
            "report.sale-detail.view",
            "report.cost-of-sale.view"
        );
        if (authErr != null) return authErr;

        var now = DateTime.UtcNow;
        var today = now.ToString("yyyy-MM-dd");
        var startOfMonth = new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");

        var from = Request.Query["date_from"].FirstOrDefault();
        var to = Request.Query["date_to"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(from)) from = startOfMonth;
        if (string.IsNullOrWhiteSpace(to)) to = today;

        var data = await Db.QueryListAsync(@"
            SELECT COALESCE(c.name, 'Uncategorized') AS category,
                   COALESCE(c.name, 'Uncategorized') AS name,
                   COALESCE(SUM(COALESCE(sii.sub_total, sii.quantity * sii.price, 0)), 0) AS value,
                   COALESCE(SUM(COALESCE(sii.sub_total, sii.quantity * sii.price, 0)), 0) AS revenue
            FROM sale_invoice_items sii
            INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
            INNER JOIN products p ON p.id = sii.product_id
            LEFT JOIN categories c ON c.id = p.category_id
            WHERE si.date BETWEEN @from AND @to
              AND (si.status IS NULL OR si.status != 'reversed')
            GROUP BY c.name
            ORDER BY value DESC
            LIMIT 10",
            new { from, to });

        return Ok(new Dictionary<string, object?>
        {
            ["data"] = data
        });
    }

    [HttpGet("top-products")]
    public async Task<IActionResult> TopProducts()
    {
        var authErr = await AuthorizeDashboardActionAsync(
            "sale-invoice.view",
            "report.sale-detail.view",
            "report.cost-of-sale.view"
        );
        if (authErr != null) return authErr;

        var now = DateTime.UtcNow;
        var today = now.ToString("yyyy-MM-dd");
        var startOfMonth = new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");

        var from = Request.Query["date_from"].FirstOrDefault();
        var to = Request.Query["date_to"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(from)) from = startOfMonth;
        if (string.IsNullOrWhiteSpace(to)) to = today;

        int limit = 10;
        if (int.TryParse(Request.Query["limit"].FirstOrDefault(), out var l) && l > 0 && l <= 100)
        {
            limit = l;
        }

        var data = await Db.QueryListAsync(@"
            SELECT p.id,
                   p.name AS product_name,
                   COALESCE(SUM(sii.quantity), 0) AS total_sold,
                   COALESCE(SUM(sii.sub_total), 0) AS revenue
            FROM sale_invoice_items sii
            INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
            INNER JOIN products p ON p.id = sii.product_id
            WHERE si.date BETWEEN @from AND @to
              AND (si.status IS NULL OR si.status != 'reversed')
            GROUP BY p.id, p.name
            ORDER BY total_sold DESC
            LIMIT @limit",
            new { from, to, limit });

        return Ok(new Dictionary<string, object?>
        {
            ["data"] = data
        });
    }

    [HttpGet("kpi-metrics")]
    public async Task<IActionResult> KpiMetrics()
    {
        var authErr = await AuthorizeDashboardActionAsync(
            "product.view",
            "sale-invoice.view",
            "purchase-invoice.view",
            "customer.view",
            "supplier.view",
            "user.view",
            "report.current-stock.view"
        );
        if (authErr != null) return authErr;

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var from = Request.Query["date_from"].FirstOrDefault();
        var to = Request.Query["date_to"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(from)) from = today;
        if (string.IsNullOrWhiteSpace(to)) to = today;

        if (!DateTime.TryParse(from, out var fromDt))
        {
            return Unprocessable("Invalid date_from.", new Dictionary<string, string[]>
            {
                ["date_from"] = new[] { "The date from field must be a valid date." }
            });
        }
        if (!DateTime.TryParse(to, out var toDt) || toDt.Date < fromDt.Date)
        {
            return Unprocessable("Invalid date_to.", new Dictionary<string, string[]>
            {
                ["date_to"] = new[] { "The date to field must be a date after or equal to date from." }
            });
        }

        var canViewValuation = await IsAdminAsync() || await CanAsync("product.view", "report.current-stock.view");

        var productSql = canViewValuation
            ? @"SELECT COUNT(*) AS active_products,
                       COALESCE(SUM(quantity), 0) AS stock_units,
                       COALESCE(SUM(COALESCE(quantity, 0) * CASE
                           WHEN COALESCE(avg_price, 0) > 0 THEN avg_price
                           WHEN COALESCE(unit_purchase_price, 0) > 0 THEN unit_purchase_price
                           WHEN COALESCE(pack_purchase_price, 0) > 0 AND COALESCE(pack_size, 0) > 0 THEN (pack_purchase_price * 1.0 / pack_size)
                           ELSE 0 END), 0) AS stock_value
                FROM products"
            : @"SELECT COUNT(*) AS active_products,
                       COALESCE(SUM(quantity), 0) AS stock_units
                FROM products";

        var productStats = await Db.QuerySingleOrDefaultAsync(productSql);
        var activeProducts = (int)ToLong(productStats, "active_products");
        var stockUnits = ToDouble(productStats, "stock_units");
        double? stockValue = canViewValuation ? ToDouble(productStats, "stock_value") : null;

        var hasUserDemands = await Db.HasTableAsync("user_demands");
        var pendingDemandsSubquery = hasUserDemands
            ? "(SELECT COUNT(*) FROM user_demands WHERE status = 'pending')"
            : "0";
        var todayNextDay = DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd");
        var threeMonthsNextDay = DateTime.UtcNow.AddMonths(3).AddDays(1).ToString("yyyy-MM-dd");
        var counts = await Db.QuerySingleOrDefaultAsync($@"
            SELECT
                (SELECT COUNT(*) FROM suppliers) AS suppliers,
                (SELECT COUNT(*) FROM customers) AS customers,
                (SELECT COUNT(*) FROM brands) AS brands,
                (SELECT COUNT(*) FROM categories) AS categories,
                (SELECT COUNT(*) FROM users) AS users,
                (SELECT COUNT(*) FROM stock_adjustments) AS stock_adjustments,
                {pendingDemandsSubquery} AS pending_demands,
                (SELECT COUNT(*) FROM batches b WHERE b.expiry_date IS NOT NULL AND b.quantity > 0 AND b.expiry_date >= @todayNextDay AND b.expiry_date < @threeMonthsNextDay) AS near_expiry",
            new { todayNextDay, threeMonthsNextDay });

        var lowStockCount = await Db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(*)
            FROM products p
            INNER JOIN (SELECT DISTINCT product_id FROM purchase_invoice_items WHERE product_id IS NOT NULL) pii
                ON pii.product_id = p.id
            WHERE p.quantity IS NOT NULL
              AND p.quantity < p.pack_size");

        double? expectedOrdersAmount = null;
        if (await IsAdminAsync() || await CanAsync("purchase-order.view"))
        {
            if (await Db.HasTableAsync("purchase_orders") && await Db.HasTableAsync("purchase_order_items"))
            {
                var fromStr = fromDt.ToString("yyyy-MM-dd");
                var toStr = toDt.ToString("yyyy-MM-dd");
                var hasExpectedCol = await Db.HasColumnAsync("purchase_orders", "expected_received_date");
                var hasDateCol = await Db.HasColumnAsync("purchase_orders", "date");
                var dateExpr = hasExpectedCol && hasDateCol
                    ? "DATE(COALESCE(po.expected_received_date, po.date))"
                    : hasExpectedCol
                        ? "DATE(po.expected_received_date)"
                        : "DATE(po.created_at)";
                var hasPiPoCol = await Db.HasTableAsync("purchase_invoices") && await Db.HasColumnAsync("purchase_invoices", "purchase_order_id");
                var notReceivedClause = hasPiPoCol
                    ? "AND NOT EXISTS (SELECT 1 FROM purchase_invoices pi WHERE pi.purchase_order_id = po.id)"
                    : "";

                expectedOrdersAmount = await Db.ExecuteScalarAsync<double>($@"
                    SELECT COALESCE(SUM(lines.ordered_quantity * lines.unit_purchase_price * (1.0 - COALESCE(lines.item_discount_percentage, 0) / 100.0)), 0)
                    FROM purchase_orders po
                    INNER JOIN purchase_order_items lines ON lines.purchase_order_id = po.id
                    WHERE po.status = 'ordered'
                      {notReceivedClause}
                      AND {dateExpr} BETWEEN @fromStr AND @toStr",
                    new { fromStr, toStr });
            }
            else
            {
                expectedOrdersAmount = 0.0;
            }
        }

        return Ok(new Dictionary<string, object?>
        {
            ["active_products"] = activeProducts,
            ["customers"] = (int)ToLong(counts, "customers"),
            ["suppliers"] = (int)ToLong(counts, "suppliers"),
            ["brands"] = (int)ToLong(counts, "brands"),
            ["categories"] = (int)ToLong(counts, "categories"),
            ["users"] = (int)ToLong(counts, "users"),
            ["near_expiry"] = (int)ToLong(counts, "near_expiry"),
            ["low_stock"] = lowStockCount,
            ["stock_adjustments"] = (int)ToLong(counts, "stock_adjustments"),
            ["pending_demands"] = (int)ToLong(counts, "pending_demands"),
            ["stock_units"] = stockUnits,
            ["stock_value"] = stockValue,
            ["expected_orders_amount"] = expectedOrdersAmount
        });
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

    private static long ToLong(Dictionary<string, object?>? row, string key)
    {
        if (row == null || !row.TryGetValue(key, out var val) || val == null) return 0L;
        if (val is long l) return l;
        if (val is int i) return i;
        return long.TryParse(val.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L;
    }

    private static string GetStr(Dictionary<string, object?>? row, string key)
    {
        if (row == null || !row.TryGetValue(key, out var val) || val == null) return "";
        return val.ToString() ?? "";
    }
}

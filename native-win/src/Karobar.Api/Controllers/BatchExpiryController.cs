using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

public class BatchExpiryController : ApiControllerBase
{
    public BatchExpiryController(KarobarDb db) : base(db)
    {
    }

    public static (int MonthsRemaining, string Label) ComputeMonthsRemainingMeta(int daysRemaining)
    {
        if (daysRemaining < 0) return (0, "Expired");
        if (daysRemaining == 0) return (1, "Expires Today");

        var months = Math.Max(1, (int)Math.Ceiling(daysRemaining / 30.4375));
        var label = months == 1 ? "1 Month Left" : $"{months} Months Left";
        return (months, label);
    }

    private async Task<(int WarningDays, int CriticalDays, int DailyFlashLimit)> GetGlobalExpirySettingsAsync()
    {
        int warning = 90, critical = 30, flash = 5;
        if (await Db.HasTableAsync("settings"))
        {
            var setting = await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1");
            if (setting != null)
            {
                if (setting.TryGetValue("expiry_warning_days", out var w) && w != null && int.TryParse(w.ToString(), out var wv)) warning = wv;
                if (setting.TryGetValue("expiry_critical_days", out var c) && c != null && int.TryParse(c.ToString(), out var cv)) critical = Math.Max(1, cv);
                if (setting.TryGetValue("expiry_daily_flash_limit", out var f) && f != null && int.TryParse(f.ToString(), out var fv)) flash = Math.Clamp(fv, 1, 20);
            }
        }
        return (warning, critical, flash);
    }

    [HttpGet("api/batch-expiry")]
    public async Task<IActionResult> Index(
        [FromQuery] string status = "all",
        [FromQuery] string? months = null,
        [FromQuery(Name = "months_mode")] string monthsMode = "exact",
        [FromQuery] string? search = "",
        [FromQuery(Name = "product_id")] long? productId = null,
        [FromQuery(Name = "supplier_id")] long? supplierId = null,
        [FromQuery(Name = "brand_id")] long? brandId = null,
        [FromQuery(Name = "category_id")] long? categoryId = null,
        [FromQuery(Name = "storage_location_id")] long? storageLocationId = null,
        [FromQuery(Name = "include_zero_qty")] string? includeZeroQtyRaw = null,
        [FromQuery(Name = "has_custom_warning")] string? hasCustom = null,
        [FromQuery(Name = "sort_by")] string sortBy = "expiry_date",
        [FromQuery(Name = "sort_dir")] string sortDir = "asc",
        [FromQuery(Name = "per_page")] int perPage = 25,
        [FromQuery] int page = 1)
    {
        if (!await CanAsync("batch-expiry.view", "product.view", "product.update")) return Forbidden();

        var includeZeroQty = !string.IsNullOrWhiteSpace(includeZeroQtyRaw) &&
            (includeZeroQtyRaw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             includeZeroQtyRaw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             includeZeroQtyRaw.Equals("yes", StringComparison.OrdinalIgnoreCase));

        var (globalWarningDays, globalCriticalDays, dailyFlashLimit) = await GetGlobalExpirySettingsAsync();

        int? monthsFilter = !string.IsNullOrWhiteSpace(months) && int.TryParse(months, out var mf) ? Math.Max(1, mf) : null;
        var resolvedMonthsMode = string.Equals(monthsMode, "within", StringComparison.OrdinalIgnoreCase) ? "within" : "exact";
        var searchTrim = (search ?? "").Trim();
        var resolvedSortDir = string.Equals(sortDir, "desc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
        perPage = Math.Clamp(perPage <= 0 ? 25 : perPage, 1, 500);
        page = Math.Max(1, page);

        var hasStorageLocations = await Db.HasTableAsync("storage_locations") && await Db.HasColumnAsync("batches", "storage_location_id");
        var hasBatchPricing = await Db.HasColumnAsync("batches", "unit_sale_price");
        var hasExpiryPolicy = await Db.HasColumnAsync("brands", "expiry_return_policy") && await Db.HasColumnAsync("suppliers", "expiry_return_policy");
        var hasProductCriticalCol = await Db.HasColumnAsync("products", "expiry_critical_days");

        var where = new List<string> { "b.expiry_date IS NOT NULL" };
        var param = new DynamicParameters();

        if (!includeZeroQty) where.Add("b.quantity > 0");

        if (!string.IsNullOrEmpty(searchTrim))
        {
            if (hasStorageLocations)
            {
                where.Add("(p.name LIKE @q OR p.product_code LIKE @q OR p.barcode LIKE @q OR b.batch_number LIKE @q OR b.rack LIKE @q OR sl.name LIKE @q)");
            }
            else
            {
                where.Add("(p.name LIKE @q OR p.product_code LIKE @q OR p.barcode LIKE @q OR b.batch_number LIKE @q)");
            }
            param.Add("q", $"%{searchTrim}%");
        }

        if (productId.HasValue) { where.Add("p.id = @productId"); param.Add("productId", productId.Value); }
        if (supplierId.HasValue) { where.Add("p.supplier_id = @supplierId"); param.Add("supplierId", supplierId.Value); }
        if (brandId.HasValue) { where.Add("p.brand_id = @brandId"); param.Add("brandId", brandId.Value); }
        if (categoryId.HasValue) { where.Add("p.category_id = @categoryId"); param.Add("categoryId", categoryId.Value); }
        if (hasStorageLocations && storageLocationId.HasValue) { where.Add("b.storage_location_id = @storageLocationId"); param.Add("storageLocationId", storageLocationId.Value); }

        if (hasCustom is "1" or "true")
        {
            where.Add(hasProductCriticalCol
                ? "(p.expiry_warning_days IS NOT NULL OR p.expiry_critical_days IS NOT NULL)"
                : "p.expiry_warning_days IS NOT NULL");
        }
        else if (hasCustom is "0" or "false")
        {
            where.Add(hasProductCriticalCol
                ? "(p.expiry_warning_days IS NULL AND p.expiry_critical_days IS NULL)"
                : "p.expiry_warning_days IS NULL");
        }

        var sql = $@"
            SELECT b.id AS batch_id, b.batch_number, b.expiry_date, b.quantity, b.created_at AS batch_created_at,
                   p.id AS product_id, p.product_code, p.barcode, p.name AS product_name, p.pack_size,
                   p.quantity AS product_total_qty, p.unit_purchase_price, p.pack_purchase_price,
                   p.unit_sale_price, p.pack_sale_price, p.avg_price, p.rack AS product_rack,
                   p.expiry_warning_days AS product_warning_days,
                   p.supplier_id, p.brand_id, p.category_id,
                   COALESCE(s.name, '') AS supplier_name, COALESCE(s.phone, '') AS supplier_phone,
                   COALESCE(br.name, '') AS brand_name, COALESCE(c.name, '') AS category_name
                   {(hasProductCriticalCol ? ", p.expiry_critical_days AS product_critical_days" : "")}
                   {(hasBatchPricing ? ", b.unit_sale_price AS batch_unit_sale_price, b.unit_purchase_price AS batch_unit_purchase_price, b.pack_sale_price AS batch_pack_sale_price, b.pack_purchase_price AS batch_pack_purchase_price, b.whole_sale_unit_price AS batch_whole_sale_unit_price, b.whole_sale_pack_price AS batch_whole_sale_pack_price" : "")}
                   {(hasExpiryPolicy ? ", br.expiry_return_policy AS brand_expiry_return_policy, br.expiry_return_months AS brand_expiry_return_months, br.expiry_return_days AS brand_expiry_return_days, s.expiry_return_policy AS supplier_expiry_return_policy, s.expiry_return_months AS supplier_expiry_return_months, s.expiry_return_days AS supplier_expiry_return_days" : "")}
                   {(hasStorageLocations ? ", b.storage_location_id, p.storage_location_id AS product_storage_location_id, b.rack AS batch_rack, COALESCE(sl.name, '') AS storage_location_name, COALESCE(sl.code, '') AS storage_location_code, COALESCE(sl.type, 'shop') AS storage_location_type" : "")}
            FROM batches b
            INNER JOIN products p ON p.id = b.product_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            LEFT JOIN brands br ON br.id = p.brand_id
            LEFT JOIN categories c ON c.id = p.category_id
            {(hasStorageLocations ? "LEFT JOIN storage_locations sl ON sl.id = b.storage_location_id" : "")}
            WHERE {string.Join(" AND ", where)}";

        var allRows = await Db.QueryListAsync(sql, param);

        var monthBuckets = new Dictionary<string, int>();
        for (int m = 1; m <= 12; m++) monthBuckets[m.ToString()] = 0;
        monthBuckets["12+"] = 0;

        int totalBatchesTracked = 0, totalExpiredBatches = 0, totalExpiredQty = 0;
        double totalExpiredValue = 0.0;
        int totalCriticalBatches = 0, totalCriticalQty = 0;
        double totalCriticalValue = 0.0;
        int totalWarningBatches = 0, totalWarningQty = 0;
        double totalWarningValue = 0.0;
        double totalStockValueAtRisk = 0.0;

        var enriched = new List<Dictionary<string, object?>>();

        foreach (var row in allRows)
        {
            string resolvedPolicy = "before_expiry";
            int resolvedPolicyMonths = 3;
            int resolvedPolicyDays = 90;

            if (hasExpiryPolicy)
            {
                if (row["brand_id"] != null && !string.IsNullOrEmpty(row["brand_expiry_return_policy"]?.ToString()))
                {
                    resolvedPolicy = row["brand_expiry_return_policy"]!.ToString()!;
                    resolvedPolicyMonths = Convert.ToInt32(row["brand_expiry_return_months"] ?? 3);
                    resolvedPolicyDays = Convert.ToInt32(row["brand_expiry_return_days"] ?? 90);
                }
                else if (row["supplier_id"] != null && !string.IsNullOrEmpty(row["supplier_expiry_return_policy"]?.ToString()))
                {
                    resolvedPolicy = row["supplier_expiry_return_policy"]!.ToString()!;
                    resolvedPolicyMonths = Convert.ToInt32(row["supplier_expiry_return_months"] ?? 3);
                    resolvedPolicyDays = Convert.ToInt32(row["supplier_expiry_return_days"] ?? 90);
                }
            }

            var intimationMeta = BrandController.EvaluateBatchIntimation(
                row["expiry_date"]?.ToString(),
                resolvedPolicy,
                resolvedPolicyMonths,
                resolvedPolicyDays);

            var daysRemaining = Convert.ToInt32(intimationMeta["days_remaining"] ?? 0);
            var (monthsRemaining, monthsRemainingLabel) = ComputeMonthsRemainingMeta(daysRemaining);

            var warningDays = row["product_warning_days"] != null
                ? Convert.ToInt32(row["product_warning_days"])
                : globalWarningDays;

            int? productCriticalDays = hasProductCriticalCol && row.TryGetValue("product_critical_days", out var pcd) && pcd != null
                ? Convert.ToInt32(pcd)
                : null;

            var criticalDays = productCriticalDays ?? Math.Min(globalCriticalDays, warningDays);
            if (warningDays < criticalDays) warningDays = criticalDays;

            string batchStatus;
            if (daysRemaining < 0) batchStatus = "expired";
            else if (daysRemaining <= criticalDays) batchStatus = "critical";
            else if (daysRemaining <= warningDays || (resolvedPolicyDays > 90 && intimationMeta["is_intimation_due"] is true)) batchStatus = "warning";
            else batchStatus = "safe";

            var packSize = Math.Max(1, Convert.ToInt32(row["pack_size"] ?? 1));
            var unitCost = Convert.ToDouble((hasBatchPricing ? row["batch_unit_purchase_price"] : null) ?? row["unit_purchase_price"] ?? 0.0);
            if (unitCost <= 0 && row["avg_price"] != null) unitCost = Convert.ToDouble(row["avg_price"]);
            if (unitCost <= 0 && row["pack_purchase_price"] != null) unitCost = Convert.ToDouble(row["pack_purchase_price"]) / packSize;

            var unitSalePrice = Convert.ToDouble((hasBatchPricing ? row["batch_unit_sale_price"] : null) ?? row["unit_sale_price"] ?? 0.0);
            var qty = Convert.ToInt32(row["quantity"] ?? 0);
            var valueAtRisk = Math.Round(qty * unitCost, 2);
            var retailValue = Math.Round(qty * unitSalePrice, 2);

            totalBatchesTracked++;
            totalStockValueAtRisk += valueAtRisk;

            if (daysRemaining >= 0)
            {
                if (monthsRemaining <= 12) monthBuckets[monthsRemaining.ToString()]++;
                else monthBuckets["12+"]++;
            }

            if (batchStatus == "expired")
            {
                totalExpiredBatches++;
                totalExpiredQty += qty;
                totalExpiredValue += valueAtRisk;
            }
            else if (batchStatus == "critical")
            {
                totalCriticalBatches++;
                totalCriticalQty += qty;
                totalCriticalValue += valueAtRisk;
            }

            if (daysRemaining <= warningDays)
            {
                totalWarningBatches++;
                totalWarningQty += qty;
                totalWarningValue += valueAtRisk;
            }

            row["days_remaining"] = daysRemaining;
            row["months_remaining"] = monthsRemaining;
            row["months_remaining_label"] = monthsRemainingLabel;
            row["product_critical_days"] = productCriticalDays;
            row["effective_warning_days"] = warningDays;
            row["effective_critical_days"] = criticalDays;
            row["has_custom_warning"] = row["product_warning_days"] != null || productCriticalDays != null;
            row["status"] = batchStatus;
            row["should_flash_warning"] = (daysRemaining >= 0 && daysRemaining <= warningDays) || daysRemaining < 0;
            row["expiry_return_policy"] = resolvedPolicy;
            row["expiry_return_months"] = resolvedPolicyMonths;
            row["expiry_return_days"] = resolvedPolicyDays;
            row["expiry_policy_label"] = BrandController.FormatExpiryPolicyLabel(resolvedPolicy, resolvedPolicyMonths, resolvedPolicyDays);
            row["return_deadline_date"] = intimationMeta["return_deadline_date"];
            row["days_until_return_deadline"] = intimationMeta["days_until_return_deadline"];
            row["intimation_status"] = intimationMeta["intimation_status"];
            row["unit_cost"] = Math.Round(unitCost, 2);
            row["value_at_risk"] = valueAtRisk;
            row["retail_value"] = retailValue;
            row["packs_count"] = qty / packSize;
            row["loose_units"] = qty % packSize;

            enriched.Add(row);
        }

        IEnumerable<Dictionary<string, object?>> filtered = status switch
        {
            "expired" => enriched.Where(i => i["status"]?.ToString() == "expired"),
            "critical" => enriched.Where(i => i["status"]?.ToString() == "critical"),
            "warning" => enriched.Where(i => i["status"]?.ToString() is "expired" or "critical" or "warning"),
            "safe" => enriched.Where(i => i["status"]?.ToString() == "safe"),
            _ => enriched
        };

        if (monthsFilter.HasValue)
        {
            filtered = resolvedMonthsMode == "within"
                ? filtered.Where(i => Convert.ToInt32(i["days_remaining"] ?? -1) >= 0 && Convert.ToInt32(i["months_remaining"] ?? 0) <= monthsFilter.Value)
                : filtered.Where(i => Convert.ToInt32(i["days_remaining"] ?? -1) >= 0 && Convert.ToInt32(i["months_remaining"] ?? 0) == monthsFilter.Value);
        }

        var filteredList = filtered.ToList();
        filteredList.Sort((a, b) =>
        {
            var valA = a.TryGetValue(sortBy, out var va) ? va : null;
            var valB = b.TryGetValue(sortBy, out var vb) ? vb : null;
            int cmp;
            if (sortBy is "days_remaining" or "months_remaining" or "quantity" or "value_at_risk")
            {
                cmp = Convert.ToDouble(valA ?? 0.0).CompareTo(Convert.ToDouble(valB ?? 0.0));
            }
            else
            {
                cmp = string.CompareOrdinal(valA?.ToString() ?? "", valB?.ToString() ?? "");
            }
            return resolvedSortDir == "desc" ? -cmp : cmp;
        });

        var total = filteredList.Count;
        var offset = (page - 1) * perPage;
        var pagedRows = filteredList.Skip(offset).Take(perPage).ToList();

        return Ok(new Dictionary<string, object?>
        {
            ["rows"] = pagedRows,
            ["total"] = total,
            ["page"] = page,
            ["per_page"] = perPage,
            ["last_page"] = (int)Math.Ceiling(total / (double)perPage),
            ["summary"] = new Dictionary<string, object?>
            {
                ["total_batches_tracked"] = totalBatchesTracked,
                ["total_expired_batches"] = totalExpiredBatches,
                ["total_expired_qty"] = totalExpiredQty,
                ["total_expired_value"] = Math.Round(totalExpiredValue, 2),
                ["total_critical_batches"] = totalCriticalBatches,
                ["total_critical_qty"] = totalCriticalQty,
                ["total_critical_value"] = Math.Round(totalCriticalValue, 2),
                ["total_warning_batches"] = totalWarningBatches,
                ["total_warning_qty"] = totalWarningQty,
                ["total_warning_value"] = Math.Round(totalWarningValue, 2),
                ["total_stock_value_at_risk"] = Math.Round(totalStockValueAtRisk, 2),
                ["global_warning_days"] = globalWarningDays,
                ["global_critical_days"] = globalCriticalDays,
                ["daily_flash_limit"] = dailyFlashLimit,
                ["month_buckets"] = monthBuckets
            },
            ["filters"] = new Dictionary<string, object?>
            {
                ["status"] = status,
                ["months"] = monthsFilter,
                ["months_mode"] = resolvedMonthsMode,
                ["search"] = searchTrim,
                ["supplier_id"] = supplierId,
                ["brand_id"] = brandId,
                ["category_id"] = categoryId,
                ["has_custom_warning"] = hasCustom,
                ["sort_by"] = sortBy,
                ["sort_dir"] = resolvedSortDir
            }
        });
    }

    [HttpPut("api/batch-expiry/product-warning/{productId:long}")]
    public async Task<IActionResult> UpdateProductWarning(long productId)
    {
        if (!await CanAsync("batch-expiry.update")) return Forbidden();

        var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @productId", new { productId });
        if (product == null) return NotFound(new { message = "Product not found." });

        var body = await ReadJsonBodyAsync();
        int? warningDays = null;
        if (body.ContainsKey("expiry_warning_days"))
        {
            var raw = GetString(body, "expiry_warning_days");
            if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var wv))
            {
                if (wv is < 1 or > 3650) return Unprocessable("expiry_warning_days must be between 1 and 3650.");
                warningDays = wv;
            }
        }

        var updates = new Dictionary<string, object?>
        {
            ["expiry_warning_days"] = warningDays
        };

        int? criticalDays = product.TryGetValue("expiry_critical_days", out var curCrit) && curCrit != null ? Convert.ToInt32(curCrit) : null;
        if (await Db.HasColumnAsync("products", "expiry_critical_days") && body.ContainsKey("expiry_critical_days"))
        {
            var rawCrit = GetString(body, "expiry_critical_days");
            if (!string.IsNullOrWhiteSpace(rawCrit) && int.TryParse(rawCrit, out var cv))
            {
                if (cv is < 1 or > 3650) return Unprocessable("expiry_critical_days must be between 1 and 3650.");
                criticalDays = cv;
            }
            else
            {
                criticalDays = null;
            }
            updates["expiry_critical_days"] = criticalDays;
        }

        await Db.UpdateAsync("products", productId, updates);

        var (globalWarning, globalCritical, _) = await GetGlobalExpirySettingsAsync();
        var effectiveWarning = warningDays ?? globalWarning;
        var effectiveCritical = criticalDays ?? Math.Min(globalCritical, effectiveWarning);

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = "Product warning threshold updated successfully.",
            ["product_id"] = productId,
            ["expiry_warning_days"] = warningDays,
            ["expiry_critical_days"] = criticalDays,
            ["effective_warning_days"] = Math.Max(effectiveWarning, effectiveCritical),
            ["effective_critical_days"] = effectiveCritical
        });
    }

    [HttpPut("api/batch-expiry/global-warning")]
    public async Task<IActionResult> UpdateGlobalWarning()
    {
        if (!await CanAsync("batch-expiry.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var warningDays = GetInt(body, "expiry_warning_days", 0);
        if (warningDays is < 1 or > 3650)
        {
            return Unprocessable("The expiry_warning_days field is required and must be between 1 and 3650.", new Dictionary<string, string[]>
            {
                ["expiry_warning_days"] = ["The expiry_warning_days field is required."]
            });
        }

        var updates = new Dictionary<string, object?>
        {
            ["expiry_warning_days"] = warningDays
        };

        if (await Db.HasColumnAsync("settings", "expiry_critical_days") && body.ContainsKey("expiry_critical_days"))
        {
            var crit = GetInt(body, "expiry_critical_days", 30);
            updates["expiry_critical_days"] = Math.Min(warningDays, Math.Max(1, crit));
        }

        if (await Db.HasColumnAsync("settings", "expiry_daily_flash_limit") && body.ContainsKey("expiry_daily_flash_limit"))
        {
            var flash = GetInt(body, "expiry_daily_flash_limit", 5);
            updates["expiry_daily_flash_limit"] = Math.Clamp(flash, 1, 20);
        }

        var existingSetting = await Db.QuerySingleOrDefaultAsync("SELECT id FROM settings ORDER BY id ASC LIMIT 1");
        if (existingSetting != null)
        {
            await Db.UpdateAsync("settings", Convert.ToInt64(existingSetting["id"]), updates);
        }
        else
        {
            updates["id"] = 1;
            await Db.InsertAsync("settings", updates);
        }

        var (gw, gc, df) = await GetGlobalExpirySettingsAsync();
        return Ok(new Dictionary<string, object?>
        {
            ["message"] = "Global expiry warning window updated successfully.",
            ["expiry_warning_days"] = gw,
            ["expiry_critical_days"] = gc,
            ["expiry_daily_flash_limit"] = df
        });
    }

    [HttpGet("api/notifications/expiry-alerts")]
    public async Task<IActionResult> ExpiryAlerts([FromQuery] int limit = 15)
    {
        if (!await CanAsync(
            "batch-expiry.view", "product.view", "report.near-expiry-product.view",
            "sale-invoice.create", "barcode-pos.view", "barcode-pos.create", "purchase-invoice.create"))
        {
            return Forbidden();
        }

        limit = Math.Clamp(limit <= 0 ? 15 : limit, 1, 100);
        var (globalWarningDays, globalCriticalDays, dailyFlashLimit) = await GetGlobalExpirySettingsAsync();
        var hasProductCriticalCol = await Db.HasColumnAsync("products", "expiry_critical_days");

        var today = DateTime.UtcNow.Date;
        var todayStr = today.ToString("yyyy-MM-dd");
        var maxGlobalWindow = Math.Max(globalWarningDays, globalCriticalDays);

        var rows = await Db.QueryListAsync($@"
            SELECT b.id AS batch_id, b.batch_number, b.expiry_date, b.quantity,
                   p.id AS product_id, p.name AS product_name, p.product_code, p.pack_size,
                   p.expiry_warning_days AS product_warning_days, p.supplier_id,
                   COALESCE(s.name, '') AS supplier_name
                   {(hasProductCriticalCol ? ", p.expiry_critical_days AS product_critical_days" : "")}
            FROM batches b
            INNER JOIN products p ON p.id = b.product_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            WHERE b.expiry_date IS NOT NULL
              AND b.quantity > 0
              AND b.expiry_date <= date(@todayStr, '+' || COALESCE(p.expiry_warning_days, @maxGlobalWindow) || ' days')
            ORDER BY b.expiry_date ASC",
            new { todayStr, maxGlobalWindow });

        var expiredList = new List<Dictionary<string, object?>>();
        var criticalList = new List<Dictionary<string, object?>>();
        var warningList = new List<Dictionary<string, object?>>();

        foreach (var row in rows)
        {
            var expStr = CustomerController.FormatDateYmd(row["expiry_date"]) ?? todayStr;
            var expDt = DateTime.TryParse(expStr, out var parsed) ? parsed.Date : today;
            var daysRemaining = (int)(expDt - today).TotalDays;
            var (monthsRemaining, monthsLabel) = ComputeMonthsRemainingMeta(daysRemaining);

            var warningDays = row["product_warning_days"] != null
                ? Convert.ToInt32(row["product_warning_days"])
                : globalWarningDays;

            int? productCriticalDays = hasProductCriticalCol && row.TryGetValue("product_critical_days", out var pcd) && pcd != null
                ? Convert.ToInt32(pcd)
                : null;

            var criticalDays = productCriticalDays ?? Math.Min(globalCriticalDays, warningDays);
            if (warningDays < criticalDays) warningDays = criticalDays;

            row["days_remaining"] = daysRemaining;
            row["months_remaining"] = monthsRemaining;
            row["months_remaining_label"] = monthsLabel;
            row["effective_warning_days"] = warningDays;
            row["effective_critical_days"] = criticalDays;

            if (daysRemaining < 0)
            {
                row["status"] = "expired";
                expiredList.Add(row);
            }
            else if (daysRemaining <= criticalDays)
            {
                row["status"] = "critical";
                criticalList.Add(row);
            }
            else if (daysRemaining <= warningDays)
            {
                row["status"] = "warning";
                warningList.Add(row);
            }
        }

        expiredList.Sort((a, b) => Convert.ToInt32(a["days_remaining"]).CompareTo(Convert.ToInt32(b["days_remaining"])));
        criticalList.Sort((a, b) => Convert.ToInt32(a["days_remaining"]).CompareTo(Convert.ToInt32(b["days_remaining"])));
        warningList.Sort((a, b) => Convert.ToInt32(a["days_remaining"]).CompareTo(Convert.ToInt32(b["days_remaining"])));

        var combined = expiredList.Concat(criticalList).Concat(warningList).ToList();
        var topItems = combined.Take(limit).ToList();

        return Ok(new Dictionary<string, object?>
        {
            ["count"] = combined.Count,
            ["expired_count"] = expiredList.Count,
            ["critical_count"] = criticalList.Count,
            ["warning_count"] = warningList.Count,
            ["global_warning_days"] = globalWarningDays,
            ["global_critical_days"] = globalCriticalDays,
            ["daily_flash_limit"] = dailyFlashLimit,
            ["rows"] = topItems
        });
    }
}

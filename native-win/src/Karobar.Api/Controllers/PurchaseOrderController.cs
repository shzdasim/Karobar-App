using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/purchase-orders")]
[Authorize]
public class PurchaseOrderController : ApiControllerBase
{
    private readonly SupplierProductSortingService _sortingService;

    public PurchaseOrderController(KarobarDb db) : base(db)
    {
        _sortingService = new SupplierProductSortingService(db);
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? supplier = null,
        [FromQuery] string? search = null,
        [FromQuery] string? q = null,
        [FromQuery] string? delivery_date = null,
        [FromQuery] string? expected_received_date = null,
        [FromQuery] string? expected_date = null,
        [FromQuery] string? sort = "newest",
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 20)
    {
        if (!await CanAsync("purchase-order.view", "purchase-invoice.view"))
        {
            return Forbidden();
        }

        var where = new List<string> { "purchase_orders.status NOT IN ('received', 'partially_received')" };
        var dyn = new DynamicParameters();

        var supplierTrim = (supplier ?? "").Trim();
        var searchTrim = (search ?? q ?? "").Trim();

        var effectiveQuery = !string.IsNullOrEmpty(supplierTrim) ? supplierTrim : searchTrim;
        if (!string.IsNullOrEmpty(effectiveQuery))
        {
            var cleanPo = Regex.Replace(effectiveQuery, "[^0-9]", "");
            if (!string.IsNullOrEmpty(cleanPo) && long.TryParse(cleanPo, out var cleanId))
            {
                where.Add("(EXISTS (SELECT 1 FROM suppliers s WHERE s.id = purchase_orders.supplier_id AND s.name LIKE @searchLike) OR purchase_orders.reference LIKE @searchLike OR purchase_orders.number LIKE @searchLike OR purchase_orders.id = @cleanId)");
                dyn.Add("cleanId", cleanId);
            }
            else
            {
                where.Add("(EXISTS (SELECT 1 FROM suppliers s WHERE s.id = purchase_orders.supplier_id AND s.name LIKE @searchLike) OR purchase_orders.reference LIKE @searchLike OR purchase_orders.number LIKE @searchLike)");
            }
            dyn.Add("searchLike", "%" + effectiveQuery + "%");
        }

        var delivDate = (delivery_date ?? expected_received_date ?? expected_date ?? "").Trim();
        if (!string.IsNullOrEmpty(delivDate))
        {
            where.Add("date(purchase_orders.expected_received_date) = date(@delivDate)");
            dyn.Add("delivDate", delivDate);
        }

        var orderBy = (sort ?? "newest").ToLowerInvariant() switch
        {
            "oldest" => "purchase_orders.id ASC",
            "date_desc" => "purchase_orders.date DESC, purchase_orders.id DESC",
            "date_asc" => "purchase_orders.date ASC, purchase_orders.id ASC",
            "delivery_desc" => "purchase_orders.expected_received_date DESC, purchase_orders.id DESC",
            "delivery_asc" => "purchase_orders.expected_received_date ASC, purchase_orders.id ASC",
            "supplier_asc" => "supplier_name ASC, purchase_orders.id DESC",
            "supplier_desc" => "supplier_name DESC, purchase_orders.id DESC",
            "total_desc" => "total_amount DESC, purchase_orders.id DESC",
            "total_asc" => "total_amount ASC, purchase_orders.id DESC",
            "packs_desc" or "units_desc" => "total_packs DESC, purchase_orders.id DESC",
            "packs_asc" or "units_asc" => "total_packs ASC, purchase_orders.id DESC",
            "items_desc" => "items_count DESC, purchase_orders.id DESC",
            "items_asc" => "items_count ASC, purchase_orders.id DESC",
            "status_asc" => "purchase_orders.status ASC, purchase_orders.id DESC",
            "status_desc" => "purchase_orders.status DESC, purchase_orders.id DESC",
            _ => "purchase_orders.id DESC"
        };

        var whereSql = string.Join(" AND ", where);
        var selectSql = $@"
            SELECT purchase_orders.*,
                   (SELECT COUNT(1) FROM purchase_order_items poi WHERE poi.purchase_order_id = purchase_orders.id) as items_count,
                   (SELECT COALESCE(SUM(poi.ordered_quantity), 0) FROM purchase_order_items poi WHERE poi.purchase_order_id = purchase_orders.id) as items_sum_ordered_quantity,
                   (SELECT COALESCE(SUM(poi.ordered_quantity * poi.unit_purchase_price * (1.0 - COALESCE(poi.item_discount_percentage, 0) / 100.0)), 0) FROM purchase_order_items poi WHERE poi.purchase_order_id = purchase_orders.id) as total_amount,
                   (SELECT COALESCE(SUM(CAST(poi.ordered_quantity AS REAL) / NULLIF(poi.pack_size, 0)), 0) FROM purchase_order_items poi WHERE poi.purchase_order_id = purchase_orders.id) as total_packs,
                   (SELECT s.name FROM suppliers s WHERE s.id = purchase_orders.supplier_id LIMIT 1) as supplier_name
            FROM purchase_orders
            WHERE {whereSql}
            ORDER BY {orderBy}";

        var countSql = $"SELECT COUNT(1) FROM purchase_orders WHERE {whereSql}";

        var paginated = await Db.PaginateAsync(selectSql, countSql, dyn, page, per_page, async rows =>
        {
            var supIds = rows.Select(r => ToLong(r.GetValueOrDefault("supplier_id"))).Where(id => id > 0).Distinct().ToArray();
            var poIds = rows.Select(r => ToLong(r.GetValueOrDefault("id"))).Where(id => id > 0).Distinct().ToArray();

            var suppliers = new Dictionary<long, Dictionary<string, object?>>();
            if (supIds.Length > 0 && await Db.HasTableAsync("suppliers"))
            {
                var sRows = await Db.QueryListAsync("SELECT * FROM suppliers WHERE id IN @ids", new { ids = supIds });
                foreach (var sr in sRows) suppliers[ToLong(sr["id"])] = sr;
            }

            var invoicesByPo = new Dictionary<long, List<Dictionary<string, object?>>>();
            if (poIds.Length > 0 && await Db.HasTableAsync("purchase_invoices"))
            {
                var invRows = await Db.QueryListAsync("SELECT * FROM purchase_invoices WHERE purchase_order_id IN @ids", new { ids = poIds });
                foreach (var ir in invRows)
                {
                    var poid = ToLong(ir.GetValueOrDefault("purchase_order_id"));
                    if (!invoicesByPo.TryGetValue(poid, out var list))
                    {
                        list = new List<Dictionary<string, object?>>();
                        invoicesByPo[poid] = list;
                    }
                    list.Add(ir);
                }
            }

            foreach (var r in rows)
            {
                var sid = ToLong(r.GetValueOrDefault("supplier_id"));
                var poid = ToLong(r.GetValueOrDefault("id"));
                r["supplier"] = sid > 0 && suppliers.TryGetValue(sid, out var sup) ? sup : null;
                r["invoices"] = invoicesByPo.TryGetValue(poid, out var invs) ? invs : new List<Dictionary<string, object?>>();
                if (r.GetValueOrDefault("number") == null || string.IsNullOrWhiteSpace(r["number"]?.ToString()))
                {
                    r["number"] = "PO-" + poid.ToString("D5");
                }
            }

            return rows;
        });

        return Ok(paginated);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("purchase-order.view", "purchase-invoice.view"))
        {
            return Forbidden();
        }

        var order = await LoadPurchaseOrderAsync(id);
        if (order == null) return NotFound(new { message = "Purchase Order not found." });

        return Ok(order);
    }

    [HttpGet("supplier-sort-order/{supplierId:long}")]
    public async Task<IActionResult> SupplierSortOrder(long supplierId)
    {
        if (!await CanAsync("purchase-order.view", "purchase-invoice.view"))
        {
            return Forbidden();
        }

        var productIds = await _sortingService.ProductIdsAsync(supplierId);
        return Ok(new Dictionary<string, object?>
        {
            ["product_ids"] = productIds
        });
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("purchase-order.create"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var (valid, errResult, header, items) = await ValidateOrderPayloadAsync(body);
        if (!valid) return errResult!;

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        header!["status"] = "ordered";
        header["created_by"] = CurrentUserId;
        header["updated_by"] = CurrentUserId;

        var orderId = await Db.InsertAsync("purchase_orders", header, conn, tx);
        var orderNumber = "PO-" + orderId.ToString("D5");
        await Db.UpdateAsync("purchase_orders", orderId, new Dictionary<string, object?>
        {
            ["number"] = orderNumber
        }, "id", conn, tx);

        foreach (var item in items!)
        {
            item["purchase_order_id"] = orderId;
            await Db.InsertAsync("purchase_order_items", item, conn, tx);
        }

        var productIds = items.Select(i => ToLong(i.GetValueOrDefault("product_id"))).Where(pid => pid > 0).Distinct().ToArray();
        if (productIds.Length > 0 && await Db.HasTableAsync("user_demands"))
        {
            await Db.ExecuteAsync(
                "UPDATE user_demands SET status = 'ordered', updated_at = @now WHERE product_id IN @ids AND status = 'pending'",
                new { ids = productIds, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                conn,
                tx);
        }

        await tx.CommitAsync();

        var loaded = await LoadPurchaseOrderAsync(orderId);
        return StatusCode(201, loaded);
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("purchase-order.update"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_orders WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Purchase Order not found." });

        var hasInvoices = await Db.HasTableAsync("purchase_invoices") &&
                          await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM purchase_invoices WHERE purchase_order_id = @id", new { id }) > 0;
        var status = existing.GetValueOrDefault("status")?.ToString() ?? "";
        if (!string.Equals(status, "ordered", StringComparison.OrdinalIgnoreCase) || hasInvoices)
        {
            return StatusCode(409, new { message = "Only unreceived orders can be edited." });
        }

        var body = await ReadJsonBodyAsync();
        var (valid, errResult, header, items) = await ValidateOrderPayloadAsync(body);
        if (!valid) return errResult!;

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        header!["updated_by"] = CurrentUserId;
        await Db.UpdateAsync("purchase_orders", id, header, "id", conn, tx);
        await Db.ExecuteAsync("DELETE FROM purchase_order_items WHERE purchase_order_id = @id", new { id }, conn, tx);

        foreach (var item in items!)
        {
            item["purchase_order_id"] = id;
            await Db.InsertAsync("purchase_order_items", item, conn, tx);
        }

        var productIds = items.Select(i => ToLong(i.GetValueOrDefault("product_id"))).Where(pid => pid > 0).Distinct().ToArray();
        if (productIds.Length > 0 && await Db.HasTableAsync("user_demands"))
        {
            await Db.ExecuteAsync(
                "UPDATE user_demands SET status = 'ordered', updated_at = @now WHERE product_id IN @ids AND status = 'pending'",
                new { ids = productIds, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                conn,
                tx);
        }

        await tx.CommitAsync();

        var loaded = await LoadPurchaseOrderAsync(id);
        return Ok(loaded);
    }

    [HttpPost("{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id)
    {
        if (!await CanAsync("purchase-order.cancel", "purchase-order.update", "purchase-order.delete"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_orders WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Purchase Order not found." });

        var hasInvoices = await Db.HasTableAsync("purchase_invoices") &&
                          await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM purchase_invoices WHERE purchase_order_id = @id", new { id }) > 0;
        var status = existing.GetValueOrDefault("status")?.ToString() ?? "";
        if (!string.Equals(status, "ordered", StringComparison.OrdinalIgnoreCase) || hasInvoices)
        {
            return StatusCode(409, new { message = "Only unreceived orders can be cancelled." });
        }

        await Db.UpdateAsync("purchase_orders", id, new Dictionary<string, object?>
        {
            ["status"] = "cancelled",
            ["updated_by"] = CurrentUserId
        });

        var updated = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_orders WHERE id = @id", new { id });
        return Ok(updated);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("purchase-order.delete"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_orders WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Purchase Order not found." });

        var hasInvoices = await Db.HasTableAsync("purchase_invoices") &&
                          await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM purchase_invoices WHERE purchase_order_id = @id", new { id }) > 0;
        var status = (existing.GetValueOrDefault("status")?.ToString() ?? "").ToLowerInvariant();
        if (status is not ("ordered" or "cancelled") || hasInvoices)
        {
            return StatusCode(409, new { message = "Received Purchase Orders cannot be deleted." });
        }

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await Db.ExecuteAsync("DELETE FROM purchase_order_items WHERE purchase_order_id = @id", new { id }, conn, tx);
        await Db.ExecuteAsync("DELETE FROM purchase_orders WHERE id = @id", new { id }, conn, tx);

        await tx.CommitAsync();
        return NoContent();
    }

    [HttpPost("{id:long}/receive")]
    public async Task<IActionResult> Receive(long id)
    {
        if (!await CanAsync("purchase-order.view") || !await CanAsync("purchase-invoice.create"))
        {
            return Forbidden();
        }

        var order = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_orders WHERE id = @id", new { id });
        if (order == null) return NotFound(new { message = "Purchase Order not found." });

        var status = (order.GetValueOrDefault("status")?.ToString() ?? "").ToLowerInvariant();
        if (status is not ("ordered" or "partially_received"))
        {
            return StatusCode(409, new { message = "This order cannot be received." });
        }

        var supplierId = ToLong(order.GetValueOrDefault("supplier_id"));
        var supplier = supplierId > 0
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @sid", new { sid = supplierId })
            : null;

        var poItems = await Db.QueryListAsync("SELECT * FROM purchase_order_items WHERE purchase_order_id = @id", new { id });
        var previousReceipts = await Db.HasTableAsync("purchase_invoices")
            ? await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM purchase_invoices WHERE purchase_order_id = @id AND (status IS NULL OR status != 'reversed')", new { id })
            : 0;

        var invoiceItems = await Db.HasTableAsync("purchase_invoice_items") && poItems.Count > 0
            ? await Db.QueryListAsync(
                @"SELECT pii.* FROM purchase_invoice_items pii
                  INNER JOIN purchase_invoices pi ON pi.id = pii.purchase_invoice_id
                  WHERE pii.purchase_order_item_id IN @ids
                    AND (pi.status IS NULL OR pi.status != 'reversed')",
                new { ids = poItems.Select(x => ToLong(x["id"])).ToArray() })
            : new List<Dictionary<string, object?>>();

        var sortedLines = await _sortingService.SortAsync(supplierId, poItems, line => ToLong(line.GetValueOrDefault("product_id")));

        long totalRemainingUnits = 0;
        var receiveItems = new List<Dictionary<string, object?>>();

        foreach (var line in sortedLines)
        {
            var lineId = ToLong(line["id"]);
            var pid = ToLong(line.GetValueOrDefault("product_id"));
            var orderedQty = (int)ToLong(line.GetValueOrDefault("ordered_quantity"));
            var orderedBonus = (int)ToLong(line.GetValueOrDefault("unit_bonus"));
            var packSize = Math.Max(1, (int)ToLong(line.GetValueOrDefault("pack_size") ?? 1));

            var lineInvItems = invoiceItems.Where(ii => ToLong(ii.GetValueOrDefault("purchase_order_item_id")) == lineId).ToList();
            var receivedQty = lineInvItems.Sum(ii => (int)ToLong(ii.GetValueOrDefault("unit_quantity")));
            var receivedBonus = lineInvItems.Sum(ii => (int)ToLong(ii.GetValueOrDefault("unit_bonus")));

            var remaining = Math.Max(0, orderedQty - receivedQty);
            totalRemainingUnits += remaining;

            if (receivedQty >= orderedQty) continue;

            var bonus = Math.Max(0, orderedBonus - receivedBonus);
            var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @pid", new { pid });
            if (product == null) continue;

            var brandId = ToLong(product.GetValueOrDefault("brand_id"));
            product["brand"] = brandId > 0
                ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM brands WHERE id = @bid", new { bid = brandId })
                : null;

            var batches = await Db.HasTableAsync("batches")
                ? await Db.QueryListAsync("SELECT * FROM batches WHERE product_id = @pid ORDER BY id DESC", new { pid })
                : new List<Dictionary<string, object?>>();
            product["batches"] = batches;

            var recentBatch = batches.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.GetValueOrDefault("batch_number")?.ToString()));
            var batchNumber = recentBatch != null ? (recentBatch.GetValueOrDefault("batch_number")?.ToString() ?? "").Trim() : "";
            var batchExpiry = recentBatch != null ? FormatDate(recentBatch.GetValueOrDefault("expiry_date")) : "";

            if (string.IsNullOrEmpty(batchNumber) && await Db.HasTableAsync("purchase_invoice_items"))
            {
                var lastPiItem = await Db.QuerySingleOrDefaultAsync(
                    "SELECT batch, expiry FROM purchase_invoice_items WHERE product_id = @pid AND batch IS NOT NULL AND batch != '' ORDER BY id DESC LIMIT 1",
                    new { pid });
                if (lastPiItem != null)
                {
                    batchNumber = (lastPiItem.GetValueOrDefault("batch")?.ToString() ?? "").Trim();
                    batchExpiry = FormatDate(lastPiItem.GetValueOrDefault("expiry"));
                }
            }

            bool hasBatch = !string.IsNullOrEmpty(batchNumber);
            var unitPurchasePrice = ToDouble(line.GetValueOrDefault("unit_purchase_price"));
            var unitSalePrice = ToDouble(product.GetValueOrDefault("unit_sale_price"));
            var wholeSaleUnitPrice = ToDouble(product.GetValueOrDefault("whole_sale_unit_price"));

            receiveItems.Add(new Dictionary<string, object?>
            {
                ["purchase_order_item_id"] = lineId,
                ["product_id"] = pid,
                ["pack_size"] = packSize,
                ["unit_quantity"] = remaining,
                ["pack_quantity"] = (double)remaining / packSize,
                ["unit_purchase_price"] = unitPurchasePrice,
                ["pack_purchase_price"] = unitPurchasePrice * packSize,
                ["unit_sale_price"] = unitSalePrice,
                ["pack_sale_price"] = unitSalePrice * packSize,
                ["whole_sale_unit_price"] = wholeSaleUnitPrice,
                ["whole_sale_pack_price"] = wholeSaleUnitPrice * packSize,
                ["item_discount_percentage"] = ToDouble(line.GetValueOrDefault("item_discount_percentage")),
                ["unit_bonus"] = bonus,
                ["pack_bonus"] = (double)bonus / packSize,
                ["batch"] = hasBatch ? batchNumber : "",
                ["expiry"] = hasBatch ? batchExpiry : "",
                ["is_unbatched"] = !hasBatch,
                ["product"] = product
            });
        }

        var orderNumber = order.GetValueOrDefault("number")?.ToString();
        if (string.IsNullOrWhiteSpace(orderNumber)) orderNumber = "PO-" + id.ToString("D5");

        return Ok(new Dictionary<string, object?>
        {
            ["purchase_order_id"] = id,
            ["receipt_key"] = Guid.NewGuid().ToString(),
            ["purchase_order"] = new Dictionary<string, object?>
            {
                ["id"] = id,
                ["number"] = orderNumber,
                ["previous_receipts"] = previousReceipts,
                ["remaining_units"] = totalRemainingUnits
            },
            ["supplier_id"] = supplierId,
            ["supplier"] = supplier,
            ["posted_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["remarks"] = order.GetValueOrDefault("notes"),
            ["items"] = receiveItems
        });
    }

    [HttpGet("forecast")]
    public async Task<IActionResult> Forecast(
        [FromQuery] string? date_from = null,
        [FromQuery] string? date_to = null,
        [FromQuery] int? projected_days = null,
        [FromQuery] long? supplier_id = null,
        [FromQuery] long? brand_id = null,
        [FromQuery(Name = "brand_ids")] List<long>? brand_ids = null,
        [FromQuery(Name = "brand_ids[]")] List<long>? brand_ids_arr = null,
        [FromQuery] int safety_packs = 0,
        [FromQuery] int moq_packs = 0)
    {
        if (!await CanAsync("purchase-order.generate", "purchase-order.forecast", "purchase-order.create"))
        {
            return Forbidden();
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(date_from) || !DateTime.TryParse(date_from, out var fromDt))
        {
            errors["date_from"] = new[] { "The date from field is required." };
            fromDt = DateTime.UtcNow;
        }
        if (string.IsNullOrWhiteSpace(date_to) || !DateTime.TryParse(date_to, out var toDt))
        {
            errors["date_to"] = new[] { "The date to field is required." };
            toDt = DateTime.UtcNow;
        }
        if (!errors.ContainsKey("date_from") && !errors.ContainsKey("date_to") && toDt.Date < fromDt.Date)
        {
            errors["date_to"] = new[] { "The date to must be a date after or equal to date from." };
        }
        if (!projected_days.HasValue || projected_days.Value < 1)
        {
            errors["projected_days"] = new[] { "The projected days must be at least 1." };
        }

        if (errors.Count > 0)
        {
            return Unprocessable("The given data was invalid.", errors);
        }

        var mergedBrandIds = new List<long>();
        if (brand_id.HasValue && brand_id.Value > 0) mergedBrandIds.Add(brand_id.Value);
        if (brand_ids != null) mergedBrandIds.AddRange(brand_ids.Where(x => x > 0));
        if (brand_ids_arr != null) mergedBrandIds.AddRange(brand_ids_arr.Where(x => x > 0));
        var brandIdArray = mergedBrandIds.Distinct().ToArray();

        var fromStr = fromDt.ToString("yyyy-MM-dd");
        var toStr = toDt.ToString("yyyy-MM-dd");
        var days = Math.Max(1, (int)(toDt.Date - fromDt.Date).TotalDays + 1);
        var proj = projected_days!.Value;

        var hasProdSuppliers = await Db.HasTableAsync("product_suppliers");
        var dyn = new DynamicParameters();
        dyn.Add("fromStr", fromStr);
        dyn.Add("toStr", toStr);

        var filterWhere = "";
        if (supplier_id.HasValue && supplier_id.Value > 0)
        {
            dyn.Add("sid", supplier_id.Value);
            if (hasProdSuppliers)
            {
                filterWhere += " AND (p.supplier_id = @sid OR EXISTS (SELECT 1 FROM product_suppliers ps WHERE ps.product_id = p.id AND ps.supplier_id = @sid))";
            }
            else
            {
                filterWhere += " AND p.supplier_id = @sid";
            }
        }

        if (brandIdArray.Length > 0)
        {
            dyn.Add("brandIds", brandIdArray);
            filterWhere += " AND p.brand_id IN @brandIds";
        }

        var soldSql = $@"
            WITH srx AS (
                SELECT sri.product_id, SUM(sri.unit_return_quantity) as units_returned
                FROM sale_return_items sri
                INNER JOIN sale_returns sr ON sr.id = sri.sale_return_id
                WHERE date(sr.date) BETWEEN date(@fromStr) AND date(@toStr)
                GROUP BY sri.product_id
            ),
            sx AS (
                SELECT sii.product_id,
                       CASE WHEN (SUM(sii.quantity + COALESCE(sii.bonus, 0)) - COALESCE(srx.units_returned, 0)) > 0
                            THEN (SUM(sii.quantity + COALESCE(sii.bonus, 0)) - COALESCE(srx.units_returned, 0))
                            ELSE 0 END as units_sold,
                       MAX(si.date) as last_sold_date_in_range
                FROM sale_invoice_items sii
                INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
                LEFT JOIN srx ON srx.product_id = sii.product_id
                WHERE date(si.date) BETWEEN date(@fromStr) AND date(@toStr)
                  AND (si.status IS NULL OR si.status != 'reversed')
                GROUP BY sii.product_id, srx.units_returned
            )
            SELECT p.id as product_id, p.product_code, p.name as product_name, p.pack_size as product_pack_size,
                   p.quantity as current_stock_units, p.unit_purchase_price, p.pack_purchase_price, p.unit_sale_price,
                   p.brand_id, p.supplier_id, b.name as brand_name, s.name as supplier_name,
                   sx.units_sold, sx.last_sold_date_in_range as last_sold_date, 0 as is_user_demand
            FROM products p
            INNER JOIN sx ON p.id = sx.product_id
            LEFT JOIN brands b ON b.id = p.brand_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            WHERE sx.units_sold > 0 {filterWhere}
            ORDER BY sx.units_sold DESC";

        var soldRows = await Db.QueryListAsync(soldSql, dyn);

        var demandRows = new List<Dictionary<string, object?>>();
        var demandInfoByProduct = new Dictionary<long, Dictionary<string, object?>>();

        if (await Db.HasTableAsync("user_demands"))
        {
            var demandSql = $@"
                SELECT p.id as product_id, p.product_code, p.name as product_name, p.pack_size as product_pack_size,
                       p.quantity as current_stock_units, p.unit_purchase_price, p.pack_purchase_price, p.unit_sale_price,
                       p.brand_id, p.supplier_id, b.name as brand_name, s.name as supplier_name,
                       SUM(ud.requested_quantity) as units_sold, MAX(ud.created_at) as last_sold_date, 1 as is_user_demand
                FROM user_demands ud
                INNER JOIN products p ON p.id = ud.product_id
                LEFT JOIN brands b ON b.id = p.brand_id
                LEFT JOIN suppliers s ON s.id = p.supplier_id
                WHERE ud.status = 'pending' {filterWhere}
                GROUP BY p.id, p.product_code, p.name, p.pack_size, p.quantity,
                         p.unit_purchase_price, p.pack_purchase_price, p.unit_sale_price,
                         p.brand_id, p.supplier_id, b.name, s.name";
            demandRows = await Db.QueryListAsync(demandSql, dyn);

            var demandInfoList = await Db.QueryListAsync(@"
                SELECT ud.product_id,
                       GROUP_CONCAT(DISTINCT COALESCE(c.name, ud.requested_name)) as customer_names,
                       SUM(ud.requested_quantity) as demand_quantity
                FROM user_demands ud
                LEFT JOIN customers c ON c.id = ud.customer_id
                WHERE ud.status = 'pending'
                GROUP BY ud.product_id");
            foreach (var di in demandInfoList)
            {
                demandInfoByProduct[ToLong(di.GetValueOrDefault("product_id"))] = di;
            }
        }

        var returnedByProduct = new Dictionary<long, Dictionary<string, object?>>();
        if (await Db.HasTableAsync("purchase_returns") && await Db.HasTableAsync("purchase_return_items"))
        {
            var retRows = await Db.QueryListAsync(@"
                SELECT pri.product_id,
                       SUM(pri.return_unit_quantity) as returned_units,
                       MAX(pr.date) as last_purchase_return_date
                FROM purchase_return_items pri
                INNER JOIN purchase_returns pr ON pr.id = pri.purchase_return_id
                WHERE date(pr.date) >= date(@fromStr) AND date(pr.date) <= date(@toStr)
                GROUP BY pri.product_id",
                new { fromStr, toStr });
            foreach (var rr in retRows)
            {
                returnedByProduct[ToLong(rr.GetValueOrDefault("product_id"))] = rr;
            }
        }

        var seen = new HashSet<long>();
        var mergedRows = new List<Dictionary<string, object?>>();
        foreach (var r in soldRows)
        {
            var pid = ToLong(r.GetValueOrDefault("product_id"));
            seen.Add(pid);
            r["is_user_demand"] = demandInfoByProduct.ContainsKey(pid) ? 1 : 0;
            mergedRows.Add(r);
        }
        foreach (var r in demandRows)
        {
            var pid = ToLong(r.GetValueOrDefault("product_id"));
            if (!seen.Contains(pid))
            {
                mergedRows.Add(r);
            }
        }

        var items = new List<Dictionary<string, object?>>();
        foreach (var row in mergedRows)
        {
            var pid = ToLong(row.GetValueOrDefault("product_id"));
            var packSize = Math.Max(1, (int)ToLong(row.GetValueOrDefault("product_pack_size") ?? 1));
            var unitsSold = (int)ToLong(row.GetValueOrDefault("units_sold"));
            var dailyUnits = days > 0 ? ((double)unitsSold / days) : 0.0;
            var projUnits = dailyUnits * proj;

            var stockUnits = (int)ToLong(row.GetValueOrDefault("current_stock_units"));
            var safetyUnits = safety_packs * packSize;
            var endingUnits = stockUnits - projUnits;
            var dueUnits = Math.Max(0.0, safetyUnits - endingUnits);
            var suggested = (int)Math.Ceiling(dueUnits / packSize);

            if (suggested > 0 && moq_packs > 0)
            {
                suggested = Math.Max(suggested, moq_packs);
            }

            var ppu = ToDouble(row.GetValueOrDefault("unit_purchase_price"));
            var ppp = ToDouble(row.GetValueOrDefault("pack_purchase_price"));
            var packPrice = ppp > 0 ? ppp : (ppu > 0 ? ppu * packSize : 0.0);

            demandInfoByProduct.TryGetValue(pid, out var demandInfo);
            returnedByProduct.TryGetValue(pid, out var returnInfo);
            bool isUserDemand = ToLong(row.GetValueOrDefault("is_user_demand")) == 1 || demandInfo != null;

            items.Add(new Dictionary<string, object?>
            {
                ["product_id"] = pid,
                ["product_code"] = row.GetValueOrDefault("product_code"),
                ["product_name"] = row.GetValueOrDefault("product_name"),
                ["brand_id"] = row.GetValueOrDefault("brand_id"),
                ["brand_name"] = row.GetValueOrDefault("brand_name"),
                ["supplier_id"] = row.GetValueOrDefault("supplier_id"),
                ["supplier_name"] = row.GetValueOrDefault("supplier_name"),
                ["pack_size"] = packSize,
                ["units_sold"] = unitsSold,
                ["current_stock_units"] = stockUnits,
                ["projected_days"] = proj,
                ["suggested_packs"] = suggested,
                ["suggested_units"] = suggested * packSize,
                ["pack_price"] = Math.Round(packPrice, 2),
                ["pack_purchase_price"] = ppp,
                ["last_sold_date"] = row.GetValueOrDefault("last_sold_date"),
                ["unit_purchase_price"] = ppu,
                ["unit_sale_price"] = row.GetValueOrDefault("unit_sale_price"),
                ["is_user_demand"] = isUserDemand ? 1 : 0,
                ["user_demand_customers"] = demandInfo?.GetValueOrDefault("customer_names"),
                ["user_demand_quantity"] = demandInfo != null ? (int)ToLong(demandInfo.GetValueOrDefault("demand_quantity")) : 0,
                ["has_purchase_return"] = returnInfo != null ? 1 : 0,
                ["purchase_return_units"] = returnInfo != null ? (int)ToLong(returnInfo.GetValueOrDefault("returned_units")) : 0,
                ["last_purchase_return_date"] = returnInfo != null ? FormatDate(returnInfo.GetValueOrDefault("last_purchase_return_date")) : null,
                ["policy"] = new Dictionary<string, object?>
                {
                    ["daily_units"] = Math.Round(dailyUnits, 4),
                    ["proj_units"] = Math.Round(projUnits, 2),
                    ["ending_units"] = Math.Round(endingUnits, 2),
                    ["safety_units"] = safetyUnits,
                    ["due_units"] = Math.Round(dueUnits, 2),
                    ["moq_packs"] = moq_packs
                }
            });
        }

        return Ok(new Dictionary<string, object?>
        {
            ["meta"] = new Dictionary<string, object?>
            {
                ["date_from"] = fromStr,
                ["date_to"] = toStr,
                ["days"] = days,
                ["projected_days"] = proj,
                ["filter"] = new Dictionary<string, object?>
                {
                    ["supplier_id"] = supplier_id,
                    ["brand_id"] = brand_id,
                    ["brand_ids"] = brandIdArray.Length > 0 ? brandIdArray : null,
                    ["safety_packs"] = safety_packs,
                    ["moq_packs"] = moq_packs
                }
            },
            ["items"] = items
        });
    }

    private async Task<Dictionary<string, object?>?> LoadPurchaseOrderAsync(long id)
    {
        var order = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_orders WHERE id = @id", new { id });
        if (order == null) return null;

        if (order.GetValueOrDefault("number") == null || string.IsNullOrWhiteSpace(order["number"]?.ToString()))
        {
            order["number"] = "PO-" + id.ToString("D5");
        }

        var supplierId = ToLong(order.GetValueOrDefault("supplier_id"));
        order["supplier"] = supplierId > 0
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @sid", new { sid = supplierId })
            : null;

        var items = await Db.QueryListAsync("SELECT * FROM purchase_order_items WHERE purchase_order_id = @id", new { id });
        var invoiceItems = await Db.HasTableAsync("purchase_invoice_items") && items.Count > 0
            ? await Db.QueryListAsync(
                @"SELECT pii.* FROM purchase_invoice_items pii
                  INNER JOIN purchase_invoices pi ON pi.id = pii.purchase_invoice_id
                  WHERE pii.purchase_order_item_id IN @ids
                    AND (pi.status IS NULL OR pi.status != 'reversed')",
                new { ids = items.Select(x => ToLong(x["id"])).ToArray() })
            : new List<Dictionary<string, object?>>();

        foreach (var item in items)
        {
            var lineId = ToLong(item["id"]);
            var pid = ToLong(item.GetValueOrDefault("product_id"));
            var orderedQty = (int)ToLong(item.GetValueOrDefault("ordered_quantity"));
            var receivedQty = invoiceItems
                .Where(ii => ToLong(ii.GetValueOrDefault("purchase_order_item_id")) == lineId)
                .Sum(ii => (int)ToLong(ii.GetValueOrDefault("unit_quantity")));

            item["received_quantity"] = receivedQty;
            item["remaining_quantity"] = Math.Max(0, orderedQty - receivedQty);

            var prod = pid > 0
                ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @pid", new { pid })
                : null;
            if (prod != null)
            {
                var bid = ToLong(prod.GetValueOrDefault("brand_id"));
                prod["brand"] = bid > 0
                    ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM brands WHERE id = @bid", new { bid })
                    : null;
            }
            item["product"] = prod;
        }

        var sortedItems = await _sortingService.SortAsync(supplierId, items, line => ToLong(line.GetValueOrDefault("product_id")));
        order["items"] = sortedItems;

        order["invoices"] = await Db.HasTableAsync("purchase_invoices")
            ? await Db.QueryListAsync("SELECT * FROM purchase_invoices WHERE purchase_order_id = @id", new { id })
            : new List<Dictionary<string, object?>>();

        return order;
    }

    private async Task<(bool Valid, IActionResult? ErrorResult, Dictionary<string, object?>? Header, List<Dictionary<string, object?>>? Items)> ValidateOrderPayloadAsync(
        Dictionary<string, JsonElement> body)
    {
        var supplierId = GetNullableLong(body, "supplier_id") ?? 0;
        var date = GetString(body, "date")?.Trim() ?? "";
        var expectedDate = GetString(body, "expected_received_date")?.Trim() ?? "";
        var reference = GetString(body, "reference");
        var notes = GetString(body, "notes");

        var errors = new Dictionary<string, string[]>();
        if (supplierId <= 0)
        {
            errors["supplier_id"] = new[] { "Please select a supplier before saving the Purchase Order." };
        }
        if (string.IsNullOrEmpty(date))
        {
            errors["date"] = new[] { "The date field is required." };
        }
        if (string.IsNullOrEmpty(expectedDate))
        {
            errors["expected_received_date"] = new[] { "The expected received date field is required." };
        }
        else if (!string.IsNullOrEmpty(date) && DateTime.TryParse(date, out var orderDt) && DateTime.TryParse(expectedDate, out var expDt) && expDt.Date < orderDt.Date)
        {
            errors["expected_received_date"] = new[] { "The expected received date must be on or after the order date." };
        }

        var items = new List<Dictionary<string, object?>>();
        var seenProducts = new HashSet<long>();

        if (!body.TryGetValue("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array || itemsEl.GetArrayLength() == 0)
        {
            errors["items"] = new[] { "At least one item is required." };
        }
        else
        {
            int idx = 0;
            foreach (var itemEl in itemsEl.EnumerateArray())
            {
                if (itemEl.ValueKind == JsonValueKind.Object)
                {
                    var d = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in itemEl.EnumerateObject()) d[p.Name] = p.Value;

                    var pid = GetNullableLong(d, "product_id") ?? 0;
                    if (pid <= 0)
                    {
                        errors[$"items.{idx}.product_id"] = new[] { "Product is required." };
                    }
                    else if (!seenProducts.Add(pid))
                    {
                        errors[$"items.{idx}.product_id"] = new[] { "Duplicate product in Purchase Order." };
                    }
                    else
                    {
                        var prod = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = pid });
                        if (prod == null)
                        {
                            errors[$"items.{idx}.product_id"] = new[] { $"Product #{pid} not found." };
                        }
                        else
                        {
                            var prodPackSize = Math.Max(1, (int)ToLong(prod.GetValueOrDefault("pack_size") ?? 1));
                            var reqPackSize = GetInt(d, "pack_size", prodPackSize);
                            if (reqPackSize != prodPackSize)
                            {
                                errors[$"items.{idx}.pack_size"] = new[] { "Pack size must match the selected product." };
                            }

                            var orderedPacks = d.ContainsKey("ordered_packs") ? GetNullableLong(d, "ordered_packs") : null;
                            var orderedQty = orderedPacks.HasValue
                                ? orderedPacks.Value * prodPackSize
                                : (GetNullableLong(d, "ordered_quantity") ?? 0);

                            if (orderedQty < 1 || orderedQty > int.MaxValue || orderedQty % prodPackSize != 0)
                            {
                                errors[$"items.{idx}.ordered_packs"] = new[] { "Order a positive whole number of packs." };
                            }

                            double unitPurchasePrice;
                            if (d.ContainsKey("pack_purchase_price") && d["pack_purchase_price"].ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                            {
                                unitPurchasePrice = (double)GetDecimal(d, "pack_purchase_price", 0m) / prodPackSize;
                            }
                            else
                            {
                                unitPurchasePrice = (double)GetDecimal(d, "unit_purchase_price", 0m);
                            }

                            var unitBonus = GetInt(d, "unit_bonus", 0);
                            var discPct = (double)GetDecimal(d, "item_discount_percentage", 0m);

                            items.Add(new Dictionary<string, object?>
                            {
                                ["product_id"] = pid,
                                ["pack_size"] = prodPackSize,
                                ["ordered_quantity"] = (int)orderedQty,
                                ["unit_purchase_price"] = unitPurchasePrice,
                                ["unit_bonus"] = Math.Max(0, unitBonus),
                                ["item_discount_percentage"] = Math.Max(0.0, Math.Min(100.0, discPct))
                            });
                        }
                    }
                }
                idx++;
            }
        }

        if (errors.Count > 0)
        {
            return (false, Unprocessable("The given data was invalid.", errors), null, null);
        }

        var header = new Dictionary<string, object?>
        {
            ["supplier_id"] = supplierId,
            ["date"] = date,
            ["expected_received_date"] = expectedDate,
            ["reference"] = reference,
            ["notes"] = notes
        };

        return (true, null, header, items);
    }

    private static string FormatDate(object? val)
    {
        var s = (val?.ToString() ?? "").Trim();
        if (string.IsNullOrEmpty(s)) return "";
        if (DateTime.TryParse(s, out var dt)) return dt.ToString("yyyy-MM-dd");
        return s.Length > 10 ? s.Substring(0, 10) : s;
    }

    private static long ToLong(object? val)
    {
        if (val == null) return 0;
        if (val is long l) return l;
        if (val is int i) return i;
        return long.TryParse(val.ToString(), out var p) ? p : 0;
    }

    private static double ToDouble(object? val)
    {
        if (val == null) return 0.0;
        if (val is double d) return d;
        if (val is decimal dec) return (double)dec;
        if (val is float f) return f;
        if (val is long l) return l;
        if (val is int i) return i;
        return double.TryParse(val.ToString(), out var p) ? p : 0.0;
    }
}

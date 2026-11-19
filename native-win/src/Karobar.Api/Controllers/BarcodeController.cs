using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/barcodes")]
[Authorize]
public class BarcodeController : ApiControllerBase
{
    public BarcodeController(KarobarDb db) : base(db)
    {
    }

    private static void NormalizeTemplateRow(Dictionary<string, object?> row)
    {
        if (row.TryGetValue("layout_options", out var lo) && lo is string str && !string.IsNullOrWhiteSpace(str))
        {
            try
            {
                row["layout_options"] = JsonSerializer.Deserialize<object>(str);
            }
            catch
            {
            }
        }
    }

    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates()
    {
        if (!await CanAsync("barcode.view", "barcode.print")) return Forbidden();

        var templates = await Db.HasTableAsync("barcode_templates")
            ? await Db.QueryListAsync("SELECT * FROM barcode_templates ORDER BY is_default DESC, id ASC")
            : new List<Dictionary<string, object?>>();

        foreach (var t in templates) NormalizeTemplateRow(t);

        var setting = await Db.HasTableAsync("settings")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
            : null;

        return Ok(new Dictionary<string, object?>
        {
            ["templates"] = templates,
            ["store_info"] = new Dictionary<string, object?>
            {
                ["store_name"] = setting?["store_name"] ?? "Karobar Pharmacy",
                ["phone_number"] = setting?["phone_number"] ?? "",
                ["currency_symbol"] = setting?["currency_symbol"] ?? "Rs.",
                ["currency_position"] = setting?["currency_position"] ?? "before",
                ["currency_decimal_places"] = Convert.ToInt32(setting?["currency_decimal_places"] ?? 2)
            }
        });
    }

    [HttpPost("templates")]
    public async Task<IActionResult> SaveTemplate()
    {
        if (!await CanAsync("barcode.create", "barcode.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";
        var paperType = GetString(body, "paper_type")?.Trim() ?? "";
        var orientation = GetString(body, "orientation")?.Trim() ?? "";
        var widthMm = GetDecimal(body, "width_mm", 0m);
        var heightMm = GetDecimal(body, "height_mm", 0m);

        if (string.IsNullOrEmpty(name)) return Unprocessable("The name field is required.");
        if (paperType != "roll" && paperType != "sheet") return Unprocessable("Invalid paper_type.");
        if (orientation != "landscape" && orientation != "portrait") return Unprocessable("Invalid orientation.");
        if (widthMm < 15 || widthMm > 300 || heightMm < 10 || heightMm > 300) return Unprocessable("Invalid dimensions.");

        var isDefault = GetBool(body, "is_default", false);
        if (isDefault)
        {
            await Db.ExecuteAsync("UPDATE barcode_templates SET is_default = 0 WHERE is_default = 1");
        }

        var data = BuildTemplateData(body);
        data["name"] = name;
        data["paper_type"] = paperType;
        data["orientation"] = orientation;
        data["width_mm"] = (double)widthMm;
        data["height_mm"] = (double)heightMm;
        data["is_default"] = isDefault;

        var id = await Db.InsertAsync("barcode_templates", data);
        var template = await Db.QuerySingleOrDefaultAsync("SELECT * FROM barcode_templates WHERE id = @id", new { id });
        if (template != null) NormalizeTemplateRow(template);

        return StatusCode(201, new
        {
            message = "Template saved successfully",
            template
        });
    }

    [HttpPut("templates/{id:long}")]
    public async Task<IActionResult> UpdateTemplate(long id)
    {
        if (!await CanAsync("barcode.update")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM barcode_templates WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Template not found." });

        var body = await ReadJsonBodyAsync();
        var data = BuildTemplateData(body);

        if (body.ContainsKey("name")) data["name"] = GetString(body, "name")?.Trim();
        if (body.ContainsKey("paper_type")) data["paper_type"] = GetString(body, "paper_type")?.Trim();
        if (body.ContainsKey("orientation")) data["orientation"] = GetString(body, "orientation")?.Trim();
        if (body.ContainsKey("width_mm")) data["width_mm"] = (double)GetDecimal(body, "width_mm");
        if (body.ContainsKey("height_mm")) data["height_mm"] = (double)GetDecimal(body, "height_mm");

        if (body.ContainsKey("is_default"))
        {
            var isDefault = GetBool(body, "is_default", false);
            data["is_default"] = isDefault;
            if (isDefault)
            {
                await Db.ExecuteAsync("UPDATE barcode_templates SET is_default = 0 WHERE id != @id AND is_default = 1", new { id });
            }
        }

        await Db.UpdateAsync("barcode_templates", id, data);
        var template = await Db.QuerySingleOrDefaultAsync("SELECT * FROM barcode_templates WHERE id = @id", new { id });
        if (template != null) NormalizeTemplateRow(template);

        return Ok(new
        {
            message = "Template updated successfully",
            template
        });
    }

    [HttpDelete("templates/{id:long}")]
    public async Task<IActionResult> DeleteTemplate(long id)
    {
        if (!await CanAsync("barcode.delete")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM barcode_templates WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Template not found." });

        var count = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM barcode_templates");
        if (count <= 1)
        {
            return Unprocessable("Cannot delete the only remaining template");
        }

        var wasDefault = existing["is_default"] is true;
        await Db.ExecuteAsync("DELETE FROM barcode_templates WHERE id = @id", new { id });

        if (wasDefault)
        {
            var firstId = await Db.ExecuteScalarAsync<long?>("SELECT id FROM barcode_templates ORDER BY id ASC LIMIT 1");
            if (firstId.HasValue)
            {
                await Db.UpdateAsync("barcode_templates", firstId.Value, new Dictionary<string, object?> { ["is_default"] = true });
            }
        }

        return Ok(new { message = "Template deleted successfully" });
    }

    [HttpGet("products-search")]
    public async Task<IActionResult> SearchProducts(
        [FromQuery] string? search = "",
        [FromQuery(Name = "category_id")] long? categoryId = null)
    {
        if (!await CanAsync("barcode.view", "barcode.print")) return Forbidden();

        var q = (search ?? "").Trim();
        var idList = new List<long>();
        foreach (var rawVal in Request.Query["product_ids"].Concat(Request.Query["product_ids[]"]))
        {
            if (string.IsNullOrWhiteSpace(rawVal)) continue;
            foreach (var part in rawVal.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part.Trim(), out var parsed) && parsed > 0) idList.Add(parsed);
            }
        }

        var param = new DynamicParameters();
        var where = "";
        if (idList.Count > 0)
        {
            where = "WHERE id IN @ids";
            param.Add("ids", idList.Distinct().ToArray());
        }
        else if (!string.IsNullOrEmpty(q))
        {
            where = "WHERE (name LIKE @q OR barcode LIKE @q OR product_code LIKE @q OR formulation LIKE @q OR rack LIKE @q)";
            param.Add("q", $"%{q}%");
        }
        else if (categoryId.HasValue)
        {
            where = "WHERE category_id = @catId";
            param.Add("catId", categoryId.Value);
        }

        var products = await Db.QueryListAsync($@"
            SELECT id, product_code, name, formulation, rack, barcode,
                   unit_sale_price, whole_sale_unit_price, quantity, pack_size, category_id, brand_id
            FROM products
            {where}
            LIMIT 50", param);

        await AttachProductBatchesAsync(products);

        return Ok(new { products });
    }

    [HttpGet("purchase-orders")]
    public async Task<IActionResult> GetPurchaseOrders(
        [FromQuery] long? id = null,
        [FromQuery(Name = "supplier_id")] long? supplierId = null,
        [FromQuery(Name = "brand_id")] long? brandId = null,
        [FromQuery] string? search = "",
        [FromQuery] int limit = 50)
    {
        if (!await CanAsync("barcode.view", "barcode.print")) return Forbidden();
        if (!await Db.HasTableAsync("purchase_orders")) return Ok(new { purchase_orders = Array.Empty<object>() });

        var canViewCosts = await CanAsync("purchase-order.view");
        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);
        var q = (search ?? "").Trim();

        if (id.HasValue && id.Value > 0)
        {
            var po = await Db.QuerySingleOrDefaultAsync(@"
                SELECT po.*,
                       s.name AS __supp_name,
                       (SELECT COUNT(1) FROM purchase_order_items poi WHERE poi.purchase_order_id = po.id) AS items_count,
                       (SELECT COALESCE(SUM(poi.ordered_quantity), 0) FROM purchase_order_items poi WHERE poi.purchase_order_id = po.id) AS items_sum_ordered_quantity
                FROM purchase_orders po
                LEFT JOIN suppliers s ON s.id = po.supplier_id
                WHERE po.id = @id", new { id = id.Value });

            if (po == null) return NotFound(new { message = "Purchase order not found" });

            await EnrichPurchaseOrdersAsync(new List<Dictionary<string, object?>> { po }, canViewCosts);
            return Ok(new { purchase_order = po });
        }

        var where = new List<string>();
        var param = new DynamicParameters();
        if (supplierId.HasValue) { where.Add("po.supplier_id = @supplierId"); param.Add("supplierId", supplierId.Value); }
        if (brandId.HasValue)
        {
            where.Add(@"EXISTS (
                SELECT 1 FROM purchase_order_items poi
                INNER JOIN products p ON p.id = poi.product_id
                WHERE poi.purchase_order_id = po.id AND p.brand_id = @brandId)");
            param.Add("brandId", brandId.Value);
        }
        if (!string.IsNullOrEmpty(q))
        {
            where.Add("(po.reference LIKE @q OR CAST(po.id AS TEXT) LIKE @q OR s.name LIKE @q)");
            param.Add("q", $"%{q}%");
        }
        param.Add("limit", limit);

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var orders = await Db.QueryListAsync($@"
            SELECT po.*,
                   s.name AS __supp_name,
                   (SELECT COUNT(1) FROM purchase_order_items poi WHERE poi.purchase_order_id = po.id) AS items_count,
                   (SELECT COALESCE(SUM(poi.ordered_quantity), 0) FROM purchase_order_items poi WHERE poi.purchase_order_id = po.id) AS items_sum_ordered_quantity
            FROM purchase_orders po
            LEFT JOIN suppliers s ON s.id = po.supplier_id
            {whereClause}
            ORDER BY po.id DESC
            LIMIT @limit", param);

        await EnrichPurchaseOrdersAsync(orders, canViewCosts);
        return Ok(new { purchase_orders = orders });
    }

    [HttpGet("purchase-invoices")]
    public async Task<IActionResult> GetPurchaseInvoices(
        [FromQuery] long? id = null,
        [FromQuery(Name = "supplier_id")] long? supplierId = null,
        [FromQuery(Name = "brand_id")] long? brandId = null,
        [FromQuery] string? search = "",
        [FromQuery] int limit = 50)
    {
        if (!await CanAsync("barcode.view", "barcode.print")) return Forbidden();
        if (!await Db.HasTableAsync("purchase_invoices")) return Ok(new { purchase_invoices = Array.Empty<object>() });

        var canViewCosts = await CanAsync("purchase-invoice.view");
        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 100);
        var q = (search ?? "").Trim();
        var hasStatus = await Db.HasColumnAsync("purchase_invoices", "status");

        if (id.HasValue && id.Value > 0)
        {
            var statusCond = hasStatus ? "AND (pi.status IS NULL OR pi.status != 'reversed')" : "";
            var pi = await Db.QuerySingleOrDefaultAsync($@"
                SELECT pi.*,
                       s.name AS __supp_name,
                       (SELECT COUNT(1) FROM purchase_invoice_items pii WHERE pii.purchase_invoice_id = pi.id) AS items_count,
                       (SELECT COALESCE(SUM(pii.quantity), 0) FROM purchase_invoice_items pii WHERE pii.purchase_invoice_id = pi.id) AS items_sum_quantity
                FROM purchase_invoices pi
                LEFT JOIN suppliers s ON s.id = pi.supplier_id
                WHERE pi.id = @id {statusCond}", new { id = id.Value });

            if (pi == null) return NotFound(new { message = "Purchase invoice not found" });

            await EnrichPurchaseInvoicesAsync(new List<Dictionary<string, object?>> { pi }, canViewCosts);
            return Ok(new { purchase_invoice = pi });
        }

        var where = new List<string>();
        if (hasStatus) where.Add("(pi.status IS NULL OR pi.status != 'reversed')");
        var param = new DynamicParameters();

        if (supplierId.HasValue) { where.Add("pi.supplier_id = @supplierId"); param.Add("supplierId", supplierId.Value); }
        if (brandId.HasValue)
        {
            where.Add(@"EXISTS (
                SELECT 1 FROM purchase_invoice_items pii
                INNER JOIN products p ON p.id = pii.product_id
                WHERE pii.purchase_invoice_id = pi.id AND p.brand_id = @brandId)");
            param.Add("brandId", brandId.Value);
        }
        if (!string.IsNullOrEmpty(q))
        {
            where.Add("(pi.posted_number LIKE @q OR pi.invoice_number LIKE @q OR s.name LIKE @q)");
            param.Add("q", $"%{q}%");
        }
        param.Add("limit", limit);

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var invoices = await Db.QueryListAsync($@"
            SELECT pi.*,
                   s.name AS __supp_name,
                   (SELECT COUNT(1) FROM purchase_invoice_items pii WHERE pii.purchase_invoice_id = pi.id) AS items_count,
                   (SELECT COALESCE(SUM(pii.quantity), 0) FROM purchase_invoice_items pii WHERE pii.purchase_invoice_id = pi.id) AS items_sum_quantity
            FROM purchase_invoices pi
            LEFT JOIN suppliers s ON s.id = pi.supplier_id
            {whereClause}
            ORDER BY pi.id DESC
            LIMIT @limit", param);

        await EnrichPurchaseInvoicesAsync(invoices, canViewCosts);
        return Ok(new { purchase_invoices = invoices });
    }

    [HttpGet("scan")]
    public async Task<IActionResult> ScanProduct(
        [FromQuery] string? barcode = null,
        [FromQuery] string? code = null,
        [FromQuery(Name = "product_id")] long? productId = null)
    {
        if (!await CanAsync("barcode-pos.view", "barcode-pos.create")) return Forbidden();

        var searchCode = (barcode ?? code ?? "").Trim();
        if ((!productId.HasValue || productId.Value <= 0) && string.IsNullOrEmpty(searchCode))
        {
            return StatusCode(422, new
            {
                found = false,
                message = "Please provide a barcode, product code, or product ID"
            });
        }

        bool isWeightBarcode = false;
        double parsedQuantity = 1;
        int? parsedWeightGrams = null;
        Dictionary<string, object?>? product = null;

        if (productId.HasValue && productId.Value > 0)
        {
            product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = productId.Value });
        }
        else
        {
            product = await Db.QuerySingleOrDefaultAsync(
                "SELECT * FROM products WHERE barcode = @code OR product_code = @code LIMIT 1",
                new { code = searchCode });

            if (product == null)
            {
                var m = Regex.Match(searchCode, @"^(2[0-9])(\d{5})(\d{5})(\d)$");
                if (m.Success)
                {
                    var prefix = m.Groups[1].Value;
                    var plu = m.Groups[2].Value;
                    var weightRaw = int.Parse(m.Groups[3].Value);
                    var pluStripped = plu.TrimStart('0');
                    if (string.IsNullOrEmpty(pluStripped)) pluStripped = "0";

                    var candidates = new[]
                    {
                        plu,
                        pluStripped,
                        "PRD-" + pluStripped.PadLeft(4, '0'),
                        "PRD-" + plu,
                        prefix + plu
                    }.Distinct().ToArray();

                    product = await Db.QuerySingleOrDefaultAsync(
                        "SELECT * FROM products WHERE barcode IN @candidates OR product_code IN @candidates LIMIT 1",
                        new { candidates });

                    if (product != null)
                    {
                        isWeightBarcode = true;
                        parsedWeightGrams = weightRaw;
                        parsedQuantity = weightRaw > 0 ? Math.Round(weightRaw / 1000.0, 3) : 1;
                    }
                }
            }
        }

        if (product == null)
        {
            var identifier = productId.HasValue && productId.Value > 0 ? $"ID '{productId.Value}'" : $"barcode '{searchCode}'";
            return NotFound(new
            {
                found = false,
                barcode = searchCode,
                message = $"Product with {identifier} not found"
            });
        }

        var pid = Convert.ToInt64(product["id"]);
        var brandId = product["brand_id"] != null ? Convert.ToInt64(product["brand_id"]) : 0;
        var catId = product["category_id"] != null ? Convert.ToInt64(product["category_id"]) : 0;

        var brandObj = brandId > 0 ? await Db.QuerySingleOrDefaultAsync("SELECT id, name FROM brands WHERE id = @id", new { id = brandId }) : null;
        var catObj = catId > 0 ? await Db.QuerySingleOrDefaultAsync("SELECT id, name FROM categories WHERE id = @id", new { id = catId }) : null;

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var batches = await Db.HasTableAsync("batches")
            ? await Db.QueryListAsync($@"
                SELECT * FROM batches
                WHERE product_id = @pid
                ORDER BY CASE WHEN expiry_date IS NOT NULL AND expiry_date >= '{today}' THEN 0 ELSE 1 END ASC,
                         expiry_date ASC,
                         id ASC", new { pid })
            : new List<Dictionary<string, object?>>();

        foreach (var b in batches) BatchController.EnrichBatchModelAttributes(b, product);

        var defaultUnitSalePrice = Convert.ToDouble(product["unit_sale_price"] ?? 0.0);
        var defaultPackSalePrice = Convert.ToDouble(product["pack_sale_price"] ?? 0.0);
        var defaultWsUnitPrice = Convert.ToDouble(product["whole_sale_unit_price"] ?? 0.0);
        var defaultWsPackPrice = Convert.ToDouble(product["whole_sale_pack_price"] ?? 0.0);
        var defaultUnitCost = Convert.ToDouble(product["unit_purchase_price"] ?? 0.0);

        Dictionary<string, object?> FormatBatchPayload(Dictionary<string, object?> b, bool isFefo)
        {
            double PickPrice(string k, double fallback) =>
                b.TryGetValue(k, out var v) && v != null && Convert.ToDouble(v) > 0 ? Convert.ToDouble(v) : fallback;

            return new Dictionary<string, object?>
            {
                ["id"] = b["id"],
                ["batch_number"] = b["batch_number"],
                ["expiry_date"] = b["expiry_date"],
                ["quantity"] = Convert.ToInt32(b["quantity"] ?? 0),
                ["is_expired"] = b["is_expired"] is true,
                ["is_fefo"] = isFefo,
                ["unit_sale_price"] = PickPrice("unit_sale_price", defaultUnitSalePrice),
                ["pack_sale_price"] = PickPrice("pack_sale_price", defaultPackSalePrice),
                ["whole_sale_unit_price"] = PickPrice("whole_sale_unit_price", defaultWsUnitPrice),
                ["whole_sale_pack_price"] = PickPrice("whole_sale_pack_price", defaultWsPackPrice),
                ["unit_purchase_price"] = PickPrice("unit_purchase_price", defaultUnitCost)
            };
        }

        var fefoBatch = batches.FirstOrDefault(b =>
        {
            var exp = b["expiry_date"]?.ToString();
            var isNotExpired = string.IsNullOrEmpty(exp) || string.CompareOrdinal(exp, today) >= 0;
            return isNotExpired && Convert.ToInt32(b["quantity"] ?? 0) > 0;
        });

        var totalProductQty = Convert.ToInt32(product["quantity"] ?? 0);
        var totalBatchedQty = batches.Sum(b => Convert.ToInt32(b["quantity"] ?? 0));
        var unbatchedQty = Math.Max(0, totalProductQty - totalBatchedQty);

        Dictionary<string, object?>? selectedBatch = null;
        if (fefoBatch != null)
        {
            selectedBatch = FormatBatchPayload(fefoBatch, true);
        }
        else if (unbatchedQty > 0 || batches.Count == 0)
        {
            selectedBatch = new Dictionary<string, object?>
            {
                ["id"] = null,
                ["batch_number"] = "",
                ["expiry_date"] = null,
                ["quantity"] = unbatchedQty,
                ["is_expired"] = false,
                ["is_unbatched"] = true,
                ["unit_sale_price"] = defaultUnitSalePrice,
                ["pack_sale_price"] = defaultPackSalePrice,
                ["whole_sale_unit_price"] = defaultWsUnitPrice,
                ["whole_sale_pack_price"] = defaultWsPackPrice,
                ["unit_purchase_price"] = defaultUnitCost
            };
        }
        else if (batches.Count > 0)
        {
            selectedBatch = FormatBatchPayload(batches[0], false);
        }

        var effectiveUnitSalePrice = selectedBatch != null && selectedBatch.TryGetValue("unit_sale_price", out var sbUsp) && sbUsp != null && Convert.ToDouble(sbUsp) > 0
            ? Convert.ToDouble(sbUsp)
            : defaultUnitSalePrice;

        var fefoId = fefoBatch != null ? Convert.ToInt64(fefoBatch["id"] ?? 0) : -1;

        return Ok(new Dictionary<string, object?>
        {
            ["found"] = true,
            ["is_weight_barcode"] = isWeightBarcode,
            ["parsed_quantity"] = parsedQuantity,
            ["weight_grams"] = parsedWeightGrams,
            ["product"] = new Dictionary<string, object?>
            {
                ["id"] = pid,
                ["name"] = product["name"],
                ["product_code"] = product["product_code"],
                ["barcode"] = product["barcode"],
                ["formulation"] = product["formulation"],
                ["rack"] = product["rack"],
                ["pack_size"] = Convert.ToInt32(product["pack_size"] ?? 1),
                ["unit_sale_price"] = effectiveUnitSalePrice,
                ["product_unit_sale_price"] = defaultUnitSalePrice,
                ["whole_sale_unit_price"] = defaultWsUnitPrice,
                ["quantity"] = totalProductQty,
                ["max_discount"] = Convert.ToDouble(product["max_discount"] ?? 100.0),
                ["is_narcotic"] = product["narcotic"]?.ToString() == "yes" || (product.TryGetValue("is_narcotic", out var inarc) && inarc is true),
                ["brand"] = brandObj,
                ["category"] = catObj,
                ["unassigned_quantity"] = unbatchedQty,
                ["is_weight_barcode"] = isWeightBarcode,
                ["parsed_quantity"] = parsedQuantity,
                ["weight_grams"] = parsedWeightGrams,
                ["selected_batch"] = selectedBatch,
                ["batches"] = batches.Select(b => FormatBatchPayload(b, fefoId > 0 && Convert.ToInt64(b["id"] ?? 0) == fefoId)).ToList()
            }
        });
    }

    // -------------------------------------------------------------------------
    // Internal Helpers
    // -------------------------------------------------------------------------

    private static Dictionary<string, object?> BuildTemplateData(Dictionary<string, JsonElement> body)
    {
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var intKey in new[] { "columns", "rows" })
        {
            if (body.ContainsKey(intKey))
            {
                var raw = GetString(body, intKey);
                data[intKey] = !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var iv) && iv > 0 ? iv : 1;
            }
        }

        foreach (var numKey in new[] { "gap_x_mm", "gap_y_mm", "margin_top_mm", "margin_left_mm", "margin_right_mm", "margin_bottom_mm" })
        {
            if (body.ContainsKey(numKey))
            {
                var raw = GetString(body, numKey);
                data[numKey] = !string.IsNullOrWhiteSpace(raw) && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0d;
            }
        }

        if (body.TryGetValue("layout_options", out var loEl))
        {
            data["layout_options"] = loEl.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                ? null
                : loEl.GetRawText();
        }

        return data;
    }

    private async Task AttachProductBatchesAsync(List<Dictionary<string, object?>> products)
    {
        if (products.Count == 0) return;

        var prodIds = products.Select(p => Convert.ToInt64(p["id"] ?? 0)).Where(id => id > 0).Distinct().ToArray();
        var batchesByProd = new Dictionary<long, List<Dictionary<string, object?>>>();

        if (prodIds.Length > 0 && await Db.HasTableAsync("batches"))
        {
            var bRows = await Db.QueryListAsync(
                "SELECT * FROM batches WHERE product_id IN @prodIds ORDER BY expiry_date ASC",
                new { prodIds });

            foreach (var b in bRows)
            {
                BatchController.EnrichBatchModelAttributes(b);
                var pid = Convert.ToInt64(b["product_id"] ?? 0);
                if (!batchesByProd.TryGetValue(pid, out var list))
                {
                    list = new List<Dictionary<string, object?>>();
                    batchesByProd[pid] = list;
                }
                list.Add(b);
            }
        }

        foreach (var p in products)
        {
            var pid = Convert.ToInt64(p["id"] ?? 0);
            p["batches"] = batchesByProd.TryGetValue(pid, out var list) ? list : new List<Dictionary<string, object?>>();
        }
    }

    private async Task EnrichPurchaseOrdersAsync(List<Dictionary<string, object?>> orders, bool canViewCosts)
    {
        if (orders.Count == 0) return;

        var poIds = orders.Select(o => Convert.ToInt64(o["id"] ?? 0)).Where(id => id > 0).ToArray();
        var items = await Db.HasTableAsync("purchase_order_items") && poIds.Length > 0
            ? await Db.QueryListAsync("SELECT * FROM purchase_order_items WHERE purchase_order_id IN @poIds ORDER BY id ASC", new { poIds })
            : new List<Dictionary<string, object?>>();

        var prodIds = items.Select(it => Convert.ToInt64(it["product_id"] ?? 0)).Where(id => id > 0).Distinct().ToArray();
        var prodMap = await LoadProductsWithBrandsSuppliersBatchesAsync(prodIds);

        var itemsByPo = new Dictionary<long, List<Dictionary<string, object?>>>();
        foreach (var it in items)
        {
            var poid = Convert.ToInt64(it["purchase_order_id"] ?? 0);
            var pid = Convert.ToInt64(it["product_id"] ?? 0);
            it["product"] = prodMap.TryGetValue(pid, out var prodObj) ? prodObj : null;

            if (!canViewCosts)
            {
                foreach (var hideKey in new[] { "unit_price", "pack_purchase_price", "unit_purchase_price", "line_total", "total" })
                {
                    it.Remove(hideKey);
                }
            }

            if (!itemsByPo.TryGetValue(poid, out var list))
            {
                list = new List<Dictionary<string, object?>>();
                itemsByPo[poid] = list;
            }
            list.Add(it);
        }

        foreach (var po in orders)
        {
            var poid = Convert.ToInt64(po["id"] ?? 0);
            po["supplier"] = po["supplier_id"] != null ? new Dictionary<string, object?>
            {
                ["id"] = po["supplier_id"],
                ["name"] = po["__supp_name"]
            } : null;
            po.Remove("__supp_name");

            po["items"] = itemsByPo.TryGetValue(poid, out var list) ? list : new List<Dictionary<string, object?>>();

            if (!canViewCosts)
            {
                foreach (var hideKey in new[] { "subtotal", "tax", "total", "total_amount" })
                {
                    po.Remove(hideKey);
                }
            }
        }
    }

    private async Task EnrichPurchaseInvoicesAsync(List<Dictionary<string, object?>> invoices, bool canViewCosts)
    {
        if (invoices.Count == 0) return;

        var piIds = invoices.Select(i => Convert.ToInt64(i["id"] ?? 0)).Where(id => id > 0).ToArray();
        var items = await Db.HasTableAsync("purchase_invoice_items") && piIds.Length > 0
            ? await Db.QueryListAsync("SELECT * FROM purchase_invoice_items WHERE purchase_invoice_id IN @piIds ORDER BY id ASC", new { piIds })
            : new List<Dictionary<string, object?>>();

        var prodIds = items.Select(it => Convert.ToInt64(it["product_id"] ?? 0)).Where(id => id > 0).Distinct().ToArray();
        var prodMap = await LoadProductsWithBrandsSuppliersBatchesAsync(prodIds);

        var itemsByPi = new Dictionary<long, List<Dictionary<string, object?>>>();
        foreach (var it in items)
        {
            var piid = Convert.ToInt64(it["purchase_invoice_id"] ?? 0);
            var pid = Convert.ToInt64(it["product_id"] ?? 0);
            it["product"] = prodMap.TryGetValue(pid, out var prodObj) ? prodObj : null;

            if (!canViewCosts)
            {
                foreach (var hideKey in new[] { "pack_purchase_price", "unit_purchase_price", "sub_total", "item_discount_percentage", "margin" })
                {
                    it.Remove(hideKey);
                }
            }

            if (!itemsByPi.TryGetValue(piid, out var list))
            {
                list = new List<Dictionary<string, object?>>();
                itemsByPi[piid] = list;
            }
            list.Add(it);
        }

        foreach (var pi in invoices)
        {
            var piid = Convert.ToInt64(pi["id"] ?? 0);
            pi["supplier"] = pi["supplier_id"] != null ? new Dictionary<string, object?>
            {
                ["id"] = pi["supplier_id"],
                ["name"] = pi["__supp_name"]
            } : null;
            pi.Remove("__supp_name");

            pi["items"] = itemsByPi.TryGetValue(piid, out var list) ? list : new List<Dictionary<string, object?>>();

            if (!canViewCosts)
            {
                foreach (var hideKey in new[] { "total_amount", "total_paid", "tax_amount", "discount_amount" })
                {
                    pi.Remove(hideKey);
                }
            }
        }
    }

    private async Task<Dictionary<long, Dictionary<string, object?>>> LoadProductsWithBrandsSuppliersBatchesAsync(long[] prodIds)
    {
        var map = new Dictionary<long, Dictionary<string, object?>>();
        if (prodIds.Length == 0) return map;

        var prods = await Db.QueryListAsync(@"
            SELECT p.id, p.name, p.product_code, p.barcode, p.unit_sale_price, p.whole_sale_unit_price,
                   p.quantity, p.pack_size, p.brand_id, p.supplier_id,
                   br.name AS __brand_name, s.name AS __supp_name
            FROM products p
            LEFT JOIN brands br ON br.id = p.brand_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            WHERE p.id IN @prodIds", new { prodIds });

        await AttachProductBatchesAsync(prods);

        foreach (var p in prods)
        {
            var pid = Convert.ToInt64(p["id"] ?? 0);
            p["brand"] = p["brand_id"] != null ? new Dictionary<string, object?> { ["id"] = p["brand_id"], ["name"] = p["__brand_name"] } : null;
            p["supplier"] = p["supplier_id"] != null ? new Dictionary<string, object?> { ["id"] = p["supplier_id"], ["name"] = p["__supp_name"] } : null;
            p.Remove("__brand_name");
            p.Remove("__supp_name");
            map[pid] = p;
        }

        return map;
    }
}

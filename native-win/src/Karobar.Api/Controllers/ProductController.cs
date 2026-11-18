using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/products")]
public class ProductController : ApiControllerBase
{
    private static readonly string[] LookupPermissions =
    [
        "product.view", "barcode.view", "barcode.print", "barcode-pos.view", "barcode-pos.create",
        "batch-expiry.view", "sale-invoice.view", "sale-invoice.create", "sale-invoice.update",
        "sale-return.view", "sale-return.create", "sale-return.update",
        "purchase-invoice.view", "purchase-invoice.create", "purchase-invoice.update",
        "purchase-return.view", "purchase-return.create", "purchase-return.update",
        "purchase-order.view", "purchase-order.create", "purchase-order.update",
        "stock-adjustment.view", "stock-adjustment.create", "stock-adjustment.update",
        "stock-disposal.view", "stock-disposal.create",
        "stocktake.view", "stocktake.create", "stocktake.update",
        "report.purchase-detail.view", "report.purchase-summary.view", "report.purchase-returns.view",
        "report.sale-detail.view", "report.sale-returns.view", "report.product-comprehensive.view",
        "report.near-expiry-product.view"
    ];

    public ProductController(KarobarDb db) : base(db)
    {
    }

    [HttpGet("search")]
    [HttpGet("by-ids")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q = "",
        [FromQuery] int limit = 30)
    {
        if (!await CanAsync(LookupPermissions)) return Forbidden();

        q = (q ?? "").Trim();
        limit = Math.Clamp(limit <= 0 ? 30 : limit, 1, 1000);

        var idList = new List<long>();
        foreach (var rawVal in Request.Query["ids"].Concat(Request.Query["ids[]"]))
        {
            if (string.IsNullOrWhiteSpace(rawVal)) continue;
            foreach (var part in rawVal.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (long.TryParse(part.Trim(), out var parsed) && parsed > 0) idList.Add(parsed);
            }
        }

        var hasCategories = await Db.HasTableAsync("categories");
        var hasLocations = await Db.HasTableAsync("storage_locations") && await Db.HasColumnAsync("products", "storage_location_id");
        var hasBatches = await Db.HasTableAsync("batches") && await Db.HasColumnAsync("batches", "quantity");

        var batchSumExpr = hasBatches
            ? "(SELECT COALESCE(SUM(b.quantity), 0) FROM batches b WHERE b.product_id = p.id)"
            : "0";

        var param = new DynamicParameters();
        string whereClause = "";
        string limitClause = "";

        if (idList.Count > 0)
        {
            whereClause = "WHERE p.id IN @ids";
            param.Add("ids", idList.Distinct().ToArray());
        }
        else
        {
            if (!string.IsNullOrEmpty(q))
            {
                whereClause = "WHERE p.name LIKE @qPrefix";
                param.Add("qPrefix", $"{q}%");
            }
            limitClause = "LIMIT @limit";
            param.Add("limit", limit);
        }

        var sql = $@"
            SELECT p.*,
                   {batchSumExpr} AS batches_sum_quantity,
                   br.name AS __brand_name,
                   s.name AS __supplier_name
                   {(hasCategories ? ", c.name AS __category_name" : "")}
                   {(hasLocations ? ", sl.code AS __loc_code, sl.name AS __loc_name, sl.type AS __loc_type" : "")}
            FROM products p
            LEFT JOIN brands br ON br.id = p.brand_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            {(hasCategories ? "LEFT JOIN categories c ON c.id = p.category_id" : "")}
            {(hasLocations ? "LEFT JOIN storage_locations sl ON sl.id = p.storage_location_id" : "")}
            {whereClause}
            ORDER BY p.name COLLATE NOCASE ASC
            {limitClause}";

        var rows = await Db.QueryListAsync(sql, param);
        foreach (var r in rows)
        {
            AttachNestedRelations(r, hasCategories, hasLocations);
        }

        return Ok(rows);
    }

    [HttpGet("new-code")]
    public async Task<IActionResult> GenerateNewCode()
    {
        if (!await CanAsync("product.create")) return Forbidden();

        var lastProd = await Db.QuerySingleOrDefaultAsync("SELECT product_code FROM products ORDER BY id DESC LIMIT 1");
        int newCodeNum = 1;
        if (lastProd != null && lastProd.TryGetValue("product_code", out var pcObj) && pcObj != null)
        {
            var m = Regex.Match(pcObj.ToString()!, @"PRD-(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var lastNum))
            {
                newCodeNum = lastNum + 1;
            }
        }

        var formattedCode = "PRD-" + newCodeNum.ToString().PadLeft(4, '0');
        var rand = Random.Shared.NextInt64(0, 10_000_000_000L);
        var barcode = "PRD" + rand.ToString().PadLeft(10, '0');

        return Ok(new
        {
            product_code = formattedCode,
            barcode
        });
    }

    [HttpGet("available-quantity")]
    [HttpGet("{product:long}/available-quantity")]
    public async Task<IActionResult> AvailableQuantity(
        [FromRoute] long? product = null,
        [FromQuery(Name = "product_id")] long? queryProductId = null,
        [FromQuery] string? batch = "")
    {
        if (!await CanAsync(LookupPermissions)) return Forbidden();

        var productId = product ?? queryProductId;
        if (!productId.HasValue || productId.Value <= 0)
        {
            return Unprocessable("product_id is required");
        }

        var prodRow = await Db.QuerySingleOrDefaultAsync("SELECT id, quantity FROM products WHERE id = @id", new { id = productId.Value });
        if (prodRow == null)
        {
            return NotFound(new { message = "Product not found" });
        }

        var totalQty = Convert.ToInt32(prodRow["quantity"] ?? 0);
        var batchName = (batch ?? "").Trim();

        var batches = await Db.HasTableAsync("batches")
            ? await Db.QueryListAsync("SELECT batch_number, quantity FROM batches WHERE product_id = @id", new { id = productId.Value })
            : new List<Dictionary<string, object?>>();

        var totalBatched = batches.Sum(b => Convert.ToInt32(b["quantity"] ?? 0));
        var unbatchedQty = Math.Max(0, totalQty - totalBatched);

        int available;
        if (!string.IsNullOrEmpty(batchName) && batchName != "__unbatched__")
        {
            available = batches
                .Where(b => string.Equals(b["batch_number"]?.ToString(), batchName, StringComparison.Ordinal))
                .Sum(b => Convert.ToInt32(b["quantity"] ?? 0));
        }
        else
        {
            available = batches.Count > 0 ? unbatchedQty : totalQty;
        }

        return Ok(new Dictionary<string, object?>
        {
            ["product_id"] = productId.Value,
            ["batch"] = batchName,
            ["available_units"] = available,
            ["unbatched_units"] = unbatchedQty,
            ["batched_units"] = totalBatched,
            ["total_units"] = totalQty
        });
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "per_page")] int perPage = 25,
        [FromQuery] int page = 1,
        [FromQuery] string? q = "",
        [FromQuery(Name = "q_name")] string? qName = "",
        [FromQuery(Name = "q_brand")] string? qBrand = "",
        [FromQuery(Name = "q_supplier")] string? qSupplier = "",
        [FromQuery(Name = "q_formulation")] string? qFormulation = "",
        [FromQuery(Name = "q_barcode")] string? qBarcode = "",
        [FromQuery(Name = "q_rack")] string? qRack = "",
        [FromQuery(Name = "low_stock")] int lowStock = 0)
    {
        if (!await CanAsync("product.view")) return Forbidden();

        perPage = Math.Clamp(perPage <= 0 ? 25 : perPage, 1, 100);
        page = Math.Max(1, page);

        var search = (q ?? "").Trim();
        var nameFilter = (qName ?? "").Trim();
        var brandFilter = (qBrand ?? "").Trim();
        var supplierFilter = (qSupplier ?? "").Trim();
        var formulationFilter = (qFormulation ?? "").Trim();
        var barcodeFilter = (qBarcode ?? "").Trim();
        var rackFilter = (qRack ?? "").Trim();

        var hasCategories = await Db.HasTableAsync("categories");
        var hasLocations = await Db.HasTableAsync("storage_locations") && await Db.HasColumnAsync("products", "storage_location_id");
        var hasBatches = await Db.HasTableAsync("batches");
        var hasProductSuppliers = await Db.HasTableAsync("product_suppliers");

        var where = new List<string>();
        var param = new DynamicParameters();

        if (!string.IsNullOrEmpty(search))
        {
            where.Add(@"(
                p.name LIKE @sPrefix OR
                p.product_code LIKE @sPrefix OR
                br.name LIKE @sPrefix OR
                s.name LIKE @sPrefix OR
                p.barcode LIKE @sPrefix OR
                p.formulation LIKE @sContains OR
                p.rack LIKE @sPrefix
            )");
            param.Add("sPrefix", $"{search}%");
            param.Add("sContains", $"%{search}%");
        }

        if (!string.IsNullOrEmpty(nameFilter))
        {
            where.Add("p.name LIKE @qName");
            param.Add("qName", $"{nameFilter}%");
        }
        if (!string.IsNullOrEmpty(brandFilter))
        {
            where.Add("br.name LIKE @qBrand");
            param.Add("qBrand", $"{brandFilter}%");
        }
        if (!string.IsNullOrEmpty(supplierFilter))
        {
            if (hasProductSuppliers)
            {
                where.Add(@"(s.name LIKE @qSupp OR EXISTS (
                    SELECT 1 FROM product_suppliers ps
                    INNER JOIN suppliers s2 ON s2.id = ps.supplier_id
                    WHERE ps.product_id = p.id AND s2.name LIKE @qSupp
                ))");
            }
            else
            {
                where.Add("s.name LIKE @qSupp");
            }
            param.Add("qSupp", $"{supplierFilter}%");
        }
        if (!string.IsNullOrEmpty(formulationFilter))
        {
            where.Add("p.formulation LIKE @qForm");
            param.Add("qForm", $"%{formulationFilter}%");
        }
        if (!string.IsNullOrEmpty(barcodeFilter))
        {
            where.Add("p.barcode LIKE @qBarcode");
            param.Add("qBarcode", $"{barcodeFilter}%");
        }
        if (!string.IsNullOrEmpty(rackFilter))
        {
            where.Add("p.rack LIKE @qRack");
            param.Add("qRack", $"{rackFilter}%");
        }
        if (lowStock == 1 && await Db.HasTableAsync("purchase_invoice_items"))
        {
            where.Add(@"p.id IN (SELECT DISTINCT pii.product_id FROM purchase_invoice_items pii WHERE pii.product_id IS NOT NULL)
                        AND p.quantity IS NOT NULL AND p.quantity < p.pack_size");
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        var batchCountExpr = hasBatches ? "(SELECT COUNT(1) FROM batches b WHERE b.product_id = p.id)" : "0";
        var batchSumExpr = hasBatches ? "(SELECT COALESCE(SUM(b.quantity), 0) FROM batches b WHERE b.product_id = p.id)" : "0";

        var selectSql = $@"
            SELECT p.*,
                   {batchCountExpr} AS batches_count,
                   {batchSumExpr} AS batches_sum_quantity,
                   br.name AS __brand_name,
                   s.name AS __supplier_name
                   {(hasCategories ? ", c.name AS __category_name" : "")}
                   {(hasLocations ? ", sl.code AS __loc_code, sl.name AS __loc_name, sl.type AS __loc_type" : "")}
            FROM products p
            LEFT JOIN brands br ON br.id = p.brand_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            {(hasCategories ? "LEFT JOIN categories c ON c.id = p.category_id" : "")}
            {(hasLocations ? "LEFT JOIN storage_locations sl ON sl.id = p.storage_location_id" : "")}
            {whereClause}
            ORDER BY p.id DESC";

        var countSql = $@"
            SELECT COUNT(1)
            FROM products p
            LEFT JOIN brands br ON br.id = p.brand_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            {whereClause}";

        var paged = await Db.PaginateAsync(selectSql, countSql, param, page, perPage, items =>
        {
            foreach (var r in items)
            {
                AttachNestedRelations(r, hasCategories, hasLocations);
            }
            return Task.FromResult(items);
        });

        return Ok(paged);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("product.create")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var (errors, data) = await ValidateProductPayloadAsync(body, null);
        if (errors.Count > 0)
        {
            return Unprocessable(errors.Values.First().First(), errors);
        }

        if (Request.HasFormContentType && Request.Form.Files.GetFile("image") is { Length: > 0 } file)
        {
            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? ".jpg";
            var dir = Db.StoragePath("app/public/products");
            Directory.CreateDirectory(dir);
            var fileName = $"{Guid.NewGuid()}{ext}";
            await using var fs = new FileStream(Path.Combine(dir, fileName), FileMode.Create);
            await file.CopyToAsync(fs);
            data["image"] = $"products/{fileName}";
        }

        if ((!data.TryGetValue("storage_location_id", out var locObj) || locObj == null) && await Db.HasTableAsync("storage_locations"))
        {
            var defaultLocId = await Db.ExecuteScalarAsync<long?>("SELECT id FROM storage_locations WHERE is_default = 1 LIMIT 1")
                               ?? await Db.ExecuteScalarAsync<long?>("SELECT id FROM storage_locations ORDER BY id ASC LIMIT 1");
            data["storage_location_id"] = defaultLocId;
        }

        var id = await Db.InsertAsync("products", data);

        if (data.TryGetValue("supplier_id", out var sidObj) && sidObj != null && await Db.HasTableAsync("product_suppliers"))
        {
            var sid = Convert.ToInt64(sidObj);
            var existingLink = await Db.QuerySingleOrDefaultAsync(
                "SELECT id FROM product_suppliers WHERE product_id = @pid AND supplier_id = @sid LIMIT 1",
                new { pid = id, sid });

            var upp = data.TryGetValue("unit_purchase_price", out var uppObj) ? uppObj : null;
            if (existingLink != null)
            {
                await Db.UpdateAsync("product_suppliers", Convert.ToInt64(existingLink["id"]), new Dictionary<string, object?>
                {
                    ["last_purchase_price"] = upp,
                    ["is_preferred"] = true
                });
            }
            else
            {
                await Db.InsertAsync("product_suppliers", new Dictionary<string, object?>
                {
                    ["product_id"] = id,
                    ["supplier_id"] = sid,
                    ["last_purchase_price"] = upp,
                    ["is_preferred"] = true
                });
            }
        }

        var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id });
        return StatusCode(201, product);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("product.view")) return Forbidden();

        var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id });
        if (product == null) return NotFound(new { message = "Product not found." });

        var brandId = product.TryGetValue("brand_id", out var bid) && bid != null ? Convert.ToInt64(bid) : 0;
        var catId = product.TryGetValue("category_id", out var cid) && cid != null ? Convert.ToInt64(cid) : 0;
        var suppId = product.TryGetValue("supplier_id", out var sid) && sid != null ? Convert.ToInt64(sid) : 0;
        var locId = product.TryGetValue("storage_location_id", out var lid) && lid != null ? Convert.ToInt64(lid) : 0;

        product["brand"] = brandId > 0 ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM brands WHERE id = @id", new { id = brandId }) : null;
        product["category"] = catId > 0 ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM categories WHERE id = @id", new { id = catId }) : null;
        product["supplier"] = suppId > 0 ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @id", new { id = suppId }) : null;

        if (locId > 0 && await Db.HasTableAsync("storage_locations"))
        {
            product["storage_location"] = await Db.QuerySingleOrDefaultAsync(
                "SELECT id, code, name, type, default_rack FROM storage_locations WHERE id = @id",
                new { id = locId });
        }

        if (await Db.HasTableAsync("product_suppliers"))
        {
            var links = await Db.QueryListAsync(@"
                SELECT ps.*, s.name AS __supp_name
                FROM product_suppliers ps
                LEFT JOIN suppliers s ON s.id = ps.supplier_id
                WHERE ps.product_id = @id
                ORDER BY ps.is_preferred DESC, ps.id ASC", new { id });

            foreach (var link in links)
            {
                link["supplier"] = link["supplier_id"] != null ? new Dictionary<string, object?>
                {
                    ["id"] = link["supplier_id"],
                    ["name"] = link["__supp_name"]
                } : null;
                link.Remove("__supp_name");
            }
            product["product_suppliers"] = links;
        }

        return Ok(product);
    }

    [HttpPut("{id:long}")]
    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("product.update")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Product not found." });

        var body = await ReadJsonBodyAsync();
        var (errors, data) = await ValidateProductPayloadAsync(body, id);
        if (errors.Count > 0)
        {
            return Unprocessable(errors.Values.First().First(), errors);
        }

        if (Request.HasFormContentType && Request.Form.Files.GetFile("image") is { Length: > 0 } file)
        {
            var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? ".jpg";
            var dir = Db.StoragePath("app/public/products");
            Directory.CreateDirectory(dir);
            var fileName = $"{Guid.NewGuid()}{ext}";
            await using var fs = new FileStream(Path.Combine(dir, fileName), FileMode.Create);
            await file.CopyToAsync(fs);
            data["image"] = $"products/{fileName}";
        }

        await Db.UpdateAsync("products", id, data);

        if (data.TryGetValue("supplier_id", out var sidObj) && sidObj != null && await Db.HasTableAsync("product_suppliers"))
        {
            var sid = Convert.ToInt64(sidObj);
            var existingLink = await Db.QuerySingleOrDefaultAsync(
                "SELECT id FROM product_suppliers WHERE product_id = @pid AND supplier_id = @sid LIMIT 1",
                new { pid = id, sid });

            var upp = data.TryGetValue("unit_purchase_price", out var uppObj) ? uppObj : existing["unit_purchase_price"];
            if (existingLink != null)
            {
                await Db.UpdateAsync("product_suppliers", Convert.ToInt64(existingLink["id"]), new Dictionary<string, object?>
                {
                    ["last_purchase_price"] = upp,
                    ["is_preferred"] = true
                });
            }
            else
            {
                await Db.InsertAsync("product_suppliers", new Dictionary<string, object?>
                {
                    ["product_id"] = id,
                    ["supplier_id"] = sid,
                    ["last_purchase_price"] = upp,
                    ["is_preferred"] = true
                });
            }
        }

        var updated = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id });
        return Ok(updated);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("product.delete")) return Forbidden();

        var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id });
        if (product == null) return NotFound(new { message = "Product not found." });

        var qty = Convert.ToInt32(product["quantity"] ?? 0);
        if (qty > 0)
        {
            return Unprocessable("Cannot delete: product has on-hand quantity.");
        }

        if (await Db.HasTableAsync("batches"))
        {
            var hasBatches = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM batches WHERE product_id = @id", new { id }) > 0;
            if (hasBatches)
            {
                return Unprocessable("Cannot delete: product has batch records.");
            }
        }

        var refSummary = await GetProductDocumentReferencesSummaryAsync(id, " | ", ", ");
        if (!string.IsNullOrEmpty(refSummary))
        {
            return Unprocessable("Product cannot be deleted because it exists in the following documents. " + refSummary);
        }

        await Db.ExecuteAsync("DELETE FROM products WHERE id = @id", new { id });
        return Ok(new { message = "Product deleted" });
    }

    [HttpPost("bulk-destroy")]
    public async Task<IActionResult> BulkDestroy()
    {
        if (!await CanAsync("product.delete")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var ids = new List<long>();
        if (body.TryGetValue("product_ids", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in idsEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var pid)) ids.Add(pid);
                else if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), out var spid)) ids.Add(spid);
            }
        }

        if (ids.Count == 0)
        {
            return Unprocessable("The product_ids field is required.", new Dictionary<string, string[]>
            {
                ["product_ids"] = ["The product_ids field is required."]
            });
        }

        int deleted = 0;
        var failed = new List<Dictionary<string, object?>>();

        foreach (var id in ids)
        {
            var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id });
            if (product == null)
            {
                failed.Add(new Dictionary<string, object?> { ["id"] = id, ["name"] = null, ["reason"] = "Not found" });
                continue;
            }

            var prodName = product["name"]?.ToString();
            var qty = Convert.ToInt32(product["quantity"] ?? 0);
            if (qty > 0)
            {
                failed.Add(new Dictionary<string, object?> { ["id"] = id, ["name"] = prodName, ["reason"] = "Has on-hand quantity" });
                continue;
            }

            if (await Db.HasTableAsync("batches"))
            {
                var hasBatches = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM batches WHERE product_id = @id", new { id }) > 0;
                if (hasBatches)
                {
                    failed.Add(new Dictionary<string, object?> { ["id"] = id, ["name"] = prodName, ["reason"] = "Has batch records" });
                    continue;
                }
            }

            var refSummary = await GetProductDocumentReferencesSummaryAsync(id, " | ", " | ");
            if (!string.IsNullOrEmpty(refSummary))
            {
                failed.Add(new Dictionary<string, object?> { ["id"] = id, ["name"] = prodName, ["reason"] = "Exists in: " + refSummary });
                continue;
            }

            await Db.ExecuteAsync("DELETE FROM products WHERE id = @id", new { id });
            deleted++;
        }

        return Ok(new
        {
            deleted,
            failed
        });
    }

    [HttpPatch("bulk-update-meta")]
    public async Task<IActionResult> BulkUpdateMeta()
    {
        if (!await CanAsync("product.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var ids = new List<long>();
        if (body.TryGetValue("product_ids", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in idsEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var pid)) ids.Add(pid);
                else if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), out var spid)) ids.Add(spid);
            }
        }

        if (ids.Count == 0)
        {
            return Unprocessable("The product_ids field is required.");
        }

        var categoryId = GetNullableLong(body, "category_id");
        var brandId = GetNullableLong(body, "brand_id");
        var supplierId = GetNullableLong(body, "supplier_id");

        var updates = new Dictionary<string, object?>();
        if (categoryId.HasValue) updates["category_id"] = categoryId.Value;
        if (brandId.HasValue) updates["brand_id"] = brandId.Value;
        if (supplierId.HasValue) updates["supplier_id"] = supplierId.Value;

        if (updates.Count == 0)
        {
            return Unprocessable("Provide at least one of category_id, brand_id, supplier_id to update.");
        }

        var setClauses = new List<string>();
        var param = new DynamicParameters();
        foreach (var kv in updates)
        {
            setClauses.Add($"{kv.Key} = @{kv.Key}");
            param.Add(kv.Key, kv.Value);
        }
        param.Add("ids", ids);

        var count = await Db.ExecuteAsync($"UPDATE products SET {string.Join(", ", setClauses)} WHERE id IN @ids", param);
        return Ok(new
        {
            updated = count,
            updates,
            product_ids = ids
        });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        if (!await CanAsync("product.export")) return Forbidden();

        var rows = await Db.QueryListAsync(@"
            SELECT p.*,
                   c.name AS __cat_name,
                   br.name AS __brand_name,
                   s.name AS __supp_name
            FROM products p
            LEFT JOIN categories c ON c.id = p.category_id
            LEFT JOIN brands br ON br.id = p.brand_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            ORDER BY p.id ASC");

        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("product_code,name,pack_size,unit_purchase_price,unit_sale_price,pack_purchase_price,pack_sale_price,whole_sale_pack_price,whole_sale_unit_price,avg_price,margin,quantity,category,brand,supplier,barcode,narcotic,max_discount,formulation,description,rack");

        foreach (var p in rows)
        {
            var line = string.Join(",",
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(p["product_code"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(p["name"])),
                Convert.ToInt32(p["pack_size"] ?? 0).ToString(),
                BrandController.EscapeCsv(p["unit_purchase_price"]?.ToString() ?? ""),
                BrandController.EscapeCsv(p["unit_sale_price"]?.ToString() ?? ""),
                BrandController.EscapeCsv(p["pack_purchase_price"]?.ToString() ?? ""),
                BrandController.EscapeCsv(p["pack_sale_price"]?.ToString() ?? ""),
                BrandController.EscapeCsv(p["whole_sale_pack_price"]?.ToString() ?? ""),
                BrandController.EscapeCsv(p["whole_sale_unit_price"]?.ToString() ?? ""),
                BrandController.EscapeCsv(p["avg_price"]?.ToString() ?? ""),
                BrandController.EscapeCsv(p["margin"]?.ToString() ?? ""),
                Convert.ToInt32(p["quantity"] ?? 0).ToString(),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(p["__cat_name"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(p["__brand_name"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(p["__supp_name"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(p["barcode"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(p["narcotic"] ?? "no")),
                BrandController.EscapeCsv(p["max_discount"]?.ToString() ?? ""),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(p["formulation"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(p["description"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(p["rack"])));
            sb.AppendLine(line);
        }

        var fileName = $"products_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=UTF-8", fileName);
    }

    [HttpGet("{product:long}/substitutes")]
    public async Task<IActionResult> Substitutes(long product)
    {
        if (!await CanAsync("product.view", "barcode-pos.view", "barcode-pos.create", "sale-invoice.view", "sale-invoice.create", "sale-invoice.update"))
        {
            return Forbidden();
        }

        var prodRow = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = product });
        if (prodRow == null) return NotFound(new { message = "Product not found." });

        var formulation = (prodRow["formulation"]?.ToString() ?? "").Trim();
        var catId = prodRow["category_id"] != null ? Convert.ToInt64(prodRow["category_id"]) : 0;
        var basePrice = Convert.ToDouble(prodRow["unit_sale_price"] ?? 0.0);
        var baseQty = Convert.ToInt32(prodRow["quantity"] ?? 0);

        var param = new DynamicParameters();
        param.Add("selfId", product);
        string matchType;
        string whereClause;

        if (!string.IsNullOrEmpty(formulation))
        {
            matchType = "formulation";
            var primarySalt = Regex.Replace(formulation, @"\b\d+(\.\d+)?\s*(mg|ml|mcg|g|iu|%)\b", "", RegexOptions.IgnoreCase).Trim();
            if (!string.IsNullOrEmpty(primarySalt) && primarySalt.Length >= 3 && !string.Equals(primarySalt, formulation, StringComparison.OrdinalIgnoreCase))
            {
                whereClause = "WHERE p.id != @selfId AND (p.formulation LIKE @formFull OR p.formulation LIKE @formSalt)";
                param.Add("formFull", $"%{formulation}%");
                param.Add("formSalt", $"%{primarySalt}%");
            }
            else
            {
                whereClause = "WHERE p.id != @selfId AND p.formulation LIKE @formFull";
                param.Add("formFull", $"%{formulation}%");
            }
        }
        else if (catId > 0)
        {
            matchType = "category";
            whereClause = "WHERE p.id != @selfId AND p.category_id = @catId";
            param.Add("catId", catId);
        }
        else
        {
            return Ok(new Dictionary<string, object?>
            {
                ["product"] = new Dictionary<string, object?>
                {
                    ["id"] = product,
                    ["name"] = prodRow["name"],
                    ["formulation"] = prodRow["formulation"],
                    ["unit_sale_price"] = basePrice,
                    ["quantity"] = baseQty
                },
                ["match_type"] = "none",
                ["substitutes"] = Array.Empty<object>()
            });
        }

        var subRows = await Db.QueryListAsync($@"
            SELECT p.*, br.name AS __brand_name, c.name AS __category_name
            FROM products p
            LEFT JOIN brands br ON br.id = p.brand_id
            LEFT JOIN categories c ON c.id = p.category_id
            {whereClause}
            ORDER BY CASE WHEN COALESCE(p.quantity, 0) > 0 THEN 0 ELSE 1 END ASC, p.unit_sale_price ASC
            LIMIT 25", param);

        var subIds = subRows.Select(s => Convert.ToInt64(s["id"] ?? 0)).Where(id => id > 0).ToArray();
        var batchesByProd = new Dictionary<long, List<Dictionary<string, object?>>>();
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");

        if (subIds.Length > 0 && await Db.HasTableAsync("batches"))
        {
            var bRows = await Db.QueryListAsync($@"
                SELECT * FROM batches
                WHERE product_id IN @subIds AND quantity > 0
                ORDER BY CASE WHEN expiry_date IS NOT NULL AND expiry_date >= '{today}' THEN 0 ELSE 1 END ASC,
                         expiry_date ASC", new { subIds });

            foreach (var br in bRows)
            {
                var pid = Convert.ToInt64(br["product_id"] ?? 0);
                if (!batchesByProd.TryGetValue(pid, out var list))
                {
                    list = new List<Dictionary<string, object?>>();
                    batchesByProd[pid] = list;
                }
                list.Add(br);
            }
        }

        var substitutes = new List<Dictionary<string, object?>>();
        foreach (var sub in subRows)
        {
            var sid = Convert.ToInt64(sub["id"] ?? 0);
            var prodBatches = batchesByProd.TryGetValue(sid, out var bList) ? bList : new List<Dictionary<string, object?>>();
            var fefoBatch = prodBatches.FirstOrDefault(b =>
            {
                var exp = CustomerController.FormatDateYmd(b["expiry_date"]);
                var notExpired = string.IsNullOrEmpty(exp) || string.CompareOrdinal(exp, today) >= 0;
                return notExpired && Convert.ToInt32(b["quantity"] ?? 0) > 0;
            }) ?? prodBatches.FirstOrDefault();

            var subUsp = Convert.ToDouble(sub["unit_sale_price"] ?? 0.0);
            var effectiveUnitPrice = fefoBatch != null && fefoBatch.TryGetValue("unit_sale_price", out var bUspObj) && bUspObj != null && Convert.ToDouble(bUspObj) > 0
                ? Convert.ToDouble(bUspObj)
                : subUsp;

            var priceDiff = Math.Round(effectiveUnitPrice - basePrice, 2);
            var subForm = (sub["formulation"]?.ToString() ?? "").Trim();
            var isExactFormulation = !string.IsNullOrEmpty(formulation) && string.Equals(subForm, formulation, StringComparison.OrdinalIgnoreCase);

            substitutes.Add(new Dictionary<string, object?>
            {
                ["id"] = sid,
                ["name"] = sub["name"],
                ["product_code"] = sub["product_code"],
                ["barcode"] = sub["barcode"],
                ["formulation"] = sub["formulation"],
                ["is_exact_formulation"] = isExactFormulation,
                ["rack"] = sub["rack"],
                ["pack_size"] = Convert.ToInt32(sub["pack_size"] ?? 1),
                ["quantity"] = Convert.ToInt32(sub["quantity"] ?? 0),
                ["unit_sale_price"] = effectiveUnitPrice,
                ["pack_sale_price"] = Convert.ToDouble(sub["pack_sale_price"] ?? 0.0),
                ["whole_sale_unit_price"] = Convert.ToDouble(sub["whole_sale_unit_price"] ?? 0.0),
                ["whole_sale_pack_price"] = Convert.ToDouble(sub["whole_sale_pack_price"] ?? 0.0),
                ["price_diff"] = priceDiff,
                ["brand"] = sub["brand_id"] != null ? new Dictionary<string, object?> { ["id"] = sub["brand_id"], ["name"] = sub["__brand_name"] } : null,
                ["category"] = sub["category_id"] != null ? new Dictionary<string, object?> { ["id"] = sub["category_id"], ["name"] = sub["__category_name"] } : null,
                ["Earliest_expiry"] = fefoBatch?["expiry_date"],
                ["fefo_batch"] = fefoBatch != null ? new Dictionary<string, object?>
                {
                    ["id"] = fefoBatch["id"],
                    ["batch_number"] = fefoBatch["batch_number"],
                    ["expiry_date"] = fefoBatch["expiry_date"],
                    ["quantity"] = Convert.ToInt32(fefoBatch["quantity"] ?? 0),
                    ["unit_sale_price"] = effectiveUnitPrice
                } : null
            });
        }

        return Ok(new Dictionary<string, object?>
        {
            ["product"] = new Dictionary<string, object?>
            {
                ["id"] = product,
                ["name"] = prodRow["name"],
                ["formulation"] = prodRow["formulation"],
                ["category_id"] = prodRow["category_id"],
                ["unit_sale_price"] = basePrice,
                ["quantity"] = baseQty
            },
            ["match_type"] = matchType,
            ["substitutes"] = substitutes
        });
    }

    [HttpGet("{product:long}/suppliers")]
    public async Task<IActionResult> GetSuppliers(long product)
    {
        if (!await CanAsync("product.view")) return Forbidden();
        if (!await Db.HasTableAsync("product_suppliers")) return Ok(Array.Empty<object>());

        var links = await Db.QueryListAsync(@"
            SELECT ps.*, s.name AS __s_name, s.phone AS __s_phone, s.email AS __s_email
            FROM product_suppliers ps
            LEFT JOIN suppliers s ON s.id = ps.supplier_id
            WHERE ps.product_id = @product
            ORDER BY ps.is_preferred DESC, ps.id ASC", new { product });

        foreach (var l in links)
        {
            l["supplier"] = l["supplier_id"] != null ? new Dictionary<string, object?>
            {
                ["id"] = l["supplier_id"],
                ["name"] = l["__s_name"],
                ["phone"] = l["__s_phone"],
                ["email"] = l["__s_email"]
            } : null;
            l.Remove("__s_name");
            l.Remove("__s_phone");
            l.Remove("__s_email");
        }

        return Ok(links);
    }

    [HttpPost("{product:long}/suppliers")]
    public async Task<IActionResult> SaveSupplier(long product)
    {
        if (!await CanAsync("product.update")) return Forbidden();

        var prodRow = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @product", new { product });
        if (prodRow == null) return NotFound(new { message = "Product not found." });

        var body = await ReadJsonBodyAsync();
        var supplierId = GetNullableLong(body, "supplier_id");
        if (!supplierId.HasValue || supplierId.Value <= 0)
        {
            return Unprocessable("The supplier_id field is required.", new Dictionary<string, string[]>
            {
                ["supplier_id"] = ["The supplier_id field is required."]
            });
        }

        var suppRow = await Db.QuerySingleOrDefaultAsync(
            "SELECT id, name, phone, email FROM suppliers WHERE id = @sid",
            new { sid = supplierId.Value });
        if (suppRow == null)
        {
            return Unprocessable("The selected supplier_id is invalid.", new Dictionary<string, string[]>
            {
                ["supplier_id"] = ["The selected supplier_id is invalid."]
            });
        }

        var isPreferred = GetBool(body, "is_preferred", false);
        if (isPreferred)
        {
            await Db.ExecuteAsync(
                "UPDATE product_suppliers SET is_preferred = 0 WHERE product_id = @product AND supplier_id != @sid",
                new { product, sid = supplierId.Value });
        }

        var lastPurchasePrice = body.ContainsKey("last_purchase_price") && !string.IsNullOrWhiteSpace(GetString(body, "last_purchase_price"))
            ? (object?)GetDecimal(body, "last_purchase_price")
            : prodRow["unit_purchase_price"];

        var leadTimeDays = GetNullableLong(body, "lead_time_days");
        var minOrderQty = GetInt(body, "min_order_qty", 1);

        var payload = new Dictionary<string, object?>
        {
            ["product_id"] = product,
            ["supplier_id"] = supplierId.Value,
            ["supplier_sku"] = GetString(body, "supplier_sku"),
            ["last_purchase_price"] = lastPurchasePrice,
            ["lead_time_days"] = leadTimeDays,
            ["min_order_qty"] = Math.Max(1, minOrderQty),
            ["is_preferred"] = isPreferred,
            ["notes"] = GetString(body, "notes")
        };

        var existing = await Db.QuerySingleOrDefaultAsync(
            "SELECT id FROM product_suppliers WHERE product_id = @product AND supplier_id = @sid LIMIT 1",
            new { product, sid = supplierId.Value });

        long recordId;
        if (existing != null)
        {
            recordId = Convert.ToInt64(existing["id"]);
            await Db.UpdateAsync("product_suppliers", recordId, payload);
        }
        else
        {
            recordId = await Db.InsertAsync("product_suppliers", payload);
        }

        var record = await Db.QuerySingleOrDefaultAsync("SELECT * FROM product_suppliers WHERE id = @recordId", new { recordId });
        if (record != null) record["supplier"] = suppRow;

        return StatusCode(201, record);
    }

    [HttpDelete("{product:long}/suppliers/{supplierId:long}")]
    public async Task<IActionResult> RemoveSupplier(long product, long supplierId)
    {
        if (!await CanAsync("product.update")) return Forbidden();

        var prodRow = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @product", new { product });
        if (prodRow == null) return NotFound(new { message = "Product not found." });

        if (Convert.ToInt64(prodRow["supplier_id"] ?? 0) == supplierId)
        {
            return Unprocessable("Cannot remove the primary supplier. Change the product primary supplier first.");
        }

        await Db.ExecuteAsync(
            "DELETE FROM product_suppliers WHERE product_id = @product AND supplier_id = @supplierId",
            new { product, supplierId });

        return Ok(new { message = "Supplier mapping removed" });
    }

    // -------------------------------------------------------------------------
    // Internal Helpers
    // -------------------------------------------------------------------------

    private static void AttachNestedRelations(Dictionary<string, object?> r, bool hasCategories, bool hasLocations)
    {
        r["brand"] = r.TryGetValue("brand_id", out var bid) && bid != null
            ? new Dictionary<string, object?> { ["id"] = bid, ["name"] = r.TryGetValue("__brand_name", out var bn) ? bn : null }
            : null;
        r.Remove("__brand_name");

        r["supplier"] = r.TryGetValue("supplier_id", out var sid) && sid != null
            ? new Dictionary<string, object?> { ["id"] = sid, ["name"] = r.TryGetValue("__supplier_name", out var sn) ? sn : null }
            : null;
        r.Remove("__supplier_name");

        if (hasCategories)
        {
            r["category"] = r.TryGetValue("category_id", out var cid) && cid != null
                ? new Dictionary<string, object?> { ["id"] = cid, ["name"] = r.TryGetValue("__category_name", out var cn) ? cn : null }
                : null;
            r.Remove("__category_name");
        }

        if (hasLocations)
        {
            r["storage_location"] = r.TryGetValue("storage_location_id", out var lid) && lid != null
                ? new Dictionary<string, object?>
                {
                    ["id"] = lid,
                    ["code"] = r.TryGetValue("__loc_code", out var lc) ? lc : null,
                    ["name"] = r.TryGetValue("__loc_name", out var ln) ? ln : null,
                    ["type"] = r.TryGetValue("__loc_type", out var lt) ? lt : null
                }
                : null;
            r.Remove("__loc_code");
            r.Remove("__loc_name");
            r.Remove("__loc_type");
        }
    }

    private async Task<(Dictionary<string, string[]> Errors, Dictionary<string, object?> Data)> ValidateProductPayloadAsync(
        Dictionary<string, JsonElement> body,
        long? ignoreId)
    {
        var errors = new Dictionary<string, string[]>();
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var productCode = GetString(body, "product_code")?.Trim() ?? "";
        var name = GetString(body, "name")?.Trim() ?? "";
        var barcode = GetString(body, "barcode")?.Trim() ?? "";
        var packSize = GetInt(body, "pack_size", 0);
        var narcotic = GetString(body, "narcotic")?.Trim().ToLowerInvariant() ?? "";
        var categoryId = GetNullableLong(body, "category_id");
        var brandId = GetNullableLong(body, "brand_id");
        var supplierId = GetNullableLong(body, "supplier_id");

        if (string.IsNullOrEmpty(productCode)) errors["product_code"] = ["The product_code field is required."];
        if (string.IsNullOrEmpty(name)) errors["name"] = ["The name field is required."];
        if (string.IsNullOrEmpty(barcode)) errors["barcode"] = ["The barcode field is required."];
        if (packSize <= 0) errors["pack_size"] = ["The pack_size field is required."];
        if (narcotic != "yes" && narcotic != "no") errors["narcotic"] = ["The selected narcotic is invalid."];
        if (!categoryId.HasValue) errors["category_id"] = ["The category_id field is required."];
        if (!brandId.HasValue) errors["brand_id"] = ["The brand_id field is required."];
        if (!supplierId.HasValue) errors["supplier_id"] = ["The supplier_id field is required."];

        if (errors.Count > 0) return (errors, data);

        var idCondition = ignoreId.HasValue ? "AND id != @ignoreId" : "";
        if (await Db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM products WHERE product_code = @productCode {idCondition}", new { productCode, ignoreId }) > 0)
        {
            errors["product_code"] = ["The product code has already been taken."];
        }
        if (await Db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM products WHERE name = @name {idCondition}", new { name, ignoreId }) > 0)
        {
            errors["name"] = ["The name has already been taken."];
        }
        if (await Db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM products WHERE barcode = @barcode {idCondition}", new { barcode, ignoreId }) > 0)
        {
            errors["barcode"] = ["The barcode has already been taken."];
        }

        if (errors.Count > 0) return (errors, data);

        data["product_code"] = productCode;
        data["name"] = name;
        data["barcode"] = barcode;
        data["pack_size"] = packSize;
        data["narcotic"] = narcotic;
        data["category_id"] = categoryId!.Value;
        data["brand_id"] = brandId!.Value;
        data["supplier_id"] = supplierId!.Value;

        string[] strFields = ["description", "rack", "hs_code", "tax_type", "sro_schedule_no", "sro_item_serial_no", "drug_schedule", "default_dosage"];
        foreach (var sf in strFields)
        {
            if (body.ContainsKey(sf))
            {
                var v = GetString(body, sf);
                data[sf] = string.IsNullOrWhiteSpace(v) ? null : v.Trim();
            }
        }

        string[] numFields =
        [
            "pack_purchase_price", "pack_sale_price", "unit_purchase_price", "unit_sale_price",
            "whole_sale_pack_price", "whole_sale_unit_price", "whole_sale_margin", "avg_price",
            "margin", "sales_tax_rate", "further_tax_rate"
        ];
        foreach (var nf in numFields)
        {
            if (body.ContainsKey(nf))
            {
                var raw = GetString(body, nf);
                data[nf] = !string.IsNullOrWhiteSpace(raw) && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : null;
            }
        }

        foreach (var intField in new[] { "quantity", "max_discount", "expiry_warning_days", "storage_location_id" })
        {
            if (body.ContainsKey(intField))
            {
                var raw = GetString(body, intField);
                data[intField] = !string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var iv) ? iv : (intField == "quantity" ? 0 : null);
            }
        }

        if (body.ContainsKey("requires_cold_chain"))
        {
            data["requires_cold_chain"] = GetBool(body, "requires_cold_chain", false);
        }

        if (data.TryGetValue("drug_schedule", out var dsObj) && dsObj?.ToString() == "narcotic")
        {
            data["narcotic"] = "yes";
        }
        else if (data["narcotic"]?.ToString() == "yes" && (!data.TryGetValue("drug_schedule", out var dsObj2) || dsObj2 == null))
        {
            data["drug_schedule"] = "narcotic";
        }

        if (body.ContainsKey("formulation"))
        {
            var rawForm = GetString(body, "formulation");
            var impSvc = new ProductImportService(Db);
            data["formulation"] = await impSvc.ResolveOrCreateFormulationAsync(rawForm);
        }

        return (errors, data);
    }

    private async Task<string?> GetProductDocumentReferencesSummaryAsync(long productId, string sectionSeparator, string itemSeparator)
    {
        const int capPerType = 8;

        async Task<List<string>> FetchRefs(string itemTable, string fkCol, string headerTable)
        {
            if (!await Db.HasTableAsync(itemTable) || !await Db.HasTableAsync(headerTable)) return new List<string>();
            var rows = await Db.QueryListAsync($@"
                SELECT DISTINCT h.id, h.posted_number
                FROM {itemTable} it
                INNER JOIN {headerTable} h ON h.id = it.{fkCol}
                WHERE it.product_id = @productId", new { productId });

            return rows
                .Select(r => r["posted_number"]?.ToString() is { Length: > 0 } pn ? pn : r["id"]?.ToString() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct()
                .ToList();
        }

        var piNos = await FetchRefs("purchase_invoice_items", "purchase_invoice_id", "purchase_invoices");
        var siNos = await FetchRefs("sale_invoice_items", "sale_invoice_id", "sale_invoices");
        var prNos = await FetchRefs("purchase_return_items", "purchase_return_id", "purchase_returns");
        var srNos = await FetchRefs("sale_return_items", "sale_return_id", "sale_returns");

        if (piNos.Count == 0 && siNos.Count == 0 && prNos.Count == 0 && srNos.Count == 0)
        {
            return null;
        }

        string? FormatPart(string label, List<string> coll)
        {
            if (coll.Count == 0) return null;
            var shown = coll.Take(capPerType);
            var more = Math.Max(0, coll.Count - capPerType);
            var baseStr = $"{label}: {string.Join(itemSeparator, shown)}";
            return more > 0 ? $"{baseStr} +{more} more" : baseStr;
        }

        var parts = new[]
        {
            FormatPart("Purchase Invoices", piNos),
            FormatPart("Sale Invoices", siNos),
            FormatPart("Purchase Returns", prNos),
            FormatPart("Sale Returns", srNos)
        }.Where(p => p != null).ToArray();

        return string.Join(sectionSeparator, parts);
    }
}

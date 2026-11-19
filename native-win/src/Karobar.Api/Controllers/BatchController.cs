using System.Globalization;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

public class BatchController : ApiControllerBase
{
    private static readonly string[] ViewBatchPermissions =
    [
        "product.view", "barcode.view", "barcode.print", "barcode-pos.view", "barcode-pos.create",
        "batch-expiry.view", "sale-invoice.create", "sale-invoice.update",
        "purchase-invoice.create", "purchase-invoice.update",
        "sale-return.create", "sale-return.update",
        "purchase-return.create", "purchase-return.update",
        "stock-adjustment.create", "stock-adjustment.update",
        "stock-disposal.create", "stocktake.create", "stocktake.update"
    ];

    public BatchController(KarobarDb db) : base(db)
    {
    }

    public static void EnrichBatchModelAttributes(Dictionary<string, object?> batch, Dictionary<string, object?>? product = null)
    {
        var today = DateTime.UtcNow.Date;
        var expStr = CustomerController.FormatDateYmd(batch.TryGetValue("expiry_date", out var ed) ? ed : null);
        batch["expiry_date"] = expStr;
        batch["expiry"] = expStr;

        var qty = Convert.ToInt32(batch.TryGetValue("quantity", out var q) && q != null ? q : 0);
        batch["quantity"] = qty;
        batch["available_units"] = qty;

        bool isExpired = false;
        int? daysRemaining = null;
        if (!string.IsNullOrEmpty(expStr) && DateTime.TryParse(expStr, out var expDt))
        {
            daysRemaining = (int)(expDt.Date - today).TotalDays;
            isExpired = daysRemaining.Value < 0;
        }

        batch["is_expired"] = isExpired;
        batch["days_remaining"] = daysRemaining;

        if (product != null)
        {
            foreach (var pf in new[] { "unit_sale_price", "pack_sale_price", "unit_purchase_price", "pack_purchase_price", "whole_sale_unit_price", "whole_sale_pack_price" })
            {
                if ((!batch.TryGetValue(pf, out var bv) || bv == null) && product.TryGetValue(pf, out var pv) && pv != null)
                {
                    batch[pf] = Convert.ToDouble(pv);
                }
            }
        }
    }

    [HttpGet("api/products/{product:long}/batches")]
    public async Task<IActionResult> Index(
        long product,
        [FromQuery(Name = "storage_location_id")] long? storageLocationId = null,
        [FromQuery(Name = "include_unbatched")] string? includeUnbatchedRaw = null)
    {
        if (!await CanAsync(ViewBatchPermissions)) return Forbidden();
        var includeUnbatched = !string.IsNullOrWhiteSpace(includeUnbatchedRaw) &&
            (includeUnbatchedRaw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
             includeUnbatchedRaw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             includeUnbatchedRaw.Equals("yes", StringComparison.OrdinalIgnoreCase));

        var prodRow = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @product", new { product });
        if (prodRow == null) return NotFound(new { message = "Product not found." });

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var hasLocations = await Db.HasTableAsync("storage_locations") && await Db.HasColumnAsync("batches", "storage_location_id");

        var param = new DynamicParameters();
        param.Add("product", product);
        var locWhere = "";
        if (storageLocationId.HasValue)
        {
            locWhere = "AND b.storage_location_id = @locId";
            param.Add("locId", storageLocationId.Value);
        }

        var sql = $@"
            SELECT b.*
                   {(hasLocations ? ", sl.code AS __loc_code, sl.name AS __loc_name, sl.type AS __loc_type, sl.default_rack AS __loc_rack" : "")}
            FROM batches b
            {(hasLocations ? "LEFT JOIN storage_locations sl ON sl.id = b.storage_location_id" : "")}
            WHERE b.product_id = @product {locWhere}
            ORDER BY CASE WHEN b.expiry_date IS NOT NULL AND b.expiry_date >= '{today}' THEN 0 ELSE 1 END ASC,
                     b.expiry_date ASC,
                     b.id ASC";

        var batches = await Db.QueryListAsync(sql, param);

        foreach (var b in batches)
        {
            if (hasLocations)
            {
                b["storage_location"] = b.TryGetValue("storage_location_id", out var lid) && lid != null
                    ? new Dictionary<string, object?>
                    {
                        ["id"] = lid,
                        ["code"] = b["__loc_code"],
                        ["name"] = b["__loc_name"],
                        ["type"] = b["__loc_type"],
                        ["default_rack"] = b["__loc_rack"]
                    }
                    : null;
                b.Remove("__loc_code");
                b.Remove("__loc_name");
                b.Remove("__loc_type");
                b.Remove("__loc_rack");
            }
            EnrichBatchModelAttributes(b, prodRow);
        }

        if (hasLocations)
        {
            batches = batches.OrderBy(b =>
            {
                var exp = b["expiry_date"]?.ToString();
                return !string.IsNullOrEmpty(exp) && string.CompareOrdinal(exp, today) >= 0 ? 0 : 1;
            }).ThenBy(b => b["expiry_date"]?.ToString() ?? "9999-12-31", StringComparer.Ordinal)
            .ThenBy(b =>
            {
                var loc = b.TryGetValue("storage_location", out var lObj) ? lObj as Dictionary<string, object?> : null;
                var lType = loc?["type"]?.ToString();
                return lType == "shop" ? 0 : (lType == "cold_storage" ? 1 : 2);
            }).ThenBy(b => Convert.ToInt64(b["id"] ?? 0)).ToList();
        }

        bool fefoRecommendedFound = false;
        foreach (var b in batches)
        {
            var isExpired = b["is_expired"] is true;
            var qty = Convert.ToInt32(b["quantity"] ?? 0);
            if (!isExpired && qty > 0 && !fefoRecommendedFound)
            {
                b["is_fefo_recommended"] = true;
                fefoRecommendedFound = true;
            }
            else
            {
                b["is_fefo_recommended"] = false;
            }
        }

        var totalProductQty = Convert.ToInt32(prodRow["quantity"] ?? 0);
        var totalBatchedQty = await Db.ExecuteScalarAsync<int?>(
            "SELECT COALESCE(SUM(quantity), 0) FROM batches WHERE product_id = @product", new { product }) ?? 0;
        var unbatchedQty = Math.Max(0, totalProductQty - totalBatchedQty);

        if (includeUnbatched && unbatchedQty > 0)
        {
            var unbatchedItem = new Dictionary<string, object?>
            {
                ["id"] = null,
                ["product_id"] = product,
                ["batch_number"] = "",
                ["expiry_date"] = null,
                ["expiry"] = null,
                ["quantity"] = unbatchedQty,
                ["available_units"] = unbatchedQty,
                ["is_expired"] = false,
                ["days_remaining"] = null,
                ["storage_location_id"] = prodRow["storage_location_id"],
                ["rack"] = prodRow["rack"],
                ["unit_sale_price"] = prodRow["unit_sale_price"],
                ["pack_sale_price"] = prodRow["pack_sale_price"],
                ["unit_purchase_price"] = prodRow["unit_purchase_price"],
                ["pack_purchase_price"] = prodRow["pack_purchase_price"],
                ["whole_sale_unit_price"] = prodRow["whole_sale_unit_price"],
                ["whole_sale_pack_price"] = prodRow["whole_sale_pack_price"],
                ["is_unbatched"] = true,
                ["display_label"] = "Unbatched Stock",
                ["is_fefo_recommended"] = false
            };
            batches.Add(unbatchedItem);
        }

        Response.Headers["X-Total-Units"] = totalProductQty.ToString();
        Response.Headers["X-Batched-Units"] = totalBatchedQty.ToString();
        Response.Headers["X-Unbatched-Units"] = unbatchedQty.ToString();

        return Ok(batches);
    }

    [HttpPost("api/products/{product:long}/batches")]
    public async Task<IActionResult> Store(long product)
    {
        if (!await CanAsync("product.update")) return Forbidden();

        var prodRow = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @product", new { product });
        if (prodRow == null) return NotFound(new { message = "Product not found." });

        var body = await ReadJsonBodyAsync();
        var batchNumber = GetString(body, "batch_number")?.Trim() ?? "";
        var expiryRaw = GetString(body, "expiry_date")?.Trim() ?? "";
        var qty = GetInt(body, "quantity", 0);

        if (string.IsNullOrEmpty(batchNumber))
        {
            return Unprocessable("The batch_number field is required.", new Dictionary<string, string[]>
            {
                ["batch_number"] = ["The batch_number field is required."]
            });
        }
        var expiryDate = CustomerController.FormatDateYmd(expiryRaw);
        if (string.IsNullOrEmpty(expiryDate))
        {
            return Unprocessable("The expiry_date field is required.", new Dictionary<string, string[]>
            {
                ["expiry_date"] = ["The expiry_date field is required."]
            });
        }

        var currentTotal = Math.Max(0, Convert.ToInt32(prodRow["quantity"] ?? 0));
        var alreadyBatched = await Db.ExecuteScalarAsync<int?>(
            "SELECT COALESCE(SUM(quantity), 0) FROM batches WHERE product_id = @product", new { product }) ?? 0;

        long? defaultLocationId = null;
        if (await Db.HasTableAsync("storage_locations"))
        {
            defaultLocationId = await Db.ExecuteScalarAsync<long?>("SELECT id FROM storage_locations WHERE is_default = 1 LIMIT 1")
                                ?? await Db.ExecuteScalarAsync<long?>("SELECT id FROM storage_locations ORDER BY id ASC LIMIT 1");
        }

        long? locationId = body.ContainsKey("storage_location_id") && GetNullableLong(body, "storage_location_id").HasValue
            ? GetNullableLong(body, "storage_location_id")
            : (prodRow["storage_location_id"] != null ? Convert.ToInt64(prodRow["storage_location_id"]) : defaultLocationId);

        var rack = body.ContainsKey("rack")
            ? (GetString(body, "rack")?.Trim() is { Length: > 0 } rStr ? rStr : null)
            : prodRow["rack"]?.ToString();

        var existingSql = locationId.HasValue
            ? "SELECT * FROM batches WHERE product_id = @product AND batch_number = @batchNumber AND storage_location_id = @locationId LIMIT 1"
            : "SELECT * FROM batches WHERE product_id = @product AND batch_number = @batchNumber LIMIT 1";
        var existing = await Db.QuerySingleOrDefaultAsync(existingSql, new { product, batchNumber, locationId });

        var existingBatchQty = existing != null ? Convert.ToInt32(existing["quantity"] ?? 0) : 0;
        var otherBatched = Math.Max(0, alreadyBatched - existingBatchQty);
        var availableUnassigned = Math.Max(0, currentTotal - otherBatched);

        if (existing == null)
        {
            if (availableUnassigned <= 0)
            {
                var msg = $"Cannot add a new batch because this product has 0 unassigned stock available (Total on-hand: {currentTotal}, already batched: {otherBatched}).";
                return Unprocessable(msg, new Dictionary<string, string[]> { ["quantity"] = [msg] });
            }
            if (qty <= 0)
            {
                var msg = $"Batch quantity must be at least 1 unit from unassigned stock (Available unassigned: {availableUnassigned}).";
                return Unprocessable(msg, new Dictionary<string, string[]> { ["quantity"] = [msg] });
            }
        }

        if (qty > availableUnassigned)
        {
            var msg = $"Cannot assign {qty} units to batch. Only {availableUnassigned} unassigned units are available for this batch (Total on-hand: {currentTotal}, other batches: {otherBatched}).";
            return Unprocessable(msg, new Dictionary<string, string[]> { ["quantity"] = [msg] });
        }

        string[] priceFields = ["unit_sale_price", "pack_sale_price", "unit_purchase_price", "pack_purchase_price", "whole_sale_unit_price", "whole_sale_pack_price"];

        long batchId;
        if (existing != null)
        {
            batchId = Convert.ToInt64(existing["id"]);
            var updatePayload = new Dictionary<string, object?>
            {
                ["expiry_date"] = expiryDate,
                ["quantity"] = qty,
                ["storage_location_id"] = locationId,
                ["rack"] = rack
            };
            foreach (var f in priceFields)
            {
                var raw = GetString(body, f);
                if (body.ContainsKey(f) && !string.IsNullOrWhiteSpace(raw) && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                {
                    updatePayload[f] = d;
                }
            }
            await Db.UpdateAsync("batches", batchId, updatePayload);
        }
        else
        {
            var createPayload = new Dictionary<string, object?>
            {
                ["product_id"] = product,
                ["batch_number"] = batchNumber,
                ["expiry_date"] = expiryDate,
                ["quantity"] = qty,
                ["storage_location_id"] = locationId,
                ["rack"] = rack
            };
            foreach (var f in priceFields)
            {
                var raw = GetString(body, f);
                if (body.ContainsKey(f) && !string.IsNullOrWhiteSpace(raw) && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
                {
                    createPayload[f] = d;
                }
                else
                {
                    createPayload[f] = prodRow[f];
                }
            }
            batchId = await Db.InsertAsync("batches", createPayload);
        }

        var fresh = await GetBatchWithLocationAsync(batchId, prodRow);
        return StatusCode(201, fresh);
    }

    [HttpPut("api/batches/{batch:long}")]
    public async Task<IActionResult> Update(long batch)
    {
        if (!await CanAsync("product.update")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM batches WHERE id = @batch", new { batch });
        if (existing == null) return NotFound(new { message = "Batch not found." });

        var productId = Convert.ToInt64(existing["product_id"]);
        var prodRow = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @productId", new { productId });
        if (prodRow == null) return NotFound(new { message = "Product not found." });

        var body = await ReadJsonBodyAsync();
        var batchNumber = GetString(body, "batch_number")?.Trim() ?? "";
        var expiryDate = CustomerController.FormatDateYmd(GetString(body, "expiry_date"));
        var newQty = GetInt(body, "quantity", -1);

        if (string.IsNullOrEmpty(batchNumber))
        {
            return Unprocessable("The batch_number field is required.", new Dictionary<string, string[]> { ["batch_number"] = ["The batch_number field is required."] });
        }
        if (string.IsNullOrEmpty(expiryDate))
        {
            return Unprocessable("The expiry_date field is required.", new Dictionary<string, string[]> { ["expiry_date"] = ["The expiry_date field is required."] });
        }
        if (newQty < 0)
        {
            return Unprocessable("The quantity field must be at least 0.", new Dictionary<string, string[]> { ["quantity"] = ["The quantity field must be at least 0."] });
        }

        long? targetLocationId = body.ContainsKey("storage_location_id")
            ? GetNullableLong(body, "storage_location_id")
            : (existing["storage_location_id"] != null ? Convert.ToInt64(existing["storage_location_id"]) : null);

        var dupSql = targetLocationId.HasValue
            ? "SELECT COUNT(1) FROM batches WHERE product_id = @productId AND batch_number = @batchNumber AND storage_location_id = @targetLocationId AND id != @batch"
            : "SELECT COUNT(1) FROM batches WHERE product_id = @productId AND batch_number = @batchNumber AND storage_location_id IS NULL AND id != @batch";
        var dupCount = await Db.ExecuteScalarAsync<int>(dupSql, new { productId, batchNumber, targetLocationId, batch });
        if (dupCount > 0)
        {
            return Unprocessable("The batch number has already been taken.", new Dictionary<string, string[]>
            {
                ["batch_number"] = ["The batch number has already been taken."]
            });
        }

        var currentTotal = Math.Max(0, Convert.ToInt32(prodRow["quantity"] ?? 0));
        var otherBatched = await Db.ExecuteScalarAsync<int?>(
            "SELECT COALESCE(SUM(quantity), 0) FROM batches WHERE product_id = @productId AND id != @batch",
            new { productId, batch }) ?? 0;
        var availableUnassigned = Math.Max(0, currentTotal - otherBatched);

        if (newQty > availableUnassigned)
        {
            var curBatchQty = Convert.ToInt32(existing["quantity"] ?? 0);
            var unbatchedPool = Math.Max(0, currentTotal - (otherBatched + curBatchQty));
            var msg = $"Cannot assign {newQty} units to batch. Maximum assignable quantity is {availableUnassigned} units (Current batch: {curBatchQty}, unassigned stock: {unbatchedPool}).";
            return Unprocessable(msg, new Dictionary<string, string[]> { ["quantity"] = [msg] });
        }

        var updatePayload = new Dictionary<string, object?>
        {
            ["batch_number"] = batchNumber,
            ["expiry_date"] = expiryDate,
            ["quantity"] = newQty,
            ["storage_location_id"] = targetLocationId
        };

        if (body.ContainsKey("rack"))
        {
            var r = GetString(body, "rack")?.Trim();
            updatePayload["rack"] = string.IsNullOrEmpty(r) ? null : r;
        }

        foreach (var f in new[] { "unit_sale_price", "pack_sale_price", "unit_purchase_price", "pack_purchase_price", "whole_sale_unit_price", "whole_sale_pack_price" })
        {
            var raw = GetString(body, f);
            if (body.ContainsKey(f) && !string.IsNullOrWhiteSpace(raw) && double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            {
                updatePayload[f] = d;
            }
        }

        await Db.UpdateAsync("batches", batch, updatePayload);
        var fresh = await GetBatchWithLocationAsync(batch, prodRow);
        return Ok(fresh);
    }

    [HttpPost("api/batches/{batch:long}/move-location")]
    public async Task<IActionResult> MoveLocation(long batch)
    {
        if (!await CanAsync("product.update", "batch-expiry.update", "stock-adjustment.create")) return Forbidden();

        var sourceBatch = await Db.QuerySingleOrDefaultAsync("SELECT * FROM batches WHERE id = @batch", new { batch });
        if (sourceBatch == null) return NotFound(new { message = "Batch not found." });

        var body = await ReadJsonBodyAsync();
        var toLocationId = GetNullableLong(body, "to_storage_location_id");
        var moveQty = GetInt(body, "quantity", 0);

        if (!toLocationId.HasValue || toLocationId.Value <= 0)
        {
            return Unprocessable("The to_storage_location_id field is required.", new Dictionary<string, string[]>
            {
                ["to_storage_location_id"] = ["The to_storage_location_id field is required."]
            });
        }
        if (moveQty < 1)
        {
            return Unprocessable("The quantity field must be at least 1.", new Dictionary<string, string[]>
            {
                ["quantity"] = ["The quantity field must be at least 1."]
            });
        }

        var toLocation = await Db.QuerySingleOrDefaultAsync("SELECT * FROM storage_locations WHERE id = @id", new { id = toLocationId.Value });
        if (toLocation == null)
        {
            return Unprocessable("The selected to_storage_location_id is invalid.", new Dictionary<string, string[]>
            {
                ["to_storage_location_id"] = ["The selected to_storage_location_id is invalid."]
            });
        }

        var sourceQty = Convert.ToInt32(sourceBatch["quantity"] ?? 0);
        if (moveQty > sourceQty)
        {
            var msg = $"Cannot move {moveQty} units. Only {sourceQty} units are available in this batch location.";
            return Unprocessable(msg, new Dictionary<string, string[]> { ["quantity"] = [msg] });
        }

        var reqRack = GetString(body, "rack")?.Trim();
        var targetRack = !string.IsNullOrEmpty(reqRack)
            ? reqRack
            : (toLocation["default_rack"]?.ToString() ?? sourceBatch["rack"]?.ToString());

        var curLocId = sourceBatch["storage_location_id"] != null ? Convert.ToInt64(sourceBatch["storage_location_id"]) : 0;
        var productId = Convert.ToInt64(sourceBatch["product_id"]);
        var prodRow = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @productId", new { productId });
        var toLocName = toLocation["name"]?.ToString() ?? "";

        if (curLocId == toLocationId.Value)
        {
            await Db.UpdateAsync("batches", batch, new Dictionary<string, object?> { ["rack"] = targetRack });
            var sameFresh = await GetBatchWithLocationAsync(batch, prodRow);
            return Ok(new Dictionary<string, object?>
            {
                ["message"] = "Batch rack/location updated.",
                ["source_batch"] = sameFresh,
                ["target_batch"] = sameFresh
            });
        }

        var batchNumber = sourceBatch["batch_number"]?.ToString() ?? "";
        var targetBatch = await Db.QuerySingleOrDefaultAsync(@"
            SELECT * FROM batches
            WHERE product_id = @productId AND batch_number = @batchNumber AND storage_location_id = @toLocId AND id != @batch
            LIMIT 1",
            new { productId, batchNumber, toLocId = toLocationId.Value, batch });

        if (targetBatch != null)
        {
            var targetId = Convert.ToInt64(targetBatch["id"]);
            var targetNewQty = Convert.ToInt32(targetBatch["quantity"] ?? 0) + moveQty;
            var targetUpdates = new Dictionary<string, object?> { ["quantity"] = targetNewQty };
            if (targetRack != null) targetUpdates["rack"] = targetRack;
            await Db.UpdateAsync("batches", targetId, targetUpdates);

            var remQty = sourceQty - moveQty;
            Dictionary<string, object?>? sourceResult = null;
            if (remQty <= 0)
            {
                await Db.ExecuteAsync("DELETE FROM batches WHERE id = @batch", new { batch });
            }
            else
            {
                await Db.UpdateAsync("batches", batch, new Dictionary<string, object?> { ["quantity"] = remQty });
                sourceResult = await GetBatchWithLocationAsync(batch, prodRow);
            }

            return Ok(new Dictionary<string, object?>
            {
                ["message"] = $"Moved {moveQty} units to {toLocName}.",
                ["source_batch"] = sourceResult,
                ["target_batch"] = await GetBatchWithLocationAsync(targetId, prodRow)
            });
        }

        if (moveQty == sourceQty)
        {
            await Db.UpdateAsync("batches", batch, new Dictionary<string, object?>
            {
                ["storage_location_id"] = toLocationId.Value,
                ["rack"] = targetRack
            });

            return Ok(new Dictionary<string, object?>
            {
                ["message"] = $"Moved all {moveQty} units to {toLocName}.",
                ["source_batch"] = null,
                ["target_batch"] = await GetBatchWithLocationAsync(batch, prodRow)
            });
        }

        await Db.UpdateAsync("batches", batch, new Dictionary<string, object?> { ["quantity"] = sourceQty - moveQty });
        var newTargetId = await Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = productId,
            ["batch_number"] = batchNumber,
            ["expiry_date"] = sourceBatch["expiry_date"],
            ["quantity"] = moveQty,
            ["storage_location_id"] = toLocationId.Value,
            ["rack"] = targetRack,
            ["pack_purchase_price"] = sourceBatch["pack_purchase_price"],
            ["unit_purchase_price"] = sourceBatch["unit_purchase_price"],
            ["pack_sale_price"] = sourceBatch["pack_sale_price"],
            ["unit_sale_price"] = sourceBatch["unit_sale_price"],
            ["whole_sale_pack_price"] = sourceBatch["whole_sale_pack_price"],
            ["whole_sale_unit_price"] = sourceBatch["whole_sale_unit_price"]
        });

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = $"Split and moved {moveQty} units to {toLocName}.",
            ["source_batch"] = await GetBatchWithLocationAsync(batch, prodRow),
            ["target_batch"] = await GetBatchWithLocationAsync(newTargetId, prodRow)
        });
    }

    [HttpDelete("api/batches/{batch:long}")]
    public async Task<IActionResult> Destroy(long batch)
    {
        if (!await CanAsync("product.update")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM batches WHERE id = @batch", new { batch });
        if (existing == null) return NotFound(new { message = "Batch not found." });

        var productId = Convert.ToInt64(existing["product_id"]);
        var batchNumber = existing["batch_number"]?.ToString() ?? "";

        if (await Db.HasTableAsync("sale_invoice_items"))
        {
            var hasSales = await Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sale_invoice_items WHERE product_id = @productId AND batch_number = @batchNumber",
                new { productId, batchNumber }) > 0;

            if (hasSales)
            {
                var msg = $"Cannot delete batch '{batchNumber}' because sale transactions are recorded for it. Adjust its quantity to 0 instead.";
                return Unprocessable(msg, new Dictionary<string, string[]> { ["batch"] = [msg] });
            }
        }

        await Db.ExecuteAsync("DELETE FROM batches WHERE id = @batch", new { batch });
        return Ok(new { message = "Batch deleted successfully" });
    }

    private async Task<Dictionary<string, object?>?> GetBatchWithLocationAsync(long batchId, Dictionary<string, object?>? product)
    {
        var row = await Db.QuerySingleOrDefaultAsync("SELECT * FROM batches WHERE id = @batchId", new { batchId });
        if (row == null) return null;

        if (row.TryGetValue("storage_location_id", out var lidObj) && lidObj != null && await Db.HasTableAsync("storage_locations"))
        {
            row["storage_location"] = await Db.QuerySingleOrDefaultAsync(
                "SELECT * FROM storage_locations WHERE id = @id",
                new { id = Convert.ToInt64(lidObj) });
        }
        else
        {
            row["storage_location"] = null;
        }

        EnrichBatchModelAttributes(row, product);
        return row;
    }
}

using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/user-demands")]
[Authorize]
public class UserDemandController : ApiControllerBase
{
    public UserDemandController(KarobarDb db) : base(db)
    {
    }

    [HttpGet("count")]
    public async Task<IActionResult> Count()
    {
        if (!await CanAsync("user-demands.view", "user-demand.view", "purchase-order.view", "purchase-invoice.view", "product.view"))
        {
            return Forbidden();
        }

        if (!await Db.HasTableAsync("user_demands"))
        {
            return Ok(new Dictionary<string, object?>
            {
                ["count"] = 0,
                ["pending_count"] = 0
            });
        }

        var count = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM user_demands");
        var pendingCount = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM user_demands WHERE status = 'pending'");

        return Ok(new Dictionary<string, object?>
        {
            ["count"] = count,
            ["pending_count"] = pendingCount
        });
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? status = null,
        [FromQuery] int limit = 100)
    {
        if (!await CanAsync("user-demands.view", "user-demand.view", "purchase-order.view", "purchase-invoice.view", "product.view"))
        {
            return Forbidden();
        }

        if (!await Db.HasTableAsync("user_demands"))
        {
            return Ok(new Dictionary<string, object?>
            {
                ["count"] = 0,
                ["pending_count"] = 0,
                ["ordered_count"] = 0,
                ["fulfilled_count"] = 0,
                ["cancelled_count"] = 0,
                ["rows"] = new List<Dictionary<string, object?>>()
            });
        }

        limit = Math.Max(1, Math.Min(limit, 1000));
        var sql = "SELECT * FROM user_demands";
        var dyn = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(status) && status is "pending" or "ordered" or "fulfilled" or "cancelled")
        {
            sql += " WHERE status = @status";
            dyn.Add("status", status);
        }

        sql += " ORDER BY id DESC LIMIT @limit";
        dyn.Add("limit", limit);

        var rows = await Db.QueryListAsync(sql, dyn);
        await EnrichDemandsAsync(rows);

        var countRows = await Db.QueryListAsync("SELECT status, COUNT(1) as cnt FROM user_demands GROUP BY status");
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cr in countRows)
        {
            var st = cr.GetValueOrDefault("status")?.ToString() ?? "";
            var cnt = Convert.ToInt32(cr.GetValueOrDefault("cnt") ?? 0);
            counts[st] = cnt;
        }

        var pendingCount = counts.GetValueOrDefault("pending", 0);
        var orderedCount = counts.GetValueOrDefault("ordered", 0);
        var fulfilledCount = counts.GetValueOrDefault("fulfilled", 0);
        var cancelledCount = counts.GetValueOrDefault("cancelled", 0);

        return Ok(new Dictionary<string, object?>
        {
            ["count"] = counts.Values.Sum(),
            ["pending_count"] = pendingCount,
            ["ordered_count"] = orderedCount,
            ["fulfilled_count"] = fulfilledCount,
            ["cancelled_count"] = cancelledCount,
            ["rows"] = rows
        });
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("user-demands.create", "user-demand.create", "purchase-order.create", "sale-invoice.create", "product.create"))
        {
            return Forbidden();
        }

        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized(new { message = "Unauthenticated" });

        var body = await ReadJsonBodyAsync();

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            if (body.TryGetValue("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                if (itemsEl.GetArrayLength() == 0)
                {
                    return Unprocessable("At least one item is required.", new Dictionary<string, string[]>
                    {
                        ["items"] = new[] { "At least one item is required." }
                    });
                }

                var rootCustomerId = GetNullableLong(body, "customer_id");
                var rootNotes = GetString(body, "notes");
                var rootStatus = GetString(body, "status") ?? "pending";

                var demands = new List<Dictionary<string, object?>>();
                foreach (var itemEl in itemsEl.EnumerateArray())
                {
                    if (itemEl.ValueKind != JsonValueKind.Object) continue;
                    var itemDict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in itemEl.EnumerateObject()) itemDict[p.Name] = p.Value;

                    var merged = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["product_id"] = GetNullableLong(itemDict, "product_id"),
                        ["name"] = GetString(itemDict, "name"),
                        ["brand_id"] = GetNullableLong(itemDict, "brand_id"),
                        ["category_id"] = GetNullableLong(itemDict, "category_id"),
                        ["supplier_id"] = GetNullableLong(itemDict, "supplier_id"),
                        ["customer_id"] = GetNullableLong(itemDict, "customer_id") ?? rootCustomerId,
                        ["pack_size"] = GetInt(itemDict, "pack_size", 1),
                        ["requested_quantity"] = GetInt(itemDict, "requested_quantity", 1),
                        ["notes"] = !string.IsNullOrWhiteSpace(GetString(itemDict, "notes")) ? GetString(itemDict, "notes") : rootNotes,
                        ["status"] = !string.IsNullOrWhiteSpace(GetString(itemDict, "status")) ? GetString(itemDict, "status") : rootStatus
                    };

                    var created = await PersistDemandAsync(merged, userId.Value, conn, tx);
                    demands.Add(created);
                }

                await tx.CommitAsync();
                await EnrichDemandsAsync(demands);

                return StatusCode(201, new Dictionary<string, object?>
                {
                    ["count"] = demands.Count,
                    ["demands"] = demands
                });
            }

            var singlePayload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["product_id"] = GetNullableLong(body, "product_id"),
                ["name"] = GetString(body, "name"),
                ["brand_id"] = GetNullableLong(body, "brand_id"),
                ["category_id"] = GetNullableLong(body, "category_id"),
                ["supplier_id"] = GetNullableLong(body, "supplier_id"),
                ["customer_id"] = GetNullableLong(body, "customer_id"),
                ["pack_size"] = GetInt(body, "pack_size", 1),
                ["requested_quantity"] = GetInt(body, "requested_quantity", 1),
                ["notes"] = GetString(body, "notes"),
                ["status"] = GetString(body, "status") ?? "pending"
            };

            var demand = await PersistDemandAsync(singlePayload, userId.Value, conn, tx);
            await tx.CommitAsync();
            await EnrichDemandsAsync(new List<Dictionary<string, object?>> { demand });

            return StatusCode(201, demand);
        }
        catch (UnauthorizedAccessException ex)
        {
            await tx.RollbackAsync();
            return Forbidden(ex.Message);
        }
        catch (ArgumentException ex)
        {
            await tx.RollbackAsync();
            return Unprocessable(ex.Message);
        }
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("user-demands.update", "user-demand.update", "purchase-order.create", "product.update"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM user_demands WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "User demand not found." });

        var body = await ReadJsonBodyAsync();
        var updates = new Dictionary<string, object?>();

        if (body.ContainsKey("status"))
        {
            var status = GetString(body, "status")?.Trim() ?? "";
            if (status is not ("pending" or "ordered" or "fulfilled" or "cancelled"))
            {
                return Unprocessable("Invalid status.");
            }
            updates["status"] = status;
        }

        if (body.ContainsKey("requested_quantity"))
        {
            var qty = GetInt(body, "requested_quantity", 1);
            if (qty < 1) return Unprocessable("Requested quantity must be at least 1.");
            updates["requested_quantity"] = qty;
        }

        if (body.ContainsKey("notes"))
        {
            updates["notes"] = GetString(body, "notes");
        }

        if (updates.Count > 0)
        {
            await Db.UpdateAsync("user_demands", id, updates);
        }

        var updated = await Db.QuerySingleOrDefaultAsync("SELECT * FROM user_demands WHERE id = @id", new { id });
        if (updated != null)
        {
            await EnrichDemandsAsync(new List<Dictionary<string, object?>> { updated });
        }

        return Ok(updated);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("user-demands.delete", "user-demand.delete", "purchase-order.delete", "product.delete"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM user_demands WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "User demand not found." });

        await Db.ExecuteAsync("DELETE FROM user_demands WHERE id = @id", new { id });
        return Ok(new Dictionary<string, object?> { ["message"] = "User demand deleted" });
    }

    private async Task<Dictionary<string, object?>> PersistDemandAsync(
        IDictionary<string, object?> validated,
        long userId,
        IDbConnection conn,
        IDbTransaction tx)
    {
        var quantity = Math.Max(1, Convert.ToInt32(validated.TryGetValue("requested_quantity", out var rq) && rq != null ? rq : 1));
        var name = (validated.TryGetValue("name", out var nObj) ? nObj?.ToString() : "")?.Trim() ?? "";
        var productId = validated.TryGetValue("product_id", out var pidObj) && pidObj != null ? Convert.ToInt64(pidObj) : 0L;

        Dictionary<string, object?>? product;
        if (productId > 0)
        {
            product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = productId }, conn, tx);
            if (product == null) throw new ArgumentException($"Product #{productId} not found.");
            name = product.GetValueOrDefault("name")?.ToString() ?? name;
        }
        else
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Product name or product_id is required.");
            }

            product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE name = @name LIMIT 1", new { name }, conn, tx);
            if (product == null)
            {
                if (!await Db.UserCanAsync(userId, "product.create"))
                {
                    throw new UnauthorizedAccessException("You do not have permission to create new products in the master catalog.");
                }

                product = await CreateProductFromDemandAsync(validated, conn, tx);
            }
            productId = Convert.ToInt64(product["id"]);
        }

        var status = (validated.TryGetValue("status", out var stObj) && stObj != null && !string.IsNullOrWhiteSpace(stObj.ToString())
            ? stObj.ToString()!
            : "pending").Trim().ToLowerInvariant();
        if (status is not ("pending" or "ordered" or "fulfilled" or "cancelled"))
        {
            throw new ArgumentException("Invalid status. Must be pending, ordered, fulfilled, or cancelled.");
        }

        var demandId = await Db.InsertAsync("user_demands", new Dictionary<string, object?>
        {
            ["product_id"] = productId,
            ["customer_id"] = validated.TryGetValue("customer_id", out var cid) ? cid : null,
            ["requested_by"] = userId,
            ["requested_name"] = name,
            ["requested_quantity"] = quantity,
            ["notes"] = validated.TryGetValue("notes", out var nt) ? nt : null,
            ["status"] = status
        }, conn, tx);

        return (await Db.QuerySingleOrDefaultAsync("SELECT * FROM user_demands WHERE id = @id", new { id = demandId }, conn, tx))!;
    }

    private async Task<Dictionary<string, object?>> CreateProductFromDemandAsync(
        IDictionary<string, object?> validated,
        IDbConnection conn,
        IDbTransaction tx)
    {
        var lastCode = await Db.ExecuteScalarAsync<string?>(
            "SELECT product_code FROM products ORDER BY id DESC LIMIT 1",
            null,
            conn,
            tx);

        int newCodeNum = 1;
        if (!string.IsNullOrEmpty(lastCode))
        {
            var m = Regex.Match(lastCode, @"PRD-(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var parsed))
            {
                newCodeNum = parsed + 1;
            }
        }

        string productCode = "PRD-" + newCodeNum.ToString("D4");
        while (await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM products WHERE product_code = @code", new { code = productCode }, conn, tx) > 0)
        {
            newCodeNum++;
            productCode = "PRD-" + newCodeNum.ToString("D4");
        }

        var barcode = "PRD" + RandomNumberGenerator.GetInt32(0, 1000000000).ToString("D10");
        var packSize = Math.Max(1, Convert.ToInt32(validated.TryGetValue("pack_size", out var ps) && ps != null ? ps : 1));
        var name = (validated.TryGetValue("name", out var n) ? n?.ToString() : "")?.Trim() ?? "Product";

        var catId = validated.TryGetValue("category_id", out var cObj) && cObj != null && ToLong(cObj) > 0 ? ToLong(cObj) : 0L;
        if (catId <= 0 && await Db.HasTableAsync("categories"))
        {
            catId = await Db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM categories ORDER BY id ASC LIMIT 1), 0)", null, conn, tx);
            if (catId <= 0)
            {
                catId = await Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "General" }, conn, tx);
            }
        }

        var brId = validated.TryGetValue("brand_id", out var bObj) && bObj != null && ToLong(bObj) > 0 ? ToLong(bObj) : 0L;
        if (brId <= 0 && await Db.HasTableAsync("brands"))
        {
            brId = await Db.ExecuteScalarAsync<long>("SELECT COALESCE((SELECT id FROM brands ORDER BY id ASC LIMIT 1), 0)", null, conn, tx);
            if (brId <= 0)
            {
                brId = await Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "General" }, conn, tx);
            }
        }

        var supId = validated.TryGetValue("supplier_id", out var sObj) && sObj != null && ToLong(sObj) > 0 ? (object?)ToLong(sObj) : null;

        var newProdId = await Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = productCode,
            ["name"] = name,
            ["pack_size"] = packSize,
            ["quantity"] = 0,
            ["category_id"] = catId > 0 ? (object?)catId : null,
            ["brand_id"] = brId > 0 ? (object?)brId : null,
            ["supplier_id"] = supId,
            ["barcode"] = barcode,
            ["narcotic"] = "no",
            ["pack_purchase_price"] = 0,
            ["pack_sale_price"] = 0,
            ["unit_purchase_price"] = 0,
            ["unit_sale_price"] = 0,
            ["avg_price"] = 0,
            ["margin"] = 0
        }, conn, tx);

        return (await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @id", new { id = newProdId }, conn, tx))!;
    }

    private async Task EnrichDemandsAsync(List<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return;

        var prodIds = rows.Select(r => ToLong(r.GetValueOrDefault("product_id"))).Where(id => id > 0).Distinct().ToArray();
        var custIds = rows.Select(r => ToLong(r.GetValueOrDefault("customer_id"))).Where(id => id > 0).Distinct().ToArray();
        var userIds = rows.Select(r => ToLong(r.GetValueOrDefault("requested_by"))).Where(id => id > 0).Distinct().ToArray();

        var prods = new Dictionary<long, Dictionary<string, object?>>();
        if (prodIds.Length > 0 && await Db.HasTableAsync("products"))
        {
            var pRows = await Db.QueryListAsync(
                @"SELECT p.id, p.name, p.product_code, p.quantity, p.brand_id, p.supplier_id,
                         b.name as brand_name, s.name as supplier_name
                  FROM products p
                  LEFT JOIN brands b ON b.id = p.brand_id
                  LEFT JOIN suppliers s ON s.id = p.supplier_id
                  WHERE p.id IN @ids",
                new { ids = prodIds });
            foreach (var pr in pRows)
            {
                var pid = ToLong(pr["id"]);
                var bId = ToLong(pr.GetValueOrDefault("brand_id"));
                var sId = ToLong(pr.GetValueOrDefault("supplier_id"));
                var bName = pr.GetValueOrDefault("brand_name")?.ToString();
                var sName = pr.GetValueOrDefault("supplier_name")?.ToString();
                pr["brand"] = bId > 0 ? new Dictionary<string, object?> { ["id"] = bId, ["name"] = bName } : null;
                pr["supplier"] = sId > 0 ? new Dictionary<string, object?> { ["id"] = sId, ["name"] = sName } : null;
                prods[pid] = pr;
            }
        }

        var custs = new Dictionary<long, Dictionary<string, object?>>();
        if (custIds.Length > 0 && await Db.HasTableAsync("customers"))
        {
            var cRows = await Db.QueryListAsync("SELECT id, name FROM customers WHERE id IN @ids", new { ids = custIds });
            foreach (var cr in cRows) custs[ToLong(cr["id"])] = cr;
        }

        var users = new Dictionary<long, Dictionary<string, object?>>();
        if (userIds.Length > 0 && await Db.HasTableAsync("users"))
        {
            var uRows = await Db.QueryListAsync("SELECT id, name FROM users WHERE id IN @ids", new { ids = userIds });
            foreach (var ur in uRows) users[ToLong(ur["id"])] = ur;
        }

        foreach (var row in rows)
        {
            var pid = ToLong(row.GetValueOrDefault("product_id"));
            var cid = ToLong(row.GetValueOrDefault("customer_id"));
            var uid = ToLong(row.GetValueOrDefault("requested_by"));

            var prod = pid > 0 && prods.TryGetValue(pid, out var p) ? p : null;
            row["product"] = prod;
            row["customer"] = cid > 0 && custs.TryGetValue(cid, out var c) ? c : null;
            row["requester"] = uid > 0 && users.TryGetValue(uid, out var u) ? u : null;
            row["product_brand_name"] = prod?.GetValueOrDefault("brand_name");
            row["product_supplier_name"] = prod?.GetValueOrDefault("supplier_name");
        }
    }

    private static long ToLong(object? val)
    {
        if (val == null) return 0;
        if (val is long l) return l;
        if (val is int i) return i;
        return long.TryParse(val.ToString(), out var p) ? p : 0;
    }
}

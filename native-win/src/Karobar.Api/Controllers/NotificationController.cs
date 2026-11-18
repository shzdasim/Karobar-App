using System.Text.Json;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/notifications")]
public class NotificationController : ApiControllerBase
{
    public NotificationController(KarobarDb db) : base(db)
    {
    }

    private async Task<List<int>> GetDismissedLowStockIdsAsync(long userId)
    {
        var list = new List<int>();
        var user = await Db.QuerySingleOrDefaultAsync("SELECT preferences FROM users WHERE id = @userId", new { userId });
        if (user == null || !user.TryGetValue("preferences", out var prefObj) || prefObj == null)
        {
            return list;
        }

        try
        {
            var jsonStr = prefObj is string s ? s : JsonSerializer.Serialize(prefObj);
            using var doc = JsonDocument.Parse(jsonStr);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("dismissed_low_stock", out var dismissedEl) &&
                dismissedEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dismissedEl.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var id))
                    {
                        list.Add(id);
                    }
                    else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var sid))
                    {
                        list.Add(sid);
                    }
                }
            }
        }
        catch
        {
        }

        return list.Distinct().ToList();
    }

    private async Task SetDismissedLowStockIdsAsync(long userId, List<int> dismissedIds)
    {
        var user = await Db.QuerySingleOrDefaultAsync("SELECT preferences FROM users WHERE id = @userId", new { userId });
        var prefsDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (user != null && user.TryGetValue("preferences", out var prefObj) && prefObj != null)
        {
            try
            {
                var jsonStr = prefObj is string s ? s : JsonSerializer.Serialize(prefObj);
                var parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonStr);
                if (parsed != null)
                {
                    foreach (var kv in parsed)
                    {
                        prefsDict[kv.Key] = kv.Value;
                    }
                }
            }
            catch
            {
            }
        }

        prefsDict["dismissed_low_stock"] = dismissedIds;
        await Db.UpdateAsync("users", userId, new Dictionary<string, object?>
        {
            ["preferences"] = JsonSerializer.Serialize(prefsDict)
        });
    }

    [HttpGet("low-stock")]
    public async Task<IActionResult> LowStock()
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(new { message = "Unauthenticated" });
        }

        if (!await IsAdminAsync() && !await CanAsync("product.view"))
        {
            return Forbidden("This action is unauthorized.");
        }

        int limit = 100;
        if (int.TryParse(Request.Query["limit"].FirstOrDefault(), out var l))
        {
            limit = Math.Max(1, Math.Min(l, 1000));
        }

        var dismissed = await GetDismissedLowStockIdsAsync(CurrentUserId.Value);

        var notInClause = dismissed.Count > 0 ? "AND p.id NOT IN @dismissed" : "";
        var sql = $@"
            SELECT p.id AS product_id,
                   p.product_code,
                   p.name AS product_name,
                   p.pack_size,
                   p.quantity,
                   p.unit_sale_price,
                   p.unit_purchase_price,
                   COALESCE(b.name, '') AS brand_name,
                   COALESCE(s.name, '') AS supplier_name,
                   (p.pack_size - p.quantity) AS units_below_pack
            FROM products p
            INNER JOIN (SELECT DISTINCT product_id FROM purchase_invoice_items WHERE product_id IS NOT NULL) pii
                ON pii.product_id = p.id
            LEFT JOIN brands b ON b.id = p.brand_id
            LEFT JOIN suppliers s ON s.id = p.supplier_id
            WHERE p.quantity IS NOT NULL
              AND p.quantity < p.pack_size
              {notInClause}
            ORDER BY (p.quantity * 1.0 / NULLIF(p.pack_size, 0)) ASC, p.name ASC
            LIMIT @limit";

        var rows = await Db.QueryListAsync(sql, new { dismissed, limit });

        int count;
        if (rows.Count < limit)
        {
            count = rows.Count;
        }
        else
        {
            var countSql = $@"
                SELECT COUNT(*)
                FROM products p
                INNER JOIN (SELECT DISTINCT product_id FROM purchase_invoice_items WHERE product_id IS NOT NULL) pii
                    ON pii.product_id = p.id
                WHERE p.quantity IS NOT NULL
                  AND p.quantity < p.pack_size
                  {notInClause}";
            count = await Db.ExecuteScalarAsync<int>(countSql, new { dismissed });
        }

        return Ok(new Dictionary<string, object?>
        {
            ["count"] = count,
            ["rows"] = rows
        });
    }

    [HttpPost("dismiss")]
    public async Task<IActionResult> Dismiss()
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(new { message = "Unauthenticated" });
        }

        if (!await IsAdminAsync() && !await CanAsync("product.view"))
        {
            return Forbidden("This action is unauthorized.");
        }

        var body = await ReadJsonBodyAsync();
        var productId = GetInt(body, "product_id", 0);
        if (productId <= 0)
        {
            return Unprocessable("The product id field is required.", new Dictionary<string, string[]>
            {
                ["product_id"] = new[] { "The product id field is required." }
            });
        }

        var dismissed = await GetDismissedLowStockIdsAsync(CurrentUserId.Value);
        if (!dismissed.Contains(productId))
        {
            dismissed.Add(productId);
        }

        await SetDismissedLowStockIdsAsync(CurrentUserId.Value, dismissed);

        return Ok(new Dictionary<string, object?>
        {
            ["dismissed"] = dismissed
        });
    }

    [HttpPost("dismiss-all")]
    public async Task<IActionResult> DismissAll()
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(new { message = "Unauthenticated" });
        }

        if (!await IsAdminAsync() && !await CanAsync("product.view"))
        {
            return Forbidden("This action is unauthorized.");
        }

        var dismissed = await GetDismissedLowStockIdsAsync(CurrentUserId.Value);

        var currentRows = await Db.QueryListAsync(@"
            SELECT p.id
            FROM products p
            WHERE EXISTS (
                SELECT 1 FROM purchase_invoice_items pii WHERE pii.product_id = p.id
            )
            AND p.quantity IS NOT NULL
            AND p.quantity < p.pack_size");

        foreach (var r in currentRows)
        {
            if (r.TryGetValue("id", out var idObj) && idObj != null && int.TryParse(idObj.ToString(), out var pid))
            {
                if (!dismissed.Contains(pid))
                {
                    dismissed.Add(pid);
                }
            }
        }

        await SetDismissedLowStockIdsAsync(CurrentUserId.Value, dismissed);

        return Ok(new Dictionary<string, object?>
        {
            ["dismissed"] = dismissed
        });
    }

    [HttpPost("ack-demand")]
    public async Task<IActionResult> AckDemand()
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(new { message = "Unauthenticated" });
        }

        if (!await IsAdminAsync() && !await CanAsync("user-demand.view"))
        {
            return Forbidden("This action is unauthorized.");
        }

        var body = await ReadJsonBodyAsync();
        var ids = new List<int>();

        var singleId = GetInt(body, "demand_id", 0);
        if (singleId > 0)
        {
            ids.Add(singleId);
        }

        if (body.TryGetValue("demand_ids", out var arrEl) && arrEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in arrEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var id) && id > 0)
                {
                    ids.Add(id);
                }
                else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var sid) && sid > 0)
                {
                    ids.Add(sid);
                }
            }
        }

        ids = ids.Distinct().ToList();
        var now = DateTimeOffset.UtcNow;
        var nowDb = now.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        int updated = 0;

        if (ids.Count > 0 && await Db.HasTableAsync("user_demands"))
        {
            updated = await Db.ExecuteAsync(@"
                UPDATE user_demands
                SET last_notified_at = @nowDb,
                    updated_at = @nowDb
                WHERE id IN @ids
                  AND status = 'pending'",
                new { nowDb, ids });
        }

        return Ok(new Dictionary<string, object?>
        {
            ["acknowledged"] = ids,
            ["updated"] = updated,
            ["notified_at"] = now.ToString("yyyy-MM-ddTHH:mm:sszzz")
        });
    }
}

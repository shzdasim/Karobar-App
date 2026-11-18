using System.Text.Json;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class DocumentAuditService
{
    private readonly KarobarDb _db;

    public DocumentAuditService(KarobarDb db)
    {
        _db = db;
    }

    public async Task<long?> RecordCreateAsync(
        string entityType,
        long entityId,
        IDictionary<string, object?> afterPayload,
        long? userId = null,
        string? reason = null)
    {
        if (!await _db.HasTableAsync("document_histories")) return null;

        var diff = new Dictionary<string, object?>
        {
            ["event"] = "Initial document creation"
        };

        return await _db.InsertAsync("document_histories", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["entity_type"] = NormalizeEntityType(entityType),
            ["entity_id"] = entityId,
            ["action"] = "created",
            ["reason"] = reason,
            ["before_payload"] = null,
            ["after_payload"] = JsonSerializer.Serialize(afterPayload),
            ["diff"] = JsonSerializer.Serialize(diff)
        });
    }

    public async Task<long?> RecordUpdateAsync(
        string entityType,
        long entityId,
        IDictionary<string, object?> beforePayload,
        IDictionary<string, object?> afterPayload,
        long? userId = null,
        string? reason = null)
    {
        if (!await _db.HasTableAsync("document_histories")) return null;

        var diff = ComputeDiff(beforePayload, afterPayload);

        return await _db.InsertAsync("document_histories", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["entity_type"] = NormalizeEntityType(entityType),
            ["entity_id"] = entityId,
            ["action"] = "updated",
            ["reason"] = reason,
            ["before_payload"] = JsonSerializer.Serialize(beforePayload),
            ["after_payload"] = JsonSerializer.Serialize(afterPayload),
            ["diff"] = JsonSerializer.Serialize(diff)
        });
    }

    public async Task<long?> RecordReversalAsync(
        string entityType,
        long entityId,
        IDictionary<string, object?> snapshot,
        long? userId = null,
        string reason = "")
    {
        if (!await _db.HasTableAsync("document_histories")) return null;

        var after = new Dictionary<string, object?>(snapshot, StringComparer.OrdinalIgnoreCase)
        {
            ["status"] = "reversed"
        };
        var diff = new Dictionary<string, object?>
        {
            ["status"] = new Dictionary<string, object?> { ["from"] = "posted", ["to"] = "reversed" }
        };

        return await _db.InsertAsync("document_histories", new Dictionary<string, object?>
        {
            ["user_id"] = userId,
            ["entity_type"] = NormalizeEntityType(entityType),
            ["entity_id"] = entityId,
            ["action"] = "reversed",
            ["reason"] = reason,
            ["before_payload"] = JsonSerializer.Serialize(snapshot),
            ["after_payload"] = JsonSerializer.Serialize(after),
            ["diff"] = JsonSerializer.Serialize(diff)
        });
    }

    public async Task<List<Dictionary<string, object?>>> GetHistoryAsync(string entityType, long entityId)
    {
        if (!await _db.HasTableAsync("document_histories"))
        {
            return new List<Dictionary<string, object?>>();
        }

        var normalized = NormalizeEntityType(entityType);
        var shortName = normalized.Split('\\').Last();

        var rows = await _db.QueryListAsync(@"
            SELECT dh.*, u.name as user_name, u.email as user_email
            FROM document_histories dh
            LEFT JOIN users u ON u.id = dh.user_id
            WHERE (dh.entity_type = @fullType OR dh.entity_type = @shortName OR dh.entity_type = @rawType)
              AND dh.entity_id = @entityId
            ORDER BY dh.id DESC",
            new { fullType = normalized, shortName, rawType = entityType, entityId });

        foreach (var row in rows)
        {
            foreach (var jsonCol in new[] { "before_payload", "after_payload", "diff" })
            {
                if (row.TryGetValue(jsonCol, out var raw) && raw is string s && !string.IsNullOrWhiteSpace(s))
                {
                    try
                    {
                        row[jsonCol] = JsonSerializer.Deserialize<object>(s);
                    }
                    catch
                    {
                    }
                }
            }

            if (row.TryGetValue("user_id", out var uid) && uid != null)
            {
                row["user"] = new Dictionary<string, object?>
                {
                    ["id"] = uid,
                    ["name"] = row.TryGetValue("user_name", out var un) ? un : null,
                    ["email"] = row.TryGetValue("user_email", out var ue) ? ue : null
                };
            }
            else
            {
                row["user"] = null;
            }

            row.Remove("user_name");
            row.Remove("user_email");
        }

        return rows;
    }

    public static string NormalizeEntityType(string entityType)
    {
        if (entityType.StartsWith("App\\Models\\", StringComparison.OrdinalIgnoreCase))
        {
            return entityType;
        }

        return entityType.ToLowerInvariant() switch
        {
            "sale_invoice" or "sale-invoice" or "saleinvoice" => "App\\Models\\SaleInvoice",
            "purchase_invoice" or "purchase-invoice" or "purchaseinvoice" => "App\\Models\\PurchaseInvoice",
            "sale_return" or "sale-return" or "salereturn" => "App\\Models\\SaleReturn",
            "purchase_return" or "purchase-return" or "purchasereturn" => "App\\Models\\PurchaseReturn",
            "stock_adjustment" or "stock-adjustment" or "stockadjustment" => "App\\Models\\StockAdjustment",
            "stock_disposal" or "stock-disposal" or "stockdisposal" => "App\\Models\\StockDisposal",
            "stocktake_session" or "stocktake-session" or "stocktakesession" => "App\\Models\\StocktakeSession",
            "payment" => "App\\Models\\Payment",
            "purchase_order" or "purchase-order" or "purchaseorder" => "App\\Models\\PurchaseOrder",
            _ => $"App\\Models\\{entityType}"
        };
    }

    private static Dictionary<string, object?> ComputeDiff(
        IDictionary<string, object?> before,
        IDictionary<string, object?> after)
    {
        var diff = new Dictionary<string, object?>();
        foreach (var kv in after)
        {
            if (kv.Key.Equals("updated_at", StringComparison.OrdinalIgnoreCase) ||
                kv.Key.Equals("created_at", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            before.TryGetValue(kv.Key, out var oldVal);
            var oldStr = oldVal == null ? "" : JsonSerializer.Serialize(oldVal);
            var newStr = kv.Value == null ? "" : JsonSerializer.Serialize(kv.Value);

            if (!string.Equals(oldStr, newStr, StringComparison.Ordinal))
            {
                diff[kv.Key] = new Dictionary<string, object?>
                {
                    ["from"] = oldVal,
                    ["to"] = kv.Value
                };
            }
        }
        return diff;
    }
}

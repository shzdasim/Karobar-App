using System.Text.Json;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/document-histories")]
public class DocumentHistoryController : ApiControllerBase
{
    private readonly DocumentAuditService _auditService;

    public DocumentHistoryController(KarobarDb db) : base(db)
    {
        _auditService = new DocumentAuditService(db);
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? entity_type = null,
        [FromQuery] long? entity_id = null,
        [FromQuery] string? action = null,
        [FromQuery] long? user_id = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 25)
    {
        if (string.IsNullOrWhiteSpace(entity_type) || !entity_id.HasValue || entity_id.Value <= 0)
        {
            if (!await CanAsync("settings.view", "user.view"))
            {
                return Forbidden();
            }

            if (!await Db.HasTableAsync("document_histories"))
            {
                return Ok(new Dictionary<string, object?>
                {
                    ["histories"] = new List<Dictionary<string, object?>>(),
                    ["data"] = new List<Dictionary<string, object?>>(),
                    ["total"] = 0,
                    ["current_page"] = 1,
                    ["last_page"] = 1
                });
            }

            var where = new List<string> { "1=1" };
            var dyn = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(entity_type))
            {
                var resolved = TryResolveEntityType(entity_type);
                if (resolved == null)
                {
                    return BadRequest(new { message = "Invalid entity type" });
                }
                where.Add("dh.entity_type = @entityType");
                dyn.Add("entityType", resolved);
            }

            if (!string.IsNullOrWhiteSpace(action))
            {
                where.Add("dh.action = @action");
                dyn.Add("action", action.Trim());
            }

            if (user_id.HasValue && user_id.Value > 0)
            {
                where.Add("dh.user_id = @userId");
                dyn.Add("userId", user_id.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = "%" + search.Trim() + "%";
                where.Add("(dh.reason LIKE @search OR CAST(dh.entity_id AS TEXT) LIKE @search)");
                dyn.Add("search", s);
            }

            var whereSql = string.Join(" AND ", where);
            per_page = Math.Max(1, Math.Min(per_page, 100));

            var selectSql = $@"
                SELECT dh.*, u.name as user_name, u.email as user_email
                FROM document_histories dh
                LEFT JOIN users u ON u.id = dh.user_id
                WHERE {whereSql}
                ORDER BY dh.created_at DESC, dh.id DESC";

            var countSql = $"SELECT COUNT(1) FROM document_histories dh WHERE {whereSql}";

            var paged = await Db.PaginateAsync(selectSql, countSql, dyn, page, per_page, async rows =>
            {
                foreach (var row in rows)
                {
                    EnrichHistoryRow(row);
                }
                await Task.CompletedTask;
                return rows;
            });

            return Ok(new Dictionary<string, object?>
            {
                ["histories"] = paged["data"],
                ["data"] = paged["data"],
                ["total"] = paged["total"],
                ["current_page"] = paged["current_page"],
                ["last_page"] = paged["last_page"]
            });
        }

        var resolvedType = TryResolveEntityType(entity_type);
        if (resolvedType == null)
        {
            return BadRequest(new { message = "Invalid entity type" });
        }

        var tableName = ResolveTableNameForType(resolvedType);
        if (!string.IsNullOrEmpty(tableName) && await Db.HasTableAsync(tableName))
        {
            var exists = await Db.ExecuteScalarAsync<int>(
                $"SELECT COUNT(1) FROM \"{tableName}\" WHERE id = @id",
                new { id = entity_id.Value });
            if (exists == 0)
            {
                return NotFound(new { message = "Entity not found." });
            }
        }

        var histories = await _auditService.GetHistoryAsync(resolvedType, entity_id.Value);
        return Ok(new Dictionary<string, object?>
        {
            ["entity_type"] = resolvedType,
            ["entity_id"] = entity_id.Value,
            ["histories"] = histories
        });
    }

    private static void EnrichHistoryRow(Dictionary<string, object?> row)
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
                ["name"] = row.GetValueOrDefault("user_name"),
                ["email"] = row.GetValueOrDefault("user_email")
            };
        }
        else
        {
            row["user"] = null;
        }

        row.Remove("user_name");
        row.Remove("user_email");
    }

    private static string? TryResolveEntityType(string type)
    {
        return type.Trim().ToLowerInvariant() switch
        {
            "sale_invoice" or "sale-invoice" or "saleinvoice" or "sale" or "app\\models\\saleinvoice" => "App\\Models\\SaleInvoice",
            "purchase_invoice" or "purchase-invoice" or "purchaseinvoice" or "purchase" or "app\\models\\purchaseinvoice" => "App\\Models\\PurchaseInvoice",
            "stock_adjustment" or "stock-adjustment" or "stockadjustment" or "app\\models\\stockadjustment" => "App\\Models\\StockAdjustment",
            "payment" or "app\\models\\payment" => "App\\Models\\Payment",
            "sale_return" or "sale-return" or "salereturn" or "app\\models\\salereturn" => "App\\Models\\SaleReturn",
            "purchase_return" or "purchase-return" or "purchasereturn" or "app\\models\\purchasereturn" => "App\\Models\\PurchaseReturn",
            "stock_disposal" or "stock-disposal" or "stockdisposal" or "app\\models\\stockdisposal" => "App\\Models\\StockDisposal",
            "stocktake_session" or "stocktake-session" or "stocktakesession" or "stocktake" or "app\\models\\stocktakesession" => "App\\Models\\StocktakeSession",
            _ => null
        };
    }

    private static string ResolveTableNameForType(string resolvedType)
    {
        return resolvedType switch
        {
            "App\\Models\\SaleInvoice" => "sale_invoices",
            "App\\Models\\PurchaseInvoice" => "purchase_invoices",
            "App\\Models\\StockAdjustment" => "stock_adjustments",
            "App\\Models\\Payment" => "payments",
            "App\\Models\\SaleReturn" => "sale_returns",
            "App\\Models\\PurchaseReturn" => "purchase_returns",
            "App\\Models\\StockDisposal" => "stock_disposals",
            "App\\Models\\StocktakeSession" => "stocktake_sessions",
            _ => ""
        };
    }
}

using System.Text.Json;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Authorize]
public class StocktakeController : ApiControllerBase
{
    private readonly StocktakeService _service;

    public StocktakeController(KarobarDb db) : base(db)
    {
        _service = new StocktakeService(db);
    }

    [HttpGet("api/stocktake-sessions/new-code")]
    public async Task<IActionResult> NewCode()
    {
        if (!await CanAsync("stocktake.create"))
        {
            return Forbidden();
        }

        var code = await _service.GenerateReferenceNoAsync(true);
        return Ok(new { reference_no = code });
    }

    [HttpGet("api/stocktake-sessions")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery(Name = "per_page")] int perPage = 50)
    {
        if (!await CanAsync("stocktake.view"))
        {
            return Forbidden();
        }

        var conditions = new List<string>();
        var dynParams = new DynamicParameters();

        var search = (q ?? "").Trim();
        if (!string.IsNullOrEmpty(search))
        {
            dynParams.Add("like", $"%{search}%");
            var subClauses = new List<string>
            {
                "reference_no LIKE @like",
                "notes LIKE @like"
            };

            if (await Db.HasTableAsync("brands"))
            {
                subClauses.Add("(scope_type = 'brand' AND scope_id IN (SELECT id FROM brands WHERE name LIKE @like))");
            }
            if (await Db.HasTableAsync("categories"))
            {
                subClauses.Add("(scope_type = 'category' AND scope_id IN (SELECT id FROM categories WHERE name LIKE @like))");
            }
            if (await Db.HasTableAsync("suppliers"))
            {
                subClauses.Add("(scope_type = 'supplier' AND scope_id IN (SELECT id FROM suppliers WHERE name LIKE @like))");
            }
            if (await Db.HasTableAsync("storage_locations"))
            {
                subClauses.Add("(scope_type = 'storage_location' AND scope_id IN (SELECT id FROM storage_locations WHERE name LIKE @like))");
            }

            conditions.Add("(" + string.Join(" OR ", subClauses) + ")");
        }

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            conditions.Add("status = @status");
            dynParams.Add("status", status.Trim());
        }

        var whereSql = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        var selectSql = $"SELECT * FROM stocktake_sessions {whereSql} ORDER BY id DESC";
        var countSql = $"SELECT COUNT(1) FROM stocktake_sessions {whereSql}";

        var paginated = await Db.PaginateAsync(
            selectSql,
            countSql,
            dynParams,
            page,
            perPage <= 0 ? 50 : perPage,
            rows => _service.EnrichSessionsBatchAsync(rows, false));

        return Ok(paginated);
    }

    [HttpGet("api/stocktake-sessions/{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("stocktake.view"))
        {
            return Forbidden();
        }

        var session = await _service.GetSessionByIdAsync(id, true);
        if (session == null)
        {
            return NotFound(new { message = "Stocktake session not found." });
        }

        var isBlind = session.GetValueOrDefault("is_blind") is true or 1 or 1L;
        var sStatus = session.GetValueOrDefault("status")?.ToString() ?? "";
        var canReveal = await CanAsync("stocktake.finalize") || !string.Equals(sStatus, "in_progress", StringComparison.OrdinalIgnoreCase);

        if (isBlind && !canReveal && session.TryGetValue("items", out var itemsObj) && itemsObj is List<Dictionary<string, object?>> itemsList)
        {
            MaskBlindItems(itemsList);
        }

        return Ok(session);
    }

    [HttpPost("api/stocktake-sessions")]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("stocktake.create"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var sessionType = (GetString(body, "session_type") ?? "").Trim().ToLowerInvariant();
        if (sessionType is not ("full" or "cycle"))
        {
            return Unprocessable("The session type must be full or cycle.", new Dictionary<string, string[]>
            {
                ["session_type"] = new[] { "The selected session type is invalid." }
            });
        }

        var scopeType = (GetString(body, "scope_type") ?? "").Trim();
        var scopeId = GetNullableLong(body, "scope_id");

        if (sessionType == "cycle")
        {
            if (scopeType is not ("category" or "brand" or "supplier" or "storage_location"))
            {
                return Unprocessable("A valid scope_type is required for cycle counts.", new Dictionary<string, string[]>
                {
                    ["scope_type"] = new[] { "The scope type field is required when session type is cycle." }
                });
            }
            if (scopeId == null || scopeId <= 0)
            {
                return Unprocessable("A valid scope_id is required for cycle counts.", new Dictionary<string, string[]>
                {
                    ["scope_id"] = new[] { "The scope id field is required when session type is cycle." }
                });
            }
        }

        var isBlind = body.ContainsKey("is_blind") ? GetBool(body, "is_blind", true) : true;
        var notes = GetString(body, "notes");

        var created = await _service.CreateSessionAsync(new Dictionary<string, object?>
        {
            ["session_type"] = sessionType,
            ["is_blind"] = isBlind,
            ["scope_type"] = string.IsNullOrEmpty(scopeType) ? null : scopeType,
            ["scope_id"] = scopeId,
            ["notes"] = notes
        }, CurrentUserId);

        return StatusCode(201, created);
    }

    [HttpPatch("api/stocktake-sessions/{id:long}/start")]
    public async Task<IActionResult> Start(long id)
    {
        if (!await CanAsync("stocktake.update"))
        {
            return Forbidden();
        }

        try
        {
            var started = await _service.StartSessionAsync(id);
            return Ok(started);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Stocktake session not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpPatch("api/stocktake-sessions/{id:long}/submit-review")]
    public async Task<IActionResult> SubmitForReview(long id)
    {
        if (!await CanAsync("stocktake.update"))
        {
            return Forbidden();
        }

        try
        {
            var reviewed = await _service.SubmitForReviewAsync(id);
            return Ok(reviewed);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Stocktake session not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpPost("api/stocktake-sessions/{id:long}/finalize")]
    public async Task<IActionResult> FinalizeSession(long id)
    {
        if (!await CanAsync("stocktake.finalize"))
        {
            return Forbidden();
        }

        try
        {
            var finalized = await _service.FinalizeSessionAsync(id, CurrentUserId);
            return Ok(finalized);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Stocktake session not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpPatch("api/stocktake-sessions/{id:long}/cancel")]
    public async Task<IActionResult> Cancel(long id)
    {
        if (!await CanAsync("stocktake.update"))
        {
            return Forbidden();
        }

        try
        {
            var cancelled = await _service.CancelSessionAsync(id);
            return Ok(cancelled);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Stocktake session not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpGet("api/stocktake-sessions/{id:long}/items")]
    public async Task<IActionResult> Items(
        long id,
        [FromQuery] string? status = "all",
        [FromQuery] bool reveal = false,
        [FromQuery] int page = 1,
        [FromQuery(Name = "per_page")] int perPage = 100)
    {
        if (!await CanAsync("stocktake.view"))
        {
            return Forbidden();
        }

        var session = await Db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_sessions WHERE id = @id", new { id });
        if (session == null)
        {
            return NotFound(new { message = "Stocktake session not found." });
        }

        var whereClauses = new List<string> { "stocktake_session_id = @id" };
        var dynParams = new DynamicParameters();
        dynParams.Add("id", id);

        if (!string.IsNullOrWhiteSpace(status) && status.Trim().ToLowerInvariant() is ("pending" or "counted"))
        {
            whereClauses.Add("status = @status");
            dynParams.Add("status", status.Trim().ToLowerInvariant());
        }

        var whereSql = "WHERE " + string.Join(" AND ", whereClauses);
        var selectSql = $"SELECT * FROM stocktake_items {whereSql} ORDER BY id ASC";
        var countSql = $"SELECT COUNT(1) FROM stocktake_items {whereSql}";

        var isBlind = session.GetValueOrDefault("is_blind") is true or 1 or 1L;
        var sStatus = session.GetValueOrDefault("status")?.ToString() ?? "";
        var canReveal = reveal && (await CanAsync("stocktake.finalize") || !string.Equals(sStatus, "in_progress", StringComparison.OrdinalIgnoreCase));

        var paginated = await Db.PaginateAsync(
            selectSql,
            countSql,
            dynParams,
            page,
            perPage <= 0 ? 100 : perPage,
            async rows =>
            {
                var enriched = await _service.EnrichItemsBatchAsync(rows);
                if (isBlind && !canReveal)
                {
                    MaskBlindItems(enriched);
                }
                return enriched;
            });

        return Ok(paginated);
    }

    [HttpPost("api/stocktake-sessions/{id:long}/items")]
    public async Task<IActionResult> AddItem(long id)
    {
        if (!await CanAsync("stocktake.update"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var productId = GetNullableLong(body, "product_id");
        if (productId == null || productId <= 0)
        {
            return Unprocessable("The product_id field is required.", new Dictionary<string, string[]>
            {
                ["product_id"] = new[] { "The product_id field is required." }
            });
        }

        var prodExists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM products WHERE id = @pid", new { pid = productId.Value });
        if (prodExists == 0)
        {
            return Unprocessable("The selected product does not exist.", new Dictionary<string, string[]>
            {
                ["product_id"] = new[] { "The selected product_id is invalid." }
            });
        }

        object? countedQty = null;
        if (body.TryGetValue("counted_qty", out var cqEl) && cqEl.ValueKind != JsonValueKind.Null && !string.IsNullOrWhiteSpace(GetString(body, "counted_qty")))
        {
            var cqVal = GetDecimal(body, "counted_qty", -1m);
            if (cqVal < 0m)
            {
                return Unprocessable("The counted_qty must be at least 0.", new Dictionary<string, string[]>
                {
                    ["counted_qty"] = new[] { "The counted_qty must be at least 0." }
                });
            }
            countedQty = cqVal;
        }

        try
        {
            var item = await _service.AddItemAsync(id, new Dictionary<string, object?>
            {
                ["product_id"] = productId.Value,
                ["batch_number"] = GetString(body, "batch_number"),
                ["expiry_date"] = GetString(body, "expiry_date"),
                ["counted_qty"] = countedQty,
                ["discrepancy_reason"] = GetString(body, "discrepancy_reason")
            }, CurrentUserId);

            return StatusCode(201, item);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Stocktake session not found." });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpPatch("api/stocktake-items/{item:long}")]
    public async Task<IActionResult> RecordCount(long item)
    {
        if (!await CanAsync("stocktake.update"))
        {
            return Forbidden();
        }

        var existingItem = await Db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_items WHERE id = @id", new { id = item });
        if (existingItem == null)
        {
            return NotFound(new { message = "Stocktake item not found." });
        }

        var body = await ReadJsonBodyAsync();
        if (!body.ContainsKey("counted_qty") || body["counted_qty"].ValueKind == JsonValueKind.Null)
        {
            return Unprocessable("The counted_qty field is required.", new Dictionary<string, string[]>
            {
                ["counted_qty"] = new[] { "The counted_qty field is required." }
            });
        }

        var countedQty = GetDecimal(body, "counted_qty", -1m);
        if (countedQty < 0m)
        {
            return Unprocessable("The counted_qty must be at least 0.", new Dictionary<string, string[]>
            {
                ["counted_qty"] = new[] { "The counted_qty must be at least 0." }
            });
        }

        try
        {
            var updated = await _service.RecordCountAsync(
                item,
                countedQty,
                GetString(body, "discrepancy_reason"),
                CurrentUserId);

            var sessionId = Convert.ToInt64(existingItem["stocktake_session_id"]);
            var session = await Db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_sessions WHERE id = @id", new { id = sessionId });
            if (session != null)
            {
                var isBlind = session.GetValueOrDefault("is_blind") is true or 1 or 1L;
                var sStatus = session.GetValueOrDefault("status")?.ToString() ?? "";
                var canReveal = await CanAsync("stocktake.finalize") || !string.Equals(sStatus, "in_progress", StringComparison.OrdinalIgnoreCase);

                if (isBlind && !canReveal)
                {
                    MaskBlindItems(new List<Dictionary<string, object?>> { updated });
                }
            }

            return Ok(updated);
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message);
        }
    }

    [HttpDelete("api/stocktake-sessions/{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("stocktake.delete"))
        {
            return Forbidden();
        }

        var session = await Db.QuerySingleOrDefaultAsync("SELECT * FROM stocktake_sessions WHERE id = @id", new { id });
        if (session == null)
        {
            return NotFound(new { message = "Stocktake session not found." });
        }

        var status = session.GetValueOrDefault("status")?.ToString() ?? "";
        if (status is not ("draft" or "cancelled"))
        {
            return Unprocessable("Only draft or cancelled sessions can be deleted.");
        }

        await Db.ExecuteAsync("DELETE FROM stocktake_items WHERE stocktake_session_id = @id", new { id });
        await Db.ExecuteAsync("DELETE FROM stocktake_sessions WHERE id = @id", new { id });

        SyncPulseService.Bump("stock");
        return NoContent();
    }

    private static void MaskBlindItems(List<Dictionary<string, object?>> items)
    {
        foreach (var item in items)
        {
            item["system_qty"] = null;
            item["variance_qty"] = null;
            item["variance_value"] = null;
            item["unit_purchase_price"] = null;
            item["unit_cost"] = null;
        }
    }
}

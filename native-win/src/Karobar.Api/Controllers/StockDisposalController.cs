using System.Text.Json;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/stock-disposals")]
[Authorize]
public class StockDisposalController : ApiControllerBase
{
    private readonly StockDisposalService _disposalService;

    public StockDisposalController(KarobarDb db) : base(db)
    {
        _disposalService = new StockDisposalService(db);
    }

    [HttpGet("new-code")]
    public async Task<IActionResult> NewCode([FromQuery] string? date = null)
    {
        if (!await CanAsync("stock-disposal.create"))
        {
            return Forbidden();
        }

        var code = await _disposalService.GenerateDisposalNumberAsync(date, true);
        return Ok(new
        {
            disposal_number = code,
            causes = StockDisposalService.Causes,
            methods = StockDisposalService.Methods
        });
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? status = null,
        [FromQuery] string? cause = null,
        [FromQuery(Name = "disposal_method")] string? disposalMethod = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null,
        [FromQuery(Name = "has_narcotics")] bool? hasNarcotics = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery(Name = "per_page")] int perPage = 20)
    {
        if (!await CanAsync("stock-disposal.view"))
        {
            return Forbidden();
        }

        var conditions = new List<string>();
        var dynParams = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(status) && !string.Equals(status, "all", StringComparison.OrdinalIgnoreCase))
        {
            conditions.Add("status = @status");
            dynParams.Add("status", status.Trim());
        }

        if (!string.IsNullOrWhiteSpace(cause) && !string.Equals(cause, "all", StringComparison.OrdinalIgnoreCase))
        {
            conditions.Add("cause = @cause");
            dynParams.Add("cause", cause.Trim());
        }

        if (!string.IsNullOrWhiteSpace(disposalMethod) && !string.Equals(disposalMethod, "all", StringComparison.OrdinalIgnoreCase))
        {
            conditions.Add("disposal_method = @disposalMethod");
            dynParams.Add("disposalMethod", disposalMethod.Trim());
        }

        if (!string.IsNullOrWhiteSpace(from))
        {
            conditions.Add("substr(disposal_date, 1, 10) >= @from");
            dynParams.Add("from", from.Trim());
        }

        if (!string.IsNullOrWhiteSpace(to))
        {
            conditions.Add("substr(disposal_date, 1, 10) <= @to");
            dynParams.Add("to", to.Trim());
        }

        if (hasNarcotics == true)
        {
            conditions.Add("has_narcotics = 1");
        }

        var s = (search ?? "").Trim();
        if (!string.IsNullOrEmpty(s))
        {
            dynParams.Add("like", $"%{s}%");
            conditions.Add(@"(
                disposal_number LIKE @like
                OR reference_no LIKE @like
                OR witness_name LIKE @like
                OR notes LIKE @like
                OR id IN (
                    SELECT sdi.stock_disposal_id
                    FROM stock_disposal_items sdi
                    LEFT JOIN products p ON p.id = sdi.product_id
                    WHERE sdi.batch_number LIKE @like
                       OR p.name LIKE @like
                       OR p.product_code LIKE @like
                )
            )");
        }

        var whereSql = conditions.Count > 0 ? "WHERE " + string.Join(" AND ", conditions) : "";
        var selectSql = $"SELECT * FROM stock_disposals {whereSql} ORDER BY disposal_date DESC, id DESC";
        var countSql = $"SELECT COUNT(1) FROM stock_disposals {whereSql}";

        var clampedPerPage = Math.Clamp(perPage <= 0 ? 20 : perPage, 5, 100);
        var paginated = await Db.PaginateAsync(
            selectSql,
            countSql,
            dynParams,
            page,
            clampedPerPage,
            rows => _disposalService.EnrichDisposalsBatchAsync(rows, false));

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1).ToString("yyyy-MM-dd");
        var monthEnd = new DateTime(now.Year, now.Month, DateTime.DaysInMonth(now.Year, now.Month)).ToString("yyyy-MM-dd");

        var quarantinedStats = await Db.QuerySingleOrDefaultAsync(@"
            SELECT COUNT(1) as count,
                   COALESCE(SUM(total_qty), 0) as qty,
                   COALESCE(SUM(total_cost_value), 0) as value
            FROM stock_disposals
            WHERE status = 'quarantined'");

        var disposedMonthStats = await Db.QuerySingleOrDefaultAsync(@"
            SELECT COUNT(1) as count,
                   COALESCE(SUM(total_qty), 0) as qty,
                   COALESCE(SUM(total_cost_value), 0) as value
            FROM stock_disposals
            WHERE status = 'disposed'
              AND substr(disposal_date, 1, 10) >= @monthStart
              AND substr(disposal_date, 1, 10) <= @monthEnd",
            new { monthStart, monthEnd });

        var allDisposedStats = await Db.QuerySingleOrDefaultAsync(@"
            SELECT COUNT(1) as count,
                   COALESCE(SUM(total_cost_value), 0) as value
            FROM stock_disposals
            WHERE status = 'disposed'");

        var narcoticsCount = await Db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1)
            FROM stock_disposals
            WHERE has_narcotics = 1
              AND status IN ('quarantined', 'disposed')");

        var summary = new Dictionary<string, object?>
        {
            ["quarantined_count"] = Convert.ToInt32(quarantinedStats?.GetValueOrDefault("count") ?? 0),
            ["quarantined_qty"] = StocktakeService.ToDecimal(quarantinedStats?.GetValueOrDefault("qty")),
            ["quarantined_value"] = Math.Round(StocktakeService.ToDecimal(quarantinedStats?.GetValueOrDefault("value")), 2),
            ["disposed_month_count"] = Convert.ToInt32(disposedMonthStats?.GetValueOrDefault("count") ?? 0),
            ["disposed_month_qty"] = StocktakeService.ToDecimal(disposedMonthStats?.GetValueOrDefault("qty")),
            ["disposed_month_value"] = Math.Round(StocktakeService.ToDecimal(disposedMonthStats?.GetValueOrDefault("value")), 2),
            ["total_disposed_count"] = Convert.ToInt32(allDisposedStats?.GetValueOrDefault("count") ?? 0),
            ["total_writeoff_value"] = Math.Round(StocktakeService.ToDecimal(allDisposedStats?.GetValueOrDefault("value")), 2),
            ["narcotic_disposals_count"] = narcoticsCount
        };

        return Ok(new
        {
            disposals = paginated,
            summary,
            causes = StockDisposalService.Causes,
            methods = StockDisposalService.Methods
        });
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("stock-disposal.view"))
        {
            return Forbidden();
        }

        var disposal = await _disposalService.GetDisposalByIdAsync(id);
        if (disposal == null)
        {
            return NotFound(new { message = "Stock disposal not found." });
        }

        return Ok(disposal);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("stock-disposal.create"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var disposalDate = (GetString(body, "disposal_date") ?? "").Trim();
        var status = (GetString(body, "status") ?? "").Trim().ToLowerInvariant();
        var cause = (GetString(body, "cause") ?? "").Trim();
        var disposalMethod = (GetString(body, "disposal_method") ?? "").Trim();

        if (string.IsNullOrEmpty(disposalDate))
        {
            return Unprocessable("The disposal date field is required.", new Dictionary<string, string[]>
            {
                ["disposal_date"] = new[] { "The disposal date field is required." }
            });
        }

        if (status is not ("quarantined" or "disposed"))
        {
            return Unprocessable("The selected status is invalid.", new Dictionary<string, string[]>
            {
                ["status"] = new[] { "The selected status is invalid." }
            });
        }

        if (string.IsNullOrEmpty(cause) || !StockDisposalService.Causes.ContainsKey(cause))
        {
            return Unprocessable("The selected cause is invalid.", new Dictionary<string, string[]>
            {
                ["cause"] = new[] { "The selected cause is invalid." }
            });
        }

        if (string.IsNullOrEmpty(disposalMethod) || !StockDisposalService.Methods.ContainsKey(disposalMethod))
        {
            return Unprocessable("The selected disposal method is invalid.", new Dictionary<string, string[]>
            {
                ["disposal_method"] = new[] { "The selected disposal method is invalid." }
            });
        }

        if (!body.TryGetValue("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array || itemsEl.GetArrayLength() == 0)
        {
            return Unprocessable("At least one item is required.", new Dictionary<string, string[]>
            {
                ["items"] = new[] { "The items field is required." }
            });
        }

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in body)
        {
            payload[kv.Key] = kv.Value;
        }

        try
        {
            var disposal = await _disposalService.CreateDisposalAsync(payload, CurrentUserId);
            var num = disposal.GetValueOrDefault("disposal_number")?.ToString() ?? "";
            var dStatus = disposal.GetValueOrDefault("status")?.ToString() ?? "";

            var message = string.Equals(dStatus, StockDisposalService.StatusDisposed, StringComparison.OrdinalIgnoreCase)
                ? $"Stock disposal {num} finalized and written off successfully."
                : $"Stock moved to Quarantine ({num}) and isolated from sellable inventory.";

            return StatusCode(201, new
            {
                message,
                disposal
            });
        }
        catch (StockDisposalValidationException ex)
        {
            return Unprocessable(ex.Message, ex.Errors);
        }
    }

    [HttpPost("{id:long}/dispose")]
    public async Task<IActionResult> DisposeVoucher(long id)
    {
        if (!await CanAsync("stock-disposal.dispose"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var disposalMethod = (GetString(body, "disposal_method") ?? "").Trim();
        if (string.IsNullOrEmpty(disposalMethod) || !StockDisposalService.Methods.ContainsKey(disposalMethod))
        {
            return Unprocessable("The selected disposal method is invalid.", new Dictionary<string, string[]>
            {
                ["disposal_method"] = new[] { "The selected disposal method is invalid." }
            });
        }

        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["disposal_method"] = disposalMethod
        };
        if (body.ContainsKey("witness_name")) data["witness_name"] = GetString(body, "witness_name");
        if (body.ContainsKey("witness_designation")) data["witness_designation"] = GetString(body, "witness_designation");
        if (body.ContainsKey("notes")) data["notes"] = GetString(body, "notes");
        if (body.ContainsKey("record_expense")) data["record_expense"] = GetBool(body, "record_expense", true);

        try
        {
            var updated = await _disposalService.FinalizeDisposalAsync(id, data, CurrentUserId);
            var num = updated.GetValueOrDefault("disposal_number")?.ToString() ?? "";
            return Ok(new
            {
                message = $"Quarantine voucher {num} has been permanently disposed and written off.",
                disposal = updated
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Stock disposal not found." });
        }
        catch (StockDisposalValidationException ex)
        {
            return Unprocessable(ex.Message, ex.Errors);
        }
    }

    [HttpPost("{id:long}/restore")]
    public async Task<IActionResult> Restore(long id)
    {
        if (!await CanAsync("stock-disposal.restore"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var reason = GetString(body, "reason");

        try
        {
            var updated = await _disposalService.RestoreFromQuarantineAsync(id, reason, CurrentUserId);
            var num = updated.GetValueOrDefault("disposal_number")?.ToString() ?? "";
            return Ok(new
            {
                message = $"Quarantine voucher {num} restored to sellable stock.",
                disposal = updated
            });
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Stock disposal not found." });
        }
        catch (StockDisposalValidationException ex)
        {
            return Unprocessable(ex.Message, ex.Errors);
        }
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("stock-disposal.delete"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT id, disposal_number FROM stock_disposals WHERE id = @id", new { id });
        if (existing == null)
        {
            return NotFound(new { message = "Stock disposal not found." });
        }

        var body = await ReadJsonBodyAsync();
        var reason = GetString(body, "reason") ?? Request.Query["reason"].FirstOrDefault();

        try
        {
            var num = existing.GetValueOrDefault("disposal_number")?.ToString() ?? "";
            await _disposalService.DeleteDisposalAsync(id, reason, CurrentUserId);
            return Ok(new
            {
                message = $"Stock disposal voucher {num} reversed and deleted."
            });
        }
        catch (StockDisposalValidationException ex)
        {
            return Unprocessable(ex.Message, ex.Errors);
        }
    }
}

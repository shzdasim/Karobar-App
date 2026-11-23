using System.Text.Json;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/trade-schemes")]
[Authorize]
public class TradeSchemeController : ApiControllerBase
{
    private readonly TradeSchemeService _schemeService;

    public TradeSchemeController(KarobarDb db) : base(db)
    {
        _schemeService = new TradeSchemeService(db);
    }

    private async Task<bool> CanViewSchemesAsync()
    {
        return await CanAsync(
            "trade-scheme.view",
            "trade-scheme.manage",
            "sale-invoice.view",
            "sale-invoice.create",
            "product.view",
            "barcode-pos.view",
            "barcode-pos.create");
    }

    [HttpGet("active-lookup")]
    public async Task<IActionResult> ActiveLookup(
        [FromQuery] string? sale_type = null,
        [FromQuery] long? customer_id = null,
        [FromQuery] string? date = null)
    {
        if (!await CanViewSchemesAsync())
        {
            return Forbidden("You do not have permission to view trade schemes.");
        }

        var checkDate = string.IsNullOrWhiteSpace(date) ? DateTime.UtcNow.ToString("yyyy-MM-dd") : date;
        var schemes = await _schemeService.GetActiveSchemesAsync(sale_type, customer_id, checkDate);
        return Ok(new Dictionary<string, object?>
        {
            ["schemes"] = schemes
        });
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? target_type = null,
        [FromQuery] string? sale_type = null,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 25)
    {
        if (!await CanViewSchemesAsync())
        {
            return Forbidden("You do not have permission to view trade schemes.");
        }

        if (!await Db.HasTableAsync("trade_schemes"))
        {
            if (per_page == -1) return Ok(new List<Dictionary<string, object?>>());
            return Ok(await Db.PaginateAsync("SELECT 1 WHERE 1=0", "SELECT 0", null, page, per_page));
        }

        var where = new List<string> { "1=1" };
        var dyn = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = "%" + search.Trim() + "%";
            where.Add(@"(
                ts.name LIKE @s
                OR ts.notes LIKE @s
                OR (ts.target_type = 'product' AND EXISTS (SELECT 1 FROM products p WHERE p.id = ts.target_id AND p.name LIKE @s))
                OR (ts.target_type = 'brand' AND EXISTS (SELECT 1 FROM brands b WHERE b.id = ts.target_id AND b.name LIKE @s))
                OR (ts.target_type = 'category' AND EXISTS (SELECT 1 FROM categories c WHERE c.id = ts.target_id AND c.name LIKE @s))
            )");
            dyn.Add("s", s);
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            if (status.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                where.Add("ts.is_active = 1 AND (ts.start_date IS NULL OR ts.start_date = '' OR date(ts.start_date) <= date(@today)) AND (ts.end_date IS NULL OR ts.end_date = '' OR date(ts.end_date) >= date(@today))");
                dyn.Add("today", today);
            }
            else if (status.Equals("inactive", StringComparison.OrdinalIgnoreCase))
            {
                where.Add("ts.is_active = 0");
            }
        }

        if (!string.IsNullOrWhiteSpace(target_type))
        {
            where.Add("ts.target_type = @targetType");
            dyn.Add("targetType", target_type.Trim());
        }

        if (!string.IsNullOrWhiteSpace(sale_type) && !sale_type.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            where.Add("(ts.sale_type = 'all' OR ts.sale_type = @saleType)");
            dyn.Add("saleType", sale_type.Trim());
        }

        var whereSql = string.Join(" AND ", where);
        var selectSql = $"SELECT ts.* FROM trade_schemes ts WHERE {whereSql} ORDER BY ts.priority DESC, ts.id DESC";
        var countSql = $"SELECT COUNT(1) FROM trade_schemes ts WHERE {whereSql}";

        if (per_page == -1)
        {
            var allRows = await Db.QueryListAsync(selectSql, dyn);
            await _schemeService.EnrichSchemesAsync(allRows);
            return Ok(allRows);
        }

        var paginated = await Db.PaginateAsync(selectSql, countSql, dyn, page, per_page, async rows =>
        {
            await _schemeService.EnrichSchemesAsync(rows);
            return rows;
        });

        return Ok(paginated);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanViewSchemesAsync())
        {
            return Forbidden("You do not have permission to view trade schemes.");
        }

        var scheme = await Db.QuerySingleOrDefaultAsync("SELECT * FROM trade_schemes WHERE id = @id", new { id });
        if (scheme == null) return NotFound(new { message = "Trade scheme not found." });

        await _schemeService.EnrichSchemesAsync(new List<Dictionary<string, object?>> { scheme });
        return Ok(scheme);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("trade-scheme.manage", "trade-scheme.create"))
        {
            return Forbidden("You do not have permission to manage trade schemes.");
        }

        var body = await ReadJsonBodyAsync();
        var (valid, errResult, payload, slabs) = await ValidateSchemePayloadAsync(body, isUpdate: false);
        if (!valid) return errResult!;

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var schemeId = await Db.InsertAsync("trade_schemes", payload!, conn, tx);
        foreach (var slab in slabs!)
        {
            slab["trade_scheme_id"] = schemeId;
            await Db.InsertAsync("trade_scheme_slabs", slab, conn, tx);
        }

        await tx.CommitAsync();

        var created = await Db.QuerySingleOrDefaultAsync("SELECT * FROM trade_schemes WHERE id = @id", new { id = schemeId });
        if (created != null)
        {
            await _schemeService.EnrichSchemesAsync(new List<Dictionary<string, object?>> { created });
        }

        return StatusCode(201, new Dictionary<string, object?>
        {
            ["message"] = "Trade scheme created successfully",
            ["scheme"] = created
        });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("trade-scheme.manage", "trade-scheme.update"))
        {
            return Forbidden("You do not have permission to manage trade schemes.");
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM trade_schemes WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Trade scheme not found." });

        var body = await ReadJsonBodyAsync();
        var (valid, errResult, payload, slabs) = await ValidateSchemePayloadAsync(body, isUpdate: true, existing: existing);
        if (!valid) return errResult!;

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await Db.UpdateAsync("trade_schemes", id, payload!, "id", conn, tx);
        await Db.ExecuteAsync("DELETE FROM trade_scheme_slabs WHERE trade_scheme_id = @id", new { id }, conn, tx);

        foreach (var slab in slabs!)
        {
            slab["trade_scheme_id"] = id;
            await Db.InsertAsync("trade_scheme_slabs", slab, conn, tx);
        }

        await tx.CommitAsync();

        var updated = await Db.QuerySingleOrDefaultAsync("SELECT * FROM trade_schemes WHERE id = @id", new { id });
        if (updated != null)
        {
            await _schemeService.EnrichSchemesAsync(new List<Dictionary<string, object?>> { updated });
        }

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = "Trade scheme updated successfully",
            ["scheme"] = updated
        });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("trade-scheme.manage", "trade-scheme.delete"))
        {
            return Forbidden("You do not have permission to manage trade schemes.");
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM trade_schemes WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Trade scheme not found." });

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        if (await Db.HasTableAsync("trade_scheme_slabs"))
        {
            await Db.ExecuteAsync("DELETE FROM trade_scheme_slabs WHERE trade_scheme_id = @id", new { id }, conn, tx);
        }
        await Db.ExecuteAsync("DELETE FROM trade_schemes WHERE id = @id", new { id }, conn, tx);

        await tx.CommitAsync();

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = "Trade scheme deleted successfully"
        });
    }

    [HttpPost("{id:long}/toggle-active")]
    public async Task<IActionResult> ToggleActive(long id)
    {
        if (!await CanAsync("trade-scheme.manage", "trade-scheme.update"))
        {
            return Forbidden("You do not have permission to manage trade schemes.");
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM trade_schemes WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Trade scheme not found." });

        var currentActive = existing.GetValueOrDefault("is_active") is true or 1 or 1L;
        var nextActive = !currentActive;

        await Db.UpdateAsync("trade_schemes", id, new Dictionary<string, object?>
        {
            ["is_active"] = nextActive ? 1 : 0
        });

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = nextActive ? "Scheme activated" : "Scheme deactivated",
            ["is_active"] = nextActive
        });
    }

    private async Task<(bool Valid, IActionResult? ErrorResult, Dictionary<string, object?>? Payload, List<Dictionary<string, object?>>? Slabs)> ValidateSchemePayloadAsync(
        Dictionary<string, JsonElement> body,
        bool isUpdate,
        Dictionary<string, object?>? existing = null)
    {
        var name = GetString(body, "name")?.Trim() ?? "";
        var schemeType = GetString(body, "scheme_type")?.Trim() ?? "";
        var targetType = GetString(body, "target_type")?.Trim() ?? "";
        var targetId = GetNullableLong(body, "target_id") ?? 0;
        var bonusType = GetString(body, "bonus_type")?.Trim() ?? "";
        var saleType = GetString(body, "sale_type")?.Trim() ?? "";
        var customerId = GetNullableLong(body, "customer_id");
        var startDate = GetString(body, "start_date");
        var endDate = GetString(body, "end_date");
        var notes = GetString(body, "notes");
        var priority = GetInt(body, "priority", 0);

        bool isActive = true;
        if (body.ContainsKey("is_active"))
        {
            isActive = GetBool(body, "is_active", true);
        }
        else if (isUpdate && existing != null)
        {
            isActive = existing.GetValueOrDefault("is_active") is true or 1 or 1L;
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrEmpty(name)) errors["name"] = new[] { "The name field is required." };
        if (schemeType is not ("ratio" or "slab")) errors["scheme_type"] = new[] { "The selected scheme type is invalid." };
        if (targetType is not ("product" or "brand" or "category")) errors["target_type"] = new[] { "The selected target type is invalid." };
        if (targetId <= 0) errors["target_id"] = new[] { "The target id field is required." };
        if (bonusType is not ("pack" or "unit")) errors["bonus_type"] = new[] { "The selected bonus type is invalid." };
        if (saleType is not ("all" or "wholesale" or "retail")) errors["sale_type"] = new[] { "The selected sale type is invalid." };

        var slabs = new List<Dictionary<string, object?>>();
        if (!body.TryGetValue("slabs", out var slabsEl) || slabsEl.ValueKind != JsonValueKind.Array || slabsEl.GetArrayLength() == 0)
        {
            errors["slabs"] = new[] { "At least one slab is required." };
        }
        else
        {
            int idx = 0;
            foreach (var item in slabsEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    var slabDict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in item.EnumerateObject()) slabDict[p.Name] = p.Value;

                    var minQty = GetDecimal(slabDict, "min_qty", 0m);
                    decimal? maxQty = slabDict.TryGetValue("max_qty", out var mqEl) && mqEl.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined) && !string.IsNullOrWhiteSpace(mqEl.ToString())
                        ? GetDecimal(slabDict, "max_qty", 0m)
                        : null;
                    var bonusQty = GetDecimal(slabDict, "bonus_qty", 0m);
                    var bonusProductId = GetNullableLong(slabDict, "bonus_product_id");

                    if (minQty < 0.01m) errors[$"slabs.{idx}.min_qty"] = new[] { "Minimum quantity must be at least 0.01." };
                    if (bonusQty < 0.01m) errors[$"slabs.{idx}.bonus_qty"] = new[] { "Bonus quantity must be at least 0.01." };
                    if (maxQty.HasValue && maxQty.Value < minQty)
                    {
                        errors[$"slabs.{idx}.max_qty"] = new[] { "Max quantity must be greater than or equal to min quantity." };
                    }

                    slabs.Add(new Dictionary<string, object?>
                    {
                        ["min_qty"] = (double)minQty,
                        ["max_qty"] = maxQty.HasValue ? (double)maxQty.Value : null,
                        ["bonus_qty"] = (double)bonusQty,
                        ["bonus_product_id"] = bonusProductId
                    });
                }
                idx++;
            }
        }

        if (errors.Count > 0)
        {
            return (false, Unprocessable("The given data was invalid.", errors), null, null);
        }

        var targetTable = targetType switch
        {
            "product" => "products",
            "brand" => "brands",
            "category" => "categories",
            _ => ""
        };

        if (!string.IsNullOrEmpty(targetTable) && await Db.HasTableAsync(targetTable))
        {
            var exists = await Db.ExecuteScalarAsync<int>($"SELECT COUNT(1) FROM \"{targetTable}\" WHERE id = @id", new { id = targetId });
            if (exists == 0)
            {
                return (false, Unprocessable($"Selected {targetType} does not exist."), null, null);
            }
        }

        var payload = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["scheme_type"] = schemeType,
            ["target_type"] = targetType,
            ["target_id"] = targetId,
            ["bonus_type"] = bonusType,
            ["customer_id"] = customerId,
            ["sale_type"] = saleType,
            ["start_date"] = string.IsNullOrWhiteSpace(startDate) ? null : startDate,
            ["end_date"] = string.IsNullOrWhiteSpace(endDate) ? null : endDate,
            ["is_active"] = isActive ? 1 : 0,
            ["priority"] = priority,
            ["notes"] = string.IsNullOrWhiteSpace(notes) ? null : notes
        };

        return (true, null, payload, slabs);
    }
}

using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/formulations")]
public class FormulationController : ApiControllerBase
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

    private static readonly string[] SavePermissions =
    [
        "product.create", "product.update", "product.import"
    ];

    public FormulationController(KarobarDb db) : base(db)
    {
    }

    [HttpGet]
    [HttpGet("search")]
    public async Task<IActionResult> Index(
        [FromQuery] string? q = null,
        [FromQuery(Name = "q_name")] string? qName = null,
        [FromQuery] int limit = 0)
    {
        if (!await CanAsync(LookupPermissions)) return Forbidden();

        var search = ProductImportService.NormalizeFormulationName(q ?? qName ?? "");
        var qLower = search.ToLowerInvariant();

        var map = new Dictionary<string, Dictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);

        if (await Db.HasTableAsync("formulations"))
        {
            var rows = await Db.QueryListAsync("SELECT id, name, normalized_name FROM formulations ORDER BY name ASC");
            foreach (var r in rows)
            {
                var clean = ProductImportService.NormalizeFormulationName(r["name"]?.ToString());
                if (string.IsNullOrEmpty(clean)) continue;
                var key = (r["normalized_name"]?.ToString() ?? "").Trim();
                if (string.IsNullOrEmpty(key)) key = clean.ToLowerInvariant();

                if (!map.ContainsKey(key))
                {
                    map[key] = new Dictionary<string, object?>
                    {
                        ["id"] = r["id"],
                        ["name"] = clean
                    };
                }
            }
        }

        if (await Db.HasTableAsync("products") && await Db.HasColumnAsync("products", "formulation"))
        {
            var prodRows = await Db.QueryListAsync(
                "SELECT DISTINCT formulation FROM products WHERE formulation IS NOT NULL AND formulation != ''");

            foreach (var pr in prodRows)
            {
                var clean = ProductImportService.NormalizeFormulationName(pr["formulation"]?.ToString());
                if (string.IsNullOrEmpty(clean)) continue;
                var key = clean.ToLowerInvariant();
                if (!map.ContainsKey(key))
                {
                    long? id = null;
                    if (await Db.HasTableAsync("formulations"))
                    {
                        try
                        {
                            id = await Db.InsertAsync("formulations", new Dictionary<string, object?>
                            {
                                ["name"] = clean,
                                ["normalized_name"] = key
                            });
                        }
                        catch
                        {
                            id = await Db.ExecuteScalarAsync<long?>(
                                "SELECT id FROM formulations WHERE normalized_name = @key OR LOWER(name) = @key LIMIT 1",
                                new { key });
                        }
                    }

                    map[key] = new Dictionary<string, object?>
                    {
                        ["id"] = id,
                        ["name"] = clean
                    };
                }
            }
        }

        var items = map.Values.ToList();

        if (!string.IsNullOrEmpty(qLower))
        {
            items = items
                .Where(item => (item["name"]?.ToString() ?? "").Contains(qLower, StringComparison.OrdinalIgnoreCase))
                .OrderBy(item =>
                {
                    var nameLower = (item["name"]?.ToString() ?? "").ToLowerInvariant();
                    return nameLower == qLower ? 0 : 1;
                })
                .ThenBy(item =>
                {
                    var nameLower = (item["name"]?.ToString() ?? "").ToLowerInvariant();
                    return nameLower.StartsWith(qLower, StringComparison.Ordinal) ? 0 : 1;
                })
                .ThenBy(item => item["name"]?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else
        {
            items = items
                .OrderBy(item => item["name"]?.ToString() ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (limit > 0)
        {
            items = items.Take(Math.Min(limit, 5000)).ToList();
        }

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync(SavePermissions)) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var rawName = GetString(body, "name") ?? "";
        var clean = ProductImportService.NormalizeFormulationName(rawName);

        if (string.IsNullOrEmpty(clean))
        {
            return Unprocessable("Formulation name cannot be empty.", new Dictionary<string, string[]>
            {
                ["name"] = ["Formulation name cannot be empty."]
            });
        }

        var key = clean.ToLowerInvariant();
        Dictionary<string, object?>? existing = null;
        if (await Db.HasTableAsync("formulations"))
        {
            existing = await Db.QuerySingleOrDefaultAsync(
                "SELECT id, name FROM formulations WHERE normalized_name = @key OR LOWER(name) = @key LIMIT 1",
                new { key });
        }

        if (existing != null)
        {
            return Ok(new
            {
                id = existing["id"],
                name = existing["name"]?.ToString() ?? clean,
                is_existing = true
            });
        }

        long? newId = null;
        if (await Db.HasTableAsync("formulations"))
        {
            newId = await Db.InsertAsync("formulations", new Dictionary<string, object?>
            {
                ["name"] = clean,
                ["normalized_name"] = key
            });
        }

        return StatusCode(201, new
        {
            id = newId,
            name = clean,
            is_existing = false
        });
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync(SavePermissions)) return Forbidden();

        var formulation = await Db.QuerySingleOrDefaultAsync("SELECT * FROM formulations WHERE id = @id", new { id });
        if (formulation == null) return NotFound(new { message = "Formulation not found." });

        var oldName = formulation["name"]?.ToString() ?? "";
        var body = await ReadJsonBodyAsync();
        var rawName = GetString(body, "name") ?? "";
        var clean = ProductImportService.NormalizeFormulationName(rawName);

        if (string.IsNullOrEmpty(clean))
        {
            return Unprocessable("Formulation name cannot be empty.", new Dictionary<string, string[]>
            {
                ["name"] = ["Formulation name cannot be empty."]
            });
        }

        var key = clean.ToLowerInvariant();
        var duplicate = await Db.QuerySingleOrDefaultAsync(
            "SELECT * FROM formulations WHERE id != @id AND (normalized_name = @key OR LOWER(name) = @key) LIMIT 1",
            new { id, key });

        if (duplicate != null)
        {
            var dupName = duplicate["name"]?.ToString() ?? clean;
            return Unprocessable($"A formulation named '{dupName}' already exists.", new Dictionary<string, string[]>
            {
                ["name"] = [$"A formulation named '{dupName}' already exists."]
            });
        }

        await Db.UpdateAsync("formulations", id, new Dictionary<string, object?>
        {
            ["name"] = clean,
            ["normalized_name"] = key
        });

        if (await Db.HasTableAsync("products") && await Db.HasColumnAsync("products", "formulation"))
        {
            await Db.ExecuteAsync(
                "UPDATE products SET formulation = @clean WHERE LOWER(formulation) = @oldLower",
                new { clean, oldLower = oldName.ToLowerInvariant() });
        }

        return Ok(new
        {
            id,
            name = clean,
            message = "Formulation updated successfully."
        });
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync(SavePermissions)) return Forbidden();

        var formulation = await Db.QuerySingleOrDefaultAsync("SELECT * FROM formulations WHERE id = @id", new { id });
        if (formulation == null) return NotFound(new { message = "Formulation not found." });

        var formName = formulation["name"]?.ToString() ?? "";

        if (await Db.HasTableAsync("products") && await Db.HasColumnAsync("products", "formulation"))
        {
            var inUseCount = await Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM products WHERE LOWER(formulation) = @lower",
                new { lower = formName.ToLowerInvariant() });

            if (inUseCount > 0)
            {
                return Unprocessable($"Cannot delete '{formName}' because it is assigned to {inUseCount} product(s).");
            }
        }

        await Db.ExecuteAsync("DELETE FROM formulations WHERE id = @id", new { id });
        return Ok(new { message = "Formulation deleted successfully." });
    }
}

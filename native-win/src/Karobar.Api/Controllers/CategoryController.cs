using System.Text;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/categories")]
public class CategoryController : ApiControllerBase
{
    private static readonly string[] LookupPermissions =
    [
        "category.view", "product.view", "product.create", "product.update",
        "barcode.view", "barcode.print", "batch-expiry.view",
        "stocktake.view", "stocktake.create", "stocktake.update",
        "report.current-stock.view"
    ];

    public CategoryController(KarobarDb db) : base(db)
    {
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string? q = "", [FromQuery] int limit = 50)
    {
        if (!await CanAsync(LookupPermissions)) return Forbidden();

        q = (q ?? "").Trim();
        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 2000);

        var sql = "SELECT id, name FROM categories";
        var param = new DynamicParameters();
        if (!string.IsNullOrEmpty(q))
        {
            sql += " WHERE name LIKE @q";
            param.Add("q", $"%{q}%");
        }
        sql += " ORDER BY name ASC LIMIT @limit";
        param.Add("limit", limit);

        var items = await Db.QueryListAsync(sql, param);
        return Ok(items);
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await CanAsync("category.view")) return Forbidden();

        var items = await Db.QueryListAsync(@"
            SELECT c.*, (SELECT COUNT(1) FROM products p WHERE p.category_id = c.id) AS products_count
            FROM categories c
            ORDER BY c.name ASC");

        return Ok(items);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("category.create")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";

        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name field is required."]
            });
        }

        var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM categories WHERE name = @name", new { name });
        if (exists > 0)
        {
            return Unprocessable("The name has already been taken.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name has already been taken."]
            });
        }

        var id = await Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = name });
        var category = await Db.QuerySingleOrDefaultAsync("SELECT * FROM categories WHERE id = @id", new { id });

        return StatusCode(201, category);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("category.view")) return Forbidden();

        var category = await Db.QuerySingleOrDefaultAsync("SELECT * FROM categories WHERE id = @id", new { id });
        if (category == null) return NotFound(new { message = "Category not found." });

        return Ok(category);
    }

    [HttpPut("{id:long}")]
    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("category.update")) return Forbidden();

        var category = await Db.QuerySingleOrDefaultAsync("SELECT * FROM categories WHERE id = @id", new { id });
        if (category == null) return NotFound(new { message = "Category not found." });

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";

        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name field is required."]
            });
        }

        var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM categories WHERE name = @name AND id != @id", new { name, id });
        if (exists > 0)
        {
            return Unprocessable("The name has already been taken.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name has already been taken."]
            });
        }

        await Db.UpdateAsync("categories", id, new Dictionary<string, object?> { ["name"] = name });
        var fresh = await Db.QuerySingleOrDefaultAsync("SELECT * FROM categories WHERE id = @id", new { id });
        return Ok(fresh);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("category.delete")) return Forbidden();

        var category = await Db.QuerySingleOrDefaultAsync("SELECT * FROM categories WHERE id = @id", new { id });
        if (category == null) return NotFound(new { message = "Category not found." });

        var prodCount = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM products WHERE category_id = @id", new { id });
        if (prodCount > 0)
        {
            return Unprocessable("Cannot delete: category is used by one or more products.");
        }

        await Db.ExecuteAsync("DELETE FROM categories WHERE id = @id", new { id });
        return NoContent();
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        if (!await CanAsync("category.export")) return Forbidden();

        var rows = await Db.QueryListAsync("SELECT name FROM categories ORDER BY name ASC");
        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("name");

        foreach (var c in rows)
        {
            sb.AppendLine(BrandController.EscapeCsv(BrandController.SanitizeCsvCell(c["name"])));
        }

        var fileName = $"categories_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=UTF-8", fileName);
    }
}

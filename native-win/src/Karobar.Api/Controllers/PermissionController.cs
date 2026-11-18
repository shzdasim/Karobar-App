using Karobar.Api.Infrastructure;
using Karobar.Api.Middleware;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/permissions")]
[Authorize]
public class PermissionController : ApiControllerBase
{
    private static readonly string[] CorePermissionPrefixes =
    {
        "user.", "role.", "permission.", "product.", "invoice.",
        "category.", "brand.", "supplier.", "customer.",
        "sale-invoice.", "purchase-invoice.", "sale-return.", "purchase-return.",
        "stock-adjustment.", "stock-disposal.", "stocktake.", "payment.",
        "customer-ledger.", "supplier-ledger.",
        "purchase-order.", "user-demands.", "report.", "settings.", "backup.",
        "account.", "cash-drawer.", "expense.",
        "barcode.", "barcode-pos.", "batch-expiry."
    };

    public PermissionController(KarobarDb db) : base(db)
    {
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? search = null)
    {
        if (!await CanAsync(
                "permission.view", "role.view", "role.create", "role.update", "role.sync.permissions",
                "user.view", "user.create", "user.update", "user.assign.roles", "user.assign.permissions", "user.manage"))
        {
            return Forbidden();
        }

        var term = (search ?? "").Trim();
        var sql = "SELECT name FROM permissions WHERE guard_name = 'sanctum'";
        if (!string.IsNullOrEmpty(term))
        {
            sql += " AND name LIKE @like";
        }
        sql += " ORDER BY name ASC";

        var rows = await Db.QueryListAsync(sql, new { like = $"%{term}%" });
        var names = rows.Select(r => r.GetValueOrDefault("name")?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();
        return Ok(names);
    }

    [HttpPost("")]
    [SpatiePermissionAuthorizationFilter("permission.create")]
    public async Task<IActionResult> Store()
    {
        var body = await ReadJsonBodyAsync();
        var name = (GetString(body, "name") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 150)
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = new[] { "The name field is required." }
            });
        }

        var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM permissions WHERE LOWER(name) = LOWER(@name)", new { name }) > 0;
        if (exists)
        {
            return Unprocessable("The name has already been taken.", new Dictionary<string, string[]>
            {
                ["name"] = new[] { "The name has already been taken." }
            });
        }

        var id = await Db.InsertAsync("permissions", new Dictionary<string, object?>
        {
            ["name"] = name,
            ["guard_name"] = "sanctum"
        });

        var created = await Db.QuerySingleOrDefaultAsync("SELECT * FROM permissions WHERE id = @id", new { id });
        return StatusCode(201, created);
    }

    [HttpDelete("{id:long}")]
    [SpatiePermissionAuthorizationFilter("permission.delete")]
    public async Task<IActionResult> Destroy(long id)
    {
        var perm = await Db.QuerySingleOrDefaultAsync("SELECT * FROM permissions WHERE id = @id", new { id });
        if (perm == null) return NotFound();

        var permName = perm.GetValueOrDefault("name")?.ToString() ?? "";
        if (CorePermissionPrefixes.Any(prefix => permName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return Unprocessable($"Cannot delete core system permission '{permName}'.");
        }

        await Db.ExecuteAsync("DELETE FROM role_has_permissions WHERE permission_id = @id", new { id });
        await Db.ExecuteAsync("DELETE FROM model_has_permissions WHERE permission_id = @id", new { id });
        await Db.ExecuteAsync("DELETE FROM permissions WHERE id = @id", new { id });

        return NoContent();
    }
}

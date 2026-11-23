using System.Text.Json;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/roles")]
[Authorize]
public class RoleController : ApiControllerBase
{
    public RoleController(KarobarDb db) : base(db)
    {
    }

    private static bool IsReservedAdminRoleName(string name)
    {
        var n = (name ?? "").Trim().ToLowerInvariant();
        return n is "admin" or "super-admin" or "super admin";
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int per_page = 15)
    {
        if (!await CanAsync("role.view", "user.view", "user.create", "user.update", "user.assign.roles", "user.manage"))
        {
            return Forbidden();
        }

        var term = (search ?? "").Trim();
        var where = "WHERE r.guard_name = 'sanctum'";
        if (!string.IsNullOrEmpty(term))
        {
            where += " AND r.name LIKE @like";
        }

        var selectSql = $@"
            SELECT r.id, r.name, r.guard_name,
                   (SELECT COUNT(1) FROM role_has_permissions rhp WHERE rhp.role_id = r.id) AS permissions_count
            FROM roles r
            {where}
            ORDER BY r.id DESC";

        var countSql = $"SELECT COUNT(1) FROM roles r {where}";
        var paged = await Db.PaginateAsync(selectSql, countSql, new { like = $"%{term}%" }, page, per_page);
        return Ok(paged);
    }

    [HttpPost("")]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("role.create")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var name = (GetString(body, "name") ?? "").Trim();
        var permissions = GetStringList(body, "permissions")
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var callerIsAdmin = await IsAdminAsync();

        if (string.IsNullOrWhiteSpace(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = new[] { "The name field is required." }
            });
        }

        if (IsReservedAdminRoleName(name))
        {
            return Unprocessable("Cannot create a role with a reserved Admin role name.");
        }

        if (permissions.Count > 0 && !callerIsAdmin)
        {
            return Forbidden("Only an Admin can assign or sync role permissions.");
        }

        var exists = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM roles WHERE LOWER(name) = LOWER(@name) AND guard_name = 'sanctum'",
            new { name }) > 0;
        if (exists)
        {
            return Unprocessable("The name has already been taken.", new Dictionary<string, string[]>
            {
                ["name"] = new[] { "The name has already been taken." }
            });
        }

        var roleId = await Db.InsertAsync("roles", new Dictionary<string, object?>
        {
            ["name"] = name,
            ["guard_name"] = "sanctum"
        });

        if (permissions.Count > 0 && callerIsAdmin)
        {
            await SyncRolePermissionsInternalAsync(roleId, permissions);
        }

        var created = await GetRoleWithPermissionsAsync(roleId);
        return StatusCode(201, created);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("role.view", "role.update", "role.sync.permissions")) return Forbidden();

        var role = await Db.QuerySingleOrDefaultAsync("SELECT * FROM roles WHERE id = @id AND guard_name = 'sanctum'", new { id });
        if (role == null) return NotFound();

        var permRows = await Db.QueryListAsync(@"
            SELECT p.name
            FROM permissions p
            INNER JOIN role_has_permissions rhp ON rhp.permission_id = p.id
            WHERE rhp.role_id = @id
            ORDER BY p.name ASC",
            new { id });

        var permNames = permRows.Select(r => r.GetValueOrDefault("name")?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList();

        return Ok(new
        {
            id = Convert.ToInt64(role["id"]),
            name = role.GetValueOrDefault("name")?.ToString() ?? "",
            guard_name = role.GetValueOrDefault("guard_name")?.ToString() ?? "sanctum",
            permissions = permNames
        });
    }

    [HttpPut("{id:long}/permissions")]
    public async Task<IActionResult> SyncPermissions(long id)
    {
        if (!await CanAsync("role.sync.permissions", "role.update")) return Forbidden();
        var role = await Db.QuerySingleOrDefaultAsync("SELECT * FROM roles WHERE id = @id AND guard_name = 'sanctum'", new { id });
        if (role == null) return NotFound();

        var currentName = role.GetValueOrDefault("name")?.ToString() ?? "";
        if (IsReservedAdminRoleName(currentName) && !await IsAdminAsync())
        {
            return Forbidden("Only an Admin can modify permissions on the Admin role.");
        }
        if (!await IsAdminAsync())
        {
            return Forbidden("Only an Admin can sync role permissions.");
        }

        var body = await ReadJsonBodyAsync();
        var permissions = new List<string>();
        if (body.TryGetValue("permissions", out var permsEl) && permsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in permsEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    permissions.Add(item.GetString()!.Trim());
            }
        }
        await SyncRolePermissionsInternalAsync(id, permissions);
        return Ok(await GetRoleWithPermissionsAsync(id));
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("role.update", "role.sync.permissions")) return Forbidden();

        var role = await Db.QuerySingleOrDefaultAsync("SELECT * FROM roles WHERE id = @id AND guard_name = 'sanctum'", new { id });
        if (role == null) return NotFound();

        var currentName = role.GetValueOrDefault("name")?.ToString() ?? "";
        var body = await ReadJsonBodyAsync();
        var newName = (GetString(body, "name") ?? currentName).Trim();
        var hasPerms = body.ContainsKey("permissions");
        var callerIsAdmin = await IsAdminAsync();
        var requestedPerms = hasPerms
            ? GetStringList(body, "permissions")
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (IsReservedAdminRoleName(currentName))
        {
            if (!string.Equals(newName, currentName, StringComparison.Ordinal))
            {
                return Unprocessable("Cannot rename Admin role");
            }
            if (!callerIsAdmin)
            {
                return Forbidden("Only an Admin can modify the Admin role");
            }
        }
        else if (IsReservedAdminRoleName(newName))
        {
            return Unprocessable("Cannot rename a role to a reserved Admin role name.");
        }
        else if (!callerIsAdmin && hasPerms)
        {
            var existingPermRows = await Db.QueryListAsync(@"
                SELECT p.name
                FROM permissions p
                INNER JOIN role_has_permissions rhp ON rhp.permission_id = p.id
                WHERE rhp.role_id = @id",
                new { id });
            var existingPerms = existingPermRows
                .Select(r => r.GetValueOrDefault("name")?.ToString()?.Trim() ?? "")
                .Where(s => !string.IsNullOrEmpty(s))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!requestedPerms.SetEquals(existingPerms))
            {
                return Forbidden("Only an Admin can sync role permissions.");
            }
        }

        var conflict = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM roles WHERE LOWER(name) = LOWER(@newName) AND guard_name = 'sanctum' AND id != @id",
            new { newName, id }) > 0;
        if (conflict)
        {
            return Unprocessable("The name has already been taken.");
        }

        await Db.UpdateAsync("roles", id, new Dictionary<string, object?> { ["name"] = newName });

        if (IsReservedAdminRoleName(newName))
        {
            var allPerms = await Db.QueryListAsync("SELECT name FROM permissions WHERE guard_name = 'sanctum'");
            var allNames = allPerms.Select(p => p.GetValueOrDefault("name")?.ToString() ?? "").Where(s => !string.IsNullOrEmpty(s));
            await SyncRolePermissionsInternalAsync(id, allNames);
        }
        else if (hasPerms && callerIsAdmin)
        {
            await SyncRolePermissionsInternalAsync(id, requestedPerms);
        }

        return Ok(await GetRoleWithPermissionsAsync(id));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("role.delete")) return Forbidden();

        var role = await Db.QuerySingleOrDefaultAsync("SELECT * FROM roles WHERE id = @id AND guard_name = 'sanctum'", new { id });
        if (role == null) return NotFound();

        var name = role.GetValueOrDefault("name")?.ToString() ?? "";
        if (IsReservedAdminRoleName(name))
        {
            return Unprocessable("Cannot delete Admin role");
        }

        await Db.ExecuteAsync("DELETE FROM role_has_permissions WHERE role_id = @id", new { id });
        await Db.ExecuteAsync("DELETE FROM model_has_roles WHERE role_id = @id", new { id });
        await Db.ExecuteAsync("DELETE FROM roles WHERE id = @id", new { id });

        return Ok(new { message = "Deleted" });
    }

    private async Task SyncRolePermissionsInternalAsync(long roleId, IEnumerable<string> permNames)
    {
        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync("DELETE FROM role_has_permissions WHERE role_id = @roleId", new { roleId }, tx);

        var distinct = permNames.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct().ToArray();
        if (distinct.Length > 0)
        {
            var perms = await conn.QueryAsync<(long id, string name)>(
                "SELECT id, name FROM permissions WHERE guard_name = 'sanctum' AND name IN @names",
                new { names = distinct },
                tx);

            foreach (var p in perms)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO role_has_permissions (permission_id, role_id) VALUES (@permId, @roleId)",
                    new { permId = p.id, roleId },
                    tx);
            }
        }

        await tx.CommitAsync();
    }

    private async Task<Dictionary<string, object?>?> GetRoleWithPermissionsAsync(long roleId)
    {
        var role = await Db.QuerySingleOrDefaultAsync("SELECT * FROM roles WHERE id = @roleId", new { roleId });
        if (role == null) return null;

        role["permissions"] = await Db.QueryListAsync(@"
            SELECT p.*
            FROM permissions p
            INNER JOIN role_has_permissions rhp ON rhp.permission_id = p.id
            WHERE rhp.role_id = @roleId
            ORDER BY p.name ASC",
            new { roleId });

        return role;
    }
}

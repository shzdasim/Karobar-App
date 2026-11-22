using System.Text.Json;
using Karobar.Api.Infrastructure;
using Karobar.Api.Middleware;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/users")]
[Authorize]
public class UserController : ApiControllerBase
{
    private readonly OperatorPinService _pinService;

    public UserController(KarobarDb db, OperatorPinService pinService) : base(db)
    {
        _pinService = pinService;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index([FromQuery] string? search = null, [FromQuery] int page = 1, [FromQuery] int per_page = 15)
    {
        if (!await CanManageOrAsync("user.view")) return Forbidden();

        var where = "WHERE 1=1";
        var term = (search ?? "").Trim();
        if (!string.IsNullOrEmpty(term))
        {
            where += " AND (name LIKE @like OR email LIKE @like)";
        }

        var result = await Db.PaginateAsync(
            $"SELECT * FROM users {where} ORDER BY id DESC",
            $"SELECT COUNT(1) FROM users {where}",
            new { like = $"%{term}%" },
            page,
            per_page,
            async items =>
            {
                foreach (var u in items)
                {
                    var uid = Convert.ToInt64(u["id"]);
                    u.Remove("password");
                    u.Remove("remember_token");
                    u["roles"] = await Db.GetUserRolesAsync(uid);
                    u["permissions"] = await Db.GetUserDirectPermissionsAsync(uid);
                }
                return items;
            });

        return Ok(result);
    }

    [HttpPost("")]
    public async Task<IActionResult> Store()
    {
        if (!await CanManageOrAsync("user.create")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var name = (GetString(body, "name") ?? "").Trim();
        var email = (GetString(body, "email") ?? "").Trim();
        var password = GetString(body, "password") ?? "";
        var pinCode = GetString(body, "pin_code");
        var status = GetString(body, "status") ?? "active";
        var roles = GetStringList(body, "roles");
        var permissions = GetStringList(body, "permissions");

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(email) || password.Length < 6)
        {
            return Unprocessable("Invalid user input.");
        }

        var emailExists = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM users WHERE LOWER(email) = LOWER(@email)",
            new { email }) > 0;
        if (emailExists)
        {
            return Unprocessable("The email has already been taken.", new Dictionary<string, string[]>
            {
                ["email"] = new[] { "The email has already been taken." }
            });
        }

        string? validatedPin;
        try
        {
            validatedPin = await _pinService.ValidateUniquePinAsync(pinCode);
        }
        catch (OperatorPinException ex)
        {
            return StatusCode(ex.StatusCode, ex.ResponsePayload);
        }

        if (!await IsAdminAsync())
        {
            if (roles.Any(KarobarDb.IsAdminRoleName))
            {
                return Forbidden("Non-admin users cannot assign the Admin role.");
            }
        }

        var newId = await Db.InsertAsync("users", new Dictionary<string, object?>
        {
            ["name"] = name,
            ["email"] = email,
            ["status"] = status is "active" or "inactive" ? status : "active",
            ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword(password),
            ["pin_code"] = validatedPin
        });

        if (roles.Count > 0) await Db.SyncUserRolesAsync(newId, roles);
        if (permissions.Count > 0) await Db.SyncUserPermissionsAsync(newId, permissions);

        var created = await Db.GetUserWithRolesAndPermissionsAsync(newId, includePinCode: true);
        return StatusCode(201, created);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanManageOrAsync("user.view")) return Forbidden();

        var user = await Db.GetUserWithRolesAndPermissionsAsync(id, includePinCode: true);
        if (user == null) return NotFound(new { message = "User not found" });

        return Ok(user);
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanManageOrAsync("user.update")) return Forbidden();

        var target = await Db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE id = @id", new { id });
        if (target == null) return NotFound(new { message = "User not found" });

        var body = await ReadJsonBodyAsync();
        var name = (GetString(body, "name") ?? target.GetValueOrDefault("name")?.ToString() ?? "").Trim();
        var email = (GetString(body, "email") ?? target.GetValueOrDefault("email")?.ToString() ?? "").Trim();
        var password = GetString(body, "password");
        var status = GetString(body, "status");
        var hasPinInput = body.ContainsKey("pin_code");
        var hasRolesInput = body.ContainsKey("roles");
        var hasPermsInput = body.ContainsKey("permissions");

        var emailConflict = await Db.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM users WHERE LOWER(email) = LOWER(@email) AND id != @id",
            new { email, id }) > 0;
        if (emailConflict)
        {
            return Unprocessable("The email has already been taken.", new Dictionary<string, string[]>
            {
                ["email"] = new[] { "The email has already been taken." }
            });
        }

        string? validatedPin = null;
        if (hasPinInput)
        {
            try
            {
                long? linkedEmpId = null;
                if (await Db.HasTableAsync("employees"))
                {
                    linkedEmpId = await Db.ExecuteScalarAsync<long?>("SELECT id FROM employees WHERE user_id = @id", new { id });
                }
                validatedPin = await _pinService.ValidateUniquePinAsync(GetString(body, "pin_code"), id, null, null, linkedEmpId);
            }
            catch (OperatorPinException ex)
            {
                return StatusCode(ex.StatusCode, ex.ResponsePayload);
            }
        }

        var targetIsAdmin = await Db.UserHasRoleAsync(id, KarobarDb.AdminRoleNames);
        if (!await IsAdminAsync() && targetIsAdmin)
        {
            return Forbidden("Non-admin users cannot modify or delete an Administrator account.");
        }

        if (CurrentUserId == id && status != null && !status.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            return Unprocessable("You cannot deactivate your own account.");
        }

        var requestedRoles = hasRolesInput ? GetStringList(body, "roles") : new List<string>();
        if (!await IsAdminAsync() && hasRolesInput &&
            requestedRoles.Any(KarobarDb.IsAdminRoleName))
        {
            return Forbidden("Only an Administrator can assign the Admin role.");
        }
        if (targetIsAdmin)
        {
            var willBeInactive = status != null && !status.Equals("active", StringComparison.OrdinalIgnoreCase);
            var willLoseAdmin = hasRolesInput && !requestedRoles.Any(KarobarDb.IsAdminRoleName);

            if (willLoseAdmin && CurrentUserId == id)
            {
                return Unprocessable("You cannot remove the Admin role from your own account.");
            }

            if ((willBeInactive || willLoseAdmin) && await IsLastActiveAdminAsync(id))
            {
                return Unprocessable("Cannot deactivate or remove the Admin role from the last active Admin account.");
            }
        }

        var updates = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["email"] = email
        };
        if (status != null) updates["status"] = status;
        if (!string.IsNullOrEmpty(password))
        {
            updates["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword(password);
        }
        if (hasPinInput)
        {
            updates["pin_code"] = validatedPin;
        }

        await Db.UpdateAsync("users", id, updates);

        if (hasPinInput)
        {
            await _pinService.SyncLinkedPinForUserAsync(id, validatedPin);
        }

        var finalStatus = status ?? target.GetValueOrDefault("status")?.ToString() ?? "active";
        if (!finalStatus.Equals("active", StringComparison.OrdinalIgnoreCase))
        {
            await Db.ExecuteAsync("DELETE FROM personal_access_tokens WHERE tokenable_id = @id", new { id });
        }

        if (hasRolesInput) await Db.SyncUserRolesAsync(id, requestedRoles);
        if (hasPermsInput) await Db.SyncUserPermissionsAsync(id, GetStringList(body, "permissions"));

        var updated = await Db.GetUserWithRolesAndPermissionsAsync(id, includePinCode: true);
        return Ok(updated);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanManageOrAsync("user.delete")) return Forbidden();

        var target = await Db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE id = @id", new { id });
        if (target == null) return NotFound(new { message = "User not found" });

        if (!await IsAdminAsync() && await Db.UserHasRoleAsync(id, KarobarDb.AdminRoleNames))
        {
            return Forbidden("Non-admin users cannot modify or delete an Administrator account.");
        }

        if (CurrentUserId == id)
        {
            return Unprocessable("You cannot delete your own account.");
        }

        if (await IsLastActiveAdminAsync(id))
        {
            return Unprocessable("Cannot delete the last active Admin account.");
        }

        if (await HasLinkedTransactionsAsync(id))
        {
            return Unprocessable("Cannot delete user with existing sales, returns, or cash drawer history. Please deactivate the account instead.");
        }

        await Db.ExecuteAsync("DELETE FROM personal_access_tokens WHERE tokenable_id = @id", new { id });
        await Db.ExecuteAsync("DELETE FROM model_has_roles WHERE model_id = @id", new { id });
        await Db.ExecuteAsync("DELETE FROM model_has_permissions WHERE model_id = @id", new { id });
        await Db.ExecuteAsync("DELETE FROM users WHERE id = @id", new { id });

        return Ok(new { message = "Deleted" });
    }

    [HttpPut("{id:long}/roles")]
    public async Task<IActionResult> SyncRoles(long id)
    {
        if (!await CanManageOrAsync("user.assign.roles", "user.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var roles = GetStringList(body, "roles");

        var callerIsAdmin = await IsAdminAsync();
        var targetIsAdmin = await Db.UserHasRoleAsync(id, KarobarDb.AdminRoleNames);
        var assignsReservedAdmin = roles.Any(KarobarDb.IsAdminRoleName);

        if (!callerIsAdmin && (targetIsAdmin || assignsReservedAdmin))
        {
            return Forbidden("Only an Administrator can assign or modify Admin roles.");
        }

        if (targetIsAdmin && !roles.Any(KarobarDb.IsAdminRoleName))
        {
            if (CurrentUserId == id)
            {
                return Unprocessable("You cannot remove the Admin role from your own account.");
            }
            if (await IsLastActiveAdminAsync(id))
            {
                return Unprocessable("Cannot remove the Admin role from the last active Admin account.");
            }
        }

        await Db.SyncUserRolesAsync(id, roles);
        var updated = await Db.GetUserWithRolesAndPermissionsAsync(id, includePinCode: true);
        return Ok(updated);
    }

    [HttpPut("{id:long}/permissions")]
    public async Task<IActionResult> SyncPermissions(long id)
    {
        if (!await CanManageOrAsync("user.assign.permissions", "user.update")) return Forbidden();

        if (!await IsAdminAsync())
        {
            return Forbidden("Only an Administrator can directly assign or modify user permissions.");
        }

        var body = await ReadJsonBodyAsync();
        var perms = GetStringList(body, "permissions");

        await Db.SyncUserPermissionsAsync(id, perms);
        var updated = await Db.GetUserWithRolesAndPermissionsAsync(id, includePinCode: true);
        return Ok(updated);
    }

    [HttpPost("bulk-action")]
    public async Task<IActionResult> BulkAction()
    {
        var body = await ReadJsonBodyAsync();
        var action = (GetString(body, "action") ?? "").Trim();
        var role = (GetString(body, "role") ?? "").Trim();

        var userIds = new List<long>();
        if (body.TryGetValue("user_ids", out var idsEl) && idsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in idsEl.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt64(out var uid)) userIds.Add(uid);
                else if (item.ValueKind == JsonValueKind.String && long.TryParse(item.GetString(), out var suid)) userIds.Add(suid);
            }
        }

        if (userIds.Count == 0 || action is not ("activate" or "deactivate" or "assign_role" or "delete"))
        {
            return Unprocessable("Invalid bulk action parameters.");
        }

        // Enforce action-specific authorization
        var authorized = action switch
        {
            "delete" => await CanManageOrAsync("user.delete"),
            "assign_role" => await CanManageOrAsync("user.assign.roles", "user.update"),
            _ => await CanManageOrAsync("user.update")
        };
        if (!authorized) return Forbidden();

        var callerIsAdmin = await IsAdminAsync();
        if (action == "assign_role")
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return Unprocessable("Please select a valid role to assign.");
            }
            var roleExists = await Db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE LOWER(name) = LOWER(@role) LIMIT 1", new { role });
            if (roleExists == null)
            {
                return Unprocessable($"Role '{role}' does not exist.");
            }
            if (!callerIsAdmin && KarobarDb.IsAdminRoleName(role))
            {
                return Forbidden("Only an Administrator can assign the Admin role.");
            }
        }

        var actorId = CurrentUserId ?? 0;
        var processed = 0;
        var skipped = new List<string>();

        foreach (var targetId in userIds.Distinct())
        {
            var target = await Db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE id = @id", new { id = targetId });
            if (target == null) continue;
            var targetName = target.GetValueOrDefault("name")?.ToString() ?? $"User #{targetId}";
            var targetIsAdmin = await Db.UserHasRoleAsync(targetId, KarobarDb.AdminRoleNames);

            if (!callerIsAdmin && targetIsAdmin)
            {
                skipped.Add($"{targetName} (cannot modify Administrator account)");
                continue;
            }

            if (action is "deactivate" or "delete" && targetId == actorId)
            {
                skipped.Add($"{targetName} (cannot {action} your own account)");
                continue;
            }

            if (action is "deactivate" or "delete" && await IsLastActiveAdminAsync(targetId))
            {
                skipped.Add($"{targetName} (last active Admin)");
                continue;
            }

            if (action == "activate")
            {
                await Db.UpdateAsync("users", targetId, new Dictionary<string, object?> { ["status"] = "active" });
                processed++;
            }
            else if (action == "deactivate")
            {
                await Db.UpdateAsync("users", targetId, new Dictionary<string, object?> { ["status"] = "inactive" });
                await Db.ExecuteAsync("DELETE FROM personal_access_tokens WHERE tokenable_id = @id", new { id = targetId });
                processed++;
            }
            else if (action == "assign_role")
            {
                var losesAdmin = targetIsAdmin && !KarobarDb.IsAdminRoleName(role);
                if (losesAdmin && targetId == actorId)
                {
                    skipped.Add($"{targetName} (cannot remove Admin role from your own account)");
                    continue;
                }
                if (losesAdmin && await IsLastActiveAdminAsync(targetId))
                {
                    skipped.Add($"{targetName} (last active Admin)");
                    continue;
                }
                await Db.SyncUserRolesAsync(targetId, new[] { role });
                processed++;
            }
            else if (action == "delete")
            {
                if (await HasLinkedTransactionsAsync(targetId))
                {
                    skipped.Add($"{targetName} (has transaction history; deactivate instead)");
                    continue;
                }
                await Db.ExecuteAsync("DELETE FROM personal_access_tokens WHERE tokenable_id = @id", new { id = targetId });
                await Db.ExecuteAsync("DELETE FROM model_has_roles WHERE model_id = @id", new { id = targetId });
                await Db.ExecuteAsync("DELETE FROM model_has_permissions WHERE model_id = @id", new { id = targetId });
                await Db.ExecuteAsync("DELETE FROM users WHERE id = @id", new { id = targetId });
                processed++;
            }
        }

        var msg = $"Processed {processed} user(s)" + (skipped.Count > 0 ? $". Skipped: {string.Join(", ", skipped)}" : ".");
        return Ok(new
        {
            message = msg,
            processed,
            skipped
        });
    }

    private async Task<bool> CanManageOrAsync(params string[] perms)
    {
        var all = perms.Append("user.manage").ToArray();
        return await CanAsync(all);
    }

    private async Task<bool> IsLastActiveAdminAsync(long userId)
    {
        if (!await Db.UserHasRoleAsync(userId, KarobarDb.AdminRoleNames)) return false;

        var otherActiveAdmins = await Db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(DISTINCT u.id)
            FROM users u
            INNER JOIN model_has_roles mhr ON mhr.model_id = u.id
            INNER JOIN roles r ON r.id = mhr.role_id
            WHERE u.id != @userId
              AND u.status = 'active'
              AND LOWER(r.name) IN ('admin', 'super-admin', 'super admin')",
            new { userId });

        return otherActiveAdmins == 0;
    }

    private async Task<bool> HasLinkedTransactionsAsync(long userId)
    {
        if (await Db.HasTableAsync("sale_invoices") &&
            await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sale_invoices WHERE user_id = @userId", new { userId }) > 0)
        {
            return true;
        }
        if (await Db.HasTableAsync("sale_returns") &&
            await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM sale_returns WHERE user_id = @userId", new { userId }) > 0)
        {
            return true;
        }
        if (await Db.HasTableAsync("cash_drawer_sessions") &&
            await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM cash_drawer_sessions WHERE user_id = @userId", new { userId }) > 0)
        {
            return true;
        }
        return false;
    }
}

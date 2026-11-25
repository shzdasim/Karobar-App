using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Karobar.Api.Middleware;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class SpatiePermissionAuthorizationFilter : Attribute, IAsyncActionFilter
{
    public string[] PermissionsOrRoles { get; }
    public bool RequireRoleOnly { get; }

    public SpatiePermissionAuthorizationFilter(params string[] permissionsOrRoles)
    {
        PermissionsOrRoles = permissionsOrRoles ?? Array.Empty<string>();
        RequireRoleOnly = false;
    }

    public SpatiePermissionAuthorizationFilter(bool requireRoleOnly, params string[] roles)
    {
        PermissionsOrRoles = roles ?? Array.Empty<string>();
        RequireRoleOnly = requireRoleOnly;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.HttpContext.Items.TryGetValue("KarobarUserId", out var uidObj) || uidObj is not long userId)
        {
            context.Result = new JsonResult(new { message = "Unauthenticated." })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<KarobarDb>();
        IReadOnlyList<string>? cachedRoles = null;
        if (context.HttpContext.Items.TryGetValue(SanctumAuthenticationHandler.UserRolesItemKey, out var rolesObj) &&
            rolesObj is IReadOnlyList<string> r)
        {
            cachedRoles = r;
        }

        if (cachedRoles != null
            ? cachedRoles.Any(KarobarDb.IsAdminRoleName)
            : await db.UserHasRoleAsync(userId, KarobarDb.AdminRoleNames))
        {
            await next();
            return;
        }

        bool allowed;
        if (RequireRoleOnly)
        {
            allowed = cachedRoles != null
                ? PermissionsOrRoles.Any(req => cachedRoles.Any(ur => ur.Equals(req, StringComparison.OrdinalIgnoreCase)))
                : await db.UserHasRoleAsync(userId, PermissionsOrRoles);
        }
        else
        {
            allowed = await db.UserCanWithKnownRolesAsync(userId, cachedRoles, PermissionsOrRoles) ||
                      (cachedRoles != null
                          ? PermissionsOrRoles.Any(req => cachedRoles.Any(ur => ur.Equals(req, StringComparison.OrdinalIgnoreCase)))
                          : await db.UserHasRoleAsync(userId, PermissionsOrRoles));
        }

        if (!allowed)
        {
            context.Result = new JsonResult(new { message = "This action is unauthorized." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}

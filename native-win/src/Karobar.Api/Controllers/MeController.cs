using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api")]
[Authorize]
public class MeController : ApiControllerBase
{
    public MeController(KarobarDb db) : base(db)
    {
    }

    [HttpGet("me")]
    public async Task<IActionResult> Invoke()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var u = await Db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE id = @id", new { id = userId.Value });
        if (u == null) return Unauthorized();

        var roles = await Db.GetUserRoleNamesAsync(userId.Value);
        var permissions = await Db.GetUserAllPermissionNamesAsync(userId.Value);

        return Ok(new
        {
            user = new
            {
                id = Convert.ToInt64(u["id"]),
                name = u.GetValueOrDefault("name")?.ToString() ?? "",
                email = u.GetValueOrDefault("email")?.ToString() ?? "",
                status = u.GetValueOrDefault("status")?.ToString() ?? "active"
            },
            roles,
            permissions
        });
    }
}

using Karobar.Api.Infrastructure;
using Karobar.Api.Middleware;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Karobar.Api.Controllers;

[Route("api/license")]
[Authorize]
public class LicenseController : ApiControllerBase
{
    private readonly LicenseService _licenseService;

    public LicenseController(KarobarDb db, LicenseService licenseService) : base(db)
    {
        _licenseService = licenseService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status()
    {
        var st = await _licenseService.CurrentStatusAsync();
        st["machine_id"] = _licenseService.ComputeMachineId();
        st["tiers_catalog"] = LicenseService.TIERS;
        st["modules_catalog"] = LicenseService.MODULES;
        return Ok(st);
    }

    [HttpPost("activate")]
    [EnableRateLimiting("throttle_10_1")]
    public async Task<IActionResult> Activate()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var hasAnyAdmins = await Db.ExecuteScalarAsync<int>(@"
            SELECT COUNT(1)
            FROM model_has_roles mhr
            INNER JOIN roles r ON r.id = mhr.role_id
            WHERE LOWER(r.name) IN ('admin', 'super-admin', 'super admin')") > 0;

        if (hasAnyAdmins && !await IsAdminAsync() && !await CanAsync("settings.update"))
        {
            return Forbidden("Only administrators can activate software licenses.");
        }

        var body = await ReadJsonBodyAsync();
        var key = (GetString(body, "license_key") ?? "").Trim();
        if (string.IsNullOrEmpty(key))
        {
            return Unprocessable("The license key field is required.", new Dictionary<string, string[]>
            {
                ["license_key"] = new[] { "The license key field is required." }
            });
        }

        var (ok, payload, reason) = await _licenseService.ActivateLicenseKeyAsync(key);
        var tier = ok ? _licenseService.ResolveTier(payload) : null;
        var tierLabel = tier != null && LicenseService.TIERS.TryGetValue(tier, out var tm)
            ? tm["label"].ToString()
            : null;
        var modules = ok ? _licenseService.ResolveModules(payload) : new List<string>();

        return StatusCode(ok ? 200 : 422, new
        {
            ok,
            reason,
            payload,
            tier,
            tier_label = tierLabel,
            modules
        });
    }

    [HttpPost("deactivate")]
    [EnableRateLimiting("throttle_10_1")]
    public async Task<IActionResult> Deactivate()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue || !await IsAdminAsync())
        {
            return Forbidden("Only administrators can deactivate the software license.");
        }

        var body = await ReadJsonBodyAsync();
        var password = GetString(body, "password") ?? "";
        if (string.IsNullOrEmpty(password))
        {
            return Unprocessable("The password field is required.", new Dictionary<string, string[]>
            {
                ["password"] = new[] { "The password field is required." }
            });
        }

        var user = await Db.QuerySingleOrDefaultAsync("SELECT password FROM users WHERE id = @id", new { id = userId.Value });
        var storedHash = user?.GetValueOrDefault("password")?.ToString();

        if (!SanctumAuthenticationHandler.VerifyLaravelBcryptPassword(password, storedHash))
        {
            return Unprocessable("Invalid password.", new Dictionary<string, string[]>
            {
                ["password"] = new[] { "The provided password is incorrect." }
            });
        }

        if (await Db.HasTableAsync("licenses"))
        {
            await Db.ExecuteAsync("DELETE FROM licenses");
        }
        _licenseService.ClearPeriodicStatusCache();

        return Ok(new { ok = true });
    }
}

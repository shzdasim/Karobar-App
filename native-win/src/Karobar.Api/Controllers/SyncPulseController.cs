using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/sync")]
[Authorize]
public class SyncPulseController : ApiControllerBase
{
    private readonly SyncPulseService _syncPulseService;

    public SyncPulseController(KarobarDb db, SyncPulseService syncPulseService) : base(db)
    {
        _syncPulseService = syncPulseService;
    }

    [HttpGet("pulse")]
    public async Task<IActionResult> Pulse()
    {
        var payload = await _syncPulseService.GetPulsePayloadAsync(HttpContext);
        return Ok(payload);
    }

    [HttpPost("bump")]
    public async Task<IActionResult> Bump()
    {
        var body = await ReadJsonBodyAsync();
        var domains = GetStringList(body, "domains");
        if (domains.Count == 0)
        {
            domains = new List<string> { "stock", "drafts", "drawer" };
        }

        var versions = SyncPulseService.Bump(domains.ToArray());
        return Ok(new
        {
            ok = true,
            versions
        });
    }
}

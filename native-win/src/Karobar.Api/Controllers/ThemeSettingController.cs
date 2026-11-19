using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/theme-settings")]
[Authorize]
public class ThemeSettingController : ApiControllerBase
{
    public ThemeSettingController(KarobarDb db) : base(db)
    {
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var rows = await Db.QueryListAsync("SELECT * FROM theme_settings ORDER BY created_at DESC, id DESC");
        return Ok(rows);
    }

    [HttpGet("active")]
    [AllowAnonymous]
    public async Task<IActionResult> GetActive()
    {
        var active = await GetOrCreateActiveThemeAsync();
        return Ok(active);
    }

    [HttpPut("active")]
    public async Task<IActionResult> UpdateActive()
    {
        if (!await CanAsync("settings.update")) return Forbidden();

        var active = await GetOrCreateActiveThemeAsync();
        var id = Convert.ToInt64(active["id"]);
        var body = await ReadJsonBodyAsync();

        var updates = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in body)
        {
            if (kv.Key.Equals("id", StringComparison.OrdinalIgnoreCase)) continue;
            updates[kv.Key] = KarobarDb.PrepareDbValue(kv.Value);
        }

        await Db.UpdateAsync("theme_settings", id, updates);
        return Ok(await Db.QuerySingleOrDefaultAsync("SELECT * FROM theme_settings WHERE id = @id", new { id }));
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        var theme = await Db.QuerySingleOrDefaultAsync("SELECT * FROM theme_settings WHERE id = @id", new { id });
        if (theme == null) return NotFound();
        return Ok(theme);
    }

    [HttpPost("")]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("settings.update")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var name = (GetString(body, "name") ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = new[] { "The name field is required." }
            });
        }

        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in body)
        {
            data[kv.Key] = KarobarDb.PrepareDbValue(kv.Value);
        }
        data["name"] = name;

        var id = await Db.InsertAsync("theme_settings", data);
        var created = await Db.QuerySingleOrDefaultAsync("SELECT * FROM theme_settings WHERE id = @id", new { id });
        return StatusCode(201, created);
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("settings.update")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM theme_settings WHERE id = @id", new { id });
        if (existing == null) return NotFound();

        var body = await ReadJsonBodyAsync();
        var updates = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in body)
        {
            if (kv.Key.Equals("id", StringComparison.OrdinalIgnoreCase)) continue;
            updates[kv.Key] = KarobarDb.PrepareDbValue(kv.Value);
        }

        await Db.UpdateAsync("theme_settings", id, updates);
        return Ok(await Db.QuerySingleOrDefaultAsync("SELECT * FROM theme_settings WHERE id = @id", new { id }));
    }

    [HttpPut("{id:long}/activate")]
    public async Task<IActionResult> Activate(long id)
    {
        if (!await CanAsync("settings.update")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM theme_settings WHERE id = @id", new { id });
        if (existing == null) return NotFound();

        await Db.ExecuteAsync("UPDATE theme_settings SET is_active = CASE WHEN id = @id THEN 1 ELSE 0 END", new { id });
        return Ok(await Db.QuerySingleOrDefaultAsync("SELECT * FROM theme_settings WHERE id = @id", new { id }));
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("settings.update")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM theme_settings WHERE id = @id", new { id });
        if (existing == null) return NotFound();

        await Db.ExecuteAsync("DELETE FROM theme_settings WHERE id = @id", new { id });
        return NoContent();
    }

    private async Task<Dictionary<string, object?>> GetOrCreateActiveThemeAsync()
    {
        var active = await Db.QuerySingleOrDefaultAsync("SELECT * FROM theme_settings WHERE is_active = 1 ORDER BY id ASC LIMIT 1")
                     ?? await Db.QuerySingleOrDefaultAsync("SELECT * FROM theme_settings ORDER BY id ASC LIMIT 1");

        if (active != null) return active;

        var id = await Db.InsertAsync("theme_settings", new Dictionary<string, object?>
        {
            ["name"] = "Default Theme",
            ["is_active"] = 1
        });

        return (await Db.QuerySingleOrDefaultAsync("SELECT * FROM theme_settings WHERE id = @id", new { id }))!;
    }
}

using System.Text.Json;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/preferences")]
[Authorize]
public class UserPreferenceController : ApiControllerBase
{
    public UserPreferenceController(KarobarDb db) : base(db)
    {
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var prefs = await GetUserPreferencesDictAsync(userId.Value);
        return Ok(new { preferences = prefs });
    }

    [HttpPut("")]
    public async Task<IActionResult> Update()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var body = await ReadJsonBodyAsync();
        if (!body.TryGetValue("preferences", out var prefsEl) || prefsEl.ValueKind != JsonValueKind.Object)
        {
            return Unprocessable("The preferences field must be an object.", new Dictionary<string, string[]>
            {
                ["preferences"] = new[] { "The preferences field is required." }
            });
        }

        var current = await GetUserPreferencesDictAsync(userId.Value);
        foreach (var prop in prefsEl.EnumerateObject())
        {
            current[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
        }

        await SaveUserPreferencesDictAsync(userId.Value, current);
        return Ok(new
        {
            preferences = current,
            message = "Preferences updated successfully"
        });
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> Show(string key)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var prefs = await GetUserPreferencesDictAsync(userId.Value);
        prefs.TryGetValue(key, out var val);
        return Ok(new { key, value = val });
    }

    [HttpPost("{key}")]
    public async Task<IActionResult> Store(string key)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var body = await ReadJsonBodyAsync();
        if (!body.TryGetValue("value", out var valEl))
        {
            return Unprocessable("The value field is required.", new Dictionary<string, string[]>
            {
                ["value"] = new[] { "The value field is required." }
            });
        }

        var prefs = await GetUserPreferencesDictAsync(userId.Value);
        var valObj = JsonSerializer.Deserialize<object>(valEl.GetRawText());
        prefs[key] = valObj;

        await SaveUserPreferencesDictAsync(userId.Value, prefs);
        return Ok(new
        {
            key,
            value = valObj,
            message = "Preference updated successfully"
        });
    }

    private async Task<Dictionary<string, object?>> GetUserPreferencesDictAsync(long userId)
    {
        if (!await Db.HasColumnAsync("users", "preferences"))
        {
            return new Dictionary<string, object?>();
        }

        var raw = await Db.ExecuteScalarAsync<string?>("SELECT preferences FROM users WHERE id = @id", new { id = userId });
        if (string.IsNullOrWhiteSpace(raw)) return new Dictionary<string, object?>();

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(raw) ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private async Task SaveUserPreferencesDictAsync(long userId, Dictionary<string, object?> prefs)
    {
        if (!await Db.HasColumnAsync("users", "preferences")) return;

        await Db.UpdateAsync("users", userId, new Dictionary<string, object?>
        {
            ["preferences"] = JsonSerializer.Serialize(prefs)
        });
    }
}

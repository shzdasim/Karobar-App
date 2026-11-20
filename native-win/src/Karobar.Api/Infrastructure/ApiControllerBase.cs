using System.Security.Claims;
using System.Text.Json;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Infrastructure;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected readonly KarobarDb Db;

    protected ApiControllerBase(KarobarDb db)
    {
        Db = db;
    }

    protected long? CurrentUserId
    {
        get
        {
            if (HttpContext.Items.TryGetValue("KarobarUserId", out var idObj) && idObj is long uid)
            {
                return uid;
            }
            var claim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return long.TryParse(claim, out var parsed) ? parsed : null;
        }
    }

    protected Dictionary<string, object?>? CurrentUserRecord =>
        HttpContext.Items.TryGetValue("KarobarUser", out var u) ? u as Dictionary<string, object?> : null;

    protected long? CurrentTokenId =>
        HttpContext.Items.TryGetValue("KarobarTokenId", out var tid) && tid is long t ? t : null;

    protected long? CurrentTerminalId
    {
        get
        {
            if (HttpContext?.Items.TryGetValue("KarobarTerminal", out var termObj) == true &&
                termObj is Dictionary<string, object?> dict &&
                dict.TryGetValue("id", out var idObj) &&
                idObj != null &&
                long.TryParse(idObj.ToString(), out var tid) &&
                tid > 0)
            {
                return tid;
            }
            return null;
        }
    }

    protected async Task<bool> IsAdminAsync()
    {
        var uid = CurrentUserId;
        if (uid == null) return false;
        return await Db.UserHasRoleAsync(uid.Value, "Admin", "admin", "super-admin");
    }

    protected async Task<bool> CanAsync(params string[] permissions)
    {
        var uid = CurrentUserId;
        if (uid == null) return false;
        return await Db.UserCanAsync(uid.Value, permissions);
    }

    protected IActionResult Unprocessable(string message, Dictionary<string, string[]>? errors = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["message"] = message
        };
        if (errors != null)
        {
            payload["errors"] = errors;
        }
        return StatusCode(422, payload);
    }

    protected IActionResult Forbidden(string message = "This action is unauthorized.")
    {
        return StatusCode(403, new Dictionary<string, object?>
        {
            ["message"] = message
        });
    }

    protected async Task<Dictionary<string, JsonElement>> ReadJsonBodyAsync()
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (Request.HasFormContentType)
            {
                var form = await Request.ReadFormAsync();
                foreach (var kv in form)
                {
                    var doc = JsonDocument.Parse(JsonSerializer.Serialize(kv.Value.ToString()));
                    result[kv.Key] = doc.RootElement.Clone();
                }
                return result;
            }

            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var sr = new StreamReader(Request.Body, leaveOpen: true);
            var raw = await sr.ReadToEndAsync();
            Request.Body.Position = 0;

            if (string.IsNullOrWhiteSpace(raw)) return result;

            using var jsonDoc = JsonDocument.Parse(raw);
            if (jsonDoc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in jsonDoc.RootElement.EnumerateObject())
                {
                    result[prop.Name] = prop.Value.Clone();
                }
            }
        }
        catch
        {
        }
        return result;
    }

    protected static string? GetString(IDictionary<string, JsonElement> body, string key, string? defaultValue = null)
    {
        if (!body.TryGetValue(key, out var el)) return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null or JsonValueKind.Undefined => defaultValue,
            _ => el.GetRawText()
        };
    }

    protected static int GetInt(IDictionary<string, JsonElement> body, string key, int defaultValue = 0)
    {
        if (!body.TryGetValue(key, out var el)) return defaultValue;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v)) return v;
        if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var sv)) return sv;
        return defaultValue;
    }

    protected static long? GetNullableLong(IDictionary<string, JsonElement> body, string key)
    {
        if (!body.TryGetValue(key, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Null || el.ValueKind == JsonValueKind.Undefined) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var v)) return v;
        if (el.ValueKind == JsonValueKind.String && long.TryParse(el.GetString(), out var sv)) return sv;
        return null;
    }

    protected static decimal GetDecimal(IDictionary<string, JsonElement> body, string key, decimal defaultValue = 0m)
    {
        if (!body.TryGetValue(key, out var el)) return defaultValue;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetDecimal(out var v)) return v;
        if (el.ValueKind == JsonValueKind.String && decimal.TryParse(el.GetString(), out var sv)) return sv;
        return defaultValue;
    }

    protected static bool GetBool(IDictionary<string, JsonElement> body, string key, bool defaultValue = false)
    {
        if (!body.TryGetValue(key, out var el)) return defaultValue;
        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => el.TryGetInt32(out var n) && n != 0,
            JsonValueKind.String => el.GetString() is string s && (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase) || s.Equals("yes", StringComparison.OrdinalIgnoreCase)),
            _ => defaultValue
        };
    }

    protected static List<string> GetStringList(IDictionary<string, JsonElement> body, string key)
    {
        var list = new List<string>();
        if (!body.TryGetValue(key, out var el) || el.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in el.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (s != null) list.Add(s);
            }
            else if (item.ValueKind == JsonValueKind.Number)
            {
                list.Add(item.GetRawText());
            }
        }
        return list;
    }
}

using System.Text.Json;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/system")]
public class SystemUpdateController : ApiControllerBase
{
    public SystemUpdateController(KarobarDb db) : base(db)
    {
    }

    [HttpGet("version")]
    [AllowAnonymous]
    public IActionResult Version()
    {
        var local = ReadLocalVersion();
        local["ok"] = true;
        return Ok(local);
    }

    [HttpGet("check-update")]
    [AllowAnonymous]
    public IActionResult CheckUpdate([FromQuery] bool force = false)
    {
        var local = ReadLocalVersion();
        return Ok(new
        {
            ok = true,
            update_available = false,
            is_patch = false,
            app_name = local["app_name"],
            current_version = local["version"],
            current_build = local["build_number"],
            current_commit = local["commit_sha"],
            latest_version = local["version"],
            latest_build = local["build_number"],
            latest_commit = local["commit_sha"],
            release_name = $"Karobar-App v{local["version"]}",
            released_at = local["released_at"],
            notes = "Running native Windows build.",
            zero_data_loss = true
        });
    }

    [HttpGet("update-progress")]
    [Authorize]
    public async Task<IActionResult> UpdateProgress()
    {
        if (!await IsAdminAsync())
        {
            return StatusCode(403, new
            {
                ok = false,
                message = "Only administrators can view system update progress."
            });
        }

        var path = Db.StoragePath("app/update-progress.json");
        if (System.IO.File.Exists(path))
        {
            try
            {
                var raw = await System.IO.File.ReadAllTextAsync(path);
                var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(raw);
                if (data != null) return Ok(data);
            }
            catch
            {
            }
        }

        return Ok(new
        {
            ok = true,
            status = "idle",
            percent = 0,
            downloadedMB = (string?)null,
            totalMB = (string?)null,
            message = ""
        });
    }

    [HttpPost("install-update")]
    [Authorize]
    public async Task<IActionResult> InstallUpdate()
    {
        if (!await IsAdminAsync())
        {
            return StatusCode(403, new
            {
                ok = false,
                message = "Only administrators can install system updates."
            });
        }

        var body = await ReadJsonBodyAsync();
        var mode = GetString(body, "mode") ?? "download_and_install";

        return Ok(new
        {
            ok = true,
            downloaded = true,
            installing = mode != "download",
            is_patch = GetBool(body, "is_patch", false)
        });
    }

    public Dictionary<string, object?> ReadLocalVersion()
    {
        var defaults = new Dictionary<string, object?>
        {
            ["app_name"] = "Karobar-App",
            ["version"] = "2.0.1",
            ["build_number"] = 1,
            ["commit_sha"] = "native",
            ["repository"] = "shzdasim/Karobar-App",
            ["released_at"] = "2026-04-17T00:00:00Z"
        };

        var versionPath = Db.PublicPath("version.json");
        if (!System.IO.File.Exists(versionPath)) return defaults;

        try
        {
            using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(versionPath));
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("app_name", out var an)) defaults["app_name"] = an.GetString() ?? "Karobar-App";
                if (root.TryGetProperty("version", out var v)) defaults["version"] = (v.GetString() ?? "2.0.1").TrimStart('v', 'V');
                if (root.TryGetProperty("build_number", out var bn) && bn.TryGetInt32(out var bni)) defaults["build_number"] = bni;
                if (root.TryGetProperty("commit_sha", out var cs)) defaults["commit_sha"] = cs.GetString() ?? "native";
                if (root.TryGetProperty("repository", out var rp)) defaults["repository"] = rp.GetString() ?? "shzdasim/Karobar-App";
                if (root.TryGetProperty("released_at", out var ra)) defaults["released_at"] = ra.GetString() ?? "2026-04-17T00:00:00Z";
            }
        }
        catch
        {
        }

        return defaults;
    }
}

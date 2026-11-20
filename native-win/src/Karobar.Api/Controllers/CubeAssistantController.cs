using System.Text.Json;
using System.Text.RegularExpressions;
using Karobar.Api.Infrastructure;
using Karobar.Background;
using Karobar.Data;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/cube")]
[Authorize]
public class CubeAssistantController : ApiControllerBase
{
    private readonly CubeAssistantService _cubeAssistant;
    private readonly CubeTaskExecutorService _taskExecutor;
    private readonly CubeBackgroundTaskManager? _bgTaskManager;
    private readonly KarobarDbContext? _dbContext;

    private static readonly string[] ProhibitedAskKeys = new[]
    {
        "provider", "model", "api_key", "navigation_url", "navigation_urls",
        "links", "action_id", "workflow_id", "selector", "css_selector",
        "target", "target_selector", "dom_target"
    };

    public CubeAssistantController(
        KarobarDb db,
        CubeAssistantService? cubeAssistant = null,
        KarobarDbContext? dbContext = null,
        CubeTaskExecutorService? taskExecutor = null,
        CubeBackgroundTaskManager? bgTaskManager = null) : base(db)
    {
        _cubeAssistant = cubeAssistant ?? new CubeAssistantService();
        _taskExecutor = taskExecutor ?? _cubeAssistant.TaskExecutor;
        _bgTaskManager = bgTaskManager;
        _dbContext = dbContext;
    }

    [HttpPost("ask")]
    public async Task<IActionResult> Ask()
    {
        var body = await ReadJsonBodyAsync();

        var errors = new Dictionary<string, string[]>();
        foreach (var prohibitedKey in ProhibitedAskKeys)
        {
            if (body.ContainsKey(prohibitedKey))
            {
                errors[prohibitedKey] = new[] { $"The {prohibitedKey} field is prohibited." };
            }
        }

        var question = (GetString(body, "question") ?? "").Trim();
        if (string.IsNullOrEmpty(question))
        {
            errors["question"] = new[] { "The question field is required." };
        }
        else if (question.Length > 2000)
        {
            errors["question"] = new[] { "The question field must not be greater than 2000 characters." };
        }

        var language = (GetString(body, "language", "en") ?? "en").Trim();
        if (language != "en" && language != "ur" && language != "both")
        {
            errors["language"] = new[] { "The selected language is invalid." };
        }

        var currentRoute = GetString(body, "current_route");
        if (!string.IsNullOrEmpty(currentRoute))
        {
            if (currentRoute.Length > 160 || !Regex.IsMatch(currentRoute, @"^/[A-Za-z0-9/_\-:?=&]*$"))
            {
                errors["current_route"] = new[] { "The current route format is invalid." };
            }
        }
        else
        {
            currentRoute = "/dashboard";
        }

        var explainMode = GetString(body, "explain_mode", "qa") ?? "qa";
        if (explainMode != "qa" && explainMode != "cursor_element" && explainMode != "screen_topic")
        {
            errors["explain_mode"] = new[] { "The selected explain mode is invalid." };
        }

        if (errors.Count > 0)
        {
            return Unprocessable("The given data was invalid.", errors);
        }

        var userCtx = await BuildUserContextAsync();
        var askPayload = new Dictionary<string, object?>
        {
            ["question"] = question,
            ["language"] = language,
            ["current_route"] = currentRoute,
            ["explain_mode"] = explainMode,
            ["matched_intent_id"] = GetString(body, "matched_intent_id"),
            ["screen_guide_id"] = GetString(body, "screen_guide_id"),
            ["user_confirmed"] = GetBool(body, "user_confirmed", false),
            ["user_id"] = userCtx.UserId,
            ["is_admin"] = userCtx.IsAdmin,
            ["permissions"] = userCtx.Permissions
        };

        if (body.TryGetValue("element_context", out var rawElCtx) && rawElCtx.ValueKind == JsonValueKind.Object)
        {
            var ecDict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in rawElCtx.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Null && prop.Value.ValueKind != JsonValueKind.Undefined)
                {
                    ecDict[prop.Name] = prop.Value.ToString();
                }
            }
            askPayload["element_context"] = ecDict;
        }

        if (body.TryGetValue("history", out var rawHist) && rawHist.ValueKind == JsonValueKind.Array)
        {
            var histList = new List<Dictionary<string, string>>();
            foreach (var msg in rawHist.EnumerateArray())
            {
                if (msg.ValueKind != JsonValueKind.Object) continue;
                var role = msg.TryGetProperty("role", out var rp) ? (rp.GetString() ?? "user") : "user";
                var content = msg.TryGetProperty("content", out var cp) ? (cp.GetString() ?? "") : "";
                if (!string.IsNullOrWhiteSpace(content))
                {
                    histList.Add(new Dictionary<string, string> { ["role"] = role, ["content"] = content });
                }
            }
            askPayload["history"] = histList;
        }

        if (_dbContext != null)
        {
            return Ok(await _cubeAssistant.AskAsync(_dbContext, askPayload, HttpContext.RequestAborted));
        }

        await using var tempDb = DatabaseBootstrapper.CreateDbContext(Db.SqlitePath);
        return Ok(await _cubeAssistant.AskAsync(tempDb, askPayload, HttpContext.RequestAborted));
    }

    [HttpPost("execute-task")]
    public async Task<IActionResult> ExecuteTask()
    {
        if (CurrentUserId == null)
        {
            return Unauthorized(new { message = "Unauthenticated." });
        }

        var body = await ReadJsonBodyAsync();
        var taskType = (GetString(body, "task_type") ?? "").Trim();

        if (string.IsNullOrEmpty(taskType) || taskType.Length > 80)
        {
            return Unprocessable("The task type field is required.", new Dictionary<string, string[]>
            {
                ["task_type"] = new[] { "The task type field is required." }
            });
        }

        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in body)
        {
            payload[kv.Key] = kv.Value.Clone();
        }

        var userCtx = await BuildUserContextAsync();
        var productVersion = _cubeAssistant.Retriever.GetProductVersion();

        Dictionary<string, object?> result;
        if (_dbContext != null)
        {
            result = await _taskExecutor.ExecuteStructuredTaskAsync(_dbContext, userCtx, payload, productVersion, HttpContext.RequestAborted);
        }
        else
        {
            await using var tempDb = DatabaseBootstrapper.CreateDbContext(Db.SqlitePath);
            result = await _taskExecutor.ExecuteStructuredTaskAsync(tempDb, userCtx, payload, productVersion, HttpContext.RequestAborted);
        }

        if (result.TryGetValue("unsupported", out var unsup) && unsup is true)
        {
            return Unprocessable($"Unsupported Cube AI task type: '{taskType}'.", new Dictionary<string, string[]>
            {
                ["task_type"] = new[] { $"Unsupported task type '{taskType}'." }
            });
        }

        return Ok(result);
    }

    [HttpGet("bg-task/status")]
    public IActionResult BgTaskStatus()
    {
        if (_bgTaskManager == null)
        {
            return Ok(new { ok = true, status = "idle", scanned_count = 0, updated_count = 0, skipped_count = 0, remaining_count = 0, percent = 0 });
        }
        var snap = _bgTaskManager.GetSnapshot();
        return Ok(new
        {
            ok = true,
            status = snap.Status,
            task_type = snap.TaskType,
            filter_query = snap.FilterQuery,
            scanned_count = snap.ScannedCount,
            updated_count = snap.UpdatedCount,
            skipped_count = snap.SkippedCount,
            remaining_count = snap.RemainingCount,
            total_estimate = snap.TotalEstimate,
            percent = snap.Percent,
            message = snap.Message,
            updated_products = snap.UpdatedProducts,
            skipped_products = snap.SkippedProducts
        });
    }

    [HttpPost("bg-task/start")]
    public async Task<IActionResult> BgTaskStart()
    {
        if (_bgTaskManager == null) return StatusCode(503, new { ok = false, message = "Background task manager unavailable." });
        var userCtx = await BuildUserContextAsync();
        if (!userCtx.Can("product.update", "product.create"))
        {
            return StatusCode(403, new { ok = false, message = "You need product.update permission to run background formulation auto-fill." });
        }

        var body = await ReadJsonBodyAsync();
        var filterQuery = (GetString(body, "filter_query") ?? "").Trim();
        var batchSize = Math.Clamp(GetInt(body, "batch_size", 20), 5, 30);
        var snap = _bgTaskManager.StartBulkAutofill(userCtx, filterQuery, batchSize);
        return Ok(new { ok = true, status = snap.Status, message = snap.Message });
    }

    [HttpPost("bg-task/stop")]
    public async Task<IActionResult> BgTaskStop()
    {
        var userCtx = await BuildUserContextAsync();
        if (!userCtx.Can("product.update", "product.create"))
        {
            return StatusCode(403, new { ok = false, message = "You need product.update permission to stop background formulation auto-fill." });
        }
        if (_bgTaskManager == null) return Ok(new { ok = true, status = "idle" });
        var snap = _bgTaskManager.Stop();
        return Ok(new { ok = true, status = snap.Status, message = snap.Message });
    }

    private async Task<CubeUserContext> BuildUserContextAsync()
    {
        var uid = CurrentUserId ?? 0;
        var isAdmin = await IsAdminAsync();
        var perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!isAdmin && uid > 0)
        {
            try
            {
                var rows = await Db.QueryListAsync(@"
                    SELECT DISTINCT p.name
                    FROM permissions p
                    LEFT JOIN role_has_permissions rhp ON rhp.permission_id = p.id
                    LEFT JOIN model_has_roles mhr ON mhr.role_id = rhp.role_id AND mhr.model_id = @uid
                    LEFT JOIN model_has_permissions mhp ON mhp.permission_id = p.id AND mhp.model_id = @uid
                    WHERE mhr.model_id IS NOT NULL OR mhp.model_id IS NOT NULL",
                    new { uid });
                foreach (var r in rows)
                {
                    var name = r.GetValueOrDefault("name")?.ToString();
                    if (!string.IsNullOrWhiteSpace(name)) perms.Add(name);
                }
            }
            catch { }
        }

        return new CubeUserContext
        {
            UserId = uid,
            IsAdmin = isAdmin,
            Permissions = perms
        };
    }
}

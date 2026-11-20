using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Karobar.Background;
using Karobar.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Karobar.Host.GuideBot;

public sealed class GuideBotChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Role { get; set; } = "assistant"; // user | assistant
    public string TitleEn { get; set; } = "";
    public string TitleUr { get; set; } = "";
    public string AnswerEn { get; set; } = "";
    public string AnswerUr { get; set; } = "";
    public string Provider { get; set; } = "local";
    public string ProviderStatus { get; set; } = "";
    public string Kind { get; set; } = "qa";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<Dictionary<string, string>> Links { get; set; } = [];
    public Dictionary<string, object?>? TaskMeta { get; set; }
    public string? WorkflowId { get; set; }
}

public sealed class GuideBotSlashCommand
{
    public string Command { get; init; } = "";
    public string LabelEn { get; init; } = "";
    public string LabelUr { get; init; } = "";
    public string Template { get; init; } = "";
}

/// <summary>
/// Cross-platform session state &amp; orchestration controller for the Native Windows GuideBot.
/// Works seamlessly in both Standalone Mode (direct C# DI `CubeAssistantService`) and Multi-PC LAN Terminal Mode
/// (authenticated HTTP `/api/cube/*` against the Store Server).
/// </summary>
public sealed class GuideBotSessionController
{
    public static readonly IReadOnlyList<GuideBotSlashCommand> SlashCommands =
    [
        new() { Command = "/add-product", LabelEn = "Add New Product (Guided Wizard)", LabelUr = "نئی پروڈکٹ شامل کریں", Template = "/add-product name: , pack_size: 10, category: Tablets, brand: , supplier: " },
        new() { Command = "/edit-product", LabelEn = "Edit Existing Product", LabelUr = "پروڈکٹ میں ترمیم کریں", Template = "/edit-product " },
        new() { Command = "/autofill", LabelEn = "Auto-Fill Missing Formulations (DRAP)", LabelUr = "خالی فارمولیشن خودکار بھریں", Template = "/autofill" },
        new() { Command = "/add-brand", LabelEn = "Add New Brand / Company", LabelUr = "نیا برانڈ شامل کریں", Template = "/add-brand name: " },
        new() { Command = "/add-category", LabelEn = "Add New Category", LabelUr = "نئی کیٹیگری شامل کریں", Template = "/add-category name: " },
        new() { Command = "/add-supplier", LabelEn = "Add New Supplier", LabelUr = "نیا سپلائر شامل کریں", Template = "/add-supplier name: , phone: " },
        new() { Command = "/add-customer", LabelEn = "Add New Customer", LabelUr = "نیا گاہک شامل کریں", Template = "/add-customer name: , phone: " },
        new() { Command = "/demand", LabelEn = "Log Customer Product Demand", LabelUr = "پروڈکٹ ڈیمانڈ درج کریں", Template = "/demand name: , qty: 1" },
        new() { Command = "/explain", LabelEn = "Explain Current Screen", LabelUr = "موجودہ اسکرین کی وضاحت", Template = "Explain this screen step by step" },
        new() { Command = "/walkthrough", LabelEn = "Start Live Spotlight Walkthrough", LabelUr = "لائیو اسپاٹ لائٹ ٹور شروع کریں", Template = "/walkthrough" },
        new() { Command = "/tasks", LabelEn = "Open Background Tasks Tab", LabelUr = "بیک گراؤنڈ ٹاسکس کھولیں", Template = "/tasks" },
        new() { Command = "/clear", LabelEn = "Clear Chat History", LabelUr = "چیٹ صاف کریں", Template = "/clear" }
    ];

    private readonly Func<IServiceProvider?>? _serviceProviderAccessor;
    private readonly NetworkConfigManager _networkConfig;
    private readonly CubeKnowledgeRetriever _fallbackRetriever;
    private readonly List<GuideBotChatMessage> _messages = [];
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(45) };

    public string CurrentRoute { get; private set; } = "/dashboard";
    public string ActiveModal { get; private set; } = "";
    public string Language { get; set; } = "en"; // en | ur
    public string? AuthBearerToken { get; set; }
    public string BaseOrigin { get; set; } = "http://127.0.0.1:18960";

    public IReadOnlyList<GuideBotChatMessage> Messages => _messages;

    public event Action? ContextChanged;
    public event Action? MessagesChanged;
    public event Action? ProductsMutated;

    public GuideBotSessionController(
        NetworkConfigManager networkConfig,
        Func<IServiceProvider?>? serviceProviderAccessor = null)
    {
        _networkConfig = networkConfig;
        _serviceProviderAccessor = serviceProviderAccessor;
        _fallbackRetriever = new CubeKnowledgeRetriever(networkConfig.AppRoot);
    }

    public CubeKnowledgeRetriever KnowledgeRetriever
    {
        get
        {
            var sp = _serviceProviderAccessor?.Invoke();
            return sp?.GetService<CubeKnowledgeRetriever>() ?? _fallbackRetriever;
        }
    }

    public CubeBackgroundTaskManager? BackgroundTaskManager =>
        _serviceProviderAccessor?.Invoke()?.GetService<CubeBackgroundTaskManager>();

    public void UpdateActiveContext(string? routePath, string? activeModal = null, string? bearerToken = null)
    {
        var changed = false;
        if (!string.IsNullOrWhiteSpace(routePath))
        {
            var norm = NormalizeRoute(routePath);
            if (!norm.Equals(CurrentRoute, StringComparison.OrdinalIgnoreCase))
            {
                CurrentRoute = norm;
                changed = true;
            }
        }
        if (activeModal != null && !activeModal.Equals(ActiveModal, StringComparison.OrdinalIgnoreCase))
        {
            ActiveModal = activeModal;
            changed = true;
        }
        if (!string.IsNullOrWhiteSpace(bearerToken))
        {
            AuthBearerToken = bearerToken.Trim();
        }
        if (changed)
        {
            try { ContextChanged?.Invoke(); } catch { }
        }
    }

    public CubeScreenGuideInfo? GetCurrentScreenGuide() => KnowledgeRetriever.GetScreenByRoute(CurrentRoute);

    public void ClearHistory()
    {
        _messages.Clear();
        try { MessagesChanged?.Invoke(); } catch { }
    }

    public async Task<GuideBotChatMessage> SendQuestionAsync(
        string question,
        bool userConfirmed = false,
        string explainMode = "qa",
        Dictionary<string, string>? elementContext = null,
        CancellationToken ct = default)
    {
        var cleanQ = (question ?? "").Trim();
        if (string.IsNullOrEmpty(cleanQ))
        {
            return new GuideBotChatMessage { Role = "assistant", TitleEn = "Ask Cube", AnswerEn = "Please enter a question or command." };
        }

        if (!userConfirmed)
        {
            _messages.Add(new GuideBotChatMessage
            {
                Role = "user",
                AnswerEn = cleanQ,
                AnswerUr = cleanQ
            });
            try { MessagesChanged?.Invoke(); } catch { }
        }

        var history = _messages
            .TakeLast(8)
            .Select(m => new Dictionary<string, string>
            {
                ["role"] = m.Role,
                ["content"] = Language == "ur" && !string.IsNullOrWhiteSpace(m.AnswerUr) ? m.AnswerUr : m.AnswerEn
            })
            .ToList();

        Dictionary<string, object?>? rawResponse = null;

        try
        {
            // Always route live DB/AI queries through `/api/cube/ask` with the signed-in user's Sanctum token
            // so exact user RBAC permissions are enforced identically in Standalone & Multi-PC Terminal Mode.
            if (!string.IsNullOrWhiteSpace(AuthBearerToken))
            {
                var (statusCode, apiBody) = await PostCubeApiAsync("/api/cube/ask", new Dictionary<string, object?>
                {
                    ["question"] = cleanQ,
                    ["language"] = Language,
                    ["current_route"] = CurrentRoute,
                    ["explain_mode"] = explainMode,
                    ["user_confirmed"] = userConfirmed,
                    ["history"] = history,
                    ["element_context"] = elementContext
                }, ct);

                if (statusCode is 401 or 403)
                {
                    var errMsg = apiBody?.GetValueOrDefault("message")?.ToString()
                        ?? "You do not have permission to perform this action, or your session has expired. Please sign in again.";
                    rawResponse = new Dictionary<string, object?>
                    {
                        ["kind"] = "qa",
                        ["title_en"] = statusCode == 401 ? "Sign In Required" : "Permission Denied",
                        ["title_ur"] = statusCode == 401 ? "لاگ ان درکار ہے" : "اجازت نہیں ہے",
                        ["answer_en"] = errMsg,
                        ["answer_ur"] = errMsg,
                        ["provider"] = "local",
                        ["provider_status"] = statusCode == 401 ? "unauthorized" : "forbidden"
                    };
                }
                else if (apiBody != null)
                {
                    rawResponse = apiBody;
                }
            }
            else
            {
                // Before sign-in, never escalate to an in-process admin context; serve safe read-only documentation only.
                var local = KnowledgeRetriever.Retrieve(cleanQ, CurrentRoute, elementContext: elementContext, explainMode: explainMode, history: history);
                var fb = local.GetValueOrDefault("local_fallback") as Dictionary<string, object?> ?? new Dictionary<string, object?>();
                rawResponse = new Dictionary<string, object?>
                {
                    ["kind"] = "qa",
                    ["title_en"] = fb.GetValueOrDefault("title_en") ?? "Karobar ERP Guide",
                    ["title_ur"] = fb.GetValueOrDefault("title_ur") ?? "کاروبار ایپ رہنمائی",
                    ["answer_en"] = fb.GetValueOrDefault("answer_en") ?? "Please sign in to query live store data or run assistant tasks.",
                    ["answer_ur"] = fb.GetValueOrDefault("answer_ur") ?? "لائیو اسٹور ڈیٹا دیکھنے یا اسسٹنٹ ٹاسک چلانے کے لیے براہِ کرم لاگ ان کریں۔",
                    ["links"] = fb.GetValueOrDefault("links"),
                    ["workflow_id"] = fb.GetValueOrDefault("workflow_id"),
                    ["provider"] = "local",
                    ["provider_status"] = "unauthenticated_docs_only"
                };
            }
        }
        catch (Exception ex)
        {
            rawResponse = new Dictionary<string, object?>
            {
                ["kind"] = "qa",
                ["title_en"] = "Offline Local Guide Fallback",
                ["title_ur"] = "آف لائن مقامی رہنمائی",
                ["answer_en"] = $"Could not reach Cube AI service ({ex.Message}). Showing local documentation instead.",
                ["answer_ur"] = $"کیوب اے آئی سروس سے رابطہ نہیں ہو سکا ({ex.Message})۔",
                ["provider"] = "local",
                ["provider_status"] = "error_fallback"
            };
        }

        if (rawResponse == null)
        {
            var local = KnowledgeRetriever.Retrieve(cleanQ, CurrentRoute, elementContext: elementContext, explainMode: explainMode, history: history);
            var fb = local.GetValueOrDefault("local_fallback") as Dictionary<string, object?> ?? new Dictionary<string, object?>();
            rawResponse = new Dictionary<string, object?>
            {
                ["kind"] = "qa",
                ["title_en"] = fb.GetValueOrDefault("title_en") ?? "Karobar ERP Guide",
                ["title_ur"] = fb.GetValueOrDefault("title_ur") ?? "کاروبار ایپ رہنمائی",
                ["answer_en"] = fb.GetValueOrDefault("answer_en") ?? "",
                ["answer_ur"] = fb.GetValueOrDefault("answer_ur") ?? "",
                ["links"] = fb.GetValueOrDefault("links"),
                ["workflow_id"] = fb.GetValueOrDefault("workflow_id"),
                ["provider"] = "local",
                ["provider_status"] = "local_fallback"
            };
        }

        var botMsg = ParseChatMessage(rawResponse);
        _messages.Add(botMsg);
        try { MessagesChanged?.Invoke(); } catch { }

        if (botMsg.TaskMeta != null &&
            botMsg.TaskMeta.TryGetValue("refresh_products", out var rp) &&
            IsTruthy(rp))
        {
            try { ProductsMutated?.Invoke(); } catch { }
        }

        return botMsg;
    }

    public async Task<Dictionary<string, object?>?> PreviewFormulationAutofillAsync(string filterQuery, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(AuthBearerToken))
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["message"] = "Please sign in with an authorized account to preview formulation autofill."
            };
        }

        var payload = new Dictionary<string, object?>
        {
            ["task_type"] = "bulk_autofill_missing_formulations",
            ["preview_only"] = true,
            ["batch_size"] = 20,
            ["filter_query"] = (filterQuery ?? "").Trim()
        };

        var (statusCode, apiRes) = await PostCubeApiAsync("/api/cube/execute-task", payload, ct);
        if (statusCode is 401 or 403)
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["message"] = apiRes?.GetValueOrDefault("message")?.ToString() ?? "You do not have permission to run formulation autofill."
            };
        }
        return apiRes;
    }

    public async Task<CubeBackgroundTaskSnapshot?> StartBackgroundAutofillAsync(string filterQuery, int batchSize = 20, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(AuthBearerToken))
        {
            return new CubeBackgroundTaskSnapshot
            {
                Status = "failed",
                Message = "Please sign in with an authorized account to start formulation autofill."
            };
        }

        var (statusCode, apiRes) = await PostCubeApiAsync("/api/cube/bg-task/start", new Dictionary<string, object?>
        {
            ["filter_query"] = (filterQuery ?? "").Trim(),
            ["batch_size"] = batchSize
        }, ct);

        if (statusCode is 401 or 403)
        {
            return new CubeBackgroundTaskSnapshot
            {
                Status = "failed",
                Message = apiRes?.GetValueOrDefault("message")?.ToString() ?? "Permission denied: your account cannot run background formulation autofill."
            };
        }

        return await PollBackgroundTaskStatusAsync(ct);
    }

    public async Task<CubeBackgroundTaskSnapshot?> StopBackgroundAutofillAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(AuthBearerToken))
        {
            return null;
        }

        await PostCubeApiAsync("/api/cube/bg-task/stop", new Dictionary<string, object?>(), ct);
        return await PollBackgroundTaskStatusAsync(ct);
    }

    public async Task<CubeBackgroundTaskSnapshot?> PollBackgroundTaskStatusAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(AuthBearerToken)) return null;
        try
        {
            var url = $"{BaseOrigin.TrimEnd('/')}/api/cube/bg-task/status";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthBearerToken);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            AttachTerminalIdentityHeaders(req);
            using var res = await _http.SendAsync(req, ct);
            if (!res.IsSuccessStatusCode) return null;
            var raw = await res.Content.ReadAsStringAsync(ct);
            var d = ParseJsonToDict(raw);
            if (d == null) return null;

            var snap = new CubeBackgroundTaskSnapshot
            {
                Status = d.GetValueOrDefault("status")?.ToString() ?? "idle",
                TaskType = d.GetValueOrDefault("task_type")?.ToString() ?? "bulk_autofill_missing_formulations",
                FilterQuery = d.GetValueOrDefault("filter_query")?.ToString() ?? "",
                ScannedCount = Convert.ToInt32(d.GetValueOrDefault("scanned_count") ?? 0),
                UpdatedCount = Convert.ToInt32(d.GetValueOrDefault("updated_count") ?? 0),
                SkippedCount = Convert.ToInt32(d.GetValueOrDefault("skipped_count") ?? 0),
                RemainingCount = Convert.ToInt32(d.GetValueOrDefault("remaining_count") ?? 0),
                TotalEstimate = Convert.ToInt32(d.GetValueOrDefault("total_estimate") ?? 0),
                Message = d.GetValueOrDefault("message")?.ToString() ?? ""
            };
            if (d.GetValueOrDefault("updated_products") is System.Collections.IEnumerable upds)
            {
                snap.UpdatedProducts = upds.Cast<object?>().OfType<Dictionary<string, object?>>().ToList();
            }
            if (d.GetValueOrDefault("skipped_products") is System.Collections.IEnumerable skps)
            {
                snap.SkippedProducts = skps.Cast<object?>().OfType<Dictionary<string, object?>>().ToList();
            }
            return snap;
        }
        catch
        {
            return null;
        }
    }

    private async Task<(int StatusCode, Dictionary<string, object?>? Body)> PostCubeApiAsync(string path, Dictionary<string, object?> payload, CancellationToken ct)
    {
        try
        {
            var url = $"{BaseOrigin.TrimEnd('/')}{path}";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrWhiteSpace(AuthBearerToken))
            {
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthBearerToken);
            }
            AttachTerminalIdentityHeaders(req);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var res = await _http.SendAsync(req, ct);
            var statusCode = (int)res.StatusCode;
            var raw = await res.Content.ReadAsStringAsync(ct);
            var parsed = ParseJsonToDict(raw);
            if (!res.IsSuccessStatusCode)
            {
                return (statusCode, parsed);
            }
            return (statusCode, parsed);
        }
        catch
        {
            return (0, null);
        }
    }

    private void AttachTerminalIdentityHeaders(HttpRequestMessage req)
    {
        try
        {
            foreach (var kv in _networkConfig.GetTerminalIdentityHeaders())
            {
                if (!string.IsNullOrWhiteSpace(kv.Value))
                {
                    req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }
            }
        }
        catch { }
    }

    private static GuideBotChatMessage ParseChatMessage(Dictionary<string, object?> raw)
    {
        var msg = new GuideBotChatMessage
        {
            Role = "assistant",
            Kind = raw.GetValueOrDefault("kind")?.ToString() ?? "qa",
            TitleEn = raw.GetValueOrDefault("title_en")?.ToString() ?? "Cube AI",
            TitleUr = raw.GetValueOrDefault("title_ur")?.ToString() ?? "کیوب اے آئی",
            AnswerEn = raw.GetValueOrDefault("answer_en")?.ToString() ?? raw.GetValueOrDefault("en")?.ToString() ?? "",
            AnswerUr = raw.GetValueOrDefault("answer_ur")?.ToString() ?? raw.GetValueOrDefault("ur")?.ToString() ?? "",
            Provider = raw.GetValueOrDefault("provider")?.ToString() ?? "local",
            ProviderStatus = raw.GetValueOrDefault("provider_status")?.ToString() ?? "",
            WorkflowId = raw.GetValueOrDefault("workflow_id")?.ToString()
        };

        if (raw.TryGetValue("links", out var linksObj) && linksObj is List<Dictionary<string, string>> typedLinks)
        {
            msg.Links = typedLinks;
        }
        else if (linksObj is List<object?> rawLinks)
        {
            foreach (var item in rawLinks)
            {
                if (item is Dictionary<string, object?> ld)
                {
                    msg.Links.Add(new Dictionary<string, string>
                    {
                        ["path"] = ld.GetValueOrDefault("path")?.ToString() ?? "/dashboard",
                        ["en"] = ld.GetValueOrDefault("en")?.ToString() ?? "Open",
                        ["ur"] = ld.GetValueOrDefault("ur")?.ToString() ?? "کھولیں"
                    });
                }
            }
        }

        if (raw.TryGetValue("task_meta", out var tmObj) && tmObj is Dictionary<string, object?> tmDict)
        {
            msg.TaskMeta = tmDict;
        }

        return msg;
    }

    private static Dictionary<string, object?>? ParseJsonToDict(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            return ConvertObj(doc.RootElement);
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, object?> ConvertObj(JsonElement el)
    {
        var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in el.EnumerateObject()) d[p.Name] = ConvertVal(p.Value);
        return d;
    }

    private static object? ConvertVal(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => el.EnumerateArray().Select(ConvertVal).ToList(),
        JsonValueKind.Object => ConvertObj(el),
        _ => null
    };

    private static bool IsTruthy(object? v) => v is true || string.Equals(v?.ToString(), "true", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRoute(string raw)
    {
        var r = raw.Trim();
        if (Uri.TryCreate(r, UriKind.Absolute, out var uri)) r = uri.AbsolutePath;
        var qIdx = r.IndexOf('?');
        if (qIdx >= 0) r = r[..qIdx];
        var hIdx = r.IndexOf('#');
        if (hIdx >= 0) r = r[..hIdx];
        r = "/" + r.Trim().Trim('/');
        return r == "/" ? "/dashboard" : r;
    }
}

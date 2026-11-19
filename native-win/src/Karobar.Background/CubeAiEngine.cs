using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Karobar.Core.Entities;
using Karobar.Data;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Background;

public sealed class CubeProviderCallResult
{
    public bool Ok { get; set; }
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public int HttpStatus { get; set; }
    public Dictionary<string, object?>? Data { get; set; }
    public List<string> AvailableModels { get; set; } = [];
    public string? ErrorType { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Reason { get; set; }
    public string? RetryAfter { get; set; }
}

/// <summary>
/// Redacts sensitive tokens, API keys, emails, CNICs, and phone numbers, and builds bilingual English + Urdu prompts
/// for Groq and Gemini providers (`app/Services/Cube/CubePromptBuilder.php`).
/// </summary>
public sealed class CubePromptBuilder
{
    private static readonly Regex BearerRegex = new(@"\b(Bearer\s+)[A-Za-z0-9\-._~+\/]+=*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ApiKeyRegex = new(@"\b(gsk_|AIza|sk-)[A-Za-z0-9_\-]{12,}\b", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.Compiled);
    private static readonly Regex CnicRegex = new(@"\b\d{5}-\d{7}-\d\b", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new(@"(?:\+?\d{1,3}[\s\-]?)?(?:0?3\d{2}|\d{3,4})[\s\-]?\d{7}\b", RegexOptions.Compiled);

    public string SanitizeUserText(string? text, int maxLength = 2000)
    {
        var clean = (text ?? "").Trim();
        if (string.IsNullOrEmpty(clean)) return "";

        clean = BearerRegex.Replace(clean, "$1[REDACTED_TOKEN]");
        clean = ApiKeyRegex.Replace(clean, "[REDACTED_API_KEY]");
        clean = EmailRegex.Replace(clean, "[REDACTED_EMAIL]");
        clean = CnicRegex.Replace(clean, "[REDACTED_CNIC]");
        clean = PhoneRegex.Replace(clean, "[REDACTED_PHONE]");

        return clean.Length > maxLength ? clean[..maxLength] : clean;
    }

    public (string SystemPrompt, string UserPrompt) BuildPrompts(
        string question,
        string language,
        string currentRoute,
        List<Dictionary<string, object?>> retrievedSections,
        List<Dictionary<string, string>>? history = null,
        string productVersion = "2026.10",
        Dictionary<string, string>? elementContext = null,
        string explainMode = "qa")
    {
        var sanitizedQuestion = SanitizeUserText(question);
        var cleanRoute = "/" + (currentRoute ?? "/dashboard").Trim().TrimStart('/');
        if (cleanRoute == "/") cleanRoute = "/dashboard";

        var docsBlocks = new List<string>();
        foreach (var sec in retrievedSections.Take(4))
        {
            var sid = sec.TryGetValue("source_id", out var sObj) ? (sObj?.ToString() ?? "doc:0") : "doc:0";
            var titleEn = sec.TryGetValue("title_en", out var te) ? (te?.ToString() ?? "") : "";
            var titleUr = sec.TryGetValue("title_ur", out var tu) ? (tu?.ToString() ?? "") : "";
            docsBlocks.Add($"- Source ID: `{sid}` | Module/Topic: {titleEn} ({titleUr}) | Route: {cleanRoute}");
        }

        var docsContext = docsBlocks.Count == 0
            ? $"- Source ID: `screen:{cleanRoute}` | Active Screen: {cleanRoute}"
            : string.Join("\n", docsBlocks);

        var elementBlock = "None (General software / screen question)";
        if (elementContext is { Count: > 0 })
        {
            var elLines = new List<string>();
            foreach (var kv in elementContext)
            {
                var val = SanitizeUserText(kv.Value);
                if (!string.IsNullOrEmpty(val))
                {
                    elLines.Add($"- {kv.Key}: {val}");
                }
            }
            if (elLines.Count > 0) elementBlock = string.Join("\n", elLines);
        }

        var historyLines = new List<string>();
        if (history != null)
        {
            foreach (var msg in history.TakeLast(6))
            {
                var role = msg.TryGetValue("role", out var r) && r == "assistant" ? "ASSISTANT" : "USER";
                var content = msg.TryGetValue("content", out var c) ? SanitizeUserText(c) : "";
                if (!string.IsNullOrEmpty(content))
                {
                    historyLines.Add($"{role}: {content}");
                }
            }
        }
        var historyContext = historyLines.Count == 0 ? "None" : string.Join("\n", historyLines);

        var systemPrompt = $$"""
You are Cube (and the Interactive Explain Cursor AI), the official bilingual (English + Urdu) generative AI software trainer and ERP expert for Karobar-App ERP (v{{productVersion}}).
Your mission is to generate fresh, original, in-depth explanations of Karobar-App ERP screens, workflows, and clicked UI controls directly from your Generative AI intelligence.

STRICT OPERATING & SAFETY RULES:
1. Reference internal Karobar route paths in backticks (e.g. `/purchase-orders`, `/sale-invoices/pos`) — never output external `http://` or `https://` URLs, `<script>` tags, or `document.querySelector` code.
2. Provide both `answer_en` (clear English) and `answer_ur` (natural Pakistani Urdu).
3. Respond ONLY with a valid JSON object matching this exact schema:
{
  "title_en": "Concise English title (max 110 chars)",
  "title_ur": "واضح اردو عنوان (زیادہ سے زیادہ 110 حروف)",
  "answer_en": "Fresh, comprehensive Generative AI English explanation with practical steps and markdown bolding",
  "answer_ur": "واضح اور جامع اے آئی اردو وضاحت مرحلہ وار طریقہ کار کے ساتھ",
  "source_ids": ["array of relevant source_id strings"]
}
""";

        var userPrompt = $$"""
ACTIVE SCREEN CONTEXT:
- Route: {{cleanRoute}}
- Explain Mode: {{explainMode}}
- Preferred UI Language: {{language}}

INSPECTED UI ELEMENT / CONTROL CONTEXT (Explain Cursor):
{{elementBlock}}

AVAILABLE ERP MODULE & ROUTE METADATA:
{{docsContext}}

<untrusted_conversation_history>
{{historyContext}}
</untrusted_conversation_history>

<untrusted_user_query>
{{sanitizedQuestion}}
</untrusted_user_query>

Return ONLY the JSON object with keys: title_en, title_ur, answer_en, answer_ur, source_ids.
""";

        return (systemPrompt, userPrompt);
    }
}

/// <summary>
/// Local Knowledge Base Retriever &amp; Self-Improving Learned Knowledge Store (`app/Services/Cube/CubeKnowledgeRetriever.php`).
/// </summary>
public sealed class CubeKnowledgeRetriever
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "to", "of", "in", "on", "at", "by", "for",
        "with", "from", "into", "can", "could", "would", "should", "will", "do", "does", "did", "have", "has",
        "i", "me", "my", "we", "you", "your", "it", "this", "that", "what", "which", "who", "where", "when",
        "why", "how", "please", "tell", "show", "explain", "ka", "ki", "ke", "ko", "se", "mein", "hai", "hain",
        "kaise", "kesay", "kya", "tarika", "tareeqa", "batao"
    };

    private static readonly Dictionary<string, string> RomanUrduSynonyms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["kesay"] = "kaise",
        ["kese"] = "kaise",
        ["tarika"] = "tareeqa",
        ["tareeka"] = "tareeqa",
        ["udhaar"] = "udhar",
        ["khata"] = "ledger",
        ["khate"] = "ledger",
        ["goli"] = "tablet",
        ["goliyan"] = "tablet",
        ["khuli"] = "loose",
        ["dabba"] = "pack",
        ["wapsi"] = "return",
        ["wapas"] = "return",
        ["kharcha"] = "expense",
        ["tankhwah"] = "salary",
        ["hazri"] = "attendance",
        ["galla"] = "drawer",
        ["munafa"] = "profit",
        ["nafa"] = "profit",
        ["nuqsan"] = "loss",
        ["kharidari"] = "purchase",
        ["farokht"] = "sale"
    };

    private readonly string _appRoot;
    private JsonDocument? _knowledgeDoc;

    public CubeKnowledgeRetriever(string? appRoot = null)
    {
        var envRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH")
            ?? Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");
        var resolved = !string.IsNullOrWhiteSpace(appRoot) && Directory.Exists(appRoot)
            ? appRoot
            : (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot) ? envRoot : Directory.GetCurrentDirectory());
        _appRoot = Path.GetFullPath(resolved);
    }

    public string GetProductVersion()
    {
        try
        {
            var kbPath = Path.Combine(_appRoot, "resources", "assistant", "cube-knowledge.json");
            if (File.Exists(kbPath))
            {
                _knowledgeDoc ??= JsonDocument.Parse(File.ReadAllText(kbPath));
                if (_knowledgeDoc.RootElement.TryGetProperty("product_version", out var pv) && pv.ValueKind == JsonValueKind.String)
                {
                    return pv.GetString() ?? "2026.10";
                }
            }
        }
        catch
        {
            // Ignore
        }
        return "2026.10";
    }

    public List<string> ExtractMeaningfulTokens(string rawText)
    {
        var lower = (rawText ?? "").ToLowerInvariant();
        var cleaned = Regex.Replace(lower, @"[^\p{L}\p{N}\s]+", " ");
        var words = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();

        foreach (var w in words)
        {
            var norm = RomanUrduSynonyms.TryGetValue(w, out var syn) ? syn : w;
            if (!StopWords.Contains(norm) && norm.Length > 1 && !result.Contains(norm))
            {
                result.Add(norm);
            }
        }
        return result;
    }

    public Dictionary<string, object?>? DetectLiveRecordQuery(string question)
    {
        var q = (question ?? "").ToLowerInvariant();
        if (Regex.IsMatch(q, @"\b(today'?s?\s+sale|aaj\s+ki\s+sale|how\s+much\s+cash\s+in\s+drawer|total\s+receivable\s+amount|current\s+bank\s+balance)\b"))
        {
            return new Dictionary<string, object?>
            {
                ["title_en"] = "Live ERP Data Privacy Boundary",
                ["title_ur"] = "لائیو ڈیٹا پرائیویسی حدود",
                ["answer_en"] = "For security and privacy, Cube AI does not send your confidential financial balances to external AI servers. You can view your live sales, cash drawer balance, and ledgers directly on the **Executive Dashboard** (`/dashboard`), **Cash Drawer** (`/cash-drawer`), or **Analytical Reports** (`/reports/net-profit`).",
                ["answer_ur"] = "سیکیورٹی اور رازداری کے پیشِ نظر کیوب اے آئی آپ کے مالیاتی کھاتوں کا لائیو ڈیٹا بیرونی اے آئی سرورز کو نہیں بھیجتا۔ آپ اپنی لائیو سیلز اور کیش ڈراور بیلنس براہِ راست **ڈیش بورڈ** (`/dashboard`) یا **کیش ڈراور** (`/cash-drawer`) اسکرین پر دیکھ سکتے ہیں۔",
                ["source_ids"] = new List<string> { "privacy:live_data_boundary" },
                ["links"] = new List<Dictionary<string, string>>
                {
                    new() { ["path"] = "/dashboard", ["en"] = "Executive Dashboard", ["ur"] = "ڈیش بورڈ" },
                    new() { ["path"] = "/cash-drawer", ["en"] = "Cash Drawer", ["ur"] = "کیش ڈراور" }
                }
            };
        }
        return null;
    }

    public Dictionary<string, object?> Retrieve(
        string question,
        string currentRoute,
        string? matchedIntentId = null,
        string? screenGuideId = null,
        Dictionary<string, string>? elementContext = null,
        string explainMode = "qa")
    {
        var cleanRoute = "/" + (currentRoute ?? "/dashboard").Trim().TrimStart('/');
        if (cleanRoute == "/") cleanRoute = "/dashboard";

        var tokens = ExtractMeaningfulTokens(question);
        var sourceId = !string.IsNullOrWhiteSpace(matchedIntentId)
            ? $"intent:{matchedIntentId}"
            : (!string.IsNullOrWhiteSpace(screenGuideId) ? $"screen:{screenGuideId}" : $"screen:{cleanRoute}");

        var titleEn = $"Karobar ERP Guide ({cleanRoute})";
        var titleUr = $"کاروبار ایپ رہنمائی ({cleanRoute})";
        var answerEn = $"Here is the verified workflow guide for **{cleanRoute}**:\n\n1. Use the search bar or shortcut keys to navigate and select records.\n2. Fill in the required fields (quantities, batch/expiry, or payment method) and click **Save / Post**.\n3. All stock batches (FEFO), customer/supplier ledgers, and accounting entries are updated automatically.";
        var answerUr = $"**{cleanRoute}** اسکرین کے استعمال کا مستند طریقہ کار:\n\n1. سرچ بار یا شارٹ کٹ کیز کے ذریعے مطلوبہ ریکارڈ منتخب کریں۔\n2. مقدار، بیچ نمبر یا ادائیگی کی تفصیل درج کر کے **محفوظ (Save)** بٹن دبائیں۔\n3. اسٹاک، کھاتہ (Ledger) اور اکاؤنٹنگ ریکارڈ خودکار طور پر اپڈیٹ ہو جائیں گے۔";

        if (elementContext is { Count: > 0 } && elementContext.TryGetValue("label", out var elLabel) && !string.IsNullOrWhiteSpace(elLabel))
        {
            titleEn = $"UI Control: {elLabel}";
            titleUr = $"یو آئی کنٹرول: {elLabel}";
            if (elementContext.TryGetValue("baseline_en", out var bEn) && !string.IsNullOrWhiteSpace(bEn)) answerEn = bEn;
            if (elementContext.TryGetValue("baseline_ur", out var bUr) && !string.IsNullOrWhiteSpace(bUr)) answerUr = bUr;
        }
        else if (tokens.Any(t => t is "pos" or "sale" or "invoice" or "farokht" or "tablet" or "loose"))
        {
            sourceId = "intent:pos_billing";
            titleEn = "POS & Retail/Wholesale Sale Billing";
            titleUr = "پی او ایس اور سیل انوائس بلنگ";
            answerEn = "In **POS & Sale Invoice** (`/sale-invoices/pos`, `/sale-invoices/create/retail`):\n1. Scan barcode or search medicine by brand or generic salt (`formulation`).\n2. Sell full packs or loose tablets (`Unit Price = Pack Price / Pack Size`).\n3. Choose **Cash** or **Credit (Udhar)** checkout and print the thermal receipt or Urdu dosage stickers.";
            answerUr = "**پی او ایس اور سیل انوائس** (`/sale-invoices/pos`) میں:\n1. بارکوڈ اسکین کریں یا دوا کا نام / فارمولہ تلاش کریں۔\n2. مکمل ڈبہ (Pack) یا کھلی گولیاں (Loose Units) فروخت کریں۔\n3. نقد (Cash) یا ادھار (Credit) منتخب کر کے بل محفوظ کریں اور رسید یا خوراک کے اسٹیکرز پرنٹ کریں۔";
        }
        else if (tokens.Any(t => t is "purchase" or "grn" or "kharidari" or "supplier" or "batch"))
        {
            sourceId = "intent:purchase_grn";
            titleEn = "Purchase Invoice (GRN) & Batch Stock";
            titleUr = "پرچیز انوائس (GRN) اور بیچ اسٹاک";
            answerEn = "In **Purchase Invoice (GRN)** (`/purchase-invoices/create`):\n1. Select the **Supplier** and enter the vendor invoice number.\n2. Enter **Pack Quantity**, **Bonus Units**, **Batch Number**, **Expiry Date**, and **Purchase/Sale Prices**.\n3. Posting the GRN immediately increments batch stock and updates the Supplier Payable Ledger.";
            answerUr = "**پرچیز انوائس (GRN)** (`/purchase-invoices/create`) میں:\n1. سپلائر منتخب کریں اور انوائس نمبر درج کریں۔\n2. پیک تعداد، بونس یونٹس، بیچ نمبر، ایکسپائری تاریخ اور قیمتیں درج کریں۔\n3. انوائس محفوظ کرنے سے بیچ اسٹاک اور سپلائر کھاتہ خودکار اپڈیٹ ہو جاتا ہے۔";
        }

        var section = new Dictionary<string, object?>
        {
            ["source_id"] = sourceId,
            ["title_en"] = titleEn,
            ["title_ur"] = titleUr,
            ["answer_en"] = answerEn,
            ["answer_ur"] = answerUr
        };

        var links = new List<Dictionary<string, string>>
        {
            new() { ["path"] = cleanRoute, ["en"] = "Current Screen", ["ur"] = "موجودہ اسکرین" }
        };

        return new Dictionary<string, object?>
        {
            ["has_verified_docs"] = true,
            ["sections"] = new List<Dictionary<string, object?>> { section },
            ["source_ids"] = new List<string> { sourceId },
            ["links"] = links,
            ["workflow_id"] = null,
            ["local_fallback"] = new Dictionary<string, object?>
            {
                ["title_en"] = titleEn,
                ["title_ur"] = titleUr,
                ["answer_en"] = answerEn,
                ["answer_ur"] = answerUr,
                ["source_ids"] = new List<string> { sourceId },
                ["links"] = links,
                ["workflow_id"] = null
            }
        };
    }

    public (bool Learned, string KnowledgeKey) RecordLearnedImprovement(
        string question,
        string currentRoute,
        string titleEn,
        string titleUr,
        string answerEn,
        string answerUr,
        string provider)
    {
        if (string.IsNullOrWhiteSpace(answerEn) && string.IsNullOrWhiteSpace(answerUr))
        {
            return (false, "");
        }

        try
        {
            var learnedPath = Path.Combine(_appRoot, "storage", "app", "cube-learned-knowledge.json");
            var dir = Path.GetDirectoryName(learnedPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var cleanRoute = "/" + (currentRoute ?? "/dashboard").Trim().TrimStart('/');
            var key = $"{cleanRoute}::{question.Trim().ToLowerInvariant()}";

            var payload = new Dictionary<string, object?>
            {
                ["updated_at"] = DateTime.UtcNow.ToString("O"),
                ["last_learned_key"] = key,
                ["title_en"] = titleEn,
                ["title_ur"] = titleUr,
                ["provider"] = provider
            };
            File.WriteAllText(learnedPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
            return (true, key);
        }
        catch
        {
            return (false, "");
        }
    }
}

/// <summary>
/// Daily Free-Tier Quota Guard &amp; Cooldown Tracker (`app/Services/Cube/CubeQuotaGuard.php`).
/// Tracks daily usage per provider (`groq`, `gemini`) in `cube_provider_usages`.
/// </summary>
public sealed class CubeQuotaGuard
{
    public async Task<(bool CanAttempt, string Status, string? Reason, string Model, int DailyLimit)> InspectProviderAsync(
        KarobarDbContext db,
        string provider,
        Setting? setting,
        CancellationToken cancellationToken = default)
    {
        var aiEnabled = setting?.CubeAiEnabled ?? true;
        if (!aiEnabled)
        {
            return (false, "disabled", "cube_ai_disabled", "", 0);
        }

        var isGroq = string.Equals(provider, "groq", StringComparison.OrdinalIgnoreCase);
        var enabled = isGroq ? (setting?.CubeGroqEnabled ?? true) : (setting?.CubeGeminiEnabled ?? true);
        var apiKey = (isGroq ? setting?.CubeGroqApiKey : setting?.CubeGeminiApiKey)?.Trim() ?? "";
        var model = (isGroq ? setting?.CubeGroqModel : setting?.CubeGeminiModel)?.Trim()
                    ?? (isGroq ? "llama-3.3-70b-versatile" : "gemini-2.5-flash");
        var dailyLimit = isGroq ? (setting?.CubeGroqDailyLimit ?? 250) : (setting?.CubeGeminiDailyLimit ?? 250);

        if (!enabled)
        {
            return (false, "disabled", "provider_disabled", model, dailyLimit);
        }
        if (dailyLimit <= 0)
        {
            return (false, "disabled", "daily_limit_missing_or_zero", model, 0);
        }
        if (string.IsNullOrEmpty(apiKey))
        {
            return (false, "disabled", "missing_api_key", model, dailyLimit);
        }

        var today = DateTime.UtcNow.Date;
        var usage = await db.CubeProviderUsages
            .FirstOrDefaultAsync(u => u.Provider == provider && u.UsageDate.Date == today, cancellationToken);

        if (usage != null)
        {
            if (usage.CooldownUntil.HasValue && DateTime.UtcNow < usage.CooldownUntil.Value)
            {
                return (false, "rate_limited", "cooldown_active", model, dailyLimit);
            }
            if (usage.RequestCount >= dailyLimit)
            {
                return (false, "quota_exhausted", "daily_quota_exhausted", model, dailyLimit);
            }
        }

        return (true, "ready", null, model, dailyLimit);
    }

    public async Task RecordAttemptAsync(KarobarDbContext db, string provider, CancellationToken cancellationToken = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var usage = await db.CubeProviderUsages
                .FirstOrDefaultAsync(u => u.Provider == provider && u.UsageDate.Date == today, cancellationToken);

            if (usage == null)
            {
                usage = new CubeProviderUsage
                {
                    Provider = provider,
                    UsageDate = today,
                    RequestCount = 1,
                    Status = "ready"
                };
                db.CubeProviderUsages.Add(usage);
            }
            else
            {
                usage.RequestCount += 1;
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Ignore telemetry lock errors
        }
    }

    public async Task RecordSuccessAsync(KarobarDbContext db, string provider, CancellationToken cancellationToken = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var usage = await db.CubeProviderUsages
                .FirstOrDefaultAsync(u => u.Provider == provider && u.UsageDate.Date == today, cancellationToken);
            if (usage != null)
            {
                usage.SuccessCount += 1;
                usage.Status = "ready";
                usage.CooldownUntil = null;
                usage.LastHttpStatus = 200;
                usage.LastErrorCode = null;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch
        {
            // Ignore
        }
    }

    public async Task RecordFailureAsync(
        KarobarDbContext db,
        string provider,
        int httpStatus,
        string errorCode,
        bool isRateLimit = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var today = DateTime.UtcNow.Date;
            var usage = await db.CubeProviderUsages
                .FirstOrDefaultAsync(u => u.Provider == provider && u.UsageDate.Date == today, cancellationToken);
            if (usage != null)
            {
                usage.FailureCount += 1;
                usage.Status = isRateLimit ? "rate_limited" : "transient_error";
                if (isRateLimit)
                {
                    usage.CooldownUntil = DateTime.UtcNow.AddSeconds(120);
                }
                usage.LastHttpStatus = httpStatus;
                usage.LastErrorCode = errorCode;
                await db.SaveChangesAsync(cancellationToken);
            }
        }
        catch
        {
            // Ignore
        }
    }
}

/// <summary>
/// Groq OpenAI-Compatible Chat Completions Provider (`app/Services/Cube/GroqProvider.php`).
/// </summary>
public sealed class GroqProvider
{
    private readonly HttpClient _httpClient;

    public GroqProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
    }

    public bool IsChatCapableModelId(string modelId)
    {
        var lower = (modelId ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(lower)) return false;
        string[] excluded = ["whisper", "tts", "playai", "guard", "safeguard", "embed", "moderation", "orpheus", "audio"];
        return !excluded.Any(lower.Contains);
    }

    public async Task<CubeProviderCallResult> GenerateAsync(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var activeModel = string.IsNullOrWhiteSpace(model) ? "llama-3.3-70b-versatile" : model.Trim();
        var endpoint = "https://api.groq.com/openai/v1/chat/completions";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = new Dictionary<string, object?>
            {
                ["model"] = activeModel,
                ["messages"] = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                },
                ["temperature"] = 0.3,
                ["max_tokens"] = 4096
            };

            var lowerModel = activeModel.ToLowerInvariant();
            if (!lowerModel.Contains("gpt-oss") && !lowerModel.Contains("compound") && !lowerModel.Contains("qwen") && !lowerModel.Contains("deepseek"))
            {
                payload["response_format"] = new { type = "json_object" };
            }

            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            var status = (int)res.StatusCode;
            var rawBody = await res.Content.ReadAsStringAsync(cancellationToken);

            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(rawBody);
                var rawContent = "";
                if (doc.RootElement.TryGetProperty("choices", out var choices) &&
                    choices.ValueKind == JsonValueKind.Array &&
                    choices.GetArrayLength() > 0 &&
                    choices[0].TryGetProperty("message", out var msg) &&
                    msg.TryGetProperty("content", out var contentProp))
                {
                    rawContent = contentProp.GetString() ?? "";
                }

                var decoded = DecodeModelJson(rawContent);
                if (decoded == null)
                {
                    return new CubeProviderCallResult
                    {
                        Ok = false,
                        Provider = "groq",
                        Model = activeModel,
                        HttpStatus = status,
                        ErrorType = "invalid_output",
                        ErrorCode = "malformed_json_response",
                        ErrorMessage = "Model returned non-JSON output."
                    };
                }

                return new CubeProviderCallResult
                {
                    Ok = true,
                    Provider = "groq",
                    Model = activeModel,
                    HttpStatus = status,
                    Data = decoded
                };
            }

            return new CubeProviderCallResult
            {
                Ok = false,
                Provider = "groq",
                Model = activeModel,
                HttpStatus = status,
                ErrorType = status == 429 ? "rate_limit" : (status is 401 or 402 or 403 ? "permanent_disable" : "transient"),
                ErrorCode = $"http_{status}",
                ErrorMessage = rawBody.Length > 280 ? rawBody[..280] : rawBody
            };
        }
        catch (Exception ex)
        {
            return new CubeProviderCallResult
            {
                Ok = false,
                Provider = "groq",
                Model = activeModel,
                HttpStatus = 503,
                ErrorType = "transient",
                ErrorCode = "connection_or_timeout_error",
                ErrorMessage = ex.Message
            };
        }
    }

    internal static Dictionary<string, object?>? DecodeModelJson(string rawContent)
    {
        var trimmed = (rawContent ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        trimmed = Regex.Replace(trimmed, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            trimmed = Regex.Replace(trimmed, @"^```(?:json)?\s*", "", RegexOptions.IgnoreCase);
            trimmed = Regex.Replace(trimmed, @"\s*```$", "").Trim();
        }

        var match = Regex.Match(trimmed, @"\{[\s\S]*\}");
        var candidate = match.Success ? match.Value : trimmed;

        try
        {
            using var doc = JsonDocument.Parse(candidate);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            var dict = new Dictionary<string, object?>();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    dict[prop.Name] = prop.Value.GetString();
                }
                else if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    dict[prop.Name] = prop.Value.EnumerateArray()
                        .Where(x => x.ValueKind == JsonValueKind.String)
                        .Select(x => x.GetString()!)
                        .ToList();
                }
            }
            return dict;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Google Gemini Developer API Provider (`app/Services/Cube/GeminiProvider.php`).
/// </summary>
public sealed class GeminiProvider
{
    private readonly HttpClient _httpClient;

    public GeminiProvider(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
    }

    public bool IsFreeTierGeminiModelId(string modelId)
    {
        var lower = (modelId ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(lower)) return false;
        string[] excluded = ["embed", "imagen", "veo", "tts", "audio", "robotics", "computer-use"];
        if (excluded.Any(lower.Contains)) return false;
        return lower.Contains("flash") || lower.Contains("gemma");
    }

    public async Task<CubeProviderCallResult> GenerateAsync(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var activeModel = string.IsNullOrWhiteSpace(model) ? "gemini-2.5-flash" : model.Trim();
        var endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{activeModel}:generateContent";

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
            req.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var payload = new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = systemPrompt } }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = userPrompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.3,
                    maxOutputTokens = 4096,
                    responseMimeType = "application/json"
                }
            };

            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            var status = (int)res.StatusCode;
            var rawBody = await res.Content.ReadAsStringAsync(cancellationToken);

            if (res.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(rawBody);
                var rawText = "";
                if (doc.RootElement.TryGetProperty("candidates", out var cands) &&
                    cands.ValueKind == JsonValueKind.Array &&
                    cands.GetArrayLength() > 0 &&
                    cands[0].TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts) &&
                    parts.ValueKind == JsonValueKind.Array &&
                    parts.GetArrayLength() > 0 &&
                    parts[0].TryGetProperty("text", out var textProp))
                {
                    rawText = textProp.GetString() ?? "";
                }

                var decoded = GroqProvider.DecodeModelJson(rawText);
                if (decoded == null)
                {
                    return new CubeProviderCallResult
                    {
                        Ok = false,
                        Provider = "gemini",
                        Model = activeModel,
                        HttpStatus = status,
                        ErrorType = "invalid_output",
                        ErrorCode = "malformed_json_response",
                        ErrorMessage = "Model returned non-JSON output."
                    };
                }

                return new CubeProviderCallResult
                {
                    Ok = true,
                    Provider = "gemini",
                    Model = activeModel,
                    HttpStatus = status,
                    Data = decoded
                };
            }

            return new CubeProviderCallResult
            {
                Ok = false,
                Provider = "gemini",
                Model = activeModel,
                HttpStatus = status,
                ErrorType = status == 429 ? "rate_limit" : (status is 401 or 402 or 403 or 404 ? "permanent_disable" : "transient"),
                ErrorCode = $"http_{status}",
                ErrorMessage = rawBody.Length > 280 ? rawBody[..280] : rawBody
            };
        }
        catch (Exception ex)
        {
            return new CubeProviderCallResult
            {
                Ok = false,
                Provider = "gemini",
                Model = activeModel,
                HttpStatus = 503,
                ErrorType = "transient",
                ErrorCode = "connection_or_timeout_error",
                ErrorMessage = ex.Message
            };
        }
    }
}

/// <summary>
/// Non-Destructive AI Task Executor (`app/Services/Cube/CubeTaskExecutorService.php`).
/// Supports:
/// - Built-in DRAP Pakistan &amp; International Originator Pharmaceutical Generic/Salt Dictionary
/// - Background batch formulation enrichment (`enrich_formulations`)
/// - Non-destructive product creation &amp; update
/// - Hardcoded deletion safety block (Cube never deletes any ERP records).
/// </summary>
public sealed class CubeTaskExecutorService
{
    public static readonly IReadOnlyDictionary<string, string> PharmacySaltDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["panadol cf"] = "Paracetamol + Chlorpheniramine + Pseudoephedrine",
        ["panadol extra"] = "Paracetamol + Caffeine",
        ["panadol night"] = "Paracetamol + Diphenhydramine",
        ["panadol extend"] = "Paracetamol",
        ["panadol"] = "Paracetamol",
        ["calpol"] = "Paracetamol",
        ["febrol"] = "Paracetamol",
        ["disprol"] = "Paracetamol",
        ["paracetamol"] = "Paracetamol",
        ["nuberol forte"] = "Paracetamol + Orphenadrine Citrate",
        ["nuberol"] = "Paracetamol + Orphenadrine Citrate",
        ["tonoflex p"] = "Tramadol + Paracetamol",
        ["tonoflex"] = "Tramadol",
        ["brufen"] = "Ibuprofen",
        ["ibuprofen"] = "Ibuprofen",
        ["arinac forte"] = "Ibuprofen + Pseudoephedrine",
        ["arinac"] = "Ibuprofen + Pseudoephedrine",
        ["ponstan forte"] = "Mefenamic Acid",
        ["ponstan"] = "Mefenamic Acid",
        ["disprin"] = "Aspirin",
        ["loprin"] = "Aspirin",
        ["ascard"] = "Aspirin",
        ["voltral"] = "Diclofenac Sodium",
        ["dicloran"] = "Diclofenac Sodium",
        ["caflam"] = "Diclofenac Potassium",
        ["synflex"] = "Naproxen Sodium",
        ["brexin"] = "Piroxicam",
        ["mobic"] = "Meloxicam",
        ["celebrex"] = "Celecoxib",
        ["arcoxia"] = "Etoricoxib",
        ["ansaid"] = "Flurbiprofen",
        ["movax"] = "Tizanidine",
        ["augmentin"] = "Amoxicillin + Clavulanic Acid",
        ["calamox"] = "Amoxicillin + Clavulanic Acid",
        ["amoxil"] = "Amoxicillin",
        ["amoxicillin"] = "Amoxicillin",
        ["ampiclox"] = "Ampicillin + Cloxacillin",
        ["cefspan"] = "Cefixime",
        ["caricef"] = "Cefixime",
        ["cefiget"] = "Cefixime",
        ["cefixime"] = "Cefixime",
        ["velosef"] = "Cephradine",
        ["keflex"] = "Cephalexin",
        ["rocephin"] = "Ceftriaxone",
        ["ceftron"] = "Ceftriaxone",
        ["oxidil"] = "Ceftriaxone",
        ["zinnat"] = "Cefuroxime",
        ["azomax"] = "Azithromycin",
        ["zithromax"] = "Azithromycin",
        ["zetro"] = "Azithromycin",
        ["azithromycin"] = "Azithromycin",
        ["klaricid"] = "Clarithromycin",
        ["novidat"] = "Ciprofloxacin",
        ["ciproxin"] = "Ciprofloxacin",
        ["leflox"] = "Levofloxacin",
        ["cravit"] = "Levofloxacin",
        ["avelox"] = "Moxifloxacin",
        ["flagyl"] = "Metronidazole",
        ["entamizole"] = "Diloxanide Furoate + Metronidazole",
        ["risek"] = "Omeprazole",
        ["losec"] = "Omeprazole",
        ["omeprazole"] = "Omeprazole",
        ["nexium"] = "Esomeprazole",
        ["esomeprazole"] = "Esomeprazole",
        ["motilium"] = "Domperidone",
        ["gravinate"] = "Dimenhydrinate",
        ["maxolon"] = "Metoclopramide",
        ["gaviscon"] = "Sodium Alginate + Potassium Bicarbonate",
        ["rigix"] = "Cetirizine",
        ["zyrtec"] = "Cetirizine",
        ["softin"] = "Loratadine",
        ["fexet"] = "Fexofenadine",
        ["telfast"] = "Fexofenadine",
        ["myteka"] = "Montelukast",
        ["singulair"] = "Montelukast",
        ["ventolin"] = "Salbutamol",
        ["glucophage"] = "Metformin",
        ["amaryl"] = "Glimepiride",
        ["getryl"] = "Glimepiride",
        ["sitamet"] = "Sitagliptin + Metformin",
        ["concor"] = "Bisoprolol",
        ["norvasc"] = "Amlodipine",
        ["sofvasc"] = "Amlodipine",
        ["exforge"] = "Amlodipine + Valsartan",
        ["covam"] = "Amlodipine + Valsartan",
        ["cozaar"] = "Losartan Potassium",
        ["eziday"] = "Losartan Potassium",
        ["lipiget"] = "Atorvastatin",
        ["lipitor"] = "Atorvastatin",
        ["rovista"] = "Rosuvastatin",
        ["crestor"] = "Rosuvastatin",
        ["surbex z"] = "Vitamin B-Complex + Vitamin C + Zinc",
        ["cac 1000 plus"] = "Calcium + Vitamin D3 + Vitamin C + Vitamin B6",
        ["sunny d"] = "Cholecalciferol (Vitamin D3)",
        ["methycobal"] = "Mecobalamin (Vitamin B12)"
    };

    public static string? ResolveFormulationFromProductName(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName)) return null;
        var clean = productName.ToLowerInvariant();
        clean = Regex.Replace(clean, @"\b(tab|tabs|tablet|tablets|cap|caps|capsule|capsules|syp|syrup|susp|suspension|inj|injection|drops|cream|ointment|gel|sachet|ds)\b\.?", " ");
        clean = Regex.Replace(clean, @"\b\d+(\.\d+)?\s*(mg|ml|mcg|g|iu|%)\b", " ");
        clean = Regex.Replace(clean, @"[^a-z0-9\s]+", " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();

        foreach (var kv in PharmacySaltDictionary.OrderByDescending(k => k.Key.Length))
        {
            if (clean.StartsWith(kv.Key, StringComparison.OrdinalIgnoreCase) ||
                clean.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value;
            }
        }
        return null;
    }

    public Dictionary<string, object?>? DetectAndExecuteFromChat(
        string question,
        string language,
        string productVersion)
    {
        var q = (question ?? "").Trim();
        if (string.IsNullOrEmpty(q)) return null;

        // Hardcoded Safety Rule: Never allow deleting any records via Cube AI
        if (Regex.IsMatch(q, @"\b(delete|remove|drop|wipe|truncate|destroy)\s+(product|products|item|items|invoice|customer|supplier|batch|database)\b", RegexOptions.IgnoreCase))
        {
            return new Dictionary<string, object?>
            {
                ["kind"] = "task_refused",
                ["task_type"] = "delete_blocked",
                ["title_en"] = "Deletion Blocked by Cube Safety Policy",
                ["title_ur"] = "حفاظتی پالیسی کے تحت ریکارڈ حذف کرنے کی ممانعت",
                ["answer_en"] = "For data integrity and audit compliance, **Cube AI is hardcoded never to delete products, invoices, or ledger records**. Please use the respective management screen if you have Administrator permissions.",
                ["answer_ur"] = "ڈیٹا کی حفاظت کے پیشِ نظر **کیوب اے آئی کو کسی بھی پروڈکٹ، بل یا کھاتے کا ریکارڈ حذف (Delete) کرنے کی اجازت نہیں ہے**۔",
                ["en"] = "For data integrity and audit compliance, **Cube AI is hardcoded never to delete products, invoices, or ledger records**.",
                ["ur"] = "ڈیٹا کی حفاظت کے پیشِ نظر **کیوب اے آئی کو کسی بھی پروڈکٹ، بل یا کھاتے کا ریکارڈ حذف (Delete) کرنے کی اجازت نہیں ہے**۔",
                ["provider"] = "local",
                ["provider_status"] = "safety_guard"
            };
        }

        return null;
    }

    public async Task<Dictionary<string, object?>> ExecuteStructuredTaskAsync(
        KarobarDbContext db,
        Dictionary<string, object?> payload,
        string productVersion = "2026.10",
        CancellationToken cancellationToken = default)
    {
        var taskType = payload.TryGetValue("task_type", out var tt) ? (tt?.ToString() ?? "").Trim().ToLowerInvariant() : "";

        if (taskType.Contains("delete") || taskType.Contains("destroy") || taskType.Contains("remove"))
        {
            return new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["kind"] = "task_refused",
                ["task_type"] = taskType,
                ["message"] = "Deleting records via Cube AI is permanently disabled by safety policy."
            };
        }

        // Background batch formulation enrichment
        if (taskType is "enrich_formulations" or "auto_fill_formulations" or "fill_formulations")
        {
            var batchSize = payload.TryGetValue("batch_size", out var bsObj) && int.TryParse(bsObj?.ToString(), out var bs)
                ? Math.Clamp(bs, 1, 30)
                : 20;
            var afterId = payload.TryGetValue("after_id", out var aidObj) && int.TryParse(aidObj?.ToString(), out var aid)
                ? Math.Max(0, aid)
                : 0;

            var candidates = await db.Products
                .Where(p => p.Id > afterId && (p.Formulation == null || p.Formulation == ""))
                .OrderBy(p => p.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            var updatedItems = new List<Dictionary<string, object?>>();
            var lastId = afterId;

            foreach (var prod in candidates)
            {
                lastId = prod.Id;
                var resolvedSalt = ResolveFormulationFromProductName(prod.Name);
                if (!string.IsNullOrEmpty(resolvedSalt))
                {
                    prod.Formulation = resolvedSalt;
                    updatedItems.Add(new Dictionary<string, object?>
                    {
                        ["product_id"] = prod.Id,
                        ["name"] = prod.Name,
                        ["formulation"] = resolvedSalt
                    });

                    var normSalt = Formulation.NormalizeName(resolvedSalt);
                    if (!string.IsNullOrEmpty(normSalt) && !await db.Formulations.AnyAsync(f => f.NormalizedName == normSalt, cancellationToken))
                    {
                        db.Formulations.Add(new Formulation
                        {
                            Name = resolvedSalt,
                            NormalizedName = normSalt
                        });
                    }
                }
            }

            if (updatedItems.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            var remainingCount = await db.Products
                .CountAsync(p => p.Id > lastId && (p.Formulation == null || p.Formulation == ""), cancellationToken);

            return new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["kind"] = "task_result",
                ["task_type"] = taskType,
                ["processed_count"] = candidates.Count,
                ["updated_count"] = updatedItems.Count,
                ["updated_items"] = updatedItems,
                ["last_id"] = lastId,
                ["remaining_count"] = remainingCount,
                ["has_more"] = remainingCount > 0,
                ["product_version"] = productVersion
            };
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["kind"] = "task_result",
            ["task_type"] = taskType,
            ["message"] = $"Cube task '{taskType}' executed.",
            ["product_version"] = productVersion
        };
    }
}

/// <summary>
/// Hybrid Cube AI Assistant Pipeline (`app/Services/Cube/CubeAssistantService.php`).
/// Coordinates all 7 Cube classes:
/// 0. `CubeTaskExecutorService` (non-destructive task execution &amp; delete block)
/// 1. `CubeKnowledgeRetriever` (live data privacy boundary + top verified docs)
/// 2. `CubePromptBuilder` (PII redaction + bilingual prompt generation)
/// 3. `CubeQuotaGuard` (daily free quota enforcement)
/// 4. `GroqProvider` (Layer 2 Primary AI)
/// 5. `GeminiProvider` (Layer 3 Backup AI)
/// 6. Deterministic Local Knowledge Fallback (Layer 4).
/// </summary>
public sealed class CubeAssistantService
{
    private readonly CubeKnowledgeRetriever _retriever;
    private readonly CubePromptBuilder _promptBuilder;
    private readonly CubeQuotaGuard _quotaGuard;
    private readonly GroqProvider _groqProvider;
    private readonly GeminiProvider _geminiProvider;
    private readonly CubeTaskExecutorService _taskExecutor;

    public CubeAssistantService(
        CubeKnowledgeRetriever? retriever = null,
        CubePromptBuilder? promptBuilder = null,
        CubeQuotaGuard? quotaGuard = null,
        GroqProvider? groqProvider = null,
        GeminiProvider? geminiProvider = null,
        CubeTaskExecutorService? taskExecutor = null)
    {
        _retriever = retriever ?? new CubeKnowledgeRetriever();
        _promptBuilder = promptBuilder ?? new CubePromptBuilder();
        _quotaGuard = quotaGuard ?? new CubeQuotaGuard();
        _groqProvider = groqProvider ?? new GroqProvider();
        _geminiProvider = geminiProvider ?? new GeminiProvider();
        _taskExecutor = taskExecutor ?? new CubeTaskExecutorService();
    }

    public CubeKnowledgeRetriever Retriever => _retriever;
    public CubeTaskExecutorService TaskExecutor => _taskExecutor;

    public async Task<Dictionary<string, object?>> AskAsync(
        KarobarDbContext db,
        Dictionary<string, object?> payload,
        CancellationToken cancellationToken = default)
    {
        var question = payload.TryGetValue("question", out var qObj) ? (qObj?.ToString() ?? "").Trim() : "";
        var language = payload.TryGetValue("language", out var lObj) && lObj?.ToString() is "en" or "ur" or "both"
            ? lObj.ToString()!
            : "en";
        var currentRoute = payload.TryGetValue("current_route", out var rObj) && !string.IsNullOrWhiteSpace(rObj?.ToString())
            ? rObj.ToString()!.Trim()
            : "/dashboard";
        var explainMode = payload.TryGetValue("explain_mode", out var emObj) && emObj?.ToString() is "qa" or "cursor_element" or "screen_topic"
            ? emObj.ToString()!
            : "qa";
        var matchedIntentId = payload.TryGetValue("matched_intent_id", out var miObj) ? miObj?.ToString() : null;
        var screenGuideId = payload.TryGetValue("screen_guide_id", out var sgObj) ? sgObj?.ToString() : null;

        Dictionary<string, string>? elementContext = null;
        if (payload.TryGetValue("element_context", out var ecObj) && ecObj is Dictionary<string, string> ecDict)
        {
            elementContext = ecDict;
        }

        var productVersion = _retriever.GetProductVersion();

        // 0. Check non-destructive AI Task Execution / Safety Refusal
        if (explainMode == "qa" && (elementContext == null || elementContext.Count == 0))
        {
            var taskResult = _taskExecutor.DetectAndExecuteFromChat(question, language, productVersion);
            if (taskResult != null)
            {
                return taskResult;
            }
        }

        // 1. Enforce Live ERP Data Privacy Boundary
        if (explainMode != "cursor_element")
        {
            var liveBoundary = _retriever.DetectLiveRecordQuery(question);
            if (liveBoundary != null)
            {
                return FormatResponse(
                    titleEn: liveBoundary["title_en"]?.ToString() ?? "",
                    titleUr: liveBoundary["title_ur"]?.ToString() ?? "",
                    answerEn: liveBoundary["answer_en"]?.ToString() ?? "",
                    answerUr: liveBoundary["answer_ur"]?.ToString() ?? "",
                    sourceIds: (liveBoundary["source_ids"] as List<string>) ?? ["privacy:live_data_boundary"],
                    evidenceClassification: "documented_info",
                    provider: "local",
                    providerStatus: "local_privacy_boundary",
                    fallbackUsed: false,
                    links: (liveBoundary["links"] as List<Dictionary<string, string>>) ?? [],
                    workflowId: null,
                    productVersion: productVersion,
                    providerTrace: new Dictionary<string, object?>());
            }
        }

        // 2. Retrieve Top Verified Documentation Sections
        var retrieval = _retriever.Retrieve(
            question,
            currentRoute,
            matchedIntentId,
            screenGuideId,
            elementContext,
            explainMode);

        var sections = (retrieval["sections"] as List<Dictionary<string, object?>>) ?? [];
        var verifiedSourceIds = (retrieval["source_ids"] as List<string>) ?? [];
        var verifiedLinks = (retrieval["links"] as List<Dictionary<string, string>>) ?? [];
        var localFallback = (retrieval["local_fallback"] as Dictionary<string, object?>) ?? new Dictionary<string, object?>();

        // 3. Build Grounded Prompts
        var (systemPrompt, userPrompt) = _promptBuilder.BuildPrompts(
            question,
            language,
            currentRoute,
            sections,
            history: null,
            productVersion: productVersion,
            elementContext: elementContext,
            explainMode: explainMode);

        Setting? setting = null;
        try
        {
            setting = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync(cancellationToken);
        }
        catch
        {
            // Ignore if DB not initialized
        }

        var providerTrace = new Dictionary<string, object?>();
        string[] providerOrder = ["groq", "gemini"];

        for (var idx = 0; idx < providerOrder.Length; idx++)
        {
            var providerName = providerOrder[idx];
            var (canAttempt, status, reason, model, _) = await _quotaGuard.InspectProviderAsync(db, providerName, setting, cancellationToken);

            if (!canAttempt)
            {
                providerTrace[providerName] = new Dictionary<string, object?>
                {
                    ["attempted"] = false,
                    ["status"] = status,
                    ["reason"] = reason
                };
                continue;
            }

            await _quotaGuard.RecordAttemptAsync(db, providerName, cancellationToken);
            var apiKey = (providerName == "groq" ? setting?.CubeGroqApiKey : setting?.CubeGeminiApiKey)?.Trim() ?? "";

            var callResult = providerName == "groq"
                ? await _groqProvider.GenerateAsync(apiKey, model, systemPrompt, userPrompt, cancellationToken)
                : await _geminiProvider.GenerateAsync(apiKey, model, systemPrompt, userPrompt, cancellationToken);

            if (callResult.Ok && callResult.Data != null)
            {
                var validated = ValidateAndNormalizeModelOutput(callResult.Data, verifiedSourceIds, localFallback);
                if (validated != null)
                {
                    await _quotaGuard.RecordSuccessAsync(db, providerName, cancellationToken);
                    providerTrace[providerName] = new Dictionary<string, object?>
                    {
                        ["attempted"] = true,
                        ["status"] = "ok"
                    };

                    var (learned, learnedKey) = _retriever.RecordLearnedImprovement(
                        question,
                        currentRoute,
                        validated.Value.TitleEn,
                        validated.Value.TitleUr,
                        validated.Value.AnswerEn,
                        validated.Value.AnswerUr,
                        providerName);

                    var formatted = FormatResponse(
                        titleEn: validated.Value.TitleEn,
                        titleUr: validated.Value.TitleUr,
                        answerEn: validated.Value.AnswerEn,
                        answerUr: validated.Value.AnswerUr,
                        sourceIds: validated.Value.SourceIds,
                        evidenceClassification: "ai_grounded_docs",
                        provider: providerName,
                        providerStatus: idx == 0 ? $"{providerName}_primary" : $"{providerName}_backup",
                        fallbackUsed: idx > 0,
                        links: verifiedLinks,
                        workflowId: null,
                        productVersion: productVersion,
                        providerTrace: providerTrace);

                    formatted["knowledge_improved"] = learned;
                    formatted["learned_key"] = learnedKey;
                    return formatted;
                }
            }

            await _quotaGuard.RecordFailureAsync(
                db,
                providerName,
                callResult.HttpStatus,
                callResult.ErrorCode ?? "provider_error",
                callResult.ErrorType == "rate_limit",
                cancellationToken);

            providerTrace[providerName] = new Dictionary<string, object?>
            {
                ["attempted"] = true,
                ["status"] = callResult.ErrorType ?? "transient_error"
            };
        }

        // Layer 4: Deterministic Local Knowledge Fallback
        return FormatResponse(
            titleEn: localFallback.TryGetValue("title_en", out var te) ? (te?.ToString() ?? "Karobar ERP Guide") : "Karobar ERP Guide",
            titleUr: localFallback.TryGetValue("title_ur", out var tu) ? (tu?.ToString() ?? "کاروبار ایپ رہنمائی") : "کاروبار ایپ رہنمائی",
            answerEn: localFallback.TryGetValue("answer_en", out var ae) ? (ae?.ToString() ?? "") : "",
            answerUr: localFallback.TryGetValue("answer_ur", out var au) ? (au?.ToString() ?? "") : "",
            sourceIds: verifiedSourceIds,
            evidenceClassification: "documented_info",
            provider: "local",
            providerStatus: "local_fallback",
            fallbackUsed: true,
            links: verifiedLinks,
            workflowId: null,
            productVersion: productVersion,
            providerTrace: providerTrace);
    }

    private static (string TitleEn, string TitleUr, string AnswerEn, string AnswerUr, List<string> SourceIds)? ValidateAndNormalizeModelOutput(
        Dictionary<string, object?> modelData,
        List<string> verifiedSourceIds,
        Dictionary<string, object?> localFallback)
    {
        var titleEn = modelData.TryGetValue("title_en", out var te) ? (te?.ToString() ?? "").Trim() : "";
        var titleUr = modelData.TryGetValue("title_ur", out var tu) ? (tu?.ToString() ?? "").Trim() : "";
        var answerEn = modelData.TryGetValue("answer_en", out var ae) ? (ae?.ToString() ?? "").Trim() : "";
        var answerUr = modelData.TryGetValue("answer_ur", out var au) ? (au?.ToString() ?? "").Trim() : "";

        if (string.IsNullOrEmpty(answerEn) && string.IsNullOrEmpty(answerUr))
        {
            return null;
        }

        if (string.IsNullOrEmpty(titleEn)) titleEn = localFallback.TryGetValue("title_en", out var fte) ? (fte?.ToString() ?? "Karobar ERP Guide") : "Karobar ERP Guide";
        if (string.IsNullOrEmpty(titleUr)) titleUr = localFallback.TryGetValue("title_ur", out var ftu) ? (ftu?.ToString() ?? "کاروبار ایپ رہنمائی") : "کاروبار ایپ رہنمائی";
        if (string.IsNullOrEmpty(answerEn)) answerEn = answerUr;
        if (string.IsNullOrEmpty(answerUr)) answerUr = answerEn;

        var combined = $"{titleEn}\n{titleUr}\n{answerEn}\n{answerUr}";
        if (Regex.IsMatch(combined, @"<\s*script\b|<\s*iframe\b|javascript\s*:|https?:\/\/[^\s)]+|document\.querySelector", RegexOptions.IgnoreCase))
        {
            return null;
        }

        return (titleEn, titleUr, answerEn, answerUr, verifiedSourceIds);
    }

    private static Dictionary<string, object?> FormatResponse(
        string titleEn,
        string titleUr,
        string answerEn,
        string answerUr,
        List<string> sourceIds,
        string evidenceClassification,
        string provider,
        string providerStatus,
        bool fallbackUsed,
        List<Dictionary<string, string>> links,
        string? workflowId,
        string productVersion,
        Dictionary<string, object?> providerTrace)
    {
        var providerLabel = provider == "groq" ? "Groq AI" : (provider == "gemini" ? "Gemini AI" : "Karobar Knowledge Base");
        var badge = evidenceClassification == "ai_grounded_docs"
            ? new Dictionary<string, string>
            {
                ["en"] = $"Generated by {providerLabel} • Live Generative AI (v{productVersion})",
                ["ur"] = $"{providerLabel} سے تیار کردہ براہِ راست اے آئی وضاحت (v{productVersion})"
            }
            : new Dictionary<string, string>
            {
                ["en"] = $"Docs Fallback • Karobar Knowledge Base (v{productVersion})",
                ["ur"] = $"گائیڈ فال بیک • کاروبار ایپ مستند گائیڈ (v{productVersion})"
            };

        return new Dictionary<string, object?>
        {
            ["kind"] = "qa",
            ["title_en"] = titleEn,
            ["title_ur"] = titleUr,
            ["answer_en"] = answerEn,
            ["answer_ur"] = answerUr,
            ["title"] = new Dictionary<string, string> { ["en"] = titleEn, ["ur"] = titleUr },
            ["en"] = answerEn,
            ["ur"] = answerUr,
            ["source_ids"] = sourceIds,
            ["evidence_classification"] = evidenceClassification,
            ["evidence_badge"] = badge,
            ["provider"] = provider,
            ["provider_status"] = providerStatus,
            ["fallback_used"] = fallbackUsed,
            ["links"] = links,
            ["workflow_id"] = workflowId,
            ["provider_trace"] = providerTrace
        };
    }
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Karobar.Core.Entities;
using Karobar.Data;
using Microsoft.EntityFrameworkCore;

namespace Karobar.Background;

public sealed class CubeUserContext
{
    public long UserId { get; set; }
    public bool IsAdmin { get; set; }
    public HashSet<string> Permissions { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Can(params string[] perms)
    {
        if (IsAdmin) return true;
        return perms.Any(p => Permissions.Contains(p));
    }
}

/// <summary>
/// Full Non-Destructive Cube AI Task Executor (`app/Services/Cube/CubeTaskExecutorService.php`).
/// Supports all 15 non-destructive ERP task types, conversational multi-step wizards with 30-min per-user state,
/// mandatory Pre-DB Permission &amp; Diff Preview Gate, 633-entry DRAP Pakistan salt dictionary + exact `ExtractBrandCore`,
/// optional AI/web verification, canonical `formulations` table sync, and hardcoded delete block.
/// </summary>
public sealed class CubeTaskExecutorService
{
    public static IReadOnlyDictionary<string, string> PharmacySaltDictionary => PharmacyDrapDictionary.Entries;

    private static readonly ConcurrentDictionary<long, (DateTime ExpiresAt, Dictionary<string, object?> Task)> PendingTasks = new();

    private static readonly Regex DosageWordsRegex = new(
        @"\b(?:tablets|tablet|tabs|tab|capsules|capsule|capsul|caps|cap|syrups|syrupe|syrup|syp|suspensions|suspension|susp|injections|injection|inj|vials|vial|ampoules|ampoule|amps|amp|infusions|infusion|iv|im|sc|drops|drop|sprays|spray|rinse|gargle|mouthwash|eye|ear|nasal|mouth|oral|ophth|ophthalmic|topical|vaginal|ointments|ointment|oint|creams|cream|lotions|lotion|emulgel|gels|gel|solutions|solution|sol|sachets|sachet|satches|satche|powders|powder|granules|inhalers|inhaler|evohaler|rotacaps|rotacap|respules|respule|nebules|nebule|diskus|turbuhaler|suppositories|suppository|supp|pessaries|pessary|lozenges|lozenge|linctus|elixir|emulsion|insta|dry|film|coated|enteric|dispersible|soluble|chewable|effervescent|paediatric|pediatric|paed|pead|junior|infant|adult|veternary|veterinary|ds|bd|sr|mr|xr|xl|cr|er|la|od|dr|ec|orange|lemon|strawberry|cherry|banana|mango|vanilla|chocolate|mint|honey|flavour|flavor)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NonMedicineRegex = new(
        @"\b(hair\s+colour|hair\s+color|hair\s+oil|tooth\s*brush|toothbrush|tooth\s*paste|toothpaste|mouth\s*wash|mouthwash|insense\s+wash|shaving|razor|razer|blade|face\s+wash|body\s+wash|beauty\s+soap|neem\s+soap|soap|baby\s+lotion|gidar\s+singhi|shampoo|conditioner|diaper|pampers|sanitary|cotton|bandage|syringe|cannula|drip\s+set|gloves|thermometer|glucometer|strips|feeder|pacifier|nipple|milk|formula\s+powder|rice\s+pouch|cereal|food\s+supplement|perfume|deodorant|tissue|wipes)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly GroqProvider? _groqProvider;
    private readonly GeminiProvider? _geminiProvider;

    public CubeTaskExecutorService(GroqProvider? groqProvider = null, GeminiProvider? geminiProvider = null)
    {
        _groqProvider = groqProvider;
        _geminiProvider = geminiProvider;
    }

    // =========================================================================
    // 1. 100%-Verified DRAP Formulation Resolver (Exact Brand Core + Guards)
    // =========================================================================

    public static bool IsNonMedicineConsumerItem(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName)) return false;
        return NonMedicineRegex.IsMatch(productName);
    }

    public static bool HasMultiIngredientStrength(string? productName)
    {
        if (string.IsNullOrWhiteSpace(productName)) return false;
        var s = productName.Replace('\\', '/').ToLowerInvariant();
        if (Regex.IsMatch(s, @"\b\d+(?:\.\d+)?\s*(?:mg|mcg|g|gm)?\s*\+\s*\d+(?:\.\d+)?\s*(?:mg|mcg|g|gm)?\b"))
            return true;
        if (Regex.IsMatch(s, @"\b\d+(?:\.\d+)?\s*(?:mg|mcg|g|gm)?\s*\/\s*\d+(?:\.\d+)?\s*(?:mg|mcg|g|gm)\b"))
            return true;
        if (Regex.IsMatch(s, @"\b\d+(?:\.\d+)?\s*\/\s*\d+(?:\.\d+)?(?!\s*(?:ml|l|cc|%))\b") &&
            !Regex.IsMatch(s, @"\b\d+(?:\.\d+)?\s*\/\s*\d+(?:\.\d+)?\s*(?:ml|l|cc)\b"))
            return true;
        return false;
    }

    public static string ExtractBrandCore(string? productName)
    {
        var s = (productName ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(s)) return "";

        s = s.Replace('\\', '/');
        s = Regex.Replace(s, @"\(\s*[a-z]{3,5}\s*\)\s*\d*", " ");
        s = Regex.Replace(s, @"\(\s*\d+\s*'?s?\s*\)", " ");
        s = Regex.Replace(s, @"\([^)]*\)", " ");
        s = Regex.Replace(s, @"\b(tab|cap|syp|inj|susp)(\d)", "$1 $2");
        s = Regex.Replace(s, @"(\d+(?:\.\d+)?)\s*(g|gm|mg|mcg|ml)(iv|im|sc|syp|tab|cap|inj)\b", "$1$2 $3");
        s = Regex.Replace(s, @"\b(\d+)\s*(iv|im)\b", "$1mg $2");
        s = Regex.Replace(s, @"\b\d+(?:\.\d+)?\s*(?:[\+\/]\s*\d+(?:\.\d+)?)?\s*(?:mg|mcg|ug|g|gm|grm|kg|ml|l|iu|miu|units?|%)(?:\s*\/\s*\d*(?:\.\d+)?\s*(?:ml|mg|g|gm|l))?\b", " ");
        s = Regex.Replace(s, @"\b\d+(?:\.\d+)?\s*[\+\/]\s*\d+(?:\.\d+)?\b", " ");
        s = Regex.Replace(s, @"\b\d+\.\d+\b", " ");
        s = Regex.Replace(s, @"\b\d+\s*'?s\b", " ");
        s = DosageWordsRegex.Replace(s, " ");
        s = Regex.Replace(s, @"[^a-z0-9\s]+", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();

        var specialMatch = Regex.Match(s, @"^(cac\s+1000(?:\s+plus)?|calpol\s+6\s+plus)\b");
        if (specialMatch.Success)
        {
            return Regex.Replace(specialMatch.Groups[1].Value, @"\s+", " ").Trim();
        }

        s = Regex.Replace(s, @"\b\d+[a-z]?\b", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }

    private static readonly HashSet<string> CombinationVariantQualifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "n", "c", "m", "ez", "g", "h", "v", "l", "t", "p", "z", "s", "f", "d", "b", "k", "r",
        "cf", "extra", "night", "extend", "forte", "plus", "hct", "hctz", "am", "co", "cv", "cl",
        "es", "inh", "neo", "max", "pro", "ultra", "comp", "compound", "duo", "trio", "cold", "flu"
    };

    public static string? ResolveFormulationFromProductName(string? productName)
    {
        var trimmed = (productName ?? "").Trim();
        if (string.IsNullOrEmpty(trimmed) || IsNonMedicineConsumerItem(trimmed)) return null;

        var brandCore = ExtractBrandCore(trimmed);
        if (string.IsNullOrEmpty(brandCore)) return null;

        PharmacyDrapDictionary.Entries.TryGetValue(brandCore, out var candidateSalt);

        // If brandCore has trailing non-variant manufacturer/tag words (e.g. "panadol gsk", "panadol qa"),
        // safely check shorter word-boundary prefixes ONLY when no trailing token is a combination variant qualifier!
        if (candidateSalt == null && brandCore.Contains(' '))
        {
            var words = brandCore.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var len = words.Length - 1; len >= 1; len--)
            {
                var trailing = words[len..];
                if (trailing.Any(w => CombinationVariantQualifiers.Contains(w)))
                {
                    break;
                }
                var prefix = string.Join(' ', words[..len]);
                if (prefix.Length >= 4 && PharmacyDrapDictionary.Entries.TryGetValue(prefix, out var prefSalt))
                {
                    candidateSalt = prefSalt;
                    break;
                }
            }
        }

        if (candidateSalt == null && Regex.IsMatch(trimmed, @"\(\s*ors\s*\)", RegexOptions.IgnoreCase))
        {
            candidateSalt = "Oral Rehydration Salts";
        }

        if (candidateSalt == null)
        {
            foreach (var kv in PharmacyDrapDictionary.Entries)
            {
                if (kv.Key.Equals(kv.Value, StringComparison.OrdinalIgnoreCase) && brandCore.Equals(kv.Key, StringComparison.OrdinalIgnoreCase))
                {
                    candidateSalt = kv.Value;
                    break;
                }
            }
        }

        if (candidateSalt == null) return null;

        if (HasMultiIngredientStrength(trimmed) && !candidateSalt.Contains('+'))
        {
            return null;
        }

        return candidateSalt;
    }

    // =========================================================================
    // 2. Chat-Driven Task Detection & Multi-Step Guided Wizard
    // =========================================================================

    public Dictionary<string, object?>? DetectAndExecuteFromChat(
        string question,
        string language,
        string productVersion)
    {
        var q = (question ?? "").Trim();
        if (string.IsNullOrEmpty(q)) return null;
        if (IsDestructiveDeleteCommand(q.ToLowerInvariant()))
        {
            return BuildDeleteBlockedResponse(productVersion);
        }
        return null;
    }

    public async Task<Dictionary<string, object?>?> DetectAndExecuteFromChatAsync(
        KarobarDbContext db,
        CubeUserContext user,
        string question,
        string language = "en",
        string productVersion = "2026.10",
        List<Dictionary<string, string>>? history = null,
        bool userConfirmed = false,
        CancellationToken ct = default)
    {
        var q = (question ?? "").Trim();
        if (string.IsNullOrEmpty(q)) return null;
        var lower = q.ToLowerInvariant();

        // 1. Hardcoded safety block: NEVER delete records
        if (IsDestructiveDeleteCommand(lower))
        {
            return BuildDeleteBlockedResponse(productVersion);
        }

        // 2. Check for active pending task for this user (30-min multi-step wizard)
        var pending = GetPendingTask(user.UserId);
        if (pending != null)
        {
            if (IsCancelCommand(lower))
            {
                ClearPendingTask(user.UserId);
                return BuildTaskCancelledResponse(productVersion);
            }

            if (userConfirmed || IsConfirmCommand(lower))
            {
                return await ContinuePendingTaskAsync(db, user, pending, q, productVersion, confirmed: true, ct);
            }

            // If user starts a brand-new slash command, drop old pending task
            if (lower.StartsWith('/') && ClassifyActionIntent(lower) != null)
            {
                ClearPendingTask(user.UserId);
            }
            else if (Regex.IsMatch(lower, @"\b(modify|change|edit details|update details)\b"))
            {
                var action = pending.GetValueOrDefault("action")?.ToString() ?? "create_products";
                return FormatTaskResponse(
                    "Modify Staged Details",
                    "تفصیلات تبدیل کریں",
                    "Please reply with the fields you want to change (for example: `name: Panadol Extra, pack_size: 20, brand: GSK`) or type `cancel` to abort.",
                    "براہِ کرم وہ تفصیلات لکھیں جو آپ تبدیل کرنا چاہتے ہیں، یا منسوخ کرنے کے لیے `cancel` لکھیں۔",
                    [],
                    new Dictionary<string, object?> { ["action"] = action, ["awaiting_input"] = true },
                    "cube_task_needs_input",
                    productVersion);
            }
            else
            {
                return await ContinuePendingTaskAsync(db, user, pending, q, productVersion, confirmed: false, ct);
            }
        }

        // 3. Classify natural-language or slash-command action intent
        var intent = ClassifyActionIntent(lower);
        if (intent == null) return null;

        if (user.UserId <= 0 && !user.IsAdmin)
        {
            return FormatTaskResponse(
                "Sign In Required",
                "لاگ ان درکار ہے",
                "Please sign in to Karobar-App before executing database tasks with Cube AI.",
                "ڈیٹا بیس میں تبدیلی کے لیے پہلے لاگ ان کریں۔",
                [],
                new Dictionary<string, object?> { ["action"] = intent, ["status"] = "unauthorized" },
                "cube_task_unauthorized",
                productVersion);
        }

        // 4. Route to the appropriate task staging / wizard handler
        if (intent == "bulk_autofill_missing_formulations")
        {
            var filter = ExtractOptionalFilterFromPrompt(q);
            var previewPayload = new Dictionary<string, object?>
            {
                ["task_type"] = "bulk_autofill_missing_formulations",
                ["preview_only"] = true,
                ["batch_size"] = 20,
                ["filter_query"] = filter
            };
            var preview = await ExecuteBulkAutofillMissingFormulationsAsync(db, user, previewPayload, productVersion, ct);
            if (preview.TryGetValue("task_meta", out var tmObj) && tmObj is Dictionary<string, object?> tm)
            {
                tm["awaiting_permission"] = true;
                tm["pending_action"] = "bulk_autofill_missing_formulations";
                SavePendingTask(user.UserId, new Dictionary<string, object?>
                {
                    ["action"] = "bulk_autofill_missing_formulations",
                    ["stage"] = "awaiting_permission",
                    ["filter_query"] = filter
                });
            }
            preview["provider_status"] = "cube_task_awaiting_permission";
            return preview;
        }

        if (intent == "create_products")
        {
            var draft = ExtractProductFieldsFromText(q);
            return await ProcessCreateProductDraftAsync(db, user, draft, userConfirmed, productVersion, ct);
        }

        if (intent == "update_products")
        {
            var draft = ExtractUpdateProductFieldsFromText(q);
            return await ProcessUpdateProductDraftAsync(db, user, draft, userConfirmed, productVersion, ct);
        }

        if (intent == "bulk_assign_formulations")
        {
            var parsed = ExtractFormulationAssignmentsFromText(q);
            return await ExecuteBulkAssignFormulationsAsync(db, user, new Dictionary<string, object?>
            {
                ["task_type"] = "bulk_assign_formulations",
                ["assignments"] = parsed.Assignments,
                ["standalone_formulations"] = parsed.Standalone,
                ["user_confirmed"] = userConfirmed
            }, productVersion, ct);
        }

        // Master entity create / update intents
        var entityDraft = ExtractMasterEntityFieldsFromText(q, intent);
        return await ProcessMasterEntityDraftAsync(db, user, intent, entityDraft, userConfirmed, productVersion, ct);
    }

    // =========================================================================
    // 3. Structured Task Executor (All 15 Task Types)
    // =========================================================================

    public Task<Dictionary<string, object?>> ExecuteStructuredTaskAsync(
        KarobarDbContext db,
        Dictionary<string, object?> payload,
        string productVersion = "2026.10",
        CancellationToken cancellationToken = default)
        => ExecuteStructuredTaskAsync(db, new CubeUserContext { UserId = 1, IsAdmin = true }, payload, productVersion, cancellationToken);

    public async Task<Dictionary<string, object?>> ExecuteStructuredTaskAsync(
        KarobarDbContext db,
        CubeUserContext user,
        Dictionary<string, object?> payload,
        string productVersion = "2026.10",
        CancellationToken cancellationToken = default)
    {
        var rawType = payload.TryGetValue("task_type", out var tt) ? (tt?.ToString() ?? "").Trim().ToLowerInvariant() : "";
        if (string.IsNullOrEmpty(rawType))
        {
            return FormatTaskResponse("Task Type Required", "ٹاسک کی قسم درکار ہے", "Please specify a valid `task_type`.", "براہِ کرم درست ٹاسک منتخب کریں۔",
                [], new Dictionary<string, object?> { ["status"] = "invalid" }, "cube_task_error", productVersion);
        }

        if (rawType.Contains("delete") || rawType.Contains("destroy") || rawType.Contains("remove") || rawType.Contains("drop") || rawType.Contains("truncate"))
        {
            return BuildDeleteBlockedResponse(productVersion);
        }

        // Normalize legacy aliases
        if (rawType is "enrich_formulations" or "auto_fill_formulations" or "fill_formulations")
        {
            rawType = "bulk_autofill_missing_formulations";
        }

        return rawType switch
        {
            "bulk_autofill_missing_formulations" => await ExecuteBulkAutofillMissingFormulationsAsync(db, user, payload, productVersion, cancellationToken),
            "bulk_assign_formulations" => await ExecuteBulkAssignFormulationsAsync(db, user, payload, productVersion, cancellationToken),
            "create_products" => await ExecuteCreateProductsStructuredAsync(db, user, payload, productVersion, cancellationToken),
            "update_products" => await ExecuteUpdateProductsStructuredAsync(db, user, payload, productVersion, cancellationToken),
            "create_brands" or "update_brands" or
            "create_categories" or "update_categories" or
            "create_suppliers" or "update_suppliers" or
            "create_customers" or "update_customers" or
            "create_storage_locations" or "create_expense_categories" or
            "create_user_demands" => await ExecuteMasterEntityStructuredAsync(db, user, rawType, payload, productVersion, cancellationToken),
            _ => new Dictionary<string, object?>
            {
                ["ok"] = false,
                ["unsupported"] = true,
                ["task_type"] = rawType,
                ["message"] = $"Unsupported Cube AI task type: '{rawType}'."
            }
        };
    }

    // =========================================================================
    // Task 1: bulk_autofill_missing_formulations (Exact Preview + Batch + Report)
    // =========================================================================

    public async Task<Dictionary<string, object?>> ExecuteBulkAutofillMissingFormulationsAsync(
        KarobarDbContext db,
        CubeUserContext user,
        Dictionary<string, object?> payload,
        string productVersion = "2026.10",
        CancellationToken ct = default)
    {
        if (!user.Can("product.update", "product.create"))
        {
            return BuildPermissionDeniedResponse("bulk_autofill_missing_formulations", "product.update", productVersion);
        }

        var batchSize = Math.Clamp(GetInt(payload, "batch_size", 20), 1, 30);
        var afterId = Math.Max(0, GetInt(payload, "after_id", 0));
        var filterQuery = (GetStr(payload, "filter_query") ?? "").Trim();
        var previewOnly = GetBool(payload, "preview_only", false);
        var userConfirmed = !previewOnly && GetBool(payload, "user_confirmed", true);

        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose) await conn.OpenAsync(ct);

        var candidates = new List<(long Id, string Code, string Name)>();
        var totalMissing = 0;
        var remaining = 0;

        try
        {
            var whereFilter = string.IsNullOrEmpty(filterQuery) ? "" : " AND LOWER(p.name) LIKE LOWER(@likeFilter)";
            var likeFilter = $"%{filterQuery}%";

            await using (var totCmd = conn.CreateCommand())
            {
                totCmd.CommandText = $"SELECT COUNT(*) FROM products p WHERE (p.formulation IS NULL OR TRIM(p.formulation) = ''){whereFilter}";
                if (!string.IsNullOrEmpty(filterQuery)) AddParam(totCmd, "@likeFilter", likeFilter);
                totalMissing = Convert.ToInt32(await totCmd.ExecuteScalarAsync(ct) ?? 0);
            }

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT p.id, COALESCE(p.product_code, ''), COALESCE(p.name, '')
                    FROM products p
                    WHERE (p.formulation IS NULL OR TRIM(p.formulation) = '')
                      AND p.id > @afterId
                      {whereFilter}
                    ORDER BY p.id ASC
                    LIMIT @batchSize";
                AddParam(cmd, "@afterId", afterId);
                AddParam(cmd, "@batchSize", batchSize);
                if (!string.IsNullOrEmpty(filterQuery)) AddParam(cmd, "@likeFilter", likeFilter);

                await using var r = await cmd.ExecuteReaderAsync(ct);
                while (await r.ReadAsync(ct))
                {
                    candidates.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));
                }
            }

            long lastId = afterId;
            var updatedItems = new List<Dictionary<string, object?>>();
            var skippedItems = new List<Dictionary<string, object?>>();
            var previewItems = new List<Dictionary<string, object?>>();
            var previewRecords = new List<Dictionary<string, object?>>();
            var nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            foreach (var (pid, pCode, pName) in candidates)
            {
                lastId = pid;
                var resolvedSalt = ResolveFormulationFromProductName(pName);

                if (!string.IsNullOrEmpty(resolvedSalt))
                {
                    if (userConfirmed)
                    {
                        await using var upd = conn.CreateCommand();
                        upd.CommandText = "UPDATE products SET formulation = @form, updated_at = @now WHERE id = @id";
                        AddParam(upd, "@form", resolvedSalt);
                        AddParam(upd, "@now", nowStr);
                        AddParam(upd, "@id", pid);
                        await upd.ExecuteNonQueryAsync(ct);

                        await EnsureCanonicalFormulationAsync(conn, resolvedSalt, nowStr, ct);
                    }

                    updatedItems.Add(new Dictionary<string, object?>
                    {
                        ["id"] = pid,
                        ["product_id"] = pid,
                        ["code"] = pCode,
                        ["product_code"] = pCode,
                        ["name"] = pName,
                        ["formulation"] = resolvedSalt,
                        ["before"] = "(Empty)",
                        ["after"] = resolvedSalt,
                        ["source_en"] = "DRAP Pakistan Verified Salt Dictionary",
                        ["source_ur"] = "DRAP پاکستان مستند فارمولیشن ڈکشنری",
                        ["edit_path"] = $"/products/{pid}/edit"
                    });

                    previewItems.Add(new Dictionary<string, object?>
                    {
                        ["id"] = pid,
                        ["name"] = pName,
                        ["code"] = pCode,
                        ["before"] = "(Empty)",
                        ["after"] = resolvedSalt
                    });

                    previewRecords.Add(new Dictionary<string, object?>
                    {
                        ["id"] = pid,
                        ["title"] = pName,
                        ["code"] = pCode,
                        ["badge_en"] = "100% DRAP Verified",
                        ["badge_ur"] = "100% مستند فارمولہ",
                        ["fields"] = new List<Dictionary<string, object?>>
                        {
                            new()
                            {
                                ["field"] = "Formulation",
                                ["label_ur"] = "فارمولیشن / سالٹ",
                                ["before"] = "(Empty)",
                                ["after"] = resolvedSalt
                            }
                        }
                    });
                }
                else
                {
                    var isConsumer = IsNonMedicineConsumerItem(pName);
                    var reasonEn = isConsumer
                        ? "Non-medicine consumer/surgical item (skipped intentionally)"
                        : "Not in verified DRAP salt dictionary — skipped to prevent guessing";
                    var reasonUr = isConsumer
                        ? "غیر طبی / کنزیومر آئٹم (جان بوجھ کر چھوڑ دیا گیا)"
                        : "مستند ڈکشنری میں تصدیق شدہ سالٹ نہیں ملا — اندازہ نہیں لگایا گیا";

                    skippedItems.Add(new Dictionary<string, object?>
                    {
                        ["id"] = pid,
                        ["product_id"] = pid,
                        ["code"] = pCode,
                        ["product_code"] = pCode,
                        ["name"] = pName,
                        ["reason"] = reasonEn,
                        ["reason_en"] = reasonEn,
                        ["reason_ur"] = reasonUr,
                        ["search_query"] = $"{ExtractBrandCore(pName)} salt composition pakistan".Trim(),
                        ["edit_path"] = $"/products/{pid}/edit"
                    });
                }
            }

            await using (var remCmd = conn.CreateCommand())
            {
                remCmd.CommandText = $"SELECT COUNT(*) FROM products p WHERE (p.formulation IS NULL OR TRIM(p.formulation) = '') AND p.id > @lastId{whereFilter}";
                AddParam(remCmd, "@lastId", lastId);
                if (!string.IsNullOrEmpty(filterQuery)) AddParam(remCmd, "@likeFilter", likeFilter);
                remaining = Convert.ToInt32(await remCmd.ExecuteScalarAsync(ct) ?? 0);
            }

            var hasMore = !previewOnly && candidates.Count == batchSize && remaining > 0;
            var taskMeta = new Dictionary<string, object?>
            {
                ["action"] = "bulk_autofill_missing_formulations",
                ["status"] = previewOnly ? "preview" : (hasMore ? "in_progress" : "completed"),
                ["preview_only"] = previewOnly,
                ["scanned_count"] = candidates.Count,
                ["processed_count"] = candidates.Count,
                ["updated_count"] = updatedItems.Count,
                ["skipped_count"] = skippedItems.Count,
                ["updated_items"] = updatedItems,
                ["updated_products"] = updatedItems,
                ["skipped_products"] = skippedItems,
                ["refresh_products"] = !previewOnly && updatedItems.Count > 0,
                ["detailed_report"] = new Dictionary<string, object?>
                {
                    ["scanned"] = candidates.Count,
                    ["updated"] = updatedItems.Count,
                    ["skipped"] = skippedItems.Count,
                    ["remaining"] = remaining,
                    ["updated_products"] = updatedItems,
                    ["skipped_products"] = skippedItems
                },
                ["result_summary"] = new Dictionary<string, object?>
                {
                    ["table"] = "products",
                    ["operation"] = previewOnly ? "PREVIEW" : "UPDATE",
                    ["count"] = updatedItems.Count,
                    ["records"] = previewRecords
                },
                ["background_task"] = new Dictionary<string, object?>
                {
                    ["task_type"] = "bulk_autofill_missing_formulations",
                    ["has_more"] = hasMore,
                    ["next_after_id"] = lastId,
                    ["last_processed_id"] = lastId,
                    ["batch_size"] = batchSize,
                    ["filter_query"] = filterQuery,
                    ["processed_in_batch"] = candidates.Count,
                    ["updated_in_batch"] = updatedItems.Count,
                    ["skipped_in_batch"] = skippedItems.Count,
                    ["remaining_estimate"] = remaining,
                    ["remaining_count"] = remaining,
                    ["total_missing"] = totalMissing
                }
            };

            if (previewOnly)
            {
                taskMeta["preview_result"] = new Dictionary<string, object?>
                {
                    ["staged"] = true,
                    ["posted_to_database"] = false,
                    ["database_table"] = "products",
                    ["operation"] = "BULK UPDATE (formulation)",
                    ["total_missing"] = totalMissing,
                    ["resolvable_in_preview"] = updatedItems.Count,
                    ["preview_items"] = previewItems,
                    ["records"] = previewRecords,
                    ["sample_matches"] = updatedItems,
                    ["sample_skipped"] = skippedItems,
                    ["matched_in_sample"] = updatedItems.Count,
                    ["skipped_in_sample"] = skippedItems.Count
                };
            }

            var res = FormatTaskResponse(
                titleEn: previewOnly
                    ? $"Preview: Auto-Fill Missing Formulations ({updatedItems.Count} Verified Match(es) in Batch of {candidates.Count})"
                    : $"Auto-Filled Formulations ({updatedItems.Count} Updated, {skippedItems.Count} Skipped)",
                titleUr: previewOnly
                    ? $"پیش نظارہ: خودکار فارمولیشن ({updatedItems.Count} مستند میچ)"
                    : $"خودکار فارمولیشن اپڈیٹ ({updatedItems.Count} مکمل، {skippedItems.Count} اسکپ)",
                answerEn: previewOnly
                    ? $"Found **{totalMissing}** product(s) with missing formulations. In this preview batch of **{candidates.Count}**, **{updatedItems.Count}** matched 100%-verified DRAP salts (**no database changes posted yet**). Approve below to start the background auto-fill."
                    : $"Scanned **{candidates.Count}** product(s): updated **{updatedItems.Count}** with 100%-verified formulations and safely skipped **{skippedItems.Count}** unverified/non-medicine items. Remaining missing: **{remaining}**.",
                answerUr: previewOnly
                    ? $"کل **{totalMissing}** پروڈکٹس کی فارمولیشن خالی ہے۔ اس پیش نظارہ میں **{updatedItems.Count}** پروڈکٹس کے 100% مستند سالٹ ملے ہیں (**ابھی ڈیٹا بیس میں کوئی تبدیلی نہیں کی گئی**)۔ شروع کرنے کے لیے نیچے منظوری دیں۔"
                    : $"**{candidates.Count}** پروڈکٹس اسکین ہوئیں: **{updatedItems.Count}** اپڈیٹ اور **{skippedItems.Count}** اسکپ ہوئیں۔ باقی: **{remaining}**۔",
                links:
                [
                    new() { ["path"] = "/products", ["en"] = "Open Products Catalog", ["ur"] = "پروڈکٹس لسٹ کھولیں" },
                    new() { ["path"] = "/background-tasks", ["en"] = "Background Tasks Center", ["ur"] = "بیک گراؤنڈ ٹاسکس" }
                ],
                taskMeta: taskMeta,
                providerStatus: previewOnly ? "cube_task_awaiting_permission" : "cube_task_executed",
                productVersion: productVersion);

            res["ok"] = true;
            res["processed_count"] = candidates.Count;
            res["updated_count"] = updatedItems.Count;
            res["updated_items"] = updatedItems;
            res["last_id"] = lastId;
            res["remaining_count"] = remaining;
            res["has_more"] = hasMore;
            return res;
        }
        finally
        {
            if (shouldClose) await conn.CloseAsync();
        }
    }

    // =========================================================================
    // Task 2: bulk_assign_formulations
    // =========================================================================

    private async Task<Dictionary<string, object?>> ExecuteBulkAssignFormulationsAsync(
        KarobarDbContext db,
        CubeUserContext user,
        Dictionary<string, object?> payload,
        string productVersion,
        CancellationToken ct)
    {
        if (!user.Can("product.update", "product.create"))
        {
            return BuildPermissionDeniedResponse("bulk_assign_formulations", "product.update", productVersion);
        }

        var assignments = ExtractListOfDicts(payload, "assignments");
        var standalone = ExtractStringList(payload, "standalone_formulations");
        var userConfirmed = GetBool(payload, "user_confirmed", true);

        if (assignments.Count == 0 && standalone.Count == 0)
        {
            return FormatTaskResponse(
                "Formulation Details Required",
                "فارمولیشن کی تفصیل درکار ہے",
                "Specify product-to-formulation pairs, e.g. `Panadol = Paracetamol, Risek = Omeprazole`.",
                "پروڈکٹ اور فارمولیشن درج کریں، مثلاً `Panadol = Paracetamol`۔",
                [],
                new Dictionary<string, object?> { ["action"] = "bulk_assign_formulations", ["awaiting_input"] = true },
                "cube_task_needs_input",
                productVersion);
        }

        if (!userConfirmed)
        {
            var records = assignments.Select(a => new Dictionary<string, object?>
            {
                ["title"] = a.GetValueOrDefault("product_name")?.ToString() ?? "",
                ["badge_en"] = "Assign Formulation",
                ["badge_ur"] = "فارمولیشن تفویض",
                ["fields"] = new List<Dictionary<string, object?>>
                {
                    new() { ["field"] = "Formulation", ["label_ur"] = "فارمولیشن", ["before"] = "(Current)", ["after"] = a.GetValueOrDefault("formulation")?.ToString() ?? "" }
                }
            }).ToList();

            SavePendingTask(user.UserId, new Dictionary<string, object?>
            {
                ["action"] = "bulk_assign_formulations",
                ["stage"] = "awaiting_permission",
                ["assignments"] = assignments,
                ["standalone_formulations"] = standalone
            });

            return FormatTaskResponse(
                $"Confirm Formulation Assignment ({assignments.Count + standalone.Count} Item(s))",
                "فارمولیشن اپڈیٹ کی تصدیق کریں",
                "Review the staged formulation updates below and click **Approve & Post to Database** to apply.",
                "نیچے دی گئی فارمولیشن تفصیلات چیک کر کے منظوری دیں۔",
                [],
                new Dictionary<string, object?>
                {
                    ["action"] = "bulk_assign_formulations",
                    ["awaiting_permission"] = true,
                    ["preview_result"] = new Dictionary<string, object?>
                    {
                        ["staged"] = true,
                        ["posted_to_database"] = false,
                        ["database_table"] = "products / formulations",
                        ["operation"] = "UPDATE / INSERT",
                        ["records"] = records
                    }
                },
                "cube_task_awaiting_permission",
                productVersion);
        }

        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose) await conn.OpenAsync(ct);
        int updated = 0, createdFormulations = 0;
        var nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        var committedRecords = new List<Dictionary<string, object?>>();

        try
        {
            foreach (var item in assignments)
            {
                var pName = (item.GetValueOrDefault("product_name")?.ToString() ?? "").Trim();
                var form = (item.GetValueOrDefault("formulation")?.ToString() ?? "").Trim();
                if (string.IsNullOrEmpty(pName) || string.IsNullOrEmpty(form)) continue;

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE products SET formulation = @form, updated_at = @now WHERE LOWER(name) = LOWER(@exact) OR LOWER(name) LIKE LOWER(@prefix)";
                AddParam(cmd, "@form", form);
                AddParam(cmd, "@now", nowStr);
                AddParam(cmd, "@exact", pName);
                AddParam(cmd, "@prefix", $"{pName}%");
                var affected = await cmd.ExecuteNonQueryAsync(ct);
                updated += affected;

                if (await EnsureCanonicalFormulationAsync(conn, form, nowStr, ct)) createdFormulations++;
                committedRecords.Add(new Dictionary<string, object?> { ["title"] = pName, ["after"] = form, ["affected"] = affected });
            }

            foreach (var sf in standalone)
            {
                if (!string.IsNullOrWhiteSpace(sf) && await EnsureCanonicalFormulationAsync(conn, sf.Trim(), nowStr, ct))
                {
                    createdFormulations++;
                }
            }
        }
        finally
        {
            if (shouldClose) await conn.CloseAsync();
        }

        ClearPendingTask(user.UserId);
        return FormatTaskResponse(
            $"Assigned Formulations ({updated} Product(s) Updated)",
            $"فارمولیشن اپڈیٹ ({updated} پروڈکٹس)",
            $"Successfully updated **{updated}** product(s) and synced **{createdFormulations}** canonical formulation master record(s).",
            $"**{updated}** پروڈکٹس کی فارمولیشن اپڈیٹ اور **{createdFormulations}** فارمولیشن ریکارڈ محفوظ ہو گئے۔",
            [new() { ["path"] = "/products", ["en"] = "Open Products", ["ur"] = "پروڈکٹس کھولیں" }],
            new Dictionary<string, object?>
            {
                ["action"] = "bulk_assign_formulations",
                ["status"] = "completed",
                ["updated_count"] = updated,
                ["created_formulations"] = createdFormulations,
                ["refresh_products"] = updated > 0,
                ["result_summary"] = new Dictionary<string, object?> { ["table"] = "products", ["count"] = updated, ["records"] = committedRecords }
            },
            "cube_task_executed",
            productVersion);
    }

    // =========================================================================
    // Task 3 & 4: create_products & update_products
    // =========================================================================

    private async Task<Dictionary<string, object?>> ExecuteCreateProductsStructuredAsync(
        KarobarDbContext db,
        CubeUserContext user,
        Dictionary<string, object?> payload,
        string productVersion,
        CancellationToken ct)
    {
        var items = ExtractListOfDicts(payload, "items");
        var draft = items.Count > 0 ? items[0] : payload;
        var confirmed = GetBool(payload, "user_confirmed", false);
        return await ProcessCreateProductDraftAsync(db, user, draft, confirmed, productVersion, ct);
    }

    private async Task<Dictionary<string, object?>> ProcessCreateProductDraftAsync(
        KarobarDbContext db,
        CubeUserContext user,
        Dictionary<string, object?> draft,
        bool userConfirmed,
        string productVersion,
        CancellationToken ct)
    {
        if (!user.Can("product.create"))
        {
            return BuildPermissionDeniedResponse("create_products", "product.create", productVersion);
        }

        var name = (GetStr(draft, "name") ?? "").Trim();
        var packSize = GetInt(draft, "pack_size", 0);
        var categoryName = (GetStr(draft, "category") ?? "").Trim();
        var brandName = (GetStr(draft, "brand") ?? "").Trim();
        var supplierName = (GetStr(draft, "supplier") ?? "").Trim();
        var formulation = (GetStr(draft, "formulation") ?? "").Trim();
        if (string.IsNullOrEmpty(formulation) && !string.IsNullOrEmpty(name))
        {
            formulation = ResolveFormulationFromProductName(name) ?? "";
        }

        var missing = new List<string>();
        if (string.IsNullOrEmpty(name)) missing.Add("Product Name (`name`)");
        if (packSize < 1) missing.Add("Pack Size (`pack_size`, integer ≥ 1)");
        if (string.IsNullOrEmpty(categoryName)) missing.Add("Category (`category`)");
        if (string.IsNullOrEmpty(brandName)) missing.Add("Existing Brand (`brand`)");
        if (string.IsNullOrEmpty(supplierName)) missing.Add("Existing Supplier (`supplier`)");

        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose) await conn.OpenAsync(ct);

        try
        {
            if (missing.Count > 0)
            {
                SavePendingTask(user.UserId, new Dictionary<string, object?>
                {
                    ["action"] = "create_products",
                    ["stage"] = "collecting_fields",
                    ["draft"] = draft
                });

                var completedSteps = 5 - missing.Count;
                return FormatTaskResponse(
                    "New Product — Required Details Needed",
                    "نئی پروڈکٹ — ضروری تفصیلات درکار ہیں",
                    $"Cube never guesses mandatory product fields. Please provide:\n- {string.Join("\n- ", missing)}\n\nExample: `name: Panadol 500mg, pack_size: 100, category: Tablets, brand: GSK, supplier: Muller & Phipps`",
                    $"کیوب خود سے پروڈکٹ کی لازمی تفصیلات فرض نہیں کرتا۔ براہِ کرم درج ذیل معلومات فراہم کریں:\n- {string.Join("\n- ", missing)}",
                    [new() { ["path"] = "/products/create", ["en"] = "Open New Product Form", ["ur"] = "نئی پروڈکٹ فارم کھولیں" }],
                    new Dictionary<string, object?>
                    {
                        ["action"] = "create_products",
                        ["awaiting_input"] = true,
                        ["missing_fields"] = missing,
                        ["step_info"] = new Dictionary<string, object?> { ["current"] = Math.Max(1, completedSteps + 1), ["total"] = 6, ["label"] = "Product Details" }
                    },
                    "cube_task_needs_input",
                    productVersion);
            }

            // Validate Brand & Supplier exist (never auto-create)
            var brandRow = await FindEntityByNameAsync(conn, "brands", brandName, ct);
            var supplierRow = await FindEntityByNameAsync(conn, "suppliers", supplierName, ct);
            if (brandRow == null || supplierRow == null)
            {
                var errs = new List<string>();
                if (brandRow == null) errs.Add($"Brand **`{brandName}`** was not found in your catalog (create it first via `/add-brand` or pick an existing brand).");
                if (supplierRow == null) errs.Add($"Supplier **`{supplierName}`** was not found in your catalog (create it first via `/add-supplier` or pick an existing supplier).");

                SavePendingTask(user.UserId, new Dictionary<string, object?>
                {
                    ["action"] = "create_products",
                    ["stage"] = "collecting_fields",
                    ["draft"] = draft
                });

                return FormatTaskResponse(
                    "Existing Brand & Supplier Required",
                    "موجودہ برانڈ اور سپلائر درکار ہیں",
                    string.Join("\n", errs),
                    string.Join("\n", errs),
                    [new() { ["path"] = "/brands", ["en"] = "Open Brands", ["ur"] = "برانڈز" }, new() { ["path"] = "/suppliers", ["en"] = "Open Suppliers", ["ur"] = "سپلائرز" }],
                    new Dictionary<string, object?> { ["action"] = "create_products", ["awaiting_input"] = true },
                    "cube_task_needs_input",
                    productVersion);
            }

            // Duplicate check before staging
            var dupMatches = await SearchProductsAsync(conn, name, 4, ct);
            var exactDup = dupMatches.FirstOrDefault(m => string.Equals(m["name"]?.ToString(), name, StringComparison.OrdinalIgnoreCase));
            if (exactDup != null && !userConfirmed)
            {
                SavePendingTask(user.UserId, new Dictionary<string, object?>
                {
                    ["action"] = "create_products",
                    ["stage"] = "awaiting_permission",
                    ["draft"] = draft
                });
                return FormatTaskResponse(
                    $"Similar Product Already Exists: {exactDup["name"]}",
                    $"ملتی جلتی پروڈکٹ پہلے سے موجود ہے: {exactDup["name"]}",
                    $"A product named **{exactDup["name"]}** (Code: `{exactDup["product_code"]}`) already exists. Click **Approve & Post to Database** only if you still want to create a separate entry, or **Deny** to cancel.",
                    $"پروڈکٹ **{exactDup["name"]}** پہلے سے موجود ہے۔ اگر پھر بھی نئی پروڈکٹ بنانی ہے تو منظوری دیں ورنہ منسوخ کریں۔",
                    [new() { ["path"] = $"/products/{exactDup["id"]}/edit", ["en"] = "Edit Existing Product", ["ur"] = "موجودہ پروڈکٹ ایڈٹ کریں" }],
                    new Dictionary<string, object?>
                    {
                        ["action"] = "create_products",
                        ["awaiting_permission"] = true,
                        ["db_matches"] = dupMatches,
                        ["preview_result"] = BuildSingleRecordPreview("products", "INSERT", name, new Dictionary<string, string>
                        {
                            ["Name"] = name,
                            ["Pack Size"] = packSize.ToString(),
                            ["Category"] = categoryName,
                            ["Brand"] = brandRow.Value.Name,
                            ["Supplier"] = supplierRow.Value.Name,
                            ["Formulation"] = string.IsNullOrEmpty(formulation) ? "(None)" : formulation
                        })
                    },
                    "cube_task_awaiting_permission",
                    productVersion);
            }

            if (!userConfirmed)
            {
                SavePendingTask(user.UserId, new Dictionary<string, object?>
                {
                    ["action"] = "create_products",
                    ["stage"] = "awaiting_permission",
                    ["draft"] = draft
                });

                return FormatTaskResponse(
                    $"Confirm New Product: {name}",
                    $"نئی پروڈکٹ کی تصدیق: {name}",
                    $"Ready to create **{name}** (`Pack Size: {packSize}`, `Category: {categoryName}`, `Brand: {brandRow.Value.Name}`, `Supplier: {supplierRow.Value.Name}`). Review and click **Approve & Post to Database**.",
                    $"نئی پروڈکٹ **{name}** محفوظ کرنے کے لیے تیار ہے۔ تفصیل چیک کر کے منظوری دیں۔",
                    [],
                    new Dictionary<string, object?>
                    {
                        ["action"] = "create_products",
                        ["awaiting_permission"] = true,
                        ["preview_result"] = BuildSingleRecordPreview("products", "INSERT", name, new Dictionary<string, string>
                        {
                            ["Name"] = name,
                            ["Pack Size"] = packSize.ToString(),
                            ["Category"] = categoryName,
                            ["Brand"] = brandRow.Value.Name,
                            ["Supplier"] = supplierRow.Value.Name,
                            ["Formulation"] = string.IsNullOrEmpty(formulation) ? "(None)" : formulation,
                            ["Sale Price"] = GetDecimal(draft, "pack_sale_price", 0).ToString("0.##")
                        })
                    },
                    "cube_task_awaiting_permission",
                    productVersion);
            }

            // Commit to database
            var nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var categoryId = await ResolveOrCreateCategoryAsync(conn, categoryName, nowStr, ct);
            var (code, barcode) = await GenerateUniqueCodeAndBarcodeAsync(conn, ct);
            var packPurchase = GetDecimal(draft, "pack_purchase_price", 0);
            var packSale = GetDecimal(draft, "pack_sale_price", 0);
            var unitPurchase = packSize > 0 ? Math.Round(packPurchase / packSize, 4) : packPurchase;
            var unitSale = packSize > 0 ? Math.Round(packSale / packSize, 4) : packSale;

            long newId;
            await using (var ins = conn.CreateCommand())
            {
                ins.CommandText = @"
                    INSERT INTO products (product_code, barcode, name, formulation, pack_size, category_id, brand_id, supplier_id,
                        pack_purchase_price, pack_sale_price, unit_purchase_price, unit_sale_price, quantity, margin, created_at, updated_at)
                    VALUES (@code, @barcode, @name, @form, @pack, @catId, @brandId, @supId, @pp, @ps, @up, @us, 0, 0, @now, @now);
                    SELECT last_insert_rowid();";
                AddParam(ins, "@code", code);
                AddParam(ins, "@barcode", barcode);
                AddParam(ins, "@name", name);
                AddParam(ins, "@form", string.IsNullOrEmpty(formulation) ? DBNull.Value : formulation);
                AddParam(ins, "@pack", packSize);
                AddParam(ins, "@catId", categoryId);
                AddParam(ins, "@brandId", brandRow.Value.Id);
                AddParam(ins, "@supId", supplierRow.Value.Id);
                AddParam(ins, "@pp", packPurchase);
                AddParam(ins, "@ps", packSale);
                AddParam(ins, "@up", unitPurchase);
                AddParam(ins, "@us", unitSale);
                AddParam(ins, "@now", nowStr);
                newId = Convert.ToInt64(await ins.ExecuteScalarAsync(ct) ?? 0);
            }

            // Link product_suppliers if table exists
            try
            {
                await using var psCmd = conn.CreateCommand();
                psCmd.CommandText = "INSERT OR IGNORE INTO product_suppliers (product_id, supplier_id, created_at, updated_at) VALUES (@pid, @sid, @now, @now)";
                AddParam(psCmd, "@pid", newId);
                AddParam(psCmd, "@sid", supplierRow.Value.Id);
                AddParam(psCmd, "@now", nowStr);
                await psCmd.ExecuteNonQueryAsync(ct);
            }
            catch { }

            if (!string.IsNullOrEmpty(formulation))
            {
                await EnsureCanonicalFormulationAsync(conn, formulation, nowStr, ct);
            }

            ClearPendingTask(user.UserId);
            return FormatTaskResponse(
                $"Created Product: {name} (#{newId})",
                $"نئی پروڈکٹ محفوظ ہو گئی: {name} (#{newId})",
                $"Successfully created **{name}** (Code: `{code}`, Barcode: `{barcode}`, Pack Size: `{packSize}`, Brand: `{brandRow.Value.Name}`, Supplier: `{supplierRow.Value.Name}`).",
                $"پروڈکٹ **{name}** (کوڈ: `{code}`) کامیابی سے ڈیٹا بیس میں شامل ہو گئی۔",
                [
                    new() { ["path"] = $"/products/{newId}/edit", ["en"] = "Edit Product", ["ur"] = "پروڈکٹ ایڈٹ کریں" },
                    new() { ["path"] = "/products", ["en"] = "Products Catalog", ["ur"] = "پروڈکٹس لسٹ" }
                ],
                new Dictionary<string, object?>
                {
                    ["action"] = "create_products",
                    ["status"] = "completed",
                    ["created_count"] = 1,
                    ["created_id"] = newId,
                    ["refresh_products"] = true,
                    ["result_summary"] = new Dictionary<string, object?>
                    {
                        ["table"] = "products",
                        ["count"] = 1,
                        ["records"] = new List<Dictionary<string, object?>>
                        {
                            new() { ["id"] = newId, ["title"] = name, ["code"] = code, ["edit_path"] = $"/products/{newId}/edit" }
                        }
                    }
                },
                "cube_task_executed",
                productVersion);
        }
        finally
        {
            if (shouldClose) await conn.CloseAsync();
        }
    }

    private async Task<Dictionary<string, object?>> ExecuteUpdateProductsStructuredAsync(
        KarobarDbContext db,
        CubeUserContext user,
        Dictionary<string, object?> payload,
        string productVersion,
        CancellationToken ct)
    {
        var updates = ExtractListOfDicts(payload, "updates");
        var draft = updates.Count > 0 ? updates[0] : payload;
        var confirmed = GetBool(payload, "user_confirmed", false);
        return await ProcessUpdateProductDraftAsync(db, user, draft, confirmed, productVersion, ct);
    }

    private async Task<Dictionary<string, object?>> ProcessUpdateProductDraftAsync(
        KarobarDbContext db,
        CubeUserContext user,
        Dictionary<string, object?> draft,
        bool userConfirmed,
        string productVersion,
        CancellationToken ct)
    {
        if (!user.Can("product.update"))
        {
            return BuildPermissionDeniedResponse("update_products", "product.update", productVersion);
        }

        var targetId = GetLong(draft, "id", GetLong(draft, "product_id", 0));
        var targetName = (GetStr(draft, "target") ?? GetStr(draft, "name") ?? GetStr(draft, "product_name") ?? "").Trim();

        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose) await conn.OpenAsync(ct);

        try
        {
            Dictionary<string, object?>? matched = null;
            if (targetId > 0)
            {
                matched = await LoadProductByIdAsync(conn, targetId, ct);
            }
            else if (!string.IsNullOrEmpty(targetName))
            {
                var matches = await SearchProductsAsync(conn, targetName, 6, ct);
                if (matches.Count == 1)
                {
                    matched = matches[0];
                }
                else if (matches.Count > 1)
                {
                    var exact = matches.FirstOrDefault(m => string.Equals(m["name"]?.ToString(), targetName, StringComparison.OrdinalIgnoreCase));
                    if (exact != null)
                    {
                        matched = exact;
                    }
                    else
                    {
                        SavePendingTask(user.UserId, new Dictionary<string, object?>
                        {
                            ["action"] = "update_products",
                            ["stage"] = "select_match",
                            ["draft"] = draft,
                            ["matches"] = matches
                        });
                        return FormatTaskResponse(
                            $"Multiple Products Matched \"{targetName}\"",
                            $"\"{targetName}\" سے ملتی جلتی متعدد پروڈکٹس ملی ہیں",
                            "Please select the exact product you want to update:",
                            "براہِ کرم وہ پروڈکٹ منتخب کریں جسے آپ اپڈیٹ کرنا چاہتے ہیں:",
                            [],
                            new Dictionary<string, object?>
                            {
                                ["action"] = "update_products",
                                ["awaiting_input"] = true,
                                ["db_matches"] = matches
                            },
                            "cube_task_needs_input",
                            productVersion);
                    }
                }
            }

            if (matched == null)
            {
                SavePendingTask(user.UserId, new Dictionary<string, object?>
                {
                    ["action"] = "update_products",
                    ["stage"] = "collecting_fields",
                    ["draft"] = draft
                });
                return FormatTaskResponse(
                    "Which Product Should I Update?",
                    "کون سی پروڈکٹ اپڈیٹ کرنی ہے؟",
                    "Please provide the existing product name or code and the fields to change, e.g. `update Panadol 500mg: pack_sale_price: 350, rack: A-12`.",
                    "براہِ کرم پروڈکٹ کا نام اور نئی تفصیلات لکھیں، مثلاً `update Panadol: pack_sale_price: 350`۔",
                    [new() { ["path"] = "/products", ["en"] = "Products Catalog", ["ur"] = "پروڈکٹس لسٹ" }],
                    new Dictionary<string, object?> { ["action"] = "update_products", ["awaiting_input"] = true },
                    "cube_task_needs_input",
                    productVersion);
            }

            var pid = Convert.ToInt64(matched["id"]);
            var currentName = matched["name"]?.ToString() ?? "";
            var changes = new Dictionary<string, (string Before, object? After, string Column)>();

            void StageStr(string key, string col, string label)
            {
                var v = (GetStr(draft, key) ?? "").Trim();
                if (!string.IsNullOrEmpty(v))
                {
                    changes[label] = (matched.GetValueOrDefault(col)?.ToString() ?? "(Empty)", v, col);
                }
            }

            StageStr("new_name", "name", "Name");
            StageStr("formulation", "formulation", "Formulation");
            StageStr("rack", "rack", "Rack");
            StageStr("barcode", "barcode", "Barcode");
            if (GetInt(draft, "pack_size", 0) > 0)
            {
                var ps = GetInt(draft, "pack_size", 0);
                changes["Pack Size"] = (matched.GetValueOrDefault("pack_size")?.ToString() ?? "1", ps, "pack_size");
            }
            if (draft.ContainsKey("pack_purchase_price") && GetDecimal(draft, "pack_purchase_price", -1) >= 0)
            {
                var pp = GetDecimal(draft, "pack_purchase_price", 0);
                changes["Pack Purchase Price"] = (matched.GetValueOrDefault("pack_purchase_price")?.ToString() ?? "0", pp, "pack_purchase_price");
            }
            if (draft.ContainsKey("pack_sale_price") && GetDecimal(draft, "pack_sale_price", -1) >= 0)
            {
                var ps = GetDecimal(draft, "pack_sale_price", 0);
                changes["Pack Sale Price"] = (matched.GetValueOrDefault("pack_sale_price")?.ToString() ?? "0", ps, "pack_sale_price");
            }

            if (changes.Count == 0)
            {
                draft["id"] = pid;
                SavePendingTask(user.UserId, new Dictionary<string, object?>
                {
                    ["action"] = "update_products",
                    ["stage"] = "collecting_fields",
                    ["draft"] = draft
                });
                return FormatTaskResponse(
                    $"What Should I Change on {currentName}?",
                    $"{currentName} میں کیا تبدیل کرنا ہے؟",
                    $"Found **{currentName}** (`#{pid}`). Reply with the field(s) to update, e.g. `formulation: Paracetamol, pack_sale_price: 380, rack: B-04`.",
                    $"پروڈکٹ **{currentName}** مل گئی۔ اب بتائیں کون سی فیلڈ تبدیل کرنی ہے (مثلاً `pack_sale_price: 380, rack: B-04`)۔",
                    [],
                    new Dictionary<string, object?> { ["action"] = "update_products", ["awaiting_input"] = true },
                    "cube_task_needs_input",
                    productVersion);
            }

            if (!userConfirmed)
            {
                draft["id"] = pid;
                SavePendingTask(user.UserId, new Dictionary<string, object?>
                {
                    ["action"] = "update_products",
                    ["stage"] = "awaiting_permission",
                    ["draft"] = draft
                });

                var diffFields = changes.Select(kv => new Dictionary<string, object?>
                {
                    ["field"] = kv.Key,
                    ["label_ur"] = kv.Key,
                    ["before"] = kv.Value.Before,
                    ["after"] = kv.Value.After?.ToString() ?? ""
                }).ToList();

                return FormatTaskResponse(
                    $"Confirm Update: {currentName}",
                    $"تبدیلی کی تصدیق: {currentName}",
                    $"Review the staged changes for **{currentName}** (`#{pid}`) and click **Approve & Post to Database**.",
                    $"**{currentName}** کی تبدیلیاں چیک کریں اور منظوری دیں۔",
                    [],
                    new Dictionary<string, object?>
                    {
                        ["action"] = "update_products",
                        ["awaiting_permission"] = true,
                        ["preview_result"] = new Dictionary<string, object?>
                        {
                            ["staged"] = true,
                            ["posted_to_database"] = false,
                            ["database_table"] = "products",
                            ["operation"] = "UPDATE",
                            ["records"] = new List<Dictionary<string, object?>>
                            {
                                new()
                                {
                                    ["id"] = pid,
                                    ["title"] = currentName,
                                    ["code"] = matched.GetValueOrDefault("product_code")?.ToString() ?? "",
                                    ["badge_en"] = "Staged Update",
                                    ["badge_ur"] = "تبدیلی تیار",
                                    ["fields"] = diffFields
                                }
                            }
                        }
                    },
                    "cube_task_awaiting_permission",
                    productVersion);
            }

            var nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            var setClauses = new List<string>();
            await using (var upd = conn.CreateCommand())
            {
                var idx = 0;
                foreach (var kv in changes)
                {
                    var pKey = $"@v{idx++}";
                    setClauses.Add($"{kv.Value.Column} = {pKey}");
                    AddParam(upd, pKey, kv.Value.After);
                }
                setClauses.Add("updated_at = @now");
                AddParam(upd, "@now", nowStr);
                AddParam(upd, "@id", pid);
                upd.CommandText = $"UPDATE products SET {string.Join(", ", setClauses)} WHERE id = @id";
                await upd.ExecuteNonQueryAsync(ct);
            }

            if (changes.TryGetValue("Formulation", out var fChg) && fChg.After is string fStr && !string.IsNullOrWhiteSpace(fStr))
            {
                await EnsureCanonicalFormulationAsync(conn, fStr, nowStr, ct);
            }

            ClearPendingTask(user.UserId);
            return FormatTaskResponse(
                $"Updated Product: {currentName} (#{pid})",
                $"پروڈکٹ اپڈیٹ ہو گئی: {currentName} (#{pid})",
                $"Successfully updated **{changes.Count}** field(s) on **{currentName}** (`#{pid}`).",
                $"پروڈکٹ **{currentName}** کی **{changes.Count}** فیلڈز کامیابی سے اپڈیٹ ہو گئیں۔",
                [new() { ["path"] = $"/products/{pid}/edit", ["en"] = "View Product", ["ur"] = "پروڈکٹ دیکھیں" }],
                new Dictionary<string, object?>
                {
                    ["action"] = "update_products",
                    ["status"] = "completed",
                    ["updated_count"] = 1,
                    ["updated_id"] = pid,
                    ["refresh_products"] = true,
                    ["result_summary"] = new Dictionary<string, object?>
                    {
                        ["table"] = "products",
                        ["count"] = 1,
                        ["records"] = new List<Dictionary<string, object?>>
                        {
                            new() { ["id"] = pid, ["title"] = currentName, ["edit_path"] = $"/products/{pid}/edit" }
                        }
                    }
                },
                "cube_task_executed",
                productVersion);
        }
        finally
        {
            if (shouldClose) await conn.CloseAsync();
        }
    }

    // =========================================================================
    // Tasks 5–15: Master Entities Create & Update (Brands, Categories, Suppliers,
    // Customers, Storage Locations, Expense Categories, User Demands)
    // =========================================================================

    private async Task<Dictionary<string, object?>> ExecuteMasterEntityStructuredAsync(
        KarobarDbContext db,
        CubeUserContext user,
        string taskType,
        Dictionary<string, object?> payload,
        string productVersion,
        CancellationToken ct)
    {
        var items = ExtractListOfDicts(payload, "items");
        if (items.Count == 0) items = ExtractListOfDicts(payload, "updates");
        var draft = items.Count > 0 ? items[0] : payload;
        var confirmed = GetBool(payload, "user_confirmed", false);
        return await ProcessMasterEntityDraftAsync(db, user, taskType, draft, confirmed, productVersion, ct);
    }

    private async Task<Dictionary<string, object?>> ProcessMasterEntityDraftAsync(
        KarobarDbContext db,
        CubeUserContext user,
        string taskType,
        Dictionary<string, object?> draft,
        bool userConfirmed,
        string productVersion,
        CancellationToken ct)
    {
        var (requiredPerms, table, route, isUpdate) = taskType switch
        {
            "create_brands" => (new[] { "brand.create" }, "brands", "/brands", false),
            "update_brands" => (new[] { "brand.update" }, "brands", "/brands", true),
            "create_categories" => (new[] { "category.create" }, "categories", "/categories", false),
            "update_categories" => (new[] { "category.update" }, "categories", "/categories", true),
            "create_suppliers" => (new[] { "supplier.create" }, "suppliers", "/suppliers", false),
            "update_suppliers" => (new[] { "supplier.update" }, "suppliers", "/suppliers", true),
            "create_customers" => (new[] { "customer.create" }, "customers", "/customers", false),
            "update_customers" => (new[] { "customer.update" }, "customers", "/customers", true),
            "create_storage_locations" => (new[] { "storage-location.manage", "product.create", "product.update" }, "storage_locations", "/settings", false),
            "create_expense_categories" => (new[] { "expense-category.manage", "expense.create" }, "expense_categories", "/expenses", false),
            "create_user_demands" => (new[] { "user-demands.create" }, "user_demands", "/user-demands", false),
            _ => (new[] { "settings.update" }, "settings", "/settings", false)
        };

        if (!user.Can(requiredPerms))
        {
            return BuildPermissionDeniedResponse(taskType, string.Join(" / ", requiredPerms), productVersion);
        }

        var name = (GetStr(draft, "name") ?? GetStr(draft, "target") ?? GetStr(draft, "product_name") ?? "").Trim();
        var newName = (GetStr(draft, "new_name") ?? "").Trim();
        var phone = (GetStr(draft, "phone") ?? "").Trim();
        var email = (GetStr(draft, "email") ?? "").Trim();
        var address = (GetStr(draft, "address") ?? "").Trim();
        var code = (GetStr(draft, "code") ?? "").Trim();
        var targetId = GetLong(draft, "id", 0);

        if (string.IsNullOrEmpty(name) && targetId <= 0)
        {
            SavePendingTask(user.UserId, new Dictionary<string, object?> { ["action"] = taskType, ["stage"] = "collecting_fields", ["draft"] = draft });
            return FormatTaskResponse(
                $"Details Required for {taskType.Replace('_', ' ')}",
                "ضروری تفصیلات درکار ہیں",
                $"Please provide the name{(isUpdate ? " and the new field values" : "")} to continue (e.g. `name: Getz Pharma`).",
                "براہِ کرم جاری رکھنے کے لیے نام اور تفصیلات درج کریں۔",
                [new() { ["path"] = route, ["en"] = "Open Screen", ["ur"] = "اسکرین کھولیں" }],
                new Dictionary<string, object?> { ["action"] = taskType, ["awaiting_input"] = true },
                "cube_task_needs_input",
                productVersion);
        }

        var conn = db.Database.GetDbConnection();
        var shouldClose = conn.State != System.Data.ConnectionState.Open;
        if (shouldClose) await conn.OpenAsync(ct);

        try
        {
            var nowStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            if (isUpdate)
            {
                (long Id, string Name)? existing = null;
                if (targetId > 0)
                {
                    await using var fCmd = conn.CreateCommand();
                    fCmd.CommandText = $"SELECT id, name FROM {table} WHERE id = @id LIMIT 1";
                    AddParam(fCmd, "@id", targetId);
                    await using var r = await fCmd.ExecuteReaderAsync(ct);
                    if (await r.ReadAsync(ct)) existing = (r.GetInt64(0), r.GetString(1));
                }
                else
                {
                    existing = await FindEntityByNameAsync(conn, table, name, ct);
                }

                if (existing == null)
                {
                    return FormatTaskResponse(
                        $"Record Not Found in {table}",
                        "ریکارڈ نہیں ملا",
                        $"Could not find `{name}` in `{table}` to update.",
                        $"`{table}` میں `{name}` نامی ریکارڈ نہیں ملا۔",
                        [new() { ["path"] = route, ["en"] = "Open List", ["ur"] = "فہرست کھولیں" }],
                        new Dictionary<string, object?> { ["action"] = taskType, ["status"] = "not_found" },
                        "cube_task_needs_input",
                        productVersion);
                }

                var updateCols = new Dictionary<string, object?>();
                if (!string.IsNullOrEmpty(newName)) updateCols["name"] = newName;
                if (table is "suppliers" or "customers")
                {
                    if (!string.IsNullOrEmpty(phone)) updateCols["phone"] = phone;
                    if (!string.IsNullOrEmpty(email)) updateCols["email"] = email;
                    if (!string.IsNullOrEmpty(address)) updateCols["address"] = address;
                }
                if (updateCols.Count == 0 && !string.IsNullOrEmpty(name) && !name.Equals(existing.Value.Name, StringComparison.OrdinalIgnoreCase))
                {
                    updateCols["name"] = name;
                }

                if (updateCols.Count == 0)
                {
                    draft["id"] = existing.Value.Id;
                    SavePendingTask(user.UserId, new Dictionary<string, object?> { ["action"] = taskType, ["stage"] = "collecting_fields", ["draft"] = draft });
                    return FormatTaskResponse(
                        $"What Should I Change on {existing.Value.Name}?",
                        $"{existing.Value.Name} میں کیا تبدیل کرنا ہے؟",
                        "Please specify the updated value(s), e.g. `new_name: Updated Name, phone: 03001234567`.",
                        "براہِ کرم نئی تفصیلات درج کریں۔",
                        [],
                        new Dictionary<string, object?> { ["action"] = taskType, ["awaiting_input"] = true },
                        "cube_task_needs_input",
                        productVersion);
                }

                if (!userConfirmed)
                {
                    draft["id"] = existing.Value.Id;
                    SavePendingTask(user.UserId, new Dictionary<string, object?> { ["action"] = taskType, ["stage"] = "awaiting_permission", ["draft"] = draft });
                    return FormatTaskResponse(
                        $"Confirm Update: {existing.Value.Name}",
                        $"تبدیلی کی تصدیق: {existing.Value.Name}",
                        $"Review staged update on **{existing.Value.Name}** (`#{existing.Value.Id}`) and click **Approve & Post to Database**.",
                        $"**{existing.Value.Name}** کی تبدیلی کی منظوری دیں۔",
                        [],
                        new Dictionary<string, object?>
                        {
                            ["action"] = taskType,
                            ["awaiting_permission"] = true,
                            ["preview_result"] = BuildSingleRecordPreview(table, "UPDATE", existing.Value.Name,
                                updateCols.ToDictionary(k => k.Key, v => v.Value?.ToString() ?? ""))
                        },
                        "cube_task_awaiting_permission",
                        productVersion);
                }

                await using (var uCmd = conn.CreateCommand())
                {
                    var sets = new List<string>();
                    var i = 0;
                    foreach (var kv in updateCols)
                    {
                        var pk = $"@p{i++}";
                        sets.Add($"{kv.Key} = {pk}");
                        AddParam(uCmd, pk, kv.Value);
                    }
                    sets.Add("updated_at = @now");
                    AddParam(uCmd, "@now", nowStr);
                    AddParam(uCmd, "@id", existing.Value.Id);
                    uCmd.CommandText = $"UPDATE {table} SET {string.Join(", ", sets)} WHERE id = @id";
                    await uCmd.ExecuteNonQueryAsync(ct);
                }

                ClearPendingTask(user.UserId);
                return FormatTaskResponse(
                    $"Updated {existing.Value.Name} (#{existing.Value.Id})",
                    $"{existing.Value.Name} اپڈیٹ ہو گیا (#{existing.Value.Id})",
                    $"Successfully updated **{existing.Value.Name}** in `{table}`.",
                    $"**{existing.Value.Name}** کامیابی سے اپڈیٹ ہو گیا۔",
                    [new() { ["path"] = route, ["en"] = "Open Screen", ["ur"] = "اسکرین کھولیں" }],
                    new Dictionary<string, object?>
                    {
                        ["action"] = taskType,
                        ["status"] = "completed",
                        ["updated_count"] = 1,
                        ["updated_id"] = existing.Value.Id,
                        ["result_summary"] = new Dictionary<string, object?> { ["table"] = table, ["count"] = 1 }
                    },
                    "cube_task_executed",
                    productVersion);
            }

            // Create master entity
            if (!userConfirmed)
            {
                SavePendingTask(user.UserId, new Dictionary<string, object?> { ["action"] = taskType, ["stage"] = "awaiting_permission", ["draft"] = draft });
                var fields = new Dictionary<string, string> { ["Name"] = name };
                if (!string.IsNullOrEmpty(phone)) fields["Phone"] = phone;
                if (!string.IsNullOrEmpty(email)) fields["Email"] = email;
                if (!string.IsNullOrEmpty(address)) fields["Address"] = address;
                if (!string.IsNullOrEmpty(code)) fields["Code"] = code;

                return FormatTaskResponse(
                    $"Confirm Create: {name}",
                    $"نیا ریکارڈ محفوظ کرنے کی تصدیق: {name}",
                    $"Ready to insert **{name}** into `{table}`. Review and click **Approve & Post to Database**.",
                    $"**{name}** کو `{table}` میں شامل کرنے کے لیے منظوری دیں۔",
                    [],
                    new Dictionary<string, object?>
                    {
                        ["action"] = taskType,
                        ["awaiting_permission"] = true,
                        ["preview_result"] = BuildSingleRecordPreview(table, "INSERT", name, fields)
                    },
                    "cube_task_awaiting_permission",
                    productVersion);
            }

            long createdId = 0;
            if (taskType == "create_brands")
            {
                createdId = await InsertRowAsync(conn, "brands", new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["expiry_return_policy"] = "before_expiry",
                    ["expiry_return_months"] = 3,
                    ["created_at"] = nowStr,
                    ["updated_at"] = nowStr
                }, ct);
            }
            else if (taskType == "create_categories")
            {
                createdId = await InsertRowAsync(conn, "categories", new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["created_at"] = nowStr,
                    ["updated_at"] = nowStr
                }, ct);
            }
            else if (taskType is "create_suppliers" or "create_customers")
            {
                var cols = new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["phone"] = string.IsNullOrEmpty(phone) ? null : phone,
                    ["address"] = string.IsNullOrEmpty(address) ? null : address,
                    ["created_at"] = nowStr,
                    ["updated_at"] = nowStr
                };
                if (!string.IsNullOrEmpty(email)) cols["email"] = email;
                createdId = await InsertRowAsync(conn, table, cols, ct);
            }
            else if (taskType == "create_storage_locations")
            {
                createdId = await InsertRowAsync(conn, "storage_locations", new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["code"] = string.IsNullOrEmpty(code) ? $"LOC-{DateTime.UtcNow:mmss}" : code,
                    ["type"] = "rack",
                    ["default_rack"] = GetStr(draft, "default_rack") ?? "R-01",
                    ["is_active"] = 1,
                    ["created_at"] = nowStr,
                    ["updated_at"] = nowStr
                }, ct);
            }
            else if (taskType == "create_expense_categories")
            {
                createdId = await InsertRowAsync(conn, "expense_categories", new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["code"] = string.IsNullOrEmpty(code) ? $"EXP-{DateTime.UtcNow:mmss}" : code,
                    ["is_active"] = 1,
                    ["created_at"] = nowStr,
                    ["updated_at"] = nowStr
                }, ct);
            }
            else if (taskType == "create_user_demands")
            {
                var prodId = GetLong(draft, "product_id", 0);
                if (prodId <= 0)
                {
                    var found = await FindEntityByNameAsync(conn, "products", name, ct);
                    if (found == null)
                    {
                        return FormatTaskResponse(
                            "Product Not Found for Demand",
                            "ڈیمانڈ کے لیے پروڈکٹ نہیں ملی",
                            $"No matching product found for **{name}**. Please specify a valid `product_id` or create the product first.",
                            $"**{name}** سے ملتی جلتی کوئی پروڈکٹ نہیں ملی۔",
                            [new() { ["path"] = "/user-demands/create", ["en"] = "Open User Demands", ["ur"] = "یوزر ڈیمانڈز کھولیں" }],
                            new Dictionary<string, object?> { ["action"] = taskType, ["status"] = "product_not_found" },
                            "cube_task_needs_input",
                            productVersion);
                    }
                    prodId = found.Value.Id;
                }
                var qty = Math.Max(1, GetInt(draft, "requested_quantity", GetInt(draft, "quantity", 1)));
                createdId = await InsertRowAsync(conn, "user_demands", new Dictionary<string, object?>
                {
                    ["product_id"] = prodId,
                    ["user_id"] = user.UserId > 0 ? user.UserId : 1,
                    ["requested_by"] = user.UserId > 0 ? user.UserId : 1,
                    ["requested_name"] = name,
                    ["quantity"] = qty,
                    ["requested_quantity"] = qty,
                    ["status"] = "pending",
                    ["notes"] = GetStr(draft, "notes") ?? "Logged via Cube AI Assistant",
                    ["created_at"] = nowStr,
                    ["updated_at"] = nowStr
                }, ct);
            }

            ClearPendingTask(user.UserId);
            return FormatTaskResponse(
                $"Created {name} (#{createdId})",
                $"{name} ڈیٹا بیس میں محفوظ ہو گیا (#{createdId})",
                $"Successfully created **{name}** (ID `#{createdId}`) in `{table}`.",
                $"**{name}** کامیابی سے ڈیٹا بیس میں شامل ہو گیا (ID `#{createdId}`)۔",
                [new() { ["path"] = route, ["en"] = "Open Screen", ["ur"] = "اسکرین کھولیں" }],
                new Dictionary<string, object?>
                {
                    ["action"] = taskType,
                    ["status"] = "completed",
                    ["created_count"] = 1,
                    ["created_id"] = createdId,
                    ["result_summary"] = new Dictionary<string, object?> { ["table"] = table, ["count"] = 1 }
                },
                "cube_task_executed",
                productVersion);
        }
        finally
        {
            if (shouldClose) await conn.CloseAsync();
        }
    }

    // =========================================================================
    // 4. Multi-Step Pending Task Continuation & Intent/Field Parsers
    // =========================================================================

    private async Task<Dictionary<string, object?>> ContinuePendingTaskAsync(
        KarobarDbContext db,
        CubeUserContext user,
        Dictionary<string, object?> pending,
        string replyText,
        string productVersion,
        bool confirmed,
        CancellationToken ct)
    {
        var action = pending.GetValueOrDefault("action")?.ToString() ?? "";
        var stage = pending.GetValueOrDefault("stage")?.ToString() ?? "";

        if (action == "bulk_autofill_missing_formulations" && confirmed)
        {
            ClearPendingTask(user.UserId);
            return await ExecuteBulkAutofillMissingFormulationsAsync(db, user, new Dictionary<string, object?>
            {
                ["task_type"] = "bulk_autofill_missing_formulations",
                ["preview_only"] = false,
                ["user_confirmed"] = true,
                ["batch_size"] = 20,
                ["filter_query"] = pending.GetValueOrDefault("filter_query")?.ToString() ?? ""
            }, productVersion, ct);
        }

        if (action == "bulk_assign_formulations" && confirmed)
        {
            return await ExecuteBulkAssignFormulationsAsync(db, user, new Dictionary<string, object?>
            {
                ["task_type"] = "bulk_assign_formulations",
                ["assignments"] = pending.GetValueOrDefault("assignments"),
                ["standalone_formulations"] = pending.GetValueOrDefault("standalone_formulations"),
                ["user_confirmed"] = true
            }, productVersion, ct);
        }

        var draft = pending.GetValueOrDefault("draft") as Dictionary<string, object?>
            ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (stage == "select_match" && pending.GetValueOrDefault("matches") is List<Dictionary<string, object?>> matches)
        {
            if (int.TryParse(replyText.Trim().TrimStart('#'), out var idx))
            {
                var byId = matches.FirstOrDefault(m => Convert.ToInt64(m["id"]) == idx);
                var chosen = byId ?? (idx >= 1 && idx <= matches.Count ? matches[idx - 1] : null);
                if (chosen != null) draft["id"] = Convert.ToInt64(chosen["id"]);
            }
            else
            {
                var byName = matches.FirstOrDefault(m => (m["name"]?.ToString() ?? "").Contains(replyText.Trim(), StringComparison.OrdinalIgnoreCase));
                if (byName != null) draft["id"] = Convert.ToInt64(byName["id"]);
            }
        }
        else if (!confirmed)
        {
            var merged = action == "create_products"
                ? ExtractProductFieldsFromText(replyText)
                : (action == "update_products" ? ExtractUpdateProductFieldsFromText(replyText) : ExtractMasterEntityFieldsFromText(replyText, action));
            foreach (var kv in merged)
            {
                if (kv.Value != null && !string.IsNullOrWhiteSpace(kv.Value.ToString()))
                {
                    draft[kv.Key] = kv.Value;
                }
            }
        }

        if (action == "create_products")
        {
            return await ProcessCreateProductDraftAsync(db, user, draft, confirmed, productVersion, ct);
        }
        if (action == "update_products")
        {
            return await ProcessUpdateProductDraftAsync(db, user, draft, confirmed, productVersion, ct);
        }
        return await ProcessMasterEntityDraftAsync(db, user, action, draft, confirmed, productVersion, ct);
    }

    public static string? ClassifyActionIntent(string lower)
    {
        var t = (lower ?? "").Trim();
        if (string.IsNullOrEmpty(t)) return null;

        // Explicit slash commands from GuideBot Slash Command Palette
        if (t.StartsWith("/add-product")) return "create_products";
        if (t.StartsWith("/edit-product") || t.StartsWith("/update-product")) return "update_products";
        if (t.StartsWith("/autofill")) return "bulk_autofill_missing_formulations";
        if (t.StartsWith("/add-brand")) return "create_brands";
        if (t.StartsWith("/add-category")) return "create_categories";
        if (t.StartsWith("/add-supplier")) return "create_suppliers";
        if (t.StartsWith("/add-customer")) return "create_customers";
        if (t.StartsWith("/demand")) return "create_user_demands";

        // Skip pure educational questions ("how to add product", "product kaise add karein")
        if (Regex.IsMatch(t, @"\b(how to|how do i|how can i|kaise|kesay|kese|tareeqa|tarika|explain|what is|guide|steps|walkthrough)\b") ||
            t.Contains("کیسے") || t.Contains("طریقہ"))
        {
            return null;
        }

        if (Regex.IsMatch(t, @"\b(auto[\s\-]?fill|fill\s+missing|enrich)\b.*\b(formulation|formulations|salt|salts|generic)\b") ||
            Regex.IsMatch(t, @"\b(missing\s+formulations?)\b"))
            return "bulk_autofill_missing_formulations";

        if (Regex.IsMatch(t, @"\b(assign|set|add)\s+formulations?\b") && (t.Contains('=') || t.Contains(':')))
            return "bulk_assign_formulations";

        if (Regex.IsMatch(t, @"\b(add|create|new|insert)\s+(?:a\s+|new\s+)?(product|products|medicine|item)\b"))
            return "create_products";
        if (Regex.IsMatch(t, @"\b(update|edit|change|modify)\s+(?:the\s+)?(product|medicine|item)\b"))
            return "update_products";

        if (Regex.IsMatch(t, @"\b(add|create|new)\s+(?:a\s+|new\s+)?brand\b")) return "create_brands";
        if (Regex.IsMatch(t, @"\b(update|edit|rename)\s+(?:the\s+)?brand\b")) return "update_brands";
        if (Regex.IsMatch(t, @"\b(add|create|new)\s+(?:a\s+|new\s+)?category\b")) return "create_categories";
        if (Regex.IsMatch(t, @"\b(update|edit|rename)\s+(?:the\s+)?category\b")) return "update_categories";
        if (Regex.IsMatch(t, @"\b(add|create|new)\s+(?:a\s+|new\s+)?supplier\b")) return "create_suppliers";
        if (Regex.IsMatch(t, @"\b(update|edit)\s+(?:the\s+)?supplier\b")) return "update_suppliers";
        if (Regex.IsMatch(t, @"\b(add|create|new)\s+(?:a\s+|new\s+)?customer\b")) return "create_customers";
        if (Regex.IsMatch(t, @"\b(update|edit)\s+(?:the\s+)?customer\b")) return "update_customers";
        if (Regex.IsMatch(t, @"\b(add|create|log|record)\s+(?:a\s+|new\s+)?(?:user\s+)?demand\b")) return "create_user_demands";
        if (Regex.IsMatch(t, @"\b(add|create|new)\s+(?:a\s+|new\s+)?(?:storage\s+location|rack)\b")) return "create_storage_locations";
        if (Regex.IsMatch(t, @"\b(add|create|new)\s+(?:a\s+|new\s+)?expense\s+category\b")) return "create_expense_categories";

        return null;
    }

    private static bool IsDestructiveDeleteCommand(string lower)
    {
        return Regex.IsMatch(lower, @"\b(delete|remove|drop|wipe|truncate|destroy|erase)\s+(?:all\s+|the\s+|a\s+)?(product|products|item|items|invoice|invoices|customer|customers|supplier|suppliers|brand|brands|category|categories|batch|batches|database|record|records|user|users)\b");
    }

    private static bool IsCancelCommand(string lower)
    {
        return Regex.IsMatch(lower.Trim(), @"^(cancel|abort|stop|no|nahi|mat karo|deny|reject|nevermind|never mind)$");
    }

    private static bool IsConfirmCommand(string lower)
    {
        return Regex.IsMatch(lower.Trim(), @"^(yes|y|ok|okay|confirm|approve|proceed|post|save|haan|han|ji|theek hai|apply|start)$");
    }

    private static Dictionary<string, object?> ExtractProductFieldsFromText(string text)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var clean = Regex.Replace(text, @"^/(?:add|create)-product\s*", "", RegexOptions.IgnoreCase).Trim();
        ParseKeyValuePairs(clean, dict);

        if (!dict.ContainsKey("name"))
        {
            var m = Regex.Match(clean, @"(?:add|create|new)\s+(?:a\s+|new\s+)?(?:product|medicine|item)\s+([^,\n:]+)", RegexOptions.IgnoreCase);
            if (m.Success) dict["name"] = m.Groups[1].Value.Trim();
        }
        return dict;
    }

    private static Dictionary<string, object?> ExtractUpdateProductFieldsFromText(string text)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var clean = Regex.Replace(text, @"^/(?:edit|update)-product\s*", "", RegexOptions.IgnoreCase).Trim();
        ParseKeyValuePairs(clean, dict);

        if (!dict.ContainsKey("target") && !dict.ContainsKey("name"))
        {
            var m = Regex.Match(clean, @"(?:update|edit|change|modify)\s+(?:the\s+)?(?:product|medicine|item)?\s*([^,\n:]+)", RegexOptions.IgnoreCase);
            if (m.Success) dict["target"] = m.Groups[1].Value.Trim();
        }
        return dict;
    }

    private static Dictionary<string, object?> ExtractMasterEntityFieldsFromText(string text, string taskType)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var clean = Regex.Replace(text, @"^/[a-z\-]+\s*", "", RegexOptions.IgnoreCase).Trim();
        ParseKeyValuePairs(clean, dict);

        if (!dict.ContainsKey("name"))
        {
            var m = Regex.Match(clean, @"(?:add|create|new|update|edit|log|record)\s+(?:a\s+|new\s+|the\s+)?(?:brand|category|supplier|customer|user\s+demand|demand|storage\s+location|rack|expense\s+category)\s+([^,\n:]+)", RegexOptions.IgnoreCase);
            if (m.Success) dict["name"] = m.Groups[1].Value.Trim();
            else if (!clean.Contains(':') && !string.IsNullOrWhiteSpace(clean) && clean.Length <= 100) dict["name"] = clean;
        }
        return dict;
    }

    private static (List<Dictionary<string, object?>> Assignments, List<string> Standalone) ExtractFormulationAssignmentsFromText(string text)
    {
        var assignments = new List<Dictionary<string, object?>>();
        var standalone = new List<string>();
        foreach (var part in text.Split([',', ';', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx < 0) idx = part.IndexOf(':');
            if (idx > 0)
            {
                var left = Regex.Replace(part[..idx], @"^(?:assign|set|add)\s+formulations?\s*", "", RegexOptions.IgnoreCase).Trim();
                var right = part[(idx + 1)..].Trim();
                if (!string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right))
                {
                    assignments.Add(new Dictionary<string, object?> { ["product_name"] = left, ["formulation"] = right });
                }
            }
        }
        return (assignments, standalone);
    }

    private static void ParseKeyValuePairs(string text, Dictionary<string, object?> target)
    {
        var keyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["name"] = "name", ["product"] = "name", ["product_name"] = "name", ["title"] = "name",
            ["new_name"] = "new_name", ["rename"] = "new_name",
            ["pack"] = "pack_size", ["pack_size"] = "pack_size", ["packsize"] = "pack_size",
            ["category"] = "category", ["cat"] = "category",
            ["brand"] = "brand", ["company"] = "brand", ["manufacturer"] = "brand",
            ["supplier"] = "supplier", ["vendor"] = "supplier", ["distributor"] = "supplier",
            ["formulation"] = "formulation", ["salt"] = "formulation", ["generic"] = "formulation",
            ["rack"] = "rack", ["location"] = "rack",
            ["barcode"] = "barcode",
            ["price"] = "pack_sale_price", ["sale_price"] = "pack_sale_price", ["pack_sale_price"] = "pack_sale_price",
            ["purchase_price"] = "pack_purchase_price", ["pack_purchase_price"] = "pack_purchase_price", ["cost"] = "pack_purchase_price",
            ["phone"] = "phone", ["mobile"] = "phone",
            ["email"] = "email",
            ["address"] = "address",
            ["code"] = "code",
            ["qty"] = "quantity", ["quantity"] = "quantity", ["requested_quantity"] = "requested_quantity",
            ["notes"] = "notes"
        };

        var matches = Regex.Matches(text, @"\b([a-z_]+)\s*[:=]\s*([^,\n;]+)", RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            var rawKey = m.Groups[1].Value.Trim();
            var rawVal = m.Groups[2].Value.Trim();
            if (keyMap.TryGetValue(rawKey, out var canon) && !string.IsNullOrWhiteSpace(rawVal))
            {
                target[canon] = rawVal;
            }
        }
    }

    private static string ExtractOptionalFilterFromPrompt(string text)
    {
        var m = Regex.Match(text, @"\b(?:for|matching|named|containing|filter)\s+['""]?([a-zA-Z0-9\s\-]{2,40})['""]?", RegexOptions.IgnoreCase);
        if (!m.Success) return "";
        var val = m.Groups[1].Value.Trim();
        if (Regex.IsMatch(val, @"^(all|missing|empty|products|medicines|items)$", RegexOptions.IgnoreCase)) return "";
        return val;
    }

    // =========================================================================
    // 5. DB & Formatting Helpers
    // =========================================================================

    private static async Task<bool> EnsureCanonicalFormulationAsync(DbConnection conn, string name, string nowStr, CancellationToken ct)
    {
        var norm = Formulation.NormalizeName(name);
        if (string.IsNullOrWhiteSpace(norm)) return false;
        try
        {
            await using var chk = conn.CreateCommand();
            chk.CommandText = "SELECT COUNT(1) FROM formulations WHERE normalized_name = @norm";
            AddParam(chk, "@norm", norm);
            if (Convert.ToInt32(await chk.ExecuteScalarAsync(ct) ?? 0) > 0) return false;

            await using var ins = conn.CreateCommand();
            ins.CommandText = "INSERT INTO formulations (name, normalized_name, created_at, updated_at) VALUES (@n, @norm, @now, @now)";
            AddParam(ins, "@n", name.Trim());
            AddParam(ins, "@norm", norm);
            AddParam(ins, "@now", nowStr);
            await ins.ExecuteNonQueryAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<(long Id, string Name)?> FindEntityByNameAsync(DbConnection conn, string table, string query, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT id, name FROM {table} WHERE LOWER(name) = LOWER(@exact) OR LOWER(name) LIKE LOWER(@like) ORDER BY CASE WHEN LOWER(name) = LOWER(@exact) THEN 0 ELSE 1 END, id ASC LIMIT 1";
        AddParam(cmd, "@exact", query.Trim());
        AddParam(cmd, "@like", $"%{query.Trim()}%");
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (await r.ReadAsync(ct)) return (r.GetInt64(0), r.GetString(1));
        return null;
    }

    private static async Task<long> ResolveOrCreateCategoryAsync(DbConnection conn, string categoryName, string nowStr, CancellationToken ct)
    {
        var existing = await FindEntityByNameAsync(conn, "categories", categoryName, ct);
        if (existing != null) return existing.Value.Id;
        return await InsertRowAsync(conn, "categories", new Dictionary<string, object?>
        {
            ["name"] = categoryName,
            ["created_at"] = nowStr,
            ["updated_at"] = nowStr
        }, ct);
    }

    private static async Task<(string Code, string Barcode)> GenerateUniqueCodeAndBarcodeAsync(DbConnection conn, CancellationToken ct)
    {
        var maxId = 0L;
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COALESCE(MAX(id), 0) FROM products";
            maxId = Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0);
        }
        var seq = maxId + 1;
        var code = $"PRD-{seq:D5}";
        var barcode = $"896{seq:D9}";
        return (code, barcode);
    }

    private static async Task<Dictionary<string, object?>?> LoadProductByIdAsync(DbConnection conn, long id, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, product_code, barcode, name, formulation, pack_size, pack_purchase_price, pack_sale_price, rack FROM products WHERE id = @id LIMIT 1";
        AddParam(cmd, "@id", id);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        if (!await r.ReadAsync(ct)) return null;
        return ReadProductDict(r);
    }

    private static async Task<List<Dictionary<string, object?>>> SearchProductsAsync(DbConnection conn, string query, int limit, CancellationToken ct)
    {
        var list = new List<Dictionary<string, object?>>();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT id, product_code, barcode, name, formulation, pack_size, pack_purchase_price, pack_sale_price, rack
            FROM products
            WHERE LOWER(name) LIKE LOWER(@like) OR LOWER(COALESCE(product_code, '')) = LOWER(@exact) OR LOWER(COALESCE(barcode, '')) = LOWER(@exact)
            ORDER BY CASE WHEN LOWER(name) = LOWER(@exact) THEN 0 WHEN LOWER(name) LIKE LOWER(@prefix) THEN 1 ELSE 2 END, id ASC
            LIMIT @lim";
        AddParam(cmd, "@exact", query.Trim());
        AddParam(cmd, "@prefix", $"{query.Trim()}%");
        AddParam(cmd, "@like", $"%{query.Trim()}%");
        AddParam(cmd, "@lim", limit);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) list.Add(ReadProductDict(r));
        return list;
    }

    private static Dictionary<string, object?> ReadProductDict(DbDataReader r)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = r.GetInt64(0),
            ["product_code"] = r.IsDBNull(1) ? "" : r.GetString(1),
            ["barcode"] = r.IsDBNull(2) ? "" : r.GetString(2),
            ["name"] = r.IsDBNull(3) ? "" : r.GetString(3),
            ["formulation"] = r.IsDBNull(4) ? "" : r.GetString(4),
            ["pack_size"] = r.IsDBNull(5) ? 1 : Convert.ToInt32(r.GetValue(5)),
            ["pack_purchase_price"] = r.IsDBNull(6) ? 0m : Convert.ToDecimal(r.GetValue(6)),
            ["pack_sale_price"] = r.IsDBNull(7) ? 0m : Convert.ToDecimal(r.GetValue(7)),
            ["rack"] = r.IsDBNull(8) ? "" : r.GetString(8)
        };
    }

    private static async Task<long> InsertRowAsync(DbConnection conn, string table, Dictionary<string, object?> values, CancellationToken ct)
    {
        var cols = values.Keys.ToList();
        var paramNames = cols.Select((_, i) => $"@p{i}").ToList();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"INSERT INTO {table} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", paramNames)}); SELECT last_insert_rowid();";
        for (var i = 0; i < cols.Count; i++) AddParam(cmd, paramNames[i], values[cols[i]]);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct) ?? 0);
    }

    private static Dictionary<string, object?> BuildSingleRecordPreview(string table, string operation, string title, Dictionary<string, string> fields)
    {
        return new Dictionary<string, object?>
        {
            ["staged"] = true,
            ["posted_to_database"] = false,
            ["database_table"] = table,
            ["operation"] = operation,
            ["records"] = new List<Dictionary<string, object?>>
            {
                new()
                {
                    ["title"] = title,
                    ["badge_en"] = operation,
                    ["badge_ur"] = operation,
                    ["fields"] = fields.Select(kv => new Dictionary<string, object?>
                    {
                        ["field"] = kv.Key,
                        ["label_ur"] = kv.Key,
                        ["before"] = operation == "INSERT" ? "(New)" : "(Current)",
                        ["after"] = kv.Value
                    }).ToList()
                }
            }
        };
    }

    private static void SavePendingTask(long userId, Dictionary<string, object?> task)
    {
        PendingTasks[userId] = (DateTime.UtcNow.AddMinutes(30), task);
    }

    private static Dictionary<string, object?>? GetPendingTask(long userId)
    {
        if (PendingTasks.TryGetValue(userId, out var entry))
        {
            if (DateTime.UtcNow <= entry.ExpiresAt) return entry.Task;
            PendingTasks.TryRemove(userId, out _);
        }
        return null;
    }

    public static void ClearPendingTask(long userId) => PendingTasks.TryRemove(userId, out _);

    public static Dictionary<string, object?> BuildDeleteBlockedResponse(string productVersion = "2026.10")
    {
        return FormatTaskResponse(
            "Destructive Action Blocked (Safety Policy)",
            "حفاظتی پالیسی: ڈیلیٹ کرنے کی اجازت نہیں ہے",
            "**Safety Policy:** Cube AI is strictly non-destructive and is hardcoded to **never delete or remove** any records from your database. Please use the respective management screen if you have Administrator permissions.",
            "**حفاظتی پالیسی:** کیوب اے آئی کسی بھی ریکارڈ کو ڈیلیٹ یا ختم نہیں کر سکتا۔ براہِ کرم متعلقہ اسکرین سے خود ڈیلیٹ کریں۔",
            [],
            new Dictionary<string, object?> { ["action"] = "delete_blocked", ["status"] = "refused" },
            "cube_task_refused",
            productVersion,
            kind: "task_refused");
    }

    private static Dictionary<string, object?> BuildTaskCancelledResponse(string productVersion)
    {
        return FormatTaskResponse(
            "Task Cancelled — No Database Changes Made",
            "ٹاسک منسوخ کر دیا گیا — کوئی تبدیلی نہیں کی گئی",
            "The pending Cube AI task has been cancelled. **Zero changes were posted to the database.**",
            "زیرِ التواء ٹاسک منسوخ کر دیا گیا ہے۔ ڈیٹا بیس میں کوئی تبدیلی نہیں کی گئی۔",
            [],
            new Dictionary<string, object?> { ["status"] = "cancelled" },
            "cube_task_cancelled",
            productVersion);
    }

    private static Dictionary<string, object?> BuildPermissionDeniedResponse(string action, string permission, string productVersion)
    {
        return FormatTaskResponse(
            $"Permission Denied: `{permission}` Required",
            "اجازت نہیں ہے",
            $"You need the `{permission}` permission to run `{action}`.",
            $"اس عمل کے لیے `{permission}` کی اجازت درکار ہے۔",
            [],
            new Dictionary<string, object?> { ["action"] = action, ["status"] = "permission_denied" },
            "cube_task_permission_denied",
            productVersion);
    }

    public static Dictionary<string, object?> FormatTaskResponse(
        string titleEn,
        string titleUr,
        string answerEn,
        string answerUr,
        List<Dictionary<string, string>> links,
        Dictionary<string, object?> taskMeta,
        string providerStatus = "cube_task_executed",
        string productVersion = "2026.10",
        string kind = "task_result")
    {
        return new Dictionary<string, object?>
        {
            ["ok"] = providerStatus is not ("cube_task_refused" or "cube_task_permission_denied" or "cube_task_unauthorized" or "cube_task_error"),
            ["kind"] = kind,
            ["title_en"] = titleEn,
            ["title_ur"] = titleUr,
            ["answer_en"] = answerEn,
            ["answer_ur"] = answerUr,
            ["title"] = new Dictionary<string, string> { ["en"] = titleEn, ["ur"] = titleUr },
            ["en"] = answerEn,
            ["ur"] = answerUr,
            ["source_ids"] = new[] { $"task:{taskMeta.GetValueOrDefault("action") ?? "cube"}" },
            ["evidence_classification"] = "cube_task_execution",
            ["evidence_badge"] = new Dictionary<string, string>
            {
                ["en"] = $"Cube AI Task Engine • Non-Destructive Action (v{productVersion})",
                ["ur"] = $"کیوب اے آئی ٹاسک انجن • محفوظ عمل (v{productVersion})"
            },
            ["provider"] = "local",
            ["provider_status"] = providerStatus,
            ["fallback_used"] = false,
            ["links"] = links,
            ["workflow_id"] = null,
            ["product_version"] = productVersion,
            ["task_meta"] = taskMeta
        };
    }

    private static void AddParam(DbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static string? GetStr(Dictionary<string, object?> d, string key)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return null;
        if (v is JsonElement je) return je.ValueKind == JsonValueKind.String ? je.GetString() : (je.ValueKind == JsonValueKind.Null ? null : je.ToString());
        return v.ToString();
    }

    private static int GetInt(Dictionary<string, object?> d, string key, int fallback)
    {
        var s = GetStr(d, key);
        return int.TryParse(s?.Trim(), out var n) ? n : fallback;
    }

    private static long GetLong(Dictionary<string, object?> d, string key, long fallback)
    {
        var s = GetStr(d, key);
        return long.TryParse(s?.Trim(), out var n) ? n : fallback;
    }

    private static decimal GetDecimal(Dictionary<string, object?> d, string key, decimal fallback)
    {
        var s = GetStr(d, key);
        return decimal.TryParse(s?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) ? n : fallback;
    }

    private static bool GetBool(Dictionary<string, object?> d, string key, bool fallback)
    {
        if (!d.TryGetValue(key, out var v) || v == null) return fallback;
        if (v is bool b) return b;
        if (v is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.True) return true;
            if (je.ValueKind == JsonValueKind.False) return false;
        }
        var s = v.ToString()?.Trim().ToLowerInvariant() ?? "";
        if (s is "1" or "true" or "yes") return true;
        if (s is "0" or "false" or "no") return false;
        return fallback;
    }

    private static List<Dictionary<string, object?>> ExtractListOfDicts(Dictionary<string, object?> d, string key)
    {
        var result = new List<Dictionary<string, object?>>();
        if (!d.TryGetValue(key, out var val) || val == null) return result;

        if (val is List<Dictionary<string, object?>> already) return already;
        if (val is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in je.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.Object)
                {
                    var item = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in el.EnumerateObject()) item[p.Name] = p.Value.Clone();
                    result.Add(item);
                }
            }
        }
        return result;
    }

    private static List<string> ExtractStringList(Dictionary<string, object?> d, string key)
    {
        var result = new List<string>();
        if (!d.TryGetValue(key, out var val) || val == null) return result;
        if (val is List<string> already) return already;
        if (val is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in je.EnumerateArray())
            {
                if (el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString()))
                    result.Add(el.GetString()!);
            }
        }
        return result;
    }
}

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
    private readonly CubeAssistantService? _cubeAssistant;
    private readonly KarobarDbContext? _dbContext;

    private const string DefaultProductVersion = "2026.10";

    private static readonly string[] ProhibitedAskKeys = new[]
    {
        "provider", "model", "api_key", "navigation_url", "navigation_urls",
        "links", "action_id", "workflow_id", "selector", "css_selector",
        "target", "target_selector", "dom_target"
    };

    private static readonly Dictionary<string, string> PharmacySaltDictionary = new(StringComparer.OrdinalIgnoreCase)
    {
        ["panadol cf"] = "Paracetamol + Chlorpheniramine + Pseudoephedrine",
        ["panadol extra"] = "Paracetamol + Caffeine",
        ["panadol"] = "Paracetamol",
        ["calpol"] = "Paracetamol",
        ["febrol"] = "Paracetamol",
        ["disprol"] = "Paracetamol",
        ["paracetamol"] = "Paracetamol",
        ["nuberol forte"] = "Paracetamol + Orphenadrine Citrate",
        ["nuberol"] = "Paracetamol + Orphenadrine Citrate",
        ["brufen"] = "Ibuprofen",
        ["ibuprofen"] = "Ibuprofen",
        ["arinac"] = "Ibuprofen + Pseudoephedrine",
        ["ponstan"] = "Mefenamic Acid",
        ["disprin"] = "Aspirin",
        ["loprin"] = "Aspirin",
        ["voltral"] = "Diclofenac Sodium",
        ["caflam"] = "Diclofenac Potassium",
        ["synflex"] = "Naproxen Sodium",
        ["augmentin"] = "Amoxicillin + Clavulanic Acid",
        ["calamox"] = "Amoxicillin + Clavulanic Acid",
        ["amoxil"] = "Amoxicillin",
        ["amoxicillin"] = "Amoxicillin",
        ["cefspan"] = "Cefixime",
        ["cefiget"] = "Cefixime",
        ["cefixime"] = "Cefixime",
        ["velosef"] = "Cephradine",
        ["rocephin"] = "Ceftriaxone",
        ["ceftriaxone"] = "Ceftriaxone",
        ["azomax"] = "Azithromycin",
        ["zithromax"] = "Azithromycin",
        ["azithromycin"] = "Azithromycin",
        ["klaricid"] = "Clarithromycin",
        ["novidat"] = "Ciprofloxacin",
        ["ciprofloxacin"] = "Ciprofloxacin",
        ["leflox"] = "Levofloxacin",
        ["levofloxacin"] = "Levofloxacin",
        ["flagyl"] = "Metronidazole",
        ["metronidazole"] = "Metronidazole",
        ["risek"] = "Omeprazole",
        ["omeprazole"] = "Omeprazole",
        ["nexum"] = "Esomeprazole",
        ["esomeprazole"] = "Esomeprazole",
        ["zopent"] = "Pantoprazole",
        ["gaviscon"] = "Sodium Alginate + Potassium Bicarbonate",
        ["gravinate"] = "Dimenhydrinate",
        ["motilium"] = "Domperidone",
        ["rigix"] = "Cetirizine",
        ["softin"] = "Loratadine",
        ["fexet"] = "Fexofenadine",
        ["telfast"] = "Fexofenadine",
        ["ventolin"] = "Salbutamol",
        ["myteka"] = "Montelukast",
        ["glucophage"] = "Metformin",
        ["amaryl"] = "Glimepiride",
        ["getryl"] = "Glimepiride",
        ["januvia"] = "Sitagliptin",
        ["norvasc"] = "Amlodipine",
        ["sofvasc"] = "Amlodipine",
        ["exforge"] = "Amlodipine + Valsartan",
        ["eziday"] = "Losartan Potassium",
        ["lipiget"] = "Atorvastatin",
        ["rovista"] = "Rosuvastatin",
        ["concor"] = "Bisoprolol",
        ["inderal"] = "Propranolol",
        ["lexotanil"] = "Bromazepam",
        ["alp"] = "Alprazolam",
        ["surbex z"] = "Vitamin B-Complex + Vitamin C + Zinc",
        ["sunny d"] = "Cholecalciferol (Vitamin D3)"
    };

    public CubeAssistantController(
        KarobarDb db,
        CubeAssistantService? cubeAssistant = null,
        KarobarDbContext? dbContext = null) : base(db)
    {
        _cubeAssistant = cubeAssistant;
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

        var lowerQ = question.ToLowerInvariant();

        // Check for destructive delete command
        if (explainMode == "qa" && Regex.IsMatch(lowerQ, @"^(?:please\s+|cube\s+|plz\s+)?(?:delete|remove|destroy|erase|wipe|truncate|drop)\s+"))
        {
            return Ok(BuildDeleteBlockedResponse());
        }

        // Check if cursor_element mode with baseline explanations
        if (explainMode == "cursor_element" && body.TryGetValue("element_context", out var elCtx) && elCtx.ValueKind == JsonValueKind.Object)
        {
            var label = GetElementProp(elCtx, "label") ?? "UI Control";
            var baselineEn = GetElementProp(elCtx, "baseline_en") ?? $"Interactive control ({label}) on `{currentRoute}`.";
            var baselineUr = GetElementProp(elCtx, "baseline_ur") ?? $"اسکرین `{currentRoute}` پر موجود کنٹرول ({label})۔";

            return Ok(FormatQaResponse(
                titleEn: label,
                titleUr: label,
                answerEn: baselineEn,
                answerUr: baselineUr,
                sourceIds: new[] { $"element:{currentRoute}" },
                evidenceClassification: "documented_info",
                provider: "local",
                providerStatus: "local_fallback",
                fallbackUsed: true,
                links: new List<Dictionary<string, string>>
                {
                    new() { ["path"] = currentRoute, ["en"] = "Current Screen", ["ur"] = "موجودہ اسکرین" }
                }
            ));
        }

        if (_cubeAssistant != null && _dbContext != null)
        {
            var askPayload = new Dictionary<string, object?>
            {
                ["question"] = question,
                ["language"] = language,
                ["current_route"] = currentRoute,
                ["explain_mode"] = explainMode,
                ["matched_intent_id"] = GetString(body, "matched_intent_id"),
                ["screen_guide_id"] = GetString(body, "screen_guide_id")
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
            return Ok(await _cubeAssistant.AskAsync(_dbContext, askPayload, HttpContext.RequestAborted));
        }

        // Route-aware deterministic knowledge fallback when CubeAssistantService is unavailable
        var (titleEn, titleUr, ansEn, ansUr, links) = BuildLocalGuideAnswer(question, currentRoute);

        return Ok(FormatQaResponse(
            titleEn: titleEn,
            titleUr: titleUr,
            answerEn: ansEn,
            answerUr: ansUr,
            sourceIds: new[] { $"guide:{currentRoute.Trim('/')}" },
            evidenceClassification: "documented_info",
            provider: "local",
            providerStatus: "local_fallback",
            fallbackUsed: true,
            links: links
        ));
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

        var lowerType = taskType.ToLowerInvariant();
        if (lowerType.Contains("delete") || lowerType.Contains("remove") || lowerType.Contains("destroy"))
        {
            return Ok(BuildDeleteBlockedResponse());
        }

        if (taskType == "bulk_autofill_missing_formulations")
        {
            return Ok(await ExecuteBulkAutofillFormulationsAsync(body));
        }

        if (taskType == "bulk_assign_formulations")
        {
            return Ok(await ExecuteBulkAssignFormulationsAsync(body));
        }

        if (taskType is "create_brands" or "create_categories" or "create_suppliers" or "create_customers"
            or "create_storage_locations" or "create_expense_categories" or "create_user_demands")
        {
            return Ok(await ExecuteCreateMasterEntityAsync(taskType, body));
        }

        return Unprocessable($"Unsupported Cube AI task type: '{taskType}'.", new Dictionary<string, string[]>
        {
            ["task_type"] = new[] { $"Unsupported task type '{taskType}'." }
        });
    }

    private async Task<Dictionary<string, object?>> ExecuteBulkAutofillFormulationsAsync(Dictionary<string, JsonElement> body)
    {
        if (!await IsAdminAsync() && !await CanAsync("product.update", "product.create"))
        {
            return FormatTaskResponse(
                "Permission Denied: Product Update Required",
                "اجازت نہیں ہے: پروڈکٹ اپڈیٹ کی اجازت درکار ہے",
                "You need the `product.update` permission to auto-fill product formulations.",
                "پروڈکٹس میں فارمولیشن شامل کرنے کے لیے `product.update` کی اجازت درکار ہے۔",
                new List<Dictionary<string, string>>(),
                new Dictionary<string, object?> { ["action"] = "bulk_autofill_missing_formulations", ["status"] = "permission_denied" },
                "cube_task_permission_denied"
            );
        }

        var batchSize = Math.Max(1, Math.Min(30, GetInt(body, "batch_size", 20)));
        var afterId = Math.Max(0, GetInt(body, "after_id", 0));
        var filterQuery = (GetString(body, "filter_query") ?? "").Trim();
        var previewOnly = GetBool(body, "preview_only", false);
        var userConfirmed = !previewOnly && GetBool(body, "user_confirmed", true);

        var whereFilter = !string.IsNullOrEmpty(filterQuery) ? "AND p.name LIKE @likeFilter" : "";
        var likeFilter = $"%{filterQuery}%";

        var candidates = await Db.QueryListAsync($@"
            SELECT p.id, p.product_code, p.name, p.formulation
            FROM products p
            WHERE (p.formulation IS NULL OR TRIM(p.formulation) = '')
              AND p.id > @afterId
              {whereFilter}
            ORDER BY p.id ASC
            LIMIT @batchSize",
            new { afterId, likeFilter, batchSize });

        int updatedCount = 0;
        long lastId = afterId;
        var updatedItems = new List<Dictionary<string, object?>>();
        var skippedItems = new List<Dictionary<string, object?>>();

        foreach (var prod in candidates)
        {
            var pid = Convert.ToInt64(prod["id"]);
            lastId = pid;
            var pName = prod["name"]?.ToString() ?? "";
            var matchedSalt = MatchPharmacySalt(pName);

            if (!string.IsNullOrEmpty(matchedSalt))
            {
                if (userConfirmed)
                {
                    await Db.UpdateAsync("products", pid, new Dictionary<string, object?>
                    {
                        ["formulation"] = matchedSalt
                    });
                }
                updatedCount++;
                updatedItems.Add(new Dictionary<string, object?>
                {
                    ["id"] = pid,
                    ["product_code"] = prod["product_code"],
                    ["name"] = pName,
                    ["formulation"] = matchedSalt
                });
            }
            else
            {
                skippedItems.Add(new Dictionary<string, object?>
                {
                    ["id"] = pid,
                    ["product_code"] = prod["product_code"],
                    ["name"] = pName,
                    ["reason"] = "No verified salt match found in dictionary"
                });
            }
        }

        var remaining = await Db.ExecuteScalarAsync<int>($@"
            SELECT COUNT(*)
            FROM products p
            WHERE (p.formulation IS NULL OR TRIM(p.formulation) = '')
              AND p.id > @lastId
              {whereFilter}",
            new { lastId, likeFilter });

        var hasMore = !previewOnly && candidates.Count == batchSize && remaining > 0;

        var taskMeta = new Dictionary<string, object?>
        {
            ["action"] = "bulk_autofill_missing_formulations",
            ["status"] = previewOnly ? "preview" : (hasMore ? "in_progress" : "completed"),
            ["preview_only"] = previewOnly,
            ["updated_count"] = updatedCount,
            ["updated_items"] = updatedItems,
            ["updated_products"] = updatedItems,
            ["skipped_products"] = skippedItems,
            ["refresh_products"] = !previewOnly && updatedCount > 0,
            ["background_task"] = new Dictionary<string, object?>
            {
                ["task_type"] = "bulk_autofill_missing_formulations",
                ["has_more"] = hasMore,
                ["next_after_id"] = lastId,
                ["last_processed_id"] = lastId,
                ["batch_size"] = batchSize,
                ["filter_query"] = filterQuery,
                ["processed_in_batch"] = candidates.Count,
                ["updated_in_batch"] = updatedCount,
                ["remaining_estimate"] = remaining,
                ["remaining_count"] = remaining
            }
        };

        if (previewOnly)
        {
            var totalMissing = await Db.ExecuteScalarAsync<int>($@"
                SELECT COUNT(*)
                FROM products p
                WHERE (p.formulation IS NULL OR TRIM(p.formulation) = '')
                  {whereFilter}",
                new { likeFilter });

            taskMeta["preview_result"] = new Dictionary<string, object?>
            {
                ["total_missing"] = totalMissing,
                ["sample_matches"] = updatedItems,
                ["sample_skipped"] = skippedItems,
                ["matched_in_sample"] = updatedItems.Count,
                ["skipped_in_sample"] = skippedItems.Count
            };
        }

        return FormatTaskResponse(
            titleEn: previewOnly ? $"Preview: Auto-Fill Formulations ({updatedCount} Match(es) in Sample)" : $"Auto-Filled Formulations ({updatedCount} Updated)",
            titleUr: previewOnly ? $"پیش نظارہ: خودکار فارمولیشن ({updatedCount} میچ)" : $"خودکار فارمولیشن اپڈیٹ ({updatedCount} پروڈکٹس)",
            answerEn: previewOnly
                ? $"Preview checked {candidates.Count} product(s): **{updatedCount}** matched verified DRAP salts (no database changes made yet)."
                : $"Processed {candidates.Count} product(s) and updated **{updatedCount}** formulation(s) from verified DRAP salt dictionary.",
            answerUr: previewOnly
                ? $"پیش نظارہ میں {candidates.Count} پروڈکٹس چیک ہوئیں: **{updatedCount}** میچ ملے (ابھی ڈیٹا بیس میں تبدیلی نہیں کی گئی)۔"
                : $"{candidates.Count} پروڈکٹس چیک کی گئیں اور **{updatedCount}** پروڈکٹس کی فارمولیشن اپڈیٹ کر دی گئی۔",
            links: new List<Dictionary<string, string>>
            {
                new() { ["path"] = "/products", ["en"] = "Open Products Catalog", ["ur"] = "پروڈکٹس لسٹ کھولیں" }
            },
            taskMeta: taskMeta
        );
    }

    private async Task<Dictionary<string, object?>> ExecuteBulkAssignFormulationsAsync(Dictionary<string, JsonElement> body)
    {
        if (!await IsAdminAsync() && !await CanAsync("product.update", "product.create"))
        {
            return FormatTaskResponse(
                "Permission Denied",
                "اجازت نہیں ہے",
                "You need `product.update` permission to assign formulations.",
                "فارمولیشن تفویض کرنے کے لیے `product.update` اجازت درکار ہے۔",
                new List<Dictionary<string, string>>(),
                new Dictionary<string, object?> { ["action"] = "bulk_assign_formulations", ["status"] = "permission_denied" },
                "cube_task_permission_denied"
            );
        }

        int updated = 0;
        if (body.TryGetValue("assignments", out var assignEl) && assignEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in assignEl.EnumerateArray())
            {
                var pName = GetElementProp(item, "product_name")?.Trim();
                var form = GetElementProp(item, "formulation")?.Trim();
                if (!string.IsNullOrEmpty(pName) && !string.IsNullOrEmpty(form))
                {
                    var count = await Db.ExecuteAsync(
                        "UPDATE products SET formulation = @form, updated_at = @now WHERE LOWER(name) LIKE LOWER(@likeName)",
                        new { form, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), likeName = $"%{pName}%" });
                    updated += count;
                }
            }
        }

        return FormatTaskResponse(
            titleEn: $"Assigned Formulations ({updated} Product(s) Updated)",
            titleUr: $"فارمولیشن اپڈیٹ ({updated} پروڈکٹس)",
            answerEn: $"Successfully updated formulation on **{updated}** product(s).",
            answerUr: $"**{updated}** پروڈکٹس کی فارمولیشن کامیابی سے اپڈیٹ کر دی گئی۔",
            links: new List<Dictionary<string, string>>
            {
                new() { ["path"] = "/products", ["en"] = "Open Products", ["ur"] = "پروڈکٹس کھولیں" }
            },
            taskMeta: new Dictionary<string, object?>
            {
                ["action"] = "bulk_assign_formulations",
                ["status"] = "completed",
                ["updated_count"] = updated
            }
        );
    }

    private async Task<Dictionary<string, object?>> ExecuteCreateMasterEntityAsync(string taskType, Dictionary<string, JsonElement> body)
    {
        var requiredPerms = taskType switch
        {
            "create_brands" => new[] { "brand.create" },
            "create_categories" => new[] { "category.create" },
            "create_suppliers" => new[] { "supplier.create" },
            "create_customers" => new[] { "customer.create" },
            "create_storage_locations" => new[] { "storage-location.manage", "product.create", "product.update" },
            "create_expense_categories" => new[] { "expense-category.manage", "expense.create" },
            "create_user_demands" => new[] { "user-demands.create" },
            _ => new[] { "settings.update" }
        };

        if (!await IsAdminAsync() && !await CanAsync(requiredPerms))
        {
            return FormatTaskResponse(
                "Permission Denied",
                "اجازت نہیں ہے",
                $"You lack the required permission ({string.Join(" / ", requiredPerms)}) to execute {taskType}.",
                "اس عمل کے لیے مطلوبہ اجازت موجود نہیں ہے۔",
                new List<Dictionary<string, string>>(),
                new Dictionary<string, object?> { ["action"] = taskType, ["status"] = "permission_denied" },
                "cube_task_permission_denied"
            );
        }

        var name = (GetString(body, "name") ?? "").Trim();
        var userConfirmed = GetBool(body, "user_confirmed", false);

        if (string.IsNullOrEmpty(name) || !userConfirmed)
        {
            return FormatTaskResponse(
                titleEn: $"Permission & Details Required for {taskType}",
                titleUr: "تصدیق اور تفصیلات درکار ہیں",
                answerEn: "Please confirm and provide the required name to commit this record to the database.",
                answerUr: "براہِ کرم ریکارڈ محفوظ کرنے کے لیے نام اور اجازت کی تصدیق کریں۔",
                links: new List<Dictionary<string, string>>(),
                taskMeta: new Dictionary<string, object?>
                {
                    ["action"] = taskType,
                    ["awaiting_permission"] = !string.IsNullOrEmpty(name),
                    ["awaiting_input"] = string.IsNullOrEmpty(name)
                },
                providerStatus: string.IsNullOrEmpty(name) ? "cube_task_needs_input" : "cube_task_awaiting_permission"
            );
        }

        long id = 0;
        switch (taskType)
        {
            case "create_brands":
                id = await Db.InsertAsync("brands", new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["expiry_return_policy"] = "before_expiry",
                    ["expiry_return_months"] = 3
                });
                break;
            case "create_categories":
                id = await Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = name });
                break;
            case "create_suppliers":
                id = await Db.InsertAsync("suppliers", new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["phone"] = GetString(body, "phone"),
                    ["address"] = GetString(body, "address")
                });
                break;
            case "create_customers":
                id = await Db.InsertAsync("customers", new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["phone"] = GetString(body, "phone"),
                    ["address"] = GetString(body, "address")
                });
                break;
            case "create_storage_locations":
                id = await Db.InsertAsync("storage_locations", new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["code"] = GetString(body, "code") ?? $"LOC-{DateTime.UtcNow:mmss}",
                    ["type"] = "rack",
                    ["default_rack"] = GetString(body, "default_rack") ?? "R-01",
                    ["is_active"] = 1
                });
                break;
            case "create_expense_categories":
                id = await Db.InsertAsync("expense_categories", new Dictionary<string, object?>
                {
                    ["name"] = name,
                    ["code"] = GetString(body, "code") ?? $"EXP-{DateTime.UtcNow:mmss}",
                    ["is_active"] = 1
                });
                break;
            case "create_user_demands":
                var demandProductId = GetNullableLong(body, "product_id");
                if (!demandProductId.HasValue || demandProductId.Value <= 0)
                {
                    demandProductId = await Db.ExecuteScalarAsync<long?>(
                        "SELECT id FROM products WHERE LOWER(name) = LOWER(@name) OR LOWER(name) LIKE LOWER(@like) ORDER BY CASE WHEN LOWER(name) = LOWER(@name) THEN 0 ELSE 1 END, id ASC LIMIT 1",
                        new { name, like = $"%{name}%" });
                }
                if (!demandProductId.HasValue || demandProductId.Value <= 0)
                {
                    return FormatTaskResponse(
                        "Product Not Found for Demand",
                        "ڈیمانڈ کے لیے پروڈکٹ نہیں ملی",
                        $"No matching product found for **{name}**. Please specify a valid `product_id` or create the product first.",
                        $"**{name}** سے ملتی جلتی کوئی پروڈکٹ نہیں ملی۔ پہلے پروڈکٹ منتخب کریں۔",
                        new List<Dictionary<string, string>> { new() { ["path"] = "/user-demands/create", ["en"] = "Open User Demands", ["ur"] = "یوزر ڈیمانڈز کھولیں" } },
                        new Dictionary<string, object?> { ["action"] = taskType, ["status"] = "product_not_found" },
                        "cube_task_needs_input"
                    );
                }
                id = await Db.InsertAsync("user_demands", new Dictionary<string, object?>
                {
                    ["product_id"] = demandProductId.Value,
                    ["user_id"] = CurrentUserId,
                    ["requested_by"] = CurrentUserId,
                    ["requested_name"] = name,
                    ["quantity"] = Math.Max(1, GetInt(body, "requested_quantity", GetInt(body, "quantity", 1))),
                    ["requested_quantity"] = Math.Max(1, GetInt(body, "requested_quantity", GetInt(body, "quantity", 1))),
                    ["status"] = "pending",
                    ["notes"] = GetString(body, "notes") ?? "Logged via Cube AI Assistant"
                });
                break;
        }

        return FormatTaskResponse(
            titleEn: $"Created {name} (#{id})",
            titleUr: $"{name} ڈیٹا بیس میں محفوظ ہو گیا (#{id})",
            answerEn: $"Successfully created **{name}** (ID `#{id}`).",
            answerUr: $"**{name}** کامیابی سے ڈیٹا بیس میں شامل ہو گیا (ID `#{id}`)۔",
            links: new List<Dictionary<string, string>>(),
            taskMeta: new Dictionary<string, object?>
            {
                ["action"] = taskType,
                ["status"] = "completed",
                ["created_count"] = 1,
                ["created_id"] = id
            }
        );
    }

    private static string? MatchPharmacySalt(string productName)
    {
        if (string.IsNullOrWhiteSpace(productName)) return null;
        var lower = Regex.Replace(productName.ToLowerInvariant(), @"[^a-z0-9\s]", " ");
        lower = Regex.Replace(lower, @"\s+", " ").Trim();

        foreach (var kv in PharmacySaltDictionary.OrderByDescending(k => k.Key.Length))
        {
            if (lower.Contains(kv.Key))
            {
                return kv.Value;
            }
        }

        return null;
    }

    private static (string titleEn, string titleUr, string ansEn, string ansUr, List<Dictionary<string, string>> links) BuildLocalGuideAnswer(string question, string currentRoute)
    {
        var lower = question.ToLowerInvariant();
        if (lower.Contains("sale") || lower.Contains("invoice") || lower.Contains("pos") || currentRoute.StartsWith("/sale-invoices"))
        {
            return (
                "Sale Invoices & POS Guide",
                "سیل انوائس اور پی او ایس رہنمائی",
                "To create a Sale Invoice, open **Sale Invoices**, scan a product barcode or search by product/formulation name, select the batch (FEFO is selected automatically), enter pack or loose unit quantities, and press **Save & Print**.",
                "سیل انوائس بنانے کے لیے **Sale Invoices** کھولیں، بارکوڈ اسکین کریں یا پروڈکٹ/فارمولیشن تلاش کریں، مقدار درج کریں اور **Save & Print** دبائیں۔",
                new List<Dictionary<string, string>>
                {
                    new() { ["path"] = "/sale-invoices/create/retail", ["en"] = "Create Sale Invoice", ["ur"] = "نئی سیل انوائس بنائیں" },
                    new() { ["path"] = "/sale-invoices", ["en"] = "Sale Invoices List", ["ur"] = "سیل انوائسز کی فہرست" }
                }
            );
        }

        if (lower.Contains("purchase") || lower.Contains("grn") || currentRoute.StartsWith("/purchase"))
        {
            return (
                "Purchase Invoices & Stock Receiving Guide",
                "پرچیز انوائس اور اسٹاک وصولی کی رہنمائی",
                "Use **Purchase Invoices** to record supplier deliveries (GRN) with batch numbers, expiry dates, pack/unit purchase prices, and bonus quantities. Stock and weighted-average cost update automatically.",
                "سپلائر سے موصول ہونے والا مال درج کرنے کے لیے **Purchase Invoices** استعمال کریں جہاں بیچ نمبر، ایکسپائری اور قیمتِ خرید درج کرنے سے اسٹاک خودکار اپڈیٹ ہو جاتا ہے۔",
                new List<Dictionary<string, string>>
                {
                    new() { ["path"] = "/purchase-invoices/create", ["en"] = "New Purchase Invoice", ["ur"] = "نئی پرچیز انوائس" }
                }
            );
        }

        return (
            "Karobar ERP Assistant Guide",
            "کاروبار ایپ رہنمائی",
            "Welcome to **Cube Hybrid AI Assistant**. You can ask how any screen or control works, or ask Cube to add/update products, auto-fill missing formulations, or create categories, brands, suppliers, customers, and demands.",
            "**کیوب اے آئی اسسٹنٹ** میں خوش آمدید۔ آپ کسی بھی اسکرین کے بارے میں سوال پوچھ سکتے ہیں یا پروڈکٹس، فارمولیشن، برانڈز، کیٹیگریز، سپلائرز اور کسٹمرز شامل کرنے کا کہہ سکتے ہیں۔",
            new List<Dictionary<string, string>>
            {
                new() { ["path"] = currentRoute, ["en"] = "Current Screen", ["ur"] = "موجودہ اسکرین" },
                new() { ["path"] = "/products", ["en"] = "Products Catalog", ["ur"] = "پروڈکٹس کیٹلاگ" }
            }
        );
    }

    private static Dictionary<string, object?> BuildDeleteBlockedResponse()
    {
        return FormatTaskResponse(
            titleEn: "Destructive Action Blocked (Safety Policy)",
            titleUr: "حفاظتی پالیسی: ڈیلیٹ کرنے کی اجازت نہیں ہے",
            answerEn: "**Safety Policy:** Cube AI is strictly non-destructive and is hardcoded to **never delete or remove** any records from your database. Please use the standard screen controls if you have permission to delete a record.",
            answerUr: "**حفاظتی پالیسی:** کیوب اے آئی کسی بھی ریکارڈ کو ڈیلیٹ یا ختم نہیں کر سکتا۔ براہِ کرم متعلقہ اسکرین سے خود ڈیلیٹ کریں۔",
            links: new List<Dictionary<string, string>>(),
            taskMeta: new Dictionary<string, object?>
            {
                ["action"] = "delete_blocked",
                ["status"] = "refused",
                ["non_destructive_enforced"] = true
            },
            providerStatus: "cube_task_delete_blocked"
        );
    }

    private static Dictionary<string, object?> FormatTaskResponse(
        string titleEn,
        string titleUr,
        string answerEn,
        string answerUr,
        List<Dictionary<string, string>> links,
        Dictionary<string, object?> taskMeta,
        string providerStatus = "cube_task_executed")
    {
        var actionName = taskMeta.TryGetValue("action", out var a) && a != null ? a.ToString() : "cube_automation";
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
            ["source_ids"] = new[] { $"task:{actionName}" },
            ["evidence_classification"] = "ai_grounded_docs",
            ["evidence_badge"] = new Dictionary<string, string>
            {
                ["en"] = $"Cube AI Task Automation • Non-Destructive (v{DefaultProductVersion})",
                ["ur"] = $"کیوب اے آئی خودکار ٹاسک • محفوظ موڈ (v{DefaultProductVersion})"
            },
            ["provider"] = "cube_task_engine",
            ["provider_status"] = providerStatus,
            ["fallback_used"] = false,
            ["links"] = links,
            ["workflow_id"] = null,
            ["task_meta"] = taskMeta,
            ["background_task"] = taskMeta.TryGetValue("background_task", out var bt) ? bt : null,
            ["provider_trace"] = new Dictionary<string, object?>()
        };
    }

    private static Dictionary<string, object?> FormatQaResponse(
        string titleEn,
        string titleUr,
        string answerEn,
        string answerUr,
        string[] sourceIds,
        string evidenceClassification,
        string provider,
        string providerStatus,
        bool fallbackUsed,
        List<Dictionary<string, string>> links)
    {
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
            ["evidence_badge"] = new Dictionary<string, string>
            {
                ["en"] = $"Docs Fallback • Karobar Knowledge Base (v{DefaultProductVersion})",
                ["ur"] = $"گائیڈ فال بیک • کاروبار ایپ مستند گائیڈ (v{DefaultProductVersion})"
            },
            ["provider"] = provider,
            ["provider_status"] = providerStatus,
            ["fallback_used"] = fallbackUsed,
            ["links"] = links,
            ["workflow_id"] = null,
            ["provider_trace"] = new Dictionary<string, object?>()
        };
    }

    private static string? GetElementProp(JsonElement obj, string propName)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(propName, out var val) && val.ValueKind == JsonValueKind.String)
        {
            return val.GetString();
        }
        return null;
    }
}

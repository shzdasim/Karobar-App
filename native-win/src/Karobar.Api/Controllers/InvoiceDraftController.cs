using System.Text.Json;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/invoice-drafts")]
public class InvoiceDraftController : ApiControllerBase
{
    public InvoiceDraftController(KarobarDb db) : base(db)
    {
    }

    private async Task<bool> CanAccessDraftsAsync() =>
        await CanAsync("sale-invoice.view", "sale-invoice.create", "sale-invoice.update", "purchase-invoice.view", "purchase-invoice.create", "purchase-invoice.update", "barcode-pos.view", "barcode-pos.create");

    private async Task<Dictionary<string, object?>?> ResolveDraftAsync(string idOrUuid)
    {
        if (!await Db.HasTableAsync("invoice_drafts")) return null;
        var hasDeletedAt = await Db.HasColumnAsync("invoice_drafts", "deleted_at");
        var notDeleted = hasDeletedAt ? " AND deleted_at IS NULL" : "";

        if (long.TryParse(idOrUuid, out var numericId))
        {
            return await Db.QuerySingleOrDefaultAsync($"SELECT * FROM invoice_drafts WHERE id = @id{notDeleted}", new { id = numericId });
        }

        return await Db.QuerySingleOrDefaultAsync($"SELECT * FROM invoice_drafts WHERE uuid = @uuid{notDeleted} LIMIT 1", new { uuid = idOrUuid });
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] long? user_id = null,
        [FromQuery(Name = "shared")] string? sharedRaw = null,
        [FromQuery(Name = "mine_only")] string? mineOnlyRaw = null,
        [FromQuery] string? scope = null,
        [FromQuery] string? type = null,
        [FromQuery] string? status = "draft")
    {
        if (!await CanAccessDraftsAsync())
        {
            return Forbidden();
        }

        if (!await Db.HasTableAsync("invoice_drafts"))
        {
            return Ok(new List<Dictionary<string, object?>>());
        }

        var shared = !string.IsNullOrWhiteSpace(sharedRaw) && (sharedRaw.Equals("1", StringComparison.OrdinalIgnoreCase) || sharedRaw.Equals("true", StringComparison.OrdinalIgnoreCase));
        var mine_only = !string.IsNullOrWhiteSpace(mineOnlyRaw) && (mineOnlyRaw.Equals("1", StringComparison.OrdinalIgnoreCase) || mineOnlyRaw.Equals("true", StringComparison.OrdinalIgnoreCase));

        var currentUid = CurrentUserId ?? 0;
        var hasTerminalHeader = Request.Headers.ContainsKey("X-Terminal-Id") ||
                                (HttpContext.Items.TryGetValue("KarobarTerminal", out var termObj) && termObj != null);
        bool isTerminalContext = hasTerminalHeader || shared || string.Equals(scope, "store", StringComparison.OrdinalIgnoreCase);

        var where = new List<string> { "1=1" };
        if (await Db.HasColumnAsync("invoice_drafts", "deleted_at"))
        {
            where.Add("d.deleted_at IS NULL");
        }
        var dyn = new DynamicParameters();

        if (user_id.HasValue && user_id.Value > 0 && await IsAdminAsync())
        {
            where.Add("d.user_id = @uid");
            dyn.Add("uid", user_id.Value);
        }
        else if (mine_only || string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase) || !isTerminalContext)
        {
            where.Add("d.user_id = @uid");
            dyn.Add("uid", currentUid);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            where.Add("d.type = @type");
            dyn.Add("type", type.Trim());
        }

        var st = string.IsNullOrWhiteSpace(status) ? "draft" : status.Trim();
        if (!string.Equals(st, "all", StringComparison.OrdinalIgnoreCase))
        {
            where.Add("d.status = @status");
            dyn.Add("status", st);
        }

        var whereSql = string.Join(" AND ", where);
        var drafts = await Db.QueryListAsync($@"
            SELECT d.*, u.name as user_name
            FROM invoice_drafts d
            LEFT JOIN users u ON u.id = d.user_id
            WHERE {whereSql}
            ORDER BY d.updated_at DESC, d.id DESC",
            dyn);

        var allProductIds = new HashSet<long>();
        var parsedPayloads = new Dictionary<long, Dictionary<string, object?>>();

        foreach (var d in drafts)
        {
            var did = ToLong(d["id"]);
            var payloadDict = ParsePayloadDict(d.GetValueOrDefault("payload"));
            parsedPayloads[did] = payloadDict;

            var items = ExtractItemsList(payloadDict);
            foreach (var it in items)
            {
                var pid = ToLong(it.GetValueOrDefault("product_id"));
                if (pid > 0) allProductIds.Add(pid);
            }
        }

        var productMap = new Dictionary<long, string>();
        if (allProductIds.Count > 0 && await Db.HasTableAsync("products"))
        {
            var pRows = await Db.QueryListAsync("SELECT id, name FROM products WHERE id IN @ids", new { ids = allProductIds.ToArray() });
            foreach (var pr in pRows)
            {
                productMap[ToLong(pr["id"])] = pr.GetValueOrDefault("name")?.ToString() ?? $"Product #{pr["id"]}";
            }
        }

        var result = new List<Dictionary<string, object?>>();
        foreach (var d in drafts)
        {
            var did = ToLong(d["id"]);
            var payloadDict = parsedPayloads.GetValueOrDefault(did) ?? new Dictionary<string, object?>();
            var items = ExtractItemsList(payloadDict);
            var itemsSummary = new List<Dictionary<string, object?>>();

            foreach (var it in items)
            {
                var pid = ToLong(it.GetValueOrDefault("product_id"));
                if (pid > 0)
                {
                    var qty = (int)ToLong(it.GetValueOrDefault("quantity") ?? it.GetValueOrDefault("unit_quantity"));
                    var name = productMap.TryGetValue(pid, out var pn) ? pn : $"Product #{pid}";
                    itemsSummary.Add(new Dictionary<string, object?>
                    {
                        ["product_id"] = pid,
                        ["name"] = name,
                        ["quantity"] = qty
                    });
                }
            }

            var partyId = payloadDict.TryGetValue("customer_id", out var cidObj) && cidObj != null
                ? cidObj
                : (payloadDict.TryGetValue("supplier_id", out var sidObj) ? sidObj : null);

            result.Add(new Dictionary<string, object?>
            {
                ["id"] = d.GetValueOrDefault("id"),
                ["uuid"] = d.GetValueOrDefault("uuid"),
                ["user_id"] = d.GetValueOrDefault("user_id"),
                ["terminal_id"] = d.GetValueOrDefault("terminal_id"),
                ["terminal_name"] = d.GetValueOrDefault("terminal_name"),
                ["type"] = d.GetValueOrDefault("type"),
                ["title"] = d.GetValueOrDefault("title"),
                ["party_id"] = partyId,
                ["party_name"] = d.GetValueOrDefault("party_name"),
                ["total"] = d.GetValueOrDefault("total_amount"),
                ["total_amount"] = d.GetValueOrDefault("total_amount"),
                ["items_count"] = d.GetValueOrDefault("items_count"),
                ["version"] = d.GetValueOrDefault("version"),
                ["status"] = d.GetValueOrDefault("status"),
                ["posted_invoice_id"] = d.GetValueOrDefault("posted_invoice_id"),
                ["posted_invoice_type"] = d.GetValueOrDefault("posted_invoice_type"),
                ["created_at"] = d.GetValueOrDefault("created_at"),
                ["updated_at"] = d.GetValueOrDefault("updated_at"),
                ["user_name"] = d.GetValueOrDefault("user_name"),
                ["payload"] = payloadDict,
                ["items_summary"] = itemsSummary
            });
        }

        return Ok(result);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Show(string id)
    {
        if (!await CanAccessDraftsAsync()) return Forbidden();
        var draft = await ResolveDraftAsync(id);
        if (draft == null) return NotFound(new { message = "Draft not found." });

        var payload = ParsePayloadDict(draft.GetValueOrDefault("payload"));
        var items = ExtractItemsList(payload);
        var productIds = items.Select(i => ToLong(i.GetValueOrDefault("product_id"))).Where(pid => pid > 0).Distinct().ToArray();

        var products = new List<Dictionary<string, object?>>();
        var productsKeyed = new Dictionary<long, Dictionary<string, object?>>();

        if (productIds.Length > 0 && await Db.HasTableAsync("products"))
        {
            products = await Db.QueryListAsync("SELECT * FROM products WHERE id IN @ids", new { ids = productIds });
            var batches = await Db.HasTableAsync("batches")
                ? await Db.QueryListAsync("SELECT * FROM batches WHERE product_id IN @ids", new { ids = productIds })
                : new List<Dictionary<string, object?>>();

            foreach (var p in products)
            {
                var pid = ToLong(p["id"]);
                p["batches"] = batches.Where(b => ToLong(b.GetValueOrDefault("product_id")) == pid).ToList();
                productsKeyed[pid] = p;
            }
        }

        foreach (var item in items)
        {
            var pid = ToLong(item.GetValueOrDefault("product_id"));
            if (pid > 0 && productsKeyed.TryGetValue(pid, out var prod))
            {
                if (item.GetValueOrDefault("product_name") == null || string.IsNullOrWhiteSpace(item["product_name"]?.ToString()))
                {
                    item["product_name"] = prod.GetValueOrDefault("name");
                }
                if (item.GetValueOrDefault("product") == null)
                {
                    item["product"] = new Dictionary<string, object?>
                    {
                        ["id"] = prod.GetValueOrDefault("id"),
                        ["name"] = prod.GetValueOrDefault("name"),
                        ["unit_sale_price"] = prod.GetValueOrDefault("unit_sale_price"),
                        ["pack_purchase_price"] = prod.GetValueOrDefault("pack_purchase_price"),
                        ["pack_size"] = prod.GetValueOrDefault("pack_size"),
                        ["quantity"] = prod.GetValueOrDefault("quantity"),
                        ["narcotic"] = prod.GetValueOrDefault("narcotic")
                    };
                }
            }
        }

        payload["items"] = items;
        draft["payload"] = payload;

        bool partyValid = true;
        string? partyWarning = null;
        var itemIssues = new Dictionary<string, List<string>>();
        var draftType = (draft.GetValueOrDefault("type")?.ToString() ?? "").ToLowerInvariant();

        if (draftType is "sale_retail" or "sale_wholesale" or "quotation")
        {
            var customerId = ToLong(payload.GetValueOrDefault("customer_id"));
            if (customerId > 0 && await Db.HasTableAsync("customers"))
            {
                var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM customers WHERE id = @id", new { id = customerId }) > 0;
                if (!exists)
                {
                    partyValid = false;
                    partyWarning = "The selected customer no longer exists.";
                }
            }

            for (int idx = 0; idx < items.Count; idx++)
            {
                var item = items[idx];
                var pid = ToLong(item.GetValueOrDefault("product_id"));
                productsKeyed.TryGetValue(pid, out var prod);
                var batchNo = (item.GetValueOrDefault("batch_number") ?? item.GetValueOrDefault("batch"))?.ToString();

                var lineIssues = new List<string>();
                if (prod == null)
                {
                    lineIssues.Add("Product no longer exists in catalogue.");
                }
                else
                {
                    var reqQty = (int)ToLong(item.GetValueOrDefault("quantity"));
                    var availQty = (int)ToLong(prod.GetValueOrDefault("quantity"));
                    var prodBatches = prod.GetValueOrDefault("batches") as List<Dictionary<string, object?>>;
                    var batch = (!string.IsNullOrWhiteSpace(batchNo) && prodBatches != null)
                        ? prodBatches.FirstOrDefault(b => string.Equals(b.GetValueOrDefault("batch_number")?.ToString(), batchNo, StringComparison.Ordinal))
                        : null;

                    if (batch != null)
                    {
                        var batchQty = (int)ToLong(batch.GetValueOrDefault("quantity"));
                        if (reqQty > batchQty)
                        {
                            lineIssues.Add($"Batch only has {batchQty} units available (requested {reqQty}).");
                        }
                    }
                    else if (reqQty > availQty)
                    {
                        lineIssues.Add($"Product only has {availQty} units in stock (requested {reqQty}).");
                    }
                }

                if (lineIssues.Count > 0)
                {
                    itemIssues[idx.ToString()] = lineIssues;
                }
            }
        }
        else if (draftType == "purchase")
        {
            var supplierId = ToLong(payload.GetValueOrDefault("supplier_id"));
            if (supplierId > 0 && await Db.HasTableAsync("suppliers"))
            {
                var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM suppliers WHERE id = @id", new { id = supplierId }) > 0;
                if (!exists)
                {
                    partyValid = false;
                    partyWarning = "The selected supplier no longer exists.";
                }
            }

            for (int idx = 0; idx < items.Count; idx++)
            {
                var pid = ToLong(items[idx].GetValueOrDefault("product_id"));
                if (pid > 0 && !productsKeyed.ContainsKey(pid))
                {
                    itemIssues[idx.ToString()] = new List<string> { "Product no longer exists in catalogue." };
                }
            }
        }

        return Ok(new Dictionary<string, object?>
        {
            ["draft"] = draft,
            ["products"] = products,
            ["annotations"] = new Dictionary<string, object?>
            {
                ["party_valid"] = partyValid,
                ["party_warning"] = partyWarning,
                ["item_issues"] = itemIssues
            }
        });
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAccessDraftsAsync()) return Forbidden();
        if (!CurrentUserId.HasValue) return Unauthorized(new { message = "Unauthenticated" });

        var body = await ReadJsonBodyAsync();
        var uuid = (GetString(body, "uuid") ?? "").Trim();
        if (string.IsNullOrEmpty(uuid)) uuid = Guid.NewGuid().ToString();

        var type = (GetString(body, "type") ?? "").Trim();
        if (type is not ("sale_retail" or "sale_wholesale" or "quotation" or "purchase"))
        {
            return Unprocessable("Invalid draft type.", new Dictionary<string, string[]>
            {
                ["type"] = new[] { "The selected type is invalid." }
            });
        }

        if (!body.TryGetValue("payload", out var payloadEl) || payloadEl.ValueKind != JsonValueKind.Object)
        {
            return Unprocessable("Payload is required.", new Dictionary<string, string[]>
            {
                ["payload"] = new[] { "The payload field is required." }
            });
        }

        var payloadDict = ParsePayloadDict(payloadEl.GetRawText());
        var items = ExtractItemsList(payloadDict).Where(i => ToLong(i.GetValueOrDefault("product_id")) > 0).ToList();
        if (items.Count == 0)
        {
            return Unprocessable("Cannot hold invoice with zero products selected. Please add at least one product.", new Dictionary<string, string[]>
            {
                ["items"] = new[] { "Cannot hold invoice with zero products selected. Please add at least one product." }
            });
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM invoice_drafts WHERE uuid = @uuid LIMIT 1", new { uuid });
        if (existing != null)
        {
            return await ExecuteUpdateAsync(existing, body, payloadDict, items);
        }

        var partyName = await ExtractPartyNameAsync(type, payloadDict);
        var totalAmount = ExtractTotalAmount(type, payloadDict);
        var itemsCount = items.Count;
        var title = GetString(body, "title")?.Trim();
        if (string.IsNullOrEmpty(title))
        {
            title = GenerateDefaultTitle(type, partyName, itemsCount);
        }

        long? terminalId = null;
        string? terminalName = Request.Headers["X-Terminal-Name"].FirstOrDefault()?.Trim();
        if (HttpContext.Items.TryGetValue("KarobarTerminal", out var termObj) && termObj is Dictionary<string, object?> termDict)
        {
            terminalId = ToLong(termDict.GetValueOrDefault("id")) > 0 ? ToLong(termDict.GetValueOrDefault("id")) : null;
            if (string.IsNullOrEmpty(terminalName))
            {
                terminalName = termDict.GetValueOrDefault("name")?.ToString();
            }
        }

        var draftId = await Db.InsertAsync("invoice_drafts", new Dictionary<string, object?>
        {
            ["uuid"] = uuid,
            ["user_id"] = CurrentUserId.Value,
            ["terminal_id"] = terminalId,
            ["terminal_name"] = string.IsNullOrWhiteSpace(terminalName) ? null : terminalName,
            ["type"] = type,
            ["title"] = title,
            ["party_name"] = partyName,
            ["total_amount"] = totalAmount,
            ["items_count"] = itemsCount,
            ["version"] = 1,
            ["payload"] = JsonSerializer.Serialize(payloadDict),
            ["status"] = "draft"
        });

        SyncPulseService.Bump("drafts");
        var created = await Db.QuerySingleOrDefaultAsync("SELECT * FROM invoice_drafts WHERE id = @id", new { id = draftId });
        if (created != null) created["payload"] = payloadDict;
        return StatusCode(201, created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id)
    {
        if (!await CanAccessDraftsAsync()) return Forbidden();
        var draft = await ResolveDraftAsync(id);
        if (draft == null) return NotFound(new { message = "Draft not found." });

        var body = await ReadJsonBodyAsync();
        if (!body.TryGetValue("payload", out var payloadEl) || payloadEl.ValueKind != JsonValueKind.Object)
        {
            return Unprocessable("Payload is required.", new Dictionary<string, string[]>
            {
                ["payload"] = new[] { "The payload field is required." }
            });
        }

        var payloadDict = ParsePayloadDict(payloadEl.GetRawText());
        var items = ExtractItemsList(payloadDict).Where(i => ToLong(i.GetValueOrDefault("product_id")) > 0).ToList();
        if (items.Count == 0)
        {
            return Unprocessable("Cannot hold invoice with zero products selected. Please add at least one product.", new Dictionary<string, string[]>
            {
                ["items"] = new[] { "Cannot hold invoice with zero products selected. Please add at least one product." }
            });
        }

        return await ExecuteUpdateAsync(draft, body, payloadDict, items);
    }

    private async Task<IActionResult> ExecuteUpdateAsync(
        Dictionary<string, object?> draft,
        Dictionary<string, JsonElement> body,
        Dictionary<string, object?> payloadDict,
        List<Dictionary<string, object?>> validItems)
    {
        var draftId = ToLong(draft["id"]);
        var status = draft.GetValueOrDefault("status")?.ToString() ?? "draft";
        if (!string.Equals(status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            return Unprocessable($"Draft cannot be updated because its status is {status}.");
        }

        var currentVersion = (int)ToLong(draft.GetValueOrDefault("version") ?? 1);
        if (body.ContainsKey("expected_version"))
        {
            var expectedVersion = GetInt(body, "expected_version", currentVersion);
            if (currentVersion != expectedVersion)
            {
                return StatusCode(409, new Dictionary<string, object?>
                {
                    ["message"] = "Draft conflict: this draft has been modified in another tab or session.",
                    ["current_version"] = currentVersion,
                    ["draft"] = draft
                });
            }
        }

        var type = draft.GetValueOrDefault("type")?.ToString() ?? "sale_retail";
        var partyName = await ExtractPartyNameAsync(type, payloadDict);
        var totalAmount = ExtractTotalAmount(type, payloadDict);
        var itemsCount = validItems.Count;

        var title = body.ContainsKey("title") && GetString(body, "title") != null
            ? GetString(body, "title")
            : draft.GetValueOrDefault("title")?.ToString();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = GenerateDefaultTitle(type, partyName, itemsCount);
        }

        await Db.UpdateAsync("invoice_drafts", draftId, new Dictionary<string, object?>
        {
            ["title"] = title,
            ["party_name"] = partyName,
            ["total_amount"] = totalAmount,
            ["items_count"] = itemsCount,
            ["version"] = currentVersion + 1,
            ["payload"] = JsonSerializer.Serialize(payloadDict)
        });

        SyncPulseService.Bump("drafts");
        var updated = await Db.QuerySingleOrDefaultAsync("SELECT * FROM invoice_drafts WHERE id = @id", new { id = draftId });
        if (updated != null) updated["payload"] = payloadDict;
        return Ok(updated);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Destroy(string id)
    {
        if (!await CanAccessDraftsAsync()) return Forbidden();
        var draft = await ResolveDraftAsync(id);
        if (draft == null) return NotFound(new { message = "Draft not found." });

        var draftId = ToLong(draft["id"]);
        await Db.ExecuteAsync("DELETE FROM invoice_drafts WHERE id = @id", new { id = draftId });
        SyncPulseService.Bump("drafts");

        return Ok(new Dictionary<string, object?>
        {
            ["message"] = "Draft discarded successfully"
        });
    }

    [HttpPost("{id}/post")]
    public async Task<IActionResult> Post(string id)
    {
        if (!await CanAccessDraftsAsync()) return Forbidden();
        var draft = await ResolveDraftAsync(id);
        if (draft == null) return NotFound(new { message = "Draft not found." });

        var draftId = ToLong(draft["id"]);
        var status = draft.GetValueOrDefault("status")?.ToString() ?? "draft";
        if (string.Equals(status, "posted", StringComparison.OrdinalIgnoreCase))
        {
            var postedType = draft.GetValueOrDefault("posted_invoice_type")?.ToString() ?? "";
            var postedId = ToLong(draft.GetValueOrDefault("posted_invoice_id"));
            string? postedNumber = null;

            if (postedId > 0)
            {
                if (postedType.Contains("SaleInvoice", StringComparison.OrdinalIgnoreCase))
                {
                    postedNumber = await Db.ExecuteScalarAsync<string?>("SELECT posted_number FROM sale_invoices WHERE id = @pid", new { pid = postedId });
                }
                else if (postedType.Contains("PurchaseInvoice", StringComparison.OrdinalIgnoreCase))
                {
                    postedNumber = await Db.ExecuteScalarAsync<string?>("SELECT posted_number FROM purchase_invoices WHERE id = @pid", new { pid = postedId });
                }
            }

            return Ok(new Dictionary<string, object?>
            {
                ["message"] = "Invoice already posted",
                ["id"] = postedId,
                ["posted_number"] = postedNumber,
                ["already_posted"] = true
            });
        }

        if (!string.Equals(status, "draft", StringComparison.OrdinalIgnoreCase))
        {
            return Unprocessable($"Draft cannot be posted because its status is {status}.");
        }

        var payloadDict = ParsePayloadDict(draft.GetValueOrDefault("payload"));
        var items = ExtractItemsList(payloadDict);
        payloadDict["draft_id"] = draftId;

        var body = await ReadJsonBodyAsync();
        var rawPin = GetString(body, "operator_pin") ?? Request.Headers["X-Operator-Pin"].FirstOrDefault();

        var type = (draft.GetValueOrDefault("type")?.ToString() ?? "sale_retail").ToLowerInvariant();
        if (type is "sale_retail" or "sale_wholesale" or "quotation")
        {
            if (!payloadDict.ContainsKey("date") || payloadDict["date"] == null || string.IsNullOrWhiteSpace(payloadDict["date"]?.ToString()))
            {
                payloadDict["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd");
            }
            if (!payloadDict.ContainsKey("sale_type") || payloadDict["sale_type"] == null || string.IsNullOrWhiteSpace(payloadDict["sale_type"]?.ToString()))
            {
                payloadDict["sale_type"] = type == "sale_wholesale" ? "wholesale" : "retail";
            }

            var saleController = new SaleInvoiceController(Db)
            {
                ControllerContext = ControllerContext
            };
            return await saleController.ExecuteStoreSaleInvoiceAsync(payloadDict, items, null, rawPin);
        }
        else
        {
            var purchaseController = new PurchaseInvoiceController(Db)
            {
                ControllerContext = ControllerContext
            };
            return await purchaseController.ExecuteStorePurchaseInvoiceAsync(payloadDict, items, rawPin);
        }
    }

    private async Task<string?> ExtractPartyNameAsync(string type, Dictionary<string, object?> payload)
    {
        if (type is "sale_retail" or "sale_wholesale" or "quotation")
        {
            var cid = ToLong(payload.GetValueOrDefault("customer_id"));
            if (cid > 0 && await Db.HasTableAsync("customers"))
            {
                return await Db.ExecuteScalarAsync<string?>("SELECT name FROM customers WHERE id = @id", new { id = cid }) ?? "Walk-in Customer";
            }
            return "Walk-in Customer";
        }

        var sid = ToLong(payload.GetValueOrDefault("supplier_id"));
        if (sid > 0 && await Db.HasTableAsync("suppliers"))
        {
            return await Db.ExecuteScalarAsync<string?>("SELECT name FROM suppliers WHERE id = @id", new { id = sid });
        }
        return null;
    }

    private static double ExtractTotalAmount(string type, Dictionary<string, object?> payload)
    {
        if (type is "sale_retail" or "sale_wholesale" or "quotation")
        {
            return ToDouble(payload.GetValueOrDefault("total"));
        }
        return ToDouble(payload.GetValueOrDefault("total_amount"));
    }

    private static string GenerateDefaultTitle(string type, string? party, int itemsCount)
    {
        var prefix = type switch
        {
            "sale_retail" => "Retail Sale",
            "sale_wholesale" => "Wholesale Sale",
            "quotation" => "Price Quotation",
            "purchase" => "Purchase Invoice",
            _ => "Draft"
        };
        var partyStr = !string.IsNullOrWhiteSpace(party) ? $" - {party}" : "";
        return $"{prefix}{partyStr} ({itemsCount} items)";
    }

    private static Dictionary<string, object?> ParsePayloadDict(object? rawPayload)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (rawPayload == null) return dict;

        try
        {
            JsonElement root;
            if (rawPayload is JsonElement je)
            {
                root = je;
            }
            else if (rawPayload is string str && !string.IsNullOrWhiteSpace(str))
            {
                using var doc = JsonDocument.Parse(str);
                root = doc.RootElement.Clone();
            }
            else
            {
                var serialized = JsonSerializer.Serialize(rawPayload);
                using var doc = JsonDocument.Parse(serialized);
                root = doc.RootElement.Clone();
            }

            if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "items", StringComparison.OrdinalIgnoreCase) && prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        var itemsList = new List<Dictionary<string, object?>>();
                        foreach (var itemEl in prop.Value.EnumerateArray())
                        {
                            if (itemEl.ValueKind == JsonValueKind.Object)
                            {
                                var itemDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                                foreach (var ip in itemEl.EnumerateObject())
                                {
                                    itemDict[ip.Name] = KarobarDb.PrepareDbValue(ip.Value);
                                }
                                itemsList.Add(itemDict);
                            }
                        }
                        dict["items"] = itemsList;
                    }
                    else
                    {
                        dict[prop.Name] = KarobarDb.PrepareDbValue(prop.Value);
                    }
                }
            }
        }
        catch
        {
        }

        return dict;
    }

    private static List<Dictionary<string, object?>> ExtractItemsList(Dictionary<string, object?> payloadDict)
    {
        if (payloadDict.TryGetValue("items", out var itemsObj) && itemsObj is List<Dictionary<string, object?>> list)
        {
            return list;
        }
        return new List<Dictionary<string, object?>>();
    }

    private static long ToLong(object? val)
    {
        if (val == null) return 0;
        if (val is long l) return l;
        if (val is int i) return i;
        return long.TryParse(val.ToString(), out var p) ? p : 0;
    }

    private static double ToDouble(object? val)
    {
        if (val == null) return 0.0;
        if (val is double d) return d;
        if (val is decimal dec) return (double)dec;
        if (val is float f) return f;
        if (val is long l) return l;
        if (val is int i) return i;
        return double.TryParse(val.ToString(), out var p) ? p : 0.0;
    }
}

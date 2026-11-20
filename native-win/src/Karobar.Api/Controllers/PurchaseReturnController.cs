using System.Data;
using System.Text.Json;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/purchase-returns")]
[Authorize]
public class PurchaseReturnController : ApiControllerBase
{
    private readonly DocumentSequenceService _sequenceService;
    private readonly InvoiceCalculationService _calculationService;
    private readonly ReturnEntitlementService _entitlementService;
    private readonly ReturnSettlementService _settlementService;
    private readonly OperatorPinService _operatorPinService;

    public PurchaseReturnController(KarobarDb db) : base(db)
    {
        _sequenceService = new DocumentSequenceService(db);
        _calculationService = new InvoiceCalculationService(db);
        _entitlementService = new ReturnEntitlementService(db);
        _settlementService = new ReturnSettlementService(db);
        _operatorPinService = new OperatorPinService(db);
    }

    [HttpGet("new-code")]
    public async Task<IActionResult> GenerateNewCode()
    {
        if (!await CanAsync("purchase-return.create"))
        {
            return Forbidden();
        }

        var code = await _sequenceService.PreviewAsync(
            "purchase_return",
            "PRRET-",
            4,
            "purchase_returns",
            "posted_number",
            true);

        return Ok(new Dictionary<string, object?> { ["posted_number"] = code });
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? posted = null,
        [FromQuery] string? supplier = null,
        [FromQuery] int page = 1,
        [FromQuery] int? per_page = null)
    {
        if (!await CanAsync("purchase-return.view"))
        {
            return Forbidden();
        }

        var where = new List<string> { "1=1" };
        var dyn = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(posted))
        {
            where.Add("pr.posted_number LIKE @posted");
            dyn.Add("posted", "%" + posted.Trim() + "%");
        }
        if (!string.IsNullOrWhiteSpace(supplier))
        {
            where.Add("EXISTS (SELECT 1 FROM suppliers s WHERE s.id = pr.supplier_id AND s.name LIKE @supplier)");
            dyn.Add("supplier", "%" + supplier.Trim() + "%");
        }

        var whereSql = string.Join(" AND ", where);
        var selectSql = $"SELECT pr.* FROM purchase_returns pr WHERE {whereSql} ORDER BY pr.id DESC";
        var countSql = $"SELECT COUNT(1) FROM purchase_returns pr WHERE {whereSql}";

        if (per_page.HasValue && per_page.Value > 0)
        {
            var clampedPerPage = Math.Max(1, Math.Min(per_page.Value, 200));
            var paged = await Db.PaginateAsync(selectSql, countSql, dyn, page, clampedPerPage, EnrichReturnBatchAsync);
            return Ok(paged);
        }

        var allRows = await Db.QueryListAsync(selectSql, dyn);
        await EnrichReturnBatchAsync(allRows);
        return Ok(allRows);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("purchase-return.view"))
        {
            return Forbidden();
        }

        var loaded = await LoadPurchaseReturnAsync(id);
        if (loaded == null) return NotFound(new { message = "Purchase return not found." });

        return Ok(loaded);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("purchase-return.create"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var (rawData, rawItems) = ParsePayloadAndItems(body);

        var supplierId = ToLong(rawData.GetValueOrDefault("supplier_id"));
        var date = rawData.GetValueOrDefault("date")?.ToString() ?? "";
        if (supplierId <= 0)
        {
            return Unprocessable("Supplier is required.", new Dictionary<string, string[]> { ["supplier_id"] = new[] { "The supplier id field is required." } });
        }
        if (string.IsNullOrWhiteSpace(date))
        {
            return Unprocessable("Date is required.", new Dictionary<string, string[]> { ["date"] = new[] { "The date field is required." } });
        }

        try
        {
            var calculated = _calculationService.CalculateAndValidatePurchaseReturn(rawData, rawItems);
            var calcItems = (List<Dictionary<string, object?>>)calculated["items"]!;

            var rawPin = GetString(body, "operator_pin") ?? Request.Headers["X-Operator-Pin"].FirstOrDefault();
            var op = await _operatorPinService.ResolveOperatorAsync(rawPin, CurrentUserId, "purchase_return");
            var operatorUserId = ToNullableLong(op.GetValueOrDefault("user_id")) ?? CurrentUserId;
            var operatorEmpId = ToNullableLong(op.GetValueOrDefault("employee_id"));

            var postedNumber = await _sequenceService.NextAsync(
                "purchase_return",
                "PRRET-",
                4,
                "purchase_returns",
                "posted_number",
                true);

            await using var conn = await Db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                await ValidateStockAvailabilityAsync(calcItems, null, conn, tx);
                await _entitlementService.ValidateAsync("purchase", rawData, calcItems, null, conn, tx);

                var createPayload = new Dictionary<string, object?>
                {
                    ["supplier_id"] = supplierId,
                    ["posted_number"] = postedNumber,
                    ["date"] = date,
                    ["purchase_invoice_id"] = ToNullableLong(rawData.GetValueOrDefault("purchase_invoice_id")),
                    ["remarks"] = rawData.GetValueOrDefault("remarks"),
                    ["gross_total"] = calculated["gross_total"],
                    ["discount_percentage"] = calculated["discount_percentage"],
                    ["discount_amount"] = calculated["discount_amount"],
                    ["tax_percentage"] = calculated["tax_percentage"],
                    ["tax_amount"] = calculated["tax_amount"],
                    ["total"] = calculated["total"],
                    ["user_id"] = operatorUserId,
                    ["employee_id"] = operatorEmpId
                };

                var prId = await Db.InsertAsync("purchase_returns", createPayload, conn, tx);
                await CreateItemsAndReduceAsync(prId, calcItems, conn, tx);

                var prRow = (await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_returns WHERE id = @id", new { id = prId }, conn, tx))!;
                prRow["settlement_mode"] = await _settlementService.ResolvePurchaseSettlementModeAsync(
                    rawData.GetValueOrDefault("settlement_mode")?.ToString(),
                    ToNullableLong(rawData.GetValueOrDefault("purchase_invoice_id")),
                    null,
                    conn,
                    tx);
                prRow["payment_method"] = rawData.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
                prRow["account_id"] = ToNullableLong(rawData.GetValueOrDefault("account_id"));

                await _settlementService.ApplyPurchaseReturnSettlementAsync(prRow, operatorUserId, conn, tx, CurrentTerminalId);

                await tx.CommitAsync();
                SyncPulseService.Bump("stock", "drawer");

                var loaded = (await LoadPurchaseReturnAsync(prId))!;
                loaded["operator_name"] = op.GetValueOrDefault("operator_name");
                return StatusCode(201, loaded);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (OperatorPinException pinEx)
        {
            return StatusCode(pinEx.StatusCode, pinEx.ResponsePayload);
        }
        catch (InvoiceValidationException valEx)
        {
            return StatusCode(valEx.StatusCode, valEx.ResponsePayload);
        }
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("purchase-return.update"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_returns WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Purchase return not found." });

        var body = await ReadJsonBodyAsync();
        var (rawData, rawItems) = ParsePayloadAndItems(body);

        try
        {
            var calculated = _calculationService.CalculateAndValidatePurchaseReturn(rawData, rawItems);
            var calcItems = (List<Dictionary<string, object?>>)calculated["items"]!;

            var rawPin = GetString(body, "operator_pin") ?? Request.Headers["X-Operator-Pin"].FirstOrDefault();
            var op = await _operatorPinService.ResolveOperatorAsync(rawPin, CurrentUserId, "purchase_return");
            var operatorUserId = ToNullableLong(op.GetValueOrDefault("user_id")) ?? CurrentUserId;
            var operatorEmpId = ToNullableLong(op.GetValueOrDefault("employee_id"));
            var verifiedViaPin = op.GetValueOrDefault("verified_via_pin") is true;

            await using var conn = await Db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                var oldItems = await Db.QueryListAsync("SELECT * FROM purchase_return_items WHERE purchase_return_id = @id", new { id }, conn, tx);
                await ValidateStockAvailabilityAsync(calcItems, oldItems, conn, tx);

                var oldSnapshot = new Dictionary<string, object?>
                {
                    ["total"] = ToDouble(existing.GetValueOrDefault("total")),
                    ["settlement_mode"] = existing.GetValueOrDefault("settlement_mode")?.ToString() ?? "cash_refund",
                    ["payment_method"] = existing.GetValueOrDefault("payment_method")?.ToString() ?? "cash",
                    ["account_id"] = existing.GetValueOrDefault("account_id")
                };

                await _entitlementService.ValidateAsync("purchase", rawData, calcItems, existing, conn, tx);

                // Revert previous stock effects
                await RevertItemsAsync(oldItems, conn, tx);
                await Db.ExecuteAsync("DELETE FROM purchase_return_items WHERE purchase_return_id = @id", new { id }, conn, tx);

                var parsedSupplierId = ToLong(rawData.GetValueOrDefault("supplier_id"));
                if (parsedSupplierId <= 0) parsedSupplierId = ToLong(existing.GetValueOrDefault("supplier_id"));
                var parsedDate = (rawData.GetValueOrDefault("date")?.ToString() ?? "").Trim();
                if (string.IsNullOrEmpty(parsedDate)) parsedDate = existing.GetValueOrDefault("date")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd");

                var updatePayload = new Dictionary<string, object?>
                {
                    ["supplier_id"] = parsedSupplierId,
                    ["posted_number"] = rawData.GetValueOrDefault("posted_number") ?? existing.GetValueOrDefault("posted_number"),
                    ["date"] = parsedDate,
                    ["purchase_invoice_id"] = ToNullableLong(rawData.GetValueOrDefault("purchase_invoice_id")),
                    ["remarks"] = rawData.GetValueOrDefault("remarks"),
                    ["gross_total"] = calculated["gross_total"],
                    ["discount_percentage"] = calculated["discount_percentage"],
                    ["discount_amount"] = calculated["discount_amount"],
                    ["tax_percentage"] = calculated["tax_percentage"],
                    ["tax_amount"] = calculated["tax_amount"],
                    ["total"] = calculated["total"]
                };

                if (verifiedViaPin)
                {
                    updatePayload["user_id"] = operatorUserId;
                    updatePayload["employee_id"] = operatorEmpId;
                }

                await Db.UpdateAsync("purchase_returns", id, updatePayload, "id", conn, tx);
                await CreateItemsAndReduceAsync(id, calcItems, conn, tx);

                var prRow = (await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_returns WHERE id = @id", new { id }, conn, tx))!;
                prRow["settlement_mode"] = await _settlementService.ResolvePurchaseSettlementModeAsync(
                    rawData.GetValueOrDefault("settlement_mode")?.ToString(),
                    ToNullableLong(rawData.GetValueOrDefault("purchase_invoice_id")),
                    oldSnapshot["settlement_mode"]?.ToString(),
                    conn,
                    tx);
                prRow["payment_method"] = rawData.GetValueOrDefault("payment_method")?.ToString() ?? (oldSnapshot["payment_method"]?.ToString() ?? "cash");
                if (rawData.ContainsKey("account_id"))
                {
                    prRow["account_id"] = ToNullableLong(rawData["account_id"]);
                }

                bool? updateDrawerFlag = null;
                if (body.ContainsKey("update_drawer")) updateDrawerFlag = GetBool(body, "update_drawer");
                else if (body.ContainsKey("update_drawer_if_pre_shift")) updateDrawerFlag = GetBool(body, "update_drawer_if_pre_shift");

                await _settlementService.SyncPurchaseReturnSettlementUpdateAsync(prRow, oldSnapshot, updateDrawerFlag, operatorUserId, conn, tx, CurrentTerminalId);

                await tx.CommitAsync();
                SyncPulseService.Bump("stock", "drawer");

                var loaded = (await LoadPurchaseReturnAsync(id))!;
                loaded["operator_name"] = op.GetValueOrDefault("operator_name");
                return Ok(loaded);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        catch (OperatorPinException pinEx)
        {
            return StatusCode(pinEx.StatusCode, pinEx.ResponsePayload);
        }
        catch (InvoiceValidationException valEx)
        {
            return StatusCode(valEx.StatusCode, valEx.ResponsePayload);
        }
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("purchase-return.delete"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_returns WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Purchase return not found." });

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            await _settlementService.RevertPurchaseReturnSettlementAsync(existing, CurrentUserId, conn, tx, CurrentTerminalId);

            var oldItems = await Db.QueryListAsync("SELECT * FROM purchase_return_items WHERE purchase_return_id = @id", new { id }, conn, tx);
            await RevertItemsAsync(oldItems, conn, tx);

            await Db.ExecuteAsync("DELETE FROM purchase_return_items WHERE purchase_return_id = @id", new { id }, conn, tx);
            await Db.ExecuteAsync("DELETE FROM purchase_returns WHERE id = @id", new { id }, conn, tx);

            await tx.CommitAsync();
            SyncPulseService.Bump("stock", "drawer");

            return NoContent();
        }
        catch (InvoiceValidationException valEx)
        {
            await tx.RollbackAsync();
            return StatusCode(valEx.StatusCode, valEx.ResponsePayload);
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private async Task ValidateStockAvailabilityAsync(
        List<Dictionary<string, object?>> items,
        List<Dictionary<string, object?>>? originalItems,
        IDbConnection conn,
        IDbTransaction tx)
    {
        originalItems ??= new List<Dictionary<string, object?>>();

        var ids = items.Select(i => ToLong(i.GetValueOrDefault("product_id")))
            .Concat(originalItems.Select(o => ToLong(o.GetValueOrDefault("product_id"))))
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0) return;

        var pRows = await Db.QueryListAsync("SELECT * FROM products WHERE id IN @ids", new { ids }, conn, tx);
        var products = pRows.ToDictionary(p => ToLong(p["id"]), p => p);

        var batches = await Db.HasTableAsync("batches")
            ? await Db.QueryListAsync("SELECT * FROM batches WHERE product_id IN @ids", new { ids }, conn, tx)
            : new List<Dictionary<string, object?>>();

        var restored = new Dictionary<long, int>();
        var batchRestored = new Dictionary<long, int>();
        var unbatchedRestored = new Dictionary<long, int>();

        foreach (var old in originalItems)
        {
            var oldPid = ToLong(old.GetValueOrDefault("product_id"));
            var retUnits = (int)ToLong(old.GetValueOrDefault("return_unit_quantity"));
            restored[oldPid] = restored.GetValueOrDefault(oldPid, 0) + retUnits;

            var batchNo = (old.GetValueOrDefault("batch")?.ToString() ?? "").Trim();
            if (!string.IsNullOrEmpty(batchNo))
            {
                var b = batches.FirstOrDefault(x => ToLong(x.GetValueOrDefault("product_id")) == oldPid &&
                                                    string.Equals((x.GetValueOrDefault("batch_number")?.ToString() ?? "").Trim(), batchNo, StringComparison.Ordinal));
                if (b != null)
                {
                    var bid = ToLong(b["id"]);
                    batchRestored[bid] = batchRestored.GetValueOrDefault(bid, 0) + retUnits;
                }
            }
            else
            {
                unbatchedRestored[oldPid] = unbatchedRestored.GetValueOrDefault(oldPid, 0) + retUnits;
            }
        }

        var requested = new Dictionary<long, int>();
        var batchRequested = new Dictionary<long, int>();
        var unbatchedRequested = new Dictionary<long, int>();

        for (int index = 0; index < items.Count; index++)
        {
            var row = items[index];
            var pid = ToLong(row.GetValueOrDefault("product_id"));
            if (!products.TryGetValue(pid, out var product))
            {
                throw new InvoiceValidationException("Selected product is invalid or unavailable.", new Dictionary<string, string[]>
                {
                    [$"items.{index}.product_id"] = new[] { "Selected product is invalid or unavailable." }
                });
            }

            var returnQty = (int)ToLong(row.GetValueOrDefault("return_unit_quantity"));
            if (returnQty <= 0) continue;

            var prodName = product.GetValueOrDefault("name")?.ToString() ?? $"Product #{pid}";
            requested[pid] = requested.GetValueOrDefault(pid, 0) + returnQty;
            var available = (int)ToLong(product.GetValueOrDefault("quantity")) + restored.GetValueOrDefault(pid, 0);

            if (requested[pid] > available)
            {
                var msg = $"Only {available} units remain available on hand for {prodName}. Insufficient stock (requested: {requested[pid]} units).";
                throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                {
                    [$"items.{index}.return_unit_quantity"] = new[] { msg }
                });
            }

            var batchNo = (row.GetValueOrDefault("batch")?.ToString() ?? "").Trim();
            if (!string.IsNullOrEmpty(batchNo))
            {
                var b = batches.FirstOrDefault(x => ToLong(x.GetValueOrDefault("product_id")) == pid &&
                                                    string.Equals((x.GetValueOrDefault("batch_number")?.ToString() ?? "").Trim(), batchNo, StringComparison.Ordinal));
                if (b != null)
                {
                    var bid = ToLong(b["id"]);
                    batchRequested[bid] = batchRequested.GetValueOrDefault(bid, 0) + returnQty;
                    var batchAvailable = (int)ToLong(b.GetValueOrDefault("quantity")) + batchRestored.GetValueOrDefault(bid, 0);
                    if (batchRequested[bid] > batchAvailable)
                    {
                        var bNum = b.GetValueOrDefault("batch_number")?.ToString() ?? batchNo;
                        var msg = $"Only {batchAvailable} units remain available in batch {bNum} for {prodName}. Insufficient stock (requested: {batchRequested[bid]} units).";
                        throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                        {
                            [$"items.{index}.return_unit_quantity"] = new[] { msg }
                        });
                    }
                }
            }
            else
            {
                var productBatches = batches.Where(x => ToLong(x.GetValueOrDefault("product_id")) == pid).ToList();
                if (productBatches.Count > 0)
                {
                    var totalBatched = productBatches.Sum(x => (int)ToLong(x.GetValueOrDefault("quantity")));
                    var unbatchedAvailable = Math.Max(0, (int)ToLong(product.GetValueOrDefault("quantity")) - totalBatched) + unbatchedRestored.GetValueOrDefault(pid, 0);
                    unbatchedRequested[pid] = unbatchedRequested.GetValueOrDefault(pid, 0) + returnQty;
                    if (unbatchedRequested[pid] > unbatchedAvailable)
                    {
                        var msg = $"Only {unbatchedAvailable} unassigned units remain available on hand for {prodName}. Insufficient unassigned stock (requested: {unbatchedRequested[pid]} units).";
                        throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                        {
                            [$"items.{index}.return_unit_quantity"] = new[] { msg }
                        });
                    }
                }
            }
        }
    }

    private async Task CreateItemsAndReduceAsync(
        long prId,
        List<Dictionary<string, object?>> items,
        IDbConnection conn,
        IDbTransaction tx)
    {
        foreach (var raw in items)
        {
            var pid = ToLong(raw.GetValueOrDefault("product_id"));
            var batch = raw.GetValueOrDefault("batch")?.ToString();
            var expiry = raw.GetValueOrDefault("expiry")?.ToString();
            var units = (int)ToLong(raw.GetValueOrDefault("return_unit_quantity"));

            await Db.InsertAsync("purchase_return_items", new Dictionary<string, object?>
            {
                ["purchase_return_id"] = prId,
                ["product_id"] = pid,
                ["batch"] = string.IsNullOrWhiteSpace(batch) ? null : batch,
                ["expiry"] = string.IsNullOrWhiteSpace(expiry) ? null : expiry,
                ["pack_size"] = (int)ToLong(raw.GetValueOrDefault("pack_size")),
                ["pack_purchased_quantity"] = (int)ToLong(raw.GetValueOrDefault("pack_purchased_quantity")),
                ["return_pack_quantity"] = ToDouble(raw.GetValueOrDefault("return_pack_quantity")),
                ["return_unit_quantity"] = units,
                ["quantity"] = units,
                ["pack_purchase_price"] = ToDouble(raw.GetValueOrDefault("pack_purchase_price")),
                ["unit_purchase_price"] = ToDouble(raw.GetValueOrDefault("unit_purchase_price")),
                ["item_discount_percentage"] = ToDouble(raw.GetValueOrDefault("item_discount_percentage")),
                ["sub_total"] = ToDouble(raw.GetValueOrDefault("sub_total"))
            }, conn, tx);

            await ApplyStockDeltaSmartAsync(pid, batch, expiry, -units, conn, tx);
        }
    }

    private async Task RevertItemsAsync(
        List<Dictionary<string, object?>> items,
        IDbConnection conn,
        IDbTransaction tx)
    {
        foreach (var item in items)
        {
            var pid = ToLong(item.GetValueOrDefault("product_id"));
            var batch = item.GetValueOrDefault("batch")?.ToString();
            var expiry = item.GetValueOrDefault("expiry")?.ToString();
            var units = (int)ToLong(item.GetValueOrDefault("return_unit_quantity"));

            await ApplyStockDeltaSmartAsync(pid, batch, expiry, +units, conn, tx);
        }
    }

    private async Task ApplyStockDeltaSmartAsync(
        long productId,
        string? batchNo,
        string? expiry,
        int deltaUnits,
        IDbConnection conn,
        IDbTransaction tx)
    {
        var bTrim = (batchNo ?? "").Trim();
        var eTrim = (expiry ?? "").Trim();
        int actualApplied = deltaUnits;

        Dictionary<string, object?>? batch = null;
        if (!string.IsNullOrEmpty(bTrim) && await Db.HasTableAsync("batches"))
        {
            if (!string.IsNullOrEmpty(eTrim))
            {
                var cleanExp = eTrim.Length > 10 ? eTrim.Substring(0, 10) : eTrim;
                batch = await Db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM batches WHERE product_id = @pid AND batch_number = @bn AND (expiry_date = @cleanExp OR expiry_date = @eTrim) LIMIT 1",
                    new { pid = productId, bn = bTrim, cleanExp, eTrim },
                    conn,
                    tx);
            }
            batch ??= await Db.QuerySingleOrDefaultAsync(
                "SELECT * FROM batches WHERE product_id = @pid AND batch_number = @bn LIMIT 1",
                new { pid = productId, bn = bTrim },
                conn,
                tx);
        }

        if (batch != null)
        {
            var before = (int)ToLong(batch.GetValueOrDefault("quantity"));
            var after = before + deltaUnits;
            if (after < 0)
            {
                throw new InvoiceValidationException($"Insufficient stock in batch {bTrim}.", new Dictionary<string, string[]>
                {
                    ["items"] = new[] { $"Insufficient stock in batch {bTrim}." }
                });
            }
            actualApplied = after - before;
            await Db.UpdateAsync("batches", batch["id"]!, new Dictionary<string, object?> { ["quantity"] = after }, "id", conn, tx);
        }

        var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @pid", new { pid = productId }, conn, tx);
        if (product != null)
        {
            var pBefore = (int)ToLong(product.GetValueOrDefault("quantity"));
            var pAfter = pBefore + actualApplied;
            if (pAfter < 0)
            {
                var pName = product.GetValueOrDefault("name")?.ToString() ?? $"Product #{productId}";
                throw new InvoiceValidationException($"Insufficient stock for {pName}.", new Dictionary<string, string[]>
                {
                    ["items"] = new[] { $"Insufficient stock for {pName}." }
                });
            }

            var prodUpdates = new Dictionary<string, object?> { ["quantity"] = pAfter };
            if (pAfter <= 0)
            {
                prodUpdates["avg_price"] = 0;
                prodUpdates["margin"] = 0;
                prodUpdates["whole_sale_margin"] = 0;
            }
            await Db.UpdateAsync("products", productId, prodUpdates, "id", conn, tx);
        }
    }

    private async Task<Dictionary<string, object?>?> LoadPurchaseReturnAsync(long id)
    {
        var pr = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_returns WHERE id = @id", new { id });
        if (pr == null) return null;

        await EnrichReturnBatchAsync(new List<Dictionary<string, object?>> { pr });

        var items = await Db.QueryListAsync("SELECT * FROM purchase_return_items WHERE purchase_return_id = @id", new { id });
        var prodIds = items.Select(i => ToLong(i.GetValueOrDefault("product_id"))).Where(pid => pid > 0).Distinct().ToArray();

        var prods = new Dictionary<long, Dictionary<string, object?>>();
        if (prodIds.Length > 0)
        {
            var pRows = await Db.QueryListAsync("SELECT * FROM products WHERE id IN @ids", new { ids = prodIds });
            foreach (var p in pRows) prods[ToLong(p["id"])] = p;
        }

        foreach (var it in items)
        {
            var pid = ToLong(it.GetValueOrDefault("product_id"));
            it["product"] = pid > 0 && prods.TryGetValue(pid, out var p) ? p : null;
        }
        pr["items"] = items;

        var accId = ToLong(pr.GetValueOrDefault("account_id"));
        pr["account"] = accId > 0 && await Db.HasTableAsync("accounts")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accId })
            : null;

        return pr;
    }

    private async Task<List<Dictionary<string, object?>>> EnrichReturnBatchAsync(List<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return rows;

        var supIds = rows.Select(r => ToLong(r.GetValueOrDefault("supplier_id"))).Where(id => id > 0).Distinct().ToArray();
        var invIds = rows.Select(r => ToLong(r.GetValueOrDefault("purchase_invoice_id"))).Where(id => id > 0).Distinct().ToArray();
        var userIds = rows.Select(r => ToLong(r.GetValueOrDefault("user_id"))).Where(id => id > 0).Distinct().ToArray();
        var empIds = rows.Select(r => ToLong(r.GetValueOrDefault("employee_id"))).Where(id => id > 0).Distinct().ToArray();

        var suppliers = new Dictionary<long, Dictionary<string, object?>>();
        if (supIds.Length > 0 && await Db.HasTableAsync("suppliers"))
        {
            var sRows = await Db.QueryListAsync("SELECT * FROM suppliers WHERE id IN @ids", new { ids = supIds });
            foreach (var sr in sRows) suppliers[ToLong(sr["id"])] = sr;
        }

        var invoices = new Dictionary<long, Dictionary<string, object?>>();
        if (invIds.Length > 0 && await Db.HasTableAsync("purchase_invoices"))
        {
            var iRows = await Db.QueryListAsync("SELECT * FROM purchase_invoices WHERE id IN @ids", new { ids = invIds });
            foreach (var ir in iRows) invoices[ToLong(ir["id"])] = ir;
        }

        var users = new Dictionary<long, Dictionary<string, object?>>();
        if (userIds.Length > 0 && await Db.HasTableAsync("users"))
        {
            var uRows = await Db.QueryListAsync("SELECT id, name FROM users WHERE id IN @ids", new { ids = userIds });
            foreach (var ur in uRows) users[ToLong(ur["id"])] = ur;
        }

        var employees = new Dictionary<long, Dictionary<string, object?>>();
        if (empIds.Length > 0 && await Db.HasTableAsync("employees"))
        {
            var eRows = await Db.QueryListAsync("SELECT id, name, employee_code FROM employees WHERE id IN @ids", new { ids = empIds });
            foreach (var er in eRows) employees[ToLong(er["id"])] = er;
        }

        foreach (var r in rows)
        {
            var sid = ToLong(r.GetValueOrDefault("supplier_id"));
            var iid = ToLong(r.GetValueOrDefault("purchase_invoice_id"));
            var uid = ToLong(r.GetValueOrDefault("user_id"));
            var eid = ToLong(r.GetValueOrDefault("employee_id"));

            r["supplier"] = sid > 0 && suppliers.TryGetValue(sid, out var s) ? s : null;
            var pi = iid > 0 && invoices.TryGetValue(iid, out var inv) ? inv : null;
            r["purchase_invoice"] = pi;
            r["purchaseInvoice"] = pi;
            r["user"] = uid > 0 && users.TryGetValue(uid, out var u) ? u : null;
            r["employee"] = eid > 0 && employees.TryGetValue(eid, out var e) ? e : null;
        }

        return rows;
    }

    private static (Dictionary<string, object?> Data, List<Dictionary<string, object?>> Items) ParsePayloadAndItems(Dictionary<string, JsonElement> body)
    {
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var items = new List<Dictionary<string, object?>>();

        foreach (var kv in body)
        {
            if (string.Equals(kv.Key, "items", StringComparison.OrdinalIgnoreCase) && kv.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var itemEl in kv.Value.EnumerateArray())
                {
                    if (itemEl.ValueKind == JsonValueKind.Object)
                    {
                        var itemDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prop in itemEl.EnumerateObject())
                        {
                            itemDict[prop.Name] = KarobarDb.PrepareDbValue(prop.Value);
                        }
                        items.Add(itemDict);
                    }
                }
            }
            else
            {
                data[kv.Key] = KarobarDb.PrepareDbValue(kv.Value);
            }
        }

        return (data, items);
    }

    private static long ToLong(object? val)
    {
        if (val == null) return 0;
        if (val is long l) return l;
        if (val is int i) return i;
        return long.TryParse(val.ToString(), out var p) ? p : 0;
    }

    private static long? ToNullableLong(object? val)
    {
        var l = ToLong(val);
        return l > 0 ? l : null;
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

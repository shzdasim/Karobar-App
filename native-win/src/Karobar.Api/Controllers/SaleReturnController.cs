using System.Data;
using System.Text.Json;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Background;
using Karobar.Data;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Authorize]
[Route("api/sale-returns")]
public class SaleReturnController : ApiControllerBase
{
    private readonly DocumentSequenceService _sequenceService;
    private readonly InvoiceCalculationService _calculationService;
    private readonly ReturnEntitlementService _entitlementService;
    private readonly ReturnSettlementService _settlementService;
    private readonly SaleCostSnapshotService _costSnapshotService;
    private readonly OperatorPinService _operatorPinService;
    private readonly FbrFiscalizationEngine? _fbrEngine;
    private readonly KarobarDbContext? _dbContext;

    public SaleReturnController(
        KarobarDb db,
        FbrFiscalizationEngine? fbrEngine = null,
        KarobarDbContext? dbContext = null) : base(db)
    {
        _sequenceService = new DocumentSequenceService(db);
        _calculationService = new InvoiceCalculationService(db);
        _entitlementService = new ReturnEntitlementService(db);
        _settlementService = new ReturnSettlementService(db);
        _costSnapshotService = new SaleCostSnapshotService(db);
        _operatorPinService = new OperatorPinService(db);
        _fbrEngine = fbrEngine;
        _dbContext = dbContext;
    }

    [HttpGet("new-code")]
    public async Task<IActionResult> GenerateNewCode()
    {
        if (!await CanAsync("sale-return.create"))
        {
            return Forbidden();
        }

        var code = await _sequenceService.PreviewAsync(
            "sale_return",
            "SLRET-",
            4,
            "sale_returns",
            "posted_number",
            true);

        return Ok(new Dictionary<string, object?> { ["posted_number"] = code });
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] string? posted = null,
        [FromQuery] string? customer = null,
        [FromQuery] int page = 1,
        [FromQuery] int? per_page = null)
    {
        if (!await CanAsync("sale-return.view"))
        {
            return Forbidden();
        }

        var where = new List<string> { "1=1" };
        var dyn = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(posted))
        {
            where.Add("sr.posted_number LIKE @posted");
            dyn.Add("posted", "%" + posted.Trim() + "%");
        }
        if (!string.IsNullOrWhiteSpace(customer))
        {
            where.Add("EXISTS (SELECT 1 FROM customers c WHERE c.id = sr.customer_id AND c.name LIKE @customer)");
            dyn.Add("customer", "%" + customer.Trim() + "%");
        }

        var whereSql = string.Join(" AND ", where);
        var selectSql = $"SELECT sr.* FROM sale_returns sr WHERE {whereSql} ORDER BY sr.id DESC";
        var countSql = $"SELECT COUNT(1) FROM sale_returns sr WHERE {whereSql}";

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
        if (!await CanAsync("sale-return.view"))
        {
            return Forbidden();
        }

        var loaded = await LoadSaleReturnAsync(id);
        if (loaded == null) return NotFound(new { message = "Sale return not found." });

        return Ok(loaded);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("sale-return.create"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var (rawData, rawItems) = ParsePayloadAndItems(body);

        var customerId = ToLong(rawData.GetValueOrDefault("customer_id"));
        var date = rawData.GetValueOrDefault("date")?.ToString() ?? "";
        if (customerId <= 0)
        {
            return Unprocessable("Customer is required.", new Dictionary<string, string[]> { ["customer_id"] = new[] { "The customer id field is required." } });
        }
        if (string.IsNullOrWhiteSpace(date))
        {
            return Unprocessable("Date is required.", new Dictionary<string, string[]> { ["date"] = new[] { "The date field is required." } });
        }

        try
        {
            var rawPin = GetString(body, "operator_pin") ?? Request.Headers["X-Operator-Pin"].FirstOrDefault();
            var op = await _operatorPinService.ResolveOperatorAsync(rawPin, CurrentUserId, "sale_return");
            var operatorUserId = ToNullableLong(op.GetValueOrDefault("user_id")) ?? CurrentUserId;
            var operatorEmpId = ToNullableLong(op.GetValueOrDefault("employee_id"));
            if (!operatorUserId.HasValue)
            {
                return Unauthorized(new { message = "Unauthenticated: user_id is required" });
            }

            var calculated = await _calculationService.CalculateAndValidateSaleReturnAsync(rawData, rawItems);
            var calcItems = (List<Dictionary<string, object?>>)calculated["items"]!;

            var postedNumber = await _sequenceService.NextAsync(
                "sale_return",
                "SLRET-",
                4,
                "sale_returns",
                "posted_number",
                true);

            await using var conn = await Db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                await _entitlementService.ValidateAsync("sale", rawData, calcItems, null, conn, tx);

                var createPayload = new Dictionary<string, object?>
                {
                    ["user_id"] = operatorUserId.Value,
                    ["employee_id"] = operatorEmpId,
                    ["customer_id"] = customerId,
                    ["sale_invoice_id"] = ToNullableLong(rawData.GetValueOrDefault("sale_invoice_id")),
                    ["posted_number"] = postedNumber,
                    ["date"] = date,
                    ["remarks"] = rawData.GetValueOrDefault("remarks"),
                    ["gross_total"] = calculated["gross_total"],
                    ["discount_percentage"] = calculated["discount_percentage"],
                    ["discount_amount"] = calculated["discount_amount"],
                    ["tax_percentage"] = calculated["tax_percentage"],
                    ["tax_amount"] = calculated["tax_amount"],
                    ["total"] = calculated["total"]
                };

                var srId = await Db.InsertAsync("sale_returns", createPayload, conn, tx);
                var srRow = (await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_returns WHERE id = @id", new { id = srId }, conn, tx))!;

                await CreateItemsAndIncreaseAsync(srRow, calcItems, null, conn, tx);

                srRow["settlement_mode"] = await _settlementService.ResolveSaleSettlementModeAsync(
                    rawData.GetValueOrDefault("settlement_mode")?.ToString(),
                    ToNullableLong(rawData.GetValueOrDefault("sale_invoice_id")),
                    null,
                    conn,
                    tx);
                srRow["payment_method"] = rawData.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
                srRow["account_id"] = ToNullableLong(rawData.GetValueOrDefault("account_id"));

                await _settlementService.ApplySaleReturnSettlementAsync(srRow, operatorUserId, conn, tx);

                await tx.CommitAsync();
                SyncPulseService.Bump("stock", "drawer");

                if (_fbrEngine != null && _dbContext != null && await Db.HasTableAsync("settings"))
                {
                    try
                    {
                        var st = await Db.QuerySingleOrDefaultAsync("SELECT fbr_enabled, fbr_auto_sync FROM settings ORDER BY id ASC LIMIT 1");
                        var fbrOn = st != null && st.GetValueOrDefault("fbr_enabled") is true or 1 or 1L;
                        var fbrAuto = st != null && st.GetValueOrDefault("fbr_auto_sync") is true or 1 or 1L;
                        if (fbrOn && fbrAuto)
                        {
                            await _fbrEngine.FiscalizeSaleReturnAsync(_dbContext, (int)srId, force: false, HttpContext?.RequestAborted ?? CancellationToken.None);
                        }
                    }
                    catch
                    {
                        // Never block return creation if FBR gateway is unreachable
                    }
                }

                var loaded = (await LoadSaleReturnAsync(srId))!;
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
        if (!await CanAsync("sale-return.update"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_returns WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Sale return not found." });

        var body = await ReadJsonBodyAsync();
        var (rawData, rawItems) = ParsePayloadAndItems(body);

        try
        {
            var rawPin = GetString(body, "operator_pin") ?? Request.Headers["X-Operator-Pin"].FirstOrDefault();
            var op = await _operatorPinService.ResolveOperatorAsync(rawPin, CurrentUserId, "sale_return");
            var operatorUserId = ToNullableLong(op.GetValueOrDefault("user_id")) ?? CurrentUserId;
            var operatorEmpId = ToNullableLong(op.GetValueOrDefault("employee_id"));
            var verifiedViaPin = op.GetValueOrDefault("verified_via_pin") is true;

            if (!operatorUserId.HasValue)
            {
                return Unauthorized(new { message = "Unauthenticated: user_id is required" });
            }

            var calculated = await _calculationService.CalculateAndValidateSaleReturnAsync(rawData, rawItems);
            var calcItems = (List<Dictionary<string, object?>>)calculated["items"]!;

            await using var conn = await Db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                var oldSnapshot = new Dictionary<string, object?>
                {
                    ["total"] = ToDouble(existing.GetValueOrDefault("total")),
                    ["settlement_mode"] = existing.GetValueOrDefault("settlement_mode")?.ToString() ?? "cash_refund",
                    ["payment_method"] = existing.GetValueOrDefault("payment_method")?.ToString() ?? "cash",
                    ["account_id"] = existing.GetValueOrDefault("account_id")
                };

                await _entitlementService.ValidateAsync("sale", rawData, calcItems, existing, conn, tx);

                var oldItems = await Db.QueryListAsync("SELECT * FROM sale_return_items WHERE sale_return_id = @id", new { id }, conn, tx);
                var sameSourceAndCustomer = ToLong(existing.GetValueOrDefault("sale_invoice_id")) == ToLong(rawData.GetValueOrDefault("sale_invoice_id")) &&
                                            ToLong(existing.GetValueOrDefault("customer_id")) == ToLong(rawData.GetValueOrDefault("customer_id"));
                var originalItems = sameSourceAndCustomer ? oldItems : null;

                await RevertItemsAsync(oldItems, conn, tx);
                await Db.ExecuteAsync("DELETE FROM sale_return_items WHERE sale_return_id = @id", new { id }, conn, tx);

                var updatePayload = new Dictionary<string, object?>
                {
                    ["user_id"] = operatorUserId.Value,
                    ["customer_id"] = ToLong(rawData.GetValueOrDefault("customer_id") ?? existing.GetValueOrDefault("customer_id")),
                    ["sale_invoice_id"] = ToNullableLong(rawData.GetValueOrDefault("sale_invoice_id")),
                    ["posted_number"] = rawData.GetValueOrDefault("posted_number") ?? existing.GetValueOrDefault("posted_number"),
                    ["date"] = rawData.GetValueOrDefault("date") ?? existing.GetValueOrDefault("date"),
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
                    updatePayload["employee_id"] = operatorEmpId;
                }

                await Db.UpdateAsync("sale_returns", id, updatePayload, "id", conn, tx);
                var srRow = (await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_returns WHERE id = @id", new { id }, conn, tx))!;

                await CreateItemsAndIncreaseAsync(srRow, calcItems, originalItems, conn, tx);

                srRow["settlement_mode"] = await _settlementService.ResolveSaleSettlementModeAsync(
                    rawData.GetValueOrDefault("settlement_mode")?.ToString(),
                    ToNullableLong(rawData.GetValueOrDefault("sale_invoice_id")),
                    oldSnapshot["settlement_mode"]?.ToString(),
                    conn,
                    tx);
                srRow["payment_method"] = rawData.GetValueOrDefault("payment_method")?.ToString() ?? (oldSnapshot["payment_method"]?.ToString() ?? "cash");
                if (rawData.ContainsKey("account_id"))
                {
                    srRow["account_id"] = ToNullableLong(rawData["account_id"]);
                }

                bool? updateDrawerFlag = null;
                if (body.ContainsKey("update_drawer")) updateDrawerFlag = GetBool(body, "update_drawer");
                else if (body.ContainsKey("update_drawer_if_pre_shift")) updateDrawerFlag = GetBool(body, "update_drawer_if_pre_shift");

                await _settlementService.SyncSaleReturnSettlementUpdateAsync(srRow, oldSnapshot, updateDrawerFlag, operatorUserId, conn, tx);

                await tx.CommitAsync();
                SyncPulseService.Bump("stock", "drawer");

                var loaded = (await LoadSaleReturnAsync(id))!;
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
        if (!await CanAsync("sale-return.delete"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_returns WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Sale return not found." });

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            await _settlementService.RevertSaleReturnSettlementAsync(existing, CurrentUserId, conn, tx);

            var oldItems = await Db.QueryListAsync("SELECT * FROM sale_return_items WHERE sale_return_id = @id", new { id }, conn, tx);
            await RevertItemsAsync(oldItems, conn, tx);

            await Db.ExecuteAsync("DELETE FROM sale_return_items WHERE sale_return_id = @id", new { id }, conn, tx);
            await Db.ExecuteAsync("DELETE FROM sale_returns WHERE id = @id", new { id }, conn, tx);

            await tx.CommitAsync();
            SyncPulseService.Bump("stock", "drawer");

            return Ok(new Dictionary<string, object?> { ["message"] = "Sale return deleted" });
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

    private async Task CreateItemsAndIncreaseAsync(
        Dictionary<string, object?> srRow,
        List<Dictionary<string, object?>> items,
        List<Dictionary<string, object?>>? originalItems,
        IDbConnection conn,
        IDbTransaction tx)
    {
        var srId = ToLong(srRow["id"]);
        var saleInvoiceId = ToLong(srRow.GetValueOrDefault("sale_invoice_id"));
        var customerId = ToLong(srRow.GetValueOrDefault("customer_id"));

        var sourceItems = new List<Dictionary<string, object?>>();
        if (saleInvoiceId > 0)
        {
            var sourceInv = await Db.QuerySingleOrDefaultAsync(
                "SELECT id FROM sale_invoices WHERE id = @id AND customer_id = @cid",
                new { id = saleInvoiceId, cid = customerId },
                conn,
                tx);
            if (sourceInv != null)
            {
                sourceItems = await Db.QueryListAsync(
                    "SELECT * FROM sale_invoice_items WHERE sale_invoice_id = @id",
                    new { id = saleInvoiceId },
                    conn,
                    tx);
            }
        }

        foreach (var raw in items)
        {
            var pid = ToLong(raw.GetValueOrDefault("product_id"));
            var matches = sourceItems.Where(si => ToLong(si.GetValueOrDefault("product_id")) == pid).ToList();
            var batchNo = (raw.GetValueOrDefault("batch_number") ?? raw.GetValueOrDefault("batch"))?.ToString();
            var expiry = raw.GetValueOrDefault("expiry")?.ToString();

            if (!string.IsNullOrWhiteSpace(batchNo) || !string.IsNullOrWhiteSpace(expiry))
            {
                matches = _costSnapshotService.MatchingRows(matches, raw);
            }

            var cost = originalItems != null
                ? _costSnapshotService.OriginalCost(originalItems, raw)
                : _costSnapshotService.UnambiguousCost(matches);

            var returnUnits = (int)ToLong(raw.GetValueOrDefault("unit_return_quantity"));

            await Db.InsertAsync("sale_return_items", new Dictionary<string, object?>
            {
                ["sale_return_id"] = srId,
                ["cost_price_snapshot"] = cost,
                ["product_id"] = pid,
                ["batch_number"] = string.IsNullOrWhiteSpace(batchNo) ? null : batchNo,
                ["expiry"] = string.IsNullOrWhiteSpace(expiry) ? null : expiry,
                ["unit_sale_quantity"] = (int)ToLong(raw.GetValueOrDefault("unit_sale_quantity")),
                ["unit_sale_price"] = ToDouble(raw.GetValueOrDefault("unit_sale_price")),
                ["item_discount_percentage"] = ToDouble(raw.GetValueOrDefault("item_discount_percentage")),
                ["unit_return_quantity"] = returnUnits,
                ["sub_total"] = ToDouble(raw.GetValueOrDefault("sub_total"))
            }, conn, tx);

            await ApplyStockDeltaSmartAsync(pid, batchNo, expiry, +returnUnits, conn, tx);
        }
    }

    private async Task RevertItemsAsync(
        List<Dictionary<string, object?>> items,
        IDbConnection conn,
        IDbTransaction tx)
    {
        foreach (var it in items)
        {
            var pid = ToLong(it.GetValueOrDefault("product_id"));
            var batchNo = it.GetValueOrDefault("batch_number")?.ToString();
            var expiry = it.GetValueOrDefault("expiry")?.ToString();
            var units = (int)ToLong(it.GetValueOrDefault("unit_return_quantity"));

            await ApplyStockDeltaSmartAsync(pid, batchNo, expiry, -units, conn, tx);
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
                var msg = $"Cannot revert or delete sale return because returned stock in batch {bTrim} has already been sold or consumed.";
                throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                {
                    ["items"] = new[] { msg }
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
                var msg = $"Cannot revert or delete sale return because returned stock for {pName} has already been sold or consumed.";
                throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                {
                    ["items"] = new[] { msg }
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

    private async Task<Dictionary<string, object?>?> LoadSaleReturnAsync(long id)
    {
        var sr = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_returns WHERE id = @id", new { id });
        if (sr == null) return null;

        await EnrichReturnBatchAsync(new List<Dictionary<string, object?>> { sr });

        var items = await Db.QueryListAsync("SELECT * FROM sale_return_items WHERE sale_return_id = @id", new { id });
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
        sr["items"] = items;

        var accId = ToLong(sr.GetValueOrDefault("account_id"));
        sr["account"] = accId > 0 && await Db.HasTableAsync("accounts")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accId })
            : null;

        return sr;
    }

    private async Task<List<Dictionary<string, object?>>> EnrichReturnBatchAsync(List<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return rows;

        var custIds = rows.Select(r => ToLong(r.GetValueOrDefault("customer_id"))).Where(id => id > 0).Distinct().ToArray();
        var invIds = rows.Select(r => ToLong(r.GetValueOrDefault("sale_invoice_id"))).Where(id => id > 0).Distinct().ToArray();
        var userIds = rows.Select(r => ToLong(r.GetValueOrDefault("user_id"))).Where(id => id > 0).Distinct().ToArray();
        var empIds = rows.Select(r => ToLong(r.GetValueOrDefault("employee_id"))).Where(id => id > 0).Distinct().ToArray();

        var customers = new Dictionary<long, Dictionary<string, object?>>();
        if (custIds.Length > 0 && await Db.HasTableAsync("customers"))
        {
            var cRows = await Db.QueryListAsync("SELECT * FROM customers WHERE id IN @ids", new { ids = custIds });
            foreach (var cr in cRows) customers[ToLong(cr["id"])] = cr;
        }

        var invoices = new Dictionary<long, Dictionary<string, object?>>();
        if (invIds.Length > 0 && await Db.HasTableAsync("sale_invoices"))
        {
            var iRows = await Db.QueryListAsync("SELECT * FROM sale_invoices WHERE id IN @ids", new { ids = invIds });
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
            var cid = ToLong(r.GetValueOrDefault("customer_id"));
            var iid = ToLong(r.GetValueOrDefault("sale_invoice_id"));
            var uid = ToLong(r.GetValueOrDefault("user_id"));
            var eid = ToLong(r.GetValueOrDefault("employee_id"));

            r["customer"] = cid > 0 && customers.TryGetValue(cid, out var c) ? c : null;
            var si = iid > 0 && invoices.TryGetValue(iid, out var inv) ? inv : null;
            r["sale_invoice"] = si;
            r["saleInvoice"] = si;
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

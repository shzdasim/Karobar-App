using System.Data;
using System.Text.Json;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/purchase-invoices")]
[Authorize]
public class PurchaseInvoiceController : ApiControllerBase
{
    private readonly DocumentSequenceService _sequenceService;
    private readonly InvoiceCalculationService _calculationService;
    private readonly PurchaseOrderReceivingService _poReceivingService;
    private readonly SupplierProductSortingService _sortingService;
    private readonly ReturnEntitlementService _entitlementService;
    private readonly ReturnSettlementService _settlementService;
    private readonly DocumentAuditService _auditService;
    private readonly OperatorPinService _operatorPinService;

    public PurchaseInvoiceController(KarobarDb db) : base(db)
    {
        _sequenceService = new DocumentSequenceService(db);
        _calculationService = new InvoiceCalculationService(db);
        _poReceivingService = new PurchaseOrderReceivingService(db);
        _sortingService = new SupplierProductSortingService(db);
        _entitlementService = new ReturnEntitlementService(db);
        _settlementService = new ReturnSettlementService(db);
        _auditService = new DocumentAuditService(db);
        _operatorPinService = new OperatorPinService(db);
    }

    [HttpGet("new-code")]
    public async Task<IActionResult> GenerateNewCode()
    {
        if (!await CanAsync("purchase-invoice.create"))
        {
            return Forbidden();
        }

        var code = await _sequenceService.PreviewAsync(
            "purchase_invoice",
            "PRINV-",
            4,
            "purchase_invoices",
            "posted_number");

        return Ok(new Dictionary<string, object?> { ["posted_number"] = code });
    }

    [HttpGet("check-unique")]
    public async Task<IActionResult> CheckUnique(
        [FromQuery] long? supplier_id = null,
        [FromQuery] string? invoice_number = null,
        [FromQuery] long? exclude_id = null)
    {
        if (!supplier_id.HasValue || supplier_id.Value <= 0 || string.IsNullOrWhiteSpace(invoice_number))
        {
            return Unprocessable("supplier_id and invoice_number are required.");
        }

        if (exclude_id.HasValue && exclude_id.Value > 0)
        {
            if (!await CanAsync("purchase-invoice.update")) return Forbidden();
        }
        else
        {
            if (!await CanAsync("purchase-invoice.create")) return Forbidden();
        }

        var sql = "SELECT COUNT(1) FROM purchase_invoices WHERE supplier_id = @sid AND invoice_number = @invNo";
        if (exclude_id.HasValue && exclude_id.Value > 0)
        {
            sql += " AND id != @excludeId";
        }

        var count = await Db.ExecuteScalarAsync<int>(sql, new
        {
            sid = supplier_id.Value,
            invNo = invoice_number.Trim(),
            excludeId = exclude_id
        });

        return Ok(new Dictionary<string, object?> { ["unique"] = count == 0 });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q = null,
        [FromQuery] long? supplier_id = null,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 20)
    {
        if (!await CanAsync("purchase-invoice.view", "purchase-return.create", "purchase-return.update", "payment.create"))
        {
            return Forbidden();
        }

        var where = new List<string> { "1=1" };
        var dyn = new DynamicParameters();

        if (supplier_id.HasValue && supplier_id.Value > 0)
        {
            where.Add("pi.supplier_id = @sid");
            dyn.Add("sid", supplier_id.Value);
        }

        var qTrim = (q ?? "").Trim();
        if (!string.IsNullOrEmpty(qTrim))
        {
            where.Add(@"(
                pi.posted_number LIKE @qLike
                OR pi.invoice_number LIKE @qLike
                OR EXISTS (SELECT 1 FROM suppliers s WHERE s.id = pi.supplier_id AND s.name LIKE @qLike)
            )");
            dyn.Add("qLike", "%" + qTrim + "%");
        }

        var whereSql = string.Join(" AND ", where);
        var selectSql = $"SELECT pi.* FROM purchase_invoices pi WHERE {whereSql} ORDER BY pi.id DESC";
        var countSql = $"SELECT COUNT(1) FROM purchase_invoices pi WHERE {whereSql}";

        var paged = await Db.PaginateAsync(selectSql, countSql, dyn, page, per_page, async rows =>
        {
            await EnrichInvoiceBatchAsync(rows);
            foreach (var r in rows)
            {
                var invTotal = ToDouble(r.GetValueOrDefault("total_amount") ?? r.GetValueOrDefault("invoice_amount"));
                var paid = ToDouble(r.GetValueOrDefault("total_paid"));
                r["remaining"] = Math.Max(0.0, invTotal - paid);
                var sup = r.GetValueOrDefault("supplier") as Dictionary<string, object?>;
                r["supplier_name"] = sup?.GetValueOrDefault("name")?.ToString() ?? r.GetValueOrDefault("supplier_id")?.ToString() ?? "N/A";
            }
            return rows;
        });

        return Ok(paged);
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] long? supplier_id = null,
        [FromQuery] string? posted = null,
        [FromQuery] string? supplier = null,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 10)
    {
        if (supplier_id.HasValue && supplier_id.Value > 0)
        {
            if (!await CanAsync("purchase-invoice.view", "purchase-return.create", "purchase-return.update", "payment.create"))
            {
                return Forbidden();
            }
        }
        else if (!await CanAsync("purchase-invoice.view"))
        {
            return Forbidden();
        }

        var where = new List<string> { "1=1" };
        var dyn = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(posted))
        {
            where.Add("pi.posted_number LIKE @posted");
            dyn.Add("posted", "%" + posted.Trim() + "%");
        }
        if (supplier_id.HasValue && supplier_id.Value > 0)
        {
            where.Add("pi.supplier_id = @sid");
            dyn.Add("sid", supplier_id.Value);
        }
        if (!string.IsNullOrWhiteSpace(supplier))
        {
            where.Add("EXISTS (SELECT 1 FROM suppliers s WHERE s.id = pi.supplier_id AND s.name LIKE @supplier)");
            dyn.Add("supplier", "%" + supplier.Trim() + "%");
        }

        var whereSql = string.Join(" AND ", where);
        var selectSql = $"SELECT pi.* FROM purchase_invoices pi WHERE {whereSql} ORDER BY pi.id DESC";
        var countSql = $"SELECT COUNT(1) FROM purchase_invoices pi WHERE {whereSql}";

        per_page = Math.Max(1, Math.Min(per_page, 200));
        var paged = await Db.PaginateAsync(selectSql, countSql, dyn, page, per_page, EnrichInvoiceBatchAsync);
        return Ok(paged);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id, [FromQuery] long? exclude_return_id = null)
    {
        if (!await CanAsync("purchase-invoice.view", "purchase-return.create", "purchase-return.update"))
        {
            return Forbidden();
        }

        var loaded = await LoadPurchaseInvoiceAsync(id, exclude_return_id);
        if (loaded == null) return NotFound(new { message = "Purchase invoice not found." });

        return Ok(loaded);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("purchase-invoice.create"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var (rawData, rawItems) = ParsePayloadAndItems(body);
        var rawPin = GetString(body, "operator_pin") ?? Request.Headers["X-Operator-Pin"].FirstOrDefault();

        return await ExecuteStorePurchaseInvoiceAsync(rawData, rawItems, rawPin);
    }

    [NonAction]
    public async Task<IActionResult> ExecuteStorePurchaseInvoiceAsync(
        Dictionary<string, object?> rawData,
        List<Dictionary<string, object?>> rawItems,
        string? rawPin = null)
    {
        var supplierId = ToLong(rawData.GetValueOrDefault("supplier_id"));
        var postedDate = rawData.GetValueOrDefault("posted_date")?.ToString() ?? "";
        var invoiceNumber = (rawData.GetValueOrDefault("invoice_number")?.ToString() ?? "").Trim();

        var errors = new Dictionary<string, string[]>();
        if (supplierId <= 0) errors["supplier_id"] = new[] { "The supplier id field is required." };
        if (string.IsNullOrWhiteSpace(postedDate)) errors["posted_date"] = new[] { "The posted date field is required." };
        if (string.IsNullOrWhiteSpace(invoiceNumber)) errors["invoice_number"] = new[] { "The invoice number field is required." };
        if (errors.Count > 0)
        {
            return Unprocessable("The given data was invalid.", errors);
        }

        try
        {
            var calculated = _calculationService.CalculateAndValidatePurchase(rawData, rawItems);
            var calcItems = (List<Dictionary<string, object?>>)calculated["items"]!;

            var invoiceType = (rawData.GetValueOrDefault("invoice_type")?.ToString() ?? "debit").ToLowerInvariant();
            double totalAmount = ToDouble(calculated["total_amount"]);
            double totalPaid;
            if (!rawData.ContainsKey("total_paid") || rawData["total_paid"] == null || string.IsNullOrWhiteSpace(rawData["total_paid"]?.ToString()))
            {
                totalPaid = invoiceType == "credit" ? 0.0 : totalAmount;
            }
            else
            {
                totalPaid = ToDouble(rawData["total_paid"]);
            }

            var op = await _operatorPinService.ResolveOperatorAsync(rawPin, CurrentUserId, "purchase_invoice");
            var operatorUserId = ToNullableLong(op.GetValueOrDefault("user_id")) ?? CurrentUserId;
            var operatorEmpId = ToNullableLong(op.GetValueOrDefault("employee_id"));

            var draftId = ToNullableLong(rawData.GetValueOrDefault("draft_id"));
            if (draftId.HasValue && await Db.HasTableAsync("invoice_drafts"))
            {
                var draft = await Db.QuerySingleOrDefaultAsync("SELECT * FROM invoice_drafts WHERE id = @id", new { id = draftId.Value });
                if (draft != null && string.Equals(draft.GetValueOrDefault("status")?.ToString(), "posted", StringComparison.OrdinalIgnoreCase))
                {
                    var postedInvId = ToLong(draft.GetValueOrDefault("posted_invoice_id"));
                    if (postedInvId > 0)
                    {
                        var postedInv = await LoadPurchaseInvoiceAsync(postedInvId);
                        return Ok(postedInv);
                    }
                }
            }

            // Check duplicate invoice_number
            var dupInvCount = await Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM purchase_invoices WHERE invoice_number = @invNo",
                new { invNo = invoiceNumber });
            if (dupInvCount > 0)
            {
                return Unprocessable("Validation failed. Please correct the highlighted fields.", new Dictionary<string, string[]>
                {
                    ["invoice_number"] = new[] { "Invoice number has already been taken for this supplier or globally." }
                });
            }

            var purchaseOrderId = ToNullableLong(rawData.GetValueOrDefault("purchase_order_id"));
            var receiptKey = rawData.GetValueOrDefault("receipt_key")?.ToString();
            if (purchaseOrderId.HasValue && !string.IsNullOrWhiteSpace(receiptKey))
            {
                var dupReceipt = await Db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM purchase_invoices WHERE receipt_key = @rk",
                    new { rk = receiptKey });
                if (dupReceipt > 0)
                {
                    return Unprocessable("Validation failed. Please correct the highlighted fields.", new Dictionary<string, string[]>
                    {
                        ["receipt_key"] = new[] { "This receipt has already been saved. Open the Purchase Order to review its invoices." }
                    });
                }
            }

            var nextCode = await _sequenceService.NextAsync(
                "purchase_invoice",
                "PRINV-",
                4,
                "purchase_invoices",
                "posted_number");

            await using var conn = await Db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                if (purchaseOrderId.HasValue)
                {
                    var po = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_orders WHERE id = @id", new { id = purchaseOrderId.Value }, conn, tx);
                    if (po == null)
                    {
                        throw new InvoiceValidationException("Purchase Order not found.", new Dictionary<string, string[]>
                        {
                            ["purchase_order_id"] = new[] { "Purchase Order not found." }
                        });
                    }
                    var poStatus = (po.GetValueOrDefault("status")?.ToString() ?? "").ToLowerInvariant();
                    if (poStatus is not ("ordered" or "partially_received"))
                    {
                        throw new InvoiceValidationException("This Purchase Order cannot be received.", new Dictionary<string, string[]>
                        {
                            ["purchase_order_id"] = new[] { "This Purchase Order cannot be received." }
                        });
                    }
                    await _poReceivingService.ValidateReceiptAsync(purchaseOrderId.Value, calcItems, conn, tx);
                }
                else
                {
                    foreach (var row in calcItems) row.Remove("purchase_order_item_id");
                }

                var dueDate = await ResolveDueDateAsync(rawData, supplierId, postedDate, conn, tx);
                var payMethod = rawData.GetValueOrDefault("payment_method")?.ToString() ?? "cash";

                var createPayload = new Dictionary<string, object?>
                {
                    ["purchase_order_id"] = purchaseOrderId,
                    ["receipt_key"] = purchaseOrderId.HasValue ? receiptKey : null,
                    ["supplier_id"] = supplierId,
                    ["invoice_type"] = invoiceType,
                    ["payment_method"] = payMethod,
                    ["account_id"] = ToNullableLong(rawData.GetValueOrDefault("account_id")),
                    ["posted_number"] = nextCode,
                    ["posted_date"] = postedDate,
                    ["due_date"] = dueDate,
                    ["remarks"] = rawData.GetValueOrDefault("remarks"),
                    ["invoice_number"] = invoiceNumber,
                    ["invoice_amount"] = calculated["invoice_amount"],
                    ["tax_percentage"] = calculated["tax_percentage"],
                    ["tax_amount"] = calculated["tax_amount"],
                    ["discount_percentage"] = calculated["discount_percentage"],
                    ["discount_amount"] = calculated["discount_amount"],
                    ["total_amount"] = totalAmount,
                    ["total_paid"] = totalPaid,
                    ["user_id"] = operatorUserId,
                    ["employee_id"] = operatorEmpId
                };

                var invoiceId = await Db.InsertAsync("purchase_invoices", createPayload, conn, tx);

                await ProcessInvoiceItemsAsync(invoiceId, supplierId, calcItems, conn, tx);

                if (invoiceType == "credit" && await Db.HasTableAsync("supplier_ledgers"))
                {
                    var invTotal = Math.Round(totalAmount, 2);
                    var paidVal = Math.Round(totalPaid, 2);
                    var paidOnInv = Math.Min(paidVal, invTotal);
                    var excessPaid = Math.Round(Math.Max(0.0, paidVal - invTotal), 2);

                    await Db.InsertAsync("supplier_ledgers", new Dictionary<string, object?>
                    {
                        ["supplier_id"] = supplierId,
                        ["purchase_invoice_id"] = invoiceId,
                        ["entry_type"] = "invoice",
                        ["is_manual"] = 0,
                        ["entry_date"] = postedDate,
                        ["due_date"] = dueDate,
                        ["posted_number"] = nextCode,
                        ["invoice_number"] = invoiceNumber,
                        ["invoice_total"] = invTotal,
                        ["total_paid"] = paidOnInv,
                        ["debited_amount"] = 0,
                        ["credit_remaining"] = Math.Max(invTotal - paidOnInv, 0.0),
                        ["payment_ref"] = null,
                        ["description"] = "Purchase invoice " + nextCode,
                        ["created_by"] = CurrentUserId
                    }, conn, tx);

                    if (excessPaid > 0)
                    {
                        await Db.InsertAsync("supplier_ledgers", new Dictionary<string, object?>
                        {
                            ["supplier_id"] = supplierId,
                            ["purchase_invoice_id"] = null,
                            ["entry_type"] = "payment",
                            ["is_manual"] = 0,
                            ["entry_date"] = postedDate,
                            ["posted_number"] = nextCode,
                            ["invoice_number"] = invoiceNumber,
                            ["invoice_total"] = 0,
                            ["total_paid"] = 0,
                            ["debited_amount"] = excessPaid,
                            ["credit_remaining"] = 0,
                            ["payment_ref"] = "Inv #" + nextCode,
                            ["description"] = "Payment made on invoice " + nextCode,
                            ["created_by"] = CurrentUserId
                        }, conn, tx);
                    }
                }

                if (purchaseOrderId.HasValue)
                {
                    await _poReceivingService.SyncAsync(purchaseOrderId.Value, CurrentUserId, conn, tx);
                    await _sortingService.RememberAsync(supplierId, calcItems.Select(i => ToLong(i.GetValueOrDefault("product_id"))), conn, tx);
                }

                if (draftId.HasValue && await Db.HasTableAsync("invoice_drafts"))
                {
                    await Db.UpdateAsync("invoice_drafts", draftId.Value, new Dictionary<string, object?>
                    {
                        ["status"] = "posted",
                        ["posted_invoice_id"] = invoiceId,
                        ["posted_invoice_type"] = "App\\Models\\PurchaseInvoice"
                    }, "id", conn, tx);
                }

                var cashPaid = totalPaid;
                if (invoiceType == "debit" && cashPaid <= 0)
                {
                    cashPaid = totalAmount;
                }
                if (cashPaid > 0)
                {
                    var account = await _settlementService.ResolveAccountAsync(ToNullableLong(rawData.GetValueOrDefault("account_id")), payMethod, conn, tx);
                    if (account != null)
                    {
                        var accId = ToLong(account["id"]);
                        var accName = account.GetValueOrDefault("name")?.ToString() ?? "Account";
                        await Db.UpdateAsync("purchase_invoices", invoiceId, new Dictionary<string, object?> { ["account_id"] = accId }, "id", conn, tx);
                        await _settlementService.WithdrawFromAccountAsync(
                            account,
                            cashPaid,
                            "purchase_invoice",
                            invoiceId,
                            $"Purchase Invoice {nextCode} ({accName})",
                            CurrentUserId,
                            conn,
                            tx);
                    }

                    if (ReturnSettlementService.IsDefaultCashDrawerAccount(account, payMethod))
                    {
                        await _settlementService.RecordCashDrawerTransactionAsync(
                            "cash_purchase",
                            cashPaid,
                            "purchase_invoice",
                            invoiceId,
                            $"Cash purchase {nextCode}",
                            CurrentUserId,
                            conn,
                            tx,
                            CurrentTerminalId);
                    }
                }

                await tx.CommitAsync();
                SyncPulseService.Bump("stock", "drafts", "drawer");

                var loaded = (await LoadPurchaseInvoiceAsync(invoiceId))!;
                await _auditService.RecordCreateAsync("App\\Models\\PurchaseInvoice", invoiceId, loaded, operatorUserId);
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
        if (!await CanAsync("purchase-invoice.update"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_invoices WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Purchase invoice not found." });

        if (string.Equals(existing.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
        {
            return Unprocessable("Cannot edit an invoice that has been reversed.");
        }

        var beforeSnapshot = (await LoadPurchaseInvoiceAsync(id)) ?? existing;
        var body = await ReadJsonBodyAsync();
        var (rawData, rawItems) = ParsePayloadAndItems(body);

        try
        {
            var calculated = _calculationService.CalculateAndValidatePurchase(rawData, rawItems);
            var calcItems = (List<Dictionary<string, object?>>)calculated["items"]!;

            var rawPin = GetString(body, "operator_pin") ?? Request.Headers["X-Operator-Pin"].FirstOrDefault();
            var op = await _operatorPinService.ResolveOperatorAsync(rawPin, CurrentUserId, "purchase_invoice");
            var operatorUserId = ToNullableLong(op.GetValueOrDefault("user_id")) ?? CurrentUserId;
            var operatorEmpId = ToNullableLong(op.GetValueOrDefault("employee_id"));
            var verifiedViaPin = op.GetValueOrDefault("verified_via_pin") is true;

            var supplierId = ToLong(rawData.GetValueOrDefault("supplier_id") ?? existing.GetValueOrDefault("supplier_id"));
            var invoiceNumber = (rawData.GetValueOrDefault("invoice_number")?.ToString() ?? existing.GetValueOrDefault("invoice_number")?.ToString() ?? "").Trim();
            var postedDate = rawData.GetValueOrDefault("posted_date")?.ToString() ?? existing.GetValueOrDefault("posted_date")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
            var postedNumber = existing.GetValueOrDefault("posted_number")?.ToString() ?? $"PRINV-{id:D4}";

            var dupInvCount = await Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM purchase_invoices WHERE invoice_number = @invNo AND id != @id",
                new { invNo = invoiceNumber, id });
            if (dupInvCount > 0)
            {
                return Unprocessable("Validation failed. Please correct the highlighted fields.", new Dictionary<string, string[]>
                {
                    ["invoice_number"] = new[] { "Invoice number has already been taken for this supplier or globally." }
                });
            }

            await using var conn = await Db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                var poId = ToNullableLong(existing.GetValueOrDefault("purchase_order_id"));
                if (poId.HasValue)
                {
                    await _poReceivingService.ValidateReceiptAsync(poId.Value, calcItems, conn, tx);
                }
                else
                {
                    foreach (var r in calcItems) r.Remove("purchase_order_item_id");
                }

                var allocatedOnInvoice = await GetAllocatedAmountForInvoiceAsync("purchase_invoice", id, conn, tx);
                var oldInvoiceType = (existing.GetValueOrDefault("invoice_type")?.ToString() ?? "debit").ToLowerInvariant();
                var newInvoiceType = (rawData.GetValueOrDefault("invoice_type")?.ToString() ?? oldInvoiceType).ToLowerInvariant();
                var totalAmount = ToDouble(calculated["total_amount"]);

                double totalPaid;
                if (allocatedOnInvoice > 0)
                {
                    var rawSubmittedPaid = Math.Round(ToDouble(rawData.GetValueOrDefault("total_paid")), 2);
                    if (rawSubmittedPaid <= Math.Round(totalAmount - allocatedOnInvoice + 0.009, 2))
                    {
                        totalPaid = Math.Round(rawSubmittedPaid + allocatedOnInvoice, 2);
                    }
                    else
                    {
                        totalPaid = rawSubmittedPaid;
                    }
                }
                else
                {
                    if (!rawData.ContainsKey("total_paid") || rawData["total_paid"] == null || string.IsNullOrWhiteSpace(rawData["total_paid"]?.ToString()))
                    {
                        totalPaid = newInvoiceType == "credit" ? 0.0 : totalAmount;
                    }
                    else
                    {
                        totalPaid = ToDouble(rawData["total_paid"]);
                    }
                }

                if (allocatedOnInvoice > 0.009 && (newInvoiceType != "credit" || supplierId != ToLong(existing.GetValueOrDefault("supplier_id"))))
                {
                    throw new InvoiceValidationException(
                        "Cannot change supplier or convert a credit invoice with active payment/debit-note allocations to cash/debit. Detach or reverse payments first.",
                        new Dictionary<string, string[]>
                        {
                            ["invoice_type"] = new[] { "Cannot change supplier or convert a credit invoice with active payment/debit-note allocations to cash/debit. Detach or reverse payments first." }
                        });
                }

                // Revert old stock and apply new stock
                var oldItems = await Db.QueryListAsync("SELECT * FROM purchase_invoice_items WHERE purchase_invoice_id = @id", new { id }, conn, tx);
                await ApplyPurchaseStockDiffAsync(oldItems, calcItems, supplierId, conn, tx);

                var dueDate = await ResolveDueDateAsync(rawData, supplierId, postedDate, conn, tx);
                var oldPayMethodStr = existing.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
                var newPayMethod = rawData.GetValueOrDefault("payment_method")?.ToString() ?? oldPayMethodStr;
                var explicitAccountId = rawData.ContainsKey("account_id") ? ToNullableLong(rawData.GetValueOrDefault("account_id")) : null;
                var resolvedAccountId = explicitAccountId
                    ?? (string.Equals(newPayMethod, oldPayMethodStr, StringComparison.OrdinalIgnoreCase)
                        ? ToNullableLong(existing.GetValueOrDefault("account_id"))
                        : null);

                var newAcc = await _settlementService.ResolveAccountAsync(resolvedAccountId, newPayMethod, conn, tx);
                if (newAcc != null)
                {
                    resolvedAccountId = ToLong(newAcc["id"]);
                }

                var updatePayload = new Dictionary<string, object?>
                {
                    ["supplier_id"] = supplierId,
                    ["invoice_type"] = newInvoiceType,
                    ["payment_method"] = newPayMethod,
                    ["account_id"] = resolvedAccountId,
                    ["posted_number"] = postedNumber,
                    ["posted_date"] = postedDate,
                    ["due_date"] = dueDate,
                    ["remarks"] = rawData.GetValueOrDefault("remarks"),
                    ["invoice_number"] = invoiceNumber,
                    ["invoice_amount"] = calculated["invoice_amount"],
                    ["tax_percentage"] = calculated["tax_percentage"],
                    ["tax_amount"] = calculated["tax_amount"],
                    ["discount_percentage"] = calculated["discount_percentage"],
                    ["discount_amount"] = calculated["discount_amount"],
                    ["total_amount"] = totalAmount,
                    ["total_paid"] = totalPaid
                };

                if (verifiedViaPin)
                {
                    updatePayload["user_id"] = operatorUserId;
                    updatePayload["employee_id"] = operatorEmpId;
                }

                await Db.UpdateAsync("purchase_invoices", id, updatePayload, "id", conn, tx);
                await Db.ExecuteAsync("DELETE FROM purchase_invoice_items WHERE purchase_invoice_id = @id", new { id }, conn, tx);

                foreach (var item in calcItems)
                {
                    var copy = new Dictionary<string, object?>(item, StringComparer.OrdinalIgnoreCase)
                    {
                        ["purchase_invoice_id"] = id
                    };
                    await Db.InsertAsync("purchase_invoice_items", copy, conn, tx);
                }

                // Mark pending/ordered user demands as fulfilled
                var receivedProdIds = calcItems
                    .Where(r => ToLong(r.GetValueOrDefault("quantity") ?? r.GetValueOrDefault("unit_quantity")) > 0)
                    .Select(r => ToLong(r.GetValueOrDefault("product_id")))
                    .Where(pid => pid > 0)
                    .Distinct()
                    .ToArray();

                if (receivedProdIds.Length > 0 && await Db.HasTableAsync("user_demands"))
                {
                    await Db.ExecuteAsync(
                        "UPDATE user_demands SET status = 'fulfilled', updated_at = @now WHERE product_id IN @ids AND status IN ('pending', 'ordered')",
                        new { ids = receivedProdIds, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                        conn,
                        tx);
                }

                // Update supplier ledger
                if (await Db.HasTableAsync("supplier_ledgers"))
                {
                    if (oldInvoiceType == "credit" && newInvoiceType != "credit")
                    {
                        await Db.ExecuteAsync("DELETE FROM supplier_ledgers WHERE purchase_invoice_id = @id", new { id }, conn, tx);
                        await Db.ExecuteAsync(
                            "DELETE FROM supplier_ledgers WHERE payment_ref = @pref AND entry_type = 'payment' AND payment_id IS NULL",
                            new { pref = "Inv #" + postedNumber },
                            conn,
                            tx);
                    }
                    else if (newInvoiceType == "credit")
                    {
                        await Db.ExecuteAsync(
                            "DELETE FROM supplier_ledgers WHERE payment_ref = @pref AND entry_type = 'payment' AND payment_id IS NULL",
                            new { pref = "Inv #" + postedNumber },
                            conn,
                            tx);

                        var invTotalAmount = Math.Round(totalAmount, 2);
                        var rawTotalPaid = Math.Round(totalPaid, 2);
                        var directPaid = Math.Max(0.0, Math.Round(rawTotalPaid - allocatedOnInvoice, 2));
                        var directPaidOnInv = Math.Min(directPaid, invTotalAmount);
                        var creditRemainingOnInv = Math.Max(0.0, Math.Round(invTotalAmount - directPaidOnInv, 2));
                        var excessPaid = Math.Max(0.0, Math.Round(directPaid - Math.Max(0.0, invTotalAmount - allocatedOnInvoice), 2));

                        var existingLedger = await Db.QuerySingleOrDefaultAsync(
                            "SELECT id FROM supplier_ledgers WHERE purchase_invoice_id = @id LIMIT 1",
                            new { id },
                            conn,
                            tx);

                        if (existingLedger != null)
                        {
                            await Db.UpdateAsync("supplier_ledgers", existingLedger["id"]!, new Dictionary<string, object?>
                            {
                                ["supplier_id"] = supplierId,
                                ["entry_date"] = postedDate,
                                ["due_date"] = dueDate,
                                ["invoice_number"] = invoiceNumber,
                                ["invoice_total"] = invTotalAmount,
                                ["total_paid"] = directPaidOnInv,
                                ["credit_remaining"] = creditRemainingOnInv,
                                ["description"] = "Purchase invoice " + postedNumber
                            }, "id", conn, tx);
                        }
                        else
                        {
                            await Db.InsertAsync("supplier_ledgers", new Dictionary<string, object?>
                            {
                                ["supplier_id"] = supplierId,
                                ["purchase_invoice_id"] = id,
                                ["entry_type"] = "invoice",
                                ["is_manual"] = 0,
                                ["entry_date"] = postedDate,
                                ["due_date"] = dueDate,
                                ["posted_number"] = postedNumber,
                                ["invoice_number"] = invoiceNumber,
                                ["invoice_total"] = invTotalAmount,
                                ["total_paid"] = directPaidOnInv,
                                ["debited_amount"] = 0,
                                ["credit_remaining"] = creditRemainingOnInv,
                                ["payment_ref"] = null,
                                ["description"] = "Purchase invoice " + postedNumber,
                                ["created_by"] = CurrentUserId
                            }, conn, tx);
                        }

                        if (excessPaid > 0)
                        {
                            await Db.InsertAsync("supplier_ledgers", new Dictionary<string, object?>
                            {
                                ["supplier_id"] = supplierId,
                                ["purchase_invoice_id"] = null,
                                ["entry_type"] = "payment",
                                ["is_manual"] = 0,
                                ["entry_date"] = postedDate,
                                ["posted_number"] = postedNumber,
                                ["invoice_number"] = invoiceNumber,
                                ["invoice_total"] = 0,
                                ["total_paid"] = 0,
                                ["debited_amount"] = excessPaid,
                                ["credit_remaining"] = 0,
                                ["payment_ref"] = "Inv #" + postedNumber,
                                ["description"] = "Payment made on invoice " + postedNumber,
                                ["created_by"] = CurrentUserId
                            }, conn, tx);
                        }
                    }
                }

                // Sync financial accounts & cash drawer on purchase invoice update
                static bool ParseFlag(object? v) => v is true or 1 or 1L || (v?.ToString()?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on");
                bool? updateDrawerFlag = null;
                if (rawData.TryGetValue("update_drawer", out var udObj) && udObj != null)
                    updateDrawerFlag = ParseFlag(udObj);
                else if (rawData.TryGetValue("update_drawer_if_pre_shift", out var udpObj) && udpObj != null)
                    updateDrawerFlag = ParseFlag(udpObj);

                var termId = CurrentTerminalId;
                var oldRawPaid = string.Equals(oldInvoiceType, "credit", StringComparison.OrdinalIgnoreCase)
                    ? ToDouble(existing.GetValueOrDefault("total_paid"))
                    : (ToDouble(existing.GetValueOrDefault("total_paid")) > 0 ? ToDouble(existing.GetValueOrDefault("total_paid")) : ToDouble(existing.GetValueOrDefault("total_amount")));
                var newRawPaid = newInvoiceType == "credit"
                    ? totalPaid
                    : (totalPaid > 0 ? totalPaid : totalAmount);
                var oldDirectPaid = Math.Max(0.0, Math.Round(oldRawPaid - allocatedOnInvoice, 2));
                var newDirectPaid = Math.Max(0.0, Math.Round(newRawPaid - allocatedOnInvoice, 2));

                var oldAcc = await _settlementService.ResolveAccountAsync(ToNullableLong(existing.GetValueOrDefault("account_id")), oldPayMethodStr, conn, tx);
                var oldAccId = oldAcc != null ? ToLong(oldAcc["id"]) : 0L;
                var newAccId = newAcc != null ? ToLong(newAcc["id"]) : 0L;

                // Remove same-shift cash drawer rows and recalculate expected balance before detecting pre-shift
                var removedOpenRows = await _settlementService.RemoveOpenCashDrawerTransactionsForRefAsync("purchase_invoice", id, conn, tx);
                var isPreShift = removedOpenRows == 0 && await _settlementService.IsPreShiftDocumentInTxAsync(
                    "purchase_invoice",
                    id,
                    existing.GetValueOrDefault("created_at")?.ToString(),
                    conn,
                    tx,
                    operatorUserId,
                    termId);

                // Reconstruct prior net withdrawals per account from account_transactions (fallback to oldAcc/oldDirectPaid)
                var oldNetByAcc = new Dictionary<long, double>();
                var accCache = new Dictionary<long, Dictionary<string, object?>>();
                if (oldAcc != null && oldAccId > 0) accCache[oldAccId] = oldAcc;
                if (newAcc != null && newAccId > 0) accCache[newAccId] = newAcc;

                if (await Db.HasTableAsync("account_transactions"))
                {
                    var prevTxns = await Db.QueryListAsync(
                        @"SELECT account_id, type, amount FROM account_transactions
                          WHERE reference_type IN ('purchase_invoice', 'purchase_invoice_update')
                            AND reference_id = @id",
                        new { id }, conn, tx);
                    foreach (var pt in prevTxns)
                    {
                        var aid = ToLong(pt.GetValueOrDefault("account_id"));
                        if (aid <= 0) continue;
                        var tp = (pt.GetValueOrDefault("type")?.ToString() ?? "").ToLowerInvariant();
                        var amt = ToDouble(pt.GetValueOrDefault("amount"));
                        // For purchase invoices, withdrawals are positive paid-out amounts, deposits are refunds
                        var signed = tp is "withdrawal" or "transfer_out" ? amt : -amt;
                        oldNetByAcc[aid] = Math.Round(oldNetByAcc.GetValueOrDefault(aid, 0.0) + signed, 2);
                    }
                }
                if (oldNetByAcc.Count == 0 && oldAccId > 0 && oldDirectPaid > 0.001)
                {
                    oldNetByAcc[oldAccId] = oldDirectPaid;
                }

                var newTargetByAcc = new Dictionary<long, double>();
                if (newAccId > 0 && newDirectPaid > 0.001)
                {
                    newTargetByAcc[newAccId] = newDirectPaid;
                }

                double oldDrawerAmt = 0.0;
                double newDrawerAmt = 0.0;

                foreach (var aid in oldNetByAcc.Keys.Union(newTargetByAcc.Keys).Distinct())
                {
                    if (!accCache.TryGetValue(aid, out var accRow))
                    {
                        accRow = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @aid", new { aid }, conn, tx);
                        if (accRow != null) accCache[aid] = accRow;
                    }
                    if (accRow == null) continue;

                    var oldAmt = oldNetByAcc.GetValueOrDefault(aid, 0.0);
                    var newAmt = newTargetByAcc.GetValueOrDefault(aid, 0.0);
                    var isDrawerAcc = ReturnSettlementService.IsDefaultCashDrawerAccount(
                        accRow,
                        aid == newAccId ? newPayMethod : (aid == oldAccId ? oldPayMethodStr : "cash"));

                    if (isDrawerAcc)
                    {
                        oldDrawerAmt = Math.Round(oldDrawerAmt + oldAmt, 2);
                        newDrawerAmt = Math.Round(newDrawerAmt + newAmt, 2);
                    }

                    // If pre-shift and caller opted NOT to touch the drawer, skip mutating default Cash in Hand symmetrically
                    if (isDrawerAcc && isPreShift && updateDrawerFlag == false)
                    {
                        continue;
                    }

                    var delta = Math.Round(newAmt - oldAmt, 2);
                    if (delta > 0.001)
                    {
                        await _settlementService.WithdrawFromAccountAsync(accRow, delta, "purchase_invoice_update", id, $"Edit payment increase for Purchase Invoice #{postedNumber}", operatorUserId, conn, tx);
                    }
                    else if (delta < -0.001)
                    {
                        await _settlementService.DepositToAccountAsync(accRow, Math.Abs(delta), "purchase_invoice_update", id, $"Edit payment reduction for Purchase Invoice #{postedNumber}", operatorUserId, conn, tx);
                    }
                }

                if (await Db.HasTableAsync("cash_drawer_transactions") && await Db.HasTableAsync("cash_drawer_sessions"))
                {
                    if (!isPreShift)
                    {
                        if (newDrawerAmt > 0.001)
                        {
                            await _settlementService.RecordCashDrawerTransactionAsync("cash_purchase", newDrawerAmt, "purchase_invoice", id, $"Purchase Invoice #{postedNumber}", operatorUserId, conn, tx, termId);
                        }
                    }
                    else if (updateDrawerFlag != false)
                    {
                        var drawerDelta = Math.Round(newDrawerAmt - oldDrawerAmt, 2);
                        if (drawerDelta > 0.001)
                        {
                            await _settlementService.RecordCashDrawerTransactionAsync("cash_purchase", drawerDelta, "purchase_invoice", id, $"Pre-shift edit adjustment for Purchase Invoice #{postedNumber}", operatorUserId, conn, tx, termId);
                        }
                        else if (drawerDelta < -0.001)
                        {
                            await _settlementService.RecordCashDrawerTransactionAsync("cash_in", Math.Abs(drawerDelta), "purchase_invoice", id, $"Pre-shift edit refund for Purchase Invoice #{postedNumber}", operatorUserId, conn, tx, termId);
                        }
                    }
                }

                if (poId.HasValue)
                {
                    await _poReceivingService.SyncAsync(poId.Value, CurrentUserId, conn, tx);
                    await _sortingService.RememberAsync(supplierId, calcItems.Select(i => ToLong(i.GetValueOrDefault("product_id"))), conn, tx);
                }

                await tx.CommitAsync();
                SyncPulseService.Bump("stock", "drawer");

                var loaded = (await LoadPurchaseInvoiceAsync(id))!;
                var reason = rawData.GetValueOrDefault("reason")?.ToString() ?? "Purchase invoice updated";
                await _auditService.RecordUpdateAsync("App\\Models\\PurchaseInvoice", id, beforeSnapshot, loaded, operatorUserId, reason);
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

    [HttpPost("{id:long}/reverse")]
    public async Task<IActionResult> Reverse(long id)
    {
        if (!await CanAsync("purchase-invoice.reverse", "purchase-invoice.delete", "purchase-invoice.update"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_invoices WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Purchase invoice not found." });

        var body = await ReadJsonBodyAsync();
        var reason = (GetString(body, "reason") ?? "").Trim();
        if (reason.Length < 3)
        {
            return Unprocessable("The reason field is required (min 3 characters).", new Dictionary<string, string[]>
            {
                ["reason"] = new[] { "The reason field is required (min 3 characters)." }
            });
        }

        if (string.Equals(existing.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
        {
            return Unprocessable("Invoice has already been reversed.");
        }

        if (await Db.HasTableAsync("purchase_returns"))
        {
            var hasReturns = await Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM purchase_returns WHERE purchase_invoice_id = @id",
                new { id }) > 0;
            if (hasReturns)
            {
                return Unprocessable("Cannot reverse this invoice because linked purchase returns exist. Please reverse or remove the returns first.");
            }
        }

        var beforeSnapshot = (await LoadPurchaseInvoiceAsync(id)) ?? existing;

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var items = await Db.QueryListAsync("SELECT * FROM purchase_invoice_items WHERE purchase_invoice_id = @id", new { id }, conn, tx);
            await ValidatePurchaseReversalStockAsync(items, conn, tx);

            foreach (var item in items)
            {
                await RevertPurchaseItemAsync(item, conn, tx);
            }

            await DetachInvoiceAllocationsAsync("purchase_invoice", id, conn, tx);

            var postedNumber = existing.GetValueOrDefault("posted_number")?.ToString() ?? "";
            if (await Db.HasTableAsync("supplier_ledgers"))
            {
                await Db.ExecuteAsync("DELETE FROM supplier_ledgers WHERE purchase_invoice_id = @id", new { id }, conn, tx);
                await Db.ExecuteAsync(
                    "DELETE FROM supplier_ledgers WHERE payment_ref = @pref AND entry_type = 'payment' AND payment_id IS NULL",
                    new { pref = "Inv #" + postedNumber },
                    conn,
                    tx);
            }

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await Db.UpdateAsync("purchase_invoices", id, new Dictionary<string, object?>
            {
                ["status"] = "reversed",
                ["reversed_at"] = now,
                ["reversed_by_id"] = CurrentUserId,
                ["reversal_reason"] = reason
            }, "id", conn, tx);

            var poId = ToNullableLong(existing.GetValueOrDefault("purchase_order_id"));
            if (poId.HasValue)
            {
                await _poReceivingService.SyncAsync(poId.Value, CurrentUserId, conn, tx);
            }

            // Refund account & cash drawer across all net direct withdrawals posted for this invoice
            var freshInv = (await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_invoices WHERE id = @id", new { id }, conn, tx))!;
            var termId = CurrentTerminalId;
            var netPaidByAcc = new Dictionary<long, double>();
            if (await Db.HasTableAsync("account_transactions"))
            {
                var priorTxns = await Db.QueryListAsync(
                    @"SELECT account_id, type, amount FROM account_transactions
                      WHERE reference_type IN ('purchase_invoice', 'purchase_invoice_update')
                        AND reference_id = @id",
                    new { id }, conn, tx);
                foreach (var pt in priorTxns)
                {
                    var aid = ToLong(pt.GetValueOrDefault("account_id"));
                    if (aid <= 0) continue;
                    var tp = (pt.GetValueOrDefault("type")?.ToString() ?? "").ToLowerInvariant();
                    var amt = ToDouble(pt.GetValueOrDefault("amount"));
                    var signed = tp is "withdrawal" or "transfer_out" ? amt : -amt;
                    netPaidByAcc[aid] = Math.Round(netPaidByAcc.GetValueOrDefault(aid, 0.0) + signed, 2);
                }
            }

            double cashDrawerRefund = 0.0;
            var payMethod = freshInv.GetValueOrDefault("payment_method")?.ToString() ?? "cash";

            if (netPaidByAcc.Count > 0)
            {
                foreach (var (aid, netAmt) in netPaidByAcc)
                {
                    if (netAmt <= 0.001) continue;
                    var acc = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @aid", new { aid }, conn, tx);
                    if (acc == null) continue;
                    await _settlementService.DepositToAccountAsync(
                        acc,
                        netAmt,
                        "purchase_invoice_reversal",
                        id,
                        $"Reversal refund for Purchase Invoice #{postedNumber}",
                        CurrentUserId,
                        conn,
                        tx);
                    if (ReturnSettlementService.IsDefaultCashDrawerAccount(acc, payMethod))
                    {
                        cashDrawerRefund = Math.Round(cashDrawerRefund + netAmt, 2);
                    }
                }
            }
            else
            {
                var revPaid = ToDouble(freshInv.GetValueOrDefault("total_paid"));
                var invType = (freshInv.GetValueOrDefault("invoice_type")?.ToString() ?? "debit").ToLowerInvariant();
                if (invType == "debit" && revPaid <= 0)
                {
                    revPaid = ToDouble(freshInv.GetValueOrDefault("total_amount"));
                }
                if (revPaid > 0.001)
                {
                    var acc = await _settlementService.ResolveAccountAsync(ToNullableLong(freshInv.GetValueOrDefault("account_id")), payMethod, conn, tx);
                    if (acc != null)
                    {
                        await _settlementService.DepositToAccountAsync(
                            acc,
                            revPaid,
                            "purchase_invoice_reversal",
                            id,
                            $"Reversal refund for Purchase Invoice #{postedNumber}",
                            CurrentUserId,
                            conn,
                            tx);
                    }
                    if (ReturnSettlementService.IsDefaultCashDrawerAccount(acc, payMethod))
                    {
                        cashDrawerRefund = revPaid;
                    }
                }
            }

            if (cashDrawerRefund > 0.001)
            {
                await _settlementService.RecordCashDrawerTransactionAsync(
                    "cash_in",
                    cashDrawerRefund,
                    "purchase_invoice_reversal",
                    id,
                    $"Reversal of Purchase Invoice #{postedNumber}: {reason}",
                    CurrentUserId,
                    conn,
                    tx,
                    termId);
            }

            await tx.CommitAsync();
            SyncPulseService.Bump("stock", "drawer");

            await _auditService.RecordReversalAsync("App\\Models\\PurchaseInvoice", id, beforeSnapshot, CurrentUserId, reason);
            var loaded = await LoadPurchaseInvoiceAsync(id);

            return Ok(new Dictionary<string, object?>
            {
                ["message"] = "Purchase invoice reversed successfully.",
                ["invoice"] = loaded
            });
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

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("purchase-invoice.delete"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_invoices WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Purchase invoice not found." });

        if (await Db.HasTableAsync("purchase_returns"))
        {
            var hasReturns = await Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM purchase_returns WHERE purchase_invoice_id = @id",
                new { id }) > 0;
            if (hasReturns)
            {
                return Unprocessable("Cannot delete this invoice because linked purchase returns exist. Please delete the returns first.");
            }
        }

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var items = await Db.QueryListAsync("SELECT * FROM purchase_invoice_items WHERE purchase_invoice_id = @id", new { id }, conn, tx);
            var isReversed = string.Equals(existing.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase);

            if (!isReversed)
            {
                await ValidatePurchaseReversalStockAsync(items, conn, tx);
                foreach (var item in items)
                {
                    await RevertPurchaseItemAsync(item, conn, tx);
                }

                await DetachInvoiceAllocationsAsync("purchase_invoice", id, conn, tx);

                var postedNumber = existing.GetValueOrDefault("posted_number")?.ToString() ?? "";
                if (await Db.HasTableAsync("supplier_ledgers"))
                {
                    await Db.ExecuteAsync("DELETE FROM supplier_ledgers WHERE purchase_invoice_id = @id", new { id }, conn, tx);
                    await Db.ExecuteAsync(
                        "DELETE FROM supplier_ledgers WHERE payment_ref = @pref AND entry_type = 'payment' AND payment_id IS NULL",
                        new { pref = "Inv #" + postedNumber },
                        conn,
                        tx);
                }

                var freshInv = (await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_invoices WHERE id = @id", new { id }, conn, tx))!;
                var termId = CurrentTerminalId;
                var netPaidByAcc = new Dictionary<long, double>();
                if (await Db.HasTableAsync("account_transactions"))
                {
                    var priorTxns = await Db.QueryListAsync(
                        @"SELECT account_id, type, amount FROM account_transactions
                          WHERE reference_type IN ('purchase_invoice', 'purchase_invoice_update')
                            AND reference_id = @id",
                        new { id }, conn, tx);
                    foreach (var pt in priorTxns)
                    {
                        var aid = ToLong(pt.GetValueOrDefault("account_id"));
                        if (aid <= 0) continue;
                        var tp = (pt.GetValueOrDefault("type")?.ToString() ?? "").ToLowerInvariant();
                        var amt = ToDouble(pt.GetValueOrDefault("amount"));
                        var signed = tp is "withdrawal" or "transfer_out" ? amt : -amt;
                        netPaidByAcc[aid] = Math.Round(netPaidByAcc.GetValueOrDefault(aid, 0.0) + signed, 2);
                    }
                }

                double cashDrawerRefunded = 0.0;
                var payMethod = freshInv.GetValueOrDefault("payment_method")?.ToString() ?? "cash";

                if (netPaidByAcc.Count > 0)
                {
                    foreach (var (aid, netAmt) in netPaidByAcc)
                    {
                        if (netAmt <= 0.001) continue;
                        var acc = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @aid", new { aid }, conn, tx);
                        if (acc == null) continue;
                        await _settlementService.DepositToAccountAsync(
                            acc,
                            netAmt,
                            "purchase_invoice_deletion",
                            id,
                            $"Refund for deleted purchase invoice #{postedNumber}",
                            CurrentUserId,
                            conn,
                            tx);
                        if (ReturnSettlementService.IsDefaultCashDrawerAccount(acc, payMethod))
                        {
                            cashDrawerRefunded = Math.Round(cashDrawerRefunded + netAmt, 2);
                        }
                    }
                }
                else
                {
                    var delPaid = ToDouble(freshInv.GetValueOrDefault("total_paid"));
                    var invType = (freshInv.GetValueOrDefault("invoice_type")?.ToString() ?? "debit").ToLowerInvariant();
                    if (invType == "debit" && delPaid <= 0)
                    {
                        delPaid = ToDouble(freshInv.GetValueOrDefault("total_amount"));
                    }
                    if (delPaid > 0.001)
                    {
                        var acc = await _settlementService.ResolveAccountAsync(ToNullableLong(freshInv.GetValueOrDefault("account_id")), payMethod, conn, tx);
                        if (acc != null)
                        {
                            await _settlementService.DepositToAccountAsync(
                                acc,
                                delPaid,
                                "purchase_invoice_deletion",
                                id,
                                $"Refund for deleted purchase invoice #{postedNumber}",
                                CurrentUserId,
                                conn,
                                tx);
                        }
                        if (ReturnSettlementService.IsDefaultCashDrawerAccount(acc, payMethod))
                        {
                            cashDrawerRefunded = delPaid;
                        }
                    }
                }

                var removedDrawerRows = await _settlementService.RemoveOpenCashDrawerTransactionsForRefAsync("purchase_invoice", id, conn, tx);
                if (removedDrawerRows == 0 && cashDrawerRefunded > 0.001)
                {
                    await _settlementService.RecordCashDrawerTransactionAsync(
                        "cash_in",
                        cashDrawerRefunded,
                        "purchase_invoice_deleted",
                        id,
                        $"Refund for deleted purchase invoice #{postedNumber}",
                        CurrentUserId,
                        conn,
                        tx,
                        termId);
                }
            }

            await Db.ExecuteAsync("DELETE FROM purchase_invoice_items WHERE purchase_invoice_id = @id", new { id }, conn, tx);
            await Db.ExecuteAsync("DELETE FROM purchase_invoices WHERE id = @id", new { id }, conn, tx);

            var poId = ToNullableLong(existing.GetValueOrDefault("purchase_order_id"));
            if (poId.HasValue)
            {
                await _poReceivingService.SyncAsync(poId.Value, CurrentUserId, conn, tx);
            }

            await tx.CommitAsync();
            SyncPulseService.Bump("stock", "drawer");

            return Ok(new Dictionary<string, object?> { ["message"] = "Invoice and related data deleted successfully" });
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

    // -------------------------------------------------------------------------
    // Internal Stock, Pricing & Batch Helpers
    // -------------------------------------------------------------------------

    private async Task ProcessInvoiceItemsAsync(
        long invoiceId,
        long supplierId,
        List<Dictionary<string, object?>> items,
        IDbConnection conn,
        IDbTransaction tx)
    {
        foreach (var item in items)
        {
            var copy = new Dictionary<string, object?>(item, StringComparer.OrdinalIgnoreCase)
            {
                ["purchase_invoice_id"] = invoiceId
            };
            await Db.InsertAsync("purchase_invoice_items", copy, conn, tx);

            var pid = ToLong(item.GetValueOrDefault("product_id"));
            var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @pid", new { pid }, conn, tx);
            if (product != null)
            {
                await ApplyPurchaseToProductAsync(product, item, conn, tx);
            }

            var receivedQty = (int)ToLong(item.GetValueOrDefault("quantity") ?? item.GetValueOrDefault("unit_quantity"));
            if (pid > 0 && receivedQty > 0 && await Db.HasTableAsync("user_demands"))
            {
                await Db.ExecuteAsync(
                    "UPDATE user_demands SET status = 'fulfilled', updated_at = @now WHERE product_id = @pid AND status IN ('pending', 'ordered')",
                    new { pid, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                    conn,
                    tx);
            }

            var batchNo = (item.GetValueOrDefault("batch")?.ToString() ?? "").Trim();
            var expiry = (item.GetValueOrDefault("expiry")?.ToString() ?? "").Trim();
            if (!string.IsNullOrEmpty(batchNo) && !string.IsNullOrEmpty(expiry) && await Db.HasTableAsync("batches"))
            {
                var existingBatch = await Db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM batches WHERE product_id = @pid AND batch_number = @bn AND expiry_date = @exp LIMIT 1",
                    new { pid, bn = batchNo, exp = expiry },
                    conn,
                    tx);

                if (existingBatch != null)
                {
                    var nextQty = (int)ToLong(existingBatch.GetValueOrDefault("quantity")) + (int)ToLong(item.GetValueOrDefault("quantity"));
                    var batchUpdates = new Dictionary<string, object?> { ["quantity"] = nextQty };
                    ApplyBatchPricingFields(batchUpdates, item, product);
                    await Db.UpdateAsync("batches", existingBatch["id"]!, batchUpdates, "id", conn, tx);
                }
                else
                {
                    var newBatch = new Dictionary<string, object?>
                    {
                        ["product_id"] = pid,
                        ["batch_number"] = batchNo,
                        ["expiry_date"] = expiry,
                        ["quantity"] = (int)ToLong(item.GetValueOrDefault("quantity")),
                        ["storage_location_id"] = item.GetValueOrDefault("storage_location_id") ?? product?.GetValueOrDefault("storage_location_id"),
                        ["rack"] = item.GetValueOrDefault("rack") ?? product?.GetValueOrDefault("rack")
                    };
                    ApplyBatchPricingFields(newBatch, item, product);
                    await Db.InsertAsync("batches", newBatch, conn, tx);
                }
            }

            if (pid > 0 && supplierId > 0)
            {
                await SyncProductSupplierLinkAsync(pid, supplierId, item, conn, tx);
            }
        }
    }

    private async Task ApplyPurchaseToProductAsync(
        Dictionary<string, object?> product,
        Dictionary<string, object?> item,
        IDbConnection conn,
        IDbTransaction tx)
    {
        var pid = ToLong(product["id"]);
        var currentQty = (int)ToLong(product.GetValueOrDefault("quantity"));
        var oldAvg = ToDouble(product.GetValueOrDefault("avg_price"));
        var newQty = Math.Max(0, (int)ToLong(item.GetValueOrDefault("quantity")));
        var effCost = ToDouble(item.GetValueOrDefault("avg_price") ?? item.GetValueOrDefault("unit_purchase_price"));

        var totalQty = currentQty + newQty;
        double nextAvg;

        if (currentQty < 0)
        {
            nextAvg = totalQty <= 0 ? 0.0 : Math.Round(Math.Max(effCost, 0.0), 2);
        }
        else
        {
            var oldValue = currentQty * oldAvg;
            var newValue = newQty * effCost;
            nextAvg = totalQty > 0 ? Math.Round(Math.Max((oldValue + newValue) / totalQty, 0.0), 2) : 0.0;
        }

        var updates = new Dictionary<string, object?>
        {
            ["quantity"] = totalQty,
            ["avg_price"] = nextAvg
        };

        foreach (var field in new[] { "pack_purchase_price", "unit_purchase_price", "pack_sale_price", "unit_sale_price", "whole_sale_pack_price", "whole_sale_unit_price" })
        {
            if (item.TryGetValue(field, out var fVal) && fVal != null && !string.IsNullOrWhiteSpace(fVal.ToString()))
            {
                updates[field] = ToDouble(fVal);
                product[field] = ToDouble(fVal);
            }
        }

        var retailSale = ToDouble(product.GetValueOrDefault("unit_sale_price"));
        var wholesaleSale = ToDouble(product.GetValueOrDefault("whole_sale_unit_price"));

        updates["margin"] = (retailSale > 0 && nextAvg > 0)
            ? Math.Round(((retailSale - nextAvg) / retailSale) * 100.0, 2)
            : 0.0;

        updates["whole_sale_margin"] = (wholesaleSale > 0 && nextAvg > 0)
            ? Math.Round(((wholesaleSale - nextAvg) / wholesaleSale) * 100.0, 2)
            : 0.0;

        await Db.UpdateAsync("products", pid, updates, "id", conn, tx);
    }

    private async Task RevertPurchaseItemAsync(
        Dictionary<string, object?> item,
        IDbConnection conn,
        IDbTransaction tx)
    {
        var pid = ToLong(item.GetValueOrDefault("product_id"));
        var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @pid", new { pid }, conn, tx);
        if (product != null)
        {
            var currentQty = (int)ToLong(product.GetValueOrDefault("quantity"));
            var currentAvg = ToDouble(product.GetValueOrDefault("avg_price"));
            var qtyToRemove = Math.Max(0, (int)ToLong(item.GetValueOrDefault("quantity")));
            var effCost = ToDouble(item.GetValueOrDefault("avg_price") ?? item.GetValueOrDefault("unit_purchase_price"));

            var remainingQty = currentQty - qtyToRemove;
            double nextAvg = 0.0;
            if (remainingQty > 0)
            {
                var currentValue = currentQty * currentAvg;
                var removedValue = qtyToRemove * effCost;
                var remainingValue = Math.Max(0.0, currentValue - removedValue);
                nextAvg = Math.Round(remainingValue / remainingQty, 2);
            }
            else
            {
                remainingQty = 0;
            }

            var retailSale = ToDouble(product.GetValueOrDefault("unit_sale_price"));
            var wholesaleSale = ToDouble(product.GetValueOrDefault("whole_sale_unit_price"));

            await Db.UpdateAsync("products", pid, new Dictionary<string, object?>
            {
                ["quantity"] = remainingQty,
                ["avg_price"] = nextAvg,
                ["margin"] = (retailSale > 0 && nextAvg > 0) ? Math.Round(((retailSale - nextAvg) / retailSale) * 100.0, 2) : 0.0,
                ["whole_sale_margin"] = (wholesaleSale > 0 && nextAvg > 0) ? Math.Round(((wholesaleSale - nextAvg) / wholesaleSale) * 100.0, 2) : 0.0
            }, "id", conn, tx);
        }

        var batchNo = (item.GetValueOrDefault("batch")?.ToString() ?? "").Trim();
        var expiry = (item.GetValueOrDefault("expiry")?.ToString() ?? "").Trim();
        if (!string.IsNullOrEmpty(batchNo) && !string.IsNullOrEmpty(expiry) && await Db.HasTableAsync("batches"))
        {
            var batch = await Db.QuerySingleOrDefaultAsync(
                "SELECT * FROM batches WHERE product_id = @pid AND batch_number = @bn AND expiry_date = @exp LIMIT 1",
                new { pid, bn = batchNo, exp = expiry },
                conn,
                tx);
            if (batch != null)
            {
                var nextQty = Math.Max(0, (int)ToLong(batch.GetValueOrDefault("quantity")) - (int)ToLong(item.GetValueOrDefault("quantity")));
                await Db.UpdateAsync("batches", batch["id"]!, new Dictionary<string, object?> { ["quantity"] = nextQty }, "id", conn, tx);
            }
        }
    }

    private async Task ApplyPurchaseStockDiffAsync(
        List<Dictionary<string, object?>> oldItems,
        List<Dictionary<string, object?>> newItems,
        long supplierId,
        IDbConnection conn,
        IDbTransaction tx)
    {
        // 1. Aggregate OLD vs NEW totals per product_id and per (product_id, batch, expiry) key
        var oldByProduct = new Dictionary<long, (int Qty, double TotalCost)>();
        foreach (var oi in oldItems)
        {
            var pid = ToLong(oi.GetValueOrDefault("product_id"));
            if (pid <= 0) continue;
            var q = Math.Max(0, (int)ToLong(oi.GetValueOrDefault("quantity")));
            var cost = ToDouble(oi.GetValueOrDefault("avg_price") ?? oi.GetValueOrDefault("unit_purchase_price"));
            var cur = oldByProduct.GetValueOrDefault(pid);
            oldByProduct[pid] = (cur.Qty + q, cur.TotalCost + q * cost);
        }

        var newByProduct = new Dictionary<long, (int Qty, double TotalCost, Dictionary<string, object?> LastItem)>();
        foreach (var ni in newItems)
        {
            var pid = ToLong(ni.GetValueOrDefault("product_id"));
            if (pid <= 0) continue;
            var q = Math.Max(0, (int)ToLong(ni.GetValueOrDefault("quantity")));
            var cost = ToDouble(ni.GetValueOrDefault("avg_price") ?? ni.GetValueOrDefault("unit_purchase_price"));
            var cur = newByProduct.GetValueOrDefault(pid);
            newByProduct[pid] = (cur.Qty + q, cur.TotalCost + q * cost, ni);
        }

        var allProductIds = oldByProduct.Keys.Union(newByProduct.Keys).Distinct().ToList();
        var productCache = new Dictionary<long, Dictionary<string, object?>>();

        // 2. Pre-validate EVERY net product-level reduction BEFORE mutating any stock
        foreach (var pid in allProductIds)
        {
            var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @pid", new { pid }, conn, tx);
            if (product == null) continue;
            productCache[pid] = product;

            var oldQty = oldByProduct.TryGetValue(pid, out var oInfo) ? oInfo.Qty : 0;
            var newQty = newByProduct.TryGetValue(pid, out var nInfo) ? nInfo.Qty : 0;
            var reduction = oldQty - newQty;
            var currentStock = (int)ToLong(product.GetValueOrDefault("quantity"));
            if (reduction > 0 && currentStock < reduction)
            {
                var pName = product.GetValueOrDefault("name")?.ToString() ?? $"Product #{pid}";
                var msg = $"Cannot reduce purchase quantity for {pName} by {reduction} units because only {currentStock} units remain in stock.";
                throw new InvoiceValidationException(msg, new Dictionary<string, string[]> { ["items"] = new[] { msg } });
            }
        }

        // 3. Aggregate & pre-validate EVERY net batch-level reduction BEFORE mutating batches
        static (long Pid, string Batch, string Expiry) BatchKey(Dictionary<string, object?> item) =>
            (ToLong(item.GetValueOrDefault("product_id")),
             (item.GetValueOrDefault("batch")?.ToString() ?? "").Trim(),
             (item.GetValueOrDefault("expiry")?.ToString() ?? "").Trim());

        var oldByBatch = new Dictionary<(long Pid, string Batch, string Expiry), int>();
        foreach (var oi in oldItems)
        {
            var key = BatchKey(oi);
            if (key.Pid <= 0 || string.IsNullOrEmpty(key.Batch) || string.IsNullOrEmpty(key.Expiry)) continue;
            oldByBatch[key] = oldByBatch.GetValueOrDefault(key) + Math.Max(0, (int)ToLong(oi.GetValueOrDefault("quantity")));
        }

        var newByBatch = new Dictionary<(long Pid, string Batch, string Expiry), (int Qty, Dictionary<string, object?> LastItem)>();
        foreach (var ni in newItems)
        {
            var key = BatchKey(ni);
            if (key.Pid <= 0 || string.IsNullOrEmpty(key.Batch) || string.IsNullOrEmpty(key.Expiry)) continue;
            var cur = newByBatch.GetValueOrDefault(key);
            newByBatch[key] = (cur.Qty + Math.Max(0, (int)ToLong(ni.GetValueOrDefault("quantity"))), ni);
        }

        var hasBatchesTable = await Db.HasTableAsync("batches");
        if (hasBatchesTable)
        {
            foreach (var kv in oldByBatch)
            {
                var newBatchQty = newByBatch.TryGetValue(kv.Key, out var nb) ? nb.Qty : 0;
                var reduction = kv.Value - newBatchQty;
                if (reduction <= 0) continue;

                var batchRow = await Db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM batches WHERE product_id = @pid AND batch_number = @bn AND expiry_date = @exp LIMIT 1",
                    new { pid = kv.Key.Pid, bn = kv.Key.Batch, exp = kv.Key.Expiry },
                    conn,
                    tx);
                var currentBatchQty = batchRow != null ? (int)ToLong(batchRow.GetValueOrDefault("quantity")) : 0;
                if (currentBatchQty < reduction)
                {
                    var pName = productCache.TryGetValue(kv.Key.Pid, out var pr) ? (pr.GetValueOrDefault("name")?.ToString() ?? $"Product #{kv.Key.Pid}") : $"Product #{kv.Key.Pid}";
                    var msg = $"Cannot reduce batch '{kv.Key.Batch}' of {pName} by {reduction} units because only {currentBatchQty} units remain in that batch.";
                    throw new InvoiceValidationException(msg, new Dictionary<string, string[]> { ["items"] = new[] { msg } });
                }
            }
        }

        // 4. Apply true NET DELTAS per product (preserves sold units & weighted average cost accurately!)
        foreach (var pid in allProductIds)
        {
            if (!productCache.TryGetValue(pid, out var product)) continue;

            var oldInfo = oldByProduct.GetValueOrDefault(pid);
            var hasNew = newByProduct.TryGetValue(pid, out var newInfo);
            var currentQty = (int)ToLong(product.GetValueOrDefault("quantity"));
            var currentAvg = ToDouble(product.GetValueOrDefault("avg_price"));
            var netQtyDelta = (hasNew ? newInfo.Qty : 0) - oldInfo.Qty;
            var nextQty = Math.Max(0, currentQty + netQtyDelta);

            double nextAvg;
            if (nextQty <= 0)
            {
                nextAvg = 0.0;
            }
            else
            {
                var currentVal = currentQty * currentAvg;
                var netValDelta = (hasNew ? newInfo.TotalCost : 0.0) - oldInfo.TotalCost;
                var nextVal = Math.Max(0.0, currentVal + netValDelta);
                nextAvg = Math.Round(nextVal / nextQty, 2);
                if (nextAvg <= 0 && hasNew && newInfo.Qty > 0)
                {
                    nextAvg = Math.Round(newInfo.TotalCost / newInfo.Qty, 2);
                }
            }

            var updates = new Dictionary<string, object?>
            {
                ["quantity"] = nextQty,
                ["avg_price"] = nextAvg
            };

            if (hasNew && newInfo.LastItem != null)
            {
                var item = newInfo.LastItem;
                var packSize = Math.Max(1, (int)ToLong(item.GetValueOrDefault("pack_size") ?? product.GetValueOrDefault("pack_size") ?? 1));
                var packPur = ToDouble(item.GetValueOrDefault("pack_purchase_price"));
                var unitPur = ToDouble(item.GetValueOrDefault("unit_purchase_price"));
                var packSale = ToDouble(item.GetValueOrDefault("pack_sale_price"));
                var unitSale = ToDouble(item.GetValueOrDefault("unit_sale_price"));
                var wsPack = ToDouble(item.GetValueOrDefault("whole_sale_pack_price"));
                var wsUnit = ToDouble(item.GetValueOrDefault("whole_sale_unit_price"));

                if (packPur > 0) updates["pack_purchase_price"] = packPur;
                if (unitPur > 0) updates["unit_purchase_price"] = unitPur;
                if (packSale > 0) updates["pack_sale_price"] = packSale;
                if (unitSale > 0) updates["unit_sale_price"] = unitSale;
                if (wsPack > 0) updates["whole_sale_pack_price"] = wsPack;
                if (wsUnit > 0) updates["whole_sale_unit_price"] = wsUnit;
                updates["pack_size"] = packSize;
            }

            var effRetailSale = updates.TryGetValue("unit_sale_price", out var rsObj) ? ToDouble(rsObj) : ToDouble(product.GetValueOrDefault("unit_sale_price"));
            var effWsSale = updates.TryGetValue("whole_sale_unit_price", out var wsObj) ? ToDouble(wsObj) : ToDouble(product.GetValueOrDefault("whole_sale_unit_price"));
            updates["margin"] = (effRetailSale > 0 && nextAvg > 0) ? Math.Round(((effRetailSale - nextAvg) / effRetailSale) * 100.0, 2) : 0.0;
            updates["whole_sale_margin"] = (effWsSale > 0 && nextAvg > 0) ? Math.Round(((effWsSale - nextAvg) / effWsSale) * 100.0, 2) : 0.0;

            await Db.UpdateAsync("products", pid, updates, "id", conn, tx);
        }

        // 5. Apply true NET DELTAS per batch (preserving storage_location_id & rack on newly created batches!)
        if (hasBatchesTable)
        {
            var allBatchKeys = oldByBatch.Keys.Union(newByBatch.Keys).Distinct().ToList();
            foreach (var key in allBatchKeys)
            {
                var oldQty = oldByBatch.GetValueOrDefault(key);
                var hasNew = newByBatch.TryGetValue(key, out var newBatchInfo);
                var delta = (hasNew ? newBatchInfo.Qty : 0) - oldQty;

                var existingBatch = await Db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM batches WHERE product_id = @pid AND batch_number = @bn AND expiry_date = @exp LIMIT 1",
                    new { pid = key.Pid, bn = key.Batch, exp = key.Expiry },
                    conn,
                    tx);

                productCache.TryGetValue(key.Pid, out var prodRow);
                if (existingBatch != null)
                {
                    var nextQty = Math.Max(0, (int)ToLong(existingBatch.GetValueOrDefault("quantity")) + delta);
                    var batchUpdates = new Dictionary<string, object?> { ["quantity"] = nextQty };
                    if (hasNew && newBatchInfo.LastItem != null)
                    {
                        ApplyBatchPricingFields(batchUpdates, newBatchInfo.LastItem, prodRow);
                    }
                    await Db.UpdateAsync("batches", existingBatch["id"]!, batchUpdates, "id", conn, tx);
                }
                else if (hasNew && newBatchInfo.Qty > 0)
                {
                    var newBatch = new Dictionary<string, object?>
                    {
                        ["product_id"] = key.Pid,
                        ["batch_number"] = key.Batch,
                        ["expiry_date"] = key.Expiry,
                        ["quantity"] = newBatchInfo.Qty,
                        ["storage_location_id"] = ToNullableLong(newBatchInfo.LastItem?.GetValueOrDefault("storage_location_id")) ?? ToNullableLong(prodRow?.GetValueOrDefault("storage_location_id")),
                        ["rack"] = newBatchInfo.LastItem?.GetValueOrDefault("rack") ?? prodRow?.GetValueOrDefault("rack")
                    };
                    if (newBatchInfo.LastItem != null)
                    {
                        ApplyBatchPricingFields(newBatch, newBatchInfo.LastItem, prodRow);
                    }
                    await Db.InsertAsync("batches", newBatch, conn, tx);
                }
            }
        }

        // 6. Sync product_suppliers link for all updated items
        foreach (var newItem in newItems)
        {
            var pid = ToLong(newItem.GetValueOrDefault("product_id"));
            if (pid > 0 && supplierId > 0)
            {
                await SyncProductSupplierLinkAsync(pid, supplierId, newItem, conn, tx);
            }
        }
    }

    private async Task ValidatePurchaseReversalStockAsync(
        List<Dictionary<string, object?>> items,
        IDbConnection conn,
        IDbTransaction tx)
    {
        var byProduct = new Dictionary<long, int>();
        var byBatch = new Dictionary<string, (long ProductId, string Batch, string Expiry, int Qty)>();

        foreach (var item in items)
        {
            var pid = ToLong(item.GetValueOrDefault("product_id"));
            var qty = (int)ToLong(item.GetValueOrDefault("quantity"));
            if (pid <= 0 || qty <= 0) continue;

            byProduct[pid] = byProduct.GetValueOrDefault(pid, 0) + qty;

            var batchNo = (item.GetValueOrDefault("batch")?.ToString() ?? "").Trim();
            var expiry = (item.GetValueOrDefault("expiry")?.ToString() ?? "").Trim();
            if (!string.IsNullOrEmpty(batchNo) && !string.IsNullOrEmpty(expiry))
            {
                var bKey = $"{pid}|{batchNo}|{expiry}";
                var prev = byBatch.TryGetValue(bKey, out var ex) ? ex.Qty : 0;
                byBatch[bKey] = (pid, batchNo, expiry, prev + qty);
            }
        }

        foreach (var kv in byProduct)
        {
            var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @pid", new { pid = kv.Key }, conn, tx);
            if (product != null && (int)ToLong(product.GetValueOrDefault("quantity")) < kv.Value)
            {
                var pName = product.GetValueOrDefault("name")?.ToString() ?? $"Product #{kv.Key}";
                var msg = $"Cannot reverse or delete purchase invoice because stock for {pName} has already been sold or consumed (on hand: {product["quantity"]}, required: {kv.Value}).";
                throw new InvoiceValidationException(msg, new Dictionary<string, string[]> { ["items"] = new[] { msg } });
            }
        }

        if (await Db.HasTableAsync("batches"))
        {
            foreach (var bInfo in byBatch.Values)
            {
                var batch = await Db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM batches WHERE product_id = @pid AND batch_number = @bn AND expiry_date = @exp LIMIT 1",
                    new { pid = bInfo.ProductId, bn = bInfo.Batch, exp = bInfo.Expiry },
                    conn,
                    tx);
                if (batch != null && (int)ToLong(batch.GetValueOrDefault("quantity")) < bInfo.Qty)
                {
                    var pName = await Db.ExecuteScalarAsync<string?>("SELECT name FROM products WHERE id = @pid", new { pid = bInfo.ProductId }, conn, tx)
                                ?? $"Product #{bInfo.ProductId}";
                    var msg = $"Cannot reverse or delete purchase invoice because batch {bInfo.Batch} for {pName} has already been sold or consumed (on hand: {batch["quantity"]}, required: {bInfo.Qty}).";
                    throw new InvoiceValidationException(msg, new Dictionary<string, string[]> { ["items"] = new[] { msg } });
                }
            }
        }
    }

    private static void ApplyBatchPricingFields(
        Dictionary<string, object?> target,
        Dictionary<string, object?> item,
        Dictionary<string, object?>? product)
    {
        foreach (var field in new[] { "pack_purchase_price", "unit_purchase_price", "pack_sale_price", "unit_sale_price", "whole_sale_pack_price", "whole_sale_unit_price" })
        {
            if (item.TryGetValue(field, out var val) && val != null && !string.IsNullOrWhiteSpace(val.ToString()))
            {
                target[field] = ToDouble(val);
            }
            else if (!target.ContainsKey(field) && product != null && product.TryGetValue(field, out var pVal) && pVal != null)
            {
                target[field] = ToDouble(pVal);
            }
        }
    }

    private async Task SyncProductSupplierLinkAsync(
        long productId,
        long supplierId,
        Dictionary<string, object?> item,
        IDbConnection conn,
        IDbTransaction tx)
    {
        if (productId <= 0 || supplierId <= 0 || !await Db.HasTableAsync("product_suppliers")) return;

        var unitCost = ToDouble(item.GetValueOrDefault("unit_purchase_price") ?? item.GetValueOrDefault("avg_price"));
        var existing = await Db.QuerySingleOrDefaultAsync(
            "SELECT * FROM product_suppliers WHERE product_id = @pid AND supplier_id = @sid LIMIT 1",
            new { pid = productId, sid = supplierId },
            conn,
            tx);

        if (existing != null)
        {
            if (unitCost > 0)
            {
                await Db.UpdateAsync("product_suppliers", existing["id"]!, new Dictionary<string, object?>
                {
                    ["last_purchase_price"] = unitCost
                }, "id", conn, tx);
            }
        }
        else
        {
            var hasAny = await Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM product_suppliers WHERE product_id = @pid",
                new { pid = productId },
                conn,
                tx) > 0;

            await Db.InsertAsync("product_suppliers", new Dictionary<string, object?>
            {
                ["product_id"] = productId,
                ["supplier_id"] = supplierId,
                ["last_purchase_price"] = unitCost > 0 ? unitCost : null,
                ["min_order_qty"] = 1,
                ["is_preferred"] = hasAny ? 0 : 1
            }, conn, tx);
        }
    }

    private async Task<string> ResolveDueDateAsync(
        Dictionary<string, object?> data,
        long supplierId,
        string postedDate,
        IDbConnection conn,
        IDbTransaction tx)
    {
        var explicitDue = data.GetValueOrDefault("due_date")?.ToString();
        if (!string.IsNullOrWhiteSpace(explicitDue) && DateTime.TryParse(explicitDue, out var dt))
        {
            return dt.ToString("yyyy-MM-dd");
        }

        int creditDays = 30;
        if (supplierId > 0 && await Db.HasTableAsync("suppliers"))
        {
            var sup = await Db.QuerySingleOrDefaultAsync("SELECT credit_days FROM suppliers WHERE id = @id", new { id = supplierId }, conn, tx);
            if (sup != null && sup.GetValueOrDefault("credit_days") != null)
            {
                creditDays = (int)ToLong(sup["credit_days"]);
            }
        }

        var baseDt = DateTime.TryParse(postedDate, out var pDt) ? pDt : DateTime.UtcNow;
        return baseDt.AddDays(creditDays).ToString("yyyy-MM-dd");
    }

    private async Task<double> GetAllocatedAmountForInvoiceAsync(string invoiceType, long invoiceId, IDbConnection conn, IDbTransaction tx)
    {
        if (!await Db.HasTableAsync("payment_allocations") || !await Db.HasTableAsync("payments")) return 0.0;

        var sum = await Db.ExecuteScalarAsync<double?>(@"
            SELECT COALESCE(SUM(pa.amount), 0)
            FROM payment_allocations pa
            INNER JOIN payments p ON p.id = pa.payment_id
            WHERE pa.invoice_type = @invoiceType AND pa.invoice_id = @invoiceId AND (p.status IS NULL OR p.status != 'reversed')",
            new { invoiceType, invoiceId },
            conn,
            tx);

        return Math.Round(sum ?? 0.0, 2);
    }

    private async Task DetachInvoiceAllocationsAsync(string invoiceType, long invoiceId, IDbConnection conn, IDbTransaction tx)
    {
        if (!await Db.HasTableAsync("payment_allocations") || !await Db.HasTableAsync("payments")) return;

        var allocs = await Db.QueryListAsync(@"
            SELECT pa.*
            FROM payment_allocations pa
            LEFT JOIN payments p ON p.id = pa.payment_id
            WHERE pa.invoice_type = @invoiceType
              AND pa.invoice_id = @invoiceId
              AND (p.status IS NULL OR LOWER(p.status) != 'reversed')",
            new { invoiceType, invoiceId },
            conn,
            tx);

        foreach (var alloc in allocs)
        {
            var paymentId = ToLong(alloc.GetValueOrDefault("payment_id"));
            var amt = ToDouble(alloc.GetValueOrDefault("amount"));

            if (paymentId > 0)
            {
                await Db.ExecuteAsync(@"
                    UPDATE payments
                    SET allocated_amount = ROUND(MAX(COALESCE(allocated_amount, 0) - @amt, 0), 2),
                        unapplied_amount = ROUND(COALESCE(unapplied_amount, 0) + @amt, 2)
                    WHERE id = @paymentId",
                    new { amt, paymentId },
                    conn,
                    tx);
            }

            if (invoiceType == "purchase_invoice")
            {
                await Db.ExecuteAsync(
                    "UPDATE purchase_invoices SET total_paid = ROUND(MAX(COALESCE(total_paid, 0) - @amt, 0), 2) WHERE id = @invoiceId",
                    new { amt, invoiceId },
                    conn,
                    tx);
            }
            else if (invoiceType == "sale_invoice")
            {
                await Db.ExecuteAsync(
                    "UPDATE sale_invoices SET total_receive = ROUND(MAX(COALESCE(total_receive, 0) - @amt, 0), 2) WHERE id = @invoiceId",
                    new { amt, invoiceId },
                    conn,
                    tx);
            }

            await Db.ExecuteAsync("DELETE FROM payment_allocations WHERE id = @id", new { id = alloc["id"] }, conn, tx);
        }
    }

    private async Task<Dictionary<string, object?>?> LoadPurchaseInvoiceAsync(long id, long? excludeReturnId = null)
    {
        var inv = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_invoices WHERE id = @id", new { id });
        if (inv == null) return null;

        await EnrichInvoiceBatchAsync(new List<Dictionary<string, object?>> { inv });

        var items = await Db.QueryListAsync("SELECT * FROM purchase_invoice_items WHERE purchase_invoice_id = @id", new { id });
        var prodIds = items.Select(i => ToLong(i.GetValueOrDefault("product_id"))).Where(pid => pid > 0).Distinct().ToArray();

        var prods = new Dictionary<long, Dictionary<string, object?>>();
        if (prodIds.Length > 0 && await Db.HasTableAsync("products"))
        {
            var pRows = await Db.QueryListAsync("SELECT * FROM products WHERE id IN @ids", new { ids = prodIds });
            var brandIds = pRows.Select(p => ToLong(p.GetValueOrDefault("brand_id"))).Where(bid => bid > 0).Distinct().ToArray();
            var brands = new Dictionary<long, Dictionary<string, object?>>();
            if (brandIds.Length > 0 && await Db.HasTableAsync("brands"))
            {
                var bRows = await Db.QueryListAsync("SELECT * FROM brands WHERE id IN @ids", new { ids = brandIds });
                foreach (var b in bRows) brands[ToLong(b["id"])] = b;
            }

            foreach (var p in pRows)
            {
                var bid = ToLong(p.GetValueOrDefault("brand_id"));
                p["brand"] = bid > 0 && brands.TryGetValue(bid, out var b) ? b : null;
                prods[ToLong(p["id"])] = p;
            }
        }

        foreach (var it in items)
        {
            var pid = ToLong(it.GetValueOrDefault("product_id"));
            it["product"] = pid > 0 && prods.TryGetValue(pid, out var p) ? p : null;
        }

        await _entitlementService.AnnotateAsync("purchase", id, items, excludeReturnId);
        inv["items"] = items;

        var accId = ToLong(inv.GetValueOrDefault("account_id"));
        inv["account"] = accId > 0 && await Db.HasTableAsync("accounts")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accId })
            : null;

        var poId = ToLong(inv.GetValueOrDefault("purchase_order_id"));
        var po = poId > 0 && await Db.HasTableAsync("purchase_orders")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_orders WHERE id = @id", new { id = poId })
            : null;
        inv["purchase_order"] = po;
        inv["purchaseOrder"] = po;

        return inv;
    }

    private async Task<List<Dictionary<string, object?>>> EnrichInvoiceBatchAsync(List<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return rows;

        var supIds = rows.Select(r => ToLong(r.GetValueOrDefault("supplier_id"))).Where(id => id > 0).Distinct().ToArray();
        var userIds = rows.Select(r => ToLong(r.GetValueOrDefault("user_id"))).Where(id => id > 0).Distinct().ToArray();
        var empIds = rows.Select(r => ToLong(r.GetValueOrDefault("employee_id"))).Where(id => id > 0).Distinct().ToArray();

        var suppliers = new Dictionary<long, Dictionary<string, object?>>();
        if (supIds.Length > 0 && await Db.HasTableAsync("suppliers"))
        {
            var sRows = await Db.QueryListAsync("SELECT * FROM suppliers WHERE id IN @ids", new { ids = supIds });
            foreach (var sr in sRows) suppliers[ToLong(sr["id"])] = sr;
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
            var uid = ToLong(r.GetValueOrDefault("user_id"));
            var eid = ToLong(r.GetValueOrDefault("employee_id"));

            r["supplier"] = sid > 0 && suppliers.TryGetValue(sid, out var s) ? s : null;
            r["user"] = uid > 0 && users.TryGetValue(uid, out var u) ? u : null;
            r["employee"] = eid > 0 && employees.TryGetValue(eid, out var e) ? e : null;
        }

        return rows;
    }

    public static (Dictionary<string, object?> Data, List<Dictionary<string, object?>> Items) ParsePayloadAndItems(Dictionary<string, JsonElement> body)
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

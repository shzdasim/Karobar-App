using System.Data;
using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/sale-invoices")]
public class SaleInvoiceController : ApiControllerBase
{
    private readonly DocumentSequenceService _sequenceService;
    private readonly InvoiceCalculationService _calculationService;
    private readonly SaleStockAvailabilityService _stockAvailabilityService;
    private readonly SaleCostSnapshotService _costSnapshotService;
    private readonly ReturnEntitlementService _entitlementService;
    private readonly ReturnSettlementService _settlementService;
    private readonly DocumentAuditService _auditService;
    private readonly OperatorPinService _operatorPinService;

    public SaleInvoiceController(KarobarDb db) : base(db)
    {
        _sequenceService = new DocumentSequenceService(db);
        _calculationService = new InvoiceCalculationService(db);
        _stockAvailabilityService = new SaleStockAvailabilityService(db);
        _costSnapshotService = new SaleCostSnapshotService(db);
        _entitlementService = new ReturnEntitlementService(db);
        _settlementService = new ReturnSettlementService(db);
        _auditService = new DocumentAuditService(db);
        _operatorPinService = new OperatorPinService(db);
    }

    [HttpGet("new-code")]
    public async Task<IActionResult> GenerateNewCode()
    {
        if (!await CanAsync("sale-invoice.create", "barcode-pos.create"))
        {
            return Forbidden();
        }

        var code = await _sequenceService.PreviewAsync(
            "sale_invoice",
            "SI-",
            6,
            "sale_invoices",
            "posted_number",
            true);

        return Ok(new Dictionary<string, object?> { ["posted_number"] = code });
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q = null,
        [FromQuery] long? customer_id = null,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 20)
    {
        if (!await CanAsync("sale-invoice.view", "sale-return.create", "sale-return.update", "payment.create", "barcode-pos.view"))
        {
            return Forbidden();
        }

        var where = new List<string> { "1=1" };
        var dyn = new DynamicParameters();

        if (customer_id.HasValue && customer_id.Value > 0)
        {
            where.Add("si.customer_id = @cid");
            dyn.Add("cid", customer_id.Value);
        }

        var qTrim = (q ?? "").Trim();
        if (!string.IsNullOrEmpty(qTrim))
        {
            where.Add(@"(
                si.posted_number LIKE @qLike
                OR EXISTS (SELECT 1 FROM customers c WHERE c.id = si.customer_id AND c.name LIKE @qLike)
            )");
            dyn.Add("qLike", "%" + qTrim + "%");
        }

        var whereSql = string.Join(" AND ", where);
        var selectSql = $"SELECT si.* FROM sale_invoices si WHERE {whereSql} ORDER BY si.id DESC";
        var countSql = $"SELECT COUNT(1) FROM sale_invoices si WHERE {whereSql}";

        var paged = await Db.PaginateAsync(selectSql, countSql, dyn, page, per_page, async rows =>
        {
            await EnrichInvoiceBatchAsync(rows);
            foreach (var r in rows)
            {
                var invTotal = ToDouble(r.GetValueOrDefault("total"));
                var received = ToDouble(r.GetValueOrDefault("total_receive"));
                r["remaining"] = Math.Max(0.0, invTotal - received);
                var cust = r.GetValueOrDefault("customer") as Dictionary<string, object?>;
                r["customer_name"] = cust?.GetValueOrDefault("name")?.ToString() ?? r.GetValueOrDefault("customer_id")?.ToString() ?? "N/A";
            }
            return rows;
        });

        return Ok(paged);
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] long? customer_id = null,
        [FromQuery] string? posted = null,
        [FromQuery] string? customer = null,
        [FromQuery] string? sale_type = null,
        [FromQuery] string? invoice_type = null,
        [FromQuery] int page = 1,
        [FromQuery] int per_page = 10)
    {
        if (customer_id.HasValue && customer_id.Value > 0)
        {
            if (!await CanAsync("sale-invoice.view", "sale-return.create", "sale-return.update", "payment.create", "barcode-pos.view"))
            {
                return Forbidden();
            }
        }
        else if (!await CanAsync("sale-invoice.view", "barcode-pos.view"))
        {
            return Forbidden();
        }

        var where = new List<string> { "1=1" };
        var dyn = new DynamicParameters();

        var normSaleType = (sale_type ?? "").Trim().ToLowerInvariant();
        var normInvoiceType = (invoice_type ?? "").Trim().ToLowerInvariant();

        if (customer_id.HasValue && customer_id.Value > 0)
        {
            where.Add("si.customer_id = @cid");
            dyn.Add("cid", customer_id.Value);
        }
        if (normSaleType is "retail" or "wholesale")
        {
            where.Add("LOWER(COALESCE(si.sale_type, 'retail')) = @saleType");
            dyn.Add("saleType", normSaleType);
        }
        if (normInvoiceType is "credit" or "debit")
        {
            where.Add("LOWER(COALESCE(si.invoice_type, 'debit')) = @invoiceType");
            dyn.Add("invoiceType", normInvoiceType);
        }
        if (!string.IsNullOrWhiteSpace(posted))
        {
            where.Add("si.posted_number LIKE @posted");
            dyn.Add("posted", "%" + posted.Trim() + "%");
        }
        if (!string.IsNullOrWhiteSpace(customer))
        {
            where.Add("EXISTS (SELECT 1 FROM customers c WHERE c.id = si.customer_id AND c.name LIKE @customer)");
            dyn.Add("customer", "%" + customer.Trim() + "%");
        }

        var whereSql = string.Join(" AND ", where);
        var selectSql = $"SELECT si.* FROM sale_invoices si WHERE {whereSql} ORDER BY si.id DESC";
        var countSql = $"SELECT COUNT(1) FROM sale_invoices si WHERE {whereSql}";

        per_page = Math.Max(1, Math.Min(per_page, 200));
        var paged = await Db.PaginateAsync(selectSql, countSql, dyn, page, per_page, EnrichInvoiceBatchAsync);
        return Ok(paged);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id, [FromQuery] long? exclude_return_id = null)
    {
        if (!await CanAsync("sale-invoice.view", "sale-return.create", "sale-return.update", "barcode-pos.view"))
        {
            return Forbidden();
        }

        var loaded = await LoadSaleInvoiceAsync(id, exclude_return_id);
        if (loaded == null) return NotFound(new { message = "Sale invoice not found." });

        return Ok(loaded);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("sale-invoice.create", "barcode-pos.create"))
        {
            return Forbidden();
        }
        if (!CurrentUserId.HasValue)
        {
            return Unauthorized(new { message = "Unauthenticated" });
        }

        var body = await ReadJsonBodyAsync();
        var (rawData, rawItems, splitPayments) = ParseSalePayload(body);
        var rawPin = GetString(body, "operator_pin") ?? Request.Headers["X-Operator-Pin"].FirstOrDefault();

        return await ExecuteStoreSaleInvoiceAsync(rawData, rawItems, splitPayments, rawPin);
    }

    [NonAction]
    public async Task<IActionResult> ExecuteStoreSaleInvoiceAsync(
        Dictionary<string, object?> rawData,
        List<Dictionary<string, object?>> rawItems,
        List<Dictionary<string, object?>>? splitPayments = null,
        string? rawPin = null)
    {
        splitPayments ??= new List<Dictionary<string, object?>>();

        var customerId = ToLong(rawData.GetValueOrDefault("customer_id"));
        var reqInvType = (rawData.GetValueOrDefault("invoice_type")?.ToString() ?? "debit").ToLowerInvariant();
        if (customerId <= 0 && reqInvType != "credit" && await Db.HasTableAsync("customers"))
        {
            var defaultCustId = await Db.ExecuteScalarAsync<long?>("SELECT id FROM customers ORDER BY id ASC LIMIT 1");
            if (defaultCustId.HasValue && defaultCustId.Value > 0)
            {
                customerId = defaultCustId.Value;
                rawData["customer_id"] = customerId;
            }
        }

        var date = rawData.GetValueOrDefault("date")?.ToString() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (customerId <= 0) errors["customer_id"] = new[] { "The customer id field is required." };
        if (string.IsNullOrWhiteSpace(date)) errors["date"] = new[] { "The date field is required." };
        if (errors.Count > 0)
        {
            return Unprocessable("The given data was invalid.", errors);
        }

        try
        {
            double? clientSubmittedTotal = rawData.TryGetValue("total", out var ctObj) && ctObj != null && double.TryParse(ctObj.ToString(), out var ctv)
                ? Math.Round(ctv, 2)
                : null;
            bool hasExplicitReceive = rawData.TryGetValue("total_receive", out var crObj) && crObj != null && !string.IsNullOrWhiteSpace(crObj.ToString());
            double? clientSubmittedReceive = hasExplicitReceive && double.TryParse(crObj!.ToString(), out var crv)
                ? Math.Round(crv, 2)
                : null;

            bool discountOverride = IsTruthy(rawData.GetValueOrDefault("discount_override"));
            var calculated = await _calculationService.CalculateAndValidateSaleAsync(rawData, rawItems, CurrentUserId, discountOverride);
            var calcItems = (List<Dictionary<string, object?>>)calculated["items"]!;

            rawData["gross_amount"] = calculated["gross_amount"];
            rawData["item_discount"] = calculated["item_discount"];
            rawData["discount_percentage"] = calculated["discount_percentage"];
            rawData["discount_amount"] = calculated["discount_amount"];
            rawData["tax_percentage"] = calculated["tax_percentage"];
            rawData["tax_amount"] = calculated["tax_amount"];
            rawData["total"] = calculated["total"];

            var serverTotal = ToDouble(calculated["total"]);
            var applyCreditAmount = Math.Round(ToDouble(rawData.GetValueOrDefault("apply_credit_amount")), 2);
            if (applyCreditAmount > 0)
            {
                rawData["invoice_type"] = "credit";
            }

            if (splitPayments.Count > 0)
            {
                var splitSum = Math.Round(splitPayments.Sum(leg => ToDouble(leg.GetValueOrDefault("amount"))), 2);
                if (!hasExplicitReceive)
                {
                    rawData["total_receive"] = splitSum;
                }
            }

            var effectiveInvoiceType = (rawData.GetValueOrDefault("invoice_type")?.ToString() ?? "debit").ToLowerInvariant();
            if (effectiveInvoiceType == "debit" && splitPayments.Count == 0)
            {
                bool isClientFullDebitReceive = !hasExplicitReceive
                    || (clientSubmittedReceive.HasValue && clientSubmittedTotal.HasValue && clientSubmittedReceive.Value >= (clientSubmittedTotal.Value - 0.009))
                    || (clientSubmittedReceive.HasValue && Math.Abs(clientSubmittedReceive.Value - serverTotal) <= 1.00);

                if (isClientFullDebitReceive)
                {
                    if (rawData.TryGetValue("cash_given", out var cgObj) && cgObj != null && double.TryParse(cgObj.ToString(), out var cgVal))
                    {
                        var cashGivenFloat = Math.Round(cgVal, 2);
                        bool isFullCashGiven = (clientSubmittedTotal.HasValue && cashGivenFloat >= (clientSubmittedTotal.Value - 0.009))
                                               || Math.Abs(cashGivenFloat - serverTotal) <= 1.00
                                               || cashGivenFloat >= serverTotal;
                        rawData["total_receive"] = isFullCashGiven ? serverTotal : Math.Min(serverTotal, cashGivenFloat);
                    }
                    else
                    {
                        rawData["total_receive"] = serverTotal;
                    }
                }
            }

            await ResolveDoctorAndPatientDataAsync(rawData);
            await ValidateCreditLimitsAsync(rawData, clientSubmittedTotal, null);

            var op = await _operatorPinService.ResolveOperatorAsync(rawPin, CurrentUserId, "sale_invoice");
            var operatorUserId = ToNullableLong(op.GetValueOrDefault("user_id")) ?? CurrentUserId!.Value;
            var operatorEmpId = ToNullableLong(op.GetValueOrDefault("employee_id"));

            var draftId = ToNullableLong(rawData.GetValueOrDefault("draft_id"));
            if (draftId.HasValue && await Db.HasTableAsync("invoice_drafts"))
            {
                var draft = await Db.QuerySingleOrDefaultAsync("SELECT * FROM invoice_drafts WHERE id = @id", new { id = draftId.Value });
                if (draft != null && string.Equals(draft.GetValueOrDefault("status")?.ToString(), "posted", StringComparison.OrdinalIgnoreCase))
                {
                    var postedInvId = ToLong(draft.GetValueOrDefault("posted_invoice_id"));
                    var postedInv = postedInvId > 0
                        ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id = postedInvId })
                        : null;
                    return Ok(new Dictionary<string, object?>
                    {
                        ["message"] = "Sale Invoice already created",
                        ["id"] = postedInvId,
                        ["posted_number"] = postedInv?.GetValueOrDefault("posted_number")
                    });
                }
            }

            var postedNumber = await _sequenceService.NextAsync(
                "sale_invoice",
                "SI-",
                6,
                "sale_invoices",
                "posted_number",
                true);

            await using var conn = await Db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                bool expiredOverride = IsTruthy(rawData.GetValueOrDefault("expired_override"));
                await _stockAvailabilityService.ValidateAsync(calcItems, null, CurrentUserId, expiredOverride, rawData, conn, tx);

                var dueDate = await ResolveDueDateAsync(rawData, customerId, date, conn, tx);
                var saleType = (rawData.GetValueOrDefault("sale_type")?.ToString() ?? "retail").ToLowerInvariant();
                var wholesaleType = rawData.GetValueOrDefault("wholesale_type")?.ToString() ?? "unit";
                var payMethod = rawData.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
                var totalReceive = ToDouble(rawData.GetValueOrDefault("total_receive"));

                long? terminalId = null;
                if (HttpContext?.Items != null && HttpContext.Items.TryGetValue("KarobarTerminal", out var termObj) && termObj is Dictionary<string, object?> termDict)
                {
                    terminalId = ToNullableLong(termDict.GetValueOrDefault("id"));
                }

                var salesmanId = ToNullableLong(rawData.GetValueOrDefault("salesman_id")) ?? operatorEmpId;
                if (!salesmanId.HasValue && await Db.HasTableAsync("employees"))
                {
                    salesmanId = await Db.ExecuteScalarAsync<long?>(
                        "SELECT id FROM employees WHERE user_id = @uid LIMIT 1",
                        new { uid = operatorUserId },
                        conn,
                        tx);
                }

                var invoiceData = new Dictionary<string, object?>
                {
                    ["user_id"] = operatorUserId,
                    ["customer_id"] = customerId,
                    ["doctor_id"] = ToNullableLong(rawData.GetValueOrDefault("doctor_id")),
                    ["salesman_id"] = salesmanId,
                    ["terminal_id"] = terminalId,
                    ["invoice_type"] = effectiveInvoiceType,
                    ["payment_method"] = payMethod,
                    ["account_id"] = ToNullableLong(rawData.GetValueOrDefault("account_id")),
                    ["sale_type"] = saleType,
                    ["wholesale_type"] = wholesaleType,
                    ["posted_number"] = postedNumber,
                    ["date"] = date,
                    ["due_date"] = dueDate,
                    ["remarks"] = rawData.GetValueOrDefault("remarks"),
                    ["doctor_name"] = rawData.GetValueOrDefault("doctor_name"),
                    ["doctor_pmdc"] = rawData.GetValueOrDefault("doctor_pmdc"),
                    ["doctor_hospital"] = rawData.GetValueOrDefault("doctor_hospital"),
                    ["patient_name"] = rawData.GetValueOrDefault("patient_name"),
                    ["patient_cnic"] = rawData.GetValueOrDefault("patient_cnic"),
                    ["patient_address"] = rawData.GetValueOrDefault("patient_address"),
                    ["prescription_no"] = rawData.GetValueOrDefault("prescription_no"),
                    ["pharmacist_name"] = rawData.GetValueOrDefault("pharmacist_name"),
                    ["discount_percentage"] = calculated["discount_percentage"],
                    ["discount_amount"] = calculated["discount_amount"],
                    ["tax_percentage"] = calculated["tax_percentage"],
                    ["tax_amount"] = calculated["tax_amount"],
                    ["item_discount"] = calculated["item_discount"],
                    ["gross_amount"] = calculated["gross_amount"],
                    ["total"] = serverTotal,
                    ["total_receive"] = totalReceive
                };

                foreach (var taxCol in new[] { "fbr_pos_fee", "further_tax_amount", "advance_wht_percentage", "advance_wht_amount" })
                {
                    if (rawData.TryGetValue(taxCol, out var tcVal) && tcVal != null)
                    {
                        invoiceData[taxCol] = Math.Max(0.0, ToDouble(tcVal));
                    }
                }

                var invoiceId = await Db.InsertAsync("sale_invoices", invoiceData, conn, tx);
                await CreateItemsAndReduceAsync(invoiceId, saleType, calcItems, null, null, conn, tx);

                if (saleType == "wholesale" && await CanAsync("customer.update", "product.update") && await Db.HasTableAsync("customer_whole_sale_prices"))
                {
                    await SaveCustomWholesalePricesAsync(customerId, calcItems, conn, tx);
                }

                if (effectiveInvoiceType == "credit" && await Db.HasTableAsync("customer_ledgers"))
                {
                    var invTotal = serverTotal;
                    var recvOnInv = Math.Min(totalReceive, invTotal);
                    var excessRecv = Math.Max(0.0, totalReceive - invTotal);

                    await Db.InsertAsync("customer_ledgers", new Dictionary<string, object?>
                    {
                        ["customer_id"] = customerId,
                        ["sale_invoice_id"] = invoiceId,
                        ["entry_type"] = "invoice",
                        ["is_manual"] = 0,
                        ["entry_date"] = date,
                        ["due_date"] = dueDate,
                        ["posted_number"] = postedNumber,
                        ["invoice_total"] = invTotal,
                        ["total_received"] = recvOnInv,
                        ["balance_remaining"] = Math.Max(invTotal - recvOnInv, 0.0),
                        ["credited_amount"] = 0,
                        ["payment_ref"] = null,
                        ["description"] = "Sale invoice " + postedNumber,
                        ["created_by"] = CurrentUserId
                    }, conn, tx);

                    if (excessRecv > 0)
                    {
                        await Db.InsertAsync("customer_ledgers", new Dictionary<string, object?>
                        {
                            ["customer_id"] = customerId,
                            ["sale_invoice_id"] = null,
                            ["entry_type"] = "payment",
                            ["is_manual"] = 0,
                            ["entry_date"] = date,
                            ["posted_number"] = postedNumber,
                            ["invoice_total"] = 0,
                            ["total_received"] = 0,
                            ["balance_remaining"] = 0,
                            ["credited_amount"] = excessRecv,
                            ["payment_ref"] = "Inv #" + postedNumber,
                            ["description"] = "Payment received on invoice " + postedNumber,
                            ["created_by"] = CurrentUserId
                        }, conn, tx);
                    }
                }

                if (draftId.HasValue && await Db.HasTableAsync("invoice_drafts"))
                {
                    await Db.UpdateAsync("invoice_drafts", draftId.Value, new Dictionary<string, object?>
                    {
                        ["status"] = "posted",
                        ["posted_invoice_id"] = invoiceId,
                        ["posted_invoice_type"] = "App\\Models\\SaleInvoice"
                    }, "id", conn, tx);
                }

                // Financial account & cash drawer recording
                var validSplitLegs = splitPayments.Where(l => Math.Round(ToDouble(l.GetValueOrDefault("amount")), 2) > 0).ToList();
                if (validSplitLegs.Count > 0)
                {
                    long? firstAccountId = null;
                    foreach (var leg in validSplitLegs)
                    {
                        var legAmount = Math.Round(ToDouble(leg.GetValueOrDefault("amount")), 2);
                        var legMethod = leg.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
                        var legAccount = await _settlementService.ResolveAccountAsync(ToNullableLong(leg.GetValueOrDefault("account_id")), legMethod, conn, tx);
                        if (legAccount != null)
                        {
                            var accId = ToLong(legAccount["id"]);
                            firstAccountId ??= accId;
                            var accName = legAccount.GetValueOrDefault("name")?.ToString() ?? "Account";
                            await _settlementService.DepositToAccountAsync(
                                legAccount,
                                legAmount,
                                "sale_invoice",
                                invoiceId,
                                $"Sale invoice {postedNumber} ({accName} - {legMethod})",
                                CurrentUserId,
                                conn,
                                tx);
                        }

                        if (ReturnSettlementService.IsDefaultCashDrawerAccount(legAccount, legMethod))
                        {
                            await _settlementService.RecordCashDrawerTransactionAsync(
                                "cash_sale",
                                legAmount,
                                "sale_invoice",
                                invoiceId,
                                $"Cash sale {postedNumber} (split)",
                                CurrentUserId,
                                conn,
                                tx);
                        }
                    }

                    if (firstAccountId.HasValue)
                    {
                        await Db.UpdateAsync("sale_invoices", invoiceId, new Dictionary<string, object?> { ["account_id"] = firstAccountId.Value }, "id", conn, tx);
                    }
                }
                else
                {
                    var cashReceived = totalReceive;
                    if (effectiveInvoiceType == "debit" && cashReceived <= 0 && applyCreditAmount <= 0)
                    {
                        cashReceived = serverTotal;
                    }
                    if (cashReceived > 0)
                    {
                        var account = await _settlementService.ResolveAccountAsync(ToNullableLong(rawData.GetValueOrDefault("account_id")), payMethod, conn, tx);
                        if (account != null)
                        {
                            var accId = ToLong(account["id"]);
                            var accName = account.GetValueOrDefault("name")?.ToString() ?? "Account";
                            await Db.UpdateAsync("sale_invoices", invoiceId, new Dictionary<string, object?> { ["account_id"] = accId }, "id", conn, tx);
                            await _settlementService.DepositToAccountAsync(
                                account,
                                cashReceived,
                                "sale_invoice",
                                invoiceId,
                                $"Sale invoice {postedNumber} ({accName})",
                                CurrentUserId,
                                conn,
                                tx);
                        }

                        if (ReturnSettlementService.IsDefaultCashDrawerAccount(account, payMethod))
                        {
                            await _settlementService.RecordCashDrawerTransactionAsync(
                                "cash_sale",
                                cashReceived,
                                "sale_invoice",
                                invoiceId,
                                $"Cash sale {postedNumber}",
                                CurrentUserId,
                                conn,
                                tx);
                        }
                    }
                }

                // Apply unapplied customer credits if requested
                if (applyCreditAmount > 0)
                {
                    var remainingOnInv = Math.Round(Math.Max(serverTotal - totalReceive, 0.0), 2);
                    var creditToApply = Math.Min(applyCreditAmount, remainingOnInv);
                    if (creditToApply > 0)
                    {
                        await ApplyCustomerCreditsToInvoiceAsync(customerId, invoiceId, creditToApply, conn, tx);
                    }
                }

                await tx.CommitAsync();
                SyncPulseService.Bump("stock", "drafts", "drawer");

                var loaded = (await LoadSaleInvoiceAsync(invoiceId))!;
                await _auditService.RecordCreateAsync("App\\Models\\SaleInvoice", invoiceId, loaded, operatorUserId);

                return StatusCode(201, new Dictionary<string, object?>
                {
                    ["message"] = "Sale Invoice created",
                    ["id"] = invoiceId,
                    ["posted_number"] = postedNumber,
                    ["operator_name"] = op.GetValueOrDefault("operator_name"),
                    ["fbr_invoice_number"] = loaded.GetValueOrDefault("fbr_invoice_number"),
                    ["fbr_sync_status"] = loaded.GetValueOrDefault("fbr_sync_status") ?? "not_applicable",
                    ["fbr_qr_data"] = loaded.GetValueOrDefault("fbr_qr_data")
                });
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
        if (!await CanAsync("sale-invoice.update"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Sale invoice not found." });

        if (string.Equals(existing.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
        {
            return Unprocessable("Cannot edit an invoice that has been reversed.");
        }

        var beforeSnapshot = (await LoadSaleInvoiceAsync(id)) ?? existing;
        var body = await ReadJsonBodyAsync();
        var (rawData, rawItems, splitPayments) = ParseSalePayload(body);

        var customerId = ToLong(rawData.GetValueOrDefault("customer_id") ?? existing.GetValueOrDefault("customer_id"));
        var reqInvType = (rawData.GetValueOrDefault("invoice_type")?.ToString() ?? existing.GetValueOrDefault("invoice_type")?.ToString() ?? "debit").ToLowerInvariant();
        if (customerId <= 0 && reqInvType != "credit" && await Db.HasTableAsync("customers"))
        {
            var defaultCustId = await Db.ExecuteScalarAsync<long?>("SELECT id FROM customers ORDER BY id ASC LIMIT 1");
            if (defaultCustId.HasValue && defaultCustId.Value > 0)
            {
                customerId = defaultCustId.Value;
                rawData["customer_id"] = customerId;
            }
        }

        try
        {
            double? clientUpdateTotal = rawData.TryGetValue("total", out var ctObj) && ctObj != null && double.TryParse(ctObj.ToString(), out var ctv)
                ? Math.Round(ctv, 2)
                : null;
            bool hasExplicitUpdateReceive = rawData.TryGetValue("total_receive", out var crObj) && crObj != null && !string.IsNullOrWhiteSpace(crObj.ToString());
            double? clientUpdateReceive = hasExplicitUpdateReceive && double.TryParse(crObj!.ToString(), out var crv)
                ? Math.Round(crv, 2)
                : null;

            bool discountOverride = IsTruthy(rawData.GetValueOrDefault("discount_override"));
            var calculated = await _calculationService.CalculateAndValidateSaleAsync(rawData, rawItems, CurrentUserId, discountOverride);
            var calcItems = (List<Dictionary<string, object?>>)calculated["items"]!;

            rawData["gross_amount"] = calculated["gross_amount"];
            rawData["item_discount"] = calculated["item_discount"];
            rawData["discount_percentage"] = calculated["discount_percentage"];
            rawData["discount_amount"] = calculated["discount_amount"];
            rawData["tax_percentage"] = calculated["tax_percentage"];
            rawData["tax_amount"] = calculated["tax_amount"];
            rawData["total"] = calculated["total"];

            var serverTotal = ToDouble(calculated["total"]);
            if (splitPayments.Count > 0 && !hasExplicitUpdateReceive)
            {
                rawData["total_receive"] = Math.Round(splitPayments.Sum(l => ToDouble(l.GetValueOrDefault("amount"))), 2);
                hasExplicitUpdateReceive = true;
            }

            if (reqInvType == "debit" && splitPayments.Count == 0)
            {
                bool isClientFullDebitUpdateReceive = !hasExplicitUpdateReceive
                    || (clientUpdateReceive.HasValue && clientUpdateTotal.HasValue && clientUpdateReceive.Value >= (clientUpdateTotal.Value - 0.009))
                    || (clientUpdateReceive.HasValue && Math.Abs(clientUpdateReceive.Value - serverTotal) <= 1.00);

                if (isClientFullDebitUpdateReceive)
                {
                    if (rawData.TryGetValue("cash_given", out var cgObj) && cgObj != null && double.TryParse(cgObj.ToString(), out var cgVal))
                    {
                        var cashGivenFloat = Math.Round(cgVal, 2);
                        bool isFullCashGiven = (clientUpdateTotal.HasValue && cashGivenFloat >= (clientUpdateTotal.Value - 0.009))
                                               || Math.Abs(cashGivenFloat - serverTotal) <= 1.00
                                               || cashGivenFloat >= serverTotal;
                        rawData["total_receive"] = isFullCashGiven ? serverTotal : Math.Min(serverTotal, cashGivenFloat);
                    }
                    else
                    {
                        rawData["total_receive"] = serverTotal;
                    }
                }
            }

            await ResolveDoctorAndPatientDataAsync(rawData);
            await ValidateCreditLimitsAsync(rawData, clientUpdateTotal, existing);

            var rawPin = GetString(body, "operator_pin") ?? Request.Headers["X-Operator-Pin"].FirstOrDefault();
            var op = await _operatorPinService.ResolveOperatorAsync(rawPin, CurrentUserId, "sale_invoice");
            var operatorUserId = ToNullableLong(op.GetValueOrDefault("user_id")) ?? CurrentUserId!.Value;
            var operatorEmpId = ToNullableLong(op.GetValueOrDefault("employee_id"));
            var verifiedViaPin = op.GetValueOrDefault("verified_via_pin") is true;

            await using var conn = await Db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            try
            {
                var allocatedOnInvoice = await GetAllocatedAmountForInvoiceAsync("sale_invoice", id, conn, tx);
                var oldInvoiceType = (existing.GetValueOrDefault("invoice_type")?.ToString() ?? "debit").ToLowerInvariant();
                var newInvoiceType = reqInvType;

                if (allocatedOnInvoice > 0.009 && (newInvoiceType != "credit" || customerId != ToLong(existing.GetValueOrDefault("customer_id"))))
                {
                    throw new InvoiceValidationException(
                        "Cannot change customer or convert a credit invoice with active payment/credit allocations to cash/debit. Detach or reverse payments first.",
                        new Dictionary<string, string[]>
                        {
                            ["invoice_type"] = new[] { "Cannot change customer or convert a credit invoice with active payment/credit allocations to cash/debit. Detach or reverse payments first." }
                        });
                }

                var originalItems = await Db.QueryListAsync("SELECT * FROM sale_invoice_items WHERE sale_invoice_id = @id", new { id }, conn, tx);
                bool expiredOverride = IsTruthy(rawData.GetValueOrDefault("expired_override"));
                await _stockAvailabilityService.ValidateAsync(calcItems, originalItems, CurrentUserId, expiredOverride, rawData, conn, tx);

                var costs = await _costSnapshotService.ProductCostsAsync(calcItems.Cast<IDictionary<string, object?>>(), conn, tx);
                await RevertItemsAsync(originalItems, conn, tx);
                await Db.ExecuteAsync("DELETE FROM sale_invoice_items WHERE sale_invoice_id = @id", new { id }, conn, tx);

                var rawSubmittedReceive = Math.Round(ToDouble(rawData.GetValueOrDefault("total_receive")), 2);
                double submittedTotalReceive;
                if (allocatedOnInvoice > 0)
                {
                    if (rawSubmittedReceive <= Math.Round(serverTotal - allocatedOnInvoice + 0.009, 2))
                    {
                        submittedTotalReceive = Math.Round(rawSubmittedReceive + allocatedOnInvoice, 2);
                    }
                    else
                    {
                        submittedTotalReceive = rawSubmittedReceive;
                    }
                }
                else
                {
                    submittedTotalReceive = rawSubmittedReceive;
                }

                var date = rawData.GetValueOrDefault("date")?.ToString() ?? existing.GetValueOrDefault("date")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
                var dueDate = await ResolveDueDateAsync(rawData, customerId, date, conn, tx);
                var postedNumber = rawData.GetValueOrDefault("posted_number")?.ToString() ?? existing.GetValueOrDefault("posted_number")?.ToString() ?? $"SI-{id:D6}";
                var newSaleType = (rawData.GetValueOrDefault("sale_type")?.ToString() ?? existing.GetValueOrDefault("sale_type")?.ToString() ?? "retail").ToLowerInvariant();
                var newPayMethod = rawData.GetValueOrDefault("payment_method")?.ToString() ?? existing.GetValueOrDefault("payment_method")?.ToString() ?? "cash";

                var updateData = new Dictionary<string, object?>
                {
                    ["customer_id"] = customerId,
                    ["invoice_type"] = newInvoiceType,
                    ["sale_type"] = newSaleType,
                    ["wholesale_type"] = rawData.GetValueOrDefault("wholesale_type") ?? existing.GetValueOrDefault("wholesale_type") ?? "unit",
                    ["posted_number"] = postedNumber,
                    ["date"] = date,
                    ["due_date"] = dueDate,
                    ["payment_method"] = newPayMethod,
                    ["doctor_id"] = ToNullableLong(rawData.GetValueOrDefault("doctor_id")),
                    ["remarks"] = rawData.GetValueOrDefault("remarks"),
                    ["doctor_name"] = rawData.GetValueOrDefault("doctor_name"),
                    ["patient_name"] = rawData.GetValueOrDefault("patient_name"),
                    ["discount_percentage"] = calculated["discount_percentage"],
                    ["discount_amount"] = calculated["discount_amount"],
                    ["tax_percentage"] = calculated["tax_percentage"],
                    ["tax_amount"] = calculated["tax_amount"],
                    ["item_discount"] = calculated["item_discount"],
                    ["gross_amount"] = calculated["gross_amount"],
                    ["total"] = serverTotal,
                    ["total_receive"] = submittedTotalReceive
                };

                if (verifiedViaPin)
                {
                    updateData["user_id"] = operatorUserId;
                }
                if (rawData.ContainsKey("salesman_id"))
                {
                    updateData["salesman_id"] = ToNullableLong(rawData["salesman_id"]) ?? (verifiedViaPin ? operatorEmpId : null);
                }
                else if (verifiedViaPin && operatorEmpId.HasValue)
                {
                    updateData["salesman_id"] = operatorEmpId.Value;
                }

                foreach (var regCol in new[] { "doctor_pmdc", "doctor_hospital", "patient_cnic", "patient_address", "prescription_no", "pharmacist_name" })
                {
                    if (rawData.ContainsKey(regCol))
                    {
                        updateData[regCol] = rawData[regCol];
                    }
                }

                await Db.UpdateAsync("sale_invoices", id, updateData, "id", conn, tx);
                await CreateItemsAndReduceAsync(id, newSaleType, calcItems, originalItems, costs, conn, tx);

                if (newSaleType == "wholesale" && await CanAsync("customer.update", "product.update") && await Db.HasTableAsync("customer_whole_sale_prices"))
                {
                    await SaveCustomWholesalePricesAsync(customerId, calcItems, conn, tx);
                }

                if (await Db.HasTableAsync("customer_ledgers"))
                {
                    if (newInvoiceType != "credit")
                    {
                        await Db.ExecuteAsync("DELETE FROM customer_ledgers WHERE sale_invoice_id = @id", new { id }, conn, tx);
                        await Db.ExecuteAsync(
                            "DELETE FROM customer_ledgers WHERE payment_ref = @pref AND entry_type = 'payment'",
                            new { pref = "Inv #" + postedNumber },
                            conn,
                            tx);
                    }
                    else
                    {
                        await Db.ExecuteAsync("DELETE FROM customer_ledgers WHERE sale_invoice_id = @id", new { id }, conn, tx);
                        await Db.ExecuteAsync(
                            "DELETE FROM customer_ledgers WHERE customer_id = @cid AND payment_ref = @pref AND entry_type = 'payment'",
                            new { cid = customerId, pref = "Inv #" + postedNumber },
                            conn,
                            tx);

                        var invTotal = serverTotal;
                        var directRecv = Math.Max(0.0, Math.Round(submittedTotalReceive - allocatedOnInvoice, 2));
                        var recvOnInv = Math.Min(directRecv, invTotal);
                        var excessRecv = Math.Max(0.0, Math.Round(directRecv - Math.Max(0.0, invTotal - allocatedOnInvoice), 2));

                        await Db.InsertAsync("customer_ledgers", new Dictionary<string, object?>
                        {
                            ["customer_id"] = customerId,
                            ["sale_invoice_id"] = id,
                            ["entry_type"] = "invoice",
                            ["is_manual"] = 0,
                            ["entry_date"] = date,
                            ["due_date"] = dueDate,
                            ["posted_number"] = postedNumber,
                            ["invoice_total"] = invTotal,
                            ["total_received"] = recvOnInv,
                            ["balance_remaining"] = Math.Max(invTotal - recvOnInv, 0.0),
                            ["credited_amount"] = 0,
                            ["payment_ref"] = null,
                            ["description"] = "Sale invoice " + postedNumber,
                            ["created_by"] = CurrentUserId
                        }, conn, tx);

                        if (excessRecv > 0)
                        {
                            await Db.InsertAsync("customer_ledgers", new Dictionary<string, object?>
                            {
                                ["customer_id"] = customerId,
                                ["sale_invoice_id"] = null,
                                ["entry_type"] = "payment",
                                ["is_manual"] = 0,
                                ["entry_date"] = date,
                                ["posted_number"] = postedNumber,
                                ["invoice_total"] = 0,
                                ["total_received"] = 0,
                                ["balance_remaining"] = 0,
                                ["credited_amount"] = excessRecv,
                                ["payment_ref"] = "Inv #" + postedNumber,
                                ["description"] = "Payment received on invoice " + postedNumber,
                                ["created_by"] = CurrentUserId
                            }, conn, tx);
                        }
                    }
                }

                await tx.CommitAsync();
                SyncPulseService.Bump("stock", "drawer");

                var loaded = (await LoadSaleInvoiceAsync(id))!;
                var reason = rawData.GetValueOrDefault("reason")?.ToString() ?? "Sale invoice updated";
                await _auditService.RecordUpdateAsync("App\\Models\\SaleInvoice", id, beforeSnapshot, loaded, operatorUserId, reason);

                return Ok(new Dictionary<string, object?>
                {
                    ["message"] = "Sale Invoice updated",
                    ["id"] = id,
                    ["operator_name"] = op.GetValueOrDefault("operator_name")
                });
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
        if (!await CanAsync("sale-invoice.reverse", "sale-invoice.delete", "sale-invoice.update"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Sale invoice not found." });

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

        if (await Db.HasTableAsync("sale_returns"))
        {
            var hasReturns = await Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sale_returns WHERE sale_invoice_id = @id",
                new { id }) > 0;
            if (hasReturns)
            {
                return Unprocessable("Cannot reverse this invoice because linked sale returns exist. Please reverse or remove the returns first.");
            }
        }

        var beforeSnapshot = (await LoadSaleInvoiceAsync(id)) ?? existing;

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var items = await Db.QueryListAsync("SELECT * FROM sale_invoice_items WHERE sale_invoice_id = @id", new { id }, conn, tx);
            await _stockAvailabilityService.ValidateAsync(new List<Dictionary<string, object?>>(), items, CurrentUserId, false, null, conn, tx);
            await RevertItemsAsync(items, conn, tx);

            await DetachInvoiceAllocationsAsync("sale_invoice", id, conn, tx);

            var postedNumber = existing.GetValueOrDefault("posted_number")?.ToString() ?? "";
            var customerId = ToLong(existing.GetValueOrDefault("customer_id"));
            if (string.Equals(existing.GetValueOrDefault("invoice_type")?.ToString(), "credit", StringComparison.OrdinalIgnoreCase) &&
                await Db.HasTableAsync("customer_ledgers"))
            {
                await Db.ExecuteAsync("DELETE FROM customer_ledgers WHERE sale_invoice_id = @id", new { id }, conn, tx);
                await Db.ExecuteAsync(
                    "DELETE FROM customer_ledgers WHERE customer_id = @cid AND payment_ref = @pref AND entry_type = 'payment'",
                    new { cid = customerId, pref = "Inv #" + postedNumber },
                    conn,
                    tx);
            }

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            await Db.UpdateAsync("sale_invoices", id, new Dictionary<string, object?>
            {
                ["status"] = "reversed",
                ["reversed_at"] = now,
                ["reversed_by_id"] = CurrentUserId,
                ["reversal_reason"] = reason
            }, "id", conn, tx);

            var freshInv = (await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id }, conn, tx))!;
            var revAmount = ToDouble(freshInv.GetValueOrDefault("total_receive"));
            var invType = (freshInv.GetValueOrDefault("invoice_type")?.ToString() ?? "debit").ToLowerInvariant();
            if (invType == "debit" && revAmount <= 0)
            {
                revAmount = ToDouble(freshInv.GetValueOrDefault("total"));
            }

            if (revAmount > 0)
            {
                var payMethod = freshInv.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
                var acc = await _settlementService.ResolveAccountAsync(ToNullableLong(freshInv.GetValueOrDefault("account_id")), payMethod, conn, tx);
                if (acc != null)
                {
                    await _settlementService.WithdrawFromAccountAsync(
                        acc,
                        revAmount,
                        "sale_invoice_reversal",
                        id,
                        $"Reversal refund for Sale Invoice #{postedNumber}",
                        CurrentUserId,
                        conn,
                        tx);
                }
                if (ReturnSettlementService.IsDefaultCashDrawerAccount(acc, payMethod))
                {
                    await _settlementService.RecordCashDrawerTransactionAsync(
                        "cash_refund",
                        revAmount,
                        "sale_invoice_reversal",
                        id,
                        $"Reversal of Sale Invoice #{postedNumber}: {reason}",
                        CurrentUserId,
                        conn,
                        tx);
                }
            }

            await tx.CommitAsync();
            SyncPulseService.Bump("stock", "drawer");

            await _auditService.RecordReversalAsync("App\\Models\\SaleInvoice", id, beforeSnapshot, CurrentUserId, reason);
            var loaded = await LoadSaleInvoiceAsync(id);

            return Ok(new Dictionary<string, object?>
            {
                ["message"] = "Sale invoice reversed successfully.",
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
    public async Task<IActionResult> Destroy(long id, [FromQuery] string? mode = null)
    {
        if (!await CanAsync("sale-invoice.delete"))
        {
            return Forbidden();
        }

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Sale invoice not found." });

        if (await Db.HasTableAsync("sale_returns"))
        {
            var hasReturns = await Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sale_returns WHERE sale_invoice_id = @id",
                new { id }) > 0;
            if (hasReturns)
            {
                return Unprocessable("Cannot delete this invoice because linked sale returns exist. Please delete the returns first.");
            }
        }

        var body = await ReadJsonBodyAsync();
        var effectiveMode = (mode ?? GetString(body, "mode") ?? "none").Trim().ToLowerInvariant();
        if (effectiveMode is not ("none" or "credit" or "refund")) effectiveMode = "none";

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            var items = await Db.QueryListAsync("SELECT * FROM sale_invoice_items WHERE sale_invoice_id = @id", new { id }, conn, tx);
            var isReversed = string.Equals(existing.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase);

            if (!isReversed)
            {
                await _stockAvailabilityService.ValidateAsync(new List<Dictionary<string, object?>>(), items, CurrentUserId, false, null, conn, tx);
                await RevertItemsAsync(items, conn, tx);
                await DetachInvoiceAllocationsAsync("sale_invoice", id, conn, tx);

                var freshInv = (await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id }, conn, tx))!;
                var received = ToDouble(freshInv.GetValueOrDefault("total_receive"));
                var isCreditInvoice = string.Equals(freshInv.GetValueOrDefault("invoice_type")?.ToString(), "credit", StringComparison.OrdinalIgnoreCase);
                var customerId = ToLong(freshInv.GetValueOrDefault("customer_id"));
                var postedNumber = freshInv.GetValueOrDefault("posted_number")?.ToString() ?? "";
                var entryDate = freshInv.GetValueOrDefault("date")?.ToString() ?? DateTime.UtcNow.ToString("yyyy-MM-dd");

                if (isCreditInvoice && await Db.HasTableAsync("customer_ledgers"))
                {
                    await Db.ExecuteAsync("DELETE FROM customer_ledgers WHERE sale_invoice_id = @id", new { id }, conn, tx);
                    await Db.ExecuteAsync(
                        "DELETE FROM customer_ledgers WHERE customer_id = @cid AND payment_ref = @pref AND entry_type = 'payment'",
                        new { cid = customerId, pref = "Inv #" + postedNumber },
                        conn,
                        tx);

                    if (effectiveMode == "credit" && received > 0)
                    {
                        await Db.InsertAsync("customer_ledgers", new Dictionary<string, object?>
                        {
                            ["customer_id"] = customerId,
                            ["entry_type"] = "manual",
                            ["is_manual"] = 1,
                            ["entry_date"] = entryDate,
                            ["posted_number"] = postedNumber,
                            ["invoice_total"] = -received,
                            ["total_received"] = 0,
                            ["balance_remaining"] = 0,
                            ["credited_amount"] = 0,
                            ["payment_ref"] = null,
                            ["sale_invoice_id"] = null,
                            ["description"] = "Store credit for deleted invoice " + postedNumber,
                            ["created_by"] = CurrentUserId
                        }, conn, tx);
                    }
                }

                if (effectiveMode != "credit")
                {
                    var delAmount = received;
                    if (!isCreditInvoice && delAmount <= 0)
                    {
                        delAmount = ToDouble(freshInv.GetValueOrDefault("total"));
                    }
                    if (delAmount > 0)
                    {
                        var payMethod = freshInv.GetValueOrDefault("payment_method")?.ToString() ?? "cash";
                        var acc = await _settlementService.ResolveAccountAsync(ToNullableLong(freshInv.GetValueOrDefault("account_id")), payMethod, conn, tx);
                        if (acc != null)
                        {
                            await _settlementService.WithdrawFromAccountAsync(
                                acc,
                                delAmount,
                                "sale_invoice_deletion",
                                id,
                                $"Refund for deleted sale invoice #{postedNumber}",
                                CurrentUserId,
                                conn,
                                tx);
                        }
                    }

                    if (await Db.HasTableAsync("cash_drawer_transactions"))
                    {
                        await Db.ExecuteAsync(
                            "DELETE FROM cash_drawer_transactions WHERE reference_type = 'sale_invoice' AND reference_id = @id",
                            new { id },
                            conn,
                            tx);
                    }
                }
            }

            await Db.ExecuteAsync("DELETE FROM sale_invoice_items WHERE sale_invoice_id = @id", new { id }, conn, tx);
            await Db.ExecuteAsync("DELETE FROM sale_invoices WHERE id = @id", new { id }, conn, tx);

            await tx.CommitAsync();
            SyncPulseService.Bump("stock", "drawer");

            return Ok(new Dictionary<string, object?> { ["message"] = "Sale Invoice deleted" });
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

    [HttpPut("{saleInvoice:long}/meta")]
    public async Task<IActionResult> UpdateMeta(long saleInvoice)
    {
        if (!await CanAsync("report.sale-detail.edit", "report.sale-detail.view", "sale-invoice.update", "sale-invoice.create"))
        {
            return Forbidden("Unauthorized");
        }

        var inv = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id = saleInvoice });
        if (inv == null) return NotFound(new { message = "Sale invoice not found." });

        var status = (inv.GetValueOrDefault("status")?.ToString() ?? "").ToLowerInvariant();
        if (status is "cancelled" or "reversed")
        {
            return Forbidden("Cancelled or reversed invoice cannot be edited");
        }

        var body = await ReadJsonBodyAsync();
        var allowed = new[]
        {
            "doctor_name", "doctor_pmdc", "doctor_hospital",
            "patient_name", "patient_cnic", "patient_address",
            "prescription_no", "pharmacist_name"
        };

        var updates = new Dictionary<string, object?>();
        foreach (var col in allowed)
        {
            if (body.ContainsKey(col))
            {
                updates[col] = GetString(body, col);
            }
        }

        if (updates.Count > 0)
        {
            await Db.UpdateAsync("sale_invoices", saleInvoice, updates);
        }

        var fresh = (await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id = saleInvoice }))!;
        return Ok(new Dictionary<string, object?>
        {
            ["success"] = true,
            ["doctor_name"] = fresh.GetValueOrDefault("doctor_name"),
            ["doctor_pmdc"] = fresh.GetValueOrDefault("doctor_pmdc"),
            ["doctor_hospital"] = fresh.GetValueOrDefault("doctor_hospital"),
            ["patient_name"] = fresh.GetValueOrDefault("patient_name"),
            ["patient_cnic"] = fresh.GetValueOrDefault("patient_cnic"),
            ["patient_address"] = fresh.GetValueOrDefault("patient_address"),
            ["prescription_no"] = fresh.GetValueOrDefault("prescription_no"),
            ["pharmacist_name"] = fresh.GetValueOrDefault("pharmacist_name")
        });
    }

    [HttpPut("{saleInvoice:long}/dosages")]
    public async Task<IActionResult> UpdateDosages(long saleInvoice)
    {
        if (!await CanAsync("sale-invoice.update", "sale-invoice.create", "sale-invoice.view", "barcode-pos.create"))
        {
            return Forbidden("Unauthorized");
        }

        var inv = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id = saleInvoice });
        if (inv == null) return NotFound(new { message = "Sale invoice not found." });

        var body = await ReadJsonBodyAsync();
        if (body.TryGetValue("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array &&
            await Db.HasColumnAsync("sale_invoice_items", "dosage_instruction"))
        {
            foreach (var itemEl in itemsEl.EnumerateArray())
            {
                if (itemEl.ValueKind != JsonValueKind.Object) continue;
                var d = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in itemEl.EnumerateObject()) d[p.Name] = p.Value;

                var itemId = GetNullableLong(d, "id") ?? 0;
                if (itemId <= 0) continue;

                var lineItem = await Db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM sale_invoice_items WHERE id = @itemId AND sale_invoice_id = @invId",
                    new { itemId, invId = saleInvoice });
                if (lineItem == null) continue;

                var dosage = (GetString(d, "dosage_instruction") ?? "").Trim();
                var dosageVal = string.IsNullOrEmpty(dosage) ? null : dosage;

                await Db.UpdateAsync("sale_invoice_items", itemId, new Dictionary<string, object?>
                {
                    ["dosage_instruction"] = dosageVal
                });

                if (GetBool(d, "save_as_product_default") && await Db.HasColumnAsync("products", "default_dosage"))
                {
                    var pid = ToLong(lineItem.GetValueOrDefault("product_id"));
                    if (pid > 0)
                    {
                        await Db.UpdateAsync("products", pid, new Dictionary<string, object?>
                        {
                            ["default_dosage"] = dosageVal
                        });
                    }
                }
            }
        }

        var loaded = await LoadSaleInvoiceAsync(saleInvoice);
        return Ok(new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["message"] = "Dosage instructions saved.",
            ["invoice"] = loaded
        });
    }

    [HttpPost("{saleInvoice:long}/prescription")]
    public async Task<IActionResult> UploadPrescription(long saleInvoice)
    {
        if (!await CanAsync("sale-invoice.update", "sale-invoice.create", "barcode-pos.create"))
        {
            return Forbidden("Unauthorized");
        }

        var inv = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id = saleInvoice });
        if (inv == null) return NotFound(new { message = "Sale invoice not found." });

        if (!Request.HasFormContentType)
        {
            return Unprocessable("Multipart form data is required.");
        }

        var form = await Request.ReadFormAsync();
        var file = form.Files.GetFile("prescription");
        if (file == null || file.Length == 0)
        {
            return Unprocessable("The prescription file is required.", new Dictionary<string, string[]>
            {
                ["prescription"] = new[] { "The prescription file is required." }
            });
        }

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp" or ".pdf"))
        {
            return Unprocessable("Invalid file type.", new Dictionary<string, string[]>
            {
                ["prescription"] = new[] { "Prescription must be a jpg, jpeg, png, webp, or pdf file." }
            });
        }

        var oldPath = inv.GetValueOrDefault("prescription_image_path")?.ToString();
        if (!string.IsNullOrWhiteSpace(oldPath))
        {
            var fullOld = Db.StoragePath(Path.Combine("app", "public", oldPath));
            if (System.IO.File.Exists(fullOld))
            {
                try { System.IO.File.Delete(fullOld); } catch { }
            }
        }

        var fileName = $"{Guid.NewGuid():N}{ext}";
        var relPath = $"prescriptions/{fileName}";
        var fullDest = Db.StoragePath(Path.Combine("app", "public", "prescriptions", fileName));
        var dir = Path.GetDirectoryName(fullDest);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        await using (var fs = new FileStream(fullDest, FileMode.Create, FileAccess.Write))
        {
            await file.CopyToAsync(fs);
        }

        var updates = new Dictionary<string, object?>
        {
            ["prescription_image_path"] = relPath
        };

        if (form.TryGetValue("prescription_no", out var pNoVal) && !string.IsNullOrWhiteSpace(pNoVal.ToString()))
        {
            updates["prescription_no"] = pNoVal.ToString().Trim();
        }

        await Db.UpdateAsync("sale_invoices", saleInvoice, updates);
        var fresh = (await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id = saleInvoice }))!;

        return Ok(new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["message"] = "Prescription attached successfully.",
            ["prescription_image_path"] = relPath,
            ["prescription_image_url"] = $"/storage/{relPath}",
            ["prescription_no"] = fresh.GetValueOrDefault("prescription_no")
        });
    }

    [HttpDelete("{saleInvoice:long}/prescription")]
    public async Task<IActionResult> DeletePrescription(long saleInvoice)
    {
        if (!await CanAsync("sale-invoice.update", "sale-invoice.delete"))
        {
            return Forbidden("Unauthorized");
        }

        var inv = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id = saleInvoice });
        if (inv == null) return NotFound(new { message = "Sale invoice not found." });

        var oldPath = inv.GetValueOrDefault("prescription_image_path")?.ToString();
        if (!string.IsNullOrWhiteSpace(oldPath))
        {
            var fullOld = Db.StoragePath(Path.Combine("app", "public", oldPath));
            if (System.IO.File.Exists(fullOld))
            {
                try { System.IO.File.Delete(fullOld); } catch { }
            }
            await Db.UpdateAsync("sale_invoices", saleInvoice, new Dictionary<string, object?>
            {
                ["prescription_image_path"] = null
            });
        }

        return Ok(new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["message"] = "Prescription attachment removed."
        });
    }

    [HttpPost("{id:long}/make-regular-customer")]
    public async Task<IActionResult> MakeRegularCustomer(long id)
    {
        if (!await CanAsync("sale-invoice.update", "customer.update", "customer.create"))
        {
            return Forbidden("Unauthorized to update customer or invoice.");
        }

        var invoice = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id });
        if (invoice == null) return NotFound(new { message = "Sale invoice not found." });

        var body = await ReadJsonBodyAsync();
        if (!body.TryGetValue("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array || itemsEl.GetArrayLength() == 0)
        {
            return Unprocessable("At least one item is required.", new Dictionary<string, string[]>
            {
                ["items"] = new[] { "At least one item is required." }
            });
        }

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        try
        {
            Dictionary<string, object?>? customer = null;
            if (body.TryGetValue("new_customer", out var ncEl) && ncEl.ValueKind == JsonValueKind.Object)
            {
                var ncDict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in ncEl.EnumerateObject()) ncDict[p.Name] = p.Value;

                var newName = (GetString(ncDict, "name") ?? "").Trim();
                var newPhone = (GetString(ncDict, "phone") ?? "").Trim();
                var newAddr = (GetString(ncDict, "address") ?? "").Trim();

                if (!string.IsNullOrEmpty(newName))
                {
                    customer = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE name = @name LIMIT 1", new { name = newName }, conn, tx);
                    if (customer == null)
                    {
                        var newCid = await Db.InsertAsync("customers", new Dictionary<string, object?>
                        {
                            ["name"] = newName,
                            ["phone"] = string.IsNullOrEmpty(newPhone) ? null : newPhone,
                            ["address"] = string.IsNullOrEmpty(newAddr) ? null : newAddr
                        }, conn, tx);
                        customer = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = newCid }, conn, tx);
                    }
                    else if (!string.IsNullOrEmpty(newPhone) && string.IsNullOrWhiteSpace(customer.GetValueOrDefault("phone")?.ToString()))
                    {
                        customer["phone"] = newPhone;
                    }
                }
            }

            if (customer == null)
            {
                var reqCid = GetNullableLong(body, "customer_id") ?? ToLong(invoice.GetValueOrDefault("customer_id"));
                if (reqCid > 0)
                {
                    customer = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = reqCid }, conn, tx);
                }
            }

            if (customer == null || IsWalkInCustomer(customer))
            {
                await tx.RollbackAsync();
                return Unprocessable("Please select an existing customer or provide a customer name to register regular refills.");
            }

            var custId = ToLong(customer["id"]);
            var cycle = GetString(body, "refill_cycle") ?? customer.GetValueOrDefault("refill_cycle")?.ToString() ?? "monthly";
            var customDays = Math.Max(1, GetInt(body, "refill_custom_days", (int)ToLong(customer.GetValueOrDefault("refill_custom_days") ?? 30)));
            var nextDate = GetString(body, "next_refill_date");
            if (string.IsNullOrWhiteSpace(nextDate))
            {
                nextDate = cycle switch
                {
                    "weekly" => DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd"),
                    "biweekly" => DateTime.UtcNow.AddDays(14).ToString("yyyy-MM-dd"),
                    "custom" => DateTime.UtcNow.AddDays(customDays).ToString("yyyy-MM-dd"),
                    _ => DateTime.UtcNow.AddMonths(1).ToString("yyyy-MM-dd")
                };
            }

            var custUpdates = new Dictionary<string, object?>
            {
                ["is_regular"] = 1,
                ["refill_cycle"] = cycle,
                ["refill_custom_days"] = cycle == "custom" ? customDays : null,
                ["reminder_lead_days"] = GetInt(body, "reminder_lead_days", (int)ToLong(customer.GetValueOrDefault("reminder_lead_days") ?? 2)),
                ["next_refill_date"] = nextDate,
                ["reminder_language"] = GetString(body, "reminder_language") ?? customer.GetValueOrDefault("reminder_language")?.ToString() ?? "english",
                ["phone"] = customer.GetValueOrDefault("phone")
            };
            if (body.ContainsKey("regular_notes"))
            {
                custUpdates["regular_notes"] = GetString(body, "regular_notes");
            }
            await Db.UpdateAsync("customers", custId, custUpdates, "id", conn, tx);

            if (await Db.HasTableAsync("customer_regular_items"))
            {
                var mergeMode = GetString(body, "merge_mode") ?? "replace";
                if (mergeMode == "replace")
                {
                    await Db.ExecuteAsync("DELETE FROM customer_regular_items WHERE customer_id = @cid", new { cid = custId }, conn, tx);
                }

                int itemsCount = 0;
                foreach (var itemEl in itemsEl.EnumerateArray())
                {
                    if (itemEl.ValueKind != JsonValueKind.Object) continue;
                    var d = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                    foreach (var p in itemEl.EnumerateObject()) d[p.Name] = p.Value;

                    var pid = GetNullableLong(d, "product_id") ?? 0;
                    if (pid <= 0) continue;

                    var prodPackSize = Math.Max(1, await Db.ExecuteScalarAsync<int?>("SELECT pack_size FROM products WHERE id = @pid", new { pid }, conn, tx) ?? 1);
                    var packQty = Math.Max(0.0, (double)GetDecimal(d, "pack_quantity", 0m));
                    var unitQty = Math.Max(0, GetInt(d, "unit_quantity", 0));

                    int finalUnit;
                    int finalPack;
                    if (unitQty > 0)
                    {
                        finalUnit = unitQty;
                        finalPack = unitQty / prodPackSize;
                    }
                    else if (packQty > 0)
                    {
                        finalUnit = Math.Max(1, (int)Math.Round(packQty * prodPackSize));
                        finalPack = (int)Math.Floor(packQty);
                    }
                    else
                    {
                        finalUnit = prodPackSize;
                        finalPack = 1;
                    }

                    var dosageNotes = GetString(d, "dosage_notes")?.Trim();

                    if (mergeMode == "merge")
                    {
                        var existingReg = await Db.QuerySingleOrDefaultAsync(
                            "SELECT * FROM customer_regular_items WHERE customer_id = @cid AND product_id = @pid LIMIT 1",
                            new { cid = custId, pid },
                            conn,
                            tx);
                        if (existingReg != null)
                        {
                            await Db.UpdateAsync("customer_regular_items", existingReg["id"]!, new Dictionary<string, object?>
                            {
                                ["pack_quantity"] = finalPack,
                                ["unit_quantity"] = finalUnit,
                                ["dosage_notes"] = !string.IsNullOrEmpty(dosageNotes) ? dosageNotes : existingReg.GetValueOrDefault("dosage_notes")
                            }, "id", conn, tx);
                            itemsCount++;
                            continue;
                        }
                    }

                    await Db.InsertAsync("customer_regular_items", new Dictionary<string, object?>
                    {
                        ["customer_id"] = custId,
                        ["product_id"] = pid,
                        ["pack_quantity"] = finalPack,
                        ["unit_quantity"] = finalUnit,
                        ["dosage_notes"] = dosageNotes
                    }, conn, tx);
                    itemsCount++;
                }
            }

            var updateInvoiceCustomer = !body.ContainsKey("update_invoice_customer") || GetBool(body, "update_invoice_customer", true);
            if (updateInvoiceCustomer && ToLong(invoice.GetValueOrDefault("customer_id")) != custId)
            {
                await Db.UpdateAsync("sale_invoices", id, new Dictionary<string, object?> { ["customer_id"] = custId }, "id", conn, tx);
                if (await Db.HasTableAsync("customer_ledgers"))
                {
                    await Db.ExecuteAsync(
                        "UPDATE customer_ledgers SET customer_id = @cid WHERE sale_invoice_id = @id",
                        new { cid = custId, id },
                        conn,
                        tx);
                }
            }

            await tx.CommitAsync();

            var loadedInvoice = await LoadSaleInvoiceAsync(id);
            var loadedCustomer = loadedInvoice?.GetValueOrDefault("customer") as Dictionary<string, object?>;
            var custName = loadedCustomer?.GetValueOrDefault("name")?.ToString() ?? "Customer";

            return Ok(new Dictionary<string, object?>
            {
                ["message"] = $"Customer '{custName}' set as Regular Customer with {itemsEl.GetArrayLength()} regular medicines.",
                ["customer"] = loadedCustomer,
                ["invoice"] = loadedInvoice,
                ["next_refill_date"] = nextDate
            });
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    [HttpGet("{saleInvoice:long}/print-url")]
    public async Task<IActionResult> PrintUrl(long saleInvoice, [FromQuery] string? type = null)
    {
        if (!await CanAsync("sale-invoice.view", "sale-invoice.create", "barcode-pos.create"))
        {
            return Forbidden();
        }

        var loaded = await LoadSaleInvoiceAsync(saleInvoice);
        if (loaded == null) return NotFound(new { message = "Sale invoice not found." });

        var typeParam = string.IsNullOrWhiteSpace(type) ? "" : $"&type={Uri.EscapeDataString(type)}";
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var url = $"{baseUrl}/print/sale-invoices/{saleInvoice}?silent_print=1{typeParam}";
        var html = await RenderInvoicePrintHtmlAsync(loaded, type);

        return Ok(new Dictionary<string, object?>
        {
            ["url"] = url,
            ["html"] = html
        });
    }

    [AllowAnonymous]
    [HttpGet("/print/sale-invoices/{invoice:long}")]
    public async Task<IActionResult> PrintWeb(long invoice, [FromQuery] string? type = null)
    {
        var loaded = await LoadSaleInvoiceAsync(invoice);
        if (loaded == null) return NotFound("Sale invoice not found.");

        var html = await RenderInvoicePrintHtmlAsync(loaded, type);
        return Content(html, "text/html; charset=utf-8");
    }

    [AllowAnonymous]
    [HttpGet("/sale-invoices/{invoice:long}/dosage-labels")]
    public async Task<IActionResult> PrintDosageLabels(long invoice, [FromQuery] bool json = false)
    {
        var loaded = await LoadSaleInvoiceAsync(invoice);
        if (loaded == null) return NotFound("Sale invoice not found.");

        var html = await RenderDosageLabelsHtmlAsync(loaded);
        var acceptHeader = Request.Headers["Accept"].ToString();
        if (json || acceptHeader.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            var fullUrl = $"{Request.Scheme}://{Request.Host}{Request.Path}{Request.QueryString}";
            return Ok(new Dictionary<string, object?>
            {
                ["ok"] = true,
                ["url"] = fullUrl,
                ["html"] = html
            });
        }

        return Content(html, "text/html; charset=utf-8");
    }

    // -------------------------------------------------------------------------
    // Stock, Pricing, Credit Limit & Ledger Helpers
    // -------------------------------------------------------------------------

    private async Task CreateItemsAndReduceAsync(
        long invoiceId,
        string invoiceSaleType,
        List<Dictionary<string, object?>> items,
        List<Dictionary<string, object?>>? originalItems,
        Dictionary<long, double>? costs,
        IDbConnection conn,
        IDbTransaction tx)
    {
        costs ??= await _costSnapshotService.ProductCostsAsync(items.Cast<IDictionary<string, object?>>(), conn, tx);
        bool legacy = originalItems != null && originalItems.Count > 0 && originalItems.All(i => i.GetValueOrDefault("cost_price_snapshot") == null);

        var prodIds = items.Select(i => ToLong(i.GetValueOrDefault("product_id"))).Where(pid => pid > 0).Distinct().ToArray();
        var productsMap = new Dictionary<long, Dictionary<string, object?>>();
        if (prodIds.Length > 0)
        {
            var pRows = await Db.QueryListAsync("SELECT * FROM products WHERE id IN @ids", new { ids = prodIds }, conn, tx);
            foreach (var p in pRows) productsMap[ToLong(p["id"])] = p;
        }

        foreach (var raw in items)
        {
            var pid = ToLong(raw.GetValueOrDefault("product_id"));
            productsMap.TryGetValue(pid, out var prod);

            var matches = originalItems == null
                ? new List<Dictionary<string, object?>>()
                : originalItems.Where(o => ToLong(o.GetValueOrDefault("product_id")) == pid).ToList();

            object? cost = legacy
                ? null
                : (matches.Count > 0
                    ? _costSnapshotService.OriginalCost(originalItems!, raw)
                    : (costs.TryGetValue(pid, out var cVal) ? cVal : 0.0));

            var subTotalVal = ToDouble(raw.GetValueOrDefault("sub_total"));
            var itemTaxPct = raw.ContainsKey("tax_percentage") && raw["tax_percentage"] != null
                ? ToDouble(raw["tax_percentage"])
                : ToDouble(prod?.GetValueOrDefault("sales_tax_rate"));
            var itemTaxAmt = raw.ContainsKey("tax_amount") && raw["tax_amount"] != null
                ? ToDouble(raw["tax_amount"])
                : Math.Round(subTotalVal * (itemTaxPct / 100.0), 2);

            var batchNumber = (raw.GetValueOrDefault("batch_number") ?? raw.GetValueOrDefault("batch"))?.ToString();
            var expiry = (raw.GetValueOrDefault("expiry") ?? raw.GetValueOrDefault("expiry_date"))?.ToString();
            var dosage = (raw.GetValueOrDefault("dosage_instruction")?.ToString() ?? "").Trim();
            var effectiveDosage = !string.IsNullOrEmpty(dosage)
                ? dosage
                : (prod?.GetValueOrDefault("default_dosage")?.ToString());

            var payload = new Dictionary<string, object?>
            {
                ["sale_invoice_id"] = invoiceId,
                ["cost_price_snapshot"] = cost,
                ["product_id"] = pid,
                ["pack_size"] = (int)ToLong(raw.GetValueOrDefault("pack_size")),
                ["batch_number"] = string.IsNullOrWhiteSpace(batchNumber) ? null : batchNumber,
                ["expiry"] = string.IsNullOrWhiteSpace(expiry) ? null : expiry,
                ["current_quantity"] = (int)ToLong(raw.GetValueOrDefault("current_quantity")),
                ["pack_quantity"] = Math.Max(0.0, Math.Round(ToDouble(raw.GetValueOrDefault("pack_quantity")), 2)),
                ["unit_quantity"] = Math.Max(0, (int)ToLong(raw.GetValueOrDefault("unit_quantity"))),
                ["quantity"] = (int)ToLong(raw.GetValueOrDefault("quantity")),
                ["pack_bonus"] = Math.Max(0.0, Math.Round(ToDouble(raw.GetValueOrDefault("pack_bonus")), 2)),
                ["unit_bonus"] = Math.Max(0, (int)ToLong(raw.GetValueOrDefault("unit_bonus"))),
                ["bonus"] = Math.Max(0, (int)ToLong(raw.GetValueOrDefault("bonus"))),
                ["pack_price"] = ToDouble(raw.GetValueOrDefault("pack_price")),
                ["price"] = ToDouble(raw.GetValueOrDefault("price")),
                ["item_discount_percentage"] = ToDouble(raw.GetValueOrDefault("item_discount_percentage")),
                ["sub_total"] = subTotalVal,
                ["sale_type"] = raw.GetValueOrDefault("sale_type")?.ToString() ?? invoiceSaleType,
                ["is_custom_price"] = IsTruthy(raw.GetValueOrDefault("is_custom_price")) ? 1 : 0,
                ["applied_scheme_id"] = ToNullableLong(raw.GetValueOrDefault("applied_scheme_id")),
                ["applied_scheme_name"] = raw.GetValueOrDefault("applied_scheme_name")?.ToString()?.Trim(),
                ["hs_code"] = raw.GetValueOrDefault("hs_code") ?? prod?.GetValueOrDefault("hs_code"),
                ["tax_percentage"] = itemTaxPct,
                ["tax_amount"] = itemTaxAmt,
                ["further_tax_amount"] = ToDouble(raw.GetValueOrDefault("further_tax_amount")),
                ["retail_mrp"] = raw.ContainsKey("retail_mrp") && raw["retail_mrp"] != null ? ToDouble(raw["retail_mrp"]) : null,
                ["dosage_instruction"] = string.IsNullOrWhiteSpace(effectiveDosage) ? null : effectiveDosage
            };

            if (IsTruthy(raw.GetValueOrDefault("save_as_product_default")) && !string.IsNullOrEmpty(dosage) && prod != null &&
                await Db.HasColumnAsync("products", "default_dosage"))
            {
                await Db.UpdateAsync("products", pid, new Dictionary<string, object?> { ["default_dosage"] = dosage }, "id", conn, tx);
            }

            await Db.InsertAsync("sale_invoice_items", payload, conn, tx);

            var units = (int)ToLong(payload["quantity"]) + Math.Max(0, (int)ToLong(payload["bonus"]));
            await ApplyStockDeltaSmartAsync(pid, batchNumber, expiry, -units, conn, tx);
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
            var batchNo = item.GetValueOrDefault("batch_number")?.ToString();
            var expiry = item.GetValueOrDefault("expiry")?.ToString();
            var units = (int)ToLong(item.GetValueOrDefault("quantity")) + Math.Max(0, (int)ToLong(item.GetValueOrDefault("bonus")));

            await ApplyStockDeltaSmartAsync(pid, batchNo, expiry, +units, conn, tx);
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

        var matchingBatches = new List<Dictionary<string, object?>>();
        if (!string.IsNullOrEmpty(bTrim) && bTrim != "__unbatched__" && await Db.HasTableAsync("batches"))
        {
            var allBatches = await Db.QueryListAsync("SELECT * FROM batches WHERE product_id = @pid ORDER BY id ASC", new { pid = productId }, conn, tx);
            matchingBatches = _stockAvailabilityService.MatchingBatches(allBatches, new Dictionary<string, object?>
            {
                ["product_id"] = productId,
                ["batch_number"] = bTrim,
                ["expiry"] = eTrim
            });
        }

        if (matchingBatches.Count > 0)
        {
            if (deltaUnits < 0)
            {
                var remainingToDeduct = Math.Abs(deltaUnits);
                var totalAvailable = matchingBatches.Sum(b => (int)ToLong(b.GetValueOrDefault("quantity")));
                if (totalAvailable < remainingToDeduct)
                {
                    throw new InvoiceValidationException("Selected batch has insufficient stock.", new Dictionary<string, string[]>
                    {
                        ["items"] = new[] { "Selected batch has insufficient stock." }
                    });
                }

                foreach (var batch in matchingBatches)
                {
                    if (remainingToDeduct <= 0) break;
                    var before = (int)ToLong(batch.GetValueOrDefault("quantity"));
                    var deduct = Math.Min(before, remainingToDeduct);
                    if (deduct > 0)
                    {
                        await Db.UpdateAsync("batches", batch["id"]!, new Dictionary<string, object?> { ["quantity"] = before - deduct }, "id", conn, tx);
                        remainingToDeduct -= deduct;
                    }
                }
                actualApplied = deltaUnits;
            }
            else
            {
                var firstBatch = matchingBatches[0];
                var before = (int)ToLong(firstBatch.GetValueOrDefault("quantity"));
                var after = before + deltaUnits;
                actualApplied = after - before;
                await Db.UpdateAsync("batches", firstBatch["id"]!, new Dictionary<string, object?> { ["quantity"] = after }, "id", conn, tx);
            }
        }

        var product = await Db.QuerySingleOrDefaultAsync("SELECT * FROM products WHERE id = @pid", new { pid = productId }, conn, tx);
        if (product != null)
        {
            var pBefore = (int)ToLong(product.GetValueOrDefault("quantity"));
            var pAfter = pBefore + actualApplied;
            if (pAfter < 0)
            {
                throw new InvoiceValidationException("Product has insufficient stock.", new Dictionary<string, string[]>
                {
                    ["items"] = new[] { "Product has insufficient stock." }
                });
            }

            if (string.IsNullOrEmpty(bTrim) && deltaUnits < 0 && matchingBatches.Count == 0 && await Db.HasTableAsync("batches"))
            {
                var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
                var activeBatches = await Db.QueryListAsync(@"
                    SELECT * FROM batches
                    WHERE product_id = @pid AND quantity > 0
                    ORDER BY CASE WHEN expiry_date IS NOT NULL AND date(expiry_date) >= date(@today) THEN 0 ELSE 1 END ASC,
                             expiry_date ASC, id ASC",
                    new { pid = productId, today },
                    conn,
                    tx);

                var totalBatchQty = activeBatches.Sum(b => (int)ToLong(b.GetValueOrDefault("quantity")));
                if (totalBatchQty > pAfter)
                {
                    var excessBatchQty = totalBatchQty - pAfter;
                    foreach (var b in activeBatches)
                    {
                        if (excessBatchQty <= 0) break;
                        var bBefore = (int)ToLong(b.GetValueOrDefault("quantity"));
                        var deduct = Math.Min(bBefore, excessBatchQty);
                        if (deduct > 0)
                        {
                            await Db.UpdateAsync("batches", b["id"]!, new Dictionary<string, object?> { ["quantity"] = bBefore - deduct }, "id", conn, tx);
                            excessBatchQty -= deduct;
                        }
                    }
                }
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

    private async Task ValidateCreditLimitsAsync(
        Dictionary<string, object?> data,
        double? clientSubmittedTotal,
        Dictionary<string, object?>? existingInvoice)
    {
        var invoiceType = (data.GetValueOrDefault("invoice_type")?.ToString() ?? existingInvoice?.GetValueOrDefault("invoice_type")?.ToString() ?? "debit").ToLowerInvariant();
        var customerId = ToLong(data.GetValueOrDefault("customer_id") ?? existingInvoice?.GetValueOrDefault("customer_id"));
        var customer = customerId > 0 && await Db.HasTableAsync("customers")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = customerId })
            : null;

        var total = Math.Round(ToDouble(data.GetValueOrDefault("total")), 2);
        var received = Math.Round(ToDouble(data.GetValueOrDefault("total_receive")), 2);
        var creditApplied = Math.Round(ToDouble(data.GetValueOrDefault("apply_credit_amount")), 2);
        var unpaidAmount = Math.Round(Math.Max(total - received - creditApplied, 0.0), 2);
        var clientTotal = clientSubmittedTotal.HasValue && clientSubmittedTotal.Value > 0 ? clientSubmittedTotal.Value : total;
        var cashCheckTarget = Math.Min(total, clientTotal);

        bool cashGivenUnderpaid = data.TryGetValue("cash_given", out var cgObj) && cgObj != null &&
                                  double.TryParse(cgObj.ToString(), out var cgVal) &&
                                  Math.Round(cgVal + creditApplied, 2) < (cashCheckTarget - 0.009);

        if (customer != null && IsWalkInCustomer(customer))
        {
            if (invoiceType == "credit" || unpaidAmount > 0.009 || cashGivenUnderpaid)
            {
                const string walkInMsg = "Walk-In Customer bills cannot be saved with unpaid credit (Udhar)—select or create a named Customer if the bill is not paid in full.";
                throw new InvoiceValidationException(walkInMsg, new Dictionary<string, string[]>
                {
                    ["customer_id"] = new[] { walkInMsg },
                    ["total_receive"] = new[] { walkInMsg }
                });
            }
        }

        if (invoiceType != "credit" || customer == null) return;

        var custName = customer.GetValueOrDefault("name")?.ToString() ?? "Customer";
        var creditStatus = (customer.GetValueOrDefault("credit_status")?.ToString() ?? "active").ToLowerInvariant();

        if (creditStatus == "blocked")
        {
            var msg = $"Customer {custName} is blocked for credit transactions.";
            throw new InvoiceValidationException(msg, statusCode: 422, customPayload: new Dictionary<string, object?>
            {
                ["message"] = msg,
                ["credit_status"] = "blocked",
                ["errors"] = new Dictionary<string, string[]>
                {
                    ["customer_id"] = new[] { msg },
                    ["credit_status"] = new[] { msg }
                }
            });
        }

        bool creditOverride = IsTruthy(data.GetValueOrDefault("credit_override"));
        var authorizingUserId = await _calculationService.ResolveAuthorizingUserIdAsync(CurrentUserId, data, "sale-invoice.credit-override");
        bool canOverride = await _calculationService.CanOverrideCreditAsync(authorizingUserId);

        if (creditStatus == "on_hold")
        {
            if (!creditOverride)
            {
                var msg = $"Customer {custName} account is on hold. Requires supervisor override.";
                throw new InvoiceValidationException(msg, statusCode: 422, customPayload: new Dictionary<string, object?>
                {
                    ["message"] = msg,
                    ["requires_override"] = true,
                    ["credit_status"] = "on_hold",
                    ["errors"] = new Dictionary<string, string[]>
                    {
                        ["credit_status"] = new[] { msg }
                    }
                });
            }
            if (!canOverride)
            {
                throw new InvoiceValidationException(
                    "You do not have permission to override customer credit holds.",
                    new Dictionary<string, string[]>
                    {
                        ["credit_override"] = new[] { "User does not have permission to authorize credit overrides." }
                    },
                    statusCode: 403);
            }
        }

        var creditLimit = ToDouble(customer.GetValueOrDefault("credit_limit"));
        if (creditLimit > 0)
        {
            var currentNet = await GetCustomerNetBalanceAsync(customerId);
            if (existingInvoice != null && string.Equals(existingInvoice.GetValueOrDefault("invoice_type")?.ToString(), "credit", StringComparison.OrdinalIgnoreCase))
            {
                var oldTotal = ToDouble(existingInvoice.GetValueOrDefault("total"));
                var oldRecv = Math.Min(oldTotal, ToDouble(existingInvoice.GetValueOrDefault("total_receive")));
                var oldUnpaid = Math.Max(0.0, oldTotal - oldRecv);
                currentNet = Math.Max(0.0, currentNet - oldUnpaid);
            }

            var newRecv = Math.Min(total, received);
            var newUnpaid = Math.Max(0.0, total - newRecv);
            var projected = Math.Round(currentNet + newUnpaid, 2);

            if (projected > creditLimit)
            {
                if (!creditOverride)
                {
                    var excess = Math.Round(projected - creditLimit, 2);
                    var msg = $"Customer credit limit of Rs. {creditLimit:N2} exceeded by Rs. {excess:N2} (projected balance: Rs. {projected:N2}). Requires supervisor override.";
                    throw new InvoiceValidationException(msg, statusCode: 422, customPayload: new Dictionary<string, object?>
                    {
                        ["message"] = msg,
                        ["requires_override"] = true,
                        ["excess_amount"] = excess,
                        ["credit_limit"] = creditLimit,
                        ["current_balance"] = currentNet,
                        ["projected_balance"] = projected,
                        ["errors"] = new Dictionary<string, string[]>
                        {
                            ["credit_limit"] = new[] { $"Customer credit limit of Rs. {creditLimit:N2} exceeded by Rs. {excess:N2}. Requires supervisor override." }
                        }
                    });
                }

                if (!canOverride)
                {
                    throw new InvoiceValidationException(
                        "You do not have permission to override customer credit limits.",
                        new Dictionary<string, string[]>
                        {
                            ["credit_override"] = new[] { "User does not have permission to authorize credit overrides." }
                        },
                        statusCode: 403);
                }
            }
        }
    }

    private async Task<double> GetCustomerNetBalanceAsync(long customerId)
    {
        if (customerId <= 0 || !await Db.HasTableAsync("customer_ledgers")) return 0.0;
        var net = await Db.ExecuteScalarAsync<double?>(@"
            SELECT COALESCE(SUM(COALESCE(invoice_total, 0) - COALESCE(total_received, 0) - COALESCE(credited_amount, 0)), 0)
            FROM customer_ledgers
            WHERE customer_id = @cid",
            new { cid = customerId });
        return Math.Round(net ?? 0.0, 2);
    }

    private async Task ResolveDoctorAndPatientDataAsync(Dictionary<string, object?> data)
    {
        Dictionary<string, object?>? doctor = null;
        var docId = ToLong(data.GetValueOrDefault("doctor_id"));
        var docName = (data.GetValueOrDefault("doctor_name")?.ToString() ?? "").Trim();

        if (await Db.HasTableAsync("doctors"))
        {
            if (docId > 0)
            {
                doctor = await Db.QuerySingleOrDefaultAsync("SELECT * FROM doctors WHERE id = @id", new { id = docId });
            }
            else if (!string.IsNullOrEmpty(docName))
            {
                doctor = await Db.QuerySingleOrDefaultAsync("SELECT * FROM doctors WHERE name = @name LIMIT 1", new { name = docName });
                if (doctor != null)
                {
                    data["doctor_id"] = doctor["id"];
                }
            }
        }

        if (doctor != null)
        {
            if (string.IsNullOrEmpty(docName)) data["doctor_name"] = doctor.GetValueOrDefault("name");
            if (string.IsNullOrWhiteSpace(data.GetValueOrDefault("doctor_pmdc")?.ToString()) && doctor.GetValueOrDefault("pmdc_number") != null)
            {
                data["doctor_pmdc"] = doctor["pmdc_number"];
            }
            if (string.IsNullOrWhiteSpace(data.GetValueOrDefault("doctor_hospital")?.ToString()) && doctor.GetValueOrDefault("hospital_clinic") != null)
            {
                data["doctor_hospital"] = doctor["hospital_clinic"];
            }
        }

        var customerId = ToLong(data.GetValueOrDefault("customer_id"));
        if (customerId > 0 && await Db.HasTableAsync("customers"))
        {
            var customer = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = customerId });
            if (customer != null && !IsWalkInCustomer(customer))
            {
                if (string.IsNullOrWhiteSpace(data.GetValueOrDefault("patient_name")?.ToString()))
                {
                    data["patient_name"] = customer.GetValueOrDefault("name");
                }
                if (string.IsNullOrWhiteSpace(data.GetValueOrDefault("patient_cnic")?.ToString()) && customer.GetValueOrDefault("cnic") != null)
                {
                    data["patient_cnic"] = customer["cnic"];
                }
                if (string.IsNullOrWhiteSpace(data.GetValueOrDefault("patient_address")?.ToString()) && customer.GetValueOrDefault("address") != null)
                {
                    data["patient_address"] = customer["address"];
                }
            }
        }
    }

    private static bool IsWalkInCustomer(Dictionary<string, object?> customer)
    {
        var name = (customer.GetValueOrDefault("name")?.ToString() ?? "").Trim().ToLowerInvariant();
        return name is "walk-in customer" or "walk in customer" or "walk-in" or "walkin" or "counter sale";
    }

    private async Task<string> ResolveDueDateAsync(
        Dictionary<string, object?> data,
        long customerId,
        string date,
        IDbConnection conn,
        IDbTransaction tx)
    {
        var explicitDue = data.GetValueOrDefault("due_date")?.ToString();
        if (!string.IsNullOrWhiteSpace(explicitDue) && DateTime.TryParse(explicitDue, out var dt))
        {
            return dt.ToString("yyyy-MM-dd");
        }

        int creditDays = 30;
        if (customerId > 0 && await Db.HasTableAsync("customers"))
        {
            var cust = await Db.QuerySingleOrDefaultAsync("SELECT credit_days FROM customers WHERE id = @id", new { id = customerId }, conn, tx);
            if (cust != null && cust.GetValueOrDefault("credit_days") != null)
            {
                creditDays = (int)ToLong(cust["credit_days"]);
            }
        }

        var baseDt = DateTime.TryParse(date, out var pDt) ? pDt : DateTime.UtcNow;
        return baseDt.AddDays(creditDays).ToString("yyyy-MM-dd");
    }

    private async Task SaveCustomWholesalePricesAsync(
        long customerId,
        List<Dictionary<string, object?>> items,
        IDbConnection conn,
        IDbTransaction tx)
    {
        foreach (var raw in items)
        {
            if (!IsTruthy(raw.GetValueOrDefault("is_custom_price"))) continue;
            var pid = ToLong(raw.GetValueOrDefault("product_id"));
            if (pid <= 0) continue;

            var packSize = Math.Max(1, (int)ToLong(raw.GetValueOrDefault("pack_size") ?? 1));
            var packPrice = ToDouble(raw.GetValueOrDefault("pack_price"));
            var price = ToDouble(raw.GetValueOrDefault("price"));
            var packPriceToSave = packPrice > 0 ? Math.Round(packPrice, 2) : Math.Round(price * packSize, 2);

            var existing = await Db.QuerySingleOrDefaultAsync(
                "SELECT id FROM customer_whole_sale_prices WHERE customer_id = @cid AND product_id = @pid LIMIT 1",
                new { cid = customerId, pid },
                conn,
                tx);

            if (existing != null)
            {
                await Db.UpdateAsync("customer_whole_sale_prices", existing["id"]!, new Dictionary<string, object?>
                {
                    ["pack_price"] = packPriceToSave
                }, "id", conn, tx);
            }
            else
            {
                await Db.InsertAsync("customer_whole_sale_prices", new Dictionary<string, object?>
                {
                    ["customer_id"] = customerId,
                    ["product_id"] = pid,
                    ["pack_price"] = packPriceToSave
                }, conn, tx);
            }
        }
    }

    private async Task ApplyCustomerCreditsToInvoiceAsync(
        long customerId,
        long invoiceId,
        double creditToApply,
        IDbConnection conn,
        IDbTransaction tx)
    {
        if (!await Db.HasTableAsync("payments") || !await Db.HasTableAsync("payment_allocations")) return;

        var openCredits = await Db.QueryListAsync(@"
            SELECT * FROM payments
            WHERE party_type = 'customer' AND party_id = @cid
              AND COALESCE(unapplied_amount, 0) > 0
              AND (status IS NULL OR status = 'posted')
            ORDER BY payment_date ASC, id ASC",
            new { cid = customerId },
            conn,
            tx);

        double remainingToApply = creditToApply;
        double totalApplied = 0.0;

        foreach (var pay in openCredits)
        {
            if (remainingToApply <= 0.001) break;
            var payId = ToLong(pay["id"]);
            var unapplied = ToDouble(pay.GetValueOrDefault("unapplied_amount"));
            var allocated = ToDouble(pay.GetValueOrDefault("allocated_amount"));
            var allocNow = Math.Round(Math.Min(unapplied, remainingToApply), 2);
            if (allocNow <= 0) continue;

            await Db.InsertAsync("payment_allocations", new Dictionary<string, object?>
            {
                ["payment_id"] = payId,
                ["invoice_type"] = "sale_invoice",
                ["invoice_id"] = invoiceId,
                ["amount"] = allocNow
            }, conn, tx);

            await Db.UpdateAsync("payments", payId, new Dictionary<string, object?>
            {
                ["allocated_amount"] = Math.Round(allocated + allocNow, 2),
                ["unapplied_amount"] = Math.Round(Math.Max(0.0, unapplied - allocNow), 2)
            }, "id", conn, tx);

            remainingToApply = Math.Round(remainingToApply - allocNow, 2);
            totalApplied = Math.Round(totalApplied + allocNow, 2);
        }

        if (totalApplied > 0)
        {
            await Db.ExecuteAsync(
                "UPDATE sale_invoices SET total_receive = ROUND(COALESCE(total_receive, 0) + @amt, 2) WHERE id = @invoiceId",
                new { amt = totalApplied, invoiceId },
                conn,
                tx);
        }
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

        var allocs = await Db.QueryListAsync(
            "SELECT * FROM payment_allocations WHERE invoice_type = @invoiceType AND invoice_id = @invoiceId",
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

            await Db.ExecuteAsync(
                "UPDATE sale_invoices SET total_receive = ROUND(MAX(COALESCE(total_receive, 0) - @amt, 0), 2) WHERE id = @invoiceId",
                new { amt, invoiceId },
                conn,
                tx);

            await Db.ExecuteAsync("DELETE FROM payment_allocations WHERE id = @id", new { id = alloc["id"] }, conn, tx);
        }
    }

    private async Task<Dictionary<string, object?>?> LoadSaleInvoiceAsync(long id, long? excludeReturnId = null)
    {
        var inv = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id });
        if (inv == null) return null;

        await EnrichInvoiceBatchAsync(new List<Dictionary<string, object?>> { inv });

        if (inv.GetValueOrDefault("customer") is Dictionary<string, object?> cust &&
            await Db.HasTableAsync("customer_regular_items"))
        {
            var cid = ToLong(cust["id"]);
            var regItems = await Db.QueryListAsync("SELECT * FROM customer_regular_items WHERE customer_id = @cid", new { cid });
            foreach (var ri in regItems)
            {
                var rpid = ToLong(ri.GetValueOrDefault("product_id"));
                ri["product"] = rpid > 0
                    ? await Db.QuerySingleOrDefaultAsync(
                        "SELECT id, name, product_code, quantity, pack_size, unit_sale_price, pack_sale_price FROM products WHERE id = @pid",
                        new { pid = rpid })
                    : null;
            }
            cust["regular_items"] = regItems;
            cust["regularItems"] = regItems;
        }

        var items = await Db.QueryListAsync("SELECT * FROM sale_invoice_items WHERE sale_invoice_id = @id", new { id });
        var prodIds = items.Select(i => ToLong(i.GetValueOrDefault("product_id"))).Where(pid => pid > 0).Distinct().ToArray();

        var prods = new Dictionary<long, Dictionary<string, object?>>();
        if (prodIds.Length > 0 && await Db.HasTableAsync("products"))
        {
            var pRows = await Db.QueryListAsync("SELECT * FROM products WHERE id IN @ids", new { ids = prodIds });
            foreach (var p in pRows) prods[ToLong(p["id"])] = p;
        }

        foreach (var it in items)
        {
            var pid = ToLong(it.GetValueOrDefault("product_id"));
            it["product"] = pid > 0 && prods.TryGetValue(pid, out var p) ? p : null;
        }

        await _entitlementService.AnnotateAsync("sale", id, items, excludeReturnId);
        inv["items"] = items;

        var docId = ToLong(inv.GetValueOrDefault("doctor_id"));
        inv["doctor"] = docId > 0 && await Db.HasTableAsync("doctors")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM doctors WHERE id = @id", new { id = docId })
            : null;

        var prescPath = inv.GetValueOrDefault("prescription_image_path")?.ToString();
        inv["prescription_image_url"] = !string.IsNullOrWhiteSpace(prescPath) ? $"/storage/{prescPath}" : null;

        return inv;
    }

    private async Task<List<Dictionary<string, object?>>> EnrichInvoiceBatchAsync(List<Dictionary<string, object?>> rows)
    {
        if (rows.Count == 0) return rows;

        var custIds = rows.Select(r => ToLong(r.GetValueOrDefault("customer_id"))).Where(id => id > 0).Distinct().ToArray();
        var userIds = rows.Select(r => ToLong(r.GetValueOrDefault("user_id"))).Where(id => id > 0).Distinct().ToArray();
        var empIds = rows.Select(r => ToLong(r.GetValueOrDefault("salesman_id"))).Where(id => id > 0).Distinct().ToArray();

        var customers = new Dictionary<long, Dictionary<string, object?>>();
        if (custIds.Length > 0 && await Db.HasTableAsync("customers"))
        {
            var cRows = await Db.QueryListAsync("SELECT * FROM customers WHERE id IN @ids", new { ids = custIds });
            foreach (var cr in cRows) customers[ToLong(cr["id"])] = cr;
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
            var hasEmpCode = await Db.HasColumnAsync("employees", "employee_code");
            var empSql = hasEmpCode
                ? "SELECT id, name, employee_code FROM employees WHERE id IN @ids"
                : "SELECT id, name FROM employees WHERE id IN @ids";
            var eRows = await Db.QueryListAsync(empSql, new { ids = empIds });
            foreach (var er in eRows) employees[ToLong(er["id"])] = er;
        }

        foreach (var r in rows)
        {
            var cid = ToLong(r.GetValueOrDefault("customer_id"));
            var uid = ToLong(r.GetValueOrDefault("user_id"));
            var sid = ToLong(r.GetValueOrDefault("salesman_id"));

            r["customer"] = cid > 0 && customers.TryGetValue(cid, out var c) ? c : null;
            r["user"] = uid > 0 && users.TryGetValue(uid, out var u) ? u : null;
            r["salesman"] = sid > 0 && employees.TryGetValue(sid, out var e) ? e : null;
        }

        return rows;
    }

    private async Task<string> RenderInvoicePrintHtmlAsync(Dictionary<string, object?> invoice, string? type)
    {
        var setting = await Db.HasTableAsync("settings")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
            : null;

        var storeName = WebUtility.HtmlEncode(setting?.GetValueOrDefault("store_name")?.ToString() ?? "KAROBAR PHARMACY & STORE");
        var storePhone = WebUtility.HtmlEncode(setting?.GetValueOrDefault("phone_number")?.ToString() ?? "");
        var storeAddress = WebUtility.HtmlEncode(setting?.GetValueOrDefault("address")?.ToString() ?? "");
        var postedNumber = WebUtility.HtmlEncode(invoice.GetValueOrDefault("posted_number")?.ToString() ?? "");
        var date = WebUtility.HtmlEncode(invoice.GetValueOrDefault("date")?.ToString() ?? "");
        var cust = invoice.GetValueOrDefault("customer") as Dictionary<string, object?>;
        var custName = WebUtility.HtmlEncode(cust?.GetValueOrDefault("name")?.ToString() ?? "Walk-In Customer");
        var total = ToDouble(invoice.GetValueOrDefault("total"));
        var received = ToDouble(invoice.GetValueOrDefault("total_receive"));
        var discount = ToDouble(invoice.GetValueOrDefault("discount_amount"));
        var tax = ToDouble(invoice.GetValueOrDefault("tax_amount"));
        var items = invoice.GetValueOrDefault("items") as List<Dictionary<string, object?>> ?? new List<Dictionary<string, object?>>();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\" /><title>Invoice " + postedNumber + "</title>");
        sb.AppendLine("<style>body{font-family:system-ui,-apple-system,sans-serif;margin:12px;color:#0f172a;font-size:12px;}table{width:100%;border-collapse:collapse;margin-top:10px;}th,td{border-bottom:1px solid #e2e8f0;padding:5px 4px;text-align:left;}th{font-weight:700;background:#f8fafc;}.right{text-align:right;}.center{text-align:center;}</style></head><body>");
        sb.AppendLine($"<div class=\"center\"><h2 style=\"margin:0 0 4px;\">{storeName}</h2><div>{storeAddress}</div><div>{storePhone}</div></div>");
        sb.AppendLine($"<hr/><div style=\"display:flex;justify-content:space-between;\"><div><strong>Invoice:</strong> {postedNumber}<br/><strong>Customer:</strong> {custName}</div><div><strong>Date:</strong> {date}</div></div>");
        sb.AppendLine("<table><thead><tr><th>Item</th><th class=\"right\">Qty</th><th class=\"right\">Price</th><th class=\"right\">Disc%</th><th class=\"right\">Total</th></tr></thead><tbody>");

        foreach (var it in items)
        {
            var prod = it.GetValueOrDefault("product") as Dictionary<string, object?>;
            var pName = WebUtility.HtmlEncode(prod?.GetValueOrDefault("name")?.ToString() ?? $"Product #{it.GetValueOrDefault("product_id")}");
            var qty = ToLong(it.GetValueOrDefault("quantity"));
            var price = ToDouble(it.GetValueOrDefault("price"));
            var discPct = ToDouble(it.GetValueOrDefault("item_discount_percentage"));
            var subTotal = ToDouble(it.GetValueOrDefault("sub_total"));
            sb.AppendLine($"<tr><td>{pName}</td><td class=\"right\">{qty}</td><td class=\"right\">{price:N2}</td><td class=\"right\">{discPct:N1}%</td><td class=\"right\">{subTotal:N2}</td></tr>");
        }

        sb.AppendLine("</tbody></table>");
        sb.AppendLine($"<div style=\"margin-top:10px;text-align:right;\"><div><strong>Discount:</strong> {discount:N2}</div><div><strong>Tax:</strong> {tax:N2}</div><div style=\"font-size:14px;\"><strong>Total:</strong> {total:N2}</div><div><strong>Received:</strong> {received:N2}</div></div>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private async Task<string> RenderDosageLabelsHtmlAsync(Dictionary<string, object?> invoice)
    {
        var setting = await Db.HasTableAsync("settings")
            ? await Db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
            : null;

        var storeName = WebUtility.HtmlEncode(setting?.GetValueOrDefault("store_name")?.ToString() ?? "Karobar Pharmacy");
        var postedNumber = WebUtility.HtmlEncode(invoice.GetValueOrDefault("posted_number")?.ToString() ?? "");
        var cust = invoice.GetValueOrDefault("customer") as Dictionary<string, object?>;
        var patientName = WebUtility.HtmlEncode(invoice.GetValueOrDefault("patient_name")?.ToString() ?? cust?.GetValueOrDefault("name")?.ToString() ?? "Patient");
        var items = invoice.GetValueOrDefault("items") as List<Dictionary<string, object?>> ?? new List<Dictionary<string, object?>>();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\" /><title>Dosage Labels " + postedNumber + "</title>");
        sb.AppendLine("<style>body{font-family:system-ui,-apple-system,sans-serif;margin:0;padding:4px;font-size:11px;}.label{border:1px solid #000;padding:6px;margin-bottom:6px;page-break-inside:avoid;}</style></head><body>");

        foreach (var it in items)
        {
            var dosage = (it.GetValueOrDefault("dosage_instruction")?.ToString() ?? "").Trim();
            if (string.IsNullOrEmpty(dosage)) continue;

            var prod = it.GetValueOrDefault("product") as Dictionary<string, object?>;
            var pName = WebUtility.HtmlEncode(prod?.GetValueOrDefault("name")?.ToString() ?? $"Product #{it.GetValueOrDefault("product_id")}");
            var qty = ToLong(it.GetValueOrDefault("quantity"));
            sb.AppendLine($"<div class=\"label\"><div><strong>{storeName}</strong> - {postedNumber}</div><div><strong>Patient:</strong> {patientName}</div><div><strong>{pName}</strong> (Qty: {qty})</div><div style=\"margin-top:4px;font-weight:700;\">{WebUtility.HtmlEncode(dosage)}</div></div>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    public static (Dictionary<string, object?> Data, List<Dictionary<string, object?>> Items, List<Dictionary<string, object?>> SplitPayments) ParseSalePayload(Dictionary<string, JsonElement> body)
    {
        var data = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var items = new List<Dictionary<string, object?>>();
        var splitPayments = new List<Dictionary<string, object?>>();

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
            else if (string.Equals(kv.Key, "split_payments", StringComparison.OrdinalIgnoreCase) && kv.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var legEl in kv.Value.EnumerateArray())
                {
                    if (legEl.ValueKind == JsonValueKind.Object)
                    {
                        var legDict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                        foreach (var prop in legEl.EnumerateObject())
                        {
                            legDict[prop.Name] = KarobarDb.PrepareDbValue(prop.Value);
                        }
                        splitPayments.Add(legDict);
                    }
                }
            }
            else
            {
                data[kv.Key] = KarobarDb.PrepareDbValue(kv.Value);
            }
        }

        return (data, items, splitPayments);
    }

    private static bool IsTruthy(object? val)
    {
        if (val == null) return false;
        if (val is bool b) return b;
        if (val is long l) return l != 0;
        if (val is int i) return i != 0;
        if (val is string s) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        return false;
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

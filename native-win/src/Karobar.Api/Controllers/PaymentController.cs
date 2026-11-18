using System.Text.Json;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/payments")]
public class PaymentController : ApiControllerBase
{
    private readonly PaymentService _paymentService;

    public PaymentController(KarobarDb db) : base(db)
    {
        _paymentService = new PaymentService(db);
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await CanAsync("payment.view", "customer-ledger.view", "supplier-ledger.view"))
            return Forbidden();

        var partyType = Request.Query["party_type"].ToString();
        var partyIdStr = Request.Query["party_id"].ToString();
        long? partyId = long.TryParse(partyIdStr, out var pid) ? pid : null;

        await _paymentService.SyncMissingReturnCreditPaymentsAsync(
            !string.IsNullOrWhiteSpace(partyType) ? partyType : null,
            partyId);

        var where = new List<string>();
        var dyn = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(partyType))
        {
            where.Add("party_type = @partyType");
            dyn.Add("partyType", partyType);
        }
        if (partyId.HasValue)
        {
            where.Add("party_id = @partyId");
            dyn.Add("partyId", partyId.Value);
        }

        var status = Request.Query["status"].ToString();
        if (!string.IsNullOrWhiteSpace(status))
        {
            where.Add("status = @status");
            dyn.Add("status", status);
        }

        var paymentMethod = Request.Query["payment_method"].ToString();
        if (!string.IsNullOrWhiteSpace(paymentMethod))
        {
            where.Add("payment_method = @paymentMethod");
            dyn.Add("paymentMethod", paymentMethod);
        }

        var hasChequeStatus = await Db.HasColumnAsync("payments", "cheque_status");
        var chequeStatus = Request.Query["cheque_status"].ToString();
        if (!string.IsNullOrWhiteSpace(chequeStatus) && hasChequeStatus)
        {
            where.Add("cheque_status = @chequeStatus");
            dyn.Add("chequeStatus", chequeStatus);
        }

        var unappliedOnly = string.Equals(Request.Query["unapplied_only"], "1", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(Request.Query["unapplied_only"], "true", StringComparison.OrdinalIgnoreCase);
        if (unappliedOnly)
        {
            where.Add("status = 'posted' AND unapplied_amount > 0");
        }

        var from = Request.Query["from"].ToString();
        if (!string.IsNullOrWhiteSpace(from))
        {
            where.Add("DATE(payment_date) >= @from");
            dyn.Add("from", from);
        }

        var to = Request.Query["to"].ToString();
        if (!string.IsNullOrWhiteSpace(to))
        {
            where.Add("DATE(payment_date) <= @to");
            dyn.Add("to", to);
        }

        var search = Request.Query["search"].ToString()?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            where.Add("(payment_number LIKE @search OR reference LIKE @search OR notes LIKE @search)");
            dyn.Add("search", $"%{search}%");
        }

        var whereClause = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";
        var baseAnd = where.Count > 0 ? whereClause + " AND " : "WHERE ";
        var chequeFilter = hasChequeStatus ? " AND (cheque_status IS NULL OR cheque_status = 'cleared')" : "";

        var cashCustomerTotal = await Db.ExecuteScalarAsync<decimal?>(
            $"SELECT COALESCE(SUM(amount), 0) FROM payments {baseAnd} status = 'posted' AND party_type = 'customer' AND (payment_method IS NULL OR payment_method != 'credit_note'){chequeFilter}",
            dyn) ?? 0m;

        var cashSupplierTotal = await Db.ExecuteScalarAsync<decimal?>(
            $"SELECT COALESCE(SUM(amount), 0) FROM payments {baseAnd} status = 'posted' AND party_type = 'supplier' AND (payment_method IS NULL OR payment_method != 'credit_note'){chequeFilter}",
            dyn) ?? 0m;

        var reversalsCount = await Db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(1) FROM payments {baseAnd} status = 'reversed'",
            dyn);

        var page = int.TryParse(Request.Query["page"], out var pg) ? Math.Max(1, pg) : 1;
        var perPage = int.TryParse(Request.Query["per_page"], out var pp) ? Math.Clamp(pp, 1, 200) : 15;

        var payload = await Db.PaginateAsync(
            $"SELECT * FROM payments {whereClause} ORDER BY payment_date DESC, id DESC",
            $"SELECT COUNT(1) FROM payments {whereClause}",
            dyn,
            page,
            perPage,
            async batch =>
            {
                for (int i = 0; i < batch.Count; i++)
                {
                    batch[i] = await _paymentService.EnrichPaymentAsync(batch[i]);
                }
                return batch;
            });

        payload["summary"] = new Dictionary<string, object?>
        {
            ["customer_total"] = Math.Round(cashCustomerTotal, 2),
            ["supplier_total"] = Math.Round(cashSupplierTotal, 2),
            ["net_cash_flow"] = Math.Round(cashCustomerTotal - cashSupplierTotal, 2),
            ["reversals"] = reversalsCount
        };

        return Ok(payload);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("payment.view", "customer-ledger.view", "supplier-ledger.view"))
            return Forbidden();

        var payment = await Db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id });
        if (payment == null) return NotFound(new { message = "Payment not found." });

        return Ok(await _paymentService.EnrichPaymentAsync(payment));
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("payment.create", "customer-ledger.create", "supplier-ledger.create"))
            return Forbidden();

        var body = await ReadJsonBodyAsync();
        var partyType = GetString(body, "party_type")?.Trim()?.ToLowerInvariant();
        var partyId = GetNullableLong(body, "party_id") ?? 0;
        var amount = GetDecimal(body, "amount", 0m);
        var paymentDate = GetString(body, "payment_date");
        var paymentMethod = GetString(body, "payment_method");

        if (partyType is not ("customer" or "supplier"))
            return Unprocessable("Invalid party_type.", new() { ["party_type"] = new[] { "The selected party type is invalid." } });
        if (partyId <= 0)
            return Unprocessable("party_id is required.", new() { ["party_id"] = new[] { "The party id field is required." } });
        if (amount < 0.01m)
            return Unprocessable("Payment amount must be at least 0.01.", new() { ["amount"] = new[] { "Payment amount must be at least 0.01." } });
        if (string.IsNullOrWhiteSpace(paymentDate))
            return Unprocessable("payment_date is required.", new() { ["payment_date"] = new[] { "The payment date field is required." } });
        if (string.IsNullOrWhiteSpace(paymentMethod))
            return Unprocessable("payment_method is required.", new() { ["payment_method"] = new[] { "The payment method field is required." } });

        var data = ConvertBodyToDictionary(body);

        try
        {
            var payment = partyType == "customer"
                ? await _paymentService.RecordCustomerPaymentAsync(partyId, data, CurrentUserId)
                : await _paymentService.RecordSupplierPaymentAsync(partyId, data, CurrentUserId);

            return StatusCode(201, new Dictionary<string, object?>
            {
                ["message"] = "Payment posted successfully",
                ["payment"] = payment
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message, new() { ["allocations"] = new[] { ex.Message } });
        }
    }

    [HttpPost("{id:long}/reverse")]
    public async Task<IActionResult> Reverse(long id)
    {
        if (!await CanAsync("payment.reverse", "payment.delete", "payment.create"))
            return Forbidden();

        var body = await ReadJsonBodyAsync();
        var reason = GetString(body, "reason")?.Trim();
        if (string.IsNullOrWhiteSpace(reason) || reason.Length < 3)
        {
            return Unprocessable("A reversal reason of at least 3 characters is required.", new()
            {
                ["reason"] = new[] { "The reason field must be at least 3 characters." }
            });
        }

        try
        {
            var reversed = await _paymentService.ReversePaymentAsync(id, reason, CurrentUserId);
            return Ok(new Dictionary<string, object?>
            {
                ["message"] = "Payment reversed successfully",
                ["payment"] = reversed
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message, new() { ["payment"] = new[] { ex.Message } });
        }
    }

    [HttpGet("open-invoices")]
    public async Task<IActionResult> OpenInvoices()
    {
        if (!await CanAsync("payment.view", "payment.create", "customer-ledger.view", "supplier-ledger.view"))
            return Forbidden();

        var partyType = Request.Query["party_type"].ToString()?.ToLowerInvariant();
        var partyId = long.TryParse(Request.Query["party_id"], out var pid) ? pid : 0L;

        if (partyType is not ("customer" or "supplier") || partyId <= 0)
        {
            return Unprocessable("party_type and party_id are required.");
        }

        var invoices = partyType == "customer"
            ? await _paymentService.GetOpenInvoicesForCustomerAsync(partyId)
            : await _paymentService.GetOpenInvoicesForSupplierAsync(partyId);

        return Ok(invoices);
    }

    [HttpPost("{id:long}/allocate")]
    public async Task<IActionResult> Allocate(long id)
    {
        if (!await CanAsync("payment.create", "payment.update"))
            return Forbidden();

        var body = await ReadJsonBodyAsync();
        if (!body.TryGetValue("allocations", out var allocEl) || allocEl.ValueKind != JsonValueKind.Array || allocEl.GetArrayLength() == 0)
        {
            return Unprocessable("The allocations field is required.", new() { ["allocations"] = new[] { "The allocations field is required." } });
        }

        var allocations = ParseAllocationsArray(allocEl);

        try
        {
            var updated = await _paymentService.AllocatePaymentAsync(id, allocations, CurrentUserId);
            return Ok(new Dictionary<string, object?>
            {
                ["message"] = "Credit allocated successfully",
                ["payment"] = updated
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message, new() { ["allocations"] = new[] { ex.Message } });
        }
    }

    [HttpGet("party-credits")]
    public async Task<IActionResult> PartyCredits()
    {
        if (!await CanAsync("payment.view", "customer-ledger.view", "supplier-ledger.view"))
            return Forbidden();

        var filters = new Dictionary<string, object?>
        {
            ["party_type"] = Request.Query["party_type"].ToString() is { Length: > 0 } pt ? pt : "all",
            ["status"] = Request.Query["status"].ToString() is { Length: > 0 } st ? st : "all",
            ["search"] = Request.Query["search"].ToString(),
            ["min_amount"] = Request.Query["min_amount"].ToString(),
            ["sort"] = Request.Query["sort"].ToString() is { Length: > 0 } srt ? srt : "highest"
        };

        return Ok(await _paymentService.GetPartyCreditsSummaryAsync(filters));
    }

    [HttpGet("party-due-details")]
    public async Task<IActionResult> PartyDueDetails()
    {
        if (!await CanAsync("payment.view", "customer-ledger.view", "supplier-ledger.view"))
            return Forbidden();

        var partyType = Request.Query["party_type"].ToString()?.ToLowerInvariant();
        var partyId = long.TryParse(Request.Query["party_id"], out var pid) ? pid : 0L;

        if (partyType is not ("customer" or "supplier") || partyId <= 0)
        {
            return Unprocessable("party_type and party_id are required.");
        }

        try
        {
            return Ok(await _paymentService.GetPartyDueDetailsAsync(partyType, partyId));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("available-credits")]
    public async Task<IActionResult> AvailableCredits()
    {
        if (!await CanAsync("payment.view", "payment.create", "customer-ledger.view", "supplier-ledger.view"))
            return Forbidden();

        var partyType = Request.Query["party_type"].ToString()?.ToLowerInvariant();
        var partyId = long.TryParse(Request.Query["party_id"], out var pid) ? pid : 0L;

        if (partyType is not ("customer" or "supplier") || partyId <= 0)
        {
            return Unprocessable("party_type and party_id are required.");
        }

        return Ok(await _paymentService.GetAvailableCreditsForPartyAsync(partyType, partyId));
    }

    [HttpPost("apply-credits")]
    public async Task<IActionResult> ApplyCredits()
    {
        if (!await CanAsync("payment.create", "payment.update"))
            return Forbidden();

        var body = await ReadJsonBodyAsync();
        var partyType = GetString(body, "party_type")?.Trim()?.ToLowerInvariant();
        var partyId = GetNullableLong(body, "party_id") ?? 0;

        if (partyType is not ("customer" or "supplier") || partyId <= 0)
            return Unprocessable("party_type and party_id are required.");

        if (!body.TryGetValue("allocations", out var allocEl) || allocEl.ValueKind != JsonValueKind.Array || allocEl.GetArrayLength() == 0)
            return Unprocessable("allocations are required.", new() { ["allocations"] = new[] { "The allocations field is required." } });

        var allocations = ParseAllocationsArray(allocEl);

        try
        {
            var result = await _paymentService.ApplyPartyCreditsAsync(partyType, partyId, allocations, CurrentUserId);
            return Ok(new Dictionary<string, object?>
            {
                ["message"] = "Available credit applied successfully",
                ["allocated_total"] = result["allocated_total"],
                ["payments"] = result["payments"]
            });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message, new() { ["allocations"] = new[] { ex.Message } });
        }
    }

    [HttpPost("{id:long}/cheque-status")]
    public async Task<IActionResult> UpdateChequeStatus(long id)
    {
        if (!await CanAsync("payment.create", "payment.update"))
            return Forbidden();

        var body = await ReadJsonBodyAsync();
        var status = (GetString(body, "status") ?? GetString(body, "cheque_status"))?.Trim()?.ToLowerInvariant();
        var accountId = GetNullableLong(body, "account_id");
        var reason = GetString(body, "reason");

        if (status is not ("deposited" or "cleared" or "bounced"))
        {
            return Unprocessable("Invalid cheque status.", new() { ["status"] = new[] { "Invalid cheque status. Allowed: deposited, cleared, bounced." } });
        }

        try
        {
            var updated = await _paymentService.UpdateChequeStatusAsync(id, status, accountId, reason, CurrentUserId);
            return Ok(new Dictionary<string, object?>
            {
                ["message"] = $"Cheque status updated to {status}",
                ["payment"] = updated
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Unprocessable(ex.Message, new() { ["status"] = new[] { ex.Message } });
        }
    }

    private static Dictionary<string, object?> ConvertBodyToDictionary(Dictionary<string, JsonElement> body)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in body)
        {
            if (kv.Key.Equals("allocations", StringComparison.OrdinalIgnoreCase) && kv.Value.ValueKind == JsonValueKind.Array)
            {
                dict[kv.Key] = ParseAllocationsArray(kv.Value);
            }
            else
            {
                dict[kv.Key] = KarobarDb.PrepareDbValue(kv.Value);
            }
        }
        return dict;
    }

    private static List<Dictionary<string, object?>> ParseAllocationsArray(JsonElement arr)
    {
        var list = new List<Dictionary<string, object?>>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object)
            {
                var d = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                foreach (var prop in item.EnumerateObject())
                {
                    d[prop.Name] = KarobarDb.PrepareDbValue(prop.Value);
                }
                list.Add(d);
            }
        }
        return list;
    }
}

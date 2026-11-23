using System.Data;
using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Karobar.Api.Helpers;
using Karobar.Api.Infrastructure;
using Karobar.Api.Middleware;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Authorize]
public class SupplierLedgerController : ApiControllerBase
{
    private readonly PrintTemplateRenderer _renderer;

    public SupplierLedgerController(KarobarDb db, PrintTemplateRenderer? renderer = null) : base(db)
    {
        _renderer = renderer ?? new PrintTemplateRenderer();
    }

    public static async Task<Dictionary<long, decimal>> SumAllocatedByInvoiceIdsAsync(
        KarobarDb db,
        string invoiceType,
        IEnumerable<long> invoiceIds,
        IDbConnection? existingConn = null,
        IDbTransaction? tx = null)
    {
        var result = new Dictionary<long, decimal>();
        var ids = invoiceIds.Where(id => id > 0).Distinct().ToList();
        if (ids.Count == 0)
        {
            return result;
        }

        if (existingConn != null)
        {
            var hasAlloc = await existingConn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='payment_allocations'",
                transaction: tx) > 0;
            var hasPay = await existingConn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='payments'",
                transaction: tx) > 0;
            if (!hasAlloc || !hasPay) return result;
        }
        else if (!await db.HasTableAsync("payment_allocations") || !await db.HasTableAsync("payments"))
        {
            return result;
        }

        foreach (var chunk in ids.Chunk(400))
        {
            var rows = await db.QueryListAsync(@"
                SELECT pa.invoice_id, SUM(COALESCE(pa.amount, 0)) as total_allocated
                FROM payment_allocations pa
                INNER JOIN payments p ON p.id = pa.payment_id
                WHERE pa.invoice_type = @invoiceType
                  AND pa.invoice_id IN @ids
                  AND p.status = 'posted'
                GROUP BY pa.invoice_id",
                new { invoiceType, ids = chunk.ToArray() },
                existingConn,
                tx);

            foreach (var r in rows)
            {
                var invId = Convert.ToInt64(r["invoice_id"]);
                result[invId] = StocktakeService.ToDecimal(r.GetValueOrDefault("total_allocated"));
            }
        }

        return result;
    }

    public static async Task<decimal> GetSupplierNetBalanceAsync(KarobarDb db, long supplierId, string? asOfDate = null)
    {
        var balances = await GetBulkSupplierNetBalancesAsync(db, new[] { supplierId }, asOfDate);
        return balances.TryGetValue(supplierId, out var bal) ? bal : 0m;
    }

    public static async Task<Dictionary<long, decimal>> GetBulkSupplierNetBalancesAsync(
        KarobarDb db,
        IEnumerable<long>? supplierIds = null,
        string? asOfDate = null)
    {
        var result = new Dictionary<long, decimal>();
        if (!await db.HasTableAsync("supplier_ledgers"))
        {
            return result;
        }

        List<long> ids;
        if (supplierIds != null)
        {
            ids = supplierIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0) return result;
        }
        else if (await db.HasTableAsync("suppliers"))
        {
            var sRows = await db.QueryListAsync("SELECT id FROM suppliers");
            ids = sRows.Select(r => Convert.ToInt64(r["id"])).Distinct().ToList();
            if (ids.Count == 0) return result;
        }
        else
        {
            return result;
        }

        var ledgerRows = new List<Dictionary<string, object?>>();
        var hasPiTable = await db.HasTableAsync("purchase_invoices");

        foreach (var chunk in ids.Chunk(400))
        {
            var sql = hasPiTable
                ? @"SELECT sl.*,
                           pi.invoice_number as pi_invoice_number,
                           pi.posted_number as pi_posted_number,
                           pi.total_amount as pi_total_amount,
                           pi.total_paid as pi_total_paid,
                           pi.status as pi_status,
                           pi.posted_date as pi_posted_date
                    FROM supplier_ledgers sl
                    LEFT JOIN purchase_invoices pi ON pi.id = sl.purchase_invoice_id
                    WHERE sl.supplier_id IN @ids"
                : "SELECT sl.* FROM supplier_ledgers sl WHERE sl.supplier_id IN @ids";

            if (!string.IsNullOrWhiteSpace(asOfDate))
            {
                sql += " AND substr(sl.entry_date, 1, 10) <= @asOfDate";
            }

            var rows = await db.QueryListAsync(sql, new { ids = chunk.ToArray(), asOfDate = asOfDate?.Trim() });
            ledgerRows.AddRange(rows);
        }

        var rowsBySupplier = ledgerRows
            .GroupBy(r => Convert.ToInt64(r["supplier_id"]))
            .ToDictionary(g => g.Key, g => g.ToList());

        var allLinkedInvIds = new HashSet<long>();
        foreach (var r in ledgerRows)
        {
            if (r.TryGetValue("purchase_invoice_id", out var piObj) && piObj != null && long.TryParse(piObj.ToString(), out var piId) && piId > 0)
            {
                allLinkedInvIds.Add(piId);
            }
        }

        var unlinkedInvoices = new List<Dictionary<string, object?>>();
        if (hasPiTable)
        {
            foreach (var chunk in ids.Chunk(400))
            {
                var uSql = @"
                    SELECT id, supplier_id, total_amount, total_paid
                    FROM purchase_invoices
                    WHERE supplier_id IN @ids
                      AND invoice_type = 'credit'
                      AND (status IS NULL OR status != 'reversed')";
                if (!string.IsNullOrWhiteSpace(asOfDate))
                {
                    uSql += " AND substr(posted_date, 1, 10) <= @asOfDate";
                }
                var uRows = await db.QueryListAsync(uSql, new { ids = chunk.ToArray(), asOfDate = asOfDate?.Trim() });
                foreach (var u in uRows)
                {
                    var uid = Convert.ToInt64(u["id"]);
                    if (!allLinkedInvIds.Contains(uid))
                    {
                        unlinkedInvoices.Add(u);
                    }
                }
            }
        }

        var unlinkedBySupplier = unlinkedInvoices
            .GroupBy(u => Convert.ToInt64(u["supplier_id"]))
            .ToDictionary(g => g.Key, g => g.ToList());

        var allInvIdsForAlloc = new HashSet<long>(allLinkedInvIds);
        foreach (var u in unlinkedInvoices)
        {
            allInvIdsForAlloc.Add(Convert.ToInt64(u["id"]));
        }

        var allocatedByInvoice = await SumAllocatedByInvoiceIdsAsync(db, "purchase_invoice", allInvIdsForAlloc);

        foreach (var supplierId in ids)
        {
            var sRows = rowsBySupplier.TryGetValue(supplierId, out var list) ? list : new List<Dictionary<string, object?>>();
            decimal totalInvoiced = 0m;
            decimal paidOnInvoice = 0m;
            decimal paymentsDebited = 0m;

            foreach (var row in sRows)
            {
                var entryType = row.GetValueOrDefault("entry_type")?.ToString()?.ToLowerInvariant() ?? "";
                var hasLinkedPi = row.TryGetValue("purchase_invoice_id", out var piObj) && piObj != null && long.TryParse(piObj.ToString(), out var piId) && piId > 0;
                long linkedPiId = hasLinkedPi ? Convert.ToInt64(row["purchase_invoice_id"]) : 0;

                if (entryType == "invoice" && hasLinkedPi && row.ContainsKey("pi_total_amount") && row["pi_total_amount"] != null)
                {
                    var piStatus = row.GetValueOrDefault("pi_status")?.ToString();
                    if (string.Equals(piStatus, "reversed", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var invTotal = StocktakeService.ToDecimal(row.GetValueOrDefault("pi_total_amount") ?? row.GetValueOrDefault("invoice_total"));
                    var rawPaid = StocktakeService.ToDecimal(row.GetValueOrDefault("pi_total_paid") ?? row.GetValueOrDefault("total_paid"));
                    var allocated = allocatedByInvoice.TryGetValue(linkedPiId, out var al) ? al : 0m;
                    var paidOnInv = Math.Max(0m, Math.Min(rawPaid - allocated, invTotal));

                    totalInvoiced += invTotal;
                    paidOnInvoice += paidOnInv;
                }
                else if (entryType is ("invoice" or "manual"))
                {
                    var invTotal = StocktakeService.ToDecimal(row.GetValueOrDefault("invoice_total"));
                    var paidOnInv = Math.Min(StocktakeService.ToDecimal(row.GetValueOrDefault("total_paid")), invTotal);

                    totalInvoiced += invTotal;
                    paidOnInvoice += paidOnInv;
                }
                else if (entryType == "payment")
                {
                    paymentsDebited += StocktakeService.ToDecimal(row.GetValueOrDefault("debited_amount"));
                }
            }

            if (unlinkedBySupplier.TryGetValue(supplierId, out var uList))
            {
                foreach (var uInv in uList)
                {
                    var uId = Convert.ToInt64(uInv["id"]);
                    var uTotal = StocktakeService.ToDecimal(uInv.GetValueOrDefault("total_amount"));
                    var uRawPaid = StocktakeService.ToDecimal(uInv.GetValueOrDefault("total_paid"));
                    var uAlloc = allocatedByInvoice.TryGetValue(uId, out var al) ? al : 0m;
                    var uPaidOnInv = Math.Max(0m, Math.Min(uRawPaid - uAlloc, uTotal));

                    totalInvoiced += uTotal;
                    paidOnInvoice += uPaidOnInv;
                }
            }

            var netBalance = (totalInvoiced - paidOnInvoice) - paymentsDebited;
            result[supplierId] = netBalance > 0m ? Math.Round(netBalance, 2) : 0m;
        }

        return result;
    }

    public static async Task<decimal> GetTotalPayablesBalanceAsync(KarobarDb db, string? asOfDate = null)
    {
        var map = await GetBulkSupplierNetBalancesAsync(db, null, asOfDate);
        return Math.Round(map.Values.Sum(), 2);
    }

    private async Task<decimal> CalculateOpeningBalanceAsync(long supplierId, string? from)
    {
        if (string.IsNullOrWhiteSpace(from)) return 0m;

        var priorRows = await Db.QueryListAsync(@"
            SELECT sl.*,
                   pi.id as pi_id,
                   pi.invoice_number as pi_invoice_number,
                   pi.posted_number as pi_posted_number,
                   pi.total_amount as pi_total_amount,
                   pi.total_paid as pi_total_paid,
                   pi.status as pi_status
            FROM supplier_ledgers sl
            LEFT JOIN purchase_invoices pi ON pi.id = sl.purchase_invoice_id
            WHERE sl.supplier_id = @supplierId
              AND substr(sl.entry_date, 1, 10) < @from",
            new { supplierId, from = from.Trim()[..Math.Min(10, from.Trim().Length)] });

        var priorInvIds = priorRows
            .Select(r => r.TryGetValue("purchase_invoice_id", out var p) && p != null && long.TryParse(p.ToString(), out var pid) ? (long?)pid : null)
            .Where(p => p.HasValue && p.Value > 0)
            .Select(p => p!.Value)
            .Distinct()
            .ToList();

        var priorAllocated = await SumAllocatedByInvoiceIdsAsync(Db, "purchase_invoice", priorInvIds);

        decimal openingBalance = 0m;
        foreach (var pRow in priorRows)
        {
            var entryType = pRow.GetValueOrDefault("entry_type")?.ToString()?.ToLowerInvariant() ?? "";
            var hasPi = pRow.TryGetValue("pi_id", out var piIdObj) && piIdObj != null && long.TryParse(piIdObj.ToString(), out var piId);
            if (entryType == "invoice" && hasPi && string.Equals(pRow.GetValueOrDefault("pi_status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var invTotal = StocktakeService.ToDecimal(pRow.GetValueOrDefault("invoice_total"));
            var paidOnInv = StocktakeService.ToDecimal(pRow.GetValueOrDefault("total_paid"));

            if (entryType == "invoice" && hasPi)
            {
                var pId = Convert.ToInt64(pRow["pi_id"]);
                invTotal = StocktakeService.ToDecimal(pRow.GetValueOrDefault("pi_total_amount") ?? invTotal);
                var rawPaid = StocktakeService.ToDecimal(pRow.GetValueOrDefault("pi_total_paid") ?? paidOnInv);
                var alloc = priorAllocated.TryGetValue(pId, out var al) ? al : 0m;
                paidOnInv = Math.Max(0m, Math.Min(rawPaid - alloc, invTotal));
            }

            if (entryType is ("invoice" or "manual"))
            {
                openingBalance += (invTotal - paidOnInv);
            }
            else if (entryType == "payment")
            {
                openingBalance -= StocktakeService.ToDecimal(pRow.GetValueOrDefault("debited_amount"));
            }
        }

        return Math.Round(openingBalance, 2);
    }

    [HttpGet("api/supplier-ledger")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "supplier_id")] long? supplierId = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        if (!await CanAsync("supplier-ledger.view"))
        {
            return Forbidden();
        }

        if (supplierId == null || supplierId <= 0)
        {
            return Unprocessable("The supplier_id field is required.", new Dictionary<string, string[]>
            {
                ["supplier_id"] = new[] { "The supplier_id field is required." }
            });
        }

        var supExists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM suppliers WHERE id = @id", new { id = supplierId.Value });
        if (supExists == 0)
        {
            return Unprocessable("The selected supplier_id is invalid.", new Dictionary<string, string[]>
            {
                ["supplier_id"] = new[] { "The selected supplier_id is invalid." }
            });
        }

        var (rows, summary) = await BuildSupplierLedgerViewAsync(supplierId.Value, from, to);
        return Ok(new
        {
            data = rows,
            summary
        });
    }

    [HttpPost("api/supplier-ledger")]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("supplier-ledger.create"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var supplierId = GetNullableLong(body, "supplier_id");
        var entryDate = (GetString(body, "entry_date") ?? "").Trim();

        if (supplierId == null || supplierId <= 0)
        {
            return Unprocessable("The supplier_id field is required.", new Dictionary<string, string[]>
            {
                ["supplier_id"] = new[] { "The supplier_id field is required." }
            });
        }

        if (string.IsNullOrEmpty(entryDate))
        {
            return Unprocessable("The entry_date field is required.", new Dictionary<string, string[]>
            {
                ["entry_date"] = new[] { "The entry_date field is required." }
            });
        }

        var purchaseInvoiceId = GetNullableLong(body, "purchase_invoice_id");
        var explicitType = (GetString(body, "entry_type") ?? "").Trim().ToLowerInvariant();
        var debitedAmount = Math.Max(0m, GetDecimal(body, "debited_amount", 0m));

        bool isPayment = explicitType == "payment" || (debitedAmount > 0m && purchaseInvoiceId == null);
        var entryType = isPayment ? "payment" : (purchaseInvoiceId.HasValue ? "invoice" : "manual");

        var invoiceTotal = Math.Max(0m, GetDecimal(body, "invoice_total", 0m));
        var totalPaid = Math.Max(0m, GetDecimal(body, "total_paid", 0m));
        var credit = Math.Max(0m, Math.Round(invoiceTotal - totalPaid, 2));

        var id = await Db.InsertAsync("supplier_ledgers", new Dictionary<string, object?>
        {
            ["supplier_id"] = supplierId.Value,
            ["purchase_invoice_id"] = purchaseInvoiceId,
            ["entry_type"] = entryType,
            ["entry_date"] = DateStr(entryDate, DateTime.UtcNow.ToString("yyyy-MM-dd")),
            ["description"] = GetString(body, "description"),
            ["posted_number"] = GetString(body, "posted_number"),
            ["invoice_number"] = GetString(body, "invoice_number"),
            ["invoice_total"] = invoiceTotal,
            ["total_paid"] = totalPaid,
            ["debited_amount"] = debitedAmount,
            ["payment_ref"] = GetString(body, "payment_ref"),
            ["credit_remaining"] = credit,
            ["is_manual"] = entryType != "invoice" ? 1 : 0,
            ["created_by"] = CurrentUserId
        });

        var row = await Db.QuerySingleOrDefaultAsync("SELECT * FROM supplier_ledgers WHERE id = @id", new { id });
        return StatusCode(201, new { data = row });
    }

    [HttpPut("api/supplier-ledger/bulk")]
    public async Task<IActionResult> BulkUpdate()
    {
        if (!await CanAsync("supplier-ledger.update"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        if (!body.TryGetValue("rows", out var rowsEl) || rowsEl.ValueKind != JsonValueKind.Array || rowsEl.GetArrayLength() == 0)
        {
            return Unprocessable("The rows field is required.", new Dictionary<string, string[]>
            {
                ["rows"] = new[] { "The rows field is required." }
            });
        }

        await Db.GetTableColumnsAsync("supplier_ledgers");

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);

        try
        {
            foreach (var el in rowsEl.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var r = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in el.EnumerateObject()) r[p.Name] = p.Value;

                var id = GetNullableLong(r, "id");
                if (id == null || id <= 0) continue;

                var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM supplier_ledgers WHERE id = @id", new { id = id.Value }, conn, tx);
                if (existing == null) continue;

                bool hasPaymentId = existing.TryGetValue("payment_id", out var pid) && pid != null && !string.IsNullOrWhiteSpace(pid.ToString());
                bool hasReturnId = existing.TryGetValue("purchase_return_id", out var rid) && rid != null && !string.IsNullOrWhiteSpace(rid.ToString());
                bool isSystemLinked = hasPaymentId || hasReturnId;

                var entryType = existing.GetValueOrDefault("entry_type")?.ToString()?.ToLowerInvariant() ?? "";
                bool isManual = existing.GetValueOrDefault("is_manual") is true or 1 or 1L;
                bool isSystemInvoice = entryType == "invoice" && !isManual;

                var invTotal = (isSystemInvoice || isSystemLinked)
                    ? StocktakeService.ToDecimal(existing.GetValueOrDefault("invoice_total"))
                    : Math.Max(0m, GetDecimal(r, "invoice_total", 0m));

                var totalPaid = (isSystemInvoice || isSystemLinked)
                    ? StocktakeService.ToDecimal(existing.GetValueOrDefault("total_paid"))
                    : Math.Max(0m, GetDecimal(r, "total_paid", 0m));

                var debited = isSystemLinked
                    ? StocktakeService.ToDecimal(existing.GetValueOrDefault("debited_amount"))
                    : Math.Max(0m, GetDecimal(r, "debited_amount", 0m));

                var credit = Math.Max(0m, Math.Round(invTotal - totalPaid, 2));
                var entryDate = DateStr(GetString(r, "entry_date"), existing.GetValueOrDefault("entry_date")?.ToString());

                await Db.UpdateAsync("supplier_ledgers", id.Value, new Dictionary<string, object?>
                {
                    ["entry_date"] = entryDate,
                    ["description"] = GetString(r, "description"),
                    ["posted_number"] = GetString(r, "posted_number"),
                    ["invoice_number"] = GetString(r, "invoice_number"),
                    ["invoice_total"] = invTotal,
                    ["total_paid"] = totalPaid,
                    ["debited_amount"] = debited,
                    ["payment_ref"] = isSystemLinked ? existing.GetValueOrDefault("payment_ref") : GetString(r, "payment_ref"),
                    ["credit_remaining"] = credit
                }, "id", conn, tx);
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return Ok(new { status = "ok" });
    }

    [HttpDelete("api/supplier-ledger/{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("supplier-ledger.delete"))
        {
            return Forbidden();
        }

        var row = await Db.QuerySingleOrDefaultAsync("SELECT * FROM supplier_ledgers WHERE id = @id", new { id });
        if (row == null)
        {
            return NotFound(new { message = "Supplier ledger entry not found." });
        }

        var entryType = row.GetValueOrDefault("entry_type")?.ToString()?.ToLowerInvariant() ?? "";
        bool isManual = row.GetValueOrDefault("is_manual") is true or 1 or 1L;

        if (!isManual && entryType == "invoice")
        {
            return Unprocessable("Cannot delete auto-synced invoice row.");
        }

        if (row.TryGetValue("purchase_return_id", out var prObj) && prObj != null && !string.IsNullOrWhiteSpace(prObj.ToString()))
        {
            return Unprocessable("Cannot delete auto-synced return debit note row. Delete or edit the Purchase Return instead.");
        }

        if (row.TryGetValue("payment_id", out var payObj) && payObj != null && long.TryParse(payObj.ToString(), out var paymentId) &&
            paymentId > 0 && await Db.HasTableAsync("payments"))
        {
            var payment = await Db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId });
            if (payment != null && !string.Equals(payment.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    await ReverseSupplierPaymentAsync(payment);
                    return Ok(new { status = "ok" });
                }
                catch (KeyNotFoundException ex)
                {
                    return NotFound(new { message = ex.Message });
                }
                catch (InvalidOperationException ex)
                {
                    return Unprocessable(ex.Message, new Dictionary<string, string[]>
                    {
                        ["payment"] = new[] { ex.Message }
                    });
                }
            }
        }

        await Db.ExecuteAsync("DELETE FROM supplier_ledgers WHERE id = @id", new { id });
        return Ok(new { status = "ok" });
    }

    [HttpPost("api/supplier-ledger/rebuild")]
    public async Task<IActionResult> Rebuild()
    {
        if (!await CanAsync("supplier-ledger.update"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var supplierId = GetNullableLong(body, "supplier_id");
        if (supplierId == null || supplierId <= 0)
        {
            return Unprocessable("The supplier_id field is required.", new Dictionary<string, string[]>
            {
                ["supplier_id"] = new[] { "The supplier_id field is required." }
            });
        }

        var supExists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM suppliers WHERE id = @id", new { id = supplierId.Value });
        if (supExists == 0)
        {
            return Unprocessable("The selected supplier_id is invalid.", new Dictionary<string, string[]>
            {
                ["supplier_id"] = new[] { "The selected supplier_id is invalid." }
            });
        }

        await Db.HasTableAsync("purchase_invoices");
        await Db.HasTableAsync("payment_allocations");
        await Db.HasTableAsync("payments");
        await Db.GetTableColumnsAsync("supplier_ledgers");

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);

        int count;
        try
        {
            await Db.ExecuteAsync(@"
                DELETE FROM supplier_ledgers
                WHERE supplier_id = @supplierId
                  AND purchase_invoice_id IN (
                      SELECT id FROM purchase_invoices WHERE supplier_id = @supplierId AND status = 'reversed'
                  )",
                new { supplierId = supplierId.Value }, conn, tx);

            var invoices = await Db.QueryListAsync(@"
                SELECT id, supplier_id, invoice_number, posted_number, posted_date, total_amount, total_paid
                FROM purchase_invoices
                WHERE supplier_id = @supplierId
                  AND invoice_type = 'credit'
                  AND (status IS NULL OR status != 'reversed')",
                new { supplierId = supplierId.Value }, conn, tx);

            count = invoices.Count;
            var invIds = invoices.Select(i => Convert.ToInt64(i["id"])).ToList();
            var allocatedByInvoice = await SumAllocatedByInvoiceIdsAsync(Db, "purchase_invoice", invIds, conn, tx);

            foreach (var inv in invoices)
            {
                var invId = Convert.ToInt64(inv["id"]);
                var invTotal = Math.Round(StocktakeService.ToDecimal(inv.GetValueOrDefault("total_amount")), 2);
                var rawPaid = Math.Round(StocktakeService.ToDecimal(inv.GetValueOrDefault("total_paid")), 2);
                var allocated = Math.Round(allocatedByInvoice.TryGetValue(invId, out var al) ? al : 0m, 2);
                var directPaid = Math.Max(0m, Math.Round(rawPaid - allocated, 2));
                var paidOnInv = Math.Min(directPaid, invTotal);
                var credit = Math.Max(0m, Math.Round(invTotal - paidOnInv, 2));
                var excessPaid = Math.Max(0m, Math.Round(directPaid - Math.Max(0m, invTotal - allocated), 2));
                var entryDate = DateStr(inv.GetValueOrDefault("posted_date")?.ToString(), DateTime.UtcNow.ToString("yyyy-MM-dd"));
                var invoiceNo = inv.GetValueOrDefault("invoice_number")?.ToString();
                var postedNo = inv.GetValueOrDefault("posted_number")?.ToString();

                var existingLedger = await Db.QuerySingleOrDefaultAsync(@"
                    SELECT id FROM supplier_ledgers
                    WHERE supplier_id = @supplierId AND purchase_invoice_id = @invId
                    LIMIT 1",
                    new { supplierId = supplierId.Value, invId }, conn, tx);

                var ledgerData = new Dictionary<string, object?>
                {
                    ["supplier_id"] = supplierId.Value,
                    ["purchase_invoice_id"] = invId,
                    ["entry_type"] = "invoice",
                    ["entry_date"] = entryDate,
                    ["description"] = $"Invoice #{invoiceNo}",
                    ["posted_number"] = postedNo,
                    ["invoice_number"] = invoiceNo,
                    ["invoice_total"] = invTotal,
                    ["total_paid"] = paidOnInv,
                    ["debited_amount"] = 0m,
                    ["credit_remaining"] = credit,
                    ["is_manual"] = 0
                };

                if (existingLedger != null)
                {
                    await Db.UpdateAsync("supplier_ledgers", Convert.ToInt64(existingLedger["id"]), ledgerData, "id", conn, tx);
                }
                else
                {
                    await Db.InsertAsync("supplier_ledgers", ledgerData, conn, tx);
                }

                if (!string.IsNullOrWhiteSpace(postedNo))
                {
                    var payRef = $"Inv #{postedNo}";
                    await conn.ExecuteAsync(@"
                        DELETE FROM supplier_ledgers
                        WHERE supplier_id = @supplierId
                          AND payment_ref = @payRef
                          AND entry_type = 'payment'
                          AND payment_id IS NULL",
                        new { supplierId = supplierId.Value, payRef }, tx);

                    if (excessPaid > 0m)
                    {
                        await Db.InsertAsync("supplier_ledgers", new Dictionary<string, object?>
                        {
                            ["supplier_id"] = supplierId.Value,
                            ["purchase_invoice_id"] = null,
                            ["entry_type"] = "payment",
                            ["entry_date"] = entryDate,
                            ["description"] = $"Payment made on invoice {postedNo}",
                            ["posted_number"] = postedNo,
                            ["invoice_number"] = invoiceNo,
                            ["invoice_total"] = 0m,
                            ["total_paid"] = 0m,
                            ["debited_amount"] = excessPaid,
                            ["payment_ref"] = payRef,
                            ["credit_remaining"] = 0m,
                            ["is_manual"] = 0
                        }, conn, tx);
                    }
                }
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        return Ok(new { status = "ok", count });
    }

    [HttpPost("api/supplier-ledger/settle-invoice")]
    public async Task<IActionResult> SettleInvoice()
    {
        if (!await CanAsync("supplier-ledger.update"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var purchaseInvoiceId = GetNullableLong(body, "purchase_invoice_id");
        if (purchaseInvoiceId == null || purchaseInvoiceId <= 0)
        {
            return Unprocessable("The purchase_invoice_id field is required.", new Dictionary<string, string[]>
            {
                ["purchase_invoice_id"] = new[] { "The purchase_invoice_id field is required." }
            });
        }

        var invoice = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_invoices WHERE id = @id", new { id = purchaseInvoiceId.Value });
        if (invoice == null)
        {
            return Unprocessable("The selected purchase_invoice_id is invalid.", new Dictionary<string, string[]>
            {
                ["purchase_invoice_id"] = new[] { "The selected purchase_invoice_id is invalid." }
            });
        }

        if (string.Equals(invoice.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
        {
            return Unprocessable("Cannot settle a reversed invoice.", new Dictionary<string, string[]>
            {
                ["purchase_invoice_id"] = new[] { "Cannot settle a reversed invoice." }
            });
        }

        var invoiceTotal = StocktakeService.ToDecimal(invoice.GetValueOrDefault("total_amount"));
        var paid = StocktakeService.ToDecimal(invoice.GetValueOrDefault("total_paid"));
        var balance = Math.Round(Math.Max(invoiceTotal - paid, 0m), 2);

        if (balance <= 0m)
        {
            return Ok(new
            {
                status = "ok",
                settled = 0,
                message = "Invoice is already fully paid"
            });
        }

        var payMethod = GetString(body, "payment_method") ?? "cash";
        var entryDate = DateStr(GetString(body, "entry_date"), DateTime.UtcNow.ToString("yyyy-MM-dd"))!;
        var accountId = GetNullableLong(body, "account_id");

        var hasPayments = await Db.HasTableAsync("payments") && await Db.HasTableAsync("payment_allocations");
        var hasAccounts = await Db.HasTableAsync("accounts");

        await Db.GetTableColumnsAsync("purchase_invoices");
        await Db.GetTableColumnsAsync("supplier_ledgers");
        if (hasPayments)
        {
            await Db.GetTableColumnsAsync("payments");
            await Db.GetTableColumnsAsync("payment_allocations");
            await Db.GetTableColumnsAsync("accounts");
            await Db.GetTableColumnsAsync("account_transactions");
            await Db.GetTableColumnsAsync("cash_drawer_sessions");
            await Db.GetTableColumnsAsync("cash_drawer_transactions");
        }

        string? payNo = hasPayments
            ? await new DocumentSequenceService(Db).NextAsync("payment", "PAY-", 5, "payments", "payment_number")
            : null;

        long? paymentId = null;
        Dictionary<string, object?>? paymentRowForAudit = null;
        var touchedDrawer = false;
        string? postedNo;
        string refLabel;

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);

        try
        {
            var lockedInvoice = await Db.QuerySingleOrDefaultAsync("SELECT * FROM purchase_invoices WHERE id = @id", new { id = purchaseInvoiceId.Value }, conn, tx);
            if (lockedInvoice == null)
            {
                await tx.RollbackAsync();
                return Unprocessable("The selected purchase_invoice_id is invalid.", new Dictionary<string, string[]>
                {
                    ["purchase_invoice_id"] = new[] { "The selected purchase_invoice_id is invalid." }
                });
            }

            if (string.Equals(lockedInvoice.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
            {
                await tx.RollbackAsync();
                return Unprocessable("Cannot settle a reversed invoice.", new Dictionary<string, string[]>
                {
                    ["purchase_invoice_id"] = new[] { "Cannot settle a reversed invoice." }
                });
            }

            invoiceTotal = StocktakeService.ToDecimal(lockedInvoice.GetValueOrDefault("total_amount"));
            paid = StocktakeService.ToDecimal(lockedInvoice.GetValueOrDefault("total_paid"));
            balance = Math.Round(Math.Max(invoiceTotal - paid, 0m), 2);
            if (balance <= 0m)
            {
                await tx.RollbackAsync();
                return Unprocessable("Invoice is already fully paid.", new Dictionary<string, string[]>
                {
                    ["purchase_invoice_id"] = new[] { "Invoice is already fully paid." }
                });
            }

            var supplierId = Convert.ToInt64(lockedInvoice["supplier_id"]);
            postedNo = lockedInvoice.GetValueOrDefault("posted_number")?.ToString();
            var invoiceNo = lockedInvoice.GetValueOrDefault("invoice_number")?.ToString();
            refLabel = !string.IsNullOrWhiteSpace(postedNo) ? postedNo : (!string.IsNullOrWhiteSpace(invoiceNo) ? invoiceNo : $"#{purchaseInvoiceId.Value}");

            if (hasPayments)
            {
                var accountSvc = new AccountService(Db);
                Dictionary<string, object?>? account = hasAccounts
                    ? await accountSvc.ResolveAccountInTxAsync(accountId, payMethod, conn, tx)
                    : null;
                if (account != null) accountId = AccountService.ToLong(account["id"]);

                paymentId = await Db.InsertAsync("payments", new Dictionary<string, object?>
                {
                    ["payment_number"] = payNo,
                    ["party_type"] = "supplier",
                    ["party_id"] = supplierId,
                    ["payment_date"] = entryDate,
                    ["amount"] = balance,
                    ["allocated_amount"] = balance,
                    ["unapplied_amount"] = 0m,
                    ["payment_method"] = payMethod,
                    ["account_id"] = accountId,
                    ["reference"] = $"Inv {refLabel}",
                    ["notes"] = $"Settled Purchase Invoice {refLabel}",
                    ["status"] = "posted",
                    ["created_by_id"] = CurrentUserId
                }, conn, tx);

                await Db.InsertAsync("payment_allocations", new Dictionary<string, object?>
                {
                    ["payment_id"] = paymentId.Value,
                    ["invoice_type"] = "purchase_invoice",
                    ["invoice_id"] = purchaseInvoiceId.Value,
                    ["amount"] = balance
                }, conn, tx);

                await Db.UpdateAsync("purchase_invoices", purchaseInvoiceId.Value, new Dictionary<string, object?>
                {
                    ["total_paid"] = invoiceTotal
                }, "id", conn, tx);

                await Db.InsertAsync("supplier_ledgers", new Dictionary<string, object?>
                {
                    ["supplier_id"] = supplierId,
                    ["purchase_invoice_id"] = null,
                    ["payment_id"] = paymentId.Value,
                    ["entry_type"] = "payment",
                    ["entry_date"] = entryDate,
                    ["posted_number"] = payNo,
                    ["invoice_number"] = invoiceNo,
                    ["invoice_total"] = 0m,
                    ["total_paid"] = 0m,
                    ["debited_amount"] = balance,
                    ["payment_ref"] = $"Inv {refLabel}",
                    ["credit_remaining"] = 0m,
                    ["description"] = $"Settled Purchase Invoice {refLabel} ({payNo})",
                    ["is_manual"] = 0,
                    ["created_by"] = CurrentUserId
                }, conn, tx);

                if (account != null && accountId.HasValue)
                {
                    await accountSvc.WithdrawInTxAsync(
                        account,
                        balance,
                        "supplier_payment",
                        paymentId.Value,
                        $"Settled Purchase Invoice {refLabel} ({payNo})",
                        CurrentUserId,
                        conn,
                        tx);
                }

                if (accountSvc.IsDefaultCashDrawerAccount(account, payMethod))
                {
                    var cashDrawerSvc = new CashDrawerService(Db);
                    var drawerTxn = await cashDrawerSvc.RecordTransactionInTxAsync(
                        null,
                        "supplier_payment",
                        balance,
                        "payment",
                        paymentId.Value,
                        $"Settled Purchase Invoice {refLabel} ({payNo})",
                        CurrentUserId,
                        CurrentTerminalId,
                        conn,
                        tx);
                    touchedDrawer = drawerTxn != null;
                }

                paymentRowForAudit = await Db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId.Value }, conn, tx);
            }
            else
            {
                await Db.UpdateAsync("purchase_invoices", purchaseInvoiceId.Value, new Dictionary<string, object?>
                {
                    ["total_paid"] = invoiceTotal
                }, "id", conn, tx);

                var ledgerRow = await Db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM supplier_ledgers WHERE purchase_invoice_id = @invId AND entry_type = 'invoice' LIMIT 1",
                    new { invId = purchaseInvoiceId.Value }, conn, tx);

                if (ledgerRow != null)
                {
                    await Db.UpdateAsync("supplier_ledgers", Convert.ToInt64(ledgerRow["id"]), new Dictionary<string, object?>
                    {
                        ["total_paid"] = invoiceTotal,
                        ["credit_remaining"] = 0m
                    }, "id", conn, tx);
                }
                else
                {
                    await Db.InsertAsync("supplier_ledgers", new Dictionary<string, object?>
                    {
                        ["supplier_id"] = supplierId,
                        ["purchase_invoice_id"] = purchaseInvoiceId.Value,
                        ["entry_type"] = "invoice",
                        ["is_manual"] = 0,
                        ["entry_date"] = entryDate,
                        ["posted_number"] = postedNo,
                        ["invoice_number"] = invoiceNo,
                        ["invoice_total"] = invoiceTotal,
                        ["total_paid"] = invoiceTotal,
                        ["debited_amount"] = 0m,
                        ["credit_remaining"] = 0m,
                        ["description"] = $"Purchase invoice {refLabel} (marked paid)",
                        ["created_by"] = CurrentUserId
                    }, conn, tx);
                }
            }

            await tx.CommitAsync();
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }

        if (touchedDrawer)
        {
            SyncPulseService.Bump("drawer");
        }

        if (paymentId.HasValue && paymentRowForAudit != null)
        {
            await new DocumentAuditService(Db).RecordCreateAsync(
                "payment",
                paymentId.Value,
                paymentRowForAudit,
                CurrentUserId,
                $"Settled Purchase Invoice {refLabel}");
        }

        return Ok(new
        {
            status = "ok",
            settled = balance,
            purchase_invoice_id = purchaseInvoiceId.Value,
            posted_number = postedNo,
            payment_id = paymentId
        });
    }

    [HttpGet("api/supplier-ledger/print-url")]
    public async Task<IActionResult> PrintUrl(
        [FromQuery(Name = "supplier_id")] long? supplierId = null,
        [FromQuery] string? type = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        if (!await CanAsync("supplier-ledger.view"))
        {
            return Forbidden();
        }

        if (supplierId == null || supplierId <= 0)
        {
            return Unprocessable("Supplier is required");
        }

        var supplier = await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @id", new { id = supplierId.Value });
        if (supplier == null)
        {
            return NotFound(new { message = "Supplier not found." });
        }

        var queryParams = new Dictionary<string, string?>
        {
            ["supplier_id"] = supplierId.Value.ToString(),
            ["silent_print"] = "1"
        };
        if (!string.IsNullOrWhiteSpace(type)) queryParams["type"] = type;
        if (!string.IsNullOrWhiteSpace(from)) queryParams["from"] = from;
        if (!string.IsNullOrWhiteSpace(to)) queryParams["to"] = to;

        var url = SignedUrlMiddleware.CreateSignedUrl(
            Request,
            "/supplier-ledger/print",
            queryParams,
            TimeSpan.FromMinutes(15));
        var html = await RenderSupplierLedgerHtmlAsync(supplier, type, from, to);

        return Ok(new
        {
            url,
            html
        });
    }

    [AllowAnonymous]
    [HttpGet("/supplier-ledger/print")]
    public async Task<IActionResult> Print(
        [FromQuery(Name = "supplier_id")] long? supplierId = null,
        [FromQuery] string? type = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        if (supplierId == null || supplierId <= 0)
        {
            return NotFound("Supplier is required");
        }

        var supplier = await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @id", new { id = supplierId.Value });
        if (supplier == null)
        {
            return NotFound("Supplier not found");
        }

        var html = await RenderSupplierLedgerHtmlAsync(supplier, type, from, to);
        return Content(html, "text/html; charset=utf-8");
    }

    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?> Summary)> BuildSupplierLedgerViewAsync(
        long supplierId,
        string? from,
        string? to)
    {
        var conditions = new List<string> { "sl.supplier_id = @supplierId" };
        var dynParams = new DynamicParameters();
        dynParams.Add("supplierId", supplierId);

        if (!string.IsNullOrWhiteSpace(from))
        {
            conditions.Add("substr(sl.entry_date, 1, 10) >= @from");
            dynParams.Add("from", from.Trim()[..Math.Min(10, from.Trim().Length)]);
        }

        if (!string.IsNullOrWhiteSpace(to))
        {
            conditions.Add("substr(sl.entry_date, 1, 10) <= @to");
            dynParams.Add("to", to.Trim()[..Math.Min(10, to.Trim().Length)]);
        }

        var whereSql = "WHERE " + string.Join(" AND ", conditions);
        var rawRows = await Db.QueryListAsync($@"
            SELECT sl.*,
                   pi.id as pi_id,
                   pi.invoice_number as pi_invoice_number,
                   pi.posted_number as pi_posted_number,
                   pi.total_amount as pi_total_amount,
                   pi.total_paid as pi_total_paid,
                   pi.status as pi_status,
                   pi.created_at as pi_created_at
            FROM supplier_ledgers sl
            LEFT JOIN purchase_invoices pi ON pi.id = sl.purchase_invoice_id
            {whereSql}
            ORDER BY sl.entry_date ASC, sl.id ASC",
            dynParams);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var r in rawRows)
        {
            var entryType = r.GetValueOrDefault("entry_type")?.ToString()?.ToLowerInvariant() ?? "";
            var hasPi = r.TryGetValue("pi_id", out var piIdObj) && piIdObj != null && long.TryParse(piIdObj.ToString(), out _);
            if (entryType == "invoice" && hasPi && string.Equals(r.GetValueOrDefault("pi_status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            rows.Add(r);
        }

        var invIds = rows
            .Select(r => r.TryGetValue("purchase_invoice_id", out var p) && p != null && long.TryParse(p.ToString(), out var pid) ? (long?)pid : null)
            .Where(p => p.HasValue && p.Value > 0)
            .Select(p => p!.Value)
            .Distinct()
            .ToList();

        var allocatedByInvoice = await SumAllocatedByInvoiceIdsAsync(Db, "purchase_invoice", invIds);

        foreach (var row in rows)
        {
            var entryType = row.GetValueOrDefault("entry_type")?.ToString()?.ToLowerInvariant() ?? "";
            var hasPi = row.TryGetValue("pi_id", out var piIdObj) && piIdObj != null && long.TryParse(piIdObj.ToString(), out var piId);

            if (hasPi)
            {
                var pId = Convert.ToInt64(row["pi_id"]);
                row["purchase_invoice"] = new Dictionary<string, object?>
                {
                    ["id"] = pId,
                    ["invoice_number"] = row.GetValueOrDefault("pi_invoice_number"),
                    ["posted_number"] = row.GetValueOrDefault("pi_posted_number"),
                    ["total_amount"] = row.GetValueOrDefault("pi_total_amount"),
                    ["total_paid"] = row.GetValueOrDefault("pi_total_paid"),
                    ["status"] = row.GetValueOrDefault("pi_status"),
                    ["created_at"] = row.GetValueOrDefault("pi_created_at")
                };

                if (entryType == "invoice")
                {
                    var invTotal = StocktakeService.ToDecimal(row.GetValueOrDefault("pi_total_amount") ?? row.GetValueOrDefault("invoice_total"));
                    var rawPaid = StocktakeService.ToDecimal(row.GetValueOrDefault("pi_total_paid") ?? row.GetValueOrDefault("total_paid"));
                    var allocated = allocatedByInvoice.TryGetValue(pId, out var al) ? al : 0m;
                    var paidOnInv = Math.Max(0m, Math.Min(rawPaid - allocated, invTotal));
                    var creditRem = Math.Max(0m, Math.Round(invTotal - paidOnInv, 2));
                    var invSettled = Math.Min(rawPaid, invTotal);
                    var invDue = Math.Max(0m, Math.Round(invTotal - invSettled, 2));

                    row["invoice_total"] = invTotal;
                    row["total_paid"] = paidOnInv;
                    row["credit_remaining"] = creditRem;
                    row["invoice_settled"] = Math.Round(invSettled, 2);
                    row["invoice_due"] = invDue;
                }
            }
            else
            {
                row["purchase_invoice"] = null;
            }

            var tsSource = (entryType == "invoice" && hasPi && row.GetValueOrDefault("pi_created_at") != null)
                ? row.GetValueOrDefault("pi_created_at")?.ToString()
                : row.GetValueOrDefault("created_at")?.ToString();

            long sortTs = 0;
            if (!string.IsNullOrWhiteSpace(tsSource) && DateTimeOffset.TryParse(tsSource, out var dto))
            {
                sortTs = dto.ToUnixTimeSeconds();
            }

            row["entry_date"] = DateStr(row.GetValueOrDefault("entry_date")?.ToString(), null);
            row["sort_ts"] = sortTs;
            row["type_order"] = entryType == "invoice" ? 1 : (entryType == "manual" ? 2 : 3);
            row["row_key"] = $"ledger-{row.GetValueOrDefault("id")}";

            row.Remove("pi_id");
            row.Remove("pi_invoice_number");
            row.Remove("pi_posted_number");
            row.Remove("pi_total_amount");
            row.Remove("pi_total_paid");
            row.Remove("pi_status");
            row.Remove("pi_created_at");
        }

        rows.Sort((a, b) =>
        {
            var da = a.GetValueOrDefault("entry_date")?.ToString() ?? "";
            var db = b.GetValueOrDefault("entry_date")?.ToString() ?? "";
            var cmpDate = string.CompareOrdinal(da, db);
            if (cmpDate != 0) return cmpDate;

            var ta = Convert.ToInt64(a.GetValueOrDefault("sort_ts") ?? 0L);
            var tb = Convert.ToInt64(b.GetValueOrDefault("sort_ts") ?? 0L);
            if (ta != tb && ta > 0 && tb > 0) return ta.CompareTo(tb);

            var oa = Convert.ToInt32(a.GetValueOrDefault("type_order") ?? 9);
            var ob = Convert.ToInt32(b.GetValueOrDefault("type_order") ?? 9);
            if (oa != ob) return oa.CompareTo(ob);

            var ida = Convert.ToInt64(a.GetValueOrDefault("id") ?? 0L);
            var idb = Convert.ToInt64(b.GetValueOrDefault("id") ?? 0L);
            return ida.CompareTo(idb);
        });

        var openingBalance = await CalculateOpeningBalanceAsync(supplierId, from);
        var runningBalance = openingBalance;
        decimal totalInvoiced = 0m;
        decimal paidOnInvoice = 0m;
        decimal paymentsDebited = 0m;

        for (int idx = 0; idx < rows.Count; idx++)
        {
            var row = rows[idx];
            var entryType = row.GetValueOrDefault("entry_type")?.ToString()?.ToLowerInvariant() ?? "";
            var invTotal = StocktakeService.ToDecimal(row.GetValueOrDefault("invoice_total"));
            var totPaid = StocktakeService.ToDecimal(row.GetValueOrDefault("total_paid"));
            var debited = StocktakeService.ToDecimal(row.GetValueOrDefault("debited_amount"));

            if (entryType is ("invoice" or "manual"))
            {
                runningBalance += (invTotal - totPaid);
                totalInvoiced += invTotal;
                paidOnInvoice += totPaid;
            }
            else if (entryType == "payment")
            {
                runningBalance -= debited;
                paymentsDebited += debited;
            }

            row["seq"] = idx + 1;
            row["running_balance"] = Math.Round(runningBalance, 2);
        }

        var netBalance = Math.Round(openingBalance + totalInvoiced - (paidOnInvoice + paymentsDebited), 2);
        var summary = new Dictionary<string, object?>
        {
            ["opening_balance"] = openingBalance,
            ["total_invoiced"] = Math.Round(totalInvoiced, 2),
            ["paid_on_invoice"] = Math.Round(paidOnInvoice, 2),
            ["payments_debited"] = Math.Round(paymentsDebited, 2),
            ["net_balance"] = netBalance
        };

        return (rows, summary);
    }

    private async Task ReverseSupplierPaymentAsync(Dictionary<string, object?> payment)
    {
        var paymentId = Convert.ToInt64(payment["id"]);
        var paymentService = new PaymentService(Db);
        await paymentService.ReversePaymentAsync(paymentId, "Deleted from Supplier Ledger", CurrentUserId, CurrentTerminalId);
    }

    private async Task<string> RenderSupplierLedgerHtmlAsync(
        Dictionary<string, object?> supplier,
        string? type,
        string? from,
        string? to)
    {
        var supplierId = Convert.ToInt64(supplier["id"]);
        var supplierName = supplier.GetValueOrDefault("name")?.ToString() ?? $"Supplier #{supplierId}";
        var settingVm = await PrintViewModelMapper.LoadPrintSettingAsync(Db);
        var (rows, summary) = await BuildSupplierLedgerViewAsync(supplierId, from, to);

        var rowVms = rows.Select(r => new PrintLedgerRowViewModel
        {
            EntryDate = r.GetValueOrDefault("entry_date")?.ToString() ?? "",
            EntryType = r.GetValueOrDefault("entry_type")?.ToString() ?? "invoice",
            PostedNumber = r.GetValueOrDefault("posted_number")?.ToString(),
            InvoiceNumber = r.GetValueOrDefault("invoice_number")?.ToString(),
            InvoiceTotal = StocktakeService.ToDecimal(r.GetValueOrDefault("invoice_total")),
            TotalPaid = StocktakeService.ToDecimal(r.GetValueOrDefault("total_paid")),
            DebitedAmount = StocktakeService.ToDecimal(r.GetValueOrDefault("debited_amount")),
            PaymentRef = r.GetValueOrDefault("payment_ref")?.ToString(),
            CreditRemainingCalc = StocktakeService.ToDecimal(r.GetValueOrDefault("credit_remaining_calc")),
            RunningBalance = StocktakeService.ToDecimal(r.GetValueOrDefault("running_balance")),
            Description = r.GetValueOrDefault("description")?.ToString()
        }).ToList();

        var summaryVm = new PrintLedgerSummaryViewModel
        {
            OpeningBalance = StocktakeService.ToDecimal(summary.GetValueOrDefault("opening_balance")),
            TotalInvoiced = StocktakeService.ToDecimal(summary.GetValueOrDefault("total_invoiced")),
            PaidOnInvoice = StocktakeService.ToDecimal(summary.GetValueOrDefault("paid_on_invoice")),
            PaymentsDebited = StocktakeService.ToDecimal(summary.GetValueOrDefault("payments_debited")),
            NetBalance = StocktakeService.ToDecimal(summary.GetValueOrDefault("net_balance"))
        };

        var paperSize = string.Equals(type, "thermal", StringComparison.OrdinalIgnoreCase) ? "thermal" : "a4";
        return _renderer.RenderPartyLedger("supplier", paperSize, supplierName, rowVms, summaryVm, settingVm, from, to, PrintViewModelMapper.QueryToDict(Request.Query));
    }

    private static string? DateStr(string? value, string? fallback = null)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var trimmed = value.Trim();
            return trimmed.Length >= 10 ? trimmed[..10] : trimmed;
        }
        return fallback;
    }
}

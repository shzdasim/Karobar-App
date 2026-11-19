using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
public class CustomerLedgerController : ApiControllerBase
{
    private readonly PrintTemplateRenderer _renderer;

    public CustomerLedgerController(KarobarDb db, PrintTemplateRenderer? renderer = null) : base(db)
    {
        _renderer = renderer ?? new PrintTemplateRenderer();
    }

    public static bool IsWalkInName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Regex.IsMatch(name.Trim(), @"walk[\s\-_]*in", RegexOptions.IgnoreCase);
    }

    public static async Task<HashSet<long>> GetWalkInIdsAsync(KarobarDb db)
    {
        var set = new HashSet<long>();
        if (!await db.HasTableAsync("customers")) return set;

        var rows = await db.QueryListAsync(@"
            SELECT id
            FROM customers
            WHERE UPPER(name) LIKE '%WALK-IN%'
               OR UPPER(name) LIKE '%WALK IN%'
               OR UPPER(name) LIKE '%WALK_IN%'
               OR UPPER(name) LIKE '%WALKIN%'");

        foreach (var r in rows)
        {
            if (r.TryGetValue("id", out var idObj) && idObj != null && long.TryParse(idObj.ToString(), out var id))
            {
                set.Add(id);
            }
        }
        return set;
    }

    public static async Task<decimal> GetCustomerNetBalanceAsync(KarobarDb db, long customerId, string? asOfDate = null)
    {
        var walkInIds = await GetWalkInIdsAsync(db);
        if (walkInIds.Contains(customerId)) return 0m;

        var balances = await GetBulkCustomerNetBalancesAsync(db, new[] { customerId }, asOfDate);
        return balances.TryGetValue(customerId, out var bal) ? bal : 0m;
    }

    public static async Task<Dictionary<long, decimal>> GetBulkCustomerNetBalancesAsync(
        KarobarDb db,
        IEnumerable<long>? customerIds = null,
        string? asOfDate = null)
    {
        var balances = new Dictionary<long, decimal>();
        if (!await db.HasTableAsync("customer_ledgers"))
        {
            return balances;
        }

        var walkInIds = await GetWalkInIdsAsync(db);
        List<long>? filterIds = null;
        if (customerIds != null)
        {
            filterIds = customerIds.Where(id => id > 0 && !walkInIds.Contains(id)).Distinct().ToList();
            if (filterIds.Count == 0) return balances;
        }

        var hasSiTable = await db.HasTableAsync("sale_invoices");
        var invTotals = new Dictionary<long, (decimal TotalInv, decimal ReceivedOnInv)>();
        var payTotals = new Dictionary<long, decimal>();

        var cleanDate = !string.IsNullOrWhiteSpace(asOfDate)
            ? asOfDate.Trim()[..Math.Min(10, asOfDate.Trim().Length)]
            : null;

        if (filterIds != null)
        {
            foreach (var chunk in filterIds.Chunk(400))
            {
                var idsArr = chunk.ToArray();
                await AccumulateCustomerBalancesChunkAsync(db, hasSiTable, idsArr, cleanDate, walkInIds, invTotals, payTotals);
            }
        }
        else
        {
            await AccumulateCustomerBalancesChunkAsync(db, hasSiTable, null, cleanDate, walkInIds, invTotals, payTotals);
        }

        var allCustIds = invTotals.Keys.Union(payTotals.Keys).Distinct();
        foreach (var cid in allCustIds)
        {
            if (cid <= 0 || walkInIds.Contains(cid)) continue;
            var inv = invTotals.TryGetValue(cid, out var iv) ? iv : (TotalInv: 0m, ReceivedOnInv: 0m);
            var pay = payTotals.TryGetValue(cid, out var pv) ? pv : 0m;
            var net = (inv.TotalInv - inv.ReceivedOnInv) - pay;
            if (net > 0.01m)
            {
                balances[cid] = Math.Round(net, 2);
            }
        }

        return balances;
    }

    private static async Task AccumulateCustomerBalancesChunkAsync(
        KarobarDb db,
        bool hasSiTable,
        long[]? idsArr,
        string? cleanDate,
        HashSet<long> walkInIds,
        Dictionary<long, (decimal TotalInv, decimal ReceivedOnInv)> invTotals,
        Dictionary<long, decimal> payTotals)
    {
        var invConditions = new List<string>
        {
            "cl.customer_id IS NOT NULL",
            "cl.entry_type IN ('invoice', 'manual')"
        };
        if (hasSiTable)
        {
            invConditions.Add("(cl.sale_invoice_id IS NULL OR si.status IS NULL OR si.status != 'reversed')");
        }
        if (idsArr != null)
        {
            invConditions.Add("cl.customer_id IN @ids");
        }
        if (cleanDate != null)
        {
            invConditions.Add("substr(cl.entry_date, 1, 10) <= @cleanDate");
        }

        var joinSql = hasSiTable ? "LEFT JOIN sale_invoices si ON si.id = cl.sale_invoice_id" : "";
        var invSql = $@"
            SELECT cl.customer_id,
                   COALESCE(SUM(COALESCE(cl.invoice_total, 0)), 0) as total_inv,
                   COALESCE(SUM(CASE WHEN COALESCE(cl.total_received, 0) < COALESCE(cl.invoice_total, 0)
                                     THEN COALESCE(cl.total_received, 0)
                                     ELSE COALESCE(cl.invoice_total, 0) END), 0) as received_on_inv
            FROM customer_ledgers cl
            {joinSql}
            WHERE {string.Join(" AND ", invConditions)}
            GROUP BY cl.customer_id";

        var invRows = await db.QueryListAsync(invSql, new { ids = idsArr, cleanDate });
        foreach (var r in invRows)
        {
            var cid = Convert.ToInt64(r["customer_id"]);
            if (walkInIds.Contains(cid)) continue;
            invTotals[cid] = (
                StocktakeService.ToDecimal(r.GetValueOrDefault("total_inv")),
                StocktakeService.ToDecimal(r.GetValueOrDefault("received_on_inv"))
            );
        }

        var payConditions = new List<string>
        {
            "customer_id IS NOT NULL",
            "entry_type = 'payment'"
        };
        if (idsArr != null)
        {
            payConditions.Add("customer_id IN @ids");
        }
        if (cleanDate != null)
        {
            payConditions.Add("substr(entry_date, 1, 10) <= @cleanDate");
        }

        var paySql = $@"
            SELECT customer_id,
                   COALESCE(SUM(COALESCE(credited_amount, 0)), 0) as total_credited
            FROM customer_ledgers
            WHERE {string.Join(" AND ", payConditions)}
            GROUP BY customer_id";

        var payRows = await db.QueryListAsync(paySql, new { ids = idsArr, cleanDate });
        foreach (var r in payRows)
        {
            var cid = Convert.ToInt64(r["customer_id"]);
            if (walkInIds.Contains(cid)) continue;
            payTotals[cid] = StocktakeService.ToDecimal(r.GetValueOrDefault("total_credited"));
        }
    }

    public static async Task<decimal> GetTotalReceivablesBalanceAsync(KarobarDb db, string? asOfDate = null)
    {
        var map = await GetBulkCustomerNetBalancesAsync(db, null, asOfDate);
        return Math.Round(map.Values.Sum(), 2);
    }

    private async Task<decimal> CalculateOpeningBalanceAsync(long customerId, string? from)
    {
        if (string.IsNullOrWhiteSpace(from)) return 0m;
        var cleanFrom = from.Trim()[..Math.Min(10, from.Trim().Length)];

        decimal openingBalance = 0m;

        if (await Db.HasTableAsync("sale_invoices"))
        {
            var priorInvoices = await Db.QueryListAsync(@"
                SELECT *
                FROM sale_invoices
                WHERE customer_id = @customerId
                  AND invoice_type = 'credit'
                  AND (status IS NULL OR status != 'reversed')
                  AND substr(date, 1, 10) < @cleanFrom",
                new { customerId, cleanFrom });

            var priorIds = priorInvoices.Select(i => Convert.ToInt64(i["id"])).ToList();
            var priorAllocated = await SupplierLedgerController.SumAllocatedByInvoiceIdsAsync(Db, "sale_invoice", priorIds);

            foreach (var pInv in priorInvoices)
            {
                var invId = Convert.ToInt64(pInv["id"]);
                var t = ReadInvoiceTotal(pInv);
                var rawR = ReadInvoiceReceived(pInv);
                var alloc = priorAllocated.TryGetValue(invId, out var al) ? al : 0m;
                var r = Math.Max(0m, Math.Min(rawR - alloc, t));
                openingBalance += Math.Max(0m, t - r);
            }
        }

        var priorLedgers = await Db.QueryListAsync(@"
            SELECT *
            FROM customer_ledgers
            WHERE customer_id = @customerId
              AND entry_type IN ('payment', 'manual')
              AND substr(entry_date, 1, 10) < @cleanFrom",
            new { customerId, cleanFrom });

        foreach (var pRow in priorLedgers)
        {
            var entryType = pRow.GetValueOrDefault("entry_type")?.ToString()?.ToLowerInvariant() ?? "";
            if (entryType == "manual")
            {
                openingBalance += (StocktakeService.ToDecimal(pRow.GetValueOrDefault("invoice_total")) -
                                   StocktakeService.ToDecimal(pRow.GetValueOrDefault("total_received")));
            }
            else if (entryType == "payment")
            {
                openingBalance -= StocktakeService.ToDecimal(pRow.GetValueOrDefault("credited_amount"));
            }
        }

        return Math.Round(openingBalance, 2);
    }

    [HttpGet("api/customer-ledger")]
    public async Task<IActionResult> Index(
        [FromQuery(Name = "customer_id")] long? customerId = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        if (!await CanAsync("customer-ledger.view"))
        {
            return Forbidden();
        }

        if (customerId == null || customerId <= 0)
        {
            return Unprocessable("The customer_id field is required.", new Dictionary<string, string[]>
            {
                ["customer_id"] = new[] { "The customer_id field is required." }
            });
        }

        var customer = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = customerId.Value });
        if (customer == null)
        {
            return Unprocessable("The selected customer_id is invalid.", new Dictionary<string, string[]>
            {
                ["customer_id"] = new[] { "The selected customer_id is invalid." }
            });
        }

        if (IsWalkInName(customer.GetValueOrDefault("name")?.ToString()))
        {
            return Ok(new
            {
                data = Array.Empty<object>(),
                summary = new Dictionary<string, object?>
                {
                    ["opening_balance"] = 0m,
                    ["total_invoiced"] = 0m,
                    ["received_on_invoice"] = 0m,
                    ["payments_credited"] = 0m,
                    ["net_balance"] = 0m
                }
            });
        }

        var (rows, summary) = await BuildCustomerLedgerViewAsync(customerId.Value, from, to);
        return Ok(new
        {
            data = rows,
            summary
        });
    }

    [HttpPost("api/customer-ledger")]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("customer-ledger.create"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var customerId = GetNullableLong(body, "customer_id");
        var entryDate = (GetString(body, "entry_date") ?? "").Trim();

        if (customerId == null || customerId <= 0)
        {
            return Unprocessable("The customer_id field is required.", new Dictionary<string, string[]>
            {
                ["customer_id"] = new[] { "The customer_id field is required." }
            });
        }

        if (string.IsNullOrEmpty(entryDate))
        {
            return Unprocessable("The entry_date field is required.", new Dictionary<string, string[]>
            {
                ["entry_date"] = new[] { "The entry_date field is required." }
            });
        }

        var customer = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = customerId.Value });
        if (customer == null)
        {
            return Unprocessable("The selected customer_id is invalid.", new Dictionary<string, string[]>
            {
                ["customer_id"] = new[] { "The selected customer_id is invalid." }
            });
        }

        if (IsWalkInName(customer.GetValueOrDefault("name")?.ToString()))
        {
            const string walkInMsg = "Walk-In Customer bills cannot be saved with unpaid credit (Udhar)—select or create a named Customer if the bill is not paid in full.";
            return Unprocessable(walkInMsg, new Dictionary<string, string[]>
            {
                ["customer_id"] = new[] { walkInMsg }
            });
        }

        var explicitType = (GetString(body, "entry_type") ?? "").Trim().ToLowerInvariant();
        var creditedAmount = GetDecimal(body, "credited_amount", 0m);
        var type = explicitType is ("payment" or "manual")
            ? explicitType
            : (creditedAmount > 0m ? "payment" : "manual");

        var invoiceTotal = GetDecimal(body, "invoice_total", 0m);
        var totalReceived = GetDecimal(body, "total_received", 0m);
        var balanceRemaining = Math.Max(invoiceTotal - totalReceived, 0m);

        var id = await Db.InsertAsync("customer_ledgers", new Dictionary<string, object?>
        {
            ["customer_id"] = customerId.Value,
            ["entry_date"] = DateStr(entryDate, DateTime.UtcNow.ToString("yyyy-MM-dd")),
            ["entry_type"] = type,
            ["is_manual"] = 1,
            ["posted_number"] = GetString(body, "posted_number"),
            ["invoice_total"] = invoiceTotal,
            ["total_received"] = totalReceived,
            ["balance_remaining"] = balanceRemaining,
            ["credited_amount"] = creditedAmount,
            ["payment_ref"] = GetString(body, "payment_ref"),
            ["sale_invoice_id"] = GetNullableLong(body, "sale_invoice_id"),
            ["description"] = GetString(body, "description"),
            ["created_by"] = CurrentUserId
        });

        var row = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customer_ledgers WHERE id = @id", new { id });
        return StatusCode(201, new { status = "ok", row });
    }

    [HttpPut("api/customer-ledger/bulk")]
    public async Task<IActionResult> BulkUpdate()
    {
        if (!await CanAsync("customer-ledger.update"))
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

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        foreach (var el in rowsEl.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var r = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in el.EnumerateObject()) r[p.Name] = p.Value;

            var id = GetNullableLong(r, "id");
            if (id == null || id <= 0) continue;

            var row = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customer_ledgers WHERE id = @id", new { id = id.Value }, conn, tx);
            if (row == null) continue;

            var entryType = row.GetValueOrDefault("entry_type")?.ToString()?.ToLowerInvariant() ?? "";
            bool isManual = row.GetValueOrDefault("is_manual") is true or 1 or 1L;

            if (entryType == "invoice" && !isManual)
            {
                await tx.RollbackAsync();
                return Unprocessable("Cannot update invoice rows");
            }

            bool hasPaymentId = row.TryGetValue("payment_id", out var pid) && pid != null && !string.IsNullOrWhiteSpace(pid.ToString());
            bool hasReturnId = row.TryGetValue("sale_return_id", out var rid) && rid != null && !string.IsNullOrWhiteSpace(rid.ToString());
            bool isSystemLinked = hasPaymentId || hasReturnId;

            var updates = new Dictionary<string, object?>
            {
                ["entry_date"] = DateStr(GetString(r, "entry_date"), row.GetValueOrDefault("entry_date")?.ToString()),
                ["posted_number"] = GetString(r, "posted_number"),
                ["description"] = GetString(r, "description")
            };

            if (entryType == "manual" && !isSystemLinked)
            {
                var invTotal = GetDecimal(r, "invoice_total", 0m);
                var totRecv = GetDecimal(r, "total_received", 0m);
                updates["invoice_total"] = invTotal;
                updates["total_received"] = totRecv;
                updates["balance_remaining"] = Math.Max(invTotal - totRecv, 0m);
            }

            if ((entryType == "payment" || isManual) && !isSystemLinked)
            {
                updates["credited_amount"] = GetDecimal(r, "credited_amount", 0m);
                updates["payment_ref"] = GetString(r, "payment_ref");
            }

            await Db.UpdateAsync("customer_ledgers", id.Value, updates, "id", conn, tx);
        }

        await tx.CommitAsync();
        return Ok(new { status = "ok" });
    }

    [HttpDelete("api/customer-ledger/{customerLedger:long}")]
    public async Task<IActionResult> Destroy(long customerLedger)
    {
        if (!await CanAsync("customer-ledger.delete"))
        {
            return Forbidden();
        }

        var row = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customer_ledgers WHERE id = @id", new { id = customerLedger });
        if (row == null)
        {
            return NotFound(new { message = "Customer ledger entry not found." });
        }

        var entryType = row.GetValueOrDefault("entry_type")?.ToString()?.ToLowerInvariant() ?? "";
        bool isManual = row.GetValueOrDefault("is_manual") is true or 1 or 1L;

        if (entryType == "invoice" && !isManual)
        {
            return Unprocessable("Cannot delete invoice row");
        }

        if (row.TryGetValue("sale_return_id", out var srObj) && srObj != null && !string.IsNullOrWhiteSpace(srObj.ToString()))
        {
            return Unprocessable("Cannot delete auto-synced return credit note row. Delete or edit the Sale Return instead.");
        }

        if (row.TryGetValue("payment_id", out var payObj) && payObj != null && long.TryParse(payObj.ToString(), out var paymentId) &&
            paymentId > 0 && await Db.HasTableAsync("payments"))
        {
            var payment = await Db.QuerySingleOrDefaultAsync("SELECT * FROM payments WHERE id = @id", new { id = paymentId });
            if (payment != null && !string.Equals(payment.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
            {
                await ReverseCustomerPaymentAsync(payment);
                return Ok(new { status = "ok" });
            }
        }

        await Db.ExecuteAsync("DELETE FROM customer_ledgers WHERE id = @id", new { id = customerLedger });
        return Ok(new { status = "ok" });
    }

    [HttpPost("api/customer-ledger/rebuild")]
    public async Task<IActionResult> Rebuild()
    {
        if (!await CanAsync("customer-ledger.update"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var customerId = GetNullableLong(body, "customer_id");
        if (customerId == null || customerId <= 0)
        {
            return Unprocessable("The customer_id field is required.", new Dictionary<string, string[]>
            {
                ["customer_id"] = new[] { "The customer_id field is required." }
            });
        }

        var customer = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = customerId.Value });
        if (customer == null)
        {
            return Unprocessable("The selected customer_id is invalid.", new Dictionary<string, string[]>
            {
                ["customer_id"] = new[] { "The selected customer_id is invalid." }
            });
        }

        if (IsWalkInName(customer.GetValueOrDefault("name")?.ToString()))
        {
            await Db.ExecuteAsync("DELETE FROM customer_ledgers WHERE customer_id = @customerId", new { customerId = customerId.Value });
            return Ok(new { status = "ok" });
        }

        await Db.ExecuteAsync(@"
            DELETE FROM customer_ledgers
            WHERE customer_id = @customerId
              AND sale_invoice_id IN (
                  SELECT id FROM sale_invoices WHERE customer_id = @customerId AND status = 'reversed'
              )",
            new { customerId = customerId.Value });

        var invoices = await Db.QueryListAsync(@"
            SELECT *
            FROM sale_invoices
            WHERE customer_id = @customerId
              AND invoice_type = 'credit'
              AND (status IS NULL OR status != 'reversed')",
            new { customerId = customerId.Value });

        var invIds = invoices.Select(i => Convert.ToInt64(i["id"])).ToList();
        var allocatedByInvoice = await SupplierLedgerController.SumAllocatedByInvoiceIdsAsync(Db, "sale_invoice", invIds);

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        foreach (var inv in invoices)
        {
            var invId = Convert.ToInt64(inv["id"]);
            var invTotal = ReadInvoiceTotal(inv);
            var rawRecv = ReadInvoiceReceived(inv);
            var allocated = allocatedByInvoice.TryGetValue(invId, out var al) ? al : 0m;
            var recv = Math.Max(0m, rawRecv - allocated);
            var recvOnInv = Math.Min(recv, invTotal);
            var excess = Math.Max(0m, recv - invTotal);

            var entryDate = DateStr(inv.GetValueOrDefault("date")?.ToString(), DateTime.UtcNow.ToString("yyyy-MM-dd"));
            var postedNo = FirstNonEmpty(inv.GetValueOrDefault("posted_number")?.ToString(), inv.GetValueOrDefault("invoice_no")?.ToString());
            var invoiceNo = inv.GetValueOrDefault("invoice_no")?.ToString();

            var existing = await Db.QuerySingleOrDefaultAsync(@"
                SELECT id FROM customer_ledgers
                WHERE customer_id = @customerId
                  AND sale_invoice_id = @invId
                  AND entry_type = 'invoice'
                  AND is_manual = 0
                LIMIT 1",
                new { customerId = customerId.Value, invId }, conn, tx);

            var rowData = new Dictionary<string, object?>
            {
                ["customer_id"] = customerId.Value,
                ["sale_invoice_id"] = invId,
                ["entry_type"] = "invoice",
                ["is_manual"] = 0,
                ["entry_date"] = entryDate,
                ["posted_number"] = postedNo,
                ["invoice_total"] = invTotal,
                ["total_received"] = recvOnInv,
                ["balance_remaining"] = Math.Max(invTotal - recvOnInv, 0m),
                ["credited_amount"] = 0m,
                ["payment_ref"] = null,
                ["description"] = !string.IsNullOrWhiteSpace(invoiceNo) ? $"Invoice #{invoiceNo}" : null
            };

            if (existing != null)
            {
                await Db.UpdateAsync("customer_ledgers", Convert.ToInt64(existing["id"]), rowData, "id", conn, tx);
            }
            else
            {
                await Db.InsertAsync("customer_ledgers", rowData, conn, tx);
            }

            var payRef = $"Inv #{postedNo}";
            if (excess > 0m && !string.IsNullOrWhiteSpace(postedNo))
            {
                var existingPay = await Db.QuerySingleOrDefaultAsync(@"
                    SELECT id FROM customer_ledgers
                    WHERE customer_id = @customerId
                      AND payment_ref = @payRef
                      AND entry_type = 'payment'
                      AND is_manual = 0
                    LIMIT 1",
                    new { customerId = customerId.Value, payRef }, conn, tx);

                var payData = new Dictionary<string, object?>
                {
                    ["customer_id"] = customerId.Value,
                    ["sale_invoice_id"] = null,
                    ["entry_type"] = "payment",
                    ["is_manual"] = 0,
                    ["entry_date"] = entryDate,
                    ["posted_number"] = postedNo,
                    ["invoice_total"] = 0m,
                    ["total_received"] = 0m,
                    ["balance_remaining"] = 0m,
                    ["credited_amount"] = excess,
                    ["payment_ref"] = payRef,
                    ["description"] = $"Payment received on invoice {postedNo}"
                };

                if (existingPay != null)
                {
                    await Db.UpdateAsync("customer_ledgers", Convert.ToInt64(existingPay["id"]), payData, "id", conn, tx);
                }
                else
                {
                    await Db.InsertAsync("customer_ledgers", payData, conn, tx);
                }
            }
            else if (!string.IsNullOrWhiteSpace(postedNo))
            {
                await conn.ExecuteAsync(@"
                    DELETE FROM customer_ledgers
                    WHERE customer_id = @customerId
                      AND payment_ref = @payRef
                      AND entry_type = 'payment'
                      AND is_manual = 0",
                    new { customerId = customerId.Value, payRef }, tx);
            }
        }

        await tx.CommitAsync();
        return Ok(new { status = "ok", message = "Rebuilt from sale invoices" });
    }

    [HttpPost("api/customer-ledger/settle-invoice")]
    public async Task<IActionResult> SettleInvoice()
    {
        if (!await CanAsync("customer-ledger.update"))
        {
            return Forbidden();
        }

        var body = await ReadJsonBodyAsync();
        var saleInvoiceId = GetNullableLong(body, "sale_invoice_id");
        if (saleInvoiceId == null || saleInvoiceId <= 0)
        {
            return Unprocessable("The sale_invoice_id field is required.", new Dictionary<string, string[]>
            {
                ["sale_invoice_id"] = new[] { "The sale_invoice_id field is required." }
            });
        }

        var invoice = await Db.QuerySingleOrDefaultAsync("SELECT * FROM sale_invoices WHERE id = @id", new { id = saleInvoiceId.Value });
        if (invoice == null)
        {
            return Unprocessable("The selected sale_invoice_id is invalid.", new Dictionary<string, string[]>
            {
                ["sale_invoice_id"] = new[] { "The selected sale_invoice_id is invalid." }
            });
        }

        var invoiceTotal = ReadInvoiceTotal(invoice);
        var received = ReadInvoiceReceived(invoice);
        var balance = Math.Round(Math.Max(invoiceTotal - received, 0m), 2);

        if (balance <= 0m)
        {
            return Ok(new
            {
                status = "ok",
                settled = 0,
                message = "Invoice is already fully paid"
            });
        }

        var customerId = Convert.ToInt64(invoice["customer_id"]);
        var payMethod = GetString(body, "payment_method") ?? "cash";
        var entryDate = DateStr(GetString(body, "entry_date"), DateTime.UtcNow.ToString("yyyy-MM-dd"))!;
        var accountId = GetNullableLong(body, "account_id");
        var postedNumber = FirstNonEmpty(invoice.GetValueOrDefault("posted_number")?.ToString(), invoice.GetValueOrDefault("invoice_no")?.ToString());
        var refLabel = !string.IsNullOrWhiteSpace(postedNumber) ? postedNumber : $"#{saleInvoiceId.Value}";

        long? paymentId = null;

        if (await Db.HasTableAsync("payments") && await Db.HasTableAsync("payment_allocations"))
        {
            var payNo = await new DocumentSequenceService(Db).NextAsync("payment", "PAY-", 5, "payments", "payment_number");

            Dictionary<string, object?>? account = null;
            if (await Db.HasTableAsync("accounts"))
            {
                if (accountId.HasValue)
                {
                    account = await Db.QuerySingleOrDefaultAsync("SELECT * FROM accounts WHERE id = @id", new { id = accountId.Value });
                }
                if (account == null)
                {
                    var accType = string.Equals(payMethod, "bank_transfer", StringComparison.OrdinalIgnoreCase) ? "bank" : payMethod;
                    account = await Db.QuerySingleOrDefaultAsync(
                        "SELECT * FROM accounts WHERE is_active = 1 AND (type = @t OR is_default = 1) ORDER BY CASE WHEN type = @t AND is_default = 1 THEN 0 WHEN type = @t THEN 1 ELSE 2 END, id ASC LIMIT 1",
                        new { t = accType });
                    if (account != null) accountId = Convert.ToInt64(account["id"]);
                }
            }

            paymentId = await Db.InsertAsync("payments", new Dictionary<string, object?>
            {
                ["payment_number"] = payNo,
                ["party_type"] = "customer",
                ["party_id"] = customerId,
                ["payment_date"] = entryDate,
                ["amount"] = balance,
                ["allocated_amount"] = balance,
                ["unapplied_amount"] = 0m,
                ["payment_method"] = payMethod,
                ["account_id"] = accountId,
                ["reference"] = $"Inv #{postedNumber ?? saleInvoiceId.Value.ToString()}",
                ["notes"] = $"Settled Sale Invoice {refLabel}",
                ["status"] = "posted",
                ["created_by_id"] = CurrentUserId
            });

            await Db.InsertAsync("payment_allocations", new Dictionary<string, object?>
            {
                ["payment_id"] = paymentId.Value,
                ["invoice_type"] = "sale_invoice",
                ["invoice_id"] = saleInvoiceId.Value,
                ["amount"] = balance
            });

            await Db.UpdateAsync("sale_invoices", saleInvoiceId.Value, new Dictionary<string, object?>
            {
                ["total_receive"] = invoiceTotal
            });

            await Db.InsertAsync("customer_ledgers", new Dictionary<string, object?>
            {
                ["customer_id"] = customerId,
                ["sale_invoice_id"] = null,
                ["payment_id"] = paymentId.Value,
                ["entry_type"] = "payment",
                ["entry_date"] = entryDate,
                ["posted_number"] = payNo,
                ["invoice_total"] = 0m,
                ["total_received"] = 0m,
                ["balance_remaining"] = 0m,
                ["credited_amount"] = balance,
                ["payment_ref"] = $"Inv #{postedNumber ?? saleInvoiceId.Value.ToString()}",
                ["description"] = $"Settled Sale Invoice {refLabel} ({payNo})",
                ["is_manual"] = 0,
                ["created_by"] = CurrentUserId
            });

            var accountSvc = new AccountService(Db);
            if (account != null && accountId.HasValue)
            {
                await accountSvc.DepositAsync(
                    account,
                    balance,
                    "customer_payment",
                    paymentId.Value,
                    $"Settled Sale Invoice {refLabel} ({payNo})",
                    CurrentUserId);
            }

            if (accountSvc.IsDefaultCashDrawerAccount(account, payMethod))
            {
                var cashDrawerSvc = new CashDrawerService(Db);
                await cashDrawerSvc.RecordTransactionAsync(
                    null,
                    "customer_payment",
                    balance,
                    "payment",
                    paymentId.Value,
                    $"Settled Sale Invoice {refLabel} ({payNo})",
                    CurrentUserId);
            }
        }
        else
        {
            await Db.UpdateAsync("sale_invoices", saleInvoiceId.Value, new Dictionary<string, object?>
            {
                ["total_receive"] = invoiceTotal
            });

            var ledgerRow = await Db.QuerySingleOrDefaultAsync(
                "SELECT * FROM customer_ledgers WHERE sale_invoice_id = @invId AND entry_type = 'invoice' LIMIT 1",
                new { invId = saleInvoiceId.Value });

            if (ledgerRow != null)
            {
                await Db.UpdateAsync("customer_ledgers", Convert.ToInt64(ledgerRow["id"]), new Dictionary<string, object?>
                {
                    ["total_received"] = invoiceTotal,
                    ["balance_remaining"] = 0m
                });
            }
            else
            {
                await Db.InsertAsync("customer_ledgers", new Dictionary<string, object?>
                {
                    ["customer_id"] = customerId,
                    ["sale_invoice_id"] = saleInvoiceId.Value,
                    ["entry_type"] = "invoice",
                    ["is_manual"] = 0,
                    ["entry_date"] = entryDate,
                    ["posted_number"] = postedNumber,
                    ["invoice_total"] = invoiceTotal,
                    ["total_received"] = invoiceTotal,
                    ["balance_remaining"] = 0m,
                    ["credited_amount"] = 0m,
                    ["description"] = $"Sale invoice {refLabel} (marked paid)",
                    ["created_by"] = CurrentUserId
                });
            }
        }

        return Ok(new
        {
            status = "ok",
            settled = balance,
            sale_invoice_id = saleInvoiceId.Value,
            posted_number = postedNumber,
            payment_id = paymentId
        });
    }

    [HttpGet("api/customer-ledger/print-url")]
    public async Task<IActionResult> PrintUrl(
        [FromQuery(Name = "customer_id")] long? customerId = null,
        [FromQuery] string? type = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        if (!await CanAsync("customer-ledger.view"))
        {
            return Forbidden();
        }

        if (customerId == null || customerId <= 0)
        {
            return Unprocessable("Customer is required");
        }

        var customer = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = customerId.Value });
        if (customer == null)
        {
            return NotFound(new { message = "Customer not found." });
        }

        var queryParams = new Dictionary<string, string?>
        {
            ["customer_id"] = customerId.Value.ToString(),
            ["silent_print"] = "1"
        };
        if (!string.IsNullOrWhiteSpace(type)) queryParams["type"] = type;
        if (!string.IsNullOrWhiteSpace(from)) queryParams["from"] = from;
        if (!string.IsNullOrWhiteSpace(to)) queryParams["to"] = to;

        var url = SignedUrlMiddleware.CreateSignedUrl(
            Request,
            "/customer-ledger/print",
            queryParams,
            TimeSpan.FromMinutes(15));
        var html = await RenderCustomerLedgerHtmlAsync(customer, type, from, to);

        return Ok(new
        {
            url,
            html
        });
    }

    [AllowAnonymous]
    [HttpGet("/customer-ledger/print")]
    public async Task<IActionResult> Print(
        [FromQuery(Name = "customer_id")] long? customerId = null,
        [FromQuery] string? type = null,
        [FromQuery] string? from = null,
        [FromQuery] string? to = null)
    {
        if (customerId == null || customerId <= 0)
        {
            return NotFound("Customer is required");
        }

        var customer = await Db.QuerySingleOrDefaultAsync("SELECT * FROM customers WHERE id = @id", new { id = customerId.Value });
        if (customer == null)
        {
            return NotFound("Customer not found");
        }

        var html = await RenderCustomerLedgerHtmlAsync(customer, type, from, to);
        return Content(html, "text/html; charset=utf-8");
    }

    private async Task<(List<Dictionary<string, object?>> Rows, Dictionary<string, object?> Summary)> BuildCustomerLedgerViewAsync(
        long customerId,
        string? from,
        string? to)
    {
        var mpConditions = new List<string>
        {
            "customer_id = @customerId",
            "entry_type IN ('payment', 'manual')"
        };
        var dynParams = new DynamicParameters();
        dynParams.Add("customerId", customerId);

        if (!string.IsNullOrWhiteSpace(from))
        {
            mpConditions.Add("substr(entry_date, 1, 10) >= @from");
            dynParams.Add("from", from.Trim()[..Math.Min(10, from.Trim().Length)]);
        }

        if (!string.IsNullOrWhiteSpace(to))
        {
            mpConditions.Add("substr(entry_date, 1, 10) <= @to");
            dynParams.Add("to", to.Trim()[..Math.Min(10, to.Trim().Length)]);
        }

        var manualAndPayments = await Db.QueryListAsync(
            $"SELECT * FROM customer_ledgers WHERE {string.Join(" AND ", mpConditions)} ORDER BY entry_date ASC, id ASC",
            dynParams);

        var invoices = new List<Dictionary<string, object?>>();
        if (await Db.HasTableAsync("sale_invoices"))
        {
            var invConditions = new List<string>
            {
                "customer_id = @customerId",
                "invoice_type = 'credit'",
                "(status IS NULL OR status != 'reversed')"
            };
            if (!string.IsNullOrWhiteSpace(from))
            {
                invConditions.Add("substr(date, 1, 10) >= @from");
            }
            if (!string.IsNullOrWhiteSpace(to))
            {
                invConditions.Add("substr(date, 1, 10) <= @to");
            }

            invoices = await Db.QueryListAsync(
                $"SELECT * FROM sale_invoices WHERE {string.Join(" AND ", invConditions)} ORDER BY date ASC, id ASC",
                dynParams);
        }

        var invoiceIds = invoices.Select(i => Convert.ToInt64(i["id"])).ToList();
        var allocatedByInvoice = await SupplierLedgerController.SumAllocatedByInvoiceIdsAsync(Db, "sale_invoice", invoiceIds);

        var all = new List<Dictionary<string, object?>>();

        foreach (var inv in invoices)
        {
            var invId = Convert.ToInt64(inv["id"]);
            var invoiceTotal = ReadInvoiceTotal(inv);
            var rawReceived = ReadInvoiceReceived(inv);
            var allocated = allocatedByInvoice.TryGetValue(invId, out var al) ? al : 0m;
            var receivedOnInv = Math.Max(0m, Math.Min(rawReceived - allocated, invoiceTotal));

            var balanceRemaining = Math.Max(invoiceTotal - receivedOnInv, 0m);
            var invoiceSettled = Math.Min(rawReceived, invoiceTotal);
            var invoiceDue = Math.Max(invoiceTotal - invoiceSettled, 0m);

            var entryDate = DateStr(inv.GetValueOrDefault("date")?.ToString(), null);
            var createdAtStr = inv.GetValueOrDefault("created_at")?.ToString();
            long sortTs = 0;
            if (!string.IsNullOrWhiteSpace(createdAtStr) && DateTimeOffset.TryParse(createdAtStr, out var dto))
            {
                sortTs = dto.ToUnixTimeSeconds();
            }

            var postedNo = FirstNonEmpty(inv.GetValueOrDefault("posted_number")?.ToString(), inv.GetValueOrDefault("invoice_no")?.ToString());
            var descRef = FirstNonEmpty(postedNo, $"#{invId}");

            all.Add(new Dictionary<string, object?>
            {
                ["id"] = invId,
                ["row_key"] = $"invoice-{invId}",
                ["customer_id"] = customerId,
                ["sale_invoice_id"] = invId,
                ["entry_type"] = "invoice",
                ["type_order"] = 1,
                ["is_manual"] = false,
                ["entry_date"] = entryDate,
                ["created_at"] = createdAtStr,
                ["sort_ts"] = sortTs,
                ["posted_number"] = postedNo,
                ["invoice_total"] = invoiceTotal,
                ["total_received"] = Math.Round(receivedOnInv, 2),
                ["credited_amount"] = 0m,
                ["payment_ref"] = "",
                ["balance_remaining"] = Math.Round(balanceRemaining, 2),
                ["invoice_settled"] = Math.Round(invoiceSettled, 2),
                ["invoice_due"] = Math.Round(invoiceDue, 2),
                ["description"] = $"Sale Invoice {descRef}"
            });
        }

        foreach (var r in manualAndPayments)
        {
            var rId = Convert.ToInt64(r["id"]);
            var entryType = r.GetValueOrDefault("entry_type")?.ToString()?.ToLowerInvariant() ?? "manual";
            bool isInvoiceLike = entryType == "manual";
            var invTotal = isInvoiceLike ? StocktakeService.ToDecimal(r.GetValueOrDefault("invoice_total")) : 0m;
            var invRecv = isInvoiceLike ? StocktakeService.ToDecimal(r.GetValueOrDefault("total_received")) : 0m;
            var balRemain = isInvoiceLike
                ? (invTotal < 0m ? (invTotal - invRecv) : Math.Max(invTotal - invRecv, 0m))
                : 0m;

            var createdAtStr = r.GetValueOrDefault("created_at")?.ToString();
            long sortTs = 0;
            if (!string.IsNullOrWhiteSpace(createdAtStr) && DateTimeOffset.TryParse(createdAtStr, out var dto))
            {
                sortTs = dto.ToUnixTimeSeconds();
            }

            all.Add(new Dictionary<string, object?>
            {
                ["id"] = rId,
                ["row_key"] = $"ledger-{rId}",
                ["customer_id"] = customerId,
                ["sale_invoice_id"] = r.GetValueOrDefault("sale_invoice_id"),
                ["sale_return_id"] = r.GetValueOrDefault("sale_return_id"),
                ["payment_id"] = r.GetValueOrDefault("payment_id"),
                ["entry_type"] = entryType,
                ["type_order"] = isInvoiceLike ? 2 : 3,
                ["is_manual"] = r.GetValueOrDefault("is_manual") is true or 1 or 1L,
                ["entry_date"] = DateStr(r.GetValueOrDefault("entry_date")?.ToString(), null),
                ["created_at"] = createdAtStr,
                ["sort_ts"] = sortTs,
                ["posted_number"] = r.GetValueOrDefault("posted_number"),
                ["invoice_total"] = invTotal,
                ["total_received"] = invRecv,
                ["credited_amount"] = StocktakeService.ToDecimal(r.GetValueOrDefault("credited_amount")),
                ["payment_ref"] = r.GetValueOrDefault("payment_ref"),
                ["balance_remaining"] = Math.Round(balRemain, 2),
                ["description"] = r.GetValueOrDefault("description")
            });
        }

        all.Sort((a, b) =>
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

        var openingBalance = await CalculateOpeningBalanceAsync(customerId, from);
        var runningBalance = openingBalance;
        decimal totalInvoiced = 0m;
        decimal receivedOnInvTotal = 0m;
        decimal paymentsCred = 0m;

        for (int idx = 0; idx < all.Count; idx++)
        {
            var row = all[idx];
            var type = row.GetValueOrDefault("entry_type")?.ToString()?.ToLowerInvariant() ?? "";

            if (type is ("invoice" or "manual"))
            {
                runningBalance += StocktakeService.ToDecimal(row.GetValueOrDefault("balance_remaining"));
                totalInvoiced += StocktakeService.ToDecimal(row.GetValueOrDefault("invoice_total"));
                receivedOnInvTotal += StocktakeService.ToDecimal(row.GetValueOrDefault("total_received"));
            }
            else if (type == "payment")
            {
                var cred = StocktakeService.ToDecimal(row.GetValueOrDefault("credited_amount"));
                runningBalance -= cred;
                paymentsCred += cred;
            }

            row["seq"] = idx + 1;
            row["running_balance"] = Math.Round(runningBalance, 2);
        }

        var net = openingBalance + (totalInvoiced - receivedOnInvTotal) - paymentsCred;
        var summary = new Dictionary<string, object?>
        {
            ["opening_balance"] = Math.Round(openingBalance, 2),
            ["total_invoiced"] = Math.Round(totalInvoiced, 2),
            ["received_on_invoice"] = Math.Round(receivedOnInvTotal, 2),
            ["payments_credited"] = Math.Round(paymentsCred, 2),
            ["net_balance"] = Math.Round(net, 2)
        };

        return (all, summary);
    }

    private async Task ReverseCustomerPaymentAsync(Dictionary<string, object?> payment)
    {
        var paymentId = Convert.ToInt64(payment["id"]);
        var paymentService = new PaymentService(Db);
        await paymentService.ReversePaymentAsync(paymentId, "Deleted from Customer Ledger", CurrentUserId);
    }

    private async Task<string> RenderCustomerLedgerHtmlAsync(
        Dictionary<string, object?> customer,
        string? type,
        string? from,
        string? to)
    {
        var customerId = Convert.ToInt64(customer["id"]);
        var customerName = customer.GetValueOrDefault("name")?.ToString() ?? $"Customer #{customerId}";
        var settingVm = await PrintViewModelMapper.LoadPrintSettingAsync(Db);
        var (rows, summary) = await BuildCustomerLedgerViewAsync(customerId, from, to);

        var rowVms = rows.Select(r => new PrintLedgerRowViewModel
        {
            EntryDate = r.GetValueOrDefault("entry_date")?.ToString() ?? "",
            EntryType = r.GetValueOrDefault("entry_type")?.ToString() ?? "invoice",
            PostedNumber = r.GetValueOrDefault("posted_number")?.ToString(),
            InvoiceNumber = r.GetValueOrDefault("invoice_number")?.ToString(),
            InvoiceTotal = StocktakeService.ToDecimal(r.GetValueOrDefault("invoice_total")),
            TotalReceived = StocktakeService.ToDecimal(r.GetValueOrDefault("total_received")),
            CreditedAmount = StocktakeService.ToDecimal(r.GetValueOrDefault("credited_amount")),
            PaymentRef = r.GetValueOrDefault("payment_ref")?.ToString(),
            CreditRemainingCalc = StocktakeService.ToDecimal(r.GetValueOrDefault("credit_remaining_calc")),
            RunningBalance = StocktakeService.ToDecimal(r.GetValueOrDefault("running_balance")),
            Description = r.GetValueOrDefault("description")?.ToString()
        }).ToList();

        var summaryVm = new PrintLedgerSummaryViewModel
        {
            OpeningBalance = StocktakeService.ToDecimal(summary.GetValueOrDefault("opening_balance")),
            TotalInvoiced = StocktakeService.ToDecimal(summary.GetValueOrDefault("total_invoiced")),
            ReceivedOnInvoice = StocktakeService.ToDecimal(summary.GetValueOrDefault("received_on_invoice")),
            PaymentsCredited = StocktakeService.ToDecimal(summary.GetValueOrDefault("payments_credited")),
            NetBalance = StocktakeService.ToDecimal(summary.GetValueOrDefault("net_balance"))
        };

        var paperSize = string.Equals(type, "thermal", StringComparison.OrdinalIgnoreCase) ? "thermal" : "a4";
        return _renderer.RenderPartyLedger("customer", paperSize, customerName, rowVms, summaryVm, settingVm, from, to, PrintViewModelMapper.QueryToDict(Request.Query));
    }

    private static decimal ReadInvoiceTotal(IDictionary<string, object?> inv)
    {
        foreach (var k in new[] { "invoice_total", "total", "grand_total", "net_total", "gross_amount", "sub_total" })
        {
            if (inv.TryGetValue(k, out var v) && v != null && !string.IsNullOrWhiteSpace(v.ToString()))
            {
                return StocktakeService.ToDecimal(v);
            }
        }
        return 0m;
    }

    private static decimal ReadInvoiceReceived(IDictionary<string, object?> inv)
    {
        foreach (var k in new[] { "total_receive", "total_recieve", "received", "amount_received" })
        {
            if (inv.TryGetValue(k, out var v) && v != null && !string.IsNullOrWhiteSpace(v.ToString()))
            {
                return StocktakeService.ToDecimal(v);
            }
        }
        return 0m;
    }

    private static string? FirstNonEmpty(params string?[] vals)
    {
        foreach (var v in vals)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return null;
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

using System.Globalization;
using System.Text;
using Dapper;
using Karobar.Api.Infrastructure;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Karobar.Api.Controllers;

[Route("api/suppliers")]
public class SupplierController : ApiControllerBase
{
    private static readonly string[] LookupPermissions =
    [
        "supplier.view", "barcode.view", "barcode.print", "batch-expiry.view",
        "purchase-invoice.view", "purchase-invoice.create", "purchase-invoice.update",
        "purchase-return.view", "purchase-return.create", "purchase-return.update",
        "purchase-order.view", "purchase-order.create", "purchase-order.update",
        "supplier-ledger.view", "payment.view", "payment.create",
        "report.purchase-detail.view", "report.purchase-summary.view", "report.purchase-returns.view",
        "report.current-stock.view", "report.reorder.view", "report.near-expiry-product.view"
    ];

    public SupplierController(KarobarDb db) : base(db)
    {
    }

    public static void AttachPolicyLabel(Dictionary<string, object?> supplier)
    {
        var pol = supplier.TryGetValue("expiry_return_policy", out var p) ? p?.ToString() : "before_expiry";
        int? months = supplier.TryGetValue("expiry_return_months", out var m) && m != null && int.TryParse(m.ToString(), out var mv) ? mv : 3;
        int? days = supplier.TryGetValue("expiry_return_days", out var d) && d != null && int.TryParse(d.ToString(), out var dv) ? dv : 90;
        supplier["expiry_policy_label"] = BrandController.FormatExpiryPolicyLabel(pol, months, days);
    }

    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q = "",
        [FromQuery] int limit = 50,
        [FromQuery] int page = 0,
        [FromQuery(Name = "per_page")] int perPage = 20)
    {
        if (!await CanAsync(LookupPermissions)) return Forbidden();

        q = (q ?? "").Trim();
        limit = Math.Clamp(limit <= 0 ? 50 : limit, 1, 10000);

        var where = "";
        var param = new DynamicParameters();
        if (!string.IsNullOrEmpty(q))
        {
            where = "WHERE name LIKE @q";
            param.Add("q", $"%{q}%");
        }

        if (page >= 1)
        {
            perPage = Math.Clamp(perPage <= 0 ? 20 : perPage, 1, 100);
            var paged = await Db.PaginateAsync(
                $"SELECT * FROM suppliers {where} ORDER BY name ASC",
                $"SELECT COUNT(1) FROM suppliers {where}",
                param,
                page,
                perPage,
                items =>
                {
                    foreach (var it in items) AttachPolicyLabel(it);
                    return Task.FromResult(items);
                });
            return Ok(paged);
        }

        param.Add("limit", limit);
        var rows = await Db.QueryListAsync($"SELECT * FROM suppliers {where} ORDER BY name ASC LIMIT @limit", param);
        foreach (var r in rows) AttachPolicyLabel(r);
        return Ok(rows);
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        if (!await CanAsync("supplier.view")) return Forbidden();

        var suppliers = await Db.QueryListAsync(@"
            SELECT s.*, (SELECT COUNT(1) FROM products p WHERE p.supplier_id = s.id) AS products_count
            FROM suppliers s
            ORDER BY s.name ASC");

        if (suppliers.Count > 0)
        {
            var supplierIds = suppliers.Select(s => Convert.ToInt64(s["id"] ?? 0)).Where(id => id > 0).ToArray();
            var batchesBySupplier = new Dictionary<long, List<Dictionary<string, object?>>>();

            if (supplierIds.Length > 0 && await Db.HasTableAsync("batches"))
            {
                var batchRows = await Db.QueryListAsync(@"
                    SELECT p.supplier_id, b.expiry_date, b.quantity,
                           br.expiry_return_policy AS brand_policy,
                           br.expiry_return_months AS brand_months,
                           br.expiry_return_days AS brand_days
                    FROM batches b
                    INNER JOIN products p ON p.id = b.product_id
                    LEFT JOIN brands br ON br.id = p.brand_id
                    WHERE p.supplier_id IN @supplierIds AND b.expiry_date IS NOT NULL AND b.quantity > 0",
                    new { supplierIds });

                foreach (var br in batchRows)
                {
                    var sid = Convert.ToInt64(br["supplier_id"] ?? 0);
                    if (!batchesBySupplier.TryGetValue(sid, out var list))
                    {
                        list = new List<Dictionary<string, object?>>();
                        batchesBySupplier[sid] = list;
                    }
                    list.Add(br);
                }
            }

            var bulkPayables = await GetBulkSupplierNetBalancesAsync(Db, supplierIds);

            foreach (var supplier in suppliers)
            {
                AttachPolicyLabel(supplier);
                var sid = Convert.ToInt64(supplier["id"] ?? 0);
                int dueCount = 0;
                int dueUnits = 0;

                if (batchesBySupplier.TryGetValue(sid, out var supBatches))
                {
                    foreach (var bRow in supBatches)
                    {
                        var policy = supplier["expiry_return_policy"]?.ToString()
                                     ?? bRow["brand_policy"]?.ToString()
                                     ?? "before_expiry";
                        var months = supplier["expiry_return_months"] != null
                            ? Convert.ToInt32(supplier["expiry_return_months"])
                            : (bRow["brand_months"] != null ? Convert.ToInt32(bRow["brand_months"]) : 3);
                        var days = supplier["expiry_return_days"] != null
                            ? Convert.ToInt32(supplier["expiry_return_days"])
                            : (bRow["brand_days"] != null ? Convert.ToInt32(bRow["brand_days"]) : 90);

                        var eval = BrandController.EvaluateBatchIntimation(bRow["expiry_date"]?.ToString(), policy, months, days);
                        if (eval["is_intimation_due"] is true)
                        {
                            dueCount++;
                            dueUnits += Convert.ToInt32(bRow["quantity"] ?? 0);
                        }
                    }
                }

                supplier["intimation_due_count"] = dueCount;
                supplier["intimation_due_units"] = dueUnits;
                supplier["current_payable"] = bulkPayables.TryGetValue(sid, out var pay) ? pay : 0.0;
            }
        }

        return Ok(suppliers);
    }

    [HttpPost]
    public async Task<IActionResult> Store()
    {
        if (!await CanAsync("supplier.create")) return Forbidden();

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name field is required."]
            });
        }

        var exists = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM suppliers WHERE name = @name", new { name });
        if (exists > 0)
        {
            return Unprocessable("The name has already been taken.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name has already been taken."]
            });
        }

        var openingBalance = Math.Round(GetDecimal(body, "opening_balance", 0m), 2);
        var data = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["address"] = GetString(body, "address"),
            ["phone"] = GetString(body, "phone"),
            ["tax_number"] = GetString(body, "tax_number"),
            ["strn"] = GetString(body, "strn"),
            ["tax_status"] = GetString(body, "tax_status"),
            ["province"] = GetString(body, "province"),
            ["email"] = GetString(body, "email"),
            ["expiry_return_policy"] = GetString(body, "expiry_return_policy"),
            ["expiry_return_months"] = GetString(body, "expiry_return_months"),
            ["expiry_return_days"] = GetString(body, "expiry_return_days"),
            ["expiry_return_notes"] = GetString(body, "expiry_return_notes")
        };

        if (body.ContainsKey("credit_limit"))
        {
            var clRaw = GetString(body, "credit_limit");
            data["credit_limit"] = !string.IsNullOrWhiteSpace(clRaw) && decimal.TryParse(clRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var cl) ? cl : null;
        }
        if (body.ContainsKey("credit_days"))
        {
            var cdRaw = GetString(body, "credit_days");
            data["credit_days"] = !string.IsNullOrWhiteSpace(cdRaw) && int.TryParse(cdRaw, out var cd) ? cd : null;
        }

        if (await Db.HasColumnAsync("suppliers", "expiry_return_policy"))
        {
            data = BrandController.NormalizeExpiryPolicyPayload(data);
        }

        await using var conn = await Db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var id = await Db.InsertAsync("suppliers", data, conn, tx);

        if (openingBalance > 0 && await Db.HasTableAsync("supplier_ledgers"))
        {
            await Db.InsertAsync("supplier_ledgers", new Dictionary<string, object?>
            {
                ["supplier_id"] = id,
                ["entry_type"] = "manual",
                ["is_manual"] = true,
                ["entry_date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                ["posted_number"] = "OB-" + id.ToString().PadLeft(5, '0'),
                ["invoice_total"] = openingBalance,
                ["total_paid"] = 0m,
                ["credit_remaining"] = openingBalance,
                ["debited_amount"] = 0m,
                ["description"] = "Opening Balance",
                ["created_by"] = CurrentUserId
            }, conn, tx);
        }

        await tx.CommitAsync();

        var supplier = await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @id", new { id });
        if (supplier != null) AttachPolicyLabel(supplier);
        return StatusCode(201, supplier);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Show(long id)
    {
        if (!await CanAsync("supplier.view")) return Forbidden();

        var supplier = await Db.QuerySingleOrDefaultAsync(@"
            SELECT s.*, (SELECT COUNT(1) FROM products p WHERE p.supplier_id = s.id) AS products_count
            FROM suppliers s WHERE s.id = @id", new { id });

        if (supplier == null) return NotFound(new { message = "Supplier not found." });
        AttachPolicyLabel(supplier);

        var intimationBatches = new List<Dictionary<string, object?>>();
        if (await Db.HasTableAsync("batches"))
        {
            var rows = await Db.QueryListAsync(@"
                SELECT b.id AS batch_id, b.batch_number, b.expiry_date, b.quantity,
                       p.id AS product_id, p.name AS product_name, p.product_code, p.pack_size,
                       COALESCE(br.name, '') AS brand_name
                FROM batches b
                INNER JOIN products p ON p.id = b.product_id
                LEFT JOIN brands br ON br.id = p.brand_id
                WHERE p.supplier_id = @id AND b.expiry_date IS NOT NULL AND b.quantity > 0
                ORDER BY b.expiry_date ASC
                LIMIT 150", new { id });

            var pol = supplier["expiry_return_policy"]?.ToString() ?? "before_expiry";
            var months = supplier["expiry_return_months"] != null ? Convert.ToInt32(supplier["expiry_return_months"]) : 3;
            var days = supplier["expiry_return_days"] != null ? Convert.ToInt32(supplier["expiry_return_days"]) : 90;

            foreach (var r in rows)
            {
                var eval = BrandController.EvaluateBatchIntimation(r["expiry_date"]?.ToString(), pol, months, days);
                foreach (var kv in eval) r[kv.Key] = kv.Value;
                intimationBatches.Add(r);
            }
        }

        supplier["intimation_batches"] = intimationBatches;
        return Ok(supplier);
    }

    [HttpGet("{id:long}/credit-summary")]
    public async Task<IActionResult> CreditSummary(long id)
    {
        if (!await CanAsync(LookupPermissions)) return Forbidden();

        var supplier = await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @id", new { id });
        if (supplier == null) return NotFound(new { message = "Supplier not found." });

        var openInvoices = await GetOpenInvoicesForSupplierAsync(Db, id);
        var balances = await GetBulkSupplierNetBalancesAsync(Db, [id]);
        var totalOutstanding = balances.TryGetValue(id, out var nb) ? nb : openInvoices.Sum(i => Convert.ToDouble(i["due"] ?? 0.0));

        double? creditLimit = supplier.TryGetValue("credit_limit", out var clObj) && clObj != null && double.TryParse(clObj.ToString(), out var clVal)
            ? clVal
            : null;
        double? availableCredit = creditLimit.HasValue ? Math.Max(0.0, Math.Round(creditLimit.Value - totalOutstanding, 2)) : null;

        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        double totalOverdue = 0.0;
        int overdueCount = 0;
        string? oldestDue = null;

        foreach (var inv in openInvoices)
        {
            var dueDate = inv["due_date"]?.ToString() ?? inv["date"]?.ToString() ?? "";
            if (!string.IsNullOrEmpty(dueDate) && string.CompareOrdinal(dueDate, today) < 0)
            {
                totalOverdue += Convert.ToDouble(inv["due"] ?? 0.0);
                overdueCount++;
                if (oldestDue == null || string.CompareOrdinal(dueDate, oldestDue) < 0)
                {
                    oldestDue = dueDate;
                }
            }
        }

        if (await Db.HasTableAsync("supplier_ledgers"))
        {
            var creditDays = Convert.ToInt32(supplier["credit_days"] ?? 30);
            var manualRows = await Db.QueryListAsync(@"
                SELECT invoice_total, total_paid, due_date, entry_date
                FROM supplier_ledgers
                WHERE supplier_id = @id AND purchase_invoice_id IS NULL AND entry_type IN ('manual', 'invoice')",
                new { id });

            foreach (var mRow in manualRows)
            {
                var mDue = Math.Max(0.0, Math.Round(Convert.ToDouble(mRow["invoice_total"] ?? 0.0) - Convert.ToDouble(mRow["total_paid"] ?? 0.0), 2));
                if (mDue <= 0.01) continue;

                string? dueDate = CustomerController.FormatDateYmd(mRow["due_date"]);
                if (string.IsNullOrEmpty(dueDate))
                {
                    var entryStr = CustomerController.FormatDateYmd(mRow["entry_date"]);
                    if (!string.IsNullOrEmpty(entryStr) && DateTime.TryParse(entryStr, out var edt))
                    {
                        dueDate = edt.AddDays(creditDays).ToString("yyyy-MM-dd");
                    }
                }

                if (!string.IsNullOrEmpty(dueDate) && string.CompareOrdinal(dueDate, today) < 0)
                {
                    totalOverdue += mDue;
                    overdueCount++;
                    if (oldestDue == null || string.CompareOrdinal(dueDate, oldestDue) < 0)
                    {
                        oldestDue = dueDate;
                    }
                }
            }
        }

        totalOverdue = Math.Min(totalOverdue, totalOutstanding);
        if (totalOverdue <= 0.01)
        {
            totalOverdue = 0.0;
            overdueCount = 0;
            oldestDue = null;
        }

        return Ok(new Dictionary<string, object?>
        {
            ["supplier_id"] = id,
            ["name"] = supplier["name"],
            ["phone"] = supplier["phone"],
            ["credit_limit"] = creditLimit,
            ["credit_days"] = supplier["credit_days"] ?? 30,
            ["net_balance"] = Math.Round(totalOutstanding, 2),
            ["current_balance"] = Math.Round(totalOutstanding, 2),
            ["available_credit"] = availableCredit,
            ["is_limit_exceeded"] = creditLimit.HasValue && creditLimit.Value > 0 && totalOutstanding > creditLimit.Value,
            ["total_overdue"] = Math.Round(totalOverdue, 2),
            ["overdue_invoices_count"] = overdueCount,
            ["oldest_due_date"] = oldestDue
        });
    }

    [HttpPut("{id:long}")]
    [HttpPatch("{id:long}")]
    public async Task<IActionResult> Update(long id)
    {
        if (!await CanAsync("supplier.update")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Supplier not found." });

        var body = await ReadJsonBodyAsync();
        var name = GetString(body, "name")?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            return Unprocessable("The name field is required.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name field is required."]
            });
        }

        var dup = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM suppliers WHERE name = @name AND id != @id", new { name, id });
        if (dup > 0)
        {
            return Unprocessable("The name has already been taken.", new Dictionary<string, string[]>
            {
                ["name"] = ["The name has already been taken."]
            });
        }

        var data = new Dictionary<string, object?>
        {
            ["name"] = name
        };

        foreach (var k in new[] { "address", "phone", "tax_number", "strn", "tax_status", "province", "email" })
        {
            if (body.ContainsKey(k)) data[k] = GetString(body, k);
        }
        if (body.ContainsKey("credit_limit"))
        {
            var clRaw = GetString(body, "credit_limit");
            data["credit_limit"] = !string.IsNullOrWhiteSpace(clRaw) && decimal.TryParse(clRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var cl) ? cl : null;
        }
        if (body.ContainsKey("credit_days"))
        {
            var cdRaw = GetString(body, "credit_days");
            data["credit_days"] = !string.IsNullOrWhiteSpace(cdRaw) && int.TryParse(cdRaw, out var cd) ? cd : null;
        }

        if (body.ContainsKey("expiry_return_policy") || body.ContainsKey("expiry_return_months") ||
            body.ContainsKey("expiry_return_days") || body.ContainsKey("expiry_return_notes"))
        {
            var merged = new Dictionary<string, object?>
            {
                ["expiry_return_policy"] = body.ContainsKey("expiry_return_policy") ? GetString(body, "expiry_return_policy") : existing["expiry_return_policy"] ?? "before_expiry",
                ["expiry_return_months"] = body.ContainsKey("expiry_return_months") ? GetString(body, "expiry_return_months") : existing["expiry_return_months"] ?? 3,
                ["expiry_return_days"] = body.ContainsKey("expiry_return_days") ? GetString(body, "expiry_return_days") : existing["expiry_return_days"] ?? 90,
                ["expiry_return_notes"] = body.ContainsKey("expiry_return_notes") ? GetString(body, "expiry_return_notes") : existing["expiry_return_notes"]
            };
            var norm = BrandController.NormalizeExpiryPolicyPayload(merged);
            foreach (var kv in norm) data[kv.Key] = kv.Value;
        }

        await Db.UpdateAsync("suppliers", id, data);
        var fresh = await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @id", new { id });
        if (fresh != null) AttachPolicyLabel(fresh);
        return Ok(fresh);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Destroy(long id)
    {
        if (!await CanAsync("supplier.delete")) return Forbidden();

        var existing = await Db.QuerySingleOrDefaultAsync("SELECT * FROM suppliers WHERE id = @id", new { id });
        if (existing == null) return NotFound(new { message = "Supplier not found." });

        var inUse = await Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM products WHERE supplier_id = @id", new { id }) > 0;
        if (inUse)
        {
            return Unprocessable("Cannot delete: supplier is used by one or more products.");
        }

        await Db.ExecuteAsync("DELETE FROM suppliers WHERE id = @id", new { id });
        return NoContent();
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export()
    {
        if (!await CanAsync("supplier.export")) return Forbidden();

        var rows = await Db.QueryListAsync(@"
            SELECT name, address, phone, expiry_return_policy, expiry_return_months, expiry_return_days, expiry_return_notes
            FROM suppliers
            ORDER BY name ASC");

        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("name,address,phone,expiry_return_policy,expiry_return_months,expiry_return_days,expiry_return_notes");

        foreach (var s in rows)
        {
            var line = string.Join(",",
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(s["name"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(s["address"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(s["phone"])),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(s["expiry_return_policy"] ?? "before_expiry")),
                BrandController.EscapeCsv((s["expiry_return_months"] ?? 3).ToString()!),
                BrandController.EscapeCsv((s["expiry_return_days"] ?? 90).ToString()!),
                BrandController.EscapeCsv(BrandController.SanitizeCsvCell(s["expiry_return_notes"])));
            sb.AppendLine(line);
        }

        var fileName = $"suppliers_{DateTime.UtcNow:yyyy-MM-dd_HH-mm-ss}.csv";
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv; charset=UTF-8", fileName);
    }

    public static async Task<Dictionary<long, double>> GetBulkSupplierNetBalancesAsync(KarobarDb db, long[] supplierIds)
    {
        var result = new Dictionary<long, double>();
        if (supplierIds.Length == 0 || !await db.HasTableAsync("supplier_ledgers"))
        {
            return result;
        }

        var ledgerRows = await db.QueryListAsync(@"
            SELECT sl.*, pi.total_amount AS pi_total_amount, pi.total_paid AS pi_total_paid, pi.status AS pi_status
            FROM supplier_ledgers sl
            LEFT JOIN purchase_invoices pi ON pi.id = sl.purchase_invoice_id
            WHERE sl.supplier_id IN @supplierIds", new { supplierIds });

        var linkedInvIds = new HashSet<long>();
        foreach (var r in ledgerRows)
        {
            if (r["purchase_invoice_id"] != null && long.TryParse(r["purchase_invoice_id"]!.ToString(), out var pid) && pid > 0)
            {
                linkedInvIds.Add(pid);
            }
        }

        var unlinkedRows = await db.QueryListAsync(@"
            SELECT id, supplier_id, total_amount, total_paid
            FROM purchase_invoices
            WHERE supplier_id IN @supplierIds
              AND invoice_type = 'credit'
              AND (status IS NULL OR status != 'reversed')", new { supplierIds });

        var allInvIds = new HashSet<long>(linkedInvIds);
        foreach (var u in unlinkedRows)
        {
            var uid = Convert.ToInt64(u["id"] ?? 0);
            if (uid > 0) allInvIds.Add(uid);
        }

        var allocatedByInv = new Dictionary<long, double>();
        if (allInvIds.Count > 0 && await db.HasTableAsync("payment_allocations") && await db.HasTableAsync("payments"))
        {
            var allocRows = await db.QueryListAsync(@"
                SELECT pa.invoice_id, SUM(COALESCE(pa.amount, 0)) AS sum_alloc
                FROM payment_allocations pa
                INNER JOIN payments p ON p.id = pa.payment_id
                WHERE pa.invoice_type = 'purchase_invoice' AND p.status = 'posted' AND pa.invoice_id IN @invIds
                GROUP BY pa.invoice_id", new { invIds = allInvIds.ToArray() });

            foreach (var ar in allocRows)
            {
                var iid = Convert.ToInt64(ar["invoice_id"] ?? 0);
                allocatedByInv[iid] = Convert.ToDouble(ar["sum_alloc"] ?? 0.0);
            }
        }

        foreach (var sid in supplierIds.Distinct())
        {
            double totalInvoiced = 0.0;
            double paidOnInvoice = 0.0;
            double paymentsDebited = 0.0;

            foreach (var row in ledgerRows.Where(r => Convert.ToInt64(r["supplier_id"] ?? 0) == sid))
            {
                var entryType = row["entry_type"]?.ToString() ?? "";
                var piId = row["purchase_invoice_id"] != null ? Convert.ToInt64(row["purchase_invoice_id"]) : 0;

                if (entryType == "invoice" && piId > 0)
                {
                    if (row["pi_status"]?.ToString() == "reversed") continue;
                    var invTotal = Convert.ToDouble(row["pi_total_amount"] ?? row["invoice_total"] ?? 0.0);
                    var rawPaid = Convert.ToDouble(row["pi_total_paid"] ?? row["total_paid"] ?? 0.0);
                    var alloc = allocatedByInv.TryGetValue(piId, out var aVal) ? aVal : 0.0;
                    var paidOnInv = Math.Max(0.0, Math.Min(rawPaid - alloc, invTotal));

                    totalInvoiced += invTotal;
                    paidOnInvoice += paidOnInv;
                }
                else if (entryType is "invoice" or "manual")
                {
                    var invTotal = Convert.ToDouble(row["invoice_total"] ?? 0.0);
                    var paidOnInv = Math.Min(Convert.ToDouble(row["total_paid"] ?? 0.0), invTotal);

                    totalInvoiced += invTotal;
                    paidOnInvoice += paidOnInv;
                }
                else if (entryType == "payment")
                {
                    paymentsDebited += Convert.ToDouble(row["debited_amount"] ?? 0.0);
                }
            }

            foreach (var uInv in unlinkedRows.Where(u => Convert.ToInt64(u["supplier_id"] ?? 0) == sid))
            {
                var uid = Convert.ToInt64(uInv["id"] ?? 0);
                if (linkedInvIds.Contains(uid)) continue;

                var uTotal = Convert.ToDouble(uInv["total_amount"] ?? 0.0);
                var uRawPaid = Convert.ToDouble(uInv["total_paid"] ?? 0.0);
                var uAlloc = allocatedByInv.TryGetValue(uid, out var aVal) ? aVal : 0.0;
                var uPaidOnInv = Math.Max(0.0, Math.Min(uRawPaid - uAlloc, uTotal));

                totalInvoiced += uTotal;
                paidOnInvoice += uPaidOnInv;
            }

            var netBalance = (totalInvoiced - paidOnInvoice) - paymentsDebited;
            result[sid] = netBalance > 0 ? Math.Round(netBalance, 2) : 0.0;
        }

        return result;
    }

    public static async Task<List<Dictionary<string, object?>>> GetOpenInvoicesForSupplierAsync(KarobarDb db, long supplierId)
    {
        if (!await db.HasTableAsync("purchase_invoices")) return new List<Dictionary<string, object?>>();

        var rows = await db.QueryListAsync(@"
            SELECT id, posted_number, invoice_number, posted_date, due_date, total_amount, total_paid
            FROM purchase_invoices
            WHERE supplier_id = @supplierId
              AND invoice_type = 'credit'
              AND (status IS NULL OR status != 'reversed')
              AND (COALESCE(total_amount, 0) - COALESCE(total_paid, 0)) > 0.01
            ORDER BY posted_date ASC, id ASC", new { supplierId });

        var open = new List<Dictionary<string, object?>>();
        foreach (var inv in rows)
        {
            var total = Convert.ToDouble(inv["total_amount"] ?? 0.0);
            var paid = Convert.ToDouble(inv["total_paid"] ?? 0.0);
            var remaining = Math.Round(Math.Max(total - paid, 0.0), 2);
            if (remaining > 0.01)
            {
                var dateStr = CustomerController.FormatDateYmd(inv["posted_date"]);
                var dueStr = CustomerController.FormatDateYmd(inv["due_date"]) ?? dateStr;
                var postedNo = inv["posted_number"]?.ToString();
                if (string.IsNullOrEmpty(postedNo)) postedNo = inv["invoice_number"]?.ToString();
                if (string.IsNullOrEmpty(postedNo)) postedNo = $"#{inv["id"]}";

                open.Add(new Dictionary<string, object?>
                {
                    ["id"] = inv["id"],
                    ["posted_number"] = postedNo,
                    ["date"] = dateStr,
                    ["due_date"] = dueStr,
                    ["total"] = total,
                    ["paid"] = paid,
                    ["due"] = remaining
                });
            }
        }
        return open;
    }
}

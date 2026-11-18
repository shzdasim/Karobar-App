using System.Data;
using System.Text.Json;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class ReturnEntitlementService
{
    private readonly KarobarDb _db;

    public ReturnEntitlementService(KarobarDb db)
    {
        _db = db;
    }

    public async Task<Dictionary<string, int>> RemainingAsync(
        string kind,
        long invoiceId,
        List<Dictionary<string, object?>>? invoiceItems = null,
        long? excludeReturnId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        bool isSale = string.Equals(kind, "sale", StringComparison.OrdinalIgnoreCase);
        var headersTable = isSale ? "sale_returns" : "purchase_returns";
        var linesTable = isSale ? "sale_return_items" : "purchase_return_items";
        var linkCol = isSale ? "sale_invoice_id" : "purchase_invoice_id";
        var parentCol = isSale ? "sale_return_id" : "purchase_return_id";
        var qtyCol = isSale ? "unit_return_quantity" : "return_unit_quantity";
        var batchCol = isSale ? "batch_number" : "batch";
        var invLinesTable = isSale ? "sale_invoice_items" : "purchase_invoice_items";

        invoiceItems ??= await _db.QueryListAsync(
            $"SELECT * FROM {invLinesTable} WHERE {linkCol} = @invoiceId",
            new { invoiceId },
            conn,
            tx);

        var original = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in invoiceItems)
        {
            var pid = ToLong(item.GetValueOrDefault("product_id"));
            var batch = item.GetValueOrDefault(batchCol)?.ToString() ?? "";
            var expiry = item.GetValueOrDefault("expiry")?.ToString() ?? "";
            var key = BuildKey(pid, batch, expiry);

            var qty = (int)ToLong(item.GetValueOrDefault("quantity"));
            var bonus = isSale ? Math.Max(0, (int)ToLong(item.GetValueOrDefault("bonus"))) : 0;
            var units = qty + bonus;

            original[key] = original.GetValueOrDefault(key, 0) + units;
        }

        if (await _db.HasTableAsync(headersTable) && await _db.HasTableAsync(linesTable))
        {
            var sql = $@"
                SELECT l.product_id, l.{batchCol} as batch, l.expiry, l.{qtyCol} as quantity
                FROM {linesTable} l
                INNER JOIN {headersTable} h ON h.id = l.{parentCol}
                WHERE h.{linkCol} = @invoiceId";

            if (excludeReturnId.HasValue && excludeReturnId.Value > 0)
            {
                sql += " AND h.id != @excludeId";
            }

            var returned = await _db.QueryListAsync(sql, new { invoiceId, excludeId = excludeReturnId }, conn, tx);
            foreach (var line in returned)
            {
                var pid = ToLong(line.GetValueOrDefault("product_id"));
                var batch = (line.GetValueOrDefault("batch")?.ToString() ?? "").Trim();
                var expiry = (line.GetValueOrDefault("expiry")?.ToString() ?? "").Trim();
                var retQty = (int)ToLong(line.GetValueOrDefault("quantity"));
                var key = BuildKey(pid, batch, expiry);

                if (original.ContainsKey(key) || !string.IsNullOrEmpty(batch) || !string.IsNullOrEmpty(expiry))
                {
                    original[key] = original.GetValueOrDefault(key, 0) - retQty;
                }
                else
                {
                    var remToDeduct = retQty;
                    var prefix = $"[{pid},";
                    foreach (var oKey in original.Keys.ToList())
                    {
                        if (remToDeduct <= 0) break;
                        var oQty = original[oKey];
                        if (oKey.StartsWith(prefix, StringComparison.Ordinal) && oQty > 0)
                        {
                            var deduct = Math.Min(oQty, remToDeduct);
                            original[oKey] = oQty - deduct;
                            remToDeduct -= deduct;
                        }
                    }

                    if (remToDeduct > 0)
                    {
                        original[key] = original.GetValueOrDefault(key, 0) - remToDeduct;
                    }
                }
            }
        }

        return original;
    }

    public async Task AnnotateAsync(
        string kind,
        long invoiceId,
        List<Dictionary<string, object?>> items,
        long? excludeReturnId = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        bool isSale = string.Equals(kind, "sale", StringComparison.OrdinalIgnoreCase);
        var batchCol = isSale ? "batch_number" : "batch";
        var remaining = await RemainingAsync(kind, invoiceId, items, excludeReturnId, conn, tx);

        foreach (var item in items)
        {
            var pid = ToLong(item.GetValueOrDefault("product_id"));
            var batch = item.GetValueOrDefault(batchCol)?.ToString() ?? "";
            var expiry = item.GetValueOrDefault("expiry")?.ToString() ?? "";
            var key = BuildKey(pid, batch, expiry);
            item["returnable_quantity"] = Math.Max(0, remaining.GetValueOrDefault(key, 0));
        }
    }

    public async Task ValidateAsync(
        string kind,
        IDictionary<string, object?> data,
        List<Dictionary<string, object?>> items,
        Dictionary<string, object?>? existingReturn = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        bool isSale = string.Equals(kind, "sale", StringComparison.OrdinalIgnoreCase);
        var linkCol = isSale ? "sale_invoice_id" : "purchase_invoice_id";
        var partyCol = isSale ? "customer_id" : "supplier_id";
        var invoiceTable = isSale ? "sale_invoices" : "purchase_invoices";
        var returnTable = isSale ? "sale_returns" : "purchase_returns";

        if (existingReturn != null)
        {
            var existingId = ToLong(existingReturn.GetValueOrDefault("id"));
            var fresh = await _db.QuerySingleOrDefaultAsync(
                $"SELECT * FROM {returnTable} WHERE id = @id",
                new { id = existingId },
                conn,
                tx);
            if (fresh == null)
            {
                throw new InvoiceValidationException("Return record not found.", statusCode: 404);
            }
            if (ToLong(fresh.GetValueOrDefault(linkCol)) != ToLong(existingReturn.GetValueOrDefault(linkCol)))
            {
                throw new InvoiceValidationException("This return changed. Reload it before editing.", statusCode: 409);
            }
        }

        var linkedInvoiceId = ToLong(data.TryGetValue(linkCol, out var lObj) ? lObj : null);
        if (linkedInvoiceId <= 0)
        {
            return;
        }

        var invoice = await _db.QuerySingleOrDefaultAsync(
            $"SELECT * FROM {invoiceTable} WHERE id = @id",
            new { id = linkedInvoiceId },
            conn,
            tx);

        var partyId = ToLong(data.TryGetValue(partyCol, out var pObj) ? pObj : null);
        if (invoice == null || ToLong(invoice.GetValueOrDefault(partyCol)) != partyId)
        {
            var partyName = isSale ? "customer." : "supplier.";
            throw new InvoiceValidationException($"The selected invoice does not belong to this {partyName}", new Dictionary<string, string[]>
            {
                [linkCol] = new[] { $"The selected invoice does not belong to this {partyName}" }
            });
        }

        long? excludeId = existingReturn != null ? ToLong(existingReturn.GetValueOrDefault("id")) : null;
        var remaining = await RemainingAsync(kind, linkedInvoiceId, null, excludeId, conn, tx);

        var totalByProduct = new Dictionary<long, int>();
        foreach (var kv in remaining)
        {
            try
            {
                using var doc = JsonDocument.Parse(kv.Key);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var pid = doc.RootElement[0].GetInt64();
                    if (pid > 0)
                    {
                        totalByProduct[pid] = totalByProduct.GetValueOrDefault(pid, 0) + Math.Max(0, kv.Value);
                    }
                }
            }
            catch
            {
            }
        }

        var requested = new Dictionary<string, int>(StringComparer.Ordinal);
        var requestedByProduct = new Dictionary<long, int>();
        var field = isSale ? "unit_return_quantity" : "return_unit_quantity";
        var batchField = isSale ? "batch_number" : "batch";

        for (int index = 0; index < items.Count; index++)
        {
            var row = items[index];
            var pid = ToLong(row.GetValueOrDefault("product_id"));
            var batchVal = (row.GetValueOrDefault(batchField)?.ToString() ?? "").Trim();
            var expiryVal = (row.GetValueOrDefault("expiry")?.ToString() ?? "").Trim();
            var key = BuildKey(pid, batchVal, expiryVal);
            var qty = (int)ToLong(row.GetValueOrDefault(field));

            requestedByProduct[pid] = requestedByProduct.GetValueOrDefault(pid, 0) + qty;
            var productAvailable = Math.Max(0, totalByProduct.GetValueOrDefault(pid, 0));

            if (string.IsNullOrEmpty(batchVal) && string.IsNullOrEmpty(expiryVal) && !remaining.ContainsKey(key) && totalByProduct.ContainsKey(pid))
            {
                if (requestedByProduct[pid] > productAvailable)
                {
                    var msg = $"Only {productAvailable} units remain available to return for this product/batch on the selected invoice.";
                    throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                    {
                        [$"items.{index}.{field}"] = new[] { msg }
                    });
                }
                continue;
            }

            var available = Math.Max(0, remaining.GetValueOrDefault(key, 0));
            requested[key] = requested.GetValueOrDefault(key, 0) + qty;
            if (!remaining.ContainsKey(key) || requested[key] > available || requestedByProduct[pid] > productAvailable)
            {
                var msg = $"Only {available} units remain available to return for this product/batch on the selected invoice.";
                throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                {
                    [$"items.{index}.{field}"] = new[] { msg }
                });
            }
        }
    }

    public static string BuildKey(long productId, string? batch, string? expiry)
    {
        var b = (batch ?? "").Trim();
        var e = (expiry ?? "").Trim();
        if (e.Length > 10) e = e.Substring(0, 10);
        return JsonSerializer.Serialize(new object[] { productId, b, e });
    }

    private static long ToLong(object? val)
    {
        if (val == null) return 0;
        if (val is long l) return l;
        if (val is int i) return i;
        return long.TryParse(val.ToString(), out var p) ? p : 0;
    }
}

using System.Data;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class PurchaseOrderReceivingService
{
    private readonly KarobarDb _db;

    public PurchaseOrderReceivingService(KarobarDb db)
    {
        _db = db;
    }

    public async Task ValidateReceiptAsync(
        long purchaseOrderId,
        List<Dictionary<string, object?>> items,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        var poItems = await _db.QueryListAsync(
            "SELECT * FROM purchase_order_items WHERE purchase_order_id = @poId",
            new { poId = purchaseOrderId },
            conn,
            tx);

        var byProductId = new Dictionary<long, Dictionary<string, object?>>();
        foreach (var poi in poItems)
        {
            var pid = ToLong(poi.GetValueOrDefault("product_id"));
            if (pid > 0) byProductId[pid] = poi;
        }

        long totalReceivedUnits = 0;
        var negativeCheckFields = new[]
        {
            "pack_quantity", "pack_bonus", "unit_quantity", "unit_bonus", "quantity",
            "unit_purchase_price", "pack_purchase_price", "unit_sale_price", "pack_sale_price",
            "whole_sale_unit_price", "whole_sale_pack_price"
        };

        for (int i = 0; i < items.Count; i++)
        {
            var row = items[i];
            var pid = ToLong(row.GetValueOrDefault("product_id"));
            if (byProductId.TryGetValue(pid, out var line))
            {
                row["purchase_order_item_id"] = line.GetValueOrDefault("id");
            }
            else
            {
                row["purchase_order_item_id"] = null;
            }

            foreach (var field in negativeCheckFields)
            {
                if (row.TryGetValue(field, out var fVal) && fVal != null && ToDouble(fVal) < 0)
                {
                    throw new InvoiceValidationException("The value cannot be negative.", new Dictionary<string, string[]>
                    {
                        [$"items.{i}.{field}"] = new[] { "The value cannot be negative." }
                    });
                }
            }

            if (row.TryGetValue("pack_size", out var psVal) && psVal != null && ToLong(psVal) < 1)
            {
                throw new InvoiceValidationException("Pack size must be positive.", new Dictionary<string, string[]>
                {
                    [$"items.{i}.pack_size"] = new[] { "Pack size must be positive." }
                });
            }

            if (row.TryGetValue("item_discount_percentage", out var discVal) && discVal != null && ToDouble(discVal) > 100)
            {
                throw new InvoiceValidationException("Discount cannot exceed 100%.", new Dictionary<string, string[]>
                {
                    [$"items.{i}.item_discount_percentage"] = new[] { "Discount cannot exceed 100%." }
                });
            }

            var qty = (int)ToLong(row.GetValueOrDefault("quantity"));
            var unitQty = (int)ToLong(row.GetValueOrDefault("unit_quantity"));
            var unitBonus = (int)ToLong(row.GetValueOrDefault("unit_bonus"));
            if (qty != unitQty + unitBonus)
            {
                throw new InvoiceValidationException("Stock quantity must equal paid units plus bonus units.", new Dictionary<string, string[]>
                {
                    [$"items.{i}.quantity"] = new[] { "Stock quantity must equal paid units plus bonus units." }
                });
            }

            totalReceivedUnits += qty;
        }

        if (totalReceivedUnits <= 0)
        {
            throw new InvoiceValidationException("Receive at least one unit.", new Dictionary<string, string[]>
            {
                ["items"] = new[] { "Receive at least one unit." }
            });
        }
    }

    public async Task SyncAsync(long purchaseOrderId, long? userId = null, IDbConnection? conn = null, IDbTransaction? tx = null)
    {
        var order = await _db.QuerySingleOrDefaultAsync(
            "SELECT * FROM purchase_orders WHERE id = @id",
            new { id = purchaseOrderId },
            conn,
            tx);
        if (order == null) return;

        var poItems = await _db.QueryListAsync(
            "SELECT * FROM purchase_order_items WHERE purchase_order_id = @id",
            new { id = purchaseOrderId },
            conn,
            tx);

        var invoices = await _db.QueryListAsync(
            "SELECT * FROM purchase_invoices WHERE purchase_order_id = @id ORDER BY id ASC",
            new { id = purchaseOrderId },
            conn,
            tx);

        var activeInvoices = invoices
            .Where(inv => !string.Equals(inv.GetValueOrDefault("status")?.ToString(), "reversed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var activeInvoiceIds = activeInvoices
            .Select(inv => ToLong(inv.GetValueOrDefault("id")))
            .Where(id => id > 0)
            .ToHashSet();

        var hasInvoices = activeInvoices.Count > 0;
        var invoiceItems = new List<Dictionary<string, object?>>();
        if (activeInvoiceIds.Count > 0)
        {
            invoiceItems = await _db.QueryListAsync(
                "SELECT * FROM purchase_invoice_items WHERE purchase_invoice_id IN @ids",
                new { ids = activeInvoiceIds.ToArray() },
                conn,
                tx);
        }

        bool complete = poItems.Count > 0 && poItems.All(line =>
        {
            var lineId = ToLong(line.GetValueOrDefault("id"));
            var orderedQty = ToLong(line.GetValueOrDefault("ordered_quantity"));
            var activeQty = invoiceItems
                .Where(ii => ToLong(ii.GetValueOrDefault("purchase_order_item_id")) == lineId)
                .Sum(ii => ToLong(ii.GetValueOrDefault("unit_quantity")));
            return activeQty >= orderedQty;
        });

        var status = hasInvoices ? (complete ? "received" : "partially_received") : "ordered";
        var latestSupplierId = hasInvoices
            ? (activeInvoices.Last().GetValueOrDefault("supplier_id") ?? order.GetValueOrDefault("supplier_id"))
            : order.GetValueOrDefault("supplier_id");

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        await _db.UpdateAsync("purchase_orders", purchaseOrderId, new Dictionary<string, object?>
        {
            ["supplier_id"] = latestSupplierId,
            ["status"] = status,
            ["received_by"] = hasInvoices ? (order.GetValueOrDefault("received_by") ?? userId) : null,
            ["updated_by"] = userId,
            ["received_at"] = hasInvoices ? (order.GetValueOrDefault("received_at") ?? now) : null
        }, "id", conn, tx);

        await SyncUndeliveredUserDemandsAsync(order, poItems, invoiceItems, status, userId, conn, tx);
    }

    private async Task SyncUndeliveredUserDemandsAsync(
        Dictionary<string, object?> order,
        List<Dictionary<string, object?>> poItems,
        List<Dictionary<string, object?>> activeInvoiceItems,
        string status,
        long? userId,
        IDbConnection? conn,
        IDbTransaction? tx)
    {
        if (!await _db.HasTableAsync("user_demands")) return;

        var poId = ToLong(order.GetValueOrDefault("id"));
        var orderNumber = order.GetValueOrDefault("number")?.ToString();
        if (string.IsNullOrWhiteSpace(orderNumber))
        {
            orderNumber = "PO-" + poId.ToString("D5");
        }
        var note = $"Undelivered from Purchase Order {orderNumber}";

        if (status != "partially_received")
        {
            await _db.ExecuteAsync(
                "DELETE FROM user_demands WHERE notes = @note AND status = 'pending'",
                new { note },
                conn,
                tx);
            return;
        }

        foreach (var line in poItems)
        {
            var lineId = ToLong(line.GetValueOrDefault("id"));
            var productId = ToLong(line.GetValueOrDefault("product_id"));
            var orderedQty = (int)ToLong(line.GetValueOrDefault("ordered_quantity"));
            var receivedUnits = (int)activeInvoiceItems
                .Where(ii => ToLong(ii.GetValueOrDefault("purchase_order_item_id")) == lineId)
                .Sum(ii => ToLong(ii.GetValueOrDefault("unit_quantity")));
            var remainingUnits = Math.Max(0, orderedQty - receivedUnits);

            if (remainingUnits <= 0)
            {
                await _db.ExecuteAsync(
                    "DELETE FROM user_demands WHERE product_id = @productId AND notes = @note AND status = 'pending'",
                    new { productId, note },
                    conn,
                    tx);
                continue;
            }

            await _db.ExecuteAsync(
                "UPDATE user_demands SET status = 'ordered', updated_at = @now WHERE product_id = @productId AND status = 'pending' AND (notes IS NULL OR notes != @note)",
                new { productId, note, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") },
                conn,
                tx);

            var productName = await _db.ExecuteScalarAsync<string?>(
                "SELECT name FROM products WHERE id = @productId",
                new { productId },
                conn,
                tx) ?? productId.ToString();

            var existing = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM user_demands WHERE product_id = @productId AND notes = @note LIMIT 1",
                new { productId, note },
                conn,
                tx);

            if (existing != null)
            {
                await _db.UpdateAsync("user_demands", existing["id"]!, new Dictionary<string, object?>
                {
                    ["requested_name"] = productName,
                    ["requested_quantity"] = remainingUnits,
                    ["status"] = "pending"
                }, "id", conn, tx);
            }
            else
            {
                await _db.InsertAsync("user_demands", new Dictionary<string, object?>
                {
                    ["product_id"] = productId,
                    ["requested_by"] = userId ?? order.GetValueOrDefault("created_by"),
                    ["requested_name"] = productName,
                    ["requested_quantity"] = remainingUnits,
                    ["notes"] = note,
                    ["status"] = "pending"
                }, conn, tx);
            }
        }
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

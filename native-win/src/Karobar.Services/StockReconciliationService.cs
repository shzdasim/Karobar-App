using Dapper;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public sealed class ProductReconciliationResult
{
    public long ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public decimal StoredStock { get; init; }
    public decimal BatchedStock { get; init; }
    public decimal UnbatchedStock => StoredStock - BatchedStock;
    public decimal TransactionDerivedStock { get; init; }
    public decimal? MovementDerivedStock { get; init; }
    public int MovementCount { get; init; }
    public bool HasNegativeStock => StoredStock < 0m || HasNegativeBatch;
    public bool HasNegativeBatch { get; init; }
    public bool BatchExceedsProductStock => BatchedStock > StoredStock;
    public bool HasLedgerMismatch => MovementDerivedStock.HasValue && MovementDerivedStock.Value != StoredStock;
    public bool HasTransactionMismatch => TransactionDerivedStock != StoredStock;
    public int DuplicateMovementGroups { get; init; }
}

/// <summary>
/// Strictly read-only stock reconciliation engine comparing stored product stock,
/// batch totals, immutable inventory movements, and underlying business transactions.
/// </summary>
public class StockReconciliationService
{
    private readonly KarobarDb _db;

    public StockReconciliationService(KarobarDb db)
    {
        _db = db;
    }

    public async Task<ProductReconciliationResult?> ReconcileProductAsync(long productId)
    {
        var all = await ReconcileInternalAsync(productId);
        return all.FirstOrDefault();
    }

    public async Task<List<ProductReconciliationResult>> ReconcileAllAsync()
    {
        return await ReconcileInternalAsync(null);
    }

    private async Task<List<ProductReconciliationResult>> ReconcileInternalAsync(long? singleProductId)
    {
        await using var conn = await _db.OpenConnectionAsync();
        var whereProd = singleProductId.HasValue ? "WHERE id = @pid" : "";
        var products = (await conn.QueryAsync<dynamic>(
            $"SELECT id, name, quantity FROM products {whereProd} ORDER BY id ASC",
            new { pid = singleProductId })).ToList();

        if (products.Count == 0) return new List<ProductReconciliationResult>();

        var hasBatches = await _db.HasTableAsync("batches");
        var hasMovements = await _db.HasTableAsync("inventory_movements");
        var hasDisposals = await _db.HasTableAsync("stock_disposals") && await _db.HasTableAsync("stock_disposal_items");

        var batchTotals = new Dictionary<long, (decimal Qty, bool HasNeg)>();
        if (hasBatches)
        {
            var bWhere = singleProductId.HasValue ? "WHERE product_id = @pid" : "";
            foreach (var r in await conn.QueryAsync<dynamic>(
                $"SELECT product_id, COALESCE(SUM(quantity), 0) AS qty, SUM(CASE WHEN quantity < 0 THEN 1 ELSE 0 END) AS neg_cnt FROM batches {bWhere} GROUP BY product_id",
                new { pid = singleProductId }))
            {
                batchTotals[(long)r.product_id] = (Convert.ToDecimal(r.qty), Convert.ToInt64(r.neg_cnt) > 0);
            }
        }

        var txTotals = new Dictionary<long, decimal>();
        void AddDelta(long pid, decimal delta) => txTotals[pid] = txTotals.GetValueOrDefault(pid, 0m) + delta;

        var pFilter = singleProductId.HasValue ? "AND pii.product_id = @pid" : "";
        foreach (var r in await conn.QueryAsync<dynamic>(
            $@"SELECT pii.product_id, COALESCE(SUM(pii.quantity), 0) AS qty
               FROM purchase_invoice_items pii
               INNER JOIN purchase_invoices pi ON pi.id = pii.purchase_invoice_id
               WHERE (pi.status IS NULL OR LOWER(pi.status) = 'posted') {pFilter}
               GROUP BY pii.product_id", new { pid = singleProductId }))
        {
            AddDelta((long)r.product_id, Convert.ToDecimal(r.qty));
        }

        var sFilter = singleProductId.HasValue ? "AND sii.product_id = @pid" : "";
        foreach (var r in await conn.QueryAsync<dynamic>(
            $@"SELECT sii.product_id, COALESCE(SUM(sii.quantity + CASE WHEN COALESCE(sii.bonus, 0) > 0 THEN sii.bonus ELSE 0 END), 0) AS qty
               FROM sale_invoice_items sii
               INNER JOIN sale_invoices si ON si.id = sii.sale_invoice_id
               WHERE (si.status IS NULL OR LOWER(si.status) = 'posted') {sFilter}
               GROUP BY sii.product_id", new { pid = singleProductId }))
        {
            AddDelta((long)r.product_id, -Convert.ToDecimal(r.qty));
        }

        var srFilter = singleProductId.HasValue ? "WHERE product_id = @pid" : "";
        foreach (var r in await conn.QueryAsync<dynamic>(
            $"SELECT product_id, COALESCE(SUM(unit_return_quantity), 0) AS qty FROM sale_return_items {srFilter} GROUP BY product_id",
            new { pid = singleProductId }))
        {
            AddDelta((long)r.product_id, Convert.ToDecimal(r.qty));
        }

        foreach (var r in await conn.QueryAsync<dynamic>(
            $"SELECT product_id, COALESCE(SUM(return_unit_quantity), 0) AS qty FROM purchase_return_items {srFilter} GROUP BY product_id",
            new { pid = singleProductId }))
        {
            AddDelta((long)r.product_id, -Convert.ToDecimal(r.qty));
        }

        foreach (var r in await conn.QueryAsync<dynamic>(
            $"SELECT product_id, COALESCE(SUM(CAST(diff_qty AS REAL)), 0) AS qty FROM stock_adjustment_items {srFilter} GROUP BY product_id",
            new { pid = singleProductId }))
        {
            AddDelta((long)r.product_id, Convert.ToDecimal(r.qty));
        }

        if (hasDisposals)
        {
            var dFilter = singleProductId.HasValue ? "AND sdi.product_id = @pid" : "";
            foreach (var r in await conn.QueryAsync<dynamic>(
                $@"SELECT sdi.product_id, COALESCE(SUM(CAST(sdi.dispose_qty AS REAL)), 0) AS qty
                   FROM stock_disposal_items sdi
                   INNER JOIN stock_disposals sd ON sd.id = sdi.stock_disposal_id
                   WHERE LOWER(sd.status) IN ('quarantined', 'disposed')
                     AND sd.stock_adjustment_id IS NULL {dFilter}
                   GROUP BY sdi.product_id", new { pid = singleProductId }))
            {
                AddDelta((long)r.product_id, -Convert.ToDecimal(r.qty));
            }
        }

        var movTotals = new Dictionary<long, (decimal Net, int Count)>();
        var dupMovs = new Dictionary<long, int>();
        if (hasMovements)
        {
            var mWhere = singleProductId.HasValue ? "WHERE product_id = @pid" : "";
            foreach (var r in await conn.QueryAsync<dynamic>(
                $@"SELECT product_id, COALESCE(SUM(CAST(quantity_in AS REAL) - CAST(quantity_out AS REAL)), 0) AS net_qty, COUNT(1) AS cnt
                   FROM inventory_movements {mWhere}
                   GROUP BY product_id", new { pid = singleProductId }))
            {
                movTotals[(long)r.product_id] = (Convert.ToDecimal(r.net_qty), Convert.ToInt32(r.cnt));
            }

            var dupWhere = singleProductId.HasValue ? "AND product_id = @pid" : "";
            foreach (var r in await conn.QueryAsync<dynamic>(
                $@"SELECT product_id, COUNT(1) AS dup_groups
                   FROM (
                       SELECT product_id, transaction_type, transaction_id, transaction_item_id, COALESCE(batch_id, 0) AS bid
                       FROM inventory_movements
                       WHERE transaction_id IS NOT NULL {dupWhere}
                       GROUP BY product_id, transaction_type, transaction_id, transaction_item_id, COALESCE(batch_id, 0)
                       HAVING COUNT(1) > 1
                   ) t
                   GROUP BY product_id", new { pid = singleProductId }))
            {
                dupMovs[(long)r.product_id] = Convert.ToInt32(r.dup_groups);
            }
        }

        var results = new List<ProductReconciliationResult>(products.Count);
        foreach (var p in products)
        {
            long pid = (long)p.id;
            decimal stored = p.quantity != null ? Convert.ToDecimal(p.quantity) : 0m;
            var bt = batchTotals.GetValueOrDefault(pid, (0m, false));
            var hasMov = movTotals.TryGetValue(pid, out var mv);

            results.Add(new ProductReconciliationResult
            {
                ProductId = pid,
                ProductName = (string)(p.name ?? string.Empty),
                StoredStock = stored,
                BatchedStock = bt.Item1,
                HasNegativeBatch = bt.Item2,
                TransactionDerivedStock = txTotals.GetValueOrDefault(pid, 0m),
                MovementDerivedStock = hasMov ? mv.Net : null,
                MovementCount = hasMov ? mv.Count : 0,
                DuplicateMovementGroups = dupMovs.GetValueOrDefault(pid, 0)
            });
        }

        return results;
    }
}

using System.Data;
using Dapper;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

/// <summary>
/// Records immutable inventory movement entries inside the active database transaction.
/// </summary>
public class InventoryLedgerService
{
    public const string BaselineReference = "BASELINE-INIT";
    public const string BaselineTxType = "baseline";
    private readonly KarobarDb _db;

    public InventoryLedgerService(KarobarDb db)
    {
        _db = db;
    }

    /// <summary>
    /// Idempotently seeds one-time baseline movements for all pre-existing product and batch balances
    /// without modifying products.quantity or batches.quantity.
    /// </summary>
    public async Task<int> EnsureBaselineInitializedAsync(long? userId = null)
    {
        if (!await _db.HasTableAsync("inventory_movements")) return 0;

        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);
        try
        {
            var alreadySeeded = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM inventory_movements WHERE transaction_type = @txType",
                new { txType = BaselineTxType },
                tx);
            if (alreadySeeded > 0)
            {
                await tx.CommitAsync();
                return 0;
            }

            var hasBatches = await _db.HasTableAsync("batches");
            var batchByProduct = new Dictionary<long, List<(long Id, string BatchNo, decimal Qty)>>();
            if (hasBatches)
            {
                foreach (var b in await conn.QueryAsync<dynamic>(
                    "SELECT id, product_id, batch_number, quantity FROM batches WHERE COALESCE(quantity, 0) != 0 ORDER BY id ASC",
                    transaction: tx))
                {
                    long pid = (long)b.product_id;
                    if (!batchByProduct.TryGetValue(pid, out var list))
                    {
                        list = new List<(long, string, decimal)>();
                        batchByProduct[pid] = list;
                    }
                    list.Add(((long)b.id, (string)(b.batch_number ?? string.Empty), Convert.ToDecimal(b.quantity)));
                }
            }

            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            var inserted = 0;

            foreach (var p in await conn.QueryAsync<dynamic>(
                "SELECT id, quantity FROM products ORDER BY id ASC",
                transaction: tx))
            {
                long pid = (long)p.id;
                decimal prodQty = p.quantity != null ? Convert.ToDecimal(p.quantity) : 0m;
                batchByProduct.TryGetValue(pid, out var batches);

                decimal batchedSum = 0m;
                if (batches != null)
                {
                    foreach (var (bid, bNo, bQty) in batches)
                    {
                        batchedSum += bQty;
                        await InsertBaselineRowAsync(conn, tx, pid, bid, bNo, bQty, prodQty, userId, now);
                        inserted++;
                    }
                }

                decimal unbatchedQty = prodQty - batchedSum;
                if (unbatchedQty != 0m)
                {
                    await InsertBaselineRowAsync(conn, tx, pid, null, null, unbatchedQty, prodQty, userId, now);
                    inserted++;
                }
            }

            await tx.CommitAsync();
            return inserted;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
    }

    private Task<long> InsertBaselineRowAsync(
        IDbConnection conn,
        IDbTransaction tx,
        long productId,
        long? batchId,
        string? batchNumber,
        decimal deltaUnits,
        decimal balanceAfter,
        long? userId,
        string now)
    {
        var qtyIn = deltaUnits > 0m ? deltaUnits : 0m;
        var qtyOut = deltaUnits < 0m ? Math.Abs(deltaUnits) : 0m;
        var reason = batchId.HasValue
            ? "Pre-ledger existing batch balance"
            : (deltaUnits < 0m
                ? "Pre-ledger legacy discrepancy: batch stock exceeds product stock"
                : "Pre-ledger existing unbatched balance");

        return _db.InsertAsync("inventory_movements", new Dictionary<string, object?>
        {
            ["product_id"] = productId,
            ["batch_id"] = batchId,
            ["batch_number"] = string.IsNullOrWhiteSpace(batchNumber) ? null : batchNumber.Trim(),
            ["transaction_type"] = BaselineTxType,
            ["transaction_id"] = null,
            ["transaction_item_id"] = null,
            ["reference_number"] = BaselineReference,
            ["quantity_in"] = qtyIn,
            ["quantity_out"] = qtyOut,
            ["balance_after"] = balanceAfter,
            ["unit"] = "unit",
            ["conversion_factor"] = 1m,
            ["reason"] = reason,
            ["user_id"] = userId,
            ["created_at"] = now
        }, conn, tx);
    }

    public async Task<long?> RecordMovementAsync(
        IDbConnection conn,
        IDbTransaction tx,
        long productId,
        long? batchId,
        string? batchNumber,
        string transactionType,
        long? transactionId,
        long? transactionItemId,
        string referenceNumber,
        decimal deltaUnits,
        decimal balanceAfter,
        string? reason = null,
        long? userId = null,
        string unit = "unit",
        decimal conversionFactor = 1m)
    {
        if (productId <= 0 || deltaUnits == 0m) return null;
        if (!await _db.HasTableAsync("inventory_movements")) return null;

        var qtyIn = deltaUnits > 0m ? deltaUnits : 0m;
        var qtyOut = deltaUnits < 0m ? Math.Abs(deltaUnits) : 0m;
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        return await _db.InsertAsync("inventory_movements", new Dictionary<string, object?>
        {
            ["product_id"] = productId,
            ["batch_id"] = batchId,
            ["batch_number"] = string.IsNullOrWhiteSpace(batchNumber) ? null : batchNumber.Trim(),
            ["transaction_type"] = transactionType,
            ["transaction_id"] = transactionId,
            ["transaction_item_id"] = transactionItemId,
            ["reference_number"] = referenceNumber,
            ["quantity_in"] = qtyIn,
            ["quantity_out"] = qtyOut,
            ["balance_after"] = balanceAfter,
            ["unit"] = string.IsNullOrWhiteSpace(unit) ? "unit" : unit,
            ["conversion_factor"] = conversionFactor <= 0m ? 1m : conversionFactor,
            ["reason"] = reason,
            ["user_id"] = userId,
            ["created_at"] = now
        }, conn, tx);
    }
}

using System.Data;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class SupplierProductSortingService
{
    private readonly KarobarDb _db;

    public SupplierProductSortingService(KarobarDb db)
    {
        _db = db;
    }

    public async Task<List<long>> ProductIdsAsync(long supplierId, IDbConnection? conn = null, IDbTransaction? tx = null)
    {
        if (supplierId <= 0 || !await _db.HasTableAsync("supplier_product_positions"))
        {
            return new List<long>();
        }

        var rows = await _db.QueryListAsync(
            "SELECT product_id FROM supplier_product_positions WHERE supplier_id = @supplierId ORDER BY position ASC, id ASC",
            new { supplierId },
            conn,
            tx);

        return rows
            .Select(r => r.TryGetValue("product_id", out var pid) && pid != null && long.TryParse(pid.ToString(), out var parsed) ? parsed : 0L)
            .Where(id => id > 0)
            .ToList();
    }

    public async Task RememberAsync(long supplierId, IEnumerable<long> productIds, IDbConnection? conn = null, IDbTransaction? tx = null)
    {
        if (supplierId <= 0 || !await _db.HasTableAsync("supplier_product_positions")) return;

        var submitted = productIds.Where(id => id > 0).Distinct().ToList();
        if (submitted.Count == 0) return;

        var existing = await ProductIdsAsync(supplierId, conn, tx);
        var knownSet = new HashSet<long>(existing);
        var submittedSet = new HashSet<long>(submitted);

        // 1) Fill slots already held by submitted products in submitted order
        var queue = new Queue<long>(submitted.Where(id => knownSet.Contains(id)));
        var merged = new List<long>(existing.Count + submitted.Count);
        foreach (var id in existing)
        {
            merged.Add(submittedSet.Contains(id) && queue.Count > 0 ? queue.Dequeue() : id);
        }

        // 2) Insert newly seen products right after their submitted predecessor
        for (int i = 0; i < submitted.Count; i++)
        {
            var id = submitted[i];
            if (knownSet.Contains(id)) continue;

            int prevIdx = i > 0 ? merged.IndexOf(submitted[i - 1]) : -1;
            if (prevIdx >= 0)
            {
                merged.Insert(prevIdx + 1, id);
                continue;
            }

            int? firstSubmitted = null;
            for (int pos = 0; pos < merged.Count; pos++)
            {
                if (submittedSet.Contains(merged[pos]))
                {
                    firstSubmitted = pos;
                    break;
                }
            }

            merged.Insert(firstSubmitted ?? merged.Count, id);
        }

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        for (int position = 0; position < merged.Count; position++)
        {
            var pid = merged[position];
            var existingRow = await _db.QuerySingleOrDefaultAsync(
                "SELECT id FROM supplier_product_positions WHERE supplier_id = @supplierId AND product_id = @pid",
                new { supplierId, pid },
                conn,
                tx);

            if (existingRow != null && existingRow.TryGetValue("id", out var rowId) && rowId != null)
            {
                await _db.ExecuteAsync(
                    "UPDATE supplier_product_positions SET position = @position, updated_at = @now WHERE id = @id",
                    new { position, now, id = rowId },
                    conn,
                    tx);
            }
            else
            {
                await _db.InsertAsync("supplier_product_positions", new Dictionary<string, object?>
                {
                    ["supplier_id"] = supplierId,
                    ["product_id"] = pid,
                    ["position"] = position,
                    ["created_at"] = now,
                    ["updated_at"] = now
                }, conn, tx);
            }
        }
    }

    public async Task<List<T>> SortAsync<T>(
        long supplierId,
        IEnumerable<T> lines,
        Func<T, long> productIdSelector,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        var list = lines.ToList();
        if (list.Count <= 1 || supplierId <= 0) return list;

        var orderedIds = await ProductIdsAsync(supplierId, conn, tx);
        if (orderedIds.Count == 0) return list;

        var rank = new Dictionary<long, int>();
        for (int i = 0; i < orderedIds.Count; i++)
        {
            rank[orderedIds[i]] = i;
        }

        int offset = rank.Count;
        return list
            .Select((line, index) => (line, sortKey: rank.TryGetValue(productIdSelector(line), out var r) ? r : offset + index))
            .OrderBy(pair => pair.sortKey)
            .Select(pair => pair.line)
            .ToList();
    }
}

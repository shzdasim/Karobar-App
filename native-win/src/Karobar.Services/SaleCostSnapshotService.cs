using System.Data;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class SaleCostSnapshotService
{
    private readonly KarobarDb _db;

    public SaleCostSnapshotService(KarobarDb db)
    {
        _db = db;
    }

    public async Task<Dictionary<long, double>> ProductCostsAsync(
        IEnumerable<IDictionary<string, object?>> rows,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        var productIds = rows
            .Select(r => ToLong(r.TryGetValue("product_id", out var pid) ? pid : null))
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var result = new Dictionary<long, double>();
        if (productIds.Length == 0 || !await _db.HasTableAsync("products"))
        {
            return result;
        }

        var products = await _db.QueryListAsync(
            "SELECT id, avg_price, unit_purchase_price FROM products WHERE id IN @ids",
            new { ids = productIds },
            conn,
            tx);

        foreach (var p in products)
        {
            var id = ToLong(p.GetValueOrDefault("id"));
            var cost = ToDouble(p.GetValueOrDefault("avg_price"));
            if (Math.Abs(cost) < 1e-9)
            {
                cost = ToDouble(p.GetValueOrDefault("unit_purchase_price"));
            }
            result[id] = cost;
        }

        return result;
    }

    public List<Dictionary<string, object?>> MatchingRows(
        IEnumerable<Dictionary<string, object?>> rows,
        IDictionary<string, object?> row)
    {
        var targetPid = ToLong(row.TryGetValue("product_id", out var p) ? p : null);
        var targetBatch = ((row.TryGetValue("batch_number", out var bn) ? bn : (row.TryGetValue("batch", out var b) ? b : null))?.ToString() ?? "").Trim();
        var targetExpiry = ((row.TryGetValue("expiry", out var ex) ? ex : (row.TryGetValue("expiry_date", out var ed) ? ed : null))?.ToString() ?? "").Trim();
        if (targetExpiry.Length > 10) targetExpiry = targetExpiry.Substring(0, 10);

        return rows.Where(old =>
        {
            var oldPid = ToLong(old.GetValueOrDefault("product_id"));
            var oldBatch = (old.GetValueOrDefault("batch_number")?.ToString() ?? "").Trim();
            var oldExpiry = (old.GetValueOrDefault("expiry")?.ToString() ?? "").Trim();
            if (oldExpiry.Length > 10) oldExpiry = oldExpiry.Substring(0, 10);

            return oldPid == targetPid &&
                   string.Equals(oldBatch, targetBatch, StringComparison.Ordinal) &&
                   string.Equals(oldExpiry, targetExpiry, StringComparison.Ordinal);
        }).ToList();
    }

    public object? OriginalCost(
        IEnumerable<Dictionary<string, object?>> rows,
        IDictionary<string, object?> row)
    {
        var list = rows.ToList();
        var rowIdStr = (row.TryGetValue("id", out var idObj) ? idObj?.ToString() : "") ?? "";
        var rowPid = ToLong(row.TryGetValue("product_id", out var pObj) ? pObj : null);

        if (!string.IsNullOrEmpty(rowIdStr))
        {
            var matched = list.FirstOrDefault(old =>
                string.Equals(old.GetValueOrDefault("id")?.ToString() ?? "", rowIdStr, StringComparison.Ordinal) &&
                ToLong(old.GetValueOrDefault("product_id")) == rowPid);
            if (matched != null)
            {
                return matched.GetValueOrDefault("cost_price_snapshot");
            }
        }

        var matches = MatchingRows(list, row);
        return UnambiguousCost(matches);
    }

    public object? UnambiguousCost(IEnumerable<Dictionary<string, object?>> rows)
    {
        var list = rows.ToList();
        if (list.Count == 0 || list.Any(r => r.GetValueOrDefault("cost_price_snapshot") == null))
        {
            return null;
        }

        var distinctCosts = list
            .Select(r => r.GetValueOrDefault("cost_price_snapshot")?.ToString() ?? "")
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return distinctCosts.Count == 1 ? list[0].GetValueOrDefault("cost_price_snapshot") : null;
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

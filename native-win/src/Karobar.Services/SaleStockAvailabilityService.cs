using System.Data;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class SaleStockAvailabilityService
{
    private readonly KarobarDb _db;
    private readonly InvoiceCalculationService _calcService;

    public SaleStockAvailabilityService(KarobarDb db)
    {
        _db = db;
        _calcService = new InvoiceCalculationService(db);
    }

    public async Task<bool> CanOverrideExpiredAsync(long? userId)
    {
        if (!userId.HasValue || userId.Value <= 0) return false;
        return await _db.UserCanAsync(userId.Value, "sale-invoice.expired-override");
    }

    public async Task ValidateAsync(
        List<Dictionary<string, object?>> items,
        List<Dictionary<string, object?>>? originalItems = null,
        long? userId = null,
        bool expiredOverride = false,
        IDictionary<string, object?>? overrideCredentials = null,
        IDbConnection? conn = null,
        IDbTransaction? tx = null)
    {
        long? effectiveUserId = userId;
        bool authorizerResolved = false;
        bool? cachedCanOverrideExpired = null;

        async Task<bool> CanCurrentOrAuthorizedUserOverrideExpiredAsync()
        {
            if (!authorizerResolved)
            {
                if (expiredOverride && overrideCredentials != null && overrideCredentials.Count > 0)
                {
                    effectiveUserId = await _calcService.ResolveAuthorizingUserIdAsync(
                        userId,
                        overrideCredentials,
                        "sale-invoice.expired-override");
                }
                authorizerResolved = true;
            }

            cachedCanOverrideExpired ??= await CanOverrideExpiredAsync(effectiveUserId);
            return cachedCanOverrideExpired.Value;
        }

        originalItems ??= new List<Dictionary<string, object?>>();

        var ids = items.Select(i => ToLong(i.GetValueOrDefault("product_id")))
            .Concat(originalItems.Select(o => ToLong(o.GetValueOrDefault("product_id"))))
            .Where(id => id > 0)
            .Distinct()
            .OrderBy(id => id)
            .ToArray();

        if (ids.Length == 0) return;

        var pRows = await _db.QueryListAsync(
            "SELECT * FROM products WHERE id IN @ids ORDER BY id ASC",
            new { ids },
            conn,
            tx);
        var products = pRows.ToDictionary(p => ToLong(p.GetValueOrDefault("id")), p => p);

        var batches = await _db.HasTableAsync("batches")
            ? await _db.QueryListAsync("SELECT * FROM batches WHERE product_id IN @ids ORDER BY id ASC", new { ids }, conn, tx)
            : new List<Dictionary<string, object?>>();

        var restored = new Dictionary<long, int>();
        var batchRestored = new Dictionary<long, int>();
        var unbatchedRestored = new Dictionary<long, int>();

        foreach (var old in originalItems)
        {
            var oldPid = ToLong(old.GetValueOrDefault("product_id"));
            var oldTotalUnits = (int)ToLong(old.GetValueOrDefault("quantity")) + Math.Max(0, (int)ToLong(old.GetValueOrDefault("bonus")));
            restored[oldPid] = restored.GetValueOrDefault(oldPid, 0) + oldTotalUnits;

            var matchedBatch = MatchBatch(batches, old);
            if (matchedBatch != null)
            {
                var bid = ToLong(matchedBatch.GetValueOrDefault("id"));
                batchRestored[bid] = batchRestored.GetValueOrDefault(bid, 0) + oldTotalUnits;
            }
            else
            {
                unbatchedRestored[oldPid] = unbatchedRestored.GetValueOrDefault(oldPid, 0) + oldTotalUnits;
            }
        }

        var requested = new Dictionary<long, int>();
        var batchRequested = new Dictionary<string, int>(StringComparer.Ordinal);
        var unbatchedRequested = new Dictionary<long, int>();
        var seenAllocations = new Dictionary<long, HashSet<string>>();

        for (int index = 0; index < items.Count; index++)
        {
            var row = items[index];
            var pid = ToLong(row.GetValueOrDefault("product_id"));
            if (!products.TryGetValue(pid, out var product))
            {
                throw new InvoiceValidationException("This product is no longer available.", new Dictionary<string, string[]>
                {
                    [$"items.{index}.product_id"] = new[] { "This product is no longer available." }
                });
            }

            var prodName = product.GetValueOrDefault("name")?.ToString() ?? $"Product #{pid}";
            var rowUnits = (int)ToLong(row.GetValueOrDefault("quantity")) + Math.Max(0, (int)ToLong(row.GetValueOrDefault("bonus")));
            requested[pid] = requested.GetValueOrDefault(pid, 0) + rowUnits;

            var available = (int)ToLong(product.GetValueOrDefault("quantity")) + restored.GetValueOrDefault(pid, 0);
            if (requested[pid] > available)
            {
                var msg = $"Insufficient stock for {prodName}. Available: {available} units; requested: {requested[pid]} units.";
                throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                {
                    [$"items.{index}.quantity"] = new[] { msg }
                });
            }

            var batchNo = ((row.GetValueOrDefault("batch_number") ?? row.GetValueOrDefault("batch"))?.ToString() ?? "").Trim();
            var allocationKey = (!string.IsNullOrEmpty(batchNo) && batchNo != "__unbatched__")
                ? $"batch:{batchNo}"
                : "unbatched";

            if (!seenAllocations.TryGetValue(pid, out var allocSet))
            {
                allocSet = new HashSet<string>(StringComparer.Ordinal);
                seenAllocations[pid] = allocSet;
            }

            if (allocSet.Contains(allocationKey))
            {
                if (allocationKey == "unbatched")
                {
                    var msg = $"Unassigned stock for {prodName} can only be added once per invoice.";
                    throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                    {
                        [$"items.{index}.quantity"] = new[] { msg }
                    });
                }
                else
                {
                    var msg = $"Duplicate batch '{batchNo}' for {prodName}. Each batch can only be selected once per invoice.";
                    throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                    {
                        [$"items.{index}.batch_number"] = new[] { msg }
                    });
                }
            }
            allocSet.Add(allocationKey);

            if (!string.IsNullOrEmpty(batchNo) && batchNo != "__unbatched__")
            {
                var batch = MatchBatch(batches, row);
                if (batch == null)
                {
                    var msg = $"The selected batch for {prodName} is unavailable or ambiguous. Select a valid batch and expiry.";
                    throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                    {
                        [$"items.{index}.batch_number"] = new[] { msg }
                    });
                }

                if (IsBatchExpired(batch))
                {
                    var bNo = batch.GetValueOrDefault("batch_number")?.ToString() ?? batchNo;
                    var expDateStr = FormatExpiryDate(batch.GetValueOrDefault("expiry_date"));
                    if (!expiredOverride)
                    {
                        var msg = $"Batch '{bNo}' for '{prodName}' expired on {expDateStr} and cannot be sold without supervisor override.";
                        throw new InvoiceValidationException(msg, statusCode: 422, customPayload: new Dictionary<string, object?>
                        {
                            ["message"] = msg,
                            ["requires_expired_override"] = true,
                            ["product_id"] = pid,
                            ["product_name"] = prodName,
                            ["batch_number"] = bNo,
                            ["expiry_date"] = expDateStr,
                            ["errors"] = new Dictionary<string, string[]>
                            {
                                [$"items.{index}.batch_number"] = new[] { msg }
                            }
                        });
                    }

                    if (!await CanCurrentOrAuthorizedUserOverrideExpiredAsync())
                    {
                        throw new InvoiceValidationException("You do not have permission to sell expired stock.", statusCode: 403, customPayload: new Dictionary<string, object?>
                        {
                            ["message"] = "You do not have permission to sell expired stock.",
                            ["code"] = "EXPIRED_STOCK_UNAUTHORIZED"
                        });
                    }
                }

                var matchingBatches = MatchingBatches(batches, row);
                var bNumber = batch.GetValueOrDefault("batch_number")?.ToString() ?? batchNo;
                var batchGroupKey = $"{pid}:{bNumber}";
                batchRequested[batchGroupKey] = batchRequested.GetValueOrDefault(batchGroupKey, 0) + rowUnits;

                var availableBatchQty = matchingBatches.Sum(b => (int)ToLong(b.GetValueOrDefault("quantity"))) +
                                        matchingBatches.Sum(b => batchRestored.GetValueOrDefault(ToLong(b.GetValueOrDefault("id")), 0));

                if (batchRequested[batchGroupKey] > availableBatchQty)
                {
                    var msg = $"Insufficient stock in batch {bNumber} for {prodName}. Available: {availableBatchQty} units; requested: {batchRequested[batchGroupKey]} units.";
                    throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                    {
                        [$"items.{index}.quantity"] = new[] { msg }
                    });
                }
            }
            else
            {
                var productBatches = batches.Where(b => ToLong(b.GetValueOrDefault("product_id")) == pid).ToList();
                if (productBatches.Count > 0)
                {
                    var totalBatched = productBatches.Sum(b => (int)ToLong(b.GetValueOrDefault("quantity")));
                    var unbatchedAvailable = Math.Max(0, ((int)ToLong(product.GetValueOrDefault("quantity")) + unbatchedRestored.GetValueOrDefault(pid, 0)) - totalBatched);

                    unbatchedRequested[pid] = unbatchedRequested.GetValueOrDefault(pid, 0) + rowUnits;

                    if (unbatchedRequested[pid] > unbatchedAvailable)
                    {
                        bool allBatchesExpired = productBatches.All(IsBatchExpired);
                        if (unbatchedAvailable == 0 && allBatchesExpired)
                        {
                            var earliestExpired = productBatches
                                .OrderBy(b => b.GetValueOrDefault("expiry_date")?.ToString() ?? "")
                                .FirstOrDefault();
                            var expiryStr = earliestExpired != null
                                ? FormatExpiryDate(earliestExpired.GetValueOrDefault("expiry_date"))
                                : "already";

                            if (!expiredOverride)
                            {
                                var msg = $"Stock for '{prodName}' has expired ({expiryStr}) and cannot be sold without supervisor override.";
                                throw new InvoiceValidationException(msg, statusCode: 422, customPayload: new Dictionary<string, object?>
                                {
                                    ["message"] = msg,
                                    ["requires_expired_override"] = true,
                                    ["product_id"] = pid,
                                    ["product_name"] = prodName,
                                    ["expiry_date"] = expiryStr,
                                    ["errors"] = new Dictionary<string, string[]>
                                    {
                                        [$"items.{index}.quantity"] = new[] { msg }
                                    }
                                });
                            }

                            if (!await CanCurrentOrAuthorizedUserOverrideExpiredAsync())
                            {
                                throw new InvoiceValidationException("You do not have permission to sell expired stock.", statusCode: 403, customPayload: new Dictionary<string, object?>
                                {
                                    ["message"] = "You do not have permission to sell expired stock.",
                                    ["code"] = "EXPIRED_STOCK_UNAUTHORIZED"
                                });
                            }
                        }

                        var msgUnbatched = $"Insufficient unbatched stock for {prodName}. Available unbatched: {unbatchedAvailable} units ({totalBatched} units are assigned to batches). Please select a batch.";
                        throw new InvoiceValidationException(msgUnbatched, new Dictionary<string, string[]>
                        {
                            [$"items.{index}.quantity"] = new[] { msgUnbatched }
                        });
                    }
                }
            }
        }
    }

    public List<Dictionary<string, object?>> MatchingBatches(
        IEnumerable<Dictionary<string, object?>> batches,
        IDictionary<string, object?> row)
    {
        var number = ((row.TryGetValue("batch_number", out var bn) ? bn : (row.TryGetValue("batch", out var b) ? b : null))?.ToString() ?? "").Trim();
        if (string.IsNullOrEmpty(number) || number == "__unbatched__")
        {
            return new List<Dictionary<string, object?>>();
        }

        var expiry = ((row.TryGetValue("expiry", out var ex) ? ex : (row.TryGetValue("expiry_date", out var ed) ? ed : null))?.ToString() ?? "").Trim();
        if (expiry.Length > 10) expiry = expiry.Substring(0, 10);

        var targetPid = ToLong(row.TryGetValue("product_id", out var p) ? p : null);

        var matches = batches.Where(batch =>
        {
            var bPid = ToLong(batch.GetValueOrDefault("product_id"));
            var bNum = (batch.GetValueOrDefault("batch_number")?.ToString() ?? "").Trim();
            var bExp = (batch.GetValueOrDefault("expiry_date")?.ToString() ?? "").Trim();
            if (bExp.Length > 10) bExp = bExp.Substring(0, 10);

            return bPid == targetPid &&
                   string.Equals(bNum, number, StringComparison.Ordinal) &&
                   (string.IsNullOrEmpty(expiry) || string.Equals(bExp, expiry, StringComparison.Ordinal));
        }).ToList();

        return matches
            .OrderByDescending(b =>
            {
                var q = (int)ToLong(b.GetValueOrDefault("quantity"));
                var id = (int)ToLong(b.GetValueOrDefault("id"));
                return (q > 0 ? 1000000 : 0) + q - id;
            })
            .ToList();
    }

    public Dictionary<string, object?>? MatchBatch(
        IEnumerable<Dictionary<string, object?>> batches,
        IDictionary<string, object?> row)
    {
        var number = ((row.TryGetValue("batch_number", out var bn) ? bn : (row.TryGetValue("batch", out var b) ? b : null))?.ToString() ?? "").Trim();
        if (string.IsNullOrEmpty(number) || number == "__unbatched__")
        {
            return null;
        }

        var list = batches.ToList();
        var targetPid = ToLong(row.TryGetValue("product_id", out var p) ? p : null);

        if (row.TryGetValue("batch_id", out var bidObj) && bidObj != null && ToLong(bidObj) > 0)
        {
            var bid = ToLong(bidObj);
            var byId = list.FirstOrDefault(x => ToLong(x.GetValueOrDefault("id")) == bid && ToLong(x.GetValueOrDefault("product_id")) == targetPid);
            if (byId != null) return byId;
        }

        var matches = MatchingBatches(list, row);
        if (matches.Count == 0) return null;
        if (matches.Count == 1) return matches[0];

        if (row.TryGetValue("storage_location_id", out var locObj) && locObj != null && ToLong(locObj) > 0)
        {
            var locId = ToLong(locObj);
            var byLoc = matches.FirstOrDefault(x => ToLong(x.GetValueOrDefault("storage_location_id")) == locId);
            if (byLoc != null) return byLoc;
        }

        return matches[0];
    }

    public static bool IsBatchExpired(Dictionary<string, object?> batch)
    {
        var expStr = (batch.GetValueOrDefault("expiry_date")?.ToString() ?? "").Trim();
        if (string.IsNullOrEmpty(expStr)) return false;
        if (DateTime.TryParse(expStr, out var expDt))
        {
            return expDt.Date < DateTime.UtcNow.Date;
        }
        return false;
    }

    private static string FormatExpiryDate(object? expiryObj)
    {
        var s = (expiryObj?.ToString() ?? "").Trim();
        if (s.Length > 10) s = s.Substring(0, 10);
        return s;
    }

    private static long ToLong(object? val)
    {
        if (val == null) return 0;
        if (val is long l) return l;
        if (val is int i) return i;
        return long.TryParse(val.ToString(), out var p) ? p : 0;
    }
}

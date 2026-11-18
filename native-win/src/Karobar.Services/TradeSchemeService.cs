using System.Data;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class TradeSchemeService
{
    private readonly KarobarDb _db;

    public TradeSchemeService(KarobarDb db)
    {
        _db = db;
    }

    public async Task<List<Dictionary<string, object?>>> GetActiveSchemesAsync(
        string? saleType = null,
        long? customerId = null,
        string? date = null)
    {
        if (!await _db.HasTableAsync("trade_schemes"))
        {
            return new List<Dictionary<string, object?>>();
        }

        if (customerId.HasValue && customerId.Value > 0 && await _db.HasTableAsync("customers"))
        {
            var customer = await _db.QuerySingleOrDefaultAsync(
                "SELECT * FROM customers WHERE id = @id",
                new { id = customerId.Value });
            if (customer != null && customer.TryGetValue("eligible_for_schemes", out var eligObj) && eligObj != null)
            {
                var isElig = eligObj is true or 1 or 1L || (eligObj is string s && (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)));
                if (!isElig)
                {
                    return new List<Dictionary<string, object?>>();
                }
            }
        }

        var checkDate = string.IsNullOrWhiteSpace(date)
            ? DateTime.UtcNow.ToString("yyyy-MM-dd")
            : date.Trim().Substring(0, Math.Min(10, date.Trim().Length));

        var sql = @"
            SELECT * FROM trade_schemes
            WHERE is_active = 1
              AND (start_date IS NULL OR start_date = '' OR date(start_date) <= date(@checkDate))
              AND (end_date IS NULL OR end_date = '' OR date(end_date) >= date(@checkDate))";

        if (!string.IsNullOrWhiteSpace(saleType) && !saleType.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            sql += " AND (sale_type = 'all' OR sale_type IS NULL OR sale_type = '' OR LOWER(sale_type) = LOWER(@saleType))";
        }

        if (customerId.HasValue && customerId.Value > 0)
        {
            sql += " AND (customer_id IS NULL OR customer_id = @customerId)";
        }
        else
        {
            sql += " AND customer_id IS NULL";
        }

        sql += " ORDER BY priority DESC, id ASC";

        var schemes = await _db.QueryListAsync(sql, new { checkDate, saleType, customerId });
        if (schemes.Count == 0) return schemes;

        await EnrichSchemesAsync(schemes);
        return schemes;
    }

    public async Task EnrichSchemesAsync(List<Dictionary<string, object?>> schemes)
    {
        if (schemes.Count == 0) return;

        var schemeIds = schemes
            .Select(s => ToLong(s.GetValueOrDefault("id")))
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var slabsByScheme = new Dictionary<long, List<Dictionary<string, object?>>>();
        if (schemeIds.Length > 0 && await _db.HasTableAsync("trade_scheme_slabs"))
        {
            var slabs = await _db.QueryListAsync(
                "SELECT * FROM trade_scheme_slabs WHERE trade_scheme_id IN @ids ORDER BY min_qty ASC, id ASC",
                new { ids = schemeIds });

            var bonusProdIds = slabs
                .Select(sl => ToLong(sl.GetValueOrDefault("bonus_product_id")))
                .Where(pid => pid > 0)
                .Distinct()
                .ToArray();

            var bonusProds = new Dictionary<long, Dictionary<string, object?>>();
            if (bonusProdIds.Length > 0 && await _db.HasTableAsync("products"))
            {
                var pRows = await _db.QueryListAsync(
                    "SELECT id, name, product_code, pack_size, unit_sale_price, pack_sale_price, whole_sale_unit_price, whole_sale_pack_price, quantity FROM products WHERE id IN @ids",
                    new { ids = bonusProdIds });
                foreach (var pr in pRows)
                {
                    bonusProds[ToLong(pr.GetValueOrDefault("id"))] = pr;
                }
            }

            foreach (var sl in slabs)
            {
                var sid = ToLong(sl.GetValueOrDefault("trade_scheme_id"));
                var bpid = ToLong(sl.GetValueOrDefault("bonus_product_id"));
                sl["bonus_product"] = bpid > 0 && bonusProds.TryGetValue(bpid, out var bp) ? bp : null;
                if (!slabsByScheme.TryGetValue(sid, out var list))
                {
                    list = new List<Dictionary<string, object?>>();
                    slabsByScheme[sid] = list;
                }
                list.Add(sl);
            }
        }

        foreach (var scheme in schemes)
        {
            var sid = ToLong(scheme.GetValueOrDefault("id"));
            scheme["slabs"] = slabsByScheme.TryGetValue(sid, out var slList)
                ? slList
                : new List<Dictionary<string, object?>>();

            var targetType = scheme.GetValueOrDefault("target_type")?.ToString()?.ToLowerInvariant() ?? "";
            var targetId = ToLong(scheme.GetValueOrDefault("target_id"));

            scheme["product"] = null;
            scheme["brand"] = null;
            scheme["category"] = null;
            scheme["customer"] = null;

            if (targetId > 0)
            {
                if (targetType == "product" && await _db.HasTableAsync("products"))
                {
                    scheme["product"] = await _db.QuerySingleOrDefaultAsync(
                        "SELECT id, name, product_code, brand_id, category_id, pack_size FROM products WHERE id = @id",
                        new { id = targetId });
                }
                else if (targetType == "brand" && await _db.HasTableAsync("brands"))
                {
                    scheme["brand"] = await _db.QuerySingleOrDefaultAsync(
                        "SELECT id, name FROM brands WHERE id = @id",
                        new { id = targetId });
                }
                else if (targetType == "category" && await _db.HasTableAsync("categories"))
                {
                    scheme["category"] = await _db.QuerySingleOrDefaultAsync(
                        "SELECT id, name FROM categories WHERE id = @id",
                        new { id = targetId });
                }
            }

            var custId = ToLong(scheme.GetValueOrDefault("customer_id"));
            if (custId > 0 && await _db.HasTableAsync("customers"))
            {
                scheme["customer"] = await _db.QuerySingleOrDefaultAsync(
                    "SELECT id, name, phone FROM customers WHERE id = @id",
                    new { id = custId });
            }
        }
    }

    public async Task<Dictionary<string, object?>> EvaluateBonusAsync(
        IDictionary<string, object?> item,
        List<Dictionary<string, object?>> activeSchemes,
        string? saleType = null)
    {
        var productId = ToLong(item.TryGetValue("product_id", out var pidObj) ? pidObj : null);
        if (productId <= 0 || activeSchemes == null || activeSchemes.Count == 0)
        {
            return NoMatch();
        }

        var packSize = Math.Max(1, (int)ToLong(item.TryGetValue("pack_size", out var psObj) ? psObj : 1));
        var packQty = Math.Max(0.0, ToDouble(item.TryGetValue("pack_quantity", out var pqObj) ? pqObj : 0));
        var unitQty = Math.Max(0, (int)Math.Round(ToDouble(item.TryGetValue("unit_quantity", out var uqObj) ? uqObj : 0)));
        var rawQty = Math.Max(0, (int)Math.Round(ToDouble(item.TryGetValue("quantity", out var rqObj) ? rqObj : 0)));

        long? brandId = item.TryGetValue("brand_id", out var bObj) && bObj != null ? ToLong(bObj) : null;
        long? categoryId = item.TryGetValue("category_id", out var cObj) && cObj != null ? ToLong(cObj) : null;

        if ((!brandId.HasValue || !categoryId.HasValue || !item.ContainsKey("pack_size")) && await _db.HasTableAsync("products"))
        {
            var prod = await _db.QuerySingleOrDefaultAsync(
                "SELECT id, brand_id, category_id, pack_size FROM products WHERE id = @id",
                new { id = productId });
            if (prod != null)
            {
                brandId ??= prod.GetValueOrDefault("brand_id") != null ? ToLong(prod["brand_id"]) : null;
                categoryId ??= prod.GetValueOrDefault("category_id") != null ? ToLong(prod["category_id"]) : null;
                if (!item.ContainsKey("pack_size") && prod.GetValueOrDefault("pack_size") != null)
                {
                    packSize = Math.Max(1, (int)ToLong(prod["pack_size"]));
                }
            }
        }

        int totalUnits;
        if (rawQty > 0)
        {
            totalUnits = rawQty;
        }
        else if (unitQty > 0 && (packQty <= 0 || (int)Math.Round(packQty * packSize) == unitQty))
        {
            totalUnits = unitQty;
        }
        else if (packQty > 0 && unitQty <= 0)
        {
            totalUnits = (int)Math.Round(packQty * packSize);
        }
        else
        {
            totalUnits = (int)Math.Round(packQty * packSize) + unitQty;
        }

        double purchasedPacks = (packQty > 0 && (int)Math.Round(packQty * packSize) == totalUnits)
            ? packQty
            : (packSize > 0 ? ((double)totalUnits / packSize) : 0.0);

        if (totalUnits <= 0 && packQty <= 0)
        {
            return NoMatch();
        }

        Dictionary<string, object?>? matchedScheme = activeSchemes.FirstOrDefault(s =>
            string.Equals(s.GetValueOrDefault("target_type")?.ToString(), "product", StringComparison.OrdinalIgnoreCase) &&
            ToLong(s.GetValueOrDefault("target_id")) == productId);

        if (matchedScheme == null && brandId.HasValue && brandId.Value > 0)
        {
            matchedScheme = activeSchemes.FirstOrDefault(s =>
                string.Equals(s.GetValueOrDefault("target_type")?.ToString(), "brand", StringComparison.OrdinalIgnoreCase) &&
                ToLong(s.GetValueOrDefault("target_id")) == brandId.Value);
        }

        if (matchedScheme == null && categoryId.HasValue && categoryId.Value > 0)
        {
            matchedScheme = activeSchemes.FirstOrDefault(s =>
                string.Equals(s.GetValueOrDefault("target_type")?.ToString(), "category", StringComparison.OrdinalIgnoreCase) &&
                ToLong(s.GetValueOrDefault("target_id")) == categoryId.Value);
        }

        if (matchedScheme == null) return NoMatch();

        var slabs = matchedScheme.GetValueOrDefault("slabs") as List<Dictionary<string, object?>>;
        if (slabs == null || slabs.Count == 0) return NoMatch();

        var orderedSlabs = slabs.OrderBy(sl => ToDouble(sl.GetValueOrDefault("min_qty"))).ToList();
        var bonusType = matchedScheme.GetValueOrDefault("bonus_type")?.ToString();
        if (string.IsNullOrWhiteSpace(bonusType)) bonusType = "pack";

        var schemeType = matchedScheme.GetValueOrDefault("scheme_type")?.ToString() ?? "ratio";

        double packBonus = 0.0;
        int unitBonus = 0;
        int bonusUnits = 0;
        Dictionary<string, object?>? matchedSlab = null;

        if (string.Equals(schemeType, "ratio", StringComparison.OrdinalIgnoreCase))
        {
            var firstSlab = orderedSlabs[0];
            var minQ = ToDouble(firstSlab.GetValueOrDefault("min_qty"));
            var bQ = ToDouble(firstSlab.GetValueOrDefault("bonus_qty"));
            var bonusProd = firstSlab.GetValueOrDefault("bonus_product") as Dictionary<string, object?>;
            var bonusProdPackSize = Math.Max(1, bonusProd != null && bonusProd.GetValueOrDefault("pack_size") != null
                ? (int)ToLong(bonusProd["pack_size"])
                : packSize);

            if (minQ > 0 && bQ > 0)
            {
                if (string.Equals(bonusType, "pack", StringComparison.OrdinalIgnoreCase))
                {
                    if (purchasedPacks + 1e-9 >= minQ)
                    {
                        var multiplier = Math.Floor((purchasedPacks + 1e-9) / minQ);
                        if (multiplier > 0)
                        {
                            packBonus = Math.Round(multiplier * bQ, 2);
                            unitBonus = (int)Math.Round(packBonus * bonusProdPackSize);
                            bonusUnits = unitBonus;
                            matchedSlab = firstSlab;
                        }
                    }
                }
                else
                {
                    if (totalUnits >= minQ)
                    {
                        var multiplier = Math.Floor(totalUnits / minQ);
                        if (multiplier > 0)
                        {
                            unitBonus = (int)Math.Round(multiplier * bQ);
                            packBonus = bonusProdPackSize > 0 ? Math.Round((double)unitBonus / bonusProdPackSize, 2) : 0.0;
                            bonusUnits = unitBonus;
                            matchedSlab = firstSlab;
                        }
                    }
                }
            }
        }
        else
        {
            var wholePurchasedPacks = Math.Floor(purchasedPacks + 1e-9);
            foreach (var slab in orderedSlabs)
            {
                var minQ = ToDouble(slab.GetValueOrDefault("min_qty"));
                var maxQObj = slab.GetValueOrDefault("max_qty");
                double? maxQ = maxQObj != null && !string.IsNullOrWhiteSpace(maxQObj.ToString()) ? ToDouble(maxQObj) : null;
                var awarded = ToDouble(slab.GetValueOrDefault("bonus_qty"));
                var bonusProd = slab.GetValueOrDefault("bonus_product") as Dictionary<string, object?>;
                var bonusProdPackSize = Math.Max(1, bonusProd != null && bonusProd.GetValueOrDefault("pack_size") != null
                    ? (int)ToLong(bonusProd["pack_size"])
                    : packSize);

                if (string.Equals(bonusType, "pack", StringComparison.OrdinalIgnoreCase))
                {
                    if (purchasedPacks + 1e-9 >= minQ && (!maxQ.HasValue || wholePurchasedPacks <= maxQ.Value))
                    {
                        packBonus = Math.Round(awarded, 2);
                        unitBonus = (int)Math.Round(packBonus * bonusProdPackSize);
                        bonusUnits = unitBonus;
                        matchedSlab = slab;
                        break;
                    }
                }
                else
                {
                    if (totalUnits >= minQ && (!maxQ.HasValue || totalUnits <= maxQ.Value))
                    {
                        unitBonus = (int)Math.Round(awarded);
                        packBonus = bonusProdPackSize > 0 ? Math.Round((double)unitBonus / bonusProdPackSize, 2) : 0.0;
                        bonusUnits = unitBonus;
                        matchedSlab = slab;
                        break;
                    }
                }
            }
        }

        if ((packBonus <= 0 && unitBonus <= 0 && bonusUnits <= 0) || matchedSlab == null)
        {
            return NoMatch();
        }

        var bonusProductId = ToLong(matchedSlab.GetValueOrDefault("bonus_product_id"));
        if (bonusProductId <= 0) bonusProductId = productId;

        var schemeName = matchedScheme.GetValueOrDefault("name")?.ToString() ?? "";

        return new Dictionary<string, object?>
        {
            ["matched"] = true,
            ["scheme_id"] = matchedScheme.GetValueOrDefault("id"),
            ["scheme_name"] = schemeName,
            ["scheme_type"] = schemeType,
            ["bonus_type"] = bonusType,
            ["pack_bonus"] = packBonus,
            ["unit_bonus"] = unitBonus,
            ["bonus_units"] = bonusUnits,
            ["bonus_product_id"] = bonusProductId,
            ["message"] = $"Scheme '{schemeName}' applied"
        };
    }

    private static Dictionary<string, object?> NoMatch()
    {
        return new Dictionary<string, object?>
        {
            ["matched"] = false,
            ["scheme_id"] = null,
            ["scheme_name"] = null,
            ["scheme_type"] = null,
            ["bonus_type"] = "pack",
            ["pack_bonus"] = 0,
            ["unit_bonus"] = 0,
            ["bonus_units"] = 0,
            ["bonus_product_id"] = null,
            ["message"] = null
        };
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

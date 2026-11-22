using System.Data;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class InvoiceValidationException : Exception
{
    public int StatusCode { get; }
    public Dictionary<string, string[]>? Errors { get; }
    public Dictionary<string, object?> ResponsePayload { get; }

    public InvoiceValidationException(
        string message,
        Dictionary<string, string[]>? errors = null,
        int statusCode = 422,
        Dictionary<string, object?>? customPayload = null)
        : base(message)
    {
        StatusCode = statusCode;
        Errors = errors;
        if (customPayload != null)
        {
            ResponsePayload = customPayload;
        }
        else
        {
            ResponsePayload = new Dictionary<string, object?>
            {
                ["message"] = message
            };
            if (errors != null)
            {
                ResponsePayload["errors"] = errors;
            }
        }
    }
}

public class InvoiceCalculationService
{
    private readonly KarobarDb _db;
    private readonly TradeSchemeService _tradeSchemeService;

    public InvoiceCalculationService(KarobarDb db)
    {
        _db = db;
        _tradeSchemeService = new TradeSchemeService(db);
    }

    public async Task<long?> ResolveAuthorizingUserIdAsync(
        long? currentUserId,
        IDictionary<string, object?>? data = null,
        string? permission = null)
    {
        var supervisorPassword = data != null && data.TryGetValue("supervisor_password", out var sp) ? sp?.ToString() ?? "" : "";
        var supervisorEmail = (data != null && data.TryGetValue("supervisor_email", out var se) ? se?.ToString() ?? "" : "").Trim();

        if (!string.IsNullOrEmpty(supervisorPassword) && await _db.HasTableAsync("users"))
        {
            if (!string.IsNullOrEmpty(supervisorEmail))
            {
                var sup = await _db.QuerySingleOrDefaultAsync(
                    "SELECT * FROM users WHERE email = @email LIMIT 1",
                    new { email = supervisorEmail });
                if (sup != null)
                {
                    var status = sup.GetValueOrDefault("status")?.ToString() ?? "active";
                    var hash = sup.GetValueOrDefault("password")?.ToString() ?? "";
                    if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) && VerifyPasswordHash(supervisorPassword, hash))
                    {
                        return ToLong(sup.GetValueOrDefault("id"));
                    }
                }
            }
            else
            {
                var users = await _db.QueryListAsync("SELECT * FROM users");
                foreach (var u in users)
                {
                    var status = u.GetValueOrDefault("status")?.ToString() ?? "active";
                    if (!string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)) continue;

                    var uid = ToLong(u.GetValueOrDefault("id"));
                    bool isEligible = await _db.UserHasRoleAsync(uid, KarobarDb.AdminRoleNames)
                                      || (!string.IsNullOrEmpty(permission) && await _db.UserCanAsync(uid, permission));
                    if (!isEligible) continue;

                    var hash = u.GetValueOrDefault("password")?.ToString() ?? "";
                    if (VerifyPasswordHash(supervisorPassword, hash))
                    {
                        return uid;
                    }
                }
            }
        }

        return currentUserId;
    }

    public static bool VerifyPasswordHash(string plainPassword, string storedHash)
    {
        if (string.IsNullOrEmpty(plainPassword) || string.IsNullOrEmpty(storedHash)) return false;
        try
        {
            var normalizedHash = storedHash.StartsWith("$2y$", StringComparison.Ordinal)
                ? "$2a$" + storedHash.Substring(4)
                : storedHash;
            return BCrypt.Net.BCrypt.Verify(plainPassword, normalizedHash);
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CanOverrideDiscountAsync(long? userId)
    {
        if (!userId.HasValue || userId.Value <= 0) return false;
        return await _db.UserCanAsync(userId.Value, "sale-invoice.discount-override");
    }

    public async Task<bool> CanOverrideCreditAsync(long? userId)
    {
        if (!userId.HasValue || userId.Value <= 0) return false;
        return await _db.UserCanAsync(userId.Value, "sale-invoice.credit-override");
    }

    public async Task<Dictionary<string, object?>> CalculateAndValidateSaleAsync(
        IDictionary<string, object?> data,
        List<Dictionary<string, object?>> items,
        long? userId = null,
        bool discountOverride = false)
    {
        if (items == null || items.Count == 0)
        {
            throw new InvoiceValidationException("At least one item is required.", new Dictionary<string, string[]>
            {
                ["items"] = new[] { "At least one item is required." }
            });
        }

        var authorizingUserId = await ResolveAuthorizingUserIdAsync(userId, data, "sale-invoice.discount-override");
        var canOverrideDisc = await CanOverrideDiscountAsync(authorizingUserId);

        var productIds = items
            .Select(i => ToLong(i.GetValueOrDefault("product_id")))
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var products = new Dictionary<long, Dictionary<string, object?>>();
        if (productIds.Length > 0 && await _db.HasTableAsync("products"))
        {
            var pRows = await _db.QueryListAsync("SELECT * FROM products WHERE id IN @ids", new { ids = productIds });
            foreach (var pr in pRows)
            {
                products[ToLong(pr.GetValueOrDefault("id"))] = pr;
            }
        }

        var calculatedItems = new List<Dictionary<string, object?>>();
        double grossSum = 0.0;
        double itemDiscSum = 0.0;
        var invoiceSaleType = data.TryGetValue("sale_type", out var stObj) && stObj != null && !string.IsNullOrWhiteSpace(stObj.ToString())
            ? stObj.ToString()!
            : "retail";

        var activeSchemes = new List<Dictionary<string, object?>>();
        try
        {
            if (await _db.HasTableAsync("trade_schemes"))
            {
                long? customerId = data.TryGetValue("customer_id", out var cidObj) && cidObj != null && ToLong(cidObj) > 0
                    ? ToLong(cidObj)
                    : null;
                var saleDate = data.TryGetValue("date", out var dObj) && dObj != null && !string.IsNullOrWhiteSpace(dObj.ToString())
                    ? dObj.ToString()!
                    : DateTime.UtcNow.ToString("yyyy-MM-dd");
                activeSchemes = await _tradeSchemeService.GetActiveSchemesAsync(invoiceSaleType, customerId, saleDate);
            }
        }
        catch
        {
            activeSchemes = new List<Dictionary<string, object?>>();
        }

        for (int idx = 0; idx < items.Count; idx++)
        {
            var raw = new Dictionary<string, object?>(items[idx], StringComparer.OrdinalIgnoreCase);
            var productId = ToLong(raw.GetValueOrDefault("product_id"));
            if (!products.TryGetValue(productId, out var product))
            {
                throw new InvoiceValidationException($"Product #{productId} not found.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.product_id"] = new[] { $"Product #{productId} not found." }
                });
            }

            var itemSaleType = raw.TryGetValue("sale_type", out var istObj) && istObj != null && !string.IsNullOrWhiteSpace(istObj.ToString())
                ? istObj.ToString()!
                : invoiceSaleType;

            var prodPackSize = Math.Max(1, (int)ToLong(product.GetValueOrDefault("pack_size") ?? 1));
            var packSize = Math.Max(1, raw.TryGetValue("pack_size", out var psObj) && psObj != null && ToLong(psObj) > 0
                ? (int)ToLong(psObj)
                : prodPackSize);

            var packQty = Math.Max(0.0, ToDouble(raw.GetValueOrDefault("pack_quantity")));
            var unitQty = Math.Max(0, (int)Math.Round(ToDouble(raw.GetValueOrDefault("unit_quantity"))));
            var packBonus = Math.Max(0.0, ToDouble(raw.GetValueOrDefault("pack_bonus")));
            var unitBonus = Math.Max(0, (int)Math.Round(ToDouble(raw.GetValueOrDefault("unit_bonus"))));
            var bonusQty = Math.Max(0, (int)Math.Round(ToDouble(raw.GetValueOrDefault("bonus"))));

            if (unitBonus > 0 && (bonusQty < 1 || bonusQty == unitBonus))
            {
                bonusQty = unitBonus;
                packBonus = Math.Round((double)bonusQty / packSize, 2);
            }
            else if (packBonus > 0 && (bonusQty < 1 || bonusQty == (int)Math.Round(packBonus * packSize)))
            {
                bonusQty = (int)Math.Round(packBonus * packSize);
                unitBonus = bonusQty;
                packBonus = Math.Round((double)bonusQty / packSize, 2);
            }
            else if (bonusQty > 0)
            {
                unitBonus = bonusQty;
                packBonus = Math.Round((double)bonusQty / packSize, 2);
            }

            var qty = Math.Max(0, (int)Math.Round(ToDouble(raw.GetValueOrDefault("quantity"))));
            if (string.Equals(itemSaleType, "wholesale", StringComparison.OrdinalIgnoreCase))
            {
                if (unitQty > 0 && (qty < 1 || qty == unitQty))
                {
                    qty = unitQty;
                    packQty = Math.Round((double)qty / packSize, 2);
                }
                else if (packQty > 0 && (qty < 1 || qty == (int)Math.Round(packQty * packSize)))
                {
                    qty = (int)Math.Round(packQty * packSize);
                    unitQty = qty;
                    packQty = Math.Round((double)qty / packSize, 2);
                }
                else if (qty > 0)
                {
                    unitQty = qty;
                    packQty = Math.Round((double)qty / packSize, 2);
                }
            }
            else
            {
                if (qty < 1 && unitQty > 0)
                {
                    qty = unitQty;
                }
                else if (qty < 1 && packQty > 0)
                {
                    qty = (int)Math.Round(packQty * packSize);
                }
            }

            if (qty < 1 && bonusQty < 1)
            {
                throw new InvoiceValidationException("Quantity must be at least 1.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.quantity"] = new[] { "Quantity must be at least 1." }
                });
            }

            bool isCustomBonus = IsTruthy(raw.GetValueOrDefault("is_custom_bonus"));
            if (bonusQty == 0 && !isCustomBonus &&
                !raw.ContainsKey("bonus") &&
                !raw.ContainsKey("unit_bonus") &&
                !raw.ContainsKey("pack_bonus") &&
                activeSchemes.Count > 0)
            {
                var schemeEval = await _tradeSchemeService.EvaluateBonusAsync(new Dictionary<string, object?>
                {
                    ["product_id"] = productId,
                    ["brand_id"] = product.GetValueOrDefault("brand_id"),
                    ["category_id"] = product.GetValueOrDefault("category_id"),
                    ["pack_size"] = packSize,
                    ["pack_quantity"] = packQty,
                    ["unit_quantity"] = unitQty,
                    ["quantity"] = qty,
                    ["sale_type"] = itemSaleType
                }, activeSchemes, itemSaleType);

                if (IsTruthy(schemeEval.GetValueOrDefault("matched")) &&
                    ToLong(schemeEval.GetValueOrDefault("bonus_units")) > 0 &&
                    ToLong(schemeEval.GetValueOrDefault("bonus_product_id") ?? productId) == productId)
                {
                    bonusQty = (int)ToLong(schemeEval.GetValueOrDefault("bonus_units"));
                    unitBonus = (int)ToLong(schemeEval.GetValueOrDefault("unit_bonus") ?? bonusQty);
                    packBonus = ToDouble(schemeEval.GetValueOrDefault("pack_bonus") ?? Math.Round((double)bonusQty / packSize, 2));
                    raw["applied_scheme_id"] = schemeEval.GetValueOrDefault("scheme_id");
                    raw["applied_scheme_name"] = schemeEval.GetValueOrDefault("scheme_name");
                }
            }

            var price = ToDouble(raw.GetValueOrDefault("price"));
            var hasPackPrice = raw.TryGetValue("pack_price", out var ppObj) && ppObj != null && !string.IsNullOrWhiteSpace(ppObj.ToString());
            var packPrice = hasPackPrice ? ToDouble(ppObj) : Math.Round(price * packSize, 2);

            if (price <= 0 && packPrice > 0 && packSize > 0)
            {
                price = Math.Round(packPrice / packSize, 4);
            }
            if (price < 0)
            {
                throw new InvoiceValidationException("Price cannot be negative.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.price"] = new[] { "Price cannot be negative." }
                });
            }

            var discPct = ToDouble(raw.GetValueOrDefault("item_discount_percentage"));
            if (discPct < 0)
            {
                throw new InvoiceValidationException("Item discount percentage cannot be negative.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.item_discount_percentage"] = new[] { "Item discount percentage cannot be negative." }
                });
            }
            if (discPct > 100)
            {
                throw new InvoiceValidationException("Item discount percentage cannot exceed 100%.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.item_discount_percentage"] = new[] { "Item discount percentage cannot exceed 100%." }
                });
            }

            double? maxDiscount = product.TryGetValue("max_discount", out var mdObj) && mdObj != null && !string.IsNullOrWhiteSpace(mdObj.ToString())
                ? ToDouble(mdObj)
                : null;
            var prodName = product.GetValueOrDefault("name")?.ToString() ?? $"Product #{productId}";

            double catalogPrice;
            if (string.Equals(itemSaleType, "retail", StringComparison.OrdinalIgnoreCase))
            {
                catalogPrice = ToDouble(product.GetValueOrDefault("unit_sale_price"));
            }
            else
            {
                var wUnit = ToDouble(product.GetValueOrDefault("whole_sale_unit_price"));
                var wPack = ToDouble(product.GetValueOrDefault("whole_sale_pack_price"));
                catalogPrice = wUnit > 0 ? wUnit : (wPack > 0 && packSize > 0 ? wPack / packSize : 0.0);
            }

            var effectiveItemDiscPct = discPct;
            if (!canOverrideDisc && maxDiscount.HasValue && maxDiscount.Value > 0 && catalogPrice > 0 && price < catalogPrice)
            {
                var effectivePriceAfterItemDisc = price * (1.0 - (discPct / 100.0));
                effectiveItemDiscPct = Math.Max(discPct, Math.Round(((catalogPrice - effectivePriceAfterItemDisc) / catalogPrice) * 100.0, 2));
            }

            if (maxDiscount.HasValue && maxDiscount.Value > 0 && effectiveItemDiscPct > maxDiscount.Value)
            {
                if (!discountOverride)
                {
                    var msg = $"Item discount of {effectiveItemDiscPct}% on product '{prodName}' exceeds maximum allowed limit of {maxDiscount.Value}%. Requires supervisor override.";
                    throw new InvoiceValidationException(msg, statusCode: 422, customPayload: new Dictionary<string, object?>
                    {
                        ["message"] = msg,
                        ["requires_discount_override"] = true,
                        ["product_id"] = productId,
                        ["product_name"] = prodName,
                        ["max_discount"] = maxDiscount.Value,
                        ["requested_discount"] = effectiveItemDiscPct,
                        ["item_index"] = idx,
                        ["errors"] = new Dictionary<string, string[]>
                        {
                            [$"items.{idx}.item_discount_percentage"] = new[]
                            {
                                $"Item discount of {effectiveItemDiscPct}% exceeds maximum allowed limit of {maxDiscount.Value}% for '{prodName}'."
                            }
                        }
                    });
                }

                if (!canOverrideDisc)
                {
                    throw new InvoiceValidationException(
                        "You do not have permission to override maximum discount limits.",
                        new Dictionary<string, string[]>
                        {
                            ["discount_override"] = new[] { "User does not have permission to authorize discount overrides." }
                        },
                        statusCode: 403);
                }
            }

            var lineGross = Math.Round(qty * price, 2);
            var lineDiscAmt = Math.Round((lineGross * discPct) / 100.0, 2);
            var lineSubTotal = Math.Round(lineGross - lineDiscAmt, 2);

            var avgCost = ToDouble(product.GetValueOrDefault("avg_price"));
            var unitPurch = ToDouble(product.GetValueOrDefault("unit_purchase_price"));
            var packPurch = ToDouble(product.GetValueOrDefault("pack_purchase_price"));
            var unitCost = avgCost > 0
                ? avgCost
                : (unitPurch > 0 ? unitPurch : (packPurch > 0 && packSize > 0 ? packPurch / packSize : 0.0));

            if ((string.Equals(itemSaleType, "wholesale", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(invoiceSaleType, "wholesale", StringComparison.OrdinalIgnoreCase)) && unitCost > 0)
            {
                var effectiveUnitPrice = price * (1.0 - (discPct / 100.0));
                var totalOutUnits = qty + bonusQty;
                var totalLineCost = Math.Round(totalOutUnits * unitCost, 2);
                if (effectiveUnitPrice < unitCost - 0.005 || lineSubTotal < totalLineCost - 0.05)
                {
                    var msg = $"Margin for '{prodName}' cannot be less than 0% (Net sale: {lineSubTotal}, Cost: {totalLineCost} at avg cost {Math.Round(unitCost, 2)}/unit).";
                    throw new InvoiceValidationException(msg, new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.price"] = new[] { msg }
                    });
                }
            }

            grossSum += lineGross;
            itemDiscSum += lineDiscAmt;

            raw["product_id"] = productId;
            raw["pack_size"] = packSize;
            raw["pack_quantity"] = packQty;
            raw["unit_quantity"] = unitQty;
            raw["quantity"] = qty;
            raw["pack_bonus"] = packBonus;
            raw["unit_bonus"] = unitBonus;
            raw["bonus"] = bonusQty;
            raw["pack_price"] = Math.Round(packPrice, 2);
            raw["price"] = price;
            raw["sale_type"] = itemSaleType;
            raw["item_discount_percentage"] = discPct;
            raw["sub_total"] = lineSubTotal;

            calculatedItems.Add(raw);
        }

        grossSum = Math.Round(grossSum, 2);
        itemDiscSum = Math.Round(itemDiscSum, 2);

        double billDiscPct = 0.0;
        double billDiscAmt = 0.0;

        if (HasNonZeroValue(data, "discount_amount", out var reqDiscAmt))
        {
            billDiscAmt = reqDiscAmt;
            if (billDiscAmt < 0)
            {
                throw new InvoiceValidationException("Discount amount cannot be negative.", new Dictionary<string, string[]>
                {
                    ["discount_amount"] = new[] { "Discount amount cannot be negative." }
                });
            }
            billDiscPct = grossSum > 0 ? Math.Round((billDiscAmt / grossSum) * 100.0, 2) : 0.0;
        }
        else if (HasNonZeroValue(data, "discount_percentage", out var reqDiscPct))
        {
            billDiscPct = reqDiscPct;
            if (billDiscPct < 0 || billDiscPct > 100)
            {
                throw new InvoiceValidationException("Discount percentage must be between 0 and 100.", new Dictionary<string, string[]>
                {
                    ["discount_percentage"] = new[] { "Discount percentage must be between 0 and 100." }
                });
            }
            billDiscAmt = Math.Round((grossSum * billDiscPct) / 100.0, 2);
        }

        var maxAllowedBillDisc = Math.Max(0.0, grossSum - itemDiscSum);
        if (billDiscAmt > maxAllowedBillDisc)
        {
            billDiscAmt = maxAllowedBillDisc;
            billDiscPct = grossSum > 0 ? Math.Round((billDiscAmt / grossSum) * 100.0, 2) : 0.0;
        }

        if (billDiscAmt > 0 && grossSum > 0 && !canOverrideDisc)
        {
            for (int idx = 0; idx < calculatedItems.Count; idx++)
            {
                var calcItem = calculatedItems[idx];
                var pid = ToLong(calcItem.GetValueOrDefault("product_id"));
                if (!products.TryGetValue(pid, out var product)) continue;

                double? maxDiscount = product.TryGetValue("max_discount", out var mdObj) && mdObj != null && !string.IsNullOrWhiteSpace(mdObj.ToString())
                    ? ToDouble(mdObj)
                    : null;
                if (maxDiscount.HasValue && maxDiscount.Value > 0)
                {
                    var combinedPct = Math.Round(ToDouble(calcItem.GetValueOrDefault("item_discount_percentage")) + billDiscPct, 2);
                    if (combinedPct > maxDiscount.Value)
                    {
                        var prodName = product.GetValueOrDefault("name")?.ToString() ?? $"Product #{pid}";
                        if (!discountOverride)
                        {
                            var msg = $"Combined discount of {combinedPct}% on product '{prodName}' exceeds maximum allowed limit of {maxDiscount.Value}%. Requires supervisor override.";
                            throw new InvoiceValidationException(msg, statusCode: 422, customPayload: new Dictionary<string, object?>
                            {
                                ["message"] = msg,
                                ["requires_discount_override"] = true,
                                ["product_id"] = pid,
                                ["product_name"] = prodName,
                                ["max_discount"] = maxDiscount.Value,
                                ["requested_discount"] = combinedPct,
                                ["item_index"] = idx,
                                ["errors"] = new Dictionary<string, string[]>
                                {
                                    ["discount_amount"] = new[]
                                    {
                                        $"Combined discount of {combinedPct}% exceeds maximum allowed limit of {maxDiscount.Value}% for '{prodName}'."
                                    }
                                }
                            });
                        }

                        throw new InvoiceValidationException(
                            "You do not have permission to override maximum discount limits.",
                            new Dictionary<string, string[]>
                            {
                                ["discount_override"] = new[] { "User does not have permission to authorize discount overrides." }
                            },
                            statusCode: 403);
                    }
                }
            }
        }

        var taxableBase = Math.Max(0.0, grossSum - itemDiscSum - billDiscAmt);

        double taxPct = 0.0;
        double taxAmt = 0.0;

        if (HasNonZeroValue(data, "tax_amount", out var reqTaxAmt))
        {
            taxAmt = reqTaxAmt;
            if (taxAmt < 0)
            {
                throw new InvoiceValidationException("Tax amount cannot be negative.", new Dictionary<string, string[]>
                {
                    ["tax_amount"] = new[] { "Tax amount cannot be negative." }
                });
            }
            taxPct = taxableBase > 0 ? Math.Round((taxAmt / taxableBase) * 100.0, 2) : 0.0;
        }
        else if (HasNonZeroValue(data, "tax_percentage", out var reqTaxPct))
        {
            taxPct = reqTaxPct;
            if (taxPct < 0 || taxPct > 100)
            {
                throw new InvoiceValidationException("Tax percentage must be between 0 and 100.", new Dictionary<string, string[]>
                {
                    ["tax_percentage"] = new[] { "Tax percentage must be between 0 and 100." }
                });
            }
            taxAmt = Math.Round((taxableBase * taxPct) / 100.0, 2);
        }

        var totalFloat = taxableBase + taxAmt;
        var total = Math.Round(totalFloat, 0, MidpointRounding.AwayFromZero);

        return new Dictionary<string, object?>
        {
            ["items"] = calculatedItems,
            ["gross_amount"] = grossSum,
            ["item_discount"] = itemDiscSum,
            ["discount_percentage"] = billDiscPct,
            ["discount_amount"] = billDiscAmt,
            ["tax_percentage"] = taxPct,
            ["tax_amount"] = taxAmt,
            ["total"] = total
        };
    }

    public Dictionary<string, object?> CalculateAndValidatePurchase(
        IDictionary<string, object?> data,
        List<Dictionary<string, object?>> items)
    {
        if (items == null || items.Count == 0)
        {
            throw new InvoiceValidationException("At least one item is required.", new Dictionary<string, string[]>
            {
                ["items"] = new[] { "At least one item is required." }
            });
        }

        var calculatedItems = new List<Dictionary<string, object?>>();
        double rowsTotal = 0.0;

        for (int idx = 0; idx < items.Count; idx++)
        {
            var raw = new Dictionary<string, object?>(items[idx], StringComparer.OrdinalIgnoreCase);
            var packSize = (int)ToLong(raw.GetValueOrDefault("pack_size"));
            var packQty = ToDouble(raw.GetValueOrDefault("pack_quantity"));
            var unitQty = (int)Math.Round(ToDouble(raw.GetValueOrDefault("unit_quantity")));
            var packPurchasePrice = ToDouble(raw.GetValueOrDefault("pack_purchase_price"));
            var unitPurchasePrice = ToDouble(raw.GetValueOrDefault("unit_purchase_price"));
            var packBonus = ToDouble(raw.GetValueOrDefault("pack_bonus"));
            var unitBonus = (int)Math.Round(ToDouble(raw.GetValueOrDefault("unit_bonus")));
            var discPct = ToDouble(raw.GetValueOrDefault("item_discount_percentage"));

            if (packQty < 0)
            {
                throw new InvoiceValidationException("Pack quantity cannot be negative.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.pack_quantity"] = new[] { "Pack quantity cannot be negative." }
                });
            }
            if (unitQty < 0)
            {
                throw new InvoiceValidationException("Unit quantity cannot be negative.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.unit_quantity"] = new[] { "Unit quantity cannot be negative." }
                });
            }
            if (packBonus < 0)
            {
                throw new InvoiceValidationException("Pack bonus cannot be negative.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.pack_bonus"] = new[] { "Pack bonus cannot be negative." }
                });
            }
            if (unitBonus < 0)
            {
                throw new InvoiceValidationException("Unit bonus cannot be negative.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.unit_bonus"] = new[] { "Unit bonus cannot be negative." }
                });
            }
            if (packPurchasePrice < 0)
            {
                throw new InvoiceValidationException("Pack purchase price cannot be negative.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.pack_purchase_price"] = new[] { "Pack purchase price cannot be negative." }
                });
            }
            if (unitPurchasePrice < 0)
            {
                throw new InvoiceValidationException("Unit purchase price cannot be negative.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.unit_purchase_price"] = new[] { "Unit purchase price cannot be negative." }
                });
            }
            if (discPct < 0 || discPct > 100)
            {
                throw new InvoiceValidationException("Discount percentage must be between 0 and 100.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.item_discount_percentage"] = new[] { "Discount percentage must be between 0 and 100." }
                });
            }

            if (packSize > 0)
            {
                if (unitQty <= 0 && packQty > 0)
                {
                    unitQty = (int)Math.Round(packQty * packSize);
                }
                else if (packQty <= 0 && unitQty > 0)
                {
                    packQty = Math.Round((double)unitQty / packSize, 2);
                }

                if (unitPurchasePrice <= 0 && packPurchasePrice > 0)
                {
                    unitPurchasePrice = Math.Round(packPurchasePrice / packSize, 4);
                }
                else if (packPurchasePrice <= 0 && unitPurchasePrice > 0)
                {
                    packPurchasePrice = Math.Round(unitPurchasePrice * packSize, 2);
                }

                if (unitBonus <= 0 && packBonus > 0)
                {
                    unitBonus = (int)Math.Round(packBonus * packSize);
                }
                else if (packBonus <= 0 && unitBonus > 0)
                {
                    packBonus = Math.Round((double)unitBonus / packSize, 2);
                }
            }

            var totalUnits = unitQty + unitBonus;
            if (raw.TryGetValue("quantity", out var rawQObj) && rawQObj != null && ToLong(rawQObj) > 0 && (int)ToLong(rawQObj) != totalUnits)
            {
                throw new InvoiceValidationException("Stock quantity must equal paid units plus bonus units.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.quantity"] = new[] { "Stock quantity must equal paid units plus bonus units." }
                });
            }

            var subTotal = Math.Round(unitQty * unitPurchasePrice, 2);
            var discountAmount = Math.Round((subTotal * discPct) / 100.0, 2);
            var netTotal = Math.Round(subTotal - discountAmount, 2);
            var avgPrice = totalUnits > 0 ? Math.Round(netTotal / totalUnits, 4) : 0.0;

            rowsTotal += netTotal;

            var unitSalePrice = ToDouble(raw.GetValueOrDefault("unit_sale_price"));
            var packSalePrice = ToDouble(raw.GetValueOrDefault("pack_sale_price"));
            if (packSalePrice <= 0 && unitSalePrice > 0) packSalePrice = Math.Round(unitSalePrice * packSize, 2);
            else if (unitSalePrice <= 0 && packSalePrice > 0) unitSalePrice = Math.Round(packSalePrice / packSize, 4);
            var itemMargin = raw.TryGetValue("margin", out var mObj) && mObj != null && !string.IsNullOrWhiteSpace(mObj.ToString())
                ? ToDouble(mObj)
                : (unitSalePrice > 0 && avgPrice > 0 ? Math.Round(((unitSalePrice - avgPrice) / unitSalePrice) * 100.0, 2) : 0.0);

            raw["pack_size"] = packSize;
            raw["pack_quantity"] = packQty;
            raw["unit_quantity"] = unitQty;
            raw["pack_purchase_price"] = packPurchasePrice;
            raw["unit_purchase_price"] = unitPurchasePrice;
            raw["pack_sale_price"] = packSalePrice;
            raw["unit_sale_price"] = unitSalePrice;
            raw["margin"] = itemMargin;
            raw["pack_bonus"] = packBonus;
            raw["unit_bonus"] = unitBonus;
            raw["quantity"] = totalUnits;
            raw["sub_total"] = netTotal;
            raw["avg_price"] = avgPrice;

            calculatedItems.Add(raw);
        }

        rowsTotal = Math.Round(rowsTotal, 2);

        double billDiscPct = 0.0;
        double billDiscAmt = 0.0;

        if (HasNonZeroValue(data, "discount_amount", out var reqDiscAmt))
        {
            billDiscAmt = reqDiscAmt;
            billDiscPct = rowsTotal > 0 ? Math.Round((billDiscAmt / rowsTotal) * 100.0, 2) : 0.0;
        }
        else if (HasNonZeroValue(data, "discount_percentage", out var reqDiscPct))
        {
            billDiscPct = reqDiscPct;
            billDiscAmt = Math.Round((rowsTotal * billDiscPct) / 100.0, 2);
        }

        if (billDiscAmt > rowsTotal)
        {
            billDiscAmt = rowsTotal;
            billDiscPct = 100.0;
        }

        var afterDiscount = Math.Round(rowsTotal - billDiscAmt, 2);

        double taxPct = 0.0;
        double taxAmt = 0.0;

        if (HasNonZeroValue(data, "tax_amount", out var reqTaxAmt))
        {
            taxAmt = reqTaxAmt;
            taxPct = afterDiscount > 0 ? Math.Round((taxAmt / afterDiscount) * 100.0, 2) : 0.0;
        }
        else if (HasNonZeroValue(data, "tax_percentage", out var reqTaxPct))
        {
            taxPct = reqTaxPct;
            taxAmt = Math.Round((afterDiscount * taxPct) / 100.0, 2);
        }

        var totalAmount = Math.Round(afterDiscount + taxAmt, 2);

        bool clientTotalMatches = data.TryGetValue("total_amount", out var ctObj) && ctObj != null &&
                                  double.TryParse(ctObj.ToString(), out var clientTotal) &&
                                  Math.Abs(clientTotal - totalAmount) < 1.0;

        var invoiceAmount = clientTotalMatches &&
                            data.TryGetValue("invoice_amount", out var iaObj) && iaObj != null &&
                            double.TryParse(iaObj.ToString(), out var clientInvAmt) && clientInvAmt >= 0
            ? Math.Round(clientInvAmt, 2)
            : rowsTotal;

        return new Dictionary<string, object?>
        {
            ["items"] = calculatedItems,
            ["invoice_amount"] = invoiceAmount,
            ["discount_percentage"] = billDiscPct,
            ["discount_amount"] = billDiscAmt,
            ["tax_percentage"] = taxPct,
            ["tax_amount"] = taxAmt,
            ["total_amount"] = totalAmount
        };
    }

    public async Task<Dictionary<string, object?>> CalculateAndValidateSaleReturnAsync(
        IDictionary<string, object?> data,
        List<Dictionary<string, object?>> items)
    {
        if (items == null || items.Count == 0)
        {
            throw new InvoiceValidationException("At least one item is required.", new Dictionary<string, string[]>
            {
                ["items"] = new[] { "At least one item is required." }
            });
        }

        var sourceInvoiceItems = new List<Dictionary<string, object?>>();
        var saleInvoiceId = data.TryGetValue("sale_invoice_id", out var siObj) ? ToLong(siObj) : 0;
        if (saleInvoiceId > 0 && await _db.HasTableAsync("sale_invoice_items"))
        {
            sourceInvoiceItems = await _db.QueryListAsync(
                "SELECT * FROM sale_invoice_items WHERE sale_invoice_id = @id",
                new { id = saleInvoiceId });
        }

        var calculatedItems = new List<Dictionary<string, object?>>();
        double rowsTotal = 0.0;

        for (int idx = 0; idx < items.Count; idx++)
        {
            var raw = new Dictionary<string, object?>(items[idx], StringComparer.OrdinalIgnoreCase);
            var returnQty = (int)ToLong(raw.GetValueOrDefault("unit_return_quantity"));
            var unitPrice = ToDouble(raw.GetValueOrDefault("unit_sale_price"));
            var discPct = ToDouble(raw.GetValueOrDefault("item_discount_percentage"));

            if (returnQty < 0)
            {
                throw new InvoiceValidationException("Return quantity cannot be negative.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.unit_return_quantity"] = new[] { "Return quantity cannot be negative." }
                });
            }
            if (unitPrice < 0)
            {
                throw new InvoiceValidationException("Unit sale price cannot be negative.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.unit_sale_price"] = new[] { "Unit sale price cannot be negative." }
                });
            }
            if (discPct < 0 || discPct > 100)
            {
                throw new InvoiceValidationException("Discount percentage must be between 0 and 100.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.item_discount_percentage"] = new[] { "Discount percentage must be between 0 and 100." }
                });
            }

            if (sourceInvoiceItems.Count > 0 && Math.Abs(discPct) < 1e-9 && !raw.ContainsKey("item_discount_percentage"))
            {
                var pid = ToLong(raw.GetValueOrDefault("product_id"));
                var sourceItem = sourceInvoiceItems.FirstOrDefault(si => ToLong(si.GetValueOrDefault("product_id")) == pid);
                if (sourceItem != null)
                {
                    var sDisc = ToDouble(sourceItem.GetValueOrDefault("item_discount_percentage"));
                    if (sDisc > 0) discPct = sDisc;
                }
            }

            var lineGross = Math.Round(returnQty * unitPrice, 2);
            var lineDiscAmt = Math.Round((lineGross * discPct) / 100.0, 2);
            var lineSubTotal = Math.Round(lineGross - lineDiscAmt, 2);

            rowsTotal += lineSubTotal;

            raw["unit_return_quantity"] = returnQty;
            raw["unit_sale_price"] = unitPrice;
            raw["item_discount_percentage"] = discPct;
            raw["sub_total"] = lineSubTotal;

            calculatedItems.Add(raw);
        }

        rowsTotal = Math.Round(rowsTotal, 2);

        double billDiscPct = 0.0;
        double billDiscAmt = 0.0;

        if (HasNonZeroValue(data, "discount_amount", out var reqDiscAmt))
        {
            billDiscAmt = Math.Max(0.0, reqDiscAmt);
            billDiscPct = rowsTotal > 0 ? Math.Round((billDiscAmt / rowsTotal) * 100.0, 2) : 0.0;
        }
        else if (HasNonZeroValue(data, "discount_percentage", out var reqDiscPct))
        {
            billDiscPct = Math.Max(0.0, Math.Min(100.0, reqDiscPct));
            billDiscAmt = Math.Round((rowsTotal * billDiscPct) / 100.0, 2);
        }

        if (billDiscAmt > rowsTotal)
        {
            billDiscAmt = rowsTotal;
            billDiscPct = rowsTotal > 0 ? 100.0 : 0.0;
        }

        var afterDiscount = Math.Max(0.0, Math.Round(rowsTotal - billDiscAmt, 2));

        double taxPct = 0.0;
        double taxAmt = 0.0;

        if (HasNonZeroValue(data, "tax_amount", out var reqTaxAmt))
        {
            taxAmt = Math.Max(0.0, reqTaxAmt);
            taxPct = afterDiscount > 0 ? Math.Round((taxAmt / afterDiscount) * 100.0, 2) : 0.0;
        }
        else if (HasNonZeroValue(data, "tax_percentage", out var reqTaxPct))
        {
            taxPct = Math.Max(0.0, Math.Min(100.0, reqTaxPct));
            taxAmt = Math.Round((afterDiscount * taxPct) / 100.0, 2);
        }

        var total = Math.Round(afterDiscount + taxAmt, 2);

        return new Dictionary<string, object?>
        {
            ["items"] = calculatedItems,
            ["gross_total"] = rowsTotal,
            ["discount_percentage"] = billDiscPct,
            ["discount_amount"] = billDiscAmt,
            ["tax_percentage"] = taxPct,
            ["tax_amount"] = taxAmt,
            ["total"] = total
        };
    }

    public Dictionary<string, object?> CalculateAndValidatePurchaseReturn(
        IDictionary<string, object?> data,
        List<Dictionary<string, object?>> items)
    {
        if (items == null || items.Count == 0)
        {
            throw new InvoiceValidationException("At least one item is required.", new Dictionary<string, string[]>
            {
                ["items"] = new[] { "At least one item is required." }
            });
        }

        var calculatedItems = new List<Dictionary<string, object?>>();
        double rowsTotal = 0.0;

        for (int idx = 0; idx < items.Count; idx++)
        {
            var raw = new Dictionary<string, object?>(items[idx], StringComparer.OrdinalIgnoreCase);
            var packSize = (int)ToLong(raw.GetValueOrDefault("pack_size"));
            var returnPackQty = ToDouble(raw.GetValueOrDefault("return_pack_quantity"));
            var returnUnitQty = (int)ToLong(raw.GetValueOrDefault("return_unit_quantity"));
            var packPurchasePrice = ToDouble(raw.GetValueOrDefault("pack_purchase_price"));
            var unitPurchasePrice = ToDouble(raw.GetValueOrDefault("unit_purchase_price"));
            var discPct = ToDouble(raw.GetValueOrDefault("item_discount_percentage"));

            if (returnUnitQty < 0)
            {
                throw new InvoiceValidationException("Return unit quantity cannot be negative.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.return_unit_quantity"] = new[] { "Return unit quantity cannot be negative." }
                });
            }
            if (packPurchasePrice < 0 || unitPurchasePrice < 0)
            {
                throw new InvoiceValidationException("Purchase price cannot be negative.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.unit_purchase_price"] = new[] { "Purchase price cannot be negative." }
                });
            }
            if (discPct < 0 || discPct > 100)
            {
                throw new InvoiceValidationException("Discount percentage must be between 0 and 100.", new Dictionary<string, string[]>
                {
                    [$"items.{idx}.item_discount_percentage"] = new[] { "Discount percentage must be between 0 and 100." }
                });
            }

            if (packSize > 0)
            {
                if (unitPurchasePrice <= 0 && packPurchasePrice > 0)
                {
                    unitPurchasePrice = Math.Round(packPurchasePrice / packSize, 4);
                }
                else if (packPurchasePrice <= 0 && unitPurchasePrice > 0)
                {
                    packPurchasePrice = Math.Round(unitPurchasePrice * packSize, 2);
                }
            }

            var lineGross = Math.Round(returnUnitQty * unitPurchasePrice, 2);
            var lineDiscAmt = Math.Round((lineGross * discPct) / 100.0, 2);
            var lineSubTotal = Math.Round(lineGross - lineDiscAmt, 2);

            rowsTotal += lineSubTotal;

            raw["pack_size"] = packSize;
            raw["return_pack_quantity"] = returnPackQty;
            raw["return_unit_quantity"] = returnUnitQty;
            raw["pack_purchase_price"] = packPurchasePrice;
            raw["unit_purchase_price"] = unitPurchasePrice;
            raw["item_discount_percentage"] = discPct;
            raw["sub_total"] = lineSubTotal;

            calculatedItems.Add(raw);
        }

        rowsTotal = Math.Round(rowsTotal, 2);

        double billDiscPct = 0.0;
        double billDiscAmt = 0.0;

        if (HasNonZeroValue(data, "discount_amount", out var reqDiscAmt))
        {
            billDiscAmt = Math.Max(0.0, reqDiscAmt);
            billDiscPct = rowsTotal > 0 ? Math.Round((billDiscAmt / rowsTotal) * 100.0, 2) : 0.0;
        }
        else if (HasNonZeroValue(data, "discount_percentage", out var reqDiscPct))
        {
            billDiscPct = Math.Max(0.0, Math.Min(100.0, reqDiscPct));
            billDiscAmt = Math.Round((rowsTotal * billDiscPct) / 100.0, 2);
        }

        if (billDiscAmt > rowsTotal)
        {
            billDiscAmt = rowsTotal;
            billDiscPct = rowsTotal > 0 ? 100.0 : 0.0;
        }

        var afterDiscount = Math.Max(0.0, Math.Round(rowsTotal - billDiscAmt, 2));

        double taxPct = 0.0;
        double taxAmt = 0.0;

        if (HasNonZeroValue(data, "tax_amount", out var reqTaxAmt))
        {
            taxAmt = Math.Max(0.0, reqTaxAmt);
            taxPct = afterDiscount > 0 ? Math.Round((taxAmt / afterDiscount) * 100.0, 2) : 0.0;
        }
        else if (HasNonZeroValue(data, "tax_percentage", out var reqTaxPct))
        {
            taxPct = Math.Max(0.0, Math.Min(100.0, reqTaxPct));
            taxAmt = Math.Round((afterDiscount * taxPct) / 100.0, 2);
        }

        var total = Math.Round(afterDiscount + taxAmt, 2);

        return new Dictionary<string, object?>
        {
            ["items"] = calculatedItems,
            ["gross_total"] = rowsTotal,
            ["discount_percentage"] = billDiscPct,
            ["discount_amount"] = billDiscAmt,
            ["tax_percentage"] = taxPct,
            ["tax_amount"] = taxAmt,
            ["total"] = total
        };
    }

    private static bool HasNonZeroValue(IDictionary<string, object?> data, string key, out double val)
    {
        val = 0.0;
        if (!data.TryGetValue(key, out var raw) || raw == null) return false;
        var str = raw.ToString();
        if (string.IsNullOrWhiteSpace(str)) return false;
        val = ToDouble(raw);
        return Math.Abs(val) > 1e-9;
    }

    private static bool IsTruthy(object? val)
    {
        if (val == null) return false;
        if (val is bool b) return b;
        if (val is long l) return l != 0;
        if (val is int i) return i != 0;
        if (val is string s) return s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
        return false;
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

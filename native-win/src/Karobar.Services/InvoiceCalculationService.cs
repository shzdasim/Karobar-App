using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Text.Json;
using Karobar.Services.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

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
    private const int MaxSupervisorPasswordLength = 256;
    private const int MaxCandidateVerifications = 50;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StaleBucketMaxAge = TimeSpan.FromMinutes(15);

    private sealed class RequestScopeState
    {
        public readonly HashSet<string> FailedBucketKeys = new(StringComparer.Ordinal);
    }

    private sealed class RequestScopeHandle : IDisposable
    {
        private readonly RequestScopeState? _previous;
        private bool _disposed;

        public RequestScopeHandle(RequestScopeState? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CurrentRequestScope.Value = _previous;
        }
    }

    private static readonly AsyncLocal<RequestScopeState?> CurrentRequestScope = new();

    public static IDisposable BeginRequestScope()
    {
        var prev = CurrentRequestScope.Value;
        CurrentRequestScope.Value = new RequestScopeState();
        return new RequestScopeHandle(prev);
    }

    private sealed class AttemptBucket
    {
        public readonly object Sync = new();
        public readonly SemaphoreSlim Gate = new(1, 1);
        public readonly Queue<DateTimeOffset> Failures = new();
        public DateTimeOffset? LockedUntil { get; set; }
        public DateTimeOffset LastTouched { get; set; }
        public int ActiveUsers;
    }

    private static readonly ConcurrentDictionary<string, AttemptBucket> AttemptBuckets = new(StringComparer.Ordinal);

    private readonly KarobarDb _db;
    private readonly TradeSchemeService _tradeSchemeService;
    private readonly Func<DateTimeOffset> _utcNowProvider;

    [ActivatorUtilitiesConstructor]
    public InvoiceCalculationService(KarobarDb db) : this(db, null)
    {
    }

    public InvoiceCalculationService(KarobarDb db, Func<DateTimeOffset>? utcNowProvider = null)
    {
        _db = db;
        _tradeSchemeService = new TradeSchemeService(db);
        _utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public static void ResetRateLimitState()
    {
        AttemptBuckets.Clear();
    }

    public async Task<long?> ResolveAuthorizingUserIdAsync(
        long? currentUserId,
        IDictionary<string, object?>? data = null,
        string? permission = null)
    {
        var supervisorPassword = data != null && data.TryGetValue("supervisor_password", out var sp) ? sp?.ToString() ?? "" : "";
        var supervisorEmail = (data != null && data.TryGetValue("supervisor_email", out var se) ? se?.ToString() ?? "" : "").Trim();

        if (string.IsNullOrEmpty(supervisorEmail) && string.IsNullOrEmpty(supervisorPassword))
        {
            return currentUserId;
        }

        if (!await _db.HasTableAsync("users"))
        {
            return null;
        }

        var bucketKey = $"{_db.SqlitePath}|supervisor|{currentUserId ?? 0}";
        var bucket = AcquireBucketForGate(bucketKey, _utcNowProvider());
        await bucket.Gate.WaitAsync();
        try
        {
            EnsureNotLockedOut(bucket);

            if (string.IsNullOrEmpty(supervisorPassword) || supervisorPassword.Length > MaxSupervisorPasswordLength)
            {
                RecordFailureOnceInRequestOrThrowRateLimit(bucket, bucketKey);
                return null;
            }

            if (!string.IsNullOrEmpty(supervisorEmail))
            {
                var sup = await _db.QuerySingleOrDefaultAsync(
                    "SELECT id, password, status FROM users WHERE LOWER(email) = LOWER(@email) LIMIT 1",
                    new { email = supervisorEmail });
                if (sup != null)
                {
                    var status = sup.GetValueOrDefault("status")?.ToString() ?? "active";
                    var hash = sup.GetValueOrDefault("password")?.ToString() ?? "";
                    if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase) && VerifyPasswordHash(supervisorPassword, hash))
                    {
                        ClearAttemptBucket(bucket);
                        return ToLong(sup.GetValueOrDefault("id"));
                    }
                }

                RecordFailureOnceInRequestOrThrowRateLimit(bucket, bucketKey);
                return null;
            }

            var candidates = await QueryEligibleSupervisorCandidatesAsync(permission);
            foreach (var u in candidates)
            {
                var uid = ToLong(u.GetValueOrDefault("id"));
                var hash = u.GetValueOrDefault("password")?.ToString() ?? "";
                if (VerifyPasswordHash(supervisorPassword, hash))
                {
                    ClearAttemptBucket(bucket);
                    return uid;
                }
            }

            RecordFailureOnceInRequestOrThrowRateLimit(bucket, bucketKey);
            return null;
        }
        finally
        {
            bucket.Gate.Release();
            Interlocked.Decrement(ref bucket.ActiveUsers);
        }
    }

    private void RecordFailureOnceInRequestOrThrowRateLimit(AttemptBucket bucket, string bucketKey)
    {
        var scope = CurrentRequestScope.Value;
        if (scope != null && !scope.FailedBucketKeys.Add(bucketKey))
        {
            return;
        }
        RecordFailureOrThrowRateLimit(bucket);
    }

    private async Task<List<Dictionary<string, object?>>> QueryEligibleSupervisorCandidatesAsync(string? permission)
    {
        var hasRoles = await _db.HasTableAsync("roles") && await _db.HasTableAsync("model_has_roles");
        var hasPermissions = !string.IsNullOrWhiteSpace(permission) && await _db.HasTableAsync("permissions");
        var hasModelPerms = hasPermissions && await _db.HasTableAsync("model_has_permissions");
        var hasRolePerms = hasPermissions && hasRoles && await _db.HasTableAsync("role_has_permissions");

        if (!hasRoles && !hasModelPerms && !hasRolePerms)
        {
            return await _db.QueryListAsync(
                @"SELECT id, password
                  FROM users
                  WHERE (status IS NULL OR LOWER(status) = 'active')
                    AND password IS NOT NULL AND password != ''
                  ORDER BY id ASC
                  LIMIT @limit",
                new { limit = MaxCandidateVerifications });
        }

        var adminExistsSql = hasRoles
            ? @"EXISTS (
                    SELECT 1 FROM model_has_roles mhr
                    INNER JOIN roles r ON r.id = mhr.role_id
                    WHERE mhr.model_id = u.id
                      AND LOWER(TRIM(r.name)) IN ('admin', 'super-admin', 'super admin')
                )"
            : "0 = 1";

        var permClauses = new List<string>();
        if (hasModelPerms)
        {
            permClauses.Add(@"EXISTS (
                    SELECT 1 FROM model_has_permissions mhp
                    INNER JOIN permissions p ON p.id = mhp.permission_id
                    WHERE mhp.model_id = u.id
                      AND LOWER(p.name) = LOWER(@permission)
                )");
        }
        if (hasRolePerms)
        {
            permClauses.Add(@"EXISTS (
                    SELECT 1 FROM model_has_roles mhr2
                    INNER JOIN role_has_permissions rhp ON rhp.role_id = mhr2.role_id
                    INNER JOIN permissions p2 ON p2.id = rhp.permission_id
                    WHERE mhr2.model_id = u.id
                      AND LOWER(p2.name) = LOWER(@permission)
                )");
        }

        var eligibleClause = permClauses.Count > 0
            ? $"({adminExistsSql} OR {string.Join(" OR ", permClauses)})"
            : adminExistsSql;

        var sql = $@"
            SELECT u.id, u.password,
                   CASE WHEN {adminExistsSql} THEN 0 ELSE 1 END AS admin_rank
            FROM users u
            WHERE (u.status IS NULL OR LOWER(u.status) = 'active')
              AND u.password IS NOT NULL AND u.password != ''
              AND {eligibleClause}
            ORDER BY admin_rank ASC, u.id ASC
            LIMIT @limit";

        return await _db.QueryListAsync(sql, new { permission = permission?.Trim(), limit = MaxCandidateVerifications });
    }

    private static AttemptBucket AcquireBucketForGate(string bucketKey, DateTimeOffset now)
    {
        while (true)
        {
            var bucket = AttemptBuckets.GetOrAdd(bucketKey, _ => new AttemptBucket { LastTouched = now });
            Interlocked.Increment(ref bucket.ActiveUsers);
            if (AttemptBuckets.TryGetValue(bucketKey, out var current) && ReferenceEquals(current, bucket))
            {
                return bucket;
            }
            Interlocked.Decrement(ref bucket.ActiveUsers);
        }
    }

    private void EnsureNotLockedOut(AttemptBucket bucket)
    {
        var now = _utcNowProvider();
        PruneStaleBucketsIfNeeded(now);

        lock (bucket.Sync)
        {
            bucket.LastTouched = now;
            if (bucket.LockedUntil.HasValue)
            {
                if (bucket.LockedUntil.Value > now)
                {
                    var retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((bucket.LockedUntil.Value - now).TotalSeconds));
                    throw CreateRateLimitException(retryAfterSeconds);
                }

                bucket.LockedUntil = null;
                bucket.Failures.Clear();
            }
        }
    }

    private void RecordFailureOrThrowRateLimit(AttemptBucket bucket)
    {
        var now = _utcNowProvider();
        PruneStaleBucketsIfNeeded(now);

        int? retryAfterSeconds = null;

        lock (bucket.Sync)
        {
            bucket.LastTouched = now;
            if (bucket.LockedUntil.HasValue && bucket.LockedUntil.Value > now)
            {
                retryAfterSeconds = Math.Max(1, (int)Math.Ceiling((bucket.LockedUntil.Value - now).TotalSeconds));
            }
            else
            {
                if (bucket.LockedUntil.HasValue && bucket.LockedUntil.Value <= now)
                {
                    bucket.LockedUntil = null;
                    bucket.Failures.Clear();
                }

                var cutoff = now - FailureWindow;
                while (bucket.Failures.Count > 0 && bucket.Failures.Peek() <= cutoff)
                {
                    bucket.Failures.Dequeue();
                }

                bucket.Failures.Enqueue(now);
                if (bucket.Failures.Count >= MaxFailedAttempts)
                {
                    bucket.LockedUntil = now + LockoutDuration;
                    bucket.Failures.Clear();
                    retryAfterSeconds = (int)LockoutDuration.TotalSeconds;
                }
            }
        }

        if (retryAfterSeconds.HasValue)
        {
            throw CreateRateLimitException(retryAfterSeconds.Value);
        }
    }

    private static void ClearAttemptBucket(AttemptBucket bucket)
    {
        lock (bucket.Sync)
        {
            bucket.Failures.Clear();
            bucket.LockedUntil = null;
        }
    }

    private static void PruneStaleBucketsIfNeeded(DateTimeOffset now)
    {
        if (AttemptBuckets.Count <= 128)
        {
            return;
        }

        foreach (var kv in AttemptBuckets)
        {
            var bucket = kv.Value;
            if (Volatile.Read(ref bucket.ActiveUsers) > 0)
            {
                continue;
            }

            var remove = false;
            lock (bucket.Sync)
            {
                var notLocked = !bucket.LockedUntil.HasValue || bucket.LockedUntil.Value <= now;
                if (Volatile.Read(ref bucket.ActiveUsers) == 0 &&
                    notLocked &&
                    (now - bucket.LastTouched) > StaleBucketMaxAge)
                {
                    remove = true;
                }
            }

            if (remove)
            {
                AttemptBuckets.TryRemove(new KeyValuePair<string, AttemptBucket>(kv.Key, bucket));
            }
        }
    }

    private static InvoiceValidationException CreateRateLimitException(int retryAfterSeconds)
    {
        var msg = $"Too many failed supervisor override attempts. Please wait {retryAfterSeconds} seconds before trying again.";
        return new InvoiceValidationException(
            msg,
            statusCode: 429,
            customPayload: new Dictionary<string, object?>
            {
                ["message"] = msg,
                ["code"] = "RATE_LIMITED",
                ["retry_after_seconds"] = retryAfterSeconds,
                ["errors"] = new Dictionary<string, string[]>
                {
                    ["supervisor_password"] = new[] { msg }
                }
            });
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

        bool? cachedCanOverrideDisc = null;
        async Task<bool> CheckCanOverrideDiscountAsync()
        {
            if (cachedCanOverrideDisc.HasValue) return cachedCanOverrideDisc.Value;
            if (!discountOverride)
            {
                cachedCanOverrideDisc = await CanOverrideDiscountAsync(userId);
                return cachedCanOverrideDisc.Value;
            }
            var authorizingUserId = await ResolveAuthorizingUserIdAsync(userId, data, "sale-invoice.discount-override");
            cachedCanOverrideDisc = await CanOverrideDiscountAsync(authorizingUserId);
            return cachedCanOverrideDisc.Value;
        }

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
        decimal grossSum = 0m;
        decimal itemDiscSum = 0m;
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

        try
        {
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

                var prodPackSizeRaw = ToLong(product.GetValueOrDefault("pack_size") ?? 1);
                var prodPackSize = (int)Math.Clamp(prodPackSizeRaw, 1L, int.MaxValue);
                var rawPackSize = raw.TryGetValue("pack_size", out var psObj)
                    ? ParseIntQuantityOrThrow(psObj, $"items.{idx}.pack_size", "pack size", clampNegativeToZero: true, requireIntegral: true)
                    : 0;
                var packSize = rawPackSize > 0 ? rawPackSize : prodPackSize;

                var packQty = Math.Max(0m, ParseDecimalOrDefault(raw.GetValueOrDefault("pack_quantity"), $"items.{idx}.pack_quantity", "pack quantity"));
                var unitQty = ParseIntQuantityOrThrow(raw.GetValueOrDefault("unit_quantity"), $"items.{idx}.unit_quantity", "unit quantity", clampNegativeToZero: true);
                var packBonus = Math.Max(0m, ParseDecimalOrDefault(raw.GetValueOrDefault("pack_bonus"), $"items.{idx}.pack_bonus", "pack bonus"));
                var unitBonus = ParseIntQuantityOrThrow(raw.GetValueOrDefault("unit_bonus"), $"items.{idx}.unit_bonus", "unit bonus", clampNegativeToZero: true);
                var bonusQty = ParseIntQuantityOrThrow(raw.GetValueOrDefault("bonus"), $"items.{idx}.bonus", "bonus quantity", clampNegativeToZero: true);

                var packBonusUnitsDec = Round0(packBonus * packSize);
                if (packBonusUnitsDec > int.MaxValue)
                {
                    throw CreateRangeValidationException($"items.{idx}.pack_bonus", "Pack bonus units exceed maximum allowed value.");
                }
                var packBonusUnits = (int)packBonusUnitsDec;

                if (unitBonus > 0 && (bonusQty < 1 || bonusQty == unitBonus))
                {
                    bonusQty = unitBonus;
                    packBonus = Round2((decimal)bonusQty / packSize);
                }
                else if (packBonus > 0m && (bonusQty < 1 || bonusQty == packBonusUnits))
                {
                    bonusQty = packBonusUnits;
                    unitBonus = bonusQty;
                    packBonus = Round2((decimal)bonusQty / packSize);
                }
                else if (bonusQty > 0)
                {
                    unitBonus = bonusQty;
                    packBonus = Round2((decimal)bonusQty / packSize);
                }

                var qty = ParseIntQuantityOrThrow(raw.GetValueOrDefault("quantity"), $"items.{idx}.quantity", "quantity", clampNegativeToZero: true);
                var packQtyUnitsDec = Round0(packQty * packSize);
                if (packQtyUnitsDec > int.MaxValue)
                {
                    throw CreateRangeValidationException($"items.{idx}.pack_quantity", "Pack quantity units exceed maximum allowed value.");
                }
                var packQtyUnits = (int)packQtyUnitsDec;

                if (string.Equals(itemSaleType, "wholesale", StringComparison.OrdinalIgnoreCase))
                {
                    if (unitQty > 0 && (qty < 1 || qty == unitQty))
                    {
                        qty = unitQty;
                        packQty = Round2((decimal)qty / packSize);
                    }
                    else if (packQty > 0m && (qty < 1 || qty == packQtyUnits))
                    {
                        qty = packQtyUnits;
                        unitQty = qty;
                        packQty = Round2((decimal)qty / packSize);
                    }
                    else if (qty > 0)
                    {
                        unitQty = qty;
                        packQty = Round2((decimal)qty / packSize);
                    }
                }
                else
                {
                    if (qty < 1 && unitQty > 0)
                    {
                        qty = unitQty;
                    }
                    else if (qty < 1 && packQty > 0m)
                    {
                        qty = packQtyUnits;
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
                        bonusQty = (int)Math.Min(int.MaxValue, ToLong(schemeEval.GetValueOrDefault("bonus_units")));
                        unitBonus = (int)Math.Min(int.MaxValue, ToLong(schemeEval.GetValueOrDefault("unit_bonus") ?? bonusQty));
                        packBonus = ParseDecimalOrDefault(schemeEval.GetValueOrDefault("pack_bonus") ?? Round2((decimal)bonusQty / packSize), $"items.{idx}.pack_bonus", "pack bonus");
                        raw["applied_scheme_id"] = schemeEval.GetValueOrDefault("scheme_id");
                        raw["applied_scheme_name"] = schemeEval.GetValueOrDefault("scheme_name");
                    }
                }

                var price = ParseDecimalOrDefault(raw.GetValueOrDefault("price"), $"items.{idx}.price", "price");
                var hasPackPrice = raw.TryGetValue("pack_price", out var ppObj) && ppObj != null && !string.IsNullOrWhiteSpace(ppObj.ToString());
                var packPrice = hasPackPrice ? ParseDecimalOrDefault(ppObj, $"items.{idx}.pack_price", "pack price") : Round2(price * packSize);

                if (price <= 0m && packPrice > 0m && packSize > 0)
                {
                    price = Round4(packPrice / packSize);
                }
                if (price < 0m)
                {
                    throw new InvoiceValidationException("Price cannot be negative.", new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.price"] = new[] { "Price cannot be negative." }
                    });
                }

                var discPct = ParseDecimalOrDefault(raw.GetValueOrDefault("item_discount_percentage"), $"items.{idx}.item_discount_percentage", "item discount percentage");
                if (discPct < 0m)
                {
                    throw new InvoiceValidationException("Item discount percentage cannot be negative.", new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.item_discount_percentage"] = new[] { "Item discount percentage cannot be negative." }
                    });
                }
                if (discPct > 100m)
                {
                    throw new InvoiceValidationException("Item discount percentage cannot exceed 100%.", new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.item_discount_percentage"] = new[] { "Item discount percentage cannot exceed 100%." }
                    });
                }

                decimal? maxDiscount = product.TryGetValue("max_discount", out var mdObj) && mdObj != null && !string.IsNullOrWhiteSpace(mdObj.ToString())
                    ? ToDecimal(mdObj)
                    : null;
                var prodName = product.GetValueOrDefault("name")?.ToString() ?? $"Product #{productId}";

                decimal catalogPrice;
                if (string.Equals(itemSaleType, "retail", StringComparison.OrdinalIgnoreCase))
                {
                    catalogPrice = ToDecimal(product.GetValueOrDefault("unit_sale_price"));
                }
                else
                {
                    var wUnit = ToDecimal(product.GetValueOrDefault("whole_sale_unit_price"));
                    var wPack = ToDecimal(product.GetValueOrDefault("whole_sale_pack_price"));
                    catalogPrice = wUnit > 0m ? wUnit : (wPack > 0m && packSize > 0 ? wPack / packSize : 0m);
                }

                var effectiveItemDiscPct = discPct;
                if (maxDiscount.HasValue && maxDiscount.Value > 0m && catalogPrice > 0m && price < catalogPrice && !await CheckCanOverrideDiscountAsync())
                {
                    var effectivePriceAfterItemDisc = price * (1m - (discPct / 100m));
                    effectiveItemDiscPct = Math.Max(discPct, Round2(((catalogPrice - effectivePriceAfterItemDisc) / catalogPrice) * 100m));
                }

                if (maxDiscount.HasValue && maxDiscount.Value > 0m && effectiveItemDiscPct > maxDiscount.Value)
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

                    if (!await CheckCanOverrideDiscountAsync())
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

                var lineGross = Round2(qty * price);
                var lineDiscAmt = Round2((lineGross * discPct) / 100m);
                var lineSubTotal = Round2(lineGross - lineDiscAmt);

                var avgCost = ToDecimal(product.GetValueOrDefault("avg_price"));
                var unitPurch = ToDecimal(product.GetValueOrDefault("unit_purchase_price"));
                var packPurch = ToDecimal(product.GetValueOrDefault("pack_purchase_price"));
                var unitCost = avgCost > 0m
                    ? avgCost
                    : (unitPurch > 0m ? unitPurch : (packPurch > 0m && packSize > 0 ? packPurch / packSize : 0m));

                if ((string.Equals(itemSaleType, "wholesale", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(invoiceSaleType, "wholesale", StringComparison.OrdinalIgnoreCase)) && unitCost > 0m)
                {
                    var effectiveUnitPrice = price * (1m - (discPct / 100m));
                    var totalOutUnits = (long)qty + bonusQty;
                    var totalLineCost = Round2(totalOutUnits * unitCost);
                    if (effectiveUnitPrice < unitCost - 0.005m || lineSubTotal < totalLineCost - 0.05m)
                    {
                        var msg = $"Margin for '{prodName}' cannot be less than 0% (Net sale: {lineSubTotal}, Cost: {totalLineCost} at avg cost {Round2(unitCost)}/unit).";
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
                raw["pack_price"] = Round2(packPrice);
                raw["price"] = price;
                raw["sale_type"] = itemSaleType;
                raw["item_discount_percentage"] = discPct;
                raw["sub_total"] = lineSubTotal;

                calculatedItems.Add(raw);
            }

            grossSum = Round2(grossSum);
            itemDiscSum = Round2(itemDiscSum);

            decimal billDiscPct = 0m;
            decimal billDiscAmt = 0m;

            if (HasNonZeroValue(data, "discount_amount", out var reqDiscAmt))
            {
                billDiscAmt = reqDiscAmt;
                if (billDiscAmt < 0m)
                {
                    throw new InvoiceValidationException("Discount amount cannot be negative.", new Dictionary<string, string[]>
                    {
                        ["discount_amount"] = new[] { "Discount amount cannot be negative." }
                    });
                }
                billDiscPct = grossSum > 0m ? Round2((billDiscAmt / grossSum) * 100m) : 0m;
            }
            else if (HasNonZeroValue(data, "discount_percentage", out var reqDiscPct))
            {
                billDiscPct = reqDiscPct;
                if (billDiscPct < 0m || billDiscPct > 100m)
                {
                    throw new InvoiceValidationException("Discount percentage must be between 0 and 100.", new Dictionary<string, string[]>
                    {
                        ["discount_percentage"] = new[] { "Discount percentage must be between 0 and 100." }
                    });
                }
                billDiscAmt = Round2((grossSum * billDiscPct) / 100m);
            }

            var maxAllowedBillDisc = Math.Max(0m, grossSum - itemDiscSum);
            if (billDiscAmt > maxAllowedBillDisc)
            {
                billDiscAmt = maxAllowedBillDisc;
                billDiscPct = grossSum > 0m ? Round2((billDiscAmt / grossSum) * 100m) : 0m;
            }

            if (billDiscAmt > 0m && grossSum > 0m)
            {
                for (int idx = 0; idx < calculatedItems.Count; idx++)
                {
                    var calcItem = calculatedItems[idx];
                    var pid = ToLong(calcItem.GetValueOrDefault("product_id"));
                    if (!products.TryGetValue(pid, out var product)) continue;

                    decimal? maxDiscount = product.TryGetValue("max_discount", out var mdObj) && mdObj != null && !string.IsNullOrWhiteSpace(mdObj.ToString())
                        ? ToDecimal(mdObj)
                        : null;
                    if (maxDiscount.HasValue && maxDiscount.Value > 0m)
                    {
                        var combinedPct = Round2(ToDecimal(calcItem.GetValueOrDefault("item_discount_percentage")) + billDiscPct);
                        if (combinedPct > maxDiscount.Value && !await CheckCanOverrideDiscountAsync())
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

            var taxableBase = Math.Max(0m, grossSum - itemDiscSum - billDiscAmt);

            decimal taxPct = 0m;
            decimal taxAmt = 0m;

            if (HasNonZeroValue(data, "tax_amount", out var reqTaxAmt))
            {
                taxAmt = reqTaxAmt;
                if (taxAmt < 0m)
                {
                    throw new InvoiceValidationException("Tax amount cannot be negative.", new Dictionary<string, string[]>
                    {
                        ["tax_amount"] = new[] { "Tax amount cannot be negative." }
                    });
                }
                taxPct = taxableBase > 0m ? Round2((taxAmt / taxableBase) * 100m) : 0m;
            }
            else if (HasNonZeroValue(data, "tax_percentage", out var reqTaxPct))
            {
                taxPct = reqTaxPct;
                if (taxPct < 0m || taxPct > 100m)
                {
                    throw new InvoiceValidationException("Tax percentage must be between 0 and 100.", new Dictionary<string, string[]>
                    {
                        ["tax_percentage"] = new[] { "Tax percentage must be between 0 and 100." }
                    });
                }
                taxAmt = Round2((taxableBase * taxPct) / 100m);
            }

            var totalDecimal = taxableBase + taxAmt;
            var total = Round0(totalDecimal);

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
        catch (OverflowException)
        {
            throw CreateRangeValidationException("total", "Calculated invoice amounts exceed maximum supported range.");
        }
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
        decimal rowsTotal = 0m;

        try
        {
            for (int idx = 0; idx < items.Count; idx++)
            {
                var raw = new Dictionary<string, object?>(items[idx], StringComparer.OrdinalIgnoreCase);
                var packSize = ParseIntQuantityOrThrow(raw.GetValueOrDefault("pack_size"), $"items.{idx}.pack_size", "pack size", clampNegativeToZero: true, requireIntegral: true);
                var packQty = ParseDecimalOrDefault(raw.GetValueOrDefault("pack_quantity"), $"items.{idx}.pack_quantity", "pack quantity");
                var unitQty = ParseIntQuantityOrThrow(raw.GetValueOrDefault("unit_quantity"), $"items.{idx}.unit_quantity", "unit quantity", negativeMessage: "Unit quantity cannot be negative.");
                var packPurchasePrice = ParseDecimalOrDefault(raw.GetValueOrDefault("pack_purchase_price"), $"items.{idx}.pack_purchase_price", "pack purchase price");
                var unitPurchasePrice = ParseDecimalOrDefault(raw.GetValueOrDefault("unit_purchase_price"), $"items.{idx}.unit_purchase_price", "unit purchase price");
                var packBonus = ParseDecimalOrDefault(raw.GetValueOrDefault("pack_bonus"), $"items.{idx}.pack_bonus", "pack bonus");
                var unitBonus = ParseIntQuantityOrThrow(raw.GetValueOrDefault("unit_bonus"), $"items.{idx}.unit_bonus", "unit bonus", negativeMessage: "Unit bonus cannot be negative.");
                var discPct = ParseDecimalOrDefault(raw.GetValueOrDefault("item_discount_percentage"), $"items.{idx}.item_discount_percentage", "item discount percentage");

                if (packQty < 0m)
                {
                    throw new InvoiceValidationException("Pack quantity cannot be negative.", new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.pack_quantity"] = new[] { "Pack quantity cannot be negative." }
                    });
                }
                if (packBonus < 0m)
                {
                    throw new InvoiceValidationException("Pack bonus cannot be negative.", new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.pack_bonus"] = new[] { "Pack bonus cannot be negative." }
                    });
                }
                if (packPurchasePrice < 0m)
                {
                    throw new InvoiceValidationException("Pack purchase price cannot be negative.", new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.pack_purchase_price"] = new[] { "Pack purchase price cannot be negative." }
                    });
                }
                if (unitPurchasePrice < 0m)
                {
                    throw new InvoiceValidationException("Unit purchase price cannot be negative.", new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.unit_purchase_price"] = new[] { "Unit purchase price cannot be negative." }
                    });
                }
                if (discPct < 0m || discPct > 100m)
                {
                    throw new InvoiceValidationException("Discount percentage must be between 0 and 100.", new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.item_discount_percentage"] = new[] { "Discount percentage must be between 0 and 100." }
                    });
                }

                if (packSize > 0)
                {
                    if (unitQty <= 0 && packQty > 0m)
                    {
                        var calcUnits = Round0(packQty * packSize);
                        if (calcUnits > int.MaxValue)
                        {
                            throw CreateRangeValidationException($"items.{idx}.pack_quantity", "Pack quantity units exceed maximum allowed value.");
                        }
                        unitQty = (int)calcUnits;
                    }
                    else if (packQty <= 0m && unitQty > 0)
                    {
                        packQty = Round2((decimal)unitQty / packSize);
                    }

                    if (unitPurchasePrice <= 0m && packPurchasePrice > 0m)
                    {
                        unitPurchasePrice = Round4(packPurchasePrice / packSize);
                    }
                    else if (packPurchasePrice <= 0m && unitPurchasePrice > 0m)
                    {
                        packPurchasePrice = Round2(unitPurchasePrice * packSize);
                    }

                    if (unitBonus <= 0 && packBonus > 0m)
                    {
                        var calcBonusUnits = Round0(packBonus * packSize);
                        if (calcBonusUnits > int.MaxValue)
                        {
                            throw CreateRangeValidationException($"items.{idx}.pack_bonus", "Pack bonus units exceed maximum allowed value.");
                        }
                        unitBonus = (int)calcBonusUnits;
                    }
                    else if (packBonus <= 0m && unitBonus > 0)
                    {
                        packBonus = Round2((decimal)unitBonus / packSize);
                    }
                }

                long totalUnitsLong = (long)unitQty + unitBonus;
                if (totalUnitsLong > int.MaxValue)
                {
                    throw CreateRangeValidationException($"items.{idx}.quantity", "Total stock quantity exceeds maximum allowed value.");
                }
                var totalUnits = (int)totalUnitsLong;

                if (raw.TryGetValue("quantity", out var rawQObj) && !IsEmptyOrNullValue(rawQObj))
                {
                    var submittedQty = ParseIntQuantityOrThrow(rawQObj, $"items.{idx}.quantity", "quantity", negativeMessage: "Stock quantity cannot be negative.", requireIntegral: true);
                    if (submittedQty > 0 && submittedQty != totalUnits)
                    {
                        throw new InvoiceValidationException("Stock quantity must equal paid units plus bonus units.", new Dictionary<string, string[]>
                        {
                            [$"items.{idx}.quantity"] = new[] { "Stock quantity must equal paid units plus bonus units." }
                        });
                    }
                }

                var subTotal = Round2(unitQty * unitPurchasePrice);
                var discountAmount = Round2((subTotal * discPct) / 100m);
                var netTotal = Round2(subTotal - discountAmount);
                var avgPrice = totalUnits > 0 ? Round4(netTotal / totalUnits) : 0m;

                rowsTotal += netTotal;

                var unitSalePrice = ParseDecimalOrDefault(raw.GetValueOrDefault("unit_sale_price"), $"items.{idx}.unit_sale_price", "unit sale price");
                var packSalePrice = ParseDecimalOrDefault(raw.GetValueOrDefault("pack_sale_price"), $"items.{idx}.pack_sale_price", "pack sale price");
                if (packSalePrice <= 0m && unitSalePrice > 0m) packSalePrice = Round2(unitSalePrice * packSize);
                else if (unitSalePrice <= 0m && packSalePrice > 0m && packSize > 0) unitSalePrice = Round4(packSalePrice / packSize);
                var itemMargin = raw.TryGetValue("margin", out var mObj) && !IsEmptyOrNullValue(mObj)
                    ? ParseDecimalOrDefault(mObj, $"items.{idx}.margin", "margin")
                    : (unitSalePrice > 0m && avgPrice > 0m ? Round2(((unitSalePrice - avgPrice) / unitSalePrice) * 100m) : 0m);

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

            rowsTotal = Round2(rowsTotal);

            decimal billDiscPct = 0m;
            decimal billDiscAmt = 0m;

            if (HasNonZeroValue(data, "discount_amount", out var reqDiscAmt))
            {
                billDiscAmt = reqDiscAmt;
                billDiscPct = rowsTotal > 0m ? Round2((billDiscAmt / rowsTotal) * 100m) : 0m;
            }
            else if (HasNonZeroValue(data, "discount_percentage", out var reqDiscPct))
            {
                billDiscPct = reqDiscPct;
                billDiscAmt = Round2((rowsTotal * billDiscPct) / 100m);
            }

            if (billDiscAmt > rowsTotal)
            {
                billDiscAmt = rowsTotal;
                billDiscPct = 100m;
            }

            var afterDiscount = Round2(rowsTotal - billDiscAmt);

            decimal taxPct = 0m;
            decimal taxAmt = 0m;

            if (HasNonZeroValue(data, "tax_amount", out var reqTaxAmt))
            {
                taxAmt = reqTaxAmt;
                taxPct = afterDiscount > 0m ? Round2((taxAmt / afterDiscount) * 100m) : 0m;
            }
            else if (HasNonZeroValue(data, "tax_percentage", out var reqTaxPct))
            {
                taxPct = reqTaxPct;
                taxAmt = Round2((afterDiscount * taxPct) / 100m);
            }

            var totalAmount = Round2(afterDiscount + taxAmt);

            bool clientTotalMatches = data.TryGetValue("total_amount", out var ctObj) && !IsEmptyOrNullValue(ctObj) &&
                                      Math.Abs(ParseDecimalOrDefault(ctObj, "total_amount", "total amount") - totalAmount) < 1.0m;

            var invoiceAmount = clientTotalMatches &&
                                data.TryGetValue("invoice_amount", out var iaObj) && !IsEmptyOrNullValue(iaObj)
                ? (ParseDecimalOrDefault(iaObj, "invoice_amount", "invoice amount") is var clientInvAmt && clientInvAmt >= 0m ? Round2(clientInvAmt) : rowsTotal)
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
        catch (OverflowException)
        {
            throw CreateRangeValidationException("total_amount", "Calculated purchase amounts exceed maximum supported range.");
        }
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
        decimal rowsTotal = 0m;

        try
        {
            for (int idx = 0; idx < items.Count; idx++)
            {
                var raw = new Dictionary<string, object?>(items[idx], StringComparer.OrdinalIgnoreCase);
                var returnQty = ParseIntQuantityOrThrow(
                    raw.GetValueOrDefault("unit_return_quantity"),
                    $"items.{idx}.unit_return_quantity",
                    "return quantity",
                    negativeMessage: "Return quantity cannot be negative.",
                    requireIntegral: true);
                var unitPrice = ParseDecimalOrDefault(raw.GetValueOrDefault("unit_sale_price"), $"items.{idx}.unit_sale_price", "unit sale price");
                var discPct = ParseDecimalOrDefault(raw.GetValueOrDefault("item_discount_percentage"), $"items.{idx}.item_discount_percentage", "item discount percentage");

                if (unitPrice < 0m)
                {
                    throw new InvoiceValidationException("Unit sale price cannot be negative.", new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.unit_sale_price"] = new[] { "Unit sale price cannot be negative." }
                    });
                }
                if (discPct < 0m || discPct > 100m)
                {
                    throw new InvoiceValidationException("Discount percentage must be between 0 and 100.", new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.item_discount_percentage"] = new[] { "Discount percentage must be between 0 and 100." }
                    });
                }

                if (sourceInvoiceItems.Count > 0 && Math.Abs(discPct) < 0.0000001m && !raw.ContainsKey("item_discount_percentage"))
                {
                    var pid = ToLong(raw.GetValueOrDefault("product_id"));
                    var sourceItem = sourceInvoiceItems.FirstOrDefault(si => ToLong(si.GetValueOrDefault("product_id")) == pid);
                    if (sourceItem != null)
                    {
                        var sDisc = ToDecimal(sourceItem.GetValueOrDefault("item_discount_percentage"));
                        if (sDisc > 0m) discPct = sDisc;
                    }
                }

                var lineGross = Round2(returnQty * unitPrice);
                var lineDiscAmt = Round2((lineGross * discPct) / 100m);
                var lineSubTotal = Round2(lineGross - lineDiscAmt);

                rowsTotal += lineSubTotal;

                raw["unit_return_quantity"] = returnQty;
                raw["unit_sale_price"] = unitPrice;
                raw["item_discount_percentage"] = discPct;
                raw["sub_total"] = lineSubTotal;

                calculatedItems.Add(raw);
            }

            rowsTotal = Round2(rowsTotal);

            decimal billDiscPct = 0m;
            decimal billDiscAmt = 0m;

            if (HasNonZeroValue(data, "discount_amount", out var reqDiscAmt))
            {
                billDiscAmt = Math.Max(0m, reqDiscAmt);
                billDiscPct = rowsTotal > 0m ? Round2((billDiscAmt / rowsTotal) * 100m) : 0m;
            }
            else if (HasNonZeroValue(data, "discount_percentage", out var reqDiscPct))
            {
                billDiscPct = Math.Max(0m, Math.Min(100m, reqDiscPct));
                billDiscAmt = Round2((rowsTotal * billDiscPct) / 100m);
            }

            if (billDiscAmt > rowsTotal)
            {
                billDiscAmt = rowsTotal;
                billDiscPct = rowsTotal > 0m ? 100m : 0m;
            }

            var afterDiscount = Math.Max(0m, Round2(rowsTotal - billDiscAmt));

            decimal taxPct = 0m;
            decimal taxAmt = 0m;

            if (HasNonZeroValue(data, "tax_amount", out var reqTaxAmt))
            {
                taxAmt = Math.Max(0m, reqTaxAmt);
                taxPct = afterDiscount > 0m ? Round2((taxAmt / afterDiscount) * 100m) : 0m;
            }
            else if (HasNonZeroValue(data, "tax_percentage", out var reqTaxPct))
            {
                taxPct = Math.Max(0m, Math.Min(100m, reqTaxPct));
                taxAmt = Round2((afterDiscount * taxPct) / 100m);
            }

            var total = Round2(afterDiscount + taxAmt);

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
        catch (OverflowException)
        {
            throw CreateRangeValidationException("total", "Calculated sale return amounts exceed maximum supported range.");
        }
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
        decimal rowsTotal = 0m;

        try
        {
            for (int idx = 0; idx < items.Count; idx++)
            {
                var raw = new Dictionary<string, object?>(items[idx], StringComparer.OrdinalIgnoreCase);
                var packSize = ParseIntQuantityOrThrow(raw.GetValueOrDefault("pack_size"), $"items.{idx}.pack_size", "pack size", clampNegativeToZero: true, requireIntegral: true);
                var returnPackQty = ParseDecimalOrDefault(raw.GetValueOrDefault("return_pack_quantity"), $"items.{idx}.return_pack_quantity", "return pack quantity");
                var returnUnitQty = ParseIntQuantityOrThrow(
                    raw.GetValueOrDefault("return_unit_quantity"),
                    $"items.{idx}.return_unit_quantity",
                    "return unit quantity",
                    negativeMessage: "Return unit quantity cannot be negative.",
                    requireIntegral: true);
                var packPurchasePrice = ParseDecimalOrDefault(raw.GetValueOrDefault("pack_purchase_price"), $"items.{idx}.pack_purchase_price", "pack purchase price");
                var unitPurchasePrice = ParseDecimalOrDefault(raw.GetValueOrDefault("unit_purchase_price"), $"items.{idx}.unit_purchase_price", "unit purchase price");
                var discPct = ParseDecimalOrDefault(raw.GetValueOrDefault("item_discount_percentage"), $"items.{idx}.item_discount_percentage", "item discount percentage");

                if (packPurchasePrice < 0m || unitPurchasePrice < 0m)
                {
                    throw new InvoiceValidationException("Purchase price cannot be negative.", new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.unit_purchase_price"] = new[] { "Purchase price cannot be negative." }
                    });
                }
                if (discPct < 0m || discPct > 100m)
                {
                    throw new InvoiceValidationException("Discount percentage must be between 0 and 100.", new Dictionary<string, string[]>
                    {
                        [$"items.{idx}.item_discount_percentage"] = new[] { "Discount percentage must be between 0 and 100." }
                    });
                }

                if (packSize > 0)
                {
                    if (unitPurchasePrice <= 0m && packPurchasePrice > 0m)
                    {
                        unitPurchasePrice = Round4(packPurchasePrice / packSize);
                    }
                    else if (packPurchasePrice <= 0m && unitPurchasePrice > 0m)
                    {
                        packPurchasePrice = Round2(unitPurchasePrice * packSize);
                    }
                }

                var lineGross = Round2(returnUnitQty * unitPurchasePrice);
                var lineDiscAmt = Round2((lineGross * discPct) / 100m);
                var lineSubTotal = Round2(lineGross - lineDiscAmt);

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

            rowsTotal = Round2(rowsTotal);

            decimal billDiscPct = 0m;
            decimal billDiscAmt = 0m;

            if (HasNonZeroValue(data, "discount_amount", out var reqDiscAmt))
            {
                billDiscAmt = Math.Max(0m, reqDiscAmt);
                billDiscPct = rowsTotal > 0m ? Round2((billDiscAmt / rowsTotal) * 100m) : 0m;
            }
            else if (HasNonZeroValue(data, "discount_percentage", out var reqDiscPct))
            {
                billDiscPct = Math.Max(0m, Math.Min(100m, reqDiscPct));
                billDiscAmt = Round2((rowsTotal * billDiscPct) / 100m);
            }

            if (billDiscAmt > rowsTotal)
            {
                billDiscAmt = rowsTotal;
                billDiscPct = rowsTotal > 0m ? 100m : 0m;
            }

            var afterDiscount = Math.Max(0m, Round2(rowsTotal - billDiscAmt));

            decimal taxPct = 0m;
            decimal taxAmt = 0m;

            if (HasNonZeroValue(data, "tax_amount", out var reqTaxAmt))
            {
                taxAmt = Math.Max(0m, reqTaxAmt);
                taxPct = afterDiscount > 0m ? Round2((taxAmt / afterDiscount) * 100m) : 0m;
            }
            else if (HasNonZeroValue(data, "tax_percentage", out var reqTaxPct))
            {
                taxPct = Math.Max(0m, Math.Min(100m, reqTaxPct));
                taxAmt = Round2((afterDiscount * taxPct) / 100m);
            }

            var total = Round2(afterDiscount + taxAmt);

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
        catch (OverflowException)
        {
            throw CreateRangeValidationException("total", "Calculated purchase return amounts exceed maximum supported range.");
        }
    }

    private static decimal Round0(decimal v) => Math.Round(v, 0, MidpointRounding.AwayFromZero);
    private static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);
    private static decimal Round4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero);

    private static InvoiceValidationException CreateRangeValidationException(string fieldKey, string message)
    {
        return new InvoiceValidationException(message, new Dictionary<string, string[]>
        {
            [fieldKey] = new[] { message }
        });
    }

    private static bool IsEmptyOrNullValue(object? val)
    {
        if (val == null || val is DBNull) return true;
        if (val is JsonElement je)
        {
            return je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined ||
                   (je.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(je.GetString()));
        }
        return val is string s && string.IsNullOrWhiteSpace(s);
    }

    private static decimal ParseDecimalOrDefault(object? val, string fieldKey, string? fieldLabel = null)
    {
        if (IsEmptyOrNullValue(val)) return 0m;
        if (TryParseDecimal(val, out var parsed)) return parsed;
        var label = fieldLabel ?? fieldKey;
        throw CreateRangeValidationException(fieldKey, $"Invalid or out-of-range numeric value for {label}.");
    }

    private static int ParseIntQuantityOrThrow(
        object? val,
        string fieldKey,
        string fieldLabel,
        bool clampNegativeToZero = false,
        string? negativeMessage = null,
        bool requireIntegral = false)
    {
        var dec = ParseDecimalOrDefault(val, fieldKey, fieldLabel);
        if (dec < 0m)
        {
            if (clampNegativeToZero) return 0;
            if (!string.IsNullOrEmpty(negativeMessage))
            {
                throw CreateRangeValidationException(fieldKey, negativeMessage);
            }
            throw CreateRangeValidationException(fieldKey, $"The {fieldLabel} cannot be negative.");
        }

        if (requireIntegral && dec != Math.Truncate(dec))
        {
            throw CreateRangeValidationException(fieldKey, $"The {fieldLabel} must be a whole number.");
        }

        var rounded = requireIntegral ? dec : Round0(dec);
        if (rounded > int.MaxValue)
        {
            throw CreateRangeValidationException(fieldKey, $"The {fieldLabel} exceeds maximum allowed value.");
        }

        return (int)rounded;
    }

    private static bool HasNonZeroValue(IDictionary<string, object?> data, string key, out decimal val)
    {
        val = 0m;
        if (!data.TryGetValue(key, out var raw) || IsEmptyOrNullValue(raw)) return false;
        val = ParseDecimalOrDefault(raw, key, key);
        return Math.Abs(val) > 0.0000001m;
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

    private static bool TryParseDecimal(object? val, out decimal result)
    {
        result = 0m;
        if (val == null || val is DBNull) return false;
        if (val is decimal dec) { result = dec; return true; }
        if (val is long l) { result = l; return true; }
        if (val is int i) { result = i; return true; }
        if (val is short s) { result = s; return true; }
        if (val is byte b) { result = b; return true; }
        if (val is double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            try { result = Convert.ToDecimal(d); return true; } catch (OverflowException) { return false; }
        }
        if (val is float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) return false;
            try { result = Convert.ToDecimal(f); return true; } catch (OverflowException) { return false; }
        }
        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number)
            {
                if (je.TryGetDecimal(out result)) return true;
                if (je.TryGetDouble(out var jd) && !double.IsNaN(jd) && !double.IsInfinity(jd))
                {
                    try { result = Convert.ToDecimal(jd); return true; } catch (OverflowException) { return false; }
                }
                return false;
            }
            if (je.ValueKind == JsonValueKind.String)
            {
                var str = je.GetString();
                if (string.IsNullOrWhiteSpace(str)) return false;
                return decimal.TryParse(str.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);
            }
            return false;
        }
        var rawStr = val.ToString();
        if (string.IsNullOrWhiteSpace(rawStr)) return false;
        return decimal.TryParse(rawStr.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);
    }

    private static decimal ToDecimal(object? val)
    {
        return TryParseDecimal(val, out var parsed) ? parsed : 0m;
    }
}

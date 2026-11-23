using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Karobar.Services.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Karobar.Services;

public class OperatorPinException : Exception
{
    public int StatusCode { get; }
    public Dictionary<string, object?> ResponsePayload { get; }

    public OperatorPinException(string message, int statusCode, Dictionary<string, object?> payload)
        : base(message)
    {
        StatusCode = statusCode;
        ResponsePayload = payload;
    }
}

public class OperatorPinService
{
    private const string PinHashPrefix = "$pbkdf2-sha256$";
    private const int Pbkdf2Iterations = 10_000;
    private const int SaltByteLength = 16;
    private const int HashByteLength = 32;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StaleBucketMaxAge = TimeSpan.FromMinutes(15);

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
    private readonly Func<DateTimeOffset> _utcNowProvider;

    private static readonly HashSet<string> TrivialPins = new(StringComparer.Ordinal)
    {
        "0000", "1111", "2222", "3333", "4444",
        "5555", "6666", "7777", "8888", "9999",
        "1234", "4321", "0123"
    };

    private static readonly string[] DefaultPinModules =
    {
        "sale_invoice", "sale_return", "purchase_invoice", "purchase_return"
    };

    [ActivatorUtilitiesConstructor]
    public OperatorPinService(KarobarDb db) : this(db, null)
    {
    }

    public OperatorPinService(KarobarDb db, Func<DateTimeOffset>? utcNowProvider = null)
    {
        _db = db;
        _utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);
    }

    public static void ResetRateLimitState()
    {
        AttemptBuckets.Clear();
    }

    public static string HashPin(string normalizedPin)
    {
        var pin = (normalizedPin ?? "").Trim();
        var salt = RandomNumberGenerator.GetBytes(SaltByteLength);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            HashByteLength);
        return $"{PinHashPrefix}{Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool IsHashedPin(string? storedPin)
    {
        return !string.IsNullOrWhiteSpace(storedPin) &&
               storedPin.Trim().StartsWith(PinHashPrefix, StringComparison.Ordinal);
    }

    public static bool VerifyPin(string? candidatePin, string? storedPin)
    {
        if (string.IsNullOrWhiteSpace(candidatePin) || string.IsNullOrWhiteSpace(storedPin))
        {
            return false;
        }

        var candidate = candidatePin.Trim();
        var stored = storedPin.Trim();

        if (stored.StartsWith(PinHashPrefix, StringComparison.Ordinal))
        {
            var parts = stored.Split('$', StringSplitOptions.None);
            if (parts.Length != 5 || !string.Equals(parts[1], "pbkdf2-sha256", StringComparison.Ordinal))
            {
                return false;
            }

            if (!int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) ||
                iterations <= 0 ||
                iterations > 1_000_000)
            {
                return false;
            }

            byte[] salt;
            byte[] expectedHash;
            try
            {
                salt = Convert.FromBase64String(parts[3]);
                expectedHash = Convert.FromBase64String(parts[4]);
            }
            catch (FormatException)
            {
                return false;
            }

            if (salt.Length == 0 || expectedHash.Length == 0)
            {
                return false;
            }

            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(candidate),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);
            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }

        if (Regex.IsMatch(stored, @"^\d{4}$"))
        {
            var candidateBytes = Encoding.UTF8.GetBytes(candidate);
            var storedBytes = Encoding.UTF8.GetBytes(stored);
            if (candidateBytes.Length != storedBytes.Length)
            {
                return false;
            }
            return CryptographicOperations.FixedTimeEquals(candidateBytes, storedBytes);
        }

        return false;
    }

    public string? NormalizePin(string? pin)
    {
        if (pin == null) return null;
        var trimmed = pin.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    public async Task<string?> ValidateUniquePinAsync(
        string? pin,
        long? ignoreUserId = null,
        long? ignoreEmployeeId = null,
        long? linkedUserId = null,
        long? linkedEmployeeId = null)
    {
        var normalized = NormalizePin(pin);
        if (normalized == null) return null;

        if (!Regex.IsMatch(normalized, @"^\d{4}$"))
        {
            throw CreateValidationError("Operator code must be exactly 4 numeric digits (0000-9999).");
        }

        if (await _db.HasTableAsync("users") && await _db.HasColumnAsync("users", "pin_code"))
        {
            var sql = "SELECT id, name, pin_code FROM users WHERE pin_code IS NOT NULL AND pin_code != ''";
            if (ignoreUserId.HasValue) sql += " AND id != @ignoreUserId";
            if (linkedUserId.HasValue) sql += " AND id != @linkedUserId";

            var userRows = await _db.QueryListAsync(sql, new { ignoreUserId, linkedUserId });
            foreach (var row in userRows)
            {
                var stored = row.GetValueOrDefault("pin_code")?.ToString();
                if (VerifyPin(normalized, stored))
                {
                    var name = row.GetValueOrDefault("name")?.ToString() ?? "User";
                    throw CreateValidationError($"4-digit code {normalized} is already assigned to user '{name}'. Each user and employee must have a unique code.");
                }
            }
        }

        if (await _db.HasTableAsync("employees") && await _db.HasColumnAsync("employees", "pin_code"))
        {
            var sql = "SELECT id, name, pin_code FROM employees WHERE pin_code IS NOT NULL AND pin_code != ''";
            if (ignoreEmployeeId.HasValue) sql += " AND id != @ignoreEmployeeId";
            if (linkedEmployeeId.HasValue) sql += " AND id != @linkedEmployeeId";

            var empRows = await _db.QueryListAsync(sql, new { ignoreEmployeeId, linkedEmployeeId });
            foreach (var row in empRows)
            {
                var stored = row.GetValueOrDefault("pin_code")?.ToString();
                if (VerifyPin(normalized, stored))
                {
                    var name = row.GetValueOrDefault("name")?.ToString() ?? "Employee";
                    throw CreateValidationError($"4-digit code {normalized} is already assigned to employee '{name}'. Each user and employee must have a unique code.");
                }
            }
        }

        return HashPin(normalized);
    }

    public async Task SyncLinkedPinForUserAsync(long userId, string? pin)
    {
        if (await _db.HasTableAsync("employees") && await _db.HasColumnAsync("employees", "pin_code"))
        {
            await _db.ExecuteAsync(
                "UPDATE employees SET pin_code = @pin, updated_at = @now WHERE user_id = @userId",
                new { pin, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), userId });
        }
    }

    public async Task SyncLinkedPinForEmployeeAsync(long? userId, string? pin)
    {
        if (userId.HasValue && await _db.HasTableAsync("users") && await _db.HasColumnAsync("users", "pin_code"))
        {
            await _db.ExecuteAsync(
                "UPDATE users SET pin_code = @pin, updated_at = @now WHERE id = @userId",
                new { pin, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), userId = userId.Value });
        }
    }

    public async Task<string> GenerateUniquePinAsync()
    {
        var storedPins = new List<string>();

        if (await _db.HasTableAsync("users") && await _db.HasColumnAsync("users", "pin_code"))
        {
            var uPins = await _db.QueryListAsync("SELECT pin_code FROM users WHERE pin_code IS NOT NULL AND pin_code != ''");
            foreach (var r in uPins)
            {
                if (r.TryGetValue("pin_code", out var p) && p != null)
                {
                    var s = p.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) storedPins.Add(s);
                }
            }
        }

        if (await _db.HasTableAsync("employees") && await _db.HasColumnAsync("employees", "pin_code"))
        {
            var ePins = await _db.QueryListAsync("SELECT pin_code FROM employees WHERE pin_code IS NOT NULL AND pin_code != ''");
            foreach (var r in ePins)
            {
                if (r.TryGetValue("pin_code", out var p) && p != null)
                {
                    var s = p.ToString();
                    if (!string.IsNullOrWhiteSpace(s)) storedPins.Add(s);
                }
            }
        }

        for (int i = 0; i < 200; i++)
        {
            var candidate = RandomNumberGenerator.GetInt32(1000, 10000).ToString("D4");
            if (!TrivialPins.Contains(candidate) && !storedPins.Any(sp => VerifyPin(candidate, sp)))
            {
                return candidate;
            }
        }

        for (int n = 1; n <= 9999; n++)
        {
            var candidate = n.ToString("D4");
            if (!storedPins.Any(sp => VerifyPin(candidate, sp)))
            {
                return candidate;
            }
        }

        throw CreateValidationError("No available 4-digit PINs remaining.");
    }

    public async Task<bool> IsRequiredForModuleAsync(string module)
    {
        if (!await _db.HasTableAsync("settings") || !await _db.HasColumnAsync("settings", "require_operator_pin"))
        {
            return false;
        }

        var setting = await _db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1");
        if (setting == null) return false;

        var reqPin = setting.GetValueOrDefault("require_operator_pin");
        bool isEnabled = reqPin is true or 1 or 1L || (reqPin is string s && (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)));
        if (!isEnabled) return false;

        var resolvedModules = ResolveOperatorPinModules(setting);
        return resolvedModules.Contains(module, StringComparer.OrdinalIgnoreCase);
    }

    public static List<string> ResolveOperatorPinModules(IDictionary<string, object?>? setting)
    {
        if (setting == null || !setting.TryGetValue("operator_pin_modules", out var raw) || raw == null)
        {
            return DefaultPinModules.ToList();
        }

        if (raw is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            return je.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
        }

        if (raw is string str && !string.IsNullOrWhiteSpace(str))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<List<string>>(str);
                if (parsed != null) return parsed;
            }
            catch
            {
            }
        }

        if (raw is IEnumerable<object> list)
        {
            return list.Select(x => x?.ToString() ?? "").Where(x => !string.IsNullOrEmpty(x)).ToList();
        }

        return DefaultPinModules.ToList();
    }

    public async Task<Dictionary<string, object?>> ResolveOperatorAsync(
        string? rawPinInput,
        long? authUserId,
        string module,
        string? scopeKey = null)
    {
        var isRequired = await IsRequiredForModuleAsync(module);
        var rawPin = (rawPinInput ?? "").Trim();

        Dictionary<string, object?>? authUser = null;
        if (authUserId.HasValue)
        {
            authUser = await _db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE id = @id", new { id = authUserId.Value });
        }

        if (!isRequired && string.IsNullOrEmpty(rawPin))
        {
            Dictionary<string, object?>? linkedEmp = null;
            if (authUserId.HasValue && await _db.HasTableAsync("employees"))
            {
                linkedEmp = await _db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE user_id = @uid", new { uid = authUserId.Value });
            }

            var opName = linkedEmp?.GetValueOrDefault("name")?.ToString()
                         ?? authUser?.GetValueOrDefault("name")?.ToString()
                         ?? "System";

            return new Dictionary<string, object?>
            {
                ["user"] = authUser,
                ["user_id"] = authUser?.GetValueOrDefault("id"),
                ["employee"] = linkedEmp,
                ["employee_id"] = linkedEmp?.GetValueOrDefault("id"),
                ["operator_name"] = opName,
                ["verified_via_pin"] = false
            };
        }

        if (string.IsNullOrEmpty(rawPin))
        {
            throw CreatePinChallenge("Please enter your 4-digit operator code to save.", module);
        }

        var effectiveScope = !string.IsNullOrWhiteSpace(scopeKey)
            ? scopeKey.Trim()
            : (authUserId.HasValue ? $"user:{authUserId.Value}" : "anon");
        var bucketKey = $"{_db.SqlitePath}|{effectiveScope}";

        var bucket = AcquireBucketForGate(bucketKey, _utcNowProvider());
        await bucket.Gate.WaitAsync();
        try
        {
            EnsureNotLockedOut(bucket, module);

            if (!Regex.IsMatch(rawPin, @"^\d{4}$"))
            {
                ThrowFailureOrRateLimit(bucket, "Operator code must be exactly 4 digits.", module);
            }

            Dictionary<string, object?>? matchedUser = null;
            string? matchedUserStoredPin = null;
            if (await _db.HasTableAsync("users") && await _db.HasColumnAsync("users", "pin_code"))
            {
                var userRows = await _db.QueryListAsync("SELECT * FROM users WHERE pin_code IS NOT NULL AND pin_code != ''");
                foreach (var row in userRows)
                {
                    var stored = row.GetValueOrDefault("pin_code")?.ToString();
                    if (VerifyPin(rawPin, stored))
                    {
                        matchedUser = row;
                        matchedUserStoredPin = stored;
                        break;
                    }
                }
            }

            Dictionary<string, object?>? matchedEmp = null;
            string? matchedEmpStoredPin = null;
            if (await _db.HasTableAsync("employees") && await _db.HasColumnAsync("employees", "pin_code"))
            {
                var empRows = await _db.QueryListAsync("SELECT * FROM employees WHERE pin_code IS NOT NULL AND pin_code != ''");
                foreach (var row in empRows)
                {
                    var stored = row.GetValueOrDefault("pin_code")?.ToString();
                    if (VerifyPin(rawPin, stored))
                    {
                        matchedEmp = row;
                        matchedEmpStoredPin = stored;
                        break;
                    }
                }
            }

            if (matchedUser == null && matchedEmp == null)
            {
                ThrowFailureOrRateLimit(bucket, "Invalid 4-digit operator code. Please check your code and try again.", module);
            }

            if (matchedUser != null)
            {
                var uStatus = matchedUser.GetValueOrDefault("status")?.ToString() ?? "active";
                var uName = matchedUser.GetValueOrDefault("name")?.ToString() ?? "Operator";
                if (!uStatus.Equals("active", StringComparison.OrdinalIgnoreCase))
                {
                    ThrowFailureOrRateLimit(bucket, $"Operator account '{uName}' is inactive.", module);
                }

                var uid = Convert.ToInt64(matchedUser["id"]);
                if (matchedEmp == null && await _db.HasTableAsync("employees"))
                {
                    matchedEmp = await _db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE user_id = @uid", new { uid });
                    matchedEmpStoredPin = matchedEmp?.GetValueOrDefault("pin_code")?.ToString();
                }

                if (matchedEmp != null)
                {
                    var eStatus = matchedEmp.GetValueOrDefault("status")?.ToString() ?? "active";
                    var eName = matchedEmp.GetValueOrDefault("name")?.ToString() ?? "Employee";
                    if (eStatus.Equals("inactive", StringComparison.OrdinalIgnoreCase))
                    {
                        ThrowFailureOrRateLimit(bucket, $"Employee profile '{eName}' is inactive.", module);
                    }
                }

                ClearAttemptBucket(bucket);
                await UpgradeLegacyUserPinIfNeededAsync(matchedUser, matchedUserStoredPin, rawPin);
                if (matchedEmp != null && VerifyPin(rawPin, matchedEmpStoredPin))
                {
                    await UpgradeLegacyEmployeePinIfNeededAsync(matchedEmp, matchedEmpStoredPin, rawPin);
                }

                return new Dictionary<string, object?>
                {
                    ["user"] = matchedUser,
                    ["user_id"] = uid,
                    ["employee"] = matchedEmp,
                    ["employee_id"] = matchedEmp?.GetValueOrDefault("id"),
                    ["operator_name"] = matchedEmp?.GetValueOrDefault("name")?.ToString() ?? uName,
                    ["verified_via_pin"] = true
                };
            }

            // Only Employee matched
            var nonNullEmp = matchedEmp!;
            var empStatus = nonNullEmp.GetValueOrDefault("status")?.ToString() ?? "active";
            var empName = nonNullEmp.GetValueOrDefault("name")?.ToString() ?? "Employee";
            if (empStatus.Equals("inactive", StringComparison.OrdinalIgnoreCase))
            {
                ThrowFailureOrRateLimit(bucket, $"Employee '{empName}' is inactive.", module);
            }

            Dictionary<string, object?>? resolvedUser = null;
            if (nonNullEmp.TryGetValue("user_id", out var empUidObj) && empUidObj != null && long.TryParse(empUidObj.ToString(), out var empUid))
            {
                resolvedUser = await _db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE id = @id", new { id = empUid });
                if (resolvedUser != null)
                {
                    var ruStatus = resolvedUser.GetValueOrDefault("status")?.ToString() ?? "active";
                    if (!ruStatus.Equals("active", StringComparison.OrdinalIgnoreCase))
                    {
                        ThrowFailureOrRateLimit(bucket, $"Linked user account for '{empName}' is inactive.", module);
                    }
                }
            }

            ClearAttemptBucket(bucket);
            await UpgradeLegacyEmployeePinIfNeededAsync(nonNullEmp, matchedEmpStoredPin, rawPin);
            if (resolvedUser != null)
            {
                var resolvedUserPin = resolvedUser.GetValueOrDefault("pin_code")?.ToString();
                if (VerifyPin(rawPin, resolvedUserPin))
                {
                    await UpgradeLegacyUserPinIfNeededAsync(resolvedUser, resolvedUserPin, rawPin);
                }
            }

            var finalUser = resolvedUser ?? authUser;
            return new Dictionary<string, object?>
            {
                ["user"] = finalUser,
                ["user_id"] = finalUser?.GetValueOrDefault("id"),
                ["employee"] = nonNullEmp,
                ["employee_id"] = nonNullEmp.GetValueOrDefault("id"),
                ["operator_name"] = empName,
                ["verified_via_pin"] = true
            };
        }
        finally
        {
            bucket.Gate.Release();
            Interlocked.Decrement(ref bucket.ActiveUsers);
        }
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

    private void EnsureNotLockedOut(AttemptBucket bucket, string module)
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
                    throw CreateRateLimitException(retryAfterSeconds, module);
                }

                bucket.LockedUntil = null;
                bucket.Failures.Clear();
            }
        }
    }

    private void ThrowFailureOrRateLimit(AttemptBucket bucket, string message, string module)
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
            throw CreateRateLimitException(retryAfterSeconds.Value, module);
        }

        throw CreatePinChallenge(message, module);
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

    private async Task UpgradeLegacyUserPinIfNeededAsync(
        Dictionary<string, object?>? userRow,
        string? storedPin,
        string rawPin)
    {
        if (userRow == null || string.IsNullOrWhiteSpace(storedPin) || IsHashedPin(storedPin))
        {
            return;
        }

        if (!userRow.TryGetValue("id", out var idObj) || idObj == null || !long.TryParse(idObj.ToString(), out var userId))
        {
            return;
        }

        var hashed = HashPin(rawPin);
        var updatedRows = await _db.ExecuteAsync(
            "UPDATE users SET pin_code = @hashed, updated_at = @now WHERE id = @userId AND pin_code = @legacyPin",
            new { hashed, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), userId, legacyPin = storedPin });
        if (updatedRows > 0)
        {
            userRow["pin_code"] = hashed;
        }
    }

    private async Task UpgradeLegacyEmployeePinIfNeededAsync(
        Dictionary<string, object?>? empRow,
        string? storedPin,
        string rawPin)
    {
        if (empRow == null || string.IsNullOrWhiteSpace(storedPin) || IsHashedPin(storedPin))
        {
            return;
        }

        if (!empRow.TryGetValue("id", out var idObj) || idObj == null || !long.TryParse(idObj.ToString(), out var empId))
        {
            return;
        }

        var hashed = HashPin(rawPin);
        var updatedRows = await _db.ExecuteAsync(
            "UPDATE employees SET pin_code = @hashed, updated_at = @now WHERE id = @empId AND pin_code = @legacyPin",
            new { hashed, now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"), empId, legacyPin = storedPin });
        if (updatedRows > 0)
        {
            empRow["pin_code"] = hashed;
        }
    }

    private static OperatorPinException CreateValidationError(string message)
    {
        return new OperatorPinException(message, 422, new Dictionary<string, object?>
        {
            ["message"] = message,
            ["errors"] = new Dictionary<string, string[]>
            {
                ["pin_code"] = new[] { message }
            }
        });
    }

    private static OperatorPinException CreatePinChallenge(string message, string module)
    {
        return new OperatorPinException(message, 422, new Dictionary<string, object?>
        {
            ["message"] = message,
            ["requires_operator_pin"] = true,
            ["module"] = module,
            ["errors"] = new Dictionary<string, string[]>
            {
                ["operator_pin"] = new[] { message }
            }
        });
    }

    private static OperatorPinException CreateRateLimitException(int retryAfterSeconds, string module)
    {
        var message = $"Too many invalid operator PIN attempts. Please wait {retryAfterSeconds} seconds before trying again.";
        return new OperatorPinException(message, 429, new Dictionary<string, object?>
        {
            ["message"] = message,
            ["retry_after_seconds"] = retryAfterSeconds,
            ["requires_operator_pin"] = true,
            ["module"] = module,
            ["errors"] = new Dictionary<string, string[]>
            {
                ["operator_pin"] = new[] { message }
            }
        });
    }
}

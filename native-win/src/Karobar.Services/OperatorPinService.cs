using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Karobar.Services.Infrastructure;

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
    private readonly KarobarDb _db;
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

    public OperatorPinService(KarobarDb db)
    {
        _db = db;
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
            var sql = "SELECT id, name FROM users WHERE pin_code = @pin";
            if (ignoreUserId.HasValue) sql += " AND id != @ignoreUserId";
            if (linkedUserId.HasValue) sql += " AND id != @linkedUserId";

            var conflict = await _db.QuerySingleOrDefaultAsync(sql, new { pin = normalized, ignoreUserId, linkedUserId });
            if (conflict != null)
            {
                var name = conflict.GetValueOrDefault("name")?.ToString() ?? "User";
                throw CreateValidationError($"4-digit code {normalized} is already assigned to user '{name}'. Each user and employee must have a unique code.");
            }
        }

        if (await _db.HasTableAsync("employees") && await _db.HasColumnAsync("employees", "pin_code"))
        {
            var sql = "SELECT id, name FROM employees WHERE pin_code = @pin";
            if (ignoreEmployeeId.HasValue) sql += " AND id != @ignoreEmployeeId";
            if (linkedEmployeeId.HasValue) sql += " AND id != @linkedEmployeeId";

            var conflict = await _db.QuerySingleOrDefaultAsync(sql, new { pin = normalized, ignoreEmployeeId, linkedEmployeeId });
            if (conflict != null)
            {
                var name = conflict.GetValueOrDefault("name")?.ToString() ?? "Employee";
                throw CreateValidationError($"4-digit code {normalized} is already assigned to employee '{name}'. Each user and employee must have a unique code.");
            }
        }

        return normalized;
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
        var used = new HashSet<string>(StringComparer.Ordinal);

        if (await _db.HasTableAsync("users") && await _db.HasColumnAsync("users", "pin_code"))
        {
            var uPins = await _db.QueryListAsync("SELECT pin_code FROM users WHERE pin_code IS NOT NULL AND pin_code != ''");
            foreach (var r in uPins)
            {
                if (r.TryGetValue("pin_code", out var p) && p != null) used.Add(p.ToString()!);
            }
        }

        if (await _db.HasTableAsync("employees") && await _db.HasColumnAsync("employees", "pin_code"))
        {
            var ePins = await _db.QueryListAsync("SELECT pin_code FROM employees WHERE pin_code IS NOT NULL AND pin_code != ''");
            foreach (var r in ePins)
            {
                if (r.TryGetValue("pin_code", out var p) && p != null) used.Add(p.ToString()!);
            }
        }

        for (int i = 0; i < 200; i++)
        {
            var candidate = RandomNumberGenerator.GetInt32(1000, 10000).ToString("D4");
            if (!used.Contains(candidate) && !TrivialPins.Contains(candidate))
            {
                return candidate;
            }
        }

        for (int n = 1; n <= 9999; n++)
        {
            var candidate = n.ToString("D4");
            if (!used.Contains(candidate))
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

    public async Task<Dictionary<string, object?>> ResolveOperatorAsync(string? rawPinInput, long? authUserId, string module)
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

        if (!Regex.IsMatch(rawPin, @"^\d{4}$"))
        {
            throw CreatePinChallenge("Operator code must be exactly 4 digits.", module);
        }

        Dictionary<string, object?>? matchedUser = null;
        if (await _db.HasTableAsync("users") && await _db.HasColumnAsync("users", "pin_code"))
        {
            matchedUser = await _db.QuerySingleOrDefaultAsync("SELECT * FROM users WHERE pin_code = @pin", new { pin = rawPin });
        }

        Dictionary<string, object?>? matchedEmp = null;
        if (await _db.HasTableAsync("employees") && await _db.HasColumnAsync("employees", "pin_code"))
        {
            matchedEmp = await _db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE pin_code = @pin", new { pin = rawPin });
        }

        if (matchedUser == null && matchedEmp == null)
        {
            throw CreatePinChallenge("Invalid 4-digit operator code. Please check your code and try again.", module);
        }

        if (matchedUser != null)
        {
            var uStatus = matchedUser.GetValueOrDefault("status")?.ToString() ?? "active";
            var uName = matchedUser.GetValueOrDefault("name")?.ToString() ?? "Operator";
            if (!uStatus.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                throw CreatePinChallenge($"Operator account '{uName}' is inactive.", module);
            }

            var uid = Convert.ToInt64(matchedUser["id"]);
            if (matchedEmp == null && await _db.HasTableAsync("employees"))
            {
                matchedEmp = await _db.QuerySingleOrDefaultAsync("SELECT * FROM employees WHERE user_id = @uid", new { uid });
            }

            if (matchedEmp != null)
            {
                var eStatus = matchedEmp.GetValueOrDefault("status")?.ToString() ?? "active";
                var eName = matchedEmp.GetValueOrDefault("name")?.ToString() ?? "Employee";
                if (eStatus.Equals("inactive", StringComparison.OrdinalIgnoreCase))
                {
                    throw CreatePinChallenge($"Employee profile '{eName}' is inactive.", module);
                }
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
            throw CreatePinChallenge($"Employee '{empName}' is inactive.", module);
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
                    throw CreatePinChallenge($"Linked user account for '{empName}' is inactive.", module);
                }
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
}

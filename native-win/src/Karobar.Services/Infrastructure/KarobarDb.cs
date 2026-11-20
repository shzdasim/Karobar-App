using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Karobar.Services.Infrastructure;

public class KarobarDb
{
    private readonly IServiceProvider? _serviceProvider;
    private readonly IConfiguration? _configuration;
    private readonly string _sqlitePath;
    private readonly string _projectRoot;

    private static readonly ConcurrentDictionary<string, bool> TableCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, HashSet<string>> ColumnCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> WalInitializedDbPaths = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> JsonColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "preferences",
        "payload",
        "abilities",
        "dosage_sticker_config",
        "operator_pin_modules",
        "meta",
        "items_json",
        "draft_data",
        "changes",
        "before_state",
        "after_state",
        "old_values",
        "new_values",
        "filters",
        "config",
        "template_config",
        "fbr_response_payload",
        "fbr_request_payload",
        "destination_config",
        "metadata",
        "thermal_custom_layout",
        "closing_denominations",
        "layout_options",
        "before_payload",
        "after_payload",
        "diff"
    };

    private static readonly HashSet<string> BooleanColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "valid",
        "is_authorized",
        "is_active",
        "is_default",
        "is_system",
        "is_recurring",
        "is_narcotic",
        "is_refrigerated",
        "is_setup_completed",
        "require_operator_pin",
        "terminal_auto_authorize",
        "print_show_logo",
        "share_show_logo",
        "fbr_enabled",
        "fbr_auto_sync",
        "fbr_pos_fee_enabled",
        "allow_negative_stock",
        "auto_backup_enabled",
        "smtp_enabled",
        "cube_ai_enabled",
        "cube_groq_enabled",
        "cube_gemini_enabled",
        "whatsapp_enabled",
        "whatsapp_auto_fallback",
        "is_automated",
        "notify_on_failure",
        "is_regular",
        "is_preferred",
        "requires_cold_chain",
        "is_narcotic_controlled",
        "enable_dosage_stickers",
        "enable_prescription_attachments",
        "enable_drug_interaction_warnings",
        "enable_cold_chain_log",
        "operator_pin_auto_submit",
        "is_blind",
        "has_narcotics",
        "record_expense",
        "is_manual",
        "eligible_for_schemes",
        "is_custom_price"
    };

    public KarobarDb(IServiceProvider? serviceProvider = null, IConfiguration? configuration = null, string? explicitSqlitePath = null)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _projectRoot = ResolveProjectRootDirectory();
        _sqlitePath = ResolveSqlitePath(explicitSqlitePath, configuration, _projectRoot);
    }

    public string ProjectRoot => _projectRoot;
    public string SqlitePath => _sqlitePath;

    public string BasePath(string relative = "")
    {
        if (string.IsNullOrWhiteSpace(relative)) return _projectRoot;
        return Path.Combine(_projectRoot, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    public string StoragePath(string relative = "")
    {
        var baseStorage = Path.Combine(_projectRoot, "storage");
        if (string.IsNullOrWhiteSpace(relative)) return baseStorage;
        return Path.Combine(baseStorage, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    public string PublicPath(string relative = "")
    {
        var basePublic = Path.Combine(_projectRoot, "public");
        if (string.IsNullOrWhiteSpace(relative)) return basePublic;
        return Path.Combine(basePublic, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    public static string ResolveProjectRootDirectory()
    {
        var envRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH")
                      ?? Environment.GetEnvironmentVariable("KAROBAR_BASE_PATH");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
        {
            return Path.GetFullPath(envRoot);
        }

        var current = Directory.GetCurrentDirectory();
        var dir = new DirectoryInfo(current);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "artisan")) ||
                Directory.Exists(Path.Combine(dir.FullName, "database")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (baseDir != null)
        {
            if (File.Exists(Path.Combine(baseDir.FullName, "artisan")) ||
                Directory.Exists(Path.Combine(baseDir.FullName, "database")))
            {
                return baseDir.FullName;
            }
            baseDir = baseDir.Parent;
        }

        return current;
    }

    private static string ResolveSqlitePath(string? explicitPath, IConfiguration? config, string projectRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        var envDb = Environment.GetEnvironmentVariable("KAROBAR_DB_PATH")
                    ?? Environment.GetEnvironmentVariable("DB_DATABASE");
        if (!string.IsNullOrWhiteSpace(envDb))
        {
            return Path.IsPathRooted(envDb) ? envDb : Path.Combine(projectRoot, envDb);
        }

        var connStr = config?.GetConnectionString("DefaultConnection") ?? config?["Database:Path"];
        if (!string.IsNullOrWhiteSpace(connStr))
        {
            if (connStr.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                var builder = new SqliteConnectionStringBuilder(connStr);
                if (!string.IsNullOrWhiteSpace(builder.DataSource))
                {
                    return Path.IsPathRooted(builder.DataSource)
                        ? builder.DataSource
                        : Path.Combine(projectRoot, builder.DataSource);
                }
            }
            else
            {
                return Path.IsPathRooted(connStr) ? connStr : Path.Combine(projectRoot, connStr);
            }
        }

        return Path.Combine(projectRoot, "database", "database.sqlite");
    }

    public SqliteConnection CreateConnection()
    {
        var dir = Path.GetDirectoryName(_sqlitePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = _sqlitePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
            DefaultTimeout = 20
        }.ToString();

        var conn = new SqliteConnection(connStr);
        conn.Open();

        var needsWal = !WalInitializedDbPaths.ContainsKey(_sqlitePath);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = needsWal
            ? "PRAGMA busy_timeout=15000; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY; PRAGMA foreign_keys=ON;"
            : "PRAGMA busy_timeout=15000; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
        if (needsWal) WalInitializedDbPaths[_sqlitePath] = true;

        return conn;
    }

    public async Task<SqliteConnection> OpenConnectionAsync()
    {
        var dir = Path.GetDirectoryName(_sqlitePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = _sqlitePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
            DefaultTimeout = 20
        }.ToString();

        var conn = new SqliteConnection(connStr);
        await conn.OpenAsync();

        var needsWal = !WalInitializedDbPaths.ContainsKey(_sqlitePath);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = needsWal
            ? "PRAGMA busy_timeout=15000; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY; PRAGMA foreign_keys=ON;"
            : "PRAGMA busy_timeout=15000; PRAGMA synchronous=NORMAL; PRAGMA temp_store=MEMORY; PRAGMA foreign_keys=ON;";
        await cmd.ExecuteNonQueryAsync();
        if (needsWal) WalInitializedDbPaths[_sqlitePath] = true;

        return conn;
    }

    private static async Task<T> ExecuteWithSqliteRetryAsync<T>(Func<Task<T>> operation)
    {
        int[] delaysMs = [35, 90, 200, 420];
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (SqliteException ex) when ((ex.SqliteErrorCode is 5 or 6) && attempt < delaysMs.Length)
            {
                await Task.Delay(delaysMs[attempt] + Random.Shared.Next(5, 30));
            }
        }
    }

    public static void FlushSchemaCache()
    {
        TableCache.Clear();
        ColumnCache.Clear();
        WalInitializedDbPaths.Clear();
        Karobar.Data.SqliteConnectionInterceptor.ResetWalCache();
    }

    public async Task<bool> HasTableAsync(string tableName)
    {
        var cacheKey = $"{_sqlitePath}:{tableName}";
        if (TableCache.TryGetValue(cacheKey, out var exists))
        {
            return exists;
        }

        try
        {
            await using var conn = await OpenConnectionAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name = @name",
                new { name = tableName });
            var result = count > 0;
            TableCache[cacheKey] = result;
            return result;
        }
        catch
        {
            return false;
        }
    }

    public bool HasTable(string tableName)
    {
        return HasTableAsync(tableName).GetAwaiter().GetResult();
    }

    public async Task<HashSet<string>> GetTableColumnsAsync(string tableName)
    {
        var cacheKey = $"{_sqlitePath}:{tableName}";
        if (ColumnCache.TryGetValue(cacheKey, out var cached) && cached.Count > 0)
        {
            return cached;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!await HasTableAsync(tableName))
            {
                return set;
            }

            await using var conn = await OpenConnectionAsync();
            var escaped = tableName.Replace("\"", "\"\"");
            var rows = await conn.QueryAsync($"PRAGMA table_info(\"{escaped}\")");
            foreach (var r in rows)
            {
                var dict = (IDictionary<string, object>)r;
                if (dict.TryGetValue("name", out var nameObj) && nameObj != null)
                {
                    set.Add(nameObj.ToString()!);
                }
            }

            if (set.Count > 0)
            {
                ColumnCache[cacheKey] = set;
            }
        }
        catch
        {
        }

        return set;
    }

    public async Task<bool> HasColumnAsync(string tableName, string columnName)
    {
        var cols = await GetTableColumnsAsync(tableName);
        return cols.Contains(columnName);
    }

    public bool HasColumn(string tableName, string columnName)
    {
        return HasColumnAsync(tableName, columnName).GetAwaiter().GetResult();
    }

    public async Task<List<Dictionary<string, object?>>> QueryListAsync(string sql, object? param = null, IDbConnection? existingConn = null, IDbTransaction? tx = null)
    {
        if (existingConn != null)
        {
            var rows = await existingConn.QueryAsync(sql, param, tx);
            return rows.Select(NormalizeRow).ToList();
        }

        await using var conn = await OpenConnectionAsync();
        var result = await conn.QueryAsync(sql, param);
        return result.Select(NormalizeRow).ToList();
    }

    public async Task<Dictionary<string, object?>?> QuerySingleOrDefaultAsync(string sql, object? param = null, IDbConnection? existingConn = null, IDbTransaction? tx = null)
    {
        if (existingConn != null)
        {
            var row = await existingConn.QueryFirstOrDefaultAsync(sql, param, tx);
            return row == null ? null : NormalizeRow(row);
        }

        await using var conn = await OpenConnectionAsync();
        var res = await conn.QueryFirstOrDefaultAsync(sql, param);
        return res == null ? null : NormalizeRow(res);
    }

    public async Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, IDbConnection? existingConn = null, IDbTransaction? tx = null)
    {
        if (existingConn != null)
        {
            return await existingConn.ExecuteScalarAsync<T>(sql, param, tx);
        }

        await using var conn = await OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<T>(sql, param);
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null, IDbConnection? existingConn = null, IDbTransaction? tx = null)
    {
        if (existingConn != null)
        {
            return await ExecuteWithSqliteRetryAsync(() => existingConn.ExecuteAsync(sql, param, tx));
        }

        return await ExecuteWithSqliteRetryAsync(async () =>
        {
            await using var conn = await OpenConnectionAsync();
            return await conn.ExecuteAsync(sql, param);
        });
    }

    public async Task<long> InsertAsync(string tableName, IDictionary<string, object?> data, IDbConnection? existingConn = null, IDbTransaction? tx = null)
    {
        var validCols = await GetTableColumnsAsync(tableName);
        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var filtered = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in data)
        {
            if (validCols.Count == 0 || validCols.Contains(kvp.Key))
            {
                filtered[kvp.Key] = PrepareDbValue(kvp.Value);
            }
        }

        if (validCols.Contains("created_at") && !filtered.ContainsKey("created_at"))
        {
            filtered["created_at"] = nowStr;
        }
        if (validCols.Contains("updated_at") && !filtered.ContainsKey("updated_at"))
        {
            filtered["updated_at"] = nowStr;
        }

        var cols = filtered.Keys.ToList();
        var quotedCols = string.Join(", ", cols.Select(c => $"\"{c.Replace("\"", "\"\"")}\""));
        var paramNames = string.Join(", ", cols.Select((c, i) => $"@p{i}"));

        var dynParams = new DynamicParameters();
        for (int i = 0; i < cols.Count; i++)
        {
            dynParams.Add($"p{i}", filtered[cols[i]]);
        }

        var escapedTable = tableName.Replace("\"", "\"\"");
        var sql = $"INSERT INTO \"{escapedTable}\" ({quotedCols}) VALUES ({paramNames}); SELECT last_insert_rowid();";

        if (existingConn != null)
        {
            return await ExecuteWithSqliteRetryAsync(() => existingConn.ExecuteScalarAsync<long>(sql, dynParams, tx));
        }

        return await ExecuteWithSqliteRetryAsync(async () =>
        {
            await using var conn = await OpenConnectionAsync();
            return await conn.ExecuteScalarAsync<long>(sql, dynParams);
        });
    }

    public async Task<int> UpdateAsync(string tableName, object id, IDictionary<string, object?> data, string idColumn = "id", IDbConnection? existingConn = null, IDbTransaction? tx = null)
    {
        var validCols = await GetTableColumnsAsync(tableName);
        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

        var filtered = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in data)
        {
            if (string.Equals(kvp.Key, idColumn, StringComparison.OrdinalIgnoreCase)) continue;
            if (validCols.Count == 0 || validCols.Contains(kvp.Key))
            {
                filtered[kvp.Key] = PrepareDbValue(kvp.Value);
            }
        }

        if (validCols.Contains("updated_at") && !filtered.ContainsKey("updated_at"))
        {
            filtered["updated_at"] = nowStr;
        }

        if (filtered.Count == 0) return 0;

        var cols = filtered.Keys.ToList();
        var setClauses = string.Join(", ", cols.Select((c, i) => $"\"{c.Replace("\"", "\"\"")}\" = @p{i}"));

        var dynParams = new DynamicParameters();
        for (int i = 0; i < cols.Count; i++)
        {
            dynParams.Add($"p{i}", filtered[cols[i]]);
        }
        dynParams.Add("idVal", id);

        var escapedTable = tableName.Replace("\"", "\"\"");
        var escapedIdCol = idColumn.Replace("\"", "\"\"");
        var sql = $"UPDATE \"{escapedTable}\" SET {setClauses} WHERE \"{escapedIdCol}\" = @idVal";

        if (existingConn != null)
        {
            return await ExecuteWithSqliteRetryAsync(() => existingConn.ExecuteAsync(sql, dynParams, tx));
        }

        return await ExecuteWithSqliteRetryAsync(async () =>
        {
            await using var conn = await OpenConnectionAsync();
            return await conn.ExecuteAsync(sql, dynParams);
        });
    }

    public async Task<Dictionary<string, object?>> PaginateAsync(
        string selectSql,
        string countSql,
        object? param,
        int page,
        int perPage,
        Func<List<Dictionary<string, object?>>, Task<List<Dictionary<string, object?>>>>? enrichBatch = null,
        int maxPerPage = 500)
    {
        page = Math.Max(1, page);
        perPage = perPage <= 0 ? 15 : Math.Min(perPage, Math.Max(1, maxPerPage));
        var offset = (page - 1) * perPage;

        await using var conn = await OpenConnectionAsync();
        var total = await conn.ExecuteScalarAsync<int>(countSql, param);

        var dynParams = new DynamicParameters(param);
        dynParams.Add("__limit", perPage);
        dynParams.Add("__offset", offset);

        var pagedSql = $"{selectSql} LIMIT @__limit OFFSET @__offset";
        var rawRows = await conn.QueryAsync(pagedSql, dynParams);
        var items = rawRows.Select(NormalizeRow).ToList();

        if (enrichBatch != null && items.Count > 0)
        {
            items = await enrichBatch(items);
        }

        var lastPage = Math.Max(1, (int)Math.Ceiling(total / (double)perPage));
        var from = total == 0 ? (int?)null : offset + 1;
        var to = total == 0 ? (int?)null : Math.Min(offset + items.Count, total);

        return new Dictionary<string, object?>
        {
            ["data"] = items,
            ["current_page"] = page,
            ["per_page"] = perPage,
            ["total"] = total,
            ["last_page"] = lastPage,
            ["from"] = from,
            ["to"] = to,
            ["meta"] = new Dictionary<string, object?>
            {
                ["current_page"] = page,
                ["per_page"] = perPage,
                ["total"] = total,
                ["last_page"] = lastPage,
                ["from"] = from,
                ["to"] = to
            }
        };
    }

    public static object? PrepareDbValue(object? val)
    {
        if (val == null) return null;
        if (val is bool b) return b ? 1 : 0;
        if (val is DateTime dt) return dt.ToString("yyyy-MM-dd HH:mm:ss");
        if (val is DateTimeOffset dto) return dto.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
        if (val is JsonElement je)
        {
            return je.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                JsonValueKind.Number => je.TryGetInt64(out var l) ? l : je.GetDouble(),
                JsonValueKind.String => je.GetString(),
                _ => je.GetRawText()
            };
        }
        if (val is not string && (val is System.Collections.IEnumerable || val.GetType().IsClass))
        {
            return JsonSerializer.Serialize(val);
        }
        return val;
    }

    public static Dictionary<string, object?> NormalizeRow(dynamic row)
    {
        var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (row is IDictionary<string, object> dapperRow)
        {
            foreach (var kv in dapperRow)
            {
                var key = kv.Key;
                var val = kv.Value;

                if (val is string strVal && JsonColumns.Contains(key))
                {
                    var trimmed = strVal.Trim();
                    if ((trimmed.StartsWith("{") && trimmed.EndsWith("}")) ||
                        (trimmed.StartsWith("[") && trimmed.EndsWith("]")))
                    {
                        try
                        {
                            dict[key] = JsonSerializer.Deserialize<object>(trimmed);
                            continue;
                        }
                        catch
                        {
                        }
                    }
                }

                if (BooleanColumns.Contains(key) && val != null)
                {
                    if (val is long l) dict[key] = l != 0;
                    else if (val is int i) dict[key] = i != 0;
                    else if (val is string s) dict[key] = s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase);
                    else dict[key] = val;
                    continue;
                }

                dict[key] = val;
            }
        }
        return dict;
    }

    // -------------------------------------------------------------------------
    // Spatie Role & Permission Helpers
    // -------------------------------------------------------------------------

    public async Task<List<Dictionary<string, object?>>> GetUserRolesAsync(long userId)
    {
        if (!await HasTableAsync("roles") || !await HasTableAsync("model_has_roles"))
        {
            return new List<Dictionary<string, object?>>();
        }

        const string sql = @"
            SELECT r.id, r.name, r.guard_name
            FROM roles r
            INNER JOIN model_has_roles mhr ON mhr.role_id = r.id
            WHERE mhr.model_id = @userId
            ORDER BY r.id ASC";
        return await QueryListAsync(sql, new { userId });
    }

    public async Task<List<string>> GetUserRoleNamesAsync(long userId)
    {
        var roles = await GetUserRolesAsync(userId);
        return roles
            .Select(r => r.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "")
            .Where(n => !string.IsNullOrEmpty(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<List<Dictionary<string, object?>>> GetUserDirectPermissionsAsync(long userId)
    {
        if (!await HasTableAsync("permissions") || !await HasTableAsync("model_has_permissions"))
        {
            return new List<Dictionary<string, object?>>();
        }

        const string sql = @"
            SELECT p.id, p.name, p.guard_name
            FROM permissions p
            INNER JOIN model_has_permissions mhp ON mhp.permission_id = p.id
            WHERE mhp.model_id = @userId
            ORDER BY p.name ASC";
        return await QueryListAsync(sql, new { userId });
    }

    public async Task<List<string>> GetUserAllPermissionNamesAsync(long userId)
    {
        if (!await HasTableAsync("permissions"))
        {
            return new List<string>();
        }

        var roleNames = await GetUserRoleNamesAsync(userId);
        if (roleNames.Any(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase) ||
                               r.Equals("super-admin", StringComparison.OrdinalIgnoreCase)))
        {
            var allPerms = await QueryListAsync("SELECT name FROM permissions WHERE guard_name = 'sanctum' ORDER BY name ASC");
            if (allPerms.Count == 0)
            {
                allPerms = await QueryListAsync("SELECT DISTINCT name FROM permissions ORDER BY name ASC");
            }
            return allPerms
                .Select(p => p.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "")
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();
        }

        const string sql = @"
            SELECT DISTINCT p.name
            FROM permissions p
            LEFT JOIN model_has_permissions mhp ON mhp.permission_id = p.id AND mhp.model_id = @userId
            LEFT JOIN role_has_permissions rhp ON rhp.permission_id = p.id
            LEFT JOIN model_has_roles mhr ON mhr.role_id = rhp.role_id AND mhr.model_id = @userId
            WHERE mhp.model_id IS NOT NULL OR mhr.model_id IS NOT NULL
            ORDER BY p.name ASC";

        var rows = await QueryListAsync(sql, new { userId });
        return rows
            .Select(r => r.TryGetValue("name", out var n) ? n?.ToString() ?? "" : "")
            .Where(n => !string.IsNullOrEmpty(n))
            .ToList();
    }

    public async Task<bool> UserHasRoleAsync(long userId, params string[] roles)
    {
        if (roles == null || roles.Length == 0) return true;
        var userRoles = await GetUserRoleNamesAsync(userId);
        return roles.Any(req => userRoles.Any(ur => ur.Equals(req, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task<bool> UserCanAsync(long userId, params string[] permissions)
    {
        if (permissions == null || permissions.Length == 0) return true;
        if (await UserHasRoleAsync(userId, "Admin", "admin", "super-admin"))
        {
            return true;
        }

        var userPerms = await GetUserAllPermissionNamesAsync(userId);
        return permissions.Any(req => userPerms.Any(up => up.Equals(req, StringComparison.OrdinalIgnoreCase)));
    }

    public async Task SyncUserRolesAsync(long userId, IEnumerable<string> roleNames)
    {
        if (!await HasTableAsync("roles") || !await HasTableAsync("model_has_roles")) return;

        await using var conn = await OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync("DELETE FROM model_has_roles WHERE model_id = @userId", new { userId }, tx);

        var distinctNames = roleNames.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).Distinct().ToArray();
        if (distinctNames.Length > 0)
        {
            var roles = await conn.QueryAsync<(long id, string name)>(
                "SELECT id, name FROM roles WHERE name IN @names",
                new { names = distinctNames },
                tx);

            foreach (var role in roles)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO model_has_roles (role_id, model_type, model_id) VALUES (@roleId, 'App\\Models\\User', @userId)",
                    new { roleId = role.id, userId },
                    tx);
            }
        }

        await tx.CommitAsync();
    }

    public async Task SyncUserPermissionsAsync(long userId, IEnumerable<string> permissionNames)
    {
        if (!await HasTableAsync("permissions") || !await HasTableAsync("model_has_permissions")) return;

        await using var conn = await OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await conn.ExecuteAsync("DELETE FROM model_has_permissions WHERE model_id = @userId", new { userId }, tx);

        var distinctNames = permissionNames.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct().ToArray();
        if (distinctNames.Length > 0)
        {
            var perms = await conn.QueryAsync<(long id, string name)>(
                "SELECT id, name FROM permissions WHERE name IN @names",
                new { names = distinctNames },
                tx);

            foreach (var perm in perms)
            {
                await conn.ExecuteAsync(
                    "INSERT INTO model_has_permissions (permission_id, model_type, model_id) VALUES (@permId, 'App\\Models\\User', @userId)",
                    new { permId = perm.id, userId },
                    tx);
            }
        }

        await tx.CommitAsync();
    }

    public async Task<Dictionary<string, object?>?> GetUserWithRolesAndPermissionsAsync(long userId, bool includePinCode = false)
    {
        var user = await QuerySingleOrDefaultAsync("SELECT * FROM users WHERE id = @userId", new { userId });
        if (user == null) return null;

        user.Remove("password");
        user.Remove("remember_token");
        if (!includePinCode)
        {
            user.Remove("pin_code");
        }

        user["roles"] = await GetUserRolesAsync(userId);
        user["permissions"] = await GetUserDirectPermissionsAsync(userId);
        return user;
    }
}

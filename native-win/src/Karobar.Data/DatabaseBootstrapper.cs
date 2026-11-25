using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Data.Common;
using System.Reflection;
using Dapper;
using Karobar.Core.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace Karobar.Data;

public sealed class SchemaVerificationResult
{
    public bool IsCompatible => MissingTables.Count == 0 && MissingColumns.Count == 0 && DbSetErrors.Count == 0;
    public string SqlitePath { get; init; } = string.Empty;
    public int VerifiedTableCount { get; set; }
    public int VerifiedColumnCount { get; set; }
    public int VerifiedDbSetCount { get; set; }
    public List<string> MissingTables { get; } = new();
    public List<string> MissingColumns { get; } = new();
    public List<string> DbSetErrors { get; } = new();
}

public sealed class DbConnectionFactory : IDbConnectionFactory
{
    public string ProjectRoot { get; }
    public string SqlitePath { get; }
    public string ConnectionString { get; }
    public bool IsMySql { get; }

    public DbConnectionFactory(IConfiguration? configuration = null, string? explicitSqlitePath = null)
    {
        ProjectRoot = DatabaseBootstrapper.ResolveProjectRootDirectory();
        var configuredProvider = configuration?["Database:Provider"]
                                 ?? Environment.GetEnvironmentVariable("DB_CONNECTION")
                                 ?? "sqlite";

        IsMySql = configuredProvider.Equals("mysql", StringComparison.OrdinalIgnoreCase) ||
                  configuredProvider.Equals("mariadb", StringComparison.OrdinalIgnoreCase);

        SqlitePath = DatabaseBootstrapper.ResolveSqlitePath(explicitSqlitePath, configuration, ProjectRoot);

        if (IsMySql)
        {
            ConnectionString = configuration?.GetConnectionString("MySqlConnection")
                               ?? configuration?.GetConnectionString("DefaultConnection")
                               ?? Environment.GetEnvironmentVariable("KAROBAR_MYSQL_CONNECTION")
                               ?? string.Empty;
        }
        else
        {
            ConnectionString = DatabaseBootstrapper.BuildSqliteConnectionString(SqlitePath);
        }
    }

    public DbConnection CreateConnection()
    {
        if (IsMySql && !string.IsNullOrWhiteSpace(ConnectionString))
        {
            var mySqlConn = new MySqlConnection(ConnectionString);
            mySqlConn.Open();
            return mySqlConn;
        }

        EnsureDirectoryExists(SqlitePath);
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        SqliteConnectionInterceptor.ApplyPragmas(conn);
        return conn;
    }

    public async Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (IsMySql && !string.IsNullOrWhiteSpace(ConnectionString))
        {
            var mySqlConn = new MySqlConnection(ConnectionString);
            await mySqlConn.OpenAsync(cancellationToken);
            return mySqlConn;
        }

        EnsureDirectoryExists(SqlitePath);
        var conn = new SqliteConnection(ConnectionString);
        await conn.OpenAsync(cancellationToken);
        await SqliteConnectionInterceptor.ApplyPragmasAsync(conn, cancellationToken);
        return conn;
    }

    public async Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, IDbTransaction? transaction = null)
    {
        if (transaction?.Connection != null)
        {
            return await transaction.Connection.QueryAsync<T>(sql, param, transaction);
        }

        await using var conn = await OpenConnectionAsync();
        return await conn.QueryAsync<T>(sql, param);
    }

    public async Task<IEnumerable<dynamic>> QueryAsync(string sql, object? param = null, IDbTransaction? transaction = null)
    {
        if (transaction?.Connection != null)
        {
            return await transaction.Connection.QueryAsync(sql, param, transaction);
        }

        await using var conn = await OpenConnectionAsync();
        return await conn.QueryAsync(sql, param);
    }

    public async Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, IDbTransaction? transaction = null)
    {
        if (transaction?.Connection != null)
        {
            return await transaction.Connection.QueryFirstOrDefaultAsync<T>(sql, param, transaction);
        }

        await using var conn = await OpenConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync<T>(sql, param);
    }

    public async Task<dynamic?> QueryFirstOrDefaultAsync(string sql, object? param = null, IDbTransaction? transaction = null)
    {
        if (transaction?.Connection != null)
        {
            return await transaction.Connection.QueryFirstOrDefaultAsync(sql, param, transaction);
        }

        await using var conn = await OpenConnectionAsync();
        return await conn.QueryFirstOrDefaultAsync(sql, param);
    }

    public async Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, IDbTransaction? transaction = null)
    {
        if (transaction?.Connection != null)
        {
            return await transaction.Connection.ExecuteScalarAsync<T>(sql, param, transaction);
        }

        await using var conn = await OpenConnectionAsync();
        return await conn.ExecuteScalarAsync<T>(sql, param);
    }

    public async Task<int> ExecuteAsync(string sql, object? param = null, IDbTransaction? transaction = null)
    {
        if (transaction?.Connection != null)
        {
            return await transaction.Connection.ExecuteAsync(sql, param, transaction);
        }

        await using var conn = await OpenConnectionAsync();
        return await conn.ExecuteAsync(sql, param);
    }

    private static void EnsureDirectoryExists(string sqlitePath)
    {
        var dir = Path.GetDirectoryName(sqlitePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }
}

public sealed class DatabaseBootstrapper
{
    private readonly IDbConnectionFactory _connectionFactory;

    public DatabaseBootstrapper(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public string SqlitePath => _connectionFactory.SqlitePath;
    public string ProjectRoot => _connectionFactory.ProjectRoot;

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
            if ((Directory.Exists(Path.Combine(dir.FullName, "database")) &&
                 Directory.Exists(Path.Combine(dir.FullName, "public"))) ||
                File.Exists(Path.Combine(dir.FullName, "database", "database.sqlite")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (baseDir != null)
        {
            if ((Directory.Exists(Path.Combine(baseDir.FullName, "database")) &&
                 Directory.Exists(Path.Combine(baseDir.FullName, "public"))) ||
                File.Exists(Path.Combine(baseDir.FullName, "database", "database.sqlite")))
            {
                return baseDir.FullName;
            }
            baseDir = baseDir.Parent;
        }

        return current;
    }

    public static string ResolveSqlitePath(string? explicitPath = null, IConfiguration? config = null, string? projectRoot = null)
    {
        projectRoot ??= ResolveProjectRootDirectory();

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.IsPathRooted(explicitPath) ? explicitPath : Path.Combine(projectRoot, explicitPath);
        }

        var envDb = Environment.GetEnvironmentVariable("KAROBAR_DB_PATH")
                    ?? Environment.GetEnvironmentVariable("DB_DATABASE");
        if (!string.IsNullOrWhiteSpace(envDb) && !envDb.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
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

    public static string BuildSqliteConnectionString(string sqlitePath)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = sqlitePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
            DefaultTimeout = 20
        }.ToString();
    }

    public static DbContextOptions<KarobarDbContext> CreateSqliteOptions(string? sqlitePath = null)
    {
        var resolvedPath = ResolveSqlitePath(sqlitePath);
        var connStr = BuildSqliteConnectionString(resolvedPath);
        var builder = new DbContextOptionsBuilder<KarobarDbContext>();
        builder.UseSqlite(connStr);
        builder.AddInterceptors(new SqliteConnectionInterceptor());
        return builder.Options;
    }

    public static KarobarDbContext CreateDbContext(string? sqlitePath = null)
    {
        return new KarobarDbContext(CreateSqliteOptions(sqlitePath));
    }

    public async Task EnsureSchemaReadyAsync(CancellationToken cancellationToken = default)
    {
        await using var context = CreateDbContext(_connectionFactory.SqlitePath);
        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        var tableCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'");

        if (tableCount == 0)
        {
            await context.Database.EnsureCreatedAsync(cancellationToken);
            await EnsurePerformanceIndexesAsync(conn, cancellationToken);
            await ApplyNativeWhatsAppPortMigrationAsync(conn, cancellationToken);
            await SeedTradeSchemePermissionsAsync(conn, cancellationToken);
            return;
        }

        // Non-destructive schema upgrade for existing / imported SQLite databases:
        // 1) Create any missing tables defined in KarobarDbContext
        // 2) Add any missing columns via safe ALTER TABLE ... ADD COLUMN statements
        var existingTables = (await conn.QueryAsync<string>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Fetch all existing columns across all tables in a single query instead of 79+ PRAGMA round trips.
        var columnsByTable = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var allColsRows = await conn.QueryAsync<(string tbl, string col)>(
                "SELECT m.name AS tbl, p.name AS col FROM sqlite_master m JOIN pragma_table_info(m.name) p WHERE m.type='table' AND m.name NOT LIKE 'sqlite_%'");
            foreach (var (tbl, col) in allColsRows)
            {
                if (string.IsNullOrWhiteSpace(tbl) || string.IsNullOrWhiteSpace(col)) continue;
                if (!columnsByTable.TryGetValue(tbl, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    columnsByTable[tbl] = set;
                }
                set.Add(col);
            }
        }
        catch
        {
            // Fallback to per-table PRAGMA table_info below if pragma_table_info() table-valued function is unavailable.
        }

        foreach (var entityType in context.Model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName();
            if (string.IsNullOrWhiteSpace(tableName)) continue;

            var escapedTable = tableName.Replace("\"", "\"\"");

            if (!existingTables.Contains(tableName))
            {
                var colDefs = new List<string>();
                var pkProps = entityType.FindPrimaryKey()?.Properties ?? [];
                var singleIntPk = pkProps.Count == 1 &&
                                  (pkProps[0].ClrType == typeof(long) || pkProps[0].ClrType == typeof(int));

                foreach (var prop in entityType.GetProperties())
                {
                    var colName = prop.GetColumnName();
                    if (string.IsNullOrWhiteSpace(colName)) continue;
                    var escapedCol = colName.Replace("\"", "\"\"");
                    var sqlType = MapClrTypeToSqlite(prop.ClrType);

                    if (singleIntPk && prop == pkProps[0])
                    {
                        colDefs.Add($"\"{escapedCol}\" INTEGER PRIMARY KEY AUTOINCREMENT");
                    }
                    else
                    {
                        var defClause = BuildSqliteDefaultClause(prop);
                        colDefs.Add($"\"{escapedCol}\" {sqlType}{defClause}");
                    }
                }

                if (!singleIntPk && pkProps.Count > 0)
                {
                    var pkCols = string.Join(", ", pkProps.Select(p => $"\"{p.GetColumnName().Replace("\"", "\"\"")}\""));
                    colDefs.Add($"PRIMARY KEY ({pkCols})");
                }

                if (colDefs.Count > 0)
                {
                    var createSql = $"CREATE TABLE IF NOT EXISTS \"{escapedTable}\" ({string.Join(", ", colDefs)});";
                    try
                    {
                        await conn.ExecuteAsync(createSql);
                        existingTables.Add(tableName);

                        // Also create EF Core indexes for newly created tables on upgraded databases
                        foreach (var idx in entityType.GetIndexes())
                        {
                            var idxCols = idx.Properties
                                .Select(p => p.GetColumnName())
                                .Where(c => !string.IsNullOrWhiteSpace(c))
                                .Select(c => $"\"{c!.Replace("\"", "\"\"")}\"")
                                .ToList();
                            if (idxCols.Count == 0) continue;

                            var idxName = idx.GetDatabaseName()
                                ?? $"IX_{tableName}_{string.Join("_", idx.Properties.Select(p => p.GetColumnName()))}";
                            var escapedIdx = idxName.Replace("\"", "\"\"");
                            var uniqueKw = idx.IsUnique ? "UNIQUE " : "";
                            try
                            {
                                await conn.ExecuteAsync($"CREATE {uniqueKw}INDEX IF NOT EXISTS \"{escapedIdx}\" ON \"{escapedTable}\" ({string.Join(", ", idxCols)});");
                            }
                            catch { }
                        }
                    }
                    catch
                    {
                        // Non-fatal if table already created concurrently
                    }
                }
                continue;
            }

            // Existing table: inspect columns and add any missing columns non-destructively
            if (!columnsByTable.TryGetValue(tableName, out var actualCols))
            {
                actualCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var pragmaRows = await conn.QueryAsync($"PRAGMA table_info(\"{escapedTable}\")");
                foreach (var row in pragmaRows)
                {
                    if (row is IDictionary<string, object> dict &&
                        dict.TryGetValue("name", out var colNameObj) &&
                        colNameObj != null)
                    {
                        actualCols.Add(colNameObj.ToString()!);
                    }
                }
            }

            foreach (var prop in entityType.GetProperties())
            {
                var colName = prop.GetColumnName();
                if (string.IsNullOrWhiteSpace(colName) || actualCols.Contains(colName)) continue;

                var escapedCol = colName.Replace("\"", "\"\"");
                var sqlType = MapClrTypeToSqlite(prop.ClrType);
                var defClause = BuildSqliteDefaultClause(prop);
                var alterSql = $"ALTER TABLE \"{escapedTable}\" ADD COLUMN \"{escapedCol}\" {sqlType}{defClause};";
                try
                {
                    await conn.ExecuteAsync(alterSql);
                    actualCols.Add(colName);
                }
                catch
                {
                    // Ignore if column cannot be added or already exists
                }
            }
        }

        await EnsurePurchaseInvoiceUniqueIndexAsync(conn, cancellationToken);
        await EnsurePerformanceIndexesAsync(conn, cancellationToken);
        await ApplyNativeWhatsAppPortMigrationAsync(conn, cancellationToken);
        await SeedTradeSchemePermissionsAsync(conn, cancellationToken);
    }

    private static async Task EnsurePerformanceIndexesAsync(DbConnection conn, CancellationToken cancellationToken)
    {
        var specs = new (string Table, string[] RequiredCols, string Sql)[]
        {
            ("sale_invoices", ["date", "status"],
                "CREATE INDEX IF NOT EXISTS \"IX_sale_invoices_date_status\" ON \"sale_invoices\" (\"date\", \"status\");"),
            ("purchase_invoices", ["posted_date", "status"],
                "CREATE INDEX IF NOT EXISTS \"IX_purchase_invoices_posted_date_status\" ON \"purchase_invoices\" (\"posted_date\", \"status\");"),
            ("products", ["name"],
                "CREATE INDEX IF NOT EXISTS \"IX_products_name_nocase\" ON \"products\" (\"name\" COLLATE NOCASE);"),
            ("batches", ["expiry_date", "quantity"],
                "CREATE INDEX IF NOT EXISTS \"IX_batches_expiry_qty\" ON \"batches\" (\"expiry_date\", \"quantity\") WHERE \"quantity\" > 0;")
        };

        foreach (var (table, reqCols, sql) in specs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var hasTable = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name = @name",
                    new { name = table }) > 0;
                if (!hasTable) continue;

                var escapedTable = table.Replace("\"", "\"\"");
                var actualCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in await conn.QueryAsync($"PRAGMA table_info(\"{escapedTable}\")"))
                {
                    if (row is IDictionary<string, object> dict &&
                        dict.TryGetValue("name", out var nameObj) &&
                        nameObj != null)
                    {
                        actualCols.Add(nameObj.ToString()!);
                    }
                }

                if (reqCols.All(c => actualCols.Contains(c)))
                {
                    await conn.ExecuteAsync(sql);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Minimal / custom test DBs should not fail schema bootstrap.
            }
        }
    }

    public const string MigrateNativeWhatsAppPort3002MigrationName = "2026_11_22_000001_migrate_native_whatsapp_agent_port_3002";

    private static async Task ApplyNativeWhatsAppPortMigrationAsync(DbConnection conn, CancellationToken cancellationToken)
    {
        try
        {
            var hasMigrations = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='migrations'") > 0;
            if (hasMigrations)
            {
                var alreadyApplied = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM \"migrations\" WHERE \"migration\" = @migration",
                    new { migration = MigrateNativeWhatsAppPort3002MigrationName }) > 0;
                if (alreadyApplied)
                {
                    return;
                }
            }

            var hasSettings = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='settings'") > 0;
            if (hasSettings)
            {
                var settingsCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var row in await conn.QueryAsync("PRAGMA table_info(\"settings\")"))
                {
                    if (row is IDictionary<string, object> dict &&
                        dict.TryGetValue("name", out var nameObj) &&
                        nameObj != null)
                    {
                        settingsCols.Add(nameObj.ToString()!);
                    }
                }

                if (settingsCols.Contains("whatsapp_api_url"))
                {
                    if (settingsCols.Contains("whatsapp_provider"))
                    {
                        await conn.ExecuteAsync(@"
                            UPDATE ""settings""
                            SET ""whatsapp_api_url"" = 'http://127.0.0.1:3002'
                            WHERE (""whatsapp_provider"" IS NULL OR TRIM(""whatsapp_provider"") = '' OR LOWER(TRIM(""whatsapp_provider"")) = 'local_agent')
                              AND LOWER(TRIM(COALESCE(""whatsapp_api_url"", ''))) IN (
                                  'http://127.0.0.1:3001',
                                  'http://localhost:3001',
                                  'http://127.0.0.1:3001/',
                                  'http://localhost:3001/'
                              );");
                    }
                    else
                    {
                        await conn.ExecuteAsync(@"
                            UPDATE ""settings""
                            SET ""whatsapp_api_url"" = 'http://127.0.0.1:3002'
                            WHERE LOWER(TRIM(COALESCE(""whatsapp_api_url"", ''))) IN (
                                  'http://127.0.0.1:3001',
                                  'http://localhost:3001',
                                  'http://127.0.0.1:3001/',
                                  'http://localhost:3001/'
                              );");
                    }
                }
            }

            if (hasMigrations)
            {
                var nextBatch = await conn.ExecuteScalarAsync<int>(
                    "SELECT COALESCE(MAX(\"batch\"), 0) + 1 FROM \"migrations\"");
                await conn.ExecuteAsync(
                    "INSERT INTO \"migrations\" (\"migration\", \"batch\") VALUES (@migration, @batch);",
                    new { migration = MigrateNativeWhatsAppPort3002MigrationName, batch = nextBatch });
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Minimal / custom test DBs without expected migration metadata should not fail schema bootstrap.
        }
    }

    private static async Task EnsurePurchaseInvoiceUniqueIndexAsync(DbConnection conn, CancellationToken cancellationToken)
    {
        try
        {
            var hasTable = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='purchase_invoices'") > 0;
            if (!hasTable)
            {
                return;
            }

            var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in await conn.QueryAsync("PRAGMA table_info(\"purchase_invoices\")"))
            {
                if (row is IDictionary<string, object> dict &&
                    dict.TryGetValue("name", out var nameObj) &&
                    nameObj != null)
                {
                    cols.Add(nameObj.ToString()!);
                }
            }

            if (!cols.Contains("supplier_id") || !cols.Contains("invoice_number"))
            {
                return;
            }

            foreach (var idxRow in await conn.QueryAsync("PRAGMA index_list(\"purchase_invoices\")"))
            {
                if (idxRow is not IDictionary<string, object> idxDict) continue;
                var isUnique = idxDict.TryGetValue("unique", out var uObj) &&
                               uObj != null &&
                               Convert.ToInt64(uObj, System.Globalization.CultureInfo.InvariantCulture) == 1;
                if (!isUnique) continue;

                var idxName = idxDict.TryGetValue("name", out var nObj) ? nObj?.ToString() : null;
                if (string.IsNullOrWhiteSpace(idxName)) continue;

                var escapedIdx = idxName.Replace("\"", "\"\"");
                var idxCols = new List<string>();
                foreach (var infoRow in await conn.QueryAsync($"PRAGMA index_info(\"{escapedIdx}\")"))
                {
                    if (infoRow is IDictionary<string, object> infoDict &&
                        infoDict.TryGetValue("name", out var colObj) &&
                        colObj != null)
                    {
                        idxCols.Add(colObj.ToString()!);
                    }
                }

                if (idxCols.Count == 2 &&
                    ((idxCols[0].Equals("supplier_id", StringComparison.OrdinalIgnoreCase) &&
                      idxCols[1].Equals("invoice_number", StringComparison.OrdinalIgnoreCase)) ||
                     (idxCols[0].Equals("invoice_number", StringComparison.OrdinalIgnoreCase) &&
                      idxCols[1].Equals("supplier_id", StringComparison.OrdinalIgnoreCase))))
                {
                    return;
                }
            }

            var hasDuplicate = await conn.ExecuteScalarAsync<int?>(@"
                SELECT 1
                FROM ""purchase_invoices""
                WHERE ""supplier_id"" IS NOT NULL
                  AND ""invoice_number"" IS NOT NULL
                GROUP BY ""supplier_id"", ""invoice_number""
                HAVING COUNT(*) > 1
                LIMIT 1;");
            if (hasDuplicate.HasValue)
            {
                return;
            }

            await conn.ExecuteAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_purchase_invoices_supplier_id_invoice_number\" ON \"purchase_invoices\" (\"supplier_id\", \"invoice_number\");");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Minimal / custom test DBs or locked legacy schemas should not fail bootstrap.
        }
    }

    private static async Task SeedTradeSchemePermissionsAsync(DbConnection conn, CancellationToken cancellationToken)
    {
        try
        {
            var hasPermissions = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name='permissions'") > 0;
            if (!hasPermissions)
            {
                return;
            }

            var permCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in await conn.QueryAsync("PRAGMA table_info(\"permissions\")"))
            {
                if (row is IDictionary<string, object> dict &&
                    dict.TryGetValue("name", out var nameObj) &&
                    nameObj != null)
                {
                    permCols.Add(nameObj.ToString()!);
                }
            }

            if (!permCols.Contains("name") || !permCols.Contains("guard_name"))
            {
                return;
            }

            var hasCreatedAt = permCols.Contains("created_at");
            var hasUpdatedAt = permCols.Contains("updated_at");
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

            foreach (var permName in new[] { "trade-scheme.view", "trade-scheme.manage" })
            {
                var exists = await conn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM \"permissions\" WHERE LOWER(\"name\") = LOWER(@name) AND \"guard_name\" = @guardName",
                    new { name = permName, guardName = "sanctum" }) > 0;
                if (exists)
                {
                    continue;
                }

                if (hasCreatedAt && hasUpdatedAt)
                {
                    await conn.ExecuteAsync(
                        "INSERT OR IGNORE INTO \"permissions\" (\"name\", \"guard_name\", \"created_at\", \"updated_at\") VALUES (@name, @guardName, @now, @now);",
                        new { name = permName, guardName = "sanctum", now });
                }
                else if (hasCreatedAt)
                {
                    await conn.ExecuteAsync(
                        "INSERT OR IGNORE INTO \"permissions\" (\"name\", \"guard_name\", \"created_at\") VALUES (@name, @guardName, @now);",
                        new { name = permName, guardName = "sanctum", now });
                }
                else
                {
                    await conn.ExecuteAsync(
                        "INSERT OR IGNORE INTO \"permissions\" (\"name\", \"guard_name\") VALUES (@name, @guardName);",
                        new { name = permName, guardName = "sanctum" });
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Minimal / custom test DBs should not fail schema bootstrap.
        }
    }

    private static string MapClrTypeToSqlite(Type clrType)
    {
        var underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (underlying == typeof(bool) || underlying == typeof(int) || underlying == typeof(long) ||
            underlying == typeof(short) || underlying == typeof(byte))
        {
            return "INTEGER";
        }

        if (underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float))
        {
            return "REAL";
        }

        return "TEXT";
    }

    private static string BuildSqliteDefaultClause(Microsoft.EntityFrameworkCore.Metadata.IProperty prop)
    {
        var defSql = prop.GetDefaultValueSql();
        if (!string.IsNullOrWhiteSpace(defSql))
        {
            return $" DEFAULT {defSql}";
        }

        var defVal = prop.GetDefaultValue();
        if (defVal != null)
        {
            return defVal switch
            {
                bool b => $" DEFAULT {(b ? 1 : 0)}",
                int or long or short or byte => $" DEFAULT {Convert.ToInt64(defVal, System.Globalization.CultureInfo.InvariantCulture)}",
                decimal or double or float => $" DEFAULT {Convert.ToDouble(defVal, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                string s => $" DEFAULT '{s.Replace("'", "''")}'",
                _ => ""
            };
        }

        var underlying = Nullable.GetUnderlyingType(prop.ClrType);
        if (underlying != null || !prop.ClrType.IsValueType)
        {
            return " DEFAULT NULL";
        }

        if (prop.ClrType == typeof(bool) || prop.ClrType == typeof(int) || prop.ClrType == typeof(long) ||
            prop.ClrType == typeof(decimal) || prop.ClrType == typeof(double) || prop.ClrType == typeof(float))
        {
            return " DEFAULT 0";
        }

        return "";
    }

    public void EnsureSchemaReady() => EnsureSchemaReadyAsync().GetAwaiter().GetResult();

    public async Task<SchemaVerificationResult> VerifySchemaCompatibilityAsync(CancellationToken cancellationToken = default)
    {
        var result = new SchemaVerificationResult
        {
            SqlitePath = _connectionFactory.SqlitePath
        };

        await using var conn = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var existingTables = (await conn.QueryAsync<string>(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var entityTypes = typeof(User).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsNested: false } &&
                        t.Namespace == "Karobar.Core.Entities" &&
                        t.GetCustomAttribute<TableAttribute>() != null)
            .ToList();

        foreach (var entityType in entityTypes)
        {
            var tableAttr = entityType.GetCustomAttribute<TableAttribute>();
            if (tableAttr == null) continue;

            var tableName = tableAttr.Name;
            if (!existingTables.Contains(tableName))
            {
                result.MissingTables.Add(tableName);
                continue;
            }

            result.VerifiedTableCount++;

            var escapedTable = tableName.Replace("\"", "\"\"");
            var pragmaRows = await conn.QueryAsync($"PRAGMA table_info(\"{escapedTable}\")");
            var actualCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in pragmaRows)
            {
                if (row is IDictionary<string, object> dict && dict.TryGetValue("name", out var colNameObj) && colNameObj != null)
                {
                    actualCols.Add(colNameObj.ToString()!);
                }
            }

            foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetCustomAttribute<NotMappedAttribute>() != null) continue;
                var colAttr = prop.GetCustomAttribute<ColumnAttribute>();
                if (colAttr == null) continue;

                var expectedCol = colAttr.Name ?? prop.Name;
                if (!actualCols.Contains(expectedCol))
                {
                    result.MissingColumns.Add($"{tableName}.{expectedCol}");
                }
                else
                {
                    result.VerifiedColumnCount++;
                }
            }
        }

        await using var dbContext = CreateDbContext(_connectionFactory.SqlitePath);
        var setMethod = typeof(DbContext)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethod && m.GetParameters().Length == 0);
        var takeMethod = typeof(Queryable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Queryable.Take) &&
                        m.GetParameters().Length == 2 &&
                        m.GetParameters()[1].ParameterType == typeof(int));
        var toListMethod = typeof(Enumerable)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Enumerable.ToList) && m.GetParameters().Length == 1);

        foreach (var entityType in entityTypes)
        {
            try
            {
                var queryable = setMethod.MakeGenericMethod(entityType).Invoke(dbContext, null)!;
                var taken = takeMethod.MakeGenericMethod(entityType).Invoke(null, [queryable, 5])!;
                _ = toListMethod.MakeGenericMethod(entityType).Invoke(null, [taken]);
                result.VerifiedDbSetCount++;
            }
            catch (Exception ex)
            {
                var inner = ex.InnerException ?? ex;
                result.DbSetErrors.Add($"{entityType.Name}: {inner.Message}");
            }
        }

        return result;
    }

    public SchemaVerificationResult VerifySchemaCompatibility() =>
        VerifySchemaCompatibilityAsync().GetAwaiter().GetResult();
}

public static class KarobarDataServiceCollectionExtensions
{
    public static IServiceCollection AddKarobarData(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        string? explicitSqlitePath = null)
    {
        services.AddSingleton<SqliteConnectionInterceptor>();
        services.AddSingleton<IDbConnectionFactory>(sp =>
            new DbConnectionFactory(
                configuration ?? sp.GetService<IConfiguration>(),
                explicitSqlitePath));
        services.AddSingleton<DatabaseBootstrapper>();

        services.AddDbContext<KarobarDbContext>((sp, options) =>
        {
            var factory = sp.GetRequiredService<IDbConnectionFactory>();
            var interceptor = sp.GetRequiredService<SqliteConnectionInterceptor>();
            options.UseSqlite(factory.ConnectionString);
            options.AddInterceptors(interceptor);
        });

        return services;
    }
}

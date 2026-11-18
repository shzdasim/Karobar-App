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
            if (File.Exists(Path.Combine(dir.FullName, "artisan")) ||
                File.Exists(Path.Combine(dir.FullName, "database", "database.sqlite")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        var baseDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (baseDir != null)
        {
            if (File.Exists(Path.Combine(baseDir.FullName, "artisan")) ||
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
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
            DefaultTimeout = 15
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
        }
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

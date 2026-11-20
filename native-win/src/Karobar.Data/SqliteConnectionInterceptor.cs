using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Karobar.Data;

public sealed class SqliteConnectionInterceptor : DbConnectionInterceptor
{
    private static readonly ConcurrentDictionary<string, bool> WalInitializedPaths = new(StringComparer.OrdinalIgnoreCase);

    public const string HighConcurrencyPragmaSql =
        "PRAGMA busy_timeout=15000; " +
        "PRAGMA journal_mode=WAL; " +
        "PRAGMA synchronous=NORMAL; " +
        "PRAGMA temp_store=MEMORY; " +
        "PRAGMA cache_size=-64000; " +
        "PRAGMA foreign_keys=ON;";

    public static void ResetWalCache(string? dbPath = null)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            WalInitializedPaths.Clear();
        }
        else
        {
            WalInitializedPaths.TryRemove(dbPath, out _);
        }
    }

    private const string FastSessionPragmaSql =
        "PRAGMA busy_timeout=15000; " +
        "PRAGMA synchronous=NORMAL; " +
        "PRAGMA temp_store=MEMORY; " +
        "PRAGMA cache_size=-64000; " +
        "PRAGMA foreign_keys=ON;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplyPragmas(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyPragmasAsync(connection, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public static void ApplyPragmas(DbConnection connection)
    {
        if (connection is not SqliteConnection sqliteConn)
        {
            return;
        }

        var dbKey = sqliteConn.DataSource ?? string.Empty;
        var needsWal = string.IsNullOrEmpty(dbKey) || !WalInitializedPaths.ContainsKey(dbKey);

        using var command = sqliteConn.CreateCommand();
        command.CommandText = needsWal ? HighConcurrencyPragmaSql : FastSessionPragmaSql;
        command.ExecuteNonQuery();

        if (needsWal && !string.IsNullOrEmpty(dbKey))
        {
            WalInitializedPaths[dbKey] = true;
        }
    }

    public static async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        if (connection is not SqliteConnection sqliteConn)
        {
            return;
        }

        var dbKey = sqliteConn.DataSource ?? string.Empty;
        var needsWal = string.IsNullOrEmpty(dbKey) || !WalInitializedPaths.ContainsKey(dbKey);

        await using var command = sqliteConn.CreateCommand();
        command.CommandText = needsWal ? HighConcurrencyPragmaSql : FastSessionPragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (needsWal && !string.IsNullOrEmpty(dbKey))
        {
            WalInitializedPaths[dbKey] = true;
        }
    }
}

using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Karobar.Data;

public sealed class SqliteConnectionInterceptor : DbConnectionInterceptor
{
    public const string HighConcurrencyPragmaSql =
        "PRAGMA journal_mode=WAL; " +
        "PRAGMA synchronous=NORMAL; " +
        "PRAGMA busy_timeout=10000; " +
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
        if (connection is not SqliteConnection)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = HighConcurrencyPragmaSql;
        command.ExecuteNonQuery();
    }

    public static async Task ApplyPragmasAsync(DbConnection connection, CancellationToken cancellationToken = default)
    {
        if (connection is not SqliteConnection)
        {
            return;
        }

        await using var command = connection.CreateCommand();
        command.CommandText = HighConcurrencyPragmaSql;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

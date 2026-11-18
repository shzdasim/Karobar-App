using System.Data;
using System.Data.Common;

namespace Karobar.Data;

public interface IDbConnectionFactory
{
    string ProjectRoot { get; }
    string SqlitePath { get; }
    string ConnectionString { get; }
    bool IsMySql { get; }

    DbConnection CreateConnection();
    Task<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);

    Task<IEnumerable<T>> QueryAsync<T>(string sql, object? param = null, IDbTransaction? transaction = null);
    Task<IEnumerable<dynamic>> QueryAsync(string sql, object? param = null, IDbTransaction? transaction = null);
    Task<T?> QueryFirstOrDefaultAsync<T>(string sql, object? param = null, IDbTransaction? transaction = null);
    Task<dynamic?> QueryFirstOrDefaultAsync(string sql, object? param = null, IDbTransaction? transaction = null);
    Task<T?> ExecuteScalarAsync<T>(string sql, object? param = null, IDbTransaction? transaction = null);
    Task<int> ExecuteAsync(string sql, object? param = null, IDbTransaction? transaction = null);
}

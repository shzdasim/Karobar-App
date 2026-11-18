using System.Text.RegularExpressions;
using Dapper;
using Karobar.Services.Infrastructure;

namespace Karobar.Services;

public class DocumentSequenceService
{
    private readonly KarobarDb _db;
    private static readonly SemaphoreSlim SequenceLock = new(1, 1);

    public DocumentSequenceService(KarobarDb db)
    {
        _db = db;
    }

    public async Task EnsureTableExistsAsync()
    {
        if (await _db.HasTableAsync("document_sequences")) return;

        try
        {
            await _db.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS document_sequences (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    sequence_key VARCHAR(80) NOT NULL UNIQUE,
                    prefix VARCHAR(40) NOT NULL,
                    padding INTEGER NOT NULL DEFAULT 4,
                    last_number INTEGER NOT NULL DEFAULT 0,
                    created_at DATETIME NULL,
                    updated_at DATETIME NULL
                );");
            KarobarDb.FlushSchemaCache();
        }
        catch
        {
        }
    }

    public async Task<string> NextAsync(
        string sequenceKey,
        string prefix,
        int padding = 4,
        string? seedTable = null,
        string? seedColumn = null,
        bool seedFromMaxId = false)
    {
        await EnsureTableExistsAsync();
        await SequenceLock.WaitAsync();
        try
        {
            await using var conn = await _db.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM document_sequences WHERE sequence_key = @key",
                new { key = sequenceKey },
                tx);

            var existingMax = await ResolveExistingTableMaxAsync(prefix, seedTable, seedColumn, seedFromMaxId);

            if (row == null)
            {
                var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
                await conn.ExecuteAsync(@"
                    INSERT OR IGNORE INTO document_sequences (sequence_key, prefix, padding, last_number, created_at, updated_at)
                    VALUES (@key, @prefix, @padding, @lastNum, @now, @now)",
                    new { key = sequenceKey, prefix, padding, lastNum = existingMax, now = nowStr },
                    tx);

                row = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT * FROM document_sequences WHERE sequence_key = @key",
                    new { key = sequenceKey },
                    tx);
            }

            long storedLast = row != null ? Convert.ToInt64(row.last_number ?? 0) : 0;
            long baseNumber = Math.Max(storedLast, existingMax);
            long nextNumber = baseNumber + 1;

            if (!string.IsNullOrWhiteSpace(seedTable) && !string.IsNullOrWhiteSpace(seedColumn) && await _db.HasTableAsync(seedTable))
            {
                var escapedTable = seedTable.Replace("\"", "\"\"");
                var escapedCol = seedColumn.Replace("\"", "\"\"");
                while (true)
                {
                    var candidate = prefix + nextNumber.ToString().PadLeft(padding, '0');
                    var exists = await conn.ExecuteScalarAsync<int>(
                        $"SELECT COUNT(1) FROM \"{escapedTable}\" WHERE \"{escapedCol}\" = @cand",
                        new { cand = candidate },
                        tx);
                    if (exists == 0) break;
                    nextNumber++;
                }
            }

            await conn.ExecuteAsync(@"
                UPDATE document_sequences
                SET last_number = @nextNum, prefix = @prefix, padding = @padding, updated_at = @now
                WHERE sequence_key = @key",
                new
                {
                    nextNum = nextNumber,
                    prefix,
                    padding,
                    now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    key = sequenceKey
                },
                tx);

            await tx.CommitAsync();
            return prefix + nextNumber.ToString().PadLeft(padding, '0');
        }
        finally
        {
            SequenceLock.Release();
        }
    }

    public async Task<string> PreviewAsync(
        string sequenceKey,
        string prefix,
        int padding = 4,
        string? seedTable = null,
        string? seedColumn = null,
        bool seedFromMaxId = false)
    {
        await EnsureTableExistsAsync();

        long storedLast = 0;
        try
        {
            storedLast = await _db.ExecuteScalarAsync<long?>(
                "SELECT last_number FROM document_sequences WHERE sequence_key = @key",
                new { key = sequenceKey }) ?? 0;
        }
        catch
        {
        }

        var existingMax = await ResolveExistingTableMaxAsync(prefix, seedTable, seedColumn, seedFromMaxId);
        var nextNumber = Math.Max(storedLast, existingMax) + 1;
        return prefix + nextNumber.ToString().PadLeft(padding, '0');
    }

    private async Task<long> ResolveExistingTableMaxAsync(
        string prefix,
        string? seedTable,
        string? seedColumn,
        bool seedFromMaxId = false)
    {
        if (string.IsNullOrWhiteSpace(seedTable) || string.IsNullOrWhiteSpace(seedColumn))
        {
            return 0;
        }

        try
        {
            if (!await _db.HasTableAsync(seedTable)) return 0;

            var escapedTable = seedTable.Replace("\"", "\"\"");
            var escapedCol = seedColumn.Replace("\"", "\"\"");

            await using var conn = await _db.OpenConnectionAsync();
            var values = await conn.QueryAsync<string?>(
                $"SELECT \"{escapedCol}\" FROM \"{escapedTable}\" WHERE \"{escapedCol}\" LIKE @like ORDER BY id DESC LIMIT 25",
                new { like = prefix + "%" });

            long max = 0;
            var regex = new Regex("^" + Regex.Escape(prefix) + @"(\d+)$", RegexOptions.IgnoreCase);
            foreach (var val in values)
            {
                if (string.IsNullOrEmpty(val)) continue;
                var m = regex.Match(val);
                if (m.Success && long.TryParse(m.Groups[1].Value, out var num) && num > max)
                {
                    max = num;
                }
            }

            if (seedFromMaxId)
            {
                var maxId = await conn.ExecuteScalarAsync<long?>($"SELECT MAX(id) FROM \"{escapedTable}\"") ?? 0;
                if (maxId > max) max = maxId;
            }

            return max;
        }
        catch
        {
            return 0;
        }
    }
}

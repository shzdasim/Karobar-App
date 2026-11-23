using System.Diagnostics;
using Dapper;
using FluentAssertions;
using Karobar.Data;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Karobar.Tests;

public class Milestone6KarobarDbStabilityTests
{
    private sealed class IsolatedEnv : IAsyncDisposable
    {
        public string TempRoot { get; }
        public string DbPath { get; }
        public KarobarDb Db { get; }
        private readonly string? _prevRootEnv;

        private IsolatedEnv(string tempRoot, string dbPath, KarobarDb db, string? prevRootEnv)
        {
            TempRoot = tempRoot;
            DbPath = dbPath;
            Db = db;
            _prevRootEnv = prevRootEnv;
        }

        public static async Task<IsolatedEnv> CreateAsync()
        {
            var tempRoot = Path.Combine(AppContext.BaseDirectory, "m6_test_scratch", $"karobar_m6_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "public"));

            var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);

            var dbPath = Path.Combine(tempRoot, "database", "database.sqlite");
            KarobarDb.FlushSchemaCache();
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: dbPath)).EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var db = new KarobarDb(explicitSqlitePath: dbPath);
            return new IsolatedEnv(tempRoot, dbPath, db, prevRoot);
        }

        public ValueTask DisposeAsync()
        {
            KarobarDb.FlushSchemaCache();
            SqliteConnection.ClearAllPools();
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", _prevRootEnv);
            try
            {
                if (Directory.Exists(TempRoot))
                {
                    Directory.Delete(TempRoot, recursive: true);
                }
            }
            catch
            {
            }
            return ValueTask.CompletedTask;
        }
    }

    private static void SpawnAndWaitBenignChildProcess()
    {
        ProcessStartInfo psi;
        if (OperatingSystem.IsWindows())
        {
            psi = new ProcessStartInfo("cmd.exe", "/c exit 0")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            };
        }
        else
        {
            var truePath = File.Exists("/usr/bin/true") ? "/usr/bin/true" : "/bin/true";
            psi = new ProcessStartInfo(truePath)
            {
                UseShellExecute = false
            };
        }

        using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start benign child process.");
        proc.WaitForExit(10_000).Should().BeTrue();
        proc.ExitCode.Should().Be(0);
    }

    [Fact]
    public async Task M6_KarobarDb_PoolingAndProcessSpawn_NoError1Flake()
    {
        const int iterations = 30;
        const int rawWorkers = 6;
        const int transferWorkers = 4;

        for (var iter = 0; iter < iterations; iter++)
        {
            await using var env = await IsolatedEnv.CreateAsync();

            var fromAccId = await env.Db.InsertAsync("accounts", new Dictionary<string, object?>
            {
                ["name"] = "Bank Source M6",
                ["type"] = "bank",
                ["code"] = $"m6_src_{iter}",
                ["opening_balance"] = 1000m,
                ["current_balance"] = 1000m,
                ["is_active"] = 1,
                ["is_default"] = 0
            });

            var toAccId = await env.Db.InsertAsync("accounts", new Dictionary<string, object?>
            {
                ["name"] = "Bank Dest M6",
                ["type"] = "bank",
                ["code"] = $"m6_dst_{iter}",
                ["opening_balance"] = 500m,
                ["current_balance"] = 500m,
                ["is_active"] = 1,
                ["is_default"] = 0
            });

            // Leave DatabaseBootstrapper's pooled connections intact, flush WAL/schema cache,
            // and spawn a child process (which previously triggered SQLite Error 1 on reused pooled handles).
            KarobarDb.FlushSchemaCache();
            SpawnAndWaitBenignChildProcess();

            var acctSvc = new AccountService(env.Db);
            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var rawTasks = Enumerable.Range(0, rawWorkers).Select(workerIdx => Task.Run(async () =>
            {
                await barrier.Task;
                await using var conn = await env.Db.OpenConnectionAsync();
                await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);
                try
                {
                    await env.Db.InsertAsync("settings", new Dictionary<string, object?>
                    {
                        ["store_name"] = $"M6-Probe-{iter}-{workerIdx}",
                        ["currency_symbol"] = "Rs.",
                        ["currency_code"] = "PKR"
                    }, conn, tx);
                    await tx.CommitAsync();
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            }));

            var transferTasks = Enumerable.Range(0, transferWorkers).Select(workerIdx => Task.Run(async () =>
            {
                await barrier.Task;
                await acctSvc.TransferAsync(fromAccId, toAccId, 25m, $"M6-Iter-{iter}-W{workerIdx}");
            }));

            barrier.SetResult();
            await Task.WhenAll(rawTasks.Concat(transferTasks));

            var insertedProbeRows = await env.Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM settings WHERE store_name LIKE @prefix",
                new { prefix = $"M6-Probe-{iter}-%" });
            insertedProbeRows.Should().Be(rawWorkers, $"iteration {iter} must commit all raw concurrent transactions");

            var fromBalance = await env.Db.ExecuteScalarAsync<decimal>(
                "SELECT current_balance FROM accounts WHERE id = @id",
                new { id = fromAccId });
            var toBalance = await env.Db.ExecuteScalarAsync<decimal>(
                "SELECT current_balance FROM accounts WHERE id = @id",
                new { id = toAccId });

            fromBalance.Should().Be(1000m - (transferWorkers * 25m), $"iteration {iter} source balance invariant");
            toBalance.Should().Be(500m + (transferWorkers * 25m), $"iteration {iter} destination balance invariant");
        }
    }

    [Fact]
    public async Task M6_KarobarDb_ConnectionPoolingDisabled_And_BeginImmediateTransactionGuards_Hold()
    {
        await using var env = await IsolatedEnv.CreateAsync();

        // Verify Pooling=false on both CreateConnection() and OpenConnectionAsync()
        using (var syncConn = env.Db.CreateConnection())
        {
            var builder = new SqliteConnectionStringBuilder(syncConn.ConnectionString);
            builder.Pooling.Should().BeFalse();
        }

        await using (var asyncConn = await env.Db.OpenConnectionAsync())
        {
            var builder = new SqliteConnectionStringBuilder(asyncConn.ConnectionString);
            builder.Pooling.Should().BeFalse();

            // Null guard throws synchronously
            Action nullCall = () => { _ = KarobarDb.BeginImmediateTransactionAsync(null!); };
            nullCall.Should().Throw<ArgumentNullException>();

            // Managed nested transaction still throws InvalidOperationException (never swallowed)
            await using var tx = await KarobarDb.BeginImmediateTransactionAsync(asyncConn);
            await Assert.ThrowsAsync<InvalidOperationException>(() => KarobarDb.BeginImmediateTransactionAsync(asyncConn));
            await tx.RollbackAsync();
        }
    }
}

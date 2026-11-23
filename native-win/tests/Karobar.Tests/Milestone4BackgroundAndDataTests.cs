using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using FluentAssertions;
using Karobar.Background;
using Karobar.Core.Entities;
using Karobar.Data;
using Karobar.Services.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Karobar.Tests;

public class Milestone4BackgroundAndDataTests
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

        public static async Task<IsolatedEnv> CreateAsync(bool bootstrapSchema = true)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_m4b_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "public"));

            var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);

            var dbPath = Path.Combine(tempRoot, "database", "database.sqlite");
            KarobarDb.FlushSchemaCache();
            if (bootstrapSchema)
            {
                await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: dbPath)).EnsureSchemaReadyAsync();
                KarobarDb.FlushSchemaCache();
            }

            var db = new KarobarDb(explicitSqlitePath: dbPath);
            return new IsolatedEnv(tempRoot, dbPath, db, prevRoot);
        }

        public KarobarDbContext CreateEfContext(string? customDbPath = null)
        {
            return DatabaseBootstrapper.CreateDbContext(customDbPath ?? DbPath);
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
            catch { }
            return ValueTask.CompletedTask;
        }
    }

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "native-win")) &&
                Directory.Exists(Path.Combine(dir.FullName, "public")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repository root from AppContext.BaseDirectory.");
    }

    private static string ComputeSha256Hex(string filePath)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(filePath);
        var hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Fact]
    public async Task BG02_ConcurrentExecuteImportCoreAsync_SameTargetDb_SerializesCleanly()
    {
        await using var env = await IsolatedEnv.CreateAsync();

        var src1Path = Path.Combine(env.TempRoot, "src1.sqlite");
        var src2Path = Path.Combine(env.TempRoot, "src2.sqlite");
        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: src1Path)).EnsureSchemaReadyAsync();
        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: src2Path)).EnsureSchemaReadyAsync();
        SqliteConnection.ClearAllPools();

        await using (var s1 = new SqliteConnection($"Data Source={src1Path};Pooling=False;"))
        {
            await s1.OpenAsync();
            await s1.ExecuteAsync("INSERT INTO categories (id, name) VALUES (1, 'M4b Concurrent Cat Alpha');");
            await s1.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        }

        await using (var s2 = new SqliteConnection($"Data Source={src2Path};Pooling=False;"))
        {
            await s2.OpenAsync();
            await s2.ExecuteAsync("INSERT INTO categories (id, name) VALUES (1, 'M4b Concurrent Cat Beta');");
            await s2.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        }

        var engine1 = new WaseelaAndSqliteImportEngine(env.TempRoot);
        var engine2 = new WaseelaAndSqliteImportEngine(env.TempRoot);

        var st1 = await engine1.StageDatabaseFromFileAsync(src1Path, "src1.sqlite");
        var st2 = await engine2.StageDatabaseFromFileAsync(src2Path, "src2.sqlite");
        var tok1 = st1["token"]!.ToString()!;
        var tok2 = st2["token"]!.ToString()!;

        try
        {
            var opts1 = new SqliteImportExecutionOptions { Mode = "merge", ConflictPolicy = "upsert", ReconcileBatches = false };
            var opts2 = new SqliteImportExecutionOptions { Mode = "merge", ConflictPolicy = "upsert", ReconcileBatches = false };

            var t1 = Task.Run(() => engine1.ExecuteImportCoreAsync(env.DbPath, tok1, opts1));
            var t2 = Task.Run(() => engine2.ExecuteImportCoreAsync(env.DbPath, tok2, opts2));
            var results = await Task.WhenAll(t1, t2);

            results.Should().OnlyContain(r => Equals(r["ok"], true));
            foreach (var r in results)
            {
                var tableReports = r["tables"] as List<Dictionary<string, object?>>;
                var warnings = tableReports!.SelectMany(tr => (tr["warnings"] as List<string>) ?? new List<string>()).ToList();
                warnings.Should().NotContain(w => w.Contains("database is locked", StringComparison.OrdinalIgnoreCase));
            }

            var catCount = await env.Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM categories WHERE name IN ('M4b Concurrent Cat Alpha', 'M4b Concurrent Cat Beta')");
            catCount.Should().Be(2);
        }
        finally
        {
            engine1.CleanupStagedSession(tok1);
            engine2.CleanupStagedSession(tok2);
        }
    }

    [Fact]
    public async Task BG02_RunPostImportReconciliationAsync_UsesWalAndRollsBackAtomicallyOnError()
    {
        await using var env = await IsolatedEnv.CreateAsync();

        await using (var conn = new SqliteConnection($"Data Source={env.DbPath};Pooling=False;"))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("""
                INSERT INTO categories (id, name) VALUES (1, 'Cat Recon');
                INSERT INTO brands (id, name) VALUES (1, 'Brand Recon');
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity, formulation)
                    VALUES (1, 'P-REC-1', 'P-REC-1', 'Recon Tab', 1, 1, 1, 5, 'Paracetamol');
                CREATE TRIGGER trg_fail_formulations_insert
                BEFORE INSERT ON formulations
                BEGIN
                    SELECT RAISE(ABORT, 'forced_reconciliation_failure');
                END;
                """);
        }

        await using var ef = env.CreateEfContext();
        var engine = new WaseelaAndSqliteImportEngine(env.TempRoot);

        Func<Task> act = async () => await engine.RunPostImportReconciliationAsync(ef);
        await act.Should().ThrowAsync<SqliteException>().WithMessage("*forced_reconciliation_failure*");

        // Opening batch insert ran BEFORE formulations insert inside the same BEGIN IMMEDIATE tx;
        // rollback must leave 0 opening batches in the DB.
        var batchCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM batches");
        batchCount.Should().Be(0);

        // Drop fault trigger and verify normal reconciliation succeeds and enables WAL
        await using (var conn = new SqliteConnection($"Data Source={env.DbPath};Pooling=False;"))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("DROP TRIGGER trg_fail_formulations_insert;");
        }

        var recon = await engine.RunPostImportReconciliationAsync(ef);
        recon["opening_batches_created"].Should().Be(1);
        recon["formulations_synced"].Should().Be(1);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM batches WHERE product_id = 1")).Should().Be(1);

        await using (var checkConn = new SqliteConnection($"Data Source={env.DbPath};Pooling=False;"))
        {
            await checkConn.OpenAsync();
            var journalMode = await checkConn.ExecuteScalarAsync<string>("PRAGMA journal_mode;");
            journalMode.Should().BeEquivalentTo("wal");
        }
    }

    [Fact]
    public async Task BG03_StageDatabaseFromFileAsync_ExcludesAppleDoubleEntries_And_EnforcesTextImportCap()
    {
        await using var env = await IsolatedEnv.CreateAsync();

        // Create a valid small SQLite database to pack into a ZIP alongside macOS AppleDouble junk
        var validDbPath = Path.Combine(env.TempRoot, "valid_inner.sqlite");
        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: validDbPath)).EnsureSchemaReadyAsync();
        SqliteConnection.ClearAllPools();
        await using (var vc = new SqliteConnection($"Data Source={validDbPath};Pooling=False;"))
        {
            await vc.OpenAsync();
            await vc.ExecuteAsync("INSERT INTO categories (id, name) VALUES (1, 'Valid Zip Cat');");
            await vc.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        }

        var mixedZipPath = Path.Combine(env.TempRoot, "mixed_macos.zip");
        using (var fs = File.Create(mixedZipPath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var appleMeta = zip.CreateEntry("__MACOSX/._valid_inner.sqlite");
            using (var s = appleMeta.Open())
            {
                var junk = new byte[4096];
                Random.Shared.NextBytes(junk);
                s.Write(junk);
            }

            var rootDotMeta = zip.CreateEntry("._shadow.sqlite");
            using (var s = rootDotMeta.Open())
            {
                var junk = new byte[4096];
                Random.Shared.NextBytes(junk);
                s.Write(junk);
            }

            var realEntry = zip.CreateEntry("nested/valid_inner.sqlite");
            using (var s = realEntry.Open())
            using (var src = File.OpenRead(validDbPath))
            {
                src.CopyTo(s);
            }
        }

        var engine = new WaseelaAndSqliteImportEngine(env.TempRoot);
        var meta = await engine.StageDatabaseFromFileAsync(mixedZipPath, "mixed_macos.zip");
        var token = meta["token"]!.ToString()!;
        try
        {
            var inspection = await engine.InspectAndProposeMappingAsync(token, env.DbPath);
            var tables = inspection["tables"] as List<Dictionary<string, object?>>;
            tables.Should().NotBeNull();
            tables!.Any(t => string.Equals(t["target_table"]?.ToString(), "categories", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        }
        finally
        {
            engine.CleanupStagedSession(token);
        }

        // ZIP containing ONLY AppleDouble metadata entries must be rejected
        var onlyAppleZipPath = Path.Combine(env.TempRoot, "only_apple.zip");
        using (var fs = File.Create(onlyAppleZipPath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var appleMeta = zip.CreateEntry("__MACOSX/._data.sqlite");
            using var s = appleMeta.Open();
            s.Write(Encoding.UTF8.GetBytes("AppleDouble resource fork"));
        }

        Func<Task> actOnlyApple = async () => await engine.StageDatabaseFromFileAsync(onlyAppleZipPath, "only_apple.zip");
        await actOnlyApple.Should().ThrowAsync<InvalidOperationException>();

        // Oversized (>256 MB) .sql/.csv text import must throw InvalidOperationException before File.ReadAllTextAsync
        var oversizedSqlPath = Path.Combine(env.TempRoot, "oversized.sql");
        using (var fs = new FileStream(oversizedSqlPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            var prefix = Encoding.UTF8.GetBytes("CREATE TABLE t(id INT);\n");
            fs.Write(prefix, 0, prefix.Length);
            fs.Seek(256L * 1024 * 1024 + 1024, SeekOrigin.Begin);
            fs.WriteByte((byte)'\n');
        }

        Func<Task> actOversizedSql = async () => await engine.StageDatabaseFromFileAsync(oversizedSqlPath, "oversized.sql");
        await actOversizedSql.Should().ThrowAsync<InvalidOperationException>().WithMessage("*256 MB*");
    }

    [Fact]
    public async Task BG04_CubeTaskExecutor_Removes4ArgImplicitAdminOverload_And_DefaultsBulkAssignConfirmationToFalse()
    {
        var executeOverloads = typeof(CubeTaskExecutorService)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(CubeTaskExecutorService.ExecuteStructuredTaskAsync))
            .ToList();

        executeOverloads.Should().HaveCount(1);
        executeOverloads[0].GetParameters().Select(p => p.ParameterType).Should().Equal(
            typeof(KarobarDbContext),
            typeof(CubeUserContext),
            typeof(Dictionary<string, object?>),
            typeof(string),
            typeof(CancellationToken));

        await using var env = await IsolatedEnv.CreateAsync();
        await using (var conn = new SqliteConnection($"Data Source={env.DbPath};Pooling=False;"))
        {
            await conn.OpenAsync();
            await conn.ExecuteAsync("""
                INSERT INTO categories (id, name) VALUES (1, 'Cat Cube');
                INSERT INTO brands (id, name) VALUES (1, 'Brand Cube');
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, pack_size, quantity, formulation)
                    VALUES (1, 'PAN-500', 'PAN-500', 'Panadol 500mg', 1, 1, 10, 10, NULL);
                """);
        }

        await using var ef = env.CreateEfContext();
        var svc = new CubeTaskExecutorService();
        var adminUser = new CubeUserContext { UserId = 9901, IsAdmin = true };

        var assignments = new List<Dictionary<string, object?>>
        {
            new() { ["product_name"] = "Panadol 500mg", ["formulation"] = "Paracetamol" }
        };

        // Omitting user_confirmed must stage preview without mutating DB
        var unconfirmedRes = await svc.ExecuteStructuredTaskAsync(ef, adminUser, new Dictionary<string, object?>
        {
            ["task_type"] = "bulk_assign_formulations",
            ["assignments"] = assignments
        });

        unconfirmedRes["provider_status"]?.ToString().Should().Be("cube_task_awaiting_permission");
        var meta = unconfirmedRes["task_meta"] as Dictionary<string, object?>;
        meta.Should().NotBeNull();
        meta!["awaiting_permission"].Should().Be(true);

        var formulationBeforeConfirm = await env.Db.ExecuteScalarAsync<string?>("SELECT formulation FROM products WHERE id = 1");
        formulationBeforeConfirm.Should().BeNull();

        // Explicitly passing user_confirmed = true commits the formulation assignment
        var confirmedRes = await svc.ExecuteStructuredTaskAsync(ef, adminUser, new Dictionary<string, object?>
        {
            ["task_type"] = "bulk_assign_formulations",
            ["assignments"] = assignments,
            ["user_confirmed"] = true
        });

        confirmedRes["provider_status"]?.ToString().Should().Be("cube_task_executed");
        var formulationAfterConfirm = await env.Db.ExecuteScalarAsync<string?>("SELECT formulation FROM products WHERE id = 1");
        formulationAfterConfirm.Should().Be("Paracetamol");
    }

    [Fact]
    public async Task BG05_ExecuteProcessRecurringExpensesAsync_CommitsAndRollsBackAtomically()
    {
        // 1. Happy path: expense + account + cash drawer all update together
        await using (var envOk = await IsolatedEnv.CreateAsync())
        {
            await using (var conn = new SqliteConnection($"Data Source={envOk.DbPath};Pooling=False;"))
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync("""
                    INSERT INTO users (id, name, email, password, status)
                        VALUES (1, 'Admin', 'admin@m4b.local', 'hash', 'active');
                    INSERT INTO expense_categories (id, name, code)
                        VALUES (1, 'Utilities', 'UTIL');
                    INSERT INTO accounts (id, name, code, type, current_balance, is_default, is_active)
                        VALUES (1, 'Cash Drawer', 'cash_drawer', 'cash', 1000.00, 1, 1);
                    INSERT INTO cash_drawer_sessions (id, user_id, opening_balance, closing_balance_expected, status, opened_at)
                        VALUES (1, 1, 1000.00, 1000.00, 'open', '2026-01-01 09:00:00');
                    INSERT INTO expenses (id, expense_number, expense_category_id, account_id, user_id, amount, date, payment_method, is_recurring, recurring_frequency, description)
                        VALUES (1, 'EXP-TPL-1', 1, 1, 1, 250.00, @tplDate, 'cash', 1, 'daily', 'Daily Cleaner');
                    """, new { tplDate = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd") });
            }

            await using var efOk = envOk.CreateEfContext();
            var res = await new KarobarCliCommandRunner(envOk.TempRoot).ExecuteProcessRecurringExpensesAsync(
                efOk,
                new Dictionary<string, string?>());

            res.ExitCode.Should().Be(0);
            res.Data["processed_count"].Should().Be(1);
            (await envOk.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM expenses")).Should().Be(2);
            (await envOk.Db.ExecuteScalarAsync<int>("SELECT is_recurring FROM expenses WHERE id = 1")).Should().Be(0);
            (await envOk.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = 1")).Should().Be(750.00m);
            (await envOk.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM account_transactions")).Should().Be(1);
            (await envOk.Db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = 1")).Should().Be(750.00m);
            (await envOk.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM cash_drawer_transactions")).Should().Be(1);
        }

        // 2. Fault path: failure during second SaveChangesAsync rolls back the first SaveChangesAsync too
        await using (var envFail = await IsolatedEnv.CreateAsync())
        {
            await using (var conn = new SqliteConnection($"Data Source={envFail.DbPath};Pooling=False;"))
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync("""
                    INSERT INTO users (id, name, email, password, status)
                        VALUES (1, 'Admin', 'admin@m4b.local', 'hash', 'active');
                    INSERT INTO expense_categories (id, name, code)
                        VALUES (1, 'Utilities', 'UTIL');
                    INSERT INTO accounts (id, name, code, type, current_balance, is_default, is_active)
                        VALUES (1, 'Cash Drawer', 'cash_drawer', 'cash', 1000.00, 1, 1);
                    INSERT INTO expenses (id, expense_number, expense_category_id, account_id, user_id, amount, date, payment_method, is_recurring, recurring_frequency, description)
                        VALUES (1, 'EXP-TPL-1', 1, 1, 1, 250.00, @tplDate, 'cash', 1, 'daily', 'Daily Cleaner');
                    CREATE TRIGGER trg_fail_account_tx_insert
                    BEFORE INSERT ON account_transactions
                    BEGIN
                        SELECT RAISE(ABORT, 'forced_recurring_ledger_failure');
                    END;
                    """, new { tplDate = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd") });
            }

            await using var efFail = envFail.CreateEfContext();
            Func<Task> act = async () => await new KarobarCliCommandRunner(envFail.TempRoot)
                .ExecuteProcessRecurringExpensesAsync(efFail, new Dictionary<string, string?>());

            await act.Should().ThrowAsync<Exception>();

            // Template must remain recurring=1 and NO partial child expense row may exist
            (await envFail.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM expenses")).Should().Be(1);
            (await envFail.Db.ExecuteScalarAsync<int>("SELECT is_recurring FROM expenses WHERE id = 1")).Should().Be(1);
            (await envFail.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = 1")).Should().Be(1000.00m);
        }
    }

    [Fact]
    public async Task BG06_ApplyPatchAndExportSqlite_FailClosedWithoutWalUnsafeCopyFallback()
    {
        await using var env = await IsolatedEnv.CreateAsync(bootstrapSchema: false);

        // Corrupt database.sqlite so BackupSqliteOnlineAsync fails
        await File.WriteAllTextAsync(env.DbPath, "NOT A VALID SQLITE DATABASE FILE");

        var runner = new KarobarCliCommandRunner(env.TempRoot);

        // 1. ExecuteExportSqliteAsync must fail with ExitCode=1 and not leave a raw File.Copy output file
        var exportOut = Path.Combine(env.TempRoot, "storage", "app", "exported.sqlite");
        var exportRes = await runner.ExecuteExportSqliteAsync(new Dictionary<string, string?>
        {
            ["output"] = exportOut
        });

        exportRes.ExitCode.Should().Be(1);
        exportRes.Data["ok"].Should().Be(false);
        File.Exists(exportOut).Should().BeFalse();

        // 2. ExecuteApplyPatchAsync must abort before modifying target files when pre-patch backup fails on a corrupt SQLite DB
        var publicDir = Path.Combine(env.TempRoot, "public");
        Directory.CreateDirectory(publicDir);
        var existingTarget = Path.Combine(publicDir, "existing-app.txt");
        await File.WriteAllTextAsync(existingTarget, "original-content");

        var corruptSqlite = new byte[4096];
        Array.Fill(corruptSqlite, (byte)0xFF);
        Encoding.ASCII.GetBytes("SQLite format 3\0").CopyTo(corruptSqlite, 0);
        await File.WriteAllBytesAsync(env.DbPath, corruptSqlite);

        var patchZip = Path.Combine(env.TempRoot, "patch.zip");
        using (var fs = File.Create(patchZip))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("public/m4b-patched.txt");
            using (var es = entry.Open())
                es.Write(Encoding.UTF8.GetBytes("patched content"));

            var overwriteEntry = zip.CreateEntry("public/existing-app.txt");
            using (var es2 = overwriteEntry.Open())
                es2.Write(Encoding.UTF8.GetBytes("overwritten content"));
        }

        var sha256 = ComputeSha256Hex(patchZip);
        var patchRes = await runner.ExecuteApplyPatchAsync(new Dictionary<string, string?>
        {
            ["patch_zip"] = patchZip,
            ["verify-sha"] = sha256
        });

        patchRes.ExitCode.Should().Be(1);
        patchRes.Data["ok"].Should().Be(false);
        patchRes.Data["patch_applied"].Should().Be(false);
        patchRes.Output.Should().Contain("Pre-patch database backup failed");
        File.Exists(Path.Combine(publicDir, "m4b-patched.txt")).Should().BeFalse();
        (await File.ReadAllTextAsync(existingTarget)).Should().Be("original-content");

        var backupDir = Path.Combine(env.TempRoot, "storage", "backups");
        if (Directory.Exists(backupDir))
        {
            Directory.GetFiles(backupDir, "pre-patch-*.sqlite").Should().BeEmpty();
        }

        // 2b. Non-magic file WITH a -wal sidecar must still trigger IsSqliteDatabaseCandidate and fail closed
        await File.WriteAllTextAsync(env.DbPath, "NOT A VALID SQLITE DATABASE FILE");
        await File.WriteAllTextAsync(env.DbPath + "-wal", "corrupt-wal-sidecar");

        var walPatchRes = await runner.ExecuteApplyPatchAsync(new Dictionary<string, string?>
        {
            ["patch_zip"] = patchZip,
            ["verify-sha"] = sha256
        });

        walPatchRes.ExitCode.Should().Be(1);
        walPatchRes.Data["ok"].Should().Be(false);
        walPatchRes.Data["patch_applied"].Should().Be(false);
        walPatchRes.Output.Should().Contain("Pre-patch database backup failed");
        File.Exists(Path.Combine(publicDir, "m4b-patched.txt")).Should().BeFalse();
        (await File.ReadAllTextAsync(existingTarget)).Should().Be("original-content");
        if (Directory.Exists(backupDir))
        {
            Directory.GetFiles(backupDir, "pre-patch-*.sqlite").Should().BeEmpty();
        }

        // 3. Preserve M1 ExtractPatchZipSafely Zip-Slip guard
        var slipZip = Path.Combine(env.TempRoot, "slip.zip");
        using (var fs = File.Create(slipZip))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var bad = zip.CreateEntry("../evil.txt");
            using var es = bad.Open();
            es.Write(Encoding.UTF8.GetBytes("bad"));
        }

        var stageDir = Path.Combine(env.TempRoot, "stage");
        Action slipAct = () => KarobarCliCommandRunner.ExtractPatchZipSafely(slipZip, stageDir);
        slipAct.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task DATA01_PurchaseInvoiceUniqueIndex_FreshUpgradedDuplicateSafe_And_WaseelaMergeCompatible()
    {
        // 1. Fresh DB (tableCount == 0): EnsureSchemaReadyAsync creates IX_purchase_invoices_supplier_id_invoice_number
        await using (var envFresh = await IsolatedEnv.CreateAsync())
        {
            await using var conn = new SqliteConnection($"Data Source={envFresh.DbPath};Pooling=False;");
            await conn.OpenAsync();
            await conn.ExecuteAsync("INSERT INTO suppliers (id, name) VALUES (1, 'Sup Fresh');");
            await conn.ExecuteAsync("""
                INSERT INTO purchase_invoices (supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount)
                VALUES (1, 'PI-F1', 'INV-DUP-1', '2026-01-01', 100, 100);
                """);

            Func<Task> dupInsert = async () => await conn.ExecuteAsync("""
                INSERT INTO purchase_invoices (supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount)
                VALUES (1, 'PI-F2', 'INV-DUP-1', '2026-01-02', 200, 200);
                """);

            var ex = await dupInsert.Should().ThrowAsync<SqliteException>();
            ex.Which.SqliteErrorCode.Should().Be(19);
        }

        // 2. Upgraded clean DB (tableCount > 0 without index): creates index idempotently across repeated bootstrap calls
        await using (var envUpgrade = await IsolatedEnv.CreateAsync(bootstrapSchema: false))
        {
            await using (var conn = new SqliteConnection($"Data Source={envUpgrade.DbPath};Pooling=False;"))
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync("""
                    CREATE TABLE suppliers (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL);
                    CREATE TABLE purchase_invoices (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        supplier_id INTEGER NOT NULL,
                        posted_number TEXT NOT NULL,
                        invoice_number TEXT NOT NULL,
                        posted_date TEXT NOT NULL,
                        invoice_amount REAL NOT NULL DEFAULT 0,
                        total_amount REAL NOT NULL DEFAULT 0
                    );
                    INSERT INTO suppliers (id, name) VALUES (1, 'Sup Up');
                    INSERT INTO purchase_invoices (supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount)
                        VALUES (1, 'PI-U1', 'INV-U-1', '2026-01-01', 50, 50);
                    """);
            }

            var bootstrapper = new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: envUpgrade.DbPath));
            await bootstrapper.EnsureSchemaReadyAsync();
            await bootstrapper.EnsureSchemaReadyAsync();
            SqliteConnection.ClearAllPools();

            await using var checkConn = new SqliteConnection($"Data Source={envUpgrade.DbPath};Pooling=False;");
            await checkConn.OpenAsync();
            Func<Task> dupInsert = async () => await checkConn.ExecuteAsync("""
                INSERT INTO purchase_invoices (supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount)
                VALUES (1, 'PI-U2', 'INV-U-1', '2026-01-02', 60, 60);
                """);

            var ex = await dupInsert.Should().ThrowAsync<SqliteException>();
            ex.Which.SqliteErrorCode.Should().Be(19);
        }

        // 3. Upgraded legacy DB with existing duplicate (supplier_id, invoice_number): skips unique index without crashing
        await using (var envLegacyDup = await IsolatedEnv.CreateAsync(bootstrapSchema: false))
        {
            await using (var conn = new SqliteConnection($"Data Source={envLegacyDup.DbPath};Pooling=False;"))
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync("""
                    CREATE TABLE suppliers (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL);
                    CREATE TABLE purchase_invoices (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        supplier_id INTEGER NOT NULL,
                        posted_number TEXT NOT NULL,
                        invoice_number TEXT NOT NULL,
                        posted_date TEXT NOT NULL,
                        invoice_amount REAL NOT NULL DEFAULT 0,
                        total_amount REAL NOT NULL DEFAULT 0
                    );
                    INSERT INTO suppliers (id, name) VALUES (1, 'Sup Legacy');
                    INSERT INTO purchase_invoices (supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount)
                        VALUES (1, 'PI-L1', 'INV-LEGACY-DUP', '2026-01-01', 50, 50);
                    INSERT INTO purchase_invoices (supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount)
                        VALUES (1, 'PI-L2', 'INV-LEGACY-DUP', '2026-01-02', 75, 75);
                    """);
            }

            var bootstrapper = new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: envLegacyDup.DbPath));
            Func<Task> act = async () => await bootstrapper.EnsureSchemaReadyAsync();
            await act.Should().NotThrowAsync();

            (await envLegacyDup.Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM purchase_invoices WHERE supplier_id = 1 AND invoice_number = 'INV-LEGACY-DUP'"))
                .Should().Be(2);
        }

        // 4. WaseelaAndSqliteImportEngine merge compatibility with DATA-01 unique index:
        //    same (supplier_id, invoice_number) with different posted_number matches & updates cleanly without Error 19
        await using (var envMerge = await IsolatedEnv.CreateAsync())
        {
            await using (var dst = new SqliteConnection($"Data Source={envMerge.DbPath};Pooling=False;"))
            {
                await dst.OpenAsync();
                await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
                await dst.ExecuteAsync("""
                    INSERT INTO suppliers (id, name) VALUES (1, 'Sup Merge');
                    INSERT INTO purchase_invoices (id, supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount, remarks)
                        VALUES (1, 1, 'PI-TGT-1', 'SUP-INV-777', '2026-01-01', 100, 100, 'Before Merge');
                    """);
            }

            var srcMergePath = Path.Combine(envMerge.TempRoot, "src_merge_pi.sqlite");
            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: srcMergePath)).EnsureSchemaReadyAsync();
            SqliteConnection.ClearAllPools();
            await using (var src = new SqliteConnection($"Data Source={srcMergePath};Pooling=False;"))
            {
                await src.OpenAsync();
                await src.ExecuteAsync("PRAGMA foreign_keys = OFF;");
                await src.ExecuteAsync("""
                    INSERT INTO suppliers (id, name) VALUES (1, 'Sup Merge');
                    INSERT INTO purchase_invoices (id, supplier_id, posted_number, invoice_number, posted_date, invoice_amount, total_amount, remarks)
                        VALUES (99, 1, 'PI-SRC-99', 'SUP-INV-777', '2026-01-01', 150, 150, 'Updated By Composite Key');
                    """);
                await src.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
            }

            var engine = new WaseelaAndSqliteImportEngine(envMerge.TempRoot);
            var staged = await engine.StageDatabaseFromFileAsync(srcMergePath, "src_merge_pi.sqlite");
            var token = staged["token"]!.ToString()!;
            try
            {
                var res = await engine.ExecuteImportCoreAsync(envMerge.DbPath, token, new SqliteImportExecutionOptions
                {
                    Mode = "merge",
                    ConflictPolicy = "upsert",
                    ReconcileBatches = false
                });

                var tableReports = res["tables"] as List<Dictionary<string, object?>>;
                var warnings = tableReports!.SelectMany(r => (r["warnings"] as List<string>) ?? new List<string>()).ToList();
                warnings.Should().NotContain(w => w.Contains("SQLite Error 19", StringComparison.OrdinalIgnoreCase));

                (await envMerge.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM purchase_invoices")).Should().Be(1);
                (await envMerge.Db.ExecuteScalarAsync<string>("SELECT remarks FROM purchase_invoices WHERE id = 1"))
                    .Should().Be("Updated By Composite Key");
            }
            finally
            {
                engine.CleanupStagedSession(token);
            }
        }
    }

    [Fact]
    public async Task DATA02_TradeSchemePermissions_SeededIdempotentlyOnFreshAndUpgradedDbs()
    {
        // 1. Fresh DB (tableCount == 0)
        await using (var envFresh = await IsolatedEnv.CreateAsync())
        {
            var viewCount = await envFresh.Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM permissions WHERE name = 'trade-scheme.view' AND guard_name = 'sanctum'");
            var manageCount = await envFresh.Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM permissions WHERE name = 'trade-scheme.manage' AND guard_name = 'sanctum'");

            viewCount.Should().Be(1);
            manageCount.Should().Be(1);
        }

        // 2. Upgraded DB (tableCount > 0 without trade-scheme.* permissions)
        await using (var envUpgrade = await IsolatedEnv.CreateAsync(bootstrapSchema: false))
        {
            await using (var conn = new SqliteConnection($"Data Source={envUpgrade.DbPath};Pooling=False;"))
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync("""
                    CREATE TABLE permissions (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        name TEXT NOT NULL,
                        guard_name TEXT NOT NULL,
                        created_at TEXT NULL,
                        updated_at TEXT NULL
                    );
                    INSERT INTO permissions (name, guard_name) VALUES ('product.view', 'sanctum');
                    """);
            }

            var bootstrapper = new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: envUpgrade.DbPath));
            await bootstrapper.EnsureSchemaReadyAsync();
            await bootstrapper.EnsureSchemaReadyAsync();

            var viewCount = await envUpgrade.Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM permissions WHERE name = 'trade-scheme.view' AND guard_name = 'sanctum'");
            var manageCount = await envUpgrade.Db.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM permissions WHERE name = 'trade-scheme.manage' AND guard_name = 'sanctum'");

            viewCount.Should().Be(1);
            manageCount.Should().Be(1);
        }
    }

    [Fact]
    public void ISS23_03b_RetiredPhpFilesRemoved_And_PreservedPublicAssetsIntact()
    {
        var repoRoot = ResolveRepoRoot();

        File.Exists(Path.Combine(repoRoot, "public", "index.php")).Should().BeFalse();
        File.Exists(Path.Combine(repoRoot, "public", ".htaccess")).Should().BeFalse();
        File.Exists(Path.Combine(repoRoot, "public", ".user.ini")).Should().BeFalse();

        File.Exists(Path.Combine(repoRoot, "public", "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "public", "version.json")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "public", "build", "manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "public", "logo.png")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "public", "favicon.ico")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "public", "fonts", "Tahoma.ttf")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "public", "fonts", "Tahoma-Bold.ttf")).Should().BeTrue();
        File.Exists(Path.Combine(repoRoot, "public", "robots.txt")).Should().BeTrue();
    }
}

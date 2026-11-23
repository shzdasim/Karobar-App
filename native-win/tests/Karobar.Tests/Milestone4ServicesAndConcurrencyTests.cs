using System.IO.Compression;
using System.Text;
using Dapper;
using FluentAssertions;
using Karobar.Background;
using Karobar.Data;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Karobar.Tests;

public class Milestone4ServicesAndConcurrencyTests
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
            var tempRoot = Path.Combine(AppContext.BaseDirectory, "m4c_test_scratch", $"karobar_m4c_{Guid.NewGuid():N}");
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

        public async Task<long> EnsureRoleAsync(string roleName)
        {
            var existing = await Db.QuerySingleOrDefaultAsync(
                "SELECT id FROM roles WHERE LOWER(name) = LOWER(@name) LIMIT 1",
                new { name = roleName });
            if (existing != null) return Convert.ToInt64(existing["id"]);

            return await Db.InsertAsync("roles", new Dictionary<string, object?>
            {
                ["name"] = roleName,
                ["guard_name"] = "sanctum"
            });
        }

        public async Task EnsurePermissionAsync(string permName)
        {
            var existing = await Db.QuerySingleOrDefaultAsync(
                "SELECT id FROM permissions WHERE LOWER(name) = LOWER(@name) LIMIT 1",
                new { name = permName });
            if (existing != null) return;

            await Db.InsertAsync("permissions", new Dictionary<string, object?>
            {
                ["name"] = permName,
                ["guard_name"] = "sanctum"
            });
        }

        public async Task<long> SeedUserAsync(string name, string email, string roleName, string plainPassword = "Password123!")
        {
            var roleId = await EnsureRoleAsync(roleName);
            var hash = BCrypt.Net.BCrypt.HashPassword(plainPassword, 4);
            var uid = await Db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = name,
                ["email"] = email,
                ["password"] = hash,
                ["status"] = "active"
            });
            await Db.ExecuteAsync(
                "INSERT OR IGNORE INTO model_has_roles (role_id, model_type, model_id) VALUES (@rid, 'App\\Models\\User', @uid)",
                new { rid = roleId, uid });
            return uid;
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

    private static async Task<(long CatId, long BrandId, long SupId, long CustId, long ProdId)> SeedCatalogAsync(
        IsolatedEnv env,
        decimal initialQty = 50m,
        decimal unitPurchasePrice = 50m,
        decimal unitSalePrice = 100m)
    {
        var catId = await env.Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "General" });
        var brandId = await env.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "BrandM4c" });
        var supId = await env.Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "SupplierM4c" });
        var custId = await env.Db.InsertAsync("customers", new Dictionary<string, object?> { ["name"] = "CustomerM4c" });
        var prodId = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = $"P-{Guid.NewGuid():N}"[..10],
            ["name"] = "Amoxil 500mg",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = supId,
            ["pack_size"] = 1,
            ["quantity"] = initialQty,
            ["pack_purchase_price"] = unitPurchasePrice,
            ["unit_purchase_price"] = unitPurchasePrice,
            ["avg_price"] = unitPurchasePrice,
            ["pack_sale_price"] = unitSalePrice,
            ["unit_sale_price"] = unitSalePrice,
            ["narcotic"] = "no"
        });
        return (catId, brandId, supId, custId, prodId);
    }

    [Fact]
    public async Task FM403_SaleStockAvailability_NonExpiredStockWithWrongOverrideCreds_SkipsEagerBcryptAndPreservesRateLimitBudget()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        InvoiceCalculationService.ResetRateLimitState();

        await env.EnsurePermissionAsync("sale-invoice.expired-override");
        await env.SeedUserAsync("Supervisor", "sup@m4c.local", "Admin", "CorrectSupPass#1");
        var cashierId = await env.SeedUserAsync("Cashier", "cashier@m4c.local", "Cashier", "CashierPass#1");
        var (_, _, _, _, prodId) = await SeedCatalogAsync(env, initialQty: 20m);

        var futureDate = DateTime.UtcNow.AddDays(180).ToString("yyyy-MM-dd");
        var pastDate = DateTime.UtcNow.AddDays(-10).ToString("yyyy-MM-dd");

        await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = prodId,
            ["batch_number"] = "FRESH-01",
            ["expiry_date"] = futureDate,
            ["quantity"] = 10
        });
        await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = prodId,
            ["batch_number"] = "EXP-01",
            ["expiry_date"] = pastDate,
            ["quantity"] = 5
        });
        await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = prodId,
            ["batch_number"] = "EXP-02",
            ["expiry_date"] = pastDate,
            ["quantity"] = 5
        });

        var svc = new SaleStockAvailabilityService(env.Db);

        // 1. Non-expired batch with expiredOverride=true & WRONG supervisor password must NOT burn rate limit or fail
        var freshItems = new List<Dictionary<string, object?>>
        {
            new() { ["product_id"] = prodId, ["quantity"] = 2, ["bonus"] = 0, ["batch_number"] = "FRESH-01", ["expiry_date"] = futureDate }
        };
        var wrongCreds = new Dictionary<string, object?>
        {
            ["supervisor_email"] = "sup@m4c.local",
            ["supervisor_password"] = "WrongPassword!"
        };

        for (int i = 0; i < 10; i++)
        {
            await svc.ValidateAsync(freshItems, null, cashierId, expiredOverride: true, overrideCredentials: wrongCreds);
        }

        // 2. Expired batch without override -> 422 requires_expired_override
        var expItems = new List<Dictionary<string, object?>>
        {
            new() { ["product_id"] = prodId, ["quantity"] = 1, ["bonus"] = 0, ["batch_number"] = "EXP-01", ["expiry_date"] = pastDate },
            new() { ["product_id"] = prodId, ["quantity"] = 1, ["bonus"] = 0, ["batch_number"] = "EXP-02", ["expiry_date"] = pastDate }
        };

        var ex422 = await Assert.ThrowsAsync<InvoiceValidationException>(
            () => svc.ValidateAsync(expItems, null, cashierId, expiredOverride: false));
        ex422.StatusCode.Should().Be(422);
        ex422.ResponsePayload["requires_expired_override"].Should().Be(true);

        // 3. Full rate-limit budget (4 failed attempts returning 403, then 5th triggers 429 lockout) must still be intact
        for (int attempt = 1; attempt <= 4; attempt++)
        {
            var ex403 = await Assert.ThrowsAsync<InvoiceValidationException>(
                () => svc.ValidateAsync(expItems, null, cashierId, expiredOverride: true, overrideCredentials: wrongCreds));
            ex403.StatusCode.Should().Be(403, $"attempt {attempt} should still have rate-limit budget and return 403");
        }

        var ex429 = await Assert.ThrowsAsync<InvoiceValidationException>(
            () => svc.ValidateAsync(expItems, null, cashierId, expiredOverride: true, overrideCredentials: wrongCreds));
        ex429.StatusCode.Should().Be(429);

        // 4. Reset rate limit and verify valid supervisor override passes for multiple expired batches
        InvoiceCalculationService.ResetRateLimitState();
        var validCreds = new Dictionary<string, object?>
        {
            ["supervisor_email"] = "sup@m4c.local",
            ["supervisor_password"] = "CorrectSupPass#1"
        };
        await svc.ValidateAsync(expItems, null, cashierId, expiredOverride: true, overrideCredentials: validCreds);
    }

    [Fact]
    public async Task FM404a_DocumentSequenceService_ConcurrentNextAsync_WithSeedTable_ProducesUniqueMonotonicNumbers()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@seq.local", "Admin");
        var (_, _, _, custId, _) = await SeedCatalogAsync(env);

        // Seed existing invoice SI-2026-0005 so NextAsync must start at 0006
        await env.Db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["customer_id"] = custId,
            ["user_id"] = adminId,
            ["posted_number"] = "SI-2026-0005",
            ["date"] = "2026-11-23",
            ["invoice_type"] = "debit",
            ["payment_method"] = "cash",
            ["total"] = 100m,
            ["total_receive"] = 100m,
            ["status"] = "posted"
        });

        // Flush schema cache so TableExistsAsync inside NextAsync runs on cold cache with (conn, tx)
        KarobarDb.FlushSchemaCache();

        var seqSvc = new DocumentSequenceService(env.Db);
        const int workers = 12;
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var tasks = Enumerable.Range(0, workers).Select(_ => Task.Run(async () =>
        {
            await barrier.Task;
            return await seqSvc.NextAsync("sale_invoice:m4c_test", "SI-2026-", 4, "sale_invoices", "posted_number");
        })).ToArray();

        barrier.SetResult();
        var results = await Task.WhenAll(tasks);

        results.Distinct().Should().HaveCount(workers);
        var expected = Enumerable.Range(6, workers).Select(n => $"SI-2026-{n:D4}").ToArray();
        results.OrderBy(r => r).Should().Equal(expected.OrderBy(r => r));
    }

    [Fact]
    public async Task FM405_BackupService_RestoreBackupAsync_RejectsCorruptInvalidAndMetadataBackups_AndAbortsIfSafetyFails()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        await env.SeedUserAsync("Admin", "admin@bk.local", "Admin");
        await env.Db.InsertAsync("settings", new Dictionary<string, object?>
        {
            ["store_name"] = "LIVE-CANARY-STORE",
            ["currency_symbol"] = "Rs.",
            ["currency_code"] = "PKR"
        });

        var backupSvc = new BackupService(env.Db, new SqliteExportService(env.Db));
        await backupSvc.EnsureBackupSchemaAsync();
        Directory.CreateDirectory(backupSvc.DefaultBackupPath);

        async Task<long> RegisterBackupFileAsync(string fileName, string type)
        {
            return await env.Db.InsertAsync("backup_logs", new Dictionary<string, object?>
            {
                ["filename"] = Path.GetFileNameWithoutExtension(fileName),
                ["type"] = type,
                ["size"] = 100,
                ["path"] = $"backups/{fileName}",
                ["status"] = "completed"
            });
        }

        // (a) Non-SQLite file with .sqlite extension
        var badTextPath = Path.Combine(backupSvc.DefaultBackupPath, "bad_text.sqlite");
        await File.WriteAllTextAsync(badTextPath, new string('X', 200));
        var idBadText = await RegisterBackupFileAsync("bad_text.sqlite", "database");
        await env.Db.ExecuteAsync("UPDATE backup_logs SET path = 'backups/bad_text.sqlite' WHERE id = @id", new { id = idBadText });

        var exBadText = await Assert.ThrowsAsync<InvalidOperationException>(() => backupSvc.RestoreBackupAsync(idBadText, "pw"));
        exBadText.Message.Should().Contain("not a valid SQLite 3 database");

        // (b) Valid 16-byte SQLite header + corrupt body -> PRAGMA quick_check failure
        var corruptSqlitePath = Path.Combine(backupSvc.DefaultBackupPath, "corrupt_body.sqlite");
        var corruptBytes = new byte[4096];
        Encoding.ASCII.GetBytes("SQLite format 3\0").CopyTo(corruptBytes, 0);
        Random.Shared.NextBytes(corruptBytes.AsSpan(16));
        await File.WriteAllBytesAsync(corruptSqlitePath, corruptBytes);
        var idCorrupt = await env.Db.InsertAsync("backup_logs", new Dictionary<string, object?>
        {
            ["filename"] = "corrupt_body",
            ["type"] = "database",
            ["size"] = corruptBytes.Length,
            ["path"] = "backups/corrupt_body.sqlite",
            ["status"] = "completed"
        });

        var exCorrupt = await Assert.ThrowsAsync<InvalidOperationException>(() => backupSvc.RestoreBackupAsync(idCorrupt, "pw"));
        exCorrupt.Message.Should().ContainAny("quick_check", "corrupted", "not a readable SQLite");

        // (c) Valid SQLite DB missing required Karobar tables (users, settings)
        var foreignDbPath = Path.Combine(backupSvc.DefaultBackupPath, "foreign_schema.sqlite");
        await using (var fc = new SqliteConnection($"Data Source={foreignDbPath};Mode=ReadWriteCreate;Pooling=False;"))
        {
            await fc.OpenAsync();
            await fc.ExecuteAsync("CREATE TABLE unrelated_items (id INTEGER PRIMARY KEY, label TEXT); INSERT INTO unrelated_items (label) VALUES ('x');");
        }
        var idForeign = await env.Db.InsertAsync("backup_logs", new Dictionary<string, object?>
        {
            ["filename"] = "foreign_schema",
            ["type"] = "database",
            ["size"] = new FileInfo(foreignDbPath).Length,
            ["path"] = "backups/foreign_schema.sqlite",
            ["status"] = "completed"
        });

        var exForeign = await Assert.ThrowsAsync<InvalidOperationException>(() => backupSvc.RestoreBackupAsync(idForeign, "pw"));
        exForeign.Message.Should().Contain("missing required Karobar schema tables");

        // (d) ZIP containing only __MACOSX/._bad.sqlite (AppleDouble metadata) -> rejected
        var appleDoubleZipPath = Path.Combine(backupSvc.DefaultBackupPath, "apple_double_only.zip");
        using (var fs = File.Create(appleDoubleZipPath))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            var e = za.CreateEntry("__MACOSX/._database.sqlite");
            using var es = e.Open();
            es.Write(corruptBytes);
        }
        var idZipMac = await env.Db.InsertAsync("backup_logs", new Dictionary<string, object?>
        {
            ["filename"] = "apple_double_only",
            ["type"] = "full",
            ["size"] = new FileInfo(appleDoubleZipPath).Length,
            ["path"] = "backups/apple_double_only.zip",
            ["status"] = "completed"
        });

        var exZipMac = await Assert.ThrowsAsync<InvalidOperationException>(() => backupSvc.RestoreBackupAsync(idZipMac, "pw"));
        exZipMac.Message.Should().Contain("does not contain");

        // Verify ZERO orphan safety snapshots were created and LIVE-CANARY-STORE is completely untouched
        Directory.GetFiles(backupSvc.DefaultBackupPath, "pre-restore-safety-*").Should().BeEmpty();
        var liveStoreName = await env.Db.ExecuteScalarAsync<string>("SELECT store_name FROM settings ORDER BY id ASC LIMIT 1");
        liveStoreName.Should().Be("LIVE-CANARY-STORE");

        // (e) Create a valid SQLite backup AFTER inserting backup_logs row so idValidZip survives restore for rapid back-to-back restore check
        var validZipPath = Path.Combine(backupSvc.DefaultBackupPath, "valid_full.zip");
        var idValidZip = await env.Db.InsertAsync("backup_logs", new Dictionary<string, object?>
        {
            ["filename"] = "valid_full",
            ["type"] = "full",
            ["size"] = 1024,
            ["path"] = "backups/valid_full.zip",
            ["status"] = "completed"
        });

        var validSnapSrc = Path.Combine(env.TempRoot, "valid_backup_src.sqlite");
        await using (var src = new SqliteConnection($"Data Source={env.DbPath};Mode=ReadOnly;Pooling=False;"))
        await using (var dst = new SqliteConnection($"Data Source={validSnapSrc};Mode=ReadWriteCreate;Pooling=False;"))
        {
            await src.OpenAsync();
            await dst.OpenAsync();
            src.BackupDatabase(dst);
        }
        await using (var vc = new SqliteConnection($"Data Source={validSnapSrc};Mode=ReadWrite;Pooling=False;"))
        {
            await vc.OpenAsync();
            await vc.ExecuteAsync("UPDATE settings SET store_name = 'RESTORED-STORE';");
        }
        using (var fs = File.Create(validZipPath))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            za.CreateEntryFromFile(validSnapSrc, "database.sqlite");
        }

        await backupSvc.RestoreBackupAsync(idValidZip, "pw");
        await backupSvc.RestoreBackupAsync(idValidZip, "pw");

        var safetyFiles = Directory.GetFiles(backupSvc.DefaultBackupPath, "pre-restore-safety-*.sqlite");
        safetyFiles.Should().HaveCount(2);
        var restoredName = await env.Db.ExecuteScalarAsync<string>("SELECT store_name FROM settings ORDER BY id ASC LIMIT 1");
        restoredName.Should().Be("RESTORED-STORE");

        // (f) Safety snapshot failure must fail fast and NOT overwrite live DB
        var externalValidPath = Path.Combine(env.TempRoot, "external_valid.sqlite");
        File.Copy(validSnapSrc, externalValidPath, overwrite: true);
        await using (var ec = new SqliteConnection($"Data Source={externalValidPath};Mode=ReadWrite;Pooling=False;"))
        {
            await ec.OpenAsync();
            await ec.ExecuteAsync("UPDATE settings SET store_name = 'SHOULD-NOT-OVERWRITE';");
        }
        var idExt = await env.Db.InsertAsync("backup_logs", new Dictionary<string, object?>
        {
            ["filename"] = "external_valid",
            ["type"] = "database",
            ["size"] = new FileInfo(externalValidPath).Length,
            ["path"] = "backups/missing_local.sqlite",
            ["remote_path"] = externalValidPath,
            ["status"] = "completed"
        });

        // Replace _backupPath directory with a regular file so Directory.CreateDirectory(_backupPath) fails
        SqliteConnection.ClearAllPools();
        Directory.Delete(backupSvc.DefaultBackupPath, recursive: true);
        await File.WriteAllTextAsync(backupSvc.DefaultBackupPath, "blocker-file");

        var snapFailEx = await Assert.ThrowsAsync<InvalidOperationException>(() => backupSvc.RestoreBackupAsync(idExt, "pw"));
        snapFailEx.Message.Should().Contain("Pre-restore safety backup failed");

        var stillRestoredName = await env.Db.ExecuteScalarAsync<string>("SELECT store_name FROM settings ORDER BY id ASC LIMIT 1");
        stillRestoredName.Should().Be("RESTORED-STORE");
    }

    [Fact]
    public async Task FM404b_StocktakeService_ConcurrentStartAndFinalize_AllowsExactlyOneAndPreventsDoubleAdjustment()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@st.local", "Admin");
        var (_, _, _, _, prodId) = await SeedCatalogAsync(env, initialQty: 10m, unitPurchasePrice: 50m);

        var stSvc = new StocktakeService(env.Db);
        var session = await stSvc.CreateSessionAsync(new Dictionary<string, object?> { ["session_type"] = "full" }, adminId);
        var sessionId = Convert.ToInt64(session["id"]);

        // 6 concurrent StartSessionAsync -> 1 succeeds, 5 throw InvalidOperationException
        var startBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var startTasks = Enumerable.Range(0, 6).Select(_ => Task.Run<Exception?>(async () =>
        {
            await startBarrier.Task;
            try
            {
                await stSvc.StartSessionAsync(sessionId);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        })).ToArray();
        startBarrier.SetResult();
        var startResults = await Task.WhenAll(startTasks);

        startResults.Count(r => r == null).Should().Be(1);
        startResults.Count(r => r is InvalidOperationException).Should().Be(5);

        var itemRow = await env.Db.QuerySingleOrDefaultAsync(
            "SELECT * FROM stocktake_items WHERE stocktake_session_id = @sid AND product_id = @pid",
            new { sid = sessionId, pid = prodId });
        itemRow.Should().NotBeNull();
        await stSvc.RecordCountAsync(Convert.ToInt64(itemRow!["id"]), 8m, "Damaged", adminId);
        await stSvc.SubmitForReviewAsync(sessionId);

        // 6 concurrent FinalizeSessionAsync -> 1 succeeds, 5 throw InvalidOperationException
        var finBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finTasks = Enumerable.Range(0, 6).Select(_ => Task.Run<Exception?>(async () =>
        {
            await finBarrier.Task;
            try
            {
                await stSvc.FinalizeSessionAsync(sessionId, adminId);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        })).ToArray();
        finBarrier.SetResult();
        var finResults = await Task.WhenAll(finTasks);

        finResults.Count(r => r == null).Should().Be(1);
        finResults.Count(r => r is InvalidOperationException).Should().Be(5);

        var finalProdQty = await env.Db.ExecuteScalarAsync<decimal>("SELECT quantity FROM products WHERE id = @id", new { id = prodId });
        finalProdQty.Should().Be(8m);

        var adjCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM stock_adjustments");
        adjCount.Should().Be(1);

        var expCount = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM expenses");
        expCount.Should().Be(1);
        var expAmt = await env.Db.ExecuteScalarAsync<decimal>("SELECT amount FROM expenses LIMIT 1");
        expAmt.Should().Be(100m); // 2 missing units * 50m
    }

    [Fact]
    public async Task FM404b_LicenseService_ConcurrentActivateLicenseKeyAsync_MaintainsSingleActiveRow()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        try
        {
            var cliRunner = new KarobarCliCommandRunner(env.TempRoot);
            var kpRes = cliRunner.ExecuteLicenseIssue(new Dictionary<string, string?> { ["generate-keypair"] = "true" });
            kpRes.Ok.Should().BeTrue();
            var pubKey = kpRes.Data["public_key"]!.ToString()!;
            var secKey = kpRes.Data["secret_key"]!.ToString()!;
            var machineId = cliRunner.ExecuteLicenseMachineId(new Dictionary<string, string?>()).Output.Trim();

            LicenseService.SetTestOverrides(publicKey: pubKey, machineId: machineId);

            var issueRes = cliRunner.ExecuteLicenseIssue(new Dictionary<string, string?>
            {
                ["machine"] = machineId,
                ["tier"] = "platinum",
                ["days"] = "365",
                ["secret-key"] = secKey,
                ["name"] = "M4c Concurrent QA"
            });
            issueRes.Ok.Should().BeTrue();
            var licenseKey = issueRes.Data["license_key"]!.ToString()!;

            var licSvc = new LicenseService(env.Db);
            var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var tasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
            {
                await barrier.Task;
                return await licSvc.ActivateLicenseKeyAsync(licenseKey);
            })).ToArray();

            barrier.SetResult();
            var results = await Task.WhenAll(tasks);

            results.Should().AllSatisfy(r => r.Ok.Should().BeTrue(r.Reason));
            var count = await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM licenses");
            count.Should().Be(1);
        }
        finally
        {
            LicenseService.SetTestOverrides();
        }
    }

    [Fact]
    public async Task FM406_PaymentService_ConcurrentReverse_AllowsExactlyOneAndPreservesAccountDrawerParity()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@payrev.local", "Admin");
        var (_, _, _, custId, _) = await SeedCatalogAsync(env);

        var acctSvc = new AccountService(env.Db);
        var cashAcc = (await acctSvc.SyncCashDrawerBalanceAsync())!;
        var cashAccId = AccountService.ToLong(cashAcc["id"]);

        var drawerSvc = new CashDrawerService(env.Db);
        var session = await drawerSvc.OpenSessionAsync(adminId, 100m, "Shift 1");
        var sessionId = AccountService.ToLong(session["id"]);

        var invId = await env.Db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["customer_id"] = custId,
            ["user_id"] = adminId,
            ["posted_number"] = "SI-REV-001",
            ["date"] = "2026-11-23",
            ["invoice_type"] = "credit",
            ["payment_method"] = "cash",
            ["total"] = 500m,
            ["total_receive"] = 0m,
            ["status"] = "posted"
        });

        var paySvc = new PaymentService(env.Db);
        var payment = await paySvc.RecordCustomerPaymentAsync(custId, new Dictionary<string, object?>
        {
            ["amount"] = 250m,
            ["payment_method"] = "cash",
            ["account_id"] = cashAccId,
            ["allocations"] = new List<Dictionary<string, object?>>
            {
                new() { ["invoice_id"] = invId, ["amount"] = 250m }
            }
        }, adminId);
        var payId = AccountService.ToLong(payment["id"]);

        (await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashAccId })).Should().Be(350m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId })).Should().Be(350m);

        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8).Select(_ => Task.Run<Exception?>(async () =>
        {
            await barrier.Task;
            try
            {
                await paySvc.ReversePaymentAsync(payId, "Duplicate click", adminId);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        })).ToArray();

        barrier.SetResult();
        var results = await Task.WhenAll(tasks);

        results.Count(r => r == null).Should().Be(1);
        results.Count(r => r is InvalidOperationException).Should().Be(7);

        (await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashAccId })).Should().Be(100m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId })).Should().Be(100m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT total_receive FROM sale_invoices WHERE id = @id", new { id = invId })).Should().Be(0m);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM customer_ledgers WHERE payment_id = @id", new { id = payId })).Should().Be(0);
    }

    [Fact]
    public async Task FM406_PaymentService_ConcurrentChequeClearAndBounce_DepositsAndReversesOnce()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@chq.local", "Admin");
        var (_, _, _, custId, _) = await SeedCatalogAsync(env);

        var bankAccId = await env.Db.InsertAsync("accounts", new Dictionary<string, object?>
        {
            ["name"] = "Meezan Bank",
            ["type"] = "bank",
            ["code"] = "meezan_m4c",
            ["opening_balance"] = 1000m,
            ["current_balance"] = 1000m,
            ["is_active"] = 1,
            ["is_default"] = 1
        });

        var paySvc = new PaymentService(env.Db);
        var cheque = await paySvc.RecordCustomerPaymentAsync(custId, new Dictionary<string, object?>
        {
            ["amount"] = 400m,
            ["payment_method"] = "cheque",
            ["cheque_status"] = "pending",
            ["cheque_number"] = "CHQ-8899",
            ["account_id"] = bankAccId
        }, adminId);
        var payId = AccountService.ToLong(cheque["id"]);

        (await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = bankAccId })).Should().Be(1000m);

        // Concurrent clear -> deposits ONCE (1000 + 400 = 1400)
        var clearBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var clearTasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await clearBarrier.Task;
            return await paySvc.UpdateChequeStatusAsync(payId, "cleared", bankAccId, userId: adminId);
        })).ToArray();
        clearBarrier.SetResult();
        var clearResults = await Task.WhenAll(clearTasks);

        clearResults.Should().AllSatisfy(r => r["cheque_status"]?.ToString().Should().Be("cleared"));
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = bankAccId })).Should().Be(1400m);

        // Concurrent bounce -> reverses ONCE (1400 - 400 = 1000)
        var bounceBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bounceTasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await bounceBarrier.Task;
            return await paySvc.UpdateChequeStatusAsync(payId, "bounced", bankAccId, "Dishonoured", adminId);
        })).ToArray();
        bounceBarrier.SetResult();
        var bounceResults = await Task.WhenAll(bounceTasks);

        bounceResults.Should().AllSatisfy(r =>
        {
            r["status"]?.ToString().Should().Be("reversed");
            r["cheque_status"]?.ToString().Should().Be("bounced");
        });
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = bankAccId })).Should().Be(1000m);
    }

    [Fact]
    public async Task FM406_PaymentService_ConcurrentIdempotencyAndAllocate_NeverOverAllocates()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@alloc.local", "Admin");
        var (_, _, _, custId, _) = await SeedCatalogAsync(env);

        var bankAccId = await env.Db.InsertAsync("accounts", new Dictionary<string, object?>
        {
            ["name"] = "HBL Bank",
            ["type"] = "bank",
            ["code"] = "hbl_m4c",
            ["opening_balance"] = 0m,
            ["current_balance"] = 0m,
            ["is_active"] = 1,
            ["is_default"] = 1
        });

        var invId = await env.Db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["customer_id"] = custId,
            ["user_id"] = adminId,
            ["posted_number"] = "SI-ALLOC-001",
            ["date"] = "2026-11-23",
            ["invoice_type"] = "credit",
            ["payment_method"] = "bank",
            ["total"] = 300m,
            ["total_receive"] = 0m,
            ["status"] = "posted"
        });

        var paySvc = new PaymentService(env.Db);

        // 1. Concurrent idempotency_key creates single payment and deposits once
        var idemBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var idemTasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            await idemBarrier.Task;
            return await paySvc.RecordCustomerPaymentAsync(custId, new Dictionary<string, object?>
            {
                ["amount"] = 100m,
                ["payment_method"] = "bank",
                ["account_id"] = bankAccId,
                ["idempotency_key"] = "idem-m4c-001"
            }, adminId);
        })).ToArray();
        idemBarrier.SetResult();
        var idemResults = await Task.WhenAll(idemTasks);

        var payId = AccountService.ToLong(idemResults[0]["id"]);
        idemResults.Should().AllSatisfy(r => AccountService.ToLong(r["id"]).Should().Be(payId));
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM payments WHERE idempotency_key = 'idem-m4c-001'")).Should().Be(1);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = bankAccId })).Should().Be(100m);

        // 2. Duplicate invoice_id lines within a single AllocatePaymentAsync call exceeding remaining balance or unapplied must throw
        await Assert.ThrowsAsync<InvalidOperationException>(() => paySvc.AllocatePaymentAsync(payId, new List<Dictionary<string, object?>>
        {
            new() { ["invoice_id"] = invId, ["amount"] = 60m },
            new() { ["invoice_id"] = invId, ["amount"] = 60m }
        }, adminId));

        // 3. 6 concurrent AllocatePaymentAsync(60m) calls against 100m unapplied -> at most 1 succeeds (60m allocated, 40m unapplied)
        var allocBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allocTasks = Enumerable.Range(0, 6).Select(_ => Task.Run<Exception?>(async () =>
        {
            await allocBarrier.Task;
            try
            {
                await paySvc.AllocatePaymentAsync(payId, new List<Dictionary<string, object?>>
                {
                    new() { ["invoice_id"] = invId, ["amount"] = 60m }
                }, adminId);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        })).ToArray();
        allocBarrier.SetResult();
        var allocResults = await Task.WhenAll(allocTasks);

        allocResults.Count(r => r == null).Should().Be(1);
        allocResults.Count(r => r is InvalidOperationException).Should().Be(5);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT allocated_amount FROM payments WHERE id = @id", new { id = payId })).Should().Be(60m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT unapplied_amount FROM payments WHERE id = @id", new { id = payId })).Should().Be(40m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT total_receive FROM sale_invoices WHERE id = @id", new { id = invId })).Should().Be(60m);

        // 4. ApplyPartyCreditsAsync applies remaining 40m atomically
        var applied = await paySvc.ApplyPartyCreditsAsync("customer", custId, new List<Dictionary<string, object?>>
        {
            new() { ["invoice_id"] = invId, ["amount"] = 40m }
        }, adminId);
        AccountService.ToDecimal(applied["allocated_total"]).Should().Be(40m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT unapplied_amount FROM payments WHERE id = @id", new { id = payId })).Should().Be(0m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT total_receive FROM sale_invoices WHERE id = @id", new { id = invId })).Should().Be(100m);
    }

    [Fact]
    public async Task FM408_AccountingService_CreateRollbackAndConcurrentReverse_PreservesTreasuryAndDrawerParity()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@jv.local", "Admin");

        var acctSvc = new AccountService(env.Db);
        var cashAcc = (await acctSvc.SyncCashDrawerBalanceAsync())!;
        var cashAccId = AccountService.ToLong(cashAcc["id"]);

        var drawerSvc = new CashDrawerService(env.Db);
        var session = await drawerSvc.OpenSessionAsync(adminId, 100m, "JV Shift");
        var sessionId = AccountService.ToLong(session["id"]);

        var accSvc = new AccountingService(env.Db);
        await accSvc.EnsureDefaultsAsync();

        var cashCoaId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM chart_of_accounts WHERE system_key = 'cash_and_banks' LIMIT 1");
        var capitalCoaId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM chart_of_accounts WHERE system_key = 'owners_capital' LIMIT 1");

        var jvPayload = new Dictionary<string, object?>
        {
            ["entry_date"] = "2026-11-23",
            ["account_id"] = cashAccId,
            ["description"] = "Owner Cash Injection",
            ["lines"] = new List<Dictionary<string, object?>>
            {
                new() { ["chart_of_account_id"] = cashCoaId, ["debit"] = 500m, ["credit"] = 0m },
                new() { ["chart_of_account_id"] = capitalCoaId, ["debit"] = 0m, ["credit"] = 500m }
            }
        };

        // 1. Simulate treasury failure inside CreateJournalEntryAsync -> entire JV & lines must roll back (0 orphans!)
        await env.Db.ExecuteAsync(@"
            CREATE TRIGGER m4c_fail_acc_txn BEFORE INSERT ON account_transactions
            BEGIN
                SELECT RAISE(ABORT, 'Simulated treasury failure');
            END;");
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => accSvc.CreateJournalEntryAsync(jvPayload, adminId));
            (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM journal_entries")).Should().Be(0);
            (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM journal_entry_lines")).Should().Be(0);
            (await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashAccId })).Should().Be(100m);
            (await env.Db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId })).Should().Be(100m);
        }
        finally
        {
            await env.Db.ExecuteAsync("DROP TRIGGER IF EXISTS m4c_fail_acc_txn;");
        }

        // 2. Normal CreateJournalEntryAsync succeeds atomically and updates cash account + cash drawer
        var createdJv = await accSvc.CreateJournalEntryAsync(jvPayload, adminId);
        var jvId = AccountService.ToLong(createdJv["id"]);

        (await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashAccId })).Should().Be(600m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId })).Should().Be(600m);

        // 3. 8 concurrent ReverseJournalEntryAsync -> 1 succeeds, 7 throw InvalidOperationException, parity restored to 100m
        var revBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revTasks = Enumerable.Range(0, 8).Select(_ => Task.Run<Exception?>(async () =>
        {
            await revBarrier.Task;
            try
            {
                await accSvc.ReverseJournalEntryAsync(jvId, "Duplicate JV", adminId);
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        })).ToArray();

        revBarrier.SetResult();
        var revResults = await Task.WhenAll(revTasks);

        revResults.Count(r => r == null).Should().Be(1);
        revResults.Count(r => r is InvalidOperationException).Should().Be(7);

        (await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = cashAccId })).Should().Be(100m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT closing_balance_expected FROM cash_drawer_sessions WHERE id = @id", new { id = sessionId })).Should().Be(100m);
        (await env.Db.ExecuteScalarAsync<string>("SELECT status FROM journal_entries WHERE id = @id", new { id = jvId })).Should().Be("reversed");
    }
}

using System.IO.Compression;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Dapper;
using FluentAssertions;
using Karobar.Api.Controllers;
using Karobar.Background;
using Karobar.Data;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Karobar.Tests;

public class Milestone3ConcurrencyAndCalcTests
{
    private static readonly byte[] ValidPngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static readonly byte[] ValidPdfBytes =
        Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<>>\nendobj\ntrailer\n<<>>\n%%EOF\n");

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
            var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_m3_{Guid.NewGuid():N}");
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

        public async Task<long> SeedUserAsync(
            string name,
            string email,
            string roleName,
            string? passwordPlain = null,
            string status = "active",
            IEnumerable<string>? directPermissions = null)
        {
            await EnsureRoleAsync(roleName);
            var hash = string.IsNullOrEmpty(passwordPlain)
                ? "$2y$10$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi"
                : BCrypt.Net.BCrypt.HashPassword(passwordPlain, 4);

            var uid = await Db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = name,
                ["email"] = email,
                ["password"] = hash,
                ["status"] = status
            });
            await Db.SyncUserRolesAsync(uid, [roleName]);

            if (directPermissions != null)
            {
                var list = directPermissions.ToList();
                foreach (var p in list) await EnsurePermissionAsync(p);
                await Db.SyncUserPermissionsAsync(uid, list);
            }

            return uid;
        }

        public ValueTask DisposeAsync()
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", _prevRootEnv);
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, recursive: true); } catch { }
            return ValueTask.CompletedTask;
        }
    }

    private static DefaultHttpContext MakeJsonHttpContext(long userId, object? payload = null)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            ], "Sanctum"))
        };
        ctx.Items["KarobarUserId"] = userId;
        if (payload != null)
        {
            var json = JsonSerializer.Serialize(payload);
            ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(json));
            ctx.Request.ContentType = "application/json";
        }
        return ctx;
    }

    private static DefaultHttpContext MakeMultipartHttpContext(
        long userId,
        Dictionary<string, string>? fields = null,
        string? fileField = null,
        string? fileName = null,
        string? contentType = null,
        byte[]? fileBytes = null)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString())
            ], "Sanctum"))
        };
        ctx.Items["KarobarUserId"] = userId;
        ctx.Request.ContentType = "multipart/form-data; boundary=----KarobarM3Boundary";

        var formDict = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        if (fields != null)
        {
            foreach (var kv in fields)
            {
                formDict[kv.Key] = new StringValues(kv.Value);
            }
        }

        var files = new FormFileCollection();
        if (fileField != null && fileName != null && fileBytes != null)
        {
            var stream = new MemoryStream(fileBytes);
            var ff = new FormFile(stream, 0, fileBytes.Length, fileField, fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType ?? "application/octet-stream"
            };
            files.Add(ff);
        }

        ctx.Request.Form = new Microsoft.AspNetCore.Http.FormCollection(formDict, files);
        return ctx;
    }

    private static int GetStatus(IActionResult result)
    {
        return (result as IStatusCodeActionResult)?.StatusCode ?? 0;
    }

    private static JsonDocument ParseBody(IActionResult result)
    {
        var obj = result as ObjectResult;
        obj.Should().NotBeNull();
        return JsonDocument.Parse(JsonSerializer.Serialize(obj!.Value));
    }

    private static async Task<(long CatId, long BrandId, long SuppId, long CustId, long ProductId)> SeedCatalogAsync(
        IsolatedEnv env,
        int initialQty = 10,
        decimal unitSale = 100m,
        decimal unitPurchase = 50m,
        decimal maxDiscount = 100m)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var catId = await env.Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = $"CatM3-{suffix}" });
        var brandId = await env.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = $"BrandM3-{suffix}" });
        var suppId = await env.Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = $"SuppM3-{suffix}" });
        var existingWalkIn = await env.Db.QuerySingleOrDefaultAsync("SELECT id FROM customers WHERE name = 'Walk-In Customer' LIMIT 1");
        var custId = existingWalkIn != null
            ? Convert.ToInt64(existingWalkIn["id"])
            : await env.Db.InsertAsync("customers", new Dictionary<string, object?> { ["name"] = "Walk-In Customer" });
        var prodId = await env.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = $"PRD-{suffix}",
            ["barcode"] = $"BC-{suffix}",
            ["name"] = $"M3 Test Product {suffix}",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["supplier_id"] = suppId,
            ["pack_size"] = 1,
            ["quantity"] = initialQty,
            ["pack_purchase_price"] = unitPurchase,
            ["unit_purchase_price"] = unitPurchase,
            ["avg_price"] = unitPurchase,
            ["pack_sale_price"] = unitSale,
            ["unit_sale_price"] = unitSale,
            ["max_discount"] = maxDiscount,
            ["narcotic"] = "no"
        });
        return (catId, brandId, suppId, custId, prodId);
    }

    // =========================================================================
    // ISS-08: SaleInvoiceController Prescription Upload & Deletion Containment
    // =========================================================================

    [Fact]
    public async Task ISS08_SaleInvoice_PrescriptionUploadValidation_And_SafeDeletion()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@rx.local", "Admin");
        var (_, _, _, custId, prodId) = await SeedCatalogAsync(env, initialQty: 20);

        var saleId = await env.Db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = adminId,
            ["customer_id"] = custId,
            ["invoice_type"] = "debit",
            ["payment_method"] = "cash",
            ["sale_type"] = "retail",
            ["posted_number"] = "SI-000001",
            ["date"] = "2026-11-23",
            ["gross_amount"] = 100m,
            ["total"] = 100m,
            ["total_receive"] = 100m
        });

        SaleInvoiceController MakeCtrl(DefaultHttpContext ctx) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };

        // 1. Missing file -> 422
        var missingRes = await MakeCtrl(MakeMultipartHttpContext(adminId)).UploadPrescription(saleId);
        GetStatus(missingRes).Should().Be(422);

        // 2. Reject 0-byte, spoofed HTML/SVG, mismatched MIME, and >5MB uploads without writing any file
        foreach (var (badName, badMime, badBytes) in new (string, string, byte[])[]
        {
            ("empty.png", "image/png", []),
            ("xss.html", "text/html", Encoding.UTF8.GetBytes("<script>alert(1)</script>")),
            ("xss.svg", "image/svg+xml", Encoding.UTF8.GetBytes("<svg onload=alert(1)/>")),
            ("spoof.jpg", "image/jpeg", Encoding.UTF8.GetBytes("<script>alert(1)</script>")),
            ("mismatch.jpg", "image/png", ValidPngBytes),
            ("toobig.pdf", "application/pdf", ValidPdfBytes.Concat(new byte[5 * 1024 * 1024 + 32]).ToArray())
        })
        {
            var badCtx = MakeMultipartHttpContext(adminId, null, "prescription", badName, badMime, badBytes);
            var badRes = await MakeCtrl(badCtx).UploadPrescription(saleId);
            GetStatus(badRes).Should().Be(422, $"file {badName} must be rejected");
            using var doc = ParseBody(badRes);
            doc.RootElement.GetProperty("errors").TryGetProperty("prescription", out _).Should().BeTrue();
        }

        var prescDir = env.Db.StoragePath("app/public/prescriptions");
        if (Directory.Exists(prescDir))
        {
            Directory.GetFiles(prescDir).Should().BeEmpty();
        }

        // 3. Valid PNG upload succeeds and updates DB
        var pngRes = await MakeCtrl(MakeMultipartHttpContext(adminId, new() { ["prescription_no"] = "RX-101" }, "prescription", "rx.png", "image/png", ValidPngBytes))
            .UploadPrescription(saleId);
        GetStatus(pngRes).Should().Be(200);

        string firstRelPath;
        using (var doc = ParseBody(pngRes))
        {
            firstRelPath = doc.RootElement.GetProperty("prescription_image_path").GetString()!;
            doc.RootElement.GetProperty("prescription_no").GetString().Should().Be("RX-101");
        }
        firstRelPath.Should().StartWith("prescriptions/").And.EndWith(".png");
        var firstFullPath = env.Db.StoragePath($"app/public/{firstRelPath}");
        File.Exists(firstFullPath).Should().BeTrue();

        // 4. Replacing PNG with valid PDF deletes old PNG and stores PDF
        var pdfRes = await MakeCtrl(MakeMultipartHttpContext(adminId, null, "prescription", "rx.pdf", "application/pdf", ValidPdfBytes))
            .UploadPrescription(saleId);
        GetStatus(pdfRes).Should().Be(200);
        File.Exists(firstFullPath).Should().BeFalse();

        string secondRelPath;
        using (var doc = ParseBody(pdfRes))
        {
            secondRelPath = doc.RootElement.GetProperty("prescription_image_path").GetString()!;
        }
        secondRelPath.Should().StartWith("prescriptions/").And.EndWith(".pdf");
        var secondFullPath = env.Db.StoragePath($"app/public/{secondRelPath}");
        File.Exists(secondFullPath).Should().BeTrue();

        // 5. Path traversal seeded in DB must NOT delete outside.txt during DeletePrescription or UploadPrescription
        var outsideFile = Path.Combine(env.TempRoot, "outside.txt");
        await File.WriteAllTextAsync(outsideFile, "KEEP_ME");
        foreach (var traversalPath in new[] { "prescriptions/../../outside.txt", @"..\outside.txt" })
        {
            await env.Db.ExecuteAsync("UPDATE sale_invoices SET prescription_image_path = @p WHERE id = @id", new { p = traversalPath, id = saleId });
            GetStatus(await MakeCtrl(MakeJsonHttpContext(adminId)).DeletePrescription(saleId)).Should().Be(200);
            File.Exists(outsideFile).Should().BeTrue();
            var cleared = await env.Db.ExecuteScalarAsync<string?>("SELECT prescription_image_path FROM sale_invoices WHERE id = @id", new { id = saleId });
            cleared.Should().BeNull();
        }

        // 6. DeletePrescription clears DB and deletes contained prescription file
        await env.Db.ExecuteAsync("UPDATE sale_invoices SET prescription_image_path = @p WHERE id = @id", new { p = secondRelPath, id = saleId });
        GetStatus(await MakeCtrl(MakeJsonHttpContext(adminId)).DeletePrescription(saleId)).Should().Be(200);
        File.Exists(secondFullPath).Should().BeFalse();

        // 7. Destroy cleans up contained prescription file after commit
        var thirdRes = await MakeCtrl(MakeMultipartHttpContext(adminId, null, "prescription", "rx3.png", "image/png", ValidPngBytes))
            .UploadPrescription(saleId);
        GetStatus(thirdRes).Should().Be(200);
        string thirdRelPath;
        using (var doc = ParseBody(thirdRes))
        {
            thirdRelPath = doc.RootElement.GetProperty("prescription_image_path").GetString()!;
        }
        var thirdFullPath = env.Db.StoragePath($"app/public/{thirdRelPath}");
        File.Exists(thirdFullPath).Should().BeTrue();

        GetStatus(await MakeCtrl(MakeJsonHttpContext(adminId)).Destroy(saleId)).Should().Be(200);
        File.Exists(thirdFullPath).Should().BeFalse();
    }

    // =========================================================================
    // ISS-11: TerminalController QuickImportDatabase & WAL-Safe Backup
    // =========================================================================

    [Fact]
    public async Task ISS11_TerminalController_QuickImport_ZipGuards_And_WalSafeBackup()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@term.local", "Admin");

        // Seed an uncheckpointed WAL row on a separate connection before QuickImport
        await env.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "PreImportWalBrand" });

        var exportSvc = new SqliteExportService(env.Db);
        TerminalController MakeCtrl(DefaultHttpContext ctx) => new(
            env.Db,
            new LicenseService(env.Db),
            exportSvc,
            new BackupService(env.Db, exportSvc),
            new DocumentSequenceService(env.Db))
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };

        // 1. Invalid extension rejected cleanly with 422
        var badExtCtx = MakeMultipartHttpContext(adminId, new() { ["mode"] = "merge" }, "file", "bad.txt", "text/plain", Encoding.UTF8.GetBytes("hello"));
        GetStatus(await MakeCtrl(badExtCtx).QuickImportDatabase()).Should().Be(422);

        // 2. ZIP with only __MACOSX metadata & 0-byte sqlite entry rejected with 422
        byte[] macOnlyZipBytes;
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var macEntry = zip.CreateEntry("__MACOSX/._ghost.sqlite");
                using (var s = macEntry.Open())
                {
                    s.Write(Encoding.ASCII.GetBytes("SQLite format 3\0"));
                }
                zip.CreateEntry("empty.sqlite");
            }
            macOnlyZipBytes = ms.ToArray();
        }
        var macOnlyCtx = MakeMultipartHttpContext(adminId, new() { ["mode"] = "merge" }, "file", "maconly.zip", "application/zip", macOnlyZipBytes);
        GetStatus(await MakeCtrl(macOnlyCtx).QuickImportDatabase()).Should().Be(422);

        // 3. CopyBoundedAsync rejects streams exceeding limit
        using (var overSrc = new MemoryStream(new byte[2048]))
        using (var overDst = new MemoryStream())
        {
            await FluentActions.Invoking(() => TerminalController.CopyBoundedAsync(overSrc, overDst, 1024, CancellationToken.None))
                .Should().ThrowAsync<InvalidOperationException>();
        }

        // 4. Crafted ZIP declaring uncompressed entry size > 2 GB rejected with 422 without exhausting disk
        byte[] zipBombHeaderBytes = CreateSpoofedLargeSizeZipBytes("bomb.sqlite", 3L * 1024 * 1024 * 1024);
        var bombCtx = MakeMultipartHttpContext(adminId, new() { ["mode"] = "merge" }, "file", "bomb.zip", "application/zip", zipBombHeaderBytes);
        GetStatus(await MakeCtrl(bombCtx).QuickImportDatabase()).Should().Be(422);

        // 5. Valid ZIP containing __MACOSX/._fake.sqlite + real data.sqlite imports real data AND creates WAL-safe backup
        var srcDbPath = Path.Combine(env.TempRoot, "src_quick.sqlite");
        await using (var srcConn = new SqliteConnection($"Data Source={srcDbPath};Pooling=False;"))
        {
            await srcConn.OpenAsync();
            await srcConn.ExecuteAsync(@"
                CREATE TABLE brands (id INTEGER PRIMARY KEY, name TEXT NOT NULL, created_at TEXT, updated_at TEXT);
                INSERT INTO brands (id, name, created_at, updated_at) VALUES (8881, 'ImportedFromZipBrand', datetime('now'), datetime('now'));");
        }
        SqliteConnection.ClearAllPools();
        var srcDbBytes = await File.ReadAllBytesAsync(srcDbPath);

        byte[] validZipBytes;
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var macEntry = zip.CreateEntry("__MACOSX/._data.sqlite");
                using (var s = macEntry.Open())
                {
                    var junk = new byte[srcDbBytes.Length + 100];
                    Encoding.ASCII.GetBytes("AppleDoubleJunk").CopyTo(junk, 0);
                    s.Write(junk);
                }
                var realEntry = zip.CreateEntry("backup/data.sqlite");
                using (var s = realEntry.Open())
                {
                    s.Write(srcDbBytes);
                }
            }
            validZipBytes = ms.ToArray();
        }

        // Hold an active WAL frame via another connection that has inserted + committed a brand right before QuickImport
        await using (var walConn = await env.Db.OpenConnectionAsync())
        {
            await walConn.ExecuteAsync("INSERT INTO brands (name) VALUES ('LiveWalCommittedBrand')");
        }

        var validCtx = MakeMultipartHttpContext(
            adminId,
            new() { ["mode"] = "merge", ["conflict_policy"] = "upsert" },
            "file",
            "../../evil_name.zip",
            "application/zip",
            validZipBytes);
        var importRes = await MakeCtrl(validCtx).QuickImportDatabase();
        GetStatus(importRes).Should().Be(200);

        var importedBrand = await env.Db.QuerySingleOrDefaultAsync("SELECT id FROM brands WHERE name = 'ImportedFromZipBrand'");
        importedBrand.Should().NotBeNull();

        // Verify pre-quick-import backup exists, is non-empty, and contains WAL-committed row!
        var backupFiles = Directory.GetFiles(env.Db.StoragePath("app/backups"), "pre-quick-import-*.sqlite");
        backupFiles.Should().HaveCount(1);
        new FileInfo(backupFiles[0]).Length.Should().BeGreaterThan(0);

        await using (var bkConn = new SqliteConnection($"Data Source={backupFiles[0]};Mode=ReadOnly;Pooling=False;"))
        {
            await bkConn.OpenAsync();
            var walRowCount = await bkConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM brands WHERE name = 'LiveWalCommittedBrand'");
            walRowCount.Should().Be(1);
        }
    }

    private static byte[] CreateSpoofedLargeSizeZipBytes(string entryName, long fakeUncompressedSize)
    {
        byte[] zipBytes;
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
                using var s = entry.Open();
                s.Write(Encoding.ASCII.GetBytes("SQLite format 3\0"));
            }
            zipBytes = ms.ToArray();
        }

        // Locate End of Central Directory (0x06054b50) to find Central Directory offset
        int eocdOffset = -1;
        for (int i = zipBytes.Length - 22; i >= 0; i--)
        {
            if (zipBytes[i] == 0x50 && zipBytes[i + 1] == 0x4B && zipBytes[i + 2] == 0x05 && zipBytes[i + 3] == 0x06)
            {
                eocdOffset = i;
                break;
            }
        }
        eocdOffset.Should().BeGreaterThan(0);
        int cdOffset = BitConverter.ToInt32(zipBytes, eocdOffset + 16);

        // Patch uncompressed size (uint32) in Local File Header (+22) and Central Directory Header (+24)
        uint patchedSize = (uint)Math.Min(uint.MaxValue - 1, fakeUncompressedSize);
        BitConverter.GetBytes(patchedSize).CopyTo(zipBytes, 22);
        BitConverter.GetBytes(patchedSize).CopyTo(zipBytes, cdOffset + 24);
        return zipBytes;
    }

    // =========================================================================
    // ISS-12: BEGIN IMMEDIATE & Concurrency / TOCTOU Races
    // =========================================================================

    [Fact]
    public async Task ISS12_KarobarDb_BeginImmediateTransactionAsync_SerializesReadThenWrite()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var accId = await env.Db.InsertAsync("accounts", new Dictionary<string, object?>
        {
            ["name"] = "Contention Acc",
            ["type"] = "cash",
            ["code"] = "cnt_acc",
            ["opening_balance"] = 100m,
            ["current_balance"] = 100m,
            ["is_active"] = 1
        });

        const int workers = 8;
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, workers).Select(async _ =>
        {
            await barrier.Task;
            await using var conn = await env.Db.OpenConnectionAsync();
            await using var tx = await KarobarDb.BeginImmediateTransactionAsync(conn);
            var row = await env.Db.QuerySingleOrDefaultAsync("SELECT current_balance FROM accounts WHERE id = @id", new { id = accId }, conn, tx);
            var bal = AccountService.ToDecimal(row!["current_balance"]);
            await Task.Delay(10);
            await env.Db.UpdateAsync("accounts", accId, new Dictionary<string, object?> { ["current_balance"] = bal + 25m }, "id", conn, tx);
            await tx.CommitAsync();
        }).ToArray();

        barrier.SetResult();
        await Task.WhenAll(tasks);

        var finalBal = await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = accId });
        finalBal.Should().Be(100m + (workers * 25m));
    }

    [Fact]
    public async Task ISS12_AccountService_ConcurrentOperations_PreserveExactBalances()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var accA = await env.Db.InsertAsync("accounts", new Dictionary<string, object?>
        {
            ["name"] = "Vault A",
            ["type"] = "bank",
            ["code"] = "vault_a",
            ["opening_balance"] = 1000m,
            ["current_balance"] = 1000m,
            ["is_active"] = 1
        });
        var accB = await env.Db.InsertAsync("accounts", new Dictionary<string, object?>
        {
            ["name"] = "Vault B",
            ["type"] = "bank",
            ["code"] = "vault_b",
            ["opening_balance"] = 500m,
            ["current_balance"] = 500m,
            ["is_active"] = 1
        });

        var svc = new AccountService(env.Db);
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ops = new List<Task>();

        for (int i = 0; i < 4; i++)
        {
            ops.Add(Task.Run(async () => { await barrier.Task; await svc.DepositAsync(accA, 100m, "test"); }));
            ops.Add(Task.Run(async () => { await barrier.Task; await svc.WithdrawAsync(accA, 40m, "test"); }));
            ops.Add(Task.Run(async () => { await barrier.Task; await svc.TransferAsync(accA, accB, 50m, "sync"); }));
        }

        barrier.SetResult();
        await Task.WhenAll(ops);

        var finalA = await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = accA });
        var finalB = await env.Db.ExecuteScalarAsync<decimal>("SELECT current_balance FROM accounts WHERE id = @id", new { id = accB });
        finalA.Should().Be(1000m + 4 * 100m - 4 * 40m - 4 * 50m); // 1040m
        finalB.Should().Be(500m + 4 * 50m); // 700m
    }

    [Fact]
    public async Task ISS12_StockDisposalService_ConcurrentCreateFinalizeRestore_PreventOversellAndDoubleRestore()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@disp.local", "Admin");
        var (_, _, _, _, prodId) = await SeedCatalogAsync(env, initialQty: 5);
        var batchId = await env.Db.InsertAsync("batches", new Dictionary<string, object?>
        {
            ["product_id"] = prodId,
            ["batch_number"] = "B-CONC-1",
            ["expiry_date"] = "2028-12-31",
            ["quantity"] = 5
        });

        var svc = new StockDisposalService(env.Db);
        var barrier1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<bool> TryCreateAsync()
        {
            await barrier1.Task;
            try
            {
                await svc.CreateDisposalAsync(new Dictionary<string, object?>
                {
                    ["status"] = StockDisposalService.StatusQuarantined,
                    ["disposal_date"] = "2026-11-23",
                    ["cause"] = "shelf_expiry",
                    ["items"] = new List<Dictionary<string, object?>>
                    {
                        new()
                        {
                            ["product_id"] = prodId,
                            ["batch_id"] = batchId,
                            ["dispose_qty"] = 4
                        }
                    }
                }, adminId);
                return true;
            }
            catch (StockDisposalValidationException)
            {
                return false;
            }
        }

        var t1 = TryCreateAsync();
        var t2 = TryCreateAsync();
        barrier1.SetResult();
        var createResults = await Task.WhenAll(t1, t2);
        createResults.Count(r => r).Should().Be(1, "only one concurrent disposal can take 4 of 5 units");
        createResults.Count(r => !r).Should().Be(1);

        (await env.Db.ExecuteScalarAsync<decimal>("SELECT quantity FROM batches WHERE id = @id", new { id = batchId })).Should().Be(1m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT quantity FROM products WHERE id = @id", new { id = prodId })).Should().Be(1m);

        var disposalId = await env.Db.ExecuteScalarAsync<long>("SELECT id FROM stock_disposals ORDER BY id DESC LIMIT 1");

        // Concurrent FinalizeDisposalAsync -> only 1 succeeds and creates 1 expense
        var barrier2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> TryFinalizeAsync()
        {
            await barrier2.Task;
            try
            {
                await svc.FinalizeDisposalAsync(disposalId, new Dictionary<string, object?>
                {
                    ["disposal_method"] = "incineration",
                    ["record_expense"] = true
                }, adminId);
                return true;
            }
            catch (StockDisposalValidationException)
            {
                return false;
            }
        }
        var f1 = TryFinalizeAsync();
        var f2 = TryFinalizeAsync();
        barrier2.SetResult();
        var finResults = await Task.WhenAll(f1, f2);
        finResults.Count(r => r).Should().Be(1);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM expenses")).Should().Be(1);

        // Concurrent RestoreFromQuarantineAsync -> only 1 succeeds and restores batch/product to 5
        var barrier3 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<bool> TryRestoreAsync()
        {
            await barrier3.Task;
            try
            {
                await svc.RestoreFromQuarantineAsync(disposalId, "False alarm", adminId);
                return true;
            }
            catch (StockDisposalValidationException)
            {
                return false;
            }
        }
        var r1 = TryRestoreAsync();
        var r2 = TryRestoreAsync();
        barrier3.SetResult();
        var restResults = await Task.WhenAll(r1, r2);
        restResults.Count(r => r).Should().Be(1);

        (await env.Db.ExecuteScalarAsync<decimal>("SELECT quantity FROM batches WHERE id = @id", new { id = batchId })).Should().Be(5m);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT quantity FROM products WHERE id = @id", new { id = prodId })).Should().Be(5m);
    }

    [Fact]
    public async Task ISS12_StockAdjustmentController_ConcurrentStoreAndDestroy_SerializeSequencesAndRevertOnce()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@adj.local", "Admin");

        var productIds = new List<long>();
        for (int i = 0; i < 4; i++)
        {
            var (_, _, _, _, pid) = await SeedCatalogAsync(env, initialQty: 10);
            productIds.Add(pid);
        }

        StockAdjustmentController MakeCtrl(long uid, object? payload = null) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(uid, payload) }
        };

        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var storeTasks = productIds.Select(async pid =>
        {
            await barrier.Task;
            var res = await MakeCtrl(adminId, new
            {
                posted_date = "2026-11-23",
                note = "Concurrent count",
                items = new[] { new { product_id = pid, actual_qty = 15 } }
            }).Store();
            return res;
        }).ToArray();

        barrier.SetResult();
        var storeResults = await Task.WhenAll(storeTasks);
        storeResults.Should().OnlyContain(r => GetStatus(r) == 200);

        var postedNumbers = await env.Db.QueryListAsync("SELECT id, posted_number FROM stock_adjustments ORDER BY id ASC");
        postedNumbers.Should().HaveCount(4);
        postedNumbers.Select(r => r["posted_number"]!.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).Should().HaveCount(4);

        // Concurrent Destroy on the first adjustment: 1 returns 204, 1 returns 404, its product reverted once (15 -> 10)
        var targetAdjId = Convert.ToInt64(postedNumbers[0]["id"]);
        var targetProdId = await env.Db.ExecuteScalarAsync<long>(
            "SELECT product_id FROM stock_adjustment_items WHERE stock_adjustment_id = @id LIMIT 1",
            new { id = targetAdjId });
        var destroyBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var d1 = Task.Run(async () => { await destroyBarrier.Task; return await MakeCtrl(adminId).Destroy(targetAdjId); });
        var d2 = Task.Run(async () => { await destroyBarrier.Task; return await MakeCtrl(adminId).Destroy(targetAdjId); });
        destroyBarrier.SetResult();
        var dResults = await Task.WhenAll(d1, d2);

        dResults.Select(GetStatus).Should().BeEquivalentTo([204, 404]);
        (await env.Db.ExecuteScalarAsync<decimal>("SELECT quantity FROM products WHERE id = @id", new { id = targetProdId })).Should().Be(10m);
    }

    [Fact]
    public async Task ISS12_SaleInvoiceController_ConcurrentLastUnit_DraftIdempotency_And_Reverse()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@saleconc.local", "Admin");
        var (_, _, _, custId, prodLastId) = await SeedCatalogAsync(env, initialQty: 1, unitSale: 100m);
        var (_, _, _, _, prodDraftId) = await SeedCatalogAsync(env, initialQty: 10, unitSale: 50m);

        await env.Db.InsertAsync("accounts", new Dictionary<string, object?>
        {
            ["name"] = "Cash in Hand",
            ["type"] = "cash",
            ["code"] = "cash_drawer",
            ["opening_balance"] = 0m,
            ["current_balance"] = 0m,
            ["is_active"] = 1,
            ["is_default"] = 1
        });

        SaleInvoiceController MakeCtrl(DefaultHttpContext ctx) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };

        // 1. Concurrent last-unit sale: 1 returns 201, 1 returns 422, stock == 0
        var barrier1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> SellLastUnitAsync()
        {
            await barrier1.Task;
            var res = await MakeCtrl(MakeJsonHttpContext(adminId)).ExecuteStoreSaleInvoiceAsync(
                new Dictionary<string, object?>
                {
                    ["customer_id"] = custId,
                    ["date"] = "2026-11-23",
                    ["invoice_type"] = "debit",
                    ["payment_method"] = "cash",
                    ["sale_type"] = "retail"
                },
                [new Dictionary<string, object?> { ["product_id"] = prodLastId, ["quantity"] = 1, ["price"] = 100m }],
                []);
            return GetStatus(res);
        }

        var s1 = SellLastUnitAsync();
        var s2 = SellLastUnitAsync();
        barrier1.SetResult();
        var sStatuses = await Task.WhenAll(s1, s2);
        sStatuses.Should().BeEquivalentTo([201, 422]);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = prodLastId })).Should().Be(0);

        // 2. Concurrent draft posting: exactly 1 invoice created, 1 returns 201 and 1 returns 200 idempotent
        var draftId = await env.Db.InsertAsync("invoice_drafts", new Dictionary<string, object?>
        {
            ["user_id"] = adminId,
            ["draft_type"] = "sale_invoice",
            ["status"] = "draft",
            ["draft_data"] = "{}"
        });

        var barrier2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<int> PostDraftAsync()
        {
            await barrier2.Task;
            var res = await MakeCtrl(MakeJsonHttpContext(adminId)).ExecuteStoreSaleInvoiceAsync(
                new Dictionary<string, object?>
                {
                    ["customer_id"] = custId,
                    ["draft_id"] = draftId,
                    ["date"] = "2026-11-23",
                    ["invoice_type"] = "debit",
                    ["payment_method"] = "cash",
                    ["sale_type"] = "retail"
                },
                [new Dictionary<string, object?> { ["product_id"] = prodDraftId, ["quantity"] = 2, ["price"] = 50m }],
                []);
            return GetStatus(res);
        }

        var d1 = PostDraftAsync();
        var d2 = PostDraftAsync();
        barrier2.SetResult();
        var draftStatuses = await Task.WhenAll(d1, d2);
        draftStatuses.Should().BeEquivalentTo([201, 200]);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = prodDraftId })).Should().Be(8);

        var draftInvId = await env.Db.ExecuteScalarAsync<long>("SELECT posted_invoice_id FROM invoice_drafts WHERE id = @id", new { id = draftId });

        // 3. Concurrent Reverse on the same invoice: 1 returns 200, 1 returns 422, stock restored once (8 -> 10)
        var revBarrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rev1 = Task.Run(async () =>
        {
            await revBarrier.Task;
            return GetStatus(await MakeCtrl(MakeJsonHttpContext(adminId, new { reason = "Customer cancelled order" })).Reverse(draftInvId));
        });
        var rev2 = Task.Run(async () =>
        {
            await revBarrier.Task;
            return GetStatus(await MakeCtrl(MakeJsonHttpContext(adminId, new { reason = "Customer cancelled order" })).Reverse(draftInvId));
        });
        revBarrier.SetResult();
        var revStatuses = await Task.WhenAll(rev1, rev2);
        revStatuses.Should().BeEquivalentTo([200, 422]);
        (await env.Db.ExecuteScalarAsync<int>("SELECT quantity FROM products WHERE id = @id", new { id = prodDraftId })).Should().Be(10);
    }

    // =========================================================================
    // ISS-13: Decimal Precision, Supervisor Override Hardening & Purchase Uniqueness
    // =========================================================================

    [Fact]
    public async Task ISS13_InvoiceCalculationService_DecimalPrecisionAndAwayFromZeroRounding()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@calc.local", "Admin");
        var (_, _, _, _, prodId1) = await SeedCatalogAsync(env, initialQty: 50, unitSale: 10m, unitPurchase: 5m, maxDiscount: 100m);
        var (_, _, _, _, prodId2) = await SeedCatalogAsync(env, initialQty: 50, unitSale: 10m, unitPurchase: 5m, maxDiscount: 100m);

        var calc = new InvoiceCalculationService(env.Db);

        // 1. Exact decimal addition (0.1 + 0.2 == 0.30, zero binary float drift)
        var exactRes = await calc.CalculateAndValidateSaleAsync(
            new Dictionary<string, object?> { ["sale_type"] = "retail" },
            [
                new Dictionary<string, object?> { ["product_id"] = prodId1, ["quantity"] = 1, ["price"] = 0.1m },
                new Dictionary<string, object?> { ["product_id"] = prodId2, ["quantity"] = 1, ["price"] = 0.2m }
            ],
            userId: adminId);
        exactRes["gross_amount"].Should().Be(0.30m);

        // 2. MidpointRounding.AwayFromZero on line discount (5% of 10.50 = 0.525 -> 0.53) and sale total (10.50 -> 11)
        var roundSaleRes = await calc.CalculateAndValidateSaleAsync(
            new Dictionary<string, object?> { ["sale_type"] = "retail" },
            [new Dictionary<string, object?> { ["product_id"] = prodId1, ["quantity"] = 1, ["price"] = 10.50m }],
            userId: adminId);
        roundSaleRes["total"].Should().Be(11m);

        var discMidRes = await calc.CalculateAndValidateSaleAsync(
            new Dictionary<string, object?> { ["sale_type"] = "retail" },
            [new Dictionary<string, object?> { ["product_id"] = prodId1, ["quantity"] = 1, ["price"] = 10.50m, ["item_discount_percentage"] = 5m }],
            userId: adminId);
        discMidRes["item_discount"].Should().Be(0.53m);
        discMidRes["total"].Should().Be(10m);

        // 3. Purchase avg_price 4-decimal AwayFromZero & Sale/Purchase Return exact decimal math
        var purchRes = calc.CalculateAndValidatePurchase(
            new Dictionary<string, object?>(),
            [
                new Dictionary<string, object?>
                {
                    ["product_id"] = prodId1,
                    ["pack_size"] = 1,
                    ["unit_quantity"] = 2,
                    ["unit_purchase_price"] = 1.0005m, // sub_total = 2.001 -> Round2 = 2.00; test 1.0025 * 1 = 1.0025 -> Round2 = 1.00
                    ["unit_sale_price"] = 2.00m
                }
            ]);
        purchRes["invoice_amount"].Should().Be(2.00m);
        purchRes["total_amount"].Should().Be(2.00m);

        var purchMidAvg = calc.CalculateAndValidatePurchase(
            new Dictionary<string, object?>(),
            [
                new Dictionary<string, object?>
                {
                    ["product_id"] = prodId1,
                    ["pack_size"] = 1,
                    ["unit_quantity"] = 200,
                    ["unit_purchase_price"] = 0.005m + 0.005m // wait: test exact 1.01 / 200 = 0.00505 -> 0.0051
                }
            ]);
        // Wait: 200 * 0.01 = 2.00; let's test 8 units @ 0.2505? 8 * 0.25 = 2.00, 1 unit paid + 1 bonus = 2 totalUnits, 0.01 / 2000?
        // Better: 1 unit paid @ 1.01 + 199 bonus = 200 totalUnits => netTotal = 1.01, avg_price = 1.01 / 200 = 0.00505 -> Round4 AwayFromZero = 0.0051!
        var purchAvgExact = calc.CalculateAndValidatePurchase(
            new Dictionary<string, object?>(),
            [
                new Dictionary<string, object?>
                {
                    ["product_id"] = prodId1,
                    ["pack_size"] = 1,
                    ["unit_quantity"] = 1,
                    ["unit_bonus"] = 199,
                    ["unit_purchase_price"] = 1.01m
                }
            ]);
        var purchItems = (List<Dictionary<string, object?>>)purchAvgExact["items"]!;
        purchItems[0]["avg_price"].Should().Be(0.0051m);

        var saleRetRes = await calc.CalculateAndValidateSaleReturnAsync(
            new Dictionary<string, object?>(),
            [new Dictionary<string, object?> { ["product_id"] = prodId1, ["unit_return_quantity"] = 1, ["unit_sale_price"] = 10.50m, ["item_discount_percentage"] = 5m }]);
        saleRetRes["total"].Should().Be(9.97m);

        var purchRetRes = calc.CalculateAndValidatePurchaseReturn(
            new Dictionary<string, object?>(),
            [new Dictionary<string, object?> { ["product_id"] = prodId1, ["pack_size"] = 1, ["return_unit_quantity"] = 3, ["unit_purchase_price"] = 19.99m }]);
        purchRetRes["total"].Should().Be(59.97m);
    }

    [Fact]
    public async Task ISS13_ResolveAuthorizingUserIdAsync_CaseInsensitiveEmail_CandidateFiltering_And_Lockout()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        InvoiceCalculationService.ResetRateLimitState();

        var cashierId = await env.SeedUserAsync("Cashier", "cashier@auth.local", "Cashier", "CashierPass!");
        var inactiveAdminId = await env.SeedUserAsync("OldAdmin", "oldadmin@auth.local", "Admin", "InactiveAdminPass!", status: "inactive");
        var supId = await env.SeedUserAsync("Supervisor", "Supervisor@Auth.Local", "Admin", "SupPass123!");

        var now = new DateTimeOffset(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);
        InvoiceCalculationService MakeCalc() => new(env.Db, () => now);

        // 1. Case-insensitive supervisor_email match
        var byEmail = await MakeCalc().ResolveAuthorizingUserIdAsync(
            cashierId,
            new Dictionary<string, object?>
            {
                ["supervisor_email"] = "SUPERVISOR@auth.local",
                ["supervisor_password"] = "SupPass123!"
            },
            "sale-invoice.discount-override");
        byEmail.Should().Be(supId);

        // 2. Inactive admin is NEVER authorized even with correct password (fails closed -> null)
        var inactiveRes = await MakeCalc().ResolveAuthorizingUserIdAsync(
            cashierId,
            new Dictionary<string, object?> { ["supervisor_password"] = "InactiveAdminPass!" },
            "sale-invoice.discount-override");
        inactiveRes.Should().BeNull();

        // 3. Password-only override still resolves active eligible supervisor (React modal + PreProductionRemediationTests parity)
        InvoiceCalculationService.ResetRateLimitState();
        var byPassOnly = await MakeCalc().ResolveAuthorizingUserIdAsync(
            cashierId,
            new Dictionary<string, object?> { ["supervisor_password"] = "SupPass123!" },
            "sale-invoice.discount-override");
        byPassOnly.Should().Be(supId);

        // 4. Empty supervisor_password (with no email) returns currentUserId and does NOT consume attempt budget
        for (int i = 0; i < 6; i++)
        {
            var emptyRes = await MakeCalc().ResolveAuthorizingUserIdAsync(
                cashierId,
                new Dictionary<string, object?> { ["supervisor_password"] = "" },
                "sale-invoice.discount-override");
            emptyRes.Should().Be(cashierId);
        }

        // 5. First 4 bad attempts (including >256 char oversized password) fail closed (null) without 429
        (await MakeCalc().ResolveAuthorizingUserIdAsync(
            cashierId,
            new Dictionary<string, object?> { ["supervisor_password"] = new string('A', 300) },
            "sale-invoice.discount-override")).Should().BeNull();

        for (int i = 0; i < 3; i++)
        {
            (await MakeCalc().ResolveAuthorizingUserIdAsync(
                cashierId,
                new Dictionary<string, object?> { ["supervisor_password"] = "WrongPass!" },
                "sale-invoice.discount-override")).Should().BeNull();
        }

        // 6. 5th failed attempt triggers 429 lockout
        var fifthEx = (await FluentActions.Invoking(() => MakeCalc().ResolveAuthorizingUserIdAsync(
                cashierId,
                new Dictionary<string, object?> { ["supervisor_password"] = "WrongPass!" },
                "sale-invoice.discount-override"))
            .Should().ThrowAsync<InvoiceValidationException>()).Which;
        fifthEx.StatusCode.Should().Be(429);
        fifthEx.ResponsePayload["retry_after_seconds"].Should().Be(60);

        // 7. Even a valid password is blocked with 429 while locked out
        now = now.AddSeconds(30);
        var lockedEx = (await FluentActions.Invoking(() => MakeCalc().ResolveAuthorizingUserIdAsync(
                cashierId,
                new Dictionary<string, object?> { ["supervisor_password"] = "SupPass123!" },
                "sale-invoice.discount-override"))
            .Should().ThrowAsync<InvoiceValidationException>()).Which;
        lockedEx.StatusCode.Should().Be(429);
        Convert.ToInt32(lockedEx.ResponsePayload["retry_after_seconds"]).Should().Be(30);

        // 8. After 60s lockout expires (+31s), valid password succeeds again
        now = now.AddSeconds(31);
        var recovered = await MakeCalc().ResolveAuthorizingUserIdAsync(
            cashierId,
            new Dictionary<string, object?> { ["supervisor_password"] = "SupPass123!" },
            "sale-invoice.discount-override");
        recovered.Should().Be(supId);
    }

    [Fact]
    public async Task ISS13_PurchaseInvoiceController_SupplierScopedUniquenessParity()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@pinv.local", "Admin");
        var (_, _, supp1Id, _, prodId) = await SeedCatalogAsync(env, initialQty: 10);
        var supp2Id = await env.Db.InsertAsync("suppliers", new Dictionary<string, object?> { ["name"] = "SuppM3-Two" });

        await env.Db.InsertAsync("accounts", new Dictionary<string, object?>
        {
            ["name"] = "Cash in Hand",
            ["type"] = "cash",
            ["code"] = "cash_drawer",
            ["opening_balance"] = 50000m,
            ["current_balance"] = 50000m,
            ["is_active"] = 1,
            ["is_default"] = 1
        });

        PurchaseInvoiceController MakeCtrl(DefaultHttpContext ctx) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };

        // 1. Store INV-100 for Supplier 1 succeeds (201)
        var store1 = await MakeCtrl(MakeJsonHttpContext(adminId)).ExecuteStorePurchaseInvoiceAsync(
            new Dictionary<string, object?>
            {
                ["supplier_id"] = supp1Id,
                ["posted_date"] = "2026-11-23",
                ["invoice_number"] = "INV-100",
                ["invoice_type"] = "debit",
                ["payment_method"] = "cash"
            },
            [new Dictionary<string, object?> { ["product_id"] = prodId, ["pack_size"] = 1, ["unit_quantity"] = 2, ["unit_purchase_price"] = 50m }]);
        GetStatus(store1).Should().Be(201);

        // 2. CheckUnique & Store INV-100 for Supplier 2 ALSO succeed (supplier-scoped parity!)
        using (var cuDoc = ParseBody(await MakeCtrl(MakeJsonHttpContext(adminId)).CheckUnique(supp2Id, "INV-100")))
        {
            cuDoc.RootElement.GetProperty("unique").GetBoolean().Should().BeTrue();
        }
        var store2 = await MakeCtrl(MakeJsonHttpContext(adminId)).ExecuteStorePurchaseInvoiceAsync(
            new Dictionary<string, object?>
            {
                ["supplier_id"] = supp2Id,
                ["posted_date"] = "2026-11-23",
                ["invoice_number"] = "INV-100",
                ["invoice_type"] = "debit",
                ["payment_method"] = "cash"
            },
            [new Dictionary<string, object?> { ["product_id"] = prodId, ["pack_size"] = 1, ["unit_quantity"] = 3, ["unit_purchase_price"] = 50m }]);
        GetStatus(store2).Should().Be(201);

        long inv2Id;
        using (var doc2 = ParseBody(store2))
        {
            inv2Id = doc2.RootElement.GetProperty("id").GetInt64();
        }

        // 3. Duplicate (supp1Id, INV-100) Store returns 422
        var dupStore = await MakeCtrl(MakeJsonHttpContext(adminId)).ExecuteStorePurchaseInvoiceAsync(
            new Dictionary<string, object?>
            {
                ["supplier_id"] = supp1Id,
                ["posted_date"] = "2026-11-23",
                ["invoice_number"] = "INV-100",
                ["invoice_type"] = "debit",
                ["payment_method"] = "cash"
            },
            [new Dictionary<string, object?> { ["product_id"] = prodId, ["pack_size"] = 1, ["unit_quantity"] = 1, ["unit_purchase_price"] = 50m }]);
        GetStatus(dupStore).Should().Be(422);

        // 4. Update invoice 2 keeping Supplier 2 + INV-100 succeeds (200)
        var okUpdate = await MakeCtrl(MakeJsonHttpContext(adminId, new
        {
            supplier_id = supp2Id,
            posted_date = "2026-11-23",
            invoice_number = "INV-100",
            invoice_type = "debit",
            payment_method = "cash",
            items = new[] { new { product_id = prodId, pack_size = 1, unit_quantity = 3, unit_purchase_price = 50m } }
        })).Update(inv2Id);
        GetStatus(okUpdate).Should().Be(200);

        // 5. Update invoice 2 switching to Supplier 1 + INV-100 fails with 422
        var conflictUpdate = await MakeCtrl(MakeJsonHttpContext(adminId, new
        {
            supplier_id = supp1Id,
            posted_date = "2026-11-23",
            invoice_number = "INV-100",
            invoice_type = "debit",
            payment_method = "cash",
            items = new[] { new { product_id = prodId, pack_size = 1, unit_quantity = 3, unit_purchase_price = 50m } }
        })).Update(inv2Id);
        GetStatus(conflictUpdate).Should().Be(422);

        // 6. Update invoice 2 with blank/whitespace invoice_number fails with 422
        var blankUpdate = await MakeCtrl(MakeJsonHttpContext(adminId, new
        {
            supplier_id = supp2Id,
            posted_date = "2026-11-23",
            invoice_number = "   ",
            invoice_type = "debit",
            payment_method = "cash",
            items = new[] { new { product_id = prodId, pack_size = 1, unit_quantity = 3, unit_purchase_price = 50m } }
        })).Update(inv2Id);
        GetStatus(blankUpdate).Should().Be(422);
    }

    // =========================================================================
    // Challenger Iteration 1 Defect Regression Tests (CHAL-01 through CHAL-07)
    // =========================================================================

    private sealed class DeletingOnReadFormFile : IFormFile
    {
        private readonly byte[] _bytes;
        private readonly Action _onOpenRead;

        public DeletingOnReadFormFile(byte[] bytes, string name, string fileName, string contentType, Action onOpenRead)
        {
            _bytes = bytes;
            Name = name;
            FileName = fileName;
            ContentType = contentType;
            _onOpenRead = onOpenRead;
        }

        public string ContentType { get; set; }
        public string ContentDisposition => $"form-data; name=\"{Name}\"; filename=\"{FileName}\"";
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public long Length => _bytes.Length;
        public string Name { get; }
        public string FileName { get; }

        public Stream OpenReadStream()
        {
            _onOpenRead();
            return new MemoryStream(_bytes);
        }

        public void CopyTo(Stream target) => OpenReadStream().CopyTo(target);
        public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
            => OpenReadStream().CopyToAsync(target, cancellationToken);
    }

    [Fact]
    public async Task CHAL01_CHAL03_CHAL04_TerminalController_QuickImport_CorruptFiles_NestedMacOsx_And_Concurrency()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@chalterm.local", "Admin");

        var exportSvc = new SqliteExportService(env.Db);
        TerminalController MakeCtrl(DefaultHttpContext ctx) => new(
            env.Db,
            new LicenseService(env.Db),
            exportSvc,
            new BackupService(env.Db, exportSvc),
            new DocumentSequenceService(env.Db))
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };

        // CHAL-01a: Corrupted .zip returns 422 instead of 500 InvalidDataException
        var badZipCtx = MakeMultipartHttpContext(
            adminId,
            new() { ["mode"] = "merge" },
            "file",
            "corrupt.zip",
            "application/zip",
            Encoding.ASCII.GetBytes("NOT_A_ZIP_FILE_AT_ALL"));
        GetStatus(await MakeCtrl(badZipCtx).QuickImportDatabase()).Should().Be(422);

        // CHAL-01b: Spoofed SQLite header + garbage bytes returns 422 BEFORE creating pre-import backup
        var corruptSqlite = Encoding.ASCII.GetBytes("SQLite format 3\0").Concat(Enumerable.Repeat((byte)0xAB, 4096)).ToArray();
        var badSqliteCtx = MakeMultipartHttpContext(
            adminId,
            new() { ["mode"] = "merge" },
            "file",
            "corrupt.sqlite",
            "application/octet-stream",
            corruptSqlite);
        GetStatus(await MakeCtrl(badSqliteCtx).QuickImportDatabase()).Should().Be(422);

        var backupsDir = env.Db.StoragePath("app/backups");
        if (Directory.Exists(backupsDir))
        {
            Directory.GetFiles(backupsDir, "pre-quick-import-*.sqlite").Should().BeEmpty();
        }

        // CHAL-04: Nested backup/__MACOSX/._inner.sqlite larger than backup/inner.sqlite is skipped cleanly
        var srcDbPath = Path.Combine(env.TempRoot, "chal04_src.sqlite");
        await using (var srcConn = new SqliteConnection($"Data Source={srcDbPath};Pooling=False;"))
        {
            await srcConn.OpenAsync();
            await srcConn.ExecuteAsync(@"
                CREATE TABLE brands (id INTEGER PRIMARY KEY, name TEXT NOT NULL, created_at TEXT, updated_at TEXT);
                INSERT INTO brands (id, name, created_at, updated_at) VALUES (9901, 'NestedMacZipBrand', datetime('now'), datetime('now'));");
        }
        SqliteConnection.ClearAllPools();
        var srcDbBytes = await File.ReadAllBytesAsync(srcDbPath);

        byte[] nestedMacZipBytes;
        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var macNested = zip.CreateEntry("backup/__MACOSX/._inner.sqlite");
                using (var s = macNested.Open())
                {
                    var junk = new byte[srcDbBytes.Length + 512];
                    Encoding.ASCII.GetBytes("AppleDoubleNestedJunk").CopyTo(junk, 0);
                    s.Write(junk);
                }

                var rootAppleDouble = zip.CreateEntry("backup/._shadow.sqlite");
                using (var s = rootAppleDouble.Open())
                {
                    s.Write(new byte[srcDbBytes.Length + 512]);
                }

                var realEntry = zip.CreateEntry("backup/inner.sqlite");
                using (var s = realEntry.Open())
                {
                    s.Write(srcDbBytes);
                }
            }
            nestedMacZipBytes = ms.ToArray();
        }

        var nestedRes = await MakeCtrl(MakeMultipartHttpContext(
            adminId,
            new() { ["mode"] = "merge", ["conflict_policy"] = "upsert" },
            "file",
            "nested.zip",
            "application/zip",
            nestedMacZipBytes)).QuickImportDatabase();
        GetStatus(nestedRes).Should().Be(200);
        (await env.Db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM brands WHERE name = 'NestedMacZipBrand'")).Should().Be(1);

        // CHAL-03: 4 concurrent QuickImportDatabase calls all return 200 and produce unique safety backups
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var concTasks = Enumerable.Range(0, 4).Select(async _ =>
        {
            await barrier.Task;
            var ctx = MakeMultipartHttpContext(
                adminId,
                new() { ["mode"] = "merge", ["conflict_policy"] = "upsert" },
                "file",
                "conc.zip",
                "application/zip",
                nestedMacZipBytes);
            return GetStatus(await MakeCtrl(ctx).QuickImportDatabase());
        }).ToArray();

        barrier.SetResult();
        var concStatuses = await Task.WhenAll(concTasks);
        concStatuses.Should().OnlyContain(s => s == 200);

        var allBackups = Directory.GetFiles(backupsDir, "pre-quick-import-*.sqlite");
        allBackups.Should().HaveCount(5); // 1 from CHAL-04 + 4 concurrent CHAL-03
        allBackups.Select(Path.GetFileName).Distinct().Should().HaveCount(5);
    }

    [Fact]
    public async Task CHAL02_CHAL05_SupervisorOverride_FailClosed_SingleAttemptPerRequest_And_TwelfthAdmin()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        InvoiceCalculationService.ResetRateLimitState();

        var admin1Id = await env.SeedUserAsync("Admin1", "admin1@chal02.local", "Admin", "Admin1Pass!");
        var cashierId = await env.SeedUserAsync("Cashier", "cashier@chal02.local", "Cashier", "CashierPass!");
        var (_, _, _, _, prodId) = await SeedCatalogAsync(env, initialQty: 50, unitSale: 100m, unitPurchase: 50m, maxDiscount: 10m);

        var custId = await env.Db.InsertAsync("customers", new Dictionary<string, object?>
        {
            ["name"] = "Credit Limit Customer",
            ["credit_status"] = "active",
            ["credit_limit"] = 50m
        });

        await env.Db.InsertAsync("accounts", new Dictionary<string, object?>
        {
            ["name"] = "Cash in Hand",
            ["type"] = "cash",
            ["code"] = "cash_drawer",
            ["opening_balance"] = 0m,
            ["current_balance"] = 0m,
            ["is_active"] = 1,
            ["is_default"] = 1
        });

        var calc = new InvoiceCalculationService(env.Db);

        // CHAL-02a: Admin passing explicit supervisor_email + WRONG password fails closed (null, never admin1Id)
        (await calc.ResolveAuthorizingUserIdAsync(
            admin1Id,
            new Dictionary<string, object?>
            {
                ["supervisor_email"] = "admin1@chal02.local",
                ["supervisor_password"] = "WRONG_PASSWORD!"
            },
            "sale-invoice.discount-override")).Should().BeNull();

        // Admin passing supervisor_email with empty password ALSO fails closed (null)
        (await calc.ResolveAuthorizingUserIdAsync(
            admin1Id,
            new Dictionary<string, object?>
            {
                ["supervisor_email"] = "admin1@chal02.local",
                ["supervisor_password"] = ""
            },
            "sale-invoice.discount-override")).Should().BeNull();

        // CalculateAndValidateSaleAsync for Admin with wrong supervisor_password throws 403
        var failClosedEx = (await FluentActions.Invoking(() => calc.CalculateAndValidateSaleAsync(
                new Dictionary<string, object?>
                {
                    ["sale_type"] = "retail",
                    ["supervisor_email"] = "admin1@chal02.local",
                    ["supervisor_password"] = "WRONG_PASSWORD!"
                },
                [new Dictionary<string, object?> { ["product_id"] = prodId, ["quantity"] = 1, ["price"] = 100m, ["item_discount_percentage"] = 25m }],
                userId: admin1Id,
                discountOverride: true))
            .Should().ThrowAsync<InvoiceValidationException>()).Which;
        failClosedEx.StatusCode.Should().Be(403);

        // CHAL-02b: Single HTTP request with credit_override=true + 5% disc (<= 10% max) + wrong password burns ONLY 1 attempt
        InvoiceCalculationService.ResetRateLimitState();
        SaleInvoiceController MakeSaleCtrl(long uid) => new(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = MakeJsonHttpContext(uid) }
        };

        for (int reqNum = 1; reqNum <= 4; reqNum++)
        {
            var res = await MakeSaleCtrl(cashierId).ExecuteStoreSaleInvoiceAsync(
                new Dictionary<string, object?>
                {
                    ["customer_id"] = custId,
                    ["date"] = "2026-11-23",
                    ["invoice_type"] = "credit",
                    ["payment_method"] = "cash",
                    ["sale_type"] = "retail",
                    ["credit_override"] = true,
                    ["discount_override"] = true,
                    ["supervisor_password"] = "WRONG_PASS!"
                },
                [new Dictionary<string, object?> { ["product_id"] = prodId, ["quantity"] = 1, ["price"] = 95m, ["item_discount_percentage"] = 5m }],
                []);
            GetStatus(res).Should().Be(403, $"HTTP request #{reqNum} should return 403 before 5th attempt lockout");
        }

        // 5th HTTP request hits 429 lockout (proving requests 1..4 each burned 1 attempt, not 2!)
        var req5Res = await MakeSaleCtrl(cashierId).ExecuteStoreSaleInvoiceAsync(
            new Dictionary<string, object?>
            {
                ["customer_id"] = custId,
                ["date"] = "2026-11-23",
                ["invoice_type"] = "credit",
                ["payment_method"] = "cash",
                ["sale_type"] = "retail",
                ["credit_override"] = true,
                ["discount_override"] = true,
                ["supervisor_password"] = "WRONG_PASS!"
            },
            [new Dictionary<string, object?> { ["product_id"] = prodId, ["quantity"] = 1, ["price"] = 95m, ["item_discount_percentage"] = 5m }],
            []);
        GetStatus(req5Res).Should().Be(429);

        // CHAL-05: 12th Admin succeeds in password-only mode (candidate limit raised to 50)
        InvoiceCalculationService.ResetRateLimitState();
        long admin12Id = 0;
        for (int i = 2; i <= 12; i++)
        {
            admin12Id = await env.SeedUserAsync($"Admin{i}", $"admin{i}@chal02.local", "Admin", $"Admin{i}Pass!");
        }

        var resolved12 = await calc.ResolveAuthorizingUserIdAsync(
            cashierId,
            new Dictionary<string, object?> { ["supervisor_password"] = "Admin12Pass!" },
            "sale-invoice.credit-override");
        resolved12.Should().Be(admin12Id);
    }

    [Fact]
    public async Task CHAL06_InvoiceCalculationService_Overflow_NaN_Int32Bounds_And_ReturnFloatStrings()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@chal06.local", "Admin");
        var (_, _, _, _, prodId) = await SeedCatalogAsync(env, initialQty: 50, unitSale: 100m, unitPurchase: 50m, maxDiscount: 100m);

        var calc = new InvoiceCalculationService(env.Db);

        // CHAL-06a: 1e30, NaN, PositiveInfinity -> 422 (never silently 0)
        foreach (object badPrice in new object[] { 1e30d, double.NaN, double.PositiveInfinity, "1e30", "NaN", "Infinity" })
        {
            var ex = (await FluentActions.Invoking(() => calc.CalculateAndValidateSaleAsync(
                    new Dictionary<string, object?> { ["sale_type"] = "retail" },
                    [new Dictionary<string, object?> { ["product_id"] = prodId, ["quantity"] = 1, ["price"] = badPrice }],
                    userId: adminId))
                .Should().ThrowAsync<InvoiceValidationException>()).Which;
            ex.StatusCode.Should().Be(422, $"bad price '{badPrice}' must fail with 422");

            var pEx = FluentActions.Invoking(() => calc.CalculateAndValidatePurchase(
                    new Dictionary<string, object?>(),
                    [new Dictionary<string, object?> { ["product_id"] = prodId, ["pack_size"] = 1, ["unit_quantity"] = 1, ["unit_purchase_price"] = badPrice }]))
                .Should().Throw<InvoiceValidationException>().Which;
            pEx.StatusCode.Should().Be(422, $"bad purchase price '{badPrice}' must fail with 422");
        }

        var badDiscEx = (await FluentActions.Invoking(() => calc.CalculateAndValidateSaleAsync(
                new Dictionary<string, object?> { ["sale_type"] = "retail", ["discount_amount"] = 1e30d },
                [new Dictionary<string, object?> { ["product_id"] = prodId, ["quantity"] = 1, ["price"] = 100m }],
                userId: adminId))
            .Should().ThrowAsync<InvoiceValidationException>()).Which;
        badDiscEx.StatusCode.Should().Be(422);

        // CHAL-06b: SaleReturn "2.0" -> 2 (exact), "2.5" -> 422; PurchaseReturn "3.0" -> 3, "3.5" -> 422
        var saleRetFloatStr = await calc.CalculateAndValidateSaleReturnAsync(
            new Dictionary<string, object?>(),
            [new Dictionary<string, object?> { ["product_id"] = prodId, ["unit_return_quantity"] = "2.0", ["unit_sale_price"] = 15.25m }]);
        saleRetFloatStr["total"].Should().Be(30.50m);

        var saleRetFracEx = (await FluentActions.Invoking(() => calc.CalculateAndValidateSaleReturnAsync(
                new Dictionary<string, object?>(),
                [new Dictionary<string, object?> { ["product_id"] = prodId, ["unit_return_quantity"] = "2.5", ["unit_sale_price"] = 10m }]))
            .Should().ThrowAsync<InvoiceValidationException>()).Which;
        saleRetFracEx.StatusCode.Should().Be(422);

        var purchRetFloatStr = calc.CalculateAndValidatePurchaseReturn(
            new Dictionary<string, object?>(),
            [new Dictionary<string, object?> { ["product_id"] = prodId, ["pack_size"] = 1, ["return_unit_quantity"] = "3.0", ["unit_purchase_price"] = 12.50m }]);
        purchRetFloatStr["total"].Should().Be(37.50m);

        var purchRetFracEx = FluentActions.Invoking(() => calc.CalculateAndValidatePurchaseReturn(
                new Dictionary<string, object?>(),
                [new Dictionary<string, object?> { ["product_id"] = prodId, ["pack_size"] = 1, ["return_unit_quantity"] = "3.5", ["unit_purchase_price"] = 10m }]))
            .Should().Throw<InvoiceValidationException>().Which;
        purchRetFracEx.StatusCode.Should().Be(422);

        // CHAL-06c: 3000000000L quantity -> 422 across all 4 pipelines (never OverflowException 500 or int wraparound)
        var saleQtyOverflow = (await FluentActions.Invoking(() => calc.CalculateAndValidateSaleAsync(
                new Dictionary<string, object?> { ["sale_type"] = "retail" },
                [new Dictionary<string, object?> { ["product_id"] = prodId, ["quantity"] = 3000000000L, ["price"] = 1m }],
                userId: adminId))
            .Should().ThrowAsync<InvoiceValidationException>()).Which;
        saleQtyOverflow.StatusCode.Should().Be(422);

        var purchQtyOverflow = FluentActions.Invoking(() => calc.CalculateAndValidatePurchase(
                new Dictionary<string, object?>(),
                [new Dictionary<string, object?> { ["product_id"] = prodId, ["pack_size"] = 1, ["unit_quantity"] = 3000000000L, ["unit_purchase_price"] = 1m }]))
            .Should().Throw<InvoiceValidationException>().Which;
        purchQtyOverflow.StatusCode.Should().Be(422);

        var saleRetQtyOverflow = (await FluentActions.Invoking(() => calc.CalculateAndValidateSaleReturnAsync(
                new Dictionary<string, object?>(),
                [new Dictionary<string, object?> { ["product_id"] = prodId, ["unit_return_quantity"] = 3000000000L, ["unit_sale_price"] = 1m }]))
            .Should().ThrowAsync<InvoiceValidationException>()).Which;
        saleRetQtyOverflow.StatusCode.Should().Be(422);

        var purchRetQtyOverflow = FluentActions.Invoking(() => calc.CalculateAndValidatePurchaseReturn(
                new Dictionary<string, object?>(),
                [new Dictionary<string, object?> { ["product_id"] = prodId, ["pack_size"] = 1, ["return_unit_quantity"] = 3000000000L, ["unit_purchase_price"] = 1m }]))
            .Should().Throw<InvoiceValidationException>().Which;
        purchRetQtyOverflow.StatusCode.Should().Be(422);
    }

    [Fact]
    public async Task CHAL07_SaleInvoiceController_UploadPrescription_MidUploadDeleteRace_Returns404AndCleansOrphan()
    {
        await using var env = await IsolatedEnv.CreateAsync();
        var adminId = await env.SeedUserAsync("Admin", "admin@chal07.local", "Admin");
        var (_, _, _, custId, _) = await SeedCatalogAsync(env, initialQty: 10);

        var prescDir = env.Db.StoragePath("app/public/prescriptions");
        Directory.CreateDirectory(prescDir);
        var oldFullPath = Path.Combine(prescDir, "existing_old.png");
        await File.WriteAllBytesAsync(oldFullPath, ValidPngBytes);

        var saleId = await env.Db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = adminId,
            ["customer_id"] = custId,
            ["invoice_type"] = "debit",
            ["payment_method"] = "cash",
            ["sale_type"] = "retail",
            ["posted_number"] = "SI-000777",
            ["date"] = "2026-11-23",
            ["gross_amount"] = 100m,
            ["total"] = 100m,
            ["total_receive"] = 100m,
            ["prescription_image_path"] = "prescriptions/existing_old.png"
        });

        var racingFile = new DeletingOnReadFormFile(
            ValidPngBytes,
            "prescription",
            "race.png",
            "image/png",
            () => env.Db.ExecuteAsync("DELETE FROM sale_invoices WHERE id = @id", new { id = saleId }).GetAwaiter().GetResult());

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, adminId.ToString())], "Sanctum"))
        };
        ctx.Items["KarobarUserId"] = adminId;
        ctx.Request.ContentType = "multipart/form-data; boundary=----KarobarM3RaceBoundary";
        ctx.Request.Form = new Microsoft.AspNetCore.Http.FormCollection(
            new Dictionary<string, StringValues>(),
            new FormFileCollection { racingFile });

        var ctrl = new SaleInvoiceController(env.Db)
        {
            ControllerContext = new ControllerContext { HttpContext = ctx }
        };

        var res = await ctrl.UploadPrescription(saleId);
        GetStatus(res).Should().Be(404);

        // Old file must remain intact and newly uploaded orphan file must be cleaned up!
        File.Exists(oldFullPath).Should().BeTrue();
        Directory.GetFiles(prescDir).Should().ContainSingle(p => Path.GetFileName(p) == "existing_old.png");
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using FluentAssertions;
using Karobar.Api;
using Karobar.Api.Controllers;
using Karobar.Background;
using Karobar.Core.Entities;
using Karobar.Data;
using Karobar.Host;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Karobar.Tests;

public class WaveP3RemediationTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "native-win")) &&
                File.Exists(Path.Combine(dir, "package.json")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return Directory.GetCurrentDirectory();
    }

    private static async Task<(WebApplication App, HttpClient Client, CookieContainer Cookies, KarobarDb Db, string TempRoot, string? PrevRoot)> StartServerWithCookiesAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_p3_http_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "public"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "backups"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "shared-pdfs"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "logs"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "public"));

        var tempDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
        var sourceFactory = new DbConnectionFactory();
        if (File.Exists(sourceFactory.SqlitePath) && new FileInfo(sourceFactory.SqlitePath).Length > 0)
        {
            File.Copy(sourceFactory.SqlitePath, tempDbPath, overwrite: true);
        }

        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
        Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", tempRoot);
        KarobarDb.FlushSchemaCache();

        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: tempDbPath)).EnsureSchemaReadyAsync();
        KarobarDb.FlushSchemaCache();

        var kDb = new KarobarDb(explicitSqlitePath: tempDbPath);
        if (await kDb.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = 'Admin'") == null)
        {
            await kDb.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "Admin", ["guard_name"] = "sanctum" });
        }

        var port = KestrelHostServer.FindFreePort(21400 + Random.Shared.Next(0, 400));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddKarobarApi(builder.Configuration, explicitSqlitePath: tempDbPath);

        var app = builder.Build();
        app.UseRouting();
        app.UseKarobarApiPipeline();
        app.MapControllers();

        await app.StartAsync();

        var cookies = new CookieContainer();
        var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            UseCookies = true,
            AllowAutoRedirect = false
        };
        var client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        return (app, client, cookies, kDb, tempRoot, prevRoot);
    }

    private static async Task CleanupServerAsync(WebApplication app, HttpClient client, string tempRoot, string? prevRoot)
    {
        client.Dispose();
        try { await app.StopAsync(); } catch { }
        try { await app.DisposeAsync(); } catch { }
        Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
        Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", prevRoot);
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task R3_1_LegalPages_RenderFullText_And_ServerLaunch_Enforces_Csrf_OverHttp()
    {
        var (app, client, cookies, db, tempRoot, prevRoot) = await StartServerWithCookiesAsync();
        try
        {
            // 1. Legal pages render full legal content & document layout
            var termsRes = await client.GetAsync("/terms-of-service");
            termsRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var termsHtml = await termsRes.Content.ReadAsStringAsync();
            termsHtml.Should().Contain("1. SINGLE-MACHINE HARDWARE-BOUND LICENSE GRANT");
            termsHtml.Should().Contain("5. WARRANTY DISCLAIMER");
            termsHtml.Should().Contain("class=\"nav-links\"");

            var privacyRes = await client.GetAsync("/privacy-policy");
            privacyRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var privacyHtml = await privacyRes.Content.ReadAsStringAsync();
            privacyHtml.Should().Contain("1. 100% LOCAL-FIRST DATA OWNERSHIP");
            privacyHtml.Should().Contain("Built-in Local WhatsApp Agent");
            privacyHtml.Should().Contain("4. ADMINISTRATOR CREDENTIALS");

            // 2. Seed server-launch.key and an active Admin user
            await File.WriteAllTextAsync(Path.Combine(tempRoot, "storage", "app", "server-launch.key"), "test-launch-secret-key-99");
            var adminRole = await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE LOWER(name) IN ('admin', 'super-admin', 'super admin') LIMIT 1");
            long adminRoleId = Convert.ToInt64(adminRole!["id"]);
            var existingAdmin = await db.QuerySingleOrDefaultAsync(@"
                SELECT u.id FROM users u
                INNER JOIN model_has_roles mhr ON mhr.model_id = u.id
                WHERE mhr.role_id = @rid AND u.status = 'active' LIMIT 1",
                new Dictionary<string, object?> { ["@rid"] = adminRoleId });
            if (existingAdmin == null)
            {
                var uid = await db.InsertAsync("users", new Dictionary<string, object?>
                {
                    ["name"] = "R31 Launch Admin",
                    ["email"] = $"r31admin_{Guid.NewGuid():N}@karobar.test",
                    ["password"] = "$2y$10$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi",
                    ["status"] = "active",
                    ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                    ["updated_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
                });
                await db.InsertAsync("model_has_roles", new Dictionary<string, object?>
                {
                    ["role_id"] = adminRoleId,
                    ["model_type"] = "App\\Models\\User",
                    ["model_id"] = uid
                });
            }

            // 3. POST /server-launch without CSRF token -> 400 Bad Request (no token minted)
            var noCsrfRes = await client.PostAsync(
                "/server-launch",
                new FormUrlEncodedContent(new Dictionary<string, string> { ["key"] = "test-launch-secret-key-99" }));
            noCsrfRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await noCsrfRes.Content.ReadAsStringAsync()).Should().NotContain("localStorage.setItem('token'");

            // 4. GET /server-launch emits non-Secure HTTP cookie + hidden CSRF form field
            var formRes = await client.GetAsync("/server-launch");
            formRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var formHtml = await formRes.Content.ReadAsStringAsync();
            var match = Regex.Match(formHtml, @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""");
            match.Success.Should().BeTrue("GET /server-launch must include __RequestVerificationToken hidden input");
            var csrfToken = WebUtility.HtmlDecode(match.Groups[1].Value);

            var issuedCookies = cookies.GetCookies(client.BaseAddress!).Cast<Cookie>().ToList();
            var afCookie = issuedCookies.FirstOrDefault(c => c.Name == "Karobar.Antiforgery");
            afCookie.Should().NotBeNull();
            afCookie!.Secure.Should().BeFalse("HTTP loopback cookie must not require HTTPS");

            // 5. POST /server-launch with invalid token -> 400 Bad Request
            var badCsrfRes = await client.PostAsync(
                "/server-launch",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = "tampered-token-value",
                    ["key"] = "test-launch-secret-key-99"
                }));
            badCsrfRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // 6. POST /server-launch with valid CSRF cookie + form token + launch key -> 200 OK with Admin token
            var validRes = await client.PostAsync(
                "/server-launch",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["__RequestVerificationToken"] = csrfToken,
                    ["key"] = "test-launch-secret-key-99"
                }));
            validRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var validHtml = await validRes.Content.ReadAsStringAsync();
            validHtml.Should().Contain("localStorage.setItem('token'");
            validHtml.Should().Contain("window.location.replace('/server-dashboard')");
        }
        finally
        {
            await CleanupServerAsync(app, client, tempRoot, prevRoot);
        }
    }

    [Fact]
    public async Task R3_2_PrintingCompleteness_Assets_BarcodeSvg_SafeFilename_And_PdfFallback_Work()
    {
        var repoRoot = FindRepoRoot();

        // 1. Verify all 6 native-win/assets files exist and retired legacy directories are absent
        var sumatraAsset = Path.Combine(repoRoot, "native-win", "assets", "SumatraPDF.exe");
        var termsAsset = Path.Combine(repoRoot, "native-win", "assets", "TermsAndConditions.txt");
        var privacyAsset = Path.Combine(repoRoot, "native-win", "assets", "PrivacyPolicy.txt");
        var iconIcoAsset = Path.Combine(repoRoot, "native-win", "assets", "icon.ico");
        var iconPngAsset = Path.Combine(repoRoot, "native-win", "assets", "icon.png");
        var cerAsset = Path.Combine(repoRoot, "native-win", "assets", "MalikAsimShahzad-KarobarERP.cer");

        File.Exists(sumatraAsset).Should().BeTrue();
        File.Exists(termsAsset).Should().BeTrue();
        File.Exists(privacyAsset).Should().BeTrue();
        File.Exists(iconIcoAsset).Should().BeTrue();
        File.Exists(iconPngAsset).Should().BeTrue();
        File.Exists(cerAsset).Should().BeTrue();
        Directory.Exists(Path.Combine(repoRoot, "desktop")).Should().BeFalse();
        Directory.Exists(Path.Combine(repoRoot, "installer")).Should().BeFalse();
        Directory.Exists(Path.Combine(repoRoot, "resources", "views")).Should().BeFalse();

        using (var sha = SHA256.Create())
        await using (var fs = File.OpenRead(sumatraAsset))
        {
            var hex = Convert.ToHexString(await sha.ComputeHashAsync(fs)).ToLowerInvariant();
            hex.Should().Be("0b359fe7df53dada3ab823d7735ca048232ec05f0d14120938c7b020cb051b16");
        }

        // 2. Verify PdfDocumentGenerator renders barcode design with Code128 SVG (A4, thermal, and empty DocNum)
        var pdfGen = new PdfDocumentGenerator();
        foreach (var (paper, docNum) in new[] { ("a4", "INV-2026-0091"), ("thermal", "INV-2026-0091"), ("a4", ""), ("thermal", "   ") })
        {
            var pdfBytes = pdfGen.GenerateSharedDocumentPdf(new SharedDocumentPdfRequest
            {
                DocumentType = "sale_invoice",
                DocumentNumber = docNum,
                PaperSize = paper,
                PdfDesign = "barcode",
                PartyName = "Barcode QA Customer",
                Columns =
                [
                    new PdfTableColumnDef { Header = "Item", RelativeWidth = 3f },
                    new PdfTableColumnDef { Header = "Amount", RelativeWidth = 1f, AlignRight = true }
                ],
                Rows = [["Amoxicillin 500mg", "450.00"]],
                Totals = [new PdfKeyValuePair("Total", "Rs. 450.00", isGrandTotal: true)]
            });
            pdfBytes.Length.Should().BeGreaterThan(500);
            Encoding.ASCII.GetString(pdfBytes, 0, 5).Should().Be("%PDF-");
        }

        // 3. Verify DocumentShareEngine returns sanitized safeFilename across pdf_built and api_fallback
        var tempShareRoot = Path.Combine(Path.GetTempPath(), $"karobar_p3_share_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempShareRoot);
        try
        {
            using var failingHttp = new HttpClient(new StubFailingHandler());
            var shareEngine = new DocumentShareEngine(tempShareRoot, pdfGen, failingHttp);
            var samplePdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF");

            var builtRes = await shareEngine.SendOrBuildWhatsAppPdfAsync(
                samplePdf,
                @"..\..\evil\INV-01.pdf",
                "03001234567",
                "Test caption",
                new WhatsAppConfiguration { Enabled = false });
            builtRes["status"].Should().Be("pdf_built");
            builtRes["filename"].Should().Be("INV-01.pdf");
            builtRes["message"]!.ToString().Should().Contain("INV-01.pdf").And.NotContain("..");

            var fallbackRes = await shareEngine.SendOrBuildWhatsAppPdfAsync(
                samplePdf,
                "../../traversal/INV-02.pdf",
                "03001234567",
                "Test caption",
                new WhatsAppConfiguration
                {
                    Enabled = true,
                    Provider = "local_agent",
                    ApiUrl = "http://127.0.0.1:3002",
                    AutoFallback = true
                });
            fallbackRes["status"].Should().Be("api_fallback");
            fallbackRes["filename"].Should().Be("INV-02.pdf");

            // 4. Verify KarobarDesktopBridge passes file:///...pdf (NEVER null, null) to NativeSilentPrintExecutor when SumatraPDF is missing
            var tempPdfFile = Path.Combine(tempShareRoot, "test-doc.pdf");
            await File.WriteAllBytesAsync(tempPdfFile, samplePdf);

            var netCfg = new NetworkConfigManager(tempShareRoot);
            var winMgr = new WebView2WindowManager(netCfg);
            var bridge = new KarobarDesktopBridge(netCfg, winMgr);

            string? capturedUrl = null;
            string? capturedHtml = "sentinel";
            bridge.PrinterListProvider = () => Task.FromResult<IReadOnlyList<PrinterInfoDto>>(new[]
            {
                new PrinterInfoDto { Name = "HP LaserJet Test", IsDefault = true, IsOnline = true, IsVirtual = false }
            });
            bridge.NativeSilentPrintExecutor = (url, html, printer, isThermal) =>
            {
                capturedUrl = url;
                capturedHtml = html;
                return Task.FromResult<(bool Ok, string? Engine, string? Error)>((true, "webview2-pdf-fallback", null));
            };

            using var jsonDoc = JsonDocument.Parse(JsonSerializer.Serialize(new { pdfFilePath = tempPdfFile }));
            var silentRes = await bridge.SilentPrintAsync(jsonDoc.RootElement);
            var silentJson = JsonSerializer.Serialize(silentRes);
            silentJson.Should().Contain("\"ok\":true");
            capturedUrl.Should().Be(new Uri(Path.GetFullPath(tempPdfFile)).AbsoluteUri);
            capturedHtml.Should().BeNull();
        }
        finally
        {
            try { if (Directory.Exists(tempShareRoot)) Directory.Delete(tempShareRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task R3_3_Scheduler_LogsErrors_RetriesFailedBackupSlot_DemandsNotify_And_Port3002()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_p3_sched_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "logs"));

        var tempDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
        var sourceFactory = new DbConnectionFactory();
        if (File.Exists(sourceFactory.SqlitePath) && new FileInfo(sourceFactory.SqlitePath).Length > 0)
        {
            File.Copy(sourceFactory.SqlitePath, tempDbPath, overwrite: true);
        }

        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        var prevAppRoot = Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");
        Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
        Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", tempRoot);
        KarobarDb.FlushSchemaCache();
        try
        {
            var services = new ServiceCollection();
            services.AddKarobarData(explicitSqlitePath: tempDbPath);
            using var sp = services.BuildServiceProvider();

            var bootstrapper = sp.GetRequiredService<DatabaseBootstrapper>();
            await bootstrapper.EnsureSchemaReadyAsync();
            KarobarDb.FlushSchemaCache();

            var cliLogger = new CapturingLogger<KarobarCliCommandRunner>();
            var schedLogger = new CapturingLogger<KarobarSchedulerHostedService>();
            var cliRunner = new KarobarCliCommandRunner(tempRoot, new WaseelaAndSqliteImportEngine(tempRoot), cliLogger);
            var scheduler = new KarobarSchedulerHostedService(sp, schedLogger, tempRoot, cliRunner, new FbrFiscalizationEngine());

            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KarobarDbContext>();
                var settingRow = await db.Settings.OrderBy(s => s.Id).FirstOrDefaultAsync();
                if (settingRow == null)
                {
                    db.Settings.Add(new Setting
                    {
                        StoreName = "M3 QA Store",
                        CurrencySymbol = "Rs.",
                        CurrencyCode = "PKR",
                        CurrencyDecimalPlaces = 2,
                        WhatsappProvider = "local_agent",
                        WhatsappApiUrl = "http://127.0.0.1:3002",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                var schedule = await db.BackupSchedules.OrderBy(s => s.Id).FirstOrDefaultAsync();
                if (schedule == null)
                {
                    schedule = new BackupSchedule();
                    db.BackupSchedules.Add(schedule);
                }
                schedule.IsAutomated = true;
                schedule.Frequency = "daily";
                schedule.TimeOfDay = "00:00";
                schedule.LastRunAt = null;
                schedule.LastRunStatus = "never";
                schedule.LastErrorMessage = null;

                var product = await db.Products.OrderBy(p => p.Id).FirstOrDefaultAsync();
                if (product == null)
                {
                    product = new Product
                    {
                        Name = "M3 QA Test Product",
                        ProductCode = "M3-QA-001",
                        PackSize = 10,
                        PackPurchasePrice = 100m,
                        PackSalePrice = 120m,
                        UnitPurchasePrice = 10m,
                        UnitSalePrice = 12m,
                        Quantity = 5,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                    db.Products.Add(product);
                    await db.SaveChangesAsync();
                }

                db.UserDemands.Add(new UserDemand
                {
                    ProductId = product.Id,
                    RequestedName = product.Name,
                    RequestedQuantity = 5,
                    Notes = "Urgent M3 QA restock",
                    Status = "pending",
                    LastNotifiedAt = null,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                await db.SaveChangesAsync();
            }

            // Block storage/app/backups with a regular file so Directory.CreateDirectory fails on first backup attempt
            var backupsBlocker = Path.Combine(tempRoot, "storage", "app", "backups");
            await File.WriteAllTextAsync(backupsBlocker, "blocker-file");

            var runTime = new DateTime(2026, 11, 22, 10, 15, 0, DateTimeKind.Local);
            var firstRun = await scheduler.RunDueScheduledJobsAsync(runTime);

            firstRun["backup:run-scheduled"].Should().BeFalse("failed backup must not report success");
            firstRun["demands:notify"].Should().BeTrue();
            cliLogger.Entries.Should().Contain(e => e.Level == LogLevel.Error);
            schedLogger.Entries.Should().Contain(e => e.Level == LogLevel.Warning || e.Level == LogLevel.Error);

            // Verify demands:notify created actionable log & updated timestamps
            var demandLog = Path.Combine(tempRoot, "storage", "logs", "demand-notifications.log");
            File.Exists(demandLog).Should().BeTrue();
            (await File.ReadAllTextAsync(demandLog)).Should().Contain("Urgent M3 QA restock").And.Contain("/user-demands");

            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KarobarDbContext>();
                var demand = await db.UserDemands.OrderByDescending(d => d.Id).FirstAsync();
                demand.LastNotifiedAt.Should().NotBeNull();
                demand.UpdatedAt.Should().NotBeNull();

                var schedRow = await db.BackupSchedules.OrderBy(s => s.Id).FirstAsync();
                schedRow.LastRunStatus.Should().Be("failed");
            }

            // Unblock backups folder and re-run within the SAME hour -> must retry instead of skipping!
            File.Delete(backupsBlocker);
            Directory.CreateDirectory(backupsBlocker);

            var secondRun = await scheduler.RunDueScheduledJobsAsync(runTime.AddMinutes(1));
            secondRun["backup:run-scheduled"].Should().BeTrue("scheduler must retry failed backup in the same hour");

            using (var scope = sp.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KarobarDbContext>();
                var schedRow = await db.BackupSchedules.OrderBy(s => s.Id).FirstAsync();
                schedRow.LastRunStatus.Should().Be("success");
            }

            // (a) Verify DatabaseBootstrapper one-time 3001 -> 3002 migration + non-reversion if admin explicitly sets 3001
            var factory = sp.GetRequiredService<IDbConnectionFactory>();
            await using (var preConn = await factory.OpenConnectionAsync())
            {
                await preConn.ExecuteAsync(
                    "UPDATE settings SET whatsapp_provider = 'local_agent', whatsapp_api_url = 'http://127.0.0.1:3001' WHERE id = (SELECT id FROM settings ORDER BY id ASC LIMIT 1)");
                await preConn.ExecuteAsync(
                    "DELETE FROM migrations WHERE migration = @mig",
                    new { mig = DatabaseBootstrapper.MigrateNativeWhatsAppPort3002MigrationName });
            }

            await bootstrapper.EnsureSchemaReadyAsync();

            await using (var postConn = await factory.OpenConnectionAsync())
            {
                var migratedUrl = await postConn.ExecuteScalarAsync<string>(
                    "SELECT whatsapp_api_url FROM settings ORDER BY id ASC LIMIT 1");
                migratedUrl.Should().Be("http://127.0.0.1:3002");

                var migrationCount = await postConn.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM migrations WHERE migration = @mig",
                    new { mig = DatabaseBootstrapper.MigrateNativeWhatsAppPort3002MigrationName });
                migrationCount.Should().Be(1);

                // Simulate admin explicitly setting port 3001 after the one-time migration ran
                await postConn.ExecuteAsync(
                    "UPDATE settings SET whatsapp_api_url = 'http://127.0.0.1:3001' WHERE id = (SELECT id FROM settings ORDER BY id ASC LIMIT 1)");
            }

            // Subsequent bootstrapper runs must NOT revert admin's explicit 3001 setting
            await bootstrapper.EnsureSchemaReadyAsync();
            await using (var recheckConn = await factory.OpenConnectionAsync())
            {
                var preservedUrl = await recheckConn.ExecuteScalarAsync<string>(
                    "SELECT whatsapp_api_url FROM settings ORDER BY id ASC LIMIT 1");
                preservedUrl.Should().Be("http://127.0.0.1:3001");

                // Restore migrated 3002 state for SettingController.TestWhatsApp check below
                await recheckConn.ExecuteAsync(
                    "UPDATE settings SET whatsapp_api_url = 'http://127.0.0.1:3002' WHERE id = (SELECT id FROM settings ORDER BY id ASC LIMIT 1)");
            }

            // (b) Verify ResolveAgentUrl strict uri.Host loopback check & explicit port preservation (including 3001)
            using var waManager = new LocalWhatsAppSidecarManager(tempRoot);
            waManager.ResolveAgentUrl().Should().Be("http://127.0.0.1:3002");
            waManager.ResolveAgentUrl(new Setting { WhatsappProvider = "local_agent", WhatsappApiUrl = "http://127.0.0.1:3001" })
                .Should().Be("http://127.0.0.1:3001");
            waManager.ResolveAgentUrl(new Setting { WhatsappProvider = "local_agent", WhatsappApiUrl = "http://localhost:3001" })
                .Should().Be("http://localhost:3001");
            waManager.ResolveAgentUrl(new Setting { WhatsappProvider = "local_agent", WhatsappApiUrl = "http://127.0.0.1:45678" })
                .Should().Be("http://127.0.0.1:45678");
            waManager.ResolveAgentUrl(new Setting { WhatsappProvider = "local_agent", WhatsappApiUrl = "http://[::1]:3005" })
                .Should().Be("http://[::1]:3005");
            waManager.ResolveAgentUrl(new Setting { WhatsappProvider = "local_agent", WhatsappApiUrl = "https://evil.example/?127.0.0.1" })
                .Should().Be("http://127.0.0.1:3002");
            waManager.ResolveAgentUrl(new Setting { WhatsappProvider = "local_agent", WhatsappApiUrl = "http://127.0.0.1.evil.com:3002" })
                .Should().Be("http://127.0.0.1:3002");
            waManager.ResolveAgentUrl(new Setting { WhatsappProvider = "local_agent", WhatsappApiUrl = "ftp://127.0.0.1:3002" })
                .Should().Be("http://127.0.0.1:3002");

            // (c) Verify SettingController.TestWhatsApp routes to 3002 on the migrated/upgraded DB
            var capturedRequests = new List<string>();
            using var stubHttp = new HttpClient(new StubCapturingHandler(req =>
            {
                var url = req.RequestUri?.ToString() ?? "";
                capturedRequests.Add(url);
                if (url.EndsWith("/status", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{\"ok\":true,\"status\":\"connected\"}", Encoding.UTF8, "application/json")
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"sent\":true,\"message_id\":\"wa_test_3002\"}", Encoding.UTF8, "application/json")
                };
            }));
            using var waForTest = new LocalWhatsAppSidecarManager(tempRoot, stubHttp);
            var shareEngine = new DocumentShareEngine(tempRoot, new PdfDocumentGenerator(), stubHttp);
            var kDbForTest = new KarobarDb(explicitSqlitePath: tempDbPath);
            using var efCtxForTest = DatabaseBootstrapper.CreateDbContext(tempDbPath);
            var adminUser = await kDbForTest.QuerySingleOrDefaultAsync(@"
                SELECT u.id FROM users u
                INNER JOIN model_has_roles mhr ON mhr.model_id = u.id
                INNER JOIN roles r ON r.id = mhr.role_id
                WHERE LOWER(r.name) IN ('admin', 'super-admin', 'super admin')
                ORDER BY u.id ASC LIMIT 1");
            var adminUid = adminUser != null ? Convert.ToInt64(adminUser["id"]) : 1L;
            var testHttpCtx = new DefaultHttpContext();
            testHttpCtx.Items["KarobarUserId"] = adminUid;
            testHttpCtx.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, adminUid.ToString())], "Sanctum"));
            var testWaPayload = Encoding.UTF8.GetBytes("{\"test_phone\":\"03001234567\"}");
            testHttpCtx.Request.Body = new MemoryStream(testWaPayload);
            testHttpCtx.Request.ContentType = "application/json";

            var settingCtrl = new SettingController(
                kDbForTest,
                new LicenseService(kDbForTest),
                waForTest,
                shareEngine,
                new PdfDocumentGenerator(),
                dbContext: efCtxForTest)
            {
                ControllerContext = new ControllerContext { HttpContext = testHttpCtx }
            };

            var waTestRes = (await settingCtrl.TestWhatsApp()).Should().BeOfType<OkObjectResult>().Subject;
            JsonSerializer.Serialize(waTestRes.Value).Should().Contain("\"provider\":\"local_agent\"");
            capturedRequests.Should().Contain(u => u == "http://127.0.0.1:3002/send-document");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", prevAppRoot);
            KarobarDb.FlushSchemaCache();
            try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void R3_4_CubeAiRoutes_AreValid_And_GuideBotDrawer_UnsubscribesEventsOnDispose()
    {
        var repoRoot = FindRepoRoot();
        var promptBuilder = new CubePromptBuilder();
        var (sysPrompt, _) = promptBuilder.BuildPrompts("Explain payroll", "en", "/dashboard", []);
        sysPrompt.Should().Contain("/accounting").And.Contain("/hr-payroll");
        sysPrompt.Should().NotContain("/journal-entries")
            .And.NotContain("/employees")
            .And.NotContain("/attendance")
            .And.NotContain("/payroll");

        var retriever = new CubeKnowledgeRetriever();
        var arQuery = retriever.DetectLiveRecordQuery("How much customer receivable udhar balance is pending?");
        arQuery.Should().NotBeNull();
        var arLinks = (List<Dictionary<string, string>>)arQuery!["links"]!;
        arLinks.Select(l => l["path"]).Should().Contain("/reports/outstanding-aging")
            .And.NotContain("/reports/accounts-receivable-aging");

        var apQuery = retriever.DetectLiveRecordQuery("What is my current supplier payable balance?");
        apQuery.Should().NotBeNull();
        var apLinks = (List<Dictionary<string, string>>)apQuery!["links"]!;
        apLinks.Select(l => l["path"]).Should().Contain("/reports/outstanding-aging")
            .And.NotContain("/reports/accounts-payable-aging");

        // Verify GuideBot drawer & WebView2Form store and unsubscribe event delegates cleanly
        var drawerSource = File.ReadAllText(Path.Combine(repoRoot, "native-win", "src", "Karobar.Host", "GuideBot", "NativeGuideBotDrawer.cs"));
        drawerSource.Should().Contain("_session.ContextChanged -= _contextChangedHandler;");
        drawerSource.Should().Contain("_session.MessagesChanged -= _messagesChangedHandler;");
        drawerSource.Should().Contain("_subscribedBgTaskManager.StateChanged -= _bgStateChangedHandler;");

        var formSource = File.ReadAllText(Path.Combine(repoRoot, "native-win", "src", "Karobar.Host", "NativeWebView2Form.cs"));
        formSource.Should().Contain("_subscribedBgTaskManager.StateChanged -= _bgStateChangedHandler;");
        formSource.Should().Contain("_subscribedBgTaskManager.ProductsMutated -= _productsMutatedHandler;");
    }

    private sealed class StubFailingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Simulated offline WhatsApp sidecar");
        }
    }

    private sealed class StubCapturingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public StubCapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responder(request));
        }
    }
}

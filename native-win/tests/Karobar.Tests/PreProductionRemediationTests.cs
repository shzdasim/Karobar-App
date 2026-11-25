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
using Karobar.Api.Middleware;
using Karobar.Background;
using Karobar.Data;
using Karobar.Host;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Karobar.Tests;

public class PreProductionRemediationTests
{
    private const string ExpectedRealDbSha256 = "729e4481de9db73d245a3f011cb9cbcd8b8cd6c1997f6e9b99b8a7c06246c298";
    private const long ExpectedRealDbSize = 6_926_336L;
    private const int ExpectedRealDbProductCount = 14_041;

    private sealed class IsolatedR5Host : IAsyncDisposable
    {
        public WebApplication App { get; }
        public HttpClient Client { get; }
        public CookieContainer Cookies { get; }
        public KarobarDb Db { get; }
        public string TempRoot { get; }
        public string TempDbPath { get; }
        public LicenseService? LicenseService { get; }
        public string? TestPublicKey { get; }
        public string? TestSecretKey { get; }
        public string? TestMachineId { get; }
        private readonly string? _prevRoot;
        private readonly string? _prevAppRoot;

        private IsolatedR5Host(
            WebApplication app,
            HttpClient client,
            CookieContainer cookies,
            KarobarDb db,
            string tempRoot,
            string tempDbPath,
            string? prevRoot,
            string? prevAppRoot,
            LicenseService? licenseService,
            string? testPublicKey,
            string? testSecretKey,
            string? testMachineId)
        {
            App = app;
            Client = client;
            Cookies = cookies;
            Db = db;
            TempRoot = tempRoot;
            TempDbPath = tempDbPath;
            _prevRoot = prevRoot;
            _prevAppRoot = prevAppRoot;
            LicenseService = licenseService;
            TestPublicKey = testPublicKey;
            TestSecretKey = testSecretKey;
            TestMachineId = testMachineId;
        }

        public static async Task<IsolatedR5Host> StartAsync(
            bool seedPlatinumLicense = true,
            bool copyRealDb = false,
            bool seedAdminRole = true,
            bool useCookies = false)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_r5_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "public"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "backups"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "shared-pdfs"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "logs"));
            Directory.CreateDirectory(Path.Combine(tempRoot, "public"));

            var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
            var prevAppRoot = Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");
            var tempDbPath = Path.Combine(tempRoot, "database", "database.sqlite");

            try
            {
                if (copyRealDb)
                {
                    var realDbPath = Path.Combine(ResolveRepoRoot(), "database", "database.sqlite");
                    if (File.Exists(realDbPath) && new FileInfo(realDbPath).Length > 0)
                    {
                        File.Copy(realDbPath, tempDbPath, overwrite: true);
                    }
                }

                Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
                Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", tempRoot);
                KarobarDb.FlushSchemaCache();

                await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: tempDbPath)).EnsureSchemaReadyAsync();
                KarobarDb.FlushSchemaCache();

                var kDb = new KarobarDb(explicitSqlitePath: tempDbPath);
                if (seedAdminRole && await kDb.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = 'Admin'") == null)
                {
                    await kDb.InsertAsync("roles", new Dictionary<string, object?>
                    {
                        ["name"] = "Admin",
                        ["guard_name"] = "sanctum"
                    });
                }

                // Ensure at least one settings row exists with AI outbound providers disabled
                if (await kDb.HasTableAsync("settings"))
                {
                    var st = await kDb.QuerySingleOrDefaultAsync("SELECT id FROM settings ORDER BY id ASC LIMIT 1");
                    if (st == null)
                    {
                        await kDb.InsertAsync("settings", new Dictionary<string, object?>
                        {
                            ["store_name"] = "R5 QA Store",
                            ["currency_symbol"] = "Rs.",
                            ["currency_code"] = "PKR",
                            ["currency_decimal_places"] = 2,
                            ["cube_ai_enabled"] = 0,
                            ["cube_groq_enabled"] = 0,
                            ["cube_gemini_enabled"] = 0
                        });
                    }
                    else
                    {
                        await kDb.UpdateAsync("settings", st["id"]!, new Dictionary<string, object?>
                        {
                            ["cube_ai_enabled"] = 0,
                            ["cube_groq_enabled"] = 0,
                            ["cube_gemini_enabled"] = 0
                        });
                    }
                }

                LicenseService? licSvc = null;
                string? pubKey = null;
                string? secKey = null;
                string? machineId = null;

                if (seedPlatinumLicense)
                {
                    var cliRunner = new KarobarCliCommandRunner(tempRoot);
                    var kpRes = cliRunner.ExecuteLicenseIssue(new Dictionary<string, string?> { ["generate-keypair"] = "true" });
                    kpRes.Ok.Should().BeTrue();
                    pubKey = kpRes.Data["public_key"]!.ToString()!;
                    secKey = kpRes.Data["secret_key"]!.ToString()!;
                    machineId = cliRunner.ExecuteLicenseMachineId(new Dictionary<string, string?>()).Output.Trim();

                    LicenseService.SetTestOverrides(publicKey: pubKey, machineId: machineId);

                    var issueRes = cliRunner.ExecuteLicenseIssue(new Dictionary<string, string?>
                    {
                        ["machine"] = machineId,
                        ["tier"] = "platinum",
                        ["days"] = "365",
                        ["secret-key"] = secKey,
                        ["name"] = "R5 QA Platinum"
                    });
                    issueRes.Ok.Should().BeTrue();
                    var licenseKey = issueRes.Data["license_key"]!.ToString()!;

                    await kDb.ExecuteAsync("DELETE FROM licenses");
                    licSvc = new LicenseService(kDb);
                    licSvc.ClearPeriodicStatusCache();
                    var (actOk, _, actReason) = await licSvc.ActivateLicenseKeyAsync(licenseKey);
                    actOk.Should().BeTrue(actReason ?? "Platinum license activation must succeed");
                }

                var port = KestrelHostServer.FindFreePort(22400 + Random.Shared.Next(0, 600));
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
                HttpClient client;
                if (useCookies)
                {
                    var handler = new HttpClientHandler
                    {
                        CookieContainer = cookies,
                        UseCookies = true,
                        AllowAutoRedirect = false
                    };
                    client = new HttpClient(handler) { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
                }
                else
                {
                    client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
                }
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                return new IsolatedR5Host(
                    app, client, cookies, kDb, tempRoot, tempDbPath,
                    prevRoot, prevAppRoot, licSvc, pubKey, secKey, machineId);
            }
            catch
            {
                LicenseService.SetTestOverrides(null, null, null);
                Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
                Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", prevAppRoot);
                KarobarDb.FlushSchemaCache();
                try { SqliteConnection.ClearAllPools(); } catch { }
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try { Client.Dispose(); } catch { }
            try { await App.StopAsync(); } catch { }
            try { await App.DisposeAsync(); } catch { }
            try { LicenseService?.ClearPeriodicStatusCache(); } catch { }
            LicenseService.SetTestOverrides(null, null, null);
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", _prevRoot);
            Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", _prevAppRoot);
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, recursive: true); } catch { }
        }
    }

    private static string ResolveRepoRoot()
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

    private static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task AssertRateLimit429Async(HttpResponseMessage resp, string contextLabel)
    {
        resp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, $"{contextLabel} must return 429 once limit is exceeded");
        resp.Headers.TryGetValues("Retry-After", out var retryValues).Should().BeTrue($"{contextLabel} must include Retry-After header");
        int.TryParse(retryValues!.FirstOrDefault(), out var retrySeconds).Should().BeTrue($"{contextLabel} Retry-After must be integer seconds");
        retrySeconds.Should().BeGreaterThanOrEqualTo(1, $"{contextLabel} Retry-After must be >= 1");

        var rawBody = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(rawBody);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        doc.RootElement.EnumerateObject().Count().Should().Be(1, $"{contextLabel} 429 JSON must only contain 'message'");
        doc.RootElement.GetProperty("message").GetString().Should().Be("Too Many Attempts.");
    }

    private static async Task<long> EnsureRoleAsync(KarobarDb db, string roleName)
    {
        var existing = await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = @name", new { name = roleName });
        if (existing != null) return Convert.ToInt64(existing["id"]);
        return await db.InsertAsync("roles", new Dictionary<string, object?>
        {
            ["name"] = roleName,
            ["guard_name"] = "sanctum"
        });
    }

    private static async Task<long> EnsurePermissionAsync(KarobarDb db, string permName)
    {
        var existing = await db.QuerySingleOrDefaultAsync("SELECT id FROM permissions WHERE name = @name", new { name = permName });
        if (existing != null) return Convert.ToInt64(existing["id"]);
        return await db.InsertAsync("permissions", new Dictionary<string, object?>
        {
            ["name"] = permName,
            ["guard_name"] = "sanctum"
        });
    }

    private static async Task<(long UserId, string Token)> SeedUserWithRoleAsync(
        KarobarDb db,
        string name,
        string email,
        string password,
        string roleName,
        string[]? directPermissions = null,
        string status = "active")
    {
        await EnsureRoleAsync(db, roleName);
        var uid = await db.InsertAsync("users", new Dictionary<string, object?>
        {
            ["name"] = name,
            ["email"] = email,
            ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword(password),
            ["status"] = status,
            ["remember_token_expires_at"] = DateTime.UtcNow.AddHours(4).ToString("yyyy-MM-dd HH:mm:ss")
        });
        await db.SyncUserRolesAsync(uid, [roleName]);
        if (directPermissions is { Length: > 0 })
        {
            foreach (var p in directPermissions)
            {
                await EnsurePermissionAsync(db, p);
            }
            await db.SyncUserPermissionsAsync(uid, directPermissions);
        }
        var token = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(
            db, uid, $"r5-{roleName}", DateTimeOffset.UtcNow.AddHours(4));
        return (uid, token);
    }

    // =========================================================================
    // R5.1 Rate Limiting Across All 7 Live Kestrel Routes
    // =========================================================================

    [Fact]
    public async Task R5_1_RateLimiting_AllSevenRoutes_EnforceExactLimits_AndReturn429Json()
    {
        await using var host = await IsolatedR5Host.StartAsync(
            seedPlatinumLicense: true,
            copyRealDb: false,
            seedAdminRole: true,
            useCookies: true);

        var (adminId, adminToken) = await SeedUserWithRoleAsync(
            host.Db, "R51 Admin", "r51-admin@karobar.local", "AdminPass123!", "Admin");
        adminId.Should().BeGreaterThan(0);

        await File.WriteAllTextAsync(Path.Combine(host.TempRoot, "storage", "app", "server-launch.key"), "r51-launch-key");

        // 1. 10/1: POST /api/login (10 allowed -> 11th 429)
        for (var i = 0; i < 10; i++)
        {
            var r = await host.Client.PostAsync("/api/login", JsonBody(new { email = "nope@karobar.local", password = "wrong" }));
            r.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"/api/login #{i + 1} must not be throttled");
        }
        await AssertRateLimit429Async(
            await host.Client.PostAsync("/api/login", JsonBody(new { email = "nope@karobar.local", password = "wrong" })),
            "POST /api/login");

        // 2. 10/1: POST /api/license/activate (10 allowed -> 11th 429)
        for (var i = 0; i < 10; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/license/activate");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = JsonBody(new { license_key = "" });
            var r = await host.Client.SendAsync(req);
            r.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, $"/api/license/activate #{i + 1} must reach validation");
        }
        using (var req11 = new HttpRequestMessage(HttpMethod.Post, "/api/license/activate"))
        {
            req11.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req11.Content = JsonBody(new { license_key = "" });
            await AssertRateLimit429Async(await host.Client.SendAsync(req11), "POST /api/license/activate");
        }

        // 3. 10/1: POST /server-launch (valid CSRF cookie+token: 10 allowed -> 11th 429)
        var launchFormRes = await host.Client.GetAsync("/server-launch");
        launchFormRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var launchHtml = await launchFormRes.Content.ReadAsStringAsync();
        var csrfMatch = Regex.Match(launchHtml, @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""");
        csrfMatch.Success.Should().BeTrue();
        var csrfToken = WebUtility.HtmlDecode(csrfMatch.Groups[1].Value);

        for (var i = 0; i < 10; i++)
        {
            var r = await host.Client.PostAsync("/server-launch", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = csrfToken,
                ["key"] = "wrong-launch-key"
            }));
            r.StatusCode.Should().Be(HttpStatusCode.Redirect, $"/server-launch #{i + 1} must reach controller");
        }
        await AssertRateLimit429Async(
            await host.Client.PostAsync("/server-launch", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = csrfToken,
                ["key"] = "wrong-launch-key"
            })),
            "POST /server-launch");

        // 4. 10/1: POST /api/settings/sqlite-import/unlock (10 allowed -> 11th 429)
        for (var i = 0; i < 10; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/settings/sqlite-import/unlock");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = JsonBody(new { password = "AdminPass123!" });
            var r = await host.Client.SendAsync(req);
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"/api/settings/sqlite-import/unlock #{i + 1} must succeed");
        }
        using (var req11 = new HttpRequestMessage(HttpMethod.Post, "/api/settings/sqlite-import/unlock"))
        {
            req11.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req11.Content = JsonBody(new { password = "AdminPass123!" });
            await AssertRateLimit429Async(await host.Client.SendAsync(req11), "POST /api/settings/sqlite-import/unlock");
        }

        // 5. 30/1: POST /api/cube/ask (local fast path, 0 outbound calls: 30 allowed -> 31st 429)
        for (var i = 0; i < 30; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/cube/ask");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = JsonBody(new
            {
                question = "How much customer receivable udhar balance is pending?",
                language = "en",
                current_route = "/dashboard",
                explain_mode = "qa"
            });
            var r = await host.Client.SendAsync(req);
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"/api/cube/ask #{i + 1} must succeed locally");
        }
        using (var req31 = new HttpRequestMessage(HttpMethod.Post, "/api/cube/ask"))
        {
            req31.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req31.Content = JsonBody(new
            {
                question = "How much customer receivable udhar balance is pending?",
                language = "en",
                current_route = "/dashboard",
                explain_mode = "qa"
            });
            await AssertRateLimit429Async(await host.Client.SendAsync(req31), "POST /api/cube/ask");
        }

        // 6. 30/1: POST /api/session/extend (30 allowed -> 31st 429 with exact Retry-After & JSON)
        for (var i = 0; i < 30; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/session/extend");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = JsonBody(new { minutes = 10 });
            var r = await host.Client.SendAsync(req);
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"/api/session/extend #{i + 1} must succeed");
        }
        using (var req31 = new HttpRequestMessage(HttpMethod.Post, "/api/session/extend"))
        {
            req31.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req31.Content = JsonBody(new { minutes = 10 });
            await AssertRateLimit429Async(await host.Client.SendAsync(req31), "POST /api/session/extend");
        }

        // 7. 60/1: POST /api/cube/execute-task (60 allowed -> 61st 429)
        for (var i = 0; i < 60; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/cube/execute-task");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Content = JsonBody(new { task_type = "delete_all" });
            var r = await host.Client.SendAsync(req);
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"/api/cube/execute-task #{i + 1} must complete cleanly");
        }
        using (var req61 = new HttpRequestMessage(HttpMethod.Post, "/api/cube/execute-task"))
        {
            req61.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req61.Content = JsonBody(new { task_type = "delete_all" });
            await AssertRateLimit429Async(await host.Client.SendAsync(req61), "POST /api/cube/execute-task");
        }
    }

    // =========================================================================
    // R5.2 RBAC Read/Write Gates & ?api_token= Query Auth Matrix
    // =========================================================================

    [Fact]
    public async Task R5_2_RbacReadWriteFixes_And_QueryTokenMatrix_EnforcedInFullPipeline()
    {
        await using var host = await IsolatedR5Host.StartAsync(
            seedPlatinumLicense: true,
            copyRealDb: false,
            seedAdminRole: true);

        var (adminId, adminToken) = await SeedUserWithRoleAsync(
            host.Db, "R52 Admin", "r52-admin@karobar.local", "Pass123!", "Admin");
        var (_, zeroPermToken) = await SeedUserWithRoleAsync(
            host.Db, "R52 Zero", "r52-zero@karobar.local", "Pass123!", "Cashier");
        var (_, settingsViewOnlyToken) = await SeedUserWithRoleAsync(
            host.Db, "R52 Viewer", "r52-viewer@karobar.local", "Pass123!", "SettingsViewer",
            directPermissions: ["settings.view"]);

        // Ensure active theme and terminal exist
        var activeThemeResp = await host.Client.GetAsync("/api/theme-settings/active");
        activeThemeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        long themeId;
        using (var doc = JsonDocument.Parse(await activeThemeResp.Content.ReadAsStringAsync()))
        {
            themeId = doc.RootElement.GetProperty("id").GetInt64();
        }

        var termId = await host.Db.InsertAsync("terminals", new Dictionary<string, object?>
        {
            ["terminal_uid"] = "TERM-R52-001",
            ["name"] = "Counter R52",
            ["mode"] = "terminal",
            ["is_authorized"] = 1,
            ["last_seen_at"] = DateTime.UtcNow.ToString("O")
        });

        // 1. 0-permission user: 403 on read endpoints, 200 on /api/theme-settings/active (both unauth and 0-perm Bearer)
        foreach (var path in new[] { "/api/terminals", "/api/theme-settings", $"/api/theme-settings/{themeId}" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", zeroPermToken);
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        using (var activeZeroReq = new HttpRequestMessage(HttpMethod.Get, "/api/theme-settings/active"))
        {
            activeZeroReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", zeroPermToken);
            (await host.Client.SendAsync(activeZeroReq)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 2. Non-admin with settings.view ONLY (no settings.update): 200 on reads, 403 on writes
        foreach (var path in new[] { "/api/terminals", "/api/theme-settings", $"/api/theme-settings/{themeId}" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settingsViewOnlyToken);
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.OK, $"settings.view must allow GET {path}");
        }

        var writeChecks = new[]
        {
            (HttpMethod.Put, "/api/theme-settings/active", (object)new { primary_color = "#112233" }),
            (HttpMethod.Post, "/api/theme-settings", (object)new { name = "Blocked Theme" }),
            (HttpMethod.Put, $"/api/theme-settings/{themeId}", (object)new { name = "Blocked Update" }),
            (HttpMethod.Put, $"/api/terminals/{termId}", (object)new { name = "Blocked Terminal Rename" })
        };
        foreach (var (method, path, payload) in writeChecks)
        {
            using var req = new HttpRequestMessage(method, path);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settingsViewOnlyToken);
            req.Content = JsonBody(payload);
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Forbidden, $"settings.view without settings.update must get 403 on {method} {path}");
        }

        // 3. Seed domain records for ?api_token= download/export tests
        var catId = await host.Db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "R52 Cat" });
        var brandId = await host.Db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "R52 Brand" });
        var custId = await host.Db.InsertAsync("customers", new Dictionary<string, object?> { ["name"] = "R52 Cust", ["phone"] = "03001234567" });
        var prodId = await host.Db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = "R52-P1",
            ["barcode"] = "R52-B1",
            ["name"] = "R52 Medicine",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["pack_size"] = 10,
            ["quantity"] = 20,
            ["pack_sale_price"] = 100m,
            ["unit_sale_price"] = 10m
        });
        var saleId = await host.Db.InsertAsync("sale_invoices", new Dictionary<string, object?>
        {
            ["user_id"] = adminId,
            ["customer_id"] = custId,
            ["posted_number"] = "SI-R52-1",
            ["date"] = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            ["gross_amount"] = 100m,
            ["total"] = 100m,
            ["total_receive"] = 100m,
            ["sale_type"] = "retail",
            ["invoice_type"] = "debit",
            ["status"] = "posted"
        });
        await host.Db.InsertAsync("sale_invoice_items", new Dictionary<string, object?>
        {
            ["sale_invoice_id"] = saleId,
            ["product_id"] = prodId,
            ["pack_size"] = 10,
            ["quantity"] = 10,
            ["price"] = 10m,
            ["sub_total"] = 100m
        });

        long backupId;
        using (var bkReq = new HttpRequestMessage(HttpMethod.Post, "/api/backups"))
        {
            bkReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            bkReq.Content = JsonBody(new { type = "database" });
            var bkRes = await host.Client.SendAsync(bkReq);
            bkRes.StatusCode.Should().Be(HttpStatusCode.Created);
            using var bkDoc = JsonDocument.Parse(await bkRes.Content.ReadAsStringAsync());
            backupId = bkDoc.RootElement.GetProperty("backup").GetProperty("id").GetInt64();
        }

        var tok = Uri.EscapeDataString(adminToken);

        // 4. ?api_token= rejected (401) on general API routes + POST /api/products + method guard
        foreach (var deniedGet in new[]
        {
            $"/api/users?api_token={tok}",
            $"/api/products?api_token={tok}",
            $"/api/settings?api_token={tok}",
            $"/api/terminals?api_token={tok}",
            $"/api/user?api_token={tok}"
        })
        {
            (await host.Client.GetAsync(deniedGet)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"?api_token= must be rejected on {deniedGet}");
        }

        (await host.Client.PostAsync($"/api/products?api_token={tok}", JsonBody(new { name = "Blocked" })))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized, "?api_token= must not authenticate POST /api/products");

        var methodGuardResp = await host.Client.PostAsync($"/api/reports/current-stock/pdf?api_token={tok}", new StringContent(""));
        methodGuardResp.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.MethodNotAllowed);
        methodGuardResp.IsSuccessStatusCode.Should().BeFalse();

        // 5. ?api_token= allowed (200) on explicit print/download/export routes
        foreach (var allowedGet in new[]
        {
            $"/api/reports/current-stock/pdf?api_token={tok}",
            $"/api/share/pdf?type=sale_invoice&id={saleId}&api_token={tok}",
            $"/api/backups/{backupId}/download?api_token={tok}",
            $"/api/server/terminal-config?api_token={tok}",
            $"/api/server/export-database?api_token={tok}",
            $"/sale-invoices/{saleId}/dosage-labels?api_token={tok}",
            $"/api/products/export?api_token={tok}",
            $"/api/customers/export?api_token={tok}"
        })
        {
            var r = await host.Client.GetAsync(allowedGet);
            r.StatusCode.Should().Be(HttpStatusCode.OK, $"?api_token= must succeed on {allowedGet}");
        }

        var postExportDbResp = await host.Client.PostAsync($"/api/server/export-database?api_token={tok}", new StringContent(""));
        postExportDbResp.StatusCode.Should().Be(HttpStatusCode.OK, "?api_token= must succeed on POST /api/server/export-database");
    }

    // =========================================================================
    // R5.3 Stale-Token Public Access Across All 4 [AllowAnonymous] Routes
    // =========================================================================

    [Fact]
    public async Task R5_3_StaleExpiredRevokedInactiveBearerTokens_SucceedOnAllFourPublicRoutes_AndRejectOnApiUser()
    {
        await using var host = await IsolatedR5Host.StartAsync(
            seedPlatinumLicense: true,
            copyRealDb: false,
            seedAdminRole: true);

        // (a) remember_token_expires_at expired
        var remExpUid = await host.Db.InsertAsync("users", new Dictionary<string, object?>
        {
            ["name"] = "R53 RemExpired",
            ["email"] = "r53-remexp@karobar.local",
            ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("Pass123!"),
            ["status"] = "active",
            ["remember_token_expires_at"] = DateTime.UtcNow.AddMinutes(-20).ToString("yyyy-MM-dd HH:mm:ss")
        });
        var remExpToken = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(
            host.Db, remExpUid, "rem-expired", DateTimeOffset.UtcNow.AddHours(2));

        // (b) personal_access_tokens.expires_at expired
        var patExpUid = await host.Db.InsertAsync("users", new Dictionary<string, object?>
        {
            ["name"] = "R53 PatExpired",
            ["email"] = "r53-patexp@karobar.local",
            ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("Pass123!"),
            ["status"] = "active",
            ["remember_token_expires_at"] = DateTime.UtcNow.AddHours(2).ToString("yyyy-MM-dd HH:mm:ss")
        });
        var patExpToken = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(
            host.Db, patExpUid, "pat-expired", DateTimeOffset.UtcNow.AddMinutes(-10));

        // (b2) revoked / non-existent token
        const string revokedToken = "999999|revoked-stale-token-value";

        // (c) inactive user token
        var inactiveUid = await host.Db.InsertAsync("users", new Dictionary<string, object?>
        {
            ["name"] = "R53 Inactive",
            ["email"] = "r53-inactive@karobar.local",
            ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("Pass123!"),
            ["status"] = "inactive"
        });
        var inactiveToken = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(
            host.Db, inactiveUid, "inactive-tok", DateTimeOffset.UtcNow.AddHours(2));

        var staleTokens = new[]
        {
            ("remember_token_expires_at expired", remExpToken, HttpStatusCode.Unauthorized),
            ("personal_access_tokens.expires_at expired", patExpToken, HttpStatusCode.Unauthorized),
            ("revoked/non-existent token", revokedToken, HttpStatusCode.Unauthorized),
            ("inactive user token", inactiveToken, HttpStatusCode.Forbidden)
        };

        var publicRoutes = new[]
        {
            "/api/system/version",
            "/api/terminals/ping",
            "/terms-of-service",
            "/print/thermal-preview/standard"
        };

        foreach (var (label, token, expectedUserStatus) in staleTokens)
        {
            foreach (var route in publicRoutes)
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, route);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var resp = await host.Client.SendAsync(req);
                resp.StatusCode.Should().Be(HttpStatusCode.OK, $"{route} must return 200 OK even with {label}");
            }

            using var userReq = new HttpRequestMessage(HttpMethod.Get, "/api/user");
            userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var userResp = await host.Client.SendAsync(userReq);
            userResp.StatusCode.Should().Be(expectedUserStatus, $"/api/user must reject {label}");
        }
    }

    // =========================================================================
    // R5.4 "Super Admin" Role Across All 8 Components
    // =========================================================================

    [Fact]
    public async Task R5_4_SuperAdminRole_WorksAcrossAllEightComponents()
    {
        await using var host = await IsolatedR5Host.StartAsync(
            seedPlatinumLicense: true,
            copyRealDb: false,
            seedAdminRole: false, // Isolate ONLY "Super Admin" as admin role
            useCookies: true);

        var db = host.Db;
        var superAdminRoleId = await EnsureRoleAsync(db, "Super Admin");
        var cashierRoleId = await EnsureRoleAsync(db, "Cashier");
        var mgrRoleId = await EnsureRoleAsync(db, "UserManager");

        // Seed standard permissions (NOT assigned to Super Admin!)
        var seededPerms = new[]
        {
            "user.view", "user.create", "user.update", "user.assign.roles",
            "role.create", "role.update", "role.delete", "settings.view", "sale-invoice.discount-override"
        };
        foreach (var p in seededPerms)
        {
            await EnsurePermissionAsync(db, p);
        }

        var (superAdminId, superAdminToken) = await SeedUserWithRoleAsync(
            db, "R54 Super Admin", "r54-superadmin@karobar.local", "SuperPass123!", "Super Admin");
        var (cashierId, cashierToken) = await SeedUserWithRoleAsync(
            db, "R54 Cashier", "r54-cashier@karobar.local", "CashPass123!", "Cashier");
        var (_, managerToken) = await SeedUserWithRoleAsync(
            db, "R54 NonAdmin Mgr", "r54-mgr@karobar.local", "MgrPass123!", "UserManager",
            directPermissions: ["user.view", "user.create", "user.update", "user.assign.roles", "role.create", "role.update", "role.delete"]);

        // Component 1: KarobarDb
        KarobarDb.IsAdminRoleName("Super Admin").Should().BeTrue();
        (await db.UserHasRoleAsync(superAdminId, KarobarDb.AdminRoleNames)).Should().BeTrue();
        (await db.UserCanAsync(superAdminId, "any.unseeded.permission")).Should().BeTrue();
        var allPermNames = await db.GetUserAllPermissionNamesAsync(superAdminId);
        allPermNames.Should().Contain(seededPerms);

        // Component 2: ApiControllerBase (IsAdminAsync via GET /api/reports/narcotics & /api/system/update-progress)
        foreach (var adminOnlyRoute in new[] { "/api/reports/narcotics", "/api/system/update-progress" })
        {
            using var okReq = new HttpRequestMessage(HttpMethod.Get, adminOnlyRoute);
            okReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            (await host.Client.SendAsync(okReq)).StatusCode.Should().Be(HttpStatusCode.OK);

            using var denyReq = new HttpRequestMessage(HttpMethod.Get, adminOnlyRoute);
            denyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
            (await host.Client.SendAsync(denyReq)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // Component 3: SpatiePermissionAuthorizationFilter (POST /api/permissions -> 201, DELETE -> 204 vs 403)
        using (var denyPermReq = new HttpRequestMessage(HttpMethod.Post, "/api/permissions"))
        {
            denyPermReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
            denyPermReq.Content = JsonBody(new { name = "custom.blocked.perm" });
            (await host.Client.SendAsync(denyPermReq)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        long createdPermId;
        using (var createPermReq = new HttpRequestMessage(HttpMethod.Post, "/api/permissions"))
        {
            createPermReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            createPermReq.Content = JsonBody(new { name = "custom.r54.probe" });
            var createPermResp = await host.Client.SendAsync(createPermReq);
            createPermResp.StatusCode.Should().Be(HttpStatusCode.Created);
            using var pDoc = JsonDocument.Parse(await createPermResp.Content.ReadAsStringAsync());
            createdPermId = pDoc.RootElement.GetProperty("id").GetInt64();
        }
        using (var delPermReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/permissions/{createdPermId}"))
        {
            delPermReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            (await host.Client.SendAsync(delPermReq)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Component 4: UserController
        // 4a. Super Admin creates a second Super Admin user
        long secondSaId;
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/users"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            req.Content = JsonBody(new
            {
                name = "Second Super Admin",
                email = "r54-sa2@karobar.local",
                password = "Password123!",
                roles = new[] { "Super Admin" }
            });
            var resp = await host.Client.SendAsync(req);
            resp.StatusCode.Should().Be(HttpStatusCode.Created);
            using var uDoc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            secondSaId = uDoc.RootElement.GetProperty("id").GetInt64();
        }

        // 4b. Non-admin manager blocked (403) from creating Super Admin or modifying Super Admin target
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/users"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
            req.Content = JsonBody(new
            {
                name = "Blocked SA",
                email = "r54-blocked-sa@karobar.local",
                password = "Password123!",
                roles = new[] { "Super Admin" }
            });
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{superAdminId}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
            req.Content = JsonBody(new { name = "Hacked" });
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{cashierId}/roles"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
            req.Content = JsonBody(new { roles = new[] { "Super Admin" } });
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        // 4c. Super Admin syncs roles & direct permissions, then demotes secondSaId so superAdminId is sole active Super Admin
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{secondSaId}/permissions"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            req.Content = JsonBody(new { permissions = new[] { "user.view" } });
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{secondSaId}/roles"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            req.Content = JsonBody(new { roles = new[] { "Cashier" } });
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // 4d. IsLastActiveAdminAsync 422 guard on sole active Super Admin
        using (var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/users/{superAdminId}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/users/{superAdminId}/roles"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            req.Content = JsonBody(new { roles = new[] { "Cashier" } });
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        // Component 5: RoleController
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/roles/{cashierRoleId}/permissions"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            req.Content = JsonBody(new { permissions = new[] { "settings.view" } });
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/roles"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            req.Content = JsonBody(new { name = "Super Admin" });
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Put, $"/api/roles/{superAdminRoleId}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            req.Content = JsonBody(new { name = "NotSuperAdmin" });
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Delete, $"/api/roles/{superAdminRoleId}"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }

        // Component 6: InvoiceCalculationService (discount override via Super Admin supervisor_password without supervisor_email)
        var catId = await db.InsertAsync("categories", new Dictionary<string, object?> { ["name"] = "R54 Cat" });
        var brandId = await db.InsertAsync("brands", new Dictionary<string, object?> { ["name"] = "R54 Brand" });
        var prodId = await db.InsertAsync("products", new Dictionary<string, object?>
        {
            ["product_code"] = "R54-DISC-1",
            ["name"] = "Discount Guard Item",
            ["category_id"] = catId,
            ["brand_id"] = brandId,
            ["pack_size"] = 1,
            ["quantity"] = 50,
            ["pack_sale_price"] = 100m,
            ["unit_sale_price"] = 100m,
            ["max_discount"] = 5m
        });

        var calcSvc = new InvoiceCalculationService(db);
        var resolvedSupervisor = await calcSvc.ResolveAuthorizingUserIdAsync(
            cashierId,
            new Dictionary<string, object?> { ["supervisor_password"] = "SuperPass123!" },
            "sale-invoice.discount-override");
        resolvedSupervisor.Should().Be(superAdminId);

        var items = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["product_id"] = prodId,
                ["quantity"] = 1,
                ["price"] = 100m,
                ["item_discount_percentage"] = 20m
            }
        };

        await FluentActions.Invoking(() => calcSvc.CalculateAndValidateSaleAsync(
                new Dictionary<string, object?> { ["sale_type"] = "retail" },
                items,
                userId: cashierId,
                discountOverride: true))
            .Should().ThrowAsync<InvoiceValidationException>()
            .Where(ex => ex.StatusCode == 403);

        var calcResult = await calcSvc.CalculateAndValidateSaleAsync(
            new Dictionary<string, object?>
            {
                ["sale_type"] = "retail",
                ["supervisor_password"] = "SuperPass123!"
            },
            items,
            userId: cashierId,
            discountOverride: true);
        Convert.ToDouble(calcResult["total"]).Should().BeApproximately(80.0, 0.01);

        // Component 8: POST /server-launch with ONLY Super Admin active admin
        await File.WriteAllTextAsync(Path.Combine(host.TempRoot, "storage", "app", "server-launch.key"), "sa-launch-secret");
        var formRes = await host.Client.GetAsync("/server-launch");
        formRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var formHtml = await formRes.Content.ReadAsStringAsync();
        var csrfToken = WebUtility.HtmlDecode(
            Regex.Match(formHtml, @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""").Groups[1].Value);

        var launchPost = await host.Client.PostAsync("/server-launch", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = csrfToken,
            ["key"] = "sa-launch-secret"
        }));
        launchPost.StatusCode.Should().Be(HttpStatusCode.OK);
        var launchOkHtml = await launchPost.Content.ReadAsStringAsync();
        launchOkHtml.Should().Contain("localStorage.setItem('token'");
        launchOkHtml.Should().Contain("r54-superadmin@karobar.local");
        launchOkHtml.Should().Contain("Super Admin");

        // Component 7: LicenseController (hasAnyAdmins blocks cashier 403; Super Admin passes activate & deactivate)
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/license/activate"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
            req.Content = JsonBody(new { license_key = "KRBL.FAKE" });
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/license/activate"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            req.Content = JsonBody(new { license_key = "" });
            // 422 confirms admin gate passed (not 403)
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/license/deactivate"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
            req.Content = JsonBody(new { password = "CashPass123!" });
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        using (var req = new HttpRequestMessage(HttpMethod.Post, "/api/license/deactivate"))
        {
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
            req.Content = JsonBody(new { password = "SuperPass123!" });
            (await host.Client.SendAsync(req)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    // =========================================================================
    // R5.5 Legal Documents & /server-launch CSRF Cookie+Token Enforcement
    // =========================================================================

    [Fact]
    public async Task R5_5_LegalDocuments_RenderAllAssetSections_And_ServerLaunchCsrf_RejectsMissingCookieOrToken()
    {
        var repoRoot = ResolveRepoRoot();
        var termsAsset = await File.ReadAllTextAsync(Path.Combine(repoRoot, "native-win", "assets", "TermsAndConditions.txt"), Encoding.UTF8);
        var privacyAsset = await File.ReadAllTextAsync(Path.Combine(repoRoot, "native-win", "assets", "PrivacyPolicy.txt"), Encoding.UTF8);
        termsAsset.Should().Contain("1. SINGLE-MACHINE HARDWARE-BOUND LICENSE GRANT");
        privacyAsset.Should().Contain("1. 100% LOCAL-FIRST DATA OWNERSHIP");

        await using var host = await IsolatedR5Host.StartAsync(
            seedPlatinumLicense: true,
            copyRealDb: false,
            seedAdminRole: false,
            useCookies: true);

        await SeedUserWithRoleAsync(
            host.Db, "R55 Super Admin", "r55-sa@karobar.local", "SuperPass123!", "Super Admin");
        await File.WriteAllTextAsync(Path.Combine(host.TempRoot, "storage", "app", "server-launch.key"), "r55-launch-key");

        // 1. Terms of service renders ALL sections from native-win/assets/TermsAndConditions.txt (not fallback)
        var termsRes = await host.Client.GetAsync("/terms-of-service");
        termsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var termsHtml = await termsRes.Content.ReadAsStringAsync();
        termsHtml.Should().Contain("IMPORTANT — READ CAREFULLY BEFORE INSTALLING");
        termsHtml.Should().Contain("1. SINGLE-MACHINE HARDWARE-BOUND LICENSE GRANT");
        termsHtml.Should().Contain("2. ANTI-TAMPERING &amp; SYSTEM CLOCK INTEGRITY");
        termsHtml.Should().Contain("3. PERMITTED COMMERCIAL &amp; ON-PREMISE USE");
        termsHtml.Should().Contain("4. LOCAL DATA STORAGE &amp; BACKUP RESPONSIBILITY");
        termsHtml.Should().Contain("5. WARRANTY DISCLAIMER &amp; LIMITATION OF LIABILITY");
        termsHtml.Should().Contain("KRBL. certificate");

        // 2. Privacy policy renders ALL sections from native-win/assets/PrivacyPolicy.txt (not fallback)
        var privacyRes = await host.Client.GetAsync("/privacy-policy");
        privacyRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var privacyHtml = await privacyRes.Content.ReadAsStringAsync();
        privacyHtml.Should().Contain("1. 100% LOCAL-FIRST DATA OWNERSHIP");
        privacyHtml.Should().Contain("2. HARDWARE FINGERPRINTING FOR LICENSE ENFORCEMENT");
        privacyHtml.Should().Contain("3. OPTIONAL OUTBOUND INTEGRATIONS");
        privacyHtml.Should().Contain("4. ADMINISTRATOR CREDENTIALS &amp; ACCESS CONTROL");
        privacyHtml.Should().Contain("I have read and agree to the Karobar App Privacy Policy");

        // 3. CSRF enforcement on POST /server-launch
        var getLaunch = await host.Client.GetAsync("/server-launch");
        getLaunch.StatusCode.Should().Be(HttpStatusCode.OK);
        var getLaunchHtml = await getLaunch.Content.ReadAsStringAsync();
        var csrfToken = WebUtility.HtmlDecode(
            Regex.Match(getLaunchHtml, @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""").Groups[1].Value);
        csrfToken.Should().NotBeNullOrWhiteSpace();

        // (a) Missing form token -> 400
        var missingTokenRes = await host.Client.PostAsync("/server-launch", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["key"] = "r55-launch-key"
        }));
        missingTokenRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // (b) Tampered form token -> 400
        var tamperedRes = await host.Client.PostAsync("/server-launch", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = "tampered-token-xyz",
            ["key"] = "r55-launch-key"
        }));
        tamperedRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // (c) Valid form token WITHOUT Karobar.Antiforgery cookie -> 400
        using (var cookielessClient = new HttpClient(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false })
        {
            BaseAddress = host.Client.BaseAddress
        })
        {
            var missingCookieRes = await cookielessClient.PostAsync("/server-launch", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = csrfToken,
                ["key"] = "r55-launch-key"
            }));
            missingCookieRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        // (d) Valid cookie + valid token + valid key -> 200 OK
        var validRes = await host.Client.PostAsync("/server-launch", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = csrfToken,
            ["key"] = "r55-launch-key"
        }));
        validRes.StatusCode.Should().Be(HttpStatusCode.OK);
        (await validRes.Content.ReadAsStringAsync()).Should().Contain("localStorage.setItem('token'");
    }

    // =========================================================================
    // R5.6 Live HTTP SqliteImportController + Waseela FK Remapping (All 3 Formats)
    // =========================================================================

    [Fact]
    public async Task R5_6_SqliteImport_LiveHttp_AllThreeFormats_FkRemapping_And_CancelCleanup()
    {
        await using var host = await IsolatedR5Host.StartAsync(
            seedPlatinumLicense: true,
            copyRealDb: false,
            seedAdminRole: true);

        var (adminId, adminToken) = await SeedUserWithRoleAsync(
            host.Db, "R56 Admin", "r56-admin@karobar.local", "AdminPass123!", "Admin");
        adminId.Should().Be(1);

        // Seed colliding id=1 target records
        await using (var dst = new SqliteConnection($"Data Source={host.TempDbPath};Pooling=False;"))
        {
            await dst.OpenAsync();
            await dst.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await dst.ExecuteAsync("""
                INSERT INTO categories (id, name) VALUES (1, 'Target Cat');
                INSERT INTO brands (id, name) VALUES (1, 'Target Brand');
                INSERT INTO suppliers (id, name) VALUES (1, 'Target Supplier');
                INSERT INTO customers (id, name, email) VALUES (1, 'Target Cust', 'target@karobar.local');
                INSERT INTO products (id, product_code, barcode, name, category_id, brand_id, supplier_id, pack_size, quantity, pack_sale_price, unit_sale_price)
                    VALUES (1, 'TGT-001', 'TGT-001', 'Target Product', 1, 1, 1, 10, 50, 100, 10);
                INSERT INTO batches (id, product_id, batch_number, expiry_date, quantity)
                    VALUES (1, 1, 'TGT-B1', '2028-12-31', 50);
                INSERT INTO purchase_invoices (id, user_id, supplier_id, invoice_number, posted_date, total_amount, total_paid, status)
                    VALUES (1, 1, 1, 'PI-TGT-1', '2026-10-01', 100, 100, 'posted');
                INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total, total_receive, sale_type, invoice_type, status)
                    VALUES (1, 1, 1, 'SI-TGT-1', '2026-10-01', 100, 100, 100, 'retail', 'debit', 'posted');
                """);
        }

        // Unlock via HTTP
        string swToken;
        using (var unlockReq = new HttpRequestMessage(HttpMethod.Post, "/api/settings/sqlite-import/unlock"))
        {
            unlockReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            unlockReq.Content = JsonBody(new { password = "AdminPass123!" });
            var unlockRes = await host.Client.SendAsync(unlockReq);
            unlockRes.StatusCode.Should().Be(HttpStatusCode.OK);
            using var uDoc = JsonDocument.Parse(await unlockRes.Content.ReadAsStringAsync());
            swToken = uDoc.RootElement.GetProperty("unlock_token").GetString()!;
        }

        async Task<JsonDocument> UploadMultipartAsync(string filePath, string fileName)
        {
            using var form = new MultipartFormDataContent();
            var bytes = await File.ReadAllBytesAsync(filePath);
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "database_file", fileName);

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/settings/sqlite-import/upload");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Headers.Add("X-Software-Admin-Token", swToken);
            req.Content = form;
            var res = await host.Client.SendAsync(req);
            var raw = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, $"upload {fileName} failed: {raw}");
            return JsonDocument.Parse(raw);
        }

        async Task<JsonDocument> PreviewTableAsync(string sessionToken, string sourceTable, string targetTable)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/settings/sqlite-import/preview-table");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Headers.Add("X-Software-Admin-Token", swToken);
            req.Content = JsonBody(new { token = sessionToken, source_table = sourceTable, target_table = targetTable });
            var res = await host.Client.SendAsync(req);
            var raw = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, $"preview {sourceTable}->{targetTable} failed: {raw}");
            return JsonDocument.Parse(raw);
        }

        async Task<JsonDocument> CommitAsync(string sessionToken)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/settings/sqlite-import/commit");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            req.Headers.Add("X-Software-Admin-Token", swToken);
            req.Content = JsonBody(new
            {
                token = sessionToken,
                options = new
                {
                    mode = "merge",
                    conflict_policy = "skip",
                    create_backup = false,
                    reconcile_batches = true
                }
            });
            var res = await host.Client.SendAsync(req);
            var raw = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, $"commit failed: {raw}");
            return JsonDocument.Parse(raw);
        }

        // --- FORMAT 1: .sqlite with colliding id=1 across parents, children, and polymorphic FKs ---
        var sourceSqlitePath = Path.Combine(host.TempRoot, "r56_source.sqlite");
        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: sourceSqlitePath)).EnsureSchemaReadyAsync();
        SqliteConnection.ClearAllPools();
        await using (var src = new SqliteConnection($"Data Source={sourceSqlitePath};Pooling=False;"))
        {
            await src.OpenAsync();
            await src.ExecuteAsync("PRAGMA foreign_keys = OFF;");
            await src.ExecuteAsync("""
                INSERT INTO categories (id, name) VALUES (1, 'SQLite Cat');
                INSERT INTO brands (id, name) VALUES (1, 'SQLite Brand');
                INSERT INTO suppliers (id, name) VALUES (1, 'SQLite Supplier');
                INSERT INTO customers (id, name, email) VALUES (1, 'SQLite Cust', 'sqcust@karobar.local');
                INSERT INTO products (id, product_code, barcode, name, formulation, category_id, brand_id, supplier_id, pack_size, quantity, pack_sale_price, unit_sale_price)
                    VALUES (1, 'SQL-P1', 'SQL-P1', 'SQLite Batched Prod', 'Cefixime', 1, 1, 1, 10, 15, 300, 30);
                INSERT INTO products (id, product_code, barcode, name, formulation, category_id, brand_id, supplier_id, pack_size, quantity, pack_sale_price, unit_sale_price)
                    VALUES (2, 'SQL-UNB', 'SQL-UNB', 'SQLite Unbatched Prod', 'Azithromycin', 1, 1, 1, 10, 10, 200, 20);
                INSERT INTO batches (id, product_id, batch_number, expiry_date, quantity)
                    VALUES (1, 1, 'SQL-B1', '2028-06-30', 15);
                INSERT INTO purchase_invoices (id, user_id, supplier_id, invoice_number, posted_date, total_amount, total_paid, status)
                    VALUES (1, 999, 1, 'PI-SQL-1', '2026-11-01', 200, 200, 'posted');
                INSERT INTO purchase_invoice_items (id, purchase_invoice_id, product_id, pack_quantity, pack_size, unit_quantity, pack_purchase_price, unit_purchase_price, pack_sale_price, unit_sale_price, margin, sub_total)
                    VALUES (1, 1, 1, 1, 10, 10, 200, 20, 300, 30, 50, 200);
                INSERT INTO sale_invoices (id, user_id, customer_id, posted_number, date, gross_amount, total, total_receive, sale_type, invoice_type, status)
                    VALUES (1, 999, 1, 'SI-SQL-1', '2026-11-02', 300, 300, 300, 'retail', 'debit', 'posted');
                INSERT INTO sale_invoice_items (id, sale_invoice_id, product_id, quantity, price, sub_total)
                    VALUES (1, 1, 1, 10, 30, 300);
                INSERT INTO payments (id, payment_number, party_type, party_id, payment_date, amount, allocated_amount, unapplied_amount, payment_method, status, created_by_id)
                    VALUES (1, 'PAY-SQL-1', 'customer', 1, '2026-11-02', 300, 300, 0, 'cash', 'posted', 999);
                INSERT INTO payment_allocations (id, payment_id, invoice_type, invoice_id, amount)
                    VALUES (1, 1, 'sale_invoice', 1, 300);
                INSERT INTO customer_ledgers (id, customer_id, sale_invoice_id, entry_date, entry_type, invoice_total, total_received, balance_remaining, is_manual)
                    VALUES (1, 1, 1, '2026-11-02', 'invoice', 300, 300, 0, 0);
                INSERT INTO supplier_ledgers (id, supplier_id, purchase_invoice_id, entry_date, entry_type, invoice_total, total_paid, credit_remaining, is_manual)
                    VALUES (1, 1, 1, '2026-11-01', 'invoice', 200, 200, 0, 0);
                """);
            await src.ExecuteAsync("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
        }

        string sqliteSessionToken;
        using (var upDoc = await UploadMultipartAsync(sourceSqlitePath, "r56_source.sqlite"))
        {
            sqliteSessionToken = upDoc.RootElement.GetProperty("session_token").GetString()!;
        }
        using (await PreviewTableAsync(sqliteSessionToken, "sale_invoices", "sale_invoices")) { }
        using (var cDoc = await CommitAsync(sqliteSessionToken))
        {
            var recon = cDoc.RootElement.GetProperty("stats").GetProperty("reconciliation");
            recon.GetProperty("opening_batches_created").GetInt32().Should().BeGreaterThanOrEqualTo(1);
            recon.GetProperty("formulations_synced").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        }

        // Verify SQLite FK remapping & target id=1 untouched
        (await host.Db.QuerySingleOrDefaultAsync("SELECT name FROM categories WHERE id = 1"))!["name"]?.ToString().Should().Be("Target Cat");
        (await host.Db.QuerySingleOrDefaultAsync("SELECT name FROM products WHERE id = 1"))!["name"]?.ToString().Should().Be("Target Product");

        var sqlCatId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM categories WHERE name = 'SQLite Cat'");
        var sqlBrandId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM brands WHERE name = 'SQLite Brand'");
        var sqlSupId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM suppliers WHERE name = 'SQLite Supplier'");
        var sqlCustId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM customers WHERE name = 'SQLite Cust'");
        var sqlProdId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'SQL-P1'");
        var sqlUnbProdId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'SQL-UNB'");
        var sqlPurchId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM purchase_invoices WHERE invoice_number = 'PI-SQL-1'");
        var sqlSaleId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM sale_invoices WHERE posted_number = 'SI-SQL-1'");
        var sqlPayId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM payments WHERE payment_number = 'PAY-SQL-1'");

        new[] { sqlCatId, sqlBrandId, sqlSupId, sqlCustId, sqlProdId, sqlUnbProdId, sqlPurchId, sqlSaleId }
            .Should().OnlyContain(id => id > 1, "all colliding id=1 parents must remap to new IDs > 1");

        var sqlProd = await host.Db.QuerySingleOrDefaultAsync("SELECT category_id, brand_id, supplier_id FROM products WHERE id = @id", new { id = sqlProdId });
        Convert.ToInt64(sqlProd!["category_id"]).Should().Be(sqlCatId);
        Convert.ToInt64(sqlProd["brand_id"]).Should().Be(sqlBrandId);
        Convert.ToInt64(sqlProd["supplier_id"]).Should().Be(sqlSupId);

        (await host.Db.ExecuteScalarAsync<long>("SELECT product_id FROM batches WHERE batch_number = 'SQL-B1'")).Should().Be(sqlProdId);
        (await host.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM batches WHERE batch_number = @b", new { b = $"OPENING-{sqlUnbProdId}" })).Should().Be(1);
        (await host.Db.ExecuteScalarAsync<long>("SELECT supplier_id FROM purchase_invoices WHERE id = @id", new { id = sqlPurchId })).Should().Be(sqlSupId);

        var sqlPiItem = await host.Db.QuerySingleOrDefaultAsync("SELECT purchase_invoice_id, product_id FROM purchase_invoice_items WHERE purchase_invoice_id = @pid", new { pid = sqlPurchId });
        Convert.ToInt64(sqlPiItem!["product_id"]).Should().Be(sqlProdId);

        var sqlSale = await host.Db.QuerySingleOrDefaultAsync("SELECT customer_id, user_id FROM sale_invoices WHERE id = @id", new { id = sqlSaleId });
        Convert.ToInt64(sqlSale!["customer_id"]).Should().Be(sqlCustId);
        Convert.ToInt64(sqlSale["user_id"]).Should().Be(1);

        var sqlSiItem = await host.Db.QuerySingleOrDefaultAsync("SELECT sale_invoice_id, product_id FROM sale_invoice_items WHERE sale_invoice_id = @sid", new { sid = sqlSaleId });
        Convert.ToInt64(sqlSiItem!["product_id"]).Should().Be(sqlProdId);

        (await host.Db.ExecuteScalarAsync<long>("SELECT party_id FROM payments WHERE id = @id", new { id = sqlPayId })).Should().Be(sqlCustId);
        (await host.Db.ExecuteScalarAsync<long>("SELECT invoice_id FROM payment_allocations WHERE payment_id = @pid", new { pid = sqlPayId })).Should().Be(sqlSaleId);

        var sqlCustLedger = await host.Db.QuerySingleOrDefaultAsync("SELECT customer_id, sale_invoice_id FROM customer_ledgers WHERE sale_invoice_id = @sid", new { sid = sqlSaleId });
        Convert.ToInt64(sqlCustLedger!["customer_id"]).Should().Be(sqlCustId);

        var sqlSupLedger = await host.Db.QuerySingleOrDefaultAsync("SELECT supplier_id, purchase_invoice_id FROM supplier_ledgers WHERE purchase_invoice_id = @pid", new { pid = sqlPurchId });
        Convert.ToInt64(sqlSupLedger!["supplier_id"]).Should().Be(sqlSupId);

        // --- FORMAT 2: Waseela .sql with colliding id=1 across tblCategory, tblCompany, tblItems, tblBatch, tblCustomer, tblKhata ---
        var waseelaSqlPath = Path.Combine(host.TempRoot, "r56_waseela.sql");
        await File.WriteAllTextAsync(waseelaSqlPath, """
            CREATE TABLE [dbo].[tblCategory] (id INTEGER PRIMARY KEY, CategoryName TEXT);
            GO
            CREATE TABLE [dbo].[tblCompany] (id INTEGER PRIMARY KEY, CompanyName TEXT);
            GO
            CREATE TABLE [dbo].[tblItems] (id INTEGER PRIMARY KEY, ItemCode TEXT, ItemName TEXT, CatId INTEGER, CompanyId INTEGER, Formulation TEXT, PackSize INTEGER, TPPack REAL, RetailPrice REAL, ClosingStock INTEGER);
            GO
            CREATE TABLE [dbo].[tblBatch] (id INTEGER PRIMARY KEY, ItemId INTEGER, BatchNo TEXT, ExpiryDate TEXT, Qty INTEGER);
            GO
            CREATE TABLE [dbo].[tblCustomer] (id INTEGER PRIMARY KEY, PartyName TEXT, Phone TEXT);
            GO
            CREATE TABLE [dbo].[tblKhata] (id INTEGER PRIMARY KEY, PartyId INTEGER, OpeningBalance REAL, KhataDetail TEXT);
            GO
            INSERT INTO [dbo].[tblCategory] (id, CategoryName) VALUES (1, 'Waseela Cat');
            GO
            INSERT INTO [dbo].[tblCompany] (id, CompanyName) VALUES (1, 'Waseela Pharma');
            GO
            INSERT INTO [dbo].[tblItems] (id, ItemCode, ItemName, CatId, CompanyId, Formulation, PackSize, TPPack, RetailPrice, ClosingStock)
            VALUES (1, 'WAS-SQL-1', 'Waseela Syrup', 1, 1, 'Cetirizine', 1, 120, 150, 20);
            GO
            INSERT INTO [dbo].[tblBatch] (id, ItemId, BatchNo, ExpiryDate, Qty)
            VALUES (1, 1, 'WAS-B1', '2028-09-30', 20);
            GO
            INSERT INTO [dbo].[tblCustomer] (id, PartyName, Phone)
            VALUES (1, 'Waseela SQL Customer', '03005554444');
            GO
            INSERT INTO [dbo].[tblKhata] (id, PartyId, OpeningBalance, KhataDetail)
            VALUES (1, 1, 1250.00, 'Waseela SQL Opening Khata');
            GO
            """, Encoding.UTF8);

        string sqlSessionToken;
        using (var upDoc = await UploadMultipartAsync(waseelaSqlPath, "r56_waseela.sql"))
        {
            upDoc.RootElement.GetProperty("waseela_detection").GetProperty("is_waseela_schema").GetBoolean().Should().BeTrue();
            sqlSessionToken = upDoc.RootElement.GetProperty("session_token").GetString()!;
        }
        using (await PreviewTableAsync(sqlSessionToken, "tblItems", "products")) { }
        using (await CommitAsync(sqlSessionToken)) { }

        var wasCatId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM categories WHERE name = 'Waseela Cat'");
        var wasBrandId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM brands WHERE name = 'Waseela Pharma'");
        var wasProdId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'WAS-SQL-1'");
        var wasCustId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM customers WHERE name = 'Waseela SQL Customer'");

        new[] { wasCatId, wasBrandId, wasProdId, wasCustId }.Should().OnlyContain(id => id > 1);
        var wasProd = await host.Db.QuerySingleOrDefaultAsync("SELECT category_id, brand_id FROM products WHERE id = @id", new { id = wasProdId });
        Convert.ToInt64(wasProd!["category_id"]).Should().Be(wasCatId);
        Convert.ToInt64(wasProd["brand_id"]).Should().Be(wasBrandId);
        (await host.Db.ExecuteScalarAsync<long>("SELECT product_id FROM batches WHERE batch_number = 'WAS-B1'")).Should().Be(wasProdId);
        (await host.Db.ExecuteScalarAsync<long>("SELECT customer_id FROM customer_ledgers WHERE description = 'Waseela SQL Opening Khata'")).Should().Be(wasCustId);

        // --- FORMAT 3: Waseela .csv (tblItems.csv with UTF-8 BOM + colliding id=1 + ClosingStock > 0) ---
        var waseelaCsvPath = Path.Combine(host.TempRoot, "tblItems.csv");
        await File.WriteAllTextAsync(waseelaCsvPath,
            "\uFEFFid,ItemCode,ItemName,CategoryName,CompanyName,Formulation,PackSize,TPPack,RetailPrice,ClosingStock\n" +
            "1,WAS-CSV-1,Waseela CSV Capsule,CSV Capsules,CSV Labs,Omeprazole,14,220,260,30\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        string csvSessionToken;
        using (var upDoc = await UploadMultipartAsync(waseelaCsvPath, "tblItems.csv"))
        {
            upDoc.RootElement.GetProperty("waseela_detection").GetProperty("is_waseela_schema").GetBoolean().Should().BeTrue();
            csvSessionToken = upDoc.RootElement.GetProperty("session_token").GetString()!;
        }
        using (await PreviewTableAsync(csvSessionToken, "tblItems", "products")) { }
        using (var cDoc = await CommitAsync(csvSessionToken))
        {
            var recon = cDoc.RootElement.GetProperty("stats").GetProperty("reconciliation");
            recon.GetProperty("opening_batches_created").GetInt32().Should().BeGreaterThanOrEqualTo(1);
            recon.GetProperty("formulations_synced").GetInt32().Should().BeGreaterThanOrEqualTo(1);
        }

        var csvProdId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM products WHERE product_code = 'WAS-CSV-1'");
        csvProdId.Should().BeGreaterThan(1);
        var csvProd = await host.Db.QuerySingleOrDefaultAsync("SELECT category_id, brand_id FROM products WHERE id = @id", new { id = csvProdId });
        var csvCatId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM categories WHERE name = 'CSV Capsules'");
        var csvBrandId = await host.Db.ExecuteScalarAsync<long>("SELECT id FROM brands WHERE name = 'CSV Labs'");
        Convert.ToInt64(csvProd!["category_id"]).Should().Be(csvCatId);
        Convert.ToInt64(csvProd["brand_id"]).Should().Be(csvBrandId);
        (await host.Db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM batches WHERE batch_number = @b", new { b = $"OPENING-{csvProdId}" })).Should().Be(1);

        // Target id=1 rows still untouched after all 3 imports
        (await host.Db.QuerySingleOrDefaultAsync("SELECT name FROM products WHERE id = 1"))!["name"]?.ToString().Should().Be("Target Product");

        // --- Global PRAGMA foreign_key_check & explicit orphan-FK sweep ---
        await using (var chk = new SqliteConnection($"Data Source={host.TempDbPath};Pooling=False;"))
        {
            await chk.OpenAsync();
            var fkViolations = (await chk.QueryAsync("PRAGMA foreign_key_check;")).ToList();
            fkViolations.Should().BeEmpty("target DB must have zero SQLite foreign key violations after all 3 imports");

            (await chk.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products p LEFT JOIN categories c ON c.id = p.category_id WHERE p.category_id IS NOT NULL AND c.id IS NULL")).Should().Be(0);
            (await chk.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products p LEFT JOIN brands b ON b.id = p.brand_id WHERE p.brand_id IS NOT NULL AND b.id IS NULL")).Should().Be(0);
            (await chk.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM batches b LEFT JOIN products p ON p.id = b.product_id WHERE p.id IS NULL")).Should().Be(0);
            (await chk.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sale_invoices si LEFT JOIN customers c ON c.id = si.customer_id WHERE si.customer_id IS NOT NULL AND c.id IS NULL")).Should().Be(0);
            (await chk.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sale_invoice_items sii LEFT JOIN sale_invoices si ON si.id = sii.sale_invoice_id WHERE si.id IS NULL")).Should().Be(0);
            (await chk.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM sale_invoice_items sii LEFT JOIN products p ON p.id = sii.product_id WHERE p.id IS NULL")).Should().Be(0);
            (await chk.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM purchase_invoices pi LEFT JOIN suppliers s ON s.id = pi.supplier_id WHERE pi.supplier_id IS NOT NULL AND s.id IS NULL")).Should().Be(0);
            (await chk.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM purchase_invoice_items pii LEFT JOIN purchase_invoices pi ON pi.id = pii.purchase_invoice_id WHERE pi.id IS NULL")).Should().Be(0);
            (await chk.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM payment_allocations pa LEFT JOIN payments p ON p.id = pa.payment_id WHERE p.id IS NULL")).Should().Be(0);
            (await chk.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM customer_ledgers cl LEFT JOIN customers c ON c.id = cl.customer_id WHERE c.id IS NULL")).Should().Be(0);
            (await chk.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM supplier_ledgers sl LEFT JOIN suppliers s ON s.id = sl.supplier_id WHERE s.id IS NULL")).Should().Be(0);
        }

        // --- Real staged DELETE /api/settings/sqlite-import/{token} cancellation ---
        string cancelToken;
        using (var upDoc = await UploadMultipartAsync(waseelaCsvPath, "tblItems_cancel.csv"))
        {
            cancelToken = upDoc.RootElement.GetProperty("session_token").GetString()!;
        }

        var stagingDir = Path.Combine(host.TempRoot, "storage", "app", "imports", "sqlite");
        var stagedFilesBefore = Directory.GetFiles(stagingDir, $"{cancelToken}*");
        stagedFilesBefore.Should().NotBeEmpty("staged session files must exist on disk before cancellation");

        using (var delReq = new HttpRequestMessage(HttpMethod.Delete, $"/api/settings/sqlite-import/{cancelToken}"))
        {
            delReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            delReq.Headers.Add("X-Software-Admin-Token", swToken);
            var delRes = await host.Client.SendAsync(delReq);
            delRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        Directory.GetFiles(stagingDir, $"{cancelToken}*").Should().BeEmpty("cancelled session files must be removed from disk");

        using (var postCancelCommitReq = new HttpRequestMessage(HttpMethod.Post, "/api/settings/sqlite-import/commit"))
        {
            postCancelCommitReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            postCancelCommitReq.Headers.Add("X-Software-Admin-Token", swToken);
            postCancelCommitReq.Content = JsonBody(new
            {
                token = cancelToken,
                options = new { mode = "merge", conflict_policy = "skip", create_backup = false }
            });
            var postCancelRes = await host.Client.SendAsync(postCancelCommitReq);
            postCancelRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "committing a cancelled session must fail with 422");
        }
    }

    // =========================================================================
    // R5.7 Read-Only Real-World Database Verification (14,041 Products)
    // =========================================================================

    [Fact]
    public async Task R5_7_RealWorldDatabase_IsolatedCopy_Verifies14041Products_And_PreservesOriginalSha256()
    {
        var repoRoot = ResolveRepoRoot();
        var realDbPath = Path.Combine(repoRoot, "database", "database.sqlite");

        if (!File.Exists(realDbPath))
        {
            if (string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            Assert.Fail($"Real-world database not found at '{realDbPath}' outside GitHub Actions.");
        }

        static async Task<(string Sha256, long Length, DateTime MtimeUtc)> SnapshotFileAsync(string path)
        {
            var fi = new FileInfo(path);
            await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var hash = await SHA256.HashDataAsync(fs);
            return (Convert.ToHexStringLower(hash), fi.Length, fi.LastWriteTimeUtc);
        }

        var before = await SnapshotFileAsync(realDbPath);
        before.Length.Should().BeGreaterThan(0);
        before.Sha256.Should().NotBeNullOrWhiteSpace();

        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_r57_realdb_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
        var tempDbPath = Path.Combine(tempRoot, "database", "database.sqlite");
        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        var prevAppRoot = Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");

        try
        {
            File.Copy(realDbPath, tempDbPath, overwrite: true);
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
            Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", tempRoot);
            KarobarDb.FlushSchemaCache();

            var factory = new DbConnectionFactory(explicitSqlitePath: tempDbPath);
            var bootstrapper = new DatabaseBootstrapper(factory);
            await bootstrapper.EnsureSchemaReadyAsync();
            var schemaResult = await bootstrapper.VerifySchemaCompatibilityAsync();

            schemaResult.IsCompatible.Should().BeTrue();
            schemaResult.MissingTables.Should().BeEmpty();
            schemaResult.MissingColumns.Should().BeEmpty();
            schemaResult.DbSetErrors.Should().BeEmpty();

            KarobarDb.FlushSchemaCache();
            var kDb = new KarobarDb(explicitSqlitePath: tempDbPath);

            var productCount = await kDb.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM products");
            productCount.Should().BeGreaterThanOrEqualTo(ExpectedRealDbProductCount);

            var adminRow = await kDb.QuerySingleOrDefaultAsync(@"
                SELECT u.id FROM users u
                INNER JOIN model_has_roles mhr ON mhr.model_id = u.id
                INNER JOIN roles r ON r.id = mhr.role_id
                WHERE LOWER(r.name) IN ('admin', 'super-admin', 'super admin')
                ORDER BY u.id ASC LIMIT 1");
            var adminUid = adminRow != null ? Convert.ToInt64(adminRow["id"]) : 1L;

            ControllerContext BuildAdminCtx(string queryString = "")
            {
                var ctx = new DefaultHttpContext();
                ctx.Items["KarobarUserId"] = adminUid;
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, adminUid.ToString())], "Sanctum"));
                if (!string.IsNullOrEmpty(queryString)) ctx.Request.QueryString = new QueryString(queryString);
                return new ControllerContext { HttpContext = ctx };
            }

            // ProductController.Index
            var prodCtrl = new ProductController(kDb) { ControllerContext = BuildAdminCtx("?page=1&per_page=25&q=&low_stock=") };
            var prodOk = (await prodCtrl.Index(perPage: 25, page: 1, q: "", lowStockRaw: "")).Should().BeOfType<OkObjectResult>().Subject;
            var prodDict = prodOk.Value.Should().BeAssignableTo<Dictionary<string, object?>>().Subject;
            Convert.ToInt32(prodDict["total"]).Should().Be(productCount);
            var firstPageRows = prodDict["data"].Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;
            firstPageRows.Should().HaveCount(25);

            // ProductController.Search against a real product name prefix from the 14,041 rows
            var sampleName = firstPageRows[0]["name"]?.ToString() ?? "";
            var prefix = sampleName.Length >= 3 ? sampleName[..3] : sampleName;
            var searchCtrl = new ProductController(kDb) { ControllerContext = BuildAdminCtx() };
            var searchOk = (await searchCtrl.Search(q: prefix, limit: 15)).Should().BeOfType<OkObjectResult>().Subject;
            var searchRows = searchOk.Value.Should().BeAssignableTo<List<Dictionary<string, object?>>>().Subject;
            searchRows.Should().NotBeEmpty();

            // SaleInvoiceController.Index & PurchaseInvoiceController.Index & DashboardController.Summary
            var saleCtrl = new SaleInvoiceController(kDb) { ControllerContext = BuildAdminCtx("?page=1&per_page=10&posted=&customer=") };
            var saleOk = (await saleCtrl.Index(customer_id: null, posted: "", customer: "", sale_type: "", invoice_type: "", page: 1, per_page: 10))
                .Should().BeOfType<OkObjectResult>().Subject;
            Convert.ToInt32(((Dictionary<string, object?>)saleOk.Value!)["total"]).Should().BeGreaterThan(0);

            var purchCtrl = new PurchaseInvoiceController(kDb) { ControllerContext = BuildAdminCtx("?page=1&per_page=10&posted=&supplier=") };
            var purchOk = (await purchCtrl.Index(supplier_id: null, posted: "", supplier: "", page: 1, per_page: 10))
                .Should().BeOfType<OkObjectResult>().Subject;
            Convert.ToInt32(((Dictionary<string, object?>)purchOk.Value!)["total"]).Should().BeGreaterThan(0);

            var dashCtrl = new DashboardController(kDb) { ControllerContext = BuildAdminCtx("?totals_only=1") };
            (await dashCtrl.Summary()).Should().BeOfType<OkObjectResult>();
        }
        finally
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", prevAppRoot);
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
        }

        Directory.Exists(tempRoot).Should().BeFalse();
        var after = await SnapshotFileAsync(realDbPath);
        after.Length.Should().Be(before.Length);
        after.Sha256.Should().Be(before.Sha256);
        after.MtimeUtc.Should().Be(before.MtimeUtc);
    }

    // =========================================================================
    // R5.8 Standalone Repo / Legacy Independence Verification
    // =========================================================================

    [Fact]
    public void R5_8_StandaloneRepo_LegacyPathsAbsent_NativeAssetsPresent_AndZeroLegacyTestRefs()
    {
        var repoRoot = ResolveRepoRoot();

        // 1. All 23 retired legacy Laravel/Electron paths + doc cleanup + 3 dead React files must be absent
        var retiredDirs = new[]
        {
            "desktop", "installer", "app", "bootstrap", "config", "routes",
            Path.Combine("database", "migrations"),
            Path.Combine("database", "seeders"),
            Path.Combine("database", "factories"),
            Path.Combine("resources", "views"),
            Path.Combine("tests", "Feature"),
            Path.Combine("tests", "Unit"),
            "deploy",
            "doc",
            Path.Combine("doc", "planning")
        };
        foreach (var d in retiredDirs)
        {
            Directory.Exists(Path.Combine(repoRoot, d)).Should().BeFalse($"retired directory '{d}' must not exist");
        }

        var retiredFiles = new[]
        {
            "artisan", "composer.json", "composer.lock", "phpunit.xml",
            Path.Combine("tests", "TestCase.php"),
            "karobar-server-setup.sh",
            Path.Combine("scripts", "build-windows-exe.ps1"),
            Path.Combine("scripts", "build-linux-server-installer.sh"),
            Path.Combine(".github", "workflows", "build-windows-exe.yml"),
            Path.Combine(".github", "workflows", "tests.yml"),
            Path.Combine("docs", "SERVER_AND_TERMINALS_SETUP_GUIDE.md"),
            Path.Combine("resources", "js", "components", "SubWindowManager.jsx"),
            Path.Combine("resources", "js", "components", "TopNavigation.jsx"),
            Path.Combine("resources", "js", "pages", "stock-adjustments", "Edit.jsx")
        };
        foreach (var f in retiredFiles)
        {
            File.Exists(Path.Combine(repoRoot, f)).Should().BeFalse($"retired file '{f}' must not exist");
        }

        // 2. Preserved paths + all 6 native-win/assets files must exist
        foreach (var p in new[]
        {
            Path.Combine("resources", "js"),
            Path.Combine("resources", "css"),
            Path.Combine("resources", "assistant"),
            Path.Combine("public", "build"),
            "native-win"
        })
        {
            Directory.Exists(Path.Combine(repoRoot, p)).Should().BeTrue($"preserved directory '{p}' must exist");
        }

        foreach (var f in new[]
        {
            Path.Combine("scripts", "whatsapp-agent.mjs"),
            Path.Combine("scripts", "build-cube-knowledge.mjs"),
            Path.Combine("public", "build", "manifest.json"),
            Path.Combine("public", "index.html"),
            Path.Combine("public", "version.json"),
            Path.Combine("native-win", "Karobar.Native.sln"),
            Path.Combine("native-win", "assets", "icon.ico"),
            Path.Combine("native-win", "assets", "icon.png"),
            Path.Combine("native-win", "assets", "MalikAsimShahzad-KarobarERP.cer"),
            Path.Combine("native-win", "assets", "TermsAndConditions.txt"),
            Path.Combine("native-win", "assets", "PrivacyPolicy.txt"),
            Path.Combine("native-win", "assets", "SumatraPDF.exe")
        })
        {
            File.Exists(Path.Combine(repoRoot, f)).Should().BeTrue($"required file '{f}' must exist");
        }

        // 3. Zero references to retired Laravel routes/manifests in native-win/tests/
        var testsRoot = Path.Combine(repoRoot, "native-win", "tests");
        var retiredTokens = new[]
        {
            string.Join("/", "routes", "api.php"),
            string.Join("/", "routes", "web.php"),
            string.Join(".", "index", "blade", "php"),
            string.Join("-", "laravel", "vite", "plugin")
        };

        var testSourceFiles = Directory.EnumerateFiles(testsRoot, "*.*", SearchOption.AllDirectories)
            .Where(p =>
            {
                var rel = Path.GetRelativePath(testsRoot, p);
                if (rel.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    rel.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith($"bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
                    rel.StartsWith($"obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                return p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                       p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        testSourceFiles.Should().NotBeEmpty();
        foreach (var file in testSourceFiles)
        {
            var content = File.ReadAllText(file);
            foreach (var token in retiredTokens)
            {
                content.Should().NotContain(token, $"test file '{Path.GetFileName(file)}' must not reference retired '{token}'");
            }
        }
    }
}

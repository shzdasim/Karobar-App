using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using static Karobar.Tests.Wave9And10LiveHttpIntegrationTests;

namespace Karobar.Tests;

public class WaveP1ReleaseBlockerTests
{
    private static async Task<(WebApplication App, HttpClient Client, KarobarDb Db, string TempRoot, string? PrevRoot)> StartFullPipelineServerAsync()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_p1_pipeline_{Guid.NewGuid():N}");
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

        var port = KestrelHostServer.FindFreePort(20250 + Random.Shared.Next(0, 400));
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
        builder.Services.AddKarobarApi(builder.Configuration, explicitSqlitePath: tempDbPath);

        var app = builder.Build();
        app.UseRouting();
        app.UseKarobarApiPipeline();
        app.MapControllers();

        await app.StartAsync();

        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return (app, client, kDb, tempRoot, prevRoot);
    }

    private static async Task CleanupServerAsync(WebApplication app, HttpClient client, string tempRoot, string? prevRoot)
    {
        client.Dispose();
        try { await app.StopAsync(); } catch { }
        try { await app.DisposeAsync(); } catch { }
        Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
        Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", prevRoot);
        try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void R1_3_AllThrottledControllerActions_HaveExpectedEnableRateLimitingPolicies()
    {
        var expectedPolicies = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AuthController.Login"] = "throttle_10_1",
            ["AuthController.ConfirmPassword"] = "throttle_10_1",
            ["AuthController.ForgotPassword"] = "throttle_10_1",
            ["AuthController.ResetPassword"] = "throttle_10_1",
            ["AuthController.ExtendSession"] = "throttle_30_1",
            ["LicenseController.Activate"] = "throttle_10_1",
            ["LicenseController.Deactivate"] = "throttle_10_1",
            ["SqliteImportController.Unlock"] = "throttle_10_1",
            ["SqliteImportController.Commit"] = "throttle_10_1",
            ["BackupController.Restore"] = "throttle_10_1",
            ["BackupController.RestoreFromUpload"] = "throttle_10_1",
            ["SystemUpdateController.InstallUpdate"] = "throttle_10_1",
            ["WebRoutesController.ServerLaunchSubmit"] = "throttle_10_1",
            ["CubeAssistantController.Ask"] = "throttle_30_1",
            ["CubeAssistantController.ExecuteTask"] = "throttle_60_1"
        };

        var apiAssembly = typeof(AuthController).Assembly;
        var actualPolicies = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var type in apiAssembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)))
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.GetCustomAttributes<HttpMethodAttribute>().Any())
                {
                    var rateAttr = method.GetCustomAttribute<EnableRateLimitingAttribute>();
                    if (rateAttr != null)
                    {
                        actualPolicies[$"{type.Name}.{method.Name}"] = rateAttr.PolicyName ?? "";
                    }
                }
            }
        }

        actualPolicies.Should().BeEquivalentTo(expectedPolicies);
    }

    [Fact]
    public async Task R1_3_And_R1_4_RateLimiting_Isolation_And_AllowAnonymousStaleTokenBypass_WorkInFullPipeline()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartFullPipelineServerAsync();
        try
        {
            // Seed active user with expired remember_token_expires_at, and an inactive user
            var activeId = await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "P1 Active",
                ["email"] = "p1-active@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("Pass123!"),
                ["status"] = "active",
                ["remember_token_expires_at"] = DateTime.UtcNow.AddMinutes(-15).ToString("yyyy-MM-dd HH:mm:ss")
            });
            var expiredSessionToken = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(
                db, activeId, "stale-session", DateTimeOffset.UtcNow.AddHours(1));

            var inactiveId = await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "P1 Inactive",
                ["email"] = "p1-inactive@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("Pass123!"),
                ["status"] = "inactive"
            });
            var inactiveToken = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(
                db, inactiveId, "inactive-session", DateTimeOffset.UtcNow.AddHours(1));

            // R1.4: Authenticated endpoint still rejects expired session (401 expired:true) & inactive user (403)
            using (var authExpiredReq = new HttpRequestMessage(HttpMethod.Get, "/api/user"))
            {
                authExpiredReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredSessionToken);
                var resp = await client.SendAsync(authExpiredReq);
                resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                (await resp.Content.ReadAsStringAsync()).Should().Contain("\"expired\":true");
            }

            // R1.4: [AllowAnonymous] routes succeed even with stale/expired/inactive Bearer token, and do not delete token
            foreach (var publicPath in new[] { "/api/system/version", "/api/terminals/ping", "/api/theme-settings/active", "/terms-of-service" })
            {
                using var req1 = new HttpRequestMessage(HttpMethod.Get, publicPath);
                req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", expiredSessionToken);
                var resp1 = await client.SendAsync(req1);
                resp1.StatusCode.Should().Be(HttpStatusCode.OK, $"{publicPath} must bypass CheckTokenExpiryMiddleware");

                using var req2 = new HttpRequestMessage(HttpMethod.Get, publicPath);
                req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", inactiveToken);
                var resp2 = await client.SendAsync(req2);
                resp2.StatusCode.Should().Be(HttpStatusCode.OK, $"{publicPath} must bypass CheckTokenExpiryMiddleware for inactive token");
            }

            var inactiveTokenStillPresent = await db.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM personal_access_tokens WHERE tokenable_id = @uid",
                new { uid = inactiveId });
            inactiveTokenStillPresent.Should().Be(1, "hitting [AllowAnonymous] routes must not purge tokens");

            using (var authInactiveReq = new HttpRequestMessage(HttpMethod.Get, "/api/user"))
            {
                authInactiveReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", inactiveToken);
                var resp = await client.SendAsync(authInactiveReq);
                resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            }

            // R1.3: throttle_10_1 on POST /api/login -> 10 allowed, 11th returns 429 + Retry-After + {"message":"Too Many Attempts."}
            for (var i = 0; i < 10; i++)
            {
                var loginResp = await client.PostAsync("/api/login", JsonBody(new { email = "nobody@karobar.local", password = "wrong" }));
                loginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"request #{i + 1} should not be rate limited");
            }

            var throttledLoginResp = await client.PostAsync("/api/login", JsonBody(new { email = "nobody@karobar.local", password = "wrong" }));
            throttledLoginResp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            throttledLoginResp.Headers.TryGetValues("Retry-After", out var retryValues).Should().BeTrue();
            int.Parse(retryValues!.First()).Should().BeGreaterThan(0);
            using (var doc = JsonDocument.Parse(await throttledLoginResp.Content.ReadAsStringAsync()))
            {
                doc.RootElement.GetProperty("message").GetString().Should().Be("Too Many Attempts.");
            }

            // R1.3: Route-partition isolation -> exhausting POST /api/login must NOT block POST /api/forgot-password
            var forgotResp = await client.PostAsync("/api/forgot-password", JsonBody(new { email = "p1-active@karobar.local" }));
            forgotResp.StatusCode.Should().Be(HttpStatusCode.OK, "different routes must have isolated rate limit partitions");

            // R1.3: throttle_30_1 on POST /api/session/extend -> 30 allowed, 31st returns 429
            var validExtendUser = await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "P1 Extend",
                ["email"] = "p1-extend@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("Pass123!"),
                ["status"] = "active",
                ["remember_token_expires_at"] = DateTime.UtcNow.AddMinutes(30).ToString("yyyy-MM-dd HH:mm:ss")
            });
            var validToken = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(
                db, validExtendUser, "extend-session", DateTimeOffset.UtcNow.AddHours(1));

            for (var i = 0; i < 30; i++)
            {
                using var extReq = new HttpRequestMessage(HttpMethod.Post, "/api/session/extend");
                extReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", validToken);
                extReq.Content = JsonBody(new { minutes = 10 });
                var extResp = await client.SendAsync(extReq);
                extResp.StatusCode.Should().Be(HttpStatusCode.OK, $"session/extend #{i + 1} should succeed");
            }

            using var throttledExtReq = new HttpRequestMessage(HttpMethod.Post, "/api/session/extend");
            throttledExtReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", validToken);
            throttledExtReq.Content = JsonBody(new { minutes = 10 });
            var throttledExtResp = await client.SendAsync(throttledExtReq);
            throttledExtResp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }
        finally
        {
            await CleanupServerAsync(app, client, tempRoot, prevRoot);
        }
    }

    [Fact]
    public async Task R1_5_RbacGates_SuperAdmin_QueryTokenRestriction_And_TrackTerminalFailClosed_WorkCorrectly()
    {
        var (app, client, db, tempRoot, prevRoot) = await StartLiveTestServerAsync();
        try
        {
            // Seed non-admin Cashier user (no settings.* permissions)
            if (await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = 'Cashier'") == null)
            {
                await db.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "Cashier", ["guard_name"] = "sanctum" });
            }
            var cashierId = await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "P1 Cashier",
                ["email"] = "p1-cashier@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("Pass123!"),
                ["status"] = "active"
            });
            await db.SyncUserRolesAsync(cashierId, new[] { "Cashier" });
            var cashierToken = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(
                db, cashierId, "cashier-tok", DateTimeOffset.UtcNow.AddHours(2));

            // Seed "Super Admin" user
            if (await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = 'Super Admin'") == null)
            {
                await db.InsertAsync("roles", new Dictionary<string, object?> { ["name"] = "Super Admin", ["guard_name"] = "sanctum" });
            }
            var superAdminId = await db.InsertAsync("users", new Dictionary<string, object?>
            {
                ["name"] = "P1 Super Admin",
                ["email"] = "p1-superadmin@karobar.local",
                ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword("Pass123!"),
                ["status"] = "active"
            });
            await db.SyncUserRolesAsync(superAdminId, new[] { "Super Admin" });
            var superAdminToken = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(
                db, superAdminId, "superadmin-tok", DateTimeOffset.UtcNow.AddHours(2));

            // Ensure at least one theme_settings row exists
            var themeResp = await client.GetAsync("/api/theme-settings/active");
            themeResp.StatusCode.Should().Be(HttpStatusCode.OK);
            long themeId;
            using (var tDoc = JsonDocument.Parse(await themeResp.Content.ReadAsStringAsync()))
            {
                themeId = tDoc.RootElement.GetProperty("id").GetInt64();
            }

            // 1. Non-admin gets 403 on GET /api/terminals, GET /api/theme-settings, GET /api/theme-settings/{id}
            foreach (var restrictedPath in new[] { "/api/terminals", "/api/theme-settings", $"/api/theme-settings/{themeId}" })
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, restrictedPath);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cashierToken);
                var resp = await client.SendAsync(req);
                resp.StatusCode.Should().Be(HttpStatusCode.Forbidden, $"{restrictedPath} must reject users without settings.view/settings.update");
            }

            // 2. "Super Admin" gets full admin access across terminals, theme-settings, users, narcotics report
            foreach (var adminPath in new[] { "/api/terminals", "/api/theme-settings", $"/api/theme-settings/{themeId}", "/api/users", "/api/reports/narcotics" })
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, adminPath);
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", superAdminToken);
                var resp = await client.SendAsync(req);
                resp.StatusCode.Should().Be(HttpStatusCode.OK, $"Super Admin must be granted access to {adminPath}");
            }

            // 3. ?api_token= is rejected (401) on regular API endpoints, allowed (200) on export/PDF download endpoints
            var usersQueryResp = await client.GetAsync($"/api/users?api_token={Uri.EscapeDataString(superAdminToken)}");
            usersQueryResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "?api_token= must not authenticate /api/users");

            var productsQueryResp = await client.GetAsync($"/api/products?api_token={Uri.EscapeDataString(superAdminToken)}");
            productsQueryResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "?api_token= must not authenticate /api/products");

            var exportPdfResp = await client.GetAsync($"/api/reports/current-stock/pdf?api_token={Uri.EscapeDataString(superAdminToken)}");
            exportPdfResp.StatusCode.Should().Be(HttpStatusCode.OK, "?api_token= must still work on PDF export routes");

            var exportCsvResp = await client.GetAsync($"/api/products/export?api_token={Uri.EscapeDataString(superAdminToken)}");
            exportCsvResp.StatusCode.Should().Be(HttpStatusCode.OK, "?api_token= must still work on export routes");

            // 4. TrackTerminalMiddleware fail-closed 503 when DB terminal lookup fails on non-handshake route
            var brokenTempRoot = Path.Combine(Path.GetTempPath(), $"karobar_broken_term_{Guid.NewGuid():N}");
            Directory.CreateDirectory(brokenTempRoot);
            var brokenDbPath = Path.Combine(brokenTempRoot, "bad.sqlite");
            await File.WriteAllTextAsync(brokenDbPath, "this-is-not-a-valid-sqlite-database");
            var brokenDb = new KarobarDb(explicitSqlitePath: brokenDbPath);
            var middleware = new TrackTerminalMiddleware(_ => Task.CompletedTask);
            using var loggerFactory = LoggerFactory.Create(_ => { });
            var logger = loggerFactory.CreateLogger<TrackTerminalMiddleware>();

            var failCtx = new DefaultHttpContext();
            failCtx.Request.Method = HttpMethods.Get;
            failCtx.Request.Path = "/api/sync/pulse";
            failCtx.Request.Headers["X-Terminal-Id"] = "TERM-BROKEN-TEST";
            failCtx.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(failCtx, brokenDb, new LicenseService(brokenDb), logger);
            failCtx.Response.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
            failCtx.Response.Body.Position = 0;
            using var reader = new StreamReader(failCtx.Response.Body);
            (await reader.ReadToEndAsync()).Should().Contain("TERMINAL_CHECK_UNAVAILABLE");

            // Handshake path must still pass through even if DB check fails
            var handshakeCtx = new DefaultHttpContext();
            handshakeCtx.Request.Method = HttpMethods.Post;
            handshakeCtx.Request.Path = "/api/terminals/ping";
            handshakeCtx.Request.Headers["X-Terminal-Id"] = "TERM-BROKEN-TEST";
            var nextCalled = false;
            var handshakeMw = new TrackTerminalMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
            await handshakeMw.InvokeAsync(handshakeCtx, brokenDb, new LicenseService(brokenDb), logger);
            nextCalled.Should().BeTrue("handshake paths must not deadlock on terminal check failure");

            try { Directory.Delete(brokenTempRoot, recursive: true); } catch { }
        }
        finally
        {
            await CleanupServerAsync(app, client, tempRoot, prevRoot);
        }
    }

    [Fact]
    public async Task R1_6_OtaSha256Enforcement_ProtectedPaths_And_CliApplyPatchHardening_WorkCorrectly()
    {
        var tmpAppRoot = Path.Combine(Path.GetTempPath(), $"karobar_p1_r16_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tmpAppRoot, "database"));
        Directory.CreateDirectory(Path.Combine(tmpAppRoot, "storage", "app"));
        Directory.CreateDirectory(Path.Combine(tmpAppRoot, "public"));

        try
        {
            // 1. Protected patch exclusions include network-mode.json, cube-learned-knowledge.json, storage/logs/
            KarobarDesktopBridge.IsProtectedPatchPath("storage/app/network-mode.json").Should().BeTrue();
            KarobarDesktopBridge.IsProtectedPatchPath("storage\\app\\network-mode.json").Should().BeTrue();
            KarobarDesktopBridge.IsProtectedPatchPath("storage/app/cube-learned-knowledge.json").Should().BeTrue();
            KarobarDesktopBridge.IsProtectedPatchPath("storage/logs/native-startup.log").Should().BeTrue();

            // 2. KarobarDesktopBridge.DownloadAndInstallUpdateAsync rejects missing/invalid SHA-256 without downloading
            var cfg = new NetworkConfigManager(tmpAppRoot);
            var bridge = new KarobarDesktopBridge(cfg, new WebView2WindowManager(cfg));

            using (var missingShaDoc = JsonDocument.Parse("""
                {
                  "mode": "download",
                  "version": "2.0.99",
                  "is_patch": true,
                  "patch_url": "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/Karobar-Native-Patch.zip"
                }
                """))
            {
                var resObj = await bridge.DownloadAndInstallUpdateAsync(missingShaDoc.RootElement.Clone());
                var json = JsonSerializer.Serialize(resObj);
                using var resDoc = JsonDocument.Parse(json);
                resDoc.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
                resDoc.RootElement.GetProperty("message").GetString().Should().Contain("SHA-256");
            }

            using (var invalidShaDoc = JsonDocument.Parse("""
                {
                  "mode": "download",
                  "version": "2.0.99",
                  "is_patch": false,
                  "installer_sha256": "not-a-valid-sha256"
                }
                """))
            {
                var resObj = await bridge.DownloadAndInstallUpdateAsync(invalidShaDoc.RootElement.Clone());
                var json = JsonSerializer.Serialize(resObj);
                using var resDoc = JsonDocument.Parse(json);
                resDoc.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
                resDoc.RootElement.GetProperty("message").GetString().Should().Contain("SHA-256");
            }

            // 3. KarobarCliCommandRunner.ExecuteApplyPatchAsync requires --verify-sha and allows whatsapp-agent.mjs
            var dbPath = Path.Combine(tmpAppRoot, "database", "database.sqlite");
            await File.WriteAllTextAsync(dbPath, "preserve-db");
            await File.WriteAllTextAsync(Path.Combine(tmpAppRoot, "storage", "app", "network-mode.json"), "preserve-net");

            var patchZipPath = Path.Combine(tmpAppRoot, "test-patch.zip");
            using (var archive = ZipFile.Open(patchZipPath, ZipArchiveMode.Create))
            {
                var appDllEntry = archive.CreateEntry("public/version.json");
                await using (var s = appDllEntry.Open())
                await using (var w = new StreamWriter(s, Encoding.UTF8))
                    await w.WriteAsync("{\"version\":\"2.0.99\"}");

                var waEntry = archive.CreateEntry("runtime/whatsapp-agent/whatsapp-agent.mjs");
                await using (var s = waEntry.Open())
                await using (var w = new StreamWriter(s, Encoding.UTF8))
                    await w.WriteAsync("console.log('updated-wa');");

                var runtimeNodeEntry = archive.CreateEntry("runtime/node/node.exe");
                await using (var s = runtimeNodeEntry.Open())
                await using (var w = new StreamWriter(s, Encoding.UTF8))
                    await w.WriteAsync("should-be-skipped");

                var dbEntry = archive.CreateEntry("database/database.sqlite");
                await using (var s = dbEntry.Open())
                await using (var w = new StreamWriter(s, Encoding.UTF8))
                    await w.WriteAsync("clobber-db-attempt");

                var netEntry = archive.CreateEntry("storage/app/network-mode.json");
                await using (var s = netEntry.Open())
                await using (var w = new StreamWriter(s, Encoding.UTF8))
                    await w.WriteAsync("clobber-net-attempt");
            }

            var patchSha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(patchZipPath)));
            var runner = new KarobarCliCommandRunner(tmpAppRoot);

            var missingShaResult = await runner.ExecuteApplyPatchAsync(new Dictionary<string, string?>
            {
                ["patch_zip"] = patchZipPath
            });
            missingShaResult.ExitCode.Should().Be(1);
            missingShaResult.Output.Should().Contain("--verify-sha");

            var badShaResult = await runner.ExecuteApplyPatchAsync(new Dictionary<string, string?>
            {
                ["patch_zip"] = patchZipPath,
                ["verify-sha"] = new string('0', 64)
            });
            badShaResult.ExitCode.Should().Be(1);

            var okResult = await runner.ExecuteApplyPatchAsync(new Dictionary<string, string?>
            {
                ["patch_zip"] = patchZipPath,
                ["verify-sha"] = patchSha
            });
            okResult.ExitCode.Should().Be(0, okResult.Output);

            (await File.ReadAllTextAsync(Path.Combine(tmpAppRoot, "public", "version.json"))).Should().Contain("2.0.99");
            (await File.ReadAllTextAsync(Path.Combine(tmpAppRoot, "runtime", "whatsapp-agent", "whatsapp-agent.mjs"))).Should().Contain("updated-wa");
            File.Exists(Path.Combine(tmpAppRoot, "runtime", "node", "node.exe")).Should().BeFalse();
            (await File.ReadAllTextAsync(dbPath)).Should().Be("preserve-db");
            (await File.ReadAllTextAsync(Path.Combine(tmpAppRoot, "storage", "app", "network-mode.json"))).Should().Be("preserve-net");
        }
        finally
        {
            try { if (Directory.Exists(tmpAppRoot)) Directory.Delete(tmpAppRoot, recursive: true); } catch { }
        }
    }
}

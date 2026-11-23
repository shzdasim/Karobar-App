using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using FluentAssertions;
using Karobar.Api;
using Karobar.Api.Middleware;
using Karobar.Background;
using Karobar.Data;
using Karobar.Host;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Karobar.Tests;

public class Milestone1SecurityAndWhatsAppTests
{
    private sealed class IsolatedM1Host : IAsyncDisposable
    {
        public WebApplication App { get; }
        public HttpClient Client { get; }
        public CookieContainer Cookies { get; }
        public KarobarDb Db { get; }
        public string TempRoot { get; }
        public string TempDbPath { get; }
        public LicenseService? LicenseService { get; }
        private readonly string? _prevRoot;
        private readonly string? _prevAppRoot;

        private IsolatedM1Host(
            WebApplication app,
            HttpClient client,
            CookieContainer cookies,
            KarobarDb db,
            string tempRoot,
            string tempDbPath,
            string? prevRoot,
            string? prevAppRoot,
            LicenseService? licenseService)
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
        }

        public static async Task<IsolatedM1Host> StartAsync(bool seedPlatinumLicense = true, bool useCookies = false)
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_m1_{Guid.NewGuid():N}");
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
                Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
                Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", tempRoot);
                KarobarDb.FlushSchemaCache();

                await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: tempDbPath)).EnsureSchemaReadyAsync();
                KarobarDb.FlushSchemaCache();

                var kDb = new KarobarDb(explicitSqlitePath: tempDbPath);
                if (await kDb.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = 'Admin'") == null)
                {
                    await kDb.InsertAsync("roles", new Dictionary<string, object?>
                    {
                        ["name"] = "Admin",
                        ["guard_name"] = "sanctum"
                    });
                }

                if (await kDb.HasTableAsync("settings"))
                {
                    var st = await kDb.QuerySingleOrDefaultAsync("SELECT id FROM settings ORDER BY id ASC LIMIT 1");
                    if (st == null)
                    {
                        await kDb.InsertAsync("settings", new Dictionary<string, object?>
                        {
                            ["store_name"] = "M1 QA Store",
                            ["currency_symbol"] = "Rs.",
                            ["currency_code"] = "PKR",
                            ["currency_decimal_places"] = 2,
                            ["network_server_token"] = "KRB-NET-M1TEST"
                        });
                    }
                }

                LicenseService? licSvc = null;
                if (seedPlatinumLicense)
                {
                    var cliRunner = new KarobarCliCommandRunner(tempRoot);
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
                        ["name"] = "M1 QA Platinum"
                    });
                    issueRes.Ok.Should().BeTrue();
                    var licenseKey = issueRes.Data["license_key"]!.ToString()!;

                    await kDb.ExecuteAsync("DELETE FROM licenses");
                    licSvc = new LicenseService(kDb);
                    licSvc.ClearPeriodicStatusCache();
                    var (actOk, _, actReason) = await licSvc.ActivateLicenseKeyAsync(licenseKey);
                    actOk.Should().BeTrue(actReason ?? "Platinum license activation must succeed");
                }

                var port = KestrelHostServer.FindFreePort(23200 + Random.Shared.Next(0, 500));
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

                return new IsolatedM1Host(app, client, cookies, kDb, tempRoot, tempDbPath, prevRoot, prevAppRoot, licSvc);
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

    private static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static async Task<(long UserId, string Token)> SeedUserAsync(
        KarobarDb db,
        string name,
        string email,
        string password,
        string roleName,
        DateTimeOffset? rememberExpiresAt = null)
    {
        var role = await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = @name", new { name = roleName });
        if (role == null)
        {
            await db.InsertAsync("roles", new Dictionary<string, object?>
            {
                ["name"] = roleName,
                ["guard_name"] = "sanctum"
            });
        }

        var uid = await db.InsertAsync("users", new Dictionary<string, object?>
        {
            ["name"] = name,
            ["email"] = email,
            ["password"] = SanctumAuthenticationHandler.HashLaravelBcryptPassword(password),
            ["status"] = "active",
            ["remember_token_expires_at"] = (rememberExpiresAt ?? DateTimeOffset.UtcNow.AddHours(4))
                .UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss")
        });
        await db.SyncUserRolesAsync(uid, [roleName]);
        var token = await SanctumAuthenticationHandler.IssuePersonalAccessTokenAsync(
            db, uid, $"m1-{roleName}", DateTimeOffset.UtcNow.AddHours(4));
        return (uid, token);
    }

    private static async Task AssertRateLimit429Async(HttpResponseMessage resp, string contextLabel)
    {
        resp.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, $"{contextLabel} must return 429 once limit is exceeded");
        resp.Headers.TryGetValues("Retry-After", out var retryValues).Should().BeTrue($"{contextLabel} must include Retry-After header");
        int.TryParse(retryValues!.FirstOrDefault(), out var retrySeconds).Should().BeTrue($"{contextLabel} Retry-After must be integer seconds");
        retrySeconds.Should().BeGreaterThanOrEqualTo(1);

        var rawBody = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(rawBody);
        doc.RootElement.GetProperty("message").GetString().Should().Be("Too Many Attempts.");
    }

    [Fact]
    public async Task MED1_BlockedTerminal_Receives403OnWhatsAppAgentLogout_WhileExactHandshakePathsRemainAllowed()
    {
        await using var host = await IsolatedM1Host.StartAsync();
        var (_, adminToken) = await SeedUserAsync(host.Db, "MED1 Admin", "med1-admin@karobar.local", "AdminPass123!", "Admin");

        var termUid = "TERM-MED1-BLOCKED";
        using (var pingReq = new HttpRequestMessage(HttpMethod.Post, "/api/terminals/ping"))
        {
            pingReq.Headers.TryAddWithoutValidation("X-Terminal-Id", termUid);
            pingReq.Headers.TryAddWithoutValidation("X-Network-Token", "KRB-NET-M1TEST");
            pingReq.Content = JsonBody(new { });
            var pingRes = await host.Client.SendAsync(pingReq);
            pingRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await host.Db.ExecuteAsync("UPDATE terminals SET is_authorized = 0 WHERE terminal_uid = @uid", new { uid = termUid });

        // Blocked terminal MUST receive 403 TERMINAL_UNAUTHORIZED on /api/settings/whatsapp-agent/logout
        using (var waLogoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/settings/whatsapp-agent/logout"))
        {
            waLogoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            waLogoutReq.Headers.TryAddWithoutValidation("X-Terminal-Id", termUid);
            waLogoutReq.Content = JsonBody(new { });
            var waLogoutRes = await host.Client.SendAsync(waLogoutReq);
            waLogoutRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var body = await waLogoutRes.Content.ReadAsStringAsync();
            body.Should().Contain("TERMINAL_UNAUTHORIZED");
        }

        // Exact handshake routes remain reachable by a blocked terminal
        using (var pingBlockedReq = new HttpRequestMessage(HttpMethod.Post, "/api/terminals/ping"))
        {
            pingBlockedReq.Headers.TryAddWithoutValidation("X-Terminal-Id", termUid);
            pingBlockedReq.Headers.TryAddWithoutValidation("X-Network-Token", "KRB-NET-M1TEST");
            pingBlockedReq.Content = JsonBody(new { });
            var pingBlockedRes = await host.Client.SendAsync(pingBlockedReq);
            pingBlockedRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var loginBlockedReq = new HttpRequestMessage(HttpMethod.Post, "/api/login"))
        {
            loginBlockedReq.Headers.TryAddWithoutValidation("X-Terminal-Id", termUid);
            loginBlockedReq.Content = JsonBody(new { email = "med1-admin@karobar.local", password = "wrong" });
            var loginBlockedRes = await host.Client.SendAsync(loginBlockedReq);
            loginBlockedRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var logoutBlockedReq = new HttpRequestMessage(HttpMethod.Post, "/api/logout"))
        {
            logoutBlockedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            logoutBlockedReq.Headers.TryAddWithoutValidation("X-Terminal-Id", termUid);
            logoutBlockedReq.Content = JsonBody(new { });
            var logoutBlockedRes = await host.Client.SendAsync(logoutBlockedReq);
            logoutBlockedRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task M2Low2_HasAvailableLicenseSeatAsync_FailsClosedOnException_And_DoesNotAutoAuthorizeTerminal()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_m2low2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app"));
        var dbPath = Path.Combine(tempRoot, "database", "database.sqlite");
        var brokenDbPath = Path.Combine(tempRoot, "database", "corrupt.sqlite");
        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        var prevAppRoot = Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");

        try
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tempRoot);
            Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", tempRoot);
            KarobarDb.FlushSchemaCache();

            await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: dbPath)).EnsureSchemaReadyAsync();
            var db = new KarobarDb(explicitSqlitePath: dbPath);
            await db.InsertAsync("settings", new Dictionary<string, object?>
            {
                ["store_name"] = "M2Low2 Store",
                ["terminal_auto_authorize"] = 1,
                ["network_server_token"] = "KRB-NET-M2LOW2"
            });

            // 1. Direct verification: exception inside HasAvailableLicenseSeatAsync must fail closed (false)
            var directSeatOk = await TrackTerminalMiddleware.HasAvailableLicenseSeatAsync(
                db,
                null!,
                "TERM-DIRECT-ERR",
                NullLogger<TrackTerminalMiddleware>.Instance);
            directSeatOk.Should().BeFalse("seat check exception must fail closed instead of auto-authorizing");

            // 2. End-to-end middleware verification: LicenseService throws during InvokeAsync -> new terminal inserted with is_authorized = 0
            await using (var brokenConn = new SqliteConnection(DatabaseBootstrapper.BuildSqliteConnectionString(brokenDbPath)))
            {
                await brokenConn.OpenAsync();
                await brokenConn.ExecuteAsync("CREATE TABLE licenses (dummy TEXT);");
            }
            var brokenDb = new KarobarDb(explicitSqlitePath: brokenDbPath);
            var brokenLicenseSvc = new LicenseService(brokenDb);
            brokenLicenseSvc.ClearPeriodicStatusCache();

            var nextCalled = false;
            var middleware = new TrackTerminalMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            var httpCtx = new DefaultHttpContext();
            httpCtx.Request.Method = HttpMethods.Get;
            httpCtx.Request.Path = "/api/sync/pulse";
            httpCtx.Request.Headers["X-Terminal-Id"] = "TERM-BROKEN-LIC-E2E";
            httpCtx.Request.Headers["X-Network-Token"] = "KRB-NET-M2LOW2";
            httpCtx.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(httpCtx, db, brokenLicenseSvc, NullLogger<TrackTerminalMiddleware>.Instance);

            nextCalled.Should().BeFalse();
            httpCtx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);

            var insertedTerminal = await db.QuerySingleOrDefaultAsync(
                "SELECT is_authorized FROM terminals WHERE terminal_uid = 'TERM-BROKEN-LIC-E2E'");
            insertedTerminal.Should().NotBeNull();
            Convert.ToInt32(insertedTerminal!["is_authorized"]).Should().Be(0);
        }
        finally
        {
            LicenseService.SetTestOverrides(null, null, null);
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", prevAppRoot);
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task M2Low1_MinimalApiRoutes_CarryAllowAnonymousMetadata_And_AllowExpiredUserToken()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"karobar_m2low1_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tempRoot, "database"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "public"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "backups"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "app", "shared-pdfs"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "storage", "logs"));
        Directory.CreateDirectory(Path.Combine(tempRoot, "public"));

        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        var prevAppRoot = Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");
        var prevAppUrl = Environment.GetEnvironmentVariable("APP_URL");
        var port = KestrelHostServer.FindFreePort(23800 + Random.Shared.Next(0, 400));
        var netCfg = new NetworkConfigManager(tempRoot, port, Path.Combine(tempRoot, "net-appdata"));
        await using var server = new KestrelHostServer(netCfg);

        try
        {
            await server.StartAsync(explicitPort: port, forceStart: true);
            server.Services.Should().NotBeNull();

            // 1. Inspect EndpointDataSource to confirm every minimal API route has IAllowAnonymous metadata
            var endpointSource = server.Services!.GetRequiredService<EndpointDataSource>();
            var minimalEndpoints = endpointSource.Endpoints.OfType<RouteEndpoint>().ToList();
            var expectedPatterns = new[]
            {
                "/up",
                "/__karobar_ipc/preload.js",
                "/__karobar_ipc/invoke",
                "/__karobar_ipc/{channel}",
                "/__karobar_ipc/events",
                "/__desktop/splash.html",
                "/__desktop/terminal-offline.html"
            };

            foreach (var pattern in expectedPatterns)
            {
                var matched = minimalEndpoints.FirstOrDefault(e =>
                    string.Equals(e.RoutePattern.RawText, pattern, StringComparison.OrdinalIgnoreCase));
                matched.Should().NotBeNull($"minimal API route '{pattern}' must be registered");
                matched!.Metadata.GetMetadata<IAllowAnonymous>().Should().NotBeNull(
                    $"minimal API route '{pattern}' must carry IAllowAnonymous metadata");
            }

            // 2. Seed a user whose remember_token_expires_at is expired and issue a Sanctum token
            var db = new KarobarDb(explicitSqlitePath: Path.Combine(tempRoot, "database", "database.sqlite"));
            var (_, expiredUserToken) = await SeedUserAsync(
                db,
                "Expired Token User",
                "expired-m2low1@karobar.local",
                "Pass123!",
                "Admin",
                rememberExpiresAt: DateTimeOffset.UtcNow.AddHours(-2));

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredUserToken);

            // Sanity check: protected API route still rejects expired user token with 401
            var apiUserRes = await client.GetAsync("/api/user");
            apiUserRes.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            // All minimal API routes MUST succeed even with the expired-user Bearer token
            (await client.GetAsync("/up")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/__karobar_ipc/preload.js")).StatusCode.Should().Be(HttpStatusCode.OK);
            client.DefaultRequestHeaders.Add("X-Karobar-Ipc-Token", server.IpcToken);
            (await client.PostAsync("/__karobar_ipc/invoke", JsonBody(new { channel = "karobar:app-info" }))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.PostAsync("/__karobar_ipc/karobar:app-info", JsonBody(new { }))).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/__karobar_ipc/events?since=0")).StatusCode.Should().Be(HttpStatusCode.OK);
            client.DefaultRequestHeaders.Remove("X-Karobar-Ipc-Token");
            (await client.GetAsync("/__desktop/splash.html")).StatusCode.Should().Be(HttpStatusCode.OK);
            (await client.GetAsync("/__desktop/terminal-offline.html")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
        finally
        {
            await server.StopAsync();
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", prevAppRoot);
            Environment.SetEnvironmentVariable("APP_URL", prevAppUrl);
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task M5Low_BearerTokenRotation_OnAllowAnonymousThrottledRoutes_StillHitsIpRateLimit429()
    {
        await using var host = await IsolatedM1Host.StartAsync(useCookies: true);

        // Seed 11 distinct valid user Bearer tokens to attempt rate-limit partition rotation
        var rotatedTokens = new List<string>(11);
        for (var i = 0; i < 11; i++)
        {
            var (_, tok) = await SeedUserAsync(
                host.Db,
                $"Rotator {i}",
                $"rotator-{i}@karobar.local",
                "Pass123!",
                "Admin");
            rotatedTokens.Add(tok);
        }

        // 1. POST /api/login ([AllowAnonymous]) with 11 different Bearer tokens -> 11th still hits 429
        for (var i = 0; i < 10; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/login");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedTokens[i]);
            req.Content = JsonBody(new { email = "missing@karobar.local", password = "wrong" });
            var res = await host.Client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        using (var req11 = new HttpRequestMessage(HttpMethod.Post, "/api/login"))
        {
            req11.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedTokens[10]);
            req11.Content = JsonBody(new { email = "missing@karobar.local", password = "wrong" });
            await AssertRateLimit429Async(await host.Client.SendAsync(req11), "POST /api/login (token rotation)");
        }

        // 2. POST /api/forgot-password ([AllowAnonymous]) with 11 different Bearer tokens -> 11th still hits 429
        for (var i = 0; i < 10; i++)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/forgot-password");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedTokens[i]);
            req.Content = JsonBody(new { email = "" });
            var res = await host.Client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        }
        using (var req11 = new HttpRequestMessage(HttpMethod.Post, "/api/forgot-password"))
        {
            req11.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedTokens[10]);
            req11.Content = JsonBody(new { email = "" });
            await AssertRateLimit429Async(await host.Client.SendAsync(req11), "POST /api/forgot-password (token rotation)");
        }

        // 3. POST /server-launch ([AllowAnonymous]) with 11 different Bearer tokens -> 11th still hits 429
        await File.WriteAllTextAsync(Path.Combine(host.TempRoot, "storage", "app", "server-launch.key"), "m5-launch-key");

        async Task<string> FetchLaunchCsrfForTokenAsync(string bearerToken)
        {
            using var getReq = new HttpRequestMessage(HttpMethod.Get, "/server-launch");
            getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            var getRes = await host.Client.SendAsync(getReq);
            getRes.StatusCode.Should().Be(HttpStatusCode.OK);
            var html = await getRes.Content.ReadAsStringAsync();
            var match = Regex.Match(html, @"<input[^>]+name=""__RequestVerificationToken""[^>]+value=""([^""]+)""");
            match.Success.Should().BeTrue();
            return WebUtility.HtmlDecode(match.Groups[1].Value);
        }

        for (var i = 0; i < 10; i++)
        {
            var csrf = await FetchLaunchCsrfForTokenAsync(rotatedTokens[i]);
            using var req = new HttpRequestMessage(HttpMethod.Post, "/server-launch");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedTokens[i]);
            req.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = csrf,
                ["key"] = "wrong-launch-key"
            });
            var res = await host.Client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        }
        {
            var csrf11 = await FetchLaunchCsrfForTokenAsync(rotatedTokens[10]);
            using var req11 = new HttpRequestMessage(HttpMethod.Post, "/server-launch");
            req11.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotatedTokens[10]);
            req11.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = csrf11,
                ["key"] = "wrong-launch-key"
            });
            await AssertRateLimit429Async(await host.Client.SendAsync(req11), "POST /server-launch (token rotation)");
        }
    }

    [Fact]
    public async Task M5Low_AllRemainingEightThrottledActions_Enforce429OverHttp()
    {
        await using var host = await IsolatedM1Host.StartAsync();
        var (_, adminToken) = await SeedUserAsync(host.Db, "M5 Admin", "m5-all@karobar.local", "AdminPass123!", "Admin");

        var routesToTest = new (string Path, object Payload, bool RequireAuth, HttpStatusCode ExpectedPreLimitStatus)[]
        {
            ("/api/verify-password", new { password = "" }, true, HttpStatusCode.UnprocessableEntity),
            ("/api/auth/confirm-password", new { password = "" }, true, HttpStatusCode.UnprocessableEntity),
            ("/api/forgot-password", new { email = "" }, false, HttpStatusCode.UnprocessableEntity),
            ("/api/reset-password", new { token = "", email = "" }, false, HttpStatusCode.UnprocessableEntity),
            ("/api/license/deactivate", new { password = "" }, true, HttpStatusCode.UnprocessableEntity),
            ("/api/backups/99999/restore", new { password = "" }, true, HttpStatusCode.UnprocessableEntity),
            ("/api/backups/upload/restore", new { }, true, HttpStatusCode.UnprocessableEntity),
            ("/api/settings/sqlite-import/commit", new { }, true, HttpStatusCode.Forbidden),
            ("/api/system/install-update", new { }, true, HttpStatusCode.ServiceUnavailable)
        };

        foreach (var (path, payload, requireAuth, expectedStatus) in routesToTest)
        {
            for (var i = 0; i < 10; i++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post, path);
                if (requireAuth)
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
                }
                req.Content = JsonBody(payload);
                var res = await host.Client.SendAsync(req);
                res.StatusCode.Should().Be(expectedStatus, $"{path} #{i + 1} must reach controller before hitting limit");
            }

            using var req11 = new HttpRequestMessage(HttpMethod.Post, path);
            if (requireAuth)
            {
                req11.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
            }
            req11.Content = JsonBody(payload);
            await AssertRateLimit429Async(await host.Client.SendAsync(req11), $"POST {path}");
        }
    }

    [Fact]
    public async Task MED3_DatabaseBootstrapper_HandlesMinimalDbWithoutMigrations_AndPreservesNonLocalAgentProvider()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"karobar_med3_min_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var minDbPath = Path.Combine(tempDir, "minimal.sqlite");

        try
        {
            await using (var conn = new SqliteConnection(DatabaseBootstrapper.BuildSqliteConnectionString(minDbPath)))
            {
                await conn.OpenAsync();
                await conn.ExecuteAsync(@"
                    CREATE TABLE settings (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        whatsapp_provider TEXT,
                        whatsapp_api_url TEXT
                    );
                    INSERT INTO settings (whatsapp_provider, whatsapp_api_url) VALUES (NULL, 'http://localhost:3001/');
                    INSERT INTO settings (whatsapp_provider, whatsapp_api_url) VALUES ('waha', 'http://127.0.0.1:3001');");
            }

            var bootstrapper = new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: minDbPath));
            await bootstrapper.EnsureSchemaReadyAsync();

            await using (var verifyConn = new SqliteConnection(DatabaseBootstrapper.BuildSqliteConnectionString(minDbPath)))
            {
                await verifyConn.OpenAsync();
                var urls = (await verifyConn.QueryAsync<string>("SELECT whatsapp_api_url FROM settings ORDER BY id ASC")).ToList();
                urls.Should().Equal("http://127.0.0.1:3002", "http://127.0.0.1:3001");
            }
        }
        finally
        {
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}

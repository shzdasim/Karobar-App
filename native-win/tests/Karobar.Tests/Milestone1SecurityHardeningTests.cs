using System.IO.Compression;
using System.Net;
using System.Net.Security;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Karobar.Api.Controllers;
using Karobar.Background;
using Karobar.Data;
using Karobar.Host;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Karobar.Tests;

public class Milestone1SecurityHardeningTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<Uri> RequestedUris { get; } = new();

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri != null)
            {
                RequestedUris.Add(request.RequestUri);
            }
            return Task.FromResult(_responder(request));
        }
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (Directory.Exists(Path.Combine(dir, "native-win", "src", "Karobar.Host")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir) ?? "";
        }
        return Directory.GetCurrentDirectory();
    }

    private static async Task<(KarobarDb Db, long AdminUserId)> SeedAdminDbAsync(string sqlitePath)
    {
        KarobarDb.FlushSchemaCache();
        await new DatabaseBootstrapper(new DbConnectionFactory(explicitSqlitePath: sqlitePath)).EnsureSchemaReadyAsync();
        KarobarDb.FlushSchemaCache();

        var db = new KarobarDb(explicitSqlitePath: sqlitePath);
        if (await db.QuerySingleOrDefaultAsync("SELECT id FROM roles WHERE name = 'Admin'") == null)
        {
            await db.InsertAsync("roles", new Dictionary<string, object?>
            {
                ["name"] = "Admin",
                ["guard_name"] = "sanctum"
            });
        }

        var uid = await db.InsertAsync("users", new Dictionary<string, object?>
        {
            ["name"] = "M1 Admin",
            ["email"] = $"m1-admin-{Guid.NewGuid():N}@karobar.local",
            ["password"] = "$2y$10$92IXUNpkjO0rOQ5byMi.Ye4oKoEa3Ro9llC/.og/at2.uheWG/igi",
            ["status"] = "active"
        });
        await db.SyncUserRolesAsync(uid, ["Admin"]);
        return (db, uid);
    }

    private static byte[] CreateValidPaddedPatchZip(int minBytes = 110 * 1024)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry("public/version.json", CompressionLevel.NoCompression);
            using var s = entry.Open();
            var content = Encoding.UTF8.GetBytes("{\"version\":\"2.0.99\"}\n" + new string(' ', minBytes));
            s.Write(content, 0, content.Length);
        }
        return ms.ToArray();
    }

    [Fact]
    public void ISS04_IsTrustedReleaseUrl_PinsOfficialRepoAndRejectsSpoofedOrTraversalUrls()
    {
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/Karobar-Native-Patch.zip")
            .Should().BeTrue();
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "https://github.com/shzdasim/Karobar-App/releases/latest/download/Karobar-Native-Setup.exe")
            .Should().BeTrue();
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "https://api.github.com/repos/shzdasim/Karobar-App/releases/tags/native-latest")
            .Should().BeTrue();

        // Untrusted owners, non-HTTPS, non-default ports, UserInfo, raw CDN hosts, and traversal paths must fail
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "https://github.com/evil/Karobar-App/releases/download/v1/x.zip").Should().BeFalse();
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "https://raw.githubusercontent.com/shzdasim/Karobar-App/main/x.zip").Should().BeFalse();
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "https://objects.githubusercontent.com/direct.zip").Should().BeFalse();
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "http://github.com/shzdasim/Karobar-App/releases/download/native-latest/x.zip").Should().BeFalse();
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "https://github.com:8443/shzdasim/Karobar-App/releases/download/native-latest/x.zip").Should().BeFalse();
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "https://user:pass@github.com/shzdasim/Karobar-App/releases/download/native-latest/x.zip").Should().BeFalse();
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "https://github.com/shzdasim/Karobar-App/releases/download/../../evil/x/releases/download/y.zip").Should().BeFalse();
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/%2E%2E/evil.zip").Should().BeFalse();
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/.%2e/evil.zip").Should().BeFalse();
        KarobarDesktopBridge.IsTrustedReleaseDownloadUrl(
            "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/%2E%2E/evil.zip").Should().BeFalse();
        SystemUpdateController.IsTrustedOfficialReleaseUrl(
            "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/%2E%2E/evil.zip").Should().BeFalse();
        SystemUpdateController.IsTrustedOfficialReleaseUrl(
            "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/Karobar-Native-Patch.zip").Should().BeTrue();
        KarobarDesktopBridge.IsTrustedReleaseUrl(
            "https://github.com/shzdasim/Karobar-App/archive/refs/heads/main.zip").Should().BeFalse();
    }

    [Fact]
    public async Task ISS04_DownloadAndInstallUpdateAsync_EnforcesPinnedManifestAndVerifiedRedirects()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), $"karobar_iss04_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tmpRoot, "public"));
        Directory.CreateDirectory(Path.Combine(tmpRoot, "storage", "app"));
        await File.WriteAllTextAsync(
            Path.Combine(tmpRoot, "public", "version.json"),
            """{"app_name":"Karobar-App Native","version":"2.0.48","build_number":48,"repository":"evil/fake-repo","runtime_layout":"split-v2","deps_fingerprint":"fp1"}""");

        var patchBytes = CreateValidPaddedPatchZip();
        var officialPatchSha = Convert.ToHexStringLower(SHA256.HashData(patchBytes));
        var downloadedTempPath = Path.Combine(Path.GetTempPath(), "Karobar-Native-Update-2.0.99.zip");
        if (File.Exists(downloadedTempPath)) File.Delete(downloadedTempPath);

        try
        {
            var manifestJson = $$"""
                {
                  "version": "2.0.99",
                  "build_number": 99,
                  "runtime_layout": "split-v2",
                  "deps_fingerprint": "fp1",
                  "download_url": "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/Karobar-Native-Setup.exe",
                  "installer_sha256": "{{new string('b', 64)}}",
                  "patch_url": "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/Karobar-Native-Patch.zip",
                  "patch_sha256": "{{officialPatchSha}}"
                }
                """;

            var cfg = new NetworkConfigManager(tmpRoot);
            var winMgr = new WebView2WindowManager(cfg);
            var bridge = new KarobarDesktopBridge(cfg, winMgr);

            var requested = new List<Uri>();
            bridge.ReleaseHttpHandlerFactory = () => new StubHttpMessageHandler(req =>
            {
                requested.Add(req.RequestUri!);
                if (req.RequestUri!.AbsoluteUri.Contains("latest-native-release.json", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
                    };
                }
                if (req.RequestUri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
                    req.RequestUri.AbsolutePath.EndsWith("Karobar-Native-Patch.zip", StringComparison.Ordinal))
                {
                    var redir = new HttpResponseMessage(HttpStatusCode.Found);
                    redir.Headers.Location = new Uri("https://objects.githubusercontent.com/github-production-release-asset/patch.zip");
                    return redir;
                }
                if (req.RequestUri.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new ByteArrayContent(patchBytes)
                    };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            // 1. Untrusted caller URL rejected immediately
            using (var badUrlDoc = JsonDocument.Parse($$"""
                {
                  "mode": "download",
                  "is_patch": true,
                  "patch_url": "https://github.com/attacker/evil/releases/download/v1/evil.zip",
                  "patch_sha256": "{{officialPatchSha}}"
                }
                """))
            {
                var res = await bridge.DownloadAndInstallUpdateAsync(badUrlDoc.RootElement.Clone());
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(res));
                doc.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
                doc.RootElement.GetProperty("message").GetString().Should().Contain("Untrusted release URL");
            }

            // 2. Caller SHA-256 mismatch against authoritative manifest rejected
            using (var mismatchShaDoc = JsonDocument.Parse($$"""
                {
                  "mode": "download",
                  "is_patch": true,
                  "patch_sha256": "{{new string('a', 64)}}"
                }
                """))
            {
                var res = await bridge.DownloadAndInstallUpdateAsync(mismatchShaDoc.RootElement.Clone());
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(res));
                doc.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
                doc.RootElement.GetProperty("message").GetString().Should().Contain("does not match the official release manifest");
            }

            // 3. Valid manifest + 302 redirect to objects.githubusercontent.com succeeds and ignores fake local.Repository
            using (var validDoc = JsonDocument.Parse($$"""
                {
                  "mode": "download",
                  "is_patch": true,
                  "patch_sha256": "{{officialPatchSha}}"
                }
                """))
            {
                var res = await bridge.DownloadAndInstallUpdateAsync(validDoc.RootElement.Clone());
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(res));
                doc.RootElement.GetProperty("ok").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("downloaded").GetBoolean().Should().BeTrue();
                doc.RootElement.GetProperty("is_patch").GetBoolean().Should().BeTrue();
            }

            requested.Should().Contain(u => u.Host == "github.com" && u.AbsolutePath.StartsWith("/shzdasim/Karobar-App/"));
            requested.Should().NotContain(u => u.AbsolutePath.Contains("evil/fake-repo"));

            // 4. Disallowed 302 redirect target (e.g. raw.githubusercontent.com) must be blocked
            if (File.Exists(downloadedTempPath)) File.Delete(downloadedTempPath);
            bridge.ReleaseHttpHandlerFactory = () => new StubHttpMessageHandler(req =>
            {
                if (req.RequestUri!.AbsoluteUri.Contains("latest-native-release.json", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(manifestJson, Encoding.UTF8, "application/json")
                    };
                }
                var badRedir = new HttpResponseMessage(HttpStatusCode.Found);
                badRedir.Headers.Location = new Uri("https://raw.githubusercontent.com/attacker/repo/main/evil.zip");
                return badRedir;
            });

            using (var validDoc = JsonDocument.Parse($$"""
                {
                  "mode": "download",
                  "is_patch": true,
                  "patch_sha256": "{{officialPatchSha}}"
                }
                """))
            {
                var res = await bridge.DownloadAndInstallUpdateAsync(validDoc.RootElement.Clone());
                using var doc = JsonDocument.Parse(JsonSerializer.Serialize(res));
                doc.RootElement.GetProperty("ok").GetBoolean().Should().BeFalse();
                doc.RootElement.GetProperty("message").GetString().Should().Contain("Blocked untrusted release redirect target");
            }
        }
        finally
        {
            try { if (File.Exists(downloadedTempPath)) File.Delete(downloadedTempPath); } catch { }
            try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ISS04_SystemUpdateController_RejectsUntrustedUrlsAndPinsRepository()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), $"karobar_iss04_ctrl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tmpRoot, "public"));
        Directory.CreateDirectory(Path.Combine(tmpRoot, "database"));
        await File.WriteAllTextAsync(
            Path.Combine(tmpRoot, "public", "version.json"),
            """{"app_name":"Karobar-App Native","version":"2.0.50","repository":"evil/attacker-repo"}""");

        var prevRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH");
        Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", tmpRoot);
        try
        {
            var dbPath = Path.Combine(tmpRoot, "database", "database.sqlite");
            var (db, adminUid) = await SeedAdminDbAsync(dbPath);
            var httpContext = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().BuildServiceProvider(),
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, adminUid.ToString()),
                    new Claim(ClaimTypes.Role, "Admin")
                ], "Sanctum"))
            };
            var payloadJson = """{"patch_url":"https://github.com/evil/repo/releases/download/v1/x.zip","patch_sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}""";
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(payloadJson));
            httpContext.Request.ContentType = "application/json";

            var controller = new SystemUpdateController(db)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };

            var res = await controller.InstallUpdate();
            var objRes = res.Should().BeOfType<ObjectResult>().Subject;
            objRes.StatusCode.Should().Be(422);

            var localMeta = controller.ReadLocalVersion();
            localMeta["repository"].Should().Be("shzdasim/Karobar-App");
        }
        finally
        {
            Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", prevRoot);
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ISS04_PatchZipExtraction_BlocksZipSlipAndZipBomb_AndPowerShellUsesSafeLoop()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), $"karobar_iss04_zip_{Guid.NewGuid():N}");
        var stageDir = Path.Combine(tmpRoot, "stage");
        Directory.CreateDirectory(tmpRoot);
        Directory.CreateDirectory(stageDir);

        try
        {
            // 1. Zip-Slip via ../ traversal
            var slipZip = Path.Combine(tmpRoot, "slip.zip");
            using (var archive = ZipFile.Open(slipZip, ZipArchiveMode.Create))
            {
                var e = archive.CreateEntry("../../pwned.txt");
                await using var s = e.Open();
                await s.WriteAsync(Encoding.UTF8.GetBytes("evil"));
            }
            var slipSha = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(slipZip)));

            Action actSlip = () => KarobarCliCommandRunner.ExtractPatchZipSafely(slipZip, stageDir);
            actSlip.Should().Throw<InvalidOperationException>().WithMessage("*traversal*");
            File.Exists(Path.Combine(tmpRoot, "pwned.txt")).Should().BeFalse();

            var runner = new KarobarCliCommandRunner(tmpRoot);
            var cliSlipRes = await runner.ExecuteApplyPatchAsync(new Dictionary<string, string?>
            {
                ["patch_zip"] = slipZip,
                ["verify-sha"] = slipSha
            });
            cliSlipRes.ExitCode.Should().Be(1);

            // 2. Zip-Slip via rooted / drive-qualified path
            var rootedZip = Path.Combine(tmpRoot, "rooted.zip");
            using (var archive = ZipFile.Open(rootedZip, ZipArchiveMode.Create))
            {
                var e = archive.CreateEntry("C:\\Windows\\evil.dll");
                await using var s = e.Open();
                await s.WriteAsync(Encoding.UTF8.GetBytes("evil"));
            }
            Action actRooted = () => KarobarCliCommandRunner.ExtractPatchZipSafely(rootedZip, stageDir);
            actRooted.Should().Throw<InvalidOperationException>().WithMessage("*unsafe rooted path*");

            // 3. Zip-Bomb byte limit enforcement
            var bombZip = Path.Combine(tmpRoot, "bomb.zip");
            using (var archive = ZipFile.Open(bombZip, ZipArchiveMode.Create))
            {
                var e = archive.CreateEntry("public/big.txt", CompressionLevel.Optimal);
                await using var s = e.Open();
                var payload = Encoding.ASCII.GetBytes(new string('A', 64 * 1024));
                await s.WriteAsync(payload);
            }
            Action actBomb = () => KarobarCliCommandRunner.ExtractPatchZipSafely(
                bombZip,
                stageDir,
                maxTotalDecompressedBytes: 4096);
            actBomb.Should().Throw<InvalidOperationException>().WithMessage("*exceeded*");

            // 4. Max entry count enforcement
            var manyZip = Path.Combine(tmpRoot, "many.zip");
            using (var archive = ZipFile.Open(manyZip, ZipArchiveMode.Create))
            {
                for (var i = 0; i < 6; i++)
                {
                    var e = archive.CreateEntry($"public/f{i}.txt");
                    await using var s = e.Open();
                    await s.WriteAsync("x"u8.ToArray());
                }
            }
            Action actMany = () => KarobarCliCommandRunner.ExtractPatchZipSafely(manyZip, stageDir, maxEntries: 5);
            actMany.Should().Throw<InvalidOperationException>().WithMessage("*maximum allowed entry count*");

            // 5. PowerShell OTA swap in KarobarDesktopBridge.cs uses streaming ZipFile::OpenRead + GetFullPath + bounds
            var bridgePath = Path.Combine(FindRepoRoot(), "native-win", "src", "Karobar.Host", "KarobarDesktopBridge.cs");
            var bridgeSrc = await File.ReadAllTextAsync(bridgePath);
            bridgeSrc.Should().Contain("[System.IO.Compression.ZipFile]::OpenRead($zipPath)");
            bridgeSrc.Should().Contain("$maxEntries = 5000");
            bridgeSrc.Should().Contain("$maxTotalBytes");
            bridgeSrc.Should().Contain("[System.IO.Path]::GetFullPath");
            bridgeSrc.Should().Contain("FileAttributes]::ReparsePoint");
            bridgeSrc.Should().NotContain("ZipFile]::ExtractToDirectory");
        }
        finally
        {
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public void ISS05_PreloadScript_ScopesIpcTokenAndEnforcesOriginAndOffscreenIsolation()
    {
        const string fakeToken = "0123456789abcdef0123456789abcdef";
        var normalJs = KarobarPreloadScript.GetPreloadScript(
            isOffscreenPrintWorker: false,
            ipcToken: fakeToken,
            trustedOrigin: "http://127.0.0.1:18960");

        normalJs.Should().Contain("const __ipcToken = ");
        normalJs.Should().Contain("const __trustedPort = \"18960\";");
        normalJs.Should().Contain("__isLoopbackPortTrusted");
        normalJs.Should().Contain("__effectivePort === __trustedPort");
        normalJs.Should().Contain("\"X-Karobar-Ipc-Token\": __ipcToken");
        normalJs.Should().Contain("__isLoopbackOrigin");
        normalJs.Should().Contain("__isTerminalOfflineFile");
        normalJs.Should().NotContain("window.karobarIpcToken");
        normalJs.Should().Contain("window.karobarDesktop");

        var defaultJs = KarobarPreloadScript.GetPreloadScript(false);
        defaultJs.Should().Contain("const __trustedPort = \"\";");
        defaultJs.Should().Contain("window.karobarDesktop");

        var offscreenJs = KarobarPreloadScript.GetPreloadScript(
            isOffscreenPrintWorker: true,
            ipcToken: fakeToken,
            trustedOrigin: "http://127.0.0.1:18960");

        offscreenJs.Should().Contain("__karobarSilentPrintWorker = true");
        offscreenJs.Should().NotContain(fakeToken);
        offscreenJs.Should().NotContain("window.karobarDesktop");
        offscreenJs.Should().NotContain("/__karobar_ipc/invoke");
    }

    [Fact]
    public void ISS05_KestrelHostServer_ValidatesIpcTokenOriginAndContentType()
    {
        var tmpRoot = Path.Combine(Path.GetTempPath(), $"karobar_iss05_ipc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpRoot);
        try
        {
            var cfg = new NetworkConfigManager(tmpRoot, activePort: 18960);
            var server = new KestrelHostServer(cfg);
            server.IpcToken.Should().HaveLength(64);

            DefaultHttpContext MakeIpcCtx(
                IPAddress? remoteIp = null,
                string? token = null,
                string? origin = null,
                string? secFetchSite = null,
                string? contentType = "application/json")
            {
                var ctx = new DefaultHttpContext();
                ctx.Connection.RemoteIpAddress = remoteIp ?? IPAddress.Loopback;
                if (token != null) ctx.Request.Headers["X-Karobar-Ipc-Token"] = token;
                if (origin != null) ctx.Request.Headers.Origin = origin;
                if (secFetchSite != null) ctx.Request.Headers["Sec-Fetch-Site"] = secFetchSite;
                if (contentType != null) ctx.Request.ContentType = contentType;
                return ctx;
            }

            server.ValidateIpcRequest(MakeIpcCtx(token: server.IpcToken, origin: "http://127.0.0.1:18960"), true, out var okStatus, out _)
                .Should().BeTrue();
            okStatus.Should().Be(200);

            server.ValidateIpcRequest(MakeIpcCtx(token: null), true, out var missingTokStatus, out _)
                .Should().BeFalse();
            missingTokStatus.Should().Be(403);

            server.ValidateIpcRequest(MakeIpcCtx(token: "wrong-token"), true, out var badTokStatus, out _)
                .Should().BeFalse();
            badTokStatus.Should().Be(403);

            server.ValidateIpcRequest(MakeIpcCtx(token: server.IpcToken, origin: "https://evil.example"), true, out var badOriginStatus, out _)
                .Should().BeFalse();
            badOriginStatus.Should().Be(403);

            server.ValidateIpcRequest(MakeIpcCtx(token: server.IpcToken, origin: "http://127.0.0.1:18961"), true, out var badPortStatus, out _)
                .Should().BeFalse();
            badPortStatus.Should().Be(403);

            server.ValidateIpcRequest(MakeIpcCtx(token: server.IpcToken, secFetchSite: "cross-site"), true, out var crossSiteStatus, out _)
                .Should().BeFalse();
            crossSiteStatus.Should().Be(403);

            server.ValidateIpcRequest(MakeIpcCtx(remoteIp: IPAddress.Parse("192.168.1.55"), token: server.IpcToken), true, out var lanStatus, out _)
                .Should().BeFalse();
            lanStatus.Should().Be(403);

            server.ValidateIpcRequest(MakeIpcCtx(token: server.IpcToken, contentType: "text/plain"), true, out var ctStatus, out _)
                .Should().BeFalse();
            ctStatus.Should().Be(415);

            // Preload endpoint rejects same-site, cross-site, untrusted referer, and non-loopback clients
            DefaultHttpContext MakePreloadCtx(
                IPAddress? remoteIp = null,
                string? secFetchSite = null,
                string? referer = null)
            {
                var ctx = new DefaultHttpContext();
                ctx.Connection.RemoteIpAddress = remoteIp ?? IPAddress.Loopback;
                if (secFetchSite != null) ctx.Request.Headers["Sec-Fetch-Site"] = secFetchSite;
                if (referer != null) ctx.Request.Headers.Referer = referer;
                return ctx;
            }

            server.ValidatePreloadRequest(MakePreloadCtx(secFetchSite: "same-origin", referer: "http://127.0.0.1:18960/dashboard"), out var preloadOkStatus, out _)
                .Should().BeTrue();
            preloadOkStatus.Should().Be(200);

            server.ValidatePreloadRequest(MakePreloadCtx(secFetchSite: "none", referer: "http://localhost:18960/dashboard"), out var preloadNoneStatus, out _)
                .Should().BeTrue();
            preloadNoneStatus.Should().Be(200);

            server.ValidatePreloadRequest(MakePreloadCtx(secFetchSite: "same-site"), out var preloadSameSiteStatus, out _)
                .Should().BeFalse();
            preloadSameSiteStatus.Should().Be(403);

            server.ValidatePreloadRequest(MakePreloadCtx(secFetchSite: "cross-site"), out var preloadCrossSiteStatus, out _)
                .Should().BeFalse();
            preloadCrossSiteStatus.Should().Be(403);

            server.ValidatePreloadRequest(MakePreloadCtx(referer: "http://127.0.0.1:9999/evil"), out var preloadBadRefStatus, out _)
                .Should().BeFalse();
            preloadBadRefStatus.Should().Be(403);

            server.ValidatePreloadRequest(MakePreloadCtx(remoteIp: IPAddress.Parse("192.168.1.55")), out var preloadLanStatus, out _)
                .Should().BeFalse();
            preloadLanStatus.Should().Be(403);
        }
        finally
        {
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ISS07_FbrAllowlistAndTlsRemoval_BlocksSsrfAndRemoteBypass()
    {
        // 1. Endpoint URL allowlist checks
        FbrController.IsAllowedFbrEndpointUrl("https://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata", "digital_invoicing", out _, out _)
            .Should().BeTrue();
        FbrController.IsAllowedFbrEndpointUrl("https://esp.fbr.gov.pk:8244/FBR/v1/api/Live/PostData", "tier1_pos_cloud", out _, out _)
            .Should().BeTrue();
        FbrController.IsAllowedFbrEndpointUrl("http://127.0.0.1:8524/api/IMSFiscal/GetInvoiceNumberByModel", "tier1_pos_ims", out _, out _)
            .Should().BeTrue();

        FbrController.IsAllowedFbrEndpointUrl("http://169.254.169.254/latest/meta-data/", "digital_invoicing", out _, out _)
            .Should().BeFalse();
        FbrController.IsAllowedFbrEndpointUrl("https://fbr.gov.pk.evil.com/post", "digital_invoicing", out _, out _)
            .Should().BeFalse();
        FbrController.IsAllowedFbrEndpointUrl("http://gw.fbr.gov.pk/di_data/v1/di/postinvoicedata", "digital_invoicing", out _, out _)
            .Should().BeFalse();
        FbrController.IsAllowedFbrEndpointUrl("https://gw.fbr.gov.pk:9999/di_data/v1/di/postinvoicedata", "digital_invoicing", out _, out _)
            .Should().BeFalse();
        FbrController.IsAllowedFbrEndpointUrl("http://192.168.1.20:8524/api/IMSFiscal", "tier1_pos_ims", out _, out _)
            .Should().BeFalse();

        // 2. FbrController.TestConnection rejects SSRF override URL with HTTP 422 before making outbound calls
        var tmpRoot = Path.Combine(Path.GetTempPath(), $"karobar_iss07_fbr_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tmpRoot, "database"));
        try
        {
            var dbPath = Path.Combine(tmpRoot, "database", "database.sqlite");
            var (db, adminUid) = await SeedAdminDbAsync(dbPath);
            var httpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, adminUid.ToString()),
                    new Claim(ClaimTypes.Role, "Admin")
                ], "Sanctum"))
            };
            var ssrfPayload = """
                {
                  "fbr_mode": "digital_invoicing",
                  "fbr_environment": "production",
                  "fbr_bearer_token": "secret-fbr-token",
                  "fbr_seller_ntn": "1234567",
                  "fbr_production_url": "https://attacker.example/steal-token"
                }
                """;
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(ssrfPayload));
            httpContext.Request.ContentType = "application/json";

            var controller = new FbrController(db)
            {
                ControllerContext = new ControllerContext { HttpContext = httpContext }
            };

            var res = await controller.TestConnection();
            var objRes = res.Should().BeOfType<ObjectResult>().Subject;
            objRes.StatusCode.Should().Be(422);
            JsonSerializer.Serialize(objRes.Value).Should().Contain("invalid_url");

            // 3. Static check confirming unconditional TLS callback is gone from FbrController.cs
            var fbrCtrlPath = Path.Combine(FindRepoRoot(), "native-win", "src", "Karobar.Api", "Controllers", "FbrController.cs");
            var fbrSrc = await File.ReadAllTextAsync(fbrCtrlPath);
            fbrSrc.Should().NotContain("(_, _, _, _) => true");
        }
        finally
        {
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task ISS07_NetworkConfig_EnforcesTlsPolicyAndExactOriginEquality()
    {
        // 1. Exact URI origin matching
        NetworkConfigManager.IsSameOrigin("http://127.0.0.1:18960/api/user", "http://127.0.0.1:18960").Should().BeTrue();
        NetworkConfigManager.IsSameOrigin("http://127.0.0.1:18960/api/user", "http://127.0.0.1:18960/dashboard").Should().BeTrue();
        NetworkConfigManager.IsSameOrigin("http://127.0.0.1:189601/api/user", "http://127.0.0.1:18960").Should().BeFalse();
        NetworkConfigManager.IsSameOrigin("http://127.0.0.1:18960.evil.com/api/user", "http://127.0.0.1:18960").Should().BeFalse();
        NetworkConfigManager.IsSameOrigin("http://user@127.0.0.1:18960/api/user", "http://127.0.0.1:18960").Should().BeFalse();
        NetworkConfigManager.IsSameOrigin("https://127.0.0.1:18960/api/user", "http://127.0.0.1:18960").Should().BeFalse();

        // 2. Internal route extraction rejects domain/port prefix spoofing
        WebView2WindowManager.TryExtractInternalAppRoute(
            "http://127.0.0.1:18960/sale-invoices?id=5",
            "http://127.0.0.1:18960",
            18960,
            out var internalRoute).Should().BeTrue();
        internalRoute.Should().Be("/sale-invoices?id=5");

        WebView2WindowManager.TryExtractInternalAppRoute(
            "http://127.0.0.1:18960.evil.com/phish",
            "http://127.0.0.1:18960",
            18960,
            out _).Should().BeFalse();

        WebView2WindowManager.TryExtractInternalAppRoute(
            "//evil.com/redirect",
            "http://127.0.0.1:18960",
            18960,
            out _).Should().BeFalse();

        // 3. TLS validation: public hosts fail on TLS errors; LAN/loopback allow self-signed; thumbprint pinning works
        const string testCertDerBase64 =
            "MIICETCCAbYCCQDEMFO84Gms4TAKBggqhkjOPQQDAjAWMRQwEgYDVQQDDAtLYXJvYmFyLUxBTjAeFw0yNjExMjMwNTUzMDZaFw0yNzExMjMwNTUzMDZaMBYxFDASBgNVBAMMC0thcm9iYXItTEFOMIIBSzCCAQMGByqGSM49AgEwgfcCAQEwLAYHKoZIzj0BAQIhAP////8AAAABAAAAAAAAAAAAAAAA////////////////MFsEIP////8AAAABAAAAAAAAAAAAAAAA///////////////8BCBaxjXYqjqT57PrvVV2mIa8ZR0GsMxTsPY7zjw+J9JgSwMVAMSdNgiG5wSTamZ44ROdJreBn36QBEEEaxfR8uEsQkf4vOblY6RA8ncDfYEt6zOg9KE5RdiYwpZP40Li/hp/m47n60p8D54WK84zV2sxXs7LtkBoN79R9QIhAP////8AAAAA//////////+85vqtpxeehPO5ysL8YyVRAgEBA0IABMU/SjEzoe6/R5xOjcw3RWkARMAAF6OGMaVXiNwx6Q09uBx9IPeus7b8lGMsRCqzz08XF0RDoNVBrjtm5ZoUpFkwCgYIKoZIzj0EAwIDSQAwRgIhAJjAgdDJA5zHOGC+u8+cv/pXg7nmDtB1bYQZ54i7M+bBAiEA4eY+NCiNyREFL2Hhn4WHE0hqp9y64k0i9xQgTVPSqo4=";
        using var selfSigned = X509CertificateLoader.LoadCertificate(Convert.FromBase64String(testCertDerBase64));
        var sha256Pin = selfSigned.GetCertHashString(HashAlgorithmName.SHA256);

        var publicReq = new HttpRequestMessage(HttpMethod.Post, "https://public.example.com/api/terminals/ping");
        var lanReq = new HttpRequestMessage(HttpMethod.Post, "https://192.168.1.10:18960/api/terminals/ping");

        NetworkConfigManager.ValidateServerCertificate(publicReq, selfSigned, SslPolicyErrors.None)
            .Should().BeTrue();
        NetworkConfigManager.ValidateServerCertificate(publicReq, selfSigned, SslPolicyErrors.RemoteCertificateChainErrors)
            .Should().BeFalse();
        NetworkConfigManager.ValidateServerCertificate(lanReq, selfSigned, SslPolicyErrors.RemoteCertificateChainErrors)
            .Should().BeTrue();
        NetworkConfigManager.ValidateServerCertificate(publicReq, selfSigned, SslPolicyErrors.RemoteCertificateChainErrors, sha256Pin)
            .Should().BeTrue();
        NetworkConfigManager.ValidateServerCertificate(publicReq, selfSigned, SslPolicyErrors.None, new string('F', 64))
            .Should().BeFalse();

        // Cloud IMDS 169.254.169.254 must never be treated as trusted private/LAN host
        NetworkConfigManager.IsPrivateOrLoopbackHost("169.254.169.254").Should().BeFalse();
        NetworkConfigManager.IsPrivateOrLoopbackHost("::ffff:169.254.169.254").Should().BeFalse();
        NetworkConfigManager.IsPrivateOrLoopbackHost("169.254.10.20").Should().BeTrue();
        var imdsReq = new HttpRequestMessage(HttpMethod.Get, "https://169.254.169.254/latest/meta-data/");
        NetworkConfigManager.ValidateServerCertificate(imdsReq, selfSigned, SslPolicyErrors.RemoteCertificateChainErrors)
            .Should().BeFalse();

        // 4. Static checks confirming NativeWebView2Form.cs and OffscreenPrintWorker.cs use IsSameOrigin
        var repoRoot = FindRepoRoot();
        var formSrc = await File.ReadAllTextAsync(Path.Combine(repoRoot, "native-win", "src", "Karobar.Host", "NativeWebView2Form.cs"));
        formSrc.Should().Contain("NetworkConfigManager.IsSameOrigin(reqUri, _baseOrigin)");
        formSrc.Should().NotContain("reqUri.StartsWith(_baseOrigin");

        var workerSrc = await File.ReadAllTextAsync(Path.Combine(repoRoot, "native-win", "src", "Karobar.Host", "OffscreenPrintWorker.cs"));
        workerSrc.Should().Contain("NetworkConfigManager.IsSameOrigin(reqUri, appBaseUrl)");
        workerSrc.Should().NotContain("reqUri.StartsWith(appBaseUrl");
    }

    [Fact]
    public void ISS09_SecurityHeadersCspAndMethodOverrideBounds_AreEnforced()
    {
        var repoRoot = FindRepoRoot();
        var tmpRoot = Path.Combine(Path.GetTempPath(), $"karobar_iss09_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(tmpRoot, "public"));
        Directory.CreateDirectory(Path.Combine(tmpRoot, "desktop"));

        var realIndexHtmlPath = Path.Combine(repoRoot, "public", "index.html");
        File.Copy(realIndexHtmlPath, Path.Combine(tmpRoot, "public", "index.html"), overwrite: true);

        try
        {
            var cfg = new NetworkConfigManager(tmpRoot, activePort: 18960);
            var server = new KestrelHostServer(cfg);

            // 1. SPA CSP hashes public/index.html inline script, allows api.github.com in connect-src, and forbids 'unsafe-inline' in script-src
            var spaCsp = server.GetContentSecurityPolicyHeader(allowLegacyUnsafeInline: false);
            spaCsp.Should().Contain("script-src 'self' 'sha256-");
            spaCsp.Should().Contain("connect-src 'self' https://api.github.com");
            var scriptSrcSegment = spaCsp.Split(';')
                .Select(s => s.Trim())
                .First(s => s.StartsWith("script-src ", StringComparison.Ordinal));
            scriptSrcSegment.Should().NotContain("'unsafe-inline'");
            spaCsp.Should().NotContain("script-src 'self' 'unsafe-inline'");
            spaCsp.Should().NotContain("'unsafe-eval'");

            // Inline script SHA-256 hashing normalizes CRLF and CR to LF per HTML5 tokenization
            var lfHashes = KestrelHostServer.ExtractInlineScriptHashes("<script>console.log(1);\nconsole.log(2);</script>");
            var crlfHashes = KestrelHostServer.ExtractInlineScriptHashes("<script>console.log(1);\r\nconsole.log(2);</script>");
            var crHashes = KestrelHostServer.ExtractInlineScriptHashes("<script>console.log(1);\rconsole.log(2);</script>");
            crlfHashes.Should().Equal(lfHashes);
            crHashes.Should().Equal(lfHashes);

            var legacyCsp = server.GetContentSecurityPolicyHeader(allowLegacyUnsafeInline: true);
            legacyCsp.Should().Contain("script-src 'self' 'unsafe-inline'");

            KestrelHostServer.IsLegacyInlineHtmlPath("/print/sale-invoice/1").Should().BeTrue();
            KestrelHostServer.IsLegacyInlineHtmlPath("/sale-invoices/42/dosage-labels").Should().BeTrue();
            KestrelHostServer.IsLegacyInlineHtmlPath("/server-launch").Should().BeTrue();
            KestrelHostServer.IsLegacyInlineHtmlPath("/dashboard").Should().BeFalse();

            var headerCtx = new DefaultHttpContext();
            headerCtx.Request.Path = "/dashboard";
            server.ApplySecurityHeaders(headerCtx);
            headerCtx.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
            headerCtx.Response.Headers["X-Frame-Options"].ToString().Should().Be("SAMEORIGIN");
            headerCtx.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
            headerCtx.Response.Headers["Permissions-Policy"].ToString().Should().Contain("camera=()");
            headerCtx.Response.Headers["Content-Security-Policy"].ToString().Should().Contain("'sha256-");

            // 2. Method override form buffering refuses unauthenticated, non-/api, or >10MB bodies
            DefaultHttpContext MakePostFormCtx(string path, string? authHeader, long? contentLength)
            {
                var ctx = new DefaultHttpContext();
                ctx.Request.Method = "POST";
                ctx.Request.Path = path;
                ctx.Request.ContentType = "multipart/form-data; boundary=---abc";
                ctx.Request.ContentLength = contentLength;
                if (authHeader != null)
                {
                    ctx.Request.Headers.Authorization = authHeader;
                }
                return ctx;
            }

            KestrelHostServer.ShouldInspectFormForMethodOverride(
                MakePostFormCtx("/api/brands/1", null, 1024)).Should().BeFalse();
            KestrelHostServer.ShouldInspectFormForMethodOverride(
                MakePostFormCtx("/up", "Bearer tok123", 1024)).Should().BeFalse();
            KestrelHostServer.ShouldInspectFormForMethodOverride(
                MakePostFormCtx("/api/brands/1", "Bearer tok123", 11L * 1024 * 1024)).Should().BeFalse();
            KestrelHostServer.ShouldInspectFormForMethodOverride(
                MakePostFormCtx("/api/brands/1", "Bearer tok123", 2048)).Should().BeTrue();
        }
        finally
        {
            KarobarDb.FlushSchemaCache();
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, recursive: true); } catch { }
        }
    }
}

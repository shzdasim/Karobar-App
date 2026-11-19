using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Karobar.Host;

public sealed class CachedStaticAsset
{
    public required string ContentType { get; init; }
    public required string CacheControl { get; init; }
    public required byte[] Buffer { get; init; }
}

/// <summary>
/// In-process Kestrel web server replacing the Node.js front gateway + PHP worker pool in desktop/main.cjs
/// and desktop/router.php.
/// - GET /up health endpoint returning 200 OK
/// - High-speed static asset serving for public/build/* (with 50MB immutable in-memory buffer cache)
///   and storage/app/public/* (/storage/*)
/// - SPA shell serving (resources/views/index.blade.php resolved with Vite manifest asset tags
///   and KarobarPreloadScript) for all 103 React routes.
/// </summary>
public sealed class KestrelHostServer : IAsyncDisposable
{
    public const long MaxStaticAssetCacheBytes = 50L * 1024L * 1024L;
    public const long MaxSingleCachedAssetBytes = 2L * 1024L * 1024L;

    public static readonly IReadOnlyDictionary<string, string> StaticMimeTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".js"] = "application/javascript; charset=utf-8",
            [".mjs"] = "application/javascript; charset=utf-8",
            [".css"] = "text/css; charset=utf-8",
            [".json"] = "application/json; charset=utf-8",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".webp"] = "image/webp",
            [".gif"] = "image/gif",
            [".svg"] = "image/svg+xml",
            [".ico"] = "image/x-icon",
            [".woff"] = "font/woff",
            [".woff2"] = "font/woff2",
            [".ttf"] = "font/ttf",
            [".eot"] = "application/vnd.ms-fontobject",
            [".pdf"] = "application/pdf",
            [".txt"] = "text/plain; charset=utf-8",
            [".html"] = "text/html; charset=utf-8"
        };

    /// <summary>
    /// Complete registry of all 103 React SPA routes across resources/js/routes/index.jsx and desktop menus.
    /// </summary>
    public static readonly IReadOnlyList<string> All103SpaRoutes =
    [
        "/",
        "/login",
        "/forgot-password",
        "/reset-password/:token",
        "/reset-password/:token/:email",
        "/windows-preview",
        "/theme-windows-preview",
        "/setup",
        "/activate",
        "/dashboard",
        "/profile",
        "/background-tasks",
        "/suppliers",
        "/suppliers/create",
        "/suppliers/:id/edit",
        "/suppliers/:id",
        "/customers",
        "/customers/create",
        "/customers/:id/edit",
        "/customers/:id",
        "/doctors",
        "/doctors/create",
        "/doctors/:id/edit",
        "/doctors/:id",
        "/categories",
        "/categories/create",
        "/categories/:id/edit",
        "/brands",
        "/brands/create",
        "/brands/:id/edit",
        "/formulations",
        "/storage-locations",
        "/products",
        "/products/create",
        "/products/:id/edit",
        "/products/:id",
        "/product-batches",
        "/batch-expiry",
        "/barcode-labels",
        "/purchase-invoices",
        "/purchase-invoices/create",
        "/purchase-invoices/:id/edit",
        "/purchase-invoices/:id",
        "/purchase-returns",
        "/purchase-returns/create",
        "/purchase-returns/:id/edit",
        "/purchase-returns/:id",
        "/purchase-orders",
        "/purchase-orders/create",
        "/purchase-orders/forecast",
        "/purchase-orders/:id",
        "/purchase-orders/:id/edit",
        "/smart-reorder",
        "/user-demands",
        "/user-demands/create",
        "/user-demands/:id/edit",
        "/sale-invoices",
        "/sale-invoices/pos",
        "/sale-invoices/barcode-pos",
        "/sale-invoices/create/retail",
        "/sale-invoices/create-retail",
        "/sale-invoices/create/wholesale",
        "/sale-invoices/create-wholesale",
        "/sale-invoices/:id/edit",
        "/sale-invoices/:id",
        "/wholesale-invoices",
        "/wholesale-invoices/create",
        "/wholesale-invoices/:id",
        "/wholesale-invoices/:id/edit",
        "/trade-schemes",
        "/trade-schemes/create",
        "/trade-schemes/:id/edit",
        "/sale-returns",
        "/sale-returns/create",
        "/sale-returns/:id/edit",
        "/sale-returns/:id",
        "/stock-adjustments",
        "/stock-adjustments/create",
        "/stock-adjustments/:id",
        "/stocktake-sessions",
        "/stocktake-sessions/create",
        "/stocktake-sessions/:id",
        "/stocktake-sessions/:id/review",
        "/stock-disposals",
        "/stock-disposals/create",
        "/stock-disposals/:id",
        "/accounts",
        "/accounting",
        "/hr-payroll",
        "/payments",
        "/party-credits",
        "/cash-drawer",
        "/supplier-ledger",
        "/customer-ledger",
        "/expenses",
        "/reports/cost-of-sale",
        "/reports/cost-of-sale-detail",
        "/reports/purchase-detail",
        "/reports/sale-detail",
        "/reports/current-stock",
        "/reports/stock-adjustment",
        "/reports/product-comprehensive",
        "/reports/near-expiry-product",
        "/reports/purchase-returns",
        "/reports/sale-returns",
        "/reports/purchase-summary",
        "/reports/outstanding-aging",
        "/reports/reorder",
        "/reports/net-profit",
        "/reports/tax-return",
        "/reports/narcotics",
        "/settings",
        "/settings/sqlite-import",
        "/sqlite-import",
        "/server-dashboard",
        "/terminals",
        "/backups",
        "/users",
        "/users/create",
        "/users/:id/edit",
        "/roles",
        "/roles/create",
        "/roles/:id/edit"
    ];

    private static readonly Regex NonSpaRoutePrefixRegex = new(
        @"^\/((api|sanctum|storage|up|print|password\/reset|supplier-ledger\/print|customer-ledger\/print|terms-of-service|privacy-policy|server-launch|__karobar_ipc|__desktop)(\/|$)|sale-invoices\/\d+\/dosage-labels(\/|$))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly NetworkConfigManager _networkConfig;
    private readonly WebView2WindowManager _windowManager;
    private readonly KarobarDesktopBridge _desktopBridge;
    private readonly ConcurrentDictionary<string, CachedStaticAsset> _staticAssetCache = new(StringComparer.OrdinalIgnoreCase);

    private long _staticAssetCacheBytes;
    private byte[]? _cachedSpaHtmlBuffer;
    private WebApplication? _app;

    public KestrelHostServer(
        NetworkConfigManager? networkConfig = null,
        WebView2WindowManager? windowManager = null,
        KarobarDesktopBridge? desktopBridge = null)
    {
        _networkConfig = networkConfig ?? new NetworkConfigManager();
        _windowManager = windowManager ?? new WebView2WindowManager(_networkConfig);
        _desktopBridge = desktopBridge ?? new KarobarDesktopBridge(_networkConfig, _windowManager, () => _app?.Services);

        _desktopBridge.OnStartBackendServices = async () => await StartAsync();
        _desktopBridge.OnStopBackendServices = async () => await StopAsync();
    }

    public NetworkConfigManager NetworkConfig => _networkConfig;
    public WebView2WindowManager WindowManager => _windowManager;
    public KarobarDesktopBridge DesktopBridge => _desktopBridge;
    public int ActivePort => _networkConfig.ActivePort;
    public string PublicDir => Path.Combine(_networkConfig.AppRoot, "public");
    public string StoragePublicDir => Path.Combine(_networkConfig.AppRoot, "storage", "app", "public");
    public bool IsRunning => _app != null;
    public IServiceProvider? Services => _app?.Services;

    /// <summary>
    /// Find an available localhost TCP port starting from preferredPort (default 18960).
    /// </summary>
    public static int FindFreePort(int preferredPort = NetworkConfigManager.DefaultPort)
    {
        for (var port = preferredPort; port < preferredPort + 500; port++)
        {
            try
            {
                using var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                listener.Stop();
                return port;
            }
            catch (SocketException)
            {
                // Port in use, try next
            }
        }

        using var fallback = new TcpListener(IPAddress.Loopback, 0);
        fallback.Start();
        var assigned = ((IPEndPoint)fallback.LocalEndpoint).Port;
        fallback.Stop();
        return assigned;
    }

    public static bool IsSpaShellRequest(string method, string? rawUrl, string? acceptHeader = null)
    {
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var url = string.IsNullOrWhiteSpace(rawUrl) ? "/" : rawUrl;
        var qIndex = url.IndexOf('?');
        var rawPath = qIndex >= 0 ? url[..qIndex] : url;

        if (string.IsNullOrWhiteSpace(rawPath) || rawPath.EndsWith(".php", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (NonSpaRoutePrefixRegex.IsMatch(rawPath))
        {
            return false;
        }

        var ext = Path.GetExtension(rawPath);
        if (!string.IsNullOrEmpty(ext) && StaticMimeTypes.ContainsKey(ext))
        {
            return false;
        }

        var accept = acceptHeader ?? string.Empty;
        if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase) &&
            !accept.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    public void CleanStaleHotMarker()
    {
        var hotFile = Path.Combine(PublicDir, "hot");
        var manifestFile = Path.Combine(PublicDir, "build", "manifest.json");
        var devHmr = Environment.GetEnvironmentVariable("KAROBAR_DEV_HMR");

        if (File.Exists(manifestFile) && File.Exists(hotFile) && string.IsNullOrWhiteSpace(devHmr))
        {
            try
            {
                File.Delete(hotFile);
            }
            catch
            {
                // Ignore if read-only
            }
        }
    }

    /// <summary>
    /// Resolves resources/views/index.blade.php (or public/index.html) with Vite manifest.json
    /// CSS and JS bundle tags plus the KarobarPreloadScript injection for window.karobarDesktop.
    /// </summary>
    public byte[] GetOrBuildSpaShellHtmlBuffer(bool forceRebuild = false)
    {
        if (!forceRebuild && _cachedSpaHtmlBuffer is { Length: > 0 })
        {
            return _cachedSpaHtmlBuffer;
        }

        var html = BuildSpaShellHtml();
        var bytes = Encoding.UTF8.GetBytes(html);
        _cachedSpaHtmlBuffer = bytes;
        return bytes;
    }

    public string BuildSpaShellHtml()
    {
        var bladePath = Path.Combine(_networkConfig.AppRoot, "resources", "views", "index.blade.php");
        var indexHtmlPath = Path.Combine(PublicDir, "index.html");

        string rawTemplate;
        if (File.Exists(bladePath))
        {
            rawTemplate = File.ReadAllText(bladePath, Encoding.UTF8);
        }
        else if (File.Exists(indexHtmlPath))
        {
            rawTemplate = File.ReadAllText(indexHtmlPath, Encoding.UTF8);
        }
        else
        {
            rawTemplate = """
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8">
                    <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
                    <title>Karobar-App</title>
                    @vite(['resources/css/app.css', 'resources/js/app.jsx'])
                </head>
                <body>
                    <div id="app"></div>
                </body>
                </html>
                """;
        }

        var viteTags = BuildViteAssetTagsFromManifest();
        var preloadTag = "<script src=\"/__karobar_ipc/preload.js\"></script>";

        // Replace Laravel Blade @if(...) @viteReactRefresh @vite(...) @endif block
        var bladeViteBlockRegex = new Regex(
            @"@if\s*\([^\r\n]*manifest\.json[^\r\n]*\)[\s\S]*?@endif",
            RegexOptions.IgnoreCase);

        string resolvedHtml;
        if (bladeViteBlockRegex.IsMatch(rawTemplate))
        {
            resolvedHtml = bladeViteBlockRegex.Replace(rawTemplate, $"{preloadTag}\n    {viteTags}");
        }
        else if (rawTemplate.Contains("@vite", StringComparison.OrdinalIgnoreCase))
        {
            resolvedHtml = Regex.Replace(rawTemplate, @"@viteReactRefresh", "", RegexOptions.IgnoreCase);
            resolvedHtml = Regex.Replace(
                resolvedHtml,
                @"@vite\s*\([^\)]*\)",
                $"{preloadTag}\n    {viteTags}",
                RegexOptions.IgnoreCase);
        }
        else
        {
            resolvedHtml = rawTemplate.Replace("</head>", $"    {preloadTag}\n    {viteTags}\n</head>", StringComparison.OrdinalIgnoreCase);
        }

        return resolvedHtml;
    }

    public string BuildViteAssetTagsFromManifest()
    {
        var manifestPath = Path.Combine(PublicDir, "build", "manifest.json");
        var cssLinks = new List<string>();
        var jsScripts = new List<string>();
        var modulePreloads = new List<string>();

        if (File.Exists(manifestPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
                var root = doc.RootElement;

                // 1. resources/css/app.css
                if (root.TryGetProperty("resources/css/app.css", out var cssEntry) &&
                    cssEntry.TryGetProperty("file", out var cssFileProp) &&
                    cssFileProp.ValueKind == JsonValueKind.String)
                {
                    var cssFile = cssFileProp.GetString();
                    if (!string.IsNullOrWhiteSpace(cssFile))
                    {
                        cssLinks.Add($"<link rel=\"stylesheet\" href=\"/build/{cssFile.TrimStart('/')}\" />");
                    }
                }

                // 2. resources/js/app.jsx
                if (root.TryGetProperty("resources/js/app.jsx", out var jsEntry))
                {
                    if (jsEntry.TryGetProperty("css", out var entryCssArr) && entryCssArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var c in entryCssArr.EnumerateArray())
                        {
                            var cf = c.GetString();
                            if (!string.IsNullOrWhiteSpace(cf))
                            {
                                cssLinks.Add($"<link rel=\"stylesheet\" href=\"/build/{cf.TrimStart('/')}\" />");
                            }
                        }
                    }

                    if (jsEntry.TryGetProperty("imports", out var importsArr) && importsArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var imp in importsArr.EnumerateArray())
                        {
                            var impKey = imp.GetString();
                            if (!string.IsNullOrWhiteSpace(impKey) &&
                                root.TryGetProperty(impKey, out var impNode))
                            {
                                if (impNode.TryGetProperty("file", out var impFileProp) &&
                                    impFileProp.ValueKind == JsonValueKind.String)
                                {
                                    var impFile = impFileProp.GetString();
                                    if (!string.IsNullOrWhiteSpace(impFile))
                                    {
                                        modulePreloads.Add($"<link rel=\"modulepreload\" href=\"/build/{impFile.TrimStart('/')}\" />");
                                    }
                                }

                                if (impNode.TryGetProperty("css", out var impCssArr) &&
                                    impCssArr.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var ic in impCssArr.EnumerateArray())
                                    {
                                        var icf = ic.GetString();
                                        if (!string.IsNullOrWhiteSpace(icf))
                                        {
                                            cssLinks.Add($"<link rel=\"stylesheet\" href=\"/build/{icf.TrimStart('/')}\" />");
                                        }
                                    }
                                }
                            }
                        }
                    }

                    if (jsEntry.TryGetProperty("file", out var jsFileProp) &&
                        jsFileProp.ValueKind == JsonValueKind.String)
                    {
                        var jsFile = jsFileProp.GetString();
                        if (!string.IsNullOrWhiteSpace(jsFile))
                        {
                            jsScripts.Add($"<script type=\"module\" src=\"/build/{jsFile.TrimStart('/')}\"></script>");
                        }
                    }
                }
            }
            catch
            {
                // Fallback below if manifest is malformed
            }
        }

        if (jsScripts.Count == 0)
        {
            jsScripts.Add("<script type=\"module\" src=\"/build/assets/app.js\"></script>");
        }

        var allTags = new List<string>();
        allTags.AddRange(cssLinks.Distinct());
        allTags.AddRange(modulePreloads.Distinct());
        allTags.AddRange(jsScripts.Distinct());
        return string.Join("\n    ", allTags);
    }

    public async Task<bool> TryServeStaticAssetAsync(HttpContext context)
    {
        var method = context.Request.Method;
        if (!HttpMethods.IsGet(method) && !HttpMethods.IsHead(method))
        {
            return false;
        }

        var rawPath = context.Request.Path.Value ?? "/";
        if (string.IsNullOrWhiteSpace(rawPath) ||
            rawPath == "/" ||
            rawPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
            rawPath.StartsWith("/print/", StringComparison.OrdinalIgnoreCase) ||
            rawPath.StartsWith("/sanctum/", StringComparison.OrdinalIgnoreCase) ||
            rawPath.EndsWith(".php", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string decodedPath;
        try
        {
            decodedPath = Uri.UnescapeDataString(rawPath);
        }
        catch
        {
            return false;
        }

        var ext = Path.GetExtension(decodedPath);
        if (string.IsNullOrEmpty(ext) || !StaticMimeTypes.TryGetValue(ext, out var contentType))
        {
            return false;
        }

        string? targetFile = null;
        if (decodedPath.StartsWith("/storage/", StringComparison.OrdinalIgnoreCase))
        {
            var relStorage = decodedPath["/storage/".Length..].TrimStart('/', '\\');
            if (string.IsNullOrWhiteSpace(relStorage)) return false;

            var fullStorageDir = Path.GetFullPath(StoragePublicDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(fullStorageDir, relStorage.Replace('/', Path.DirectorySeparatorChar)));
            if (candidate.StartsWith(fullStorageDir, StringComparison.OrdinalIgnoreCase))
            {
                targetFile = candidate;
            }
        }
        else
        {
            var relPublic = decodedPath.TrimStart('/', '\\');
            if (string.IsNullOrWhiteSpace(relPublic)) return false;

            var fullPublicDir = Path.GetFullPath(PublicDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var candidate = Path.GetFullPath(Path.Combine(fullPublicDir, relPublic.Replace('/', Path.DirectorySeparatorChar)));
            if (candidate.StartsWith(fullPublicDir, StringComparison.OrdinalIgnoreCase))
            {
                targetFile = candidate;
            }
        }

        if (string.IsNullOrWhiteSpace(targetFile))
        {
            return false;
        }

        var isImmutableBuildAsset = decodedPath.StartsWith("/build/assets/", StringComparison.OrdinalIgnoreCase);
        if (isImmutableBuildAsset && _staticAssetCache.TryGetValue(targetFile, out var cached))
        {
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = cached.ContentType;
            context.Response.ContentLength = cached.Buffer.Length;
            context.Response.Headers.CacheControl = cached.CacheControl;

            if (!HttpMethods.IsHead(method))
            {
                await context.Response.Body.WriteAsync(cached.Buffer, context.RequestAborted);
            }
            return true;
        }

        if (!File.Exists(targetFile))
        {
            return false;
        }

        var fileInfo = new FileInfo(targetFile);
        var cacheControl = isImmutableBuildAsset
            ? "public, max-age=31536000, immutable"
            : "public, max-age=300";

        if (HttpMethods.IsGet(method) &&
            isImmutableBuildAsset &&
            fileInfo.Length <= MaxSingleCachedAssetBytes &&
            Interlocked.Read(ref _staticAssetCacheBytes) + fileInfo.Length <= MaxStaticAssetCacheBytes)
        {
            var buf = await File.ReadAllBytesAsync(targetFile, context.RequestAborted);
            var entry = new CachedStaticAsset
            {
                ContentType = contentType,
                CacheControl = cacheControl,
                Buffer = buf
            };
            if (_staticAssetCache.TryAdd(targetFile, entry))
            {
                Interlocked.Add(ref _staticAssetCacheBytes, buf.Length);
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = contentType;
            context.Response.ContentLength = buf.Length;
            context.Response.Headers.CacheControl = cacheControl;
            await context.Response.Body.WriteAsync(buf, context.RequestAborted);
            return true;
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = contentType;
        context.Response.ContentLength = fileInfo.Length;
        context.Response.Headers.CacheControl = cacheControl;

        if (!HttpMethods.IsHead(method))
        {
            await context.Response.SendFileAsync(targetFile, context.RequestAborted);
        }

        return true;
    }

    public WebApplication BuildWebApplication(int? explicitPort = null, string[]? args = null)
    {
        var port = explicitPort ?? FindFreePort(NetworkConfigManager.DefaultPort);
        _networkConfig.ActivePort = port;
        CleanStaleHotMarker();

        Environment.SetEnvironmentVariable("KAROBAR_ROOT_PATH", _networkConfig.AppRoot);
        Environment.SetEnvironmentVariable("KAROBAR_APP_ROOT", _networkConfig.AppRoot);
        Environment.SetEnvironmentVariable("APP_URL", $"http://127.0.0.1:{port}");

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args ?? [],
            ContentRootPath = _networkConfig.AppRoot,
            WebRootPath = Directory.Exists(PublicDir) ? PublicDir : _networkConfig.AppRoot
        });

        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Limits.MaxRequestBodySize = 1024L * 1024 * 1024; // 1 GB for large SQLite / backup uploads
            k.ListenAnyIP(port); // Listen on 0.0.0.0 so remote Multi-PC POS Terminals on the LAN can connect
        });
        builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
        {
            o.MultipartBodyLengthLimit = 1024L * 1024 * 1024;
            o.ValueLengthLimit = int.MaxValue;
            o.MultipartHeadersLengthLimit = int.MaxValue;
        });

        // Register Host Singletons
        builder.Services.AddSingleton(_networkConfig);
        builder.Services.AddSingleton(_windowManager);
        builder.Services.AddSingleton(_desktopBridge);
        builder.Services.AddSingleton(this);
        builder.Services.AddHttpContextAccessor();

        // Register Controllers & Services across all Karobar assemblies
        var karobarAssemblies = LoadKarobarAssemblies();
        var mvcBuilder = builder.Services.AddControllers();
        foreach (var asm in karobarAssemblies)
        {
            mvcBuilder.AddApplicationPart(asm);
        }

        RegisterKarobarAssemblyServices(builder.Services, builder.Configuration, karobarAssemblies);

        var app = builder.Build();

        // 0. Laravel _method spoofing (POST with ?_method=PUT or FormData _method=PUT/PATCH/DELETE)
        // Must execute BEFORE UseRouting() so endpoint routing matches [HttpPut]/[HttpPatch]/[HttpDelete].
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsPost(context.Request.Method))
            {
                string? spoofed = null;
                if (context.Request.Query.TryGetValue("_method", out var qMethod) && !string.IsNullOrWhiteSpace(qMethod))
                {
                    spoofed = qMethod.ToString().Trim().ToUpperInvariant();
                }
                else if (context.Request.HasFormContentType)
                {
                    context.Request.EnableBuffering();
                    var form = await context.Request.ReadFormAsync(context.RequestAborted);
                    if (form.TryGetValue("_method", out var fMethod) && !string.IsNullOrWhiteSpace(fMethod))
                    {
                        spoofed = fMethod.ToString().Trim().ToUpperInvariant();
                    }
                }

                if (spoofed is "PUT" or "PATCH" or "DELETE")
                {
                    context.Request.Method = spoofed;
                }
            }

            await next();
        });
        app.UseRouting();

        // 1. Health Endpoint: GET /up -> 200 OK
        app.MapGet("/up", () => Results.Text("OK", "text/plain; charset=utf-8"));

        // 2. Desktop IPC Preload Script & Invoke Endpoints
        app.MapGet("/__karobar_ipc/preload.js", (HttpContext ctx) =>
        {
            var isOffscreen = ctx.Request.Query.ContainsKey("offscreen");
            var js = KarobarPreloadScript.GetPreloadScript(isOffscreen);
            return Results.Text(js, "application/javascript; charset=utf-8");
        });

        app.MapPost("/__karobar_ipc/invoke", async (HttpContext ctx) =>
        {
            using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
            var root = doc.RootElement;
            var channel = root.TryGetProperty("channel", out var chProp) && chProp.ValueKind == JsonValueKind.String
                ? chProp.GetString() ?? ""
                : "";
            JsonElement? payload = root.TryGetProperty("payload", out var plProp) ? plProp.Clone() : null;

            var result = await _desktopBridge.DispatchIpcAsync(channel, payload, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(new { ok = true, result }, ctx.RequestAborted);
        });

        app.MapPost("/__karobar_ipc/{channel}", async (string channel, HttpContext ctx) =>
        {
            JsonElement? payload = null;
            if (ctx.Request.ContentLength is > 0)
            {
                using var doc = await JsonDocument.ParseAsync(ctx.Request.Body, cancellationToken: ctx.RequestAborted);
                payload = doc.RootElement.Clone();
            }

            var result = await _desktopBridge.DispatchIpcAsync(channel, payload, ctx.RequestAborted);
            await ctx.Response.WriteAsJsonAsync(new { ok = true, result }, ctx.RequestAborted);
        });

        app.MapGet("/__karobar_ipc/events", (HttpContext ctx) =>
        {
            var since = long.TryParse(ctx.Request.Query["since"], out var s) ? s : 0L;
            var events = _windowManager.GetRecentEventsSince(since);
            return Results.Json(new { ok = true, events });
        });

        // 3. Built-in Desktop HTML Views (Splash & Terminal Offline Recovery)
        app.MapGet("/__desktop/splash.html", () =>
        {
            var splashPath = _windowManager.ResolveSplashHtmlPath();
            var html = File.Exists(splashPath)
                ? File.ReadAllText(splashPath, Encoding.UTF8)
                : new SplashWindowController(splashPath).GetHtmlContent();
            return Results.Text(html, "text/html; charset=utf-8");
        });

        app.MapGet("/__desktop/terminal-offline.html", () =>
        {
            var offlinePath = _windowManager.ResolveTerminalOfflineHtmlPath();
            var html = File.Exists(offlinePath)
                ? File.ReadAllText(offlinePath, Encoding.UTF8)
                : "<html><body><h1>Cannot Reach Karobar Server</h1></body></html>";
            if (!html.Contains("/__karobar_ipc/preload.js", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("<head>", "<head>\n  <script src=\"/__karobar_ipc/preload.js\"></script>", StringComparison.OrdinalIgnoreCase);
            }
            return Results.Text(html, "text/html; charset=utf-8");
        });

        // 4. Terminal Identity Header Injection + High-Speed Static Asset Middleware
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.Value == "/up")
            {
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync("OK", context.RequestAborted);
                return;
            }

            // Inject X-Terminal-* headers if not already present on local WebView2 requests
            foreach (var kv in _networkConfig.GetTerminalIdentityHeaders())
            {
                if (!context.Request.Headers.ContainsKey(kv.Key))
                {
                    context.Request.Headers[kv.Key] = kv.Value;
                }
            }

            if (await TryServeStaticAssetAsync(context))
            {
                return;
            }

            if (IsSpaShellRequest(context.Request.Method, context.Request.Path.Value, context.Request.Headers.Accept))
            {
                var spaBytes = GetOrBuildSpaShellHtmlBuffer();
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength = spaBytes.Length;
                context.Response.Headers.CacheControl = "no-cache";

                if (!HttpMethods.IsHead(context.Request.Method))
                {
                    await context.Response.Body.WriteAsync(spaBytes, context.RequestAborted);
                }
                return;
            }

            await next();
        });

        // 5. Invoke custom middleware/endpoints from Karobar.Api & Karobar.Printing if defined
        ConfigureKarobarAssemblyPipeline(app, karobarAssemblies);

        app.MapControllers();

        // 6. SPA Fallback for any unmatched React navigation routes
        app.MapFallback(async (HttpContext context) =>
        {
            if (IsSpaShellRequest(context.Request.Method, context.Request.Path.Value, context.Request.Headers.Accept))
            {
                var spaBytes = GetOrBuildSpaShellHtmlBuffer();
                context.Response.StatusCode = StatusCodes.Status200OK;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength = spaBytes.Length;
                context.Response.Headers.CacheControl = "no-cache";
                if (!HttpMethods.IsHead(context.Request.Method))
                {
                    await context.Response.Body.WriteAsync(spaBytes, context.RequestAborted);
                }
                return;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new { message = "Not Found." }, context.RequestAborted);
        });

        return app;
    }

    public async Task StartAsync(int? explicitPort = null, CancellationToken cancellationToken = default, bool forceStart = false)
    {
        if (!forceStart && _networkConfig.IsTerminalMode() && explicitPort == null)
        {
            // In Multi-PC Terminal Mode, connect directly to the remote Store Server over LAN
            return;
        }

        if (_app != null)
        {
            return;
        }

        _app = BuildWebApplication(explicitPort);
        try
        {
            var bootstrapper = _app.Services.GetService<Karobar.Data.DatabaseBootstrapper>();
            if (bootstrapper != null)
            {
                await bootstrapper.EnsureSchemaReadyAsync(cancellationToken);
            }
        }
        catch
        {
            // Non-fatal if DB already initialized or running in terminal mode
        }
        await _app.StartAsync(cancellationToken);
        await WaitForServerReadyAsync($"http://127.0.0.1:{ActivePort}/up", 15000, cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_app == null)
        {
            return;
        }

        try
        {
            await _app.StopAsync(cancellationToken);
            await _app.DisposeAsync();
        }
        catch
        {
            // Ignore shutdown errors
        }
        finally
        {
            _app = null;
        }
    }

    public static async Task<bool> WaitForServerReadyAsync(
        string url,
        int timeoutMs = 15000,
        CancellationToken cancellationToken = default)
    {
        var start = Environment.TickCount64;
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };

        while (Environment.TickCount64 - start < timeoutMs && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var res = await client.GetAsync(url, cancellationToken);
                if (res.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch
            {
                // Server still booting
            }

            await Task.Delay(50, cancellationToken);
        }

        return false;
    }

    internal static IReadOnlyList<Assembly> LoadKarobarAssemblies()
    {
        var loaded = new List<Assembly>
        {
            typeof(Karobar.Core.HardwareMachineIdProvider).Assembly,
            typeof(Karobar.Data.KarobarDbContext).Assembly,
            typeof(Karobar.Services.LicenseService).Assembly,
            typeof(Karobar.Printing.PrintTemplateRenderer).Assembly,
            typeof(Karobar.Background.KarobarCliCommandRunner).Assembly,
            typeof(Karobar.Api.KarobarApiServiceExtensions).Assembly,
            typeof(KestrelHostServer).Assembly
        };

        return loaded.Distinct().ToList();
    }

    internal static void RegisterKarobarAssemblyServices(
        IServiceCollection services,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IReadOnlyList<Assembly> assemblies)
    {
        // 1. Invoke any static AddKarobar*(IServiceCollection, ...) extension methods
        foreach (var asm in assemblies)
        {
            Type[] types;
            try
            {
                types = asm.GetExportedTypes();
            }
            catch
            {
                continue;
            }

            foreach (var type in types.Where(t => t.IsAbstract && t.IsSealed))
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!method.Name.StartsWith("AddKarobar", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var parameters = method.GetParameters();
                    if (parameters.Length < 1 || parameters[0].ParameterType != typeof(IServiceCollection))
                        continue;

                    try
                    {
                        var invokeArgs = new object?[parameters.Length];
                        invokeArgs[0] = services;
                        var canInvoke = true;

                        for (var pIdx = 1; pIdx < parameters.Length; pIdx++)
                        {
                            var p = parameters[pIdx];
                            if (p.ParameterType.IsAssignableFrom(configuration.GetType()))
                            {
                                invokeArgs[pIdx] = configuration;
                            }
                            else if (p.HasDefaultValue)
                            {
                                invokeArgs[pIdx] = p.DefaultValue;
                            }
                            else
                            {
                                canInvoke = false;
                                break;
                            }
                        }

                        if (canInvoke)
                        {
                            method.Invoke(null, invokeArgs);
                        }
                    }
                    catch
                    {
                        // Ignore if already registered or optional dependency missing
                    }
                }
            }
        }

        // 2. Auto-register concrete service/engine/db classes from Karobar.* assemblies so DI works out of the box
        foreach (var asm in assemblies)
        {
            Type[] types;
            try
            {
                types = asm.GetExportedTypes();
            }
            catch
            {
                continue;
            }

            foreach (var type in types.Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }))
            {
                if (typeof(IHostedService).IsAssignableFrom(type))
                {
                    continue;
                }

                if (type.Name == "KarobarDb")
                {
                    services.TryAddSingleton(sp =>
                        ActivatorUtilities.CreateInstance(sp, type));
                    continue;
                }

                if (type.Name.EndsWith("Service", StringComparison.Ordinal) ||
                    type.Name.EndsWith("Engine", StringComparison.Ordinal) ||
                    type.Name.EndsWith("Renderer", StringComparison.Ordinal) ||
                    type.Name.EndsWith("Generator", StringComparison.Ordinal) ||
                    type.Name.EndsWith("Manager", StringComparison.Ordinal) ||
                    type.Name.EndsWith("Runner", StringComparison.Ordinal) ||
                    type.Name.EndsWith("Retriever", StringComparison.Ordinal) ||
                    type.Name.EndsWith("Builder", StringComparison.Ordinal) ||
                    type.Name.EndsWith("Guard", StringComparison.Ordinal) ||
                    type.Name.EndsWith("Provider", StringComparison.Ordinal))
                {
                    services.TryAddScoped(type);
                    foreach (var iface in type.GetInterfaces().Where(i => i.Namespace?.StartsWith("Karobar", StringComparison.Ordinal) == true))
                    {
                        services.TryAddScoped(iface, type);
                    }
                }
            }
        }
    }

    private static void ConfigureKarobarAssemblyPipeline(WebApplication app, IReadOnlyList<Assembly> assemblies)
    {
        foreach (var asm in assemblies)
        {
            Type[] types;
            try
            {
                types = asm.GetExportedTypes();
            }
            catch
            {
                continue;
            }

            foreach (var type in types.Where(t => t.IsAbstract && t.IsSealed))
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (!method.Name.StartsWith("UseKarobar", StringComparison.OrdinalIgnoreCase) &&
                        !method.Name.StartsWith("MapKarobar", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var parameters = method.GetParameters();
                    if (parameters.Length >= 1 &&
                        parameters[0].ParameterType.IsAssignableFrom(typeof(WebApplication)) &&
                        parameters.Skip(1).All(p => p.HasDefaultValue))
                    {
                        try
                        {
                            var invokeArgs = new object?[parameters.Length];
                            invokeArgs[0] = app;
                            for (var pIdx = 1; pIdx < parameters.Length; pIdx++)
                            {
                                invokeArgs[pIdx] = parameters[pIdx].DefaultValue;
                            }
                            method.Invoke(null, invokeArgs);
                        }
                        catch
                        {
                            // Ignore if middleware already added
                        }
                    }
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _windowManager.Dispose();
    }
}

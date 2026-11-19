using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Karobar.Host;

public sealed class OpenWindowInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Karobar-App";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("isFocused")]
    public bool IsFocused { get; set; }
}

public sealed class AppWindowOptions
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("isOffscreenPrintWorker")]
    public bool IsOffscreenPrintWorker { get; set; }
}

public sealed class DesktopEventEnvelope
{
    [JsonPropertyName("seq")]
    public long Seq { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "ipc-event";

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "";

    [JsonPropertyName("payload")]
    public object? Payload { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class MenuSectionItem
{
    public string Label { get; init; } = "";
    public string Path { get; init; } = "/";
    public string? Accelerator { get; init; }
}

public sealed class AppWindowInstance : IDisposable
{
    public int Id { get; }
    public string Title { get; set; }
    public string RoutePath { get; set; }
    public string Url { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool IsFocused { get; set; }
    public bool IsMinimized { get; set; }
    public bool IsMaximized { get; set; }
    public bool IsDestroyed { get; private set; }
    public bool IsOffscreenPrintWorker { get; }
    public IntPtr Hwnd { get; set; }
    public Process? NativeHostProcess { get; set; }
    public Action<string>? WebMessageSender { get; set; }
    public Action<string>? UrlNavigator { get; set; }

    public AppWindowInstance(
        int id,
        string title,
        string routePath,
        string url,
        int width = 1440,
        int height = 900,
        bool isOffscreenPrintWorker = false)
    {
        Id = id;
        Title = string.IsNullOrWhiteSpace(title) ? "Karobar-App" : title;
        RoutePath = string.IsNullOrWhiteSpace(routePath) ? "/" : routePath;
        Url = url;
        Width = width > 0 ? width : 1440;
        Height = height > 0 ? height : 900;
        IsOffscreenPrintWorker = isOffscreenPrintWorker;
    }

    public void Restore()
    {
        IsMinimized = false;
        if (OperatingSystem.IsWindows() && Hwnd != IntPtr.Zero)
        {
            Win32Native.ShowWindow(Hwnd, Win32Native.SW_RESTORE);
        }
    }

    public void Show()
    {
        if (IsDestroyed) return;
        if (OperatingSystem.IsWindows() && Hwnd != IntPtr.Zero)
        {
            Win32Native.ShowWindow(Hwnd, IsMaximized ? Win32Native.SW_SHOWMAXIMIZED : Win32Native.SW_SHOW);
        }
    }

    public void Maximize()
    {
        IsMaximized = true;
        IsMinimized = false;
        if (OperatingSystem.IsWindows() && Hwnd != IntPtr.Zero)
        {
            Win32Native.ShowWindow(Hwnd, Win32Native.SW_SHOWMAXIMIZED);
        }
    }

    public void Focus()
    {
        if (IsDestroyed) return;
        IsFocused = true;
        if (OperatingSystem.IsWindows() && Hwnd != IntPtr.Zero)
        {
            Win32Native.SetForegroundWindow(Hwnd);
        }
    }

    public void LoadUrl(string newUrl)
    {
        if (IsDestroyed) return;
        Url = newUrl;
        UrlNavigator?.Invoke(newUrl);
        WebMessageSender?.Invoke(JsonSerializer.Serialize(new
        {
            type = "ipc-event",
            channel = "karobar:navigate",
            payload = new { path = newUrl, title = Title }
        }));
    }

    public void SendEvent(string channel, object? payload)
    {
        if (IsDestroyed) return;
        var json = JsonSerializer.Serialize(new
        {
            type = "ipc-event",
            channel,
            payload
        });
        WebMessageSender?.Invoke(json);
    }

    public void Destroy()
    {
        if (IsDestroyed) return;
        IsDestroyed = true;
        IsFocused = false;
        try
        {
            if (NativeHostProcess is { HasExited: false })
            {
                NativeHostProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Ignore process termination errors
        }

        if (OperatingSystem.IsWindows() && Hwnd != IntPtr.Zero)
        {
            try
            {
                Win32Native.DestroyWindow(Hwnd);
            }
            catch
            {
                // Ignore
            }
            Hwnd = IntPtr.Zero;
        }
    }

    public void Dispose() => Destroy();
}

/// <summary>
/// Manages native Windows WebView2 window lifecycle, shared CoreWebView2Environment profile,
/// Splash screen (desktop/splash.html), Terminal Offline screen (desktop/terminal-offline.html),
/// System Tray (NotifyIcon using desktop/icon.ico), Application Menu bar, and offscreen print worker window pool.
/// </summary>
public sealed class WebView2WindowManager : IDisposable
{
    private static readonly Regex LocalPrintRouteRegex = new(
        @"\/(print|customer-ledger\/print|supplier-ledger\/print)(\/|\?|$)|[?&]silent_print=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly NetworkConfigManager _networkConfig;
    private readonly ConcurrentDictionary<int, AppWindowInstance> _activeWindows = new();
    private readonly ConcurrentQueue<DesktopEventEnvelope> _recentEvents = new();
    private readonly SemaphoreSlim _printJobMutex = new(1, 1);
    private readonly object _windowLock = new();

    private int _nextWindowId = 1;
    private long _nextEventSeq = 1;
    private AppWindowInstance? _mainWindow;
    private AppWindowInstance? _printWorkerWindow;
    private SplashWindowController? _splashWindow;
    private SystemTrayController? _systemTray;
    private bool _isQuitting;
    private string _lastMenuSignature = "";

    public WebView2WindowManager(NetworkConfigManager networkConfig)
    {
        _networkConfig = networkConfig;
        SharedWebView2UserDataFolder = ResolveSharedWebView2UserDataFolder();
    }

    public string SharedWebView2UserDataFolder { get; }
    public bool IsQuitting => _isQuitting;
    public AppWindowInstance? MainWindow => _mainWindow;
    public SplashWindowController? SplashWindow => _splashWindow;
    public SystemTrayController? SystemTray => _systemTray;

    public event Action<DesktopEventEnvelope>? OnDesktopEventDispatched;
    public Func<Task>? OnRunBackupRequested { get; set; }
    public Action? OnQuitRequested { get; set; }
    public Action<AppWindowInstance>? OnWindowCreated { get; set; }

    private static string ResolveSharedWebView2UserDataFolder()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                var dir = Path.Combine(appData, NetworkConfigManager.AppDataFolderName, "WebView2Environment");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
        catch
        {
            // Fallback to temp directory
        }

        var fallback = Path.Combine(Path.GetTempPath(), "Karobar-App-Native-WebView2Environment");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    public string? ResolveIconPath()
    {
        var candidates = new[]
        {
            Path.Combine(_networkConfig.AppRoot, "desktop", "icon.ico"),
            Path.Combine(_networkConfig.AppRoot, "desktop", "icon.png"),
            Path.Combine(AppContext.BaseDirectory, "desktop", "icon.ico"),
            Path.Combine(AppContext.BaseDirectory, "icon.ico"),
            Path.Combine(_networkConfig.AppRoot, "public", "logo.png")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public string ResolveSplashHtmlPath()
    {
        var candidates = new[]
        {
            Path.Combine(_networkConfig.AppRoot, "desktop", "splash.html"),
            Path.Combine(AppContext.BaseDirectory, "desktop", "splash.html"),
            Path.Combine(_networkConfig.AppRoot, "native-win", "src", "Karobar.Host", "desktop", "splash.html")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    public string ResolveTerminalOfflineHtmlPath()
    {
        var candidates = new[]
        {
            Path.Combine(_networkConfig.AppRoot, "desktop", "terminal-offline.html"),
            Path.Combine(AppContext.BaseDirectory, "desktop", "terminal-offline.html"),
            Path.Combine(_networkConfig.AppRoot, "native-win", "src", "Karobar.Host", "desktop", "terminal-offline.html")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    public SplashWindowController CreateSplashWindow()
    {
        _splashWindow = new SplashWindowController(ResolveSplashHtmlPath());
        _splashWindow.Show();
        return _splashWindow;
    }

    public void CloseSplashWindow()
    {
        _splashWindow?.Close();
        _splashWindow = null;
    }

    public SystemTrayController CreateSystemTray()
    {
        var iconPath = ResolveIconPath();
        _systemTray = new SystemTrayController(
            iconPath,
            onOpenApp: () =>
            {
                var target = GetActiveWindow();
                if (target is { IsDestroyed: false })
                {
                    if (target.IsMinimized) target.Restore();
                    target.Show();
                    target.Focus();
                }
                else
                {
                    CreateAppWindow("/");
                }
            },
            onOpenPosTerminal: () => SendOpenSubWindow("/sale-invoices/pos", "POS Terminal"),
            onNewWindow: () => CreateAppWindow("/"),
            onCheckUpdates: () =>
            {
                var target = GetActiveWindow();
                if (target is { IsDestroyed: false })
                {
                    target.Show();
                    target.Focus();
                }
                SendTriggerUpdateCheck(manual: true);
            },
            onRunBackupNow: () =>
            {
                if (OnRunBackupRequested != null)
                {
                    _ = Task.Run(OnRunBackupRequested);
                }
            },
            onQuit: () =>
            {
                _isQuitting = true;
                OnQuitRequested?.Invoke();
            });

        _systemTray.Initialize();
        return _systemTray;
    }

    public AppWindowInstance? GetActiveWindow()
    {
        foreach (var win in _activeWindows.Values)
        {
            if (!win.IsDestroyed && win.IsFocused)
            {
                return win;
            }
        }

        if (_mainWindow is { IsDestroyed: false })
        {
            return _mainWindow;
        }

        foreach (var win in _activeWindows.Values)
        {
            if (!win.IsDestroyed)
            {
                return win;
            }
        }

        return null;
    }

    public AppWindowInstance CreateMainWindow() => CreateAppWindow("/");

    public AppWindowInstance CreateAppWindow(string routePath = "/", AppWindowOptions? options = null)
    {
        options ??= new AppWindowOptions();
        var cleanRoute = string.IsNullOrWhiteSpace(routePath) ? "/" : routePath.Trim();
        var baseUrl = _networkConfig.GetAppBaseUrl();
        var fullUrl = cleanRoute.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                      cleanRoute.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                      cleanRoute.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            ? cleanRoute
            : $"{baseUrl}{(cleanRoute.StartsWith('/') ? "" : "/")}{cleanRoute}";

        lock (_windowLock)
        {
            var id = Interlocked.Increment(ref _nextWindowId);
            foreach (var existing in _activeWindows.Values)
            {
                existing.IsFocused = false;
            }

            var win = new AppWindowInstance(
                id,
                options.Title ?? "Karobar-App",
                cleanRoute,
                fullUrl,
                options.Width ?? 1440,
                options.Height ?? 900,
                options.IsOffscreenPrintWorker)
            {
                IsFocused = !options.IsOffscreenPrintWorker
            };

            if (!options.IsOffscreenPrintWorker)
            {
                _activeWindows[id] = win;
                if (_mainWindow == null || _mainWindow.IsDestroyed)
                {
                    _mainWindow = win;
                }

                if (_activeWindows.Count == 1)
                {
                    win.Maximize();
                }

                win.Show();
                win.Focus();
                CloseSplashWindow();
                UpdateApplicationMenus();
                OnWindowCreated?.Invoke(win);
            }

            return win;
        }
    }

    public void LoadTerminalOfflineFallback(AppWindowInstance? targetWindow = null)
    {
        var win = targetWindow ?? GetActiveWindow() ?? CreateMainWindow();
        var offlinePath = ResolveTerminalOfflineHtmlPath();
        if (File.Exists(offlinePath))
        {
            var fileUri = new Uri(offlinePath).AbsoluteUri;
            win.LoadUrl(fileUri);
        }
        else
        {
            win.LoadUrl($"http://127.0.0.1:{_networkConfig.ActivePort}/__desktop/terminal-offline.html");
        }
    }

    public IReadOnlyList<OpenWindowInfo> GetOpenWindows()
    {
        var focused = GetActiveWindow();
        var list = new List<OpenWindowInfo>();
        foreach (var win in _activeWindows.Values.OrderBy(w => w.Id))
        {
            if (!win.IsDestroyed)
            {
                list.Add(new OpenWindowInfo
                {
                    Id = win.Id,
                    Title = win.Title,
                    Url = win.Url,
                    IsFocused = focused != null && win.Id == focused.Id
                });
            }
        }
        return list;
    }

    public bool FocusWindow(int winId)
    {
        lock (_windowLock)
        {
            if (!_activeWindows.TryGetValue(winId, out var target) || target.IsDestroyed)
            {
                return false;
            }

            foreach (var win in _activeWindows.Values)
            {
                win.IsFocused = win.Id == winId;
            }

            if (target.IsMinimized)
            {
                target.Restore();
            }

            target.Show();
            target.Focus();
            UpdateApplicationMenus();
            return true;
        }
    }

    public bool CloseWindow(int winId)
    {
        lock (_windowLock)
        {
            if (!_activeWindows.TryRemove(winId, out var win))
            {
                return false;
            }

            win.Destroy();
            if (_mainWindow?.Id == winId)
            {
                _mainWindow = _activeWindows.Values.FirstOrDefault(w => !w.IsDestroyed);
            }

            if (_activeWindows.IsEmpty)
            {
                _isQuitting = true;
                _printWorkerWindow?.Destroy();
                _printWorkerWindow = null;
            }
            else
            {
                UpdateApplicationMenus();
            }

            return true;
        }
    }

    public static bool IsLocalPrintRouteUrl(string? rawUrl)
    {
        var u = (rawUrl ?? string.Empty).Trim();
        return !string.IsNullOrEmpty(u) && LocalPrintRouteRegex.IsMatch(u);
    }

    /// <summary>
    /// Evaluates window.open / navigation requests matching configurePopupAndPrintHandler() in desktop/main.cjs.
    /// </summary>
    public string HandleWindowOpenRequest(
        string? rawUrl,
        string? frameName,
        Func<string, Task>? onSilentPrintIntercepted = null)
    {
        var url = (rawUrl ?? string.Empty).Trim();
        var frame = (frameName ?? string.Empty).Trim();

        // 1. Intercept print routes and spool silently in background
        if (IsLocalPrintRouteUrl(url))
        {
            var baseUrl = _networkConfig.GetAppBaseUrl();
            var resolvedUrl = url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                              url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? url
                : $"{baseUrl}{(url.StartsWith('/') ? "" : "/")}{url}";

            if (!Regex.IsMatch(resolvedUrl, @"[?&]silent_print=", RegexOptions.IgnoreCase) &&
                !Regex.IsMatch(resolvedUrl, @"[?&]signature=", RegexOptions.IgnoreCase))
            {
                resolvedUrl += (resolvedUrl.Contains('?') ? "&" : "?") + "silent_print=1";
            }

            if (onSilentPrintIntercepted != null)
            {
                _ = Task.Run(() => onSilentPrintIntercepted(resolvedUrl));
            }
            return "deny-silent-print";
        }

        // 2. Block about:blank or named print popups (e.g. salePrintWin)
        if (string.IsNullOrEmpty(url) ||
            string.Equals(url, "about:blank", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            frame.Contains("print", StringComparison.OrdinalIgnoreCase))
        {
            return "deny";
        }

        // 3. Allow blob:/data: document previews (such as "Open PDF" in ShareDocumentModal)
        if (url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return "allow-preview";
        }

        // 4. Intercept internal application URLs and spawn a native app window
        var appBaseUrl = _networkConfig.GetAppBaseUrl();
        var localIpBase = $"http://127.0.0.1:{_networkConfig.ActivePort}";
        var localhostBase = $"http://localhost:{_networkConfig.ActivePort}";

        var isInternal = url.StartsWith(appBaseUrl, StringComparison.OrdinalIgnoreCase) ||
                         url.StartsWith(localIpBase, StringComparison.OrdinalIgnoreCase) ||
                         url.StartsWith(localhostBase, StringComparison.OrdinalIgnoreCase) ||
                         (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal));

        if (isInternal)
        {
            var route = url;
            if (route.StartsWith(appBaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                route = route[appBaseUrl.Length..];
            }
            else if (route.StartsWith(localIpBase, StringComparison.OrdinalIgnoreCase))
            {
                route = route[localIpBase.Length..];
            }
            else if (route.StartsWith(localhostBase, StringComparison.OrdinalIgnoreCase))
            {
                route = route[localhostBase.Length..];
            }

            CreateAppWindow(string.IsNullOrWhiteSpace(route) ? "/" : route);
            return "deny-spawned-internal";
        }

        // 5. External links (https:, http:, mailto:, whatsapp:, tel:)
        if (Regex.IsMatch(url, @"^(https?:|mailto:|whatsapp:|tel:)", RegexOptions.IgnoreCase))
        {
            TryOpenExternalUrl(url);
        }

        return "deny";
    }

    public static bool TryOpenExternalUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Regex.IsMatch(url, @"^(https?:|mailto:|whatsapp:|tel:)", RegexOptions.IgnoreCase))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    // =========================================================================
    // Offscreen Print Worker Pool (matching prewarmPrintWindow / runOffscreenPrintJob)
    // =========================================================================

    public AppWindowInstance CreateHiddenPrintWindow()
    {
        var id = Interlocked.Increment(ref _nextWindowId);
        return new AppWindowInstance(
            id,
            "Karobar Offscreen Print Worker",
            "about:blank",
            "about:blank",
            width: 820,
            height: 900,
            isOffscreenPrintWorker: true);
    }

    public AppWindowInstance PrewarmPrintWindow()
    {
        if (_printWorkerWindow == null || _printWorkerWindow.IsDestroyed)
        {
            _printWorkerWindow = CreateHiddenPrintWindow();
        }
        return _printWorkerWindow;
    }

    public AppWindowInstance AcquirePrintWindow()
    {
        var win = _printWorkerWindow;
        _printWorkerWindow = null;
        if (win == null || win.IsDestroyed)
        {
            win = CreateHiddenPrintWindow();
        }

        if (!_isQuitting)
        {
            _ = Task.Delay(50).ContinueWith(_ =>
            {
                if (!_isQuitting) PrewarmPrintWindow();
            });
        }

        return win;
    }

    public void ReleasePrintWindow(AppWindowInstance? win)
    {
        if (win == null) return;
        _ = Task.Delay(5000).ContinueWith(_ => win.Destroy());
    }

    public async Task<T> RunOffscreenPrintJobAsync<T>(Func<AppWindowInstance, Task<T>> jobFunc)
    {
        await _printJobMutex.WaitAsync();
        var printWin = AcquirePrintWindow();
        try
        {
            return await jobFunc(printWin);
        }
        finally
        {
            ReleasePrintWindow(printWin);
            _printJobMutex.Release();
        }
    }

    // =========================================================================
    // Desktop Event Emitters (karobar:navigate, karobar:open-subwindow,
    // karobar:window-command, karobar:update-progress, karobar:trigger-update-check)
    // =========================================================================

    public void SendNavigate(string routePath, string? title = null)
    {
        var target = GetActiveWindow();
        if (target == null || target.IsDestroyed)
        {
            CreateAppWindow(routePath, new AppWindowOptions { Title = title });
            return;
        }

        target.RoutePath = routePath;
        if (!string.IsNullOrWhiteSpace(title))
        {
            target.Title = title;
        }

        DispatchEvent("karobar:navigate", new { path = routePath, title = title ?? target.Title }, target);
    }

    public void SendOpenNewWindow(string routePath, string? title = null)
    {
        CreateAppWindow(routePath, new AppWindowOptions { Title = title });
    }

    public void SendOpenSubWindow(string routePath, string? title = null)
    {
        var win = CreateAppWindow(routePath, new AppWindowOptions { Title = title });
        DispatchEvent("karobar:open-subwindow", new { path = routePath, title = win.Title, windowId = win.Id });
    }

    public void SendWindowCommand(string command, Dictionary<string, object?>? extraPayload = null)
    {
        var target = GetActiveWindow();
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["command"] = command
        };

        if (extraPayload != null)
        {
            foreach (var kv in extraPayload)
            {
                payload[kv.Key] = kv.Value;
            }
        }

        DispatchEvent("karobar:window-command", payload, target);
    }

    public void SendUpdateProgress(object payload)
    {
        DispatchEvent("karobar:update-progress", payload, targetOnly: null);
    }

    public void SendTriggerUpdateCheck(bool manual = true)
    {
        DispatchEvent("karobar:trigger-update-check", new { manual }, GetActiveWindow());
    }

    public IReadOnlyList<DesktopEventEnvelope> GetRecentEventsSince(long sinceSeq)
    {
        return _recentEvents.Where(e => e.Seq > sinceSeq).ToList();
    }

    private void DispatchEvent(string channel, object? payload, AppWindowInstance? targetOnly = null)
    {
        var envelope = new DesktopEventEnvelope
        {
            Seq = Interlocked.Increment(ref _nextEventSeq),
            Type = "ipc-event",
            Channel = channel,
            Payload = payload,
            Timestamp = DateTimeOffset.UtcNow
        };

        _recentEvents.Enqueue(envelope);
        while (_recentEvents.Count > 100)
        {
            _recentEvents.TryDequeue(out _);
        }

        if (targetOnly is { IsDestroyed: false })
        {
            targetOnly.SendEvent(channel, payload);
        }
        else
        {
            foreach (var win in _activeWindows.Values)
            {
                if (!win.IsDestroyed)
                {
                    win.SendEvent(channel, payload);
                }
            }
        }

        OnDesktopEventDispatched?.Invoke(envelope);
    }

    // =========================================================================
    // Native Application Menu Bar Definition (matching createApplicationMenu in desktop/main.cjs)
    // =========================================================================

    public IReadOnlyDictionary<string, IReadOnlyList<MenuSectionItem>> GetApplicationMenuSections()
    {
        return new Dictionary<string, IReadOnlyList<MenuSectionItem>>
        {
            ["File & Workspace"] =
            [
                new() { Label = "Dashboard (Main View)", Path = "/dashboard", Accelerator = "CmdOrCtrl+Shift+H" },
                new() { Label = "Quick Window: Barcode POS", Path = "/sale-invoices/pos", Accelerator = "CmdOrCtrl+Shift+P" },
                new() { Label = "Quick Window: Retail Sale Invoice", Path = "/sale-invoices/create/retail", Accelerator = "CmdOrCtrl+Shift+S" },
                new() { Label = "Quick Window: Wholesale Invoice", Path = "/sale-invoices/create/wholesale", Accelerator = "CmdOrCtrl+Shift+W" },
                new() { Label = "Quick Window: Purchase Invoice", Path = "/purchase-invoices/create", Accelerator = "CmdOrCtrl+Shift+U" },
                new() { Label = "Quick Window: Customer Ledger", Path = "/customer-ledger" }
            ],
            ["Items & Masters"] =
            [
                new() { Label = "Products", Path = "/products" },
                new() { Label = "Add Product", Path = "/products/create" },
                new() { Label = "Product Batches", Path = "/product-batches" },
                new() { Label = "Categories", Path = "/categories" },
                new() { Label = "Brands", Path = "/brands" },
                new() { Label = "Add Brand", Path = "/brands/create" },
                new() { Label = "Suppliers", Path = "/suppliers" },
                new() { Label = "Add Supplier", Path = "/suppliers/create" },
                new() { Label = "Customers", Path = "/customers" },
                new() { Label = "Add Customer", Path = "/customers/create" },
                new() { Label = "Barcode Studio", Path = "/barcode-labels" }
            ],
            ["Invoices & POS"] =
            [
                new() { Label = "Barcode POS Terminal", Path = "/sale-invoices/pos" },
                new() { Label = "Sale Invoices", Path = "/sale-invoices" },
                new() { Label = "Create Retail Sale Invoice", Path = "/sale-invoices/create/retail" },
                new() { Label = "Wholesale Invoices", Path = "/wholesale-invoices" },
                new() { Label = "Create Wholesale Invoice", Path = "/sale-invoices/create/wholesale" },
                new() { Label = "Purchase Invoices", Path = "/purchase-invoices" },
                new() { Label = "Create Purchase Invoice", Path = "/purchase-invoices/create" },
                new() { Label = "Purchase Orders", Path = "/purchase-orders" },
                new() { Label = "Create Purchase Order", Path = "/purchase-orders/create" },
                new() { Label = "Smart Reorder Forecast", Path = "/smart-reorder" }
            ],
            ["Returns"] =
            [
                new() { Label = "Sale Returns", Path = "/sale-returns" },
                new() { Label = "Create Sale Return", Path = "/sale-returns/create" },
                new() { Label = "Purchase Returns", Path = "/purchase-returns" },
                new() { Label = "Create Purchase Return", Path = "/purchase-returns/create" }
            ],
            ["Stock & Inventory"] =
            [
                new() { Label = "Batch & Expiry", Path = "/batch-expiry" },
                new() { Label = "User Demands", Path = "/user-demands" },
                new() { Label = "Request Product", Path = "/user-demands/create" },
                new() { Label = "Stock Adjustments", Path = "/stock-adjustments" },
                new() { Label = "Add Stock Adjustment", Path = "/stock-adjustments/create" },
                new() { Label = "Stocktake Sessions", Path = "/stocktake-sessions" },
                new() { Label = "New Stocktake Session", Path = "/stocktake-sessions/create" },
                new() { Label = "Quarantine & Disposal", Path = "/stock-disposals" },
                new() { Label = "New Disposal", Path = "/stock-disposals/create" }
            ],
            ["Finance & Ledgers"] =
            [
                new() { Label = "Customer Ledger", Path = "/customer-ledger" },
                new() { Label = "Supplier Ledger", Path = "/supplier-ledger" },
                new() { Label = "Payment Vouchers", Path = "/payments" },
                new() { Label = "Cash Drawer", Path = "/cash-drawer" },
                new() { Label = "Financial Accounts", Path = "/accounts" },
                new() { Label = "Operating Expenses", Path = "/expenses" },
                new() { Label = "Double-Entry & GL", Path = "/accounting" },
                new() { Label = "HR, Payroll & Commissions", Path = "/hr-payroll" }
            ],
            ["Reports"] =
            [
                new() { Label = "Net Profit (P&L)", Path = "/reports/net-profit" },
                new() { Label = "Statutory Tax Return", Path = "/reports/tax-return" },
                new() { Label = "Cost of Sale", Path = "/reports/cost-of-sale" },
                new() { Label = "Cost of Sale Detail", Path = "/reports/cost-of-sale-detail" },
                new() { Label = "Outstanding Aging", Path = "/reports/outstanding-aging" },
                new() { Label = "Sale Detail", Path = "/reports/sale-detail" },
                new() { Label = "Purchase Detail", Path = "/reports/purchase-detail" },
                new() { Label = "Purchase Summary", Path = "/reports/purchase-summary" },
                new() { Label = "Sale Returns Report", Path = "/reports/sale-returns" },
                new() { Label = "Purchase Returns Report", Path = "/reports/purchase-returns" },
                new() { Label = "Current Stock", Path = "/reports/current-stock" },
                new() { Label = "Low Stock & Reorder", Path = "/reports/reorder" },
                new() { Label = "Product Comprehensive", Path = "/reports/product-comprehensive" },
                new() { Label = "Near Expiry Product", Path = "/reports/near-expiry-product" },
                new() { Label = "Stock Adjustment Report", Path = "/reports/stock-adjustment" },
                new() { Label = "Narcotics & Controlled Drugs Register", Path = "/reports/narcotics" }
            ],
            ["System & Settings"] =
            [
                new() { Label = "Settings", Path = "/settings" },
                new() { Label = "Server & Attached Terminals", Path = "/server-dashboard" },
                new() { Label = "Users", Path = "/users" },
                new() { Label = "Add User", Path = "/users/create" },
                new() { Label = "Roles", Path = "/roles" },
                new() { Label = "Create Role", Path = "/roles/create" },
                new() { Label = "Backups & Storage", Path = "/backups" },
                new() { Label = "Setup Genie Wizard", Path = "/setup" },
                new() { Label = "License Activation", Path = "/settings#license" }
            ]
        };
    }

    public void UpdateApplicationMenus()
    {
        var focused = GetActiveWindow();
        var sigBuilder = new StringBuilder();
        foreach (var win in _activeWindows.Values.OrderBy(w => w.Id))
        {
            if (!win.IsDestroyed)
            {
                sigBuilder.Append($"{win.Id}:{(focused?.Id == win.Id ? 1 : 0)}:{win.Title}|");
            }
        }

        var nextSig = sigBuilder.ToString();
        if (nextSig == _lastMenuSignature)
        {
            return;
        }

        _lastMenuSignature = nextSig;
    }

    public void ShutdownAllWindows()
    {
        _isQuitting = true;
        CloseSplashWindow();
        _systemTray?.Dispose();
        _systemTray = null;
        _printWorkerWindow?.Destroy();
        _printWorkerWindow = null;

        foreach (var win in _activeWindows.Values)
        {
            win.Destroy();
        }
        _activeWindows.Clear();
    }

    public void Dispose() => ShutdownAllWindows();
}

/// <summary>
/// Renders desktop/splash.html aesthetic (480x310 frameless, centered, alwaysOnTop, #0f172a background) during startup.
/// </summary>
public sealed class SplashWindowController : IDisposable
{
    public string SplashHtmlPath { get; }
    public int Width => 480;
    public int Height => 310;
    public string BackgroundColor => "#0f172a";
    public bool IsVisible { get; private set; }

    public SplashWindowController(string splashHtmlPath)
    {
        SplashHtmlPath = splashHtmlPath;
    }

    public void Show()
    {
        IsVisible = true;
    }

    public void Close()
    {
        IsVisible = false;
    }

    public string GetHtmlContent()
    {
        if (File.Exists(SplashHtmlPath))
        {
            return File.ReadAllText(SplashHtmlPath, Encoding.UTF8);
        }

        return """
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="UTF-8"><title>Starting Karobar-App…</title></head>
        <body style="background:#0f172a;color:#f8fafc;display:flex;flex-direction:column;align-items:center;justify-content:center;height:100vh;margin:0;font-family:Segoe UI,sans-serif;">
          <h1>Karobar<span style="color:#38bdf8;">-</span>App</h1>
          <p id="status">Initializing local database &amp; secure runtime…</p>
        </body>
        </html>
        """;
    }

    public void Dispose() => Close();
}

/// <summary>
/// Windows System Tray (NotifyIcon / Shell_NotifyIconW) controller using desktop/icon.ico
/// with quick actions: Show (Open Karobar-App), POS Terminal, New Window, Check Updates, Run Backup Now, Quit.
/// </summary>
public sealed class SystemTrayController : IDisposable
{
    public string? IconPath { get; }
    public string ToolTip => "Karobar-App";
    public bool IsInitialized { get; private set; }

    private readonly Action _onOpenApp;
    private readonly Action _onOpenPosTerminal;
    private readonly Action _onNewWindow;
    private readonly Action _onCheckUpdates;
    private readonly Action _onRunBackupNow;
    private readonly Action _onQuit;

    public IReadOnlyList<string> ContextMenuItems { get; } =
    [
        "Open Karobar-App",
        "POS Terminal",
        "New Window",
        "Check for Updates…",
        "Run Backup Now",
        "Quit Karobar-App"
    ];

    public SystemTrayController(
        string? iconPath,
        Action onOpenApp,
        Action onOpenPosTerminal,
        Action onNewWindow,
        Action onCheckUpdates,
        Action onRunBackupNow,
        Action onQuit)
    {
        IconPath = iconPath;
        _onOpenApp = onOpenApp;
        _onOpenPosTerminal = onOpenPosTerminal;
        _onNewWindow = onNewWindow;
        _onCheckUpdates = onCheckUpdates;
        _onRunBackupNow = onRunBackupNow;
        _onQuit = onQuit;
    }

    public void Initialize()
    {
        IsInitialized = true;
    }

    public void TriggerMenuAction(string label)
    {
        switch (label)
        {
            case "Open Karobar-App":
            case "Show":
                _onOpenApp();
                break;
            case "POS Terminal":
                _onOpenPosTerminal();
                break;
            case "New Window":
                _onNewWindow();
                break;
            case "Check for Updates…":
            case "Check Updates":
                _onCheckUpdates();
                break;
            case "Run Backup Now":
                _onRunBackupNow();
                break;
            case "Quit Karobar-App":
            case "Quit":
                _onQuit();
                break;
        }
    }

    public void Dispose()
    {
        IsInitialized = false;
    }
}

internal static class Win32Native
{
    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOWMAXIMIZED = 3;
    public const int SW_SHOW = 5;
    public const int SW_RESTORE = 9;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool IsWindowVisible(IntPtr hWnd);
}

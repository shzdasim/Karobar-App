namespace Karobar.Host;

public static class Program
{
    public const string SingleInstanceMutexName = @"Global\KarobarNativeErpV2Mutex";
    public const string FallbackLocalMutexName = @"Local\KarobarNativeErpV2Mutex";
    public const string ApplicationDisplayName = "Karobar-App Native";
    public const string ExecutableFileName = "Karobar-Native.exe";

    public static async Task<int> Main(string[] args)
    {
        var parsed = CommandLineRouter.Parse(args);

        // 1. Headless CLI execution (`--cli <command>` or `artisan <command>`)
        if (parsed.Mode == HostLaunchMode.HeadlessCli)
        {
            return await CommandLineRouter.ExecuteHeadlessCliAsync(parsed.CliArguments);
        }

        // 2. Single-Instance Named Windows Mutex (`Global\KarobarAppErpV2Mutex`)
        using var instanceLock = AcquireSingleInstanceLock(out var isFirstInstance);
        if (!isFirstInstance)
        {
            ActivateExistingWindow();
            return 0;
        }

        var networkConfig = new NetworkConfigManager();
        var windowManager = new WebView2WindowManager(networkConfig);
        await using var hostServer = new KestrelHostServer(networkConfig, windowManager);

        using var shutdownCts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            shutdownCts.Cancel();
        };
        windowManager.OnQuitRequested = () => shutdownCts.Cancel();

        // 3. Headless Server Mode (`--server-only` / `--headless`)
        if (parsed.Mode == HostLaunchMode.HeadlessServer)
        {
            await hostServer.StartAsync(parsed.ExplicitPort, shutdownCts.Token);
            try
            {
                await Task.Delay(Timeout.Infinite, shutdownCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Graceful exit
            }
            return 0;
        }

        // 4. Normal Native Desktop Launch
        windowManager.CreateSplashWindow();
        if (OperatingSystem.IsWindows())
        {
            _ = hostServer.DesktopBridge.GetPrintersAsync(forceRefresh: true);
        }

        var isOfflineTerminal = false;
        if (networkConfig.IsTerminalMode())
        {
            var cfg = networkConfig.LoadNetworkConfig();
            var ping = await networkConfig.TestServerConnectionAsync(cfg.ServerUrl, cfg.TerminalName, cancellationToken: shutdownCts.Token);
            if (!ping.Ok)
            {
                isOfflineTerminal = true;
                // Start local server so terminal-offline.html and window.karobarDesktop IPC remain available
                await hostServer.StartAsync(parsed.ExplicitPort, shutdownCts.Token);
            }
        }
        else
        {
            await hostServer.StartAsync(parsed.ExplicitPort, shutdownCts.Token);
        }

        windowManager.CreateSystemTray();
        windowManager.PrewarmPrintWindow();
        var mainWin = windowManager.CreateMainWindow();

        if (isOfflineTerminal)
        {
            windowManager.LoadTerminalOfflineFallback(mainWin);
        }

        // If launched inside a non-interactive verification test with KAROBAR_EXIT_AFTER_BOOT=1, exit cleanly after boot
        if (string.Equals(Environment.GetEnvironmentVariable("KAROBAR_EXIT_AFTER_BOOT"), "1", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        try
        {
            await Task.Delay(Timeout.Infinite, shutdownCts.Token);
        }
        catch (OperationCanceledException)
        {
            // Normal exit
        }

        return 0;
    }

    public static Mutex? AcquireSingleInstanceLock(out bool createdNew)
    {
        try
        {
            var mutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out createdNew);
            return mutex;
        }
        catch (UnauthorizedAccessException)
        {
            // An instance in another session already owns Global\KarobarAppErpV2Mutex
            createdNew = false;
            return null;
        }
        catch
        {
            try
            {
                var fallback = new Mutex(initiallyOwned: true, FallbackLocalMutexName, out createdNew);
                return fallback;
            }
            catch
            {
                createdNew = true;
                return null;
            }
        }
    }

    public static bool ActivateExistingWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var hwnd = Win32Native.FindWindowW(null, ApplicationDisplayName);
            if (hwnd == IntPtr.Zero)
            {
                hwnd = Win32Native.FindWindowW(null, "Karobar-App");
            }

            if (hwnd != IntPtr.Zero)
            {
                Win32Native.ShowWindow(hwnd, Win32Native.SW_RESTORE);
                Win32Native.SetForegroundWindow(hwnd);
                return true;
            }
        }
        catch
        {
            // Ignore Win32 activation errors
        }

        return false;
    }
}

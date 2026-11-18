namespace Karobar.Host;

public static class Program
{
    public const string SingleInstanceMutexName = @"Global\KarobarNativeErpV2Mutex";
    public const string FallbackLocalMutexName = @"Local\KarobarNativeErpV2Mutex";
    public const string ApplicationDisplayName = "Karobar-App Native";
    public const string ExecutableFileName = "Karobar-Native.exe";

    [STAThread]
    public static int Main(string[] args)
    {
        var parsed = CommandLineRouter.Parse(args);

        // 1. Headless CLI execution (`--cli <command>` or `artisan <command>`)
        if (parsed.Mode == HostLaunchMode.HeadlessCli)
        {
            return CommandLineRouter.ExecuteHeadlessCliAsync(parsed.CliArguments).GetAwaiter().GetResult();
        }

        // 2. Single-Instance Named Windows Mutex (`Global\KarobarNativeErpV2Mutex`)
        using var instanceLock = AcquireSingleInstanceLock(out var isFirstInstance);
        if (!isFirstInstance)
        {
            ActivateExistingWindow();
            return 0;
        }

        var networkConfig = new NetworkConfigManager();
        var windowManager = new WebView2WindowManager(networkConfig);
        using var shutdownCts = new CancellationTokenSource();
        var hostServer = new KestrelHostServer(networkConfig, windowManager);

        try
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                shutdownCts.Cancel();
            };

            // 3. Headless Server Mode (`--server-only` / `--headless`)
            if (parsed.Mode == HostLaunchMode.HeadlessServer)
            {
                windowManager.OnQuitRequested = () => shutdownCts.Cancel();
                hostServer.StartAsync(parsed.ExplicitPort, shutdownCts.Token).GetAwaiter().GetResult();
                try
                {
                    Task.Delay(Timeout.Infinite, shutdownCts.Token).GetAwaiter().GetResult();
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

#if WINDOWS_DESKTOP
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
            System.Windows.Forms.Application.EnableVisualStyles();
            System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

            using var splashForm = new NativeSplashForm(windowManager.ResolveIconPath());
            splashForm.Show();
            System.Windows.Forms.Application.DoEvents();
#endif

            var isOfflineTerminal = false;
            if (networkConfig.IsTerminalMode())
            {
                var cfg = networkConfig.LoadNetworkConfig();
                var ping = networkConfig.TestServerConnectionAsync(cfg.ServerUrl, cfg.TerminalName, cancellationToken: shutdownCts.Token)
                    .GetAwaiter().GetResult();
                if (!ping.Ok)
                {
                    isOfflineTerminal = true;
                    hostServer.StartAsync(parsed.ExplicitPort, shutdownCts.Token).GetAwaiter().GetResult();
                }
            }
            else
            {
                hostServer.StartAsync(parsed.ExplicitPort, shutdownCts.Token).GetAwaiter().GetResult();
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

#if WINDOWS_DESKTOP
            splashForm.Close();
            var mainForm = new NativeWaseelaMainForm(networkConfig, windowManager, hostServer);

            windowManager.OnQuitRequested = () =>
            {
                shutdownCts.Cancel();
                try { System.Windows.Forms.Application.Exit(); } catch { }
            };

            System.Windows.Forms.Application.Run(mainForm);
#else
            windowManager.OnQuitRequested = () => shutdownCts.Cancel();
            try
            {
                Task.Delay(Timeout.Infinite, shutdownCts.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Normal exit
            }
#endif

            return 0;
        }
        finally
        {
            hostServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
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

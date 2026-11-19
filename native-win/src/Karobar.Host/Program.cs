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

            // Run Kestrel + DB bootstrap on a thread-pool task so we NEVER deadlock
            // WindowsFormsSynchronizationContext while pumping the splash UI message loop.
            var isOfflineTerminal = false;
            var bootTask = Task.Run(async () =>
            {
                if (networkConfig.IsTerminalMode())
                {
                    var cfg = networkConfig.LoadNetworkConfig();
                    var ping = await networkConfig.TestServerConnectionAsync(cfg.ServerUrl, cfg.TerminalName, cancellationToken: shutdownCts.Token)
                        .ConfigureAwait(false);
                    if (!ping.Ok)
                    {
                        isOfflineTerminal = true;
                        await hostServer.StartAsync(parsed.ExplicitPort, shutdownCts.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    await hostServer.StartAsync(parsed.ExplicitPort, shutdownCts.Token).ConfigureAwait(false);
                }
            }, shutdownCts.Token);

#if WINDOWS_DESKTOP
            while (!bootTask.IsCompleted)
            {
                System.Windows.Forms.Application.DoEvents();
                Thread.Sleep(20);
            }
#endif
            bootTask.GetAwaiter().GetResult();

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
            var mainForm = new NativeWebView2Form(mainWin, windowManager, hostServer.DesktopBridge);

            // Route all child-window / openWindow / openSubWindow requests into Waseela-style tabs
            // inside the SINGLE main window instead of spawning separate OS windows.
            windowManager.OnWindowCreated = childWin =>
            {
                if (childWin.Id == mainWin.Id || childWin.IsOffscreenPrintWorker) return;
                if (mainForm.IsDisposed) return;

                mainForm.OpenTabFromExternal(childWin.RoutePath, childWin.Title);
            };

            windowManager.OnQuitRequested = () =>
            {
                shutdownCts.Cancel();
                try
                {
                    if (!mainForm.IsDisposed && mainForm.IsHandleCreated && mainForm.InvokeRequired)
                    {
                        mainForm.BeginInvoke((Action)(() =>
                        {
                            try { mainForm.Close(); } catch { }
                            try { System.Windows.Forms.Application.Exit(); } catch { }
                        }));
                    }
                    else
                    {
                        try { mainForm.Close(); } catch { }
                        try { System.Windows.Forms.Application.Exit(); } catch { }
                    }
                }
                catch { }
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
        catch (Exception ex)
        {
            var logMessage = $"[{DateTime.UtcNow:O}] Karobar-App Native startup failed:{Environment.NewLine}{ex}{Environment.NewLine}";
            try
            {
                var appDataLogDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Karobar-App-Native");
                Directory.CreateDirectory(appDataLogDir);
                File.AppendAllText(Path.Combine(appDataLogDir, "startup-error.log"), logMessage);
            }
            catch { }

            try
            {
                var localLogDir = Path.Combine(networkConfig.AppRoot, "storage", "logs");
                Directory.CreateDirectory(localLogDir);
                File.AppendAllText(Path.Combine(localLogDir, "native-startup.log"), logMessage);
            }
            catch { }

            Console.Error.WriteLine(logMessage);

#if WINDOWS_DESKTOP
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    $"Karobar-App Native could not start:\n\n{ex.Message}\n\nDetails were saved to %APPDATA%\\Karobar-App-Native\\startup-error.log",
                    ApplicationDisplayName,
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            catch { }
#endif
            return 1;
        }
        finally
        {
            Task.Run(() => hostServer.DisposeAsync().AsTask()).GetAwaiter().GetResult();
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

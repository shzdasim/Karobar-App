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

        // 1. Headless CLI execution (`--cli <command>`)
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
            System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
            System.Windows.Forms.Application.ThreadException += (_, argsEx) => LogFatalException("WinForms ThreadException", argsEx.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, argsEx) =>
            {
                if (argsEx.ExceptionObject is Exception uex) LogFatalException("AppDomain UnhandledException", uex);
            };
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
                        await hostServer.StartAsync(parsed.ExplicitPort, shutdownCts.Token, forceStart: true).ConfigureAwait(false);
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
            var appContext = new KarobarApplicationContext(
                windowManager,
                hostServer.DesktopBridge,
                shutdownCts,
                windowManager.ResolveIconPath());
            var mainForm = new NativeWebView2Form(mainWin, windowManager, hostServer.DesktopBridge, ownsTrayIcon: false);
            appContext.RegisterForm(mainForm);

            // Wire real Windows hardware silent printing (active tab on surviving form OR persistent OffscreenPrintWorker)
            hostServer.DesktopBridge.NativeSilentPrintExecutor = appContext.ExecuteSilentPrintAsync;

            // Route all child-window / openWindow / openSubWindow requests into tabs on the active surviving window
            windowManager.OnOpenTabRequested = (routePath, title) => appContext.OpenTabOnActiveForm(routePath, title);
            windowManager.OnWindowCreated = childWin =>
            {
                if (childWin.Id == mainWin.Id || childWin.IsOffscreenPrintWorker) return;
                appContext.OpenTabOnActiveForm(childWin.RoutePath, childWin.Title);
            };
            windowManager.OnQuitRequested = () =>
            {
                shutdownCts.Cancel();
                appContext.QuitAll();
            };

            mainForm.Show();
            System.Windows.Forms.Application.Run(appContext);
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
            try
            {
                Task.Run(() => hostServer.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(5));
            }
            catch { }
        }
    }

    private static void LogFatalException(string source, Exception ex)
    {
        try
        {
            var appDataLogDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Karobar-App-Native");
            Directory.CreateDirectory(appDataLogDir);
            File.AppendAllText(
                Path.Combine(appDataLogDir, "startup-error.log"),
                $"[{DateTime.UtcNow:O}] [{source}] {ex}{Environment.NewLine}");
        }
        catch { }
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
            var foundHwnd = IntPtr.Zero;
            Win32Native.EnumWindows((hWnd, _) =>
            {
                if (!Win32Native.IsWindowVisible(hWnd)) return true;
                var sb = new System.Text.StringBuilder(256);
                if (Win32Native.GetWindowTextW(hWnd, sb, sb.Capacity) > 0)
                {
                    var title = sb.ToString();
                    if (title.Equals(ApplicationDisplayName, StringComparison.OrdinalIgnoreCase) ||
                        title.EndsWith($"— {ApplicationDisplayName}", StringComparison.OrdinalIgnoreCase) ||
                        title.EndsWith($"- {ApplicationDisplayName}", StringComparison.OrdinalIgnoreCase))
                    {
                        foundHwnd = hWnd;
                        return false;
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (foundHwnd != IntPtr.Zero)
            {
                Win32Native.ShowWindow(foundHwnd, Win32Native.SW_RESTORE);
                Win32Native.SetForegroundWindow(foundHwnd);
                return true;
            }
        }
        catch
        {
            // Ignore Win32 activation errors
        }

        return false;
    }

#if WINDOWS_DESKTOP
    private sealed class KarobarApplicationContext : System.Windows.Forms.ApplicationContext
    {
        private readonly WebView2WindowManager _windowManager;
        private readonly KarobarDesktopBridge _desktopBridge;
        private readonly CancellationTokenSource _shutdownCts;
        private readonly List<NativeWebView2Form> _openForms = new();
        private NativeWebView2Form? _lastActiveForm;
        private System.Windows.Forms.NotifyIcon? _trayIcon;
        private System.Windows.Forms.ContextMenuStrip? _trayMenu;
        private bool _isQuitting;

        public KarobarApplicationContext(
            WebView2WindowManager windowManager,
            KarobarDesktopBridge desktopBridge,
            CancellationTokenSource shutdownCts,
            string? iconPath)
        {
            _windowManager = windowManager;
            _desktopBridge = desktopBridge;
            _shutdownCts = shutdownCts;
            BuildSharedSystemTray(iconPath);
        }

        public void RegisterForm(NativeWebView2Form form)
        {
            if (form.IsDisposed || _openForms.Contains(form)) return;
            _openForms.Add(form);
            _lastActiveForm = form;

            form.DetachedFormCreated += RegisterForm;
            form.Activated += (_, _) => _lastActiveForm = form;
            form.FormClosed += (_, _) =>
            {
                _openForms.Remove(form);
                if (_lastActiveForm == form)
                {
                    _lastActiveForm = _openForms.LastOrDefault(f => !f.IsDisposed);
                }

                if (_openForms.Count == 0)
                {
                    DisposeTrayResources();
                    ExitThread();
                }
            };
        }

        public NativeWebView2Form? GetPreferredActiveForm()
        {
            if (_lastActiveForm is { IsDisposed: false })
            {
                return _lastActiveForm;
            }

            var activeWin = _windowManager.GetActiveWindow();
            if (activeWin is { IsDestroyed: false } && activeWin.Hwnd != IntPtr.Zero)
            {
                var match = _openForms.FirstOrDefault(f => !f.IsDisposed && f.IsHandleCreated && f.Handle == activeWin.Hwnd);
                if (match != null) return match;
            }

            return _openForms.FirstOrDefault(f => !f.IsDisposed);
        }

        public void OpenTabOnActiveForm(string routePath, string? title = null, bool allowDuplicate = false, bool forceSingleton = false)
        {
            var form = GetPreferredActiveForm();
            if (form == null) return;
            form.OpenTabFromExternal(routePath, title, allowDuplicate, forceSingleton);
        }

        public Task<(bool Ok, string? Engine, string? Error)> ExecuteSilentPrintAsync(
            string? targetUrl,
            string? targetHtml,
            string printerName,
            bool isThermal)
        {
            if (!string.IsNullOrWhiteSpace(targetUrl) || !string.IsNullOrWhiteSpace(targetHtml))
            {
                return _windowManager.OffscreenPrintWorker.PrintAsync(targetUrl, targetHtml, printerName, isThermal);
            }

            var activeForm = GetPreferredActiveForm();
            if (activeForm == null || activeForm.IsDisposed)
            {
                return Task.FromResult<(bool, string?, string?)>((false, "webview2-silent-print", "No active window available to print."));
            }

            return activeForm.ExecuteNativeSilentPrintAsync(null, null, printerName, isThermal);
        }

        public void QuitAll()
        {
            if (_isQuitting) return;
            _isQuitting = true;

            void CloseAllOnUi()
            {
                foreach (var form in _openForms.ToList())
                {
                    try
                    {
                        if (!form.IsDisposed)
                        {
                            form.Close();
                        }
                    }
                    catch { }
                }
                DisposeTrayResources();
                try { ExitThread(); } catch { }
                try { System.Windows.Forms.Application.Exit(); } catch { }
            }

            try
            {
                var uiForm = _openForms.FirstOrDefault(f => !f.IsDisposed && f.IsHandleCreated);
                if (uiForm != null && uiForm.InvokeRequired)
                {
                    uiForm.BeginInvoke((Action)CloseAllOnUi);
                }
                else
                {
                    CloseAllOnUi();
                }
            }
            catch
            {
                DisposeTrayResources();
            }
        }

        private void ActivateSurvivingWindow(Action<NativeWebView2Form>? afterShow = null)
        {
            var form = GetPreferredActiveForm();
            if (form == null || form.IsDisposed) return;

            form.Show();
            if (form.WindowState == System.Windows.Forms.FormWindowState.Minimized)
            {
                form.WindowState = System.Windows.Forms.FormWindowState.Normal;
            }
            form.Activate();
            afterShow?.Invoke(form);
        }

        private void BuildSharedSystemTray(string? iconPath)
        {
            try
            {
                _trayMenu = new System.Windows.Forms.ContextMenuStrip
                {
                    BackColor = System.Drawing.Color.FromArgb(30, 41, 59),
                    ForeColor = System.Drawing.Color.FromArgb(248, 250, 252),
                    Renderer = new DarkMenuStripRenderer()
                };
                _trayMenu.Items.Add($"Show {ApplicationDisplayName}", null, (_, _) => ActivateSurvivingWindow());
                _trayMenu.Items.Add("New POS Tab (Ctrl+Shift+P)", null, (_, _) =>
                    ActivateSurvivingWindow(f => f.OpenTabFromExternal("/sale-invoices/pos", "Barcode POS", allowDuplicate: false, forceSingleton: true)));
                _trayMenu.Items.Add("Cube AI GuideBot (Ctrl+G)", null, (_, _) =>
                    ActivateSurvivingWindow(f => f.ToggleGuideBot()));
                _trayMenu.Items.Add("Check for Updates…", null, (_, _) =>
                    ActivateSurvivingWindow(_ => _windowManager.SendTriggerUpdateCheck(manual: true)));
                _trayMenu.Items.Add("Run Backup Now", null, (_, _) =>
                    _ = _desktopBridge.RunBackupNowAsync());
                _trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                _trayMenu.Items.Add($"Quit {ApplicationDisplayName}", null, (_, _) =>
                {
                    _shutdownCts.Cancel();
                    QuitAll();
                });

                System.Drawing.Icon? trayIconImg = null;
                if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath) &&
                    iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
                {
                    try { trayIconImg = new System.Drawing.Icon(iconPath); } catch { }
                }

                _trayIcon = new System.Windows.Forms.NotifyIcon
                {
                    Text = ApplicationDisplayName,
                    Icon = trayIconImg ?? System.Drawing.SystemIcons.Application,
                    ContextMenuStrip = _trayMenu,
                    Visible = true
                };
                _trayIcon.DoubleClick += (_, _) => ActivateSurvivingWindow();
            }
            catch
            {
                // Non-fatal if shell tray is unavailable in headless Windows sessions
            }
        }

        private void DisposeTrayResources()
        {
            if (_trayIcon != null)
            {
                try { _trayIcon.Visible = false; _trayIcon.Dispose(); } catch { }
                _trayIcon = null;
            }
            if (_trayMenu != null)
            {
                try { _trayMenu.Dispose(); } catch { }
                _trayMenu = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeTrayResources();
            }
            base.Dispose(disposing);
        }
    }
#endif
}

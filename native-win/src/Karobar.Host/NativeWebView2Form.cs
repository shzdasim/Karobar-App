#if WINDOWS_DESKTOP
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Karobar.Host;

/// <summary>
/// Single-window Waseela-style Multi-Tab MDI Host powered by Microsoft Edge WebView2.
/// All screens (Dashboard, POS, parallel Retail/Wholesale Sale Invoices, Purchase Invoices,
/// Products, Ledgers, Reports) open as instant-switching tabs inside ONE native window sharing
/// a single CoreWebView2Environment so form state is 100% preserved across tabs.
/// </summary>
public sealed class NativeWebView2Form : Form
{
    private readonly AppWindowInstance _winInstance;
    private readonly WebView2WindowManager _windowManager;
    private readonly KarobarDesktopBridge _desktopBridge;
    private readonly NativeTabWorkspace _workspace = new();
    private readonly NativeTabStripControl _tabStrip;
    private readonly Panel _tabContentPanel;
    private readonly Dictionary<string, WebView2> _tabWebViews = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _envInitLock = new(1, 1);
    private CoreWebView2Environment? _sharedEnv;
    private string _baseOrigin;
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _trayMenu;
    private long _lastTabShortcutMs;
    private string _lastTabShortcutAction = "";

    public NativeWebView2Form(
        AppWindowInstance winInstance,
        WebView2WindowManager windowManager,
        KarobarDesktopBridge desktopBridge)
    {
        _winInstance = winInstance;
        _windowManager = windowManager;
        _desktopBridge = desktopBridge;
        _baseOrigin = ResolveBaseOrigin(winInstance.Url);

        Text = string.IsNullOrWhiteSpace(winInstance.Title) ? Program.ApplicationDisplayName : winInstance.Title;
        Width = winInstance.Width > 0 ? winInstance.Width : 1440;
        Height = winInstance.Height > 0 ? winInstance.Height : 900;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 23, 42); // #0f172a
        KeyPreview = true;

        var iconPath = windowManager.ResolveIconPath();
        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath) && iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Icon = new Icon(iconPath);
            }
            catch
            {
                // Ignore invalid icon format
            }
        }

        if (winInstance.IsMaximized)
        {
            WindowState = FormWindowState.Maximized;
        }

        // WinForms Z-order: Fill panel first, TabStrip second, MenuStrip last (sits at top y=0)
        _tabContentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 23, 42)
        };
        Controls.Add(_tabContentPanel);

        _tabStrip = new NativeTabStripControl(_workspace);
        WireTabStripEvents();
        Controls.Add(_tabStrip);

        BuildNativeMenuBar();
        if (winInstance.IsMain)
        {
            BuildSystemTrayIcon(iconPath);
        }

        // Route windowManager messages & navigation to the active tab's WebView2
        _winInstance.WebMessageSender = BroadcastMessageToActiveTab;
        _winInstance.UrlNavigator = targetUrl =>
        {
            if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri) &&
                (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
            {
                _baseOrigin = $"{uri.Scheme}://{uri.Authority}";
            }
            OpenOrNavigateTabFromUrl(targetUrl, openNewTab: false);
        };

        Load += async (_, _) => await InitializeInitialTabAsync();
        Activated += (_, _) => _winInstance.IsFocused = true;
        Deactivate += (_, _) => _winInstance.IsFocused = false;
        FormClosed += (_, _) =>
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
            foreach (var wv in _tabWebViews.Values)
            {
                try { wv.Dispose(); } catch { }
            }
            _tabWebViews.Clear();
            try { _envInitLock.Dispose(); } catch { }
            _windowManager.CloseWindow(_winInstance.Id);
        };
    }

    private void BuildSystemTrayIcon(string? iconPath)
    {
        try
        {
            _trayMenu = new ContextMenuStrip
            {
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.FromArgb(248, 250, 252),
                Renderer = new DarkMenuStripRenderer()
            };
            _trayMenu.Items.Add($"Show {Program.ApplicationDisplayName}", null, (_, _) =>
            {
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
            });
            _trayMenu.Items.Add("New POS Tab (Ctrl+Shift+P)", null, (_, _) =>
            {
                Show();
                Activate();
                _ = OpenTabAsync("/sale-invoices/pos", "Barcode POS", allowDuplicate: false);
            });
            _trayMenu.Items.Add("Check for Updates…", null, (_, _) =>
            {
                Show();
                Activate();
                _ = _desktopBridge.CheckForUpdatesAsync(null);
            });
            _trayMenu.Items.Add("Run Backup Now", null, (_, _) =>
            {
                _ = _desktopBridge.RunBackupNowAsync();
            });
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add($"Quit {Program.ApplicationDisplayName}", null, (_, _) =>
            {
                try { if (_trayIcon != null) _trayIcon.Visible = false; } catch { }
                Application.Exit();
            });

            _trayIcon = new NotifyIcon
            {
                Text = Program.ApplicationDisplayName,
                Icon = Icon ?? (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath) && iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
                    ? new Icon(iconPath)
                    : SystemIcons.Application),
                ContextMenuStrip = _trayMenu,
                Visible = true
            };
            _trayIcon.DoubleClick += (_, _) =>
            {
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
            };
        }
        catch
        {
            // Non-fatal if shell tray is unavailable in headless Windows sessions
        }
    }

    private void WireTabStripEvents()
    {
        _tabStrip.TabSelected += tabId => ActivateTab(tabId);
        _tabStrip.TabCloseRequested += tabId => CloseTabById(tabId);
        _tabStrip.NewTabRequested += () => _ = OpenTabAsync("/dashboard", "Dashboard", allowDuplicate: true);
        _tabStrip.QuickOpenRequested += (route, title, allowDup) => _ = OpenTabAsync(route, title, allowDuplicate: allowDup);
        _tabStrip.TabReloadRequested += tabId =>
        {
            if (_tabWebViews.TryGetValue(tabId, out var wv) && wv.CoreWebView2 != null)
            {
                wv.CoreWebView2.Reload();
            }
        };
        _tabStrip.TabDuplicateRequested += tabId =>
        {
            var tab = _workspace.Tabs.FirstOrDefault(t => t.TabId == tabId);
            if (tab != null)
            {
                _ = OpenTabAsync(tab.RoutePath, tab.Title, allowDuplicate: true);
            }
        };
        _tabStrip.TabPinToggled += tabId =>
        {
            _workspace.TogglePin(tabId);
            _tabStrip.RefreshTabs();
        };
        _tabStrip.CloseOthersRequested += keepId =>
        {
            var removed = _workspace.CloseOtherTabs(keepId);
            DisposeTabControls(removed);
            SyncActiveTabVisuals();
        };
        _tabStrip.CloseToRightRequested += fromId =>
        {
            var removed = _workspace.CloseTabsToRight(fromId);
            DisposeTabControls(removed);
            SyncActiveTabVisuals();
        };
        _tabStrip.ReopenClosedRequested += () =>
        {
            var restored = _workspace.ReopenLastClosedTab();
            if (restored != null)
            {
                _ = EnsureTabWebViewInitializedAsync(restored);
                SyncActiveTabVisuals();
            }
        };
        _tabStrip.DetachTabRequested += tabId => DetachTabToSeparateWindow(tabId);
    }

    private void BuildNativeMenuBar()
    {
        var darkRenderer = new DarkMenuStripRenderer();
        var menuStrip = new MenuStrip
        {
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.FromArgb(248, 250, 252),
            Renderer = darkRenderer
        };

        var sections = _windowManager.GetApplicationMenuSections();
        foreach (var section in sections)
        {
            var topItem = new ToolStripMenuItem(section.Key)
            {
                ForeColor = Color.FromArgb(248, 250, 252),
                BackColor = Color.FromArgb(15, 23, 42)
            };
            topItem.DropDown.BackColor = Color.FromArgb(30, 41, 59);
            topItem.DropDown.ForeColor = Color.FromArgb(248, 250, 252);
            topItem.DropDown.Renderer = darkRenderer;

            foreach (var item in section.Value)
            {
                var route = item.Path;
                var label = item.Label;
                var cleanLabel = label.StartsWith("Quick Window:", StringComparison.OrdinalIgnoreCase)
                    ? label.Replace("Quick Window:", "").Trim()
                    : label;
                var subItem = new ToolStripMenuItem(cleanLabel)
                {
                    ForeColor = Color.FromArgb(248, 250, 252),
                    BackColor = Color.FromArgb(30, 41, 59)
                };
                subItem.Click += (_, _) =>
                {
                    // Every menu selection opens cleanly as a Waseela-style tab inside THIS window
                    _ = OpenTabAsync(route, cleanLabel, allowDuplicate: false);
                };
                topItem.DropDownItems.Add(subItem);
            }

            menuStrip.Items.Add(topItem);
        }

        // Dedicated "Tabs" menu for discoverable Waseela keyboard multitasking
        var tabsMenu = new ToolStripMenuItem("Tabs")
        {
            ForeColor = Color.FromArgb(248, 250, 252),
            BackColor = Color.FromArgb(15, 23, 42)
        };
        tabsMenu.DropDown.BackColor = Color.FromArgb(30, 41, 59);
        tabsMenu.DropDown.ForeColor = Color.FromArgb(248, 250, 252);
        tabsMenu.DropDown.Renderer = darkRenderer;

        void AddTabMenuItem(string text, Action onClick)
        {
            var mi = new ToolStripMenuItem(text)
            {
                ForeColor = Color.FromArgb(248, 250, 252),
                BackColor = Color.FromArgb(30, 41, 59)
            };
            mi.Click += (_, _) => onClick();
            tabsMenu.DropDownItems.Add(mi);
        }

        AddTabMenuItem("Next Tab (Ctrl+Tab / Ctrl+PageDown)", () => ExecuteTabShortcutAction("cycle-next"));
        AddTabMenuItem("Previous Tab (Ctrl+Shift+Tab / Ctrl+PageUp)", () => ExecuteTabShortcutAction("cycle-prev"));
        AddTabMenuItem("Jump to Tab 1–9 (Alt+1..9 / Ctrl+Shift+1..9)", () => ExecuteTabShortcutAction("cycle-next"));
        tabsMenu.DropDownItems.Add(new ToolStripSeparator());
        AddTabMenuItem("New Tab (Ctrl+T / Ctrl+Shift+N)", () => ExecuteTabShortcutAction("new-tab"));
        AddTabMenuItem("New Purchase Invoice Tab (Ctrl+Shift+G)", () => ExecuteTabShortcutAction("new-purchase"));
        AddTabMenuItem("Open Barcode POS Tab (Ctrl+Shift+P)", () => ExecuteTabShortcutAction("open-pos"));
        AddTabMenuItem("Open Customer Ledger Tab (Ctrl+Shift+L)", () => ExecuteTabShortcutAction("open-ledger"));
        tabsMenu.DropDownItems.Add(new ToolStripSeparator());
        AddTabMenuItem("Duplicate Active Tab (Ctrl+Shift+D)", () => ExecuteTabShortcutAction("duplicate"));
        AddTabMenuItem("Close Active Tab (Ctrl+F4 / Ctrl+Shift+Q)", () => ExecuteTabShortcutAction("close-active"));
        AddTabMenuItem("Close Other Tabs (Ctrl+Shift+X)", () => ExecuteTabShortcutAction("close-others"));
        AddTabMenuItem("Reopen Closed Tab (Ctrl+Shift+T)", () => ExecuteTabShortcutAction("reopen-closed"));
        menuStrip.Items.Add(tabsMenu);

        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);
    }

    private async Task InitializeInitialTabAsync()
    {
        _winInstance.Hwnd = Handle;
        var initialRoute = NativeTabWorkspace.NormalizeRoutePath(_winInstance.RoutePath);
        var (tab, _) = _workspace.OpenTab(initialRoute, NativeTabWorkspace.ResolveTitleForRoute(initialRoute), allowDuplicate: true, focus: true, pinned: initialRoute == "/dashboard");
        await EnsureTabWebViewInitializedAsync(tab, explicitUrl: _winInstance.Url);
        SyncActiveTabVisuals();
    }

    /// <summary>
    /// Thread-safe entrypoint used by Program.cs and IPC handlers so "openWindow" / "openSubWindow"
    /// opens a Waseela tab inside THIS single window instead of spawning a second OS window.
    /// </summary>
    public void OpenTabFromExternal(string routePath, string? title = null, bool allowDuplicate = false)
    {
        if (IsDisposed) return;

        void DoOpen()
        {
            if (IsDisposed) return;
            _ = OpenTabAsync(routePath, title, allowDuplicate);
            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }
            Activate();
        }

        if (InvokeRequired)
        {
            BeginInvoke((Action)DoOpen);
        }
        else
        {
            DoOpen();
        }
    }

    public async Task OpenTabAsync(string routePath, string? title = null, bool allowDuplicate = false)
    {
        if (IsDisposed) return;

        var (tab, createdNew) = _workspace.OpenTab(routePath, title, allowDuplicate: allowDuplicate, focus: true);
        if (createdNew || !_tabWebViews.ContainsKey(tab.TabId))
        {
            await EnsureTabWebViewInitializedAsync(tab);
        }
        else if (_tabWebViews.TryGetValue(tab.TabId, out var existingWv) && existingWv.CoreWebView2 != null)
        {
            // If caller navigated to a different query string on the same singleton route, update it
            var targetUrl = BuildAbsoluteUrl(tab.RoutePath);
            if (!string.Equals(existingWv.CoreWebView2.Source, targetUrl, StringComparison.OrdinalIgnoreCase) &&
                tab.RoutePath.Contains('?'))
            {
                existingWv.CoreWebView2.Navigate(targetUrl);
            }
        }

        SyncActiveTabVisuals();
    }

    private void OpenOrNavigateTabFromUrl(string targetUrl, bool openNewTab)
    {
        if (IsDisposed) return;

        void Run()
        {
            if (IsDisposed) return;
            var route = NativeTabWorkspace.NormalizeRoutePath(targetUrl);
            if (openNewTab)
            {
                _ = OpenTabAsync(route, null, allowDuplicate: false);
                return;
            }

            if (_workspace.ActiveTabId != null &&
                _tabWebViews.TryGetValue(_workspace.ActiveTabId, out var wv) &&
                wv.CoreWebView2 != null)
            {
                wv.CoreWebView2.Navigate(targetUrl);
            }
            else
            {
                _ = OpenTabAsync(route, null, allowDuplicate: false);
            }
        }

        if (InvokeRequired) BeginInvoke((Action)Run);
        else Run();
    }

    private void ActivateTab(string tabId)
    {
        if (_workspace.FocusTab(tabId))
        {
            SyncActiveTabVisuals();
        }
    }

    private void CloseTabById(string tabId)
    {
        if (_workspace.CloseTab(tabId))
        {
            DisposeTabControls([tabId]);
            SyncActiveTabVisuals();
        }
    }

    private void DuplicateActiveTab()
    {
        var active = _workspace.ActiveTab;
        if (active != null)
        {
            _ = OpenTabAsync(active.RoutePath, active.Title, allowDuplicate: true);
        }
    }

    private void DetachTabToSeparateWindow(string tabId)
    {
        var tab = _workspace.Tabs.FirstOrDefault(t => t.TabId == tabId);
        if (tab == null) return;

        var detachedWin = new AppWindowInstance(
            id: Random.Shared.Next(10000, 99999),
            title: tab.Title,
            routePath: tab.RoutePath,
            url: BuildAbsoluteUrl(tab.RoutePath),
            width: 1360,
            height: 860);

        var detachedForm = new NativeWebView2Form(detachedWin, _windowManager, _desktopBridge);
        detachedForm.Show();

        if (_workspace.Tabs.Count > 1 && !tab.IsPinned)
        {
            CloseTabById(tabId);
        }
    }

    private void DisposeTabControls(IEnumerable<string> tabIds)
    {
        foreach (var id in tabIds)
        {
            if (_tabWebViews.Remove(id, out var wv))
            {
                try
                {
                    _tabContentPanel.Controls.Remove(wv);
                    wv.Dispose();
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    private void SyncActiveTabVisuals()
    {
        if (IsDisposed) return;

        var activeId = _workspace.ActiveTabId;
        foreach (var kv in _tabWebViews)
        {
            var isThisActive = kv.Key == activeId;
            if (kv.Value.Visible != isThisActive)
            {
                kv.Value.Visible = isThisActive;
            }
            if (isThisActive)
            {
                kv.Value.BringToFront();
                kv.Value.Focus();
            }
        }

        var activeTab = _workspace.ActiveTab;
        if (activeTab != null)
        {
            Text = $"{activeTab.Title} — {Program.ApplicationDisplayName}";
            _winInstance.Title = Text;
            _winInstance.RoutePath = activeTab.RoutePath;
        }

        _tabStrip.RefreshTabs();
    }

    private async Task<CoreWebView2Environment> GetOrCreateSharedEnvironmentAsync()
    {
        if (_sharedEnv != null) return _sharedEnv;

        await _envInitLock.WaitAsync();
        try
        {
            if (_sharedEnv != null) return _sharedEnv;
            _sharedEnv = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _windowManager.SharedWebView2UserDataFolder,
                options: new CoreWebView2EnvironmentOptions("--disable-features=msSmartScreenProtection"));
            return _sharedEnv;
        }
        finally
        {
            _envInitLock.Release();
        }
    }

    private async Task EnsureTabWebViewInitializedAsync(WorkspaceTabRecord tab, string? explicitUrl = null)
    {
        if (IsDisposed) return;

        try
        {
            if (!_tabWebViews.TryGetValue(tab.TabId, out var wv))
            {
                wv = new WebView2
                {
                    Dock = DockStyle.Fill,
                    DefaultBackgroundColor = Color.FromArgb(15, 23, 42),
                    Visible = tab.TabId == _workspace.ActiveTabId
                };
                // Intercept Waseela tab-switching accelerators directly from WebView2 (CoreWebView2Controller.AcceleratorKeyPressed)
                // so Ctrl+Tab, Ctrl+Shift+Tab, Ctrl+PageUp/Down, Ctrl+F6, Alt+1..9, Ctrl+Shift+1..9, Ctrl+F4 work even while typing in React forms!
                wv.KeyDown += (_, keyArgs) =>
                {
                    if (HandleTabKeyData(keyArgs.KeyData))
                    {
                        keyArgs.Handled = true;
                        keyArgs.SuppressKeyPress = true;
                    }
                };
                _tabWebViews[tab.TabId] = wv;
                _tabContentPanel.Controls.Add(wv);
            }

            var env = await GetOrCreateSharedEnvironmentAsync();
            await wv.EnsureCoreWebView2Async(env);

            var core = wv.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;

            var preloadJs = "window.__karobarUseNativeWebView2PostMessage = true;\n" +
                            KarobarPreloadScript.GetPreloadScript(_winInstance.IsOffscreenPrintWorker);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(preloadJs);

            core.DocumentTitleChanged += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(core.DocumentTitle))
                {
                    _workspace.UpdateTabMetadata(tab.TabId, core.DocumentTitle, null);
                    if (tab.TabId == _workspace.ActiveTabId)
                    {
                        Text = $"{tab.Title} — {Program.ApplicationDisplayName}";
                    }
                    _tabStrip.RefreshTabs();
                }
            };

            core.SourceChanged += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(core.Source))
                {
                    _workspace.UpdateTabMetadata(tab.TabId, null, core.Source);
                    _tabStrip.RefreshTabs();
                }
            };

            core.NewWindowRequested += (_, e) =>
            {
                var decision = _windowManager.HandleWindowOpenRequest(
                    e.Uri,
                    e.Name,
                    onSilentPrintIntercepted: async printUrl =>
                    {
                        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(new { url = printUrl, silent = true }));
                        await _desktopBridge.PrintUrlAsync(doc.RootElement);
                    });

                if (decision == "allow-preview")
                {
                    return;
                }

                e.Handled = true;
                // HandleWindowOpenRequest already handled silent print ("deny-silent-print")
                // and routed internal app links via CreateAppWindow -> OpenTabFromExternal.
            };

            core.WebMessageReceived += async (_, e) =>
            {
                try
                {
                    var raw = e.TryGetWebMessageAsString();
                    if (string.IsNullOrWhiteSpace(raw)) return;

                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("type", out var msgTypeProp) &&
                        msgTypeProp.GetString() == "tab-shortcut" &&
                        root.TryGetProperty("action", out var actProp))
                    {
                        var action = actProp.GetString() ?? "";
                        var index = root.TryGetProperty("index", out var idxProp) && idxProp.TryGetInt32(out var idxVal) ? idxVal : -1;
                        if (InvokeRequired)
                        {
                            BeginInvoke((Action)(() => { ExecuteTabShortcutAction(action, index); }));
                        }
                        else
                        {
                            ExecuteTabShortcutAction(action, index);
                        }
                        return;
                    }

                    if (root.TryGetProperty("type", out var typeProp) &&
                        typeProp.GetString() == "ipc-invoke" &&
                        root.TryGetProperty("id", out var idProp) &&
                        root.TryGetProperty("channel", out var chanProp))
                    {
                        var id = idProp.GetString() ?? "";
                        var channel = chanProp.GetString() ?? "";
                        JsonElement? payload = root.TryGetProperty("payload", out var p) ? p.Clone() : null;

                        try
                        {
                            var result = await _desktopBridge.DispatchIpcAsync(channel, payload);
                            var responseJson = JsonSerializer.Serialize(new
                            {
                                type = "ipc-response",
                                id,
                                result
                            });
                            if (!IsDisposed && wv.CoreWebView2 != null)
                            {
                                wv.CoreWebView2.PostWebMessageAsString(responseJson);
                            }
                        }
                        catch (Exception ex)
                        {
                            var errJson = JsonSerializer.Serialize(new
                            {
                                type = "ipc-response",
                                id,
                                error = ex.Message
                            });
                            if (!IsDisposed && wv.CoreWebView2 != null)
                            {
                                wv.CoreWebView2.PostWebMessageAsString(errJson);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore malformed WebMessage
                }
            };

            core.NavigationCompleted += (_, _) =>
            {
                _windowManager.CloseSplashWindow();
            };

            var navUrl = !string.IsNullOrWhiteSpace(explicitUrl) ? explicitUrl : BuildAbsoluteUrl(tab.RoutePath);
            core.Navigate(navUrl);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to initialize Microsoft Edge WebView2 Runtime:\n{ex.Message}\n\nPlease ensure Microsoft Edge WebView2 Runtime is installed on Windows.",
                Program.ApplicationDisplayName,
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void BroadcastMessageToActiveTab(string json)
    {
        if (IsDisposed) return;

        void Send()
        {
            if (IsDisposed) return;
            var activeId = _workspace.ActiveTabId;
            if (activeId != null && _tabWebViews.TryGetValue(activeId, out var wv) && wv.CoreWebView2 != null)
            {
                try { wv.CoreWebView2.PostWebMessageAsString(json); } catch { }
            }
        }

        if (InvokeRequired) BeginInvoke((Action)Send);
        else Send();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (HandleTabKeyData(keyData))
        {
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool HandleTabKeyData(Keys keyData)
    {
        // Waseela & Karobar conflict-free Tab Multitasking Shortcuts
        // (Preserves Ctrl+1/2/3, Ctrl+W, Ctrl+D, Ctrl+R, Ctrl+S, F4/F6/F7/F8/F9 for React invoicing screens!)
        if (keyData == Keys.F5)
        {
            return ExecuteTabShortcutAction("reload-active");
        }
        if (keyData == (Keys.Control | Keys.Tab) ||
            keyData == (Keys.Control | Keys.PageDown) ||
            keyData == (Keys.Control | Keys.OemCloseBrackets) ||
            keyData == (Keys.Control | Keys.F6))
        {
            return ExecuteTabShortcutAction("cycle-next");
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab) ||
            keyData == (Keys.Control | Keys.PageUp) ||
            keyData == (Keys.Control | Keys.OemOpenBrackets) ||
            keyData == (Keys.Control | Keys.Shift | Keys.F6))
        {
            return ExecuteTabShortcutAction("cycle-prev");
        }
        if (keyData == (Keys.Control | Keys.F4) || keyData == (Keys.Control | Keys.Shift | Keys.Q))
        {
            return ExecuteTabShortcutAction("close-active");
        }
        if (keyData == (Keys.Control | Keys.T) || keyData == (Keys.Control | Keys.Shift | Keys.N))
        {
            return ExecuteTabShortcutAction("new-tab");
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.T))
        {
            return ExecuteTabShortcutAction("reopen-closed");
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.X))
        {
            return ExecuteTabShortcutAction("close-others");
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.D))
        {
            return ExecuteTabShortcutAction("duplicate");
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.P))
        {
            return ExecuteTabShortcutAction("open-pos");
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.G))
        {
            return ExecuteTabShortcutAction("new-purchase");
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.L))
        {
            return ExecuteTabShortcutAction("open-ledger");
        }

        // Alt+1..8 / Ctrl+Shift+1..8 jump directly to tab index; Alt+9 / Ctrl+Shift+9 jump to last tab
        var isAltOnly = (keyData & Keys.Alt) == Keys.Alt && (keyData & (Keys.Control | Keys.Shift)) == 0;
        var isCtrlShiftOnly = (keyData & (Keys.Control | Keys.Shift)) == (Keys.Control | Keys.Shift) && (keyData & Keys.Alt) == 0;
        if (isAltOnly || isCtrlShiftOnly)
        {
            var keyCode = keyData & Keys.KeyCode;
            if (keyCode is >= Keys.D1 and <= Keys.D8)
            {
                return ExecuteTabShortcutAction("focus-index", (int)(keyCode - Keys.D1));
            }
            if (keyCode is >= Keys.NumPad1 and <= Keys.NumPad8)
            {
                return ExecuteTabShortcutAction("focus-index", (int)(keyCode - Keys.NumPad1));
            }
            if (keyCode == Keys.D9 || keyCode == Keys.NumPad9)
            {
                return ExecuteTabShortcutAction("focus-last");
            }
        }

        return false;
    }

    private bool ExecuteTabShortcutAction(string action, int index = -1)
    {
        if (IsDisposed || string.IsNullOrWhiteSpace(action)) return false;

        // Dedupe if both WebView2 KeyDown (AcceleratorKeyPressed) and DOM keydown fire within 80ms
        var nowMs = Environment.TickCount64;
        var dedupeKey = $"{action}:{index}";
        if (dedupeKey == _lastTabShortcutAction && nowMs - _lastTabShortcutMs < 80)
        {
            return true;
        }
        _lastTabShortcutAction = dedupeKey;
        _lastTabShortcutMs = nowMs;

        switch (action)
        {
            case "reload-active":
                if (_workspace.ActiveTabId != null) ReloadTab(_workspace.ActiveTabId);
                return true;
            case "cycle-next":
                if (_workspace.CycleTab(1) != null) SyncActiveTabVisuals();
                return true;
            case "cycle-prev":
                if (_workspace.CycleTab(-1) != null) SyncActiveTabVisuals();
                return true;
            case "focus-index":
                if (index >= 0 && _workspace.FocusByIndex(index) != null) SyncActiveTabVisuals();
                return true;
            case "focus-last":
                if (_workspace.FocusLastTab() != null) SyncActiveTabVisuals();
                return true;
            case "new-tab":
                _ = OpenTabAsync("/dashboard", "Dashboard", allowDuplicate: true);
                return true;
            case "close-active":
                if (_workspace.ActiveTabId != null) CloseTabById(_workspace.ActiveTabId);
                return true;
            case "close-others":
                if (_workspace.ActiveTabId != null)
                {
                    DisposeTabControls(_workspace.CloseOtherTabs(_workspace.ActiveTabId));
                    SyncActiveTabVisuals();
                }
                return true;
            case "reopen-closed":
                var restored = _workspace.ReopenLastClosedTab();
                if (restored != null)
                {
                    _ = EnsureTabWebViewInitializedAsync(restored);
                    SyncActiveTabVisuals();
                }
                return true;
            case "duplicate":
                DuplicateActiveTab();
                return true;
            case "open-pos":
                _ = OpenTabAsync("/sale-invoices/pos", "Barcode POS", allowDuplicate: false);
                return true;
            case "new-purchase":
                _ = OpenTabAsync("/purchase-invoices/create", "Purchase Invoice", allowDuplicate: true);
                return true;
            case "open-ledger":
                _ = OpenTabAsync("/customer-ledger", "Customer Ledger", allowDuplicate: false);
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Executes real Windows hardware silent printing through Microsoft Edge WebView2
    /// (CoreWebView2.PrintAsync) for both thermal POS receipts and A4 documents, with zero OS dialog.
    /// When targetUrl &amp; targetHtml are null, prints the currently active tab.
    /// </summary>
    public Task<(bool Ok, string? Engine, string? Error)> ExecuteNativeSilentPrintAsync(
        string? targetUrl,
        string? targetHtml,
        string printerName,
        bool isThermal)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return Task.FromResult<(bool, string?, string?)>((false, "webview2-silent-print", "Main window is not active."));
        }

        var tcs = new TaskCompletionSource<(bool Ok, string? Engine, string? Error)>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task RunOnUiThreadAsync()
        {
            Form? offscreenForm = null;
            try
            {
                CoreWebView2? coreToPrint = null;

                if (string.IsNullOrWhiteSpace(targetUrl) && string.IsNullOrWhiteSpace(targetHtml))
                {
                    if (_workspace.ActiveTabId != null &&
                        _tabWebViews.TryGetValue(_workspace.ActiveTabId, out var activeWv) &&
                        activeWv.CoreWebView2 != null)
                    {
                        coreToPrint = activeWv.CoreWebView2;
                    }
                    else
                    {
                        tcs.TrySetResult((false, "webview2-silent-print", "No active tab available to print."));
                        return;
                    }
                }
                else
                {
                    offscreenForm = new OffscreenPrintForm(isThermal ? 380 : 820, 900);

                    var printWv = new WebView2
                    {
                        Dock = DockStyle.Fill,
                        DefaultBackgroundColor = Color.White
                    };
                    offscreenForm.Controls.Add(printWv);
                    offscreenForm.Show();

                    var env = await GetOrCreateSharedEnvironmentAsync();
                    await printWv.EnsureCoreWebView2Async(env);
                    coreToPrint = printWv.CoreWebView2;

                    await coreToPrint.AddScriptToExecuteOnDocumentCreatedAsync(
                        "window.__karobarSilentPrintWorker = true; window.print = function(){}; window.close = function(){};");

                    var navTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    void OnNavCompleted(object? s, CoreWebView2NavigationCompletedEventArgs args)
                    {
                        navTcs.TrySetResult(args.IsSuccess);
                    }

                    coreToPrint.NavigationCompleted += OnNavCompleted;
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(targetUrl))
                        {
                            coreToPrint.Navigate(targetUrl);
                        }
                        else
                        {
                            coreToPrint.NavigateToString(targetHtml!);
                        }

                        var completed = await Task.WhenAny(navTcs.Task, Task.Delay(15000));
                        if (completed != navTcs.Task)
                        {
                            tcs.TrySetResult((false, "webview2-silent-print", "Timed out waiting for receipt page to load before printing."));
                            return;
                        }
                        if (!await navTcs.Task)
                        {
                            tcs.TrySetResult((false, "webview2-silent-print", "Failed to load print document (navigation error or HTTP 403/404/500)."));
                            return;
                        }
                    }
                    finally
                    {
                        coreToPrint.NavigationCompleted -= OnNavCompleted;
                    }

                    try
                    {
                        await coreToPrint.ExecuteScriptAsync("document.fonts ? document.fonts.ready : Promise.resolve()");
                    }
                    catch { }
                    await Task.Delay(250);
                }

                var result = await PrintCoreWebViewAsync(coreToPrint, printerName, isThermal);
                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetResult((false, "webview2-silent-print", ex.Message));
            }
            finally
            {
                if (offscreenForm != null)
                {
                    var formToDispose = offscreenForm;
                    _ = Task.Delay(3000).ContinueWith(_ =>
                    {
                        try
                        {
                            if (!IsDisposed && IsHandleCreated)
                            {
                                BeginInvoke((Action)(() =>
                                {
                                    try
                                    {
                                        if (!formToDispose.IsDisposed)
                                        {
                                            formToDispose.Close();
                                            formToDispose.Dispose();
                                        }
                                    }
                                    catch { }
                                }));
                            }
                        }
                        catch { }
                    });
                }
            }
        }

        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => { _ = RunOnUiThreadAsync(); }));
        }
        else
        {
            _ = RunOnUiThreadAsync();
        }

        return tcs.Task;
    }

    private static async Task<(bool Ok, string? Engine, string? Error)> PrintCoreWebViewAsync(
        CoreWebView2 core,
        string printerName,
        bool isThermal)
    {
        var settings = core.Environment.CreatePrintSettings();
        settings.PrinterName = printerName;
        settings.Copies = 1;
        settings.Orientation = CoreWebView2PrintOrientation.Portrait;
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.ShouldPrintSelectionOnly = false;
        if (isThermal)
        {
            settings.MarginTop = 0;
            settings.MarginBottom = 0;
            settings.MarginLeft = 0;
            settings.MarginRight = 0;
            settings.ScaleFactor = 1.0;

            try
            {
                var metaRaw = await core.ExecuteScriptAsync(
                    "(function(){var m=document.querySelector('meta[name=\"karobar-print-config\"]');return m?m.getAttribute('content'):'';})()");
                if (!string.IsNullOrWhiteSpace(metaRaw) && metaRaw != "null" && metaRaw != "\"\"")
                {
                    var unescaped = JsonSerializer.Deserialize<string>(metaRaw);
                    if (!string.IsNullOrWhiteSpace(unescaped))
                    {
                        using var cfgDoc = JsonDocument.Parse(unescaped);
                        if (cfgDoc.RootElement.TryGetProperty("paperWidthMm", out var pwEl) && pwEl.TryGetDouble(out var pwMm) && pwMm is >= 25 and <= 216)
                        {
                            settings.PageWidth = pwMm / 25.4;
                        }
                        if (cfgDoc.RootElement.TryGetProperty("copies", out var cpEl) && cpEl.TryGetInt32(out var copies) && copies is >= 1 and <= 20)
                        {
                            settings.Copies = copies;
                        }
                    }
                }
            }
            catch { }
        }

        CoreWebView2PrintStatus status;
        try
        {
            status = await core.PrintAsync(settings);
        }
        catch (ArgumentException)
        {
            // Fallback for thermal drivers that reject explicit 0 margins or custom scale factor
            var fallback = core.Environment.CreatePrintSettings();
            fallback.PrinterName = printerName;
            fallback.ShouldPrintBackgrounds = true;
            fallback.ShouldPrintHeaderAndFooter = false;
            status = await core.PrintAsync(fallback);
        }

        if (status == CoreWebView2PrintStatus.Succeeded)
        {
            return (true, "webview2-silent-print", null);
        }

        var reason = status == CoreWebView2PrintStatus.PrinterUnavailable
            ? $"Printer '{printerName}' is offline, disconnected, or unavailable in Windows."
            : $"Windows printer '{printerName}' failed to print (status: {status}).";
        return (false, "webview2-silent-print", reason);
    }

    private string BuildAbsoluteUrl(string routePath)
    {
        var norm = NativeTabWorkspace.NormalizeRoutePath(routePath);
        return _baseOrigin.TrimEnd('/') + norm;
    }

    private static string ResolveBaseOrigin(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return $"{uri.Scheme}://{uri.Authority}";
        }
        return $"http://127.0.0.1:{NetworkConfigManager.DefaultPort}";
    }
}

/// <summary>
/// Dark-slate WinForms MenuStrip / ContextMenuStrip renderer so open, hovered, and pressed
/// menu items &amp; dropdowns never flash white-on-white.
/// </summary>
internal sealed class DarkMenuStripRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color TextNormal = Color.FromArgb(248, 250, 252);
    private static readonly Color TextDisabled = Color.FromArgb(148, 163, 184);

    public DarkMenuStripRenderer() : base(new DarkMenuColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? TextNormal : TextDisabled;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = TextNormal;
        base.OnRenderArrow(e);
    }
}

internal sealed class DarkMenuColorTable : ProfessionalColorTable
{
    private static readonly Color BarBg = Color.FromArgb(15, 23, 42);        // #0f172a
    private static readonly Color DropdownBg = Color.FromArgb(30, 41, 59);   // #1e293b
    private static readonly Color HoverBg = Color.FromArgb(51, 65, 85);      // #334155
    private static readonly Color BorderColor = Color.FromArgb(71, 85, 105); // #475569
    private static readonly Color AccentBorder = Color.FromArgb(56, 189, 248);

    public override Color MenuStripGradientBegin => BarBg;
    public override Color MenuStripGradientEnd => BarBg;
    public override Color ToolStripDropDownBackground => DropdownBg;
    public override Color ImageMarginGradientBegin => DropdownBg;
    public override Color ImageMarginGradientMiddle => DropdownBg;
    public override Color ImageMarginGradientEnd => DropdownBg;
    public override Color MenuBorder => BorderColor;
    public override Color MenuItemBorder => AccentBorder;
    public override Color MenuItemSelected => HoverBg;
    public override Color MenuItemSelectedGradientBegin => HoverBg;
    public override Color MenuItemSelectedGradientEnd => HoverBg;
    public override Color MenuItemPressedGradientBegin => DropdownBg;
    public override Color MenuItemPressedGradientMiddle => DropdownBg;
    public override Color MenuItemPressedGradientEnd => DropdownBg;
    public override Color SeparatorDark => BorderColor;
    public override Color SeparatorLight => DropdownBg;
}

/// <summary>
/// Non-activating offscreen Form for silent WebView2 hardware printing so printing a receipt
/// never steals focus from the cashier's active POS input or flashes on the taskbar.
/// </summary>
internal sealed class OffscreenPrintForm : Form
{
    public OffscreenPrintForm(int width, int height)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(width, height);
        Opacity = 0.0;
        BackColor = Color.White;
    }

    protected override bool ShowWithoutActivation => true;
}

/// <summary>
/// Frameless native Windows Splash Form shown for &lt;1 second while Kestrel &amp; WebView2 initialize.
/// </summary>
public sealed class NativeSplashForm : Form
{
    public NativeSplashForm(string? iconPath)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 480;
        Height = 280;
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(15, 23, 42);

        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath) && iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            try { Icon = new Icon(iconPath); } catch { }
        }

        var titleLabel = new Label
        {
            Text = "Karobar-App Native",
            ForeColor = Color.FromArgb(248, 250, 252),
            Font = new Font("Segoe UI", 22F, FontStyle.Bold),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 130,
            Padding = new Padding(0, 45, 0, 0)
        };

        var subLabel = new Label
        {
            Text = "Starting Native Windows C# / .NET 9 Engine…",
            ForeColor = Color.FromArgb(56, 189, 248),
            Font = new Font("Segoe UI", 10.5F, FontStyle.Regular),
            AutoSize = false,
            TextAlign = ContentAlignment.TopCenter,
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 12, 0, 0)
        };

        Controls.Add(subLabel);
        Controls.Add(titleLabel);
    }
}
#endif

#if WINDOWS_DESKTOP
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text.Json;
using System.Windows.Forms;
using Karobar.Host.GuideBot;
using Karobar.Host.Mascot;
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
    private string _baseOrigin;
    private NotifyIcon? _trayIcon;
    private ContextMenuStrip? _trayMenu;
    private GuideBotSessionController? _guideSession;
    private NativeGuideBotDrawer? _guideDrawer;
    private NativeMascotOverlayForm? _mascotOverlay;
    private GuideBotOutsideClickFilter? _outsideClickFilter;
    private Karobar.Background.CubeBackgroundTaskManager? _subscribedBgTaskManager;
    private Action<Karobar.Background.CubeBackgroundTaskSnapshot>? _bgStateChangedHandler;
    private Action? _productsMutatedHandler;
    private Action? _bridgeToggleGuideBotHandler;
    private Action<string?>? _bridgeOpenGuideBotHandler;
    private long _lastGuideToggleMs;
    private long _lastTabShortcutMs;
    private string _lastTabShortcutAction = "";

    public event Action<NativeWebView2Form>? DetachedFormCreated;

    public NativeWebView2Form(
        AppWindowInstance winInstance,
        WebView2WindowManager windowManager,
        KarobarDesktopBridge desktopBridge,
        bool ownsTrayIcon = false)
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

        // WinForms Z-order: Fill panel first, DockRight GuideBot Drawer second, TabStrip third, MenuStrip last (sits at top y=0)
        _tabContentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(15, 23, 42)
        };
        Controls.Add(_tabContentPanel);

        var isPrimaryWindow = winInstance.Id == _windowManager.MainWindow?.Id || (!winInstance.IsOffscreenPrintWorker && _windowManager.MainWindow == null);
        if (isPrimaryWindow)
        {
            InitializeNativeGuideBotAndMascot();
        }

        _tabStrip = new NativeTabStripControl(_workspace);
        WireTabStripEvents();
        Controls.Add(_tabStrip);

        BuildNativeMenuBar();
        if (ownsTrayIcon && isPrimaryWindow)
        {
            BuildSystemTrayIcon(iconPath);
        }

        // Route windowManager messages & navigation to the active tab's WebView2,
        // and global broadcasts (update-progress, products-updated) to ALL tabs.
        _winInstance.WebMessageSender = BroadcastMessageToActiveTab;
        _winInstance.BroadcastMessageSender = BroadcastMessageToAllTabs;
        _winInstance.UrlNavigator = targetUrl =>
        {
            void NavigateOnUi()
            {
                if (IsDisposed) return;

                var originChanged = false;
                if (Uri.TryCreate(targetUrl, UriKind.Absolute, out var uri) &&
                    (uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)))
                {
                    var nextOrigin = $"{uri.Scheme}://{uri.Authority}";
                    if (!string.Equals(nextOrigin, _baseOrigin, StringComparison.OrdinalIgnoreCase))
                    {
                        _baseOrigin = nextOrigin;
                        originChanged = true;
                        if (_guideSession != null)
                        {
                            _guideSession.BaseOrigin = _baseOrigin;
                            _guideSession.AuthBearerToken = null;
                        }
                    }
                }

                if (originChanged && _workspace.ActiveTabId != null)
                {
                    // Close stale tabs pointing to the previous server origin (Standalone <-> Terminal mode switch)
                    var removed = _workspace.CloseOtherTabs(_workspace.ActiveTabId);
                    DisposeTabControls(removed);
                    SyncActiveTabVisuals();
                }

                OpenOrNavigateTabFromUrl(targetUrl, openNewTab: false);
            }

            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke((Action)NavigateOnUi);
            else NavigateOnUi();
        };

        Load += async (_, _) =>
        {
            try
            {
                await InitializeInitialTabAsync();
            }
            catch (Exception ex)
            {
                try
                {
                    var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Karobar-App-Native");
                    Directory.CreateDirectory(logDir);
                    File.AppendAllText(Path.Combine(logDir, "startup-error.log"), $"[{DateTime.Now:O}] Initial tab load error: {ex}\n");
                }
                catch { }
            }
        };
        Activated += (_, _) => _winInstance.IsFocused = true;
        Deactivate += (_, _) => _winInstance.IsFocused = false;
        FormClosed += (_, _) =>
        {
            if (_subscribedBgTaskManager != null)
            {
                if (_bgStateChangedHandler != null)
                {
                    _subscribedBgTaskManager.StateChanged -= _bgStateChangedHandler;
                    _bgStateChangedHandler = null;
                }
                if (_productsMutatedHandler != null)
                {
                    _subscribedBgTaskManager.ProductsMutated -= _productsMutatedHandler;
                    _productsMutatedHandler = null;
                }
                _subscribedBgTaskManager = null;
            }
            if (_outsideClickFilter != null)
            {
                try { Application.RemoveMessageFilter(_outsideClickFilter); } catch { }
                _outsideClickFilter = null;
            }
            if (_mascotOverlay != null)
            {
                try { _mascotOverlay.Close(); _mascotOverlay.Dispose(); } catch { }
                _mascotOverlay = null;
            }
            if (_guideDrawer != null)
            {
                try { _guideDrawer.Dispose(); } catch { }
                _guideDrawer = null;
            }
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
            if (_bridgeToggleGuideBotHandler != null && ReferenceEquals(_desktopBridge.OnToggleGuideBot, _bridgeToggleGuideBotHandler))
            {
                _desktopBridge.OnToggleGuideBot = null;
            }
            _bridgeToggleGuideBotHandler = null;
            if (_bridgeOpenGuideBotHandler != null && ReferenceEquals(_desktopBridge.OnOpenGuideBot, _bridgeOpenGuideBotHandler))
            {
                _desktopBridge.OnOpenGuideBot = null;
            }
            _bridgeOpenGuideBotHandler = null;
            _winInstance.Hwnd = IntPtr.Zero;
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
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                _ = OpenTabAsync("/sale-invoices/pos", "Barcode POS", allowDuplicate: false);
            });
            _trayMenu.Items.Add("Cube AI GuideBot (Ctrl+G)", null, (_, _) =>
            {
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                ToggleGuideBot();
            });
            _trayMenu.Items.Add("Check for Updates…", null, (_, _) =>
            {
                Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                _windowManager.SendTriggerUpdateCheck(manual: true);
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
        _tabStrip.QuickOpenRequested += (route, title, allowDup) =>
            _ = OpenTabAsync(route, title, allowDuplicate: allowDup, forceSingleton: string.Equals(route, "/sale-invoices/pos", StringComparison.OrdinalIgnoreCase));
        _tabStrip.TabReloadRequested += tabId => ReloadTab(tabId);
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
        _tabStrip.GuideBotToggleRequested += ToggleGuideBot;
    }

    private void InitializeNativeGuideBotAndMascot()
    {
        _guideSession = new GuideBotSessionController(
            _windowManager.NetworkConfig,
            _desktopBridge.ServiceProviderAccessor)
        {
            BaseOrigin = _baseOrigin
        };
        _guideSession.ProductsMutated += BroadcastProductsUpdatedToAllTabs;

        _guideDrawer = new NativeGuideBotDrawer(_guideSession, _desktopBridge.MascotPreferencesStore);
        _guideDrawer.NavigateRequested += (route, title) => _ = OpenTabAsync(route, title, allowDuplicate: false, forceSingleton: true);
        _guideDrawer.StartSpotlightTourRequested += (workflowId, targetRoute) =>
        {
            _ = LaunchSpotlightTourAsync(workflowId, targetRoute);
        };
        _guideDrawer.DrawerVisibilityChanged += () =>
        {
            _mascotOverlay?.SetDrawerState(_guideDrawer.Visible, _guideDrawer.Width);
            if (!_guideDrawer.Visible)
            {
                // Resume any paused Spotlight Tour detour cleanly when the drawer closes
                var resumePayload = JsonSerializer.Serialize(new
                {
                    type = "ipc-event",
                    channel = "karobar:guide-tour",
                    payload = new { action = "resume" }
                });
                BroadcastMessageToActiveTab(resumePayload);
            }
        };
        Controls.Add(_guideDrawer);

        _outsideClickFilter = new GuideBotOutsideClickFilter(this);
        Application.AddMessageFilter(_outsideClickFilter);

        _mascotOverlay = new NativeMascotOverlayForm(this, _desktopBridge.MascotPreferencesStore);
        _mascotOverlay.MascotClicked += () =>
        {
            if (InvokeRequired) BeginInvoke((Action)(() => _guideDrawer?.ShowDrawer("chat")));
            else _guideDrawer?.ShowDrawer("chat");
        };

        var bgMgr = _guideSession.BackgroundTaskManager;
        if (bgMgr != null)
        {
            _subscribedBgTaskManager = bgMgr;
            _bgStateChangedHandler = snap =>
            {
                var pill = snap.Status == "running" ? $"Auto-filling {snap.ScannedCount}/{Math.Max(1, snap.TotalEstimate)}" : null;
                _mascotOverlay?.SetBackgroundTaskProgress(pill);
            };
            _productsMutatedHandler = BroadcastProductsUpdatedToAllTabs;
            bgMgr.StateChanged += _bgStateChangedHandler;
            bgMgr.ProductsMutated += _productsMutatedHandler;
        }

        _bridgeToggleGuideBotHandler = ToggleGuideBot;
        _bridgeOpenGuideBotHandler = tab =>
        {
            if (IsDisposed) return;
            if (InvokeRequired) BeginInvoke((Action)(() => _guideDrawer?.ShowDrawer(tab)));
            else _guideDrawer?.ShowDrawer(tab);
        };
        _desktopBridge.OnToggleGuideBot = _bridgeToggleGuideBotHandler;
        _desktopBridge.OnOpenGuideBot = _bridgeOpenGuideBotHandler;
    }

    private async Task LaunchSpotlightTourAsync(string workflowId, string? targetRoute)
    {
        if (IsDisposed || _guideSession == null) return;
        if (!string.IsNullOrWhiteSpace(targetRoute))
        {
            await OpenTabAsync(targetRoute, null, allowDuplicate: false, forceSingleton: true);
            await Task.Delay(550);
        }
        var payload = JsonSerializer.Serialize(new
        {
            type = "ipc-event",
            channel = "karobar:guide-tour",
            payload = new { action = "start", workflowId, lang = _guideSession.Language }
        });
        BroadcastMessageToActiveTab(payload);
    }

    internal void ToggleGuideBot()
    {
        if (IsDisposed) return;
        var now = Environment.TickCount64;
        if (now - _lastGuideToggleMs < 150) return;
        _lastGuideToggleMs = now;

        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => _guideDrawer?.ToggleDrawer()));
        }
        else
        {
            _guideDrawer?.ToggleDrawer();
        }
    }

    private void CloseGuideBotIfOpen()
    {
        if (IsDisposed || _guideDrawer == null || !_guideDrawer.Visible) return;
        if (InvokeRequired) BeginInvoke((Action)(() => _guideDrawer?.HideDrawer()));
        else _guideDrawer.HideDrawer();
    }

    private sealed class GuideBotOutsideClickFilter : IMessageFilter
    {
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private readonly NativeWebView2Form _form;

        public GuideBotOutsideClickFilter(NativeWebView2Form form) => _form = form;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_LBUTTONDOWN && m.Msg != WM_RBUTTONDOWN && m.Msg != WM_NCLBUTTONDOWN)
            {
                return false;
            }
            if (_form.IsDisposed || !_form.Visible || _form._guideDrawer == null || !_form._guideDrawer.Visible)
            {
                return false;
            }
            if (_form._guideDrawer.IsPopupOpen)
            {
                return false;
            }

            var targetCtrl = Control.FromHandle(m.HWnd);
            // Ignore clicks inside MenuStrip or any ToolStripDropDown (menus, slash menu, context menus)
            if (targetCtrl is MenuStrip or ToolStripDropDown || targetCtrl?.FindForm() is ToolStripDropDown)
            {
                return false;
            }
            // Only act on clicks inside this main window (not dialogs or other forms)
            if (targetCtrl != null && targetCtrl.FindForm() != _form)
            {
                return false;
            }

            var cursorScreen = Cursor.Position;
            // Ignore clicks inside the GuideBot drawer itself
            var drawerRect = _form._guideDrawer.RectangleToScreen(_form._guideDrawer.ClientRectangle);
            if (drawerRect.Contains(cursorScreen))
            {
                return false;
            }
            // Ignore clicks on the "Cube AI" pill in the tab strip so it toggles cleanly without double-toggling
            if (_form._tabStrip != null && _form._tabStrip.IsPointOnGuideBotPill(cursorScreen))
            {
                return false;
            }

            _form.CloseGuideBotIfOpen();
            return false; // Let the user's click proceed normally on whatever they clicked!
        }
    }

    private void BroadcastProductsUpdatedToAllTabs()
    {
        _windowManager.SendProductsUpdated("cube_ai");
    }

    private void BroadcastMessageToAllTabs(string json)
    {
        if (IsDisposed) return;
        void SendAll()
        {
            if (IsDisposed) return;
            foreach (var wv in _tabWebViews.Values)
            {
                if (wv.CoreWebView2 != null)
                {
                    try { wv.CoreWebView2.PostWebMessageAsString(json); } catch { }
                }
            }
        }
        if (InvokeRequired) BeginInvoke((Action)SendAll);
        else SendAll();
    }

    private void ReloadTab(string tabId)
    {
        if (_tabWebViews.TryGetValue(tabId, out var wv) && wv.CoreWebView2 != null)
        {
            wv.CoreWebView2.Reload();
        }
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
                if (TryParseWinFormsShortcutKeys(item.Accelerator, out var shortcutKeys))
                {
                    try
                    {
                        subItem.ShortcutKeys = shortcutKeys;
                        subItem.ShowShortcutKeys = true;
                    }
                    catch
                    {
                        // Ignore unsupported shortcut key combinations
                    }
                }
                subItem.Click += (_, _) =>
                {
                    // Every menu selection opens cleanly as a Waseela-style tab inside THIS window
                    _ = OpenTabAsync(route, cleanLabel, allowDuplicate: false, forceSingleton: true);
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

        var jumpMenu = new ToolStripMenuItem("Jump to Tab 1–9")
        {
            ForeColor = Color.FromArgb(248, 250, 252),
            BackColor = Color.FromArgb(30, 41, 59)
        };
        jumpMenu.DropDown.BackColor = Color.FromArgb(30, 41, 59);
        jumpMenu.DropDown.ForeColor = Color.FromArgb(248, 250, 252);
        jumpMenu.DropDown.Renderer = darkRenderer;

        var jumpItems = new List<ToolStripMenuItem>(9);
        for (var i = 0; i < 8; i++)
        {
            var tabIdx = i;
            var ji = new ToolStripMenuItem($"Tab {i + 1} (Alt+{i + 1} / Ctrl+Shift+{i + 1})")
            {
                ForeColor = Color.FromArgb(248, 250, 252),
                BackColor = Color.FromArgb(30, 41, 59)
            };
            ji.Click += (_, _) => ExecuteTabShortcutAction("focus-index", tabIdx);
            jumpMenu.DropDownItems.Add(ji);
            jumpItems.Add(ji);
        }
        var lastTabItem = new ToolStripMenuItem("Last Tab (Alt+9 / Ctrl+Shift+9)")
        {
            ForeColor = Color.FromArgb(248, 250, 252),
            BackColor = Color.FromArgb(30, 41, 59)
        };
        lastTabItem.Click += (_, _) => ExecuteTabShortcutAction("focus-last");
        jumpMenu.DropDownItems.Add(lastTabItem);
        jumpItems.Add(lastTabItem);

        jumpMenu.DropDownOpening += (_, _) =>
        {
            var tabs = _workspace.Tabs;
            for (var i = 0; i < 8; i++)
            {
                var item = jumpItems[i];
                if (i < tabs.Count)
                {
                    item.Text = $"Tab {i + 1}: {tabs[i].Title} (Alt+{i + 1})";
                    item.Enabled = true;
                }
                else
                {
                    item.Text = $"Tab {i + 1} (Alt+{i + 1} / Ctrl+Shift+{i + 1})";
                    item.Enabled = false;
                }
            }
            lastTabItem.Text = tabs.Count > 0
                ? $"Last Tab: {tabs[^1].Title} (Alt+9)"
                : "Last Tab (Alt+9 / Ctrl+Shift+9)";
            lastTabItem.Enabled = tabs.Count > 0;
        };

        tabsMenu.DropDownItems.Add(jumpMenu);
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

        // Native Cube AI & Bottom Mascot menu
        var cubeMenu = new ToolStripMenuItem("Cube AI")
        {
            ForeColor = Color.FromArgb(248, 250, 252),
            BackColor = Color.FromArgb(15, 23, 42)
        };
        cubeMenu.DropDown.BackColor = Color.FromArgb(30, 41, 59);
        cubeMenu.DropDown.ForeColor = Color.FromArgb(248, 250, 252);
        cubeMenu.DropDown.Renderer = darkRenderer;

        void AddCubeMenuItem(string text, Action onClick)
        {
            var mi = new ToolStripMenuItem(text)
            {
                ForeColor = Color.FromArgb(248, 250, 252),
                BackColor = Color.FromArgb(30, 41, 59)
            };
            mi.Click += (_, _) => onClick();
            cubeMenu.DropDownItems.Add(mi);
        }

        AddCubeMenuItem("Open / Hide GuideBot (Ctrl+G)", ToggleGuideBot);
        AddCubeMenuItem("Background Formulations Task Center", () => _guideDrawer?.ShowDrawer("tasks"));
        cubeMenu.DropDownItems.Add(new ToolStripSeparator());
        AddCubeMenuItem("Show / Hide Bottom Mascot", () => _desktopBridge.MascotPreferencesStore.Update(p => p.Enabled = !p.Enabled));
        AddCubeMenuItem("Switch Mascot Skin (Cube / Pixel / Bot)", () => _desktopBridge.MascotPreferencesStore.Update(p => p.Skin = MascotPreferences.NextSkin(p.Skin)));
        AddCubeMenuItem("Pause / Resume Mascot Roaming", () => _desktopBridge.MascotPreferencesStore.Update(p => p.RoamingEnabled = !p.RoamingEnabled));
        cubeMenu.DropDownItems.Add(new ToolStripSeparator());
        AddCubeMenuItem("Cube AI & Mascot Settings…", () => _ = OpenTabAsync("/settings#cube", "Cube AI Settings", allowDuplicate: false));
        menuStrip.Items.Add(cubeMenu);

        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);
    }

    private async Task InitializeInitialTabAsync()
    {
        _winInstance.Hwnd = Handle;
        _windowManager.CaptureUiSynchronizationContext();
        _ = _windowManager.PrewarmOffscreenPrintWorkerAsync();
        var initialRoute = NativeTabWorkspace.NormalizeRoutePath(_winInstance.RoutePath);
        var (tab, _) = _workspace.OpenTab(initialRoute, NativeTabWorkspace.ResolveTitleForRoute(initialRoute), allowDuplicate: true, focus: true, pinned: initialRoute == "/dashboard");
        await EnsureTabWebViewInitializedAsync(tab, explicitUrl: _winInstance.Url);
        SyncActiveTabVisuals();
        _mascotOverlay?.StartOverlay();
    }

    /// <summary>
    /// Thread-safe entrypoint used by Program.cs and IPC handlers so "openWindow" / "openSubWindow"
    /// opens a Waseela tab inside THIS single window instead of spawning a second OS window.
    /// </summary>
    public void OpenTabFromExternal(string routePath, string? title = null, bool allowDuplicate = false, bool forceSingleton = false)
    {
        if (IsDisposed) return;

        void DoOpen()
        {
            if (IsDisposed) return;
            _ = OpenTabAsync(routePath, title, allowDuplicate, forceSingleton);
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

    public async Task OpenTabAsync(string routePath, string? title = null, bool allowDuplicate = false, bool forceSingleton = false)
    {
        if (IsDisposed) return;

        var (tab, createdNew) = _workspace.OpenTab(routePath, title, allowDuplicate: allowDuplicate, focus: true, forceSingleton: forceSingleton);
        if (createdNew || !_tabWebViews.ContainsKey(tab.TabId))
        {
            await EnsureTabWebViewInitializedAsync(tab);
        }
        else if (_tabWebViews.TryGetValue(tab.TabId, out var existingWv) && existingWv.CoreWebView2 != null)
        {
            // If caller navigated to a different query string or hash (e.g. /settings#cube) on the same singleton route, update it
            var targetUrl = BuildAbsoluteUrl(tab.RoutePath);
            if (!string.Equals(existingWv.CoreWebView2.Source, targetUrl, StringComparison.OrdinalIgnoreCase) &&
                (tab.RoutePath.Contains('?') || tab.RoutePath.Contains('#')))
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

        var detachedWin = _windowManager.CreateDetachedWindow(tab.RoutePath, tab.Title, width: 1360, height: 860);
        var detachedForm = new NativeWebView2Form(detachedWin, _windowManager, _desktopBridge, ownsTrayIcon: false);
        DetachedFormCreated?.Invoke(detachedForm);
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
            _guideSession?.UpdateActiveContext(activeTab.RoutePath);
        }

        _tabStrip.RefreshTabs();
    }

    private Task<CoreWebView2Environment> GetOrCreateSharedEnvironmentAsync()
        => _windowManager.GetOrCreateSharedWebView2EnvironmentAsync();

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
            if (IsDisposed || wv.IsDisposed) return;
            await wv.EnsureCoreWebView2Async(env);
            if (IsDisposed || wv.IsDisposed || wv.CoreWebView2 == null) return;

            var core = wv.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;

            // Inject terminal identity & LAN pairing token headers on every request to the active Store Server origin
            // so Multi-PC LAN Terminal Mode authenticates seamlessly without relying on local Kestrel.
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, reqArgs) =>
            {
                try
                {
                    var reqUri = reqArgs.Request.Uri ?? "";
                    if (!string.IsNullOrWhiteSpace(_baseOrigin) &&
                        reqUri.StartsWith(_baseOrigin, StringComparison.OrdinalIgnoreCase))
                    {
                        var headers = _windowManager.NetworkConfig.GetTerminalIdentityHeaders();
                        foreach (var kv in headers)
                        {
                            if (!string.IsNullOrWhiteSpace(kv.Value))
                            {
                                reqArgs.Request.Headers.SetHeader(kv.Key, kv.Value);
                            }
                        }
                    }
                }
                catch
                {
                    // Never block resource loading on header injection error
                }
            };

            var preloadJs = "window.__karobarUseNativeWebView2PostMessage = true;\n" +
                            KarobarPreloadScript.GetPreloadScript(_winInstance.IsOffscreenPrintWorker);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(preloadJs);
            if (IsDisposed || wv.IsDisposed) return;

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
                    if (root.TryGetProperty("type", out var msgTypeProp))
                    {
                        var msgType = msgTypeProp.GetString() ?? "";
                        if (msgType == "tab-shortcut" && root.TryGetProperty("action", out var actProp))
                        {
                            var action = actProp.GetString() ?? "";
                            var index = root.TryGetProperty("index", out var idxProp) && idxProp.TryGetInt32(out var idxVal) ? idxVal : -1;
                            if (InvokeRequired) BeginInvoke((Action)(() => { ExecuteTabShortcutAction(action, index); }));
                            else ExecuteTabShortcutAction(action, index);
                            return;
                        }
                        if (msgType == "guide-context")
                        {
                            var route = root.TryGetProperty("route", out var rp) ? rp.GetString() : null;
                            var tok = root.TryGetProperty("token", out var tp) ? tp.GetString() : null;
                            var modal = root.TryGetProperty("modal", out var mp) ? mp.GetString() : null;
                            if (tab.TabId == _workspace.ActiveTabId)
                            {
                                _guideSession?.UpdateActiveContext(route, modal, tok);
                            }
                            else if (!string.IsNullOrWhiteSpace(tok) && _guideSession != null)
                            {
                                _guideSession.AuthBearerToken = tok;
                            }
                            return;
                        }
                        if (msgType == "guide-activity")
                        {
                            _mascotOverlay?.NotifyUserActivity();
                            return;
                        }
                        if (msgType == "guide-outside-click")
                        {
                            CloseGuideBotIfOpen();
                            return;
                        }
                        if (msgType == "guide-focus-rect" && tab.TabId == _workspace.ActiveTabId)
                        {
                            var l = root.TryGetProperty("left", out var lp) && lp.TryGetInt32(out var lv) ? lv : 0;
                            var t = root.TryGetProperty("top", out var tp) && tp.TryGetInt32(out var tv) ? tv : 0;
                            var r = root.TryGetProperty("right", out var rrp) && rrp.TryGetInt32(out var rv) ? rv : 0;
                            var b = root.TryGetProperty("bottom", out var bp) && bp.TryGetInt32(out var bv) ? bv : 0;
                            _mascotOverlay?.AvoidFocusedInput(l, t, r, b);
                            return;
                        }
                        if (msgType == "guide-tour-event")
                        {
                            var ev = root.TryGetProperty("event", out var ep) ? ep.GetString() ?? "" : "";
                            if (ev == "ask-ai-about-step" && root.TryGetProperty("prompt", out var prp))
                            {
                                var prompt = prp.GetString() ?? "";
                                if (!string.IsNullOrWhiteSpace(prompt))
                                {
                                    if (InvokeRequired) BeginInvoke((Action)(() => _guideDrawer?.AskFromSpotlightStep(prompt)));
                                    else _guideDrawer?.AskFromSpotlightStep(prompt);
                                }
                            }
                            return;
                        }
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

            core.NavigationCompleted += (_, navArgs) =>
            {
                _windowManager.CloseSplashWindow();

                // In Multi-PC Terminal Mode, if the Store Server becomes unreachable mid-session,
                // navigate cleanly to the local offline recovery page instead of showing Edge's raw error page.
                if (!navArgs.IsSuccess && _windowManager.NetworkConfig.IsTerminalMode())
                {
                    var currentUri = core.Source ?? "";
                    if (!currentUri.Contains("terminal-offline.html", StringComparison.OrdinalIgnoreCase) &&
                        !currentUri.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    {
                        var offlinePath = _windowManager.ResolveTerminalOfflineHtmlPath();
                        if (File.Exists(offlinePath))
                        {
                            try { core.Navigate(new Uri(offlinePath).AbsoluteUri); } catch { }
                        }
                    }
                }
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
        var activeRoute = _workspace.ActiveTab?.RoutePath ?? "";
        if (keyData == (Keys.Control | Keys.Shift | Keys.P) &&
            ShortcutAcceleratorHelper.IsSaleInvoiceFormRoute(activeRoute))
        {
            // Yield Ctrl+Shift+P to React SaleInvoiceRetailForm / SaleInvoiceWholesaleForm (Save & Print)
            // BEFORE base.ProcessCmdKey can dispatch ToolStripMenuItem.ShortcutKeys!
            return false;
        }
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
            keyData == (Keys.Control | Keys.Shift | Keys.PageDown) ||
            keyData == (Keys.Control | Keys.OemCloseBrackets) ||
            keyData == (Keys.Control | Keys.F6))
        {
            return ExecuteTabShortcutAction("cycle-next");
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.Tab) ||
            keyData == (Keys.Control | Keys.PageUp) ||
            keyData == (Keys.Control | Keys.Shift | Keys.PageUp) ||
            keyData == (Keys.Control | Keys.OemOpenBrackets) ||
            keyData == (Keys.Control | Keys.Shift | Keys.F6))
        {
            return ExecuteTabShortcutAction("cycle-prev");
        }
        if (keyData == (Keys.Control | Keys.F4) || keyData == (Keys.Control | Keys.Shift | Keys.Q))
        {
            return ExecuteTabShortcutAction("close-active");
        }
        if (keyData == (Keys.Control | Keys.G))
        {
            return ExecuteTabShortcutAction("toggle-guide-bot");
        }
        var activeRoute = _workspace.ActiveTab?.RoutePath ?? "";
        var isInvoiceFormRoute = ShortcutAcceleratorHelper.IsInvoiceFormRoute(activeRoute);

        if (keyData == (Keys.Control | Keys.T) || keyData == (Keys.Control | Keys.Shift | Keys.N))
        {
            // Preserve Ctrl+T on Sale / Wholesale / Purchase Invoice form screens
            if (keyData == (Keys.Control | Keys.T) && isInvoiceFormRoute)
            {
                return false;
            }
            return ExecuteTabShortcutAction("new-tab");
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.T))
        {
            if (isInvoiceFormRoute) return false;
            return ExecuteTabShortcutAction("reopen-closed");
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.X))
        {
            return ExecuteTabShortcutAction("close-others");
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.D))
        {
            if (isInvoiceFormRoute) return false;
            return ExecuteTabShortcutAction("duplicate");
        }
        if (keyData == (Keys.Control | Keys.Shift | Keys.P))
        {
            if (ShortcutAcceleratorHelper.IsSaleInvoiceFormRoute(activeRoute))
            {
                return false;
            }
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
                _ = OpenTabAsync("/sale-invoices/pos", "Barcode POS", allowDuplicate: false, forceSingleton: true);
                return true;
            case "new-purchase":
                _ = OpenTabAsync("/purchase-invoices/create", "Purchase Invoice", allowDuplicate: true);
                return true;
            case "open-ledger":
                _ = OpenTabAsync("/customer-ledger", "Customer Ledger", allowDuplicate: false, forceSingleton: true);
                return true;
            case "toggle-guide-bot":
                ToggleGuideBot();
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// Executes real Windows hardware silent printing through Microsoft Edge WebView2
    /// (CoreWebView2.PrintAsync) for both thermal POS receipts and A4 documents, with zero OS dialog.
    /// When targetUrl &amp; targetHtml are null, prints the currently active tab; otherwise
    /// delegates to the persistent prewarmed OffscreenPrintWorker owned by WebView2WindowManager.
    /// </summary>
    public Task<(bool Ok, string? Engine, string? Error)> ExecuteNativeSilentPrintAsync(
        string? targetUrl,
        string? targetHtml,
        string printerName,
        bool isThermal,
        string scaleMode = "auto")
    {
        if (!string.IsNullOrWhiteSpace(targetUrl) || !string.IsNullOrWhiteSpace(targetHtml))
        {
            return _windowManager.OffscreenPrintWorker.PrintAsync(targetUrl, targetHtml, printerName, isThermal, scaleMode);
        }

        if (IsDisposed || !IsHandleCreated)
        {
            return Task.FromResult<(bool, string?, string?)>((false, "webview2-silent-print", "Main window is not active."));
        }

        var tcs = new TaskCompletionSource<(bool Ok, string? Engine, string? Error)>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task PrintActiveTabOnUiAsync()
        {
            try
            {
                if (_workspace.ActiveTabId != null &&
                    _tabWebViews.TryGetValue(_workspace.ActiveTabId, out var activeWv) &&
                    activeWv.CoreWebView2 != null)
                {
                    var result = await OffscreenPrintWorker.PrintCoreWebViewAsync(
                        activeWv.CoreWebView2,
                        printerName,
                        isThermal,
                        scaleMode,
                        _windowManager.NetworkConfig.AppRoot);
                    tcs.TrySetResult(result);
                }
                else
                {
                    tcs.TrySetResult((false, "webview2-silent-print", "No active tab available to print."));
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetResult((false, "webview2-silent-print", ex.Message));
            }
        }

        if (InvokeRequired)
        {
            BeginInvoke((Action)(() => { _ = PrintActiveTabOnUiAsync(); }));
        }
        else
        {
            _ = PrintActiveTabOnUiAsync();
        }

        return tcs.Task;
    }

    private static bool TryParseWinFormsShortcutKeys(string? accelerator, out Keys shortcutKeys)
    {
        shortcutKeys = Keys.None;
        var parsed = ShortcutAcceleratorHelper.ParseAccelerator(accelerator);
        if (parsed == null) return false;

        var modifiers = Keys.None;
        if (parsed.Ctrl) modifiers |= Keys.Control;
        if (parsed.Shift) modifiers |= Keys.Shift;
        if (parsed.Alt) modifiers |= Keys.Alt;

        if (modifiers == Keys.None) return false;

        if (parsed.Key.Length == 1 && parsed.Key[0] is >= 'A' and <= 'Z')
        {
            shortcutKeys = modifiers | (Keys)Enum.Parse(typeof(Keys), parsed.Key);
            return true;
        }

        if (Enum.TryParse<Keys>(parsed.Key, ignoreCase: true, out var keyPart) && keyPart != Keys.None)
        {
            shortcutKeys = modifiers | keyPart;
            return true;
        }

        return false;
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

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_TOOLWINDOW = 0x00000080;
            const int WS_EX_NOACTIVATE = 0x08000000;
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }
}

/// <summary>
/// Frameless native Windows Splash Form shown for &lt;1 second while Kestrel &amp; WebView2 initialize,
/// matching <c>desktop/splash.html</c> branding via zero-latency double-buffered GDI+ rendering.
/// </summary>
public sealed class NativeSplashForm : Form
{
    private readonly System.Windows.Forms.Timer _animationTimer;
    private Image? _logoImage;
    private float _progressPhase;

    public NativeSplashForm(string? iconPath)
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(480, 310);
        TopMost = true;
        ShowInTaskbar = false;
        BackColor = Color.FromArgb(15, 23, 42);
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);

        if (!string.IsNullOrWhiteSpace(iconPath) && File.Exists(iconPath) && iconPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
        {
            try { Icon = new Icon(iconPath); } catch { }
        }

        _logoImage = ResolveSplashLogoImage(iconPath, Icon);

        _animationTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _animationTimer.Tick += (_, _) =>
        {
            _progressPhase += 0.024f;
            if (_progressPhase >= 1f) _progressPhase = 0f;
            Invalidate(new Rectangle(120, 204, 240, 16));
        };
        _animationTimer.Start();
    }

    private static Image? ResolveSplashLogoImage(string? iconPath, Icon? formIcon)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            if (iconPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(iconPath);
            }
            var siblingPng = Path.ChangeExtension(iconPath, ".png");
            if (!string.IsNullOrWhiteSpace(siblingPng))
            {
                candidates.Add(siblingPng);
            }
        }

        candidates.Add(Path.Combine(AppContext.BaseDirectory, "desktop", "icon.png"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "assets", "icon.png"));
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "icon.png"));

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(candidate) || !File.Exists(candidate)) continue;
            try
            {
                using var fs = File.OpenRead(candidate);
                using var loaded = Image.FromStream(fs);
                return new Bitmap(loaded);
            }
            catch
            {
                // Try next icon.png candidate
            }
        }

        if (formIcon != null)
        {
            try { return formIcon.ToBitmap(); } catch { }
        }

        return null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var bounds = new Rectangle(0, 0, Width, Height);

        // Dark slate gradient background (#1e293b -> #0f172a -> #020617)
        using (var bgBrush = new LinearGradientBrush(
            new Point(0, 0),
            new Point(Width, Height),
            Color.FromArgb(30, 41, 59),
            Color.FromArgb(2, 6, 23)))
        {
            bgBrush.InterpolationColors = new ColorBlend
            {
                Colors =
                [
                    Color.FromArgb(30, 41, 59),
                    Color.FromArgb(15, 23, 42),
                    Color.FromArgb(2, 6, 23)
                ],
                Positions = [0f, 0.65f, 1f]
            };
            g.FillRectangle(bgBrush, bounds);
        }

        // Subtle 1px slate card border
        using (var borderPen = new Pen(Color.FromArgb(51, 148, 163, 184), 1f))
        {
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        }

        // 68x68 rounded logo badge with subtle blue glow
        const int badgeSize = 68;
        var badgeX = (Width - badgeSize) / 2;
        const int badgeY = 44;
        var badgeRect = new Rectangle(badgeX, badgeY, badgeSize, badgeSize);

        for (var i = 3; i >= 1; i--)
        {
            var glowRect = new Rectangle(badgeX - i * 4, badgeY - i * 3, badgeSize + i * 8, badgeSize + i * 8);
            using var glowPath = CreateRoundedRectPath(glowRect, 20 + i * 2);
            using var glowBrush = new SolidBrush(Color.FromArgb(18, 37, 99, 235));
            g.FillPath(glowBrush, glowPath);
        }

        using (var badgePath = CreateRoundedRectPath(badgeRect, 18))
        {
            if (_logoImage != null)
            {
                using var prevClip = g.Clip;
                g.SetClip(badgePath, CombineMode.Intersect);
                g.DrawImage(_logoImage, badgeRect);
                g.Clip = prevClip;
            }
            else
            {
                using var fallbackBrush = new LinearGradientBrush(
                    badgeRect,
                    Color.FromArgb(37, 99, 235),
                    Color.FromArgb(29, 78, 216),
                    45f);
                g.FillPath(fallbackBrush, badgePath);
                using var monoFont = new Font("Segoe UI", 26f, FontStyle.Bold, GraphicsUnit.Pixel);
                using var monoBrush = new SolidBrush(Color.White);
                using var centerFmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("K", monoFont, monoBrush, badgeRect, centerFmt);
            }
        }

        // Title: "Karobar" + "-" (#38bdf8) + "App"
        using (var titleFont = new Font("Segoe UI", 22f, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var fmt = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.MeasureTrailingSpaces })
        using (var whiteBrush = new SolidBrush(Color.FromArgb(248, 250, 252)))
        using (var accentBrush = new SolidBrush(Color.FromArgb(56, 189, 248)))
        {
            const string part1 = "Karobar";
            const string part2 = "-";
            const string part3 = "App";
            var w1 = g.MeasureString(part1, titleFont, 400, fmt).Width;
            var w2 = g.MeasureString(part2, titleFont, 400, fmt).Width;
            var w3 = g.MeasureString(part3, titleFont, 400, fmt).Width;
            var totalW = w1 + w2 + w3;
            var startX = (Width - totalW) / 2f;
            const float titleY = 128f;
            g.DrawString(part1, titleFont, whiteBrush, startX, titleY, fmt);
            g.DrawString(part2, titleFont, accentBrush, startX + w1, titleY, fmt);
            g.DrawString(part3, titleFont, whiteBrush, startX + w1 + w2, titleY, fmt);
        }

        // Subtitle
        using (var subFont = new Font("Segoe UI", 12.5f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var subBrush = new SolidBrush(Color.FromArgb(148, 163, 184)))
        using (var centerFmt = new StringFormat { Alignment = StringAlignment.Center })
        {
            g.DrawString(
                "Initializing local database & secure runtime…",
                subFont,
                subBrush,
                new RectangleF(24, 163, Width - 48, 24),
                centerFmt);
        }

        // Animated marquee gradient progress bar (#3b82f6 -> #10b981)
        const int trackW = 220;
        const int trackH = 4;
        var trackX = (Width - trackW) / 2;
        const int trackY = 210;
        var trackRect = new Rectangle(trackX, trackY, trackW, trackH);
        using (var trackPath = CreateRoundedRectPath(trackRect, 2))
        {
            using (var trackBrush = new SolidBrush(Color.FromArgb(41, 148, 163, 184)))
            {
                g.FillPath(trackBrush, trackPath);
            }

            const int fillW = 99; // 45% of 220
            var eased = (float)(0.5 - 0.5 * Math.Cos(_progressPhase * Math.PI));
            var fillX = trackX - fillW + eased * (trackW + fillW);
            var fillRect = new RectangleF(fillX, trackY, fillW, trackH);

            using var prevClip = g.Clip;
            g.SetClip(trackPath, CombineMode.Intersect);
            using (var fillPath = CreateRoundedRectPath(Rectangle.Round(fillRect), 2))
            using (var fillBrush = new LinearGradientBrush(
                new PointF(fillRect.Left, trackY),
                new PointF(fillRect.Right, trackY),
                Color.FromArgb(59, 130, 246),
                Color.FromArgb(16, 185, 129)))
            {
                g.FillPath(fillBrush, fillPath);
            }
            g.Clip = prevClip;
        }

        // Footer
        using (var footerFont = new Font("Segoe UI", 11f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var footerBrush = new SolidBrush(Color.FromArgb(100, 116, 139)))
        using (var centerFmt = new StringFormat { Alignment = StringAlignment.Center })
        {
            g.DrawString(
                "Karobar-App Desktop Edition • Inventory • POS • Accounting",
                footerFont,
                footerBrush,
                new RectangleF(16, Height - 30, Width - 32, 20),
                centerFmt);
        }
    }

    private static GraphicsPath CreateRoundedRectPath(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        if (d <= 0)
        {
            path.AddRectangle(rect);
            return path;
        }
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        try { _animationTimer.Stop(); } catch { }
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _animationTimer.Stop(); } catch { }
            try { _animationTimer.Dispose(); } catch { }
            if (_logoImage != null)
            {
                try { _logoImage.Dispose(); } catch { }
                _logoImage = null;
            }
        }
        base.Dispose(disposing);
    }
}
#endif

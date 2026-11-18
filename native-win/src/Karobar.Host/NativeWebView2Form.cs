#if WINDOWS_DESKTOP
using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Karobar.Host;

/// <summary>
/// Real native Windows Form hosting Microsoft Edge WebView2 (CoreWebView2) with
/// native menu strip, icon, system tray integration, and zero-overhead IPC bridge.
/// </summary>
public sealed class NativeWebView2Form : Form
{
    private readonly AppWindowInstance _winInstance;
    private readonly WebView2WindowManager _windowManager;
    private readonly KarobarDesktopBridge _desktopBridge;
    private readonly WebView2 _webView;
    private CoreWebView2Environment? _env;

    public NativeWebView2Form(
        AppWindowInstance winInstance,
        WebView2WindowManager windowManager,
        KarobarDesktopBridge desktopBridge)
    {
        _winInstance = winInstance;
        _windowManager = windowManager;
        _desktopBridge = desktopBridge;

        Text = string.IsNullOrWhiteSpace(winInstance.Title) ? Program.ApplicationDisplayName : winInstance.Title;
        Width = winInstance.Width > 0 ? winInstance.Width : 1440;
        Height = winInstance.Height > 0 ? winInstance.Height : 900;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(15, 23, 42); // #0f172a

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

        _webView = new WebView2
        {
            Dock = DockStyle.Fill,
            DefaultBackgroundColor = Color.FromArgb(15, 23, 42)
        };
        Controls.Add(_webView);

        BuildNativeMenuBar();

        Load += async (_, _) => await InitializeWebViewAsync();
        Activated += (_, _) => _winInstance.IsFocused = true;
        Deactivate += (_, _) => _winInstance.IsFocused = false;
        FormClosed += (_, _) => _windowManager.CloseWindow(_winInstance.Id);
    }

    private void BuildNativeMenuBar()
    {
        var menuStrip = new MenuStrip
        {
            BackColor = Color.FromArgb(15, 23, 42),
            ForeColor = Color.FromArgb(248, 250, 252)
        };

        var sections = _windowManager.GetApplicationMenuSections();
        foreach (var section in sections)
        {
            var topItem = new ToolStripMenuItem(section.Key)
            {
                ForeColor = Color.FromArgb(248, 250, 252)
            };

            foreach (var item in section.Value)
            {
                var route = item.Path;
                var label = item.Label;
                var subItem = new ToolStripMenuItem(label);
                subItem.Click += (_, _) =>
                {
                    if (label.StartsWith("Quick Window:", StringComparison.OrdinalIgnoreCase))
                    {
                        _windowManager.SendOpenNewWindow(route, label.Replace("Quick Window:", "").Trim());
                    }
                    else
                    {
                        _windowManager.SendNavigate(route, label);
                    }
                };
                topItem.DropDownItems.Add(subItem);
            }

            menuStrip.Items.Add(topItem);
        }

        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            _winInstance.Hwnd = Handle;

            _env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: _windowManager.SharedWebView2UserDataFolder,
                options: new CoreWebView2EnvironmentOptions("--disable-features=msSmartScreenProtection"));

            await _webView.EnsureCoreWebView2Async(_env);

            var core = _webView.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = true;

            // Inject window.karobarDesktop preload bridge before any DOM script runs
            var preloadJs = "window.__karobarUseNativeWebView2PostMessage = true;\n" +
                            KarobarPreloadScript.GetPreloadScript(_winInstance.IsOffscreenPrintWorker);
            await core.AddScriptToExecuteOnDocumentCreatedAsync(preloadJs);

            core.DocumentTitleChanged += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(core.DocumentTitle))
                {
                    Text = core.DocumentTitle;
                    _winInstance.Title = core.DocumentTitle;
                }
            };

            core.NewWindowRequested += (_, e) =>
            {
                var decision = _windowManager.HandleWindowOpenRequest(
                    e.Uri,
                    e.Name,
                    onSilentPrintIntercepted: async printUrl =>
                    {
                        await _desktopBridge.PrintUrlAsync(printUrl, new Dictionary<string, object?> { ["silent"] = true });
                    });

                if (decision != "allow-preview")
                {
                    e.Handled = true;
                }
            };

            core.WebMessageReceived += async (_, e) =>
            {
                try
                {
                    var raw = e.TryGetWebMessageAsString();
                    if (string.IsNullOrWhiteSpace(raw)) return;

                    using var doc = JsonDocument.Parse(raw);
                    var root = doc.RootElement;
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
                            if (!IsDisposed && _webView.CoreWebView2 != null)
                            {
                                _webView.CoreWebView2.PostWebMessageAsString(responseJson);
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
                            if (!IsDisposed && _webView.CoreWebView2 != null)
                            {
                                _webView.CoreWebView2.PostWebMessageAsString(errJson);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore malformed WebMessage
                }
            };

            _winInstance.WebMessageSender = json =>
            {
                if (IsDisposed) return;
                try
                {
                    if (InvokeRequired)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (!IsDisposed && _webView.CoreWebView2 != null)
                            {
                                _webView.CoreWebView2.PostWebMessageAsString(json);
                            }
                        }));
                    }
                    else if (_webView.CoreWebView2 != null)
                    {
                        _webView.CoreWebView2.PostWebMessageAsString(json);
                    }
                }
                catch
                {
                    // Ignore
                }
            };

            _winInstance.UrlNavigator = targetUrl =>
            {
                if (IsDisposed) return;
                try
                {
                    if (InvokeRequired)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            if (!IsDisposed && _webView.CoreWebView2 != null)
                            {
                                _webView.CoreWebView2.Navigate(targetUrl);
                            }
                        }));
                    }
                    else if (_webView.CoreWebView2 != null)
                    {
                        _webView.CoreWebView2.Navigate(targetUrl);
                    }
                }
                catch
                {
                    // Ignore
                }
            };

            core.NavigationCompleted += (_, _) =>
            {
                _windowManager.CloseSplashWindow();
            };

            core.Navigate(_winInstance.Url);
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

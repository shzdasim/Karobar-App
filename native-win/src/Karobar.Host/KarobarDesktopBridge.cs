using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Karobar.Host;

public sealed class PrinterInfoDto
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("driverName")]
    public string DriverName { get; set; } = "";

    [JsonPropertyName("portName")]
    public string PortName { get; set; } = "";

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("isDefault")]
    public bool IsDefault { get; set; }

    [JsonPropertyName("isVirtual")]
    public bool IsVirtual { get; set; }

    [JsonPropertyName("workOffline")]
    public bool WorkOffline { get; set; }

    [JsonPropertyName("isOnline")]
    public bool IsOnline { get; set; }

    [JsonPropertyName("isThermalPos")]
    public bool IsThermalPos { get; set; }

    [JsonPropertyName("isPreferred")]
    public bool IsPreferred { get; set; }

    [JsonPropertyName("isLastUsedInChromium")]
    public bool IsLastUsedInChromium { get; set; }
}

public sealed class PrinterStrategyResult
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "no-printer";

    [JsonPropertyName("deviceName")]
    public string? DeviceName { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("printerInfo")]
    public PrinterInfoDto? PrinterInfo { get; set; }
}

public sealed class LocalVersionMeta
{
    [JsonPropertyName("app_name")]
    public string AppName { get; set; } = "Karobar-App";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "2.0.1";

    [JsonPropertyName("build_number")]
    public int BuildNumber { get; set; } = 1;

    [JsonPropertyName("commit_sha")]
    public string CommitSha { get; set; } = "local";

    [JsonPropertyName("repository")]
    public string Repository { get; set; } = "shzdasim/Karobar-App";

    [JsonPropertyName("runtime_layout")]
    public string RuntimeLayout { get; set; } = "";

    [JsonPropertyName("deps_fingerprint")]
    public string DepsFingerprint { get; set; } = "";
}

/// <summary>
/// Implements all 22 window.karobarDesktop properties/methods and 5 event channels
/// matching desktop/main.cjs and desktop/preload.cjs 1-to-1.
/// </summary>
public sealed class KarobarDesktopBridge
{
    private static readonly Regex VirtualPrinterNameOrDriverRegex = new(
        @"(microsoft print to pdf|microsoft xps|xps document writer|onenote|send to onenote|fax|anydesk|teamviewer|adobe pdf|foxit|cutepdf|dopdf|bullzip|nitro pdf|pdfcreator|pdffactory|clawpdf|pdf24|wondershare|wps pdf|fineprint|snagit|paperport|priprinter|universal document|canva|save as pdf)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VirtualPrinterPortRegex = new(
        @"^(portprompt:|file:|nul:|xpsport:|shrfax:|microsoft\.office\.onenote|anydesk|pdf)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ThermalPosPrinterRegex = new(
        @"(pos|thermal|receipt|58mm|80mm|78mm|72mm|pos58|pos80|pos-58|pos-80|xprinter|xp-|black\s*copper|bc-|epson.*tm|tm-t|tm-u|bixolon|star\s*tsp|citizen|rongta|rp58|rp80|birch|sewoo|gainscha|hprt|deli|zywell|munbyn|netum|goojprt|milestone|gp-|zjiang|zj-|gprinter|hoin|hop-|snbc|btp-|srp-|mht-|niimbot|speedx|tsc|argox|zebra)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly NetworkConfigManager _networkConfig;
    private readonly WebView2WindowManager _windowManager;
    private readonly Func<IServiceProvider?>? _serviceProviderAccessor;
    private readonly Karobar.Host.Mascot.MascotPreferencesStore _mascotPrefs = new();
    private readonly Karobar.Printing.Win32RawPrinterService _rawPrinterService;

    private List<PrinterInfoDto>? _cachedPrinters;
    private long _cachedPrintersAt;
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly object _progressFileLock = new();

    public Karobar.Host.Mascot.MascotPreferencesStore MascotPreferencesStore => _mascotPrefs;
    public Func<IServiceProvider?>? ServiceProviderAccessor => _serviceProviderAccessor;
    public Action? OnToggleGuideBot { get; set; }
    public Action<string?>? OnOpenGuideBot { get; set; }

    public KarobarDesktopBridge(
        NetworkConfigManager networkConfig,
        WebView2WindowManager windowManager,
        Func<IServiceProvider?>? serviceProviderAccessor = null)
    {
        _networkConfig = networkConfig;
        _windowManager = windowManager;
        _serviceProviderAccessor = serviceProviderAccessor;
        _rawPrinterService = new Karobar.Printing.Win32RawPrinterService(_networkConfig.AppRoot);
        _rawPrinterService.NativePdfPrintFallback = async (pdfPathArg, printerNameArg, isThermalArg, _) =>
        {
            if (NativeSilentPrintExecutor == null || string.IsNullOrWhiteSpace(pdfPathArg) || !File.Exists(pdfPathArg))
            {
                return (false, null, "Desktop silent print host is unavailable.");
            }
            var pdfFileUri = new Uri(Path.GetFullPath(pdfPathArg)).AbsoluteUri;
            var res = await NativeSilentPrintExecutor(pdfFileUri, null, printerNameArg, isThermalArg);
            return (res.Ok, res.Engine ?? "webview2-pdf-fallback", res.Error);
        };
        _windowManager.OnRunBackupRequested = async () => await RunBackupNowAsync();
    }

    // =========================================================================
    // 3 Static Properties on window.karobarDesktop
    // =========================================================================

    public bool IsDesktop => true;
    public bool isDesktop => true;

    public bool IsOffscreenPrintWorker { get; set; }
    public bool isOffscreenPrintWorker => IsOffscreenPrintWorker;

    public string Platform => "win32";
    public string platform => "win32";

    public Func<Task>? OnStartBackendServices { get; set; }

    /// <summary>
    /// Optional native desktop print executor (wired on Windows to NativeWebView2Form's
    /// CoreWebView2.PrintAsync engine) so printUrl, printHtml, and window.print() spool
    /// directly to the physical thermal/A4 hardware printer without OS dialogs.
    /// Signature: (targetUrl, targetHtml, printerName, isThermal) => (Ok, Engine, Error).
    /// </summary>
    public Func<string?, string?, string, bool, Task<(bool Ok, string? Engine, string? Error)>>? NativeSilentPrintExecutor { get; set; }
    public Func<string?, string?, string, bool, string, Task<(bool Ok, string? Engine, string? Error)>>? NativeSilentPrintWithScaleExecutor { get; set; }
    public Func<Task<IReadOnlyList<PrinterInfoDto>>>? PrinterListProvider { get; set; }
    public Func<Task>? OnStopBackendServices { get; set; }

    // =========================================================================
    // Unified IPC Dispatcher (WebView2 postMessage & HTTP /__karobar_ipc/invoke)
    // =========================================================================

    public async Task<object?> DispatchIpcAsync(string channel, JsonElement? payload = null, CancellationToken cancellationToken = default)
    {
        var normalizedChannel = (channel ?? string.Empty).Trim();
        var body = payload ?? default;

        return normalizedChannel switch
        {
            "karobar:app-info" or "getAppInfo" => await GetAppInfoAsync(),
            "karobar:get-network-config" or "getNetworkConfig" => await GetNetworkConfigAsync(),
            "karobar:save-network-config" or "saveNetworkConfig" => await SaveNetworkConfigAsync(body),
            "karobar:test-server-connection" or "testServerConnection" => await TestServerConnectionAsync(body, cancellationToken),
            "karobar:get-printers" or "getPrinters" => await GetPrintersAsync(forceRefresh: true),
            "karobar:get-preferred-printer" or "getPreferredPrinter" => await GetPreferredPrinterAsync(),
            "karobar:set-preferred-printer" or "setPreferredPrinter" => await SetPreferredPrinterAsync(body),
            "karobar:silent-print" or "silentPrint" => await SilentPrintAsync(body, cancellationToken),
            "karobar:print-url" or "printUrl" => await PrintUrlAsync(body, cancellationToken),
            "karobar:kick-cash-drawer" or "kickCashDrawer" => await KickCashDrawerAsync(body, cancellationToken),
            "karobar:print-thermal-raw" or "printThermalReceipt" => await PrintThermalRawAsync(body, cancellationToken),
            "karobar:print-html" or "printHtml" => await PrintHtmlAsync(body, cancellationToken),
            "karobar:select-directory" or "selectDirectory" => await SelectDirectoryAsync(),
            "karobar:select-sqlite-file" or "selectSqliteFile" => await SelectSqliteFileAsync(),
            "karobar:run-backup-now" or "runBackupNow" => await RunBackupNowAsync(cancellationToken),
            "karobar:check-for-updates" or "checkForUpdates" => await CheckForUpdatesAsync(body, cancellationToken),
            "karobar:download-and-install-update" or "downloadAndInstallUpdate" => await DownloadAndInstallUpdateAsync(body, cancellationToken),
            "karobar:open-external-url" or "openExternalUrl" => await OpenExternalUrlAsync(body),
            "karobar:open-window" or "openWindow" => await OpenWindowAsync(body),
            "karobar:get-open-windows" or "getOpenWindows" => await GetOpenWindowsAsync(),
            "karobar:focus-window" or "focusWindow" => await FocusWindowAsync(body),
            "karobar:toggle-guide-bot" or "toggleGuideBot" => ToggleGuideBotIpc(),
            "karobar:open-guide-bot" or "openGuideBot" => OpenGuideBotIpc(body),
            "karobar:get-mascot-prefs" or "getMascotPrefs" => _mascotPrefs.Current,
            "karobar:set-mascot-prefs" or "setMascotPrefs" => SetMascotPrefsIpc(body),
            _ => new { ok = false, error = $"Unknown IPC channel: {normalizedChannel}" }
        };
    }

    private object ToggleGuideBotIpc()
    {
        OnToggleGuideBot?.Invoke();
        return new { ok = true };
    }

    private object OpenGuideBotIpc(JsonElement body)
    {
        string? tab = null;
        if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("tab", out var tp) && tp.ValueKind == JsonValueKind.String)
        {
            tab = tp.GetString();
        }
        OnOpenGuideBot?.Invoke(tab);
        return new { ok = true };
    }

    private object SetMascotPrefsIpc(JsonElement body)
    {
        var updated = _mascotPrefs.Update(p =>
        {
            if (body.ValueKind != JsonValueKind.Object) return;
            if (body.TryGetProperty("enabled", out var eProp) && (eProp.ValueKind == JsonValueKind.True || eProp.ValueKind == JsonValueKind.False))
                p.Enabled = eProp.GetBoolean();
            if (body.TryGetProperty("skin", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                p.Skin = sProp.GetString() ?? "cube";
            if (body.TryGetProperty("roaming_enabled", out var rProp) && (rProp.ValueKind == JsonValueKind.True || rProp.ValueKind == JsonValueKind.False))
                p.RoamingEnabled = rProp.GetBoolean();
            if (body.TryGetProperty("roamingEnabled", out var r2Prop) && (r2Prop.ValueKind == JsonValueKind.True || r2Prop.ValueKind == JsonValueKind.False))
                p.RoamingEnabled = r2Prop.GetBoolean();
            if (body.TryGetProperty("language", out var lProp) && lProp.ValueKind == JsonValueKind.String)
                p.Language = lProp.GetString() ?? "en";
        });
        return new { ok = true, prefs = updated };
    }

    public async Task<string> HandleWebView2MessageAsync(string rawMessageJson, CancellationToken cancellationToken = default)
    {
        string? invokeId = null;
        try
        {
            using var doc = JsonDocument.Parse(rawMessageJson);
            var root = doc.RootElement;
            invokeId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
            var channel = root.TryGetProperty("channel", out var chProp) ? chProp.GetString() ?? "" : "";
            JsonElement? payload = root.TryGetProperty("payload", out var plProp) ? plProp.Clone() : null;

            var result = await DispatchIpcAsync(channel, payload, cancellationToken);
            return JsonSerializer.Serialize(new
            {
                type = "ipc-response",
                id = invokeId,
                result
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                type = "ipc-response",
                id = invokeId,
                error = ex.Message
            });
        }
    }

    // =========================================================================
    // 1. getAppInfo (karobar:app-info)
    // =========================================================================

    public Task<object> GetAppInfoAsync()
    {
        var cfg = _networkConfig.LoadNetworkConfig();
        var versionMeta = ReadLocalVersionMeta();
        string hostname;
        try
        {
            hostname = Environment.MachineName;
        }
        catch
        {
            hostname = "PC";
        }

        object info = new
        {
            version = versionMeta.Version,
            appRoot = _networkConfig.AppRoot,
            port = _networkConfig.ActivePort,
            platform = "win32",
            hostname,
            networkMode = cfg.Mode,
            serverUrl = cfg.ServerUrl,
            terminalId = cfg.TerminalId,
            terminalName = cfg.TerminalName,
            baseUrl = _networkConfig.GetAppBaseUrl()
        };

        return Task.FromResult(info);
    }

    public Task<object> getAppInfo() => GetAppInfoAsync();

    // =========================================================================
    // 2. getNetworkConfig (karobar:get-network-config)
    // =========================================================================

    public Task<object> GetNetworkConfigAsync()
    {
        var cfg = _networkConfig.LoadNetworkConfig();
        string hostname;
        try
        {
            hostname = Environment.MachineName;
        }
        catch
        {
            hostname = "PC";
        }

        object res = new
        {
            mode = cfg.Mode,
            serverUrl = cfg.ServerUrl,
            terminalId = cfg.TerminalId,
            terminalName = cfg.TerminalName,
            pairingToken = cfg.PairingToken,
            hostname,
            baseUrl = _networkConfig.GetAppBaseUrl()
        };

        return Task.FromResult(res);
    }

    public Task<object> getNetworkConfig() => GetNetworkConfigAsync();

    // =========================================================================
    // 3. saveNetworkConfig (karobar:save-network-config)
    // =========================================================================

    public async Task<object> SaveNetworkConfigAsync(JsonElement payload)
    {
        var update = new NetworkConfigUpdate();

        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("mode", out var modeProp) && modeProp.ValueKind == JsonValueKind.String)
                update.Mode = modeProp.GetString();
            if (payload.TryGetProperty("serverUrl", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                update.ServerUrl = urlProp.GetString();
            if (payload.TryGetProperty("terminalId", out var idProp) && idProp.ValueKind == JsonValueKind.String)
                update.TerminalId = idProp.GetString();
            if (payload.TryGetProperty("terminalName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                update.TerminalName = nameProp.GetString();
            if (payload.TryGetProperty("pairingToken", out var tokProp) && tokProp.ValueKind == JsonValueKind.String)
                update.PairingToken = tokProp.GetString();
            if (payload.TryGetProperty("relaunch", out var relProp))
                update.Relaunch = relProp.ValueKind == JsonValueKind.True;
        }

        var updated = _networkConfig.SaveNetworkConfig(update);

        if (update.Relaunch)
        {
            if (updated.Mode == "standalone" && OnStartBackendServices != null)
            {
                await OnStartBackendServices();
            }
            else if (updated.Mode == "terminal" && !string.IsNullOrWhiteSpace(updated.ServerUrl) && OnStopBackendServices != null)
            {
                await OnStopBackendServices();
            }

            var targetUrl = _networkConfig.GetAppBaseUrl();
            var activeWin = _windowManager.GetActiveWindow();
            if (activeWin != null)
            {
                activeWin.LoadUrl($"{targetUrl}/");
            }
            else
            {
                _windowManager.SendNavigate($"{targetUrl}/", "Karobar-App");
            }
        }

        return new
        {
            ok = true,
            config = updated,
            baseUrl = _networkConfig.GetAppBaseUrl()
        };
    }

    public Task<object> saveNetworkConfig(JsonElement payload) => SaveNetworkConfigAsync(payload);

    // =========================================================================
    // 4. testServerConnection (karobar:test-server-connection)
    // =========================================================================

    public async Task<ServerConnectionTestResult> TestServerConnectionAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var serverUrl = "";
        var terminalName = "";
        string? pairingToken = null;

        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("serverUrl", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                serverUrl = sProp.GetString() ?? "";
            if (payload.TryGetProperty("terminalName", out var tProp) && tProp.ValueKind == JsonValueKind.String)
                terminalName = tProp.GetString() ?? "";
            if (payload.TryGetProperty("pairingToken", out var pProp) && pProp.ValueKind == JsonValueKind.String)
                pairingToken = pProp.GetString();
        }
        else if (payload.ValueKind == JsonValueKind.String)
        {
            serverUrl = payload.GetString() ?? "";
        }

        var version = ReadLocalVersionMeta().Version;
        return await _networkConfig.TestServerConnectionAsync(serverUrl, terminalName, version, cancellationToken, pairingToken);
    }

    public Task<ServerConnectionTestResult> testServerConnection(JsonElement payload) => TestServerConnectionAsync(payload);

    // =========================================================================
    // 5, 6, 7. Printer Discovery & Configuration
    // (getPrinters, getPreferredPrinter, setPreferredPrinter)
    // =========================================================================

    public static string GetPrinterConfigFilePath()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                return Path.Combine(appData, NetworkConfigManager.AppDataFolderName, "printer-config.json");
            }
        }
        catch
        {
            // Ignore
        }

        return Path.Combine(Path.GetTempPath(), "karobar-native-printer-config.json");
    }

    public static string ReadPreferredPrinterConfig()
    {
        try
        {
            var file = GetPrinterConfigFilePath();
            if (!File.Exists(file)) return "";
            using var doc = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8));
            if (doc.RootElement.TryGetProperty("preferredPrinter", out var prop) &&
                prop.ValueKind == JsonValueKind.String)
            {
                var name = (prop.GetString() ?? "").Trim();
                if (string.IsNullOrEmpty(name) ||
                    string.Equals(name, "default", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(name, "system_default", StringComparison.OrdinalIgnoreCase))
                {
                    return "";
                }
                return name;
            }
        }
        catch
        {
            // Ignore
        }

        return "";
    }

    public static string WritePreferredPrinterConfig(string? deviceName)
    {
        try
        {
            var clean = (deviceName ?? "").Trim();
            var normalized = string.IsNullOrEmpty(clean) ||
                             string.Equals(clean, "default", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(clean, "system_default", StringComparison.OrdinalIgnoreCase)
                ? ""
                : clean;

            var file = GetPrinterConfigFilePath();
            var dir = Path.GetDirectoryName(file);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(new
            {
                preferredPrinter = normalized,
                updatedAt = DateTime.UtcNow.ToString("O")
            }, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(file, json, Encoding.UTF8);
            return normalized;
        }
        catch
        {
            return "";
        }
    }

    public static PrinterInfoDto ClassifyPrinterInfo(
        string name,
        string? displayName,
        string? driverName,
        string? portName,
        int statusMask,
        int wmiPrinterStatus,
        bool isDefault,
        bool wmiWorkOffline,
        string? preferredName,
        string? lastChromiumName = null)
    {
        var cleanName = (name ?? "").Trim();
        var cleanDisplay = string.IsNullOrWhiteSpace(displayName) ? cleanName : displayName.Trim();
        var cleanDriver = (driverName ?? "").Trim();
        var cleanPort = (portName ?? "").Trim();

        var isVirtual = VirtualPrinterNameOrDriverRegex.IsMatch(cleanName) ||
                        VirtualPrinterNameOrDriverRegex.IsMatch(cleanDisplay) ||
                        VirtualPrinterNameOrDriverRegex.IsMatch(cleanDriver) ||
                        VirtualPrinterPortRegex.IsMatch(cleanPort);

        var workOffline = wmiWorkOffline ||
                          wmiPrinterStatus == 7 ||
                          (statusMask & (0x00000080 | 0x00001000)) != 0;

        var hasHardwareError = (statusMask & (0x00000002 | 0x00000004)) != 0;
        var isOnline = !isVirtual && !workOffline && !hasHardwareError;

        var isThermalPos = !isVirtual &&
                           (ThermalPosPrinterRegex.IsMatch(cleanName) ||
                            ThermalPosPrinterRegex.IsMatch(cleanDisplay) ||
                            ThermalPosPrinterRegex.IsMatch(cleanDriver));

        var isPreferred = !string.IsNullOrWhiteSpace(preferredName) &&
                          string.Equals(cleanName, preferredName, StringComparison.OrdinalIgnoreCase);

        var isLastUsed = !string.IsNullOrWhiteSpace(lastChromiumName) &&
                         string.Equals(cleanName, lastChromiumName, StringComparison.OrdinalIgnoreCase);

        return new PrinterInfoDto
        {
            Name = cleanName,
            DisplayName = cleanDisplay,
            DriverName = cleanDriver,
            PortName = cleanPort,
            Status = statusMask,
            IsDefault = isDefault,
            IsVirtual = isVirtual,
            WorkOffline = workOffline,
            IsOnline = isOnline,
            IsThermalPos = isThermalPos,
            IsPreferred = isPreferred,
            IsLastUsedInChromium = isLastUsed
        };
    }

    public async Task<IReadOnlyList<PrinterInfoDto>> GetPrintersAsync(bool forceRefresh = false)
    {
        var now = Environment.TickCount64;
        if (!forceRefresh && _cachedPrinters != null && now - _cachedPrintersAt < 15000)
        {
            return _cachedPrinters;
        }

        if (PrinterListProvider != null)
        {
            var customList = (await PrinterListProvider()).ToList();
            _cachedPrinters = customList;
            _cachedPrintersAt = Environment.TickCount64;
            return customList;
        }

        var preferredName = ReadPreferredPrinterConfig();
        var printers = new List<PrinterInfoDto>();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var nativePrinters = await _rawPrinterService.GetPrintersAsync(forceRefresh);
                foreach (var np in nativePrinters)
                {
                    printers.Add(new PrinterInfoDto
                    {
                        Name = np.Name,
                        DisplayName = np.DisplayName,
                        DriverName = np.DriverName,
                        PortName = np.PortName,
                        Status = np.Status,
                        IsDefault = np.IsDefault,
                        IsVirtual = np.IsVirtual,
                        WorkOffline = np.WorkOffline,
                        IsOnline = np.IsOnline,
                        IsThermalPos = np.IsThermalPos,
                        IsPreferred = !string.IsNullOrWhiteSpace(preferredName) &&
                                      string.Equals(np.Name, preferredName, StringComparison.OrdinalIgnoreCase),
                        IsLastUsedInChromium = false
                    });
                }
            }
            catch
            {
                if (_cachedPrinters != null)
                {
                    return _cachedPrinters;
                }
            }
        }

        _cachedPrinters = printers;
        _cachedPrintersAt = Environment.TickCount64;
        return printers;
    }

    public Task<IReadOnlyList<PrinterInfoDto>> getPrinters() => GetPrintersAsync(true);

    public async Task<PrinterStrategyResult> ResolvePrinterStrategyAsync(string? requestedDeviceName = null, bool isThermalDoc = false)
    {
        var explicitRequest = (!string.IsNullOrWhiteSpace(requestedDeviceName)
            ? requestedDeviceName
            : ReadPreferredPrinterConfig()).Trim();

        var printers = await GetPrintersAsync();
        if (PrinterListProvider == null && (printers.Count == 0 || !printers.Any(p => !p.IsVirtual)))
        {
            printers = await GetPrintersAsync(forceRefresh: true);
        }
        if (printers.Count == 0)
        {
            return new PrinterStrategyResult
            {
                Mode = "no-printer",
                DeviceName = null,
                Error = "No printers found on Windows. Please connect and install a printer."
            };
        }

        if (!string.IsNullOrWhiteSpace(explicitRequest) &&
            !string.Equals(explicitRequest, "default", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(explicitRequest, "system_default", StringComparison.OrdinalIgnoreCase))
        {
            var matched = printers.FirstOrDefault(p => p.Name == explicitRequest) ??
                          printers.FirstOrDefault(p => string.Equals(p.Name, explicitRequest, StringComparison.OrdinalIgnoreCase));

            if (matched != null)
            {
                if (!matched.IsVirtual && matched.WorkOffline)
                {
                    var onlineAlt = printers.FirstOrDefault(p => !p.IsVirtual && p.IsOnline);
                    if (onlineAlt != null)
                    {
                        return new PrinterStrategyResult
                        {
                            Mode = "spooler",
                            DeviceName = onlineAlt.Name,
                            PrinterInfo = onlineAlt
                        };
                    }
                }

                return new PrinterStrategyResult
                {
                    Mode = "spooler",
                    DeviceName = matched.Name,
                    PrinterInfo = matched
                };
            }
        }

        var physicalPrinters = printers.Where(p => !string.IsNullOrWhiteSpace(p.Name) && !p.IsVirtual).ToList();
        if (physicalPrinters.Count == 0)
        {
            return new PrinterStrategyResult
            {
                Mode = "no-printer",
                DeviceName = null,
                Error = "No physical hardware printer connected (only virtual PDF/OneNote printers detected). Please connect your printer or select it in Settings → Print & Receipt Layouts."
            };
        }

        var onlinePhysical = physicalPrinters.Where(p => p.IsOnline).ToList();
        var candidatePool = onlinePhysical.Count > 0 ? onlinePhysical : physicalPrinters;

        var lastChromium = candidatePool.FirstOrDefault(p => p.IsLastUsedInChromium);
        if (lastChromium != null)
        {
            return new PrinterStrategyResult { Mode = "spooler", DeviceName = lastChromium.Name, PrinterInfo = lastChromium };
        }

        if (isThermalDoc)
        {
            var thermalMatch = candidatePool.FirstOrDefault(p => p.IsThermalPos);
            if (thermalMatch != null)
            {
                return new PrinterStrategyResult { Mode = "spooler", DeviceName = thermalMatch.Name, PrinterInfo = thermalMatch };
            }
        }

        var osDefaultPhysical = candidatePool.FirstOrDefault(p => p.IsDefault);
        if (osDefaultPhysical != null)
        {
            return new PrinterStrategyResult { Mode = "spooler", DeviceName = osDefaultPhysical.Name, PrinterInfo = osDefaultPhysical };
        }

        var chosen = candidatePool[0];
        return new PrinterStrategyResult { Mode = "spooler", DeviceName = chosen.Name, PrinterInfo = chosen };
    }

    public async Task<object> GetPreferredPrinterAsync()
    {
        var saved = ReadPreferredPrinterConfig();
        var strategy = await ResolvePrinterStrategyAsync(saved, isThermalDoc: false);
        return new
        {
            preferredPrinter = saved,
            resolvedPrinter = strategy.DeviceName ?? "",
            mode = strategy.Mode
        };
    }

    public Task<object> getPreferredPrinter() => GetPreferredPrinterAsync();

    public async Task<object> SetPreferredPrinterAsync(JsonElement payload)
    {
        string? deviceName = null;
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("deviceName", out var dProp) &&
            dProp.ValueKind == JsonValueKind.String)
        {
            deviceName = dProp.GetString();
        }
        else if (payload.ValueKind == JsonValueKind.String)
        {
            deviceName = payload.GetString();
        }

        var saved = WritePreferredPrinterConfig(deviceName);
        var strategy = await ResolvePrinterStrategyAsync(saved, isThermalDoc: false);
        return new
        {
            ok = true,
            preferredPrinter = saved,
            resolvedPrinter = strategy.DeviceName ?? ""
        };
    }

    public Task<object> setPreferredPrinter(JsonElement payload) => SetPreferredPrinterAsync(payload);

    // =========================================================================
    // 8, 9, 10. Silent Printing (silentPrint, printUrl, printHtml)
    // =========================================================================

    public string? ResolveSumatraPdfBinary()
    {
        if (!OperatingSystem.IsWindows()) return null;

        var candidates = new[]
        {
            Path.Combine(_networkConfig.AppRoot, "assets", "SumatraPDF.exe"),
            Path.Combine(_networkConfig.AppRoot, "assets", "SumatraPDF-3.4.6-32.exe"),
            Path.Combine(_networkConfig.AppRoot, "native-win", "assets", "SumatraPDF.exe"),
            Path.Combine(AppContext.BaseDirectory, "assets", "SumatraPDF.exe"),
            Path.Combine(AppContext.BaseDirectory, "assets", "SumatraPDF-3.4.6-32.exe"),
            Path.Combine(_networkConfig.AppRoot, "desktop", "SumatraPDF.exe"),
            Path.Combine(_networkConfig.AppRoot, "desktop", "SumatraPDF-3.4.6-32.exe"),
            Path.Combine(AppContext.BaseDirectory, "desktop", "SumatraPDF.exe"),
            Path.Combine(AppContext.BaseDirectory, "SumatraPDF.exe"),
            Path.Combine(AppContext.BaseDirectory, "SumatraPDF-3.4.6-32.exe")
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

    public Task<object> SilentPrintAsync(JsonElement options, CancellationToken cancellationToken = default)
        => ExecuteSilentPrintCoreAsync(options, targetUrl: null, targetHtml: null, cancellationToken);

    private async Task<object> ExecuteSilentPrintCoreAsync(
        JsonElement options,
        string? targetUrl,
        string? targetHtml,
        CancellationToken cancellationToken)
    {
        string? requestedDeviceName = null;
        var isThermal = false;
        var scaleMode = "auto";
        string? pdfPath = null;

        if (options.ValueKind == JsonValueKind.Object)
        {
            if (options.TryGetProperty("deviceName", out var dProp) && dProp.ValueKind == JsonValueKind.String)
                requestedDeviceName = dProp.GetString();
            if (options.TryGetProperty("isThermal", out var tProp))
                isThermal = tProp.ValueKind == JsonValueKind.True;
            if (options.TryGetProperty("scaleMode", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                scaleMode = sProp.GetString() ?? "auto";
            if (options.TryGetProperty("pdfFilePath", out var pProp) && pProp.ValueKind == JsonValueKind.String)
                pdfPath = pProp.GetString();
        }

        if (!string.IsNullOrWhiteSpace(targetHtml))
        {
            var (metaIsThermal, metaScaleMode) = Karobar.Printing.PrintTemplateRenderer.ExtractPrintMetaHints(targetHtml);
            if (metaIsThermal == true)
            {
                isThermal = true;
            }
            if ((string.IsNullOrWhiteSpace(scaleMode) || scaleMode.Equals("auto", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(metaScaleMode))
            {
                scaleMode = metaScaleMode;
            }
        }
        else if (!isThermal && !string.IsNullOrWhiteSpace(targetUrl) &&
                 (targetUrl.Contains("/print/thermal-preview/", StringComparison.OrdinalIgnoreCase) ||
                  targetUrl.Contains("/print/sticker-preview/", StringComparison.OrdinalIgnoreCase) ||
                  targetUrl.Contains("/print/cash-drawer/", StringComparison.OrdinalIgnoreCase) ||
                  targetUrl.Contains("type=thermal", StringComparison.OrdinalIgnoreCase)))
        {
            isThermal = true;
        }

        var hasNativeHostExecutor = NativeSilentPrintExecutor != null || NativeSilentPrintWithScaleExecutor != null;
        var hasPdfFile = !string.IsNullOrWhiteSpace(pdfPath) && File.Exists(pdfPath);

        var strategy = await ResolvePrinterStrategyAsync(requestedDeviceName, isThermal);
        if (strategy.Mode == "no-printer" || string.IsNullOrWhiteSpace(strategy.DeviceName))
        {
            return new
            {
                ok = false,
                noPhysicalPrinter = true,
                fallbackToBrowserPrint = !hasNativeHostExecutor && !hasPdfFile,
                error = strategy.Error ??
                        "No physical hardware printer detected on Windows. Please connect a printer or select one in Settings → Print & Receipt Layouts."
            };
        }

        var targetPrinter = strategy.DeviceName;
        var effectiveIsThermal = isThermal || (strategy.PrinterInfo?.IsThermalPos == true);
        var sumatraExe = ResolveSumatraPdfBinary();

        if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(sumatraExe) && hasPdfFile)
        {
            var sumatraRes = await SpoolPdfViaSumatraAsync(sumatraExe, pdfPath!, targetPrinter, effectiveIsThermal, scaleMode, cancellationToken);
            if (sumatraRes.Ok)
            {
                return new
                {
                    ok = true,
                    printer = targetPrinter,
                    engine = "sumatra-win32"
                };
            }
        }

        var effectiveTargetUrl = targetUrl;
        var effectiveTargetHtml = targetHtml;
        if (hasPdfFile && string.IsNullOrWhiteSpace(effectiveTargetUrl) && string.IsNullOrWhiteSpace(effectiveTargetHtml))
        {
            effectiveTargetUrl = new Uri(Path.GetFullPath(pdfPath!)).AbsoluteUri;
        }

        if (hasNativeHostExecutor)
        {
            var nativeRes = NativeSilentPrintWithScaleExecutor != null
                ? await NativeSilentPrintWithScaleExecutor(effectiveTargetUrl, effectiveTargetHtml, targetPrinter, effectiveIsThermal, scaleMode)
                : await NativeSilentPrintExecutor!(effectiveTargetUrl, effectiveTargetHtml, targetPrinter, effectiveIsThermal);
            if (nativeRes.Ok)
            {
                return new
                {
                    ok = true,
                    printer = targetPrinter,
                    engine = nativeRes.Engine ?? (hasPdfFile ? "webview2-pdf-fallback" : "webview2-silent-print")
                };
            }

            if (hasPdfFile && OperatingSystem.IsWindows())
            {
                var shellRes = await _rawPrinterService.SpoolPdfViaShellPrintToAsync(pdfPath!, targetPrinter, cancellationToken);
                if (shellRes.Ok)
                {
                    return new
                    {
                        ok = true,
                        printer = targetPrinter,
                        engine = shellRes.Engine ?? "shell-printto"
                    };
                }
            }

            return new
            {
                ok = false,
                printer = targetPrinter,
                engine = nativeRes.Engine ?? (hasPdfFile ? "webview2-pdf-fallback" : "webview2-silent-print"),
                error = nativeRes.Error ?? $"Failed to print to '{targetPrinter}'."
            };
        }

        if (hasPdfFile && OperatingSystem.IsWindows())
        {
            var shellRes = await _rawPrinterService.SpoolPdfViaShellPrintToAsync(pdfPath!, targetPrinter, cancellationToken);
            if (shellRes.Ok)
            {
                return new
                {
                    ok = true,
                    printer = targetPrinter,
                    engine = shellRes.Engine ?? "shell-printto"
                };
            }
        }

        return new
        {
            ok = false,
            fallbackToBrowserPrint = true,
            printer = targetPrinter,
            engine = "webview2-silent-print",
            error = "Desktop silent print host is unavailable in this environment."
        };
    }

    public async Task<object> KickCashDrawerAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var printerName = "";
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("deviceName", out var dn) && dn.ValueKind == JsonValueKind.String)
        {
            printerName = (dn.GetString() ?? "").Trim();
        }
        if (string.IsNullOrWhiteSpace(printerName))
        {
            printerName = ReadPreferredPrinterConfig();
        }

        var res = await _rawPrinterService.KickCashDrawerAsync(string.IsNullOrWhiteSpace(printerName) ? null : printerName);
        return new
        {
            ok = res.Ok,
            printer = res.Printer,
            error = res.Error
        };
    }

    public async Task<object> PrintThermalRawAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var printerName = "";
        var text = "";
        var kickDrawer = false;
        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("deviceName", out var dn) && dn.ValueKind == JsonValueKind.String)
                printerName = (dn.GetString() ?? "").Trim();
            if (payload.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String)
                text = txt.GetString() ?? "";
            if (payload.TryGetProperty("kickDrawer", out var kd) && kd.ValueKind == JsonValueKind.True)
                kickDrawer = true;
        }
        if (string.IsNullOrWhiteSpace(printerName))
        {
            var strategy = await ResolvePrinterStrategyAsync(isThermalDoc: true);
            printerName = !string.IsNullOrWhiteSpace(strategy.DeviceName)
                ? strategy.DeviceName
                : ReadPreferredPrinterConfig();
        }

        var bytes = _rawPrinterService.BuildEscPosReceiptPayload(text, kickDrawer: kickDrawer);
        var res = _rawPrinterService.SendRawBytesToPrinter(printerName, bytes, "Karobar Thermal Receipt");
        return new
        {
            ok = res.Ok,
            printer = printerName,
            bytesWritten = res.BytesWritten,
            error = res.Error
        };
    }

    public Task<object> silentPrint(JsonElement options) => SilentPrintAsync(options);

    public async Task<object> PrintUrlAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var rawUrl = "";
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("url", out var uProp) &&
            uProp.ValueKind == JsonValueKind.String)
        {
            rawUrl = (uProp.GetString() ?? "").Trim();
        }
        else if (payload.ValueKind == JsonValueKind.String)
        {
            rawUrl = (payload.GetString() ?? "").Trim();
        }

        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return new { ok = false, error = "Missing print URL" };
        }

        var baseUrl = _networkConfig.GetAppBaseUrl();
        var resolvedUrl = rawUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                          rawUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? rawUrl
            : $"{baseUrl}{(rawUrl.StartsWith('/') ? "" : "/")}{rawUrl}";

        if (!Regex.IsMatch(resolvedUrl, @"[?&]silent_print=", RegexOptions.IgnoreCase) &&
            !Regex.IsMatch(resolvedUrl, @"[?&]signature=", RegexOptions.IgnoreCase))
        {
            resolvedUrl += (resolvedUrl.Contains('?') ? "&" : "?") + "silent_print=1";
        }

        return await ExecuteSilentPrintCoreAsync(payload, targetUrl: resolvedUrl, targetHtml: null, cancellationToken);
    }

    public Task<object> printUrl(JsonElement payload) => PrintUrlAsync(payload);

    public async Task<object> PrintHtmlAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var rawHtml = "";
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("html", out var hProp) &&
            hProp.ValueKind == JsonValueKind.String)
        {
            rawHtml = hProp.GetString() ?? "";
        }
        else if (payload.ValueKind == JsonValueKind.String)
        {
            rawHtml = payload.GetString() ?? "";
        }

        if (string.IsNullOrWhiteSpace(rawHtml))
        {
            return new { ok = false, error = "Missing print HTML content" };
        }

        // Neutralize inline window.print() / window.close() calls inside the HTML
        var sanitizedHtml = Regex.Replace(rawHtml, @"window\.print\s*\(\s*\)", "void(0)");
        sanitizedHtml = Regex.Replace(sanitizedHtml, @"window\.close\s*\(\s*\)", "void(0)");
        sanitizedHtml = InjectBaseHrefForPrintHtml(sanitizedHtml, _networkConfig.GetAppBaseUrl());

        return await ExecuteSilentPrintCoreAsync(payload, targetUrl: null, targetHtml: sanitizedHtml, cancellationToken);
    }

    public Task<object> printHtml(JsonElement payload) => PrintHtmlAsync(payload);

    public static (bool? IsThermal, string? ScaleMode) ExtractPrintMetaHints(string? html)
        => Karobar.Printing.PrintTemplateRenderer.ExtractPrintMetaHints(html);

    public static string InjectBaseHrefForPrintHtml(string html, string appBaseUrl)
    {
        if (string.IsNullOrWhiteSpace(html) || string.IsNullOrWhiteSpace(appBaseUrl))
        {
            return html;
        }
        if (Regex.IsMatch(html, @"<base\b", RegexOptions.IgnoreCase))
        {
            return html;
        }

        var normalizedBase = appBaseUrl.TrimEnd('/') + "/";
        var escapedBase = System.Net.WebUtility.HtmlEncode(normalizedBase);
        var baseTag = $"<base href=\"{escapedBase}\">";

        var headMatch = Regex.Match(html, @"<head\b[^>]*>", RegexOptions.IgnoreCase);
        if (headMatch.Success)
        {
            return html.Insert(headMatch.Index + headMatch.Length, baseTag);
        }

        return baseTag + html;
    }

    private static async Task<(bool Ok, string? Error)> SpoolPdfViaSumatraAsync(
        string sumatraExe,
        string pdfFilePath,
        string? deviceName,
        bool isThermal,
        string scaleMode,
        CancellationToken cancellationToken)
    {
        try
        {
            var effectiveScale = scaleMode is "noscale" or "fit" or "shrink"
                ? scaleMode
                : isThermal
                    ? "noscale"
                    : "fit";

            var psi = new ProcessStartInfo
            {
                FileName = sumatraExe,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };

            if (!string.IsNullOrWhiteSpace(deviceName))
            {
                psi.ArgumentList.Add("-print-to");
                psi.ArgumentList.Add(deviceName);
            }
            else
            {
                psi.ArgumentList.Add("-print-to-default");
            }

            psi.ArgumentList.Add("-silent");
            psi.ArgumentList.Add("-print-settings");
            psi.ArgumentList.Add(effectiveScale);
            psi.ArgumentList.Add(pdfFilePath);

            using var proc = Process.Start(psi);
            if (proc == null) return (false, "Failed to start SumatraPDF process");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var stderr = await proc.StandardError.ReadToEndAsync(cts.Token);
            await proc.WaitForExitAsync(cts.Token);

            if (proc.ExitCode != 0)
            {
                return (false, string.IsNullOrWhiteSpace(stderr) ? "SumatraPDF spooler failed" : stderr.Trim());
            }

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    // =========================================================================
    // 11 & 12. Native Dialogs (selectDirectory, selectSqliteFile)
    // =========================================================================

#if WINDOWS_DESKTOP
    private System.Windows.Forms.Form? ResolveOwnerForm()
    {
        try
        {
            var activeWin = _windowManager.GetActiveWindow();
            if (activeWin is { IsDestroyed: false } && activeWin.Hwnd != IntPtr.Zero)
            {
                if (System.Windows.Forms.Control.FromHandle(activeWin.Hwnd) is System.Windows.Forms.Form matched &&
                    !matched.IsDisposed && matched.IsHandleCreated)
                {
                    return matched;
                }
            }

            if (System.Windows.Forms.Form.ActiveForm is { IsDisposed: false, IsHandleCreated: true } activeForm)
            {
                return activeForm;
            }

            return System.Windows.Forms.Application.OpenForms
                .OfType<NativeWebView2Form>()
                .FirstOrDefault(f => !f.IsDisposed && f.IsHandleCreated);
        }
        catch
        {
            return null;
        }
    }

    private Task<T> RunWinFormsDialogAsync<T>(
        Func<System.Windows.Forms.IWin32Window?, T> showDialog,
        T fallbackValue)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = ResolveOwnerForm();

        if (owner is { IsDisposed: false, IsHandleCreated: true })
        {
            try
            {
                owner.BeginInvoke((Action)(() =>
                {
                    try
                    {
                        var dialogOwner = !owner.IsDisposed && owner.IsHandleCreated ? owner : null;
                        tcs.TrySetResult(showDialog(dialogOwner));
                    }
                    catch
                    {
                        tcs.TrySetResult(fallbackValue);
                    }
                }));
                return tcs.Task;
            }
            catch
            {
                // Fall through to STA thread fallback
            }
        }

        var staThread = new Thread(() =>
        {
            try
            {
                tcs.TrySetResult(showDialog(null));
            }
            catch
            {
                tcs.TrySetResult(fallbackValue);
            }
        });
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.IsBackground = true;
        staThread.Start();
        return tcs.Task;
    }
#endif

    public Task<string?> SelectDirectoryAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult<string?>(null);
        }

#if WINDOWS_DESKTOP
        return RunWinFormsDialogAsync<string?>(owner =>
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Backup Folder",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            return dlg.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK &&
                   !string.IsNullOrWhiteSpace(dlg.SelectedPath)
                ? dlg.SelectedPath
                : null;
        }, null);
#else
        return Task.FromResult<string?>(null);
#endif
    }

    public Task<string?> selectDirectory() => SelectDirectoryAsync();

    public Task<object> SelectSqliteFileAsync()
    {
        object canceledResult = new { canceled = true, filePath = (string?)null };
        if (!OperatingSystem.IsWindows())
        {
            return Task.FromResult(canceledResult);
        }

#if WINDOWS_DESKTOP
        return RunWinFormsDialogAsync<object>(owner =>
        {
            using var dlg = new System.Windows.Forms.OpenFileDialog
            {
                Title = "Select Database or CSV File to Import",
                Filter = "Database & Migration Files (*.sqlite;*.sqlite3;*.db;*.db3;*.zip;*.gz;*.sql;*.csv)|*.sqlite;*.sqlite3;*.db;*.db3;*.zip;*.gz;*.sql;*.csv|SQLite Databases (*.sqlite;*.sqlite3;*.db;*.db3)|*.sqlite;*.sqlite3;*.db;*.db3|SQL & CSV Files (*.sql;*.gz;*.zip;*.csv)|*.sql;*.gz;*.zip;*.csv|All Files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };
            if (dlg.ShowDialog(owner) == System.Windows.Forms.DialogResult.OK &&
                !string.IsNullOrWhiteSpace(dlg.FileName))
            {
                return new { canceled = false, filePath = (string?)dlg.FileName };
            }
            return new { canceled = true, filePath = (string?)null };
        }, canceledResult);
#else
        return Task.FromResult(canceledResult);
#endif
    }

    public Task<object> selectSqliteFile() => SelectSqliteFileAsync();

    // =========================================================================
    // 13. runBackupNow (karobar:run-backup-now)
    // =========================================================================

    public async Task<object> RunBackupNowAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Try invoking via registered C# CLI command runner or BackupService first
            var sp = _serviceProviderAccessor?.Invoke();
            if (sp != null)
            {
                var cliExecuted = await CommandLineRouter.TryExecuteInProcessCliAsync(
                    sp,
                    ["backup:run-scheduled", "--force"],
                    cancellationToken);
                if (cliExecuted.Handled && cliExecuted.ExitCode == 0)
                {
                    return new { ok = true };
                }
            }

            // Fallback: create WAL-safe SQLite Online Backup snapshot in storage/app/backups
            var dbPath = Path.Combine(_networkConfig.AppRoot, "database", "database.sqlite");
            if (File.Exists(dbPath))
            {
                var backupDir = Path.Combine(_networkConfig.AppRoot, "storage", "app", "backups");
                Directory.CreateDirectory(backupDir);
                var destFile = Path.Combine(backupDir, $"backup-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.sqlite");
                using var srcConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;Pooling=False;");
                using var dstConn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={destFile};Mode=ReadWriteCreate;Pooling=False;");
                await srcConn.OpenAsync(cancellationToken);
                await dstConn.OpenAsync(cancellationToken);
                using (var pragmaCmd = srcConn.CreateCommand())
                {
                    pragmaCmd.CommandText = "PRAGMA busy_timeout=15000;";
                    await pragmaCmd.ExecuteNonQueryAsync(cancellationToken);
                }
                srcConn.BackupDatabase(dstConn);
            }

            return new { ok = true };
        }
        catch (Exception ex)
        {
            return new { ok = false, message = ex.Message };
        }
    }

    public Task<object> runBackupNow() => RunBackupNowAsync();

    // =========================================================================
    // 14 & 15. OTA Software Updates (checkForUpdates, downloadAndInstallUpdate)
    // =========================================================================

    public LocalVersionMeta ReadLocalVersionMeta()
    {
        var defaults = new LocalVersionMeta();
        try
        {
            var versionFile = Path.Combine(_networkConfig.AppRoot, "public", "version.json");
            if (File.Exists(versionFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(versionFile, Encoding.UTF8));
                var root = doc.RootElement;
                var runtimeLayout = root.TryGetProperty("runtime_layout", out var rl) && rl.ValueKind == JsonValueKind.String
                    ? rl.GetString() ?? ""
                    : "";
                if (string.IsNullOrWhiteSpace(runtimeLayout) &&
                    File.Exists(Path.Combine(_networkConfig.AppRoot, "Karobar.Core.dll")))
                {
                    runtimeLayout = "split-v2";
                }
                return new LocalVersionMeta
                {
                    AppName = root.TryGetProperty("app_name", out var an) && an.ValueKind == JsonValueKind.String
                        ? an.GetString() ?? defaults.AppName
                        : defaults.AppName,
                    Version = (root.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString() ?? defaults.Version
                        : defaults.Version).TrimStart('v', 'V'),
                    BuildNumber = root.TryGetProperty("build_number", out var bn) && bn.TryGetInt32(out var bNum)
                        ? bNum
                        : defaults.BuildNumber,
                    CommitSha = root.TryGetProperty("commit_sha", out var cs) && cs.ValueKind == JsonValueKind.String
                        ? cs.GetString() ?? defaults.CommitSha
                        : defaults.CommitSha,
                    Repository = root.TryGetProperty("repository", out var rp) && rp.ValueKind == JsonValueKind.String
                        ? rp.GetString() ?? defaults.Repository
                        : defaults.Repository,
                    RuntimeLayout = runtimeLayout,
                    DepsFingerprint = root.TryGetProperty("deps_fingerprint", out var df) && df.ValueKind == JsonValueKind.String
                        ? df.GetString() ?? ""
                        : ""
                };
            }
            if (File.Exists(Path.Combine(_networkConfig.AppRoot, "Karobar.Core.dll")))
            {
                defaults.RuntimeLayout = "split-v2";
            }
        }
        catch
        {
            // Ignore malformed version.json
        }

        return defaults;
    }

    public static bool CompareSemverNewer(string? remoteVer, string? localVer)
    {
        var rParts = (remoteVer ?? "0.0.0").TrimStart('v', 'V')
            .Split('.')
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .ToArray();
        var lParts = (localVer ?? "0.0.0").TrimStart('v', 'V')
            .Split('.')
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .ToArray();

        var maxLen = Math.Max(rParts.Length, lParts.Length);
        for (var i = 0; i < maxLen; i++)
        {
            var r = i < rParts.Length ? rParts[i] : 0;
            var l = i < lParts.Length ? lParts[i] : 0;
            if (r > l) return true;
            if (r < l) return false;
        }

        return false;
    }

    public static bool IsTrustedReleaseUrl(string? rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var host = parsed.Host.ToLowerInvariant();
        return host == "github.com" || host.EndsWith(".githubusercontent.com", StringComparison.Ordinal);
    }

    public static (bool CanPatch, string? Reason) EvaluatePatchEligibility(
        LocalVersionMeta local,
        string? patchUrl,
        string? remoteLayout,
        string? remoteDeps,
        IReadOnlyCollection<string>? compatibleDepsFingerprints,
        string? minPatchVer,
        bool hasBundledWaRuntime)
    {
        if (string.IsNullOrWhiteSpace(patchUrl))
        {
            return (false, "missing_patch_url");
        }

        var layoutCompatible = string.IsNullOrEmpty(remoteLayout) ||
                               string.Equals(remoteLayout, local.RuntimeLayout, StringComparison.OrdinalIgnoreCase);
        if (!layoutCompatible)
        {
            return (false, "runtime_layout_mismatch");
        }

        var depsCompatible = string.IsNullOrEmpty(remoteDeps) ||
                             (!string.IsNullOrEmpty(local.DepsFingerprint) &&
                              (string.Equals(remoteDeps, local.DepsFingerprint, StringComparison.OrdinalIgnoreCase) ||
                               (compatibleDepsFingerprints != null &&
                                compatibleDepsFingerprints.Any(fp =>
                                    !string.IsNullOrWhiteSpace(fp) &&
                                    string.Equals(fp.Trim(), local.DepsFingerprint, StringComparison.OrdinalIgnoreCase)))));
        if (!depsCompatible)
        {
            return (false, "deps_fingerprint_mismatch");
        }

        if (!hasBundledWaRuntime)
        {
            return (false, "missing_whatsapp_runtime");
        }

        if (CompareSemverNewer(minPatchVer ?? "2.0.0", local.Version))
        {
            return (false, "below_min_patch_version");
        }

        return (true, null);
    }

    public async Task<object> CheckForUpdatesAsync(JsonElement? payload = null, CancellationToken cancellationToken = default)
    {
        var local = ReadLocalVersionMeta();
        var repo = string.IsNullOrWhiteSpace(local.Repository) ? "shzdasim/Karobar-App" : local.Repository;

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 6
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Karobar-App-Desktop-Updater/2.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };

        // 1. Primary manifest: dedicated `native-latest` rolling channel (never collides with Electron `latest`)
        string[] manifestCandidates =
        [
            $"https://github.com/{repo}/releases/download/native-latest/latest-native-release.json",
            $"https://github.com/{repo}/releases/latest/download/latest-native-release.json"
        ];

        foreach (var manifestUrl in manifestCandidates)
        {
            try
            {
                var jsonText = await client.GetStringAsync(manifestUrl, cancellationToken);
                using var doc = JsonDocument.Parse(jsonText);
                var remote = doc.RootElement;

                if (remote.TryGetProperty("version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
                {
                    var remoteVer = (vProp.GetString() ?? "").TrimStart('v', 'V');
                    var remoteBuild = remote.TryGetProperty("build_number", out var bProp) && bProp.TryGetInt32(out var bVal) ? bVal : 0;
                    var isNewer = remoteBuild > 0 && local.BuildNumber > 0 && remoteBuild != local.BuildNumber
                        ? remoteBuild > local.BuildNumber
                        : CompareSemverNewer(remoteVer, local.Version);

                    var minPatchVer = (remote.TryGetProperty("min_patch_version", out var mpProp) && mpProp.ValueKind == JsonValueKind.String
                        ? mpProp.GetString() ?? "2.0.0"
                        : "2.0.0").TrimStart('v', 'V');

                    var patchUrl = remote.TryGetProperty("patch_url", out var puProp) && puProp.ValueKind == JsonValueKind.String
                        ? puProp.GetString()
                        : null;

                    var remoteLayout = remote.TryGetProperty("runtime_layout", out var rlProp) && rlProp.ValueKind == JsonValueKind.String
                        ? rlProp.GetString() ?? ""
                        : "";

                    var remoteDeps = remote.TryGetProperty("deps_fingerprint", out var rdProp) && rdProp.ValueKind == JsonValueKind.String
                        ? rdProp.GetString() ?? ""
                        : "";

                    List<string>? compatibleDeps = null;
                    if (remote.TryGetProperty("compatible_deps_fingerprints", out var cdfProp) && cdfProp.ValueKind == JsonValueKind.Array)
                    {
                        compatibleDeps = cdfProp.EnumerateArray()
                            .Where(e => e.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(e.GetString()))
                            .Select(e => e.GetString()!.Trim())
                            .ToList();
                    }

                    // On Windows, if the local install does not yet have the bundled portable Node + Baileys WhatsApp runtime,
                    // require a one-time zero-data-loss full installer upgrade instead of a differential patch.
                    var hasBundledWaRuntime = !OperatingSystem.IsWindows() ||
                        (File.Exists(Path.Combine(_networkConfig.AppRoot, "runtime", "node", "node.exe")) &&
                         Directory.Exists(Path.Combine(_networkConfig.AppRoot, "runtime", "whatsapp-agent", "node_modules", "@whiskeysockets", "baileys")));

                    var (canPatch, patchIneligibleReason) = EvaluatePatchEligibility(
                        local,
                        patchUrl,
                        remoteLayout,
                        remoteDeps,
                        compatibleDeps,
                        minPatchVer,
                        hasBundledWaRuntime);

                    return new
                    {
                        ok = true,
                        update_available = isNewer,
                        is_patch = canPatch,
                        patch_ineligible_reason = patchIneligibleReason,
                        app_name = "Karobar-App Native",
                        current_version = local.Version,
                        current_build = local.BuildNumber,
                        latest_version = remoteVer,
                        latest_build = remoteBuild,
                        release_name = remote.TryGetProperty("release_name", out var rn) && rn.ValueKind == JsonValueKind.String
                            ? rn.GetString()
                            : $"Karobar-App Native v{remoteVer}",
                        released_at = remote.TryGetProperty("released_at", out var ra) && ra.ValueKind == JsonValueKind.String
                            ? ra.GetString()
                            : "",
                        notes = remote.TryGetProperty("notes", out var nt) && nt.ValueKind == JsonValueKind.String
                            ? nt.GetString()
                            : "Performance improvements, bug fixes, and new features.",
                        download_url = remote.TryGetProperty("download_url", out var du) && du.ValueKind == JsonValueKind.String
                            ? du.GetString()
                            : $"https://github.com/{repo}/releases/download/native-latest/Karobar-Native-Setup.exe",
                        installer_size = remote.TryGetProperty("installer_size", out var isz) && isz.TryGetInt64(out var iSize) ? (long?)iSize : null,
                        bundle_url = remote.TryGetProperty("bundle_url", out var bu) && bu.ValueKind == JsonValueKind.String
                            ? bu.GetString()
                            : $"https://github.com/{repo}/releases/download/native-latest/Karobar-Native-Setup-Bundle.zip",
                        patch_url = canPatch ? patchUrl : null,
                        patch_size = canPatch && remote.TryGetProperty("patch_size", out var ps) && ps.TryGetInt64(out var pSize) ? (long?)pSize : null,
                        patch_sha256 = canPatch && remote.TryGetProperty("patch_sha256", out var pSha) && pSha.ValueKind == JsonValueKind.String
                            ? pSha.GetString()
                            : null,
                        installer_sha256 = remote.TryGetProperty("installer_sha256", out var iSha) && iSha.ValueKind == JsonValueKind.String
                            ? iSha.GetString()
                            : null,
                        deps_fingerprint = remoteDeps,
                        local_deps_fingerprint = local.DepsFingerprint,
                        min_patch_version = minPatchVer,
                        release_url = remote.TryGetProperty("release_url", out var ru) && ru.ValueKind == JsonValueKind.String
                            ? ru.GetString()
                            : $"https://github.com/{repo}/releases/tag/native-latest",
                        zero_data_loss = true
                    };
                }
            }
            catch
            {
                // Try next manifest candidate
            }
        }

        // 2. Fallback: GitHub Releases REST API (strictly `native-latest` tag so Electron `latest` is never returned)
        try
        {
            var apiText = await client.GetStringAsync($"https://api.github.com/repos/{repo}/releases/tags/native-latest", cancellationToken);
            using var doc = JsonDocument.Parse(apiText);
            var apiData = doc.RootElement;

            if (apiData.TryGetProperty("tag_name", out var tagProp) && tagProp.ValueKind == JsonValueKind.String)
            {
                var rawTag = tagProp.GetString() ?? "";
                var releaseName = apiData.TryGetProperty("name", out var rn) && rn.ValueKind == JsonValueKind.String
                    ? rn.GetString() ?? ""
                    : "";
                var releaseBody = apiData.TryGetProperty("body", out var bd) && bd.ValueKind == JsonValueKind.String
                    ? bd.GetString() ?? ""
                    : "";

                var remoteVer = Regex.Replace(rawTag, @"^(native-)?[vV]", "");
                if (!Regex.IsMatch(remoteVer, @"^\d+\.\d+\.\d+$"))
                {
                    var nameVerMatch = Regex.Match(releaseName, @"\bv?(\d+\.\d+\.\d+)\b");
                    if (nameVerMatch.Success)
                    {
                        remoteVer = nameVerMatch.Groups[1].Value;
                    }
                    else
                    {
                        var bodyVerMatch = Regex.Match(releaseBody, @"\bv?(\d+\.\d+\.\d+)\b");
                        remoteVer = bodyVerMatch.Success ? bodyVerMatch.Groups[1].Value : "";
                    }
                }

                var match = Regex.Match(remoteVer, @"^2\.0\.(\d+)$");
                var remoteBuild = match.Success && int.TryParse(match.Groups[1].Value, out var rb) ? rb : 0;
                var isNewer = !string.IsNullOrWhiteSpace(remoteVer) &&
                              (remoteBuild > 0 && local.BuildNumber > 0 && remoteBuild != local.BuildNumber
                                  ? remoteBuild > local.BuildNumber
                                  : CompareSemverNewer(remoteVer, local.Version));

                var downloadUrl = $"https://github.com/{repo}/releases/download/native-latest/Karobar-Native-Setup.exe";
                var bundleUrl = $"https://github.com/{repo}/releases/download/native-latest/Karobar-Native-Setup-Bundle.zip";
                long? installerSize = null;

                if (apiData.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var nProp) && nProp.ValueKind == JsonValueKind.String ? nProp.GetString() ?? "" : "";
                        var url = asset.TryGetProperty("browser_download_url", out var uProp) && uProp.ValueKind == JsonValueKind.String ? uProp.GetString() ?? "" : "";
                        if (!string.IsNullOrWhiteSpace(url) && name is "Karobar-Native-Setup.exe")
                        {
                            downloadUrl = url;
                            if (asset.TryGetProperty("size", out var isProp) && isProp.TryGetInt64(out var iSz))
                                installerSize = iSz;
                        }
                        if (!string.IsNullOrWhiteSpace(url) && name is "Karobar-Native-Setup-Bundle.zip")
                            bundleUrl = url;
                    }
                }

                // Never enable differential patch in the GitHub REST fallback because runtime_layout,
                // deps_fingerprint, min_patch_version, and patch_sha256 from native-version.json were not verified.
                return new
                {
                    ok = true,
                    update_available = isNewer,
                    is_patch = false,
                    app_name = "Karobar-App Native",
                    current_version = local.Version,
                    current_build = local.BuildNumber,
                    latest_version = string.IsNullOrWhiteSpace(remoteVer) ? local.Version : remoteVer,
                    latest_build = remoteBuild,
                    release_name = !string.IsNullOrWhiteSpace(releaseName)
                        ? releaseName
                        : $"Karobar-App Native v{(string.IsNullOrWhiteSpace(remoteVer) ? local.Version : remoteVer)}",
                    released_at = apiData.TryGetProperty("published_at", out var pa) && pa.ValueKind == JsonValueKind.String
                        ? pa.GetString()
                        : "",
                    notes = !string.IsNullOrWhiteSpace(releaseBody)
                        ? releaseBody
                        : "New Karobar-App Native release available.",
                    download_url = downloadUrl,
                    installer_size = installerSize,
                    bundle_url = bundleUrl,
                    patch_url = (string?)null,
                    patch_size = (long?)null,
                    patch_sha256 = (string?)null,
                    installer_sha256 = (string?)null,
                    min_patch_version = "2.0.0",
                    release_url = apiData.TryGetProperty("html_url", out var hu) && hu.ValueKind == JsonValueKind.String
                        ? hu.GetString()
                        : $"https://github.com/{repo}/releases/tag/native-latest",
                    zero_data_loss = true
                };
            }
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                update_available = false,
                current_version = local.Version,
                current_build = local.BuildNumber,
                message = ex.Message
            };
        }

        return new
        {
            ok = false,
            update_available = false,
            current_version = local.Version,
            current_build = local.BuildNumber,
            message = "Unable to reach GitHub Releases right now."
        };
    }

    public Task<object> checkForUpdates(JsonElement? payload = null) => CheckForUpdatesAsync(payload);

    public async Task<object> DownloadAndInstallUpdateAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var mode = "download_and_install";
        var downloadOnly = false;
        var forceFull = false;
        var isPatchFlag = false;
        string? candidatePatchUrl = null;
        string? candidatePatchSha256 = null;
        string? candidateInstallerSha256 = null;
        string? candidateDownloadUrl = null;
        var rawVersion = "latest";

        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("mode", out var mProp) && mProp.ValueKind == JsonValueKind.String)
                mode = mProp.GetString() ?? mode;
            if (payload.TryGetProperty("downloadOnly", out var doProp) && doProp.ValueKind == JsonValueKind.True)
                downloadOnly = true;
            if (payload.TryGetProperty("forceFull", out var ffProp) && ffProp.ValueKind == JsonValueKind.True)
                forceFull = true;
            if (payload.TryGetProperty("isPatch", out var ip1) && ip1.ValueKind == JsonValueKind.True)
                isPatchFlag = true;
            if (payload.TryGetProperty("is_patch", out var ip2) && ip2.ValueKind == JsonValueKind.True)
                isPatchFlag = true;
            if (payload.TryGetProperty("patchUrl", out var pu1) && pu1.ValueKind == JsonValueKind.String)
                candidatePatchUrl = pu1.GetString();
            if (payload.TryGetProperty("patch_url", out var pu2) && pu2.ValueKind == JsonValueKind.String)
                candidatePatchUrl ??= pu2.GetString();
            if (payload.TryGetProperty("patchSha256", out var ps1) && ps1.ValueKind == JsonValueKind.String)
                candidatePatchSha256 = ps1.GetString();
            if (payload.TryGetProperty("patch_sha256", out var ps2) && ps2.ValueKind == JsonValueKind.String)
                candidatePatchSha256 ??= ps2.GetString();
            if (payload.TryGetProperty("installerSha256", out var is1) && is1.ValueKind == JsonValueKind.String)
                candidateInstallerSha256 = is1.GetString();
            if (payload.TryGetProperty("installer_sha256", out var is2) && is2.ValueKind == JsonValueKind.String)
                candidateInstallerSha256 ??= is2.GetString();
            if (payload.TryGetProperty("downloadUrl", out var du1) && du1.ValueKind == JsonValueKind.String)
                candidateDownloadUrl = du1.GetString();
            if (payload.TryGetProperty("download_url", out var du2) && du2.ValueKind == JsonValueKind.String)
                candidateDownloadUrl ??= du2.GetString();
            if (payload.TryGetProperty("version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
                rawVersion = vProp.GetString() ?? "latest";
        }

        if (downloadOnly && mode == "download_and_install")
        {
            mode = "download";
        }

        var isPatch = isPatchFlag && !forceFull && !string.IsNullOrWhiteSpace(candidatePatchUrl);
        var expectedSha256 = isPatch ? candidatePatchSha256 : candidateInstallerSha256;
        if (string.IsNullOrWhiteSpace(expectedSha256) || !Regex.IsMatch(expectedSha256.Trim(), "^[0-9a-fA-F]{64}$"))
        {
            return new
            {
                ok = false,
                message = "Update package is missing a valid SHA-256 checksum and cannot be installed safely."
            };
        }

        var versionLabel = Regex.Replace(rawVersion, @"[^a-zA-Z0-9._-]", "");
        if (string.IsNullOrWhiteSpace(versionLabel)) versionLabel = "latest";

        var downloadUrl = isPatch
            ? (IsTrustedReleaseUrl(candidatePatchUrl)
                ? candidatePatchUrl!
                : "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/Karobar-Native-Patch.zip")
            : (IsTrustedReleaseUrl(candidateDownloadUrl)
                ? candidateDownloadUrl!
                : "https://github.com/shzdasim/Karobar-App/releases/download/native-latest/Karobar-Native-Setup.exe");

        var fileExt = isPatch ? "zip" : "exe";
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"Karobar-Native-Update-{versionLabel}.{fileExt}");
        var minValidSize = isPatch ? 100 * 1024 : 5 * 1024 * 1024;

        // Serialize concurrent update/download invocations (e.g. multiple open tabs or fallback retries)
        // so two callers never open or overwrite the same temp archive simultaneously.
        await _updateGate.WaitAsync(cancellationToken);
        try
        {
            var alreadyDownloaded = File.Exists(tempFilePath) && new FileInfo(tempFilePath).Length >= minValidSize;
            if (alreadyDownloaded && !VerifyFileSha256(tempFilePath, expectedSha256))
            {
                try { File.Delete(tempFilePath); } catch { }
                alreadyDownloaded = false;
            }

            if (!alreadyDownloaded)
            {
                PublishUpdateProgress(new
                {
                    status = "starting",
                    percent = 2,
                    is_patch = isPatch,
                    message = isPatch ? "Starting fast differential patch download…" : "Starting background update download…"
                });

                var partPath = $"{tempFilePath}.{Guid.NewGuid():N}.part";
                try
                {
                    await DownloadFileWithProgressAsync(downloadUrl, partPath, isPatch, cancellationToken);

                    var downloadedSize = File.Exists(partPath) ? new FileInfo(partPath).Length : 0L;
                    if (downloadedSize < minValidSize)
                    {
                        throw new InvalidOperationException($"Downloaded update file is unexpectedly small ({downloadedSize} bytes). Please retry.");
                    }

                    if (!VerifyFileSha256(partPath, expectedSha256))
                    {
                        throw new InvalidOperationException("Downloaded update failed SHA-256 integrity verification. Please retry.");
                    }

                    File.Move(partPath, tempFilePath, overwrite: true);
                }
                catch
                {
                    try { if (File.Exists(partPath)) File.Delete(partPath); } catch { }
                    throw;
                }
            }

            if (mode == "download")
            {
                PublishUpdateProgress(new
                {
                    status = "downloaded",
                    percent = 100,
                    is_patch = isPatch,
                    installerPath = tempFilePath,
                    message = isPatch
                        ? "Fast patch downloaded & verified. Ready to apply."
                        : "Update downloaded and ready to install."
                });

                return new
                {
                    ok = true,
                    downloaded = true,
                    is_patch = isPatch,
                    installing = false,
                    installerPath = tempFilePath
                };
            }

            await RunBackupNowAsync(cancellationToken);

            PublishUpdateProgress(new
            {
                status = "installing",
                percent = 100,
                is_patch = isPatch,
                message = isPatch
                    ? "Applying fast differential patch & restarting Karobar-App Native…"
                    : "Launching Karobar-App Native installer & restarting…"
            });

            if (OperatingSystem.IsWindows() && File.Exists(tempFilePath))
            {
                var installDir = _networkConfig.AppRoot.TrimEnd(Path.DirectorySeparatorChar);
                if (isPatch)
                {
                    LaunchWindowsDifferentialPatchSwap(tempFilePath, installDir, _networkConfig.IsTerminalMode());
                }
                else
                {
                    var otaFlagPath = Path.Combine(Path.GetTempPath(), "karobar-native-ota-upgrade.flag");
                    try
                    {
                        await File.WriteAllTextAsync(otaFlagPath, installDir, new UTF8Encoding(false), cancellationToken);
                    }
                    catch
                    {
                        // Ignore flag error
                    }

                    // Wait for Karobar-Native.exe to exit & release Global\KarobarNativeErpV2Mutex BEFORE starting Inno Setup
                    LaunchWindowsInstallerAfterExit(tempFilePath, installDir);
                }

                // Allow the IPC/HTTP response to flush cleanly, then guarantee a prompt exit so DLL locks release immediately
                _ = Task.Run(async () =>
                {
                    await Task.Delay(350);
                    try { _windowManager.OnQuitRequested?.Invoke(); } catch { }
                    await Task.Delay(1500);
                    Environment.Exit(0);
                });
            }

            return new { ok = true, installing = true, is_patch = isPatch };
        }
        catch (Exception ex)
        {
            return new
            {
                ok = false,
                message = ex.Message
            };
        }
        finally
        {
            _updateGate.Release();
        }
    }

    public static readonly string[] ProtectedPatchExclusionPrefixes =
    [
        "database/database.sqlite",
        "database\\database.sqlite",
        "storage/app/.machine-id",
        "storage\\app\\.machine-id",
        "storage/app/.license-clock",
        "storage\\app\\.license-clock",
        "storage/app/.app-key",
        "storage\\app\\.app-key",
        "storage/app/network-mode.json",
        "storage\\app\\network-mode.json",
        "storage/app/cube-learned-knowledge.json",
        "storage\\app\\cube-learned-knowledge.json",
        "storage/app/public/",
        "storage\\app\\public\\",
        "storage/app/whatsapp-auth/",
        "storage\\app\\whatsapp-auth\\",
        "storage/app/whatsapp-agent/",
        "storage\\app\\whatsapp-agent\\",
        "storage/app/backups/",
        "storage\\app\\backups\\",
        "storage/backups/",
        "storage\\backups\\",
        "storage/logs/",
        "storage\\logs\\",
        ".env"
    ];

    public static bool IsProtectedPatchPath(string relativePath)
    {
        var norm = (relativePath ?? "").Replace('\\', '/').TrimStart('/');
        if (string.IsNullOrEmpty(norm)) return true;
        if (norm.Equals(".env", StringComparison.OrdinalIgnoreCase)) return true;
        return ProtectedPatchExclusionPrefixes.Any(p =>
            norm.StartsWith(p.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase));
    }

    public static bool VerifyFileSha256(string filePath, string expectedSha256Hex)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256Hex) || !File.Exists(filePath)) return false;
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(filePath);
        var actualHex = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        return string.Equals(actualHex, expectedSha256Hex.Trim().ToLowerInvariant(), StringComparison.Ordinal);
    }

    private static bool CanWriteToInstallDirectory(string installDir)
    {
        try
        {
            var probe = Path.Combine(installDir, $".karobar-patch-probe-{Environment.ProcessId}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);

            // Also test overwriting an existing binary/metadata file attribute check if present
            var versionFile = Path.Combine(installDir, "public", "version.json");
            if (File.Exists(versionFile))
            {
                using var fs = new FileStream(versionFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void LaunchWindowsDifferentialPatchSwap(string patchZipPath, string installDir, bool isTerminalMode)
    {
        var pid = Environment.ProcessId;
        var helperScriptPath = Path.Combine(Path.GetTempPath(), $"karobar-apply-patch-{pid}.ps1");
        var exePath = Path.Combine(installDir, Program.ExecutableFileName);
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), NetworkConfigManager.AppDataFolderName);
        try { Directory.CreateDirectory(logDir); } catch { }
        var logPath = Path.Combine(logDir, "patch-update.log");
        var needsElevation = !CanWriteToInstallDirectory(installDir);
        var isTerminalLiteral = isTerminalMode ? "$true" : "$false";

        var ps1 = $$"""
            $ErrorActionPreference = 'Stop'
            $targetPid = {{pid}}
            $zipPath = '{{patchZipPath.Replace("'", "''")}}'
            $installDir = '{{installDir.Replace("'", "''")}}'
            $exePath = '{{exePath.Replace("'", "''")}}'
            $logPath = '{{logPath.Replace("'", "''")}}'
            $isTerminalMode = {{isTerminalLiteral}}
            $stageDir = Join-Path $env:TEMP "karobar-patch-stage-$targetPid"
            $rollbackDir = Join-Path $env:TEMP "karobar-patch-rollback-$targetPid"
            $backedUpRels = New-Object System.Collections.Generic.List[string]
            $deletedRels = New-Object System.Collections.Generic.List[string]
            $createdRels = New-Object System.Collections.Generic.List[string]

            function Write-Log([string]$msg) {
                try {
                    Add-Content -Path $logPath -Value ("[" + (Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + "] " + $msg) -Encoding UTF8
                } catch {}
            }

            Write-Log "=== Starting differential OTA patch swap (PID=$targetPid, TerminalMode=$isTerminalMode, Zip=$zipPath, InstallDir=$installDir) ==="

            try {
                # 1. Wait cleanly for Karobar-Native.exe to exit; force-close after 6s if still lingering so DLL locks release
                for ($i = 0; $i -lt 40; $i++) {
                    if (-not (Get-Process -Id $targetPid -ErrorAction SilentlyContinue)) { break }
                    if ($i -eq 24) {
                        Write-Log "Process $targetPid still active after 6s; requesting force termination to release DLL locks."
                        Stop-Process -Id $targetPid -Force -ErrorAction SilentlyContinue
                    }
                    Start-Sleep -Milliseconds 250
                }
                Start-Sleep -Milliseconds 350

                # 2. Extract patch zip cleanly using .NET ZipFile (fast, reliable across PowerShell 5.1 & 7)
                if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir -ErrorAction SilentlyContinue }
                if (Test-Path $rollbackDir) { Remove-Item -Recurse -Force $rollbackDir -ErrorAction SilentlyContinue }
                New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
                New-Item -ItemType Directory -Force -Path $rollbackDir | Out-Null
                Add-Type -AssemblyName System.IO.Compression.FileSystem
                [System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $stageDir)
                Write-Log "Extracted patch archive into $stageDir"

                $protected = @(
                    'database\database.sqlite',
                    'storage\app\.machine-id',
                    'storage\app\.license-clock',
                    'storage\app\.app-key',
                    'storage\app\network-mode.json',
                    'storage\app\cube-learned-knowledge.json',
                    'storage\app\public\',
                    'storage\app\whatsapp-auth\',
                    'storage\app\whatsapp-agent\',
                    'storage\app\backups\',
                    'storage\backups\',
                    'storage\logs\',
                    '.env'
                )

                $copied = 0
                $files = Get-ChildItem -Path $stageDir -Recurse -File
                foreach ($f in $files) {
                    $rel = $f.FullName.Substring($stageDir.Length).TrimStart('\', '/')
                    $skip = $false
                    foreach ($p in $protected) {
                        if ($rel.StartsWith($p, [System.StringComparison]::OrdinalIgnoreCase)) {
                            $skip = $true
                            break
                        }
                    }
                    if ($skip) { continue }

                    $dest = Join-Path $installDir $rel
                    $destDir = Split-Path -Parent $dest
                    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }

                    # Back up existing file before overwriting so a mid-patch failure can roll back cleanly
                    if (Test-Path $dest) {
                        $rbDest = Join-Path $rollbackDir $rel
                        $rbDir = Split-Path -Parent $rbDest
                        if (-not (Test-Path $rbDir)) { New-Item -ItemType Directory -Force -Path $rbDir | Out-Null }
                        Copy-Item -Path $dest -Destination $rbDest -Force
                        $backedUpRels.Add($rel)
                    } else {
                        $createdRels.Add($rel)
                    }

                    $ok = $false
                    $lastErr = $null
                    for ($attempt = 1; $attempt -le 8; $attempt++) {
                        try {
                            Copy-Item -Path $f.FullName -Destination $dest -Force
                            $ok = $true
                            break
                        } catch {
                            $lastErr = $_
                            Start-Sleep -Milliseconds 350
                        }
                    }
                    if (-not $ok) {
                        throw "Failed to overwrite $dest after 8 attempts: $lastErr"
                    }
                    $copied++
                }
                Write-Log "Successfully updated $copied files in $installDir"

                # Prune obsolete hashed Vite assets under public\build\assets\ when patch includes assets or manifest
                $stageAssetsDir = Join-Path $stageDir 'public\build\assets'
                $stageManifestPath = Join-Path $stageDir 'public\build\manifest.json'
                $targetAssetsDir = Join-Path $installDir 'public\build\assets'
                $hasStageAssets = Test-Path $stageAssetsDir
                $hasStageManifest = Test-Path $stageManifestPath
                if ((Test-Path $targetAssetsDir) -and ($hasStageAssets -or $hasStageManifest)) {
                    $keepAssetRels = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
                    $manifestValid = $true
                    if ($hasStageAssets) {
                        foreach ($sf in (Get-ChildItem -Path $stageAssetsDir -Recurse -File)) {
                            $sRel = $sf.FullName.Substring($stageAssetsDir.Length).TrimStart('\', '/')
                            [void]$keepAssetRels.Add($sRel)
                        }
                    }
                    if ($hasStageManifest) {
                        try {
                            $rawManifest = Get-Content -Path $stageManifestPath -Raw -Encoding UTF8
                            $trimmedManifest = if ($null -eq $rawManifest) { '' } else { $rawManifest.TrimStart().TrimStart([char]0xFEFF).TrimStart() }
                            if ([string]::IsNullOrWhiteSpace($trimmedManifest) -or -not $trimmedManifest.StartsWith('{')) {
                                $manifestValid = $false
                            } else {
                                $mJson = $trimmedManifest | ConvertFrom-Json
                                if (-not ($mJson -is [pscustomobject] -or $mJson -is [System.Collections.IDictionary])) {
                                    $manifestValid = $false
                                } else {
                                    foreach ($prop in $mJson.PSObject.Properties) {
                                        $entry = $prop.Value
                                        if ($null -eq $entry -or -not ($entry -is [pscustomobject] -or $entry -is [System.Collections.IDictionary])) { continue }
                                        foreach ($candidate in @($entry.file, $entry.css, $entry.assets)) {
                                            if ($null -eq $candidate) { continue }
                                            foreach ($item in @($candidate)) {
                                                if ($item -is [string] -and -not [string]::IsNullOrWhiteSpace($item)) {
                                                    $normItem = $item.Replace('/', '\').TrimStart('\')
                                                    if ($normItem.StartsWith('assets\', [System.StringComparison]::OrdinalIgnoreCase)) {
                                                        [void]$keepAssetRels.Add($normItem.Substring(7))
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        } catch {
                            $manifestValid = $false
                        }
                    }
                    if ($hasStageAssets -or $manifestValid) {
                        $prunedCount = 0
                        foreach ($ef in (Get-ChildItem -Path $targetAssetsDir -Recurse -File)) {
                            $assetRel = $ef.FullName.Substring($targetAssetsDir.Length).TrimStart('\', '/')
                            if (-not $keepAssetRels.Contains($assetRel)) {
                                $fullRel = 'public\build\assets\' + $assetRel
                                $rbDel = Join-Path $rollbackDir $fullRel
                                $rbDelDir = Split-Path -Parent $rbDel
                                if (-not (Test-Path $rbDelDir)) { New-Item -ItemType Directory -Force -Path $rbDelDir | Out-Null }
                                Copy-Item -Path $ef.FullName -Destination $rbDel -Force
                                $deletedRels.Add($fullRel)
                                Remove-Item -Path $ef.FullName -Force -ErrorAction SilentlyContinue
                                $prunedCount++
                            }
                        }
                        if ($prunedCount -gt 0) {
                            Write-Log "Pruned $prunedCount stale build asset(s) from public\build\assets"
                        }
                    } else {
                        Write-Log "Skipping stale asset prune because staged manifest.json is invalid or non-object and no staged assets directory is present."
                    }
                }

                if (Test-Path $exePath) {
                    if ($isTerminalMode) {
                        Write-Log "Terminal Mode detected - skipping local --cli install --upgrade-only."
                    } else {
                        Write-Log "Running post-patch --cli install --upgrade-only..."
                        $upgProc = Start-Process -FilePath $exePath -ArgumentList '--cli', 'install', '--upgrade-only' -WorkingDirectory $installDir -WindowStyle Hidden -Wait -PassThru
                        Write-Log "Post-patch schema upgrade exited with code $($upgProc.ExitCode)"
                        if ($upgProc.ExitCode -ne 0) {
                            throw "Post-patch schema/upgrade command exited with code $($upgProc.ExitCode)."
                        }
                    }
                }

                Remove-Item -Force $zipPath -ErrorAction SilentlyContinue
                Write-Log "Patch applied successfully!"
            } catch {
                Write-Log "ERROR: $($_.Exception.Message)"
                if ($backedUpRels.Count -gt 0 -or $deletedRels.Count -gt 0) {
                    Write-Log "Rolling back $($backedUpRels.Count) overwritten and $($deletedRels.Count) pruned file(s)..."
                    foreach ($rel in $backedUpRels) {
                        try {
                            $rbSrc = Join-Path $rollbackDir $rel
                            $dest = Join-Path $installDir $rel
                            if (Test-Path $rbSrc) { Copy-Item -Path $rbSrc -Destination $dest -Force }
                        } catch {}
                    }
                    foreach ($rel in $deletedRels) {
                        try {
                            $rbSrc = Join-Path $rollbackDir $rel
                            $dest = Join-Path $installDir $rel
                            $destDir = Split-Path -Parent $dest
                            if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
                            if (Test-Path $rbSrc) { Copy-Item -Path $rbSrc -Destination $dest -Force }
                        } catch {}
                    }
                }
                foreach ($rel in $createdRels) {
                    try {
                        $dest = Join-Path $installDir $rel
                        if (Test-Path $dest) { Remove-Item -Path $dest -Force -ErrorAction SilentlyContinue }
                    } catch {}
                }
                try {
                    Add-Type -AssemblyName System.Windows.Forms
                    [System.Windows.Forms.MessageBox]::Show("Karobar-App Native patch update failed and was rolled back:`n`n$($_.Exception.Message)`n`nLog: $logPath", 'Karobar-App Native Updater', 'OK', 'Error') | Out-Null
                } catch {}
            } finally {
                Remove-Item -Recurse -Force $stageDir -ErrorAction SilentlyContinue
                Remove-Item -Recurse -Force $rollbackDir -ErrorAction SilentlyContinue
                if (Test-Path $exePath) {
                    Write-Log "Relaunching $exePath"
                    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
                    if ($isAdmin) {
                        # Relaunch de-elevated through explorer.exe so WebView2 runs under normal user privileges
                        Start-Process -FilePath 'explorer.exe' -ArgumentList "`"$exePath`""
                    } else {
                        Start-Process -FilePath $exePath -WorkingDirectory $installDir
                    }
                }
                if ($PSCommandPath -and (Test-Path $PSCommandPath)) {
                    Remove-Item -Path $PSCommandPath -Force -ErrorAction SilentlyContinue
                }
            }
            """;

        File.WriteAllText(helperScriptPath, ps1, new UTF8Encoding(false));

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{helperScriptPath}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (needsElevation)
        {
            psi.Verb = "runas";
        }
        Process.Start(psi);
    }

    private static void LaunchWindowsInstallerAfterExit(string installerExePath, string installDir)
    {
        var pid = Environment.ProcessId;
        var exePath = Path.Combine(installDir, Program.ExecutableFileName);
        var otaFlagPath = Path.Combine(Path.GetTempPath(), "karobar-native-ota-upgrade.flag");
        var helperScriptPath = Path.Combine(Path.GetTempPath(), $"karobar-run-setup-{pid}.ps1");
        var ps1 = $$"""
            $targetPid = {{pid}}
            $setupExe = '{{installerExePath.Replace("'", "''")}}'
            $installDir = '{{installDir.Replace("'", "''")}}'
            $exePath = '{{exePath.Replace("'", "''")}}'
            $otaFlagPath = '{{otaFlagPath.Replace("'", "''")}}'

            # Wait for Karobar-Native.exe to exit and release Global\KarobarNativeErpV2Mutex before Inno Setup starts
            for ($i = 0; $i -lt 40; $i++) {
                if (-not (Get-Process -Id $targetPid -ErrorAction SilentlyContinue)) { break }
                if ($i -eq 24) {
                    Stop-Process -Id $targetPid -Force -ErrorAction SilentlyContinue
                }
                Start-Sleep -Milliseconds 250
            }
            Start-Sleep -Milliseconds 400

            $setupArgs = "/SILENT /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /UPGRADE=1 `"/OTAFLAG=$otaFlagPath`" `"/DIR=$installDir`""
            try {
                $setupProc = Start-Process -FilePath $setupExe -ArgumentList $setupArgs -Wait -PassThru -ErrorAction Stop
                if ($setupProc -and $setupProc.ExitCode -ne 0) {
                    if (Test-Path $exePath) { Start-Process -FilePath $exePath -WorkingDirectory $installDir }
                }
            } catch {
                # If the user cancels UAC elevation or setup fails to start, relaunch the app cleanly
                if (Test-Path $exePath) { Start-Process -FilePath $exePath -WorkingDirectory $installDir }
            } finally {
                if (Test-Path $otaFlagPath) { Remove-Item -Path $otaFlagPath -Force -ErrorAction SilentlyContinue }
                if ($PSCommandPath -and (Test-Path $PSCommandPath)) {
                    Remove-Item -Path $PSCommandPath -Force -ErrorAction SilentlyContinue
                }
            }
            """;

        File.WriteAllText(helperScriptPath, ps1, new UTF8Encoding(false));
        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{helperScriptPath}\"",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    public Task<object> downloadAndInstallUpdate(JsonElement payload) => DownloadAndInstallUpdateAsync(payload);

    private void PublishUpdateProgress(object progressPayload)
    {
        _windowManager.SendUpdateProgress(progressPayload);
        try
        {
            var progressPath = Path.Combine(_networkConfig.AppRoot, "storage", "app", "update-progress.json");
            Directory.CreateDirectory(Path.GetDirectoryName(progressPath)!);
            var json = JsonSerializer.Serialize(progressPayload);
            lock (_progressFileLock)
            {
                var tmpPath = progressPath + ".tmp";
                File.WriteAllText(tmpPath, json, new UTF8Encoding(false));
                File.Move(tmpPath, progressPath, overwrite: true);
            }
        }
        catch { }
    }

    private async Task DownloadFileWithProgressAsync(
        string url,
        string destPath,
        bool isPatch,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Karobar-App-Desktop-Updater/2.0");

        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? 0L;
        await using var netStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);

        var buffer = new byte[81920];
        long downloadedBytes = 0;
        int read;
        int lastPublishedPct = -1;
        while ((read = await netStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloadedBytes += read;

            var percent = totalBytes > 0
                ? (int)Math.Min(99, Math.Round((double)downloadedBytes / totalBytes * 100))
                : 50;
            var downloadedMb = (downloadedBytes / (1024.0 * 1024.0)).ToString("F1");
            var totalMb = totalBytes > 0 ? (totalBytes / (1024.0 * 1024.0)).ToString("F1") : null;

            var prog = new
            {
                status = "downloading",
                percent,
                is_patch = isPatch,
                downloadedMB = downloadedMb,
                totalMB = totalMb,
                message = totalMb != null
                    ? (isPatch
                        ? $"Downloading fast patch ({downloadedMb} MB / {totalMb} MB)…"
                        : $"Downloading update ({downloadedMb} MB / {totalMb} MB)…")
                    : (isPatch
                        ? $"Downloading fast patch ({downloadedMb} MB)…"
                        : $"Downloading update ({downloadedMb} MB)…")
            };
            if (percent != lastPublishedPct)
            {
                lastPublishedPct = percent;
                PublishUpdateProgress(prog);
            }
            else
            {
                _windowManager.SendUpdateProgress(prog);
            }
        }

        await fileStream.FlushAsync(cancellationToken);
        if (totalBytes > 0 && downloadedBytes != totalBytes)
        {
            throw new IOException($"Incomplete update download: received {downloadedBytes} of {totalBytes} bytes.");
        }
    }

    // =========================================================================
    // 16. openExternalUrl (karobar:open-external-url)
    // =========================================================================

    public Task<object> OpenExternalUrlAsync(JsonElement payload)
    {
        string? url = null;
        if (payload.ValueKind == JsonValueKind.String)
        {
            url = payload.GetString();
        }
        else if (payload.ValueKind == JsonValueKind.Object &&
                 payload.TryGetProperty("url", out var uProp) &&
                 uProp.ValueKind == JsonValueKind.String)
        {
            url = uProp.GetString();
        }

        if (!string.IsNullOrWhiteSpace(url) &&
            (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
             url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
             url.StartsWith("whatsapp:", StringComparison.OrdinalIgnoreCase) ||
             url.StartsWith("tel:", StringComparison.OrdinalIgnoreCase)))
        {
            WebView2WindowManager.TryOpenExternalUrl(url);
            return Task.FromResult<object>(new { ok = true });
        }

        return Task.FromResult<object>(new { ok = false });
    }

    public Task<object> openExternalUrl(JsonElement payload) => OpenExternalUrlAsync(payload);

    // =========================================================================
    // 17, 18, 19. Multi-Window Management (openWindow, getOpenWindows, focusWindow)
    // =========================================================================

    public Task<object> OpenWindowAsync(JsonElement payload)
    {
        var routePath = "/";
        var title = "Karobar-App";
        int? width = null;
        int? height = null;

        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("path", out var pProp) && pProp.ValueKind == JsonValueKind.String)
                routePath = pProp.GetString() ?? "/";
            if (payload.TryGetProperty("title", out var tProp) && tProp.ValueKind == JsonValueKind.String)
                title = tProp.GetString() ?? "Karobar-App";
            if (payload.TryGetProperty("width", out var wProp) && wProp.TryGetInt32(out var wVal))
                width = wVal;
            if (payload.TryGetProperty("height", out var hProp) && hProp.TryGetInt32(out var hVal))
                height = hVal;
        }
        else if (payload.ValueKind == JsonValueKind.String)
        {
            routePath = payload.GetString() ?? "/";
        }

        var win = _windowManager.CreateAppWindow(routePath, new AppWindowOptions
        {
            Title = title,
            Width = width,
            Height = height
        });

        return Task.FromResult<object>(new { ok = true, windowId = win.Id });
    }

    public Task<object> openWindow(JsonElement payload) => OpenWindowAsync(payload);

    public Task<IReadOnlyList<OpenWindowInfo>> GetOpenWindowsAsync()
    {
        return Task.FromResult(_windowManager.GetOpenWindows());
    }

    public Task<IReadOnlyList<OpenWindowInfo>> getOpenWindows() => GetOpenWindowsAsync();

    public Task<bool> FocusWindowAsync(JsonElement payload)
    {
        var winId = 0;
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("winId", out var idProp) &&
            idProp.TryGetInt32(out var parsedId))
        {
            winId = parsedId;
        }
        else if (payload.ValueKind == JsonValueKind.Number && payload.TryGetInt32(out var directId))
        {
            winId = directId;
        }

        return Task.FromResult(_windowManager.FocusWindow(winId));
    }

    public Task<bool> focusWindow(JsonElement payload) => FocusWindowAsync(payload);

    // =========================================================================
    // 5 Event Emitters (onNavigate, onOpenSubWindow, onWindowCommand,
    // onUpdateProgress, onTriggerUpdateCheck)
    // =========================================================================

    public void EmitNavigate(string path, string? title = null) => _windowManager.SendNavigate(path, title);
    public void onNavigate(string path, string? title = null) => EmitNavigate(path, title);

    public void EmitOpenSubWindow(string path, string? title = null) => _windowManager.SendOpenSubWindow(path, title);
    public void onOpenSubWindow(string path, string? title = null) => EmitOpenSubWindow(path, title);

    public void EmitWindowCommand(string command, Dictionary<string, object?>? payload = null) =>
        _windowManager.SendWindowCommand(command, payload);
    public void onWindowCommand(string command, Dictionary<string, object?>? payload = null) =>
        EmitWindowCommand(command, payload);

    public void EmitUpdateProgress(object payload) => _windowManager.SendUpdateProgress(payload);
    public void onUpdateProgress(object payload) => EmitUpdateProgress(payload);

    public void EmitTriggerUpdateCheck(bool manual = true) => _windowManager.SendTriggerUpdateCheck(manual);
    public void onTriggerUpdateCheck(bool manual = true) => EmitTriggerUpdateCheck(manual);
}

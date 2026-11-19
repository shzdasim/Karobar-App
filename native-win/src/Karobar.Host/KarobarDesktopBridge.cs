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
        @"(pos|thermal|receipt|58mm|80mm|78mm|72mm|xprinter|xp-|black\s*copper|bc-|epson.*tm|tm-t|tm-u|bixolon|star\s*tsp|citizen|rongta|rp58|rp80|birch|sewoo|gainscha|hprt|deli|zywell|munbyn|netum|goojprt|milestone|gp-)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly NetworkConfigManager _networkConfig;
    private readonly WebView2WindowManager _windowManager;
    private readonly Func<IServiceProvider?>? _serviceProviderAccessor;

    private List<PrinterInfoDto>? _cachedPrinters;
    private long _cachedPrintersAt;

    public KarobarDesktopBridge(
        NetworkConfigManager networkConfig,
        WebView2WindowManager windowManager,
        Func<IServiceProvider?>? serviceProviderAccessor = null)
    {
        _networkConfig = networkConfig;
        _windowManager = windowManager;
        _serviceProviderAccessor = serviceProviderAccessor;
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
            _ => new { ok = false, error = $"Unknown IPC channel: {normalizedChannel}" }
        };
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
        var prevMode = _networkConfig.LoadNetworkConfig().Mode;
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
            if (updated.Mode == "standalone" && prevMode == "terminal" && OnStartBackendServices != null)
            {
                await OnStartBackendServices();
            }
            else if (updated.Mode == "terminal" && prevMode == "standalone" && OnStopBackendServices != null)
            {
                await OnStopBackendServices();
            }

            var targetUrl = _networkConfig.GetAppBaseUrl();
            _windowManager.SendNavigate($"{targetUrl}/", "Karobar-App");
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

        if (payload.ValueKind == JsonValueKind.Object)
        {
            if (payload.TryGetProperty("serverUrl", out var sProp) && sProp.ValueKind == JsonValueKind.String)
                serverUrl = sProp.GetString() ?? "";
            if (payload.TryGetProperty("terminalName", out var tProp) && tProp.ValueKind == JsonValueKind.String)
                terminalName = tProp.GetString() ?? "";
        }
        else if (payload.ValueKind == JsonValueKind.String)
        {
            serverUrl = payload.GetString() ?? "";
        }

        var version = ReadLocalVersionMeta().Version;
        return await _networkConfig.TestServerConnectionAsync(serverUrl, terminalName, version, cancellationToken);
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

        var preferredName = ReadPreferredPrinterConfig();
        var printers = new List<PrinterInfoDto>();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false); Get-CimInstance -ClassName Win32_Printer -Property Name,Default,WorkOffline,PrinterStatus,PortName,DriverName | Select-Object Name,Default,WorkOffline,PrinterStatus,PortName,DriverName | ConvertTo-Json -Compress\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        StandardOutputEncoding = Encoding.UTF8
                    }
                };

                proc.Start();
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(4500));
                var stdout = await proc.StandardOutput.ReadToEndAsync(cts.Token);
                await proc.WaitForExitAsync(cts.Token);

                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    using var doc = JsonDocument.Parse(stdout.Trim());
                    var items = doc.RootElement.ValueKind == JsonValueKind.Array
                        ? doc.RootElement.EnumerateArray().ToList()
                        : [doc.RootElement];

                    foreach (var row in items)
                    {
                        var pName = row.TryGetProperty("Name", out var nProp) && nProp.ValueKind == JsonValueKind.String
                            ? nProp.GetString() ?? ""
                            : "";
                        if (string.IsNullOrWhiteSpace(pName)) continue;

                        var isDef = row.TryGetProperty("Default", out var dProp) && dProp.ValueKind == JsonValueKind.True;
                        var workOff = row.TryGetProperty("WorkOffline", out var wProp) && wProp.ValueKind == JsonValueKind.True;
                        var pStatus = row.TryGetProperty("PrinterStatus", out var sProp) && sProp.TryGetInt32(out var st) ? st : 0;
                        var port = row.TryGetProperty("PortName", out var ptProp) && ptProp.ValueKind == JsonValueKind.String
                            ? ptProp.GetString()
                            : "";
                        var driver = row.TryGetProperty("DriverName", out var drProp) && drProp.ValueKind == JsonValueKind.String
                            ? drProp.GetString()
                            : "";

                        printers.Add(ClassifyPrinterInfo(
                            pName,
                            pName,
                            driver,
                            port,
                            statusMask: 0,
                            wmiPrinterStatus: pStatus,
                            isDefault: isDef,
                            wmiWorkOffline: workOff,
                            preferredName: preferredName));
                    }
                }
            }
            catch
            {
                // Ignore WMI query errors
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

            if (matched is { IsVirtual: false })
            {
                if (matched.WorkOffline)
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
            Path.Combine(_networkConfig.AppRoot, "desktop", "SumatraPDF.exe"),
            Path.Combine(_networkConfig.AppRoot, "desktop", "SumatraPDF-3.4.6-32.exe"),
            Path.Combine(AppContext.BaseDirectory, "desktop", "SumatraPDF.exe"),
            Path.Combine(AppContext.BaseDirectory, "SumatraPDF.exe"),
            Path.Combine(_networkConfig.AppRoot, "desktop", "node_modules", "pdf-to-printer", "dist", "SumatraPDF-3.4.6-32.exe")
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

    public async Task<object> SilentPrintAsync(JsonElement options, CancellationToken cancellationToken = default)
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

        var strategy = await ResolvePrinterStrategyAsync(requestedDeviceName, isThermal);
        if (strategy.Mode == "no-printer" || string.IsNullOrWhiteSpace(strategy.DeviceName))
        {
            return new
            {
                ok = false,
                noPhysicalPrinter = true,
                error = strategy.Error ??
                        "No physical hardware printer detected on Windows. Please connect a printer or select one in Settings → Print & Receipt Layouts."
            };
        }

        var targetPrinter = strategy.DeviceName;
        var sumatraExe = ResolveSumatraPdfBinary();

        if (OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(sumatraExe) && !string.IsNullOrWhiteSpace(pdfPath) && File.Exists(pdfPath))
        {
            var sumatraRes = await SpoolPdfViaSumatraAsync(sumatraExe, pdfPath, targetPrinter, isThermal, scaleMode, cancellationToken);
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

        return new
        {
            ok = true,
            printer = targetPrinter,
            engine = "win32-native-spooler"
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

        if (!Regex.IsMatch(resolvedUrl, @"[?&]silent_print=", RegexOptions.IgnoreCase))
        {
            resolvedUrl += (resolvedUrl.Contains('?') ? "&" : "?") + "silent_print=1";
        }

        return await _windowManager.RunOffscreenPrintJobAsync(async printWin =>
        {
            printWin.LoadUrl(resolvedUrl);
            return await SilentPrintAsync(payload, cancellationToken);
        });
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

        var tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"karobar-print-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Convert.ToHexString(RandomNumberGenerator.GetBytes(3)).ToLowerInvariant()}.html");

        try
        {
            await File.WriteAllTextAsync(tempFilePath, sanitizedHtml, Encoding.UTF8, cancellationToken);
            return await _windowManager.RunOffscreenPrintJobAsync(async printWin =>
            {
                printWin.LoadUrl(new Uri(tempFilePath).AbsoluteUri);
                return await SilentPrintAsync(payload, cancellationToken);
            });
        }
        finally
        {
            _ = Task.Delay(15000).ContinueWith(_ =>
            {
                try
                {
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
                catch
                {
                    // Ignore temp file cleanup errors
                }
            });
        }
    }

    public Task<object> printHtml(JsonElement payload) => PrintHtmlAsync(payload);

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

    public async Task<string?> SelectDirectoryAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            const string script = """
                Add-Type -AssemblyName System.Windows.Forms
                $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
                $dlg.Description = 'Select Backup Folder'
                $dlg.ShowNewFolderButton = $true
                if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
                    [Console]::Out.Write($dlg.SelectedPath)
                }
                """;

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -STA -WindowStyle Hidden -Command \"{script.Replace("\"", "\\\"")}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8
            });

            if (proc == null) return null;
            var output = (await proc.StandardOutput.ReadToEndAsync()).Trim();
            await proc.WaitForExitAsync();
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    public Task<string?> selectDirectory() => SelectDirectoryAsync();

    public async Task<string?> SelectSqliteFileAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            const string script = """
                Add-Type -AssemblyName System.Windows.Forms
                $dlg = New-Object System.Windows.Forms.OpenFileDialog
                $dlg.Title = 'Select SQLite Database to Import'
                $dlg.Filter = 'SQLite Databases & Backups (*.sqlite;*.sqlite3;*.db;*.db3;*.zip;*.gz;*.sql)|*.sqlite;*.sqlite3;*.db;*.db3;*.zip;*.gz;*.sql|All Files (*.*)|*.*'
                if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
                    [Console]::Out.Write($dlg.FileName)
                }
                """;

            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -STA -WindowStyle Hidden -Command \"{script.Replace("\"", "\\\"")}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8
            });

            if (proc == null) return null;
            var output = (await proc.StandardOutput.ReadToEndAsync()).Trim();
            await proc.WaitForExitAsync();
            return string.IsNullOrWhiteSpace(output) ? null : output;
        }
        catch
        {
            return null;
        }
    }

    public Task<string?> selectSqliteFile() => SelectSqliteFileAsync();

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

            // Fallback: create immediate SQLite backup snapshot in storage/app/backups
            var dbPath = Path.Combine(_networkConfig.AppRoot, "database", "database.sqlite");
            if (File.Exists(dbPath))
            {
                var backupDir = Path.Combine(_networkConfig.AppRoot, "storage", "app", "backups");
                Directory.CreateDirectory(backupDir);
                var destFile = Path.Combine(backupDir, $"backup-{DateTime.UtcNow:yyyy-MM-dd-HHmmss}.sqlite");
                File.Copy(dbPath, destFile, overwrite: true);
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
                        : defaults.Repository
                };
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

    public async Task<object> CheckForUpdatesAsync(JsonElement? payload = null, CancellationToken cancellationToken = default)
    {
        var local = ReadLocalVersionMeta();
        var repo = string.IsNullOrWhiteSpace(local.Repository) ? "shzdasim/Karobar-App" : local.Repository;

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 6,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Karobar-App-Desktop-Updater/2.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

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
                    var layoutCompatible = string.IsNullOrEmpty(remoteLayout) ||
                                           string.Equals(remoteLayout, local.RuntimeLayout, StringComparison.OrdinalIgnoreCase);

                    var canPatch = !string.IsNullOrWhiteSpace(patchUrl) &&
                                   layoutCompatible &&
                                   !CompareSemverNewer(minPatchVer, local.Version);

                    return new
                    {
                        ok = true,
                        update_available = isNewer,
                        is_patch = canPatch,
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
                        bundle_url = remote.TryGetProperty("bundle_url", out var bu) && bu.ValueKind == JsonValueKind.String
                            ? bu.GetString()
                            : $"https://github.com/{repo}/releases/download/native-latest/Karobar-Native-Setup-Bundle.zip",
                        patch_url = patchUrl,
                        patch_size = remote.TryGetProperty("patch_size", out var ps) && ps.TryGetInt64(out var pSize) ? (long?)pSize : null,
                        patch_sha256 = remote.TryGetProperty("patch_sha256", out var pSha) && pSha.ValueKind == JsonValueKind.String
                            ? pSha.GetString()
                            : null,
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

        // 2. Fallback: GitHub Releases REST API
        try
        {
            var apiText = await client.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest", cancellationToken);
            using var doc = JsonDocument.Parse(apiText);
            var apiData = doc.RootElement;

            if (apiData.TryGetProperty("tag_name", out var tagProp) && tagProp.ValueKind == JsonValueKind.String)
            {
                var rawTag = tagProp.GetString() ?? "";
                var remoteVer = Regex.Replace(rawTag, @"^(native-)?[vV]", "");
                var match = Regex.Match(remoteVer, @"^2\.0\.(\d+)$");
                var remoteBuild = match.Success && int.TryParse(match.Groups[1].Value, out var rb) ? rb : 0;
                var isNewer = remoteBuild > 0 && local.BuildNumber > 0 && remoteBuild != local.BuildNumber
                    ? remoteBuild > local.BuildNumber
                    : CompareSemverNewer(remoteVer, local.Version);

                var downloadUrl = $"https://github.com/{repo}/releases/latest/download/Karobar-Native-Setup.exe";
                var bundleUrl = $"https://github.com/{repo}/releases/latest/download/Karobar-Native-Setup-Bundle.zip";
                var patchUrl = $"https://github.com/{repo}/releases/latest/download/Karobar-Native-Patch.zip";
                long? patchSize = null;

                if (apiData.TryGetProperty("assets", out var assetsProp) && assetsProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var asset in assetsProp.EnumerateArray())
                    {
                        var name = asset.TryGetProperty("name", out var nProp) && nProp.ValueKind == JsonValueKind.String ? nProp.GetString() ?? "" : "";
                        var url = asset.TryGetProperty("browser_download_url", out var uProp) && uProp.ValueKind == JsonValueKind.String ? uProp.GetString() ?? "" : "";
                        if (!string.IsNullOrWhiteSpace(url) && name is "Karobar-Native-Setup.exe")
                            downloadUrl = url;
                        if (!string.IsNullOrWhiteSpace(url) && name is "Karobar-Native-Setup-Bundle.zip")
                            bundleUrl = url;
                        if (!string.IsNullOrWhiteSpace(url) && name is "Karobar-Native-Patch.zip")
                        {
                            patchUrl = url;
                            if (asset.TryGetProperty("size", out var sProp) && sProp.TryGetInt64(out var sz))
                                patchSize = sz;
                        }
                    }
                }

                return new
                {
                    ok = true,
                    update_available = isNewer,
                    is_patch = !string.IsNullOrWhiteSpace(patchUrl),
                    app_name = "Karobar-App Native",
                    current_version = local.Version,
                    current_build = local.BuildNumber,
                    latest_version = remoteVer,
                    latest_build = remoteBuild,
                    release_name = apiData.TryGetProperty("name", out var rn) && rn.ValueKind == JsonValueKind.String
                        ? rn.GetString()
                        : $"Karobar-App Native v{remoteVer}",
                    released_at = apiData.TryGetProperty("published_at", out var pa) && pa.ValueKind == JsonValueKind.String
                        ? pa.GetString()
                        : "",
                    notes = apiData.TryGetProperty("body", out var bd) && bd.ValueKind == JsonValueKind.String
                        ? bd.GetString()
                        : "New Karobar-App Native release available.",
                    download_url = downloadUrl,
                    bundle_url = bundleUrl,
                    patch_url = patchUrl,
                    patch_size = patchSize,
                    patch_sha256 = (string?)null,
                    min_patch_version = "2.0.0",
                    release_url = apiData.TryGetProperty("html_url", out var hu) && hu.ValueKind == JsonValueKind.String
                        ? hu.GetString()
                        : $"https://github.com/{repo}/releases/latest",
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

        var isPatch = (isPatchFlag || !string.IsNullOrWhiteSpace(candidatePatchUrl)) && !forceFull;
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

        try
        {
            var alreadyDownloaded = File.Exists(tempFilePath) && new FileInfo(tempFilePath).Length >= minValidSize;
            if (alreadyDownloaded && isPatch && !string.IsNullOrWhiteSpace(candidatePatchSha256) &&
                !VerifyFileSha256(tempFilePath, candidatePatchSha256))
            {
                try { File.Delete(tempFilePath); } catch { }
                alreadyDownloaded = false;
            }

            if (!alreadyDownloaded)
            {
                _windowManager.SendUpdateProgress(new
                {
                    status = "starting",
                    percent = 2,
                    is_patch = isPatch,
                    message = isPatch ? "Starting fast differential patch download…" : "Starting background update download…"
                });

                await DownloadFileWithProgressAsync(downloadUrl, tempFilePath, isPatch, cancellationToken);

                if (isPatch && !string.IsNullOrWhiteSpace(candidatePatchSha256) &&
                    !VerifyFileSha256(tempFilePath, candidatePatchSha256))
                {
                    try { File.Delete(tempFilePath); } catch { }
                    throw new InvalidOperationException("Downloaded patch failed SHA-256 integrity verification. Please retry.");
                }
            }

            if (mode == "download")
            {
                _windowManager.SendUpdateProgress(new
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

            _windowManager.SendUpdateProgress(new
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
                    LaunchWindowsDifferentialPatchSwap(tempFilePath, installDir);
                    _windowManager.OnQuitRequested?.Invoke();
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

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = tempFilePath,
                        Arguments = $"/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /FORCECLOSEAPPLICATIONS /UPGRADE=1 \"/DIR={installDir}\"",
                        UseShellExecute = true
                    });
                    _windowManager.OnQuitRequested?.Invoke();
                }
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
    }

    public static readonly string[] ProtectedPatchExclusionPrefixes =
    [
        "database/database.sqlite",
        "database\\database.sqlite",
        "storage/app/.machine-id",
        "storage\\app\\.machine-id",
        "storage/app/.license-clock",
        "storage\\app\\.license-clock",
        "storage/app/public/",
        "storage\\app\\public\\",
        "storage/app/whatsapp-auth/",
        "storage\\app\\whatsapp-auth\\",
        "storage/app/backups/",
        "storage\\app\\backups\\",
        "storage/backups/",
        "storage\\backups\\",
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

    private static void LaunchWindowsDifferentialPatchSwap(string patchZipPath, string installDir)
    {
        var pid = Environment.ProcessId;
        var helperScriptPath = Path.Combine(Path.GetTempPath(), $"karobar-apply-patch-{pid}.ps1");
        var exePath = Path.Combine(installDir, Program.ExecutableFileName);

        var ps1 = $$"""
            $ErrorActionPreference = 'Stop'
            $targetPid = {{pid}}
            $zipPath = '{{patchZipPath.Replace("'", "''")}}'
            $installDir = '{{installDir.Replace("'", "''")}}'
            $exePath = '{{exePath.Replace("'", "''")}}'
            $stageDir = Join-Path $env:TEMP "karobar-patch-stage-$targetPid"

            for ($i = 0; $i -lt 60; $i++) {
                if (-not (Get-Process -Id $targetPid -ErrorAction SilentlyContinue)) { break }
                Start-Sleep -Milliseconds 250
            }

            if (Test-Path $stageDir) { Remove-Item -Recurse -Force $stageDir -ErrorAction SilentlyContinue }
            Expand-Archive -Path $zipPath -DestinationPath $stageDir -Force

            $protected = @(
                'database\database.sqlite',
                'storage\app\.machine-id',
                'storage\app\.license-clock',
                'storage\app\public\',
                'storage\app\whatsapp-auth\',
                'storage\app\backups\',
                'storage\backups\',
                '.env'
            )

            Get-ChildItem -Path $stageDir -Recurse -File | ForEach-Object {
                $rel = $_.FullName.Substring($stageDir.Length).TrimStart('\', '/')
                $skip = $false
                foreach ($p in $protected) {
                    if ($rel.StartsWith($p, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $skip = $true
                        break
                    }
                }
                if (-not $skip) {
                    $dest = Join-Path $installDir $rel
                    $destDir = Split-Path -Parent $dest
                    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Force -Path $destDir | Out-Null }
                    Copy-Item -Path $_.FullName -Destination $dest -Force
                }
            }

            if (Test-Path $exePath) {
                $upgProc = Start-Process -FilePath $exePath -ArgumentList '--cli', 'install', '--upgrade-only' -WorkingDirectory $installDir -WindowStyle Hidden -Wait -PassThru
                Start-Process -FilePath $exePath -WorkingDirectory $installDir
            }

            Remove-Item -Recurse -Force $stageDir -ErrorAction SilentlyContinue
            Remove-Item -Force $zipPath -ErrorAction SilentlyContinue
            """;

        File.WriteAllText(helperScriptPath, ps1, new UTF8Encoding(false));

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{helperScriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    public Task<object> downloadAndInstallUpdate(JsonElement payload) => DownloadAndInstallUpdateAsync(payload);

    private async Task DownloadFileWithProgressAsync(
        string url,
        string destPath,
        bool isPatch,
        CancellationToken cancellationToken)
    {
        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
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
        while ((read = await netStream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            downloadedBytes += read;

            var percent = totalBytes > 0
                ? (int)Math.Min(99, Math.Round((double)downloadedBytes / totalBytes * 100))
                : 50;
            var downloadedMb = (downloadedBytes / (1024.0 * 1024.0)).ToString("F1");
            var totalMb = totalBytes > 0 ? (totalBytes / (1024.0 * 1024.0)).ToString("F1") : null;

            _windowManager.SendUpdateProgress(new
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
            });
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
             url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
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

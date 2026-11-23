using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Karobar.Host;

public sealed class NetworkConfig
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "standalone";

    [JsonPropertyName("serverUrl")]
    public string ServerUrl { get; set; } = "";

    [JsonPropertyName("terminalId")]
    public string TerminalId { get; set; } = "";

    [JsonPropertyName("terminalName")]
    public string TerminalName { get; set; } = "";

    [JsonPropertyName("pairingToken")]
    public string PairingToken { get; set; } = "";

    [JsonPropertyName("serverCertThumbprint")]
    public string ServerCertThumbprint { get; set; } = "";
}

public sealed class NetworkConfigUpdate
{
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    [JsonPropertyName("serverUrl")]
    public string? ServerUrl { get; set; }

    [JsonPropertyName("terminalId")]
    public string? TerminalId { get; set; }

    [JsonPropertyName("terminalName")]
    public string? TerminalName { get; set; }

    [JsonPropertyName("pairingToken")]
    public string? PairingToken { get; set; }

    [JsonPropertyName("serverCertThumbprint")]
    public string? ServerCertThumbprint { get; set; }

    [JsonPropertyName("relaunch")]
    public bool Relaunch { get; set; }
}

public sealed class ServerConnectionTestResult
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }

    [JsonPropertyName("serverUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ServerUrl { get; set; }

    [JsonPropertyName("storeName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StoreName { get; set; }

    [JsonPropertyName("dbDriver")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DbDriver { get; set; }

    [JsonPropertyName("osFamily")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OsFamily { get; set; }

    [JsonPropertyName("terminal")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Terminal { get; set; }

    [JsonPropertyName("latencyMs")]
    public int LatencyMs { get; set; }
}

/// <summary>
/// Multi-PC Network / Terminal Mode configuration manager matching desktop/main.cjs:
/// getNetworkConfigPaths(), normalizeServerUrl(), loadNetworkConfig(), saveNetworkConfig(),
/// isTerminalMode(), getAppBaseUrl(), and testRemoteServerConnection().
/// </summary>
public sealed class NetworkConfigManager
{
    public const int DefaultPort = 18960;
    public const string AppDataFolderName = "Karobar-App-Native";

    private readonly object _syncLock = new();
    private readonly string _appRoot;
    private readonly string? _userDataDirOverride;
    private NetworkConfig? _networkConfigCache;
    private int _activePort = DefaultPort;

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public NetworkConfigManager(string? appRoot = null, int activePort = DefaultPort, string? userDataDirOverride = null)
    {
        _appRoot = string.IsNullOrWhiteSpace(appRoot) ? ResolveAppRoot() : Path.GetFullPath(appRoot);
        _activePort = activePort > 0 ? activePort : DefaultPort;
        _userDataDirOverride = string.IsNullOrWhiteSpace(userDataDirOverride) ? null : Path.GetFullPath(userDataDirOverride);
    }

    public string AppRoot => _appRoot;

    public int ActivePort
    {
        get => _activePort;
        set
        {
            if (value > 0)
            {
                _activePort = value;
            }
        }
    }

    /// <summary>
    /// Resolve the root Karobar application directory whether running unpacked in an
    /// Inno Setup installation folder or directly from the repository during development.
    /// </summary>
    public static string ResolveAppRoot()
    {
        var candidates = new List<string>();

        var envRoot = Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            candidates.Add(envRoot);
        }

        var procDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrWhiteSpace(procDir))
        {
            candidates.Add(procDir);
            candidates.Add(Path.GetFullPath(Path.Combine(procDir, "app")));
        }

        var exeDir = AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(exeDir))
        {
            candidates.Add(Path.GetFullPath(Path.Combine(exeDir, "app")));
            candidates.Add(Path.GetFullPath(Path.Combine(exeDir, "..")));

            var walk = new DirectoryInfo(exeDir);
            while (walk != null)
            {
                candidates.Add(walk.FullName);
                walk = walk.Parent;
            }
        }

        var cwd = Directory.GetCurrentDirectory();
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            var walk = new DirectoryInfo(cwd);
            while (walk != null)
            {
                candidates.Add(walk.FullName);
                walk = walk.Parent;
            }
        }

        foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(dir) &&
                    (File.Exists(Path.Combine(dir, "public", "index.html")) ||
                     File.Exists(Path.Combine(dir, "public", "build", "manifest.json")) ||
                     (Directory.Exists(Path.Combine(dir, "database")) && Directory.Exists(Path.Combine(dir, "public")))))
                {
                    return dir;
                }
            }
            catch
            {
                // Ignore inaccessible candidate paths
            }
        }

        return Directory.GetCurrentDirectory();
    }

    /// <summary>
    /// Candidate configuration file paths:
    /// 1. %APPDATA%/Karobar-App-Native/network-mode.json
    /// 2. %APPDATA%/Karobar-App-Native/network-config.json (written by Inno Setup ssPostInstall)
    /// 3. &lt;APP_ROOT&gt;/storage/app/network-mode.json
    /// </summary>
    public IReadOnlyList<string> GetNetworkConfigPaths()
    {
        var paths = new List<string>();
        try
        {
            var appData = _userDataDirOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (!string.IsNullOrWhiteSpace(appData))
            {
                paths.Add(Path.Combine(appData, AppDataFolderName, "network-mode.json"));
                paths.Add(Path.Combine(appData, AppDataFolderName, "network-config.json"));
            }
        }
        catch
        {
            // Ignore if SpecialFolder unavailable
        }

        paths.Add(Path.Combine(_appRoot, "storage", "app", "network-mode.json"));
        return paths;
    }

    // JS camelCase alias for parity checks
    public IReadOnlyList<string> getNetworkConfigPaths() => GetNetworkConfigPaths();

    public static string NormalizeServerUrl(string? rawUrl)
    {
        var u = (rawUrl ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(u))
        {
            return string.Empty;
        }

        if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            u = "http://" + u;
        }

        return u.TrimEnd('/');
    }

    public static string normalizeServerUrl(string? rawUrl) => NormalizeServerUrl(rawUrl);

    private static string GetSanitizedHostname()
    {
        string rawHost;
        try
        {
            rawHost = Environment.MachineName;
        }
        catch
        {
            rawHost = "PC";
        }

        var cleaned = Regex.Replace(rawHost ?? "PC", @"[^a-zA-Z0-9_-]", "");
        if (cleaned.Length > 20)
        {
            cleaned = cleaned[..20];
        }

        return string.IsNullOrWhiteSpace(cleaned) ? "PC" : cleaned;
    }

    private static string GenerateTerminalUid(string hostname)
    {
        var hex = Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToUpperInvariant();
        return $"TERM-{hostname.ToUpperInvariant()}-{hex}";
    }

    public NetworkConfig LoadNetworkConfig()
    {
        lock (_syncLock)
        {
            if (_networkConfigCache != null)
            {
                return CloneConfig(_networkConfigCache);
            }

            var defaultHostname = GetSanitizedHostname();
            var defaultUid = GenerateTerminalUid(defaultHostname);
            NetworkConfig? loaded = null;
            var hadTerminalIdOnDisk = false;

            // When multiple candidate files exist (e.g. standard user %APPDATA% vs elevated installer {app}\storage\app),
            // prefer the most recently modified valid file so installer mode switches always take effect immediately.
            var existingPaths = GetNetworkConfigPaths()
                .Select((path, index) => (path, index))
                .Where(item => File.Exists(item.path))
                .OrderByDescending(item =>
                {
                    try { return File.GetLastWriteTimeUtc(item.path); }
                    catch { return DateTime.MinValue; }
                })
                .ThenBy(item => item.index)
                .Select(item => item.path);

            foreach (var cfgPath in existingPaths)
            {
                try
                {
                    var json = File.ReadAllText(cfgPath, Encoding.UTF8);
                    var parsed = JsonSerializer.Deserialize<NetworkConfig>(json, JsonReadOptions);
                    if (parsed != null)
                    {
                        loaded = parsed;
                        hadTerminalIdOnDisk = !string.IsNullOrWhiteSpace(parsed.TerminalId);
                        break;
                    }
                }
                catch
                {
                    // Ignore malformed config file
                }
            }

            _networkConfigCache = new NetworkConfig
            {
                Mode = string.Equals(loaded?.Mode, "terminal", StringComparison.OrdinalIgnoreCase)
                    ? "terminal"
                    : "standalone",
                ServerUrl = NormalizeServerUrl(loaded?.ServerUrl),
                TerminalId = !string.IsNullOrWhiteSpace(loaded?.TerminalId)
                    ? loaded!.TerminalId.Trim()
                    : defaultUid,
                TerminalName = !string.IsNullOrWhiteSpace(loaded?.TerminalName)
                    ? loaded!.TerminalName.Trim()
                    : $"Counter ({defaultHostname})",
                PairingToken = (loaded?.PairingToken ?? string.Empty).Trim(),
                ServerCertThumbprint = (loaded?.ServerCertThumbprint ?? string.Empty).Trim()
            };

            if (loaded == null || !hadTerminalIdOnDisk)
            {
                PersistConfigToDisk(_networkConfigCache);
            }

            return CloneConfig(_networkConfigCache);
        }
    }

    public NetworkConfig loadNetworkConfig() => LoadNetworkConfig();

    public NetworkConfig SaveNetworkConfig(NetworkConfigUpdate? partial = null)
    {
        lock (_syncLock)
        {
            var current = _networkConfigCache ?? LoadNetworkConfig();
            partial ??= new NetworkConfigUpdate();

            var nextMode = current.Mode;
            if (!string.IsNullOrWhiteSpace(partial.Mode))
            {
                if (string.Equals(partial.Mode, "terminal", StringComparison.OrdinalIgnoreCase))
                {
                    nextMode = "terminal";
                }
                else if (string.Equals(partial.Mode, "standalone", StringComparison.OrdinalIgnoreCase))
                {
                    nextMode = "standalone";
                }
            }

            var next = new NetworkConfig
            {
                Mode = nextMode,
                ServerUrl = partial.ServerUrl != null
                    ? NormalizeServerUrl(partial.ServerUrl)
                    : current.ServerUrl,
                TerminalId = !string.IsNullOrWhiteSpace(partial.TerminalId)
                    ? partial.TerminalId.Trim()
                    : current.TerminalId,
                TerminalName = !string.IsNullOrWhiteSpace(partial.TerminalName)
                    ? partial.TerminalName.Trim()
                    : current.TerminalName,
                PairingToken = partial.PairingToken != null
                    ? partial.PairingToken.Trim()
                    : current.PairingToken,
                ServerCertThumbprint = partial.ServerCertThumbprint != null
                    ? partial.ServerCertThumbprint.Trim()
                    : current.ServerCertThumbprint
            };

            _networkConfigCache = next;
            PersistConfigToDisk(next);
            return CloneConfig(next);
        }
    }

    public NetworkConfig saveNetworkConfig(NetworkConfigUpdate? partial = null) => SaveNetworkConfig(partial);

    public bool IsTerminalMode()
    {
        var cfg = LoadNetworkConfig();
        return string.Equals(cfg.Mode, "terminal", StringComparison.OrdinalIgnoreCase) &&
               !string.IsNullOrWhiteSpace(cfg.ServerUrl);
    }

    public bool isTerminalMode() => IsTerminalMode();

    public string GetAppBaseUrl(int? portOverride = null)
    {
        var cfg = LoadNetworkConfig();
        if (string.Equals(cfg.Mode, "terminal", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(cfg.ServerUrl))
        {
            return cfg.ServerUrl;
        }

        var port = portOverride ?? _activePort;
        return $"http://127.0.0.1:{port}";
    }

    public string getAppBaseUrl() => GetAppBaseUrl();

    public static bool IsSameOrigin(string? requestUrl, string? expectedOriginOrUrl)
    {
        if (string.IsNullOrWhiteSpace(requestUrl) || string.IsNullOrWhiteSpace(expectedOriginOrUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(requestUrl.Trim(), UriKind.Absolute, out var reqUri) ||
            !Uri.TryCreate(expectedOriginOrUrl.Trim(), UriKind.Absolute, out var expUri))
        {
            return false;
        }

        if ((!string.Equals(reqUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(reqUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) ||
            (!string.Equals(expUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
             !string.Equals(expUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(reqUri.UserInfo) || !string.IsNullOrEmpty(expUri.UserInfo))
        {
            return false;
        }

        return string.Equals(reqUri.Scheme, expUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(reqUri.Host, expUri.Host, StringComparison.OrdinalIgnoreCase) &&
               reqUri.Port == expUri.Port;
    }

    public static bool IsPrivateOrLoopbackHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        var clean = host.Trim().Trim('[', ']');
        if (string.Equals(clean, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!IPAddress.TryParse(clean, out var ip))
        {
            return false;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
        {
            return true;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            if (b[0] == 169 && b[1] == 254 && b[2] == 169 && b[3] == 254)
            {
                return false;
            }
            if (b[0] == 10) return true;
            if (b[0] == 172 && b[1] >= 16 && b[1] <= 31) return true;
            if (b[0] == 192 && b[1] == 168) return true;
            if (b[0] == 169 && b[1] == 254) return true;
            return false;
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            var b = ip.GetAddressBytes();
            if ((b[0] & 0xFE) == 0xFC) return true; // RFC 4193 Unique Local Address fc00::/7
        }

        return false;
    }

    public static bool ValidateServerCertificate(
        HttpRequestMessage? req,
        X509Certificate2? cert,
        SslPolicyErrors errors,
        string? pinnedThumbprint = null)
    {
        var normalizedPin = (pinnedThumbprint ?? string.Empty).Replace(":", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal).Trim();
        if (!string.IsNullOrEmpty(normalizedPin))
        {
            if (cert == null) return false;
            var actualHash = cert.GetCertHashString(HashAlgorithmName.SHA256);
            if (actualHash.Length != normalizedPin.Length) return false;
            var aBytes = Encoding.ASCII.GetBytes(actualHash.ToUpperInvariant());
            var bBytes = Encoding.ASCII.GetBytes(normalizedPin.ToUpperInvariant());
            return CryptographicOperations.FixedTimeEquals(aBytes, bBytes);
        }

        return errors == SslPolicyErrors.None || IsPrivateOrLoopbackHost(req?.RequestUri?.Host);
    }

    private string? _cachedAppVersion;

    public string ResolveInstalledAppVersion()
    {
        if (!string.IsNullOrWhiteSpace(_cachedAppVersion)) return _cachedAppVersion;
        try
        {
            var versionPath = Path.Combine(_appRoot, "public", "version.json");
            if (File.Exists(versionPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(versionPath, Encoding.UTF8));
                if (doc.RootElement.TryGetProperty("version", out var vProp) && vProp.ValueKind == JsonValueKind.String)
                {
                    var v = (vProp.GetString() ?? "").Trim();
                    if (!string.IsNullOrWhiteSpace(v))
                    {
                        _cachedAppVersion = v;
                        return v;
                    }
                }
            }
        }
        catch { }
        return _cachedAppVersion = "2.0.1";
    }

    public Dictionary<string, string> GetTerminalIdentityHeaders(string? appVersion = null)
    {
        var resolvedVersion = string.IsNullOrWhiteSpace(appVersion) ? ResolveInstalledAppVersion() : appVersion.Trim();
        var cfg = LoadNetworkConfig();
        var hostname = GetSanitizedHostname();
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Terminal-Id"] = cfg.TerminalId,
            ["X-Terminal-Name"] = cfg.TerminalName,
            ["X-Terminal-Mode"] = cfg.Mode,
            ["X-Terminal-Hostname"] = hostname,
            ["X-Terminal-OS"] = $"win32-{Environment.OSVersion.Version}",
            ["X-Terminal-Version"] = resolvedVersion
        };

        if (!string.IsNullOrWhiteSpace(cfg.PairingToken))
        {
            headers["X-Network-Token"] = cfg.PairingToken;
            headers["X-Terminal-Token"] = cfg.PairingToken;
        }

        return headers;
    }

    public async Task<ServerConnectionTestResult> TestServerConnectionAsync(
        string? rawServerUrl,
        string? customTerminalName = null,
        string? appVersion = null,
        CancellationToken cancellationToken = default,
        string? pairingTokenOverride = null)
    {
        var resolvedVersion = string.IsNullOrWhiteSpace(appVersion) ? ResolveInstalledAppVersion() : appVersion.Trim();
        var normalized = NormalizeServerUrl(rawServerUrl);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new ServerConnectionTestResult
            {
                Ok = false,
                Error = "Please enter a valid Server URL (e.g. http://192.168.1.10:18960)."
            };
        }

        if (!Uri.TryCreate($"{normalized}/api/terminals/ping", UriKind.Absolute, out var pingUri))
        {
            return new ServerConnectionTestResult
            {
                Ok = false,
                Error = "Invalid URL format."
            };
        }

        var cfg = LoadNetworkConfig();
        var effectiveTerminalName = !string.IsNullOrWhiteSpace(customTerminalName)
            ? customTerminalName.Trim()
            : cfg.TerminalName;
        var effectivePairingToken = pairingTokenOverride != null
            ? pairingTokenOverride.Trim()
            : cfg.PairingToken;
        var hostname = GetSanitizedHostname();
        var osPlatform = $"win32-{Environment.OSVersion.Version}";

        var payloadObj = new
        {
            terminal_uid = cfg.TerminalId,
            terminal_name = effectiveTerminalName,
            hostname,
            os_platform = osPlatform,
            app_version = resolvedVersion,
            mode = "terminal"
        };

        var bodyJson = JsonSerializer.Serialize(payloadObj);
        var startTs = Environment.TickCount64;

        using var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            ServerCertificateCustomValidationCallback = (req, cert, _, errors) =>
                ValidateServerCertificate(req, cert, errors, cfg.ServerCertThumbprint)
        };
        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, pingUri)
        {
            Content = new StringContent(bodyJson, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Terminal-Id", cfg.TerminalId);
        request.Headers.TryAddWithoutValidation("X-Terminal-Name", effectiveTerminalName);
        request.Headers.TryAddWithoutValidation("X-Terminal-Mode", "terminal");
        request.Headers.TryAddWithoutValidation("X-Terminal-Hostname", hostname);
        request.Headers.TryAddWithoutValidation("X-Terminal-OS", osPlatform);
        request.Headers.TryAddWithoutValidation("X-Terminal-Version", resolvedVersion);
        if (!string.IsNullOrWhiteSpace(effectivePairingToken))
        {
            request.Headers.TryAddWithoutValidation("X-Network-Token", effectivePairingToken);
            request.Headers.TryAddWithoutValidation("X-Terminal-Token", effectivePairingToken);
        }

        try
        {
            using var response = await client.SendAsync(request, cancellationToken);
            var latencyMs = (int)Math.Max(1, Environment.TickCount64 - startTs);
            var rawText = await response.Content.ReadAsStringAsync(cancellationToken);

            try
            {
                using var doc = JsonDocument.Parse(rawText);
                var root = doc.RootElement;
                var isOk = root.TryGetProperty("ok", out var okProp) && okProp.ValueKind == JsonValueKind.True;

                if (response.IsSuccessStatusCode && isOk)
                {
                    JsonElement? termEl = root.TryGetProperty("terminal", out var term) ? term.Clone() : null;
                    if (termEl.HasValue && termEl.Value.ValueKind == JsonValueKind.Object &&
                        termEl.Value.TryGetProperty("is_authorized", out var authProp) &&
                        (authProp.ValueKind == JsonValueKind.False ||
                         (authProp.ValueKind == JsonValueKind.Number && authProp.TryGetInt32(out var av) && av == 0)))
                    {
                        return new ServerConnectionTestResult
                        {
                            Ok = false,
                            ServerUrl = normalized,
                            Terminal = termEl,
                            LatencyMs = latencyMs,
                            Error = "Connected to Store Server, but this terminal is not yet authorized. Approve this terminal in Store Server Settings -> Network & Multi-PC Terminals (or check your license seats / pairing token)."
                        };
                    }

                    return new ServerConnectionTestResult
                    {
                        Ok = true,
                        ServerUrl = normalized,
                        StoreName = root.TryGetProperty("store_name", out var sn) && sn.ValueKind == JsonValueKind.String
                            ? sn.GetString()
                            : "Karobar Server",
                        DbDriver = root.TryGetProperty("db_driver", out var dd) && dd.ValueKind == JsonValueKind.String
                            ? dd.GetString()
                            : "mariadb",
                        OsFamily = root.TryGetProperty("os_family", out var of) && of.ValueKind == JsonValueKind.String
                            ? of.GetString()
                            : "Linux",
                        Terminal = termEl,
                        LatencyMs = latencyMs
                    };
                }

                var msg = root.TryGetProperty("message", out var msgProp) && msgProp.ValueKind == JsonValueKind.String
                    ? msgProp.GetString()
                    : $"Server responded with status {(int)response.StatusCode}";

                return new ServerConnectionTestResult
                {
                    Ok = false,
                    Error = msg
                };
            }
            catch
            {
                if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 400)
                {
                    return new ServerConnectionTestResult
                    {
                        Ok = true,
                        ServerUrl = normalized,
                        StoreName = "Karobar Server",
                        LatencyMs = latencyMs
                    };
                }

                return new ServerConnectionTestResult
                {
                    Ok = false,
                    Error = $"Unexpected response from {normalized} (HTTP {(int)response.StatusCode})"
                };
            }
        }
        catch (TaskCanceledException)
        {
            return new ServerConnectionTestResult
            {
                Ok = false,
                Error = $"Connection timed out reaching {normalized}"
            };
        }
        catch (Exception ex)
        {
            return new ServerConnectionTestResult
            {
                Ok = false,
                Error = $"Cannot reach {normalized}: {ex.Message}"
            };
        }
    }

    private void PersistConfigToDisk(NetworkConfig config)
    {
        var serialized = JsonSerializer.Serialize(config, JsonWriteOptions);
        foreach (var cfgPath in GetNetworkConfigPaths())
        {
            try
            {
                var dir = Path.GetDirectoryName(cfgPath);
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(cfgPath, serialized, Encoding.UTF8);
            }
            catch
            {
                // Ignore if one candidate path is read-only
            }
        }
    }

    private static NetworkConfig CloneConfig(NetworkConfig src) => new()
    {
        Mode = src.Mode,
        ServerUrl = src.ServerUrl,
        TerminalId = src.TerminalId,
        TerminalName = src.TerminalName,
        PairingToken = src.PairingToken,
        ServerCertThumbprint = src.ServerCertThumbprint
    };
}

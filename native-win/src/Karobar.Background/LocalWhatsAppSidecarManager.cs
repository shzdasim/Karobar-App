using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Karobar.Core.Entities;
using Karobar.Data;

namespace Karobar.Background;

/// <summary>
/// Native Local WhatsApp Sidecar Manager (`app/Services/LocalWhatsAppAgentService.php`).
/// Spawns and manages `scripts/whatsapp-agent.mjs` using bundled portable Node.js (`runtime/node/node.exe`),
/// binds the child process to a Windows Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` so zero orphan
/// Node.js processes remain when the main application exits, and exposes `StatusAsync`, `StartAsync`,
/// `RequestPairingCodeAsync`, `LogoutAsync`, and `StopAsync`.
/// </summary>
public sealed class LocalWhatsAppSidecarManager : IDisposable
{
    public const int DefaultPort = 3001;
    public const string DefaultHost = "127.0.0.1";

    private readonly string _appRoot;
    private readonly HttpClient _httpClient;
    private Process? _sidecarProcess;
    private IntPtr _jobObjectHandle = IntPtr.Zero;
    private readonly object _procLock = new();

    public LocalWhatsAppSidecarManager(string? appRoot = null, HttpClient? httpClient = null)
    {
        _appRoot = ResolveAppRoot(appRoot);
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        InitializeWindowsJobObject();
    }

    private static string ResolveAppRoot(string? explicitRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot) && Directory.Exists(explicitRoot))
        {
            return Path.GetFullPath(explicitRoot);
        }

        var envRoot = Environment.GetEnvironmentVariable("KAROBAR_ROOT_PATH")
            ?? Environment.GetEnvironmentVariable("KAROBAR_APP_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
        {
            return Path.GetFullPath(envRoot);
        }

        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
        {
            if (File.Exists(Path.Combine(dir, "artisan")) || File.Exists(Path.Combine(dir, "scripts", "whatsapp-agent.mjs")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir) ?? "";
        }

        return Directory.GetCurrentDirectory();
    }

    #region Windows Job Object P/Invoke (Auto-Terminate Child Sidecar on App Close)

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    private void InitializeWindowsJobObject()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        try
        {
            var hJob = CreateJobObject(IntPtr.Zero, null);
            if (hJob == IntPtr.Zero) return;

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
            {
                BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
                {
                    LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
                }
            };

            var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
            var ptr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, ptr, false);
                if (SetInformationJobObject(hJob, JobObjectExtendedLimitInformation, ptr, (uint)length))
                {
                    _jobObjectHandle = hJob;
                }
                else
                {
                    CloseHandle(hJob);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            // Ignore if running in restricted container
        }
    }

    private void AttachProcessToJobObject(Process proc)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || _jobObjectHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            AssignProcessToJobObject(_jobObjectHandle, proc.Handle);
        }
        catch
        {
            // Ignore if process already exited or attached to parent job
        }
    }

    #endregion

    public string ResolveAgentUrl(Setting? setting = null)
    {
        var configured = (setting?.WhatsappApiUrl ?? "").Trim();
        if (!string.IsNullOrEmpty(configured) &&
            (configured.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
             configured.Contains("localhost", StringComparison.OrdinalIgnoreCase)) &&
            Uri.TryCreate(configured, UriKind.Absolute, out var uri))
        {
            var port = uri.Port > 0 ? uri.Port : DefaultPort;
            return $"{uri.Scheme}://{uri.Host}:{port}";
        }
        return $"http://{DefaultHost}:{DefaultPort}";
    }

    public async Task<string> EnsureApiKeyAsync(
        KarobarDbContext? db = null,
        Setting? setting = null,
        CancellationToken cancellationToken = default)
    {
        var existing = (setting?.WhatsappAccessToken ?? "").Trim();
        if (!string.IsNullOrEmpty(existing))
        {
            return existing;
        }

        var randomHex = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
        var generated = $"karobar_wa_{randomHex}";

        if (setting != null && db != null)
        {
            try
            {
                setting.WhatsappAccessToken = generated;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Ignore if locked
            }
        }

        return generated;
    }

    /// <summary>
    /// Locate the Node.js executable across bundled portable runtime (`runtime/node/node.exe`),
    /// Laravel Herd, Homebrew, NVM, FNM, Volta, Laragon, and system paths.
    /// </summary>
    public string ResolveNodeBinary()
    {
        var envBinary = (Environment.GetEnvironmentVariable("WHATSAPP_NODE_BINARY")
                         ?? Environment.GetEnvironmentVariable("NODE_BINARY")
                         ?? "").Trim();
        if (!string.IsNullOrEmpty(envBinary) && File.Exists(envBinary))
        {
            return envBinary;
        }

        var bundledWinNode = Path.Combine(_appRoot, "runtime", "node", "node.exe");
        if (File.Exists(bundledWinNode))
        {
            return bundledWinNode;
        }

        var baseDirNode = Path.Combine(AppContext.BaseDirectory, "runtime", "node", "node.exe");
        if (File.Exists(baseDirNode))
        {
            return baseDirNode;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var candidates = new[]
            {
                Path.Combine(userProfile, ".config", "herd", "bin", "node.exe"),
                @"C:\Program Files\nodejs\node.exe",
                @"C:\Program Files (x86)\nodejs\node.exe",
                Path.Combine(localAppData, "Programs", "nodejs", "node.exe"),
                @"C:\nvm4w\nodejs\node.exe"
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }
            return "node.exe";
        }
        else
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new[]
            {
                Path.Combine(home, "Library", "Application Support", "Herd", "bin", "node"),
                "/opt/homebrew/bin/node",
                "/usr/local/bin/node",
                "/usr/bin/node"
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c)) return c;
            }
            return "node";
        }
    }

    /// <summary>
    /// Query the local Node.js Baileys WhatsApp Agent status (`GET /status`).
    /// </summary>
    public async Task<Dictionary<string, object?>> StatusAsync(
        KarobarDbContext? db = null,
        Setting? setting = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = ResolveAgentUrl(setting);
        var apiKey = await EnsureApiKeyAsync(db, setting, cancellationToken);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(2));

            using var req = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/status");
            req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);

            using var res = await _httpClient.SendAsync(req, cts.Token);
            if (res.IsSuccessStatusCode)
            {
                var body = await res.Content.ReadAsStringAsync(cts.Token);
                var result = new Dictionary<string, object?>
                {
                    ["ok"] = true,
                    ["agent_running"] = true,
                    ["agent_url"] = baseUrl,
                    ["api_key"] = apiKey
                };

                if (!string.IsNullOrWhiteSpace(body))
                {
                    using var doc = JsonDocument.Parse(body);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in doc.RootElement.EnumerateObject())
                        {
                            result[prop.Name] = prop.Value.ValueKind switch
                            {
                                JsonValueKind.String => prop.Value.GetString(),
                                JsonValueKind.True => true,
                                JsonValueKind.False => false,
                                JsonValueKind.Null => null,
                                _ => JsonSerializer.Deserialize<object>(prop.Value.GetRawText())
                            };
                        }
                    }
                }

                result["ok"] = true;
                result["agent_running"] = true;
                result["agent_url"] = baseUrl;
                result["api_key"] = apiKey;
                return result;
            }
        }
        catch
        {
            // Agent process is not running on port 3001
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["agent_running"] = false,
            ["status"] = "stopped",
            ["qr_data_url"] = null,
            ["pairing_code"] = null,
            ["user"] = null,
            ["last_error"] = ReadRecentAgentError(),
            ["agent_url"] = baseUrl,
            ["api_key"] = apiKey
        };
    }

    /// <summary>
    /// Start the built-in Node.js WhatsApp Agent sidecar (`scripts/whatsapp-agent.mjs`) in the background
    /// and attach it to the Windows Job Object.
    /// </summary>
    public async Task<Dictionary<string, object?>> StartAsync(
        KarobarDbContext? db = null,
        Setting? setting = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = ResolveAgentUrl(setting);
        var apiKey = await EnsureApiKeyAsync(db, setting, cancellationToken);

        if (setting != null && db != null)
        {
            try
            {
                setting.WhatsappEnabled = true;
                setting.WhatsappProvider = "local_agent";
                setting.WhatsappApiUrl = baseUrl;
                setting.WhatsappAccessToken = apiKey;
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Continue even if save fails
            }
        }

        var current = await StatusAsync(db, setting, cancellationToken);
        if (current.TryGetValue("agent_running", out var runningObj) && runningObj is true)
        {
            var curStatus = current.TryGetValue("status", out var stObj) ? stObj?.ToString() : "";
            if (curStatus is "disconnected" or "error")
            {
                try
                {
                    using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/restart");
                    req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
                    await _httpClient.SendAsync(req, cancellationToken);
                    await Task.Delay(400, cancellationToken);
                }
                catch
                {
                    // Ignore
                }
            }
            return await StatusAsync(db, setting, cancellationToken);
        }

        var scriptPath = Path.Combine(_appRoot, "scripts", "whatsapp-agent.mjs");
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Built-in WhatsApp Agent script not found at {scriptPath}");
        }

        var nodeBinary = ResolveNodeBinary();
        var uri = new Uri(baseUrl);
        var port = uri.Port > 0 ? uri.Port : DefaultPort;
        var host = uri.Host;
        var logFile = Path.Combine(_appRoot, "storage", "logs", "whatsapp-agent.log");
        var storageDir = Path.Combine(_appRoot, "storage", "app", "whatsapp-agent");

        Directory.CreateDirectory(Path.GetDirectoryName(logFile)!);
        Directory.CreateDirectory(storageDir);

        try
        {
            await File.WriteAllTextAsync(logFile, "", cancellationToken);
        }
        catch
        {
            // Ignore
        }

        SpawnSidecarProcess(nodeBinary, scriptPath, logFile, storageDir, host, port, apiKey);

        for (var i = 0; i < 16; i++)
        {
            await Task.Delay(250, cancellationToken);
            var check = await StatusAsync(db, setting, cancellationToken);
            if (check.TryGetValue("agent_running", out var isRun) && isRun is true)
            {
                var st = check.TryGetValue("status", out var s) ? s?.ToString() : "";
                if (st is "qr_ready" or "connected" || i >= 8)
                {
                    return check;
                }
            }
        }

        return await StatusAsync(db, setting, cancellationToken);
    }

    private void SpawnSidecarProcess(
        string nodeBinary,
        string scriptPath,
        string logFile,
        string storageDir,
        string host,
        int port,
        string apiKey)
    {
        lock (_procLock)
        {
            if (_sidecarProcess is { HasExited: false })
            {
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = nodeBinary,
                WorkingDirectory = _appRoot,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add(scriptPath);
            psi.Environment["WHATSAPP_AGENT_PORT"] = port.ToString();
            psi.Environment["WHATSAPP_AGENT_HOST"] = host;
            psi.Environment["WHATSAPP_AGENT_API_KEY"] = apiKey;
            psi.Environment["WHATSAPP_AGENT_STORAGE_DIR"] = storageDir;
            psi.Environment["NODE_NO_WARNINGS"] = "1";

            try
            {
                if (_sidecarProcess != null)
                {
                    try { _sidecarProcess.Dispose(); } catch { }
                    _sidecarProcess = null;
                }

                var proc = Process.Start(psi);
                if (proc != null)
                {
                    AttachProcessToJobObject(proc);
                    _sidecarProcess = proc;

                    // Drain stdout and stderr concurrently line-by-line (never await ReadToEndAsync sequentially -> avoids 4KB Windows pipe deadlock & unbounded RAM growth!)
                    void StartLineDrain(StreamReader reader, string tag)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                string? line;
                                while ((line = await reader.ReadLineAsync()) != null)
                                {
                                    try
                                    {
                                        var fi = new FileInfo(logFile);
                                        if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                                        {
                                            File.WriteAllText(logFile, $"[{DateTime.UtcNow:O}] Log rotated (exceeded 2MB){Environment.NewLine}");
                                        }
                                        await File.AppendAllTextAsync(logFile, $"[{tag}] {line}{Environment.NewLine}");
                                    }
                                    catch { }
                                }
                            }
                            catch { }
                        });
                    }
                    StartLineDrain(proc.StandardOutput, "OUT");
                    StartLineDrain(proc.StandardError, "ERR");
                }
            }
            catch (Exception ex)
            {
                try
                {
                    File.AppendAllText(logFile, $"Failed to spawn Node binary ({nodeBinary}): {ex.Message}{Environment.NewLine}");
                }
                catch
                {
                    // Ignore
                }
            }
        }
    }

    /// <summary>
    /// Request an 8-digit phone number pairing code from the built-in agent (`POST /pair-code`).
    /// </summary>
    public async Task<Dictionary<string, object?>> RequestPairingCodeAsync(
        string rawPhone,
        KarobarDbContext? db = null,
        Setting? setting = null,
        CancellationToken cancellationToken = default)
    {
        var status = await StatusAsync(db, setting, cancellationToken);
        if (!status.TryGetValue("agent_running", out var running) || running is not true)
        {
            await StartAsync(db, setting, cancellationToken);
        }

        var baseUrl = ResolveAgentUrl(setting);
        var apiKey = await EnsureApiKeyAsync(db, setting, cancellationToken);
        var defaultCc = string.IsNullOrWhiteSpace(setting?.WhatsappDefaultCountryCode) ? "92" : setting!.WhatsappDefaultCountryCode!.Trim();
        var digits = Regex.Replace(rawPhone ?? "", @"\D+", "");
        if (digits.StartsWith("00", StringComparison.Ordinal)) digits = digits[2..];
        if (digits.Length == 11 && digits.StartsWith("03", StringComparison.Ordinal)) digits = defaultCc + digits[1..];
        else if (digits.Length == 10 && digits.StartsWith("3", StringComparison.Ordinal)) digits = defaultCc + digits;

        if (string.IsNullOrEmpty(digits))
        {
            throw new ArgumentException("Please enter a valid WhatsApp phone number (e.g. 0300-1234567).");
        }

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/pair-code");
        req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
        req.Content = new StringContent(JsonSerializer.Serialize(new { phone = digits }), Encoding.UTF8, "application/json");

        using var res = await _httpClient.SendAsync(req, cancellationToken);
        var body = await res.Content.ReadAsStringAsync(cancellationToken);
        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Failed to request 8-digit pairing code from WhatsApp: {body}");
        }

        return JsonSerializer.Deserialize<Dictionary<string, object?>>(body) ?? new Dictionary<string, object?> { ["ok"] = true };
    }

    /// <summary>
    /// Unlink the currently connected WhatsApp account and generate a fresh QR code (`POST /logout`).
    /// </summary>
    public async Task<Dictionary<string, object?>> LogoutAsync(
        KarobarDbContext? db = null,
        Setting? setting = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = ResolveAgentUrl(setting);
        var apiKey = await EnsureApiKeyAsync(db, setting, cancellationToken);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/logout");
            req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            using var res = await _httpClient.SendAsync(req, cancellationToken);
            if (res.IsSuccessStatusCode)
            {
                await Task.Delay(800, cancellationToken);
                return await StatusAsync(db, setting, cancellationToken);
            }
        }
        catch
        {
            // Clear auth folder directly if agent wasn't running
        }

        var authDir = Path.Combine(_appRoot, "storage", "app", "whatsapp-agent", "auth");
        if (Directory.Exists(authDir))
        {
            foreach (var file in Directory.GetFiles(authDir))
            {
                try { File.Delete(file); } catch { }
            }
        }

        return await StatusAsync(db, setting, cancellationToken);
    }

    /// <summary>
    /// Stop the background Node.js WhatsApp Agent process (`POST /shutdown`).
    /// </summary>
    public async Task<Dictionary<string, object?>> StopAsync(
        KarobarDbContext? db = null,
        Setting? setting = null,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = ResolveAgentUrl(setting);
        var apiKey = await EnsureApiKeyAsync(db, setting, cancellationToken);

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(3));
            using var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/shutdown");
            req.Headers.TryAddWithoutValidation("X-Api-Key", apiKey);
            await _httpClient.SendAsync(req, cts.Token);
            await Task.Delay(300, cancellationToken);
        }
        catch
        {
            // Already stopped
        }

        lock (_procLock)
        {
            if (_sidecarProcess is { HasExited: false })
            {
                try { _sidecarProcess.Kill(entireProcessTree: true); } catch { }
            }
            _sidecarProcess = null;
        }

        return await StatusAsync(db, setting, cancellationToken);
    }

    public string? ReadRecentAgentError()
    {
        var logFile = Path.Combine(_appRoot, "storage", "logs", "whatsapp-agent.log");
        if (!File.Exists(logFile)) return null;

        try
        {
            var lines = File.ReadAllLines(logFile)
                .Select(l => l.Trim())
                .Where(l => !string.IsNullOrEmpty(l) && !l.Contains("[WhatsApp Agent] Listening on", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (lines.Count == 0) return null;
            var last = lines[^1];
            return last.Length > 240 ? last[..240] : last;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        lock (_procLock)
        {
            if (_sidecarProcess is { HasExited: false })
            {
                try { _sidecarProcess.Kill(entireProcessTree: true); } catch { }
            }
            _sidecarProcess?.Dispose();
            _sidecarProcess = null;
        }

        if (_jobObjectHandle != IntPtr.Zero && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            CloseHandle(_jobObjectHandle);
            _jobObjectHandle = IntPtr.Zero;
        }
    }
}

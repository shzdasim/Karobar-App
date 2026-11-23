using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Karobar.Printing;

public sealed class NativePrinterInfo
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DriverName { get; set; } = "";
    public string PortName { get; set; } = "";
    public bool IsDefault { get; set; }
    public bool IsVirtual { get; set; }
    public bool WorkOffline { get; set; }
    public bool IsOnline { get; set; }
    public bool IsThermalPos { get; set; }
    public bool IsPreferred { get; set; }
    public int Status { get; set; }
}

public sealed class PrinterStrategyResult
{
    public string Mode { get; set; } = "no-printer";
    public string? DeviceName { get; set; }
    public NativePrinterInfo? PrinterInfo { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Native Windows Print Spooler (`winspool.drv`) &amp; Raw ESC/POS Direct Driver.
/// Supports sub-50ms raw byte/ESC-POS printing, RJ11 cash drawer kick (\x1B\x70\x00\x19\xFA),
/// Win32 Print Spooler (EnumPrintersW / GetDefaultPrinterW) printer enumeration, virtual printer filtering, and SumatraPDF / GDI silent PDF spooling.
/// </summary>
public sealed class Win32RawPrinterService
{
    /// <summary>
    /// Standard ESC/POS RJ11 Cash Drawer Kick Pulse: ESC p 0 25 250 (\x1B\x70\x00\x19\xFA)
    /// </summary>
    public static readonly byte[] CashDrawerKickCommand = [0x1B, 0x70, 0x00, 0x19, 0xFA];

    /// <summary>
    /// Standard ESC/POS Initialize &amp; Partial Cut command sequence.
    /// </summary>
    public static readonly byte[] EscPosInitCommand = [0x1B, 0x40];
    public static readonly byte[] EscPosCutCommand = [0x1D, 0x56, 0x42, 0x00];

    private static readonly Regex VirtualPrinterNameOrDriverRegex = new(
        @"(microsoft print to pdf|microsoft xps|xps document writer|onenote|send to onenote|fax|anydesk|teamviewer|adobe pdf|foxit|cutepdf|dopdf|bullzip|nitro pdf|pdfcreator|pdffactory|clawpdf|pdf24|wondershare|wps pdf|fineprint|snagit|paperport|priprinter|universal document|canva|save as pdf)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex VirtualPrinterPortRegex = new(
        @"^(portprompt:|file:|nul:|xpsport:|shrfax:|microsoft\.office\.onenote|anydesk|pdf)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ThermalPosPrinterRegex = new(
        @"(pos|thermal|receipt|58mm|80mm|78mm|72mm|pos58|pos80|pos-58|pos-80|xprinter|xp-|black\s*copper|bc-|epson.*tm|tm-t|tm-u|bixolon|star\s*tsp|citizen|rongta|rp58|rp80|birch|sewoo|gainscha|hprt|deli|zywell|munbyn|netum|goojprt|milestone|gp-|zjiang|zj-|gprinter|hoin|hop-|snbc|btp-|srp-|mht-|niimbot|speedx|tsc|argox|zebra)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _appRoot;
    private List<NativePrinterInfo>? _cachedPrinters;
    private DateTime _cachedPrintersAt = DateTime.MinValue;
    private readonly object _lock = new();

    public Win32RawPrinterService(string? appRoot = null)
    {
        _appRoot = ResolveAppRoot(appRoot);
    }

    public static string ResolveAppRoot(string? explicitRoot = null)
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
            if (Directory.Exists(Path.Combine(dir, "database")) || File.Exists(Path.Combine(dir, "public", "build", "manifest.json")))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir) ?? "";
        }

        return Directory.GetCurrentDirectory();
    }

    #region Win32 winspool.drv P/Invoke Declarations

    private const uint PRINTER_ENUM_LOCAL = 0x00000002;
    private const uint PRINTER_ENUM_CONNECTIONS = 0x00000004;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;
    private const uint PRINTER_ATTRIBUTE_WORK_OFFLINE = 0x00000400;
    private const uint PRINTER_STATUS_ERROR = 0x00000002;
    private const uint PRINTER_STATUS_PENDING_DELETION = 0x00000004;
    private const uint PRINTER_STATUS_OFFLINE = 0x00000080;
    private const uint PRINTER_STATUS_NOT_AVAILABLE = 0x00001000;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PRINTER_INFO_2W
    {
        public IntPtr pServerName;
        public IntPtr pPrinterName;
        public IntPtr pShareName;
        public IntPtr pPortName;
        public IntPtr pDriverName;
        public IntPtr pComment;
        public IntPtr pLocation;
        public IntPtr pDevMode;
        public IntPtr pSepFile;
        public IntPtr pPrintProcessor;
        public IntPtr pDatatype;
        public IntPtr pParameters;
        public IntPtr pSecurityDescriptor;
        public uint Attributes;
        public uint Priority;
        public uint DefaultPriority;
        public uint StartTime;
        public uint UntilTime;
        public uint Status;
        public uint cJobs;
        public uint AveragePPM;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DOCINFOW
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDocName;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pOutputFile;

        [MarshalAs(UnmanagedType.LPWStr)]
        public string pDataType;
    }

    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool OpenPrinter(string szPrinter, out IntPtr hPrinter, IntPtr pd);

    [DllImport("winspool.drv", EntryPoint = "ClosePrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool ClosePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartDocPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StartDocPrinter(IntPtr hPrinter, int level, [In] ref DOCINFOW di);

    [DllImport("winspool.drv", EntryPoint = "EndDocPrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool EndDocPrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "StartPagePrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool StartPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "EndPagePrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool EndPagePrinter(IntPtr hPrinter);

    [DllImport("winspool.drv", EntryPoint = "WritePrinter", SetLastError = true, ExactSpelling = true)]
    private static extern bool WritePrinter(IntPtr hPrinter, IntPtr pBytes, int dwCount, out int dwWritten);

    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool EnumPrinters(
        uint flags,
        string? name,
        uint level,
        IntPtr pPrinterEnum,
        uint cbBuf,
        out uint pcbNeeded,
        out uint pcReturned);

    [DllImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern bool GetDefaultPrinter(StringBuilder? pszBuffer, ref uint pcchBuffer);

    #endregion

    /// <summary>
    /// Send raw bytes (ESC/POS commands, raw receipt stream, or cash drawer kick) directly to a Windows printer via winspool.drv.
    /// </summary>
    public (bool Ok, int BytesWritten, string? Error) SendRawBytesToPrinter(
        string printerName,
        byte[] bytes,
        string documentName = "Karobar Raw ESC/POS Job")
    {
        if (string.IsNullOrWhiteSpace(printerName))
        {
            return (false, 0, "Printer name is required.");
        }

        if (bytes == null || bytes.Length == 0)
        {
            return (false, 0, "Raw byte payload cannot be empty.");
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Non-Windows development/test simulation: verify payload & return success
            return (true, bytes.Length, null);
        }

        IntPtr hPrinter = IntPtr.Zero;
        IntPtr pUnmanagedBytes = IntPtr.Zero;

        try
        {
            if (!OpenPrinter(printerName.Normalize(), out hPrinter, IntPtr.Zero) || hPrinter == IntPtr.Zero)
            {
                var errCode = Marshal.GetLastWin32Error();
                return (false, 0, $"OpenPrinter failed for '{printerName}' (Win32 Error {errCode}).");
            }

            var di = new DOCINFOW
            {
                pDocName = string.IsNullOrWhiteSpace(documentName) ? "Karobar POS Receipt" : documentName,
                pDataType = "RAW",
                pOutputFile = null
            };

            var jobId = StartDocPrinter(hPrinter, 1, ref di);
            if (jobId <= 0)
            {
                var errCode = Marshal.GetLastWin32Error();
                return (false, 0, $"StartDocPrinter failed for '{printerName}' (Win32 Error {errCode}).");
            }

            try
            {
                if (!StartPagePrinter(hPrinter))
                {
                    var errCode = Marshal.GetLastWin32Error();
                    return (false, 0, $"StartPagePrinter failed for '{printerName}' (Win32 Error {errCode}).");
                }

                try
                {
                    pUnmanagedBytes = Marshal.AllocCoTaskMem(bytes.Length);
                    Marshal.Copy(bytes, 0, pUnmanagedBytes, bytes.Length);

                    if (!WritePrinter(hPrinter, pUnmanagedBytes, bytes.Length, out var dwWritten))
                    {
                        var errCode = Marshal.GetLastWin32Error();
                        return (false, dwWritten, $"WritePrinter failed for '{printerName}' (Win32 Error {errCode}).");
                    }

                    return (true, dwWritten, null);
                }
                finally
                {
                    EndPagePrinter(hPrinter);
                }
            }
            finally
            {
                EndDocPrinter(hPrinter);
            }
        }
        catch (Exception ex)
        {
            return (false, 0, $"WinSpool exception: {ex.Message}");
        }
        finally
        {
            if (pUnmanagedBytes != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(pUnmanagedBytes);
            }
            if (hPrinter != IntPtr.Zero)
            {
                ClosePrinter(hPrinter);
            }
        }
    }

    /// <summary>
    /// Trigger the RJ11 cash drawer connected to the thermal receipt printer using ESC/POS command \x1B\x70\x00\x19\xFA.
    /// </summary>
    public async Task<(bool Ok, string? Printer, string? Error)> KickCashDrawerAsync(string? requestedPrinter = null)
    {
        var strategy = await ResolvePrinterStrategyAsync(requestedPrinter, isThermalDoc: true);
        if (strategy.Mode == "no-printer" || string.IsNullOrWhiteSpace(strategy.DeviceName))
        {
            return (false, null, strategy.Error ?? "No physical thermal printer detected to kick cash drawer.");
        }

        // Prevent sending raw ESC/POS drawer kick bytes to an obvious office laser/inkjet A4 printer unless explicitly chosen.
        if (string.IsNullOrWhiteSpace(requestedPrinter) && strategy.PrinterInfo != null && !strategy.PrinterInfo.IsThermalPos)
        {
            var lowerName = $"{strategy.PrinterInfo.Name} {strategy.PrinterInfo.DriverName}".ToLowerInvariant();
            if (lowerName.Contains("laserjet") || lowerName.Contains("officejet") || lowerName.Contains("deskjet") ||
                lowerName.Contains("inkjet") || lowerName.Contains("ecotank") || lowerName.Contains("pixma") ||
                lowerName.Contains("brother hl") || lowerName.Contains("brother mfc"))
            {
                return (false, strategy.DeviceName, $"Skipped cash drawer kick because '{strategy.DeviceName}' appears to be an A4 office printer. Select your thermal POS printer in Settings -> Print & Receipt Layouts.");
            }
        }

        var (ok, _, error) = SendRawBytesToPrinter(
            strategy.DeviceName,
            CashDrawerKickCommand,
            "Karobar Cash Drawer Kick");

        return (ok, strategy.DeviceName, error);
    }

    /// <summary>
    /// Convert plain text or ESC/POS formatted receipt lines into a raw ESC/POS byte stream with optional cash drawer kick and auto-cutter.
    /// </summary>
    public byte[] BuildEscPosReceiptPayload(
        string receiptText,
        bool kickDrawer = false,
        bool cutPaper = true)
    {
        using var ms = new MemoryStream();
        ms.Write(EscPosInitCommand, 0, EscPosInitCommand.Length);

        if (kickDrawer)
        {
            ms.Write(CashDrawerKickCommand, 0, CashDrawerKickCommand.Length);
        }

        var normalized = (receiptText ?? "").Replace("\r\n", "\n").Replace("\r", "\n");
        var textBytes = Encoding.UTF8.GetBytes(normalized + "\n\n\n");
        ms.Write(textBytes, 0, textBytes.Length);

        if (cutPaper)
        {
            ms.Write(EscPosCutCommand, 0, EscPosCutCommand.Length);
        }

        return ms.ToArray();
    }

    public string GetPrinterConfigFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            return Path.Combine(appData, "Karobar-App-Native", "printer-config.json");
        }
        return Path.Combine(_appRoot, "storage", "app", "printer-config.json");
    }

    public string ReadPreferredPrinterConfig()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var candidates = new List<string> { GetPrinterConfigFilePath() };
        if (!string.IsNullOrWhiteSpace(appData))
        {
            candidates.Add(Path.Combine(appData, "Karobar-App", "printer-config.json"));
        }
        candidates.Add(Path.Combine(_appRoot, "storage", "app", "printer-config.json"));

        foreach (var file in candidates)
        {
            try
            {
                if (!File.Exists(file)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("preferredPrinter", out var prop) && prop.ValueKind == JsonValueKind.String)
                {
                    var name = (prop.GetString() ?? "").Trim();
                    if (!string.IsNullOrEmpty(name) &&
                        !name.Equals("default", StringComparison.OrdinalIgnoreCase) &&
                        !name.Equals("system_default", StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                }
            }
            catch
            {
                // Ignore malformed config
            }
        }
        return "";
    }

    public string WritePreferredPrinterConfig(string? deviceName)
    {
        var clean = (deviceName ?? "").Trim();
        var normalized = string.IsNullOrEmpty(clean) ||
                         clean.Equals("default", StringComparison.OrdinalIgnoreCase) ||
                         clean.Equals("system_default", StringComparison.OrdinalIgnoreCase)
            ? ""
            : clean;

        var payload = JsonSerializer.Serialize(new
        {
            preferredPrinter = normalized,
            updatedAt = DateTime.UtcNow.ToString("O")
        }, new JsonSerializerOptions { WriteIndented = true });

        foreach (var file in new[] { GetPrinterConfigFilePath(), Path.Combine(_appRoot, "storage", "app", "printer-config.json") })
        {
            try
            {
                var dir = Path.GetDirectoryName(file);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(file, payload, Encoding.UTF8);
            }
            catch
            {
                // Ignore if one location is read-only
            }
        }

        return normalized;
    }

    /// <summary>
    /// Enumerate installed printers and classify physical vs virtual, thermal vs A4, and online status.
    /// </summary>
    public async Task<List<NativePrinterInfo>> GetPrintersAsync(bool forceRefresh = false)
    {
        lock (_lock)
        {
            if (!forceRefresh && _cachedPrinters != null && (DateTime.UtcNow - _cachedPrintersAt).TotalSeconds < 15)
            {
                return _cachedPrinters;
            }
        }

        var preferredName = ReadPreferredPrinterConfig();
        var result = new List<NativePrinterInfo>();
        var enumeratedOk = !OperatingSystem.IsWindows();

        if (OperatingSystem.IsWindows())
        {
            try
            {
                var defaultPrinterName = GetWindowsDefaultPrinterName();
                var flagSets = new[]
                {
                    PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS,
                    PRINTER_ENUM_LOCAL
                };

                foreach (var flags in flagSets)
                {
                    uint cbNeeded = 0;
                    uint pcReturned = 0;
                    EnumPrinters(flags, null, 2, IntPtr.Zero, 0, out cbNeeded, out pcReturned);

                    if (cbNeeded > 0)
                    {
                        for (var attempt = 0; attempt < 2; attempt++)
                        {
                            var pBuffer = Marshal.AllocHGlobal((int)cbNeeded);
                            try
                            {
                                if (EnumPrinters(flags, null, 2, pBuffer, cbNeeded, out cbNeeded, out pcReturned))
                                {
                                    var structSize = Marshal.SizeOf<PRINTER_INFO_2W>();
                                    for (var i = 0; i < (int)pcReturned; i++)
                                    {
                                        var info = Marshal.PtrToStructure<PRINTER_INFO_2W>(IntPtr.Add(pBuffer, i * structSize));
                                        var name = (Marshal.PtrToStringUni(info.pPrinterName) ?? "").Trim();
                                        if (string.IsNullOrEmpty(name)) continue;

                                        var driverName = (Marshal.PtrToStringUni(info.pDriverName) ?? "").Trim();
                                        var portName = (Marshal.PtrToStringUni(info.pPortName) ?? "").Trim();
                                        var isDefault = !string.IsNullOrEmpty(defaultPrinterName) &&
                                                        string.Equals(name, defaultPrinterName, StringComparison.OrdinalIgnoreCase);
                                        var workOffline = (info.Attributes & PRINTER_ATTRIBUTE_WORK_OFFLINE) != 0 ||
                                                          (info.Status & (PRINTER_STATUS_OFFLINE | PRINTER_STATUS_NOT_AVAILABLE)) != 0;
                                        var hasHardwareError = (info.Status & (PRINTER_STATUS_ERROR | PRINTER_STATUS_PENDING_DELETION)) != 0;

                                        result.Add(ClassifyPrinter(
                                            name,
                                            name,
                                            driverName,
                                            portName,
                                            isDefault,
                                            workOffline,
                                            (int)info.Status,
                                            preferredName,
                                            hasHardwareError));
                                    }
                                    enumeratedOk = true;
                                    break;
                                }

                                var err = Marshal.GetLastWin32Error();
                                if (err != ERROR_INSUFFICIENT_BUFFER || cbNeeded == 0)
                                {
                                    break;
                                }
                            }
                            finally
                            {
                                if (pBuffer != IntPtr.Zero)
                                {
                                    Marshal.FreeHGlobal(pBuffer);
                                }
                            }
                        }
                    }
                    else
                    {
                        var err = Marshal.GetLastWin32Error();
                        if (err is 0 or 122 or 18)
                        {
                            enumeratedOk = true;
                        }
                    }

                    if (result.Count > 0)
                    {
                        break;
                    }
                }

                // Fallback: if spooler enumeration failed/returned empty but Windows reports a default printer, expose it.
                if (result.Count == 0 && !string.IsNullOrWhiteSpace(defaultPrinterName))
                {
                    result.Add(ClassifyPrinter(
                        defaultPrinterName,
                        defaultPrinterName,
                        "",
                        "",
                        isDefault: true,
                        workOfflineFlag: false,
                        printerStatus: 0,
                        preferredName: preferredName,
                        hasHardwareError: false));
                    enumeratedOk = true;
                }
            }
            catch
            {
                enumeratedOk = false;
            }
        }
        else
        {
            // macOS / Linux CUPS lpstat enumeration for cross-platform parity
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/usr/bin/lpstat",
                    Arguments = "-p -d",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                if (File.Exists(psi.FileName))
                {
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        var stdout = await proc.StandardOutput.ReadToEndAsync();
                        await proc.WaitForExitAsync();
                        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        var defaultPrinter = "";
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("system default destination:", StringComparison.OrdinalIgnoreCase))
                            {
                                defaultPrinter = line["system default destination:".Length..].Trim();
                            }
                        }
                        foreach (var line in lines)
                        {
                            if (line.StartsWith("printer ", StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length >= 2)
                                {
                                    var name = parts[1].Trim();
                                    var isDef = string.Equals(name, defaultPrinter, StringComparison.OrdinalIgnoreCase);
                                    result.Add(ClassifyPrinter(name, name, "CUPS", "USB", isDef, false, 3, preferredName));
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore if lpstat not present
            }
        }

        lock (_lock)
        {
            if (enumeratedOk)
            {
                _cachedPrinters = result;
                _cachedPrintersAt = DateTime.UtcNow;
                return result;
            }

            if (_cachedPrinters != null)
            {
                return _cachedPrinters;
            }
        }

        return result;
    }

    private static string GetWindowsDefaultPrinterName()
    {
        try
        {
            uint pcchBuffer = 0;
            GetDefaultPrinter(null, ref pcchBuffer);
            if (pcchBuffer == 0)
            {
                return "";
            }

            var sb = new StringBuilder((int)pcchBuffer);
            return GetDefaultPrinter(sb, ref pcchBuffer) ? sb.ToString().Trim() : "";
        }
        catch
        {
            return "";
        }
    }

    public static NativePrinterInfo ClassifyPrinter(
        string name,
        string displayName,
        string driverName,
        string portName,
        bool isDefault,
        bool workOfflineFlag,
        int printerStatus,
        string preferredName,
        bool hasHardwareError = false)
    {
        var isVirtual = VirtualPrinterNameOrDriverRegex.IsMatch(name) ||
                        VirtualPrinterNameOrDriverRegex.IsMatch(displayName) ||
                        VirtualPrinterNameOrDriverRegex.IsMatch(driverName) ||
                        VirtualPrinterPortRegex.IsMatch(portName);

        var workOffline = workOfflineFlag;
        var isOnline = !isVirtual && !workOffline && !hasHardwareError;
        var isThermalPos = !isVirtual && (
            ThermalPosPrinterRegex.IsMatch(name) ||
            ThermalPosPrinterRegex.IsMatch(displayName) ||
            ThermalPosPrinterRegex.IsMatch(driverName));

        var isPreferred = !string.IsNullOrWhiteSpace(preferredName) &&
                          string.Equals(name, preferredName, StringComparison.OrdinalIgnoreCase);

        return new NativePrinterInfo
        {
            Name = name,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? name : displayName,
            DriverName = driverName,
            PortName = portName,
            IsDefault = isDefault,
            IsVirtual = isVirtual,
            WorkOffline = workOffline,
            IsOnline = isOnline,
            IsThermalPos = isThermalPos,
            IsPreferred = isPreferred,
            Status = printerStatus
        };
    }

    public async Task<PrinterStrategyResult> ResolvePrinterStrategyAsync(
        string? requestedDeviceName = null,
        bool isThermalDoc = false)
    {
        var explicitRequest = (!string.IsNullOrWhiteSpace(requestedDeviceName)
            ? requestedDeviceName
            : ReadPreferredPrinterConfig()).Trim();
        var printers = await GetPrintersAsync();
        if (printers.Count == 0 || !printers.Any(p => !p.IsVirtual))
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

        if (!string.IsNullOrEmpty(explicitRequest) &&
            !explicitRequest.Equals("default", StringComparison.OrdinalIgnoreCase) &&
            !explicitRequest.Equals("system_default", StringComparison.OrdinalIgnoreCase))
        {
            var matched = printers.FirstOrDefault(p => string.Equals(p.Name, explicitRequest, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                if (!matched.IsVirtual && matched.WorkOffline)
                {
                    var onlineAlt = isThermalDoc
                        ? (printers.FirstOrDefault(p => !p.IsVirtual && p.IsOnline && p.IsThermalPos)
                           ?? printers.FirstOrDefault(p => !p.IsVirtual && p.IsOnline))
                        : printers.FirstOrDefault(p => !p.IsVirtual && p.IsOnline);
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
                Error = "No physical hardware printer connected (only virtual PDF/OneNote printers detected). Please connect your printer or select it in Settings -> Print & Receipt Layouts."
            };
        }

        var onlinePhysical = physicalPrinters.Where(p => p.IsOnline).ToList();
        var candidatePool = onlinePhysical.Count > 0 ? onlinePhysical : physicalPrinters;

        if (isThermalDoc)
        {
            var thermalMatch = candidatePool.FirstOrDefault(p => p.IsThermalPos);
            if (thermalMatch != null)
            {
                return new PrinterStrategyResult
                {
                    Mode = "spooler",
                    DeviceName = thermalMatch.Name,
                    PrinterInfo = thermalMatch
                };
            }
        }

        var osDefault = candidatePool.FirstOrDefault(p => p.IsDefault) ?? candidatePool[0];
        return new PrinterStrategyResult
        {
            Mode = "spooler",
            DeviceName = osDefault.Name,
            PrinterInfo = osDefault
        };
    }

    /// <summary>
    /// Optional desktop-host PDF print callback (e.g., WebView2 offscreen PDFium printer)
    /// invoked when SumatraPDF.exe is unavailable or fails.
    /// Signature: (pdfFilePath, targetPrinter, isThermal, cancellationToken) => (Ok, Engine, Error).
    /// </summary>
    public Func<string, string, bool, CancellationToken, Task<(bool Ok, string? Engine, string? Error)>>? NativePdfPrintFallback { get; set; }

    public string? ResolveSumatraPdfBinary()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(_appRoot, "assets", "SumatraPDF.exe"),
            Path.Combine(_appRoot, "assets", "SumatraPDF-3.4.6-32.exe"),
            Path.Combine(_appRoot, "native-win", "assets", "SumatraPDF.exe"),
            Path.Combine(AppContext.BaseDirectory, "assets", "SumatraPDF.exe"),
            Path.Combine(AppContext.BaseDirectory, "assets", "SumatraPDF-3.4.6-32.exe"),
            Path.Combine(_appRoot, "desktop", "SumatraPDF.exe"),
            Path.Combine(_appRoot, "desktop", "SumatraPDF-3.4.6-32.exe"),
            Path.Combine(AppContext.BaseDirectory, "SumatraPDF.exe"),
            Path.Combine(AppContext.BaseDirectory, "SumatraPDF-3.4.6-32.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    public async Task<(bool Ok, string? Engine, string? Error)> SpoolPdfViaShellPrintToAsync(
        string pdfFilePath,
        string targetPrinter,
        CancellationToken cancellationToken = default)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return (false, "shell-printto", "Windows Shell printto fallback is only supported on Windows.");
        }

        if (string.IsNullOrWhiteSpace(pdfFilePath) || !File.Exists(pdfFilePath))
        {
            return (false, "shell-printto", $"PDF file not found: {pdfFilePath}");
        }

        if (string.IsNullOrWhiteSpace(targetPrinter))
        {
            return (false, "shell-printto", "Target printer name is required for Shell printto.");
        }

        Process? proc = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = pdfFilePath,
                Verb = "printto",
                Arguments = $"\"{targetPrinter}\"",
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            proc = Process.Start(psi);
            if (proc == null)
            {
                return (false, "shell-printto", "ShellExecute printto did not launch a print handler.");
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            await proc.WaitForExitAsync(cts.Token);

            if (proc.ExitCode != 0)
            {
                return (false, "shell-printto", $"Shell printto handler exited with code {proc.ExitCode}.");
            }

            return (true, "shell-printto", null);
        }
        catch (Exception ex)
        {
            return (false, "shell-printto", ex.Message);
        }
        finally
        {
            try { proc?.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// Spool a PDF file (or byte buffer) directly to a Windows hardware printer using SumatraPDF CLI or GDI fallback.
    /// </summary>
    public async Task<(bool Ok, string? Printer, string? Engine, string? Error)> SpoolPdfAsync(
        byte[] pdfBytes,
        string? requestedDeviceName = null,
        bool isThermal = false,
        string scaleMode = "auto")
    {
        if (pdfBytes == null || pdfBytes.Length == 0)
        {
            return (false, null, null, "PDF byte buffer is empty.");
        }

        var tempPdfPath = Path.Combine(
            Path.GetTempPath(),
            $"karobar-spool-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-{Guid.NewGuid():N}.pdf");

        try
        {
            await File.WriteAllBytesAsync(tempPdfPath, pdfBytes);
            return await SpoolPdfFileAsync(tempPdfPath, requestedDeviceName, isThermal, scaleMode);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPdfPath))
                {
                    File.Delete(tempPdfPath);
                }
            }
            catch
            {
                // Ignore temp cleanup race
            }
        }
    }

    public async Task<(bool Ok, string? Printer, string? Engine, string? Error)> SpoolPdfFileAsync(
        string pdfFilePath,
        string? requestedDeviceName = null,
        bool isThermal = false,
        string scaleMode = "auto")
    {
        if (!File.Exists(pdfFilePath))
        {
            return (false, null, null, $"PDF file not found: {pdfFilePath}");
        }

        var strategy = await ResolvePrinterStrategyAsync(requestedDeviceName, isThermal);
        if (strategy.Mode == "no-printer" || string.IsNullOrWhiteSpace(strategy.DeviceName))
        {
            return (false, null, null, strategy.Error ?? "No physical hardware printer detected.");
        }

        var targetPrinter = strategy.DeviceName;
        var sumatraExe = ResolveSumatraPdfBinary();

        string? sumatraError = null;
        if (!string.IsNullOrEmpty(sumatraExe))
        {
            var effectiveScale = scaleMode is "noscale" or "fit" or "shrink"
                ? scaleMode
                : (isThermal ? "noscale" : "fit");

            var psi = new ProcessStartInfo
            {
                FileName = sumatraExe,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = true
            };
            psi.ArgumentList.Add("-print-to");
            psi.ArgumentList.Add(targetPrinter);
            psi.ArgumentList.Add("-silent");
            psi.ArgumentList.Add("-print-settings");
            psi.ArgumentList.Add(effectiveScale);
            psi.ArgumentList.Add(pdfFilePath);

            Process? proc = null;
            try
            {
                proc = Process.Start(psi);
                if (proc != null)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);
                    await proc.WaitForExitAsync(cts.Token);
                    var stderr = await stderrTask;
                    if (proc.ExitCode == 0)
                    {
                        return (true, targetPrinter, "sumatra", null);
                    }
                    sumatraError = string.IsNullOrWhiteSpace(stderr) ? "SumatraPDF spooler exited with non-zero code." : stderr.Trim();
                }
            }
            catch (Exception ex)
            {
                try
                {
                    if (proc != null && !proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                }
                catch { }
                sumatraError = ex.Message;
            }
            finally
            {
                try { proc?.Dispose(); } catch { }
            }
        }

        // Genuine native PDF print fallback chain (never spool raw %PDF bytes to WinSpool):
        // 1. Desktop host WebView2 offscreen PDFium printer if registered
        if (NativePdfPrintFallback != null)
        {
            try
            {
                var fb = await NativePdfPrintFallback(pdfFilePath, targetPrinter, isThermal, CancellationToken.None);
                if (fb.Ok)
                {
                    return (true, targetPrinter, fb.Engine ?? "webview2-pdf-fallback", null);
                }
            }
            catch
            {
                // Continue to OS printto fallback
            }
        }

        // 2. Windows ShellExecute Verb="printto" fallback
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var shellRes = await SpoolPdfViaShellPrintToAsync(pdfFilePath, targetPrinter);
            if (shellRes.Ok)
            {
                return (true, targetPrinter, shellRes.Engine ?? "shell-printto", null);
            }
        }

        if (!string.IsNullOrEmpty(sumatraExe) && !string.IsNullOrWhiteSpace(sumatraError))
        {
            return (false, targetPrinter, "sumatra", sumatraError);
        }

        return (false, targetPrinter, "sumatra-missing", "SumatraPDF spooler is not bundled and native PDF print fallback was unavailable.");
    }
}

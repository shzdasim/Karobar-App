using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Karobar.Core;

/// <summary>
/// Single source of truth for deterministic hardware Machine ID generation, Vendor Master Public Key,
/// license key normalization, and HMAC row sealing — matching app/Services/LicenseService.php
/// and installer/precheck.php byte-for-byte across Windows, macOS, and Linux.
/// </summary>
public static class HardwareMachineIdProvider
{
    /// <summary>
    /// Pinned Vendor Master Public Key (Ed25519, 32 bytes in base64url).
    /// Matches App\Services\LicenseService::MASTER_PUBLIC_KEY.
    /// </summary>
    public const string MasterPublicKeyB64u = "Fo2gpFWlgBCUcUZ2hUfwCizcT8EwxsrTuFL10gNpC0w";

    /// <summary>
    /// SHA-256 digest of the raw 32-byte Master Public Key for runtime integrity verification.
    /// </summary>
    public const string MasterPublicKeySha256 = "b4a91e9a0cc6aa627bd035e7a5407e9c9caffec2c9218cf8cdeb2f9bac0b5b27";

    /// <summary>
    /// Canonical prefix for formatted Karobar license keys.
    /// </summary>
    public const string KeyPrefix = "KRBL.";

    public const long ClockSkewToleranceSeconds = 900;

    public static string B64uEncode(byte[] bin)
    {
        return Convert.ToBase64String(bin).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] B64uDecode(string txt)
    {
        if (string.IsNullOrWhiteSpace(txt)) return Array.Empty<byte>();
        var r = txt.Replace('-', '+').Replace('_', '/');
        var pad = r.Length % 4;
        if (pad > 0)
        {
            r += new string('=', 4 - pad);
        }
        try
        {
            return Convert.FromBase64String(r);
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    public static byte[] GetMasterPublicKeyBytes()
    {
        var raw = B64uDecode(MasterPublicKeyB64u);
        var shaHex = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        if (raw.Length != 32 || !string.Equals(shaHex, MasterPublicKeySha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Master license public key integrity check failed.");
        }
        return raw;
    }

    public static string NormalizeLicenseKey(string? licenseKey, bool withPrefix = false)
    {
        var clean = Regex.Replace(licenseKey ?? string.Empty, @"\s+", string.Empty);
        if (clean.StartsWith(KeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[KeyPrefix.Length..];
        }
        else if (clean.StartsWith("KRB-LIC-", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean[8..];
        }

        return withPrefix ? (KeyPrefix + clean) : clean;
    }

    public static string ComputeHmacHex(string data, string key)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Computes the deterministic hardware Machine ID (KRB-XXXX-XXXX-XXXX-XXXX) matching
    /// App\Services\LicenseService::computeMachineId() and installer/precheck.php byte-for-byte.
    /// </summary>
    public static string ComputeDeterministicMachineId()
    {
        var basis = ProbeHardwareBasis();
        var digest = ComputeHmacHex(basis, "KAROBAR-HWID-V2:" + MasterPublicKeyB64u).ToUpperInvariant();
        var hex16 = digest[..16];
        return $"KRB-{hex16[..4]}-{hex16.Substring(4, 4)}-{hex16.Substring(8, 4)}-{hex16.Substring(12, 4)}";
    }

    /// <summary>
    /// Produces the exact hardware basis string matching PHP's probeHardwareBasis():
    /// - Windows: "Windows|Windows NT|AMD64|win:{MachineGuid}:{ComputerHardwareId}"
    /// - macOS:   "Darwin|Darwin|{arch}|darwin:{IOPlatformUUID}:{IOPlatformSerialNumber}"
    /// - Linux:   "Linux|Linux|{arch}|linux:{machine-id}|"
    /// </summary>
    public static string ProbeHardwareBasis()
    {
        string osFamily;
        string unameS;
        string unameM;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            osFamily = "Windows";
            unameS = "Windows NT";
            unameM = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "AMD64",
                Architecture.Arm64 => "ARM64",
                Architecture.X86 => "i586",
                _ => "AMD64"
            };
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            osFamily = "Darwin";
            unameS = "Darwin";
            unameM = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X64 => "x86_64",
                _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
            };
        }
        else
        {
            osFamily = "Linux";
            unameS = "Linux";
            unameM = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x86_64",
                Architecture.Arm64 => "aarch64",
                _ => RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant()
            };
        }

        var hwToken = "";

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var outStr = RunProcessCapture("ioreg", "-rd1 -c IOPlatformExpertDevice");
                if (!string.IsNullOrEmpty(outStr))
                {
                    var m1 = Regex.Match(outStr, "\"IOPlatformUUID\"\\s*=\\s*\"([^\"]+)\"");
                    var m2 = Regex.Match(outStr, "\"IOPlatformSerialNumber\"\\s*=\\s*\"([^\"]+)\"");
                    var uuid = m1.Success ? m1.Groups[1].Value.Trim() : "";
                    var serial = m2.Success ? m2.Groups[1].Value.Trim() : "";
                    if (!string.IsNullOrEmpty(uuid) || !string.IsNullOrEmpty(serial))
                    {
                        hwToken = $"darwin:{uuid}:{serial}";
                    }
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var winParts = new List<string>();

                var out1 = RunProcessCapture("reg", "query \"HKLM\\SOFTWARE\\Microsoft\\Cryptography\" /v MachineGuid /reg:64");
                if (string.IsNullOrEmpty(out1) || !out1.Contains("MachineGuid", StringComparison.OrdinalIgnoreCase))
                {
                    out1 = RunProcessCapture("reg", "query \"HKLM\\SOFTWARE\\Microsoft\\Cryptography\" /v MachineGuid");
                }
                var m1 = Regex.Match(out1 ?? "", @"MachineGuid\s+REG_SZ\s+([^\r\n]+)", RegexOptions.IgnoreCase);
                if (m1.Success)
                {
                    winParts.Add(m1.Groups[1].Value.Trim());
                }

                var out2 = RunProcessCapture("reg", "query \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\SystemInformation\" /v ComputerHardwareId /reg:64");
                if (string.IsNullOrEmpty(out2) || !out2.Contains("ComputerHardwareId", StringComparison.OrdinalIgnoreCase))
                {
                    out2 = RunProcessCapture("reg", "query \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\SystemInformation\" /v ComputerHardwareId");
                }
                var m2 = Regex.Match(out2 ?? "", @"ComputerHardwareId\s+REG_SZ\s+([^\r\n]+)", RegexOptions.IgnoreCase);
                if (m2.Success)
                {
                    winParts.Add(m2.Groups[1].Value.Trim());
                }

                if (winParts.Count > 0)
                {
                    hwToken = "win:" + string.Join(":", winParts);
                }
            }
            else
            {
                foreach (var f in new[] { "/etc/host-machine-id", "/etc/machine-id", "/var/lib/dbus/machine-id", "/sys/class/dmi/id/product_uuid" })
                {
                    if (File.Exists(f))
                    {
                        var val = File.ReadAllText(f).Trim();
                        if (!string.IsNullOrEmpty(val))
                        {
                            hwToken += $"linux:{val}|";
                            if (f == "/etc/host-machine-id") break;
                        }
                    }
                }
            }
        }
        catch
        {
            // Fallback below
        }

        if (string.IsNullOrEmpty(hwToken))
        {
            hwToken = $"fallback:{Environment.MachineName}:0";
        }

        return $"{osFamily}|{unameS}|{unameM}|{hwToken}";
    }

    public static string ComputeRowSeal(string licenseKey, string machineId, long activatedTimestamp)
    {
        var normalizedKey = NormalizeLicenseKey(licenseKey, false);
        var normalizedMachine = (machineId ?? string.Empty).Trim().ToUpperInvariant();
        var data = $"{normalizedKey}|{normalizedMachine}|{activatedTimestamp}";
        var sealKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("KAROBAR-ROW-SEAL-V2:" + MasterPublicKeyB64u),
            Encoding.UTF8.GetBytes(normalizedMachine));

        var sig = HMACSHA256.HashData(sealKey, Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(sig).ToLowerInvariant();
    }

    public static long ParseUtcTimestampSeconds(object? val)
    {
        if (val == null) return 0;
        if (val is long l) return l;
        if (val is int i) return i;
        if (val is DateTime dt)
        {
            return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToUnixTimeSeconds();
        }
        var str = val.ToString();
        if (string.IsNullOrWhiteSpace(str)) return 0;
        if (long.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix)) return unix;
        if (DateTimeOffset.TryParse(
                str,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dto))
        {
            return dto.ToUnixTimeSeconds();
        }
        return 0;
    }

    private static string? RunProcessCapture(string fileName, string arguments)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            proc.Start();
            var output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(3000);
            return output;
        }
        catch
        {
            return null;
        }
    }
}

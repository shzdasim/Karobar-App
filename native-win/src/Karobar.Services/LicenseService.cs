using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Karobar.Services.Infrastructure;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Karobar.Services;

public class LicenseService
{
    public const string MASTER_PUBLIC_KEY = "Fo2gpFWlgBCUcUZ2hUfwCizcT8EwxsrTuFL10gNpC0w";
    private const string MASTER_PUBLIC_KEY_SHA256 = "b4a91e9a0cc6aa627bd035e7a5407e9c9caffec2c9218cf8cdeb2f9bac0b5b27";
    public const string KEY_PREFIX = "KRBL.";
    public const long CLOCK_SKEW_TOLERANCE = 900;
    public const long PERIODIC_CHECK_INTERVAL = 28800;

    public static readonly Dictionary<string, Dictionary<string, object>> MODULES = new(StringComparer.OrdinalIgnoreCase)
    {
        ["basic_operations"] = new()
        {
            ["key"] = "basic_operations",
            ["label"] = "Basic Store Operations",
            ["description"] = "Products, Batches, Sales, Purchases, Returns, Inventory, Ledgers, Accounts, Cash Drawer, Expenses & Standard Reports",
            ["required_tier"] = "basic"
        },
        ["barcode"] = new()
        {
            ["key"] = "barcode",
            ["label"] = "Barcode POS & Barcode Studio",
            ["description"] = "Fast Fullscreen Barcode Scanner POS & Custom Barcode Label Designer / Printer",
            ["required_tier"] = "gold"
        },
        ["fbr"] = new()
        {
            ["key"] = "fbr",
            ["label"] = "FBR Digital Invoicing & POS Tax",
            ["description"] = "Official FBR QR & Logo Receipt Stamp, PRAL Real-Time Sync, Offline Queue & Statutory Tax Return Report",
            ["required_tier"] = "gold"
        },
        ["server_terminals"] = new()
        {
            ["key"] = "server_terminals",
            ["label"] = "Server & Multi-PC Terminals",
            ["description"] = "Multi-Counter LAN SyncPulse, Attached Terminals Dashboard & 1-Click Server DB Import/Export",
            ["required_tier"] = "platinum"
        },
        ["accounting_gl"] = new()
        {
            ["key"] = "accounting_gl",
            ["label"] = "Double-Entry Accounting & GL",
            ["description"] = "Chart of Accounts, Journal Vouchers & Financial Statements",
            ["required_tier"] = "platinum"
        },
        ["hr_payroll"] = new()
        {
            ["key"] = "hr_payroll",
            ["label"] = "HR, Attendance & Payroll",
            ["description"] = "Employees, Attendance Tracking, Staff Advances, Sales Commissions & Monthly Payroll Runs",
            ["required_tier"] = "platinum"
        },
        ["cube_ai"] = new()
        {
            ["key"] = "cube_ai",
            ["label"] = "Cube AI Assistant & SQLite Migration",
            ["description"] = "Hybrid AI Co-Pilot, Automated Task Runner & Intelligent SQLite Database Import",
            ["required_tier"] = "platinum"
        }
    };

    public static readonly Dictionary<string, Dictionary<string, object>> TIERS = new(StringComparer.OrdinalIgnoreCase)
    {
        ["basic"] = new()
        {
            ["key"] = "basic",
            ["label"] = "Basic",
            ["edition"] = "Basic",
            ["rank"] = 1,
            ["default_seats"] = 1,
            ["tagline"] = "All basic store, inventory, billing, ledger & reporting operations (1 PC)",
            ["modules"] = new[] { "basic_operations" }
        },
        ["gold"] = new()
        {
            ["key"] = "gold",
            ["label"] = "Gold",
            ["edition"] = "Gold",
            ["rank"] = 2,
            ["default_seats"] = 1,
            ["tagline"] = "Everything in Basic + FBR Digital Invoicing & Barcode POS / Barcode Studio",
            ["modules"] = new[] { "basic_operations", "barcode", "fbr" }
        },
        ["platinum"] = new()
        {
            ["key"] = "platinum",
            ["label"] = "Platinum",
            ["edition"] = "Platinum",
            ["rank"] = 3,
            ["default_seats"] = 0,
            ["tagline"] = "Everything in Gold + Server & Multi-PC Terminals, GL Accounting, HR/Payroll & Cube AI",
            ["modules"] = new[]
            {
                "basic_operations", "barcode", "fbr",
                "server_terminals", "accounting_gl", "hr_payroll", "cube_ai"
            }
        }
    };

    private readonly KarobarDb _db;
    private static string? _cachedMachineId;
    private static (long VerifiedAt, Dictionary<string, object?> Status)? _cachedVerifiedStatus;
    private static string? _testPublicKey;
    private static string? _testMachineId;
    private static long? _testCurrentTime;

    public LicenseService(KarobarDb db)
    {
        _db = db;
    }

    public static void SetTestOverrides(string? publicKey = null, string? machineId = null, long? currentTime = null)
    {
        _testPublicKey = publicKey;
        _testMachineId = machineId;
        _testCurrentTime = currentTime;
        _cachedMachineId = null;
        _cachedVerifiedStatus = null;
    }

    public static string B64uEncode(byte[] bin)
    {
        return Convert.ToBase64String(bin).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static byte[] B64uDecode(string txt)
    {
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

    protected long CurrentTime()
    {
        return _testCurrentTime ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public byte[] GetPublicKey()
    {
        if (!string.IsNullOrEmpty(_testPublicKey))
        {
            return B64uDecode(_testPublicKey);
        }

        var raw = B64uDecode(MASTER_PUBLIC_KEY);
        var shaHex = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        if (raw.Length != 32 || !string.Equals(shaHex, MASTER_PUBLIC_KEY_SHA256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Master license public key integrity check failed.");
        }
        return raw;
    }

    public string NormalizeLicenseKey(string licenseKey, bool withPrefix = false)
    {
        var clean = Regex.Replace(licenseKey ?? "", @"\s+", "");
        if (clean.StartsWith(KEY_PREFIX, StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(KEY_PREFIX.Length);
        }
        else if (clean.StartsWith("KRB-LIC-", StringComparison.OrdinalIgnoreCase))
        {
            clean = clean.Substring(8);
        }

        return withPrefix ? (KEY_PREFIX + clean) : clean;
    }

    public string ComputeMachineId()
    {
        if (!string.IsNullOrEmpty(_testMachineId))
        {
            return _testMachineId;
        }

        if (!string.IsNullOrEmpty(_cachedMachineId))
        {
            return _cachedMachineId;
        }

        var cachePath = _db.StoragePath("app/.machine-id");
        var envelope = ComputeFastOsEnvelope();
        var envSig = ComputeHmacHex(envelope, "KAROBAR-ENV-SIG-V2:" + MASTER_PUBLIC_KEY);

        if (File.Exists(cachePath))
        {
            try
            {
                var raw = File.ReadAllText(cachePath);
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    root.TryGetProperty("id", out var idProp) &&
                    root.TryGetProperty("env_sig", out var envSigProp) &&
                    root.TryGetProperty("id_sig", out var idSigProp))
                {
                    var idVal = idProp.GetString() ?? "";
                    var storedEnvSig = envSigProp.GetString() ?? "";
                    var storedIdSig = idSigProp.GetString() ?? "";

                    if (!string.IsNullOrEmpty(idVal) &&
                        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(storedEnvSig), Encoding.UTF8.GetBytes(envSig)))
                    {
                        var expectedIdSig = ComputeHmacHex($"{idVal}|{envSig}", "KAROBAR-ID-SEAL-V2:" + MASTER_PUBLIC_KEY);
                        if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(storedIdSig), Encoding.UTF8.GetBytes(expectedIdSig)))
                        {
                            _cachedMachineId = idVal;
                            return _cachedMachineId;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        var basis = ProbeHardwareBasis();
        var digest = ComputeHmacHex(basis, "KAROBAR-HWID-V2:" + MASTER_PUBLIC_KEY).ToUpperInvariant();
        var hex16 = digest.Substring(0, 16);
        var formattedId = $"KRB-{hex16.Substring(0, 4)}-{hex16.Substring(4, 4)}-{hex16.Substring(8, 4)}-{hex16.Substring(12, 4)}";

        var idSig = ComputeHmacHex($"{formattedId}|{envSig}", "KAROBAR-ID-SEAL-V2:" + MASTER_PUBLIC_KEY);
        try
        {
            var dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(new
            {
                id = formattedId,
                env_sig = envSig,
                id_sig = idSig
            }));
        }
        catch
        {
        }

        _cachedMachineId = formattedId;
        return formattedId;
    }

    protected string ComputeFastOsEnvelope()
    {
        var basePath = _db.ProjectRoot.Replace('\\', '/').ToLowerInvariant();
        var osFamily = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Darwin" : "Linux";

        return $"{osFamily}|{RuntimeInformation.OSDescription}|{RuntimeInformation.ProcessArchitecture}|{Environment.MachineName}|{basePath}";
    }

    protected string ProbeHardwareBasis()
    {
        var osFamily = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "Darwin" : "Linux";
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
                var m1 = Regex.Match(out1 ?? "", @"MachineGuid\s+REG_SZ\s+([^\r\n]+)", RegexOptions.IgnoreCase);
                if (m1.Success) winParts.Add(m1.Groups[1].Value.Trim());

                var out2 = RunProcessCapture("reg", "query \"HKLM\\SYSTEM\\CurrentControlSet\\Control\\SystemInformation\" /v ComputerHardwareId /reg:64");
                var m2 = Regex.Match(out2 ?? "", @"ComputerHardwareId\s+REG_SZ\s+([^\r\n]+)", RegexOptions.IgnoreCase);
                if (m2.Success) winParts.Add(m2.Groups[1].Value.Trim());

                if (winParts.Count > 0)
                {
                    hwToken = "win:" + string.Join(":", winParts);
                }
            }
            else
            {
                foreach (var f in new[] { "/etc/host-machine-id", "/etc/machine-id", "/var/lib/dbus/machine-id" })
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
        }

        if (string.IsNullOrEmpty(hwToken))
        {
            hwToken = $"fallback:{Environment.MachineName}";
        }

        return $"{osFamily}|{RuntimeInformation.ProcessArchitecture}|{hwToken}";
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

    public string SignPayload(IDictionary<string, object?> payload, byte[] secretKeyRaw)
    {
        if (secretKeyRaw.Length != 64 && secretKeyRaw.Length != 32)
        {
            throw new ArgumentException("Ed25519 secret key must be 32 or 64 raw bytes.");
        }

        var seed = secretKeyRaw.Length == 64 ? secretKeyRaw.Take(32).ToArray() : secretKeyRaw;
        var payloadJson = JsonSerializer.Serialize(payload);
        var msgBytes = Encoding.UTF8.GetBytes(payloadJson);

        var privParams = new Ed25519PrivateKeyParameters(seed, 0);
        var signer = new Ed25519Signer();
        signer.Init(true, privParams);
        signer.BlockUpdate(msgBytes, 0, msgBytes.Length);
        var sig = signer.GenerateSignature();

        return KEY_PREFIX + B64uEncode(sig) + "." + B64uEncode(msgBytes);
    }

    public async Task<(bool Valid, Dictionary<string, object?>? Payload, string? Reason)> VerifyStringAsync(
        string licenseKey,
        string? machineId = null,
        bool requireMachineBinding = false,
        Dictionary<string, object?>? knownRecord = null)
    {
        var normalized = NormalizeLicenseKey(licenseKey, false);
        if (string.IsNullOrEmpty(normalized) || !normalized.Contains('.'))
        {
            return (false, null, "Malformed license");
        }

        var parts = normalized.Split('.', 3);
        if (parts.Length != 2)
        {
            return (false, null, "Malformed license");
        }

        var sig = B64uDecode(parts[0]);
        var payloadBytes = B64uDecode(parts[1]);
        if (sig.Length != 64 || payloadBytes.Length == 0)
        {
            return (false, null, "Malformed license");
        }

        Dictionary<string, object?>? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(Encoding.UTF8.GetString(payloadBytes));
            if (payload == null) return (false, null, "Invalid JSON payload");
        }
        catch
        {
            return (false, null, "Invalid JSON payload");
        }

        bool signatureOk;
        try
        {
            var pubKey = new Ed25519PublicKeyParameters(GetPublicKey(), 0);
            var verifier = new Ed25519Signer();
            verifier.Init(false, pubKey);
            verifier.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
            signatureOk = verifier.VerifySignature(sig);
        }
        catch
        {
            return (false, null, "Bad signature");
        }

        if (!signatureOk)
        {
            return (false, payload, "Bad signature");
        }

        var iss = GetStringProp(payload, "iss");
        if (!string.IsNullOrEmpty(iss) && iss is not ("Karobar" or "KarobarApp" or "YourCompany"))
        {
            return (false, payload, "Invalid license issuer");
        }

        var sub = GetStringProp(payload, "sub");
        if (!string.IsNullOrEmpty(sub) && sub is not ("KarobarApp" or "karobar-pos" or "YourApp"))
        {
            return (false, payload, "Invalid license product");
        }

        var now = CurrentTime();
        var highWaterMark = await GetHighWaterMarkTimestampAsync(knownRecord);
        if (highWaterMark > 0 && (now + CLOCK_SKEW_TOLERANCE) < highWaterMark)
        {
            return (false, payload, "System clock tampering detected. Please restore the correct date and time.");
        }

        var nbf = GetLongProp(payload, "nbf");
        if (nbf.HasValue && (now + CLOCK_SKEW_TOLERANCE) < nbf.Value)
        {
            return (false, payload, "Not yet valid");
        }

        var exp = GetLongProp(payload, "exp");
        if (exp.HasValue && exp.Value > 0 && now > exp.Value)
        {
            return (false, payload, "Expired");
        }

        var payloadMachine = GetStringProp(payload, "machine");
        if (requireMachineBinding && string.IsNullOrWhiteSpace(payloadMachine))
        {
            return (false, payload, "License key is not bound to a Machine ID");
        }

        if (!string.IsNullOrWhiteSpace(payloadMachine))
        {
            var actualMachine = !string.IsNullOrWhiteSpace(machineId) ? machineId : ComputeMachineId();
            if (!string.Equals(payloadMachine.Trim(), actualMachine.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return (false, payload, "Wrong machine");
            }
        }

        return (true, payload, null);
    }

    public async Task<(bool Ok, Dictionary<string, object?>? Payload, string? Reason)> ActivateLicenseKeyAsync(
        string licenseKey,
        bool requireMachineBinding = false)
    {
        var machineId = ComputeMachineId();
        var (ok, payload, reason) = await VerifyStringAsync(licenseKey, machineId, requireMachineBinding);
        if (!ok || payload == null)
        {
            return (false, payload, reason ?? "Invalid license");
        }

        var canonicalKey = NormalizeLicenseKey(licenseKey, true);
        var activatedTs = CurrentTime();
        var nowDt = DateTimeOffset.FromUnixTimeSeconds(activatedTs).UtcDateTime;
        var nowStr = nowDt.ToString("yyyy-MM-dd HH:mm:ss");

        var seal = ComputeRowSeal(canonicalKey, machineId, activatedTs);
        var storedPayload = new Dictionary<string, object?>(payload, StringComparer.OrdinalIgnoreCase)
        {
            ["_seal"] = seal
        };

        var expTs = GetLongProp(payload, "exp");
        var expiresAtStr = expTs.HasValue && expTs.Value > 0
            ? DateTimeOffset.FromUnixTimeSeconds(expTs.Value).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss")
            : null;

        await using var conn = await _db.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();

        await _db.ExecuteAsync("DELETE FROM licenses", null, conn, tx);
        var recId = await _db.InsertAsync("licenses", new Dictionary<string, object?>
        {
            ["license_key"] = canonicalKey,
            ["payload"] = JsonSerializer.Serialize(storedPayload),
            ["valid"] = 1,
            ["reason"] = null,
            ["machine_hash"] = machineId,
            ["activated_at"] = nowStr,
            ["expires_at"] = expiresAtStr,
            ["last_verified_at"] = nowStr
        }, conn, tx);

        await tx.CommitAsync();

        var rec = await _db.QuerySingleOrDefaultAsync("SELECT * FROM licenses WHERE id = @id", new { id = recId });
        await RecordMonotonicTimestampAsync(activatedTs, rec, true);

        var cleanPayload = SanitizePayload(payload);
        var tier = ResolveTier(cleanPayload);
        var tierMeta = TIERS.GetValueOrDefault(tier) ?? TIERS["platinum"];
        var modules = ResolveModules(cleanPayload);
        var seats = GetLongProp(cleanPayload, "seats") is long s ? (int)s : Convert.ToInt32(tierMeta["default_seats"]);

        var statusObj = new Dictionary<string, object?>
        {
            ["valid"] = true,
            ["reason"] = null,
            ["payload"] = cleanPayload,
            ["expires_at"] = expTs,
            ["tier"] = tier,
            ["tier_label"] = tierMeta["label"],
            ["modules"] = modules,
            ["seats"] = seats
        };

        WritePeriodicStatusCache(statusObj, activatedTs);
        return (true, cleanPayload, null);
    }

    public string ResolveTier(IDictionary<string, object?>? payload)
    {
        if (payload == null) return "basic";

        var raw = (GetStringProp(payload, "tier")
                   ?? GetStringProp(payload, "license_type")
                   ?? GetStringProp(payload, "Edition")
                   ?? GetStringProp(payload, "edition")
                   ?? GetStringProp(payload, "plan")
                   ?? "").Trim().ToLowerInvariant();

        if (raw is "basic" or "basic_pos" or "basic pos" or "starter" or "standard" or "silver" || raw.Contains("basic"))
        {
            return "basic";
        }
        if (raw is "gold" or "gold_edition" or "gold edition" or "fbr" or "fbr_edition" || raw.Contains("gold"))
        {
            return "gold";
        }
        return "platinum";
    }

    public List<string> ResolveModules(IDictionary<string, object?>? payload)
    {
        if (payload == null) return new List<string>();

        var tier = ResolveTier(payload);
        var tierMeta = TIERS.GetValueOrDefault(tier) ?? TIERS["platinum"];
        var baseMods = ((string[])tierMeta["modules"]).ToList();

        if (payload.TryGetValue("modules", out var modsObj) && modsObj != null)
        {
            if (modsObj is JsonElement je && je.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in je.EnumerateArray())
                {
                    var mk = (item.GetString() ?? "").Trim().ToLowerInvariant();
                    if (MODULES.ContainsKey(mk) && !baseMods.Contains(mk, StringComparer.OrdinalIgnoreCase))
                    {
                        baseMods.Add(mk);
                    }
                }
            }
            else if (modsObj is IEnumerable<object> enumerable)
            {
                foreach (var item in enumerable)
                {
                    var mk = (item?.ToString() ?? "").Trim().ToLowerInvariant();
                    if (MODULES.ContainsKey(mk) && !baseMods.Contains(mk, StringComparer.OrdinalIgnoreCase))
                    {
                        baseMods.Add(mk);
                    }
                }
            }
        }

        return baseMods;
    }

    public async Task<bool> HasModuleAsync(string moduleKey)
    {
        var st = await CurrentStatusAsync();
        if (st.GetValueOrDefault("valid") is not true) return false;
        if (st.GetValueOrDefault("modules") is IEnumerable<string> mods)
        {
            return mods.Contains(moduleKey, StringComparer.OrdinalIgnoreCase);
        }
        return false;
    }

    public async Task<bool> HasTierAsync(string minimumTier)
    {
        var st = await CurrentStatusAsync();
        if (st.GetValueOrDefault("valid") is not true) return false;

        var currentTier = st.GetValueOrDefault("tier")?.ToString() ?? "platinum";
        var currentRank = TIERS.TryGetValue(currentTier, out var cMeta) ? Convert.ToInt32(cMeta["rank"]) : 3;
        var reqRank = TIERS.TryGetValue(minimumTier.Trim().ToLowerInvariant(), out var rMeta) ? Convert.ToInt32(rMeta["rank"]) : 3;

        return currentRank >= reqRank;
    }

    public string ComputeRowSeal(string licenseKey, string machineId, long activatedTimestamp)
    {
        var normalizedKey = NormalizeLicenseKey(licenseKey, false);
        var normalizedMachine = machineId.Trim().ToUpperInvariant();
        var data = $"{normalizedKey}|{normalizedMachine}|{activatedTimestamp}";
        var sealKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes("KAROBAR-ROW-SEAL-V2:" + MASTER_PUBLIC_KEY),
            Encoding.UTF8.GetBytes(normalizedMachine));

        var sig = HMACSHA256.HashData(sealKey, Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(sig).ToLowerInvariant();
    }

    public bool VerifyRowSeal(Dictionary<string, object?> rec, string currentMachineId)
    {
        var machineHash = rec.GetValueOrDefault("machine_hash")?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(machineHash)) return false;

        if (!string.Equals(machineHash.Trim(), currentMachineId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = ExtractPayloadDict(rec.GetValueOrDefault("payload"));
        if (payload == null) return false;

        var storedSeal = GetStringProp(payload, "_seal");
        if (string.IsNullOrWhiteSpace(storedSeal)) return false;

        var activatedTs = ParseTimestampSeconds(rec.GetValueOrDefault("activated_at"));
        var licenseKey = rec.GetValueOrDefault("license_key")?.ToString() ?? "";
        var expectedSeal = ComputeRowSeal(licenseKey, currentMachineId, activatedTs);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSeal),
            Encoding.UTF8.GetBytes(storedSeal));
    }

    public static Dictionary<string, object?>? SanitizePayload(IDictionary<string, object?>? payload)
    {
        if (payload == null) return null;
        var copy = new Dictionary<string, object?>(payload, StringComparer.OrdinalIgnoreCase);
        copy.Remove("_seal");
        return copy;
    }

    public async Task<Dictionary<string, object?>> CurrentStatusAsync(bool force = false)
    {
        var now = CurrentTime();
        if (!force && _testCurrentTime == null)
        {
            var cached = ReadPeriodicStatusCache(now);
            if (cached != null)
            {
                return cached;
            }
        }

        if (!await _db.HasTableAsync("licenses"))
        {
            ClearPeriodicStatusCache();
            return BuildUnlicensedStatus("No license installed");
        }

        var rec = await _db.QuerySingleOrDefaultAsync("SELECT * FROM licenses ORDER BY id DESC LIMIT 1");
        if (rec == null)
        {
            ClearPeriodicStatusCache();
            return BuildUnlicensedStatus("No license installed");
        }

        var currentMachineId = ComputeMachineId();
        var licenseKey = rec.GetValueOrDefault("license_key")?.ToString() ?? "";
        var (ok, payload, reason) = await VerifyStringAsync(licenseKey, currentMachineId, false, rec);

        if (ok && !VerifyRowSeal(rec, currentMachineId))
        {
            ok = false;
            reason = "Wrong machine";
        }

        if (ok)
        {
            await RecordMonotonicTimestampAsync(now, rec);
        }

        var cleanPayload = SanitizePayload(payload);
        var tier = ok ? ResolveTier(cleanPayload) : null;
        var tierMeta = tier != null ? (TIERS.GetValueOrDefault(tier) ?? TIERS["platinum"]) : null;
        var modules = ok ? ResolveModules(cleanPayload) : new List<string>();
        var seats = ok
            ? (GetLongProp(cleanPayload, "seats") is long s ? (int)s : Convert.ToInt32(tierMeta?["default_seats"] ?? 1))
            : 0;

        var result = new Dictionary<string, object?>
        {
            ["valid"] = ok,
            ["reason"] = reason,
            ["payload"] = cleanPayload,
            ["expires_at"] = GetLongProp(cleanPayload, "exp"),
            ["tier"] = tier,
            ["tier_label"] = tierMeta?["label"],
            ["modules"] = modules,
            ["seats"] = seats
        };

        if (ok)
        {
            WritePeriodicStatusCache(result, now);
        }
        else
        {
            ClearPeriodicStatusCache();
        }

        return result;
    }

    public async Task<bool> ClearLicenseIfMachineChangedAsync()
    {
        if (!await _db.HasTableAsync("licenses")) return false;
        var rec = await _db.QuerySingleOrDefaultAsync("SELECT * FROM licenses ORDER BY id DESC LIMIT 1");
        if (rec == null) return false;

        var currentMachineId = ComputeMachineId();
        var licenseKey = rec.GetValueOrDefault("license_key")?.ToString() ?? "";
        var (ok, _, reason) = await VerifyStringAsync(licenseKey, currentMachineId, false, rec);

        if (ok && !VerifyRowSeal(rec, currentMachineId))
        {
            ok = false;
            reason = "Wrong machine";
        }

        if (!ok && reason == "Wrong machine")
        {
            var id = rec.GetValueOrDefault("id");
            if (id != null)
            {
                await _db.ExecuteAsync("DELETE FROM licenses WHERE id = @id", new { id });
            }
            ClearPeriodicStatusCache();
            return true;
        }

        return false;
    }

    public void ClearPeriodicStatusCache()
    {
        _cachedVerifiedStatus = null;
        try
        {
            var cachePath = _db.StoragePath("app/.license-verified");
            if (File.Exists(cachePath)) File.Delete(cachePath);
        }
        catch
        {
        }
    }

    private Dictionary<string, object?>? ReadPeriodicStatusCache(long now)
    {
        if (_cachedVerifiedStatus == null) return null;
        var (verifiedAt, status) = _cachedVerifiedStatus.Value;
        if (status.GetValueOrDefault("valid") is not true) return null;

        if ((now + CLOCK_SKEW_TOLERANCE) < verifiedAt || (now - verifiedAt) >= PERIODIC_CHECK_INTERVAL)
        {
            return null;
        }

        if (status.TryGetValue("expires_at", out var expObj) && expObj != null &&
            long.TryParse(expObj.ToString(), out var exp) && exp > 0 && now > exp)
        {
            return null;
        }

        return status;
    }

    private void WritePeriodicStatusCache(Dictionary<string, object?> status, long now)
    {
        _cachedVerifiedStatus = (now, status);
    }

    public async Task<long> GetHighWaterMarkTimestampAsync(Dictionary<string, object?>? knownRecord = null)
    {
        long maxTs = 0;

        try
        {
            var clockPath = _db.StoragePath("app/.license-clock");
            if (File.Exists(clockPath))
            {
                var raw = File.ReadAllText(clockPath);
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("ts", out var tsProp) &&
                    doc.RootElement.TryGetProperty("sig", out var sigProp))
                {
                    var ts = tsProp.GetInt64();
                    var sig = sigProp.GetString() ?? "";
                    var expectedSig = ComputeHmacHex(ts.ToString(), "KAROBAR-CLOCK-SEAL-V2:" + MASTER_PUBLIC_KEY);
                    if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expectedSig), Encoding.UTF8.GetBytes(sig)) && ts > maxTs)
                    {
                        maxTs = ts;
                    }
                }
            }
        }
        catch
        {
        }

        try
        {
            var rec = knownRecord ?? (await _db.HasTableAsync("licenses")
                ? await _db.QuerySingleOrDefaultAsync("SELECT * FROM licenses ORDER BY id DESC LIMIT 1")
                : null);

            if (rec != null)
            {
                var actTs = ParseTimestampSeconds(rec.GetValueOrDefault("activated_at"));
                if (actTs > maxTs) maxTs = actTs;

                var verTs = ParseTimestampSeconds(rec.GetValueOrDefault("last_verified_at"));
                if (verTs > maxTs) maxTs = verTs;
            }
        }
        catch
        {
        }

        return maxTs;
    }

    public async Task RecordMonotonicTimestampAsync(long timestamp, Dictionary<string, object?>? rec = null, bool force = false)
    {
        try
        {
            var clockPath = _db.StoragePath("app/.license-clock");
            long existingTs = 0;
            if (File.Exists(clockPath))
            {
                var raw = File.ReadAllText(clockPath);
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("ts", out var tsProp))
                {
                    existingTs = tsProp.GetInt64();
                }
            }

            if (force || (timestamp - existingTs) >= 60)
            {
                var newTs = Math.Max(timestamp, existingTs);
                var sig = ComputeHmacHex(newTs.ToString(), "KAROBAR-CLOCK-SEAL-V2:" + MASTER_PUBLIC_KEY);
                var dir = Path.GetDirectoryName(clockPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(clockPath, JsonSerializer.Serialize(new { ts = newTs, sig }));
            }
        }
        catch
        {
        }

        if (rec != null && rec.TryGetValue("id", out var idObj) && idObj != null)
        {
            var lastVerifiedTs = ParseTimestampSeconds(rec.GetValueOrDefault("last_verified_at"));
            if (force || (timestamp - lastVerifiedTs) >= 300)
            {
                try
                {
                    var newDt = DateTimeOffset.FromUnixTimeSeconds(Math.Max(timestamp, lastVerifiedTs)).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss");
                    await _db.ExecuteAsync("UPDATE licenses SET last_verified_at = @dt WHERE id = @id", new { dt = newDt, id = idObj });
                }
                catch
                {
                }
            }
        }
    }

    private static Dictionary<string, object?> BuildUnlicensedStatus(string reason)
    {
        return new Dictionary<string, object?>
        {
            ["valid"] = false,
            ["reason"] = reason,
            ["tier"] = null,
            ["tier_label"] = null,
            ["modules"] = Array.Empty<string>(),
            ["seats"] = 0
        };
    }

    private static string ComputeHmacHex(string data, string key)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static Dictionary<string, object?>? ExtractPayloadDict(object? raw)
    {
        if (raw is Dictionary<string, object?> d) return d;
        if (raw is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText());
        }
        if (raw is string s && !string.IsNullOrWhiteSpace(s))
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, object?>>(s);
            }
            catch
            {
            }
        }
        return null;
    }

    private static string? GetStringProp(IDictionary<string, object?>? dict, string key)
    {
        if (dict == null || !dict.TryGetValue(key, out var val) || val == null) return null;
        if (val is JsonElement je)
        {
            return je.ValueKind == JsonValueKind.String ? je.GetString() : je.GetRawText();
        }
        return val.ToString();
    }

    private static long? GetLongProp(IDictionary<string, object?>? dict, string key)
    {
        if (dict == null || !dict.TryGetValue(key, out var val) || val == null) return null;
        if (val is long l) return l;
        if (val is int i) return i;
        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out var jl)) return jl;
            if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out var jsl)) return jsl;
            return null;
        }
        return long.TryParse(val.ToString(), out var parsed) ? parsed : null;
    }

    private static long ParseTimestampSeconds(object? val)
    {
        if (val == null) return 0;
        if (val is long l) return l;
        if (val is DateTime dt) return new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc)).ToUnixTimeSeconds();
        if (DateTimeOffset.TryParse(val.ToString(), out var dto)) return dto.ToUnixTimeSeconds();
        return 0;
    }
}

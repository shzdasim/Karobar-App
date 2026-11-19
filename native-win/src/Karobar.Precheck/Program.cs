using System.Text;
using System.Text.Json;
using Karobar.Core;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Karobar.Precheck;

/// <summary>
/// Tiny standalone pre-install CLI helper bundled in Inno Setup {tmp} (`dontcopy`) so that
/// Karobar-Native.exe in {app} can stay split-file for ~3–5 MB differential OTA patches while
/// pre-install Machine ID detection and Ed25519 license verification execute in &lt;50ms standalone.
/// </summary>
public static class Program
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static int Main(string[] args)
    {
        var cliArgs = StripCliPrefix(args);
        if (cliArgs.Length == 0)
        {
            return 1;
        }

        var cmd = cliArgs[0].Trim().ToLowerInvariant();
        var opts = ParseOptions(cliArgs.Skip(1).ToArray());

        return cmd switch
        {
            "license:machine-id" => RunMachineId(opts),
            "license:verify-installer" => RunVerifyInstaller(opts),
            _ => 1
        };
    }

    private static string[] StripCliPrefix(string[] args)
    {
        if (args.Length >= 2 && (args[0].Equals("--cli", StringComparison.OrdinalIgnoreCase) || args[0].Equals("artisan", StringComparison.OrdinalIgnoreCase)))
        {
            return args[1..];
        }
        if (args.Length >= 1 && args[0].StartsWith("--cli=", StringComparison.OrdinalIgnoreCase))
        {
            return [args[0]["--cli=".Length..], .. args[1..]];
        }
        return args;
    }

    private static Dictionary<string, string?> ParseOptions(string[] rawArgs)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rawArgs.Length; i++)
        {
            var token = rawArgs[i]?.Trim() ?? "";
            if (!token.StartsWith("--", StringComparison.Ordinal)) continue;
            var body = token[2..];
            var eq = body.IndexOf('=');
            if (eq > 0)
            {
                dict[body[..eq].Trim()] = body[(eq + 1)..].Trim().Trim('"');
            }
            else if (i + 1 < rawArgs.Length && !rawArgs[i + 1].StartsWith("-", StringComparison.Ordinal))
            {
                dict[body.Trim()] = rawArgs[++i].Trim().Trim('"');
            }
            else
            {
                dict[body.Trim()] = "true";
            }
        }
        return dict;
    }

    private static int RunMachineId(Dictionary<string, string?> opts)
    {
        var machineId = HardwareMachineIdProvider.ComputeDeterministicMachineId();
        if (opts.TryGetValue("out", out var outFile) && !string.IsNullOrWhiteSpace(outFile))
        {
            var dir = Path.GetDirectoryName(outFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(outFile, machineId, Utf8NoBom);
        }
        Console.WriteLine(machineId);
        return 0;
    }

    private static int RunVerifyInstaller(Dictionary<string, string?> opts)
    {
        var errorOut = opts.TryGetValue("error-out", out var eo) ? (eo ?? "").Trim() : "";
        var machineId = HardwareMachineIdProvider.ComputeDeterministicMachineId();
        var allowUnbound = opts.TryGetValue("allow-unbound", out var au) &&
                           (au?.Trim().ToLowerInvariant() is "1" or "true" or "yes");

        int Fail(string msg)
        {
            if (!string.IsNullOrWhiteSpace(errorOut))
            {
                try
                {
                    var dir = Path.GetDirectoryName(errorOut);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    File.WriteAllText(errorOut, msg, Utf8NoBom);
                }
                catch { }
            }
            Console.Error.WriteLine(msg);
            return 1;
        }

        var key = opts.TryGetValue("key", out var k) ? (k ?? "").Trim('\uFEFF', ' ', '\t', '\r', '\n') : "";
        if (string.IsNullOrEmpty(key) && opts.TryGetValue("key-file", out var kf) && !string.IsNullOrWhiteSpace(kf) && File.Exists(kf))
        {
            key = File.ReadAllText(kf, Encoding.UTF8).Trim('\uFEFF', ' ', '\t', '\r', '\n');
        }

        if (string.IsNullOrWhiteSpace(key)) return Fail("Please enter a valid Karobar license key.");

        var normalized = HardwareMachineIdProvider.NormalizeLicenseKey(key, withPrefix: false);
        var parts = normalized.Split('.', 3);
        if (parts.Length != 2) return Fail("Invalid or corrupted license key format.");

        var sigBytes = HardwareMachineIdProvider.B64uDecode(parts[0]);
        var payloadBytes = HardwareMachineIdProvider.B64uDecode(parts[1]);
        if (sigBytes.Length != 64 || payloadBytes.Length == 0)
        {
            return Fail("Invalid cryptographic signature or payload encoding.");
        }

        Dictionary<string, object?>? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Dictionary<string, object?>>(Encoding.UTF8.GetString(payloadBytes));
        }
        catch
        {
            payload = null;
        }
        if (payload == null) return Fail("Invalid JSON payload in license key.");

        bool sigOk;
        try
        {
            var pubBytes = HardwareMachineIdProvider.GetMasterPublicKeyBytes();
            var verifier = new Ed25519Signer();
            verifier.Init(false, new Ed25519PublicKeyParameters(pubBytes, 0));
            verifier.BlockUpdate(payloadBytes, 0, payloadBytes.Length);
            sigOk = verifier.VerifySignature(sigBytes);
        }
        catch
        {
            sigOk = false;
        }

        if (!sigOk) return Fail("License signature verification failed. This license key is invalid or has been altered.");

        var iss = GetString(payload, "iss");
        if (!string.IsNullOrEmpty(iss) && iss is not ("Karobar" or "KarobarApp" or "YourCompany"))
            return Fail("Invalid license issuer.");

        var sub = GetString(payload, "sub");
        if (!string.IsNullOrEmpty(sub) && sub is not ("KarobarApp" or "karobar-pos" or "YourApp"))
            return Fail("Invalid license product.");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (TryGetLong(payload, "nbf", out var nbf) && (now + HardwareMachineIdProvider.ClockSkewToleranceSeconds) < nbf)
            return Fail("This license key is not yet valid (check your system date and time).");

        if (TryGetLong(payload, "exp", out var exp) && exp > 0 && now > exp)
            return Fail("This license key has expired. Please contact support for a renewal key.");

        var payloadMachine = GetString(payload, "machine");
        if (string.IsNullOrWhiteSpace(payloadMachine))
        {
            if (!allowUnbound)
            {
                return Fail($"This license key is not bound to a Machine ID. Please request a machine-bound license key for Machine ID: {machineId}");
            }
        }
        else if (!string.Equals(payloadMachine.Trim(), machineId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return Fail($"This license key is bound to another computer ({payloadMachine}) and cannot be used on this machine (Your Machine ID: {machineId}).");
        }

        var canonicalKey = HardwareMachineIdProvider.NormalizeLicenseKey(key, withPrefix: true);
        var sealData = new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["license_key"] = canonicalKey,
            ["machine_id"] = machineId,
            ["verified_at"] = now,
            ["seal"] = HardwareMachineIdProvider.ComputeRowSeal(canonicalKey, machineId, now),
            ["payload"] = payload
        };

        if (opts.TryGetValue("seal-out", out var sealOut) && !string.IsNullOrWhiteSpace(sealOut))
        {
            var dir = Path.GetDirectoryName(sealOut);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(sealOut, JsonSerializer.Serialize(sealData, new JsonSerializerOptions { WriteIndented = true }), Utf8NoBom);
        }

        if (!string.IsNullOrWhiteSpace(errorOut) && File.Exists(errorOut))
        {
            try { File.Delete(errorOut); } catch { }
        }

        return 0;
    }

    private static string? GetString(Dictionary<string, object?> dict, string key)
    {
        if (!dict.TryGetValue(key, out var val) || val == null) return null;
        if (val is JsonElement je)
        {
            return je.ValueKind == JsonValueKind.String ? je.GetString() : (je.ValueKind == JsonValueKind.Null ? null : je.ToString());
        }
        return val.ToString();
    }

    private static bool TryGetLong(Dictionary<string, object?> dict, string key, out long result)
    {
        result = 0;
        if (!dict.TryGetValue(key, out var val) || val == null) return false;
        if (val is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt64(out result)) return true;
            if (je.ValueKind == JsonValueKind.String && long.TryParse(je.GetString(), out result)) return true;
            return false;
        }
        return long.TryParse(val.ToString(), out result);
    }
}

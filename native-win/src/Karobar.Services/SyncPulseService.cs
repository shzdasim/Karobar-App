using System.Collections.Concurrent;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Karobar.Services;

public class SyncPulseService
{
    private readonly KarobarDb _db;

    private static readonly ConcurrentDictionary<string, long> Versions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stock_version"] = 1,
        ["drafts_version"] = 1,
        ["drawer_version"] = 1,
        ["terminals_version"] = 1,
        ["settings_version"] = 1,
        ["updated_at_ms"] = 1
    };

    private static DateTime _lastCountCheckUtc = DateTime.MinValue;
    private static int _cachedOnlineTerminals = 1;
    private static int _cachedOpenDrafts = 0;

    public SyncPulseService(KarobarDb db)
    {
        _db = db;
    }

    public static Dictionary<string, long> Bump(params string[] domains)
    {
        if (domains == null || domains.Length == 0)
        {
            domains = new[] { "stock" };
        }

        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var domain in domains)
        {
            if (string.IsNullOrWhiteSpace(domain)) continue;
            var key = domain.EndsWith("_version", StringComparison.OrdinalIgnoreCase)
                ? domain
                : $"{domain}_version";

            Versions.AddOrUpdate(key, nowMs, (_, prev) => Math.Max(prev + 1, nowMs));
        }

        Versions["updated_at_ms"] = nowMs;
        return GetVersions();
    }

    public static Dictionary<string, long> GetVersions()
    {
        return new Dictionary<string, long>(Versions, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<Dictionary<string, object?>> GetPulsePayloadAsync(HttpContext? httpContext = null)
    {
        var versions = GetVersions();

        if ((DateTime.UtcNow - _lastCountCheckUtc).TotalSeconds >= 3)
        {
            _lastCountCheckUtc = DateTime.UtcNow;
            var onlineTerminals = 0;
            var openDrafts = 0;

            try
            {
                if (await _db.HasTableAsync("terminals"))
                {
                    var cutoff = DateTime.UtcNow.AddSeconds(-25).ToString("yyyy-MM-dd HH:mm:ss");
                    onlineTerminals = await _db.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM terminals WHERE is_authorized = 1 AND last_seen_at >= @cutoff",
                        new { cutoff });
                }
            }
            catch
            {
            }

            try
            {
                if (await _db.HasTableAsync("invoice_drafts"))
                {
                    openDrafts = await _db.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM invoice_drafts WHERE status = 'draft'");
                }
            }
            catch
            {
            }

            _cachedOnlineTerminals = Math.Max(1, onlineTerminals);
            _cachedOpenDrafts = openDrafts;
        }

        Dictionary<string, object?>? currentTerminal = null;
        if (httpContext != null && httpContext.Items.TryGetValue("KarobarTerminal", out var termObj) && termObj is Dictionary<string, object?> termDict)
        {
            currentTerminal = new Dictionary<string, object?>
            {
                ["id"] = termDict.GetValueOrDefault("id"),
                ["terminal_uid"] = termDict.GetValueOrDefault("terminal_uid"),
                ["name"] = termDict.GetValueOrDefault("name"),
                ["mode"] = termDict.GetValueOrDefault("mode"),
                ["is_authorized"] = termDict.GetValueOrDefault("is_authorized") is true or 1 or 1L
            };
        }

        return new Dictionary<string, object?>
        {
            ["ok"] = true,
            ["server_time"] = DateTime.UtcNow.ToString("O"),
            ["versions"] = versions,
            ["online_terminals_count"] = _cachedOnlineTerminals,
            ["open_held_bills_count"] = _cachedOpenDrafts,
            ["terminal"] = currentTerminal
        };
    }
}

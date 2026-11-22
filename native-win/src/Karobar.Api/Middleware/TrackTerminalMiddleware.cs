using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Karobar.Api.Middleware;

public class TrackTerminalMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly ConcurrentDictionary<long, DateTime> HeartbeatThrottle = new();
    private static readonly int[] BusyRetryDelaysMs = [50, 150, 350];

    public TrackTerminalMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, KarobarDb db, LicenseService licenseService, ILogger<TrackTerminalMiddleware> logger)
    {
        var terminalUid = (context.Request.Headers["X-Terminal-Id"].FirstOrDefault()
                           ?? context.Request.Query["terminal_uid"].FirstOrDefault()
                           ?? "").Trim();

        if (!string.IsNullOrEmpty(terminalUid))
        {
            var path = context.Request.Path.Value ?? "";
            var normalizedPath = path.TrimEnd('/');
            var isHandshakePath = normalizedPath.Equals("/api/terminals/ping", StringComparison.OrdinalIgnoreCase) ||
                                  normalizedPath.Equals("/api/terminals/handshake", StringComparison.OrdinalIgnoreCase) ||
                                  normalizedPath.Equals("/api/login", StringComparison.OrdinalIgnoreCase) ||
                                  normalizedPath.Equals("/api/logout", StringComparison.OrdinalIgnoreCase);

            var terminalName = (context.Request.Headers["X-Terminal-Name"].FirstOrDefault()
                                ?? context.Request.Query["terminal_name"].FirstOrDefault()
                                ?? "").Trim();
            var hostname = (context.Request.Headers["X-Terminal-Hostname"].FirstOrDefault()
                            ?? context.Request.Query["hostname"].FirstOrDefault()
                            ?? "").Trim();
            var osPlatform = (context.Request.Headers["X-Terminal-OS"].FirstOrDefault()
                              ?? context.Request.Query["os_platform"].FirstOrDefault()
                              ?? "").Trim();
            var appVersion = (context.Request.Headers["X-Terminal-Version"].FirstOrDefault()
                              ?? context.Request.Query["app_version"].FirstOrDefault()
                              ?? "").Trim();
            var mode = (context.Request.Headers["X-Terminal-Mode"].FirstOrDefault()
                        ?? context.Request.Query["mode"].FirstOrDefault()
                        ?? "terminal").Trim();

            try
            {
                var hasTerminalsTable = await ExecuteWithBusyRetryAsync(
                    async () => await db.ExecuteScalarAsync<int>(
                        "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name = 'terminals'") > 0,
                    logger,
                    terminalUid);

                if (hasTerminalsTable)
                {
                    var terminal = await ExecuteWithBusyRetryAsync(
                        () => db.QuerySingleOrDefaultAsync(
                            "SELECT * FROM terminals WHERE terminal_uid = @uid",
                            new { uid = terminalUid }),
                        logger,
                        terminalUid);

                    long? userId = context.Items.TryGetValue("KarobarUserId", out var uidObj) && uidObj is long u ? u : null;
                    var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";

                    if (terminal == null)
                    {
                        var setting = await ExecuteWithBusyRetryAsync(
                            async () => await db.ExecuteScalarAsync<int>(
                                "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name = 'settings'") > 0
                                ? await db.QuerySingleOrDefaultAsync("SELECT * FROM settings ORDER BY id ASC LIMIT 1")
                                : null,
                            logger,
                            terminalUid);

                        var providedToken = (context.Request.Headers["X-Network-Token"].FirstOrDefault()
                                             ?? context.Request.Headers["X-Terminal-Token"].FirstOrDefault()
                                             ?? context.Request.Query["network_token"].FirstOrDefault()
                                             ?? "").Trim();
                        var storedToken = (setting?.GetValueOrDefault("network_server_token")?.ToString() ?? "").Trim();

                        var hasValidToken = !string.IsNullOrEmpty(storedToken) &&
                                            !string.IsNullOrEmpty(providedToken) &&
                                            CryptographicOperations.FixedTimeEquals(
                                                Encoding.UTF8.GetBytes(storedToken),
                                                Encoding.UTF8.GetBytes(providedToken));

                        if (userId == null && !hasValidToken)
                        {
                            await _next(context);
                            return;
                        }

                        bool autoAuthorize = true;
                        if (setting != null && setting.TryGetValue("terminal_auto_authorize", out var aaObj) && aaObj != null)
                        {
                            autoAuthorize = aaObj is true or 1 or 1L ||
                                            (aaObj is string s && (s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase)));
                        }

                        var remoteIp = context.Connection.RemoteIpAddress;
                        var isLoopback = remoteIp == null || System.Net.IPAddress.IsLoopback(remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp);
                        if (!isLoopback && !string.IsNullOrEmpty(storedToken) && !hasValidToken)
                        {
                            autoAuthorize = false;
                        }

                        if (autoAuthorize && !await HasAvailableLicenseSeatAsync(db, licenseService, terminalUid, logger))
                        {
                            autoAuthorize = false;
                        }

                        var count = (await ExecuteWithBusyRetryAsync(
                            () => db.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM terminals"),
                            logger,
                            terminalUid)) + 1;
                        var defaultName = !string.IsNullOrEmpty(terminalName)
                            ? terminalName
                            : (!string.IsNullOrEmpty(hostname) ? $"Counter ({hostname})" : $"Counter #{count}");

                        var validMode = mode is "standalone" or "server" or "terminal" or "browser" ? mode : "terminal";
                        var nowStr = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");

                        await ExecuteWithBusyRetryAsync(
                            () => db.ExecuteAsync(@"
                                INSERT OR IGNORE INTO terminals
                                    (terminal_uid, name, hostname, ip_address, os_platform, app_version, mode, current_user_id, is_authorized, last_seen_at, created_at, updated_at)
                                VALUES
                                    (@terminal_uid, @name, @hostname, @ip_address, @os_platform, @app_version, @mode, @current_user_id, @is_authorized, @last_seen_at, @nowStr, @nowStr)",
                                new
                                {
                                    terminal_uid = terminalUid,
                                    name = defaultName,
                                    hostname = string.IsNullOrEmpty(hostname) ? null : hostname,
                                    ip_address = ipAddress,
                                    os_platform = string.IsNullOrEmpty(osPlatform) ? null : osPlatform,
                                    app_version = string.IsNullOrEmpty(appVersion) ? null : appVersion,
                                    mode = validMode,
                                    current_user_id = userId,
                                    is_authorized = autoAuthorize ? 1 : 0,
                                    last_seen_at = nowStr,
                                    nowStr
                                }),
                            logger,
                            terminalUid);

                        terminal = await ExecuteWithBusyRetryAsync(
                            () => db.QuerySingleOrDefaultAsync("SELECT * FROM terminals WHERE terminal_uid = @uid", new { uid = terminalUid }),
                            logger,
                            terminalUid);
                    }
                    else
                    {
                        var termId = Convert.ToInt64(terminal["id"]);
                        var now = DateTime.UtcNow;
                        bool needsUpdate = !HeartbeatThrottle.TryGetValue(termId, out var lastBeat) ||
                                           (now - lastBeat).TotalSeconds >= 8 ||
                                           (userId.HasValue && Convert.ToInt64(terminal.GetValueOrDefault("current_user_id") ?? 0) != userId.Value);

                        if (needsUpdate)
                        {
                            var updates = new Dictionary<string, object?>
                            {
                                ["last_seen_at"] = now.ToString("yyyy-MM-dd HH:mm:ss"),
                                ["ip_address"] = ipAddress
                            };
                            if (userId.HasValue) updates["current_user_id"] = userId.Value;
                            if (!string.IsNullOrEmpty(hostname)) updates["hostname"] = hostname;
                            if (!string.IsNullOrEmpty(osPlatform)) updates["os_platform"] = osPlatform;
                            if (!string.IsNullOrEmpty(appVersion)) updates["app_version"] = appVersion;
                            if (mode is "standalone" or "server" or "terminal" or "browser") updates["mode"] = mode;

                            await ExecuteWithBusyRetryAsync(
                                () => db.UpdateAsync("terminals", termId, updates),
                                logger,
                                terminalUid);
                            HeartbeatThrottle[termId] = now;
                        }
                    }

                    if (terminal != null)
                    {
                        context.Items["KarobarTerminal"] = terminal;

                        var isAuth = terminal.GetValueOrDefault("is_authorized") is true or 1 or 1L;
                        if (!isAuth && !isHandshakePath)
                        {
                            var tName = terminal.GetValueOrDefault("name")?.ToString() ?? "Terminal";
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                message = $"Terminal '{tName}' is not authorized or has been blocked by the Server Administrator.",
                                code = "TERMINAL_UNAUTHORIZED",
                                terminal_uid = terminalUid,
                                terminal_name = tName
                            });
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Terminal verification failed for {TerminalUid} on {Method} {Path}", terminalUid, context.Request.Method, path);
                if (!isHandshakePath && !context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        message = "Terminal authorization check temporarily unavailable. Please retry.",
                        code = "TERMINAL_CHECK_UNAVAILABLE",
                        terminal_uid = terminalUid
                    });
                    return;
                }
            }
        }

        await _next(context);

        try
        {
            var method = context.Request.Method;
            if (method is "POST" or "PUT" or "PATCH" or "DELETE" &&
                context.Response.StatusCode is >= 200 and < 400)
            {
                var path = context.Request.Path.Value ?? "";
                var domains = new List<string>();

                if (Regex.IsMatch(path, @"(sale-invoices|purchase-invoices|sale-returns|purchase-returns|products|batches|stock-adjustments|stock-disposals|stocktake|user-demands)", RegexOptions.IgnoreCase))
                {
                    domains.Add("stock");
                    domains.Add("drawer");
                    domains.Add("drafts");
                }
                if (path.Contains("invoice-drafts", StringComparison.OrdinalIgnoreCase))
                {
                    domains.Add("drafts");
                }
                if (Regex.IsMatch(path, @"(cash-drawer|payments|expenses|accounts)", RegexOptions.IgnoreCase))
                {
                    domains.Add("drawer");
                }
                if (path.Contains("terminals", StringComparison.OrdinalIgnoreCase) && !path.Contains("terminals/ping", StringComparison.OrdinalIgnoreCase))
                {
                    domains.Add("terminals");
                }
                if (path.Contains("settings", StringComparison.OrdinalIgnoreCase))
                {
                    domains.Add("settings");
                }

                if (domains.Count > 0)
                {
                    SyncPulseService.Bump(domains.Distinct().ToArray());
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SyncPulseService.Bump failed after {Method} {Path}", context.Request.Method, context.Request.Path.Value);
        }
    }

    private static async Task<T> ExecuteWithBusyRetryAsync<T>(
        Func<Task<T>> operation,
        ILogger<TrackTerminalMiddleware> logger,
        string terminalUid)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await operation();
            }
            catch (SqliteException ex) when ((ex.SqliteErrorCode is 5 or 6) && attempt < BusyRetryDelaysMs.Length)
            {
                var delay = BusyRetryDelaysMs[attempt] + Random.Shared.Next(0, 25);
                logger.LogWarning(ex, "SQLite busy/locked (code {ErrorCode}) during terminal check for {TerminalUid}; retrying attempt {Attempt} in {DelayMs}ms.",
                    ex.SqliteErrorCode, terminalUid, attempt + 1, delay);
                await Task.Delay(delay);
            }
        }
    }

    public static async Task<bool> HasAvailableLicenseSeatAsync(
        KarobarDb db,
        LicenseService licenseSvc,
        string incomingUid,
        ILogger<TrackTerminalMiddleware> logger)
    {
        try
        {
            var status = await ExecuteWithBusyRetryAsync(
                () => licenseSvc.CurrentStatusAsync(),
                logger,
                incomingUid);
            int seats;
            if (status.GetValueOrDefault("valid") is true &&
                !await ExecuteWithBusyRetryAsync(
                    () => licenseSvc.HasModuleAsync("server_terminals"),
                    logger,
                    incomingUid))
            {
                seats = 1;
            }
            else
            {
                seats = Convert.ToInt32(status.GetValueOrDefault("seats") ?? 0);
                if (seats <= 0) return true;
            }

            var cutoff = DateTime.UtcNow.AddMinutes(-15).ToString("yyyy-MM-dd HH:mm:ss");
            var activeCount = await ExecuteWithBusyRetryAsync(
                () => db.ExecuteScalarAsync<int>(
                    "SELECT COUNT(1) FROM terminals WHERE is_authorized = 1 AND terminal_uid != @uid AND last_seen_at >= @cutoff",
                    new { uid = incomingUid, cutoff }),
                logger,
                incomingUid);

            return activeCount < seats;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to evaluate available license seat count for terminal {TerminalUid}; failing closed (denying auto-authorization).", incomingUid);
            return false;
        }
    }
}

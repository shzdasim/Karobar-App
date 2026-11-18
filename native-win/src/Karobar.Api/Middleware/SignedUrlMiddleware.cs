using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Karobar.Api.Middleware;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class RequireSignedUrlAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!SignedUrlMiddleware.ValidateSignedRequest(context.HttpContext.Request))
        {
            context.Result = new JsonResult(new { message = "Invalid or expired signature." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}

public class SignedUrlMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly string SigningKey =
        Environment.GetEnvironmentVariable("APP_KEY")
        ?? "karobar-native-win-signed-url-secret-key-2026";

    private static readonly string[] SignedRoutePrefixes =
    {
        "/print/sale-invoices/",
        "/print/cash-drawer/",
        "/supplier-ledger/print",
        "/customer-ledger/print",
        "/print/shared-pdf/"
    };

    public SignedUrlMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (SignedRoutePrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            if (!ValidateSignedRequest(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message = "Invalid or expired signature." });
                return;
            }
        }

        await _next(context);
    }

    public static string CreateSignedUrl(HttpRequest request, string relativePath, IDictionary<string, string?>? queryParams = null, TimeSpan? ttl = null)
    {
        var scheme = string.IsNullOrWhiteSpace(request.Scheme) ? "http" : request.Scheme;
        var host = request.Host.HasValue ? request.Host.Value : "127.0.0.1:8000";
        var cleanPath = relativePath.StartsWith("/") ? relativePath : "/" + relativePath;

        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (queryParams != null)
        {
            foreach (var kv in queryParams)
            {
                if (kv.Value != null)
                {
                    parameters[kv.Key] = kv.Value;
                }
            }
        }

        var expires = DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(30)).ToUnixTimeSeconds();
        parameters["expires"] = expires.ToString();

        var queryWithoutSig = string.Join("&", parameters.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var baseUrlWithParams = $"{scheme}://{host}{cleanPath}?{queryWithoutSig}";
        var signature = ComputeHmacSha256(baseUrlWithParams);

        return $"{baseUrlWithParams}&signature={signature}";
    }

    public static bool ValidateSignedRequest(HttpRequest request)
    {
        if (!request.Query.TryGetValue("signature", out var sigVals) || string.IsNullOrWhiteSpace(sigVals))
        {
            return false;
        }

        if (request.Query.TryGetValue("expires", out var expVals) &&
            long.TryParse(expVals.ToString(), out var expTs))
        {
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expTs)
            {
                return false;
            }
        }

        // Accept either native signed URL or Laravel's APP_KEY HMAC-SHA256 URL
        var scheme = string.IsNullOrWhiteSpace(request.Scheme) ? "http" : request.Scheme;
        var host = request.Host.HasValue ? request.Host.Value : "127.0.0.1:8000";
        var path = request.Path.Value ?? "/";

        var sortedParams = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var originalOrderParams = new List<string>();

        foreach (var kv in request.Query)
        {
            if (kv.Key.Equals("signature", StringComparison.OrdinalIgnoreCase)) continue;
            var val = kv.Value.ToString();
            sortedParams[kv.Key] = val;
            originalOrderParams.Add($"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(val)}");
        }

        var providedSig = sigVals.ToString();

        var sortedQuery = string.Join("&", sortedParams.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
        var sortedUrl = string.IsNullOrEmpty(sortedQuery) ? $"{scheme}://{host}{path}" : $"{scheme}://{host}{path}?{sortedQuery}";
        var expectedSortedSig = ComputeHmacSha256(sortedUrl);

        if (CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedSortedSig.ToLowerInvariant()),
                Encoding.UTF8.GetBytes(providedSig.ToLowerInvariant())))
        {
            return true;
        }

        var origQuery = string.Join("&", originalOrderParams);
        var origUrl = string.IsNullOrEmpty(origQuery) ? $"{scheme}://{host}{path}" : $"{scheme}://{host}{path}?{origQuery}";
        var expectedOrigSig = ComputeHmacSha256(origUrl);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedOrigSig.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(providedSig.ToLowerInvariant()));
    }

    private static string ComputeHmacSha256(string payload)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(SigningKey), Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

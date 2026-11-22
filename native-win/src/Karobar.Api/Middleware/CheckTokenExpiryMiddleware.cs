using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Karobar.Api.Middleware;

public class CheckTokenExpiryMiddleware
{
    private readonly RequestDelegate _next;

    public CheckTokenExpiryMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, KarobarDb db)
    {
        if (context.GetEndpoint()?.Metadata.GetMetadata<IAllowAnonymous>() != null)
        {
            await _next(context);
            return;
        }
        if (context.Items.TryGetValue("KarobarUser", out var userObj) &&
            userObj is Dictionary<string, object?> user)
        {
            var userId = Convert.ToInt64(user["id"]);
            var status = user.GetValueOrDefault("status")?.ToString() ?? "active";

            if (!status.Equals("active", StringComparison.OrdinalIgnoreCase))
            {
                if (await db.HasTableAsync("personal_access_tokens"))
                {
                    await db.ExecuteAsync(
                        "DELETE FROM personal_access_tokens WHERE tokenable_id = @userId",
                        new { userId });
                }

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Your account has been deactivated. Please contact an administrator."
                });
                return;
            }

            if (user.TryGetValue("remember_token_expires_at", out var remExpObj) &&
                remExpObj != null &&
                !string.IsNullOrWhiteSpace(remExpObj.ToString()) &&
                DateTimeOffset.TryParse(
                    remExpObj.ToString(),
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                    out var remExp) &&
                DateTimeOffset.UtcNow > remExp)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "Session expired. Please login again.",
                    expired = true
                });
                return;
            }
        }

        await _next(context);
    }
}

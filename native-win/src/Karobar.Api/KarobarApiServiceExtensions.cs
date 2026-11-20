using Karobar.Api.Middleware;
using Karobar.Background;
using Karobar.Data;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Karobar.Api;

public static class KarobarApiServiceExtensions
{
    public static IServiceCollection AddKarobarApi(
        this IServiceCollection services,
        IConfiguration? configuration = null,
        string? explicitSqlitePath = null)
    {
        services.AddKarobarData(configuration, explicitSqlitePath);
        services.AddKarobarPrinting();
        services.AddKarobarBackgroundServices();

        services.TryAddScoped<KarobarDb>(sp =>
            new KarobarDb(sp, configuration ?? sp.GetService<IConfiguration>(), explicitSqlitePath));

        // Phase 3 & 4 Services
        services.TryAddScoped<LicenseService>();
        services.TryAddScoped<OperatorPinService>();
        services.TryAddScoped<SyncPulseService>();
        services.TryAddScoped<SqliteExportService>();
        services.TryAddScoped<BackupService>();
        services.TryAddScoped<DocumentSequenceService>();
        services.TryAddScoped<DocumentAuditService>();

        // Auto-register any other *Service classes in Karobar.Services assembly
        var servicesAssembly = typeof(KarobarDb).Assembly;
        foreach (var type in servicesAssembly.GetExportedTypes())
        {
            if (type.IsClass && !type.IsAbstract && type.Name.EndsWith("Service", StringComparison.Ordinal))
            {
                services.TryAddScoped(type);
            }
        }

        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = SanctumAuthenticationHandler.SchemeName;
                options.DefaultChallengeScheme = SanctumAuthenticationHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, SanctumAuthenticationHandler>(
                SanctumAuthenticationHandler.SchemeName,
                _ => { });

        services.AddAuthorization();

        services
            .AddControllers()
            .AddApplicationPart(typeof(KarobarApiServiceExtensions).Assembly)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = null;
                options.JsonSerializerOptions.DictionaryKeyPolicy = null;
            });

        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in context.ModelState)
                {
                    if (kvp.Value?.Errors.Count > 0)
                    {
                        errors[kvp.Key] = kvp.Value.Errors
                            .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage)
                            .ToArray();
                    }
                }
                var firstMsg = errors.Values.SelectMany(v => v).FirstOrDefault() ?? "The given data was invalid.";
                return new ObjectResult(new Dictionary<string, object?>
                {
                    ["message"] = firstMsg,
                    ["errors"] = errors
                })
                {
                    StatusCode = StatusCodes.Status422UnprocessableEntity
                };
            };
        });

        return services;
    }

    public static IApplicationBuilder UseKarobarApiPipeline(this IApplicationBuilder app)
    {
        // Global JSON exception boundary for /api/* routes so domain/validation/missing-record errors
        // never leak raw HTML or stack traces and instead return clean Laravel-compatible { message } JSON.
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception ex) when (context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) && !context.Response.HasStarted)
            {
                var (statusCode, msg) = ex switch
                {
                    KeyNotFoundException => (StatusCodes.Status404NotFound, string.IsNullOrWhiteSpace(ex.Message) ? "Resource not found." : ex.Message),
                    ArgumentException or InvalidOperationException => (StatusCodes.Status422UnprocessableEntity, string.IsNullOrWhiteSpace(ex.Message) ? "Invalid operation." : ex.Message),
                    UnauthorizedAccessException => (StatusCodes.Status403Forbidden, string.IsNullOrWhiteSpace(ex.Message) ? "This action is unauthorized." : ex.Message),
                    Microsoft.Data.Sqlite.SqliteException sqlex when sqlex.SqliteErrorCode == 19 => (StatusCodes.Status422UnprocessableEntity, "Database constraint violation: please check required or unique fields."),
                    _ => (StatusCodes.Status500InternalServerError, "An unexpected server error occurred. Please check logs or try again.")
                };
                context.Response.Clear();
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new Dictionary<string, object?> { ["message"] = msg });
            }
        });

        // Match Laravel's ConvertEmptyStringsToNull middleware globally so empty query params
        // like ?low_stock=&customer_id=&supplier_id= never trigger ASP.NET Core 400/422 errors
        // on typed int/long/bool [FromQuery] parameters across all 59 controllers.
        app.Use(async (context, next) =>
        {
            if (context.Request.QueryString.HasValue)
            {
                var parsed = QueryHelpers.ParseQuery(context.Request.QueryString.Value);
                var hasEmpty = false;
                foreach (var kv in parsed)
                {
                    if (kv.Value.Any(string.IsNullOrWhiteSpace))
                    {
                        hasEmpty = true;
                        break;
                    }
                }

                if (hasEmpty)
                {
                    var cleaned = new List<KeyValuePair<string, string?>>();
                    foreach (var kv in parsed)
                    {
                        foreach (var v in kv.Value)
                        {
                            if (!string.IsNullOrWhiteSpace(v))
                            {
                                cleaned.Add(new KeyValuePair<string, string?>(kv.Key, v));
                            }
                        }
                    }

                    context.Request.QueryString = cleaned.Count > 0
                        ? QueryString.Create(cleaned)
                        : QueryString.Empty;
                }
            }

            await next();
        });

        app.UseAuthentication();
        app.UseMiddleware<CheckTokenExpiryMiddleware>();
        app.UseMiddleware<TrackTerminalMiddleware>();
        app.UseMiddleware<SignedUrlMiddleware>();
        app.UseMiddleware<EnsureLicensedMiddleware>();
        app.UseAuthorization();
        return app;
    }
}

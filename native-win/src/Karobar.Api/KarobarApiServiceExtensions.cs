using Karobar.Api.Middleware;
using Karobar.Background;
using Karobar.Data;
using Karobar.Printing;
using Karobar.Services;
using Karobar.Services.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
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

        return services;
    }

    public static IApplicationBuilder UseKarobarApiPipeline(this IApplicationBuilder app)
    {
        app.UseAuthentication();
        app.UseMiddleware<CheckTokenExpiryMiddleware>();
        app.UseMiddleware<TrackTerminalMiddleware>();
        app.UseMiddleware<SignedUrlMiddleware>();
        app.UseMiddleware<EnsureLicensedMiddleware>();
        app.UseAuthorization();
        return app;
    }
}

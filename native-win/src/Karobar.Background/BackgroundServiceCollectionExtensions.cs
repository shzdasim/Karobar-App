using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Karobar.Background;

public static class BackgroundServiceCollectionExtensions
{
    public static IServiceCollection AddKarobarBackgroundServices(this IServiceCollection services, string? appRoot = null)
    {
        services.TryAddSingleton<FbrFiscalizationEngine>();
        services.TryAddSingleton(_ => new CubeKnowledgeRetriever(appRoot));
        services.TryAddSingleton<CubePromptBuilder>();
        services.TryAddSingleton<CubeQuotaGuard>();
        services.TryAddSingleton<GroqProvider>();
        services.TryAddSingleton<GeminiProvider>();
        services.TryAddSingleton<CubeTaskExecutorService>();
        services.TryAddSingleton<CubeBackgroundTaskManager>();
        services.TryAddSingleton<CubeAssistantService>();
        services.TryAddSingleton(_ => new LocalWhatsAppSidecarManager(appRoot));
        services.TryAddSingleton(_ => new WaseelaAndSqliteImportEngine(appRoot));
        services.TryAddSingleton(sp => new KarobarCliCommandRunner(
            appRoot,
            sp.GetRequiredService<WaseelaAndSqliteImportEngine>(),
            sp.GetService<ILogger<KarobarCliCommandRunner>>()));
        if (!services.Any(d => d.ImplementationType == typeof(KarobarSchedulerHostedService)))
        {
            services.AddHostedService<KarobarSchedulerHostedService>();
        }
        return services;
    }
}

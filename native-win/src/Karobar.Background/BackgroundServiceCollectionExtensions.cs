using Microsoft.Extensions.DependencyInjection;

namespace Karobar.Background;

public static class BackgroundServiceCollectionExtensions
{
    public static IServiceCollection AddKarobarBackgroundServices(this IServiceCollection services, string? appRoot = null)
    {
        services.AddSingleton<FbrFiscalizationEngine>();
        services.AddSingleton(_ => new CubeKnowledgeRetriever(appRoot));
        services.AddSingleton<CubePromptBuilder>();
        services.AddSingleton<CubeQuotaGuard>();
        services.AddSingleton<GroqProvider>();
        services.AddSingleton<GeminiProvider>();
        services.AddSingleton<CubeTaskExecutorService>();
        services.AddSingleton<CubeAssistantService>();
        services.AddSingleton(_ => new LocalWhatsAppSidecarManager(appRoot));
        services.AddSingleton(_ => new WaseelaAndSqliteImportEngine(appRoot));
        services.AddSingleton(sp => new KarobarCliCommandRunner(appRoot, sp.GetRequiredService<WaseelaAndSqliteImportEngine>()));
        services.AddHostedService<KarobarSchedulerHostedService>();
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Karobar.Printing;

public static class PrintingServiceCollectionExtensions
{
    public static IServiceCollection AddKarobarPrinting(this IServiceCollection services, string? appRoot = null)
    {
        services.TryAddSingleton(_ => new Win32RawPrinterService(appRoot));
        services.TryAddSingleton<PrintTemplateRenderer>();
        services.TryAddSingleton<PdfDocumentGenerator>();
        services.TryAddSingleton(sp => new DocumentShareEngine(appRoot, sp.GetRequiredService<PdfDocumentGenerator>()));
        return services;
    }
}

using Microsoft.Extensions.DependencyInjection;

namespace Karobar.Printing;

public static class PrintingServiceCollectionExtensions
{
    public static IServiceCollection AddKarobarPrinting(this IServiceCollection services, string? appRoot = null)
    {
        services.AddSingleton(_ => new Win32RawPrinterService(appRoot));
        services.AddSingleton<PrintTemplateRenderer>();
        services.AddSingleton<PdfDocumentGenerator>();
        services.AddSingleton(sp => new DocumentShareEngine(appRoot, sp.GetRequiredService<PdfDocumentGenerator>()));
        return services;
    }
}

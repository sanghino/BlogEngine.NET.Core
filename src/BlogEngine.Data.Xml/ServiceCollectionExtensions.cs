using BlogEngine.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlogEngine.Data.Xml;

public static class ServiceCollectionExtensions
{
    /// <summary>Registra solo il provider XML, senza collegarlo ai contratti (per la scelta a runtime).</summary>
    public static IServiceCollection AddBlogEngineXmlProvider(this IServiceCollection services)
    {
        services.TryAddSingleton<XmlDataStore>();
        return services;
    }

    /// <summary>
    /// Registra il provider XML per tutti i contratti di memorizzazione.
    /// Richiede che <c>BlogEnginePaths</c> sia già registrato.
    /// </summary>
    public static IServiceCollection AddBlogEngineXmlStorage(this IServiceCollection services) =>
        services.AddBlogEngineXmlProvider().AddBlogEngineStores(sp => sp.GetRequiredService<XmlDataStore>());
}

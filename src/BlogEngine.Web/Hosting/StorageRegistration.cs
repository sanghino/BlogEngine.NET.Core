using BlogEngine.Core;
using BlogEngine.Core.Configuration;
using BlogEngine.Data.Sqlite;
using BlogEngine.Data.Xml;

namespace BlogEngine.Web.Hosting;

public static class StorageRegistration
{
    /// <summary>
    /// Registra entrambi i provider e collega i contratti a quello scelto nell'installazione.
    /// La scelta avviene alla prima creazione di uno store, così la configurazione iniziale può
    /// ancora decidere il provider prima che venga usato.
    /// </summary>
    public static IServiceCollection AddBlogEngineStorage(this IServiceCollection services)
    {
        services.AddBlogEngineXmlProvider();
        services.AddBlogEngineSqliteProvider();
        services.AddBlogEngineStores(sp => sp.GetRequiredService<InstallationState>().LockProvider() switch
        {
            DataProviderKind.Sqlite => sp.GetRequiredService<SqliteDataStore>(),
            _ => sp.GetRequiredService<XmlDataStore>(),
        });

        return services;
    }
}

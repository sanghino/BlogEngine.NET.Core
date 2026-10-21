using BlogEngine.Core.Data;
using BlogEngine.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Data.Xml;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registra il provider XML per tutti i contratti di memorizzazione.
    /// Richiede che <c>BlogEnginePaths</c> sia già registrato.
    /// </summary>
    public static IServiceCollection AddBlogEngineXmlStorage(this IServiceCollection services)
    {
        services.AddSingleton<XmlDataStore>();
        services.AddSingleton<IDataStoreInitializer>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<IBlogStore>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<IEntityStore<Post>>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<IEntityStore<Page>>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<IEntityStore<Category>>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<IEntityStore<BlogRollItem>>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<IEntityStore<Referrer>>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<IProfileStore>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<IRightsStore>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<ICustomFieldStore>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<IStringListStore>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<IExtensionDataStore>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<IUserStore>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<IRoleStore>(sp => sp.GetRequiredService<XmlDataStore>());
        services.AddSingleton<ICommentFilterStore>(sp => sp.GetRequiredService<XmlDataStore>());
        return services;
    }
}

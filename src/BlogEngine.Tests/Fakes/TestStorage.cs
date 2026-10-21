using BlogEngine.Core.Configuration;
using BlogEngine.Core.Data;
using BlogEngine.Data.Sqlite;
using BlogEngine.Data.Xml;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Fakes;

public enum StorageKind
{
    Memory,
    Xml,
    Sqlite,
}

/// <summary>
/// Cartella dati temporanea e registrazione del provider scelto. La cartella viene eliminata con Dispose.
/// </summary>
public sealed class TestStorage : IDisposable
{
    public TestStorage(StorageKind kind)
    {
        Kind = kind;
        DataRoot = Path.Combine(Path.GetTempPath(), "be-tests", Guid.NewGuid().ToString("N"));
        Paths = new BlogEnginePaths(DataRoot, new BlogEngineOptions { DataPath = DataRoot });
    }

    public StorageKind Kind { get; }

    public string DataRoot { get; }

    public BlogEnginePaths Paths { get; }

    /// <summary>Store in memoria condiviso, così una "nuova istanza" vede gli stessi dati.</summary>
    public InMemoryStores Memory { get; } = new();

    /// <summary>Registra il provider: per Memory lo store condiviso, per XML e SQLite i provider veri.</summary>
    public void Register(IServiceCollection services)
    {
        services.AddSingleton(Paths);
        switch (Kind)
        {
            case StorageKind.Xml:
                services.AddBlogEngineXmlStorage();
                break;
            case StorageKind.Sqlite:
                services.AddLogging();
                services.AddBlogEngineSqliteStorage();
                break;
            default:
                RegisterMemory(services, Memory);
                break;
        }
    }

    /// <summary>Crea un nuovo contenitore con il provider e restituisce lo store, già inizializzato.</summary>
    public async Task<(ServiceProvider Provider, object Store)> CreateStoreAsync()
    {
        var services = new ServiceCollection();
        Register(services);
        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IDataStoreInitializer>().InitializeAsync();
        return (provider, provider.GetRequiredService<IBlogStore>());
    }

    public static void RegisterMemory(IServiceCollection services, InMemoryStores stores)
    {
        services.AddSingleton<IDataStoreInitializer>(stores);
        services.AddSingleton<IBlogStore>(stores);
        services.AddSingleton<IEntityStore<BlogEngine.Models.Post>>(stores);
        services.AddSingleton<IEntityStore<BlogEngine.Models.Page>>(stores);
        services.AddSingleton<IEntityStore<BlogEngine.Models.Category>>(stores);
        services.AddSingleton<IEntityStore<BlogEngine.Models.BlogRollItem>>(stores);
        services.AddSingleton<IEntityStore<BlogEngine.Models.Referrer>>(stores);
        services.AddSingleton<IProfileStore>(stores);
        services.AddSingleton<ISettingsStore>(stores);
        services.AddSingleton<IRightsStore>(stores);
        services.AddSingleton<ICustomFieldStore>(stores);
        services.AddSingleton<IStringListStore>(stores);
        services.AddSingleton<IExtensionDataStore>(stores);
        services.AddSingleton<IUserStore>(stores);
        services.AddSingleton<IRoleStore>(stores);
        services.AddSingleton<ICommentFilterStore>(stores);
    }

    public void Dispose()
    {
        // Il pool di connessioni di SQLite tiene aperto il file: va svuotato prima di cancellarlo.
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(DataRoot))
        {
            Directory.Delete(DataRoot, recursive: true);
        }
    }
}

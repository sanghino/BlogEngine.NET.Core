using BlogEngine.Core.Configuration;
using BlogEngine.Core.Data;
using BlogEngine.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Data.Sqlite;

public static class ServiceCollectionExtensions
{
    public const string DatabaseFileName = "blogengine.db";

    /// <summary>
    /// Registra il provider SQLite per tutti i contratti di memorizzazione. Il database è il file
    /// "blogengine.db" nella cartella dati. Richiede che <c>BlogEnginePaths</c> sia già registrato.
    /// </summary>
    public static IServiceCollection AddBlogEngineSqliteStorage(this IServiceCollection services)
    {
        services.AddDbContextFactory<BlogDbContext>((sp, options) =>
            options.UseSqlite(BuildConnectionString(sp.GetRequiredService<BlogEnginePaths>().Data)));

        services.AddSingleton<SqliteDataStore>();
        services.AddSingleton<IDataStoreInitializer>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IBlogStore>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IEntityStore<Post>>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IEntityStore<Page>>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IEntityStore<Category>>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IEntityStore<BlogRollItem>>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IEntityStore<Referrer>>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IProfileStore>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<ISettingsStore>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IRightsStore>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<ICustomFieldStore>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IStringListStore>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IExtensionDataStore>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IUserStore>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<IRoleStore>(sp => sp.GetRequiredService<SqliteDataStore>());
        services.AddSingleton<ICommentFilterStore>(sp => sp.GetRequiredService<SqliteDataStore>());
        return services;
    }

    public static string BuildConnectionString(string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);

        // Journal predefinito (non WAL): il WAL richiede memoria condivisa e non è affidabile
        // sulle cartelle di rete che alcuni hosting condivisi usano per lo spazio web.
        return new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataFolder, DatabaseFileName),
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
        }.ToString();
    }
}

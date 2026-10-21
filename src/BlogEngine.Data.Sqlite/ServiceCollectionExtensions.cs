using BlogEngine.Core;
using BlogEngine.Core.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlogEngine.Data.Sqlite;

public static class ServiceCollectionExtensions
{
    public const string DatabaseFileName = "blogengine.db";

    /// <summary>Registra solo il provider SQLite, senza collegarlo ai contratti (per la scelta a runtime).</summary>
    public static IServiceCollection AddBlogEngineSqliteProvider(this IServiceCollection services)
    {
        services.AddDbContextFactory<BlogDbContext>((sp, options) =>
            options.UseSqlite(BuildConnectionString(sp.GetRequiredService<BlogEnginePaths>().Data)));
        services.TryAddSingleton<SqliteDataStore>();
        return services;
    }

    /// <summary>
    /// Registra il provider SQLite per tutti i contratti di memorizzazione. Il database è il file
    /// "blogengine.db" nella cartella dati. Richiede che <c>BlogEnginePaths</c> sia già registrato.
    /// </summary>
    public static IServiceCollection AddBlogEngineSqliteStorage(this IServiceCollection services) =>
        services.AddBlogEngineSqliteProvider().AddBlogEngineStores(sp => sp.GetRequiredService<SqliteDataStore>());

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

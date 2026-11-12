using BlogEngine.Core.Services;
using BlogEngine.Web.Hosting;

namespace BlogEngine.Web.Import;

/// <summary>
/// Comando "import-bludit": importa un blog Bludit nel blog principale e termina, senza avviare il sito.
/// Esempio: dotnet run --project BlogEngine.Web -- import-bludit "C:\bludit\bl-content" admin
/// </summary>
public static class BluditCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string folder, string? author)
    {
        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BluditCommand));
        var blog = (await scope.ServiceProvider.GetRequiredService<BlogsCache>().GetAllAsync()).First(b => b.IsPrimary);
        scope.ServiceProvider.GetRequiredService<RequestBlogContext>().RunAsSystem(blog);

        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<BluditImporter>().ImportAsync(blog.Id, folder, author);
            Console.WriteLine($"Importati {result.Posts} post, {result.Pages} pagine, {result.Categories} categorie nuove, {result.Files} file.");
            foreach (var item in result.Skipped)
            {
                Console.WriteLine($"  saltato: {item}");
            }

            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Text.Json.JsonException)
        {
            logger.LogError(ex, "Importazione da Bludit non riuscita.");
            return 1;
        }
    }
}

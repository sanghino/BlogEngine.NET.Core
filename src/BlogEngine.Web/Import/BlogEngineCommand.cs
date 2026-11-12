using BlogEngine.Core.Services;
using BlogEngine.Web.Hosting;

namespace BlogEngine.Web.Import;

/// <summary>
/// Comando "import-blogengine": importa nel blog principale il BlogML esportato da un vecchio BlogEngine.NET
/// e termina, senza avviare il sito. Scarica immagini e allegati dal vecchio sito, che deve essere ancora online.
/// Esempio: BlogEngine.Web import-blogengine "C:\export\BlogML.xml" https://www.fiorentina.info [Europe/Rome] [indirizzo da cui scaricare]
/// L'ultimo argomento serve quando il dominio punta già al nuovo sito e il vecchio è raggiungibile solo da un indirizzo provvisorio.
/// </summary>
public static class BlogEngineCommand
{
    public const string Name = "import-blogengine";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine($"Uso: BlogEngine.Web {Name} <file BlogML> [indirizzo del vecchio sito] [fuso orario, predefinito Europe/Rome] [indirizzo da cui scaricare i file]");
            return 2;
        }

        if (!TryParseSite(args, 2, out var oldSite) || !TryParseSite(args, 4, out var downloadFrom))
        {
            return 2;
        }

        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(args.Length > 3 && args[3] != "-" ? args[3] : "Europe/Rome");
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            Console.Error.WriteLine($"Fuso orario sconosciuto: {args[3]}");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(BlogEngineCommand));
        var blog = (await scope.ServiceProvider.GetRequiredService<BlogsCache>().GetAllAsync()).First(b => b.IsPrimary);
        scope.ServiceProvider.GetRequiredService<RequestBlogContext>().RunAsSystem(blog);

        try
        {
            var result = await scope.ServiceProvider.GetRequiredService<BlogEngineImporter>().ImportAsync(blog.Id, Path.GetFullPath(args[1]), oldSite, zone, downloadFrom);
            Console.WriteLine($"Importati {result.Posts} post, {result.Pages} pagine, {result.Categories} categorie nuove, {result.Comments} commenti.");
            Console.WriteLine($"File: {result.DownloadedFiles} scaricati, {result.ExistingFiles} già presenti, {result.FailedFiles.Count} non riusciti.");
            foreach (var item in result.FailedFiles)
            {
                Console.WriteLine($"  non scaricato: {item}");
            }

            return 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Importazione da BlogEngine.NET non riuscita.");
            return 1;
        }
    }

    /// <summary>Indirizzo http(s) in posizione <paramref name="index"/>; assente o "-" vale null.</summary>
    private static bool TryParseSite(string[] args, int index, out Uri? site)
    {
        site = null;
        if (args.Length <= index || args[index] == "-")
        {
            return true;
        }

        if (Uri.TryCreate(args[index].TrimEnd('/') + "/", UriKind.Absolute, out site) && site.Scheme is "http" or "https")
        {
            return true;
        }

        Console.Error.WriteLine($"Indirizzo non valido: {args[index]}");
        return false;
    }
}

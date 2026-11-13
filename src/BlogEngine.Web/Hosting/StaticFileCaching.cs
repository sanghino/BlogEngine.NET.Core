using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace BlogEngine.Web.Hosting;

/// <summary>
/// Cache dei file statici. Script, fogli di stile e template HTML (amministrazione AngularJS, temi, widget)
/// cambiano a ogni aggiornamento ma hanno sempre lo stesso nome: con "no-cache" il browser ne tiene una copia
/// e prima di usarla chiede al server se è cambiata (risposta 304 con l'ETag, senza riscaricarla). Così, dopo
/// un aggiornamento o il passaggio da una vecchia installazione con gli stessi nomi di file, nessun browser
/// resta con versioni vecchie. Immagini e font mantengono il comportamento predefinito.
/// </summary>
public static class StaticFileCaching
{
    private static readonly HashSet<string> RevalidatedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".js", ".mjs", ".css", ".html", ".htm", ".json", ".map",
    };

    /// <summary>True per i file che il browser deve ricontrollare a ogni uso.</summary>
    public static bool MustRevalidate(string fileName) => RevalidatedExtensions.Contains(Path.GetExtension(fileName));

    public static void Apply(StaticFileResponseContext context)
    {
        if (MustRevalidate(context.File.Name))
        {
            context.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
        }
    }
}

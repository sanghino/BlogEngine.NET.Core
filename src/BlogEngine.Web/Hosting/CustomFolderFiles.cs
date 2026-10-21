using Microsoft.Extensions.FileProviders;

namespace BlogEngine.Web.Hosting;

public static class CustomFolderFiles
{
    /// <summary>
    /// Serve i file statici di temi o widget con lo stesso indirizzo (es. /Custom/Themes/Standard/...),
    /// cercandoli prima nella cartella del pacchetto e poi in quella degli upload in App_Data.
    /// Sono serviti solo i tipi di file noti (CSS, JS, immagini, font...): i template .liquid no.
    /// </summary>
    public static IApplicationBuilder UseCustomFolderFiles(this IApplicationBuilder app, string requestPath, params string[] folders)
    {
        var providers = folders
            .Select(folder =>
            {
                Directory.CreateDirectory(folder);
                return (IFileProvider)new PhysicalFileProvider(folder);
            })
            .ToArray();

        return app.UseStaticFiles(new StaticFileOptions
        {
            RequestPath = requestPath,
            FileProvider = new CompositeFileProvider(providers),
            OnPrepareResponse = context => context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff",
        });
    }
}

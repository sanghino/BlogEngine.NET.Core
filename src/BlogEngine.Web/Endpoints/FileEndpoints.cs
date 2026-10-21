using BlogEngine.Core.Files;
using BlogEngine.Services;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace BlogEngine.Web.Endpoints;

/// <summary>
/// File e immagini caricati, con gli stessi indirizzi della versione originale:
/// "file.axd?file=/2025/03/documento.pdf" e "image.axd?picture=/2025/03/foto.jpg".
/// I file stanno fuori dalla cartella pubblicata e passano sempre da qui. Video e audio usano "/media/nome.mp4".
/// </summary>
public static class FileEndpoints
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private static readonly HashSet<string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/gif", "image/bmp", "image/tiff", "image/webp", "image/svg+xml", "image/x-icon",
    };

    /// <summary>Cartella dei file del blog con video e audio (estensione MediaElementPlayer).</summary>
    public const string MediaFolder = "/media";

    private static readonly HashSet<string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "video/mp4", "video/webm", "video/ogg", "audio/mpeg", "audio/mp4", "audio/ogg", "audio/wav", "audio/webm",
        "image/jpeg", "image/png", "image/gif", "image/webp",
    };

    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Video e audio (con le immagini di anteprima): /media/nome.mp4, riproducibili anche a salti.
        endpoints.MapGet("/media/{**path}", (string path, HttpContext http, FileStore files, IBlogContext context) =>
            Serve(http, files, context, $"{MediaFolder}/{path}", imagesOnly: false, mediaOnly: true));
        endpoints.MapGet("/file.axd", (string? file, HttpContext http, FileStore files, IBlogContext context) =>
            Serve(http, files, context, file, imagesOnly: false, mediaOnly: false));
        endpoints.MapGet("/image.axd", (string? picture, HttpContext http, FileStore files, IBlogContext context) =>
            Serve(http, files, context, picture, imagesOnly: true, mediaOnly: false));
        return endpoints;
    }

    private static IResult Serve(HttpContext http, FileStore files, IBlogContext context, string? relativePath, bool imagesOnly, bool mediaOnly)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return Results.NotFound();
        }

        string path;
        try
        {
            path = files.Resolve(context.Blog.Id, relativePath);
        }
        catch (ArgumentException)
        {
            return Results.NotFound();
        }

        if (!File.Exists(path))
        {
            return Results.NotFound();
        }

        var contentType = ContentTypes.TryGetContentType(path, out var type) ? type : "application/octet-stream";
        if ((imagesOnly && !ImageTypes.Contains(contentType)) || (mediaOnly && !MediaTypes.Contains(contentType)))
        {
            return Results.NotFound();
        }

        // Un file caricato non deve poter eseguire script nel contesto del sito.
        http.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        http.Response.Headers[HeaderNames.ContentSecurityPolicy] = "default-src 'none'; img-src 'self' data:; style-src 'unsafe-inline'; sandbox";
        http.Response.Headers[HeaderNames.CacheControl] = "public, max-age=2592000";

        var info = new FileInfo(path);
        return Results.File(
            path,
            contentType,
            fileDownloadName: imagesOnly || mediaOnly ? null : info.Name,
            lastModified: info.LastWriteTimeUtc,
            enableRangeProcessing: true);
    }
}

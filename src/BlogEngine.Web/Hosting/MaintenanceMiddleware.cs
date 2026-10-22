using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Themes;

namespace BlogEngine.Web.Hosting;

/// <summary>
/// Modalità manutenzione (Impostazioni → Base): i visitatori ricevono la pagina "sito in manutenzione" con
/// codice 503 e Retry-After, così i motori di ricerca non considerano il sito sparito. Chi può accedere
/// all'amministrazione vede il blog normalmente; login, amministrazione, API e file statici restano raggiungibili.
/// Ogni blog ha la sua impostazione.
/// </summary>
public sealed class MaintenanceMiddleware(RequestDelegate next)
{
    /// <summary>Secondi suggeriti a browser e motori di ricerca prima di riprovare.</summary>
    public const int RetryAfterSeconds = 3600;

    private static readonly string[] OpenPaths =
    [
        "/admin", "/api", "/account", "/setup", "/error", "/metaweblog.axd",
        "/Custom", "/Content", "/Scripts", "/image.axd", "/file.axd", "/media",
    ];

    public async Task InvokeAsync(HttpContext http, RequestBlogContext context)
    {
        if (!context.HasBlog || IsOpen(http.Request.Path))
        {
            await next(http);
            return;
        }

        var blogSettings = await http.RequestServices.GetRequiredService<ISettingsService>().GetAsync();
        if (!blogSettings.EnableMaintenanceMode
            || await http.RequestServices.GetRequiredService<IRightsService>().IsAuthorizedAsync(Rights.AccessAdminPages))
        {
            await next(http);
            return;
        }

        var site = http.RequestServices.GetRequiredService<SiteRenderer>();
        var html = await site.MaintenanceAsync(blogSettings.MaintenanceMessage);
        http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        http.Response.ContentType = "text/html; charset=utf-8";
        http.Response.Headers.RetryAfter = RetryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        http.Response.Headers.CacheControl = "no-store";
        if (!HttpMethods.IsHead(http.Request.Method))
        {
            await http.Response.WriteAsync(html);
        }
    }

    private static bool IsOpen(PathString path) =>
        OpenPaths.Any(open => path.StartsWithSegments(open, StringComparison.OrdinalIgnoreCase));
}

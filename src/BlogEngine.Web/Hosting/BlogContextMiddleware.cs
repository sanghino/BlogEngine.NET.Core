using BlogEngine.Core.Services;
using BlogEngine.Services;

namespace BlogEngine.Web.Hosting;

/// <summary>
/// Determina il blog della richiesta (porting di Blog.CurrentInstance). Per un blog in sottocartella
/// sposta il prefisso nel PathBase: "/secondo/post/x" diventa PathBase "/secondo" e Path "/post/x",
/// così pagine e API funzionano allo stesso modo per ogni blog.
/// </summary>
public sealed class BlogContextMiddleware(RequestDelegate next)
{
    /// <summary>Intestazione con cui l'amministrazione indica il blog delle chiamate alle API.</summary>
    public const string BlogInstanceHeader = "x-blog-instance";

    public async Task InvokeAsync(HttpContext http, RequestBlogContext context, InstallationState installation)
    {
        context.RequestBaseUri = new Uri($"{http.Request.Scheme}://{http.Request.Host}/");
        if (!installation.IsInstalled)
        {
            await next(http);
            return;
        }

        // Risolto solo a installazione avvenuta: creare i servizi dei dati fisserebbe il provider
        // prima che la configurazione iniziale lo abbia scelto.
        var blogs = http.RequestServices.GetRequiredService<IBlogService>();
        Guid? explicitBlogId = Guid.TryParse(http.Request.Headers[BlogInstanceHeader], out var id) ? id : null;
        var path = http.Request.PathBase.Add(http.Request.Path).Value ?? "/";
        var blog = await blogs.ResolveAsync(http.Request.Host.Host, path, explicitBlogId, http.RequestAborted);
        if (blog is null)
        {
            http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        context.Blog = blog;

        var root = BlogResolver.RelativeRoot(blog).TrimEnd('/');
        if (root.Length > 0 && http.Request.Path.StartsWithSegments(root, StringComparison.OrdinalIgnoreCase, out var remaining))
        {
            http.Request.PathBase = http.Request.PathBase.Add(http.Request.Path.Value![..root.Length]);
            http.Request.Path = remaining.HasValue ? remaining : "/";
        }

        await next(http);
    }
}

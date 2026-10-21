using System.Text;
using System.Text.Json;
using BlogEngine.Core.Configuration;
using BlogEngine.Core.Services;
using BlogEngine.Services;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace BlogEngine.Web.Admin;

/// <summary>
/// "admin.res.axd": script con le variabili del sito (SiteVars) e le etichette tradotte dell'amministrazione
/// (BlogAdmin.i18n), come nella versione originale. Le chiavi sono quelle di Resources/AdminLabels.txt.
/// </summary>
public static class AdminResourcesEndpoint
{
    private static readonly Lazy<IReadOnlyList<string>> LabelKeys = new(() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Resources", "AdminLabels.txt"))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList());

    public static IEndpointRouteBuilder MapAdminResources(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin/admin.res.axd", (HttpContext http, IBlogContext context, ILinkGenerator links,
            IStringLocalizer<Labels> labels, IOptions<BlogEngineOptions> options) =>
        {
            var root = links.BlogRoot;
            var siteVars = new Dictionary<string, string>
            {
                ["ApplicationRelativeWebRoot"] = "/",
                ["RelativeWebRoot"] = root,
                ["AbsoluteWebRoot"] = links.ToAbsolute(root).ToString(),
                ["IsPrimary"] = context.Blog.IsPrimary ? "True" : "False",
                ["BlogInstanceId"] = context.Blog.Id.ToString(),
                ["BlogStorageLocation"] = root,
                ["BlogFilesFolder"] = "files",
                ["GenericPageSize"] = options.Value.GenericPageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["GalleryFeedUrl"] = string.Empty,
                ["Version"] = AdminViewContext.Version,
            };

            var i18n = LabelKeys.Value.ToDictionary(k => k, k => labels[k].Value, StringComparer.Ordinal);
            var script = new StringBuilder()
                .Append("SiteVars = ").Append(JsonSerializer.Serialize(siteVars)).Append("; ")
                .Append("BlogAdmin = { i18n: ").Append(JsonSerializer.Serialize(i18n)).Append(" };")
                .ToString();

            // Dipende dall'utente (lingua) e dal blog: non va messo in cache pubblica.
            http.Response.Headers.CacheControl = "private, no-cache";
            return Results.Text(script, "text/javascript", Encoding.UTF8);
        });

        return endpoints;
    }
}

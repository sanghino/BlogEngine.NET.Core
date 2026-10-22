using System.Net;
using System.Text.RegularExpressions;

namespace BlogEngine.Tests.Web;

/// <summary>
/// Verifica che tutti i file usati dall'amministrazione esistano con lo stesso uso di maiuscole e minuscole:
/// su Windows un nome sbagliato funziona comunque, su Linux diventa un errore 404.
/// </summary>
[TestClass]
public sealed partial class AdminAssetsTests
{
    private static readonly string WebRoot = SourcePaths.WebRoot;

    [TestMethod]
    public async Task AdminShellAndEditor_ReferenceOnlyExistingFiles()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();

        foreach (var page in new[] { "/admin/", "/admin/app/editor/editpost.cshtml", "/admin/app/editor/filemanager.cshtml" })
        {
            var html = await admin.GetStringAsync(page);
            foreach (Match match in AssetRegex().Matches(html))
            {
                var url = match.Groups["url"].Value.Split('?')[0];

                // Gli indirizzi .axd sono generati dal server, non sono file.
                if (!url.EndsWith(".axd", StringComparison.Ordinal))
                {
                    Assert.IsTrue(ExistsWithExactCase(url), $"{page} usa {url}, che non esiste con questo nome esatto.");
                }

                Assert.AreEqual(HttpStatusCode.OK, (await admin.GetAsync(match.Groups["url"].Value)).StatusCode, url);
            }
        }
    }

    [TestMethod]
    public void AngularRoutes_PointToExistingViews()
    {
        var appJs = File.ReadAllText(Path.Combine(WebRoot, "admin", "app", "app.js"));
        var views = TemplateRegex().Matches(appJs).Select(m => m.Groups["path"].Value).ToList();

        Assert.IsTrue(views.Count > 20);
        foreach (var view in views)
        {
            Assert.IsTrue(ExistsWithExactCase("/admin/" + view), $"Vista mancante o con maiuscole diverse: {view}");
        }
    }

    [TestMethod]
    public void AdminStylesheets_ReferenceExistingFiles()
    {
        foreach (var css in Directory.EnumerateFiles(Path.Combine(WebRoot, "admin", "themes"), "*.css", SearchOption.AllDirectories))
        {
            var folder = Path.GetDirectoryName(css)!;
            foreach (Match match in CssUrlRegex().Matches(File.ReadAllText(css)))
            {
                var url = match.Groups["url"].Value.Split('?', '#')[0];
                var target = Path.GetFullPath(Path.Combine(folder, url));
                var relative = "/" + Path.GetRelativePath(WebRoot, target).Replace('\\', '/');
                Assert.IsTrue(ExistsWithExactCase(relative), $"{Path.GetFileName(css)} usa {url}, che non esiste con questo nome esatto.");
            }
        }
    }

    /// <summary>Confronta ogni segmento del percorso con i nomi reali presenti sul disco.</summary>
    private static bool ExistsWithExactCase(string url)
    {
        var current = WebRoot;
        foreach (var segment in url.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var entries = Directory.Exists(current) ? Directory.EnumerateFileSystemEntries(current).Select(Path.GetFileName) : [];
            if (!entries.Contains(segment, StringComparer.Ordinal))
            {
                return false;
            }

            current = Path.Combine(current, segment);
        }

        return true;
    }

    [GeneratedRegex(@"(?:src|href)=""(?<url>/(?:Scripts|Content|admin|fonts)/[^""#]+\.(?:js|css|png|jpg|gif|ico|svg|axd)(?:\?[^""]*)?)""")]
    private static partial Regex AssetRegex();

    [GeneratedRegex(@"templateUrl:\s*""(?<path>[^""]+)""")]
    private static partial Regex TemplateRegex();

    [GeneratedRegex("""url\(['"]?(?<url>(?!data:|https?:|#)[^'")]+)['"]?\)""")]
    private static partial Regex CssUrlRegex();
}

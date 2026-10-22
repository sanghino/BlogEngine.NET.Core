using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlogEngine.Core.Configuration;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>Pagine pubbliche mostrate con i temi Liquid: elenco, post, commenti, filtri e pagine di servizio.</summary>
[TestClass]
public sealed partial class PublicSiteTests
{
    [TestMethod]
    public async Task Home_UsesStandardThemeWithWidgetsAndWelcomePost()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync(blogName: "Blog pubblico")).Dispose();
        using var visitor = factory.CreateBrowser();

        var home = await visitor.GetStringAsync("/");

        StringAssert.Contains(home, "<title>Blog pubblico</title>");
        StringAssert.Contains(home, "blog-header", "Il layout è quello del tema Standard.");
        StringAssert.Contains(home, "/Custom/Themes/Standard/src/css/styles.min.css");
        StringAssert.Contains(home, "id=\"widgetzone_sidebar\"", "La zona dei widget del tema viene mostrata.");
        StringAssert.Contains(home, "widget categorylist", "Senza configurazione si usano i widget predefiniti del tema.");
        StringAssert.Contains(home, "Welcome to Blog pubblico");
        StringAssert.Contains(home, "BlogEngineRes = {");
        Assert.IsFalse(home.Contains("widget administration"), "Il menu di amministrazione è solo per gli utenti autenticati.");

        // I file del tema si scaricano, i template no.
        Assert.AreEqual(HttpStatusCode.OK, (await visitor.GetAsync("/Custom/Themes/Standard/src/css/styles.min.css")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await visitor.GetAsync("/Custom/Themes/Standard/layout.liquid")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await visitor.GetAsync("/Custom/Widgets/TagCloud/widget.liquid")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await visitor.GetAsync("/Scripts/blog.js")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await visitor.GetAsync("/Content/Auto/Global.css")).StatusCode);
    }

    [DataTestMethod]
    [DataRow(DataProviderKind.Xml)]
    [DataRow(DataProviderKind.Sqlite)]
    public async Task Post_ShowsContentAndAcceptsComments(DataProviderKind provider)
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync(provider);
        using var visitor = factory.CreateBrowser();

        var postUrl = PostLinkRegex().Match(await visitor.GetStringAsync("/")).Groups["url"].Value;
        Assert.IsTrue(postUrl.StartsWith("/post/", StringComparison.Ordinal), postUrl);

        var page = await visitor.GetStringAsync(postUrl);
        StringAssert.Contains(page, "post-single");
        StringAssert.Contains(page, "id=\"txtContent\"", "Il modulo dei commenti è presente.");
        StringAssert.Contains(page, "ratingcontainer");

        // I dati strutturati sono JSON valido (le stringhe non devono essere codificate come HTML).
        var jsonLd = JsonLdRegex().Matches(page).Select(m => m.Groups["json"].Value).ToList();
        Assert.IsTrue(jsonLd.Count >= 2);
        foreach (var json in jsonLd)
        {
            using var _ = JsonDocument.Parse(json);
        }

        var postId = Regex.Match(page, @"BlogEngine\.comments\.postId = '(?<id>[0-9a-f-]+)'").Groups["id"].Value;
        // SimpleCaptcha è attiva per default, come nella versione originale: domanda "5+5 =" e risposta "10".
        StringAssert.Contains(page, "name=\"simpleCaptchaValue\"");
        var wrong = await PostActionAsync(visitor, page, "/comment", new()
        {
            ["postId"] = postId, ["author"] = "Mario", ["email"] = "mario@example.com", ["content"] = "Spam", ["simpleCaptchaValue"] = "7",
        });
        Assert.AreEqual(HttpStatusCode.BadRequest, wrong.StatusCode);

        var response = await PostActionAsync(visitor, page, "/comment", new()
        {
            ["postId"] = postId,
            ["author"] = "Mario <b>Rossi</b>",
            ["email"] = "mario@example.com",
            ["content"] = "Primo commento\nseconda riga <script>alert(1)</script>",
            ["notify"] = "true",
            ["simpleCaptchaValue"] = "10",
        });

        var body = await response.Content.ReadAsStringAsync();
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, body);
        using var result = JsonDocument.Parse(body);
        var html = result.RootElement.GetProperty("html").GetString()!;
        StringAssert.Contains(html, "Primo commento<br />seconda riga");
        StringAssert.Contains(html, "Mario &lt;b&gt;Rossi&lt;/b&gt;");
        Assert.IsFalse(html.Contains("<script>", StringComparison.Ordinal), "Il testo dei visitatori viene codificato.");
        Assert.IsFalse(result.RootElement.GetProperty("approved").GetBoolean(), "Con la moderazione attiva il commento resta in attesa.");

        // In attesa di moderazione: invisibile ai visitatori, visibile (con il comando "approva") all'amministratore.
        Assert.IsFalse((await visitor.GetStringAsync(postUrl)).Contains("Primo commento"));
        var adminPage = await admin.GetStringAsync(postUrl);
        StringAssert.Contains(adminPage, "Primo commento");
        StringAssert.Contains(adminPage, "BlogEngine.approveComment(");

        var commentId = result.RootElement.GetProperty("id").GetString()!;
        var approve = await PostActionAsync(admin, adminPage, $"/moderation/approve-comment/{commentId}", []);
        Assert.AreEqual(HttpStatusCode.OK, approve.StatusCode);
        StringAssert.Contains(await visitor.GetStringAsync(postUrl), "Primo commento");

        // Senza token antifalsificazione la richiesta viene rifiutata.
        var forged = await visitor.PostAsync("/comment", new FormUrlEncodedContent(new Dictionary<string, string> { ["postId"] = postId }));
        Assert.AreEqual(HttpStatusCode.BadRequest, forged.StatusCode);

        // Voto: una sola volta per visitatore.
        var first = await PostActionAsync(visitor, page, "/rating", new() { ["id"] = postId, ["rating"] = "4" });
        StringAssert.Contains(await first.Content.ReadAsStringAsync(), "\"OK\"");
        var second = await PostActionAsync(visitor, page, "/rating", new() { ["id"] = postId, ["rating"] = "5" });
        StringAssert.Contains(await second.Content.ReadAsStringAsync(), "HASRATED");
    }

    [TestMethod]
    public async Task Comment_WithMissingFields_IsRejected()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        using var visitor = factory.CreateBrowser();
        var postUrl = PostLinkRegex().Match(await visitor.GetStringAsync("/")).Groups["url"].Value;
        var page = await visitor.GetStringAsync(postUrl);
        var postId = Regex.Match(page, @"BlogEngine\.comments\.postId = '(?<id>[0-9a-f-]+)'").Groups["id"].Value;

        var response = await PostActionAsync(visitor, page, "/comment", new() { ["postId"] = postId, ["author"] = "x", ["email"] = "non-valida", ["content"] = "" });

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [TestMethod]
    public async Task Filters_Archive_Search_Calendar_Contact_AndErrors()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        await factory.RunAsSystemAsync(async services =>
        {
            var posts = services.GetRequiredService<IPostService>();
            await posts.SaveAsync(new Post { Title = "Ricetta della torta", Content = "<p>Farina e uova</p>", Author = "admin", Tags = ["Cucina Facile"], DateCreated = new DateTime(2024, 3, 15, 10, 0, 0, DateTimeKind.Utc) });
            await posts.SaveAsync(new Post { Title = "Bozza segreta", Content = "testo", Author = "admin", IsPublished = false });
            await services.GetRequiredService<IPageService>().SaveAsync(new Page { Title = "Chi sono", Content = "<p>Presentazione</p>", IsPublished = true, ShowInList = true });
        });
        using var visitor = factory.CreateBrowser();

        var tag = await visitor.GetStringAsync("/tag/cucina-facile");
        StringAssert.Contains(tag, "Ricetta della torta");
        StringAssert.Contains(await visitor.GetStringAsync("/?tag=/Cucina Facile"), "Ricetta della torta", "Vecchio formato default.aspx?tag=");
        StringAssert.Contains(await visitor.GetStringAsync("/2024/03"), "Ricetta della torta");
        StringAssert.Contains(await visitor.GetStringAsync("/2023"), "class=\"no-posts\"");
        StringAssert.Contains(await visitor.GetStringAsync("/author/admin"), "Ricetta della torta");
        StringAssert.Contains(await visitor.GetStringAsync("/category/general"), "Welcome to");

        var archive = await visitor.GetStringAsync("/archive");
        StringAssert.Contains(archive, "archive-page");
        Assert.IsFalse(archive.Contains("Bozza segreta"), "Le bozze non compaiono ai visitatori.");

        var search = await visitor.GetStringAsync("/search?q=torta");
        StringAssert.Contains(search, "searchresult");
        StringAssert.Contains(search, "Ricetta della torta");

        StringAssert.Contains(await visitor.GetStringAsync("/calendar?year=2024&month=3"), "Ricetta della torta");

        var page = await visitor.GetStringAsync("/page/chi-sono");
        StringAssert.Contains(page, "Presentazione");
        StringAssert.Contains(await visitor.GetStringAsync("/"), "href=\"/page/Chi-sono\"", "La pagina compare nel menu del tema.");

        var contact = await visitor.GetStringAsync("/contact");
        StringAssert.Contains(contact, "name=\"__RequestVerificationToken\"");

        var missing = await visitor.GetAsync("/post/non-esiste");
        Assert.AreEqual(HttpStatusCode.NotFound, missing.StatusCode);
        StringAssert.Contains(await missing.Content.ReadAsStringAsync(), "error404", "Il 404 usa il tema.");
        Assert.AreEqual(HttpStatusCode.NotFound, (await visitor.GetAsync("/post/bozza-segreta")).StatusCode);

        // Le API continuano a rispondere senza la pagina del tema.
        var api = await visitor.GetAsync("/api/posts/" + Guid.NewGuid());
        Assert.IsFalse((await api.Content.ReadAsStringAsync()).Contains("error404"));
    }

    [TestMethod]
    public async Task Theme_CopiedInDataFolder_IsUsedAndReloadedWhenChanged()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        var folder = Path.Combine(factory.DataRoot, "themes", "Prova");
        Directory.CreateDirectory(folder);
        var layout = Path.Combine(folder, "layout.liquid");
        await File.WriteAllTextAsync(layout, "<html><body>VERSIONE-1 {{ blog.name }} {{ content_for_layout }}</body></html>");
        await File.WriteAllTextAsync(Path.Combine(folder, "style.css"), "body{}");
        factory.Services.GetRequiredService<ThemeCatalog>().Refresh();

        using var visitor = factory.CreateBrowser();
        var first = await visitor.GetStringAsync("/?theme=Prova");
        StringAssert.Contains(first, "VERSIONE-1 Blog di prova");
        StringAssert.Contains(first, "id=\"posts\"", "Il corpo viene dal template predefinito index.liquid.");
        Assert.AreEqual(HttpStatusCode.OK, (await visitor.GetAsync("/Custom/Themes/Prova/style.css")).StatusCode);

        await File.WriteAllTextAsync(layout, "<html><body>VERSIONE-2 {{ content_for_layout }}</body></html>");
        File.SetLastWriteTimeUtc(layout, DateTime.UtcNow.AddMinutes(1));
        StringAssert.Contains(await visitor.GetStringAsync("/?theme=Prova"), "VERSIONE-2");

        // Un tema inesistente non viene mai usato.
        StringAssert.Contains(await visitor.GetStringAsync("/?theme=../../etc"), "blog-header");

        // Il tema attivo si cambia dalle impostazioni.
        await factory.RunAsSystemAsync(async services =>
        {
            var settings = services.GetRequiredService<ISettingsService>();
            var current = await settings.GetAsync();
            current.Theme = "Prova";
            await settings.SaveAsync(current);
        });
        StringAssert.Contains(await visitor.GetStringAsync("/"), "VERSIONE-2");
    }

    [TestMethod]
    public async Task Theme_WithSyntaxError_ShowsErrorPage()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        var folder = Path.Combine(factory.DataRoot, "themes", "Rotto");
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "layout.liquid"), "<html>{% if %}</html>");
        factory.Services.GetRequiredService<ThemeCatalog>().Refresh();

        using var visitor = factory.CreateBrowser();
        var response = await visitor.GetAsync("/?theme=Rotto");

        Assert.AreEqual(HttpStatusCode.InternalServerError, response.StatusCode);
    }

    [TestMethod]
    public async Task WidgetZone_SavedByAdministrator_IsShown()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        await factory.RunAsSystemAsync(async services =>
        {
            var widgets = services.GetRequiredService<BlogEngine.Web.Themes.WidgetService>();
            var textBox = new BlogEngine.Web.Themes.WidgetPlacement { Name = "TextBox", Title = "Benvenuto" };
            await widgets.SaveZoneAsync("sidebar", [textBox, new BlogEngine.Web.Themes.WidgetPlacement { Name = "TagCloud", Title = "Tag" }]);
            await widgets.SaveSettingsAsync(textBox.Id, new Dictionary<string, string> { ["content"] = "<p class=\"saluto\">Ciao a tutti</p>" });
        });

        using var visitor = factory.CreateBrowser();
        var home = await visitor.GetStringAsync("/");

        StringAssert.Contains(home, "<p class=\"saluto\">Ciao a tutti</p>");
        StringAssert.Contains(home, "widget tagcloud");
        Assert.IsFalse(home.Contains("widget categorylist"), "La zona salvata sostituisce i widget predefiniti.");
    }

    [TestMethod]
    public async Task CustomFields_OfThemeAndPost_AreApplied()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());

        // L'amministrazione trova i campi usati dai template del tema, con il valore predefinito.
        var fields = JsonNode.Parse(await admin.GetStringAsync("/api/customfields?filter=" + Uri.EscapeDataString("CustomType == \"THEME\" && ObjectId == \"Standard\"")))!.AsArray();
        var facebook = fields.FirstOrDefault(f => f!["Key"]!.GetValue<string>() == "Facebook Account");
        Assert.IsNotNull(facebook);
        Assert.AreEqual("#/", facebook["Value"]!.GetValue<string>());

        await factory.RunAsSystemAsync(async services =>
        {
            var customFields = services.GetRequiredService<ICustomFieldService>();
            await customFields.SaveAsync(new CustomField { CustomType = CustomField.ThemeType, ObjectId = "Standard", Key = "Facebook Account", Value = "https://facebook.com/mio-blog" });

            var post = new Post { Title = "Campi", Content = "<p>Colore: [CUSTOMFIELD|POST|campi|colore|rosso/] [CUSTOMBLOCK]x[/CUSTOMBLOCK]</p>", Author = "admin" };
            await services.GetRequiredService<IPostService>().SaveAsync(post);
            await customFields.SaveAsync(new CustomField { CustomType = CustomField.PostType, ObjectId = "campi", Key = "colore", Value = "blu" });
        });

        StringAssert.Contains(await admin.GetStringAsync("/"), "href=\"https://facebook.com/mio-blog\"");
        var page = await admin.GetStringAsync("/post/Campi");
        StringAssert.Contains(page, "Colore: blu x");
        Assert.IsFalse(page.Contains("CUSTOMBLOCK"));
    }

    [DataTestMethod]
    [DataRow("Standard")]
    [DataRow("Standard-2017")]
    public async Task Themes_ReferenceOnlyExistingFiles(string theme)
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        var webProject = SourcePaths.WebProject;

        var home = await admin.GetStringAsync($"/?theme={theme}");
        var postUrl = PostLinkRegex().Match(home).Groups["url"].Value;
        var pages = new[] { home, await admin.GetStringAsync($"{postUrl}?theme={theme}"), await admin.GetStringAsync($"/archive?theme={theme}") };

        var assets = pages.SelectMany(p => LocalAssetRegex().Matches(p).Select(m => m.Groups["url"].Value)).Distinct().ToList();
        Assert.IsTrue(assets.Count > 10, string.Join(", ", assets));
        foreach (var url in assets)
        {
            var path = url.Split('?')[0];
            var root = path.StartsWith("/Custom/", StringComparison.Ordinal) ? webProject : Path.Combine(webProject, "wwwroot");
            Assert.IsTrue(ExistsWithExactCase(root, path), $"{theme} usa {path}, che non esiste con questo nome esatto.");
            Assert.AreEqual(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode, url);
        }
    }

    [TestMethod]
    public void ThemeStylesheets_ReferenceExistingFiles()
    {
        var webProject = SourcePaths.WebProject;
        var css = Directory.EnumerateFiles(Path.Combine(webProject, "Custom", "Themes"), "*.css", SearchOption.AllDirectories)
            .Append(Path.Combine(webProject, "wwwroot", "Content", "Auto", "Global.css"));
        foreach (var file in css)
        {
            var folder = Path.GetDirectoryName(file)!;
            foreach (Match match in CssUrlRegex().Matches(File.ReadAllText(file)))
            {
                // Immagine del carosello "owl" già assente nel tema originale (stile non usato).
                if (match.Groups["url"].Value == "owl.video.play.png")
                {
                    continue;
                }

                var target = Path.GetFullPath(Path.Combine(folder, match.Groups["url"].Value.Split('?', '#')[0]));
                var root = target.StartsWith(Path.Combine(webProject, "wwwroot"), StringComparison.OrdinalIgnoreCase) ? Path.Combine(webProject, "wwwroot") : webProject;
                var relative = "/" + Path.GetRelativePath(root, target).Replace('\\', '/');
                Assert.IsTrue(ExistsWithExactCase(root, relative), $"{Path.GetFileName(file)} usa {match.Groups["url"].Value}, che non esiste con questo nome esatto.");
            }
        }
    }

    [GeneratedRegex("""url\(['"]?(?<url>(?!data:|https?:|#)[^'")]+)['"]?\)""")]
    private static partial Regex CssUrlRegex();

    private static bool ExistsWithExactCase(string root, string url)
    {
        var current = root;
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

    [GeneratedRegex(@"(?:src|href)=""(?<url>/(?:Custom|Scripts|Content)/[^""#]+\.(?:js|css|png|jpg|gif|ico|svg)(?:\?[^""]*)?)""")]
    private static partial Regex LocalAssetRegex();

    /// <summary>Invia una richiesta come blog.js: token antifalsificazione letto da BlogEngineRes nella pagina.</summary>
    private static async Task<HttpResponseMessage> PostActionAsync(HttpClient client, string page, string url, Dictionary<string, string> fields)
    {
        var token = Regex.Match(page, "\"antiforgery\":\"(?<token>[^\"]+)\"").Groups["token"].Value;
        Assert.IsFalse(string.IsNullOrEmpty(token), "Token antifalsificazione non trovato nella pagina.");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(fields) };
        request.Headers.Add("RequestVerificationToken", token);
        return await client.SendAsync(request);
    }

    [GeneratedRegex("href=\"(?<url>/post/[^\"#?]+)\"")]
    private static partial Regex PostLinkRegex();

    [GeneratedRegex("<script type=\"application/ld\\+json\">(?<json>.*?)</script>", RegexOptions.Singleline)]
    private static partial Regex JsonLdRegex();
}

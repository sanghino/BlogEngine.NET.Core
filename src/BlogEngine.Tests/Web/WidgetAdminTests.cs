using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BlogEngine.Web.Hosting;
using BlogEngine.Web.Themes;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>Gestione dei widget dall'amministrazione: zone del tema, trascinamento e pagine di modifica.</summary>
[TestClass]
public sealed class WidgetAdminTests
{
    [TestMethod]
    public async Task Zones_AreReadFromThemeAndSaved()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());

        var vm = JsonNode.Parse(await admin.GetStringAsync("/api/widgets"))!;
        var available = vm["AvailableWidgets"]!.AsArray().Select(w => w!["Name"]!.GetValue<string>()).ToList();
        CollectionAssert.IsSubsetOf(new[] { "TagCloud", "TextBox", "LinkList", "Search" }, available);

        var zones = vm["WidgetZones"]!.AsArray();
        Assert.AreEqual(1, zones.Count, "Il tema Standard ha una sola zona.");
        Assert.AreEqual("sidebar", zones[0]!["Id"]!.GetValue<string>());
        Assert.IsTrue(zones[0]!["Widgets"]!.AsArray().Count > 3, "La zona mostra i widget predefiniti del tema.");

        // Come il JavaScript: un widget nuovo arriva con un Id numerico provvisorio.
        var body = """[{"Id":"sidebar","Widgets":[{"Id":"123","Name":"TagCloud","Title":"Nuvola","ShowTitle":false},{"Id":"9","Name":"NonEsiste","Title":"x"}]}]""";
        var saved = await admin.PutAsync("/api/widgets/update/item", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.AreEqual(HttpStatusCode.OK, saved.StatusCode);

        var widgets = JsonNode.Parse(await admin.GetStringAsync("/api/widgets"))!["WidgetZones"]![0]!["Widgets"]!.AsArray();
        Assert.AreEqual(1, widgets.Count, "I widget inesistenti vengono scartati.");
        Assert.IsTrue(Guid.TryParse(widgets[0]!["Id"]!.GetValue<string>(), out _));
        Assert.AreEqual("Nuvola", widgets[0]!["Title"]!.GetValue<string>());

        var home = await admin.GetStringAsync("/");
        StringAssert.Contains(home, "Nuvola");
        Assert.IsFalse(home.Contains("widget categorylist"));

        using var anonymous = factory.CreateBrowser();
        anonymous.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/widgets")).StatusCode);
    }

    [TestMethod]
    public async Task EditPage_SavesSettingsDescribedInManifest()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        var id = Guid.NewGuid().ToString();
        var url = $"/Custom/Widgets/TagCloud/edit.cshtml?id={id}&zone=sidebar";

        var page = await admin.GetStringAsync(url);
        StringAssert.Contains(page, "name=\"minimumposts\"");
        StringAssert.Contains(page, "name=\"tagcloudsize\"");

        var response = await admin.PostFormAsync(url, new() { ["minimumposts"] = "3", ["tagcloudsize"] = "25" });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "toastr.success");

        await factory.RunAsSystemAsync(async services =>
        {
            var settings = await services.GetRequiredService<WidgetService>().GetSettingsAsync(id);
            Assert.AreEqual("3", settings["minimumposts"]);
            Assert.AreEqual("25", settings["tagcloudsize"]);
        });

        // Elenco di link: aggiunta con il pulsante dedicato, gli indirizzi "javascript:" vengono rifiutati.
        var linksUrl = $"/Custom/Widgets/LinkList/edit.cshtml?id={id}x";
        await admin.PostFormAsync(linksUrl, new() { ["linkAction"] = "add", ["linkTitle"] = "Sito", ["linkUrl"] = "https://example.com" });
        await admin.PostFormAsync(linksUrl, new() { ["linkAction"] = "add", ["linkTitle"] = "Cattivo", ["linkUrl"] = "javascript:alert(1)" });
        var links = await admin.GetStringAsync(linksUrl);
        StringAssert.Contains(links, "https://example.com");
        Assert.IsFalse(links.Contains("javascript:alert"));

        // Widget senza impostazioni: l'amministrazione ripiega sulla pagina comune.
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.GetAsync($"/Custom/Widgets/Search/edit.cshtml?id={id}")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await admin.GetAsync("/Custom/Widgets/common.cshtml?id=x")).StatusCode);

        // Solo chi gestisce i widget può aprire la pagina.
        using var anonymous = factory.CreateBrowser(followRedirects: false);
        Assert.AreEqual(HttpStatusCode.Redirect, (await anonymous.GetAsync(url)).StatusCode);
    }

    [TestMethod]
    public async Task Advertisement_InSidebarAndBetweenPostsOfTerminalTheme()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());
        await factory.RunAsSystemAsync(async services =>
        {
            var settingsService = services.GetRequiredService<BlogEngine.Services.ISettingsService>();
            var settings = await settingsService.GetAsync();
            settings.Theme = "Terminal";
            await settingsService.SaveAsync(settings);
        });

        for (var i = 1; i <= 3; i++)
        {
            var post = await admin.PostAsync("/api/posts", new StringContent(
                $$"""{ "Title": "Post numero {{i}}", "Content": "<p>Testo {{i}}</p>", "IsPublished": true, "DateCreated": "2026-01-0{{i}} 10:00" }""",
                Encoding.UTF8, "application/json"));
            Assert.AreEqual(HttpStatusCode.Created, post.StatusCode);
        }

        var zones = JsonNode.Parse(await admin.GetStringAsync("/api/widgets"))!["WidgetZones"]!.AsArray()
            .Select(z => z!["Id"]!.GetValue<string>()).ToList();
        CollectionAssert.AreEquivalent(new[] { "sidebar", "between-posts" }, zones);

        var body = """
            [{"Id":"sidebar","Widgets":[{"Id":"1","Name":"Advertisement","Title":"Pubblicità"}]},
             {"Id":"between-posts","Widgets":[{"Id":"2","Name":"Advertisement","Title":"Pubblicità"}]}]
            """;
        Assert.AreEqual(HttpStatusCode.OK, (await admin.PutAsync("/api/widgets/update/item", new StringContent(body, Encoding.UTF8, "application/json"))).StatusCode);
        var saved = JsonNode.Parse(await admin.GetStringAsync("/api/widgets"))!["WidgetZones"]!.AsArray();
        string IdOf(string zone) => saved.Single(z => z!["Id"]!.GetValue<string>() == zone)!["Widgets"]![0]!["Id"]!.GetValue<string>();

        var feedUrl = $"/Custom/Widgets/Advertisement/edit.cshtml?id={IdOf("between-posts")}&zone=between-posts";
        StringAssert.Contains(await admin.GetStringAsync(feedUrl), "name=\"code\"");
        await admin.PostFormAsync(feedUrl, new() { ["code"] = "<script>window.adFeed = 1;</script><div id=\"ad-feed\">AD</div>", ["show_label"] = "on" });
        await admin.PostFormAsync($"/Custom/Widgets/Advertisement/edit.cshtml?id={IdOf("sidebar")}&zone=sidebar",
            new() { ["code"] = "<div id=\"ad-side\">AD</div>", ["show_label"] = "on", ["hide_for_users"] = "on" });

        using var visitor = factory.CreateBrowser();
        var home = await visitor.GetStringAsync("/");
        StringAssert.Contains(home, "<script>window.adFeed = 1;</script>", "Il codice dell'annuncio arriva intatto, script compresi.");
        StringAssert.Contains(home, "id=\"ad-side\"");
        var ad = home.IndexOf("id=\"ad-feed\"", StringComparison.Ordinal);
        // Ordine dal più recente: benvenuto (oggi), 3, 2, 1. L'annuncio sta tra il terzo e il quarto.
        Assert.IsTrue(ad > home.IndexOf("Post numero 2", StringComparison.Ordinal), "L'annuncio segue il terzo post.");
        Assert.IsTrue(ad < home.IndexOf("Post numero 1", StringComparison.Ordinal), "...e precede il quarto.");

        var asAdmin = await admin.GetStringAsync("/");
        StringAssert.Contains(asAdmin, "id=\"ad-feed\"");
        Assert.IsFalse(asAdmin.Contains("id=\"ad-side\""), "Annuncio nascosto agli utenti collegati.");
    }
}

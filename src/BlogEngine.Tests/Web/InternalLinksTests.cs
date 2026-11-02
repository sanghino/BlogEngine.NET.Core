using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using BlogEngine.Web.Hosting;

namespace BlogEngine.Tests.Web;

/// <summary>Ricerca di post e pagine per il pulsante "Sfoglia" della finestra "Inserisci link" dell'editor.</summary>
[TestClass]
public sealed class InternalLinksTests
{
    [TestMethod]
    public async Task Search_FindsPublishedPostsAndPages()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());

        async Task Create(string api, string json) =>
            Assert.AreEqual(HttpStatusCode.Created, (await admin.PostAsync(api, new StringContent(json, Encoding.UTF8, "application/json"))).StatusCode);

        await Create("/api/posts", """{ "Title": "Festa di Città a Sesto", "Content": "<p>x</p>", "IsPublished": true }""");
        await Create("/api/posts", """{ "Title": "Festa segreta", "Content": "<p>x</p>", "IsPublished": false }""");
        await Create("/api/pages", """{ "Title": "Chi siamo e la festa", "Content": "<p>x</p>", "IsPublished": true }""");

        // Senza maiuscole e senza accenti; tutte le parole devono comparire nel titolo.
        var found = JsonNode.Parse(await admin.GetStringAsync("/api/links?q=festa%20citta"))!.AsArray();
        Assert.AreEqual(1, found.Count);
        Assert.AreEqual("post", found[0]!["Type"]!.GetValue<string>());
        Assert.AreEqual("Festa di Città a Sesto", found[0]!["Title"]!.GetValue<string>());
        var url = found[0]!["Url"]!.GetValue<string>();
        Assert.AreEqual(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode, "Il link porta al post.");

        // Le pagine vengono prima dei post; le bozze non compaiono.
        var titles = JsonNode.Parse(await admin.GetStringAsync("/api/links?q=FESTA"))!.AsArray()
            .Select(i => i!["Title"]!.GetValue<string>()).ToList();
        CollectionAssert.AreEqual(new[] { "Chi siamo e la festa", "Festa di Città a Sesto" }, titles);

        // Senza testo: i contenuti più recenti, nel limite richiesto.
        Assert.AreEqual(2, JsonNode.Parse(await admin.GetStringAsync("/api/links?take=2"))!.AsArray().Count);

        using var anonymous = factory.CreateBrowser();
        anonymous.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/links")).StatusCode);
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Web.Protocols;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>Feed, documenti XML, redirect dai vecchi indirizzi e protocolli tra blog (pingback, trackback).</summary>
[TestClass]
public sealed class ProtocolTests
{
    [TestMethod]
    public async Task Feeds_RssAndAtom_WithCategoriesCommentsAndConditionalGet()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        await factory.RunAsSystemAsync(async services =>
        {
            var post = new Post { Title = "Notizia & novità", Content = "<p>Vedi <a href=\"/page/info\">qui</a> <img src=\"/image.axd?picture=/a.png\" /></p>", Author = "admin", Tags = ["dotnet"] };
            await services.GetRequiredService<IPostService>().SaveAsync(post);
            var settings = services.GetRequiredService<ISettingsService>();
            var blogSettings = await settings.GetAsync();
            blogSettings.EnableCommentsModeration = false;
            blogSettings.EnableTagExport = true;
            await settings.SaveAsync(blogSettings);
            await services.GetRequiredService<ICommentService>().AddAsync(post, new Comment { Author = "Lettore", Email = "l@example.com", Content = "Bel post" });
        });
        using var reader = factory.CreateBrowser();

        var rss = await reader.GetAsync("/syndication.axd");
        Assert.AreEqual("application/rss+xml", rss.Content.Headers.ContentType?.MediaType);
        var document = XDocument.Parse(await rss.Content.ReadAsStringAsync());
        var item = document.Descendants("item").First(i => i.Element("title")?.Value == "Notizia & novità");
        StringAssert.StartsWith(item.Element("link")!.Value, "http://localhost/post/");
        StringAssert.Contains(item.Element("description")!.Value, "href=\"http://localhost/page/info\"", "Indirizzi assoluti nel testo.");
        StringAssert.Contains(item.Element("description")!.Value, "src=\"http://localhost/image.axd");
        Assert.AreEqual("dotnet", item.Element(XName.Get("tag", "https://blogengine.io/schemas/tags"))!.Value);
        Assert.AreEqual("1", item.Element(XName.Get("comments", "http://purl.org/rss/1.0/modules/slash/"))!.Value);

        // Richiesta condizionale: nessuna novità, nessun contenuto.
        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/syndication.axd");
        conditional.Headers.IfModifiedSince = rss.Content.Headers.LastModified ?? DateTimeOffset.Parse(rss.Headers.GetValues("Last-Modified").First());
        Assert.AreEqual(HttpStatusCode.NotModified, (await reader.SendAsync(conditional)).StatusCode);

        var atom = XDocument.Parse(await reader.GetStringAsync("/syndication.axd?format=atom"));
        Assert.AreEqual("feed", atom.Root!.Name.LocalName);
        Assert.IsTrue(atom.Root.Elements(XName.Get("entry", "http://www.w3.org/2005/Atom")).Count() >= 2);

        var comments = XDocument.Parse(await reader.GetStringAsync("/syndication.axd?comments=true"));
        Assert.AreEqual("Lettore on Notizia & novità", comments.Descendants("item").Single().Element("title")!.Value);

        var category = XDocument.Parse(await reader.GetStringAsync("/category/feed/General"));
        StringAssert.Contains(category.Descendants("channel").Single().Element("title")!.Value, "General");

        Assert.AreEqual(HttpStatusCode.NotFound, (await reader.GetAsync("/syndication.axd?category=" + Guid.NewGuid())).StatusCode);
    }

    [DataTestMethod]
    [DataRow("/sitemap.axd", "urlset")]
    [DataRow("/rsd.axd", "rsd")]
    [DataRow("/opensearch.axd", "OpenSearchDescription")]
    [DataRow("/opml.axd", "opml")]
    [DataRow("/apml.axd", "APML")]
    [DataRow("/foaf.axd", "RDF")]
    [DataRow("/sioc.axd", "RDF")]
    public async Task SemanticDocuments_AreValidXml(string url, string root)
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        using var reader = factory.CreateBrowser();

        var response = await reader.GetAsync(url);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, url);
        var document = XDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(root, document.Root!.Name.LocalName);
        if (url == "/sitemap.axd")
        {
            StringAssert.Contains(document.ToString(), "http://localhost/post/");
        }
    }

    [TestMethod]
    public async Task Robots_PointsToSitemap()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        StringAssert.Contains(await factory.CreateBrowser().GetStringAsync("/robots.txt"), "Sitemap: http://localhost/sitemap.axd");
    }

    [DataTestMethod]
    [DataRow("/post/Ciao.aspx", "/post/Ciao")]
    [DataRow("/default.aspx?tag=net", "/?tag=net")]
    [DataRow("/2025/03/default.aspx", "/2025/03")]
    [DataRow("/archive.aspx", "/archive")]
    [DataRow("/search.aspx?q=prova", "/search?q=prova")]
    [DataRow("/Account/Login.aspx?ReturnUrl=%2Fadmin%2F", "/account/login?ReturnUrl=%2Fadmin%2F")]
    [DataRow("/calendar/default.aspx", "/calendar")]
    public async Task OldAspxAddresses_RedirectPermanently(string oldUrl, string newUrl)
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        using var browser = factory.CreateBrowser(followRedirects: false);

        var response = await browser.GetAsync(oldUrl);

        Assert.AreEqual(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.AreEqual(newUrl, response.Headers.Location?.OriginalString);
    }

    [TestMethod]
    public async Task OldPostAddressWithId_ReachesThePost()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        Guid id = default;
        await factory.RunAsSystemAsync(async services => id = (await services.GetRequiredService<IPostService>().GetPostsAsync()).First().Id);

        var page = await factory.CreateBrowser().GetAsync($"/post.aspx?id={id}");

        Assert.AreEqual(HttpStatusCode.OK, page.StatusCode);
        StringAssert.StartsWith(page.RequestMessage!.RequestUri!.AbsolutePath, "/post/Welcome");
    }

    [TestMethod]
    public async Task Pingback_IsVerifiedAndSavedOnce()
    {
        var remote = new FakeRemotePages();
        using var factory = new BlogWebFactory { TestServices = s => s.AddSingleton<IRemotePages>(remote) };
        (await factory.InstallAsync()).Dispose();
        var (postUrl, postId) = await FirstPostAsync(factory);
        remote.Pages["https://altro-blog.example/articolo"] = $"<html><head><title>Un articolo che ti cita</title></head><body><a href=\"{postUrl}\">post</a></body></html>";
        remote.Pages["https://spam.example/"] = "<html><body>niente link</body></html>";
        using var client = factory.CreateBrowser();

        var ok = await PingAsync(client, "https://altro-blog.example/articolo", postUrl);
        StringAssert.Contains(ok, "registered");
        Assert.IsFalse(ok.Contains("<fault>"));

        StringAssert.Contains(await PingAsync(client, "https://altro-blog.example/articolo", postUrl), "<int>48</int>", "Un secondo pingback uguale viene rifiutato.");
        StringAssert.Contains(await PingAsync(client, "https://spam.example/", postUrl), "<int>17</int>", "La pagina d'origine deve contenere il link.");
        StringAssert.Contains(await PingAsync(client, "https://altro-blog.example/articolo", "http://localhost/post/non-esiste"), "<int>33</int>");

        await factory.RunAsSystemAsync(async services =>
        {
            var post = await services.GetRequiredService<IPostService>().GetByIdAsync(postId);
            var pingback = post!.AllComments.Single(c => c.IsPingbackOrTrackback);
            Assert.AreEqual(Comment.PingbackEmail, pingback.Email);
            Assert.AreEqual("Un articolo che ti cita", pingback.Author);
        });
    }

    [TestMethod]
    public async Task Trackback_IsVerifiedAndSaved()
    {
        var remote = new FakeRemotePages();
        using var factory = new BlogWebFactory { TestServices = s => s.AddSingleton<IRemotePages>(remote) };
        (await factory.InstallAsync()).Dispose();
        var (postUrl, postId) = await FirstPostAsync(factory);
        remote.Pages["https://altro-blog.example/tb"] = $"<a href=\"{postUrl}\">link</a>";
        using var client = factory.CreateBrowser();

        var response = await client.PostAsync($"/trackback.axd?id={postId}", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["title"] = "Risposta", ["excerpt"] = "<b>Ne parlo anche io</b>", ["url"] = "https://altro-blog.example/tb", ["blog_name"] = "Altro blog",
        }));
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "<error>0</error>");

        var missing = await client.PostAsync($"/trackback.axd?id={Guid.NewGuid()}", new FormUrlEncodedContent(new Dictionary<string, string> { ["url"] = "https://altro-blog.example/tb" }));
        StringAssert.Contains(await missing.Content.ReadAsStringAsync(), "<error>1</error>");

        await factory.RunAsSystemAsync(async services =>
        {
            var trackback = (await services.GetRequiredService<IPostService>().GetByIdAsync(postId))!.AllComments.Single(c => c.IsPingbackOrTrackback);
            Assert.AreEqual("Altro blog", trackback.Author);
            StringAssert.Contains(trackback.Content, "Ne parlo anche io");
        });
    }

    [TestMethod]
    public async Task LinkbackSender_UsesPingbackServerOrTrackbackRdf()
    {
        var remote = new FakeRemotePages();
        remote.Pages["https://uno.example/a"] = "<html><head><link rel=\"pingback\" href=\"https://uno.example/xmlrpc\" /></head></html>";
        remote.Pages["https://due.example/b"] = "<rdf:RDF><rdf:Description rdf:about=\"https://due.example/b\" trackback:ping=\"https://due.example/tb/1\" /></rdf:RDF>";
        var sender = new LinkbackSender(remote, Microsoft.Extensions.Logging.Abstractions.NullLogger<LinkbackSender>.Instance);
        var source = new LinkbackSource(new Uri("https://mio.example/post/x"), "Titolo", "Riassunto", "Mio blog",
            "<a href=\"https://uno.example/a\">uno</a> <a href=\"https://due.example/b\">due</a> <a href=\"https://mio.example/altro\">interno</a>");

        await sender.SendAsync(source, pingbacks: true, trackbacks: true, CancellationToken.None);

        var pingback = remote.Posts.Single(p => p.Url == "https://uno.example/xmlrpc");
        StringAssert.Contains(pingback.Body, "<methodName>pingback.ping</methodName>");
        StringAssert.Contains(pingback.Body, "https://mio.example/post/x");
        var trackback = remote.Posts.Single(p => p.Url == "https://due.example/tb/1");
        StringAssert.Contains(trackback.Body, "blog_name=Mio+blog");
        Assert.IsFalse(remote.Requested.Contains("https://mio.example/altro"), "I link interni non vengono notificati.");
    }

    [TestMethod]
    public void RemotePages_RejectPrivateAddresses()
    {
        Assert.IsFalse(RemotePages.IsPublic(IPAddress.Loopback));
        Assert.IsFalse(RemotePages.IsPublic(IPAddress.Parse("10.1.2.3")));
        Assert.IsFalse(RemotePages.IsPublic(IPAddress.Parse("192.168.0.1")));
        Assert.IsFalse(RemotePages.IsPublic(IPAddress.Parse("169.254.169.254")));
        Assert.IsFalse(RemotePages.IsPublic(IPAddress.IPv6Loopback));
        Assert.IsTrue(RemotePages.IsPublic(IPAddress.Parse("93.184.216.34")));
    }

    [TestMethod]
    public async Task WwwReferrersAndCompression_FollowTheSettings()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        await factory.RunAsSystemAsync(async services =>
        {
            var settings = services.GetRequiredService<ISettingsService>();
            var blogSettings = await settings.GetAsync();
            blogSettings.HandleWwwSubdomain = "remove";
            blogSettings.EnableReferrerTracking = true;
            blogSettings.EnableHttpCompression = true;
            await settings.SaveAsync(blogSettings);
        });

        using var browser = factory.CreateBrowser(followRedirects: false);
        using var www = new HttpRequestMessage(HttpMethod.Get, "/archive");
        www.Headers.Host = "www.example.com";
        var redirect = await browser.SendAsync(www);
        Assert.AreEqual(HttpStatusCode.MovedPermanently, redirect.StatusCode);
        Assert.AreEqual("http://example.com/archive", redirect.Headers.Location?.ToString());

        using var fromGoogle = new HttpRequestMessage(HttpMethod.Get, "/");
        fromGoogle.Headers.Referrer = new Uri("https://www.google.com/search?q=blog");
        fromGoogle.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        var home = await browser.SendAsync(fromGoogle);
        Assert.AreEqual("gzip", home.Content.Headers.ContentEncoding.Single());

        await factory.RunAsSystemAsync(async services =>
        {
            var days = await services.GetRequiredService<IReferrerService>().GetByDayAsync();
            Assert.IsTrue(days.SelectMany(d => d.Value).Any(r => r.ReferrerUrl?.Host == "www.google.com"));
        });
    }

    private static async Task<(string Url, Guid Id)> FirstPostAsync(BlogWebFactory factory)
    {
        (string, Guid) result = default;
        await factory.RunAsSystemAsync(async services =>
        {
            var post = (await services.GetRequiredService<IPostService>().GetPostsAsync()).First();
            var links = services.GetRequiredService<ILinkGenerator>();
            result = (links.ToAbsolute(await links.PostAsync(post)).ToString(), post.Id);
        });
        return result;
    }

    private static async Task<string> PingAsync(HttpClient client, string source, string target)
    {
        var body = $"<?xml version=\"1.0\"?><methodCall><methodName>pingback.ping</methodName><params><param><value><string>{source}</string></value></param><param><value><string>{target}</string></value></param></params></methodCall>";
        var response = await client.PostAsync("/pingback.axd", new StringContent(body, Encoding.UTF8, "text/xml"));
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Siti esterni simulati: nessuna richiesta esce davvero in rete.</summary>
    private sealed class FakeRemotePages : IRemotePages
    {
        public ConcurrentDictionary<string, string> Pages { get; } = new();

        public ConcurrentBag<string> Requested { get; } = [];

        public ConcurrentBag<(string Url, string Body)> Posts { get; } = [];

        public Task<RemotePage?> GetAsync(Uri url, CancellationToken cancellationToken)
        {
            Requested.Add(url.ToString());
            if (!Pages.TryGetValue(url.ToString(), out var html))
            {
                return Task.FromResult<RemotePage?>(null);
            }

            using var response = new HttpResponseMessage();
            return Task.FromResult<RemotePage?>(new RemotePage(html, response.Headers, url));
        }

        public async Task<string?> PostAsync(Uri url, HttpContent content, CancellationToken cancellationToken)
        {
            Posts.Add((url.ToString(), WebUtility.UrlDecode(await content.ReadAsStringAsync(cancellationToken)).Replace(' ', '+')));
            return "<response><error>0</error></response>";
        }
    }
}

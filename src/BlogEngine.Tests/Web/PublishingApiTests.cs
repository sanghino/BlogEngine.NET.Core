using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Web.Hosting;
using BlogEngine.Web.Protocols;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>MetaWeblog (Open Live Writer), BlogML e pagina di diagnostica.</summary>
[TestClass]
public sealed class PublishingApiTests
{
    private const string User = BlogWebFactory.AdminUser;
    private const string Password = BlogWebFactory.AdminPassword;

    [TestMethod]
    public async Task MetaWeblog_FullLiveWriterSession()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        using var writer = factory.CreateBrowser();

        // Configurazione dell'account in Live Writer: blog dell'utente, categorie, manifest.
        var blogs = await CallAsync(writer, "blogger.getUsersBlogs", S("key"), S(User), S(Password));
        Assert.AreEqual("http://localhost/", Member(blogs.Descendants("struct").First(), "blogid"));
        StringAssert.Contains(await writer.GetStringAsync("/wlwmanifest.xml"), "<supportsPages>Yes</supportsPages>");
        StringAssert.Contains(await writer.GetStringAsync("/rsd.axd"), "metaweblog.axd");

        var wrong = await CallAsync(writer, "metaWeblog.getCategories", S("1"), S(User), S("sbagliata"));
        StringAssert.Contains(wrong.ToString(), "<int>11</int>");

        // Nuovo post con categoria nuova, tag, slug e data.
        var postStruct = Struct(
            ("title", S("Scritto con Live Writer")),
            ("description", S("<p>Testo dal programma</p>")),
            ("categories", "<array><data><value><string>Desktop</string></value></data></array>"),
            ("mt_keywords", S("live, writer")),
            ("wp_slug", S("da-live-writer")),
            ("dateCreated", "<dateTime.iso8601>20250309T10:30:00</dateTime.iso8601>"),
            ("mt_allow_comments", "<int>1</int>"));
        var created = await CallAsync(writer, "metaWeblog.newPost", S("1"), S(User), S(Password), postStruct, "<boolean>1</boolean>");
        var postId = created.Descendants("string").Single().Value;
        Assert.IsTrue(Guid.TryParse(postId, out _), created.ToString());

        var post = await CallAsync(writer, "metaWeblog.getPost", S(postId), S(User), S(Password));
        var data = post.Descendants("struct").First();
        Assert.AreEqual("Scritto con Live Writer", Member(data, "title"));
        Assert.AreEqual("live,writer", Member(data, "mt_keywords"));
        Assert.AreEqual("da-live-writer", Member(data, "wp_slug"));
        Assert.AreEqual("Desktop", data.Descendants("array").Single().Descendants("string").Single().Value);
        StringAssert.Contains(await writer.GetStringAsync("/post/da-live-writer"), "Testo dal programma");

        // Immagine inserita nel post.
        var image = Convert.ToBase64String(Encoding.UTF8.GetBytes("finta-immagine"));
        var media = await CallAsync(writer, "metaWeblog.newMediaObject", S("1"), S(User), S(Password),
            Struct(("name", S("Open-Live-Writer/foto.png")), ("type", S("image/png")), ("bits", $"<base64>{image}</base64>")));
        StringAssert.StartsWith(Member(media.Descendants("struct").First(), "url"), "http://localhost/image.axd?picture=");

        // Modifica, elenco recente, eliminazione.
        var edited = Struct(("title", S("Titolo corretto")), ("description", S("<p>Nuovo testo</p>")));
        StringAssert.Contains((await CallAsync(writer, "metaWeblog.editPost", S(postId), S(User), S(Password), edited, "<boolean>1</boolean>")).ToString(), "<boolean>1</boolean>");
        var recent = await CallAsync(writer, "metaWeblog.getRecentPosts", S("1"), S(User), S(Password), "<int>10</int>");
        Assert.IsTrue(recent.Descendants("member").Any(m => m.Element("name")?.Value == "title" && m.Descendants("string").First().Value == "Titolo corretto"));

        await CallAsync(writer, "blogger.deletePost", S("key"), S(postId), S(User), S(Password), "<boolean>1</boolean>");
        await factory.RunAsSystemAsync(async services =>
            Assert.IsTrue((await services.GetRequiredService<IPostService>().GetByIdAsync(Guid.Parse(postId)))!.IsDeleted, "Il post va nel cestino."));

        // Pagine e tag.
        var page = await CallAsync(writer, "wp.newPage", S("1"), S(User), S(Password), Struct(("title", S("Chi siamo")), ("description", S("<p>Noi</p>"))), "<boolean>1</boolean>");
        var pageId = page.Descendants("string").Single().Value;
        var pages = await CallAsync(writer, "wp.getPages", S("1"), S(User), S(Password));
        Assert.IsTrue(pages.Descendants("member").Any(m => m.Element("name")?.Value == "page_id" && m.Descendants("string").First().Value == pageId));
        // I tag "live" e "writer" erano solo nel post cestinato: l'elenco è vuoto ma valido.
        Assert.IsNotNull((await CallAsync(writer, "wp.getTags", S("1"), S(User), S(Password))).Descendants("array").SingleOrDefault());
    }

    [TestMethod]
    public async Task MetaWeblog_RespectsRolesAndUnknownMethods()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        await factory.RunAsSystemAsync(services =>
            services.GetRequiredService<BlogEngine.Core.Services.IUserService>().CreateUserAsync("lettore", "lettore@example.com", "Password-Lettore-1", []));
        using var writer = factory.CreateBrowser();

        var denied = await CallAsync(writer, "metaWeblog.newPost", S("1"), S("lettore"), S("Password-Lettore-1"), Struct(("title", S("x")), ("description", S("y"))), "<boolean>1</boolean>");
        StringAssert.Contains(denied.ToString(), "<int>11</int>", "Un utente senza il permesso di scrivere non crea post.");

        StringAssert.Contains((await CallAsync(writer, "metodo.inesistente", S("1"), S(User), S(Password))).ToString(), "<fault>");
    }

    [TestMethod]
    public async Task BlogML_ExportThenImportIntoAnotherBlog()
    {
        string xml;
        using (var source = new BlogWebFactory())
        {
            using var admin = await source.InstallAsync();
            await source.RunAsSystemAsync(async services =>
            {
                var posts = services.GetRequiredService<IPostService>();
                var post = new Post { Title = "Da esportare", Content = "<p>Contenuto <img src=\"https://immagini.example/foto.jpg\" /></p>", Author = "admin", Tags = ["storia"] };
                await posts.SaveAsync(post);
                await services.GetRequiredService<ICommentService>().AddAsync(post, new Comment { Author = "Mario", Email = "m@example.com", Content = "Primo!" });
            });

            using var anonymous = source.CreateBrowser();
            Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/blogml.axd")).StatusCode, "Solo gli amministratori esportano.");
            var export = await admin.GetAsync("/blogml.axd");
            Assert.AreEqual(HttpStatusCode.OK, export.StatusCode);
            xml = await export.Content.ReadAsStringAsync();
        }

        var document = XDocument.Parse(xml);
        var ns = BlogMLService.Ns;
        Assert.IsTrue(document.Descendants(ns + "post").Any(p => p.Element(ns + "title")?.Value == "Da esportare"));

        var remote = new ProtocolTests.FakeRemotePages();
        remote.Files["https://immagini.example/foto.jpg"] = Encoding.UTF8.GetBytes("jpeg");
        using var target = new BlogWebFactory { TestServices = s => s.AddSingleton<IRemotePages>(remote) };
        using var targetAdmin = await target.InstallAsync(blogName: "Nuovo blog");
        targetAdmin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());

        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(Encoding.UTF8.GetBytes(xml));
        file.Headers.ContentType = new MediaTypeHeaderValue("text/xml");
        form.Add(file, "file", "blogml.xml");
        var import = await targetAdmin.PostAsync("/api/upload?action=import", form);
        var result = JsonNode.Parse(await import.Content.ReadAsStringAsync())!;
        Assert.AreEqual(HttpStatusCode.OK, import.StatusCode);
        Assert.AreEqual(2, result["Posts"]!.GetValue<int>(), "Il post di benvenuto e quello nuovo.");
        Assert.AreEqual(1, result["DownloadedFiles"]!.GetValue<int>());

        await target.RunAsSystemAsync(async services =>
        {
            var imported = (await services.GetRequiredService<IPostService>().GetPostsAsync()).Single(p => p.Title == "Da esportare");
            CollectionAssert.AreEqual(new[] { "storia" }, imported.Tags);
            Assert.AreEqual("Primo!", imported.AllComments.Single().Content);
            StringAssert.Contains(imported.Content, "image.axd?picture=", "L'immagine remota è stata copiata sul sito.");
        });
    }

    [TestMethod]
    public async Task Diagnostics_ShowsWritableFolders()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();

        var page = await admin.GetStringAsync("/admin/diagnostics");

        StringAssert.Contains(page, "Writable: App_Data");
        StringAssert.Contains(page, ".NET 8");
        Assert.IsFalse(page.Contains("fa-exclamation-triangle text-danger\"></i>\n                            <span>Scrittura"), "Le cartelle dei dati sono scrivibili.");

        using var anonymous = factory.CreateBrowser(followRedirects: false);
        Assert.AreEqual(HttpStatusCode.Redirect, (await anonymous.GetAsync("/admin/diagnostics")).StatusCode);
    }

    private static string S(string value) => $"<string>{WebUtility.HtmlEncode(value)}</string>";

    private static string Struct(params (string Name, string Value)[] members) =>
        "<struct>" + string.Concat(members.Select(m => $"<member><name>{m.Name}</name><value>{m.Value}</value></member>")) + "</struct>";

    private static string Member(XElement data, string name) =>
        data.Elements("member").Single(m => m.Element("name")!.Value == name).Element("value")!.Value;

    private static async Task<XDocument> CallAsync(HttpClient client, string method, params string[] parameters)
    {
        var body = $"<?xml version=\"1.0\"?><methodCall><methodName>{method}</methodName><params>{string.Concat(parameters.Select(p => $"<param><value>{p}</value></param>"))}</params></methodCall>";
        var response = await client.PostAsync("/metaweblog.axd", new StringContent(body, Encoding.UTF8, "text/xml"));
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return XDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}

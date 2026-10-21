using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlogEngine.Core.Extensions;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>Estensioni compilate all'avvio dai sorgenti di Custom/Extensions e App_Data/extensions.</summary>
[TestClass]
public sealed partial class ExtensionTests
{
    private static readonly string[] Bundled =
    [
        "AkismetFilter", "BBCode", "BreakPost", "MediaElementPlayer", "Recaptcha", "ResolveLinks",
        "SendCommentMail", "SendNewsletters", "SendPings", "SimpleCaptcha", "Smilies", "StopForumSpam",
    ];

    [TestMethod]
    public void BundledExtensions_CompileWithoutErrors()
    {
        using var factory = new BlogWebFactory();
        var registry = factory.Services.GetRequiredService<ExtensionRegistry>();

        foreach (var name in Bundled)
        {
            var extension = registry.Find(name);
            Assert.IsNotNull(extension, $"Estensione {name} non trovata.");
            Assert.IsNull(extension.Error, $"{name}: {extension.Error}");
        }

        Assert.IsTrue(Directory.EnumerateFiles(Path.Combine(Path.GetTempPath(), "be-tests-extensions"), "BBCode.*.dll").Any(),
            "Gli assembly compilati vanno nella cache.");
    }

    [TestMethod]
    public async Task TextExtensions_ChangePostsAndComments_AndCanBeDisabledWithoutRestart()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());

        await factory.RunAsSystemAsync(async services =>
        {
            var posts = services.GetRequiredService<IPostService>();
            var post = new Post
            {
                Title = "Estensioni",
                Author = "admin",
                Content = "<p>Inizio del post</p><p><strong>prima [more] dopo</strong></p><p>[video src=\"film.mp4\" width=\"320\"]</p>",
            };
            await posts.SaveAsync(post);
            await services.GetRequiredService<ICommentService>().AddAsync(post, new Comment
            {
                Author = "Lettore",
                Email = "lettore@example.com",
                Content = "[b]grassetto[/b] :) vedi www.example.com/pagina",
            });
        });

        // BreakPost: nell'elenco il testo si ferma a [more] e i tag aperti vengono chiusi
        // (con Standard-2017, che nell'elenco mostra il testo dei post e non la descrizione).
        var home = await admin.GetStringAsync("/?theme=Standard-2017");
        StringAssert.Contains(home, "<strong>prima <a class=\"more\" href=\"/post/Estensioni#continue\">");
        Assert.IsFalse(home.Contains("dopo</strong>"));

        var page = await admin.GetStringAsync("/post/Estensioni");
        Assert.IsFalse(page.Contains("[more]"));

        // MediaElementPlayer: tag HTML5 e script del lettore nell'intestazione.
        StringAssert.Contains(page, "<video class=\"mep\" controls=\"controls\" src=\"/media/film.mp4\" width=\"320\">");
        StringAssert.Contains(page, "/Scripts/mediaelement/mediaelementplayer.min.js");
        StringAssert.Contains(page, ".mediaelementplayer(");

        // BBCode, Smilies e ResolveLinks sul commento.
        StringAssert.Contains(page, "<strong>grassetto</strong>");
        StringAssert.Contains(page, "smiley-smile.gif");
        StringAssert.Contains(page, "<a href=\"http://www.example.com/pagina\" rel=\"nofollow\">");

        // Disattivazione dall'amministrazione: effetto immediato.
        var disable = await admin.PutAsync("/api/packages/processchecked/disable", Json("""[ { "Id": "BBCode", "IsChecked": true }, { "Id": "Smilies", "IsChecked": true } ]"""));
        Assert.AreEqual(HttpStatusCode.OK, disable.StatusCode);
        page = await admin.GetStringAsync("/post/Estensioni");
        StringAssert.Contains(page, "[b]grassetto[/b]");
        Assert.IsFalse(page.Contains("smiley-smile.gif"));

        var list = JsonNode.Parse(await admin.GetStringAsync("/api/packages?filter=extensions&take=0"))!.AsArray();
        var bbcode = list.Single(p => p!["Id"]!.GetValue<string>() == "BBCode")!;
        Assert.IsFalse(bbcode["Enabled"]!.GetValue<bool>());
        StringAssert.Contains(bbcode["SettingsUrl"]!.GetValue<string>(), "admin/extensions/settings?ext=BBCode");
        Assert.IsTrue(list.Single(p => p!["Id"]!.GetValue<string>() == "AkismetFilter")!["Enabled"]!.GetValue<bool>() == false, "I filtri che richiedono una chiave partono disattivati.");

        // Lo stato viene salvato: dopo un riavvio (nuovo stato caricato dai dati) resta disattivata.
        var states = factory.Services.GetRequiredService<ExtensionStates>();
        await states.LoadAsync();
        Assert.IsFalse(states.IsEnabled("BBCode"));

        await admin.PutAsync("/api/packages/processchecked/enable", Json("""[ { "Id": "BBCode", "IsChecked": true } ]"""));
        StringAssert.Contains(await admin.GetStringAsync("/post/Estensioni"), "<strong>grassetto</strong>");
    }

    [TestMethod]
    public async Task SettingsPage_EditsScalarValuesAndTables()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        using var visitor = factory.CreateBrowser();

        // Valori singoli: nuova domanda e nuova risposta per SimpleCaptcha.
        var url = "/admin/extensions/settings?ext=SimpleCaptcha";
        StringAssert.Contains(await admin.GetStringAsync(url), "name=\"SimpleCaptcha.CaptchaAnswer\"");
        var saved = await admin.PostFormAsync(url + "&handler=Save&group=SimpleCaptcha", new()
        {
            ["SimpleCaptcha.CaptchaLabel"] = "Capitale d'Italia?",
            ["SimpleCaptcha.CaptchaAnswer"] = "Roma",
        });
        StringAssert.Contains(await saved.Content.ReadAsStringAsync(), "toastr.success");

        var postUrl = PostLinkRegex().Match(await visitor.GetStringAsync("/")).Groups["url"].Value;
        StringAssert.Contains(await visitor.GetStringAsync(postUrl), "Capitale d&#39;Italia?");

        // Tabelle: una riga aggiunta a BBCode, con controllo dei duplicati.
        var bbcode = "/admin/extensions/settings?ext=BBCode";
        await admin.PostFormAsync(bbcode + "&handler=Add&group=BBCode", new() { ["BBCode.Code"] = "s", ["BBCode.OpenTag"] = "del", ["BBCode.CloseTag"] = "" });
        var duplicate = await admin.PostFormAsync(bbcode + "&handler=Add&group=BBCode", new() { ["BBCode.Code"] = "s", ["BBCode.OpenTag"] = "del" });
        StringAssert.Contains(await duplicate.Content.ReadAsStringAsync(), "già presente");

        await factory.RunAsSystemAsync(async services =>
        {
            var rows = (await services.GetRequiredService<BlogEngine.Extensions.IExtensionSettingsService>().GetAsync("BBCode")).Rows;
            Assert.AreEqual(5, rows.Count);
            Assert.AreEqual("del", rows[^1]["OpenTag"]);
        });

        // Solo chi gestisce le estensioni può aprire la pagina.
        using var anonymous = factory.CreateBrowser(followRedirects: false);
        Assert.AreEqual(HttpStatusCode.Redirect, (await anonymous.GetAsync(bbcode)).StatusCode);
    }

    [TestMethod]
    public async Task ExtensionCopiedInDataFolder_IsCompiled_AndBrokenOneDoesNotStopTheSite()
    {
        using var factory = new BlogWebFactory();
        var good = Path.Combine(factory.DataRoot, "extensions", "Firma");
        Directory.CreateDirectory(good);
        await File.WriteAllTextAsync(Path.Combine(good, "Firma.cs"), """
            [Extension("Aggiunge una firma ai post", "1.0", "Test")]
            public sealed class Firma : IBlogExtension
            {
                public void ConfigureServices(IServiceCollection services) =>
                    services.AddScoped<IEventHandler<ServingEvent<Post>>, FirmaHandler>();

                public IEnumerable<ExtensionSettingsDefinition> Settings =>
                [
                    new ExtensionSettingsDefinition("Firma") { IsScalar = true }.AddParameter("Testo").SetDefault("Testo", "-- firmato"),
                ];
            }

            public sealed class FirmaHandler(IExtensionSettingsService settings) : IEventHandler<ServingEvent<Post>>
            {
                public async Task HandleAsync(ServingEvent<Post> e, CancellationToken cancellationToken) =>
                    e.Body += "<p class=\"firma\">" + (await settings.GetAsync("Firma")).Get("Testo") + "</p>";
            }
            """);

        var broken = Path.Combine(factory.DataRoot, "extensions", "Rotta");
        Directory.CreateDirectory(broken);
        await File.WriteAllTextAsync(Path.Combine(broken, "Rotta.cs"), "public class Rotta : IBlogExtension { questo non è C# }");

        using var admin = await factory.InstallAsync();
        var registry = factory.Services.GetRequiredService<ExtensionRegistry>();
        Assert.IsNull(registry.Find("Firma")!.Error);
        Assert.IsTrue(registry.Find("Firma")!.IsUploaded);
        StringAssert.Contains(registry.Find("Rotta")!.Error, "Rotta.cs(1)");

        var postUrl = PostLinkRegex().Match(await admin.GetStringAsync("/")).Groups["url"].Value;
        StringAssert.Contains(await admin.GetStringAsync(postUrl), "<p class=\"firma\">-- firmato</p>");

        // L'estensione con errori compare nell'amministrazione con il messaggio di errore.
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());
        var list = JsonNode.Parse(await admin.GetStringAsync("/api/packages?filter=extensions&take=0"))!.AsArray();
        StringAssert.StartsWith(list.Single(p => p!["Id"]!.GetValue<string>() == "Rotta")!["Description"]!.GetValue<string>(), "Errore:");

        // Le estensioni del pacchetto non si eliminano dall'amministrazione.
        Assert.AreEqual(HttpStatusCode.BadRequest, (await admin.PutAsync("/api/packages/uninstall/BBCode", Json("{}"))).StatusCode);
    }

    [TestMethod]
    public async Task Notifications_AreSentToSubscribersAndBlogOwner()
    {
        var sent = new ConcurrentQueue<EmailMessage>();
        using var factory = new BlogWebFactory { TestServices = s => s.AddScoped<IEmailQueue>(_ => new RecordingQueue(sent)) };
        (await factory.InstallAsync()).Dispose();

        await factory.RunAsSystemAsync(async services =>
        {
            var settings = services.GetRequiredService<ISettingsService>();
            var blogSettings = await settings.GetAsync();
            blogSettings.SendMailOnComment = true;
            blogSettings.EnableCommentsModeration = false;
            blogSettings.Email = "owner@example.com";
            await settings.SaveAsync(blogSettings);

            var post = (await services.GetRequiredService<IPostService>().GetPostsAsync()).First();
            var copy = BlogEngine.Web.Api.EntityCopies.Copy(post);
            copy.NotificationEmails.Add("iscritto@example.com");
            var comments = services.GetRequiredService<ICommentService>();
            await comments.AddAsync(copy, new Comment { Author = "Anna", Email = "anna@example.com", Content = "Ciao a tutti" });
        });

        var toSubscriber = sent.Single(m => m.To.Contains("iscritto@example.com"));
        StringAssert.Contains(toSubscriber.HtmlBody, "unsubscribe-email=iscritto%40example.com");
        var toOwner = sent.Single(m => m.To.Contains("owner@example.com"));
        StringAssert.Contains(toOwner.HtmlBody, "Ciao a tutti");
        Assert.AreEqual("anna@example.com", toOwner.ReplyTo);
    }

    [TestMethod]
    public async Task Newsletter_IsSentWhenAPostIsPublished()
    {
        var sent = new ConcurrentQueue<EmailMessage>();
        using var factory = new BlogWebFactory { TestServices = s => s.AddScoped<IEmailQueue>(_ => new RecordingQueue(sent)) };
        (await factory.InstallAsync()).Dispose();

        await factory.RunAsSystemAsync(async services =>
        {
            await services.GetRequiredService<BlogEngine.Core.Services.NewsletterSubscribers>().AddAsync("lettore@example.com");
            await services.GetRequiredService<IPostService>().SaveAsync(new Post { Title = "Novità del mese", Content = "<p>Testo</p>", Author = "admin" });
        });

        var newsletter = sent.Single(m => m.To.Contains("lettore@example.com"));
        Assert.AreEqual("Novità del mese", newsletter.Subject);
        StringAssert.Contains(newsletter.HtmlBody, "/post/Novit");
    }

    [TestMethod]
    public async Task VideoUpload_IsServedFromMediaWithRanges()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());

        using var form = new MultipartFormDataContent();
        var video = new ByteArrayContent(Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray());
        video.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(video, "file", "vacanze 2026.mp4");

        var upload = await admin.PostAsync("/api/upload?action=video", form);
        Assert.AreEqual(HttpStatusCode.Created, upload.StatusCode);
        var name = (await upload.Content.ReadAsStringAsync()).Trim('"');

        using var request = new HttpRequestMessage(HttpMethod.Get, "/media/" + Uri.EscapeDataString(name));
        request.Headers.Range = new RangeHeaderValue(100, 199);
        var partial = await admin.SendAsync(request);
        Assert.AreEqual(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.AreEqual(100, (await partial.Content.ReadAsByteArrayAsync()).Length);

        // Solo video, audio e immagini: gli altri file della cartella non passano da /media.
        Assert.AreEqual(HttpStatusCode.NotFound, (await admin.GetAsync("/media/../settings.xml")).StatusCode);
    }

    private sealed class RecordingQueue(ConcurrentQueue<EmailMessage> sent) : IEmailQueue
    {
        public void Enqueue(EmailMessage message) => sent.Enqueue(message);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    [GeneratedRegex("href=\"(?<url>/post/[^\"#?]+)\"")]
    private static partial Regex PostLinkRegex();
}

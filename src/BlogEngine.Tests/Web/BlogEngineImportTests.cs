using System.Net;
using BlogEngine.Core.Files;
using BlogEngine.Web.Import;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>Importazione del BlogML esportato da un vecchio BlogEngine.NET (WebForms).</summary>
[TestClass]
public sealed class BlogEngineImportTests
{
    private const string Ns = "http://www.blogml.com/2006/09/BlogML";

    // PNG 1x1 trasparente.
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private static readonly Guid PostId = Guid.Parse("8c05b746-8237-4f69-a9ab-67a6a3a4455a");
    private static readonly Guid CategoryId = Guid.Parse("aa4ec129-6ad8-4ca5-8235-b7f16a54b686");

    [TestMethod]
    public async Task Import_KeepsAddressesIdsPagesCommentsAndFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "be-tests", "blogengine-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var blogml = Path.Combine(folder, "BlogML.xml");
        File.WriteAllText(blogml, BlogML);
        try
        {
            using var factory = new BlogWebFactory();
            using var admin = await factory.InstallAsync();
            BlogEngineImportResult? result = null;
            await factory.RunAsSystemAsync(async services =>
            {
                var blog = (await services.GetRequiredService<BlogEngine.Core.Services.BlogsCache>().GetAllAsync()).First(b => b.IsPrimary);

                // Un file già presente non viene riscaricato; gli altri falliscono (l'indirizzo da cui scaricare non risponde).
                var existing = services.GetRequiredService<FileStore>().Resolve(blog.Id, "/2026/11/copertina.png");
                Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
                File.WriteAllBytes(existing, Convert.FromBase64String(Png));

                var importer = services.GetRequiredService<BlogEngineImporter>();
                var oldSite = new Uri("https://www.fiorentina.info/");
                var unreachable = new Uri("http://127.0.0.1:9/");
                var rome = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
                result = await importer.ImportAsync(blog.Id, blogml, oldSite, rome, unreachable);

                // Ripetere l'importazione aggiorna i contenuti invece di duplicarli.
                await importer.ImportAsync(blog.Id, blogml, oldSite, rome, unreachable);
            });

            Assert.AreEqual(3, result!.Posts, "Tre post, compresi i due con lo stesso titolo.");
            Assert.AreEqual(3, result.Pages, "Gli elementi \"article\" sono pagine.");
            Assert.AreEqual(1, result.Categories);
            Assert.AreEqual(1, result.Comments, "I trackback non contano come commenti.");
            Assert.AreEqual(1, result.ExistingFiles, $"Scaricati {result.DownloadedFiles}, falliti: {string.Join(" | ", result.FailedFiles)}");
            Assert.AreEqual(3, result.FailedFiles.Count, "La foto 2011/6, l'mp3 e la faccina dell'editor.");

            using var anonymous = factory.CreateBrowser(followRedirects: false);

            // Lo slug viene dal vecchio indirizzo, non dal titolo; il vecchio indirizzo .aspx porta a quello nuovo.
            var post = await anonymous.GetStringAsync("/post/Sassuolo-Fiorentina-2-4-ma-che-fatica");
            StringAssert.Contains(post, "Sassuolo-Fiorentina 2-4");
            StringAssert.Contains(post, "/image.axd?picture=/2026/11/copertina.png", "Gli indirizzi delle immagini restano quelli di prima, ma relativi.");
            StringAssert.Contains(post, "/file.axd?file=/Cori%20Stadio%20-%20Batistuta.mp3", "Il + dei vecchi indirizzi è uno spazio.");
            StringAssert.Contains(post, "/image.axd?picture=/vecchio-sito/editors/tiny_mce/plugins/emotions/img/smiley-wink.gif", "Le faccine dell'editor diventano file del blog.");
            StringAssert.Contains(post, "href=\"/page/Chi-Sono\"", "I link interni diventano relativi e senza .aspx.");
            StringAssert.Contains(post, "href=\"https://it.wikipedia.org/wiki/Firenze\"", "I link esterni restano.");
            StringAssert.Contains(post, "Che partita!");
            StringAssert.Contains(post, "Calcio");

            // Il vecchio indirizzo arriva, con redirect permanenti, a quello canonico del post.
            var address = "/post/Sassuolo-Fiorentina-2-4-ma-che-fatica-!.aspx";
            var response = await anonymous.GetAsync(address);
            for (var hops = 0; response.StatusCode == HttpStatusCode.MovedPermanently && hops < 3; hops++)
            {
                address = response.Headers.Location!.IsAbsoluteUri ? response.Headers.Location.PathAndQuery : response.Headers.Location.OriginalString;
                response = await anonymous.GetAsync(address);
            }

            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("/post/Sassuolo-Fiorentina-2-4-ma-che-fatica", address);

            // post.aspx?id=: l'id del vecchio post è rimasto.
            var byId = await anonymous.GetAsync($"/post.aspx?id={PostId}");
            Assert.IsTrue(byId.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.Redirect, $"post.aspx?id= risponde {byId.StatusCode}");

            // Fra due post con lo stesso titolo il vecchio indirizzo apre il più recente; il più vecchio ha un numero.
            StringAssert.Contains(await anonymous.GetStringAsync("/post/auguri"), "Auguri 2013");
            StringAssert.Contains(await anonymous.GetStringAsync("/post/auguri1"), "Auguri 2009");

            // Pagine: pubblicate, bozza non visibile, sottopagina ricavata dai link.
            StringAssert.Contains(await anonymous.GetStringAsync("/page/Chi-Sono"), "Tifoso viola");
            StringAssert.Contains(await anonymous.GetStringAsync("/page/Batistuta-Gabriel-Omar"), "Re Leone");
            Assert.AreEqual(HttpStatusCode.NotFound, (await anonymous.GetAsync("/page/Example-page")).StatusCode, "La bozza non è pubblica.");

            await factory.RunAsSystemAsync(async services =>
            {
                var pages = await services.GetRequiredService<BlogEngine.Services.IPageService>().GetPagesAsync();
                var chiSono = pages.Single(p => p.Slug == "Chi-Sono");
                var batistuta = pages.Single(p => p.Slug == "Batistuta-Gabriel-Omar");
                Assert.AreEqual(chiSono.Id, batistuta.Parent, "La pagina linkata finisce sotto quella che la linka.");
                Assert.AreEqual(Guid.Empty, chiSono.Parent);

                var posts = services.GetRequiredService<BlogEngine.Services.IPostService>();
                var imported = await posts.GetByIdAsync(PostId);
                Assert.IsNotNull(imported);
                Assert.AreEqual(new DateTime(2026, 11, 4, 15, 53, 0, DateTimeKind.Utc), imported.DateCreated, "Le 16:53 di Roma sono le 15:53 UTC.");
                Assert.AreEqual(2, imported.AllComments.Count, "Il commento e il trackback, non duplicati dalla seconda importazione.");
                Assert.AreEqual("Francesco", imported.Author, "Senza un utente con quel nome, l'autore resta com'era.");
                CollectionAssert.Contains(imported.Categories, CategoryId, "La categoria tiene il suo id.");
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void SlugFromUrl_UsesTheOldAddress()
    {
        Assert.AreEqual("Sassuolo-Fiorentina-2-4-ma-che-fatica", BlogEngineImporter.SlugFromUrl("/post/Sassuolo-Fiorentina-2-4-ma-che-fatica-!.aspx"));
        Assert.AreEqual("UEFA-Champions-League-(ex-Coppa-dei-Campioni)", BlogEngineImporter.SlugFromUrl("/page/UEFA-Champions-League-(ex-Coppa-dei-Campioni).aspx"));
        Assert.AreEqual("Avanti-cos236", BlogEngineImporter.SlugFromUrl("/post/Avanti-cos236;.aspx"));
        Assert.AreEqual(string.Empty, BlogEngineImporter.SlugFromUrl("/post/.aspx"));
    }

    [TestMethod]
    public void ConvertContent_RecordsDownloadsFromTheOldSiteOnly()
    {
        var oldSite = new Uri("https://www.fiorentina.info/");
        var downloads = new Dictionary<string, Uri>();
        var html = BlogEngineImporter.ConvertContent(
            "<img src=\"http://fiorentina.info/BlogEngine.NET/image.axd?picture=Teamgeist.jpg\" />" +
            "<img src=\"https://altro.it/image.axd?picture=x.jpg\" />" +
            "<a href=\"/post/2010/03/15/il-popolo-viola.aspx#commenti\">x</a>" +
            "<a href=\"/file.axd?file=Cori+-+Quando+l'inno+s'alzer%c3%a0.MP3\">coro</a>",
            oldSite, BlogEngineImporter.SiteHosts(oldSite), downloads);

        StringAssert.Contains(html, "src=\"/image.axd?picture=/Teamgeist.jpg\"");
        StringAssert.Contains(html, "src=\"https://altro.it/image.axd?picture=x.jpg\"", "Le immagini di altri siti restano dove sono.");
        StringAssert.Contains(html, "href=\"/post/il-popolo-viola#commenti\"");
        StringAssert.Contains(html, "href=\"/file.axd?file=/Cori%20-%20Quando%20l%27inno%20s%27alzer%C3%A0.MP3\"", "L'apostrofo nel nome del file non interrompe l'indirizzo.");
        Assert.AreEqual(2, downloads.Count);
        Assert.AreEqual("https://www.fiorentina.info/image.axd?picture=%2FTeamgeist.jpg", downloads["/Teamgeist.jpg"].AbsoluteUri);
    }

    private static readonly string BlogML = $$"""
        <?xml version="1.0" encoding="utf-8"?>
        <blog root-url="/" date-created="2026-11-11T23:04:04" xmlns="{{Ns}}">
          <title type="text"><![CDATA[Fiorentina.Info]]></title>
          <authors><author id="francesco" approved="true" email="f@example.com"><title type="text"><![CDATA[francesco]]></title></author></authors>
          <categories>
            <category id="{{CategoryId}}" date-created="0001-01-01T00:00:00" approved="true" parentref=""><title type="text"><![CDATA[Calcio]]></title></category>
          </categories>
          <posts>
            <post id="{{PostId}}" date-created="2026-11-04T16:53:00" date-modified="2026-11-04T17:12:39" approved="true"
                  post-url="/post/Sassuolo-Fiorentina-2-4-ma-che-fatica-!.aspx" type="normal" hasexcerpt="false" is-published="True">
              <title type="text">Sassuolo-Fiorentina 2-4: ma che fatica !</title>
              <content type="text">&lt;p&gt;&lt;img src="http://www.fiorentina.info/image.axd?picture=%2f2026%2f11%2fcopertina.png" /&gt;&lt;/p&gt;
        &lt;p&gt;&lt;img src="/image.axd?picture=2011%2f6%2fNo_Foto.jpg" /&gt;
        &lt;img src="http://www.fiorentina.info/editors/tiny_mce/plugins/emotions/img/smiley-wink.gif" /&gt;&lt;/p&gt;
        &lt;p&gt;&lt;a href="/file.axd?file=Cori+Stadio+-+Batistuta.mp3"&gt;Coro&lt;/a&gt;
        &lt;a href="http://www.fiorentina.info/page/Chi-Sono.aspx"&gt;Chi sono&lt;/a&gt;
        &lt;a href="https://it.wikipedia.org/wiki/Firenze"&gt;Firenze&lt;/a&gt;&lt;/p&gt;</content>
              <post-name type="text">Sassuolo-Fiorentina 2-4: ma che fatica !</post-name>
              <authors><author ref="Francesco" /></authors>
              <categories><category ref="{{CategoryId}}" /></categories>
              <tags><tag ref="Serie A" /></tags>
              <comments>
                <comment id="50ba0fc5-3345-407c-b57c-479024a97153" parentid="00000000-0000-0000-0000-000000000000" date-created="2026-11-04T18:00:00" approved="true" user-name="Oliviero" user-email="o@example.com" user-ip="127.0.0.1" user-url="">
                  <title type="text">re: Sassuolo</title>
                  <content type="text">Che partita!</content>
                </comment>
              </comments>
              <trackbacks>
                <trackback id="9b1a0fc5-3345-407c-b57c-479024a97153" date-created="2026-11-05T10:00:00" approved="true" url="https://example.com/viola">
                  <title type="text">Un altro blog viola</title>
                </trackback>
              </trackbacks>
            </post>
            <post id="11111111-0000-0000-0000-000000000001" date-created="2013-01-01T23:21:00" approved="true" post-url="/post/auguri.aspx" type="normal" is-published="True">
              <title type="text">Auguri</title>
              <content type="text">&lt;p&gt;Auguri 2013&lt;/p&gt;</content>
              <post-name type="text">Auguri</post-name>
              <authors><author ref="francesco" /></authors>
            </post>
            <post id="11111111-0000-0000-0000-000000000002" date-created="2009-01-11T11:39:23" approved="true" post-url="/post/auguri.aspx" type="normal" is-published="True">
              <title type="text">Auguri</title>
              <content type="text">&lt;p&gt;Auguri 2009&lt;/p&gt;</content>
              <post-name type="text">Auguri</post-name>
              <authors><author ref="francesco" /></authors>
            </post>
          </posts>
          <posts>
            <post id="22222222-0000-0000-0000-000000000001" date-created="2011-06-20T15:34:37" approved="true" post-url="/page/Chi-Sono.aspx" type="article" hasexcerpt="true" is-published="True">
              <title type="text">Chi Sono</title>
              <content type="text">&lt;p&gt;Tifoso viola. &lt;a href="/page/Batistuta-Gabriel-Omar.aspx"&gt;Il mio idolo&lt;/a&gt;&lt;/p&gt;</content>
              <post-name type="text">Chi Sono</post-name>
              <authors><author ref="Francesco" /></authors>
            </post>
            <post id="22222222-0000-0000-0000-000000000002" date-created="2011-06-20T15:40:00" approved="true" post-url="/page/Batistuta-Gabriel-Omar.aspx" type="article" is-published="True">
              <title type="text">Batistuta, Gabriel Omar</title>
              <content type="text">&lt;p&gt;Il Re Leone. &lt;a href="/page/Chi-Sono.aspx"&gt;Torna&lt;/a&gt;&lt;/p&gt;</content>
              <post-name type="text">Batistuta, Gabriel Omar</post-name>
              <authors><author ref="Francesco" /></authors>
            </post>
            <post id="22222222-0000-0000-0000-000000000003" date-created="2011-06-20T15:45:00" approved="true" post-url="/page/Example-page.aspx" type="article" is-published="False">
              <title type="text">Example page</title>
              <content type="text">&lt;p&gt;Bozza&lt;/p&gt;</content>
              <post-name type="text">Example page</post-name>
              <authors><author ref="admin" /></authors>
            </post>
          </posts>
        </blog>
        """;
}

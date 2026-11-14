using System.Net;
using BlogEngine.Web.Import;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>Importazione di un blog Bludit dalla sua cartella bl-content.</summary>
[TestClass]
public sealed class BluditImportTests
{
    private const string PhpGuard = "<?php defined('BLUDIT') or die('Bludit CMS.'); ?>\n";

    // PNG 1x1 trasparente.
    private const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    [TestMethod]
    public async Task Import_BringsPostsPagesImagesAndOldAddresses()
    {
        var folder = Path.Combine(Path.GetTempPath(), "be-tests", "bludit-" + Guid.NewGuid().ToString("N"), "bl-content");
        try
        {
            CreateBluditSite(folder);

            using var factory = new BlogWebFactory();
            using var admin = await factory.InstallAsync();
            BluditImportResult? result = null;
            await factory.RunAsSystemAsync(async services =>
            {
                var blog = (await services.GetRequiredService<BlogEngine.Core.Services.BlogsCache>().GetAllAsync()).First(b => b.IsPrimary);
                result = await services.GetRequiredService<BluditImporter>().ImportAsync(blog.Id, Path.GetDirectoryName(folder)!);

                // Una pagina sistemata nel nuovo blog dopo la prima importazione...
                var pageStore = services.GetRequiredService<BlogEngine.Core.Caching.EntityRepository<BlogEngine.Models.Page>>();
                var imported = (await pageStore.GetAllAsync(blog.Id)).Single(p => p.Slug == "chi-sono");
                imported.Title = "Chi sono (sistemata)";
                imported.ShowInMenu = false;
                await pageStore.SaveAsync(imported);

                // ...resta com'è quando si ripete l'importazione, che aggiorna i post invece di duplicarli.
                await services.GetRequiredService<BluditImporter>().ImportAsync(blog.Id, folder);
            });

            Assert.AreEqual(2, result!.Posts, "Il post pubblicato e la bozza.");
            Assert.AreEqual(1, result.Pages);
            Assert.IsTrue(result.Skipped.Any(s => s.StartsWith("autosave-", StringComparison.Ordinal)), "I salvataggi automatici non vengono importati.");

            using var anonymous = factory.CreateBrowser(followRedirects: false);

            // Il punto nello slug di Bludit sparisce; il vecchio indirizzo porta a quello nuovo.
            var post = await anonymous.GetStringAsync("/post/patch-alla-714");
            StringAssert.Contains(post, "Patch alla 7.1.4");
            StringAssert.Contains(post, "/image.axd?picture=/bludit/pages/abc123/copertina.png", "L'immagine di copertina è in cima al testo.");
            StringAssert.Contains(post, "/image.axd?picture=/bludit/inline/", "Le immagini incollate nel testo diventano file.");
            StringAssert.Contains(post, "href=\"/page/chi-sono\"", "I link interni puntano ai nuovi indirizzi.");
            StringAssert.Contains(post, "style=\"text-align: center;\"", "Gli stili utili restano.");
            Assert.IsFalse(post.Contains("font-family: Roboto", StringComparison.Ordinal), "Gli stili copiati da altri siti vengono tolti.");
            StringAssert.Contains(post, "Sicurezza informatica");
            StringAssert.Contains(post, "Il resto dell'articolo.", "Il post singolo è completo.");
            Assert.IsFalse(post.Contains("pagebreak", StringComparison.Ordinal) || post.Contains("[more]", StringComparison.Ordinal));

            // Nell'elenco (con un tema che mostra il testo dei post) il post si ferma al "Leggi tutto" di Bludit.
            var home = await anonymous.GetStringAsync("/?theme=ExtraGeek");
            StringAssert.Contains(home, "class=\"more\"");
            Assert.IsFalse(home.Contains("Il resto dell'articolo.", StringComparison.Ordinal), "Nell'elenco c'è solo l'inizio del post.");

            string? language = null;
            await factory.RunAsSystemAsync(async services =>
                language = (await services.GetRequiredService<BlogEngine.Services.ISettingsService>().GetAsync()).Language);
            Assert.AreEqual("it-IT", language, "La lingua di Bludit diventa quella del blog.");

            var moved = await anonymous.GetAsync("/patch-alla-7.1.4");
            Assert.AreEqual(HttpStatusCode.MovedPermanently, moved.StatusCode);
            Assert.AreEqual("/post/patch-alla-714", PathOf(moved.Headers.Location!));

            StringAssert.Contains(await anonymous.GetStringAsync("/page/chi-sono"), "Chi sono (sistemata)", "La pagina modificata non viene sovrascritta.");

            var page = await anonymous.GetAsync("/chi-sono");
            Assert.AreEqual(HttpStatusCode.MovedPermanently, page.StatusCode);
            Assert.AreEqual("/page/chi-sono", PathOf(page.Headers.Location!));

            var image = await anonymous.GetAsync("/bl-content/uploads/pages/abc123/thumbnails/copertina.png");
            Assert.AreEqual(HttpStatusCode.MovedPermanently, image.StatusCode);
            Assert.AreEqual(HttpStatusCode.OK, (await anonymous.GetAsync(image.Headers.Location)).StatusCode, "L'immagine è stata copiata.");

            Assert.AreEqual(HttpStatusCode.NotFound, (await anonymous.GetAsync("/bozza")).StatusCode, "La bozza non è pubblica.");
            Assert.AreEqual(HttpStatusCode.NotFound, (await anonymous.GetAsync("/indirizzo-inesistente")).StatusCode);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    private static string PathOf(Uri location) => location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;

    private static void CreateBluditSite(string folder)
    {
        void Write(string path, string text)
        {
            var full = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text);
        }

        Write("databases/site.php", PhpGuard + """{"title":"Geek","slogan":"Benvenuti","url":"https://www.example.com","timezone":"Europe/Rome","language":"it_IT"}""");
        Write("databases/categories.php", PhpGuard + """{"news":{"name":"News","description":"","list":["patch-alla-7.1.4"]},"videos":{"name":"Videos","description":"","list":[]}}""");
        Write("databases/pages.php", PhpGuard + """
            {
              "patch-alla-7.1.4": {"title":"Patch alla 7.1.4","description":"","username":"admin","tags":{"sicurezza":"Sicurezza informatica"},
                "type":"published","date":"2026-11-02 14:49:26","dateModified":"","position":1,"coverImage":"copertina.png","category":"news",
                "uuid":"abc123","allowComments":true},
              "bozza": {"title":"Bozza","description":"","tags":[],"type":"draft","date":"2026-11-03 10:00:00","category":"","uuid":"d1","coverImage":""},
              "chi-sono": {"title":"Chi sono","description":"","tags":[],"type":"static","date":"2023-01-23 22:50:42","position":2,"category":"","uuid":"p1","coverImage":""},
              "autosave-123": {"title":"Bozza automatica","tags":[],"type":"autosave","date":"2026-11-03 10:00:00","uuid":"autosave-123"}
            }
            """);
        Write("pages/patch-alla-7.1.4/index.txt",
            "<p style=\"color: #474b4f; font-family: Roboto, sans-serif; text-align: center;\">Aggiornate subito.</p>\n" +
            $"<p><img src=\"data:image/png;base64,{Png}\" /></p>\n" +
            "<p><a href=\"https://www.example.com/chi-sono\">Chi sono</a><!-- pagebreak --></p>\n" +
            "<p>Il resto dell'articolo.</p>");
        Write("pages/bozza/index.txt", "<p>Non ancora pronta.</p>");
        Write("pages/chi-sono/index.txt", "<p>Scrivo di informatica.</p>");
        Write("pages/autosave-123/index.txt", "<p>...</p>");

        var image = Path.Combine(folder, "uploads", "pages", "abc123", "copertina.png");
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(image)!, "thumbnails"));
        File.WriteAllBytes(image, Convert.FromBase64String(Png));
        File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(image)!, "thumbnails", "copertina.png"), Convert.FromBase64String(Png));
    }
}

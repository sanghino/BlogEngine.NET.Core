using System.Net;
using System.Net.Http.Headers;

namespace BlogEngine.Tests.Web;

/// <summary>
/// Script, stili e template vengono ricontrollati dal browser a ogni uso: dopo un aggiornamento (o il passaggio
/// da un vecchio BlogEngine.NET con gli stessi nomi di file) nessuno resta con le versioni vecchie in cache.
/// </summary>
[TestClass]
public sealed class StaticFileCachingTests
{
    [TestMethod]
    [DataRow("/admin/app/app.js")]
    [DataRow("/admin/app/dashboard/dashboardView.html")]
    [DataRow("/Scripts/blog.js")]
    [DataRow("/Custom/Themes/Fiorentina/style.css")]
    [DataRow("/Custom/Widgets/NextEvent/widget.json")]
    public async Task CodeAndTemplates_AreRevalidated(string path)
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();

        var response = await admin.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, path);
        Assert.IsTrue(response.Headers.CacheControl?.NoCache == true, $"{path}: Cache-Control = {response.Headers.CacheControl}");

        // Il ricontrollo costa poco: con l'ETag il server risponde 304 senza rimandare il file.
        var etag = response.Headers.ETag;
        Assert.IsNotNull(etag, path);
        using var again = new HttpRequestMessage(HttpMethod.Get, path);
        again.Headers.IfNoneMatch.Add(etag);
        Assert.AreEqual(HttpStatusCode.NotModified, (await admin.SendAsync(again)).StatusCode, path);
    }

    [TestMethod]
    [DataRow("/Content/images/blog/noavatar.jpg")]
    [DataRow("/Custom/Themes/Fiorentina/images/logo-fiorentina-info.png")]
    [DataRow("/Custom/Themes/Fiorentina/fonts/open-sans-latin.woff2")]
    public async Task ImagesAndFonts_KeepDefaultCaching(string path)
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();

        var response = await admin.GetAsync(path);
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, path);
        Assert.IsFalse(response.Headers.CacheControl?.NoCache == true, $"{path}: Cache-Control = {response.Headers.CacheControl}");
    }

    [TestMethod]
    public void MustRevalidate_OnlyCodeAndTemplates()
    {
        Assert.IsTrue(BlogEngine.Web.Hosting.StaticFileCaching.MustRevalidate("dashboardController.js"));
        Assert.IsTrue(BlogEngine.Web.Hosting.StaticFileCaching.MustRevalidate("STYLE.CSS"));
        Assert.IsFalse(BlogEngine.Web.Hosting.StaticFileCaching.MustRevalidate("foto.jpg"));
        Assert.IsFalse(BlogEngine.Web.Hosting.StaticFileCaching.MustRevalidate("font.woff2"));
    }
}

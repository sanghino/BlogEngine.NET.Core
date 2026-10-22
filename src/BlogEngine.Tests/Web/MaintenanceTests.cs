using System.Net;
using BlogEngine.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>Modalità manutenzione: visitatori fermi alla pagina 503, chi amministra vede il blog.</summary>
[TestClass]
public sealed class MaintenanceTests
{
    private static Task SetMaintenanceAsync(BlogWebFactory factory, bool enabled, string message = "") =>
        factory.RunAsSystemAsync(async services =>
        {
            var settingsService = services.GetRequiredService<ISettingsService>();
            var settings = await settingsService.GetAsync();
            settings.EnableMaintenanceMode = enabled;
            settings.MaintenanceMessage = message;
            await settingsService.SaveAsync(settings);
        });

    [TestMethod]
    public async Task Visitors_GetMaintenancePage_AdministratorsSeeTheBlog()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        var postUrl = System.Text.RegularExpressions.Regex.Match(await admin.GetStringAsync("/"), "href=\"(?<url>/post/[^\"]+)\"").Groups["url"].Value;
        await SetMaintenanceAsync(factory, enabled: true);

        using var visitor = factory.CreateBrowser(followRedirects: false);
        var home = await visitor.GetAsync("/");
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, home.StatusCode);
        Assert.AreEqual(TimeSpan.FromSeconds(3600), home.Headers.RetryAfter?.Delta, "I motori di ricerca riproveranno più tardi.");
        var page = await home.Content.ReadAsStringAsync();
        StringAssert.Contains(page, "Site under maintenance");
        StringAssert.Contains(page, "as soon as possible", "Senza messaggio personalizzato vale il testo predefinito.");
        Assert.IsFalse(page.Contains(postUrl), "La pagina di manutenzione non mostra i contenuti del blog.");

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await visitor.GetAsync(postUrl)).StatusCode);
        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, (await visitor.GetAsync("/syndication.axd")).StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, (await visitor.GetAsync("/account/login")).StatusCode, "Il login resta raggiungibile.");
        Assert.AreEqual(HttpStatusCode.OK, (await visitor.GetAsync("/Content/Auto/Global.css")).StatusCode, "I file statici restano raggiungibili.");

        var asAdmin = await admin.GetAsync("/");
        Assert.AreEqual(HttpStatusCode.OK, asAdmin.StatusCode, "Chi amministra vede il blog...");
        StringAssert.Contains(await asAdmin.Content.ReadAsStringAsync(), "be-maintenance-notice", "...con l'avviso che è in manutenzione.");

        await SetMaintenanceAsync(factory, enabled: true, message: "Torniamo lunedì mattina.");
        StringAssert.Contains(await (await visitor.GetAsync("/")).Content.ReadAsStringAsync(), "Torniamo lunedì mattina.");

        await SetMaintenanceAsync(factory, enabled: false);
        var reopened = await visitor.GetAsync("/");
        Assert.AreEqual(HttpStatusCode.OK, reopened.StatusCode);
        Assert.IsFalse((await reopened.Content.ReadAsStringAsync()).Contains("be-maintenance-notice"));
    }
}

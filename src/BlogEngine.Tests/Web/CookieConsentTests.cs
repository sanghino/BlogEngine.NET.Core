using BlogEngine.Services;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>Banner del consenso ai cookie: gli script partono solo dopo il consenso alla loro categoria.</summary>
[TestClass]
public sealed class CookieConsentTests
{
    private const string Analytics = """<script async src="https://www.googletagmanager.com/gtag/js?id=G-TEST"></script><script>window.dataLayer = window.dataLayer || []; gtag('config', 'G-TEST');</script>""";

    private static CookieConsent Consent(bool enabled, string? cookie)
    {
        var http = new DefaultHttpContext();
        if (cookie is not null)
        {
            http.Request.Headers.Cookie = $"{CookieConsent.CookieName}={Uri.EscapeDataString(cookie)}";
        }

        return CookieConsent.FromRequest(http.Request, enabled);
    }

    [TestMethod]
    public void Gate_DisablesOnlyJavaScriptUntilConsent()
    {
        var none = Consent(enabled: true, cookie: null);
        var html = """<meta name="google-site-verification" content="x" /><script type="application/ld+json">{"a":1}</script><script type='text/javascript'>run()</script>""" + Analytics;
        var gated = none.Gate(html, CookieConsent.Statistics);

        StringAssert.Contains(gated, """<meta name="google-site-verification" content="x" />""", "L'HTML che non è script resta.");
        StringAssert.Contains(gated, """<script type="application/ld+json">""", "I dati strutturati non sono script da bloccare.");
        StringAssert.Contains(gated, """<script type="text/plain" data-consent="statistics" async src="https://www.googletagmanager.com/gtag/js?id=G-TEST">""");
        StringAssert.Contains(gated, """<script type="text/plain" data-consent="statistics" data-type="text/javascript">run()</script>""");
        Assert.IsFalse(none.HasChoice);

        Assert.AreEqual(html, Consent(enabled: true, cookie: "v1:statistics").Gate(html, CookieConsent.Statistics), "Consenso dato: script attivi.");
        Assert.AreNotEqual(html, Consent(enabled: true, cookie: "v1:statistics").Gate(html, CookieConsent.Marketing), "Il consenso è per categoria.");
        Assert.AreNotEqual(html, Consent(enabled: true, cookie: "v1:").Gate(html, CookieConsent.Statistics), "Solo necessari: script spenti.");
        Assert.AreEqual(html, Consent(enabled: false, cookie: null).Gate(html, CookieConsent.Statistics), "Senza banner tutto resta com'è.");
        Assert.IsFalse(Consent(enabled: true, cookie: "valore-strano").HasChoice, "Un cookie sconosciuto non vale come scelta.");
    }

    [TestMethod]
    public async Task Banner_GatesTrackingCodeUntilVisitorConsents()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        await factory.RunAsSystemAsync(async services =>
        {
            var settingsService = services.GetRequiredService<ISettingsService>();
            var settings = await settingsService.GetAsync();
            settings.EnableCookieConsent = true;
            settings.CookiePolicyUrl = "/page/privacy";
            settings.HtmlHeader = Analytics;
            await settingsService.SaveAsync(settings);
        });

        using var visitor = factory.CreateBrowser();
        var first = await visitor.GetStringAsync("/");
        StringAssert.Contains(first, "id=\"be-consent\"");
        Assert.IsFalse(first.Contains("id=\"be-consent\" class=\"be-consent\" role=\"dialog\" aria-labelledby=\"be-consent-title\" data-cookie=\"blogengine_consent\" data-path=\"/\" data-days=\"180\" hidden"),
            "Alla prima visita il banner è visibile.");
        StringAssert.Contains(first, "href=\"/page/privacy\"");
        StringAssert.Contains(first, "data-consent=\"statistics\" async src=\"https://www.googletagmanager.com");
        Assert.IsFalse(first.Contains("<script async src=\"https://www.googletagmanager.com"), "Analytics non parte prima del consenso.");
        StringAssert.Contains(first, "Scripts/consent.js");

        using var consented = factory.CreateBrowser();
        consented.DefaultRequestHeaders.Add("Cookie", $"{CookieConsent.CookieName}={Uri.EscapeDataString("v1:statistics")}");
        var later = await consented.GetStringAsync("/");
        StringAssert.Contains(later, "<script async src=\"https://www.googletagmanager.com", "Dopo il consenso Analytics parte subito.");
        StringAssert.Contains(later, "data-days=\"180\" hidden>", "Chi ha già scelto non rivede il banner...");
        Assert.IsFalse(later.Contains("id=\"be-consent-reopen\" class=\"be-consent-reopen\" hidden"), "...ma ha il pulsante per cambiare idea.");
    }
}

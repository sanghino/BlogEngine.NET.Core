using System.Net;
using System.Text.Json.Nodes;
using BlogEngine.Core.Configuration;
using BlogEngine.Services;
using BlogEngine.Web.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BlogEngine.Tests.Web;

/// <summary>Statistiche delle visite della dashboard: conteggio, esclusioni, provenienza e salvataggio.</summary>
[TestClass]
public sealed class VisitStatisticsTests
{
    private const string Chrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";
    private const string IPhone = "Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.0 Mobile/15E148 Safari/604.1";

    [DataTestMethod]
    [DataRow(null, VisitSources.Direct)]
    [DataRow("http://localhost/post/altro", VisitSources.Direct)]
    [DataRow("https://www.google.it/", VisitSources.Search)]
    [DataRow("https://news.google.com/articles", VisitSources.Search)]
    [DataRow("https://duckduckgo.com/", VisitSources.Search)]
    [DataRow("https://l.facebook.com/l.php", VisitSources.Social)]
    [DataRow("https://t.co/abc", VisitSources.Social)]
    [DataRow("https://www.t.corriere.it/", VisitSources.Sites)]
    [DataRow("https://www.fiorentina.info/post/x", VisitSources.Sites)]
    public void Sources_AreClassifiedFromTheReferrer(string? referrer, string expected) =>
        Assert.AreEqual(expected, VisitSources.Classify(referrer is null ? null : new Uri(referrer), "localhost"));

    [DataTestMethod]
    [DataRow("", true)]
    [DataRow("curl/8.4.0", true)]
    [DataRow("Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)", true)]
    [DataRow("Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/120.0", true)]
    [DataRow(Chrome, false)]
    [DataRow(IPhone, false)]
    public void Robots_AreRecognisedFromTheUserAgent(string userAgent, bool isBot) =>
        Assert.AreEqual(isBot, VisitTrackingMiddleware.IsBot(userAgent));

    [TestMethod]
    public async Task PublicPages_AreCountedAndShownInTheDashboard()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());

        using var visitor = Browser(factory, Chrome);
        var home = await visitor.GetStringAsync("/");
        var postUrl = System.Text.RegularExpressions.Regex.Match(home, "href=\"(?<url>/post/[^\"]+)\"").Groups["url"].Value;
        await visitor.GetStringAsync(postUrl);

        // Lo stesso visitatore conta una volta sola; un altro, da cellulare, arriva da Google.
        using var phone = Browser(factory, IPhone, "198.51.100.7");
        using (var fromGoogle = new HttpRequestMessage(HttpMethod.Get, postUrl))
        {
            fromGoogle.Headers.Referrer = new Uri("https://www.google.it/");
            Assert.AreEqual(HttpStatusCode.OK, (await phone.SendAsync(fromGoogle)).StatusCode);
        }

        // Non contano: robot, file, pagine inesistenti, utenti collegati, anteprime del browser.
        using var robot = Browser(factory, "Mozilla/5.0 (compatible; bingbot/2.0; +http://www.bing.com/bingbot.htm)");
        await robot.GetAsync("/");
        await visitor.GetAsync("/Scripts/blog.js");
        await visitor.GetAsync("/post/non-esiste");
        admin.DefaultRequestHeaders.UserAgent.ParseAdd(Chrome);
        await admin.GetAsync("/");
        using (var prefetch = new HttpRequestMessage(HttpMethod.Get, "/"))
        {
            prefetch.Headers.Add("Sec-Purpose", "prefetch");
            await visitor.SendAsync(prefetch);
        }

        var report = await GetReportAsync(admin);
        Assert.IsTrue(report["Enabled"]!.GetValue<bool>());
        Assert.AreEqual(30, report["Days"]!.AsArray().Count, "Un elemento per ogni giorno, anche senza visite.");
        Assert.AreEqual(3, report["Today"]!["Views"]!.GetValue<int>());
        Assert.AreEqual(2, report["Today"]!["Visitors"]!.GetValue<int>());
        Assert.AreEqual(50, report["MobilePercent"]!.GetValue<int>());

        var top = report["Pages"]!.AsArray()[0]!;
        Assert.AreEqual(postUrl, top["Url"]!.GetValue<string>());
        Assert.AreEqual(2, top["Views"]!.GetValue<int>());
        StringAssert.StartsWith(top["Title"]!.GetValue<string>(), "Welcome", "Al posto dell'indirizzo c'è il titolo del post.");

        var sources = report["Sources"]!.AsArray().ToDictionary(s => s!["Name"]!.GetValue<string>(), s => s!["Count"]!.GetValue<int>());
        Assert.AreEqual(1, sources[VisitSources.Search]);
        Assert.AreEqual(1, sources[VisitSources.Direct]);

        // Il salvataggio su disco viene riletto alla ripartenza del sito.
        var statistics = factory.Services.GetRequiredService<VisitStatistics>();
        statistics.Flush();
        var blogId = (await factory.Services.GetRequiredService<BlogEngine.Core.Services.BlogsCache>().GetAllAsync()).Single().Id;
        var file = statistics.FilePath(blogId);
        Assert.IsTrue(File.Exists(file));
        Assert.IsFalse(File.ReadAllText(file).Contains("198.51.100.7", StringComparison.Ordinal), "Gli indirizzi IP non vengono salvati.");

        var reloaded = new VisitStatistics(factory.Services.GetRequiredService<BlogEnginePaths>(), NullLogger<VisitStatistics>.Instance);
        var today = DateOnly.Parse(report["Days"]!.AsArray()[^1]!["Date"]!.GetValue<string>());
        Assert.AreEqual(3, reloaded.GetDays(blogId, today, today).Single().Views);
    }

    [TestMethod]
    public async Task TurnedOff_NothingIsCounted()
    {
        using var factory = new BlogWebFactory();
        using var admin = await factory.InstallAsync();
        admin.DefaultRequestHeaders.Add(BlogContextMiddleware.BlogInstanceHeader, Guid.Empty.ToString());
        await factory.RunAsSystemAsync(async services =>
        {
            var settings = services.GetRequiredService<ISettingsService>();
            var blogSettings = await settings.GetAsync();
            blogSettings.EnableVisitStatistics = false;
            await settings.SaveAsync(blogSettings);
        });

        using var visitor = Browser(factory, Chrome);
        await visitor.GetStringAsync("/");

        var report = await GetReportAsync(admin);
        Assert.IsFalse(report["Enabled"]!.GetValue<bool>());
        Assert.AreEqual(0, report["Today"]!["Views"]!.GetValue<int>());
    }

    [TestMethod]
    public async Task Report_RequiresTheDashboardRight()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        using var anonymous = factory.CreateBrowser();
        Assert.AreEqual(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/visits")).StatusCode);
    }

    private static HttpClient Browser(BlogWebFactory factory, string userAgent, string? forwardedFor = null)
    {
        var browser = factory.CreateBrowser();
        browser.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        if (forwardedFor is not null)
        {
            browser.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        }

        return browser;
    }

    private static async Task<JsonObject> GetReportAsync(HttpClient admin)
    {
        var response = await admin.GetAsync("/api/visits");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (JsonObject)JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }
}

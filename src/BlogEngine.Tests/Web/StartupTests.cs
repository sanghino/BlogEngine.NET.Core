using System.Net;
using BlogEngine.Core.Configuration;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

[TestClass]
public sealed class StartupTests
{
    [TestMethod]
    public async Task NotInstalled_RedirectsToSetupAndCreatesKey()
    {
        using var factory = new BlogWebFactory();
        using var browser = factory.CreateBrowser(followRedirects: false);

        var response = await browser.GetAsync("/");

        Assert.AreEqual(HttpStatusCode.Redirect, response.StatusCode);
        Assert.AreEqual("/setup", response.Headers.Location?.OriginalString);
        Assert.AreEqual(32, factory.ReadSetupKey().Length);
        Assert.AreEqual(HttpStatusCode.OK, (await browser.GetAsync("/setup")).StatusCode);
    }

    [TestMethod]
    public async Task Setup_WithWrongKey_IsRejected()
    {
        using var factory = new BlogWebFactory();
        using var browser = factory.CreateBrowser();
        await browser.GetAsync("/");

        var response = await browser.PostFormAsync("/setup", new()
        {
            ["Input.SetupKey"] = "chiave-sbagliata",
            ["Input.DataProvider"] = "Xml",
            ["Input.BlogName"] = "x",
            ["Input.UserName"] = "admin",
            ["Input.Email"] = "a@example.com",
            ["Input.Password"] = BlogWebFactory.AdminPassword,
            ["Input.ConfirmPassword"] = BlogWebFactory.AdminPassword,
        });

        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "setup key is not valid");
        Assert.IsFalse(File.Exists(Path.Combine(factory.DataRoot, InstallationState.InstallationFileName)));
    }

    [DataTestMethod]
    [DataRow(DataProviderKind.Xml)]
    [DataRow(DataProviderKind.Sqlite)]
    public async Task Setup_CreatesAdministratorAndSignsIn(DataProviderKind provider)
    {
        using var factory = new BlogWebFactory();
        using var browser = await factory.InstallAsync(provider, "Il mio blog");

        var home = await browser.GetStringAsync("/");
        StringAssert.Contains(home, "Il mio blog");
        StringAssert.Contains(home, "widget administration", "L'utente appena creato è collegato e vede il menu di amministrazione.");

        Assert.IsTrue(File.Exists(Path.Combine(factory.DataRoot, InstallationState.InstallationFileName)));
        Assert.IsFalse(File.Exists(Path.Combine(factory.DataRoot, InstallationState.SetupKeyFileName)), "La chiave viene eliminata.");
        Assert.AreEqual(HttpStatusCode.NotFound, (await browser.GetAsync("/setup")).StatusCode, "La configurazione non è più raggiungibile.");

        var expectedDataFile = provider == DataProviderKind.Sqlite ? "blogengine.db" : "blogs.xml";
        Assert.IsTrue(File.Exists(Path.Combine(factory.DataRoot, "data", expectedDataFile)));
    }

    [TestMethod]
    public async Task Login_Logout_AndProtectedPages()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        using var browser = factory.CreateBrowser(followRedirects: false);

        var protectedPage = await browser.GetAsync("/account/change-password");
        Assert.AreEqual(HttpStatusCode.Redirect, protectedPage.StatusCode);
        StringAssert.StartsWith(protectedPage.Headers.Location?.ToString(), "http://localhost/account/login");

        var failed = await browser.PostFormAsync("/account/login", new() { ["UserName"] = "admin", ["Password"] = "sbagliata" });
        StringAssert.Contains(await failed.Content.ReadAsStringAsync(), "Login failed");

        var ok = await browser.PostFormAsync("/account/login", new() { ["UserName"] = "ADMIN", ["Password"] = BlogWebFactory.AdminPassword, ["ReturnUrl"] = "https://evil.example.com/" });
        Assert.AreEqual(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.AreEqual("/", ok.Headers.Location?.OriginalString, "Un returnUrl esterno viene ignorato.");
        Assert.AreEqual(HttpStatusCode.OK, (await browser.GetAsync("/account/change-password")).StatusCode);

        await browser.PostFormAsync("/account/logout", []);
        Assert.AreEqual(HttpStatusCode.Redirect, (await browser.GetAsync("/account/change-password")).StatusCode);
    }

    [TestMethod]
    public async Task ChangePassword_ThenLoginWithNewPassword()
    {
        using var factory = new BlogWebFactory();
        using var browser = await factory.InstallAsync();

        var changed = await browser.PostFormAsync("/account/change-password", new()
        {
            ["OldPassword"] = BlogWebFactory.AdminPassword,
            ["NewPassword"] = "Nuova-Password-2",
            ["ConfirmPassword"] = "Nuova-Password-2",
        });
        StringAssert.Contains(await changed.Content.ReadAsStringAsync(), "changed successfully");

        using var other = factory.CreateBrowser(followRedirects: false);
        var login = await other.PostFormAsync("/account/login", new() { ["UserName"] = "admin", ["Password"] = "Nuova-Password-2" });
        Assert.AreEqual(HttpStatusCode.Redirect, login.StatusCode);
    }

    [TestMethod]
    public async Task ChildBlog_IsServedUnderItsPathWithOwnSettingsAndUsers()
    {
        using var factory = new BlogWebFactory();
        using var browser = await factory.InstallAsync(blogName: "Principale");

        await factory.RunAsSystemAsync(async services =>
        {
            var child = new Blog { Name = "Figlio", VirtualPath = "~/figlio" };
            await services.GetRequiredService<IBlogService>().SaveAsync(child);
            var settings = services.GetRequiredService<ISettingsService>();
            var childSettings = await settings.GetAsync(child.Id);
            childSettings.Name = "Blog figlio";
            await settings.SaveAsync(childSettings, child.Id);
        });

        var childHome = await browser.GetStringAsync("/figlio/");
        StringAssert.Contains(childHome, "Blog figlio");
        Assert.IsFalse(childHome.Contains("widget administration"), "L'amministratore del principale è un visitatore nel blog figlio.");
        StringAssert.Contains(childHome, "href=\"/figlio/account/login?ReturnUrl=", "I link del blog figlio includono il suo percorso.");

        StringAssert.Contains(await browser.GetStringAsync("/"), "Principale");
    }

    [TestMethod]
    public async Task Login_IsRateLimited()
    {
        using var factory = new BlogWebFactory();
        (await factory.InstallAsync()).Dispose();
        using var browser = factory.CreateBrowser();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 12; i++)
        {
            statuses.Add((await browser.PostAsync("/account/login", new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
        }

        CollectionAssert.Contains(statuses, HttpStatusCode.TooManyRequests);
    }

    [TestMethod]
    public async Task Startup_PersistsDataProtectionKeysInDataFolder()
    {
        using var factory = new BlogWebFactory();
        factory.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("test").Protect("ciao"u8.ToArray());

        Assert.IsTrue(Directory.EnumerateFiles(Path.Combine(factory.DataRoot, "keys"), "key-*.xml").Any());
        await Task.CompletedTask;
    }

    [TestMethod]
    public void Messages_AreTranslatedInEnglishAndItalian()
    {
        using var factory = new BlogWebFactory();
        using var scope = factory.Services.CreateScope();
        var translator = scope.ServiceProvider.GetRequiredService<MessageTranslator>();
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new("en");
            Assert.AreEqual("The password must be at least 8 characters long.", translator[BlogEngine.Core.Messages.Format(BlogEngine.Core.Messages.PasswordTooShort, 8)]);

            System.Globalization.CultureInfo.CurrentUICulture = new("it");
            Assert.AreEqual("La password deve avere almeno 8 caratteri.", translator[BlogEngine.Core.Messages.Format(BlogEngine.Core.Messages.PasswordTooShort, 8)]);
            Assert.AreEqual("Il titolo è obbligatorio.", translator[BlogEngine.Core.Messages.TitleRequired]);
            Assert.AreEqual("Testo libero", translator["Testo libero"], "Un testo che non è una chiave resta com'è.");
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }
}

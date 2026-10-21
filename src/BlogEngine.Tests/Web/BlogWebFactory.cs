using System.Net;
using System.Text.RegularExpressions;
using BlogEngine.Core.Configuration;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

/// <summary>Il sito vero su una cartella dati temporanea, eliminata con Dispose.</summary>
public sealed partial class BlogWebFactory : WebApplicationFactory<Program>
{
    public const string AdminUser = "admin";
    public const string AdminPassword = "Password-Sicura-1";

    public string DataRoot { get; } = Path.Combine(Path.GetTempPath(), "be-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Servizi da sostituire nel sito di prova (es. la coda delle email).</summary>
    public Action<IServiceCollection>? TestServices { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("BlogEngine:DataPath", DataRoot);

        // Estensioni compilate una volta per tutti i test; nessun riavvio automatico durante i test.
        builder.UseSetting("BlogEngine:ExtensionCachePath", Path.Combine(Path.GetTempPath(), "be-tests-extensions"));
        builder.UseSetting("BlogEngine:RestartOnExtensionChange", "false");
        if (TestServices is not null)
        {
            builder.ConfigureTestServices(TestServices);
        }
    }

    public HttpClient CreateBrowser(bool followRedirects = true) =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = followRedirects, HandleCookies = true });

    public string ReadSetupKey() => File.ReadAllText(Path.Combine(DataRoot, InstallationState.SetupKeyFileName)).Trim();

    /// <summary>Completa la configurazione iniziale e restituisce un browser già autenticato come amministratore.</summary>
    public async Task<HttpClient> InstallAsync(DataProviderKind provider = DataProviderKind.Xml, string blogName = "Blog di prova")
    {
        var browser = CreateBrowser();
        await browser.GetAsync("/");
        var response = await browser.PostFormAsync("/setup", new()
        {
            ["Input.SetupKey"] = ReadSetupKey(),
            ["Input.DataProvider"] = provider.ToString(),
            ["Input.BlogName"] = blogName,
            ["Input.UserName"] = AdminUser,
            ["Input.Email"] = "admin@example.com",
            ["Input.Password"] = AdminPassword,
            ["Input.ConfirmPassword"] = AdminPassword,
        });
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);

        // Il modulo viene ripresentato (sempre con 200) anche in caso di errore: si verifica l'esito vero.
        Assert.IsTrue(File.Exists(Path.Combine(DataRoot, InstallationState.InstallationFileName)),
            "Configurazione non completata:\n" + await response.Content.ReadAsStringAsync());
        return browser;
    }

    /// <summary>Esegue codice con i servizi del sito come amministratore di sistema sul blog principale.</summary>
    public async Task RunAsSystemAsync(Func<IServiceProvider, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        var blogs = await scope.ServiceProvider.GetRequiredService<BlogEngine.Core.Services.BlogsCache>().GetAllAsync();
        var context = scope.ServiceProvider.GetRequiredService<RequestBlogContext>();
        context.RunAsSystem(blogs.First(b => b.IsPrimary));
        context.RequestBaseUri = new Uri("http://localhost/");
        await action(scope.ServiceProvider);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(DataRoot))
        {
            Directory.Delete(DataRoot, recursive: true);
        }
    }
}

public static partial class HttpClientFormExtensions
{
    /// <summary>Apre la pagina, legge il token antiforgery del modulo e invia il POST con i campi indicati.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient client, string url, Dictionary<string, string> fields)
    {
        var page = await client.GetStringAsync(url);
        var token = TokenRegex().Match(page);
        Assert.IsTrue(token.Success, $"Nessun token antiforgery in {url}.");
        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        return await client.PostAsync(url, new FormUrlEncodedContent(fields));
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenRegex();
}

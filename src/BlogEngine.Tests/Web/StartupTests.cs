using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Web;

[TestClass]
public sealed class StartupTests
{
    private string _dataRoot = null!;
    private WebApplicationFactory<Program> _factory = null!;

    [TestInitialize]
    public void Setup()
    {
        _dataRoot = Path.Combine(Path.GetTempPath(), "be-tests", Guid.NewGuid().ToString("N"));
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("BlogEngine:DataPath", _dataRoot));
    }

    [TestCleanup]
    public void Cleanup()
    {
        _factory.Dispose();
        if (Directory.Exists(_dataRoot))
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
    }

    [TestMethod]
    public async Task HomePage_RespondsOk()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [TestMethod]
    public void Startup_CreatesDataFoldersAndPersistsKeys()
    {
        // Accedere a Services avvia l'applicazione; proteggere un dato forza la creazione di una chiave.
        var provider = _factory.Services.GetRequiredService<IDataProtectionProvider>();
        provider.CreateProtector("test").Protect("ciao"u8.ToArray());

        Assert.IsTrue(Directory.Exists(Path.Combine(_dataRoot, "files")));
        Assert.IsTrue(Directory.EnumerateFiles(Path.Combine(_dataRoot, "keys"), "key-*.xml").Any(),
            "Le chiavi di Data Protection devono essere salvate in App_Data/keys.");
    }
}

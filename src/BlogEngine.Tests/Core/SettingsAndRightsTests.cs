using BlogEngine.Core.Services;
using BlogEngine.Events;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Settings;
using BlogEngine.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Core;

[TestClass]
public sealed class SettingsAndRightsTests
{
    [TestMethod]
    public void SettingsSerializer_RoundTripsAllValues()
    {
        var settings = new BlogSettings { Name = "Il mio blog", PostsPerPage = 7, GeocodingLatitude = 45.5f, CommentProvider = CommentProvider.Disqus, EnableRating = false };

        var restored = SettingsSerializer.Deserialize(SettingsSerializer.Serialize(settings));

        Assert.AreEqual("Il mio blog", restored.Name);
        Assert.AreEqual(7, restored.PostsPerPage);
        Assert.AreEqual(45.5f, restored.GeocodingLatitude);
        Assert.AreEqual(CommentProvider.Disqus, restored.CommentProvider);
        Assert.IsFalse(restored.EnableRating);
    }

    [TestMethod]
    public void SettingsSerializer_UsesLowercaseNamesAndInvariantCulture()
    {
        var values = SettingsSerializer.Serialize(new BlogSettings { GeocodingLatitude = 1.5f });

        Assert.AreEqual("1.5", values["geocodinglatitude"]);
        Assert.AreEqual("True", values["enablerating"]);
    }

    [TestMethod]
    public void SettingsSerializer_IgnoresUnknownAndInvalidValues()
    {
        var invalid = new List<string>();
        var settings = SettingsSerializer.Deserialize(
            new Dictionary<string, string> { ["postsperpage"] = "non è un numero", ["iscocommentenabled"] = "True", ["name"] = "Ok" },
            invalid);

        Assert.AreEqual(10, settings.PostsPerPage, "Il valore non valido lascia il predefinito.");
        Assert.AreEqual("Ok", settings.Name);
        CollectionAssert.AreEqual(new[] { "postsperpage" }, invalid);
    }

    [TestMethod]
    public async Task SettingsService_ReturnsCopiesAndSavesForAdministrators()
    {
        var events = new RecordingHandler<SettingsChangedEvent>();
        using var host = new TestBlogHost(s => s.AddSingleton<IEventHandler<SettingsChangedEvent>>(events)).AsAdmin();

        var settings = await host.Settings.GetAsync();
        settings.Name = "Nuovo nome";
        Assert.AreEqual("Name of the blog", (await host.Settings.GetAsync()).Name, "La copia non modifica la cache.");

        await host.Settings.SaveAsync(settings);

        Assert.AreEqual("Nuovo nome", (await host.Settings.GetAsync()).Name);
        Assert.AreEqual("Nuovo nome", host.Stores.StoredSettings(host.PrimaryBlog.Id)!["name"]);
        Assert.AreEqual(1, events.Received.Count);
    }

    [TestMethod]
    public async Task SettingsService_RejectsEditors()
    {
        using var host = new TestBlogHost().AsEditor();
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(async () => await host.Settings.SaveAsync(new BlogSettings()));
    }

    [TestMethod]
    public void RightsMatrix_DefaultsMatchOriginalInstallation()
    {
        var matrix = RightsMatrix.CreateDefault();

        Assert.IsTrue(matrix.HasRight(Rights.ManagePackages, [SystemRoles.Administrators]));
        Assert.IsFalse(matrix.HasRight(Rights.ManagePackages, [SystemRoles.Editors]));
        Assert.IsTrue(matrix.HasRight(Rights.PublishOwnPosts, [SystemRoles.Editors]));
        Assert.IsFalse(matrix.HasRight(Rights.EditOtherUsersPosts, [SystemRoles.Editors]));
        Assert.IsTrue(matrix.HasRight(Rights.CreateComments, [SystemRoles.Anonymous]));
        Assert.IsFalse(matrix.HasRight(Rights.AccessAdminPages, [SystemRoles.Anonymous]));
        Assert.IsFalse(matrix.HasRight(Rights.None, [SystemRoles.Administrators]));
    }

    [TestMethod]
    public void RightsMatrix_AdministratorsAlwaysHaveEveryRight()
    {
        var matrix = RightsMatrix.FromStoreFormat(new Dictionary<string, IReadOnlyCollection<string>>
        {
            ["ViewDashboard"] = ["Editors"],
            ["RightThatNoLongerExists"] = ["Editors"],
        });

        Assert.IsTrue(RightsMatrix.AllRights.All(r => matrix.HasRight(r, [SystemRoles.Administrators])));
        Assert.IsTrue(matrix.HasRight(Rights.ViewDashboard, ["editors"]), "I nomi dei ruoli non distinguono le maiuscole.");
    }

    [TestMethod]
    public async Task RightsService_SetRightsForRole_ReplacesRoleRights()
    {
        using var host = new TestBlogHost().AsAdmin();
        var rights = host.Get<IRightsService>();

        await rights.SetRightsForRoleAsync("Writers", [Rights.CreateNewPosts, Rights.EditOwnPosts]);
        await rights.SetRightsForRoleAsync("Writers", [Rights.CreateNewPosts]);

        CollectionAssert.AreEquivalent(new[] { Rights.CreateNewPosts }, (await rights.GetRightsForRoleAsync("Writers")).ToArray());
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => rights.SetRightsForRoleAsync(SystemRoles.Administrators, []));
    }

    [TestMethod]
    public async Task RightsService_EmptyRightsListIsNeverAuthorized()
    {
        using var host = new TestBlogHost().AsAdmin();
        Assert.IsFalse(await host.Get<IRightsService>().IsAuthorizedAsync(AuthorizationCheck.HasAny, []));
    }
}

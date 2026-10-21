using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Tests.Fakes;

namespace BlogEngine.Tests.Data;

/// <summary>
/// Scenari completi attraverso i servizi del Core su provider veri, con riavvio dell'applicazione
/// fra la scrittura e la lettura.
/// </summary>
[TestClass]
public sealed class ProviderPersistenceTests
{
    [DataTestMethod]
    [DataRow(StorageKind.Xml)]
    [DataRow(StorageKind.Sqlite)]
    public async Task PostsCommentsAndSettings_SurviveRestart(StorageKind kind)
    {
        using var storage = new TestStorage(kind);
        Guid postId;
        using (var host = new TestBlogHost(storage: storage).AsAdmin())
        {
            var settings = await host.Settings.GetAsync();
            settings.Name = "Blog persistente";
            settings.EnableCommentsModeration = false;
            await host.Settings.SaveAsync(settings);

            var category = new Category { Title = "Notizie" };
            await host.Categories.SaveAsync(category);

            var post = await host.CreatePostAsync("Sopravvive al riavvio", p => { p.Tags = ["persistenza"]; p.Categories = [category.Id]; });
            postId = post.Id;

            host.AsAnonymous();
            await host.Comments.AddAsync(post, new Comment { Author = "Lettore", Email = "l@x.it", Content = "Commento" });
            await host.Posts.RateAsync(post, 4);
        }

        using (var restarted = new TestBlogHost(storage: storage).AsAnonymous())
        {
            Assert.AreEqual("Blog persistente", (await restarted.Settings.GetAsync()).Name);

            var post = await restarted.Posts.GetBySlugAsync("sopravvive-al-riavvio");
            Assert.IsNotNull(post);
            Assert.AreEqual(postId, post.Id);
            Assert.AreEqual(4f, post.Rating);
            Assert.AreEqual("Notizie", (await restarted.Categories.GetByIdAsync(post.Categories.Single()))?.Title);
            Assert.AreEqual(1, (await restarted.Posts.GetByTagAsync("persistenza")).Count);

            var tree = await restarted.Comments.GetCommentTreeAsync(post);
            Assert.AreEqual("Commento", tree.Single().Comment.Content, "Il commento è stato approvato e salvato.");
        }
    }

    [DataTestMethod]
    [DataRow(StorageKind.Xml)]
    [DataRow(StorageKind.Sqlite)]
    public async Task RightsAndPages_SurviveRestart(StorageKind kind)
    {
        using var storage = new TestStorage(kind);
        using (var host = new TestBlogHost(storage: storage).AsAdmin())
        {
            await host.Get<IRightsService>().SetRightsForRoleAsync("Writers", [BlogEngine.Security.Rights.CreateNewPosts]);
            await host.Pages.SaveAsync(new Page { Title = "Contatti", Content = "x", IsPublished = true, IsFrontPage = true });
        }

        using var restarted = new TestBlogHost(storage: storage).AsAnonymous();
        CollectionAssert.AreEqual(
            new[] { BlogEngine.Security.Rights.CreateNewPosts },
            (await restarted.Get<IRightsService>().GetRightsForRoleAsync("Writers")).ToArray());
        Assert.AreEqual("Contatti", (await restarted.Pages.GetFrontPageAsync())?.Title);
    }

    [TestMethod]
    public async Task Xml_WritesReadableFilesInBlogFolder()
    {
        using var storage = new TestStorage(StorageKind.Xml);
        using var host = new TestBlogHost(storage: storage);
        var post = await host.CreatePostAsync("File leggibile");

        var file = Path.Combine(storage.Paths.Data, "blogs", host.PrimaryBlog.Id.ToString("N"), "posts", post.Id.ToString("N") + ".xml");
        Assert.IsTrue(File.Exists(file));
        StringAssert.Contains(await File.ReadAllTextAsync(file), "<title>File leggibile</title>");
        Assert.IsFalse(Directory.EnumerateFiles(storage.Paths.Data, "*.tmp", SearchOption.AllDirectories).Any(), "Nessun file temporaneo residuo.");
    }

    [TestMethod]
    public async Task Sqlite_CreatesDatabaseInDataFolder()
    {
        using var storage = new TestStorage(StorageKind.Sqlite);
        using var host = new TestBlogHost(storage: storage);
        await host.CreatePostAsync("Nel database");

        Assert.IsTrue(File.Exists(Path.Combine(storage.Paths.Data, BlogEngine.Data.Sqlite.ServiceCollectionExtensions.DatabaseFileName)));
    }
}

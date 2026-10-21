using BlogEngine.Core.Data;
using BlogEngine.Models;
using BlogEngine.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Data;

/// <summary>
/// Comportamento che ogni provider dati deve rispettare. Le classi derivate eseguono gli stessi test
/// sullo store in memoria, sul provider XML e su quello SQLite.
/// </summary>
public abstract class StoreContractTests
{
    private static readonly DateTime Created = new(2025, 5, 4, 3, 2, 1, DateTimeKind.Utc);

    private TestStorage _storage = null!;
    private readonly List<ServiceProvider> _providers = [];

    protected abstract StorageKind Kind { get; }

    [TestInitialize]
    public void Setup() => _storage = new TestStorage(Kind);

    [TestCleanup]
    public void Cleanup()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }

        _storage.Dispose();
    }

    /// <summary>Nuova istanza del provider sugli stessi dati: simula un riavvio dell'applicazione.</summary>
    private async Task<T> NewStoreAsync<T>()
        where T : class
    {
        var (provider, _) = await _storage.CreateStoreAsync();
        _providers.Add(provider);
        return provider.GetRequiredService<T>();
    }

    [TestMethod]
    public async Task Blogs_SaveUpdateDelete()
    {
        var store = await NewStoreAsync<IBlogStore>();
        var blog = new Blog { Name = "Figlio", VirtualPath = "~/figlio", Hostname = "x.it", IsSiteAggregation = true, DateCreated = Created };

        await store.SaveAsync(blog);
        await store.SaveAsync(new Blog { Id = blog.Id, Name = "Rinominato", VirtualPath = "~/figlio", DateCreated = Created });

        var loaded = (await (await NewStoreAsync<IBlogStore>()).GetAllAsync()).Single();
        Assert.AreEqual("Rinominato", loaded.Name);
        Assert.AreEqual("~/figlio", loaded.VirtualPath);
        Assert.AreEqual(Created, loaded.DateCreated);

        await store.DeleteAsync(blog.Id);
        Assert.AreEqual(0, (await (await NewStoreAsync<IBlogStore>()).GetAllAsync()).Count);
    }

    [TestMethod]
    public async Task Posts_RoundTripAllFieldsAndComments()
    {
        var blogId = Guid.NewGuid();
        var store = await NewStoreAsync<IEntityStore<Post>>();
        var category = Guid.NewGuid();
        var reply = Guid.NewGuid();
        var post = new Post
        {
            BlogId = blogId, Title = "Titolo <con> & caratteri", Slug = "titolo", Author = "mario", Description = "desc",
            Content = "<p>Contenuto con àccenti</p>", IsPublished = false, HasCommentsEnabled = false, Raters = 3, Rating = 4.25f,
            DateCreated = Created, DateModified = Created.AddHours(1),
            Categories = [category], Tags = ["uno", "due"], NotificationEmails = ["a@b.it"],
            AllComments =
            [
                new Comment { Id = reply, BlogId = blogId, Author = "Lettore", Email = "l@x.it", Website = new Uri("https://lettore.it/"), IP = "1.2.3.4",
                    Content = "Primo", DateCreated = Created, IsApproved = true, ModeratedBy = "Auto" },
                new Comment { BlogId = blogId, ParentId = reply, Author = "Altro", Email = "pingback", Content = "Risposta", DateCreated = Created, IsSpam = true },
            ],
        };
        foreach (var c in post.AllComments)
        {
            c.PostId = post.Id;
        }

        await store.SaveAsync(post);

        var loaded = (await (await NewStoreAsync<IEntityStore<Post>>()).GetAllAsync(blogId)).Single();
        Assert.AreEqual(post.Title, loaded.Title);
        Assert.AreEqual(post.Content, loaded.Content);
        Assert.IsFalse(loaded.IsPublished);
        Assert.IsFalse(loaded.HasCommentsEnabled);
        Assert.AreEqual(4.25f, loaded.Rating);
        Assert.AreEqual(Created, loaded.DateCreated);
        Assert.AreEqual(DateTimeKind.Utc, loaded.DateCreated.Kind);
        CollectionAssert.AreEqual(new[] { category }, loaded.Categories);
        CollectionAssert.AreEqual(new[] { "uno", "due" }, loaded.Tags);
        CollectionAssert.AreEqual(new[] { "a@b.it" }, loaded.NotificationEmails);

        Assert.AreEqual(2, loaded.AllComments.Count);
        var first = loaded.AllComments.Single(c => c.Id == reply);
        Assert.AreEqual(new Uri("https://lettore.it/"), first.Website);
        Assert.AreEqual(loaded.Id, first.PostId);
        Assert.IsTrue(first.IsApproved);
        Assert.AreEqual("Auto", first.ModeratedBy);
        Assert.IsTrue(loaded.AllComments.Single(c => c.Id != reply).IsPingbackOrTrackback);
    }

    [TestMethod]
    public async Task Posts_UpdateSynchronizesComments()
    {
        var blogId = Guid.NewGuid();
        var store = await NewStoreAsync<IEntityStore<Post>>();
        var kept = new Comment { BlogId = blogId, Content = "resta", DateCreated = Created };
        var removed = new Comment { BlogId = blogId, Content = "via", DateCreated = Created };
        var post = new Post { BlogId = blogId, Title = "t", Content = "c", DateCreated = Created, AllComments = [kept, removed] };
        await store.SaveAsync(post);

        var updated = new Post
        {
            Id = post.Id, BlogId = blogId, Title = "nuovo", Content = "c", DateCreated = Created,
            AllComments =
            [
                new Comment { Id = kept.Id, BlogId = blogId, PostId = post.Id, Content = "modificato", DateCreated = Created },
                new Comment { BlogId = blogId, PostId = post.Id, Content = "aggiunto", DateCreated = Created },
            ],
        };
        await store.SaveAsync(updated);

        var loaded = (await (await NewStoreAsync<IEntityStore<Post>>()).GetAllAsync(blogId)).Single();
        Assert.AreEqual("nuovo", loaded.Title);
        CollectionAssert.AreEquivalent(new[] { "modificato", "aggiunto" }, loaded.AllComments.Select(c => c.Content).ToArray());

        await store.DeleteAsync(blogId, post.Id);
        Assert.AreEqual(0, (await (await NewStoreAsync<IEntityStore<Post>>()).GetAllAsync(blogId)).Count);
    }

    [TestMethod]
    public async Task PagesCategoriesBlogRollReferrers_RoundTrip()
    {
        var blogId = Guid.NewGuid();
        var parent = Guid.NewGuid();
        await (await NewStoreAsync<IEntityStore<Page>>()).SaveAsync(
            new Page { BlogId = blogId, Title = "Chi sono", Slug = "chi-sono", Content = "x", Parent = parent, IsFrontPage = true, SortOrder = 3, DateCreated = Created });
        await (await NewStoreAsync<IEntityStore<Category>>()).SaveAsync(new Category { BlogId = blogId, Title = "Senza padre", DateCreated = Created });
        await (await NewStoreAsync<IEntityStore<Category>>()).SaveAsync(new Category { BlogId = blogId, Title = "Con padre", Parent = parent, DateCreated = Created });
        await (await NewStoreAsync<IEntityStore<BlogRollItem>>()).SaveAsync(
            new BlogRollItem { BlogId = blogId, Title = "Amico", BlogUrl = new Uri("https://amico.it/"), FeedUrl = new Uri("https://amico.it/feed"), Xfn = "friend", SortIndex = 2 });
        await (await NewStoreAsync<IEntityStore<Referrer>>()).SaveAsync(
            new Referrer { BlogId = blogId, Day = Created.Date, Url = new Uri("https://blog.it/post/x"), ReferrerUrl = new Uri("https://google.com/"), Count = 7 });

        var page = (await (await NewStoreAsync<IEntityStore<Page>>()).GetAllAsync(blogId)).Single();
        Assert.AreEqual(parent, page.Parent);
        Assert.IsTrue(page.IsFrontPage);
        Assert.AreEqual(3, page.SortOrder);

        var categories = await (await NewStoreAsync<IEntityStore<Category>>()).GetAllAsync(blogId);
        Assert.IsNull(categories.Single(c => c.Title == "Senza padre").Parent);
        Assert.AreEqual(parent, categories.Single(c => c.Title == "Con padre").Parent);

        var item = (await (await NewStoreAsync<IEntityStore<BlogRollItem>>()).GetAllAsync(blogId)).Single();
        Assert.AreEqual(new Uri("https://amico.it/feed"), item.FeedUrl);
        Assert.AreEqual("friend", item.Xfn);

        var referrer = (await (await NewStoreAsync<IEntityStore<Referrer>>()).GetAllAsync(blogId)).Single();
        Assert.AreEqual(7, referrer.Count);
        Assert.AreEqual(Created.Date, referrer.Day);

        await (await NewStoreAsync<IEntityStore<Referrer>>()).DeleteAsync(blogId, referrer.Id);
        Assert.AreEqual(0, (await (await NewStoreAsync<IEntityStore<Referrer>>()).GetAllAsync(blogId)).Count);
    }

    [TestMethod]
    public async Task ProfilesUsersRoles_AreKeyedByNameIgnoringCase()
    {
        var blogId = Guid.NewGuid();
        var profiles = await NewStoreAsync<IProfileStore>();
        await profiles.SaveAsync(new AuthorProfile { BlogId = blogId, UserName = "Mario", DisplayName = "Mario R.", Birthday = Created });
        await profiles.SaveAsync(new AuthorProfile { BlogId = blogId, UserName = "mario", DisplayName = "Aggiornato" });
        var profile = (await (await NewStoreAsync<IProfileStore>()).GetAllAsync(blogId)).Single();
        Assert.AreEqual("Aggiornato", profile.DisplayName);

        var users = await NewStoreAsync<IUserStore>();
        await users.SaveAsync(new BlogUser { BlogId = blogId, UserName = "Admin", Email = "a@b.it", PasswordHash = "hash", LastLoginTime = Created });
        await users.SaveAsync(new BlogUser { BlogId = blogId, UserName = "Editor", Email = "e@b.it", PasswordHash = "h2" });
        await users.DeleteAsync(blogId, "EDITOR");
        var user = (await (await NewStoreAsync<IUserStore>()).GetAllAsync(blogId)).Single();
        Assert.AreEqual("hash", user.PasswordHash);
        Assert.AreEqual(Created, user.LastLoginTime);

        var roles = await NewStoreAsync<IRoleStore>();
        await roles.SaveAsync(new BlogRole { BlogId = blogId, Name = "Editors", Users = ["mario", "luigi"] });
        await roles.SaveAsync(new BlogRole { BlogId = blogId, Name = "editors", Users = ["mario"] });
        var role = (await (await NewStoreAsync<IRoleStore>()).GetAllAsync(blogId)).Single();
        CollectionAssert.AreEqual(new[] { "mario" }, role.Users);

        await profiles.DeleteAsync(blogId, "MARIO");
        Assert.AreEqual(0, (await (await NewStoreAsync<IProfileStore>()).GetAllAsync(blogId)).Count);
    }

    [TestMethod]
    public async Task SettingsRightsAndLists_AreReplacedAsAWhole()
    {
        var blogId = Guid.NewGuid();
        var settings = await NewStoreAsync<ISettingsStore>();
        await settings.SaveAsync(blogId, new Dictionary<string, string> { ["name"] = "Primo", ["obsoleta"] = "x" });
        await settings.SaveAsync(blogId, new Dictionary<string, string> { ["name"] = "Il mio <blog>", ["postsperpage"] = "5" });
        var loadedSettings = await (await NewStoreAsync<ISettingsStore>()).LoadAsync(blogId);
        Assert.AreEqual(2, loadedSettings.Count);
        Assert.AreEqual("Il mio <blog>", loadedSettings["name"]);

        var rights = await NewStoreAsync<IRightsStore>();
        await rights.SaveAsync(blogId, new Dictionary<string, IReadOnlyCollection<string>> { ["ViewDashboard"] = ["Administrators", "Editors"], ["CreateComments"] = [] });
        var loadedRights = await (await NewStoreAsync<IRightsStore>()).LoadAsync(blogId);
        CollectionAssert.AreEquivalent(new[] { "Administrators", "Editors" }, loadedRights["ViewDashboard"].ToArray());

        var lists = await NewStoreAsync<IStringListStore>();
        await lists.SaveAsync(blogId, StringListKind.PingServices, ["https://b.it", "https://a.it"]);
        await lists.SaveAsync(blogId, StringListKind.StopWords, ["il", "lo"]);
        var reloadedLists = await NewStoreAsync<IStringListStore>();
        CollectionAssert.AreEqual(new[] { "https://b.it", "https://a.it" }, (await reloadedLists.LoadAsync(blogId, StringListKind.PingServices)).ToArray());
        CollectionAssert.AreEqual(new[] { "il", "lo" }, (await reloadedLists.LoadAsync(blogId, StringListKind.StopWords)).ToArray());

        var filters = await NewStoreAsync<ICommentFilterStore>();
        await filters.SaveRulesAsync(blogId,
        [
            new CommentFilterRule { Action = CommentFilterAction.Block, Subject = CommentFilterSubject.Email, Filter = "z" },
            new CommentFilterRule { Action = CommentFilterAction.Delete, Subject = CommentFilterSubject.Comment, Operator = CommentFilterOperator.Contains, Filter = "a" },
        ]);
        var rules = await (await NewStoreAsync<ICommentFilterStore>()).GetRulesAsync(blogId);
        CollectionAssert.AreEqual(new[] { "z", "a" }, rules.Select(r => r.Filter).ToArray(), "L'ordine delle regole va conservato.");
        Assert.AreEqual(CommentFilterAction.Delete, rules[1].Action);
        Assert.AreEqual(CommentFilterOperator.Contains, rules[1].Operator);
    }

    [TestMethod]
    public async Task CustomFieldsAndExtensionData_RoundTrip()
    {
        var blogId = Guid.NewGuid();
        var fields = await NewStoreAsync<ICustomFieldStore>();
        await fields.SaveAsync(new CustomField { BlogId = blogId, CustomType = "POST", ObjectId = "1", Key = "colore", Value = "rosso" });
        await fields.SaveAsync(new CustomField { BlogId = blogId, CustomType = "post", ObjectId = "1", Key = "COLORE", Value = "blu" });
        await fields.SaveAsync(new CustomField { BlogId = blogId, CustomType = "POST", ObjectId = "2", Key = "colore", Value = "verde" });
        await fields.DeleteAsync(new CustomField { BlogId = blogId, CustomType = "POST", ObjectId = "2", Key = "colore" });
        var field = (await (await NewStoreAsync<ICustomFieldStore>()).GetAllAsync(blogId)).Single();
        Assert.AreEqual("blu", field.Value);

        var data = await NewStoreAsync<IExtensionDataStore>();
        const string id = "Akismet/Filtro: \"spam\"";
        await data.SaveAsync(blogId, ExtensionDataKind.Extension, id, "{\"key\":\"valore\"}");
        Assert.AreEqual("{\"key\":\"valore\"}", await (await NewStoreAsync<IExtensionDataStore>()).LoadAsync(blogId, ExtensionDataKind.Extension, id));
        Assert.IsNull(await data.LoadAsync(blogId, ExtensionDataKind.Widget, id), "Tipi diversi non si sovrappongono.");

        await data.RemoveAsync(blogId, ExtensionDataKind.Extension, id);
        Assert.IsNull(await (await NewStoreAsync<IExtensionDataStore>()).LoadAsync(blogId, ExtensionDataKind.Extension, id));
    }

    [TestMethod]
    public async Task Data_IsIsolatedPerBlogAndDeletedWithIt()
    {
        var blogs = await NewStoreAsync<IBlogStore>();
        var posts = await NewStoreAsync<IEntityStore<Post>>();
        var settings = await NewStoreAsync<ISettingsStore>();
        var first = new Blog { Name = "Uno" };
        var second = new Blog { Name = "Due" };
        await blogs.SaveAsync(first);
        await blogs.SaveAsync(second);
        await posts.SaveAsync(new Post { BlogId = first.Id, Title = "Del primo", Content = "x" });
        await posts.SaveAsync(new Post { BlogId = second.Id, Title = "Del secondo", Content = "x" });
        await settings.SaveAsync(first.Id, new Dictionary<string, string> { ["name"] = "Uno" });

        Assert.AreEqual("Del secondo", (await posts.GetAllAsync(second.Id)).Single().Title);

        await blogs.DeleteAsync(first.Id);

        var reloaded = await NewStoreAsync<IEntityStore<Post>>();
        Assert.AreEqual(0, (await reloaded.GetAllAsync(first.Id)).Count);
        Assert.AreEqual(0, (await (await NewStoreAsync<ISettingsStore>()).LoadAsync(first.Id)).Count);
        Assert.AreEqual(1, (await reloaded.GetAllAsync(second.Id)).Count);
        Assert.AreEqual("Due", (await (await NewStoreAsync<IBlogStore>()).GetAllAsync()).Single().Name);
    }

    [TestMethod]
    public async Task Initialize_CanRunMoreThanOnce()
    {
        await NewStoreAsync<IBlogStore>();
        var (provider, _) = await _storage.CreateStoreAsync();
        _providers.Add(provider);
        await provider.GetRequiredService<IDataStoreInitializer>().InitializeAsync();
    }
}

[TestClass]
public sealed class MemoryStoreContractTests : StoreContractTests
{
    protected override StorageKind Kind => StorageKind.Memory;
}

[TestClass]
public sealed class XmlStoreContractTests : StoreContractTests
{
    protected override StorageKind Kind => StorageKind.Xml;
}

[TestClass]
public sealed class SqliteStoreContractTests : StoreContractTests
{
    protected override StorageKind Kind => StorageKind.Sqlite;
}

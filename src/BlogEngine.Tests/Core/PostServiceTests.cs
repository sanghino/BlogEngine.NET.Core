using BlogEngine.Core;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Core;

[TestClass]
public sealed class PostServiceTests
{
    [TestMethod]
    public async Task Save_NewPost_FillsDefaultsAndPersists()
    {
        using var host = new TestBlogHost().AsEditor("mario");
        var post = new Post { Title = "Il mio primo post!", Content = "Ciao" };

        await host.Posts.SaveAsync(post);

        Assert.AreEqual("Il-mio-primo-post", post.Slug);
        Assert.AreEqual("mario", post.Author);
        Assert.AreEqual(host.PrimaryBlog.Id, post.BlogId);
        Assert.AreEqual(TestBlogHost.StartTime.UtcDateTime, post.DateCreated);
        Assert.AreEqual(1, host.Stores.Stored<Post>().Count);
    }

    [TestMethod]
    public async Task Save_DuplicateTitle_GetsNumberedSlug()
    {
        using var host = new TestBlogHost();
        var first = await host.CreatePostAsync("Stesso titolo");
        var second = await host.CreatePostAsync("Stesso titolo");
        var third = await host.CreatePostAsync("Stesso titolo");

        Assert.AreEqual("Stesso-titolo", first.Slug);
        Assert.AreEqual("Stesso-titolo1", second.Slug);
        Assert.AreEqual("Stesso-titolo2", third.Slug);
    }

    [TestMethod]
    public async Task Save_WithoutTitleOrContent_IsRejected()
    {
        using var host = new TestBlogHost().AsAdmin();

        var error = await Assert.ThrowsExceptionAsync<BlogValidationException>(() => host.Posts.SaveAsync(new Post()));

        CollectionAssert.AreEquivalent(new[] { "Title", "Content" }, error.Errors.Keys.ToArray());
    }

    [TestMethod]
    public async Task Save_ChecksRights()
    {
        using var host = new TestBlogHost();
        var othersPost = await host.CreatePostAsync("Di un altro", p => p.Author = "admin");

        host.AsAnonymous();
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => host.Posts.SaveAsync(new Post { Title = "x", Content = "x" }));

        host.AsEditor("mario");
        othersPost.Title = "Modificato";
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => host.Posts.SaveAsync(othersPost));

        // Un editor non può "appropriarsi" del post di un altro cambiandone l'autore.
        othersPost.Author = "mario";
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => host.Posts.SaveAsync(othersPost));

        // Né pubblicare un post a nome di un altro.
        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() =>
            host.Posts.SaveAsync(new Post { Title = "x", Content = "x", Author = "admin", IsPublished = true }));
    }

    [TestMethod]
    public async Task Save_RaisesPublishedOnlyWhenPostBecomesPublic()
    {
        var published = new RecordingHandler<PostPublishedEvent>();
        using var host = new TestBlogHost(s => s.AddSingleton<IEventHandler<PostPublishedEvent>>(published)).AsAdmin();

        var draft = new Post { Title = "Bozza", Content = "x", IsPublished = false };
        await host.Posts.SaveAsync(draft);
        Assert.AreEqual(0, published.Received.Count);

        draft.IsPublished = true;
        await host.Posts.SaveAsync(draft);
        Assert.AreEqual(1, published.Received.Count);

        draft.Content = "modificato";
        await host.Posts.SaveAsync(draft);
        Assert.AreEqual(1, published.Received.Count, "Un post già pubblico non viene ripubblicato.");
    }

    [TestMethod]
    public async Task Save_PublishingCanBeCancelledByHandler()
    {
        var veto = new RecordingHandler<PostPublishingEvent>(e => e.Cancel = true);
        using var host = new TestBlogHost(s => s.AddSingleton<IEventHandler<PostPublishingEvent>>(veto)).AsAdmin();
        var post = new Post { Title = "Bloccato", Content = "x", IsPublished = true };

        await host.Posts.SaveAsync(post);

        Assert.IsFalse(post.IsPublished);
    }

    [TestMethod]
    public async Task Visibility_HidesDraftsAndScheduledPostsFromVisitors()
    {
        using var host = new TestBlogHost();
        await host.CreatePostAsync("Pubblico");
        await host.CreatePostAsync("Bozza", p => p.IsPublished = false);
        var scheduled = await host.CreatePostAsync("Programmato", p => p.DateCreated = TestBlogHost.StartTime.UtcDateTime.AddDays(1));

        host.AsAnonymous();
        CollectionAssert.AreEqual(new[] { "Pubblico" }, (await host.Posts.GetVisiblePostsAsync()).Select(p => p.Title).ToArray());

        host.AsEditor();
        Assert.AreEqual(3, (await host.Posts.GetVisiblePostsAsync()).Count);

        host.AsAnonymous();
        host.Clock.Now = TestBlogHost.StartTime.AddDays(2);
        Assert.IsTrue(await host.Posts.IsVisibleAsync(scheduled), "Alla data prevista il post diventa pubblico.");
    }

    [TestMethod]
    public async Task Queries_FilterByTagCategoryAuthorAndDate()
    {
        using var host = new TestBlogHost().AsAdmin();
        var category = new Category { Title = "Sviluppo" };
        await host.Categories.SaveAsync(category);

        await host.CreatePostAsync("A", p => { p.Tags = ["ASP.NET Core"]; p.Categories = [category.Id]; p.Author = "Mario Rossi"; });
        await host.CreatePostAsync("B", p => p.DateCreated = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        host.AsAnonymous();
        Assert.AreEqual("A", (await host.Posts.GetByTagAsync("aspnet-core")).Single().Title);
        Assert.AreEqual("A", (await host.Posts.GetByCategoryAsync(category.Id)).Single().Title);
        Assert.AreEqual("A", (await host.Posts.GetByAuthorAsync("mario-rossi")).Single().Title);
        Assert.AreEqual("B", (await host.Posts.GetByDateAsync(new DateTime(2025, 6, 1), new DateTime(2025, 6, 30))).Single().Title);
    }

    [TestMethod]
    public async Task GetAdjacent_ReturnsOlderAndNewerPost()
    {
        using var host = new TestBlogHost();
        var oldest = await host.CreatePostAsync("1", p => p.DateCreated = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var middle = await host.CreatePostAsync("2", p => p.DateCreated = new DateTime(2025, 2, 1, 0, 0, 0, DateTimeKind.Utc));
        var newest = await host.CreatePostAsync("3", p => p.DateCreated = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc));

        var (previous, next) = await host.Posts.GetAdjacentAsync(middle);

        Assert.AreSame(oldest, previous);
        Assert.AreSame(newest, next);
    }

    [TestMethod]
    public async Task DeleteRestorePurge_MovesPostThroughTrash()
    {
        using var host = new TestBlogHost();
        var post = await host.CreatePostAsync("Da eliminare");
        host.AsAdmin();

        await host.Posts.DeleteAsync(post);
        Assert.AreEqual(0, (await host.Posts.GetPostsAsync()).Count);
        Assert.AreEqual(1, (await host.Posts.GetDeletedPostsAsync()).Count);

        await host.Posts.RestoreAsync(post);
        Assert.AreEqual(1, (await host.Posts.GetPostsAsync()).Count);

        await host.Posts.PurgeAsync(post);
        Assert.AreEqual(0, host.Stores.Stored<Post>().Count);
        Assert.IsNull(await host.Posts.GetByIdAsync(post.Id));
    }

    [TestMethod]
    public async Task Rate_UpdatesAverage()
    {
        using var host = new TestBlogHost();
        var post = await host.CreatePostAsync("Da votare");
        host.AsAnonymous();

        await host.Posts.RateAsync(post, 5);
        await host.Posts.RateAsync(post, 2);

        Assert.AreEqual(2, post.Raters);
        Assert.AreEqual(3.5f, post.Rating);
        await Assert.ThrowsExceptionAsync<ArgumentOutOfRangeException>(() => host.Posts.RateAsync(post, 6));
    }

    [TestMethod]
    public async Task Cache_LoadsBlogDataOnlyOnce()
    {
        using var host = new TestBlogHost();
        await host.CreatePostAsync("Uno");
        var loads = host.Stores.LoadCount;

        await host.Posts.GetPostsAsync();
        await host.Posts.GetVisiblePostsAsync();

        Assert.AreEqual(loads, host.Stores.LoadCount);
    }
}

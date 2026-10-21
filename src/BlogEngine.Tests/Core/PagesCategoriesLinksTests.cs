using BlogEngine.Core;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Core;

[TestClass]
public sealed class PagesCategoriesLinksTests
{
    [TestMethod]
    public async Task Pages_OnlyOneFrontPage()
    {
        using var host = new TestBlogHost().AsAdmin();
        var first = new Page { Title = "Home", Content = "x", IsPublished = true, IsFrontPage = true };
        var second = new Page { Title = "Nuova home", Content = "x", IsPublished = true, IsFrontPage = true };

        await host.Pages.SaveAsync(first);
        await host.Pages.SaveAsync(second);

        Assert.IsFalse(first.IsFrontPage);
        Assert.AreSame(second, await host.Pages.GetFrontPageAsync());
    }

    [TestMethod]
    public async Task Pages_DraftsAreHiddenFromVisitors()
    {
        using var host = new TestBlogHost().AsAdmin();
        await host.Pages.SaveAsync(new Page { Title = "Chi sono", Content = "x", IsPublished = true });
        await host.Pages.SaveAsync(new Page { Title = "In lavorazione", Content = "x" });

        host.AsAnonymous();
        Assert.AreEqual("Chi-sono", (await host.Pages.GetVisiblePagesAsync()).Single().Slug);
        Assert.IsNotNull(await host.Pages.GetBySlugAsync("chi-sono"));
    }

    [TestMethod]
    public async Task Categories_CompleteTitleAndValidation()
    {
        using var host = new TestBlogHost().AsAdmin();
        var parent = new Category { Title = "Sviluppo" };
        var child = new Category { Title = ".NET", Parent = parent.Id };
        await host.Categories.SaveAsync(parent);
        await host.Categories.SaveAsync(child);

        Assert.AreEqual("Sviluppo - .NET", await host.Categories.GetCompleteTitleAsync(child));
        await Assert.ThrowsExceptionAsync<BlogValidationException>(() => host.Categories.SaveAsync(new Category { Title = "sviluppo" }));

        parent.Parent = child.Id;
        await Assert.ThrowsExceptionAsync<BlogValidationException>(() => host.Categories.SaveAsync(parent), "Le gerarchie circolari non sono ammesse.");
    }

    [TestMethod]
    public async Task Categories_DeleteMovesChildrenToTopLevel()
    {
        using var host = new TestBlogHost().AsAdmin();
        var parent = new Category { Title = "Padre" };
        var child = new Category { Title = "Figlio", Parent = parent.Id };
        await host.Categories.SaveAsync(parent);
        await host.Categories.SaveAsync(child);

        await host.Categories.DeleteAsync(parent);

        Assert.IsNull(child.Parent);
        Assert.AreEqual(1, (await host.Categories.GetCategoriesAsync()).Count);
    }

    [TestMethod]
    public async Task Links_FollowOriginalUrlStructure()
    {
        using var host = new TestBlogHost();
        var post = await host.CreatePostAsync("Ciao mondo", p => p.DateCreated = new DateTime(2025, 3, 9, 0, 0, 0, DateTimeKind.Utc));
        var links = host.Get<ILinkGenerator>();

        Assert.AreEqual("/post/Ciao-mondo", await links.PostAsync(post));
        Assert.AreEqual("/post/Ciao-mondo#id_" + Guid.Empty, await links.CommentAsync(post, new Comment { Id = Guid.Empty }));
        Assert.AreEqual("/category/Sviluppo-web", await links.CategoryAsync(new Category { Title = "Sviluppo web" }));
        Assert.AreEqual("/tag/aspnet", links.Tag("aspnet"));
        Assert.AreEqual("/search?q=a%20b", links.Search("a b"));
        Assert.AreEqual(new Uri("https://localhost:5001/post/Ciao-mondo"), links.ToAbsolute(await links.PostAsync(post)));

        host.AsAdmin();
        var settings = await host.Settings.GetAsync();
        settings.TimeStampPostLinks = true;
        await host.Settings.SaveAsync(settings);
        Assert.AreEqual("/post/2025/03/09/Ciao-mondo", await links.PostAsync(post));
    }

    [TestMethod]
    public async Task Links_PostsOfOtherBlogsUseTheirRoot()
    {
        using var host = new TestBlogHost();
        var child = new Blog { Name = "Figlio", VirtualPath = "~/figlio" };
        var hosted = new Blog { Name = "Esterno", VirtualPath = "~/", Hostname = "altro.example.com" };
        host.AsAdmin();
        await host.Get<IBlogService>().SaveAsync(child);
        await host.Get<IBlogService>().SaveAsync(hosted);
        var links = host.Get<ILinkGenerator>();

        Assert.AreEqual("/figlio/post/x", await links.PostAsync(new Post { Slug = "x", BlogId = child.Id }));
        Assert.AreEqual("https://altro.example.com/post/x", await links.PostAsync(new Post { Slug = "x", BlogId = hosted.Id }));
    }

    [TestMethod]
    public async Task Blogs_PrimaryCannotBeDeletedAndDeletionClearsData()
    {
        using var host = new TestBlogHost().AsAdmin();
        var blogs = host.Get<IBlogService>();
        var child = new Blog { Name = "Figlio", VirtualPath = "~/figlio" };
        await blogs.SaveAsync(child);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => blogs.DeleteAsync(host.PrimaryBlog));

        await blogs.DeleteAsync(child);
        Assert.AreEqual(1, (await blogs.GetBlogsAsync()).Count);
        Assert.AreSame(host.PrimaryBlog, await blogs.ResolveAsync("localhost", "/figlio/post/x"));
    }

    [TestMethod]
    public async Task SiteAggregation_ShowsPostsOfAllBlogs()
    {
        using var host = new TestBlogHost().AsAdmin();
        var child = new Blog { Name = "Figlio", VirtualPath = "~/figlio" };
        await host.Get<IBlogService>().SaveAsync(child);
        await host.CreatePostAsync("Nel principale");
        await host.CreatePostAsync("Nel figlio", p => p.BlogId = child.Id);

        Assert.AreEqual(1, (await host.Posts.GetPostsAsync()).Count);

        host.PrimaryBlog.IsSiteAggregation = true;
        Assert.AreEqual(2, (await host.Posts.GetPostsAsync()).Count);
    }

    [TestMethod]
    public async Task Events_FaultyHandlerDoesNotStopOthersAndOrderIsRespected()
    {
        var calls = new List<string>();
        using var host = new TestBlogHost(s =>
        {
            s.AddSingleton<IEventHandler<SavedEvent<Post>>>(new RecordingHandler<SavedEvent<Post>>(_ => calls.Add("secondo"), order: 10));
            s.AddSingleton<IEventHandler<SavedEvent<Post>>>(new RecordingHandler<SavedEvent<Post>>(_ => throw new InvalidOperationException("estensione rotta"), order: 0));
            s.AddSingleton<IEventHandler<SavedEvent<Post>>>(new RecordingHandler<SavedEvent<Post>>(_ => calls.Add("primo"), order: -5));
        });

        await host.CreatePostAsync("Post");

        CollectionAssert.AreEqual(new[] { "primo", "secondo" }, calls);
    }
}

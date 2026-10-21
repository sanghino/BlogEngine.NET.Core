using BlogEngine.Core.Services;
using BlogEngine.Models;

namespace BlogEngine.Tests.Core;

[TestClass]
public sealed class BlogResolverTests
{
    private static readonly Blog Primary = new() { Name = "Primary", IsPrimary = true, VirtualPath = "~/" };
    private static readonly Blog Child = new() { Name = "Child", VirtualPath = "~/child" };
    private static readonly Blog Hosted = new() { Name = "Hosted", VirtualPath = "~/", Hostname = "altro.example.com" };
    private static readonly Blog AnySubdomain = new() { Name = "Sub", VirtualPath = "~/", Hostname = "example.org", IsAnyTextBeforeHostnameAccepted = true };
    private static readonly Blog Inactive = new() { Name = "Inactive", VirtualPath = "~/off", IsActive = false };

    private static readonly IReadOnlyList<Blog> All = [Primary, Child, Hosted, AnySubdomain, Inactive];

    [DataTestMethod]
    [DataRow("~/", "/")]
    [DataRow("~/child", "/child/")]
    [DataRow("~/child/", "/child/")]
    [DataRow("", "/")]
    public void RelativeRoot_NormalizesVirtualPath(string virtualPath, string expected)
    {
        Assert.AreEqual(expected, BlogResolver.RelativeRoot(new Blog { VirtualPath = virtualPath }));
    }

    [TestMethod]
    public void Resolve_MatchesPathPrefix()
    {
        Assert.AreSame(Child, BlogResolver.Resolve(All, "www.sito.it", "/child/post/ciao"));
        Assert.AreSame(Child, BlogResolver.Resolve(All, "www.sito.it", "/child"));
        Assert.AreSame(Primary, BlogResolver.Resolve(All, "www.sito.it", "/children"));
    }

    [TestMethod]
    public void Resolve_MatchesHostname()
    {
        Assert.AreSame(Hosted, BlogResolver.Resolve(All, "altro.example.com", "/"));
        Assert.AreSame(AnySubdomain, BlogResolver.Resolve(All, "www.example.org", "/post/x"));
        Assert.AreSame(Primary, BlogResolver.Resolve(All, "www.altro.example.com", "/"));
    }

    [TestMethod]
    public void Resolve_ExplicitIdWinsWhenActive()
    {
        Assert.AreSame(Child, BlogResolver.Resolve(All, "altro.example.com", "/", Child.Id));
        Assert.AreSame(Primary, BlogResolver.Resolve(All, "www.sito.it", "/", Inactive.Id));
    }

    [TestMethod]
    public void Resolve_IgnoresInactiveBlogs()
    {
        Assert.AreSame(Primary, BlogResolver.Resolve(All, "www.sito.it", "/off/post/x"));
    }
}

using BlogEngine.Core.Data;
using BlogEngine.Core.Services;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Tests.Core;

[TestClass]
public sealed class CommentServiceTests
{
    private static Comment NewComment(string content = "Bel post", string email = "lettore@example.com", string ip = "10.0.0.1") =>
        new() { Author = "Lettore", Email = email, IP = ip, Content = content };

    [TestMethod]
    public async Task Add_WithModeration_LeavesCommentPending()
    {
        using var host = new TestBlogHost();
        var post = await host.CreatePostAsync("Post");
        host.AsAnonymous();

        var comment = NewComment();
        Assert.IsTrue(await host.Comments.AddAsync(post, comment));

        Assert.IsFalse(comment.IsApproved);
        Assert.AreEqual(post.Id, comment.PostId);
        Assert.AreEqual(1, post.NotApprovedComments.Count());
        Assert.AreEqual(0, (await host.Comments.GetCommentTreeAsync(post)).Count, "I visitatori non vedono i commenti in attesa.");
    }

    [TestMethod]
    public async Task Add_WithoutModeration_ApprovesAutomatically()
    {
        using var host = new TestBlogHost().AsAdmin();
        var settings = await host.Settings.GetAsync();
        settings.EnableCommentsModeration = false;
        await host.Settings.SaveAsync(settings);
        var post = await host.CreatePostAsync("Post");
        host.AsAnonymous();

        var comment = NewComment();
        await host.Comments.AddAsync(post, comment);

        Assert.IsTrue(comment.IsApproved);
        Assert.AreEqual(CommentModerationHandler.AutoModerator, comment.ModeratedBy);
    }

    [TestMethod]
    public async Task Add_ByAuthenticatedUser_IsTrusted()
    {
        using var host = new TestBlogHost();
        var post = await host.CreatePostAsync("Post");
        host.AsEditor();

        var comment = NewComment();
        await host.Comments.AddAsync(post, comment);

        Assert.IsTrue(comment.IsApproved);
        Assert.AreEqual("Rule:authenticated", comment.ModeratedBy);
    }

    [TestMethod]
    public async Task Add_AfterTenApprovedComments_AuthorIsWhiteListed()
    {
        using var host = new TestBlogHost();
        var post = await host.CreatePostAsync("Post", p =>
            p.AllComments.AddRange(Enumerable.Range(0, 10).Select(_ => new Comment { Email = "fidato@example.com", IsApproved = true })));
        host.AsAnonymous();

        var comment = NewComment(email: "FIDATO@example.com");
        await host.Comments.AddAsync(post, comment);

        Assert.IsTrue(comment.IsApproved);
        Assert.AreEqual("Rule:white list", comment.ModeratedBy);
    }

    [TestMethod]
    public async Task Add_FilterRules_BlockOrDiscardComments()
    {
        using var host = new TestBlogHost();
        await ((ICommentFilterStore)host.Stores).SaveRulesAsync(host.PrimaryBlog.Id,
        [
            new CommentFilterRule { Action = CommentFilterAction.Delete, Subject = CommentFilterSubject.Comment, Operator = CommentFilterOperator.Contains, Filter = "casino" },
            new CommentFilterRule { Action = CommentFilterAction.Block, Subject = CommentFilterSubject.IP, Operator = CommentFilterOperator.Equals, Filter = "6.6.6.6" },
        ]);
        var post = await host.CreatePostAsync("Post");
        host.AsAnonymous();

        Assert.IsFalse(await host.Comments.AddAsync(post, NewComment("Vinci al CASINO!")), "La regola Delete scarta il commento.");

        var blocked = NewComment(ip: "6.6.6.6");
        Assert.IsTrue(await host.Comments.AddAsync(post, blocked));
        Assert.IsTrue(blocked.IsSpam);
        Assert.AreEqual(1, post.AllComments.Count);
    }

    [TestMethod]
    public async Task Add_SpamFilterCanRejectComment()
    {
        using var host = new TestBlogHost(s => s.AddSingleton<ICommentSpamFilter>(new KeywordSpamFilter("viagra")));
        var post = await host.CreatePostAsync("Post");
        host.AsAnonymous();

        var spam = NewComment("compra viagra");
        await host.Comments.AddAsync(post, spam);

        Assert.IsTrue(spam.IsSpam);
        Assert.AreEqual("Keyword", spam.ModeratedBy);
    }

    [TestMethod]
    public async Task Add_FailsWhenCommentsAreClosed()
    {
        using var host = new TestBlogHost().AsAdmin();
        var settings = await host.Settings.GetAsync();
        settings.DaysCommentsAreEnabled = 30;
        await host.Settings.SaveAsync(settings);
        var post = await host.CreatePostAsync("Vecchio");
        host.AsAnonymous();

        host.Clock.Now = TestBlogHost.StartTime.AddDays(31);

        Assert.IsFalse(await host.Comments.AreCommentsOpenAsync(post));
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => host.Comments.AddAsync(post, NewComment()));
    }

    [TestMethod]
    public async Task Moderation_ApproveDisapproveRemoveRestorePurge()
    {
        var changed = new RecordingHandler<CommentChangedEvent>();
        using var host = new TestBlogHost(s => s.AddSingleton<IEventHandler<CommentChangedEvent>>(changed));
        var post = await host.CreatePostAsync("Post");
        host.AsAnonymous();
        var comment = NewComment();
        await host.Comments.AddAsync(post, comment);

        await Assert.ThrowsExceptionAsync<UnauthorizedAccessException>(() => host.Comments.ApproveAsync(post, comment));

        host.AsEditor();
        await host.Comments.ApproveAsync(post, comment);
        Assert.IsTrue(comment.IsApproved);
        Assert.AreEqual("editor", comment.ModeratedBy);

        await host.Comments.DisapproveAsync(post, comment);
        Assert.IsTrue(comment.IsSpam);

        await host.Comments.RemoveAsync(post, comment);
        Assert.AreEqual(1, post.DeletedComments.Count());

        await host.Comments.RestoreAsync(post, comment);
        await host.Comments.PurgeAsync(post, comment);
        Assert.AreEqual(0, post.AllComments.Count);

        CollectionAssert.AreEqual(
            new[] { CommentAction.Add, CommentAction.Approve, CommentAction.Disapprove, CommentAction.Remove, CommentAction.Restore, CommentAction.Purge },
            changed.Received.Select(e => e.Action).ToArray());
    }

    [TestMethod]
    public async Task CommentTree_NestsReplies()
    {
        using var host = new TestBlogHost();
        var root = new Comment { Content = "radice", Email = "a@a.it", IsApproved = true, DateCreated = new DateTime(2025, 1, 1) };
        var reply = new Comment { Content = "risposta", Email = "b@b.it", IsApproved = true, ParentId = root.Id, DateCreated = new DateTime(2025, 1, 2) };
        var orphan = new Comment { Content = "orfano", Email = "c@c.it", IsApproved = true, ParentId = Guid.NewGuid(), DateCreated = new DateTime(2025, 1, 3) };
        var post = await host.CreatePostAsync("Post", p => p.AllComments = [orphan, reply, root]);
        host.AsAnonymous();

        var tree = await host.Comments.GetCommentTreeAsync(post);

        CollectionAssert.AreEqual(new[] { "radice", "orfano" }, tree.Select(n => n.Comment.Content).ToArray());
        Assert.AreEqual("risposta", tree[0].Replies.Single().Comment.Content);
    }

    private sealed class KeywordSpamFilter(string keyword) : ICommentSpamFilter
    {
        public string Name => "Keyword";

        public Task<SpamCheckResult> CheckAsync(Post post, Comment comment, CancellationToken cancellationToken) =>
            Task.FromResult(comment.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase) ? SpamCheckResult.Spam : SpamCheckResult.Ham);
    }
}

using BlogEngine.Core.Services;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

[Route("api/dashboard")]
public sealed class DashboardController(
    IPostService posts,
    IPageService pages,
    ISettingsService settings,
    IRightsService rights,
    AdminMapper mapper,
    TrashService trash,
    FileLog log) : AdminApiController
{
    [HttpGet("")]
    public async Task<DashboardVM> Get()
    {
        if (!await rights.IsAuthorizedAsync(Rights.ViewDashboard))
        {
            throw new UnauthorizedAccessException();
        }

        var blogSettings = await settings.GetAsync();
        var visiblePosts = await posts.GetVisiblePostsAsync();
        var vm = new DashboardVM();

        foreach (var post in visiblePosts.Where(p => !p.IsPublished))
        {
            vm.DraftPosts.Add(await mapper.ToItemAsync(post, blogSettings));
        }

        vm.PostDraftCnt = vm.DraftPosts.Count;
        vm.PostPublishedCnt = visiblePosts.Count(p => p.IsPublished);

        var allComments = visiblePosts.SelectMany(p => p.Comments.Select(c => (Post: p, Comment: c))).ToList();
        vm.ApprovedCommentsCnt = allComments.Count(x => x.Comment.IsPublished);
        vm.PendingCommentsCnt = allComments.Count(x => !x.Comment.IsPublished && !x.Comment.IsSpam);
        vm.SpamCommentsCnt = allComments.Count(x => !x.Comment.IsPublished && x.Comment.IsSpam);
        foreach (var (post, comment) in allComments.OrderByDescending(x => x.Comment.DateCreated).Take(5))
        {
            vm.Comments.Add(await mapper.ToItemAsync(post, comment));
        }

        var visiblePages = await pages.GetVisiblePagesAsync();
        foreach (var page in visiblePages.Where(p => !p.IsPublished))
        {
            vm.DraftPages.Add(await mapper.ToItemAsync(page, visiblePages));
        }

        vm.PageDraftCnt = vm.DraftPages.Count;
        vm.PagePublishedCnt = visiblePages.Count(p => p.IsPublished);
        vm.Trash = await trash.LoadAsync(TrashType.All);
        vm.Logs = log.ReadAll();
        return vm;
    }
}

[Route("api/stats")]
public sealed class StatsController(
    IPostService posts,
    IPageService pages,
    ICategoryService categories,
    IUserService users,
    IRightsService rights,
    IBlogContext context) : AdminApiController
{
    [HttpGet("")]
    public async Task<Stats> Get()
    {
        if (!await rights.IsAuthorizedAsync(Rights.AccessAdminPages))
        {
            throw new UnauthorizedAccessException();
        }

        var visiblePosts = (await posts.GetVisiblePostsAsync()).AsEnumerable();
        if (!await rights.IsAuthorizedAsync(Rights.EditOtherUsersPosts))
        {
            visiblePosts = visiblePosts.Where(p => string.Equals(p.Author, context.UserName, StringComparison.OrdinalIgnoreCase));
        }

        var postList = visiblePosts.ToList();
        var allPages = await pages.GetPagesAsync();
        var comments = postList.SelectMany(p => p.Comments).ToList();
        return new Stats
        {
            PublishedPostsCount = postList.Count(p => p.IsPublished),
            DraftPostsCount = postList.Count(p => !p.IsPublished),
            PublishedPagesCount = allPages.Count(p => p.IsPublished),
            DraftPagesCount = allPages.Count(p => !p.IsPublished),
            PublishedCommentsCount = comments.Count(c => c.IsPublished),
            UnapprovedCommentsCount = comments.Count(c => !c.IsPublished && !c.IsSpam),
            SpamCommentsCount = comments.Count(c => !c.IsPublished && c.IsSpam),
            CategoriesCount = (await categories.GetCategoriesAsync()).Count,
            TagsCount = postList.SelectMany(p => p.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            UsersCount = (await users.GetUsersAsync()).Count,
            SubscribersCount = "0",
        };
    }
}

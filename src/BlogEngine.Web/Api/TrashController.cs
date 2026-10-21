using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

/// <summary>Cestino: commenti, post e pagine eliminati, da ripristinare o eliminare definitivamente.</summary>
[Route("api/trash")]
public sealed class TrashController(TrashService trash, IRightsService rights) : AdminApiController
{
    [HttpGet("")]
    public async Task<TrashVM> Get(int type = 1, int take = 10, int skip = 0, string filter = "1==1", string order = "DateCreated descending")
    {
        await DemandAsync();
        var items = await trash.LoadAsync((TrashType)type);
        return new TrashVM
        {
            TotalCount = items.Count,
            Items = ApiQuery.Apply(items, filter, order, take, skip).ToList(),
        };
    }

    [HttpPut("purge/{id?}")]
    public async Task<IActionResult> Purge([FromBody] TrashItem item)
    {
        await DemandAsync();
        await trash.ProcessAsync(item.ObjectType, item.Id, restore: false);
        return Ok();
    }

    [HttpPut("purgeall/{id?}")]
    public async Task<IActionResult> PurgeAll()
    {
        await DemandAsync();
        foreach (var item in await trash.LoadAsync(TrashType.All))
        {
            await trash.ProcessAsync(item.ObjectType, item.Id, restore: false);
        }

        return Ok();
    }

    [HttpPut("restore/{id?}")]
    public async Task<IActionResult> Restore([FromBody] TrashItem item)
    {
        await DemandAsync();
        await trash.ProcessAsync(item.ObjectType, item.Id, restore: true);
        return Ok();
    }

    private async Task DemandAsync()
    {
        if (!await rights.IsAuthorizedAsync(Rights.ViewDashboard))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

public enum TrashType
{
    All,
    Comment,
    Post,
    Page,
}

/// <summary>Lettura e svuotamento del cestino, usato dal cestino e dalla dashboard.</summary>
public sealed class TrashService(
    IPostService posts,
    IPageService pages,
    ICommentService comments,
    IRightsService rights,
    IBlogContext context,
    ILinkGenerator links,
    AdminMapper mapper)
{
    /// <summary>Elementi del cestino visibili all'utente (i propri, se non può modificare quelli degli altri).</summary>
    public async Task<List<TrashItem>> LoadAsync(TrashType type)
    {
        var all = await rights.IsAuthorizedAsync(Rights.EditOtherUsersPosts);
        bool Visible(Post p) => all || string.Equals(p.Author, context.UserName, StringComparison.OrdinalIgnoreCase);
        var items = new List<TrashItem>();

        if (type is TrashType.All or TrashType.Comment)
        {
            foreach (var post in (await posts.GetPostsAsync()).Where(p => p.BlogId == context.Blog.Id && Visible(p)))
            {
                foreach (var comment in post.DeletedComments)
                {
                    items.Add(new TrashItem
                    {
                        Id = comment.Id,
                        Title = $"{comment.Author}: {comment.Teaser}",
                        RelativeUrl = await links.CommentAsync(post, comment),
                        ObjectType = "Comment",
                        DateCreated = await mapper.FormatDateAsync(comment.DateCreated, AdminMapper.TrashDateFormat),
                    });
                }
            }
        }

        if (type is TrashType.All or TrashType.Post)
        {
            foreach (var post in (await posts.GetDeletedPostsAsync()).Where(Visible).OrderByDescending(p => p.DateCreated))
            {
                items.Add(new TrashItem
                {
                    Id = post.Id,
                    Title = System.Net.WebUtility.HtmlEncode(post.Title),
                    RelativeUrl = await links.PostAsync(post),
                    ObjectType = "Post",
                    DateCreated = await mapper.FormatDateAsync(post.DateCreated, AdminMapper.TrashDateFormat),
                });
            }
        }

        if (type is TrashType.All or TrashType.Page)
        {
            foreach (var page in (await pages.GetDeletedPagesAsync()).OrderByDescending(p => p.DateCreated))
            {
                items.Add(new TrashItem
                {
                    Id = page.Id,
                    Title = System.Net.WebUtility.HtmlEncode(page.Title),
                    RelativeUrl = await links.PageAsync(page),
                    ObjectType = "Page",
                    DateCreated = await mapper.FormatDateAsync(page.DateCreated, AdminMapper.TrashDateFormat),
                });
            }
        }

        return items;
    }

    public async Task ProcessAsync(string? objectType, Guid id, bool restore)
    {
        switch (objectType)
        {
            case "Comment":
                foreach (var post in (await posts.GetPostsAsync()).Where(p => p.BlogId == context.Blog.Id))
                {
                    if (post.DeletedComments.FirstOrDefault(c => c.Id == id) is { } comment)
                    {
                        _ = restore ? await comments.RestoreAsync(post, comment) : await comments.PurgeAsync(post, comment);
                        return;
                    }
                }

                break;
            case "Post":
                if ((await posts.GetDeletedPostsAsync()).FirstOrDefault(p => p.Id == id) is { } deletedPost)
                {
                    await (restore ? posts.RestoreAsync(deletedPost) : posts.PurgeAsync(deletedPost));
                }

                break;
            case "Page":
                if ((await pages.GetDeletedPagesAsync()).FirstOrDefault(p => p.Id == id) is { } deletedPage)
                {
                    await (restore ? pages.RestoreAsync(deletedPage) : pages.PurgeAsync(deletedPage));
                }

                break;
        }
    }
}

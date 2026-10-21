using BlogEngine.Core.Services;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

[Route("api/comments")]
public sealed class CommentsController(
    IPostService posts,
    ICommentService comments,
    IUserService users,
    IProfileService profiles,
    IRightsService rights,
    IBlogContext context,
    AdminMapper mapper) : AdminApiController
{
    [HttpGet("")]
    public async Task<CommentsVM> Get()
    {
        await DemandAsync(Rights.ViewPublicComments);
        var all = await rights.IsAuthorizedAsync(Rights.EditOtherUsersPosts);
        var vm = new CommentsVM();
        foreach (var post in await BlogPostsAsync())
        {
            if (!all && !string.Equals(post.Author, context.UserName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var comment in post.Comments)
            {
                vm.Items.Add(await mapper.ToItemAsync(post, comment));
            }
        }

        return vm;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        await DemandAsync(Rights.ViewPublicComments);
        return await FindAsync(id, includeDeleted: true) is { } found ? Ok(AdminMapper.ToDetail(found.Comment)) : NotFound();
    }

    /// <summary>Risposta dell'utente corrente a un commento (o nuovo commento a un post).</summary>
    [HttpPost("")]
    public async Task<IActionResult> Post([FromBody] CommentDetail item)
    {
        await DemandAsync(Rights.CreateComments);
        var post = await posts.GetByIdAsync(item.PostId);
        var userName = context.UserName;
        if (post is null || userName is null)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        var profile = await profiles.GetAsync(userName);
        var comment = new Comment
        {
            ParentId = item.ParentId,
            Content = item.Content ?? string.Empty,
            Author = string.IsNullOrWhiteSpace(profile?.DisplayName) ? userName : profile.DisplayName,
            Email = (await users.GetUserAsync(userName))?.Email ?? string.Empty,
            IP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
            IsApproved = true,
        };

        if (!await comments.AddAsync(post, comment))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return StatusCode(StatusCodes.Status201Created, await mapper.ToItemAsync(post, comment));
    }

    [HttpPut("")]
    public async Task<IActionResult> Put([FromBody] CommentItem item)
    {
        await UpdateAsync(item, "update");
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (Guid.TryParse(id, out var commentId) && await FindAsync(commentId) is { } found)
        {
            await comments.RemoveAsync(found.Post, found.Comment);
        }

        return Ok();
    }

    [HttpPut("processchecked/{id}")]
    public async Task<IActionResult> ProcessChecked(string id, [FromBody] List<CommentItem> items)
    {
        if (items is not { Count: > 0 })
        {
            return StatusCode(StatusCodes.Status417ExpectationFailed);
        }

        foreach (var item in items.Where(i => i.IsChecked))
        {
            if (id.Equals("delete", StringComparison.OrdinalIgnoreCase))
            {
                if (await FindAsync(item.Id) is { } found)
                {
                    await comments.RemoveAsync(found.Post, found.Comment);
                }
            }
            else
            {
                await UpdateAsync(item, id.ToLowerInvariant());
            }
        }

        return Ok();
    }

    /// <summary>Elimina (sposta nel cestino) tutti i commenti in attesa o tutti quelli di spam.</summary>
    [HttpPut("deleteall/{id}")]
    public async Task<IActionResult> DeleteAll(string id)
    {
        await DemandAsync(Rights.ModerateComments);
        var type = id.ToLowerInvariant();
        if (type is not ("pending" or "spam"))
        {
            return Ok();
        }

        foreach (var post in (await posts.GetPostsAsync()).Where(p => p.IsPublished))
        {
            var targets = type == "pending" ? post.NotApprovedComments.ToList() : post.SpamComments.ToList();
            foreach (var comment in targets)
            {
                await comments.RemoveAsync(post, comment);
            }
        }

        return Ok();
    }

    private async Task UpdateAsync(CommentItem item, string action)
    {
        await DemandAsync(Rights.ModerateComments);
        if (await FindAsync(item.Id) is not { } found)
        {
            return;
        }

        switch (action)
        {
            case "approve":
                await comments.ApproveAsync(found.Post, found.Comment);
                return;
            case "unapprove":
                await comments.DisapproveAsync(found.Post, found.Comment);
                return;
        }

        // Modifica dei dati e dello stato (in attesa, approvato, spam) dalla finestra del commento.
        var changed = new Comment
        {
            Id = found.Comment.Id,
            Author = item.Author ?? found.Comment.Author,
            Email = item.Email ?? found.Comment.Email,
            Website = found.Comment.Website,
            Country = found.Comment.Country,
            IP = found.Comment.IP,
            Content = found.Comment.Content,
            IsDeleted = found.Comment.IsDeleted,
            ModeratedBy = found.Comment.ModeratedBy,
            IsApproved = found.Comment.IsApproved,
            IsSpam = found.Comment.IsSpam,
        };

        if (item.IsPending)
        {
            (changed.IsApproved, changed.IsSpam) = (false, false);
        }

        await comments.UpdateAsync(found.Post, changed);
        if (item.IsApproved && !found.Comment.IsApproved)
        {
            await comments.ApproveAsync(found.Post, found.Comment);
        }
        else if (item.IsSpam && !found.Comment.IsSpam)
        {
            await comments.DisapproveAsync(found.Post, found.Comment);
        }
    }

    private async Task<IReadOnlyList<Post>> BlogPostsAsync() =>
        (await posts.GetPostsAsync()).Where(p => p.BlogId == context.Blog.Id).ToList();

    private async Task<(Post Post, Comment Comment)?> FindAsync(Guid commentId, bool includeDeleted = false)
    {
        foreach (var post in await BlogPostsAsync())
        {
            var comment = (includeDeleted ? post.AllComments : post.Comments).FirstOrDefault(c => c.Id == commentId);
            if (comment is not null)
            {
                return (post, comment);
            }
        }

        return null;
    }

    private async Task DemandAsync(Rights right)
    {
        if (!await rights.IsAuthorizedAsync(right))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

[Route("api/tags")]
public sealed class TagsController(IPostService posts, IRightsService rights) : AdminApiController
{
    /// <summary>Tag con il numero di post che li usano; con postId solo quelli del post indicato.</summary>
    [HttpGet("")]
    public async Task<IEnumerable<TagItem>> Get(int take = 10, int skip = 0, string postId = "", string order = "")
    {
        await DemandAsync(Rights.ViewPublicPosts);
        var source = await posts.GetPostsAsync();
        if (Guid.TryParse(postId, out var id))
        {
            source = source.Where(p => p.Id == id).ToList();
        }

        var tags = source.SelectMany(p => p.Tags)
            .GroupBy(t => t, StringComparer.Ordinal)
            .Select(g => new TagItem { TagName = g.Key, TagCount = g.Count() })
            .ToList();

        return ApiQuery.Apply(tags, null, string.IsNullOrEmpty(order) ? "TagName" : order, take, skip);
    }

    /// <summary>Rinomina un tag in tutti i post del blog.</summary>
    [HttpPut("update/{id?}")]
    public async Task<IActionResult> Update([FromBody] TagToUpdate tag)
    {
        await DemandAsync(Rights.AccessAdminPages);
        if (string.IsNullOrWhiteSpace(tag.OldTag) || string.IsNullOrWhiteSpace(tag.NewTag))
        {
            return BadRequest();
        }

        await ReplaceAsync(tag.OldTag, tag.NewTag.Trim());
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        await DemandAsync(Rights.AccessAdminPages);
        await ReplaceAsync(id, null);
        return Ok();
    }

    [HttpPut("processchecked/{id}")]
    public async Task<IActionResult> ProcessChecked(string id, [FromBody] List<TagItem> items)
    {
        if (items is not { Count: > 0 })
        {
            return StatusCode(StatusCodes.Status417ExpectationFailed);
        }

        await DemandAsync(Rights.AccessAdminPages);
        if (id.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in items.Where(i => i.IsChecked && !string.IsNullOrEmpty(i.TagName)))
            {
                await ReplaceAsync(item.TagName!, null);
            }
        }

        return Ok();
    }

    /// <summary>Sostituisce il tag con <paramref name="newTag"/>, oppure lo elimina se null.</summary>
    private async Task ReplaceAsync(string oldTag, string? newTag)
    {
        foreach (var post in (await posts.GetPostsAsync()).Where(p => p.Tags.Contains(oldTag, StringComparer.Ordinal)).ToList())
        {
            var copy = EntityCopies.Copy(post);
            copy.Tags = copy.Tags.Where(t => t != oldTag).ToList();
            if (newTag is not null)
            {
                copy.Tags.Add(newTag);
            }

            await posts.SaveAsync(copy);
        }
    }

    private async Task DemandAsync(Rights right)
    {
        if (!await rights.IsAuthorizedAsync(right))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

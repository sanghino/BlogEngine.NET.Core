using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

[Route("api/posts")]
public sealed class PostsController(
    IPostService posts,
    ICategoryService categories,
    IRightsService rights,
    ISettingsService settings,
    IBlogContext context,
    AdminMapper mapper) : AdminApiController
{
    [HttpGet("")]
    public async Task<IEnumerable<PostItem>> Get(int take = 10, int skip = 0, string filter = "", string order = "")
    {
        await DemandAsync(Rights.ViewPublicPosts);
        var list = await posts.GetVisiblePostsAsync(HttpContext.RequestAborted);

        // Chi non può modificare i post degli altri vede solo i propri.
        if (!await rights.IsAuthorizedAsync(Rights.EditOtherUsersPosts))
        {
            list = list.Where(p => string.Equals(p.Author, context.UserName, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        var blogSettings = await settings.GetAsync();
        var items = new List<PostItem>();
        foreach (var post in list)
        {
            items.Add(await mapper.ToItemAsync(post, blogSettings));
        }

        return ApiQuery.Apply(items, filter, string.IsNullOrEmpty(order) ? "DateCreated desc" : order, take, skip);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        await DemandAsync(Rights.ViewPublicPosts);
        return await posts.GetByIdAsync(id) is { } post ? Ok(await mapper.ToDetailAsync(post)) : NotFound();
    }

    [HttpPost("")]
    public async Task<IActionResult> Post([FromBody] PostDetail item)
    {
        await DemandAsync(Rights.CreateNewPosts);
        var post = new Post();
        await SaveAsync(post, item);
        return StatusCode(StatusCodes.Status201Created, await mapper.ToDetailAsync(post));
    }

    [HttpPut("update/{id?}")]
    public async Task<IActionResult> Update([FromBody] PostDetail item)
    {
        await DemandAsync(Rights.EditOwnPosts);
        if (await posts.GetByIdAsync(item.Id) is { } post)
        {
            await SaveAsync(EntityCopies.Copy(post), item);
        }

        return Ok();
    }

    [HttpPut("processchecked/{id}")]
    public async Task<IActionResult> ProcessChecked(string id, [FromBody] List<PostDetail> items)
    {
        if (items is not { Count: > 0 })
        {
            return StatusCode(StatusCodes.Status417ExpectationFailed);
        }

        foreach (var item in items.Where(i => i.IsChecked))
        {
            if (id.Equals("delete", StringComparison.OrdinalIgnoreCase))
            {
                await DeleteAsync(item.Id);
                continue;
            }

            await DemandAsync(Rights.EditOwnPosts);
            if (await posts.GetByIdAsync(item.Id) is not { } post)
            {
                continue;
            }

            var copy = EntityCopies.Copy(post);
            switch (id.ToLowerInvariant())
            {
                case "publish":
                    copy.IsPublished = true;
                    await posts.SaveAsync(copy);
                    break;
                case "unpublish":
                    copy.IsPublished = false;
                    await posts.SaveAsync(copy);
                    break;
                default:
                    await SaveAsync(copy, item);
                    break;
            }
        }

        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (Guid.TryParse(id, out var postId))
        {
            await DeleteAsync(postId);
        }

        return Ok();
    }

    private async Task DeleteAsync(Guid id)
    {
        await DemandAsync(Rights.DeleteOwnPosts);
        if (await posts.GetByIdAsync(id) is { } post)
        {
            await posts.DeleteAsync(post);
        }
    }

    /// <summary>Applica i dati dell'editor al post e lo salva (porting di PostRepository.Save).</summary>
    private async Task SaveAsync(Post post, PostDetail detail)
    {
        post.Title = detail.Title ?? string.Empty;
        post.Author = string.IsNullOrWhiteSpace(detail.Author) ? context.UserName ?? string.Empty : detail.Author;
        post.Description = AdminMapper.DescriptionOrExcerpt(detail.Description, detail.Content);
        post.Content = detail.Content ?? string.Empty;
        post.IsPublished = detail.IsPublished;
        post.HasCommentsEnabled = detail.HasCommentsEnabled;
        post.IsDeleted = detail.IsDeleted;
        post.DateCreated = await mapper.ParseDateAsync(detail.DateCreated);
        if (!string.IsNullOrWhiteSpace(detail.Slug))
        {
            post.Slug = detail.Slug;
        }

        post.Categories = await ResolveCategoriesAsync(detail.Categories);
        post.Tags = (detail.Tags ?? []).Select(t => t.TagName?.Trim()).Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList()!;
        await posts.SaveAsync(post);
    }

    /// <summary>Le categorie sono indicate per titolo: quelle che non esistono ancora vengono create.</summary>
    private async Task<List<Guid>> ResolveCategoriesAsync(List<CategoryItem>? items)
    {
        var result = new List<Guid>();
        foreach (var item in items ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Title))
            {
                continue;
            }

            var existing = (await categories.GetCategoriesAsync())
                .FirstOrDefault(c => c.BlogId == context.Blog.Id && string.Equals(c.Title, item.Title, StringComparison.Ordinal));
            if (existing is null)
            {
                existing = new Category { Title = item.Title.Trim(), Description = item.Description ?? string.Empty };
                await categories.SaveAsync(existing);
            }

            if (!result.Contains(existing.Id))
            {
                result.Add(existing.Id);
            }
        }

        return result;
    }

    private async Task DemandAsync(Rights right)
    {
        if (!await rights.IsAuthorizedAsync(right))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

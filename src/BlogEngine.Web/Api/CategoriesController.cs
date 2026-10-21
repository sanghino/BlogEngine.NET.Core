using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

[Route("api/categories")]
public sealed class CategoriesController(ICategoryService categories, IPostService posts, IRightsService rights, AdminMapper mapper) : AdminApiController
{
    [HttpGet("")]
    public async Task<IEnumerable<CategoryItem>> Get(int take = 10, int skip = 0, string filter = "", string order = "")
    {
        await DemandAsync(Rights.ViewPublicPosts);
        return ApiQuery.Apply(await LoadAsync(), filter, string.IsNullOrEmpty(order) ? "Title" : order, take, skip);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        await DemandAsync(Rights.ViewPublicPosts);
        return (await LoadAsync()).FirstOrDefault(c => c.Id == id) is { } item ? Ok(item) : NotFound();
    }

    [HttpPost("")]
    public async Task<IActionResult> Post([FromBody] CategoryItem item)
    {
        await DemandAsync(Rights.CreateNewPosts);
        var category = new Category();
        await ApplyAsync(category, item);
        return StatusCode(StatusCodes.Status201Created, await mapper.ToItemAsync(category));
    }

    [HttpPut("update/{id?}")]
    public async Task<IActionResult> Update([FromBody] CategoryItem item)
    {
        await DemandAsync(Rights.EditOwnPosts);
        if (await categories.GetByIdAsync(item.Id) is { } category)
        {
            await ApplyAsync(EntityCopies.Copy(category), item);
        }

        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        if (Guid.TryParse(id, out var categoryId) && await categories.GetByIdAsync(categoryId) is { } category)
        {
            await categories.DeleteAsync(category);
        }

        return Ok();
    }

    [HttpPut("processchecked/{id}")]
    public async Task<IActionResult> ProcessChecked(string id, [FromBody] List<CategoryItem> items)
    {
        if (items is not { Count: > 0 })
        {
            return StatusCode(StatusCodes.Status417ExpectationFailed);
        }

        if (id.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in items.Where(i => i.IsChecked))
            {
                if (await categories.GetByIdAsync(item.Id) is { } category)
                {
                    await categories.DeleteAsync(category);
                }
            }
        }

        return Ok();
    }

    /// <summary>Categorie con il numero di post (non eliminati) che le usano.</summary>
    private async Task<List<CategoryItem>> LoadAsync()
    {
        var allPosts = await posts.GetPostsAsync();
        var items = new List<CategoryItem>();
        foreach (var category in await categories.GetCategoriesAsync())
        {
            items.Add(await mapper.ToItemAsync(category, allPosts.Count(p => p.Categories.Contains(category.Id))));
        }

        return items;
    }

    private async Task ApplyAsync(Category category, CategoryItem item)
    {
        category.Title = item.Title?.Trim() ?? string.Empty;
        category.Description = item.Description ?? string.Empty;
        category.Parent = Guid.TryParse(item.Parent?.OptionValue, out var parent) && parent != Guid.Empty ? parent : null;
        await categories.SaveAsync(category);
    }

    private async Task DemandAsync(Rights right)
    {
        if (!await rights.IsAuthorizedAsync(right))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

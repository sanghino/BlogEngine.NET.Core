using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

[Route("api/pages")]
public sealed class PagesController(IPageService pages, IRightsService rights, AdminMapper mapper) : AdminApiController
{
    [HttpGet("")]
    public async Task<IEnumerable<PageItem>> Get(int take = 10, int skip = 0, string filter = "", string order = "DateCreated descending")
    {
        await DemandAsync(Rights.ViewPublicPages);
        var all = await pages.GetPagesAsync();
        var items = new List<PageItem>();
        foreach (var page in all)
        {
            items.Add(await mapper.ToItemAsync(page, all));
        }

        return ApiQuery.Apply(items, filter, string.IsNullOrEmpty(order) ? "DateCreated desc" : order, take, skip);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        await DemandAsync(Rights.ViewPublicPages);
        return await pages.GetByIdAsync(id) is { } page ? Ok(await mapper.ToDetailAsync(page)) : NotFound();
    }

    [HttpPost("")]
    public async Task<IActionResult> Post([FromBody] PageDetail item)
    {
        await DemandAsync(Rights.CreateNewPages);
        var page = new Page();
        await SaveAsync(page, item);
        return StatusCode(StatusCodes.Status201Created, await mapper.ToDetailAsync(page));
    }

    [HttpPut("update/{id?}")]
    public async Task<IActionResult> Update([FromBody] PageDetail item)
    {
        await DemandAsync(Rights.CreateNewPages);
        if (await pages.GetByIdAsync(item.Id) is { } page)
        {
            await SaveAsync(EntityCopies.Copy(page), item);
        }

        return Ok();
    }

    [HttpPut("processchecked/{id}")]
    public async Task<IActionResult> ProcessChecked(string id, [FromBody] List<PageDetail> items)
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

            await DemandAsync(Rights.CreateNewPages);
            if (await pages.GetByIdAsync(item.Id) is not { } page)
            {
                continue;
            }

            var copy = EntityCopies.Copy(page);
            switch (id.ToLowerInvariant())
            {
                case "publish":
                    copy.IsPublished = true;
                    await pages.SaveAsync(copy);
                    break;
                case "unpublish":
                    copy.IsPublished = false;
                    await pages.SaveAsync(copy);
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
        if (Guid.TryParse(id, out var pageId))
        {
            await DeleteAsync(pageId);
        }

        return Ok();
    }

    private async Task DeleteAsync(Guid id)
    {
        await DemandAsync(Rights.CreateNewPages);
        if (await pages.GetByIdAsync(id) is not { } page)
        {
            return;
        }

        if ((await pages.GetPagesAsync()).Any(p => p.Parent == page.Id))
        {
            throw new InvalidOperationException("Can not delete; page has child pages");
        }

        await pages.DeleteAsync(page);
    }

    /// <summary>Applica i dati dell'editor alla pagina e la salva (porting di PageRepository.Save).</summary>
    private async Task SaveAsync(Page page, PageDetail detail)
    {
        page.Title = detail.Title ?? string.Empty;
        page.DateCreated = await mapper.ParseDateAsync(detail.DateCreated);
        page.IsPublished = detail.IsPublished;
        page.ShowInList = detail.ShowInList;
        page.IsDeleted = detail.IsDeleted;
        page.Content = detail.Content ?? string.Empty;
        page.Description = AdminMapper.DescriptionOrExcerpt(detail.Description, detail.Content);
        page.Keywords = detail.Keywords ?? string.Empty;
        page.IsFrontPage = detail.IsFrontPage;
        page.SortOrder = detail.SortOrder;
        if (!string.IsNullOrWhiteSpace(detail.Slug))
        {
            page.Slug = detail.Slug;
        }

        var parent = detail.Parent?.OptionValue;
        if (parent is null || parent == "none" || parent.Length == 0)
        {
            page.Parent = Guid.Empty;
        }
        else if (Guid.TryParse(parent, out var parentId))
        {
            page.Parent = parentId;
        }

        await pages.SaveAsync(page);
    }

    private async Task DemandAsync(Rights right)
    {
        if (!await rights.IsAuthorizedAsync(right))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

using BlogEngine.Core.Data;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

/// <summary>Voce del blogroll come la usa l'amministrazione (con la casella di selezione).</summary>
public sealed class BlogRollDto
{
    public bool IsChecked { get; set; }

    public Guid Id { get; set; }

    public string? Title { get; set; }

    public string? Description { get; set; }

    public string? BlogUrl { get; set; }

    public string? FeedUrl { get; set; }

    public string? Xfn { get; set; }

    public int SortIndex { get; set; }
}

[Route("api/blogroll")]
public sealed class BlogRollController(IBlogRollService blogRoll) : AdminApiController
{
    [HttpGet("")]
    public async Task<IEnumerable<BlogRollDto>> Get() => (await blogRoll.GetItemsAsync()).Select(ToDto);

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id) =>
        (await blogRoll.GetItemsAsync()).FirstOrDefault(i => i.Id == id) is { } item ? Ok(ToDto(item)) : NotFound();

    [HttpPost("")]
    public async Task<IActionResult> Post([FromBody] BlogRollDto item)
    {
        var entity = ToEntity(item, new BlogRollItem());
        await blogRoll.SaveAsync(entity);
        return StatusCode(StatusCodes.Status201Created, ToDto(entity));
    }

    [HttpPut("update/{id?}")]
    public async Task<IActionResult> Update([FromBody] BlogRollDto item)
    {
        if ((await blogRoll.GetItemsAsync()).FirstOrDefault(i => i.Id == item.Id) is not { } existing)
        {
            return NotFound();
        }

        await blogRoll.SaveAsync(ToEntity(item, new BlogRollItem { Id = existing.Id, BlogId = existing.BlogId }));
        return Ok();
    }

    [HttpPut("processchecked/{id}")]
    public async Task<IActionResult> ProcessChecked(string id, [FromBody] List<BlogRollDto> items)
    {
        if (items is not { Count: > 0 })
        {
            return StatusCode(StatusCodes.Status417ExpectationFailed);
        }

        if (id.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in items.Where(i => i.IsChecked))
            {
                await blogRoll.DeleteAsync(item.Id);
            }
        }

        return Ok();
    }

    private static BlogRollDto ToDto(BlogRollItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        Description = item.Description,
        BlogUrl = item.BlogUrl?.ToString(),
        FeedUrl = item.FeedUrl?.ToString(),
        Xfn = item.Xfn,
        SortIndex = item.SortIndex,
    };

    private static BlogRollItem ToEntity(BlogRollDto dto, BlogRollItem entity)
    {
        entity.Title = dto.Title?.Trim() ?? string.Empty;
        entity.Description = dto.Description ?? string.Empty;
        entity.BlogUrl = ToUri(dto.BlogUrl);
        entity.FeedUrl = ToUri(dto.FeedUrl);
        entity.Xfn = string.IsNullOrWhiteSpace(dto.Xfn) ? "contact" : dto.Xfn;
        entity.SortIndex = dto.SortIndex;
        return entity;
    }

    /// <summary>Indirizzo assoluto; senza schema viene aggiunto "http://" come nella versione originale.</summary>
    private static Uri? ToUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var value = url.Contains("://", StringComparison.Ordinal) ? url.Trim() : "http://" + url.Trim();
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) ? uri : null;
    }
}

[Route("api/pingservices")]
public sealed class PingServicesController(IStringListStore lists, IRightsService rights, IBlogContext context) : AdminApiController
{
    [HttpGet("")]
    public async Task<IEnumerable<PingItem>> Get()
    {
        await DemandAsync();
        return (await LoadAsync()).Select(s => new PingItem { ServiceUrl = s });
    }

    [HttpPost("")]
    public async Task<IActionResult> Post([FromBody] PingItem item)
    {
        await DemandAsync();
        var services = (await LoadAsync()).ToList();
        if (Uri.TryCreate(item.ServiceUrl?.Trim(), UriKind.Absolute, out var uri) && !services.Contains(uri.ToString()))
        {
            services.Add(uri.ToString());
            await lists.SaveAsync(context.Blog.Id, StringListKind.PingServices, services);
        }

        return StatusCode(StatusCodes.Status201Created, services.Select(s => new PingItem { ServiceUrl = s }));
    }

    [HttpPut("processchecked/{id}")]
    public async Task<IActionResult> ProcessChecked(string id, [FromBody] List<PingItem> items)
    {
        await DemandAsync();
        if (items is not { Count: > 0 })
        {
            return StatusCode(StatusCodes.Status417ExpectationFailed);
        }

        var remove = items.Where(i => i.IsChecked).Select(i => i.ServiceUrl).ToHashSet();
        await lists.SaveAsync(context.Blog.Id, StringListKind.PingServices, (await LoadAsync()).Where(s => !remove.Contains(s)).ToList());
        return Ok();
    }

    private Task<IReadOnlyList<string>> LoadAsync() => lists.LoadAsync(context.Blog.Id, StringListKind.PingServices, HttpContext.RequestAborted);

    private async Task DemandAsync()
    {
        if (!await rights.IsAuthorizedAsync(Rights.AccessAdminSettingsPages))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

/// <summary>Regole di moderazione dei commenti configurate dall'amministratore.</summary>
[Route("api/commentfilter")]
public sealed class CommentFilterController(ICommentFilterStore store, IRightsService rights, IBlogContext context) : AdminApiController
{
    [HttpGet("")]
    public async Task<IEnumerable<CommentFilterItem>> Get(int take = 10, int skip = 0, string filter = "", string order = "Filter")
    {
        await DemandAsync();
        var items = (await LoadAsync()).Select(ToItem).ToList();
        return ApiQuery.Apply(items, filter, string.IsNullOrEmpty(order) ? "Filter" : order, take, skip);
    }

    [HttpPost("")]
    public async Task<IActionResult> Post([FromBody] CommentFilterItem item)
    {
        await DemandAsync();
        if (!Enum.TryParse<CommentFilterAction>(item.Action, true, out var action)
            || !Enum.TryParse<CommentFilterSubject>(item.Subject, true, out var subject)
            || !Enum.TryParse<CommentFilterOperator>(item.Operation, true, out var operation)
            || string.IsNullOrWhiteSpace(item.Filter))
        {
            return BadRequest(new { Message = T("errorInvalidFilter") });
        }

        var rules = (await LoadAsync()).ToList();
        var rule = new CommentFilterRule { Action = action, Subject = subject, Operator = operation, Filter = item.Filter.Trim() };
        if (rules.Any(r => r.Action == rule.Action && r.Subject == rule.Subject && r.Operator == rule.Operator
                           && string.Equals(r.Filter, rule.Filter, StringComparison.OrdinalIgnoreCase)))
        {
            return BadRequest(new { Message = T("errorItemExists") });
        }

        rules.Add(rule);
        await store.SaveRulesAsync(context.Blog.Id, rules);
        return StatusCode(StatusCodes.Status201Created, ToItem(rule));
    }

    [HttpPut("deleteall/{id?}")]
    public async Task<IActionResult> DeleteAll()
    {
        await DemandAsync();
        await store.SaveRulesAsync(context.Blog.Id, []);
        return Ok();
    }

    [HttpPut("processchecked/{id}")]
    public async Task<IActionResult> ProcessChecked(string id, [FromBody] List<CommentFilterItem> items)
    {
        await DemandAsync();
        if (items is not { Count: > 0 })
        {
            return StatusCode(StatusCodes.Status417ExpectationFailed);
        }

        if (id.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            var remove = items.Where(i => i.IsChecked).Select(i => i.Id).ToHashSet();
            await store.SaveRulesAsync(context.Blog.Id, (await LoadAsync()).Where(r => !remove.Contains(r.Id)).ToList());
        }

        return Ok();
    }

    private Task<IReadOnlyList<CommentFilterRule>> LoadAsync() => store.GetRulesAsync(context.Blog.Id, HttpContext.RequestAborted);

    private static CommentFilterItem ToItem(CommentFilterRule rule) => new()
    {
        Id = rule.Id,
        Action = rule.Action.ToString(),
        Subject = rule.Subject.ToString(),
        Operation = rule.Operator.ToString(),
        Filter = rule.Filter,
    };

    private async Task DemandAsync()
    {
        if (!await rights.IsAuthorizedAsync(Rights.AccessAdminPages))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

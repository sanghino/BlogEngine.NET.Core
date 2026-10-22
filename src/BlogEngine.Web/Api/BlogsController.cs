using BlogEngine.Core.Configuration;
using BlogEngine.Core.Services;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

/// <summary>
/// Blog del multi-blog, dal blog principale. Elenco e creazione: a chi ha il permesso "Crea nuovi blog";
/// modifica ed eliminazione: solo agli amministratori.
/// </summary>
[Route("api/blogs")]
public sealed class BlogsController(
    IBlogService blogs,
    BlogProvisioningService provisioning,
    IRightsService rights,
    IBlogContext context,
    BlogEnginePaths paths) : AdminApiController
{
    [HttpGet("")]
    public async Task<IEnumerable<BlogDto>> Get(int take = 10, int skip = 0, string filter = "", string order = "Name")
    {
        await DemandBlogCreatorAsync();
        var items = (await blogs.GetBlogsAsync()).Select(ToDto).ToList();
        return ApiQuery.Apply(items, filter, string.IsNullOrEmpty(order) ? "Name" : order, take, skip);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id)
    {
        await DemandBlogCreatorAsync();
        return await blogs.GetByIdAsync(id) is { } blog ? Ok(ToDto(blog)) : NotFound();
    }

    [HttpPost("")]
    public async Task<IActionResult> Post([FromBody] BlogItem item)
    {
        if (item.Password != item.ConfirmPassword)
        {
            return BadRequest(new { Message = "Le password non coincidono." });
        }

        var blog = await provisioning.CreateBlogAsync(item.Name ?? string.Empty, item.UserName ?? string.Empty, item.Email ?? string.Empty, item.Password ?? string.Empty);
        return StatusCode(StatusCodes.Status201Created, ToDto(blog));
    }

    [HttpPut("update/{id?}")]
    public async Task<IActionResult> Update([FromBody] BlogDto item)
    {
        DemandPrimaryAdministrator();
        if (await blogs.GetByIdAsync(item.Id) is not { } blog)
        {
            return NotFound();
        }

        var updated = new Blog
        {
            Id = blog.Id,
            Name = item.Name ?? blog.Name,
            StorageContainerName = item.StorageContainerName ?? blog.StorageContainerName,
            Hostname = item.Hostname?.Trim() ?? string.Empty,
            IsAnyTextBeforeHostnameAccepted = item.IsAnyTextBeforeHostnameAccepted,
            VirtualPath = string.IsNullOrWhiteSpace(item.VirtualPath) ? blog.VirtualPath : item.VirtualPath.Trim(),
            IsActive = item.IsActive,
            IsPrimary = item.IsPrimary,
            DateCreated = blog.DateCreated,
        };
        await blogs.SaveAsync(updated);
        return Ok();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        DemandPrimaryAdministrator();
        if (Guid.TryParse(id, out var blogId) && await blogs.GetByIdAsync(blogId) is { } blog)
        {
            await blogs.DeleteAsync(blog);
        }

        return Ok();
    }

    [HttpPut("processchecked/{id}")]
    public async Task<IActionResult> ProcessChecked(string id, [FromBody] List<BlogDto> items)
    {
        DemandPrimaryAdministrator();
        if (items is not { Count: > 0 })
        {
            return StatusCode(StatusCodes.Status417ExpectationFailed);
        }

        if (id.Equals("delete", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var item in items.Where(i => i.IsChecked))
            {
                if (await blogs.GetByIdAsync(item.Id) is { } blog)
                {
                    await blogs.DeleteAsync(blog);
                }
            }
        }

        return Ok();
    }

    /// <summary>Dal blog principale, con il permesso di creare blog (gli amministratori lo hanno sempre).</summary>
    private async Task DemandBlogCreatorAsync()
    {
        if (!context.Blog.IsPrimary || !await rights.IsAuthorizedAsync(BlogEngine.Security.Rights.CreateNewBlogs))
        {
            throw new UnauthorizedAccessException();
        }
    }

    private void DemandPrimaryAdministrator()
    {
        // I blog secondari non possono vedere né gestire gli altri blog.
        if (!context.Blog.IsPrimary || !rights.IsAdministrator)
        {
            throw new UnauthorizedAccessException();
        }
    }

    private BlogDto ToDto(Blog blog)
    {
        var root = BlogResolver.RelativeRoot(blog);
        var authority = !string.IsNullOrWhiteSpace(blog.Hostname)
            ? new UriBuilder(Request.Scheme, blog.Hostname).Uri
            : new Uri($"{Request.Scheme}://{Request.Host}/");

        return new BlogDto
        {
            Id = blog.Id,
            Name = blog.Name,
            StorageContainerName = blog.StorageContainerName,
            Hostname = blog.Hostname,
            IsAnyTextBeforeHostnameAccepted = blog.IsAnyTextBeforeHostnameAccepted,
            VirtualPath = blog.VirtualPath,
            IsActive = blog.IsActive,
            IsPrimary = blog.IsPrimary,
            RelativeWebRoot = root,
            AbsoluteWebRoot = new Uri(authority, root),
            PhysicalStorageLocation = Path.Combine(paths.Data, "blogs", blog.Id.ToString("N")),
            CanUserEdit = true,
            CanUserDelete = !blog.IsPrimary,
        };
    }
}

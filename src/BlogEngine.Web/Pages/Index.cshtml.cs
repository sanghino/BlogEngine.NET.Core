using BlogEngine.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlogEngine.Web.Pages;

/// <summary>Home provvisoria: le pagine pubbliche arrivano con il motore dei temi.</summary>
public sealed class IndexModel(ISettingsService settings, IBlogContext context) : PageModel
{
    public string BlogName { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string? UserName => context.UserName;

    public async Task OnGetAsync()
    {
        var blogSettings = await settings.GetAsync(cancellationToken: HttpContext.RequestAborted);
        BlogName = blogSettings.Name;
        Description = blogSettings.Description;
    }
}

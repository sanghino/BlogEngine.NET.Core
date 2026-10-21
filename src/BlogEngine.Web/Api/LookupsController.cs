using BlogEngine.Core.Services;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

/// <summary>Elenchi usati dai menu a tendina dell'amministrazione.</summary>
[Route("api/lookups")]
public sealed class LookupsController(
    IRightsService rights,
    IUserService users,
    IPageService pages,
    ICategoryService categories,
    ISettingsService settings,
    IBlogContext context,
    SettingsCache settingsCache,
    ThemeCatalog themes) : AdminApiController
{
    [HttpGet("")]
    public async Task<Lookups> Get()
    {
        await DemandAsync();
        var blogSettings = await settings.GetAsync();
        var canEditOthers = await rights.IsAuthorizedAsync(Rights.EditOtherUsersPosts);

        return new Lookups
        {
            Cultures = new[] { new SelectOption { OptionName = "Auto", OptionValue = BlogCultureProvider.AutoCulture } }
                .Concat(BlogCultureProvider.SupportedCultures.Select(c => new SelectOption { OptionName = c.NativeName, OptionValue = c.Name }))
                .ToList(),
            SelfRegisterRoles = (await users.GetRolesAsync())
                .Where(r => !string.Equals(r.Name, SystemRoles.Anonymous, StringComparison.OrdinalIgnoreCase))
                .Select(r => new SelectOption { OptionName = r.Name, OptionValue = r.Name })
                .ToList(),
            PageList = (await pages.GetPagesAsync()).Select(p => new SelectOption { OptionName = p.Title, OptionValue = p.Id.ToString() }).ToList(),
            AuthorList = canEditOthers
                ? (await users.GetUsersAsync()).Select(u => new SelectOption { OptionName = u.UserName, OptionValue = u.UserName }).ToList()
                : [new SelectOption { OptionName = context.UserName, OptionValue = context.UserName }],
            CategoryList = (await categories.GetCategoriesAsync())
                .Where(c => c.BlogId == context.Blog.Id)
                .Select(c => new SelectOption { OptionName = c.Title, OptionValue = c.Id.ToString() })
                .ToList(),
            InstalledThemes = themes.GetThemes().Select(t => new SelectOption { OptionName = t.Title, OptionValue = t.Id }).ToList(),
            PostOptions = new EditorOptions
            {
                OptionType = "Post",
                ShowSlug = blogSettings.PostOptionsSlug,
                ShowDescription = blogSettings.PostOptionsDescription,
                ShowCustomFields = blogSettings.PostOptionsCustomFields,
            },
            PageOptions = new EditorOptions
            {
                OptionType = "Page",
                ShowSlug = blogSettings.PageOptionsSlug,
                ShowDescription = blogSettings.PageOptionsDescription,
                ShowCustomFields = blogSettings.PageOptionsCustomFields,
            },
        };
    }

    /// <summary>Campi visibili negli editor di post e pagine.</summary>
    [HttpPut("update/{id?}")]
    public async Task<bool> Update([FromBody] EditorOptions options)
    {
        await DemandAsync();
        var blogSettings = await settings.GetAsync();
        if (options.OptionType == "Post")
        {
            blogSettings.PostOptionsSlug = options.ShowSlug;
            blogSettings.PostOptionsDescription = options.ShowDescription;
            blogSettings.PostOptionsCustomFields = options.ShowCustomFields;
        }
        else if (options.OptionType == "Page")
        {
            blogSettings.PageOptionsSlug = options.ShowSlug;
            blogSettings.PageOptionsDescription = options.ShowDescription;
            blogSettings.PageOptionsCustomFields = options.ShowCustomFields;
        }

        // Le opzioni dell'editor sono preferenze di chi scrive: come nella versione originale bastano i permessi
        // di accesso all'amministrazione, quindi si scrive direttamente nella cache senza il controllo di ISettingsService.
        await settingsCache.SaveAsync(context.Blog.Id, blogSettings, HttpContext.RequestAborted);
        return true;
    }

    private async Task DemandAsync()
    {
        if (!await rights.IsAuthorizedAsync(Rights.AccessAdminPages))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

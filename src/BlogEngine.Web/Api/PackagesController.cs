using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

/// <summary>
/// Temi, estensioni e widget installati. La galleria online non esiste più: "install", "rate" e l'elenco
/// "all" (la galleria) non sono disponibili; temi ed estensioni si installano copiando una cartella.
/// Estensioni e widget arriveranno con le fasi dedicate del porting.
/// </summary>
[Route("api/packages")]
public sealed class PackagesController(ThemeCatalog themes, ISettingsService settings, IRightsService rights) : AdminApiController
{
    [HttpGet("")]
    public async Task<IEnumerable<Package>> Get(int take = 10, int skip = 0, string filter = "", string order = "")
    {
        IEnumerable<Package> packages;
        switch (filter?.ToLowerInvariant())
        {
            case "themes":
                await DemandAsync(Rights.ManageThemes);
                packages = await ThemesAsync();
                break;
            case "extensions":
                await DemandAsync(Rights.ManageExtensions);
                packages = [];
                break;
            case "widgets":
                await DemandAsync(Rights.ManageWidgets);
                packages = [];
                break;
            default:
                await DemandAsync(Rights.ManagePackages);
                packages = [];
                break;
        }

        return ApiQuery.Apply(packages.ToList(), null, string.IsNullOrEmpty(order) ? "LastUpdated desc" : order, take, skip);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id)
    {
        await DemandAsync(Rights.ManagePackages);
        return (await ThemesAsync()).FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) is { } package
            ? Ok(package)
            : NotFound();
    }

    [HttpPut("settheme/{id}")]
    public async Task<IActionResult> SetTheme(string id)
    {
        await DemandAsync(Rights.ManageThemes);
        if (themes.Find(id) is null)
        {
            return NotFound();
        }

        var blogSettings = await settings.GetAsync();
        blogSettings.Theme = id;
        await settings.SaveAsync(blogSettings);
        return Ok();
    }

    [HttpPut("install/{id}")]
    [HttpPut("uninstall/{id}")]
    [HttpPut("rate/{id}")]
    [HttpPut("processchecked/{id}")]
    public IActionResult GalleryNotAvailable() =>
        BadRequest(new { Message = "La galleria online non è più disponibile: temi ed estensioni si installano copiando la loro cartella sul server." });

    [HttpPut("update/{id?}")]
    public async Task<bool> Update([FromBody] Package item)
    {
        await DemandAsync(Rights.ManagePackages);
        return true;
    }

    [HttpPut("refresh/{id?}")]
    public bool Refresh() => true;

    private async Task<IEnumerable<Package>> ThemesAsync()
    {
        var current = (await settings.GetAsync()).Theme;
        return themes.GetThemes().Select(t => new Package
        {
            Id = t.Id,
            PackageType = "Theme",
            Title = t.Title,
            Description = t.Description,
            LocalVersion = t.Version,
            Authors = t.Authors,
            Website = t.Website,
            IconUrl = $"/Custom/Themes/{Uri.EscapeDataString(t.Id)}/theme.png",
            Enabled = string.Equals(t.Id, current, StringComparison.OrdinalIgnoreCase),
            LastUpdated = string.Empty,
        });
    }

    private async Task DemandAsync(Rights right)
    {
        if (!await rights.IsAuthorizedAsync(right))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

[Route("api/packageextra")]
public sealed class PackageExtraController : AdminApiController
{
    [HttpGet("")]
    public IEnumerable<object> Get() => [];

    [HttpGet("{id}")]
    public IActionResult Get(string id) => Ok();
}

/// <summary>Zone dei widget dei temi: disponibili con il motore dei temi (Fase 5).</summary>
[Route("api/widgets")]
public sealed class WidgetsController(IRightsService rights) : AdminApiController
{
    [HttpGet("")]
    public async Task<WidgetsVM> Get()
    {
        if (!await rights.IsAuthorizedAsync(Rights.ManageWidgets))
        {
            throw new UnauthorizedAccessException();
        }

        return new WidgetsVM();
    }

    [HttpPut("update/{id?}")]
    public IActionResult Update() =>
        BadRequest(new { Message = "La gestione dei widget sarà disponibile con il motore dei temi." });
}

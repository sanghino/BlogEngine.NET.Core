using BlogEngine.Core.Extensions;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using BlogEngine.Web.Hosting;
using BlogEngine.Web.Themes;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

/// <summary>
/// Temi, estensioni e widget installati. La galleria online non esiste più: "install", "rate" e l'elenco
/// "all" (la galleria) non sono disponibili; temi ed estensioni si installano copiando una cartella.
/// Per le estensioni: attivazione, disattivazione e priorità (valide per tutti i blog) e rimozione di
/// quelle caricate in App_Data/extensions.
/// </summary>
[Route("api/packages")]
public sealed class PackagesController(
    ThemeCatalog themes,
    WidgetCatalog widgetCatalog,
    ExtensionRegistry extensions,
    ExtensionStates extensionStates,
    ISettingsService settings,
    IRightsService rights,
    IBlogContext context,
    IHostApplicationLifetime lifetime) : AdminApiController
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
                packages = Extensions();
                break;
            case "widgets":
                await DemandAsync(Rights.ManageWidgets);
                packages = Widgets();
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
        return (await ThemesAsync()).Concat(Widgets()).Concat(Extensions()).FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) is { } package
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

    /// <summary>Comandi sulle estensioni selezionate: "enable", "disable" e "uninstall".</summary>
    [HttpPut("processchecked/{id}")]
    public async Task<IActionResult> ProcessChecked(string id, [FromBody] List<Package> items)
    {
        await DemandExtensionsAsync();
        foreach (var item in (items ?? []).Where(i => i.IsChecked && extensions.Find(i.Id ?? string.Empty) is not null))
        {
            var state = extensionStates.Get(item.Id!);
            switch (id.ToLowerInvariant())
            {
                case "enable":
                    await extensionStates.SetAsync(item.Id!, true, state.Priority);
                    break;
                case "disable":
                    await extensionStates.SetAsync(item.Id!, false, state.Priority);
                    break;
                case "uninstall":
                    var result = Uninstall(item.Id!);
                    if (result is not OkResult)
                    {
                        return result;
                    }

                    break;
            }
        }

        return Ok();
    }

    [HttpPut("uninstall/{id}")]
    public async Task<IActionResult> UninstallOne(string id)
    {
        await DemandExtensionsAsync();
        return Uninstall(id);
    }

    [HttpPut("install/{id}")]
    [HttpPut("rate/{id}")]
    public IActionResult GalleryNotAvailable() =>
        BadRequest(new { Message = T("errorGalleryUnavailable") });

    [HttpPut("update/{id?}")]
    public async Task<bool> Update([FromBody] Package item)
    {
        await DemandAsync(Rights.ManagePackages);
        if (string.Equals(item.PackageType, "Extension", StringComparison.OrdinalIgnoreCase) && extensions.Find(item.Id ?? string.Empty) is { } extension)
        {
            await DemandExtensionsAsync();
            await extensionStates.SetAsync(extension.Name, item.Enabled, item.Priority);
        }

        return true;
    }

    /// <summary>
    /// Rimuove un'estensione caricata in App_Data/extensions; quelle del pacchetto (Custom/Extensions)
    /// si tolgono via FTP o si disattivano. Il sito si riavvia per scaricarla dalla memoria.
    /// </summary>
    private IActionResult Uninstall(string name)
    {
        if (extensions.Find(name) is not { } extension)
        {
            return NotFound();
        }

        if (!extension.IsUploaded)
        {
            return BadRequest(new { Message = T("errorBundledExtensionDelete") });
        }

        Directory.Delete(extension.Folder, recursive: true);
        lifetime.StopApplication();
        return Ok();
    }

    private IEnumerable<Package> Extensions() => extensions.All.Select(e => new Package
    {
        Id = e.Name,
        PackageType = "Extension",
        Title = e.Name,
        Description = e.Error is null ? e.Description : $"Errore: {e.Error}",
        LocalVersion = e.Version,
        Authors = e.Author,
        Website = string.Empty,
        IconUrl = "/Content/images/blog/ext.png",
        Enabled = extensionStates.IsEnabled(e.Name),
        Priority = extensionStates.Get(e.Name).Priority,
        SettingsUrl = e.AdminPage ?? (e.Settings.Any(s => !s.Hidden) ? $"~/admin/extensions/settings?ext={Uri.EscapeDataString(e.Name)}" : null),
        LastUpdated = string.Empty,
    });

    /// <summary>Lo stato delle estensioni vale per tutti i blog: si cambia solo dal blog principale.</summary>
    private async Task DemandExtensionsAsync()
    {
        await DemandAsync(Rights.ManageExtensions);
        if (!context.Blog.IsPrimary)
        {
            throw new UnauthorizedAccessException();
        }
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

    private IEnumerable<Package> Widgets() => widgetCatalog.GetWidgets().Select(w => new Package
    {
        Id = w.Name,
        PackageType = "Widget",
        Title = w.Title,
        Description = w.Description,
        LocalVersion = w.Version,
        Authors = w.Authors,
        Website = w.Website,
        IconUrl = $"/Custom/Widgets/{Uri.EscapeDataString(w.Name)}/screenshot.png",
        Enabled = true,
        LastUpdated = string.Empty,
    });

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

/// <summary>
/// Widget disponibili e zone del tema attivo (porting di WidgetsRepository). Le zone sono quelle in cui
/// i template del tema usano il tag widget_zone.
/// </summary>
[Route("api/widgets")]
public sealed class WidgetsController(IRightsService rights, ISettingsService settings, ThemeCatalog themes, WidgetCatalog catalog, WidgetService widgets) : AdminApiController
{
    [HttpGet("")]
    public async Task<WidgetsVM> Get()
    {
        await DemandAsync();
        var theme = themes.FindOrFallback((await settings.GetAsync()).Theme);
        var vm = new WidgetsVM
        {
            AvailableWidgets = catalog.GetWidgets()
                .Select(w => new WidgetItem { Id = w.Name, Name = w.Name, Title = w.Title, ShowTitle = false, ShowUnistall = false })
                .ToList(),
        };

        if (theme is null)
        {
            return vm;
        }

        foreach (var zone in WidgetService.FindZones(theme))
        {
            var defaults = theme.DefaultWidgets.TryGetValue(zone, out var list) ? list : null;
            vm.WidgetZones.Add(new WidgetZoneItem
            {
                Id = zone,
                Widgets = (await widgets.GetZoneAsync(zone, defaults))
                    .Select(w => new WidgetItem { Id = w.Id, Name = w.Name, Title = w.Title, ShowTitle = w.ShowTitle })
                    .ToList(),
            });
        }

        return vm;
    }

    /// <summary>
    /// Salva tutte le zone. I widget appena aggiunti (Id numerico provvisorio assegnato dal JavaScript)
    /// ricevono un Id definitivo; gli altri lo conservano, così restano collegati alle loro impostazioni.
    /// </summary>
    [HttpPut("update/{id?}")]
    public async Task<IActionResult> Update([FromBody] List<WidgetZoneItem> zones)
    {
        await DemandAsync();
        foreach (var zone in zones ?? [])
        {
            if (string.IsNullOrWhiteSpace(zone.Id))
            {
                continue;
            }

            var placements = zone.Widgets
                .Where(w => !string.IsNullOrWhiteSpace(w.Name) && catalog.Find(w.Name!) is not null)
                .Select(w => new WidgetPlacement
                {
                    Id = string.IsNullOrEmpty(w.Id) || int.TryParse(w.Id, out _) ? Guid.NewGuid().ToString() : w.Id,
                    Name = w.Name!,
                    Title = string.IsNullOrWhiteSpace(w.Title) ? w.Name! : w.Title.Trim(),
                    ShowTitle = true,
                });

            await widgets.SaveZoneAsync(zone.Id, placements);
        }

        return Ok(true);
    }

    private async Task DemandAsync()
    {
        if (!await rights.IsAuthorizedAsync(Rights.ManageWidgets))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

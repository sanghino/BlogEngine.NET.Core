using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using BlogEngine.Core.Configuration;
using BlogEngine.Core.Data;
using BlogEngine.Services;
using BlogEngine.Text;
using BlogEngine.Web.Hosting;
using Fluid;

namespace BlogEngine.Web.Themes;

/// <summary>Widget disponibile: una cartella con widget.liquid e, facoltativo, il manifest widget.json.</summary>
public sealed record WidgetInfo(string Name, string Title, string Description, string Version, string Authors, string Website, string Folder, bool IsInstalledByUpload)
{
    public string TemplatePath => Path.Combine(Folder, "widget.liquid");

    /// <summary>Impostazioni modificabili dall'amministrazione, descritte nel manifest.</summary>
    public IReadOnlyList<WidgetSettingDefinition> Settings { get; init; } = [];
}

/// <summary>
/// Un'impostazione del widget nel manifest widget.json. Tipi: text, number, checkbox, select, html
/// e quelli speciali category, author, links, newsletter, blogroll. "label" è una chiave delle
/// traduzioni oppure un testo libero.
/// </summary>
public sealed class WidgetSettingDefinition
{
    public string Key { get; set; } = string.Empty;

    public string Type { get; set; } = "text";

    public string Label { get; set; } = string.Empty;

    public string Default { get; set; } = string.Empty;

    public List<WidgetSettingOption> Options { get; set; } = [];
}

public sealed class WidgetSettingOption
{
    public string Value { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// Elenco dei widget: le cartelle di Custom/Widgets (pubblicate o copiate via FTP) e di
/// App_Data/widgets (caricate dall'amministrazione). Ogni cartella contiene widget.liquid.
/// </summary>
public sealed class WidgetCatalog(BlogEnginePaths paths)
{
    public const string ManifestFileName = "widget.json";

    public string BundledFolder => paths.BundledWidgets;

    public string UploadFolder => paths.InstalledWidgets;

    private (DateTime Expires, IReadOnlyList<WidgetInfo> Widgets)? _cache;

    /// <summary>Widget presenti sul disco, riletti ogni pochi secondi (come i temi).</summary>
    public IReadOnlyList<WidgetInfo> GetWidgets()
    {
        if (_cache is { } cache && cache.Expires > DateTime.UtcNow)
        {
            return cache.Widgets;
        }

        var widgets = Load();
        _cache = (DateTime.UtcNow.AddSeconds(5), widgets);
        return widgets;
    }

    public void Refresh() => _cache = null;

    private IReadOnlyList<WidgetInfo> Load() =>
        Scan(BundledFolder, uploaded: false)
            .Concat(Scan(UploadFolder, uploaded: true))
            .GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public WidgetInfo? Find(string name) =>
        GetWidgets().FirstOrDefault(w => string.Equals(w.Name, name, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<WidgetInfo> Scan(string root, bool uploaded)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            if (!File.Exists(Path.Combine(folder, "widget.liquid")))
            {
                continue;
            }

            var name = Path.GetFileName(folder);
            var manifest = ReadManifest(Path.Combine(folder, ManifestFileName));
            yield return new WidgetInfo(
                name,
                manifest?.Title ?? name,
                manifest?.Description ?? string.Empty,
                manifest?.Version ?? string.Empty,
                manifest?.Authors ?? string.Empty,
                manifest?.Website ?? string.Empty,
                folder,
                uploaded)
            {
                Settings = manifest?.Settings ?? [],
            };
        }
    }

    private static WidgetManifest? ReadManifest(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<WidgetManifest>(File.ReadAllText(path), ThemeJson.Options)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class WidgetManifest
    {
        public string? Title { get; set; }

        public string? Description { get; set; }

        public string? Version { get; set; }

        public string? Authors { get; set; }

        public string? Website { get; set; }

        public List<WidgetSettingDefinition>? Settings { get; set; }
    }
}

/// <summary>Widget inserito in una zona (come veniva salvato nel vecchio file della zona).</summary>
public sealed class WidgetPlacement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public bool ShowTitle { get; set; } = true;
}

/// <summary>
/// Prepara i dati di un widget incluso in BlogEngine o aggiunto da un'estensione. Il template
/// del widget li trova in <c>widget.data</c>; i widget senza codice usano solo <c>widget.settings</c>.
/// </summary>
public interface IWidgetDataProvider
{
    /// <summary>Nome della cartella del widget (es. "TagCloud").</summary>
    string Name { get; }

    Task<object?> GetDataAsync(WidgetModel widget, CancellationToken cancellationToken);
}

/// <summary>Zone dei widget e impostazioni dei singoli widget, salvate nell'archivio delle estensioni.</summary>
public sealed partial class WidgetService(
    IExtensionDataStore store,
    IBlogContext context,
    WidgetCatalog catalog,
    IEnumerable<IWidgetDataProvider> providers,
    BlogEngine.Core.Extensions.IExtensionGate gate,
    ILogger<WidgetService> logger)
{
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _settings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nome della zona come veniva normalizzato in origine (niente spazi o caratteri speciali).</summary>
    public static string NormalizeZone(string zone) => Slug.Create(zone.Trim());

    /// <summary>Widget della zona; se la zona non è mai stata salvata si usano quelli previsti dal tema.</summary>
    public async Task<IReadOnlyList<WidgetPlacement>> GetZoneAsync(string zone, IReadOnlyList<string>? defaults = null, CancellationToken cancellationToken = default)
    {
        var json = await store.LoadAsync(context.Blog.Id, ExtensionDataKind.Widget, ZoneKey(zone), cancellationToken);
        if (json is null)
        {
            return (defaults ?? [])
                .Select(name => catalog.Find(name))
                .Where(w => w is not null)
                .Select(w => new WidgetPlacement { Id = DefaultId(zone, w!.Name), Name = w!.Name, Title = w.Title })
                .ToList();
        }

        try
        {
            return JsonSerializer.Deserialize<List<WidgetPlacement>>(json, ThemeJson.Options) ?? [];
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Zona dei widget {Zone} non leggibile.", zone);
            return [];
        }
    }

    public Task SaveZoneAsync(string zone, IEnumerable<WidgetPlacement> widgets, CancellationToken cancellationToken = default) =>
        store.SaveAsync(context.Blog.Id, ExtensionDataKind.Widget, ZoneKey(zone), JsonSerializer.Serialize(widgets.ToList(), ThemeJson.Options), cancellationToken);

    public async Task<IReadOnlyDictionary<string, string>> GetSettingsAsync(string widgetId, CancellationToken cancellationToken = default)
    {
        if (_settings.TryGetValue(widgetId, out var cached))
        {
            return cached;
        }

        var json = await store.LoadAsync(context.Blog.Id, ExtensionDataKind.Widget, widgetId, cancellationToken);
        Dictionary<string, string> settings;
        try
        {
            settings = json is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            settings = [];
        }

        var result = new Dictionary<string, string>(settings, StringComparer.OrdinalIgnoreCase);
        _settings[widgetId] = result;
        return result;
    }

    public async Task SaveSettingsAsync(string widgetId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default)
    {
        await store.SaveAsync(context.Blog.Id, ExtensionDataKind.Widget, widgetId, JsonSerializer.Serialize(settings), cancellationToken);
        _settings.Remove(widgetId);
    }

    /// <summary>Scrive la zona con i suoi widget (porting del controllo WidgetZone).</summary>
    internal async Task RenderZoneAsync(ThemeEngine engine, ThemeScope scope, string zone, TextWriter writer, TextEncoder encoder, TemplateContext templateContext)
    {
        var name = NormalizeZone(zone);
        await writer.WriteAsync($"<div id=\"widgetzone_{encoder.Encode(name)}\" class=\"widgetzone\">");
        var defaults = scope.Theme.DefaultWidgets.TryGetValue(name, out var list) ? list : null;
        foreach (var placement in await GetZoneAsync(name, defaults))
        {
            await RenderWidgetAsync(engine, scope, name, placement, writer, encoder, templateContext);
        }

        await writer.WriteAsync("</div>");
    }

    private async Task RenderWidgetAsync(ThemeEngine engine, ThemeScope scope, string zone, WidgetPlacement placement, TextWriter writer, TextEncoder encoder, TemplateContext templateContext)
    {
        var widget = catalog.Find(placement.Name);
        if (widget is null)
        {
            await writer.WriteAsync($"<p style=\"color:red\">Widget {encoder.Encode(placement.Name)} not found.</p>");
            return;
        }

        try
        {
            var model = new WidgetModel
            {
                Id = placement.Id,
                Name = widget.Name,
                Title = placement.Title,
                ShowTitle = placement.ShowTitle,
                Zone = zone,
                Settings = await GetSettingsAsync(placement.Id),
            };

            var provider = providers.FirstOrDefault(p => gate.IsActive(p) && string.Equals(p.Name, widget.Name, StringComparison.OrdinalIgnoreCase));
            if (provider is not null)
            {
                model = new WidgetModel
                {
                    Id = model.Id,
                    Name = model.Name,
                    Title = model.Title,
                    ShowTitle = model.ShowTitle,
                    Zone = model.Zone,
                    Settings = model.Settings,
                    Data = await provider.GetDataAsync(model, CancellationToken.None),
                };
            }

            // Il tema può ridefinire l'aspetto di un widget con il file widgets/<Nome>.liquid.
            var themeOverride = Path.Combine(scope.Theme.Folder, "widgets", widget.Name + ThemeEngine.TemplateExtension);
            var path = File.Exists(themeOverride) ? themeOverride : widget.TemplatePath;

            templateContext.EnterChildScope();
            try
            {
                templateContext.SetValue("widget", model);

                // Un widget che non ha nulla da mostrare (annuncio senza codice, menu di amministrazione per
                // i visitatori) non lascia righe vuote: così una zona senza contenuti resta davvero vuota.
                using var buffer = new StringWriter();
                await engine.RenderFileAsync(path, buffer, encoder, templateContext);
                var html = buffer.ToString();
                if (!string.IsNullOrWhiteSpace(html))
                {
                    await writer.WriteAsync(html);
                }
            }
            finally
            {
                templateContext.ReleaseScope();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Errore nel widget {Widget}.", widget.Name);
            await writer.WriteAsync($"<p style=\"color:red\">Widget {encoder.Encode(widget.Name)}: error, check the log for details.</p>");
        }
    }

    /// <summary>Nomi delle zone usate dai template del tema (tag widget_zone).</summary>
    public static IReadOnlyList<string> FindZones(ThemeInfo theme)
    {
        if (!Directory.Exists(theme.Folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(theme.Folder, "*" + ThemeEngine.TemplateExtension, SearchOption.AllDirectories)
            .SelectMany(file => ZoneTagRegex().Matches(File.ReadAllText(file)).Select(m => NormalizeZone(m.Groups["zone"].Value)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Campi personalizzati usati dai template del tema ({{ 'Chiave' | theme_field: 'predefinito' }}),
    /// con il loro valore predefinito: l'amministrazione li mostra per poterli modificare.
    /// </summary>
    public static IReadOnlyList<(string Key, string Default)> FindThemeFields(ThemeInfo theme)
    {
        if (!Directory.Exists(theme.Folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(theme.Folder, "*" + ThemeEngine.TemplateExtension, SearchOption.AllDirectories)
            .SelectMany(file => ThemeFieldRegex().Matches(File.ReadAllText(file)))
            .Select(m => (Key: m.Groups["key"].Value, Default: m.Groups["default"].Value))
            .DistinctBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ZoneKey(string zone) => "zone-" + NormalizeZone(zone);

    /// <summary>Id stabile per i widget predefiniti, così le loro impostazioni restano collegate.</summary>
    private static string DefaultId(string zone, string widget) => $"default-{NormalizeZone(zone)}-{widget}".ToLowerInvariant();

    [GeneratedRegex(@"\{%-?\s*widget_zone\s+['""](?<zone>[^'""]+)['""]")]
    private static partial Regex ZoneTagRegex();

    [GeneratedRegex(@"'(?<key>[^']+)'\s*\|\s*theme_field\s*:\s*'(?<default>[^']*)'")]
    private static partial Regex ThemeFieldRegex();
}

internal static class ThemeJson
{
    public static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
}

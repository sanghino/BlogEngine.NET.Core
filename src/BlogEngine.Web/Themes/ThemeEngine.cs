using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Encodings.Web;
using Fluid;
using Fluid.Ast;
using Fluid.Values;
using Microsoft.AspNetCore.Html;
using Microsoft.Extensions.FileProviders;

namespace BlogEngine.Web.Themes;

/// <summary>
/// Motore dei temi Liquid (libreria Fluid). I template vengono letti dal disco e interpretati
/// a ogni modifica del file: un tema si installa o si aggiorna copiando la sua cartella, senza
/// ricompilare né riavviare il sito.
/// <para>
/// Un template viene cercato prima nella cartella del tema e poi in <c>Templates/</c>, che
/// contiene le versioni predefinite: un tema deve fornire solo ciò che vuole personalizzare.
/// </para>
/// </summary>
public sealed class ThemeEngine
{
    public const string TemplateExtension = ".liquid";

    /// <summary>Chiave con cui il contesto della richiesta viene passato a tag e filtri.</summary>
    internal const string ScopeKey = "BlogEngine.ThemeScope";

    /// <summary>
    /// Codifica HTML che lascia invariate le lettere accentate e gli altri caratteri Unicode
    /// (quella predefinita le scriverebbe come &amp;#xE8;); &lt;, &gt;, &amp; e virgolette restano codificati.
    /// </summary>
    internal static readonly HtmlEncoder Encoder = HtmlEncoder.Create(System.Text.Unicode.UnicodeRanges.All);

    private readonly FluidParser _parser = new(new FluidParserOptions { AllowFunctions = false, AllowParentheses = true });
    private readonly ConcurrentDictionary<string, TemplateOptions> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedTemplate> _files = new(StringComparer.Ordinal);
    private readonly string _defaultsFolder;
    private readonly ILogger<ThemeEngine> _logger;

    public ThemeEngine(IWebHostEnvironment environment, ILogger<ThemeEngine> logger)
    {
        _defaultsFolder = Path.Combine(environment.ContentRootPath, "Templates");
        _logger = logger;

        // {% widget_zone 'sidebar' %}: mostra i widget che l'amministratore ha messo nella zona.
        _parser.RegisterExpressionTag("widget_zone", async (expression, writer, encoder, context) =>
        {
            var zone = (await expression.EvaluateAsync(context)).ToStringValue();
            if (GetScope(context) is { } scope)
            {
                await scope.RenderWidgetZoneAsync(this, zone, writer, encoder, context);
            }

            return Completion.Normal;
        });
    }

    public string DefaultsFolder => _defaultsFolder;

    /// <summary>
    /// Mostra una pagina completa: prima il template della pagina (es. "post"), poi il layout del
    /// tema che lo contiene. Per i post si usa "layout-post" e per le altre pagine "layout-page",
    /// se il tema li prevede, altrimenti "layout" (come post.master, page.master e site.master).
    /// </summary>
    public async Task<string> RenderPageAsync(ThemeScope scope, string template, string layout, IReadOnlyDictionary<string, object?> values)
    {
        var context = CreateContext(scope, values);
        var body = await RenderTemplateAsync(scope, template, context);
        context.SetValue("content_for_layout", new StringValue(body, false));

        var layoutName = !string.IsNullOrEmpty(layout) && Exists(scope, layout) ? layout : "layout";
        return await RenderTemplateAsync(scope, layoutName, context);
    }

    /// <summary>Mostra un solo template senza layout (es. un commento appena inviato).</summary>
    public async Task<string> RenderPartialAsync(ThemeScope scope, string template, IReadOnlyDictionary<string, object?> values)
    {
        var context = CreateContext(scope, values);
        return await RenderTemplateAsync(scope, template, context);
    }

    /// <summary>Mostra un template indicato con il percorso completo (i widget) nel contesto corrente.</summary>
    internal async Task RenderFileAsync(string path, TextWriter writer, TextEncoder encoder, TemplateContext context)
    {
        var template = ParseFile(path);
        await template.RenderAsync(writer, encoder, context);
    }

    public bool Exists(ThemeScope scope, string template) =>
        GetOptions(scope.Theme.Folder).FileProvider.GetFileInfo(template + TemplateExtension).Exists;

    /// <summary>Svuota la cache dei template (dopo l'installazione o la rimozione di un tema).</summary>
    public void Reset()
    {
        _options.Clear();
        _files.Clear();
    }

    private async Task<string> RenderTemplateAsync(ThemeScope scope, string name, TemplateContext context)
    {
        var options = GetOptions(scope.Theme.Folder);
        var file = options.FileProvider.GetFileInfo(name + TemplateExtension);
        if (!file.Exists || file.PhysicalPath is null)
        {
            throw new FileNotFoundException($"Il template \"{name}{TemplateExtension}\" non esiste né nel tema \"{scope.Theme.Id}\" né in Templates/.");
        }

        var template = ParseFile(file.PhysicalPath);
        await using var writer = new StringWriter(CultureInfo.InvariantCulture);
        await template.RenderAsync(writer, Encoder, context);
        return writer.ToString();
    }

    /// <summary>Legge e interpreta un file, riutilizzando il risultato finché il file non cambia.</summary>
    private IFluidTemplate ParseFile(string path)
    {
        var modified = File.GetLastWriteTimeUtc(path);
        if (_files.TryGetValue(path, out var cached) && cached.Modified == modified)
        {
            return cached.Template;
        }

        var source = File.ReadAllText(path);
        if (!_parser.TryParse(source, out var template, out var error))
        {
            _logger.LogError("Errore nel template {Path}: {Error}", path, error);
            throw new ThemeTemplateException($"Errore nel template {Path.GetFileName(path)}: {error}");
        }

        _files[path] = new CachedTemplate(template, modified);
        return template;
    }

    private TemplateContext CreateContext(ThemeScope scope, IReadOnlyDictionary<string, object?> values)
    {
        var context = new TemplateContext(GetOptions(scope.Theme.Folder))
        {
            CultureInfo = CultureInfo.CurrentCulture,
        };

        context.AmbientValues[ScopeKey] = scope;
        foreach (var (key, value) in scope.GlobalValues)
        {
            context.SetValue(key, value);
        }

        foreach (var (key, value) in values)
        {
            context.SetValue(key, value);
        }

        return context;
    }

    private TemplateOptions GetOptions(string themeFolder) => _options.GetOrAdd(themeFolder, folder =>
    {
        var providers = new List<IFileProvider>();
        if (Directory.Exists(folder))
        {
            providers.Add(new PhysicalFileProvider(folder));
        }

        if (Directory.Exists(_defaultsFolder))
        {
            providers.Add(new PhysicalFileProvider(_defaultsFolder));
        }

        var options = new TemplateOptions
        {
            FileProvider = new CompositeFileProvider(providers),
            MemberAccessStrategy = new UnsafeMemberAccessStrategy { MemberNameStrategy = MemberNameStrategies.SnakeCase },
            MaxRecursion = 30,
        };

        options.ValueConverters.Add(value => value is IHtmlContent html ? new StringValue(ToHtml(html), false) : null);
        ThemeFilters.Register(options.Filters);
        return options;
    });

    internal static ThemeScope? GetScope(TemplateContext context) =>
        context.AmbientValues.TryGetValue(ScopeKey, out var value) ? value as ThemeScope : null;

    private static string ToHtml(IHtmlContent html)
    {
        if (html is HtmlString text)
        {
            return text.Value ?? string.Empty;
        }

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        html.WriteTo(writer, Encoder);
        return writer.ToString();
    }

    private sealed record CachedTemplate(IFluidTemplate Template, DateTime Modified);
}

/// <summary>Un template del tema contiene un errore di sintassi.</summary>
public sealed class ThemeTemplateException(string message) : Exception(message);

using System.Text.Encodings.Web;
using BlogEngine.Web.Hosting;
using Fluid;

namespace BlogEngine.Web.Themes;

/// <summary>
/// Tutto ciò che serve ai template durante una richiesta: il tema scelto, gli indirizzi di base,
/// le traduzioni e le variabili comuni (<c>blog</c>, <c>user</c>, <c>content_for_header</c>, ...).
/// </summary>
public sealed class ThemeScope
{
    private readonly Func<string, string> _translate;
    private readonly Func<string, Task<string?>> _themeField;
    private readonly WidgetService _widgets;

    internal ThemeScope(ThemeInfo theme, string blogRoot, string appRoot, Func<string, string> translate, Func<string, Task<string?>> themeField, WidgetService widgets)
    {
        Theme = theme;
        BlogRoot = blogRoot;
        AppRoot = appRoot;
        ThemeUrl = $"{appRoot}Custom/Themes/{Uri.EscapeDataString(theme.Id)}/";
        _translate = translate;
        _themeField = themeField;
        _widgets = widgets;
    }

    public ThemeInfo Theme { get; }

    /// <summary>Radice del blog corrente (es. "/" o "/secondo/").</summary>
    public string BlogRoot { get; }

    /// <summary>Radice dell'applicazione, dove si trovano Scripts/, Content/ e Custom/.</summary>
    public string AppRoot { get; }

    /// <summary>Indirizzo della cartella del tema (Custom/Themes/Nome/).</summary>
    public string ThemeUrl { get; }

    /// <summary>Variabili disponibili in tutti i template.</summary>
    public Dictionary<string, object?> GlobalValues { get; } = new(StringComparer.Ordinal);

    public string Translate(string key) => _translate(key);

    public Task<string?> GetThemeFieldAsync(string key) => _themeField(key);

    internal Task RenderWidgetZoneAsync(ThemeEngine engine, string zone, TextWriter writer, TextEncoder encoder, TemplateContext context) =>
        _widgets.RenderZoneAsync(engine, this, zone, writer, encoder, context);
}

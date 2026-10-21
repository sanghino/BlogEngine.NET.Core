using System.Globalization;
using Fluid;
using Fluid.Filters;
using Fluid.Values;

namespace BlogEngine.Web.Themes;

/// <summary>
/// Filtri Liquid aggiunti da BlogEngine, oltre a quelli standard (upcase, truncate, strip_html, ...).
/// <list type="bullet">
/// <item><c>{{ 'addComment' | t }}</c> – testo tradotto nella lingua del blog; con argomenti
/// sostituisce {0}, {1}...: <c>{{ 'currentlyRated' | t: post.rating, post.raters }}</c>.</item>
/// <item><c>{{ 'src/css/style.css' | theme_url }}</c> – indirizzo di un file del tema.</item>
/// <item><c>{{ 'Scripts/blog.js' | app_url }}</c> – indirizzo di un file comune del sito.</item>
/// <item><c>{{ 'archive' | blog_url }}</c> – indirizzo all'interno del blog corrente.</item>
/// <item><c>{{ 'Facebook Account' | theme_field: '#' }}</c> – campo personalizzato del tema,
/// modificabile dall'amministrazione, con il valore predefinito indicato.</item>
/// <item><c>{{ post.date_created | date_format: 'dd MMMM yyyy' }}</c> – data in formato .NET.</item>
/// </list>
/// </summary>
internal static class ThemeFilters
{
    public static void Register(FilterCollection filters)
    {
        filters.AddFilter("t", Translate);
        filters.AddFilter("theme_url", (input, _, context) => Url(context, s => s.ThemeUrl, input));
        filters.AddFilter("app_url", (input, _, context) => Url(context, s => s.AppRoot, input));
        filters.AddFilter("blog_url", (input, _, context) => Url(context, s => s.BlogRoot, input));
        filters.AddFilter("theme_field", ThemeField);
        filters.AddFilter("date_format", DateFormat);

        // Sostituisce il filtro json standard, il cui risultato verrebbe codificato in HTML: qui è
        // JSON valido e sicuro dentro <script> (i caratteri <, > e & diventano sequenze \u....).
        filters.AddFilter("json", Json);
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default,
    };

    private static ValueTask<FluidValue> Json(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        object? value = input.Type switch
        {
            FluidValues.Nil => null,
            FluidValues.Boolean => input.ToBooleanValue(),
            FluidValues.Number => input.ToNumberValue(),
            _ => input.ToStringValue(),
        };

        return new ValueTask<FluidValue>(new StringValue(System.Text.Json.JsonSerializer.Serialize(value, JsonOptions), false));
    }

    private static ValueTask<FluidValue> Translate(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        var key = input.ToStringValue();
        var scope = ThemeEngine.GetScope(context);
        var text = scope?.Translate(key) ?? key;
        if (arguments.Count > 0)
        {
            var values = arguments.Values.Select(v => v.ToObjectValue()).ToArray();
            try
            {
                text = string.Format(context.CultureInfo, text, values);
            }
            catch (FormatException)
            {
                // Una traduzione con segnaposto errati non deve bloccare la pagina.
            }
        }

        return new ValueTask<FluidValue>(new StringValue(text));
    }

    private static ValueTask<FluidValue> Url(TemplateContext context, Func<ThemeScope, string> root, FluidValue input)
    {
        var scope = ThemeEngine.GetScope(context);
        var value = input.ToStringValue();

        // Gli indirizzi completi (es. un logo su un altro sito) restano invariati.
        if (value.Contains("://", StringComparison.Ordinal) || value.StartsWith("//", StringComparison.Ordinal))
        {
            return new ValueTask<FluidValue>(new StringValue(value));
        }

        return new ValueTask<FluidValue>(new StringValue((scope is null ? "/" : root(scope)) + value.TrimStart('/')));
    }

    private static async ValueTask<FluidValue> ThemeField(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        var fallback = arguments.At(0).ToStringValue();
        var scope = ThemeEngine.GetScope(context);
        var value = scope is null ? null : await scope.GetThemeFieldAsync(input.ToStringValue());
        return new StringValue(string.IsNullOrEmpty(value) ? fallback : value);
    }

    private static ValueTask<FluidValue> DateFormat(FluidValue input, FilterArguments arguments, TemplateContext context)
    {
        var format = arguments.Count > 0 ? arguments.At(0).ToStringValue() : "d";
        DateTime? date = input.ToObjectValue() switch
        {
            DateTime d => d,
            DateTimeOffset o => o.DateTime,
            string s when DateTime.TryParse(s, context.CultureInfo, DateTimeStyles.None, out var parsed) => parsed,
            _ => null,
        };

        return new ValueTask<FluidValue>(date is null ? NilValue.Instance : new StringValue(date.Value.ToString(format, context.CultureInfo)));
    }
}

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BlogEngine.Models;

namespace BlogEngine.Web.Themes;

/// <summary>Una sottopagina da mostrare nell'elenco della pagina genitore.</summary>
public sealed class SubpageItem
{
    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>Prima immagine del testo della pagina, per le schede.</summary>
    public string Image { get; init; } = string.Empty;

    public DateTime DateCreated { get; init; }

    public int SortOrder { get; init; }
}

/// <summary>Scelte per un elenco di sottopagine: quelle della pagina, eventualmente cambiate dal segnaposto.</summary>
public sealed record SubpagesOptions
{
    public SubpagesStyle Style { get; init; }

    public int Columns { get; init; } = 3;

    public bool Images { get; init; }

    public string Title { get; init; } = string.Empty;

    public SubpagesOrder Order { get; init; }

    public static SubpagesOptions From(Page page) => new()
    {
        Style = page.SubpagesStyle,
        Columns = Math.Clamp(page.SubpagesColumns == 0 ? 3 : page.SubpagesColumns, 2, 4),
        Images = page.SubpagesImages,
        Title = page.SubpagesTitle,
        Order = page.SubpagesOrder,
    };
}

/// <summary>
/// Elenco delle sottopagine di una pagina, in fondo o al posto del segnaposto "[sottopagine]" (o "[subpages]").
/// Il segnaposto accetta opzioni che valgono solo per quell'elenco, in italiano o in inglese:
/// <code>[sottopagine stile=schede colonne=3 immagini=si titolo="In questa sezione" ordine=alfabetico]</code>
/// stile/style: elenco|list, numerato|numbered, descrizioni|descriptions, schede|cards, pulsanti|buttons, riga|inline;
/// ordine/order: manuale|manual, alfabetico|title, recenti|newest; colonne/columns: 2-4; immagini/images: si|no.
/// Se la pagina non ha sottopagine il segnaposto sparisce.
/// </summary>
public static partial class SubpagesPlaceholder
{
    /// <summary>Il segnaposto, da solo in un paragrafo (come lo scrive l'editor) oppure in mezzo al testo.</summary>
    [GeneratedRegex(@"<p[^>]*>(?:\s|&nbsp;)*(?<tag>\[(?:sottopagine|subpages)(?:\s[^\]]*)?\])(?:\s|&nbsp;)*</p>|(?<tag>\[(?:sottopagine|subpages)(?:\s[^\]]*)?\])", RegexOptions.IgnoreCase)]
    private static partial Regex Marker();

    [GeneratedRegex(@"(?<key>[a-z]+)\s*=\s*(?:""(?<value>[^""]*)""|&quot;(?<value>.*?)&quot;|(?<value>[^\s\]]+))", RegexOptions.IgnoreCase)]
    private static partial Regex Option();

    [GeneratedRegex(@"<img[^>]+src\s*=\s*[""'](?<src>[^""']+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex FirstImage();

    public static bool IsIn(string? html) => !string.IsNullOrEmpty(html) && Marker().IsMatch(html);

    /// <summary>Prima immagine di un testo HTML, o vuoto.</summary>
    public static string ImageOf(string? html) =>
        string.IsNullOrEmpty(html) ? string.Empty : WebUtility.HtmlDecode(FirstImage().Match(html).Groups["src"].Value);

    /// <summary>Sostituisce ogni segnaposto con l'elenco delle sottopagine, con le sue opzioni.</summary>
    public static string Replace(string html, IReadOnlyList<SubpageItem> children, SubpagesOptions defaults) =>
        Marker().Replace(html, m => Render(children, Parse(m.Groups["tag"].Value, defaults)));

    /// <summary>Testo senza segnaposto, per riassunti e descrizioni.</summary>
    public static string Remove(string? html) => string.IsNullOrEmpty(html) ? html ?? string.Empty : Marker().Replace(html, string.Empty);

    /// <summary>Opzioni scritte nel segnaposto; quelle assenti restano come nella pagina.</summary>
    public static SubpagesOptions Parse(string tag, SubpagesOptions defaults)
    {
        var options = defaults;
        foreach (Match match in Option().Matches(WebUtility.HtmlDecode(tag)))
        {
            var value = match.Groups["value"].Value.Trim();
            var lower = value.ToLowerInvariant();
            switch (match.Groups["key"].Value.ToLowerInvariant())
            {
                case "stile" or "style":
                    options = options with
                    {
                        Style = lower switch
                        {
                            "numerato" or "numbered" or "numeri" => SubpagesStyle.Numbered,
                            "descrizioni" or "descrizione" or "descriptions" => SubpagesStyle.Descriptions,
                            "schede" or "cards" => SubpagesStyle.Cards,
                            "pulsanti" or "buttons" => SubpagesStyle.Buttons,
                            "riga" or "inline" => SubpagesStyle.Inline,
                            "elenco" or "list" => SubpagesStyle.List,
                            _ => options.Style,
                        },
                    };
                    break;
                case "ordine" or "order":
                    options = options with
                    {
                        Order = lower switch
                        {
                            "alfabetico" or "titolo" or "title" or "alphabetical" => SubpagesOrder.Title,
                            "recenti" or "newest" or "data" or "date" => SubpagesOrder.Newest,
                            "manuale" or "manual" => SubpagesOrder.Manual,
                            _ => options.Order,
                        },
                    };
                    break;
                case "colonne" or "columns":
                    if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var columns))
                    {
                        options = options with { Columns = Math.Clamp(columns, 2, 4) };
                    }

                    break;
                case "immagini" or "immagine" or "images" or "image":
                    options = options with { Images = lower is "si" or "sì" or "yes" or "true" or "1" };
                    break;
                case "titolo" or "title":
                    options = options with { Title = value };
                    break;
            }
        }

        return options;
    }

    /// <summary>L'elenco nello stile scelto; vuoto se non ci sono sottopagine.</summary>
    public static string Render(IReadOnlyList<SubpageItem> children, SubpagesOptions options)
    {
        if (children.Count == 0)
        {
            return string.Empty;
        }

        static string E(string value) => WebUtility.HtmlEncode(value);
        IEnumerable<SubpageItem> ordered = options.Order switch
        {
            SubpagesOrder.Title => children.OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase),
            SubpagesOrder.Newest => children.OrderByDescending(c => c.DateCreated),
            _ => children.OrderBy(c => c.SortOrder),
        };

        var html = new StringBuilder();
        if (options.Title.Trim().Length > 0)
        {
            html.Append("<h3 class=\"page-children-title\">").Append(E(options.Title.Trim())).Append("</h3>");
        }

        switch (options.Style)
        {
            case SubpagesStyle.Numbered:
                html.Append("<ol class=\"page-children page-children-numbered\">");
                foreach (var child in ordered)
                {
                    html.Append("<li><a href=\"").Append(E(child.Url)).Append("\">").Append(E(child.Title)).Append("</a></li>");
                }

                html.Append("</ol>");
                break;

            case SubpagesStyle.Descriptions:
                html.Append("<ul class=\"page-children page-children-descriptions\">");
                foreach (var child in ordered)
                {
                    html.Append("<li><a href=\"").Append(E(child.Url)).Append("\">").Append(E(child.Title)).Append("</a>");
                    if (child.Description.Length > 0)
                    {
                        html.Append("<p class=\"page-child-description\">").Append(E(child.Description)).Append("</p>");
                    }

                    html.Append("</li>");
                }

                html.Append("</ul>");
                break;

            case SubpagesStyle.Cards:
                html.Append("<div class=\"page-children page-children-cards page-children-cols-")
                    .Append(options.Columns.ToString(CultureInfo.InvariantCulture)).Append("\">");
                foreach (var child in ordered)
                {
                    html.Append("<a class=\"page-child-card\" href=\"").Append(E(child.Url)).Append("\">");
                    if (options.Images && child.Image.Length > 0)
                    {
                        html.Append("<span class=\"page-child-image\"><img src=\"").Append(E(child.Image)).Append("\" alt=\"\" loading=\"lazy\" /></span>");
                    }

                    html.Append("<span class=\"page-child-title\">").Append(E(child.Title)).Append("</span>");
                    if (child.Description.Length > 0)
                    {
                        html.Append("<span class=\"page-child-description\">").Append(E(child.Description)).Append("</span>");
                    }

                    html.Append("</a>");
                }

                html.Append("</div>");
                break;

            case SubpagesStyle.Buttons:
                html.Append("<div class=\"page-children page-children-buttons\">");
                foreach (var child in ordered)
                {
                    html.Append("<a class=\"page-child-button\" href=\"").Append(E(child.Url)).Append("\">").Append(E(child.Title)).Append("</a>");
                }

                html.Append("</div>");
                break;

            case SubpagesStyle.Inline:
                html.Append("<p class=\"page-children page-children-inline\">")
                    .AppendJoin("<span class=\"page-children-sep\"> · </span>", ordered.Select(c => $"<a href=\"{E(c.Url)}\">{E(c.Title)}</a>"))
                    .Append("</p>");
                break;

            default:
                // Come è sempre stato: elenco puntato con i titoli.
                html.Append("<ul class=\"page-children\">");
                foreach (var child in ordered)
                {
                    html.Append("<li><a href=\"").Append(E(child.Url)).Append("\">").Append(E(child.Title)).Append("</a></li>");
                }

                html.Append("</ul>");
                break;
        }

        return html.ToString();
    }
}

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BlogEngine.Web.Themes;

/// <summary>
/// Segnaposto "[sottopagine]" (o "[subpages]") nel testo di una pagina: l'elenco delle sottopagine compare in
/// quel punto invece che in fondo. Se la pagina non ha sottopagine il segnaposto sparisce.
/// </summary>
public static partial class SubpagesPlaceholder
{
    /// <summary>Il segnaposto, da solo in un paragrafo (come lo scrive l'editor) oppure in mezzo al testo.</summary>
    [GeneratedRegex(@"<p[^>]*>(?:\s|&nbsp;)*\[(?:sottopagine|subpages)\](?:\s|&nbsp;)*</p>|\[(?:sottopagine|subpages)\]", RegexOptions.IgnoreCase)]
    private static partial Regex Marker();

    public static bool IsIn(string? html) => !string.IsNullOrEmpty(html) && Marker().IsMatch(html);

    /// <summary>Sostituisce il segnaposto (anche se ripetuto) con l'elenco delle sottopagine.</summary>
    public static string Replace(string html, IReadOnlyList<PageLinkModel> children)
    {
        var list = new StringBuilder();
        if (children.Count > 0)
        {
            list.Append("<ul class=\"page-children\">");
            foreach (var child in children)
            {
                list.Append("<li><a href=\"").Append(WebUtility.HtmlEncode(child.Url)).Append("\">")
                    .Append(WebUtility.HtmlEncode(child.Title)).Append("</a></li>");
            }

            list.Append("</ul>");
        }

        return Marker().Replace(html, list.ToString());
    }

    /// <summary>Testo senza segnaposto, per riassunti e descrizioni.</summary>
    public static string Remove(string? html) => string.IsNullOrEmpty(html) ? html ?? string.Empty : Marker().Replace(html, string.Empty);
}

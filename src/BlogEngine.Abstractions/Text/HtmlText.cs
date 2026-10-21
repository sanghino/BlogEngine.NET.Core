using System.Text.RegularExpressions;

namespace BlogEngine.Text;

/// <summary>
/// Funzioni di utilità per il testo HTML.
/// </summary>
public static partial class HtmlText
{
    /// <summary>
    /// Rimuove i tag HTML. Le entità (&amp;lt; ecc.) restano codificate, così il risultato
    /// non può reintrodurre markup se viene stampato senza ulteriore codifica.
    /// </summary>
    public static string Strip(string? html) =>
        string.IsNullOrWhiteSpace(html) ? string.Empty : TagRegex().Replace(html, string.Empty).Trim();

    /// <summary>
    /// Testo semplice ricavato dall'HTML: senza tag, con le entità convertite in caratteri
    /// (&amp;egrave; → è) e gli spazi compattati. Il risultato va codificato quando lo si scrive in una pagina.
    /// </summary>
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // Uno spazio al posto di ogni tag evita di unire le parole di paragrafi diversi.
        var text = System.Net.WebUtility.HtmlDecode(TagRegex().Replace(html, " "));
        return SpacesRegex().Replace(text, " ").Trim();
    }

    /// <summary>Testo semplice troncato a <paramref name="maxLength"/> caratteri, con "..." finale se tagliato.</summary>
    public static string Excerpt(string? html, int maxLength)
    {
        var text = Strip(html);
        return maxLength <= 0 || text.Length <= maxLength ? text : $"{text[..maxLength].TrimEnd()}...";
    }

    /// <summary>Indirizzo della prima immagine contenuta nell'HTML, oppure null.</summary>
    public static string? FirstImageSource(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return null;
        }

        var match = ImageRegex().Match(html);
        return match.Success ? match.Groups[2].Value : null;
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex("""<img\s+?.*?src=('|")(.*?)\1.*?>""", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ImageRegex();
}

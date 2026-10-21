using System.Globalization;
using System.Text;

namespace BlogEngine.Text;

/// <summary>
/// Trasforma un testo libero (titolo, tag, nome di categoria o autore) nella forma usata negli URL.
/// </summary>
public static class Slug
{
    private static readonly char[] RemovedChars = [':', '/', '\\', '?', '#', '[', ']', '@', '*', '.', ',', '"', '&', '\'', '<', '>', '%', '+', '=', ';', '|', '!', '$', '^', '`', '{', '}'];

    /// <summary>
    /// Rimuove i caratteri non validi negli URL, gli accenti e la punteggiatura tipografica,
    /// sostituisce gli spazi con trattini e compatta i trattini ripetuti. Le maiuscole vengono mantenute.
    /// </summary>
    public static string Create(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Trim().Normalize(NormalizationForm.FormD))
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.InitialQuotePunctuation
                or UnicodeCategory.FinalQuotePunctuation
                or UnicodeCategory.Control)
            {
                continue;
            }

            if (Array.IndexOf(RemovedChars, c) >= 0)
            {
                continue;
            }

            // Spazi e trattini tipografici (Live Writer invia il carattere 8211) diventano "-".
            builder.Append(char.IsWhiteSpace(c) || category == UnicodeCategory.DashPunctuation ? '-' : c);
        }

        var slug = builder.ToString().Normalize(NormalizationForm.FormC);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return slug.Trim('-');
    }

    /// <summary>Confronta due testi nella loro forma URL, senza distinguere le maiuscole.</summary>
    public static bool AreEquivalent(string? a, string? b) =>
        string.Equals(Create(a), Create(b), StringComparison.OrdinalIgnoreCase);
}

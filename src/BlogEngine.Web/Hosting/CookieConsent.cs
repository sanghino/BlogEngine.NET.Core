using System.Text.RegularExpressions;

namespace BlogEngine.Web.Hosting;

/// <summary>
/// Consenso ai cookie del visitatore (GDPR, linee guida del Garante 2021). Con il banner attivo, gli script
/// di una categoria non ancora accettata vengono scritti "spenti" (<c>type="text/plain"</c>, che il browser
/// non esegue) e Scripts/consent.js li accende appena il visitatore acconsente. Chi ha già scelto riceve
/// la pagina con gli script accettati già attivi.
/// </summary>
public sealed partial class CookieConsent
{
    /// <summary>Script di statistiche (Google Analytics...): codice nell'head e script di tracciamento.</summary>
    public const string Statistics = "statistics";

    /// <summary>Annunci pubblicitari (widget Advertisement).</summary>
    public const string Marketing = "marketing";

    public const string CookieName = "blogengine_consent";

    /// <summary>Durata della scelta: dopo sei mesi il banner viene riproposto.</summary>
    public const int CookieDays = 180;

    private const string Version = "v1";

    private static readonly HashSet<string> JavaScriptTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        string.Empty, "text/javascript", "application/javascript", "module", "text/ecmascript", "application/ecmascript",
    };

    private readonly HashSet<string> _granted;

    private CookieConsent(bool enabled, bool hasChoice, IEnumerable<string> granted)
    {
        Enabled = enabled;
        HasChoice = hasChoice;
        _granted = new HashSet<string>(granted, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Banner disattivato: tutto è consentito.</summary>
    public static CookieConsent Disabled { get; } = new(false, false, []);

    /// <summary>Il banner è attivo per il blog.</summary>
    public bool Enabled { get; }

    /// <summary>Il visitatore ha già fatto la sua scelta (accetta, rifiuta o personalizzata).</summary>
    public bool HasChoice { get; }

    /// <summary>
    /// Legge la scelta dal cookie "blogengine_consent", nel formato "v1:statistics,marketing"
    /// ("v1:" = solo cookie necessari). Un valore sconosciuto vale come nessuna scelta.
    /// </summary>
    public static CookieConsent FromRequest(HttpRequest request, bool enabled)
    {
        if (!enabled)
        {
            return Disabled;
        }

        var value = request.Cookies[CookieName];
        if (string.IsNullOrEmpty(value) || !value.StartsWith(Version + ":", StringComparison.Ordinal))
        {
            return new CookieConsent(true, false, []);
        }

        var granted = value[(Version.Length + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new CookieConsent(true, true, granted);
    }

    /// <summary>La categoria può essere usata (banner disattivato oppure consenso dato).</summary>
    public bool Allows(string category) => !Enabled || _granted.Contains(category);

    /// <summary>
    /// Codice HTML con gli script della categoria spenti finché manca il consenso. Gli script che non sono
    /// JavaScript (es. JSON-LD) e il resto dell'HTML restano invariati.
    /// </summary>
    public string Gate(string? html, string category)
    {
        if (string.IsNullOrEmpty(html) || Allows(category))
        {
            return html ?? string.Empty;
        }

        return ScriptTagRegex().Replace(html, match =>
        {
            var attributes = match.Groups["attrs"].Value;
            var typeMatch = TypeAttributeRegex().Match(attributes);
            var type = typeMatch.Success ? typeMatch.Groups["type"].Value.Trim() : string.Empty;
            if (!JavaScriptTypes.Contains(type))
            {
                return match.Value;
            }

            var rest = typeMatch.Success ? attributes.Remove(typeMatch.Index, typeMatch.Length) : attributes;
            var original = type.Length > 0 ? $" data-type=\"{System.Net.WebUtility.HtmlEncode(type)}\"" : string.Empty;
            return $"<script type=\"text/plain\" data-consent=\"{category}\"{original}{rest}>";
        });
    }

    [GeneratedRegex(@"<script\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTagRegex();

    [GeneratedRegex(@"\s+type\s*=\s*(?:""(?<type>[^""]*)""|'(?<type>[^']*)'|(?<type>[^\s>]+))", RegexOptions.IgnoreCase)]
    private static partial Regex TypeAttributeRegex();
}

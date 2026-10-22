using System.Globalization;
using BlogEngine.Core;
using Microsoft.Extensions.Localization;

namespace BlogEngine.Web.Hosting;

/// <summary>
/// Traduce nella lingua corrente i messaggi del Core (chiavi di <see cref="Messages"/>, con eventuali
/// parametri dopo "|") usando Resources/Labels.*.resx. Un testo che non è una chiave nota resta com'è.
/// </summary>
public sealed class MessageTranslator(IStringLocalizer<Labels> labels)
{
    public string this[string? message] => Translate(message);

    public string Translate(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return string.Empty;
        }

        var parts = message.Split(Messages.ParameterSeparator);
        var text = labels[parts[0]];
        if (text.ResourceNotFound)
        {
            return message;
        }

        if (parts.Length == 1)
        {
            return text.Value;
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, text.Value, parts[1..]);
        }
        catch (FormatException)
        {
            return text.Value;
        }
    }

    /// <summary>Errori di validazione tradotti (campo → messaggio).</summary>
    public IReadOnlyDictionary<string, string> Translate(IReadOnlyDictionary<string, string> errors) =>
        errors.ToDictionary(e => e.Key, e => Translate(e.Value), StringComparer.OrdinalIgnoreCase);
}

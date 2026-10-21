using System.Globalization;
using System.Xml.Linq;

namespace BlogEngine.Data.Xml;

/// <summary>
/// Conversione dei valori in elementi XML e viceversa, sempre in cultura invariante.
/// Le date sono scritte in formato ISO 8601 e rilette come UTC.
/// </summary>
internal static class XmlValues
{
    public static XElement Element(string name, object? value) => new(name, Format(value));

    public static XElement List(string name, string itemName, IEnumerable<string> values) =>
        new(name, values.Select(v => new XElement(itemName, v)));

    public static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime date => DateTime.SpecifyKind(date, date.Kind == DateTimeKind.Unspecified ? DateTimeKind.Utc : date.Kind)
            .ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty,
    };

    public static string String(this XElement element, string name) => element.Element(name)?.Value ?? string.Empty;

    public static bool Bool(this XElement element, string name, bool defaultValue = false) =>
        bool.TryParse(element.Element(name)?.Value, out var value) ? value : defaultValue;

    public static int Int(this XElement element, string name) =>
        int.TryParse(element.Element(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public static float Float(this XElement element, string name) =>
        float.TryParse(element.Element(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public static Guid Guid(this XElement element, string name) =>
        System.Guid.TryParse(element.Element(name)?.Value, out var value) ? value : System.Guid.Empty;

    public static Guid? NullableGuid(this XElement element, string name) =>
        System.Guid.TryParse(element.Element(name)?.Value, out var value) ? value : null;

    public static DateTime Date(this XElement element, string name) => element.NullableDate(name) ?? default;

    public static DateTime? NullableDate(this XElement element, string name) =>
        DateTime.TryParse(element.Element(name)?.Value, CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value)
            ? value
            : null;

    public static Uri? Uri(this XElement element, string name) =>
        System.Uri.TryCreate(element.Element(name)?.Value, UriKind.Absolute, out var value) ? value : null;

    public static TEnum Enum<TEnum>(this XElement element, string name)
        where TEnum : struct, Enum =>
        System.Enum.TryParse<TEnum>(element.Element(name)?.Value, ignoreCase: true, out var value) ? value : default;

    public static List<string> Strings(this XElement element, string name, string itemName) =>
        element.Element(name)?.Elements(itemName).Select(e => e.Value).ToList() ?? [];
}

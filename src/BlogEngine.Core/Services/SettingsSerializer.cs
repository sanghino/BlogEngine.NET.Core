using System.Globalization;
using System.Reflection;
using BlogEngine.Settings;

namespace BlogEngine.Core.Services;

/// <summary>
/// Converte <see cref="BlogSettings"/> in coppie nome/valore e viceversa. I nomi sono quelli delle
/// proprietà in minuscolo (come nel vecchio settings.xml); i valori usano la cultura invariante.
/// </summary>
public static class SettingsSerializer
{
    private static readonly IReadOnlyDictionary<string, PropertyInfo> Properties =
        typeof(BlogSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .ToDictionary(p => p.Name.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, string> Serialize(BlogSettings settings) =>
        Properties.ToDictionary(
            p => p.Key,
            p => Convert.ToString(p.Value.GetValue(settings), CultureInfo.InvariantCulture) ?? string.Empty);

    /// <summary>
    /// Crea le impostazioni partendo dai valori predefiniti e applicando quelli memorizzati.
    /// Nomi sconosciuti e valori non convertibili vengono ignorati e riportati in <paramref name="invalid"/>.
    /// </summary>
    public static BlogSettings Deserialize(IReadOnlyDictionary<string, string> values, ICollection<string>? invalid = null)
    {
        var settings = new BlogSettings();
        foreach (var (name, value) in values)
        {
            if (!Properties.TryGetValue(name, out var property))
            {
                continue;
            }

            try
            {
                var type = property.PropertyType;
                object converted = type.IsEnum
                    ? Enum.Parse(type, value, ignoreCase: true)
                    : Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
                property.SetValue(settings, converted);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or ArgumentException or OverflowException)
            {
                invalid?.Add(name);
            }
        }

        return settings;
    }
}

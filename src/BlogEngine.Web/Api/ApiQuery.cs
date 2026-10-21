using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace BlogEngine.Web.Api;

/// <summary>
/// Filtri e ordinamenti passati dall'amministrazione nei parametri "filter" e "order". La versione originale
/// li eseguiva come LINQ dinamico; qui sono ammesse solo le forme che l'amministrazione usa davvero, sulle
/// proprietà pubbliche del DTO:
/// <list type="bullet">
/// <item>filtro: "1 == 1" oppure condizioni <c>Campo == "testo"</c>, <c>Campo == true</c> unite da <c>&amp;&amp;</c>;</item>
/// <item>ordine: <c>Campo</c>, <c>Campo asc</c>, <c>Campo desc</c> o <c>Campo descending</c>.</item>
/// </list>
/// Qualunque altra espressione viene rifiutata con <see cref="ArgumentException"/> (risposta 400).
/// </summary>
public static partial class ApiQuery
{
    public static IEnumerable<T> Apply<T>(IEnumerable<T> items, string? filter, string? order, int take, int skip)
    {
        var result = Filter(items, filter);
        result = Order(result, order);
        result = result.Skip(Math.Max(0, skip));
        return take > 0 ? result.Take(take) : result;
    }

    public static IEnumerable<T> Filter<T>(IEnumerable<T> items, string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter) || TautologyRegex().IsMatch(filter))
        {
            return items;
        }

        var conditions = filter.Split("&&").Select(ParseCondition<T>).ToList();
        return items.Where(item => conditions.All(c => c(item)));
    }

    public static IEnumerable<T> Order<T>(IEnumerable<T> items, string? order)
    {
        if (string.IsNullOrWhiteSpace(order))
        {
            return items;
        }

        var match = OrderRegex().Match(order.Trim());
        if (!match.Success)
        {
            throw new ArgumentException($"Ordinamento non ammesso: {order}");
        }

        var property = FindProperty<T>(match.Groups["field"].Value);
        var descending = match.Groups["dir"].Value.StartsWith("desc", StringComparison.OrdinalIgnoreCase);
        Func<T, object?> key = item => property.GetValue(item);
        var comparer = Comparer<object?>.Create(CompareValues);
        return descending ? items.OrderByDescending(key, comparer) : items.OrderBy(key, comparer);
    }

    private static Func<T, bool> ParseCondition<T>(string condition)
    {
        var match = ConditionRegex().Match(condition.Trim());
        if (!match.Success)
        {
            throw new ArgumentException($"Filtro non ammesso: {condition}");
        }

        var property = FindProperty<T>(match.Groups["field"].Value);
        if (match.Groups["text"].Success)
        {
            var expected = Regex.Unescape(match.Groups["text"].Value);
            return item => string.Equals(Convert.ToString(property.GetValue(item), CultureInfo.InvariantCulture), expected, StringComparison.Ordinal);
        }

        var flag = bool.Parse(match.Groups["bool"].Value);
        return item => property.GetValue(item) is bool value && value == flag;
    }

    private static PropertyInfo FindProperty<T>(string name) =>
        typeof(T).GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
        ?? throw new ArgumentException($"Campo sconosciuto: {name}");

    private static int CompareValues(object? a, object? b) => (a, b) switch
    {
        (null, null) => 0,
        (null, _) => -1,
        (_, null) => 1,
        (string x, string y) => string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase),
        (IComparable x, _) when a.GetType() == b.GetType() => x.CompareTo(b),
        _ => string.Compare(a.ToString(), b.ToString(), StringComparison.CurrentCultureIgnoreCase),
    };

    [GeneratedRegex(@"^\s*1\s*==\s*1\s*$")]
    private static partial Regex TautologyRegex();

    [GeneratedRegex("""^(?<field>[A-Za-z][A-Za-z0-9]*)\s*==\s*(?:"(?<text>(?:[^"\\]|\\.)*)"|(?<bool>true|false))$""", RegexOptions.IgnoreCase)]
    private static partial Regex ConditionRegex();

    [GeneratedRegex(@"^(?<field>[A-Za-z][A-Za-z0-9]*)(?:\s+(?<dir>asc|ascending|desc|descending))?$", RegexOptions.IgnoreCase)]
    private static partial Regex OrderRegex();
}

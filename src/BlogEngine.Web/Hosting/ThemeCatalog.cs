using System.Text.Json;
using BlogEngine.Core.Configuration;

namespace BlogEngine.Web.Hosting;

/// <summary>Tema disponibile: una cartella con l'eventuale manifest theme.json.</summary>
public sealed record ThemeInfo(string Id, string Title, string Description, string Version, string Authors, string Website, string Folder, bool IsInstalledByUpload)
{
    /// <summary>Widget da mostrare nelle zone che l'amministratore non ha ancora configurato.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DefaultWidgets { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Elenco dei temi: le cartelle di Custom/Themes (pubblicate o copiate via FTP) e di App_Data/themes
/// (caricate dall'amministrazione).
/// </summary>
public sealed class ThemeCatalog(BlogEnginePaths paths)
{
    public const string ManifestFileName = "theme.json";

    private (DateTime Expires, IReadOnlyList<ThemeInfo> Themes)? _cache;

    /// <summary>
    /// Temi presenti sul disco. L'elenco viene riletto ogni pochi secondi, così un tema copiato
    /// via FTP compare senza riavviare il sito.
    /// </summary>
    public IReadOnlyList<ThemeInfo> GetThemes()
    {
        if (_cache is { } cache && cache.Expires > DateTime.UtcNow)
        {
            return cache.Themes;
        }

        var themes = Load();
        _cache = (DateTime.UtcNow.AddSeconds(5), themes);
        return themes;
    }

    /// <summary>Rilegge subito l'elenco (dopo un'installazione o una rimozione).</summary>
    public void Refresh() => _cache = null;

    private IReadOnlyList<ThemeInfo> Load() =>
        Scan(paths.BundledThemes, uploaded: false)
            .Concat(Scan(paths.InstalledThemes, uploaded: true))
            .GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Tema con l'id indicato; se manca si ripiega su <see cref="FallbackTheme"/>.</summary>
    public ThemeInfo? FindOrFallback(string? id) =>
        (string.IsNullOrWhiteSpace(id) ? null : Find(id)) ?? Find(FallbackTheme) ?? GetThemes().FirstOrDefault();

    public const string FallbackTheme = "Standard";

    public ThemeInfo? Find(string id) => GetThemes().FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<ThemeInfo> Scan(string root, bool uploaded)
    {
        if (!Directory.Exists(root))
        {
            yield break;
        }

        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            var id = Path.GetFileName(folder);
            var manifest = ReadManifest(Path.Combine(folder, ManifestFileName));
            yield return new ThemeInfo(
                id,
                manifest?.Title ?? id,
                manifest?.Description ?? string.Empty,
                manifest?.Version ?? string.Empty,
                manifest?.Authors ?? string.Empty,
                manifest?.Website ?? string.Empty,
                folder,
                uploaded)
            {
                DefaultWidgets = (manifest?.DefaultWidgets ?? [])
                    .ToDictionary(z => z.Key, z => (IReadOnlyList<string>)z.Value, StringComparer.OrdinalIgnoreCase),
            };
        }
    }

    private static ThemeManifest? ReadManifest(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<ThemeManifest>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed class ThemeManifest
    {
        public string? Title { get; set; }

        public string? Description { get; set; }

        public string? Version { get; set; }

        public string? Authors { get; set; }

        public string? Website { get; set; }

        public Dictionary<string, List<string>>? DefaultWidgets { get; set; }
    }
}

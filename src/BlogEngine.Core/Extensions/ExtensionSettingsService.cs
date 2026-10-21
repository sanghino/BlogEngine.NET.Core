using System.Text.Json;
using BlogEngine.Core.Data;
using BlogEngine.Core.Services;
using BlogEngine.Extensions;
using BlogEngine.Services;

namespace BlogEngine.Core.Extensions;

/// <summary>
/// Impostazioni delle estensioni, salvate per blog nell'archivio delle estensioni. Un blog che non le
/// ha mai modificate usa quelle del blog principale e, in mancanza anche di queste, i valori iniziali.
/// </summary>
public sealed class ExtensionSettingsService(
    ExtensionRegistry registry,
    ExtensionStates states,
    IExtensionDataStore store,
    IBlogContext context,
    BlogsCache blogs) : IExtensionSettingsService
{
    public async Task<ExtensionSettingsValues> GetAsync(string extension, string? settingsName = null, CancellationToken cancellationToken = default)
    {
        var name = settingsName ?? extension;
        var saved = await LoadAsync(context.Blog.Id, extension, cancellationToken).ConfigureAwait(false);
        if (!saved.ContainsKey(name) && !context.Blog.IsPrimary
            && (await blogs.GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(b => b.IsPrimary) is { } primary)
        {
            saved = await LoadAsync(primary.Id, extension, cancellationToken).ConfigureAwait(false);
        }

        if (saved.TryGetValue(name, out var rows))
        {
            return new ExtensionSettingsValues { Name = name, Rows = rows };
        }

        var definition = registry.Find(extension)?.Settings.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        return new ExtensionSettingsValues
        {
            Name = name,
            Rows = definition?.Defaults.Select(r => new Dictionary<string, string>(r, StringComparer.OrdinalIgnoreCase)).ToList() ?? [],
        };
    }

    public async Task SaveAsync(string extension, ExtensionSettingsValues values, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(values);
        var saved = await LoadAsync(context.Blog.Id, extension, cancellationToken).ConfigureAwait(false);
        saved[values.Name] = values.Rows;
        await store.SaveAsync(context.Blog.Id, ExtensionDataKind.Extension, extension, JsonSerializer.Serialize(saved), cancellationToken).ConfigureAwait(false);
    }

    public bool IsEnabled(string extension) => states.IsEnabled(extension);

    private async Task<Dictionary<string, List<Dictionary<string, string>>>> LoadAsync(Guid blogId, string extension, CancellationToken cancellationToken)
    {
        var json = await store.LoadAsync(blogId, ExtensionDataKind.Extension, extension, cancellationToken).ConfigureAwait(false);
        Dictionary<string, List<Dictionary<string, string>>>? data = null;
        try
        {
            data = json is null ? null : JsonSerializer.Deserialize<Dictionary<string, List<Dictionary<string, string>>>>(json);
        }
        catch (JsonException)
        {
            // Dati illeggibili: si riparte dai valori iniziali.
        }

        return new Dictionary<string, List<Dictionary<string, string>>>(
            (data ?? []).Select(p => KeyValuePair.Create(p.Key, p.Value.Select(r => new Dictionary<string, string>(r, StringComparer.OrdinalIgnoreCase)).ToList())),
            StringComparer.OrdinalIgnoreCase);
    }
}

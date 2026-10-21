using System.Text.Json;
using BlogEngine.Core.Data;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

/// <summary>
/// Indirizzi iscritti alla newsletter del widget Newsletter (porting di newsletter.xml). L'invio
/// dei messaggi alla pubblicazione di un post è compito dell'estensione SendNewsletters.
/// </summary>
public sealed class NewsletterSubscribers(IExtensionDataStore store, IBlogContext context)
{
    private const string Key = "newsletter";
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public async Task<IReadOnlyList<string>> GetAsync(CancellationToken cancellationToken = default)
    {
        var json = await store.LoadAsync(context.Blog.Id, ExtensionDataKind.Widget, Key, cancellationToken);
        try
        {
            return json is null ? [] : JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public Task AddAsync(string email, CancellationToken cancellationToken = default) =>
        UpdateAsync(list => list.Contains(email, StringComparer.OrdinalIgnoreCase) ? list : [.. list, email.Trim()], cancellationToken);

    public Task RemoveAsync(string email, CancellationToken cancellationToken = default) =>
        UpdateAsync(list => list.Where(e => !string.Equals(e, email, StringComparison.OrdinalIgnoreCase)).ToList(), cancellationToken);

    private async Task UpdateAsync(Func<List<string>, List<string>> change, CancellationToken cancellationToken)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            var list = change((await GetAsync(cancellationToken)).ToList()).OrderBy(e => e, StringComparer.OrdinalIgnoreCase).ToList();
            await store.SaveAsync(context.Blog.Id, ExtensionDataKind.Widget, Key, JsonSerializer.Serialize(list), cancellationToken);
        }
        finally
        {
            Lock.Release();
        }
    }
}

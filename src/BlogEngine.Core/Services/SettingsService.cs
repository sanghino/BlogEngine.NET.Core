using System.Collections.Concurrent;
using BlogEngine.Core.Caching;
using BlogEngine.Core.Data;
using BlogEngine.Events;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Settings;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Core.Services;

/// <summary>Cache condivisa delle impostazioni di ogni blog.</summary>
public sealed class SettingsCache(ISettingsStore store, ILogger<SettingsCache> logger) : IBlogScopedCache
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<BlogSettings>>> _settings = new();

    public async Task<BlogSettings> GetAsync(Guid blogId, CancellationToken cancellationToken)
    {
        var entry = _settings.GetOrAdd(blogId, id => new Lazy<Task<BlogSettings>>(() => LoadAsync(id)));
        try
        {
            return await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (entry.Value.IsFaulted)
        {
            _settings.TryRemove(new KeyValuePair<Guid, Lazy<Task<BlogSettings>>>(blogId, entry));
            throw;
        }
    }

    public async Task SaveAsync(Guid blogId, BlogSettings settings, CancellationToken cancellationToken)
    {
        var copy = settings.Clone();
        await store.SaveAsync(blogId, SettingsSerializer.Serialize(copy), cancellationToken).ConfigureAwait(false);
        _settings[blogId] = new Lazy<Task<BlogSettings>>(() => Task.FromResult(copy));
    }

    public void Invalidate(Guid blogId) => _settings.TryRemove(blogId, out _);

    private async Task<BlogSettings> LoadAsync(Guid blogId)
    {
        var invalid = new List<string>();
        var settings = SettingsSerializer.Deserialize(await store.LoadAsync(blogId).ConfigureAwait(false), invalid);
        if (invalid.Count > 0)
        {
            logger.LogWarning("Impostazioni non valide ignorate nel blog {BlogId}: {Names}", blogId, string.Join(", ", invalid));
        }

        return settings;
    }
}

public sealed class SettingsService(
    SettingsCache cache,
    IBlogContext context,
    IRightsService rights,
    IEventPublisher events) : ISettingsService
{
    public async Task<BlogSettings> GetAsync(Guid? blogId = null, CancellationToken cancellationToken = default) =>
        (await cache.GetAsync(blogId ?? context.Blog.Id, cancellationToken).ConfigureAwait(false)).Clone();

    public async Task SaveAsync(BlogSettings settings, Guid? blogId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!await rights.IsAuthorizedAsync(Rights.AccessAdminSettingsPages, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }

        var id = blogId ?? context.Blog.Id;
        await cache.SaveAsync(id, settings, cancellationToken).ConfigureAwait(false);
        await events.PublishAsync(new SettingsChangedEvent { BlogId = id, Settings = settings.Clone() }, cancellationToken).ConfigureAwait(false);
    }
}

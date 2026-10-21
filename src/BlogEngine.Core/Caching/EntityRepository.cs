using BlogEngine.Core.Data;
using BlogEngine.Models;

namespace BlogEngine.Core.Caching;

/// <summary>
/// Accesso in memoria alle entità di un blog con scrittura sullo store sottostante.
/// Registrato come singleton: la cache è condivisa tra tutte le richieste.
/// </summary>
public sealed class EntityRepository<T> : IBlogScopedCache
    where T : class, IBlogItem
{
    private readonly IEntityStore<T> _store;
    private readonly BlogDataCache<T> _cache;

    public EntityRepository(IEntityStore<T> store)
    {
        _store = store;
        _cache = new BlogDataCache<T>(blogId => store.GetAllAsync(blogId));
    }

    /// <summary>Tutti gli elementi del blog, compresi quelli nel cestino.</summary>
    public Task<IReadOnlyList<T>> GetAllAsync(Guid blogId, CancellationToken cancellationToken = default) =>
        _cache.GetAsync(blogId, cancellationToken);

    public async Task<T?> FindAsync(Guid blogId, Guid id, CancellationToken cancellationToken = default) =>
        (await GetAllAsync(blogId, cancellationToken).ConfigureAwait(false)).FirstOrDefault(e => e.Id == id);

    public Task SaveAsync(T entity, CancellationToken cancellationToken = default) =>
        _cache.WriteAsync(entity.BlogId, async items =>
        {
            // Una volta acquisito il lock la scrittura non viene più interrotta, per non
            // lasciare cache e store disallineati.
            await _store.SaveAsync(entity, CancellationToken.None).ConfigureAwait(false);
            return BlogDataCache<T>.Upsert(items, entity, e => e.Id == entity.Id);
        }, cancellationToken);

    public Task DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken = default) =>
        _cache.WriteAsync(blogId, async items =>
        {
            await _store.DeleteAsync(blogId, id, CancellationToken.None).ConfigureAwait(false);
            return BlogDataCache<T>.Remove(items, e => e.Id == id);
        }, cancellationToken);

    public void Invalidate(Guid blogId) => _cache.Invalidate(blogId);
}

using System.Collections.Concurrent;

namespace BlogEngine.Core.Caching;

/// <summary>Cache che contiene dati di un singolo blog e va svuotata quando il blog viene eliminato.</summary>
public interface IBlogScopedCache
{
    void Invalidate(Guid blogId);
}

/// <summary>
/// Cache in memoria, per blog, di un insieme di elementi letto da uno store. Il primo accesso carica
/// tutti gli elementi del blog; le scritture sono serializzate per blog e sostituiscono l'insieme in cache
/// con una nuova copia, così chi sta leggendo non vede mai una lista modificata a metà.
/// </summary>
public sealed class BlogDataCache<T>(Func<Guid, Task<IReadOnlyList<T>>> load) : IBlogScopedCache
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<IReadOnlyList<T>>>> _snapshots = new();
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _writeLocks = new();

    public async Task<IReadOnlyList<T>> GetAsync(Guid blogId, CancellationToken cancellationToken = default)
    {
        var entry = _snapshots.GetOrAdd(blogId, id => new Lazy<Task<IReadOnlyList<T>>>(() => load(id)));
        try
        {
            return await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (entry.Value.IsFaulted || entry.Value.IsCanceled)
        {
            // Un caricamento fallito non deve restare in cache: il prossimo accesso riprova.
            _snapshots.TryRemove(new KeyValuePair<Guid, Lazy<Task<IReadOnlyList<T>>>>(blogId, entry));
            throw;
        }
    }

    /// <summary>
    /// Esegue una scrittura in modo esclusivo per il blog. <paramref name="write"/> riceve l'insieme corrente,
    /// salva sullo store e restituisce il nuovo insieme da mettere in cache.
    /// </summary>
    public async Task WriteAsync(
        Guid blogId,
        Func<IReadOnlyList<T>, Task<IReadOnlyList<T>>> write,
        CancellationToken cancellationToken = default)
    {
        var gate = _writeLocks.GetOrAdd(blogId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await GetAsync(blogId, cancellationToken).ConfigureAwait(false);
            var updated = await write(current).ConfigureAwait(false);
            _snapshots[blogId] = new Lazy<Task<IReadOnlyList<T>>>(() => Task.FromResult(updated));
        }
        finally
        {
            gate.Release();
        }
    }

    public void Invalidate(Guid blogId) => _snapshots.TryRemove(blogId, out _);

    /// <summary>Copia della lista con l'elemento aggiunto o sostituito (stessa chiave).</summary>
    public static IReadOnlyList<T> Upsert(IReadOnlyList<T> items, T item, Func<T, bool> sameKey)
    {
        var copy = new List<T>(items.Count + 1);
        var replaced = false;
        foreach (var existing in items)
        {
            if (!replaced && sameKey(existing))
            {
                copy.Add(item);
                replaced = true;
            }
            else
            {
                copy.Add(existing);
            }
        }

        if (!replaced)
        {
            copy.Add(item);
        }

        return copy;
    }

    /// <summary>Copia della lista senza gli elementi indicati.</summary>
    public static IReadOnlyList<T> Remove(IReadOnlyList<T> items, Func<T, bool> predicate) =>
        items.Where(i => !predicate(i)).ToList();
}

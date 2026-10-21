using BlogEngine.Models;

namespace BlogEngine.Services;

/// <summary>Gestione delle istanze del multi-blog.</summary>
public interface IBlogService
{
    /// <summary>Tutti i blog non eliminati.</summary>
    Task<IReadOnlyList<Blog>> GetBlogsAsync(CancellationToken cancellationToken = default);

    Task<Blog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Blog?> GetPrimaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Determina il blog a cui è destinata una richiesta, in base all'id esplicito (intestazione
    /// "x-blog-instance" delle chiamate dell'amministrazione), al nome host e al percorso.
    /// Se nessun blog corrisponde restituisce il blog principale.
    /// </summary>
    Task<Blog?> ResolveAsync(string host, string path, Guid? explicitBlogId = null, CancellationToken cancellationToken = default);

    Task SaveAsync(Blog blog, CancellationToken cancellationToken = default);

    /// <summary>Elimina un blog con tutti i suoi dati. Il blog principale non può essere eliminato.</summary>
    Task DeleteAsync(Blog blog, CancellationToken cancellationToken = default);
}

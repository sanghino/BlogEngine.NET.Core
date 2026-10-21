using BlogEngine.Models;

namespace BlogEngine.Services;

/// <summary>
/// Operazioni sui commenti. Ogni operazione genera un <c>CommentChangingEvent</c> (annullabile)
/// e un <c>CommentChangedEvent</c>.
/// </summary>
public interface ICommentService
{
    /// <summary>
    /// Aggiunge un commento. I filtri antispam intervengono sull'evento di aggiunta.
    /// Restituisce false se un gestore ha annullato l'operazione.
    /// </summary>
    Task<bool> AddAsync(Post post, Comment comment, CancellationToken cancellationToken = default);

    Task<bool> UpdateAsync(Post post, Comment comment, CancellationToken cancellationToken = default);

    Task<bool> ApproveAsync(Post post, Comment comment, CancellationToken cancellationToken = default);

    Task ApproveAllAsync(Post post, CancellationToken cancellationToken = default);

    /// <summary>Segna il commento come spam.</summary>
    Task<bool> DisapproveAsync(Post post, Comment comment, CancellationToken cancellationToken = default);

    /// <summary>Sposta il commento nel cestino.</summary>
    Task<bool> RemoveAsync(Post post, Comment comment, CancellationToken cancellationToken = default);

    Task<bool> RestoreAsync(Post post, Comment comment, CancellationToken cancellationToken = default);

    /// <summary>Elimina definitivamente il commento.</summary>
    Task<bool> PurgeAsync(Post post, Comment comment, CancellationToken cancellationToken = default);

    /// <summary>Indica se il post accetta ancora nuovi commenti.</summary>
    Task<bool> AreCommentsOpenAsync(Post post, CancellationToken cancellationToken = default);

    /// <summary>
    /// Commenti visibili all'utente corrente organizzati ad albero (primo livello e risposte),
    /// in ordine cronologico. Con l'annidamento disattivato tutti i commenti sono di primo livello.
    /// </summary>
    Task<IReadOnlyList<CommentNode>> GetCommentTreeAsync(Post post, CancellationToken cancellationToken = default);
}

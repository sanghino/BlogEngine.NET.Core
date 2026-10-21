using BlogEngine.Models;

namespace BlogEngine.Services;

/// <summary>
/// Lettura e modifica dei post del blog corrente. Gli elenchi sono ordinati dal più recente;
/// se il blog corrente aggrega gli altri (site aggregation) includono i post di tutti i blog attivi.
/// Le ricerche per autore, categoria, tag e data restituiscono solo i post visibili all'utente corrente.
/// </summary>
/// <remarks>
/// Gli oggetti restituiti sono condivisi tra le richieste: per modificarli conviene crearne una copia,
/// o comunque passarli subito a <see cref="SaveAsync"/>.
/// </remarks>
public interface IPostService
{
    /// <summary>Tutti i post non eliminati, pubblicati o meno.</summary>
    Task<IReadOnlyList<Post>> GetPostsAsync(CancellationToken cancellationToken = default);

    /// <summary>I post che l'utente corrente può vedere.</summary>
    Task<IReadOnlyList<Post>> GetVisiblePostsAsync(CancellationToken cancellationToken = default);

    /// <summary>I post nel cestino del blog corrente.</summary>
    Task<IReadOnlyList<Post>> GetDeletedPostsAsync(CancellationToken cancellationToken = default);

    /// <summary>Post con l'id indicato, anche se non visibile o nel cestino (per l'amministrazione).</summary>
    Task<Post?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Post del blog corrente con lo slug indicato, pubblicato o meno (la visibilità va verificata a parte).</summary>
    Task<Post?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Post>> GetByAuthorAsync(string author, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Post>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Post>> GetByTagAsync(string tag, CancellationToken cancellationToken = default);

    /// <summary>Post creati tra le due date (estremi inclusi, confronto sul giorno).</summary>
    Task<IReadOnlyList<Post>> GetByDateAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default);

    /// <summary>Post precedente (più vecchio) e successivo (più recente) visibili, nello stesso blog.</summary>
    Task<(Post? Previous, Post? Next)> GetAdjacentAsync(Post post, CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserisce o aggiorna il post. Valida i dati, verifica i permessi, rende lo slug univoco
    /// e genera gli eventi di salvataggio e pubblicazione.
    /// </summary>
    Task SaveAsync(Post post, CancellationToken cancellationToken = default);

    /// <summary>Sposta il post nel cestino.</summary>
    Task DeleteAsync(Post post, CancellationToken cancellationToken = default);

    /// <summary>Ripristina un post dal cestino.</summary>
    Task RestoreAsync(Post post, CancellationToken cancellationToken = default);

    /// <summary>Elimina definitivamente il post.</summary>
    Task PurgeAsync(Post post, CancellationToken cancellationToken = default);

    /// <summary>Registra il voto di un visitatore (1-5).</summary>
    Task RateAsync(Post post, int rating, CancellationToken cancellationToken = default);

    Task<bool> IsVisibleAsync(Post post, CancellationToken cancellationToken = default);

    Task<bool> CanEditAsync(Post post, CancellationToken cancellationToken = default);

    Task<bool> CanDeleteAsync(Post post, CancellationToken cancellationToken = default);

    /// <summary>Indica se l'utente corrente può pubblicare un post dell'autore indicato.</summary>
    Task<bool> CanPublishAsync(string author, CancellationToken cancellationToken = default);
}

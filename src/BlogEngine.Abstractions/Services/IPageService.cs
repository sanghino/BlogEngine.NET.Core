using BlogEngine.Models;

namespace BlogEngine.Services;

/// <summary>Lettura e modifica delle pagine del blog corrente.</summary>
public interface IPageService
{
    /// <summary>Tutte le pagine non eliminate, ordinate per SortOrder e titolo.</summary>
    Task<IReadOnlyList<Page>> GetPagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Le pagine che l'utente corrente può vedere.</summary>
    Task<IReadOnlyList<Page>> GetVisiblePagesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Page>> GetDeletedPagesAsync(CancellationToken cancellationToken = default);

    Task<Page?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Page?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    Task<Page?> GetFrontPageAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Page>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default);

    Task SaveAsync(Page page, CancellationToken cancellationToken = default);

    Task DeleteAsync(Page page, CancellationToken cancellationToken = default);

    Task RestoreAsync(Page page, CancellationToken cancellationToken = default);

    Task PurgeAsync(Page page, CancellationToken cancellationToken = default);

    Task<bool> IsVisibleAsync(Page page, CancellationToken cancellationToken = default);

    Task<bool> CanEditAsync(CancellationToken cancellationToken = default);
}

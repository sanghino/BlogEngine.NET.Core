using BlogEngine.Models;

namespace BlogEngine.Services;

/// <summary>Blogroll del blog corrente.</summary>
public interface IBlogRollService
{
    /// <summary>Voci ordinate per SortIndex.</summary>
    Task<IReadOnlyList<BlogRollItem>> GetItemsAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(BlogRollItem item, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

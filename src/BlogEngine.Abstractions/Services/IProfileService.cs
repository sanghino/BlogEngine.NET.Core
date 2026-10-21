using BlogEngine.Models;

namespace BlogEngine.Services;

/// <summary>Profili pubblici degli autori del blog corrente.</summary>
public interface IProfileService
{
    Task<IReadOnlyList<AuthorProfile>> GetProfilesAsync(CancellationToken cancellationToken = default);

    Task<AuthorProfile?> GetAsync(string userName, CancellationToken cancellationToken = default);

    Task<AuthorProfile?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task SaveAsync(AuthorProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(string userName, CancellationToken cancellationToken = default);
}

using BlogEngine.Core.Caching;
using BlogEngine.Core.Data;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

/// <summary>Cache condivisa dei profili autore.</summary>
public sealed class ProfileRepository(IProfileStore store) : IBlogScopedCache
{
    private readonly BlogDataCache<AuthorProfile> _cache = new(blogId => store.GetAllAsync(blogId));

    public Task<IReadOnlyList<AuthorProfile>> GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        _cache.GetAsync(blogId, cancellationToken);

    public Task SaveAsync(AuthorProfile profile, CancellationToken cancellationToken) =>
        _cache.WriteAsync(profile.BlogId, async items =>
        {
            await store.SaveAsync(profile, CancellationToken.None).ConfigureAwait(false);
            return BlogDataCache<AuthorProfile>.Upsert(items, profile, p => SameUser(p.UserName, profile.UserName));
        }, cancellationToken);

    public Task DeleteAsync(Guid blogId, string userName, CancellationToken cancellationToken) =>
        _cache.WriteAsync(blogId, async items =>
        {
            await store.DeleteAsync(blogId, userName, CancellationToken.None).ConfigureAwait(false);
            return BlogDataCache<AuthorProfile>.Remove(items, p => SameUser(p.UserName, userName));
        }, cancellationToken);

    public void Invalidate(Guid blogId) => _cache.Invalidate(blogId);

    public static bool SameUser(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

public sealed class ProfileService(ProfileRepository profiles, IBlogContext context, IRightsService rights) : IProfileService
{
    public async Task<IReadOnlyList<AuthorProfile>> GetProfilesAsync(CancellationToken cancellationToken = default) =>
        (await profiles.GetAllAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false))
            .OrderBy(p => p.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<AuthorProfile?> GetAsync(string userName, CancellationToken cancellationToken = default) =>
        (await profiles.GetAllAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(p => ProfileRepository.SameUser(p.UserName, userName));

    public async Task<AuthorProfile?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        (await profiles.GetAllAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(p => string.Equals(p.EmailAddress, email, StringComparison.OrdinalIgnoreCase));

    public async Task SaveAsync(AuthorProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        BlogValidationException.ThrowIfInvalid(("UserName", string.IsNullOrWhiteSpace(profile.UserName), "Il nome utente è obbligatorio."));
        await DemandForUserAsync(profile.UserName, Rights.EditOwnUser, Rights.EditOtherUsers, cancellationToken).ConfigureAwait(false);

        if (profile.BlogId == Guid.Empty)
        {
            profile.BlogId = context.Blog.Id;
        }

        await profiles.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string userName, CancellationToken cancellationToken = default)
    {
        await DemandForUserAsync(userName, Rights.DeleteUserSelf, Rights.DeleteUsersOtherThanSelf, cancellationToken).ConfigureAwait(false);
        await profiles.DeleteAsync(context.Blog.Id, userName, cancellationToken).ConfigureAwait(false);
    }

    private async Task DemandForUserAsync(string userName, Rights own, Rights others, CancellationToken cancellationToken)
    {
        var isOwn = context.UserName is { } current && ProfileRepository.SameUser(current, userName);
        if (!await rights.IsAuthorizedAsync(isOwn ? own : others, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

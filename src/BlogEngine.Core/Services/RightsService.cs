using System.Collections.Concurrent;
using BlogEngine.Core.Caching;
using BlogEngine.Core.Data;
using BlogEngine.Security;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

/// <summary>Cache condivisa della matrice permesso → ruoli di ogni blog.</summary>
public sealed class RightsCache(IRightsStore store) : IBlogScopedCache
{
    private readonly ConcurrentDictionary<Guid, Lazy<Task<RightsMatrix>>> _matrices = new();

    public async Task<RightsMatrix> GetAsync(Guid blogId, CancellationToken cancellationToken)
    {
        var entry = _matrices.GetOrAdd(blogId, id => new Lazy<Task<RightsMatrix>>(() => LoadAsync(id)));
        try
        {
            return await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (entry.Value.IsFaulted)
        {
            _matrices.TryRemove(new KeyValuePair<Guid, Lazy<Task<RightsMatrix>>>(blogId, entry));
            throw;
        }
    }

    public async Task SaveAsync(Guid blogId, RightsMatrix matrix, CancellationToken cancellationToken)
    {
        await store.SaveAsync(blogId, matrix.ToStoreFormat(), cancellationToken).ConfigureAwait(false);
        _matrices[blogId] = new Lazy<Task<RightsMatrix>>(() => Task.FromResult(matrix));
    }

    public void Invalidate(Guid blogId) => _matrices.TryRemove(blogId, out _);

    private async Task<RightsMatrix> LoadAsync(Guid blogId)
    {
        var stored = await store.LoadAsync(blogId).ConfigureAwait(false);
        return stored.Count == 0 ? RightsMatrix.CreateDefault() : RightsMatrix.FromStoreFormat(stored);
    }
}

/// <summary>
/// Matrice immutabile permesso → ruoli. Gli amministratori hanno sempre tutti i permessi.
/// </summary>
public sealed class RightsMatrix
{
    public static readonly IReadOnlyList<Rights> AllRights = Enum.GetValues<Rights>().Where(r => r != Rights.None).ToArray();

    private static readonly IReadOnlySet<Rights> AnonymousDefaults = new HashSet<Rights>
    {
        Rights.ViewPublicComments, Rights.CreateComments, Rights.ViewPublicPosts, Rights.ViewPublicPages,
        Rights.ViewRatingsOnPosts, Rights.SubmitRatingsOnPosts,
    };

    private static readonly IReadOnlySet<Rights> EditorDefaults = new HashSet<Rights>(AnonymousDefaults)
    {
        Rights.AccessAdminPages, Rights.ViewUnmoderatedComments, Rights.ModerateComments, Rights.ViewUnpublishedPosts,
        Rights.CreateNewPosts, Rights.EditOwnPosts, Rights.DeleteOwnPosts, Rights.PublishOwnPosts,
        Rights.ViewUnpublishedPages, Rights.CreateNewPages, Rights.EditOwnPages, Rights.EditOwnUser,
    };

    private readonly IReadOnlyDictionary<Rights, IReadOnlySet<string>> _roles;

    private RightsMatrix(IDictionary<Rights, HashSet<string>> roles)
    {
        foreach (var right in AllRights)
        {
            if (!roles.TryGetValue(right, out var set))
            {
                roles[right] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            set.Add(SystemRoles.Administrators);
        }

        _roles = roles.ToDictionary(p => p.Key, p => (IReadOnlySet<string>)p.Value);
    }

    public IReadOnlyDictionary<Rights, IReadOnlySet<string>> Roles => _roles;

    public static IReadOnlySet<Rights> GetDefaultRights(string role) =>
        string.Equals(role, SystemRoles.Editors, StringComparison.OrdinalIgnoreCase) ? EditorDefaults
        : string.Equals(role, SystemRoles.Anonymous, StringComparison.OrdinalIgnoreCase) ? AnonymousDefaults
        : string.Equals(role, SystemRoles.Administrators, StringComparison.OrdinalIgnoreCase) ? AllRights.ToHashSet()
        : new HashSet<Rights>();

    /// <summary>Permessi di una nuova installazione.</summary>
    public static RightsMatrix CreateDefault()
    {
        var roles = AllRights.ToDictionary(r => r, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        foreach (var right in EditorDefaults)
        {
            roles[right].Add(SystemRoles.Editors);
        }

        foreach (var right in AnonymousDefaults)
        {
            roles[right].Add(SystemRoles.Anonymous);
        }

        return new RightsMatrix(roles);
    }

    /// <summary>Legge il formato dello store; i nomi di permesso sconosciuti vengono ignorati.</summary>
    public static RightsMatrix FromStoreFormat(IReadOnlyDictionary<string, IReadOnlyCollection<string>> stored)
    {
        var roles = new Dictionary<Rights, HashSet<string>>();
        foreach (var (name, roleNames) in stored)
        {
            if (Enum.TryParse<Rights>(name, ignoreCase: true, out var right) && right != Rights.None)
            {
                roles[right] = new HashSet<string>(roleNames.Where(r => !string.IsNullOrWhiteSpace(r)), StringComparer.OrdinalIgnoreCase);
            }
        }

        return new RightsMatrix(roles);
    }

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> ToStoreFormat() =>
        _roles.ToDictionary(p => p.Key.ToString(), p => (IReadOnlyCollection<string>)p.Value.Order(StringComparer.OrdinalIgnoreCase).ToArray());

    public bool HasRight(Rights right, IEnumerable<string> roles)
    {
        if (right == Rights.None)
        {
            return false;
        }

        var allowed = _roles[right];
        return roles.Any(allowed.Contains);
    }

    public IReadOnlySet<Rights> GetRightsForRole(string role) =>
        _roles.Where(p => p.Value.Contains(role)).Select(p => p.Key).ToHashSet();

    /// <summary>Nuova matrice in cui il ruolo ha esattamente i permessi indicati.</summary>
    public RightsMatrix WithRole(string role, IEnumerable<Rights> rights)
    {
        var granted = rights.ToHashSet();
        var roles = _roles.ToDictionary(p => p.Key, p => new HashSet<string>(p.Value, StringComparer.OrdinalIgnoreCase));
        foreach (var (right, set) in roles)
        {
            if (granted.Contains(right))
            {
                set.Add(role);
            }
            else
            {
                set.Remove(role);
            }
        }

        return new RightsMatrix(roles);
    }

    /// <summary>Nuova matrice senza il ruolo (quando un ruolo viene eliminato).</summary>
    public RightsMatrix WithoutRole(string role) => WithRole(role, []);
}

public sealed class RightsService(RightsCache cache, IBlogContext context) : IRightsService
{
    public bool IsAdministrator => context.Roles.Contains(SystemRoles.Administrators, StringComparer.OrdinalIgnoreCase);

    public Task<bool> IsAuthorizedAsync(Rights right, CancellationToken cancellationToken = default) =>
        HasRightAsync(right, context.Roles, cancellationToken);

    public async Task<bool> IsAuthorizedAsync(AuthorizationCheck check, IEnumerable<Rights> rights, CancellationToken cancellationToken = default)
    {
        var list = rights.ToList();
        if (list.Count == 0)
        {
            // Una verifica su un elenco vuoto è quasi sempre un errore: meglio negare.
            return false;
        }

        var matrix = await cache.GetAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false);
        return check switch
        {
            AuthorizationCheck.HasAny => list.Any(r => matrix.HasRight(r, context.Roles)),
            AuthorizationCheck.HasAll => list.All(r => matrix.HasRight(r, context.Roles)),
            _ => throw new ArgumentOutOfRangeException(nameof(check)),
        };
    }

    public async Task<bool> HasRightAsync(Rights right, IEnumerable<string> roles, CancellationToken cancellationToken = default) =>
        (await cache.GetAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false)).HasRight(right, roles);

    public async Task<IReadOnlySet<Rights>> GetRightsForRoleAsync(string role, CancellationToken cancellationToken = default) =>
        (await cache.GetAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false)).GetRightsForRole(role);

    public async Task<IReadOnlyDictionary<Rights, IReadOnlySet<string>>> GetRoleMatrixAsync(CancellationToken cancellationToken = default) =>
        (await cache.GetAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false)).Roles;

    public async Task SetRightsForRoleAsync(string role, IEnumerable<Rights> rights, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        if (!await IsAuthorizedAsync(Rights.EditRoles, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }

        if (string.Equals(role, SystemRoles.Administrators, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Messages.AdministratorRightsReadOnly);
        }

        var matrix = await cache.GetAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false);
        await cache.SaveAsync(context.Blog.Id, matrix.WithRole(role, rights), cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlySet<Rights> GetDefaultRights(string role) => RightsMatrix.GetDefaultRights(role);
}

using BlogEngine.Security;

namespace BlogEngine.Services;

/// <summary>
/// Permessi dei ruoli nel blog corrente e verifiche sull'utente corrente.
/// Sostituisce Security.IsAuthorizedTo e la classe Right.
/// </summary>
public interface IRightsService
{
    /// <summary>Indica se l'utente corrente ha il permesso.</summary>
    Task<bool> IsAuthorizedAsync(Rights right, CancellationToken cancellationToken = default);

    Task<bool> IsAuthorizedAsync(AuthorizationCheck check, IEnumerable<Rights> rights, CancellationToken cancellationToken = default);

    /// <summary>Indica se almeno uno dei ruoli indicati ha il permesso.</summary>
    Task<bool> HasRightAsync(Rights right, IEnumerable<string> roles, CancellationToken cancellationToken = default);

    /// <summary>Permessi assegnati a un ruolo.</summary>
    Task<IReadOnlySet<Rights>> GetRightsForRoleAsync(string role, CancellationToken cancellationToken = default);

    /// <summary>Ruoli che possiedono ciascun permesso.</summary>
    Task<IReadOnlyDictionary<Rights, IReadOnlySet<string>>> GetRoleMatrixAsync(CancellationToken cancellationToken = default);

    /// <summary>Sostituisce i permessi di un ruolo. I permessi degli amministratori non sono modificabili.</summary>
    Task SetRightsForRoleAsync(string role, IEnumerable<Rights> rights, CancellationToken cancellationToken = default);

    /// <summary>Permessi predefiniti di un ruolo di sistema (vuoto per gli altri ruoli).</summary>
    IReadOnlySet<Rights> GetDefaultRights(string role);

    bool IsAdministrator { get; }
}

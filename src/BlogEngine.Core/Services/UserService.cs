using System.Net.Mail;
using BlogEngine.Core.Caching;
using BlogEngine.Core.Data;
using BlogEngine.Core.Security;
using BlogEngine.Security;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

/// <summary>
/// Account e ruoli del blog corrente. Sostituisce Membership e Roles di ASP.NET.
/// Resta fuori dall'SDK pubblico perché gestisce le password.
/// </summary>
public interface IUserService
{
    Task<IReadOnlyList<BlogUser>> GetUsersAsync(CancellationToken cancellationToken = default);

    Task<BlogUser?> GetUserAsync(string userName, CancellationToken cancellationToken = default);

    Task<BlogUser?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>Restituisce l'utente se nome e password sono corretti e ne registra l'accesso.</summary>
    Task<BlogUser?> ValidateCredentialsAsync(string userName, string password, CancellationToken cancellationToken = default);

    /// <summary>Crea un account (richiede il permesso CreateNewUsers).</summary>
    Task<BlogUser> CreateUserAsync(string userName, string email, string password, IEnumerable<string>? roles = null, CancellationToken cancellationToken = default);

    /// <summary>Registrazione autonoma di un visitatore, se abilitata nelle impostazioni, con il ruolo iniziale configurato.</summary>
    Task<BlogUser> RegisterAsync(string userName, string email, string password, CancellationToken cancellationToken = default);

    Task UpdateEmailAsync(string userName, string email, CancellationToken cancellationToken = default);

    /// <summary>Cambio password dell'utente corrente, che deve indicare quella attuale.</summary>
    Task<bool> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default);

    /// <summary>Imposta una nuova password senza conoscere quella attuale (amministrazione o reimpostazione via email).</summary>
    Task SetPasswordAsync(string userName, string newPassword, bool bypassRights = false, CancellationToken cancellationToken = default);

    /// <summary>Elimina l'account. L'ultimo amministratore non può essere eliminato.</summary>
    Task DeleteUserAsync(string userName, CancellationToken cancellationToken = default);

    /// <summary>Tutti i ruoli, compresi quelli di sistema.</summary>
    Task<IReadOnlyList<BlogRole>> GetRolesAsync(CancellationToken cancellationToken = default);

    /// <summary>Ruoli di un utente; un utente sconosciuto non ha ruoli.</summary>
    Task<IReadOnlyList<string>> GetRolesForUserAsync(string userName, CancellationToken cancellationToken = default);

    Task CreateRoleAsync(string roleName, CancellationToken cancellationToken = default);

    /// <summary>Elimina un ruolo (non di sistema) togliendogli anche i permessi.</summary>
    Task DeleteRoleAsync(string roleName, CancellationToken cancellationToken = default);

    /// <summary>Sostituisce i ruoli dell'utente.</summary>
    Task SetUserRolesAsync(string userName, IEnumerable<string> roles, bool bypassRights = false, CancellationToken cancellationToken = default);
}

/// <summary>Cache condivisa di utenti e ruoli.</summary>
public sealed class UserRepository(IUserStore users, IRoleStore roles) : IBlogScopedCache
{
    private readonly BlogDataCache<BlogUser> _users = new(blogId => users.GetAllAsync(blogId));
    private readonly BlogDataCache<BlogRole> _roles = new(blogId => roles.GetAllAsync(blogId));

    public Task<IReadOnlyList<BlogUser>> GetUsersAsync(Guid blogId, CancellationToken cancellationToken) => _users.GetAsync(blogId, cancellationToken);

    public Task<IReadOnlyList<BlogRole>> GetRolesAsync(Guid blogId, CancellationToken cancellationToken) => _roles.GetAsync(blogId, cancellationToken);

    public Task SaveUserAsync(BlogUser user, CancellationToken cancellationToken) =>
        _users.WriteAsync(user.BlogId, async items =>
        {
            await users.SaveAsync(user, CancellationToken.None).ConfigureAwait(false);
            return BlogDataCache<BlogUser>.Upsert(items, user, u => SameName(u.UserName, user.UserName));
        }, cancellationToken);

    public Task DeleteUserAsync(Guid blogId, string userName, CancellationToken cancellationToken) =>
        _users.WriteAsync(blogId, async items =>
        {
            await users.DeleteAsync(blogId, userName, CancellationToken.None).ConfigureAwait(false);
            return BlogDataCache<BlogUser>.Remove(items, u => SameName(u.UserName, userName));
        }, cancellationToken);

    public Task SaveRoleAsync(BlogRole role, CancellationToken cancellationToken) =>
        _roles.WriteAsync(role.BlogId, async items =>
        {
            await roles.SaveAsync(role, CancellationToken.None).ConfigureAwait(false);
            return BlogDataCache<BlogRole>.Upsert(items, role, r => SameName(r.Name, role.Name));
        }, cancellationToken);

    public Task DeleteRoleAsync(Guid blogId, string roleName, CancellationToken cancellationToken) =>
        _roles.WriteAsync(blogId, async items =>
        {
            await roles.DeleteAsync(blogId, roleName, CancellationToken.None).ConfigureAwait(false);
            return BlogDataCache<BlogRole>.Remove(items, r => SameName(r.Name, roleName));
        }, cancellationToken);

    public void Invalidate(Guid blogId)
    {
        _users.Invalidate(blogId);
        _roles.Invalidate(blogId);
    }

    public static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

public sealed class UserService(
    UserRepository repository,
    RightsCache rightsCache,
    IBlogContext context,
    IRightsService rights,
    ISettingsService settings,
    IBlogClock clock) : IUserService
{
    private static readonly string[] SystemRoleNames = [SystemRoles.Administrators, SystemRoles.Editors, SystemRoles.Registered, SystemRoles.Anonymous];

    private Guid BlogId => context.Blog.Id;

    public async Task<IReadOnlyList<BlogUser>> GetUsersAsync(CancellationToken cancellationToken = default) =>
        (await repository.GetUsersAsync(BlogId, cancellationToken).ConfigureAwait(false))
            .OrderBy(u => u.UserName, StringComparer.OrdinalIgnoreCase)
            .ToList();

    public async Task<BlogUser?> GetUserAsync(string userName, CancellationToken cancellationToken = default) =>
        (await repository.GetUsersAsync(BlogId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(u => UserRepository.SameName(u.UserName, userName));

    public async Task<BlogUser?> GetUserByEmailAsync(string email, CancellationToken cancellationToken = default) =>
        (await repository.GetUsersAsync(BlogId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));

    public async Task<BlogUser?> ValidateCredentialsAsync(string userName, string password, CancellationToken cancellationToken = default)
    {
        var user = await GetUserAsync(userName.Trim(), cancellationToken).ConfigureAwait(false);
        if (user is null || !PasswordHasher.Verify(user.PasswordHash, password))
        {
            return null;
        }

        user.LastLoginTime = clock.UtcNow;
        await repository.SaveUserAsync(user, cancellationToken).ConfigureAwait(false);
        return user;
    }

    public async Task<BlogUser> CreateUserAsync(string userName, string email, string password, IEnumerable<string>? roles = null, CancellationToken cancellationToken = default)
    {
        await DemandAsync(Rights.CreateNewUsers, cancellationToken).ConfigureAwait(false);
        var user = await AddUserAsync(userName, email, password, cancellationToken).ConfigureAwait(false);
        if (roles is not null)
        {
            await SetUserRolesAsync(user.UserName, roles, bypassRights: true, cancellationToken).ConfigureAwait(false);
        }

        return user;
    }

    public async Task<BlogUser> RegisterAsync(string userName, string email, string password, CancellationToken cancellationToken = default)
    {
        var blogSettings = await settings.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!blogSettings.EnableSelfRegistration)
        {
            throw new UnauthorizedAccessException("La registrazione non è abilitata.");
        }

        var user = await AddUserAsync(userName, email, password, cancellationToken).ConfigureAwait(false);
        var role = (await GetRolesAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => UserRepository.SameName(r.Name, blogSettings.SelfRegistrationInitialRole));

        // Per sicurezza la registrazione autonoma non può mai assegnare il ruolo di amministratore.
        if (role is not null && !UserRepository.SameName(role.Name, SystemRoles.Administrators))
        {
            await SetUserRolesAsync(user.UserName, [role.Name], bypassRights: true, cancellationToken).ConfigureAwait(false);
        }

        return user;
    }

    public async Task UpdateEmailAsync(string userName, string email, CancellationToken cancellationToken = default)
    {
        var user = await GetRequiredUserAsync(userName, cancellationToken).ConfigureAwait(false);
        await DemandForUserAsync(user.UserName, Rights.EditOwnUser, Rights.EditOtherUsers, cancellationToken).ConfigureAwait(false);
        await ValidateEmailAsync(email, user.UserName, cancellationToken).ConfigureAwait(false);

        user.Email = email.Trim();
        await repository.SaveUserAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        var userName = context.UserName ?? throw new UnauthorizedAccessException();
        var user = await GetRequiredUserAsync(userName, cancellationToken).ConfigureAwait(false);
        if (!PasswordHasher.Verify(user.PasswordHash, currentPassword))
        {
            return false;
        }

        ValidatePassword(newPassword);
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await repository.SaveUserAsync(user, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task SetPasswordAsync(string userName, string newPassword, bool bypassRights = false, CancellationToken cancellationToken = default)
    {
        var user = await GetRequiredUserAsync(userName, cancellationToken).ConfigureAwait(false);
        if (!bypassRights)
        {
            await DemandForUserAsync(user.UserName, Rights.EditOwnUser, Rights.EditOtherUsers, cancellationToken).ConfigureAwait(false);
        }

        ValidatePassword(newPassword);
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await repository.SaveUserAsync(user, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteUserAsync(string userName, CancellationToken cancellationToken = default)
    {
        var user = await GetRequiredUserAsync(userName, cancellationToken).ConfigureAwait(false);
        await DemandForUserAsync(user.UserName, Rights.DeleteUserSelf, Rights.DeleteUsersOtherThanSelf, cancellationToken).ConfigureAwait(false);

        var userRoles = await GetRolesForUserAsync(user.UserName, cancellationToken).ConfigureAwait(false);
        if (userRoles.Contains(SystemRoles.Administrators, StringComparer.OrdinalIgnoreCase)
            && (await UsersInRoleAsync(SystemRoles.Administrators, cancellationToken).ConfigureAwait(false)) <= 1)
        {
            throw new InvalidOperationException(Messages.LastAdministrator);
        }

        foreach (var role in (await repository.GetRolesAsync(BlogId, cancellationToken).ConfigureAwait(false)).Where(r => r.Users.Contains(user.UserName, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            var updated = new BlogRole { BlogId = BlogId, Name = role.Name, Users = role.Users.Where(u => !UserRepository.SameName(u, user.UserName)).ToList() };
            await repository.SaveRoleAsync(updated, cancellationToken).ConfigureAwait(false);
        }

        await repository.DeleteUserAsync(BlogId, user.UserName, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BlogRole>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var stored = await repository.GetRolesAsync(BlogId, cancellationToken).ConfigureAwait(false);
        var missingSystemRoles = SystemRoleNames
            .Where(name => !stored.Any(r => UserRepository.SameName(r.Name, name)))
            .Select(name => new BlogRole { BlogId = BlogId, Name = name });

        return stored.Concat(missingSystemRoles).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<IReadOnlyList<string>> GetRolesForUserAsync(string userName, CancellationToken cancellationToken = default) =>
        (await repository.GetRolesAsync(BlogId, cancellationToken).ConfigureAwait(false))
            .Where(r => r.Users.Contains(userName, StringComparer.OrdinalIgnoreCase))
            .Select(r => r.Name)
            .ToList();

    public async Task CreateRoleAsync(string roleName, CancellationToken cancellationToken = default)
    {
        await DemandAsync(Rights.CreateNewRoles, cancellationToken).ConfigureAwait(false);
        var name = roleName?.Trim() ?? string.Empty;
        var existing = await GetRolesAsync(cancellationToken).ConfigureAwait(false);
        BlogValidationException.ThrowIfInvalid(
            ("Name", name.Length == 0, Messages.RoleNameRequired),
            ("Name", existing.Any(r => UserRepository.SameName(r.Name, name)), Messages.RoleExists));

        await repository.SaveRoleAsync(new BlogRole { BlogId = BlogId, Name = name }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteRoleAsync(string roleName, CancellationToken cancellationToken = default)
    {
        await DemandAsync(Rights.DeleteRoles, cancellationToken).ConfigureAwait(false);
        if (SystemRoles.IsSystemRole(roleName))
        {
            throw new InvalidOperationException(Messages.SystemRolesDelete);
        }

        await repository.DeleteRoleAsync(BlogId, roleName, cancellationToken).ConfigureAwait(false);
        var matrix = await rightsCache.GetAsync(BlogId, cancellationToken).ConfigureAwait(false);
        await rightsCache.SaveAsync(BlogId, matrix.WithoutRole(roleName), cancellationToken).ConfigureAwait(false);
    }

    public async Task SetUserRolesAsync(string userName, IEnumerable<string> roles, bool bypassRights = false, CancellationToken cancellationToken = default)
    {
        var user = await GetRequiredUserAsync(userName, cancellationToken).ConfigureAwait(false);
        if (!bypassRights)
        {
            await DemandForUserAsync(user.UserName, Rights.EditOwnRoles, Rights.EditOtherUsersRoles, cancellationToken).ConfigureAwait(false);
        }

        var wanted = roles.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allRoles = await GetRolesAsync(cancellationToken).ConfigureAwait(false);
        var unknown = wanted.Where(w => !allRoles.Any(r => UserRepository.SameName(r.Name, w))).ToList();
        BlogValidationException.ThrowIfInvalid(("Roles", unknown.Count > 0, Messages.Format(Messages.RolesUnknown, string.Join(", ", unknown))));

        var wasAdmin = (await GetRolesForUserAsync(user.UserName, cancellationToken).ConfigureAwait(false)).Contains(SystemRoles.Administrators, StringComparer.OrdinalIgnoreCase);
        if (wasAdmin && !wanted.Contains(SystemRoles.Administrators)
            && (await UsersInRoleAsync(SystemRoles.Administrators, cancellationToken).ConfigureAwait(false)) <= 1)
        {
            throw new InvalidOperationException(Messages.BlogNeedsAdministrator);
        }

        foreach (var role in allRoles)
        {
            var isMember = role.Users.Contains(user.UserName, StringComparer.OrdinalIgnoreCase);
            var shouldBe = wanted.Contains(role.Name);
            if (isMember == shouldBe)
            {
                continue;
            }

            var updated = new BlogRole
            {
                BlogId = BlogId,
                Name = role.Name,
                Users = shouldBe
                    ? [.. role.Users, user.UserName]
                    : role.Users.Where(u => !UserRepository.SameName(u, user.UserName)).ToList(),
            };
            await repository.SaveRoleAsync(updated, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<BlogUser> AddUserAsync(string userName, string email, string password, CancellationToken cancellationToken)
    {
        // Tutti gli errori insieme, così il modulo li mostra in una volta sola.
        var name = userName?.Trim() ?? string.Empty;
        BlogValidationException.ThrowIfInvalid(
        [
            ("UserName", name.Length == 0, Messages.UserNameRequired),
            ("UserName", name.Length > 100 || name.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '/' or '\\' or '<' or '>' or '"'),
                Messages.UserNameInvalid),
            ("UserName", name.Length > 0 && await GetUserAsync(name, cancellationToken).ConfigureAwait(false) is not null, Messages.UserNameInUse),
            .. await EmailRulesAsync(email, null, cancellationToken).ConfigureAwait(false),
            PasswordRule(password),
        ]);

        var user = new BlogUser { BlogId = BlogId, UserName = name, Email = email.Trim(), PasswordHash = PasswordHasher.Hash(password) };
        await repository.SaveUserAsync(user, cancellationToken).ConfigureAwait(false);
        return user;
    }

    private async Task ValidateEmailAsync(string email, string? ownerUserName, CancellationToken cancellationToken) =>
        BlogValidationException.ThrowIfInvalid([.. await EmailRulesAsync(email, ownerUserName, cancellationToken).ConfigureAwait(false)]);

    private async Task<(string Field, bool IsInvalid, string Message)[]> EmailRulesAsync(string email, string? ownerUserName, CancellationToken cancellationToken)
    {
        var address = email?.Trim() ?? string.Empty;
        var other = address.Length == 0 ? null : await GetUserByEmailAsync(address, cancellationToken).ConfigureAwait(false);
        return
        [
            ("Email", !MailAddress.TryCreate(address, out _), Messages.EmailInvalid),
            ("Email", other is not null && (ownerUserName is null || !UserRepository.SameName(other.UserName, ownerUserName)), Messages.EmailInUse),
        ];
    }

    private static void ValidatePassword(string password) => BlogValidationException.ThrowIfInvalid(PasswordRule(password));

    private static (string Field, bool IsInvalid, string Message) PasswordRule(string password) =>
        ("Password", string.IsNullOrEmpty(password) || password.Length < PasswordHasher.MinimumLength,
            Messages.Format(Messages.PasswordTooShort, PasswordHasher.MinimumLength));

    private async Task<int> UsersInRoleAsync(string roleName, CancellationToken cancellationToken) =>
        (await repository.GetRolesAsync(BlogId, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => UserRepository.SameName(r.Name, roleName))?.Users.Count ?? 0;

    private async Task<BlogUser> GetRequiredUserAsync(string userName, CancellationToken cancellationToken) =>
        await GetUserAsync(userName, cancellationToken).ConfigureAwait(false)
        ?? throw new KeyNotFoundException($"Utente '{userName}' non trovato.");

    private async Task DemandAsync(Rights right, CancellationToken cancellationToken)
    {
        if (!await rights.IsAuthorizedAsync(right, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }
    }

    private Task DemandForUserAsync(string userName, Rights own, Rights others, CancellationToken cancellationToken) =>
        DemandAsync(context.UserName is { } current && UserRepository.SameName(current, userName) ? own : others, cancellationToken);
}

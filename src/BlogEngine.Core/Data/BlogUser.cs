namespace BlogEngine.Core.Data;

/// <summary>
/// Account di accesso. Resta nel Core (non nell'SDK pubblico) perché contiene l'hash della password.
/// </summary>
public sealed class BlogUser
{
    public Guid BlogId { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>Hash della password nel formato di ASP.NET Core Identity.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    public DateTime? LastLoginTime { get; set; }
}

/// <summary>Ruolo con l'elenco dei suoi utenti.</summary>
public sealed class BlogRole
{
    public Guid BlogId { get; set; }

    public string Name { get; set; } = string.Empty;

    public List<string> Users { get; set; } = [];
}

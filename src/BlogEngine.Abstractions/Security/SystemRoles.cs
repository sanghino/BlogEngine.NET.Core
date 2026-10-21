namespace BlogEngine.Security;

/// <summary>
/// Ruoli predefiniti, che non possono essere eliminati.
/// </summary>
public static class SystemRoles
{
    /// <summary>Possiede sempre tutti i permessi.</summary>
    public const string Administrators = "Administrators";

    public const string Editors = "Editors";

    /// <summary>Ruolo assegnato ai visitatori non autenticati.</summary>
    public const string Anonymous = "Anonymous";

    public static bool IsSystemRole(string? role) =>
        string.Equals(role, Administrators, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, Editors, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(role, Anonymous, StringComparison.OrdinalIgnoreCase);
}

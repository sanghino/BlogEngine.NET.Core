namespace BlogEngine.Models;

/// <summary>
/// Profilo pubblico di un autore, identificato dal nome utente.
/// </summary>
public sealed class AuthorProfile
{
    public string UserName { get; set; } = string.Empty;

    public Guid BlogId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;

    public string MiddleName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public DateTime? Birthday { get; set; }

    public string CityTown { get; set; } = string.Empty;

    public string RegionState { get; set; } = string.Empty;

    public string Country { get; set; } = string.Empty;

    public string Company { get; set; } = string.Empty;

    public string PhoneMain { get; set; } = string.Empty;

    public string PhoneMobile { get; set; } = string.Empty;

    public string PhoneFax { get; set; } = string.Empty;

    public string PhotoUrl { get; set; } = string.Empty;

    public string AboutMe { get; set; } = string.Empty;

    /// <summary>Profilo visibile solo agli utenti autenticati.</summary>
    public bool Private { get; set; }

    public string FullName =>
        string.Join(' ', new[] { FirstName, MiddleName, LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

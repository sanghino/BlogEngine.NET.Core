namespace BlogEngine.Services;

/// <summary>Immagine dell'autore di un commento o di un utente.</summary>
public interface IAvatarService
{
    /// <summary>
    /// Nell'ordine: icona dei pingback, foto del profilo con quell'email, Gravatar (se abilitato nelle
    /// impostazioni), immagine predefinita.
    /// </summary>
    Task<string> GetUrlAsync(string? email, CancellationToken cancellationToken = default);
}

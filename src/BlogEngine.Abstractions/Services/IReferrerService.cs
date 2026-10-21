using BlogEngine.Models;

namespace BlogEngine.Services;

/// <summary>Statistiche dei siti di provenienza dei visitatori del blog corrente.</summary>
public interface IReferrerService
{
    /// <summary>Referrer degli ultimi giorni (impostazione NumberOfReferrerDays), raggruppati per giorno.</summary>
    Task<IReadOnlyDictionary<DateTime, IReadOnlyList<Referrer>>> GetByDayAsync(CancellationToken cancellationToken = default);

    /// <summary>Conta una visita alla pagina <paramref name="url"/> proveniente da <paramref name="referrerUrl"/>.</summary>
    Task RegisterAsync(Uri url, Uri referrerUrl, bool possibleSpam, CancellationToken cancellationToken = default);
}

namespace BlogEngine.Services;

/// <summary>
/// Ora corrente e conversioni di fuso orario. Le date vengono memorizzate in UTC e convertite
/// nel fuso orario del blog solo per la visualizzazione.
/// </summary>
public interface IBlogClock
{
    DateTime UtcNow { get; }

    /// <summary>Converte una data UTC nell'ora locale del blog corrente.</summary>
    Task<DateTime> ToBlogTimeAsync(DateTime utc);

    /// <summary>Converte un'ora locale del blog corrente in UTC.</summary>
    Task<DateTime> ToUtcAsync(DateTime blogTime);
}

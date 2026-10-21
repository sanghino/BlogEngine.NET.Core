using BlogEngine.Models;

namespace BlogEngine.Services;

/// <summary>Esito della verifica di un filtro antispam.</summary>
public enum SpamCheckResult
{
    /// <summary>Il filtro non ha potuto verificare (non configurato, servizio non raggiungibile).</summary>
    NotChecked,

    /// <summary>Commento legittimo.</summary>
    Ham,

    Spam,
}

/// <summary>
/// Filtro antispam esterno (es. Akismet, StopForumSpam). Viene interpellato per i nuovi commenti
/// che non sono già stati decisi dalle regole automatiche o da quelle dell'amministratore.
/// Sostituisce ICustomFilter.
/// </summary>
public interface ICommentSpamFilter
{
    /// <summary>Nome mostrato nell'amministrazione e registrato in Comment.ModeratedBy.</summary>
    string Name { get; }

    /// <summary>Ordine di interrogazione: i valori più bassi vengono interpellati prima.</summary>
    int Priority => 0;

    Task<SpamCheckResult> CheckAsync(Post post, Comment comment, CancellationToken cancellationToken);

    /// <summary>Segnala al servizio che il filtro ha sbagliato su questo commento.</summary>
    Task ReportMistakeAsync(Post post, Comment comment, CancellationToken cancellationToken) => Task.CompletedTask;
}

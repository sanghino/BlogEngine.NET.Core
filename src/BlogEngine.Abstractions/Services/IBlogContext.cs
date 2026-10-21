using BlogEngine.Models;

namespace BlogEngine.Services;

/// <summary>
/// Contesto dell'operazione corrente: blog e utente. Nel sito web corrisponde alla richiesta HTTP;
/// nelle attività in background viene impostato esplicitamente.
/// Sostituisce Blog.CurrentInstance e Security.CurrentUser.
/// </summary>
public interface IBlogContext
{
    /// <summary>Blog su cui si sta operando.</summary>
    Blog Blog { get; }

    /// <summary>Nome dell'utente autenticato, oppure null per un visitatore anonimo.</summary>
    string? UserName { get; }

    /// <summary>Ruoli dell'utente. Per un visitatore anonimo contiene solo "Anonymous".</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>
    /// Indirizzo base della richiesta corrente (schema, host e porta), usato per i link assoluti
    /// dei blog senza nome host dedicato. Null fuori da una richiesta HTTP.
    /// </summary>
    Uri? RequestBaseUri { get; }

    bool IsAuthenticated => UserName is not null;
}

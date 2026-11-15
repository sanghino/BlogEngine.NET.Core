namespace BlogEngine.Web.Hosting;

/// <summary>
/// Registra nel log del sito le chiamate alle API dell'amministrazione che finiscono con un errore (4xx) e che
/// il filtro delle eccezioni non ha già registrato: indirizzi inesistenti (per esempio una pagina
/// dell'amministrazione che chiama un'API sbagliata), elementi non trovati, metodi non ammessi.
/// Solo per gli utenti collegati, così le scansioni automatiche dei robot non riempiono il log.
/// Come per le richieste rifiutate, si registra l'indirizzo e mai i dati inviati.
/// </summary>
public sealed class ApiErrorLoggingMiddleware(RequestDelegate next, ILogger<ApiErrorLoggingMiddleware> logger)
{
    /// <summary>Segnala che la richiesta è già stata registrata (dal filtro delle eccezioni delle API).</summary>
    public const string LoggedItem = "BlogEngine.ApiErrorLogged";

    public async Task InvokeAsync(HttpContext http)
    {
        await next(http);

        var status = http.Response.StatusCode;
        if (status is < 400 or >= 500 || http.Items.ContainsKey(LoggedItem) || http.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var reason = http.GetEndpoint() is null ? "indirizzo inesistente" : "elemento non trovato o richiesta non valida";
        logger.LogWarning("Amministrazione: richiesta {Method} {Path} rifiutata ({Status}) per l'utente {User}. Motivo: {Reason}",
            http.Request.Method, http.Request.Path.Value, status, http.User.Identity.Name, reason);
    }
}

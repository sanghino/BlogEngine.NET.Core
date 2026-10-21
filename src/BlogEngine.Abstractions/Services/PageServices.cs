namespace BlogEngine.Services;

/// <summary>Modulo in cui viene mostrato un captcha.</summary>
public enum CaptchaForm
{
    Comment,
    Contact,
}

/// <summary>Dati della richiesta utili al captcha.</summary>
public sealed class CaptchaContext
{
    public required CaptchaForm Form { get; init; }

    public bool IsAuthenticated { get; init; }

    public string RemoteIp { get; init; } = string.Empty;
}

/// <summary>
/// Captcha per i moduli dei commenti e dei contatti (es. SimpleCaptcha, reCAPTCHA). L'HTML viene
/// inserito nel modulo; alla conferma i campi del modulo vengono passati a <see cref="ValidateAsync"/>.
/// Sostituisce i controlli SimpleCaptchaControl e RecaptchaControl.
/// </summary>
public interface ICaptchaProvider
{
    string Name { get; }

    /// <summary>HTML da mostrare nel modulo, oppure null se il captcha non serve (es. utente autenticato).</summary>
    Task<string?> RenderAsync(CaptchaContext context, CancellationToken cancellationToken);

    /// <summary>Verifica la risposta; i campi sono quelli del modulo inviato (nome → valore).</summary>
    Task<bool> ValidateAsync(CaptchaContext context, IReadOnlyDictionary<string, string> fields, CancellationToken cancellationToken);
}

/// <summary>
/// Script, fogli di stile e codice HTML da aggiungere alla pagina che si sta preparando
/// (porting di Helpers.AddScript / AddStyle e RegisterStartupScript). Vale per la sola richiesta corrente.
/// </summary>
public interface IPageResources
{
    /// <summary>Indirizzo della radice del sito, dove si trovano i file comuni (Scripts/, Content/, admin/).</summary>
    string ApplicationRoot { get; }

    void AddScript(string url);

    void AddStylesheet(string url);

    /// <summary>Codice HTML aggiunto in fondo a &lt;head&gt;.</summary>
    void AddHeadHtml(string html);

    /// <summary>Codice HTML aggiunto in fondo alla pagina (es. script di avvio).</summary>
    void AddFooterHtml(string html);
}

/// <summary>Testi dell'interfaccia tradotti nella lingua del blog (es. "more", "delete").</summary>
public interface ILabels
{
    /// <summary>Testo tradotto; se la chiave non esiste restituisce la chiave stessa.</summary>
    string this[string key] { get; }
}

/// <summary>
/// Invio di email in background, senza far attendere il visitatore (porting di SendMailMessageAsync).
/// Il messaggio viene inviato con le impostazioni SMTP del blog corrente.
/// </summary>
public interface IEmailQueue
{
    void Enqueue(EmailMessage message);
}

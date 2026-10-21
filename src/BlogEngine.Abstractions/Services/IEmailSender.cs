using BlogEngine.Events;

namespace BlogEngine.Services;

/// <summary>Messaggio email in formato HTML.</summary>
public sealed class EmailMessage
{
    public required IReadOnlyList<string> To { get; init; }

    /// <summary>Oggetto; il prefisso impostato nel blog (EmailSubjectPrefix) viene aggiunto automaticamente.</summary>
    public required string Subject { get; init; }

    public required string HtmlBody { get; init; }

    public string? ReplyTo { get; init; }

    public IReadOnlyList<EmailAttachment> Attachments { get; init; } = [];
}

public sealed record EmailAttachment(string FileName, string ContentType, byte[] Content);

/// <summary>
/// Invio di email con le impostazioni SMTP del blog corrente. Genera <see cref="EmailSentEvent"/>
/// o <see cref="EmailFailedEvent"/>.
/// </summary>
public interface IEmailSender
{
    /// <summary>Invia il messaggio; restituisce null se l'invio è riuscito, altrimenti la descrizione dell'errore.</summary>
    Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}

public sealed class EmailSentEvent : BlogEvent
{
    public required EmailMessage Message { get; init; }
}

public sealed class EmailFailedEvent : BlogEvent
{
    public required EmailMessage Message { get; init; }

    public required string Error { get; init; }
}

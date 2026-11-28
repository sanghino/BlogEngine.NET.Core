namespace BlogEngine.Models;

/// <summary>Stato di un messaggio del modulo di contatto trattenuto dal filtro antispam.</summary>
public enum ContactMessageStatus
{
    /// <summary>Bloccato dal filtro: non è stato inviato, in attesa di verifica.</summary>
    Blocked,

    /// <summary>Sbloccato dall'amministratore e recapitato.</summary>
    Released,
}

/// <summary>
/// Messaggio del modulo di contatto bloccato dal filtro antispam, con tutti i dati inseriti
/// e il motivo del blocco. Dall'amministrazione si può rileggere e, se è valido, recapitare.
/// </summary>
public sealed class ContactMessage : IBlogItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BlogId { get; set; }

    /// <summary>Data di invio (UTC).</summary>
    public DateTime DateCreated { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string IP { get; set; } = string.Empty;

    public string UserAgent { get; set; } = string.Empty;

    /// <summary>Filtro che ha bloccato il messaggio (es. "Honeypot", "Links", "Filter", "StopForumSpam").</summary>
    public string BlockedBy { get; set; } = string.Empty;

    /// <summary>Spiegazione leggibile del blocco.</summary>
    public string Reason { get; set; } = string.Empty;

    public ContactMessageStatus Status { get; set; }

    /// <summary>Data dello sblocco e del recapito (UTC).</summary>
    public DateTime? DateReleased { get; set; }

    /// <summary>Utente che ha sbloccato il messaggio.</summary>
    public string ReleasedBy { get; set; } = string.Empty;

    public string AttachmentName { get; set; } = string.Empty;

    public string AttachmentType { get; set; } = string.Empty;

    /// <summary>Contenuto dell'allegato, conservato per poterlo recapitare insieme al messaggio.</summary>
    public byte[]? AttachmentData { get; set; }
}

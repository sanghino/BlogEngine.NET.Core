using BlogEngine.Text;

namespace BlogEngine.Models;

/// <summary>
/// Commento a un post. Pingback e trackback sono commenti con email "pingback" o "trackback".
/// </summary>
public sealed class Comment : IPublishable, IBlogItem
{
    public const string PingbackEmail = "pingback";
    public const string TrackbackEmail = "trackback";

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Post a cui appartiene il commento.</summary>
    public Guid PostId { get; set; }

    public Guid BlogId { get; set; }

    /// <summary>Commento a cui questo risponde; <see cref="Guid.Empty"/> per i commenti di primo livello.</summary>
    public Guid ParentId { get; set; }

    public string Author { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public Uri? Website { get; set; }

    public string Country { get; set; } = string.Empty;

    public string IP { get; set; } = string.Empty;

    public string Avatar { get; set; } = string.Empty;

    /// <summary>Testo del commento così come inserito (non codificato).</summary>
    public string Content { get; set; } = string.Empty;

    public DateTime DateCreated { get; set; }

    public bool IsApproved { get; set; }

    public bool IsSpam { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>Chi ha approvato o rifiutato il commento (utente o filtro antispam).</summary>
    public string ModeratedBy { get; set; } = string.Empty;

    public bool IsPublished => IsApproved && !IsSpam && !IsDeleted;

    public bool IsPingbackOrTrackback =>
        Email.Equals(PingbackEmail, StringComparison.OrdinalIgnoreCase) ||
        Email.Equals(TrackbackEmail, StringComparison.OrdinalIgnoreCase);

    /// <summary>Estratto del testo senza HTML, al massimo 120 caratteri.</summary>
    public string Teaser
    {
        get
        {
            var text = HtmlText.Strip(Content);
            return text.Length > 120 ? $"{text[..116]} ..." : text;
        }
    }

    string IPublishable.Title => Author;

    string IPublishable.Description => string.Empty;

    DateTime IPublishable.DateModified => DateCreated;
}

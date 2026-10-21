namespace BlogEngine.Models;

/// <summary>
/// Articolo del blog. I commenti sono memorizzati insieme al post.
/// </summary>
public sealed class Post : BlogEntity, IPublishable
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Parte finale dell'URL. Se vuoto viene generato dal titolo al salvataggio.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsPublished { get; set; } = true;

    /// <summary>Post nel cestino: non visibile, ma ripristinabile.</summary>
    public bool IsDeleted { get; set; }

    public bool HasCommentsEnabled { get; set; } = true;

    public int Raters { get; set; }

    public float Rating { get; set; }

    /// <summary>Id delle categorie del post.</summary>
    public List<Guid> Categories { get; set; } = [];

    public List<string> Tags { get; set; } = [];

    /// <summary>Indirizzi iscritti alla notifica dei nuovi commenti.</summary>
    public List<string> NotificationEmails { get; set; } = [];

    /// <summary>Tutti i commenti, compresi quelli nel cestino.</summary>
    public List<Comment> AllComments { get; set; } = [];

    /// <summary>Commenti non eliminati.</summary>
    public IEnumerable<Comment> Comments => AllComments.Where(c => !c.IsDeleted);

    /// <summary>Commenti in attesa di approvazione (spam, pingback e trackback esclusi).</summary>
    public IEnumerable<Comment> NotApprovedComments =>
        Comments.Where(c => !c.IsApproved && !c.IsSpam && !c.IsPingbackOrTrackback);

    /// <summary>Pingback e trackback approvati.</summary>
    public IEnumerable<Comment> Pingbacks =>
        Comments.Where(c => c.IsApproved && !c.IsSpam && c.IsPingbackOrTrackback);

    public IEnumerable<Comment> SpamComments => Comments.Where(c => c.IsSpam);

    public IEnumerable<Comment> DeletedComments => AllComments.Where(c => c.IsDeleted);

    /// <summary>
    /// Commenti da mostrare. Con la moderazione attiva servono i commenti approvati;
    /// senza moderazione si mostrano tutti quelli che non sono spam.
    /// </summary>
    public IEnumerable<Comment> ApprovedComments(bool moderationEnabled) =>
        Comments.Where(c => (!moderationEnabled || c.IsApproved) && !c.IsSpam && !c.IsPingbackOrTrackback);

    /// <summary>Aggiunge un voto e aggiorna la media.</summary>
    public void AddRating(int rating)
    {
        Rating = Raters > 0 ? ((Raters * Rating) + rating) / (Raters + 1) : rating;
        Raters++;
    }

    public override string ToString() => Title;
}

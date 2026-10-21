namespace BlogEngine.Models;

/// <summary>
/// Visite provenienti da un sito esterno verso una pagina del blog, aggregate per giorno.
/// </summary>
public sealed class Referrer : IBlogItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BlogId { get; set; }

    public DateTime Day { get; set; }

    /// <summary>Pagina del blog visitata.</summary>
    public Uri? Url { get; set; }

    /// <summary>Pagina esterna di provenienza.</summary>
    public Uri? ReferrerUrl { get; set; }

    public int Count { get; set; }

    /// <summary>La pagina di provenienza non contiene un link al blog: probabile spam.</summary>
    public bool PossibleSpam { get; set; }
}

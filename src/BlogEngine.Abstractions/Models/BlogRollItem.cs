namespace BlogEngine.Models;

/// <summary>
/// Collegamento a un altro blog nel blogroll.
/// </summary>
public sealed class BlogRollItem : IBlogItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BlogId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Uri? BlogUrl { get; set; }

    public Uri? FeedUrl { get; set; }

    /// <summary>Relazione XFN (es. "friend", "colleague").</summary>
    public string Xfn { get; set; } = string.Empty;

    public int SortIndex { get; set; }
}

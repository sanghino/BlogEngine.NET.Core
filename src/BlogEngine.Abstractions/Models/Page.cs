namespace BlogEngine.Models;

/// <summary>
/// Pagina statica, eventualmente figlia di un'altra pagina.
/// </summary>
public sealed class Page : BlogEntity, IPublishable
{
    public string Title { get; set; } = string.Empty;

    /// <summary>Parte finale dell'URL. Se vuoto viene generato dal titolo al salvataggio.</summary>
    public string Slug { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Keywords { get; set; } = string.Empty;

    /// <summary>Pagina padre; <see cref="Guid.Empty"/> per le pagine di primo livello.</summary>
    public Guid Parent { get; set; }

    public bool IsPublished { get; set; }

    public bool IsDeleted { get; set; }

    /// <summary>Pagina usata come home page del blog.</summary>
    public bool IsFrontPage { get; set; }

    /// <summary>Mostra la pagina nei menu e nell'elenco delle pagine.</summary>
    public bool ShowInList { get; set; }

    public int SortOrder { get; set; }

    public bool HasParentPage => Parent != Guid.Empty;

    /// <summary>Le pagine non hanno un autore: viene mostrato quello impostato nel blog.</summary>
    string IPublishable.Author => string.Empty;

    public override string ToString() => Title;
}

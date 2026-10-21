namespace BlogEngine.Models;

/// <summary>
/// Istanza del multi-blog. Un blog si riconosce dal nome host e/o dal percorso virtuale.
/// </summary>
public sealed class Blog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    /// <summary>Blog principale: non può essere eliminato e risponde quando nessun altro corrisponde.</summary>
    public bool IsPrimary { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsDeleted { get; set; }

    /// <summary>Blog che mostra anche i contenuti di tutti gli altri blog.</summary>
    public bool IsSiteAggregation { get; set; }

    /// <summary>Nome host dedicato (es. "blog.esempio.it"); vuoto per usare quello della richiesta.</summary>
    public string Hostname { get; set; } = string.Empty;

    /// <summary>Accetta anche i sottodomini del nome host (es. "www.").</summary>
    public bool IsAnyTextBeforeHostnameAccepted { get; set; }

    /// <summary>Nome della cartella o contenitore dei dati; vuoto per il blog principale.</summary>
    public string StorageContainerName { get; set; } = string.Empty;

    /// <summary>Percorso virtuale del blog, per esempio "~/" o "~/secondo-blog/".</summary>
    public string VirtualPath { get; set; } = "~/";

    public DateTime DateCreated { get; set; }

    public DateTime DateModified { get; set; }

    public override string ToString() => Name;
}

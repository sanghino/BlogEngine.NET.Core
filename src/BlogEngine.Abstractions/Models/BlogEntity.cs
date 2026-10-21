namespace BlogEngine.Models;

/// <summary>
/// Base delle entità che appartengono a un blog (post, pagine, categorie, ...).
/// Le entità sono semplici contenitori di dati: la logica sta nei servizi.
/// </summary>
public abstract class BlogEntity : IBlogItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Blog (istanza del multi-blog) a cui appartiene l'entità.</summary>
    public Guid BlogId { get; set; }

    /// <summary>Data di creazione, in UTC.</summary>
    public DateTime DateCreated { get; set; }

    /// <summary>Data dell'ultima modifica, in UTC.</summary>
    public DateTime DateModified { get; set; }
}

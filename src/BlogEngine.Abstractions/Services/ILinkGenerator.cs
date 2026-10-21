using BlogEngine.Models;

namespace BlogEngine.Services;

/// <summary>
/// Costruisce i link pubblici del blog. I link relativi partono dalla radice del sito ("/...");
/// quelli assoluti usano il nome host del blog o, se assente, quello della richiesta corrente.
/// </summary>
public interface ILinkGenerator
{
    /// <summary>Radice relativa del blog corrente, sempre terminata da "/" (es. "/" o "/secondo-blog/").</summary>
    string BlogRoot { get; }

    /// <summary>Link al post, nel blog a cui il post appartiene.</summary>
    Task<string> PostAsync(Post post, CancellationToken cancellationToken = default);

    Task<string> PageAsync(Page page, CancellationToken cancellationToken = default);

    /// <summary>Link al commento: pagina del post con l'ancora "#id_{commentId}".</summary>
    Task<string> CommentAsync(Post post, Comment comment, CancellationToken cancellationToken = default);

    Task<string> CategoryAsync(Category category, CancellationToken cancellationToken = default);

    Task<string> CategoryFeedAsync(Category category, CancellationToken cancellationToken = default);

    string Tag(string tag);

    string Author(string userName);

    string Archive();

    string Contact();

    string Search(string? query = null);

    /// <summary>Trasforma un link relativo in assoluto per il blog corrente.</summary>
    Uri ToAbsolute(string relativeLink);
}

namespace BlogEngine.Models;

/// <summary>
/// Contenuto pubblicabile e visualizzabile: post, pagine e commenti.
/// </summary>
public interface IPublishable
{
    Guid Id { get; }

    Guid BlogId { get; }

    string Title { get; }

    string Content { get; }

    string Description { get; }

    string Author { get; }

    DateTime DateCreated { get; }

    DateTime DateModified { get; }

    bool IsPublished { get; }

    bool IsDeleted { get; }
}

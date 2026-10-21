using BlogEngine.Models;

namespace BlogEngine.Web.Api;

/// <summary>
/// Copie delle entità da modificare. I servizi restituiscono oggetti condivisi dalla cache: modificando
/// una copia, un salvataggio rifiutato (dati non validi, permessi) non lascia modifiche a metà in memoria.
/// </summary>
public static class EntityCopies
{
    public static Post Copy(Post post) => new()
    {
        Id = post.Id,
        BlogId = post.BlogId,
        Title = post.Title,
        Slug = post.Slug,
        Author = post.Author,
        Content = post.Content,
        Description = post.Description,
        IsPublished = post.IsPublished,
        IsDeleted = post.IsDeleted,
        HasCommentsEnabled = post.HasCommentsEnabled,
        Raters = post.Raters,
        Rating = post.Rating,
        DateCreated = post.DateCreated,
        DateModified = post.DateModified,
        Categories = [.. post.Categories],
        Tags = [.. post.Tags],
        NotificationEmails = [.. post.NotificationEmails],
        AllComments = [.. post.AllComments],
    };

    public static Page Copy(Page page) => new()
    {
        Id = page.Id,
        BlogId = page.BlogId,
        Title = page.Title,
        Slug = page.Slug,
        Content = page.Content,
        Description = page.Description,
        Keywords = page.Keywords,
        Parent = page.Parent,
        IsPublished = page.IsPublished,
        IsDeleted = page.IsDeleted,
        IsFrontPage = page.IsFrontPage,
        ShowInList = page.ShowInList,
        SortOrder = page.SortOrder,
        DateCreated = page.DateCreated,
        DateModified = page.DateModified,
    };

    public static Category Copy(Category category) => new()
    {
        Id = category.Id,
        BlogId = category.BlogId,
        Title = category.Title,
        Description = category.Description,
        Parent = category.Parent,
        DateCreated = category.DateCreated,
        DateModified = category.DateModified,
    };
}

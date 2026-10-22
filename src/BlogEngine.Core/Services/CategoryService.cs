using BlogEngine.Core.Caching;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Text;

namespace BlogEngine.Core.Services;

public sealed class CategoryService(
    EntityRepository<Category> categories,
    EntityRepository<Post> posts,
    IBlogContext context,
    IRightsService rights,
    IBlogClock clock,
    IEventPublisher events) : ICategoryService
{
    public async Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<Category>(await categories.GetAllAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false));
        return list.OrderBy(c => CompleteTitle(c, list), StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await GetCategoriesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(c => c.Id == id);

    public async Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        (await GetCategoriesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(c => Slug.AreEquivalent(c.Title, slug));

    public async Task<string> GetCompleteTitleAsync(Category category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        return CompleteTitle(category, await categories.GetAllAsync(category.BlogId, cancellationToken).ConfigureAwait(false));
    }

    public async Task SaveAsync(Category category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        await DemandAsync(Rights.EditOwnPosts, cancellationToken).ConfigureAwait(false);
        if (category.BlogId == Guid.Empty)
        {
            category.BlogId = context.Blog.Id;
        }

        var all = await categories.GetAllAsync(category.BlogId, cancellationToken).ConfigureAwait(false);
        BlogValidationException.ThrowIfInvalid(
            ("Title", string.IsNullOrWhiteSpace(category.Title), Messages.TitleRequired),
            ("Title", all.Any(c => c.Id != category.Id && Slug.AreEquivalent(c.Title, category.Title)), Messages.CategoryExists),
            ("Parent", category.Parent == category.Id || CreatesCycle(category, all), Messages.CategoryParentInvalid));

        var isNew = all.All(c => c.Id != category.Id);
        var now = clock.UtcNow;
        if (category.DateCreated == default)
        {
            category.DateCreated = now;
        }

        category.DateModified = now;
        var action = isNew ? SaveAction.Insert : SaveAction.Update;
        await events.PublishAsync(new SavingEvent<Category> { BlogId = category.BlogId, Entity = category, Action = action }, cancellationToken).ConfigureAwait(false);
        await categories.SaveAsync(category, cancellationToken).ConfigureAwait(false);
        await events.PublishAsync(new SavedEvent<Category> { BlogId = category.BlogId, Entity = category, Action = action }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Category category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        // Come nella versione originale (CategoryRepository.Remove) basta il permesso di eliminare i propri post.
        await DemandAsync(Rights.DeleteOwnPosts, cancellationToken).ConfigureAwait(false);

        await events.PublishAsync(new SavingEvent<Category> { BlogId = category.BlogId, Entity = category, Action = SaveAction.Delete }, cancellationToken).ConfigureAwait(false);

        // La categoria viene tolta dai post che la usano.
        foreach (var post in (await posts.GetAllAsync(category.BlogId, cancellationToken).ConfigureAwait(false)).Where(p => p.Categories.Contains(category.Id)).ToList())
        {
            post.Categories = post.Categories.Where(id => id != category.Id).ToList();
            await posts.SaveAsync(post, cancellationToken).ConfigureAwait(false);
        }

        // Le sottocategorie diventano di primo livello.
        foreach (var child in (await categories.GetAllAsync(category.BlogId, cancellationToken).ConfigureAwait(false)).Where(c => c.Parent == category.Id).ToList())
        {
            child.Parent = null;
            await categories.SaveAsync(child, cancellationToken).ConfigureAwait(false);
        }

        await categories.DeleteAsync(category.BlogId, category.Id, cancellationToken).ConfigureAwait(false);
        await events.PublishAsync(new SavedEvent<Category> { BlogId = category.BlogId, Entity = category, Action = SaveAction.Delete }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>"Padre - Figlio", risalendo la gerarchia; si ferma se incontra un ciclo.</summary>
    private static string CompleteTitle(Category category, IReadOnlyList<Category> all)
    {
        var titles = new List<string> { category.Title };
        var visited = new HashSet<Guid> { category.Id };
        var current = category;
        while (current.Parent is { } parentId
               && visited.Add(parentId)
               && all.FirstOrDefault(c => c.Id == parentId) is { } parent)
        {
            titles.Insert(0, parent.Title);
            current = parent;
        }

        return string.Join(" - ", titles);
    }

    private static bool CreatesCycle(Category category, IReadOnlyList<Category> all)
    {
        var visited = new HashSet<Guid> { category.Id };
        var parentId = category.Parent;
        while (parentId is { } id)
        {
            if (!visited.Add(id))
            {
                return true;
            }

            parentId = all.FirstOrDefault(c => c.Id == id)?.Parent;
        }

        return false;
    }

    private async Task DemandAsync(Rights right, CancellationToken cancellationToken)
    {
        if (!await rights.IsAuthorizedAsync(right, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

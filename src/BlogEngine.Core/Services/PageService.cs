using BlogEngine.Core.Caching;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Text;

namespace BlogEngine.Core.Services;

public sealed class PageService(
    EntityRepository<Page> pages,
    IBlogContext context,
    IRightsService rights,
    IBlogClock clock,
    IEventPublisher events) : IPageService
{
    public async Task<IReadOnlyList<Page>> GetPagesAsync(CancellationToken cancellationToken = default) =>
        Sort((await pages.GetAllAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false)).Where(p => !p.IsDeleted));

    public async Task<IReadOnlyList<Page>> GetVisiblePagesAsync(CancellationToken cancellationToken = default)
    {
        var canSeeUnpublished = await rights.IsAuthorizedAsync(Rights.ViewUnpublishedPages, cancellationToken).ConfigureAwait(false);
        return (await GetPagesAsync(cancellationToken).ConfigureAwait(false)).Where(p => p.IsPublished || canSeeUnpublished).ToList();
    }

    public async Task<IReadOnlyList<Page>> GetDeletedPagesAsync(CancellationToken cancellationToken = default) =>
        Sort((await pages.GetAllAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false)).Where(p => p.IsDeleted));

    public Task<Page?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        pages.FindAsync(context.Blog.Id, id, cancellationToken);

    public async Task<Page?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        (await GetPagesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(p => Slug.AreEquivalent(p.Slug, slug));

    public async Task<Page?> GetFrontPageAsync(CancellationToken cancellationToken = default) =>
        (await GetVisiblePagesAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(p => p.IsFrontPage);

    public async Task<IReadOnlyList<Page>> GetChildrenAsync(Guid parentId, CancellationToken cancellationToken = default) =>
        (await GetVisiblePagesAsync(cancellationToken).ConfigureAwait(false)).Where(p => p.Parent == parentId).ToList();

    public async Task SaveAsync(Page page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        BlogValidationException.ThrowIfInvalid(
            ("Title", string.IsNullOrWhiteSpace(page.Title), Messages.TitleRequired),
            ("Content", string.IsNullOrWhiteSpace(page.Content), Messages.ContentRequired),
            ("Parent", page.Parent == page.Id, Messages.PageParentSelf));

        if (page.BlogId == Guid.Empty)
        {
            page.BlogId = context.Blog.Id;
        }

        var all = await pages.GetAllAsync(page.BlogId, cancellationToken).ConfigureAwait(false);
        var isNew = all.All(p => p.Id != page.Id);
        await DemandAsync(isNew ? Rights.CreateNewPages : Rights.EditOwnPages, cancellationToken).ConfigureAwait(false);

        var now = clock.UtcNow;
        if (page.DateCreated == default)
        {
            page.DateCreated = now;
        }

        page.DateModified = now;
        page.Slug = UniqueSlug(page, all);

        var action = isNew ? SaveAction.Insert : SaveAction.Update;
        await events.PublishAsync(new SavingEvent<Page> { BlogId = page.BlogId, Entity = page, Action = action }, cancellationToken).ConfigureAwait(false);

        if (page.IsFrontPage)
        {
            // Una sola home page per blog.
            foreach (var other in all.Where(p => p.IsFrontPage && p.Id != page.Id).ToList())
            {
                other.IsFrontPage = false;
                await pages.SaveAsync(other, cancellationToken).ConfigureAwait(false);
            }
        }

        await pages.SaveAsync(page, cancellationToken).ConfigureAwait(false);
        await events.PublishAsync(new SavedEvent<Page> { BlogId = page.BlogId, Entity = page, Action = action }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Page page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        await DemandAsync(Rights.EditOwnPages, cancellationToken).ConfigureAwait(false);
        await events.PublishAsync(new SavingEvent<Page> { BlogId = page.BlogId, Entity = page, Action = SaveAction.Delete }, cancellationToken).ConfigureAwait(false);

        page.IsDeleted = true;
        page.DateModified = clock.UtcNow;
        await pages.SaveAsync(page, cancellationToken).ConfigureAwait(false);

        await events.PublishAsync(new SavedEvent<Page> { BlogId = page.BlogId, Entity = page, Action = SaveAction.Delete }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(Page page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        await DemandAsync(Rights.EditOwnPages, cancellationToken).ConfigureAwait(false);
        page.IsDeleted = false;
        page.DateModified = clock.UtcNow;
        await pages.SaveAsync(page, cancellationToken).ConfigureAwait(false);
    }

    public async Task PurgeAsync(Page page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        await DemandAsync(Rights.EditOwnPages, cancellationToken).ConfigureAwait(false);
        await pages.DeleteAsync(page.BlogId, page.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsVisibleAsync(Page page, CancellationToken cancellationToken = default) =>
        !page.IsDeleted && (page.IsPublished || await rights.IsAuthorizedAsync(Rights.ViewUnpublishedPages, cancellationToken).ConfigureAwait(false));

    /// <summary>Le pagine non hanno un autore: chiunque abbia il permesso può modificarle tutte.</summary>
    public Task<bool> CanEditAsync(CancellationToken cancellationToken = default) =>
        rights.IsAuthorizedAsync(Rights.EditOwnPages, cancellationToken);

    private static IReadOnlyList<Page> Sort(IEnumerable<Page> list) =>
        list.OrderBy(p => p.SortOrder).ThenBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase).ToList();

    private static string UniqueSlug(Page page, IReadOnlyList<Page> all)
    {
        var baseSlug = Slug.Create(string.IsNullOrWhiteSpace(page.Slug) ? page.Title : page.Slug);
        if (baseSlug.Length == 0)
        {
            baseSlug = page.Id.ToString("N");
        }

        var others = all.Where(p => p.Id != page.Id).Select(p => p.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidate = baseSlug;
        for (var i = 1; others.Contains(candidate); i++)
        {
            candidate = $"{baseSlug}{i}";
        }

        return candidate;
    }

    private async Task DemandAsync(Rights right, CancellationToken cancellationToken)
    {
        if (!await rights.IsAuthorizedAsync(right, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

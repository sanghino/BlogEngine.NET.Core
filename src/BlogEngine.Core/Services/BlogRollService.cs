using BlogEngine.Core.Caching;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

public sealed class BlogRollService(EntityRepository<BlogRollItem> items, IBlogContext context, IRightsService rights) : IBlogRollService
{
    public async Task<IReadOnlyList<BlogRollItem>> GetItemsAsync(CancellationToken cancellationToken = default) =>
        (await items.GetAllAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false))
            .OrderBy(i => i.SortIndex)
            .ThenBy(i => i.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public async Task SaveAsync(BlogRollItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await DemandAsync(cancellationToken).ConfigureAwait(false);
        BlogValidationException.ThrowIfInvalid(
            ("Title", string.IsNullOrWhiteSpace(item.Title), Messages.TitleRequired),
            ("BlogUrl", item.BlogUrl is null, Messages.BlogUrlRequired));

        if (item.BlogId == Guid.Empty)
        {
            item.BlogId = context.Blog.Id;
        }

        await items.SaveAsync(item, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await DemandAsync(cancellationToken).ConfigureAwait(false);
        await items.DeleteAsync(context.Blog.Id, id, cancellationToken).ConfigureAwait(false);
    }

    private async Task DemandAsync(CancellationToken cancellationToken)
    {
        if (!await rights.IsAuthorizedAsync(Rights.AccessAdminSettingsPages, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

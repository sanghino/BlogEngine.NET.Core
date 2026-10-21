using BlogEngine.Core.Caching;
using BlogEngine.Core.Data;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

/// <summary>Cache condivisa dei campi personalizzati.</summary>
public sealed class CustomFieldRepository(ICustomFieldStore store) : IBlogScopedCache
{
    private readonly BlogDataCache<CustomField> _cache = new(blogId => store.GetAllAsync(blogId));

    public Task<IReadOnlyList<CustomField>> GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        _cache.GetAsync(blogId, cancellationToken);

    public Task SaveAsync(CustomField field, CancellationToken cancellationToken) =>
        _cache.WriteAsync(field.BlogId, async items =>
        {
            await store.SaveAsync(field, CancellationToken.None).ConfigureAwait(false);
            return BlogDataCache<CustomField>.Upsert(items, field, f => SameField(f, field));
        }, cancellationToken);

    public Task DeleteAsync(IReadOnlyList<CustomField> fields, Guid blogId, CancellationToken cancellationToken) =>
        _cache.WriteAsync(blogId, async items =>
        {
            foreach (var field in fields)
            {
                await store.DeleteAsync(field, CancellationToken.None).ConfigureAwait(false);
            }

            return BlogDataCache<CustomField>.Remove(items, f => fields.Any(d => SameField(f, d)));
        }, cancellationToken);

    public void Invalidate(Guid blogId) => _cache.Invalidate(blogId);

    public static bool SameField(CustomField a, CustomField b) =>
        string.Equals(a.CustomType, b.CustomType, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.ObjectId, b.ObjectId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
}

public sealed class CustomFieldService(CustomFieldRepository fields, IBlogContext context, IRightsService rights) : ICustomFieldService
{
    public async Task<IReadOnlyList<CustomField>> GetFieldsAsync(string customType, string objectId, CancellationToken cancellationToken = default) =>
        (await fields.GetAllAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false))
            .Where(f => string.Equals(f.CustomType, customType, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(f.ObjectId, objectId, StringComparison.OrdinalIgnoreCase))
            .ToList();

    public async Task SaveAsync(CustomField field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);
        await DemandAsync(cancellationToken).ConfigureAwait(false);
        BlogValidationException.ThrowIfInvalid(
            ("CustomType", string.IsNullOrWhiteSpace(field.CustomType), "Il tipo è obbligatorio."),
            ("Key", string.IsNullOrWhiteSpace(field.Key), "La chiave è obbligatoria."));

        if (field.BlogId == Guid.Empty)
        {
            field.BlogId = context.Blog.Id;
        }

        await fields.SaveAsync(field, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(CustomField field, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);
        await DemandAsync(cancellationToken).ConfigureAwait(false);
        await fields.DeleteAsync([field], context.Blog.Id, cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearAsync(string customType, string objectId, CancellationToken cancellationToken = default)
    {
        await DemandAsync(cancellationToken).ConfigureAwait(false);
        var existing = await GetFieldsAsync(customType, objectId, cancellationToken).ConfigureAwait(false);
        if (existing.Count > 0)
        {
            await fields.DeleteAsync(existing, context.Blog.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DemandAsync(CancellationToken cancellationToken)
    {
        if (!await rights.IsAuthorizedAsync(Rights.AccessAdminPages, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

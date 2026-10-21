using BlogEngine.Core.Caching;
using BlogEngine.Core.Data;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

/// <summary>
/// Cache condivisa dell'elenco dei blog. Se lo store è vuoto crea il blog principale, come faceva
/// la versione .NET Framework al primo avvio.
/// </summary>
public sealed class BlogsCache
{
    private static readonly Guid AllBlogsKey = Guid.Empty;

    private readonly IBlogStore _store;
    private readonly TimeProvider _time;
    private readonly BlogDataCache<Blog> _cache;

    public BlogsCache(IBlogStore store, TimeProvider time)
    {
        _store = store;
        _time = time;
        _cache = new BlogDataCache<Blog>(_ => LoadAsync());
    }

    public Task<IReadOnlyList<Blog>> GetAllAsync(CancellationToken cancellationToken = default) =>
        _cache.GetAsync(AllBlogsKey, cancellationToken);

    public Task SaveAsync(Blog blog, CancellationToken cancellationToken = default) =>
        _cache.WriteAsync(AllBlogsKey, async blogs =>
        {
            await _store.SaveAsync(blog, CancellationToken.None).ConfigureAwait(false);
            return BlogDataCache<Blog>.Upsert(blogs, blog, b => b.Id == blog.Id);
        }, cancellationToken);

    public Task DeleteAsync(Guid blogId, CancellationToken cancellationToken = default) =>
        _cache.WriteAsync(AllBlogsKey, async blogs =>
        {
            await _store.DeleteAsync(blogId, CancellationToken.None).ConfigureAwait(false);
            return BlogDataCache<Blog>.Remove(blogs, b => b.Id == blogId);
        }, cancellationToken);

    private async Task<IReadOnlyList<Blog>> LoadAsync()
    {
        var blogs = (await _store.GetAllAsync().ConfigureAwait(false)).Where(b => !b.IsDeleted).ToList();
        if (blogs.Count == 0)
        {
            var now = _time.GetUtcNow().UtcDateTime;
            var primary = new Blog { Name = "Primary", IsPrimary = true, VirtualPath = "~/", DateCreated = now, DateModified = now };
            await _store.SaveAsync(primary).ConfigureAwait(false);
            blogs.Add(primary);
        }

        return blogs;
    }
}

public sealed class BlogService(
    BlogsCache blogs,
    IEnumerable<IBlogScopedCache> blogCaches,
    IRightsService rights,
    IBlogClock clock,
    IEventPublisher events) : IBlogService
{
    public async Task<IReadOnlyList<Blog>> GetBlogsAsync(CancellationToken cancellationToken = default) =>
        BlogResolver.SortForMatching(await blogs.GetAllAsync(cancellationToken).ConfigureAwait(false));

    public async Task<Blog?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await blogs.GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(b => b.Id == id);

    public async Task<Blog?> GetPrimaryAsync(CancellationToken cancellationToken = default) =>
        (await blogs.GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(b => b.IsPrimary);

    public async Task<Blog?> ResolveAsync(string host, string path, Guid? explicitBlogId = null, CancellationToken cancellationToken = default) =>
        BlogResolver.Resolve(await blogs.GetAllAsync(cancellationToken).ConfigureAwait(false), host, path, explicitBlogId);

    public async Task SaveAsync(Blog blog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blog);
        DemandAdministrator();
        BlogValidationException.ThrowIfInvalid(("Name", string.IsNullOrWhiteSpace(blog.Name), "Il nome del blog è obbligatorio."));

        var all = await blogs.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var isNew = all.All(b => b.Id != blog.Id);
        if (isNew)
        {
            blog.DateCreated = clock.UtcNow;
        }

        blog.DateModified = clock.UtcNow;
        var action = isNew ? SaveAction.Insert : SaveAction.Update;
        await events.PublishAsync(new SavingEvent<Blog> { BlogId = blog.Id, Entity = blog, Action = action }, cancellationToken).ConfigureAwait(false);

        if (blog.IsPrimary)
        {
            // Un solo blog principale.
            foreach (var other in all.Where(b => b.IsPrimary && b.Id != blog.Id))
            {
                other.IsPrimary = false;
                await blogs.SaveAsync(other, cancellationToken).ConfigureAwait(false);
            }
        }

        await blogs.SaveAsync(blog, cancellationToken).ConfigureAwait(false);
        await events.PublishAsync(new SavedEvent<Blog> { BlogId = blog.Id, Entity = blog, Action = action }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Blog blog, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(blog);
        DemandAdministrator();
        if (blog.IsPrimary)
        {
            throw new InvalidOperationException("Il blog principale non può essere eliminato.");
        }

        await events.PublishAsync(new SavingEvent<Blog> { BlogId = blog.Id, Entity = blog, Action = SaveAction.Delete }, cancellationToken).ConfigureAwait(false);
        await blogs.DeleteAsync(blog.Id, cancellationToken).ConfigureAwait(false);
        foreach (var cache in blogCaches)
        {
            cache.Invalidate(blog.Id);
        }

        await events.PublishAsync(new SavedEvent<Blog> { BlogId = blog.Id, Entity = blog, Action = SaveAction.Delete }, cancellationToken).ConfigureAwait(false);
    }

    private void DemandAdministrator()
    {
        if (!rights.IsAdministrator)
        {
            throw new UnauthorizedAccessException();
        }
    }
}

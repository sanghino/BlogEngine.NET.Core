using System.Collections.Concurrent;
using BlogEngine.Core.Caching;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Text;

namespace BlogEngine.Core.Services;

/// <summary>
/// Autore e stato di pubblicazione dei post così come sono memorizzati. Serve perché i post in cache sono
/// oggetti condivisi: se il chiamante li modifica prima di salvarli, il servizio deve comunque sapere
/// chi era l'autore e se il post era già pubblico (per i permessi e per l'evento di pubblicazione).
/// </summary>
public sealed class PostStateTracker
{
    private readonly ConcurrentDictionary<Guid, (string Author, bool IsPublished)> _states = new();

    public (string Author, bool IsPublished) GetOrAdd(Post post) => _states.GetOrAdd(post.Id, _ => (post.Author, post.IsPublished));

    public void Set(Post post) => _states[post.Id] = (post.Author, post.IsPublished);

    public void Remove(Guid postId) => _states.TryRemove(postId, out _);
}

public sealed class PostService(
    EntityRepository<Post> posts,
    PostStateTracker states,
    IBlogContext context,
    IRightsService rights,
    IBlogClock clock,
    IEventPublisher events) : IPostService
{
    /// <summary>Numero massimo di suffissi provati per rendere univoco uno slug, come nella versione originale.</summary>
    private const int MaxSlugAttempts = 100;

    public async Task<IReadOnlyList<Post>> GetPostsAsync(CancellationToken cancellationToken = default)
    {
        // Ogni blog mostra solo i propri post: il blog principale non include quelli dei blog figli.
        var result = await GetBlogPostsAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false);
        return result.OrderByDescending(p => p.DateCreated).ToList();
    }

    public async Task<IReadOnlyList<Post>> GetVisiblePostsAsync(CancellationToken cancellationToken = default) =>
        await FilterVisibleAsync(await GetPostsAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

    public async Task<IReadOnlyList<Post>> GetDeletedPostsAsync(CancellationToken cancellationToken = default) =>
        (await posts.GetAllAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false))
            .Where(p => p.IsDeleted)
            .OrderByDescending(p => p.DateModified)
            .ToList();

    public async Task<Post?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        (await GetPostsAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(p => p.Id == id)
        ?? (await posts.FindAsync(context.Blog.Id, id, cancellationToken).ConfigureAwait(false));

    public async Task<Post?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var candidates = await GetBlogPostsAsync(context.Blog.Id, cancellationToken).ConfigureAwait(false);
        return candidates.FirstOrDefault(p => Slug.AreEquivalent(p.Slug, slug));
    }

    public async Task<IReadOnlyList<Post>> GetByAuthorAsync(string author, CancellationToken cancellationToken = default) =>
        (await GetVisiblePostsAsync(cancellationToken).ConfigureAwait(false))
            .Where(p => Slug.AreEquivalent(p.Author, author))
            .ToList();

    public async Task<IReadOnlyList<Post>> GetByCategoryAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
        (await GetVisiblePostsAsync(cancellationToken).ConfigureAwait(false))
            .Where(p => p.Categories.Contains(categoryId))
            .ToList();

    public async Task<IReadOnlyList<Post>> GetByTagAsync(string tag, CancellationToken cancellationToken = default) =>
        (await GetVisiblePostsAsync(cancellationToken).ConfigureAwait(false))
            .Where(p => p.Tags.Any(t => Slug.AreEquivalent(t, tag)))
            .ToList();

    public async Task<IReadOnlyList<Post>> GetByDateAsync(DateTime from, DateTime to, CancellationToken cancellationToken = default) =>
        (await GetVisiblePostsAsync(cancellationToken).ConfigureAwait(false))
            .Where(p => p.DateCreated.Date >= from.Date && p.DateCreated.Date <= to.Date)
            .ToList();

    public async Task<(Post? Previous, Post? Next)> GetAdjacentAsync(Post post, CancellationToken cancellationToken = default)
    {
        var sameBlog = await FilterVisibleAsync(await GetBlogPostsAsync(post.BlogId, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        var index = sameBlog.ToList().FindIndex(p => p.Id == post.Id);
        if (index < 0)
        {
            return (null, null);
        }

        // L'elenco è ordinato dal più recente: il successivo sta prima, il precedente dopo.
        var next = index > 0 ? sameBlog[index - 1] : null;
        var previous = index < sameBlog.Count - 1 ? sameBlog[index + 1] : null;
        return (previous, next);
    }

    public async Task SaveAsync(Post post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        BlogValidationException.ThrowIfInvalid(
            ("Title", string.IsNullOrWhiteSpace(post.Title), "Il titolo è obbligatorio."),
            ("Content", string.IsNullOrWhiteSpace(post.Content), "Il contenuto è obbligatorio."));

        if (post.BlogId == Guid.Empty)
        {
            post.BlogId = context.Blog.Id;
        }

        if (string.IsNullOrWhiteSpace(post.Author))
        {
            post.Author = context.UserName ?? string.Empty;
        }

        var existing = await posts.FindAsync(post.BlogId, post.Id, cancellationToken).ConfigureAwait(false);
        var isNew = existing is null;
        var stored = isNew ? default : states.GetOrAdd(existing!);

        if (isNew)
        {
            await DemandAsync(Rights.CreateNewPosts, cancellationToken).ConfigureAwait(false);
        }
        else if (!await CanEditAuthorAsync(stored.Author, cancellationToken).ConfigureAwait(false)
                 || !await CanEditAuthorAsync(post.Author, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }

        var isBeingPublished = post.IsPublished && !post.IsDeleted && (isNew || !stored.IsPublished);
        if (isBeingPublished && !await CanPublishAsync(post.Author, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }

        var now = clock.UtcNow;
        if (post.DateCreated == default)
        {
            post.DateCreated = now;
        }

        post.DateModified = now;
        post.Slug = await GetUniqueSlugAsync(post, cancellationToken).ConfigureAwait(false);
        post.Tags = post.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var action = isNew ? SaveAction.Insert : SaveAction.Update;
        await events.PublishAsync(new SavingEvent<Post> { BlogId = post.BlogId, Entity = post, Action = action }, cancellationToken).ConfigureAwait(false);

        if (isBeingPublished)
        {
            var publishing = new PostPublishingEvent { BlogId = post.BlogId, Post = post };
            await events.PublishAsync(publishing, cancellationToken).ConfigureAwait(false);
            if (publishing.Cancel)
            {
                post.IsPublished = false;
                isBeingPublished = false;
            }
        }

        await posts.SaveAsync(post, cancellationToken).ConfigureAwait(false);
        states.Set(post);

        await events.PublishAsync(new SavedEvent<Post> { BlogId = post.BlogId, Entity = post, Action = action }, cancellationToken).ConfigureAwait(false);
        if (isBeingPublished)
        {
            await events.PublishAsync(new PostPublishedEvent { BlogId = post.BlogId, Post = post }, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task DeleteAsync(Post post, CancellationToken cancellationToken = default)
    {
        await DemandDeleteAsync(post, cancellationToken).ConfigureAwait(false);
        await events.PublishAsync(new SavingEvent<Post> { BlogId = post.BlogId, Entity = post, Action = SaveAction.Delete }, cancellationToken).ConfigureAwait(false);

        post.IsDeleted = true;
        post.DateModified = clock.UtcNow;
        await posts.SaveAsync(post, cancellationToken).ConfigureAwait(false);

        await events.PublishAsync(new SavedEvent<Post> { BlogId = post.BlogId, Entity = post, Action = SaveAction.Delete }, cancellationToken).ConfigureAwait(false);
    }

    public async Task RestoreAsync(Post post, CancellationToken cancellationToken = default)
    {
        await DemandDeleteAsync(post, cancellationToken).ConfigureAwait(false);
        post.IsDeleted = false;
        post.DateModified = clock.UtcNow;
        await posts.SaveAsync(post, cancellationToken).ConfigureAwait(false);
    }

    public async Task PurgeAsync(Post post, CancellationToken cancellationToken = default)
    {
        await DemandDeleteAsync(post, cancellationToken).ConfigureAwait(false);
        await posts.DeleteAsync(post.BlogId, post.Id, cancellationToken).ConfigureAwait(false);
        states.Remove(post.Id);
    }

    public async Task RateAsync(Post post, int rating, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentOutOfRangeException.ThrowIfLessThan(rating, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rating, 5);
        await DemandAsync(Rights.SubmitRatingsOnPosts, cancellationToken).ConfigureAwait(false);

        post.AddRating(rating);
        await posts.SaveAsync(post, cancellationToken).ConfigureAwait(false);
        await events.PublishAsync(new PostRatedEvent { BlogId = post.BlogId, Post = post, Rating = rating }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> IsVisibleAsync(Post post, CancellationToken cancellationToken = default) =>
        !post.IsDeleted && (IsPublic(post) || await rights.IsAuthorizedAsync(Rights.ViewUnpublishedPosts, cancellationToken).ConfigureAwait(false));

    public Task<bool> CanEditAsync(Post post, CancellationToken cancellationToken = default) =>
        CanEditAuthorAsync(states.GetOrAdd(post).Author, cancellationToken);

    public Task<bool> CanDeleteAsync(Post post, CancellationToken cancellationToken = default) =>
        IsOwn(states.GetOrAdd(post).Author)
            ? rights.IsAuthorizedAsync(Rights.DeleteOwnPosts, cancellationToken)
            : rights.IsAuthorizedAsync(Rights.DeleteOtherUsersPosts, cancellationToken);

    public Task<bool> CanPublishAsync(string author, CancellationToken cancellationToken = default) =>
        IsOwn(author)
            ? rights.IsAuthorizedAsync(Rights.PublishOwnPosts, cancellationToken)
            : rights.IsAuthorizedAsync(Rights.PublishOtherUsersPosts, cancellationToken);

    /// <summary>Post non eliminati di un singolo blog, dal più recente.</summary>
    private async Task<IReadOnlyList<Post>> GetBlogPostsAsync(Guid blogId, CancellationToken cancellationToken) =>
        (await posts.GetAllAsync(blogId, cancellationToken).ConfigureAwait(false))
            .Where(p => !p.IsDeleted)
            .OrderByDescending(p => p.DateCreated)
            .ToList();

    private async Task<IReadOnlyList<Post>> FilterVisibleAsync(IReadOnlyList<Post> list, CancellationToken cancellationToken)
    {
        var canSeeUnpublished = await rights.IsAuthorizedAsync(Rights.ViewUnpublishedPosts, cancellationToken).ConfigureAwait(false);
        return list.Where(p => !p.IsDeleted && (canSeeUnpublished || IsPublic(p))).ToList();
    }

    /// <summary>Pubblicato e con data non futura (i post con data futura sono programmati).</summary>
    private bool IsPublic(Post post) => post.IsPublished && !post.IsDeleted && post.DateCreated <= clock.UtcNow;

    private bool IsOwn(string author) =>
        context.UserName is { } user && string.Equals(user, author, StringComparison.OrdinalIgnoreCase);

    private Task<bool> CanEditAuthorAsync(string author, CancellationToken cancellationToken) =>
        IsOwn(author)
            ? rights.IsAuthorizedAsync(Rights.EditOwnPosts, cancellationToken)
            : rights.IsAuthorizedAsync(Rights.EditOtherUsersPosts, cancellationToken);

    private async Task DemandDeleteAsync(Post post, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(post);
        if (!await CanDeleteAsync(post, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }
    }

    private async Task DemandAsync(Rights right, CancellationToken cancellationToken)
    {
        if (!await rights.IsAuthorizedAsync(right, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }
    }

    /// <summary>Slug dal titolo se mancante, reso univoco nel blog aggiungendo un numero.</summary>
    private async Task<string> GetUniqueSlugAsync(Post post, CancellationToken cancellationToken)
    {
        var baseSlug = Slug.Create(string.IsNullOrWhiteSpace(post.Slug) ? post.Title : post.Slug);
        if (baseSlug.Length == 0)
        {
            baseSlug = post.Id.ToString("N");
        }

        var others = (await posts.GetAllAsync(post.BlogId, cancellationToken).ConfigureAwait(false))
            .Where(p => p.Id != post.Id)
            .Select(p => p.Slug)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidate = baseSlug;
        for (var i = 1; others.Contains(candidate) && i <= MaxSlugAttempts; i++)
        {
            candidate = $"{baseSlug}{i}";
        }

        return candidate;
    }

}

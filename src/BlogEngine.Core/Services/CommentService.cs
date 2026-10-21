using BlogEngine.Core.Caching;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

public sealed class CommentService(
    EntityRepository<Post> posts,
    IBlogContext context,
    IRightsService rights,
    ISettingsService settings,
    IBlogClock clock,
    IEnumerable<ICommentSpamFilter> spamFilters,
    IEventPublisher events) : ICommentService
{
    public async Task<bool> AddAsync(Post post, Comment comment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(comment);
        await DemandAsync(Rights.CreateComments, cancellationToken).ConfigureAwait(false);
        if (!await AreCommentsOpenAsync(post, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("I commenti di questo post sono chiusi.");
        }

        if (comment.Id == Guid.Empty)
        {
            comment.Id = Guid.NewGuid();
        }

        comment.PostId = post.Id;
        comment.BlogId = post.BlogId;
        if (comment.DateCreated == default)
        {
            comment.DateCreated = clock.UtcNow;
        }

        // La moderazione (regole, filtri e antispam) avviene nel gestore CommentModerationHandler.
        if (!await RaiseChangingAsync(post, comment, CommentAction.Add, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        post.AllComments.Add(comment);
        await SavePostAsync(post, cancellationToken).ConfigureAwait(false);
        await RaiseChangedAsync(post, comment, CommentAction.Add, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> UpdateAsync(Post post, Comment comment, CancellationToken cancellationToken = default)
    {
        var stored = await FindModeratedAsync(post, comment, cancellationToken).ConfigureAwait(false);
        if (!await RaiseChangingAsync(post, comment, CommentAction.Update, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (!ReferenceEquals(stored, comment))
        {
            stored.Author = comment.Author;
            stored.Email = comment.Email;
            stored.Website = comment.Website;
            stored.Country = comment.Country;
            stored.IP = comment.IP;
            stored.Content = comment.Content;
            stored.IsApproved = comment.IsApproved;
            stored.IsSpam = comment.IsSpam;
            stored.IsDeleted = comment.IsDeleted;
            stored.ModeratedBy = comment.ModeratedBy;
        }

        await SavePostAsync(post, cancellationToken).ConfigureAwait(false);
        await RaiseChangedAsync(post, stored, CommentAction.Update, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> ApproveAsync(Post post, Comment comment, CancellationToken cancellationToken = default)
    {
        var stored = await FindModeratedAsync(post, comment, cancellationToken).ConfigureAwait(false);
        if (!await RaiseChangingAsync(post, stored, CommentAction.Approve, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        // Un commento scartato come spam da un filtro e poi approvato è un errore del filtro.
        if (stored.IsSpam)
        {
            await ReportMistakeAsync(post, stored, cancellationToken).ConfigureAwait(false);
        }

        stored.IsApproved = true;
        stored.IsSpam = false;
        stored.ModeratedBy = context.UserName ?? stored.ModeratedBy;
        await SavePostAsync(post, cancellationToken).ConfigureAwait(false);
        await RaiseChangedAsync(post, stored, CommentAction.Approve, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task ApproveAllAsync(Post post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        foreach (var comment in post.NotApprovedComments.ToList())
        {
            await ApproveAsync(post, comment, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> DisapproveAsync(Post post, Comment comment, CancellationToken cancellationToken = default)
    {
        var stored = await FindModeratedAsync(post, comment, cancellationToken).ConfigureAwait(false);
        if (!await RaiseChangingAsync(post, stored, CommentAction.Disapprove, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        // Uno spam lasciato passare dai filtri è un errore dei filtri.
        if (stored.IsApproved)
        {
            await ReportMistakeAsync(post, stored, cancellationToken).ConfigureAwait(false);
        }

        stored.IsApproved = false;
        stored.IsSpam = true;
        stored.ModeratedBy = context.UserName ?? stored.ModeratedBy;
        await SavePostAsync(post, cancellationToken).ConfigureAwait(false);
        await RaiseChangedAsync(post, stored, CommentAction.Disapprove, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public Task<bool> RemoveAsync(Post post, Comment comment, CancellationToken cancellationToken = default) =>
        SetDeletedAsync(post, comment, deleted: true, CommentAction.Remove, cancellationToken);

    public Task<bool> RestoreAsync(Post post, Comment comment, CancellationToken cancellationToken = default) =>
        SetDeletedAsync(post, comment, deleted: false, CommentAction.Restore, cancellationToken);

    public async Task<bool> PurgeAsync(Post post, Comment comment, CancellationToken cancellationToken = default)
    {
        var stored = await FindModeratedAsync(post, comment, cancellationToken).ConfigureAwait(false);
        if (!await RaiseChangingAsync(post, stored, CommentAction.Purge, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        post.AllComments.Remove(stored);
        await SavePostAsync(post, cancellationToken).ConfigureAwait(false);
        await RaiseChangedAsync(post, stored, CommentAction.Purge, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> AreCommentsOpenAsync(Post post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        var blogSettings = await settings.GetAsync(post.BlogId, cancellationToken).ConfigureAwait(false);
        if (!blogSettings.IsCommentsEnabled || !post.HasCommentsEnabled)
        {
            return false;
        }

        return blogSettings.DaysCommentsAreEnabled <= 0
            || post.DateCreated.AddDays(blogSettings.DaysCommentsAreEnabled) >= clock.UtcNow;
    }

    public async Task<IReadOnlyList<CommentNode>> GetCommentTreeAsync(Post post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        var blogSettings = await settings.GetAsync(post.BlogId, cancellationToken).ConfigureAwait(false);
        var comments = await rights.IsAuthorizedAsync(Rights.ViewUnmoderatedComments, cancellationToken).ConfigureAwait(false)
            ? post.Comments.Where(c => !c.IsSpam && !c.IsPingbackOrTrackback)
            : post.ApprovedComments(blogSettings.EnableCommentsModeration);

        var nodes = comments.OrderBy(c => c.DateCreated).Select(c => new CommentNode(c)).ToList();
        if (!blogSettings.IsCommentNestingEnabled)
        {
            return nodes;
        }

        var byId = nodes.ToDictionary(n => n.Comment.Id);
        var roots = new List<CommentNode>();
        foreach (var node in nodes)
        {
            // Una risposta il cui commento padre non è visibile viene mostrata al primo livello.
            if (node.Comment.ParentId != Guid.Empty && byId.TryGetValue(node.Comment.ParentId, out var parent) && parent != node)
            {
                parent.Replies.Add(node);
            }
            else
            {
                roots.Add(node);
            }
        }

        return roots;
    }

    private async Task<bool> SetDeletedAsync(Post post, Comment comment, bool deleted, CommentAction action, CancellationToken cancellationToken)
    {
        var stored = await FindModeratedAsync(post, comment, cancellationToken).ConfigureAwait(false);
        if (!await RaiseChangingAsync(post, stored, action, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        stored.IsDeleted = deleted;
        await SavePostAsync(post, cancellationToken).ConfigureAwait(false);
        await RaiseChangedAsync(post, stored, action, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>Verifica il permesso di moderazione e restituisce il commento memorizzato nel post.</summary>
    private async Task<Comment> FindModeratedAsync(Post post, Comment comment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(post);
        ArgumentNullException.ThrowIfNull(comment);
        await DemandAsync(Rights.ModerateComments, cancellationToken).ConfigureAwait(false);
        return post.AllComments.FirstOrDefault(c => c.Id == comment.Id)
            ?? throw new KeyNotFoundException($"Il commento {comment.Id} non appartiene al post {post.Id}.");
    }

    private async Task ReportMistakeAsync(Post post, Comment comment, CancellationToken cancellationToken)
    {
        var blogSettings = await settings.GetAsync(post.BlogId, cancellationToken).ConfigureAwait(false);
        if (!blogSettings.CommentReportMistakes)
        {
            return;
        }

        // Se il commento era stato approvato in automatico hanno sbagliato tutti i filtri.
        var missedByAll = comment.ModeratedBy == CommentModerationHandler.AutoModerator;
        foreach (var filter in spamFilters.Where(f => missedByAll || f.Name == comment.ModeratedBy))
        {
            await filter.ReportMistakeAsync(post, comment, cancellationToken).ConfigureAwait(false);
        }
    }

    private Task SavePostAsync(Post post, CancellationToken cancellationToken)
    {
        // Un commento non cambia la data di modifica del post, come nella versione originale per i nuovi commenti.
        return posts.SaveAsync(post, cancellationToken);
    }

    private async Task<bool> RaiseChangingAsync(Post post, Comment comment, CommentAction action, CancellationToken cancellationToken)
    {
        var changing = new CommentChangingEvent { BlogId = post.BlogId, Post = post, Comment = comment, Action = action };
        await events.PublishAsync(changing, cancellationToken).ConfigureAwait(false);
        return !changing.Cancel;
    }

    private Task RaiseChangedAsync(Post post, Comment comment, CommentAction action, CancellationToken cancellationToken) =>
        events.PublishAsync(new CommentChangedEvent { BlogId = post.BlogId, Post = post, Comment = comment, Action = action }, cancellationToken);

    private async Task DemandAsync(Rights right, CancellationToken cancellationToken)
    {
        if (!await rights.IsAuthorizedAsync(right, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }
    }
}

using BlogEngine.Core.Caching;
using BlogEngine.Core.Data;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Services;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Core.Services;

/// <summary>
/// Moderazione automatica dei nuovi commenti (porting di CommentHandlers.PostAddingComment).
/// Nell'ordine:
/// 1. senza moderazione il commento è approvato ("Auto");
/// 2. regole: utenti autenticati fidati, lista bianca o nera (10 commenti approvati o respinti con lo stesso email o IP);
/// 3. filtri dell'amministratore (consenti, blocca, elimina);
/// 4. filtri antispam esterni, fino al primo che riconosce lo spam.
/// Viene eseguito prima di tutti gli altri gestori, che vedono quindi la decisione già presa.
/// </summary>
public sealed class CommentModerationHandler(
    EntityRepository<Post> posts,
    ICommentFilterStore filterStore,
    IBlogContext context,
    ISettingsService settings,
    IEnumerable<ICommentSpamFilter> spamFilters,
    ILogger<CommentModerationHandler> logger) : IEventHandler<CommentChangingEvent>
{
    public const string AutoModerator = "Auto";

    /// <summary>Commenti con lo stesso email o IP oltre i quali l'autore è considerato fidato o spammer.</summary>
    private const int ListThreshold = 10;

    public int Order => int.MinValue;

    public async Task HandleAsync(CommentChangingEvent blogEvent, CancellationToken cancellationToken)
    {
        if (blogEvent.Action != CommentAction.Add)
        {
            return;
        }

        var comment = blogEvent.Comment;
        var blogSettings = await settings.GetAsync(blogEvent.BlogId, cancellationToken).ConfigureAwait(false);
        if (!blogSettings.EnableCommentsModeration)
        {
            comment.IsApproved = true;
            comment.ModeratedBy = AutoModerator;
        }

        if (blogSettings.TrustAuthenticatedUsers && context.IsAuthenticated)
        {
            Approve(comment, "Rule:authenticated");
            return;
        }

        if (await ApplyListsAsync(blogEvent.BlogId, comment, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var rules = await filterStore.GetRulesAsync(blogEvent.BlogId, cancellationToken).ConfigureAwait(false);
        if (rules.FirstOrDefault(r => Matches(r, comment)) is { } rule)
        {
            switch (rule.Action)
            {
                case CommentFilterAction.Delete:
                    comment.IsApproved = false;
                    comment.ModeratedBy = "Delete";
                    blogEvent.Cancel = true;
                    break;
                case CommentFilterAction.Block:
                    MarkSpam(comment, "Filter");
                    break;
                default:
                    Approve(comment, "Filter");
                    break;
            }

            return;
        }

        foreach (var filter in spamFilters.OrderBy(f => f.Priority).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (await filter.CheckAsync(blogEvent.Post, comment, cancellationToken).ConfigureAwait(false) == SpamCheckResult.Spam)
            {
                MarkSpam(comment, filter.Name);
                logger.LogInformation("Il filtro {Filter} ha classificato come spam il commento {CommentId}.", filter.Name, comment.Id);
                break;
            }
        }
    }

    public static bool Matches(CommentFilterRule rule, Comment comment)
    {
        var filter = rule.Filter.Trim();
        if (filter.Length == 0)
        {
            return false;
        }

        var value = rule.Subject switch
        {
            CommentFilterSubject.IP => comment.IP,
            CommentFilterSubject.Author => comment.Author,
            CommentFilterSubject.Website => comment.Website?.ToString() ?? string.Empty,
            CommentFilterSubject.Email => comment.Email,
            _ => comment.Content,
        };

        return rule.Operator == CommentFilterOperator.Equals
            ? value.Equals(filter, StringComparison.OrdinalIgnoreCase)
            : value.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> ApplyListsAsync(Guid blogId, Comment comment, CancellationToken cancellationToken)
    {
        var approved = 0;
        var rejected = 0;
        foreach (var previous in (await posts.GetAllAsync(blogId, cancellationToken).ConfigureAwait(false)).SelectMany(p => p.Comments))
        {
            var sameAuthor = (comment.Email.Length > 0 && previous.Email.Equals(comment.Email, StringComparison.OrdinalIgnoreCase))
                || (comment.IP.Length > 0 && previous.IP == comment.IP);
            if (!sameAuthor)
            {
                continue;
            }

            if (previous.IsApproved)
            {
                approved++;
            }
            else
            {
                rejected++;
            }
        }

        if (approved >= ListThreshold)
        {
            Approve(comment, "Rule:white list");
            return true;
        }

        if (rejected >= ListThreshold)
        {
            MarkSpam(comment, "Rule:black list");
            return true;
        }

        return false;
    }

    private static void Approve(Comment comment, string moderatedBy)
    {
        comment.IsApproved = true;
        comment.IsSpam = false;
        comment.ModeratedBy = moderatedBy;
    }

    private static void MarkSpam(Comment comment, string moderatedBy)
    {
        comment.IsApproved = false;
        comment.IsSpam = true;
        comment.ModeratedBy = moderatedBy;
    }
}

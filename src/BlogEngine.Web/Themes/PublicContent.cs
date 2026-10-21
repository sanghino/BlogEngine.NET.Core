using System.Globalization;
using System.Net;
using System.Text;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Settings;
using BlogEngine.Text;
using Microsoft.AspNetCore.Html;

namespace BlogEngine.Web.Themes;

/// <summary>
/// Trasforma post, commenti e pagine negli oggetti per i template (porting di PostViewBase,
/// CommentViewBase e page.aspx): testo elaborato dalle estensioni, link e comandi di amministrazione.
/// </summary>
public sealed class PublicContent(
    SiteRenderer site,
    IBlogContext context,
    IRightsService rights,
    IPostService posts,
    ICommentService comments,
    ICategoryService categories,
    IProfileService profiles,
    IAvatarService avatars,
    ILinkGenerator links,
    IBlogClock clock,
    IEventPublisher events)
{
    private readonly Dictionary<Rights, bool> _rights = [];

    public async Task<bool> CanAsync(Rights right)
    {
        if (!_rights.TryGetValue(right, out var allowed))
        {
            allowed = await rights.IsAuthorizedAsync(right);
            _rights[right] = allowed;
        }

        return allowed;
    }

    /// <summary>
    /// Dati del post per i template; null se un'estensione ne ha annullato la visualizzazione.
    /// Negli elenchi, se previsto dalle impostazioni, il testo è sostituito dal riassunto.
    /// </summary>
    public async Task<PostModel?> PostAsync(Post post, int index, ServingLocation location, ServingContentBy contentBy, bool showExcerpt)
    {
        var blogSettings = await site.GetSettingsAsync();
        var url = await links.PostAsync(post);
        var body = post.Content;
        if (showExcerpt)
        {
            var more = $" <a href=\"{WebUtility.HtmlEncode(url)}\">[{WebUtility.HtmlEncode(site.T("more"))}]</a>";
            var length = contentBy is ServingContentBy.Tag or ServingContentBy.Category
                ? blogSettings.DescriptionCharactersForPostsByTagOrCategory
                : blogSettings.DescriptionCharacters;

            if (!string.IsNullOrWhiteSpace(post.Description))
            {
                body = WebUtility.HtmlEncode(WebUtility.HtmlDecode(post.Description)).Replace("\n", "<br />", StringComparison.Ordinal) + more;
            }
            else
            {
                var text = HtmlText.ToPlainText(post.Content);
                body = WebUtility.HtmlEncode(length > 0 && text.Length > length ? $"{text[..length]}..." : text) + more;
            }
        }

        var serving = new ServingEvent<Post> { BlogId = post.BlogId, Item = post, Body = body, Location = location, ContentBy = contentBy };
        await events.PublishAsync(serving);
        if (serving.Cancel)
        {
            return null;
        }

        var profile = await profiles.GetAsync(post.Author);
        var categoryLinks = new List<LinkModel>();
        foreach (var id in post.Categories)
        {
            if (await categories.GetByIdAsync(id) is { } category)
            {
                categoryLinks.Add(new LinkModel { Title = category.Title, Url = await links.CategoryAsync(category), FeedUrl = await links.CategoryFeedAsync(category) });
            }
        }

        var appRoot = (await site.GetScopeAsync()).AppRoot;
        var firstImage = HtmlText.FirstImageSource(post.Content) ?? string.Empty;
        // Sul post singolo il testo elaborato (con i campi personalizzati già inseriti) è anche la base della descrizione.
        var source = !string.IsNullOrWhiteSpace(post.Description) ? post.Description : location == ServingLocation.SinglePost ? serving.Body : post.Content;
        var plain = HtmlText.ToPlainText(source);
        return new PostModel
        {
            Id = post.Id.ToString(),
            Index = index,
            Title = post.Title,
            Slug = post.Slug,
            Url = url,
            AbsoluteUrl = links.ToAbsolute(url).ToString(),
            Author = post.Author,
            AuthorName = string.IsNullOrWhiteSpace(profile?.DisplayName) ? post.Author : profile.DisplayName,
            AuthorUrl = links.Author(post.Author),
            DateCreated = await clock.ToBlogTimeAsync(post.DateCreated),
            DateModified = await clock.ToBlogTimeAsync(post.DateModified),
            Body = new HtmlString(serving.Body),
            Content = new HtmlString(post.Content),
            // La descrizione è testo semplice: le entità salvate dall'editor (es. &egrave;) diventano caratteri.
            Description = WebUtility.HtmlDecode(post.Description),
            PlainDescription = plain,
            FirstImage = firstImage,
            AbsoluteFirstImage = firstImage.Length == 0 || firstImage.Contains("://", StringComparison.Ordinal)
                ? firstImage
                : links.ToAbsolute(firstImage.StartsWith('/') ? firstImage : appRoot + firstImage).ToString(),
            Categories = categoryLinks,
            Tags = post.Tags.Select(t => new LinkModel { Title = t, Url = links.Tag(t) }).ToList(),
            CommentsCount = post.ApprovedComments(blogSettings.EnableCommentsModeration).Count(c => !c.IsPingbackOrTrackback),
            CommentsEnabled = await comments.AreCommentsOpenAsync(post),
            IsPublished = post.IsPublished,
            Rating = Math.Round(post.Rating, 1),
            Raters = post.Raters,
            RatingHtml = blogSettings.EnableRating
                ? new HtmlString($"<div class=\"ratingcontainer\" style=\"visibility:hidden\">{post.Id}|{post.Raters}|{post.Rating.ToString("0.0", CultureInfo.InvariantCulture)}|{post.BlogId}</div>")
                : HtmlString.Empty,
            AdminLinks = await PostAdminLinksAsync(post, url),
            FeedUrl = $"{links.BlogRoot}syndication.axd?post={post.Id}",
            EditUrl = await posts.CanEditAsync(post) ? $"{links.BlogRoot}admin/app/editor/editpost.cshtml?id={post.Id}" : string.Empty,
        };
    }

    /// <summary>Comandi per chi può modificare il post (porting di PostViewBase.AdminLinks).</summary>
    private async Task<HtmlString> PostAdminLinksAsync(Post post, string url)
    {
        if (string.IsNullOrEmpty(context.UserName))
        {
            return HtmlString.Empty;
        }

        var items = new List<string>();
        var pending = post.NotApprovedComments.Count();
        if (pending > 0 && await CanAsync(Rights.ModerateComments))
        {
            items.Add($"<a href=\"{E(url)}#comment\">{E(site.T("unapprovedcomments"))} ({pending})</a>");
            items.Add($"<a href=\"#\" onclick=\"return BlogEngine.approveAllComments('{post.Id}');\">{E(site.T("approveallcomments"))}</a>");
        }

        if (await posts.CanEditAsync(post))
        {
            items.Add($"<a href=\"{E(links.BlogRoot)}admin/app/editor/editpost.cshtml?id={post.Id}\">{E(site.T("edit"))}</a>");
        }

        if (await posts.CanDeleteAsync(post))
        {
            items.Add($"<a href=\"#\" onclick=\"return BlogEngine.deletePost('{post.Id}');\">{E(site.T("delete"))}</a>");
        }

        return new HtmlString(string.Join(" | ", items));
    }

    /// <summary>Commenti visibili del post, già organizzati in risposte se l'annidamento è attivo.</summary>
    public async Task<CommentsModel> CommentsAsync(Post post, PostModel model)
    {
        var blogSettings = await site.GetSettingsAsync();
        var tree = await comments.GetCommentTreeAsync(post);
        var open = model.CommentsEnabled && blogSettings.IsCommentsEnabled && await CanAsync(Rights.CreateComments);
        var items = new List<CommentModel>();
        foreach (var node in tree)
        {
            if (await CommentAsync(post, node, open) is { } item)
            {
                items.Add(item);
            }
        }

        var pingbacks = new List<CommentModel>();
        foreach (var pingback in post.Pingbacks.Where(c => c.IsApproved && !c.IsSpam))
        {
            if (await CommentAsync(post, new CommentNode(pingback), false) is { } item)
            {
                pingbacks.Add(item);
            }
        }

        return new CommentsModel
        {
            Items = items,
            Pingbacks = pingbacks,
            Count = Count(items),
            IsOpen = open && blogSettings.CommentProvider == CommentProvider.BlogEngine,
            NestingEnabled = blogSettings.IsCommentNestingEnabled,
            Moderation = blogSettings.EnableCommentsModeration && !(blogSettings.TrustAuthenticatedUsers && !string.IsNullOrEmpty(context.UserName)),
            ShowWebsite = blogSettings.EnableWebsiteInComments,
            ShowCountry = blogSettings.EnableCountryInComments,
            ShowLivePreview = blogSettings.ShowLivePreview,
            Provider = blogSettings.CommentProvider.ToString().ToLowerInvariant(),
            DisqusWebsiteName = blogSettings.DisqusWebsiteName,
            DisqusDevMode = blogSettings.DisqusDevMode,
            FacebookLanguage = blogSettings.FacebookLanguage,
            FacebookAppId = blogSettings.FacebookAppId,
            PostUrl = $"{links.BlogRoot}comment",
        };

        static int Count(IEnumerable<CommentModel> list) => list.Sum(c => 1 + Count(c.Replies));
    }

    /// <summary>Un commento con le sue risposte (porting di CommentViewBase).</summary>
    public async Task<CommentModel?> CommentAsync(Post post, CommentNode node, bool commentsOpen)
    {
        var comment = node.Comment;
        var blogSettings = await site.GetSettingsAsync();

        // Il testo viene codificato prima di passarlo alle estensioni: nessun HTML del visitatore arriva alla pagina.
        var serving = new ServingEvent<Comment> { BlogId = comment.BlogId, Item = comment, Body = WebUtility.HtmlEncode(comment.Content), Location = ServingLocation.SinglePost };
        await events.PublishAsync(serving);
        if (serving.Cancel)
        {
            return null;
        }

        var text = serving.Body
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\n", "<br />", StringComparison.Ordinal)
            .Replace("\t", "&nbsp;&nbsp;", StringComparison.Ordinal)
            .Replace("  ", "&nbsp;&nbsp;", StringComparison.Ordinal);

        var replies = new List<CommentModel>();
        foreach (var reply in node.Replies)
        {
            if (await CommentAsync(post, reply, commentsOpen) is { } item)
            {
                replies.Add(item);
            }
        }

        var appRoot = (await site.GetScopeAsync()).AppRoot;
        var avatarUrl = comment.IsPingbackOrTrackback
            ? appRoot + Core.Services.AvatarService.PingbackPath
            : !string.IsNullOrWhiteSpace(comment.Avatar) ? comment.Avatar : await avatars.GetUrlAsync(comment.Email);
        var website = comment.Website is { } address && (address.Scheme == Uri.UriSchemeHttp || address.Scheme == Uri.UriSchemeHttps) ? address.ToString() : string.Empty;

        return new CommentModel
        {
            Id = comment.Id.ToString(),
            ParentId = comment.ParentId == Guid.Empty ? string.Empty : comment.ParentId.ToString(),
            Author = comment.Author,
            Website = website,
            Country = comment.Country,
            AvatarUrl = avatarUrl,
            Avatar = new HtmlString($"<img class=\"photo\" src=\"{E(avatarUrl)}\" alt=\"{E(comment.Author)}\" width=\"48\" height=\"48\" />"),
            Flag = blogSettings.EnableCountryInComments && comment.Country.Length == 2
                ? new HtmlString($"<img class=\"flag\" src=\"{E(appRoot)}Content/images/blog/flags/{E(comment.Country.ToLowerInvariant())}.png\" alt=\"{E(comment.Country)}\" />")
                : HtmlString.Empty,
            DateCreated = await clock.ToBlogTimeAsync(comment.DateCreated),
            Text = new HtmlString(text),
            IsApproved = comment.IsApproved,
            IsPingback = comment.IsPingbackOrTrackback,
            IsByPostAuthor = !string.IsNullOrEmpty(post.Author) && string.Equals(post.Author, comment.Author, StringComparison.OrdinalIgnoreCase),
            ReplyLink = blogSettings.IsCommentNestingEnabled && commentsOpen && !comment.IsPingbackOrTrackback
                ? new HtmlString($"<a href=\"javascript:void(0);\" class=\"reply-to-comment\" onclick=\"BlogEngine.replyToComment('{comment.Id}');\">{E(site.T("replyToThis"))}</a>")
                : HtmlString.Empty,
            AdminLinks = await CommentAdminLinksAsync(comment, node.Replies.Count > 0),
            Replies = replies,
        };
    }

    /// <summary>Comandi di moderazione (porting di CommentViewBase.AdminLinks).</summary>
    private async Task<HtmlString> CommentAdminLinksAsync(Comment comment, bool hasReplies)
    {
        if (!await CanAsync(Rights.ModerateComments))
        {
            return HtmlString.Empty;
        }

        var items = new List<string>();
        if (!string.IsNullOrEmpty(comment.Email) && !comment.IsPingbackOrTrackback)
        {
            items.Add($"<a class=\"email\" href=\"mailto:{E(comment.Email)}\">{E(comment.Email)}</a>");
        }

        if (!string.IsNullOrEmpty(comment.IP))
        {
            items.Add($"<a href=\"https://whois.domaintools.com/{E(Uri.EscapeDataString(comment.IP))}\" rel=\"nofollow\">{E(comment.IP)}</a>");
        }

        if (hasReplies)
        {
            items.Add($"<a href=\"#\" onclick=\"return BlogEngine.deleteComment('{comment.Id}', false);\">{E(site.T("deleteKeepReplies"))}</a>");
            items.Add($"<a href=\"#\" onclick=\"return BlogEngine.deleteComment('{comment.Id}', true);\">{E(site.T("deletePlusReplies"))}</a>");
        }
        else
        {
            items.Add($"<a href=\"#\" onclick=\"return BlogEngine.deleteComment('{comment.Id}', false);\">{E(site.T("delete"))}</a>");
        }

        if (!comment.IsApproved)
        {
            items.Add($"<a href=\"#\" onclick=\"return BlogEngine.approveComment('{comment.Id}');\">{E(site.T("approve"))}</a>");
        }

        return new HtmlString(string.Join(" | ", items));
    }

    public async Task<StaticPageModel?> PageAsync(Page page, IReadOnlyList<Page> visiblePages)
    {
        var serving = new ServingEvent<Page> { BlogId = page.BlogId, Item = page, Body = page.Content, Location = ServingLocation.SinglePage };
        await events.PublishAsync(serving);
        if (serving.Cancel)
        {
            return null;
        }

        var url = await links.PageAsync(page);
        var children = new List<PageLinkModel>();
        foreach (var child in visiblePages.Where(p => p.Parent == page.Id && p.IsPublished).OrderBy(p => p.SortOrder))
        {
            children.Add(new PageLinkModel { Title = child.Title, Url = await links.PageAsync(child), Description = child.Description });
        }

        var canEdit = await CanAsync(Rights.EditOwnPages);
        var adminLinks = new List<string>();
        if (canEdit)
        {
            adminLinks.Add($"<a href=\"{E(links.BlogRoot)}admin/app/editor/editpage.cshtml?id={page.Id}\">{E(site.T("edit"))}</a>");
        }

        if (canEdit)
        {
            adminLinks.Add($"<a href=\"#\" onclick=\"return BlogEngine.deletePage('{page.Id}');\">{E(site.T("delete"))}</a>");
        }

        return new StaticPageModel
        {
            Id = page.Id.ToString(),
            Title = page.Title,
            Url = url,
            AbsoluteUrl = links.ToAbsolute(url).ToString(),
            Content = new HtmlString(serving.Body),
            Description = page.Description,
            Keywords = page.Keywords,
            DateCreated = await clock.ToBlogTimeAsync(page.DateCreated),
            DateModified = await clock.ToBlogTimeAsync(page.DateModified),
            Children = children,
            AdminLinks = adminLinks.Count == 0 ? HtmlString.Empty : new HtmlString($"<div class=\"page-adminlinks\">{string.Join(" | ", adminLinks)}</div>"),
            EditUrl = canEdit ? $"{links.BlogRoot}admin/app/editor/editpage.cshtml?id={page.Id}" : string.Empty,
        };
    }

    /// <summary>Paginazione con il parametro ?page=N (1 = post più recenti), mantenendo gli altri parametri.</summary>
    public static PagerModel Pager(HttpRequest request, int page, int pageSize, int total)
    {
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)Math.Max(1, pageSize)));
        string UrlFor(int number)
        {
            var query = request.Query.Where(q => !q.Key.Equals("page", StringComparison.OrdinalIgnoreCase))
                .SelectMany(q => q.Value.Select(v => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(v ?? string.Empty)}"))
                .ToList();
            if (number > 1)
            {
                query.Add($"page={number}");
            }

            var path = request.PathBase.Add(request.Path).Value ?? "/";
            return query.Count == 0 ? path : $"{path}?{string.Join('&', query)}";
        }

        return new PagerModel
        {
            Page = page,
            TotalPages = pages,
            TotalItems = total,
            NewerUrl = page > 1 ? UrlFor(page - 1) : string.Empty,
            OlderUrl = page < pages ? UrlFor(page + 1) : string.Empty,
            Pages = PageNumbers(pages, page).Select(n => n == 0
                ? new PagerLinkModel { IsEllipsis = true }
                : new PagerLinkModel { Number = n, Url = UrlFor(n), IsCurrent = n == page }).ToList(),
        };
    }

    /// <summary>Numeri di pagina da mostrare: inizio, fine e quelle vicine alla corrente; 0 indica "...".</summary>
    private static IEnumerable<int> PageNumbers(int total, int current)
    {
        var shown = new SortedSet<int> { 1, 2, total - 1, total };
        for (var i = current - 4; i <= current + 4; i++)
        {
            shown.Add(i);
        }

        var previous = 0;
        foreach (var number in shown.Where(n => n >= 1 && n <= total))
        {
            if (number > previous + 1)
            {
                yield return 0;
            }

            yield return number;
            previous = number;
        }
    }

    private static string E(string value) => WebUtility.HtmlEncode(value);
}

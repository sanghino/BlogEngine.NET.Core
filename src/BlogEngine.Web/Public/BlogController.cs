using System.Globalization;
using BlogEngine.Core.Services;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Text;
using BlogEngine.Web.Themes;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Public;

/// <summary>
/// Pagine pubbliche del blog mostrate con il tema attivo (porting di default.aspx, post.aspx,
/// page.aspx, archive.aspx, search.aspx ed error404.aspx). Gli indirizzi sono quelli generati
/// da <see cref="ILinkGenerator"/>; i vecchi parametri (?tag=, ?year=, ?date=...) sono ancora accettati.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class BlogController(
    SiteRenderer site,
    PublicContent content,
    IPostService posts,
    IPageService pages,
    ICategoryService categories,
    IProfileService profiles,
    ILinkGenerator links,
    IBlogClock clock,
    SearchService search,
    Hosting.RequestBlogContext system) : Controller
{
    private const int SearchPageSize = 10;

    // ---------------------------------------------------------------- elenco dei post

    [HttpGet("/")]
    public async Task<IActionResult> Index(int page = 1)
    {
        if (!await content.CanAsync(Rights.ViewPublicPosts))
        {
            return RedirectToLogin();
        }

        var query = Request.Query;

        // Vecchi indirizzi con parametri: default.aspx?tag=/x, ?name=autore, ?id=categoria, ?year=&month=, ?date=, ?calendar=show.
        if (query.TryGetValue("tag", out var tag) && !string.IsNullOrWhiteSpace(tag))
        {
            return await TagAsync(tag.ToString().TrimStart('/'), page);
        }

        if (query.TryGetValue("name", out var author) && !string.IsNullOrWhiteSpace(author))
        {
            return await AuthorAsync(author.ToString(), page);
        }

        if (Guid.TryParse(query["id"], out var categoryId) && await categories.GetByIdAsync(categoryId) is { } legacyCategory)
        {
            return await CategoryListAsync(legacyCategory, page);
        }

        if (int.TryParse(query["year"], out var year))
        {
            return await DateAsync(year, int.TryParse(query["month"], out var month) ? month : null, null, page);
        }

        if (DateTime.TryParse(query["date"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return await DateAsync(date.Year, date.Month, date.Day, page);
        }

        if (query.ContainsKey("calendar"))
        {
            return await Calendar(null, null);
        }

        // Pagina iniziale personalizzata (impostazione o pagina marcata come "home"), se non si chiede /blog.
        if (!query.ContainsKey("blog") && page == 1 && await GetFrontPageAsync() is { } frontPage)
        {
            return await ShowPageAsync(frontPage);
        }

        var blogSettings = await site.GetSettingsAsync();
        return await PostListAsync(await posts.GetVisiblePostsAsync(), page, ServingContentBy.AllContent, string.Empty,
            new PageMeta { Description = blogSettings.Description, Keywords = await AllTagsAsync() });
    }

    /// <summary>Elenco dei post anche quando è impostata una pagina iniziale.</summary>
    [HttpGet("/blog")]
    public async Task<IActionResult> Blog(int page = 1)
    {
        if (!await content.CanAsync(Rights.ViewPublicPosts))
        {
            return RedirectToLogin();
        }

        var blogSettings = await site.GetSettingsAsync();
        return await PostListAsync(await posts.GetVisiblePostsAsync(), page, ServingContentBy.AllContent, string.Empty,
            new PageMeta { Description = blogSettings.Description });
    }

    [HttpGet("/category/{slug}")]
    public async Task<IActionResult> Category(string slug, int page = 1)
    {
        if (!await content.CanAsync(Rights.ViewPublicPosts))
        {
            return RedirectToLogin();
        }

        return await categories.GetBySlugAsync(slug) is { } category ? await CategoryListAsync(category, page) : NotFound();
    }

    [HttpGet("/tag/{tag}")]
    public async Task<IActionResult> Tag(string tag, int page = 1) =>
        await content.CanAsync(Rights.ViewPublicPosts) ? await TagAsync(tag, page) : RedirectToLogin();

    [HttpGet("/author/{name}")]
    public async Task<IActionResult> Author(string name, int page = 1) =>
        await content.CanAsync(Rights.ViewPublicPosts) ? await AuthorAsync(name, page) : RedirectToLogin();

    [HttpGet("/{year:int:range(1900,9999)}")]
    [HttpGet("/{year:int:range(1900,9999)}/{month:int:range(1,12)}")]
    [HttpGet("/{year:int:range(1900,9999)}/{month:int:range(1,12)}/default")]
    [HttpGet("/{year:int:range(1900,9999)}/{month:int:range(1,12)}/{day:int:range(1,31)}")]
    [HttpGet("/{year:int:range(1900,9999)}/{month:int:range(1,12)}/{day:int:range(1,31)}/default")]
    public async Task<IActionResult> Date(int year, int? month, int? day, int page = 1) =>
        await content.CanAsync(Rights.ViewPublicPosts) ? await DateAsync(year, month, day, page) : RedirectToLogin();

    private async Task<IActionResult> CategoryListAsync(Category category, int page) =>
        await PostListAsync(await posts.GetByCategoryAsync(category.Id), page, ServingContentBy.Category, category.Title,
            new PageMeta { Description = category.Description, Canonical = links.ToAbsolute(await links.CategoryAsync(category)).ToString() });

    private async Task<IActionResult> TagAsync(string tag, int page)
    {
        // L'indirizzo contiene la forma "slug" del tag: si cerca il tag originale corrispondente.
        var all = await posts.GetVisiblePostsAsync();
        var match = all.SelectMany(p => p.Tags).FirstOrDefault(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase))
            ?? all.SelectMany(p => p.Tags).FirstOrDefault(t => Slug.AreEquivalent(t, tag))
            ?? tag;

        var list = all.Where(p => p.Tags.Any(t => string.Equals(t, match, StringComparison.OrdinalIgnoreCase))).ToList();
        return await PostListAsync(list, page, ServingContentBy.Tag, $"{site.T("AllPostsTagged")} '{match}'",
            new PageMeta { Keywords = match });
    }

    private async Task<IActionResult> AuthorAsync(string name, int page)
    {
        var list = await posts.GetByAuthorAsync(name);
        var profile = await profiles.GetAsync(name);
        var display = string.IsNullOrWhiteSpace(profile?.DisplayName) ? name : profile.DisplayName;
        return await PostListAsync(list, page, ServingContentBy.Author, $"{site.T("AllPostsBy")} {display}",
            new PageMeta { Author = display });
    }

    private async Task<IActionResult> DateAsync(int year, int? month, int? day, int page)
    {
        DateTime from, to;
        string title;
        try
        {
            from = new DateTime(year, month ?? 1, day ?? 1);
            to = day is not null ? from.AddDays(1) : month is not null ? from.AddMonths(1) : from.AddYears(1);
            title = day is not null ? from.ToString("D", CultureInfo.CurrentCulture)
                : month is not null ? from.ToString("Y", CultureInfo.CurrentCulture)
                : from.Year.ToString(CultureInfo.CurrentCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return NotFound();
        }

        // Le date dell'indirizzo sono nel fuso orario del blog, quelle salvate in UTC.
        var list = await posts.GetByDateAsync(await clock.ToUtcAsync(from), await clock.ToUtcAsync(to));
        return await PostListAsync(list, page, ServingContentBy.DateRange, title, new PageMeta());
    }

    /// <summary>Porting di PostList.ascx: una pagina di post con la paginazione ?page=N.</summary>
    private async Task<IActionResult> PostListAsync(IReadOnlyList<Post> list, int page, ServingContentBy contentBy, string title, PageMeta meta)
    {
        var blogSettings = await site.GetSettingsAsync();
        var visible = list.Where(p => p.IsPublished && !p.IsDeleted).OrderByDescending(p => p.DateCreated).ToList();
        var pageSize = Math.Max(1, blogSettings.PostsPerPage);
        var pager = PublicContent.Pager(Request, Math.Max(1, page), pageSize, visible.Count);
        if (page > pager.TotalPages && visible.Count > 0)
        {
            return NotFound();
        }

        var showExcerpt = blogSettings.ShowDescriptionInPostList
            || (blogSettings.ShowDescriptionInPostListForPostsByTagOrCategory && contentBy is ServingContentBy.Tag or ServingContentBy.Category);

        var models = new List<PostModel>();
        var index = 0;
        foreach (var post in visible.Skip((pager.Page - 1) * pageSize).Take(pageSize))
        {
            if (await content.PostAsync(post, index, ServingLocation.PostList, contentBy, showExcerpt) is { } model)
            {
                models.Add(model);
                index++;
            }
        }

        meta.Title = title;
        meta.PreviousUrl = pager.NewerUrl;
        meta.NextUrl = pager.OlderUrl;
        return await site.PageAsync("index", meta, new Dictionary<string, object?>
        {
            ["posts"] = models,
            ["pager"] = pager,
            ["list_title"] = title,
            ["content_by"] = contentBy.ToString().ToLowerInvariant(),
            ["show_excerpt"] = showExcerpt,
        });
    }

    // ---------------------------------------------------------------- post singolo

    [HttpGet("/post/{slug}")]
    [HttpGet("/post/{year:int}/{month:int}/{day:int}/{slug}")]
    public async Task<IActionResult> Post(string slug)
    {
        return await posts.GetBySlugAsync(slug) is { } post ? await ShowPostAsync(post) : NotFound();
    }

    /// <summary>post.aspx?id=...: reindirizza all'indirizzo definitivo del post.</summary>
    [HttpGet("/post")]
    public async Task<IActionResult> PostById(Guid id) =>
        await posts.GetByIdAsync(id) is { } post && await posts.IsVisibleAsync(post)
            ? RedirectPermanent(await links.PostAsync(post))
            : NotFound();

    private async Task<IActionResult> ShowPostAsync(Post post)
    {
        if (!await posts.IsVisibleAsync(post))
        {
            return await content.CanAsync(Rights.ViewPublicPosts) ? NotFound() : RedirectToLogin();
        }

        // Un solo indirizzo per post: le varianti (con o senza data, maiuscole diverse) rimandano a quello corretto.
        var canonical = await links.PostAsync(post);
        var requested = Request.PathBase.Add(Request.Path).Value ?? string.Empty;
        if (canonical.StartsWith('/') && !string.Equals(Uri.UnescapeDataString(canonical), Uri.UnescapeDataString(requested), StringComparison.Ordinal))
        {
            return RedirectPermanent(canonical + Request.QueryString);
        }

        var blogSettings = await site.GetSettingsAsync();
        var unsubscribed = false;
        if (Request.Query.TryGetValue("unsubscribe-email", out var email) && !string.IsNullOrWhiteSpace(email))
        {
            unsubscribed = await UnsubscribeAsync(post, email.ToString());
        }

        if (await content.PostAsync(post, 0, ServingLocation.SinglePost, ServingContentBy.Unspecified, showExcerpt: false) is not { } model)
        {
            return NotFound();
        }

        var values = new Dictionary<string, object?>
        {
            ["post"] = model,
            ["comments"] = await content.CommentsAsync(post, model),
            ["unsubscribed"] = unsubscribed,
            ["trackback_url"] = $"{links.ToAbsolute(links.BlogRoot)}trackback.axd?id={post.Id}",
        };

        if (blogSettings.ShowPostNavigation)
        {
            var (previous, next) = await posts.GetAdjacentAsync(post);
            values["navigation"] = new
            {
                previous_url = previous is null ? string.Empty : await links.PostAsync(previous),
                previous_title = previous?.Title ?? string.Empty,
                next_url = next is null ? string.Empty : await links.PostAsync(next),
                next_title = next?.Title ?? string.Empty,
            };
        }

        if (blogSettings.EnableRelatedPosts)
        {
            values["related_posts"] = await RelatedPostsAsync(post);
        }

        // Indirizzo per i pingback, come l'header X-Pingback della versione originale.
        Response.Headers["x-pingback"] = $"{links.ToAbsolute(links.BlogRoot)}pingback.axd";

        return await site.PageAsync("post", new PageMeta
        {
            Title = post.Title,
            Description = model.PlainDescription.Length > 300 ? model.PlainDescription[..300] : model.PlainDescription,
            Keywords = string.Join(',', post.Tags),
            Author = model.AuthorName,
            Canonical = model.AbsoluteUrl,
        }, values);
    }

    /// <summary>Post correlati: quelli con più tag e categorie in comune (porting di RelatedPosts).</summary>
    private async Task<IReadOnlyList<object>> RelatedPostsAsync(Post post)
    {
        var titleWords = post.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 3).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var related = (await posts.GetVisiblePostsAsync())
            .Where(p => p.Id != post.Id)
            .Select(p => (Post: p, Score:
                (p.Tags.Count(t => post.Tags.Contains(t, StringComparer.OrdinalIgnoreCase)) * 3)
                + (p.Categories.Count(post.Categories.Contains) * 2)
                + p.Title.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(titleWords.Contains)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Post.DateCreated)
            .Take(3)
            .ToList();

        var result = new List<object>();
        foreach (var (item, _) in related)
        {
            var text = HtmlText.Strip(string.IsNullOrWhiteSpace(item.Description) ? item.Content : item.Description);
            result.Add(new
            {
                title = item.Title,
                url = await links.PostAsync(item),
                description = text.Length > 100 ? text[..100] + "..." : text,
            });
        }

        return result;
    }

    /// <summary>Cancella l'indirizzo dalle notifiche dei nuovi commenti del post.</summary>
    private async Task<bool> UnsubscribeAsync(Post post, string email)
    {
        if (!post.NotificationEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var copy = Api.EntityCopies.Copy(post);
        copy.NotificationEmails = copy.NotificationEmails.Where(e => !string.Equals(e, email, StringComparison.OrdinalIgnoreCase)).ToList();
        await system.RunElevatedAsync(() => posts.SaveAsync(copy));
        return true;
    }

    // ---------------------------------------------------------------- pagine

    [HttpGet("/page/{slug}")]
    public async Task<IActionResult> Page(string slug) =>
        await pages.GetBySlugAsync(slug) is { } page ? await ShowPageAsync(page) : NotFound();

    private async Task<IActionResult> ShowPageAsync(Page page)
    {
        if (!await pages.IsVisibleAsync(page))
        {
            return NotFound();
        }

        var visible = await pages.GetVisiblePagesAsync();
        if (await content.PageAsync(page, visible) is not { } model)
        {
            return NotFound();
        }

        var blogSettings = await site.GetSettingsAsync();
        return await site.PageAsync("page", new PageMeta
        {
            Title = page.Title,
            Description = page.Description,
            Keywords = page.Keywords,
            Canonical = model.AbsoluteUrl,
        }, new Dictionary<string, object?>
        {
            ["page"] = model,
            ["comments"] = new CommentsModel
            {
                Provider = blogSettings.CommentProvider.ToString().ToLowerInvariant(),
                DisqusWebsiteName = blogSettings.DisqusWebsiteName,
                DisqusDevMode = blogSettings.DisqusDevMode,
                IsOpen = blogSettings.DisqusAddCommentsToPages,
            },
        });
    }

    private async Task<Page?> GetFrontPageAsync()
    {
        var blogSettings = await site.GetSettingsAsync();
        if (Guid.TryParse(blogSettings.CustomFrontPage, out var id) && await pages.GetByIdAsync(id) is { } custom && await pages.IsVisibleAsync(custom))
        {
            return custom;
        }

        return await pages.GetFrontPageAsync();
    }

    // ---------------------------------------------------------------- archivio e calendario

    [HttpGet("/archive")]
    public async Task<IActionResult> Archive()
    {
        if (!await content.CanAsync(Rights.ViewPublicPosts))
        {
            return RedirectToLogin();
        }

        var blogSettings = await site.GetSettingsAsync();
        var visible = (await posts.GetVisiblePostsAsync()).OrderByDescending(p => p.DateCreated).ToList();
        var groups = new List<ArchiveCategoryModel>();
        var sorted = new List<(string Sort, Category Category)>();
        foreach (var category in await categories.GetCategoriesAsync())
        {
            sorted.Add((await categories.GetCompleteTitleAsync(category), category));
        }

        foreach (var (_, category) in sorted.OrderBy(s => s.Sort, StringComparer.CurrentCultureIgnoreCase))
        {
            var categoryPosts = visible.Where(p => p.Categories.Contains(category.Id)).ToList();
            if (categoryPosts.Count == 0)
            {
                continue;
            }

            groups.Add(new ArchiveCategoryModel
            {
                Title = category.Title,
                Url = await links.CategoryAsync(category),
                FeedUrl = await links.CategoryFeedAsync(category),
                Anchor = Slug.Create(category.Title),
                Posts = await ArchivePostsAsync(categoryPosts, blogSettings.EnableCommentsModeration),
            });
        }

        var uncategorized = visible.Where(p => p.Categories.Count == 0).ToList();
        if (uncategorized.Count > 0)
        {
            groups.Add(new ArchiveCategoryModel
            {
                Title = site.T("uncategorized"),
                Anchor = "uncategorized",
                Posts = await ArchivePostsAsync(uncategorized, blogSettings.EnableCommentsModeration),
            });
        }

        var rated = visible.Where(p => p.Raters > 0).ToList();
        return await site.PageAsync("archive", new PageMeta { Title = site.T("archive") }, new Dictionary<string, object?>
        {
            ["categories"] = groups,
            ["totals"] = new ArchiveTotalsModel
            {
                Posts = visible.Count,
                Comments = visible.Sum(p => p.ApprovedComments(blogSettings.EnableCommentsModeration).Count(c => !c.IsPingbackOrTrackback)),
                Raters = visible.Sum(p => p.Raters),
                AverageRating = rated.Count == 0 ? 0 : Math.Round(rated.Average(p => p.Rating), 1),
            },
        });
    }

    private async Task<IReadOnlyList<ArchivePostModel>> ArchivePostsAsync(IEnumerable<Post> list, bool moderation)
    {
        var result = new List<ArchivePostModel>();
        foreach (var post in list)
        {
            result.Add(new ArchivePostModel
            {
                Title = post.Title,
                Url = await links.PostAsync(post),
                DateCreated = await clock.ToBlogTimeAsync(post.DateCreated),
                CommentsCount = post.ApprovedComments(moderation).Count(c => !c.IsPingbackOrTrackback),
                Rating = Math.Round(post.Rating, 1),
                Raters = post.Raters,
            });
        }

        return result;
    }

    /// <summary>Calendario mensile dei post (porting del controllo PostCalendar).</summary>
    [HttpGet("/calendar")]
    public async Task<IActionResult> Calendar(int? year, int? month)
    {
        if (!await content.CanAsync(Rights.ViewPublicPosts))
        {
            return RedirectToLogin();
        }

        var today = await clock.ToBlogTimeAsync(clock.UtcNow);
        var first = year is >= 1900 and <= 9999 && month is >= 1 and <= 12 ? new DateTime(year.Value, month.Value, 1) : new DateTime(today.Year, today.Month, 1);
        var culture = CultureInfo.CurrentCulture;
        var firstDay = (int)culture.DateTimeFormat.FirstDayOfWeek;
        var start = first.AddDays(-(((int)first.DayOfWeek - firstDay + 7) % 7));

        var monthPosts = new List<(DateTime Date, LinkModel Link)>();
        foreach (var post in await posts.GetByDateAsync(await clock.ToUtcAsync(start), await clock.ToUtcAsync(start.AddDays(42))))
        {
            monthPosts.Add((await clock.ToBlogTimeAsync(post.DateCreated), new LinkModel { Title = post.Title, Url = await links.PostAsync(post) }));
        }

        var weeks = new List<IReadOnlyList<CalendarDayModel>>();
        for (var week = 0; week < 6; week++)
        {
            var days = new List<CalendarDayModel>();
            for (var d = 0; d < 7; d++)
            {
                var day = start.AddDays((week * 7) + d);
                var dayPosts = monthPosts.Where(p => p.Date.Date == day).Select(p => p.Link).ToList();
                days.Add(new CalendarDayModel
                {
                    Day = day.Day,
                    IsCurrentMonth = day.Month == first.Month,
                    IsToday = day == today.Date,
                    IsWeekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                    Url = dayPosts.Count > 0 ? $"{links.BlogRoot}{day:yyyy}/{day:MM}/{day:dd}" : string.Empty,
                    Posts = dayPosts,
                });
            }

            weeks.Add(days);
        }

        var previous = first.AddMonths(-1);
        var next = first.AddMonths(1);
        var calendar = new CalendarModel
        {
            Title = first.ToString("Y", culture),
            PreviousUrl = $"{links.BlogRoot}calendar?year={previous.Year}&month={previous.Month}",
            NextUrl = $"{links.BlogRoot}calendar?year={next.Year}&month={next.Month}",
            DayNames = Enumerable.Range(0, 7).Select(i => culture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)((firstDay + i) % 7))).ToList(),
            Weeks = weeks,
        };

        return await site.PageAsync("calendar", new PageMeta { Title = site.T("calendar") }, new Dictionary<string, object?> { ["calendar"] = calendar });
    }

    // ---------------------------------------------------------------- ricerca

    [HttpGet("/search")]
    public async Task<IActionResult> Search(string? q, string? comment, int page = 1)
    {
        if (!await content.CanAsync(Rights.ViewPublicPosts))
        {
            return RedirectToLogin();
        }

        var blogSettings = await site.GetSettingsAsync();
        var includeComments = blogSettings.EnableCommentSearch && !string.IsNullOrEmpty(comment);
        var hits = await search.SearchAsync(q, includeComments);
        var pager = PublicContent.Pager(Request, Math.Max(1, page), SearchPageSize, hits.Count);

        var results = new List<SearchResultModel>();
        foreach (var hit in hits.Skip((pager.Page - 1) * SearchPageSize).Take(SearchPageSize))
        {
            var url = hit switch
            {
                { Comment: { } c, Post: { } p } => await links.CommentAsync(p, c),
                { Post: { } p } => await links.PostAsync(p),
                { Page: { } pg } => await links.PageAsync(pg),
                _ => links.BlogRoot,
            };

            results.Add(new SearchResultModel
            {
                Title = hit.Title,
                Url = url,
                AbsoluteUrl = links.ToAbsolute(url).ToString(),
                Text = hit.Text,
                Type = hit.Type,
                DateCreated = await clock.ToBlogTimeAsync(hit.DateCreated),
            });
        }

        var title = string.IsNullOrWhiteSpace(q) ? site.T("search") : $"{site.T("searchResultsFor")} '{q}'";
        return await site.PageAsync("search", new PageMeta { Title = title, NoIndex = true, PreviousUrl = pager.NewerUrl, NextUrl = pager.OlderUrl }, new Dictionary<string, object?>
        {
            ["search"] = new
            {
                query = q ?? string.Empty,
                title,
                include_comments = includeComments,
                show_comments_option = blogSettings.EnableCommentSearch && blogSettings.ShowIncludeCommentsOption,
                comments_label = blogSettings.SearchCommentLabelText,
                button_text = blogSettings.SearchButtonText,
                total = hits.Count,
            },
            ["results"] = results,
            ["pager"] = pager,
        });
    }

    // ---------------------------------------------------------------- errori

    /// <summary>Pagine di errore con il tema (404 e gli altri codici impostati dal sito).</summary>
    [HttpGet("/error/{code:int}")]
    public async Task<IActionResult> Error(int code)
    {
        var blogSettings = await site.GetSettingsAsync();
        var suggestions = new List<SearchResultModel>();
        if (code == StatusCodes.Status404NotFound && HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodeReExecuteFeature>() is { } original)
        {
            // Come error404.aspx: propone i contenuti che somigliano all'indirizzo richiesto.
            var words = original.OriginalPath.Replace('-', ' ').Replace('/', ' ').Replace('_', ' ');
            foreach (var hit in (await search.SearchAsync(words, includeComments: false)).Take(5))
            {
                var url = hit.Post is { } p ? await links.PostAsync(p) : hit.Page is { } pg ? await links.PageAsync(pg) : links.BlogRoot;
                suggestions.Add(new SearchResultModel { Title = hit.Title, Url = url, Type = hit.Type, Text = hit.Text });
            }
        }

        var title = code == StatusCodes.Status404NotFound ? site.T("PageNotFound") : blogSettings.ErrorTitle;
        return await site.PageAsync("error", new PageMeta { Title = title, NoIndex = true }, new Dictionary<string, object?>
        {
            ["error"] = new
            {
                code,
                title,
                text = new Microsoft.AspNetCore.Html.HtmlString(code == StatusCodes.Status404NotFound ? string.Empty : blogSettings.ErrorText),
                suggestions,
            },
        }, code);
    }

    private async Task<string> AllTagsAsync() =>
        string.Join(',', (await posts.GetVisiblePostsAsync()).SelectMany(p => p.Tags).Distinct(StringComparer.OrdinalIgnoreCase).Take(30));

    private IActionResult RedirectToLogin() =>
        Redirect($"{links.BlogRoot}account/login?ReturnUrl={Uri.EscapeDataString(Request.PathBase.Add(Request.Path).Value + Request.QueryString)}");
}

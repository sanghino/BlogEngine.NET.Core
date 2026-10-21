using System.Collections.Concurrent;
using System.Globalization;
using System.ServiceModel.Syndication;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Settings;
using BlogEngine.Text;

namespace BlogEngine.Web.Themes;

// Codice dei widget inclusi in BlogEngine (porting dei vecchi widget.cshtml). Ogni classe prepara
// i dati che il template Custom/Widgets/<Nome>/widget.liquid trova in "widget.data".

internal static class WidgetSettings
{
    public static int Int(this WidgetModel widget, string key, int fallback) =>
        widget.Settings.TryGetValue(key, out var value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : fallback;

    public static bool Bool(this WidgetModel widget, string key, bool fallback) =>
        widget.Settings.TryGetValue(key, out var value) && bool.TryParse(value, out var flag) ? flag : fallback;

    public static string Text(this WidgetModel widget, string key, string fallback) =>
        widget.Settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : fallback;
}

public sealed class AuthorListWidget(IPostService posts, IProfileService profiles, IAvatarService avatars, ILinkGenerator links) : IWidgetDataProvider
{
    public string Name => "AuthorList";

    public async Task<object?> GetDataAsync(WidgetModel widget, CancellationToken cancellationToken)
    {
        var authors = new List<object>();
        foreach (var group in (await posts.GetVisiblePostsAsync(cancellationToken)).GroupBy(p => p.Author, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var profile = await profiles.GetAsync(group.Key, cancellationToken);
            authors.Add(new
            {
                name = group.Key,
                display_name = string.IsNullOrWhiteSpace(profile?.DisplayName) ? group.Key : profile.DisplayName,
                url = links.Author(group.Key),
                feed_url = $"{links.BlogRoot}syndication.axd?author={Uri.EscapeDataString(group.Key)}",
                avatar_url = await avatars.GetUrlAsync(profile?.EmailAddress, cancellationToken),
                count = group.Count(),
            });
        }

        return new { authors };
    }
}

public sealed class BlogListWidget(IBlogService blogs) : IWidgetDataProvider
{
    public string Name => "BlogList";

    public async Task<object?> GetDataAsync(WidgetModel widget, CancellationToken cancellationToken) => new
    {
        blogs = (await blogs.GetBlogsAsync(cancellationToken))
            .Where(b => b.IsActive && !b.IsDeleted)
            .OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(b => new { name = b.Name, url = Core.Services.BlogResolver.RelativeRoot(b) })
            .ToList(),
    };
}

public sealed class BlogRollWidget(IBlogRollService blogRoll, ISettingsService settings, BlogRollFeeds feeds) : IWidgetDataProvider
{
    public string Name => "BlogRoll";

    public async Task<object?> GetDataAsync(WidgetModel widget, CancellationToken cancellationToken)
    {
        var blogSettings = await settings.GetAsync(cancellationToken: cancellationToken);
        var items = (await blogRoll.GetItemsAsync(cancellationToken)).OrderBy(i => i.SortIndex).ThenBy(i => i.Title).ToList();
        return new
        {
            items = items.Select(i => new
            {
                title = i.Title,
                description = i.Description,
                url = i.BlogUrl?.ToString() ?? string.Empty,
                feed_url = i.FeedUrl?.ToString() ?? string.Empty,
                xfn = string.IsNullOrWhiteSpace(i.Xfn) ? "contact" : i.Xfn,
                posts = feeds.GetPosts(i.FeedUrl, blogSettings),
            }).ToList(),
        };
    }
}

/// <summary>
/// Ultimi post dei blog del blogroll. I feed vengono scaricati in background e tenuti in memoria
/// per il tempo indicato nelle impostazioni, così la pagina non aspetta mai un sito esterno.
/// </summary>
public sealed class BlogRollFeeds(IHttpClientFactory httpClients, ILogger<BlogRollFeeds> logger)
{
    private readonly ConcurrentDictionary<string, Entry> _feeds = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<object> GetPosts(Uri? feedUrl, BlogSettings settings)
    {
        if (feedUrl is null || settings.BlogrollVisiblePosts <= 0 || (feedUrl.Scheme != Uri.UriSchemeHttp && feedUrl.Scheme != Uri.UriSchemeHttps))
        {
            return [];
        }

        var key = feedUrl.ToString();
        var entry = _feeds.GetOrAdd(key, _ => new Entry());
        if (entry.Expires < DateTime.UtcNow && Interlocked.CompareExchange(ref entry.Loading, 1, 0) == 0)
        {
            _ = Task.Run(() => RefreshAsync(feedUrl, entry, settings));
        }

        return entry.Posts
            .Take(settings.BlogrollVisiblePosts)
            .Select(p => (object)new
            {
                title = p.Title,
                short_title = settings.BlogrollMaxLength > 0 && p.Title.Length > settings.BlogrollMaxLength ? p.Title[..settings.BlogrollMaxLength] + "..." : p.Title,
                url = p.Url,
            })
            .ToList();
    }

    private async Task RefreshAsync(Uri feedUrl, Entry entry, BlogSettings settings)
    {
        try
        {
            var client = httpClients.CreateClient(nameof(BlogRollFeeds));
            client.Timeout = TimeSpan.FromSeconds(10);
            await using var stream = await client.GetStreamAsync(feedUrl);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, Async = true });
            var feed = SyndicationFeed.Load(reader);
            entry.Posts = feed.Items
                .Select(i => (Title: i.Title?.Text ?? string.Empty, Url: i.Links.FirstOrDefault()?.Uri?.ToString() ?? string.Empty))
                .Where(p => p.Url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                .Take(20)
                .ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or XmlException or InvalidOperationException or IOException)
        {
            logger.LogInformation("Feed del blogroll non disponibile ({Url}): {Message}", feedUrl, ex.Message);
        }
        finally
        {
            entry.Expires = DateTime.UtcNow.AddMinutes(Math.Max(1, settings.BlogrollUpdateMinutes));
            Interlocked.Exchange(ref entry.Loading, 0);
        }
    }

    private sealed class Entry
    {
        public int Loading;

        public DateTime Expires { get; set; } = DateTime.MinValue;

        public IReadOnlyList<(string Title, string Url)> Posts { get; set; } = [];
    }
}

public sealed class CategoryListWidget(ICategoryService categories, IPostService posts, ILinkGenerator links) : IWidgetDataProvider
{
    public string Name => "CategoryList";

    public async Task<object?> GetDataAsync(WidgetModel widget, CancellationToken cancellationToken)
    {
        var visible = await posts.GetVisiblePostsAsync(cancellationToken);
        var items = new List<(string Sort, object Item)>();
        foreach (var category in await categories.GetCategoriesAsync(cancellationToken))
        {
            var complete = await categories.GetCompleteTitleAsync(category, cancellationToken);
            var level = complete.Count(c => c == '-');
            items.Add((complete, new
            {
                title = category.Title,
                description = category.Description,
                url = await links.CategoryAsync(category, cancellationToken),
                feed_url = await links.CategoryFeedAsync(category, cancellationToken),
                count = visible.Count(p => p.Categories.Contains(category.Id)),
                level,
                indent = new Microsoft.AspNetCore.Html.HtmlString(string.Concat(Enumerable.Repeat("&nbsp;&nbsp;&nbsp;", level))),
            }));
        }

        return new
        {
            categories = items.OrderBy(i => i.Sort, StringComparer.CurrentCultureIgnoreCase).Select(i => i.Item).ToList(),
            show_rss_icon = widget.Bool("showrssicon", true),
            show_post_count = widget.Bool("showpostcount", true),
        };
    }
}

public sealed partial class CommentListWidget(IPostService posts, ISettingsService settings, ILinkGenerator links) : IWidgetDataProvider
{
    public string Name => "CommentList";

    public async Task<object?> GetDataAsync(WidgetModel widget, CancellationToken cancellationToken)
    {
        var blogSettings = await settings.GetAsync(cancellationToken: cancellationToken);
        var visible = await posts.GetVisiblePostsAsync(cancellationToken);
        var recent = visible
            .SelectMany(p => p.ApprovedComments(blogSettings.EnableCommentsModeration).Where(c => c.IsApproved && c.Email.Contains('@')).Select(c => (Post: p, Comment: c)))
            .OrderByDescending(x => x.Comment.DateCreated)
            .Take(widget.Int("numberofcomments", 10))
            .ToList();

        var comments = new List<object>();
        foreach (var (post, comment) in recent)
        {
            var body = TagRegex().Replace(comment.Content, string.Empty);
            var text = body.Length > 50 ? body[..50] + "..." : body;
            comments.Add(new
            {
                post_title = post.Title,
                post_url = await links.PostAsync(post, cancellationToken) + "#comment",
                post_comments = post.ApprovedComments(blogSettings.EnableCommentsModeration).Count(c => !c.IsPingbackOrTrackback),
                author = comment.Author,
                text,
                date_created = comment.DateCreated,
            });
        }

        return new { comments, feed_url = $"{links.BlogRoot}syndication.axd?comments=true" };
    }

    [GeneratedRegex(@"\[(.*?)\]")]
    private static partial Regex TagRegex();
}

/// <summary>Elenco di link modificabile; i link sono salvati nell'impostazione "links" in JSON.</summary>
public sealed class LinkListWidget : IWidgetDataProvider
{
    public string Name => "LinkList";

    public Task<object?> GetDataAsync(WidgetModel widget, CancellationToken cancellationToken)
    {
        List<LinkItem> items;
        try
        {
            items = widget.Settings.TryGetValue("links", out var json) ? JsonSerializer.Deserialize<List<LinkItem>>(json, ThemeJson.Options) ?? [] : [];
        }
        catch (JsonException)
        {
            items = [];
        }

        var max = widget.Int("numberoflinks", 0);
        var links = (max > 0 ? items.Take(max) : items)
            .Where(l => Uri.TryCreate(l.Url, UriKind.RelativeOrAbsolute, out var uri) && (!uri.IsAbsoluteUri || uri.Scheme is "http" or "https" or "mailto"))
            .Select(l => new { title = l.Title, url = l.Url, new_window = l.NewWindow })
            .ToList();
        return Task.FromResult<object?>(new { links });
    }

    public sealed class LinkItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public string Title { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        public bool NewWindow { get; set; }
    }
}

public sealed class MonthListWidget(IPostService posts, IBlogClock clock, ILinkGenerator links) : IWidgetDataProvider
{
    public string Name => "MonthList";

    public async Task<object?> GetDataAsync(WidgetModel widget, CancellationToken cancellationToken)
    {
        var dates = new List<DateTime>();
        foreach (var post in await posts.GetVisiblePostsAsync(cancellationToken))
        {
            dates.Add(await clock.ToBlogTimeAsync(post.DateCreated));
        }

        var years = dates
            .GroupBy(d => d.Year)
            .OrderByDescending(g => g.Key)
            .Select(y => new YearModel
            {
                Year = y.Key,
                Count = y.Count(),
                Months = y.GroupBy(d => d.Month).OrderByDescending(m => m.Key).Select(m => new MonthModel
                {
                    Year = y.Key,
                    Month = m.Key,
                    Title = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(m.Key),
                    Url = $"{links.BlogRoot}{y.Key}/{m.Key:00}",
                    Count = m.Count(),
                }).ToList(),
            })
            .ToList();

        return new { years };
    }
}

public sealed class PageListWidget(IPageService pages, ILinkGenerator links) : IWidgetDataProvider
{
    public string Name => "PageList";

    public async Task<object?> GetDataAsync(WidgetModel widget, CancellationToken cancellationToken)
    {
        var result = new List<object>();
        foreach (var page in (await pages.GetVisiblePagesAsync(cancellationToken)).Where(p => p.ShowInList && p.IsPublished).OrderBy(p => p.SortOrder))
        {
            result.Add(new { title = page.Title, description = page.Description, url = await links.PageAsync(page, cancellationToken) });
        }

        return new { pages = result };
    }
}

public sealed class PostListWidget(IPostService posts, ICategoryService categories, ILinkGenerator links, IBlogClock clock) : IWidgetDataProvider
{
    public string Name => "PostList";

    public async Task<object?> GetDataAsync(WidgetModel widget, CancellationToken cancellationToken)
    {
        IEnumerable<Post> list = await posts.GetVisiblePostsAsync(cancellationToken);

        // "cutegory" è il nome (con l'errore di battitura) usato dalla versione originale.
        var category = widget.Text("cutegory", "All");
        if (category != "All")
        {
            var match = (await categories.GetCategoriesAsync(cancellationToken)).FirstOrDefault(c => c.Title == category);
            list = match is null ? [] : list.Where(p => p.Categories.Contains(match.Id));
        }

        var author = widget.Text("author", "All");
        if (author != "All")
        {
            list = list.Where(p => string.Equals(p.Author, author, StringComparison.OrdinalIgnoreCase));
        }

        list = widget.Text("sortorder", "Published") switch
        {
            "Alphabetical" => list.OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase),
            "Comments" => list.OrderByDescending(p => p.Comments.Count()),
            _ => list.OrderByDescending(p => p.DateCreated),
        };

        var items = new List<object>();
        foreach (var post in list.Take(widget.Int("numberofposts", 10)))
        {
            items.Add(new
            {
                title = post.Title,
                url = await links.PostAsync(post, cancellationToken),
                description = post.Description,
                first_image = HtmlText.FirstImageSource(post.Content) ?? string.Empty,
                date_created = await clock.ToBlogTimeAsync(post.DateCreated),
            });
        }

        return new
        {
            posts = items,
            show_image = widget.Bool("showimg", false),
            show_description = widget.Bool("showdesc", false),
            show_date = widget.Bool("showdate", false),
        };
    }
}

/// <summary>Nuvola di tag (porting di TagCloud): la classe CSS indica il "peso" del tag.</summary>
public sealed class TagCloudWidget(IPostService posts, ILinkGenerator links) : IWidgetDataProvider
{
    public string Name => "TagCloud";

    public async Task<object?> GetDataAsync(WidgetModel widget, CancellationToken cancellationToken)
    {
        var minimum = widget.Int("minimumposts", 1);
        var size = widget.Int("tagcloudsize", -1);
        var counts = (await posts.GetVisiblePostsAsync(cancellationToken))
            .SelectMany(p => p.Tags)
            .GroupBy(t => t, StringComparer.Ordinal)
            .Select(g => (Tag: g.Key, Count: g.Count()))
            .Where(t => t.Count >= minimum)
            .OrderBy(t => t.Tag, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (size > 0)
        {
            counts = counts.Take(size).ToList();
        }

        var max = counts.Count == 0 ? 1 : counts.Max(t => t.Count);
        var tags = counts
            .Select(t => (t.Tag, t.Count, Weight: t.Count / (double)max * 100))
            .Where(t => t.Weight >= 3)
            .Select(t => new
            {
                name = t.Tag,
                url = links.Tag(t.Tag),
                count = t.Count,
                css_class = t.Weight >= 99 ? "biggest" : t.Weight >= 70 ? "big" : t.Weight >= 40 ? "medium" : t.Weight >= 20 ? "small" : "smallest",
            })
            .ToList();

        return new { tags };
    }
}

public static class BuiltInWidgetRegistration
{
    public static IServiceCollection AddBuiltInWidgets(this IServiceCollection services)
    {
        services.AddSingleton<BlogRollFeeds>();
        services.AddScoped<IWidgetDataProvider, AuthorListWidget>();
        services.AddScoped<IWidgetDataProvider, BlogListWidget>();
        services.AddScoped<IWidgetDataProvider, BlogRollWidget>();
        services.AddScoped<IWidgetDataProvider, CategoryListWidget>();
        services.AddScoped<IWidgetDataProvider, CommentListWidget>();
        services.AddScoped<IWidgetDataProvider, LinkListWidget>();
        services.AddScoped<IWidgetDataProvider, MonthListWidget>();
        services.AddScoped<IWidgetDataProvider, PageListWidget>();
        services.AddScoped<IWidgetDataProvider, PostListWidget>();
        services.AddScoped<IWidgetDataProvider, TagCloudWidget>();
        return services;
    }
}

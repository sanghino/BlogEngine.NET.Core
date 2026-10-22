using System.Globalization;
using System.Text.RegularExpressions;
using BlogEngine.Core.Files;
using BlogEngine.Core.Services;
using BlogEngine.Events;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Settings;
using BlogEngine.Text;
using BlogEngine.Web.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace BlogEngine.Web.Protocols;

/// <summary>
/// Feed RSS 2.0 e Atom (porting di SyndicationHandler). Stessi parametri della versione originale:
/// syndication.axd?format=rss|atom, &amp;category=id, &amp;tag=, &amp;author=, &amp;post=id (commenti del post),
/// &amp;comments=true (ultimi commenti), &amp;q= (ricerca), &amp;maxitems=N. Anche category/feed/..., tag/feed/... e post/feed/...
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public sealed partial class FeedController(
    IPostService posts,
    ICategoryService categories,
    ISettingsService settings,
    IRightsService rights,
    ILinkGenerator links,
    IEventPublisher events,
    SearchService search,
    FileStore files,
    IBlogContext context) : Controller
{
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".mp3"] = "audio/mpeg", [".m4a"] = "audio/mp4", [".ogg"] = "audio/ogg", [".oga"] = "audio/ogg", [".wav"] = "audio/wav",
        [".mp4"] = "video/mp4", [".m4v"] = "video/mp4", [".webm"] = "video/webm", [".ogv"] = "video/ogg", [".pdf"] = "application/pdf",
    };

    [HttpGet("/syndication.axd")]
    public async Task<IActionResult> Syndication(string? format, string? category, string? tag, string? author, string? post, string? comments, string? q, int? maxitems)
    {
        if (!await rights.IsAuthorizedAsync(Rights.ViewPublicPosts))
        {
            return Unauthorized();
        }

        var blogSettings = await settings.GetAsync();
        var title = blogSettings.Name;
        IEnumerable<FeedItem> items;

        if (!string.IsNullOrEmpty(category))
        {
            if (!Guid.TryParse(category.Length >= 36 ? category[..36] : category, out var categoryId) || await categories.GetByIdAsync(categoryId) is not { } found)
            {
                return NotFound();
            }

            title = $"{title} - {found.Title}";
            items = await PostItemsAsync(await posts.GetByCategoryAsync(categoryId), blogSettings);
        }
        else if (!string.IsNullOrEmpty(tag))
        {
            items = await PostItemsAsync((await posts.GetVisiblePostsAsync()).Where(p => p.Tags.Any(t => string.Equals(t, tag.Trim(), StringComparison.OrdinalIgnoreCase) || Slug.AreEquivalent(t, tag))).ToList(), blogSettings);
        }
        else if (!string.IsNullOrEmpty(author))
        {
            title = $"{title} - {author}";
            items = await PostItemsAsync(await posts.GetByAuthorAsync(author), blogSettings);
        }
        else if (!string.IsNullOrEmpty(post))
        {
            if (!Guid.TryParse(post, out var postId) || await posts.GetByIdAsync(postId) is not { } found || !await posts.IsVisibleAsync(found))
            {
                return NotFound();
            }

            title = $"{title} - {found.Title}";
            items = await CommentItemsAsync([found], blogSettings, int.MaxValue);
        }
        else if (!string.IsNullOrEmpty(comments))
        {
            title = $"{title} - Comments";
            items = await CommentItemsAsync(await posts.GetVisiblePostsAsync(), blogSettings, Max(maxitems, blogSettings));
        }
        else if (!string.IsNullOrEmpty(q))
        {
            var hits = (await search.SearchAsync(q, includeComments: false)).Where(h => h.Post is not null).Select(h => h.Post!).ToList();
            items = await PostItemsAsync(hits, blogSettings, keepOrder: true);
        }
        else
        {
            items = await PostItemsAsync(await posts.GetVisiblePostsAsync(), blogSettings);
        }

        var list = items.Take(Max(maxitems, blogSettings)).ToList();
        return Feed(format, blogSettings, title, list);
    }

    [HttpGet("/category/feed/{slug}")]
    public async Task<IActionResult> CategoryFeed(string slug, string? format) =>
        await categories.GetBySlugAsync(slug) is { } category ? await Syndication(format, category.Id.ToString(), null, null, null, null, null, null) : NotFound();

    [HttpGet("/tag/feed/{tag}")]
    public Task<IActionResult> TagFeed(string tag, string? format) => Syndication(format, null, tag, null, null, null, null, null);

    [HttpGet("/post/feed/{slug}")]
    public async Task<IActionResult> PostFeed(string slug, string? format) =>
        await posts.GetBySlugAsync(slug) is { } post ? await Syndication(format, null, null, null, post.Id.ToString(), null, null, null) : NotFound();

    private IActionResult Feed(string? format, BlogSettings blogSettings, string title, IReadOnlyList<FeedItem> items)
    {
        // Risposta "non modificato" ai lettori di feed che hanno già l'ultima versione.
        var lastModified = items.Count == 0 ? DateTime.UtcNow.Date : items.Max(i => i.UpdatedUtc);
        lastModified = new DateTime(lastModified.Ticks - (lastModified.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        if (DateTimeOffset.TryParse(Request.Headers.IfModifiedSince, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var since) && since.UtcDateTime >= lastModified)
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        Response.Headers[HeaderNames.LastModified] = lastModified.ToString("r", CultureInfo.InvariantCulture);
        var root = links.ToAbsolute(links.BlogRoot).ToString();
        var atom = string.Equals(format ?? blogSettings.SyndicationFormat, "atom", StringComparison.OrdinalIgnoreCase);
        var channel = new FeedChannel
        {
            Title = title,
            Description = blogSettings.Description,
            Link = root,
            SelfLink = $"{root}syndication.axd?format={(atom ? "atom" : "rss")}",
            Language = blogSettings.Language,
            AuthorName = blogSettings.AuthorName,
            FeedAuthor = blogSettings.FeedAuthor,
            PingbackServer = blogSettings.EnablePingBackReceive ? $"{root}pingback.axd" : string.Empty,
            Endorsement = blogSettings.Endorsement,
            Latitude = blogSettings.GeocodingLatitude,
            Longitude = blogSettings.GeocodingLongitude,
            Generator = AdminViewContext.Version,
        };

        Response.Headers[HeaderNames.ContentDisposition] = atom ? "inline; filename=atom.xml" : "inline; filename=rss.xml";
        return Content(atom ? FeedWriter.Atom(channel, items) : FeedWriter.Rss(channel, items), atom ? "application/atom+xml; charset=utf-8" : "application/rss+xml; charset=utf-8");
    }

    private async Task<List<FeedItem>> PostItemsAsync(IEnumerable<Post> source, BlogSettings blogSettings, bool keepOrder = false)
    {
        var visible = source.Where(p => p.IsPublished && !p.IsDeleted);
        if (!keepOrder)
        {
            visible = visible.OrderByDescending(p => p.DateCreated);
        }

        var root = links.ToAbsolute(links.BlogRoot).ToString();
        var result = new List<FeedItem>();
        foreach (var post in visible.Take(Math.Max(blogSettings.PostsPerFeed, 1) * 2))
        {
            var serving = new ServingEvent<Post> { BlogId = post.BlogId, Item = post, Body = post.Content, Location = ServingLocation.Feed };
            await events.PublishAsync(serving);
            if (serving.Cancel)
            {
                continue;
            }

            var link = links.ToAbsolute(await links.PostAsync(post)).ToString();
            var content = Absolute(serving.Body, links.ToAbsolute("/").ToString());
            var categoryTitles = new List<string>();
            foreach (var id in post.Categories)
            {
                if (await categories.GetByIdAsync(id) is { } category)
                {
                    categoryTitles.Add(category.Title);
                }
            }

            result.Add(new FeedItem
            {
                Title = post.Title,
                Link = link,
                PermaLink = $"{root}post.aspx?id={post.Id}",
                Content = content,
                Author = post.Author,
                PublishedUtc = post.DateCreated,
                UpdatedUtc = post.DateModified > post.DateCreated ? post.DateModified : post.DateCreated,
                Categories = categoryTitles,
                Tags = blogSettings.EnableTagExport ? post.Tags : [],
                CommentsLink = link + "#comment",
                CommentsFeed = $"{root}syndication.axd?post={post.Id}",
                CommentCount = post.ApprovedComments(blogSettings.EnableCommentsModeration).Count(),
                TrackbackLink = blogSettings.EnableTrackBackReceive ? $"{root}trackback.axd?id={post.Id}" : null,
                Enclosure = blogSettings.EnableEnclosures ? FindEnclosure(content, root) : null,
            });
        }

        return result;
    }

    private async Task<List<FeedItem>> CommentItemsAsync(IEnumerable<Post> source, BlogSettings blogSettings, int max)
    {
        var root = links.ToAbsolute(links.BlogRoot).ToString();
        var result = new List<FeedItem>();
        var recent = source
            .SelectMany(p => p.ApprovedComments(blogSettings.EnableCommentsModeration).Where(c => c.IsPublished).Select(c => (Post: p, Comment: c)))
            .OrderByDescending(x => x.Comment.DateCreated)
            .Take(max);

        foreach (var (post, comment) in recent)
        {
            var serving = new ServingEvent<Comment> { BlogId = comment.BlogId, Item = comment, Body = System.Net.WebUtility.HtmlEncode(comment.Content), Location = ServingLocation.Feed };
            await events.PublishAsync(serving);
            if (serving.Cancel)
            {
                continue;
            }

            var link = links.ToAbsolute(await links.CommentAsync(post, comment)).ToString();
            result.Add(new FeedItem
            {
                Title = $"{comment.Author} on {post.Title}",
                Link = link,
                PermaLink = link,
                Content = Absolute(serving.Body.Replace("\n", "<br />", StringComparison.Ordinal), links.ToAbsolute("/").ToString()),
                Author = comment.Author,
                PublishedUtc = comment.DateCreated,
                UpdatedUtc = comment.DateCreated,
            });
        }

        return result;
    }

    /// <summary>Gli indirizzi relativi al sito ("/...") diventano assoluti: il feed viene letto fuori dal sito.</summary>
    private static string Absolute(string html, string authority) =>
        RelativeUrlRegex().Replace(html, m => $"{m.Groups["attr"].Value}=\"{authority}{m.Groups["path"].Value}\"");

    /// <summary>Primo file audio/video del sito citato nel testo, con la sua dimensione (porting di GetEnclosure).</summary>
    private (string Url, long Length, string Type)? FindEnclosure(string html, string root)
    {
        foreach (Match match in MediaLinkRegex().Matches(html))
        {
            var url = System.Net.WebUtility.HtmlDecode(match.Groups["url"].Value);
            if (!url.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = url[root.Length..];
            string? path = null;
            if (relative.StartsWith("file.axd?file=", StringComparison.OrdinalIgnoreCase))
            {
                path = Uri.UnescapeDataString(relative["file.axd?file=".Length..]);
            }
            else if (relative.StartsWith("media/", StringComparison.OrdinalIgnoreCase))
            {
                path = "/media/" + Uri.UnescapeDataString(relative["media/".Length..]);
            }

            if (path is null || !MediaTypes.TryGetValue(Path.GetExtension(path.Split('?')[0]), out var type))
            {
                continue;
            }

            try
            {
                var file = new FileInfo(files.Resolve(context.Blog.Id, path));
                if (file.Exists)
                {
                    return (url, file.Length, type);
                }
            }
            catch (ArgumentException)
            {
                // Percorso non valido.
            }
        }

        return null;
    }

    private static int Max(int? requested, BlogSettings blogSettings) =>
        Math.Clamp(requested ?? blogSettings.PostsPerFeed, 1, 500);

    [GeneratedRegex("(?<attr>href|src)=\"/(?!/)(?<path>[^\"]*)\"", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeUrlRegex();

    [GeneratedRegex("(?:href|src)=\"(?<url>[^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex MediaLinkRegex();
}

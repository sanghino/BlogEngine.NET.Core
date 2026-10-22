using System.Net;
using System.Text.RegularExpressions;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Web.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BlogEngine.Web.Protocols;

/// <summary>
/// Notifiche di altri blog che citano un post (porting di PingbackHandler e TrackbackHandler).
/// Ogni notifica viene verificata scaricando la pagina d'origine, che deve contenere il link al post,
/// e salvata come commento di tipo pingback o trackback (soggetto alla moderazione).
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public sealed partial class LinkbackController(
    IPostService posts,
    ICommentService comments,
    ISettingsService settings,
    ILinkGenerator links,
    IRemotePages remote,
    ILogger<LinkbackController> logger) : Controller
{
    /// <summary>pingback.ping(sourceURI, targetURI) via XML-RPC.</summary>
    [HttpPost("/pingback.axd")]
    [EnableRateLimiting(Public.BlogActionsController.FormsRateLimit)]
    public async Task<IActionResult> Pingback()
    {
        string body;
        using (var reader = new StreamReader(Request.Body))
        {
            body = await reader.ReadToEndAsync(HttpContext.RequestAborted);
        }

        try
        {
            if (!(await settings.GetAsync()).EnablePingBackReceive)
            {
                throw new XmlRpcFaultException(0, "Pingbacks are disabled.");
            }

            var call = XmlRpc.Parse(body);
            if (!string.Equals(call.Method, "pingback.ping", StringComparison.Ordinal))
            {
                throw new XmlRpcFaultException(-32601, $"Method {call.Method} not supported.");
            }

            if (!Uri.TryCreate(call.String(0), UriKind.Absolute, out var source))
            {
                throw new XmlRpcFaultException(16, "The source URI does not exist.");
            }

            if (await FindPostAsync(call.String(1)) is not { } post)
            {
                throw new XmlRpcFaultException(33, "The specified target URI cannot be used as a target.");
            }

            if (post.Comments.Any(c => c.IsPingbackOrTrackback && SameUrl(c.Website, source)))
            {
                throw new XmlRpcFaultException(48, "The pingback has already been registered.");
            }

            var page = await remote.GetAsync(source, HttpContext.RequestAborted) ?? throw new XmlRpcFaultException(16, "The source URI does not exist.");
            var target = links.ToAbsolute(await links.PostAsync(post)).ToString();
            if (!LinksTo(page.Html, target, post))
            {
                throw new XmlRpcFaultException(17, "The source URI does not contain a link to the target URI.");
            }

            var title = PageTitle(page.Html) ?? source.Host;
            await AddAsync(post, Comment.PingbackEmail, title, source, $"Pingback from {source.Host}\n\n{title}");
            return Content(XmlRpc.Response("Thanks! Your pingback has been registered."), "text/xml; charset=utf-8");
        }
        catch (XmlRpcFaultException ex)
        {
            logger.LogInformation("Pingback rifiutato ({Code}): {Message}", ex.Code, ex.Message);
            return Content(XmlRpc.Fault(ex.Code, ex.Message), "text/xml; charset=utf-8");
        }
    }

    /// <summary>Trackback: POST trackback.axd?id=post con title, excerpt, url e blog_name.</summary>
    [HttpPost("/trackback.axd")]
    [EnableRateLimiting(Public.BlogActionsController.FormsRateLimit)]
    public async Task<IActionResult> Trackback(Guid id, [FromForm] string? title, [FromForm] string? excerpt, [FromForm] string? url, [FromForm(Name = "blog_name")] string? blogName)
    {
        if (!(await settings.GetAsync()).EnableTrackBackReceive)
        {
            return TrackbackResponse("Trackbacks are disabled.");
        }

        if (await posts.GetByIdAsync(id) is not { } post || !await posts.IsVisibleAsync(post))
        {
            return TrackbackResponse("The post does not exist.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var source))
        {
            return TrackbackResponse("The url parameter is required.");
        }

        if (post.Comments.Any(c => c.IsPingbackOrTrackback && SameUrl(c.Website, source)))
        {
            return TrackbackResponse("The trackback has already been registered.");
        }

        var page = await remote.GetAsync(source, HttpContext.RequestAborted);
        if (page is null || !LinksTo(page.Html, links.ToAbsolute(await links.PostAsync(post)).ToString(), post))
        {
            return TrackbackResponse("The source page does not link to the post.");
        }

        var author = string.IsNullOrWhiteSpace(blogName) ? source.Host : blogName.Trim();
        var text = string.IsNullOrWhiteSpace(title) ? author : title.Trim();
        if (!string.IsNullOrWhiteSpace(excerpt))
        {
            text += "\n\n" + BlogEngine.Text.HtmlText.ToPlainText(excerpt);
        }

        try
        {
            await AddAsync(post, Comment.TrackbackEmail, author, source, text.Length > 2000 ? text[..2000] : text);
        }
        catch (XmlRpcFaultException ex)
        {
            return TrackbackResponse(ex.Message);
        }

        return TrackbackResponse(null);
    }

    /// <summary>Senza parametri risponde con un errore, come i vecchi gestori.</summary>
    [HttpGet("/trackback.axd")]
    [HttpGet("/pingback.axd")]
    public IActionResult Info() => Content("This address only accepts POST requests.", "text/plain");

    private async Task AddAsync(Post post, string email, string author, Uri website, string content)
    {
        var comment = new Comment
        {
            Author = author.Length > 100 ? author[..100] : author,
            Email = email,
            Website = website,
            Content = content,
            IP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
        };

        try
        {
            if (await comments.AddAsync(EntityCopies.Copy(post), comment))
            {
                return;
            }
        }
        catch (InvalidOperationException)
        {
            // Commenti chiusi per questo post.
        }

        throw new XmlRpcFaultException(0, "The notification was rejected.");
    }

    /// <summary>Post indicato dall'indirizzo: /post/slug, /post/aaaa/mm/gg/slug, post.aspx?id=...</summary>
    private async Task<Post?> FindPostAsync(string target)
    {
        if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || !string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Post? post = null;
        var id = QueryIdRegex().Match(uri.Query);
        if (id.Success && Guid.TryParse(id.Groups["id"].Value, out var postId))
        {
            post = await posts.GetByIdAsync(postId);
        }
        else
        {
            var slug = Uri.UnescapeDataString(uri.AbsolutePath.TrimEnd('/').Split('/').Last());
            if (slug.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
            {
                slug = slug[..^5];
            }

            post = await posts.GetBySlugAsync(slug);
        }

        return post is not null && await posts.IsVisibleAsync(post) ? post : null;
    }

    /// <summary>La pagina contiene un link al post (indirizzo completo, relativo o con l'id).</summary>
    private bool LinksTo(string html, string target, Post post)
    {
        var decoded = WebUtility.HtmlDecode(html);
        var path = new Uri(target).AbsolutePath;
        return decoded.Contains(target, StringComparison.OrdinalIgnoreCase)
            || decoded.Contains(Uri.UnescapeDataString(target), StringComparison.OrdinalIgnoreCase)
            || decoded.Contains($"{Request.Host.Host}{path}", StringComparison.OrdinalIgnoreCase)
            || decoded.Contains(post.Id.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string? PageTitle(string html)
    {
        var match = TitleRegex().Match(html);
        var title = match.Success ? WebUtility.HtmlDecode(match.Groups["title"].Value).Trim() : null;
        return string.IsNullOrWhiteSpace(title) ? null : title.Length > 100 ? title[..100] : title;
    }

    private static bool SameUrl(Uri? a, Uri b) => a is not null && string.Equals(a.ToString().TrimEnd('/'), b.ToString().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private ContentResult TrackbackResponse(string? error) => Content(
        error is null
            ? "<?xml version=\"1.0\" encoding=\"utf-8\"?><response><error>0</error></response>"
            : $"<?xml version=\"1.0\" encoding=\"utf-8\"?><response><error>1</error><message>{WebUtility.HtmlEncode(error)}</message></response>",
        "text/xml; charset=utf-8");

    [GeneratedRegex(@"[?&]id=(?<id>[0-9a-fA-F-]{32,36})")]
    private static partial Regex QueryIdRegex();

    [GeneratedRegex(@"<title[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleRegex();
}

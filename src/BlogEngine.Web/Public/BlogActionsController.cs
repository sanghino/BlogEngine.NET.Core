using System.Net.Mail;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api;
using BlogEngine.Web.Hosting;
using BlogEngine.Web.Themes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BlogEngine.Web.Public;

/// <summary>Dati di un nuovo commento inviati da blog.js.</summary>
public sealed class CommentForm
{
    public Guid PostId { get; set; }

    public Guid ParentId { get; set; }

    public string? Author { get; set; }

    public string? Email { get; set; }

    public string? Website { get; set; }

    public string? Country { get; set; }

    public string? Content { get; set; }

    public bool Notify { get; set; }

    /// <summary>Campo nascosto lasciato vuoto dalle persone: se compilato, è un programma automatico.</summary>
    public string? Hp { get; set; }
}

/// <summary>
/// Azioni dei visitatori e comandi rapidi di moderazione dalle pagine pubbliche: sostituiscono i
/// callback di Web Forms (commenti), rating.axd e i vecchi link "?deletepost=", "?approvecomment=".
/// Tutte le richieste devono contenere il token antifalsificazione che blog.js legge da BlogEngineRes.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
[AutoValidateAntiforgeryToken]
public sealed class BlogActionsController(
    SiteRenderer site,
    PublicContent content,
    IPostService posts,
    IPageService pages,
    ICommentService comments,
    IBlogContext context,
    NewsletterSubscribers newsletter,
    ILinkGenerator links,
    ILogger<BlogActionsController> logger) : Controller
{
    public const string FormsRateLimit = "forms";
    private const string RatingCookie = "rating";

    /// <summary>Aggiunge un commento e restituisce il suo HTML, da inserire nella pagina.</summary>
    [HttpPost("/comment")]
    [EnableRateLimiting(FormsRateLimit)]
    public async Task<IActionResult> AddComment([FromForm] CommentForm form)
    {
        if (!string.IsNullOrEmpty(form.Hp))
        {
            return BadRequest(new { message = "Invalid request." });
        }

        var author = form.Author?.Trim() ?? string.Empty;
        var email = form.Email?.Trim() ?? string.Empty;
        var text = form.Content?.Trim() ?? string.Empty;
        var errors = new List<string>();
        if (author.Length is 0 or > 100)
        {
            errors.Add($"{site.T("name")}: {site.T("required")}");
        }

        if (email.Length is 0 or > 200 || !MailAddress.TryCreate(email, out _))
        {
            errors.Add(site.T("enterValidEmail"));
        }

        if (text.Length is 0 or > 10000)
        {
            errors.Add($"{site.T("comment")}: {site.T("required")}");
        }

        if (errors.Count > 0)
        {
            return BadRequest(new { message = string.Join(" ", errors) });
        }

        if (await posts.GetByIdAsync(form.PostId) is not { } post || !await posts.IsVisibleAsync(post))
        {
            return NotFound();
        }

        if (!await comments.AreCommentsOpenAsync(post) || !await content.CanAsync(Rights.CreateComments))
        {
            return BadRequest(new { message = site.T("commentsAreClosed") });
        }

        var blogSettings = await site.GetSettingsAsync();
        var comment = new Comment
        {
            Author = author,
            Email = email,
            Content = text,
            ParentId = blogSettings.IsCommentNestingEnabled && post.Comments.Any(c => c.Id == form.ParentId) ? form.ParentId : Guid.Empty,
            Website = blogSettings.EnableWebsiteInComments ? ParseWebsite(form.Website) : null,
            Country = blogSettings.EnableCountryInComments && form.Country is { Length: 2 } country ? country.ToLowerInvariant() : string.Empty,
            IP = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
        };

        var copy = EntityCopies.Copy(post);
        if (form.Notify && !copy.NotificationEmails.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            copy.NotificationEmails.Add(email);
        }

        if (!await comments.AddAsync(copy, comment))
        {
            // Un'estensione (es. un captcha o un filtro) ha rifiutato il commento.
            return BadRequest(new { message = "The comment was rejected." });
        }

        var model = await content.CommentAsync(copy, new CommentNode(comment), commentsOpen: true);
        var html = model is null ? string.Empty : await site.PartialAsync("comment-view", new Dictionary<string, object?>
        {
            ["comment"] = model,
            ["post"] = new { author = post.Author },
        });

        return Json(new { html, approved = comment.IsApproved && !comment.IsSpam, id = comment.Id });
    }

    /// <summary>Voto da 1 a 5 stelle; ogni visitatore può votare un post una sola volta (cookie).</summary>
    [HttpPost("/rating")]
    [EnableRateLimiting(FormsRateLimit)]
    public async Task<IActionResult> Rate([FromForm] Guid id, [FromForm] int rating)
    {
        var blogSettings = await site.GetSettingsAsync();
        if (!blogSettings.EnableRating || rating is < 1 or > 5)
        {
            return BadRequest(new { status = "ERROR" });
        }

        var rated = (Request.Cookies[RatingCookie] ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (rated.Contains(id.ToString("N")))
        {
            return Json(new { status = "HASRATED" });
        }

        if (await posts.GetByIdAsync(id) is not { } post || !await posts.IsVisibleAsync(post) || !await content.CanAsync(Rights.SubmitRatingsOnPosts))
        {
            return NotFound(new { status = "ERROR" });
        }

        await posts.RateAsync(EntityCopies.Copy(post), rating);
        rated.Add(id.ToString("N"));
        Response.Cookies.Append(RatingCookie, string.Join(',', rated.TakeLast(200)), new CookieOptions
        {
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
        });

        return Json(new { status = "OK", rating });
    }

    [HttpPost("/newsletter")]
    [EnableRateLimiting(FormsRateLimit)]
    public async Task<IActionResult> Subscribe([FromForm] string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 200 || !MailAddress.TryCreate(email.Trim(), out _))
        {
            return BadRequest(new { message = site.T("enterEmailAddress") });
        }

        await newsletter.AddAsync(email.Trim());
        return Json(new { message = site.T("thankYou") });
    }

    /// <summary>
    /// Comandi rapidi dalle pagine pubbliche per chi ha i permessi: approvare o eliminare commenti,
    /// approvarli tutti, eliminare un post o una pagina. I servizi verificano i permessi.
    /// </summary>
    [HttpPost("/moderation/{command}/{id:guid}")]
    public async Task<IActionResult> Moderate(string command, Guid id)
    {
        if (string.IsNullOrEmpty(context.UserName))
        {
            return Unauthorized();
        }

        try
        {
            switch (command.ToLowerInvariant())
            {
                case "approve-comment":
                case "delete-comment":
                case "delete-comment-and-replies":
                    if (await FindCommentAsync(id) is not { } found)
                    {
                        return NotFound();
                    }

                    if (command.Equals("approve-comment", StringComparison.OrdinalIgnoreCase))
                    {
                        await comments.ApproveAsync(found.Post, found.Comment);
                    }
                    else
                    {
                        // Le risposte si eliminano prima del commento a cui rispondono.
                        if (command.EndsWith("replies", StringComparison.OrdinalIgnoreCase))
                        {
                            foreach (var reply in Descendants(found.Post, found.Comment.Id).Reverse())
                            {
                                await comments.RemoveAsync(found.Post, reply);
                            }
                        }

                        await comments.RemoveAsync(found.Post, found.Comment);
                    }

                    break;

                case "approve-all":
                    if (await posts.GetByIdAsync(id) is not { } post || !await content.CanAsync(Rights.ModerateComments))
                    {
                        return NotFound();
                    }

                    await comments.ApproveAllAsync(post);
                    break;

                case "delete-post":
                    if (await posts.GetByIdAsync(id) is not { } toDelete || !await posts.CanDeleteAsync(toDelete))
                    {
                        return Forbid();
                    }

                    await posts.DeleteAsync(toDelete);
                    return Json(new { redirect = links.BlogRoot });

                case "delete-page":
                    if (await pages.GetByIdAsync(id) is not { } page)
                    {
                        return NotFound();
                    }

                    await pages.DeleteAsync(EntityCopies.Copy(page));
                    return Json(new { redirect = links.BlogRoot });

                default:
                    return NotFound();
            }
        }
        catch (UnauthorizedAccessException)
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        return Json(new { ok = true });
    }

    private async Task<(Post Post, Comment Comment)?> FindCommentAsync(Guid id)
    {
        foreach (var post in await posts.GetPostsAsync())
        {
            if (post.Comments.FirstOrDefault(c => c.Id == id) is { } comment)
            {
                return (post, comment);
            }
        }

        return null;
    }

    private static IEnumerable<Comment> Descendants(Post post, Guid parentId)
    {
        foreach (var child in post.Comments.Where(c => c.ParentId == parentId).ToList())
        {
            yield return child;
            foreach (var grandChild in Descendants(post, child.Id))
            {
                yield return grandChild;
            }
        }
    }

    private Uri? ParseWebsite(string? website)
    {
        if (string.IsNullOrWhiteSpace(website))
        {
            return null;
        }

        var value = website.Trim();
        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "http://" + value;
        }

        if (value.Length <= 300 && Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri;
        }

        logger.LogDebug("Sito web del commento ignorato: {Website}", website);
        return null;
    }
}

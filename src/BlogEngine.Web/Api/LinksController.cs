using System.Globalization;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api.Models;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Api;

/// <summary>
/// Ricerca dei post e delle pagine pubblicati, per inserirne il link dall'editor ("Inserisci link" → Sfoglia).
/// Senza testo di ricerca restituisce le pagine e i post più recenti.
/// </summary>
[Route("api/links")]
public sealed class LinksController(
    IPostService posts,
    IPageService pages,
    IRightsService rights,
    ILinkGenerator links,
    AdminMapper mapper) : AdminApiController
{
    private const int MaxResults = 50;

    [HttpGet("")]
    public async Task<IEnumerable<InternalLink>> Get(string q = "", int take = 20)
    {
        if (!await rights.IsAuthorizedAsync(Rights.CreateNewPosts) && !await rights.IsAuthorizedAsync(Rights.CreateNewPages))
        {
            throw new UnauthorizedAccessException();
        }

        take = Math.Clamp(take, 1, MaxResults);
        var search = q.Trim();
        var result = new List<InternalLink>();

        // Prima le pagine (sono poche), poi i post dal più recente.
        var foundPages = (await pages.GetPagesAsync(HttpContext.RequestAborted))
            .Where(p => p.IsPublished && Matches(p.Title, search))
            .OrderBy(p => p.Title, StringComparer.CurrentCultureIgnoreCase)
            .Take(take);
        foreach (var page in foundPages)
        {
            result.Add(new InternalLink
            {
                Type = "page",
                Title = page.Title,
                Url = await links.PageAsync(page),
                Date = await mapper.FormatDateAsync(page.DateCreated, "yyyy-MM-dd"),
            });
        }

        var foundPosts = (await posts.GetPostsAsync(HttpContext.RequestAborted))
            .Where(p => p.IsPublished && Matches(p.Title, search))
            .OrderByDescending(p => p.DateCreated)
            .Take(take - result.Count);
        foreach (var post in foundPosts)
        {
            result.Add(new InternalLink
            {
                Type = "post",
                Title = post.Title,
                Url = await links.PostAsync(post),
                Date = await mapper.FormatDateAsync(post.DateCreated, "yyyy-MM-dd"),
            });
        }

        return result;
    }

    /// <summary>Tutte le parole cercate compaiono nel titolo, senza badare a maiuscole e accenti.</summary>
    private static bool Matches(string? title, string search)
    {
        if (search.Length == 0)
        {
            return true;
        }

        var compare = CultureInfo.InvariantCulture.CompareInfo;
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return search.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .All(word => compare.IndexOf(title ?? string.Empty, word, options) >= 0);
    }
}

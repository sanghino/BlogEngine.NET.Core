using System.Text.RegularExpressions;
using BlogEngine.Core.Data;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Text;

namespace BlogEngine.Core.Services;

/// <summary>Risultato della ricerca: un post, una pagina o un commento (con il suo post).</summary>
public sealed record SearchHit(string Type, string Title, string Text, DateTime DateCreated, double Score, Post? Post = null, Page? Page = null, Comment? Comment = null);

/// <summary>
/// Ricerca nei contenuti visibili del blog (porting della classe Search). Le parole comuni
/// (stop words) vengono ignorate; i risultati sono ordinati per rilevanza: una parola nel titolo
/// conta più di una nei tag, nella descrizione o nel testo.
/// </summary>
public sealed partial class SearchService(IPostService posts, IPageService pages, IStringListStore lists, IBlogContext context)
{
    private const double TitleWeight = 20;
    private const double TagWeight = 12;
    private const double DescriptionWeight = 6;
    private const double ContentWeight = 1;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string? query, bool includeComments, CancellationToken cancellationToken = default)
    {
        var terms = await GetTermsAsync(query, cancellationToken).ConfigureAwait(false);
        if (terms.Count == 0)
        {
            return [];
        }

        var hits = new List<SearchHit>();
        foreach (var post in await posts.GetVisiblePostsAsync(cancellationToken).ConfigureAwait(false))
        {
            var score = Score(terms, post.Title, string.Join(' ', post.Tags), post.Description, post.Content);
            if (score > 0)
            {
                hits.Add(new SearchHit("post", post.Title, Text(post.Description, post.Content), post.DateCreated, score, Post: post));
            }

            if (includeComments)
            {
                foreach (var comment in post.Comments.Where(c => c.IsPublished && !c.IsPingbackOrTrackback))
                {
                    var commentScore = Score(terms, string.Empty, string.Empty, comment.Author, comment.Content);
                    if (commentScore > 0)
                    {
                        hits.Add(new SearchHit("comment", post.Title, Text(null, comment.Content), comment.DateCreated, commentScore, Post: post, Comment: comment));
                    }
                }
            }
        }

        foreach (var page in await pages.GetVisiblePagesAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!page.IsPublished)
            {
                continue;
            }

            var score = Score(terms, page.Title, page.Keywords, page.Description, page.Content);
            if (score > 0)
            {
                hits.Add(new SearchHit("page", page.Title, Text(page.Description, page.Content), page.DateCreated, score, Page: page));
            }
        }

        return hits.OrderByDescending(h => h.Score).ThenByDescending(h => h.DateCreated).ToList();
    }

    /// <summary>Parole della ricerca, senza duplicati, parole troppo corte e stop words.</summary>
    public async Task<IReadOnlyList<string>> GetTermsAsync(string? query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var stopWords = (await lists.LoadAsync(context.Blog.Id, StringListKind.StopWords, cancellationToken).ConfigureAwait(false))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return WordRegex().Matches(HtmlText.Strip(query))
            .Select(m => m.Value)
            .Where(w => w.Length > 1 && !stopWords.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private static double Score(IReadOnlyList<string> terms, string title, string tags, string description, string content)
    {
        var plainContent = HtmlText.Strip(content);
        double score = 0;
        foreach (var term in terms)
        {
            var pattern = new Regex($@"\b{Regex.Escape(term)}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            score += (pattern.Matches(title).Count * TitleWeight)
                + (pattern.Matches(tags).Count * TagWeight)
                + (pattern.Matches(description).Count * DescriptionWeight)
                + (pattern.Matches(plainContent).Count * ContentWeight);
        }

        return score;
    }

    private static string Text(string? description, string content)
    {
        var text = string.IsNullOrWhiteSpace(description) ? HtmlText.Strip(content) : HtmlText.Strip(description);
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > 200 ? text[..200] + "..." : text;
    }

    [GeneratedRegex(@"[\p{L}\p{N}][\p{L}\p{N}\-_']*")]
    private static partial Regex WordRegex();
}

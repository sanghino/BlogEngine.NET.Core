using System.Globalization;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Text;

namespace BlogEngine.Core.Services;

/// <summary>
/// Link pubblici con la stessa struttura della versione .NET Framework, senza l'estensione ".aspx":
/// post/{slug} (oppure post/aaaa/mm/gg/{slug}), page/{slug}, category/{slug}, category/feed/{slug},
/// tag/{tag}, author/{utente}, archive, contact, search?q=.
/// </summary>
public sealed class LinkGenerator(IBlogContext context, IBlogService blogs, ISettingsService settings) : ILinkGenerator
{
    public string BlogRoot => BlogResolver.RelativeRoot(context.Blog);

    public async Task<string> PostAsync(Post post, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(post);
        var blog = await GetBlogAsync(post.BlogId, cancellationToken).ConfigureAwait(false);
        var blogSettings = await settings.GetAsync(blog.Id, cancellationToken).ConfigureAwait(false);
        var date = blogSettings.TimeStampPostLinks
            ? post.DateCreated.ToString("yyyy/MM/dd/", CultureInfo.InvariantCulture)
            : string.Empty;

        return ForBlog(blog, $"post/{date}{Segment(post.Slug)}");
    }

    public async Task<string> PageAsync(Page page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(page);
        var blog = await GetBlogAsync(page.BlogId, cancellationToken).ConfigureAwait(false);
        return ForBlog(blog, $"page/{Segment(page.Slug)}");
    }

    public async Task<string> CommentAsync(Post post, Comment comment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(comment);
        return $"{await PostAsync(post, cancellationToken).ConfigureAwait(false)}#id_{comment.Id}";
    }

    public Task<string> CategoryAsync(Category category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        return Task.FromResult($"{BlogRoot}category/{Segment(Slug.Create(category.Title))}");
    }

    public Task<string> CategoryFeedAsync(Category category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        return Task.FromResult($"{BlogRoot}category/feed/{Segment(Slug.Create(category.Title))}");
    }

    public string Tag(string tag) => $"{BlogRoot}tag/{Segment(Slug.Create(tag))}";

    public string Author(string userName) => $"{BlogRoot}author/{Segment(userName)}";

    public string Archive() => $"{BlogRoot}archive";

    public string Contact() => $"{BlogRoot}contact";

    public string Search(string? query = null) =>
        string.IsNullOrWhiteSpace(query) ? $"{BlogRoot}search" : $"{BlogRoot}search?q={Uri.EscapeDataString(query)}";

    public Uri ToAbsolute(string relativeLink) => new(GetAuthority(context.Blog), relativeLink);

    /// <summary>
    /// Link relativo se il blog di destinazione ha lo stesso nome host del blog corrente,
    /// altrimenti assoluto (es. post di un blog figlio mostrati nel blog aggregatore).
    /// </summary>
    private string ForBlog(Blog blog, string path)
    {
        var relative = BlogResolver.RelativeRoot(blog) + path;
        return string.Equals(blog.Hostname, context.Blog.Hostname, StringComparison.OrdinalIgnoreCase)
            ? relative
            : new Uri(GetAuthority(blog), relative).ToString();
    }

    /// <summary>Nel blog aggregatore le categorie sono raggruppate per titolo alla radice del sito.</summary>
    private Uri GetAuthority(Blog blog)
    {
        var request = context.RequestBaseUri;
        if (!string.IsNullOrWhiteSpace(blog.Hostname))
        {
            return new UriBuilder(request?.Scheme ?? Uri.UriSchemeHttps, blog.Hostname).Uri;
        }

        return request is null
            ? throw new InvalidOperationException("Impossibile costruire un link assoluto: il blog non ha un nome host e non c'è una richiesta corrente.")
            : new Uri(request.GetLeftPart(UriPartial.Authority));
    }

    private async Task<Blog> GetBlogAsync(Guid blogId, CancellationToken cancellationToken) =>
        blogId == context.Blog.Id || blogId == Guid.Empty
            ? context.Blog
            : await blogs.GetByIdAsync(blogId, cancellationToken).ConfigureAwait(false) ?? context.Blog;

    private static string Segment(string value) => Uri.EscapeDataString(value);
}

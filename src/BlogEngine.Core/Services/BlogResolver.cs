using BlogEngine.Models;

namespace BlogEngine.Core.Services;

/// <summary>
/// Regole del multi-blog: radice relativa di un blog e scelta del blog a cui è destinata una richiesta.
/// Porting di Blog.RelativeWebRoot, Blog.CompareTo e Blog.CurrentInstance.
/// </summary>
public static class BlogResolver
{
    /// <summary>Radice relativa del blog: "~/" diventa "/", "~/blog2" diventa "/blog2/".</summary>
    public static string RelativeRoot(Blog blog)
    {
        var path = string.IsNullOrWhiteSpace(blog.VirtualPath) ? "~/" : blog.VirtualPath.Trim();
        if (path.StartsWith('~'))
        {
            path = path[1..];
        }

        path = "/" + path.Trim('/');
        return path.EndsWith('/') ? path : path + "/";
    }

    /// <summary>
    /// Ordina i blog dal criterio più specifico al più generico: prima gli attivi, poi i nomi host
    /// più lunghi, poi quelli che non accettano sottodomini, poi le radici più lunghe, infine per nome.
    /// </summary>
    public static IReadOnlyList<Blog> SortForMatching(IEnumerable<Blog> blogs) =>
        blogs
            .OrderByDescending(b => b.IsActive)
            .ThenByDescending(b => b.Hostname.Length)
            .ThenBy(b => b.Hostname.Length > 0 && b.IsAnyTextBeforeHostnameAccepted)
            .ThenByDescending(b => RelativeRoot(b).Length)
            .ThenBy(b => b.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Blog a cui è destinata la richiesta. L'id esplicito (intestazione "x-blog-instance") ha la precedenza;
    /// poi si confrontano nome host e percorso. Se nulla corrisponde si usa il blog principale.
    /// </summary>
    public static Blog? Resolve(IReadOnlyList<Blog> blogs, string host, string path, Guid? explicitBlogId = null)
    {
        if (blogs.Count <= 1)
        {
            return blogs.FirstOrDefault();
        }

        if (explicitBlogId is { } id && blogs.FirstOrDefault(b => b.Id == id && b.IsActive) is { } explicitBlog)
        {
            return explicitBlog;
        }

        // Il confronto avviene con lo slash finale: "/blog1" deve corrispondere alla radice "/blog1/".
        var normalizedPath = string.IsNullOrEmpty(path) ? "/" : path.EndsWith('/') ? path : path + "/";
        foreach (var blog in SortForMatching(blogs))
        {
            if (!blog.IsActive)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(blog.Hostname))
            {
                var hostMatches = blog.IsAnyTextBeforeHostnameAccepted
                    ? host.EndsWith(blog.Hostname, StringComparison.OrdinalIgnoreCase)
                    : host.Equals(blog.Hostname, StringComparison.OrdinalIgnoreCase);
                if (!hostMatches)
                {
                    continue;
                }
            }

            if (normalizedPath.StartsWith(RelativeRoot(blog), StringComparison.OrdinalIgnoreCase))
            {
                return blog;
            }
        }

        return blogs.FirstOrDefault(b => b.IsPrimary);
    }
}

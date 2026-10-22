using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using BlogEngine.Core.Files;
using BlogEngine.Core.Services;
using BlogEngine.Models;
using BlogEngine.Services;

namespace BlogEngine.Web.Protocols;

/// <summary>Esito di un'importazione BlogML.</summary>
public sealed record BlogMLImportResult(int Posts, int Categories, int Comments, int DownloadedFiles);

/// <summary>
/// Esportazione e importazione nel formato BlogML 2.0 (porting di BlogMLExportHandler e BlogReader):
/// autori, categorie, post con tag, commenti e trackback. Così il blog si può spostare da o verso
/// altre piattaforme che usano lo stesso formato.
/// </summary>
public sealed partial class BlogMLService(
    IPostService posts,
    ICategoryService categories,
    IUserService users,
    ISettingsService settings,
    ILinkGenerator links,
    IBlogContext context,
    FileStore files,
    IRemotePages remote,
    IBlogClock clock,
    ILogger<BlogMLService> logger)
{
    public static readonly XNamespace Ns = "http://www.blogml.com/2006/09/BlogML";

    public async Task<string> ExportAsync(CancellationToken cancellationToken = default)
    {
        var blogSettings = await settings.GetAsync(cancellationToken: cancellationToken);
        var root = links.ToAbsolute(links.BlogRoot).ToString();
        var allCategories = await categories.GetCategoriesAsync(cancellationToken);
        var allPosts = (await posts.GetPostsAsync(cancellationToken)).Where(p => !p.IsDeleted).OrderBy(p => p.DateCreated).ToList();

        var postElements = new List<XElement>();
        foreach (var post in allPosts)
        {
            postElements.Add(new XElement(Ns + "post",
                new XAttribute("id", post.Id),
                Dates(post.DateCreated, post.DateModified),
                new XAttribute("approved", post.IsPublished ? "true" : "false"),
                new XAttribute("post-url", await links.PostAsync(post, cancellationToken)),
                new XAttribute("type", "normal"),
                new XAttribute("hasexcerpt", string.IsNullOrEmpty(post.Description) ? "false" : "true"),
                new XAttribute("views", "0"),
                Text("title", post.Title),
                Text("content", post.Content),
                Text("post-name", post.Slug),
                string.IsNullOrEmpty(post.Description) ? null : Text("excerpt", post.Description),
                new XElement(Ns + "authors", new XElement(Ns + "author", new XAttribute("ref", post.Author))),
                new XElement(Ns + "categories", post.Categories.Select(c => new XElement(Ns + "category", new XAttribute("ref", c)))),
                new XElement(Ns + "tags", post.Tags.Select(t => new XElement(Ns + "tag", new XAttribute("ref", t)))),
                new XElement(Ns + "comments", post.AllComments.Where(c => !c.IsDeleted && !c.IsPingbackOrTrackback).Select(c => new XElement(Ns + "comment",
                    new XAttribute("id", c.Id),
                    new XAttribute("parentid", c.ParentId),
                    Dates(c.DateCreated, c.DateCreated),
                    new XAttribute("approved", c.IsApproved ? "true" : "false"),
                    new XAttribute("user-name", c.Author),
                    new XAttribute("user-email", c.Email),
                    new XAttribute("user-ip", c.IP),
                    new XAttribute("user-url", c.Website?.ToString() ?? string.Empty),
                    Text("title", c.Teaser),
                    Text("content", c.Content)))),
                new XElement(Ns + "trackbacks", post.AllComments.Where(c => !c.IsDeleted && c.IsPingbackOrTrackback).Select(c => new XElement(Ns + "trackback",
                    new XAttribute("id", c.Id),
                    Dates(c.DateCreated, c.DateCreated),
                    new XAttribute("approved", c.IsApproved ? "true" : "false"),
                    new XAttribute("url", c.Website?.ToString() ?? string.Empty),
                    Text("title", c.Author))))));
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(Ns + "blog",
            new XAttribute("root-url", root),
            new XAttribute("date-created", Date(clock.UtcNow)),
            Text("title", blogSettings.Name),
            Text("sub-title", blogSettings.Description),
            new XElement(Ns + "authors", (await users.GetUsersAsync(cancellationToken)).Select(u => new XElement(Ns + "author",
                new XAttribute("id", u.UserName),
                Dates(clock.UtcNow, clock.UtcNow),
                new XAttribute("approved", "true"),
                new XAttribute("email", u.Email),
                Text("title", u.UserName)))),
            new XElement(Ns + "extended-properties",
                new XElement(Ns + "property", new XAttribute("name", "CommentModeration"), new XAttribute("value", blogSettings.EnableCommentsModeration ? "Authenticated" : "Anonymous")),
                new XElement(Ns + "property", new XAttribute("name", "SendTrackback"), new XAttribute("value", blogSettings.EnableTrackBackSend ? "Yes" : "No"))),
            new XElement(Ns + "categories", allCategories.Select(c => new XElement(Ns + "category",
                new XAttribute("id", c.Id),
                Dates(c.DateCreated, c.DateModified),
                new XAttribute("approved", "true"),
                new XAttribute("parentref", c.Parent?.ToString() ?? "0"),
                string.IsNullOrEmpty(c.Description) ? null : new XAttribute("description", c.Description),
                Text("title", c.Title)))),
            new XElement(Ns + "posts", postElements)));

        return document.Declaration + Environment.NewLine + document.ToString();
    }

    public async Task<BlogMLImportResult> ImportAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException("errorInvalidBlogML", ex);
        }

        var blog = document.Root;
        if (blog is null || blog.Name.LocalName != "blog")
        {
            throw new InvalidOperationException("errorInvalidBlogML");
        }

        var ns = blog.Name.Namespace;
        var blogSettings = await settings.GetAsync(cancellationToken: cancellationToken);

        // Categorie: quelle con lo stesso titolo vengono riutilizzate, le altre create (poi i genitori).
        var categoryMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var parents = new List<(Guid Id, string ParentRef)>();
        var createdCategories = 0;
        foreach (var element in blog.Element(ns + "categories")?.Elements(ns + "category") ?? [])
        {
            var title = Value(element, ns, "title");
            if (title.Length == 0)
            {
                continue;
            }

            var existing = (await categories.GetCategoriesAsync(cancellationToken)).FirstOrDefault(c => string.Equals(c.Title, title, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new Category { Title = title, Description = (string?)element.Attribute("description") ?? string.Empty };
                await categories.SaveAsync(existing, cancellationToken);
                createdCategories++;
                parents.Add((existing.Id, (string?)element.Attribute("parentref") ?? string.Empty));
            }

            categoryMap[(string?)element.Attribute("id") ?? title] = existing.Id;
        }

        foreach (var (id, parentRef) in parents)
        {
            if (categoryMap.TryGetValue(parentRef, out var parentId) && parentId != id && await categories.GetByIdAsync(id, cancellationToken) is { } category)
            {
                var copy = Api.EntityCopies.Copy(category);
                copy.Parent = parentId;
                await categories.SaveAsync(copy, cancellationToken);
            }
        }

        var importedPosts = 0;
        var importedComments = 0;
        var downloads = 0;
        foreach (var element in blog.Element(ns + "posts")?.Elements(ns + "post") ?? [])
        {
            var title = Value(element, ns, "title");
            if (title.Length == 0)
            {
                continue;
            }

            var content = Value(element, ns, "content");
            if (blogSettings.AllowServerToDownloadRemoteFiles)
            {
                (content, var count) = await DownloadImagesAsync(content, blogSettings.RemoteMaxFileSize, blogSettings.RemoteFileDownloadTimeout, cancellationToken);
                downloads += count;
            }

            var post = new Post
            {
                Title = title,
                Content = content,
                Description = Value(element, ns, "excerpt"),
                Slug = Value(element, ns, "post-name"),
                Author = (string?)element.Element(ns + "authors")?.Element(ns + "author")?.Attribute("ref") ?? context.UserName ?? string.Empty,
                IsPublished = !string.Equals((string?)element.Attribute("approved"), "false", StringComparison.OrdinalIgnoreCase),
                DateCreated = ParseDate((string?)element.Attribute("date-created")) ?? clock.UtcNow,
                Categories = (element.Element(ns + "categories")?.Elements(ns + "category") ?? [])
                    .Select(c => (string?)c.Attribute("ref"))
                    .Where(r => r is not null && categoryMap.ContainsKey(r))
                    .Select(r => categoryMap[r!])
                    .Distinct()
                    .ToList(),
                Tags = (element.Element(ns + "tags")?.Elements(ns + "tag") ?? []).Select(t => (string?)t.Attribute("ref") ?? t.Value).Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()!,
            };

            if (string.IsNullOrWhiteSpace(post.Description))
            {
                post.Description = Api.AdminMapper.DescriptionOrExcerpt(null, post.Content);
            }

            // I commenti arrivano con il loro stato, senza passare dalla moderazione dei nuovi commenti.
            var idMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var comment in element.Element(ns + "comments")?.Elements(ns + "comment") ?? [])
            {
                var imported = new Comment
                {
                    Id = Guid.NewGuid(),
                    Author = (string?)comment.Attribute("user-name") ?? "Anonymous",
                    Email = (string?)comment.Attribute("user-email") ?? string.Empty,
                    IP = (string?)comment.Attribute("user-ip") ?? string.Empty,
                    Website = Uri.TryCreate((string?)comment.Attribute("user-url"), UriKind.Absolute, out var site) ? site : null,
                    Content = BlogEngine.Text.HtmlText.ToPlainText(Value(comment, ns, "content")),
                    DateCreated = ParseDate((string?)comment.Attribute("date-created")) ?? post.DateCreated,
                    IsApproved = !string.Equals((string?)comment.Attribute("approved"), "false", StringComparison.OrdinalIgnoreCase),
                    ModeratedBy = "BlogML",
                    PostId = post.Id,
                    BlogId = context.Blog.Id,
                };
                idMap[(string?)comment.Attribute("id") ?? imported.Id.ToString()] = imported.Id;
                if ((string?)comment.Attribute("parentid") is { } parent && idMap.TryGetValue(parent, out var parentId))
                {
                    imported.ParentId = parentId;
                }

                post.AllComments.Add(imported);
                importedComments++;
            }

            foreach (var trackback in element.Element(ns + "trackbacks")?.Elements(ns + "trackback") ?? [])
            {
                post.AllComments.Add(new Comment
                {
                    Author = Value(trackback, ns, "title"),
                    Email = Comment.TrackbackEmail,
                    Website = Uri.TryCreate((string?)trackback.Attribute("url"), UriKind.Absolute, out var url) ? url : null,
                    Content = Value(trackback, ns, "title"),
                    DateCreated = ParseDate((string?)trackback.Attribute("date-created")) ?? post.DateCreated,
                    IsApproved = !string.Equals((string?)trackback.Attribute("approved"), "false", StringComparison.OrdinalIgnoreCase),
                    ModeratedBy = "BlogML",
                    PostId = post.Id,
                    BlogId = context.Blog.Id,
                });
            }

            await posts.SaveAsync(post, cancellationToken);
            importedPosts++;
        }

        logger.LogInformation("BlogML importato: {Posts} post, {Categories} categorie, {Comments} commenti, {Files} file scaricati.", importedPosts, createdCategories, importedComments, downloads);
        return new BlogMLImportResult(importedPosts, createdCategories, importedComments, downloads);
    }

    /// <summary>Copia sul sito le immagini dei post che si trovano su altri siti (impostazione "scarica file remoti").</summary>
    private async Task<(string Content, int Count)> DownloadImagesAsync(string content, int maxBytes, int timeoutMs, CancellationToken cancellationToken)
    {
        var count = 0;
        var host = links.ToAbsolute("/").Host;
        var now = await clock.ToBlogTimeAsync(clock.UtcNow);
        foreach (var url in ImageRegex().Matches(content).Select(m => m.Groups["url"].Value).Distinct().ToList())
        {
            if (!Uri.TryCreate(System.Net.WebUtility.HtmlDecode(url), UriKind.Absolute, out var uri) || string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var bytes = await remote.DownloadAsync(uri, Math.Max(maxBytes, 1), TimeSpan.FromMilliseconds(Math.Clamp(timeoutMs, 1000, 120000)), cancellationToken);
            if (bytes is null)
            {
                continue;
            }

            var name = FileStore.SafeFileName(Path.GetFileName(uri.AbsolutePath));
            if (string.IsNullOrEmpty(Path.GetExtension(name)))
            {
                name += ".jpg";
            }

            using var stream = new MemoryStream(bytes);
            var saved = await files.SaveAsync(context.Blog.Id, now.ToString("/yyyy/MM", CultureInfo.InvariantCulture), name, stream, cancellationToken);
            content = content.Replace(url, $"{links.BlogRoot}image.axd?picture={Uri.EscapeDataString(saved)}", StringComparison.Ordinal);
            count++;
        }

        return (content, count);
    }

    private static XElement Text(string name, string? value) =>
        new(Ns + name, new XAttribute("type", "text"), new XCData(value ?? string.Empty));

    private static XAttribute[] Dates(DateTime created, DateTime modified) =>
        [new XAttribute("date-created", Date(created)), new XAttribute("date-modified", Date(modified))];

    private static string Date(DateTime value) => value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var date)
            ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
            : null;

    private static string Value(XElement element, XNamespace ns, string name) => element.Element(ns + name)?.Value.Trim() ?? string.Empty;

    [GeneratedRegex("<img[^>]+src=[\"'](?<url>https?://[^\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex ImageRegex();
}

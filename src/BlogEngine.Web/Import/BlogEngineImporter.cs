using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using BlogEngine.Core.Caching;
using BlogEngine.Core.Data;
using BlogEngine.Core.Files;
using BlogEngine.Models;
using BlogEngine.Text;

namespace BlogEngine.Web.Import;

/// <summary>Esito di un'importazione dal BlogML di BlogEngine.NET.</summary>
public sealed record BlogEngineImportResult(
    int Posts,
    int Pages,
    int Categories,
    int Comments,
    int DownloadedFiles,
    int ExistingFiles,
    IReadOnlyList<string> FailedFiles);

/// <summary>
/// Importa un blog BlogEngine.NET (WebForms) dal file BlogML esportato dalla sua amministrazione
/// (Impostazioni → Avanzate → Esporta). Rispetto all'importazione BlogML generica:
/// <list type="bullet">
/// <item>gli elementi <c>type="article"</c> sono le pagine statiche e diventano pagine;</item>
/// <item>lo slug viene da <c>post-url</c> (BlogEngine mette il titolo in <c>post-name</c>), così i vecchi indirizzi restano validi;</item>
/// <item>post, pagine, categorie e commenti tengono il loro id (funzionano anche i vecchi link <c>post.aspx?id=</c>);</item>
/// <item>immagini e allegati (<c>image.axd</c>, <c>file.axd</c>) vengono scaricati dal vecchio sito e salvati nello stesso
/// percorso, quindi i loro indirizzi non cambiano;</item>
/// <item>le date del BlogML sono nell'ora locale del vecchio server e vengono convertite in UTC.</item>
/// </list>
/// I contenuti vengono scritti direttamente nei dati del blog, senza eventi di pubblicazione (niente ping né newsletter).
/// L'importazione si può ripetere: i contenuti con lo stesso id vengono aggiornati, i file già scaricati non si riscaricano,
/// i commenti arrivati dopo nel nuovo blog e la posizione delle pagine (genitore, menu) restano com'erano.
/// </summary>
public sealed partial class BlogEngineImporter(
    EntityRepository<Post> posts,
    EntityRepository<Page> pages,
    EntityRepository<Category> categories,
    IUserStore users,
    FileStore files,
    IHttpClientFactory httpClients,
    ILogger<BlogEngineImporter> logger)
{
    /// <summary>Cartella in cui finiscono i file del vecchio sito che non passavano da image.axd/file.axd (per esempio le faccine dell'editor).</summary>
    public const string LegacyFolder = "/vecchio-sito";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg",
    };

    private static readonly HashSet<string> AssetExtensions = new(ImageExtensions.Concat(
        [".mp3", ".wav", ".wma", ".mp4", ".wmv", ".avi", ".pdf", ".zip", ".rar", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt"]),
        StringComparer.OrdinalIgnoreCase);

    /// <param name="blogId">Blog di destinazione.</param>
    /// <param name="blogmlPath">File BlogML esportato da BlogEngine.NET.</param>
    /// <param name="oldSite">Indirizzo del vecchio sito (per esempio https://www.fiorentina.info), da cui scaricare i file; null per non scaricare nulla.</param>
    /// <param name="timeZone">Fuso orario del vecchio server, in cui sono scritte le date del BlogML.</param>
    /// <param name="downloadFrom">
    /// Indirizzo da cui scaricare i file, se diverso da <paramref name="oldSite"/>: per esempio l'indirizzo provvisorio del vecchio
    /// sito quando il dominio punta già al nuovo.
    /// </param>
    public async Task<BlogEngineImportResult> ImportAsync(Guid blogId, string blogmlPath, Uri? oldSite, TimeZoneInfo timeZone, Uri? downloadFrom = null, CancellationToken cancellationToken = default)
    {
        var blog = Load(blogmlPath);
        var ns = blog.Name.Namespace;
        var hosts = SiteHosts(oldSite);
        downloadFrom ??= oldSite;

        // ------------------------------------------------ categorie (riutilizzate per titolo, altrimenti create con il loro id)
        var categoryMap = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var existingCategories = (await categories.GetAllAsync(blogId, cancellationToken)).ToList();
        var newCategories = new List<(Category Category, string ParentRef)>();
        foreach (var element in blog.Elements(ns + "categories").Elements(ns + "category"))
        {
            var title = Value(element, ns, "title");
            if (title.Length == 0)
            {
                continue;
            }

            var id = Guid.TryParse((string?)element.Attribute("id"), out var parsed) ? parsed : Guid.NewGuid();
            var category = existingCategories.FirstOrDefault(c => c.Id == id)
                ?? existingCategories.FirstOrDefault(c => string.Equals(c.Title, title, StringComparison.OrdinalIgnoreCase));
            if (category is null)
            {
                category = new Category
                {
                    Id = id,
                    BlogId = blogId,
                    Title = title,
                    Description = (string?)element.Attribute("description") ?? string.Empty,
                    DateCreated = DateTime.UtcNow,
                    DateModified = DateTime.UtcNow,
                };
                existingCategories.Add(category);
                newCategories.Add((category, (string?)element.Attribute("parentref") ?? string.Empty));
            }

            categoryMap[(string?)element.Attribute("id") ?? title] = category.Id;
        }

        foreach (var (category, parentRef) in newCategories)
        {
            if (categoryMap.TryGetValue(parentRef, out var parentId) && parentId != category.Id)
            {
                category.Parent = parentId;
            }

            await categories.SaveAsync(category, cancellationToken);
        }

        // ------------------------------------------------ elenco dei contenuti, con id e slug definitivi
        var items = blog.Elements(ns + "posts").Elements(ns + "post")
            .Select(e => Item.From(e, ns, timeZone))
            .Where(i => i.Title.Length > 0)
            .ToList();

        var existingPosts = await posts.GetAllAsync(blogId, cancellationToken);
        var existingPages = await pages.GetAllAsync(blogId, cancellationToken);
        AssignSlugs(items.Where(i => !i.IsPage), existingPosts.Select(p => (p.Id, p.Slug)));
        AssignSlugs(items.Where(i => i.IsPage), existingPages.Select(p => (p.Id, p.Slug)));

        var parents = InferPageParents(items.Where(i => i.IsPage).ToList());
        var userNames = (await users.GetAllAsync(blogId, cancellationToken)).Select(u => u.UserName).ToList();

        // ------------------------------------------------ testi: indirizzi dei file e link interni
        var downloads = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            item.Content = ConvertContent(item.Content, downloadFrom, hosts, downloads);
        }

        // ------------------------------------------------ scrittura
        int importedPosts = 0, importedPages = 0, importedComments = 0;
        foreach (var item in items)
        {
            if (item.IsPage)
            {
                var stored = await pages.FindAsync(blogId, item.Id, cancellationToken);
                var page = stored ?? new Page { Id = item.Id, BlogId = blogId };
                page.Title = item.Title;
                page.Slug = item.Slug;
                page.Content = item.Content;
                page.Description = Api.AdminMapper.DescriptionOrExcerpt(item.Excerpt, item.Content);
                page.IsPublished = item.IsPublished;
                page.DateCreated = item.DateCreated;
                page.DateModified = item.DateModified;
                if (stored is null)
                {
                    // Posizione nel menu solo alla prima importazione: dopo si decide dall'amministrazione.
                    page.Parent = parents.TryGetValue(item.Id, out var parent) ? parent : Guid.Empty;
                    page.ShowInList = item.IsPublished;
                    page.ShowInMenu = false;
                }

                await pages.SaveAsync(page, cancellationToken);
                importedPages++;
                continue;
            }

            var post = await posts.FindAsync(blogId, item.Id, cancellationToken) ?? new Post { Id = item.Id, BlogId = blogId };
            post.Title = item.Title;
            post.Slug = item.Slug;
            post.Author = userNames.FirstOrDefault(u => string.Equals(u, item.Author, StringComparison.OrdinalIgnoreCase)) ?? item.Author;
            post.Content = item.Content;
            post.Description = Api.AdminMapper.DescriptionOrExcerpt(item.Excerpt, item.Content);
            post.IsPublished = item.IsPublished;
            post.DateCreated = item.DateCreated;
            post.DateModified = item.DateModified;
            post.Categories = item.CategoryRefs.Where(categoryMap.ContainsKey).Select(r => categoryMap[r]).Distinct().ToList();
            post.Tags = item.Tags;

            var comments = ReadComments(item.Element, ns, post, timeZone);
            var importedIds = comments.Select(c => c.Id).ToHashSet();
            post.AllComments = comments.Concat(post.AllComments.Where(c => !importedIds.Contains(c.Id))).OrderBy(c => c.DateCreated).ToList();
            importedComments += comments.Count(c => !c.IsPingbackOrTrackback);

            await posts.SaveAsync(post, cancellationToken);
            importedPosts++;
        }

        // ------------------------------------------------ file dal vecchio sito
        var (downloaded, existing, failed) = await DownloadAsync(blogId, downloads, cancellationToken);

        logger.LogInformation(
            "BlogEngine importato: {Posts} post, {Pages} pagine, {Categories} categorie nuove, {Comments} commenti, {Downloaded} file scaricati ({Existing} già presenti, {Failed} non riusciti).",
            importedPosts, importedPages, newCategories.Count, importedComments, downloaded, existing, failed.Count);
        return new BlogEngineImportResult(importedPosts, importedPages, newCategories.Count, importedComments, downloaded, existing, failed);
    }

    // ---------------------------------------------------------------- lettura

    private static XElement Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Il file {path} non esiste.");
        }

        XDocument document;
        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document = XDocument.Load(reader);
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"{path} non è un file BlogML valido: {ex.Message}", ex);
        }

        if (document.Root is not { Name.LocalName: "blog" } blog)
        {
            throw new InvalidOperationException($"{path} non è un file BlogML: manca l'elemento <blog>.");
        }

        return blog;
    }

    private sealed class Item
    {
        public required XElement Element { get; init; }
        public required Guid Id { get; init; }
        public required bool IsPage { get; init; }
        public required string Title { get; init; }
        public required string UrlSlug { get; init; }
        public string Slug { get; set; } = string.Empty;
        public required string Content { get; set; }
        public required string Excerpt { get; init; }
        public required string Author { get; init; }
        public required bool IsPublished { get; init; }
        public required DateTime DateCreated { get; init; }
        public required DateTime DateModified { get; init; }
        public required List<string> CategoryRefs { get; init; }
        public required List<string> Tags { get; init; }

        public static Item From(XElement element, XNamespace ns, TimeZoneInfo zone)
        {
            var created = ToUtc((string?)element.Attribute("date-created"), zone) ?? DateTime.UtcNow;
            return new Item
            {
                Element = element,
                Id = Guid.TryParse((string?)element.Attribute("id"), out var id) ? id : Guid.NewGuid(),
                IsPage = string.Equals((string?)element.Attribute("type"), "article", StringComparison.OrdinalIgnoreCase),
                Title = Value(element, ns, "title"),
                UrlSlug = SlugFromUrl((string?)element.Attribute("post-url")),
                Content = Value(element, ns, "content"),
                Excerpt = Value(element, ns, "excerpt"),
                Author = (string?)element.Element(ns + "authors")?.Element(ns + "author")?.Attribute("ref") ?? string.Empty,
                IsPublished = !IsFalse(element, "approved") && !IsFalse(element, "is-published"),
                DateCreated = created,
                DateModified = ToUtc((string?)element.Attribute("date-modified"), zone) ?? created,
                CategoryRefs = (element.Element(ns + "categories")?.Elements(ns + "category") ?? [])
                    .Select(c => (string?)c.Attribute("ref")).OfType<string>().ToList(),
                Tags = (element.Element(ns + "tags")?.Elements(ns + "tag") ?? [])
                    .Select(t => (string?)t.Attribute("ref") ?? t.Value)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Select(t => t.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList(),
            };
        }
    }

    /// <summary>"/post/Titolo-del-post.aspx" o "/page/Chi-Sono.aspx" → "Titolo-del-post", "Chi-Sono".</summary>
    public static string SlugFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var path = url;
        if (Uri.TryCreate(url, UriKind.Absolute, out var absolute))
        {
            path = absolute.AbsolutePath;
        }

        var last = path.Split('?', '#')[0].TrimEnd('/').Split('/').Last();
        if (last.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
        {
            last = last[..^".aspx".Length];
        }

        return Slug.Create(WebUtility.UrlDecode(last));
    }

    /// <summary>
    /// Slug univoci: lo slug del vecchio indirizzo, o quello del titolo se mancava. Fra titoli uguali il vecchio indirizzo
    /// apriva il post più recente: quello tiene lo slug, i più vecchi ricevono un numero (come fa il blog per i nuovi post).
    /// </summary>
    private static void AssignSlugs(IEnumerable<Item> items, IEnumerable<(Guid Id, string Slug)> existing)
    {
        var list = items.OrderByDescending(i => i.DateCreated).ToList();
        var importedIds = list.Select(i => i.Id).ToHashSet();
        var used = existing.Where(e => !importedIds.Contains(e.Id)).Select(e => e.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in list)
        {
            var baseSlug = item.UrlSlug.Length > 0 ? item.UrlSlug : Slug.Create(item.Title);
            if (baseSlug.Length == 0)
            {
                baseSlug = item.Id.ToString("N");
            }

            var candidate = baseSlug;
            for (var i = 1; used.Contains(candidate); i++)
            {
                candidate = $"{baseSlug}{i}";
            }

            used.Add(candidate);
            item.Slug = candidate;
        }
    }

    /// <summary>
    /// Il BlogML non dice quale pagina è sotto quale. Una pagina linkata dal testo di un'altra pagina viene messa sotto di essa
    /// (il primo collegamento vince), evitando i cicli. Il risultato si può correggere dall'amministrazione.
    /// </summary>
    private static Dictionary<Guid, Guid> InferPageParents(List<Item> pageItems)
    {
        var bySlug = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in pageItems)
        {
            bySlug.TryAdd(item.UrlSlug.Length > 0 ? item.UrlSlug : item.Slug, item);
        }

        var parents = new Dictionary<Guid, Guid>();
        foreach (var page in pageItems.OrderBy(p => p.DateCreated))
        {
            foreach (Match match in PageLink().Matches(WebUtility.HtmlDecode(page.Content)))
            {
                if (!bySlug.TryGetValue(Slug.Create(WebUtility.UrlDecode(match.Groups["slug"].Value)), out var child)
                    || child.Id == page.Id
                    || parents.ContainsKey(child.Id)
                    || IsAncestor(child.Id, page.Id, parents))
                {
                    continue;
                }

                parents[child.Id] = page.Id;
            }
        }

        return parents;
    }

    private static bool IsAncestor(Guid candidate, Guid of, Dictionary<Guid, Guid> parents)
    {
        for (var current = of; parents.TryGetValue(current, out var parent); current = parent)
        {
            if (parent == candidate)
            {
                return true;
            }
        }

        return false;
    }

    private static List<Comment> ReadComments(XElement element, XNamespace ns, Post post, TimeZoneInfo zone)
    {
        var result = new List<Comment>();
        foreach (var comment in element.Element(ns + "comments")?.Elements(ns + "comment") ?? [])
        {
            result.Add(new Comment
            {
                Id = Guid.TryParse((string?)comment.Attribute("id"), out var id) ? id : Guid.NewGuid(),
                ParentId = Guid.TryParse((string?)comment.Attribute("parentid"), out var parent) ? parent : Guid.Empty,
                Author = (string?)comment.Attribute("user-name") is { Length: > 0 } name ? name : "Anonimo",
                Email = (string?)comment.Attribute("user-email") ?? string.Empty,
                IP = (string?)comment.Attribute("user-ip") ?? string.Empty,
                Website = Uri.TryCreate((string?)comment.Attribute("user-url"), UriKind.Absolute, out var site) ? site : null,
                Content = HtmlText.ToPlainText(Value(comment, ns, "content")),
                DateCreated = ToUtc((string?)comment.Attribute("date-created"), zone) ?? post.DateCreated,
                IsApproved = !IsFalse(comment, "approved"),
                ModeratedBy = "BlogEngine.NET",
                PostId = post.Id,
                BlogId = post.BlogId,
            });
        }

        foreach (var trackback in element.Element(ns + "trackbacks")?.Elements(ns + "trackback") ?? [])
        {
            result.Add(new Comment
            {
                Id = Guid.TryParse((string?)trackback.Attribute("id"), out var id) ? id : Guid.NewGuid(),
                Author = Value(trackback, ns, "title"),
                Email = Comment.TrackbackEmail,
                Website = Uri.TryCreate((string?)trackback.Attribute("url"), UriKind.Absolute, out var url) ? url : null,
                Content = Value(trackback, ns, "title"),
                DateCreated = ToUtc((string?)trackback.Attribute("date-created"), zone) ?? post.DateCreated,
                IsApproved = !IsFalse(trackback, "approved"),
                ModeratedBy = "BlogEngine.NET",
                PostId = post.Id,
                BlogId = post.BlogId,
            });
        }

        return result;
    }

    // ---------------------------------------------------------------- testi

    /// <summary>
    /// Riscrive gli indirizzi del testo e annota in <paramref name="downloads"/> i file da prendere dal vecchio sito
    /// (percorso nel nuovo blog → indirizzo da cui scaricarlo).
    /// </summary>
    public static string ConvertContent(string html, Uri? oldSite, Regex hosts, IDictionary<string, Uri> downloads) =>
        UrlAttribute().Replace(html, m =>
        {
            var raw = WebUtility.HtmlDecode(m.Groups["url"].Value);
            var replaced = ConvertUrl(raw, m.Groups["attr"].Value, oldSite, hosts, downloads);
            return replaced is null ? m.Value : $"{m.Groups["attr"].Value}={m.Groups["q"].Value}{WebUtility.HtmlEncode(replaced)}{m.Groups["q"].Value}";
        });

    private static string? ConvertUrl(string url, string attribute, Uri? oldSite, Regex hosts, IDictionary<string, Uri> downloads)
    {
        string pathAndQuery;
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var absolute) || !hosts.IsMatch(absolute.Host))
            {
                return null;
            }

            pathAndQuery = absolute.PathAndQuery + absolute.Fragment;
        }
        else if (url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal))
        {
            pathAndQuery = url;
        }
        else
        {
            return null;
        }

        // image.axd?picture=... e file.axd?file=..., anche sotto una vecchia sottocartella ("/BlogEngine.NET/image.axd").
        if (AxdReference().Match(pathAndQuery) is { Success: true } axd)
        {
            var isImage = axd.Groups["handler"].Value.Equals("image", StringComparison.OrdinalIgnoreCase);
            var path = NormalizeFilePath(WebUtility.UrlDecode(axd.Groups["value"].Value));
            if (path is null)
            {
                return null;
            }

            if (oldSite is not null)
            {
                downloads.TryAdd(path, new Uri(oldSite, $"{(isImage ? "image.axd?picture=" : "file.axd?file=")}{Uri.EscapeDataString(path)}"));
            }

            return FileUrl(path, isImage);
        }

        var pathOnly = pathAndQuery.Split('?', '#')[0];
        var extension = Path.GetExtension(pathOnly);

        // Altri file del vecchio sito (faccine dell'editor, immagini caricate a mano...): solo se il link era assoluto,
        // perché quelli relativi potrebbero esistere anche nel nuovo blog.
        if (pathAndQuery != url && AssetExtensions.Contains(extension) && !pathOnly.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
        {
            var path = NormalizeFilePath(LegacyFolder + Uri.UnescapeDataString(pathOnly));
            if (path is null)
            {
                return null;
            }

            if (oldSite is not null)
            {
                downloads.TryAdd(path, new Uri(oldSite, pathOnly));
            }

            return FileUrl(path, ImageExtensions.Contains(extension));
        }

        // Link interni: relativi e senza ".aspx" per post e pagine (gli altri .aspx li sistema LegacyUrlMiddleware).
        if (!string.Equals(attribute, "href", StringComparison.OrdinalIgnoreCase))
        {
            return pathAndQuery == url ? null : pathAndQuery;
        }

        if (OldContentLink().Match(pathAndQuery) is { Success: true } content)
        {
            return $"/{content.Groups["kind"].Value.ToLowerInvariant()}/{Slug.Create(WebUtility.UrlDecode(content.Groups["slug"].Value))}{content.Groups["rest"].Value}";
        }

        return pathAndQuery == url ? null : pathAndQuery;
    }

    /// <summary>"2011\6\foto.jpg" → "/2011/6/foto.jpg"; null se il percorso non è accettabile.</summary>
    private static string? NormalizeFilePath(string value)
    {
        var path = "/" + value.Replace('\\', '/').Trim().TrimStart('/');
        if (path.Length < 2 || path.Contains("..", StringComparison.Ordinal) || path.Contains(':') || path.Any(char.IsControl))
        {
            return null;
        }

        while (path.Contains("//", StringComparison.Ordinal))
        {
            path = path.Replace("//", "/", StringComparison.Ordinal);
        }

        return path;
    }

    /// <summary>Indirizzo di un file del blog: "/image.axd?picture=/2011/6/foto.jpg".</summary>
    public static string FileUrl(string path, bool isImage)
    {
        var escaped = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
        return isImage ? $"/image.axd?picture={escaped}" : $"/file.axd?file={escaped}";
    }

    // ---------------------------------------------------------------- file

    private async Task<(int Downloaded, int Existing, List<string> Failed)> DownloadAsync(Guid blogId, IDictionary<string, Uri> downloads, CancellationToken cancellationToken)
    {
        var downloaded = 0;
        var existing = 0;
        var failed = new List<string>();
        var done = 0;
        var client = httpClients.CreateClient(nameof(BlogEngineImporter));
        client.Timeout = TimeSpan.FromMinutes(2);

        await Parallel.ForEachAsync(downloads, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken }, async (entry, token) =>
        {
            var (path, source) = (entry.Key, entry.Value);
            string target;
            try
            {
                target = files.Resolve(blogId, path);
            }
            catch (ArgumentException)
            {
                lock (failed) { failed.Add($"{path} (percorso non valido)"); }
                return;
            }

            if (File.Exists(target))
            {
                Interlocked.Increment(ref existing);
            }
            else
            {
                try
                {
                    using var response = await client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, token);
                    var type = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                    if (!response.IsSuccessStatusCode || type.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                    {
                        lock (failed) { failed.Add($"{path} ({(int)response.StatusCode} {type})"); }
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        var temporary = target + ".download";
                        await using (var file = File.Create(temporary))
                        {
                            await response.Content.CopyToAsync(file, token);
                        }

                        File.Move(temporary, target, overwrite: true);
                        Interlocked.Increment(ref downloaded);
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException && !token.IsCancellationRequested)
                {
                    lock (failed) { failed.Add($"{path} ({ex.Message})"); }
                }
            }

            if (Interlocked.Increment(ref done) % 50 == 0)
            {
                logger.LogInformation("File: {Done} di {Total}.", done, downloads.Count);
            }
        });

        failed.Sort(StringComparer.OrdinalIgnoreCase);
        return (downloaded, existing, failed);
    }

    // ---------------------------------------------------------------- utilità

    private static string Value(XElement element, XNamespace ns, string name) => element.Element(ns + name)?.Value.Trim() ?? string.Empty;

    private static bool IsFalse(XElement element, string attribute) =>
        string.Equals((string?)element.Attribute(attribute), "false", StringComparison.OrdinalIgnoreCase);

    private static DateTime? ToUtc(string? value, TimeZoneInfo zone)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local) || local.Year < 1900)
        {
            return null;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return zone.IsInvalidTime(local) ? TimeZoneInfo.ConvertTimeToUtc(local.AddHours(1), zone) : TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>Il dominio del vecchio sito, con e senza "www".</summary>
    public static Regex SiteHosts(Uri? site)
    {
        if (site is null)
        {
            return new Regex("(?!)");
        }

        var host = site.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? site.Host[4..] : site.Host;
        return new Regex($@"^(www\.)?{Regex.Escape(host)}$", RegexOptions.IgnoreCase);
    }

    // Dentro "..." l'apostrofo fa parte dell'indirizzo (file come "Quando l'inno.mp3"), e viceversa.
    [GeneratedRegex(@"(?<attr>src|href)\s*=\s*(?:(?<q>"")(?<url>[^""]*)""|(?<q>')(?<url>[^']*)')", RegexOptions.IgnoreCase)]
    private static partial Regex UrlAttribute();

    [GeneratedRegex(@"^(?:/[^?#]*)?/(?<handler>image|file)\.axd\?(?:[^#]*&)?(?:picture|file)=(?<value>[^&#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AxdReference();

    [GeneratedRegex(@"^/(?<kind>post|page)/(?:\d{4}/\d{1,2}/\d{1,2}/)?(?<slug>[^/?#]+?)\.aspx(?<rest>[?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex OldContentLink();

    [GeneratedRegex(@"/page/(?<slug>[^""'?#/]+?)\.aspx", RegexOptions.IgnoreCase)]
    private static partial Regex PageLink();
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using BlogEngine.Core.Caching;
using BlogEngine.Core.Data;
using BlogEngine.Core.Files;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Settings;
using BlogEngine.Text;

namespace BlogEngine.Web.Import;

/// <summary>Esito di un'importazione da Bludit.</summary>
public sealed record BluditImportResult(int Posts, int Pages, int Categories, int Files, IReadOnlyList<string> Skipped);

/// <summary>
/// Importa un blog Bludit dalla sua cartella "bl-content": articoli, pagine statiche, categorie, tag e immagini.
/// I contenuti vengono scritti direttamente nei dati del blog, senza passare dagli eventi di pubblicazione:
/// così l'importazione non invia newsletter né ping. Può essere ripetuta: post e pagine con lo stesso
/// indirizzo vengono aggiornati invece che duplicati.
/// </summary>
public sealed partial class BluditImporter(
    EntityRepository<Post> posts,
    EntityRepository<Page> pages,
    EntityRepository<Category> categories,
    IUserStore users,
    ISettingsService settings,
    FileStore files,
    ILogger<BluditImporter> logger)
{
    /// <summary>Cartella dei file del blog in cui finiscono le immagini di Bludit.</summary>
    public const string FilesFolder = "/bludit";

    /// <summary>Stili che hanno senso anche nel nuovo tema; gli altri (font, colori, margini copiati da altri siti) vengono tolti.</summary>
    private static readonly HashSet<string> KeptStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "text-align", "float", "width", "height", "font-weight", "font-style", "text-decoration", "vertical-align",
        "border", "border-color", "border-style", "border-width", "border-collapse", "list-style-position",
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".svg",
    };

    public async Task<BluditImportResult> ImportAsync(Guid blogId, string folder, string? author = null, CancellationToken cancellationToken = default)
    {
        var root = Directory.Exists(Path.Combine(folder, "bl-content")) ? Path.Combine(folder, "bl-content") : folder;
        var databases = Path.Combine(root, "databases");
        if (!File.Exists(Path.Combine(databases, "pages.php")))
        {
            throw new InvalidOperationException($"In {root} non c'è databases/pages.php: indica la cartella bl-content di Bludit.");
        }

        var site = ReadDatabase(Path.Combine(databases, "site.php"));
        var timeZone = FindTimeZone((string?)site?["timezone"]);
        var siteHosts = SiteHosts((string?)site?["url"]);
        author ??= (await users.GetAllAsync(blogId, cancellationToken)).Select(u => u.UserName).FirstOrDefault() ?? "admin";

        var copied = CopyUploads(blogId, Path.Combine(root, "uploads"));
        var skipped = new List<string>();

        // Prima gli indirizzi di tutti i contenuti, per poter correggere i link interni dei testi.
        var entries = ReadDatabase(Path.Combine(databases, "pages.php"))?.AsObject() ?? [];
        var existingPosts = await posts.GetAllAsync(blogId, cancellationToken);
        var existingPages = await pages.GetAllAsync(blogId, cancellationToken);
        var targets = new Dictionary<string, (bool IsPage, string Slug, Guid Id)>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in entries)
        {
            var type = (string?)value?["type"];
            if (type is not ("published" or "sticky" or "scheduled" or "draft" or "static"))
            {
                skipped.Add($"{key} ({type})");
                continue;
            }

            if (!File.Exists(Path.Combine(root, "pages", key, "index.txt")))
            {
                skipped.Add($"{key} (testo mancante)");
                continue;
            }

            var isPage = type == "static";
            var slug = Slug.Create(key);
            var id = isPage
                ? existingPages.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.NewGuid()
                : existingPosts.FirstOrDefault(p => string.Equals(p.Slug, slug, StringComparison.OrdinalIgnoreCase))?.Id ?? Guid.NewGuid();
            targets[key] = (isPage, slug, id);
        }

        // Categorie: solo quelle usate, riutilizzando quelle che esistono già con lo stesso nome.
        var bluditCategories = ReadDatabase(Path.Combine(databases, "categories.php"))?.AsObject() ?? [];
        var categoryIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var createdCategories = 0;
        foreach (var key in targets.Where(t => !t.Value.IsPage).Select(t => (string?)entries[t.Key]?["category"]).Where(c => !string.IsNullOrEmpty(c)).Distinct())
        {
            var title = (string?)bluditCategories[key!]?["name"] ?? key!;
            var existing = (await categories.GetAllAsync(blogId, cancellationToken))
                .FirstOrDefault(c => string.Equals(c.Title, title, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new Category
                {
                    BlogId = blogId,
                    Title = title,
                    Description = (string?)bluditCategories[key!]?["description"] ?? string.Empty,
                    DateCreated = DateTime.UtcNow,
                    DateModified = DateTime.UtcNow,
                };
                await categories.SaveAsync(existing, cancellationToken);
                createdCategories++;
            }

            categoryIds[key!] = existing.Id;
        }

        int importedPosts = 0, importedPages = 0;
        foreach (var (key, target) in targets)
        {
            var entry = entries[key]!;
            var type = (string)entry["type"]!;
            var title = (string?)entry["title"] ?? key;
            var created = ToUtc((string?)entry["date"], timeZone) ?? DateTime.UtcNow;
            var modified = ToUtc((string?)entry["dateModified"], timeZone) ?? created;

            var content = await File.ReadAllTextAsync(Path.Combine(root, "pages", key, "index.txt"), cancellationToken);
            content = await ConvertContentAsync(blogId, content, siteHosts, targets, cancellationToken);
            if ((string?)entry["coverImage"] is { Length: > 0 } cover)
            {
                content = AddCoverImage(content, (string?)entry["uuid"], cover, title);
            }

            var description = AdminApiDescription((string?)entry["description"], content);
            if (target.IsPage)
            {
                var page = (await pages.FindAsync(blogId, target.Id, cancellationToken)) ?? new Page { Id = target.Id, BlogId = blogId };
                page.Title = title;
                page.Slug = target.Slug;
                page.Content = content;
                page.Description = description;
                page.IsPublished = true;
                page.ShowInList = true;
                page.ShowInMenu = true;
                page.SortOrder = (int?)entry["position"] ?? 0;
                page.DateCreated = created;
                page.DateModified = modified;
                await pages.SaveAsync(page, cancellationToken);
                importedPages++;
            }
            else
            {
                var post = (await posts.FindAsync(blogId, target.Id, cancellationToken)) ?? new Post { Id = target.Id, BlogId = blogId };
                post.Title = title;
                post.Slug = target.Slug;
                post.Author = author;
                post.Content = content;
                post.Description = description;
                post.IsPublished = type != "draft";
                post.HasCommentsEnabled = (bool?)entry["allowComments"] ?? true;
                post.Categories = (string?)entry["category"] is { Length: > 0 } category && categoryIds.TryGetValue(category, out var categoryId) ? [categoryId] : [];
                post.Tags = entry["tags"] is JsonObject tags ? tags.Select(t => (string?)t.Value ?? t.Key).Where(t => !string.IsNullOrWhiteSpace(t)).ToList()! : [];
                post.DateCreated = created;
                post.DateModified = modified;
                await posts.SaveAsync(post, cancellationToken);
                importedPosts++;
            }
        }

        await UpdateSettingsAsync(blogId, site, cancellationToken);
        logger.LogInformation("Bludit importato: {Posts} post, {Pages} pagine, {Categories} categorie, {Files} file.", importedPosts, importedPages, createdCategories, copied);
        return new BluditImportResult(importedPosts, importedPages, createdCategories, copied, skipped);
    }

    /// <summary>Indirizzo di un file di Bludit ("pages/uuid/foto.png") nel nuovo blog.</summary>
    public static string FileUrl(string bluditPath)
    {
        var path = $"{FilesFolder}/{bluditPath.TrimStart('/')}";
        return ImageExtensions.Contains(Path.GetExtension(bluditPath)) ? $"/image.axd?picture={path}" : $"/file.axd?file={path}";
    }

    /// <summary>
    /// Indirizzo nuovo per un vecchio indirizzo di Bludit, se ne esiste uno: "/slug" diventa "/post/slug" o "/page/slug",
    /// "/bl-content/uploads/..." diventa l'indirizzo del file importato.
    /// </summary>
    public static async Task<string?> NewAddressAsync(string path, string blogRoot, Func<string, Task<string?>> findBySlug)
    {
        if (BluditUploadPath().Match(path) is { Success: true } upload)
        {
            return blogRoot.TrimEnd('/') + FileUrl(WithoutThumbnails(Uri.UnescapeDataString(upload.Groups["path"].Value)));
        }

        var segment = path.Trim('/');
        return segment.Length > 0 && !segment.Contains('/') ? await findBySlug(Slug.Create(Uri.UnescapeDataString(segment))) : null;
    }

    // ---------------------------------------------------------------- testi

    private async Task<string> ConvertContentAsync(Guid blogId, string html, Regex siteHosts, IReadOnlyDictionary<string, (bool IsPage, string Slug, Guid Id)> targets, CancellationToken cancellationToken)
    {
        // Immagini incollate dentro il testo (data:...;base64): diventano file veri.
        var inline = new Dictionary<string, string>();
        foreach (Match match in InlineImage().Matches(html))
        {
            if (inline.ContainsKey(match.Value))
            {
                continue;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(Regex.Replace(match.Groups["data"].Value, @"\s", string.Empty));
            }
            catch (FormatException)
            {
                continue;
            }

            var extension = match.Groups["type"].Value.ToLowerInvariant() switch { "jpeg" or "jpg" => ".jpg", var other => "." + other };
            var name = Convert.ToHexString(SHA256.HashData(bytes))[..16].ToLowerInvariant() + extension;
            var path = files.Resolve(blogId, $"{FilesFolder}/inline/{name}");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, bytes, cancellationToken);
            inline[match.Value] = FileUrl($"inline/{name}");
        }

        foreach (var (data, url) in inline)
        {
            html = html.Replace(data, url, StringComparison.Ordinal);
        }

        // Link e immagini che puntano al vecchio sito.
        html = UploadReference().Replace(html, m => siteHosts.IsMatch(m.Groups["host"].Value) || m.Groups["host"].Length == 0
            ? FileUrl(WithoutThumbnails(Uri.UnescapeDataString(m.Groups["path"].Value)))
            : m.Value);
        html = InternalLink().Replace(html, m =>
        {
            if (m.Groups["host"].Length > 0 && !siteHosts.IsMatch(m.Groups["host"].Value))
            {
                return m.Value;
            }

            var path = m.Groups["path"].Value;
            var key = Uri.UnescapeDataString(path.Trim('/'));
            if (targets.TryGetValue(key, out var target))
            {
                return $"{m.Groups["attr"].Value}=\"{(target.IsPage ? "/page/" : "/post/")}{target.Slug}\"";
            }

            return m.Groups["host"].Length > 0 ? $"{m.Groups["attr"].Value}=\"/{path.TrimStart('/')}\"" : m.Value;
        });

        return CleanStyles(html);
    }

    private static string AddCoverImage(string content, string? uuid, string cover, string title)
    {
        var url = cover.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? cover
            : FileUrl($"pages/{uuid}/{cover}");
        if (content.Contains(url, StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        return $"<p><img src=\"{url}\" alt=\"{System.Net.WebUtility.HtmlEncode(title)}\" /></p>\n{content}";
    }

    /// <summary>Toglie dagli attributi style le proprietà che non sono in <see cref="KeptStyles"/>.</summary>
    public static string CleanStyles(string html) => StyleAttribute().Replace(html, m =>
    {
        var kept = m.Groups["css"].Value.Split(';')
            .Select(d => d.Trim())
            .Where(d => d.IndexOf(':') > 0 && KeptStyles.Contains(d[..d.IndexOf(':')].Trim()))
            .ToList();
        return kept.Count == 0 ? string.Empty : $" style=\"{string.Join("; ", kept)};\"";
    });

    private static string AdminApiDescription(string? description, string content) =>
        Api.AdminMapper.DescriptionOrExcerpt(description, content);

    private static string WithoutThumbnails(string path) => path.Replace("/thumbnails/", "/", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- file

    private int CopyUploads(Guid blogId, string uploads)
    {
        if (!Directory.Exists(uploads))
        {
            return 0;
        }

        var count = 0;
        foreach (var source in Directory.EnumerateFiles(uploads, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(uploads, source).Replace('\\', '/');
            if (relative.Contains("/thumbnails/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("thumbnails/", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith("profiles/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = files.Resolve(blogId, $"{FilesFolder}/{relative}");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
            count++;
        }

        return count;
    }

    // ---------------------------------------------------------------- impostazioni e dati

    private async Task UpdateSettingsAsync(Guid blogId, JsonNode? site, CancellationToken cancellationToken)
    {
        var blogSettings = await settings.GetAsync(blogId, cancellationToken);
        var changed = false;
        var description = (string?)site?["slogan"] is { Length: > 0 } slogan ? slogan : (string?)site?["description"];
        if (blogSettings.Description == new BlogSettings().Description && !string.IsNullOrWhiteSpace(description))
        {
            blogSettings.Description = description;
            changed = true;
        }

        if (blogSettings.TimeZoneId == new BlogSettings().TimeZoneId && (string?)site?["timezone"] is { Length: > 0 } zone && FindTimeZone(zone) is not null)
        {
            // Nell'amministrazione di Windows i fusi orari hanno i nomi di Windows ("W. Europe Standard Time").
            blogSettings.TimeZoneId = OperatingSystem.IsWindows() && TimeZoneInfo.TryConvertIanaIdToWindowsId(zone, out var windowsId) ? windowsId : zone;
            changed = true;
        }

        if (changed)
        {
            await settings.SaveAsync(blogSettings, blogId, cancellationToken);
        }
    }

    /// <summary>I database di Bludit sono file JSON preceduti da una riga PHP che ne impedisce la lettura dal web.</summary>
    private static JsonNode? ReadDatabase(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var text = File.ReadAllText(path);
        if (text.StartsWith("<?php", StringComparison.Ordinal))
        {
            text = text[(text.IndexOf("?>", StringComparison.Ordinal) + 2)..];
        }

        return JsonNode.Parse(text);
    }

    private static TimeZoneInfo? FindTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static DateTime? ToUtc(string? value, TimeZoneInfo? zone)
    {
        if (!DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return null;
        }

        return zone is null ? DateTime.SpecifyKind(local, DateTimeKind.Utc) : TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    /// <summary>Il dominio del vecchio sito, con e senza "www".</summary>
    private static Regex SiteHosts(string? url)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "localhost";
        if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
        {
            host = host[4..];
        }

        return new Regex($@"^https?://(www\.)?{Regex.Escape(host)}$", RegexOptions.IgnoreCase);
    }

    [GeneratedRegex(@"data:image/(?<type>png|jpe?g|gif|webp);base64,(?<data>[A-Za-z0-9+/=\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex InlineImage();

    [GeneratedRegex(@"(?<host>https?://[^/""'\s]+)?/bl-content/uploads/(?<path>[^""'\s?#)]+)", RegexOptions.IgnoreCase)]
    private static partial Regex UploadReference();

    [GeneratedRegex(@"^/bl-content/uploads/(?<path>.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex BluditUploadPath();

    [GeneratedRegex(@"(?<attr>href)=""(?<host>https?://[^/""]+)?(?<path>/[^""?#]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex InternalLink();

    [GeneratedRegex(@"\s+style=""(?<css>[^""]*)""", RegexOptions.IgnoreCase)]
    private static partial Regex StyleAttribute();
}

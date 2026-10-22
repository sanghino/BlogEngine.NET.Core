using System.Globalization;
using BlogEngine.Core.Files;
using BlogEngine.Core.Services;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Api;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace BlogEngine.Web.Protocols;

/// <summary>
/// API MetaWeblog/XML-RPC per scrivere dal computer con Open Live Writer e programmi simili (porting di
/// MetaWeblogHandler): metaWeblog.*, blogger.getUsersBlogs, blogger.deletePost e wp.* per pagine,
/// autori e tag. Utente e password arrivano in ogni chiamata e valgono i permessi del suo ruolo.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class MetaWeblogController(
    IUserService users,
    RequestBlogContext context,
    IRightsService rights,
    IPostService posts,
    IPageService pages,
    ICategoryService categories,
    IProfileService profiles,
    ISettingsService settings,
    ILinkGenerator links,
    FileStore files,
    IBlogClock clock,
    ILogger<MetaWeblogController> logger) : Controller
{
    public const string RateLimit = "metaweblog";

    [HttpPost("/metaweblog.axd")]
    [EnableRateLimiting(RateLimit)]
    [RequestSizeLimit(32 * 1024 * 1024)]
    public async Task<IActionResult> Handle()
    {
        string body;
        using (var reader = new StreamReader(Request.Body))
        {
            body = await reader.ReadToEndAsync(HttpContext.RequestAborted);
        }

        try
        {
            if ((await settings.GetAsync()).RequireSslMetaWeblogApi && !Request.IsHttps)
            {
                throw new XmlRpcFaultException(0, "This blog requires an SSL connection (https) for MetaWeblog API requests.");
            }

            var call = XmlRpc.Parse(body);

            // Nelle chiamate blogger.* il primo parametro è la "appKey", poi id, utente e password.
            var (userIndex, passwordIndex) = call.Method switch
            {
                "blogger.deletePost" => (2, 3),
                "wp.getPage" or "wp.editPage" => (2, 3),
                _ => (1, 2),
            };

            await SignInAsync(call.String(userIndex), call.String(passwordIndex));
            var result = await DispatchAsync(call);
            return Xml(XmlRpc.Response(result));
        }
        catch (XmlRpcFaultException ex)
        {
            return Xml(XmlRpc.Fault(ex.Code, ex.Message));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException)
        {
            return Xml(XmlRpc.Fault(11, "User authentication failed"));
        }
        catch (BlogEngine.Core.BlogValidationException ex)
        {
            return Xml(XmlRpc.Fault(0, string.Join(" ", HttpContext.RequestServices.GetRequiredService<MessageTranslator>().Translate(ex.Errors).Values)));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or FormatException)
        {
            return Xml(XmlRpc.Fault(0, HttpContext.RequestServices.GetRequiredService<MessageTranslator>()[ex.Message]));
        }
    }

    /// <summary>Indica a Open Live Writer le funzioni supportate dal blog (pagine, tag, slug, riassunto...).</summary>
    [HttpGet("/wlwmanifest.xml")]
    public IActionResult Manifest() => Content(FeedWriter.Write(writer =>
    {
        writer.WriteStartElement("manifest", "http://schemas.microsoft.com/wlw/manifest/weblog");
        writer.WriteStartElement("options");
        foreach (var (name, value) in new[]
        {
            ("clientType", "Metaweblog"), ("supportsEmbeds", "Yes"), ("supportsKeywords", "Yes"), ("supportsNewCategories", "Yes"),
            ("supportsNewCategoriesInline", "Yes"), ("supportsCommentPolicy", "Yes"), ("supportsSlug", "Yes"), ("supportsExcerpt", "Yes"),
            ("supportsPages", "Yes"), ("supportsPageParent", "Yes"), ("supportsAuthor", "Yes"), ("supportsGetTags", "Yes"),
            ("requiresHtmlTitles", "No"), ("fileUploadNameFormat", "{FileName}"),
        })
        {
            writer.WriteElementString(name, value);
        }

        writer.WriteEndElement();
        writer.WriteStartElement("weblog");
        writer.WriteElementString("ServiceName", "BlogEngine.NET");
        writer.WriteElementString("imageUrl", "Content/images/blog/wrenchicon16.png");
        writer.WriteElementString("watermarkImageUrl", "Content/images/blog/wrench84watermark.png");
        writer.WriteElementString("homepageLinkText", "View your blog");
        writer.WriteElementString("adminLinkText", "Manage your blog");
        writer.WriteElementString("adminUrl", "{blog-homepage-url}admin/");
        writer.WriteEndElement();
        writer.WriteEndElement();
    }), "text/xml; charset=utf-8");

    private async Task<object?> DispatchAsync(XmlRpcCall call)
    {
        switch (call.Method)
        {
            case "blogger.getUsersBlogs":
            case "metaWeblog.getUsersBlogs":
                var name = (await settings.GetAsync()).Name;
                return new List<object> { new Dictionary<string, object?> { ["url"] = Root, ["blogid"] = Root, ["blogName"] = name } };

            case "metaWeblog.getCategories":
                return await CategoriesAsync();

            case "metaWeblog.getRecentPosts":
                var count = call.Parameters.Count > 3 && call.Parameters[3] is int n ? n : 10;
                var recent = new List<object>();
                foreach (var post in (await posts.GetPostsAsync()).Where(p => !p.IsDeleted).OrderByDescending(p => p.DateCreated))
                {
                    // Solo i post che l'utente può modificare (i propri, o tutti per editor e amministratori).
                    if (recent.Count < Math.Clamp(count, 1, 200) && await posts.CanEditAsync(post))
                    {
                        recent.Add(await PostStructAsync(post));
                    }
                }

                return recent;

            case "metaWeblog.getPost":
                return await PostStructAsync(await FindPostAsync(call.String(0)));

            case "metaWeblog.newPost":
                Demand(await rights.IsAuthorizedAsync(Rights.CreateNewPosts));
                var created = new Post();
                await SavePostAsync(created, Struct(call, 3), Flag(call, 4));
                return created.Id.ToString();

            case "metaWeblog.editPost":
                var existing = await FindPostAsync(call.String(0));
                Demand(await posts.CanEditAsync(existing));
                await SavePostAsync(EntityCopies.Copy(existing), Struct(call, 3), Flag(call, 4));
                return true;

            case "blogger.deletePost":
                var toDelete = await FindPostAsync(call.String(1));
                Demand(await posts.CanDeleteAsync(toDelete));
                await posts.DeleteAsync(toDelete);
                return true;

            case "metaWeblog.newMediaObject":
                return await SaveMediaAsync(Struct(call, 3));

            case "wp.newPage":
                Demand(await rights.IsAuthorizedAsync(Rights.CreateNewPages));
                var page = new Page();
                await SavePageAsync(page, Struct(call, 3), Flag(call, 4));
                return page.Id.ToString();

            case "wp.getPageList":
            case "wp.getPages":
                var list = new List<object>();
                foreach (var item in (await pages.GetPagesAsync()).Where(p => !p.IsDeleted).OrderBy(p => p.SortOrder))
                {
                    list.Add(await PageStructAsync(item));
                }

                return list;

            case "wp.getPage":
                return await PageStructAsync(await FindPageAsync(call.String(1)));

            case "wp.editPage":
                var editPage = await FindPageAsync(call.String(1));
                Demand(await pages.CanEditAsync());
                await SavePageAsync(EntityCopies.Copy(editPage), Struct(call, 4), Flag(call, 5));
                return true;

            case "wp.deletePage":
                var deletePage = await FindPageAsync(call.String(3));
                Demand(await pages.CanEditAsync());
                await pages.DeleteAsync(EntityCopies.Copy(deletePage));
                return true;

            case "wp.getAuthors":
                var authors = new List<object>();
                foreach (var user in await users.GetUsersAsync())
                {
                    var profile = await profiles.GetAsync(user.UserName);
                    authors.Add(new Dictionary<string, object?>
                    {
                        ["user_id"] = user.UserName,
                        ["user_login"] = user.UserName,
                        ["display_name"] = string.IsNullOrWhiteSpace(profile?.DisplayName) ? user.UserName : profile.DisplayName,
                        ["user_email"] = user.Email,
                        ["meta_value"] = string.Empty,
                    });
                }

                return authors;

            case "wp.getTags":
                return (await posts.GetPostsAsync()).Where(p => !p.IsDeleted).SelectMany(p => p.Tags)
                    .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                    .Select(g => (object)new Dictionary<string, object?>
                    {
                        ["tag_id"] = g.Key,
                        ["name"] = g.Key,
                        ["count"] = g.Count(),
                        ["slug"] = BlogEngine.Text.Slug.Create(g.Key),
                        ["html_url"] = links.ToAbsolute(links.Tag(g.Key)).ToString(),
                        ["rss_url"] = $"{Root}syndication.axd?tag={Uri.EscapeDataString(g.Key)}",
                    })
                    .ToList();

            case "blogger.getUserInfo":
                throw new XmlRpcFaultException(10, "The method GetUserInfo is not implemented.");

            default:
                throw new XmlRpcFaultException(2, $"Unknown Method. ({call.Method})");
        }
    }

    /// <summary>Verifica utente e password e lavora con i suoi ruoli per il resto della chiamata.</summary>
    private async Task SignInAsync(string userName, string password)
    {
        var user = await users.ValidateCredentialsAsync(userName, password);
        if (user is null)
        {
            logger.LogWarning("MetaWeblog: accesso non riuscito per {User} da {Ip}.", userName, HttpContext.Connection.RemoteIpAddress);
            throw new XmlRpcFaultException(11, "User authentication failed");
        }

        context.UserName = user.UserName;
        context.Roles = (await users.GetRolesForUserAsync(user.UserName)).Append(SystemRoles.Anonymous).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task SavePostAsync(Post post, IReadOnlyDictionary<string, object?> data, bool publish)
    {
        var author = Text(data, "wp_author_id");
        post.Author = author.Length > 0 ? author : string.IsNullOrEmpty(post.Author) ? context.UserName! : post.Author;
        post.Title = Text(data, "title");
        post.Content = Text(data, "description");
        if (data.ContainsKey("mt_excerpt"))
        {
            post.Description = Text(data, "mt_excerpt");
        }

        if (Text(data, "wp_slug") is { Length: > 0 } slug)
        {
            post.Slug = slug;
        }

        if (data.TryGetValue("mt_allow_comments", out var comments) && comments is not null)
        {
            post.HasCommentsEnabled = comments switch
            {
                int i => i == 1,
                bool b => b,
                _ => comments.ToString() is "1" or "open",
            };
        }

        if (publish && !await posts.CanPublishAsync(post.Author))
        {
            throw new XmlRpcFaultException(11, "Not authorized to publish this Post.");
        }

        post.IsPublished = publish;
        if (data.TryGetValue("dateCreated", out var date) && date is DateTime created && created.Year > 1900)
        {
            post.DateCreated = created;
        }
        else if (post.DateCreated == default)
        {
            post.DateCreated = clock.UtcNow;
        }

        // Le categorie sono indicate per nome; quelle nuove vengono create (come faceva Live Writer).
        if (data.TryGetValue("categories", out var list) && list is List<object?> names)
        {
            post.Categories = [];
            foreach (var categoryName in names.Select(c => c?.ToString()?.Trim()).Where(c => !string.IsNullOrEmpty(c)))
            {
                var category = (await categories.GetCategoriesAsync()).FirstOrDefault(c => string.Equals(c.Title, categoryName, StringComparison.OrdinalIgnoreCase));
                if (category is null)
                {
                    category = new Category { Title = categoryName! };
                    await categories.SaveAsync(category);
                }

                if (!post.Categories.Contains(category.Id))
                {
                    post.Categories.Add(category.Id);
                }
            }
        }

        if (data.ContainsKey("mt_keywords"))
        {
            post.Tags = Text(data, "mt_keywords").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        if (string.IsNullOrWhiteSpace(post.Description))
        {
            post.Description = AdminMapper.DescriptionOrExcerpt(null, post.Content);
        }

        await posts.SaveAsync(post);
    }

    private async Task SavePageAsync(Page page, IReadOnlyDictionary<string, object?> data, bool publish)
    {
        page.Title = Text(data, "title");
        page.Content = Text(data, "description");
        page.IsPublished = publish;
        if (Text(data, "wp_slug") is { Length: > 0 } slug)
        {
            page.Slug = slug;
        }

        if (data.ContainsKey("mt_keywords"))
        {
            page.Keywords = Text(data, "mt_keywords");
        }

        if (Guid.TryParse(Text(data, "wp_page_parent_id"), out var parent))
        {
            page.Parent = parent;
        }

        if (data.TryGetValue("dateCreated", out var date) && date is DateTime created && created.Year > 1900)
        {
            page.DateCreated = created;
        }
        else if (page.DateCreated == default)
        {
            page.DateCreated = clock.UtcNow;
        }

        await pages.SaveAsync(page);
    }

    /// <summary>Immagini e file inseriti nel post: salvati nella cartella del mese, come i caricamenti dell'editor.</summary>
    private async Task<object> SaveMediaAsync(IReadOnlyDictionary<string, object?> data)
    {
        Demand(await rights.IsAuthorizedAsync(Rights.EditOwnPosts));
        if (data.GetValueOrDefault("bits") is not byte[] bits || bits.Length == 0)
        {
            throw new XmlRpcFaultException(0, "The media object has no content.");
        }

        var fileName = FileStore.SafeFileName(Path.GetFileName(Text(data, "name").Replace('\\', '/')));
        var now = await clock.ToBlogTimeAsync(clock.UtcNow);
        using var stream = new MemoryStream(bits);
        var saved = await files.SaveAsync(context.Blog.Id, now.ToString("/yyyy/MM", CultureInfo.InvariantCulture), fileName, stream, HttpContext.RequestAborted);
        var isImage = Text(data, "type").StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            || new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" }.Contains(Path.GetExtension(saved), StringComparer.OrdinalIgnoreCase);
        var url = isImage ? $"{Root}image.axd?picture={Uri.EscapeDataString(saved)}" : $"{Root}file.axd?file={Uri.EscapeDataString(saved)}";
        return new Dictionary<string, object?> { ["url"] = url };
    }

    private async Task<List<object>> CategoriesAsync()
    {
        var result = new List<object>();
        foreach (var category in (await categories.GetCategoriesAsync()).OrderBy(c => c.Title, StringComparer.CurrentCultureIgnoreCase))
        {
            result.Add(new Dictionary<string, object?>
            {
                ["description"] = category.Title,
                ["categoryid"] = category.Id.ToString(),
                ["title"] = category.Title,
                ["htmlUrl"] = links.ToAbsolute(await links.CategoryAsync(category)).ToString(),
                ["rssUrl"] = links.ToAbsolute(await links.CategoryFeedAsync(category)).ToString(),
            });
        }

        return result;
    }

    private async Task<Dictionary<string, object?>> PostStructAsync(Post post)
    {
        var categoryNames = new List<object?>();
        foreach (var id in post.Categories)
        {
            if (await categories.GetByIdAsync(id) is { } category)
            {
                categoryNames.Add(category.Title);
            }
        }

        var link = links.ToAbsolute(await links.PostAsync(post)).ToString();
        return new Dictionary<string, object?>
        {
            ["postid"] = post.Id.ToString(),
            ["title"] = post.Title,
            ["description"] = post.Content,
            ["link"] = link,
            ["permaLink"] = link,
            ["wp_slug"] = post.Slug,
            ["mt_excerpt"] = post.Description,
            ["mt_allow_comments"] = post.HasCommentsEnabled ? 1 : 0,
            ["dateCreated"] = post.DateCreated,
            ["date_created_gmt"] = post.DateCreated,
            ["publish"] = post.IsPublished,
            ["mt_keywords"] = string.Join(",", post.Tags),
            ["wp_author_id"] = post.Author,
            ["categories"] = categoryNames,
        };
    }

    private async Task<Dictionary<string, object?>> PageStructAsync(Page page) => new()
    {
        ["page_id"] = page.Id.ToString(),
        ["title"] = page.Title,
        ["page_title"] = page.Title,
        ["description"] = page.Content,
        ["link"] = links.ToAbsolute(await links.PageAsync(page)).ToString(),
        ["mt_convert_breaks"] = "__default__",
        ["dateCreated"] = page.DateCreated,
        ["date_created_gmt"] = page.DateCreated,
        ["page_parent_id"] = page.Parent == Guid.Empty ? string.Empty : page.Parent.ToString(),
        ["wp_slug"] = page.Slug,
        ["mt_keywords"] = page.Keywords,
    };

    private async Task<Post> FindPostAsync(string id) =>
        Guid.TryParse(id, out var postId) && await posts.GetByIdAsync(postId) is { IsDeleted: false } post
            ? post
            : throw new XmlRpcFaultException(0, "The post does not exist.");

    private async Task<Page> FindPageAsync(string id) =>
        Guid.TryParse(id, out var pageId) && await pages.GetByIdAsync(pageId) is { IsDeleted: false } page
            ? page
            : throw new XmlRpcFaultException(0, "The page does not exist.");

    private string Root => links.ToAbsolute(links.BlogRoot).ToString();

    private static IReadOnlyDictionary<string, object?> Struct(XmlRpcCall call, int index) =>
        index < call.Parameters.Count && call.Parameters[index] is Dictionary<string, object?> data
            ? data
            : throw new XmlRpcFaultException(0, "Invalid request: a struct parameter is missing.");

    private static bool Flag(XmlRpcCall call, int index) =>
        index < call.Parameters.Count && call.Parameters[index] switch
        {
            bool b => b,
            int i => i != 0,
            var other => other?.ToString() is "1" or "true",
        };

    private static string Text(IReadOnlyDictionary<string, object?> data, string key) =>
        data.TryGetValue(key, out var value) ? value?.ToString()?.Trim() ?? string.Empty : string.Empty;

    private static void Demand(bool allowed)
    {
        if (!allowed)
        {
            throw new XmlRpcFaultException(11, "User authentication failed");
        }
    }

    private ContentResult Xml(string xml) => Content(xml, "text/xml; charset=utf-8");
}

/// <summary>Esportazione BlogML (blogml.axd), riservata agli amministratori come in origine.</summary>
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class BlogMLController(BlogMLService blogML, IRightsService rights) : Controller
{
    [HttpGet("/blogml.axd")]
    public async Task<IActionResult> Export()
    {
        if (!rights.IsAdministrator)
        {
            return Unauthorized();
        }

        var xml = await blogML.ExportAsync(HttpContext.RequestAborted);
        return File(System.Text.Encoding.UTF8.GetBytes(xml), "text/xml", $"BlogML-{DateTime.UtcNow:yyyyMMdd}.xml");
    }
}

using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using BlogEngine.Core.Services;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Settings;
using BlogEngine.Web.Admin;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace BlogEngine.Web.Themes;

/// <summary>Titolo, descrizione e collegamenti dell'intestazione HTML di una pagina pubblica.</summary>
public sealed class PageMeta
{
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Keywords { get; set; } = string.Empty;

    public string Author { get; set; } = string.Empty;

    public string Canonical { get; set; } = string.Empty;

    /// <summary>Collegamenti rel="prev"/"next" della paginazione.</summary>
    public string PreviousUrl { get; set; } = string.Empty;

    public string NextUrl { get; set; } = string.Empty;

    /// <summary>Le pagine di errore e di ricerca non vanno indicizzate dai motori di ricerca.</summary>
    public bool NoIndex { get; set; }

    /// <summary>Codice HTML aggiuntivo per l'intestazione (es. l'header X-Pingback come link).</summary>
    public string ExtraHead { get; set; } = string.Empty;
}

/// <summary>
/// Mostra le pagine pubbliche con il tema attivo (porting di BlogBasePage): sceglie il tema,
/// prepara le variabili comuni e l'intestazione HTML, poi passa il lavoro al motore Liquid.
/// </summary>
public sealed class SiteRenderer(
    ThemeEngine engine,
    ThemeCatalog themes,
    WidgetService widgets,
    ISettingsService settings,
    IBlogContext context,
    IRightsService rights,
    IPageService pages,
    ILinkGenerator links,
    ICustomFieldService customFields,
    IStringLocalizer<Labels> labels,
    IAntiforgery antiforgery,
    IHttpContextAccessor httpAccessor)
{
    /// <summary>Parametro con cui si prova un tema senza attivarlo (?theme=Nome), come in origine.</summary>
    public const string ThemeQueryKey = "theme";

    private ThemeScope? _scope;
    private BlogSettings? _settings;
    private AntiforgeryTokenSet? _tokens;

    private AntiforgeryTokenSet Tokens => _tokens ??= antiforgery.GetAndStoreTokens(Http);

    private HttpContext Http => httpAccessor.HttpContext ?? throw new InvalidOperationException("Nessuna richiesta HTTP corrente.");

    public async Task<BlogSettings> GetSettingsAsync() => _settings ??= await settings.GetAsync();

    /// <summary>Etichetta tradotta; se manca restituisce la chiave.</summary>
    public string T(string key)
    {
        var value = labels[key];
        return value.ResourceNotFound ? key : value.Value;
    }

    /// <summary>Mostra una pagina completa (template + layout).</summary>
    public async Task<IActionResult> PageAsync(string template, PageMeta meta, IReadOnlyDictionary<string, object?> values, int statusCode = StatusCodes.Status200OK)
    {
        var scope = await GetScopeAsync();
        await AddPageValuesAsync(scope, template, meta);
        var layout = template == "post" ? "layout-post" : template == "index" ? string.Empty : "layout-page";
        var html = await engine.RenderPageAsync(scope, template, layout, values);
        return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = statusCode };
    }

    /// <summary>Mostra un frammento senza layout (es. il commento appena aggiunto).</summary>
    public async Task<string> PartialAsync(string template, IReadOnlyDictionary<string, object?> values)
    {
        var scope = await GetScopeAsync();
        return await engine.RenderPartialAsync(scope, template, values);
    }

    public async Task<ThemeScope> GetScopeAsync()
    {
        if (_scope is not null)
        {
            return _scope;
        }

        var blogSettings = await GetSettingsAsync();
        var theme = ResolveTheme(blogSettings);
        var blogRoot = links.BlogRoot;
        var appRoot = ApplicationRoot(Http.Request.PathBase.Value, blogRoot);

        Dictionary<string, string>? fields = null;
        async Task<string?> ThemeField(string key)
        {
            fields ??= (await customFields.GetFieldsAsync(CustomField.ThemeType, theme.Id))
                .GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
            return fields.TryGetValue(key, out var value) ? value : null;
        }

        var scope = new ThemeScope(theme, blogRoot, appRoot, T, ThemeField, widgets);
        scope.GlobalValues["blog"] = await BuildBlogAsync(blogSettings, theme, blogRoot, appRoot);
        scope.GlobalValues["user"] = await BuildUserAsync();
        scope.GlobalValues["theme"] = new { id = theme.Id, title = theme.Title, url = scope.ThemeUrl };
        scope.GlobalValues["request"] = new
        {
            path = Http.Request.Path.Value ?? "/",
            url = Http.Request.PathBase.Add(Http.Request.Path).Value + Http.Request.QueryString,
            query = Http.Request.Query["q"].ToString(),
        };

        // Campo nascosto da inserire nei moduli inviati in POST: {{ antiforgery }}.
        var tokens = Tokens;
        scope.GlobalValues["antiforgery"] = new Microsoft.AspNetCore.Html.HtmlString(
            $"<input type=\"hidden\" name=\"{WebUtility.HtmlEncode(tokens.FormFieldName)}\" value=\"{WebUtility.HtmlEncode(tokens.RequestToken)}\" />");

        _scope = scope;
        return scope;
    }

    private ThemeInfo ResolveTheme(BlogSettings blogSettings)
    {
        // Ordine come in origine: tema richiesto nell'indirizzo, poi il cookie del selettore di temi, poi quello del blog.
        var requested = Http.Request.Query[ThemeQueryKey].ToString();
        if (string.IsNullOrEmpty(requested) && !string.IsNullOrEmpty(blogSettings.ThemeCookieName))
        {
            requested = Http.Request.Cookies[blogSettings.ThemeCookieName] ?? string.Empty;
        }

        var theme = (string.IsNullOrEmpty(requested) ? null : themes.Find(requested)) ?? themes.FindOrFallback(blogSettings.Theme);
        return theme ?? new ThemeInfo("Default", "Default", string.Empty, string.Empty, string.Empty, string.Empty, engine.DefaultsFolder, false);
    }

    private async Task<BlogModel> BuildBlogAsync(BlogSettings blogSettings, ThemeInfo theme, string blogRoot, string appRoot)
    {
        var current = Http.Request.Path.Value ?? "/";
        var visible = (await pages.GetVisiblePagesAsync()).Where(p => p.IsPublished && p.ShowInList).OrderBy(p => p.SortOrder).ToList();
        var pageLinks = new List<PageLinkModel>();
        foreach (var parent in visible.Where(p => p.Parent == Guid.Empty || visible.All(x => x.Id != p.Parent)))
        {
            var children = new List<PageLinkModel>();
            foreach (var child in visible.Where(p => p.Parent == parent.Id))
            {
                children.Add(await PageLinkAsync(child, current, []));
            }

            pageLinks.Add(await PageLinkAsync(parent, current, children));
        }

        var culture = CultureInfo.CurrentUICulture;
        return new BlogModel
        {
            Id = context.Blog.Id.ToString(),
            Name = blogSettings.Name,
            Description = blogSettings.Description,
            Url = blogRoot,
            AbsoluteUrl = links.ToAbsolute(blogRoot).ToString(),
            AppUrl = appRoot,
            Language = culture.Name,
            Direction = culture.TextInfo.IsRightToLeft ? "rtl" : "ltr",
            Theme = theme.Id,
            Version = AdminViewContext.Version,
            Year = DateTime.Now.Year,
            ArchiveUrl = links.Archive(),
            ContactUrl = links.Contact(),
            SearchUrl = links.Search(),
            CalendarUrl = blogRoot + "calendar",
            FeedUrl = FeedUrl(blogSettings, blogRoot),
            LoginUrl = $"{blogRoot}account/login?ReturnUrl={Uri.EscapeDataString(blogRoot + "admin/")}",
            LogoutUrl = blogRoot + "account/logout",
            AdminUrl = blogRoot + "admin/",
            ChangePasswordUrl = blogRoot + "account/change-password",
            RegisterUrl = blogSettings.EnableSelfRegistration ? blogRoot + "account/register" : string.Empty,
            CommentsEnabled = blogSettings.IsCommentsEnabled,
            RatingEnabled = blogSettings.EnableRating,
            SearchDefaultText = blogSettings.SearchDefaultText,
            Pages = pageLinks,
        };
    }

    private async Task<PageLinkModel> PageLinkAsync(Page page, string currentPath, IReadOnlyList<PageLinkModel> children)
    {
        var url = await links.PageAsync(page);
        return new PageLinkModel
        {
            Title = page.Title,
            Description = page.Description,
            Url = url,
            IsCurrent = url.EndsWith(currentPath, StringComparison.OrdinalIgnoreCase) && currentPath != "/",
            Children = children,
        };
    }

    private async Task<UserModel> BuildUserAsync() => new()
    {
        IsAuthenticated = !string.IsNullOrEmpty(context.UserName),
        Name = context.UserName ?? string.Empty,
        IsAdmin = rights.IsAdministrator,
        CanAccessAdmin = await rights.IsAuthorizedAsync(Rights.AccessAdminPages),
        CanCreatePosts = await rights.IsAuthorizedAsync(Rights.CreateNewPosts),
        CanManageWidgets = await rights.IsAuthorizedAsync(Rights.ManageWidgets),
        CanManageThemes = await rights.IsAuthorizedAsync(Rights.ManageThemes),
        CanAccessSettings = await rights.IsAuthorizedAsync(Rights.AccessAdminSettingsPages),
    };

    /// <summary>Variabili della singola pagina: titolo e codice per &lt;head&gt; e fine pagina.</summary>
    private async Task AddPageValuesAsync(ThemeScope scope, string template, PageMeta meta)
    {
        var blogSettings = await GetSettingsAsync();
        var title = string.IsNullOrEmpty(meta.Title)
            ? blogSettings.Name
            : blogSettings.UseBlogNameInPageTitles ? $"{blogSettings.Name} | {meta.Title}" : meta.Title;

        scope.GlobalValues["page_title"] = title;
        scope.GlobalValues["template"] = template;
        scope.GlobalValues["meta"] = new { description = meta.Description, keywords = meta.Keywords, author = meta.Author };
        scope.GlobalValues["content_for_header"] = new Microsoft.AspNetCore.Html.HtmlString(BuildHeader(scope, blogSettings, meta));
        scope.GlobalValues["content_for_footer"] = new Microsoft.AspNetCore.Html.HtmlString(blogSettings.TrackingScript ?? string.Empty);
    }

    /// <summary>Porting delle aggiunte automatiche di BlogBasePage all'intestazione della pagina.</summary>
    private string BuildHeader(ThemeScope scope, BlogSettings blogSettings, PageMeta meta)
    {
        static string E(string value) => WebUtility.HtmlEncode(value);
        var absolute = links.ToAbsolute(scope.BlogRoot).ToString();
        var name = E(blogSettings.Name);
        var feed = E(FeedUrl(blogSettings, scope.BlogRoot));
        var html = new StringBuilder();

        html.AppendLine($"<meta name=\"generator\" content=\"{E(AdminViewContext.Version)}\" />");
        if (!string.IsNullOrEmpty(meta.Description))
        {
            html.AppendLine($"<meta name=\"description\" content=\"{E(meta.Description)}\" />");
        }

        if (!string.IsNullOrEmpty(meta.Keywords))
        {
            html.AppendLine($"<meta name=\"keywords\" content=\"{E(meta.Keywords)}\" />");
        }

        if (!string.IsNullOrEmpty(meta.Author))
        {
            html.AppendLine($"<meta name=\"author\" content=\"{E(meta.Author)}\" />");
        }

        if (meta.NoIndex)
        {
            html.AppendLine("<meta name=\"robots\" content=\"noindex\" />");
        }

        if (!string.IsNullOrEmpty(meta.Canonical))
        {
            html.AppendLine($"<link rel=\"canonical\" href=\"{E(meta.Canonical)}\" />");
        }

        if (!string.IsNullOrEmpty(meta.PreviousUrl))
        {
            html.AppendLine($"<link rel=\"prev\" href=\"{E(meta.PreviousUrl)}\" />");
        }

        if (!string.IsNullOrEmpty(meta.NextUrl))
        {
            html.AppendLine($"<link rel=\"next\" href=\"{E(meta.NextUrl)}\" />");
        }

        html.AppendLine($"<link rel=\"contents\" title=\"{E(T("archive"))}\" href=\"{E(links.Archive())}\" />");
        html.AppendLine($"<link rel=\"start\" title=\"{name}\" href=\"{E(scope.BlogRoot)}\" />");
        html.AppendLine($"<link type=\"application/rsd+xml\" rel=\"edituri\" title=\"RSD\" href=\"{E(absolute)}rsd.axd\" />");
        html.AppendLine($"<link type=\"application/rdf+xml\" rel=\"meta\" title=\"SIOC\" href=\"{E(absolute)}sioc.axd\" />");
        html.AppendLine($"<link type=\"application/apml+xml\" rel=\"meta\" title=\"APML\" href=\"{E(absolute)}apml.axd\" />");
        html.AppendLine($"<link type=\"application/rdf+xml\" rel=\"meta\" title=\"FOAF\" href=\"{E(absolute)}foaf.axd\" />");
        html.AppendLine($"<link type=\"application/rss+xml\" rel=\"alternate\" title=\"{name} (RSS)\" href=\"{feed}{(feed.Contains('?') ? "&amp;" : "?")}format=rss\" />");
        html.AppendLine($"<link type=\"application/atom+xml\" rel=\"alternate\" title=\"{name} (ATOM)\" href=\"{feed}{(feed.Contains('?') ? "&amp;" : "?")}format=atom\" />");
        if (blogSettings.EnableOpenSearch)
        {
            html.AppendLine($"<link type=\"application/opensearchdescription+xml\" rel=\"search\" title=\"{name}\" href=\"{E(absolute)}opensearch.axd\" />");
        }

        html.AppendLine($"<link rel=\"stylesheet\" href=\"{E(scope.AppRoot)}Content/Auto/Global.css\" />");
        html.AppendLine($"<script src=\"{E(scope.AppRoot)}Scripts/jquery-2.1.4.min.js\"></script>");
        html.AppendLine($"<script>BlogEngineRes = {ResourcesScript(scope)};</script>");
        html.AppendLine($"<script src=\"{E(scope.AppRoot)}Scripts/blog.js\"></script>");
        html.AppendLine(meta.ExtraHead);
        html.AppendLine(blogSettings.HtmlHeader ?? string.Empty);
        return html.ToString();
    }

    /// <summary>
    /// Variabili per blog.js (porting di res.axd): indirizzi, id del blog, token antifalsificazione
    /// per le chiamate in POST e le etichette tradotte usate dagli script.
    /// </summary>
    private string ResourcesScript(ThemeScope scope)
    {
        string[] keys =
        [
            "savingTheComment", "comments", "commentWasSaved", "commentWaitingModeration", "cancel", "beTheFirstToRate",
            "currentlyRated", "ratingHasBeenRegistered", "rateThisXStars", "areYouSure", "areYouSureDeleteComment",
            "required", "enterValidEmail", "name", "email", "comment", "thankYou", "enterEmailAddress", "filter",
            "filterByApml", "filterByApmlDescription", "delete", "replyToThis", "cancelReply",
        ];

        var tokens = Tokens;
        var data = new Dictionary<string, object?>
        {
            ["webRoot"] = scope.BlogRoot,
            ["applicationWebRoot"] = scope.AppRoot,
            ["blogInstanceId"] = context.Blog.Id.ToString(),
            ["fileExtension"] = string.Empty,
            ["antiforgery"] = tokens.RequestToken,
            ["antiforgeryHeader"] = tokens.HeaderName,
            ["i18n"] = keys.Distinct().ToDictionary(k => k, T),
        };

        // Codifica sicura per un tag <script> (niente "<" o "</script>" nei testi).
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default });
    }

    private static string FeedUrl(BlogSettings blogSettings, string blogRoot) =>
        string.IsNullOrWhiteSpace(blogSettings.AlternateFeedUrl) ? blogRoot + "syndication.axd" : blogSettings.AlternateFeedUrl;

    /// <summary>Radice dell'applicazione: il PathBase della richiesta senza la cartella del blog.</summary>
    internal static string ApplicationRoot(string? pathBase, string blogRoot)
    {
        var basePath = (pathBase ?? string.Empty).TrimEnd('/');
        var blogPath = blogRoot.TrimEnd('/');
        if (blogPath.Length > 0 && basePath.EndsWith(blogPath, StringComparison.OrdinalIgnoreCase))
        {
            basePath = basePath[..^blogPath.Length];
        }

        return basePath + "/";
    }
}

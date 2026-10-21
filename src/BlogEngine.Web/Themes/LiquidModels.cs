using Microsoft.AspNetCore.Html;

namespace BlogEngine.Web.Themes;

// Oggetti passati ai template Liquid. Nei template i nomi delle proprietà sono in snake_case
// (es. post.date_created). Le proprietà di tipo HtmlString vengono scritte senza codifica,
// tutte le altre vengono codificate in HTML.

/// <summary>Il blog corrente: disponibile in tutti i template come <c>blog</c>.</summary>
public sealed class BlogModel
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>Radice del blog, es. "/" oppure "/secondo/".</summary>
    public string Url { get; init; } = "/";

    public string AbsoluteUrl { get; init; } = string.Empty;

    /// <summary>Radice dell'applicazione, per i file comuni (Scripts/, Content/).</summary>
    public string AppUrl { get; init; } = "/";

    public string Language { get; init; } = "en";

    /// <summary>"ltr" oppure "rtl".</summary>
    public string Direction { get; init; } = "ltr";

    public string Theme { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public int Year { get; init; }

    public string ArchiveUrl { get; init; } = string.Empty;

    public string ContactUrl { get; init; } = string.Empty;

    public string SearchUrl { get; init; } = string.Empty;

    public string CalendarUrl { get; init; } = string.Empty;

    public string FeedUrl { get; init; } = string.Empty;

    public string LoginUrl { get; init; } = string.Empty;

    public string LogoutUrl { get; init; } = string.Empty;

    public string AdminUrl { get; init; } = string.Empty;

    public string ChangePasswordUrl { get; init; } = string.Empty;

    public string RegisterUrl { get; init; } = string.Empty;

    public bool CommentsEnabled { get; init; }

    public bool RatingEnabled { get; init; }

    public string SearchDefaultText { get; init; } = string.Empty;

    /// <summary>Pagine da mostrare nel menu (solo quelle pubblicate e "visibili nell'elenco").</summary>
    public IReadOnlyList<PageLinkModel> Pages { get; init; } = [];
}

/// <summary>Il visitatore: disponibile come <c>user</c>.</summary>
public sealed class UserModel
{
    public bool IsAuthenticated { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool IsAdmin { get; init; }

    public bool CanAccessAdmin { get; init; }

    public bool CanCreatePosts { get; init; }

    public bool CanManageWidgets { get; init; }

    public bool CanManageThemes { get; init; }

    public bool CanAccessSettings { get; init; }
}

public sealed class PageLinkModel
{
    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public bool IsCurrent { get; init; }

    public IReadOnlyList<PageLinkModel> Children { get; init; } = [];
}

public sealed class LinkModel
{
    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string FeedUrl { get; init; } = string.Empty;

    public int Count { get; init; }
}

public sealed class PostModel
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Posizione nell'elenco (0, 1, 2...), come la proprietà Index dei vecchi temi.</summary>
    public int Index { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Slug { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string AbsoluteUrl { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public string AuthorName { get; init; } = string.Empty;

    public string AuthorUrl { get; init; } = string.Empty;

    public DateTime DateCreated { get; init; }

    public DateTime DateModified { get; init; }

    /// <summary>Testo del post (o il riassunto negli elenchi), già elaborato dalle estensioni.</summary>
    public HtmlString Body { get; init; } = HtmlString.Empty;

    public HtmlString Content { get; init; } = HtmlString.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>Descrizione senza HTML e su una sola riga, per meta tag e dati strutturati.</summary>
    public string PlainDescription { get; init; } = string.Empty;

    public string FirstImage { get; init; } = string.Empty;

    public string AbsoluteFirstImage { get; init; } = string.Empty;

    public IReadOnlyList<LinkModel> Categories { get; init; } = [];

    public IReadOnlyList<LinkModel> Tags { get; init; } = [];

    public int CommentsCount { get; init; }

    public bool CommentsEnabled { get; init; }

    public bool IsPublished { get; init; }

    public double Rating { get; init; }

    public int Raters { get; init; }

    /// <summary>Contenitore del voto letto da blog.js: "id|votanti|voto|blog".</summary>
    public HtmlString RatingHtml { get; init; } = HtmlString.Empty;

    /// <summary>Link di amministrazione (modifica, elimina, approva commenti), vuoto per i visitatori.</summary>
    public HtmlString AdminLinks { get; init; } = HtmlString.Empty;

    public string FeedUrl { get; init; } = string.Empty;

    public string EditUrl { get; init; } = string.Empty;
}

public sealed class CommentModel
{
    public string Id { get; init; } = string.Empty;

    public string ParentId { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public string Website { get; init; } = string.Empty;

    public string Country { get; init; } = string.Empty;

    public string AvatarUrl { get; init; } = string.Empty;

    /// <summary>Immagine dell'avatar già pronta (porting di Gravatar(48)).</summary>
    public HtmlString Avatar { get; init; } = HtmlString.Empty;

    public HtmlString Flag { get; init; } = HtmlString.Empty;

    public DateTime DateCreated { get; init; }

    /// <summary>Testo già codificato, con gli a capo trasformati in &lt;br /&gt;.</summary>
    public HtmlString Text { get; init; } = HtmlString.Empty;

    public bool IsApproved { get; init; }

    public bool IsPingback { get; init; }

    public bool IsByPostAuthor { get; init; }

    public HtmlString ReplyLink { get; init; } = HtmlString.Empty;

    public HtmlString AdminLinks { get; init; } = HtmlString.Empty;

    public IReadOnlyList<CommentModel> Replies { get; init; } = [];
}

public sealed class CommentsModel
{
    public IReadOnlyList<CommentModel> Items { get; init; } = [];

    public IReadOnlyList<CommentModel> Pingbacks { get; init; } = [];

    public int Count { get; init; }

    /// <summary>Il modulo per i nuovi commenti va mostrato.</summary>
    public bool IsOpen { get; init; }

    public bool NestingEnabled { get; init; }

    public bool Moderation { get; init; }

    public bool ShowWebsite { get; init; }

    public bool ShowCountry { get; init; }

    /// <summary>Anteprima del commento mentre lo si scrive.</summary>
    public bool ShowLivePreview { get; init; }

    /// <summary>Captcha delle estensioni attive, da inserire nell'elemento commentCaptcha del modulo.</summary>
    public HtmlString Captcha { get; init; } = HtmlString.Empty;

    /// <summary>"blogengine", "disqus" o "facebook".</summary>
    public string Provider { get; init; } = "blogengine";

    public string DisqusWebsiteName { get; init; } = string.Empty;

    public bool DisqusDevMode { get; init; }

    public string FacebookLanguage { get; init; } = string.Empty;

    public string FacebookAppId { get; init; } = string.Empty;

    /// <summary>Indirizzo a cui blog.js invia i nuovi commenti.</summary>
    public string PostUrl { get; init; } = string.Empty;
}

public sealed class StaticPageModel
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string AbsoluteUrl { get; init; } = string.Empty;

    public HtmlString Content { get; init; } = HtmlString.Empty;

    public string Description { get; init; } = string.Empty;

    public string Keywords { get; init; } = string.Empty;

    public DateTime DateCreated { get; init; }

    public DateTime DateModified { get; init; }

    public IReadOnlyList<PageLinkModel> Children { get; init; } = [];

    public HtmlString AdminLinks { get; init; } = HtmlString.Empty;

    public string EditUrl { get; init; } = string.Empty;
}

public sealed class PagerModel
{
    public int Page { get; init; } = 1;

    public int TotalPages { get; init; } = 1;

    public int TotalItems { get; init; }

    /// <summary>Post più recenti (pagina precedente), vuoto sulla prima pagina.</summary>
    public string NewerUrl { get; init; } = string.Empty;

    /// <summary>Post più vecchi (pagina successiva), vuoto sull'ultima pagina.</summary>
    public string OlderUrl { get; init; } = string.Empty;

    public IReadOnlyList<PagerLinkModel> Pages { get; init; } = [];
}

public sealed class PagerLinkModel
{
    public int Number { get; init; }

    public string Url { get; init; } = string.Empty;

    public bool IsCurrent { get; init; }

    /// <summary>Segnaposto "..." tra numeri di pagina non consecutivi.</summary>
    public bool IsEllipsis { get; init; }
}

public sealed class SearchResultModel
{
    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string AbsoluteUrl { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    /// <summary>"post", "page" o "comment".</summary>
    public string Type { get; init; } = string.Empty;

    public DateTime DateCreated { get; init; }
}

public sealed class ArchiveCategoryModel
{
    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public string FeedUrl { get; init; } = string.Empty;

    public string Anchor { get; init; } = string.Empty;

    public IReadOnlyList<ArchivePostModel> Posts { get; init; } = [];
}

public sealed class ArchivePostModel
{
    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public DateTime DateCreated { get; init; }

    public int CommentsCount { get; init; }

    public double Rating { get; init; }

    public int Raters { get; init; }
}

public sealed class ArchiveTotalsModel
{
    public int Posts { get; init; }

    public int Comments { get; init; }

    public int Raters { get; init; }

    public double AverageRating { get; init; }
}

public sealed class MonthModel
{
    public int Year { get; init; }

    public int Month { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Url { get; init; } = string.Empty;

    public int Count { get; init; }
}

public sealed class YearModel
{
    public int Year { get; init; }

    public int Count { get; init; }

    public IReadOnlyList<MonthModel> Months { get; init; } = [];
}

public sealed class CalendarModel
{
    public string Title { get; init; } = string.Empty;

    public string PreviousUrl { get; init; } = string.Empty;

    public string NextUrl { get; init; } = string.Empty;

    public IReadOnlyList<string> DayNames { get; init; } = [];

    public IReadOnlyList<IReadOnlyList<CalendarDayModel>> Weeks { get; init; } = [];
}

public sealed class CalendarDayModel
{
    public int Day { get; init; }

    public bool IsCurrentMonth { get; init; }

    public bool IsToday { get; init; }

    public bool IsWeekend { get; init; }

    public string Url { get; init; } = string.Empty;

    public IReadOnlyList<LinkModel> Posts { get; init; } = [];
}

public sealed class ContactModel
{
    public HtmlString Message { get; init; } = HtmlString.Empty;

    public HtmlString ThankMessage { get; init; } = HtmlString.Empty;

    public HtmlString ErrorMessage { get; init; } = HtmlString.Empty;

    public bool AllowAttachments { get; init; }

    /// <summary>Captcha delle estensioni attive, da inserire nel modulo.</summary>
    public HtmlString Captcha { get; init; } = HtmlString.Empty;

    public bool Sent { get; init; }

    public bool Failed { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Subject { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<string> Errors { get; init; } = [];
}

/// <summary>Un widget all'interno di una zona: disponibile nel suo template come <c>widget</c>.</summary>
public sealed class WidgetModel
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public bool ShowTitle { get; init; } = true;

    public string Zone { get; init; } = string.Empty;

    /// <summary>Impostazioni salvate dall'amministrazione (chiave → valore).</summary>
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();

    /// <summary>Dati preparati dal codice del widget (elenchi di post, categorie, ...).</summary>
    public object? Data { get; init; }
}

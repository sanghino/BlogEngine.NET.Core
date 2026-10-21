using System.Globalization;
using BlogEngine.Models;
using BlogEngine.Services;
using BlogEngine.Settings;
using BlogEngine.Web.Api.Models;

namespace BlogEngine.Web.Api;

/// <summary>
/// Conversione fra entità e oggetti dell'amministrazione (porting di Data.Services.Json). Le date sono
/// mostrate nell'ora locale del blog nel formato "yyyy-MM-dd HH:mm" e riconvertite in UTC al salvataggio.
/// </summary>
public sealed class AdminMapper(
    ILinkGenerator links,
    IBlogClock clock,
    IPostService posts,
    IPageService pages,
    ICategoryService categories,
    ISettingsService settings,
    IAvatarService avatars)
{
    public const string DateFormat = "yyyy-MM-dd HH:mm";

    /// <summary>Formato delle date nel cestino (MM/dd/yyyy HH:mm), come nella versione originale.</summary>
    public const string TrashDateFormat = "MM/dd/yyyy HH:mm";

    public async Task<string> FormatDateAsync(DateTime utc, string format = DateFormat) =>
        (await clock.ToBlogTimeAsync(utc).ConfigureAwait(false)).ToString(format, CultureInfo.InvariantCulture);

    /// <summary>Data inserita nell'amministrazione (ora del blog) convertita in UTC; se mancante o non valida, ora.</summary>
    public async Task<DateTime> ParseDateAsync(string? value)
    {
        if (DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            || DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
        {
            return await clock.ToUtcAsync(local).ConfigureAwait(false);
        }

        return clock.UtcNow;
    }

    public async Task<PostItem> ToItemAsync(Post post, BlogSettings blogSettings) => new()
    {
        Id = post.Id,
        Author = post.Author,
        Title = post.Title,
        Slug = post.Slug,
        RelativeLink = await links.PostAsync(post).ConfigureAwait(false),
        DateCreated = await FormatDateAsync(post.DateCreated).ConfigureAwait(false),
        Categories = await CategoriesAsync(post).ConfigureAwait(false),
        Tags = Tags(post),
        Comments = CommentCounts(post, blogSettings),
        IsPublished = post.IsPublished,
    };

    public async Task<PostDetail> ToDetailAsync(Post post)
    {
        var blogSettings = await settings.GetAsync(post.BlogId).ConfigureAwait(false);
        return new PostDetail
        {
            Id = post.Id,
            Author = post.Author,
            Title = post.Title,
            Slug = post.Slug,
            Description = post.Description,
            Content = post.Content,
            RelativeLink = await links.PostAsync(post).ConfigureAwait(false),
            DateCreated = await FormatDateAsync(post.DateCreated).ConfigureAwait(false),
            Categories = await CategoriesAsync(post).ConfigureAwait(false),
            Tags = Tags(post),
            Comments = CommentCounts(post, blogSettings),
            HasCommentsEnabled = post.HasCommentsEnabled,
            IsPublished = post.IsPublished,
            IsDeleted = post.IsDeleted,
            CanUserEdit = await posts.CanEditAsync(post).ConfigureAwait(false),
            CanUserDelete = await posts.CanDeleteAsync(post).ConfigureAwait(false),
        };
    }

    public async Task<PageItem> ToItemAsync(Page page, IReadOnlyList<Page> allPages) => new()
    {
        Id = page.Id,
        ShowInList = page.ShowInList,
        Title = page.Title,
        Slug = page.Slug,
        Parent = ParentOption(page, allPages),
        Keywords = page.Keywords,
        DateCreated = await FormatDateAsync(page.DateCreated).ConfigureAwait(false),
        HasChildren = allPages.Any(p => p.Parent == page.Id),
        IsPublished = page.IsPublished,
        IsFrontPage = page.IsFrontPage,
        SortOrder = page.SortOrder,
    };

    public async Task<PageDetail> ToDetailAsync(Page page)
    {
        var allPages = await pages.GetPagesAsync().ConfigureAwait(false);
        var canEdit = await pages.CanEditAsync().ConfigureAwait(false);
        return new PageDetail
        {
            Id = page.Id,
            ShowInList = page.ShowInList,
            Title = page.Title,
            Slug = page.Slug,
            RelativeLink = await links.PageAsync(page).ConfigureAwait(false),
            Content = page.Content,
            Parent = ParentOption(page, allPages),
            Description = page.Description,
            Keywords = page.Keywords,
            DateCreated = await FormatDateAsync(page.DateCreated).ConfigureAwait(false),
            HasChildren = allPages.Any(p => p.Parent == page.Id),
            IsPublished = page.IsPublished,
            IsFrontPage = page.IsFrontPage,
            IsDeleted = page.IsDeleted,
            SortOrder = page.SortOrder,
            CanUserEdit = canEdit,
            CanUserDelete = canEdit,
        };
    }

    public async Task<CommentItem> ToItemAsync(Post post, Comment comment) => new()
    {
        Id = comment.Id,
        IsApproved = comment.IsApproved,
        IsSpam = comment.IsSpam,
        IsPending = !comment.IsApproved && !comment.IsSpam,
        Email = comment.Email.Equals(Comment.TrackbackEmail, StringComparison.OrdinalIgnoreCase) ? Comment.PingbackEmail : comment.Email,
        Author = comment.Author,
        Title = Shorten(comment.Teaser),
        DateCreated = await FormatDateAsync(comment.DateCreated).ConfigureAwait(false),
        RelativeLink = await links.CommentAsync(post, comment).ConfigureAwait(false),
        HasChildren = post.Comments.Any(c => c.ParentId == comment.Id),
        Avatar = await avatars.GetUrlAsync(comment.Email).ConfigureAwait(false),
    };

    public static CommentDetail ToDetail(Comment comment) => new()
    {
        Id = comment.Id,
        ParentId = comment.ParentId,
        PostId = comment.PostId,
        Title = Shorten(comment.Teaser),
        Content = comment.Content,
        Website = comment.Website?.ToString() ?? string.Empty,
        Ip = comment.IP,
    };

    public async Task<CategoryItem> ToItemAsync(Category category, int count = 0) => new()
    {
        Id = category.Id,
        Title = category.Title,
        Description = category.Description,
        Parent = await CategoryOptionAsync(category.Parent).ConfigureAwait(false),
        Count = count,
    };

    public async Task<SelectOption?> CategoryOptionAsync(Guid? id)
    {
        if (id is not { } categoryId || categoryId == Guid.Empty || await categories.GetByIdAsync(categoryId).ConfigureAwait(false) is not { } parent)
        {
            return null;
        }

        return new SelectOption { OptionName = parent.Title, OptionValue = parent.Id.ToString() };
    }

    /// <summary>Descrizione se indicata, altrimenti i primi 100 caratteri del testo, come nella versione originale.</summary>
    public static string DescriptionOrExcerpt(string? description, string? content)
    {
        if (!string.IsNullOrWhiteSpace(description))
        {
            return description;
        }

        var text = BlogEngine.Text.HtmlText.Strip(content);
        return text.Length > 100 ? text[..100] : text;
    }

    private static string Shorten(string teaser) => teaser.Length < 80 ? teaser : teaser[..80] + "...";

    private static SelectOption? ParentOption(Page page, IReadOnlyList<Page> allPages) =>
        page.Parent != Guid.Empty && allPages.FirstOrDefault(p => p.Id == page.Parent) is { } parent
            ? new SelectOption { OptionName = parent.Title, OptionValue = parent.Id.ToString() }
            : null;

    private async Task<List<CategoryItem>?> CategoriesAsync(Post post)
    {
        if (post.Categories.Count == 0)
        {
            return null;
        }

        var result = new List<CategoryItem>();
        foreach (var id in post.Categories)
        {
            if (await categories.GetByIdAsync(id).ConfigureAwait(false) is { } category)
            {
                result.Add(await ToItemAsync(category).ConfigureAwait(false));
            }
        }

        return result.Count == 0 ? null : result;
    }

    private static List<TagItem>? Tags(Post post) =>
        post.Tags.Count == 0 ? null : post.Tags.Select(t => new TagItem { TagName = t }).ToList();

    /// <summary>[in attesa, approvati, spam] oppure null se il post non ha commenti.</summary>
    private static string[]? CommentCounts(Post post, BlogSettings blogSettings) =>
        !post.Comments.Any()
            ? null
            :
            [
                post.NotApprovedComments.Count().ToString(CultureInfo.InvariantCulture),
                post.ApprovedComments(blogSettings.EnableCommentsModeration).Count().ToString(CultureInfo.InvariantCulture),
                post.SpamComments.Count().ToString(CultureInfo.InvariantCulture),
            ];
}

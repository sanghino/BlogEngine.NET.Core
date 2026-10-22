// Oggetti scambiati in JSON con l'amministrazione AngularJS. Nomi e proprietà sono identici a quelli di
// BlogEngine.Core.Data.Models della versione .NET Framework: il JavaScript dell'amministrazione li usa così.
// Le date sono stringhe nel formato "yyyy-MM-dd HH:mm", nell'ora locale del blog.

namespace BlogEngine.Web.Api.Models;

public sealed class SelectOption
{
    public string? OptionName { get; set; }

    public string? OptionValue { get; set; }

    public string? OptionSummary { get; set; }

    public bool IsSelected { get; set; }
}

public sealed class JsonResponse
{
    public string? Data { get; set; }

    public string? Message { get; set; }

    public bool Success { get; set; }
}

// ---- Post, pagine, categorie, tag ----

public sealed class PostItem
{
    public bool IsChecked { get; set; }

    public Guid Id { get; set; }

    public string? Title { get; set; }

    public string? Author { get; set; }

    public string? DateCreated { get; set; }

    public string? Slug { get; set; }

    public string? RelativeLink { get; set; }

    public List<CategoryItem>? Categories { get; set; }

    public List<TagItem>? Tags { get; set; }

    /// <summary>Commenti in attesa, approvati, spam (null se il post non ha commenti).</summary>
    public string[]? Comments { get; set; }

    public bool IsPublished { get; set; }
}

public sealed class PostDetail
{
    public bool IsChecked { get; set; }

    public Guid Id { get; set; }

    public string? Title { get; set; }

    public string? Author { get; set; }

    public string? Description { get; set; }

    public string? Content { get; set; }

    public string? DateCreated { get; set; }

    public string? Slug { get; set; }

    public string? RelativeLink { get; set; }

    public List<CategoryItem>? Categories { get; set; }

    public List<TagItem>? Tags { get; set; }

    public string[]? Comments { get; set; }

    public bool HasCommentsEnabled { get; set; }

    public bool IsPublished { get; set; }

    public bool IsDeleted { get; set; }

    public bool CanUserDelete { get; set; }

    public bool CanUserEdit { get; set; }
}

public sealed class PageItem
{
    public bool IsChecked { get; set; }

    public Guid Id { get; set; }

    public string? Slug { get; set; }

    public string? Title { get; set; }

    public string? Keywords { get; set; }

    public bool ShowInList { get; set; }

    public bool IsPublished { get; set; }

    public bool IsFrontPage { get; set; }

    public string? DateCreated { get; set; }

    public SelectOption? Parent { get; set; }

    public bool HasChildren { get; set; }

    public int SortOrder { get; set; }
}

public sealed class PageDetail
{
    public bool IsChecked { get; set; }

    public Guid Id { get; set; }

    public string? Slug { get; set; }

    public string? RelativeLink { get; set; }

    public string? Title { get; set; }

    public string? Content { get; set; }

    public string? Description { get; set; }

    public string? Keywords { get; set; }

    public bool ShowInList { get; set; }

    public bool IsPublished { get; set; }

    public bool IsFrontPage { get; set; }

    public string? DateCreated { get; set; }

    public SelectOption? Parent { get; set; }

    public bool HasChildren { get; set; }

    public bool CanUserDelete { get; set; }

    public bool CanUserEdit { get; set; }

    public bool IsDeleted { get; set; }

    public int SortOrder { get; set; }
}

public sealed class CategoryItem
{
    public bool IsChecked { get; set; }

    public Guid Id { get; set; }

    public string? Title { get; set; }

    public SelectOption? Parent { get; set; }

    public string? Description { get; set; }

    public int Count { get; set; }
}

public sealed class TagItem
{
    public bool IsChecked { get; set; }

    public string? TagName { get; set; }

    public int TagCount { get; set; }
}

public sealed class TagToUpdate
{
    public string? OldTag { get; set; }

    public string? NewTag { get; set; }
}

// ---- Commenti ----

public sealed class CommentItem
{
    public bool IsChecked { get; set; }

    public Guid Id { get; set; }

    public bool IsPending { get; set; }

    public bool IsApproved { get; set; }

    public bool IsSpam { get; set; }

    public string? Author { get; set; }

    public string? Email { get; set; }

    public string? Avatar { get; set; }

    public bool HasChildren { get; set; }

    public string? Title { get; set; }

    public string? DateCreated { get; set; }

    public string? RelativeLink { get; set; }
}

public sealed class CommentDetail
{
    public Guid Id { get; set; }

    public Guid ParentId { get; set; }

    public Guid PostId { get; set; }

    public string? Title { get; set; }

    public string? Website { get; set; }

    public string? Ip { get; set; }

    public string? Content { get; set; }
}

public sealed class CommentsVM
{
    public List<CommentItem> Items { get; set; } = [];

    public CommentItem SelectedItem { get; set; } = new();

    public CommentDetail Detail { get; set; } = new();
}

public sealed class CommentFilterItem
{
    public bool IsChecked { get; set; }

    public Guid Id { get; set; }

    public string? Action { get; set; }

    public string? Subject { get; set; }

    public string? Operation { get; set; }

    public string? Filter { get; set; }
}

public sealed class CustomFilter
{
    public string? Name { get; set; }

    public string? FullName { get; set; }

    public bool Enabled { get; set; }

    public int Checked { get; set; }

    public int Spam { get; set; }

    public int Mistakes { get; set; }

    public string Accuracy =>
        Mistakes < 1 || Checked < 1 ? "100"
        : Mistakes >= Checked ? "0"
        : (100 - (100.0 / Checked * Mistakes)).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
}

// ---- Blog ----

/// <summary>Blog del multi-blog ("Blog" nella versione originale).</summary>
public sealed class BlogDto
{
    public bool IsChecked { get; set; }

    public Guid Id { get; set; }

    public string? Name { get; set; }

    public string? VirtualPath { get; set; }

    public string? StorageContainerName { get; set; }

    public string? Hostname { get; set; }

    public bool IsAnyTextBeforeHostnameAccepted { get; set; }

    public string? PhysicalStorageLocation { get; set; }

    public string? RelativeWebRoot { get; set; }

    public Uri? AbsoluteWebRoot { get; set; }

    public bool IsPrimary { get; set; }

    public bool IsActive { get; set; }


    public bool CanUserDelete { get; set; }

    public bool CanUserEdit { get; set; }
}

/// <summary>Dati per creare un nuovo blog.</summary>
public sealed class BlogItem
{
    public string? Name { get; set; }

    public string? UserName { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? ConfirmPassword { get; set; }
}

// ---- Utenti, ruoli, profili ----

/// <summary>Utente con profilo e ruoli ("BlogUser" nella versione originale).</summary>
public sealed class BlogUserDto
{
    public bool IsChecked { get; set; }

    public string? UserName { get; set; }

    public string? Email { get; set; }

    public string? Password { get; set; }

    public string? OldPassword { get; set; }

    public ProfileDto? Profile { get; set; }

    public List<RoleItem>? Roles { get; set; }
}

public sealed class ProfileDto
{
    public string? DisplayName { get; set; }

    public string? FirstName { get; set; }

    public string? MiddleName { get; set; }

    public string? LastName { get; set; }

    public string? EmailAddress { get; set; }

    public string? Birthday { get; set; }

    public string? PhotoUrl { get; set; }

    public bool Private { get; set; }

    public string? PhoneMobile { get; set; }

    public string? PhoneMain { get; set; }

    public string? PhoneFax { get; set; }

    public string? CityTown { get; set; }

    public string? RegionState { get; set; }

    public string? Country { get; set; }

    public string? AboutMe { get; set; }
}

public sealed class RoleItem
{
    public bool IsChecked { get; set; }

    public string? RoleName { get; set; }

    public bool IsSystemRole { get; set; }
}

/// <summary>Gruppo di permessi (categoria) nell'editor dei ruoli.</summary>
public sealed class Group
{
    public string? Title { get; set; }

    public List<Permission> Permissions { get; set; } = [];
}

public sealed class Permission
{
    public string? Id { get; set; }

    public string? Title { get; set; }

    public bool IsChecked { get; set; }
}

// ---- Varie ----

public sealed class CustomFieldItem
{
    public string? CustomType { get; set; }

    public string? ObjectId { get; set; }

    public Guid BlogId { get; set; }

    public string? Key { get; set; }

    public string? Value { get; set; }

    public string? Attribute { get; set; }

    public string ControlId => BlogEngine.Text.Slug.Create(Key);
}

public sealed class TrashItem
{
    public bool IsChecked { get; set; }

    public Guid Id { get; set; }

    public string? Title { get; set; }

    public string? RelativeUrl { get; set; }

    public string? ObjectType { get; set; }

    public string? DateCreated { get; set; }
}

public sealed class TrashVM
{
    public List<TrashItem> Items { get; set; } = [];

    public int TotalCount { get; set; }

    public int Threshold { get; set; }
}

public sealed class PingItem
{
    public bool IsChecked { get; set; }

    public string? ServiceUrl { get; set; }
}

public sealed class EditorOptions
{
    public string? OptionType { get; set; }

    public bool ShowSlug { get; set; }

    public bool ShowDescription { get; set; }

    public bool ShowCustomFields { get; set; }
}

public sealed class Lookups
{
    public IEnumerable<SelectOption> Cultures { get; set; } = [];

    public IEnumerable<SelectOption> SelfRegisterRoles { get; set; } = [];

    public IEnumerable<SelectOption> PageList { get; set; } = [];

    public IEnumerable<SelectOption> AuthorList { get; set; } = [];

    public IEnumerable<SelectOption> CategoryList { get; set; } = [];

    public IEnumerable<SelectOption> InstalledThemes { get; set; } = [];

    public EditorOptions? PostOptions { get; set; }

    public EditorOptions? PageOptions { get; set; }
}

public sealed class Stats
{
    public int PublishedPostsCount { get; set; }

    public int DraftPostsCount { get; set; }

    public int PublishedPagesCount { get; set; }

    public int DraftPagesCount { get; set; }

    public int PublishedCommentsCount { get; set; }

    public int UnapprovedCommentsCount { get; set; }

    public int SpamCommentsCount { get; set; }

    public int CategoriesCount { get; set; }

    public int TagsCount { get; set; }

    public int UsersCount { get; set; }

    public string? SubscribersCount { get; set; }
}

public sealed class DashboardVM
{
    public List<PostItem> DraftPosts { get; set; } = [];

    public int PostPublishedCnt { get; set; }

    public int PostDraftCnt { get; set; }

    public List<CommentItem> Comments { get; set; } = [];

    public int ApprovedCommentsCnt { get; set; }

    public int PendingCommentsCnt { get; set; }

    public int SpamCommentsCnt { get; set; }

    public List<PageItem> DraftPages { get; set; } = [];

    public int PagePublishedCnt { get; set; }

    public int PageDraftCnt { get; set; }

    public List<TrashItem> Trash { get; set; } = [];

    public string Logs { get; set; } = string.Empty;
}

public enum FileType
{
    Directory,
    File,
    Image,
    None,
}

public sealed class FileInstance
{
    public bool IsChecked { get; set; }

    public int SortOrder { get; set; }

    public string? Created { get; set; }

    public string? Name { get; set; }

    public string? FileSize { get; set; }

    public FileType FileType { get; set; }

    public string? FullPath { get; set; }

    /// <summary>Icona Font Awesome per i file che non sono immagini.</summary>
    public string ImgPlaceholder => FileType != FileType.File ? string.Empty : Path.GetExtension(Name ?? string.Empty).ToLowerInvariant() switch
    {
        ".zip" or ".gzip" or ".7zip" or ".rar" => "fa fa-file-archive-o",
        ".doc" or ".docx" => "fa fa-file-word-o",
        ".xls" or ".xlsx" => "fa fa-file-excel-o",
        ".pdf" => "fa fa-file-pdf-o",
        ".txt" => "fa fa-file-text-o",
        _ => "fa fa-file-o",
    };
}

/// <summary>Tema, estensione o widget installato ("Package" nella versione originale).</summary>
public sealed class Package
{
    public bool IsChecked { get; set; }

    public string? Id { get; set; }

    public string? PackageType { get; set; }

    public string? Title { get; set; }

    public string? LocalVersion { get; set; }

    public string? OnlineVersion { get; set; }

    public string? Description { get; set; }

    public int DownloadCount { get; set; }

    public string? LastUpdated { get; set; }

    public string? Website { get; set; }

    public string? PackageUrl { get; set; }

    public string? IconUrl { get; set; }

    public string? Authors { get; set; }

    public string? Tags { get; set; }

    public double Rating { get; set; }

    public bool Enabled { get; set; }

    public int Priority { get; set; }

    public string? SettingsUrl { get; set; }

    /// <summary>La galleria online non esiste più: non ci sono mai aggiornamenti da scaricare.</summary>
    public bool UpdateAvailable => false;
}

public sealed class WidgetsVM
{
    public List<WidgetItem> AvailableWidgets { get; set; } = [];

    public List<WidgetZoneItem> WidgetZones { get; set; } = [];
}

/// <summary>Zona dei widget di un tema (Id = nome della zona).</summary>
public sealed class WidgetZoneItem
{
    public string? Id { get; set; }

    public List<WidgetItem> Widgets { get; set; } = [];
}

/// <summary>Widget disponibile o inserito in una zona. I widget nuovi arrivano con un Id numerico provvisorio.</summary>
public sealed class WidgetItem
{
    public string? Id { get; set; }

    public string? Name { get; set; }

    public string? Title { get; set; }

    public bool ShowTitle { get; set; }

    /// <summary>Nome con l'errore di battitura della versione originale, letto dal JavaScript dell'amministrazione.</summary>
    public bool ShowUnistall { get; set; }
}

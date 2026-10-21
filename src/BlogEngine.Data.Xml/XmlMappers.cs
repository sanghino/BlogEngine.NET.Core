using System.Xml.Linq;
using BlogEngine.Core.Data;
using BlogEngine.Models;
using static BlogEngine.Data.Xml.XmlValues;

namespace BlogEngine.Data.Xml;

/// <summary>Formato XML di ogni entità. I nomi degli elementi fanno parte del formato dei file: non vanno cambiati.</summary>
internal static class XmlMappers
{
    // ---- Blog ----

    public static XElement ToXml(Blog blog) => new("blog",
        Element("id", blog.Id),
        Element("name", blog.Name),
        Element("isPrimary", blog.IsPrimary),
        Element("isActive", blog.IsActive),
        Element("isDeleted", blog.IsDeleted),
        Element("isSiteAggregation", blog.IsSiteAggregation),
        Element("hostname", blog.Hostname),
        Element("isAnyTextBeforeHostnameAccepted", blog.IsAnyTextBeforeHostnameAccepted),
        Element("storageContainerName", blog.StorageContainerName),
        Element("virtualPath", blog.VirtualPath),
        Element("dateCreated", blog.DateCreated),
        Element("dateModified", blog.DateModified));

    public static Blog ToBlog(XElement e) => new()
    {
        Id = e.Guid("id"),
        Name = e.String("name"),
        IsPrimary = e.Bool("isPrimary"),
        IsActive = e.Bool("isActive", true),
        IsDeleted = e.Bool("isDeleted"),
        IsSiteAggregation = e.Bool("isSiteAggregation"),
        Hostname = e.String("hostname"),
        IsAnyTextBeforeHostnameAccepted = e.Bool("isAnyTextBeforeHostnameAccepted"),
        StorageContainerName = e.String("storageContainerName"),
        VirtualPath = e.String("virtualPath"),
        DateCreated = e.Date("dateCreated"),
        DateModified = e.Date("dateModified"),
    };

    // ---- Post e commenti ----

    public static XElement ToXml(Post post) => new("post",
        Element("id", post.Id),
        Element("blogId", post.BlogId),
        Element("title", post.Title),
        Element("slug", post.Slug),
        Element("author", post.Author),
        Element("description", post.Description),
        Element("content", post.Content),
        Element("isPublished", post.IsPublished),
        Element("isDeleted", post.IsDeleted),
        Element("hasCommentsEnabled", post.HasCommentsEnabled),
        Element("raters", post.Raters),
        Element("rating", post.Rating),
        Element("dateCreated", post.DateCreated),
        Element("dateModified", post.DateModified),
        List("categories", "category", post.Categories.Select(c => c.ToString())),
        List("tags", "tag", post.Tags),
        List("notificationEmails", "email", post.NotificationEmails),
        new XElement("comments", post.AllComments.Select(ToXml)));

    public static Post ToPost(XElement e)
    {
        var post = new Post
        {
            Id = e.Guid("id"),
            BlogId = e.Guid("blogId"),
            Title = e.String("title"),
            Slug = e.String("slug"),
            Author = e.String("author"),
            Description = e.String("description"),
            Content = e.String("content"),
            IsPublished = e.Bool("isPublished", true),
            IsDeleted = e.Bool("isDeleted"),
            HasCommentsEnabled = e.Bool("hasCommentsEnabled", true),
            Raters = e.Int("raters"),
            Rating = e.Float("rating"),
            DateCreated = e.Date("dateCreated"),
            DateModified = e.Date("dateModified"),
            Categories = e.Strings("categories", "category").Select(s => Guid.TryParse(s, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList(),
            Tags = e.Strings("tags", "tag"),
            NotificationEmails = e.Strings("notificationEmails", "email"),
        };

        post.AllComments = e.Element("comments")?.Elements("comment").Select(c => ToComment(c, post)).ToList() ?? [];
        return post;
    }

    private static XElement ToXml(Comment comment) => new("comment",
        Element("id", comment.Id),
        Element("parentId", comment.ParentId),
        Element("author", comment.Author),
        Element("email", comment.Email),
        Element("website", comment.Website),
        Element("country", comment.Country),
        Element("ip", comment.IP),
        Element("avatar", comment.Avatar),
        Element("content", comment.Content),
        Element("dateCreated", comment.DateCreated),
        Element("isApproved", comment.IsApproved),
        Element("isSpam", comment.IsSpam),
        Element("isDeleted", comment.IsDeleted),
        Element("moderatedBy", comment.ModeratedBy));

    private static Comment ToComment(XElement e, Post post) => new()
    {
        Id = e.Guid("id"),
        PostId = post.Id,
        BlogId = post.BlogId,
        ParentId = e.Guid("parentId"),
        Author = e.String("author"),
        Email = e.String("email"),
        Website = e.Uri("website"),
        Country = e.String("country"),
        IP = e.String("ip"),
        Avatar = e.String("avatar"),
        Content = e.String("content"),
        DateCreated = e.Date("dateCreated"),
        IsApproved = e.Bool("isApproved"),
        IsSpam = e.Bool("isSpam"),
        IsDeleted = e.Bool("isDeleted"),
        ModeratedBy = e.String("moderatedBy"),
    };

    // ---- Pagine e categorie ----

    public static XElement ToXml(Page page) => new("page",
        Element("id", page.Id),
        Element("blogId", page.BlogId),
        Element("title", page.Title),
        Element("slug", page.Slug),
        Element("description", page.Description),
        Element("keywords", page.Keywords),
        Element("content", page.Content),
        Element("parent", page.Parent),
        Element("isPublished", page.IsPublished),
        Element("isDeleted", page.IsDeleted),
        Element("isFrontPage", page.IsFrontPage),
        Element("showInList", page.ShowInList),
        Element("sortOrder", page.SortOrder),
        Element("dateCreated", page.DateCreated),
        Element("dateModified", page.DateModified));

    public static Page ToPage(XElement e) => new()
    {
        Id = e.Guid("id"),
        BlogId = e.Guid("blogId"),
        Title = e.String("title"),
        Slug = e.String("slug"),
        Description = e.String("description"),
        Keywords = e.String("keywords"),
        Content = e.String("content"),
        Parent = e.Guid("parent"),
        IsPublished = e.Bool("isPublished"),
        IsDeleted = e.Bool("isDeleted"),
        IsFrontPage = e.Bool("isFrontPage"),
        ShowInList = e.Bool("showInList"),
        SortOrder = e.Int("sortOrder"),
        DateCreated = e.Date("dateCreated"),
        DateModified = e.Date("dateModified"),
    };

    public static XElement ToXml(Category category) => new("category",
        Element("id", category.Id),
        Element("blogId", category.BlogId),
        Element("title", category.Title),
        Element("description", category.Description),
        Element("parent", category.Parent),
        Element("dateCreated", category.DateCreated),
        Element("dateModified", category.DateModified));

    public static Category ToCategory(XElement e) => new()
    {
        Id = e.Guid("id"),
        BlogId = e.Guid("blogId"),
        Title = e.String("title"),
        Description = e.String("description"),
        Parent = e.NullableGuid("parent"),
        DateCreated = e.Date("dateCreated"),
        DateModified = e.Date("dateModified"),
    };

    // ---- Blogroll e referrer ----

    public static XElement ToXml(BlogRollItem item) => new("item",
        Element("id", item.Id),
        Element("blogId", item.BlogId),
        Element("title", item.Title),
        Element("description", item.Description),
        Element("blogUrl", item.BlogUrl),
        Element("feedUrl", item.FeedUrl),
        Element("xfn", item.Xfn),
        Element("sortIndex", item.SortIndex));

    public static BlogRollItem ToBlogRollItem(XElement e) => new()
    {
        Id = e.Guid("id"),
        BlogId = e.Guid("blogId"),
        Title = e.String("title"),
        Description = e.String("description"),
        BlogUrl = e.Uri("blogUrl"),
        FeedUrl = e.Uri("feedUrl"),
        Xfn = e.String("xfn"),
        SortIndex = e.Int("sortIndex"),
    };

    public static XElement ToXml(Referrer referrer) => new("referrer",
        Element("id", referrer.Id),
        Element("blogId", referrer.BlogId),
        Element("day", referrer.Day),
        Element("url", referrer.Url),
        Element("referrerUrl", referrer.ReferrerUrl),
        Element("count", referrer.Count),
        Element("possibleSpam", referrer.PossibleSpam));

    public static Referrer ToReferrer(XElement e) => new()
    {
        Id = e.Guid("id"),
        BlogId = e.Guid("blogId"),
        Day = e.Date("day"),
        Url = e.Uri("url"),
        ReferrerUrl = e.Uri("referrerUrl"),
        Count = e.Int("count"),
        PossibleSpam = e.Bool("possibleSpam"),
    };

    // ---- Profili, campi personalizzati, utenti e ruoli ----

    public static XElement ToXml(AuthorProfile p) => new("profile",
        Element("userName", p.UserName),
        Element("blogId", p.BlogId),
        Element("displayName", p.DisplayName),
        Element("firstName", p.FirstName),
        Element("middleName", p.MiddleName),
        Element("lastName", p.LastName),
        Element("emailAddress", p.EmailAddress),
        Element("birthday", p.Birthday),
        Element("cityTown", p.CityTown),
        Element("regionState", p.RegionState),
        Element("country", p.Country),
        Element("company", p.Company),
        Element("phoneMain", p.PhoneMain),
        Element("phoneMobile", p.PhoneMobile),
        Element("phoneFax", p.PhoneFax),
        Element("photoUrl", p.PhotoUrl),
        Element("aboutMe", p.AboutMe),
        Element("private", p.Private));

    public static AuthorProfile ToProfile(XElement e) => new()
    {
        UserName = e.String("userName"),
        BlogId = e.Guid("blogId"),
        DisplayName = e.String("displayName"),
        FirstName = e.String("firstName"),
        MiddleName = e.String("middleName"),
        LastName = e.String("lastName"),
        EmailAddress = e.String("emailAddress"),
        Birthday = e.NullableDate("birthday"),
        CityTown = e.String("cityTown"),
        RegionState = e.String("regionState"),
        Country = e.String("country"),
        Company = e.String("company"),
        PhoneMain = e.String("phoneMain"),
        PhoneMobile = e.String("phoneMobile"),
        PhoneFax = e.String("phoneFax"),
        PhotoUrl = e.String("photoUrl"),
        AboutMe = e.String("aboutMe"),
        Private = e.Bool("private"),
    };

    public static XElement ToXml(CustomField f) => new("field",
        Element("blogId", f.BlogId),
        Element("customType", f.CustomType),
        Element("objectId", f.ObjectId),
        Element("key", f.Key),
        Element("value", f.Value),
        Element("attribute", f.Attribute));

    public static CustomField ToCustomField(XElement e) => new()
    {
        BlogId = e.Guid("blogId"),
        CustomType = e.String("customType"),
        ObjectId = e.String("objectId"),
        Key = e.String("key"),
        Value = e.String("value"),
        Attribute = e.String("attribute"),
    };

    public static XElement ToXml(BlogUser user) => new("user",
        Element("userName", user.UserName),
        Element("blogId", user.BlogId),
        Element("email", user.Email),
        Element("passwordHash", user.PasswordHash),
        Element("lastLoginTime", user.LastLoginTime));

    public static BlogUser ToUser(XElement e) => new()
    {
        UserName = e.String("userName"),
        BlogId = e.Guid("blogId"),
        Email = e.String("email"),
        PasswordHash = e.String("passwordHash"),
        LastLoginTime = e.NullableDate("lastLoginTime"),
    };

    public static XElement ToXml(BlogRole role) => new("role",
        Element("name", role.Name),
        Element("blogId", role.BlogId),
        List("users", "user", role.Users));

    public static BlogRole ToRole(XElement e) => new()
    {
        Name = e.String("name"),
        BlogId = e.Guid("blogId"),
        Users = e.Strings("users", "user"),
    };

    public static XElement ToXml(CommentFilterRule rule) => new("rule",
        Element("id", rule.Id),
        Element("action", rule.Action),
        Element("subject", rule.Subject),
        Element("operator", rule.Operator),
        Element("filter", rule.Filter));

    public static CommentFilterRule ToRule(XElement e) => new()
    {
        Id = e.Guid("id"),
        Action = e.Enum<CommentFilterAction>("action"),
        Subject = e.Enum<CommentFilterSubject>("subject"),
        Operator = e.Enum<CommentFilterOperator>("operator"),
        Filter = e.String("filter"),
    };
}

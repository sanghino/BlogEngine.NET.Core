using System.Net.Mail;
using System.Text.RegularExpressions;
using BlogEngine.Core.Caching;
using BlogEngine.Core.Data;
using BlogEngine.Core.Security;
using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

/// <summary>
/// Creazione di un nuovo blog del multi-blog con il suo amministratore, una categoria e un post di benvenuto.
/// Porting di BlogGenerator: il nuovo blog risponde all'indirizzo "/{nome}/".
/// </summary>
public sealed partial class BlogProvisioningService(
    BlogsCache blogs,
    SettingsCache settings,
    RightsCache rights,
    UserRepository users,
    EntityRepository<Category> categories,
    EntityRepository<Post> posts,
    IStringListStore lists,
    IBlogContext context,
    IRightsService rightsService,
    IBlogClock clock)
{
    /// <summary>Nomi che coincidono con percorsi del sito e non possono diventare blog.</summary>
    public static readonly IReadOnlySet<string> ReservedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "account", "admin", "api", "app_code", "app_data", "app_globalresources", "bin", "content", "custom", "editors",
        "modules", "pics", "scripts", "setup", "templates", "tests", "themes", "user controls", "widgets", "post", "page",
        "category", "author", "tag", "calendar", "archive", "contact", "search", "error", "lib", "css", "fonts",
    };

    /// <summary>
    /// Crea un blog figlio. Consentito dal blog principale a chi ha il permesso "Crea nuovi blog":
    /// gli amministratori e i ruoli a cui l'amministratore lo ha concesso.
    /// </summary>
    public async Task<Blog> CreateBlogAsync(string name, string userName, string email, string password, CancellationToken cancellationToken = default)
    {
        if (!context.Blog.IsPrimary || !await rightsService.IsAuthorizedAsync(Rights.CreateNewBlogs, cancellationToken).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException();
        }

        var blogName = name?.Trim() ?? string.Empty;
        var user = userName?.Trim() ?? string.Empty;
        var existing = await blogs.GetAllAsync(cancellationToken).ConfigureAwait(false);
        BlogValidationException.ThrowIfInvalid(
            ("Name", blogName.Length == 0, Messages.BlogNameRequired),
            ("Name", blogName.Length > 0 && !NameRegex().IsMatch(blogName), Messages.BlogNameInvalid),
            ("Name", ReservedNames.Contains(blogName), Messages.BlogNameReserved),
            ("Name", existing.Any(b => string.Equals(b.Name, blogName, StringComparison.OrdinalIgnoreCase)
                                       || string.Equals(BlogResolver.RelativeRoot(b), $"/{blogName}/", StringComparison.OrdinalIgnoreCase)),
                Messages.BlogExists),
            ("UserName", user.Length == 0 || !NameRegex().IsMatch(user), Messages.UserNameSimpleChars),
            ("Email", !MailAddress.TryCreate(email?.Trim() ?? string.Empty, out _), Messages.EmailInvalid),
            ("Password", string.IsNullOrEmpty(password) || password.Length < PasswordHasher.MinimumLength,
                Messages.Format(Messages.PasswordTooShort, PasswordHasher.MinimumLength)));

        var now = clock.UtcNow;
        var blog = new Blog
        {
            Name = blogName,
            StorageContainerName = blogName.ToLowerInvariant(),
            VirtualPath = "~/" + blogName.ToLowerInvariant(),
            IsAnyTextBeforeHostnameAccepted = true,
            IsActive = true,
            DateCreated = now,
            DateModified = now,
        };
        await blogs.SaveAsync(blog, cancellationToken).ConfigureAwait(false);

        var blogSettings = new BlogEngine.Settings.BlogSettings { Name = blogName, AuthorName = user, Email = email!.Trim() };
        await settings.SaveAsync(blog.Id, blogSettings, cancellationToken).ConfigureAwait(false);
        await rights.SaveAsync(blog.Id, RightsMatrix.CreateDefault(), cancellationToken).ConfigureAwait(false);

        await users.SaveUserAsync(new BlogUser { BlogId = blog.Id, UserName = user, Email = email.Trim(), PasswordHash = PasswordHasher.Hash(password) }, cancellationToken).ConfigureAwait(false);
        await users.SaveRoleAsync(new BlogRole { BlogId = blog.Id, Name = SystemRoles.Administrators, Users = [user] }, cancellationToken).ConfigureAwait(false);
        await users.SaveRoleAsync(new BlogRole { BlogId = blog.Id, Name = SystemRoles.Editors }, cancellationToken).ConfigureAwait(false);
        await users.SaveRoleAsync(new BlogRole { BlogId = blog.Id, Name = SystemRoles.Registered }, cancellationToken).ConfigureAwait(false);

        await SeedContentAsync(blog, user, cancellationToken: cancellationToken).ConfigureAwait(false);
        return blog;
    }

    /// <summary>
    /// Contenuti iniziali di un blog appena creato: servizi di ping, parole escluse dalla ricerca, la categoria
    /// "General" e un post di benvenuto. Usato anche dalla configurazione iniziale per il blog principale.
    /// </summary>
    public async Task SeedContentAsync(Blog blog, string userName, string? blogTitle = null, CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;
        await lists.SaveAsync(blog.Id, StringListKind.PingServices, DefaultContent.PingServices, cancellationToken).ConfigureAwait(false);
        await lists.SaveAsync(blog.Id, StringListKind.StopWords, DefaultContent.StopWords, cancellationToken).ConfigureAwait(false);

        var category = new Category { BlogId = blog.Id, Title = DefaultContent.DefaultCategoryTitle, Description = DefaultContent.DefaultCategoryDescription, DateCreated = now, DateModified = now };
        await categories.SaveAsync(category, cancellationToken).ConfigureAwait(false);

        var title = $"Welcome to {blogTitle ?? blog.Name}'s blog";
        await posts.SaveAsync(new Post
        {
            BlogId = blog.Id,
            Title = title,
            Slug = BlogEngine.Text.Slug.Create(title),
            Author = userName,
            Content = $"<p>Dear {System.Net.WebUtility.HtmlEncode(userName)}, welcome to your new blog!</p>"
                + $"<p>Log in <a href=\"{BlogResolver.RelativeRoot(blog)}account/login\">here</a> to start using it.</p><p>Happy blogging!</p>",
            Categories = [category.Id],
            DateCreated = now,
            DateModified = now,
        }, cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex("^[a-z0-9_-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex NameRegex();
}

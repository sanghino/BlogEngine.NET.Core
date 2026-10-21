using BlogEngine.Core.Configuration;
using BlogEngine.Core.Services;
using BlogEngine.Security;
using BlogEngine.Services;
using Microsoft.Extensions.Options;

namespace BlogEngine.Web.Admin;

/// <summary>
/// Dati comuni alle pagine dell'amministrazione (utente, permessi, blog, opzioni), calcolati una volta per richiesta.
/// </summary>
public sealed class AdminViewContext(
    IBlogContext context,
    IRightsService rights,
    IProfileService profiles,
    ISettingsService settings,
    IOptions<BlogEngineOptions> options,
    ILinkGenerator links)
{
    public const string Version = "BlogEngine.NET 4.0 (.NET 8)";

    private bool _loaded;

    public string UserName => context.UserName ?? string.Empty;

    public string DisplayName { get; private set; } = "Anonymous";

    public string AvatarUrl { get; private set; } = "/" + AvatarService.NoAvatarPath;

    public IReadOnlyList<Rights> UserRights { get; private set; } = [];

    public bool IsAdministrator => rights.IsAdministrator;

    public bool IsPrimaryBlog => context.Blog.IsPrimary;

    public string BlogRoot => links.BlogRoot;

    public string BlogName { get; private set; } = string.Empty;

    public string ActiveTheme { get; private set; } = string.Empty;

    public CommentProviderInfo CommentProvider { get; private set; }

    public BlogEngineOptions Options => options.Value;

    public bool Can(Rights right) => UserRights.Contains(right);

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_loaded)
        {
            return;
        }

        var matrix = await rights.GetRoleMatrixAsync(cancellationToken);
        UserRights = matrix.Where(p => p.Value.Overlaps(context.Roles)).Select(p => p.Key).OrderBy(r => r).ToList();

        var blogSettings = await settings.GetAsync(cancellationToken: cancellationToken);
        BlogName = blogSettings.Name;
        ActiveTheme = blogSettings.Theme;
        CommentProvider = new CommentProviderInfo(blogSettings.CommentProvider == BlogEngine.Settings.CommentProvider.BlogEngine);

        if (context.UserName is { } user && await profiles.GetAsync(user, cancellationToken) is { } profile)
        {
            DisplayName = string.IsNullOrWhiteSpace(profile.DisplayName) ? user : profile.DisplayName;
            if (!string.IsNullOrWhiteSpace(profile.PhotoUrl))
            {
                AvatarUrl = $"{links.BlogRoot}image.axd?picture=/avatars/{Uri.EscapeDataString(profile.PhotoUrl.Replace("\"", string.Empty))}";
            }
        }
        else if (context.UserName is { } name)
        {
            DisplayName = name;
        }

        _loaded = true;
    }

    public readonly record struct CommentProviderInfo(bool IsBlogEngine);
}

using System.Security.Cryptography;
using System.Text;
using BlogEngine.Models;
using BlogEngine.Services;

namespace BlogEngine.Core.Services;

/// <summary>Porting di Data.Services.Avatar.</summary>
public sealed class AvatarService(IProfileService profiles, ISettingsService settings, ILinkGenerator links) : IAvatarService
{
    public const string NoAvatarPath = "Content/images/blog/noavatar.jpg";
    public const string PingbackPath = "Content/images/blog/pingback.png";

    /// <summary>Stili di Gravatar accettati nell'impostazione Avatar; "none" disattiva Gravatar.</summary>
    private static readonly HashSet<string> GravatarStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "identicon", "wavatar", "retro", "mm", "blank", "monsterid",
    };

    public async Task<string> GetUrlAsync(string? email, CancellationToken cancellationToken = default)
    {
        var address = email?.Trim() ?? string.Empty;
        if (address.Equals(Comment.PingbackEmail, StringComparison.OrdinalIgnoreCase) || address.Equals(Comment.TrackbackEmail, StringComparison.OrdinalIgnoreCase))
        {
            return "/" + PingbackPath;
        }

        if (address.Length > 0 && await profiles.GetByEmailAsync(address, cancellationToken).ConfigureAwait(false) is { PhotoUrl.Length: > 0 } profile)
        {
            var photo = profile.PhotoUrl.Replace("\"", string.Empty);
            return photo.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || photo.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? photo
                : $"{links.BlogRoot}image.axd?picture=/avatars/{Uri.EscapeDataString(photo)}";
        }

        var style = (await settings.GetAsync(cancellationToken: cancellationToken).ConfigureAwait(false)).Avatar;
        if (address.Length > 0 && GravatarStyles.Contains(style))
        {
            var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(address.ToLowerInvariant()))).ToLowerInvariant();
            return $"https://www.gravatar.com/avatar/{hash}.jpg?d={style.ToLowerInvariant()}";
        }

        return "/" + NoAvatarPath;
    }
}

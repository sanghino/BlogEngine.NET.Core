using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BlogEngine.Core.Data;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace BlogEngine.Web.Security;

public static class BlogClaims
{
    /// <summary>Blog in cui l'utente si è autenticato: gli utenti sono distinti per blog.</summary>
    public const string BlogId = "blogengine:blog";
}

public static class AccountSignIn
{
    public static readonly TimeSpan PersistentLifetime = TimeSpan.FromDays(30);

    /// <summary>
    /// Emette il cookie di autenticazione. Con "ricordami" il cookie dura 30 giorni, altrimenti
    /// scade alla chiusura del browser, come nella versione originale.
    /// </summary>
    public static Task SignInAsync(HttpContext http, BlogUser user, bool persistent)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, user.UserName), new Claim(BlogClaims.BlogId, user.BlogId.ToString())],
            CookieAuthenticationDefaults.AuthenticationScheme);

        return http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = persistent, AllowRefresh = true });
    }

    public static Task SignOutAsync(HttpContext http) => http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
}

/// <summary>
/// Token del link di reimpostazione della password: firmato e cifrato con Data Protection, valido 2 ore,
/// legato al blog e all'hash della password attuale, quindi inutilizzabile dopo il cambio della password.
/// </summary>
public sealed class PasswordResetTokens(IDataProtectionProvider protection, TimeProvider time)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(2);

    private readonly ITimeLimitedDataProtector _protector = protection.CreateProtector("BlogEngine.PasswordReset").ToTimeLimitedDataProtector();

    public string Create(BlogUser user) =>
        _protector.Protect($"{user.BlogId:N}|{user.UserName}|{Stamp(user)}", time.GetUtcNow().Add(Lifetime));

    /// <summary>Nome utente del token, se valido per il blog e per la password attuale dell'utente.</summary>
    public string? GetUserName(string? token, Guid blogId)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        try
        {
            var parts = _protector.Unprotect(token, out var expiration).Split('|');
            return parts.Length == 3 && expiration > time.GetUtcNow() && parts[0] == blogId.ToString("N") ? parts[1] : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    public bool IsCurrent(string? token, BlogUser user)
    {
        try
        {
            return token is not null && _protector.Unprotect(token).Split('|') is [_, _, var stamp] && stamp == Stamp(user);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string Stamp(BlogUser user) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.PasswordHash)))[..16];
}

using System.Security.Claims;
using BlogEngine.Core.Services;
using BlogEngine.Security;
using BlogEngine.Web.Security;

namespace BlogEngine.Web.Hosting;

/// <summary>
/// Imposta utente e ruoli nel contesto della richiesta. I ruoli sono riletti a ogni richiesta (dalla cache),
/// quindi una modifica ha effetto subito; un cookie emesso da un altro blog vale come visitatore anonimo.
/// </summary>
public sealed class UserContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, RequestBlogContext context)
    {
        context.UserName = null;
        context.Roles = [SystemRoles.Anonymous];

        // Il servizio utenti si risolve solo quando serve: prima dell'installazione non va creato
        // nessun servizio dei dati (vedi BlogContextMiddleware).
        if (context.HasBlog
            && http.User.Identity?.IsAuthenticated == true
            && http.User.FindFirstValue(BlogClaims.BlogId) == context.Blog.Id.ToString()
            && http.User.Identity.Name is { Length: > 0 } name
            && http.RequestServices.GetRequiredService<IUserService>() is var users
            && await users.GetUserAsync(name, http.RequestAborted) is { } user)
        {
            context.UserName = user.UserName;

            // Un utente autenticato ha sempre almeno i permessi di un visitatore.
            var roles = await users.GetRolesForUserAsync(user.UserName, http.RequestAborted);
            context.Roles = roles.Append(SystemRoles.Anonymous).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        await next(http);
    }
}

using BlogEngine.Models;
using BlogEngine.Security;
using BlogEngine.Services;

namespace BlogEngine.Web.Hosting;

/// <summary>
/// Contesto della richiesta HTTP corrente (scoped). Blog e indirizzo base vengono impostati da
/// <see cref="BlogContextMiddleware"/>, utente e ruoli da <see cref="UserContextMiddleware"/>.
/// Per le operazioni di sistema (configurazione iniziale, attività in background) si imposta a mano.
/// </summary>
public sealed class RequestBlogContext : IBlogContext
{
    private Blog? _blog;

    public Blog Blog
    {
        get => _blog ?? throw new InvalidOperationException("Il blog della richiesta non è ancora stato determinato.");
        set => _blog = value;
    }

    public bool HasBlog => _blog is not null;

    public string? UserName { get; set; }

    public IReadOnlyCollection<string> Roles { get; set; } = [SystemRoles.Anonymous];

    public Uri? RequestBaseUri { get; set; }

    /// <summary>Opera come amministratore senza un utente reale (configurazione iniziale e attività di sistema).</summary>
    public void RunAsSystem(Blog blog)
    {
        Blog = blog;
        UserName = null;
        Roles = [SystemRoles.Administrators];
    }
}

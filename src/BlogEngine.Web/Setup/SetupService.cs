using BlogEngine.Core;
using BlogEngine.Core.Configuration;
using BlogEngine.Core.Data;
using BlogEngine.Core.Services;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Hosting;

namespace BlogEngine.Web.Setup;

public sealed class SetupRequest
{
    public string SetupKey { get; set; } = string.Empty;

    public DataProviderKind DataProvider { get; set; }

    public string BlogName { get; set; } = string.Empty;

    public string UserName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string ConfirmPassword { get; set; } = string.Empty;
}

public enum SetupError
{
    None,
    AlreadyInstalled,
    InvalidKey,
    PasswordsDoNotMatch,
    ProviderLocked,
}

/// <summary>
/// Configurazione iniziale: inizializza il provider scelto, crea il blog principale con le impostazioni
/// di base, i permessi predefiniti e l'account amministratore, poi registra l'installazione.
/// </summary>
public sealed class SetupService(InstallationState installation, IServiceScopeFactory scopes, TimeProvider time)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Esegue la configurazione; in caso di dati non validi lancia <see cref="BlogValidationException"/>.</summary>
    public async Task<(SetupError Error, BlogUser? Administrator)> InstallAsync(SetupRequest request, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            if (installation.IsInstalled)
            {
                return (SetupError.AlreadyInstalled, null);
            }

            if (!installation.VerifySetupKey(request.SetupKey))
            {
                return (SetupError.InvalidKey, null);
            }

            if (request.Password != request.ConfirmPassword)
            {
                return (SetupError.PasswordsDoNotMatch, null);
            }

            BlogValidationException.ThrowIfInvalid(("BlogName", string.IsNullOrWhiteSpace(request.BlogName), "Il nome del blog è obbligatorio."));

            if (!installation.TrySelectProvider(request.DataProvider))
            {
                return (SetupError.ProviderLocked, null);
            }

            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            await services.GetRequiredService<IDataStoreInitializer>().InitializeAsync(cancellationToken);

            // Il blog principale viene creato al primo accesso all'elenco dei blog.
            var primary = (await services.GetRequiredService<BlogsCache>().GetAllAsync(cancellationToken)).First(b => b.IsPrimary);
            services.GetRequiredService<RequestBlogContext>().RunAsSystem(primary);

            // L'account viene creato per primo: se i dati non sono validi non resta nulla di salvato a metà.
            var users = services.GetRequiredService<IUserService>();
            // Se un tentativo precedente si è interrotto dopo aver creato l'account, vale la password appena inserita.
            var administrator = await users.GetUserAsync(request.UserName, cancellationToken);
            if (administrator is null)
            {
                administrator = await users.CreateUserAsync(request.UserName, request.Email, request.Password, [SystemRoles.Administrators], cancellationToken);
            }
            else
            {
                await users.SetPasswordAsync(administrator.UserName, request.Password, bypassRights: true, cancellationToken);
            }

            await users.SetUserRolesAsync(administrator.UserName, [SystemRoles.Administrators], bypassRights: true, cancellationToken);

            var settingsService = services.GetRequiredService<ISettingsService>();
            var settings = await settingsService.GetAsync(cancellationToken: cancellationToken);
            settings.Name = request.BlogName.Trim();
            settings.AuthorName = request.UserName.Trim();
            settings.Email = request.Email.Trim();
            settings.SmtpUserName = string.Empty;
            await settingsService.SaveAsync(settings, cancellationToken: cancellationToken);

            var rights = services.GetRequiredService<RightsCache>();
            await rights.SaveAsync(primary.Id, RightsMatrix.CreateDefault(), cancellationToken);

            // Solo alla prima installazione riuscita: un nuovo tentativo non duplica il post di benvenuto.
            if ((await services.GetRequiredService<IPostService>().GetPostsAsync(cancellationToken)).Count == 0)
            {
                await services.GetRequiredService<BlogProvisioningService>().SeedContentAsync(primary, administrator.UserName, request.BlogName.Trim(), cancellationToken);
            }

            installation.MarkInstalled(time.GetUtcNow().UtcDateTime);
            return (SetupError.None, administrator);
        }
        finally
        {
            Gate.Release();
        }
    }
}

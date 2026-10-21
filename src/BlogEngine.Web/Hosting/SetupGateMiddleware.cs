namespace BlogEngine.Web.Hosting;

/// <summary>
/// Finché il sito non è configurato, ogni richiesta (tranne i file statici, serviti prima) porta alla
/// configurazione iniziale. A installazione completata la configurazione non è più raggiungibile.
/// </summary>
public sealed class SetupGateMiddleware(RequestDelegate next)
{
    public const string SetupPath = "/setup";

    public Task InvokeAsync(HttpContext http, InstallationState installation)
    {
        var isSetup = http.Request.Path.StartsWithSegments(SetupPath, StringComparison.OrdinalIgnoreCase);
        if (!installation.IsInstalled && !isSetup)
        {
            http.Response.Redirect(SetupPath);
            return Task.CompletedTask;
        }

        if (installation.IsInstalled && isSetup)
        {
            http.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        return next(http);
    }
}

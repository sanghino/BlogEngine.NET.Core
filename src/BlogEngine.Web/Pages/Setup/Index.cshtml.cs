using BlogEngine.Core;
using BlogEngine.Core.Security;
using BlogEngine.Web.Hosting;
using BlogEngine.Web.Security;
using BlogEngine.Web.Setup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace BlogEngine.Web.Pages.Setup;

[EnableRateLimiting(WebServiceCollectionExtensions.AccountRateLimit)]
public sealed class SetupModel(SetupService setup, InstallationState installation, IStringLocalizer<Labels> labels) : PageModel
{
    [BindProperty]
    public SetupRequest Input { get; set; } = new();

    public int MinimumPasswordLength => PasswordHasher.MinimumLength;

    /// <summary>Percorso del file con la chiave, relativo alla radice dello spazio web.</summary>
    public string SetupKeyFile => $"{new DirectoryInfo(Path.GetDirectoryName(installation.SetupKeyPath)!).Name}/{InstallationState.SetupKeyFileName}";

    public void OnGet() => Input.DataProvider = installation.DefaultProvider;

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var (error, administrator) = await setup.InstallAsync(Input, HttpContext.RequestAborted);
            switch (error)
            {
                case SetupError.None:
                    await AccountSignIn.SignInAsync(HttpContext, administrator!, persistent: false);
                    return LocalRedirect("~/");
                case SetupError.AlreadyInstalled:
                    return LocalRedirect("~/");
                case SetupError.InvalidKey:
                    ModelState.AddModelError(nameof(Input.SetupKey), labels["setupKeyInvalid"]);
                    break;
                case SetupError.PasswordsDoNotMatch:
                    ModelState.AddModelError(nameof(Input.ConfirmPassword), labels["passwordsDoNotMatch"]);
                    break;
                case SetupError.ProviderLocked:
                    ModelState.AddModelError(nameof(Input.DataProvider), labels["providerLocked"]);
                    break;
            }
        }
        catch (BlogValidationException ex)
        {
            foreach (var (field, message) in ex.Errors)
            {
                ModelState.AddModelError($"Input.{field}", message);
            }
        }

        return Page();
    }
}

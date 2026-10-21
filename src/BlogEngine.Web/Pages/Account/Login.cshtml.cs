using BlogEngine.Core.Services;
using BlogEngine.Services;
using BlogEngine.Web.Hosting;
using BlogEngine.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace BlogEngine.Web.Pages.Account;

[EnableRateLimiting(WebServiceCollectionExtensions.AccountRateLimit)]
public sealed class LoginModel(IUserService users, ISettingsService settings, IStringLocalizer<Labels> labels) : PageModel
{
    [BindProperty]
    public string UserName { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public bool RememberMe { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public bool ShowPasswordReset { get; private set; }

    public bool ShowRegistration { get; private set; }

    public async Task OnGetAsync()
    {
        // Come nella versione originale, aprire la pagina di login chiude la sessione corrente.
        if (User.Identity?.IsAuthenticated == true)
        {
            await AccountSignIn.SignOutAsync(HttpContext);
        }

        await LoadOptionsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = string.IsNullOrWhiteSpace(UserName) || string.IsNullOrEmpty(Password)
            ? null
            : await users.ValidateCredentialsAsync(UserName, Password, HttpContext.RequestAborted);

        if (user is null)
        {
            ModelState.AddModelError(string.Empty, labels["loginFailed"]);
            await LoadOptionsAsync();
            return Page();
        }

        await AccountSignIn.SignInAsync(HttpContext, user, RememberMe);

        // Solo indirizzi locali: un parametro returnUrl verso un altro sito viene ignorato.
        return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl! : "~/");
    }

    private async Task LoadOptionsAsync()
    {
        var blogSettings = await settings.GetAsync(cancellationToken: HttpContext.RequestAborted);
        ShowPasswordReset = blogSettings.EnablePasswordReset;
        ShowRegistration = blogSettings.EnableSelfRegistration;
    }
}

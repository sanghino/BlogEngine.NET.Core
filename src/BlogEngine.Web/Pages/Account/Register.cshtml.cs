using BlogEngine.Core;
using BlogEngine.Core.Security;
using BlogEngine.Core.Services;
using BlogEngine.Services;
using BlogEngine.Web.Hosting;
using BlogEngine.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace BlogEngine.Web.Pages.Account;

/// <summary>Registrazione autonoma, se abilitata nelle impostazioni (EnableSelfRegistration).</summary>
[EnableRateLimiting(WebServiceCollectionExtensions.AccountRateLimit)]
public sealed class RegisterModel(IUserService users, ISettingsService settings, IStringLocalizer<Labels> labels) : PageModel
{
    [BindProperty]
    public string UserName { get; set; } = string.Empty;

    [BindProperty]
    public string Email { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool Enabled { get; private set; }

    public int MinimumPasswordLength => PasswordHasher.MinimumLength;

    public async Task OnGetAsync() =>
        Enabled = (await settings.GetAsync(cancellationToken: HttpContext.RequestAborted)).EnableSelfRegistration;

    public async Task<IActionResult> OnPostAsync()
    {
        Enabled = (await settings.GetAsync(cancellationToken: HttpContext.RequestAborted)).EnableSelfRegistration;
        if (!Enabled)
        {
            return Page();
        }

        if (Password != ConfirmPassword)
        {
            ModelState.AddModelError(nameof(ConfirmPassword), labels["passwordsDoNotMatch"]);
            return Page();
        }

        try
        {
            var user = await users.RegisterAsync(UserName, Email, Password, HttpContext.RequestAborted);
            await AccountSignIn.SignInAsync(HttpContext, user, persistent: false);
            return LocalRedirect("~/");
        }
        catch (BlogValidationException ex)
        {
            foreach (var (field, message) in ex.Errors)
            {
                ModelState.AddModelError(field, message);
            }

            return Page();
        }
    }
}

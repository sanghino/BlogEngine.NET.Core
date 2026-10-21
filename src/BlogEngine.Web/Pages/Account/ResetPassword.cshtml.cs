using BlogEngine.Core;
using BlogEngine.Core.Data;
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

[EnableRateLimiting(WebServiceCollectionExtensions.AccountRateLimit)]
public sealed class ResetPasswordModel(
    IUserService users,
    IBlogContext context,
    PasswordResetTokens tokens,
    IStringLocalizer<Labels> labels) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool TokenIsValid { get; private set; }

    public bool Completed { get; private set; }

    public int MinimumPasswordLength => PasswordHasher.MinimumLength;

    public async Task OnGetAsync() => TokenIsValid = await FindUserAsync() is not null;

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await FindUserAsync();
        TokenIsValid = user is not null;
        if (user is null)
        {
            return Page();
        }

        if (NewPassword != ConfirmPassword)
        {
            ModelState.AddModelError(nameof(ConfirmPassword), labels["passwordsDoNotMatch"]);
            return Page();
        }

        try
        {
            await users.SetPasswordAsync(user.UserName, NewPassword, bypassRights: true, HttpContext.RequestAborted);
            Completed = true;
        }
        catch (BlogValidationException ex)
        {
            foreach (var (field, message) in ex.Errors)
            {
                ModelState.AddModelError(field, message);
            }
        }

        return Page();
    }

    private async Task<BlogUser?> FindUserAsync()
    {
        if (tokens.GetUserName(Token, context.Blog.Id) is not { } userName
            || await users.GetUserAsync(userName, HttpContext.RequestAborted) is not { } user)
        {
            return null;
        }

        // Il token vale solo finché la password non cambia: dopo l'uso diventa inservibile.
        return tokens.IsCurrent(Token, user) ? user : null;
    }
}

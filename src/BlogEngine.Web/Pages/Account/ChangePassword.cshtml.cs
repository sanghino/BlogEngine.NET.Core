using BlogEngine.Core;
using BlogEngine.Core.Security;
using BlogEngine.Core.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Localization;

namespace BlogEngine.Web.Pages.Account;

[Authorize]
public sealed class ChangePasswordModel(IUserService users, IStringLocalizer<Labels> labels) : PageModel
{
    [BindProperty]
    public string OldPassword { get; set; } = string.Empty;

    [BindProperty]
    public string NewPassword { get; set; } = string.Empty;

    [BindProperty]
    public string ConfirmPassword { get; set; } = string.Empty;

    public bool Completed { get; private set; }

    public int MinimumPasswordLength => PasswordHasher.MinimumLength;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (NewPassword != ConfirmPassword)
        {
            ModelState.AddModelError(nameof(ConfirmPassword), labels["passwordsDoNotMatch"]);
            return Page();
        }

        try
        {
            Completed = await users.ChangePasswordAsync(OldPassword, NewPassword, HttpContext.RequestAborted);
            if (!Completed)
            {
                ModelState.AddModelError(nameof(OldPassword), labels["passwordNotChanged"]);
            }
        }
        catch (BlogValidationException ex)
        {
            foreach (var (field, message) in ex.Errors)
            {
                ModelState.AddModelError(field, message);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Il cookie è valido ma appartiene a un altro blog o a un utente eliminato.
            return Challenge();
        }

        return Page();
    }
}

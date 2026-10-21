using BlogEngine.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlogEngine.Web.Pages.Account;

/// <summary>L'uscita avviene solo con POST (protetto da antiforgery); la GET mostra il pulsante.</summary>
public sealed class LogoutModel : PageModel
{
    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await AccountSignIn.SignOutAsync(HttpContext);
        return LocalRedirect("~/");
    }
}

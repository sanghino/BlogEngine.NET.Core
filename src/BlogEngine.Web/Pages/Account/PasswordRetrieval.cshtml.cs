using System.Net;
using BlogEngine.Core.Services;
using BlogEngine.Services;
using BlogEngine.Web.Hosting;
using BlogEngine.Web.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace BlogEngine.Web.Pages.Account;

/// <summary>
/// Recupero della password: invia un link per sceglierne una nuova. La versione originale inviava
/// la nuova password in chiaro per email; il link a scadenza evita di far circolare password.
/// </summary>
[EnableRateLimiting(WebServiceCollectionExtensions.AccountRateLimit)]
public sealed class PasswordRetrievalModel(
    IUserService users,
    ISettingsService settings,
    IEmailSender email,
    PasswordResetTokens tokens,
    IStringLocalizer<Labels> labels,
    ILogger<PasswordRetrievalModel> logger) : PageModel
{
    [BindProperty]
    public string UserName { get; set; } = string.Empty;

    public bool Enabled { get; private set; }

    public bool Requested { get; private set; }

    public async Task OnGetAsync() =>
        Enabled = (await settings.GetAsync(cancellationToken: HttpContext.RequestAborted)).EnablePasswordReset;

    public async Task<IActionResult> OnPostAsync()
    {
        var blogSettings = await settings.GetAsync(cancellationToken: HttpContext.RequestAborted);
        Enabled = blogSettings.EnablePasswordReset;
        if (!Enabled)
        {
            return Page();
        }

        var user = await users.GetUserAsync(UserName.Trim(), HttpContext.RequestAborted);
        if (user is not null)
        {
            var link = Url.Page("/Account/ResetPassword", pageHandler: null, values: new { token = tokens.Create(user) }, protocol: Request.Scheme)!;
            var body = string.Format(labels["resetPasswordEmailBody"], WebUtility.HtmlEncode(blogSettings.Name),
                $"<a href=\"{WebUtility.HtmlEncode(link)}\">{WebUtility.HtmlEncode(link)}</a>");

            var error = await email.SendAsync(new EmailMessage { To = [user.Email], Subject = labels["resetPasswordEmailSubject"], HtmlBody = $"<p>{body}</p>" },
                HttpContext.RequestAborted);
            if (error is not null)
            {
                logger.LogWarning("Email di reimpostazione password non inviata: {Error}", error);
                ModelState.AddModelError(string.Empty, labels["emailSendFailed"]);
                return Page();
            }
        }

        // Stesso messaggio che l'utente esista o meno, per non rivelare quali nomi utente sono registrati.
        Requested = true;
        return Page();
    }
}

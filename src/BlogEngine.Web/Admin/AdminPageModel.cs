using BlogEngine.Security;
using BlogEngine.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace BlogEngine.Web.Admin;

/// <summary>
/// Base delle pagine dell'amministrazione: verifica il permesso richiesto e, se manca, rimanda al login
/// (porting di Security.RedirectForUnauthorizedRequest).
/// </summary>
public abstract class AdminPageModel(Rights requiredRight) : PageModel
{
    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var services = HttpContext.RequestServices;
        if (!await services.GetRequiredService<IRightsService>().IsAuthorizedAsync(requiredRight, HttpContext.RequestAborted))
        {
            var returnUrl = $"{Request.PathBase}{Request.Path}{Request.QueryString}";
            context.Result = Redirect($"{Request.PathBase}/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}");
            return;
        }

        await services.GetRequiredService<AdminViewContext>().LoadAsync(HttpContext.RequestAborted);
        await next();
    }
}

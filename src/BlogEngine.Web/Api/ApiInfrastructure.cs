using BlogEngine.Core;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlogEngine.Web.Api;

/// <summary>
/// Base dei controller delle API dell'amministrazione (route "api/..."), con le stesse convenzioni della
/// Web API originale: errori di permesso → 401, dati non validi → 400 con il messaggio.
/// </summary>
[ApiController]
[ApiExceptionFilter]
[RequireBlogInstanceHeader]
public abstract class AdminApiController : ControllerBase
{
}

/// <summary>Traduce le eccezioni del Core nelle risposte che l'amministrazione AngularJS si aspetta.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        context.Result = context.Exception switch
        {
            UnauthorizedAccessException => new StatusCodeResult(StatusCodes.Status401Unauthorized),
            BlogValidationException validation => new BadRequestObjectResult(new { Message = validation.Message, validation.Errors }),
            KeyNotFoundException => new NotFoundResult(),
            InvalidOperationException or ArgumentException => new BadRequestObjectResult(new { context.Exception.Message }),
            _ => null,
        };

        context.ExceptionHandled = context.Result is not null;
    }
}

/// <summary>
/// Protezione dalle richieste contraffatte da altri siti (CSRF): le chiamate che modificano dati devono avere
/// l'intestazione "x-blog-instance", che l'amministrazione invia sempre e che una pagina di un altro sito non
/// può aggiungere senza un'autorizzazione CORS che il blog non concede.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireBlogInstanceHeaderAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var request = context.HttpContext.Request;
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method) && !HttpMethods.IsOptions(request.Method)
            && !request.Headers.ContainsKey(BlogContextMiddleware.BlogInstanceHeader))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
        }
    }
}

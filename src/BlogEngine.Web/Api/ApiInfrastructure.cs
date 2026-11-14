using BlogEngine.Core;
using BlogEngine.Web.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

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
    /// <summary>Messaggio tradotto nella lingua del blog (chiave di Resources/Labels.*.resx).</summary>
    protected string T(string key) => HttpContext.RequestServices.GetRequiredService<MessageTranslator>()[key];
}

/// <summary>Traduce le eccezioni del Core nelle risposte che l'amministrazione AngularJS si aspetta.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class ApiExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        // I messaggi del Core sono chiavi: qui diventano testo nella lingua del blog.
        var translator = context.HttpContext.RequestServices.GetRequiredService<MessageTranslator>();
        context.Result = context.Exception switch
        {
            UnauthorizedAccessException => new StatusCodeResult(StatusCodes.Status401Unauthorized),
            BlogValidationException validation => Validation(translator.Translate(validation.Errors)),
            KeyNotFoundException => new NotFoundResult(),
            InvalidOperationException or ArgumentException => new BadRequestObjectResult(new { Message = translator[context.Exception.Message] }),
            _ => null,
        };

        context.ExceptionHandled = context.Result is not null;
        if (context.ExceptionHandled)
        {
            LogRejected(context, translator);
        }
    }

    /// <summary>
    /// Registra nel log del sito le richieste dell'amministrazione rifiutate (dati non validi, permessi, elementi
    /// inesistenti): l'utente vede il messaggio nella finestra, l'amministratore lo ritrova in Log anche dopo.
    /// Si registra il motivo, mai i dati inviati (potrebbero contenere password).
    /// </summary>
    private static void LogRejected(ExceptionContext context, MessageTranslator translator)
    {
        var http = context.HttpContext;
        var reason = context.Exception switch
        {
            BlogValidationException validation => string.Join(" ", translator.Translate(validation.Errors).Select(e => $"{e.Key}: {e.Value}")),
            UnauthorizedAccessException => "permessi insufficienti",
            _ => translator[context.Exception.Message],
        };

        http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("BlogEngine.Web.Api")
            .LogWarning("Amministrazione: richiesta {Method} {Path} rifiutata ({Status}) per l'utente {User}. Motivo: {Reason}",
                http.Request.Method, http.Request.Path.Value, (context.Result as IStatusCodeActionResult)?.StatusCode,
                http.User.Identity?.Name ?? "anonimo", reason);
    }

    private static BadRequestObjectResult Validation(IReadOnlyDictionary<string, string> errors) =>
        new(new { Message = string.Join(Environment.NewLine, errors.Values), Errors = errors });
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

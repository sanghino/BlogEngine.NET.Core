using BlogEngine.Services;
using Microsoft.AspNetCore.ResponseCompression;

namespace BlogEngine.Web.Hosting;

/// <summary>
/// Vecchi indirizzi con estensione .aspx (porting di UrlRewrite e RedirectToRemoveFileExtension):
/// vengono rimandati in modo permanente a quelli nuovi, così link e motori di ricerca non si perdono.
/// Esempi: post/titolo.aspx → post/titolo, default.aspx?tag=x → ?tag=x, Account/login.aspx → account/login.
/// </summary>
public sealed class LegacyUrlMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        var path = http.Request.Path.Value ?? "/";
        if ((HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method))
            && path.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("/error404.aspx", StringComparison.OrdinalIgnoreCase))
        {
            http.Response.Redirect($"{http.Request.PathBase}{NewPath(path)}{http.Request.QueryString}", permanent: true);
            return Task.CompletedTask;
        }

        return next(http);
    }

    public static string NewPath(string path)
    {
        var target = path[..^".aspx".Length];

        // Le pagine "default" erano la home o l'elenco di un periodo (/2025/03/default.aspx).
        if (target.Equals("/default", StringComparison.OrdinalIgnoreCase))
        {
            return "/";
        }

        if (target.EndsWith("/default", StringComparison.OrdinalIgnoreCase))
        {
            target = target[..^"/default".Length];
        }

        // Le pagine dell'account ora sono in minuscolo (/account/login).
        if (target.StartsWith("/account/", StringComparison.OrdinalIgnoreCase))
        {
            target = target.ToLowerInvariant();
        }

        return target.Length == 0 ? "/" : target;
    }
}

/// <summary>
/// Aggiunge o toglie il prefisso "www" (impostazione HandleWwwSubdomain: "add" o "remove"), con un
/// redirect permanente. Porting di WwwSubDomainModule. Gli indirizzi IP e localhost non vengono toccati.
/// </summary>
public sealed class WwwRedirectMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, InstallationState installation)
    {
        var host = http.Request.Host.Host;
        if (installation.IsInstalled && host.Contains('.') && !System.Net.IPAddress.TryParse(host, out _)
            && (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)))
        {
            var mode = (await http.RequestServices.GetRequiredService<ISettingsService>().GetAsync()).HandleWwwSubdomain;
            var hasWww = host.StartsWith("www.", StringComparison.OrdinalIgnoreCase);
            var newHost = mode switch
            {
                "remove" when hasWww => host[4..],
                "add" when !hasWww => "www." + host,
                _ => null,
            };

            if (newHost is not null)
            {
                var port = http.Request.Host.Port is { } p ? $":{p}" : string.Empty;
                http.Response.Redirect($"{http.Request.Scheme}://{newHost}{port}{http.Request.PathBase}{http.Request.Path}{http.Request.QueryString}", permanent: true);
                return;
            }
        }

        await next(http);
    }
}

/// <summary>
/// Conta le visite che arrivano da altri siti (impostazione EnableReferrerTracking), mostrate nella
/// dashboard dell'amministrazione. Porting di ReferrerModule.
/// </summary>
public sealed class ReferrerMiddleware(RequestDelegate next)
{
    private static readonly string[] SkippedPrefixes = ["/admin", "/api", "/account", "/setup", "/error"];

    public async Task InvokeAsync(HttpContext http, InstallationState installation, ILogger<ReferrerMiddleware> logger)
    {
        if (installation.IsInstalled
            && HttpMethods.IsGet(http.Request.Method)
            && Uri.TryCreate(http.Request.Headers.Referer.ToString(), UriKind.Absolute, out var referrer)
            && referrer.Scheme is "http" or "https"
            && !string.Equals(referrer.Host, http.Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && !SkippedPrefixes.Any(p => http.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))
            && !Path.HasExtension(http.Request.Path.Value))
        {
            try
            {
                if ((await http.RequestServices.GetRequiredService<ISettingsService>().GetAsync()).EnableReferrerTracking)
                {
                    var url = new Uri($"{http.Request.Scheme}://{http.Request.Host}{http.Request.PathBase}{http.Request.Path}");
                    await http.RequestServices.GetRequiredService<IReferrerService>().RegisterAsync(url, referrer, possibleSpam: false, http.RequestAborted);
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                // Le statistiche non devono mai impedire di vedere la pagina.
                logger.LogWarning(ex, "Referrer non registrato.");
            }
        }

        await next(http);
    }
}

/// <summary>
/// Compressione gzip/brotli delle pagine quando l'impostazione EnableHttpCompression è attiva
/// (porting di CompressionModule).
/// </summary>
public sealed class ConditionalCompressionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ResponseCompressionMiddleware _compression;

    public ConditionalCompressionMiddleware(RequestDelegate next, IResponseCompressionProvider provider)
    {
        _next = next;
        _compression = new ResponseCompressionMiddleware(next, provider);
    }

    public async Task InvokeAsync(HttpContext http, InstallationState installation)
    {
        if (installation.IsInstalled && (await http.RequestServices.GetRequiredService<ISettingsService>().GetAsync()).EnableHttpCompression)
        {
            await _compression.Invoke(http);
            return;
        }

        await _next(http);
    }

    public static void Register(IServiceCollection services) =>
        services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                ["application/rss+xml", "application/atom+xml", "application/rdf+xml", "application/opensearchdescription+xml"]);
        });
}

/// <summary>
/// Intestazioni di sicurezza su tutte le risposte: il sito non può essere incorniciato da altri siti
/// (clickjacking su login e amministrazione), il browser non "indovina" il tipo dei file e agli altri
/// siti arriva solo l'origine come referrer. Un'intestazione già impostata (es. dai file caricati) resta.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext http)
    {
        http.Response.OnStarting(() =>
        {
            var headers = http.Response.Headers;
            headers.TryAdd("X-Frame-Options", "SAMEORIGIN");
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
            return Task.CompletedTask;
        });

        return next(http);
    }
}

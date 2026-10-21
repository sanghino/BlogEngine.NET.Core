using BlogEngine.Core.Extensions;
using BlogEngine.Services;
using Microsoft.AspNetCore.Html;

namespace BlogEngine.Web.Extensions;

/// <summary>Captcha delle estensioni attive per i moduli dei commenti e dei contatti.</summary>
public sealed class CaptchaService(IEnumerable<ICaptchaProvider> providers, IExtensionGate gate, IBlogContext context, IHttpContextAccessor http)
{
    private IEnumerable<ICaptchaProvider> Active => providers.Where(gate.IsActive).OrderBy(gate.PriorityOf);

    public async Task<HtmlString> RenderAsync(CaptchaForm form, CancellationToken cancellationToken = default)
    {
        var html = new List<string>();
        foreach (var provider in Active)
        {
            if (await provider.RenderAsync(Context(form), cancellationToken) is { Length: > 0 } markup)
            {
                html.Add(markup);
            }
        }

        return new HtmlString(string.Join(Environment.NewLine, html));
    }

    /// <summary>Tutti i captcha attivi devono accettare la risposta.</summary>
    public async Task<bool> ValidateAsync(CaptchaForm form, IFormCollection fields, CancellationToken cancellationToken = default)
    {
        var values = fields.ToDictionary(f => f.Key, f => f.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        foreach (var provider in Active)
        {
            if (!await provider.ValidateAsync(Context(form), values, cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private CaptchaContext Context(CaptchaForm form) => new()
    {
        Form = form,
        IsAuthenticated = context.IsAuthenticated,
        RemoteIp = http.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
    };
}

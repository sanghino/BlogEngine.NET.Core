// Trasforma gli indirizzi scritti nei commenti (http://..., www...) in link cliccabili con rel="nofollow".
// Porting dell'estensione ResolveLinks di BlogEngine.NET.

using System.Text.RegularExpressions;

[Extension("Auto resolves URLs in the comments and turn them into valid hyperlinks.", "4.0", "BlogEngine.NET")]
public sealed class ResolveLinks : IBlogExtension
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IEventHandler<ServingEvent<Comment>>, ResolveLinksHandler>();
}

public sealed class ResolveLinksHandler : IEventHandler<ServingEvent<Comment>>
{
    private const int MaxLength = 50;

    private static readonly Regex LinkRegex = new(
        @"((http://|https://|www\.)([A-Z0-9.\-]{1,})\.[0-9A-Z?;~&%\(\)#,=\-_\./\+]{2,}[0-9A-Z?~&%#=\-_/\+])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task HandleAsync(ServingEvent<Comment> blogEvent, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(blogEvent.Body))
        {
            blogEvent.Body = LinkRegex.Replace(blogEvent.Body, match =>
                $"<a href=\"{(match.Value.Contains("://", StringComparison.Ordinal) ? string.Empty : "http://")}{match.Value}\" rel=\"nofollow\">{Shorten(match.Value)}</a>");
        }

        return Task.CompletedTask;
    }

    /// <summary>Accorcia gli indirizzi lunghi togliendo le parti centrali, come nella versione originale.</summary>
    private static string Shorten(string url)
    {
        if (url.Length <= MaxLength)
        {
            return url;
        }

        var scheme = url.IndexOf("://", StringComparison.Ordinal);
        if (scheme > -1)
        {
            url = url[(scheme + 3)..];
        }

        var first = url.IndexOf('/') + 1;
        var last = url.LastIndexOf('/');
        if (url.Length > MaxLength && first > 0 && first < last)
        {
            url = url.Remove(first, last - first).Insert(first, "...");
        }

        foreach (var separator in new[] { '?', '#' })
        {
            if (url.Length > MaxLength && url.IndexOf(separator) is var index and > -1)
            {
                url = url[..index];
            }
        }

        return url.Length > MaxLength ? url[..(MaxLength - 3)] + "..." : url;
    }
}

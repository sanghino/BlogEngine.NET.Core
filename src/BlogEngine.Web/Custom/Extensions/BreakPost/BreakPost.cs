// Taglia il post nel punto in cui il testo contiene [more] e aggiunge il link al post completo.
// Porting dell'estensione BreakPost di BlogEngine.NET.

using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

[Extension("Breaks a post where [more] is found in the body and adds a link to full post", "4.0", "BlogEngine.NET", Priority = 1010)]
public sealed class BreakPost : IBlogExtension
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IEventHandler<ServingEvent<Post>>, BreakPostHandler>();
}

public sealed class BreakPostHandler(ILinkGenerator links, ILabels labels, IHttpContextAccessor http) : IEventHandler<ServingEvent<Post>>
{
    private const string Marker = "[more]";

    private static readonly Regex OpeningTagRegex = new(@"<([A-Z][A-Z0-9]*?)\b[^>/]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ClosingTagRegex = new(@"</([A-Z][A-Z0-9]*?)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task HandleAsync(ServingEvent<Post> blogEvent, CancellationToken cancellationToken)
    {
        if (!blogEvent.Body.Contains(Marker, StringComparison.Ordinal))
        {
            return;
        }

        switch (blogEvent.Location)
        {
            case ServingLocation.PostList:
                var url = await links.PostAsync(blogEvent.Item, cancellationToken);
                blogEvent.Body = Cut(blogEvent.Body, $"<a class=\"more\" href=\"{System.Net.WebUtility.HtmlEncode(url)}#continue\">{System.Net.WebUtility.HtmlEncode(labels["more"])}...</a>");
                break;
            case ServingLocation.SinglePost:
                // Il segnaposto diventa un'ancora solo per chi arriva dal sito (dal link "continua").
                var request = http.HttpContext?.Request;
                var fromSite = request is not null && Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referrer)
                    && string.Equals(referrer.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase);
                blogEvent.Body = blogEvent.Body.Replace(Marker, fromSite ? "<span id=\"continue\"></span>" : string.Empty, StringComparison.Ordinal);
                break;
            default:
                blogEvent.Body = blogEvent.Body.Replace(Marker, string.Empty, StringComparison.Ordinal);
                break;
        }
    }

    /// <summary>Tiene il testo prima di [more], aggiunge il link e chiude i tag rimasti aperti.</summary>
    private static string Cut(string body, string link)
    {
        var text = body[..body.IndexOf(Marker, StringComparison.Ordinal)];
        var open = OpeningTagRegex.Matches(text).Select(m => m.Groups[1].Value).ToList();
        foreach (Match closed in ClosingTagRegex.Matches(text))
        {
            var index = open.FindLastIndex(t => t.Equals(closed.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
            if (index != -1)
            {
                open.RemoveAt(index);
            }
        }

        open.Reverse();
        return text + link + string.Concat(open.Select(t => $"</{t}>"));
    }
}

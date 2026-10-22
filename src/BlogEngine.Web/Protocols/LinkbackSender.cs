using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace BlogEngine.Web.Protocols;

/// <summary>Dati del post da notificare ai siti che cita.</summary>
public sealed record LinkbackSource(Uri PostUrl, string Title, string Excerpt, string BlogName, string Content);

/// <summary>
/// Avvisa i siti citati in un post (porting di Services/Messaging/Ping/Manager): per ogni link esterno
/// cerca l'indirizzo pingback (intestazione X-Pingback o &lt;link rel="pingback"&gt;) e, se manca,
/// quello trackback indicato nel codice RDF della pagina. Usato dall'estensione SendPings.
/// </summary>
public sealed partial class LinkbackSender(IRemotePages remote, ILogger<LinkbackSender> logger)
{
    private const int MaxLinks = 20;

    public async Task SendAsync(LinkbackSource source, bool pingbacks, bool trackbacks, CancellationToken cancellationToken)
    {
        if (!pingbacks && !trackbacks)
        {
            return;
        }

        foreach (var target in ExternalLinks(source))
        {
            var page = await remote.GetAsync(target, cancellationToken);
            if (page is null)
            {
                continue;
            }

            if (pingbacks && PingbackServer(page) is { } server)
            {
                var request = XmlRpcRequest("pingback.ping", source.PostUrl.ToString(), target.ToString());
                var response = await remote.PostAsync(server, new StringContent(request, Encoding.UTF8, "text/xml"), cancellationToken);
                logger.LogInformation("Pingback inviato a {Target}: {Result}", target, response is null ? "nessuna risposta" : response.Contains("<fault>", StringComparison.Ordinal) ? "rifiutato" : "accettato");
                continue;
            }

            if (trackbacks && TrackbackUrl(page.Html, target) is { } trackback)
            {
                var form = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["title"] = source.Title,
                    ["excerpt"] = source.Excerpt,
                    ["url"] = source.PostUrl.ToString(),
                    ["blog_name"] = source.BlogName,
                });
                var response = await remote.PostAsync(trackback, form, cancellationToken);
                logger.LogInformation("Trackback inviato a {Target}: {Result}", trackback, response?.Contains("<error>0</error>", StringComparison.Ordinal) == true ? "accettato" : "rifiutato");
            }
        }
    }

    /// <summary>Link http(s) del testo verso altri siti, senza duplicati.</summary>
    public static IReadOnlyList<Uri> ExternalLinks(LinkbackSource source) =>
        HrefRegex().Matches(source.Content)
            .Select(m => Uri.TryCreate(WebUtility.HtmlDecode(m.Groups["url"].Value), UriKind.Absolute, out var uri) ? uri : null)
            .Where(u => u is not null && u.Scheme is "http" or "https" && !string.Equals(u.Host, source.PostUrl.Host, StringComparison.OrdinalIgnoreCase))
            .Select(u => u!)
            .DistinctBy(u => u.ToString())
            .Take(MaxLinks)
            .ToList();

    private static Uri? PingbackServer(RemotePage page)
    {
        if (page.Headers.TryGetValues("X-Pingback", out var values) && Uri.TryCreate(values.FirstOrDefault(), UriKind.Absolute, out var header))
        {
            return header;
        }

        var link = PingbackLinkRegex().Match(page.Html);
        return link.Success && Uri.TryCreate(WebUtility.HtmlDecode(link.Groups["url"].Value), UriKind.Absolute, out var url) ? url : null;
    }

    /// <summary>Indirizzo trackback:ping del blocco RDF che descrive la pagina citata.</summary>
    private static Uri? TrackbackUrl(string html, Uri target)
    {
        foreach (Match rdf in RdfRegex().Matches(html))
        {
            var about = AboutRegex().Match(rdf.Value);
            var ping = PingRegex().Match(rdf.Value);
            var matchesTarget = !about.Success || about.Groups["url"].Value.TrimEnd('/').Equals(target.ToString().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
            if (ping.Success && matchesTarget && Uri.TryCreate(WebUtility.HtmlDecode(ping.Groups["url"].Value), UriKind.Absolute, out var url))
            {
                return url;
            }
        }

        return null;
    }

    private static string XmlRpcRequest(string method, params string[] parameters)
    {
        var text = new StringBuilder($"<?xml version=\"1.0\"?><methodCall><methodName>{method}</methodName><params>");
        foreach (var parameter in parameters)
        {
            text.Append("<param><value><string>").Append(WebUtility.HtmlEncode(parameter)).Append("</string></value></param>");
        }

        return text.Append("</params></methodCall>").ToString();
    }

    [GeneratedRegex("href=[\"'](?<url>https?://[^\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex HrefRegex();

    [GeneratedRegex("<link[^>]*rel=[\"']pingback[\"'][^>]*href=[\"'](?<url>[^\"']+)[\"']|<link[^>]*href=[\"'](?<url>[^\"']+)[\"'][^>]*rel=[\"']pingback[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex PingbackLinkRegex();

    [GeneratedRegex("<rdf:RDF.*?</rdf:RDF>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RdfRegex();

    [GeneratedRegex("rdf:about=[\"'](?<url>[^\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex AboutRegex();

    [GeneratedRegex("trackback:ping=[\"'](?<url>[^\"']+)[\"']", RegexOptions.IgnoreCase)]
    private static partial Regex PingRegex();
}

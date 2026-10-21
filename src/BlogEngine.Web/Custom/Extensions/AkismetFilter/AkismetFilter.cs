// Filtro antispam Akismet (https://akismet.com): serve una chiave API, da inserire nelle impostazioni.
// Segnala ad Akismet anche i suoi errori (spam approvati o commenti validi scartati), se l'opzione
// "segnala gli errori ai filtri" dei commenti è attiva. Porting di AkismetFilter (Joel Thoms).

using System.Net.Http;

[Extension("Akismet anti-spam comment filter", "4.0", "<a href=\"https://blogengine.io\">BlogEngine.NET</a>", EnabledByDefault = false)]
public sealed class AkismetFilter : IBlogExtension
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<ICommentSpamFilter, AkismetSpamFilter>();

    public IEnumerable<ExtensionSettingsDefinition> Settings =>
    [
        new ExtensionSettingsDefinition(nameof(AkismetFilter)) { IsScalar = true, Help = "Get your API key at <a href=\"https://akismet.com\" target=\"_blank\">akismet.com</a>." }
            .AddParameter("SiteURL", "Site URL", 200, required: true)
            .AddParameter("ApiKey", "API Key", 50, required: true)
            .SetDefault("SiteURL", "http://example.com/blog")
            .SetDefault("ApiKey", "123456789"),
    ];
}

public sealed class AkismetSpamFilter(
    IExtensionSettingsService settings,
    IHttpClientFactory httpClients,
    ILinkGenerator links,
    ILogger<AkismetSpamFilter> logger) : ICommentSpamFilter
{
    public string Name => nameof(AkismetFilter);

    public async Task<SpamCheckResult> CheckAsync(Post post, Comment comment, CancellationToken cancellationToken)
    {
        var response = await CallAsync("comment-check", post, comment, cancellationToken);
        return response switch
        {
            "true" => SpamCheckResult.Spam,
            "false" => SpamCheckResult.Ham,
            _ => SpamCheckResult.NotChecked,
        };
    }

    public async Task ReportMistakeAsync(Post post, Comment comment, CancellationToken cancellationToken)
    {
        // Approvato dall'amministratore: Akismet aveva sbagliato a considerarlo spam, e viceversa.
        logger.LogInformation("Akismet: segnalazione di {Kind} da \"{Author}\" ({Ip})", comment.IsApproved ? "NON spam" : "spam", comment.Author, comment.IP);
        await CallAsync(comment.IsApproved ? "submit-ham" : "submit-spam", post, comment, cancellationToken);
    }

    private async Task<string?> CallAsync(string method, Post post, Comment comment, CancellationToken cancellationToken)
    {
        var values = await settings.GetAsync(nameof(AkismetFilter), cancellationToken: cancellationToken);
        var key = values.Get("ApiKey");
        if (string.IsNullOrWhiteSpace(key) || key == "123456789")
        {
            return null;
        }

        var form = new Dictionary<string, string>
        {
            ["blog"] = values.Get("SiteURL"),
            ["user_ip"] = comment.IP,
            ["permalink"] = links.ToAbsolute(await links.PostAsync(post, cancellationToken)).ToString(),
            ["comment_type"] = "comment",
            ["comment_author"] = comment.Author,
            ["comment_author_email"] = comment.Email,
            ["comment_author_url"] = comment.Website?.ToString() ?? string.Empty,
            ["comment_content"] = comment.Content,
        };

        try
        {
            var client = httpClients.CreateClient(nameof(AkismetFilter));
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("BlogEngine.NET/4.0 Akismet/1.11");
            using var response = await client.PostAsync($"https://{Uri.EscapeDataString(key)}.rest.akismet.com/1.1/{method}", new FormUrlEncodedContent(form), cancellationToken);
            return (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or UriFormatException)
        {
            logger.LogInformation("Akismet non raggiungibile: {Message}", ex.Message);
            return null;
        }
    }
}

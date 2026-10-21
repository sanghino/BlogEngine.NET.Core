// Filtro antispam: controlla l'indirizzo IP di chi commenta nell'elenco degli spammer di stopforumspam.com.
// Porting dell'estensione StopForumSpam (disattivata alla prima installazione, come in origine).

using System.Net.Http;
using System.Text.Json;

[Extension("Checks comment against stopforumspam.com spammers list.", "4.0", "BlogEngine.NET", EnabledByDefault = false)]
public sealed class StopForumSpam : IBlogExtension
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<ICommentSpamFilter, StopForumSpamFilter>();
}

public sealed class StopForumSpamFilter(IHttpClientFactory httpClients, ILogger<StopForumSpamFilter> logger) : ICommentSpamFilter
{
    public string Name => nameof(StopForumSpam);

    public async Task<SpamCheckResult> CheckAsync(Post post, Comment comment, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(comment.IP))
        {
            return SpamCheckResult.NotChecked;
        }

        try
        {
            var client = httpClients.CreateClient(nameof(StopForumSpam));
            client.Timeout = TimeSpan.FromSeconds(10);
            var json = await client.GetStringAsync($"https://api.stopforumspam.org/api?json&ip={Uri.EscapeDataString(comment.IP)}", cancellationToken);
            using var document = JsonDocument.Parse(json);
            var appears = document.RootElement.TryGetProperty("ip", out var ip) && ip.TryGetProperty("appears", out var value) && value.GetInt32() > 0;
            return appears ? SpamCheckResult.Spam : SpamCheckResult.Ham;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            logger.LogInformation("Errore nel controllo su stopforumspam.com: {Message}", ex.Message);
            return SpamCheckResult.NotChecked;
        }
    }
}

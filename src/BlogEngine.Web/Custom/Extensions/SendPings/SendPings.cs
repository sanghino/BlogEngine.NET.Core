// Quando un post o una pagina pubblici vengono salvati, avvisa i servizi di ping elencati in
// Impostazioni → Controlli → Ping services (protocollo XML-RPC weblogUpdates.ping).
// Porting dell'estensione SendPings di BlogEngine.NET. L'invio di trackback e pingback ai siti
// citati nel testo verrà aggiunto con gli altri protocolli (fase 7).

using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using BlogEngine.Core.Data;

[Extension("Pings all the ping services specified on the PingServices admin page", "4.0", "BlogEngine.NET")]
public sealed class SendPings : IBlogExtension
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IEventHandler<SavedEvent<Post>>, SendPingsHandler>();
        services.AddScoped<IEventHandler<SavedEvent<Page>>, SendPingsHandler>();
    }
}

public sealed class SendPingsHandler(
    IStringListStore lists,
    ISettingsService settings,
    ILinkGenerator links,
    IBlogClock clock,
    IHttpClientFactory httpClients,
    ILogger<SendPingsHandler> logger) : IEventHandler<SavedEvent<Post>>, IEventHandler<SavedEvent<Page>>
{
    public Task HandleAsync(SavedEvent<Post> blogEvent, CancellationToken cancellationToken) =>
        blogEvent.Entity is { IsPublished: true, IsDeleted: false } post && post.DateCreated <= clock.UtcNow
            ? PingAsync(blogEvent.BlogId, blogEvent.Action, cancellationToken)
            : Task.CompletedTask;

    public Task HandleAsync(SavedEvent<Page> blogEvent, CancellationToken cancellationToken) =>
        blogEvent.Entity is { IsPublished: true, IsDeleted: false }
            ? PingAsync(blogEvent.BlogId, blogEvent.Action, cancellationToken)
            : Task.CompletedTask;

    private async Task PingAsync(Guid blogId, SaveAction action, CancellationToken cancellationToken)
    {
        if (action is not (SaveAction.Insert or SaveAction.Update))
        {
            return;
        }

        var services = await lists.LoadAsync(blogId, StringListKind.PingServices, cancellationToken);
        if (services.Count == 0)
        {
            return;
        }

        var name = (await settings.GetAsync(cancellationToken: cancellationToken)).Name;
        var blogUrl = links.ToAbsolute(links.BlogRoot).ToString();
        var request = new XDocument(
            new XElement("methodCall",
                new XElement("methodName", "weblogUpdates.ping"),
                new XElement("params",
                    new XElement("param", new XElement("value", name)),
                    new XElement("param", new XElement("value", blogUrl))))).ToString();

        // In background: chi salva il post non deve aspettare i servizi esterni.
        _ = Task.Run(async () =>
        {
            var client = httpClients.CreateClient(nameof(SendPings));
            client.Timeout = TimeSpan.FromSeconds(15);
            foreach (var service in services.Where(s => Uri.TryCreate(s, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
            {
                try
                {
                    using var response = await client.PostAsync(service, new StringContent(request, Encoding.UTF8, "text/xml"));
                    logger.LogInformation("Ping inviato a {Service}: {Status}", service, (int)response.StatusCode);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    logger.LogInformation("Ping non riuscito verso {Service}: {Message}", service, ex.Message);
                }
            }
        }, CancellationToken.None);
    }
}

// Quando un post o una pagina pubblici vengono salvati:
// - avvisa i servizi di ping elencati in Impostazioni → Controlli → Ping services (XML-RPC weblogUpdates.ping);
// - se attivato nelle impostazioni dei commenti, invia pingback e trackback ai siti citati nel testo.
// Porting dell'estensione SendPings di BlogEngine.NET.

using System.Net.Http;
using System.Text;
using System.Xml.Linq;
using BlogEngine.Core.Data;
using BlogEngine.Web.Protocols;

[Extension("Pings all the ping services specified on the PingServices admin page and send track- and pingbacks", "4.0", "BlogEngine.NET")]
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
    LinkbackSender linkbacks,
    ILogger<SendPingsHandler> logger) : IEventHandler<SavedEvent<Post>>, IEventHandler<SavedEvent<Page>>
{
    public async Task HandleAsync(SavedEvent<Post> blogEvent, CancellationToken cancellationToken)
    {
        if (blogEvent.Entity is { IsPublished: true, IsDeleted: false } post && post.DateCreated <= clock.UtcNow)
        {
            await PingAsync(blogEvent.BlogId, blogEvent.Action, links.ToAbsolute(await links.PostAsync(post, cancellationToken)), post.Title, post.Description, post.Content, cancellationToken);
        }
    }

    public async Task HandleAsync(SavedEvent<Page> blogEvent, CancellationToken cancellationToken)
    {
        if (blogEvent.Entity is { IsPublished: true, IsDeleted: false } page)
        {
            await PingAsync(blogEvent.BlogId, blogEvent.Action, links.ToAbsolute(await links.PageAsync(page, cancellationToken)), page.Title, page.Description, page.Content, cancellationToken);
        }
    }

    private async Task PingAsync(Guid blogId, SaveAction action, Uri url, string title, string description, string content, CancellationToken cancellationToken)
    {
        if (action is not (SaveAction.Insert or SaveAction.Update))
        {
            return;
        }

        var blogSettings = await settings.GetAsync(cancellationToken: cancellationToken);
        var services = await lists.LoadAsync(blogId, StringListKind.PingServices, cancellationToken);
        var blogUrl = links.ToAbsolute(links.BlogRoot).ToString();
        var request = new XDocument(
            new XElement("methodCall",
                new XElement("methodName", "weblogUpdates.ping"),
                new XElement("params",
                    new XElement("param", new XElement("value", blogSettings.Name)),
                    new XElement("param", new XElement("value", blogUrl))))).ToString();
        var source = new LinkbackSource(url, title, string.IsNullOrWhiteSpace(description) ? BlogEngine.Text.HtmlText.Excerpt(content, 250) : description, blogSettings.Name, content);
        var (pingbacks, trackbacks) = (blogSettings.EnablePingBackSend, blogSettings.EnableTrackBackSend);

        // In background: chi salva il post non deve aspettare i siti esterni.
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

            try
            {
                await linkbacks.SendAsync(source, pingbacks, trackbacks, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Invio di pingback e trackback non riuscito per {Url}.", url);
            }
        }, CancellationToken.None);
    }
}

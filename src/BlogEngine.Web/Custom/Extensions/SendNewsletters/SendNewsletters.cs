// Alla pubblicazione di un post invia un'email agli iscritti del widget Newsletter.
// Il messaggio usa il file newsletter.html del tema attivo (o del tema Standard), con i segnaposto
// [TITLE], [LINK], [LINK_DESCRIPTION], [WebRoot] e [httpBase]. Porting di SendNewsletters.

using System.Net;
using BlogEngine.Core.Services;
using BlogEngine.Web.Hosting;

[Extension("Sends emails to newsletter subscribers", "4.0", "BlogEngine.NET")]
public sealed class SendNewsletters : IBlogExtension
{
    public void ConfigureServices(IServiceCollection services) =>
        services.AddScoped<IEventHandler<PostPublishedEvent>, SendNewslettersHandler>();
}

public sealed class SendNewslettersHandler(
    NewsletterSubscribers subscribers,
    ISettingsService settings,
    ThemeCatalog themes,
    ILinkGenerator links,
    IEmailQueue emails,
    ILogger<SendNewslettersHandler> logger) : IEventHandler<PostPublishedEvent>
{
    public async Task HandleAsync(PostPublishedEvent blogEvent, CancellationToken cancellationToken)
    {
        var addresses = await subscribers.GetAsync(cancellationToken);
        if (addresses.Count == 0)
        {
            return;
        }

        var theme = themes.FindOrFallback((await settings.GetAsync(cancellationToken: cancellationToken)).Theme);
        var template = new[] { theme?.Folder, themes.Find(ThemeCatalog.FallbackTheme)?.Folder }
            .Where(folder => folder is not null)
            .Select(folder => Path.Combine(folder!, "newsletter.html"))
            .FirstOrDefault(File.Exists);
        if (template is null)
        {
            logger.LogWarning("Newsletter non inviata: newsletter.html non esiste né nel tema attivo né nel tema Standard.");
            return;
        }

        var post = blogEvent.Post;
        var webRoot = links.ToAbsolute(links.BlogRoot).ToString();
        var body = (await File.ReadAllTextAsync(template, cancellationToken))
            .Replace("[TITLE]", WebUtility.HtmlEncode(post.Title), StringComparison.Ordinal)
            .Replace("[LINK]", links.ToAbsolute(await links.PostAsync(post, cancellationToken)).ToString(), StringComparison.Ordinal)
            .Replace("[LINK_DESCRIPTION]", WebUtility.HtmlEncode(post.Description), StringComparison.Ordinal)
            .Replace("[WebRoot]", webRoot, StringComparison.Ordinal)
            .Replace("[httpBase]", $"{webRoot}Custom/Themes/{Uri.EscapeDataString(theme?.Id ?? ThemeCatalog.FallbackTheme)}", StringComparison.Ordinal);

        // Un messaggio per iscritto: nessuno vede gli indirizzi degli altri.
        foreach (var address in addresses.Where(a => System.Net.Mail.MailAddress.TryCreate(a, out _)))
        {
            emails.Enqueue(new EmailMessage { To = [address], Subject = post.Title, HtmlBody = body });
        }
    }
}

using System.Text;
using System.Threading.Channels;
using BlogEngine.Core.Configuration;
using BlogEngine.Core.Extensions;
using BlogEngine.Services;
using BlogEngine.Web.Hosting;

namespace BlogEngine.Web.Extensions;

/// <summary>Script e stili aggiunti dalle estensioni alla pagina corrente (scoped).</summary>
public sealed class PageResources(IHttpContextAccessor http, ILinkGenerator links) : IPageResources
{
    private readonly List<string> _head = [];
    private readonly List<string> _footer = [];

    public string ApplicationRoot => Themes.SiteRenderer.ApplicationRoot(http.HttpContext?.Request.PathBase.Value, links.BlogRoot);

    public void AddScript(string url) => Add(_head, $"<script src=\"{System.Net.WebUtility.HtmlEncode(url)}\"></script>");

    public void AddStylesheet(string url) => Add(_head, $"<link rel=\"stylesheet\" href=\"{System.Net.WebUtility.HtmlEncode(url)}\" />");

    public void AddHeadHtml(string html) => Add(_head, html);

    public void AddFooterHtml(string html) => Add(_footer, html);

    public string HeadHtml => string.Join(Environment.NewLine, _head);

    public string FooterHtml => string.Join(Environment.NewLine, _footer);

    // Lo stesso script aggiunto da più post della pagina viene scritto una volta sola.
    private static void Add(List<string> list, string html)
    {
        if (!list.Contains(html))
        {
            list.Add(html);
        }
    }
}

/// <summary>Traduzioni dell'interfaccia (Resources/Labels.*.resx) per le estensioni.</summary>
public sealed class LabelsAdapter(Microsoft.Extensions.Localization.IStringLocalizer<Labels> localizer) : ILabels
{
    public string this[string key] => localizer[key] is { ResourceNotFound: false } value ? value.Value : key;
}

/// <summary>Coda delle email in attesa di invio, condivisa da tutto il sito.</summary>
public sealed class EmailChannel
{
    public Channel<(Guid BlogId, EmailMessage Message)> Channel { get; } =
        System.Threading.Channels.Channel.CreateUnbounded<(Guid, EmailMessage)>(new UnboundedChannelOptions { SingleReader = true });
}

/// <summary>Accoda le email del blog corrente.</summary>
public sealed class BackgroundEmailQueue(EmailChannel channel, IBlogContext context) : IEmailQueue
{
    public void Enqueue(EmailMessage message) => channel.Channel.Writer.TryWrite((context.Blog.Id, message));
}

/// <summary>Invia in background le email accodate, con le impostazioni SMTP del loro blog.</summary>
public sealed class EmailSenderService(EmailChannel channel, IServiceScopeFactory scopes, ILogger<EmailSenderService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var (blogId, message) in channel.Channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var blog = await scope.ServiceProvider.GetRequiredService<IBlogService>().GetByIdAsync(blogId, stoppingToken);
                if (blog is null)
                {
                    continue;
                }

                scope.ServiceProvider.GetRequiredService<RequestBlogContext>().RunAsSystem(blog);
                await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendAsync(message, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Invio dell'email \"{Subject}\" non riuscito.", message.Subject);
            }
        }
    }
}

/// <summary>
/// Controlla ogni pochi secondi le cartelle delle estensioni: se un file viene aggiunto, modificato o
/// tolto (es. via FTP) il sito si ferma e IIS lo riavvia alla richiesta successiva, ricompilando le
/// estensioni, come accadeva con App_Code. Scrive anche nel log l'esito della compilazione.
/// </summary>
public sealed class ExtensionWatcher(
    BlogEnginePaths paths,
    ExtensionLoader.StartupReport report,
    ExtensionRegistry registry,
    IHostApplicationLifetime lifetime,
    IConfiguration configuration,
    ILogger<ExtensionWatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var message in report.Messages)
        {
            logger.LogInformation("{Message}", message);
        }

        foreach (var failed in registry.All.Where(e => e.Error is not null))
        {
            logger.LogWarning("Estensione {Name} non attiva: {Error}", failed.Name, failed.Error);
        }

        if (!configuration.GetValue("BlogEngine:RestartOnExtensionChange", true))
        {
            return;
        }

        var initial = Snapshot();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (Snapshot() != initial)
            {
                logger.LogWarning("Le estensioni sono cambiate: il sito viene riavviato per ricompilarle.");
                lifetime.StopApplication();
                return;
            }
        }
    }

    /// <summary>Elenco dei file con data e dimensione: cambia se cambia qualunque file.</summary>
    private string Snapshot()
    {
        var text = new StringBuilder();
        foreach (var (folder, _) in ExtensionLoader.Folders(paths))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal))
                {
                    var info = new FileInfo(file);
                    text.Append(file).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).AppendLine();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                text.Append(folder).AppendLine("|?");
            }
        }

        return text.ToString();
    }
}

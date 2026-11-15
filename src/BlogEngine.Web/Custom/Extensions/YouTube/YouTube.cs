// Video di YouTube: il codice [youtube:ID] nel testo di post e pagine (usato dalla vecchia estensione
// di BlogEngine.NET) diventa il riquadro del video. Nei feed e nelle email resta un collegamento.

using System.Net;
using System.Text.RegularExpressions;

[Extension("YouTube videos: [youtube:ID]", "4.0", "ExtraGeek-BlogEngine")]
public sealed class YouTube : IBlogExtension
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IEventHandler<ServingEvent<Post>>, YouTubeHandler>();
        services.AddScoped<IEventHandler<ServingEvent<Page>>, YouTubeHandler>();
    }

    public IEnumerable<ExtensionSettingsDefinition> Settings => [];
}

public sealed class YouTubeHandler : IEventHandler<ServingEvent<Post>>, IEventHandler<ServingEvent<Page>>
{
    // Accetta anche l'indirizzo completo del video: [youtube:https://www.youtube.com/watch?v=ID].
    private static readonly Regex ShortCodeRegex = new(
        @"\[youtube:\s*(?:https?://(?:www\.)?(?:youtube\.com/watch\?v=|youtu\.be/))?(?<id>[A-Za-z0-9_-]{6,20})[^\]]*\]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public Task HandleAsync(ServingEvent<Post> blogEvent, CancellationToken cancellationToken)
    {
        blogEvent.Body = Render(blogEvent.Body, blogEvent.Location);
        return Task.CompletedTask;
    }

    public Task HandleAsync(ServingEvent<Page> blogEvent, CancellationToken cancellationToken)
    {
        blogEvent.Body = Render(blogEvent.Body, blogEvent.Location);
        return Task.CompletedTask;
    }

    public static string Render(string body, ServingLocation location)
    {
        if (string.IsNullOrEmpty(body) || body.IndexOf("[youtube:", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return body;
        }

        return ShortCodeRegex.Replace(body, match =>
        {
            var id = WebUtility.HtmlEncode(match.Groups["id"].Value);
            if (location is ServingLocation.Feed or ServingLocation.Email)
            {
                return $"<a href=\"https://www.youtube.com/watch?v={id}\">https://www.youtube.com/watch?v={id}</a>";
            }

            // youtube-nocookie: YouTube non salva cookie finché il visitatore non avvia il video.
            return "<span class=\"youtube-video\" style=\"display:block;position:relative;max-width:560px;margin:0 auto;aspect-ratio:16/9\">"
                + $"<iframe src=\"https://www.youtube-nocookie.com/embed/{id}\" title=\"YouTube\" loading=\"lazy\" "
                + "style=\"position:absolute;inset:0;width:100%;height:100%;border:0\" "
                + "allow=\"accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture\" "
                + "referrerpolicy=\"strict-origin-when-cross-origin\" allowfullscreen></iframe></span>";
        });
    }
}

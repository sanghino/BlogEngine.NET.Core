// Video di YouTube: il codice [youtube:ID] nel testo di post e pagine (usato dalla vecchia estensione
// di BlogEngine.NET) diventa il riquadro del video. Lo stesso vale per i vecchi lettori Flash
// (<object>/<embed> con youtube.com/v/ID), che i browser non eseguono più.
// Nei feed e nelle email resta un collegamento.

using System.Net;
using System.Text.RegularExpressions;

[Extension("YouTube videos: [youtube:ID] and old Flash players", "4.0", "ExtraGeek-BlogEngine")]
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

    // Vecchio codice di incorporamento Flash: <object ...><param name="movie" value="http://www.youtube.com/v/ID&..." />...</object>.
    private static readonly Regex FlashObjectRegex = new(
        @"<object\b(?:(?!</object>).)*?youtube(?:-nocookie)?\.com/v/(?<id>[A-Za-z0-9_-]{6,20})(?:(?!</object>).)*</object>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // <embed src="http://www.youtube.com/v/ID..."> senza <object> intorno.
    private static readonly Regex FlashEmbedRegex = new(
        @"<embed\b[^>]*youtube(?:-nocookie)?\.com/v/(?<id>[A-Za-z0-9_-]{6,20})[^>]*>(?:\s*</embed>)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Render(string body, ServingLocation location)
    {
        if (string.IsNullOrEmpty(body) || body.IndexOf("youtube", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return body;
        }

        string Replace(Match match) => Embed(match.Groups["id"].Value, location);
        body = ShortCodeRegex.Replace(body, Replace);
        body = FlashObjectRegex.Replace(body, Replace);
        return FlashEmbedRegex.Replace(body, Replace);
    }

    private static string Embed(string videoId, ServingLocation location)
    {
        var id = WebUtility.HtmlEncode(videoId);
        if (location is ServingLocation.Feed or ServingLocation.Email)
        {
            return $"<a href=\"https://www.youtube.com/watch?v={id}\">https://www.youtube.com/watch?v={id}</a>";
        }

        // youtube-nocookie: YouTube non salva cookie finché il visitatore non avvia il video.
        // Niente loading="lazy": con il riquadro posizionato in questo modo alcuni browser non caricano mai il video.
        // margin:0 annulla il margine che alcuni temi danno ai video, che farebbe uscire il riquadro dal suo spazio.
        return "<span class=\"youtube-video\" style=\"display:block;position:relative;max-width:560px;margin:0 auto;aspect-ratio:16/9\">"
            + $"<iframe src=\"https://www.youtube-nocookie.com/embed/{id}\" title=\"YouTube\" "
            + "style=\"position:absolute;inset:0;width:100%;height:100%;margin:0;border:0\" "
            + "allow=\"accelerometer; clipboard-write; encrypted-media; gyroscope; picture-in-picture\" "
            + "referrerpolicy=\"strict-origin-when-cross-origin\" allowfullscreen></iframe></span>";
    }
}

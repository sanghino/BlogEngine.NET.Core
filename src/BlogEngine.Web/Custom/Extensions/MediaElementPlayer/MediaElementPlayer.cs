// Lettore HTML5 per video e audio: i codici [video src="file.mp4"] e [audio src="file.mp3"] nel testo
// di post e pagine diventano un lettore MediaElement.js.
// Porting dell'estensione MediaElementPlayer di John Dyer (http://johndyer.name/).

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

[Extension("HTML5 Video/Audio Player", "4.0", "<a href=\"http://johndyer.me/\">John Dyer</a>")]
public sealed class MediaElementPlayer : IBlogExtension
{
    public const int DefaultWidth = 480;
    public const int DefaultHeight = 360;

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IEventHandler<ServingEvent<Post>>, MediaElementHandler>();
        services.AddScoped<IEventHandler<ServingEvent<Page>>, MediaElementHandler>();
    }

    public IEnumerable<ExtensionSettingsDefinition> Settings =>
    [
        new ExtensionSettingsDefinition(nameof(MediaElementPlayer))
        {
            IsScalar = true,
            Help = """
                <p>Built on <a href="https://www.mediaelementjs.com/" target="_blank">MediaElement.js</a>, the HTML5 video/audio player.</p>
                <ol>
                    <li>Upload media files from the post editor (they are stored in the blog's <b>media</b> folder),
                        or use a full address (https://...).</li>
                    <li>Add short code to your post: [video src="myfile.mp4"] for video and [audio src="myfile.mp3"] for audio.</li>
                    <li>Optional parameters: <b>width</b>, <b>height</b>, <b>autoplay</b>, <b>poster</b>, <b>preload</b>,
                        and alternative formats <b>mp4</b>, <b>webm</b>, <b>ogg</b>, <b>mp3</b>.</li>
                </ol>
                <p>A complete example: [video mp4="myfile.mp4" webm="myfile.webm" width="480" height="360"]</p>
                """,
        }
            .AddParameter("width", "Default Width", 5, type: ExtensionParameterType.Integer)
            .AddParameter("height", "Default Height", 5, type: ExtensionParameterType.Integer)
            .SetDefault("width", DefaultWidth)
            .SetDefault("height", DefaultHeight),
    ];
}

public sealed class MediaElementHandler(IExtensionSettingsService settings, IPageResources page, ILinkGenerator links)
    : IEventHandler<ServingEvent<Post>>, IEventHandler<ServingEvent<Page>>
{
    private static readonly Regex ShortCodeRegex = new(@"(<p>[\s\n]?)*\[(?<tag>video|audio)(?<attrs>[^\]]+)\]([\s\n]?</p>)*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AttributeRegex = new(@"(?<attr>\w+)=""?(?<val>[^""\s\]]+)""?", RegexOptions.Compiled);

    public Task HandleAsync(ServingEvent<Post> blogEvent, CancellationToken cancellationToken) =>
        HandleAsync(blogEvent.Body, blogEvent.Location, body => blogEvent.Body = body, cancellationToken);

    public Task HandleAsync(ServingEvent<Page> blogEvent, CancellationToken cancellationToken) =>
        HandleAsync(blogEvent.Body, blogEvent.Location, body => blogEvent.Body = body, cancellationToken);

    private async Task HandleAsync(string body, ServingLocation location, Action<string> update, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(body) || !body.Contains('[') || !ShortCodeRegex.IsMatch(body))
        {
            return;
        }

        // Nei feed servono indirizzi completi: il lettore viene sostituito dal semplice tag HTML5.
        var mediaRoot = links.BlogRoot + "media/";
        if (location is ServingLocation.Feed or ServingLocation.Email)
        {
            mediaRoot = links.ToAbsolute(mediaRoot).ToString();
        }

        update(ShortCodeRegex.Replace(body, match => Render(match, mediaRoot)));

        if (location is ServingLocation.PostList or ServingLocation.SinglePost or ServingLocation.SinglePage)
        {
            var values = await settings.GetAsync(nameof(MediaElementPlayer), cancellationToken: cancellationToken);
            var path = page.ApplicationRoot + "Scripts/mediaelement/";
            page.AddStylesheet(path + "mediaelementplayer.min.css");
            page.AddScript(path + "mediaelement.min.js");
            page.AddScript(path + "mediaelementplayer.min.js");
            page.AddFooterHtml($$"""
                <script>
                    $(function () {
                        $('audio.mep, video.mep').mediaelementplayer({ defaultVideoWidth: {{values.GetInt32("width", MediaElementPlayer.DefaultWidth)}}, defaultVideoHeight: {{values.GetInt32("height", MediaElementPlayer.DefaultHeight)}} });
                    });
                </script>
                """);
        }
    }

    private static string Render(Match match, string mediaRoot)
    {
        var tag = match.Groups["tag"].Value.ToLowerInvariant();
        var attributes = AttributeRegex.Matches(match.Groups["attrs"].Value)
            .GroupBy(m => m.Groups["attr"].Value.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First().Groups["val"].Value);

        string Url(string file) => WebUtility.HtmlEncode(file.StartsWith("http", StringComparison.OrdinalIgnoreCase) || file.StartsWith('/') ? file : mediaRoot + file);
        string Attribute(string name) => attributes.TryGetValue(name, out var value) ? WebUtility.HtmlEncode(value) : string.Empty;

        var html = new StringBuilder($"<{tag} class=\"mep\" controls=\"controls\"");
        if (attributes.TryGetValue("src", out var src))
        {
            html.Append($" src=\"{Url(src)}\"");
        }

        if (attributes.TryGetValue("poster", out var poster))
        {
            html.Append($" poster=\"{Url(poster)}\"");
        }

        foreach (var size in new[] { "width", "height" })
        {
            if (int.TryParse(Attribute(size), out var pixels))
            {
                html.Append($" {size}=\"{pixels}\"");
            }
        }

        if (attributes.ContainsKey("autoplay"))
        {
            html.Append(" autoplay=\"autoplay\"");
        }

        if (attributes.ContainsKey("preload"))
        {
            html.Append($" preload=\"{Attribute("preload")}\"");
        }

        html.Append('>');
        foreach (var (format, type) in new[] { ("mp4", "video/mp4"), ("mp3", "audio/mp3"), ("webm", "video/webm"), ("ogg", "video/ogg") })
        {
            if (attributes.TryGetValue(format, out var file))
            {
                html.Append($"<source src=\"{Url(file)}\" type=\"{type}\" />");
            }
        }

        return html.Append($"</{tag}>").ToString();
    }
}

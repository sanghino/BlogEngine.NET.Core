// Gallerie di immagini delle vecchie pagine di BlogEngine.NET: il codice
// [usercontrol: /user controls/gallery.ascx ImageUrl=/public/gallery/nome;RepeatColumns=4;Width=160]
// diventa una griglia con le immagini della cartella indicata, presa tra i file del blog
// (App_Data/files/<blog>/public/gallery/nome). Se esiste una sottocartella "thumbs" con le miniature
// dallo stesso nome, la griglia usa quelle. Se la cartella non c'è o è vuota il codice sparisce.

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BlogEngine.Core.Files;

[Extension("Image galleries of old pages: [usercontrol: ... gallery.ascx ImageUrl=...]", "4.0", "ExtraGeek-BlogEngine")]
public sealed class Gallery : IBlogExtension
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddScoped<IEventHandler<ServingEvent<Post>>, GalleryHandler>();
        services.AddScoped<IEventHandler<ServingEvent<Page>>, GalleryHandler>();
    }

    public IEnumerable<ExtensionSettingsDefinition> Settings => [];
}

public sealed class GalleryHandler(FileStore files, IBlogContext context, ILinkGenerator links)
    : IEventHandler<ServingEvent<Post>>, IEventHandler<ServingEvent<Page>>
{
    public const int DefaultColumns = 4;
    public const int DefaultWidth = 160;
    public const string ThumbnailsFolder = "thumbs";

    private static readonly Regex ShortCodeRegex = new(
        @"\[usercontrol:\s*[^\]]*?gallery\.ascx(?<params>[^\]]*)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

    private string Render(string body, ServingLocation location)
    {
        if (string.IsNullOrEmpty(body) || body.IndexOf("gallery.ascx", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return body;
        }

        var absolute = location is ServingLocation.Feed or ServingLocation.Email;
        return ShortCodeRegex.Replace(body, match => RenderGallery(Parameters(match.Groups["params"].Value), absolute));
    }

    /// <summary>"ImageUrl=/a/b;RepeatColumns=4;Width=160" diventa un dizionario senza distinzione di maiuscole.</summary>
    private static Dictionary<string, string> Parameters(string text) =>
        text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split('=', 2, StringSplitOptions.TrimEntries))
            .Where(p => p.Length == 2 && p[0].Length > 0)
            .GroupBy(p => p[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First()[1], StringComparer.OrdinalIgnoreCase);

    private string RenderGallery(Dictionary<string, string> parameters, bool absolute)
    {
        if (!parameters.TryGetValue("ImageUrl", out var folder) || string.IsNullOrWhiteSpace(folder))
        {
            return string.Empty;
        }

        IReadOnlyList<FileEntry> entries;
        try
        {
            entries = files.List(context.Blog.Id, folder);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }

        var images = entries.Where(e => e.IsImage).ToList();
        if (images.Count == 0)
        {
            return string.Empty;
        }

        var thumbnails = entries.Any(e => e.IsDirectory && e.Name.Equals(ThumbnailsFolder, StringComparison.OrdinalIgnoreCase))
            ? files.List(context.Blog.Id, folder.TrimEnd('/') + "/" + ThumbnailsFolder).Where(e => e.IsImage)
                .ToDictionary(e => e.Name, e => e.RelativePath, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var columns = Number(parameters, "RepeatColumns", DefaultColumns, 1, 12);
        var width = Number(parameters, "Width", DefaultWidth, 40, 800);
        const int gap = 8;

        string Url(string relativePath)
        {
            var url = links.BlogRoot + "image.axd?picture=" + Uri.EscapeDataString(relativePath).Replace("%2F", "/");
            return WebUtility.HtmlEncode(absolute ? links.ToAbsolute(url).ToString() : url);
        }

        var html = new StringBuilder();
        html.Append($"<span class=\"image-gallery\" style=\"display:grid;gap:{gap}px;max-width:{columns * (width + gap) - gap}px;")
            .Append($"grid-template-columns:repeat(auto-fill,minmax(min({width * 3 / 4}px,100%),1fr));margin:0 auto\">");
        foreach (var image in images)
        {
            var thumbnail = thumbnails.TryGetValue(image.Name, out var small) ? small : image.RelativePath;
            var title = WebUtility.HtmlEncode(Path.GetFileNameWithoutExtension(image.Name).Replace('_', ' ').Replace('-', ' '));
            html.Append($"<a href=\"{Url(image.RelativePath)}\" target=\"_blank\" title=\"{title}\">")
                .Append($"<img src=\"{Url(thumbnail)}\" alt=\"{title}\" loading=\"lazy\" ")
                .Append("style=\"display:block;width:100%;aspect-ratio:1;object-fit:cover;border-radius:3px\" /></a>");
        }

        return html.Append("</span>").ToString();
    }

    private static int Number(Dictionary<string, string> parameters, string name, int fallback, int min, int max) =>
        parameters.TryGetValue(name, out var text) && int.TryParse(text, out var value) ? Math.Clamp(value, min, max) : fallback;
}

using System.Globalization;
using System.Text;
using System.Xml;

namespace BlogEngine.Web.Protocols;

/// <summary>Una voce del feed: un post, una pagina o un commento.</summary>
public sealed class FeedItem
{
    public required string Title { get; init; }

    public required string Link { get; init; }

    /// <summary>Indirizzo permanente (guid RSS / id Atom).</summary>
    public required string PermaLink { get; init; }

    /// <summary>Testo HTML con indirizzi assoluti.</summary>
    public string Content { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public DateTime PublishedUtc { get; init; }

    public DateTime UpdatedUtc { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = [];

    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Solo per i post: commenti, feed dei commenti e trackback.</summary>
    public string? CommentsLink { get; init; }

    public string? CommentsFeed { get; init; }

    public int? CommentCount { get; init; }

    public string? TrackbackLink { get; init; }

    /// <summary>File audio o video allegato (podcast), se le impostazioni lo prevedono.</summary>
    public (string Url, long Length, string Type)? Enclosure { get; init; }
}

/// <summary>Dati del canale.</summary>
public sealed class FeedChannel
{
    public required string Title { get; init; }

    public string Description { get; init; } = string.Empty;

    public required string Link { get; init; }

    public required string SelfLink { get; init; }

    public string Language { get; init; } = string.Empty;

    public string AuthorName { get; init; } = string.Empty;

    /// <summary>Autore/indirizzo per l'elemento &lt;author&gt; delle voci RSS (impostazione FeedAuthor).</summary>
    public string FeedAuthor { get; init; } = string.Empty;

    public string PingbackServer { get; init; } = string.Empty;

    public string Endorsement { get; init; } = string.Empty;

    public float Latitude { get; init; }

    public float Longitude { get; init; }

    public string Generator { get; init; } = string.Empty;
}

/// <summary>
/// Scrive feed RSS 2.0 e Atom 1.0 con le stesse estensioni della versione originale
/// (porting di SyndicationGenerator): Dublin Core, slash, wfw, pingback, trackback, geo e betag.
/// </summary>
public static class FeedWriter
{
    private const string Dc = "http://purl.org/dc/elements/1.1/";
    private const string Pingback = "http://madskills.com/public/xml/rss/module/pingback/";
    private const string Trackback = "http://madskills.com/public/xml/rss/module/trackback/";
    private const string Wfw = "http://wellformedweb.org/CommentAPI/";
    private const string Slash = "http://purl.org/rss/1.0/modules/slash/";
    private const string Geo = "http://www.w3.org/2003/01/geo/wgs84_pos#";
    private const string BeTag = "https://blogengine.io/schemas/tags";
    private const string BlogChannel = "http://backend.userland.com/blogChannelModule";

    private static readonly Dictionary<string, string> Namespaces = new()
    {
        ["blogChannel"] = BlogChannel,
        ["dc"] = Dc,
        ["pingback"] = Pingback,
        ["trackback"] = Trackback,
        ["wfw"] = Wfw,
        ["slash"] = Slash,
        ["geo"] = Geo,
        ["betag"] = BeTag,
    };

    public static string Rss(FeedChannel channel, IEnumerable<FeedItem> items) => Write(writer =>
    {
        writer.WriteStartElement("rss");
        writer.WriteAttributeString("version", "2.0");
        WriteNamespaces(writer);
        writer.WriteStartElement("channel");
        writer.WriteElementString("title", channel.Title);
        writer.WriteElementString("description", channel.Description);
        writer.WriteElementString("link", channel.Link);
        writer.WriteElementString("docs", "http://www.rssboard.org/rss-specification");
        writer.WriteElementString("generator", channel.Generator);
        if (!string.IsNullOrEmpty(channel.Language))
        {
            writer.WriteElementString("language", channel.Language);
        }

        if (!string.IsNullOrEmpty(channel.Endorsement))
        {
            writer.WriteElementString("blogChannel", "blogRoll", BlogChannel, channel.Endorsement);
        }

        if (!string.IsNullOrEmpty(channel.AuthorName))
        {
            writer.WriteElementString("dc", "creator", Dc, channel.AuthorName);
        }

        writer.WriteElementString("dc", "title", Dc, channel.Title);
        WriteGeo(writer, channel);

        foreach (var item in items)
        {
            writer.WriteStartElement("item");
            writer.WriteElementString("title", item.Title);
            writer.WriteElementString("description", item.Content);
            writer.WriteElementString("link", item.Link);
            if (item.Enclosure is { } enclosure)
            {
                writer.WriteStartElement("enclosure");
                writer.WriteAttributeString("url", enclosure.Url);
                writer.WriteAttributeString("length", enclosure.Length.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("type", enclosure.Type);
                writer.WriteEndElement();
            }

            if (!string.IsNullOrEmpty(channel.FeedAuthor))
            {
                writer.WriteElementString("author", channel.FeedAuthor);
            }

            if (item.CommentsLink is not null)
            {
                writer.WriteElementString("comments", item.CommentsLink);
            }

            writer.WriteElementString("guid", item.PermaLink);
            writer.WriteElementString("pubDate", item.PublishedUtc.ToString("r", CultureInfo.InvariantCulture));
            foreach (var category in item.Categories)
            {
                writer.WriteElementString("category", category);
            }

            WriteItemExtensions(writer, channel, item);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    });

    public static string Atom(FeedChannel channel, IEnumerable<FeedItem> items)
    {
        var list = items.ToList();
        return Write(writer =>
        {
            writer.WriteStartElement("feed", "http://www.w3.org/2005/Atom");
            WriteNamespaces(writer);
            writer.WriteElementString("id", channel.Link);
            writer.WriteElementString("title", channel.Title);
            writer.WriteElementString("updated", W3C(list.Count == 0 ? DateTime.UtcNow : list.Max(i => i.UpdatedUtc)));
            writer.WriteStartElement("link");
            writer.WriteAttributeString("href", channel.Link);
            writer.WriteEndElement();
            writer.WriteStartElement("link");
            writer.WriteAttributeString("rel", "self");
            writer.WriteAttributeString("href", channel.SelfLink);
            writer.WriteEndElement();
            if (!string.IsNullOrEmpty(channel.Description))
            {
                writer.WriteElementString("subtitle", channel.Description);
            }

            writer.WriteStartElement("author");
            writer.WriteElementString("name", string.IsNullOrEmpty(channel.AuthorName) ? channel.Title : channel.AuthorName);
            writer.WriteEndElement();
            writer.WriteStartElement("generator");
            writer.WriteAttributeString("uri", "https://blogengine.io/");
            writer.WriteString(channel.Generator);
            writer.WriteEndElement();
            if (!string.IsNullOrEmpty(channel.Language))
            {
                writer.WriteElementString("dc", "language", Dc, channel.Language);
            }

            WriteGeo(writer, channel);

            foreach (var item in list)
            {
                writer.WriteStartElement("entry");
                writer.WriteElementString("id", item.PermaLink);
                writer.WriteElementString("title", item.Title);
                writer.WriteElementString("updated", W3C(item.UpdatedUtc));
                writer.WriteElementString("published", W3C(item.PublishedUtc));
                writer.WriteStartElement("link");
                writer.WriteAttributeString("href", item.Link);
                writer.WriteEndElement();
                if (item.CommentsLink is not null)
                {
                    writer.WriteStartElement("link");
                    writer.WriteAttributeString("rel", "related");
                    writer.WriteAttributeString("href", item.CommentsLink);
                    writer.WriteEndElement();
                }

                if (item.Enclosure is { } enclosure)
                {
                    writer.WriteStartElement("link");
                    writer.WriteAttributeString("rel", "enclosure");
                    writer.WriteAttributeString("href", enclosure.Url);
                    writer.WriteAttributeString("length", enclosure.Length.ToString(CultureInfo.InvariantCulture));
                    writer.WriteAttributeString("type", enclosure.Type);
                    writer.WriteEndElement();
                }

                writer.WriteStartElement("author");
                writer.WriteElementString("name", item.Author);
                writer.WriteEndElement();
                writer.WriteStartElement("summary");
                writer.WriteAttributeString("type", "html");
                writer.WriteString(item.Content);
                writer.WriteEndElement();
                foreach (var category in item.Categories)
                {
                    writer.WriteStartElement("category");
                    writer.WriteAttributeString("term", category);
                    writer.WriteEndElement();
                }

                WriteItemExtensions(writer, channel, item);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        });
    }

    private static void WriteItemExtensions(XmlWriter writer, FeedChannel channel, FeedItem item)
    {
        foreach (var tag in item.Tags)
        {
            writer.WriteElementString("betag", "tag", BeTag, tag);
        }

        if (!string.IsNullOrEmpty(item.Author))
        {
            writer.WriteElementString("dc", "publisher", Dc, item.Author);
        }

        if (!string.IsNullOrEmpty(channel.PingbackServer))
        {
            writer.WriteElementString("pingback", "server", Pingback, channel.PingbackServer);
            writer.WriteElementString("pingback", "target", Pingback, item.PermaLink);
        }

        if (item.CommentCount is { } count)
        {
            writer.WriteElementString("slash", "comments", Slash, count.ToString(CultureInfo.InvariantCulture));
        }

        if (item.TrackbackLink is not null)
        {
            writer.WriteElementString("trackback", "ping", Trackback, item.TrackbackLink);
        }

        if (item.CommentsLink is not null)
        {
            writer.WriteElementString("wfw", "comment", Wfw, item.CommentsLink);
        }

        if (item.CommentsFeed is not null)
        {
            writer.WriteElementString("wfw", "commentRss", Wfw, item.CommentsFeed);
        }
    }

    private static void WriteGeo(XmlWriter writer, FeedChannel channel)
    {
        if (channel.Latitude != 0 || channel.Longitude != 0)
        {
            writer.WriteElementString("geo", "lat", Geo, channel.Latitude.ToString("0.000000", CultureInfo.InvariantCulture));
            writer.WriteElementString("geo", "long", Geo, channel.Longitude.ToString("0.000000", CultureInfo.InvariantCulture));
        }
    }

    private static void WriteNamespaces(XmlWriter writer)
    {
        foreach (var (prefix, uri) in Namespaces)
        {
            writer.WriteAttributeString("xmlns", prefix, null, uri);
        }
    }

    private static string W3C(DateTime utc) => utc.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    /// <summary>XML in UTF-8, con rientri, scritto in memoria.</summary>
    internal static string Write(Action<XmlWriter> write)
    {
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(new Utf8StringWriter(builder), new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
        {
            writer.WriteStartDocument();
            write(writer);
            writer.WriteEndDocument();
        }

        return builder.ToString();
    }

    private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}

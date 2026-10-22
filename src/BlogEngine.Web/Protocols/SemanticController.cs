using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using BlogEngine.Security;
using BlogEngine.Services;
using BlogEngine.Web.Admin;
using Microsoft.AspNetCore.Mvc;

namespace BlogEngine.Web.Protocols;

/// <summary>
/// Documenti per motori di ricerca, lettori e programmi di scrittura, agli indirizzi della versione
/// originale: sitemap.axd, rsd.axd, opensearch.axd, opml.axd, apml.axd, foaf.axd, sioc.axd e robots.txt.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class SemanticController(
    IPostService posts,
    IPageService pages,
    IBlogRollService blogRoll,
    IProfileService profiles,
    ISettingsService settings,
    IRightsService rights,
    ILinkGenerator links,
    IBlogClock clock) : Controller
{
    private const string Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
    private const string Rdfs = "http://www.w3.org/2000/01/rdf-schema#";
    private const string Foaf = "http://xmlns.com/foaf/0.1/";
    private const string Sioc = "http://rdfs.org/sioc/ns#";
    private const string Dc = "http://purl.org/dc/elements/1.1/";
    private const string Dcterms = "http://purl.org/dc/terms/";

    private string Root => links.ToAbsolute(links.BlogRoot).ToString();

    /// <summary>Mappa del sito per i motori di ricerca (porting di SiteMap).</summary>
    [HttpGet("/sitemap.axd")]
    public async Task<IActionResult> SiteMap()
    {
        if (!await rights.IsAuthorizedAsync(Rights.ViewPublicPosts))
        {
            return Unauthorized();
        }

        var urls = new List<(string Loc, DateTime Modified, string Frequency)>
        {
            (Root, clock.UtcNow, "daily"),
            (links.ToAbsolute(links.Contact()).ToString(), clock.UtcNow, "monthly"),
        };

        foreach (var post in (await posts.GetVisiblePostsAsync()).Where(p => p.IsPublished))
        {
            urls.Add((links.ToAbsolute(await links.PostAsync(post)).ToString(), post.DateModified, "monthly"));
        }

        foreach (var page in (await pages.GetVisiblePagesAsync()).Where(p => p.IsPublished))
        {
            urls.Add((links.ToAbsolute(await links.PageAsync(page)).ToString(), page.DateModified, "monthly"));
        }

        return Xml(FeedWriter.Write(writer =>
        {
            writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            foreach (var (loc, modified, frequency) in urls)
            {
                writer.WriteStartElement("url");
                writer.WriteElementString("loc", loc);
                writer.WriteElementString("lastmod", modified.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                writer.WriteElementString("changefreq", frequency);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }));
    }

    /// <summary>Really Simple Discovery: dice ai programmi di scrittura dove si trova l'API MetaWeblog.</summary>
    [HttpGet("/rsd.axd")]
    public IActionResult Rsd() => Xml(FeedWriter.Write(writer =>
    {
        writer.WriteStartElement("rsd", "http://archipelago.phrasewise.com/rsd");
        writer.WriteAttributeString("version", "1.0");
        writer.WriteStartElement("service");
        writer.WriteElementString("engineName", AdminViewContext.Version);
        writer.WriteElementString("engineLink", "https://blogengine.io");
        writer.WriteElementString("homePageLink", Root);
        writer.WriteStartElement("apis");
        writer.WriteStartElement("api");
        writer.WriteAttributeString("name", "MetaWeblog");
        writer.WriteAttributeString("preferred", "true");
        writer.WriteAttributeString("apiLink", $"{Root}metaweblog.axd");
        writer.WriteAttributeString("blogID", Root);
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }));

    /// <summary>Ricerca del blog dalla barra del browser.</summary>
    [HttpGet("/opensearch.axd")]
    public async Task<IActionResult> OpenSearch()
    {
        var blogSettings = await settings.GetAsync();
        if (!blogSettings.EnableOpenSearch)
        {
            return NotFound();
        }

        return Content(FeedWriter.Write(writer =>
        {
            writer.WriteStartElement("OpenSearchDescription", "http://a9.com/-/spec/opensearch/1.1/");
            writer.WriteElementString("ShortName", blogSettings.Name.Length > 16 ? blogSettings.Name[..16] : blogSettings.Name);
            writer.WriteElementString("Description", string.IsNullOrEmpty(blogSettings.Description) ? blogSettings.Name : blogSettings.Description);
            writer.WriteStartElement("Image");
            writer.WriteAttributeString("height", "16");
            writer.WriteAttributeString("width", "16");
            writer.WriteAttributeString("type", "image/vnd.microsoft.icon");
            writer.WriteString(links.ToAbsolute("/Content/images/blog/blogengine.ico").ToString());
            writer.WriteEndElement();
            writer.WriteStartElement("Url");
            writer.WriteAttributeString("type", "text/html");
            writer.WriteAttributeString("template", $"{Root}search?q={{searchTerms}}");
            writer.WriteEndElement();
            writer.WriteStartElement("Url");
            writer.WriteAttributeString("type", "application/rss+xml");
            writer.WriteAttributeString("template", $"{Root}syndication.axd?q={{searchTerms}}");
            writer.WriteEndElement();
            writer.WriteEndElement();
        }), "application/opensearchdescription+xml; charset=utf-8");
    }

    /// <summary>Blogroll in formato OPML, importabile nei lettori di feed.</summary>
    [HttpGet("/opml.axd")]
    public async Task<IActionResult> Opml()
    {
        var blogSettings = await settings.GetAsync();
        var items = await blogRoll.GetItemsAsync();
        return Xml(FeedWriter.Write(writer =>
        {
            writer.WriteStartElement("opml");
            writer.WriteAttributeString("version", "1.0");
            writer.WriteStartElement("head");
            writer.WriteElementString("title", blogSettings.Name);
            writer.WriteEndElement();
            writer.WriteStartElement("body");
            foreach (var item in items.OrderBy(i => i.SortIndex))
            {
                writer.WriteStartElement("outline");
                writer.WriteAttributeString("type", "rss");
                writer.WriteAttributeString("text", item.Title);
                writer.WriteAttributeString("title", item.Title);
                writer.WriteAttributeString("description", item.Description);
                writer.WriteAttributeString("xfn", item.Xfn);
                writer.WriteAttributeString("xmlUrl", item.FeedUrl?.ToString() ?? string.Empty);
                writer.WriteAttributeString("htmlUrl", item.BlogUrl?.ToString() ?? string.Empty);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }));
    }

    /// <summary>Interessi del blog (tag più usati) e siti seguiti, in formato APML.</summary>
    [HttpGet("/apml.axd")]
    public async Task<IActionResult> Apml()
    {
        var blogSettings = await settings.GetAsync();
        var visible = await posts.GetVisiblePostsAsync();
        var tags = visible.SelectMany(p => p.Tags.Select(t => (Tag: t, p.DateModified)))
            .GroupBy(t => t.Tag, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Title: g.Key, Count: g.Count(), Updated: g.Max(t => t.DateModified)))
            .ToList();
        var max = tags.Count == 0 ? 1 : tags.Max(t => t.Count);
        var blogs = await blogRoll.GetItemsAsync();
        var now = clock.UtcNow.ToString("s", CultureInfo.InvariantCulture);

        return Xml(FeedWriter.Write(writer =>
        {
            writer.WriteStartElement("APML", "http://www.apml.org/apml-0.6");
            writer.WriteAttributeString("version", "0.6");
            writer.WriteStartElement("Head");
            writer.WriteElementString("Title", $"APML data for {blogSettings.Name}");
            writer.WriteElementString("Generator", AdminViewContext.Version);
            writer.WriteElementString("DateCreated", now);
            writer.WriteEndElement();
            writer.WriteStartElement("Body");
            writer.WriteAttributeString("defaultProfile", "tags");
            writer.WriteStartElement("Profile");
            writer.WriteAttributeString("name", "tags");
            writer.WriteStartElement("ImplicitData");
            writer.WriteStartElement("Concepts");
            foreach (var tag in tags.OrderByDescending(t => t.Count))
            {
                writer.WriteStartElement("Concept");
                writer.WriteAttributeString("key", tag.Title);
                writer.WriteAttributeString("value", (tag.Count / (double)max).ToString("0.00", CultureInfo.InvariantCulture));
                writer.WriteAttributeString("from", Root);
                writer.WriteAttributeString("updated", tag.Updated.ToString("s", CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteStartElement("Profile");
            writer.WriteAttributeString("name", "links");
            writer.WriteStartElement("ExplicitData");
            writer.WriteStartElement("Concepts");
            foreach (var blog in blogs.Where(b => b.FeedUrl is not null))
            {
                writer.WriteStartElement("Source");
                writer.WriteAttributeString("key", blog.FeedUrl!.ToString());
                writer.WriteAttributeString("name", blog.Title);
                writer.WriteAttributeString("value", "1.0");
                writer.WriteAttributeString("type", "application/rss+xml");
                writer.WriteAttributeString("from", Root);
                writer.WriteAttributeString("updated", now);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndElement();
        }));
    }

    /// <summary>
    /// Profilo FOAF degli autori (porting di Foaf). foaf.axd descrive l'autore principale del blog,
    /// foaf_nome.axd un autore specifico; i blog del blogroll con relazione "friend" o "met" sono conoscenti.
    /// </summary>
    [HttpGet("/foaf.axd")]
    [HttpGet("/foaf_{name}.axd")]
    public async Task<IActionResult> FoafDocument(string? name)
    {
        var blogSettings = await settings.GetAsync();
        var all = (await profiles.GetProfilesAsync()).Where(p => !p.Private).ToList();
        var person = string.IsNullOrEmpty(name)
            ? all.FirstOrDefault(p => string.Equals(p.UserName, blogSettings.AuthorName, StringComparison.OrdinalIgnoreCase)) ?? all.FirstOrDefault()
            : all.FirstOrDefault(p => string.Equals(p.UserName, name, StringComparison.OrdinalIgnoreCase));
        if (person is null && !string.IsNullOrEmpty(name))
        {
            return NotFound();
        }

        var friends = (await blogRoll.GetItemsAsync()).Where(b => b.Xfn.Contains("friend", StringComparison.OrdinalIgnoreCase) || b.Xfn.Contains("met", StringComparison.OrdinalIgnoreCase)).ToList();
        var displayName = person is null ? blogSettings.AuthorName : (string.IsNullOrWhiteSpace(person.DisplayName) ? person.UserName : person.DisplayName);

        return Content(FeedWriter.Write(writer =>
        {
            writer.WriteStartElement("rdf", "RDF", Rdf);
            writer.WriteAttributeString("xmlns", "rdfs", null, Rdfs);
            writer.WriteAttributeString("xmlns", "foaf", null, Foaf);
            writer.WriteStartElement("foaf", "PersonalProfileDocument", Foaf);
            writer.WriteAttributeString("rdf", "about", Rdf, string.Empty);
            Resource(writer, "foaf", "maker", Foaf, "#me");
            Resource(writer, "foaf", "primaryTopic", Foaf, "#me");
            writer.WriteEndElement();

            writer.WriteStartElement("foaf", "Person", Foaf);
            writer.WriteAttributeString("rdf", "ID", Rdf, "me");
            writer.WriteElementString("foaf", "name", Foaf, displayName);
            if (person is not null)
            {
                Optional(writer, "givenname", person.FirstName);
                Optional(writer, "family_name", person.LastName);
                if (!string.IsNullOrWhiteSpace(person.EmailAddress))
                {
                    writer.WriteElementString("foaf", "mbox_sha1sum", Foaf, Sha1("mailto:" + person.EmailAddress.Trim()));
                }

                Optional(writer, "phone", person.PhoneMain);
                if (person.Birthday is { } birthday)
                {
                    writer.WriteElementString("foaf", "birthday", Foaf, birthday.ToString("MM-dd", CultureInfo.InvariantCulture));
                }

                if (!string.IsNullOrWhiteSpace(person.PhotoUrl))
                {
                    Resource(writer, "foaf", "depiction", Foaf, links.ToAbsolute(person.PhotoUrl.StartsWith('/') ? person.PhotoUrl : "/" + person.PhotoUrl).ToString());
                }
            }

            Resource(writer, "foaf", "weblog", Foaf, Root);
            foreach (var friend in friends.Where(f => f.BlogUrl is not null))
            {
                writer.WriteStartElement("foaf", "knows", Foaf);
                writer.WriteStartElement("foaf", "Person", Foaf);
                writer.WriteElementString("foaf", "name", Foaf, friend.Title);
                Resource(writer, "foaf", "weblog", Foaf, friend.BlogUrl!.ToString());
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
        }), "application/rdf+xml; charset=utf-8");
    }

    /// <summary>Descrizione SIOC del blog e degli ultimi post (porting di Sioc).</summary>
    [HttpGet("/sioc.axd")]
    public async Task<IActionResult> SiocDocument()
    {
        if (!await rights.IsAuthorizedAsync(Rights.ViewPublicPosts))
        {
            return Unauthorized();
        }

        var blogSettings = await settings.GetAsync();
        var recent = new List<(string Url, string Title, string Author, DateTime Created)>();
        foreach (var post in (await posts.GetVisiblePostsAsync()).OrderByDescending(p => p.DateCreated).Take(Math.Max(blogSettings.PostsPerFeed, 10)))
        {
            recent.Add((links.ToAbsolute(await links.PostAsync(post)).ToString(), post.Title, post.Author, post.DateCreated));
        }

        return Content(FeedWriter.Write(writer =>
        {
            writer.WriteStartElement("rdf", "RDF", Rdf);
            writer.WriteAttributeString("xmlns", "rdfs", null, Rdfs);
            writer.WriteAttributeString("xmlns", "foaf", null, Foaf);
            writer.WriteAttributeString("xmlns", "sioc", null, Sioc);
            writer.WriteAttributeString("xmlns", "dc", null, Dc);
            writer.WriteAttributeString("xmlns", "dcterms", null, Dcterms);

            writer.WriteStartElement("foaf", "Document", Foaf);
            writer.WriteAttributeString("rdf", "about", Rdf, $"{Root}sioc.axd");
            writer.WriteElementString("dc", "title", Dc, $"SIOC profile for \"{blogSettings.Name}\"");
            writer.WriteElementString("dc", "description", Dc, "A SIOC profile describes the structure and contents of a weblog in a machine readable form.");
            Resource(writer, "foaf", "primaryTopic", Foaf, Root);
            writer.WriteEndElement();

            writer.WriteStartElement("sioc", "Forum", Sioc);
            writer.WriteAttributeString("rdf", "about", Rdf, Root);
            writer.WriteElementString("sioc", "name", Sioc, blogSettings.Name);
            Resource(writer, "sioc", "link", Sioc, Root);
            foreach (var post in recent)
            {
                Resource(writer, "sioc", "container_of", Sioc, post.Url);
            }

            writer.WriteEndElement();

            foreach (var post in recent)
            {
                writer.WriteStartElement("sioc", "Post", Sioc);
                writer.WriteAttributeString("rdf", "about", Rdf, post.Url);
                writer.WriteElementString("dc", "title", Dc, post.Title);
                writer.WriteElementString("dcterms", "created", Dcterms, post.Created.ToString("s", CultureInfo.InvariantCulture) + "Z");
                Resource(writer, "sioc", "link", Sioc, post.Url);
                Resource(writer, "sioc", "has_container", Sioc, Root);
                writer.WriteElementString("sioc", "has_creator", Sioc, post.Author);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }), "application/rdf+xml; charset=utf-8");
    }

    /// <summary>Istruzioni per i motori di ricerca, con l'indirizzo della sitemap.</summary>
    [HttpGet("/robots.txt")]
    public IActionResult Robots() => Content(
        $"""
        User-agent: *
        Disallow: /account/
        Disallow: /admin/
        Disallow: /search
        Disallow: /error/

        Sitemap: {Root}sitemap.axd
        """,
        "text/plain; charset=utf-8");

    private ContentResult Xml(string xml) => Content(xml, "text/xml; charset=utf-8");

    private static void Resource(XmlWriter writer, string prefix, string name, string ns, string resource)
    {
        writer.WriteStartElement(prefix, name, ns);
        writer.WriteAttributeString("rdf", "resource", Rdf, resource);
        writer.WriteEndElement();
    }

    private static void Optional(XmlWriter writer, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            writer.WriteElementString("foaf", name, Foaf, value);
        }
    }

    private static string Sha1(string value) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

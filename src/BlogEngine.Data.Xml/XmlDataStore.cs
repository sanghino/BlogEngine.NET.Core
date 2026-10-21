using System.Globalization;
using System.Xml.Linq;
using BlogEngine.Core.Configuration;
using BlogEngine.Core.Data;
using BlogEngine.Models;

namespace BlogEngine.Data.Xml;

/// <summary>
/// Provider dati su file XML. Struttura nella cartella dati (App_Data/data):
/// <code>
/// blogs.xml
/// blogs/{idBlog}/posts/{idPost}.xml      un file per post, commenti inclusi
/// blogs/{idBlog}/pages/{idPagina}.xml    un file per pagina
/// blogs/{idBlog}/referrers/{aaaa-mm-gg}.xml
/// blogs/{idBlog}/datastore/{tipo}/{id}.json
/// blogs/{idBlog}/categories.xml, blogroll.xml, profiles.xml, settings.xml, rights.xml,
///   customfields.xml, users.xml, roles.xml, commentfilters.xml, pingservices.xml, stopwords.xml
/// </code>
/// </summary>
public sealed class XmlDataStore :
    IDataStoreInitializer,
    IBlogStore,
    IEntityStore<Post>,
    IEntityStore<Page>,
    IEntityStore<Category>,
    IEntityStore<BlogRollItem>,
    IEntityStore<Referrer>,
    IProfileStore,
    ISettingsStore,
    IRightsStore,
    ICustomFieldStore,
    IStringListStore,
    IExtensionDataStore,
    IUserStore,
    IRoleStore,
    ICommentFilterStore
{
    private readonly XmlFiles _files = new();
    private readonly string _root;

    public XmlDataStore(BlogEnginePaths paths)
    {
        _root = paths.Data;
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(Path.Combine(_root, "blogs"));
        return Task.CompletedTask;
    }

    // ---- Blog ----

    async Task<IReadOnlyList<Blog>> IBlogStore.GetAllAsync(CancellationToken cancellationToken) =>
        await ReadCollectionAsync(BlogsFile, "blog", XmlMappers.ToBlog, cancellationToken).ConfigureAwait(false);

    Task IBlogStore.SaveAsync(Blog blog, CancellationToken cancellationToken) =>
        UpsertAsync(BlogsFile, "blogs", "blog", XmlMappers.ToXml(blog), e => e.Guid("id") == blog.Id, cancellationToken);

    async Task IBlogStore.DeleteAsync(Guid blogId, CancellationToken cancellationToken)
    {
        await RemoveAsync(BlogsFile, "blogs", "blog", e => e.Guid("id") == blogId, cancellationToken).ConfigureAwait(false);
        var folder = BlogFolder(blogId);
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ---- Post e pagine: un file per elemento ----

    Task<IReadOnlyList<Post>> IEntityStore<Post>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        ReadFolderAsync(Path.Combine(BlogFolder(blogId), "posts"), XmlMappers.ToPost, cancellationToken);

    Task IEntityStore<Post>.SaveAsync(Post entity, CancellationToken cancellationToken) =>
        _files.SaveAsync(ItemFile(entity.BlogId, "posts", entity.Id), new XDocument(XmlMappers.ToXml(entity)), cancellationToken);

    Task IEntityStore<Post>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) =>
        _files.DeleteAsync(ItemFile(blogId, "posts", id), cancellationToken);

    Task<IReadOnlyList<Page>> IEntityStore<Page>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        ReadFolderAsync(Path.Combine(BlogFolder(blogId), "pages"), XmlMappers.ToPage, cancellationToken);

    Task IEntityStore<Page>.SaveAsync(Page entity, CancellationToken cancellationToken) =>
        _files.SaveAsync(ItemFile(entity.BlogId, "pages", entity.Id), new XDocument(XmlMappers.ToXml(entity)), cancellationToken);

    Task IEntityStore<Page>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) =>
        _files.DeleteAsync(ItemFile(blogId, "pages", id), cancellationToken);

    // ---- Categorie e blogroll: un file per blog ----

    async Task<IReadOnlyList<Category>> IEntityStore<Category>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        await ReadCollectionAsync(BlogFile(blogId, "categories.xml"), "category", XmlMappers.ToCategory, cancellationToken).ConfigureAwait(false);

    Task IEntityStore<Category>.SaveAsync(Category entity, CancellationToken cancellationToken) =>
        UpsertAsync(BlogFile(entity.BlogId, "categories.xml"), "categories", "category", XmlMappers.ToXml(entity), e => e.Guid("id") == entity.Id, cancellationToken);

    Task IEntityStore<Category>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) =>
        RemoveAsync(BlogFile(blogId, "categories.xml"), "categories", "category", e => e.Guid("id") == id, cancellationToken);

    async Task<IReadOnlyList<BlogRollItem>> IEntityStore<BlogRollItem>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        await ReadCollectionAsync(BlogFile(blogId, "blogroll.xml"), "item", XmlMappers.ToBlogRollItem, cancellationToken).ConfigureAwait(false);

    Task IEntityStore<BlogRollItem>.SaveAsync(BlogRollItem entity, CancellationToken cancellationToken) =>
        UpsertAsync(BlogFile(entity.BlogId, "blogroll.xml"), "blogroll", "item", XmlMappers.ToXml(entity), e => e.Guid("id") == entity.Id, cancellationToken);

    Task IEntityStore<BlogRollItem>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) =>
        RemoveAsync(BlogFile(blogId, "blogroll.xml"), "blogroll", "item", e => e.Guid("id") == id, cancellationToken);

    // ---- Referrer: un file per giorno, così ogni visita riscrive solo quello del giorno ----

    async Task<IReadOnlyList<Referrer>> IEntityStore<Referrer>.GetAllAsync(Guid blogId, CancellationToken cancellationToken)
    {
        var result = new List<Referrer>();
        foreach (var file in EnumerateXml(Path.Combine(BlogFolder(blogId), "referrers")))
        {
            result.AddRange(await ReadCollectionAsync(file, "referrer", XmlMappers.ToReferrer, cancellationToken).ConfigureAwait(false));
        }

        return result;
    }

    Task IEntityStore<Referrer>.SaveAsync(Referrer entity, CancellationToken cancellationToken) =>
        UpsertAsync(ReferrerFile(entity.BlogId, entity.Day), "referrers", "referrer", XmlMappers.ToXml(entity), e => e.Guid("id") == entity.Id, cancellationToken);

    async Task IEntityStore<Referrer>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken)
    {
        foreach (var file in EnumerateXml(Path.Combine(BlogFolder(blogId), "referrers")))
        {
            await RemoveAsync(file, "referrers", "referrer", e => e.Guid("id") == id, cancellationToken).ConfigureAwait(false);
        }
    }

    // ---- Profili ----

    async Task<IReadOnlyList<AuthorProfile>> IProfileStore.GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        await ReadCollectionAsync(BlogFile(blogId, "profiles.xml"), "profile", XmlMappers.ToProfile, cancellationToken).ConfigureAwait(false);

    Task IProfileStore.SaveAsync(AuthorProfile profile, CancellationToken cancellationToken) =>
        UpsertAsync(BlogFile(profile.BlogId, "profiles.xml"), "profiles", "profile", XmlMappers.ToXml(profile), e => SameName(e, "userName", profile.UserName), cancellationToken);

    Task IProfileStore.DeleteAsync(Guid blogId, string userName, CancellationToken cancellationToken) =>
        RemoveAsync(BlogFile(blogId, "profiles.xml"), "profiles", "profile", e => SameName(e, "userName", userName), cancellationToken);

    // ---- Impostazioni e permessi ----

    async Task<IReadOnlyDictionary<string, string>> ISettingsStore.LoadAsync(Guid blogId, CancellationToken cancellationToken)
    {
        var document = await _files.LoadAsync(BlogFile(blogId, "settings.xml"), cancellationToken).ConfigureAwait(false);
        return document?.Root?.Elements("setting")
                   .GroupBy(e => (string?)e.Attribute("name") ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                   .Where(g => g.Key.Length > 0)
                   .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.OrdinalIgnoreCase)
               ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    Task ISettingsStore.SaveAsync(Guid blogId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken) =>
        _files.SaveAsync(BlogFile(blogId, "settings.xml"), new XDocument(new XElement("settings",
            settings.OrderBy(s => s.Key, StringComparer.OrdinalIgnoreCase)
                .Select(s => new XElement("setting", new XAttribute("name", s.Key), s.Value)))), cancellationToken);

    async Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> IRightsStore.LoadAsync(Guid blogId, CancellationToken cancellationToken)
    {
        var document = await _files.LoadAsync(BlogFile(blogId, "rights.xml"), cancellationToken).ConfigureAwait(false);
        return document?.Root?.Elements("right")
                   .Where(e => !string.IsNullOrEmpty((string?)e.Attribute("name")))
                   .ToDictionary(e => (string)e.Attribute("name")!, e => (IReadOnlyCollection<string>)e.Elements("role").Select(r => r.Value).ToList())
               ?? new Dictionary<string, IReadOnlyCollection<string>>();
    }

    Task IRightsStore.SaveAsync(Guid blogId, IReadOnlyDictionary<string, IReadOnlyCollection<string>> rights, CancellationToken cancellationToken) =>
        _files.SaveAsync(BlogFile(blogId, "rights.xml"), new XDocument(new XElement("rights",
            rights.OrderBy(r => r.Key, StringComparer.Ordinal)
                .Select(r => new XElement("right", new XAttribute("name", r.Key), r.Value.Select(role => new XElement("role", role)))))), cancellationToken);

    // ---- Campi personalizzati ----

    async Task<IReadOnlyList<CustomField>> ICustomFieldStore.GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        await ReadCollectionAsync(BlogFile(blogId, "customfields.xml"), "field", XmlMappers.ToCustomField, cancellationToken).ConfigureAwait(false);

    Task ICustomFieldStore.SaveAsync(CustomField field, CancellationToken cancellationToken) =>
        UpsertAsync(BlogFile(field.BlogId, "customfields.xml"), "customfields", "field", XmlMappers.ToXml(field), e => SameField(e, field), cancellationToken);

    Task ICustomFieldStore.DeleteAsync(CustomField field, CancellationToken cancellationToken) =>
        RemoveAsync(BlogFile(field.BlogId, "customfields.xml"), "customfields", "field", e => SameField(e, field), cancellationToken);

    // ---- Elenchi di testo ----

    async Task<IReadOnlyList<string>> IStringListStore.LoadAsync(Guid blogId, StringListKind kind, CancellationToken cancellationToken)
    {
        var document = await _files.LoadAsync(StringListFile(blogId, kind), cancellationToken).ConfigureAwait(false);
        return document?.Root?.Elements("item").Select(e => e.Value).ToList() ?? [];
    }

    Task IStringListStore.SaveAsync(Guid blogId, StringListKind kind, IReadOnlyList<string> values, CancellationToken cancellationToken) =>
        _files.SaveAsync(StringListFile(blogId, kind), new XDocument(XmlValues.List("items", "item", values)), cancellationToken);

    // ---- Dati delle estensioni ----

    Task<string?> IExtensionDataStore.LoadAsync(Guid blogId, ExtensionDataKind kind, string id, CancellationToken cancellationToken) =>
        _files.ReadTextAsync(ExtensionDataFile(blogId, kind, id), cancellationToken);

    Task IExtensionDataStore.SaveAsync(Guid blogId, ExtensionDataKind kind, string id, string data, CancellationToken cancellationToken) =>
        _files.WriteTextAsync(ExtensionDataFile(blogId, kind, id), data, cancellationToken);

    Task IExtensionDataStore.RemoveAsync(Guid blogId, ExtensionDataKind kind, string id, CancellationToken cancellationToken) =>
        _files.DeleteAsync(ExtensionDataFile(blogId, kind, id), cancellationToken);

    // ---- Utenti e ruoli ----

    async Task<IReadOnlyList<BlogUser>> IUserStore.GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        await ReadCollectionAsync(BlogFile(blogId, "users.xml"), "user", XmlMappers.ToUser, cancellationToken).ConfigureAwait(false);

    Task IUserStore.SaveAsync(BlogUser user, CancellationToken cancellationToken) =>
        UpsertAsync(BlogFile(user.BlogId, "users.xml"), "users", "user", XmlMappers.ToXml(user), e => SameName(e, "userName", user.UserName), cancellationToken);

    Task IUserStore.DeleteAsync(Guid blogId, string userName, CancellationToken cancellationToken) =>
        RemoveAsync(BlogFile(blogId, "users.xml"), "users", "user", e => SameName(e, "userName", userName), cancellationToken);

    async Task<IReadOnlyList<BlogRole>> IRoleStore.GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        await ReadCollectionAsync(BlogFile(blogId, "roles.xml"), "role", XmlMappers.ToRole, cancellationToken).ConfigureAwait(false);

    Task IRoleStore.SaveAsync(BlogRole role, CancellationToken cancellationToken) =>
        UpsertAsync(BlogFile(role.BlogId, "roles.xml"), "roles", "role", XmlMappers.ToXml(role), e => SameName(e, "name", role.Name), cancellationToken);

    Task IRoleStore.DeleteAsync(Guid blogId, string roleName, CancellationToken cancellationToken) =>
        RemoveAsync(BlogFile(blogId, "roles.xml"), "roles", "role", e => SameName(e, "name", roleName), cancellationToken);

    // ---- Regole dei filtri dei commenti ----

    async Task<IReadOnlyList<CommentFilterRule>> ICommentFilterStore.GetRulesAsync(Guid blogId, CancellationToken cancellationToken) =>
        await ReadCollectionAsync(BlogFile(blogId, "commentfilters.xml"), "rule", XmlMappers.ToRule, cancellationToken).ConfigureAwait(false);

    Task ICommentFilterStore.SaveRulesAsync(Guid blogId, IReadOnlyList<CommentFilterRule> rules, CancellationToken cancellationToken) =>
        _files.SaveAsync(BlogFile(blogId, "commentfilters.xml"), new XDocument(new XElement("commentfilters", rules.Select(XmlMappers.ToXml))), cancellationToken);

    // ---- Percorsi ----

    private string BlogsFile => Path.Combine(_root, "blogs.xml");

    private string BlogFolder(Guid blogId) => Path.Combine(_root, "blogs", blogId.ToString("N"));

    private string BlogFile(Guid blogId, string name) => Path.Combine(BlogFolder(blogId), name);

    private string ItemFile(Guid blogId, string folder, Guid id) => Path.Combine(BlogFolder(blogId), folder, id.ToString("N") + ".xml");

    private string ReferrerFile(Guid blogId, DateTime day) =>
        Path.Combine(BlogFolder(blogId), "referrers", day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".xml");

    private string StringListFile(Guid blogId, StringListKind kind) =>
        BlogFile(blogId, kind == StringListKind.PingServices ? "pingservices.xml" : "stopwords.xml");

    private string ExtensionDataFile(Guid blogId, ExtensionDataKind kind, string id) =>
        Path.Combine(BlogFolder(blogId), "datastore", kind.ToString().ToLowerInvariant(), SafeFileName(id) + ".json");

    /// <summary>Nome di file valido su Windows e Linux; i caratteri non ammessi diventano "_".</summary>
    internal static string SafeFileName(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        var invalid = Path.GetInvalidFileNameChars().Concat(['/', '\\', ':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var safe = new string(id.Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray()).Trim('.', ' ');
        return safe.Length == 0 ? "_" : safe;
    }

    // ---- Operazioni comuni ----

    private static IEnumerable<string> EnumerateXml(string folder) =>
        Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, "*.xml")
                .Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
            : [];

    private async Task<IReadOnlyList<T>> ReadFolderAsync<T>(string folder, Func<XElement, T> map, CancellationToken cancellationToken)
    {
        var result = new List<T>();
        foreach (var file in EnumerateXml(folder))
        {
            if (await _files.LoadAsync(file, cancellationToken).ConfigureAwait(false) is { Root: { } root })
            {
                result.Add(map(root));
            }
        }

        return result;
    }

    private async Task<List<T>> ReadCollectionAsync<T>(string path, string itemName, Func<XElement, T> map, CancellationToken cancellationToken)
    {
        var document = await _files.LoadAsync(path, cancellationToken).ConfigureAwait(false);
        return document?.Root?.Elements(itemName).Select(map).ToList() ?? [];
    }

    private Task UpsertAsync(string path, string rootName, string itemName, XElement item, Func<XElement, bool> sameKey, CancellationToken cancellationToken) =>
        _files.UpdateAsync(path, rootName, root =>
        {
            var existing = root.Elements(itemName).FirstOrDefault(sameKey);
            if (existing is null)
            {
                root.Add(item);
            }
            else
            {
                existing.ReplaceWith(item);
            }
        }, cancellationToken);

    private Task RemoveAsync(string path, string rootName, string itemName, Func<XElement, bool> predicate, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return Task.CompletedTask;
        }

        return _files.UpdateAsync(path, rootName, root => root.Elements(itemName).Where(predicate).Remove(), cancellationToken);
    }

    private static bool SameName(XElement element, string name, string value) =>
        string.Equals(element.String(name), value, StringComparison.OrdinalIgnoreCase);

    private static bool SameField(XElement element, CustomField field) =>
        SameName(element, "customType", field.CustomType)
        && SameName(element, "objectId", field.ObjectId)
        && SameName(element, "key", field.Key);
}

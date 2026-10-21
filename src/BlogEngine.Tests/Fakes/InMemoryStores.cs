using System.Collections.Concurrent;
using BlogEngine.Core.Data;
using BlogEngine.Models;

namespace BlogEngine.Tests.Fakes;

/// <summary>Store in memoria che implementa tutti i contratti dei provider, per i test del Core.</summary>
public sealed class InMemoryStores :
    IDataStoreInitializer,
    IStringListStore,
    IExtensionDataStore,
    IUserStore,
    IRoleStore,
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
    ICommentFilterStore
{
    private readonly ConcurrentDictionary<Guid, Blog> _blogs = new();
    private readonly ConcurrentDictionary<(Type, Guid), object> _entities = new();
    private readonly ConcurrentDictionary<(Guid, string), AuthorProfile> _profiles = new();
    private readonly ConcurrentDictionary<Guid, IReadOnlyDictionary<string, string>> _settings = new();
    private readonly ConcurrentDictionary<Guid, IReadOnlyDictionary<string, IReadOnlyCollection<string>>> _rights = new();
    private readonly ConcurrentDictionary<(Guid, string, string, string), CustomField> _fields = new();
    private readonly ConcurrentDictionary<Guid, IReadOnlyList<CommentFilterRule>> _rules = new();

    /// <summary>Numero di letture complete eseguite dai servizi (per verificare la cache).</summary>
    public int LoadCount { get; private set; }

    // ---- Blog ----

    Task<IReadOnlyList<Blog>> IBlogStore.GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Blog>>(_blogs.Values.ToList());

    Task IBlogStore.SaveAsync(Blog blog, CancellationToken cancellationToken)
    {
        _blogs[blog.Id] = blog;
        return Task.CompletedTask;
    }

    Task IBlogStore.DeleteAsync(Guid blogId, CancellationToken cancellationToken)
    {
        _blogs.TryRemove(blogId, out _);
        foreach (var key in _entities.Where(e => ((IBlogItem)e.Value).BlogId == blogId).Select(e => e.Key).ToList())
        {
            _entities.TryRemove(key, out _);
        }

        _settings.TryRemove(blogId, out _);
        _rights.TryRemove(blogId, out _);
        _rules.TryRemove(blogId, out _);
        return Task.CompletedTask;
    }

    // ---- Entità ----

    Task<IReadOnlyList<Post>> IEntityStore<Post>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => GetAll<Post>(blogId);
    Task IEntityStore<Post>.SaveAsync(Post entity, CancellationToken cancellationToken) => Save(entity);
    Task IEntityStore<Post>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) => Delete<Post>(id);

    Task<IReadOnlyList<Page>> IEntityStore<Page>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => GetAll<Page>(blogId);
    Task IEntityStore<Page>.SaveAsync(Page entity, CancellationToken cancellationToken) => Save(entity);
    Task IEntityStore<Page>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) => Delete<Page>(id);

    Task<IReadOnlyList<Category>> IEntityStore<Category>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => GetAll<Category>(blogId);
    Task IEntityStore<Category>.SaveAsync(Category entity, CancellationToken cancellationToken) => Save(entity);
    Task IEntityStore<Category>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) => Delete<Category>(id);

    Task<IReadOnlyList<BlogRollItem>> IEntityStore<BlogRollItem>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => GetAll<BlogRollItem>(blogId);
    Task IEntityStore<BlogRollItem>.SaveAsync(BlogRollItem entity, CancellationToken cancellationToken) => Save(entity);
    Task IEntityStore<BlogRollItem>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) => Delete<BlogRollItem>(id);

    Task<IReadOnlyList<Referrer>> IEntityStore<Referrer>.GetAllAsync(Guid blogId, CancellationToken cancellationToken) => GetAll<Referrer>(blogId);
    Task IEntityStore<Referrer>.SaveAsync(Referrer entity, CancellationToken cancellationToken) => Save(entity);
    Task IEntityStore<Referrer>.DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken) => Delete<Referrer>(id);

    /// <summary>Elementi di un tipo attualmente "su disco".</summary>
    public IReadOnlyList<T> Stored<T>() where T : class => _entities.Where(e => e.Key.Item1 == typeof(T)).Select(e => (T)e.Value).ToList();

    private Task<IReadOnlyList<T>> GetAll<T>(Guid blogId) where T : class, IBlogItem
    {
        LoadCount++;
        return Task.FromResult<IReadOnlyList<T>>(Stored<T>().Where(e => e.BlogId == blogId).ToList());
    }

    private Task Save<T>(T entity) where T : class, IBlogItem
    {
        _entities[(typeof(T), entity.Id)] = entity;
        return Task.CompletedTask;
    }

    private Task Delete<T>(Guid id)
    {
        _entities.TryRemove((typeof(T), id), out _);
        return Task.CompletedTask;
    }

    // ---- Profili ----

    Task<IReadOnlyList<AuthorProfile>> IProfileStore.GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<AuthorProfile>>(_profiles.Values.Where(p => p.BlogId == blogId).ToList());

    Task IProfileStore.SaveAsync(AuthorProfile profile, CancellationToken cancellationToken)
    {
        _profiles[(profile.BlogId, profile.UserName.ToLowerInvariant())] = profile;
        return Task.CompletedTask;
    }

    Task IProfileStore.DeleteAsync(Guid blogId, string userName, CancellationToken cancellationToken)
    {
        _profiles.TryRemove((blogId, userName.ToLowerInvariant()), out _);
        return Task.CompletedTask;
    }

    // ---- Impostazioni e permessi ----

    Task<IReadOnlyDictionary<string, string>> ISettingsStore.LoadAsync(Guid blogId, CancellationToken cancellationToken) =>
        Task.FromResult(_settings.TryGetValue(blogId, out var values) ? values : new Dictionary<string, string>());

    Task ISettingsStore.SaveAsync(Guid blogId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken)
    {
        _settings[blogId] = settings;
        return Task.CompletedTask;
    }

    public IReadOnlyDictionary<string, string>? StoredSettings(Guid blogId) => _settings.GetValueOrDefault(blogId);

    Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> IRightsStore.LoadAsync(Guid blogId, CancellationToken cancellationToken) =>
        Task.FromResult(_rights.TryGetValue(blogId, out var rights) ? rights : new Dictionary<string, IReadOnlyCollection<string>>());

    Task IRightsStore.SaveAsync(Guid blogId, IReadOnlyDictionary<string, IReadOnlyCollection<string>> rights, CancellationToken cancellationToken)
    {
        _rights[blogId] = rights;
        return Task.CompletedTask;
    }

    // ---- Campi personalizzati ----

    Task<IReadOnlyList<CustomField>> ICustomFieldStore.GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<CustomField>>(_fields.Values.Where(f => f.BlogId == blogId).ToList());

    Task ICustomFieldStore.SaveAsync(CustomField field, CancellationToken cancellationToken)
    {
        _fields[FieldKey(field)] = field;
        return Task.CompletedTask;
    }

    Task ICustomFieldStore.DeleteAsync(CustomField field, CancellationToken cancellationToken)
    {
        _fields.TryRemove(FieldKey(field), out _);
        return Task.CompletedTask;
    }

    private static (Guid, string, string, string) FieldKey(CustomField f) =>
        (f.BlogId, f.CustomType.ToUpperInvariant(), f.ObjectId.ToLowerInvariant(), f.Key.ToLowerInvariant());

    // ---- Inizializzazione, elenchi, dati delle estensioni, utenti e ruoli ----

    private readonly ConcurrentDictionary<(Guid, StringListKind), IReadOnlyList<string>> _lists = new();
    private readonly ConcurrentDictionary<(Guid, ExtensionDataKind, string), string> _extensionData = new();
    private readonly ConcurrentDictionary<(Guid, string), BlogUser> _users = new();
    private readonly ConcurrentDictionary<(Guid, string), BlogRole> _roles = new();

    Task IDataStoreInitializer.InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    Task<IReadOnlyList<string>> IStringListStore.LoadAsync(Guid blogId, StringListKind kind, CancellationToken cancellationToken) =>
        Task.FromResult(_lists.TryGetValue((blogId, kind), out var values) ? values : []);

    Task IStringListStore.SaveAsync(Guid blogId, StringListKind kind, IReadOnlyList<string> values, CancellationToken cancellationToken)
    {
        _lists[(blogId, kind)] = values.ToList();
        return Task.CompletedTask;
    }

    Task<string?> IExtensionDataStore.LoadAsync(Guid blogId, ExtensionDataKind kind, string id, CancellationToken cancellationToken) =>
        Task.FromResult(_extensionData.GetValueOrDefault((blogId, kind, id)));

    Task IExtensionDataStore.SaveAsync(Guid blogId, ExtensionDataKind kind, string id, string data, CancellationToken cancellationToken)
    {
        _extensionData[(blogId, kind, id)] = data;
        return Task.CompletedTask;
    }

    Task IExtensionDataStore.RemoveAsync(Guid blogId, ExtensionDataKind kind, string id, CancellationToken cancellationToken)
    {
        _extensionData.TryRemove((blogId, kind, id), out _);
        return Task.CompletedTask;
    }

    Task<IReadOnlyList<BlogUser>> IUserStore.GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BlogUser>>(_users.Values.Where(u => u.BlogId == blogId).ToList());

    Task IUserStore.SaveAsync(BlogUser user, CancellationToken cancellationToken)
    {
        _users[(user.BlogId, user.UserName.ToLowerInvariant())] = user;
        return Task.CompletedTask;
    }

    Task IUserStore.DeleteAsync(Guid blogId, string userName, CancellationToken cancellationToken)
    {
        _users.TryRemove((blogId, userName.ToLowerInvariant()), out _);
        return Task.CompletedTask;
    }

    Task<IReadOnlyList<BlogRole>> IRoleStore.GetAllAsync(Guid blogId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BlogRole>>(_roles.Values.Where(r => r.BlogId == blogId).ToList());

    Task IRoleStore.SaveAsync(BlogRole role, CancellationToken cancellationToken)
    {
        _roles[(role.BlogId, role.Name.ToLowerInvariant())] = role;
        return Task.CompletedTask;
    }

    Task IRoleStore.DeleteAsync(Guid blogId, string roleName, CancellationToken cancellationToken)
    {
        _roles.TryRemove((blogId, roleName.ToLowerInvariant()), out _);
        return Task.CompletedTask;
    }

    // ---- Filtri dei commenti ----

    Task<IReadOnlyList<CommentFilterRule>> ICommentFilterStore.GetRulesAsync(Guid blogId, CancellationToken cancellationToken) =>
        Task.FromResult(_rules.TryGetValue(blogId, out var rules) ? rules : []);

    Task ICommentFilterStore.SaveRulesAsync(Guid blogId, IReadOnlyList<CommentFilterRule> rules, CancellationToken cancellationToken)
    {
        _rules[blogId] = rules;
        return Task.CompletedTask;
    }
}

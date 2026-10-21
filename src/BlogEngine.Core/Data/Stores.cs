using BlogEngine.Models;

namespace BlogEngine.Core.Data;

// Contratti di memorizzazione implementati dai provider (XML, SQLite). Sostituiscono la classe BlogProvider.
// I servizi del Core tengono in memoria i dati di ogni blog, quindi i provider devono solo leggere
// tutto l'insieme di un blog e salvare o eliminare un elemento alla volta.

/// <summary>
/// Prepara lo spazio di memorizzazione all'avvio dell'applicazione: crea le cartelle (XML)
/// o applica le migrazioni dello schema (SQLite). Deve poter essere eseguito più volte.
/// </summary>
public interface IDataStoreInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}

/// <summary>Elenco dei blog del multi-blog.</summary>
public interface IBlogStore
{
    Task<IReadOnlyList<Blog>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(Blog blog, CancellationToken cancellationToken = default);

    /// <summary>Elimina il blog e tutti i suoi dati.</summary>
    Task DeleteAsync(Guid blogId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Entità di un blog identificate da un Guid: post (commenti inclusi), pagine, categorie,
/// voci del blogroll e referrer.
/// </summary>
public interface IEntityStore<T>
    where T : class
{
    /// <summary>Tutti gli elementi del blog, compresi quelli nel cestino.</summary>
    Task<IReadOnlyList<T>> GetAllAsync(Guid blogId, CancellationToken cancellationToken = default);

    /// <summary>Inserisce o aggiorna l'elemento.</summary>
    Task SaveAsync(T entity, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid blogId, Guid id, CancellationToken cancellationToken = default);
}

public interface IProfileStore
{
    Task<IReadOnlyList<AuthorProfile>> GetAllAsync(Guid blogId, CancellationToken cancellationToken = default);

    Task SaveAsync(AuthorProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid blogId, string userName, CancellationToken cancellationToken = default);
}

/// <summary>Impostazioni come coppie nome/valore (nomi in minuscolo, valori in cultura invariante).</summary>
public interface ISettingsStore
{
    Task<IReadOnlyDictionary<string, string>> LoadAsync(Guid blogId, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid blogId, IReadOnlyDictionary<string, string> settings, CancellationToken cancellationToken = default);
}

/// <summary>Associazione permesso → ruoli. Le chiavi sono i nomi dell'enum Rights.</summary>
public interface IRightsStore
{
    Task<IReadOnlyDictionary<string, IReadOnlyCollection<string>>> LoadAsync(Guid blogId, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid blogId, IReadOnlyDictionary<string, IReadOnlyCollection<string>> rights, CancellationToken cancellationToken = default);
}

public interface ICustomFieldStore
{
    Task<IReadOnlyList<CustomField>> GetAllAsync(Guid blogId, CancellationToken cancellationToken = default);

    /// <summary>Inserisce o aggiorna il campo identificato da tipo, oggetto e chiave.</summary>
    Task SaveAsync(CustomField field, CancellationToken cancellationToken = default);

    Task DeleteAsync(CustomField field, CancellationToken cancellationToken = default);
}

/// <summary>Elenchi di testo semplici di un blog: servizi di ping e parole escluse dalla ricerca.</summary>
public interface IStringListStore
{
    Task<IReadOnlyList<string>> LoadAsync(Guid blogId, StringListKind kind, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid blogId, StringListKind kind, IReadOnlyList<string> values, CancellationToken cancellationToken = default);
}

public enum StringListKind
{
    PingServices,
    StopWords,
}

/// <summary>
/// Dati liberi di estensioni, widget e temi, serializzati dal chiamante (JSON).
/// Sostituisce LoadFromDataStore/SaveToDataStore.
/// </summary>
public interface IExtensionDataStore
{
    Task<string?> LoadAsync(Guid blogId, ExtensionDataKind kind, string id, CancellationToken cancellationToken = default);

    Task SaveAsync(Guid blogId, ExtensionDataKind kind, string id, string data, CancellationToken cancellationToken = default);

    Task RemoveAsync(Guid blogId, ExtensionDataKind kind, string id, CancellationToken cancellationToken = default);
}

public enum ExtensionDataKind
{
    Extension,
    Widget,
    Theme,
}

public interface IUserStore
{
    Task<IReadOnlyList<BlogUser>> GetAllAsync(Guid blogId, CancellationToken cancellationToken = default);

    Task SaveAsync(BlogUser user, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid blogId, string userName, CancellationToken cancellationToken = default);
}

public interface IRoleStore
{
    Task<IReadOnlyList<BlogRole>> GetAllAsync(Guid blogId, CancellationToken cancellationToken = default);

    Task SaveAsync(BlogRole role, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid blogId, string roleName, CancellationToken cancellationToken = default);
}

public interface ICommentFilterStore
{
    Task<IReadOnlyList<CommentFilterRule>> GetRulesAsync(Guid blogId, CancellationToken cancellationToken = default);

    Task SaveRulesAsync(Guid blogId, IReadOnlyList<CommentFilterRule> rules, CancellationToken cancellationToken = default);
}

using System.Collections.Concurrent;
using System.Text.Json;
using BlogEngine.Core.Data;
using BlogEngine.Core.Services;
using BlogEngine.Extensions;

namespace BlogEngine.Core.Extensions;

/// <summary>Un'estensione trovata all'avvio, compilata o con i suoi errori di compilazione.</summary>
public sealed class ExtensionDescriptor
{
    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Author { get; init; } = string.Empty;

    public int DefaultPriority { get; init; }

    public bool EnabledByDefault { get; init; } = true;

    public string? AdminPage { get; init; }

    /// <summary>Cartella dei sorgenti.</summary>
    public string Folder { get; init; } = string.Empty;

    /// <summary>Caricata dall'amministrazione (App_Data/extensions) e quindi eliminabile.</summary>
    public bool IsUploaded { get; init; }

    public IReadOnlyList<ExtensionSettingsDefinition> Settings { get; init; } = [];

    /// <summary>Errori di compilazione o di avvio: l'estensione non è attiva.</summary>
    public string? Error { get; init; }

    /// <summary>Tipi registrati dall'estensione nel contenitore dei servizi.</summary>
    public IReadOnlySet<Type> ComponentTypes { get; init; } = new HashSet<Type>();

    /// <summary>Assembly compilato dai sorgenti dell'estensione.</summary>
    public System.Reflection.Assembly? Assembly { get; init; }
}

/// <summary>Elenco delle estensioni installate (porting di ExtensionManager).</summary>
public sealed class ExtensionRegistry
{
    private readonly ConcurrentDictionary<string, ExtensionDescriptor> _extensions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<Type, ExtensionDescriptor> _byComponent = new();

    public IReadOnlyList<ExtensionDescriptor> All => _extensions.Values.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

    public void Add(ExtensionDescriptor extension)
    {
        _extensions[extension.Name] = extension;
        foreach (var type in extension.ComponentTypes)
        {
            _byComponent[type] = extension;
        }
    }

    public ExtensionDescriptor? Find(string name) => _extensions.TryGetValue(name, out var extension) ? extension : null;

    /// <summary>Estensione a cui appartiene un componente (gestore, filtro...), oppure null se è del sito.</summary>
    public ExtensionDescriptor? FindByComponent(Type type)
    {
        if (_byComponent.TryGetValue(type, out var extension))
        {
            return extension;
        }

        // Un tipo dell'assembly di un'estensione (es. creato da una factory) appartiene a quell'estensione,
        // se nella cartella ce n'è una sola.
        var owners = _extensions.Values.Where(e => e.Assembly is not null && e.Assembly == type.Assembly).Take(2).ToList();
        return owners.Count == 1 ? owners[0] : null;
    }
}

/// <summary>Stato di un'estensione deciso dall'amministratore.</summary>
public sealed record ExtensionState(bool Enabled, int Priority);

/// <summary>
/// Attivazione e priorità delle estensioni, uguali per tutti i blog e salvate con i dati del blog
/// principale. Lo stato è in memoria: cambiarlo non richiede di riavviare il sito.
/// </summary>
public sealed class ExtensionStates(ExtensionRegistry registry, IExtensionDataStore store, BlogsCache blogs)
{
    private const string StateKey = "_state";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, ExtensionState> _states = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public ExtensionState Get(string name)
    {
        if (_states.TryGetValue(name, out var state))
        {
            return state;
        }

        var extension = registry.Find(name);
        return new ExtensionState(extension?.EnabledByDefault ?? false, extension?.DefaultPriority ?? 0);
    }

    public bool IsEnabled(string name) => registry.Find(name) is { Error: null } && Get(name).Enabled;

    /// <summary>Legge lo stato salvato (all'avvio e dopo la configurazione iniziale).</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var json = await store.LoadAsync(await PrimaryBlogIdAsync(cancellationToken).ConfigureAwait(false), ExtensionDataKind.Extension, StateKey, cancellationToken).ConfigureAwait(false);
            var saved = json is null ? null : JsonSerializer.Deserialize<Dictionary<string, ExtensionState>>(json);
            _states = new Dictionary<string, ExtensionState>(saved ?? [], StringComparer.OrdinalIgnoreCase);
            _loaded = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SetAsync(string name, bool enabled, int priority, CancellationToken cancellationToken = default)
    {
        if (!_loaded)
        {
            await LoadAsync(cancellationToken).ConfigureAwait(false);
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var states = new Dictionary<string, ExtensionState>(_states, StringComparer.OrdinalIgnoreCase) { [name] = new ExtensionState(enabled, Math.Max(0, priority)) };
            await store.SaveAsync(await PrimaryBlogIdAsync(cancellationToken).ConfigureAwait(false), ExtensionDataKind.Extension, StateKey, JsonSerializer.Serialize(states), cancellationToken).ConfigureAwait(false);
            _states = states;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Guid> PrimaryBlogIdAsync(CancellationToken cancellationToken) =>
        (await blogs.GetAllAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(b => b.IsPrimary)?.Id
        ?? throw new InvalidOperationException("Il blog principale non esiste ancora.");
}

/// <summary>
/// Decide se un componente registrato nel contenitore dei servizi può essere usato: quelli del sito
/// sempre, quelli di un'estensione solo se l'estensione è attiva e compilata senza errori.
/// </summary>
public interface IExtensionGate
{
    bool IsActive(object component);

    /// <summary>Priorità dell'estensione del componente (0 per i componenti del sito).</summary>
    int PriorityOf(object component);
}

/// <summary>Usato quando non ci sono estensioni (es. nei test del Core): tutto è attivo.</summary>
public sealed class AllActiveExtensionGate : IExtensionGate
{
    public bool IsActive(object component) => true;

    public int PriorityOf(object component) => 0;
}

public sealed class ExtensionGate(ExtensionRegistry registry, ExtensionStates states) : IExtensionGate
{
    public bool IsActive(object component) =>
        registry.FindByComponent(component.GetType()) is not { } extension || states.IsEnabled(extension.Name);

    public int PriorityOf(object component) =>
        registry.FindByComponent(component.GetType()) is { } extension ? states.Get(extension.Name).Priority : 0;
}

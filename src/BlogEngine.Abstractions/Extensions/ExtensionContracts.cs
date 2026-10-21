using Microsoft.Extensions.DependencyInjection;

namespace BlogEngine.Extensions;

/// <summary>
/// Dati dell'estensione mostrati nell'amministrazione (porting dell'attributo [Extension]).
/// Il nome dell'estensione è il nome della classe.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ExtensionAttribute(string description, string version, string author) : Attribute
{
    public string Description { get; } = description;

    public string Version { get; } = version;

    /// <summary>Autore; può contenere un link HTML come nelle estensioni originali.</summary>
    public string Author { get; } = author;

    /// <summary>Ordine di esecuzione rispetto alle altre estensioni: i valori più bassi vengono eseguiti prima.</summary>
    public int Priority { get; set; }

    /// <summary>Stato alla prima installazione (es. i filtri antispam che richiedono una chiave partono disattivati).</summary>
    public bool EnabledByDefault { get; set; } = true;

    /// <summary>Pagina di amministrazione propria, al posto di quella generata dalle impostazioni (es. "~/admin/...").</summary>
    public string? AdminPage { get; set; }
}

/// <summary>
/// Un'estensione: una classe con l'attributo <see cref="ExtensionAttribute"/> in una cartella di
/// Custom/Extensions (o App_Data/extensions), compilata dal sito all'avvio.
/// </summary>
public interface IBlogExtension
{
    /// <summary>
    /// Registra i componenti dell'estensione: gestori di eventi (<c>IEventHandler&lt;T&gt;</c>), filtri antispam,
    /// captcha, dati per i widget... Quando l'estensione è disattivata il sito li ignora.
    /// </summary>
    void ConfigureServices(IServiceCollection services);

    /// <summary>Impostazioni modificabili dall'amministrazione, con i valori iniziali.</summary>
    IEnumerable<ExtensionSettingsDefinition> Settings => [];
}

/// <summary>Tipo di un parametro delle impostazioni (stessi valori della versione originale).</summary>
public enum ExtensionParameterType
{
    String,
    Boolean,
    Integer,
    Long,
    Float,
    Double,
    Decimal,
    DropDown,
    ListBox,
    RadioGroup,
}

/// <summary>Un campo delle impostazioni di un'estensione.</summary>
public sealed class ExtensionParameter
{
    public required string Name { get; init; }

    public string Label { get; init; } = string.Empty;

    public int MaxLength { get; init; } = 100;

    public bool Required { get; init; }

    /// <summary>Nelle tabelle, il campo che identifica la riga e non può ripetersi.</summary>
    public bool KeyField { get; init; }

    public ExtensionParameterType Type { get; init; } = ExtensionParameterType.String;

    /// <summary>Valori proposti per DropDown, ListBox e RadioGroup ("valore" oppure "valore|testo").</summary>
    public IReadOnlyList<string> Options { get; init; } = [];
}

/// <summary>
/// Un gruppo di impostazioni (porting di ExtensionSettings): un insieme di valori singoli
/// (<see cref="IsScalar"/>) oppure una tabella di righe con le stesse colonne.
/// </summary>
public sealed class ExtensionSettingsDefinition
{
    private readonly List<ExtensionParameter> _parameters = [];
    private readonly List<Dictionary<string, string>> _defaults = [];

    public ExtensionSettingsDefinition(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    public string Name { get; }

    /// <summary>Spiegazione mostrata sopra il modulo (HTML).</summary>
    public string Help { get; set; } = string.Empty;

    public bool IsScalar { get; set; }

    /// <summary>Impostazioni interne, non mostrate nell'amministrazione.</summary>
    public bool Hidden { get; set; }

    public IReadOnlyList<ExtensionParameter> Parameters => _parameters;

    /// <summary>Valori iniziali: una riga per le impostazioni singole, più righe per le tabelle.</summary>
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Defaults => _defaults;

    public ExtensionSettingsDefinition AddParameter(
        string name,
        string? label = null,
        int maxLength = 100,
        bool required = false,
        bool keyField = false,
        ExtensionParameterType type = ExtensionParameterType.String,
        params string[] options)
    {
        _parameters.Add(new ExtensionParameter
        {
            Name = name.Replace(" ", string.Empty, StringComparison.Ordinal),
            Label = label ?? name,
            MaxLength = maxLength,
            Required = required,
            KeyField = keyField,
            Type = type,
            Options = options,
        });
        return this;
    }

    /// <summary>Valore iniziale di un'impostazione singola.</summary>
    public ExtensionSettingsDefinition SetDefault(string parameter, object value)
    {
        if (_defaults.Count == 0)
        {
            _defaults.Add(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        _defaults[0][parameter] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        return this;
    }

    /// <summary>Riga iniziale di una tabella, con i valori nell'ordine dei parametri.</summary>
    public ExtensionSettingsDefinition AddRow(params string[] values)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _parameters.Count && i < values.Length; i++)
        {
            row[_parameters[i].Name] = values[i];
        }

        _defaults.Add(row);
        return this;
    }
}

/// <summary>Valori salvati di un gruppo di impostazioni.</summary>
public sealed class ExtensionSettingsValues
{
    public required string Name { get; init; }

    public List<Dictionary<string, string>> Rows { get; init; } = [];

    /// <summary>Valore di un'impostazione singola (prima riga), oppure <paramref name="fallback"/>.</summary>
    public string Get(string parameter, string fallback = "") =>
        Rows.Count > 0 && Rows[0].TryGetValue(parameter, out var value) ? value : fallback;

    public bool GetBoolean(string parameter, bool fallback = false) =>
        bool.TryParse(Get(parameter), out var value) ? value : fallback;

    public int GetInt32(string parameter, int fallback = 0) =>
        int.TryParse(Get(parameter), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : fallback;
}

/// <summary>Impostazioni delle estensioni del blog corrente.</summary>
public interface IExtensionSettingsService
{
    /// <summary>
    /// Valori del gruppo di impostazioni <paramref name="settingsName"/> (per default quello con il nome
    /// dell'estensione): quelli salvati per il blog, altrimenti quelli del blog principale o i valori iniziali.
    /// </summary>
    Task<ExtensionSettingsValues> GetAsync(string extension, string? settingsName = null, CancellationToken cancellationToken = default);

    Task SaveAsync(string extension, ExtensionSettingsValues values, CancellationToken cancellationToken = default);

    /// <summary>L'estensione è installata e attiva.</summary>
    bool IsEnabled(string extension);
}

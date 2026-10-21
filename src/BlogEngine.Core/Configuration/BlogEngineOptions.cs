namespace BlogEngine.Core.Configuration;

/// <summary>
/// Impostazioni generali lette dalla sezione "BlogEngine" di appsettings.json.
/// </summary>
public sealed class BlogEngineOptions
{
    public const string SectionName = "BlogEngine";

    /// <summary>
    /// Cartella in cui l'applicazione scrive dati, file caricati, cache, chiavi e log.
    /// Un percorso relativo è risolto rispetto alla cartella dell'applicazione (content root).
    /// Su Aruba vale "../App_Data", cioè la cartella accanto a "public".
    /// </summary>
    public string DataPath { get; set; } = "App_Data";

    /// <summary>Provider dei dati: "Xml" (file nella cartella dati) oppure "Sqlite" (un unico database).</summary>
    public DataProviderKind DataProvider { get; set; } = DataProviderKind.Xml;
}

public enum DataProviderKind
{
    Xml,
    Sqlite,
}

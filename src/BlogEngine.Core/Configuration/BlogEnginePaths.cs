namespace BlogEngine.Core.Configuration;

/// <summary>
/// Percorsi fisici usati dall'applicazione. Tutto ciò che viene scritto a runtime sta sotto <see cref="DataRoot"/>;
/// il resto del sito (content root) è usato in sola lettura.
/// </summary>
public sealed class BlogEnginePaths
{
    public BlogEnginePaths(string contentRoot, BlogEngineOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRoot);
        ArgumentNullException.ThrowIfNull(options);

        ContentRoot = Path.GetFullPath(contentRoot);
        DataRoot = Path.GetFullPath(Path.Combine(ContentRoot, options.DataPath));
    }

    /// <summary>Cartella dell'applicazione pubblicata (su Aruba: "public").</summary>
    public string ContentRoot { get; }

    /// <summary>Cartella dati scrivibile (su Aruba: "App_Data" accanto a "public").</summary>
    public string DataRoot { get; }

    /// <summary>Dati del blog gestiti dal provider (file XML o database SQLite).</summary>
    public string Data => Path.Combine(DataRoot, "data");

    /// <summary>File e immagini caricati dall'amministrazione.</summary>
    public string Files => Path.Combine(DataRoot, "files");

    /// <summary>Assembly delle estensioni compilate a runtime.</summary>
    public string Cache => Path.Combine(DataRoot, "cache");

    /// <summary>Chiavi di Data Protection (cookie di autenticazione, antiforgery).</summary>
    public string Keys => Path.Combine(DataRoot, "keys");

    public string Logs => Path.Combine(DataRoot, "logs");

    /// <summary>Temi installati tramite upload dall'amministrazione.</summary>
    public string InstalledThemes => Path.Combine(DataRoot, "themes");

    /// <summary>Estensioni installate tramite upload dall'amministrazione.</summary>
    public string InstalledExtensions => Path.Combine(DataRoot, "extensions");

    /// <summary>Temi inclusi nel pacchetto o copiati via FTP.</summary>
    public string BundledThemes => Path.Combine(ContentRoot, "Custom", "Themes");

    /// <summary>Estensioni incluse nel pacchetto o copiate via FTP.</summary>
    public string BundledExtensions => Path.Combine(ContentRoot, "Custom", "Extensions");

    /// <summary>Crea le cartelle scrivibili che non esistono ancora.</summary>
    public void EnsureDataDirectories()
    {
        foreach (var dir in new[] { DataRoot, Data, Files, Cache, Keys, Logs, InstalledThemes, InstalledExtensions })
        {
            Directory.CreateDirectory(dir);
        }
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlogEngine.Core.Configuration;
using Microsoft.Extensions.Options;

namespace BlogEngine.Web.Hosting;

/// <summary>
/// Stato dell'installazione, salvato in App_Data/installation.json (la cartella pubblicata è in sola lettura).
/// Finché il file non esiste il sito mostra solo la configurazione iniziale, protetta dalla chiave in
/// App_Data/setup-key.txt: su un hosting condiviso il file è leggibile solo da chi ha accesso FTP.
/// </summary>
public sealed class InstallationState
{
    public const string InstallationFileName = "installation.json";
    public const string SetupKeyFileName = "setup-key.txt";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly BlogEnginePaths _paths;
    private readonly object _sync = new();
    private InstallationInfo? _info;
    private DataProviderKind? _pendingProvider;
    private DataProviderKind? _lockedProvider;

    public InstallationState(BlogEnginePaths paths, IOptions<BlogEngineOptions> options)
    {
        _paths = paths;
        DefaultProvider = options.Value.DataProvider;
        _info = Load();
    }

    public bool IsInstalled => _info is not null;

    /// <summary>Provider proposto dalla configurazione (appsettings.json), usato come scelta predefinita.</summary>
    public DataProviderKind DefaultProvider { get; }

    /// <summary>Provider in uso: quello dell'installazione o, durante la configurazione, quello scelto.</summary>
    public DataProviderKind DataProvider => _info?.DataProvider ?? _pendingProvider ?? DefaultProvider;

    public string SetupKeyPath => Path.Combine(_paths.DataRoot, SetupKeyFileName);

    /// <summary>
    /// Il provider diventa definitivo per la vita del processo appena uno store viene creato:
    /// da quel momento i servizi in cache lo usano e non può più essere cambiato senza riavvio.
    /// </summary>
    public DataProviderKind LockProvider()
    {
        lock (_sync)
        {
            _lockedProvider ??= DataProvider;
            return _lockedProvider.Value;
        }
    }

    /// <summary>Sceglie il provider durante la configurazione; restituisce false se ne è già in uso un altro.</summary>
    public bool TrySelectProvider(DataProviderKind provider)
    {
        lock (_sync)
        {
            if (_lockedProvider is { } locked && locked != provider)
            {
                return false;
            }

            _pendingProvider = provider;
            return true;
        }
    }

    /// <summary>Crea la chiave di configurazione se il sito non è installato e la chiave non esiste.</summary>
    public string? EnsureSetupKey()
    {
        if (IsInstalled)
        {
            return null;
        }

        if (!File.Exists(SetupKeyPath))
        {
            Directory.CreateDirectory(_paths.DataRoot);
            File.WriteAllText(SetupKeyPath, Convert.ToHexString(RandomNumberGenerator.GetBytes(16)));
        }

        return File.ReadAllText(SetupKeyPath).Trim();
    }

    public bool VerifySetupKey(string? key)
    {
        var expected = EnsureSetupKey();
        if (expected is null || string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected.ToUpperInvariant()),
            Encoding.UTF8.GetBytes(key.Trim().ToUpperInvariant()));
    }

    /// <summary>Registra l'installazione completata ed elimina la chiave di configurazione.</summary>
    public void MarkInstalled(DateTime installedAtUtc)
    {
        lock (_sync)
        {
            var info = new InstallationInfo { DataProvider = DataProvider, InstalledAtUtc = installedAtUtc };
            Directory.CreateDirectory(_paths.DataRoot);
            File.WriteAllText(Path.Combine(_paths.DataRoot, InstallationFileName), JsonSerializer.Serialize(info, JsonOptions));
            File.Delete(SetupKeyPath);
            _info = info;
        }
    }

    private InstallationInfo? Load()
    {
        var path = Path.Combine(_paths.DataRoot, InstallationFileName);
        return File.Exists(path) ? JsonSerializer.Deserialize<InstallationInfo>(File.ReadAllText(path)) : null;
    }

    private sealed class InstallationInfo
    {
        public DataProviderKind DataProvider { get; init; }

        public DateTime InstalledAtUtc { get; init; }
    }
}

using System.Globalization;
using System.Reflection;

namespace BlogEngine.Web.Hosting;

/// <summary>
/// Versione del programma installato, scritta nell'assembly durante la compilazione (target BlogEngineBuildVersion
/// in Directory.Build.props): numero 4.0.N (N = numero di commit), commit, modifiche non committate e data.
/// Serve a riconoscere a colpo d'occhio, dall'amministrazione, quale pacchetto gira su un sito.
/// </summary>
public sealed record BuildInfo(string Version, string Commit, bool HasLocalChanges, DateTime? BuildDateUtc)
{
    /// <summary>Versione di questo programma.</summary>
    public static BuildInfo Current { get; } = FromAssembly(typeof(BuildInfo).Assembly);

    public static BuildInfo FromAssembly(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? string.Empty;
        var version = informational.Split('+')[0];
        if (version.Length == 0)
        {
            version = assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }

        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToDictionary(a => a.Key, a => a.Value ?? string.Empty, StringComparer.Ordinal);
        DateTime? date = metadata.TryGetValue("BuildDate", out var text)
            && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
            : null;

        return new BuildInfo(
            version,
            metadata.GetValueOrDefault("GitCommit", string.Empty),
            string.Equals(metadata.GetValueOrDefault("GitDirty"), "true", StringComparison.OrdinalIgnoreCase),
            date);
    }

    /// <summary>
    /// Testo per l'amministrazione, per esempio "4.0.486 (commit e06a49c, 13/11/2026 02:17 UTC)".
    /// <paramref name="localChanges"/> è il testo tradotto per le modifiche non committate.
    /// </summary>
    public string Describe(string localChanges = "modifiche locali")
    {
        var details = new List<string>();
        if (Commit.Length > 0)
        {
            details.Add(HasLocalChanges ? $"commit {Commit} + {localChanges}" : $"commit {Commit}");
        }

        if (BuildDateUtc is { } date)
        {
            details.Add(date.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture));
        }

        return details.Count == 0 ? Version : $"{Version} ({string.Join(", ", details)})";
    }
}

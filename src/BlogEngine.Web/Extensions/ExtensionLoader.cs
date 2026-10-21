using System.Diagnostics;
using System.Reflection;
using BlogEngine.Core.Configuration;
using BlogEngine.Core.Extensions;
using BlogEngine.Extensions;

namespace BlogEngine.Web.Extensions;

/// <summary>
/// All'avvio cerca le estensioni (Custom/Extensions e App_Data/extensions), le compila e registra i loro
/// componenti. Un'estensione che non si compila o genera un errore viene solo segnalata: il sito parte
/// comunque e le altre estensioni funzionano.
/// </summary>
public static class ExtensionLoader
{
    /// <summary>Messaggi dell'avvio, scritti nel log quando il sito è pronto.</summary>
    public sealed class StartupReport
    {
        public List<string> Messages { get; } = [];
    }

    /// <param name="cacheFolder">Cartella degli assembly compilati; per default App_Data/cache/extensions.</param>
    public static IServiceCollection AddBlogEngineExtensions(this IServiceCollection services, BlogEnginePaths paths, string? cacheFolder = null)
    {
        var registry = new ExtensionRegistry();
        var report = new StartupReport();
        services.AddSingleton(registry);
        services.AddSingleton(report);

        var compiler = new ExtensionCompiler(cacheFolder ?? Path.Combine(paths.Cache, "extensions"));
        foreach (var (folder, uploaded) in Folders(paths))
        {
            var name = Path.GetFileName(folder);
            if (registry.Find(name) is not null)
            {
                report.Messages.Add($"Estensione {name} ignorata in {folder}: esiste già una cartella con lo stesso nome.");
                continue;
            }

            var watch = Stopwatch.StartNew();
            CompiledExtension compiled;
            try
            {
                compiled = compiler.Compile(name, folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or FileLoadException)
            {
                compiled = new CompiledExtension(null, ex.Message, false);
            }

            if (compiled.Assembly is null)
            {
                registry.Add(new ExtensionDescriptor { Name = name, Folder = folder, IsUploaded = uploaded, Error = compiled.Error, Description = "Errore di compilazione" });
                report.Messages.Add($"Estensione {name} non compilata:{Environment.NewLine}{compiled.Error}");
                continue;
            }

            foreach (var extension in Register(services, compiled.Assembly, folder, uploaded, report))
            {
                registry.Add(extension);
            }

            report.Messages.Add($"Estensione {name} {(compiled.FromCache ? "caricata dalla cache" : "compilata")} in {watch.ElapsedMilliseconds} ms.");
        }

        return services;
    }

    /// <summary>Cartelle delle estensioni: prima quelle del pacchetto, poi quelle caricate.</summary>
    public static IEnumerable<(string Folder, bool Uploaded)> Folders(BlogEnginePaths paths) =>
        Subfolders(paths.BundledExtensions).Select(f => (f, false))
            .Concat(Subfolders(paths.InstalledExtensions).Select(f => (f, true)));

    private static IEnumerable<string> Subfolders(string root) =>
        Directory.Exists(root) ? Directory.EnumerateDirectories(root).Order(StringComparer.OrdinalIgnoreCase) : [];

    private static IEnumerable<ExtensionDescriptor> Register(IServiceCollection services, Assembly assembly, string folder, bool uploaded, StartupReport report)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }

        var found = false;
        foreach (var type in types.Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IBlogExtension).IsAssignableFrom(t)))
        {
            found = true;
            var attribute = type.GetCustomAttribute<ExtensionAttribute>();
            var before = services.Count;
            string? error = null;
            IReadOnlyList<ExtensionSettingsDefinition> settings = [];
            try
            {
                var extension = (IBlogExtension)Activator.CreateInstance(type)!;
                settings = extension.Settings.ToList();
                extension.ConfigureServices(services);
            }
            catch (Exception ex)
            {
                // Il codice delle estensioni è di terze parti: qualunque errore viene solo segnalato.
                error = ex.InnerException?.Message ?? ex.Message;
                report.Messages.Add($"Estensione {type.Name} non avviata: {error}");

                // Le registrazioni parziali vengono annullate.
                while (services.Count > before)
                {
                    services.RemoveAt(services.Count - 1);
                }
            }

            var components = services.Skip(before)
                .Select(d => d.ImplementationType ?? d.ImplementationInstance?.GetType())
                .Where(t => t is not null)
                .Append(type)
                .ToHashSet()!;

            yield return new ExtensionDescriptor
            {
                Name = type.Name,
                Description = attribute?.Description ?? string.Empty,
                Version = attribute?.Version ?? string.Empty,
                Author = attribute?.Author ?? string.Empty,
                DefaultPriority = attribute?.Priority ?? 0,
                EnabledByDefault = attribute?.EnabledByDefault ?? true,
                AdminPage = attribute?.AdminPage,
                Folder = folder,
                IsUploaded = uploaded,
                Settings = settings,
                Error = error,
                ComponentTypes = components!,
                Assembly = assembly,
            };
        }

        if (!found)
        {
            report.Messages.Add($"Nessuna classe IBlogExtension in {folder}.");
        }
    }
}

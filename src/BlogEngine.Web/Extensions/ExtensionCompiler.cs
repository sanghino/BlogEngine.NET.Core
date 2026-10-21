using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace BlogEngine.Web.Extensions;

/// <summary>Risultato della compilazione di una cartella di estensione.</summary>
public sealed record CompiledExtension(Assembly? Assembly, string? Error, bool FromCache);

/// <summary>
/// Compila i sorgenti .cs di una cartella di estensione con Roslyn (come faceva ASP.NET con App_Code).
/// L'assembly ottenuto viene salvato in App_Data/cache/extensions e riutilizzato finché sorgenti,
/// librerie della cartella e versione del sito non cambiano: i riavvii successivi sono immediati.
/// </summary>
public sealed class ExtensionCompiler(string cacheFolder)
{
    /// <summary>Using validi in tutti i file delle estensioni, per scrivere meno codice.</summary>
    private const string GlobalUsings = """
        global using System;
        global using System.Collections.Generic;
        global using System.IO;
        global using System.Linq;
        global using System.Threading;
        global using System.Threading.Tasks;
        global using BlogEngine.Events;
        global using BlogEngine.Extensions;
        global using BlogEngine.Models;
        global using BlogEngine.Services;
        global using Microsoft.Extensions.DependencyInjection;
        global using Microsoft.Extensions.Logging;
        """;

    private static readonly Lazy<IReadOnlyList<MetadataReference>> FrameworkReferences = new(LoadReferences);

    public CompiledExtension Compile(string name, string folder)
    {
        var sources = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(f => !IsBuildFolder(folder, f))
            .Order(StringComparer.Ordinal)
            .ToList();
        var libraries = Directory.EnumerateFiles(folder, "*.dll", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
        if (sources.Count == 0)
        {
            return new CompiledExtension(null, "La cartella non contiene file .cs.", false);
        }

        var loadContext = new ExtensionLoadContext(name, libraries);
        var cached = Path.Combine(cacheFolder, $"{name}.{Hash(folder, sources, libraries)}.dll");
        if (File.Exists(cached))
        {
            return new CompiledExtension(Load(loadContext, File.ReadAllBytes(cached)), null, true);
        }

        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = sources
            .Select(file => CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(file), Encoding.UTF8), parseOptions, Path.GetRelativePath(folder, file)))
            .Append(CSharpSyntaxTree.ParseText(GlobalUsings, parseOptions, "GlobalUsings.g.cs"))
            .ToList();

        var references = FrameworkReferences.Value.Concat(libraries.Select(l => MetadataReference.CreateFromFile(l))).ToList();
        var compilation = CSharpCompilation.Create(
            $"BlogEngine.Extension.{name}",
            trees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release, nullableContextOptions: NullableContextOptions.Enable));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        if (!result.Success)
        {
            var errors = result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Take(10)
                .Select(d => $"{d.Location.GetLineSpan().Path}({d.Location.GetLineSpan().StartLinePosition.Line + 1}): {d.GetMessage()}");
            return new CompiledExtension(null, string.Join(Environment.NewLine, errors), false);
        }

        // Le versioni precedenti della stessa estensione non servono più.
        Directory.CreateDirectory(cacheFolder);
        foreach (var old in Directory.EnumerateFiles(cacheFolder, $"{name}.*.dll"))
        {
            TryDelete(old);
        }

        File.WriteAllBytes(cached, stream.ToArray());
        return new CompiledExtension(Load(loadContext, stream.ToArray()), null, false);
    }

    /// <summary>Caricato dalla memoria: il file in cache non resta bloccato e si può sostituire o eliminare.</summary>
    private static Assembly Load(AssemblyLoadContext context, byte[] image)
    {
        using var stream = new MemoryStream(image);
        return context.LoadFromStream(stream);
    }

    /// <summary>Impronta di sorgenti, librerie e versione del sito: cambia se cambia uno di questi.</summary>
    private static string Hash(string folder, IEnumerable<string> sources, IEnumerable<string> libraries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(typeof(BlogEngine.Extensions.IBlogExtension).Assembly.ManifestModule.ModuleVersionId.ToByteArray());
        hash.AppendData(typeof(ExtensionCompiler).Assembly.ManifestModule.ModuleVersionId.ToByteArray());
        foreach (var file in sources.Concat(libraries))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(folder, file)));
            hash.AppendData(File.ReadAllBytes(file));
        }

        return Convert.ToHexString(hash.GetHashAndReset())[..16].ToLowerInvariant();
    }

    /// <summary>Riferimenti: tutte le librerie già caricabili dal sito (.NET, ASP.NET Core, BlogEngine...).</summary>
    private static IReadOnlyList<MetadataReference> LoadReferences()
    {
        var paths = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var references = new List<MetadataReference>();
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new System.Reflection.PortableExecutable.PEReader(stream);
                if (reader.HasMetadata)
                {
                    references.Add(MetadataReference.CreateFromFile(path));
                }
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
            {
                // File non leggibile o non .NET: non serve come riferimento.
            }
        }

        return references;
    }

    private static bool IsBuildFolder(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        return relative.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In uso da un'istanza precedente del sito: verrà eliminato a un avvio successivo.
        }
    }
}

/// <summary>
/// Contesto di caricamento di un'estensione: le librerie .dll della sua cartella vengono cercate lì,
/// tutto il resto (BlogEngine, .NET, ASP.NET Core) è condiviso con il sito.
/// </summary>
internal sealed class ExtensionLoadContext(string name, IReadOnlyList<string> libraries) : AssemblyLoadContext($"Extension.{name}")
{
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Le librerie del sito hanno la precedenza: i tipi condivisi devono essere gli stessi.
        if (Default.Assemblies.Any(a => AssemblyName.ReferenceMatchesDefinition(a.GetName(), assemblyName)))
        {
            return null;
        }

        var local = libraries.FirstOrDefault(l => string.Equals(Path.GetFileNameWithoutExtension(l), assemblyName.Name, StringComparison.OrdinalIgnoreCase));
        if (local is null)
        {
            return null;
        }

        // Dalla memoria, come l'estensione: la libreria si può sostituire via FTP mentre il sito è acceso.
        using var stream = new MemoryStream(File.ReadAllBytes(local));
        return LoadFromStream(stream);
    }
}

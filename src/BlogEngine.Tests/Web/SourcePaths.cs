using System.Runtime.CompilerServices;

namespace BlogEngine.Tests.Web;

/// <summary>
/// Cartelle dei sorgenti, ricavate dalla posizione di questo file: valgono qualunque sia la cartella
/// di output dei test (anche con dotnet test --artifacts-path).
/// </summary>
internal static class SourcePaths
{
    public static string WebProject { get; } = Path.GetFullPath(Path.Combine(ThisDirectory(), "..", "..", "BlogEngine.Web"));

    public static string WebRoot { get; } = Path.Combine(WebProject, "wwwroot");

    private static string ThisDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}

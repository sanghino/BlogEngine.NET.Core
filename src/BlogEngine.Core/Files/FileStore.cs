using BlogEngine.Core.Configuration;

namespace BlogEngine.Core.Files;

/// <summary>Elemento di una cartella dei file caricati.</summary>
public sealed record FileEntry(string Name, string RelativePath, bool IsDirectory, long Size, DateTime CreatedUtc)
{
    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp", ".svg"];

    public bool IsImage => !IsDirectory && ImageExtensions.Contains(Path.GetExtension(Name), StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// File e immagini caricati dagli autori, in App_Data/files/{id blog}/. I percorsi sono sempre relativi alla
/// cartella del blog ("/2025/03/foto.jpg"): qualunque percorso che ne uscirebbe ("..", percorsi assoluti) viene rifiutato.
/// Sostituisce BlogService.GetDirectory/UploadFile e i provider del file system.
/// </summary>
public sealed class FileStore(BlogEnginePaths paths)
{
    /// <summary>Su Linux i percorsi distinguono le maiuscole: il controllo deve fare altrettanto.</summary>
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public string GetBlogRoot(Guid blogId) => Path.Combine(paths.Files, blogId.ToString("N"));

    /// <summary>Percorso fisico corrispondente al percorso relativo, oppure eccezione se esce dalla cartella del blog.</summary>
    public string Resolve(Guid blogId, string? relativePath)
    {
        var root = Path.GetFullPath(GetBlogRoot(blogId));
        var clean = (relativePath ?? string.Empty).Replace('\\', '/').Trim().TrimStart('/');
        if (clean.Contains('\0') || Path.IsPathRooted(clean) || clean.Contains(':'))
        {
            throw new ArgumentException(Messages.InvalidPath);
        }

        var full = Path.GetFullPath(Path.Combine(root, clean));
        if (!full.Equals(root, PathComparison)
            && !full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new ArgumentException(Messages.InvalidPath);
        }

        return full;
    }

    /// <summary>Percorso relativo normalizzato ("/a/b"), con "/" per la radice.</summary>
    public string ToRelative(Guid blogId, string fullPath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(GetBlogRoot(blogId)), fullPath).Replace('\\', '/');
        return relative == "." ? "/" : "/" + relative;
    }

    public IReadOnlyList<FileEntry> List(Guid blogId, string? relativeDirectory)
    {
        var directory = Resolve(blogId, relativeDirectory);
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var info = new DirectoryInfo(directory);
        return info.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new FileEntry(d.Name, ToRelative(blogId, d.FullName), true, 0, d.CreationTimeUtc))
            .Concat(info.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .Select(f => new FileEntry(f.Name, ToRelative(blogId, f.FullName), false, f.Length, f.CreationTimeUtc)))
            .ToList();
    }

    public void CreateDirectory(Guid blogId, string relativeDirectory) => Directory.CreateDirectory(Resolve(blogId, relativeDirectory));

    /// <summary>Elimina un file o una cartella (con il contenuto). La radice non può essere eliminata.</summary>
    public void Delete(Guid blogId, string relativePath)
    {
        var full = Resolve(blogId, relativePath);
        if (full.Equals(Path.GetFullPath(GetBlogRoot(blogId)), PathComparison))
        {
            throw new ArgumentException(Messages.RootFolderDelete);
        }

        if (Directory.Exists(full))
        {
            Directory.Delete(full, recursive: true);
        }
        else if (File.Exists(full))
        {
            File.Delete(full);
        }
    }

    /// <summary>
    /// Salva un file caricato nella cartella indicata; se esiste già un file con lo stesso nome ne sceglie
    /// uno libero ("foto(1).jpg"). Restituisce il percorso relativo del file salvato.
    /// </summary>
    public async Task<string> SaveAsync(Guid blogId, string relativeDirectory, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        var safeName = SafeFileName(fileName);
        var directory = Resolve(blogId, relativeDirectory);
        Directory.CreateDirectory(directory);

        var target = Path.Combine(directory, safeName);
        for (var i = 1; File.Exists(target); i++)
        {
            target = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(safeName)}({i}){Path.GetExtension(safeName)}");
        }

        await using (var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
        }

        return ToRelative(blogId, target);
    }

    /// <summary>Nome del file senza cartelle e senza caratteri non validi su Windows o Linux.</summary>
    public static string SafeFileName(string fileName)
    {
        var name = Path.GetFileName((fileName ?? string.Empty).Replace('\\', '/').Split('/').Last());
        var invalid = Path.GetInvalidFileNameChars().Concat(['<', '>', ':', '"', '|', '?', '*']).ToHashSet();
        name = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray()).Trim(' ', '.');
        return name.Length == 0 ? $"file-{Guid.NewGuid():N}" : name;
    }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        if (bytes <= 0)
        {
            return "0B";
        }

        var place = Math.Min(units.Length - 1, (int)Math.Floor(Math.Log(bytes, 1024)));
        return $"{Math.Round(bytes / Math.Pow(1024, place), 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}{units[place]}";
    }
}

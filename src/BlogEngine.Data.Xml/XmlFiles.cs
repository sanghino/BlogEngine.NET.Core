using System.Collections.Concurrent;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace BlogEngine.Data.Xml;

/// <summary>
/// Lettura e scrittura dei file del provider XML. Ogni file ha un proprio lock; la scrittura avviene
/// su un file temporaneo poi sostituito a quello vero, così un'interruzione non lascia file troncati.
/// </summary>
internal sealed class XmlFiles
{
    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Indent = true,
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Async = true,
    };

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<XDocument?> LoadAsync(string path, CancellationToken cancellationToken)
    {
        using var _ = await LockAsync(path, cancellationToken).ConfigureAwait(false);
        return await LoadUnlockedAsync(path, cancellationToken).ConfigureAwait(false);
    }

    public async Task SaveAsync(string path, XDocument document, CancellationToken cancellationToken)
    {
        using var _ = await LockAsync(path, cancellationToken).ConfigureAwait(false);
        await SaveUnlockedAsync(path, document, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Legge, modifica e riscrive un file tenendo il lock per tutta l'operazione.</summary>
    public async Task UpdateAsync(string path, string rootName, Action<XElement> change, CancellationToken cancellationToken)
    {
        using var _ = await LockAsync(path, cancellationToken).ConfigureAwait(false);
        var document = await LoadUnlockedAsync(path, cancellationToken).ConfigureAwait(false) ?? new XDocument(new XElement(rootName));
        change(document.Root!);
        await SaveUnlockedAsync(path, document, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        using var _ = await LockAsync(path, cancellationToken).ConfigureAwait(false);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false) : null;
    }

    public async Task WriteTextAsync(string path, string text, CancellationToken cancellationToken)
    {
        using var _ = await LockAsync(path, cancellationToken).ConfigureAwait(false);
        var temp = PrepareTemp(path);
        await File.WriteAllTextAsync(temp, text, WriterSettings.Encoding, cancellationToken).ConfigureAwait(false);
        await RetryAsync(() => File.Move(temp, path, overwrite: true), cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        using var _ = await LockAsync(path, cancellationToken).ConfigureAwait(false);
        await RetryAsync(() => File.Delete(path), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Ripete un'operazione sui file che fallisce per un blocco temporaneo: antivirus e indicizzatori
    /// possono tenere aperto per un istante un file appena scritto, sia su Windows sia sugli hosting condivisi.
    /// </summary>
    private static async Task RetryAsync(Action operation, CancellationToken cancellationToken)
    {
        const int attempts = 5;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                operation();
                return;
            }
            catch (Exception ex) when (attempt < attempts && ex is IOException or UnauthorizedAccessException)
            {
                await Task.Delay(50 * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<XDocument?> LoadUnlockedAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        return await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SaveUnlockedAsync(string path, XDocument document, CancellationToken cancellationToken)
    {
        var temp = PrepareTemp(path);
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
        await using (var writer = XmlWriter.Create(stream, WriterSettings))
        {
            await document.SaveAsync(writer, cancellationToken).ConfigureAwait(false);
        }

        await RetryAsync(() => File.Move(temp, path, overwrite: true), cancellationToken).ConfigureAwait(false);
    }

    private static string PrepareTemp(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path + ".tmp";
    }

    private async Task<IDisposable> LockAsync(string path, CancellationToken cancellationToken)
    {
        var gate = _locks.GetOrAdd(Path.GetFullPath(path), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}

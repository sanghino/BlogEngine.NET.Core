using System.Globalization;
using System.Text;
using BlogEngine.Core.Configuration;

namespace BlogEngine.Web.Hosting;

/// <summary>
/// Log del sito in App_Data/logs/blogengine.log, mostrato nell'amministrazione (sostituisce logger.txt).
/// Ogni voce è preceduta dalla riga separatrice usata dalla versione originale. Oltre 1 MB il file
/// viene rinominato in blogengine.old.log, così lo spazio occupato resta limitato.
/// </summary>
public sealed class FileLog(BlogEnginePaths paths)
{
    public const string Separator = "*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*-*";
    private const long MaxSize = 1024 * 1024;

    private readonly object _sync = new();

    public string FilePath => Path.Combine(paths.Logs, "blogengine.log");

    public void Write(string message)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(paths.Logs);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxSize)
                {
                    File.Move(FilePath, Path.Combine(paths.Logs, "blogengine.old.log"), overwrite: true);
                }

                File.AppendAllText(FilePath, $"{Separator}{Environment.NewLine}{message}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (IOException)
            {
                // Il log non deve mai far fallire la richiesta.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>Contenuto completo del log (vuoto se non esiste).</summary>
    public string ReadAll()
    {
        lock (_sync)
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath, Encoding.UTF8) : string.Empty;
        }
    }

    /// <summary>Voci del log, con le righe di ciascuna unite da "&lt;br/&gt;" come nella versione originale.</summary>
    public IReadOnlyList<string> ReadEntries() =>
        ReadAll()
            .Split(Separator, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim())
            .Where(e => e.Length > 0)
            .Select(e => string.Join("<br/>", e.Split('\n').Select(l => System.Net.WebUtility.HtmlEncode(l.TrimEnd('\r')))))
            .ToList();

    public void Clear()
    {
        lock (_sync)
        {
            if (File.Exists(FilePath))
            {
                File.WriteAllText(FilePath, string.Empty);
            }
        }
    }
}

/// <summary>
/// Inoltra a <see cref="FileLog"/> gli avvisi del blog e gli errori di qualunque componente.
/// </summary>
public sealed class FileLoggerProvider(FileLog log) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new FileLogger(log, categoryName);

    public void Dispose()
    {
    }

    private sealed class FileLogger(FileLog log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) =>
            logLevel >= (category.StartsWith("BlogEngine", StringComparison.Ordinal) ? LogLevel.Warning : LogLevel.Error);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var text = new StringBuilder()
                .Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture))
                .Append(" [").Append(logLevel).Append("] ").AppendLine(category)
                .Append(formatter(state, exception));
            if (exception is not null)
            {
                text.AppendLine().Append(exception);
            }

            log.Write(text.ToString());
        }
    }
}

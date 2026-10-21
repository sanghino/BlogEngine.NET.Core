namespace BlogEngine.Core;

/// <summary>
/// Dati non validi in un salvataggio. <see cref="Errors"/> associa il nome del campo al messaggio.
/// </summary>
public sealed class BlogValidationException(IReadOnlyDictionary<string, string> errors)
    : Exception(string.Join(Environment.NewLine, errors.Values))
{
    public IReadOnlyDictionary<string, string> Errors { get; } = errors;

    /// <summary>Verifica le regole e lancia l'eccezione se almeno una non è rispettata.</summary>
    public static void ThrowIfInvalid(params (string Field, bool IsInvalid, string Message)[] rules)
    {
        var errors = rules.Where(r => r.IsInvalid).ToDictionary(r => r.Field, r => r.Message, StringComparer.OrdinalIgnoreCase);
        if (errors.Count > 0)
        {
            throw new BlogValidationException(errors);
        }
    }
}

using BlogEngine.Settings;

namespace BlogEngine.Services;

/// <summary>Impostazioni dei blog.</summary>
public interface ISettingsService
{
    /// <summary>
    /// Impostazioni del blog indicato o, se null, del blog corrente. Restituisce una copia:
    /// per modificarle si cambia la copia e si chiama <see cref="SaveAsync"/>.
    /// </summary>
    Task<BlogSettings> GetAsync(Guid? blogId = null, CancellationToken cancellationToken = default);

    /// <summary>Salva le impostazioni del blog corrente o di quello indicato.</summary>
    Task SaveAsync(BlogSettings settings, Guid? blogId = null, CancellationToken cancellationToken = default);
}

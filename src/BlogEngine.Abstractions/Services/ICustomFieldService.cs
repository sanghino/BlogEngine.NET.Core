using BlogEngine.Models;

namespace BlogEngine.Services;

/// <summary>Campi personalizzati del blog corrente.</summary>
public interface ICustomFieldService
{
    /// <summary>Campi di un oggetto (es. tipo <see cref="CustomField.PostType"/> e id del post).</summary>
    Task<IReadOnlyList<CustomField>> GetFieldsAsync(string customType, string objectId, CancellationToken cancellationToken = default);

    /// <summary>Inserisce o aggiorna il campo identificato da tipo, oggetto e chiave.</summary>
    Task SaveAsync(CustomField field, CancellationToken cancellationToken = default);

    Task DeleteAsync(CustomField field, CancellationToken cancellationToken = default);

    /// <summary>Elimina tutti i campi di un oggetto.</summary>
    Task ClearAsync(string customType, string objectId, CancellationToken cancellationToken = default);
}

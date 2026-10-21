using BlogEngine.Models;

namespace BlogEngine.Services;

/// <summary>Lettura e modifica delle categorie del blog corrente.</summary>
public interface ICategoryService
{
    /// <summary>Categorie ordinate per titolo completo; in un blog aggregatore include quelle di tutti i blog.</summary>
    Task<IReadOnlyList<Category>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Cerca la categoria in base al titolo nella forma URL (slug).</summary>
    Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Titolo comprensivo delle categorie padre, es. "Sviluppo - .NET".</summary>
    Task<string> GetCompleteTitleAsync(Category category, CancellationToken cancellationToken = default);

    Task SaveAsync(Category category, CancellationToken cancellationToken = default);

    /// <summary>Elimina la categoria, togliendola dai post; le sottocategorie diventano di primo livello.</summary>
    Task DeleteAsync(Category category, CancellationToken cancellationToken = default);
}

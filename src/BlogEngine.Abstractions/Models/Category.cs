namespace BlogEngine.Models;

/// <summary>
/// Categoria dei post. Le categorie possono essere annidate tramite <see cref="Parent"/>.
/// </summary>
public sealed class Category : BlogEntity
{
    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Guid? Parent { get; set; }

    public override string ToString() => Title;
}

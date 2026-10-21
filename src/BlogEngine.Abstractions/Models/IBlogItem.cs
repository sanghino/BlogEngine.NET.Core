namespace BlogEngine.Models;

/// <summary>Elemento identificato da un Guid e appartenente a un blog.</summary>
public interface IBlogItem
{
    Guid Id { get; }

    Guid BlogId { get; }
}

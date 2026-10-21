namespace BlogEngine.Models;

/// <summary>
/// Nodo dell'albero dei commenti di un post: il commento con le sue risposte.
/// L'albero viene costruito a ogni richiesta e non modifica i commenti memorizzati.
/// </summary>
public sealed class CommentNode(Comment comment)
{
    public Comment Comment { get; } = comment;

    public List<CommentNode> Replies { get; } = [];
}

namespace BlogEngine.Core.Data;

/// <summary>
/// Regola di moderazione configurata dall'amministratore, es. "blocca i commenti il cui email contiene X".
/// </summary>
public sealed class CommentFilterRule
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public CommentFilterAction Action { get; set; }

    public CommentFilterSubject Subject { get; set; }

    public CommentFilterOperator Operator { get; set; }

    /// <summary>Testo da cercare; il confronto non distingue le maiuscole.</summary>
    public string Filter { get; set; } = string.Empty;
}

public enum CommentFilterAction
{
    /// <summary>Approva il commento.</summary>
    Allow,

    /// <summary>Segna il commento come spam.</summary>
    Block,

    /// <summary>Scarta il commento senza salvarlo.</summary>
    Delete,
}

public enum CommentFilterSubject
{
    IP,
    Author,
    Website,
    Email,
    Comment,
}

public enum CommentFilterOperator
{
    Equals,
    Contains,
}

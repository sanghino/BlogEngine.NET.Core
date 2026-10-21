using BlogEngine.Models;
using BlogEngine.Settings;

namespace BlogEngine.Events;

/// <summary>Tipo di salvataggio.</summary>
public enum SaveAction
{
    None,
    Insert,
    Update,
    Delete,
}

/// <summary>Dove viene mostrato un contenuto.</summary>
public enum ServingLocation
{
    None,
    SinglePost,
    PostList,
    SinglePage,
    Feed,
    Email,
    Other,
}

/// <summary>Criterio con cui è stato selezionato il contenuto mostrato.</summary>
public enum ServingContentBy
{
    Unspecified,
    AllContent,
    Tag,
    Category,
    Author,
    DateRange,
    Apml,
}

/// <summary>
/// Il contenuto di un post, una pagina o un commento sta per essere mostrato. I gestori possono
/// modificare <see cref="Body"/> (es. BBCode, emoticon, "leggi tutto") o nasconderlo con Cancel.
/// Sostituisce gli eventi Post.Serving, Page.Serving e Comment.Serving.
/// </summary>
public sealed class ServingEvent<TItem> : CancelableBlogEvent
    where TItem : IPublishable
{
    public required TItem Item { get; init; }

    public required string Body { get; set; }

    public required ServingLocation Location { get; init; }

    public ServingContentBy ContentBy { get; init; }
}

/// <summary>
/// Un'entità sta per essere salvata o eliminata; i gestori possono ancora modificarla.
/// Sostituisce l'evento Saving (che, come allora, non è annullabile).
/// </summary>
public sealed class SavingEvent<TEntity> : BlogEvent
    where TEntity : class
{
    public required TEntity Entity { get; init; }

    public required SaveAction Action { get; init; }
}

/// <summary>Un'entità è stata salvata o eliminata. Sostituisce l'evento Saved.</summary>
public sealed class SavedEvent<TEntity> : BlogEvent
    where TEntity : class
{
    public required TEntity Entity { get; init; }

    public required SaveAction Action { get; init; }
}

/// <summary>Un post sta per diventare pubblico; con Cancel resta in bozza.</summary>
public sealed class PostPublishingEvent : CancelableBlogEvent
{
    public required Post Post { get; init; }
}

/// <summary>Un post è diventato pubblico (es. per inviare ping e newsletter).</summary>
public sealed class PostPublishedEvent : BlogEvent
{
    public required Post Post { get; init; }
}

/// <summary>Un visitatore ha votato un post.</summary>
public sealed class PostRatedEvent : BlogEvent
{
    public required Post Post { get; init; }

    public required int Rating { get; init; }
}

/// <summary>Operazione su un commento.</summary>
public enum CommentAction
{
    Add,
    Update,
    Approve,
    Disapprove,
    Remove,
    Restore,
    Purge,
}

/// <summary>
/// Un'operazione su un commento sta per essere eseguita; con Cancel viene annullata.
/// Con <see cref="CommentAction.Add"/> è il punto in cui intervengono i filtri antispam,
/// che possono anche modificare <see cref="Comment"/> (es. impostare IsSpam o IsApproved).
/// </summary>
public sealed class CommentChangingEvent : CancelableBlogEvent
{
    public required Post Post { get; init; }

    public required Comment Comment { get; init; }

    public required CommentAction Action { get; init; }
}

/// <summary>Un'operazione su un commento è stata eseguita (es. per inviare notifiche).</summary>
public sealed class CommentChangedEvent : BlogEvent
{
    public required Post Post { get; init; }

    public required Comment Comment { get; init; }

    public required CommentAction Action { get; init; }
}

/// <summary>Le impostazioni del blog sono state salvate.</summary>
public sealed class SettingsChangedEvent : BlogEvent
{
    public required BlogSettings Settings { get; init; }
}

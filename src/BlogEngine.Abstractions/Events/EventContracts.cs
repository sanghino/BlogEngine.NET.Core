namespace BlogEngine.Events;

/// <summary>Base di tutti gli eventi del blog.</summary>
public abstract class BlogEvent
{
    /// <summary>Blog in cui si è verificato l'evento.</summary>
    public required Guid BlogId { get; init; }
}

/// <summary>
/// Evento che precede un'operazione: un gestore può annullarla impostando <see cref="Cancel"/>.
/// Tutti i gestori vengono comunque eseguiti.
/// </summary>
public abstract class CancelableBlogEvent : BlogEvent
{
    public bool Cancel { get; set; }
}

/// <summary>
/// Gestore di un tipo di evento. Le estensioni implementano questa interfaccia e vengono
/// registrate nel contenitore dei servizi.
/// </summary>
public interface IEventHandler<in TEvent>
    where TEvent : BlogEvent
{
    /// <summary>Ordine di esecuzione: i valori più bassi vengono eseguiti prima.</summary>
    int Order => 0;

    Task HandleAsync(TEvent blogEvent, CancellationToken cancellationToken);
}

/// <summary>Pubblica un evento a tutti i gestori registrati.</summary>
public interface IEventPublisher
{
    /// <summary>
    /// Esegue i gestori in ordine. Un gestore che genera un'eccezione viene registrato nel log
    /// e non interrompe gli altri, così un'estensione difettosa non blocca il sito.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent blogEvent, CancellationToken cancellationToken = default)
        where TEvent : BlogEvent;
}

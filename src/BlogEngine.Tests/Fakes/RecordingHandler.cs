using System.Collections.Concurrent;
using BlogEngine.Events;

namespace BlogEngine.Tests.Fakes;

/// <summary>Gestore che registra gli eventi ricevuti ed esegue un'azione opzionale.</summary>
public sealed class RecordingHandler<TEvent>(Action<TEvent>? onEvent = null, int order = 0) : IEventHandler<TEvent>
    where TEvent : BlogEvent
{
    public ConcurrentQueue<TEvent> Events { get; } = new();

    public IReadOnlyList<TEvent> Received => Events.ToList();

    public int Order => order;

    public Task HandleAsync(TEvent blogEvent, CancellationToken cancellationToken)
    {
        Events.Enqueue(blogEvent);
        onEvent?.Invoke(blogEvent);
        return Task.CompletedTask;
    }
}

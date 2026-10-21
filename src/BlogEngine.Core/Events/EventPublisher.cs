using BlogEngine.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlogEngine.Core.Events;

/// <summary>
/// Distribuisce gli eventi ai gestori registrati nel contenitore dei servizi, in ordine di <c>Order</c>.
/// </summary>
public sealed class EventPublisher(IServiceProvider services, ILogger<EventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync<TEvent>(TEvent blogEvent, CancellationToken cancellationToken = default)
        where TEvent : BlogEvent
    {
        ArgumentNullException.ThrowIfNull(blogEvent);

        foreach (var handler in services.GetServices<IEventHandler<TEvent>>().OrderBy(h => h.Order))
        {
            try
            {
                await handler.HandleAsync(blogEvent, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Il gestore {Handler} dell'evento {Event} ha generato un errore.",
                    handler.GetType().FullName, typeof(TEvent).Name);
            }
        }
    }
}

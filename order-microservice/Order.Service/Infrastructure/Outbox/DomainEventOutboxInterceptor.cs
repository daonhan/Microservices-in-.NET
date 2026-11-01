using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Order.Service.Domain.Events;

namespace Order.Service.Infrastructure.Outbox;

internal sealed class DomainEventOutboxInterceptor
{
    private readonly Dictionary<Type, IIntegrationMap> _maps;
    private readonly IOutboxStore _outboxStore;

    public DomainEventOutboxInterceptor(IEnumerable<IIntegrationMap> maps, IOutboxStore outboxStore)
    {
        _maps = maps.ToDictionary(m => m.DomainEventType);
        _outboxStore = outboxStore;
    }

    public Task PublishAsync(IEnumerable<IDomainEvent> domainEvents) => PublishMappedAsync(Map(domainEvents));

    public IReadOnlyList<Event> Map(IEnumerable<IDomainEvent> domainEvents)
    {
        var events = new List<Event>();
        foreach (var domainEvent in domainEvents)
        {
            if (!_maps.TryGetValue(domainEvent.GetType(), out var map))
            {
                throw new InvalidOperationException(
                    $"No integration-event translation registered for domain event {domainEvent.GetType().Name}");
            }

            events.Add(map.Map(domainEvent));
        }

        return events;
    }

    public async Task PublishMappedAsync(IReadOnlyList<Event> events)
    {
        foreach (var @event in events)
        {
            await _outboxStore.AddOutboxEvent(@event);
        }
    }
}

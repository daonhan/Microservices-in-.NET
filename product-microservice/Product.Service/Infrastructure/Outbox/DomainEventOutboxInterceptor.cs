using ECommerce.Shared.Infrastructure.Outbox;
using Product.Service.Domain.Events;

namespace Product.Service.Infrastructure.Outbox;

internal sealed class DomainEventOutboxInterceptor
{
    private readonly Dictionary<Type, IIntegrationMap> _maps;
    private readonly IOutboxStore _outboxStore;

    public DomainEventOutboxInterceptor(IEnumerable<IIntegrationMap> maps, IOutboxStore outboxStore)
    {
        _maps = maps.ToDictionary(m => m.DomainEventType);
        _outboxStore = outboxStore;
    }

    public async Task PublishAsync(IEnumerable<IDomainEvent> domainEvents)
    {
        foreach (var domainEvent in domainEvents)
        {
            if (!_maps.TryGetValue(domainEvent.GetType(), out var map))
            {
                throw new InvalidOperationException(
                    $"No integration-event translation registered for domain event {domainEvent.GetType().Name}");
            }

            await _outboxStore.AddOutboxEvent(map.Map(domainEvent));
        }
    }
}

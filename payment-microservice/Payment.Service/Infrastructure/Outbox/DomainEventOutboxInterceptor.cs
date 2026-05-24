using ECommerce.Shared.Infrastructure.EventBus;
using Payment.Service.Domain;

namespace Payment.Service.Infrastructure.Outbox;

internal sealed class DomainEventOutboxInterceptor
{
    private readonly Dictionary<Type, IIntegrationMap> _maps;
    private readonly MessageCorrelationContext _correlation;

    public DomainEventOutboxInterceptor(
        IEnumerable<IIntegrationMap> maps,
        MessageCorrelationContext correlation)
    {
        _maps = maps.ToDictionary(m => m.DomainEventType);
        _correlation = correlation;
    }

    public IReadOnlyList<Event> Translate(IEnumerable<IDomainEvent> domainEvents)
    {
        var result = new List<Event>();
        foreach (var domainEvent in domainEvents)
        {
            if (!_maps.TryGetValue(domainEvent.GetType(), out var map))
            {
                throw new InvalidOperationException(
                    $"No integration-event translation registered for domain event {domainEvent.GetType().Name}");
            }

            var integrationEvent = map.Map(domainEvent);
            integrationEvent.CorrelationId = _correlation.CorrelationId;
            integrationEvent.CausationId = _correlation.CausationId;
            integrationEvent.SagaId = _correlation.SagaId;
            result.Add(integrationEvent);
        }

        return result;
    }
}

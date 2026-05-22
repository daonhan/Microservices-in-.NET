using ECommerce.Shared.Infrastructure.EventBus;
using Product.Service.Domain.Events;

namespace Product.Service.Infrastructure.Outbox;

internal interface IIntegrationMap
{
    Type DomainEventType { get; }
    Event Map(IDomainEvent domainEvent);
}

internal interface IIntegrationMap<TDomainEvent, TIntegrationEvent> : IIntegrationMap
    where TDomainEvent : IDomainEvent
    where TIntegrationEvent : Event
{
    TIntegrationEvent Map(TDomainEvent domainEvent);
}

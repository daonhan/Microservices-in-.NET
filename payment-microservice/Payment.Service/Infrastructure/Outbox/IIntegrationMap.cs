using ECommerce.Shared.Infrastructure.EventBus;
using Payment.Service.Domain;

namespace Payment.Service.Infrastructure.Outbox;

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

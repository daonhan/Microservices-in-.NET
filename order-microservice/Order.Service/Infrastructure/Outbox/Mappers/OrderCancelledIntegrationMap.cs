using ECommerce.Shared.Infrastructure.EventBus;
using Order.Service.Contracts.Integration;
using Order.Service.Domain.Events;

namespace Order.Service.Infrastructure.Outbox.Mappers;

internal sealed class OrderCancelledIntegrationMap : IIntegrationMap<OrderCancelledDomainEvent, OrderCancelledEvent>
{
    public Type DomainEventType => typeof(OrderCancelledDomainEvent);

    public OrderCancelledEvent Map(OrderCancelledDomainEvent domainEvent) =>
        new(domainEvent.OrderId, domainEvent.CustomerId);

    Event IIntegrationMap.Map(IDomainEvent domainEvent) => Map((OrderCancelledDomainEvent)domainEvent);
}

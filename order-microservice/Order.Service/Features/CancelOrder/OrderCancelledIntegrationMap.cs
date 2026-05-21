using ECommerce.Shared.Infrastructure.EventBus;
using Order.Service.Contracts.Integration;
using Order.Service.Domain.Events;
using Order.Service.Infrastructure.Outbox;

namespace Order.Service.Features.CancelOrder;

internal sealed class OrderCancelledIntegrationMap : IIntegrationMap<OrderCancelledDomainEvent, OrderCancelledEvent>
{
    public Type DomainEventType => typeof(OrderCancelledDomainEvent);

    public OrderCancelledEvent Map(OrderCancelledDomainEvent domainEvent) =>
        new(domainEvent.OrderId, domainEvent.CustomerId);

    Event IIntegrationMap.Map(IDomainEvent domainEvent) => Map((OrderCancelledDomainEvent)domainEvent);
}

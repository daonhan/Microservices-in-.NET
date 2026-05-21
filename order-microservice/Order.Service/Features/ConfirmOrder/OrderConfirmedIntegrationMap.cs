using ECommerce.Shared.Infrastructure.EventBus;
using Order.Service.Contracts.Integration;
using Order.Service.Domain.Events;
using Order.Service.Infrastructure.Outbox;

namespace Order.Service.Features.ConfirmOrder;

internal sealed class OrderConfirmedIntegrationMap : IIntegrationMap<OrderConfirmedDomainEvent, OrderConfirmedEvent>
{
    public Type DomainEventType => typeof(OrderConfirmedDomainEvent);

    public OrderConfirmedEvent Map(OrderConfirmedDomainEvent domainEvent) =>
        new(domainEvent.OrderId, domainEvent.CustomerId);

    Event IIntegrationMap.Map(IDomainEvent domainEvent) => Map((OrderConfirmedDomainEvent)domainEvent);
}

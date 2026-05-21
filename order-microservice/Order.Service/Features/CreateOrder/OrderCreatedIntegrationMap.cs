using ECommerce.Shared.Infrastructure.EventBus;
using Order.Service.Contracts.Integration;
using Order.Service.Domain.Events;
using Order.Service.Infrastructure.Outbox;

namespace Order.Service.Features.CreateOrder;

internal sealed class OrderCreatedIntegrationMap : IIntegrationMap<OrderCreatedDomainEvent, OrderCreatedEvent>
{
    public Type DomainEventType => typeof(OrderCreatedDomainEvent);

    public OrderCreatedEvent Map(OrderCreatedDomainEvent domainEvent) => new(
        domainEvent.OrderId,
        domainEvent.CustomerId,
        domainEvent.Items.Select(i => new OrderItem(i.ProductId, i.Quantity, i.UnitPrice)).ToList(),
        domainEvent.Currency);

    Event IIntegrationMap.Map(IDomainEvent domainEvent) => Map((OrderCreatedDomainEvent)domainEvent);
}

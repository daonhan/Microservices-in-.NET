using ECommerce.Shared.Infrastructure.EventBus;
using Product.Service.Contracts.Integration;
using Product.Service.Domain.Events;

namespace Product.Service.Infrastructure.Outbox.Mappers;

internal sealed class ProductCreatedIntegrationMap : IIntegrationMap<ProductCreatedDomainEvent, ProductCreatedEvent>
{
    public Type DomainEventType => typeof(ProductCreatedDomainEvent);

    public ProductCreatedEvent Map(ProductCreatedDomainEvent domainEvent) => new(
        domainEvent.Product.Id,
        domainEvent.Product.Name,
        domainEvent.Product.Price);

    Event IIntegrationMap.Map(IDomainEvent domainEvent) => Map((ProductCreatedDomainEvent)domainEvent);
}

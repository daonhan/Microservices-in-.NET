using ECommerce.Shared.Infrastructure.EventBus;
using Product.Service.Contracts.Integration;
using Product.Service.Domain.Events;
using Product.Service.Infrastructure.Outbox;

namespace Product.Service.Features.UpdateProduct;

internal sealed class ProductPriceUpdatedIntegrationMap : IIntegrationMap<ProductPriceChangedDomainEvent, ProductPriceUpdatedEvent>
{
    public Type DomainEventType => typeof(ProductPriceChangedDomainEvent);

    public ProductPriceUpdatedEvent Map(ProductPriceChangedDomainEvent domainEvent) => new(
        domainEvent.ProductId,
        domainEvent.NewPrice);

    Event IIntegrationMap.Map(IDomainEvent domainEvent) => Map((ProductPriceChangedDomainEvent)domainEvent);
}

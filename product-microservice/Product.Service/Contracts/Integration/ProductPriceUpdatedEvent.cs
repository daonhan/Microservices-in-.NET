using ECommerce.Shared.Infrastructure.EventBus;

namespace Product.Service.Contracts.Integration;

public record ProductPriceUpdatedEvent(int ProductId, decimal NewPrice) : Event;

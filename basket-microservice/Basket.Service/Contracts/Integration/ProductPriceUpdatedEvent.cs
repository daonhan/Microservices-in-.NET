using ECommerce.Shared.Infrastructure.EventBus;

namespace Basket.Service.Contracts.Integration;

public record ProductPriceUpdatedEvent(int ProductId, decimal NewPrice) : Event;

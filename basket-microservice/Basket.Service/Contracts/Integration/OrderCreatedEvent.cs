using ECommerce.Shared.Infrastructure.EventBus;

namespace Basket.Service.Contracts.Integration;

public record OrderCreatedEvent(string CustomerId) : Event;

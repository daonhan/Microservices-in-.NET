using ECommerce.Shared.Infrastructure.EventBus;

namespace Order.Service.IntegrationEvents.Events;

public record StockReservedEvent(
    Guid OrderId,
    decimal Amount = 0m,
    string Currency = "USD") : Event;

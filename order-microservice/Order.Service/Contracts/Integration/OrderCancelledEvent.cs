using ECommerce.Shared.Infrastructure.EventBus;

namespace Order.Service.Contracts.Integration;

public record OrderCancelledEvent(Guid OrderId, string CustomerId) : Event;

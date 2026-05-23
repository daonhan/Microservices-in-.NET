using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.Contracts.Integration;

public record OrderConfirmedEvent(Guid OrderId, string CustomerId) : Event;

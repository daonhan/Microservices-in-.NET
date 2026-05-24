using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Contracts.Integration.InboundEvents;

public record OrderConfirmedEvent(Guid OrderId, string CustomerId) : Event;

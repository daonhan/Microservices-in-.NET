using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Contracts.Integration.InboundEvents;

public record PaymentFailedEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    string Reason) : Event;

using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Contracts.Integration.InboundEvents;

public record PaymentRefundedEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount) : Event;

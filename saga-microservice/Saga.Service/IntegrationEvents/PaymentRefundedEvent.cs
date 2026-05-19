using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.IntegrationEvents;

public record PaymentRefundedEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount) : Event;

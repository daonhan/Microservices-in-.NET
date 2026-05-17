using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.IntegrationEvents;

public record PaymentCapturedEvent(
    Guid PaymentId,
    Guid OrderId,
    decimal Amount) : Event;

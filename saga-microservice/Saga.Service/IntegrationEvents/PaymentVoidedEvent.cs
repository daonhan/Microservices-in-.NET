using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.IntegrationEvents;

public record PaymentVoidedEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    string Reason) : Event;

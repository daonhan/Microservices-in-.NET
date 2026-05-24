using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Contracts.Integration.InboundEvents;

public record PaymentVoidedEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    string Reason) : Event;

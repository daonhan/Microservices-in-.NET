using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Contracts.Integration.InboundEvents;

public record ShipmentCancelledEvent(
    Guid ShipmentId,
    Guid OrderId,
    string CustomerId,
    DateTime OccurredAt,
    string? Reason) : Event;

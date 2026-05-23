using ECommerce.Shared.Infrastructure.EventBus;

namespace Shipping.Service.Contracts.Integration;

public record ShipmentStatusChangedEvent(
    Guid ShipmentId,
    Guid OrderId,
    int? FromStatus,
    int ToStatus,
    DateTime OccurredAt) : Event;

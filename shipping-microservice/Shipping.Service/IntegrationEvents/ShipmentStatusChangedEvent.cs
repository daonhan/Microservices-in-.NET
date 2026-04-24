using ECommerce.Shared.Infrastructure.EventBus;
using Shipping.Service.Models;

namespace Shipping.Service.IntegrationEvents;

public record ShipmentStatusChangedEvent(
    Guid ShipmentId,
    Guid OrderId,
    ShipmentStatus? FromStatus,
    ShipmentStatus ToStatus,
    DateTime OccurredAt) : Event;

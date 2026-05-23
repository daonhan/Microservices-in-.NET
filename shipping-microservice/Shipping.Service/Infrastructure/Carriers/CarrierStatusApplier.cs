using ECommerce.Shared.Infrastructure.EventBus;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Domain;
using Shipping.Service.Domain.Abstractions;
using Shipping.Service.Infrastructure.Observability;

namespace Shipping.Service.Infrastructure.Carriers;

/// <summary>
/// Applies a <see cref="CarrierStatus"/> update to a <see cref="Shipment"/>
/// through the aggregate's legal transitions and returns the corresponding
/// milestone + <see cref="ShipmentStatusChangedEvent"/> instances.
/// The caller is responsible for saving changes and enqueueing returned events
/// through the Outbox unit-of-work.
/// </summary>
internal static class CarrierStatusApplier
{
    public static Task<IReadOnlyList<Event>> ApplyAsync(
        Shipment shipment,
        CarrierStatus status,
        ShipmentStatusSource source,
        DateTime occurredAt,
        ShippingMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(status);

        if (shipment.IsTerminal)
        {
            // Graceful no-op for late-arriving webhooks / polls.
            return Task.FromResult<IReadOnlyList<Event>>(Array.Empty<Event>());
        }

        var fromStatus = shipment.Status;
        var createdAt = shipment.CreatedAt;
        var events = new List<Event>();

        switch (status.Code)
        {
            case CarrierStatusCode.InTransit:
                if (!shipment.TryMarkInTransit(occurredAt, source))
                {
                    return Task.FromResult<IReadOnlyList<Event>>(Array.Empty<Event>());
                }

                break;

            case CarrierStatusCode.Delivered:
                if (!shipment.TryDeliver(occurredAt, source))
                {
                    return Task.FromResult<IReadOnlyList<Event>>(Array.Empty<Event>());
                }

                events.Add(new ShipmentDeliveredEvent(
                    ShipmentId: shipment.Id,
                    OrderId: shipment.OrderId,
                    CustomerId: shipment.CustomerId,
                    CarrierKey: shipment.CarrierKey,
                    TrackingNumber: shipment.TrackingNumber,
                    OccurredAt: occurredAt));
                break;

            case CarrierStatusCode.Failed:
                var reason = string.IsNullOrWhiteSpace(status.Detail)
                    ? "Reported failed by carrier"
                    : status.Detail!;
                if (!shipment.TryFail(reason, occurredAt, source))
                {
                    return Task.FromResult<IReadOnlyList<Event>>(Array.Empty<Event>());
                }

                events.Add(new ShipmentFailedEvent(
                    ShipmentId: shipment.Id,
                    OrderId: shipment.OrderId,
                    CustomerId: shipment.CustomerId,
                    CarrierKey: shipment.CarrierKey,
                    TrackingNumber: shipment.TrackingNumber,
                    Reason: reason,
                    OccurredAt: occurredAt));
                break;

            case CarrierStatusCode.Accepted:
            case CarrierStatusCode.Unknown:
            default:
                return Task.FromResult<IReadOnlyList<Event>>(Array.Empty<Event>());
        }

        events.Add(new ShipmentStatusChangedEvent(
            shipment.Id,
            shipment.OrderId,
            FromStatus: (int?)fromStatus,
            ToStatus: (int)shipment.Status,
            OccurredAt: occurredAt));

        if (metrics is not null)
        {
            metrics.RecordStatusChange(shipment.Status);
            if (shipment.Status == ShipmentStatus.Delivered)
            {
                metrics.RecordTimeToDelivery(createdAt, occurredAt);
            }
        }

        return Task.FromResult<IReadOnlyList<Event>>(events);
    }
}

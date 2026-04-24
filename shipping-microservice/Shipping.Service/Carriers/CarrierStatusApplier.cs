using ECommerce.Shared.Infrastructure.Outbox;
using Shipping.Service.IntegrationEvents;
using Shipping.Service.Models;
using Shipping.Service.Observability;

namespace Shipping.Service.Carriers;

/// <summary>
/// Applies a <see cref="CarrierStatus"/> update to a <see cref="Shipment"/>
/// through the aggregate's legal transitions and emits the corresponding
/// milestone + <see cref="ShipmentStatusChangedEvent"/> through the outbox.
/// The caller is responsible for the surrounding transaction and
/// <c>SaveChangesAsync</c>.
/// </summary>
internal static class CarrierStatusApplier
{
    public static async Task<bool> ApplyAsync(
        Shipment shipment,
        CarrierStatus status,
        ShipmentStatusSource source,
        DateTime occurredAt,
        IOutboxStore outboxStore,
        ShippingMetrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(shipment);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(outboxStore);

        if (shipment.IsTerminal)
        {
            // Graceful no-op for late-arriving webhooks / polls.
            return false;
        }

        var fromStatus = shipment.Status;
        var createdAt = shipment.CreatedAt;

        switch (status.Code)
        {
            case CarrierStatusCode.InTransit:
                if (!shipment.TryMarkInTransit(occurredAt, source))
                {
                    return false;
                }

                break;

            case CarrierStatusCode.Delivered:
                if (!shipment.TryDeliver(occurredAt, source))
                {
                    return false;
                }

                await outboxStore.AddOutboxEvent(new ShipmentDeliveredEvent(
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
                    return false;
                }

                await outboxStore.AddOutboxEvent(new ShipmentFailedEvent(
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
                return false;
        }

        await outboxStore.AddOutboxEvent(new ShipmentStatusChangedEvent(
            shipment.Id,
            shipment.OrderId,
            FromStatus: fromStatus,
            ToStatus: shipment.Status,
            OccurredAt: occurredAt));

        if (metrics is not null)
        {
            metrics.RecordStatusChange(shipment.Status);
            if (shipment.Status == ShipmentStatus.Delivered)
            {
                metrics.RecordTimeToDelivery(createdAt, occurredAt);
            }
        }

        return true;
    }
}
